// RubiKit LLM Module - Settings & Tools
(() => {
  // ===================================
  // CONFIG & STATE
  // ===================================
  const STORAGE_KEY = 'rubikit.llm';

  const defaultConfig = {
    provider: 'openai',
    endpoint: 'https://api.openai.com/v1',
    apiKey: '',
    model: '',
    temperature: 0.7,
    maxTokens: 4096,
    topP: 1.0,
    systemPrompt: 'You are a helpful assistant.',
    enabledTools: [],
    customTools: []
  };

  const PROVIDER_ENDPOINTS = {
    openai: 'https://api.openai.com/v1',
    anthropic: 'https://api.anthropic.com/v1',
    ollama: 'http://localhost:11434',
    openrouter: 'https://openrouter.ai/api/v1',
    lmstudio: 'http://localhost:1234/v1',
    custom: ''
  };

  // Built-in tools available
  const BUILTIN_TOOLS = [
    {
      id: 'web_search',
      name: 'Web Search',
      description: 'Search the web for current information',
      schema: {
        type: 'object',
        properties: {
          query: { type: 'string', description: 'Search query' }
        },
        required: ['query']
      }
    },
    {
      id: 'code_execute',
      name: 'Code Execution',
      description: 'Execute code snippets in a sandboxed environment',
      schema: {
        type: 'object',
        properties: {
          language: { type: 'string', description: 'Programming language' },
          code: { type: 'string', description: 'Code to execute' }
        },
        required: ['language', 'code']
      }
    },
    {
      id: 'file_read',
      name: 'File Read',
      description: 'Read contents of a file',
      schema: {
        type: 'object',
        properties: {
          path: { type: 'string', description: 'File path to read' }
        },
        required: ['path']
      }
    },
    {
      id: 'file_write',
      name: 'File Write',
      description: 'Write contents to a file',
      schema: {
        type: 'object',
        properties: {
          path: { type: 'string', description: 'File path to write' },
          content: { type: 'string', description: 'Content to write' }
        },
        required: ['path', 'content']
      }
    },
    {
      id: 'calculator',
      name: 'Calculator',
      description: 'Perform mathematical calculations',
      schema: {
        type: 'object',
        properties: {
          expression: { type: 'string', description: 'Math expression to evaluate' }
        },
        required: ['expression']
      }
    },
    {
      id: 'datetime',
      name: 'Date/Time',
      description: 'Get current date, time, or perform date calculations',
      schema: {
        type: 'object',
        properties: {
          action: { type: 'string', enum: ['now', 'format', 'diff'], description: 'Action to perform' },
          format: { type: 'string', description: 'Date format string' },
          date1: { type: 'string', description: 'First date for comparison' },
          date2: { type: 'string', description: 'Second date for comparison' }
        },
        required: ['action']
      }
    },
    {
      id: 'json_parse',
      name: 'JSON Parser',
      description: 'Parse and manipulate JSON data',
      schema: {
        type: 'object',
        properties: {
          data: { type: 'string', description: 'JSON string to parse' },
          path: { type: 'string', description: 'JSONPath expression to extract' }
        },
        required: ['data']
      }
    },
    {
      id: 'http_request',
      name: 'HTTP Request',
      description: 'Make HTTP requests to external APIs',
      schema: {
        type: 'object',
        properties: {
          url: { type: 'string', description: 'URL to request' },
          method: { type: 'string', enum: ['GET', 'POST', 'PUT', 'DELETE'], description: 'HTTP method' },
          headers: { type: 'object', description: 'Request headers' },
          body: { type: 'string', description: 'Request body' }
        },
        required: ['url']
      }
    }
  ];

  let config = { ...defaultConfig };
  let detectedModels = [];
  let isConnected = false;

  // ===================================
  // DOM ELEMENTS
  // ===================================
  const els = {
    provider: document.getElementById('provider'),
    endpoint: document.getElementById('endpoint'),
    endpointHint: document.getElementById('endpointHint'),
    apiKey: document.getElementById('apiKey'),
    toggleKey: document.getElementById('toggleKey'),
    testConnection: document.getElementById('testConnection'),
    detectModels: document.getElementById('detectModels'),
    model: document.getElementById('model'),
    refreshModels: document.getElementById('refreshModels'),
    modelInfo: document.getElementById('modelInfo'),
    contextLength: document.getElementById('contextLength'),
    modelType: document.getElementById('modelType'),
    detectedModels: document.getElementById('detectedModels'),
    modelCount: document.getElementById('modelCount'),
    modelList: document.getElementById('modelList'),
    temperature: document.getElementById('temperature'),
    tempValue: document.getElementById('tempValue'),
    maxTokens: document.getElementById('maxTokens'),
    topP: document.getElementById('topP'),
    topPValue: document.getElementById('topPValue'),
    systemPrompt: document.getElementById('systemPrompt'),
    enabledToolsCount: document.getElementById('enabledToolsCount'),
    toggleAllTools: document.getElementById('toggleAllTools'),
    toolsList: document.getElementById('toolsList'),
    addCustomTool: document.getElementById('addCustomTool'),
    chatMessages: document.getElementById('chatMessages'),
    chatInput: document.getElementById('chatInput'),
    sendMessage: document.getElementById('sendMessage'),
    saveSettings: document.getElementById('saveSettings'),
    resetSettings: document.getElementById('resetSettings'),
    exportSettings: document.getElementById('exportSettings'),
    importSettings: document.getElementById('importSettings'),
    statusDot: document.getElementById('statusDot'),
    statusText: document.getElementById('statusText'),
    toolModal: document.getElementById('toolModal'),
    toolName: document.getElementById('toolName'),
    toolDescription: document.getElementById('toolDescription'),
    toolSchema: document.getElementById('toolSchema'),
    cancelTool: document.getElementById('cancelTool'),
    saveTool: document.getElementById('saveTool')
  };

  // ===================================
  // UTILITY FUNCTIONS
  // ===================================
  function log(message, type = 'info') {
    console.log(`[LLM ${type}] ${message}`);
    addChatMessage(message, type === 'error' ? 'error' : 'system');
  }

  function setStatus(connected, text) {
    isConnected = connected;
    els.statusDot.className = 'dot ' + (connected ? 'ok' : '');
    els.statusText.textContent = text;
  }

  function addChatMessage(text, type = 'user') {
    const msg = document.createElement('div');
    msg.className = `message ${type}`;
    msg.textContent = text;
    els.chatMessages.appendChild(msg);
    els.chatMessages.scrollTop = els.chatMessages.scrollHeight;
  }

  // ===================================
  // STORAGE FUNCTIONS
  // ===================================
  function saveConfig() {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(config));
      log('Settings saved', 'success');
    } catch (err) {
      log(`Failed to save: ${err.message}`, 'error');
    }
  }

  function loadConfig() {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored) {
        config = { ...defaultConfig, ...JSON.parse(stored) };
      }
    } catch (err) {
      log(`Failed to load settings: ${err.message}`, 'error');
      config = { ...defaultConfig };
    }
  }

  // ===================================
  // MODEL DETECTION
  // ===================================
  async function detectModelsForProvider() {
    const provider = config.provider;
    const endpoint = config.endpoint;
    const apiKey = config.apiKey;

    setStatus(false, 'Detecting models...');
    detectedModels = [];

    try {
      let models = [];

      switch (provider) {
        case 'openai':
          models = await detectOpenAIModels(endpoint, apiKey);
          break;
        case 'anthropic':
          models = await detectAnthropicModels();
          break;
        case 'ollama':
          models = await detectOllamaModels(endpoint);
          break;
        case 'openrouter':
          models = await detectOpenRouterModels(endpoint, apiKey);
          break;
        case 'lmstudio':
          models = await detectLMStudioModels(endpoint);
          break;
        case 'custom':
          models = await detectCustomEndpointModels(endpoint, apiKey);
          break;
      }

      detectedModels = models;
      renderDetectedModels(models);
      populateModelSelect(models);

      if (models.length > 0) {
        setStatus(true, `Found ${models.length} models`);
      } else {
        setStatus(false, 'No models found');
      }
    } catch (err) {
      log(`Detection failed: ${err.message}`, 'error');
      setStatus(false, 'Detection failed');
    }
  }

  async function detectOpenAIModels(endpoint, apiKey) {
    const response = await fetch(`${endpoint}/models`, {
      headers: {
        'Authorization': `Bearer ${apiKey}`,
        'Content-Type': 'application/json'
      }
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    return (data.data || [])
      .filter(m => m.id.includes('gpt') || m.id.includes('text') || m.id.includes('claude'))
      .map(m => ({
        id: m.id,
        name: m.id,
        type: m.id.includes('gpt-4') ? 'chat' : 'completion',
        contextLength: getContextLength(m.id)
      }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  async function detectAnthropicModels() {
    // Anthropic doesn't have a models endpoint, return known models
    return [
      { id: 'claude-3-opus-20240229', name: 'Claude 3 Opus', type: 'chat', contextLength: 200000 },
      { id: 'claude-3-sonnet-20240229', name: 'Claude 3 Sonnet', type: 'chat', contextLength: 200000 },
      { id: 'claude-3-haiku-20240307', name: 'Claude 3 Haiku', type: 'chat', contextLength: 200000 },
      { id: 'claude-3-5-sonnet-20241022', name: 'Claude 3.5 Sonnet', type: 'chat', contextLength: 200000 },
      { id: 'claude-3-5-haiku-20241022', name: 'Claude 3.5 Haiku', type: 'chat', contextLength: 200000 }
    ];
  }

  async function detectOllamaModels(endpoint) {
    const response = await fetch(`${endpoint}/api/tags`);

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    return (data.models || []).map(m => ({
      id: m.name,
      name: m.name,
      type: 'chat',
      contextLength: m.details?.parameter_size || 4096,
      size: m.size
    }));
  }

  async function detectOpenRouterModels(endpoint, apiKey) {
    const response = await fetch(`${endpoint}/models`, {
      headers: {
        'Authorization': `Bearer ${apiKey}`,
        'Content-Type': 'application/json'
      }
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    return (data.data || []).map(m => ({
      id: m.id,
      name: m.name || m.id,
      type: 'chat',
      contextLength: m.context_length || 4096,
      pricing: m.pricing
    }));
  }

  async function detectLMStudioModels(endpoint) {
    const response = await fetch(`${endpoint}/models`);

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    return (data.data || []).map(m => ({
      id: m.id,
      name: m.id,
      type: 'chat',
      contextLength: 4096
    }));
  }

  async function detectCustomEndpointModels(endpoint, apiKey) {
    // Try OpenAI-compatible endpoint first
    try {
      const headers = { 'Content-Type': 'application/json' };
      if (apiKey) headers['Authorization'] = `Bearer ${apiKey}`;

      const response = await fetch(`${endpoint}/models`, { headers });

      if (response.ok) {
        const data = await response.json();
        if (data.data) {
          return data.data.map(m => ({
            id: m.id,
            name: m.id,
            type: 'chat',
            contextLength: 4096
          }));
        }
      }
    } catch (e) {
      // Try Ollama format
      try {
        const response = await fetch(`${endpoint}/api/tags`);
        if (response.ok) {
          const data = await response.json();
          return (data.models || []).map(m => ({
            id: m.name,
            name: m.name,
            type: 'chat',
            contextLength: 4096
          }));
        }
      } catch (e2) {
        // Fallback empty
      }
    }
    return [];
  }

  function getContextLength(modelId) {
    const contexts = {
      'gpt-4-turbo': 128000,
      'gpt-4-0125-preview': 128000,
      'gpt-4-1106-preview': 128000,
      'gpt-4': 8192,
      'gpt-4-32k': 32768,
      'gpt-3.5-turbo': 16385,
      'gpt-3.5-turbo-16k': 16385
    };
    for (const [key, ctx] of Object.entries(contexts)) {
      if (modelId.includes(key)) return ctx;
    }
    return 4096;
  }

  function renderDetectedModels(models) {
    els.detectedModels.classList.toggle('hidden', models.length === 0);
    els.modelCount.textContent = models.length;

    els.modelList.innerHTML = models.map(m => `
      <div class="model-item" data-id="${m.id}">
        <span class="model-name">${m.name}</span>
        <span class="model-meta">${m.contextLength ? m.contextLength.toLocaleString() + ' ctx' : ''}</span>
      </div>
    `).join('');

    els.modelList.querySelectorAll('.model-item').forEach(item => {
      item.addEventListener('click', () => {
        els.model.value = item.dataset.id;
        config.model = item.dataset.id;
        updateModelInfo(item.dataset.id);
      });
    });
  }

  function populateModelSelect(models) {
    els.model.innerHTML = '<option value="">-- Select a model --</option>' +
      models.map(m => `<option value="${m.id}">${m.name}</option>`).join('');

    if (config.model && models.find(m => m.id === config.model)) {
      els.model.value = config.model;
    }
  }

  function updateModelInfo(modelId) {
    const model = detectedModels.find(m => m.id === modelId);
    if (model) {
      els.modelInfo.classList.remove('hidden');
      els.contextLength.textContent = model.contextLength ? model.contextLength.toLocaleString() : '-';
      els.modelType.textContent = model.type || '-';
    } else {
      els.modelInfo.classList.add('hidden');
    }
  }

  // ===================================
  // CONNECTION TEST
  // ===================================
  async function testConnection() {
    const provider = config.provider;
    const endpoint = config.endpoint;
    const apiKey = config.apiKey;

    setStatus(false, 'Testing...');

    try {
      let success = false;

      switch (provider) {
        case 'openai':
        case 'openrouter':
        case 'custom':
          const resp = await fetch(`${endpoint}/models`, {
            headers: {
              'Authorization': `Bearer ${apiKey}`,
              'Content-Type': 'application/json'
            }
          });
          success = resp.ok;
          break;
        case 'anthropic':
          // Anthropic requires a message to test
          success = !!apiKey;
          break;
        case 'ollama':
        case 'lmstudio':
          const localResp = await fetch(`${endpoint}/api/tags`).catch(() =>
            fetch(`${endpoint}/models`)
          );
          success = localResp && localResp.ok;
          break;
      }

      if (success) {
        setStatus(true, 'Connected');
        log('Connection successful!', 'success');
      } else {
        setStatus(false, 'Connection failed');
        log('Connection failed', 'error');
      }
    } catch (err) {
      setStatus(false, 'Error');
      log(`Connection error: ${err.message}`, 'error');
    }
  }

  // ===================================
  // CHAT TEST
  // ===================================
  async function sendTestMessage() {
    const message = els.chatInput.value.trim();
    if (!message) return;

    addChatMessage(message, 'user');
    els.chatInput.value = '';

    if (!config.model) {
      addChatMessage('Please select a model first', 'error');
      return;
    }

    try {
      addChatMessage('Thinking...', 'system');

      const response = await callLLM(message);

      // Remove "Thinking..." message
      els.chatMessages.lastChild.remove();
      addChatMessage(response, 'assistant');
    } catch (err) {
      els.chatMessages.lastChild.remove();
      addChatMessage(`Error: ${err.message}`, 'error');
    }
  }

  async function callLLM(message) {
    const provider = config.provider;
    const endpoint = config.endpoint;
    const apiKey = config.apiKey;
    const model = config.model;

    const messages = [
      { role: 'system', content: config.systemPrompt },
      { role: 'user', content: message }
    ];

    // Build tools array if any are enabled
    const tools = getEnabledTools();

    let response;

    switch (provider) {
      case 'anthropic':
        response = await fetch(`${endpoint}/messages`, {
          method: 'POST',
          headers: {
            'x-api-key': apiKey,
            'anthropic-version': '2023-06-01',
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            model,
            max_tokens: config.maxTokens,
            temperature: config.temperature,
            system: config.systemPrompt,
            messages: [{ role: 'user', content: message }],
            ...(tools.length > 0 && { tools })
          })
        });
        const anthropicData = await response.json();
        if (anthropicData.error) throw new Error(anthropicData.error.message);
        return anthropicData.content[0].text;

      case 'ollama':
        response = await fetch(`${endpoint}/api/chat`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            model,
            messages,
            options: {
              temperature: config.temperature,
              num_predict: config.maxTokens
            }
          })
        });
        const ollamaData = await response.json();
        return ollamaData.message?.content || ollamaData.response;

      default:
        // OpenAI-compatible
        response = await fetch(`${endpoint}/chat/completions`, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${apiKey}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            model,
            messages,
            temperature: config.temperature,
            max_tokens: config.maxTokens,
            top_p: config.topP,
            ...(tools.length > 0 && { tools, tool_choice: 'auto' })
          })
        });
        const openaiData = await response.json();
        if (openaiData.error) throw new Error(openaiData.error.message);
        return openaiData.choices[0].message.content;
    }
  }

  function getEnabledTools() {
    const allTools = [...BUILTIN_TOOLS, ...config.customTools];
    return allTools
      .filter(t => config.enabledTools.includes(t.id))
      .map(t => ({
        type: 'function',
        function: {
          name: t.id,
          description: t.description,
          parameters: t.schema
        }
      }));
  }

  // ===================================
  // TOOLS MANAGEMENT
  // ===================================
  function renderTools() {
    const allTools = [...BUILTIN_TOOLS, ...config.customTools];

    els.toolsList.innerHTML = allTools.map(tool => `
      <div class="tool-item" data-id="${tool.id}">
        <label class="tool-toggle">
          <input type="checkbox" ${config.enabledTools.includes(tool.id) ? 'checked' : ''}>
          <span class="tool-name">${tool.name}</span>
        </label>
        <span class="tool-description">${tool.description}</span>
        ${config.customTools.find(t => t.id === tool.id) ?
          `<button class="btn-icon delete-tool" title="Delete">&#128465;</button>` : ''}
      </div>
    `).join('');

    // Attach event listeners
    els.toolsList.querySelectorAll('.tool-item').forEach(item => {
      const checkbox = item.querySelector('input[type="checkbox"]');
      checkbox.addEventListener('change', () => {
        const toolId = item.dataset.id;
        if (checkbox.checked) {
          if (!config.enabledTools.includes(toolId)) {
            config.enabledTools.push(toolId);
          }
        } else {
          config.enabledTools = config.enabledTools.filter(id => id !== toolId);
        }
        updateToolsCount();
      });

      const deleteBtn = item.querySelector('.delete-tool');
      if (deleteBtn) {
        deleteBtn.addEventListener('click', () => {
          config.customTools = config.customTools.filter(t => t.id !== item.dataset.id);
          config.enabledTools = config.enabledTools.filter(id => id !== item.dataset.id);
          renderTools();
        });
      }
    });

    updateToolsCount();
  }

  function updateToolsCount() {
    els.enabledToolsCount.textContent = config.enabledTools.length;
  }

  function showToolModal() {
    els.toolModal.classList.remove('hidden');
    els.toolName.value = '';
    els.toolDescription.value = '';
    els.toolSchema.value = '{\n  "type": "object",\n  "properties": {\n    "param1": {\n      "type": "string",\n      "description": "Description"\n    }\n  },\n  "required": ["param1"]\n}';
  }

  function hideToolModal() {
    els.toolModal.classList.add('hidden');
  }

  function addCustomTool() {
    const name = els.toolName.value.trim();
    const description = els.toolDescription.value.trim();
    let schema;

    try {
      schema = JSON.parse(els.toolSchema.value);
    } catch (err) {
      log('Invalid JSON schema', 'error');
      return;
    }

    if (!name) {
      log('Tool name is required', 'error');
      return;
    }

    const id = name.toLowerCase().replace(/\s+/g, '_');

    if (BUILTIN_TOOLS.find(t => t.id === id) || config.customTools.find(t => t.id === id)) {
      log('Tool with this name already exists', 'error');
      return;
    }

    config.customTools.push({ id, name, description, schema });
    hideToolModal();
    renderTools();
    log(`Added custom tool: ${name}`, 'success');
  }

  // ===================================
  // UI INITIALIZATION
  // ===================================
  function applyConfigToUI() {
    els.provider.value = config.provider;
    els.endpoint.value = config.endpoint;
    els.apiKey.value = config.apiKey;
    els.temperature.value = config.temperature;
    els.tempValue.textContent = config.temperature;
    els.maxTokens.value = config.maxTokens;
    els.topP.value = config.topP;
    els.topPValue.textContent = config.topP;
    els.systemPrompt.value = config.systemPrompt;

    updateEndpointHint();
    renderTools();
  }

  function updateEndpointHint() {
    const provider = config.provider;
    const defaultEndpoint = PROVIDER_ENDPOINTS[provider];

    if (provider === 'custom') {
      els.endpointHint.textContent = 'Enter your custom API endpoint';
    } else {
      els.endpointHint.textContent = `Default: ${defaultEndpoint}`;
    }
  }

  function initEventListeners() {
    // Provider change
    els.provider.addEventListener('change', () => {
      config.provider = els.provider.value;
      config.endpoint = PROVIDER_ENDPOINTS[config.provider] || config.endpoint;
      els.endpoint.value = config.endpoint;
      updateEndpointHint();
    });

    // Endpoint change
    els.endpoint.addEventListener('change', () => {
      config.endpoint = els.endpoint.value;
    });

    // API key
    els.apiKey.addEventListener('change', () => {
      config.apiKey = els.apiKey.value;
    });

    els.toggleKey.addEventListener('click', () => {
      els.apiKey.type = els.apiKey.type === 'password' ? 'text' : 'password';
    });

    // Model selection
    els.model.addEventListener('change', () => {
      config.model = els.model.value;
      updateModelInfo(config.model);
    });

    // Detection buttons
    els.testConnection.addEventListener('click', testConnection);
    els.detectModels.addEventListener('click', detectModelsForProvider);
    els.refreshModels.addEventListener('click', detectModelsForProvider);

    // Parameters
    els.temperature.addEventListener('input', () => {
      config.temperature = parseFloat(els.temperature.value);
      els.tempValue.textContent = config.temperature;
    });

    els.maxTokens.addEventListener('change', () => {
      config.maxTokens = parseInt(els.maxTokens.value);
    });

    els.topP.addEventListener('input', () => {
      config.topP = parseFloat(els.topP.value);
      els.topPValue.textContent = config.topP;
    });

    els.systemPrompt.addEventListener('change', () => {
      config.systemPrompt = els.systemPrompt.value;
    });

    // Tools
    els.toggleAllTools.addEventListener('click', () => {
      const allTools = [...BUILTIN_TOOLS, ...config.customTools];
      if (config.enabledTools.length === allTools.length) {
        config.enabledTools = [];
      } else {
        config.enabledTools = allTools.map(t => t.id);
      }
      renderTools();
    });

    els.addCustomTool.addEventListener('click', showToolModal);
    els.cancelTool.addEventListener('click', hideToolModal);
    els.saveTool.addEventListener('click', addCustomTool);

    // Chat
    els.sendMessage.addEventListener('click', sendTestMessage);
    els.chatInput.addEventListener('keypress', (e) => {
      if (e.key === 'Enter') sendTestMessage();
    });

    // Settings buttons
    els.saveSettings.addEventListener('click', saveConfig);

    els.resetSettings.addEventListener('click', () => {
      if (confirm('Reset all LLM settings to defaults?')) {
        config = { ...defaultConfig };
        applyConfigToUI();
        saveConfig();
      }
    });

    els.exportSettings.addEventListener('click', () => {
      const exportData = { ...config };
      delete exportData.apiKey; // Don't export API key
      const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'rubikit-llm-settings.json';
      a.click();
      URL.revokeObjectURL(url);
    });

    els.importSettings.addEventListener('click', () => {
      const input = document.createElement('input');
      input.type = 'file';
      input.accept = '.json';
      input.onchange = async (e) => {
        const file = e.target.files[0];
        if (file) {
          const text = await file.text();
          try {
            const imported = JSON.parse(text);
            config = { ...config, ...imported };
            applyConfigToUI();
            saveConfig();
            log('Settings imported successfully', 'success');
          } catch (err) {
            log(`Import failed: ${err.message}`, 'error');
          }
        }
      };
      input.click();
    });
  }

  // ===================================
  // INITIALIZATION
  // ===================================
  function init() {
    loadConfig();
    applyConfigToUI();
    initEventListeners();

    // Auto-detect if we have stored credentials
    if (config.apiKey || config.provider === 'ollama' || config.provider === 'lmstudio') {
      setTimeout(detectModelsForProvider, 500);
    }

    log('LLM module initialized');
  }

  init();
})();
