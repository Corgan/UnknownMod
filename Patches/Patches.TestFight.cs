using HarmonyLib;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>Hooks that drive <see cref="TestFight"/> through the game's Practice mode.</summary>
    public static partial class Patches
    {
        // The practice hero selection clears the chosen combat; set it again once it has done that
        [HarmonyPatch(typeof(HeroSelectionManager), "SetCombatToolUI")]
        [HarmonyPostfix]
        public static void TestFight_SetCombatToolUI_Post() => TestFight.Apply();

        [HarmonyPatch(typeof(AtOManager), nameof(AtOManager.LaunchCombat))]
        [HarmonyPrefix]
        public static void TestFight_LaunchCombat_Pre(CombatData _combatData) => TestFight.OnCombatLaunch(_combatData);

        [HarmonyPatch(typeof(AtOManager), nameof(AtOManager.FinishCombat))]
        [HarmonyPrefix]
        public static void TestFight_FinishCombat_Pre(bool resigned) => TestFight.OnCombatFinish(resigned);
    }
}
