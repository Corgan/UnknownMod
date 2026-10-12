using HarmonyLib;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>
    /// Boss-specific patches — activates mod-provided boss controllers
    /// discovered by <see cref="ScriptLoader"/> from scripts.dll files.
    /// </summary>
    public static partial class Patches
    {
        /// <summary>
        /// After MatchManager.UpdateBossNpc() checks vanilla bosses,
        /// check if any mod script provides a boss controller for an
        /// NPC on the field.
        /// </summary>
        [HarmonyPatch(typeof(MatchManager), "UpdateBossNpc")]
        [HarmonyPostfix]
        public static void UpdateBossNpc_Postfix(MatchManager __instance)
        {
            // Only proceed if no boss handler was assigned by vanilla
            if (__instance.BossNpc != null)
                return;

            var team = __instance.GetTeamNPC();
            if (team == null) return;
            foreach (Character character in team)
            {
                NPC npc = character as NPC;
                if (npc == null || !npc.NPCIsBoss()) continue;

                var controller = ScriptLoader.FindBossController(npc.NpcData.Id);
                if (controller != null)
                {
                    var boss = ScriptHost.Call<BossNPC>(controller, "CreateBossNpc", () => controller.CreateBossNpc(npc));
                    if (boss == null) continue;
                    __instance.BossNpc = boss;
                    Plugin.Log.LogInfo($"[Patches] Boss controller '{controller.GetType().Name}' activated for '{npc.NpcData.Id}'");
                    break;
                }
            }
        }
    }
}
