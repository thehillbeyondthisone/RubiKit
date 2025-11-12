(function(){
  const state={mods:[]};
  window.RubiKit={registerModule(m){ if(!m||!m.id||!m.name||!m.render)return; state.mods.push(m);} };
  async function load(){
    try{const res=await fetch("modules/modules.json"); const arr=await res.json(); await Promise.all(arr.map(x=>new Promise((ok,err)=>{const s=document.createElement('script'); s.src=(x.entry||x); s.onload=ok; s.onerror=()=>err(new Error('load '+s.src)); document.head.appendChild(s);})));}catch(e){console.log('modules.json load failed:', e.message);}
    setTimeout(init, 30);
  }
  function init(){
    const tabs=document.getElementById('rk-tabs'), view=document.getElementById('rk-view');
    if(!state.mods.length){ view.innerHTML='<div class=rk-card>No modules loaded</div>'; return; }
    tabs.innerHTML='';
    state.mods.forEach((m,i)=>{
      const b=document.createElement('button'); b.className='rk-tab'; b.textContent=m.name; b.onclick=()=>show(m); tabs.appendChild(b);
      if(i===0) show(m);
    });
    function show(m){ view.innerHTML=''; const p=document.createElement('section'); p.className='rk-card'; view.appendChild(p); m.render(p); }
  }
  window.addEventListener('DOMContentLoaded', load);
})();