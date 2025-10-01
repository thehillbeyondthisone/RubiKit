// Ascension License Issuer (Offline · RSA XML) — script.js
// Parity v1: RSASSA-PKCS1-v1_5(SHA-256), Base64(payload + 0x1E + signature)
// Payload order: ver=1;char=...;exp=ISO;tick=HEX;features=...
// Duration capped to 12h. Tick = SHA256(floor(now/T) + "|" + SALT), accept current/prev slot.

(function () {
  // ---------- Parity defaults (keep in sync with plugin) ----------
  const DEFAULTS = {
    TICK_WINDOW_SEC: 600,
    BUILD_SALT: "A6F0B5C4-2E91-4B2C-9E7D-0C2F9B2A1D77", // change if you changed it in the plugin
    MAX_DURATION_SEC: 43200 // 12h
  };

  // ---------- DOM ----------
  const $ = (id) => document.getElementById(id);

  const status = $("status");
  const keyStatus = $("keystatus");
  const remember = $("remember");

  // Key management
  const taPrivXml = $("privXml");
  const taPubXmlOut = $("pubXmlOut");
  const btnImportPrivXml = $("importPrivXml");
  const btnExportPrivXml = $("exportPrivXml");
  const btnCopyPubXml = $("copyPubXml");

  // (new) key generator & helpers
  const btnGenRsa = $("genRsa");
  const btnCopyPrivXml = $("copyPrivXml");
  const btnExportPubXml = $("exportPubXml");

  // Salt builder
  const inpBuildSalt = $("buildSalt");
  const btnGenSaltGuid = $("genSaltGuid");
  const btnGenSaltHex = $("genSaltHex");
  const btnCopySalt = $("copySalt");

  // License issue
  const inpCharacter = $("character");
  const inpHours = $("hours");
  const inpMinutes = $("minutes");
  const inpNote = $("note");

  const btnIssue = $("issue");
  const btnClear = $("clear");
  const outWrap = $("out");
  const outChar = $("oChar");
  const outExp = $("oExp");
  const taToken = $("token");
  const btnCopyTok = $("copy");

  // ---------- State ----------
  let rsaPrivateKey = null; // CryptoKey for RSASSA-PKCS1-v1_5 sign
  let jwkPriv = null;       // cached JWK (so we can derive public XML & parity hints)

  // ---------- Utils ----------
  const enc = new TextEncoder();

  const setStatus = (s, bad) => {
    status.textContent = s;
    status.style.color = bad ? "#ff9090" : "";
  };
  const setKeyStatus = (s) => {
    keyStatus.textContent = s;
  };

  function b64ToUint8(b64) {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
  }
  function uint8ToB64(arr) {
    let s = "";
    for (let i = 0; i < arr.length; i++) s += String.fromCharCode(arr[i]);
    return btoa(s);
  }
  function b64ToB64Url(b64) {
    return b64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
  }
  function b64UrlToB64(b64u) {
    let s = b64u.replace(/-/g, "+").replace(/_/g, "/");
    while (s.length % 4) s += "=";
    return s;
  }

  // XML helpers
  function buildPublicXml(modulusB64, exponentB64) {
    return ["<RSAKeyValue>", `<Modulus>${modulusB64}</Modulus>`, `<Exponent>${exponentB64}</Exponent>`, "</RSAKeyValue>"].join("");
  }
  function buildPrivateXml(parts) {
    // parts: {n,e,d,p,q,dp,dq,qi} in base64url or base64; we’ll accept base64url and convert
    const n = parts.n_b64 || b64UrlToB64(parts.n);
    const e = parts.e_b64 || b64UrlToB64(parts.e);
    const d = parts.d_b64 || b64UrlToB64(parts.d);
    const p = parts.p_b64 || b64UrlToB64(parts.p);
    const q = parts.q_b64 || b64UrlToB64(parts.q);
    const dp = parts.dp_b64 || b64UrlToB64(parts.dp);
    const dq = parts.dq_b64 || b64UrlToB64(parts.dq);
    const qi = parts.qi_b64 || b64UrlToB64(parts.qi);
    return [
      "<RSAKeyValue>",
      `<Modulus>${n}</Modulus>`,
      `<Exponent>${e}</Exponent>`,
      `<P>${p}</P>`,
      `<Q>${q}</Q>`,
      `<DP>${dp}</DP>`,
      `<DQ>${dq}</DQ>`,
      `<InverseQ>${qi}</InverseQ>`,
      `<D>${d}</D>`,
      "</RSAKeyValue>"
    ].join("");
  }
  function parseRsaPrivateXml(xml) {
    const get = (tag) => {
      const m = new RegExp(`<${tag}>\\s*([\\s\\S]*?)\\s*</${tag}>`, "i").exec(xml);
      return m ? m[1].trim() : null;
    };
    const Modulus = get("Modulus");
    const Exponent = get("Exponent");
    const D = get("D");
    const P = get("P");
    const Q = get("Q");
    const DP = get("DP");
    const DQ = get("DQ");
    const InverseQ = get("InverseQ");
    if (!Modulus || !Exponent || !D || !P || !Q || !DP || !DQ || !InverseQ) {
      throw new Error("Invalid RSA PRIVATE KEY XML: missing one or more required elements.");
    }
    return { Modulus, Exponent, D, P, Q, DP, DQ, InverseQ };
    // all are standard Base64
  }

  // Import private XML to WebCrypto JWK
  async function importPrivateXml(xml) {
    const f = parseRsaPrivateXml(xml);
    const jwk = {
      kty: "RSA",
      n: b64ToB64Url(f.Modulus),
      e: b64ToB64Url(f.Exponent),
      d: b64ToB64Url(f.D),
      p: b64ToB64Url(f.P),
      q: b64ToB64Url(f.Q),
      dp: b64ToB64Url(f.DP),
      dq: b64ToB64Url(f.DQ),
      qi: b64ToB64Url(f.InverseQ),
      ext: true,
      key_ops: ["sign"],
      alg: "RS256"
    };
    const algo = { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" };
    rsaPrivateKey = await crypto.subtle.importKey("jwk", jwk, algo, true, ["sign"]);
    jwkPriv = jwk;
  }

  // Derive public XML from loaded private JWK
  function currentPublicXml() {
    if (!jwkPriv) throw new Error("No private key loaded.");
    return buildPublicXml(b64UrlToB64(jwkPriv.n), b64UrlToB64(jwkPriv.e));
  }

  // Compute tick hex
  async function computeTickHex(tickWindowSec, buildSalt) {
    const now = Math.floor(Date.now() / 1000);
    const slot = Math.floor(now / tickWindowSec);
    const text = `${slot}|${buildSalt}`;
    const digest = await crypto.subtle.digest("SHA-256", enc.encode(text));
    return Array.from(new Uint8Array(digest)).map((b) => b.toString(16).padStart(2, "0")).join("").toUpperCase();
  }

  function isoUtcFromSeconds(sec) {
    return new Date(sec * 1000).toISOString();
  }

  // Sign bytes with RSASSA-PKCS1-v1_5 / SHA-256
  async function signBytesRS256(bytes) {
    if (!rsaPrivateKey) throw new Error("No private key loaded.");
    const sig = await crypto.subtle.sign({ name: "RSASSA-PKCS1-v1_5" }, rsaPrivateKey, bytes);
    return new Uint8Array(sig);
  }

  // Sanitize features (no ';' or '='; collapse whitespace)
  function sanitizeFeatures(s) {
    return (s || "").replace(/[;=]/g, " ").replace(/\s+/g, " ").trim();
  }

  // Build payload (Parity v1) and sign to token
  async function buildAndSignToken({ character, seconds, note, tickWindow, buildSalt }) {
    if (seconds > DEFAULTS.MAX_DURATION_SEC) seconds = DEFAULTS.MAX_DURATION_SEC;
    if (seconds <= 0) throw new Error("Duration must be > 0.");
    const now = Math.floor(Date.now() / 1000);
    const exp = now + seconds;
    const tickHex = await computeTickHex(tickWindow, buildSalt);
    const features = sanitizeFeatures(note);

    // ORDER MATTERS (Parity v1): ver, char, exp, tick, features
    const payload =
      `ver=1;` +
      `char=${character};` +
      `exp=${isoUtcFromSeconds(exp)};` +
      `tick=${tickHex};` +
      `features=${features}`;

    const payloadBytes = enc.encode(payload);
    const signature = await signBytesRS256(payloadBytes);

    // Blob = payloadBytes + 0x1E + signature
    const blob = new Uint8Array(payloadBytes.length + 1 + signature.length);
    blob.set(payloadBytes, 0);
    blob[payloadBytes.length] = 0x1E;
    blob.set(signature, payloadBytes.length + 1);

    return { token: uint8ToB64(blob), expIso: isoUtcFromSeconds(exp), payload };
  }

  // ---------- Parity badge ----------
  function injectParityBadge() {
    const card = document.querySelector(".card");
    if (!card || document.getElementById("parityBadge")) return;
    const div = document.createElement("div");
    div.id = "parityBadge";
    div.style.cssText = "margin:6px 0 12px 0; font-size:12px; color:var(--muted); display:flex; gap:8px; align-items:center; flex-wrap:wrap;";
    div.innerHTML = `
      <span style="padding:2px 6px;border:1px solid var(--border);border-radius:6px;">Parity <strong>v1</strong></span>
      <span>Tick Window: <code id="pb_tw">${DEFAULTS.TICK_WINDOW_SEC}s</code></span>
      <span>Max Duration: <code>${DEFAULTS.MAX_DURATION_SEC/3600}h</code></span>
      <span>Salt: <code id="pb_salt">${DEFAULTS.BUILD_SALT}</code></span>
      <span>Key: <code id="pb_key">—</code></span>
      <button id="btnExportParity" style="margin-left:auto;">Export Parity JSON</button>
    `;
    card.insertBefore(div, card.children[1] || null);

    document.getElementById("btnExportParity").addEventListener("click", () => {
      const pubXml = (function(){ try { return currentPublicXml(); } catch { return ""; }})();
      const modulusHint = (function(){
        try {
          if (!jwkPriv) return "";
          const nBytes = b64ToUint8(b64UrlToB64(jwkPriv.n));
          const first16 = nBytes.slice(0,16);
          return uint8ToB64(first16);
        } catch { return ""; }
      })();
      const parity = {
        parity: 1,
        tick_window_sec: DEFAULTS.TICK_WINDOW_SEC,
        build_salt: inpBuildSalt.value || DEFAULTS.BUILD_SALT,
        max_duration_sec: DEFAULTS.MAX_DURATION_SEC,
        public_key_xml_present: !!pubXml,
        public_key_modulus_hint: modulusHint
      };
      const blob = new Blob([JSON.stringify(parity, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = "ascension.parity.json"; a.click();
      URL.revokeObjectURL(url);
    });
  }
  function refreshParityBadge() {
    const saltEl = document.getElementById("pb_salt");
    if (saltEl) saltEl.textContent = (inpBuildSalt.value || DEFAULTS.BUILD_SALT);
    const keyEl = document.getElementById("pb_key");
    if (keyEl) {
      try {
        if (!jwkPriv) { keyEl.textContent = "—"; return; }
        const nBytes = b64ToUint8(b64UrlToB64(jwkPriv.n));
        keyEl.textContent = "mod " + nBytes.length*8 + "b";
      } catch { keyEl.textContent = "—"; }
    }
    const twEl = document.getElementById("pb_tw");
    if (twEl) twEl.textContent = `${DEFAULTS.TICK_WINDOW_SEC}s`;
  }

  // ---------- RSA generation (XML) ----------
  async function generateRsaXml2048() {
    const algo = { name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([0x01,0x00,0x01]), hash: "SHA-256" };
    const kp = await crypto.subtle.generateKey(algo, true, ["sign","verify"]);
    rsaPrivateKey = kp.privateKey;
    const jwk = await crypto.subtle.exportKey("jwk", rsaPrivateKey);
    jwkPriv = jwk;

    const privXml = buildPrivateXml({ n: jwk.n, e: jwk.e, d: jwk.d, p: jwk.p, q: jwk.q, dp: jwk.dp, dq: jwk.dq, qi: jwk.qi });
    const pubXml  = buildPublicXml(b64UrlToB64(jwk.n), b64UrlToB64(jwk.e));

    taPrivXml.value = privXml;
    taPubXmlOut.value = pubXml;

    if (remember.checked) localStorage.setItem("asc_rsa_priv_xml", privXml);

    setKeyStatus("New RSA-2048 keypair generated.");
    refreshParityBadge();
  }

  // ---------- Salt helpers ----------
  function genGuidSalt() {
    // v4-ish GUID
    const s4 = () => Math.floor((1 + Math.random()) * 0x10000).toString(16).substring(1);
    return `${s4()}${s4()}-${s4()}-${s4()}-${s4()}-${s4()}${s4()}${s4()}`.toUpperCase();
  }
  function genHexSalt32() {
    const b = new Uint8Array(32);
    crypto.getRandomValues(b);
    return Array.from(b).map(x => x.toString(16).padStart(2,"0")).join("").toUpperCase();
  }

  // ---------- Events ----------
  btnImportPrivXml.addEventListener("click", async () => {
    try {
      const xml = (taPrivXml.value || "").trim();
      if (!xml) { setKeyStatus("Paste PRIVATE KEY XML first."); return; }
      await importPrivateXml(xml);
      if (remember.checked) localStorage.setItem("asc_rsa_priv_xml", xml);
      taPubXmlOut.value = currentPublicXml();
      setKeyStatus("Private key (RSA XML) loaded.");
      refreshParityBadge();
    } catch (e) { setKeyStatus("Import failed: " + e.message); }
  });

  btnExportPrivXml.addEventListener("click", async () => {
    try {
      const xml = (taPrivXml.value || "").trim();
      if (!xml) throw new Error("No private key XML in the box.");
      const blob = new Blob([xml], { type: "text/xml" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a"); a.href = url; a.download = "private_key.xml"; a.click();
      URL.revokeObjectURL(url);
      setKeyStatus("Private key XML downloaded.");
    } catch (e) { setKeyStatus("Export failed: " + e.message); }
  });

  btnCopyPubXml.addEventListener("click", async () => {
    try {
      const pubXml = currentPublicXml();
      taPubXmlOut.value = pubXml;
      await navigator.clipboard.writeText(pubXml);
      setKeyStatus("Public key XML copied to clipboard.");
    } catch (e) { setKeyStatus("Copy failed: " + e.message); }
  });

  // NEW: generate RSA, copy priv/public, export public
  btnGenRsa.addEventListener("click", async () => {
    try { await generateRsaXml2048(); } catch (e) { setKeyStatus("Generate failed: " + e.message); }
  });
  btnCopyPrivXml.addEventListener("click", async () => {
    try {
      const xml = (taPrivXml.value || "").trim();
      if (!xml) throw new Error("No private key XML in the box.");
      await navigator.clipboard.writeText(xml);
      setKeyStatus("Private key XML copied.");
    } catch (e) { setKeyStatus("Copy failed: " + e.message); }
  });
  btnExportPubXml.addEventListener("click", async () => {
    try {
      const pubXml = currentPublicXml();
      const blob = new Blob([pubXml], { type: "text/xml" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a"); a.href = url; a.download = "public_key.xml"; a.click();
      URL.revokeObjectURL(url);
      setKeyStatus("Public key XML downloaded.");
    } catch (e) { setKeyStatus("Export failed: " + e.message); }
  });

  // Salt builder
  btnGenSaltGuid.addEventListener("click", () => { inpBuildSalt.value = genGuidSalt(); refreshParityBadge(); });
  btnGenSaltHex.addEventListener("click", () => { inpBuildSalt.value = genHexSalt32(); refreshParityBadge(); });
  btnCopySalt.addEventListener("click", async () => {
    try { await navigator.clipboard.writeText(inpBuildSalt.value || ""); setKeyStatus("Salt copied."); }
    catch (e) { setKeyStatus("Copy failed: " + e.message); }
  });
  inpBuildSalt.addEventListener("input", refreshParityBadge);

  // Issue license
  btnIssue.addEventListener("click", async () => {
    try {
      if (!rsaPrivateKey) { setStatus("Import or generate a PRIVATE KEY first.", true); return; }
      const name = (inpCharacter.value || "").trim();
      if (!name) { setStatus("Enter character name.", true); return; }
      const hrs = Math.max(0, parseInt(inpHours.value || "0", 10));
      const mins = Math.max(0, parseInt(inpMinutes.value || "0", 10));
      let seconds = hrs * 3600 + mins * 60;
      if (seconds <= 0) { setStatus("Set a duration greater than 0.", true); return; }
      if (seconds > DEFAULTS.MAX_DURATION_SEC) { seconds = DEFAULTS.MAX_DURATION_SEC; setStatus("Duration capped at 12 hours.", true); }

      const tickWindow = DEFAULTS.TICK_WINDOW_SEC; // fixed by parity v1
      const buildSalt = (inpBuildSalt.value || DEFAULTS.BUILD_SALT);

      const { token, expIso } = await buildAndSignToken({
        character: name,
        seconds,
        note: inpNote.value || "",
        tickWindow,
        buildSalt
      });

      outChar.textContent = name;
      outExp.textContent = expIso;
      taToken.value = token;
      outWrap.classList.remove("hidden");
      setStatus("Signed ✓ — paste with /license <token>");
    } catch (e) { console.error(e); setStatus("Failed: " + e.message, true); }
  });

  btnClear.addEventListener("click", () => {
    inpCharacter.value = "";
    inpHours.value = "1";
    inpMinutes.value = "0";
    inpNote.value = "";
    taToken.value = "";
    outWrap.classList.add("hidden");
    setStatus("Ready");
  });

  btnCopyTok.addEventListener("click", () => {
    taToken.select(); document.execCommand("copy");
    setStatus("Token copied to clipboard");
  });

  // ---------- Init ----------
  (async function init() {
    // Inject parity badge + defaults
    injectParityBadge();
    inpBuildSalt.value = DEFAULTS.BUILD_SALT;
    refreshParityBadge();

    // Load saved private key XML if present
    const saved = localStorage.getItem("asc_rsa_priv_xml");
    if (saved) {
      taPrivXml.value = saved;
      try {
        await importPrivateXml(saved);
        taPubXmlOut.value = currentPublicXml();
        setKeyStatus("Private key (RSA XML) loaded from browser storage.");
        refreshParityBadge();
      } catch (e) {
        console.warn("Auto-import failed:", e);
      }
    }
  })();
})();
