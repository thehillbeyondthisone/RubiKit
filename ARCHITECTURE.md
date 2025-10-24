# RubiKit Architecture & Folder Structure

## 📁 Deployment Structure

When deployed via Inno Setup or manually, the RubiKit plugin should have this structure:

```
<AOSharp Plugins Folder>/RubiKit/
├── RubiKit.dll                    # Main plugin DLL (loads into game)
├── notumhud_prefs.json            # User preferences for NotumHUD (auto-created)
├── packages/                       # AOSharp dependencies
│   ├── AOSharp.Core.dll
│   └── AOSharp.Common.dll
└── RubiKit/                        # Web root (served via HTTP on port 8780)
    ├── index.html                  # Main dashboard UI
    ├── rubikit.js                  # Dashboard JavaScript
    ├── rubikit.css                 # Dashboard styles
    ├── manifest-manager.js         # Manifest utilities
    ├── boot/                       # Boot splash screen
    │   ├── boot.html
    │   ├── boot.js
    │   └── boot.css
    └── modules/                    # Module directory
        ├── modules.json            # Module manifest (defines available modules)
        ├── notumhud/               # NotumHUD module (built-in)
        │   ├── index.html
        │   ├── script.js
        │   └── style.css
        └── webdock/                # WebDock module (built-in)
            ├── index.html
            └── module.json
```

## 🔌 Backend Architecture

### RubiKit.dll (C# Plugin)

**Namespaces:**
- `RubiKit` - Main plugin entry point, HTTP server, API routing
- `RubiKit.GameData` - Game state providers and NotumHUD API

**Key Components:**

1. **RubiKit.cs** (Main Entry Point)
   - Initializes HTTP server on port `8780`
   - Web root: `<pluginDir>/RubiKit/`
   - Registers `/rubi` and `/about` chat commands
   - Starts NotumHUD API on plugin load

2. **ApiRouter.cs** (HTTP API Router)
   - Routes requests to registered handlers
   - Terminal API: `/api/terminal/*`
   - Debug API: `/api/debug/*`
   - NotumHUD API: `/api/state`, `/api/cmd`, `/events`

3. **GameData/GameStateProvider.cs**
   - Enumerates ALL stats from `AOSharp.Common.GameData.Stat` enum
   - Adds special vitals: Health, MaxHealth, CurrentNano, MaxNanoEnergy
   - Includes player metadata: Level, Profession, Breed, Side

4. **GameData/UserPreferences.cs**
   - Manages pinned stats, UI settings, custom categories
   - Persists to `notumhud_prefs.json` in plugin directory

5. **GameData/NotumHudAPI.cs**
   - `GET /api/state` - Full game state snapshot (JSON)
   - `POST /api/cmd?action=...&value=...` - Commands (pin_add, pin_remove, theme, etc.)
   - `GET /events` - Server-Sent Events stream (500ms updates)

## 🌐 Frontend Architecture

### Dashboard (RubiKitOS)

**Location:** `RubiKit/index.html`

**Features:**
- Module grid home screen
- Taskbar with module tabs
- Debug console
- Settings & manifest builder
- Theme switcher (Inferno, Notum Blue, Terminal, Paper, Monokai)
- Desktop visual effects (Aurora, Breathe, Scanline)

**Module Discovery:**
1. Checks `localStorage` for cached manifest
2. Falls back to fetching `modules/modules.json`
3. Manual scan: Users can click "Choose Folder" to scan local folders via File System Access API

### Built-in Modules

#### NotumHUD (`modules/notumhud/`)
- **Purpose**: Real-time game stats overlay
- **Features**:
  - Live stat browser with 500+ stats
  - Pinned stats panel
  - Health/Nano bars
  - Core stats (AAO, AAD, Crit, XP Mod)
  - Damage modifiers & AC chips
  - 6 themes, 8 fonts, responsive design
- **API**: Connects to `/api/state` and `/events`

#### WebDock (`modules/webdock/`)
- **Purpose**: Window nesting and container management
- **Features**: TBD

## 🔗 API Endpoints

All endpoints available at `http://127.0.0.1:8780`:

### Terminal API
- `GET /api/terminal/pull` - Get terminal log
- `POST /api/terminal/push` - Push log entry
- `POST /api/debug/toggle` - Toggle debug flags
- `POST /api/debug/level` - Set log level
- `POST /api/manifest/clear` - Clear loaded modules

