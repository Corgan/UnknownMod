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
        //  BUILD A SINGLE MOD
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build all new content + patches from a ModProject.
        /// New content is registered additively; patches replace existing entries.
        /// </summary>
        public static void Build(ModProject proj)
        {
            Plugin.Log.LogInfo($"[Builder] Building mod '{proj.ModId}'...");

            // ── AuraCurse (build first — referenced by cards, items, etc.) ──
            BuildAuraCurses(proj.AuraCurses, isNew: true);
            BuildAuraCurses(proj.AuraCursePatches, isNew: false);

            // ── Cards ────────────────────────────────────────────────
            BuildCards(proj.Cards, isNew: true);
            BuildCards(proj.CardPatches, isNew: false);

            // ── Items ────────────────────────────────────────────────
            BuildItems(proj.Items);
            BuildItems(proj.ItemPatches);

            // ── Loot ─────────────────────────────────────────────────
            BuildLoot(proj.Loot);
            BuildLoot(proj.LootPatches);

            // ── NPCs ─────────────────────────────────────────────────
            BuildNpcs(proj.Npcs);
            BuildNpcs(proj.NpcPatches);

            // ── Sprites ──────────────────────────────────────────────
            // Register sprite + NPC defs in ModRegistry for runtime resolution.
            ModRegistry.RegisterModSprites(proj.Sprites, proj.SpritePatches);
            ModRegistry.RegisterModNpcs(proj.Npcs, proj.NpcPatches);

            // ── Zones ────────────────────────────────────────────────
            foreach (var zone in proj.Zones.Values)
            {
                BuildZone(zone);
                // Register in ModRegistry so IsModdedZone/MapBuilder/GetZoneFolder work
                string zoneFolder = System.IO.Path.Combine(
                    ModProjectLoader.ModFolder(proj.ModId), "zones", zone.ZoneId);
                ModRegistry.RegisterModZone(zone, zoneFolder);
            }

            // ── Zone patches ─────────────────────────────────────────
            foreach (var patch in proj.ZonePatches.Values)
                ApplyZonePatch(patch);

            // ── Heroes ────────────────────────────────────────────────
            BuildHeroes(proj.Heroes);
            BuildHeroes(proj.HeroPatches);

            // ── Traits ────────────────────────────────────────────────
            BuildTraits(proj.Traits);
            BuildTraits(proj.TraitPatches);

            // ── Skins ─────────────────────────────────────────────────
            BuildSkins(proj.Skins);
            BuildSkins(proj.SkinPatches);

            // ── Perks ─────────────────────────────────────────────────
            BuildPerks(proj.Perks);
            BuildPerks(proj.PerkPatches);

            // ── PerkNodes ─────────────────────────────────────────────
            BuildPerkNodes(proj.PerkNodes);
            BuildPerkNodes(proj.PerkNodePatches);

            // ── Requirements ─────────────────────────────────────────
            BuildRequirements(proj.Requirements);
            BuildRequirements(proj.RequirementPatches);

            // ── Cardbacks ──────────────────────────────────────────────
            BuildCardbacks(proj.Cardbacks);
            BuildCardbacks(proj.CardbackPatches);

            // ── TierRewards ───────────────────────────────────────────
            BuildTierRewards(proj.TierRewards);
            BuildTierRewards(proj.TierRewardPatches);

            // ── Packs ─────────────────────────────────────────────────
            BuildPacks(proj.Packs);
            BuildPacks(proj.PackPatches);

            // ── CardPlayerPacks ───────────────────────────────────────
            BuildCardPlayerPacks(proj.CardPlayerPacks);
            BuildCardPlayerPacks(proj.CardPlayerPackPatches);

            // ── CardPlayerPairsPacks ──────────────────────────────────
            BuildCardPlayerPairsPacks(proj.CardPlayerPairsPacks);
            BuildCardPlayerPairsPacks(proj.CardPlayerPairsPackPatches);

            // ── HeroData ──────────────────────────────────────────────
            BuildHeroDataEntries(proj.HeroDataEntries);
            BuildHeroDataEntries(proj.HeroDataPatches);

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

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Extract the ID string from an AuraCurseData reference (null-safe).</summary>
        private static string GetACId(AuraCurseData ac)
        {
            if (ac == null) return "";
            return Traverse.Create(ac).Field<string>("id").Value ?? "";
        }
    }
}
