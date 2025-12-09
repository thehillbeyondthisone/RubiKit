using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.GameData;

namespace Ascension.Features
{
    /// <summary>
    /// Stealth proximity watcher:
    /// - Detects nearby players (excludes you + whitelist).
    /// - Announces threats even when not stacking (HUD mode).
    /// - Auto-pauses/resumes via provided delegates when stacking.
    /// </summary>
    public sealed class ProximityGuard
    {
        // Small POCO instead of tuples (some compilers drop tuple element names)
        public struct ThreatInfo
        {
            public string Name;
            public float DistanceM;
            public ThreatInfo(string name, float distanceM)
            {
                Name = name;
                DistanceM = distanceM;
            }
        }

        // Behavior
        public bool Enabled { get; set; } = true;
        public float TriggerRangeM { get; set; } = 40f;
        public float ClearSeconds { get; set; } = 8f;
        public float ScanHz { get; set; } = 5f;

        // Heads-up announcements (work even if you’re not stacking)
        public bool AnnounceThreats { get; set; } = true;
        public float AnnounceIntervalSec { get; set; } = 5f;
        public int MaxNamesInAnnouncement { get; set; } = 3;

        private readonly HashSet<string> _whitelist;
        private readonly Action _pause;
        private readonly Action _resume;
        private readonly Func<bool> _isRunning;
        private readonly Func<bool> _isPausedByUser;
        private readonly Action<string> _info;
        private readonly Action<string> _warn;
        private readonly Func<string> _whoAmI;

        private bool _pausedByGuard;
        private float _accum;
        private DateTime _lastThreatTimeUtc = DateTime.MinValue;

        // HUD announce state
        private DateTime _lastAnnounceUtc = DateTime.MinValue;
        private string _lastAnnounceKey = "";

        public ProximityGuard(Action pause,
                              Action resume,
                              Func<bool> isRunning,
                              Func<bool> isPausedByUser,
                              Action<string> info,
                              Action<string> warn,
                              Func<string> whoAmI,
                              IEnumerable<string> initialWhitelist = null)
        {
            _pause = pause ?? throw new ArgumentNullException(nameof(pause));
            _resume = resume ?? throw new ArgumentNullException(nameof(resume));
            _isRunning = isRunning ?? (() => true);
            _isPausedByUser = isPausedByUser ?? (() => false);
            _info = info ?? (_ => { });
            _warn = warn ?? (_ => { });
            _whoAmI = whoAmI ?? (() => "");
            _whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (initialWhitelist != null)
            {
                foreach (var n in initialWhitelist.Where(s => !string.IsNullOrWhiteSpace(s)))
                    _whitelist.Add(n.Trim());
            }
        }

        public IEnumerable<string> Whitelist => _whitelist;
        public void AddWhitelist(string name) { if (!string.IsNullOrWhiteSpace(name)) _whitelist.Add(name.Trim()); }
        public bool RemoveWhitelist(string name) => !string.IsNullOrWhiteSpace(name) && _whitelist.Remove(name.Trim());
        public void ClearWhitelist() => _whitelist.Clear();

        public void Update(float dt)
        {
            if (!Enabled) { _accum = 0f; return; }

            _accum += dt;
            var interval = 1f / Math.Max(ScanHz, 0.1f);
            if (_accum < interval) return;
            _accum = 0f;

            // Build a snapshot list once per scan
            var threats = GetThreatsSnapshot();
            var anyThreat = threats.Count > 0;

            // --- HUD announcements (work even if not stacking) ---
            if (AnnounceThreats)
            {
                var now = DateTime.UtcNow;
                var key = MakeAnnounceKey(threats);
                var due = (now - _lastAnnounceUtc).TotalSeconds >= AnnounceIntervalSec;

                if (anyThreat && (due || key != _lastAnnounceKey))
                {
                    AnnounceThreatsList(threats);
                    _lastAnnounceUtc = now;
                    _lastAnnounceKey = key;
                }
                else if (!anyThreat && key != _lastAnnounceKey && _lastAnnounceKey != "")
                {
                    _info("[Ascension:Stealth] Area clear.");
                    _lastAnnounceUtc = now;
                    _lastAnnounceKey = key; // becomes empty
                }
            }

            // --- Auto-pause / resume when stacking ---
            if (anyThreat)
            {
                _lastThreatTimeUtc = DateTime.UtcNow;

                if (_isRunning() && !_pausedByGuard && !_isPausedByUser())
                {
                    _pause();
                    _pausedByGuard = true;

                    var first = threats[0];
                    _warn($"[Ascension:Stealth] Threat in range ({first.Name}, {first.DistanceM:0.#} m). Auto-paused.");
                }
            }
            else if (_pausedByGuard)
            {
                var clearFor = (DateTime.UtcNow - _lastThreatTimeUtc).TotalSeconds;
                if (clearFor >= ClearSeconds && !_isPausedByUser())
                {
                    _resume();
                    _pausedByGuard = false;
                    _info("[Ascension:Stealth] Area clear. Auto-resumed.");
                }
            }
        }

