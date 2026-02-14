using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Editor;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>
    /// Harmony patches to inject modded zone content into the game.
    /// </summary>
    [HarmonyPatch]
    public static class Patches
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

        // ═══════════════════════════════════════════════════════════════
        //  MAP SCENE — cache base-game zone data for editor synthesis
        // ═══════════════════════════════════════════════════════════════

        // ── Suppress MapManager initialization during additive scene load ──

        [HarmonyPatch(typeof(MapManager), "Awake")]
        [HarmonyPrefix]
        public static bool MapManager_Awake_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed MapManager.Awake during zone data extraction.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(MapManager), "Start")]
        [HarmonyPrefix]
        public static bool MapManager_Start_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed MapManager.Start during zone data extraction.");
                return false;
            }
            return true;
        }

        // ── Suppress MatchManager initialization during additive scene load ──

        [HarmonyPatch(typeof(MatchManager), "Awake")]
        [HarmonyPrefix]
        public static bool MatchManager_Awake_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed MatchManager.Awake during combat data extraction.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(MatchManager), "Start")]
        [HarmonyPrefix]
        public static bool MatchManager_Start_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed MatchManager.Start during combat data extraction.");
                return false;
            }
            return true;
        }

        // ── Suppress other Combat scene MonoBehaviours during additive load ──

        [HarmonyPatch(typeof(SideCharacters), "Start")]
        [HarmonyPrefix]
        public static bool SideCharacters_Start_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad)
                return false;
            return true;
        }

        [HarmonyPatch(typeof(SideCharacters), "Awake")]
        [HarmonyPrefix]
        public static bool SideCharacters_Awake_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad)
                return false;
            return true;
        }

        [HarmonyPatch(typeof(GameManager), "ChangeScene")]
        [HarmonyPrefix]
        public static bool GameManager_ChangeScene_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad)
                return false;
            return true;
        }

        // ── Nuclear guard: block ALL direct SceneManager.LoadScene calls during suppression ──
        // Components in the Combat scene may bypass SceneStatic/GameManager and call
        // SceneManager.LoadScene directly, which would unload the active scene (blank screen).
        // We allow our own additive load (LoadSceneMode.Additive) but block everything else.

        [HarmonyPatch(typeof(SceneManager), "LoadScene", typeof(string))]
        [HarmonyPrefix]
        public static bool SceneManager_LoadSceneStr_Prefix(string sceneName)
        {
            if (ZoneEditingService.SuppressSceneLoad)
            {
                Plugin.Log.LogInfo($"[Patches] Blocked LoadScene('{sceneName}') during suppression.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(SceneManager), "LoadScene", typeof(string), typeof(LoadSceneMode))]
        [HarmonyPrefix]
        public static bool SceneManager_LoadSceneMode_Prefix(string sceneName, LoadSceneMode mode)
        {
            if (ZoneEditingService.SuppressSceneLoad && mode != LoadSceneMode.Additive)
            {
                Plugin.Log.LogInfo($"[Patches] Blocked non-additive LoadScene('{sceneName}') during suppression.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(SceneManager), "LoadScene", typeof(int))]
        [HarmonyPrefix]
        public static bool SceneManager_LoadSceneInt_Prefix(int sceneBuildIndex)
        {
            if (ZoneEditingService.SuppressSceneLoad)
            {
                Plugin.Log.LogInfo($"[Patches] Blocked LoadScene(index={sceneBuildIndex}) during suppression.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// When the map scene loads normally, cache all base-game zone node positions
        /// and road waypoints so the editor can synthesize ZoneDefs for zone patches
        /// even after leaving the map scene.
        /// </summary>
        [HarmonyPatch(typeof(MapManager), "Start")]
        [HarmonyPostfix]
        public static void MapManager_Start_Postfix()
        {
            if (!ZoneEditingService.SuppressSceneLoad)
                ZoneEditingService.CacheAllBaseZones();
        }

        /// <summary>
        /// Prevent any scene redirects from firing during our additive scene load.
        /// Without this, other MonoBehaviours in the Map scene could trigger
        /// SceneStatic.LoadByName and cause unwanted scene switches.
        /// </summary>
        [HarmonyPatch(typeof(SceneStatic), "LoadByName")]
        [HarmonyPrefix]
        public static bool SceneStatic_LoadByName_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed SceneStatic.LoadByName during zone data extraction.");
                return false;
            }
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  EDIT MODE — block game interaction when MapEditor is active
        // ═══════════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(Node), "OnMouseUp")]
        [HarmonyPrefix]
        public static bool OnMouseUp_BlockPrefix()
        {
            return !ModEditor.IsEditing; // skip original when editing
        }

        [HarmonyPatch(typeof(Node), "OnMouseEnter")]
        [HarmonyPrefix]
        public static bool OnMouseEnter_BlockPrefix()
        {
            return !ModEditor.IsEditing;
        }

        [HarmonyPatch(typeof(Node), "OnMouseExit")]
        [HarmonyPrefix]
        public static bool OnMouseExit_BlockPrefix()
        {
            return !ModEditor.IsEditing;
        }

        [HarmonyPatch(typeof(Node), "OnMouseOver")]
        [HarmonyPrefix]
        public static bool OnMouseOver_BlockPrefix()
        {
            return !ModEditor.IsEditing;
        }

        // ═══════════════════════════════════════════════════════════════
        //  BOSS PATCHES
        // ═══════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE OVERRIDES — apply bone transforms after NPC model init
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// After NPCItem.Init() instantiates the animated model, attach the
        /// NpcSpriteOverride component which applies bone overrides in LateUpdate
        /// (so they persist through animations).
        /// </summary>
        [HarmonyPatch(typeof(NPCItem), "Init")]
        [HarmonyPostfix]
        public static void NPCItemInit_Postfix(NPCItem __instance)
        {
            try
            {
                if (__instance?.NPC == null)
                {
                    Plugin.Log.LogDebug($"[Patches] SpriteOverride skip: NPC={__instance?.NPC != null}");
                    return;
                }

                string npcId = __instance.NPC.GameName;

                // Find which modded zone owns this NPC (works across multiple loaded mods)
                string baseId = ModRegistry.StripVariantSuffix(npcId);
                ZoneDef zone = ModRegistry.FindZoneForNpc(npcId);
                if (zone == null)
                {
                    Plugin.Log.LogDebug($"[Patches] SpriteOverride: no mod zone owns NPC '{npcId}'");
                    return;
                }

                // Resolve sprite definition: NPC → NpcDef.SpriteSource → Sprites dict
                SpriteOverrideDef overrideDef = null;
                if (zone.Npcs.TryGetValue(baseId, out var npcDef))
                    overrideDef = ModRegistry.ResolveSpriteDefForNpc(zone, npcDef);

                if (overrideDef == null)
                {
                    Plugin.Log.LogDebug($"[Patches] SpriteOverride: no sprite def for '{npcId}' in zone '{zone.ZoneId}' (sprites: {string.Join(", ", zone.Sprites.Keys)})");
                    return;
                }

                // Skip if no per-frame overrides exist (prefab-only changes
                // like removed bones, tint, shader, offset are handled by NpcPrefabBuilder)
                if (overrideDef.Bones.Count == 0 && overrideDef.CustomSprites.Count == 0 &&
                    overrideDef.ScaleMultiplier == 1f &&
                    !overrideDef.FlipX && !overrideDef.FlipY &&
                    (overrideDef.AnimOverrides == null || overrideDef.AnimOverrides.Count == 0))
                    return;

                // Find the animated model root — use NPCItem.animatedTransform
                // which is set during Init() after the GameObjectAnimated prefab
                // is instantiated. GetComponentInChildren<Animator>() is unreliable
                // because it can hit UI elements (e.g. EmoteCharacterPing) first.
                Transform animRoot = __instance.animatedTransform;

                if (animRoot == null)
                {
                    Plugin.Log.LogWarning($"[Patches] SpriteOverride: animatedTransform is null on '{npcId}' — NPC may not have an animated model");
                    return;
                }

                Plugin.Log.LogInfo($"[Patches] SpriteOverride: attaching NpcSpriteOverride to '{npcId}' " +
                    $"(bones={overrideDef.Bones.Count}, scale={overrideDef.ScaleMultiplier}, " +
                    $"shader={overrideDef.UseShaderEffects}, animRoot='{animRoot.name}')");

                // Attach NpcSpriteOverride component for LateUpdate-based override persistence
                var ovrComponent = animRoot.gameObject.AddComponent<NpcSpriteOverride>();
                ovrComponent.Init(overrideDef);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[Patches] SpriteOverride failed: {ex}");
            }
        }
    }
}
