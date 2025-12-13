// NotumHUD: Aetherium v3 by Gemini
// - A complete architectural redesign for clarity, performance, and maintainability.
// - Features a modular service-based architecture.
// - Utilizes Newtonsoft.Json for robust settings and API handling.
// - Asynchronous web server for improved responsiveness.
// - UI assets are now managed as clean, separate embedded resources.

using AOSharp.Core;
using AOSharp.Core.UI;
using NotumHUD.Services;
using NotumHUD.Settings;
using SimpleInjector;
using System;
using System.IO;

namespace NotumHUD
{
    public class PluginCore : AOPluginEntry
    {
        private Container _container;

        [Obsolete("AOSharp requires overriding an obsolete Run signature.")]
        public override void Run(string pluginDir)
        {
            try
            {
                // Set up the dependency injection container
                _container = new Container();
                _container.RegisterSingleton(() => new SettingsService(pluginDir));
                _container.RegisterSingleton<StatService>();
                _container.RegisterSingleton<WebServerService>();
                _container.Verify();

                // Start services
                var webServer = _container.GetInstance<WebServerService>();
                webServer.Start();

                Chat.RegisterCommand("stat", (cmd, args, a) =>
                {
                    var settings = _container.GetInstance<SettingsService>().GetSettings();
                    Chat.WriteLine($"NotumHUD: http://127.0.0.1:{settings.Port}/  |  Overlay: /overlay");
                });

                Chat.WriteLine($"[NotumHUD] Aetherium v3 loaded. WebUI: http://127.0.0.1:{_container.GetInstance<SettingsService>().GetSettings().Port}");

                Game.OnUpdate += OnUpdate;
            }
            catch (Exception e)
            {
                Chat.WriteLine($"[NotumHUD] Error loading plugin: {e.Message}");
            }
        }

        private void OnUpdate(object s, float e)
        {
            _container.GetInstance<StatService>().Tick(e);
        }

        public override void Teardown()
        {
            Game.OnUpdate -= OnUpdate;
            _container.GetInstance<WebServerService>()?.Stop();
            _container.Dispose();
            Chat.WriteLine("[NotumHUD] Aetherium v3 stopped.");
        }
    }
}