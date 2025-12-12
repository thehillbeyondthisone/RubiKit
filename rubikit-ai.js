/**
 * RubiKit AI Service
 * Integrates LLM capabilities with game state for contextual AI assistance
 */

class RubiKitAI {
  constructor(config = {}) {
    this.config = {
      llmEndpoint: config.llmEndpoint || 'http://192.168.56.1:1234',
      apiEndpoint: config.apiEndpoint || 'http://127.0.0.1:8777',
      model: config.model || 'local-model',
      temperature: config.temperature || 0.7,
      maxTokens: config.maxTokens || 150,
      enabled: config.enabled !== false,
      debug: config.debug || false
    };

    this.gameState = {};
    this.context = [];
    this.subscribers = new Map();
    this.calloutHandlers = [];
    this.eventSource = null;
    this.updateInterval = null;

    this.log('RubiKit AI Service initialized', this.config);
  }

  // ============================================
  // Initialization & Connection
  // ============================================

  async initialize() {
    if (!this.config.enabled) {
      this.log('AI service disabled');
      return;
    }

    try {
      // Connect to game state stream
      await this.connectToGameState();

      // Start context updates
      this.startContextUpdates();

      // Test LLM connection
      await this.testLLMConnection();

      this.log('AI service fully initialized');
      return true;
    } catch (error) {
      console.error('[RubiKit AI] Initialization failed:', error);
      return false;
    }
  }

  async testLLMConnection() {
    try {
      const response = await this.queryLLM('Hello, are you online?', {
        max_tokens: 10,
        system: 'You are a helpful assistant. Respond with "Yes, I am online."'
      });
      this.log('LLM connection test:', response);
      return true;
    } catch (error) {
      console.warn('[RubiKit AI] LLM not available:', error.message);
      return false;
    }
  }

  // ============================================
  // Game State Management
  // ============================================

  async connectToGameState() {
    // Connect to SSE stream for real-time updates
    this.eventSource = new EventSource(`${this.config.apiEndpoint}/events`);

    this.eventSource.addEventListener('message', (event) => {
      try {
        const data = JSON.parse(event.data);
        this.updateGameState(data);
      } catch (error) {
        console.error('[RubiKit AI] Failed to parse game state:', error);
      }
    });

    this.eventSource.addEventListener('error', (error) => {
      console.error('[RubiKit AI] EventSource error:', error);
    });

    // Also poll for full state
    this.updateInterval = setInterval(() => this.fetchGameState(), 1000);
  }

  async fetchGameState() {
    try {
      const response = await fetch(`${this.config.apiEndpoint}/api/state`);
      if (response.ok) {
        const data = await response.json();
        this.updateGameState(data);
      }
    } catch (error) {
      // Silent fail for polling
    }
  }

  updateGameState(newState) {
    const oldState = { ...this.gameState };
    this.gameState = { ...this.gameState, ...newState };

    // Detect significant changes
    this.detectSignificantChanges(oldState, this.gameState);

    // Notify subscribers
    this.notifySubscribers('stateUpdate', this.gameState);
  }

  detectSignificantChanges(oldState, newState) {
    const changes = [];

    // Health threshold
    if (oldState.hp && newState.hp) {
      const oldPct = oldState.hp.pct || 0;
      const newPct = newState.hp.pct || 0;

      if (oldPct > 30 && newPct <= 30) {
        changes.push({ type: 'health_low', severity: 'warning', data: newState.hp });
      }
      if (oldPct > 50 && newPct <= 50) {
        changes.push({ type: 'health_medium', severity: 'info', data: newState.hp });
      }
    }

    // Nano threshold
    if (oldState.nano && newState.nano) {
      const oldPct = oldState.nano.pct || 0;
      const newPct = newState.nano.pct || 0;

      if (oldPct > 20 && newPct <= 20) {
        changes.push({ type: 'nano_low', severity: 'warning', data: newState.nano });
      }
    }

    // Stat changes
    if (oldState.core && newState.core) {
      for (const [key, value] of Object.entries(newState.core)) {
        if (oldState.core[key] !== value) {
          changes.push({
            type: 'stat_change',
            severity: 'info',
            data: { stat: key, old: oldState.core[key], new: value }
          });
        }
      }
    }

    // Process changes
    if (changes.length > 0) {
      this.processChanges(changes);
    }
  }

