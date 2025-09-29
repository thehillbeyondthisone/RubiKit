# RubiKit
Build your project as usual (the one that outputs your .dll / host).

**IMPORTANT** Copy the included RubiKit folder (containing index.html, rubikit.js, rubikit.css, and optionally a modules/ folder) next to your built .dll (same directory level).

Inject with AOSharp into only ONE character.
Type /rubi ingame.
(if the UI doesn't open with this command, go to 127.0.0.1:8780 in Chrome)

------------------------------------------------------------------------

Select your module folder (Builder → Choose Folder)
Tip: Chrome/Edge support the folder picker used by the Builder.

Pick the folder that contains your module subfolders (as in the layout above).
RubiKit scans each subfolder:
If it finds a module.json, it adds that module to the manifest.

Watch Builder Log for “✓ …” lines and see the generated manifest JSON appear in the editor.

In the Builder, click Apply to Runtime.
RubiKit loads the manifest immediately into the dashboard.
Click "update JSON" to cache the manifest in (localStorage), so a normal Reload keeps it until you clear it.

On future launches, RubiKit looks for a manifest in this order:
Cached manifest (if present).
./modules/modules.json
./modules.json
Otherwise, the dashboard will say “No modules found.”

To clear the dashboard:
In Builder → System, click Clear Loaded Modules (removes the cached manifest and reloads).

Contact:
Discord - YellowUmbrellaGroup#8576
