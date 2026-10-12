using HarmonyLib;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>Lets rule sets narrow which cards an enemy may play. Own class so a game update that changes the AI cannot disable other patches.</summary>
    [HarmonyPatch]
    public static class RulePatches
    {
        [HarmonyPatch(typeof(AI), "CastConditionsMetNotRelatedToTarget")]
        [HarmonyPostfix]
        public static void AiCastConditions_Post(AICards theAICard, NPC caster, ref bool __result)
        {
            if (!__result || !RuleEngine.HasRules) return;
            __result = RuleEngine.AiAllows(caster, theAICard?.Card?.Id);
        }
    }
}
