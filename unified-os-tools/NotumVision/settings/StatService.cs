using AOSharp.Core;
using AOSharp.Common.GameData;
using NotumHUD.Settings;
using NotumHUD.Web;
using System;
using System.Collections.Generic;

namespace NotumHUD.Services
{
    public class StatService
    {
        private readonly SettingsService _settingsService;
        private readonly WebServerService _webServer;
        private double _timer;
        private bool _isEnabled = true;

        public StatService(SettingsService settingsService, WebServerService webServer)
        {
            _settingsService = settingsService;
            _webServer = webServer;
        }

        public void Toggle(bool enabled) => _isEnabled = enabled;

        public void Tick()
        {
            if (!_isEnabled || DynelManager.LocalPlayer == null) return;
            
            _timer += Time.DeltaTime;
            
            var settings = _settingsService.GetSettings();
            if (_timer < settings.IntervalMs / 1000.0) return;
            
            _timer = 0;
            
            try
            {
                var snapshot = StatSnapshot.FromPlayer(DynelManager.LocalPlayer, settings);
                _webServer.BroadcastSnapshot(snapshot);
            }
            catch (Exception ex)
            {
                Chat.WriteLine($"[NotumHUD] Stat Update Error: {ex.Message}");
            }
        }
    }
}