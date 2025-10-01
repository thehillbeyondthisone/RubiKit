# Ascension Plugin v2.0

A license-protected auto-swapper plugin for Anarchy Online (AOSharp framework).

## 🔑 License System Setup

This plugin uses **RSA-signed licenses** for authentication. Follow these steps to set up the licensing system:

### Step 1: Generate Your RSA Keypair

1. Open the **Ascension License Issuer** module in RubiKit
2. Click **"🎲 Generate New RSA Keypair (2048)"**
3. Click **"🔒 Export Private Key"** and save it securely (you'll need this to generate licenses)
4. Click **"📋 Copy Public Key (XML)"** to copy the public key

### Step 2: Update the Plugin with Your Public Key

1. Open `Ascension.cs` in your code editor
2. Find the `PublicKeyXml` constant (around line 280)
3. Replace the placeholder with your copied public key:

```csharp
private const string PublicKeyXml =
    "<RSAKeyValue>" +
    "<Modulus>YOUR_MODULUS_HERE</Modulus>" +
    "<Exponent>AQAB</Exponent>" +
    "</RSAKeyValue>";
```

### Step 3: Build the Plugin

```bash
# Using .NET CLI
dotnet build Ascension.csproj -c Release

# Or use Visual Studio / Rider to build
```

The compiled DLL will be in `bin/Release/net48/Ascension.dll`

### Step 4: Generate a License

1. Go back to the **Ascension License Issuer** module
2. Enter the **character name** (exact match required)
3. Set the **expiration date** (default: 30 days from now)
4. Add optional **features** (e.g., "premium", "beta-access")
5. Click **"🎫 Generate License"**
6. Click **"📋 Copy License"** to copy the license key

### Step 5: Activate the License In-Game

1. Load the plugin in AOSharp
2. In-game, type: `/asc license set <paste your license here>`
3. Verify with: `/asc status`

You should see:
```
[Ascension] License: VALID (until 2025-XX-XX UTC)
```

## 📋 Plugin Commands

### License Management
- `/asc license set <key>` - Activate a license
- `/asc license clear` - Remove current license
- `/asc status` - Show plugin status

### Features (Requires Valid License)
- `/asc setitem <slot> <itemId>` - Bind an item to a slot
- `/asc routine start` - Start swap routine
- `/asc routine stop` - Stop swap routine
- `/asc autoredo on|off` - Enable/disable auto-redo after death

## 🔧 Development Mode

For testing without a license:

### DEBUG Build
In DEBUG mode, the license check is automatically bypassed. Build with:
```bash
dotnet build -c Debug
```

### Dev File Bypass
In RELEASE mode, create an empty file named `Ascension.dev` next to the DLL:
```bash
touch Ascension.dev
```

The plugin will bypass license validation while this file exists.

## 🔐 Security Notes

- **Keep your private key secure!** Anyone with it can generate valid licenses.
- Only the **public key** goes in the plugin code
- Licenses are **offline** - no server needed
- License format: `base64(CHARACTER|DATE|FEATURES).base64(RSA-signature)`

## 📦 Project Structure

```
Ascension/
├── Ascension.cs          # Main plugin code
├── Ascension.csproj      # Project file
└── README.md            # This file
```

## 🛠️ Dependencies

- .NET Framework 4.8
- AOSharp.Core (referenced from `../libs/`)
- AOSharp.Common (referenced from `../libs/`)

## 📝 License Format

Licenses are RSA-SHA256 signed tokens with this format:
- **Payload**: `CHARACTER|YYYY-MM-DD|FEATURES`
- **Signature**: RSASSA-PKCS1-v1_5 with SHA-256
- **Token**: `base64(payload).base64(signature)`

## ❓ Troubleshooting

**"Invalid license"**
- Ensure the public key in the plugin matches your private key
- Check that the character name matches exactly (case-sensitive)
- Verify the license hasn't expired

**"Feature locked"**
- License may be expired
- Use `/asc status` to check license status
- Generate a new license with a future expiration date

**"No key loaded" (License Issuer)**
- Click "Generate New RSA Keypair" or import an existing private key
- Make sure "remember in this browser" is checked to save the key

## 📄 License

This software is for personal use only. Keep your private signing key secure.
