using System.Collections.Generic;
using System.Reflection;
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
        // ═══════════════════════════════════════════════════════════════
        //  BASE-GAME NULL GUARDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// HeroData.Awake() does Regex.Replace(heroName, ...) which throws
        /// ArgumentNullException when heroName is null — as it always is when
        /// we create a HeroData via ScriptableObject.CreateInstance because
        /// Awake fires before we can assign any fields. Skip in that case.
        /// </summary>
        [HarmonyPatch(typeof(HeroData), "Awake")]
        [HarmonyPrefix]
        public static bool HeroDataAwake_Prefix(HeroData __instance)
        {
            // Let Awake run normally when heroName is set (e.g. asset deserialization).
            // Skip it when heroName is null (our runtime CreateInstance path).
            return __instance.HeroName != null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  CONTENT INJECTION
        // ═══════════════════════════════════════════════════════════════

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
        /// Transpiler on BeginAdventure: replaces the ldstr "sen_0" with a call to
        /// ModRegistry.GetStarterNode() so the starter node is resolved at runtime
        /// (after mods have loaded and configured StarterNodeId).
        /// </summary>
        [HarmonyPatch(typeof(AtOManager), "BeginAdventure")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> BeginAdventure_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var resolver = typeof(ModRegistry).GetMethod(
                nameof(ModRegistry.GetStarterNode),
                BindingFlags.Public | BindingFlags.Static);

            int replaced = 0;
            foreach (var inst in instructions)
            {
                if (inst.opcode == OpCodes.Ldstr && (string)inst.operand == "sen_0")
                {
                    // Replace:  ldstr "sen_0"
                    // With:     call string ModRegistry::GetStarterNode()
                    yield return new CodeInstruction(OpCodes.Call, resolver);
                    replaced++;
                }
                else
                {
                    yield return inst;
                }
            }
            if (replaced > 0)
                Plugin.Log.LogInfo($"[Transpiler] Patched {replaced} 'sen_0' reference(s) in BeginAdventure → GetStarterNode()");
            else
                Plugin.Log.LogWarning("[Transpiler] Did NOT find 'sen_0' in BeginAdventure IL!");
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

            // If map is already built (returns false), that's still a success for the game
            __result = MapBuilder.BuildAndInjectMap(zoneId, MapManager.Instance.worldTransform)
                    || MapBuilder.MapExists(zoneId, MapManager.Instance.worldTransform);
            return false;
        }
    }
}
