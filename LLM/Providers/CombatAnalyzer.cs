// RubiKit Combat Analyzer
// Provides real-time combat analysis and tactical callouts

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RubiKit.LLM.Providers
{
    /// <summary>
    /// Combat analysis provider - analyzes damage types, ACs, and provides tactical callouts
    /// </summary>
    public sealed class CombatAnalyzer : AnalysisProviderBase
    {
        public override string Id => "combat";
        public override string Name => "Combat Analyzer";
        public override string Domain => "combat";
        public override int Priority => 100; // High priority for real-time callouts

        // Configuration
        public bool EnableACAnalysis { get; set; } = true;
        public bool EnableInitCallouts { get; set; } = true;
        public bool EnableDPSTracking { get; set; } = true;
        public int LowHealthThreshold { get; set; } = 25; // Percent
        public int LowNanoThreshold { get; set; } = 20;

        // Tracking
        private DateTime _lastACCallout = DateTime.MinValue;
        private DateTime _lastDPSCallout = DateTime.MinValue;
        private string _lastTarget = null;
        private int _damageWindow = 0;
        private DateTime _damageWindowStart = DateTime.UtcNow;
        private readonly List<(DateTime time, int damage)> _damageHistory = new List<(DateTime, int)>();

        public override void Initialize(ContextEngine context, LLMService llm)
        {
            base.Initialize(context, llm);

            // Register combat event triggers
            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "combat_target_changed",
                EventType = GameEventType.TargetChanged,
                Domain = "combat",
                CooldownMs = 2000,
                OnTrigger = (evt, ctx) => AnalyzeTargetChange(evt)
            });

            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "health_low",
                EventType = GameEventType.HealthLow,
                Domain = "combat",
                CooldownMs = 10000,
                OnTrigger = (evt, ctx) => AddCallout("Health critically low!", CalloutType.Warning, 3000)
            });

            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "nano_low",
                EventType = GameEventType.NanoLow,
                Domain = "combat",
                CooldownMs = 15000,
                OnTrigger = (evt, ctx) => AddCallout("Nano running low", CalloutType.Warning, 3000)
            });
        }

        public override void OnEvent(GameEvent evt)
        {
            switch (evt.Type)
            {
                case GameEventType.CombatStart:
                    _damageWindow = 0;
                    _damageWindowStart = DateTime.UtcNow;
                    _damageHistory.Clear();
                    AddCallout("Combat started", CalloutType.Combat, 2000);
                    break;

                case GameEventType.CombatEnd:
                    GenerateCombatSummary();
                    break;

                case GameEventType.DamageDealt:
                    if (evt.Data.TryGetValue("amount", out var dmg))
                    {
                        var amount = Convert.ToInt32(dmg);
                        _damageWindow += amount;
                        _damageHistory.Add((DateTime.UtcNow, amount));

                        // Prune old entries (keep last 10 seconds)
                        var cutoff = DateTime.UtcNow.AddSeconds(-10);
                        _damageHistory.RemoveAll(d => d.time < cutoff);
                    }
                    break;

                case GameEventType.TargetChanged:
                    AnalyzeTargetChange(evt);
                    break;

                case GameEventType.NanoResisted:
                    if (evt.Data.TryGetValue("nano", out var nano))
                    {
                        AddCallout($"Nano resisted: {nano}", CalloutType.Warning, 3000);
                    }
                    break;
            }
        }

        private void AnalyzeTargetChange(GameEvent evt)
        {
            if (!EnableACAnalysis) return;
            if ((DateTime.UtcNow - _lastACCallout).TotalSeconds < 3) return;

            var targetName = evt.Data.TryGetValue("target", out var t) ? t?.ToString() : null;
            if (string.IsNullOrEmpty(targetName) || targetName == _lastTarget) return;

            _lastTarget = targetName;

            // Get target ACs if available
            if (evt.Data.TryGetValue("acs", out var acsObj) && acsObj is Dictionary<string, int> acs)
            {
                Context.Combat.TargetACs = acs;

                // Find weakest AC
                var optimal = FindOptimalDamageType(acs);
                if (optimal != null)
                {
                    _lastACCallout = DateTime.UtcNow;
                    AddCallout($"{optimal.Value.type} is optimal vs {targetName} (AC: {optimal.Value.ac})", CalloutType.Combat, 5000);
                }
            }
            else
            {
                // Request LLM analysis if no direct AC data
                RequestLLMTargetAnalysis(targetName);
            }
        }

        private (string type, int ac, int differential)? FindOptimalDamageType(Dictionary<string, int> targetACs)
        {
            var dmgTypes = new[] { "Melee", "Projectile", "Energy", "Fire", "Cold", "Poison", "Radiation", "Chemical" };
            string bestType = null;
            int lowestAC = int.MaxValue;
            int bestDiff = int.MinValue;

            foreach (var type in dmgTypes)
            {
                var acKey = type + "AC";
                var dmgKey = type + "DamageModifier";

                if (targetACs.TryGetValue(acKey, out int ac))
                {
                    var playerDmg = Context.GetStat(dmgKey);
                    var differential = playerDmg - ac;

                    if (ac < lowestAC || (ac == lowestAC && differential > bestDiff))
                    {
                        lowestAC = ac;
                        bestType = type;
                        bestDiff = differential;
                    }
                }
            }

            if (bestType != null)
            {
                return (bestType, lowestAC, bestDiff);
            }
            return null;
        }

        private void RequestLLMTargetAnalysis(string targetName)
        {
            if (LLM == null || !LLM.Config.Enabled) return;

            var context = GetContext();
            var prompt = $"New target: {targetName}. Analyze my damage modifiers and suggest optimal damage type. Be brief (under 30 words).";

            LLM.AnalyzeAsync(context + "\n\n" + prompt, "combat", response =>
            {
                AddCallout(response, CalloutType.Combat, 6000);
            });
        }

        private void GenerateCombatSummary()
        {
            var duration = (DateTime.UtcNow - _damageWindowStart).TotalSeconds;
            if (duration < 5) return; // Skip very short fights

            var dps = _damageWindow / duration;
            var summary = $"Combat ended: {_damageWindow:N0} damage, {dps:N0} DPS over {duration:N0}s";

            AddCallout(summary, CalloutType.Combat, 5000);

            // Request LLM combat summary for longer fights
            if (duration > 30 && LLM != null && LLM.Config.Enabled)
            {
                var context = GetContext();
                var prompt = $"Combat summary: {_damageWindow:N0} total damage over {duration:N0} seconds ({dps:N0} DPS). " +
                            $"Damage taken: {Context.Combat.DamageTakenTotal:N0}. Provide brief feedback (under 50 words).";

                LLM.AnalyzeAsync(context + "\n\n" + prompt, "combat", response =>
                {
                    AddCallout(response, CalloutType.Tip, 8000);
                });
            }
        }

        public override async Task<IEnumerable<AnalysisResult>> AnalyzeAsync()
        {
            var results = new List<AnalysisResult>();

            // Analyze current combat state
            if (Context.Combat.InCombat)
            {
                // DPS Analysis
                if (_damageHistory.Count > 0)
                {
                    var recentDamage = _damageHistory.Sum(d => d.damage);
                    var dps = recentDamage / 10.0; // 10 second window

                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = "Current DPS",
                        Content = $"{dps:N0} damage per second",
                        Severity = AnalysisSeverity.Info,
                        Metadata = { { "dps", dps }, { "window", 10 } }
                    });
                }

                // AC Analysis
                if (Context.Combat.TargetACs.Count > 0)
                {
                    var optimal = FindOptimalDamageType(Context.Combat.TargetACs);
                    if (optimal.HasValue)
                    {
                        results.Add(new AnalysisResult
                        {
                            ProviderId = Id,
                            Title = "Optimal Damage Type",
                            Content = $"{optimal.Value.type} - Target AC: {optimal.Value.ac}, Your advantage: {optimal.Value.differential}",
                            Severity = optimal.Value.differential > 1000 ? AnalysisSeverity.Opportunity : AnalysisSeverity.Info,
                            Metadata = {
                                { "type", optimal.Value.type },
                                { "targetAC", optimal.Value.ac },
                                { "differential", optimal.Value.differential }
                            }
                        });
                    }
                }
            }

            // Health/Nano analysis
            var hp = Context.GetStat("Health");
            var maxHp = Context.GetStat("MaxHealth");
            if (maxHp > 0)
            {
                var hpPct = (double)hp / maxHp * 100;
                if (hpPct < LowHealthThreshold)
                {
                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = "Low Health Warning",
                        Content = $"Health at {hpPct:N0}% ({hp:N0}/{maxHp:N0})",
                        Severity = hpPct < 15 ? AnalysisSeverity.Critical : AnalysisSeverity.Warning
                    });
                }
            }

            var nano = Context.GetStat("CurrentNano");
            var maxNano = Context.GetStat("MaxNanoEnergy");
            if (maxNano > 0)
            {
                var nanoPct = (double)nano / maxNano * 100;
                if (nanoPct < LowNanoThreshold)
                {
                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = "Low Nano Warning",
                        Content = $"Nano at {nanoPct:N0}% ({nano:N0}/{maxNano:N0})",
                        Severity = AnalysisSeverity.Warning
                    });
                }
            }

            return results;
        }

        public override string GetContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Combat Context ===");
            sb.AppendLine($"In Combat: {Context.Combat.InCombat}");

            if (Context.Combat.InCombat)
            {
                var duration = (DateTime.UtcNow - Context.Combat.CombatStartTime).TotalSeconds;
                sb.AppendLine($"Duration: {duration:N0}s");
                sb.AppendLine($"Target: {Context.Combat.CurrentTarget ?? "Unknown"}");
                sb.AppendLine($"Damage Dealt: {Context.Combat.DamageDealtTotal:N0}");
                sb.AppendLine($"Damage Taken: {Context.Combat.DamageTakenTotal:N0}");

                if (_damageHistory.Count > 0)
                {
                    var dps = _damageHistory.Sum(d => d.damage) / 10.0;
                    sb.AppendLine($"Current DPS: {dps:N0}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Player Damage Modifiers:");
            foreach (var dmg in Context.Combat.PlayerDamageModifiers)
            {
                sb.AppendLine($"  {dmg.Key}: {dmg.Value}");
            }

            if (Context.Combat.TargetACs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Target ACs:");
                foreach (var ac in Context.Combat.TargetACs)
                {
                    sb.AppendLine($"  {ac.Key}: {ac.Value}");
                }
            }

            return sb.ToString();
        }

        public override string GetConfigJson()
        {
            return $"{{\"id\":\"{Id}\",\"name\":\"{Name}\",\"domain\":\"{Domain}\",\"enabled\":{Enabled.ToString().ToLower()}," +
                   $"\"enableACAnalysis\":{EnableACAnalysis.ToString().ToLower()}," +
                   $"\"enableInitCallouts\":{EnableInitCallouts.ToString().ToLower()}," +
                   $"\"enableDPSTracking\":{EnableDPSTracking.ToString().ToLower()}," +
                   $"\"lowHealthThreshold\":{LowHealthThreshold}," +
                   $"\"lowNanoThreshold\":{LowNanoThreshold}}}";
        }

        public override void UpdateConfig(Dictionary<string, string> config)
        {
            base.UpdateConfig(config);

            if (config.TryGetValue("enableACAnalysis", out var ac))
                EnableACAnalysis = ac == "true" || ac == "1";
            if (config.TryGetValue("enableInitCallouts", out var init))
                EnableInitCallouts = init == "true" || init == "1";
            if (config.TryGetValue("enableDPSTracking", out var dps))
                EnableDPSTracking = dps == "true" || dps == "1";
            if (config.TryGetValue("lowHealthThreshold", out var hp) && int.TryParse(hp, out var hpVal))
                LowHealthThreshold = hpVal;
            if (config.TryGetValue("lowNanoThreshold", out var nano) && int.TryParse(nano, out var nanoVal))
                LowNanoThreshold = nanoVal;
        }
    }
}