        /// <summary>
        /// On-demand list of threats right now (names + distances within TriggerRangeM).
        /// </summary>
        public List<ThreatInfo> GetThreatsSnapshot()
        {
            var list = new List<ThreatInfo>();

            var me = DynelManager.LocalPlayer;
            if (me == null) return list;

            var myPos = me.Position;
            var myName = _whoAmI();

            foreach (var sc in EnumeratePlayersSafe())
            {
                try
                {
                    if (!string.IsNullOrEmpty(myName) && sc.Name.Equals(myName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (_whitelist.Contains(sc.Name))
                        continue;

                    var d = Vector3.Distance(sc.Position, myPos);
                    if (d <= TriggerRangeM)
                        list.Add(new ThreatInfo(sc.Name, d));
                }
                catch { /* ignore bad dynels */ }
            }

            // Sort by distance
            list.Sort((a, b) => a.DistanceM.CompareTo(b.DistanceM));
            return list;
        }

        private void AnnounceThreatsList(List<ThreatInfo> threats)
        {
            if (threats == null || threats.Count == 0) return;

            var show = Math.Min(MaxNamesInAnnouncement, threats.Count);
            var parts = new List<string>(show);
            for (int i = 0; i < show; i++)
                parts.Add($"{threats[i].Name} ({threats[i].DistanceM:0.#}m)");

            var suffix = threats.Count > show ? $" +{threats.Count - show} more" : "";
            _warn($"[Ascension:Stealth] {threats.Count} player(s) within {TriggerRangeM:0.#}m: {string.Join(", ", parts)}{suffix}");
        }

        private string MakeAnnounceKey(List<ThreatInfo> threats)
        {
            if (threats == null || threats.Count == 0) return "";
            // Stable-ish signature (already sorted by distance); bucket to 1 decimal to reduce spam
            return string.Join("|", threats.Select(t => $"{t.Name}:{Math.Round(t.DistanceM, 1)}"));
        }

        /// <summary>
        /// Works on AOSharp builds without GetAll():
        /// - Primary: DynelManager.Characters
        /// - Fallback (runtime reflection): DynelManager.GetAll()
        /// </summary>
        private static IEnumerable<SimpleChar> EnumeratePlayersSafe()
        {
            var list = new List<SimpleChar>();

            // Primary path: Characters
            try
            {
                var charsEnum = DynelManager.Characters;
                foreach (var c in charsEnum)
                {
                    if (c != null && SafeIsPlayer(c))
                        list.Add(c);
                }
            }
            catch
            {
                // ignore; try reflection fallback below
            }

            // Reflection fallback to avoid compile-time dependency on GetAll()
            try
            {
                var mi = typeof(DynelManager).GetMethod("GetAll", BindingFlags.Public | BindingFlags.Static);
                if (mi != null)
                {
                    var result = mi.Invoke(null, null) as IEnumerable;
                    if (result != null)
                    {
                        foreach (var d in result)
                        {
                            var sc = d as SimpleChar;
                            if (sc != null && SafeIsPlayer(sc) && !list.Contains(sc))
                                list.Add(sc);
                        }
                    }
                }
            }
            catch
            {
                // swallow — not all builds have GetAll
            }

            return list;
        }

        private static bool SafeIsPlayer(SimpleChar sc)
        {
            try { return sc.IsPlayer; }
            catch { try { return !sc.IsNpc; } catch { return true; } }
        }
    }
}
