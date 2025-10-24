
;(()=>{
  'use strict';
  const $ = s=>document.querySelector(s);
  const logEl = $('#boot-log'), pctEl = $('#boot-pct'), fillEl = $('#boot-fill'), stepEl = $('#boot-step');
  const bootEl = $('#boot'); bootEl.classList.add('boot-animate');

  // Pull theme from RubiKitOS (if present)
  try{
    const theme = JSON.parse(localStorage.getItem('rk.theme')||'null');
    if(theme) Object.entries(theme).forEach(([k,v])=> document.documentElement.style.setProperty(k,v));
  }catch{}

  const BOOT_MS = parseInt(getComputedStyle(document.documentElement).getPropertyValue('--boot-ms')) || 4200;
  const DEST = new URLSearchParams(location.search).get('to') || '../index.html';
  const CHIME = localStorage.getItem('rk.boot.wav');
  if (CHIME){ try{ const a=$('#boot-chime'); a.src = CHIME; a.play().catch(()=>{});}catch{} }

  const steps = ['Probing modules','Linking theme runtime','Warming caches','Starting services','Finalizing'];
  const tStart = Date.now();
  let tick=0;
  const log = (m)=>{ const d=document.createElement('div'); d.textContent=`[${new Date().toLocaleTimeString()}] ${m}`; logEl.appendChild(d); logEl.scrollTop = logEl.scrollHeight; };
  const timer = setInterval(()=>{
    const elapsed = Date.now()-tStart;
    const p = Math.min(100, Math.floor(elapsed/BOOT_MS*100));
    fillEl.style.width = p+'%'; pctEl.textContent = p+'%';
    if (elapsed > (tick+1)*(BOOT_MS/steps.length)){ stepEl.textContent = steps[tick%steps.length]+'…'; tick++; }
    if (p>=100){ clearInterval(timer); forward(); }
  }, 50);

  function forward(){
    log('Boot complete. Redirecting to dashboard…');
    setTimeout(()=> location.href = DEST, 250);
  }
  function skip(){
    fillEl.style.width='100%'; pctEl.textContent='100%';
    forward();
  }

  $('#btn-skip').onclick = skip;
  $('#btn-reload').onclick = ()=> location.reload();
  document.addEventListener('keydown', (e)=>{ if(e.key==='Escape') skip(); });

  // Watchdog: in case something stalls, still move on
  setTimeout(()=>{ try{ if(parseInt(pctEl.textContent)||0 < 100) skip(); }catch{} }, BOOT_MS + 2500);
})();
