/**
 * RubiKit LLM Client - Frontend integration for LLM services
 * Provides API access to all LLM endpoints and real-time callouts
 */

(function(global) {
    'use strict';

    const DEFAULT_PORT = 8777;
    const API_BASE = `http://127.0.0.1:${DEFAULT_PORT}`;

    /**
     * RubiKit LLM Client
     */
    class RubiKitLLM {
        constructor(options = {}) {
            this.apiBase = options.apiBase || API_BASE;
            this.onCallout = options.onCallout || null;
            this.onAnalysis = options.onAnalysis || null;
            this.onStatusChange = options.onStatusChange || null;
            this.pollInterval = options.pollInterval || 2000;
            this._pollTimer = null;
            this._eventSource = null;
            this._connected = false;
        }

        // ============ Connection Management ============

        /**
         * Connect to RubiKit API with SSE for real-time updates
         */
        connect() {
            if (this._eventSource) {
                this._eventSource.close();
            }

            this._eventSource = new EventSource(`${this.apiBase}/events`);

            this._eventSource.onopen = () => {
                this._connected = true;
                this.onStatusChange?.('connected');
            };

            this._eventSource.onerror = () => {
                this._connected = false;
                this.onStatusChange?.('disconnected');
                // Fallback to polling
                this._startPolling();
            };

            this._eventSource.onmessage = (event) => {
                try {
                    const data = JSON.parse(event.data);
                    this._handleStateUpdate(data);
                } catch (e) {
                    console.error('[RubiKitLLM] Parse error:', e);
                }
            };
        }

        /**
         * Disconnect from RubiKit API
         */
        disconnect() {
            if (this._eventSource) {
                this._eventSource.close();
                this._eventSource = null;
            }
            this._stopPolling();
            this._connected = false;
        }

        _startPolling() {
            if (this._pollTimer) return;
            this._pollTimer = setInterval(() => this._poll(), this.pollInterval);
        }

        _stopPolling() {
            if (this._pollTimer) {
                clearInterval(this._pollTimer);
                this._pollTimer = null;
            }
        }

        async _poll() {
            try {
                const callouts = await this.getCallouts();
                callouts.forEach(c => this.onCallout?.(c));
            } catch (e) {
                // Ignore polling errors
            }
        }

        _handleStateUpdate(data) {
            // State updates are handled by NotumHUD
            // We focus on LLM-specific features here
        }

        // ============ LLM API Methods ============

        /**
         * Get LLM service status
         */
        async getStatus() {
            const res = await fetch(`${this.apiBase}/api/llm/status`);
            return res.json();
        }

        /**
         * Get LLM configuration
         */
        async getConfig() {
            const res = await fetch(`${this.apiBase}/api/llm/config`);
            return res.json();
        }

        /**
         * Update LLM configuration
         */
        async setConfig(config) {
            const params = new URLSearchParams(config);
            const res = await fetch(`${this.apiBase}/api/llm/config?${params}`, {
                method: 'POST'
            });
            return res.json();
        }

        /**
         * Get available LLM models
         */
        async getModels() {
            const res = await fetch(`${this.apiBase}/api/llm/models`);
            return res.json();
        }

        /**
         * Send a completion request to the LLM
         */
        async complete(prompt, options = {}) {
            const params = new URLSearchParams({
                prompt,
                domain: options.domain || 'core',
                ...(options.noContext && { noContext: 'true' })
            });

            const res = await fetch(`${this.apiBase}/api/llm/complete?${params}`, {
                method: 'POST'
            });
            return res.json();
        }

        /**
         * Ask the AI assistant a question
         */
        async ask(question, options = {}) {
            const params = new URLSearchParams({
                question,
                ...(options.noContext && { noContext: 'true' })
            });

            const res = await fetch(`${this.apiBase}/api/assistant/ask?${params}`, {
                method: 'POST'
            });
            return res.json();
        }

        /**
         * Get assistant chat history
         */
        async getHistory() {
            const res = await fetch(`${this.apiBase}/api/assistant/history`);
            return res.json();
        }

        // ============ Context API Methods ============

        /**
         * Get full context for a domain
         */
        async getContext(domain = 'core') {
            const res = await fetch(`${this.apiBase}/api/context?domain=${domain}`);
            return res.json();
        }

        /**
         * Get combat context
         */
        async getCombatContext() {
            const res = await fetch(`${this.apiBase}/api/context/combat`);
            return res.text();
        }

        /**
         * Get trading context
         */
        async getTradingContext() {
            const res = await fetch(`${this.apiBase}/api/context/trading`);
            return res.text();
        }

        /**
         * Get build context
         */
        async getBuildContext() {
            const res = await fetch(`${this.apiBase}/api/context/build`);
            return res.text();
        }

        // ============ Provider API Methods ============

        /**
         * Get all registered providers
         */
        async getProviders() {
            const res = await fetch(`${this.apiBase}/api/providers`);
            return res.json();
        }

        /**
         * Get a specific provider's config
         */
        async getProvider(id) {
            const res = await fetch(`${this.apiBase}/api/providers/${id}`);
            return res.json();
        }

        /**
         * Update a provider's config
         */
        async setProviderConfig(id, config) {
            const params = new URLSearchParams({ id, ...config });
            const res = await fetch(`${this.apiBase}/api/providers/config?${params}`, {
                method: 'POST'
            });
            return res.json();
        }

        // ============ Analysis API Methods ============

        /**
         * Get latest analysis results
         */
        async getAnalysis(domain = null) {
            const url = domain
                ? `${this.apiBase}/api/analysis?domain=${domain}`
                : `${this.apiBase}/api/analysis`;
            const res = await fetch(url);
            return res.json();
        }

        /**
         * Trigger an analysis run
         */
        async runAnalysis() {
            const res = await fetch(`${this.apiBase}/api/analysis/run`, {
                method: 'POST'
            });
            return res.json();
        }

        // ============ Callout API Methods ============

        /**
         * Get pending callouts
         */
        async getCallouts() {
            const res = await fetch(`${this.apiBase}/api/callouts`);
            return res.json();
        }

        /**
         * Dismiss a callout
         */
        async dismissCallout(id) {
            const res = await fetch(`${this.apiBase}/api/callouts/dismiss?id=${id}`, {
                method: 'POST'
            });
            return res.json();
        }

        // ============ Event API Methods ============

        /**
         * Get recent events
         */
        async getEvents(count = 20) {
            const res = await fetch(`${this.apiBase}/api/events?count=${count}`);
            return res.json();
        }

        /**
         * Push a game event
         */
        async pushEvent(type, data = {}) {
            const params = new URLSearchParams({ type, ...data });
            const res = await fetch(`${this.apiBase}/api/events/push?${params}`, {
                method: 'POST'
            });
            return res.json();
        }

        // ============ Calculation API Methods ============

        /**
         * Calculate implant requirements
         */
        async calcImplant(ql, slot = 'head') {
            const res = await fetch(`${this.apiBase}/api/calc/implant?ql=${ql}&slot=${slot}`);
            return res.json();
        }

        // ============ Module API Methods ============

        /**
         * Get registered modules
         */
        async getModules() {
            const res = await fetch(`${this.apiBase}/api/modules`);
            return res.json();
        }

        /**
         * Get all API endpoints
         */
        async getEndpoints() {
            const res = await fetch(`${this.apiBase}/api/endpoints`);
            return res.json();
        }

        /**
         * Get full API info
         */
        async getAPIInfo() {
            const res = await fetch(`${this.apiBase}/api`);
            return res.json();
        }
    }

    /**
     * LLM Callout Display Component
     */
    class CalloutDisplay {
        constructor(container, options = {}) {
            this.container = typeof container === 'string'
                ? document.querySelector(container)
                : container;
            this.maxCallouts = options.maxCallouts || 5;
            this.defaultDuration = options.defaultDuration || 5000;
            this.callouts = new Map();

            this._init();
        }

        _init() {
            if (!this.container) return;
            this.container.classList.add('rubikit-callouts');
        }

        /**
         * Show a callout
         */
        show(callout) {
            const el = document.createElement('div');
            el.className = `rubikit-callout rubikit-callout-${callout.type || 'info'}`;
            el.innerHTML = `
                <span class="callout-source">[${callout.source || 'RubiKit'}]</span>
                <span class="callout-text">${callout.text}</span>
                <button class="callout-dismiss">×</button>
            `;

            el.querySelector('.callout-dismiss').onclick = () => this.dismiss(callout.id);

            this.container.appendChild(el);
            this.callouts.set(callout.id, { el, callout });

            // Animate in
            requestAnimationFrame(() => el.classList.add('visible'));

            // Auto-dismiss
            if (!callout.pinned) {
                const duration = callout.durationMs || this.defaultDuration;
                setTimeout(() => this.dismiss(callout.id), duration);
            }

            // Trim if too many
            while (this.callouts.size > this.maxCallouts) {
                const oldest = this.callouts.keys().next().value;
                this.dismiss(oldest);
            }
        }

        /**
         * Dismiss a callout
         */
        dismiss(id) {
            const item = this.callouts.get(id);
            if (!item) return;

            item.el.classList.remove('visible');
            setTimeout(() => {
                item.el.remove();
                this.callouts.delete(id);
            }, 300);
        }

        /**
         * Clear all callouts
         */
        clear() {
            for (const id of this.callouts.keys()) {
                this.dismiss(id);
            }
        }
    }

    /**
     * LLM Chat Panel Component
     */
    class ChatPanel {
        constructor(container, llmClient) {
            this.container = typeof container === 'string'
                ? document.querySelector(container)
                : container;
            this.llm = llmClient;
            this.history = [];

            this._init();
        }

        _init() {
            if (!this.container) return;

            this.container.innerHTML = `
                <div class="rubikit-chat">
                    <div class="chat-header">
                        <span>RubiKit AI</span>
                        <span class="chat-status">●</span>
                    </div>
                    <div class="chat-messages"></div>
                    <div class="chat-input-area">
                        <input type="text" class="chat-input" placeholder="Ask anything...">
                        <button class="chat-send">Send</button>
                    </div>
                </div>
            `;

            this.messagesEl = this.container.querySelector('.chat-messages');
            this.inputEl = this.container.querySelector('.chat-input');
            this.sendBtn = this.container.querySelector('.chat-send');
            this.statusEl = this.container.querySelector('.chat-status');

            this.sendBtn.onclick = () => this._send();
            this.inputEl.onkeypress = (e) => {
                if (e.key === 'Enter') this._send();
            };

            this._loadHistory();
        }

        async _loadHistory() {
            try {
                const history = await this.llm.getHistory();
                this.history = history;
                history.forEach(msg => this._addMessage(msg.role, msg.content, false));
            } catch (e) {
                console.error('[ChatPanel] Failed to load history:', e);
            }
        }

        async _send() {
            const question = this.inputEl.value.trim();
            if (!question) return;

            this.inputEl.value = '';
            this._addMessage('user', question);

            try {
                this.statusEl.textContent = '...';
                const result = await this.llm.ask(question);
                this._addMessage('assistant', result.answer || result.error);
            } catch (e) {
                this._addMessage('error', 'Failed to get response');
            } finally {
                this.statusEl.textContent = '●';
            }
        }

        _addMessage(role, content, scroll = true) {
            const el = document.createElement('div');
            el.className = `chat-message chat-${role}`;
            el.textContent = content;
            this.messagesEl.appendChild(el);

            if (scroll) {
                this.messagesEl.scrollTop = this.messagesEl.scrollHeight;
            }
        }

        clear() {
            this.messagesEl.innerHTML = '';
            this.history = [];
        }
    }

    // Export to global
    global.RubiKitLLM = RubiKitLLM;
    global.CalloutDisplay = CalloutDisplay;
    global.ChatPanel = ChatPanel;

})(typeof window !== 'undefined' ? window : global);
