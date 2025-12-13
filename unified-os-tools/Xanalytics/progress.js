(function(){
  const modal = document.getElementById('savedLogsModal');
  const listEl = document.getElementById('savedLogsList');
  const closeBtn = document.getElementById('closeSavedLogs');

  // IndexedDB setup (mirrors the pattern in your Hydra.html logic)
  const DB_NAME='xanalyticsFS';
  const STORE='files';
  let dbPromise=null;

  function dbOpen(){
    if(dbPromise) return dbPromise;
    dbPromise=new Promise((resolve,reject)=>{
      const req=indexedDB.open(DB_NAME,1);
      req.onupgradeneeded=e=>{
        const db=req.result;
        if(!db.objectStoreNames.contains(STORE)){
          db.createObjectStore(STORE,{keyPath:'id',autoIncrement:true});
        }
      };
      req.onsuccess=()=>resolve(req.result);
      req.onerror=e=>reject(e.target.error);
    });
    return dbPromise;
  }

  async function addHandle(label,handle){
    const db=await dbOpen();
    return new Promise((resolve,reject)=>{
      const tx=db.transaction(STORE,'readwrite');
      tx.objectStore(STORE).add({label,handle,ts:Date.now()});
      tx.oncomplete=resolve;
      tx.onerror=e=>reject(e.target.error);
    });
  }

  async function listHandles(){
    const db=await dbOpen();
    return new Promise((resolve,reject)=>{
      const tx=db.transaction(STORE,'readonly');
      const req=tx.objectStore(STORE).getAll();
      req.onsuccess=()=>resolve(req.result||[]);
      req.onerror=e=>reject(e.target.error);
    });
  }

  async function deleteAllHandles(){
    const db=await dbOpen();
    return new Promise((resolve,reject)=>{
      const tx=db.transaction(STORE,'readwrite');
      tx.objectStore(STORE).clear();
      tx.oncomplete=resolve;
      tx.onerror=e=>reject(e.target.error);
    });
  }

  async function renderList(){
    const rows=await listHandles();
    if(!rows.length){
      listEl.innerHTML='<div class="small dim">(no saved logs)</div>';
      return;
    }
    const parts=[];
    rows.forEach((row,i)=>{
      const when=new Date(row.ts).toLocaleString();
      const safeLabel=(row.label||('log '+row.id));
      parts.push(
        `<div class="savedRow">
          <div><b>${safeLabel}</b><br><span class="dim small">${when}</span></div>
          <div class="row">
            <button class="btn tiny" data-idx="${i}" data-act="load">Load</button>
          </div>
        </div>`
      );
    });
    parts.push('<div style="margin-top:8px;"><button class="btn ghost small" data-act="wipe">Delete All</button></div>');
    listEl.innerHTML=parts.join('');
  }

  async function openModal(){
    await renderList();
    modal.style.display='block';
  }
  function closeModal(){
    modal.style.display='none';
  }

  closeBtn.addEventListener('click',closeModal);
  modal.addEventListener('click',e=>{
    if(e.target===modal) closeModal();
  });

  listEl.addEventListener('click',async e=>{
    const act=e.target.getAttribute('data-act');
    if(!act)return;
    if(act==='wipe'){
      await deleteAllHandles();
      await renderList();
      return;
    }
    if(act==='load'){
      const idx=parseInt(e.target.getAttribute('data-idx'),10);
      const rows=await listHandles();
      const row=rows[idx];
      if(!row)return;
      const h=row.handle;
      try{
        // Request permission before use
        if(h.requestPermission){
          const p=await h.requestPermission({mode:'read'});
          if(p!=='granted')return;
        }
        if(window._xanHooks){
          window._xanHooks.resetSession(true);
          window._xanHooks.startTailWithHandle(h);
          window._xanHooks.pushDebug('Loaded saved log "'+(row.label||('log '+row.id))+'"');
          closeModal();
        }
      }catch(err){
        if(window._xanHooks) window._xanHooks.pushDebug('LOAD_SAVED_ERR: '+err);
      }
    }
  });

  // Listen for the Saved Logs button press (script.js dispatches this event)
  document.addEventListener('xan-open-savedlogs',openModal);

  // Helper for saving the current handle to the Saved Logs DB.
  // You can run this from console:
  //   saveCurrentLogHandle("Inferno 10-27 run")
  async function saveCurrentLogHandle(label){
    if(!window._xanHooks) return;
    const st=window._xanHooks.state;
    if(!st || !st.fileHandle) return;
    await addHandle(label||st.fileHandle.name||'AO Log', st.fileHandle);
    if(window._xanHooks) window._xanHooks.pushDebug('Saved current log handle.');
  }
  window.saveCurrentLogHandle=saveCurrentLogHandle;

  // Future: level tracking will hook here.
  // We'll wrap parseLine later to:
  //   - watch for "You are now level 156." / "You have gained a Shadowlevel. You are now level 205."
  //   - update xpState.playerLevel, xpState.levelProgress, xpState.levelNeeded
  //   - write Ding ETA into #dingETA
  // We are waiting for you to give us your real level-up / shadowlevel lines so we parse the exact phrasing (not guessed).

})();
