using System;
using System.Collections.Generic;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class ModProjectBuilder
    {
        // ═══════════════════════════════════════════════════════════════
        //  THIN BUILD WRAPPERS (Loot, NPCs, Heroes, Traits, etc.)
        // ═══════════════════════════════════════════════════════════════

        private static void BuildLoot(Dictionary<string, LootDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var loot = DataHelper.MakeLoot(kvp.Value);
                    DataHelper.RegisterLoot(loot);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Loot '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildNpcs(Dictionary<string, NpcDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var npc = DataHelper.MakeFullNpc(kvp.Value);
                    DataHelper.RegisterNPC(npc);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build NPC '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildHeroes(Dictionary<string, HeroDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var sc = DataHelper.MakeFullHero(kvp.Value);
                    DataHelper.RegisterHero(sc);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Hero '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildTraits(Dictionary<string, TraitDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var trait = DataHelper.MakeTrait(kvp.Value);
                    DataHelper.RegisterTrait(trait);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Trait '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildSkins(Dictionary<string, SkinDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var skin = DataHelper.MakeSkin(kvp.Value);
                    DataHelper.RegisterSkin(skin);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Skin '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildPerks(Dictionary<string, PerkDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var perk = DataHelper.MakePerk(kvp.Value);
                    DataHelper.RegisterPerk(perk);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Perk '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildPerkNodes(Dictionary<string, PerkNodeDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var node = DataHelper.MakePerkNode(kvp.Value);
                    DataHelper.RegisterPerkNode(node);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build PerkNode '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildRequirements(Dictionary<string, RequirementDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var req = DataHelper.MakeRequirement(kvp.Value);
                    DataHelper.RegisterRequirement(req);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Requirement '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildCardbacks(Dictionary<string, CardbackDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var cb = DataHelper.MakeCardback(kvp.Value);
                    DataHelper.RegisterCardback(cb);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Cardback '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildTierRewards(Dictionary<string, TierRewardDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var tr = DataHelper.MakeTierReward(kvp.Value);
                    DataHelper.RegisterTierReward(tr);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build TierReward '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildPacks(Dictionary<string, PackDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var pack = DataHelper.MakePack(kvp.Value);
                    DataHelper.RegisterPack(pack);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Pack '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildCardPlayerPacks(Dictionary<string, CardPlayerPackDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var pack = DataHelper.MakeCardPlayerPack(kvp.Value);
                    DataHelper.RegisterCardPlayerPack(pack);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build CardPlayerPack '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildCardPlayerPairsPacks(Dictionary<string, CardPlayerPairsPackDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var pack = DataHelper.MakeCardPlayerPairsPack(kvp.Value);
                    DataHelper.RegisterCardPlayerPairsPack(pack);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build CardPlayerPairsPack '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildHeroDataEntries(Dictionary<string, HeroDataDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var hero = DataHelper.MakeHeroData(kvp.Value);
                    DataHelper.RegisterHeroData(hero);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build HeroData '{kvp.Key}': {ex.Message}");
                }
            }
        }
    }
}
