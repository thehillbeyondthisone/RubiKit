using AOSharp.Common.GameData;
using AOSharp.Core;
using Newtonsoft.Json;
using NotumHUD.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NotumHUD.Web
{
    // A data model representing the state sent to the front-end.
    // Using JsonProperty attributes for clean serialization.
    public class StatSnapshot
    {
        [JsonProperty("core")]
        public Dictionary<string, int> Core { get; set; }

        [JsonProperty("hp")]
        public ResourceInfo Hp { get; set; }

        [JsonProperty("nano")]
        public ResourceInfo Nano { get; set; }

        [JsonProperty("dmg")]
        public Dictionary<string, int> Damage { get; set; }

        [JsonProperty("ac")]
        public Dictionary<string, int> ArmorClasses { get; set; }

        [JsonProperty("pins")]
        public List<PinInfo> Pins { get; set; }

        [JsonProperty("all")]
        public Dictionary<string, int> AllStats { get; set; }
        
        [JsonProperty("all_names")]
        public List<string> AllStatNames { get; set; }

        [JsonProperty("hiddenMisc")]
        public HashSet<string> HiddenMisc { get; set; }

        [JsonProperty("settings")]
        public SnapshotSettings Settings { get; set; }

        [JsonProperty("t")]
        public double Timestamp { get; set; }

        public class ResourceInfo
        {
            [JsonProperty("now")] public int Now { get; set; }
            [JsonProperty("max")] public int Max { get; set; }
            [JsonProperty("pct")] public int Pct => Max > 0 ? Math.Max(0, Math.Min(100, (int)Math.Round((double)Now * 100 / Max))) : 0;
        }

        public class PinInfo
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("v")] public int Value { get; set; }
            [JsonProperty("label")] public string Label => StatLabels.Get(Name);
        }
        
        public class SnapshotSettings
        {
            [JsonProperty("enabled")] public bool Enabled { get; set; }
            [JsonProperty("interval_ms")] public int IntervalMs { get; set; }
            [JsonProperty("theme")] public string Theme { get; set; }
            [JsonProperty("compact")] public bool Compact { get; set; }
            [JsonProperty("panelOrder")] public List<string> PanelOrder { get; set; }
        }

        public static StatSnapshot FromPlayer(SimpleChar player, SettingsModel settings)
        {
            var allStats = ReadAllStats(player);
            
            Func<string, int> get = (key) => allStats.TryGetValue(key, out int v) ? v : 0;
            Func<int, bool> meaningful = (v) => v != 0 && v != 12345678 && v != 1234567890;

            var snapshot = new StatSnapshot
            {
                Core = StatDefinitions.CoreStatNames.ToDictionary(name => name, name => get(name)),
                Hp = new ResourceInfo { Now = get("_HP_NOW"), Max = get("_HP_MAX") },
                Nano = new ResourceInfo { Now = get("_NP_NOW"), Max = get("_NP_MAX") },
                Damage = StatDefinitions.DmgStatNames.Where(name => meaningful(get(name))).ToDictionary(name => name, name => get(name)),
                ArmorClasses = StatDefinitions.AcStats.Where(t => meaningful(get(t.StatName))).ToDictionary(t => t.StatName, t => get(t.StatName)),
                Pins = settings.Pins.Where(name => meaningful(get(name))).Select(name => new PinInfo { Name = name, Value = get(name) }).ToList(),
                AllStats = allStats.Where(kvp => !kvp.Key.StartsWith("_") && meaningful(kvp.Value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                HiddenMisc = settings.HiddenMisc,
                Settings = new SnapshotSettings
                {
                    Enabled = true, // Assuming if we're sending a snapshot, it's enabled
                    IntervalMs = settings.IntervalMs,
                    Theme = settings.Theme,
                    Compact = settings.Compact,
                    PanelOrder = settings.PanelOrder
                },
                Timestamp = Time.NormalTime
            };
            snapshot.AllStatNames = snapshot.AllStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            
            return snapshot;
        }

        private static Dictionary<string, int> ReadAllStats(SimpleChar player)
        {
            var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Stat stat in Enum.GetValues(typeof(Stat)))
            {
                try { stats[stat.ToString()] = player.GetStat(stat); } catch { /* Ignore stats that fail */ }
            }
            
            // Helper function to read the first valid stat from a list of names
            bool TryReadAny(string[] names, out int value)
            {
                foreach (var name in names)
                {
                    if (stats.TryGetValue(name, out value)) return true;
                }
                value = 0;
                return false;
            }

            if (TryReadAny(StatDefinitions.HpNowNames, out int hpNow)) stats["_HP_NOW"] = hpNow;
            if (TryReadAny(StatDefinitions.HpMaxNames, out int hpMax)) stats["_HP_MAX"] = hpMax;
            if (TryReadAny(StatDefinitions.NanoNowNames, out int npNow)) stats["_NP_NOW"] = npNow;
            if (TryReadAny(StatDefinitions.NanoMaxNames, out int npMax)) stats["_NP_MAX"] = npMax;

            return stats;
        }
    }
}