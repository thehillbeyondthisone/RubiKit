// RubiKit LLM Service - OpenAI-compatible client for LMStudio and other local LLMs
// Designed for infinite expandability with provider abstraction

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RubiKit.LLM
{
    /// <summary>
    /// Configuration for LLM service connections
    /// </summary>
    public sealed class LLMConfig
    {
        public string Endpoint { get; set; } = "http://192.168.56.1:1234";
        public string Model { get; set; } = ""; // Empty = use default model
        public int MaxTokens { get; set; } = 500;
        public float Temperature { get; set; } = 0.7f;
        public int TimeoutMs { get; set; } = 30000;
        public bool Enabled { get; set; } = true;
        public int MaxContextMessages { get; set; } = 20;
        public int ThrottleMs { get; set; } = 1000; // Min time between requests

        public string ToJson()
        {
            return $"{{\"endpoint\":\"{Escape(Endpoint)}\",\"model\":\"{Escape(Model)}\",\"maxTokens\":{MaxTokens}," +
                   $"\"temperature\":{Temperature:F2},\"timeoutMs\":{TimeoutMs},\"enabled\":{Enabled.ToString().ToLower()}," +
                   $"\"maxContextMessages\":{MaxContextMessages},\"throttleMs\":{ThrottleMs}}}";
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Message structure for chat completions
    /// </summary>
    public sealed class LLMMessage
    {
        public string Role { get; set; } // "system", "user", "assistant"
        public string Content { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } // Module that generated this message

        public string ToJson() => $"{{\"role\":\"{Role}\",\"content\":\"{Escape(Content)}\"}}";
        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    /// <summary>
    /// Response from LLM completion
    /// </summary>
    public sealed class LLMResponse
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public string Error { get; set; }
        public int TokensUsed { get; set; }
        public long LatencyMs { get; set; }
        public string Model { get; set; }

        public string ToJson()
        {
            return $"{{\"success\":{Success.ToString().ToLower()},\"content\":\"{Escape(Content ?? "")}\",\"error\":\"{Escape(Error ?? "")}\"," +
                   $"\"tokensUsed\":{TokensUsed},\"latencyMs\":{LatencyMs},\"model\":\"{Escape(Model ?? "")}\"}}";
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    /// <summary>
    /// Streaming chunk from LLM
    /// </summary>
    public sealed class LLMStreamChunk
    {
        public string Delta { get; set; }
        public bool IsComplete { get; set; }
        public string FinishReason { get; set; }
    }

    /// <summary>
    /// Core LLM service - handles communication with LMStudio/OpenAI-compatible endpoints
    /// </summary>
    public sealed class LLMService : IDisposable
    {
        public LLMConfig Config { get; private set; }
        private readonly ConcurrentQueue<LLMMessage> _conversationHistory = new ConcurrentQueue<LLMMessage>();
        private readonly ConcurrentDictionary<string, string> _systemPrompts = new ConcurrentDictionary<string, string>();
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly object _throttleLock = new object();
        private bool _disposed = false;

        // Events for extensibility
        public event Action<LLMResponse> OnResponse;
        public event Action<LLMStreamChunk> OnStreamChunk;
        public event Action<string> OnError;
        public event Action<bool> OnConnectionStatusChanged;

        // Connection status
        public bool IsConnected { get; private set; }
        public DateTime LastSuccessfulRequest { get; private set; }
        public int TotalRequests { get; private set; }
        public int FailedRequests { get; private set; }

        public LLMService() : this(new LLMConfig()) { }

        public LLMService(LLMConfig config)
        {
            Config = config ?? new LLMConfig();
            InitializeDefaultPrompts();
        }

        private void InitializeDefaultPrompts()
        {
            // Core system prompt for RubiKit context
            _systemPrompts["core"] = @"You are RubiKit AI, an intelligent assistant integrated into RubiKit for Anarchy Online.
You have real-time access to player stats, combat data, market information, and game state.
Provide concise, actionable insights. Be specific with numbers and recommendations.
Format callouts for quick reading - use short phrases, not paragraphs.
You understand AO mechanics: nano programs, implants, armor classes, damage types, perks, research, and tradeskills.";

            // Combat analysis prompt
            _systemPrompts["combat"] = @"You are analyzing combat data for Anarchy Online.
Focus on: optimal damage types vs enemy ACs, init requirements, heal timing, nano efficiency.
Provide tactical callouts like: 'Target weak to Chemical (AC: 2400 vs your 8500 Chem dmg)'
Keep responses under 50 words for real-time callouts.";

            // Trading/market prompt
            _systemPrompts["trading"] = @"You are analyzing Anarchy Online market data.
Focus on: price trends, arbitrage opportunities, supply/demand, crafting profits.
Provide insights like: 'GMS spread +15% on Carbonum - buy orders at 2.1M, sells at 2.4M'
Be specific with credits and percentages.";

            // Build/equipment prompt
            _systemPrompts["builds"] = @"You are an Anarchy Online build optimizer.
Focus on: IP allocation, implant QL requirements, symbiants, armor choices, nano requirements.
Provide insights like: 'QL 240 implant needs 951 Treatment - you have 923, need +28'
Consider trickle-down effects and buff requirements.";

            // General assistant
            _systemPrompts["assistant"] = @"You are a helpful assistant for Anarchy Online players using RubiKit.
Answer questions about game mechanics, help with calculations, and provide general guidance.
You have access to real-time character data when provided in context.";
        }

        /// <summary>
        /// Register or update a system prompt for a specific domain
        /// </summary>
        public void RegisterSystemPrompt(string domain, string prompt)
        {
            _systemPrompts[domain] = prompt;
        }

        /// <summary>
        /// Get a registered system prompt
        /// </summary>
        public string GetSystemPrompt(string domain)
        {
            return _systemPrompts.TryGetValue(domain, out var prompt) ? prompt : _systemPrompts["core"];
        }

        /// <summary>
        /// Get all registered prompt domains
        /// </summary>
        public IEnumerable<string> GetPromptDomains() => _systemPrompts.Keys;

        /// <summary>
        /// Update configuration at runtime
        /// </summary>
        public void UpdateConfig(LLMConfig newConfig)
        {
            Config = newConfig ?? Config;
        }

        /// <summary>
        /// Add a message to conversation history
        /// </summary>
        public void AddToHistory(LLMMessage message)
        {
            _conversationHistory.Enqueue(message);

            // Trim history if too long
            while (_conversationHistory.Count > Config.MaxContextMessages)
            {
                _conversationHistory.TryDequeue(out _);
            }
        }

        /// <summary>
        /// Clear conversation history
        /// </summary>
        public void ClearHistory()
        {
            while (_conversationHistory.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Get current conversation history
        /// </summary>
        public IEnumerable<LLMMessage> GetHistory() => _conversationHistory.ToArray();

        /// <summary>
        /// Check if we should throttle the next request
        /// </summary>
        private bool ShouldThrottle()
        {
            lock (_throttleLock)
            {
                var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
                return elapsed < Config.ThrottleMs;
            }
        }

        /// <summary>
        /// Test connection to LLM endpoint
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var request = WebRequest.CreateHttp(Config.Endpoint + "/v1/models");
                request.Method = "GET";
                request.Timeout = 5000;

                using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                {
                    IsConnected = true;
                    OnConnectionStatusChanged?.Invoke(true);
                    return true;
                }
            }
            catch
            {
                IsConnected = false;
                OnConnectionStatusChanged?.Invoke(false);
                return false;
            }
        }

        /// <summary>
        /// Get available models from the endpoint
        /// </summary>
        public async Task<List<string>> GetAvailableModelsAsync()
        {
            var models = new List<string>();
            try
            {
                var request = WebRequest.CreateHttp(Config.Endpoint + "/v1/models");
                request.Method = "GET";
                request.Timeout = 5000;

                using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    // Simple parsing - extract model IDs
                    var startIdx = 0;
                    while ((startIdx = json.IndexOf("\"id\":", startIdx)) != -1)
                    {
                        startIdx += 5;
                        var quoteStart = json.IndexOf('"', startIdx);
                        if (quoteStart == -1) break;
                        var quoteEnd = json.IndexOf('"', quoteStart + 1);
                        if (quoteEnd == -1) break;
                        models.Add(json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1));
                        startIdx = quoteEnd;
                    }
                }
            }
            catch { }
            return models;
        }

        /// <summary>
        /// Send a completion request (non-streaming)
        /// </summary>
        public async Task<LLMResponse> CompleteAsync(string userMessage, string domain = "core", string additionalContext = null)
        {
            if (!Config.Enabled)
            {
                return new LLMResponse { Success = false, Error = "LLM service is disabled" };
            }

            if (ShouldThrottle())
            {
                return new LLMResponse { Success = false, Error = "Request throttled" };
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            TotalRequests++;

            try
            {
                lock (_throttleLock) { _lastRequestTime = DateTime.UtcNow; }

                var messages = BuildMessageList(userMessage, domain, additionalContext);
                var requestBody = BuildRequestBody(messages, false);

                var request = WebRequest.CreateHttp(Config.Endpoint + "/v1/chat/completions");
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = Config.TimeoutMs;

                var bodyBytes = Encoding.UTF8.GetBytes(requestBody);
                using (var reqStream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await reqStream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
                }

                using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var result = ParseCompletionResponse(json);
                    result.LatencyMs = sw.ElapsedMilliseconds;

                    if (result.Success)
                    {
                        IsConnected = true;
                        LastSuccessfulRequest = DateTime.UtcNow;

                        // Add to history
                        AddToHistory(new LLMMessage { Role = "user", Content = userMessage, Source = domain });
                        AddToHistory(new LLMMessage { Role = "assistant", Content = result.Content, Source = domain });
                    }

                    OnResponse?.Invoke(result);
                    return result;
                }
            }
            catch (WebException ex)
            {
                FailedRequests++;
                var error = $"Connection error: {ex.Message}";
                OnError?.Invoke(error);
                IsConnected = false;
                OnConnectionStatusChanged?.Invoke(false);
                return new LLMResponse { Success = false, Error = error, LatencyMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                FailedRequests++;
                var error = $"Error: {ex.Message}";
                OnError?.Invoke(error);
                return new LLMResponse { Success = false, Error = error, LatencyMs = sw.ElapsedMilliseconds };
            }
        }

        /// <summary>
        /// Send a streaming completion request
        /// </summary>
        public async Task<LLMResponse> CompleteStreamingAsync(string userMessage, string domain = "core",
            string additionalContext = null, Action<string> onChunk = null, CancellationToken ct = default)
        {
            if (!Config.Enabled)
            {
                return new LLMResponse { Success = false, Error = "LLM service is disabled" };
            }

            if (ShouldThrottle())
            {
                return new LLMResponse { Success = false, Error = "Request throttled" };
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var fullContent = new StringBuilder();
            TotalRequests++;

            try
            {
                lock (_throttleLock) { _lastRequestTime = DateTime.UtcNow; }

                var messages = BuildMessageList(userMessage, domain, additionalContext);
                var requestBody = BuildRequestBody(messages, true);

                var request = WebRequest.CreateHttp(Config.Endpoint + "/v1/chat/completions");
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = Config.TimeoutMs;

                var bodyBytes = Encoding.UTF8.GetBytes(requestBody);
                using (var reqStream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await reqStream.WriteAsync(bodyBytes, 0, bodyBytes.Length, ct).ConfigureAwait(false);
                }

                using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        if (ct.IsCancellationRequested) break;

                        if (line.StartsWith("data: "))
                        {
                            var data = line.Substring(6);
                            if (data == "[DONE]") break;

                            var chunk = ParseStreamChunk(data);
                            if (!string.IsNullOrEmpty(chunk.Delta))
                            {
                                fullContent.Append(chunk.Delta);
                                onChunk?.Invoke(chunk.Delta);
                                OnStreamChunk?.Invoke(chunk);
                            }
                        }
                    }
                }

                IsConnected = true;
                LastSuccessfulRequest = DateTime.UtcNow;

                var result = new LLMResponse
                {
                    Success = true,
                    Content = fullContent.ToString(),
                    LatencyMs = sw.ElapsedMilliseconds
                };

                AddToHistory(new LLMMessage { Role = "user", Content = userMessage, Source = domain });
                AddToHistory(new LLMMessage { Role = "assistant", Content = result.Content, Source = domain });

                OnResponse?.Invoke(result);
                return result;
            }
            catch (Exception ex)
            {
                FailedRequests++;
                IsConnected = false;
                OnConnectionStatusChanged?.Invoke(false);
                return new LLMResponse { Success = false, Error = ex.Message, LatencyMs = sw.ElapsedMilliseconds };
            }
        }

        /// <summary>
        /// Quick analysis - fire and forget style for real-time callouts
        /// </summary>
        public void AnalyzeAsync(string context, string domain, Action<string> callback)
        {
            Task.Run(async () =>
            {
                var response = await CompleteAsync(context, domain).ConfigureAwait(false);
                if (response.Success)
                {
                    callback?.Invoke(response.Content);
                }
            });
        }

        private List<LLMMessage> BuildMessageList(string userMessage, string domain, string additionalContext)
        {
            var messages = new List<LLMMessage>();

            // System prompt
            var systemPrompt = GetSystemPrompt(domain);
            if (!string.IsNullOrEmpty(additionalContext))
            {
                systemPrompt += "\n\nCurrent Context:\n" + additionalContext;
            }
            messages.Add(new LLMMessage { Role = "system", Content = systemPrompt });

            // Add relevant history
            foreach (var msg in _conversationHistory.Where(m => m.Source == domain || m.Source == "core").Take(10))
            {
                messages.Add(msg);
            }

            // Current message
            messages.Add(new LLMMessage { Role = "user", Content = userMessage });

            return messages;
        }

        private string BuildRequestBody(List<LLMMessage> messages, bool stream)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            if (!string.IsNullOrEmpty(Config.Model))
            {
                sb.Append($"\"model\":\"{Config.Model}\",");
            }

            sb.Append("\"messages\":[");
            sb.Append(string.Join(",", messages.Select(m => m.ToJson())));
            sb.Append("],");

            sb.Append($"\"max_tokens\":{Config.MaxTokens},");
            sb.Append($"\"temperature\":{Config.Temperature:F2},");
            sb.Append($"\"stream\":{stream.ToString().ToLower()}");
            sb.Append("}");

            return sb.ToString();
        }

        private LLMResponse ParseCompletionResponse(string json)
        {
            var response = new LLMResponse();
            try
            {
                // Parse content from choices[0].message.content
                var contentStart = json.IndexOf("\"content\":");
                if (contentStart != -1)
                {
                    contentStart = json.IndexOf('"', contentStart + 10) + 1;
                    var contentEnd = FindClosingQuote(json, contentStart);
                    response.Content = UnescapeJson(json.Substring(contentStart, contentEnd - contentStart));
                    response.Success = true;
                }

                // Parse model
                var modelStart = json.IndexOf("\"model\":");
                if (modelStart != -1)
                {
                    modelStart = json.IndexOf('"', modelStart + 8) + 1;
                    var modelEnd = json.IndexOf('"', modelStart);
                    response.Model = json.Substring(modelStart, modelEnd - modelStart);
                }

                // Parse token usage
                var usageStart = json.IndexOf("\"total_tokens\":");
                if (usageStart != -1)
                {
                    usageStart += 15;
                    var usageEnd = json.IndexOfAny(new[] { ',', '}' }, usageStart);
                    int.TryParse(json.Substring(usageStart, usageEnd - usageStart).Trim(), out int tokens);
                    response.TokensUsed = tokens;
                }
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Error = "Parse error: " + ex.Message;
            }
            return response;
        }

        private LLMStreamChunk ParseStreamChunk(string json)
        {
            var chunk = new LLMStreamChunk();
            try
            {
                var deltaStart = json.IndexOf("\"delta\":");
                if (deltaStart != -1)
                {
                    var contentStart = json.IndexOf("\"content\":", deltaStart);
                    if (contentStart != -1)
                    {
                        contentStart = json.IndexOf('"', contentStart + 10) + 1;
                        var contentEnd = FindClosingQuote(json, contentStart);
                        chunk.Delta = UnescapeJson(json.Substring(contentStart, contentEnd - contentStart));
                    }
                }

                var finishStart = json.IndexOf("\"finish_reason\":");
                if (finishStart != -1 && !json.Substring(finishStart, 30).Contains("null"))
                {
                    chunk.IsComplete = true;
                    var reasonStart = json.IndexOf('"', finishStart + 16) + 1;
                    var reasonEnd = json.IndexOf('"', reasonStart);
                    chunk.FinishReason = json.Substring(reasonStart, reasonEnd - reasonStart);
                }
            }
            catch { }
            return chunk;
        }

        private static int FindClosingQuote(string s, int start)
        {
            for (int i = start; i < s.Length; i++)
            {
                if (s[i] == '"' && (i == 0 || s[i - 1] != '\\')) return i;
            }
            return s.Length;
        }

        private static string UnescapeJson(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t")
                    .Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearHistory();
        }

        /// <summary>
        /// Get service status as JSON
        /// </summary>
        public string GetStatusJson()
        {
            return $"{{\"connected\":{IsConnected.ToString().ToLower()},\"endpoint\":\"{Config.Endpoint}\"," +
                   $"\"enabled\":{Config.Enabled.ToString().ToLower()},\"totalRequests\":{TotalRequests}," +
                   $"\"failedRequests\":{FailedRequests},\"historyCount\":{_conversationHistory.Count}," +
                   $"\"lastSuccess\":\"{LastSuccessfulRequest:O}\"}}";
        }
    }
}
