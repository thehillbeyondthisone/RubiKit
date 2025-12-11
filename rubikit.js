/**
 * RubiKit OS - Desktop Operating System Interface
 * Loads and manages modules in a unified desktop environment
 */

(function() {
  'use strict';

  // ============================================
  // State Management
  // ============================================

  const state = {
    modules: [],
    openTabs: [],
    activeTabId: null,
    settings: {
      theme: 'theme-cyber',
      crtEnabled: true,
      glowIntensity: 60
    }
  };

  // ============================================
  // DOM Elements
  // ============================================

  const elements = {
    desktopGrid: null,
    desktopInfo: null,
    desktop: null,
    windowManager: null,
    tabs: null,
    contentArea: null,
    themeSelect: null,
    crtToggle: null,
    crtOverlay: null,
    glowSlider: null,
    showDesktopBtn: null,
    statusText: null,
    moduleCount: null,
    timeDisplay: null
  };

  // ============================================
  // Initialization
  // ============================================

  async function init() {
    console.log('[RubiKit] Initializing...');

    // Get DOM elements
    elements.desktopGrid = document.getElementById('rk-desktop-grid');
    elements.desktopInfo = document.querySelector('.rk-desktop-info');
    elements.desktop = document.getElementById('rk-desktop');
    elements.windowManager = document.getElementById('rk-window-manager');
    elements.tabs = document.getElementById('rk-tabs');
    elements.contentArea = document.getElementById('rk-content-area');
    elements.themeSelect = document.getElementById('rk-theme-select');
    elements.crtToggle = document.getElementById('rk-crt-toggle');
    elements.crtOverlay = document.getElementById('rk-crt-overlay');
    elements.glowSlider = document.getElementById('rk-glow-intensity');
    elements.showDesktopBtn = document.getElementById('rk-show-desktop');
    elements.statusText = document.getElementById('rk-status-text');
    elements.moduleCount = document.getElementById('rk-module-count');
    elements.timeDisplay = document.getElementById('rk-time');

    // Load settings
    loadSettings();

    // Setup event listeners
    setupEventListeners();

    // Apply theme
    applyTheme(state.settings.theme);

    // Apply CRT settings
    applyCRTSettings();

    // Load modules
    await loadModules();

    // Render desktop
    renderDesktop();

    // Start clock
    updateClock();
    setInterval(updateClock, 1000);

    console.log('[RubiKit] Initialization complete');
  }

  // ============================================
  // Module Loading
  // ============================================

  async function loadModules() {
    try {
      const response = await fetch('unified-os-tools/modules.json');
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      state.modules = await response.json();
      console.log(`[RubiKit] Loaded ${state.modules.length} modules`);

      elements.moduleCount.textContent = `${state.modules.length} modules loaded`;
      elements.statusText.textContent = 'Ready';

      return state.modules;
    } catch (error) {
      console.error('[RubiKit] Failed to load modules:', error);
      elements.statusText.textContent = 'Error loading modules';
      elements.desktopInfo.innerHTML = `
        <p style="color: var(--rk-error);">Failed to load modules</p>
        <p style="font-size: 12px; margin-top: 8px;">${error.message}</p>
      `;
      return [];
    }
  }

  // ============================================
  // Desktop Rendering
  // ============================================

  function renderDesktop() {
    if (state.modules.length === 0) {
      elements.desktopInfo.innerHTML = '<p>No modules available</p>';
      return;
    }

    // Clear info message
    elements.desktopInfo.style.display = 'none';

    // Clear existing icons
    elements.desktopGrid.innerHTML = '';

    // Create icon for each module
    state.modules.forEach(module => {
      const icon = createDesktopIcon(module);
      elements.desktopGrid.appendChild(icon);
    });
  }

  function createDesktopIcon(module) {
    const icon = document.createElement('div');
    icon.className = 'rk-desktop-icon rk-fade-in';
    icon.dataset.moduleId = module.id;

    // Check if icon is SVG path or emoji/text
    const iconContent = module.icon.endsWith('.svg')
      ? `<object type="image/svg+xml" data="${module.icon}" class="rk-icon-svg"></object>`
      : module.icon;

    icon.innerHTML = `
      <div class="rk-icon-image">${iconContent}</div>
      <div class="rk-icon-label">${module.name}</div>
      <div class="rk-icon-description">${module.description || ''}</div>
    `;

    icon.addEventListener('click', () => openModule(module));

    return icon;
  }

  // ============================================
  // Module/Tab Management
  // ============================================

  function openModule(module) {
    // Check if module is already open
    const existingTab = state.openTabs.find(tab => tab.id === module.id);
    if (existingTab) {
      switchToTab(module.id);
      return;
    }

    console.log(`[RubiKit] Opening module: ${module.name}`);

    // Create tab
    const tab = createTab(module);
    elements.tabs.appendChild(tab);

    // Create iframe
    const iframe = createModuleFrame(module);
    elements.contentArea.appendChild(iframe);

    // Add to open tabs
    state.openTabs.push({
      id: module.id,
      module: module,
      tab: tab,
      iframe: iframe
    });

    // Switch to new tab
    switchToTab(module.id);

    // Show window manager, hide desktop
    elements.desktop.style.display = 'none';
    elements.windowManager.style.display = 'flex';

    // Update status
    elements.statusText.textContent = `Opened ${module.name}`;
  }

  function createTab(module) {
    const tab = document.createElement('div');
    tab.className = 'rk-tab';
    tab.dataset.moduleId = module.id;

    // Check if icon is SVG path or emoji/text
    const iconContent = module.icon.endsWith('.svg')
      ? `<object type="image/svg+xml" data="${module.icon}" class="rk-tab-icon-svg"></object>`
      : module.icon;

    tab.innerHTML = `
      <span class="rk-tab-icon">${iconContent}</span>
      <span class="rk-tab-label">${module.name}</span>
      <button class="rk-tab-close" title="Close">×</button>
    `;

    // Tab click - switch to this tab
    tab.addEventListener('click', (e) => {
      if (!e.target.classList.contains('rk-tab-close')) {
        switchToTab(module.id);
      }
    });

    // Close button
    const closeBtn = tab.querySelector('.rk-tab-close');
    closeBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      closeTab(module.id);
    });

    return tab;
  }

  function createModuleFrame(module) {
    const iframe = document.createElement('iframe');
    iframe.className = 'rk-module-frame';
    iframe.dataset.moduleId = module.id;
    iframe.src = module.path;
    iframe.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-forms');

    return iframe;
  }

  function switchToTab(moduleId) {
    // Deactivate all tabs and frames
    state.openTabs.forEach(tab => {
      tab.tab.classList.remove('active');
      tab.iframe.classList.remove('active');
    });

    // Activate selected tab
    const activeTab = state.openTabs.find(tab => tab.id === moduleId);
    if (activeTab) {
      activeTab.tab.classList.add('active');
      activeTab.iframe.classList.add('active');
      state.activeTabId = moduleId;

      elements.statusText.textContent = `Viewing ${activeTab.module.name}`;
    }
  }

  function closeTab(moduleId) {
    const tabIndex = state.openTabs.findIndex(tab => tab.id === moduleId);
    if (tabIndex === -1) return;

    const closedTab = state.openTabs[tabIndex];

    // Remove DOM elements
    closedTab.tab.remove();
    closedTab.iframe.remove();

    // Remove from state
    state.openTabs.splice(tabIndex, 1);

    console.log(`[RubiKit] Closed module: ${closedTab.module.name}`);

    // If this was the active tab, switch to another or show desktop
    if (state.activeTabId === moduleId) {
      if (state.openTabs.length > 0) {
        // Switch to the previous tab or first tab
        const newActiveIndex = Math.max(0, tabIndex - 1);
        switchToTab(state.openTabs[newActiveIndex].id);
      } else {
        // No tabs left, show desktop
        showDesktop();
      }
    }

    elements.statusText.textContent = `Closed ${closedTab.module.name}`;
  }

  function showDesktop() {
    elements.desktop.style.display = 'flex';
    elements.windowManager.style.display = 'none';
    state.activeTabId = null;

    elements.statusText.textContent = 'Desktop';
  }

  // ============================================
  // Theme Management
  // ============================================

  function applyTheme(themeName) {
    // Remove all theme classes
    document.body.classList.remove(
      'theme-cyber',
      'theme-retro',
      'theme-terminal',
      'theme-neon',
      'theme-classic'
    );

    // Add new theme class
    document.body.classList.add(themeName);
    state.settings.theme = themeName;

    // Save settings
    saveSettings();

    console.log(`[RubiKit] Applied theme: ${themeName}`);
  }

  // ============================================
  // CRT Effects
  // ============================================

  function applyCRTSettings() {
    // Toggle CRT overlay
    if (state.settings.crtEnabled) {
      elements.crtOverlay.classList.add('active');
      elements.crtToggle.checked = true;
    } else {
      elements.crtOverlay.classList.remove('active');
      elements.crtToggle.checked = false;
    }

    // Set glow intensity
    const intensity = state.settings.glowIntensity / 100;
    document.documentElement.style.setProperty('--rk-crt-glow-intensity', intensity);
    elements.glowSlider.value = state.settings.glowIntensity;
  }

  function toggleCRT(enabled) {
    state.settings.crtEnabled = enabled;
    applyCRTSettings();
    saveSettings();

    console.log(`[RubiKit] CRT effects ${enabled ? 'enabled' : 'disabled'}`);
  }

  function setGlowIntensity(value) {
    state.settings.glowIntensity = value;
    applyCRTSettings();
    saveSettings();
  }

  // ============================================
  // Settings Persistence
  // ============================================

  function loadSettings() {
    try {
      const saved = localStorage.getItem('rubikit-settings');
      if (saved) {
        const parsed = JSON.parse(saved);
        state.settings = { ...state.settings, ...parsed };
        console.log('[RubiKit] Loaded saved settings');
      }
    } catch (error) {
      console.error('[RubiKit] Failed to load settings:', error);
    }
  }

  function saveSettings() {
    try {
      localStorage.setItem('rubikit-settings', JSON.stringify(state.settings));
    } catch (error) {
      console.error('[RubiKit] Failed to save settings:', error);
    }
  }

  // ============================================
  // Event Listeners
  // ============================================

  function setupEventListeners() {
    // Theme selector
    elements.themeSelect.value = state.settings.theme;
    elements.themeSelect.addEventListener('change', (e) => {
      applyTheme(e.target.value);
    });

    // CRT toggle
    elements.crtToggle.addEventListener('change', (e) => {
      toggleCRT(e.target.checked);
    });

    // Glow intensity slider
    elements.glowSlider.addEventListener('input', (e) => {
      setGlowIntensity(parseInt(e.target.value));
    });

    // Show desktop button
    elements.showDesktopBtn.addEventListener('click', showDesktop);

    // Keyboard shortcuts
    document.addEventListener('keydown', (e) => {
      // Alt+D - Show desktop
      if (e.altKey && e.key === 'd') {
        e.preventDefault();
        showDesktop();
      }

      // Alt+W - Close active tab
      if (e.altKey && e.key === 'w') {
        e.preventDefault();
        if (state.activeTabId) {
          closeTab(state.activeTabId);
        }
      }

      // Alt+Tab - Switch tabs (simplified)
      if (e.altKey && e.key === 'Tab') {
        e.preventDefault();
        if (state.openTabs.length > 1) {
          const currentIndex = state.openTabs.findIndex(t => t.id === state.activeTabId);
          const nextIndex = (currentIndex + 1) % state.openTabs.length;
          switchToTab(state.openTabs[nextIndex].id);
        }
      }
    });
  }

  // ============================================
  // Utility Functions
  // ============================================

  function updateClock() {
    const now = new Date();
    const timeStr = now.toLocaleTimeString('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false
    });
    elements.timeDisplay.textContent = timeStr;
  }

  // ============================================
  // Public API (for external use)
  // ============================================

  window.RubiKit = {
    openModule: (moduleId) => {
      const module = state.modules.find(m => m.id === moduleId);
      if (module) {
        openModule(module);
      } else {
        console.warn(`[RubiKit] Module not found: ${moduleId}`);
      }
    },
    closeModule: (moduleId) => {
      closeTab(moduleId);
    },
    getModules: () => state.modules,
    getOpenTabs: () => state.openTabs,
    setTheme: (themeName) => {
      applyTheme(themeName);
      elements.themeSelect.value = themeName;
    }
  };

  // ============================================
  // Start Application
  // ============================================

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

})();
