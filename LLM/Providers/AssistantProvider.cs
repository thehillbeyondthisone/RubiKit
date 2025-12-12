// RubiKit Assistant Provider
// General-purpose AI assistant for questions, calculations, and guidance

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RubiKit.LLM.Providers
{
    /// <summary>
    /// General assistant provider - answers questions, provides guidance, helps with calculations
    /// </summary>
    public sealed class AssistantProvider : AnalysisProviderBase
    {
        public override string Id => "assistant";
        public override string Name => "AI Assistant";
        public override string Domain => "assistant";
        public override int Priority => 10; // Low priority - mostly on-demand

        // Configuration
        public bool EnableProactiveHelp { get; set; } = true;
        public int MaxHistoryLength { get; set; } = 20;

        // Conversation history for this session
        private readonly List<ChatMessage> _chatHistory = new List<ChatMessage>();

        // Quick answer cache
        private readonly Dictionary<string, (string answer, DateTime expires)> _answerCache =
            new Dictionary<string, (string, DateTime)>();

        public override void Initialize(ContextEngine context, LLMService llm)
        {
            base.Initialize(context, llm);

            // Add custom system prompts for assistant
            if (llm != null)
            {
                llm.RegisterSystemPrompt("assistant", @"You are RubiKit AI, an intelligent assistant for Anarchy Online players.
You have deep knowledge of AO mechanics including:
- Implant requirements (Treatment, abilities by slot)
- Nano programming and skill requirements
- Armor class mechanics and damage types
- IP allocation and leveling strategies
- Tradeskills and crafting
- PvP and PvE strategies

When given context about the player's stats, use it to provide personalized advice.
Be concise but helpful. Use numbers when relevant.
If you don't know something specific, say so rather than guessing.");
            }
        }

        /// <summary>
        /// Ask a question to the AI assistant
        /// </summary>
        public async Task<string> AskAsync(string question, bool includeContext = true)
        {
            if (LLM == null || !LLM.Config.Enabled)
            {
                return "LLM service is not available. Check your connection to LMStudio.";
            }

            // Check cache
            var cacheKey = question.ToLower().Trim();
            if (_answerCache.TryGetValue(cacheKey, out var cached) && cached.expires > DateTime.UtcNow)
            {
                return cached.answer;
            }

            // Build context
            string context = null;
            if (includeContext)
            {
                context = Context.GetContextForDomain("assistant");
            }

            // Add to history
            _chatHistory.Add(new ChatMessage
            {
                Role = "user",
                Content = question,
                Timestamp = DateTime.UtcNow
            });

            // Get response
            var response = await LLM.CompleteAsync(question, "assistant", context);

            if (response.Success)
            {
                // Add to history
                _chatHistory.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = response.Content,
                    Timestamp = DateTime.UtcNow
                });

                // Trim history
                while (_chatHistory.Count > MaxHistoryLength)
                {
                    _chatHistory.RemoveAt(0);
                }

                // Cache short answers
                if (response.Content.Length < 500)
                {
                    _answerCache[cacheKey] = (response.Content, DateTime.UtcNow.AddMinutes(10));
                }

                return response.Content;
            }
            else
            {
                return $"Error: {response.Error}";
            }
        }

        /// <summary>
        /// Ask with streaming response
        /// </summary>
        public async Task AskStreamingAsync(string question, Action<string> onChunk, bool includeContext = true)
        {
            if (LLM == null || !LLM.Config.Enabled)
            {
                onChunk?.Invoke("LLM service is not available.");
                return;
            }

            string context = includeContext ? Context.GetContextForDomain("assistant") : null;

            _chatHistory.Add(new ChatMessage
            {
                Role = "user",
                Content = question,
                Timestamp = DateTime.UtcNow
            });

            var fullResponse = new StringBuilder();

            var response = await LLM.CompleteStreamingAsync(question, "assistant", context, chunk =>
            {
                fullResponse.Append(chunk);
                onChunk?.Invoke(chunk);
            });

            if (response.Success)
            {
                _chatHistory.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = fullResponse.ToString(),
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Get quick calculation (e.g., "treatment for QL 200 implant?")
        /// </summary>
        public string Calculate(string query)
        {
            query = query.ToLower().Trim();

            // Treatment requirements
            if (query.Contains("treatment") && query.Contains("ql"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(query, @"ql\s*(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int ql))
                {
                    var treatment = BuildAnalyzer.GetTreatmentForQL(ql);
                    return $"QL {ql} implants require {treatment} Treatment";
                }
            }

            // Damage calculations
            if (query.Contains("damage") || query.Contains("dps"))
            {
                // TODO: Add damage calculation formulas
                return "Damage calculations require more context. Try: 'calculate dps for [weapon] at [attack speed]'";
            }

            // IP calculations
            if (query.Contains("ip") && (query.Contains("cost") || query.Contains("skill")))
            {
                // TODO: Add IP cost formulas
                return "IP calculations require skill name and target level.";
            }

            return null; // Unknown calculation, fallback to LLM
        }

        public override async Task<IEnumerable<AnalysisResult>> AnalyzeAsync()
        {
            var results = new List<AnalysisResult>();

            // Proactive help suggestions
            if (EnableProactiveHelp)
            {
                var suggestions = GenerateProactiveSuggestions();
                if (suggestions.Count > 0)
                {
                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = "AI Suggestions",
                        Content = string.Join("; ", suggestions.Take(2)),
                        Severity = AnalysisSeverity.Suggestion
                    });
                }
            }

            // Session summary
            if (_chatHistory.Count > 0)
            {
                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = "Chat History",
                    Content = $"{_chatHistory.Count} messages this session",
                    Severity = AnalysisSeverity.Info
                });
            }

            return results;
        }

        private List<string> GenerateProactiveSuggestions()
        {
            var suggestions = new List<string>();

            // Check if player could upgrade implants
            var treatment = Context.GetStat("Treatment");
            var nextTier = GetNextImplantTier(treatment);
            if (nextTier > 0)
            {
                var needed = BuildAnalyzer.GetTreatmentForQL(nextTier) - treatment;
                if (needed > 0 && needed <= 50)
                {
                    suggestions.Add($"Ask: 'How can I get +{needed} Treatment for QL {nextTier} implants?'");
                }
            }

            // Check build optimization opportunities
            var aao = Context.GetStat("AddAllOff");
            var aad = Context.GetStat("AddAllDef");
            if (aao > 0 && aad > 0 && Math.Abs(aao - aad) > 100)
            {
                var focus = aao > aad ? "offense" : "defense";
                suggestions.Add($"Your build is {focus}-focused. Ask: 'Should I balance AAO/AAD?'");
            }

            return suggestions;
        }

        private int GetNextImplantTier(int treatment)
        {
            int[] tiers = { 50, 100, 150, 200, 250, 300 };
            foreach (var tier in tiers)
            {
                var req = BuildAnalyzer.GetTreatmentForQL(tier);
                if (treatment < req && treatment >= req - 100)
                {
                    return tier;
                }
            }
            return 0;
        }

        public override string GetContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Assistant Context ===");
            sb.AppendLine($"Chat History: {_chatHistory.Count} messages");

            if (_chatHistory.Count > 0)
            {
                sb.AppendLine("Recent questions:");
                foreach (var msg in _chatHistory.Where(m => m.Role == "user").TakeLast(3))
                {
                    sb.AppendLine($"  - {msg.Content.Substring(0, Math.Min(50, msg.Content.Length))}...");
                }
            }

            return sb.ToString();
        }

        public void ClearHistory()
        {
            _chatHistory.Clear();
            _answerCache.Clear();
        }

        public IEnumerable<ChatMessage> GetHistory() => _chatHistory.AsReadOnly();

        public override string GetConfigJson()
        {
            return $"{{\"id\":\"{Id}\",\"name\":\"{Name}\",\"domain\":\"{Domain}\",\"enabled\":{Enabled.ToString().ToLower()}," +
                   $"\"enableProactiveHelp\":{EnableProactiveHelp.ToString().ToLower()}," +
                   $"\"maxHistoryLength\":{MaxHistoryLength}," +
                   $"\"historyCount\":{_chatHistory.Count}}}";
        }
    }

    public sealed class ChatMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
