using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnknownMod.Scripting;

namespace UnknownMod.Core
{
    [Flags]
    public enum ScriptHook
    {
        None = 0, Update = 1, Scene = 2, CombatLaunch = 4, CombatEvent = 8, CardCast = 16,
        Killed = 32, CombatEnd = 64, EventShown = 128, EventOption = 256
    }

    /// <summary>One script object (a ModScript or one of the older interface handlers) and how it has been behaving.</summary>
    public sealed class ScriptHandlerInfo
    {
        public object Instance;
        public string ModId;
        public string TypeName;
        public string Kind;       // ModScript, IAuraBehaviour, ITraitBehaviour, IBossController
        public string Target;     // aura id, trait id or NPC prefix where there is one
        public ScriptHook Hooks;
        public int Calls, Errors;
        public long Ticks;
        public long SlowCalls;
        public bool Disabled;
        public string LastError;

        public double AverageMs => Calls == 0 ? 0 : Ticks * 1000.0 / Stopwatch.Frequency / Calls;
        public string Label => Target == null ? $"{Kind} {TypeName}" : $"{Kind} {TypeName} ({Target})";
    }

    /// <summary>
    /// Calls into scripts. Every call is guarded: an exception is logged and counted, the game carries on, and a handler
    /// that keeps failing is switched off. Calls are timed so a slow script shows up in the Scripts tab.
    /// </summary>
    public static class ScriptHost
    {
        public const int MaxErrors = 5;
        private static readonly double SlowMs = 50;

        private static readonly Dictionary<object, ScriptHandlerInfo> _info = new(ReferenceEqualityComparer.Instance);
        private static ScriptHandlerInfo[] _modScripts = Array.Empty<ScriptHandlerInfo>();
        private static ScriptHook _wanted;

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();
            bool IEqualityComparer<object>.Equals(object a, object b) => ReferenceEquals(a, b);
            int IEqualityComparer<object>.GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        public static ScriptHandlerInfo Info(object handler) => handler != null && _info.TryGetValue(handler, out var i) ? i : null;

        internal static void Register(ScriptHandlerInfo info) => _info[info.Instance] = info;

        internal static void Unregister(ScriptHandlerInfo info) => _info.Remove(info.Instance);

        /// <summary>Recomputes which ModScript hooks anything listens to. Call after handlers change or are switched on or off.</summary>
        internal static void Refresh()
        {
            _modScripts = _info.Values.Where(i => i.Instance is ModScript && !i.Disabled).ToArray();
            _wanted = _modScripts.Aggregate(ScriptHook.None, (m, i) => m | i.Hooks);
        }

        public static void SetDisabled(ScriptHandlerInfo info, bool disabled)
        {
            info.Disabled = disabled;
            if (!disabled) info.Errors = 0;
            Refresh();
        }

        /// <summary>Which hooks a ModScript subclass overrides, so hooks nobody listens to cost nothing.</summary>
        internal static ScriptHook HooksOf(Type type)
        {
            var map = new (string name, ScriptHook hook)[]
            {
                ("OnUpdate", ScriptHook.Update), ("OnSceneChanged", ScriptHook.Scene), ("OnCombatLaunch", ScriptHook.CombatLaunch),
                ("OnCombatEvent", ScriptHook.CombatEvent), ("OnCardCast", ScriptHook.CardCast), ("OnCharacterKilled", ScriptHook.Killed),
                ("OnCombatEnd", ScriptHook.CombatEnd), ("OnEventShown", ScriptHook.EventShown), ("OnEventOption", ScriptHook.EventOption)
            };
            var found = ScriptHook.None;
            foreach (var (name, hook) in map)
            {
                var m = type.GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (m != null && m.GetBaseDefinition().DeclaringType == typeof(ModScript) && m.DeclaringType != typeof(ModScript))
                    found |= hook;
            }
            return found;
        }

        public static bool Wants(ScriptHook hook) => (_wanted & hook) != 0;

        /// <summary>Calls one hook on every active ModScript that overrides it.</summary>
        public static void Dispatch(ScriptHook hook, Action<ModScript> call)
        {
            if (!Wants(hook)) return;
            foreach (var info in _modScripts)
            {
                if (info.Disabled || (info.Hooks & hook) == 0) continue;
                var script = (ModScript)info.Instance;
                Call(info, hook.ToString(), () => call(script));
            }
        }

        /// <summary>Runs one call into a handler. Returns false if it threw or the handler is switched off.</summary>
        public static bool Call(object handler, string hook, Action action)
        {
            var info = Info(handler);
            if (info == null) { try { action(); return true; } catch (Exception ex) { Plugin.Log.LogError($"[Script] {hook} threw: {ex}"); return false; } }
            if (info.Disabled) return false;

            long start = Stopwatch.GetTimestamp();
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                info.Errors++;
                info.LastError = $"{hook}: {ex.GetType().Name}: {ex.Message}";
                if (info.Errors == 1) Plugin.Log.LogError($"[Script] {info.ModId}: {info.Label} threw in {hook}: {ex}");
                else Plugin.Log.LogError($"[Script] {info.ModId}: {info.Label} threw in {hook} again ({info.Errors}): {ex.Message}");
                if (info.Errors >= MaxErrors)
                {
                    info.Disabled = true;
                    Plugin.Log.LogError($"[Script] {info.ModId}: {info.Label} was switched off after {MaxErrors} errors. Switch it back on in Extras > Scripts.");
                    Refresh();
                }
                return false;
            }
            finally
            {
                long took = Stopwatch.GetTimestamp() - start;
                info.Calls++;
                info.Ticks += took;
                if (took * 1000.0 / Stopwatch.Frequency > SlowMs && info.SlowCalls++ == 0)
                    Plugin.Log.LogWarning($"[Script] {info.ModId}: {info.Label} took {took * 1000.0 / Stopwatch.Frequency:F0} ms in {hook}. Hooks run on the main thread and block the game while they work.");
            }
        }

        /// <summary>Like <see cref="Call"/> for something that returns a value; <paramref name="fallback"/> when the call fails.</summary>
        public static T Call<T>(object handler, string hook, Func<T> func, T fallback = default)
        {
            T result = fallback;
            // A block body keeps this bound to the Action overload; an expression body would pick this generic one and recurse forever.
            Call(handler, hook, (Action)(() => { result = func(); }));
            return result;
        }
    }
}
