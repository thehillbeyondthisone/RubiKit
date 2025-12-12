// RubiKit Build Analyzer
// Provides implant/equipment analysis, IP optimization, and skill requirement tracking

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RubiKit.LLM.Providers
{
    /// <summary>
    /// Build analysis provider - implants, equipment, IP allocation, skill requirements
    /// </summary>
    public sealed class BuildAnalyzer : AnalysisProviderBase
    {
        public override string Id => "builds";
        public override string Name => "Build Analyzer";
        public override string Domain => "builds";
        public override int Priority => 60;

        // Configuration
        public bool EnableImplantAnalysis { get; set; } = true;
        public bool EnableIPTracking { get; set; } = true;
        public bool EnableSkillReqTracking { get; set; } = true;
        public bool EnableBuffRecommendations { get; set; } = true;

        // Static data for AO mechanics
        private static readonly Dictionary<int, int> ImplantTreatmentReqs = new Dictionary<int, int>
        {
            { 1, 11 }, { 10, 26 }, { 20, 46 }, { 30, 66 }, { 40, 86 }, { 50, 106 },
            { 60, 126 }, { 70, 147 }, { 80, 167 }, { 90, 188 }, { 100, 209 },
            { 110, 230 }, { 120, 251 }, { 130, 273 }, { 140, 295 }, { 150, 317 },
            { 160, 339 }, { 170, 362 }, { 180, 385 }, { 190, 408 }, { 200, 432 },
            { 210, 456 }, { 220, 481 }, { 230, 506 }, { 240, 531 }, { 250, 557 },
            { 260, 583 }, { 270, 610 }, { 280, 637 }, { 290, 665 }, { 300, 693 }
        };

        private static readonly Dictionary<string, string> AbilityForSlot = new Dictionary<string, string>
        {
            { "head", "Intelligence" }, { "eye", "Agility" }, { "ear", "Intelligence" },
            { "rarm", "Strength" }, { "chest", "Stamina" }, { "larm", "Strength" },
            { "rwrist", "Agility" }, { "waist", "Stamina" }, { "lwrist", "Agility" },
            { "rhand", "Agility" }, { "leg", "Agility" }, { "lhand", "Agility" },
            { "feet", "Agility" }
        };

        // Tracking
        private readonly Dictionary<string, ImplantTarget> _implantTargets = new Dictionary<string, ImplantTarget>();
        private readonly Dictionary<string, SkillTarget> _skillTargets = new Dictionary<string, SkillTarget>();
        private int _lastTreatment = 0;
        private int _lastComputerLit = 0;

        public override void Initialize(ContextEngine context, LLMService llm)
        {
            base.Initialize(context, llm);

            // Register stat change triggers
            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "treatment_changed",
                EventType = GameEventType.StatChanged,
                Domain = "builds",
                CooldownMs = 5000,
                Condition = evt => evt.Data.TryGetValue("stat", out var s) && s?.ToString() == "Treatment",
                OnTrigger = (evt, ctx) => CheckImplantUnlocks()
            });

            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "equipment_changed",
                EventType = GameEventType.ItemEquipped,
                Domain = "builds",
                CooldownMs = 2000,
                OnTrigger = (evt, ctx) => AnalyzeEquipmentChange(evt)
            });
        }

        public override void OnEvent(GameEvent evt)
        {
            switch (evt.Type)
            {
                case GameEventType.StatChanged:
                    CheckStatTargets(evt);
                    break;

                case GameEventType.ItemEquipped:
                case GameEventType.ImplantChanged:
                    AnalyzeEquipmentChange(evt);
                    break;
            }
        }

        private void CheckImplantUnlocks()
        {
            if (!EnableImplantAnalysis) return;

            var treatment = Context.GetStat("Treatment");
            if (treatment == _lastTreatment) return;
            _lastTreatment = treatment;

            // Check implant targets
            foreach (var target in _implantTargets.Values.Where(t => !t.Achieved))
            {
                if (treatment >= target.RequiredTreatment)
                {
                    target.Achieved = true;
                    AddCallout($"QL {target.QL} implant now available for {target.Slot}!", CalloutType.Success, 5000);
                }
            }

            // Find highest installable QL
            int maxQL = 1;
            foreach (var kvp in ImplantTreatmentReqs.OrderByDescending(k => k.Key))
            {
                if (treatment >= kvp.Value)
                {
                    maxQL = kvp.Key;
                    break;
                }
            }

            // Find next tier
            var nextTier = ImplantTreatmentReqs.FirstOrDefault(k => k.Value > treatment);
            if (nextTier.Key > 0 && maxQL != nextTier.Key)
            {
                var needed = nextTier.Value - treatment;
                if (needed <= 50) // Only show if close
                {
                    AddCallout($"QL {nextTier.Key} implants: need {needed} more Treatment ({treatment} → {nextTier.Value})",
                        CalloutType.Tip, 5000);
                }
            }
        }

        private void CheckStatTargets(GameEvent evt)
        {
            if (!EnableSkillReqTracking) return;

            if (!evt.Data.TryGetValue("stat", out var statObj)) return;
            var stat = statObj?.ToString();

            foreach (var target in _skillTargets.Values.Where(t => t.Stat == stat && !t.Achieved))
            {
                var current = Context.GetStat(stat);
                if (current >= target.Required)
                {
                    target.Achieved = true;
                    AddCallout($"Reached {target.Required} {stat}! ({target.Purpose})", CalloutType.Success, 4000);
                }
                else if (target.Required - current <= 20)
                {
                    AddCallout($"Almost there: {current}/{target.Required} {stat} for {target.Purpose}",
                        CalloutType.Tip, 4000);
                }
            }
        }

        private void AnalyzeEquipmentChange(GameEvent evt)
        {
            var slot = evt.Data.TryGetValue("slot", out var s) ? s?.ToString() : null;
            var item = evt.Data.TryGetValue("item", out var i) ? i?.ToString() : null;
            var ql = evt.Data.TryGetValue("ql", out var q) ? Convert.ToInt32(q) : 0;

            if (!string.IsNullOrEmpty(item))
            {
                AddCallout($"Equipped: {item} (QL {ql})", CalloutType.Build, 3000);
            }
        }

        public override async Task<IEnumerable<AnalysisResult>> AnalyzeAsync()
        {
            var results = new List<AnalysisResult>();

            // Treatment analysis
            var treatment = Context.GetStat("Treatment");
            var maxImplantQL = GetMaxImplantQL(treatment);

            results.Add(new AnalysisResult
            {
                ProviderId = Id,
                Title = "Implant Capacity",
                Content = $"Treatment {treatment} = max QL {maxImplantQL} implants",
                Severity = AnalysisSeverity.Info,
                Metadata = {
                    { "treatment", treatment },
                    { "maxQL", maxImplantQL }
                }
            });

            // Next tier analysis
            var nextTier = ImplantTreatmentReqs.FirstOrDefault(k => k.Value > treatment);
            if (nextTier.Key > 0)
            {
                var needed = nextTier.Value - treatment;
                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = $"Next Implant Tier (QL {nextTier.Key})",
                    Content = $"Need {needed} more Treatment ({treatment} → {nextTier.Value})",
                    Severity = needed <= 30 ? AnalysisSeverity.Opportunity : AnalysisSeverity.Info,
                    Metadata = {
                        { "nextQL", nextTier.Key },
                        { "required", nextTier.Value },
                        { "needed", needed }
                    }
                });
            }

            // Computer Literacy analysis
            var compLit = Context.GetStat("ComputerLiteracy");
            results.Add(new AnalysisResult
            {
                ProviderId = Id,
                Title = "NCU Capacity",
                Content = $"Computer Literacy: {compLit}",
                Severity = AnalysisSeverity.Info,
                Metadata = { { "compLit", compLit } }
            });

            // Pending skill targets
            foreach (var target in _skillTargets.Values.Where(t => !t.Achieved))
            {
                var current = Context.GetStat(target.Stat);
                var remaining = target.Required - current;

                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = $"Skill Target: {target.Purpose}",
                    Content = $"{target.Stat}: {current}/{target.Required} (need +{remaining})",
                    Severity = remaining <= 20 ? AnalysisSeverity.Opportunity : AnalysisSeverity.Info,
                    Metadata = {
                        { "stat", target.Stat },
                        { "current", current },
                        { "required", target.Required },
                        { "remaining", remaining }
                    }
                });
            }

            // Pending implant targets
            foreach (var target in _implantTargets.Values.Where(t => !t.Achieved))
            {
                var needed = target.RequiredTreatment - treatment;

                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = $"Implant Target: {target.Slot} QL {target.QL}",
                    Content = $"Treatment: {treatment}/{target.RequiredTreatment} (need +{needed})",
                    Severity = needed <= 30 ? AnalysisSeverity.Opportunity : AnalysisSeverity.Info,
                    Metadata = {
                        { "slot", target.Slot },
                        { "ql", target.QL },
                        { "needed", needed }
                    }
                });
            }

            // Buff recommendations if enabled
            if (EnableBuffRecommendations && LLM != null && LLM.Config.Enabled)
            {
                var prompt = GetBuffRecommendationPrompt();
                if (!string.IsNullOrEmpty(prompt))
                {
                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = "Buff Recommendation",
                        Content = "LLM analysis pending...",
                        Severity = AnalysisSeverity.Suggestion,
                        RequiresLLM = true,
                        LLMPrompt = prompt
                    });
                }
            }

            return results;
        }

        private int GetMaxImplantQL(int treatment)
        {
            int maxQL = 1;
            foreach (var kvp in ImplantTreatmentReqs.OrderByDescending(k => k.Key))
            {
                if (treatment >= kvp.Value)
                {
                    maxQL = kvp.Key;
                    break;
                }
            }
            return maxQL;
        }

        private string GetBuffRecommendationPrompt()
        {
            var treatment = Context.GetStat("Treatment");
            var compLit = Context.GetStat("ComputerLiteracy");

            // Only generate if there's something to improve
            if (_implantTargets.Count == 0 && _skillTargets.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("Current stats:");
            sb.AppendLine($"  Treatment: {treatment}");
            sb.AppendLine($"  Computer Literacy: {compLit}");
            sb.AppendLine();

            if (_implantTargets.Count > 0)
            {
                sb.AppendLine("Implant targets:");
                foreach (var target in _implantTargets.Values.Where(t => !t.Achieved))
                {
                    sb.AppendLine($"  {target.Slot} QL {target.QL}: need {target.RequiredTreatment} Treatment");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Recommend treatment buffs to reach next implant tier. Be specific with nano program names. Under 50 words.");

            return sb.ToString();
        }

        public override string GetContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Build Context ===");

            var treatment = Context.GetStat("Treatment");
            var compLit = Context.GetStat("ComputerLiteracy");
            var maxQL = GetMaxImplantQL(treatment);

            sb.AppendLine($"Treatment: {treatment} (max QL {maxQL} implants)");
            sb.AppendLine($"Computer Literacy: {compLit}");
            sb.AppendLine();

            // Key stats
            var keyStats = new[] { "Strength", "Agility", "Stamina", "Intelligence", "Sense", "Psychic",
                "NanoProgramming", "FirstAid", "AddAllOff", "AddAllDef" };

            sb.AppendLine("Key Stats:");
            foreach (var stat in keyStats)
            {
                var val = Context.GetStat(stat);
                if (val > 0)
                    sb.AppendLine($"  {stat}: {val}");
            }

            if (_implantTargets.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Implant Targets:");
                foreach (var target in _implantTargets.Values)
                {
                    var status = target.Achieved ? "DONE" : $"need {target.RequiredTreatment - treatment}";
                    sb.AppendLine($"  {target.Slot} QL {target.QL}: {status}");
                }
            }

            if (_skillTargets.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Skill Targets:");
                foreach (var target in _skillTargets.Values)
                {
                    var current = Context.GetStat(target.Stat);
                    var status = target.Achieved ? "DONE" : $"{current}/{target.Required}";
                    sb.AppendLine($"  {target.Stat} for {target.Purpose}: {status}");
                }
            }

            return sb.ToString();
        }

        // Public API for setting targets
        public void SetImplantTarget(string slot, int ql)
        {
            var treatmentReq = ImplantTreatmentReqs.TryGetValue(ql, out var t) ? t : ql * 2;

            _implantTargets[slot] = new ImplantTarget
            {
                Slot = slot,
                QL = ql,
                RequiredTreatment = treatmentReq,
                Achieved = Context.GetStat("Treatment") >= treatmentReq
            };
        }

        public void RemoveImplantTarget(string slot)
        {
            _implantTargets.Remove(slot);
        }

        public void SetSkillTarget(string stat, int required, string purpose)
        {
            _skillTargets[stat + "_" + purpose] = new SkillTarget
            {
                Stat = stat,
                Required = required,
                Purpose = purpose,
                Achieved = Context.GetStat(stat) >= required
            };
        }

        public void RemoveSkillTarget(string key)
        {
            _skillTargets.Remove(key);
        }

        public void ClearTargets()
        {
            _implantTargets.Clear();
            _skillTargets.Clear();
        }

        // Calculate treatment needed for a given implant QL
        public static int GetTreatmentForQL(int ql)
        {
            if (ImplantTreatmentReqs.TryGetValue(ql, out var req))
                return req;

            // Interpolate for non-standard QLs
            return (int)(ql * 2.2);
        }

        // Calculate ability requirement for implant slot/QL
        public static int GetAbilityForSlot(string slot, int ql)
        {
            // Simplified formula - actual AO uses complex calculations
            return (int)(ql * 0.8);
        }

        public override string GetConfigJson()
        {
            return $"{{\"id\":\"{Id}\",\"name\":\"{Name}\",\"domain\":\"{Domain}\",\"enabled\":{Enabled.ToString().ToLower()}," +
                   $"\"enableImplantAnalysis\":{EnableImplantAnalysis.ToString().ToLower()}," +
                   $"\"enableIPTracking\":{EnableIPTracking.ToString().ToLower()}," +
                   $"\"enableSkillReqTracking\":{EnableSkillReqTracking.ToString().ToLower()}," +
                   $"\"enableBuffRecommendations\":{EnableBuffRecommendations.ToString().ToLower()}," +
                   $"\"implantTargets\":{_implantTargets.Count},\"skillTargets\":{_skillTargets.Count}}}";
        }

        public override void UpdateConfig(Dictionary<string, string> config)
        {
            base.UpdateConfig(config);

            if (config.TryGetValue("enableImplantAnalysis", out var imp))
                EnableImplantAnalysis = imp == "true" || imp == "1";
            if (config.TryGetValue("enableIPTracking", out var ip))
                EnableIPTracking = ip == "true" || ip == "1";
            if (config.TryGetValue("enableSkillReqTracking", out var skill))
                EnableSkillReqTracking = skill == "true" || skill == "1";
            if (config.TryGetValue("enableBuffRecommendations", out var buff))
                EnableBuffRecommendations = buff == "true" || buff == "1";
        }
    }

    // Supporting classes
    public sealed class ImplantTarget
    {
        public string Slot { get; set; }
        public int QL { get; set; }
        public int RequiredTreatment { get; set; }
        public int RequiredAbility { get; set; }
        public bool Achieved { get; set; }
    }

    public sealed class SkillTarget
    {
        public string Stat { get; set; }
        public int Required { get; set; }
        public string Purpose { get; set; }
        public bool Achieved { get; set; }
    }
}
