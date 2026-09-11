// RubiKit launcher — lists installed modules from /api/modules and helps install more.
(() => {
  const API = location.origin;
  const grid = document.getElementById('grid');
  const statusText = document.getElementById('statusText');
  const dot = document.getElementById('dot');
  const installMsg = document.getElementById('installMsg');

  const escapeHtml = (s) => (s || '').replace(/[&<>"]/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  function iconHtml(m) {
    const ic = (m.icon || '').trim();
    // A path-like icon (contains "." or "/") is served from the module's own folder.
    if (ic && (ic.includes('/') || ic.includes('.'))) {
      const src = `/modules/${encodeURIComponent(m.folder)}/${ic.replace(/^\/+/, '')}`;
      return `<img class="icon" src="${escapeHtml(src)}" alt="" ` +
             `onerror="this.outerHTML='<span class=&quot;icon icon-emoji&quot;>\\u{1F9E9}</span>'">`;
    }
    return `<span class="icon icon-emoji">${escapeHtml(ic) || '\u{1F9E9}'}</span>`;
  }

  function card(m) {
    const el = document.createElement('div');
    el.className = 'card';
    const ver = m.version ? `<span class="ver">v${escapeHtml(m.version)}</span>` : '';
    const href = '/' + String(m.path || '').replace(/^\/+/, '');
    el.innerHTML =
      iconHtml(m) +
      `<div class="card-body">` +
        `<div class="card-title">${escapeHtml(m.name)} ${ver}</div>` +
        `<div class="card-desc">${escapeHtml(m.description || '')}</div>` +
      `</div>` +
      `<a class="btn open" href="${escapeHtml(href)}">Open</a>`;
    return el;
  }

  async function load() {
    statusText.textContent = 'Loading';
    dot.className = 'dot';
    try {
      const res = await fetch(`${API}/api/modules`, { cache: 'no-store' });
      const mods = await res.json();
      grid.innerHTML = '';
      if (!Array.isArray(mods) || mods.length === 0) {
        grid.innerHTML = '<div class="empty">No modules found. Install one below.</div>';
      } else {
        mods.forEach((m) => grid.appendChild(card(m)));
      }
      statusText.textContent = 'Online';
      dot.className = 'dot ok';
    } catch (e) {
      grid.innerHTML = '<div class="empty">Could not reach the RubiKit API.</div>';
      statusText.textContent = 'Offline';
      dot.className = 'dot err';
    }
  }

  document.getElementById('refresh').addEventListener('click', load);
  document.getElementById('openFolder').addEventListener('click', async () => {
    installMsg.textContent = '';
    try {
      const r = await fetch(`${API}/api/cmd?action=open_modules`, { method: 'POST' });
      installMsg.textContent = r.ok
        ? 'Opened the modules folder in your file browser.'
        : 'Could not open the folder — browse to the plugin’s modules directory manually.';
    } catch (e) {
      installMsg.textContent = 'Could not open the folder — browse to the plugin’s modules directory manually.';
    }
  });

  load();
})();
