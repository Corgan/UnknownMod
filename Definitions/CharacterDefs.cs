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
        private List<Enums.EventActivation> _activations = new();
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public List<Enums.EventActivation> Activations
        {
            get => _activations;
            set
            {
                _activations = value ?? new List<Enums.EventActivation>();
            }
        }
        public bool ShouldSerializeActivations() => Activations != null && Activations.Count > 0;

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
        /// <summary>Trait card is only shown for display (not playable).</summary>
        public bool TraitCardDisplayOnly = false;
        /// <summary>Flat damage bonus applies only while the hero has this aura/curse (AuraCurse ID).</summary>
        public string DamageFlatNecessaryAuraCurse = "";
        /// <summary>Minimum charges of the required aura/curse.</summary>
        public int DamageFlatNecessaryAuraCurseCharges = 0;
        /// <summary>Extra flat damage per stack of the required aura/curse.</summary>
        public int DamageBonusFlatPerAuraCurseStack = 0;
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
        public bool HideInGame = false;

        // ── Visual Source ────────────────────────────────────────
        public string SpriteSource = "";

        /// <summary>Optional Graft Sprite (visual graph) id. A graft replaces SpriteSource as the sprite; its Host Model is the rig.</summary>

        public string VisualGraphId = "";
        public bool ShouldSerializeVisualGraphId() => !string.IsNullOrEmpty(VisualGraphId);

        /// <summary>Animation retarget: game trigger (attack, cast, hit, pet) to the rig's own trigger name. "-" plays nothing.</summary>
        public Dictionary<string, string> AnimTriggers = new();
        public bool ShouldSerializeAnimTriggers() => AnimTriggers != null && AnimTriggers.Count > 0;

        /// <summary>Do not borrow a neighbouring animation (cast for attack and back) when the rig lacks a trigger.</summary>
        public bool AnimNoFallback = false;
        public bool ShouldSerializeAnimNoFallback() => AnimNoFallback;

        // ── Portrait / Silhouette art (independent of the rig) ───
        /// <summary>Base skin whose portraits and silhouettes this skin uses. Empty = same as the rig (SpriteSource / graft Host Model).</summary>
        public string PortraitSource = "";
        public bool ShouldSerializePortraitSource() => !string.IsNullOrEmpty(PortraitSource);

        /// <summary>Optional sprite names (mod PNG file names or game sprite names) that override the art copied from PortraitSource.</summary>
        public string Portrait = "";
        public string PortraitLarge = "";
        public string Silhouette = "";
        public string SilhouetteLarge = "";
        public bool ShouldSerializePortrait() => !string.IsNullOrEmpty(Portrait);
        public bool ShouldSerializePortraitLarge() => !string.IsNullOrEmpty(PortraitLarge);
        public bool ShouldSerializeSilhouette() => !string.IsNullOrEmpty(Silhouette);
        public bool ShouldSerializeSilhouetteLarge() => !string.IsNullOrEmpty(SilhouetteLarge);

        /// <summary>Mirror the portraits and silhouettes horizontally (pair with a Flip graft when using a monster as a hero, or the reverse).</summary>
        public bool FlipPortrait = false;
        public bool ShouldSerializeFlipPortrait() => FlipPortrait;

        /// <summary>Zoom of the portrait content inside its frame (above 1 zooms in and crops, below 1 shrinks). 1 = unchanged.</summary>
        public float PortraitScale = 1f;
        public float PortraitOffsetX = 0f;
        public float PortraitOffsetY = 0f;
        public bool ShouldSerializePortraitScale() => PortraitScale != 1f;
        public bool ShouldSerializePortraitOffsetX() => PortraitOffsetX != 0f;
        public bool ShouldSerializePortraitOffsetY() => PortraitOffsetY != 0f;

        // ── Selection Screen ─────────────────────────────────────
        public float HeroSelectionScreenScale = 1f;
        public float HeroSelectionScreenOffsetX = 0f;
    }
}
