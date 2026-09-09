# History

## 2026-09 — 2.2: dotnet CLI build + remote debug logging

Verified the 2.2 rebuild actually compiles (previously untested — no toolchain, no AO client
available). Installed `dotnet-sdk-8.0` and added `Microsoft.NETFramework.ReferenceAssemblies` to
`RubiKit.csproj` (a `PrivateAssets`-only package; doesn't affect Visual Studio builds, which
already have the real reference assemblies) so `dotnet build` can target `net48` without a
Windows Developer Pack. Both `AnyCPU` and the real `x86` config build clean — 0 warnings, 0
errors — and `bin\x86\Release\` comes out as the complete, ready-to-deploy plugin folder.

Compiling surfaced two real bugs: `Main.Run(string)` was overriding an obsolete
`AOPluginEntry.Run(string)` (confirmed via reflection on `AOSharp.Core.dll` — the current
contract is a sealed `Init(string)` that sets a protected `PluginDirectory` and calls
parameterless `Run()`; switched to that), and an unused exception variable.

Also added structured diagnostics (`DebugLog`, `rubikit-debug.log`, `GET /api/debug`) since
there's still no way to run this against a live AO client from outside a Windows desktop with
the game installed. Every previously-silent `catch` (module.json parse failures, HTTP handler
errors, and — most importantly — `StatProvider.Read()` failures, which used to fail completely
silently and just serve stale data forever) now logs. This is meant to make remote debugging
(sharing a log file or the `/api/debug` JSON with someone who can't run the game themselves)
actually possible.

## 2026-09 — 2.2: module framework + recovered NotumHUD frontend

The repo had drifted badly: `RubiKit.cs` served a hardcoded NotumHUD path, but the actual NotumHUD
frontend (`index.html`/`script.js`/`style.css`) didn't exist anywhere in source — the only surviving copy
was buried as forgotten build output under `bin/x86/Release/modules/notumhud/`, accidentally committed.
Root-level `index.html`/`rubikit.css`/`rubikit.js` were a separate, abandoned dashboard concept (empty
stubs). Leftover native-DLL files (`dllmain.cpp`, `pch.h`, `pch.cpp`, `framework.h`) from a pre-AOSharp
prototype were still tracked. `/notum` was documented but never registered as a command.

Changes:
- Recovered the real NotumHUD module from `bin/` and promoted it to `modules/notumhud/` as proper source
  (themes, fonts, stat pinning, API/diagnostics panel — matches what the README always claimed existed).
- Removed the dead C++ prototype files, the empty root dashboard stubs, and all committed build output
  (`bin/`, now gitignored).
- Built a real **drop-in module framework**: `RubiKit.cs` now scans `modules/*/module.json` at runtime
  (`GET /api/modules`) instead of hardcoding a single module path. A new `boot.html` launcher (served at
  `/`, matching what the README always documented but the code never implemented) renders a card per
  discovered module. Adding a new tool is: create `modules/<id>/` with an `index.html` + `module.json`,
  no core code changes, no restart.
- `RubiKit.csproj` now copies `boot.html` and `modules/**` into `bin\Release\` on build, so the deployed
  plugin folder is built from one `bin\Release\` copy instead of three manual copy steps — and copying
  forward (not mirroring) means any module dropped straight into a deployed folder survives future
  RubiKit.dll upgrades.
- Fixed real bugs found while wiring this up: `StateStore`'s default theme (`theme-aetherium`) didn't
  match any theme the frontend actually offers (now `theme-notum`); `font`/`fontSize`/`set_category`
  commands the frontend already sent were silently 400ing since the server never handled them; `/notum`
  was documented in the README but never registered as an in-game command.
- Registered `/notum` to open NotumHUD directly; `/rubi` now opens the module launcher instead of
  hardcoding NotumHUD.

Not verified: could not compile or run in this environment (no Windows/.NET Framework toolchain, no AO
client, no AOSharp runtime available). Reviewed by hand; needs an in-game smoke test before treating as
proven.
