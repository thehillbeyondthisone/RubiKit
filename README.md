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

## 🚀 Deployment

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

## ⚠️ Common Gotchas

### Port 8777 Already in Use
If port 8777 is already taken, the plugin will log a helpful message in chat. **Fix:** Restart the game/plugin.

### DLL Reference Mismatch
Make sure the AOSharp referenced DLLs used at build-time match the runtime environment your AO client provides. Version mismatches can cause unexpected behavior.

### Module Not Loading
Verify that:
- The `modules/` folder is in the same directory as `RubiKit.dll`
- Static file paths match the directory structure exactly
- `index.html` files exist in their expected locations

---

## 📞 Support

**Discord:** YellowUmbrellaGroup#8576

---
