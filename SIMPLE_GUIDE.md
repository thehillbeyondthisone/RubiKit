# 🎯 SIMPLE GUIDE: Your RubiKit Setup (For Non-Programmers)

## ✅ What You Have Right Now

**Location:** Your computer, in the `RubiKit` folder
**Branch:** `main` (the main version)
**Status:** All your latest work is HERE and SAFE!

---

## 📁 Your Folder Structure

```
RubiKit/
├── RubiKit.cs             ← Backend code (C#)
├── GameData/              ← NotumHUD backend (NEW!)
├── Web/                   ← Frontend (HTML/CSS/JS)
│   ├── index.html
│   ├── modules/
│   │   ├── notumhud/      ← NotumHUD module (NEW!)
│   │   └── webdock/
│   └── ...
└── packages/              ← Dependencies
```

---

## 🚀 How to Build & Deploy

### **Step 1: Build the DLL**

Open Visual Studio and click "Build Solution" (or press `Ctrl+Shift+B`)

This creates: `bin/x86/Release/RubiKit.dll`

---

### **Step 2: Copy to AOSharp**

Copy these 3 things to your AOSharp plugins folder:

```
From RubiKit repo                  →  To AOSharp Plugins
─────────────────────                 ──────────────────
bin/x86/Release/RubiKit.dll        →  Plugins/RubiKit/RubiKit.dll
Web/  (entire folder)              →  Plugins/RubiKit/Web/
packages/  (entire folder)         →  Plugins/RubiKit/packages/
```

---

### **Step 3: Test In-Game**

1. Launch Anarchy Online with AOSharp
2. Type `/rubi` in chat
3. Browser opens showing the dashboard
4. Click "NotumHUD" to test

---

## 🔄 Git Workflow (Simplified)

### **If You Make Changes:**

```bash
# 1. Save your changes
git add .
git commit -m "Describe what you changed"

# 2. Try to push to GitHub
git push

# If it says "rejected" or "403 error", that's OK!
# Your work is saved locally, you just can't push to GitHub
# (probably due to branch protection)
```

---

## 🆘 Common Questions

### **Q: Where is my latest work?**
**A:** On the `main` branch on your computer. Type `git branch` to confirm.

### **Q: How do I push to GitHub?**
**A:** You might have branch protection enabled. Contact your GitHub admin, or just keep working locally (it's fine!).

### **Q: Can I lose my work?**
**A:** No! As long as you commit changes (Step 1 above), your work is safe.

### **Q: What if I accidentally delete something?**
**A:** Type `git status` to see what changed. Type `git restore <filename>` to undo.

---

## 📦 Quick Reference

| Task | Command |
|------|---------|
| See what branch you're on | `git branch` |
| See what changed | `git status` |
| Save changes | `git add . && git commit -m "message"` |
| Undo uncommitted changes | `git restore <filename>` |
| See recent commits | `git log --oneline -10` |

---

## ✨ What's New in Your Code

1. ✅ **NotumHUD integrated** - No separate DLL needed
2. ✅ **Web/ folder at repo root** - Easier to find and deploy
3. ✅ **Modules auto-load** - No manual scanning required
4. ✅ **.gitignore added** - Build files won't clutter repo
5. ✅ **Clean architecture** - Well-documented in ARCHITECTURE.md

---

## 🎉 You're All Set!

Your code is:
- ✅ Safe on your computer
- ✅ Organized cleanly
- ✅ Ready to build and deploy
- ✅ Fully documented

Just follow the "Build & Deploy" steps above whenever you want to test!

---

**Questions?** Check `ARCHITECTURE.md` for detailed technical docs.
