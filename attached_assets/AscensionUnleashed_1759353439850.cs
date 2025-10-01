// Ascension.cs
// Single-file plugin: Entry + Pairing + Licensing (RS256) + CommandBridge
// Target: .NET Framework 4.7.2  (x86)
// Refs: AOSharp.Core, AOSharp.Common, AOSharp.Bootstrap, System, System.Core, System.Runtime.Serialization, System.Security

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using AOSharp.Core;
using AOSharp.Core.UI;

namespace Ascension
{
    // [PluginInfo("Ascension", "RubiKit", "1.0.0")]
    public class AscensionPlugin : AOPlugin
    {
        private const int PORT = 8778;
        private const string ORIGIN = "http://127.0.0.1:8780"; // set to your RubiKitOS origin, or "*" for dev

        protected override void OnInitialize()
        {
            Chat.WriteLine("[Ascension] Initializing…");

            Pairing.BindLan = false;
            Pairing.Port = PORT;
            Pairing.AllowOrigin = ORIGIN;
            Pairing.Init();

            Licensing.Port = PORT;
            Licensing.AllowOrigin = ORIGIN;
            Licensing.Init();

            CommandBridge.Port = PORT;
            CommandBridge.AllowOrigin = ORIGIN;
            CommandBridge.Init();

            Chat.WriteLine("[Ascension] Ready on http://127.0.0.1:" + PORT);
        }

        protected override void OnDisposed()
        {
            try { Pairing.Stop(); } catch { }
            try { CommandBridge.Shutdown(); } catch { }
            Chat.WriteLine("[Ascension] Disposed.");
        }
    }

