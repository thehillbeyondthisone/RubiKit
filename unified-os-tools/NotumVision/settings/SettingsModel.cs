using Newtonsoft.Json;
using System.Collections.Generic;

namespace NotumHUD.Settings
{
    public class SettingsModel
    {
        [JsonProperty("port")]
        public int Port { get; set; } = 8777;

        [JsonProperty("interval_ms")]
        public int IntervalMs { get; set; } = 500;

        [JsonProperty("theme")]
        public string Theme { get; set; } = "theme-inferno";
        
        [JsonProperty("compact")]
        public bool Compact { get; set; } = false;

        [JsonProperty("pins")]
        public HashSet<string> Pins { get; set; } = new HashSet<string>();

        [JsonProperty("hiddenMisc")]
        public HashSet<string> HiddenMisc { get; set; } = new HashSet<string>();
        
        [JsonProperty("panelOrder")]
        public List<string> PanelOrder { get; set; } = new List<string> { "hpnano", "dmg", "ac", "aao", "aad", "crit", "xp", "pins" };
    }
}