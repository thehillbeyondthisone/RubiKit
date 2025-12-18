(function(){
  var es, built=false, builtKey='';
  var els = {
    hud: document.getElementById('hud'),
    status: document.getElementById('status'),
    groupsContainer: document.getElementById('groups-container'),
    // Dock quick stats
    dockAao: document.getElementById('dock-aao'),
    dockAad: document.getElementById('dock-aad'),
    dockCrit: document.getElementById('dock-crit'),
    dockXp: document.getElementById('dock-xp'),
    // Cards
    aao: document.getElementById('aao'), aad: document.getElementById('aad'),
    crit: document.getElementById('crit'), xpmod: document.getElementById('xpmod'),
    dmgchips: document.getElementById('dmgchips'), acs: document.getElementById('acs'),
    pins: document.getElementById('pins'),
    theme: document.getElementById('theme'), compact: document.getElementById('compact'),
    interval: document.getElementById('interval'), toggle: document.getElementById('toggle'),
    filter: document.getElementById('filter'),
    showHidden: document.getElementById('showHidden'), btnResetHidden: document.getElementById('btnResetHidden'),
    hpCanvas: document.getElementById('hp-canvas'), nanoCanvas: document.getElementById('nano-canvas'),
  };

  // Rate buttons
  function syncRateButtons(ms){
    var btns=document.querySelectorAll('.seg [data-rate]');
    for (var i=0;i<btns.length;i++){
      var v=parseInt(btns[i].getAttribute('data-rate')||'0',10)|0;
      btns[i].classList.toggle('active', v===(ms|0));
    }
  }
  var segBtns=document.querySelectorAll('.seg [data-rate]');
  for (var i=0;i<segBtns.length;i++){
    (function(btn){
      btn.addEventListener('click', function(){
        var ms=parseInt(btn.getAttribute('data-rate')||'500',10)|0;
        els.interval.value=ms; syncRateButtons(ms); post('interval_ms', ms);
      });
    })(segBtns[i]);
  }

  // Server-backed settings
  function post(action, value){
    fetch('/api/cmd?action='+encodeURIComponent(action)+'&value='+encodeURIComponent(value==null?'':value), {method:'POST'});
  }
  els.interval.addEventListener('change', function(){ var v=parseInt(els.interval.value||'500',10); if (isNaN(v)) v=500; post('interval_ms', v); syncRateButtons(v); });
  els.toggle.addEventListener('change', function(){ post('enable', els.toggle.checked?1:0); });
  els.theme.addEventListener('change', function(){ document.body.className = els.theme.value; post('theme', els.theme.value); redrawGaugesSoon(); });
  els.compact.addEventListener('change', function(){ if (els.compact.checked) document.body.classList.add('compact'); else document.body.classList.remove('compact'); post('compact', els.compact.checked?1:0); });

  if (els.btnResetHidden) els.btnResetHidden.addEventListener('click', function(){ post('misc_hide_clear', 1); });

  // Theming hook for custom user themes
  (function(){
    var dyn=document.createElement('style'); dyn.id='dyn-themes'; document.head.appendChild(dyn);
    fetch('/api/themes').then(function(r){return r.text();}).then(function(t){
      var arr=[]; try{ arr=JSON.parse(t||'[]'); }catch(e){}
      var css='';
      for (var i=0;i<arr.length;i++){
        var th=arr[i]; if (!th || !th.id || !th.vars) continue;
        var cls='body.'+th.id; css+=cls+'{';
        for (var k in th.vars){ if (Object.prototype.hasOwnProperty.call(th.vars,k)){ css+=k+':'+th.vars[k]+';'; } }
        css+='}';
        var opt=document.createElement('option'); opt.value=th.id; opt.textContent=th.label||th.id.replace(/^theme-/,''); els.theme.appendChild(opt);
      }
      dyn.textContent=css;
    }).catch(function(){});
  })();

  // Draggable panels
  (function(){
    var dragEl = null;
    els.hud.addEventListener('dragstart', function(e){
      var t = e.target;
      if (t && t.classList && t.classList.contains('card')) {
        dragEl = t;
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', dragEl.id);
        setTimeout(function(){ dragEl.classList.add('dragging'); }, 0);
      }
    });
    els.hud.addEventListener('dragend', function(){
      if (dragEl) {
        dragEl.classList.remove('dragging');
        dragEl = null;
        var order = Array.prototype.slice.call(els.hud.children).map(function(c){return c.id.replace('card-','')}).join(',');
        post('panel_order_set', order);
      }
    });
    els.hud.addEventListener('dragover', function(e){
      e.preventDefault();
      var target = e.target && e.target.closest ? e.target.closest('.card') : null;
      if (target && target !== dragEl) {
        var rect = target.getBoundingClientRect();
        var next = (e.clientY - rect.top) / rect.height > 0.5;
        els.hud.insertBefore(dragEl, next ? target.nextSibling : target);
      }
    });
  })();

  function labelFor(n){
    var map={'TwoHandedEdged':'2HE','OneHandedEdged':'1HE','TwoHandedBlunt':'2HB','OneHandedBlunt':'1HB','MeleeEnergy':'ME','RangedEnergy':'RE','FullAuto':'Full Auto','FlingShot':'Fling','AimedShot':'Aimed Shot','ComputerLiteracy':'Comp Lit','NanoCInit':'Nano Init','NanoProg':'Nano Programming','BodyDev':'Body Dev','DuckExp':'Duck-Exp','DodgeRanged':'Dodge Ranged','EvadeClsC':'Evade Close','AddAllOff':'AAO','AddAllDef':'AAD','CriticalIncrease':'Crit+','XPModifier':'XP %','MatterCreation':'MC','MatterMetamorphosis':'MM','BiologicalMetamorphosis':'BM','TimeAndSpace':'TS','PsychologicalModification':'PM','SensoryImprovement':'SI','SubMachineGun':'SMG','NanoResist':'Nano Resist','FirstAid':'First Aid','HealDelta':'Heal Delta','NanoDelta':'Nano Delta','Treatment':'Treatment'};
    if (map[n]) return map[n];
    return n.replace(/([a-z])([A-Z])/g,'$1 $2');
  }

  // DPI-safe canvas setup
  function setupCanvas(canvas, cssSize){
    var dpr = window.devicePixelRatio || 1;
    canvas.style.width = cssSize + 'px';
    canvas.style.height = cssSize + 'px';
    canvas.width  = Math.round(cssSize * dpr);
    canvas.height = Math.round(cssSize * dpr);
    var ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    return ctx;
  }
  var hpCtx = setupCanvas(els.hpCanvas, 160);
  var nanoCtx = setupCanvas(els.nanoCanvas, 110);

  var serverHidden = {}; // name -> 1 (from config)
  function makeItem(name, val, withCloser){
    var div=document.createElement('div'); div.className='kv'; div.dataset.name=name;
    var left=document.createElement('span'); left.className='name'; left.textContent=labelFor(name);
    var right=document.createElement('span'); right.className='val numeric'; right.textContent=val|0;
    div.appendChild(left); div.appendChild(right);
    if (withCloser){
      var x=document.createElement('span'); x.className='closer'; x.textContent='×'; x.title='Hide';
      x.addEventListener('click', function(ev){ ev.stopPropagation(); post('misc_hide_add', name); });
      div.appendChild(x);
    }
    div.addEventListener('click', function(){
      var n=this.dataset.name; var pinned=this.classList.contains('pinned');
      if (pinned){ post('pin_remove', n); this.classList.remove('pinned'); }
      else { post('pin_add', n); this.classList.add('pinned'); }
    });
    return div;
  }

  var GROUPS = [];
  var miscListEl = null;

  function buildGroups(allNames, allMap){
    var assigned = {}; for (var g=0;g<GROUPS.length;g++){ for (var k=0;k<GROUPS[g].Stats.length;k++){ assigned[GROUPS[g].Stats[k]] = 1; } }
    var misc = []; for (var i=0;i<allNames.length;i++){ var n=allNames[i]; if (!assigned[n]) misc.push(n); }

    for (var i=0; i<GROUPS.length; i++){
        var group = GROUPS[i];
        var host = document.getElementById('list-' + group.Id);
        if (!host) continue;
        host.innerHTML='';
        var frag=document.createDocumentFragment();
        for(var j=0; j<group.Stats.length; j++) { var n=group.Stats[j]; if (allMap[n]!=null) frag.appendChild(makeItem(n, allMap[n], false)); }
        host.appendChild(frag);
    }
    
    var showHidden = !!els.showHidden && !!els.showHidden.checked;
    if (!miscListEl) miscListEl = document.getElementById('list-misc');
    if (miscListEl) {
        miscListEl.innerHTML='';
        var frag=document.createDocumentFragment();
        for(var j=0; j<misc.length; j++){
          var n=misc[j];
          if (allMap[n]==null) continue;
          var hidden = !!serverHidden[n];
          if (!showHidden && hidden) continue;
          var div=makeItem(n, allMap[n], true);
          if (hidden){
            div.classList.add('hidden');
            var r=document.createElement('span'); r.className='closer'; r.textContent='↺'; r.title='Unhide';
            (function(name,el){ r.addEventListener('click', function(ev){ ev.stopPropagation(); post('misc_hide_remove', name); }); })(n,div);
            div.appendChild(r);
          }
          frag.appendChild(div);
        }
        miscListEl.appendChild(frag);
    }

    built=true;
    builtKey = allNames.join('|') + '|' + (showHidden ? '1':'0') + '|' + Object.keys(serverHidden).sort().join('|');
    applyFilter(true);
  }

  function applyFilter(fromBuild){
    var q=(els.filter.value||'').toLowerCase();
    var allLists = document.querySelectorAll('.kvlist');
    for(var l=0; l<allLists.length; l++){
      var list = allLists[l];
      var items=list.children, anyShown=false;
      for (var i=0;i<items.length;i++){
        var n = items[i].dataset.name;
        var lab = labelFor(n).toLowerCase();
        var show = (q==='') || (n.toLowerCase().indexOf(q)>=0) || (lab.indexOf(q)>=0);
        items[i].style.display = show ? '' : 'none';
        if (show) anyShown = true;
      }
      var details = list.parentElement;
      if (q!=='' && details.tagName === 'DETAILS'){
        details.open = anyShown;
      }
    }
  }
  els.filter.addEventListener('input', function(){ applyFilter(false); });
  if(els.showHidden) els.showHidden.addEventListener('change', function(){ built=false; });


  function connect(){
    if (es) es.close();
    es=new EventSource('/events');
    es.onopen=function(){ els.status.textContent='live'; };
    es.onerror=function(){ els.status.textContent='reconnecting…'; };
    es.onmessage=function(ev){ try{ render(JSON.parse(ev.data)); }catch(e){} };
  }

  function nz(v){ return v!=null && v!==0 && v!==12345678 && v!==1234567890; }

  // Animated Gauges
  var hpGauge = { ctx: hpCtx, current: 0, target: 0 };
  var nanoGauge = { ctx: nanoCtx, current: 0, target: 0 };

  function drawGauge(gauge, pct, now, max, label, color) {
    var ctx = gauge.ctx, dpr = window.devicePixelRatio || 1;
    var w = ctx.canvas.width/dpr, h = ctx.canvas.height/dpr;
    var center = w/2, radius = w/2-10, lineWidth=12;
    if (w < 120) { radius = w/2-6; lineWidth=8; } // smaller nano gauge
    
    var start = -Math.PI / 2, end = start + (pct * 2 * Math.PI);
    
    ctx.clearRect(0,0,w,h);
    
    ctx.beginPath();
    ctx.arc(center, center, radius, 0, 2*Math.PI);
    ctx.strokeStyle = 'rgba(128,128,128,0.1)';
    ctx.lineWidth = lineWidth;
    ctx.stroke();

    if (pct > 0.001) {
      ctx.beginPath();
      ctx.arc(center, center, radius, start, end);
      ctx.strokeStyle = color;
      ctx.lineCap = 'round';
      ctx.stroke();
    }

    ctx.fillStyle = 'var(--fg)';
    ctx.font = 'bold ' + (w>120?18:16) + 'px ' + getComputedStyle(document.body).fontFamily;
    ctx.textAlign = 'center';
    ctx.fillText(now, center, h/2 - (w>120?5:2));

    ctx.fillStyle = 'var(--muted)';
    ctx.font = '12px ' + getComputedStyle(document.body).fontFamily;
    ctx.fillText(label, center, h/2 + 15);
  }

  var animHandle = 0;
  function animLoop() {
      animHandle = 0;
      var changed = false;
      var styles = getComputedStyle(document.body);
      
      if (Math.abs(hpGauge.target - hpGauge.current) > 0.0001) {
          hpGauge.current += (hpGauge.target - hpGauge.current) * 0.1;
          changed = true;
      } else { hpGauge.current = hpGauge.target; }
      drawGauge(hpGauge, hpGauge.current, hpGauge.now, hpGauge.max, 'HP', styles.getPropertyValue('--hp-color').trim());

      if (Math.abs(nanoGauge.target - nanoGauge.current) > 0.0001) {
          nanoGauge.current += (nanoGauge.target - nanoGauge.current) * 0.1;
          changed = true;
      } else { nanoGauge.current = nanoGauge.target; }
      drawGauge(nanoGauge, nanoGauge.current, nanoGauge.now, nanoGauge.max, 'Nano', styles.getPropertyValue('--nano-color').trim());
      
      if (changed) {
          animHandle = requestAnimationFrame(animLoop);
      }
  }

  var lastPinVals = {};
  var currentPanelOrder = [];
  var redrawGaugesTimer = 0;
  function redrawGaugesSoon(){
    clearTimeout(redrawGaugesTimer);
    redrawGaugesTimer = setTimeout(function(){ if(!animHandle) animLoop(); }, 50);
  }

  var firstRender = true;
  function render(d){
    if(firstRender) {
        fetch('/api/groups').then(function(r){return r.json()}).then(function(groups){
            GROUPS = groups;
            var container = els.groupsContainer;
            container.innerHTML = '';
            for(var i=0; i<groups.length; i++) {
                var group = groups[i];
                var details = document.createElement('details');
                details.id = 'g-' + group.Id;
                var summary = document.createElement('summary');
                summary.textContent = group.Label;
                var div = document.createElement('div');
                div.className = 'kvlist';
                div.id = 'list-' + group.Id;
                details.appendChild(summary);
                details.appendChild(div);
                container.appendChild(details);
            }
            firstRender = false;
            render(d); // re-render with groups loaded
        });
        return;
    }

    if (d.settings){
      var s=d.settings;
      var newClassName = s.theme || 'theme-aetherium';
      if (s.compact) { newClassName += ' compact'; }
      if (document.body.className !== newClassName) {
          document.body.className = newClassName;
          redrawGaugesSoon();
      }
      if (els.theme.value!==s.theme) els.theme.value = s.theme;
      els.compact.checked = !!s.compact;
      var im = s.interval_ms|0;
      if ((els.interval.value|0)!==im) els.interval.value = im;
      syncRateButtons(im);
      els.toggle.checked = !!s.enabled;
      if (s.panelOrder && s.panelOrder.join(',') !== currentPanelOrder.join(',')) {
          currentPanelOrder = s.panelOrder;
          currentPanelOrder.forEach(function(cardId) {
              var card = document.getElementById('card-' + cardId);
              if (card) els.hud.appendChild(card);
          });
      }
    }

    var c=d.core||{};
    var coreStats = {
      'aao': nz(c.AddAllOff) ? c.AddAllOff : '–',
      'aad': nz(c.AddAllDef) ? c.AddAllDef : '–',
      'crit': nz(c.CriticalIncrease) ? c.CriticalIncrease : '–',
      'xp': nz(c.XPModifier) ? (c.XPModifier+'%') : '–'
    };
    els.aao.textContent = coreStats.aao; els.aad.textContent = coreStats.aad;
    els.crit.textContent = coreStats.crit; els.xpmod.textContent = coreStats.xp;
    els.dockAao.textContent = coreStats.aao; els.dockAad.textContent = coreStats.aad;
    els.dockCrit.textContent = coreStats.crit; els.dockXp.textContent = coreStats.xp;

    var hp=d.hp||{now:0,max:1,pct:0};
    var np=d.nano||{now:0,max:1,pct:0};
    hpGauge.target = hp.pct / 100; hpGauge.now = hp.now; hpGauge.max = hp.max;
    nanoGauge.target = np.pct / 100; nanoGauge.now = np.now; nanoGauge.max = np.max;
    if (!animHandle) animLoop();

    els.dmgchips.innerHTML=''; var dm=d.dmg||{}, keys=Object.keys(dm).sort();
    for (var i=0;i<keys.length;i++){ var k=keys[i], v=dm[k]|0; if (!nz(v)) continue; var chip=document.createElement('div'); chip.className='chip numeric'; chip.textContent=k.replace('DamageModifier','')+': '+v; els.dmgchips.appendChild(chip); }

    els.acs.innerHTML=''; var ac=d.ac||{}, ack=Object.keys(ac).sort();
    for (var j=0;j<ack.length;j++){ var k2=ack[j], v2=ac[k2]|0; if (!nz(v2)) continue; var row=document.createElement('div'); row.className='ac'; var b=document.createElement('b'); b.textContent=k2.replace('AC',''); var sp=document.createElement('span'); sp.className='numeric'; sp.textContent=v2; row.appendChild(b); row.appendChild(sp); els.acs.appendChild(row); }

    var all=d.all||{}, names=d.all_names||[];
    var hid = d.hiddenMisc||[]; serverHidden = {}; for (var h=0; h<hid.length; h++) serverHidden[hid[h]] = 1;
    var buildKey = names.join('|') + '|' + (els.showHidden && els.showHidden.checked ? '1':'0') + '|' + Object.keys(serverHidden).sort().join('|');
    if (!built || buildKey!==builtKey) buildGroups(names, all);

    var allLists = document.querySelectorAll('.kvlist');
    for(var l=0; l<allLists.length; l++){
      var list = allLists[l];
      var items=list.children;
      for (var i=0;i<items.length;i++){
        var name=items[i].dataset.name;
        items[i].querySelector('.val').textContent = all[name]|0;
      }
    }

    var pins = d.pins||[];
    els.pins.innerHTML= pins.length ? '' : 'None pinned yet. Use the groups to pin.';
    var pinSet={}; for (var pi=0;pi<pins.length;pi++){ if (nz(pins[pi].v)) pinSet[pins[pi].name]=1; }
    for(l=0; l<allLists.length; l++){
      var list=allLists[l], items=list.children;
      for (var i=0;i<items.length;i++){
        var n=items[i].dataset.name;
        items[i].classList.toggle('pinned', !!pinSet[n]);
      }
    }
    for (var q=0;q<pins.length;q++){
      var pin=pins[q]; if (!nz(pin.v)) continue;
      var div=document.createElement('div'); div.className='pin';
      div.innerHTML='<b>'+pin.label+'</b> <span class="val numeric">'+(pin.v|0)+'</span> <span class="x" title="Unpin">×</span>';
      (function(name,el){ el.querySelector('.x').addEventListener('click', function(){ post('pin_remove', name); }); })(pin.name,div);
      if (lastPinVals.hasOwnProperty(pin.name) && (lastPinVals[pin.name]|0)!==(pin.v|0)){ div.classList.add('changed'); setTimeout((function(d){ return function(){ d.classList.remove('changed'); }; })(div), 500); }
      lastPinVals[pin.name] = pin.v|0;
      els.pins.appendChild(div);
    }
    var currentPins = {}; pins.forEach(function(p){ currentPins[p.name]=p.v; });
    Object.keys(lastPinVals).forEach(function(k){ if (!currentPins.hasOwnProperty(k)) delete lastPinVals[k]; });

    if ((els.filter.value||'')!=='') applyFilter(false);

    els.status.textContent='live';
  }

  connect();
})();