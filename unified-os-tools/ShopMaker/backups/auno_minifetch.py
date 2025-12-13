#!/usr/bin/env python3
"""
auno_minifetch.py — tiny helper to grab minimal info for an AO item page

Features
- CLI: pass an Auno URL or numeric id; prints JSON with {id, name, ql, source}
- Server mode: `--serve :8797` to run a CORS-friendly local endpoint
  GET /auno?id=292161 or /auno?url=https://auno.org/ao/db.php?id=292161
- Zero external dependencies: uses urllib + simple regex parsing
- Robust-ish parsing: prefers <h1>, falls back to <title> cleanup
- Timeouts + polite retry with mirror if direct fetch fails

Examples
  python auno_minifetch.py 292161
  python auno_minifetch.py https://auno.org/ao/db.php?id=292161
  python auno_minifetch.py --serve :8797
  # then in JS: fetch("http://127.0.0.1:8797/auno?id=292161")
"""

import re, sys, json, time, html
import urllib.request, urllib.error, urllib.parse
from http.server import BaseHTTPRequestHandler, HTTPServer

USER_AGENT = "auno-minifetch/1.0 (+local dev tool)"
DEFAULT_TIMEOUT = 12

def _fetch(url: str, timeout=DEFAULT_TIMEOUT) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read().decode("utf-8", errors="ignore")

def _extract_id_and_ql(s: str):
    # Accept full URL, itemref, or bare numeric
    # Returns (id, ql or None)
    s = s.strip()
    # itemref://low/high/ql
    m = re.search(r"itemref://(\d+)/(\d+)/(\d+)", s, re.I)
    if m:
        return m.group(2), m.group(3)
    # URL
    try:
        parsed = urllib.parse.urlparse(s)
        if parsed.scheme in ("http","https"):
            qs = urllib.parse.parse_qs(parsed.query)
            idv = None
            if "id" in qs and qs["id"]:
                idv = qs["id"][0]
            ql = None
            if "ql" in qs and qs["ql"]:
                ql = qs["ql"][0]
            if idv:
                return idv, ql
    except Exception:
        pass
    # Bare numeric
    if re.fullmatch(r"\d+", s):
        return s, None
    return None, None

def _parse_name(html_text: str) -> str:
    # Prefer <h1>...</h1>
    h1 = re.search(r"<h1[^>]*>(.*?)</h1>", html_text, re.I|re.S)
    if h1:
        name = h1.group(1)
        # Strip tags
        name = re.sub(r"<[^>]+>", "", name)
        return html.unescape(name).strip()
    # Fallback to <title>
    title = re.search(r"<title[^>]*>(.*?)</title>", html_text, re.I|re.S)
    if title:
        t = title.group(1)
        t = re.sub(r"\s*\|\s*auno\.org.*$", "", t, flags=re.I)
        t = re.sub(r"<[^>]+>", "", t)
        return html.unescape(t).strip()
    return ""

def fetch_minimal(s: str) -> dict:
    idv, ql = _extract_id_and_ql(s)
    if not idv:
        raise ValueError("Unable to parse ID from input. Provide an Auno URL, itemref, or numeric ID.")

    # Try direct, then mirror
    urls = [
        f"https://auno.org/ao/db.php?id={idv}",
        f"https://r.jina.ai/http://auno.org/ao/db.php?id={idv}",
        f"https://r.jina.ai/https://auno.org/ao/db.php?id={idv}",
    ]
    last_err = None
    for u in urls:
        try:
            html_text = _fetch(u)
            name = _parse_name(html_text)
            if name:
                return {"id": idv, "name": name, "ql": ql, "source": u}
        except Exception as e:
            last_err = str(e)
            time.sleep(0.3)
    # If parsing failed entirely, still return the id/ql so caller can proceed
    return {"id": idv, "name": "", "ql": ql, "source": urls[-1], "error": last_err or "parse-failed"}

# ---------------- HTTP server ----------------

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
        for k,v in CORS_HEADERS:
            self.send_header(k, v)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_response(204)
        for k,v in CORS_HEADERS:
            self.send_header(k, v)
        self.end_headers()

    def do_GET(self):
        try:
            parsed = urllib.parse.urlparse(self.path)
            if parsed.path == "/auno":
                qs = urllib.parse.parse_qs(parsed.query)
                q = ""
                if "id" in qs and qs["id"]:
                    q = qs["id"][0]
                elif "url" in qs and qs["url"]:
                    q = qs["url"][0]
                else:
                    self._send(400, {"error":"missing id or url"}); return
                res = fetch_minimal(q)
                self._send(200, res); return
            elif parsed.path == "/healthz":
                self._send(200, {"ok": True}); return
            else:
                self._send(404, {"error":"not found"}); return
        except Exception as e:
            self._send(500, {"error": str(e)})

def serve(addr="127.0.0.1", port=8797):
    httpd = HTTPServer((addr, port), Handler)
    print(f"[auno-minifetch] serving on http://{addr}:{port}")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[auno-minifetch] shutting down")
    finally:
        httpd.server_close()

def main(argv):
    if not argv or argv[0] in ("-h","--help"):
        print(__doc__.strip())
        return 0
    if argv[0] == "--serve":
        hostport = argv[1] if len(argv) > 1 else ":8797"
        if hostport.startswith(":"):
            host = "127.0.0.1"
            port = int(hostport[1:])
        else:
            if ":" in hostport:
                host, ps = hostport.split(":",1)
                port = int(ps)
            else:
                host, port = hostport, 8797
        serve(host, port); return 0
    q = argv[0]
    res = fetch_minimal(q)
    print(json.dumps(res, ensure_ascii=False, indent=2))
    return 0

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
