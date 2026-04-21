using System;
using HarmonyLib;
using UnityEngine;

namespace UnknownMod
{
    public static partial class Patches
    {
        /// <summary>
        /// Bump all character HUD elements (energy, HP bar, block/shield/doom, buffs,
        /// deck/discard counters) to a fixed high sort order on the Characters layer
        /// so custom background layers can sit between body sprites and the HUD.
        /// Also bumps initiative portraits, SpriteMask ranges, and non-CharacterItem
        /// combat HUD.
        ///
        /// Uses per-renderer while-loop: adds HudBase until order >= HudBase.
        /// Idempotent and handles deeply negative prefab values.
        /// </summary>
        private const int HudBase = 20000;

        [HarmonyPatch(typeof(CharacterItem), "DrawOrderSprites")]
        [HarmonyPostfix]
        static void DrawOrderSprites_Postfix(CharacterItem __instance)
        {
            BumpIfNotNull(__instance.healthBar);
            // energyT points to a single EnergyPoint inside Energy — use parent for the whole container.
            if (__instance.energyT != null)
                BumpAllRenderers(__instance.energyT.parent);
            BumpIfNotNull(__instance.heroDecks);
            BumpIfNotNull(__instance.skull);
            BumpIfNotNull(__instance.thornsTransform);
            BumpIfNotNull(__instance.tauntTextTransform);
            BumpIfNotNull(__instance.activeMarkTR);
            BumpIfNotNull(__instance.keyTransform);
            if (__instance.GO_Taunt != null)
                BumpAllRenderers(__instance.GO_Taunt.transform);

            // Fix buff icon ordering: Background < Shadow < Buff icon
            if (__instance.GO_Buffs != null)
            {
                foreach (var buff in __instance.GO_Buffs.GetComponentsInChildren<Buff>(true))
                {
                    var buffSRs = buff.GetComponentsInChildren<SpriteRenderer>(true);
                    if (buffSRs.Length == 0) continue;

                    int minOrder = int.MaxValue;
                    foreach (var sr in buffSRs)
                        if (sr.sortingOrder < minOrder) minOrder = sr.sortingOrder;

                    foreach (var sr in buffSRs)
                    {
                        if (sr.gameObject.name == "Background")
                            sr.sortingOrder = minOrder;
                        else if (sr.gameObject.name == "Shadow")
                            sr.sortingOrder = minOrder + 1;
                        else
                            sr.sortingOrder = minOrder + 2;
                    }
                }
            }
        }

        /// <summary>
        /// Bump initiative portrait child renderers after the game sets them.
        /// </summary>
        [HarmonyPatch(typeof(InitiativePortrait), "SortingOrder")]
        [HarmonyPostfix]
        static void InitiativePortrait_SortingOrder_Postfix(InitiativePortrait __instance)
        {
            // Bump root renderer (SR order 5 on the portrait itself)
            var rootR = __instance.GetComponent<Renderer>();
            if (rootR != null) BumpRenderer(rootR);

            var renderers = Traverse.Create(__instance).Field<Renderer[]>("childRenderers").Value;
            if (renderers == null) return;
            foreach (var r in renderers)
                BumpRenderer(r);
        }

        /// <summary>
        /// Bump non-CharacterItem combat HUD (EnergyCounter, BotNextTurn, Round,
        /// CombatTarget, InitiativeRoundSeparator) when character sorting refreshes.
        /// Uses direct field refs instead of GameObject.Find (which skips inactive objects).
        /// </summary>
        [HarmonyPatch(typeof(MatchManager), "SortCharacterSprites")]
        [HarmonyPostfix]
        static void SortCharacterSprites_Postfix(MatchManager __instance)
        {
            // Energy counter — parent of energyCounterBg holds the whole group
            if (__instance.energyCounterBg != null)
                BumpAllRenderers(__instance.energyCounterBg.transform.parent);

            // End turn / bot next turn button
            if (__instance.botEndTurn != null)
                BumpAllRenderers(__instance.botEndTurn);

            // Round tracker (ThermoRound) and deck counter — move above Characters layer
            if (__instance.roundTransform != null)
                MoveAboveCharactersAndBump(__instance.roundTransform);
            if (__instance.deckCounter != null)
                MoveAboveCharactersAndBump(__instance.deckCounter);

            // Combat target elements
            if (__instance.combatTarget != null && __instance.combatTarget.elements != null)
                BumpAllRenderers(__instance.combatTarget.elements);

            // Scene objects reachable via GameObject.Find (active during combat)
            BumpNamedObject("World/Round");
            BumpNamedObject("World/DeckBackground");
            BumpNamedObject("Options/Elements/Cards");
            BumpNamedObject("TraitInfo");

            // Initiative bar — portraits, round separators, and hover effects
            var initiative = GameObject.Find("GOs/Initiative");
            if (initiative != null)
                BumpAllRenderers(initiative.transform);
        }

        private static void BumpNamedObject(string path)
        {
            var go = GameObject.Find(path);
            if (go != null) BumpAllRenderers(go.transform);
        }

        /// <summary>
        /// Move renderers to a layer above Characters (Cards) so they render above
        /// the scanline/tv overlay, then bump sort orders above HudBase.
        /// </summary>
        private static void MoveAboveCharactersAndBump(Transform parent)
        {
            int cardsLayerID = SortingLayer.NameToID("Cards");
            foreach (var sr in parent.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr != null)
                {
                    sr.sortingLayerID = cardsLayerID;
                    while (sr.sortingOrder < HudBase)
                        sr.sortingOrder += HudBase;
                }
            }
            foreach (var mr in parent.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null)
                {
                    mr.sortingLayerID = cardsLayerID;
                    while (mr.sortingOrder < HudBase)
                        mr.sortingOrder += HudBase;
                }
            }
        }

        private static void BumpIfNotNull(Transform t)
        {
            if (t != null) BumpAllRenderers(t);
        }

        /// <summary>
        /// Bump all Characters-layer renderers under parent above HudBase.
        /// Uses per-renderer while-loop: adds HudBase until order >= HudBase.
        /// This is idempotent and handles any starting value (even deeply negative).
        /// Also bumps SpriteMask custom ranges so masked sprites (hp_red, hp_armor_shader)
        /// remain within their mask's range after bumping.
        /// </summary>
        private static void BumpAllRenderers(Transform parent)
        {
            foreach (var sr in parent.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr != null && sr.sortingLayerName == "Characters")
                    while (sr.sortingOrder < HudBase)
                        sr.sortingOrder += HudBase;
            }
            foreach (var mr in parent.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null && mr.sortingLayerName == "Characters")
                    while (mr.sortingOrder < HudBase)
                        mr.sortingOrder += HudBase;
            }
            foreach (var mask in parent.GetComponentsInChildren<SpriteMask>(true))
            {
                if (mask != null && mask.isCustomRangeActive && mask.frontSortingOrder < HudBase)
                {
                    mask.frontSortingOrder += HudBase;
                    mask.backSortingOrder += HudBase;
                }
            }
        }

        /// <summary>
        /// Bump a single renderer. Uses double-add for deeply negative values.
        /// </summary>
        private static void BumpRenderer(Renderer r)
        {
            if (r == null || r.sortingLayerName != "Characters") return;
            while (r.sortingOrder < HudBase)
                r.sortingOrder += HudBase;
        }
    }
}
