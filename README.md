# RubiKit

Build from source, after you inject the first time there should be a RubiKit folder next to your .dll

Download the .zip from releases and extract it to the RubiKit folder.


Click Modules → Select Folder.
RubiKitOS recursively scans for module.json.
Newly discovered modules appear in the Module Library.
Use Manifest Builder to decide which modules are active.

🧾 Manifest Builder (Enable/Disable Modules)

public/manifest.json is the single source of truth that RubiKitOS loads at startup.

In the UI
Open Manifest Builder from the taskbar menu.
Toggle modules on/off (the UI shows discovered modules).
Click Update Manifest to write changes to public/manifest.json.
Use Clear Loaded Modules if you want a clean slate.
