using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Ascension.Features
{
    // Fallback controller: if no public API exists, try to stop/start by:
    //  • invoking Stop/Pause/Disable & Start/Resume/Enable on candidate types
    //  • canceling CancellationTokenSource fields/props (pause only)
    //  • flipping common bool flags (Enabled/IsRunning) when setters exist
    public class ReflectiveController
    {
        public bool Enabled = true;
        public bool Verbose = false;

        private readonly Action<string> _info;
        private readonly Action<string> _warn;

        private static readonly string[] TypeHints = { "Stack", "Auto", "Routine", "Engine", "Runner", "Bot", "Equip", "Unequip", "Cycle", "Plugin" };
        private static readonly string[] PauseNames = { "Pause", "Stop", "Halt", "Suspend", "Disable", "Off", "Deactivate", "AutoOff", "StopStack", "StopStacking", "StopEquip", "StopAuto" };
        private static readonly string[] ResumeNames = { "Resume", "Start", "Continue", "Play", "Enable", "On", "Activate", "AutoOn", "StartStack", "StartStacking", "StartEquip", "StartAuto" };
        private static readonly string[] RunProps = { "IsRunning", "Running", "IsActive", "Active", "IsBusy", "Enabled", "IsEnabled", "IsOn", "On", "Auto", "IsAuto", "AutoRunning", "Stacking", "IsStacking" };
        private static readonly string[] SingletonNames = { "Instance", "Current", "Singleton", "Default", "Shared" };

        public ReflectiveController(Action<string> info, Action<string> warn)
        {
            _info = info ?? (_ => { });
            _warn = warn ?? (_ => { });
        }

        public bool TryPause()
        {
            if (!Enabled) return false;
            bool didSomething = false;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var an = asm.GetName().Name;
                if (an.StartsWith("System") || an.StartsWith("Microsoft"))
                    continue;

                foreach (var t in SafeGetTypes(asm))
                {
                    if (!LooksRelevant(t)) continue;

                    object inst = FindSingletonInstance(t);
                    var iflags = BindingFlags.Public | BindingFlags.NonPublic | (inst == null ? BindingFlags.Static : BindingFlags.Instance);

                    // 1) Direct Pause/Stop/Disable
                    var pause = t.GetMethods(iflags).FirstOrDefault(m => m.GetParameters().Length == 0 && PauseNames.Contains(m.Name));
                    if (pause != null)
                    {
                        if (TryInvoke(inst, pause)) { didSomething = true; if (Verbose) _info("[REFL] " + t.FullName + " ." + pause.Name + "()"); continue; }
                    }

                    // 2) Cancel any CTS fields/props
                    bool canceledAny = CancelAnyTokens(t, inst, iflags);
                    didSomething = didSomething || canceledAny;

                    // 3) Flip common run/enable flags to false
                    bool flipped = SetAnyRunFlag(t, inst, iflags, false);
                    didSomething = didSomething || flipped;
                }
            }

            return didSomething;
        }

        public bool TryResume()
        {
            if (!Enabled) return false;
            bool didSomething = false;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var an = asm.GetName().Name;
                if (an.StartsWith("System") || an.StartsWith("Microsoft"))
                    continue;

                foreach (var t in SafeGetTypes(asm))
                {
                    if (!LooksRelevant(t)) continue;

                    object inst = FindSingletonInstance(t);
                    var iflags = BindingFlags.Public | BindingFlags.NonPublic | (inst == null ? BindingFlags.Static : BindingFlags.Instance);

                    // 1) Direct Resume/Start/Enable
                    var start = t.GetMethods(iflags).FirstOrDefault(m => m.GetParameters().Length == 0 && ResumeNames.Contains(m.Name));
                    if (start != null)
                    {
                        if (TryInvoke(inst, start)) { didSomething = true; if (Verbose) _info("[REFL] " + t.FullName + " ." + start.Name + "()"); continue; }
                    }

                    // 2) Flip common run/enable flags to true (some engines poll these)
                    bool flipped = SetAnyRunFlag(t, inst, iflags, true);
                    didSomething = didSomething || flipped;
                }
            }

            return didSomething;
        }

        // Helpers

        private static Type[] SafeGetTypes(Assembly a) { try { return a.GetTypes(); } catch { return new Type[0]; } }

        private bool LooksRelevant(Type t)
        {
            if (t == null) return false;
            var ns = t.Namespace ?? "";
            if (ns.StartsWith("Ascension", StringComparison.OrdinalIgnoreCase)) return true;

            var name = t.Name;
            for (int i = 0; i < TypeHints.Length; i++)
                if (name.IndexOf(TypeHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            if (ns.IndexOf("AO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ns.IndexOf("AOS", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private bool TryInvoke(object inst, MethodInfo mi)
        {
            try { mi.Invoke(inst, null); return true; } catch { return false; }
        }

        private bool CancelAnyTokens(Type t, object inst, BindingFlags flags)
        {
            bool did = false;

            // Fields
            var fields = t.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (typeof(CancellationTokenSource).IsAssignableFrom(f.FieldType))
                {
                    try
                    {
                        var cts = f.GetValue(inst) as CancellationTokenSource;
                        if (cts != null && !cts.IsCancellationRequested) { cts.Cancel(); did = true; if (Verbose) _info("[REFL] " + t.FullName + " canceled " + f.Name); }
                    }
                    catch { }
                }
            }

            // Properties
            var props = t.GetProperties(flags);
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                if (typeof(CancellationTokenSource).IsAssignableFrom(p.PropertyType))
                {
                    try
                    {
                        var cts = p.GetValue(inst, null) as CancellationTokenSource;
                        if (cts != null && !cts.IsCancellationRequested) { cts.Cancel(); did = true; if (Verbose) _info("[REFL] " + t.FullName + " canceled " + p.Name); }
                    }
                    catch { }
                }
            }

            return did;
        }

        private bool SetAnyRunFlag(Type t, object inst, BindingFlags flags, bool value)
        {
            bool did = false;

            var props = t.GetProperties(flags);
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                if (p.CanWrite && p.PropertyType == typeof(bool) && RunProps.Contains(p.Name))
                {
                    try { p.SetValue(inst, value, null); did = true; if (Verbose) _info("[REFL] " + t.FullName + " " + p.Name + "=" + value); } catch { }
                }
            }

            var fields = t.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f.FieldType == typeof(bool) && RunProps.Contains(f.Name))
                {
                    try { f.SetValue(inst, value); did = true; if (Verbose) _info("[REFL] " + t.FullName + " " + f.Name + "=" + value); } catch { }
                }
            }

            return did;
        }

        private static object FindSingletonInstance(Type t)
        {
            for (int i = 0; i < SingletonNames.Length; i++)
            {
                var name = SingletonNames[i];

                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null && t.IsAssignableFrom(p.PropertyType))
                {
                    try { var val = p.GetValue(null, null); if (val != null) return val; } catch { }
                }

                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null && t.IsAssignableFrom(f.FieldType))
                {
                    try { var val = f.GetValue(null); if (val != null) return val; } catch { }
                }
            }
            return null;
        }
    }
}
