using HarmonyLib;

namespace UnknownMod
{
    /// <summary>
    /// Optional switch that unhides content the game ships but hides from players via ReleaseContent
    /// (Beastmaster, Savannah/Shaman Cave/Zambora Plains/Frog Fight/Boss3/BossTests zones, their NPCs and cards).
    /// Own class so a failure cannot disable other patches. Needs a restart: the hidden sets are applied while Globals loads.
    /// </summary>
    [HarmonyPatch]
    public static class ReleaseContentPatches
    {
        private static bool Unhide => Plugin.UnhideUnreleasedContent != null && Plugin.UnhideUnreleasedContent.Value;

        [HarmonyPatch(typeof(ReleaseContent), nameof(ReleaseContent.IsHiddenSubClass))]
        [HarmonyPrefix]
        public static bool IsHiddenSubClass_Prefix(ref bool __result) => Skip(ref __result);

        [HarmonyPatch(typeof(ReleaseContent), nameof(ReleaseContent.IsHiddenZone))]
        [HarmonyPrefix]
        public static bool IsHiddenZone_Prefix(ref bool __result) => Skip(ref __result);

        [HarmonyPatch(typeof(ReleaseContent), nameof(ReleaseContent.IsHiddenZoneName))]
        [HarmonyPrefix]
        public static bool IsHiddenZoneName_Prefix(ref bool __result) => Skip(ref __result);

        [HarmonyPatch(typeof(ReleaseContent), nameof(ReleaseContent.IsHiddenNpc))]
        [HarmonyPrefix]
        public static bool IsHiddenNpc_Prefix(ref bool __result) => Skip(ref __result);

        [HarmonyPatch(typeof(ReleaseContent), nameof(ReleaseContent.IsHiddenCard))]
        [HarmonyPrefix]
        public static bool IsHiddenCard_Prefix(ref bool __result) => Skip(ref __result);

        /// <summary>When unhiding, answer "not hidden" and skip the original.</summary>
        private static bool Skip(ref bool result)
        {
            if (!Unhide) return true;
            result = false;
            return false;
        }
    }
}
