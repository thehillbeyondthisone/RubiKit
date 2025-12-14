using NotumHUD.Services;
using NotumHUD.Settings;
using System;
using System.Net;
using System.Threading.Tasks;

namespace NotumHUD.Web
{
    public class RequestRouter
    {
        private readonly SettingsController _settingsController;
        private readonly ResourceController _resourceController;
        private readonly SseController _sseController;

        public RequestRouter(SettingsService settings, StatService statService, SseManager sseManager)
        {
            _settingsController = new SettingsController(settings, statService);
            _resourceController = new ResourceController();
            _sseController = new SseController(sseManager);
        }

        public async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                switch (request.Url.AbsolutePath)
                {
                    case "/":
                    case "/index.html":
                        await _resourceController.ServeFileAsync(response, "UI.index.html", "text/html");
                        break;
                    case "/app.css":
                        await _resourceController.ServeFileAsync(response, "UI.app.css", "text/css");
                        break;
                    case "/app.js":
                        await _resourceController.ServeFileAsync(response, "UI.app.js", "application/javascript");
                        break;
                    case "/overlay":
                         await _resourceController.ServeFileAsync(response, "UI.overlay.html", "text/html");
                        break;
                    case "/overlay.js":
                         await _resourceController.ServeFileAsync(response, "UI.overlay.js", "application/javascript");
                        break;
                    case "/api/config":
                        await _settingsController.GetCurrentConfigAsync(response);
                        break;
                    case "/api/cmd":
                        await _settingsController.HandleCommandAsync(request, response);
                        break;
                    case "/events":
                        await _sseController.HandleConnectionAsync(context);
                        break;
                    case "/health":
                        await ResponseHelper.SendStringAsync(response, "OK", "text/plain");
                        break;
                    // Note: /api/groups and /api/themes are now handled by the ResourceController
                    case "/api/groups":
                        await _resourceController.ServeFileAsync(response, "UI.groups.json", "application/json");
                        break;
                    case "/api/themes":
                         await _resourceController.ServeFileAsync(response, "UI.themes.json", "application/json");
                        break;
                    default:
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        break;
                }
            }
            catch (Exception ex)
            {
                if (!response.OutputStream.CanWrite) return;
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await ResponseHelper.SendStringAsync(response, $"Error: {ex.Message}");
            }
            finally
            {
                response.Close();
            }
        }
    }
}