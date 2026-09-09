# RubiKit 2.2 🚀
![reactor](https://markdown-rbmk.vercel.app/api/badge?username=thehillbeyondthisone)

A lightweight, local HTTP + SSE plugin for **Anarchy Online** (C# 7.3 / .NET Framework 4.8) that exposes real-time
in-game stats, and serves a **drop-in module framework**: any HTML/JS tool you place under `modules/` shows up in
the launcher automatically, and survives future rebuilds/redeploys of the DLL untouched.

Single DLL plugin — drop, run, open.

---

## ✨ Quick Facts

- **HTTP API** on `127.0.0.1:8777`
  - `/api/state` — Current character state (JSON)
  - `/api/groups` — Stat group definitions (JSON)
  - `/api/modules` — Installed modules, discovered from `modules/*/module.json` (JSON)
  - `/events` — Server-Sent Events stream
- **In-game commands:**
  - `/rubi` — Open the module launcher (`boot.html`)
  - `/notum` — Open NotumHUD directly
  - `/about` — Plugin info

---

## 📁 Directory Layout

This is what `bin\Release\` looks like after building — copy its contents straight into your AO plugin folder
(the folder your AO plugin loader uses):

```
YourPluginFolder/
├── RubiKit.dll
├── boot.html              (module launcher — served at "/")
└── modules/
    └── notumhud/
        ├── index.html
        ├── script.js
        ├── style.css
        └── module.json    (id, name, icon, entry, description)
```

### Notes:
- `http://127.0.0.1:8777/` serves `boot.html` if present (falls back to `dashboard.html`, then a plain status page)
- `boot.html` fetches `/api/modules` and renders a card per module — no code changes needed to add one
- Static assets must live under `modules/<id>/` so `/modules/<id>/*` resolves and gets auto-discovered
- Deploy by **copying files forward**, not mirroring/wiping the destination: any module folder you dropped
  into a deployed `modules/` directory that isn't part of this repo is left alone when you copy a new build over it

---

## 🔧 Build Instructions

**Target:** .NET Framework 4.8, C# language version 7.3  
**Dependencies:** AOSharp assemblies used at runtime

### Using Visual Studio (Recommended)

1. **Create a Class Library project** targeting .NET Framework 4.8
2. **Set C# language version to 7.3:**
   - Go to **Project → Properties → Build → Advanced**
   - Set **Language version** to `7.3`
   - Or add `<LangVersion>7.3</LangVersion>` to your `.csproj` file
3. **Add references** (copy or reference from AO client / AOSharp dev environment):
   - `AOSharp.Core.dll`
   - `AOSharp.Common.dll`
   - `AOSharp.Core.UI.dll`
4. **Build → Release**
5. Copy `bin\Release\RubiKit.dll` to your plugin folder

### Project File Example

Ensure your `.csproj` includes:

```xml
<PropertyGroup>
  <TargetFramework>net48</TargetFramework>
  <LangVersion>7.3</LangVersion>
</PropertyGroup>
```

---

## 🎮 Deployment

1. Build → `bin\Release\` now contains `RubiKit.dll`, `boot.html`, and `modules\` together
2. Copy everything in `bin\Release\` into your AO plugin directory (copy forward — don't delete extra files
   already there, so any modules you've dropped in manually survive the upgrade)
3. Inject `RubiKit.dll` into a single character using your plugin loader
4. Use `/rubi` in-game, or open `http://127.0.0.1:8777/` in your browser

---

## 🧩 Adding Modules

Any tool can be dropped in without touching `RubiKit.cs`:

1. Create `modules/<yourtool>/` next to `RubiKit.dll`
2. Add an `index.html` (plus whatever JS/CSS it needs) — pull live stats from the existing `/api/state`,
   `/api/groups`, `/events`, and `/api/cmd` endpoints if it needs them
3. Add a `module.json` describing it:
   ```json
   {
     "id": "yourtool",
     "name": "Your Tool",
     "icon": "🛠️",
     "entry": "index.html",
     "description": "One line about what it does."
   }
   ```
4. Reload `http://127.0.0.1:8777/` — it now shows up as a card in the launcher automatically

`modules/` is scanned fresh on every request to `/api/modules`, so there's nothing to restart. Because it's a
plain folder read at runtime (not compiled into the DLL), a module survives rebuilding or upgrading RubiKit
as long as you copy new builds forward instead of wiping the plugin folder first.

---

## ✅ Quick Sanity Checks

Test these endpoints to verify everything is working:

| Endpoint | Expected Result |
|----------|----------------|
| `http://127.0.0.1:8777/health` | Returns `OK` |
| `http://127.0.0.1:8777/api/state` | Returns JSON state data |
| `http://127.0.0.1:8777/api/modules` | Returns JSON array of discovered modules |
| `http://127.0.0.1:8777/events` | SSE stream (use `EventSource` in browser) |

---

# NotumHUD 📊

A real-time character statistics HUD for Anarchy Online, displaying your character's vital stats, modifiers, and armor classes in a sleek, customizable interface.

---

<img width="1431" height="813" alt="Screenshot 2025-11-13 183501" src="https://github.com/user-attachments/assets/2a51b0b5-3746-441e-9672-ce6953763381" />

---

<img width="1430" height="815" alt="Screenshot 2025-11-13 183643" src="https://github.com/user-attachments/assets/3f296c2f-9045-46bc-89f4-74feadab0652" />

---

<img width="1430" height="810" alt="Screenshot 2025-11-13 183523" src="https://github.com/user-attachments/assets/f1fb5d80-edaf-4620-87fc-8f5fe12aa2e5" />

---

## Features ✨

### 📈 Real-Time Stats Display
- **Core Stats**: HP, Nano, AAO, AAD, Crit, XP%, and many others at a glance
- **Damage Modifiers**: All your +damage bonuses in one place
- **Armor Classes**: Complete AC breakdown by damage type
- **Stats Browser**: Search and browse through all character statistics

### 🎨 Personalization
- **6 Themes**: Choose from Notum, Inferno, Nixie, Terminal, Paper, and Monokai
- **8 Font Options**: From sci-fi (Exo 2) to retro pixel (VT323)
- **Adjustable Scale**: 80% to 130% zoom for comfortable viewing
- **Compact Mode**: Condensed layout for smaller screen; currently looks like shit.

### 📌 Stat Pinning
Keep your most important stats visible at a glance:

(NOTE: Edit mode changes stat categories manually. You probably won't have to touch this.

### 🔧 API Panel
Access connection settings and diagnostics (toggle with the bottom-right button):

- **Port Configuration**: Set your API port (default scans automatically)
- **Connection Status**: See when you're connected and receiving data
- **Port Scanner**: Auto-detect available ports
- **API Inspector**: Send custom commands for advanced use
- **Event Log**: View connection events and data updates
- **JSON Viewer**: Inspect the raw data payload

### 💾 Category Management
- **Export Categories**: Save your stat organization and pins
- **Reset Options**: Clear categories or restore default settings



## Tips 💡

- Set your favorite theme and font as defaults using the ⭐ buttons
- Use Compact Mode on smaller displays or when you need more screen space
- Pin stats you check frequently (resists, recharge, casting speed, etc.)
- The API panel includes a self-check button to verify your setup

## Troubleshooting 🔍

### Port 8777 Already in Use
If port 8777 is already taken, the plugin will log a helpful message in chat. **Fix:** Restart the game/plugin.

### DLL Reference Mismatch
Make sure the AOSharp referenced DLLs used at build-time match the runtime environment your AO client provides. Version mismatches can cause unexpected behavior.

### Module Not Loading
Verify that:
- The `modules/` folder is in the same directory as `RubiKit.dll`
- Each module folder has a valid `module.json` (check `/api/modules` for parse errors — malformed ones are skipped silently)
- The module's `entry` file (usually `index.html`) exists at the path `module.json` points to

**Not connecting?**
- Check that the AOSharp plugin is running
- Try the "Scan" button to detect available ports
- Verify your port number in the Connection section

**Stats not updating?**
- Check the Event Log in the API panel for errors
- Try reconnecting with the "Connect" button
- Verify the Last Payload shows recent timestamps

---
## 📞 Contact

**Discord:** YellowUmbrellaGroup#8576
