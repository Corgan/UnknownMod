using HarmonyLib;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod
{
    /// <summary>
    /// Sprite override patches — attach NpcSpriteOverride after NPC model init.
    /// </summary>
    public static partial class Patches
    {
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
