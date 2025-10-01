// Main.cs — Ascension DEV (C# 7.3) — full features + color UI + XP tracker
// ---------------------------------------------------------------------------------
// This is the DEVELOPER build: all automation features are enabled.
// Includes: safe+turbo swap, scan, calibration, limits, meter, graphs,
// gradient ASCII logo (/ascend), high-contrast UI (/style), license system,
// XP per-kill tracker (/asc xpm), and detailed ASCII bar chart output.
// No System.Linq used — pure loops/reflection for maximum compatibility.
// ---------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using System.Globalization;

using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Core.Inventory;
using AOSharp.Common.GameData;

using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace Ascension
{
    public class Main : AOPluginEntry
    {
        // ===== Build toggles =====
        private const bool DevBuildBypassLock = true; // developers: set true to ignore license gates

        // ===== Output routing =====
        private static ChatWindow _outWin;

        // ===== Swap core =====
        private static Container _stackBag;
        private static EquipSlot _slotToUse;
        private static bool _doSwapOnce;

        private enum SwapState { Idle, EquipIssued, PullIssued, VerifyBagRecovered, Cooldown }
        private static SwapState _state = SwapState.Idle;
        private static float _stateTimerMs;
        private static int _delayMs = 600;
        private static int _autoIntervalMs = 600;

        private static bool _autoEnabled;
        private static int _autoCyclesRemaining = -1; // -1 = infinite
        private static int _autoCyclesTarget = -1;
        private static float _autoTimerMs;
        private static bool _turbo;
        private static int _turboBurst = 20;

        // ===== Safe verification =====
        private static int _preBagCount;

        // ===== Meter & history =====
        private static bool _meterEnabled;
        private static int _meterEverySwaps = 10;
        private static int _sinceLastMeterSwaps;
        private static int _totalSwaps;
        private static int _swapsThisSecond;
        private static float _secAccum;
        private const int HistoryWidth = 40;
        private static readonly int[] _rateHistory = new int[HistoryWidth];
        private static int _rateIndex;

        // ===== Scan =====
        private static bool _scanning;
        private static float _scanTimerMs;
        private static int _scanStage; // 1=equip wait, 2=pull wait
        private static readonly Dictionary<Stat, int> _scanBaseline = new Dictionary<Stat, int>();
        private static readonly Dictionary<Stat, int> _scanEquipped = new Dictionary<Stat, int>();
        private static readonly Dictionary<Stat, int> _itemDelta = new Dictionary<Stat, int>();
        private static bool _hasItemScan;

        // ===== Calibration =====
        private static bool _calibrating;
        private static float _calObservedEquipMs = -1f;
        private static float _calTimerMs;

        // ===== Limits =====
        private struct Limit { public readonly Stat Id; public readonly int Target; public readonly string Alias; public Limit(Stat id, int target, string alias) { Id = id; Target = target; Alias = alias; } }
        private static readonly List<Limit> _limits = new List<Limit>();

        // ===== Swaps limit =====
        private static int _swapLimitTarget = -1;
        private static int _swapLimitBaseline = 0;

        // ===== Tracking presets =====
        private struct Tracked { public readonly Stat Id; public readonly string Alias; public Tracked(Stat id, string alias) { Id = id; Alias = alias; } }
        private static readonly List<Tracked> _tracked = new List<Tracked>();
        private static readonly Dictionary<Stat, int> _baselineStats = new Dictionary<Stat, int>();
        private static readonly Dictionary<Stat, int> _lastPrintedStats = new Dictionary<Stat, int>();
        private static readonly Dictionary<Stat, int> _injectionSnapshot = new Dictionary<Stat, int>();
        private static bool _autoTrack;

        // Pseudo stat: XP%
        private const int PseudoXpPercentId = -10001;
        private static readonly Stat PseudoXpPercent = (Stat)PseudoXpPercentId;

        // ===== License / warnings =====
        private static float _tamperTimer;
        private static float _warnTimer;
        private const string Warn1 = "[Ascension] WARNING: This tool automates gameplay. Use at your own risk; it may violate the game's Terms and result in penalties.";
        private const string Warn2 = "[Ascension] DISCLAIMER: For personal use only. Keys are non-transferable. Support ends if tamper is detected.";
        private static string _licPasteBuffer = "";

        // ===== XP per-kill tracker =====
        private static bool _xpmEnabled;
        private static float _xpmPollTimer;
        private const float XpmPollEvery = 0.20f;
        private const int XpmHistory = 64;
        private static readonly int[] _xpEvents = new int[XpmHistory];
        private static int _xpEvtCount, _xpEvtHead;
        private static int _lastXpThisLevel = -1;
        private static int _lastXpToNext = -1;
        private static bool _xpmReady;

        // ===== Init baselines / change trackers =====
        private static DateTime _startTs;
        private static int _initDelay, _initBurst; private static bool _initTurbo;
        private static int _changesDelaySets, _changesBurstSets, _changesTurboToggles, _changesSlotSets, _changesMeterSets, _calRuns, _autoStarts, _autoStops;

        public override void Run(string pluginDir)
        {
            License.Init();
            AscensionAscii.Install();
            AscUi.Install();
            RegisterAscCommand();

            _outWin = null;
            Game.OnUpdate += OnUpdate;

            _startTs = DateTime.UtcNow;
            _initDelay = _delayMs; _initBurst = _turboBurst; _initTurbo = _turbo;

            ApplyAbilitiesTracking();
            TryCaptureAllBaselines();

            Out("[Ascension] Loaded. Try: /asc setitem, /asc auto on, /asc graph, /asc xpm on  |  /asc channel default|here");
            if (License.IsUnlocked) PrintUnlockBanner();
            else Out("[Ascension] Locked. Use /asc license apply <base64> (then /asc license status).");
        }

        public override void Teardown()
        {
            Game.OnUpdate -= OnUpdate;
            AscensionAscii.Uninstall();
        }

        // ===================== Command registration (LINQ-free) =====================
        private void RegisterAscCommand()
        {
            MethodInfo[] methods = typeof(Chat).GetMethods(BindingFlags.Public | BindingFlags.Static);
            bool registered = false;
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i]; if (m.Name != "RegisterCommand") continue;
                var ps = m.GetParameters(); if (ps.Length != 2) continue;
                Type t = ps[1].ParameterType;
                try
                {
                    if (t == typeof(Action<string, string[], ChatWindow>))
                    { Action<string, string[], ChatWindow> h3 = OnCmd; m.Invoke(null, new object[] { "asc", h3 }); registered = true; break; }
                    if (t == typeof(Action<string, string[]>))
                    { Action<string, string[]> h2 = OnCmd2; m.Invoke(null, new object[] { "asc", h2 }); registered = true; break; }
                }
                catch { }
            }
            Out(registered ? "[Ascension] /asc ready." : "[Ascension] Failed to register /asc.");
        }
        private void OnCmd2(string command, string[] args) => OnCmd(command, args, null);

        // ===================== Output helper =====================
        private static void Out(string msg)
        {
            try
            {
                if (_outWin != null)
                {
                    try
                    {
                        var nameProp = _outWin.GetType().GetProperty("Name") ?? _outWin.GetType().GetProperty("Title");
                        string wname = nameProp != null ? (nameProp.GetValue(_outWin, null) as string ?? "") : "";
                        if (wname.IndexOf("combat", StringComparison.OrdinalIgnoreCase) >= 0 || wname.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0) _outWin = null;
                    }
                    catch { }
                    if (_outWin != null)
                    {
                        string b = msg ?? ""; if (b.StartsWith("[Ascension]")) b = b.Substring(12).TrimStart();
                        _outWin.WriteLine(AscUi.Prefix() + b); return;
                    }
                }
            }
            catch { }
            string s = msg ?? ""; if (s.StartsWith("[Ascension]")) s = s.Substring(12).TrimStart();
            Chat.WriteLine(AscUi.Prefix() + s);
        }

        // ===================== /asc command =====================
        private void OnCmd(string command, string[] args, ChatWindow invokingWindow)
        {
            if (args == null || args.Length == 0) { PrintQuickHelp(); return; }
            string sub = (args[0] ?? "").ToLowerInvariant();

            bool locked = !(DevBuildBypassLock || License.IsUnlocked);
            bool basicAllowed = sub == "help" || sub == "license" || sub == "status" || sub == "channel" || sub == "graph" || sub == "about" || sub == "ascend" || sub == "xpm";
            if (locked && !basicAllowed) { Out("[Ascension] Locked. Use /asc license status | /asc license apply <base64>."); return; }

            switch (sub)
            {
                case "help": PrintFullHelp(); break;
                case "about": PrintAbout(); break;
                case "ascend": PrintAscend(); break;
                case "graph": PrintGraphSimple(); break;
                case "status": PrintStatus(); break;
                case "chart": PrintProgressBar(); break;
                case "channel": HandleChannel(args, invokingWindow); break;

                case "setitem":
                case "set": DoSetItem(); break;

                case "scan": StartScan(); break;

                case "toggle":
                case "go":
                case "swap": _doSwapOnce = true; Out("[Ascension] Swapping..."); break;

                case "delay":
                    if (args.Length >= 2)
                    {
                        int ms; if (int.TryParse(args[1], out ms)) { SetDelayAndInterval(ms); Out("[Ascension] Delay & interval = " + _delayMs + " ms."); }
                        else Out("[Ascension] Usage: /asc delay <2..5000>");
                    }
                    else Out("[Ascension] Current delay/interval: " + _delayMs + " ms.");
                    break;

                case "auto": HandleAuto(args); break;
                case "cal":
                case "calibrate": StartCalibration(); break;
                case "turbo": HandleTurbo(args); break;
                case "meter": HandleMeter(args); break;
                case "track": HandleTrack(args); break;
                case "abilities":
                    { bool currentlyAbilities = IsTracked(Stat.Strength); if (currentlyAbilities) ApplyXpOnlyTracking(); else ApplyAbilitiesTracking(); break; }
                case "stats":
                    {
                        bool any; string tbl = BuildStatTable(out any);
                        if (any) Out("[Ascension] Stat deltas (total | +last):\n" + tbl); else Out("[Ascension] Stat gains: none");
                        break;
                    }
                case "limit": HandleLimit(args); break;
                case "swaps": HandleSwaps(args); break;
                case "license": HandleLicense(args); break;

                // XP tracker
                case "xpm": HandleXpm(args); break;

                default: Out("[Ascension] Unknown subcommand. Use /asc help."); break;
            }
        }

        private static void PrintQuickHelp()
        {
            AscUi.Title("Quick Help");
            Chat.WriteLine(AscUi.Bul("Try:")
                + " " + AscUi.Cmd("/asc setitem")
                + " " + AscUi.Cmd("/asc auto on [cycles] [ms]")
                + " " + AscUi.Cmd("/asc xpm on")
                + " " + AscUi.Cmd("/asc graph")
                + " " + AscUi.Cmd("/asc status")
                + " " + AscUi.Cmd("/style"));
        }
        private static void PrintFullHelp()
        {
            AscUi.Title("Ascension Commands");
            AscUi.KV("/asc about", "plugin info");
            AscUi.KV("/asc setitem", "first item in backpack named \"stack\"; auto-select equip slot");
            AscUi.KV("/asc toggle", "perform one safe swap");
            AscUi.KV("/asc auto on [cycles] [ms]", "continuous swaps; /asc auto off");
            AscUi.KV("/asc turbo on [burst]", "spam mode (default 20) | off");
            AscUi.KV("/asc delay <2..5000>", "set delay&interval");
            AscUi.KV("/asc cal", "auto-calibrate delay to latency");
            AscUi.KV("/asc meter on [every]", "periodic simplified graph | off");
            AscUi.KV("/asc graph", "progress + SPS + XP%");
            AscUi.KV("/asc abilities", "toggle preset (abilities ↔ XP%)");
            AscUi.KV("/asc track ...", "custom stat tracking");
            AscUi.KV("/asc limit ...", "auto-stop on stat target");
            AscUi.KV("/asc swaps limit <N>", "stop after N swaps | status | clear");
            AscUi.KV("/asc xpm on", "enable XP per-kill tracker | graph | bars | clear");
            AscUi.KV("/asc license ...", "status | apply | chunked paste | reload");
            AscUi.KV("/asc channel ...", "default|here");
        }
        private static void PrintAbout()
        {
            AscUi.Title("About Ascension");
            Chat.WriteLine(AscUi.Muted("Automation, stats, and UI utilities for testing and research."));
        }
        private static void PrintAscend()
        {
            AscUi.Title("Ascension Protocol");
            string[] tips = {
                "Rename a backpack to " + AscUi.Val("stack") + ".",
                "Place the swap item as the " + AscUi.Val("first") + " item in that bag.",
                "Use " + AscUi.Cmd("/asc setitem") + " to autodetect slot.",
                "Test: " + AscUi.Cmd("/asc toggle") + "   |   Continuous: " + AscUi.Cmd("/asc auto on [cycles] [ms]"),
                "Speed: " + AscUi.Cmd("/asc turbo on 20..40") + "    Meter: " + AscUi.Cmd("/asc meter on 10")
            };
            for (int i = 0; i < tips.Length; i++) Chat.WriteLine(AscUi.Bul(tips[i]));
        }

        // ===================== Channel =====================
        private void HandleChannel(string[] args, ChatWindow win)
        {
            if (args.Length == 1)
            {
                Out("[Ascension] /asc channel default  — use main chat (recommended)");
                Out("[Ascension] /asc channel here     — pin output to THIS window");
                Out("[Ascension] /asc channel clear    — same as default");
                return;
            }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "here") { _outWin = win; Out("[Ascension] Output pinned to this window."); return; }
            if (sub == "default" || sub == "clear") { _outWin = null; Out("[Ascension] Output routed to default/main chat."); return; }
            Out("[Ascension] /asc channel default|here|clear");
        }

        // ===================== Auto/Turbo/Meter =====================
        private void HandleAuto(string[] args)
        {
            if (args.Length == 1) { Out("[Ascension] /asc auto on [cycles] [intervalMs] | /asc auto off"); return; }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "off") { if (_autoEnabled) _autoStops++; _autoEnabled = false; _autoCyclesRemaining = -1; _autoCyclesTarget = -1; Out("[Ascension] Auto OFF."); return; }
            if (sub == "on")
            {
                if (!_autoEnabled) _autoStarts++; _autoEnabled = true;
                int cycles;
                if (args.Length >= 3 && int.TryParse(args[2], out cycles)) { _autoCyclesRemaining = Math.Max(1, cycles); _autoCyclesTarget = _autoCyclesRemaining; } else { _autoCyclesRemaining = -1; _autoCyclesTarget = -1; }
                int interval;
                if (args.Length >= 4 && int.TryParse(args[3], out interval)) SetDelayAndInterval(interval);
                _autoTimerMs = 0f;
                if (!_hasItemScan) Out("[Ascension] Tip: run /asc scan to measure per-equip stat deltas (optional).");
                Out("[Ascension] Auto ON. cycles=" + (_autoCyclesRemaining < 0 ? "∞" : _autoCyclesRemaining.ToString()) + ", interval=" + _autoIntervalMs + "ms.");
                return;
            }
            Out("[Ascension] /asc auto on [cycles] [intervalMs] | /asc auto off");
        }
        private void HandleTurbo(string[] args)
        {
            if (args.Length == 1) { Out("[Ascension] /asc turbo on [burstPerFrame] | /asc turbo off"); return; }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "off") { if (_turbo) _changesTurboToggles++; _turbo = false; Out("[Ascension] Turbo OFF."); return; }
            if (sub == "on")
            {
                if (!_turbo) _changesTurboToggles++; _turbo = true;
                int burst; if (args.Length >= 3 && int.TryParse(args[2], out burst)) { burst = ClampInt(burst, 1, 100); if (burst != _turboBurst) _changesBurstSets++; _turboBurst = burst; }
                else { if (20 != _turboBurst) _changesBurstSets++; _turboBurst = 20; }
                Out("[Ascension] Turbo ON. burst=" + _turboBurst + "."); return;
            }
            Out("[Ascension] /asc turbo on [burstPerFrame] | /asc turbo off");
        }
        private void HandleMeter(string[] args)
        {
            if (args.Length == 1) { Out("[Ascension] /asc meter on [everySwaps] | /asc meter off"); return; }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "off") { _meterEnabled = false; Out("[Ascension] Meter OFF."); return; }
            if (sub == "on")
            {
                _meterEnabled = true;
                int every; _meterEverySwaps = (args.Length >= 3 && int.TryParse(args[2], out every)) ? ClampInt(every, 1, 500) : 10;
                _sinceLastMeterSwaps = 0; _changesMeterSets++;
                Out("[Ascension] Meter ON. update every " + _meterEverySwaps + " swaps."); return;
            }
            Out("[Ascension] /asc meter on [everySwaps] | /asc meter off");
        }

        // ===================== Limits / Swaps limit =====================
        private void HandleLimit(string[] args)
        {
            if (args.Length == 1) { Out("[Ascension] Limits: /asc limit add <statId> <value> [alias] | /asc limit remove <id|alias> | /asc limit list | /asc limit clear"); return; }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "list")
            {
                if (_limits.Count == 0) { Out("[Ascension] (no limits set)"); return; }
                for (int i = 0; i < _limits.Count; i++) { var L = _limits[i]; string name = (L.Alias ?? "").Length > 0 ? L.Alias : (Enum.IsDefined(typeof(Stat), L.Id) ? Enum.GetName(typeof(Stat), L.Id) : "id " + (int)L.Id); Out(" - " + name + " >= " + L.Target); }
                return;
            }
            if (sub == "clear") { _limits.Clear(); Out("[Ascension] Limits cleared."); return; }
            if (sub == "remove" && args.Length >= 3)
            {
                string key = args[2]; int idNum;
                if (int.TryParse(key, out idNum))
                {
                    int before = _limits.Count; for (int i = _limits.Count - 1; i >= 0; i--) if ((int)_limits[i].Id == idNum) _limits.RemoveAt(i);
                    Out(_limits.Count != before ? "[Ascension] Removed limit for id " + idNum + "." : "[Ascension] No limit for id " + idNum + "."); return;
                }
                else
                {
                    int before = _limits.Count; for (int i = _limits.Count - 1; i >= 0; i--) if (string.Equals(_limits[i].Alias, key, StringComparison.OrdinalIgnoreCase)) _limits.RemoveAt(i);
                    Out(_limits.Count != before ? "[Ascension] Removed limit for alias '" + key + "'." : "[Ascension] No limit for alias '" + key + "'."); return;
                }
            }
            if (sub == "add" && args.Length >= 4)
            {
                int idNum, value; if (!int.TryParse(args[2], out idNum) || !int.TryParse(args[3], out value)) { Out("[Ascension] Usage: /asc limit add <statId> <value> [alias]"); return; }
                string alias = args.Length >= 5 ? args[4] : ""; var lim = new Limit((Stat)idNum, value, alias);
                for (int i = 0; i < _limits.Count; i++) if (_limits[i].Id == lim.Id) { _limits[i] = lim; Out("[Ascension] Replaced limit for id " + idNum + " = " + value + "."); return; }
                _limits.Add(lim); Out("[Ascension] Limit added: id " + idNum + " >= " + value + (alias != "" ? (" (" + alias + ")") : "")); return;
            }
            Out("[Ascension] Limits: /asc limit add <statId> <value> [alias] | /asc limit remove <id|alias> | /asc limit list | /asc limit clear");
        }
        private void HandleSwaps(string[] args)
        {
            if (args.Length == 1) { Out("[Ascension] Swaps limit: /asc swaps limit <N>  |  /asc swaps clear  |  /asc swaps status"); return; }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "status") { if (_swapLimitTarget > 0) { int used = _totalSwaps - _swapLimitBaseline; Out("[Ascension] Swaps limit " + _swapLimitTarget + "  | used " + used + "  | left " + Math.Max(0, _swapLimitTarget - used)); } else Out("[Ascension] Swaps limit is OFF."); return; }
            if (sub == "clear") { _swapLimitTarget = -1; Out("[Ascension] Swaps limit cleared."); return; }
            if (sub == "limit" && args.Length >= 3) { int n; if (!int.TryParse(args[2], out n) || n <= 0) { Out("[Ascension] Usage: /asc swaps limit <N>"); return; } _swapLimitTarget = n; _swapLimitBaseline = _totalSwaps; Out("[Ascension] Will stop after " + n + " swaps from now."); return; }
            Out("[Ascension] Swaps limit: /asc swaps limit <N>  |  /asc swaps clear  |  /asc swaps status");
        }

        // ===================== Tracking Presets =====================
        private static void ApplyXpOnlyTracking()
        {
            _tracked.Clear(); _tracked.Add(new Tracked(PseudoXpPercent, "XP%")); _autoTrack = false; CaptureStatBaseline(); Out("[Ascension] Tracking set to: XP% only.");
        }
        private static void ApplyAbilitiesTracking()
        {
            _tracked.Clear();
            _tracked.Add(new Tracked(Stat.Strength, "Str"));
            _tracked.Add(new Tracked(Stat.Agility, "Agi"));
            _tracked.Add(new Tracked(Stat.Stamina, "Sta"));
            _tracked.Add(new Tracked(Stat.Sense, "Sen"));
            _tracked.Add(new Tracked(Stat.Intelligence, "Int"));
            _tracked.Add(new Tracked(Stat.Psychic, "Psy"));
            _tracked.Add(new Tracked(PseudoXpPercent, "XP%"));
            _autoTrack = false; CaptureStatBaseline(); Out("[Ascension] Tracking set to base abilities (+ XP%).");
        }
        private void HandleTrack(string[] args)
        {
            if (args.Length == 1)
            {
                Out("[Ascension] Tracking:"); PrintTrackList();
                Out("[Ascension] Commands: /asc track add <statId> <alias> | /asc track remove <alias|statId> | /asc track list | /asc track preset xp|abilities | /asc track auto on|off"); return;
            }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "list") { PrintTrackList(); return; }
            if (sub == "preset" && args.Length >= 3) { string which = (args[2] ?? "").ToLowerInvariant(); if (which == "xp") { ApplyXpOnlyTracking(); return; } if (which == "abilities") { ApplyAbilitiesTracking(); return; } Out("[Ascension] Unknown preset. Supported: xp, abilities"); return; }
            if (sub == "auto" && args.Length >= 3) { string v = (args[2] ?? "").ToLowerInvariant(); _autoTrack = (v == "on" || v == "true" || v == "1"); Out("[Ascension] Auto-track " + (_autoTrack ? "ON" : "OFF") + "."); return; }
            if (sub == "add" && args.Length >= 4)
            {
                int idNum; if (!int.TryParse(args[2], out idNum)) { Out("[Ascension] Usage: /asc track add <statId> <alias>"); return; }
                Stat sid = (Stat)idNum; string alias = args[3];
                for (int i = 0; i < _tracked.Count; i++) if (string.Equals(_tracked[i].Alias, alias, StringComparison.OrdinalIgnoreCase)) { _tracked[i] = new Tracked(sid, alias); CaptureStatBaseline(); Out("[Ascension] Replaced alias " + alias + " -> id " + idNum + "."); return; }
                _tracked.Add(new Tracked(sid, alias)); CaptureStatBaseline(); Out("[Ascension] Tracking id " + idNum + " as '" + alias + "'."); return;
            }
            if (sub == "remove" && args.Length >= 3)
            {
                string key = args[2]; int idNum;
                if (int.TryParse(key, out idNum)) { int before = _tracked.Count; for (int i = _tracked.Count - 1; i >= 0; i--) if ((int)_tracked[i].Id == idNum) _tracked.RemoveAt(i); if (_tracked.Count != before) { CaptureStatBaseline(); Out("[Ascension] Removed tracking for id " + idNum + "."); } else Out("[Ascension] No entry for id " + idNum + "."); return; }
                else { int before = _tracked.Count; for (int i = _tracked.Count - 1; i >= 0; i--) if (string.Equals(_tracked[i].Alias, key, StringComparison.OrdinalIgnoreCase)) _tracked.RemoveAt(i); if (_tracked.Count != before) { CaptureStatBaseline(); Out("[Ascension] Removed tracking for alias '" + key + "'."); } else Out("[Ascension] No entry for alias '" + key + "'."); return; }
            }
            Out("[Ascension] Commands: /asc track add <statId> <alias> | /asc track remove <alias|statId> | /asc track list | /asc track preset xp|abilities | /asc track auto on|off");
        }
        private static void PrintTrackList()
        {
            if (_tracked.Count == 0) { Out("[Ascension] (no stats tracked)"); return; }
            for (int i = 0; i < _tracked.Count; i++) Out(" - " + PadRight(_tracked[i].Alias, 8) + "  id=" + (int)_tracked[i].Id);
        }

        // ===================== SetItem / Scan / Calibration =====================
        private static Container FindStackBag()
        {
            try
            {
                foreach (var bag in Inventory.Backpacks)
                {
                    if (bag != null && bag.Name != null && string.Equals(bag.Name, "stack", StringComparison.OrdinalIgnoreCase))
                        return bag;
                }
            }
            catch { }
            return null;
        }
        private static void EnsureSlotFromItem(Item item)
        {
            if (_slotToUse != 0) return;
            try
            {
                if (item.EquipSlots != null)
                {
                    foreach (EquipSlot s in item.EquipSlots) { _slotToUse = s; _changesSlotSets++; break; }
                }
            }
            catch { }
            if (_slotToUse == 0) throw new Exception(item.Name + " is not equippable.");
        }
        private void DoSetItem()
        {
            _stackBag = FindStackBag();
            if (_stackBag == null) { Out("[Ascension] Could not find a backpack named \"stack\". Rename your bag to exactly: stack"); return; }
            Item first = null; try { foreach (var it in _stackBag.Items) { first = it; break; } } catch { }
            if (first == null) { Out("[Ascension] Your \"stack\" bag is empty."); return; }
            var allowed = first.EquipSlots;
            if (allowed == null) { Out("[Ascension] " + first.Name + " is not equippable."); return; }
            _slotToUse = 0; foreach (EquipSlot s in allowed) { _slotToUse = s; break; }
            if (_slotToUse == 0) { Out("[Ascension] " + first.Name + " has no valid slot."); return; }
            _changesSlotSets++; _hasItemScan = false; _itemDelta.Clear();
            Out("[Ascension] Set item: " + first.Name + "  |  Slot: " + _slotToUse + ".");
        }

        private void StartScan()
        {
            _stackBag = FindStackBag();
            if (_stackBag == null) { Out("[Ascension] Scan aborted: \"stack\" bag not found."); return; }
            Item first = null; try { foreach (var it in _stackBag.Items) { first = it; break; } } catch { }
            if (first == null) { Out("[Ascension] Scan aborted: \"stack\" bag is empty."); return; }
            try { EnsureSlotFromItem(first); } catch (Exception ex) { Out("[Ascension] Scan aborted: " + ex.Message); return; }

            if (_scanning) { Out("[Ascension] Scan already in progress."); return; }

            _scanBaseline.Clear(); foreach (var id in AllStatIds()) _scanBaseline[id] = ReadStat(id);
            try { first.Equip(_slotToUse); } catch (Exception ex) { Out("[Ascension] Scan failed to equip: " + ex.Message); return; }

            _scanning = true; _scanStage = 1; _scanTimerMs = 0f; _preBagCount = SafeBagCount();
            Out("[Ascension] Scanning… capturing equipped stats next, then pulling back.");
        }
        private void TickScan(float dt)
        {
            if (!_scanning) return;
            _scanTimerMs += dt * 1000f;
            if (_scanStage == 1)
            {
                if (_scanTimerMs >= _delayMs)
                {
                    _scanEquipped.Clear(); foreach (var id in AllStatIds()) _scanEquipped[id] = ReadStat(id);
                    TryPullSwappedFromWearToBag(); _scanTimerMs = 0f; _scanStage = 2;
                }
                return;
            }
            if (_scanStage == 2)
            {
                if (_scanTimerMs >= 300f)
                {
                    _itemDelta.Clear(); int nonZero = 0;
                    foreach (var kv in _scanEquipped)
                    {
                        int before = _scanBaseline.ContainsKey(kv.Key) ? _scanBaseline[kv.Key] : 0;
                        int delta = kv.Value - before;
                        if (delta != 0) { _itemDelta[kv.Key] = delta; nonZero++; }
                    }
                    _hasItemScan = true; _scanning = false; _scanStage = 0;
                    Out(nonZero == 0 ? "[Ascension] Scan complete. No stat deltas detected." : "[Ascension] Scan complete. Captured " + nonZero + " stat deltas.");
                }
            }
        }

        private void StartCalibration()
        {
            _stackBag = FindStackBag();
            if (_stackBag == null) { Out("[Ascension] Calibration aborted: \"stack\" bag not found."); return; }
            Item first = null; try { foreach (var it in _stackBag.Items) { first = it; break; } } catch { }
            if (first == null) { Out("[Ascension] Calibration aborted: \"stack\" bag is empty."); return; }
            try { EnsureSlotFromItem(first); } catch (Exception ex) { Out("[Ascension] Calibration aborted: " + ex.Message); return; }

            _calibrating = true; _state = SwapState.Idle; _calObservedEquipMs = -1f; _calTimerMs = 0f; _preBagCount = SafeBagCount();
            try { first.Equip(_slotToUse); Out("[Ascension] Calibrating… do not touch anything."); } catch (Exception ex) { _calibrating = false; Out("[Ascension] Calibration failed: " + ex.Message); }
        }
        private void TickCalibration(float dt)
        {
            _calTimerMs += dt * 1000f;
            if (_calObservedEquipMs < 0f)
            {
                if (SafeBagCount() <= _preBagCount - 1) { _calObservedEquipMs = _calTimerMs; SetDelayAndInterval((int)(_calObservedEquipMs + 120f)); TryPullSwappedFromWearToBag(); _calTimerMs = 0f; _calRuns++; }
                else if (_calTimerMs > 2000f) { Out("[Ascension] Calibration timeout (no equip reflection). Keeping prior values."); _calibrating = false; }
                return;
            }
            if (SafeBagCount() >= Math.Max(0, _preBagCount - 1)) { Out("[Ascension] Calibration done. Delay/interval = " + _delayMs + " ms (equip ack ~" + (int)_calObservedEquipMs + " ms)."); _calibrating = false; return; }
            if (_calTimerMs > 2000f) { Out("[Ascension] Calibration warning: pull verify timeout. Values set anyway; adjust with /asc delay."); _calibrating = false; }
        }

        // ===================== Update loop =====================
        private void OnUpdate(object s, float dt)
        {
            // License/tamper
            _tamperTimer += dt; if (_tamperTimer >= 5f) { _tamperTimer = 0f; License.PeriodicCheck(); }
            if (!License.IsUnlocked && !DevBuildBypassLock) { _autoEnabled = false; _turbo = false; _meterEnabled = false; _scanning = false; _calibrating = false; }
            if (License.IsUnlocked) { _warnTimer += dt; if (_warnTimer >= 300f) { _warnTimer = 0f; Out(Warn1); Out(Warn2); TimeSpan left; if (License.TryGetTimeLeft(out left)) Out("[Ascension] License time left: " + License.TimeLeftShort()); } }

            // SPS 1s buckets
            _secAccum += dt;
            if (_secAccum >= 1f) { int idx = _rateIndex % HistoryWidth; _rateHistory[idx] = _swapsThisSecond; _rateIndex = (idx + 1) % HistoryWidth; _swapsThisSecond = 0; _secAccum -= 1f; }

            // Limits stop
            if (_autoEnabled && _limits.Count > 0) CheckLimitsAndMaybeStop();

            // Auto ignition
            if (_autoEnabled && !_calibrating && !_scanning) { _autoTimerMs += dt * 1000f; if (_autoTimerMs >= _autoIntervalMs) { _autoTimerMs = 0f; _doSwapOnce = true; } }

            // Calibration/Scan
            if (_calibrating) { TickCalibration(dt); return; }
            if (_scanning) { TickScan(dt); return; }

            // XP tracker
            if (_xpmEnabled) { _xpmPollTimer += dt; if (_xpmPollTimer >= XpmPollEvery) { _xpmPollTimer = 0f; PollXpPerKill(); } }

            // Turbo path
            if (_turbo) { TickTurbo(); return; }

            // Safe path
            if (_doSwapOnce && _state == SwapState.Idle) { _doSwapOnce = false; BeginSafeSwap(); }
            TickSafe(dt);
        }

        // ===================== Swap paths =====================
        private void TickTurbo()
        {
            if (!_doSwapOnce && !_autoEnabled) return;
            _stackBag = FindStackBag(); if (_stackBag == null) return;
            Item item = null; foreach (var it in _stackBag.Items) { item = it; break; }
            if (item == null) return;

            try
            {
                EnsureSlotFromItem(item);
                int loops = _turboBurst;
                for (int i = 0; i < loops; i++)
                {
                    item.Equip(_slotToUse);
                    var bank = new Identity { Type = IdentityType.BankByRef, Instance = (int)_slotToUse };
                    Network.Send(new ClientContainerAddItem { Target = _stackBag.Identity, Source = bank });
                    RegisterSwap(1);
                    item = null; foreach (var it in _stackBag.Items) { item = it; break; }
                    if (item == null) break;

                    if (_autoEnabled && _autoCyclesRemaining > 0)
                    {
                        _autoCyclesRemaining--; if (_autoCyclesRemaining == 0) { _autoEnabled = false; Out("[Ascension] Auto completed all cycles."); break; }
                    }
                }
            }
            catch (Exception ex) { Out("[Ascension] Turbo swap error: " + ex.Message); }
            if (_doSwapOnce && !_autoEnabled) _doSwapOnce = false;
        }

        private void BeginSafeSwap()
        {
            _stackBag = FindStackBag();
            if (_stackBag == null) { Out("[Ascension] \"stack\" bag not found."); return; }
            Item item = null; foreach (var it in _stackBag.Items) { item = it; break; }
            if (item == null) { Out("[Ascension] \"stack\" bag is empty."); return; }
            try
            {
                EnsureSlotFromItem(item);
                _preBagCount = SafeBagCount();
                item.Equip(_slotToUse);
                _state = SwapState.EquipIssued; _stateTimerMs = 0f;
            }
            catch (Exception ex) { Out("[Ascension] Equip failed: " + ex.Message); _state = SwapState.Idle; }
        }
        private void TickSafe(float dt)
        {
            switch (_state)
            {
                case SwapState.Idle: return;
                case SwapState.EquipIssued:
                    _stateTimerMs += dt * 1000f; if (_stateTimerMs >= _delayMs) { TryPullSwappedFromWearToBag(); _state = SwapState.PullIssued; _stateTimerMs = 0f; }
                    break;
                case SwapState.PullIssued:
                    _stateTimerMs += dt * 1000f; if (_stateTimerMs >= 250f) { _state = SwapState.VerifyBagRecovered; _stateTimerMs = 0f; }
                    break;
                case SwapState.VerifyBagRecovered:
                    if (SafeBagCount() >= Math.Max(0, _preBagCount - 1))
                    {
                        RegisterSwap(1);
                        if (_autoEnabled && _autoCyclesRemaining > 0) { _autoCyclesRemaining--; if (_autoCyclesRemaining == 0) { _autoEnabled = false; Out("[Ascension] Auto completed all cycles."); } }
                        _state = SwapState.Cooldown; _stateTimerMs = 0f;
                    }
                    else
                    {
                        _stateTimerMs += dt * 1000f; if (_stateTimerMs > 1500f) { Out("[Ascension] Warning: verification timeout; swap likely completed but not confirmed."); RegisterSwap(1); _state = SwapState.Cooldown; _stateTimerMs = 0f; }
                    }
                    break;
                case SwapState.Cooldown:
                    _stateTimerMs += dt * 1000f; if (_stateTimerMs >= 150f) { _state = SwapState.Idle; _stateTimerMs = 0f; }
                    break;
            }
        }
        private static void TryPullSwappedFromWearToBag()
        {
            try { var bank = new Identity { Type = IdentityType.BankByRef, Instance = (int)_slotToUse }; Network.Send(new ClientContainerAddItem { Target = _stackBag.Identity, Source = bank }); }
            catch (Exception ex) { Out("[Ascension] Pull failed: " + ex.Message); }
        }
        private static int SafeBagCount()
        {
            try { _stackBag = _stackBag ?? FindStackBag(); if (_stackBag == null || _stackBag.Items == null) return 0; int c = 0; foreach (var _ in _stackBag.Items) c++; return c; } catch { return 0; }
        }

        // ===================== Register swap + meter =====================
        private static void RegisterSwap(int count)
        {
            _totalSwaps += count; _swapsThisSecond += count;
            if (_swapLimitTarget > 0)
            {
                int used = _totalSwaps - _swapLimitBaseline;
                if (used >= _swapLimitTarget) { _autoEnabled = false; Out("[Ascension] Swaps limit reached (" + _swapLimitTarget + "). Auto OFF."); _swapLimitTarget = -1; }
            }
            if (_meterEnabled) { _sinceLastMeterSwaps += count; if (_sinceLastMeterSwaps >= _meterEverySwaps) { _sinceLastMeterSwaps = 0; PrintGraphSimple(); } }
        }

        // ===================== Limits check =====================
        private void CheckLimitsAndMaybeStop()
        {
            for (int i = 0; i < _limits.Count; i++)
            {
                var L = _limits[i]; int now = ReadStat(L.Id);
                if (now >= L.Target)
                {
                    _autoEnabled = false; _autoCyclesRemaining = -1; _autoCyclesTarget = -1;
                    string name = (L.Alias ?? "").Length > 0 ? L.Alias : (Enum.IsDefined(typeof(Stat), L.Id) ? Enum.GetName(typeof(Stat), L.Id) : "id " + (int)L.Id);
                    Out("[Ascension] LIMIT REACHED: " + name + " = " + now + " (target " + L.Target + "). Auto OFF."); break;
                }
            }
        }

        // ===================== UI graphs =====================
        private static void PrintGraphSimple()
        {
            int done = DoneCount();
            int total = (_autoCyclesTarget > 0 ? _autoCyclesTarget : -1);
            double sps5 = CurrentSPSAvg(5);
            double spsNow = _rateHistory[(_rateIndex + HistoryWidth - 1) % HistoryWidth];

            AscUi.Title("Progress");
            Chat.WriteLine(AscUi.Bar(30, done, total));

            int xp = TryReadXpPercent();
            string xpLine = (xp >= 0) ? ("  " + AscUi.Badge("XP%", AscUi.Palette.Accent) + " " + AscUi.Val(xp.ToString())) : "";
            Chat.WriteLine(
                AscUi.Badge("avg5", AscUi.Palette.Muted) + " " + AscUi.Val(sps5.ToString("0.0")) +
                "  " + AscUi.Badge("now", AscUi.Palette.Accent) + " " + AscUi.Val(spsNow.ToString("0")) +
                "  " + AscUi.Badge("swaps", AscUi.Palette.Accent) + " " + AscUi.Val(_totalSwaps.ToString()) + xpLine
            );
        }
        private static void PrintProgressBar()
        {
            AscUi.Title("Progress");
            Chat.WriteLine(AscUi.Bar(30, DoneCount(), _autoCyclesTarget > 0 ? _autoCyclesTarget : -1));
            Chat.WriteLine(AscUi.Muted("~" + CurrentSPSAvg(5).ToString("0.0") + " SPS avg5"));
        }

        private static int DoneCount() { if (_autoCyclesTarget > 0 && _autoCyclesRemaining >= 0) return _autoCyclesTarget - _autoCyclesRemaining; return _totalSwaps; }
        private static double CurrentSPSAvg(int seconds)
        {
            int n = seconds < HistoryWidth ? seconds : HistoryWidth; int sum = 0, cnt = 0;
            for (int i = 0; i < n; i++) { int idx = (_rateIndex - 1 - i + HistoryWidth) % HistoryWidth; sum += _rateHistory[idx]; cnt++; }
            return cnt > 0 ? (double)sum / cnt : 0.0;
        }

        // ===================== Stats I/O + trackers =====================
        private static int ReadStat(Stat id)
        {
            if ((int)id == PseudoXpPercentId) { int p = TryReadXpPercent(); return p >= 0 ? p : 0; }
            try
            {
                var lp = DynelManager.LocalPlayer; if (lp == null) return 0;
                var statsProp = lp.GetType().GetProperty("Stats", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (statsProp != null)
                {
                    var statsObj = statsProp.GetValue(lp, null);
                    if (statsObj != null)
                    {
                        var indexer = statsObj.GetType().GetProperty("Item", new[] { typeof(Stat) });
                        if (indexer != null)
                        {
                            var statObj = indexer.GetValue(statsObj, new object[] { id });
                            if (statObj != null)
                            {
                                var valProp = statObj.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (valProp != null)
                                {
                                    object boxed = valProp.GetValue(statObj, null);
                                    if (boxed is int) return (int)boxed;
                                    int iv; if (boxed != null && int.TryParse(boxed.ToString(), out iv)) return iv;
                                }
                            }
                        }
                    }
                }
                var getStat = lp.GetType().GetMethod("GetStat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Stat) }, null);
                if (getStat != null)
                {
                    var statObj = getStat.Invoke(lp, new object[] { id });
                    if (statObj != null)
                    {
                        var valProp = statObj.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (valProp != null)
                        {
                            object boxed = valProp.GetValue(statObj, null);
                            if (boxed is int) return (int)boxed;
                            int iv; if (boxed != null && int.TryParse(boxed.ToString(), out iv)) return iv;
                        }
                    }
                }
            }
            catch { }
            return 0;
        }
        private static int TryReadXpPercent()
        {
            try
            {
                var lp = DynelManager.LocalPlayer; if (lp == null) return -1;
                string[] props = { "LevelProgress", "LevelPercent", "XpPercent", "XPPercent", "PercentToNextLevel" };
                for (int i = 0; i < props.Length; i++)
                {
                    var p = lp.GetType().GetProperty(props[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null)
                    {
                        object v = p.GetValue(lp, null);
                        if (v is float) return ClampInt((int)Math.Round((float)v), 0, 100);
                        if (v is double) return ClampInt((int)Math.Round((double)v), 0, 100);
                        int iv; if (v != null && int.TryParse(v.ToString(), out iv)) return ClampInt(iv, 0, 100);
                    }
                }

                string[] statNames = { "LevelProgress", "LevelProgressPercent", "XPPercent", "XpPercent" };
                for (int i = 0; i < statNames.Length; i++)
                {
                    try { var sid = (Stat)Enum.Parse(typeof(Stat), statNames[i], true); int val = ReadStat(sid); if (val > 0 && val <= 1000) { if (val > 100) val = (int)Math.Round(val / 10.0); return ClampInt(val, 0, 100); } } catch { }
                }

                string[] numerNames = { "XPThisLevel", "ExperienceThisLevel", "LevelXPProgress" };
                string[] denomNames = { "XPToNextLevel", "ExperienceToNextLevel", "NextLevelXP", "XPNecessary" };
                int numer = -1, denom = -1;
                for (int i = 0; i < numerNames.Length && numer < 0; i++) { try { var sid = (Stat)Enum.Parse(typeof(Stat), numerNames[i], true); numer = ReadStat(sid); } catch { } }
                for (int i = 0; i < denomNames.Length && denom < 0; i++) { try { var sid = (Stat)Enum.Parse(typeof(Stat), denomNames[i], true); denom = ReadStat(sid); } catch { } }
                if (numer >= 0 && denom > 0) { int pct = (int)Math.Round(100.0 * numer / denom); return ClampInt(pct, 0, 100); }
            }
            catch { }
            return -1;
        }
        private static Stat[] AllStatIds() { try { return (Stat[])Enum.GetValues(typeof(Stat)); } catch { return new Stat[] { Stat.Strength, Stat.Agility, Stat.Stamina, Stat.Sense, Stat.Intelligence, Stat.Psychic }; } }
        private static void TryCaptureAllBaselines() { _injectionSnapshot.Clear(); foreach (var id in AllStatIds()) _injectionSnapshot[id] = ReadStat(id); _injectionSnapshot[PseudoXpPercent] = ReadStat(PseudoXpPercent); }
        private static void CaptureStatBaseline() { _baselineStats.Clear(); _lastPrintedStats.Clear(); for (int i = 0; i < _tracked.Count; i++) { var id = _tracked[i].Id; int baseVal = _injectionSnapshot.ContainsKey(id) ? _injectionSnapshot[id] : ReadStat(id); _baselineStats[id] = baseVal; _lastPrintedStats[id] = baseVal; } }
        private static string BuildStatTable(out bool anyGain)
        {
            if (_autoTrack) TryAutoDiscoverChangedStats();
            anyGain = false; var rows = new List<string>(); int aliasW = 0; for (int i = 0; i < _tracked.Count; i++) if (_tracked[i].Alias.Length > aliasW) aliasW = _tracked[i].Alias.Length;
            for (int i = 0; i < _tracked.Count; i++)
            {
                var id = _tracked[i].Id; string name = _tracked[i].Alias; int now = ReadStat(id);
                int baseVal = _baselineStats.ContainsKey(id) ? _baselineStats[id] : now; int last = _lastPrintedStats.ContainsKey(id) ? _lastPrintedStats[id] : now;
                int deltaTotal = now - baseVal; int deltaSince = now - last; if (deltaTotal != 0 || deltaSince != 0) anyGain = true;
                string totalTxt = deltaTotal > 0 ? ("+" + deltaTotal) : (deltaTotal < 0 ? deltaTotal.ToString() : "0");
                string sinceTxt = deltaSince > 0 ? ("+" + deltaSince) : (deltaSince < 0 ? deltaSince.ToString() : "0");
                rows.Add(PadRight(name, aliasW) + "  " + PadLeft(totalTxt, 4) + "  (" + PadLeft(sinceTxt, 3) + ")");
                _lastPrintedStats[id] = now;
            }
            return string.Join("\n", rows.ToArray());
        }
        private static void TryAutoDiscoverChangedStats()
        {
            try
            {
                foreach (var sid in AllStatIds())
                {
                    int now = ReadStat(sid);
                    int baseVal = _injectionSnapshot.ContainsKey(sid) ? _injectionSnapshot[sid] : now;
                    if (now != baseVal && !IsTracked(sid))
                    {
                        string alias = Enum.IsDefined(typeof(Stat), sid) ? Enum.GetName(typeof(Stat), sid) : ("S" + ((int)sid));
                        int suffix = 1; for (; ; ) { bool clash = false; for (int i = 0; i < _tracked.Count; i++) if (string.Equals(_tracked[i].Alias, alias, StringComparison.OrdinalIgnoreCase)) { clash = true; break; } if (!clash) break; alias = alias + "_" + suffix; suffix++; }
                        _tracked.Add(new Tracked(sid, alias)); _baselineStats[sid] = baseVal; _lastPrintedStats[sid] = baseVal;
                    }
                }
            }
            catch { }
        }
        private static bool IsTracked(Stat sid) { for (int i = 0; i < _tracked.Count; i++) if (_tracked[i].Id.Equals(sid)) return true; return false; }
        private static string PadRight(string s, int w) { if (s == null) s = ""; if (s.Length >= w) return s; return s + new string(' ', w - s.Length); }
        private static string PadLeft(string s, int w) { if (s == null) s = ""; if (s.Length >= w) return s; return new string(' ', w - s.Length) + s; }

        // ===================== XP Tracker =====================
        private void HandleXpm(string[] args)
        {
            if (args.Length == 1)
            {
                AscUi.Title("XP Tracker");
                AscUi.KV("Status", _xpmEnabled ? AscUi.Ok("ON") : AscUi.Muted("off"), "use: " + AscUi.Cmd("/asc xpm on|off"));
                AscUi.KV("Views", "graph (sparkline), bars (ASCII chart), clear");
                Chat.WriteLine(AscUi.Bul("Reads XPThisLevel / XPToNext via reflection. No chat parsing."));
                return;
            }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "on") { _xpmEnabled = true; _xpmPollTimer = 0f; _xpmReady = false; _xpEvtCount = 0; _xpEvtHead = 0; _lastXpThisLevel = -1; _lastXpToNext = -1; Out("[Ascension] XP tracker ON."); return; }
            if (sub == "off") { _xpmEnabled = false; Out("[Ascension] XP tracker OFF."); return; }
            if (sub == "graph") { PrintXpmSparkline(); return; }
            if (sub == "bars") { PrintXpmBars(20, 30); return; } // last 20 kills, bar width 30
            if (sub == "clear") { _xpEvtCount = 0; _xpEvtHead = 0; Out("[Ascension] XP history cleared."); return; }
            Out("[Ascension] /asc xpm on|off|graph|bars|clear");
        }
        private static void PollXpPerKill()
        {
            int cur, toNext; if (!TryReadXpPair(out cur, out toNext)) return;
            if (!_xpmReady) { _lastXpThisLevel = cur; _lastXpToNext = toNext; _xpmReady = true; return; }

            int delta;
            if (cur >= _lastXpThisLevel) delta = cur - _lastXpThisLevel;
            else { int remainderBefore = Math.Max(0, _lastXpToNext - _lastXpThisLevel); delta = remainderBefore + cur; }

            if (delta > 0)
            {
                PushXpEvent(delta);
                Chat.WriteLine(AscUi.Badge("XP", AscUi.Palette.Success) + " +" + AscUi.Val(delta.ToString()) + " " + AscUi.Muted("(kill/reward)"));
            }

            _lastXpThisLevel = cur; _lastXpToNext = toNext;
        }
        private static bool TryReadXpPair(out int cur, out int toNext)
        {
            cur = -1; toNext = -1;
            string[] numerNames = { "XPThisLevel", "ExperienceThisLevel", "LevelXPProgress" };
            string[] denomNames = { "XPToNextLevel", "ExperienceToNextLevel", "NextLevelXP", "XPNecessary" };
            for (int i = 0; i < numerNames.Length && cur < 0; i++) { try { var sid = (Stat)Enum.Parse(typeof(Stat), numerNames[i], true); cur = ReadStat(sid); } catch { } }
            for (int i = 0; i < denomNames.Length && toNext < 0; i++) { try { var sid = (Stat)Enum.Parse(typeof(Stat), denomNames[i], true); toNext = ReadStat(sid); } catch { } }
            return (cur >= 0 && toNext > 0);
        }
        private static void PushXpEvent(int value)
        {
            int idx = (_xpEvtHead + _xpEvtCount) % XpmHistory;
            if (_xpEvtCount == XpmHistory) { _xpEvtHead = (_xpEvtHead + 1) % XpmHistory; idx = (_xpEvtHead + _xpEvtCount - 1 + XpmHistory) % XpmHistory; }
            else _xpEvtCount++;
            _xpEvents[idx] = value;
        }
        private static void PrintXpmSparkline()
        {
            AscUi.Title("XP per Kill — Sparkline");
            if (_xpEvtCount == 0) { Chat.WriteLine(AscUi.Muted("No XP events yet. Enable with /asc xpm on and kill something.")); return; }
            int[] tmp = CopyXpEvents();
            int sum = 0, mx = 0, mn = int.MaxValue; for (int i = 0; i < tmp.Length; i++) { int v = tmp[i]; sum += v; if (v > mx) mx = v; if (v < mn) mn = v; }
            double avg = (double)sum / tmp.Length;
            Chat.WriteLine(AscUi.Spark(tmp, Math.Min(tmp.Length, 40)));
            Chat.WriteLine(AscUi.KvInline("avg", AscUi.Val(((int)Math.Round(avg)).ToString())) + "  " + AscUi.KvInline("max", AscUi.Val(mx.ToString())) + "  " + AscUi.KvInline("min", AscUi.Val(mn.ToString())) + "  " + AscUi.KvInline("events", AscUi.Val(tmp.Length.ToString())));
        }
        private static void PrintXpmBars(int lastN, int barWidth)
        {
            AscUi.Title("XP per Kill — Bars (last " + lastN + ")");
            if (_xpEvtCount == 0) { Chat.WriteLine(AscUi.Muted("No XP events yet.")); return; }
            int[] tmp = CopyXpEvents();
            int n = tmp.Length; int start = n > lastN ? n - lastN : 0;
            int max = 0; for (int i = start; i < n; i++) if (tmp[i] > max) max = tmp[i]; if (max <= 0) { Chat.WriteLine(AscUi.Muted("(all zero)")); return; }
            for (int i = start; i < n; i++)
            {
                int val = tmp[i];
                int prev = (i > 0) ? tmp[i - 1] : val;
                double pct = prev > 0 ? ((double)(val - prev) / prev) * 100.0 : 0.0;
                int fill = (int)Math.Round((double)val / max * barWidth);
                string filled = new string('#', Math.Max(0, fill));
                string empty = new string('.', Math.Max(0, barWidth - fill));
                string bar = "[" + AscUi.Color(filled, AscUi.Palette.Accent) + AscUi.Color(empty, AscUi.Palette.Muted) + "]";
                string idx = (i + 1).ToString().PadLeft(2);
                string pctTxt = (pct >= 0 ? "+" : "") + ((int)Math.Round(pct)).ToString() + "%";
                Chat.WriteLine(" " + AscUi.Badge(idx, AscUi.Palette.Muted) + " " + bar + " " + AscUi.Val(val.ToString()) + "  " + AscUi.Muted("(Δ " + pctTxt + ")"));
            }
        }
        private static int[] CopyXpEvents()
        {
            int[] tmp = new int[_xpEvtCount]; for (int i = 0; i < _xpEvtCount; i++) { int idx = (_xpEvtHead + i) % XpmHistory; tmp[i] = _xpEvents[idx]; }
            return tmp;
        }

        // ===================== Delay / util =====================
        private static int ClampInt(int v, int min, int max) { if (v < min) return min; if (v > max) return max; return v; }
        private static void MaybeWarnLowLatency(int ms) { if (ms < 150) Out("[Ascension] Warning: ultra-low latency (<150ms) may desync. Try /asc cal."); }
        private static void SetDelayAndInterval(int ms) { int v = ClampInt(ms, 2, 5000); if (v != _delayMs) _changesDelaySets++; _delayMs = v; _autoIntervalMs = v; MaybeWarnLowLatency(v); }

        // ===================== License Commands =====================
        private void HandleLicense(string[] args)
        {
            if (args.Length == 1)
            {
                Out("[Ascension] license status | license apply <base64> | license begin|add|commit|cancel | license reload");
                Out("[Ascension] Tip: If chat truncates long messages, use chunked mode: begin → add → add → commit.");
                return;
            }
            string sub = (args[1] ?? "").ToLowerInvariant();
            if (sub == "status") { Out("[Ascension] " + License.Status()); TimeSpan _lf; if (License.TryGetTimeLeft(out _lf)) Out("[Ascension] License time left: " + License.TimeLeftShort()); if (!string.IsNullOrEmpty(_licPasteBuffer)) Out("[Ascension] (paste-buffer has " + _licPasteBuffer.Length + " chars)"); return; }
            if (sub == "apply" && args.Length >= 3) { string joined = JoinFrom(args, 2); License.ApplyFromPaste(joined); Out("[Ascension] " + License.Status()); if (License.IsUnlocked) PrintUnlockBanner(); return; }
            if (sub == "begin") { _licPasteBuffer = ""; Out("[Ascension] Paste-buffer cleared. Use: /asc license add <chunk> (repeat), then /asc license commit"); return; }
            if (sub == "add" && args.Length >= 3) { string chunk = JoinFrom(args, 2); chunk = License.SanitizeBase64(chunk); _licPasteBuffer += chunk; Out("[Ascension] Added chunk. Buffer length = " + _licPasteBuffer.Length + " chars."); return; }
            if (sub == "commit") { if (string.IsNullOrEmpty(_licPasteBuffer)) { Out("[Ascension] Paste-buffer is empty. Use: /asc license add <chunk>"); return; } License.ApplyFromPaste(_licPasteBuffer); Out("[Ascension] " + License.Status()); if (License.IsUnlocked) PrintUnlockBanner(); return; }
            if (sub == "cancel") { _licPasteBuffer = ""; Out("[Ascension] Paste-buffer cancelled."); return; }
            if (sub == "reload") { License.TryLoadFromDisk(); Out("[Ascension] " + License.Status()); if (License.IsUnlocked) PrintUnlockBanner(); return; }
            Out("[Ascension] license status | license apply <base64> | license begin|add|commit|cancel | license reload");
        }
        private static string JoinFrom(string[] parts, int start) { if (parts == null || start >= parts.Length) return ""; var sb = new StringBuilder(256); for (int i = start; i < parts.Length; i++) sb.Append(parts[i]); return sb.ToString(); }
        private static void PrintStatus()
        {
            string cyclesText = _autoCyclesRemaining < 0 ? "∞" : _autoCyclesRemaining.ToString();
            int xp = TryReadXpPercent(); string xpTxt = (xp >= 0) ? (xp + "%") : "n/a";
            string licText; TimeSpan left; if (License.TryGetTimeLeft(out left)) licText = "unlocked (" + License.TimeLeftShort() + " left)"; else licText = "locked";
            AscUi.Title("Status");
            AscUi.KV("slot", _slotToUse.ToString());
            AscUi.KV("turbo", _turbo ? AscUi.Ok("ON") : AscUi.Muted("off"), "burst=" + _turboBurst);
            AscUi.KV("delay/interval", _delayMs + "ms");
            AscUi.KV("auto", _autoEnabled ? AscUi.Ok("ON") : AscUi.Muted("off"), "cycles=" + cyclesText);
            AscUi.KV("swaps", _totalSwaps.ToString());
            AscUi.KV("XP%", xpTxt);
            AscUi.KV("license", licText);
            if (License.IsUnlocked) { Chat.WriteLine(AscUi.Muted(Warn1)); Chat.WriteLine(AscUi.Muted(Warn2)); }
        }
        private static void PrintUnlockBanner()
        {
            Out("[Ascension] Unlocked."); TimeSpan left; if (License.TryGetTimeLeft(out left)) Out("[Ascension] License time left: " + License.TimeLeftShort() + " (auto-locks at expiry)."); Out(Warn1); Out(Warn2);
        }

        // ===================== UI: /ascend (ASCII logo) =====================
        public static class AscensionAscii
        {
            private static bool _useColor = true; private static string _theme = "notum"; private static int _targetWidth = 92;
            private struct Theme { public string Start, End, Accent; public Theme(string s, string e, string a) { Start = s; End = e; Accent = a; } }
            private static readonly Dictionary<string, Theme> _themes = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase)
            {
                {"notum",   new Theme("#44E1FF","#1A66FF","#90E7FF")},
                {"neon",    new Theme("#FF6BFF","#7A00FF","#FFD0FF")},
                {"gold",    new Theme("#FFB400","#FF7A00","#FFE28A")},
                {"emerald", new Theme("#61FF9E","#008C5A","#B9FFD7")},
                {"sunset",  new Theme("#FF7EB3","#FF6A00","#FFD3E6")},
                {"steel",   new Theme("#CFE3FF","#6A7C99","#E7F1FF")},
                {"mono",    new Theme("#FFFFFF","#FFFFFF","#FFFFFF")},
                {"amber",   new Theme("#FFC94D","#FF8C00","#FFF1B8")},
                {"violet",  new Theme("#B28DFF","#4C2FFF","#E3D7FF")}
            };
            public static void Install()
            {
                MethodInfo[] methods = typeof(Chat).GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i]; if (m.Name != "RegisterCommand") continue; var ps = m.GetParameters(); if (ps.Length != 2) continue; Type t = ps[1].ParameterType;
                    try
                    {
                        if (t == typeof(Action<string, string[], ChatWindow>)) { Action<string, string[], ChatWindow> h3 = Handle3; m.Invoke(null, new object[] { "ascend", h3 }); break; }
                        if (t == typeof(Action<string, string[]>)) { Action<string, string[]> h2 = Handle2; m.Invoke(null, new object[] { "ascend", h2 }); break; }
                    }
                    catch { }
                }
                Chat.WriteLine("[Ascension] /ascend ready. (Try: /ascend theme list | /ascend theme gold | /ascend color off)");
            }
            public static void Uninstall()
            {
                var mi = typeof(Chat).GetMethod("UnregisterCommand", BindingFlags.Public | BindingFlags.Static); if (mi == null) return;
                try { Action<string, string[], ChatWindow> h3 = Handle3; mi.Invoke(null, new object[] { "ascend", h3 }); } catch { }
                try { Action<string, string[]> h2 = Handle2; mi.Invoke(null, new object[] { "ascend", h2 }); } catch { }
            }
            private static void Handle3(string cmd, string[] args, ChatWindow win) { Dispatch(args); }
            private static void Handle2(string cmd, string[] args) { Dispatch(args); }
            private static void Dispatch(string[] args)
            {
                if (args != null && args.Length > 0)
                {
                    string sub = (args[0] ?? "").ToLowerInvariant();
                    if (sub == "theme")
                    {
                        if (args.Length == 1 || string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase))
                        {
                            var keys = new List<string>(); foreach (var kv in _themes) keys.Add(kv.Key); keys.Sort(StringComparer.OrdinalIgnoreCase);
                            var sb = new StringBuilder(); for (int i = 0; i < keys.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(keys[i]); }
                            Chat.WriteLine("[Ascension] Themes: " + sb.ToString()); return;
                        }
                        string t = args[1]; if (_themes.ContainsKey(t)) { _theme = t; Chat.WriteLine("[Ascension] Theme = " + _theme + "."); } else Chat.WriteLine("[Ascension] Unknown theme. Use /ascend theme list"); return;
                    }
                    if (sub == "color" && args.Length >= 2) { string v = (args[1] ?? "").ToLowerInvariant(); _useColor = (v == "on" || v == "true" || v == "1"); Chat.WriteLine("[Ascension] Colors " + (_useColor ? "ON" : "OFF") + "."); return; }
                    if (sub == "width" && args.Length >= 2) { int n; if (int.TryParse(args[1], out n)) { _targetWidth = Clamp(n, 60, 140); Chat.WriteLine("[Ascension] Width = " + _targetWidth + "."); } else Chat.WriteLine("[Ascension] Usage: /ascend width <60..140>"); return; }
                    if (sub == "preview") { PreviewPalette(); return; }
                }
                PrintArt();
            }
            private static void PrintArt()
            {
                string[] lines = _art.Split('\n'); var payload = new List<string>(); for (int i = 0; i < lines.Length; i++) { string l = lines[i].TrimEnd('\r'); if (l.Length > 0) payload.Add(l); }
                int n = payload.Count; Theme th = _themes.ContainsKey(_theme) ? _themes[_theme] : _themes["notum"]; int j = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string raw = lines[i].TrimEnd('\r'); if (raw.Length == 0) { Chat.WriteLine(" "); continue; }
                    string centered = Center(raw, _targetWidth);
                    if (!_useColor || _theme == "mono") { Chat.WriteLine(centered); }
                    else { string color = LerpColor(th.Start, th.End, (n <= 1) ? 0f : (float)j / (n - 1)); Chat.WriteLine(WrapColor(centered, color)); }
                    j++;
                }
                string cap = Center("A  S  C  E  N  S  I  O  N", _targetWidth); Chat.WriteLine(_useColor ? WrapColor(cap, th.Accent) : cap);
            }
            private static void PreviewPalette()
            {
                Theme th = _themes.ContainsKey(_theme) ? _themes[_theme] : _themes["notum"];
                Chat.WriteLine("[Ascension] Preview (" + _theme + "):");
                for (int i = 0; i < 24; i++) { float t = (float)i / 23f; string swatch = new string('█', 24); string line = Center(swatch, _targetWidth); Chat.WriteLine(_useColor ? WrapColor(line, LerpColor(th.Start, th.End, t)) : line); }
                string accent = Center("▲ Accent", _targetWidth); Chat.WriteLine(_useColor ? WrapColor(accent, th.Accent) : accent);
            }
            private static string Center(string s, int width) { int pad = Math.Max(0, (width - s.Length) / 2); return (pad > 0 ? new string(' ', pad) : "") + s; }
            private static int Clamp(int v, int min, int max) { if (v < min) return min; if (v > max) return max; return v; }
            private static string WrapColor(string text, string hex) { return "<font color='" + hex + "'>" + text + "</font>"; }
            private static string LerpColor(string hexA, string hexB, float t) { int r1, g1, b1; ParseHex(hexA, out r1, out g1, out b1); int r2, g2, b2; ParseHex(hexB, out r2, out g2, out b2); int r = (int)Math.Round(r1 + (r2 - r1) * t); int g = (int)Math.Round(g1 + (g2 - g1) * t); int b = (int)Math.Round(b1 + (b2 - b1) * t); return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2"); }
            private static void ParseHex(string hex, out int r, out int g, out int b) { string h = hex.StartsWith("#") ? hex.Substring(1) : hex; r = Convert.ToInt32(h.Substring(0, 2), 16); g = Convert.ToInt32(h.Substring(2, 2), 16); b = Convert.ToInt32(h.Substring(4, 2), 16); }
            private static readonly string _art = @"
                                      ^                                      
                                     / \                                     
                                    /   \                                    
                                   /     \                                   
                                  /       \                                  
                                 /         \                                 
                                /           \                                
                               /             \                               
                              /               \                              
                             /   ___________   \                             
                            /   /           \   \                            
                           /   /  /\     /\  \   \                           
                          /   /  /  \   /  \  \   \                          
                         /   /  /    \ /    \  \   \                         
                        /   /  /      V      \  \   \                        
                       /   /  /               \  \   \                       
                      /   /  /                 \  \   \                      
                     /   /  /                   \  \   \                     
                    /   /  /                     \  \   \                    
                   /   /  /                       \  \   \                   
                  /   /  /                         \  \   \                  
                 /   /  /                           \  \   \                 
                /   /  /                             \  \   \                
               /   /  /                               \  \   \               
              /   /  /                                 \  \   \              
             /   /  /                                   \  \   \             
            /   /  /_____________________________________\  \   \            
            \   \ /                                       \ /   /            
             \   V                                         V   /             
              \         __________     _____     __________         /        
               \       /          \   /     \   /          \       /        
                \     /   /\  /\   \ /  /\   \ /   /\  /\   \     /         
                 \   /   /  \/  \   V  /  \   V   /  \/  \   \   /          
                  \ /   /         \   /    \     /         \   \ /           
                   V   \           \ /      \   /           /   V            
                   /\   \           V  /\    V /           /   /\            
                  /  \   \            /  \    /           /   /  \           
                 / /\ \   \          / /\ \  /           /   / /\ \          
                / /  \ \   \        / /  \ \/           /   / /  \ \         
               /_/____\_\   \______/ /____\ \__________/   /_/____\_\        
                 \      /            \      /            \      /            
                  \    /              \    /              \    /             
                   \  /                \  /                \  /              
                    \/                  \/                  \/               
";
        }

        // ===================== UI: /style (themes + helpers) =====================
        public static class AscUi
        {
            public static bool Enabled = true; public static string Theme = "notum-pro"; public static int Width = 92;

            public struct Pal
            {
                public string Primary, Accent, Success, Warning, Error, Muted, GradStart, GradEnd;
                public Pal(string p, string a, string s, string w, string e, string m, string g1, string g2) { Primary = p; Accent = a; Success = s; Warning = w; Error = e; Muted = m; GradStart = g1; GradEnd = g2; }
            }

            public static readonly Dictionary<string, Pal> Themes = new Dictionary<string, Pal>(StringComparer.OrdinalIgnoreCase)
            {
                {"notum-pro", new Pal("#A7F3FF","#24B3FF","#7CFC98","#FFD166","#FF6B6B","#9AA4B2","#7CE9FF","#1F7CFF")},
                {"gold",      new Pal("#FFE9A3","#FFC436","#7CFC98","#FFD166","#FF6B6B","#9AA4B2","#FFD36E","#FF8C1F")},
                {"mint",      new Pal("#BEFFE2","#36E3A4","#7CFC98","#FFD166","#FF6B6B","#9AA4B2","#9CFFD0","#1FB784")},
                {"rose",      new Pal("#FFD6E7","#FF6BAA","#7CFC98","#FFD166","#FF6B6B","#9AA4B2","#FF9CC7","#C71585")},
                {"steel",     new Pal("#E6EEF9","#7AA0D8","#7CFC98","#FFD166","#FF6B6B","#9AA4B2","#CFE3FF","#6A7C99")},
                {"mono",      new Pal("#FFFFFF","#FFFFFF","#FFFFFF","#FFFFFF","#FFFFFF","#CCCCCC","#FFFFFF","#FFFFFF")},
                {"amber",     new Pal("#FFE39A","#FF9D00","#7CFC98","#FFD166","#FF6B6B","#9AA4B2","#FFD36E","#FF8C1F")},
                {"violet",    new Pal("#E0D7FF","#7F6BFF","#7CFC98","#FFD166","#FF6B6B","#9AA4B2","#C9B8FF","#5A47FF")}
            };
            public static Pal Palette { get { Pal p; if (Themes.TryGetValue(Theme, out p)) return p; return Themes["notum-pro"]; } }

            public static void Install()
            {
                MethodInfo[] methods = typeof(Chat).GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i]; if (m.Name != "RegisterCommand") continue; var ps = m.GetParameters(); if (ps.Length != 2) continue; Type t = ps[1].ParameterType;
                    try
                    {
                        if (t == typeof(Action<string, string[], ChatWindow>)) { Action<string, string[], ChatWindow> h3 = Handle3; m.Invoke(null, new object[] { "style", h3 }); break; }
                        if (t == typeof(Action<string, string[]>)) { Action<string, string[]> h2 = Handle2; m.Invoke(null, new object[] { "style", h2 }); break; }
                    }
                    catch { }
                }
                Chat.WriteLine(Prefix() + "UI ready. " + Cmd("/style theme list") + " | " + Cmd("/style preview"));
            }
            private static void Handle3(string cmd, string[] args, ChatWindow _) { Dispatch(args); }
            private static void Handle2(string cmd, string[] args) { Dispatch(args); }
            private static void Dispatch(string[] args)
            {
                if (args == null || args.Length == 0)
                {
                    Title("Style");
                    KV("Theme", Theme, "change: " + Cmd("/style theme <name>"));
                    KV("Colors", Enabled ? Ok("ON") : Muted("off"), "toggle: " + Cmd("/style color on|off"));
                    KV("Width", Width.ToString(), "adjust: " + Cmd("/style width <n>"));
                    Chat.WriteLine(Bul("Try " + Cmd("/style theme list") + " or " + Cmd("/style preview")));
                    return;
                }
                string sub = (args[0] ?? "").ToLowerInvariant();
                if (sub == "theme")
                {
                    if (args.Length == 1 || (args.Length >= 2 && string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase)))
                    {
                        Title("Themes");
                        var keys = new List<string>(); foreach (var kv in Themes) keys.Add(kv.Key); keys.Sort(StringComparer.OrdinalIgnoreCase);
                        var sb = new StringBuilder(); for (int i = 0; i < keys.Count; i++) { string k = keys[i]; if (i > 0) sb.Append("  "); if (k.Equals(Theme, StringComparison.OrdinalIgnoreCase)) sb.Append(Badge(k, Palette.Accent)); else sb.Append(k); }
                        Chat.WriteLine(sb.ToString()); return;
                    }
                    string name = args[1]; if (Themes.ContainsKey(name)) { Theme = name; Chat.WriteLine(Prefix() + "Theme set to " + Val(name) + "."); } else Chat.WriteLine(Prefix() + "Unknown theme. Try " + Cmd("/style theme list")); return;
                }
                if (sub == "color" && args.Length >= 2) { string v = (args[1] ?? "").ToLowerInvariant(); Enabled = (v == "on" || v == "true" || v == "1"); Chat.WriteLine(Prefix() + "Colors " + (Enabled ? Ok("ON") : Muted("off")) + "."); return; }
                if (sub == "width" && args.Length >= 2) { int n; if (int.TryParse(args[1], out n)) { Width = Clamp(n, 60, 140); Chat.WriteLine(Prefix() + "Width set to " + Val(Width.ToString()) + "."); } else Chat.WriteLine(Prefix() + "Usage: " + Cmd("/style width <60..140>")); return; }
                if (sub == "preview")
                {
                    Title("Palette Preview (" + Theme + ")");
                    for (int i = 0; i < 18; i++) { float t = (float)i / 17f; string swatch = new string('█', 24); Chat.WriteLine(Color(Center(swatch, Width), Lerp(Palette.GradStart, Palette.GradEnd, t))); }
                    Chat.WriteLine(Badge("Primary", Palette.Primary) + " " + Badge("Accent", Palette.Accent) + " " + Badge("Success", Palette.Success) + " " + Badge("Warn", Palette.Warning) + " " + Badge("Error", Palette.Error) + " " + Badge("Muted", Palette.Muted));
                    return;
                }
                Chat.WriteLine(Prefix() + "Unknown /style command.");
            }

            // Pretty printers
            public static void Title(string text) { string mid = " " + text + " "; string bar = new string('─', Math.Max(8, Math.Min(Width - mid.Length, 34))); string line = Center(bar + mid + bar, Width); Chat.WriteLine(Color(line, Palette.Accent)); }
            public static void Divider() { Chat.WriteLine(Color(Center(new string('-', Math.Min(Width, 60)), Width), Palette.Muted)); }
            public static void KV(string key, string value, string hint = null) { string line = Badge(key, Palette.Accent) + " " + Val(value); if (!string.IsNullOrEmpty(hint)) line += "  " + Muted(hint); Chat.WriteLine(line); }
            public static string Bar(int width, int done, int total)
            {
                if (width < 4) width = 4; double pct = (total > 0) ? Math.Max(0, Math.Min(1, (double)done / total)) : 0; int fill = (total > 0) ? (int)Math.Round(pct * width) : (done % (width + 1));
                string filled = new string('#', Math.Max(0, fill)); string empty = new string('.', Math.Max(0, width - fill));
                string bar = "[" + Color(filled, Palette.Accent) + Color(empty, Palette.Muted) + "]"; string tail = (total > 0) ? (" " + ((int)Math.Round(pct * 100)).ToString() + "% (" + done + "/" + total + ")") : " ∞";
                return Center(bar + tail, Width);
            }
            public static string Bul(string text) { return Color("• ", Palette.Accent) + text; }

            // Tokens
            public static string Ok(string t) { return Color(t, Palette.Success); }
            public static string Warn(string t) { return Color(t, Palette.Warning); }
            public static string Err(string t) { return Color(t, Palette.Error); }
            public static string Val(string t) { return Color(t, Palette.Primary); }
            public static string Muted(string t) { return Color(t, Palette.Muted); }
            public static string Cmd(string t) { return Color(t, Palette.Accent); }
            public static string Badge(string t, string hex) { return Color("[" + t + "]", hex); }
            public static void WarnLine(string t) { Chat.WriteLine(Badge("WARN", Palette.Warning) + " " + t); }

            // Extra helpers for XP tracker
            public static string KvInline(string key, string value) { return Badge(key, Palette.Accent) + " " + value; }
            public static string Spark(int[] values, int width)
            {
                if (values == null || values.Length == 0) return Muted("(no data)");
                int n = values.Length; if (width <= 0) width = n;
                int cols = Math.Min(width, n); double step = (double)n / cols; int max = 0; for (int i = 0; i < n; i++) if (values[i] > max) max = values[i]; if (max <= 0) return Muted("(all zero)");
                string levels = "▁▂▃▄▅▆▇█"; var sb = new StringBuilder(cols);
                for (int c = 0; c < cols; c++) { int idx = (int)Math.Floor(c * step); if (idx >= n) idx = n - 1; double t = (double)values[idx] / max; int li = (int)Math.Round(t * (levels.Length - 1)); if (li < 0) li = 0; if (li >= levels.Length) li = levels.Length - 1; sb.Append(levels[li]); }
                return Color(sb.ToString(), Palette.Accent);
            }

            // Core color helpers
            public static string Prefix() { return Color("[Ascension] ", Palette.Accent); }
            public static string Color(string text, string hex) { return Enabled ? "<font color='" + hex + "'>" + text + "</font>" : text; }

            // Utilities
            private static int Clamp(int v, int min, int max) { if (v < min) return min; if (v > max) return max; return v; }
            private static string Center(string s, int width) { int pad = Math.Max(0, (width - s.Length) / 2); return (pad > 0 ? new string(' ', pad) : "") + s; }
            private static string Lerp(string a, string b, float t) { int r1, g1, b1; ParseHex(a, out r1, out g1, out b1); int r2, g2, b2; ParseHex(b, out r2, out g2, out b2); int r = (int)Math.Round(r1 + (r2 - r1) * t); int g = (int)Math.Round(g1 + (g2 - g1) * t); int bl = (int)Math.Round(b1 + (b2 - b1) * t); return "#" + r.ToString("X2") + g.ToString("X2") + bl.ToString("X2"); }
            private static void ParseHex(string hex, out int r, out int g, out int b) { string h = hex.StartsWith("#") ? hex.Substring(1) : hex; r = Convert.ToInt32(h.Substring(0, 2), 16); g = Convert.ToInt32(h.Substring(2, 2), 16); b = Convert.ToInt32(h.Substring(4, 2), 16); }
        }

        // ===================== License (rolling 10-minute window) =====================
        internal static class License
        {
            private const string PublicKeyXml =
@"<RSAKeyValue><Modulus>56ls1zZxQTLw4NYv03y67MkdOC8M8AUuJIiy6pvmTilOnsMd25uDQMt/YIINyoXjPwfol/+bplp9RDVYZvNVvh80eFhPhxutYHZIFPi2RRMVcGtqehrMxVUSrp1IA9XURlpR9ZF0IpG8oB4t86xVSJrEbE29GD5LY3HdfZ0TUfk64eosx1dzcJgPWRG/lZYIpG0Ai2lryScLpYsQb7taDGdzL/uv5fnWkPWGU0/ynP12LEYZ3PBYfrKgwF/BKYP4+jmw2IFUbD9xGq6hnIsAm5QoTflTUqt2pJz1sNLN2aaWvCCnJkI1Ls2Mo+mApQSZp5oM08NYpFWrWVM5CewXWQ==</Modulus>
<Exponent>AQAB</Exponent></RSAKeyValue>";
            private const string BuildSalt = "SET-YOUR-BUILD-SALT-GUID-6F7B3B6A-AB9E-4B3F-9B55-27F7E75A1D2F";
            private const int TickWindowSec = 600;
            private static readonly string LicPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ascension", "asc.lic");

            private static bool _unlocked; private static string _reason = "No license"; private static DateTime _expiry = DateTime.MinValue; private static string _char = ""; private static string _tick = "";
            private static byte[] _dllHash0;

            public static bool IsUnlocked { get { return _unlocked; } }
            public static void Init()
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(LicPath)); } catch { }
                TryLoadFromDisk();
                try { using (var sha = SHA256.Create()) { _dllHash0 = sha.ComputeHash(File.ReadAllBytes(typeof(Main).Assembly.Location)); } } catch { }
            }
            public static void PeriodicCheck()
            {
                try { byte[] now; using (var sha = SHA256.Create()) { now = sha.ComputeHash(File.ReadAllBytes(typeof(Main).Assembly.Location)); } if (_dllHash0 != null && !EqualBytes(_dllHash0, now)) { _unlocked = false; _reason = "Tamper detected"; } } catch { }
                if (_unlocked) { string currentTick = ComputeTick(DateTime.UtcNow); if (!string.Equals(_tick, currentTick, StringComparison.Ordinal)) { _unlocked = false; _reason = "Window expired"; } }
                if (_unlocked && _expiry != DateTime.MinValue && DateTime.UtcNow > _expiry) { _unlocked = false; _reason = "Expired"; }
            }
            public static void ApplyFromPaste(string base64)
            {
                string why; string cleaned = SanitizeBase64(base64);
                if (Verify(cleaned, out why))
                { _unlocked = true; _reason = "OK"; try { var raw = Convert.FromBase64String(cleaned); File.WriteAllBytes(LicPath, raw); } catch { } }
                else { _unlocked = false; _reason = why; }
            }
            public static void TryLoadFromDisk()
            {
                try { if (!File.Exists(LicPath)) { _unlocked = false; _reason = "No license file"; return; } var raw = File.ReadAllBytes(LicPath); var base64 = Convert.ToBase64String(raw); string why; if (Verify(base64, out why)) { _unlocked = true; _reason = "OK"; } else { _unlocked = false; _reason = why; } }
                catch (Exception ex) { _unlocked = false; _reason = "Load error: " + ex.Message; }
            }
            internal static string SanitizeBase64(string s)
            {
                if (string.IsNullOrEmpty(s)) return ""; s = s.Trim(); s = s.Replace('-', '+').Replace('_', '/');
                var sb = new StringBuilder(s.Length); for (int i = 0; i < s.Length; i++) { char c = s[i]; if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=') sb.Append(c); }
                string t = sb.ToString(); int mod = t.Length % 4; if (mod != 0) t = t + new string('=', 4 - mod); return t;
            }
            private static bool Verify(string base64, out string reason)
            {
                reason = ""; try
                {
                    byte[] blob = Convert.FromBase64String(base64); int sep = IndexOf(blob, (byte)0x1E); if (sep <= 0) { reason = "Bad format"; return false; }
                    byte[] payload = new byte[sep]; CopyBytes(blob, 0, payload, 0, sep); byte[] sig = new byte[blob.Length - sep - 1]; CopyBytes(blob, sep + 1, sig, 0, sig.Length);
                    string text = Encoding.UTF8.GetString(payload);
                    string charName = GetKV(text, "char"); string expStr = GetKV(text, "exp"); string tickStr = GetKV(text, "tick");
                    if (string.IsNullOrEmpty(tickStr)) { reason = "Missing tick"; return false; }
                    DateTime expUtc; if (!TryParseUtc(expStr, out expUtc)) { DateTime expLocal; if (!DateTime.TryParse(expStr, out expLocal)) { reason = "Bad expiry"; return false; } expUtc = expLocal.ToUniversalTime(); }
                    string currentChar = DynelManager.LocalPlayer != null ? DynelManager.LocalPlayer.Name : "";
                    if (!string.IsNullOrEmpty(charName) && !string.Equals(charName, currentChar, StringComparison.OrdinalIgnoreCase)) { reason = "Character mismatch"; return false; }

                    using (var rsa = new RSACryptoServiceProvider())
                    {
                        rsa.PersistKeyInCsp = false; rsa.FromXmlString(PublicKeyXml);
                        using (var sha = SHA256.Create()) { if (!rsa.VerifyData(payload, sha, sig)) { reason = "Invalid signature"; return false; } }
                    }

                    string currentTick = ComputeTick(DateTime.UtcNow); if (!string.Equals(tickStr, currentTick, StringComparison.Ordinal)) { reason = "Stale tick (window)"; return false; }
                    if (DateTime.UtcNow > expUtc) { reason = "Expired"; return false; }
                    _char = charName; _expiry = expUtc; _tick = tickStr; return true;
                }
                catch (Exception ex) { reason = "Verify error: " + ex.Message; return false; }
            }
            public static string Status()
            {
                if (_unlocked && _expiry != DateTime.MinValue && DateTime.UtcNow > _expiry) { _unlocked = false; _reason = "Expired"; }
                if (_unlocked) { var left = TimeLeft(); return "Unlocked for '" + (_char ?? "") + "' (time left " + FormatSpan(left) + ")."; }
                return "Locked: " + _reason;
            }
            public static bool TryGetTimeLeft(out TimeSpan left) { if (!_unlocked || _expiry == DateTime.MinValue) { left = TimeSpan.Zero; return false; } left = TimeLeft(); return true; }
            public static string TimeLeftShort() { TimeSpan left; if (!TryGetTimeLeft(out left)) return "0s"; return FormatSpan(left); }
            private static TimeSpan TimeLeft()
            {
                var now = DateTime.UtcNow; var absLeft = (_expiry > now) ? (_expiry - now) : TimeSpan.Zero;
                long unix = (long)Math.Floor((now - new DateTime(1970, 1, 1)).TotalSeconds); long inWindow = TickWindowSec - (unix % TickWindowSec);
                var tickLeft = TimeSpan.FromSeconds(inWindow); return (absLeft < tickLeft) ? absLeft : tickLeft;
            }
            private static string GetKV(string text, string key) { var parts = text.Split(';'); for (int i = 0; i < parts.Length; i++) { var p = parts[i]; if (p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return p.Substring(key.Length + 1); } return ""; }
            private static string FormatSpan(TimeSpan t) { if (t <= TimeSpan.Zero) return "0s"; var sb = new StringBuilder(); if (t.Days > 0) sb.Append(t.Days).Append("d "); if (t.Hours > 0) sb.Append(t.Hours).Append("h "); if (t.Minutes > 0) sb.Append(t.Minutes).Append("m "); if (t.Seconds > 0 && t.TotalHours < 1) sb.Append(t.Seconds).Append("s"); var s = sb.ToString().Trim(); return string.IsNullOrEmpty(s) ? ((int)t.TotalSeconds) + "s" : s; }
            private static bool TryParseUtc(string s, out DateTime utc)
            {
                if (s != null && s.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) { DateTime dt; if (DateTime.TryParse(s, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt)) { utc = dt; return true; } }
                if (DateTime.TryParse(s, out utc)) { utc = utc.ToUniversalTime(); return true; }
                utc = DateTime.MinValue; return false;
            }
            private static string ComputeTick(DateTime utc)
            {
                long window = (long)Math.Floor((utc - new DateTime(1970, 1, 1)).TotalSeconds / 600.0); using (var sha = SHA256.Create()) { var bytes = Encoding.UTF8.GetBytes(BuildSalt + "|" + window.ToString()); var h = sha.ComputeHash(bytes); return BitConverter.ToString(h, 0, 6).Replace("-", ""); }
            }
            private static bool EqualBytes(byte[] a, byte[] b) { if (ReferenceEquals(a, b)) return true; if (a == null || b == null) return false; if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
            private static int IndexOf(byte[] arr, byte value) { for (int i = 0; i < arr.Length; i++) if (arr[i] == value) return i; return -1; }
            private static void CopyBytes(byte[] src, int so, byte[] dst, int d0, int count) { for (int i = 0; i < count; i++) dst[d0 + i] = src[so + i]; }
        }
    }
}
