using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>
    /// Routes the game's id-keyed aura and trait logic to mod IAuraBehaviour / ITraitBehaviour handlers.
    /// Own class so a failure here cannot disable the main Patches class. Handler exceptions are caught
    /// and logged so a broken script cannot break combat.
    /// </summary>
    [HarmonyPatch]
    public static class BehaviourPatches
    {
        [HarmonyPatch(typeof(Trait), nameof(Trait.DoTrait))]
        [HarmonyPrefix]
        public static bool DoTrait_Prefix(Enums.EventActivation _theEvent, string _trait, Character _character,
            Character _target, int _auxInt, string _auxString, Cards.CardRealtimeData _castedCard)
        {
            var handlers = ScriptLoader.FindTraitBehaviours(_trait);
            if (handlers == null) return true;
            bool runVanilla = true;
            Scripting.TraitContext.Card = _castedCard;
            try
            {
                foreach (var h in handlers)
                {
                    if (ScriptHost.Call<bool>(h, "OnActivate", () => h.OnActivate(_theEvent, _character, _target, _auxInt, _auxString)))
                        runVanilla = false;
                }
            }
            finally { Scripting.TraitContext.Card = null; }
            return runVanilla;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetAuraCurse))]
        [HarmonyPostfix]
        public static void SetAuraCurse_Postfix(Character __instance, Character theCaster, AuraCurseData _acData,
            int charges, Character.AuraSetResult __result)
        {
            if (_acData == null || (__result != Character.AuraSetResult.Added && __result != Character.AuraSetResult.Updated)) return;
            var handlers = ScriptLoader.FindAuraBehaviours(_acData.Id);
            if (handlers == null) return;
            int total = __instance.GetAuraCharges(_acData.Id);
            foreach (var h in handlers)
            {
                ScriptHost.Call(h, "OnApplied", () => h.OnApplied(__instance, theCaster, charges, total));
            }
        }

        [HarmonyPatch(typeof(MatchManager), "ConsumeAuraCurseCo")]
        [HarmonyPostfix]
        public static void ConsumeAuraCurseCo_Postfix(string whenToConsume, Character character, string auraToConsume, ref IEnumerator __result)
        {
            if (!ScriptLoader.HasAuraBehaviours || character?.AuraCurseList == null || __result == null) return;
            var before = new List<KeyValuePair<string, int>>();
            foreach (var ac in character.AuraCurseList.ToArray())
            {
                if (ac?.ACData == null || ac.AuraCharges <= 0) continue;
                if (!string.IsNullOrEmpty(auraToConsume) && !string.Equals(ac.ACData.Id, auraToConsume, StringComparison.OrdinalIgnoreCase)) continue;
                if (ScriptLoader.FindAuraBehaviours(ac.ACData.Id) != null)
                    before.Add(new KeyValuePair<string, int>(ac.ACData.Id, ac.AuraCharges));
            }
            if (before.Count > 0) __result = AfterConsume(__result, whenToConsume, character, before);
        }

        /// <summary>Runs the game's coroutine to its end, then tells the aura handlers. Not reached if the coroutine is stopped.</summary>
        private static IEnumerator AfterConsume(IEnumerator inner, string whenToConsume, Character character, List<KeyValuePair<string, int>> before)
        {
            while (inner.MoveNext()) yield return inner.Current;
            if (character == null || !character.Alive) yield break;
            foreach (var entry in before)
            {
                var handlers = ScriptLoader.FindAuraBehaviours(entry.Key);
                if (handlers == null) continue;
                foreach (var h in handlers)
                    ScriptHost.Call(h, "OnConsume", () => h.OnConsume(character, whenToConsume, entry.Value));
            }
        }
    }
}
