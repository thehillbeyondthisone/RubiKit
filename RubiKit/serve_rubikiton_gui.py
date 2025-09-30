import os, sys, socket, threading, subprocess, platform, webbrowser, json
import urllib.parse
try:
    import tkinter as tk
    from tkinter import ttk, messagebox, filedialog
except Exception as e:
    print("ERROR: Tkinter failed to import. Install Python with Tcl/Tk.", e)
    input("Press Enter to exit...")
    sys.exit(1)

from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from functools import partial

APP_TITLE = "RubiKitOS LAN Host"
DEFAULT_PORT = 8780
MODULES_URL_PREFIX = "/modules"   # what clients request
MANIFEST_NAME = "modules.json"

# ---------------- OS helpers ----------------
def is_windows():
    return platform.system().lower().startswith("win")

def is_admin():
    if not is_windows():
        return False
    try:
        import ctypes
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False

def relaunch_elevated():
    if not is_windows():
        return False
    try:
        import ctypes
        params = " ".join(['"%s"' % a for a in sys.argv])
        ret = ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable, params, None, 1)
        return ret > 32
    except Exception:
        return False

def get_lan_ip_fallback():
    ip = "127.0.0.1"
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            s.connect(("8.8.8.8", 80))
            ip = s.getsockname()[0]
    except Exception:
        try:
            ip = socket.gethostbyname(socket.gethostname())
        except Exception:
            pass
    return ip

def add_firewall_rule(port: int, rule_name: str):
    if not is_windows():
        return True, "Non-Windows OS: skipping firewall rule."
    try:
        cmd = ["netsh","advfirewall","firewall","add","rule",
               f"name={rule_name}","dir=in","action=allow","protocol=TCP",
               f"localport={port}","profile=private","enable=yes"]
        p = subprocess.run(cmd, capture_output=True, text=True)
        ok = p.returncode == 0 or "Ok." in (p.stdout or "")
        msg = (p.stdout or p.stderr).strip() or "Firewall rule added."
        return ok, msg
    except Exception as e:
        return False, str(e)

def remove_firewall_rule(rule_name: str):
    if not is_windows():
        return True, "Non-Windows OS: skipping firewall cleanup."
    try:
        p = subprocess.run(["netsh","advfirewall","firewall","delete","rule",f"name={rule_name}"],
                           capture_output=True, text=True)
        ok = p.returncode == 0
        msg = (p.stdout or p.stderr).strip() or "Firewall rule deleted."
        return ok, msg
    except Exception as e:
        return False, str(e)

# -------------- Custom handler with mounts --------------
class MountingHTTPRequestHandler(SimpleHTTPRequestHandler):
    """Serve base_dir normally, but map URL prefixes to external folders."""
    mounts = {}  # e.g. { "/modules": "D:/AO/modules" }

    def translate_path(self, path):
        # Remove query/fragment
        urlpath = urllib.parse.urlparse(path).path
        # If path starts with a mounted prefix, serve from that real dir
        for url_prefix, real_dir in self.mounts.items():
            if urlpath == url_prefix or urlpath.startswith(url_prefix + "/"):
                rest = urlpath[len(url_prefix):].lstrip("/")
                # Normalize and join safely
                full = os.path.normpath(os.path.join(real_dir, rest))
                # Prevent path escape
                if os.path.commonpath([full, os.path.abspath(real_dir)]) != os.path.abspath(real_dir):
                    return super().translate_path("/404")  # deny traversal
                return full
        # Otherwise, default behavior (base_dir provided via partial)
        return super().translate_path(path)

# -------------- HTTP server thread --------------
class HttpServerThread(threading.Thread):
    def __init__(self, base_dir: str, bind_ip: str, port: int, mounts: dict, status_cb):
        super().__init__(daemon=True)
        self.base_dir = base_dir
        self.bind_ip = bind_ip
        self.port = port
        self.mounts = mounts
        self.status_cb = status_cb
        self.httpd = None

    def run(self):
        try:
            handler_cls = partial(MountingHTTPRequestHandler, directory=self.base_dir)
            # inject mounts
            handler_cls.mounts = dict(self.mounts)
            self.httpd = ThreadingHTTPServer((self.bind_ip, self.port), handler_cls)
            self.status_cb(f"Serving {self.base_dir} on {self.bind_ip}:{self.port}")
            if self.mounts:
                for p, d in self.mounts.items():
                    self.status_cb(f"  Mounted {p} -> {d}")
            self.httpd.serve_forever()
        except OSError as e:
            self.status_cb(f"ERROR: {e}")
        except Exception as e:
            self.status_cb(f"ERROR: {repr(e)}")

    def stop(self):
        try:
            if self.httpd:
                self.httpd.shutdown()
                self.httpd.server_close()
        finally:
            self.httpd = None