    // ----------------------- JSON helper (no System.Web) -----------------------
    internal static class JsonHelper
    {
        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            var ser = new DataContractJsonSerializer(obj.GetType());
            using (var ms = new MemoryStream())
            { ser.WriteObject(ms, obj); return Encoding.UTF8.GetString(ms.ToArray()); }
        }
        public static T Deserialize<T>(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            { return (T)ser.ReadObject(ms); }
        }
        public static object DeserializeObject(string json, Type t)
        {
            var ser = new DataContractJsonSerializer(t);
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            { return ser.ReadObject(ms); }
        }
    }

    // ------------------------------ Pairing (HS256 caps) ------------------------------
    [DataContract]
    internal class CapPayload
    {
        [DataMember] public string sub;
        [DataMember] public string iss;
        [DataMember] public string nonce;
        [DataMember] public long iat;
        [DataMember] public long exp;
        [DataMember] public string[] scopes;
    }

    internal static class Pairing
    {
        public static bool BindLan = false;
        public static int Port = 8778;
        public static int PairWindowMinutes = 5;
        public static int CapTTLMinutes = 30;
        public static string Issuer = "Ascension";
        public static string[] DefaultScopes = new[] { "hud.read", "chat.send", "macro.run" };
        public static string AllowOrigin = "http://127.0.0.1:8780";

        private static HttpListener _http;
        private static Thread _thread;
        private static int _init;

        private static readonly string[] _words = new[] {
            "amber","apex","arch","atlas","aurora","axiom","binary","blaze","cinder","cobalt","comet","crux",
            "delta","ember","falcon","flux","gamma","glint","helios","ionic","jade","kepler","lambda","lumen",
            "matrix","nova","onyx","oracle","phoenix","quantum","quartz","raven","relay","sable","sigma","sol",
            "tango","umbra","valor","vega","vertex","vortex","xenon","zephyr"
        };

        private static byte[] _serverKey;
        private static byte[] _pairSeed;
        private static readonly HashSet<string> _revoked = new HashSet<string>();

        public static void Init(byte[] serverKey = null, byte[] pairSeed = null)
        {
            if (Interlocked.Exchange(ref _init, 1) == 1) return;
            _serverKey = serverKey ?? RandomKey(32);
            _pairSeed = pairSeed ?? RandomKey(32);

            _thread = new Thread(ServerLoop) { IsBackground = true, Name = "Ascension-Pairing" };
            _thread.Start();

            Log("Pairing service ready.");
        }

        public static void Stop()
        {
            try { _http?.Stop(); _http?.Close(); } catch { }
        }

        public static bool TryAuthorize(HttpListenerRequest req, string requiredScope, out string denyReason)
        {
            denyReason = null;
            var cap = (req.Headers["Authorization"] ?? "").Trim();
            if (cap.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) cap = cap.Substring(7).Trim();

            if (string.IsNullOrEmpty(cap)) { denyReason = "missing_capability"; return false; }
            lock (_revoked) if (_revoked.Contains(cap)) { denyReason = "revoked"; return false; }

            CapPayload payload; string err;
            if (!TryVerifyCap(cap, out payload, out err)) { denyReason = err; return false; }
            if (payload.exp < Now()) { denyReason = "expired"; return false; }
            if (payload.scopes == null || Array.IndexOf(payload.scopes, requiredScope) < 0) { denyReason = "insufficient_scope"; return false; }
            return true;
        }

        private static void ServerLoop()
        {
            _http = new HttpListener();
            var host = BindLan ? "+" : "127.0.0.1";
            _http.Prefixes.Add($"http://{host}:{Port}/api/pairinfo/");
            _http.Prefixes.Add($"http://{host}:{Port}/api/pair/");
            _http.Prefixes.Add($"http://{host}:{Port}/api/capability/");
            _http.Prefixes.Add($"http://{host}:{Port}/api/revoke/");
            try { _http.Start(); } catch (HttpListenerException ex) { Log("Bind failed: " + ex.Message); return; }
            Log($"Listening on {(BindLan ? "LAN" : "local")}:{Port}");

            while (_http.IsListening)
            {
                HttpListenerContext ctx = null;
                try { ctx = _http.GetContext(); } catch { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        [DataContract]
        private class PairReq
        {
            [DataMember] public string code;
            [DataMember] public string[] scopes;
            [DataMember] public int ttlMin;
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod == "OPTIONS") { Cors(ctx); ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }
                var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

                if (path.StartsWith("/api/pairinfo"))
                {
                    Cors(ctx); JsonResp(ctx, BuildPairInfo()); return;
                }

                if (path.StartsWith("/api/pair") && ctx.Request.HttpMethod == "POST")
                {
                    var body = ReadBody(ctx);
                    var req = JsonHelper.Deserialize<PairReq>(body);
                    var code = (req.code ?? "").Trim();
                    var scopes = req.scopes ?? DefaultScopes;
                    var ttlMin = req.ttlMin > 0 ? req.ttlMin : CapTTLMinutes;

                    if (string.IsNullOrEmpty(code)) { Bad(ctx, "missing_code"); return; }
                    if (!IsCurrentCode(code)) { Bad(ctx, "invalid_or_stale_code"); return; }

                    var payload = new CapPayload
                    {
                        sub = "module",
                        iss = "Ascension",
                        iat = Now(),
                        exp = Now() + ttlMin * 60,
                        scopes = scopes,
                        nonce = Guid.NewGuid().ToString("N")
                    };
                    var cap = CreateCap(payload);
                    Cors(ctx); JsonResp(ctx, new { cap, exp = payload.exp, scopes = payload.scopes }); return;
                }

                if (path.StartsWith("/api/capability") && ctx.Request.HttpMethod == "POST")
                {
                    string deny;
                    if (!TryAuthorize(ctx.Request, "hud.read", out deny)) { Forbidden(ctx, deny); return; }

                    var payload = new CapPayload
                    {
                        sub = "module",
                        iss = "Ascension",
                        iat = Now(),
                        exp = Now() + CapTTLMinutes * 60,
                        scopes = DefaultScopes,
                        nonce = Guid.NewGuid().ToString("N")
                    };
                    var cap = CreateCap(payload);
                    Cors(ctx); JsonResp(ctx, new { cap, exp = payload.exp, scopes = payload.scopes }); return;
                }

                if (path.StartsWith("/api/revoke") && ctx.Request.HttpMethod == "POST")
                {
                    var body = ReadBody(ctx);
                    // simple revoke accepts {"cap":"..."} or empty (no-op)
                    try
                    {
                        var dict = JsonHelper.Deserialize<Dictionary<string, string>>(body);
                        string cap; if (dict != null && dict.TryGetValue("cap", out cap) && !string.IsNullOrEmpty(cap))
                            lock (_revoked) _revoked.Add(cap);
                    }
                    catch { }
                    Cors(ctx); JsonResp(ctx, new { ok = true }); return;
                }

                NotFound(ctx);
            }
            catch (Exception ex)
            {
                try { Cors(ctx); ctx.Response.StatusCode = 500; JsonResp(ctx, new { error = ex.Message }); } catch { }
            }
        }

        private static object BuildPairInfo()
        {
            var code = CurrentCode();
            var until = WindowEndUnix();
            var host = BindLan ? GetHostForClients() : "127.0.0.1";
            var uri = $"ascension://pair?host={host}&port={Port}&code={Uri.EscapeDataString(code)}&exp={until}";
            return new { code, host, port = Port, exp = until, qr = new { scheme = "ascension", url = uri }, scopes = DefaultScopes, windowMins = PairWindowMinutes, capTTL = CapTTLMinutes };
        }

        private static string CurrentCode() => CodeForWindow(CurrentWindow());
        private static bool IsCurrentCode(string code)
        {
            var w = CurrentWindow();
            return code.Equals(CodeForWindow(w), StringComparison.OrdinalIgnoreCase)
                || code.Equals(CodeForWindow(w - 1), StringComparison.OrdinalIgnoreCase);
        }
        private static long CurrentWindow() { var now = Now(); return now / (PairWindowMinutes * 60); }
        private static long WindowEndUnix() { var w = CurrentWindow(); var len = PairWindowMinutes * 60; return (w + 1) * len; }

        private static string CodeForWindow(long window)
        {
            var msg = BitConverter.GetBytes(window);
            using (var h = new HMACSHA256(_pairSeed))
            {
                var bytes = h.ComputeHash(msg);
                var parts = new List<string>(6);
                var idx = 0;
                for (int i = 0; i < 6; i++)
                {
                    int val = (bytes[idx] << 8) | bytes[idx + 1];
                    idx += 2;
                    var w = _words[val % _words.Length];
                    parts.Add(w);
                }
                return string.Join("-", parts);
            }
        }

        private static string GetHostForClients()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName())
                              .AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return host?.ToString() ?? "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }

        // CAP (HS256)
        private static string CreateCap(CapPayload p)
        {
            var header = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"CAP\"}"));
            var payload = B64Url(Encoding.UTF8.GetBytes(JsonHelper.Serialize(p)));
            var mac = Sign(header + "." + payload);
            return header + "." + payload + "." + mac;
        }
        private static bool TryVerifyCap(string cap, out CapPayload payload, out string err)
        {
            payload = null; err = null;
            var parts = cap.Split('.'); if (parts.Length != 3) { err = "format"; return false; }
            var expect = Sign(parts[0] + "." + parts[1]);
            if (!TimingEq(parts[2], expect)) { err = "sig"; return false; }

            try { payload = JsonHelper.Deserialize<CapPayload>(Encoding.UTF8.GetString(B64UrlDecode(parts[1]))); }
            catch { err = "payload"; return false; }
            if (payload.iss != "Ascension") { err = "issuer"; return false; }
            return true;
        }

        // utils
        private static string ReadBody(HttpListenerContext ctx) { using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding)) return sr.ReadToEnd(); }
        private static void JsonResp(HttpListenerContext ctx, object obj)
        {
            var b = Encoding.UTF8.GetBytes(JsonHelper.Serialize(obj));
            Cors(ctx);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = b.Length;
            using (var s = ctx.Response.OutputStream) s.Write(b, 0, b.Length);
        }
        private static void Cors(HttpListenerContext ctx)
        {
            ctx.Response.AddHeader("Access-Control-Allow-Origin", string.IsNullOrEmpty(AllowOrigin) ? "*" : AllowOrigin);
            ctx.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }
        private static void NotFound(HttpListenerContext ctx) { Cors(ctx); ctx.Response.StatusCode = 404; JsonResp(ctx, new { error = "not_found" }); }
        private static void Bad(HttpListenerContext ctx, string msg) { Cors(ctx); ctx.Response.StatusCode = 400; JsonResp(ctx, new { error = msg }); }

        private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] B64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }
        private static string Sign(string s) { using (var h = new HMACSHA256(_serverKey)) return B64Url(h.ComputeHash(Encoding.UTF8.GetBytes(s))); }
        private static bool TimingEq(string a, string b) { if (a.Length != b.Length) return false; int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0; }
        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private static byte[] RandomKey(int n) { var b = new byte[n]; using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(b); return b; }
        private static void Log(string s) { try { Chat.WriteLine("[Pairing] " + s); } catch { } }
    }

    // ------------------------------ Licensing (RS256) ------------------------------
    [DataContract]
    internal class LicensePayload
    {
        [DataMember] public string sub;
        [DataMember] public string tier;
        [DataMember] public string device;
        [DataMember] public string nonce;
        [DataMember] public long iat;
        [DataMember] public long exp;
        [DataMember] public long grace;
        [DataMember] public string[] features;
    }

    internal static class Licensing
    {
        public static string AllowOrigin = "http://127.0.0.1:8780";
        public static int Port = 8778;

        public static string LicenseFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ascension", "license.bin");

        // Paste your full public_key.xml text between the quotes below:
        private const string PUBLIC_KEY_XML = @"PASTE_YOUR_PUBLIC_KEY_XML_HERE";

        private static HttpListener _http;
        private static RSA _rsa;
        private static LicensePayload _active;
        private static readonly object _sync = new object();

        public static void Init()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LicenseFile));

            _rsa = RSA.Create();
            try { _rsa.FromXmlString(PUBLIC_KEY_XML); }
            catch (Exception ex) { Log("Public key import failed: " + ex.Message); }

            LoadStored();
            StartHttp();
        }

        public static bool Enforce(string feature, out string deny)
        {
            deny = null;
            if (!IsValidNow(out var reason)) { deny = reason; return false; }
            if (!DeviceMatches()) { deny = "invalid_device"; return false; }
            if (!FeatureAllowed(feature)) { deny = "feature_denied"; return false; }
            return true;
        }

        public static bool IsValidNow(out string reason)
        {
            lock (_sync)
            {
                if (_active == null) { reason = "no_license"; return false; }
                var now = Now();
                if (now <= _active.exp) { reason = null; return true; }
                var graceEnd = _active.exp + Math.Max(0, _active.grace);
                if (now <= graceEnd) { reason = "expired_in_grace"; return true; }
                reason = "expired"; return false;
            }
        }
        public static bool FeatureAllowed(string f)
        {
            lock (_sync)
            {
                if (_active == null || _active.features == null) return false;
                if (_active.features.Contains("*")) return true;
                return _active.features.Contains(f);
            }
        }
        public static bool DeviceMatches()
        {
            lock (_sync)
            {
                if (_active == null || string.IsNullOrEmpty(_active.device) || _active.device == "*") return true;
                return _active.device.Equals(DeviceFingerprint(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void StartHttp()
        {
            try
            {
                _http = new HttpListener();
                _http.Prefixes.Add($"http://127.0.0.1:{Port}/api/license/status/");
                _http.Prefixes.Add($"http://127.0.0.1:{Port}/api/license/activate/");
                _http.Prefixes.Add($"http://127.0.0.1:{Port}/api/license/revoke/");
                _http.Start();
            }
            catch (HttpListenerException ex) { Log("HTTP bind failed: " + ex.Message); return; }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (_http.IsListening)
                {
                    HttpListenerContext ctx = null;
                    try { ctx = _http.GetContext(); } catch { break; }
                    ThreadPool.QueueUserWorkItem(__ => Handle(ctx));
                }
            });
            Log("Licensing HTTP ready.");
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod == "OPTIONS") { Cors(ctx); ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }
                var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

                if (path.StartsWith("/api/license/status"))
                {
                    LicensePayload snap; lock (_sync) snap = _active == null ? null : Clone(_active);
                    var ok = false; string reason = "no_license";
                    if (snap != null)
                    {
                        if (DeviceMatches())
                        {
                            string r; ok = IsValidNow(out r); reason = ok ? "ok" : r ?? "invalid_device";
                        }
                        else { ok = false; reason = "invalid_device"; }
                    }
                    Cors(ctx); JsonResp(ctx, new
                    {
                        ok,
                        reason,
                        tier = snap?.tier,
                        exp = snap?.exp ?? 0,
                        grace = snap?.grace ?? 0,
                        features = snap?.features,
                        device = snap?.device
                    }); return;
                }

                if (path.StartsWith("/api/license/activate") && ctx.Request.HttpMethod == "POST")
                {
                    var token = ReadBody(ctx).Trim();
                    if (string.IsNullOrEmpty(token)) { Bad(ctx, "empty_license"); return; }
                    string err;
                    LicensePayload payload;
                    if (!TryVerify(token, out payload, out err)) { Bad(ctx, "verify_failed:" + err); return; }
                    if (payload.sub != "Ascension") { Bad(ctx, "issuer_mismatch"); return; }
                    if (!string.IsNullOrEmpty(payload.device) && payload.device != "*" && payload.device != DeviceFingerprint()) { Bad(ctx, "device_mismatch"); return; }

                    StoreLicense(token);
                    lock (_sync) _active = payload;
                    Cors(ctx); JsonResp(ctx, new { ok = true, exp = payload.exp, tier = payload.tier, features = payload.features }); return;
                }

                if (path.StartsWith("/api/license/revoke") && ctx.Request.HttpMethod == "POST")
                {
                    try { if (File.Exists(LicenseFile)) File.Delete(LicenseFile); } catch { }
                    lock (_sync) _active = null;
                    Cors(ctx); JsonResp(ctx, new { ok = true }); return;
                }

                NotFound(ctx);
            }
            catch (Exception ex)
            {
                try { Cors(ctx); ctx.Response.StatusCode = 500; JsonResp(ctx, new { error = ex.Message }); } catch { }
            }
        }

        // Verify RS256 (header.alg == "RS256")
        private static bool TryVerify(string token, out LicensePayload payload, out string err)
        {
            payload = null; err = null;
            var parts = token.Split('.'); if (parts.Length != 3) { err = "format"; return false; }

            byte[] headerBytes, payloadBytes, sig;
            try
            {
                headerBytes = B64UrlDecode(parts[0]);
                payloadBytes = B64UrlDecode(parts[1]);
                sig = B64UrlDecode(parts[2]);
            }
            catch { err = "b64"; return false; }

            var headerJson = Encoding.UTF8.GetString(headerBytes);
            if (headerJson.IndexOf("\"RS256\"", StringComparison.OrdinalIgnoreCase) < 0) { err = "alg"; return false; }

            var data = Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]);
            bool ok;
            try { ok = _rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256"), sig); }
            catch { err = "verify_exc"; return false; }
            if (!ok) { err = "sig"; return false; }

            try { payload = JsonHelper.Deserialize<LicensePayload>(Encoding.UTF8.GetString(payloadBytes)); }
            catch { err = "payload"; return false; }

            return true;
        }

        private static void StoreLicense(string token)
        {
            try
            {
                var plain = Encoding.UTF8.GetBytes(token);
                var blob = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); // DPAPI
                File.WriteAllBytes(LicenseFile, blob);
            }
            catch (Exception ex) { Log("StoreLicense: " + ex.Message); }
        }
        private static void LoadStored()
        {
            try
            {
                if (!File.Exists(LicenseFile)) return;
                var blob = File.ReadAllBytes(LicenseFile);
                var plain = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
                string err; LicensePayload p;
                if (TryVerify(Encoding.UTF8.GetString(plain), out p, out err))
                    lock (_sync) _active = p;
                else Log("Stored license invalid: " + err);
            }
            catch { }
        }

        private static LicensePayload Clone(LicensePayload p)
        {
            return new LicensePayload
            {
                sub = p.sub,
                tier = p.tier,
                device = p.device,
                nonce = p.nonce,
                iat = p.iat,
                exp = p.exp,
                grace = p.grace,
                features = p.features == null ? null : (string[])p.features.Clone()
            };
        }

        private static string DeviceFingerprint()
        {
            try
            {
                var machineGuid = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "") as string ?? "";
                var user = Environment.UserName ?? "";
                var basis = machineGuid + "|" + user;
                using (var sha = SHA256.Create())
                {
                    var h = sha.ComputeHash(Encoding.UTF8.GetBytes(basis));
                    return BitConverter.ToString(h, 0, 16).Replace("-", "").ToLowerInvariant();
                }
            }
            catch { return "unknown"; }
        }

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private static byte[] B64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }
        private static void Cors(HttpListenerContext ctx)
        {
            ctx.Response.AddHeader("Access-Control-Allow-Origin", string.IsNullOrEmpty(AllowOrigin) ? "*" : AllowOrigin);
            ctx.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }
        private static string ReadBody(HttpListenerContext ctx) { using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding)) return sr.ReadToEnd(); }
        private static void JsonResp(HttpListenerContext ctx, object obj)
        {
            var b = Encoding.UTF8.GetBytes(JsonHelper.Serialize(obj));
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = b.Length; Cors(ctx);
            using (var s = ctx.Response.OutputStream) s.Write(b, 0, b.Length);
        }
        private static void NotFound(HttpListenerContext ctx) { Cors(ctx); ctx.Response.StatusCode = 404; JsonResp(ctx, new { error = "not_found" }); }
        private static void Bad(HttpListenerContext ctx, string msg) { Cors(ctx); ctx.Response.StatusCode = 400; JsonResp(ctx, new { error = msg }); }
        private static void Log(string m) { try { Chat.WriteLine("[Ascension/Lic] " + m); } catch { } }
    }

    // ------------------------------ Command Bridge ------------------------------
    [DataContract]
    internal class CommandReq
    {
        [DataMember] public string type;
        [DataMember] public Dictionary<string, object> args;
        [DataMember] public string id;
    }
    [DataContract]
    internal class CommandRes
    {
        [DataMember] public string id;
        [DataMember] public string status;
        [DataMember] public string message;
        [DataMember] public object data;
    }

    internal static class CommandBridge
    {
        public static int Port = 8778;
        public static string AllowOrigin = "http://127.0.0.1:8780";

        private static HttpListener _http;
        private static Thread _thread;

        public static void Init()
        {
            _thread = new Thread(ServerLoop) { IsBackground = true, Name = "Ascension-CommandBridge" };
            _thread.Start();
        }

        public static void Shutdown()
        {
            try { _http?.Stop(); _http?.Close(); } catch { }
        }

        private static void ServerLoop()
        {
            _http = new HttpListener();
            _http.Prefixes.Add($"http://127.0.0.1:{Port}/api/command/");
            _http.Prefixes.Add($"http://127.0.0.1:{Port}/api/commands/");
            try { _http.Start(); } catch (HttpListenerException ex) { Chat.WriteLine("[Ascension/Command] Bind failed: " + ex.Message); return; }

            while (_http.IsListening)
            {
                HttpListenerContext ctx = null;
                try { ctx = _http.GetContext(); } catch { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod == "OPTIONS") { Cors(ctx); ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

                var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
                if ((path.StartsWith("/api/command") || path.StartsWith("/api/commands")) && ctx.Request.HttpMethod == "POST")
                {
                    string deny;
                    if (!Pairing.TryAuthorize(ctx.Request, "macro.run", out deny))
                    { Cors(ctx); ctx.Response.StatusCode = 403; WriteJson(ctx, new { error = "capability_" + deny }); return; }

                    string lic;
                    if (!Licensing.Enforce("macro.run", out lic))
                    { Cors(ctx); ctx.Response.StatusCode = 403; WriteJson(ctx, new { error = "license_" + lic }); return; }

                    string body; using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding)) body = sr.ReadToEnd();

                    // handle array or single
                    object result;
                    if (body.TrimStart().StartsWith("["))
                    {
                        var ser = new DataContractJsonSerializer(typeof(CommandReq[]));
                        CommandReq[] arr;
                        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body))) arr = (CommandReq[])ser.ReadObject(ms);
                        var list = new List<object>();
                        foreach (var r in arr) list.Add(Dispatch(r));
                        result = list.ToArray();
                    }
                    else
                    {
                        var ser = new DataContractJsonSerializer(typeof(CommandReq));
                        CommandReq req;
                        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body))) req = (CommandReq)ser.ReadObject(ms);
                        result = Dispatch(req);
                    }

                    Cors(ctx); WriteJson(ctx, result); return;
                }

                Cors(ctx); ctx.Response.StatusCode = 404; WriteJson(ctx, new { error = "not_found" });
            }
            catch (Exception ex)
            {
                try { Cors(ctx); ctx.Response.StatusCode = 500; WriteJson(ctx, new { error = ex.Message }); } catch { }
            }
        }

        private static void Cors(HttpListenerContext ctx)
        {
            ctx.Response.AddHeader("Access-Control-Allow-Origin", string.IsNullOrEmpty(AllowOrigin) ? "*" : AllowOrigin);
            ctx.Response.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
            ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }

        private static void WriteJson(HttpListenerContext ctx, object obj)
        {
            var json = JsonHelper.Serialize(obj);
            var b = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = b.Length;
            using (var s = ctx.Response.OutputStream) s.Write(b, 0, b.Length);
        }

        private static object Dispatch(CommandReq req)
        {
            try
            {
                var t = (req.type ?? "").ToLowerInvariant();
                switch (t)
                {
                    case "ping": return Ok(req, "pong", new { ts = DateTime.UtcNow });
                    case "chat": return HandleChat(req);
                    case "macro": return HandleMacro(req);
                    default: return Err(req, "unknown_type");
                }
            }
            catch (Exception ex)
            {
                return Err(req, ex.Message);
            }
        }

        private static object HandleChat(CommandReq req)
        {
            var ch = Get(req.args, "channel", "vicinity");
            var text = Get(req.args, "text", "");
            if (string.IsNullOrWhiteSpace(text)) return Err(req, "missing_text");

            try
            {
                // TODO: replace with your actual "send chat" call in AOSharp.
                // For now, just echo to client log:
                Chat.WriteLine("[Ascension→" + ch + "] " + text);
            }
            catch { Chat.WriteLine("[Ascension] Chat send stub failed"); }
            return Ok(req, "sent", new { channel = ch, text });
        }

        private static object HandleMacro(CommandReq req)
        {
            var name = Get(req.args, "name", "");
            if (string.IsNullOrEmpty(name)) return Err(req, "missing_name");

            // TODO: wire to your real macro runner
            Chat.WriteLine("[Ascension] Running macro: " + name);
            return Ok(req, "started", new { name });
        }

        private static T Get<T>(Dictionary<string, object> d, string k, T defVal)
        {
            if (d == null) return defVal;
            object v; if (!d.TryGetValue(k, out v) || v == null) return defVal;
            try { return (T)Convert.ChangeType(v, typeof(T)); } catch { return defVal; }
        }

        private static object Ok(CommandReq req, string msg, object data = null) => new CommandRes { id = req.id, status = "ok", message = msg, data = data };
        private static object Err(CommandReq req, string msg) => new CommandRes { id = req.id, status = "error", message = msg, data = null };
    }
}
