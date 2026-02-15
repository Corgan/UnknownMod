using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnknownMod.Core;
using UnknownMod.Runtime;

namespace UnknownMod
{
    /// <summary>
    /// Harmony patches — split by concern into partial files.
    /// This file: content injection and map building.
    /// </summary>
    [HarmonyPatch]
    public static partial class Patches
    {
        /// <summary>
        /// After Globals.CreateGameContent() finishes loading vanilla data,
        /// discover and build all modded content via LoadOrderManager.
        /// </summary>
        [HarmonyPatch(typeof(Globals), "CreateGameContent")]
        [HarmonyPostfix]
        public static void CreateGameContent_Postfix()
        {
            Plugin.Log.LogInfo("[Patches] Injecting modded content...");

            // Load all mods from disk in load-order sequence and build them
            LoadOrderManager.LoadAndBuildAll();

            // Ensure the persistent editor exists
            ModRegistry.EnsureEditorExists();

            Plugin.Log.LogInfo("[Patches] Modded content injection complete.");
        }

        /// <summary>
        /// [TEST] Transpiler on BeginAdventure: replaces the hardcoded "sen_0" string
        /// with "myc_0" so new adventures start in our zone.
        /// Remove this patch when done testing.
        /// </summary>
        [HarmonyPatch(typeof(AtOManager), "BeginAdventure")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> BeginAdventure_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (var inst in instructions)
            {
                if (inst.opcode == OpCodes.Ldstr && (string)inst.operand == "sen_0")
                {
                    inst.operand = "myc_0";
                    replaced++;
                }
                yield return inst;
            }
            if (replaced == 0)
                Plugin.Log.LogWarning("[Transpiler] Did NOT find \"sen_0\" in BeginAdventure IL!");
        }

        /// <summary>
        /// Intercept MapManager.IncludeMapPrefab() for modded zones.
        /// When a node from a modded zone is requested, build the zone map at runtime
        /// instead of looking for a pre-built prefab in mapList.
        /// </summary>
        [HarmonyPatch(typeof(MapManager), "IncludeMapPrefab")]
        [HarmonyPrefix]
        public static bool IncludeMapPrefab_Prefix(string nodeId, ref bool __result)
        {
            if (string.IsNullOrEmpty(nodeId))
                return true;

            var nodeData = Globals.Instance.GetNodeData(nodeId);
            if (nodeData == null || nodeData.NodeZone == null)
                return true;

            string zoneId = nodeData.NodeZone.ZoneId;

            if (!ModRegistry.IsModdedZone(zoneId))
                return true;

            __result = MapBuilder.BuildAndInjectMap(zoneId, MapManager.Instance.worldTransform);
            return false;
        }
    }
}
