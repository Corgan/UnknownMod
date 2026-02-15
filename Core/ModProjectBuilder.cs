using System;
using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Converts mod DTOs into game ScriptableObjects and registers them in Globals.Instance.
    /// Called after <see cref="ModProjectLoader"/> has populated a <see cref="ModProject"/>.
    /// </summary>
    public static partial class ModProjectBuilder
    {
        // ═══════════════════════════════════════════════════════════════
        //  GENERIC BUILD HELPER
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Iterate a dictionary of defs, create ScriptableObjects, and register them.
        /// Replaces all the identical per-type Build* wrappers.
        /// </summary>
        private static void BuildDict<TDef, TSO>(
            Dictionary<string, TDef> defs,
            Func<TDef, TSO> make,
            Action<TSO> register,
            string label,
            Action<TDef, TSO> afterBuild = null)
            where TDef : IModEntity
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var so = make(kvp.Value);
                    register(so);
                    afterBuild?.Invoke(kvp.Value, so);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build {label} '{kvp.Key}': {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  NAMED BUILD PIPELINE
        // ═══════════════════════════════════════════════════════════════

        private struct BuildStep
        {
            public string Name;
            public Action<ModProject> Execute;
        }

        /// <summary>
        /// Ordered build steps. AuraCurses first (referenced by cards/items/etc.),
        /// then the rest in dependency order.
        /// </summary>
        private static readonly BuildStep[] Pipeline =
        {
            new() { Name = "AuraCurses", Execute = p => {
                BuildDict(p.AuraCurses,      MakeAuraCurse, RegisterAuraCurse, "AuraCurse");
                BuildDict(p.AuraCursePatches, MakeAuraCurse, RegisterAuraCurse, "AuraCurse");
            }},
            new() { Name = "Cards", Execute = p => {
                BuildDict(p.Cards,       MakeFullCard, DataHelper.RegisterCard, "Card");
                BuildDict(p.CardPatches, MakeFullCard, DataHelper.RegisterCard, "Card");
            }},
            new() { Name = "Items", Execute = p => {
                BuildDict(p.Items,       DataHelper.MakeFullItem, DataHelper.RegisterItem, "Item",
                    (d, item) => DataHelper.RegisterCard(DataHelper.MakeItemCard(d, item)));
                BuildDict(p.ItemPatches, DataHelper.MakeFullItem, DataHelper.RegisterItem, "Item",
                    (d, item) => DataHelper.RegisterCard(DataHelper.MakeItemCard(d, item)));
            }},
            new() { Name = "Loot", Execute = p => {
                BuildDict(p.Loot,        DataHelper.MakeLoot, DataHelper.RegisterLoot, "Loot");
                BuildDict(p.LootPatches, DataHelper.MakeLoot, DataHelper.RegisterLoot, "Loot");
            }},
            new() { Name = "NPCs", Execute = p => {
                BuildDict(p.Npcs,       DataHelper.MakeFullNpc, DataHelper.RegisterNPC, "NPC");
                BuildDict(p.NpcPatches, DataHelper.MakeFullNpc, DataHelper.RegisterNPC, "NPC");
            }},
            new() { Name = "Heroes", Execute = p => {
                BuildDict(p.Heroes,      DataHelper.MakeFullHero, DataHelper.RegisterHero, "Hero");
                BuildDict(p.HeroPatches, DataHelper.MakeFullHero, DataHelper.RegisterHero, "Hero");
            }},
            new() { Name = "Traits", Execute = p => {
                BuildDict(p.Traits,      DataHelper.MakeTrait, DataHelper.RegisterTrait, "Trait");
                BuildDict(p.TraitPatches, DataHelper.MakeTrait, DataHelper.RegisterTrait, "Trait");
            }},
            new() { Name = "Skins", Execute = p => {
                BuildDict(p.Skins,       DataHelper.MakeSkin, DataHelper.RegisterSkin, "Skin");
                BuildDict(p.SkinPatches, DataHelper.MakeSkin, DataHelper.RegisterSkin, "Skin");
            }},
            new() { Name = "Perks", Execute = p => {
                BuildDict(p.Perks,       DataHelper.MakePerk, DataHelper.RegisterPerk, "Perk");
                BuildDict(p.PerkPatches, DataHelper.MakePerk, DataHelper.RegisterPerk, "Perk");
            }},
            new() { Name = "PerkNodes", Execute = p => {
                BuildDict(p.PerkNodes,       DataHelper.MakePerkNode, DataHelper.RegisterPerkNode, "PerkNode");
                BuildDict(p.PerkNodePatches, DataHelper.MakePerkNode, DataHelper.RegisterPerkNode, "PerkNode");
            }},
            new() { Name = "Requirements", Execute = p => {
                BuildDict(p.Requirements,       DataHelper.MakeRequirement, DataHelper.RegisterRequirement, "Requirement");
                BuildDict(p.RequirementPatches, DataHelper.MakeRequirement, DataHelper.RegisterRequirement, "Requirement");
            }},
            new() { Name = "Cardbacks", Execute = p => {
                BuildDict(p.Cardbacks,       DataHelper.MakeCardback, DataHelper.RegisterCardback, "Cardback");
                BuildDict(p.CardbackPatches, DataHelper.MakeCardback, DataHelper.RegisterCardback, "Cardback");
            }},
            new() { Name = "TierRewards", Execute = p => {
                BuildDict(p.TierRewards,       DataHelper.MakeTierReward, DataHelper.RegisterTierReward, "TierReward");
                BuildDict(p.TierRewardPatches, DataHelper.MakeTierReward, DataHelper.RegisterTierReward, "TierReward");
            }},
            new() { Name = "Packs", Execute = p => {
                BuildDict(p.Packs,       DataHelper.MakePack, DataHelper.RegisterPack, "Pack");
                BuildDict(p.PackPatches, DataHelper.MakePack, DataHelper.RegisterPack, "Pack");
            }},
            new() { Name = "CardPlayerPacks", Execute = p => {
                BuildDict(p.CardPlayerPacks,       DataHelper.MakeCardPlayerPack, DataHelper.RegisterCardPlayerPack, "CardPlayerPack");
                BuildDict(p.CardPlayerPackPatches, DataHelper.MakeCardPlayerPack, DataHelper.RegisterCardPlayerPack, "CardPlayerPack");
            }},
            new() { Name = "CardPlayerPairsPacks", Execute = p => {
                BuildDict(p.CardPlayerPairsPacks,       DataHelper.MakeCardPlayerPairsPack, DataHelper.RegisterCardPlayerPairsPack, "CardPlayerPairsPack");
                BuildDict(p.CardPlayerPairsPackPatches, DataHelper.MakeCardPlayerPairsPack, DataHelper.RegisterCardPlayerPairsPack, "CardPlayerPairsPack");
            }},
            new() { Name = "HeroData", Execute = p => {
                BuildDict(p.HeroDataEntries, DataHelper.MakeHeroData, DataHelper.RegisterHeroData, "HeroData");
                BuildDict(p.HeroDataPatches, DataHelper.MakeHeroData, DataHelper.RegisterHeroData, "HeroData");
            }},
        };

        // ═══════════════════════════════════════════════════════════════
        //  BUILD A SINGLE MOD
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build all new content + patches from a ModProject.
        /// New content is registered additively; patches replace existing entries.
        /// </summary>
        public static void Build(ModProject proj)
        {
            Plugin.Log.LogInfo($"[Builder] Building mod '{proj.ModId}'...");

            // ── Run the standard build pipeline ──────────────────
            foreach (var step in Pipeline)
            {
                try
                {
                    step.Execute(proj);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[Builder] Pipeline step '{step.Name}' failed: {ex.Message}");
                }
            }

            // ── Special handling: Sprites + NPC registry ─────────
            ModRegistry.RegisterModSprites(proj.Sprites, proj.SpritePatches);
            ModRegistry.RegisterModNpcs(proj.Npcs, proj.NpcPatches);

            // ── Special handling: Zones ───────────────────────────
            foreach (var zone in proj.Zones.Values)
            {
                BuildZone(zone);
                string zoneFolder = System.IO.Path.Combine(
                    ModProjectLoader.ModFolder(proj.ModId), "zones", zone.ZoneId);
                ModRegistry.RegisterModZone(zone, zoneFolder);
            }

            // ── Special handling: Zone patches ────────────────────
            foreach (var patch in proj.ZonePatches.Values)
                ApplyZonePatch(patch);

            Plugin.Log.LogInfo($"[Builder] Mod '{proj.ModId}' built successfully.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  BUILD ALL MODS (runtime load-order sequence)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build all mods in load-order sequence.
        /// Later mods override earlier ones' patches.
        /// </summary>
        public static void BuildAll(List<ModProject> mods)
        {
            // Clear all registries before full rebuild
            ModRegistry.ClearAll();
            Runtime.MapBuilder.ClearCache();

            Plugin.Log.LogInfo($"[Builder] Building {mods.Count} mod(s) in load order...");
            foreach (var mod in mods)
            {
                try
                {
                    Build(mod);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[Builder] Error building mod '{mod.ModId}': {ex.Message}");
                    Plugin.Log.LogError(ex.StackTrace);
                }
            }
            Plugin.Log.LogInfo("[Builder] All mods built.");
        }

    }
}
