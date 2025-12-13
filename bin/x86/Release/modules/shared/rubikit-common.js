/**
 * RubiKit Common Utilities
 * Shared functionality across all modules
 */

const RubiKit = (function() {
  'use strict';

  const API_BASE = 'http://127.0.0.1:8777';

  // ===== Theme Management =====
  const ThemeManager = {
    themes: ['theme-inferno', 'theme-nixie', 'theme-dark', 'theme-light', 'theme-monokai'],
    fonts: ['font-sci-fi', 'font-mono', 'font-tech', 'font-aldrich', 'font-vt323'],

    load() {
      const theme = localStorage.getItem('rk-theme') || '';
      const font = localStorage.getItem('rk-font') || '';
      if (theme) document.body.classList.add(theme);
      if (font) document.body.classList.add(font);
      return { theme, font };
    },

    setTheme(theme) {
      this.themes.forEach(t => document.body.classList.remove(t));
      if (theme) document.body.classList.add(theme);
      localStorage.setItem('rk-theme', theme);
    },

    setFont(font) {
      this.fonts.forEach(f => document.body.classList.remove(f));
      if (font) document.body.classList.add(font);
      localStorage.setItem('rk-font', font);
    }
  };

  // ===== API Client =====
  const API = {
    async get(path) {
      const res = await fetch(`${API_BASE}${path}`, { mode: 'cors' });
      if (!res.ok) throw new Error(`API error: ${res.status}`);
      return res.json();
    },

    async post(path, params = {}) {
      const queryString = new URLSearchParams(params).toString();
      const url = queryString ? `${API_BASE}${path}?${queryString}` : `${API_BASE}${path}`;
      const res = await fetch(url, { method: 'POST', mode: 'cors' });
      if (!res.ok) throw new Error(`API error: ${res.status}`);
      return res.json();
    },

    async health() {
      try {
        const res = await fetch(`${API_BASE}/health`, { mode: 'cors' });
        return res.ok;
      } catch {
        return false;
      }
    },

    async llmStatus() {
      try {
        return await this.get('/api/llm/status');
      } catch {
        return { enabled: false };
      }
    },

    async askAssistant(question) {
      return this.post('/api/assistant/ask', { question });
    },

    async getContext(domain) {
      return this.get(`/api/context?domain=${domain}`);
    },

    async getProviderStatus(providerId) {
      return this.get(`/api/providers/${providerId}`);
    },

    async getProviderContext(providerId) {
      return this.get(`/api/providers/${providerId}/context`);
    },

    async triggerAnalysis(domain) {
      return this.post('/api/analysis/run', { domain });
    },

    async getAnalysis(domain) {
      return this.get(`/api/analysis?domain=${domain}`);
    },

    async getCallouts() {
      return this.get('/api/callouts');
    },

    async pushEvent(type, data = {}) {
      return this.post('/api/events/push', { type, ...data });
    }
  };

  // ===== Floating Assistant =====
  const Assistant = {
    panel: null,
    messages: null,
    input: null,
    isOpen: false,

    init() {
      this.injectHTML();
      this.bindEvents();
    },

    injectHTML() {
      const html = `
        <button id="rk-assistant-toggle" class="rk-assistant-toggle" title="RubiKit Assistant">
          <svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
            <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"/>
          </svg>
        </button>
        <div id="rk-assistant-panel" class="rk-assistant-panel">
          <div class="rk-assistant-header">
            <h3>RubiKit Assistant</h3>
            <span class="rk-llm-status">
              <span id="rk-llm-dot" class="rk-status-dot"></span>
              <span id="rk-llm-text">LLM</span>
            </span>
          </div>
          <div class="rk-quick-actions">
            <button class="rk-quick-action" data-prompt="What implants can I equip?">Implants</button>
            <button class="rk-quick-action" data-prompt="Analyze my build">Build</button>
            <button class="rk-quick-action" data-prompt="Damage tips">Damage</button>
          </div>
          <div id="rk-assistant-messages" class="rk-assistant-messages rk-scroll">
            <div class="rk-message rk-system">Ask me anything about AO.</div>
          </div>
          <div class="rk-assistant-input">
            <input type="text" id="rk-assistant-input" placeholder="Ask RubiKit..." autocomplete="off">
            <button id="rk-assistant-send">Send</button>
          </div>
        </div>
      `;

      const style = `
        <style>
          .rk-assistant-toggle {
            position: fixed;
            bottom: 1.5rem;
            right: 1.5rem;
            z-index: 1000;
            width: 56px;
            height: 56px;
            background: linear-gradient(135deg, var(--rk-accent) 0%, var(--rk-accent-secondary) 100%);
            border: none;
            border-radius: 50%;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            box-shadow: 0 4px 20px var(--rk-accent-glow);
            transition: all 0.2s ease;
          }
          .rk-assistant-toggle:hover {
            transform: scale(1.1);
            box-shadow: 0 6px 28px var(--rk-accent-glow);
          }
          .rk-assistant-toggle svg {
            width: 28px;
            height: 28px;
            fill: white;
          }
          .rk-assistant-panel {
            position: fixed;
            bottom: 88px;
            right: 1.5rem;
            z-index: 999;
            width: 380px;
            max-height: 520px;
            background: var(--rk-bg-secondary);
            border: 1px solid var(--rk-border);
            border-radius: 12px;
            box-shadow: 0 8px 40px rgba(0,0,0,0.4);
            display: none;
            flex-direction: column;
            overflow: hidden;
            animation: rk-slide-up 0.25s ease-out;
          }
          .rk-assistant-panel.open { display: flex; }
          .rk-assistant-header {
            padding: 1rem;
            background: var(--rk-bg-card);
            border-bottom: 1px solid var(--rk-border);
            display: flex;
            align-items: center;
            gap: 0.5rem;
          }
          .rk-assistant-header h3 {
            font-size: 0.9rem;
            font-weight: 600;
            margin: 0;
            flex: 1;
          }
          .rk-llm-status {
            font-size: 0.7rem;
            color: var(--rk-text-muted);
            display: flex;
            align-items: center;
            gap: 4px;
          }
          .rk-assistant-messages {
            flex: 1;
            overflow-y: auto;
            padding: 1rem;
            display: flex;
            flex-direction: column;
            gap: 0.5rem;
            min-height: 280px;
          }
          .rk-message {
            max-width: 85%;
            padding: 0.5rem 0.75rem;
            border-radius: 8px;
            font-size: 0.85rem;
            line-height: 1.5;
            animation: rk-fade-in 0.2s ease-out;
          }
          .rk-message.rk-user {
            align-self: flex-end;
            background: var(--rk-accent);
            color: var(--rk-bg-primary);
          }
          .rk-message.rk-assistant {
            align-self: flex-start;
            background: var(--rk-bg-card);
            color: var(--rk-text-primary);
            border: 1px solid var(--rk-border);
          }
          .rk-message.rk-system {
            align-self: center;
            background: transparent;
            color: var(--rk-text-muted);
            font-size: 0.75rem;
            font-style: italic;
          }
          .rk-assistant-input {
            padding: 1rem;
            background: var(--rk-bg-card);
            border-top: 1px solid var(--rk-border);
            display: flex;
            gap: 0.5rem;
          }
          .rk-assistant-input input {
            flex: 1;
            font-family: var(--rk-font-ui);
            font-size: 0.85rem;
            background: var(--rk-bg-primary);
            border: 1px solid var(--rk-border);
            color: var(--rk-text-primary);
            padding: 0.5rem 0.75rem;
            border-radius: 8px;
            outline: none;
          }
          .rk-assistant-input input:focus {
            border-color: var(--rk-accent);
            box-shadow: 0 0 0 3px var(--rk-ring);
          }
          .rk-assistant-input button {
            font-family: var(--rk-font-ui);
            font-size: 0.85rem;
            font-weight: 600;
            background: var(--rk-accent);
            border: none;
            color: var(--rk-bg-primary);
            padding: 0.5rem 1rem;
            border-radius: 8px;
            cursor: pointer;
          }
          .rk-assistant-input button:hover {
            background: var(--rk-accent-secondary);
          }
          .rk-quick-actions {
            padding: 0.5rem 1rem;
            display: flex;
            gap: 0.25rem;
            flex-wrap: wrap;
            border-bottom: 1px solid var(--rk-border);
          }
          .rk-quick-action {
            font-size: 0.7rem;
            padding: 0.25rem 0.5rem;
            background: var(--rk-bg-interactive);
            border: 1px solid var(--rk-border);
            color: var(--rk-text-secondary);
            border-radius: 12px;
            cursor: pointer;
          }
          .rk-quick-action:hover {
            background: var(--rk-accent-glow);
            border-color: var(--rk-accent);
            color: var(--rk-text-primary);
          }
          @media (max-width: 768px) {
            .rk-assistant-panel {
              width: calc(100vw - 32px);
              right: 16px;
            }
          }
        </style>
      `;

      document.head.insertAdjacentHTML('beforeend', style);
      document.body.insertAdjacentHTML('beforeend', html);

      this.panel = document.getElementById('rk-assistant-panel');
      this.messages = document.getElementById('rk-assistant-messages');
      this.input = document.getElementById('rk-assistant-input');
    },

    bindEvents() {
      document.getElementById('rk-assistant-toggle').addEventListener('click', () => this.toggle());
      document.getElementById('rk-assistant-send').addEventListener('click', () => this.send());
      this.input.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') this.send();
      });

      document.querySelectorAll('.rk-quick-action').forEach(btn => {
        btn.addEventListener('click', () => {
          this.input.value = btn.dataset.prompt;
          this.send();
        });
      });

      this.checkLLMStatus();
    },

    toggle() {
      this.isOpen = !this.isOpen;
      this.panel.classList.toggle('open', this.isOpen);
      if (this.isOpen) this.input.focus();
    },

    addMessage(text, type = 'rk-assistant') {
      const msg = document.createElement('div');
      msg.className = `rk-message ${type}`;
      msg.textContent = text;
      this.messages.appendChild(msg);
      this.messages.scrollTop = this.messages.scrollHeight;
    },

    async send() {
      const question = this.input.value.trim();
      if (!question) return;

      this.addMessage(question, 'rk-user');
      this.input.value = '';

      try {
        const data = await API.askAssistant(question);
        this.addMessage(data.answer || 'No response.', 'rk-assistant');
      } catch (e) {
        this.addMessage('Error: Assistant offline.', 'rk-system');
      }
    },

    async checkLLMStatus() {
      const dot = document.getElementById('rk-llm-dot');
      const text = document.getElementById('rk-llm-text');
      const status = await API.llmStatus();
      if (status.enabled) {
        dot.classList.add('ok');
        text.textContent = status.model || 'Ready';
      } else {
        text.textContent = 'Offline';
      }
    }
  };

  // ===== Status Bar Component =====
  const StatusBar = {
    dot: null,
    text: null,

    init(dotId = 'status-dot', textId = 'status-text') {
      this.dot = document.getElementById(dotId);
      this.text = document.getElementById(textId);
      this.check();
      setInterval(() => this.check(), 30000);
    },

    async check() {
      const connected = await API.health();
      if (this.dot && this.text) {
        if (connected) {
          this.dot.classList.add('ok');
          this.dot.classList.remove('error');
          this.text.textContent = 'Connected';
        } else {
          this.dot.classList.remove('ok');
          this.dot.classList.add('error');
          this.text.textContent = 'Disconnected';
        }
      }
      return connected;
    }
  };

  // ===== Callout System =====
  const Callouts = {
    container: null,

    init() {
      this.container = document.createElement('div');
      this.container.className = 'rk-callouts';
      this.container.style.cssText = `
        position: fixed;
        top: 1rem;
        right: 1rem;
        z-index: 1001;
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        max-width: 320px;
      `;
      document.body.appendChild(this.container);
      this.poll();
    },

    show(text, type = 'info', duration = 5000) {
      const callout = document.createElement('div');
      callout.className = `rk-callout rk-callout-${type}`;
      callout.style.cssText = `
        padding: 0.75rem 1rem;
        background: var(--rk-bg-card);
        border: 1px solid var(--rk-border);
        border-left: 3px solid var(--rk-${type === 'info' ? 'accent' : type === 'success' ? 'success' : type === 'warning' ? 'warning' : 'error'});
        border-radius: 8px;
        font-size: 0.85rem;
        color: var(--rk-text-primary);
        box-shadow: 0 4px 12px rgba(0,0,0,0.3);
        animation: rk-slide-up 0.3s ease-out;
      `;
      callout.textContent = text;
      this.container.appendChild(callout);

      setTimeout(() => {
        callout.style.opacity = '0';
        callout.style.transform = 'translateX(100%)';
        callout.style.transition = 'all 0.3s ease';
        setTimeout(() => callout.remove(), 300);
      }, duration);
    },

    async poll() {
      try {
        const callouts = await API.getCallouts();
        callouts.forEach(c => this.show(c.text, c.type?.toLowerCase() || 'info', c.durationMs || 5000));
      } catch {}
      setTimeout(() => this.poll(), 5000);
    }
  };

  // ===== Module Page Setup =====
  function initModule(options = {}) {
    ThemeManager.load();

    if (options.assistant !== false) {
      Assistant.init();
    }

    if (options.status !== false) {
      StatusBar.init(options.statusDotId, options.statusTextId);
    }

    if (options.callouts !== false) {
      Callouts.init();
    }

    // Setup theme selectors if present
    const themeSelect = document.getElementById('theme-select');
    const fontSelect = document.getElementById('font-select');

    if (themeSelect) {
      const { theme } = ThemeManager.load();
      themeSelect.value = theme;
      themeSelect.addEventListener('change', (e) => ThemeManager.setTheme(e.target.value));
    }

    if (fontSelect) {
      const { font } = ThemeManager.load();
      fontSelect.value = font;
      fontSelect.addEventListener('change', (e) => ThemeManager.setFont(e.target.value));
    }
  }

  // Public API
  return {
    API,
    ThemeManager,
    Assistant,
    StatusBar,
    Callouts,
    init: initModule
  };
})();
