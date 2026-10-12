using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Animation retargeting. The game fires a handful of animator triggers (attack, cast, hit, pet) on every character rig.
    /// A rig that does not own a trigger (a monster used as a hero, a model with one attack animation) silently does nothing.
    /// A retarget map says which of the rig's own triggers to fire instead, and rigs that lack a trigger fall back to a sensible neighbour.
    /// </summary>
    public static class AnimRetarget
    {
        /// <summary>Value that swallows a trigger so nothing plays.</summary>
        public const string Swallow = "-";

        /// <summary>Triggers the game fires on character rigs.</summary>
        public static readonly string[] GameTriggers = { "attack", "cast", "hit", "pet" };

        // The neighbours a rig may borrow when it lacks the trigger the game asked for.
        private static readonly Dictionary<string, string[]> Fallbacks = new(StringComparer.OrdinalIgnoreCase)
        {
            { "attack", new[] { "cast" } },
            { "cast", new[] { "attack" } },
        };

        private sealed class Entry
        {
            public Dictionary<string, string> Map;
            public bool Fallback;
            public HashSet<string> Params;
            public bool Adopted;      // copied from the owning character's entry (pets)
            public int AdoptedVersion;
        }

        private static ConditionalWeakTable<Animator, Entry> Bound = new();

        // Animators already checked for an owner to inherit from, so unrelated animators cost one lookup.
        private sealed class Checked { public int Version; }
        private static ConditionalWeakTable<Animator, Checked> Seen = new();
        private static int _version;
        private static readonly Dictionary<int, string> HashNames = new();

        // The projects' own dictionaries, so editor edits are seen the next time a character appears.
        private static readonly Dictionary<string, ModProject> Mods = new();

        /// <summary>Retargets that fired recently, newest last. Shown in the editor.</summary>
        public static readonly List<string> Recent = new();
        private const int LogCap = 30;

        static AnimRetarget()
        {
            foreach (var n in GameTriggers) HashNames[Animator.StringToHash(n)] = n;
        }

        // ── Catalog ────────────────────────────────────────────────

        public static void Register(ModProject proj)
        {
            if (proj == null) return;
            Mods[proj.ModId] = proj;
        }

        public static void Clear()
        {
            Mods.Clear();
            Bound = new ConditionalWeakTable<Animator, Entry>();
            Seen = new ConditionalWeakTable<Animator, Checked>();
            _version++;
        }

        public static bool ForNpc(string npcId, out Dictionary<string, string> map, out bool noFallback)
        {
            map = null; noFallback = false;
            if (string.IsNullOrEmpty(npcId)) return false;
            string baseId = ModRegistry.StripVariantSuffix(npcId);
            foreach (var proj in Mods.Values)
                foreach (var dict in new[] { proj.Npcs, proj.NpcPatches })
                {
                    if (dict == null) continue;
                    if (!dict.TryGetValue(npcId, out var def) && !dict.TryGetValue(baseId, out def)) continue;
                    map = def.AnimTriggers; noFallback = def.AnimNoFallback;
                    return true;
                }
            return false;
        }

        public static bool ForSkin(string skinId, out Dictionary<string, string> map, out bool noFallback)
        {
            map = null; noFallback = false;
            if (string.IsNullOrEmpty(skinId)) return false;
            foreach (var proj in Mods.Values)
                foreach (var dict in new[] { proj.Skins, proj.SkinPatches })
                {
                    if (dict == null || !dict.TryGetValue(skinId, out var def)) continue;
                    map = def.AnimTriggers; noFallback = def.AnimNoFallback;
                    return true;
                }
            return false;
        }

        // ── Binding ────────────────────────────────────────────────

        /// <summary>Attach a retarget to one rig's animator. Called when a character is created.</summary>
        public static void Bind(Animator anim, Dictionary<string, string> map, bool noFallback)
        {
            if (anim == null) return;
            _version++;
            Bound.Remove(anim);
            bool hasMap = map != null && map.Count > 0;
            if (!hasMap && noFallback) return;
            var entry = new Entry { Map = hasMap ? new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase) : null, Fallback = !noFallback };
            try
            {
                entry.Params = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in anim.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger) entry.Params.Add(p.name);
            }
            catch (Exception)
            {
                entry.Params = null;
            }
            if (hasMap)
                foreach (var key in entry.Map.Keys) HashNames[Animator.StringToHash(key)] = key;
            Bound.Add(anim, entry);
        }

        private static HashSet<string> TriggersOf(Animator anim)
        {
            try
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in anim.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger) set.Add(p.name);
                return set;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// The retarget for an animator. Pet animators on a bound character (the enchantment pets) have no entry of their own,
        /// so they follow their owner's map and fall back using their own triggers.
        /// </summary>
        private static bool TryGet(Animator anim, out Entry entry)
        {
            entry = null;
            if (anim == null) return false;
            if (Bound.TryGetValue(anim, out entry))
            {
                if (!entry.Adopted || entry.AdoptedVersion == _version) return true;
                Bound.Remove(anim); // the owner was rebound since; re-derive
                entry = null;
            }
            if (Seen.TryGetValue(anim, out var seen) && seen.Version == _version) return false;
            if (seen == null) Seen.Add(anim, seen = new Checked());
            seen.Version = _version;
            // Pet objects carry their own CharacterItem with no animator set, so walk up until one that owns a bound animator
            Entry parent = null;
            for (var t = anim.transform; t != null && parent == null; t = t.parent)
            {
                var owner = t.GetComponent<CharacterItem>();
                if (owner != null && owner.Anim != null && owner.Anim != anim) Bound.TryGetValue(owner.Anim, out parent);
            }
            if (parent == null) return false;
            entry = new Entry { Map = parent.Map, Fallback = parent.Fallback, Params = TriggersOf(anim), Adopted = true, AdoptedVersion = _version };
            Bound.Add(anim, entry);
            return true;
        }

        /// <summary>The trigger a rig should fire for the one the game asked for, or null for none.</summary>
        public static string Resolve(Animator anim, string name)
        {
            if (anim == null || string.IsNullOrEmpty(name) || !TryGet(anim, out var e)) return name;
            string to = name;
            if (e.Map != null && e.Map.TryGetValue(name, out var mapped) && !string.IsNullOrEmpty(mapped))
            {
                to = mapped == Swallow ? null : mapped;
            }
            else if (e.Fallback && e.Params != null && e.Params.Count > 0 && !e.Params.Contains(name) && Fallbacks.TryGetValue(name, out var chain))
            {
                foreach (var c in chain)
                    if (e.Params.Contains(c)) { to = c; break; }
            }
            if (!string.Equals(to, name, StringComparison.Ordinal)) Note(anim, name, to);
            return to;
        }

        private static void Note(Animator anim, string from, string to)
        {
            string line = $"{(anim.transform.root != null ? anim.transform.root.name : anim.name)}: {from} \u2192 {to ?? "(nothing)"}";
            if (Recent.Count > 0 && Recent[Recent.Count - 1] == line) return;
            Recent.Add(line);
            if (Recent.Count > LogCap) Recent.RemoveAt(0);
        }

        // ── Call-site replacements (the transpiler swaps the game's animator calls for these) ──

        public static void Set(Animator a, string name)
        {
            var to = Resolve(a, name);
            if (to != null) a.SetTrigger(to);
        }

        public static void Set(Animator a, int hash)
        {
            if (a != null && HashNames.TryGetValue(hash, out var name) && TryGet(a, out _))
            {
                var to = Resolve(a, name);
                if (to != null) a.SetTrigger(to);
                return;
            }
            a.SetTrigger(hash);
        }

        public static void Reset(Animator a, string name)
        {
            var to = Resolve(a, name);
            if (to != null) a.ResetTrigger(to);
        }

        public static void Reset(Animator a, int hash)
        {
            if (a != null && HashNames.TryGetValue(hash, out var name) && TryGet(a, out _))
            {
                var to = Resolve(a, name);
                if (to != null) a.ResetTrigger(to);
                return;
            }
            a.ResetTrigger(hash);
        }
    }
}

