// RubiKit 2.1 — NotumHUD-ready single DLL (for notumhud.js)
// C# 7.3 AND .NET 4.8 COMPATIBLE
// FIXED: Port conflict resolution when switching characters
// Implements the API structure required by notumhud.js (e.g., /api/state, /api/groups)
// Refs: AOSharp.Core, AOSharp.Common, AOSharp.Core.UI

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;

// Disambiguate File reference
using File = System.IO.File;

namespace RubiKit
{
    public class Main : AOPluginEntry
    {
        private static Kernel _kernel;

        public override void Run(string pluginDir)
        {
            try
            {
                _kernel = new Kernel(pluginDir ?? "");
                _kernel.Start();
                Chat.WriteLine("<color=#4da3ff>[RubiKit 2.1]</color> API on 127.0.0.1:8777  |  /rubi to open NotumHUD");
                Chat.RegisterCommand("rubi", (cmd, a, w) => _kernel.OpenStatus());
                Chat.RegisterCommand("about", (cmd, a, w) => _kernel.ShowAbout());
            }
            catch (Exception ex)
            {
                Chat.WriteLine("[RubiKit] Failed: " + ex.Message, ChatColor.Red);
            }
        }

        public override void Teardown()
        {
            try
            {
                if (_kernel != null)
                {
                    Chat.WriteLine("[RubiKit] Shutting down...");
                    _kernel.Dispose();
                    _kernel = null;
                }
            }
            catch (Exception ex)
            {
                Chat.WriteLine("[RubiKit] Teardown error: " + ex.Message, ChatColor.Yellow);
            }
            Chat.WriteLine("[RubiKit] Unloaded.");
        }
    }

    internal sealed class Kernel : IDisposable
    {
        private readonly string _baseDir;
        private HttpListener _http;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly StatService _statService;
        private readonly StateStore _state = new StateStore();
        private readonly object _sseGate = new object();
        private readonly List<SseClient> _clients = new List<SseClient>();
        private System.Timers.Timer _pushTimer;
        private System.Timers.Timer _cleanupTimer;
        private Task _httpLoopTask;
        private bool _disposed = false;
        public const int Port = 8777;

        public Kernel(string baseDir)
        {
            _baseDir = baseDir;
            _statService = new StatService(new StatProvider());
            _statService.OnSample += snap =>
            {
                _state.UpdateStats(snap.Stats);
                _state.LastUpdatedUtc = DateTime.UtcNow;
            };
        }

        public void Start()
        {
            _statService.Start(_cts.Token);

            // FIX: Better error handling for port binding with more detailed messages
            try
            {
                _http = new HttpListener();
                _http.Prefixes.Add("http://127.0.0.1:" + Port + "/");
                _http.Prefixes.Add("http://localhost:" + Port + "/");
                _http.Start();
            }
            catch (HttpListenerException ex)
            {
                if (ex.ErrorCode == 183) // ERROR_ALREADY_EXISTS
                {
                    Chat.WriteLine($"[RubiKit] Port {Port} is already in use. This usually means:", ChatColor.Red);
                    Chat.WriteLine("  1. Another instance of the plugin is running", ChatColor.Yellow);
                    Chat.WriteLine("  2. The previous instance didn't shut down cleanly", ChatColor.Yellow);
                    Chat.WriteLine("  Solution: Wait 30 seconds and try again, or restart the game.", ChatColor.Yellow);
                }
                else
                {
                    Chat.WriteLine($"[RubiKit] Failed to bind to port {Port}. Error code: {ex.ErrorCode}", ChatColor.Red);
                }
                throw;
            }

            _httpLoopTask = Task.Run(HttpLoop);

            _pushTimer = new System.Timers.Timer(250);
            _pushTimer.AutoReset = true;
            _pushTimer.Elapsed += (s, e) => BroadcastState();
            _pushTimer.Start();

            _cleanupTimer = new System.Timers.Timer(5000);
            _cleanupTimer.AutoReset = true;
            _cleanupTimer.Elapsed += (s, e) => CleanupDeadConnections();
            _cleanupTimer.Start();
        }

