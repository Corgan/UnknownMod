using System;
using System.Collections.Generic;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Metadata about a mod entity type. Used by the editor base class
    /// and build pipeline to operate on any entity type uniformly.
    /// </summary>
    public class EntityTypeInfo
    {
        public string TypeLabel;
        public string FolderName;
        public Type DefType;

        /// <summary>Returns all base-game IDs for the override browser.</summary>
        public Func<List<string>> GetAllBaseIds;
    }

    /// <summary>
    /// Central registry mapping entity definition types to their metadata.
    /// Populated once at startup via <see cref="Initialize"/>.
    /// </summary>
    public static class EntityTypeRegistry
    {
        private static readonly Dictionary<Type, EntityTypeInfo> _registry = new();

        public static void Register(EntityTypeInfo info)
            => _registry[info.DefType] = info;

        public static EntityTypeInfo Get<TDef>() where TDef : IModEntity
            => _registry.TryGetValue(typeof(TDef), out var info) ? info : null;

        public static EntityTypeInfo Get(Type type)
            => _registry.TryGetValue(type, out var info) ? info : null;

        public static IEnumerable<EntityTypeInfo> All => _registry.Values;

        /// <summary>
        /// Register all entity types. Call once at plugin startup.
        /// </summary>
        public static void Initialize()
        {
            Register(new EntityTypeInfo { DefType = typeof(AuraCurseDef),            TypeLabel = "AuraCurse",            FolderName = "auracurse",            GetAllBaseIds = DataHelper.GetAllAuraCurseIds });
            Register(new EntityTypeInfo { DefType = typeof(CardDef),                 TypeLabel = "Card",                 FolderName = "cards",                GetAllBaseIds = DataHelper.GetAllCardIds });
            Register(new EntityTypeInfo { DefType = typeof(LootDef),                 TypeLabel = "Loot",                 FolderName = "loot",                 GetAllBaseIds = DataHelper.GetAllLootIds });
            Register(new EntityTypeInfo { DefType = typeof(NpcDef),                  TypeLabel = "NPC",                  FolderName = "npcs",                 GetAllBaseIds = DataHelper.GetAllNpcIds });
            Register(new EntityTypeInfo { DefType = typeof(HeroDef),                 TypeLabel = "Hero",                 FolderName = "heroes",               GetAllBaseIds = DataHelper.GetAllSubClassIds });
            Register(new EntityTypeInfo { DefType = typeof(TraitDef),                TypeLabel = "Trait",                FolderName = "traits",               GetAllBaseIds = DataHelper.GetAllTraitIds });
            Register(new EntityTypeInfo { DefType = typeof(SkinDef),                 TypeLabel = "Skin",                 FolderName = "skins",                GetAllBaseIds = DataHelper.GetAllSkinIds });
            Register(new EntityTypeInfo { DefType = typeof(PerkDef),                 TypeLabel = "Perk",                 FolderName = "perks",                GetAllBaseIds = DataHelper.GetAllPerkIds });
            Register(new EntityTypeInfo { DefType = typeof(PerkNodeDef),             TypeLabel = "PerkNode",             FolderName = "perknodes",            GetAllBaseIds = DataHelper.GetAllPerkNodeIds });
            Register(new EntityTypeInfo { DefType = typeof(RequirementDef),          TypeLabel = "Requirement",          FolderName = "requirements",         GetAllBaseIds = DataHelper.GetAllEventRequirementIds });
            Register(new EntityTypeInfo { DefType = typeof(CardbackDef),             TypeLabel = "Cardback",             FolderName = "cardbacks",            GetAllBaseIds = DataHelper.GetAllCardbackIds });
            Register(new EntityTypeInfo { DefType = typeof(TierRewardDef),           TypeLabel = "TierReward",           FolderName = "tierrewards",          GetAllBaseIds = () => DataHelper.GetAllTierRewardTiers().ConvertAll(t => t.ToString()) });
            Register(new EntityTypeInfo { DefType = typeof(PackDef),                 TypeLabel = "Pack",                 FolderName = "packs",                GetAllBaseIds = DataHelper.GetAllPackIds });
            Register(new EntityTypeInfo { DefType = typeof(CardPlayerPackDef),       TypeLabel = "CardPlayerPack",       FolderName = "cardplayerpacks",      GetAllBaseIds = DataHelper.GetAllCardPlayerPackIds });
            Register(new EntityTypeInfo { DefType = typeof(CardPlayerPairsPackDef),  TypeLabel = "CardPlayerPairsPack",  FolderName = "cardplayerpairspacks", GetAllBaseIds = DataHelper.GetAllCardPlayerPairsPackIds });
            Register(new EntityTypeInfo { DefType = typeof(HeroDataDef),             TypeLabel = "HeroData",             FolderName = "herodata",             GetAllBaseIds = DataHelper.GetAllHeroDataIds });
            Register(new EntityTypeInfo { DefType = typeof(CharacterOverrideDef),       TypeLabel = "SpriteSkin",           FolderName = "spriteskins",          GetAllBaseIds = null }); // no base-game IDs
        }
    }
}
