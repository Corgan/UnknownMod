using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using BattleMatch;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod
{
    /// <summary>
    /// Graft Sprite patches — apply visual graphs after NPC/Hero/pet model init.
    /// </summary>
    public static partial class Patches
    {
        private static bool ApplyVisualGraph(CompiledCharacterVisualGraph graph, Transform root, CharacterItem item)
        {
            if (graph == null || root == null) return false;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            var rootAnimator = root.GetComponent<Animator>();
            return VisualGraphHost.GetOrAdd(root).Apply(graph, root, item, rootAnimator != null ? rootAnimator : item.Anim);
        }

        /// <summary>The game rewrites every sprite's sort order as (base - index) whenever it re-sorts a character; lay grafts back into the draw stack afterwards.</summary>
        [HarmonyPatch(typeof(CharacterItem), "DrawOrderSprites")]
        [HarmonyPostfix]
        public static void CharacterItemDrawOrderSprites_Postfix(CharacterItem __instance)
        {
            try { VisualGraphHost.RelayerFor(__instance); }
            catch (System.Exception ex) { Plugin.Log.LogError($"[Patches] Graft Sprite layering failed: {ex}"); }
        }

        // ═══════════════════════════════════════════════════════════════
        //  NPC GRAFT SPRITES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>After NPCItem.Init() instantiates the animated model, apply the NPC's Graft Sprite, if any.</summary>
        [HarmonyPatch(typeof(NPCItem), "Init")]
        [HarmonyPostfix]
        public static void NPCItemInit_Postfix(NPCItem __instance)
        {
            try
            {
                if (__instance?.NPC == null) return;

                string npcId = __instance.NPC.GameName;
                string baseId = ModRegistry.StripVariantSuffix(npcId);

                // Exact id first so variant-specific defs win over base defs.
                NpcDef npcDef;
                if (!ModRegistry.GlobalNpcs.TryGetValue(npcId, out npcDef) &&
                    !ModRegistry.GlobalNpcs.TryGetValue(baseId, out npcDef))
                    return;

                CompiledCharacterVisualGraph graph;
                if (!ModRegistry.TryResolveVisualGraphForNpc(npcDef, out graph)) return;
                if (__instance.animatedTransform == null) return;
                VisualGraphHost.GetOrAdd(__instance.animatedTransform).Apply(graph, __instance.animatedTransform, __instance, __instance.Anim,
                    new VisualGraphBinding { Kind = VisualGraphBindingKind.Npc, PrimaryId = npcId });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[Patches] Graft Sprite NPC failed: {ex}");
            }
        }
        // ═══════════════════════════════════════════════════════════════
        //  HERO GRAFT SPRITES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>After HeroItem.Init() instantiates the animated model, apply the active skin's Graft Sprite, if any.</summary>
        [HarmonyPatch(typeof(HeroItem), "Init")]
        [HarmonyPostfix]
        public static void HeroItemInit_Postfix(HeroItem __instance, Hero _hero)
        {
            try
            {
                string skinId = _hero?.SkinUsed;
                if (string.IsNullOrEmpty(skinId)) return;

                CompiledCharacterVisualGraph graph;
                if (!ModRegistry.TryResolveVisualGraphForSkin(skinId, out graph)) return;
                if (__instance.animatedTransform == null)
                {
                    Plugin.Log.LogWarning($"[Patches] Skin '{skinId}' has a Graft Sprite but its hero has no animated model. Its Host Model must be a hero skin (e.g. 'archer') or a monster with an animated model.");
                    return;
                }
                VisualGraphHost.GetOrAdd(__instance.animatedTransform).Apply(graph, __instance.animatedTransform, __instance, __instance.Anim,
                    new VisualGraphBinding { Kind = VisualGraphBindingKind.HeroSkin, PrimaryId = skinId });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[Patches] Graft Sprite hero failed: {ex}");
            }
        }
        /// <summary>
        /// The hero select popup instantiates the skin's raw model without HeroItem.Init, so the skin's visual graph (grafts, flip, tint)
        /// would be missing there. Apply it to the popup's preview model.
        /// </summary>
        [HarmonyPatch(typeof(CharPopup), "SetHeroAnimated")]
        [HarmonyPostfix]
        public static void CharPopupSetHeroAnimated_Postfix(CharPopup __instance, GameObject heroSkin)
        {
            try
            {
                var t = Traverse.Create(__instance);
                var model = t.Field("heroAnimated").GetValue<GameObject>();
                var scd = t.Field("SCD").GetValue<SubClassData>();
                if (model == null || scd == null || ModRegistry.SkinVisualGraphs.Count == 0) return;

                string active = PlayerManager.Instance.GetActiveSkin(scd.Id);
                string skinId = active;
                if (heroSkin != null)
                {
                    skinId = null;
                    foreach (var s in Globals.Instance.GetSkinsBySubclass(scd.Id))
                    {
                        if (s == null || s.SkinGo != heroSkin) continue;
                        if (string.Equals(s.SkinId, active, System.StringComparison.OrdinalIgnoreCase)) { skinId = s.SkinId; break; }
                        if (skinId == null || ModRegistry.SkinVisualGraphs.ContainsKey(s.SkinId ?? "")) skinId = s.SkinId;
                    }
                }
                if (string.IsNullOrEmpty(skinId)) return;
                if (!ModRegistry.TryResolveVisualGraphForSkin(skinId, out var graph)) return;

                var host = VisualGraphHost.GetOrAdd(model.transform);
                if (string.Equals(host.GraphId, graph.Id, System.StringComparison.OrdinalIgnoreCase)) return;
                host.ApplyStatic(graph, model.transform, model.GetComponent<Animator>(), 2000, SortingLayer.NameToID("UI"),
                    new VisualGraphBinding { Kind = VisualGraphBindingKind.HeroSkin, PrimaryId = skinId });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[Patches] Graft Sprite hero popup failed: {ex}");
            }
        }

        [HarmonyPatch(typeof(BattleTeamManager), "CreatePet")]
        [HarmonyPostfix]
        public static void BattleTeamManagerCreatePet_Postfix(
            Cards.CardRealtimeData petCard, GameObject charGO, Hero _hero, NPC _npc,
            bool _fromEnchant, string _enchantName)
        {
            try
            {
                if (petCard == null || charGO == null) return;
                CompiledCharacterVisualGraph graph;
                if (!ModRegistry.TryResolveVisualGraphForPet(petCard.Id, petCard.InternalId, out graph)) return;
                string childName = _fromEnchant ? "thePetEnchantment" + (_enchantName ?? "") : "thePet";
                Transform petRoot = charGO.transform.Find(childName);
                NPCItem petItem = petRoot == null ? null : petRoot.GetComponent<NPCItem>();
                if (petItem == null)
                {
                    Plugin.Log.LogWarning($"[Patches] Visual graph '{graph.Id}' pet '{petCard.Id}' child '{childName}' has no NPCItem.");
                    return;
                }
                VisualGraphHost.GetOrAdd(petRoot).Apply(graph, petRoot, petItem, petItem.Anim,
                    new VisualGraphBinding
                    {
                        Kind = _fromEnchant ? VisualGraphBindingKind.EnchantmentPet : VisualGraphBindingKind.Pet,
                        PrimaryId = petCard.Id,
                        SecondaryId = petCard.InternalId
                    });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[Patches] Visual graph pet attachment failed: {ex}");
            }
        }
    }
}
