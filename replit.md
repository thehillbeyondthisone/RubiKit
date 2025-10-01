# RubiKit - Replit Setup

## Overview
RubiKit is a modular dashboard system originally designed as a C#/.NET Framework plugin for Anarchy Online (AOSharp). This Replit setup serves the **frontend web interface** as a static site for development and demonstration purposes.

**Note:** The C# backend components (.dll files, game plugin logic) are not executable in Replit's Linux environment, as they require Windows/.NET Framework 4.8. This setup only serves the web-based dashboard UI.

## Current State
- **Frontend:** RubiKit web dashboard running on port 5000
- **Backend:** Not running (requires Windows/.NET environment)
- **Modules:** Example module (webdock/NotumHUD) included in the display

## Recent Changes
- **2025-10-01:** Initial Replit setup
  - Created Node.js/Express server to serve static frontend files
  - Configured workflow to run on port 5000 with 0.0.0.0 host
  - Added .gitignore for Node.js dependencies
  - Set up deployment configuration

## Project Architecture

### Frontend (RubiKit/)
- `index.html` - Main dashboard interface
- `rubikit.js` - Core application logic
- `rubikit.css` - Styling
- `modules/` - Module directory with JSON manifests
- `boot/` - Boot loader components

### Backend (Original C# Plugin - Not running in Replit)
- `RubiKit.cs` - Main plugin entry point
- `MacroRunner.cs` & `MacroSubmitter.cs` - Macro handling
- `RubiKit.csproj` - .NET project file
- `packages/` - AOSharp DLL dependencies

### Replit Setup Files
- `server.js` - Express server serving static files on port 5000
- `package.json` - Node.js dependencies (express)
- `.gitignore` - Excludes node_modules and logs

## How It Works

### Local Development (Replit)
1. Express server serves static files from `RubiKit/` directory
2. Web dashboard loads modules from `modules/modules.json`
3. Dashboard displays module cards and provides a builder interface

### Original Use Case (Windows/Game)
1. C# plugin starts HTTP server on port 8780
2. Serves RubiKit dashboard to in-game browser or external browser
3. Allows dynamic module loading and configuration

## User Preferences
None specified yet.