### NotumHUD API
- `GET /api/state` - Full game state
  ```json
  {
    "all": { "Strength": 349, "Agility": 367, ... },
    "all_names": ["Strength", "Agility", ...],
    "pins": [{"name": "NanoCInit", "v": 1800, "label": "Nano Init"}],
    "settings": {"theme": "theme-notum", "font": "font-default", "fontSize": 100},
    "localIP": "192.168.1.100"
  }
  ```
- `POST /api/cmd?action=pin_add&value=Strength` - Add pinned stat
- `POST /api/cmd?action=pin_remove&value=Strength` - Remove pinned stat
- `POST /api/cmd?action=theme&value=theme-inferno` - Set theme
- `POST /api/cmd?action=font&value=font-sci-fi` - Set font
- `POST /api/cmd?action=fontSize&value=110` - Set font size
- `POST /api/cmd?action=set_category&value={json}` - Set custom category
- `GET /events` - Server-Sent Events stream (real-time updates)

## 📦 Adding New Modules

### Option 1: Manual Entry in modules.json

Edit `RubiKit/modules/modules.json`:

```json
{
  "modules": [
    {
      "id": "mymodule",
      "name": "My Module",
      "icon": "🚀",
      "description": "Description here",
      "tags": ["tag1", "tag2"],
      "href": "modules/mymodule/index.html"
    }
  ]
}
```

### Option 2: File System Access API (Manual Scan)

1. Open RubiKit dashboard (`/rubi`)
2. Click "Builder" in the taskbar
3. Click "Choose Folder"
4. Select your `RubiKit/modules/` folder
5. Click "Update .json" to save
6. Click "Apply to Runtime" to load

The scanner will detect:
- Folders with `module.json` (preferred)
- Folders with `index.html` (fallback)

### Module Structure

**Option A: With module.json** (Recommended)
```
modules/mymodule/
├── module.json       # Module metadata
├── index.html        # Entry point
├── script.js         # Module logic
└── style.css         # Module styles
```

**module.json example:**
```json
{
  "id": "mymodule",
  "name": "My Module",
  "icon": "🚀",
  "description": "My awesome module",
  "tags": ["utility"],
  "href": "index.html"
}
```

**Option B: Minimal** (auto-detected)
```
modules/mymodule/
└── index.html
```

## 🚀 Development Workflow

### Building

```bash
# Build in Visual Studio or via CLI
msbuild RubiKit.sln /p:Configuration=Release /p:Platform=x86
```

Output: `bin/x86/Release/RubiKit.dll`

### Testing

1. Copy `RubiKit.dll` to AOSharp plugins folder
2. Copy `RubiKit/` folder to AOSharp plugins folder
3. Launch Anarchy Online with AOSharp
4. Type `/rubi` in-game to open dashboard
5. Navigate to NotumHUD or other modules

### Hot Reload (Frontend Only)

Since the web assets are served via HTTP, you can edit HTML/CSS/JS files and refresh the browser without restarting the game:
- Edit `/RubiKit/modules/notumhud/script.js`
- Refresh browser window
- Changes take effect immediately

### Backend Changes

Backend C# changes require:
1. Stop AOSharp/game
2. Rebuild RubiKit.dll
3. Copy new DLL to plugins folder
4. Restart game

## 📝 Configuration Files

### notumhud_prefs.json (Auto-Generated)

```json
{
  "Pins": [
    {"name": "NanoCInit", "label": "Nano Init", "v": 0}
  ],
  "Settings": {
    "theme": "theme-notum",
    "font": "font-default",
    "fontSize": 100
  },
  "CustomCategories": {
    "SomeStatName": "custom-category"
  }
}
```

### RubiKit.json (Optional, User-Created)

Override default HTTP port or startup behavior:

```json
{
  "Port": 8780,
  "ServeStatic": true,
  "AutoStartServer": true
}
```

## 🎨 Theming

Both the dashboard and NotumHUD support theming:

**Dashboard Themes:**
- Inferno (default)
- Notum Blue
- Terminal
- Paper
- Monokai

**NotumHUD Themes:**
- Notum (default)
- Inferno
- Nixie
- Terminal
- Paper
- Monokai

Themes are saved in `localStorage` and persist across sessions.

## 🔒 Security Notes

- HTTP server binds to `127.0.0.1` only (localhost)
- No external network access
- Static file server has path traversal protection
- JSON parsing handles malformed input gracefully

## 📊 Performance

- **SSE Stream**: Updates every 500ms (NotumHUD)
- **Stat Enumeration**: ~200-500 stats per update
- **Memory**: Minimal (ring buffer limited to 5000 log lines)
- **CPU**: Negligible (<1% on modern systems)