        // FIX: Improved disposal sequence to prevent port conflicts
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Step 1: Stop timers immediately
            try { _pushTimer?.Stop(); _pushTimer?.Dispose(); } catch { }
            try { _cleanupTimer?.Stop(); _cleanupTimer?.Dispose(); } catch { }

            // Step 2: Cancel all async operations
            try { _cts.Cancel(); } catch { }

            // Step 3: Close all SSE client connections
            lock (_sseGate)
            {
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    try { _clients[i].Dispose(); } catch { }
                }
                _clients.Clear();
            }

            // Step 4: Stop the HTTP listener FIRST (stops accepting new connections)
            if (_http != null && _http.IsListening)
            {
                try
                {
                    _http.Stop();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, that's fine
                }
                catch (Exception ex)
                {
                    Chat.WriteLine("[RubiKit] Error stopping HTTP listener: " + ex.Message, ChatColor.Yellow);
                }
            }

            // Step 5: Wait briefly for the HTTP loop to exit
            if (_httpLoopTask != null)
            {
                try
                {
                    if (!_httpLoopTask.Wait(2000))
                    {
                        Chat.WriteLine("[RubiKit] HTTP loop did not exit cleanly.", ChatColor.Yellow);
                    }
                }
                catch { }
            }

            // Step 6: Close the HTTP listener (releases the port)
            if (_http != null)
            {
                try
                {
                    _http.Close();
                    _http = null;
                }
                catch (Exception ex)
                {
                    Chat.WriteLine("[RubiKit] Error closing HTTP listener: " + ex.Message, ChatColor.Yellow);
                }
            }

            // Step 7: Dispose the cancellation token source
            try { _cts.Dispose(); } catch { }

            // Step 8: Force garbage collection to release resources faster
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public void OpenStatus()
        {
            var url = "http://127.0.0.1:" + Port + "/modules/notumHUD/index.html";
            try { System.Diagnostics.Process.Start(url); } catch { }
            Chat.WriteLine("[RubiKit] Opening NotumHUD: " + url);
        }

