// NotumHudAPI.cs — API endpoints for NotumHUD game state streaming
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RubiKit.GameData
{
    /// <summary>
    /// Provides NotumHUD API endpoints:
    /// - GET /api/state : Full game state snapshot (JSON)
    /// - POST /api/cmd?action=X&value=Y : User commands (pin, settings, etc.)
    /// - GET /events : Server-Sent Events stream for real-time updates
    /// </summary>
    internal static class NotumHudAPI
    {
        private static GameStateProvider _stateProvider;
        private static UserPreferences _prefs;
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private static readonly List<SseClient> _sseClients = new List<SseClient>();
        private static Timer _sseTimer;
        private static readonly object _lock = new object();

        public static void Initialize(string pluginDir)
        {
            UserPreferences.Initialize(pluginDir);
            _stateProvider = new GameStateProvider();
            _prefs = UserPreferences.Load();

            // Register routes in the main API router
            ApiRouter.Register("/api/state", HandleState);
            ApiRouter.Register("/api/cmd", HandleCommand);
            ApiRouter.Register("/events", HandleSSE);

            // Start SSE broadcast timer (500ms updates)
            _sseTimer = new Timer(BroadcastSSE, null, 500, 500);
        }

        public static void Shutdown()
        {
            _sseTimer?.Dispose();
            lock (_lock)
            {
                foreach (var client in _sseClients)
                {
                    try { client.Response.Close(); } catch { }
                }
                _sseClients.Clear();
            }
        }

        // ====== /api/state ======
        private static void HandleState(HttpListenerContext ctx)
        {
            var stateJson = BuildStateJson();
            RespondJson(ctx, stateJson, 200);
        }

        // ====== /api/cmd ======
        private static void HandleCommand(HttpListenerContext ctx)
        {
            var query = ctx.Request.QueryString;
            var action = query["action"] ?? "";
            var value = query["value"] ?? "";

            switch (action.ToLowerInvariant())
            {
                case "pin_add":
                    _prefs.AddPin(value);
                    _prefs = UserPreferences.Load(); // Reload to sync
                    ctx.Response.StatusCode = 204;
                    break;

                case "pin_remove":
                    _prefs.RemovePin(value);
                    _prefs = UserPreferences.Load();
                    ctx.Response.StatusCode = 204;
                    break;

                case "theme":
                case "font":
                case "fontsize":
                    _prefs.SetSetting(action, value);
                    _prefs = UserPreferences.Load();
                    ctx.Response.StatusCode = 204;
                    break;

                case "set_category":
                    try
                    {
                        var obj = _json.Deserialize<Dictionary<string, object>>(value);
                        if (obj.ContainsKey("name") && obj.ContainsKey("category"))
                        {
                            _prefs.SetCategory(obj["name"].ToString(), obj["category"].ToString());
                            _prefs = UserPreferences.Load();
                        }
                        ctx.Response.StatusCode = 204;
                    }
                    catch
                    {
                        ctx.Response.StatusCode = 400;
                    }
                    break;

                case "ping":
                    RespondJson(ctx, "{\"ok\":true}", 200);
                    return;

                default:
                    ctx.Response.StatusCode = 400;
                    break;
            }

            try { ctx.Response.Close(); } catch { }
        }

        // ====== /events (SSE) ======
        private static void HandleSSE(HttpListenerContext ctx)
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.AddHeader("Cache-Control", "no-cache");
            ctx.Response.AddHeader("Connection", "keep-alive");
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            ctx.Response.StatusCode = 200;

            lock (_lock)
            {
                _sseClients.Add(new SseClient { Response = ctx.Response });
            }

            // Initial state push
            try
            {
                var stateJson = BuildStateJson();
                var sseMessage = "data: " + stateJson + "\n\n";
                var bytes = Encoding.UTF8.GetBytes(sseMessage);
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Flush();
            }
            catch { }

            // Connection stays open; timer will broadcast updates
        }

        private static void BroadcastSSE(object state)
        {
            lock (_lock)
            {
                if (_sseClients.Count == 0)
                    return;

                var stateJson = BuildStateJson();
                var sseMessage = "data: " + stateJson + "\n\n";
                var bytes = Encoding.UTF8.GetBytes(sseMessage);

                var deadClients = new List<SseClient>();

                foreach (var client in _sseClients)
                {
                    try
                    {
                        client.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        client.Response.OutputStream.Flush();
                    }
                    catch
                    {
                        deadClients.Add(client);
                    }
                }

                // Remove disconnected clients
                foreach (var dead in deadClients)
                {
                    _sseClients.Remove(dead);
                    try { dead.Response.Close(); } catch { }
                }
            }
        }

        // ====== HELPERS ======
        private static string BuildStateJson()
        {
            var stats = _stateProvider.GetAllStats();
            var statNames = _stateProvider.GetAllStatNames();
            var localIP = _stateProvider.GetLocalIP();

            // Update pin values from current stats
            foreach (var pin in _prefs.Pins)
            {
                if (stats.ContainsKey(pin.name))
                    pin.v = stats[pin.name];
            }

            var state = new Dictionary<string, object>
            {
                { "all", stats },
                { "all_names", statNames },
                { "pins", _prefs.Pins },
                { "settings", _prefs.Settings }
            };

            if (localIP != null)
                state["localIP"] = localIP;

            return _json.Serialize(state);
        }

        private static void RespondJson(HttpListenerContext ctx, string json, int statusCode)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            try { ctx.Response.OutputStream.Write(bytes, 0, bytes.Length); } catch { }
            try { ctx.Response.OutputStream.Flush(); } catch { }
            try { ctx.Response.Close(); } catch { }
        }

        private class SseClient
        {
            public HttpListenerResponse Response { get; set; }
        }
    }
}
