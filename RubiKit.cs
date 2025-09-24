// RubiKitBridge.cs
// .NET Framework 4.8 / C# 7.3 compatible.
// Refs: AOSharp.Core.dll, AOSharp.Common.dll, System.Web.Extensions

using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RubiKitBridge
{
    public class Main : AOPluginEntry
    {
        // === Configurable defaults ===
        private const int DEFAULT_PORT = 8780;
        private const bool DEFAULT_SERVE_STATIC = true;
        private const bool DEFAULT_AUTOSTART = true;
        private const string CONFIG_FILE = "RubiKit.json";
        private const string THEMES_FILE = "RubiKit-Themes.json";
        private const string SETTINGS_FILE = "RubiKit-Settings.json";

        // === internal state ===
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static string _pluginDir;
        private static string _webRoot;
        private static string _themeFile;
        private static string _settingsFile;
        private static string _configPath;
        private static int _port = DEFAULT_PORT;
        private static bool _serveStatic = DEFAULT_SERVE_STATIC;
        private static bool _autoStart = DEFAULT_AUTOSTART;

        private static readonly JavaScriptSerializer Json = NewJson();
        private static JavaScriptSerializer NewJson()
        {
            var j = new JavaScriptSerializer();
            j.MaxJsonLength = int.MaxValue;
            return j;
        }

        // Public version for modules to check
        private static readonly string RubiKitVersion = "1.0.0";

        // Logger
        private static readonly SafeLogger Log = new SafeLogger();

        // One-time diagnostic marker to avoid spam
        private static bool _loggedStaticIssue = false;

        [Obsolete("AOSharp still calls Run; this override is intentional.", false)]
        public override void Run(string pluginDir)
        {
            _pluginDir = pluginDir;
            _webRoot = Path.Combine(pluginDir, "RubiKit");
            _themeFile = Path.Combine(pluginDir, THEMES_FILE);
            _settingsFile = Path.Combine(pluginDir, SETTINGS_FILE);
            _configPath = Path.Combine(pluginDir, CONFIG_FILE);

            Directory.CreateDirectory(_webRoot);

            // Load config if present (non-fatal)
            LoadConfigOverrides(pluginDir);

            // Clean startup UX
            PresentStartupSplash();

            // Start server if configured
            if (_serveStatic && _autoStart)
                StartServer();
            else
                Log.Info("HTTP bridge disabled by config.");

            // Register commands
            RegisterCommands();

            Log.Info("RubiKit ready (v" + RubiKitVersion + ").");
        }

        public override void Teardown()
        {
            StopServer();
            Log.Info("RubiKit unloaded.");
        }

        // ---------------- Commands ----------------
        private void RegisterCommands()
        {
            Chat.RegisterCommand("rubi", (cmd, args, wnd) =>
            {
                if (!_serveStatic)
                {
                    Chat.WriteLine("[RubiKit] Bridge disabled. Use the module launcher to open Script Studio.");
                    return;
                }
                var url = $"http://127.0.0.1:{_port}/";
                TryOpen(url);
                Chat.WriteLine("[RubiKit] Opened " + url);
            });

            Chat.RegisterCommand("scriptstudio", (cmd, a, w) =>
            {
                if (!_serveStatic) { Chat.WriteLine("[RubiKit] Bridge disabled."); return; }
                var url = $"http://127.0.0.1:{_port}/ScriptStudio/index.html";
                TryOpen(url);
                Chat.WriteLine("[RubiKit] Script Studio opened.");
            });

            Chat.RegisterCommand("rubirestart", (cmd, a, w) =>
            {
                StopServer(); StartServer();
                Chat.WriteLine($"[RubiKit] Server restarted on http://127.0.0.1:{_port}/");
            });

            Chat.RegisterCommand("rubipath", (cmd, a, w) =>
            {
                Chat.WriteLine("[RubiKit] pluginDir=" + SanitizePath(_pluginDir));
                Chat.WriteLine("[RubiKit] webRoot=" + SanitizePath(_webRoot));
                Chat.WriteLine("[RubiKit] port=" + _port);
                Chat.WriteLine("[RubiKit] indexExists=" + File.Exists(Path.Combine(_webRoot, "index.html")));
            });

            Chat.RegisterCommand("rubidebug", (cmd, a, w) =>
            {
                Log.ToggleVerbose();
                Chat.WriteLine("[RubiKit] Debug mode " + (Log.IsVerbose ? "ENABLED" : "DISABLED"), ChatColor.Green);
            });

            Chat.RegisterCommand("rubiclearlog", (cmd, a, w) =>
            {
                Log.Clear();
                Chat.WriteLine("[RubiKit] In-memory log cleared.");
            });
        }

        // ---------------- Startup UX ----------------
        private void PresentStartupSplash()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[RubiKit] RubiKit v" + RubiKitVersion + " loaded.");
            sb.AppendLine("[RubiKit] Modules directory: " + SanitizePath(_webRoot));
            if (_serveStatic)
                sb.AppendLine("[RubiKit] Web bridge: http://127.0.0.1:" + _port + "/ (serving index.html by default)");
            else
                sb.AppendLine("[RubiKit] Web bridge disabled (serveStatic=false)");
            sb.AppendLine("[RubiKit] Commands: /rubi /scriptstudio /rubirestart /rubipath /rubidebug");

            foreach (var line in sb.ToString().Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                Chat.WriteLine(line, ChatColor.Green);
        }

        // Default landing: ALWAYS the main RubiKit page
        private string ResolveDefaultIndex()
        {
            return "/index.html";
        }

        // ---------------- Config ----------------
        private void LoadConfigOverrides(string pluginDir)
        {
            try
            {
                var cfg = Path.Combine(pluginDir, CONFIG_FILE);
                if (!File.Exists(cfg)) return;
                var txt = File.ReadAllText(cfg);
                var dict = FromJson<Dictionary<string, object>>(txt);
                if (dict == null) return;

                if (dict.TryGetValue("Port", out var pObj) && int.TryParse(Convert.ToString(pObj), out var p) && p > 0 && p < 65536)
                    _port = p;
                if (dict.TryGetValue("ServeStatic", out var sObj) && bool.TryParse(Convert.ToString(sObj), out var s)) _serveStatic = s;
                if (dict.TryGetValue("AutoStartServer", out var aObj) && bool.TryParse(Convert.ToString(aObj), out var a)) _autoStart = a;

                Log.Info("Loaded config overrides.");
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to load config: " + ex.Message);
            }
        }

        // ---------------- HTTP server ----------------
        private void StartServer()
        {
            if (!_serveStatic) { Log.Info("ServeStatic disabled; not starting HTTP listener."); return; }
            if (_listener != null) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                Task.Run(() => AcceptLoop(_cts.Token));
                Log.Info("HTTP bridge listening on 127.0.0.1:" + _port);
            }
            catch (Exception ex)
            {
                Log.Error("HttpListener failed: " + ex.Message);
                _listener = null;
            }
        }

        private void StopServer()
        {
            try
            {
                _cts?.Cancel();
                if (_listener != null && _listener.IsListening) _listener.Stop();
                _listener?.Close();
            }
            catch { }
            finally
            {
                _listener = null;
                _cts = null;
            }
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx = null;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                if (ctx != null) _ = Task.Run(() => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            if (Log.IsVerbose)
                Chat.WriteLine($"[RubiKit:INFO] {req.HttpMethod} {req.Url.AbsolutePath}", ChatColor.Green);

            try
            {
                var path = (req.Url.AbsolutePath ?? "/");
                if (path == "/") path = ResolveDefaultIndex();

                // --- /rubi/* health
                if (path.StartsWith("/rubi/", StringComparison.OrdinalIgnoreCase))
                {
                    HandleRubi(ctx, path);
                    return;
                }

                // --- /api/* info
                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    if (path == "/api/ping") { WriteJson(res, "{\"ok\":true,\"name\":\"RubiKitBridge\",\"version\":\"" + RubiKitVersion + "\"}"); return; }
                    if (path == "/api/info") { WriteJson(res, BuildInfoJson()); return; }
                    res.StatusCode = 404; res.Close(); return;
                }

                // --- Static files
                try
                {
                    // Alias fixups for accidental root references
                    var alias = ResolveStaticAlias(path);
                    if (alias != null) path = alias;

                    var full = SafeMap(path);
                    if (File.Exists(full))
                    {
                        res.AddHeader("Cache-Control", "no-cache");
                        res.ContentType = GuessContentType(full);
                        using (var fs = File.OpenRead(full)) fs.CopyTo(res.OutputStream);
                        res.OutputStream.Close();
                    }
                    else
                    {
                        TryLogOnce($"[RubiKit] 404 static: {path} -> {SanitizePath(full)}");
                        res.StatusCode = 404;
                        var msg = Encoding.UTF8.GetBytes("Not found");
                        res.OutputStream.Write(msg, 0, msg.Length);
                        res.OutputStream.Close();
                    }
                }
                catch (Exception ex)
                {
                    TryLogOnce($"[RubiKit] Static mapping error for '{path}': {ex.Message}");
                    res.StatusCode = 404;
                    var bytes = Encoding.UTF8.GetBytes("Not found");
                    res.OutputStream.Write(bytes, 0, bytes.Length);
                    res.OutputStream.Close();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    res.StatusCode = 500;
                    var bytes = Encoding.UTF8.GetBytes("Server error");
                    res.OutputStream.Write(bytes, 0, bytes.Length);
                    res.OutputStream.Close();
                }
                catch { }
                Log.Error("Unhandled request error: " + ex.Message);
            }
        }

        // Map accidental root requests to ScriptStudio assets
        private string ResolveStaticAlias(string path)
        {
            var p = (path ?? "").ToLowerInvariant();
            if (p == "/scriptstudio.css") return "/ScriptStudio/scriptstudio.css";
            if (p == "/scriptstudio.js") return "/ScriptStudio/scriptstudio.js";
            if (p == "/icon.svg" && File.Exists(Path.Combine(_webRoot, "ScriptStudio", "icon.svg")))
                return "/ScriptStudio/icon.svg";
            return null;
        }

        // ---------------- /rubi handlers ----------------
        private void HandleRubi(HttpListenerContext ctx, string path)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            if (req.HttpMethod == "GET" && path == "/rubi/ping")
            {
                WriteJson(res, JsonSerialize(new { ok = true, pong = true, port = _port, version = RubiKitVersion }));
                return;
            }

            // Unknown /rubi route
            res.StatusCode = 404;
            res.Close();
        }

        // ---------------- helpers ----------------
        private string BuildInfoJson()
        {
            var indexExists = false;
            try { indexExists = File.Exists(Path.Combine(_webRoot ?? "", "index.html")); } catch { }
            var info = new Dictionary<string, object> {
                {"ok", true},
                {"version", RubiKitVersion},
                {"port", _port},
                {"pluginDir", SanitizePath(_pluginDir)},
                {"webRoot", SanitizePath(_webRoot)},
                {"indexExists", indexExists},
                {"time", DateTime.UtcNow.ToString("o")}
            };
            return JsonSerialize(info);
        }

        private static void WriteJson(HttpListenerResponse res, string json)
        {
            res.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }

        private static string JsonSerialize(object o)
        {
            try { return Json.Serialize(o); } catch { return "{}"; }
        }

        // Sanitize path for chat output (no home path leaks)
        private static string SanitizePath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return "N/A";
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = path.Substring(home.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var outp = Path.Combine("~", rest);
                    return outp.Length > 160 ? outp.Substring(0, 140) + "..." : outp;
                }
                return path.Length > 160 ? "..." + path.Substring(path.Length - 140) : path;
            }
            catch { return "N/A"; }
        }

        // Hardened path mapper for static files
        private string SafeMap(string urlPath)
        {
            string raw = urlPath ?? "/";
            int q = raw.IndexOf('?'); if (q >= 0) raw = raw.Substring(0, q);
            int h = raw.IndexOf('#'); if (h >= 0) raw = raw.Substring(0, h);
            string unescaped;
            try { unescaped = Uri.UnescapeDataString(raw); } catch { unescaped = raw; }

            string rel = unescaped.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrEmpty(rel)) rel = "index.html";

            // Remove drive letters and colons
            if (rel.Length >= 2 && char.IsLetter(rel[0]) && rel[1] == ':') rel = rel.Substring(2);
            rel = rel.Replace(":", "");

            // Remove traversal tokens
            while (rel.Contains("../")) rel = rel.Replace("../", "");
            while (rel.Contains("..\\")) rel = rel.Replace("..\\", "");

            var normalizedRoot = Path.GetFullPath(_webRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.Combine(_webRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            var resolved = Path.GetFullPath(candidate);

            if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path traversal blocked");

            return resolved;
        }

        private static string GuessContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                default: return "application/octet-stream";
            }
        }

        private static T FromJson<T>(string text)
        {
            try { return Json.Deserialize<T>(text); } catch { return default(T); }
        }

        private void TryOpen(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("Open browser failed: " + ex.Message); }
        }

        // One-time diagnostic logger to avoid chat spam
        private void TryLogOnce(string msg)
        {
            if (_loggedStaticIssue) return;
            _loggedStaticIssue = true;
            try { Chat.WriteLine(msg, ChatColor.Orange); } catch { }
            Log.Warn(msg);
        }

        // ---------------- SafeLogger ----------------
        private sealed class SafeLogger
        {
            private readonly List<string> _lines = new List<string>();
            private bool _verbose = false;
            public bool IsVerbose => _verbose;

            private static string Sanitize(string s)
            {
                if (string.IsNullOrEmpty(s)) return s;
                try
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!string.IsNullOrEmpty(home) && s.IndexOf(home, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s.Replace(home, "~");
                    return s;
                }
                catch { return s; }
            }

            public void ToggleVerbose() => _verbose = !_verbose;
            public void Clear() { lock (_lines) { _lines.Clear(); } }

            public void Info(string msg)
            {
                var t = Format("INFO", msg);
                Append(t);
                if (_verbose) Chat.WriteLine(t, ChatColor.Green);
            }

            public void Warn(string msg)
            {
                var t = Format("WARN", msg);
                Append(t);
                Chat.WriteLine(t, ChatColor.Orange);
            }

            public void Error(string msg)
            {
                var t = Format("ERROR", msg);
                Append(t);
                Chat.WriteLine(t, ChatColor.Gold);
            }

            private string Format(string level, string msg) => $"[RubiKit:{level}] {Sanitize(msg)}";

            private void Append(string line)
            {
                lock (_lines)
                {
                    _lines.Add($"{DateTime.UtcNow:O} {line}");
                    if (_lines.Count > 1000) _lines.RemoveAt(0);
                }
            }

            public string GetRecent(int lines = 200)
            {
                lock (_lines) { return string.Join("\n", _lines.Count <= lines ? _lines : _lines.GetRange(_lines.Count - lines, lines)); }
            }
        }
    }
}
