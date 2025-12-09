using AOSharp.Core.UI;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;

namespace NotumHUD.Settings
{
    public class SettingsService
    {
        private readonly string _configPath;
        private SettingsModel _settings;
        private readonly object _lock = new object();

        public SettingsService(string pluginDir)
        {
            _configPath = Path.Combine(pluginDir, "NotumHUD.config.json");
            Load();
        }

        /// <summary>
        /// Gets a thread-safe copy of the current settings.
        /// </summary>
        public SettingsModel GetSettings()
        {
            lock (_lock)
            {
                // Return a copy to prevent external modification of the instance
                return JsonConvert.DeserializeObject<SettingsModel>(JsonConvert.SerializeObject(_settings));
            }
        }
        
        /// <summary>
        /// Applies an update action to the settings and saves the result.
        /// </summary>
        public void Update(Action<SettingsModel> updateAction)
        {
            lock (_lock)
            {
                updateAction(_settings);
                Save();
            }
        }

        private void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_configPath))
                    {
                        string json = File.ReadAllText(_configPath, Encoding.UTF8);
                        _settings = JsonConvert.DeserializeObject<SettingsModel>(json) ?? new SettingsModel();
                    }
                    else
                    {
                        _settings = new SettingsModel();
                        Save(); // Create initial config file
                    }
                }
                catch (Exception ex)
                {
                    Chat.WriteLine($"[NotumHUD] Error loading config: {ex.Message}. Using defaults.");
                    _settings = new SettingsModel();
                }
            }
        }

        private void Save()
        {
            // This is called from within a lock
            try
            {
                string json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_configPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Chat.WriteLine($"[NotumHUD] Error saving config: {ex.Message}");
            }
        }
    }
}