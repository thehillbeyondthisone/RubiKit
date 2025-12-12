// RubiKit Analysis Provider Interface
// Base interface for all domain-specific analysis providers

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RubiKit.LLM.Providers
{
    /// <summary>
    /// Analysis result from a provider
    /// </summary>
    public sealed class AnalysisResult
    {
        public string ProviderId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public AnalysisSeverity Severity { get; set; } = AnalysisSeverity.Info;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public bool RequiresLLM { get; set; } = false;
        public string LLMPrompt { get; set; }

        public string ToJson()
        {
            var meta = string.Join(",", Metadata.Select(m => $"\"{m.Key}\":\"{Escape(m.Value?.ToString() ?? "")}\""));
            return $"{{\"providerId\":\"{Escape(ProviderId)}\",\"title\":\"{Escape(Title)}\",\"content\":\"{Escape(Content)}\"," +
                   $"\"severity\":\"{Severity}\",\"timestamp\":\"{Timestamp:O}\",\"metadata\":{{{meta}}}," +
                   $"\"requiresLLM\":{RequiresLLM.ToString().ToLower()}}}";
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

    public enum AnalysisSeverity
    {
        Debug,
        Info,
        Suggestion,
        Warning,
        Critical,
        Opportunity // For positive insights like arbitrage opportunities
    }

    /// <summary>
    /// Callout for real-time display
    /// </summary>
    public sealed class Callout
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Source { get; set; }
        public string Text { get; set; }
        public CalloutType Type { get; set; } = CalloutType.Info;
        public int DurationMs { get; set; } = 5000;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool Pinned { get; set; } = false;
        public string Icon { get; set; }

        public string ToJson()
        {
            return $"{{\"id\":\"{Id}\",\"source\":\"{Escape(Source)}\",\"text\":\"{Escape(Text)}\"," +
                   $"\"type\":\"{Type}\",\"durationMs\":{DurationMs},\"timestamp\":\"{Timestamp:O}\"," +
                   $"\"pinned\":{Pinned.ToString().ToLower()},\"icon\":\"{Escape(Icon ?? "")}\"}}";
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
    }

    public enum CalloutType
    {
        Info,
        Success,
        Warning,
        Error,
        Tip,
        Combat,
        Trade,
        Build
    }

    /// <summary>
    /// Base interface for all analysis providers
    /// </summary>
    public interface IAnalysisProvider
    {
        /// <summary>
        /// Unique identifier for this provider
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Display name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Domain this provider analyzes (combat, trading, builds, etc.)
        /// </summary>
        string Domain { get; }

        /// <summary>
        /// Whether this provider is currently enabled
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        /// Priority for ordering (higher = processed first)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Initialize the provider with context
        /// </summary>
        void Initialize(ContextEngine context, LLMService llm);

        /// <summary>
        /// Perform analysis and return results
        /// </summary>
        Task<IEnumerable<AnalysisResult>> AnalyzeAsync();

        /// <summary>
        /// Get quick callouts for real-time display
        /// </summary>
        IEnumerable<Callout> GetCallouts();

        /// <summary>
        /// Handle a game event
        /// </summary>
        void OnEvent(GameEvent evt);

        /// <summary>
        /// Get provider-specific context for LLM
        /// </summary>
        string GetContext();

        /// <summary>
        /// Get provider configuration as JSON
        /// </summary>
        string GetConfigJson();

        /// <summary>
        /// Update provider configuration
        /// </summary>
        void UpdateConfig(Dictionary<string, string> config);
    }

    /// <summary>
    /// Base class with common functionality for providers
    /// </summary>
    public abstract class AnalysisProviderBase : IAnalysisProvider
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Domain { get; }
        public bool Enabled { get; set; } = true;
        public virtual int Priority => 0;

        protected ContextEngine Context { get; private set; }
        protected LLMService LLM { get; private set; }
        protected readonly List<Callout> _pendingCallouts = new List<Callout>();
        protected readonly object _calloutLock = new object();

        public virtual void Initialize(ContextEngine context, LLMService llm)
        {
            Context = context;
            LLM = llm;

            // Register as context provider
            context.RegisterContextProvider(Id, GetContext);
        }

        public abstract Task<IEnumerable<AnalysisResult>> AnalyzeAsync();

        public virtual IEnumerable<Callout> GetCallouts()
        {
            lock (_calloutLock)
            {
                var callouts = _pendingCallouts.ToList();
                _pendingCallouts.Clear();
                return callouts;
            }
        }

        protected void AddCallout(Callout callout)
        {
            lock (_calloutLock)
            {
                _pendingCallouts.Add(callout);
                // Keep max 10 pending callouts
                while (_pendingCallouts.Count > 10)
                    _pendingCallouts.RemoveAt(0);
            }
        }

        protected void AddCallout(string text, CalloutType type = CalloutType.Info, int durationMs = 5000)
        {
            AddCallout(new Callout
            {
                Source = Id,
                Text = text,
                Type = type,
                DurationMs = durationMs
            });
        }

        public virtual void OnEvent(GameEvent evt) { }

        public virtual string GetContext() => "";

        public virtual string GetConfigJson()
        {
            return $"{{\"id\":\"{Id}\",\"name\":\"{Name}\",\"domain\":\"{Domain}\",\"enabled\":{Enabled.ToString().ToLower()},\"priority\":{Priority}}}";
        }

        public virtual void UpdateConfig(Dictionary<string, string> config)
        {
            if (config.TryGetValue("enabled", out var enabled))
            {
                Enabled = enabled == "true" || enabled == "1";
            }
        }
    }
}
