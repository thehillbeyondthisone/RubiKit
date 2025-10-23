// GameStateProvider.cs — Reads ALL game stats from AOSharp
using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RubiKit.GameData
{
    /// <summary>
    /// Provides access to all player stats from the game engine.
    /// Enumerates EVERY stat in the Stat enum and reads current values.
    /// </summary>
    internal class GameStateProvider
    {
        /// <summary>
        /// Retrieves all stats as a dictionary { "StatName": value }.
        /// Includes special vitals (Health, Nano) that aren't in the Stat enum.
        /// </summary>
        public Dictionary<string, int> GetAllStats()
        {
            var stats = new Dictionary<string, int>();

            if (!DynelManager.LocalPlayer.IsValid)
                return stats; // Empty if not in-game

            var player = DynelManager.LocalPlayer;

            // 1. Enumerate ALL stats from the Stat enum
            foreach (Stat statId in Enum.GetValues(typeof(Stat)))
            {
                try
                {
                    var value = player.GetStat(statId);
                    stats[statId.ToString()] = value;
                }
                catch
                {
                    // Some stats may not be readable; skip them
                }
            }

            // 2. Add special vitals not in Stat enum
            try { stats["Health"] = player.Health; } catch { }
            try { stats["MaxHealth"] = player.MaxHealth; } catch { }
            try { stats["CurrentNano"] = player.Nano; } catch { }
            try { stats["MaxNanoEnergy"] = player.MaxNano; } catch { }

            // 3. Add any other special fields we want to expose
            try { stats["Level"] = player.Level; } catch { }
            try { stats["Profession"] = (int)player.Profession; } catch { }
            try { stats["Breed"] = (int)player.Breed; } catch { }
            try { stats["Side"] = (int)player.Side; } catch { }

            return stats;
        }

        /// <summary>
        /// Returns the list of ALL available stat names.
        /// Used by the frontend to build the stat browser.
        /// </summary>
        public List<string> GetAllStatNames()
        {
            var names = new List<string>();

            // All Stat enum members
            foreach (Stat statId in Enum.GetValues(typeof(Stat)))
            {
                names.Add(statId.ToString());
            }

            // Add special vitals
            names.Add("Health");
            names.Add("MaxHealth");
            names.Add("CurrentNano");
            names.Add("MaxNanoEnergy");
            names.Add("Level");
            names.Add("Profession");
            names.Add("Breed");
            names.Add("Side");

            return names.Distinct().OrderBy(n => n).ToList();
        }

        /// <summary>
        /// Gets the local IP address for LAN display.
        /// Returns null if unavailable.
        /// </summary>
        public string GetLocalIP()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
