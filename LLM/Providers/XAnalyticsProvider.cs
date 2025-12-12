// RubiKit XAnalytics Provider
// Provides XP tracking, leveling analysis, and progression insights

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RubiKit.LLM.Providers
{
    /// <summary>
    /// XAnalytics provider - XP tracking, leveling, progression analysis
    /// </summary>
    public sealed class XAnalyticsProvider : AnalysisProviderBase
    {
        public override string Id => "xanalytics";
        public override string Name => "XAnalytics";
        public override string Domain => "progression";
        public override int Priority => 50;

        // Configuration
        public bool EnableXPTracking { get; set; } = true;
        public bool EnableLevelPrediction { get; set; } = true;
        public bool EnableSessionSummary { get; set; } = true;
        public int XPAlertThreshold { get; set; } = 1000000; // Alert on 1M+ XP gains

        // Tracking
        private DateTime _sessionStart = DateTime.UtcNow;
        private long _sessionStartXP = 0;
        private long _lastXP = 0;
        private readonly List<(DateTime time, long xp, string source)> _xpHistory = new List<(DateTime, long, string)>();
        private readonly Dictionary<string, long> _xpBySource = new Dictionary<string, long>();
        private int _lastLevel = 0;

        // AO Level/XP data (simplified)
        private static readonly Dictionary<int, long> LevelXP = new Dictionary<int, long>
        {
            { 1, 0 }, { 10, 1500 }, { 20, 12000 }, { 30, 40500 }, { 40, 96000 },
            { 50, 187500 }, { 60, 324000 }, { 70, 514500 }, { 80, 768000 },
            { 90, 1093500 }, { 100, 1500000 }, { 110, 1996500 }, { 120, 2592000 },
            { 130, 3295500 }, { 140, 4116000 }, { 150, 5062500 }, { 160, 6144000 },
            { 170, 7369500 }, { 180, 8748000 }, { 190, 10288500 }, { 200, 12000000 },
            { 205, 15000000 }, { 210, 20000000 }, { 215, 30000000 }, { 220, 50000000 }
        };

        public override void Initialize(ContextEngine context, LLMService llm)
        {
            base.Initialize(context, llm);
            _sessionStart = DateTime.UtcNow;

            // Try to get initial XP
            var xpMod = context.GetStat("XPModifier");
            _sessionStartXP = xpMod;
            _lastXP = xpMod;
        }

        public override void OnEvent(GameEvent evt)
        {
            if (evt.Type == GameEventType.StatChanged)
            {
                if (evt.Data.TryGetValue("stat", out var stat) && stat?.ToString() == "XPModifier")
                {
                    if (evt.Data.TryGetValue("newValue", out var newVal))
                    {
                        var xp = Convert.ToInt64(newVal);
                        TrackXPGain(xp);
                    }
                }
            }
        }

        private void TrackXPGain(long newXP)
        {
            if (!EnableXPTracking) return;

            var gain = newXP - _lastXP;
            if (gain > 0)
            {
                _xpHistory.Add((DateTime.UtcNow, gain, "combat"));
                if (!_xpBySource.ContainsKey("combat"))
                    _xpBySource["combat"] = 0;
                _xpBySource["combat"] += gain;

                // Prune old history (keep last hour)
                var cutoff = DateTime.UtcNow.AddHours(-1);
                _xpHistory.RemoveAll(x => x.time < cutoff);

                // Large XP gain alert
                if (gain >= XPAlertThreshold)
                {
                    AddCallout($"+{gain:N0} XP!", CalloutType.Success, 3000);
                }
            }

            _lastXP = newXP;
        }

        public void TrackXPGain(long amount, string source)
        {
            if (!EnableXPTracking) return;

            _xpHistory.Add((DateTime.UtcNow, amount, source));
            if (!_xpBySource.ContainsKey(source))
                _xpBySource[source] = 0;
            _xpBySource[source] += amount;

            _lastXP += amount;

            if (amount >= XPAlertThreshold)
            {
                AddCallout($"+{amount:N0} XP from {source}!", CalloutType.Success, 3000);
            }
        }

        public override async Task<IEnumerable<AnalysisResult>> AnalyzeAsync()
        {
            var results = new List<AnalysisResult>();

            // Session summary
            var sessionDuration = DateTime.UtcNow - _sessionStart;
            var totalXP = _xpHistory.Sum(x => x.xp);
            var xpPerHour = sessionDuration.TotalHours > 0 ? totalXP / sessionDuration.TotalHours : 0;

            results.Add(new AnalysisResult
            {
                ProviderId = Id,
                Title = "Session XP",
                Content = $"{totalXP:N0} XP in {sessionDuration.TotalMinutes:N0} minutes ({xpPerHour:N0}/hr)",
                Severity = AnalysisSeverity.Info,
                Metadata = {
                    { "totalXP", totalXP },
                    { "xpPerHour", xpPerHour },
                    { "duration", sessionDuration.TotalMinutes }
                }
            });

            // XP by source breakdown
            if (_xpBySource.Count > 1)
            {
                var sources = string.Join(", ", _xpBySource.OrderByDescending(x => x.Value)
                    .Take(3).Select(x => $"{x.Key}: {x.Value:N0}"));

                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = "XP Sources",
                    Content = sources,
                    Severity = AnalysisSeverity.Info
                });
            }

            // Level prediction
            if (EnableLevelPrediction && xpPerHour > 0)
            {
                var level = Context.Build.Level;
                var nextLevelXP = GetXPForLevel(level + 1);
                var currentXP = _lastXP;

                if (nextLevelXP > currentXP)
                {
                    var remaining = nextLevelXP - currentXP;
                    var hoursToLevel = remaining / xpPerHour;

                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = $"Level {level + 1} ETA",
                        Content = $"{remaining:N0} XP remaining (~{hoursToLevel:F1} hours at current rate)",
                        Severity = hoursToLevel < 1 ? AnalysisSeverity.Opportunity : AnalysisSeverity.Info,
                        Metadata = {
                            { "remainingXP", remaining },
                            { "hoursToLevel", hoursToLevel }
                        }
                    });
                }
            }

            // Recent XP rate (last 10 minutes)
            var recentCutoff = DateTime.UtcNow.AddMinutes(-10);
            var recentXP = _xpHistory.Where(x => x.time > recentCutoff).Sum(x => x.xp);
            var recentRate = recentXP * 6; // Project to hourly

            if (recentRate > 0 && Math.Abs(recentRate - xpPerHour) / Math.Max(1, xpPerHour) > 0.2)
            {
                var trend = recentRate > xpPerHour ? "UP" : "DOWN";
                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = "XP Rate Trend",
                    Content = $"Recent rate {trend}: {recentRate:N0}/hr (session avg: {xpPerHour:N0}/hr)",
                    Severity = AnalysisSeverity.Info
                });
            }

            return results;
        }

        private long GetXPForLevel(int level)
        {
            if (LevelXP.TryGetValue(level, out var xp))
                return xp;

            // Interpolate for levels not in table
            var lower = LevelXP.Where(l => l.Key < level).OrderByDescending(l => l.Key).FirstOrDefault();
            var upper = LevelXP.Where(l => l.Key > level).OrderBy(l => l.Key).FirstOrDefault();

            if (lower.Key == 0) return upper.Value;
            if (upper.Key == 0) return lower.Value;

            var ratio = (double)(level - lower.Key) / (upper.Key - lower.Key);
            return (long)(lower.Value + (upper.Value - lower.Value) * ratio);
        }

        public override string GetContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== XAnalytics Context ===");

            var sessionDuration = DateTime.UtcNow - _sessionStart;
            var totalXP = _xpHistory.Sum(x => x.xp);
            var xpPerHour = sessionDuration.TotalHours > 0 ? totalXP / sessionDuration.TotalHours : 0;

            sb.AppendLine($"Session Duration: {sessionDuration.TotalMinutes:N0} minutes");
            sb.AppendLine($"Total XP: {totalXP:N0}");
            sb.AppendLine($"XP/Hour: {xpPerHour:N0}");
            sb.AppendLine();

            if (_xpBySource.Count > 0)
            {
                sb.AppendLine("XP by Source:");
                foreach (var source in _xpBySource.OrderByDescending(x => x.Value))
                {
                    sb.AppendLine($"  {source.Key}: {source.Value:N0}");
                }
            }

            return sb.ToString();
        }

        public void ResetSession()
        {
            _sessionStart = DateTime.UtcNow;
            _sessionStartXP = _lastXP;
            _xpHistory.Clear();
            _xpBySource.Clear();
        }

        public override string GetConfigJson()
        {
            return $"{{\"id\":\"{Id}\",\"name\":\"{Name}\",\"domain\":\"{Domain}\",\"enabled\":{Enabled.ToString().ToLower()}," +
                   $"\"enableXPTracking\":{EnableXPTracking.ToString().ToLower()}," +
                   $"\"enableLevelPrediction\":{EnableLevelPrediction.ToString().ToLower()}," +
                   $"\"enableSessionSummary\":{EnableSessionSummary.ToString().ToLower()}," +
                   $"\"xpAlertThreshold\":{XPAlertThreshold}}}";
        }
    }
}
