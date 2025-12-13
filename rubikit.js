// RubiKit OS - Module & Tab System with Command Support
(function() {
  const MODULES_PATH = 'modules/modules.json';
  const BOOT_PATH = 'C:\\Users\\Administrator\\source\\repos\\RubiKit\\index.html';

  const state = {
    modules: [],
    activeTab: null,
    iframes: new Map(),
    commandHistory: []
  };

  // Expose RubiKit API globally
  window.RubiKit = {
    registerModule(m) {
      if (!m || !m.id || !m.name) return;
      state.modules.push(m);
    },
    getActiveModule() {
      return state.activeTab;
    },
    switchTab(moduleId) {
      const mod = state.modules.find(m => m.id === moduleId);
      if (mod) showModule(mod);
    },
    executeCommand(cmd) {
      return handleCommand(cmd);
    },
    boot() {
      handleCommand('/boot');
    }
  };

  // ===================================
  // COMMAND SYSTEM
  // ===================================
  const COMMANDS = {
    '/boot': {
      description: 'Navigate to RubiKit boot location',
      handler: () => {
        window.location.href = BOOT_PATH;
        return `Booting to ${BOOT_PATH}...`;
      }
    },
    '/help': {
      description: 'Show available commands',
      handler: () => {
        const cmds = Object.entries(COMMANDS)
          .map(([cmd, info]) => `${cmd} - ${info.description}`)
          .join('\n');
        console.log('Available commands:\n' + cmds);
        return cmds;
      }
    },
    '/modules': {
      description: 'List loaded modules',
      handler: () => {
        const list = state.modules.map(m => `${m.id}: ${m.name}`).join('\n');
        console.log('Loaded modules:\n' + list);
        return list;
      }
    },
    '/switch': {
      description: 'Switch to a module by ID',
      handler: (args) => {
        const moduleId = args[0];
        const mod = state.modules.find(m => m.id === moduleId);
        if (mod) {
          showModule(mod);
          return `Switched to ${mod.name}`;
        }
        return `Module not found: ${moduleId}`;
      }
    },
    '/reload': {
      description: 'Reload all modules',
      handler: () => {
        location.reload();
        return 'Reloading...';
      }
    }
  };

  function handleCommand(input) {
    if (!input || !input.startsWith('/')) return null;

    const parts = input.trim().split(/\s+/);
    const cmd = parts[0].toLowerCase();
    const args = parts.slice(1);

    state.commandHistory.push(input);

    if (COMMANDS[cmd]) {
      return COMMANDS[cmd].handler(args);
    }

    return `Unknown command: ${cmd}. Type /help for available commands.`;
  }

  // ===================================
  // MODULE LOADING
  // ===================================
  async function loadModules() {
    try {
      const response = await fetch(MODULES_PATH);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);

      const modulesConfig = await response.json();

      for (const mod of modulesConfig) {
        state.modules.push({
          id: mod.id,
          name: mod.name,
          icon: mod.icon,
          path: mod.path,
          persistent: mod.persistent || false
        });
      }

      console.log(`[RubiKit] Loaded ${state.modules.length} modules`);
    } catch (err) {
      console.warn('[RubiKit] Failed to load modules.json:', err.message);
    }

    initUI();
  }

  // ===================================
  // UI INITIALIZATION
  // ===================================
  function initUI() {
    const tabsContainer = document.getElementById('rk-tabs');
    const viewContainer = document.getElementById('rk-view');

    if (!tabsContainer || !viewContainer) {
      console.error('[RubiKit] Missing #rk-tabs or #rk-view containers');
      return;
    }

    // Clear existing content
    tabsContainer.innerHTML = '';
    viewContainer.innerHTML = '';

    if (state.modules.length === 0) {
      viewContainer.innerHTML = '<div class="rk-card rk-empty">No modules loaded. Check modules/modules.json</div>';
      return;
    }

    // Create tab bar
    const tabBar = document.createElement('div');
    tabBar.className = 'rk-tab-bar';

    state.modules.forEach((mod, index) => {
      // Create tab button
      const tab = document.createElement('button');
      tab.className = 'rk-tab';
      tab.dataset.moduleId = mod.id;
      tab.innerHTML = `<span class="rk-tab-icon">${getIcon(mod.icon)}</span><span class="rk-tab-name">${mod.name}</span>`;
      tab.onclick = () => showModule(mod);
      tabBar.appendChild(tab);

      // Create iframe container for each module (hidden initially)
      const frameContainer = document.createElement('div');
      frameContainer.className = 'rk-frame-container';
      frameContainer.id = `frame-${mod.id}`;
      frameContainer.style.display = 'none';

      const iframe = document.createElement('iframe');
      iframe.className = 'rk-module-frame';
      iframe.src = mod.path;
      iframe.title = mod.name;
      iframe.setAttribute('loading', 'lazy');

      frameContainer.appendChild(iframe);
      viewContainer.appendChild(frameContainer);

      state.iframes.set(mod.id, { container: frameContainer, iframe, loaded: false });

      // Show first module by default
      if (index === 0) {
        showModule(mod);
      }
    });

    // Add command input
    const cmdInput = document.createElement('div');
    cmdInput.className = 'rk-cmd-container';
    cmdInput.innerHTML = `
      <input type="text" class="rk-cmd-input" placeholder="Type /help for commands..." id="rk-cmd">
    `;
    tabBar.appendChild(cmdInput);

    tabsContainer.appendChild(tabBar);

    // Command input handler
    const cmdField = document.getElementById('rk-cmd');
    cmdField.addEventListener('keypress', (e) => {
      if (e.key === 'Enter') {
        const result = handleCommand(cmdField.value);
        if (result) {
          console.log('[RubiKit]', result);
          // Could show a toast notification here
        }
        cmdField.value = '';
      }
    });
  }

  function showModule(mod) {
    // Update tab states
    document.querySelectorAll('.rk-tab').forEach(tab => {
      tab.classList.toggle('active', tab.dataset.moduleId === mod.id);
    });

    // Hide all iframe containers, show the selected one
    state.iframes.forEach((data, id) => {
      data.container.style.display = id === mod.id ? 'block' : 'none';
    });

    state.activeTab = mod.id;
    console.log(`[RubiKit] Switched to: ${mod.name}`);
  }

  function getIcon(iconName) {
    const icons = {
      chart: '📊',
      brain: '🧠',
      settings: '⚙️',
      home: '🏠',
      terminal: '💻',
      file: '📁',
      search: '🔍',
      user: '👤',
      default: '📦'
    };
    return icons[iconName] || icons.default;
  }

  // ===================================
  // KEYBOARD SHORTCUTS
  // ===================================
  document.addEventListener('keydown', (e) => {
    // Ctrl/Cmd + number to switch tabs
    if ((e.ctrlKey || e.metaKey) && e.key >= '1' && e.key <= '9') {
      const index = parseInt(e.key) - 1;
      if (state.modules[index]) {
        e.preventDefault();
        showModule(state.modules[index]);
      }
    }

    // Ctrl/Cmd + K to focus command input
    if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
      e.preventDefault();
      document.getElementById('rk-cmd')?.focus();
    }
  });

  // ===================================
  // INIT
  // ===================================
  window.addEventListener('DOMContentLoaded', loadModules);
})();
