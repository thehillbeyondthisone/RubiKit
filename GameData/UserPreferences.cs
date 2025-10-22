// UserPreferences.cs — Stores user settings, pinned stats, and custom categories
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace RubiKit.GameData
{
    /// <summary>
    /// Manages user preferences for NotumHUD:
    /// - Pinned stats
    /// - UI settings (theme, font, fontSize)
    /// - Custom stat-to-category mappings
    ///
    /// Persisted to notumhud_prefs.json in the plugin directory.
    /// </summary>
    internal class UserPreferences
    {
        public List<PinnedStat> Pins { get; set; } = new List<PinnedStat>();
        public UISettings Settings { get; set; } = new UISettings();
        public Dictionary<string, string> CustomCategories { get; set; } = new Dictionary<string, string>();

        private static string _prefsPath;
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public static void Initialize(string pluginDir)
        {
            _prefsPath = Path.Combine(pluginDir ?? "", "notumhud_prefs.json");
        }

        /// <summary>
        /// Loads preferences from disk. Returns defaults if file doesn't exist.
        /// </summary>
        public static UserPreferences Load()
        {
            if (string.IsNullOrEmpty(_prefsPath) || !File.Exists(_prefsPath))
                return new UserPreferences();

            try
            {
                var json = File.ReadAllText(_prefsPath);
                return _json.Deserialize<UserPreferences>(json) ?? new UserPreferences();
            }
            catch
            {
                return new UserPreferences();
            }
        }

        /// <summary>
        /// Saves preferences to disk.
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(_prefsPath))
                return;

            try
            {
                var json = _json.Serialize(this);
                File.WriteAllText(_prefsPath, json);
            }
            catch { }
        }

        /// <summary>
        /// Adds a pinned stat. Returns false if already pinned.
        /// </summary>
        public bool AddPin(string statName, string label = null)
        {
            if (Pins.Exists(p => p.name == statName))
                return false;

            Pins.Add(new PinnedStat { name = statName, label = label });
            Save();
            return true;
        }

        /// <summary>
        /// Removes a pinned stat. Returns false if not found.
        /// </summary>
        public bool RemovePin(string statName)
        {
            var removed = Pins.RemoveAll(p => p.name == statName) > 0;
            if (removed)
                Save();
            return removed;
        }

        /// <summary>
        /// Updates a UI setting and saves.
        /// </summary>
        public void SetSetting(string key, string value)
        {
            if (key == "theme") Settings.theme = value;
            else if (key == "font") Settings.font = value;
            else if (key == "fontSize" && int.TryParse(value, out var size)) Settings.fontSize = size;
            Save();
        }

        /// <summary>
        /// Sets a custom category for a stat.
        /// </summary>
        public void SetCategory(string statName, string category)
        {
            CustomCategories[statName] = category;
            Save();
        }
    }

    /// <summary>
    /// Represents a pinned stat in the UI.
    /// </summary>
    public class PinnedStat
    {
        public string name { get; set; }
        public string label { get; set; }
        public int v { get; set; } // Current value (set at render time)
    }

    /// <summary>
    /// UI settings for theme, font, and scale.
    /// </summary>
    public class UISettings
    {
        public string theme { get; set; } = "theme-notum";
        public string font { get; set; } = "font-default";
        public int fontSize { get; set; } = 100;
    }
}
