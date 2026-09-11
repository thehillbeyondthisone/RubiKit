// RubiKit 2.1 — Diagnostics subsystem
// C# 7.3 / .NET 4.8 compatible
//
// Purpose: total runtime observability for the plugin while it is injected in the
// AO client. Every HTTP request, every stat sample cycle, every exception, every
// SSE client, and the full raw game-data surface is captured into a ring buffer,
// mirrored to a rolling log file, and streamed live to the /debug console.
//
// Endpoints added (see Kernel.Handle):
//   GET /debug              -> live console (modules/debug/index.html)
//   GET /debug/stream       -> SSE feed of log entries as they happen
//   GET /api/debug          -> snapshot: counters, uptime, last error, recent log
//   GET /api/debug/log      -> ?limit=&level=&cat=  filtered log entries
//   GET /api/debug/stats    -> EVERY AOSharp Stat enum value + frame-to-frame diff
//   GET /api/debug/game     -> reflective dump of LocalPlayer / team / world
//   GET /api/debug/verbose  -> ?on=1|0  toggle per-cycle trace logging

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AOSharp.Common.GameData;
using AOSharp.Core;
using File = System.IO.File;

namespace RubiKit
{
    internal static class Diag
    {
        public sealed class Entry
        {
            public long Seq;
            public string TsIso;
            public string Level;   // trace | info | warn | error
            public string Cat;     // http | stat | sse | cmd | game | life | diag
            public string Msg;
            public string DataJson; // already-serialized JSON fragment or null
            public double? Ms;
        }

        public static readonly DateTime StartedUtc = DateTime.UtcNow;
        public static volatile bool Verbose = false;

        private const int Cap = 4000;
        private static readonly ConcurrentQueue<Entry> _ring = new ConcurrentQueue<Entry>();
        private static long _seq;
        private static long _count;

        private static string _logPath;
        private static readonly object _fileGate = new object();
        private const long MaxLogBytes = 5 * 1024 * 1024;

        // Live counters (read by /api/debug).
        public static long HttpRequests;
        public static long HttpErrors;
        public static long StatCycles;
        public static long SseConnects;
        public static long SseDisconnects;
        public static double LastCycleMs;
        public static int LastChangedStats;
        public static string LastError = "";
        public static string LastErrorTsIso = "";

        // Set by Kernel so log entries can be pushed to /debug/stream subscribers.
        public static Action<string> OnEntry;
        // Set by Kernel so the snapshot can report live SSE client counts.
        public static Func<int> StateClientCount;
        public static Func<int> DebugClientCount;

        public static void Init(string baseDir)
        {
            try { _logPath = Path.Combine(baseDir ?? "", "rubikit-debug.log"); }
            catch { _logPath = null; }
            Log("info", "diag", "Diagnostics online", "{\"pid\":" + SafePid() + ",\"cwd\":" + Json.Str(SafeCwd()) + "}");
        }

        public static void Log(string level, string cat, string msg, string dataJson = null, double? ms = null)
        {
            var e = new Entry
            {
                Seq = System.Threading.Interlocked.Increment(ref _seq),
                TsIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Level = level ?? "info",
                Cat = cat ?? "diag",
                Msg = msg ?? "",
                DataJson = dataJson,
                Ms = ms
            };

            _ring.Enqueue(e);
            if (System.Threading.Interlocked.Increment(ref _count) > Cap)
            {
                if (_ring.TryDequeue(out _)) System.Threading.Interlocked.Decrement(ref _count);
            }

            if (e.Level == "error")
            {
                LastError = e.Cat + ": " + e.Msg;
                LastErrorTsIso = e.TsIso;
            }

            WriteFileLine(e);

            var cb = OnEntry;
            if (cb != null)
            {
                try { cb("event: log\ndata: " + EntryJson(e) + "\n\n"); }
                catch { }
            }
        }

