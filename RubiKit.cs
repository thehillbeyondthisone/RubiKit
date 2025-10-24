// RubiKit.cs — C# 7.3 compatible; serves real files instead of "OK"
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;

namespace RubiKit
{
    public class RubiKit : AOPluginEntry
    {
        public static readonly string Version = "6.0.0";
        public static readonly string Name = "RubiKit";
        public static readonly string Github = "github.com/your-org/RubiKit";
        public static readonly int HttpPort = 8780;

        private static HttpListener _http;
        private static readonly RubiLogger Log = new RubiLogger();
        private static readonly TerminalBridge Terminal = new TerminalBridge(Log);
        private static bool _uiMounted;
        private static string _webRoot = "";   // e.g., <pluginDir>\Web

        [Obsolete("AOSharp marks Run obsolete; still required by loader.")]
        public override void Run(string pluginDir)
        {
            // Where your HTML/JS/CSS live (adjust if you use a different subfolder)
            _webRoot = Path.Combine(pluginDir ?? "", "RubiKit");
            if (!Directory.Exists(_webRoot))
                Directory.CreateDirectory(_webRoot); // won't throw if already exists

            Chat.WriteLine(
                "<font color='#7ee787'>[" + Name + "]</font> v" + Version +
                " loaded. Use <font color='#58a6ff'>/rubi</font> to open, <font color='#58a6ff'>/about</font> for info.");

            // AOSharp command signature: Action<string,string[],ChatWindow>
            Chat.RegisterCommand("rubi", (cmd, args, wnd) => OpenRubi());
            Chat.RegisterCommand("about", (cmd, args, wnd) => ShowAbout());

            // Initialize NotumHUD API endpoints
            GameData.NotumHudAPI.Initialize(pluginDir);

            StartHttp();
            Log.Info(Name + " boot complete. HTTP :" + HttpPort + " root=" + _webRoot);
        }

        public override void Teardown()
        {
            try
            {
                // Shutdown NotumHUD API
                GameData.NotumHudAPI.Shutdown();

                if (_http != null)
                {
                    _http.Stop();
                    _http.Close();
                }
            }
            catch { }
            Log.Info("Teardown complete.");
        }

        private static void OpenRubi()
        {
            try
            {
                if (!_uiMounted)
                {
                    Terminal.PushSystem("Mounting UI …");
                    _uiMounted = true;
                }

                // Open default browser to boot or index (whichever exists)
                var boot = File.Exists(Path.Combine(_webRoot, "boot.html")) ? "boot.html" :
                           File.Exists(Path.Combine(_webRoot, "index.html")) ? "index.html" : null;

                var target = boot != null ? ("http://127.0.0.1:" + HttpPort + "/" + boot)
                                          : ("http://127.0.0.1:" + HttpPort + "/");
                try { System.Diagnostics.Process.Start(target); }
                catch (Exception ex) { Log.Warn("Failed to open browser: " + ex.Message); }

                Terminal.PushSystem("Open " + target);
            }
            catch (Exception ex)
            {
                Log.Error("OpenRubi failed: " + ex.Message);
            }
        }

        private static void ShowAbout()
        {
            var asm = Assembly.GetExecutingAssembly();
            var build = (asm != null && asm.GetName() != null && asm.GetName().Version != null)
                ? asm.GetName().Version.ToString()
                : "n/a";

            var sb = new StringBuilder();
            sb.AppendLine("<font color='#7ee787'>" + Name + "</font> v" + Version);
            sb.AppendLine("Build: " + build);
            sb.AppendLine("Commands: <font color='#58a6ff'>/rubi</font>, <font color='#58a6ff'>/about</font>");
            sb.AppendLine("HTTP: http://localhost:" + HttpPort + "/");
            sb.AppendLine("Source: " + Github);
            Chat.WriteLine(sb.ToString());
        }

        private static void StartHttp()
        {
            _http = new HttpListener();
            _http.Prefixes.Add("http://127.0.0.1:" + HttpPort + "/");
            _http.Prefixes.Add("http://localhost:" + HttpPort + "/");
            _http.Start();
            Task.Run(HttpLoop);
        }

