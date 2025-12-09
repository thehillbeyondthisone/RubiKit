using AOSharp.Core.UI;
using Newtonsoft.Json;
using NotumHUD.Settings;
using NotumHUD.Web;
using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NotumHUD.Services
{
    public class WebServerService
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts;
        private readonly RequestRouter _router;
        private readonly SseManager _sseManager;
        private Task _serverTask;

        public WebServerService(SettingsService settingsService, StatService statService)
        {
            var settings = settingsService.GetSettings();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{settings.Port}/");
            _listener.Prefixes.Add($"http://localhost:{settings.Port}/");

            _cts = new CancellationTokenSource();
            _sseManager = new SseManager();
            _router = new RequestRouter(settingsService, statService, _sseManager);
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                _serverTask = Task.Run(() => ListenAsync(_cts.Token));
            }
            catch (HttpListenerException hlex) when (hlex.ErrorCode == 5) // Access denied
            {
                Chat.WriteLine("[NotumHUD] HTTP start failed: Access Denied.");
                var port = _listener.Prefixes.First().Split(':')[2].TrimEnd('/');
                Chat.WriteLine($"[NotumHUD] Please run this command as an Administrator:");
                Chat.WriteLine($"  netsh http add urlacl url=http://+:{port}/ user=Everyone");
            }
            catch (Exception ex)
            {
                Chat.WriteLine($"[NotumHUD] Failed to start web server: {ex.Message}");
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            _sseManager.Shutdown();
            try
            {
                _listener.Stop();
                _serverTask?.Wait(2000);
            }
            catch { /* Ignore exceptions on shutdown */ }
            _cts.Dispose();
        }

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => _router.HandleRequestAsync(context), token);
                }
                catch (HttpListenerException) when (token.IsCancellationRequested)
                {
                    break; // Listener was stopped
                }
                catch (Exception ex)
                {
                    Chat.WriteLine($"[NotumHUD] Web server error: {ex.Message}");
                }
            }
        }

        public void BroadcastSnapshot(StatSnapshot snapshot)
        {
            string json = JsonConvert.SerializeObject(snapshot);
            _sseManager.Broadcast(json);
        }
    }
}