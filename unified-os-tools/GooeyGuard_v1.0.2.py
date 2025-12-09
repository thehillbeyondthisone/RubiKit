#!/usr/bin/env python3
#
# GooeyGuard - Anarchy Online GUI & Prefs Manager
# Version: 1.0.2 (Final Release)
# Date: 2025-10-10
#
# A safe, responsive, and user-friendly tool for backing up, restoring,
# and migrating Anarchy Online character settings and GUI layouts.
#
import os
import sys
import json
import shutil
import zipfile
import webbrowser
import traceback
import threading
import queue
from datetime import datetime, timedelta
from pathlib import Path
from fnmatch import fnmatch
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

# --- Constants ---
APP_NAME = "GooeyGuard"
VERSION = "1.0.2"

DEFAULT_INCLUDE_GLOBS = ["*.xml", "*.cfg", "*.ini", "*.txt", "*.dat"]
DEFAULT_EXCLUDE_DIRS = {"Logs", "Cache", "Screenshots", "CrashReports", "TestLive", "Browser", "cd_image"}
DEFAULT_EXCLUDE_GLOBS = {"*.log", "*.bak", "*.tmp", "*.old", "*.zip"}
DONATION_URL = "https://www.buymeacoffee.com/example" # Placeholder URL

# --- Helper Functions ---
def get_initial_roots():
    roots = [r"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online", r"C:\Program Files (x86)\Funcom\Anarchy Online", r"C:\Funcom\Anarchy Online"]
    try:
        program_files = os.getenv("ProgramFiles(x86)", os.getenv("ProgramFiles", r"C:\Program Files"))
        roots.append(os.path.join(program_files, "Funcom", "Anarchy Online"))
        local_app_data = os.getenv("LOCALAPPDATA")
        if local_app_data: roots.append(os.path.join(local_app_data, "Funcom", "Anarchy Online"))
    except Exception: pass
    return [Path(r) for r in set(roots) if Path(r).exists()]

def logtime(): return datetime.now().strftime("%H:%M:%S")
def tsfile(): return datetime.now().strftime("%Y%m%d-%H%M%S")
def pretty(p: Path) -> str: return str(p)
def format_size(size_bytes):
    if size_bytes == 0: return "0 B"
    power, n = 1024, 0
    power_labels = {0: 'B', 1: 'KB', 2: 'MB', 3: 'GB'}
    while size_bytes >= power and n < len(power_labels)-1: size_bytes /= power; n += 1
    return f"{size_bytes:.2f} {power_labels[n]}"
def collect_files(root: Path, include_globs, exclude_globs):
    files = []
    try:
        for p in root.rglob("*"):
            if p.is_file():
                rel_parts = p.relative_to(root).parts
                if any(part in DEFAULT_EXCLUDE_DIRS for part in rel_parts): continue
                if any(fnmatch(p.name, g) for g in exclude_globs): continue
                if any(fnmatch(p.name, g) for g in include_globs): files.append(p)
    except Exception: pass
    return files
def latest_change_in_dir(root: Path, include_globs, exclude_globs):
    files_to_check = collect_files(root, include_globs, exclude_globs)
    if not files_to_check: return None
    try:
        latest_mtime = max(p.stat().st_mtime for p in files_to_check)
        return datetime.fromtimestamp(latest_mtime) if latest_mtime > 0 else None
    except FileNotFoundError: return None

# --- AO Logic ---
def iter_accounts(prefs_dir: Path):
    try:
        for child in sorted(prefs_dir.iterdir()):
            if child.is_dir() and child.name not in DEFAULT_EXCLUDE_DIRS: yield child
    except Exception: return
def iter_chars(account_dir: Path):
    try:
        for child in sorted(account_dir.iterdir()):
            if child.is_dir() and child.name not in DEFAULT_EXCLUDE_DIRS: yield child
    except Exception: return
def get_prefs_summary(pr: Path, inc, exc):
    accs = list(iter_accounts(pr))
    char_count = sum(len(list(iter_chars(a))) for a in accs)
    latest = latest_change_in_dir(pr, inc, exc)
    return len(accs), char_count, latest
