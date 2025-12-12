// RubiKit Module Registry
// Central hub for all module registration, API exposure, and LLM integration

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RubiKit.LLM.Providers;

namespace RubiKit.LLM
{
    /// <summary>
    /// Module information for registration
    /// </summary>
    public sealed class ModuleInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string Domain { get; set; } // combat, trading, builds, utility, etc.
        public bool SupportsLLM { get; set; } = true;
        public string[] RequiredAPIs { get; set; } = Array.Empty<string>();
        public string[] ProvidedAPIs { get; set; } = Array.Empty<string>();
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        public string ToJson()
        {
            var required = "[" + string.Join(",", RequiredAPIs.Select(a => $"\"{a}\"")) + "]";
            var provided = "[" + string.Join(",", ProvidedAPIs.Select(a => $"\"{a}\"")) + "]";

            return $"{{\"id\":\"{Escape(Id)}\",\"name\":\"{Escape(Name)}\",\"version\":\"{Escape(Version ?? "1.0.0")}\"," +
                   $"\"description\":\"{Escape(Description ?? "")}\",\"author\":\"{Escape(Author ?? "")}\"," +
                   $"\"domain\":\"{Escape(Domain ?? "utility")}\",\"supportsLLM\":{SupportsLLM.ToString().ToLower()}," +
                   $"\"requiredAPIs\":{required},\"providedAPIs\":{provided}}}";
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// API endpoint registration
    /// </summary>
    public sealed class APIEndpoint
    {
        public string Path { get; set; }
        public string Method { get; set; } = "GET";
        public string Description { get; set; }
        public string ModuleId { get; set; }
        public Func<Dictionary<string, string>, string> Handler { get; set; }
        public bool RequiresLLM { get; set; } = false;

        public string ToJson()
        {
            return $"{{\"path\":\"{Escape(Path)}\",\"method\":\"{Method}\",\"description\":\"{Escape(Description ?? "")}\"," +
                   $"\"moduleId\":\"{Escape(ModuleId ?? "core")}\",\"requiresLLM\":{RequiresLLM.ToString().ToLower()}}}";
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Central registry for all RubiKit modules and their APIs
    /// </summary>
    public sealed class ModuleRegistry : IDisposable
    {
        // Core services
        public LLMService LLM { get; }
        public ContextEngine Context { get; }

        // Module tracking
        private readonly ConcurrentDictionary<string, ModuleInfo> _modules = new ConcurrentDictionary<string, ModuleInfo>();
        private readonly ConcurrentDictionary<string, IAnalysisProvider> _providers = new ConcurrentDictionary<string, IAnalysisProvider>();
        private readonly ConcurrentDictionary<string, APIEndpoint> _endpoints = new ConcurrentDictionary<string, APIEndpoint>();

        // Callout management
        private readonly ConcurrentQueue<Callout> _calloutQueue = new ConcurrentQueue<Callout>();
        private const int MaxQueuedCallouts = 50;

        // Analysis scheduling
        private System.Timers.Timer _analysisTimer;
        private readonly object _analysisLock = new object();
        private bool _disposed = false;

        // Events
        public event Action<ModuleInfo> OnModuleRegistered;
        public event Action<string> OnModuleUnregistered;
        public event Action<Callout> OnCallout;
        public event Action<AnalysisResult> OnAnalysisResult;

        public ModuleRegistry() : this(new LLMConfig()) { }

        public ModuleRegistry(LLMConfig llmConfig)
        {
            LLM = new LLMService(llmConfig);
            Context = new ContextEngine();

            // Wire up context events to LLM
            Context.OnEvent += evt =>
            {
                // Could log events or trigger analysis here
            };

            InitializeCoreProviders();
            InitializeCoreEndpoints();
            StartAnalysisTimer();
        }

        private void InitializeCoreProviders()
        {
            // Register built-in analysis providers
            RegisterProvider(new CombatAnalyzer());
            RegisterProvider(new TradingAnalyzer());
            RegisterProvider(new BuildAnalyzer());
            RegisterProvider(new XAnalyticsProvider());
            RegisterProvider(new HydraProvider());
            RegisterProvider(new AssistantProvider());
        }

        private void InitializeCoreEndpoints()
        {
            // LLM endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/llm/status",
                Method = "GET",
                Description = "Get LLM service status",
                ModuleId = "core",
                Handler = _ => LLM.GetStatusJson()
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/llm/config",
                Method = "GET",
                Description = "Get LLM configuration",
                ModuleId = "core",
                Handler = _ => LLM.Config.ToJson()
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/llm/config",
                Method = "POST",
                Description = "Update LLM configuration",
                ModuleId = "core",
                Handler = args => UpdateLLMConfig(args)
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/llm/complete",
                Method = "POST",
                Description = "Send completion request to LLM",
                ModuleId = "core",
                RequiresLLM = true,
                Handler = args => HandleLLMComplete(args)
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/llm/models",
                Method = "GET",
                Description = "Get available LLM models",
                ModuleId = "core",
                Handler = _ => GetModelsJson()
            });

            // Context endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/context",
                Method = "GET",
                Description = "Get full context state",
                ModuleId = "core",
                Handler = args =>
                {
                    var domain = args.TryGetValue("domain", out var d) ? d : "core";
                    return $"{{\"context\":\"{Escape(Context.GetContextForDomain(domain))}\"}}";
                }
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/context/combat",
                Method = "GET",
                Description = "Get combat context",
                ModuleId = "core",
                Handler = _ => Context.Combat.ToContextString()
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/context/trading",
                Method = "GET",
                Description = "Get trading context",
                ModuleId = "core",
                Handler = _ => Context.Trading.ToContextString()
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/context/build",
                Method = "GET",
                Description = "Get build context",
                ModuleId = "core",
                Handler = _ => Context.Build.ToContextString()
            });

            // Provider endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/providers",
                Method = "GET",
                Description = "List all registered providers",
                ModuleId = "core",
                Handler = _ => GetProvidersJson()
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/providers/config",
                Method = "POST",
                Description = "Update provider configuration",
                ModuleId = "core",
                Handler = args => UpdateProviderConfig(args)
            });

            // Callout endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/callouts",
                Method = "GET",
                Description = "Get pending callouts",
                ModuleId = "core",
                Handler = _ => GetCalloutsJson()
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/callouts/dismiss",
                Method = "POST",
                Description = "Dismiss a callout",
                ModuleId = "core",
                Handler = args => DismissCallout(args)
            });

            // Analysis endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/analysis",
                Method = "GET",
                Description = "Get latest analysis results",
                ModuleId = "core",
                Handler = args => GetAnalysisJson(args)
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/analysis/run",
                Method = "POST",
                Description = "Trigger analysis run",
                ModuleId = "core",
                Handler = args => TriggerAnalysis(args)
            });

            // Module endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/modules",
                Method = "GET",
                Description = "List registered modules",
                ModuleId = "core",
                Handler = _ => GetModulesJson()
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/endpoints",
                Method = "GET",
                Description = "List all API endpoints",
                ModuleId = "core",
                Handler = _ => GetEndpointsJson()
            });

            // Events endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/events",
                Method = "GET",
                Description = "Get recent events",
                ModuleId = "core",
                Handler = args =>
                {
                    var count = args.TryGetValue("count", out var c) && int.TryParse(c, out var n) ? n : 20;
                    return GetEventsJson(count);
                }
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/events/push",
                Method = "POST",
                Description = "Push a game event",
                ModuleId = "core",
                Handler = args => HandleEventPush(args)
            });

            // Assistant endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/assistant/ask",
                Method = "POST",
                Description = "Ask the AI assistant a question",
                ModuleId = "core",
                RequiresLLM = true,
                Handler = args => HandleAssistantAsk(args)
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/assistant/history",
                Method = "GET",
                Description = "Get chat history",
                ModuleId = "core",
                Handler = _ => GetAssistantHistoryJson()
            });

            // Calculation endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = "/api/calc/implant",
                Method = "GET",
                Description = "Calculate implant requirements",
                ModuleId = "core",
                Handler = args => CalculateImplantReqs(args)
            });
        }

        private void StartAnalysisTimer()
        {
            _analysisTimer = new System.Timers.Timer(5000); // Run analysis every 5 seconds
            _analysisTimer.AutoReset = true;
            _analysisTimer.Elapsed += async (s, e) => await RunAnalysisAsync();
            _analysisTimer.Start();
        }

        /// <summary>
        /// Get the assistant provider for direct access
        /// </summary>
        public AssistantProvider GetAssistant()
        {
            return _providers.Values.OfType<AssistantProvider>().FirstOrDefault();
        }

        /// <summary>
        /// Get a specific provider by ID
        /// </summary>
        public IAnalysisProvider GetProvider(string id)
        {
            return _providers.TryGetValue(id, out var provider) ? provider : null;
        }

        /// <summary>
        /// Get a specific provider by type
        /// </summary>
        public T GetProvider<T>() where T : class, IAnalysisProvider
        {
            return _providers.Values.OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// Register a module
        /// </summary>
        public void RegisterModule(ModuleInfo module)
        {
            if (string.IsNullOrEmpty(module?.Id)) return;

            _modules[module.Id] = module;
            OnModuleRegistered?.Invoke(module);
        }

        /// <summary>
        /// Unregister a module
        /// </summary>
        public void UnregisterModule(string moduleId)
        {
            if (_modules.TryRemove(moduleId, out _))
            {
                // Remove associated endpoints
                var toRemove = _endpoints.Where(e => e.Value.ModuleId == moduleId).Select(e => e.Key).ToList();
                foreach (var key in toRemove)
                {
                    _endpoints.TryRemove(key, out _);
                }

                // Remove associated provider
                if (_providers.TryRemove(moduleId, out var provider))
                {
                    Context.UnregisterContextProvider(moduleId);
                }

                OnModuleUnregistered?.Invoke(moduleId);
            }
        }

        /// <summary>
        /// Register an analysis provider
        /// </summary>
        public void RegisterProvider(IAnalysisProvider provider)
        {
            if (provider == null) return;

            provider.Initialize(Context, LLM);
            _providers[provider.Id] = provider;

            // Register provider-specific endpoints
            RegisterEndpoint(new APIEndpoint
            {
                Path = $"/api/providers/{provider.Id}",
                Method = "GET",
                Description = $"Get {provider.Name} status and config",
                ModuleId = provider.Id,
                Handler = _ => provider.GetConfigJson()
            });

            RegisterEndpoint(new APIEndpoint
            {
                Path = $"/api/providers/{provider.Id}/context",
                Method = "GET",
                Description = $"Get {provider.Name} context",
                ModuleId = provider.Id,
                Handler = _ => $"{{\"context\":\"{Escape(provider.GetContext())}\"}}"
            });
        }

        /// <summary>
        /// Register an API endpoint
        /// </summary>
        public void RegisterEndpoint(APIEndpoint endpoint)
        {
            if (string.IsNullOrEmpty(endpoint?.Path)) return;

            var key = $"{endpoint.Method}:{endpoint.Path}";
            _endpoints[key] = endpoint;
        }

        /// <summary>
        /// Get endpoint by path and method
        /// </summary>
        public APIEndpoint GetEndpoint(string method, string path)
        {
            var key = $"{method}:{path}";
            return _endpoints.TryGetValue(key, out var endpoint) ? endpoint : null;
        }

        /// <summary>
        /// Handle an API request
        /// </summary>
        public string HandleRequest(string method, string path, Dictionary<string, string> parameters)
        {
            var endpoint = GetEndpoint(method, path);
            if (endpoint == null)
            {
                return $"{{\"error\":\"Endpoint not found: {method} {path}\"}}";
            }

            if (endpoint.RequiresLLM && !LLM.Config.Enabled)
            {
                return "{\"error\":\"LLM service is disabled\"}";
            }

            try
            {
                return endpoint.Handler?.Invoke(parameters) ?? "{}";
            }
            catch (Exception ex)
            {
                return $"{{\"error\":\"{Escape(ex.Message)}\"}}";
            }
        }

        /// <summary>
        /// Run analysis across all providers
        /// </summary>
        public async Task RunAnalysisAsync()
        {
            if (_disposed) return;

            lock (_analysisLock)
            {
                foreach (var provider in _providers.Values.Where(p => p.Enabled).OrderByDescending(p => p.Priority))
                {
                    try
                    {
                        // Get callouts
                        foreach (var callout in provider.GetCallouts())
                        {
                            QueueCallout(callout);
                        }

                        // Run analysis (async but we don't await to avoid blocking)
                        Task.Run(async () =>
                        {
                            try
                            {
                                var results = await provider.AnalyzeAsync();
                                foreach (var result in results)
                                {
                                    OnAnalysisResult?.Invoke(result);

                                    // If result requires LLM and has a prompt, execute it
                                    if (result.RequiresLLM && !string.IsNullOrEmpty(result.LLMPrompt) && LLM.Config.Enabled)
                                    {
                                        LLM.AnalyzeAsync(result.LLMPrompt, provider.Domain, response =>
                                        {
                                            QueueCallout(new Callout
                                            {
                                                Source = provider.Id,
                                                Text = response,
                                                Type = CalloutType.Tip,
                                                DurationMs = 8000
                                            });
                                        });
                                    }
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Queue a callout for display
        /// </summary>
        public void QueueCallout(Callout callout)
        {
            _calloutQueue.Enqueue(callout);

            // Trim queue
            while (_calloutQueue.Count > MaxQueuedCallouts)
            {
                _calloutQueue.TryDequeue(out _);
            }

            OnCallout?.Invoke(callout);
        }

        /// <summary>
        /// Get and clear pending callouts
        /// </summary>
        public IEnumerable<Callout> GetPendingCallouts()
        {
            var callouts = new List<Callout>();
            while (_calloutQueue.TryDequeue(out var callout))
            {
                callouts.Add(callout);
            }
            return callouts;
        }

        /// <summary>
        /// Push a game event to the context engine
        /// </summary>
        public void PushEvent(GameEvent evt)
        {
            Context.PushEvent(evt);

            // Forward to providers
            foreach (var provider in _providers.Values.Where(p => p.Enabled))
            {
                try
                {
                    provider.OnEvent(evt);
                }
                catch { }
            }
        }

        // JSON generation helpers
        private string GetModulesJson()
        {
            var modules = _modules.Values.Select(m => m.ToJson());
            return "[" + string.Join(",", modules) + "]";
        }

        private string GetProvidersJson()
        {
            var providers = _providers.Values.Select(p => p.GetConfigJson());
            return "[" + string.Join(",", providers) + "]";
        }

        private string GetEndpointsJson()
        {
            var endpoints = _endpoints.Values.Select(e => e.ToJson());
            return "[" + string.Join(",", endpoints) + "]";
        }

        private string GetCalloutsJson()
        {
            var callouts = _calloutQueue.ToArray();
            return "[" + string.Join(",", callouts.Select(c => c.ToJson())) + "]";
        }

        private string GetEventsJson(int count)
        {
            var events = Context.GetRecentEvents(count);
            var json = events.Select(e =>
                $"{{\"type\":\"{e.Type}\",\"timestamp\":\"{e.Timestamp:O}\",\"source\":\"{Escape(e.Source ?? "")}\"}}");
            return "[" + string.Join(",", json) + "]";
        }

        private string GetModelsJson()
        {
            var task = LLM.GetAvailableModelsAsync();
            task.Wait(5000);
            var models = task.IsCompleted ? task.Result : new List<string>();
            return "[" + string.Join(",", models.Select(m => $"\"{Escape(m)}\"")) + "]";
        }

        // Handler implementations
        private string UpdateLLMConfig(Dictionary<string, string> args)
        {
            var config = LLM.Config;

            if (args.TryGetValue("endpoint", out var endpoint))
                config.Endpoint = endpoint;
            if (args.TryGetValue("model", out var model))
                config.Model = model;
            if (args.TryGetValue("maxTokens", out var tokens) && int.TryParse(tokens, out var t))
                config.MaxTokens = t;
            if (args.TryGetValue("temperature", out var temp) && float.TryParse(temp, out var tp))
                config.Temperature = tp;
            if (args.TryGetValue("enabled", out var enabled))
                config.Enabled = enabled == "true" || enabled == "1";
            if (args.TryGetValue("throttleMs", out var throttle) && int.TryParse(throttle, out var th))
                config.ThrottleMs = th;

            return "{\"success\":true}";
        }

        private string HandleLLMComplete(Dictionary<string, string> args)
        {
            var prompt = args.TryGetValue("prompt", out var p) ? p : "";
            var domain = args.TryGetValue("domain", out var d) ? d : "core";
            var includeContext = !args.TryGetValue("noContext", out var nc) || nc != "true";

            if (string.IsNullOrEmpty(prompt))
            {
                return "{\"error\":\"Missing prompt\"}";
            }

            var context = includeContext ? Context.GetContextForDomain(domain) : null;
            var task = LLM.CompleteAsync(prompt, domain, context);
            task.Wait(30000);

            if (task.IsCompleted)
            {
                return task.Result.ToJson();
            }

            return "{\"error\":\"Request timed out\"}";
        }

        private string UpdateProviderConfig(Dictionary<string, string> args)
        {
            if (!args.TryGetValue("id", out var id))
            {
                return "{\"error\":\"Missing provider id\"}";
            }

            if (!_providers.TryGetValue(id, out var provider))
            {
                return "{\"error\":\"Provider not found\"}";
            }

            provider.UpdateConfig(args);
            return "{\"success\":true}";
        }

        private string DismissCallout(Dictionary<string, string> args)
        {
            // Callouts are already dequeued when read, so this is a no-op for now
            return "{\"success\":true}";
        }

        private string GetAnalysisJson(Dictionary<string, string> args)
        {
            var domain = args.TryGetValue("domain", out var d) ? d : null;

            var providers = domain != null
                ? _providers.Values.Where(p => p.Domain == domain)
                : _providers.Values;

            var results = new List<string>();
            foreach (var provider in providers.Where(p => p.Enabled))
            {
                var task = provider.AnalyzeAsync();
                task.Wait(5000);
                if (task.IsCompleted)
                {
                    results.AddRange(task.Result.Select(r => r.ToJson()));
                }
            }

            return "[" + string.Join(",", results) + "]";
        }

        private string TriggerAnalysis(Dictionary<string, string> args)
        {
            Task.Run(() => RunAnalysisAsync());
            return "{\"success\":true,\"message\":\"Analysis triggered\"}";
        }

        private string HandleEventPush(Dictionary<string, string> args)
        {
            if (!args.TryGetValue("type", out var typeStr) || !Enum.TryParse<GameEventType>(typeStr, true, out var type))
            {
                return "{\"error\":\"Invalid or missing event type\"}";
            }

            var evt = new GameEvent
            {
                Type = type,
                Source = args.TryGetValue("source", out var src) ? src : "api",
                Data = args.Where(a => a.Key != "type" && a.Key != "source")
                          .ToDictionary(a => a.Key, a => (object)a.Value)
            };

            PushEvent(evt);
            return "{\"success\":true}";
        }

        private string HandleAssistantAsk(Dictionary<string, string> args)
        {
            if (!args.TryGetValue("question", out var question))
            {
                return "{\"error\":\"Missing question\"}";
            }

            var assistant = _providers.Values.OfType<AssistantProvider>().FirstOrDefault();
            if (assistant == null)
            {
                return "{\"error\":\"Assistant not available\"}";
            }

            // Try quick calculation first
            var calcResult = assistant.Calculate(question);
            if (!string.IsNullOrEmpty(calcResult))
            {
                return $"{{\"answer\":\"{Escape(calcResult)}\",\"source\":\"calculation\"}}";
            }

            // Fall back to LLM
            var includeContext = !args.TryGetValue("noContext", out var nc) || nc != "true";
            var task = assistant.AskAsync(question, includeContext);
            task.Wait(30000);

            if (task.IsCompleted)
            {
                return $"{{\"answer\":\"{Escape(task.Result)}\",\"source\":\"llm\"}}";
            }

            return "{\"error\":\"Request timed out\"}";
        }

        private string GetAssistantHistoryJson()
        {
            var assistant = _providers.Values.OfType<AssistantProvider>().FirstOrDefault();
            if (assistant == null)
            {
                return "[]";
            }

            var history = assistant.GetHistory().Select(m =>
                $"{{\"role\":\"{m.Role}\",\"content\":\"{Escape(m.Content)}\",\"timestamp\":\"{m.Timestamp:O}\"}}");
            return "[" + string.Join(",", history) + "]";
        }

        private string CalculateImplantReqs(Dictionary<string, string> args)
        {
            if (!args.TryGetValue("ql", out var qlStr) || !int.TryParse(qlStr, out var ql))
            {
                return "{\"error\":\"Missing or invalid QL parameter\"}";
            }

            var slot = args.TryGetValue("slot", out var s) ? s : "head";

            var treatment = BuildAnalyzer.GetTreatmentForQL(ql);
            var ability = BuildAnalyzer.GetAbilityForSlot(slot, ql);

            return $"{{\"ql\":{ql},\"slot\":\"{slot}\",\"treatmentRequired\":{treatment},\"abilityRequired\":{ability}}}";
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _analysisTimer?.Stop();
            _analysisTimer?.Dispose();

            LLM?.Dispose();
            Context?.Dispose();

            _modules.Clear();
            _providers.Clear();
            _endpoints.Clear();
        }
    }
}
