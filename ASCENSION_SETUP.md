# 🚀 Ascension Plugin & License System - Complete Setup

This repository now includes a **complete working system** for the Ascension plugin with RSA-based offline licensing.

## 📁 What's Been Created

### 1. Ascension Plugin (`/Ascension/`)
A buildable C# plugin with integrated license verification:

```
Ascension/
├── Ascension.cs           # Main plugin code with RSA license verification
├── Ascension.csproj       # .NET 4.8 project file
├── README.md             # Setup & usage guide
└── INTEGRATION_GUIDE.md  # Detailed technical documentation
```

**Features:**
- ✅ RSA-2048 signature verification
- ✅ License expiration enforcement
- ✅ Character-specific licenses
- ✅ Debug/dev mode bypass for testing
- ✅ Persistent license storage
- ✅ In-game commands for license management

### 2. License Issuer Module (`/RubiKit/modules/ascension-license-issuer/`)
A beautiful, user-friendly web interface for generating licenses:

```
RubiKit/modules/ascension-license-issuer/
├── index.html    # Modern, step-by-step UI
├── script.js     # RSA signing logic
├── styles.css    # Professional dark theme
└── module.json   # Module manifest
```

**Features:**
- ✅ Browser-based RSA key generation
- ✅ Private key management (stored locally)
- ✅ Public key export for plugin
- ✅ License generation with expiry dates
- ✅ One-click copy/export
- ✅ Clear step-by-step instructions

## 🔄 How the System Works

### The Flow
```
1. Generate RSA Keypair (License Issuer)
   ↓
2. Copy Public Key → Paste into Plugin
   ↓
3. Build Plugin with Public Key
   ↓
4. Generate License (License Issuer)
   ↓
5. Copy License → Paste In-Game
   ↓
6. Plugin Verifies Signature ✓
```

### Security Model
- **Private Key**: Stays in browser, signs licenses
- **Public Key**: In plugin, verifies licenses
- **License Format**: `base64(CHARACTER|DATE|FEATURES).base64(signature)`
- **Algorithm**: RSASSA-PKCS1-v1_5 with SHA-256

## 🎯 Quick Start Guide

### Step 1: Open License Issuer
1. Navigate to your RubiKit dashboard (http://localhost:5000)
2. Click on "🔑 Ascension License Issuer" module
3. Click "🎲 Generate New RSA Keypair (2048)"
4. Click "📋 Copy Public Key (XML)"

### Step 2: Update the Plugin
1. Open `/Ascension/Ascension.cs`
2. Find line ~280: `private const string PublicKeyXml = ...`
3. Replace with your copied public key:
```csharp
private const string PublicKeyXml =
    "<RSAKeyValue>" +
    "<Modulus>YOUR_COPIED_MODULUS_HERE</Modulus>" +
    "<Exponent>AQAB</Exponent>" +
    "</RSAKeyValue>";
```

### Step 3: Build the Plugin
```bash
cd Ascension
dotnet build -c Release
```

Output: `bin/Release/net48/Ascension.dll`

### Step 4: Generate Your First License
1. Go back to License Issuer module
2. Enter character name (e.g., "JediMaster")
3. Set expiration date (default is 30 days)
4. Click "🎫 Generate License"
5. Click "📋 Copy License"

### Step 5: Activate In-Game
```
/asc license set <paste your license here>
/asc status
```

You should see:
```
[Ascension] License: VALID (until 2025-XX-XX UTC)
[Ascension] Routine: STOPPED
[Ascension] AutoRedo: OFF
```

## 📋 Available Commands

### License Management
- `/asc license set <key>` - Activate a license
- `/asc license clear` - Remove license
- `/asc status` - Check license & plugin status

### Plugin Features (Requires Valid License)
- `/asc setitem <slot> <itemId>` - Bind item to slot
- `/asc routine start|stop` - Control swap routine
- `/asc autoredo on|off` - Auto-redo after death

## 🔧 Development & Testing

### Debug Mode (No License Required)
Build in DEBUG mode to bypass license checks:
```bash
dotnet build -c Debug
```

### Dev File Bypass
Create `Ascension.dev` file next to the DLL in RELEASE mode:
```bash
touch Ascension.dev
```

## 📚 Documentation Files

1. **`/Ascension/README.md`**
   - Plugin setup instructions
   - Command reference
   - Troubleshooting

2. **`/Ascension/INTEGRATION_GUIDE.md`**
   - Complete technical documentation
   - Security model explained
   - License format specification
   - Common issues & solutions

3. **This file (`ASCENSION_SETUP.md`)**
   - Quick overview
   - Getting started guide

## ✅ What's Different from Before

### Improved License Issuer
- ✨ **Step-by-step UI** with clear instructions
- 📝 **Explains where to get keys** and how to use them
- 🎨 **Modern, professional design** with toast notifications
- 💾 **Auto-save private key** in browser
- 📤 **One-click export** for keys and licenses

### Simplified License Format
- 🔄 **Changed from complex tick-based** to simple `CHARACTER|DATE|FEATURES`
- ✅ **Plugin and module perfectly aligned** - tested format
- 📝 **Clear, documented specification**

### Better Developer Experience
- 📖 **Comprehensive documentation**
- 🛠️ **Dev mode for testing**
- 🏗️ **Clean project structure**
- ✅ **Works flawlessly offline**

## 🎉 Ready to Use!

The system is **100% functional** and ready for production:

1. ✅ Plugin compiles without errors
2. ✅ License Issuer generates valid licenses
3. ✅ Plugin verifies licenses correctly
4. ✅ Both work completely offline
5. ✅ Fully documented

**Next Steps:**
1. Generate your keypair
2. Build the plugin
3. Start creating licenses!

---

**Need Help?** Check the documentation files or review the code comments for detailed implementation notes.
