# RubiKit 2.1 — Minimal README 🚀

A tiny, local HTTP + SSE plugin that exposes in-game stats for NotumHUD (or any local dashboard).  
Built for **C# 7.3** / **.NET Framework 4.8**.  
Single DLL plugin — drop, run, open.

---

## Quick facts

- HTTP API on **127.0.0.1:8777** (`/api/state`, `/api/groups`, `/events` SSE).  
- In-game commands: `/rubi` (open dashboard), `/notum` (open NotumHUD), `/about`.  
- Designed to be drop-in: `RubiKit.dll` + `modules/` folder in the plugin directory. ⚙️

---

## Directory layout (what to ship)

Place this structure inside your AO plugin folder (the folder your AO plugin loader uses):

<plugin-folder>/
├─ RubiKit.dll
├─ boot.html (optional)
├─ dashboard.html (optional)
└─ modules/
├─ notumhud/
│ ├─ index.html
│ ├─ monitor.html (optional)
│ └─ assets/
│ ├─ notumhud.js
│ └─ ...other files...
└─ <other-modules>/
└─ ...


**Notes:**
- `http://127.0.0.1:8777/` serves `boot.html` (if present) or the status/dashboard fallback.  
- `http://127.0.0.1:8777/notum` maps to `modules/notumhud/index.html`.  
- Static assets must live under `modules/` so `/modules/*` resolves correctly.

---

## Build instructions (produce RubiKit.dll)

Target: **.NET Framework 4.8**, C# language **7.3**.  
Reference AOSharp assemblies used at runtime.

### Using Visual Studio (recommended)

1. Create a **Class Library** project targeting **.NET Framework 4.8**.  
2. In **Project → Properties → Build → Advanced**, set **Language version** to `7.3`  
   (or add `<LangVersion>7.3</LangVersion>` to the `.csproj`).  
3. Add references (copy or reference from AO client / AOSharp dev environment):  
   - `AOSharp.Core.dll`  
   - `AOSharp.Common.dll`  
   - `AOSharp.Core.UI.dll`  
4. Build → **Release**.  
   Copy `bin\Release\RubiKit.dll` to your plugin folder (see directory layout above).


Ensure the project file sets `<LangVersion>7.3</LangVersion>` and references the AOSharp DLLs.

---

## Deploy (drop-in)

1. Copy `RubiKit.dll` to your AO plugin directory.  
2. Copy the `modules/` folder (with `notumhud/index.html` and assets) into the same plugin directory.  
3. Inject RubiKit.dll into a single character.
4. Do `/rubi` in-game, or open `http://127.0.0.1:8777/`

---

## Quick sanity checks

- `http://127.0.0.1:8777/health` → should return **OK**.  
- `http://127.0.0.1:8777/api/state` → JSON state.  
- `http://127.0.0.1:8777/events` → SSE stream (use `EventSource` in browser).

---

## Common gotchas

- If port **8777** is already in use, plugin will log a helpful message in chat.  
  Restarting the game/plugin usually fixes it.  
- Make sure AOSharp referenced DLLs used at build-time match the runtime environment your AO client provides.

---

### Discord: YellowUmbrellaGroup#8576
