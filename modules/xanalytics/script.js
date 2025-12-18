(function(){
  const short=(s,n=120)=>{if(s==null)return"";s=String(s);return s.length<=n?s:s.slice(0,n-1)+'…'};
  const pad=(s,w)=>{s=s==null?'':String(s);if(s.length>=w)return s.slice(0,w);return s+' '.repeat(w-s.length)};
  const htmlDecode=s=>{const t=document.createElement('textarea');t.innerHTML=s;return t.value};
  const esc=s=>String(s).replace(/[&<>"']/g,m=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));
  const fmt=n=>Number(n||0).toLocaleString();
  const toNum=s=>{if(s==null)return 0;if(typeof s==='number')return Math.floor(s);return parseInt(String(s).replace(/[,_\s]/g,''),10)||0};
  const now=()=>Date.now();
  function gid(id){return document.getElementById(id)}

  // UI refs
  const styleSel=gid('styleSel');
  const btnAttach = gid('btnAttach');
  const btnInfo   = gid('btnInfo');
  const btnReset  = gid('btnReset');
  const btnSavedLogs = gid('btnSavedLogs');

  const toggleParsed = gid('toggleParsed');
  const toggleRaw = gid('toggleRaw');
  const toggleDebug = gid('toggleDebug');

  const fileInfo = gid('fileInfo');
  const cutoffLabel = gid('cutoffLabel');
  const modeLabel = gid('modeLabel');
  const pathLabel = gid('pathLabel');

  const parsedLog = gid('parsedLog');
  const rawPane = gid('rawPane');
  const debugPane = gid('debugPane');
  const tableEl = gid('table');
  const metricsInline = gid('metricsInline');
  const dot = gid('dot');

  const inclDiscardedChk = gid('inclDiscarded');
  const normalizeVisibleChk = gid('normalizeVisible');
  const dimDiscardedOnlyChk = gid('dimDiscardedOnly');
  const combineQLChk = gid('combineQL');

  const eventSortBySel = gid('eventSortBy');
  const eventSortDirSel = gid('eventSortDir');
  const eventSearchInp = gid('eventSearch');

  const viewAll=gid('viewAll');
  const viewKept=gid('viewKept');
  const viewDiscarded=gid('viewDiscarded');

  const sumLine=gid('sumLine');

  const fxp=gid('fxp'), faxp=gid('faxp'), fsk=gid('fsk'), frxp=gid('frxp');

  const exportStateLink = gid('exportStateLink');
  const exportCSVLink   = gid('exportCSVLink');

  // XP cards / bars / numbers
  const xpd={
    total:{xp:gid('xpTotal'),axp:gid('axpTotal'),sk:gid('skTotal'),rxp:gid('rxpTotal')},
    rate10:{xp:gid('xpRate10'),axp:gid('axpRate10'),sk:gid('skRate10'),rxp:gid('rxpRate10')},
    rateS:{xp:gid('xpRateSess'),axp:gid('axpRateSess'),sk:gid('skRateSess'),rxp:gid('rxpRateSess')},
    last:{xp:gid('xpLast'),axp:gid('axpLast'),sk:gid('skLast'),rxp:gid('rxpLast')},

    need:{xp:gid('xpNeed'),axp:gid('axpNeed'),sk:gid('skNeed')},
    needL:{xp:gid('xpNeedLabel'),axp:gid('axpNeedLabel'),sk:gid('skNeedLabel')},
    eta:{xp:gid('xpETA'),axp:gid('axpETA'),sk:gid('skETA')},
    bar:{xp:gid('barXP'),axp:gid('barAXP'),sk:gid('barSK')},

    uptime:gid('xpUptime'),
    events:gid('xpEvents'),
    feed:gid('xpFeed')
  };

  const xpResetBtn = gid('xpResetBtn');
  const axpResetBtn = gid('axpResetBtn');
  const skResetBtn = gid('skResetBtn');

  const infoPanel = gid('infoPanel');
  const infoCloseBtn = gid('infoCloseBtn');
  const infoTabBtns = Array.from(document.querySelectorAll('.infoTabBtn'));
  const infoTabs = {
    howto: gid('tab-howto'),
    notes: gid('tab-notes')
  };

  // Saved logs modal dispatch
  btnSavedLogs.addEventListener('click',()=>{
    // Let progress.js open the modal and render list
    document.dispatchEvent(new CustomEvent('xan-open-savedlogs'));
  });

  // Session / state
  const state={
    fileHandle:null,
    fileObj:null,
    offset:0,
    tailTimer:null,
    reading:false,
    paused:false,
    startTs:0
  };

  let linesRead=0,parseHits=0,parseMisses=0,rotationCount=0,readErrors=0,totalKept=0,totalDiscarded=0;

  const stats={};          // Loot stats by item key
  const debugLog=[];       // rolling debug
  const rawLines=[];       // rolling raw tail
  const parsedLines=[];    // rolling "parsed" lines (xp+loot that actually affected stats/xp)

  let eventView='all';

  // Which XP pools are shown / counted in feed + UI
  const xpInclude={xp:true,axp:true,sk:true,rxp:true};

  // xpState tracks XP/AXP/SK/Research across the session
  const xpState={
    start:null,
    totals:{xp:0,axp:0,sk:0,rxp:0},
    last:{xp:0,axp:0,sk:0,rxp:0},
    events:0,
    samples:{xp:[],axp:[],sk:[],rxp:[]}, // 10 min rolling window
    sess:{xp:0,axp:0,sk:0,rxp:0},        // total this session
    goals:{xp:0,axp:0,sk:0},             // user-entered "I need this much"
    goalStart:{
      xp:{t:null,totalAtStart:0},
      axp:{t:null,totalAtStart:0},
      sk:{t:null,totalAtStart:0}
    },
    // placeholders for future ding ETA logic from progress.js
    playerLevel:null,
    levelProgress:0,
    levelNeeded:0
  };

  // regexes to read XP/SK/etc from AO logs

  // Side bonus XP lines, example:
  // ["#.."]181 xp was gained as a side bonus!
  // We REQUIRE "as a side bonus" so we don't swallow normal XP.
  const rxXPBonus=/\b([0-9][0-9,]*)\s+xp\s+was\s+gained\s+as\s+a\s+side\s+bonus/i;

  // Standard XP lines:
  // You gained 12345 XP
  // You received 12345 experience
  const rxXP=/You\s+(?:gained|received)\s+([\d,]+)\s+(?:XP|experience)\b(?!.*Alien)/i;

  // Alien XP:
  // You gained 150 Alien Experience Points.
  // We capture number BEFORE "Alien Experience Points".
  const rxAXP=/You\s+gained\s+([\d,]+)\s+Alien\s+Experience\s+Points/i;

  // Shadowknowledge (SK):
  // You gained 1279 points of Shadowknowledge.
  // You gained 500 SK.
  const rxSK=/(?:You\s+gained\s+)?([\d,]+)\s+(?:points\s+of\s+)?(?:Shadowknowledge|SK)\b/i;

  // Research XP:
  // 1234 of your XP were allocated to your personal research
  const rxRXP=/([\d,]+)\s+of\s+your\s+XP\s+were\s+allocated\s+to\s+your\s+personal\s+research/i;

  // Shadowlevel welcome line (SK level up)
  // ["#0000000040000001#","System","",1761627196]Welcome to Shadowlevel 207.
  const rxShadowWelcome=/Welcome\s+to\s+Shadowlevel\s+(\d+)\./i;

  // Loot parsing
  // AO puts itemref hyperlinks like:
  // <a href="itemref://high/low/ql">Item Name</a>
  // Sometimes high/low or ql segments are missing.
  const rxItemref=/<a\s+href\s*=\s*"itemref:\/\/(\d+)(?:\/(\d+))?(?:\/(\d+))?">(.*?)<\/a>/ig;

  // Phrases that indicate item action
  // We want to know if it was looted/received/etc vs deleted
  const rxAction=/(?:^|\W)(deleted|looted|picked up|picked|received|acquired|added to your inventory|added)(?:\W|$)/i;
  const rxLoose=/\b(deleted|looted|picked up|picked|received|acquired|added)\b/i;

  // Try to pull a source like "from Some Mob"
  const rxSource=/\bfrom\s+([A-Za-z0-9 '\-\.]{2,80})/i;

  function rarityOf(p){
    if(p>=5)return'Common';
    if(p>=1)return'Uncommon';
    if(p>=0.2)return'Rare';
    return'Epic';
  }

  // Debug log: cap memory
  function pushDebug(s){
    const t=`[${new Date().toISOString()}] ${s}`;
    debugLog.push(t);
    if (debugLog.length > 2000) {
      debugLog.splice(0, debugLog.length - 2000);
    }
    if(debugPane.style.display!=='none'){
      const pre=debugPane.querySelector('pre')||document.createElement('pre');
      pre.textContent=debugLog.join('\n');
      debugPane.innerHTML='';
      debugPane.appendChild(pre);
      debugPane.scrollTop=debugPane.scrollHeight;
    }
  }

  // Raw log tail buffer: cap memory
  function pushRaw(ln){
    rawLines.push(ln);
    if (rawLines.length > 2000) {
      rawLines.splice(0, rawLines.length - 2000);
    }
    if(rawPane.style.display!=='none'){
      const pre=rawPane.querySelector('pre');
      pre.textContent=rawLines.slice(-200).join('\n');
      rawPane.scrollTop=rawPane.scrollHeight;
    }
  }

  // Parsed (xp+loot) buffer: cap memory
  function pushParsedLineBuf(tag,text){
    const lineText = `[${new Date().toLocaleTimeString()}] ${tag.toUpperCase()} ${text}`;
    parsedLines.push(lineText);
    if(parsedLines.length>2000){
        parsedLines.splice(0,parsedLines.length-2000);
    }
  }

  // Render a single parsed event into the live Parsed Log pane
  function appendParsedDOM(tag,text){
    if (parsedLog.style.display === 'none') return;
    const line=document.createElement('div'); line.className='line';
    const pill=document.createElement('span'); pill.className='pill '+tag; pill.textContent=tag.toUpperCase();
    const span=document.createElement('span'); span.textContent=' '+text;
    line.appendChild(pill); line.appendChild(span);
    parsedLog.appendChild(line);
    while(parsedLog.childNodes.length > 300) {
      parsedLog.removeChild(parsedLog.firstChild);
    }
    parsedLog.scrollTop=parsedLog.scrollHeight;
  }

  // High-level helper: we always buffer; we only live-render if pane visible
  function pushParsed(tag,text){
    pushParsedLineBuf(tag,text);
    if(parsedLog.style.display!=='none'){
      appendParsedDOM(tag,text);
    }
  }

  // When user clicks "Show Parsed Log", rebuild from buffer
  function rebuildParsedDOM(){
    parsedLog.innerHTML='';
    const recent = parsedLines.slice(-300);
    for(const pl of recent){
      // pl looks like: "[HH:MM:SS AM] TAG text..."
      const m = pl.match(/^\[(.*?)\]\s+([A-Z]+)\s+(.*)$/);
      const line=document.createElement('div');
      line.className='line';

      const pill=document.createElement('span');
      if(m){
        pill.className='pill '+m[2].toLowerCase();
        pill.textContent=m[2];
      }else{
        pill.className='pill xp';
        pill.textContent='LOG';
      }

      const span=document.createElement('span');
      span.textContent=' '+(m?m[3]:pl);

      line.appendChild(pill);
      line.appendChild(span);
      parsedLog.appendChild(line);
    }
    parsedLog.scrollTop=parsedLog.scrollHeight;
  }

  // ===== XP tracking / rates / ETA logic =====

  // Push an XP / AXP / SK / RXP gain into xpState
  function xpPush(key,val){
    const t=now();
    xpState.totals[key]+=val;
    xpState.last[key]=val;
    xpState.events++;
    xpState.samples[key].push({t,v:val});
    xpState.sess[key]+=val;

    // Keep only last 10 minutes of samples for each XP type
    const cut=t-10*60*1000;
    ['xp','axp','sk','rxp'].forEach(k=>{
      const a=xpState.samples[k];
      while(a.length && a[0].t<cut) a.shift();
    });

    refreshXPUI();
    pushXPFeed(key,val,t);
  }

  // Rolling per-hour rate from last ~10 minutes of samples
  function rate10(k){
    const a=xpState.samples[k];
    if(!a.length) return 0;
    const sum=a.reduce((x,y)=>x+y.v,0);
    const span=(a[a.length-1].t-a[0].t)/1000||1;
    return sum/span*3600;
  }

  // Session-wide per-hour rate from session start
  function rateSess(k){
    if(!xpState.start)return 0;
    const h=(now()-xpState.start)/3600000;
    return h>0?xpState.sess[k]/h:0;
  }

  function resetGoalTimer(kind){
    xpState.goalStart[kind].t = now();
    xpState.goalStart[kind].totalAtStart = xpState.totals[kind];
  }

  // Build ETA text and progress string using baseline + rolling 10-minute rate.
  function etaFor(kind){
    const goal = xpState.goals[kind];
    if(!goal || goal<=0){
      return {eta:'ETA —', pctStr:'—', curGain:0};
    }
    const gs = xpState.goalStart[kind];
    if(!gs.t){
      // first time we ever look at this stat since load: baseline now
      resetGoalTimer(kind);
    }
    const gained = xpState.totals[kind]-gs.totalAtStart;
    const pctDone = goal>0 ? (Math.min(gained/goal,1)*100) : 0;
    const pctStr = `${fmt(Math.max(gained,0))} / ${fmt(goal)} (${pctDone.toFixed(1)}%)`;

    const rph = rate10(kind); // per-hour from last 10m
    if(rph<=0){
      return {eta:'ETA —', pctStr, curGain:gained};
    }
    const remain = Math.max(goal-gained,0);
    if(remain===0){
      return {eta:'ETA — 0m', pctStr, curGain:gained};
    }
    const hoursLeft = remain / rph;
    const h=Math.floor(hoursLeft);
    const m=Math.round((hoursLeft-h)*60);
    const eta=`ETA — ${h? h+'h ' : ''}${m}m`;
    return {eta,pctStr,curGain:gained};
  }

  // Update progress bar fills
  function setBar(el,need,have){
    const N=toNum(need),H=toNum(have);
    if(!N){
      el.style.width='0%';
      return;
    }
    const pct=Math.max(0,Math.min(100,H/N*100));
    el.style.width=pct.toFixed(2)+'%';
  }

  // Refresh all XP/AXP/SK/RXP UI elements
  function refreshXPUI(){
    // basic totals / last / rates
    ['xp','axp','sk','rxp'].forEach(k=>{
      if(xpd.total[k]) xpd.total[k].textContent=fmt(xpState.totals[k]);
      if(xpd.last[k]) xpd.last[k].textContent=fmt(xpState.last[k]);
      if(xpd.rate10[k]) xpd.rate10[k].textContent=fmt(Math.round(rate10(k)))+'/hr';
      if(xpd.rateS[k]) xpd.rateS[k].textContent=fmt(Math.round(rateSess(k)))+'/hr';
    });

    // XP goal card
    {
      const d = etaFor('xp');
      setBar(xpd.bar.xp, xpState.goals.xp, d.curGain);
      xpd.needL.xp.textContent = d.pctStr;
      xpd.eta.xp.textContent   = d.eta;
    }
    // AXP goal
    {
      const d = etaFor('axp');
      setBar(xpd.bar.axp, xpState.goals.axp, d.curGain);
      xpd.needL.axp.textContent = d.pctStr;
      xpd.eta.axp.textContent   = d.eta;
    }
    // SK goal
    {
      const d = etaFor('sk');
      setBar(xpd.bar.sk, xpState.goals.sk, d.curGain);
      xpd.needL.sk.textContent = d.pctStr;
      xpd.eta.sk.textContent   = d.eta;
    }

    // uptime + events
    if(!xpState.start){
      xpd.uptime.textContent='Uptime — 0m';
    }else{
      const mins=Math.floor((now()-xpState.start)/60000);
      xpd.uptime.textContent=`Uptime — ${mins}m`;
    }
    xpd.events.textContent=`XP events — ${fmt(xpState.events)}`;
  }

  // Reset whole XP session (on new tail / Reset button)
  function xpReset(){
    xpState.start=new Date();
    xpState.totals={xp:0,axp:0,sk:0,rxp:0};
    xpState.last={xp:0,axp:0,sk:0,rxp:0};
    xpState.events=0;
    xpState.samples={xp:[],axp:[],sk:[],rxp:[]};
    xpState.sess={xp:0,axp:0,sk:0,rxp:0};

    xpState.goals.xp=toNum(xpd.need.xp.value);
    xpState.goals.axp=toNum(xpd.need.axp.value);
    xpState.goals.sk=toNum(xpd.need.sk.value);

    xpState.goalStart={
      xp:{t:now(),totalAtStart:0},
      axp:{t:now(),totalAtStart:0},
      sk:{t:now(),totalAtStart:0}
    };

    xpState.playerLevel=null;
    xpState.levelProgress=0;
    xpState.levelNeeded=0;

    refreshXPUI();
    xpd.feed.innerHTML='<div class="small dim">(XP feed populates as you tail)</div>';
  }

  // XP feed (recent XP ticks scrollback)
  function pushXPFeed(type,val,t){
    if(!xpInclude[type]) return;
    const row=document.createElement('div');
    row.className='line';

    const pill=document.createElement('span');
    pill.className='pill '+type;
    pill.textContent=type.toUpperCase();

    const amt=document.createElement('span');
    amt.textContent=' +'+fmt(val);

    const when=document.createElement('span');
    when.className='when';
    when.textContent=new Date(t).toLocaleTimeString();

    row.appendChild(pill);
    row.appendChild(amt);
    row.appendChild(when);

    xpd.feed.prepend(row);
    while(xpd.feed.childNodes.length>40)
      xpd.feed.removeChild(xpd.feed.lastChild);
  }

  // ===== Loot stats tracking =====

  // Create a stable-ish key for an item
  function makeKey(h,l,n){
    if((!h||h===0)&&(!l||l===0)){
      let x=0;
      for(let i=0;i<n.length;i++){
        x=((x<<5)-x)+n.charCodeAt(i);
        x=x&x;
      }
      return 'N:'+Math.abs(x)+':'+n;
    }
    return h+':'+l+':'+n;
  }

  // Upsert loot stats for this item
  function upsert(ev){
    const key=makeKey(ev.high,ev.low,ev.name);
    let s=stats[key];
    if(!s) s=stats[key]={
      key,
      name:ev.name,high:ev.high,low:ev.low,
      events:0,kept:0,discarded:0,
      minql:99999,maxql:0,sampleqls:[],
      lastSource:null,lastSeen:null
    };

    if(ev.ql&&ev.ql>0){
      if(ev.ql<s.minql)s.minql=ev.ql;
      if(ev.ql>s.maxql)s.maxql=ev.ql;
      if(s.sampleqls.length<6 && !s.sampleqls.includes(ev.ql))s.sampleqls.push(ev.ql);
    }

    if(ev.isKept){s.kept++; totalKept++;}
    if(ev.isDiscarded){s.discarded++; totalDiscarded++;}

    s.events=s.kept+s.discarded;
    s.lastSource=ev.source||s.lastSource;
    s.lastSeen=new Date().toISOString();
  }

  // Build the visible array of stat rows (optionally merged across QLs)
  function buildStatArray() {
    const arr = Object.values(stats);

    if (!combineQLChk.checked) {
      return arr.slice();
    }

    // Merge all QLs of same item name
    const merged = {};
    for (const s of arr) {
      const nm = s.name || '(unknown)';
      let M = merged[nm];
      if (!M) {
        M = merged[nm] = {
          key: nm,
          name: nm,
          events: 0,
          kept: 0,
          discarded: 0,
          minql: 99999,
          maxql: 0,
          sampleqls: [],
          lastSource: null,
          lastSeen: null
        };
      }

      M.kept      += s.kept||0;
      M.discarded += s.discarded||0;
      M.events     = M.kept + M.discarded;

      if (s.minql && s.minql < M.minql) M.minql = s.minql;
      if (s.maxql && s.maxql > M.maxql) M.maxql = s.maxql;

      for (const q of (s.sampleqls||[])) {
        if (M.sampleqls.length < 6 && !M.sampleqls.includes(q)) {
          M.sampleqls.push(q);
        }
      }

      if (!M.lastSeen || (s.lastSeen && Date.parse(s.lastSeen) > Date.parse(M.lastSeen||0))) {
        M.lastSeen = s.lastSeen;
        M.lastSource = s.lastSource;
      }
    }

    return Object.values(merged);
  }

  // Render loot stats table
  function renderTable(){
    const includeDiscardedInPct=inclDiscardedChk.checked;
    let pctBase=(includeDiscardedInPct?(totalKept+totalDiscarded):totalKept)||1;

    const q=(eventSearchInp.value||'').toLowerCase().trim();

    let arr=buildStatArray();

    // compute per-row derived fields
    arr.forEach(s=>{
      s.events=s.kept+s.discarded;
      s.percent=100*s.events/pctBase;
      s.rarity=rarityOf(s.percent);
      s.discardedOnly=(s.kept===0&&s.discarded>0);
      s.keptOnly=(s.discarded===0&&s.kept>0);
    });

    // view filter:
    // "all": show everything
    // "kept": only items we actually kept at least once
    // "discarded": anything we ever deleted (as requested)
    if(eventView==='kept') arr=arr.filter(s=>s.kept>0);
    if(eventView==='discarded') arr=arr.filter(s=>s.discarded>0);

    // search filter (by name or lastSource)
    if(q)
      arr=arr.filter(s=>(s.name||'').toLowerCase().includes(q)||
                        (s.lastSource||'').toLowerCase().includes(q));

    // visibleBase for % if normalizeVisible is checked
    const visibleBase = normalizeVisibleChk.checked
      ? Math.max(1, arr.reduce((a,s)=>a+s.events,0))
      : pctBase;

    // sort
    const sortBy=eventSortBySel.value;
    const dir=eventSortDirSel.value==='asc'?1:-1;

    arr.sort((a,b)=>{
      const K={
        events:[a.events,b.events],
        percent:[a.events/visibleBase,b.events/visibleBase],
        kept:[a.kept,b.kept],
        discarded:[a.discarded,b.discarded],
        name:[a.name||'',b.name||''],
        last:[a.lastSeen?Date.parse(a.lastSeen):0,
              b.lastSeen?Date.parse(b.lastSeen):0]
      }[sortBy];

      if(sortBy==='name')
        return K[0].localeCompare(K[1])*dir;

      if(K[0]===K[1])
        return (a.name||'').localeCompare(b.name||'')*dir;

      return (K[0]<K[1]?-1:1)*dir;
    });

    // Build text table
    const H={
      name:36,events:7,kept:8,discarded:8,
      pct:7,rarity:10,ql:12,src:26
    };

    const hdr=
      pad('Name',H.name)+
      pad('Events',H.events)+
      pad('Kept',H.kept)+
      pad('Discarded',H.discarded)+
      pad('%',H.pct)+
      pad('Rarity',H.rarity)+
      pad('QL',H.ql)+
      ' Last Source | Last\n';

    const sep='-'.repeat(H.name)+' '+
      '-'.repeat(H.events-1)+' '+
      '-'.repeat(H.kept-1)+' '+
      '-'.repeat(H.discarded-1)+' '+
      '-'.repeat(H.pct-1)+' '+
      '-'.repeat(H.rarity-1)+' '+
      '-'.repeat(H.ql-1)+' '+
      '-'.repeat(H.src)+' '+'-----\n';

    let html=`<pre class="mono">${esc(hdr+sep)}`;

    let sumPct=0;
    if(!arr.length){
      html+=esc('(no rows match filters)\n');
    } else {
      const dim=dimDiscardedOnlyChk.checked;
      for(const s of arr){
        const pct=(100*s.events/visibleBase);
        sumPct+=pct;

        const ql = (s.minql===99999 && !s.maxql)
          ? (s.sampleqls && s.sampleqls.length ? s.sampleqls[0] : '-')
          : (s.minql===s.maxql || s.maxql===0
              ? (s.minql===99999?'-':String(s.minql))
              : `${s.minql===99999?'-':s.minql}-${s.maxql}`);

        const row=
          pad((s.name||'').slice(0,H.name),H.name)+
          pad(fmt(s.events),H.events)+
          pad(fmt(s.kept),H.kept)+
          pad(fmt(s.discarded),H.discarded)+
          pad(pct.toFixed(2),H.pct)+
          pad(s.rarity,H.rarity)+
          pad(ql,H.ql)+
          pad((s.lastSource||'-').slice(0,H.src),H.src)+' '+
          (s.lastSeen?new Date(s.lastSeen).toLocaleTimeString():'-');

        html += (dim && s.discardedOnly)
          ? `<span class="dim">${esc(row)}</span>\n`
          : `${esc(row)}\n`;
      }
    }
    html+='</pre>';
    tableEl.innerHTML=html;

    sumLine.textContent = `Visible % sum: ${
      arr.length? (sumPct.toFixed(2)+'%'): '—'
    }`;
  }

  // ===== line parsing =====

  // for loot dedupe across "vicinity"/"team" spam
  const recentLootSeen=[];

  function seenLootSig(ev){
    const sig = ev.name+'|'+ev.ql+'|'+(ev.source||'')+'|'+Math.floor(Date.now()/1000);
    for(let i=recentLootSeen.length-1;i>=0;i--){
      if(recentLootSeen[i]===sig) return true;
    }
    recentLootSeen.push(sig);
    if(recentLootSeen.length>50){
      recentLootSeen.splice(0,recentLootSeen.length-50);
    }
    return false;
  }

  function parseLine(line){
    linesRead++;
    pushRaw(line);
    pushDebug(`LINE_RX: ${short(line,140)}`);

    let m=null;

    // Shadowlevel welcome -> track level (placeholder for later ding ETA)
    // ["#..."]Welcome to Shadowlevel 207.
    if((m=line.match(rxShadowWelcome))){
      const lvl=parseInt(m[1],10);
      if(lvl>0){
        xpState.playerLevel = lvl;
        xpState.levelProgress = 0;
        // xpState.levelNeeded stays 0 until we wire table in progress.js
        pushParsed('sk',`Shadowlevel ${lvl}`);
      }
    }

    // XP bonus line (side bonus). We do NOT use else-if so we can also parse normal XP
    if((m=line.match(rxXPBonus))){
      const v=toNum(m[1]);
      if(v){
        if(xpInclude.xp)pushParsed('xp','+'+fmt(v)+' XP (bonus)');
        xpPush('xp',v);
      }
    }

    // AXP
    if((m=line.match(rxAXP))){
      const v=toNum(m[1]);
      if(v){
        if(xpInclude.axp)pushParsed('axp','+'+fmt(v)+' AXP');
        xpPush('axp',v);
      }
    }

    // SK
    if((m=line.match(rxSK))){
      const v=toNum(m[1]);
      if(v){
        if(xpInclude.sk)pushParsed('sk','+'+fmt(v)+' SK');
        xpPush('sk',v);
      }
    }

    // Research XP
    if((m=line.match(rxRXP))){
      const v=toNum(m[1]);
      if(v){
        if(xpInclude.rxp)pushParsed('rxp','+'+fmt(v)+' Research');
        xpPush('rxp',v);
      }
    }

    // Normal XP
    if((m=line.match(rxXP))){
      const v=toNum(m[1]);
      if(v){
        if(xpInclude.xp)pushParsed('xp','+'+fmt(v)+' XP');
        xpPush('xp',v);
      }
    }

    // Loot parsing
    const body=line.replace(/^\s*\[[^\]]*\]\s*/, '').trim();

    let matches=[],mm;
    while((mm=rxItemref.exec(body))!==null){
      const p1=parseInt(mm[1]||'0',10),
            p2=parseInt(mm[2]||'0',10),
            ql=parseInt(mm[3]||'0',10),
            raw=mm[4]||'';

      let high=0,low=0;
      if(p2>0){
        high=Math.max(p1,p2);low=Math.min(p1,p2);
      }else{
        high=p1;low=0;
      }

      matches.push({
        high,low,ql,
        name:htmlDecode(raw).trim()
      });
    }

    if(!matches.length){
      parseMisses++;
      updateMetrics();
      return;
    }

    let actionM=body.match(rxAction);
    let action=actionM?actionM[1].toLowerCase():null;
    if(!action){
      const lm=body.match(rxLoose);
      if(lm) action=lm[1].toLowerCase();
    }
    if(!action) action='looted';

    let srcM=body.match(rxSource);
    let source=srcM?srcM[1].trim().replace(/[.,;:]$/,''):null;

    for(const it of matches){
      const isDiscarded=/delete/i.test(action);
      const isKept=/loot|pick|receiv|acquir|add/i.test(action);

      const ev={
        name:it.name,
        ql:it.ql||0,
        high:it.high,
        low:it.low,
        isDiscarded,
        isKept,
        source,
        when:new Date().toISOString()
      };

      // dedupe spam: vicinity/team echo of same loot line within same second
      if(seenLootSig(ev)){
        continue;
      }

      parseHits++;
      pushParsed(isDiscarded?'discard':'keep',
        `${ev.name} | QL:${ev.ql||'-'} | src:${ev.source||'-'}`
      );
      upsert(ev);
    }

    updateMetrics();
    renderTable();
  }

  // ===== metrics / export / session control =====

  function updateMetrics(){
    metricsInline.textContent=
      `lines=${linesRead} hits=${parseHits} misses=${parseMisses} `+
      `rot=${rotationCount} err=${readErrors} kept=${totalKept} discarded=${totalDiscarded}`;
  }

  async function startTailWithHandle(handle){
    state.fileHandle=handle;
    state.fileObj=null;
    try{
      const f=await handle.getFile();
      state.offset=f.size;
      state.startTs=now();

      cutoffLabel.textContent=new Date(state.startTs).toLocaleString();
      modeLabel.textContent='Live tail (FSA)';
      fileInfo.style.display='block';
      fileInfo.textContent=`${f.name} — starting at EOF (${fmt(f.size)} bytes)`;
      pathLabel.textContent=f.name;

      xpReset();
      setReading(true);

      if(state.tailTimer) clearInterval(state.tailTimer);
      const poll=800;
      state.tailTimer=setInterval(async ()=>{
        if(!state.reading||state.paused) return;
        try{
          const nf=await handle.getFile();
          const size=nf.size;
          if(size>state.offset){
            const slice=nf.slice(state.offset,size);
            const txt=await slice.text();
            state.offset=size;
            txt.split(/\r?\n/).forEach(ln=>{
              if(ln&&ln.trim())parseLine(ln)
            });
          }
          else if(size<state.offset){
            rotationCount++;
            state.offset=0;
            const slice=nf.slice(0,size);
            const txt=await slice.text();
            state.offset=size;
            txt.split(/\r?\n/).forEach(ln=>{
              if(ln&&ln.trim())parseLine(ln)
            });
          }
        }catch(e){
          readErrors++;
          pushDebug('READ_HANDLE_ERR: '+e);
        }
      },poll);

    }catch(e){
      readErrors++;
      pushDebug('OPEN_HANDLE_ERR: '+e);
      alert('Failed to open file handle: '+e);
    }
  }

  async function startTailWithFile(file){
    state.fileObj=file;
    state.fileHandle=null;
    try{
      const size=file.size;
      state.offset=size;
      state.startTs=now();

      cutoffLabel.textContent=new Date(state.startTs).toLocaleString();
      modeLabel.textContent='Polling fallback';
      fileInfo.style.display='block';
      fileInfo.textContent=`${file.name} — starting at EOF (${fmt(size)} bytes)`;
      pathLabel.textContent=file.name;

      xpReset();
      setReading(true);

      if(state.tailTimer) clearInterval(state.tailTimer);
      const poll=1200;
      state.tailTimer=setInterval(async ()=>{
        if(!state.reading||state.paused) return;
        try{
          const buf=await file.arrayBuffer();
          const txt=new TextDecoder().decode(buf);

          if(txt.length>state.offset){
            const append=txt.slice(state.offset);
            state.offset=txt.length;
            append.split(/\r?\n/).forEach(ln=>{
              if(ln&&ln.trim())parseLine(ln)
            });
          }
          else if(txt.length<state.offset){
            rotationCount++;
            state.offset=txt.length;
            txt.split(/\r?\n/).forEach(ln=>{
              if(ln&&ln.trim())parseLine(ln)
            });
          }
        }catch(e){
          readErrors++;
          pushDebug('POLL_READ_ERR: '+e);
        }
      },poll);

    }catch(e){
      readErrors++;
      pushDebug('START_FALLBACK_ERR: '+e);
    }
  }

  function setReading(on){
    state.reading=on&&!state.paused;
    gid('statusText').textContent=state.reading?'Parsing':(state.paused?'Paused':'Idle');
    dot.classList.toggle('ok',state.reading);
    dot.classList.toggle('err',!state.reading);
    updateMetrics();
  }

  function stopTail(){
    if(state.tailTimer) clearInterval(state.tailTimer);
    state.tailTimer=null;
    state.reading=false;
    state.paused=false;
    modeLabel.textContent='Stopped';
    setReading(false);
  }

  // Export state snapshot as JSON
  function exportState(){
    const prefs={
      inclDiscarded:inclDiscardedChk.checked,
      normalizeVisible:normalizeVisibleChk.checked,
      dimDiscardedOnly:dimDiscardedOnlyChk.checked,
      sortBy:eventSortBySel.value,
      sortDir:eventSortDirSel.value,
      search:eventSearchInp.value,
      view:eventView,
      xpInclude:{...xpInclude},
      style:styleSel.value,
      combineQL:combineQLChk.checked
    };
    const events=Object.values(stats);
    const out={
      when:new Date().toISOString(),
      totals:{totalKept,totalDiscarded,linesRead,parseHits,parseMisses,rotationCount,readErrors},
      prefs,
      events,
      xp:xpState
    };
    const blob=new Blob([JSON.stringify(out,null,2)],{type:'application/json'});
    const url=URL.createObjectURL(blob);
    const a=document.createElement('a');
    a.href=url; a.download='xanalytics_state.json'; a.click();
    URL.revokeObjectURL(url);
  }

  // Export loot stats as CSV
  function exportCSV(){
    const includeDiscardedInPct=inclDiscardedChk.checked;
    let pctBase=(includeDiscardedInPct?(totalKept+totalDiscarded):totalKept)||1;
    const rows=[[
      'High','Low','Name','Events','Kept','Discarded',
      'Percent','Rarity','MinQL','MaxQL','Samples','LastSource','LastSeen'
    ]];
    for(const k in stats){
      const s=stats[k];
      const events=s.kept+s.discarded;
      const pct=(100.0*events)/pctBase;
      rows.push([
        s.high||0,
        s.low||0,
        s.name,
        events,
        s.kept,
        s.discarded,
        pct.toFixed(2)+'%',
        s.rarity||rarityOf(pct),
        s.minql===99999?'':s.minql,
        s.maxql||'',
        (s.sampleqls||[]).join(' '),
        s.lastSource||'',
        s.lastSeen||''
      ]);
    }
    const csv=rows.map(r=>r.map(c=>{
      const t=String(c??'');
      return (t.includes('"')||t.includes(',')||t.includes('\n'))
        ? `"${t.replace(/"/g,'""')}"`
        : t;
    }).join(',')).join('\n');

    const blob=new Blob([csv],{type:'text/csv;charset=utf-8;'});
    const url=URL.createObjectURL(blob);
    const a=document.createElement('a');
    a.href=url; a.download='xanalytics_session.csv'; a.click();
    URL.revokeObjectURL(url);
  }

  // Reset everything: stop tail, clear stats/xp/etc.
  function resetSession(clearStats){
    stopTail();
    state.fileHandle=null;
    state.fileObj=null;
    state.offset=0;
    state.startTs=0;

    if(clearStats){
      for(const k in stats) delete stats[k];
      totalKept=totalDiscarded=0;
      linesRead=parseHits=parseMisses=rotationCount=readErrors=0;
      debugLog.length=0;
      rawLines.length=0;
      parsedLines.length=0;
      xpReset();

      if(parsedLog.style.display !== 'none')
        parsedLog.innerHTML='<div class="small dim">(parsed lines will appear here)</div>';

      if(rawPane.style.display !== 'none')
        rawPane.querySelector('pre').textContent = '(cleared)';

      if(debugPane.style.display !== 'none')
        debugPane.querySelector('pre').textContent = '(cleared)';
    }

    renderTable();
    updateMetrics();

    fileInfo.style.display='none';
    pathLabel.textContent='—';
    cutoffLabel.textContent='—';
    modeLabel.textContent='Idle';

    setReading(false);
  }

  // ===== UI wiring =====

  const dropEl=gid('drop');

  dropEl.addEventListener('click',()=>btnAttach.click());

  ['dragenter','dragover'].forEach(eName=>{
    dropEl.addEventListener(eName,ev=>{
      ev.preventDefault();ev.stopPropagation();
      dropEl.classList.add('dragover');
    });
  });
  ['dragleave','drop'].forEach(eName=>{
    dropEl.addEventListener(eName,ev=>{
      ev.preventDefault();ev.stopPropagation();
      dropEl.classList.remove('dragover');
    });
  });

  async function startFromDrop(file) {
    resetSession(true);
    if (typeof file.getFile === 'function') {
      await startTailWithHandle(file);
    } else {
      await startTailWithFile(file);
    }
  }

  dropEl.addEventListener('drop',async e=>{
    e.preventDefault(); e.stopPropagation();
    let fileToProcess = null;

    if (e.dataTransfer?.items?.length) {
      const item = e.dataTransfer.items[0];
      if (item.kind === 'file' && typeof item.getAsFileSystemHandle === 'function') {
        try {
          const handle = await item.getAsFileSystemHandle();
          if (handle && handle.kind === 'file') {
            fileToProcess = handle;
          }
        } catch (err) {
          pushDebug('DROP_HANDLE_ERR: '+err);
        }
      }
    }

    if (!fileToProcess && e.dataTransfer?.files?.length) {
      fileToProcess = e.dataTransfer.files[0];
    }

    if (fileToProcess) {
      startFromDrop(fileToProcess);
    } else {
      pushDebug('DROP_ERR: No valid file found.');
    }
  });

  btnAttach.addEventListener('click',async ()=>{
    if('showOpenFilePicker' in window){
      try{
        const [h]=await window.showOpenFilePicker({multiple:false});
        await startFromDrop(h);
      }catch(e){
        if(e.name !== 'AbortError')
          pushDebug('PICK_CANCEL/ERR: '+e);
      }
    }else{
      const input=document.createElement('input');
      input.type='file';
      input.accept='.txt,text/plain';
      input.addEventListener('change',()=>{
        const f=input.files?.[0];
        if(f) startFromDrop(f);
      });
      input.click();
    }
  });

  btnInfo.addEventListener('click',()=>{
    infoPanel.style.display = (infoPanel.style.display==='none'||infoPanel.style.display==='') ? 'block' : 'none';
  });

  infoCloseBtn.addEventListener('click',()=>{
    infoPanel.style.display='none';
  });

  infoTabBtns.forEach(btn=>{
    btn.addEventListener('click',()=>{
      infoTabBtns.forEach(b=>b.classList.remove('active'));
      btn.classList.add('active');

      const tabKey = btn.dataset.tab.replace('tab-','');
      infoTabs.howto.style.display = (tabKey==='howto')?'block':'none';
      infoTabs.notes.style.display = (tabKey==='notes')?'block':'none';
    });
  });

  btnReset.addEventListener('click',()=>{
    if(confirm('Reset session (clear stats and XP)?')) resetSession(true);
  });

  toggleParsed.addEventListener('click',()=>{
    const off=parsedLog.style.display!=='none';
    parsedLog.style.display=off?'none':'';
    toggleParsed.textContent=off?'Show':'Hide';
    if(!off){
      rebuildParsedDOM();
      if(!parsedLog.innerHTML.trim())
        parsedLog.innerHTML='<div class="small dim">(parsed lines will appear here)</div>';
    }
  });

  toggleRaw.addEventListener('click',()=>{
    const off=rawPane.style.display!=='none';
    rawPane.style.display=off?'none':'';
    toggleRaw.textContent=off?'Show':'Hide';
    if(!off){
      const pre=rawPane.querySelector('pre') || document.createElement('pre');
      pre.textContent=rawLines.slice(-200).join('\n');
      rawPane.innerHTML='';
      rawPane.appendChild(pre);
      rawPane.scrollTop = rawPane.scrollHeight;
    }
  });

  toggleDebug.addEventListener('click',()=>{
    const off=debugPane.style.display!=='none';
    debugPane.style.display=off?'none':'';
    toggleDebug.textContent=off?'Show':'Hide';
    if(!off){
      const pre=debugPane.querySelector('pre')||document.createElement('pre');
      pre.textContent=debugLog.join('\n');
      debugPane.innerHTML='';
      debugPane.appendChild(pre);
      debugPane.scrollTop = debugPane.scrollHeight;
    }
  });

  [inclDiscardedChk,normalizeVisibleChk,dimDiscardedOnlyChk,combineQLChk,
   eventSortBySel,eventSortDirSel,
   eventSearchInp].forEach(el=>{
    el.addEventListener('input',renderTable);
  });

  [viewAll,viewKept,viewDiscarded].forEach(btn=>{
    btn.addEventListener('click',()=>{
      [viewAll,viewKept,viewDiscarded].forEach(b=>b.classList.remove('active'));
      btn.classList.add('active');
      eventView =
        (btn===viewAll)?'all' :
        (btn===viewKept)?'kept' :
        'discarded';
      renderTable();
    });
  });

  // Goal inputs: when you change them, update xpState.goals[...] and reset that stat's baseline
  ['xp','axp','sk'].forEach(k=>{
    xpd.need[k].addEventListener('change',()=>{
      xpState.goals[k]=toNum(xpd.need[k].value);
      resetGoalTimer(k);
      refreshXPUI();
    });
  });

  // Per-track ↺ reset buttons: keeps same goal number, but re-baselines ETA/rate now
  xpResetBtn.addEventListener('click',()=>{
    resetGoalTimer('xp');
    refreshXPUI();
  });
  axpResetBtn.addEventListener('click',()=>{
    resetGoalTimer('axp');
    refreshXPUI();
  });
  skResetBtn.addEventListener('click',()=>{
    resetGoalTimer('sk');
    refreshXPUI();
  });

  // XP feed filters
  [fxp,faxp,fsk,frxp].forEach(ch=>{
    ch.addEventListener('change',()=>{
      xpInclude.xp = fxp.checked;
      xpInclude.axp= faxp.checked;
      xpInclude.sk = fsk.checked;
      xpInclude.rxp= frxp.checked;
    });
  });

  // Theme / style selector
  styleSel.addEventListener('change',()=>{
    document.body.classList.toggle('classic', styleSel.value==='classic');
  });
  document.body.classList.remove('classic');

  exportStateLink.addEventListener('click',(e)=>{
    e.preventDefault();
    exportState();
  });

  exportCSVLink.addEventListener('click',(e)=>{
    e.preventDefault();
    exportCSV();
  });

  // init
  resetSession(true);
  pushDebug('Xanalytics ready — drop or attach a file');

  // expose a hook surface so progress.js can do Saved Logs etc without editing internals
  window._xanHooks = {
    startTailWithHandle,
    resetSession,
    xpState,
    pushDebug,
    state
  };
})();
