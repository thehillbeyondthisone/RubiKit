using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;

using AOSharp.Core;
using AOSharp.Core.UI;

namespace Ascension
{
    public class Main : AOPluginEntry
    {
        private static string _pluginDir = ".";
        private static LicenseManager _lic;
        private static AscensionCore _core;

        public override void Init(string pluginDir)
        {
            _pluginDir = pluginDir;

            Chat.WriteLine("[Ascension] v2.0.0 loaded.");
            RegisterCommands();

            string licPath = Path.Combine(_pluginDir, "Ascension.lic");
            _lic = new LicenseManager(licPath);

            if (_lic.TryLoad())
            {
                if (_lic.IsExpired)
                    Chat.WriteLine($"[Ascension] License expired on {_lic.ExpirationUtc:yyyy-MM-dd}. /asc license set <key>");
                else
                    Chat.WriteLine($"[Ascension] License OK. Expires {_lic.ExpirationUtc:yyyy-MM-dd} UTC.");
            }
            else
            {
                Chat.WriteLine("[Ascension] No license set. Use /asc license set <key>");
            }

            _core = new AscensionCore(_lic);
        }

        [Obsolete("AOSharp now calls Init(string). Keeping Run(string) for loaders that still invoke it.")]
        public override void Run(string pluginDir) => Init(pluginDir);

        private static void RegisterCommands()
        {
            Chat.RegisterCommand("asc", OnAscCommand);
        }

        private static void OnAscCommand(string cmd, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                PrintHelp();
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "help":
                    PrintHelp();
                    return;

                case "status":
                    PrintStatus();
                    return;

                case "license":
                    HandleLicense(args);
                    return;

                case "setitem":
                    if (!EnsureLicensed()) return;
                    HandleSetItem(args);
                    return;

                case "routine":
                    if (!EnsureLicensed()) return;
                    HandleRoutine(args);
                    return;

                case "autoredo":
                    if (!EnsureLicensed()) return;
                    HandleAutoRedo(args);
                    return;

                default:
                    PrintHelp();
                    return;
            }
        }

        private static void PrintHelp()
        {
            Chat.WriteLine("[Ascension] Commands:");
            Chat.WriteLine("  /asc status");
            Chat.WriteLine("  /asc license set <key>");
            Chat.WriteLine("  /asc license clear");
            Chat.WriteLine("  /asc setitem <slot> <itemId>");
            Chat.WriteLine("  /asc routine start|stop");
            Chat.WriteLine("  /asc autoredo on|off");
        }

        private static void PrintStatus()
        {
            string lic =
                _lic.IsLicensed
                ? (_lic.IsExpired ? "EXPIRED" : $"VALID (until {_lic.ExpirationUtc:yyyy-MM-dd} UTC)")
                : "NOT SET";

            Chat.WriteLine($"[Ascension] License: {lic}");
            Chat.WriteLine($"[Ascension] Routine: {(_core.IsRunning ? "RUNNING" : "STOPPED")}");
            Chat.WriteLine($"[Ascension] AutoRedo: {(_core.AutoRedo ? "ON" : "OFF")}");
            Chat.WriteLine($"[Ascension] Bindings: {_core.BindingCount} slot(s) bound.");
        }

        private static void HandleLicense(string[] args)
        {
            if (args.Length >= 2 && args[1].Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3)
                {
                    Chat.WriteLine("[Ascension] Usage: /asc license set <key>");
                    return;
                }

                string key = string.Join(" ", args, 2, args.Length - 2).Trim();
                if (_lic.SetAndSave(key))
                {
                    Chat.WriteLine(_lic.IsExpired
                        ? $"[Ascension] License accepted but expired on {_lic.ExpirationUtc:yyyy-MM-dd}."
                        : $"[Ascension] License OK. Expires {_lic.ExpirationUtc:yyyy-MM-dd} UTC.");
                }
                else
                {
                    Chat.WriteLine("[Ascension] Invalid license.");
                }
                return;
            }

            if (args.Length >= 2 && args[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                _lic.Clear();
                Chat.WriteLine("[Ascension] License cleared.");
                return;
            }

            Chat.WriteLine("[Ascension] Usage: /asc license set <key> | clear");
        }

        private static void HandleSetItem(string[] args)
        {
            if (args.Length < 3)
            {
                Chat.WriteLine("[Ascension] Usage: /asc setitem <slot> <itemId>");
                return;
            }

            string slot = args[1];
            string itemId = args[2];

            _core.SetItem(slot, itemId);
            Chat.WriteLine($"[Ascension] Bound: {slot} -> {itemId}");
        }

