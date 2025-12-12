// RubiKit Hydra Provider
// Provides multi-character/multi-account coordination and analysis

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RubiKit.LLM.Providers
{
    /// <summary>
    /// Hydra provider - multi-character coordination, team analysis, synergy tracking
    /// </summary>
    public sealed class HydraProvider : AnalysisProviderBase
    {
        public override string Id => "hydra";
        public override string Name => "Hydra Multi-Char";
        public override string Domain => "team";
        public override int Priority => 70;

        // Configuration
        public bool EnableTeamTracking { get; set; } = true;
        public bool EnableBuffSync { get; set; } = true;
        public bool EnableSynergyAnalysis { get; set; } = true;
        public int MaxTrackedCharacters { get; set; } = 12;

        // Character tracking
        private readonly ConcurrentDictionary<string, CharacterState> _characters =
            new ConcurrentDictionary<string, CharacterState>();

        // Team composition
        private readonly ConcurrentDictionary<string, string> _teamRoles =
            new ConcurrentDictionary<string, string>(); // charName -> role

        // Buff tracking
        private readonly ConcurrentDictionary<string, List<BuffStatus>> _buffStatus =
            new ConcurrentDictionary<string, List<BuffStatus>>();

        // Events
        public event Action<string, CharacterState> OnCharacterUpdated;
        public event Action<string, string> OnTeamAlert; // charName, message

        public override void Initialize(ContextEngine context, LLMService llm)
        {
            base.Initialize(context, llm);

            // Register team event triggers
            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "team_member_low_hp",
                EventType = GameEventType.HealthLow,
                Domain = "team",
                CooldownMs = 3000,
                OnTrigger = (evt, ctx) => CheckTeamHealth()
            });
        }

        public override void OnEvent(GameEvent evt)
        {
            // Events can be forwarded from other RubiKit instances
            if (evt.Data.TryGetValue("charName", out var charObj))
            {
                var charName = charObj?.ToString();
                if (!string.IsNullOrEmpty(charName))
                {
                    UpdateCharacterFromEvent(charName, evt);
                }
            }
        }

        private void UpdateCharacterFromEvent(string charName, GameEvent evt)
        {
            var state = _characters.GetOrAdd(charName, _ => new CharacterState { Name = charName });
            state.LastUpdate = DateTime.UtcNow;

            switch (evt.Type)
            {
                case GameEventType.StatChanged:
                    if (evt.Data.TryGetValue("stat", out var stat) && evt.Data.TryGetValue("newValue", out var val))
                    {
                        state.Stats[stat.ToString()] = Convert.ToInt32(val);
                    }
                    break;

                case GameEventType.HealthLow:
                    if (evt.Data.TryGetValue("pct", out var hpPct))
                    {
                        state.HealthPercent = Convert.ToInt32(hpPct);
                    }
                    break;

                case GameEventType.BuffApplied:
                    if (evt.Data.TryGetValue("buff", out var buff))
                    {
                        TrackBuff(charName, buff.ToString(), true);
                    }
                    break;

                case GameEventType.BuffExpired:
                    if (evt.Data.TryGetValue("buff", out var expiredBuff))
                    {
                        TrackBuff(charName, expiredBuff.ToString(), false);
                    }
                    break;
            }

            OnCharacterUpdated?.Invoke(charName, state);
        }

        private void TrackBuff(string charName, string buffName, bool active)
        {
            var buffs = _buffStatus.GetOrAdd(charName, _ => new List<BuffStatus>());

            lock (buffs)
            {
                var existing = buffs.FirstOrDefault(b => b.Name == buffName);
                if (existing != null)
                {
                    existing.Active = active;
                    existing.LastUpdate = DateTime.UtcNow;
                }
                else if (active)
                {
                    buffs.Add(new BuffStatus { Name = buffName, Active = true });
                }
            }
        }

        private void CheckTeamHealth()
        {
            foreach (var kvp in _characters)
            {
                if (kvp.Value.HealthPercent < 25)
                {
                    var role = _teamRoles.TryGetValue(kvp.Key, out var r) ? r : "DPS";
                    AddCallout($"{kvp.Key} ({role}) HP critical: {kvp.Value.HealthPercent}%",
                        CalloutType.Warning, 4000);
                    OnTeamAlert?.Invoke(kvp.Key, "Health critical");
                }
            }
        }

        public override async Task<IEnumerable<AnalysisResult>> AnalyzeAsync()
        {
            var results = new List<AnalysisResult>();

            // Team composition
            if (_characters.Count > 0)
            {
                var roleCount = _teamRoles.Values.GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
                var composition = string.Join(", ", roleCount.Select(r => $"{r.Key}: {r.Value}"));

                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = "Team Composition",
                    Content = $"{_characters.Count} characters - {composition}",
                    Severity = AnalysisSeverity.Info,
                    Metadata = { { "count", _characters.Count } }
                });
            }

            // Health status
            var lowHealth = _characters.Values.Where(c => c.HealthPercent < 50).ToList();
            if (lowHealth.Count > 0)
            {
                var names = string.Join(", ", lowHealth.Select(c => $"{c.Name}:{c.HealthPercent}%"));
                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = "Low Health Alert",
                    Content = names,
                    Severity = lowHealth.Any(c => c.HealthPercent < 25)
                        ? AnalysisSeverity.Critical
                        : AnalysisSeverity.Warning
                });
            }

            // Buff sync status
            if (EnableBuffSync)
            {
                var buffCounts = new Dictionary<string, int>();
                foreach (var charBuffs in _buffStatus.Values)
                {
                    lock (charBuffs)
                    {
                        foreach (var buff in charBuffs.Where(b => b.Active))
                        {
                            if (!buffCounts.ContainsKey(buff.Name))
                                buffCounts[buff.Name] = 0;
                            buffCounts[buff.Name]++;
                        }
                    }
                }

                // Find buffs that aren't on all characters
                var missingBuffs = buffCounts.Where(b => b.Value < _characters.Count && b.Value > 0)
                    .Select(b => $"{b.Key}: {b.Value}/{_characters.Count}")
                    .ToList();

                if (missingBuffs.Count > 0)
                {
                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = "Buff Sync",
                        Content = string.Join(", ", missingBuffs.Take(3)),
                        Severity = AnalysisSeverity.Suggestion
                    });
                }
            }

            // Synergy analysis
            if (EnableSynergyAnalysis && _characters.Count >= 2 && LLM != null && LLM.Config.Enabled)
            {
                var professions = _characters.Values
                    .Where(c => !string.IsNullOrEmpty(c.Profession))
                    .Select(c => c.Profession)
                    .Distinct()
                    .ToList();

                if (professions.Count >= 2)
                {
                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = "Team Synergy",
                        Content = "LLM analysis available",
                        Severity = AnalysisSeverity.Info,
                        RequiresLLM = true,
                        LLMPrompt = $"Analyze team synergy for: {string.Join(", ", professions)}. " +
                                   $"Suggest buff priorities and coordination strategies. Under 100 words."
                    });
                }
            }

            return results;
        }

        public override string GetContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Hydra Team Context ===");
            sb.AppendLine($"Characters: {_characters.Count}");
            sb.AppendLine();

            foreach (var character in _characters.Values.OrderBy(c => c.Name))
            {
                var role = _teamRoles.TryGetValue(character.Name, out var r) ? r : "Unassigned";
                sb.AppendLine($"{character.Name} ({role}):");
                sb.AppendLine($"  HP: {character.HealthPercent}%, Nano: {character.NanoPercent}%");

                if (_buffStatus.TryGetValue(character.Name, out var buffs))
                {
                    lock (buffs)
                    {
                        var activeBuffs = buffs.Where(b => b.Active).Select(b => b.Name).Take(5);
                        if (activeBuffs.Any())
                        {
                            sb.AppendLine($"  Buffs: {string.Join(", ", activeBuffs)}");
                        }
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // Public API for registering characters
        public void RegisterCharacter(string name, string profession = null, string role = null)
        {
            var state = _characters.GetOrAdd(name, _ => new CharacterState { Name = name });
            state.Profession = profession ?? state.Profession;

            if (!string.IsNullOrEmpty(role))
            {
                _teamRoles[name] = role;
            }
        }

        public void UnregisterCharacter(string name)
        {
            _characters.TryRemove(name, out _);
            _teamRoles.TryRemove(name, out _);
            _buffStatus.TryRemove(name, out _);
        }

        public void UpdateCharacterStats(string name, Dictionary<string, int> stats)
        {
            if (!_characters.TryGetValue(name, out var state)) return;

            foreach (var stat in stats)
            {
                state.Stats[stat.Key] = stat.Value;
            }
            state.LastUpdate = DateTime.UtcNow;

            // Update health/nano percentages
            if (stats.TryGetValue("Health", out var hp) && stats.TryGetValue("MaxHealth", out var maxHp))
            {
                state.HealthPercent = maxHp > 0 ? (int)((double)hp / maxHp * 100) : 100;
            }
            if (stats.TryGetValue("CurrentNano", out var nano) && stats.TryGetValue("MaxNanoEnergy", out var maxNano))
            {
                state.NanoPercent = maxNano > 0 ? (int)((double)nano / maxNano * 100) : 100;
            }

            OnCharacterUpdated?.Invoke(name, state);
        }

        public void SetCharacterRole(string name, string role)
        {
            _teamRoles[name] = role;
        }

        public CharacterState GetCharacter(string name)
        {
            return _characters.TryGetValue(name, out var state) ? state : null;
        }

        public IEnumerable<CharacterState> GetAllCharacters()
        {
            return _characters.Values;
        }

        public void ClearTeam()
        {
            _characters.Clear();
            _teamRoles.Clear();
            _buffStatus.Clear();
        }

        public override string GetConfigJson()
        {
            return $"{{\"id\":\"{Id}\",\"name\":\"{Name}\",\"domain\":\"{Domain}\",\"enabled\":{Enabled.ToString().ToLower()}," +
                   $"\"enableTeamTracking\":{EnableTeamTracking.ToString().ToLower()}," +
                   $"\"enableBuffSync\":{EnableBuffSync.ToString().ToLower()}," +
                   $"\"enableSynergyAnalysis\":{EnableSynergyAnalysis.ToString().ToLower()}," +
                   $"\"maxTrackedCharacters\":{MaxTrackedCharacters}," +
                   $"\"characterCount\":{_characters.Count}}}";
        }
    }

    // Supporting classes
    public sealed class CharacterState
    {
        public string Name { get; set; }
        public string Profession { get; set; }
        public string Breed { get; set; }
        public int Level { get; set; }
        public int HealthPercent { get; set; } = 100;
        public int NanoPercent { get; set; } = 100;
        public Dictionary<string, int> Stats { get; } = new Dictionary<string, int>();
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        public bool IsOnline => (DateTime.UtcNow - LastUpdate).TotalMinutes < 5;
    }

    public sealed class BuffStatus
    {
        public string Name { get; set; }
        public bool Active { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        public int RemainingSeconds { get; set; }
    }
}
