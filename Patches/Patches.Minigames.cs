using HarmonyLib;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>
    /// Town minigame content is serialised into scenes with no registry, so mod entries are appended to the
    /// live managers just before they read their pools. Kept in its own class so a failure here cannot
    /// disable the main Patches class.
    /// </summary>
    [HarmonyPatch]
    public static class MinigamePatches
    {
        [HarmonyPatch(typeof(BettingGameManager), "StartRound")]
        [HarmonyPrefix]
        public static void BettingStartRound(BettingGameManager __instance)
        {
            try { DataHelper.ApplyBettingTeams(__instance); }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"[Minigames] Betting teams: {ex.Message}"); }
        }

        [HarmonyPatch(typeof(MemorySequenceManager), "LoadCards")]
        [HarmonyPrefix]
        public static void MemoryLoadCards(MemorySequenceManager __instance)
        {
            try { DataHelper.ApplyMemorySequences(__instance); }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"[Minigames] Memory sequences: {ex.Message}"); }
        }

        [HarmonyPatch(typeof(CardPairMemoryManager), "InitializeGame")]
        [HarmonyPrefix]
        public static void CardPairInitializeGame(CardPairMemoryManager __instance)
        {
            try { DataHelper.ApplyCardPairRewards(__instance); }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"[Minigames] Card-pair rewards: {ex.Message}"); }
        }
    }
}
