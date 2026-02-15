using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  TRAIT
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class TraitDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
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
    public class SkinDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
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
        public string SpriteSource = "";

        // ── Selection Screen ─────────────────────────────────────
        public float HeroSelectionScreenScale = 1f;
        public float HeroSelectionScreenOffsetX = 0f;
    }

    // ───────────────────────────────────────────────────────────────
    //  PERK
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class PerkDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
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
    public class PerkNodeDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

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
        public List<string> PerksConnected = new(); // PerkNodeData IDs
    }
}
