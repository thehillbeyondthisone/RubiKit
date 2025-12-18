(function(){
  var aao=document.getElementById('ov-aao'), aad=document.getElementById('ov-aad'), crit=document.getElementById('ov-crit'), xp=document.getElementById('ov-xp');
  var hpbar=document.getElementById('ov-hpbar'), hptext=document.getElementById('ov-hptext');
  var nb=document.getElementById('ov-nanobar'), nt=document.getElementById('ov-nanotext');

  // Drag
  (function(){
    var el=document.getElementById('ov'), ox=0, oy=0, down=false, sx=0, sy=0;
    el.addEventListener('mousedown', function(e){ down=true; sx=e.clientX; sy=e.clientY; var r=el.getBoundingClientRect(); ox=r.left; oy=r.top; e.preventDefault(); });
    window.addEventListener('mousemove', function(e){ if(!down) return; var dx=e.clientX-sx, dy=e.clientY-sy; el.style.left=(ox+dx)+'px'; el.style.top=(oy+dy)+'px'; });
    window.addEventListener('mouseup', function(){ down=false; });
  })();

  // Custom themes hook
  (function(){
    var dyn=document.createElement('style'); dyn.id='dyn-themes'; document.head.appendChild(dyn);
    fetch('/api/themes').then(function(r){return r.text();}).then(function(t){
      var arr=[]; try{ arr=JSON.parse(t||'[]'); }catch(e){}
      var css=''; for (var i=0;i<arr.length;i++){ var th=arr[i]; if (!th||!th.id||!th.vars) continue; var cls='body.'+th.id; css+=cls+'{'; for (var k in th.vars){ if(Object.prototype.hasOwnProperty.call(th.vars,k)){ css+=k+':'+th.vars[k]+';'; } } css+='}'; }
      dyn.textContent=css;
    }).catch(function(){});
  })();

  function nz(v){ return v!=null && v!==0 && v!==12345678 && v!==1234567890; }
  var es=new EventSource('/events');
  es.onmessage=function(ev){
    try{
      var d=JSON.parse(ev.data||'{}');
      var c=d.core||{}, s=d.settings||{}, hp=d.hp||{now:0,max:0,pct:0}, np=d.nano||{now:0,max:0,pct:0};
      
      var newClassName = s.theme || 'theme-aetherium';
      if (document.body.className !== newClassName) {
          document.body.className = newClassName;
      }

      aao.textContent = nz(c.AddAllOff) ? c.AddAllOff : '–';
      aad.textContent = nz(c.AddAllDef) ? c.AddAllDef : '–';
      crit.textContent= nz(c.CriticalIncrease) ? c.CriticalIncrease : '–';
      xp.textContent  = nz(c.XPModifier) ? (c.XPModifier+'%') : '–';
      hpbar.style.width=(hp.pct||0)+'%'; hptext.textContent=(hp.now||0)+' / '+(hp.max||0)+' ('+(hp.pct||0)+'%)';
      nb.style.width=(np.pct||0)+'%';     nt.textContent =(np.now||0)+' / '+(np.max||0)+' ('+(np.pct||0)+'%)';
    }catch(e){}
  };
})();