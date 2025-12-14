#!/usr/bin/env python3
"""
auno_minifetch.py — tiny helper to grab minimal info for an AO item page (with icon)

Outputs: {id, name, ql, icon, source, error?}

Run a local CORS-friendly server on port 8797:
  python auno_minifetch.py --serve :8797
"""
import re, sys, json, time, html
import urllib.request, urllib.error, urllib.parse
from http.server import BaseHTTPRequestHandler, HTTPServer

USER_AGENT = "auno-minifetch/1.1 (+local dev tool)"
DEFAULT_TIMEOUT = 12
BASE_HTTP = "https://auno.org"

def _fetch(url: str, timeout=DEFAULT_TIMEOUT) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read().decode("utf-8", errors="ignore")

def _extract_id_and_ql(s: str):
    s = s.strip()
    m = re.search(r"itemref://(\d+)/(\d+)/(\d+)", s, re.I)
    if m:
        return m.group(2), m.group(3)
    try:
        parsed = urllib.parse.urlparse(s)
        if parsed.scheme in ("http","https"):
            qs = urllib.parse.parse_qs(parsed.query)
            idv = qs.get("id", [None])[0]
            ql = qs.get("ql", [None])[0]
            if idv:
                return idv, ql
    except Exception:
        pass
    if re.fullmatch(r"\d+", s):
        return s, None
    return None, None

def _parse_name(html_text: str) -> str:
    h1 = re.findall(r"<h1[^>]*>(.*?)</h1>", html_text, re.I|re.S)
    if h1:
        name = re.sub(r"<[^>]+>", "", h1[0])
        return html.unescape(name).strip()
    title = re.findall(r"<title[^>]*>(.*?)</title>", html_text, re.I|re.S)
    if title:
        t = title[0]
        t = re.sub(r"\s*\|\s*auno\.org.*$", "", t, flags=re.I)
        t = re.sub(r"<[^>]+>", "", t)
        return html.unescape(t).strip()
    return ""

def _absolutize(src: str) -> str:
    if not src:
        return ""
    src = src.strip()
    if src.startswith("//"):
        return "https:" + src
    if src.startswith("http://") or src.startswith("https://"):
        return src
    if not src.startswith("/"):
        src = "/" + src
    return BASE_HTTP + src

def _parse_icon(html_text: str) -> str:
    imgs = re.findall(r"<img[^>]+src=['\"]([^'\"]+)['\"][^>]*>", html_text, re.I)
    if not imgs:
        return ""
    # rank by 'icon' hints, filetype, and avoid spacers
    best = ""
    best_score = -10**9
    for src in imgs:
        low = src.lower()
        sc = 0
        if "icon" in low or "/icons/" in low or "/ico/" in low:
            sc += 3
        if low.endswith((".png",".gif",".webp",".jpg",".jpeg")):
            sc += 1
        if any(bad in low for bad in ("spacer","pixel","blank")):
            sc -= 3
        if sc > best_score:
            best_score = sc
            best = src
    return _absolutize(best)

def fetch_minimal(s: str) -> dict:
    idv, ql = _extract_id_and_ql(s)
    if not idv:
        raise ValueError("Unable to parse ID from input. Provide an Auno URL, itemref, or numeric ID.")
    urls = [
        f"{BASE_HTTP}/ao/db.php?id={idv}",
        f"https://r.jina.ai/http://auno.org/ao/db.php?id={idv}",
        f"https://r.jina.ai/https://auno.org/ao/db.php?id={idv}",
    ]
    last_err = None
    for u in urls:
        try:
            html_text = _fetch(u)
            name = _parse_name(html_text)
            icon = _parse_icon(html_text)
            if name or icon:
                return {"id": idv, "name": name, "ql": ql, "icon": icon, "source": u}
        except Exception as e:
            last_err = str(e); time.sleep(0.25)
    return {"id": idv, "name": "", "ql": ql, "icon": "", "source": urls[-1], "error": last_err or "parse-failed"}

CORS_HEADERS = [
    ("Access-Control-Allow-Origin", "*"),
    ("Access-Control-Allow-Methods", "GET, OPTIONS"),
    ("Access-Control-Allow-Headers", "Content-Type"),
    ("Cache-Control", "no-store"),
]

class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        return
    def _send(self, code, data: dict):
        body = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        for k,v in CORS_HEADERS: self.send_header(k, v)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
    def do_OPTIONS(self):
        self.send_response(204)
        for k,v in CORS_HEADERS: self.send_header(k, v)
        self.end_headers()
    def do_GET(self):
        try:
            parsed = urllib.parse.urlparse(self.path)
            if parsed.path == "/auno":
                qs = urllib.parse.parse_qs(parsed.query)
                q = qs.get("id", [""])[0] or qs.get("url", [""])[0]
                if not q: self._send(400, {"error":"missing id or url"}); return
                res = fetch_minimal(q); self._send(200, res); return
            elif parsed.path == "/healthz":
                self._send(200, {"ok": True}); return
            else:
                self._send(404, {"error":"not found"}); return
        except Exception as e:
            self._send(500, {"error": str(e)})

def serve(addr="127.0.0.1", port=8797):
    httpd = HTTPServer((addr, port), Handler)
    print(f"[auno-minifetch] serving on http://{addr}:{port}")
    try: httpd.serve_forever()
    except KeyboardInterrupt: print("\n[auno-minifetch] shutting down")
    finally: httpd.server_close()

def main(argv):
    if not argv or argv[0] in ("-h","--help"):
        print(__doc__.strip()); return 0
    if argv[0] == "--serve":
        hostport = argv[1] if len(argv) > 1 else ":8797"
        if hostport.startswith(":"): host, port = "127.0.0.1", int(hostport[1:])
        else:
            if ":" in hostport: host, ps = hostport.split(":",1); port = int(ps)
            else: host, port = hostport, 8797
        serve(host, port); return 0
    q = argv[0]; res = fetch_minimal(q); print(json.dumps(res, ensure_ascii=False, indent=2)); return 0

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
