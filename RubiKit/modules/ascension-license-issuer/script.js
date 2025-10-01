// Ascension License Issuer v2.0 - Simple USER|DATE|FEATURES format

(function () {
  const $ = (id) => document.getElementById(id);

  const keystatus = $("keystatus");
  const remember = $("remember");

  const taPrivXml = $("privXml");
  const taPubXmlOut = $("pubXmlOut");
  const btnImportPrivXml = $("importPrivXml");
  const btnExportPrivXml = $("exportPrivXml");
  const btnCopyPubXml = $("copyPubXml");
  const btnGenRsa = $("genRsa");
  const btnExportPubXml = $("exportPubXml");

  const inpCharacter = $("character");
  const inpExpDate = $("expDate");
  const inpFeatures = $("features");

  const btnIssue = $("issue");
  const btnClear = $("clear");
  const outWrap = $("result");
  const outChar = $("oChar");
  const outExp = $("oExp");
  const taToken = $("token");
  const btnCopyTok = $("copy");

  let rsaPrivateKey = null;
  let jwkPriv = null;

  const enc = new TextEncoder();

  const setKeyStatus = (s, isError = false) => {
    keystatus.textContent = s;
    keystatus.style.color = isError ? "#ff6b6b" : "";
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

  function buildPublicXml(modulusB64, exponentB64) {
    return `<RSAKeyValue><Modulus>${modulusB64}</Modulus><Exponent>${exponentB64}</Exponent></RSAKeyValue>`;
  }

  function buildPrivateXml(parts) {
    const n = b64UrlToB64(parts.n);
    const e = b64UrlToB64(parts.e);
    const d = b64UrlToB64(parts.d);
    const p = b64UrlToB64(parts.p);
    const q = b64UrlToB64(parts.q);
    const dp = b64UrlToB64(parts.dp);
    const dq = b64UrlToB64(parts.dq);
    const qi = b64UrlToB64(parts.qi);
    return `<RSAKeyValue><Modulus>${n}</Modulus><Exponent>${e}</Exponent><P>${p}</P><Q>${q}</Q><DP>${dp}</DP><DQ>${dq}</DQ><InverseQ>${qi}</InverseQ><D>${d}</D></RSAKeyValue>`;
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
      throw new Error("Invalid RSA PRIVATE KEY XML: missing required elements.");
    }
    return { Modulus, Exponent, D, P, Q, DP, DQ, InverseQ };
  }

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

  function currentPublicXml() {
    if (!jwkPriv) throw new Error("No private key loaded.");
    return buildPublicXml(b64UrlToB64(jwkPriv.n), b64UrlToB64(jwkPriv.e));
  }

  async function signBytesRS256(bytes) {
    if (!rsaPrivateKey) throw new Error("No private key loaded.");
    const sig = await crypto.subtle.sign({ name: "RSASSA-PKCS1-v1_5" }, rsaPrivateKey, bytes);
    return new Uint8Array(sig);
  }

  async function generateRsaXml2048() {
    const algo = { name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([0x01,0x00,0x01]), hash: "SHA-256" };
    const kp = await crypto.subtle.generateKey(algo, true, ["sign","verify"]);
    rsaPrivateKey = kp.privateKey;
    const jwk = await crypto.subtle.exportKey("jwk", rsaPrivateKey);
    jwkPriv = jwk;

    const privXml = buildPrivateXml(jwk);
    const pubXml = buildPublicXml(b64UrlToB64(jwk.n), b64UrlToB64(jwk.e));

    taPrivXml.value = privXml;
    taPubXmlOut.value = pubXml;

    if (remember.checked) localStorage.setItem("asc_rsa_priv_xml", privXml);

    setKeyStatus("✅ New RSA-2048 keypair generated. Export your private key to save it!");
  }

  async function buildAndSignLicense(character, expDate, features) {
    if (!character) throw new Error("Character name required");
    if (!expDate) throw new Error("Expiration date required");

    const payload = `${character}|${expDate}|${features || ""}`;
    const payloadBytes = enc.encode(payload);
    const signature = await signBytesRS256(payloadBytes);

    const payloadB64 = uint8ToB64(payloadBytes);
    const sigB64 = uint8ToB64(signature);

    return `${payloadB64}.${sigB64}`;
  }

  function toast(msg) {
    const t = document.createElement("div");
    t.className = "toast";
    t.textContent = msg;
    const container = $("rk-toasts") || document.body;
    container.appendChild(t);
    setTimeout(() => t.classList.add("show"), 10);
    setTimeout(() => {
      t.classList.remove("show");
      setTimeout(() => t.remove(), 300);
    }, 3000);
  }

  function setDefaultExpiration() {
    const d = new Date();
    d.setDate(d.getDate() + 30);
    inpExpDate.value = d.toISOString().split('T')[0];
  }

  (function init() {
    setDefaultExpiration();
    const saved = localStorage.getItem("asc_rsa_priv_xml");
    if (saved) {
      taPrivXml.value = saved;
      importPrivateXml(saved).then(() => {
        taPubXmlOut.value = currentPublicXml();
        setKeyStatus("✅ Private key loaded from browser storage.");
      }).catch(() => {
        setKeyStatus("⚠️ Saved key invalid. Generate a new one.", true);
      });
    }
  })();

  btnGenRsa.addEventListener("click", async () => {
    try {
      await generateRsaXml2048();
      toast("✅ Keypair generated!");
    } catch (e) {
      setKeyStatus("❌ Generation failed: " + e.message, true);
    }
  });

  btnImportPrivXml.addEventListener("click", async () => {
    try {
      const xml = taPrivXml.value.trim();
      if (!xml) { setKeyStatus("⚠️ Paste private key XML first.", true); return; }
      await importPrivateXml(xml);
      if (remember.checked) localStorage.setItem("asc_rsa_priv_xml", xml);
      taPubXmlOut.value = currentPublicXml();
      setKeyStatus("✅ Private key imported successfully.");
      toast("✅ Private key imported!");
    } catch (e) {
      setKeyStatus("❌ Import failed: " + e.message, true);
    }
  });

  btnExportPrivXml.addEventListener("click", () => {
    try {
      const xml = taPrivXml.value.trim();
      if (!xml) throw new Error("No private key to export");
      const blob = new Blob([xml], { type: "text/xml" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "ascension_private_key.xml";
      a.click();
      URL.revokeObjectURL(url);
      toast("✅ Private key exported!");
    } catch (e) {
      toast("❌ Export failed: " + e.message);
    }
  });

  btnExportPubXml.addEventListener("click", () => {
    try {
      const xml = taPubXmlOut.value.trim();
      if (!xml) throw new Error("Generate a keypair first");
      const blob = new Blob([xml], { type: "text/xml" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "ascension_public_key.xml";
      a.click();
      URL.revokeObjectURL(url);
      toast("✅ Public key exported!");
    } catch (e) {
      toast("❌ Export failed: " + e.message);
    }
  });

  btnCopyPubXml.addEventListener("click", async () => {
    try {
      const xml = taPubXmlOut.value.trim();
      if (!xml) throw new Error("Generate a keypair first");
      await navigator.clipboard.writeText(xml);
      toast("✅ Public key copied to clipboard!");
    } catch (e) {
      toast("❌ Copy failed: " + e.message);
    }
  });

  btnIssue.addEventListener("click", async () => {
    try {
      if (!rsaPrivateKey) {
        toast("❌ Generate or import a private key first!");
        return;
      }

      const character = inpCharacter.value.trim();
      const expDate = inpExpDate.value;
      const features = inpFeatures.value.trim();

      if (!character) {
        toast("❌ Enter a character name!");
        return;
      }

      if (!expDate) {
        toast("❌ Select an expiration date!");
        return;
      }

      const license = await buildAndSignLicense(character, expDate, features);

      outChar.textContent = character;
      outExp.textContent = expDate + " UTC";
      taToken.value = license;
      outWrap.classList.remove("hidden");

      toast("✅ License generated!");
    } catch (e) {
      toast("❌ Error: " + e.message);
    }
  });

  btnClear.addEventListener("click", () => {
    inpCharacter.value = "";
    inpFeatures.value = "";
    setDefaultExpiration();
    outWrap.classList.add("hidden");
    taToken.value = "";
  });

  btnCopyTok.addEventListener("click", async () => {
    try {
      const license = taToken.value.trim();
      if (!license) throw new Error("No license to copy");
      await navigator.clipboard.writeText(license);
      toast("✅ License copied to clipboard!");
    } catch (e) {
      toast("❌ Copy failed: " + e.message);
    }
  });
})();