def discover_prefs_under(root: Path, max_depth=8):
    found, q, visited = [], [(root, 0)], set()
    if not root.exists() or not root.is_dir(): return found
    while q:
        base, d = q.pop(0)
        if d > max_depth or base in visited: continue
        visited.add(base)
        try:
            for child in base.iterdir():
                if child.is_dir():
                    low = child.name.lower()
                    if low == "prefs": found.append(child)
                    elif low not in DEFAULT_EXCLUDE_DIRS: q.append((child, d + 1))
        except (OSError, PermissionError): pass
    unique_resolved_paths = set()
    for p in found:
        try: unique_resolved_paths.add(p.resolve())
        except (OSError, FileNotFoundError): pass
    return list(unique_resolved_paths)
def install_id_of(p: Path) -> str:
    try:
        parent = p.parent
        while parent.name.lower() != "anarchy online" and parent.parent != parent: parent = parent.parent
        return parent.parent.name
    except Exception: return p.parent.name

# --- Main App ---
class GooeyGuardApp(ttk.Frame):
    def __init__(self, master):
        super().__init__(master, padding=10)
        self.master, self.master.title(f"{APP_NAME} v{VERSION} (Final Release)"), self.master.minsize(1200, 800)
        self.master.protocol("WM_DELETE_WINDOW", self.on_closing)
        
        self.src_prefs_path = tk.StringVar(value="None (Right-click a 'Prefs' row to select)")
        self.root_hint = tk.StringVar(value=""); self.filter_hide_legacy = tk.BooleanVar(value=True)
        self.filter_days = tk.IntVar(value=365); self.filter_hide_empty = tk.BooleanVar(value=True)
        
        self.task_queue, self.thread, self.details_window = queue.Queue(), None, None
        self._create_widgets(); self.after(100, self._process_queue); self.scan_all()
        self.logf(f"GooeyGuard v{VERSION} initialized successfully.")

    def on_closing(self): self.master.destroy()

    def _create_widgets(self):
        self.master.columnconfigure(0, weight=1); self.master.rowconfigure(0, weight=1)
        self.grid(row=0, column=0, sticky="nsew")
        main_pane = ttk.PanedWindow(self, orient='horizontal'); main_pane.grid(row=0, column=0, sticky="nsew", pady=(0, 5))
        self.rowconfigure(0, weight=1); self.columnconfigure(0, weight=1)
        
        left_frame = ttk.Frame(main_pane, padding=5); main_pane.add(left_frame, weight=3)
        left_frame.rowconfigure(2, weight=1); left_frame.columnconfigure(0, weight=1)
        
        topbar = ttk.Frame(left_frame); topbar.grid(row=0, column=0, columnspan=2, sticky="ew", pady=(0, 10)); topbar.columnconfigure(0, weight=1)
        ttk.Entry(topbar, textvariable=self.root_hint).grid(row=0, column=0, sticky="ew", padx=(0, 5))
        ttk.Button(topbar, text="Add Folder...", command=self.add_root).grid(row=0, column=1)
        ttk.Button(topbar, text="Rescan All", command=self.scan_all).grid(row=0, column=2, padx=(5, 0))
        ttk.Button(topbar, text="About", command=self.show_about_popup).grid(row=0, column=3, padx=(10, 0))

        filt = ttk.LabelFrame(left_frame, text="Display Filters", padding=5); filt.grid(row=1, column=0, columnspan=2, sticky="ew", pady=(0, 5))
        ttk.Checkbutton(filt, text="Hide Prefs older than", variable=self.filter_hide_legacy, command=self.scan_all).grid(row=0, column=0, sticky="w")
        tk.Spinbox(filt, from_=7, to=3650, width=5, textvariable=self.filter_days, command=self.scan_all).grid(row=0, column=1, sticky="w", padx=3)
        ttk.Label(filt, text="days").grid(row=0, column=2, sticky="w", padx=(0, 15)); ttk.Checkbutton(filt, text="Hide empty Prefs", variable=self.filter_hide_empty, command=self.scan_all).grid(row=0, column=3, sticky="w")
        
        cols = ("location", "accounts", "chars", "last_mod"); self.tree = ttk.Treeview(left_frame, columns=cols, show="tree headings", selectmode="browse"); self.tree.grid(row=2, column=0, columnspan=2, sticky="nsew")
        vsb = ttk.Scrollbar(left_frame, orient="vertical", command=self.tree.yview); vsb.grid(row=2, column=1, sticky='ns'); self.tree.configure(yscrollcommand=vsb.set)
        
        col_map = {"#0": ("Install Group", 180, False), "location": ("Prefs Location", 350, True), "accounts": ("Accts", 70, False), "chars": ("Chars", 70, False), "last_mod": ("Last Modified", 140, False)}
        for c, (text, width, stretch) in col_map.items():
            self.tree.heading(c, text=text, anchor='w')
            self.tree.column(c, width=width, stretch=stretch, anchor='center' if c not in ["#0", "location"] else 'w')
        self.tree.bind("<Button-3>", self.on_right_click)

        self.menu = tk.Menu(self.tree, tearoff=0); self.menu.add_command(label="Set as SOURCE for Backup", command=self.mark_prefs); self.menu.add_separator(); self.menu.add_command(label="View Details...", command=self.show_details_window); self.menu.add_command(label="Open Folder in Explorer", command=self.open_in_explorer)
        
        right_pane = ttk.PanedWindow(main_pane, orient='vertical'); main_pane.add(right_pane, weight=2)
        action_frame = ttk.Frame(right_pane, padding=5); right_pane.add(action_frame, weight=1); action_frame.columnconfigure(0, weight=1)
        
        howto_frame = ttk.LabelFrame(action_frame, text="How to Use", padding=10); howto_frame.grid(row=0, column=0, sticky="ew", pady=(0, 10)); ttk.Label(howto_frame, text="1. Right-click a 'Prefs' on the left to mark it as SOURCE.\n2. Use the actions below to create or restore a backup.", justify='left').pack(anchor='w')
        src_frame = ttk.LabelFrame(action_frame, text="✅ Source Selection", padding=10); src_frame.grid(row=1, column=0, sticky="ew", pady=(0, 10)); ttk.Label(src_frame, text="Current Source:", font="-weight bold").pack(anchor='w'); ttk.Label(src_frame, textvariable=self.src_prefs_path, wraplength=400).pack(anchor='w', pady=(2,0))
        
        action_box = ttk.LabelFrame(action_frame, text="🚀 Actions", padding=10); action_box.grid(row=2, column=0, sticky="ew", pady=(0, 10))
        self.create_kit_button = ttk.Button(action_box, text="Create Backup Kit (.zip) from Source", command=self.create_migration_kit, state="disabled"); self.create_kit_button.pack(fill='x', ipady=5, pady=2)
        self.restore_kit_button = ttk.Button(action_box, text="Restore Backup Kit to a Folder...", command=self.direct_restore_kit); self.restore_kit_button.pack(fill='x', ipady=5, pady=2)

        adv_frame = ttk.LabelFrame(action_frame, text="Advanced Filters", padding=10); adv_frame.grid(row=3, column=0, sticky="ew", pady=(10, 0)); adv_frame.columnconfigure(1, weight=1)
        ttk.Label(adv_frame, text="Include:").grid(row=0, column=0, sticky="w", padx=(0,5)); self.inc_entry = ttk.Entry(adv_frame); self.inc_entry.grid(row=0, column=1, sticky="ew"); self.inc_entry.insert(0, " ".join(DEFAULT_INCLUDE_GLOBS))
        ttk.Label(adv_frame, text="Exclude:").grid(row=1, column=0, sticky="w", padx=(0,5), pady=(5,0)); self.exc_entry = ttk.Entry(adv_frame); self.exc_entry.grid(row=1, column=1, sticky="ew", pady=(5,0)); self.exc_entry.insert(0, " ".join(DEFAULT_EXCLUDE_GLOBS))

        log_frame = ttk.LabelFrame(right_pane, text="Log", padding=5); right_pane.add(log_frame, weight=1); log_frame.rowconfigure(0, weight=1); log_frame.columnconfigure(0, weight=1)
        self.log = tk.Text(log_frame, height=10, wrap="word", font=("Consolas", 9)); self.log.grid(row=0, column=0, sticky="nsew"); log_vsb = ttk.Scrollbar(log_frame, orient="vertical", command=self.log.yview); log_vsb.grid(row=0, column=1, sticky='ns'); self.log.configure(yscrollcommand=log_vsb.set)
        
        self.status_frame = ttk.Frame(self, padding=(5, 2)); self.status_frame.grid(row=1, column=0, sticky="ew"); self.status_label = ttk.Label(self.status_frame, text="Ready."); self.status_label.pack(side="left"); self.progress_bar = ttk.Progressbar(self.status_frame, orient='horizontal', mode='indeterminate'); self.progress_bar.pack(side="right", fill='x', expand=True, padx=(10,0))
    
    # --- Task & UI Helpers ---
    def _center_window(self, window):
        window.update_idletasks()
        parent = self.master
        x = parent.winfo_x() + (parent.winfo_width() // 2) - (window.winfo_width() // 2)
        y = parent.winfo_y() + (parent.winfo_height() // 2) - (window.winfo_height() // 2)
        window.geometry(f"+{x}+{y}")

    def run_task(self, task_func, *args):
        if self.thread and self.thread.is_alive(): messagebox.showwarning(APP_NAME, "Another task is already in progress."); return
        self.progress_bar.start(); self.set_status('Task started...'); self.thread = threading.Thread(target=task_func, args=args, daemon=True); self.thread.start()
    def _process_queue(self):
        try:
            while not self.task_queue.empty():
                msg, data = self.task_queue.get_nowait()
                handlers = {'status': self.set_status, 'log': self.logf, 'error': self.errorf, 'scan_results': self._populate_tree, 'details_results': self._populate_details_window,
                            'task_done': lambda d: (self.progress_bar.stop(), self.set_status(d)),
                            'kit_success': lambda d: (self.progress_bar.stop(), self.set_status("Backup Kit created!"), self.logf(f"[SUCCESS] Kit saved: {d}"),
                                                      webbrowser.open(Path(d).parent) if messagebox.askyesno(APP_NAME, f"Backup Kit created!\n\nSaved to:\n{d}\n\nOpen the backups folder?") else None)}
                if msg in handlers: handlers[msg](data)
        finally: self.after(100, self._process_queue)
    def logf(self, msg): self.log.insert("end", f"[{logtime()}] {msg}\n"); self.log.see("end")
    def set_status(self, msg): self.status_label.config(text=msg)
    def errorf(self, msg): messagebox.showerror(APP_NAME, msg); self.logf(f"ERROR: {msg}")
    
    # <<< FIXED: Now sets focus on the right-clicked item
    def on_right_click(self, event):
        iid = self.tree.identify_row(event.y)
        if iid and iid.startswith("prefs::"):
            self.tree.selection_set(iid)
            self.tree.focus(iid) # This ensures get_selected_prefs_path() works correctly
            self.menu.tk_popup(event.x_root, event.y_root)

    def get_selected_prefs_path(self): iid = self.tree.focus(); return Path(iid.split("::", 1)[1]) if iid and iid.startswith("prefs::") else None
    def open_in_explorer(self): p = self.get_selected_prefs_path(); webbrowser.open(p) if p and p.exists() else None
    def add_root(self): p = filedialog.askdirectory(title="Select a folder to scan"); self.root_hint.set(p); self.scan_all() if p else None
    def get_current_filters(self): return (self.inc_entry.get().strip().split() or DEFAULT_INCLUDE_GLOBS, self.exc_entry.get().strip().split() or DEFAULT_EXCLUDE_GLOBS)

    # --- Core Actions ---
    def mark_prefs(self):
        p = self.get_selected_prefs_path()
        if not p: self.errorf("Please right-click on a valid 'Prefs' row."); return
        self.src_prefs_path.set(pretty(p)); self.logf(f"Marked SOURCE: {pretty(p)}"); self.create_kit_button.config(state="normal")
    def scan_all(self): self.tree.delete(*self.tree.get_children()); self.run_task(self._task_scan_filesystem)
    def _task_scan_filesystem(self):
        try:
            self.task_queue.put(('log', "Starting filesystem scan..."))
            self.task_queue.put(('status', 'Scanning for Anarchy Online installations...'))
            roots = set(get_initial_roots()); user_hint = self.root_hint.get()
            if user_hint and Path(user_hint).exists(): roots.add(Path(user_hint))
            if not roots: self.task_queue.put(('log', "No common AO install locations found. Use 'Add Folder...'."))
            for r in roots: self.task_queue.put(('log', f"Searching in: {r}"))
            
            pref_paths = {p for r in roots for p in discover_prefs_under(r)}
            self.task_queue.put(('log', f"Found {len(pref_paths)} unique 'Prefs' folders."))
            inc, exc = self.get_current_filters()
            rows = [{"install": install_id_of(pr), "prefs": pr, **dict(zip(["accounts", "chars", "last"], get_prefs_summary(pr, inc, exc)))} for pr in pref_paths]
            self.task_queue.put(('scan_results', rows)); self.task_queue.put(('task_done', f"Scan complete. Displaying results."))
        except Exception as e: self.task_queue.put(('error', f"Scan failed: {e}\n{traceback.format_exc()}")); self.task_queue.put(('task_done', "Scan failed."))
    def _populate_tree(self, rows):
        self.tree.delete(*self.tree.get_children())
        self.logf(f"Populating view with {len(rows)} found items...")
        
        num_before_filter = len(rows)
        if self.filter_hide_empty.get(): rows = [r for r in rows if r["accounts"] > 0]
        if self.filter_hide_legacy.get():
            cutoff = datetime.now() - timedelta(days=self.filter_days.get())
            rows = [r for r in rows if r["last"] and r["last"] >= cutoff]
        self.logf(f"Filtered down to {len(rows)} items based on display settings.")

        by_install = {}
        for r in rows: by_install.setdefault(r["install"], []).append(r)
        for install, items in sorted(by_install.items()):
            newest = max(items, key=lambda r: r["last"] or datetime.min)
            last_str = newest['last'].strftime('%Y-%m-%d %H:%M') if newest['last'] else 'N/A'
            gid = self.tree.insert("", "end", text=f"📂 {install}", values=("", "", "", last_str), open=True)
            for r in sorted(items, key=lambda r: r["last"] or datetime.min, reverse=True):
                last_str = r["last"].strftime("%Y-%m-%d %H:%M") if r["last"] else "N/A"
                self.tree.insert(gid, "end", iid=f"prefs::{r['prefs']}", text="    🗂️ Prefs", values=(r['prefs'].name, r["accounts"], r["chars"], last_str))

    # --- Details Window ---
    def show_details_window(self):
        p = self.get_selected_prefs_path()
        if p:
            if self.details_window and self.details_window.winfo_exists(): self.details_window.destroy()
            self.details_window=tk.Toplevel(self.master); self.details_window.title(f"Details for {p.name}"); self.details_window.geometry("900x700")
            self.details_window.transient(self.master); self._center_window(self.details_window)
            ttk.Label(self.details_window, text="Analyzing files, please wait...", font="-size 12").pack(pady=20)
            self.run_task(self._task_gather_details, p)
    def _task_gather_details(self, p):
        try:
            self.task_queue.put(('log', f"Gathering details for {p}..."))
            self.task_queue.put(('status', f"Inspecting {p.name}..."))
            inc, exc = self.get_current_filters()
            files=collect_files(p, inc, exc)
            self.task_queue.put(('details_results', {"path": p, "files": files, "total_size": sum(f.stat().st_size for f in files)}))
            self.task_queue.put(('task_done', f"Inspection of {p.name} complete."))
        except Exception as e: self.task_queue.put(('error', f"Failed to get details: {e}\n{traceback.format_exc()}")); self.task_queue.put(('task_done', "Inspection failed."))
    def _populate_details_window(self, data):
        if not (self.details_window and self.details_window.winfo_exists()): return
        for w in self.details_window.winfo_children(): w.destroy()
        
        self.details_window.rowconfigure(1, weight=1); self.details_window.columnconfigure(0, weight=1)
        sf=ttk.LabelFrame(self.details_window, text="Backup Summary", padding=10); sf.grid(row=0, column=0, sticky="ew", padx=10, pady=5)
        ttk.Label(sf, text=f"Path: {pretty(data['path'])}\nTotal Files to Backup: {len(data['files'])}  |  Total Size: {format_size(data['total_size'])}").pack(anchor='w')
        
        dp=ttk.PanedWindow(self.details_window, orient='horizontal'); dp.grid(row=1, column=0, sticky='nsew', padx=10, pady=5)
        af=ttk.LabelFrame(dp, text="Folder Contents", padding=5); dp.add(af, weight=1); af.rowconfigure(0, weight=1); af.columnconfigure(0, weight=1)
        contents_tree = ttk.Treeview(af, show="tree"); contents_tree.grid(row=0, column=0, sticky='nsew')
        root_id = contents_tree.insert("", "end", text=f"🗂️ {data['path'].name} (All Files)", open=True, iid="::root::")
        for acc_dir in iter_accounts(data['path']):
            acc_id = contents_tree.insert(root_id, "end", text=f"👤 {acc_dir.name}", open=False, iid=str(acc_dir))
            for char_dir in iter_chars(acc_dir):
                contents_tree.insert(acc_id, "end", text=f"  - {char_dir.name}", iid=str(char_dir))
        
        ff=ttk.LabelFrame(dp, text="Files Included in Selection", padding=5); dp.add(ff, weight=3); ff.rowconfigure(0, weight=1); ff.columnconfigure(0, weight=1)
        ft=tk.Text(ff, wrap="none", font=("Consolas", 9)); ft.grid(row=0, column=0, sticky='nsew')
        fvsb=ttk.Scrollbar(ff, orient="vertical", command=ft.yview); fvsb.grid(row=0, column=1, sticky='ns'); ft.configure(yscrollcommand=fvsb.set)

        def update_file_list(event):
            selection_id = contents_tree.focus()
            ft.config(state="normal"); ft.delete("1.0", "end")
            if not selection_id or selection_id == "::root::": filtered_files = data['files']
            else: filtered_files = [f for f in data['files'] if Path(selection_id) in f.parents]
            file_list_str = "\n".join(str(p.relative_to(data['path'])) for p in sorted(filtered_files))
            ft.insert("1.0", file_list_str if filtered_files else "No files in this selection.")
            ft.config(state="disabled")
        contents_tree.bind("<<TreeviewSelect>>", update_file_list)
        contents_tree.selection_set(root_id); contents_tree.focus(root_id)

    # --- Backup & Restore ---
    def create_migration_kit(self):
        src = self.get_selected_prefs_path()
        if not src: self.errorf("Please select a SOURCE Prefs folder first."); return
        self.run_task(self._task_create_kit, src)
    def _task_create_kit(self, src_prefs):
        try:
            self.task_queue.put(('log', f"Starting backup process for: {pretty(src_prefs)}"))
            include, exclude = self.get_current_filters()
            self.task_queue.put(('log', f"Using Include filters: {' '.join(include)}"))
            self.task_queue.put(('log', f"Using Exclude filters: {' '.join(exclude)}"))
            files = collect_files(src_prefs, include, exclude)
            if not files: raise RuntimeError("No files matched filters. Nothing to back up.")
            self.task_queue.put(('log', f"Found {len(files)} files to include in the backup."))
            backup_dir = src_prefs.parent / "GooeyGuard_Backups"; backup_dir.mkdir(exist_ok=True)
            zip_path = backup_dir / f"{APP_NAME}_Backup_{tsfile()}.zip"
            readme = (f"GooeyGuard v{VERSION} — AO Prefs Backup\n{'='*40}\n\n"
                      f"This backup was created from:\nSOURCE: {pretty(src_prefs)}\n\n"
                      f"--- Manual Restore Instructions ---\n"
                      f"1. Close Anarchy Online completely.\n"
                      f"2. Make a safety copy of your NEW 'Prefs' folder.\n"
                      f"3. Extract this .zip, then copy the contents of the 'payload' folder\n"
                      f"   into your NEW 'Prefs' folder, overwriting files.\n")
            with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, 9) as zf:
                zf.writestr("MANIFEST.json", json.dumps({"tool":APP_NAME, "ver":VERSION, "src":str(src_prefs)}, indent=2))
                zf.writestr("README.txt", readme)
                for i, f in enumerate(files): self.task_queue.put(('status', f"Archiving file {i+1}/{len(files)}...")); zf.write(f, f"payload/{f.relative_to(src_prefs).as_posix()}")
            self.task_queue.put(('kit_success', str(zip_path)))
        except Exception as e: self.task_queue.put(('error', f"Backup creation failed: {e}\n{traceback.format_exc()}")); self.task_queue.put(('task_done', "Backup creation failed."))
    def direct_restore_kit(self):
        zip_path_str = filedialog.askopenfilename(title="Select a Backup Kit to Restore", filetypes=[("Backup Kits", "*.zip")])
        if not zip_path_str: return
        target_path_str = filedialog.askdirectory(title="Select the TARGET 'Prefs' Folder to Restore Into")
        if not target_path_str: return
        
        zip_path, target_path = Path(zip_path_str), Path(target_path_str)
        self.logf(f"Initiating direct restore of '{zip_path.name}' to '{target_path}'.")
        if not messagebox.askokcancel("Confirm Restore", f"You are about to restore files to:\n\n{target_path}\n\nThis will OVERWRITE existing files. A safety backup will be made first. Proceed?"):
            self.logf("User cancelled restore operation.")
            return
        self.run_task(self._task_direct_restore, zip_path, target_path)
    def _task_direct_restore(self, zip_path, target_path):
        try:
            self.task_queue.put(('status', f"Creating safety backup of target..."))
            self.task_queue.put(('log', f"Creating mandatory safety backup of '{target_path}' before restore."))
            safety_dir = target_path.parent / "GooeyGuard_Backups"; safety_dir.mkdir(exist_ok=True)
            safety_zip = safety_dir / f"SAFETY_{target_path.name}_{tsfile()}.zip"
            with zipfile.ZipFile(safety_zip, 'w', zipfile.ZIP_DEFLATED, 9) as zf: [zf.write(f, f.relative_to(target_path)) for f in target_path.rglob('*')]
            self.task_queue.put(('log', f"Safety backup completed: {safety_zip}"))

            self.task_queue.put(('status', "Restoring files...")); count = 0
            with zipfile.ZipFile(zip_path, 'r') as zf:
                payloads = [m for m in zf.infolist() if m.filename.startswith('payload/') and not m.is_dir()]
                self.task_queue.put(('log', f"Found {len(payloads)} files to restore from backup."))
                for i, member in enumerate(payloads):
                    self.task_queue.put(('status', f"Extracting {i+1}/{len(payloads)}...")); target_file = target_path / Path(member.filename).relative_to('payload')
                    target_file.parent.mkdir(parents=True, exist_ok=True); member_data = zf.read(member.filename); target_file.write_bytes(member_data); count+=1
            
            self.task_queue.put(('task_done', "Restore complete!")); self.task_queue.put(('log', f"Successfully restored {count} files to {target_path}"))
            messagebox.showinfo("Success", f"Restore complete.\n\nYour safety backup is located at:\n{safety_zip}")
        except Exception as e: self.task_queue.put(('error', f"Restore failed: {e}\n{traceback.format_exc()}")); self.task_queue.put(('task_done', "Restore failed."))
        
    def show_about_popup(self):
        about = tk.Toplevel(self.master); about.title("About GooeyGuard"); about.geometry("350x180"); about.transient(self.master); about.resizable(False, False)
        self._center_window(about)
        ttk.Label(about, text=f"{APP_NAME} v{VERSION}", font="-size 14 -weight bold").pack(pady=(15, 5))
        ttk.Label(about, text="A safe backup and migration tool for\nAnarchy Online character settings.", justify='center').pack()
        link = ttk.Label(about, text="Donate to the developer", foreground="blue", cursor="hand2"); link.pack(pady=10)
        link.bind("<Button-1>", lambda e: webbrowser.open_new_tab(DONATION_URL))
        ttk.Button(about, text="Close", command=about.destroy).pack(pady=15)
        about.focus_set(); about.grab_set()

def main():
    try: from ctypes import windll; windll.shcore.SetProcessDpiAwareness(1)
    except (ImportError, AttributeError): pass
    root = tk.Tk()
    style = ttk.Style()
    if sys.platform == "win32": style.theme_use('vista')
    elif sys.platform == "darwin": style.theme_use('aqua')
    else: style.theme_use('clam')
    
    app = GooeyGuardApp(root)
    root.mainloop()

if __name__ == "__main__":
    main()