# 🔗 Ascension Integration Guide

This guide explains how the **License Issuer Module** and **Ascension Plugin** work together to create a secure, offline licensing system.

## 📊 System Overview

```
┌─────────────────────────┐         ┌──────────────────────┐
│  License Issuer Module  │         │  Ascension Plugin    │
│  (Browser/RubiKit)      │         │  (In-Game/AOSharp)   │
├─────────────────────────┤         ├──────────────────────┤
│                         │         │                      │
│  1. Generate RSA Keys   │────────>│  Public Key Only     │
│     (Private + Public)  │         │  (for verification)  │
│                         │         │                      │
│  2. Sign Licenses       │         │                      │
│     CHARACTER|DATE|...  │────────>│  3. Verify & Accept  │
│                         │         │     RSA-SHA256       │
│                         │         │                      │
│  ✅ Private Key Stays   │         │  ✅ Validates Token  │
│     In Browser          │         │     Checks Expiry    │
└─────────────────────────┘         └──────────────────────┘
```

## 🔐 How It Works

### 1. Key Generation (One-Time Setup)

The License Issuer generates an **RSA-2048 keypair**:
- **Private Key**: Used to sign licenses (stays in your browser)
- **Public Key**: Embedded in the plugin (verifies licenses)

**License Issuer** (JavaScript):
```javascript
// Generates keypair using Web Crypto API
const keypair = await crypto.subtle.generateKey({
  name: "RSASSA-PKCS1-v1_5",
  modulusLength: 2048,
  publicExponent: new Uint8Array([0x01, 0x00, 0x01]),
  hash: "SHA-256"
}, true, ["sign", "verify"]);
```

