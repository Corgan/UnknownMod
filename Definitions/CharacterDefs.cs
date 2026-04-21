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
        public bool HideTimesPerTurnText = false;
        public List<string> KeyNotes = new();
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

        /// <summary>
        /// Optional: ID of a CharacterOverrideDef for runtime bone/sprite overrides.
        /// The def is resolved from ModProject.SpriteSkins at build time.
        /// At build: builds a custom prefab (removed bones, added sprites, tint, etc.)
        /// At runtime: attaches CharacterOverrideDriver component for per-frame LateUpdate work
        /// (bone transforms, model offset/flip, animation keyframes, graft puppets).
        /// </summary>
        [JsonProperty("OverrideId")]
        public string OverrideId = "";
        public bool ShouldSerializeOverrideId() => !string.IsNullOrEmpty(OverrideId);

        // ── Selection Screen ─────────────────────────────────────
        public float HeroSelectionScreenScale = 1f;
        public float HeroSelectionScreenOffsetX = 0f;
    }
}
