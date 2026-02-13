using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ═══════════════════════════════════════════════════════════════
    //  Mod-scope DTO classes for data types that live outside zones.
    //  These are JSON-serialized and stored in the mod folder.
    //  Reference types (AuraCurseData, CardData, etc.) are stored
    //  as string IDs and resolved at build time.
    // ═══════════════════════════════════════════════════════════════

    // ───────────────────────────────────────────────────────────────
    //  AURA / CURSE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class AuraCurseDef
    {
        public string Id = "";
        public string ACName = "";
        public bool IsAura = true;
        public string Description = "";
        public int MaxCharges = -1;
        public int MaxMadnessCharges = -1;
        public int AuraConsumed = 1;
        public int ChargesMultiplierDescription = 0;
        public float ChargesAuxNeedForOne1 = 0f;
        public int ChargesAuxNeedForOne2 = 0;
        public string Sprite = "";           // sprite name/path
        public string EffectTick = "";
        public string EffectTickSides = "";
        public string Sound = "";            // audio clip name
        public string SoundRework = "";

        // Config
        public bool Removable = true;
        public bool GainCharges = true;
        public bool IconShow = true;
        public bool CombatlogShow = true;
        public bool Preventable = true;
        public bool CanBeAddedToImmunityDespiteNotBeingPreventable = false;

        // Expiration
        public int PriorityOnConsumption = 0;
        public bool ConsumeAll = false;
        public bool ConsumedAtCast = false;
        public bool ConsumedAtTurnBegin = false;
        public bool ConsumedAtTurn = false;
        public bool ConsumedAtRoundBegin = false;
        public bool ConsumedAtRound = false;
        public bool ProduceDamageWhenConsumed = false;
        public bool ProduceHealWhenConsumed = false;
        public bool DieWhenConsumedAll = false;

        // Aura Damage Bonus (4 slots)
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType AuraDamageType = Enums.DamageType.None;
        public string AuraDamageChargesBasedOnACCharges = "";  // AC ID
        public int AuraDamageIncreasedTotal = 0;
        public float AuraDamageIncreasedPerStack = 0f;
        public int AuraDamageIncreasedPercent = 0;
        public float AuraDamageIncreasedPercentPerStack = 0f;
        public float AuraDamageIncreasedPercentPerStackPerEnergy = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType AuraDamageType2 = Enums.DamageType.None;
        public int AuraDamageIncreasedTotal2 = 0;
        public float AuraDamageIncreasedPerStack2 = 0f;
        public int AuraDamageIncreasedPercent2 = 0;
        public float AuraDamageIncreasedPercentPerStack2 = 0f;
        public float AuraDamageIncreasedPercentPerStackPerEnergy2 = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType AuraDamageType3 = Enums.DamageType.None;
        public int AuraDamageIncreasedTotal3 = 0;
        public float AuraDamageIncreasedPerStack3 = 0f;
        public int AuraDamageIncreasedPercent3 = 0;
        public float AuraDamageIncreasedPercentPerStack3 = 0f;
        public float AuraDamageIncreasedPercentPerStackPerEnergy3 = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType AuraDamageType4 = Enums.DamageType.None;
        public int AuraDamageIncreasedTotal4 = 0;
        public float AuraDamageIncreasedPerStack4 = 0f;
        public int AuraDamageIncreasedPercent4 = 0;
        public float AuraDamageIncreasedPercentPerStack4 = 0f;
        public float AuraDamageIncreasedPercentPerStackPerEnergy4 = 0f;

        // Aura Heal Bonus
        public int HealDoneTotal = 0;
        public int HealDonePerStack = 0;
        public int HealDonePercent = 0;
        public int HealDonePercentPerStack = 0;
        public int HealDonePercentPerStackPerEnergy = 0;
        public int HealReceivedTotal = 0;
        public int HealReceivedPerStack = 0;
        public int HealReceivedPercent = 0;
        public int HealReceivedPercentPerStack = 0;

        // Draw
        public int CardsDrawPerStack = 0;

        // Damage Reflected
        public int ChargesPreReqForDamageReflection = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.RefectedDamageModifierType DamageReflectedModifierType = Enums.RefectedDamageModifierType.DamagePerAuraCharge;
        public int DamageReflectedMultiplier = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageReflectedType = Enums.DamageType.None;
        public int DamageReflectedConsumeCharges = 0;

        // Block
        public int BlockChargesGainedPerStack = 0;
        public bool NoRemoveBlockAtTurnEnd = false;
        public bool GrantBlockToTeamForAmountOfDamageBlocked = false;
        public int ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked = 0;

        // Prevention
        public int DamagePreventedPerStack = 0;
        public int CursePreventedPerStack = 0;
        public string PreventedAuraCurse = "";  // AC ID
        public int PreventedAuraCurseStackPerStack = 0;

        // Damage Received (2 slots)
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType IncreasedDamageReceivedType = Enums.DamageType.None;
        public int IncreasedDirectDamageChargesMultiplierNeededForOne = 0;
        public int IncreasedDirectDamageReceivedPerTurn = 0;
        public float IncreasedDirectDamageReceivedPerStack = 0f;
        public int IncreasedPercentDamageReceivedPerTurn = 0;
        public int IncreasedPercentDamageReceivedPerStack = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType IncreasedDamageReceivedType2 = Enums.DamageType.None;
        public int IncreasedDirectDamageChargesMultiplierNeededForOne2 = 0;
        public int IncreasedDirectDamageReceivedPerTurn2 = 0;
        public float IncreasedDirectDamageReceivedPerStack2 = 0f;
        public int IncreasedPercentDamageReceivedPerTurn2 = 0;
        public int IncreasedPercentDamageReceivedPerStack2 = 0;

        // Damage Prevented
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType PreventedDamageTypePerStack = Enums.DamageType.None;
        public int PreventedDamagePerStack = 0;

        // Heal Attacker
        public int HealAttackerPerStack = 0;
        public int HealAttackerConsumeCharges = 0;

        // Character Stat Modification
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CharacterStat CharacterStatModified = Enums.CharacterStat.None;
        public int CharacterStatChargesMultiplierNeededForOne = 0;
        public int CharacterStatModifiedValue = 0;
        public float CharacterStatModifiedValuePerStack = 0f;
        public bool CharacterStatAbsolute = false;
        public int CharacterStatAbsoluteValue = 0;
        public int CharacterStatAbsoluteValuePerStack = 0;

        // Resist Modification (3 slots)
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified = Enums.DamageType.None;
        public float ResistModifiedValue = 0f;
        public float ResistModifiedPercentagePerStack = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified2 = Enums.DamageType.None;
        public float ResistModifiedValue2 = 0f;
        public float ResistModifiedPercentagePerStack2 = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified3 = Enums.DamageType.None;
        public float ResistModifiedValue3 = 0f;
        public float ResistModifiedPercentagePerStack3 = 0f;

        // Explode
        public int ExplodeAtStacks = 0;
        public int HealTotalOnExplode = 0;
        public float HealPerChargeOnExplode = 0f;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.AuraCurseExplodeHealTarget HealTargetOnExplode = Enums.AuraCurseExplodeHealTarget.None;
        public string ACOnExplode = "";     // AC ID
        public int ACTotalChargesOnExplode = 0;
        public int ACChargesPerStackChargeOnExplode = 0;

        // Consume Damage
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageTypeWhenConsumed = Enums.DamageType.None;
        public string ConsumedDamageChargesBasedOnACCharges = "";  // AC ID
        public string ConsumeDamageChargesIfACApplied = "";        // AC ID
        public int DamageWhenConsumed = 0;
        public float DamageWhenConsumedPerCharge = 0f;
        public int DamageSidesWhenConsumed = 0;
        public int DamageSidesWhenConsumedPerCharge = 0;
        public int DoubleDamageIfCursesLessThan = 0;

        // Consume Heal
        public int HealWhenConsumed = 0;
        public float HealWhenConsumedPerCharge = 0f;
        public int HealSidesWhenConsumed = 0;
        public float HealSidesWhenConsumedPerCharge = 0f;

        // Remove/Gain AC on consumption
        public string RemoveAuraCurse = "";   // AC ID
        public string RemoveAuraCurse2 = "";  // AC ID
        public string GainAuraCurseConsumption = "";      // AC ID
        public int GainAuraCurseConsumptionPerCharge = 0;
        public string GainChargesFromThisAuraCurse = "";  // AC ID
        public string GainAuraCurseConsumption2 = "";     // AC ID
        public int GainAuraCurseConsumptionPerCharge2 = 0;
        public string GainChargesFromThisAuraCurse2 = ""; // AC ID

        // Reveal / Cost
        public int RevealCardsPerCharge = 0;
        public int ModifyCardCostPerChargeNeededForOne = 0;

        // Disabled Card Types
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Enums.CardType[] DisabledCardTypes = Array.Empty<Enums.CardType>();

        // Misc
        public bool Invulnerable = false;
        public bool Stealth = false;
        public bool Taunt = false;
        public bool SkipsNextTurn = false;

        // Charge bonuses (complex nested types — simplified for DTO)
        public List<AuraCurseChargesBonusDef> ACBonusData = new();
        public List<AuraDamageBonusDef> AuraDamageConditionalBonuses = new();
    }

    [Serializable]
    public class AuraCurseChargesBonusDef
    {
        public string AuraCurseId = "";          // AC ID reference
        public int ChargesBonus = 0;
    }

    [Serializable]
    public class AuraDamageBonusDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageType = Enums.DamageType.None;
        public int FlatBonus = 0;
        public float FlatBonusPerStack = 0f;
        public int PercentBonus = 0;
        public float PercentBonusPerStack = 0f;
    }

    // ───────────────────────────────────────────────────────────────
    //  HERO (SubClass)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class HeroCardDef
    {
        public string CardId = "";
        public int UnitsInDeck = 1;
    }

    [Serializable]
    public class HeroDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";                 // subClassName → Id via Init()
        public string SubClassName = "";       // display name (e.g. "Liam I")
        public string CharacterName = "";      // base hero name (e.g. "Liam")
        public string CharacterDescription = "";
        public string CharacterDescriptionStrength = "";
        public bool MainCharacter = false;
        public bool InitialUnlock = false;
        public string Sku = "";                // DLC requirement

        // ── Visual / Sprite ──────────────────────────────────────
        public string SpriteSource = "";       // base-game subclass ID to copy visuals from
        public float FluffOffsetX = 0f;
        public float FluffOffsetY = 0f;
        public bool Female = false;
        public float StickerOffsetX = 0f;

        // ── Class ────────────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClass = Enums.HeroClass.Warrior;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClassSecondary = Enums.HeroClass.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClassThird = Enums.HeroClass.None;

        // ── Stats ────────────────────────────────────────────────
        public int OrderInList = 0;
        public bool Blocked = true;
        public int Speed = 0;
        public int Hp = 0;
        public int Energy = 0;
        public int EnergyTurn = 0;

        // ── Resistances ──────────────────────────────────────────
        public int ResSlash = 0;
        public int ResBlunt = 0;
        public int ResPierce = 0;
        public int ResFire = 0;
        public int ResCold = 0;
        public int ResLight = 0;
        public int ResMind = 0;
        public int ResHoly = 0;
        public int ResShadow = 0;

        // ── Item ─────────────────────────────────────────────────
        public string ItemId = "";             // starting item (CardData ref)

        // ── Level HP ─────────────────────────────────────────────
        public List<int> MaxHp = new();

        // ── Starting Cards ───────────────────────────────────────
        public List<HeroCardDef> Cards = new();

        // ── Singularity Cards ────────────────────────────────────
        public List<string> CardsSingularity = new();  // CardData IDs

        // ── Trait Tree ───────────────────────────────────────────
        public string Trait0 = "";
        public string Trait1A = "";
        public string Trait1ACard = "";        // CardData ID
        public string Trait1B = "";
        public string Trait1BCard = "";
        public string Trait2A = "";
        public string Trait2B = "";
        public string Trait3A = "";
        public string Trait3ACard = "";
        public string Trait3B = "";
        public string Trait3BCard = "";
        public string Trait4A = "";
        public string Trait4B = "";

        // ── Challenge Packs ──────────────────────────────────────
        public string ChallengePack0 = "";
        public string ChallengePack1 = "";
        public string ChallengePack2 = "";
        public string ChallengePack3 = "";
        public string ChallengePack4 = "";
        public string ChallengePack5 = "";
        public string ChallengePack6 = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  TRAIT
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class TraitDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        public string TraitName = "";
        public string Description = "";

        // ── Activation ───────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EventActivation Activation = Enums.EventActivation.None;
        public bool ActivateOnRuneTypeAdded = false;
        public bool TryActivateOnEveryEvent = false;
        public int TimesPerTurn = 0;
        public int TimesPerRound = 0;

        // ── Cards ────────────────────────────────────────────────
        public string TraitCard = "";           // CardData ID
        public string TraitCardForAllHeroes = ""; // CardData ID

        // ── Character Stat Modification ──────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CharacterStat CharacterStatModified = Enums.CharacterStat.None;
        public int CharacterStatModifiedValue = 0;

        // ── Resist Modification (3 slots) ────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified1 = Enums.DamageType.None;
        public int ResistModifiedValue1 = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified2 = Enums.DamageType.None;
        public int ResistModifiedValue2 = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified3 = Enums.DamageType.None;
        public int ResistModifiedValue3 = 0;

        // ── AuraCurse Immunity (3 slots) ─────────────────────────
        public string AuracurseImmune1 = "";
        public string AuracurseImmune2 = "";
        public string AuracurseImmune3 = "";

        // ── AuraCurse Bonus (3 slots) ────────────────────────────
        public string AuracurseBonus1 = "";     // AuraCurseData ID
        public int AuracurseBonusValue1 = 0;
        public string AuracurseBonus2 = "";
        public int AuracurseBonusValue2 = 0;
        public string AuracurseBonus3 = "";
        public int AuracurseBonusValue3 = 0;

        // ── Heal Bonuses ─────────────────────────────────────────
        public int HealFlatBonus = 0;
        public float HealPercentBonus = 0f;
        public int HealReceivedFlatBonus = 0;
        public float HealReceivedPercentBonus = 0f;

        // ── Damage Flat Bonus (3 slots) ──────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageBonusFlat = Enums.DamageType.None;
        public int DamageBonusFlatValue = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageBonusFlat2 = Enums.DamageType.None;
        public int DamageBonusFlatValue2 = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageBonusFlat3 = Enums.DamageType.None;
        public int DamageBonusFlatValue3 = 0;

        // ── Damage Percent Bonus (3 slots) ───────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageBonusPercent = Enums.DamageType.None;
        public float DamageBonusPercentValue = 0f;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageBonusPercent2 = Enums.DamageType.None;
        public float DamageBonusPercentValue2 = 0f;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageBonusPercent3 = Enums.DamageType.None;
        public float DamageBonusPercentValue3 = 0f;

        // ── Misc ─────────────────────────────────────────────────
        public int MaxBleedDamagePerTurn = -1;
    }

    // ───────────────────────────────────────────────────────────────
    //  SKIN
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class SkinDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        public string SkinName = "";
        public string SkinSubclass = "";       // SubClassData ID

        // ── Config ───────────────────────────────────────────────
        public bool BaseSkin = false;
        public int SkinOrder = 0;
        public int PerkLevel = 0;
        public string Sku = "";
        public string SteamStat = "";
        public string SkinTextId = "";

        // ── Visual Source ────────────────────────────────────────
        // String ID of an existing skin to copy GO + sprites from.
        // At build time, MakeSkin copies the prefab & sprites.
        public string SpriteSource = "";

        // ── Selection Screen ─────────────────────────────────────
        public float HeroSelectionScreenScale = 1f;
        public float HeroSelectionScreenOffsetX = 0f;
    }

    // ───────────────────────────────────────────────────────────────
    //  PERK
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class PerkDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        public string CustomDescription = "";

        // ── Classification ───────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardClass CardClass = Enums.CardClass.None;
        public bool MainPerk = false;
        public bool ObeliskPerk = false;

        // ── Position ─────────────────────────────────────────────
        public int Level = 0;
        public int Row = 0;

        // ── Icon ─────────────────────────────────────────────────
        public string IconTextValue = "";

        // ── Currency ─────────────────────────────────────────────
        public int AdditionalCurrency = 0;
        public int AdditionalShards = 0;

        // ── Stats ────────────────────────────────────────────────
        public int MaxHealth = 0;
        public int EnergyBegin = 0;
        public int SpeedQuantity = 0;
        public int HealQuantity = 0;

        // ── Damage Bonus ─────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageFlatBonus = Enums.DamageType.None;
        public int DamageFlatBonusValue = 0;

        // ── AuraCurse Bonus ──────────────────────────────────────
        public string AuracurseBonus = "";   // AC ID
        public int AuracurseBonusValue = 0;

        // ── Resist Modification ──────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified = Enums.DamageType.None;
        public int ResistModifiedValue = 0;
    }

    // ───────────────────────────────────────────────────────────────
    //  PERK NODE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class PerkNodeDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";

        // ── Layout ───────────────────────────────────────────────
        public int Type = 0;
        public int Column = 0;
        public int Row = 0;

        // ── Flags ────────────────────────────────────────────────
        public bool LockedInTown = false;
        public bool NotStack = false;

        // ── Cost ─────────────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.PerkCost Cost = Enums.PerkCost.PerkCostBase;

        // ── References ───────────────────────────────────────────
        public string Perk = "";              // PerkData ID
        public string PerkRequired = "";      // PerkNodeData ID
        public List<string> PerksConnected = new List<string>(); // PerkNodeData IDs
    }

    // ───────────────────────────────────────────────────────────────
    //  EVENT REQUIREMENT
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class RequirementDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        public string RequirementName = "";
        public string Description = "";

        // ── Tracking ─────────────────────────────────────────────
        public bool AssignToPlayerAtBegin = false;
        public bool RequirementTrack = false;
        public bool ItemTrack = false;

        // ── Zone Tracking ────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.Zone RequirementZoneFinishTrack = Enums.Zone.None;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.Zone RequirementZoneFinishTrackAlternate = Enums.Zone.None;

        // ── Card Reference ───────────────────────────────────────
        public string TrackCard = "";     // CardData ID
    }

    // ───────────────────────────────────────────────────────────────
    //  CARDBACK
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CardbackDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        public string CardbackName = "";
        public string CardbackTextId = "";

        // ── Config ───────────────────────────────────────────────
        public int CardbackOrder = 1000;
        public bool BaseCardback = false;
        public bool Locked = false;
        public bool ShowIfLocked = false;

        // ── Requirements ─────────────────────────────────────────
        public int RankLevel = 0;
        public string Sku = "";
        public string SteamStat = "";
        public int AdventureLevel = 0;
        public int ObeliskLevel = 0;
        public int SingularityLevel = 0;
        public bool PdxAccountRequired = false;

        // ── Subclass Reference ───────────────────────────────────
        public string CardbackSubclass = "";   // SubClassData ID

        // ── Sprite Source ────────────────────────────────────────
        // String ID of an existing cardback to copy sprite from.
        public string SpriteSource = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  TIER REWARD
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class TierRewardDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";             // keyed by tier number as string
        public int Tier = 0;

        // ── Rewards ──────────────────────────────────────────────
        public int Common = 0;
        public int Uncommon = 0;
        public int Rare = 0;
        public int Epic = 0;
        public int Mythic = 0;
        public int Dust = 0;
    }

    // ───────────────────────────────────────────────────────────────
    //  ZONE PATCH
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents modifications to a base-game zone.
    /// Contains new entities to add and existing entities to override.
    /// The zone's base data is read from Globals.Instance at build time.
    /// </summary>
    [Serializable]
    public class ZonePatchDef
    {
        /// <summary>Base-game zone ID being patched (e.g. "Aquarfall").</summary>
        public string TargetZoneId = "";

        /// <summary>Auto-detected prefix for new entity IDs (e.g. "aqua_").</summary>
        public string DetectedPrefix = "";

        /// <summary>Next available node number for new entities.</summary>
        public int NextNodeNumber = 0;

        /// <summary>Added or modified nodes.</summary>
        public Dictionary<string, NodeDef> Nodes = new();

        /// <summary>Added or modified encounters.</summary>
        public Dictionary<string, CombatDef> Encounters = new();

        /// <summary>Added or modified events.</summary>
        public Dictionary<string, EventDef> Events = new();

        /// <summary>Modified roads.</summary>
        public Dictionary<string, RoadDef> Roads = new();
    }
}
