# RubiKit

## Setup

1. **Build your project** as usual (the one that outputs your `.dll` / host).  
2. **Copy the included `RubiKit` folder** (containing `index.html`, `rubikit.js`, `rubikit.css`, and optionally a `modules/` folder) next to your built `.dll` (same directory level).  
3. **Inject with AOSharp** into only **one character**.  
   - Type `/rubi` in-game.  
   - If the UI doesn’t open with this command, navigate to [http://127.0.0.1:8780](http://127.0.0.1:8780) in Chrome.  

## Loading Modules

1. Open **Builder → Choose Folder**.  
   - *Tip:* Chrome/Edge support the folder picker used by the Builder.  
2. Pick the folder containing your **module subfolders** (as in the layout above).  
   - RubiKit scans each subfolder:  
     - If it finds a `module.json`, it adds that module to the manifest.  
3. Watch the **Builder Log** for “✓ …” lines and see the generated manifest JSON appear in the editor.  
4. In the Builder:  
   - Click **Apply to Runtime** → RubiKit loads the manifest immediately into the dashboard.  
   - Click **Update JSON** → caches the manifest in `localStorage`, so it should persist until cleared.
  
   ![module_selection](https://github.com/user-attachments/assets/2f27c5ae-ae2b-45ac-9e08-df5df02f3b67)


## Load Order

On future launches, RubiKit looks for a manifest in this order:

1. Cached manifest (if present)  
2. `./modules/modules.json`  
3. `./modules.json`  

If none are found, the dashboard will display **“No modules found.”**

## Clearing the Dashboard

To clear the dashboard:  
- Go to **Builder → System → Clear Loaded Modules**.  
- This removes the cached manifest and reloads the dashboard.  

## Contact

Discord: **YellowUmbrellaGroup#8576**
