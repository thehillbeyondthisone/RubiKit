// RubiKit OS - Desktop Environment
(function() {
  const MODULES_PATH = 'modules/modules.json';

  const state = {
    modules: [],
    openTabs: new Map(), // moduleId -> { window, taskbarBtn }
    activeTab: null,
    zIndex: 100
  };

  // ===================================
  // MODULE LOADING
  // ===================================
  async function loadModules() {
    try {
      const response = await fetch(MODULES_PATH);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      state.modules = await response.json();
      console.log(`[RubiKit] Loaded ${state.modules.length} modules`);
    } catch (err) {
      console.warn('[RubiKit] Failed to load modules.json:', err.message);
      state.modules = [];
    }
    initDesktop();
  }

  // ===================================
  // DESKTOP INITIALIZATION
  // ===================================
  function initDesktop() {
    const desktop = document.getElementById('desktop');

    state.modules.forEach((mod, index) => {
      const icon = document.createElement('div');
      icon.className = 'desktop-icon';
      icon.dataset.moduleId = mod.id;

      // Create icon image (use SVG if available, else emoji fallback)
      const iconImg = document.createElement('div');
      iconImg.className = 'icon-image';
      if (mod.icon && mod.icon.endsWith('.svg')) {
        iconImg.innerHTML = `<img src="${mod.icon}" alt="${mod.name}">`;
      } else {
        iconImg.innerHTML = getIconEmoji(mod.icon || mod.id);
      }

      const iconLabel = document.createElement('span');
      iconLabel.className = 'icon-label';
      iconLabel.textContent = mod.name;

      icon.appendChild(iconImg);
      icon.appendChild(iconLabel);

      // Position icons in a grid
      const col = index % 4;
      const row = Math.floor(index / 4);
      icon.style.left = `${20 + col * 100}px`;
      icon.style.top = `${20 + row * 100}px`;

      // Double-click to open
      icon.addEventListener('dblclick', () => openModule(mod));

      desktop.appendChild(icon);
    });

    // Start clock
    updateClock();
    setInterval(updateClock, 1000);
  }

  // ===================================
  // WINDOW MANAGEMENT
  // ===================================
  function openModule(mod) {
    // If already open, just focus it
    if (state.openTabs.has(mod.id)) {
      focusWindow(mod.id);
      return;
    }

    const windowsContainer = document.getElementById('windows');
    const taskbarTabs = document.getElementById('taskbar-tabs');

    // Create window
    const win = document.createElement('div');
    win.className = 'window';
    win.dataset.moduleId = mod.id;
    win.style.zIndex = ++state.zIndex;

    // Window header
    const header = document.createElement('div');
    header.className = 'window-header';
    header.innerHTML = `
      <span class="window-title">${mod.name}</span>
      <div class="window-controls">
        <button class="win-btn minimize" title="Minimize">_</button>
        <button class="win-btn maximize" title="Maximize">[]</button>
        <button class="win-btn close" title="Close">x</button>
      </div>
    `;

    // Window content (iframe)
    const content = document.createElement('div');
    content.className = 'window-content';
    const iframe = document.createElement('iframe');
    iframe.src = mod.path;
    iframe.title = mod.name;
    content.appendChild(iframe);

    win.appendChild(header);
    win.appendChild(content);

    // Position window
    const offset = state.openTabs.size * 30;
    win.style.left = `${50 + offset}px`;
    win.style.top = `${50 + offset}px`;

    windowsContainer.appendChild(win);

    // Create taskbar button
    const taskBtn = document.createElement('button');
    taskBtn.className = 'taskbar-btn active';
    taskBtn.textContent = mod.name;
    taskBtn.dataset.moduleId = mod.id;
    taskBtn.addEventListener('click', () => {
      if (win.classList.contains('minimized')) {
        win.classList.remove('minimized');
      }
      focusWindow(mod.id);
    });
    taskbarTabs.appendChild(taskBtn);

    // Store reference
    state.openTabs.set(mod.id, { window: win, taskbarBtn: taskBtn, iframe });
    state.activeTab = mod.id;

    // Window controls
    header.querySelector('.close').addEventListener('click', () => closeModule(mod.id));
    header.querySelector('.minimize').addEventListener('click', () => minimizeWindow(mod.id));
    header.querySelector('.maximize').addEventListener('click', () => maximizeWindow(mod.id));

    // Make window draggable
    makeDraggable(win, header);

    // Focus on click
    win.addEventListener('mousedown', () => focusWindow(mod.id));

    // Update taskbar state
    updateTaskbarState();
  }

  function closeModule(moduleId) {
    const tab = state.openTabs.get(moduleId);
    if (!tab) return;

    tab.window.remove();
    tab.taskbarBtn.remove();
    state.openTabs.delete(moduleId);

    // Focus another window if available
    if (state.activeTab === moduleId) {
      const remaining = Array.from(state.openTabs.keys());
      if (remaining.length > 0) {
        focusWindow(remaining[remaining.length - 1]);
      } else {
        state.activeTab = null;
      }
    }
    updateTaskbarState();
  }

  function minimizeWindow(moduleId) {
    const tab = state.openTabs.get(moduleId);
    if (!tab) return;
    tab.window.classList.add('minimized');
    tab.taskbarBtn.classList.remove('active');

    // Focus another window
    const visible = Array.from(state.openTabs.entries())
      .filter(([id, t]) => !t.window.classList.contains('minimized'));
    if (visible.length > 0) {
      focusWindow(visible[visible.length - 1][0]);
    }
  }

  function maximizeWindow(moduleId) {
    const tab = state.openTabs.get(moduleId);
    if (!tab) return;
    tab.window.classList.toggle('maximized');
  }

  function focusWindow(moduleId) {
    const tab = state.openTabs.get(moduleId);
    if (!tab) return;

    // Bring to front
    tab.window.style.zIndex = ++state.zIndex;
    state.activeTab = moduleId;

    // Update taskbar
    updateTaskbarState();
  }

  function updateTaskbarState() {
    state.openTabs.forEach((tab, id) => {
      const isActive = id === state.activeTab && !tab.window.classList.contains('minimized');
      tab.taskbarBtn.classList.toggle('active', isActive);
    });
  }

  // ===================================
  // DRAGGABLE WINDOWS
  // ===================================
  function makeDraggable(element, handle) {
    let offsetX, offsetY, isDragging = false;

    handle.addEventListener('mousedown', (e) => {
      if (e.target.classList.contains('win-btn')) return;
      isDragging = true;
      offsetX = e.clientX - element.offsetLeft;
      offsetY = e.clientY - element.offsetTop;
      element.classList.add('dragging');
    });

    document.addEventListener('mousemove', (e) => {
      if (!isDragging) return;
      element.style.left = `${e.clientX - offsetX}px`;
      element.style.top = `${e.clientY - offsetY}px`;
    });

    document.addEventListener('mouseup', () => {
      isDragging = false;
      element.classList.remove('dragging');
    });
  }

  // ===================================
  // UTILITIES
  // ===================================
  function getIconEmoji(iconName) {
    const icons = {
      notumhud: '📊', chart: '📊',
      llm: '🧠', brain: '🧠',
      hydra: '🐉',
      xanalytics: '📈',
      shopmaker: '🛒', shop: '🛒',
      mapviewer: '🗺️', map: '🗺️',
      notumvision: '👁️',
      settings: '⚙️',
      terminal: '💻',
      default: '📦'
    };
    return icons[iconName] || icons.default;
  }

  function updateClock() {
    const clock = document.getElementById('clock');
    const now = new Date();
    clock.textContent = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  // ===================================
  // KEYBOARD SHORTCUTS
  // ===================================
  document.addEventListener('keydown', (e) => {
    // Alt+Tab style cycling (Ctrl+Tab)
    if (e.ctrlKey && e.key === 'Tab') {
      e.preventDefault();
      const tabs = Array.from(state.openTabs.keys());
      if (tabs.length < 2) return;
      const currentIndex = tabs.indexOf(state.activeTab);
      const nextIndex = (currentIndex + 1) % tabs.length;
      focusWindow(tabs[nextIndex]);
    }
  });

  // ===================================
  // EXPOSE API
  // ===================================
  window.RubiKit = {
    openModule: (id) => {
      const mod = state.modules.find(m => m.id === id);
      if (mod) openModule(mod);
    },
    closeModule,
    getOpenModules: () => Array.from(state.openTabs.keys()),
    focusModule: focusWindow
  };

  // ===================================
  // INIT
  // ===================================
  window.addEventListener('DOMContentLoaded', loadModules);
})();
