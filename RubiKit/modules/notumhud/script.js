(() => {
  // ===================================
  // CONFIG & STATE
  // ===================================
  let API_BASE = 'http://127.0.0.1:8780'; // Changed from 8777 to 8780 for RubiKit integration
  let es = null;
  let isConnected = false;
  let browserInitialized = false;
  let activeCategory = '';
  let customCategories = {}; // This will hold the user's custom mappings
  const CATEGORY_STORAGE_KEY = 'notumHUD.customCategories';
  const optimisticPins = new Map();

  const els = {
    body: document.body, statusText: document.getElementById('statusText'), dot: document.getElementById('dot'),
    ipDisplay: document.getElementById('ip-display'), aao: document.getElementById('aao'), aad: document.getElementById('aad'),
    crit: document.getElementById('crit'), xpmod: document.getElementById('xpmod'), hpbar: document.getElementById('hpbar'),
    hptext: document.getElementById('hptext'), nanobar: document.getElementById('nanobar'), nanotext: document.getElementById('nanotext'),
    dmgchips: document.getElementById('dmgchips'), acs: document.getElementById('acs'),
    pins: document.getElementById('pins'), filter: document.getElementById('filter'), theme: document.getElementById('theme'),
    font: document.getElementById('font'), fontSize: document.getElementById('font-size'),
    compactToggle: document.getElementById('compact-toggle'),
    editModeToggle: document.getElementById('edit-mode-toggle'), debugLog: document.getElementById('debug-log'),
    jsonViewer: document.getElementById('json-viewer'), portInput: document.getElementById('port-input'),
    reconnectBtn: document.getElementById('reconnect-btn'), scanPortsBtn: document.getElementById('scan-ports-btn'),
    selfCheckBtn: document.getElementById('self-check-btn'), autoscrollDebug: document.getElementById('autoscroll-debug'),
    apiPanelContainer: document.getElementById('api-panel-container'), apiPanelToggle: document.getElementById('api-panel-toggle'),
    apiInspectorForm: document.getElementById('api-inspector-form'), apiAction: document.getElementById('api-action'),
    apiValue: document.getElementById('api-value'), browserNav: document.getElementById('browser-nav'),
    browserLists: document.getElementById('browser-lists'),
    templates: { chip: document.getElementById('template-chip'), kv: document.getElementById('template-kv'), pin: document.getElementById('template-pin') }
  };

  const GROUPS = { abilities: ['Strength','Agility','Stamina','Intelligence','Sense','Psychic'], body: ['BodyDev','NanoPool','HealDelta','NanoDelta','FirstAid','Treatment','Adventuring'], melee: ['MartialArts','MeleeEnergy','OneHandedBlunt','OneHandedEdged','Piercing','TwoHandedBlunt','TwoHandedEdged','Brawl','FastAttack','SneakAttack','MultiMelee','Dimach','Riposte','SharpObject'], ranged: ['AssaultRifle','Bow','Grenade','HeavyWeapons','Pistol','RangedEnergy','Rifle','Shotgun','AimedShot','Burst','FlingShot','FullAuto','MultiRanged','BowSpecialAttack'], speed: ['MeleeInit','RangedInit','PhysicalInit','AggDef','DodgeRanged','EvadeClsC','DuckExp','RunSpeed','Parry'], tradeskills: ['MechanicalEngineering','ElectricalEngineering','FieldQuantumPhysics','WeaponSmithing','Pharmaceuticals','Chemistry','Tutoring','ComputerLiteracy','Psychology'], nano: ['MatterMetamorphosis','BiologicalMetamorphosis','PsychologicalModification','MatterCreation','TimeAndSpace','SensoryImprovement','NanoCInit','NanoProg','NanoResist'], spying: ['BreakingEntry','Concealment','Perception','TrapDisarm'], navigation: ['MapNavigation','Swimming','VehicleAir','VehicleGround','VehicleWater'] };
  const LABELS = {'TwoHandedEdged':'2HE','OneHandedEdged':'1HE','TwoHandedBlunt':'2HB','OneHandedBlunt':'1HB','MeleeEnergy':'ME','RangedEnergy':'RE','ComputerLiteracy':'CompLit','NanoCInit':'NanoInit','NanoProg':'NanoProg','AddAllOff':'AAO','AddAllDef':'AAD','CriticalIncrease':'Crit+','BiologicalMetamorphosis':'BM','PsychologicalModification':'PM','SensoryImprovement':'SI','FieldQuantumPhysics':'QFT'};
  const defaultSkillToCategoryMap = Object.entries(GROUPS).reduce((acc,[g,skills])=>{skills.forEach(s=>acc[s]=g);return acc;},{});

  // ===================================
  // UTILITY FUNCTIONS
  // ===================================
  const logDebug = (message, type = 'info') => { const row = document.createElement('span'); row.className = `log-${type}`; row.innerHTML = `<span class="log-time">[${new Date().toLocaleTimeString()}]</span> <span>${message}</span>`; els.debugLog.appendChild(row); if (els.autoscrollDebug.checked) els.debugLog.scrollTop = els.debugLog.scrollHeight; };
  const post = (action, value) => { logDebug(`API POST > action=${action}, value=${value ?? ''}`, 'api'); fetch(`${API_BASE}/api/cmd?action=${encodeURIComponent(action)}&value=${encodeURIComponent(value ?? '')}`,{method:'POST', mode:'cors'}).catch(err=>logDebug(`POST Error: ${err.message}`,'error')); };
  const updateStat = (element, value) => { if (element && element.textContent !== String(value)) { element.textContent = value; element.classList.add('updated'); setTimeout(() => element.classList.remove('updated'), 400); } };
  const labelFor = n => LABELS[n] || n.replace(/([a-z])([A-Z])/g,'$1 $2');
  const nz = v => v != null && v !== 0 && v !== 12345678;
  const HIDE_VALUE = 1234567890;
  const saveLocal = (key, val) => localStorage.setItem(`notumHUD.${key}`, String(val));
  const loadLocal = (key, fallback) => localStorage.getItem(`notumHUD.${key}`) ?? fallback;
  const updateBodyClass = () => { els.body.className = `${els.theme.value} ${els.font.value}` + (els.compactToggle.checked ? ' compact':'') + (els.editModeToggle.checked ? ' edit-mode':''); };

  // ===================================
  // RENDER FUNCTIONS
  // ===================================
  const renderCoreStats = (stats) => { updateStat(els.aao, nz(stats.AddAllOff) ? stats.AddAllOff : '–'); updateStat(els.aad, nz(stats.AddAllDef) ? stats.AddAllDef : '–'); updateStat(els.crit, nz(stats.CriticalIncrease) ? stats.CriticalIncrease : '–'); updateStat(els.xpmod, nz(stats.XPModifier) ? `${stats.XPModifier}%` : '–'); };
  const renderBars = (stats) => { const hp_now = stats.Health || 0, hp_max = stats.MaxHealth || 0; const hp_pct = hp_max > 0 ? Math.min(100, (hp_now / hp_max) * 100) : 0; els.hpbar.style.width = `${hp_pct}%`; els.hptext.textContent = `${hp_now} / ${hp_max} (${Math.round(hp_pct)}%)`; const nano_now = stats.CurrentNano || 0, nano_max = stats.MaxNanoEnergy || 0; const nano_pct = nano_max > 0 ? Math.min(100, (nano_now / nano_max) * 100) : 0; els.nanobar.style.width = `${nano_pct}%`; els.nanotext.textContent = `${nano_now} / ${nano_max} (${Math.round(nano_pct)}%)`; };
  const reconcileList = (container, items, keyProp, renderFn, updateFn) => { const newKeys = new Set(items.map(item => item[keyProp])); const existingElements = new Map(Array.from(container.children).map(el => [el.dataset.key, el])); for (const [key, el] of existingElements.entries()) { if (!newKeys.has(key)) el.remove(); } for (const item of items) { const key = item[keyProp]; if (existingElements.has(key)) { updateFn(existingElements.get(key), item); } else { container.appendChild(renderFn(item)); } } };

  const createPinElement = (p) => {
    const clone = els.templates.pin.content.cloneNode(true);
    const el = clone.querySelector('.pin');
    el.dataset.key = p.name;
    el.querySelector('b').textContent = p.label || labelFor(p.name);
    el.querySelector('.val').textContent = p.v;
    el.addEventListener('click', () => {
      if (!els.body.classList.contains('edit-mode')) return;
      post('pin_remove', p.name);
      optimisticPins.delete(p.name);
      el.remove();
      const browserItem = els.browserLists.querySelector(`.kv[data-name="${p.name}"]`);
      if (browserItem) browserItem.classList.remove('pinned');
    });
    return el;
  };
  const renderPins = (pins = []) => { const updatePinElement = (el, p) => updateStat(el.querySelector('.val'), p.v); reconcileList(els.pins, pins, 'name', createPinElement, updatePinElement); };
  const renderChips = (stats) => { const renderChipContainer = (container, filter) => { const items = Object.entries(stats).filter(([k, v]) => k.endsWith(filter) && nz(v)).map(([key, value]) => ({ name: key.replace(filter, ''), value })); const createChipElement = (item) => { const clone = els.templates.chip.content.cloneNode(true); const el = clone.querySelector('.chip'); el.dataset.key = item.name; el.querySelector('b').textContent = item.name; el.querySelector('span').textContent = item.value; return el; }; const updateChipElement = (el, item) => updateStat(el.querySelector('span'), item.value); reconcileList(container, items, 'name', createChipElement, updateChipElement); }; renderChipContainer(els.dmgchips, 'DamageModifier'); renderChipContainer(els.acs, 'AC'); };

  const setupBrowser = (data, stats) => {
    const statNames = data.all_names || Object.keys(stats);
    const statsByCategory = {};
    for (const name of statNames) { const category = customCategories[name] || defaultSkillToCategoryMap[name] || 'misc'; if (!statsByCategory[category]) statsByCategory[category] = []; statsByCategory[category].push(name); }
    const sortedCategories = Object.keys(statsByCategory).sort();
    sortedCategories.forEach(category => {
      const btn = document.createElement('button'); btn.textContent = category; btn.dataset.category = category; btn.addEventListener('click', () => { els.browserNav.querySelector('.active')?.classList.remove('active'); els.browserLists.querySelector('.active')?.classList.remove('active'); btn.classList.add('active'); document.getElementById(`list-${category}`)?.classList.add('active'); activeCategory = category; }); els.browserNav.appendChild(btn);
      const listEl = document.createElement('div'); listEl.id = `list-${category}`; listEl.className = 'kvlist'; listEl.dataset.category = category;
      for (const name of statsByCategory[category]) {
        const clone = els.templates.kv.content.cloneNode(true); const item = clone.querySelector('.kv'); item.dataset.name = name; item.querySelector('.name').textContent = data.customLabels?.[name] || labelFor(name); if (stats[name] === HIDE_VALUE) { item.style.display = 'none'; item.dataset.hiddenByValue = 'true'; }
        const select = item.querySelector('.category-select');
        for (const group in GROUPS) { select.add(new Option(group, group, group === category, group === category)); }
        select.addEventListener('change', () => {
          const newCat = select.value;
          const defaultCat = defaultSkillToCategoryMap[name] || 'misc';
          if (newCat !== defaultCat) { customCategories[name] = newCat; } else { delete customCategories[name]; }
          saveCustomCategories();
          post('set_category', JSON.stringify({ name, category: newCat }));
          els.browserLists.querySelector(`#list-${newCat}`)?.appendChild(item);
        });
        item.addEventListener('click', (e) => {
          if (e.target.tagName === 'SELECT') return;
          const isPinned = !item.classList.contains('pinned'); post(isPinned ? 'pin_add' : 'pin_remove', name); item.classList.toggle('pinned');
          if (isPinned) { optimisticPins.set(name, { v: item.querySelector('.val').textContent, label: labelFor(name) }); } else { optimisticPins.delete(name); }
          renderPins(buildEffectivePins(data.pins));
        });
        listEl.appendChild(clone);
      }
      els.browserLists.appendChild(listEl);
    });
    const firstBtn = els.browserNav.querySelector('button');
    if (firstBtn) { firstBtn.classList.add('active'); activeCategory = firstBtn.dataset.category; }
    els.browserLists.querySelector('.kvlist')?.classList.add('active');
    browserInitialized = true; logDebug("Stat browser initialized.", "success");
  };
  const updateBrowserStats = (data, stats) => { if (!browserInitialized) return; const currentPins = new Set((data.pins || []).map(p => p.name)); for (const name in stats) { const item = els.browserLists.querySelector(`.kv[data-name="${name}"]`); if (item) { const newValue = stats[name]; updateStat(item.querySelector('.val'), newValue); item.classList.toggle('pinned', currentPins.has(name) || optimisticPins.has(name)); if (newValue === HIDE_VALUE) { item.style.display = 'none'; item.dataset.hiddenByValue = 'true'; } else if (item.dataset.hiddenByValue === 'true') { item.style.display = ''; item.dataset.hiddenByValue = 'false'; } } } };
  const renderSettings = (settings = {}) => { els.theme.value = settings.theme || loadLocal('theme','theme-notum'); els.font.value = settings.font || loadLocal('font','font-default'); els.fontSize.value = settings.fontSize || loadLocal('fontSize', 100); document.documentElement.style.setProperty('--font-scale', `${els.fontSize.value}%`); updateBodyClass(); };
  const buildEffectivePins = (serverPins = []) => { const effectivePins = new Map(serverPins.map(p => [p.name, p])); optimisticPins.forEach((value, key) => { if (!effectivePins.has(key)) effectivePins.set(key, { name: key, ...value }); }); serverPins.forEach(p => optimisticPins.delete(p.name)); return Array.from(effectivePins.values()); };

  function render(data) {
    isConnected = true; if(els.jsonViewer) els.jsonViewer.textContent = JSON.stringify(data,null,2); let statsCollection = {}; if (data.stats && typeof data.stats === 'object') { Object.values(data.stats).forEach(cat => Object.assign(statsCollection, cat)); } else if (data.all) { statsCollection = data.all; } else { if(isConnected && Object.keys(data).length > 2) logDebug('Could not find stats in payload.', 'warn'); return; }
    if (!browserInitialized && (data.all_names || Object.keys(statsCollection).length > 0)) { setupBrowser(data, statsCollection); }
    const effectivePins = buildEffectivePins(data.pins);
    renderCoreStats(statsCollection); renderBars(statsCollection); renderChips(statsCollection); renderPins(effectivePins); updateBrowserStats(data, statsCollection); renderSettings(data.settings);
    if(data.localIP){ const port = els.portInput.value || API_BASE.split(':').pop(); els.ipDisplay.textContent = `LAN: ${data.localIP}:${port}`; }
  }

  // ===================================
  // PERSISTENCE LOGIC
  // ===================================
  function saveCustomCategories() {
    localStorage.setItem(CATEGORY_STORAGE_KEY, JSON.stringify(customCategories));
    logDebug('Custom categories saved.', 'success');
  }

  function loadCustomCategories() {
    const stored = localStorage.getItem(CATEGORY_STORAGE_KEY);
    customCategories = stored ? JSON.parse(stored) : {};
    logDebug('Custom categories loaded.');
  }

  // ===================================
  // CONNECTIVITY & INITIALIZATION
  // ===================================
  function connect(){ if(es){ es.close(); } logDebug(`Attempting SSE connection to ${API_BASE}/events...`); try{ es = new EventSource(`${API_BASE}/events`); es.onopen = () => { logDebug('SSE connection established.','success'); els.statusText.textContent='Live'; els.dot.className='dot ok'; isConnected=true; }; es.onerror = () => { logDebug('SSE connection failed. Falling back to poll.','error'); els.statusText.textContent='SSE Error'; els.dot.className='dot err'; es.close(); pollState(); }; es.onmessage = (e) => { if(e.data){ try{ render(JSON.parse(e.data)); } catch(err){ logDebug(`Render Error: ${err.message}`,'error'); } } }; } catch(err) { logDebug(`EventSource constructor failed: ${err.message}`,'error'); pollState(); } }
  async function pollState(){ logDebug(`Polling ${API_BASE}/api/state...`,'api'); els.statusText.textContent='Polling'; els.dot.className='dot'; try{ const r = await fetch(`${API_BASE}/api/state`,{mode:'cors'}); if(r.ok){ logDebug('Poll successful.','success'); render(await r.json()); } else{ logDebug(`Poll failed: HTTP ${r.status}`,'error'); } } catch(err) { logDebug(`Poll fetch error: ${err.message}`,'error'); } setTimeout(connect, 5000); }
  async function scanPorts(){ const defaults = [8780, 8777, 8778, 8000, 8080, 3000, 5000]; const current = els.portInput.value ? Number(els.portInput.value) : null; const ports = [...new Set([current, ...defaults].filter(Boolean))]; logDebug(`--- Starting Port Scan: ${ports.join(', ')} ---`); let found = false; for(const port of ports){ const controller = new AbortController(); const timeoutId = setTimeout(()=>controller.abort(), 1500); try{ const res = await fetch(`http://127.0.0.1:${port}/api/state`, {signal:controller.signal, mode:'cors'}); clearTimeout(timeoutId); if(res.ok){ logDebug(`✅ Port ${port}: Found! Set port and click 'Connect'.`,'success'); found = true; } else{ logDebug(`🔵 Port ${port}: Responded (HTTP ${res.status}).`,'warn'); } }catch(e){ clearTimeout(timeoutId); logDebug(`🔴 Port ${port}: No response.`); } } logDebug(found ? '--- Port Scan Complete ---' : 'Scan complete. No active NotumHUD ports found.','warn'); }

  function initEventListeners() {
    ['theme','font'].forEach(key => els[key].addEventListener('change', () => { updateBodyClass(); post(key, els[key].value); saveLocal(key, els[key].value); }));
    els.fontSize.addEventListener('input', () => document.documentElement.style.setProperty('--font-scale', `${els.fontSize.value}%`));
    els.fontSize.addEventListener('change', () => { post('fontSize', els.fontSize.value); saveLocal('fontSize', els.fontSize.value); });
    els.compactToggle.addEventListener('change', () => { updateBodyClass(); saveLocal('compact', els.compactToggle.checked ? '1':'0'); });
    els.editModeToggle.addEventListener('change', () => { updateBodyClass(); saveLocal('editMode', els.editModeToggle.checked ? '1':'0'); });
    els.apiPanelToggle.addEventListener('click', () => els.apiPanelContainer.classList.toggle('is-open'));
    els.reconnectBtn.addEventListener('click', () => { const newPort = els.portInput.value; if(!newPort){ logDebug('Port cannot be empty.','error'); return; } saveLocal('port', newPort); API_BASE = `http://127.0.0.1:${newPort}`; logDebug(`API base set to ${API_BASE}. Reconnecting...`); connect(); });
    els.scanPortsBtn.addEventListener('click', scanPorts);
    els.apiInspectorForm.addEventListener('submit', (e) => { e.preventDefault(); const action = (els.apiAction.value||'').trim(), value = (els.apiValue.value||'').trim(); if(!action){ logDebug('API Inspector: action is required.','error'); return; } post(action, value); });
    els.selfCheckBtn.addEventListener('click', async () => { logDebug('Running Self-Check...'); try{ const ping = await fetch(`${API_BASE}/api/state`, {mode:'cors'}); logDebug(`State: HTTP ${ping.status}`,(ping.ok?'success':'warn')); }catch(err){ logDebug(`State error: ${err.message}`,'error'); } try{ const test = await fetch(`${API_BASE}/api/cmd?action=ping&value=ui`, {method:'POST', mode:'cors'}); logDebug(`Cmd ping: HTTP ${test.status}`,(test.ok?'success':'warn')); }catch(err){ logDebug(`Cmd error: ${err.message}`,'error'); } });

    // Global Filter Logic
    els.filter.addEventListener('input', () => {
      const q = els.filter.value.toLowerCase().trim();
      els.browserLists.classList.toggle('is-filtering', !!q);
      if (q) {
        els.browserNav.querySelector('.active')?.classList.remove('active');
        els.browserLists.querySelectorAll('.kvlist').forEach(list => {
          let hasVisibleMatch = false;
          list.querySelectorAll('.kv').forEach(item => {
            const name = item.querySelector('.name')?.textContent.toLowerCase() || '';
            const isHiddenByValue = item.dataset.hiddenByValue === 'true';
            if (!isHiddenByValue && name.includes(q)) { item.style.display = 'flex'; hasVisibleMatch = true; } else { item.style.display = 'none'; }
          });
          list.style.display = hasVisibleMatch ? 'flex' : 'none';
        });
      } else {
        els.browserLists.querySelectorAll('.kv').forEach(item => { item.style.display = item.dataset.hiddenByValue === 'true' ? 'none' : ''; });
        els.browserLists.querySelectorAll('.kvlist').forEach(list => { list.style.display = ''; list.classList.toggle('active', list.dataset.category === activeCategory); });
        els.browserNav.querySelector(`[data-category="${activeCategory}"]`)?.classList.add('active');
      }
    });
  }

  function init() {
    loadCustomCategories();
    const savedPort = loadLocal('port','8780'); els.portInput.value = savedPort; API_BASE = `http://127.0.0.1:${savedPort}`; els.theme.value = loadLocal('theme', 'theme-notum'); els.font.value = loadLocal('font', 'font-default'); els.fontSize.value = loadLocal('fontSize', '100'); if(loadLocal('compact','0') === '1') els.compactToggle.checked = true; if(loadLocal('editMode','0') === '1') els.editModeToggle.checked = true; logDebug("Initializing NotumHUD..."); renderSettings(); initEventListeners(); connect(); setTimeout(() => { if(!isConnected){ logDebug('No connection. Loading demo data.','warn'); els.statusText.textContent = 'Demo Mode'; els.dot.className='dot'; render({ "all":{ Strength:349, Agility:367, Stamina:433, Health:12345, MaxHealth:15000, CurrentNano:5678, MaxNanoEnergy:8000, AddAllOff:150, AddAllDef:200, CriticalIncrease:5, XPModifier:10, ProjectileDamageModifier:50, MeleeAC:12000, NanoCInit:1800, Tutoring: HIDE_VALUE }, "all_names": ["Strength", "Agility", "Stamina", "AddAllOff", "AddAllDef", "CriticalIncrease", "XPModifier", "ProjectileDamageModifier", "MeleeAC", "NanoCInit", "Health", "MaxHealth", "CurrentNano", "MaxNanoEnergy", "Tutoring"], "pins":[{"name":"NanoCInit","v":1800,"label":"Nano Init"}], "settings":{"theme":"theme-inferno","font":"font-sci-fi"} }); } }, 1500);
  }

  init();
})();
