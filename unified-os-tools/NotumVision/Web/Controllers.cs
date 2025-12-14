using AOSharp.Core.UI;
using NotumHUD.Services;
using NotumHUD.Settings;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NotumHUD.Web
{
    /// <summary>
    /// Handles commands to modify plugin settings.
    /// </summary>
    public class SettingsController
    {
        private readonly SettingsService _settingsService;
        private readonly StatService _statService;

        public SettingsController(SettingsService settingsService, StatService statService)
        {
            _settingsService = settingsService;
            _statService = statService;
        }

        public async Task GetCurrentConfigAsync(HttpListenerResponse response)
        {
            var settings = _settingsService.GetSettings();
            await ResponseHelper.SendJsonAsync(response, settings);
        }

        public async Task HandleCommandAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            var query = request.QueryString;
            string action = query["action"];
            string value = query["value"];

            _settingsService.Update(settings =>
            {
                switch (action)
                {
                    case "enable":
                        _statService.Toggle(Truthy(value));
                        break;
                    case "interval_ms":
                        if (int.TryParse(value, out int ms))
                        {
                            settings.IntervalMs = Math.Max(100, Math.Min(5000, ms));
                        }
                        break;
                    case "theme":
                        if (!string.IsNullOrWhiteSpace(value)) settings.Theme = value;
                        break;
                    case "compact":
                        settings.Compact = Truthy(value);
                        break;
                    case "pin_add":
                        if (!string.IsNullOrEmpty(value)) settings.Pins.Add(value);
                        break;
                    case "pin_remove":
                        settings.Pins.Remove(value);
                        break;
                    case "misc_hide_add":
                        if (!string.IsNullOrEmpty(value)) settings.HiddenMisc.Add(value);
                        break;
                    case "misc_hide_remove":
                        settings.HiddenMisc.Remove(value);
                        break;
                    case "misc_hide_clear":
                        settings.HiddenMisc.Clear();
                        break;
                    case "panel_order_set":
                        if (!string.IsNullOrEmpty(value))
                        {
                            settings.PanelOrder = value.Split(',').ToList();
                        }
                        break;
                }
            });
            
            await ResponseHelper.SendJsonAsync(response, new { ok = true });
        }

        private static bool Truthy(string v) => !string.IsNullOrEmpty(v) && (v == "true" || v == "1" || v == "on" || v == "yes");
    }

    /// <summary>
    /// Handles establishing and managing Server-Sent Events (SSE) connections.
    /// </summary>
    public class SseController
    {
        private readonly SseManager _sseManager;
        public SseController(SseManager sseManager) => _sseManager = sseManager;
        public Task HandleConnectionAsync(HttpListenerContext context) => _sseManager.AddClient(context);
    }
    
    /// <summary>
    /// Handles serving embedded UI files.
    /// </summary>
    public class ResourceController
    {
        public async Task ServeFileAsync(HttpListenerResponse response, string resourceName, string contentType)
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Namespace is NotumHUD, resources are in UI folder.
            var fullResourceName = $"NotumHUD.{resourceName}"; 

            using (var stream = assembly.GetManifestResourceStream(fullResourceName))
            {
                if (stream == null)
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    Chat.WriteLine($"[NotumHUD] Resource not found: {fullResourceName}");
                    return;
                }

                response.ContentType = contentType;
                response.ContentEncoding = Encoding.UTF8;
                
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string content = await reader.ReadToEndAsync();
                    await ResponseHelper.SendStringAsync(response, content, contentType);
                }
            }
        }
    }
}