using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod
{
    /// <summary>
    /// Character override patches — attach CharacterOverrideDriver after NPC/Hero model init.
    /// </summary>
    public static partial class Patches
    {
        /// <summary>
        /// Register graft puppet SpriteRenderers with the host CharacterItem so
        /// game effects (hover outline, darken, stealth/taunt materials) include them.
        /// </summary>
        private static void RegisterPuppetSpritesWithHost(CharacterItem host, CharacterOverrideDriver driver)
        {
            var puppetSprites = driver.GetPuppetVisibleSprites();
            if (puppetSprites.Count == 0) return;

            // Access animatedSprites (internal) and animatedSpritesDefaultMaterial (private) via Harmony
            var animSprites = Traverse.Create(host).Field<List<SpriteRenderer>>("animatedSprites").Value;
            var matDict = Traverse.Create(host).Field<Dictionary<string, Material>>("animatedSpritesDefaultMaterial").Value;
            if (animSprites == null || matDict == null) return;

            foreach (var sr in puppetSprites)
            {
                if (sr == null) continue;
                // Guard against double-registration (e.g. game re-calls GetSpritesFromAnimated)
                if (animSprites.Contains(sr)) continue;
                animSprites.Add(sr);
                // Use a unique key to avoid name collisions with host bones that share
                // the same name (game's SetStealth/SetTaunt/SetParalyze restore materials
                // via animatedSpritesDefaultMaterial[sr.name], so collisions cause wrong material).
                string matKey = $"puppet_{sr.GetInstanceID()}";
                sr.name = matKey;
                if (!matDict.ContainsKey(matKey))
                    matDict.Add(matKey, sr.sharedMaterial);
            }

            Plugin.Log.LogInfo($"[Patches] Registered {puppetSprites.Count} puppet sprites with host '{host.name}'");
        }

        /// <summary>
        /// Remove stale puppet sprites from the host's animatedSprites/material dict,
        /// then destroy the old driver. Must be called BEFORE destroying GraftPuppet GOs.
        /// </summary>
        private static void CleanupOldOverride(CharacterOverrideDriver oldDriver, CharacterItem host)
        {
            // Collect all SpriteRenderers that the old driver registered as puppets
            var staleSprites = new HashSet<SpriteRenderer>(oldDriver.GetPuppetVisibleSprites());
            if (staleSprites.Count > 0)
            {
                var animSprites = Traverse.Create(host).Field<List<SpriteRenderer>>("animatedSprites").Value;
                var matDict = Traverse.Create(host).Field<Dictionary<string, Material>>("animatedSpritesDefaultMaterial").Value;
                if (animSprites != null)
                    animSprites.RemoveAll(sr => sr == null || staleSprites.Contains(sr));
                if (matDict != null)
                {
                    foreach (var sr in staleSprites)
                    {
                        if (sr != null) matDict.Remove(sr.name);
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(oldDriver);
        }

        // ═══════════════════════════════════════════════════════════════
        //  NPC SPRITE OVERRIDES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// After NPCItem.Init() instantiates the animated model, attach the
        /// CharacterOverrideDriver component which applies bone overrides in LateUpdate
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
                    Plugin.Log.LogDebug($"[Patches] CharacterOverride skip: NPC={__instance?.NPC != null}");
                    return;
                }

                string npcId = __instance.NPC.GameName;

                // Find which modded zone owns this NPC (works across multiple loaded mods)
                string baseId = ModRegistry.StripVariantSuffix(npcId);

                // Resolve sprite definition: try GlobalNpcs → SpriteSkinId → GlobalSpriteSkins first
                CharacterOverrideDef overrideDef = null;
                NpcDef npcDef = null;

                // Direct path: global NPC defs → SpriteSkinId
                // Try exact npcId first so variant-specific defs win over base defs.
                if (ModRegistry.GlobalNpcs.TryGetValue(npcId, out npcDef) ||
                    ModRegistry.GlobalNpcs.TryGetValue(baseId, out npcDef))
                {
                    overrideDef = ModRegistry.ResolveOverrideForNpc(npcDef);
                }

                if (overrideDef == null)
                {
                    Plugin.Log.LogDebug($"[Patches] CharacterOverride: no override def for '{npcId}'");
                    return;
                }

                // Skip if no per-frame overrides exist.
                // Note: RemovedBones requires per-frame enforcement because the Animator
                // can re-enable SpriteRenderers via animation curves each frame.
                if (overrideDef.BoneOverrides.Count == 0 && overrideDef.CustomSprites.Count == 0 &&
                    overrideDef.RemovedBones.Count == 0 && overrideDef.Grafts.Count == 0 &&
                    overrideDef.Model.IsDefault() &&
                    (overrideDef.AnimOverrides == null || overrideDef.AnimOverrides.Count == 0))
                    return;

                // Find the animated model root — use NPCItem.animatedTransform
                // which is set during Init() after the GameObjectAnimated prefab
                // is instantiated. GetComponentInChildren<Animator>() is unreliable
                // because it can hit UI elements (e.g. EmoteCharacterPing) first.
                Transform animRoot = __instance.animatedTransform;

                if (animRoot == null)
                {
                    Plugin.Log.LogWarning($"[Patches] CharacterOverride: animatedTransform is null on '{npcId}' — NPC may not have an animated model");
                    return;
                }

                Plugin.Log.LogInfo($"[Patches] CharacterOverride: attaching driver to '{npcId}' " +
                    $"(bones={overrideDef.BoneOverrides.Count}, grafts={overrideDef.Grafts.Count}, " +
                    $"animRoot='{animRoot.name}')");

                // The custom prefab is built with SetActive(false) to hide the
                // DontDestroyOnLoad template from the scene. Instantiate clones inherit
                // inactive state, so activate the model here before attaching components.
                if (!animRoot.gameObject.activeSelf)
                    animRoot.gameObject.SetActive(true);

                // Guard against double-init: destroy existing driver + puppets
                var existing = animRoot.gameObject.GetComponent<CharacterOverrideDriver>();
                if (existing != null)
                {
                    Plugin.Log.LogDebug($"[Patches] CharacterOverride: destroying previous driver on '{animRoot.name}'");
                    CleanupOldOverride(existing, __instance);
                }
                foreach (var oldPuppet in animRoot.GetComponentsInChildren<GraftPuppet>(true))
                    UnityEngine.Object.DestroyImmediate(oldPuppet.gameObject);

                // Attach CharacterOverrideDriver for LateUpdate-based override persistence
                var ovrComponent = animRoot.gameObject.AddComponent<CharacterOverrideDriver>();
                ovrComponent.Init(overrideDef);

                // Register puppet sprites with host CharacterItem for game effects
                RegisterPuppetSpritesWithHost(__instance, ovrComponent);

                // Defer collider recalc to first LateUpdate when all bone positions are finalized
                ovrComponent.ScheduleColliderRecalc(__instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[Patches] CharacterOverride NPC failed: {ex}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HERO SPRITE OVERRIDES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// After HeroItem.Init() instantiates the animated model, attach the
        /// CharacterOverrideDriver component if the hero's active skin has a sprite
        /// override registered. Uses the same generic component as NPCs since
        /// both share the same CharacterItem/SpriteSkin/Animator infrastructure.
        /// </summary>
        [HarmonyPatch(typeof(HeroItem), "Init")]
        [HarmonyPostfix]
        public static void HeroItemInit_Postfix(HeroItem __instance, Hero _hero)
        {
            try
            {
                if (_hero == null)
                {
                    Plugin.Log.LogDebug("[Patches] HeroCharacterOverride skip: hero is null");
                    return;
                }

                // Get the active skin ID from the Hero's Character base class
                string skinId = _hero.SkinUsed;
                if (string.IsNullOrEmpty(skinId))
                {
                    Plugin.Log.LogDebug($"[Patches] HeroCharacterOverride: no skin set for hero '{_hero.SubclassName}'");
                    return;
                }

                // Look up the sprite override def for this skin
                CharacterOverrideDef overrideDef = ModRegistry.ResolveOverrideForSkin(skinId);
                if (overrideDef == null)
                {
                    Plugin.Log.LogDebug($"[Patches] HeroCharacterOverride: no override def for skin '{skinId}'");
                    return;
                }

                // Skip if no per-frame overrides exist.
                // Note: RemovedBones requires per-frame enforcement because the Animator
                // can re-enable SpriteRenderers via animation curves each frame.
                if (overrideDef.BoneOverrides.Count == 0 && overrideDef.CustomSprites.Count == 0 &&
                    overrideDef.RemovedBones.Count == 0 && overrideDef.Grafts.Count == 0 &&
                    overrideDef.Model.IsDefault() &&
                    (overrideDef.AnimOverrides == null || overrideDef.AnimOverrides.Count == 0))
                    return;

                // Find the animated model root
                Transform animRoot = __instance.animatedTransform;
                if (animRoot == null)
                {
                    Plugin.Log.LogWarning($"[Patches] HeroCharacterOverride: animatedTransform is null on hero '{_hero.SubclassName}' skin '{skinId}'");
                    return;
                }

                Plugin.Log.LogInfo($"[Patches] HeroCharacterOverride: attaching to hero '{_hero.SubclassName}' " +
                    $"skin='{skinId}' (bones={overrideDef.BoneOverrides.Count}, grafts={overrideDef.Grafts.Count}, " +
                    $"animRoot='{animRoot.name}')");

                // Activate if cloned from an inactive custom prefab template
                if (!animRoot.gameObject.activeSelf)
                    animRoot.gameObject.SetActive(true);

                // Guard against double-init: destroy existing driver + puppets
                var existing = animRoot.gameObject.GetComponent<CharacterOverrideDriver>();
                if (existing != null)
                {
                    Plugin.Log.LogDebug($"[Patches] HeroCharacterOverride: destroying previous driver on '{animRoot.name}'");
                    CleanupOldOverride(existing, __instance);
                }
                foreach (var oldPuppet in animRoot.GetComponentsInChildren<GraftPuppet>(true))
                    UnityEngine.Object.DestroyImmediate(oldPuppet.gameObject);

                // Attach CharacterOverrideDriver for LateUpdate-based override persistence
                var ovrComponent = animRoot.gameObject.AddComponent<CharacterOverrideDriver>();
                ovrComponent.Init(overrideDef);

                // Register puppet sprites with host CharacterItem for game effects
                RegisterPuppetSpritesWithHost(__instance, ovrComponent);

                // Defer collider recalc to first LateUpdate when all bone positions are finalized
                ovrComponent.ScheduleColliderRecalc(__instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[Patches] HeroCharacterOverride failed: {ex}");
            }
        }
    }
}