  async processChanges(changes) {
    for (const change of changes) {
      this.notifySubscribers('change', change);

      // Auto-generate AI callouts for significant changes
      if (this.config.enabled && change.severity !== 'info') {
        await this.generateCallout(change);
      }
    }
  }

  // ============================================
  // Context Building
  // ============================================

  startContextUpdates() {
    setInterval(() => this.updateContext(), 5000);
  }

  updateContext() {
    const ctx = this.buildContext();
    this.context = ctx;
    this.notifySubscribers('contextUpdate', ctx);
  }

  buildContext() {
    const context = [];

    // Current vitals
    if (this.gameState.hp) {
      context.push(`HP: ${this.gameState.hp.now}/${this.gameState.hp.max} (${this.gameState.hp.pct}%)`);
    }
    if (this.gameState.nano) {
      context.push(`Nano: ${this.gameState.nano.now}/${this.gameState.nano.max} (${this.gameState.nano.pct}%)`);
    }

    // Core stats
    if (this.gameState.core) {
      context.push(`AAO: ${this.gameState.core.AddAllOff}, AAD: ${this.gameState.core.AddAllDef}`);
      context.push(`Crit: ${this.gameState.core.CriticalIncrease}, XP: ${this.gameState.core.XPModifier}%`);
    }

    // Damage modifiers
    if (this.gameState.dmg) {
      const dmgMods = Object.entries(this.gameState.dmg)
        .map(([k, v]) => `${k.replace('DamageModifier', '')}: ${v}`)
        .join(', ');
      context.push(`Damage: ${dmgMods}`);
    }

    // Armor classes
    if (this.gameState.ac) {
      const acValues = Object.entries(this.gameState.ac)
        .map(([k, v]) => `${k.replace('AC', '')}: ${v}`)
        .join(', ');
      context.push(`AC: ${acValues}`);
    }

    return context;
  }

  getContextString() {
    return this.context.join('\n');
  }

  // ============================================
  // LLM Integration
  // ============================================

  async queryLLM(prompt, options = {}) {
    if (!this.config.enabled) {
      throw new Error('AI service is disabled');
    }

    const payload = {
      model: options.model || this.config.model,
      messages: [
        {
          role: 'system',
          content: options.system || this.getDefaultSystemPrompt()
        },
        {
          role: 'user',
          content: prompt
        }
      ],
      temperature: options.temperature || this.config.temperature,
      max_tokens: options.max_tokens || this.config.maxTokens,
      stream: false
    };

    try {
      const response = await fetch(`${this.config.llmEndpoint}/v1/chat/completions`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        throw new Error(`LLM request failed: ${response.status} ${response.statusText}`);
      }

      const data = await response.json();
      const message = data.choices?.[0]?.message?.content || '';

      this.log('LLM response:', message);
      return message.trim();
    } catch (error) {
      console.error('[RubiKit AI] LLM query failed:', error);
      throw error;
    }
  }

  getDefaultSystemPrompt() {
    return `You are an AI assistant for Anarchy Online, a sci-fi MMORPG. You analyze game stats and provide tactical advice.
You have access to the player's current stats and can suggest optimal strategies.
Be concise, tactical, and helpful. Focus on actionable advice.
Current game state:\n${this.getContextString()}`;
  }

  async generateCallout(change) {
    try {
      let prompt = '';

      switch (change.type) {
        case 'health_low':
          prompt = `My health just dropped to ${change.data.pct}%. What should I do?`;
          break;
        case 'nano_low':
          prompt = `My nano just dropped to ${change.data.pct}%. What should I do?`;
          break;
        case 'stat_change':
          prompt = `My ${change.data.stat} changed from ${change.data.old} to ${change.data.new}. What does this mean?`;
          break;
        default:
          return;
      }

      const response = await this.queryLLM(prompt, { max_tokens: 100 });

      const callout = {
        type: change.type,
        severity: change.severity,
        message: response,
        timestamp: Date.now(),
        change: change
      };

      this.triggerCallout(callout);
    } catch (error) {
      this.log('Failed to generate callout:', error.message);
    }
  }

