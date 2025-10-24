/* =======================================================
   RubiKitOS • manifest-manager.js
   - Theme sync (Inferno default; PalettePilot hooks)
   - Terminal polling (/api/terminal/pull)
   - Search filtering for module tiles
   - Tiny utility bus exposed on window.RubiKit
   ======================================================= */

(function () {
  const THEME_API_GET = "/api/theme/get";
  const THEME_API_SET = "/api/theme/set";
  const TERM_PULL = "/api/terminal/pull";

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

  const state = {
    themeName: "inferno",
    themeVars: {},
    termTimer: 0,
    lastTerm: "",
  };

  // ---------------- THEME ----------------
  async function loadTheme() {
    try {
      const res = await fetch(THEME_API_GET, { cache: "no-store" });
      if (!res.ok) throw new Error("HTTP " + res.status);
      const json = await res.json();
      const name = (json.theme || "inferno").toLowerCase();
      const vars = json.variables || {};

      state.themeName = name;
      state.themeVars = vars;

      applyThemeName(name);
      applyThemeVars(vars);
      logDock(`Theme loaded: ${name}`);
    } catch (e) {
      // fallback to inferno without noise
      applyThemeName("inferno");
    }
  }

  function applyThemeName(name) {
    document.documentElement.setAttribute("data-theme", name || "inferno");
    if (typeof window.applyTheme === "function") {
      // keep compatibility with index.html’s hook
      window.applyTheme(name);
    }
  }

  function applyThemeVars(vars) {
    const root = document.documentElement;
    for (const k in vars) {
      root.style.setProperty(`--${k}`, String(vars[k]));
    }
  }

  async function saveTheme(name, vars) {
    try {
      const body = JSON.stringify({ theme: name || state.themeName, variables: vars || state.themeVars });
      const res = await fetch(THEME_API_SET, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body
      });
      if (!res.ok && res.status !== 204) throw new Error("HTTP " + res.status);
      logDock("Theme saved.");
    } catch (e) {
      logDock("Theme save failed: " + e.message);
    }
  }

  // PalettePilot external hooks
  window.PalettePilot = window.PalettePilot || {};
  window.PalettePilot.apply = function (payload) {
    // payload: { theme?:string, variables?:{k:v} }
    if (payload && payload.theme) {
      state.themeName = payload.theme;
      applyThemeName(payload.theme);
    }
    if (payload && payload.variables) {
      state.themeVars = { ...state.themeVars, ...payload.variables };
      applyThemeVars(payload.variables);
    }
    // persist to disk
    saveTheme(state.themeName, state.themeVars);
  };

  // ---------------- TERMINAL ----------------
  function logDock(line) {
    const el = $("#dockBody");
    if (!el) return;
    el.textContent += `[${new Date().toLocaleTimeString()}] ${line}\n`;
    el.scrollTop = el.scrollHeight;
  }

  async function pullTerminal() {
    try {
      const res = await fetch(TERM_PULL, { cache: "no-store" });
      if (!res.ok) throw new Error("HTTP " + res.status);
      const txt = await res.text();

      if (txt && txt !== state.lastTerm) {
        state.lastTerm = txt;
        const el = $("#dockBody");
        if (el) {
          el.textContent = txt;
          el.scrollTop = el.scrollHeight;
        }
      }
    } catch (e) {
      // keep silent; avoid spam
    }
  }

  function startTerminalPolling() {
    stopTerminalPolling();
    state.termTimer = window.setInterval(pullTerminal, 1000);
    pullTerminal();
  }

  function stopTerminalPolling() {
    if (state.termTimer) {
      clearInterval(state.termTimer);
      state.termTimer = 0;
    }
  }

  // ---------------- SEARCH FILTER ----------------
  function wireSearch() {
    const input = $("#search");
    if (!input) return;

    input.addEventListener("input", () => {
      const q = (input.value || "").trim().toLowerCase();
      const tiles = $$("#homeGrid .tile");
      tiles.forEach(tile => {
        const name = (tile.querySelector(".name")?.textContent || "").toLowerCase();
        const meta = (tile.querySelector(".desc")?.textContent || "").toLowerCase();
        const hit = !q || name.includes(q) || meta.includes(q);
        tile.style.display = hit ? "" : "none";
      });
    });
  }

  // ---------------- WINDOW BUS ----------------
  window.RubiKit = {
    get theme() { return { name: state.themeName, variables: { ...state.themeVars } }; },
    setTheme(name) { state.themeName = name; applyThemeName(name); saveTheme(name); },
    setVariables(vars) { state.themeVars = { ...state.themeVars, ...vars }; applyThemeVars(vars); saveTheme(state.themeName, state.themeVars); },
    // terminal
    startTerminal: startTerminalPolling,
    stopTerminal: stopTerminalPolling,
    log: logDock
  };

  // ---------------- INIT ----------------
  function init() {
    loadTheme();
    wireSearch();
    startTerminalPolling();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