        private static void HandleRoutine(string[] args)
        {
            if (args.Length < 2)
            {
                Chat.WriteLine("[Ascension] Usage: /asc routine start|stop");
                return;
            }

            if (args[1].Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                _core.StartRoutine();
                Chat.WriteLine("[Ascension] Routine started.");
                return;
            }

            if (args[1].Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                _core.StopRoutine();
                Chat.WriteLine("[Ascension] Routine stopped.");
                return;
            }

            Chat.WriteLine("[Ascension] Usage: /asc routine start|stop");
        }

        private static void HandleAutoRedo(string[] args)
        {
            if (args.Length < 2)
            {
                Chat.WriteLine("[Ascension] Usage: /asc autoredo on|off");
                return;
            }

            bool on = IsTrue(args[1]);
            _core.AutoRedo = on;
            Chat.WriteLine($"[Ascension] AutoRedo {(on ? "ON" : "OFF")}");
        }

        private static bool EnsureLicensed()
        {
            if (_lic.IsLicensed) return true;
            Chat.WriteLine("[Ascension] Feature locked. /asc license set <key>");
            return false;
        }

        private static bool IsTrue(string s)
            => s.Equals("1") || s.Equals("on", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class AscensionCore
    {
        private readonly LicenseManager _lic;
        private readonly object _lock = new object();

        public bool AutoRedo { get; set; }
        public bool IsRunning { get; private set; }

        private readonly Dictionary<string, string> _bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int BindingCount { get { lock (_lock) return _bindings.Count; } }

        public AscensionCore(LicenseManager lic)
        {
            _lic = lic;
        }

        public void SetItem(string slot, string itemId)
        {
            lock (_lock)
            {
                _bindings[slot] = itemId;
            }
        }

        public void StartRoutine()
        {
            if (!_lic.IsLicensed)
                return;

            IsRunning = true;
        }

        public void StopRoutine()
        {
            IsRunning = false;
        }
    }

    internal sealed class LicenseManager
    {
        public bool IsValid { get; private set; }
        public bool IsExpired => IsValid && DateTime.UtcNow.Date > ExpirationUtc.Date;

        public bool DevBypass
        {
            get
            {
                try
                {
#if DEBUG
                    return true;
#else
                    return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Ascension.dev"));
#endif
                }
                catch { return false; }
            }
        }

        public bool IsLicensed => (IsValid && !IsExpired) || DevBypass;

        public DateTime ExpirationUtc { get; private set; } = DateTime.MinValue;
        public string CharacterName { get; private set; } = "";
        public string RawKey { get; private set; } = "";

        private readonly string _path;

        private const string PublicKeyXml =
            "<RSAKeyValue>" +
            "<Modulus>PASTE_YOUR_PUBLIC_KEY_MODULUS_HERE</Modulus>" +
            "<Exponent>AQAB</Exponent>" +
            "</RSAKeyValue>";

        private static readonly Encoding Enc = Encoding.UTF8;

        public LicenseManager(string path)
        {
            _path = path;
        }

        public bool TryLoad()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    IsValid = false;
                    RawKey = "";
                    ExpirationUtc = DateTime.MinValue;
                    CharacterName = "";
                    return false;
                }

                string key = File.ReadAllText(_path).Trim();
                return Apply(key);
            }
            catch
            {
                IsValid = false;
                return false;
            }
        }

        public bool SetAndSave(string key)
        {
            if (!Apply(key))
                return false;

            try
            {
                File.WriteAllText(_path, key);
                return true;
            }
            catch
            {
                IsValid = false;
                return false;
            }
        }

        public void Clear()
        {
            IsValid = false;
            RawKey = "";
            ExpirationUtc = DateTime.MinValue;
            CharacterName = "";

            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }

        private bool Apply(string key)
        {
            if (!TryVerify(key, out var character, out var exp))
            {
                IsValid = false;
                RawKey = "";
                ExpirationUtc = DateTime.MinValue;
                CharacterName = "";
                return false;
            }

            IsValid = true;
            RawKey = key;
            ExpirationUtc = exp;
            CharacterName = character;
            return true;
        }

        private static bool TryVerify(string key, out string character, out DateTime exp)
        {
            character = "";
            exp = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            var parts = key.Split('.');
            if (parts.Length != 2)
                return false;

            byte[] payload, signature;
            try
            {
                payload = Convert.FromBase64String(parts[0]);
                signature = Convert.FromBase64String(parts[1]);
            }
            catch
            {
                return false;
            }

            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(PublicKeyXml);
                    bool ok = rsa.VerifyData(payload, new SHA256CryptoServiceProvider(), signature);
                    if (!ok)
                        return false;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                string text = Enc.GetString(payload);
                var tokens = text.Split('|');
                if (tokens.Length < 2)
                    return false;

                character = tokens[0];

                if (!DateTime.TryParse(tokens[1], out var d))
                    return false;

                exp = DateTime.SpecifyKind(d, DateTimeKind.Utc);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