  // ============================================
  // AI Analysis APIs
  // ============================================

  async analyzeStats(stats) {
    const prompt = `Analyze these stats and suggest improvements:\n${JSON.stringify(stats, null, 2)}`;
    return await this.queryLLM(prompt, { max_tokens: 200 });
  }

  async analyzeCombat(combatLog) {
    const prompt = `Analyze this combat log and suggest tactics:\n${combatLog}`;
    return await this.queryLLM(prompt, { max_tokens: 200 });
  }

  async suggestLoadout(profession, level) {
    const prompt = `Suggest an optimal equipment loadout for a level ${level} ${profession}.`;
    return await this.queryLLM(prompt, { max_tokens: 250 });
  }

  async explainStat(statName, currentValue) {
    const prompt = `Explain what ${statName} (current value: ${currentValue}) does in Anarchy Online and how to improve it.`;
    return await this.queryLLM(prompt, { max_tokens: 150 });
  }

  async suggestArmorType(damageType) {
    const prompt = `I'm taking ${damageType} damage. Which armor class should I prioritize?`;
    return await this.queryLLM(prompt, { max_tokens: 100 });
  }

  // ============================================
  // Pub/Sub System
  // ============================================

  subscribe(eventType, handler) {
    if (!this.subscribers.has(eventType)) {
      this.subscribers.set(eventType, []);
    }
    this.subscribers.get(eventType).push(handler);

    // Return unsubscribe function
    return () => {
      const handlers = this.subscribers.get(eventType);
      const index = handlers.indexOf(handler);
      if (index > -1) {
        handlers.splice(index, 1);
      }
    };
  }

  notifySubscribers(eventType, data) {
    const handlers = this.subscribers.get(eventType) || [];
    handlers.forEach(handler => {
      try {
        handler(data);
      } catch (error) {
        console.error(`[RubiKit AI] Subscriber error for ${eventType}:`, error);
      }
    });
  }

  // ============================================
  // Callout System
  // ============================================

  registerCalloutHandler(handler) {
    this.calloutHandlers.push(handler);
  }

  triggerCallout(callout) {
    this.log('Callout triggered:', callout);
    this.calloutHandlers.forEach(handler => {
      try {
        handler(callout);
      } catch (error) {
        console.error('[RubiKit AI] Callout handler error:', error);
      }
    });
  }

  // ============================================
  // Module Integration
  // ============================================

  getAPI() {
    return {
      // State access
      getGameState: () => ({ ...this.gameState }),
      getContext: () => [...this.context],
      subscribe: (event, handler) => this.subscribe(event, handler),

      // LLM queries
      query: (prompt, options) => this.queryLLM(prompt, options),
      analyzeStats: (stats) => this.analyzeStats(stats),
      analyzeCombat: (log) => this.analyzeCombat(log),
      suggestLoadout: (prof, lvl) => this.suggestLoadout(prof, lvl),
      explainStat: (name, val) => this.explainStat(name, val),
      suggestArmorType: (dmg) => this.suggestArmorType(dmg),

      // Callouts
      onCallout: (handler) => this.registerCalloutHandler(handler),

      // Config
      isEnabled: () => this.config.enabled,
      getConfig: () => ({ ...this.config })
    };
  }

  // ============================================
  // Utilities
  // ============================================

  log(...args) {
    if (this.config.debug) {
      console.log('[RubiKit AI]', ...args);
    }
  }

  disconnect() {
    if (this.eventSource) {
      this.eventSource.close();
      this.eventSource = null;
    }
    if (this.updateInterval) {
      clearInterval(this.updateInterval);
      this.updateInterval = null;
    }
  }
}

// Global instance
window.RubiKitAI = RubiKitAI;

// Auto-initialize if RubiKit is present
if (window.RubiKit) {
  window.rubiAI = new RubiKitAI({
    debug: true,
    enabled: true
  });

  // Initialize when page loads
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      window.rubiAI.initialize();
    });
  } else {
    window.rubiAI.initialize();
  }
}