        private static async Task HttpLoop()
        {
            while (_http != null && _http.IsListening)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = await _http.GetContextAsync().ConfigureAwait(false);

                    var method = ctx.Request.HttpMethod ?? "GET";
                    var rawUrl = ctx.Request.RawUrl ?? "/";
                    Terminal.PushHttp(method + " " + rawUrl);

                    // First: API routes
                    if (ApiRouter.TryDispatch(ctx))
                        continue;

                    // Static files
                    StaticFileServer.Serve(_webRoot, ctx);
                }
                catch (HttpListenerException)
                {
                    // listener stopped
                }
                catch (Exception ex)
                {
                    Log.Error("HTTP error: " + ex.Message);
                    if (ctx != null)
                    {
                        ctx.Response.StatusCode = 500;
                        var err = Encoding.UTF8.GetBytes("500");
                        try { ctx.Response.OutputStream.Write(err, 0, err.Length); } catch { }
                        try { ctx.Response.Close(); } catch { }
                    }
                }
            }
        }
    }

    // -------- Static file server (no more "OK") --------
    internal static class StaticFileServer
    {
        // default documents we try (first found wins)
        private static readonly string[] DefaultDocs = new string[] { "boot.html", "index.html" };

        public static void Serve(string root, HttpListenerContext ctx)
        {
            var reqPath = (ctx.Request.RawUrl ?? "/");
            var qIdx = reqPath.IndexOf('?');
            if (qIdx >= 0) reqPath = reqPath.Substring(0, qIdx);

            if (reqPath == "/") // default docs
            {
                for (int i = 0; i < DefaultDocs.Length; i++)
                {
                    var candidate = Path.Combine(root, DefaultDocs[i]);
                    if (File.Exists(candidate))
                    {
                        SendFile(ctx, candidate, GetMime(candidate), 200);
                        return;
                    }
                }
                // nothing found, 404
                SendText(ctx, "Not Found", 404);
                return;
            }

            // Normalize and prevent path traversal
            reqPath = reqPath.Replace('/', Path.DirectorySeparatorChar);
            if (reqPath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                reqPath = reqPath.Substring(1);

            var full = Path.GetFullPath(Path.Combine(root, reqPath));
            var fullRoot = Path.GetFullPath(root);

            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                SendText(ctx, "Forbidden", 403);
                return;
            }

            if (Directory.Exists(full))
            {
                // try default docs inside the folder
                for (int i = 0; i < DefaultDocs.Length; i++)
                {
                    var index = Path.Combine(full, DefaultDocs[i]);
                    if (File.Exists(index))
                    {
                        SendFile(ctx, index, GetMime(index), 200);
                        return;
                    }
                }
                SendText(ctx, "Not Found", 404);
                return;
            }

            if (!File.Exists(full))
            {
                SendText(ctx, "Not Found", 404);
                return;
            }

            SendFile(ctx, full, GetMime(full), 200);
        }

        private static void SendFile(HttpListenerContext ctx, string path, string mime, int code)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                ctx.Response.StatusCode = code;
                ctx.Response.ContentType = mime;
                // simple cache headers for static assets
                if (mime != "text/html")
                    ctx.Response.AddHeader("Cache-Control", "public, max-age=120");

                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch
            {
                try { SendText(ctx, "500", 500); } catch { }
                return;
            }
            finally
            {
                try { ctx.Response.OutputStream.Flush(); } catch { }
                try { ctx.Response.Close(); } catch { }
            }
        }

        private static void SendText(HttpListenerContext ctx, string body, int code)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "");
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            try { ctx.Response.OutputStream.Write(bytes, 0, bytes.Length); } catch { }
            try { ctx.Response.OutputStream.Flush(); } catch { }
            try { ctx.Response.Close(); } catch { }
        }

        // minimal set; add as needed
        private static string GetMime(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".html" || ext == ".htm") return "text/html; charset=utf-8";
            if (ext == ".js") return "application/javascript; charset=utf-8";
            if (ext == ".mjs") return "application/javascript; charset=utf-8";
            if (ext == ".css") return "text/css; charset=utf-8";
            if (ext == ".svg") return "image/svg+xml";
            if (ext == ".png") return "image/png";
            if (ext == ".jpg" || ext == ".jpeg") return "image/jpeg";
            if (ext == ".gif") return "image/gif";
            if (ext == ".webp") return "image/webp";
            if (ext == ".json") return "application/json; charset=utf-8";
            if (ext == ".txt" || ext == ".log") return "text/plain; charset=utf-8";
            if (ext == ".wasm") return "application/wasm";
            if (ext == ".ico") return "image/x-icon";
            return "application/octet-stream";
        }
    }

    // -------- Logger -> Terminal only (never AO chat) --------
    internal class RubiLogger
    {
        public enum Level { Debug, Info, Warn, Error }
        public Level MinimumLevel = Level.Info;
        public event Action<Level, string> OnLog;

        public void Debug(string msg) { Emit(Level.Debug, msg); }
        public void Info(string msg) { Emit(Level.Info, msg); }
        public void Warn(string msg) { Emit(Level.Warn, msg); }
        public void Error(string msg) { Emit(Level.Error, msg); }

        private void Emit(Level level, string msg)
        {
            if ((int)level < (int)MinimumLevel) return;
            var onLog = OnLog;
            if (onLog != null) onLog(level, msg);
        }

        public static string Prefix(Level level)
        {
            if (level == Level.Debug) return "[DBG]";
            if (level == Level.Info) return "[INF]";
            if (level == Level.Warn) return "[WRN]";
            if (level == Level.Error) return "[ERR]";
            return "[???]";
        }
    }

    // -------- Terminal bridge + API endpoints --------
    internal class TerminalBridge
    {
        private readonly RubiLogger _log;
        private readonly ConcurrentQueue<string> _ring = new ConcurrentQueue<string>();
        private const int RingMax = 5000;

        public TerminalBridge(RubiLogger log)
        {
            _log = log;
            _log.OnLog += OnLog;

            ApiRouter.Register("/api/terminal/pull", PullHandler);
            ApiRouter.Register("/api/terminal/push", PushHandler);
            ApiRouter.Register("/api/debug/toggle", ToggleHandler);
            ApiRouter.Register("/api/debug/level", LevelHandler);
            ApiRouter.Register("/api/manifest/clear", ManifestClearHandler);
        }

        private void OnLog(RubiLogger.Level lvl, string msg)
        {
            Push(RubiLogger.Prefix(lvl) + " " + msg);
        }

        public void PushHttp(string line) { Push("[HTTP] " + line); }
        public void PushSystem(string line) { Push("[SYS] " + line); }

        private void Push(string line)
        {
            _ring.Enqueue(DateTime.Now.ToString("HH:mm:ss") + " " + line);
            while (_ring.Count > RingMax && _ring.TryDequeue(out _)) { }
        }

        private void PullHandler(HttpListenerContext ctx)
        {
            var sb = new StringBuilder();
            foreach (var ln in _ring)
                sb.AppendLine(ln);

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            try { ctx.Response.OutputStream.Write(bytes, 0, bytes.Length); } catch { }
            try { ctx.Response.OutputStream.Flush(); } catch { }
            try { ctx.Response.Close(); } catch { }
        }

        private void PushHandler(HttpListenerContext ctx)
        {
            try
            {
                string body;
                using (var rdr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                {
                    body = rdr.ReadToEnd();
                }
                Push("[CLI] " + body);
                ctx.Response.StatusCode = 204;
            }
            catch { ctx.Response.StatusCode = 400; }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        private void ToggleHandler(HttpListenerContext ctx)
        {
            var q = ctx.Request.QueryString;
            var httpEcho = string.Equals(q["httpEcho"], "true", StringComparison.OrdinalIgnoreCase);
            var chatEcho = string.Equals(q["chatEcho"], "true", StringComparison.OrdinalIgnoreCase);

            PushSystem("Debug toggles updated. httpEcho=" + httpEcho + " chatEcho=" + chatEcho);
            ctx.Response.StatusCode = 204;
            try { ctx.Response.Close(); } catch { }
        }

        private void LevelHandler(HttpListenerContext ctx)
        {
            var q = ctx.Request.QueryString;
            RubiLogger.Level lvl;
            if (Enum.TryParse<RubiLogger.Level>((q["min"] ?? ""), true, out lvl))
            {
                _log.MinimumLevel = lvl;
                PushSystem("Log level set to " + lvl);
                ctx.Response.StatusCode = 204;
            }
            else
            {
                ctx.Response.StatusCode = 400;
            }
            try { ctx.Response.Close(); } catch { }
        }

        private void ManifestClearHandler(HttpListenerContext ctx)
        {
            try
            {
                ManifestStore.Clear();
                PushSystem("Loaded modules cleared.");
                ctx.Response.StatusCode = 204;
            }
            catch (Exception ex)
            {
                _log.Error("Manifest clear failed: " + ex.Message);
                ctx.Response.StatusCode = 500;
            }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }

    // -------- Minimal API router --------
    internal static class ApiRouter
    {
        private static readonly Dictionary<string, Action<HttpListenerContext>> _routes =
            new Dictionary<string, Action<HttpListenerContext>>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string path, Action<HttpListenerContext> handler)
        {
            if (path.Length > 1 && path.EndsWith("/"))
                path = path.Substring(0, path.Length - 1);

            if (_routes.ContainsKey(path))
                _routes[path] = handler;
            else
                _routes.Add(path, handler);
        }

        public static bool TryDispatch(HttpListenerContext ctx)
        {
            var raw = ctx.Request.RawUrl ?? "/";
            var qIdx = raw.IndexOf('?');
            if (qIdx >= 0) raw = raw.Substring(0, qIdx);
            if (raw.Length > 1 && raw.EndsWith("/"))
                raw = raw.Substring(0, raw.Length - 1);

            Action<HttpListenerContext> handler;
            if (_routes.TryGetValue(raw, out handler))
            {
                handler(ctx);
                return true;
            }
            return false;
        }
    }

    // -------- Your manifest state hook (stub) --------
    internal static class ManifestStore
    {
        public static void Clear()
        {
            // wipe in-memory registry and (optionally) persisted modules.json
            // UI will refresh after POST
        }
    }
}