        public static void Info(string cat, string msg, string dataJson = null) => Log("info", cat, msg, dataJson);
        public static void Warn(string cat, string msg, string dataJson = null) => Log("warn", cat, msg, dataJson);
        public static void Trace(string cat, string msg, string dataJson = null) { if (Verbose) Log("trace", cat, msg, dataJson); }

        public static void Error(string cat, string msg, Exception ex)
        {
            System.Threading.Interlocked.Increment(ref HttpErrors);
            var data = "{\"type\":" + Json.Str(ex?.GetType().Name) +
                       ",\"message\":" + Json.Str(ex?.Message) +
                       ",\"stack\":" + Json.Str(ex?.StackTrace) + "}";
            Log("error", cat, msg, data);
        }

        private static void WriteFileLine(Entry e)
        {
            if (_logPath == null) return;
            try
            {
                lock (_fileGate)
                {
                    if (File.Exists(_logPath))
                    {
                        var fi = new FileInfo(_logPath);
                        if (fi.Length > MaxLogBytes)
                        {
                            var bak = _logPath + ".1";
                            try { if (File.Exists(bak)) File.Delete(bak); } catch { }
                            try { File.Move(_logPath, bak); } catch { }
                        }
                    }

                    var line = new StringBuilder();
                    line.Append(e.TsIso).Append(' ')
                        .Append(e.Level.ToUpperInvariant().PadRight(5)).Append(' ')
                        .Append('[').Append(e.Cat).Append("] ")
                        .Append(e.Msg);
                    if (e.Ms.HasValue) line.Append(" (").Append(e.Ms.Value.ToString("0.0")).Append("ms)");
                    if (!string.IsNullOrEmpty(e.DataJson)) line.Append(' ').Append(e.DataJson);

                    File.AppendAllText(_logPath, line.ToString() + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { /* never let logging throw */ }
        }

        // ---- JSON output --------------------------------------------------

        private static string EntryJson(Entry e)
        {
            var sb = new StringBuilder(256);
            sb.Append('{')
              .Append("\"seq\":").Append(e.Seq)
              .Append(",\"ts\":").Append(Json.Str(e.TsIso))
              .Append(",\"level\":").Append(Json.Str(e.Level))
              .Append(",\"cat\":").Append(Json.Str(e.Cat))
              .Append(",\"msg\":").Append(Json.Str(e.Msg));
            if (e.Ms.HasValue) sb.Append(",\"ms\":").Append(e.Ms.Value.ToString("0.0"));
            sb.Append(",\"data\":").Append(string.IsNullOrEmpty(e.DataJson) ? "null" : e.DataJson);
            sb.Append('}');
            return sb.ToString();
        }

        public static string EntriesJson(int limit, string levelFilter, string catFilter)
        {
            var all = _ring.ToArray();
            IEnumerable<Entry> q = all;
            if (!string.IsNullOrEmpty(levelFilter)) q = q.Where(x => x.Level == levelFilter);
            if (!string.IsNullOrEmpty(catFilter)) q = q.Where(x => x.Cat == catFilter);
            var list = q.ToList();
            if (limit > 0 && list.Count > limit) list = list.Skip(list.Count - limit).ToList();

            var sb = new StringBuilder(4096);
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EntryJson(list[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        public static string SnapshotJson()
        {
            var up = (DateTime.UtcNow - StartedUtc).TotalSeconds;
            var sb = new StringBuilder(8192);
            sb.Append('{')
              .Append("\"startedUtc\":").Append(Json.Str(StartedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")))
              .Append(",\"uptimeSec\":").Append(up.ToString("0.0"))
              .Append(",\"verbose\":").Append(Verbose ? "true" : "false")
              .Append(",\"counters\":{")
              .Append("\"httpRequests\":").Append(HttpRequests)
              .Append(",\"httpErrors\":").Append(HttpErrors)
              .Append(",\"statCycles\":").Append(StatCycles)
              .Append(",\"sseConnects\":").Append(SseConnects)
              .Append(",\"sseDisconnects\":").Append(SseDisconnects)
              .Append(",\"lastCycleMs\":").Append(LastCycleMs.ToString("0.0"))
              .Append(",\"lastChangedStats\":").Append(LastChangedStats)
              .Append(",\"stateClients\":").Append(SafeCount(StateClientCount))
              .Append(",\"debugClients\":").Append(SafeCount(DebugClientCount))
              .Append('}')
              .Append(",\"lastError\":").Append(Json.Str(LastError))
              .Append(",\"lastErrorTs\":").Append(Json.Str(LastErrorTsIso))
              .Append(",\"ringCount\":").Append(System.Threading.Interlocked.Read(ref _count))
              .Append(",\"log\":").Append(EntriesJson(200, null, null))
              .Append('}');
            return sb.ToString();
        }

        private static int SafeCount(Func<int> f) { try { return f?.Invoke() ?? -1; } catch { return -1; } }
        private static int SafePid() { try { return Process.GetCurrentProcess().Id; } catch { return -1; } }
        private static string SafeCwd() { try { return Directory.GetCurrentDirectory(); } catch { return ""; } }
    }

    // Minimal JSON string helper shared by the diagnostics code.
    internal static class Json
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    // Raw, exhaustive view of the game-data surface the client exposes to plugins.
    internal static class GameProbe
    {
        private static readonly Dictionary<string, int> _prevStats =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly object _gate = new object();

        // The engine returns this exact value for stat IDs that don't apply to
        // the queried Dynel (item-only / NPC-only / server-only stats read off
        // the local player). It is NOT real character data — roughly 300 of the
        // 705 Stat enum entries come back as this on a player character, and
        // counting them as "non-zero" data makes the raw dump look far richer
        // than what's actually there.
        public const int NotApplicableSentinel = 1234567890;

        // Every value of the AOSharp Stat enum, read from the local player,
        // with a frame-to-frame diff so you can see exactly what the client is
        // changing in real time.
        public static string RawStatDumpJson()
        {
            var sw = Stopwatch.StartNew();
            var names = Enum.GetNames(typeof(Stat));
            var values = (Stat[])Enum.GetValues(typeof(Stat));

            var current = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
            bool havePlayer = false;
            string playerErr = null;

            try
            {
                var lp = DynelManager.LocalPlayer;
                havePlayer = lp != null;
                if (havePlayer)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        int v;
                        try { v = lp.GetStat(values[i]); }
                        catch { v = int.MinValue; }
                        // Stat enum has duplicate members (aliases); keep first.
                        if (!current.ContainsKey(names[i])) current[names[i]] = v;
                    }
                }
            }
            catch (Exception ex) { playerErr = ex.Message; }

            var changed = new List<string>();
            int nonZero = 0;
            int sentinel = 0;
            lock (_gate)
            {
                foreach (var kv in current)
                {
                    if (kv.Value == NotApplicableSentinel) sentinel++;
                    else if (kv.Value != 0 && kv.Value != int.MinValue) nonZero++;
                    if (_prevStats.TryGetValue(kv.Key, out int old))
                    {
                        if (old != kv.Value && kv.Value != NotApplicableSentinel && old != NotApplicableSentinel)
                            changed.Add("{\"name\":" + Json.Str(kv.Key) + ",\"old\":" + old + ",\"new\":" + kv.Value + "}");
                    }
                    _prevStats[kv.Key] = kv.Value;
                }
            }

            sw.Stop();
            var sb = new StringBuilder(32768);
            sb.Append('{')
              .Append("\"ts\":").Append(Json.Str(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")))
              .Append(",\"havePlayer\":").Append(havePlayer ? "true" : "false")
              .Append(",\"playerError\":").Append(Json.Str(playerErr))
              .Append(",\"enumCount\":").Append(names.Length)
              .Append(",\"distinctCount\":").Append(current.Count)
              .Append(",\"nonZeroCount\":").Append(nonZero)
              .Append(",\"sentinelCount\":").Append(sentinel)
              .Append(",\"readMs\":").Append(sw.Elapsed.TotalMilliseconds.ToString("0.00"))
              .Append(",\"changed\":[").Append(string.Join(",", changed)).Append(']')
              .Append(",\"stats\":{");
            bool first = true;
            foreach (var kv in current.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!first) sb.Append(',');
                sb.Append(Json.Str(kv.Key)).Append(':').Append(kv.Value);
                first = false;
            }
            sb.Append("}}");
            return sb.ToString();
        }

        // Reflective dump of whatever the client hands us: LocalPlayer, the local
        // team, the current target, and world object counts. No compile-time
        // dependency on specific AOSharp members — if the API surface changes,
        // this keeps working and simply reports what it finds.
        public static string GameDumpJson()
        {
            var sb = new StringBuilder(16384);
            sb.Append('{')
              .Append("\"ts\":").Append(Json.Str(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")))
              .Append(",\"zoning\":").Append(SafeBool(() => Game.IsZoning));

            object lp = null;
            try { lp = DynelManager.LocalPlayer; } catch { }
            sb.Append(",\"localPlayer\":").Append(DumpObject(lp, 3));

            // Current target — AOSharp.Core.Targeting, not a property on LocalPlayer.
            sb.Append(",\"target\":").Append(TargetJson());

            // World object counts via reflection over DynelManager statics.
            sb.Append(",\"world\":{");
            var dm = typeof(DynelManager);
            bool wfirst = true;
            foreach (var pn in new[] { "Players", "NPCs", "Characters", "Corpses", "Dynels", "Monsters", "Pets" })
            {
                var col = TryStatic(dm, pn);
                if (col == null) continue;
                int? c = TryCount(col);
                if (!wfirst) sb.Append(',');
                sb.Append(Json.Str(pn)).Append(':').Append(c?.ToString() ?? "null");
                wfirst = false;
            }
            sb.Append('}');

            // Local team / group — AOSharp.Core.Team.
            sb.Append(",\"team\":").Append(TeamJson());

            sb.Append('}');
            return sb.ToString();
        }

        private static string TargetJson()
        {
            try
            {
                if (!Targeting.HasTarget) return "{\"hasTarget\":false}";
                var t = Targeting.TargetChar;
                if (t == null)
                {
                    // Has a target but it isn't a char (e.g. a chest/corpse/item).
                    var d = Targeting.Target;
                    return "{\"hasTarget\":true,\"isChar\":false,\"name\":" + Json.Str(d != null ? SafeStrOf(() => d.Name) : null) + "}";
                }
                return "{\"hasTarget\":true,\"isChar\":true" +
                       ",\"name\":" + Json.Str(SafeStrOf(() => t.Name)) +
                       ",\"level\":" + SafeIntOf(() => t.Level) +
                       ",\"isNpc\":" + SafeBool(() => t.IsNpc) +
                       ",\"isAlive\":" + SafeBool(() => t.IsAlive) +
                       ",\"hpPct\":" + SafeFloatOf(() => t.HealthPercent).ToString("0") + "}";
            }
            catch (Exception ex) { return "{\"error\":" + Json.Str(ex.Message) + "}"; }
        }

        private static string TeamJson()
        {
            try
            {
                if (!Team.IsInTeam) return "{\"inTeam\":false}";
                var members = Team.Members ?? new List<TeamMember>();
                var rows = members.Select(m =>
                    "{\"name\":" + Json.Str(SafeStrOf(() => m.Name)) +
                    ",\"level\":" + SafeIntOf(() => m.Level) +
                    ",\"leader\":" + SafeBool(() => m.IsLeader) +
                    ",\"hpPct\":" + (m.Character != null ? SafeFloatOf(() => m.Character.HealthPercent).ToString("0") : "null") + "}");
                return "{\"inTeam\":true,\"isLeader\":" + (Team.IsLeader ? "true" : "false") +
                       ",\"isRaid\":" + (Team.IsRaid ? "true" : "false") +
                       ",\"members\":[" + string.Join(",", rows) + "]}";
            }
            catch (Exception ex) { return "{\"error\":" + Json.Str(ex.Message) + "}"; }
        }

        private static string SafeStrOf(Func<string> f) { try { return f(); } catch { return null; } }
        private static int SafeIntOf(Func<int> f) { try { return f(); } catch { return -1; } }
        private static float SafeFloatOf(Func<float> f) { try { return f(); } catch { return -1f; } }

        // ---- reflection helpers ----------------------------------------------

        private static string DumpObject(object o, int depth)
        {
            if (o == null) return "null";
            var t = o.GetType();

            if (o is string || t.IsPrimitive || o is decimal)
                return Scalar(o);
            if (o is Enum)
                return Json.Str(o.ToString());

            // Known small AO structs worth expanding by ToString.
            if (t.Name == "Vector3" || t.Name == "Quaternion" || t.Name == "Identity")
                return Json.Str(o.ToString());

            // Buff — the generic dumper never gets deep enough to show anything
            // but its type name, and the actual fields (which nanoline, how much
            // time is left) are the only reason anyone cares about a buff list.
            if (o is Buff buff)
            {
                return "{\"nanoline\":" + Json.Str(SafeStrOf(() => buff.Nanoline.ToString())) +
                       ",\"remainingSec\":" + SafeFloatOf(() => buff.RemainingTime).ToString("0") +
                       ",\"totalSec\":" + SafeFloatOf(() => buff.TotalTime).ToString("0") +
                       ",\"stack\":" + SafeIntOf(() => buff.StackingOrder) +
                       ",\"ncu\":" + SafeIntOf(() => buff.NCU) + "}";
            }

            if (depth <= 0) return Json.Str(o.ToString());

            if (o is System.Collections.IEnumerable en && !(o is string))
            {
                var items = new List<string>();
                int n = 0;
                foreach (var it in en)
                {
                    if (n++ >= 25) { items.Add("\"…truncated\""); break; }
                    items.Add(DumpObject(it, depth - 1));
                }
                return "[" + string.Join(",", items) + "]";
            }

            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"$type\":").Append(Json.Str(t.Name));
            PropertyInfo[] props;
            try { props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance); }
            catch { props = new PropertyInfo[0]; }

            foreach (var p in props)
            {
                if (p.GetIndexParameters().Length != 0) continue;
                object val;
                try { val = p.GetValue(o, null); }
                catch (Exception ex) { sb.Append(',').Append(Json.Str(p.Name)).Append(":").Append(Json.Str("<err: " + ex.GetType().Name + ">")); continue; }
                sb.Append(',').Append(Json.Str(p.Name)).Append(':').Append(DumpObject(val, depth - 1));
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string Scalar(object o)
        {
            if (o == null) return "null";
            if (o is bool b) return b ? "true" : "false";
            if (o is string s) return Json.Str(s);
            if (o is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (o is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (o is IFormattable fm) return fm.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            return Json.Str(o.ToString());
        }

        private static object TryGet(object o, string prop)
        {
            if (o == null) return null;
            try
            {
                var p = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                return p?.GetValue(o, null);
            }
            catch { return null; }
        }

        private static object TryStatic(Type t, string member)
        {
            try
            {
                var p = t.GetProperty(member, BindingFlags.Public | BindingFlags.Static);
                if (p != null) return p.GetValue(null, null);
                var f = t.GetField(member, BindingFlags.Public | BindingFlags.Static);
                return f?.GetValue(null);
            }
            catch { return null; }
        }

        private static int? TryCount(object col)
        {
            try
            {
                var p = col.GetType().GetProperty("Count");
                if (p != null) return (int)p.GetValue(col, null);
                if (col is System.Collections.IEnumerable en)
                {
                    int n = 0;
                    foreach (var _ in en) n++;
                    return n;
                }
            }
            catch { }
            return null;
        }

        private static string SafeBool(Func<bool> f)
        {
            try { return f() ? "true" : "false"; } catch { return "null"; }
        }
    }
}