**Plugin** (C#):
```csharp
// Public key from License Issuer (paste here)
private const string PublicKeyXml =
    "<RSAKeyValue>" +
    "<Modulus>...</Modulus>" +
    "<Exponent>AQAB</Exponent>" +
    "</RSAKeyValue>";
```

### 2. License Generation

**License Issuer** creates a signed token:

```javascript
// Build payload
const payload = `${character}|${expDate}|${features}`;

// Sign with private key
const signature = await crypto.subtle.sign(
  { name: "RSASSA-PKCS1-v1_5" },
  privateKey,
  encoder.encode(payload)
);

// Create token: base64(payload).base64(signature)
const token = `${btoa(payload)}.${btoa(signature)}`;
```

**Example Token**:
```
SmVkaU1hc3RlcnwyMDI1LTEyLTMxfHByZW1pdW0=.MGQCMGRkNz...
└──────────────┬─────────────────────┘ └─────┬─────────┘
           payload                      signature
```

### 3. License Verification

**Plugin** verifies the token in-game:

```csharp
// 1. Split token
var parts = token.Split('.');
byte[] payload = Convert.FromBase64String(parts[0]);
byte[] signature = Convert.FromBase64String(parts[1]);

// 2. Verify RSA signature
using (var rsa = new RSACryptoServiceProvider())
{
    rsa.FromXmlString(PublicKeyXml);
    bool valid = rsa.VerifyData(
        payload,
        new SHA256CryptoServiceProvider(),
        signature
    );
}

// 3. Parse and validate
string text = Encoding.UTF8.GetString(payload);
// "CHARACTER|2025-12-31|features"
var parts = text.Split('|');
DateTime expiry = DateTime.Parse(parts[1]);
bool expired = DateTime.UtcNow > expiry;
```

## 🎯 Complete Workflow

### First-Time Setup

1. **Open License Issuer Module** (in RubiKit)
2. **Generate Keypair**
   - Click "Generate New RSA Keypair"
   - Export and save private key (backup!)
   - Copy public key XML

3. **Update Plugin**
   - Paste public key into `Ascension.cs`
   - Replace `PublicKeyXml` constant
   - Build plugin (`dotnet build -c Release`)

4. **Deploy Plugin**
   - Copy `Ascension.dll` to AOSharp plugins folder
   - Load plugin in-game

### Generating Licenses (Anytime)

1. **Open License Issuer Module**
2. **Enter Details**:
   - Character name: `JediMaster` (exact match!)
   - Expiration: `2025-12-31` (or use default 30 days)
   - Features: `premium` (optional)

3. **Generate & Copy**
   - Click "Generate License"
   - Copy the token

4. **Activate In-Game**
   ```
   /asc license set <paste token here>
   ```

5. **Verify**
   ```
   /asc status
   ```

## 🔒 Security Model

### ✅ Secure
- Private key **never leaves your browser**
- Public key in plugin **cannot generate licenses**
- Licenses are **cryptographically signed**
- Expiration is **enforced**
- Offline verification (no server needed)

### ⚠️ Important
- **Backup your private key!** If lost, you can't generate licenses
- **Keep private key secret!** Anyone with it can generate valid licenses
- **Character names are case-sensitive**
- **Expiration is in UTC timezone**

## 📝 License Format Specification

### Payload Structure
```
CHARACTER|YYYY-MM-DD|FEATURES
```

- **CHARACTER**: In-game character name (exact match, case-sensitive)
- **YYYY-MM-DD**: Expiration date in UTC (ISO 8601 format)
- **FEATURES**: Optional comma-separated feature flags

### Token Structure
```
BASE64(payload).BASE64(signature)
```

- **Payload**: UTF-8 encoded character|date|features
- **Signature**: RSASSA-PKCS1-v1_5(SHA-256) of payload bytes
- **Separator**: Single period (`.`)

### Example Breakdown
```
Token: VGVzdFVzZXJ8MjAyNS0xMi0zMXxwcmVtaXVt.MEUCIQDx...

Decoded Payload: TestUser|2025-12-31|premium
                 ^^^^^^^^ ^^^^^^^^^^ ^^^^^^^
                 Character  Expiry   Features

Signature: RSA-2048 signature verifying payload integrity
```

## 🐛 Common Issues & Solutions

### Issue: "Invalid license"
**Causes**:
- Public key mismatch (plugin has different key than issuer)
- Corrupted token (copy/paste error)
- Character name mismatch (case-sensitive!)

**Solutions**:
1. Verify public key in plugin matches your private key
2. Re-copy the license (ensure no line breaks)
3. Check character name spelling/case

### Issue: License expired
**Causes**:
- Expiration date in the past
- System clock incorrect

**Solutions**:
1. Generate new license with future date
2. Check system time/timezone

### Issue: "No key loaded" (Issuer)
**Causes**:
- Browser cleared localStorage
- Using different browser/incognito
- Key never generated

**Solutions**:
1. Click "Generate New RSA Keypair"
2. Or import saved private key
3. Check "remember in browser"

## 🔄 Key Rotation

To rotate keys (e.g., private key compromised):

1. **Generate new keypair** in License Issuer
2. **Update plugin** with new public key
3. **Rebuild and redeploy** plugin
4. **Generate new licenses** for all users
5. **Revoke old key** (delete private key file)

## 📚 Technical Details

### Cryptography
- **Algorithm**: RSASSA-PKCS1-v1_5
- **Hash**: SHA-256
- **Key Size**: 2048 bits
- **Encoding**: Base64

### Storage
- **Plugin**: License stored in `Ascension.lic` file
- **Issuer**: Private key in browser localStorage

### Verification Flow
```
1. Split token at '.'
2. Decode base64 payload and signature
3. Verify signature using public key + SHA256
4. Parse payload (CHARACTER|DATE|FEATURES)
5. Check expiration date
6. Grant/deny access
```

## 💡 Best Practices

1. **Backup Private Key**: Store in secure location
2. **Use Descriptive Features**: e.g., "premium-tier1" instead of "p1"
3. **Set Reasonable Expiry**: Default 30 days works well
4. **Test Licenses**: Generate test license before distribution
5. **Document Keys**: Note which public key is in which build
6. **Version Control**: Don't commit private keys to git!

---

**Questions?** Check the plugin README or refer to the code comments for implementation details.
