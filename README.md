# RubiKit 2.1 🚀

A lightweight, local HTTP + SSE plugin that exposes real-time in-game stats for NotumHUD and other local dashboards. Built for **Anarchy Online** using C# 7.3 / .NET Framework 4.8.

Single DLL plugin — drop, run, open.

---

## ✨ Quick Facts

- **HTTP API** on `127.0.0.1:8777`
  - `/api/state` — Current character state (JSON)
  - `/api/groups` — Group information (JSON)
  - `/events` — Server-Sent Events stream
- **In-game commands:**
  - `/rubi` — Open main panel
  - `/notum` — Open NotumHUD
  - `/about` — Plugin info

---

## 📁 Directory Layout

Place this structure inside your AO plugin folder (the folder your AO plugin loader uses):

```
YourPluginFolder/
├── RubiKit.dll
├── boot.html              (optional - custom landing page)
├── dashboard.html         (optional - fallback dashboard)
└── modules/
    └── notumhud/
        ├── index.html
        ├── monitor.html   (optional)
        └── assets/
            ├── notumhud.js
            └── ...other files...
```

### Notes:
- `http://127.0.0.1:8777/` serves `boot.html` (if present) or the status/dashboard fallback
- `http://127.0.0.1:8777/notum` maps to `modules/notumhud/index.html`
- Static assets must live under `modules/` so `/modules/*` resolves correctly

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

1. Copy `RubiKit.dll` to your AO plugin directory
2. Copy the `modules/` folder (with `notumhud/index.html` and assets) into the same plugin directory
3. Inject `RubiKit.dll` into a single character using your plugin loader
4. Use `/rubi` in-game, or open `http://127.0.0.1:8777/` in your browser

---

## ✅ Quick Sanity Checks

Test these endpoints to verify everything is working:

| Endpoint | Expected Result |
|----------|----------------|
| `http://127.0.0.1:8777/health` | Returns `OK` |
| `http://127.0.0.1:8777/api/state` | Returns JSON state data |
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
- Static file paths match the directory structure exactly
- `index.html` files exist in their expected locations

1. Place the `notumHUD` folder in your AOSharp modules directory
2. Ensure the companion plugin is running in-game
3. Open NotumHUD from your modules menu
4. The HUD will automatically scan for an available connection port

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
