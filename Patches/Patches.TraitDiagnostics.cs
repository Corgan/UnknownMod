using HarmonyLib;

namespace UnknownMod
{
    /// <summary>
    /// The game rejects a level-up trait silently (AssignTrait just returns false), so log why.
    /// </summary>
    public static partial class Patches
    {
        [HarmonyPatch(typeof(Hero), nameof(Hero.AssignTrait))]
        [HarmonyPostfix]
        public static void AssignTrait_Post(Hero __instance, string traitName, bool __result)
        {
            if (__result) return;
            string why;
            var data = Globals.Instance.GetTraitData(Globals.Instance.GetCanonicalTraitId(traitName, __instance), __instance);
            int level = __instance.HeroData?.HeroSubClass?.GetTraitLevel(traitName) ?? -1;
            if (data == null) why = "no TraitData is registered under that id";
            else if (level < 0) why = $"the id is not one of the hero's traits ({__instance.HeroData?.HeroSubClass?.Id})";
            else why = $"level slot {level} already holds '{__instance.Traits?[level]}'";
            Plugin.Log.LogWarning($"[Trait] AssignTrait('{traitName}') for {__instance.SourceName} rejected: {why}.");
        }

        [HarmonyPatch(typeof(TraitLevel), nameof(TraitLevel.OnMouseUp))]
        [HarmonyPrefix]
        public static void TraitLevelClick_Pre(TraitLevel __instance) =>
            Trace($"TraitLevel click: active={Traverse.Create(__instance).Field("active").GetValue()} trait={Traverse.Create(__instance).Field<TraitData>("traitData").Value?.Id ?? "null"}");
    }
}