namespace UnknownMod
{
    /// <summary>Own class so a game update that moves an animator call cannot disable other patches.</summary>
    [HarmonyPatch]
    public static class AnimRetargetPatches
    {
        private static readonly MethodInfo SetStr = AccessTools.Method(typeof(Animator), nameof(Animator.SetTrigger), new[] { typeof(string) });
        private static readonly MethodInfo SetInt = AccessTools.Method(typeof(Animator), nameof(Animator.SetTrigger), new[] { typeof(int) });
        private static readonly MethodInfo ResetStr = AccessTools.Method(typeof(Animator), nameof(Animator.ResetTrigger), new[] { typeof(string) });
        private static readonly MethodInfo ResetInt = AccessTools.Method(typeof(Animator), nameof(Animator.ResetTrigger), new[] { typeof(int) });

        private static MethodInfo Replacement(object operand)
        {
            if (!(operand is MethodInfo m)) return null;
            if (m == SetStr) return AccessTools.Method(typeof(Runtime.AnimRetarget), nameof(Runtime.AnimRetarget.Set), new[] { typeof(Animator), typeof(string) });
            if (m == SetInt) return AccessTools.Method(typeof(Runtime.AnimRetarget), nameof(Runtime.AnimRetarget.Set), new[] { typeof(Animator), typeof(int) });
            if (m == ResetStr) return AccessTools.Method(typeof(Runtime.AnimRetarget), nameof(Runtime.AnimRetarget.Reset), new[] { typeof(Animator), typeof(string) });
            if (m == ResetInt) return AccessTools.Method(typeof(Runtime.AnimRetarget), nameof(Runtime.AnimRetarget.Reset), new[] { typeof(Animator), typeof(int) });
            return null;
        }

        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            var candidates = new List<MethodInfo>(AccessTools.GetDeclaredMethods(typeof(CharacterItem)));
            // Coroutines (such as the enchantment pet cast) compile into nested types whose MoveNext holds the calls.
            foreach (var nested in typeof(CharacterItem).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                candidates.AddRange(AccessTools.GetDeclaredMethods(nested).Where(x => x.Name == "MoveNext"));
            foreach (var m in candidates)
            {
                if (m.IsAbstract || m.GetMethodBody() == null) continue;
                List<CodeInstruction> il;
                try { il = PatchProcessor.GetCurrentInstructions(m); }
                catch (Exception) { continue; }
                if (il.Any(i => (i.opcode == OpCodes.Callvirt || i.opcode == OpCodes.Call) && Replacement(i.operand) != null))
                    yield return m;
            }
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                var repl = ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call ? Replacement(ins.operand) : null;
                if (repl != null) { ins.opcode = OpCodes.Call; ins.operand = repl; }
                yield return ins;
            }
        }

    }

    /// <summary>Harmony refuses a class that mixes TargetMethods with per-method annotations, so the binders live apart.</summary>
    public static class AnimRetargetBinders
    {
        // Bind the retarget when a character's model is created.

        [HarmonyPatch(typeof(NPCItem), "Init")]
        [HarmonyPostfix]
        public static void NpcInit_Post(NPCItem __instance)
        {
            try
            {
                string id = __instance?.NPC?.GameName;
                if (id == null || __instance.Anim == null) return;
                if (Runtime.AnimRetarget.ForNpc(id, out var map, out var noFallback)) Runtime.AnimRetarget.Bind(__instance.Anim, map, noFallback);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[AnimRetarget] NPC bind failed: {e.Message}"); }
        }

        [HarmonyPatch(typeof(HeroItem), "Init")]
        [HarmonyPostfix]
        public static void HeroInit_Post(HeroItem __instance, Hero _hero)
        {
            try
            {
                if (__instance?.Anim == null || string.IsNullOrEmpty(_hero?.SkinUsed)) return;
                if (Runtime.AnimRetarget.ForSkin(_hero.SkinUsed, out var map, out var noFallback)) Runtime.AnimRetarget.Bind(__instance.Anim, map, noFallback);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[AnimRetarget] Hero bind failed: {e.Message}"); }
        }
    }
}
