using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AOSharp.Core;
using AOSharp.Core.UI;

namespace RubiKit.Bridge
{
    /// <summary>
    /// Minimal, resilient chat executor:
    /// - Call Init() once on plugin load.
    /// - Call Prime(window) from any command that receives a ChatWindow (e.g., /rubi, /dgt).
    /// - Queue() or QueueMany() from HTTP endpoints.
    /// </summary>
    public static class MacroSubmitter
    {
        static ChatWindow _win;
        static MethodInfo _submit;           // cached one-arg submit method on ChatWindow
        static readonly Queue<string> _q = new Queue<string>();
        static int _delayMs = 150;
        static double _accumMs = 0;
        static bool _inited;

        public static void Init(int delayMs = 150)
        {
            if (_inited) return;
            _inited = true;
            _delayMs = Math.Max(0, delayMs);
            Game.OnUpdate += Tick;
        }

        public static void Prime(ChatWindow window)
        {
            if (window is null) return;
            _win = window;
            _submit = FindSubmit(window);
        }

        public static void Queue(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_q) _q.Enqueue(line.Trim());
        }

        public static void QueueMany(IEnumerable<string> lines)
        {
            if (lines is null) return;
            lock (_q)
            {
                foreach (var l in lines)
                {
                    if (!string.IsNullOrWhiteSpace(l))
                        _q.Enqueue(l.Trim());
                }
            }
        }

        // --- Pump ---
        static void Tick(object sender, float dt)
        {
            if (_q.Count == 0) return;

            _accumMs += dt * 1000.0;
            if (_accumMs < _delayMs) return;
            _accumMs = 0;

            string next = null;
            lock (_q)
            {
                if (_q.Count > 0) next = _q.Dequeue();
            }
            if (next is null) return;

            var text = Normalize(next);
            if (!Submit(text))
            {
                // Visible fallback so you can confirm flow even before priming
                Chat.WriteLine($"<color=#ffcc66>[RubiKit]</color> would send: {Escape(text)}");
            }
        }

        // --- Helpers ---
        static string Normalize(string s)
        {
            var t = s.Trim();
            if (t.Length == 0) return t;
            // make plain text a vicinity chat
            return (t[0] == '/' || t[0] == '!' || t[0] == '#') ? t : "/s " + t;
        }

        static bool Submit(string text)
        {
            // 1) Preferred: active ChatWindow submit method
            if (_win is ChatWindow)
            {
                _submit = _submit ?? FindSubmit(_win);
                if (_submit is MethodInfo)
                {
                    try { _submit.Invoke(_win, new object[] { text }); return true; } catch { }
                }
            }

            // 2) Optional: try a static AOSharp Chat executor if present in your build
            //    (kept generic so it won't break builds lacking it)
            try
            {
                var cand = typeof(Chat).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(string) &&
                        NameLooksLikeSubmit(m.Name));
                if (cand is MethodInfo mi)
                {
                    mi.Invoke(null, new object[] { text });
                    return true;
                }
            }
            catch { /* harmless if absent */ }

            return false;
        }

        static MethodInfo FindSubmit(ChatWindow w) =>
            w.GetType()
             .GetMethods(BindingFlags.Instance | BindingFlags.Public)
             .FirstOrDefault(m =>
             {
                 var ps = m.GetParameters();
                 if (ps.Length != 1 || ps[0].ParameterType != typeof(string)) return false;
                 return NameLooksLikeSubmit(m.Name);
             });

        static bool NameLooksLikeSubmit(string n)
        {
            n = n?.ToLowerInvariant() ?? string.Empty;
            return n.Contains("send") || n.Contains("submit") || n.Contains("enter") || n.Contains("post");
        }

        static string Escape(string s) => s?.Replace("<", "&lt;").Replace(">", "&gt;") ?? string.Empty;
    }
}