# -------------------- Tk App --------------------
class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("840x640")
        self.minsize(820, 600)

        # Serve from script directory
        self.base_dir = os.path.abspath(os.path.dirname(__file__))
        self.index_path = os.path.join(self.base_dir, "index.html")
        if not os.path.isfile(self.index_path):
            messagebox.showerror("index.html not found",
                                 f"Place this script next to your RubiKitOS index.html.\n\nExpected:\n{self.index_path}")
            self.destroy()
            sys.exit(1)

        # State
        self.server_thread = None
        self.firewall_rule_name = None

        # Vars
        self.port_var = tk.IntVar(value=DEFAULT_PORT)
        self.lan_ip = get_lan_ip_fallback()
        self.bind_choice = tk.StringVar(value="0.0.0.0")
        # Modules mount: default to ./modules, or pick an external folder
        default_modules = os.path.join(self.base_dir, "modules")
        self.use_external_modules = tk.BooleanVar(value=False)
        self.modules_dir_var = tk.StringVar(value=default_modules if os.path.isdir(default_modules) else "")

        self._build_ui()
        self._write_instructions()
        self.protocol("WM_DELETE_WINDOW", self.on_close)

    # ---------- UI ----------
    def _build_ui(self):
        pad = {"padx": 10, "pady": 8}
        root = ttk.Frame(self); root.pack(fill="both", expand=True, **pad)

        # Row 0: path + admin + elevate
        row0 = ttk.Frame(root); row0.pack(fill="x", **pad)
        ttk.Label(row0, text="Web Root:").pack(side="left")
        e = ttk.Entry(row0)
        e.insert(0, self.base_dir)
        e.config(state="readonly")
        e.pack(side="left", fill="x", expand=True, padx=8)
        self.admin_var = tk.StringVar(value="Admin: Yes" if is_admin() else "Admin: No")
        ttk.Label(row0, textvariable=self.admin_var).pack(side="left", padx=(6,8))
        ttk.Button(row0, text="Run as Admin", command=self.run_as_admin).pack(side="left")

        # Row 1: port/bind
        row1 = ttk.Frame(root); row1.pack(fill="x", **pad)
        ttk.Label(row1, text="Port:").pack(side="left")
        ttk.Entry(row1, textvariable=self.port_var, width=8).pack(side="left", padx=(6,16))
        ttk.Label(row1, text="Bind:").pack(side="left")
        ttk.Combobox(row1, textvariable=self.bind_choice, width=22, state="readonly",
                     values=["127.0.0.1", "0.0.0.0", self.lan_ip]).pack(side="left", padx=(6,16))
        ttk.Button(row1, text="Start", command=self.start_server).pack(side="left")
        ttk.Button(row1, text="Stop", command=self.stop_server).pack(side="left", padx=(6,0))

        # Row 2: modules mounting
        mods = ttk.LabelFrame(root, text="Modules Mount (/modules)"); mods.pack(fill="x", **pad)
        ttk.Checkbutton(mods, text="Use external modules folder", variable=self.use_external_modules,
                        command=self._toggle_modules_input).grid(row=0, column=0, sticky="w", padx=8, pady=6)
        self.modules_entry = ttk.Entry(mods, textvariable=self.modules_dir_var, width=80, state="disabled")
        self.modules_entry.grid(row=1, column=0, columnspan=2, sticky="ew", padx=8, pady=(0,8))
        ttk.Button(mods, text="Browse…", command=self.choose_modules_dir).grid(row=1, column=2, sticky="e", padx=8, pady=(0,8))
        mods.columnconfigure(0, weight=1)

        # Row 3: manifest status + quick test
        man = ttk.LabelFrame(root, text="Manifest Status"); man.pack(fill="x", **pad)
        self.manifest_status = tk.StringVar(value="(not checked)")
        ttk.Label(man, textvariable=self.manifest_status, justify="left").grid(row=0, column=0, sticky="w", padx=8, pady=6)
        ttk.Button(man, text="Check /modules/modules.json", command=self.check_manifest).grid(row=0, column=1, sticky="e", padx=8, pady=6)
        man.columnconfigure(0, weight=1)

        # URLs
        urls = ttk.LabelFrame(root, text="Access URLs"); urls.pack(fill="x", **pad)
        urls.columnconfigure(1, weight=1)
        ttk.Label(urls, text="Localhost:").grid(row=0, column=0, sticky="w", padx=8, pady=4)
        self.local_url = tk.StringVar(value="(not running)")
        ttk.Entry(urls, textvariable=self.local_url, state="readonly").grid(row=0, column=1, sticky="ew", padx=8, pady=4)
        ttk.Button(urls, text="Open", command=lambda: self._open_url(self.local_url.get())).grid(row=0, column=2, padx=4)
        ttk.Button(urls, text="Copy", command=lambda: self._copy(self.local_url.get())).grid(row=0, column=3, padx=4)

        ttk.Label(urls, text="LAN:").grid(row=1, column=0, sticky="w", padx=8, pady=4)
        self.lan_url = tk.StringVar(value="(not running)")
        ttk.Entry(urls, textvariable=self.lan_url, state="readonly").grid(row=1, column=1, sticky="ew", padx=8, pady=4)
        ttk.Button(urls, text="Open", command=lambda: self._open_url(self.lan_url.get())).grid(row=1, column=2, padx=4)
        ttk.Button(urls, text="Copy", command=lambda: self._copy(self.lan_url.get())).grid(row=1, column=3, padx=4)

        # Status / instructions
        self.status = ttk.LabelFrame(root, text="Status & Instructions")
        self.status.pack(fill="both", expand=True, **pad)
        self.text = tk.Text(self.status, height=14, wrap="word")
        self.text.pack(fill="both", expand=True, padx=8, pady=8)
        self._write_help()

    def _write_help(self):
        self._log(
            "How it works:\n"
            f" • Web root = this folder (must contain index.html)\n"
            f" • {MODULES_URL_PREFIX} is served from either ./modules (default) OR an external folder you choose.\n"
            f" • Your phone hits: http://<LAN-IP>:{DEFAULT_PORT}/rubi and fetches {MODULES_URL_PREFIX}/{MANIFEST_NAME}\n\n"
            "Steps for LAN:\n"
            "  1) Optional: Click 'Run as Admin' so Windows Firewall allows inbound.\n"
            "  2) If your modules live elsewhere, enable 'Use external modules folder' and pick the real folder.\n"
            f"  3) Click 'Check /modules/{MANIFEST_NAME}' to verify the manifest is visible.\n"
            "  4) Start the server. Open the LAN URL on your phone.\n"
            "  5) RubiKitOS will load the manifest and show your module tiles.\n"
        )

    def _write_instructions(self):
        pass  # kept for compatibility with earlier code paths

    def _log(self, msg: str):
        self.text.insert("end", msg + "\n")
        self.text.see("end")

    def _open_url(self, url: str):
        if url.startswith("http"):
            webbrowser.open(url)
        else:
            messagebox.showinfo("Not running", "Start the server first.")

    def _copy(self, s: str):
        if not s.startswith("http"):
            messagebox.showinfo("Not running", "Start the server first.")
            return
        self.clipboard_clear()
        self.clipboard_append(s)
        self._log(f"Copied to clipboard: {s}")

    def run_as_admin(self):
        if not is_windows():
            messagebox.showinfo("Not Windows", "Elevation is only needed on Windows.")
            return
        if is_admin():
            messagebox.showinfo("Already Admin", "This instance is already running as Administrator.")
            return
        if relaunch_elevated():
            self._log("Restarting with Administrator privileges...")
            self.destroy()
            sys.exit(0)
        else:
            messagebox.showwarning("Elevation failed", "Could not restart elevated. You can still run local-only.")

    def _toggle_modules_input(self):
        state = "normal" if self.use_external_modules.get() else "disabled"
        self.modules_entry.config(state=state)

    def choose_modules_dir(self):
        d = filedialog.askdirectory(mustexist=True, initialdir=self.modules_dir_var.get() or self.base_dir)
        if d:
            self.modules_dir_var.set(d)

    def _resolve_mounts(self):
        """Return dict of URL prefix -> real folder to mount."""
        mounts = {}
        if self.use_external_modules.get():
            real = self.modules_dir_var.get().strip()
            if os.path.isdir(real):
                mounts[MODULES_URL_PREFIX] = real
            else:
                messagebox.showwarning("Modules folder not found",
                                       "External modules folder is enabled, but the path does not exist.")
        else:
            # Default to local ./modules (only if it exists)
            local_mods = os.path.join(self.base_dir, "modules")
            if os.path.isdir(local_mods):
                mounts[MODULES_URL_PREFIX] = local_mods
        return mounts

    def check_manifest(self):
        """Read the *filesystem* manifest that will be served at /modules/modules.json and report what we see."""
        mounts = self._resolve_mounts()
        if MODULES_URL_PREFIX not in mounts:
            self.manifest_status.set("No modules mount. (Create ./modules or enable external folder.)")
            self._log("Manifest check: no /modules mount configured.")
            return
        fs_dir = mounts[MODULES_URL_PREFIX]
        manifest_path = os.path.join(fs_dir, MANIFEST_NAME)
        if not os.path.isfile(manifest_path):
            self.manifest_status.set(f"Missing {MANIFEST_NAME} in {fs_dir}")
            self._log(f"Manifest check: {manifest_path} not found.")
            return
        try:
            with open(manifest_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            items = data.get("items", [])
            ids = [str(x.get("id") or x.get("name") or "?") for x in items]
            self.manifest_status.set(f"OK: {len(items)} module(s): {', '.join(ids[:8])}{'…' if len(ids)>8 else ''}")
            self._log(f"Manifest OK: {len(items)} modules found.")
        except Exception as e:
            self.manifest_status.set(f"Invalid JSON: {e}")
            self._log(f"Manifest parse error: {e}")

    def start_server(self):
        if self.server_thread is not None:
            self._log("Already running.")
            return
        # Port
        try:
            port = int(self.port_var.get())
            if not (1 <= port <= 65535): raise ValueError
        except ValueError:
            messagebox.showerror("Invalid port", "Port must be an integer between 1 and 65535.")
            return

        bind_ip = self.bind_choice.get().strip() or "0.0.0.0"
        if bind_ip == get_lan_ip_fallback() and bind_ip == "127.0.0.1":
            bind_ip = "0.0.0.0"

        # Update admin indicator
        self.admin_var.set("Admin: Yes" if is_admin() else "Admin: No")

        # Firewall (best if elevated)
        self.firewall_rule_name = f"RubiKitOS Python {port}"
        ok, fw_msg = add_firewall_rule(port, self.firewall_rule_name)
        self._log(f"[Firewall] {fw_msg}" if ok else f"[Firewall] Could not add rule: {fw_msg}")
        if is_windows() and not ok and not is_admin():
            self._log("Hint: Click 'Run as Admin' to allow Windows Firewall and enable LAN access.")

        # Mounts (/modules -> external or local)
        mounts = self._resolve_mounts()
        if MODULES_URL_PREFIX in mounts:
            self._log(f"Mounting {MODULES_URL_PREFIX} -> {mounts[MODULES_URL_PREFIX]}")
        else:
            self._log("No modules mount configured; RubiKit will only serve the web root.")

        # Start server thread
        self.server_thread = HttpServerThread(self.base_dir, bind_ip, port, mounts, self._log)
        self.server_thread.start()

        # URLs
        self.local_url.set(f"http://127.0.0.1:{port}/rubi")
        lan_host = bind_ip if bind_ip not in ("0.0.0.0","127.0.0.1") else get_lan_ip_fallback()
        self.lan_url.set(f"http://{lan_host}:{port}/rubi")

        self._log("Server started.")
        self._log(f"Local: {self.local_url.get()}")
        self._log(f"LAN:   {self.lan_url.get()}")
        self._log(f"Tip: Test manifest at {self.lan_url.get().rsplit('/rubi',1)[0]}{MODULES_URL_PREFIX}/{MANIFEST_NAME}")

    def stop_server(self):
        if self.server_thread is not None:
            self._log("Stopping server...")
            try:
                self.server_thread.stop()
            except Exception:
                pass
            self.server_thread = None
            self._log("Server stopped.")

        if self.firewall_rule_name:
            ok, fw_msg = remove_firewall_rule(self.firewall_rule_name)
            self._log(f"[Firewall] {fw_msg}")
            self.firewall_rule_name = None

        self.local_url.set("(not running)")
        self.lan_url.set("(not running)")

    def on_close(self):
        self.stop_server()
        self.destroy()

if __name__ == "__main__":
    try:
        app = App()
        app.mainloop()
    except Exception as e:
        print("Fatal error starting GUI:", e)
        input("Press Enter to exit...")
        raise
