// RubiKit Context Engine - Aggregates game state for LLM analysis
// Provides structured context to AI for intelligent callouts

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RubiKit.LLM
{
    /// <summary>
    /// Event types that can trigger LLM analysis
    /// </summary>
    public enum GameEventType
    {
        // Combat events
        CombatStart,
        CombatEnd,
        DamageTaken,
        DamageDealt,
        HealReceived,
        NanoCast,
        NanoResisted,
        TargetChanged,

        // State changes
        StatChanged,
        BuffApplied,
        BuffExpired,
        HealthLow,
        NanoLow,

        // Trading events
        ShopSale,
        ShopPurchase,
        GMSTrade,
        PriceAlert,

        // Equipment events
        ItemEquipped,
        ImplantChanged,
        WeaponSwapped,

        // General
        ZoneChanged,
        TeamChanged,
        Custom
    }

    /// <summary>
    /// Game event for context tracking
    /// </summary>
    public sealed class GameEvent
    {
        public GameEventType Type { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } // Module that generated event
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
        public int Priority { get; set; } = 0; // Higher = more important for context

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.Append($"[{Timestamp:HH:mm:ss}] {Type}");
            if (Data.Count > 0)
            {
                sb.Append(": ");
                sb.Append(string.Join(", ", Data.Select(d => $"{d.Key}={d.Value}")));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Combat context snapshot
    /// </summary>
    public sealed class CombatContext
    {
        public bool InCombat { get; set; }
        public DateTime CombatStartTime { get; set; }
        public string CurrentTarget { get; set; }
        public Dictionary<string, int> TargetACs { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> PlayerDamageModifiers { get; set; } = new Dictionary<string, int>();
        public int DamageDealtTotal { get; set; }
        public int DamageTakenTotal { get; set; }
        public int HealsReceivedTotal { get; set; }
        public int NanoCastsTotal { get; set; }
        public List<string> RecentAbilitiesUsed { get; set; } = new List<string>();

        public string GetOptimalDamageType()
        {
            if (TargetACs.Count == 0) return null;

            var damageTypes = new[] { "Melee", "Projectile", "Energy", "Fire", "Cold", "Poison", "Radiation", "Chemical" };
            string best = null;
            int bestScore = int.MinValue;

            foreach (var type in damageTypes)
            {
                var acKey = type + "AC";
                var dmgKey = type + "DamageModifier";

                if (TargetACs.TryGetValue(acKey, out int targetAC) &&
                    PlayerDamageModifiers.TryGetValue(dmgKey, out int playerDmg))
                {
                    var score = playerDmg - targetAC; // Higher is better
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = type;
                    }
                }
            }
            return best;
        }

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Combat: {(InCombat ? "ACTIVE" : "None")}");
            if (InCombat)
            {
                sb.AppendLine($"Duration: {(DateTime.UtcNow - CombatStartTime).TotalSeconds:F0}s");
                if (!string.IsNullOrEmpty(CurrentTarget))
                    sb.AppendLine($"Target: {CurrentTarget}");
                sb.AppendLine($"Damage Dealt: {DamageDealtTotal:N0}, Taken: {DamageTakenTotal:N0}");

                var optimal = GetOptimalDamageType();
                if (optimal != null)
                    sb.AppendLine($"Optimal Damage Type: {optimal}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Trading/market context
    /// </summary>
    public sealed class TradingContext
    {
        public Dictionary<string, MarketItem> TrackedItems { get; set; } = new Dictionary<string, MarketItem>();
        public List<TradeEvent> RecentTrades { get; set; } = new List<TradeEvent>();
        public Dictionary<string, PriceAlert> ActiveAlerts { get; set; } = new Dictionary<string, PriceAlert>();
        public long TotalProfitSession { get; set; }
        public int TradesCount { get; set; }

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Trading: {TradesCount} trades, {TotalProfitSession:N0} profit");

            if (ActiveAlerts.Count > 0)
            {
                sb.AppendLine($"Active Alerts: {ActiveAlerts.Count}");
                foreach (var alert in ActiveAlerts.Values.Take(3))
                {
                    sb.AppendLine($"  - {alert.ItemName}: {alert.Condition}");
                }
            }

            if (RecentTrades.Count > 0)
            {
                sb.AppendLine("Recent Trades:");
                foreach (var trade in RecentTrades.Skip(Math.Max(0, RecentTrades.Count - 3)))
                {
                    sb.AppendLine($"  - {trade.ItemName}: {trade.Price:N0} ({trade.Type})");
                }
            }

            return sb.ToString();
        }
    }

    public sealed class MarketItem
    {
        public string Name { get; set; }
        public long CurrentPrice { get; set; }
        public long AvgPrice { get; set; }
        public long MinPrice { get; set; }
        public long MaxPrice { get; set; }
        public int Volume24h { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public sealed class TradeEvent
    {
        public string ItemName { get; set; }
        public long Price { get; set; }
        public string Type { get; set; } // "buy", "sell"
        public DateTime Timestamp { get; set; }
        public long Profit { get; set; }
    }

    public sealed class PriceAlert
    {
        public string ItemName { get; set; }
        public string Condition { get; set; } // "above 1M", "below 500k"
        public long Threshold { get; set; }
        public bool Triggered { get; set; }
    }

    /// <summary>
    /// Build/equipment context
    /// </summary>
    public sealed class BuildContext
    {
        public int Level { get; set; }
        public string Profession { get; set; }
        public string Breed { get; set; }
        public Dictionary<string, int> CurrentStats { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> BuffedStats { get; set; } = new Dictionary<string, int>();
        public List<EquipmentSlot> Equipment { get; set; } = new List<EquipmentSlot>();
        public List<ImplantSlot> Implants { get; set; } = new List<ImplantSlot>();
        public Dictionary<string, int> IPSpent { get; set; } = new Dictionary<string, int>();
        public int TotalIP { get; set; }
        public int FreeIP { get; set; }

        public string ToContextString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Build: Level {Level} {Breed} {Profession}");
            sb.AppendLine($"IP: {FreeIP:N0} free / {TotalIP:N0} total");

            // Key stats
            var keyStats = new[] { "Treatment", "ComputerLiteracy", "NanoProgramming", "AddAllOff", "AddAllDef" };
            sb.AppendLine("Key Stats:");
            foreach (var stat in keyStats)
            {
                if (CurrentStats.TryGetValue(stat, out int val))
                {
                    var buffed = BuffedStats.TryGetValue(stat, out int b) ? b : val;
                    sb.AppendLine($"  {stat}: {val} ({buffed} buffed)");
                }
            }

            return sb.ToString();
        }
    }

    public sealed class EquipmentSlot
    {
        public string Slot { get; set; }
        public string ItemName { get; set; }
        public int QL { get; set; }
        public Dictionary<string, int> Mods { get; set; } = new Dictionary<string, int>();
    }

    public sealed class ImplantSlot
    {
        public string Slot { get; set; }
        public int QL { get; set; }
        public string Cluster1 { get; set; }
        public string Cluster2 { get; set; }
        public string Cluster3 { get; set; }
        public int RequiredTreatment { get; set; }
        public int RequiredAbility { get; set; }
    }

    /// <summary>
    /// Central context aggregation engine
    /// </summary>
    public sealed class ContextEngine : IDisposable
    {
        // Event history
        private readonly ConcurrentQueue<GameEvent> _eventHistory = new ConcurrentQueue<GameEvent>();
        private const int MaxEventHistory = 100;

        // Context stores
        public CombatContext Combat { get; } = new CombatContext();
        public TradingContext Trading { get; } = new TradingContext();
        public BuildContext Build { get; } = new BuildContext();

        // Current player stats (updated by stat service)
        private readonly ConcurrentDictionary<string, int> _currentStats = new ConcurrentDictionary<string, int>();

        // Custom context providers
        private readonly ConcurrentDictionary<string, Func<string>> _customProviders = new ConcurrentDictionary<string, Func<string>>();

        // Events for modules to subscribe
        public event Action<GameEvent> OnEvent;
        public event Action<string> OnCalloutGenerated;

        // Analysis triggers
        private readonly ConcurrentDictionary<GameEventType, List<AnalysisTrigger>> _triggers =
            new ConcurrentDictionary<GameEventType, List<AnalysisTrigger>>();

        /// <summary>
        /// Register a custom context provider for a module
        /// </summary>
        public void RegisterContextProvider(string name, Func<string> provider)
        {
            _customProviders[name] = provider;
        }

        /// <summary>
        /// Unregister a context provider
        /// </summary>
        public void UnregisterContextProvider(string name)
        {
            _customProviders.TryRemove(name, out _);
        }

        /// <summary>
        /// Register an analysis trigger
        /// </summary>
        public void RegisterTrigger(AnalysisTrigger trigger)
        {
            var list = _triggers.GetOrAdd(trigger.EventType, _ => new List<AnalysisTrigger>());
            lock (list)
            {
                list.Add(trigger);
            }
        }

        /// <summary>
        /// Push a game event
        /// </summary>
        public void PushEvent(GameEvent evt)
        {
            // Add to history
            _eventHistory.Enqueue(evt);
            while (_eventHistory.Count > MaxEventHistory)
            {
                _eventHistory.TryDequeue(out _);
            }

            // Update relevant context
            UpdateContext(evt);

            // Notify subscribers
            OnEvent?.Invoke(evt);

            // Check triggers
            if (_triggers.TryGetValue(evt.Type, out var triggers))
            {
                lock (triggers)
                {
                    foreach (var trigger in triggers)
                    {
                        if (trigger.ShouldTrigger(evt))
                        {
                            trigger.OnTrigger?.Invoke(evt, GetContextForDomain(trigger.Domain));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update stats from stat service
        /// </summary>
        public void UpdateStats(ConcurrentDictionary<string, int> stats)
        {
            foreach (var stat in stats)
            {
                var oldValue = _currentStats.GetOrAdd(stat.Key, 0);
                _currentStats[stat.Key] = stat.Value;

                // Check for significant changes
                if (Math.Abs(stat.Value - oldValue) > oldValue * 0.1) // 10% change
                {
                    PushEvent(new GameEvent
                    {
                        Type = GameEventType.StatChanged,
                        Source = "stats",
                        Data = new Dictionary<string, object>
                        {
                            { "stat", stat.Key },
                            { "oldValue", oldValue },
                            { "newValue", stat.Value }
                        }
                    });
                }
            }

            // Update build context
            foreach (var stat in stats)
            {
                Build.CurrentStats[stat.Key] = stat.Value;
            }

            // Update combat context damage modifiers
            var dmgTypes = new[] { "Melee", "Projectile", "Energy", "Fire", "Cold", "Poison", "Radiation", "Chemical" };
            foreach (var type in dmgTypes)
            {
                var key = type + "DamageModifier";
                if (_currentStats.TryGetValue(key, out int val))
                {
                    Combat.PlayerDamageModifiers[key] = val;
                }
            }

            // Check health/nano thresholds
            if (_currentStats.TryGetValue("Health", out int hp) && _currentStats.TryGetValue("MaxHealth", out int maxHp))
            {
                var pct = (double)hp / Math.Max(1, maxHp);
                if (pct < 0.25 && !Combat.InCombat) // Low HP out of combat
                {
                    PushEvent(new GameEvent { Type = GameEventType.HealthLow, Data = { { "pct", pct * 100 } } });
                }
            }

            if (_currentStats.TryGetValue("CurrentNano", out int nano) && _currentStats.TryGetValue("MaxNanoEnergy", out int maxNano))
            {
                var pct = (double)nano / Math.Max(1, maxNano);
                if (pct < 0.2)
                {
                    PushEvent(new GameEvent { Type = GameEventType.NanoLow, Data = { { "pct", pct * 100 } } });
                }
            }
        }

        /// <summary>
        /// Get current stat value
        /// </summary>
        public int GetStat(string name) => _currentStats.TryGetValue(name, out int val) ? val : 0;

        /// <summary>
        /// Get all current stats
        /// </summary>
        public Dictionary<string, int> GetAllStats() => _currentStats.ToDictionary(k => k.Key, v => v.Value);

        private void UpdateContext(GameEvent evt)
        {
            switch (evt.Type)
            {
                case GameEventType.CombatStart:
                    Combat.InCombat = true;
                    Combat.CombatStartTime = evt.Timestamp;
                    Combat.DamageDealtTotal = 0;
                    Combat.DamageTakenTotal = 0;
                    break;

                case GameEventType.CombatEnd:
                    Combat.InCombat = false;
                    break;

                case GameEventType.DamageDealt:
                    if (evt.Data.TryGetValue("amount", out var dmg))
                        Combat.DamageDealtTotal += Convert.ToInt32(dmg);
                    break;

                case GameEventType.DamageTaken:
                    if (evt.Data.TryGetValue("amount", out var taken))
                        Combat.DamageTakenTotal += Convert.ToInt32(taken);
                    break;

                case GameEventType.TargetChanged:
                    if (evt.Data.TryGetValue("target", out var target))
                        Combat.CurrentTarget = target?.ToString();
                    if (evt.Data.TryGetValue("acs", out var acs) && acs is Dictionary<string, int> acDict)
                        Combat.TargetACs = acDict;
                    break;

                case GameEventType.ShopSale:
                case GameEventType.ShopPurchase:
                    Trading.TradesCount++;
                    if (evt.Data.TryGetValue("profit", out var profit))
                        Trading.TotalProfitSession += Convert.ToInt64(profit);
                    Trading.RecentTrades.Add(new TradeEvent
                    {
                        ItemName = evt.Data.TryGetValue("item", out var item) ? item?.ToString() : "Unknown",
                        Price = evt.Data.TryGetValue("price", out var price) ? Convert.ToInt64(price) : 0,
                        Type = evt.Type == GameEventType.ShopSale ? "sell" : "buy",
                        Timestamp = evt.Timestamp
                    });
                    if (Trading.RecentTrades.Count > 50)
                        Trading.RecentTrades.RemoveAt(0);
                    break;
            }
        }

        /// <summary>
        /// Get recent events of a specific type
        /// </summary>
        public IEnumerable<GameEvent> GetRecentEvents(GameEventType type, int count = 10)
        {
            var filtered = _eventHistory.Where(e => e.Type == type).ToList();
            return filtered.Skip(Math.Max(0, filtered.Count - count));
        }

        /// <summary>
        /// Get all recent events
        /// </summary>
        public IEnumerable<GameEvent> GetRecentEvents(int count = 20)
        {
            var events = _eventHistory.ToList();
            return events.Skip(Math.Max(0, events.Count - count));
        }

        /// <summary>
        /// Build context string for a specific domain
        /// </summary>
        public string GetContextForDomain(string domain)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== CURRENT GAME STATE ===");
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();

            switch (domain.ToLower())
            {
                case "combat":
                    sb.Append(Combat.ToContextString());
                    sb.AppendLine();
                    sb.AppendLine("Recent Combat Events:");
                    foreach (var evt in GetRecentEvents(10).Where(e =>
                        e.Type == GameEventType.DamageDealt ||
                        e.Type == GameEventType.DamageTaken ||
                        e.Type == GameEventType.NanoCast))
                    {
                        sb.AppendLine("  " + evt.ToContextString());
                    }
                    break;

                case "trading":
                    sb.Append(Trading.ToContextString());
                    break;

                case "builds":
                    sb.Append(Build.ToContextString());
                    break;

                default:
                    // Full context
                    sb.AppendLine("-- Combat --");
                    sb.Append(Combat.ToContextString());
                    sb.AppendLine();
                    sb.AppendLine("-- Trading --");
                    sb.Append(Trading.ToContextString());
                    sb.AppendLine();
                    sb.AppendLine("-- Build --");
                    sb.Append(Build.ToContextString());
                    break;
            }

            // Add custom context from providers
            foreach (var provider in _customProviders)
            {
                try
                {
                    var context = provider.Value();
                    if (!string.IsNullOrEmpty(context))
                    {
                        sb.AppendLine();
                        sb.AppendLine($"-- {provider.Key} --");
                        sb.Append(context);
                    }
                }
                catch { }
            }

            // Add key stats
            sb.AppendLine();
            sb.AppendLine("-- Key Stats --");
            var keyStats = new[] { "Health", "MaxHealth", "CurrentNano", "MaxNanoEnergy",
                "AddAllOff", "AddAllDef", "CriticalIncrease", "Treatment", "ComputerLiteracy" };
            foreach (var stat in keyStats)
            {
                if (_currentStats.TryGetValue(stat, out int val))
                {
                    sb.AppendLine($"{stat}: {val}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get full context as JSON for API
        /// </summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");

            // Combat
            sb.Append("\"combat\":{");
            sb.Append($"\"inCombat\":{Combat.InCombat.ToString().ToLower()},");
            sb.Append($"\"target\":\"{Escape(Combat.CurrentTarget ?? "")}\",");
            sb.Append($"\"damageDealt\":{Combat.DamageDealtTotal},");
            sb.Append($"\"damageTaken\":{Combat.DamageTakenTotal}");
            sb.Append("},");

            // Trading
            sb.Append("\"trading\":{");
            sb.Append($"\"tradesCount\":{Trading.TradesCount},");
            sb.Append($"\"sessionProfit\":{Trading.TotalProfitSession},");
            sb.Append($"\"alertsCount\":{Trading.ActiveAlerts.Count}");
            sb.Append("},");

            // Build
            sb.Append("\"build\":{");
            sb.Append($"\"level\":{Build.Level},");
            sb.Append($"\"profession\":\"{Escape(Build.Profession ?? "")}\",");
            sb.Append($"\"freeIP\":{Build.FreeIP}");
            sb.Append("},");

            // Recent events
            sb.Append("\"recentEvents\":[");
            sb.Append(string.Join(",", GetRecentEvents(10).Select(e =>
                $"{{\"type\":\"{e.Type}\",\"time\":\"{e.Timestamp:HH:mm:ss}\",\"source\":\"{Escape(e.Source ?? "")}\"}}")));
            sb.Append("],");

            // Custom providers
            sb.Append("\"providers\":[");
            sb.Append(string.Join(",", _customProviders.Keys.Select(k => $"\"{Escape(k)}\"")));
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        public void Dispose()
        {
            while (_eventHistory.TryDequeue(out _)) { }
            _customProviders.Clear();
            _triggers.Clear();
        }
    }

    /// <summary>
    /// Analysis trigger configuration
    /// </summary>
    public sealed class AnalysisTrigger
    {
        public string Name { get; set; }
        public GameEventType EventType { get; set; }
        public string Domain { get; set; } = "core";
        public Func<GameEvent, bool> Condition { get; set; }
        public Action<GameEvent, string> OnTrigger { get; set; }
        public int CooldownMs { get; set; } = 5000;

        private DateTime _lastTrigger = DateTime.MinValue;

        public bool ShouldTrigger(GameEvent evt)
        {
            if ((DateTime.UtcNow - _lastTrigger).TotalMilliseconds < CooldownMs)
                return false;

            if (Condition != null && !Condition(evt))
                return false;

            _lastTrigger = DateTime.UtcNow;
            return true;
        }
    }
}
