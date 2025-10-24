(() => {
  const $ = (id) => document.getElementById(id);

  const taskMods = $('taskbar-modules');
  const home = $('home'), gridHost = $('grid'), workspace = $('workspace');
  const dock = $('dock'), dockLog = $('console');
  const btnHome = $('btn-home'), btnConsole = $('toggle-console'), btnReload = $('reload');
  const homeStatus = $('home-status');

  const builder = $('builder'), btnBuilderToggle = $('toggle-builder'), btnBuilderClose = $('builder-close');
  const btnPick = $('builder-pick'), btnSave = $('builder-save'), btnApply = $('builder-apply');
  const builderLog = $('builder-log'), builderOut = $('builder-out');

  const RAW = Object.assign({
    basePath: ".",
    modulesDir: "./modules/",
    modulesManifestCandidates: ["./modules/modules.json", "./modules.json"],
    autodetectModules: false
  }, window.RubiKitConfig || {});

  const PAGE_DIR = new URL('.', location.href).href;
  const urlFrom = (base, p) => new URL(String(p || '').replace(/^\//, ''), base).href;

  const CFG = {
    pageDir: PAGE_DIR,
    modulesDir: urlFrom(PAGE_DIR, RAW.modulesDir),
    manifests: (RAW.modulesManifestCandidates || []).map(p => urlFrom(PAGE_DIR, p)),
    autodetect: !!RAW.autodetectModules
  };

  let modules = [];
  let activeId = null;

  const uiLog = (level, msg) => {
    const line = document.createElement('div');
    line.className = `log-line ${level}`;
    const ts = new Date().toLocaleTimeString();
    line.textContent = `[${ts}] ${level.toUpperCase()} • ${msg}`;
    dockLog.appendChild(line);
    dockLog.scrollTop = dockLog.scrollHeight;
  };
  const INFO = (m) => uiLog('info', m);
  const WARN = (m) => uiLog('warn', m);
  const ERROR = (m) => uiLog('error', m);
  const DEBUG = (m) => uiLog('debug', m);

  (function proxyConsole() {
    const orig = {
      log: console.log.bind(console),
      info: console.info.bind(console),
      warn: console.warn.bind(console),
      error: console.error.bind(console),
      debug: console.debug?.bind(console) || console.log.bind(console)
    };
    console.log = (...a) => { orig.log(...a); uiLog('info', a.map(x => formatAny(x)).join(' ')); };
    console.info = (...a) => { orig.info(...a); uiLog('info', a.map(x => formatAny(x)).join(' ')); };
    console.warn = (...a) => { orig.warn(...a); uiLog('warn', a.map(x => formatAny(x)).join(' ')); };
    console.error = (...a) => { orig.error(...a); uiLog('error', a.map(x => formatAny(x)).join(' ')); };
    console.debug = (...a) => { orig.debug(...a); uiLog('debug', a.map(x => formatAny(x)).join(' ')); };
  })();

  function formatAny(v) {
    try {
      if (typeof v === 'string') return v;
      if (v instanceof Error) return `${v.name}: ${v.message}`;
      return JSON.stringify(v);
    } catch { return String(v); }
  }

  window.addEventListener('error', (e) => ERROR(`Uncaught error: ${e.message} @ ${e.filename}:${e.lineno}:${e.colno}`));
  window.addEventListener('unhandledrejection', (e) => ERROR(`Unhandled promise rejection: ${formatAny(e.reason)}`));

  const cleanPath = (s) => String(s || '').replace(/\\/g, '/');
  function normalizeHref(h) {
    const s = cleanPath(h).trim();
    if (!s) return '';
    if (/^https?:\/\//i.test(s) || s.startsWith('data:')) return s;
    if (s.startsWith('./') || s.startsWith('../')) return urlFrom(CFG.pageDir, s);
    if (s.startsWith('/')) return urlFrom(CFG.pageDir, s.slice(1));
    return urlFrom(CFG.modulesDir, s);
  }
  function isIconPath(s) { return /[\/\\]/.test(s) || /\.(svg|png|jpe?g|gif|webp)$/i.test(s) || /^data:image\//i.test(s); }
  function normalizeIcon(iconRaw, hrefAbs) {
    const raw = cleanPath(iconRaw || '').trim();
    if (!raw) return { emoji: '🧩', url: '' };
    if (!isIconPath(raw)) return { emoji: raw, url: '' };
    const modBase = new URL('.', hrefAbs).href;
    if (/^https?:\/\//i.test(raw) || raw.startsWith('data:')) return { emoji: '', url: raw };
    if (raw.startsWith('/')) return { emoji: '', url: urlFrom(CFG.pageDir, raw.slice(1)) };
    return { emoji: '', url: new URL(raw, modBase).href };
  }
  function nrmModule(m) {
    const out = Object.assign({ icon: "🧩", description: "", tags: [] }, m);
    out.id = out.id || out.name || 'mod';
    out.name = out.name || out.id || 'Module';
    const hrefAbs = normalizeHref(out.href || (out.id ? `${out.id}/index.html` : ''));
    const icon = normalizeIcon(out.icon, hrefAbs);
    out.href = hrefAbs;
    out._iconUrl = icon.url;
    out._iconEmoji = icon.emoji;
    return out;
  }

  function parseLenientJSON(t) {
    try { return JSON.parse(t); }
    catch {
      const s = t.replace(/^\uFEFF/, '')
                 .replace(/\/\/[^\n\r]*|\/\*[\s\S]*?\*\//g, '')
                 .replace(/,\s*([}\]])/g, '$1');
      return JSON.parse(s);
    }
  }

  async function jget(url) {
    const t0 = performance.now();
    try {
      const r = await fetch(url, { cache: "no-cache" });
      const raw = await r.text();
      const parsed = parseLenientJSON(raw);
      const dt = (performance.now() - t0).toFixed(1);
      INFO(`Fetched ${url} (${r.status}) in ${dt}ms`);
      return parsed;
    } catch (e) {
      const dt = (performance.now() - t0).toFixed(1);
      WARN(`fetchJSON failed in ${dt}ms: ${url} :: ${e.message || e}`);
      return null;
    }
  }

  function applyInitialSettings() {
    const savedTheme = localStorage.getItem('rubikit_theme') || 'inferno';
    document.body.dataset.theme = savedTheme;
    const themeRadio = document.querySelector(`input[name="theme"][value="${savedTheme}"]`);
    if (themeRadio) themeRadio.checked = true;

    const textScale = localStorage.getItem('rubikit_text_scale') || '1';
    const iconScale = localStorage.getItem('rubikit_icon_scale') || '1';
    document.documentElement.style.setProperty('--text-scale', textScale);
    document.documentElement.style.setProperty('--icon-scale', iconScale);
    $('text-scale').value = parseFloat(textScale) * 100;
    $('icon-scale').value = parseFloat(iconScale) * 100;
  }

  function wire() {
    $('dock-clear')?.addEventListener('click', () => dockLog.innerHTML = '');
    $('dock-close')?.addEventListener('click', () => dock.classList.add('hidden'));
    btnHome.addEventListener('click', () => {
      workspace.classList.add('hidden');
      home.classList.remove('hidden');
      taskMods.querySelectorAll('button').forEach(b => b.classList.remove('active'));
      activeId = null;
    });
    btnConsole.addEventListener('click', () => dock.classList.toggle('hidden'));
    btnReload?.addEventListener('click', () => location.reload());
    window.addEventListener('keydown', e => {
      if ((e.ctrlKey || e.metaKey) && e.key === '`') {
        e.preventDefault();
        dock.classList.toggle('hidden');
      }
    });

    btnBuilderToggle?.addEventListener('click', () => builder.classList.toggle('open'));
    btnBuilderClose?.addEventListener('click', () => builder.classList.remove('open'));

    document.querySelectorAll('input[name="theme"]').forEach(radio => radio.addEventListener('change', (e) => {
      const newTheme = e.target.value;
      document.body.dataset.theme = newTheme;
      localStorage.setItem('rubikit_theme', newTheme);
      INFO(`Theme → ${newTheme}`);
    }));
    const textSlider = $('text-scale');
    const iconSlider = $('icon-scale');
    function applyScale() {
      const t = textSlider.value / 100;
      const i = iconSlider.value / 100;
      document.documentElement.style.setProperty('--text-scale', t);
      document.documentElement.style.setProperty('--icon-scale', i);
      localStorage.setItem('rubikit_text_scale', t);
      localStorage.setItem('rubikit_icon_scale', i);
      DEBUG(`Scale changed: text=${t}, icon=${i}`);
    }
    textSlider.addEventListener('input', applyScale);
    iconSlider.addEventListener('input', applyScale);

    const effectRadios = document.querySelectorAll('input[name="desktop-effect"]');
    const intensitySlider = $('effect-intensity');
    function applyDesktopEffects() {
      const effect = document.querySelector('input[name="desktop-effect"]:checked').value;
      const intensity = intensitySlider.value;
      home.dataset.effect = effect;
      const duration = 60 - (intensity * 0.55);
      home.style.setProperty('--effect-duration', `${Math.max(5, duration)}s`);
      const opacity = 0.05 + (intensity * 0.002);
      home.style.setProperty('--effect-opacity', opacity);
      DEBUG(`Effect=${effect}, intensity=${intensity}, duration=${Math.max(5, duration)}s, opacity=${opacity.toFixed(3)}`);
    }
    effectRadios.forEach(radio => radio.addEventListener('change', applyDesktopEffects));
    intensitySlider.addEventListener('input', applyDesktopEffects);
    applyDesktopEffects();

    btnPick?.addEventListener('click', scanLocalModules);
    btnSave?.addEventListener('click', saveManifestToFolder);
    btnApply?.addEventListener('click', applyBuilderManifest);

    $('clear-cache')?.addEventListener('click', () => {
      localStorage.removeItem('rubikit_manifest_cache');
      INFO('Cleared module cache. Reloading…');
      setTimeout(() => location.reload(), 300);
    });

    INFO(`RubiKitOS ready • ua=${navigator.userAgent}`);
    INFO(`pageDir=${CFG.pageDir}`);
    INFO(`modulesDir=${CFG.modulesDir}`);
    INFO(`manifest candidates: ${CFG.manifests.join(' | ')}`);
  }

  async function loadModules() {
    const t0 = performance.now();
    const cached = localStorage.getItem('rubikit_manifest_cache');
    if (cached) {
      INFO('Using cached manifest from localStorage.');
      try {
        const parsed = JSON.parse(cached);
        const list = Array.isArray(parsed) ? parsed : parsed.modules;
        modules = (list || []).map(nrmModule);
        paintModules(gridHost, modules);
        homeStatus.textContent = `Loaded ${modules.length} module(s) from cache.`;
        INFO(`Loaded ${modules.length} module(s) from cache in ${(performance.now() - t0).toFixed(1)}ms`);
        return;
      } catch (e) {
        WARN(`Cached manifest parse error: ${e.message}. Clearing cache.`);
        localStorage.removeItem('rubikit_manifest_cache');
      }
    }
    INFO('No valid cache. Probing project manifests…');
    let list = null, source = null;
    for (const m of CFG.manifests) {
      const j = await jget(m);
      if (j && (Array.isArray(j) || Array.isArray(j.modules))) {
        list = Array.isArray(j) ? j : j.modules;
        source = m;
        break;
      } else {
        WARN(`Manifest not found/invalid at ${m}`);
      }
    }
    if (!list) {
      modules = [];
      paintModules(gridHost, modules);
      homeStatus.textContent = "No modules found.";
      WARN('No manifest sources succeeded.');
      return;
    }
    modules = list.map(nrmModule);
    paintModules(gridHost, modules);
    homeStatus.textContent = `Found ${modules.length} module(s).`;
    INFO(`Loaded ${modules.length} module(s) from ${source} in ${(performance.now() - t0).toFixed(1)}ms`);
  }

  function paintModules(container, list) {
    const t0 = performance.now();
    const data = list || [];
    container.innerHTML = "";
    const grid = document.createElement('div');
    grid.className = 'chip-grid';
    container.appendChild(grid);
    if (!data.length) {
      const empty = document.createElement('div');
      empty.className = 'pane-hello';
      empty.textContent = "No modules.";
      grid.appendChild(empty);
      return;
    }
    data.forEach(mod => {
      const chip = document.createElement('button');
      chip.className = 'chip'; chip.id = `mod-${mod.id}`;
      chip.title = mod.description || mod.name;
      chip.innerHTML = `<span class="ico">${mod._iconUrl ? `<img src="${mod._iconUrl}" alt="">` : mod._iconEmoji}</span><span class="name">${mod.name}</span>`;
      chip.addEventListener('click', () => openModule(mod));
      grid.appendChild(chip);
    });
    DEBUG(`paintModules: ${data.length} card(s) in ${(performance.now() - t0).toFixed(1)}ms`);
  }

  function openModule(mod) {
    if (!mod || !mod.href) { WARN("Module missing href"); return; }
    document.querySelectorAll('.chip.active').forEach(b => b.classList.remove('active'));
    document.querySelector(`#mod-${mod.id}`)?.classList.add('active');
    const existing = $(`frame-${mod.id}`);
    if (existing) { switchTo(mod.id); return; }
    const frame = document.createElement('iframe');
    frame.id = `frame-${mod.id}`;
    frame.className = 'workframe';
    const start = performance.now();
    frame.addEventListener('load', () => INFO(`Module loaded: ${mod.id} → ${mod.href} (${(performance.now() - start).toFixed(1)}ms)`));
    frame.addEventListener('error', () => WARN(`Module failed: ${mod.id} → ${mod.href}`));
    frame.src = mod.href;
    frame.style.display = 'none';
    workspace.appendChild(frame);
    const btn = document.createElement('button');
    btn.id = `taskbtn-${mod.id}`;
    btn.title = mod.name;
    btn.innerHTML = `
      <span class="tab-ico">${mod._iconUrl ? `<img src="${mod._iconUrl}" alt="">` : mod._iconEmoji}</span>
      <span class="tab-name">${mod.name}</span>
      <span class="tab-close" title="Close">×</span>`;
    btn.onclick = () => switchTo(mod.id);
    btn.querySelector('.tab-close').addEventListener('click', (e) => { e.stopPropagation(); closeModule(mod.id); });
    taskMods.appendChild(btn);
    switchTo(mod.id);
  }

  function closeModule(id) {
    const frame = $(`frame-${id}`);
    const tab = $(`taskbtn-${id}`);
    if (frame) frame.remove();
    if (tab) tab.remove();
    INFO(`Closed module: ${id}`);
    if (activeId === id) {
      const remainingTabs = taskMods.querySelectorAll('button');
      if (remainingTabs.length > 0) {
        const lastTabId = remainingTabs[remainingTabs.length - 1].id.replace('taskbtn-', '');
        switchTo(lastTabId);
      } else { $('btn-home').click(); }
    }
  }

  function switchTo(idStr) {
    home.classList.add('hidden');
    workspace.classList.remove('hidden');
    workspace.querySelectorAll('iframe').forEach(f => f.style.display = 'none');
    taskMods.querySelectorAll('button').forEach(b => b.classList.remove('active'));
    const f = $(`frame-${idStr}`); if (f) f.style.display = 'block';
    const b = $(`taskbtn-${idStr}`); if (b) b.classList.add('active');
    activeId = idStr;
    DEBUG(`Switched to ${idStr}`);
  }

  let pickedDirHandle = null;
  let scannedManifest = { modules: [] };

  function blog(msg) {
    builderLog.textContent += msg + "\n";
    builderLog.scrollTop = builderLog.scrollHeight;
  }

  async function readJsonLenient(file) {
    const t = await file.text();
    return parseLenientJSON(t);
  }

  async function scanLocalModules() {
    if (!('showDirectoryPicker' in window)) { blog('❌ Your browser does not support folder access.'); return; }
    try {
      pickedDirHandle = await window.showDirectoryPicker({ id: 'rubikit-modules', mode: 'readwrite' });
      blog('📂 Picked: ' + pickedDirHandle.name);
      const subs = [];
      for await (const [name, handle] of pickedDirHandle.entries()) {
        if (handle.kind === 'directory') subs.push({ name, handle });
      }
      if (!subs.length) { blog('⚠️ No subfolders found.'); btnSave.disabled = true; return; }
      const found = [];
      for (const { name, handle } of subs) {
        let meta = null;
        try {
          const fh = await handle.getFileHandle('module.json');
          meta = await readJsonLenient(await fh.getFile());
        } catch {}
        if (meta) {
          found.push({ id: meta.id || name, name: meta.name || name, icon: cleanPath(meta.icon || "") || "🧩", description: meta.description || "", tags: meta.tags || [], href: `${name}/${cleanPath(meta.href || "index.html")}` });
          blog(`✓ ${name} (module.json)`);
          continue;
        }
        try {
          await handle.getFileHandle('index.html');
          found.push({ id: name, name: name.replace(/[-_]/g, ' ').replace(/\b\w/g, c => c.toUpperCase()), icon: "🧭", description: "", tags: [], href: `${name}/index.html` });
          blog(`✓ ${name} (index.html)`);
        } catch { blog(`– ${name} (skipped)`); }
      }
      scannedManifest = { modules: found };
      builderOut.value = JSON.stringify(scannedManifest, null, 2);
      const enable = !!found.length;
      btnSave.disabled = !enable;
      blog(`✔ Found ${found.length} module(s).`);
      INFO(`Scanner found ${found.length} module(s) in picked folder.`);
    } catch (e) {
      blog('❌ Picker failed: ' + (e.message || e));
      ERROR(`Folder picker: ${e.message || e}`);
    }
  }

  async function saveManifestToFolder() {
    if (!pickedDirHandle) { blog('⚠️ No folder picked.'); return; }
    const manifestJson = builderOut.value.trim();
    if (!manifestJson) { blog('⚠️ Editor is empty.'); return; }
    try { JSON.parse(manifestJson); } catch (e) { blog(`❌ Invalid JSON: ${e.message}`); return; }
    try {
      const fh = await pickedDirHandle.getFileHandle('modules.json', { create: true });
      const w = await fh.createWritable();
      await w.write(manifestJson);
      await w.close();
      blog('💾 Updated modules.json in folder.');
      localStorage.setItem('rubikit_manifest_cache', manifestJson);
      localStorage.setItem('rubikit_manifest_cache_ts', String(Date.now()));
      INFO('Cached manifest to localStorage after Update.');
    } catch (e) {
      blog('❌ Save failed: ' + (e.message || e));
      ERROR(`Save manifest failed: ${e.message || e}`);
    }
  }

  function applyBuilderManifest() {
    let parsed;
    try { parsed = JSON.parse(builderOut.value); }
    catch { blog('❌ Invalid JSON in editor.'); return; }
    if (!parsed || !Array.isArray(parsed.modules)) { blog('❌ Manifest must have a "modules" array.'); return; }
    modules = parsed.modules.map(nrmModule);
    paintModules(gridHost, modules);
    home.classList.remove('hidden'); workspace.classList.add('hidden');
    taskMods.innerHTML = ''; workspace.querySelectorAll('iframe').forEach(f => f.remove());
    activeId = null;
    homeStatus.textContent = `Loaded ${modules.length} module(s) from editor.`;
    blog('✔ Applied to runtime.');
    INFO(`Applied manifest to runtime: ${modules.length} module(s).`);
    localStorage.setItem('rubikit_manifest_cache', JSON.stringify(parsed));
    localStorage.setItem('rubikit_manifest_cache_ts', String(Date.now()));
    DEBUG('Runtime manifest cached (Apply).');
  }

  document.addEventListener('DOMContentLoaded', () => {
    applyInitialSettings();
    wire();
    loadModules();
  });
})();
