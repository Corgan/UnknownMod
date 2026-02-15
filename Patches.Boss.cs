using HarmonyLib;
using UnknownMod.Runtime;

namespace UnknownMod
{
    /// <summary>
    /// Boss-specific patches — custom boss handler activation.
    /// </summary>
    public static partial class Patches
    {
        /// <summary>
        /// After MatchManager.UpdateBossNpc() checks vanilla bosses,
        /// also check for the Mycelarch and create the custom handler.
        /// </summary>
        [HarmonyPatch(typeof(MatchManager), "UpdateBossNpc")]
        [HarmonyPostfix]
        public static void UpdateBossNpc_Postfix(MatchManager __instance)
        {
            // Only proceed if no boss handler was assigned by vanilla
            if (__instance.BossNpc != null)
                return;

            NPC[] team = __instance.GetTeamNPC();
            foreach (NPC npc in team)
            {
                if (npc != null && npc.NPCIsBoss() && npc.NpcData.Id.StartsWith("myc_mycelarch"))
                {
                    __instance.BossNpc = new MycelarchBoss(npc);
                    Plugin.Log.LogInfo("[Patches] Mycelarch boss handler activated.");
                    break;
                }
            }
        }
    }
}
