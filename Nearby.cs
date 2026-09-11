// RubiKit 2.1 — Nearby entity tracker
// C# 7.3 / .NET 4.8 compatible
//
// Streams a live view of every player and every openable container (chests,
// corpses) around the local character while the plugin is injected: name,
// distance, position, and — for containers — their contents once opened.
// Every enter/leave is also written through Diag so it shows up in the same
// log stream as everything else.
//
// Endpoints (wired in Kernel.Handle):
//   GET /nearby/stream  -> SSE feed, one "nearby" frame per sample
//   GET /api/nearby     -> last sample as JSON

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RubiKit
{
    internal static class NearbyTracker
    {
        private static readonly HashSet<string> _prevKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object _gate = new object();

        public static string SampleJson()
        {
            var sw = Stopwatch.StartNew();
            LocalPlayer lp = null;
            string err = null;
            try { lp = DynelManager.LocalPlayer; }
            catch (Exception ex) { err = ex.Message; }

            var playerRows = new List<KeyValuePair<float, string>>();
            var containerRows = new List<KeyValuePair<float, string>>();
            var curKeys = new HashSet<string>(StringComparer.Ordinal);
            var entered = new List<string>();

            if (lp != null)
            {
                try
                {
                    foreach (var p in DynelManager.Players)
                    {
                        if (p == null || !SafeBool(() => p.IsValid)) continue;
                        if (p.Identity.Equals(lp.Identity)) continue;

                        float dist = SafeFloat(() => p.DistanceFrom(lp));
                        string key = "p:" + p.Identity.Instance;
                        curKeys.Add(key);
                        if (!_prevKeys.Contains(key))
                            entered.Add(SafeName(p) + " (player, " + dist.ToString("0") + "m)");
                        playerRows.Add(new KeyValuePair<float, string>(dist, PlayerJson(p, dist)));
                    }
                }
                catch (Exception ex) { Diag.Error("nearby", "player scan failed", ex); }

                try
                {
                    foreach (var c in DynelManager.Chests)
                        AddContainer(c, "chest", lp, curKeys, entered, containerRows);
                }
                catch (Exception ex) { Diag.Error("nearby", "chest scan failed", ex); }

                try
                {
                    foreach (var c in DynelManager.Corpses)
                        AddContainer(c, "corpse", lp, curKeys, entered, containerRows);
                }
                catch (Exception ex) { Diag.Error("nearby", "corpse scan failed", ex); }
            }

            int leftCount;
            lock (_gate)
            {
                leftCount = _prevKeys.Count(k => !curKeys.Contains(k));
                _prevKeys.Clear();
                foreach (var k in curKeys) _prevKeys.Add(k);
            }

            if (entered.Count > 0)
                Diag.Info("nearby", entered.Count + " entered range",
                    "{\"names\":[" + string.Join(",", entered.Select(Json.Str)) + "]}");
            if (leftCount > 0)
                Diag.Info("nearby", leftCount + " left range");

            playerRows.Sort((a, b) => a.Key.CompareTo(b.Key));
            containerRows.Sort((a, b) => a.Key.CompareTo(b.Key));

            sw.Stop();
            var sb = new StringBuilder(8192);
            sb.Append('{')
              .Append("\"ts\":").Append(Json.Str(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")))
              .Append(",\"havePlayer\":").Append(lp != null ? "true" : "false")
              .Append(",\"error\":").Append(Json.Str(err))
              .Append(",\"readMs\":").Append(sw.Elapsed.TotalMilliseconds.ToString("0.00"))
              .Append(",\"playerCount\":").Append(playerRows.Count)
              .Append(",\"containerCount\":").Append(containerRows.Count)
              .Append(",\"players\":[").Append(string.Join(",", playerRows.Select(r => r.Value))).Append(']')
              .Append(",\"containers\":[").Append(string.Join(",", containerRows.Select(r => r.Value))).Append(']')
              .Append('}');
            return sb.ToString();
        }

        private static void AddContainer(LockableContainer c, string kind, LocalPlayer lp,
            HashSet<string> curKeys, List<string> entered, List<KeyValuePair<float, string>> rows)
        {
            if (c == null || !SafeBool(() => c.IsValid)) return;
            float dist = SafeFloat(() => c.DistanceFrom(lp));
            string key = "c:" + kind + ":" + c.Identity.Instance;
            curKeys.Add(key);
            if (!_prevKeys.Contains(key))
                entered.Add(SafeName(c) + " (" + kind + ", " + dist.ToString("0") + "m)");
            rows.Add(new KeyValuePair<float, string>(dist, ContainerJson(c, kind, dist)));
        }

        private static string PlayerJson(AOSharp.Core.SimpleChar p, float dist)
        {
            var pos = SafePos(p);
            return "{\"id\":" + p.Identity.Instance +
                   ",\"name\":" + Json.Str(SafeName(p)) +
                   ",\"dist\":" + dist.ToString("0.0") +
                   ",\"level\":" + SafeInt(() => p.Level) +
                   ",\"profession\":" + Json.Str(SafeProfession(p)) +
                   ",\"breed\":" + Json.Str(SafeStr(() => p.Breed.ToString())) +
                   ",\"side\":" + Json.Str(SafeStr(() => p.Side.ToString())) +
                   ",\"hpPct\":" + SafeFloat(() => p.HealthPercent).ToString("0") +
                   ",\"los\":" + (SafeBool(() => p.IsInLineOfSight) ? "true" : "false") +
                   ",\"pos\":{\"x\":" + pos.X.ToString("0.0") + ",\"y\":" + pos.Y.ToString("0.0") + ",\"z\":" + pos.Z.ToString("0.0") + "}}";
        }

        private static string ContainerJson(LockableContainer c, string kind, float dist)
        {
            var pos = SafePos(c);
            bool open = SafeBool(() => c.IsOpen);
            bool locked = SafeBool(() => c.IsLocked);
            int itemCount = -1;
            var items = new List<string>();
            try
            {
                var container = c.Container;
                var list = container?.Items;
                if (list != null)
                {
                    itemCount = list.Count;
                    foreach (var it in list.Take(30)) items.Add(Json.Str(ItemLabel(it)));
                }
            }
            catch { }

            return "{\"kind\":" + Json.Str(kind) +
                   ",\"id\":" + c.Identity.Instance +
                   ",\"name\":" + Json.Str(SafeName(c)) +
                   ",\"dist\":" + dist.ToString("0.0") +
                   ",\"open\":" + (open ? "true" : "false") +
                   ",\"locked\":" + (locked ? "true" : "false") +
                   ",\"itemCount\":" + itemCount +
                   ",\"items\":[" + string.Join(",", items) + "]" +
                   ",\"pos\":{\"x\":" + pos.X.ToString("0.0") + ",\"y\":" + pos.Y.ToString("0.0") + ",\"z\":" + pos.Z.ToString("0.0") + "}}";
        }

        private static string ItemLabel(object item)
        {
            if (item == null) return "?";
            try
            {
                var p = item.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                var v = p?.GetValue(item, null) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { }
            try { return item.ToString(); } catch { return "?"; }
        }

        // The client only resolves a nearby character's profession once it has
        // been fully "discovered" (line-of-sight / proximity); until then this
        // reads back as uint.MaxValue (0xFFFFFFFF), not a real profession.
        private static string SafeProfession(AOSharp.Core.SimpleChar p)
        {
            try
            {
                var prof = p.Profession;
                if ((uint)(int)prof == 0xFFFFFFFF) return "Unknown";
                return prof.ToString();
            }
            catch { return "Unknown"; }
        }

        // ---- guarded accessors (LocalPlayer.Position/Name can throw while zoning) ----
        private static string SafeName(Dynel d) { try { return d.Name ?? ""; } catch { return ""; } }
        private static Vector3 SafePos(Dynel d) { try { return d.Position; } catch { return new Vector3(0, 0, 0); } }
        private static int SafeInt(Func<int> f) { try { return f(); } catch { return -1; } }
        private static float SafeFloat(Func<float> f) { try { return f(); } catch { return -1f; } }
        private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }
        private static string SafeStr(Func<string> f) { try { return f(); } catch { return null; } }
    }
}