        public void ShowAbout()
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm?.GetName()?.Version?.ToString() ?? "n/a";
            var sb = new StringBuilder();
            sb.AppendLine("<font color='#7ee787'>RubiKit</font> v2.1 (notumhud.js)");
            sb.AppendLine("Build: " + ver);
            sb.AppendLine("HTTP: http://localhost:" + Port + "/");
            Chat.WriteLine(sb.ToString());
        }

        private async Task HttpLoop()
        {
            while (!_disposed && _http != null && _http.IsListening)
            {
                try
                {
                    HttpListenerContext ctx = await _http.GetContextAsync().ConfigureAwait(false);

                    // Check if we're disposing
                    if (_disposed) break;

                    Handle(ctx);
                }
                catch (HttpListenerException)
                {
                    // Listener stopped, exit gracefully
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    if (!_disposed)
                    {
                        Chat.WriteLine("[RubiKit] HTTP error: " + ex.Message, ChatColor.Yellow);
                    }
                }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            // Check if disposing
            if (_disposed)
            {
                try { ctx.Response.StatusCode = 503; ctx.Response.Close(); } catch { }
                return;
            }

            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;
            string path = req.Url.AbsolutePath ?? "/";

            // More secure CORS - only allow localhost
            string origin = req.Headers["Origin"] ?? "";
            if (origin.Contains("127.0.0.1") || origin.Contains("localhost") || string.IsNullOrEmpty(origin))
            {
                res.AddHeader("Access-Control-Allow-Origin", string.IsNullOrEmpty(origin) ? "*" : origin);
            }

            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                try { res.OutputStream.Close(); } catch { }
                return;
            }

            try
            {
                if (path == "/events") { HandleEvents(res); return; }
                if (path == "/api/state") { SendJson(res, 200, _state.ToJson()); return; }
                if (path == "/api/groups") { SendJson(res, 200, StatProvider.GetGroupsJson()); return; }
                if (path == "/api/themes") { SendJson(res, 200, "[]"); return; }
                if (path == "/api/cmd") { HandleCmd(req, res); return; }
                if (path == "/health") { SendText(res, 200, "OK"); return; }

                if (path.StartsWith("/modules/", StringComparison.OrdinalIgnoreCase))
                {
                    ServeStaticUnder(Path.Combine(_baseDir, "modules"), path.Substring("/modules/".Length), ctx);
                    return;
                }
                if (path == "/" || path == "/index.html" || path == "/monitor.html")
                {
                    string fileName = path == "/monitor.html" ? "monitor.html" : "index.html";
                    string fullPath = Path.Combine(_baseDir, "modules", "notumHUD", fileName);

                    if (!File.Exists(fullPath) && fileName == "index.html")
                    {
                        SendStatusPage(res);
                        return;
                    }
                    else if (File.Exists(fullPath))
                    {
                        ServeStaticUnder(Path.Combine(_baseDir, "modules"), "notumHUD/" + fileName, ctx);
                        return;
                    }
                }

                SendStatusPage(res);
            }
            catch (Exception ex)
            {
                try
                {
                    SendText(res, 500, "Internal Server Error");
                }
                catch { }
            }
            finally
            {
                if (res.ContentType != "text/event-stream")
                {
                    try { res.OutputStream.Close(); } catch { }
                }
            }
        }

        private void SendStatusPage(HttpListenerResponse res)
        {
            res.StatusCode = 200;
            res.ContentType = "text/html; charset=utf-8";
            var html = "<!doctype html><meta charset='utf-8'><title>RubiKit 2.1</title>" +
                       "<style>body{font:14px/1.4 system-ui,sans-serif;padding:18px;background:#0e1f12;color:#e9ecf1} a{color:#58a6ff;}</style>" +
                       "<h2>RubiKit 2.1</h2><p>Online. Open <a href='/modules/notumHUD/index.html'>NotumHUD</a>.</p>";
            var bytes = Encoding.UTF8.GetBytes(html);
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private void HandleEvents(HttpListenerResponse res)
        {
            if (_disposed) return;

            res.StatusCode = 200;
            res.KeepAlive = true;
            res.ContentType = "text/event-stream";
            res.AddHeader("Cache-Control", "no-cache");
            res.SendChunked = true;
            var client = new SseClient(res);
            client.Send("event: hello\ndata: {\"ok\":true}\n\n");
            lock (_sseGate) _clients.Add(client);
        }

        private void BroadcastState()
        {
            if (_disposed || _clients.Count == 0) return;
            string payload = "data: " + _state.ToJson() + "\n\n";
            lock (_sseGate)
            {
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    if (!_clients[i].Send(payload))
                    {
                        try { _clients[i].Dispose(); } catch { }
                        _clients.RemoveAt(i);
                    }
                }
            }
        }

        private void CleanupDeadConnections()
        {
            if (_disposed) return;
            lock (_sseGate)
            {
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    if (!_clients[i].IsAlive())
                    {
                        try { _clients[i].Dispose(); } catch { }
                        _clients.RemoveAt(i);
                    }
                }
            }
        }

        private sealed class SseClient : IDisposable
        {
            private readonly HttpListenerResponse _res;
            private readonly StreamWriter _w;
            private DateTime _lastSend;
            private bool _isDisposed;

            public SseClient(HttpListenerResponse res)
            {
                _res = res;
                _w = new StreamWriter(res.OutputStream, new UTF8Encoding(false)) { AutoFlush = true };
                _lastSend = DateTime.UtcNow;
            }

            public bool Send(string s)
            {
                try
                {
                    _w.Write(s);
                    _lastSend = DateTime.UtcNow;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public bool IsAlive()
            {
                if (_isDisposed) return false;
                return (DateTime.UtcNow - _lastSend).TotalSeconds < 30;
            }

            public void Dispose()
            {
                if (_isDisposed) return;
                _isDisposed = true;
                try { _w.Flush(); _res.OutputStream.Close(); } catch { }
            }
        }

        private void HandleCmd(HttpListenerRequest req, HttpListenerResponse res)
        {
            var q = req.QueryString;
            var action = (q["action"] ?? "").Trim().ToLowerInvariant();
            var value = q["value"] ?? "";

            switch (action)
            {
                case "pin_add": _state.Pin(value); res.StatusCode = 204; break;
                case "pin_remove": _state.Unpin(value); res.StatusCode = 204; break;
                case "theme": _state.Settings["theme"] = value; res.StatusCode = 204; break;
                case "compact": _state.Settings["compact"] = value == "1" ? "true" : "false"; res.StatusCode = 204; break;
                case "interval_ms": _state.Settings["interval_ms"] = value; res.StatusCode = 204; break;
                case "enable": _state.Settings["enabled"] = value == "1" ? "true" : "false"; res.StatusCode = 204; break;
                case "panel_order_set": _state.Settings["panelOrder"] = value; res.StatusCode = 204; break;
                case "misc_hide_add": _state.HideMisc(value); res.StatusCode = 204; break;
                case "misc_hide_remove": _state.ShowMisc(value); res.StatusCode = 204; break;
                case "misc_hide_clear": _state.ClearHiddenMisc(); res.StatusCode = 204; break;
                default: res.StatusCode = 400; break;
            }
        }

        private void ServeStaticUnder(string root, string rel, HttpListenerContext ctx)
        {
            rel = (rel ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, rel));
            var jail = Path.GetFullPath(root);
            if (!full.StartsWith(jail, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                SendText(ctx.Response, 404, "Not Found");
                return;
            }
            SendFile(ctx.Response, full, GetMime(full), 200);
        }

        private static string GetMime(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".html": case ".htm": return "text/html; charset=utf-8";
                case ".js": case ".mjs": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                default: return "application/octet-stream";
            }
        }

        private static void SendFile(HttpListenerResponse res, string path, string mime, int code)
        {
            var bytes = File.ReadAllBytes(path);
            res.StatusCode = code;
            res.ContentType = mime;
            if (mime != "text/html; charset=utf-8") res.AddHeader("Cache-Control", "public, max-age=300");
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static void SendJson(HttpListenerResponse res, int code, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json ?? "{}");
            res.StatusCode = code;
            res.ContentType = "application/json; charset=utf-8";
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static void SendText(HttpListenerResponse res, int code, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            res.StatusCode = code;
            res.ContentType = "text/plain; charset=utf-8";
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }
    }

    internal sealed class StateStore
    {
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        private readonly ConcurrentDictionary<string, int> _stats = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentDictionary<string, string> Settings = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _pins = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _hiddenMisc = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public StateStore()
        {
            Settings["theme"] = "theme-aetherium";
            Settings["compact"] = "false";
            Settings["interval_ms"] = "250";
            Settings["enabled"] = "true";
            Settings["panelOrder"] = "core,dmg,ac,pins";
            Pin("NanoCInit");
        }

        public void UpdateStats(ConcurrentDictionary<string, int> newStats)
        {
            _stats.Clear();
            foreach (var stat in newStats)
            {
                _stats[stat.Key] = stat.Value;
            }
        }

        public void Pin(string name, string labelOverride = null) => _pins[name] = labelOverride ?? LabelFor(name);
        public void Unpin(string name) => _pins.TryRemove(name, out _);
        public void HideMisc(string name) => _hiddenMisc[name] = true;
        public void ShowMisc(string name) => _hiddenMisc.TryRemove(name, out _);
        public void ClearHiddenMisc() => _hiddenMisc.Clear();

        public string ToJson()
        {
            var sb = new StringBuilder(4096);
            sb.Append('{');
            sb.Append("\"settings\":{");
            sb.Append(string.Join(",", Settings.Select(s => $"\"{s.Key}\":\"{Escape(s.Value)}\"")));
            sb.Append("},");
            sb.Append("\"core\":{");
            sb.Append($"\"AddAllOff\":{GetStat("AddAllOff")},");
            sb.Append($"\"AddAllDef\":{GetStat("AddAllDef")},");
            sb.Append($"\"CriticalIncrease\":{GetStat("CriticalIncrease")},");
            sb.Append($"\"XPModifier\":{GetStat("XPModifier")}");
            sb.Append("},");
            int hpNow = GetStat("Health");
            int hpMax = Math.Max(1, GetStat("MaxHealth"));
            sb.Append("\"hp\":{");
            sb.Append($"\"now\":{hpNow},\"max\":{hpMax},\"pct\":{(int)Math.Round((double)hpNow * 100 / hpMax)}");
            sb.Append("},");
            int nanoNow = GetStat("CurrentNano");
            int nanoMax = Math.Max(1, GetStat("MaxNanoEnergy"));
            sb.Append("\"nano\":{");
            sb.Append($"\"now\":{nanoNow},\"max\":{nanoMax},\"pct\":{(int)Math.Round((double)nanoNow * 100 / nanoMax)}");
            sb.Append("},");
            sb.Append("\"dmg\":{");
            sb.Append(string.Join(",", StatProvider.DmgNames.Select(name => $"\"{name}\":{GetStat(name)}")));
            sb.Append("},");
            sb.Append("\"ac\":{");
            sb.Append(string.Join(",", StatProvider.AcNames.Select(name => $"\"{name}\":{GetStat(name)}")));
            sb.Append("},");
            sb.Append("\"all\":{");
            sb.Append(string.Join(",", _stats.Select(s => $"\"{s.Key}\":{s.Value}")));
            sb.Append("},");
            sb.Append("\"all_names\":[");
            sb.Append(string.Join(",", StatProvider.AllStatNames.Select(name => $"\"{name}\"")));
            sb.Append("],");
            sb.Append("\"pins\":[");
            sb.Append(string.Join(",", _pins.Select(p => $"{{\"name\":\"{p.Key}\",\"v\":{GetStat(p.Key)},\"label\":\"{Escape(p.Value)}\"}}")));
            sb.Append("],");
            sb.Append("\"hiddenMisc\":[");
            sb.Append(string.Join(",", _hiddenMisc.Keys.Select(k => $"\"{k}\"")));
            sb.Append("]");
            sb.Append('}');
            return sb.ToString();
        }

        private int GetStat(string name) => _stats.TryGetValue(name, out int val) ? val : 0;
        private static string Escape(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        private static string LabelFor(string n)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                {"TwoHandedEdged","2HE"}, {"OneHandedEdged","1HE"}, {"TwoHandedBlunt","2HB"}, {"OneHandedBlunt","1HB"},
                {"MeleeEnergy","ME"}, {"RangedEnergy","RE"}, {"FullAuto","Full Auto"}, {"FlingShot","Fling"},
                {"AimedShot","Aimed Shot"}, {"ComputerLiteracy","Comp Lit"}, {"NanoCInit","Nano Init"},
                {"NanoProg","Nano Programming"}, {"BodyDev","Body Dev"}, {"DuckExp","Duck-Exp"},
                {"DodgeRanged","Dodge Ranged"}, {"EvadeClsC","Evade Close"}, {"AddAllOff","AAO"},
                {"AddAllDef","AAD"}, {"CriticalIncrease","Crit+"}, {"XPModifier","XP %"},
                {"MatterCreation","MC"}, {"MatterMetamorphosis","MM"}, {"BiologicalMetamorphosis","BM"},
                {"TimeAndSpace","TS"}, {"PsychologicalModification","PM"}, {"SensoryImprovement","SI"},
                {"SubMachineGun","SMG"}, {"NanoResist","Nano Resist"}, {"FirstAid","First Aid"},
                {"HealDelta","Heal Delta"}, {"NanoDelta","Nano Delta"}, {"Treatment","Treatment"}
            };
            return map.TryGetValue(n, out var label) ? label : n.Replace("DamageModifier", "").Replace("AC", "");
        }
    }

    public sealed class StatSnapshot
    {
        public ConcurrentDictionary<string, int> Stats { get; } = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class StatProvider
    {
        public static IEnumerable<string> DmgNames { get; } = new[] { "MeleeDamageModifier", "ProjectileDamageModifier", "EnergyDamageModifier", "FireDamageModifier", "ColdDamageModifier", "PoisonDamageModifier", "RadiationDamageModifier", "ChemicalDamageModifier" };
        public static IEnumerable<string> AcNames { get; } = new[] { "MeleeAC", "ProjectileAC", "EnergyAC", "FireAC", "ColdAC", "PoisonAC", "RadiationAC", "ChemicalAC", "NanoAC" };
        public static IEnumerable<string> AllStatNames => _statMap.Keys;

        private static readonly (string Id, string Label, string[] Stats)[] _groups = {
            ("abilities", "Abilities", new[] { "Strength", "Agility", "Stamina", "Intelligence", "Sense", "Psychic" }),
            ("body", "Body", new[] { "BodyDev", "NanoPool", "HealDelta", "NanoDelta", "FirstAid", "Treatment", "Adventuring", "MaxHealth", "BodyDevelopment" }),
            ("melee", "Melee", new[] { "MartialArts", "MeleeEnergy", "OneHandedBlunt", "OneHandedEdged", "Piercing", "TwoHandedBlunt", "TwoHandedEdged", "Brawl", "FastAttack", "SneakAttack", "MultiMelee", "Dimach", "Riposte", "SharpObject" }),
            ("ranged", "Ranged", new[] { "AssaultRifle", "Bow", "Grenade", "HeavyWeapons", "Pistol", "RangedEnergy", "Rifle", "Shotgun", "SubMachineGun", "AimedShot", "Burst", "FlingShot", "FullAuto", "MultiRanged", "BowSpecialAttack", "MGSMG" }),
            ("speed", "Speed", new[] { "MeleeInit", "RangedInit", "PhysicalInit", "AggDef", "DodgeRanged", "EvadeClsC", "DuckExp", "RunSpeed", "Parry" }),
            ("tradeskills", "Tradeskills", new[] { "MechanicalEngineering", "ElectricalEngineering", "FieldQuantumPhysics", "WeaponSmithing", "Pharmaceuticals", "Chemistry", "Tutoring", "ComputerLiteracy", "Psychology", "QuantumFT", "NanoProgramming" }),
            ("nano", "Nano", new[] { "MatterMetamorphosis", "BiologicalMetamorphosis", "PsychologicalModification", "MatterCreation", "TimeAndSpace", "SensoryImprovement", "NanoCInit", "NanoProg", "NanoResist", "MaterialMetamorphosis", "MaterialCreation", "SpaceTime" }),
            ("spying", "Spying", new[] { "BreakingEntry", "Concealment", "Perception", "TrapDisarm" }),
            ("nav", "Navigation", new[] { "MapNavigation", "Swimming", "VehicleAir", "VehicleGround", "VehicleWater" })
        };

        private static string _groupsJson;
        public static string GetGroupsJson()
        {
            if (_groupsJson == null)
            {
                var sb = new StringBuilder();
                sb.Append('[');
                bool firstGroup = true;
                foreach (var group in _groups)
                {
                    if (!firstGroup) sb.Append(',');
                    sb.Append($"{{\"Id\":\"{group.Id}\",\"Label\":\"{group.Label}\",\"Stats\":[");
                    sb.Append(string.Join(",", group.Stats.Select(s => $"\"{s}\"")));
                    sb.Append("]}");
                    firstGroup = false;
                }
                sb.Append(']');
                _groupsJson = sb.ToString();
            }
            return _groupsJson;
        }

        private static readonly Dictionary<string, Stat> _statMap = new Dictionary<string, Stat>(StringComparer.OrdinalIgnoreCase)
        {
            // Abilities
            {"Strength", Stat.Strength}, {"Agility", Stat.Agility}, {"Stamina", Stat.Stamina}, {"Intelligence", Stat.Intelligence}, {"Sense", Stat.Sense}, {"Psychic", Stat.Psychic},
            // Body
            {"BodyDev", Stat.BodyDevelopment}, {"NanoPool", Stat.NanoPool}, {"HealDelta", Stat.HealDelta}, {"NanoDelta", Stat.NanoDelta}, {"FirstAid", Stat.FirstAid}, {"Treatment", Stat.Treatment}, {"Adventuring", Stat.Adventuring}, {"MaxHealth", Stat.MaxHealth}, {"BodyDevelopment", Stat.BodyDevelopment},
            // Melee
            {"MartialArts", Stat.MartialArts}, {"MeleeEnergy", Stat.MeleeEnergy}, {"OneHandedBlunt", Stat._1hBlunt}, {"OneHandedEdged", Stat._1hEdged}, {"Piercing", Stat.Piercing}, {"TwoHandedBlunt", Stat._2hBlunt}, {"TwoHandedEdged", Stat.Skill2hEdged}, {"Brawl", Stat.Brawl}, {"FastAttack", Stat.FastAttack}, {"SneakAttack", Stat.SneakAttack}, {"MultiMelee", Stat.MultiMelee}, {"Dimach", Stat.Dimach}, {"Riposte", Stat.Riposte}, {"SharpObject", Stat.SharpObject},
            // Ranged
            {"AssaultRifle", Stat.AssaultRifle}, {"Bow", Stat.Bow}, {"Grenade", Stat.Grenade}, {"HeavyWeapons", Stat.HeavyWeapons}, {"Pistol", Stat.Pistol}, {"RangedEnergy", Stat.RangedEnergy}, {"Rifle", Stat.Rifle}, {"Shotgun", Stat.Shotgun},
            {"SubMachineGun", Stat.MGSMG},
            {"AimedShot", Stat.AimedShot}, {"Burst", Stat.Burst}, {"FlingShot", Stat.FlingShot}, {"FullAuto", Stat.FullAuto}, {"MultiRanged", Stat.MultiRanged}, {"BowSpecialAttack", Stat.BowSpecialAttack}, {"MGSMG", Stat.MGSMG},
            // Speed
            {"MeleeInit", Stat.MeleeInit}, {"RangedInit", Stat.RangedInit}, {"PhysicalInit", Stat.PhysicalInit}, {"AggDef", Stat.AggDef}, {"DodgeRanged", Stat.DodgeRanged}, {"EvadeClsC", Stat.EvadeClsC}, {"DuckExp", Stat.DuckExp}, {"RunSpeed", Stat.RunSpeed}, {"Parry", Stat.Parry},
            // Tradeskills
            {"MechanicalEngineering", Stat.MechanicalEngineering}, {"ElectricalEngineering", Stat.ElectricalEngineering}, {"FieldQuantumPhysics", Stat.QuantumFT}, {"WeaponSmithing", Stat.WeaponSmithing}, {"Pharmaceuticals", Stat.Pharmaceuticals}, {"Chemistry", Stat.Chemistry}, {"Tutoring", Stat.Tutoring}, {"ComputerLiteracy", Stat.ComputerLiteracy}, {"Psychology", Stat.Psychology}, {"QuantumFT", Stat.QuantumFT}, {"NanoProgramming", Stat.NanoProgramming},
            // Nano
            {"MatterMetamorphosis", Stat.MaterialMetamorphosis}, {"BiologicalMetamorphosis", Stat.BiologicalMetamorphosis}, {"PsychologicalModification", Stat.PsychologicalModification}, {"MatterCreation", Stat.MaterialCreation}, {"TimeAndSpace", Stat.SpaceTime}, {"SensoryImprovement", Stat.SensoryImprovement}, {"NanoCInit", Stat.NanoCInit}, {"NanoProg", Stat.NanoProgramming}, {"NanoResist", Stat.NanoResist},
            {"MaterialMetamorphosis", Stat.MaterialMetamorphosis}, {"MaterialCreation", Stat.MaterialCreation}, {"SpaceTime", Stat.SpaceTime},
            // Spying
            {"BreakingEntry", Stat.BreakingEntry}, {"Concealment", Stat.Concealment}, {"Perception", Stat.Perception}, {"TrapDisarm", Stat.TrapDisarm},
            // Navigation
            {"MapNavigation", Stat.MapNavigation}, {"Swimming", Stat.Swimming}, {"VehicleAir", Stat.VehicleAir}, {"VehicleGround", Stat.VehicleGround}, {"VehicleWater", Stat.VehicleWater},
            // Core Stats
            {"AddAllOff", Stat.AddAllOff}, {"AddAllDef", Stat.AddAllDef}, {"CriticalIncrease", Stat.CriticalIncrease}, {"XPModifier", Stat.XPModifier},
            // Vitals
            {"Health", Stat.Health}, {"CurrentNano", Stat.CurrentNano}, {"MaxNanoEnergy", Stat.MaxNanoEnergy},
            // Damage Modifiers
            {"MeleeDamageModifier", Stat.MeleeDamageModifier}, {"ProjectileDamageModifier", Stat.ProjectileDamageModifier}, {"EnergyDamageModifier", Stat.EnergyDamageModifier}, {"FireDamageModifier", Stat.FireDamageModifier}, {"ColdDamageModifier", Stat.ColdDamageModifier}, {"PoisonDamageModifier", Stat.PoisonDamageModifier}, {"RadiationDamageModifier", Stat.RadiationDamageModifier}, {"ChemicalDamageModifier", Stat.ChemicalDamageModifier},
            // Armor Classes (ACs)
            {"MeleeAC", Stat.MeleeAC}, {"ProjectileAC", Stat.ProjectileAC}, {"EnergyAC", Stat.EnergyAC}, {"FireAC", Stat.FireAC}, {"ColdAC", Stat.ColdAC},
            {"PoisonAC", Stat.PoisonAC},
            {"RadiationAC", Stat.RadiationAC}, {"ChemicalAC", Stat.ChemicalAC}, {"NanoAC", Stat.NanoResist}
        };

        private StatSnapshot _lastSnapshot = new StatSnapshot();

        public StatSnapshot Read()
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null)
                return _lastSnapshot;

            try
            {
                var snapshot = new StatSnapshot();
                foreach (var pair in _statMap)
                {
                    snapshot.Stats[pair.Key] = DynelManager.LocalPlayer.GetStat(pair.Value);
                }

                _lastSnapshot = snapshot;
                return snapshot;
            }
            catch (Exception)
            {
                return _lastSnapshot;
            }
        }
    }

    public sealed class StatService
    {
        private readonly StatProvider _provider;
        private readonly TimeSpan _tick = TimeSpan.FromMilliseconds(250);
        public event Action<StatSnapshot> OnSample;

        public StatService(StatProvider provider) => _provider = provider;

        public void Start(CancellationToken ct)
        {
            Task.Run(async () =>
            {
                DateTime next = DateTime.UtcNow;
                while (!ct.IsCancellationRequested)
                {
                    var snap = _provider.Read();
                    OnSample?.Invoke(snap);

                    next += _tick;
                    var delay = next - DateTime.UtcNow;
                    if (delay <= TimeSpan.Zero) delay = _tick;

                    try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                    catch (TaskCanceledException) { break; }
                }
            }, ct);
        }
    }
}