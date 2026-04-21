using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    /// <summary>
    /// Item/equipment-specific fields embedded in <see cref="CardDef"/>.
    /// Mirrors game ItemData fields. Null on non-equipment cards.
    /// Used for weapons, armor, jewelry, accessories, pets, and enchantments.
    /// </summary>
    [Serializable]
    public class ItemFields
    {
        // ── Activation / Requisite ───────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EventActivation Activation = Enums.EventActivation.None;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ActivationManual ActivationManual = Enums.ActivationManual.None;
        public bool ActivationOnlyOnHeroes = false;
        public bool ActivateOnReceive = false;
        public bool PreventApplyForHeroTarget = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ItemTarget ItemTarget = Enums.ItemTarget.Self;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ItemTarget OverrideTargetText = Enums.ItemTarget.None;
        public bool DontTargetBoss = false;

        public int TimesPerTurn = 0;
        public int TimesPerCombat = 0;
        public int ExactRound = 0;
        public int RoundCycle = 0;

        public string AuraCurseSetted = "";   // AC ID
        public string AuraCurseSetted2 = "";  // AC ID
        public string AuraCurseSetted3 = "";  // AC ID
        public int AuraCurseNumForOneEvent = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CastedCardType = Enums.CardType.None;
        public bool UsedEnergy = false;
        public float LowerOrEqualPercentHP = 100f;
        public bool EmptyHand = false;
        public bool NotShowCharacterBonus = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ActivePets PetActivation = Enums.ActivePets.None;

        // ── Damage Bonuses (passive stat) ────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageFlatBonus = Enums.DamageType.None;
        public int DamageFlatBonusValue = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageFlatBonus2 = Enums.DamageType.None;
        public int DamageFlatBonusValue2 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageFlatBonus3 = Enums.DamageType.None;
        public int DamageFlatBonusValue3 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamagePercentBonus = Enums.DamageType.None;
        public float DamagePercentBonusValue = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamagePercentBonus2 = Enums.DamageType.None;
        public float DamagePercentBonusValue2 = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamagePercentBonus3 = Enums.DamageType.None;
        public float DamagePercentBonusValue3 = 0f;

        // ── Resist Bonuses ───────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified1 = Enums.DamageType.None;
        public int ResistModifiedValue1 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified2 = Enums.DamageType.None;
        public int ResistModifiedValue2 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified3 = Enums.DamageType.None;
        public int ResistModifiedValue3 = 0;

        // ── Character Stat Mods ──────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CharacterStat CharacterStatModified = Enums.CharacterStat.None;
        public int CharacterStatModifiedValue = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CharacterStat CharacterStatModified2 = Enums.CharacterStat.None;
        public int CharacterStatModifiedValue2 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CharacterStat CharacterStatModified3 = Enums.CharacterStat.None;
        public int CharacterStatModifiedValue3 = 0;

        public int MaxHealth = 0;

        // ── Heal Bonuses ─────────────────────────────────────────
        public int HealFlatBonus = 0;
        public float HealPercentBonus = 0f;
        public int HealReceivedFlatBonus = 0;
        public float HealReceivedPercentBonus = 0f;
        public int HealQuantity = 0;
        public SpecialValueDef HealQuantitySpecialValue;
        public int HealPercentQuantity = 0;
        public int HealPercentQuantitySelf = 0;
        public float HealSelfPerDamageDonePercent = 0f;
        public bool HealSelfTeamPerDamageDonePercent = false;
        public int HealBasedOnAuraCurse = 0;

        // ── Energy / Draw ────────────────────────────────────────
        public int EnergyQuantity = 0;
        public int DrawCards = 0;
        public bool DrawMultiplyByEnergyUsed = false;

        // ── On-activation AuraCurse (target) ─────────────────────
        public string AuracurseGain1 = "";
        public int AuracurseGainValue1 = 0;
        public SpecialValueDef AuracurseGain1SpecialValue;
        public bool Acg1MultiplyByEnergyUsed = false;

        public string AuracurseGain2 = "";
        public int AuracurseGainValue2 = 0;
        public SpecialValueDef AuracurseGain2SpecialValue;
        public bool Acg2MultiplyByEnergyUsed = false;

        public string AuracurseGain3 = "";
        public int AuracurseGainValue3 = 0;
        public SpecialValueDef AuracurseGain3SpecialValue;
        public bool Acg3MultiplyByEnergyUsed = false;
        public bool ChooseOneACToGain = false;

        // ── On-activation AuraCurse (self) ───────────────────────
        public string AuracurseGainSelf1 = "";
        public int AuracurseGainSelfValue1 = 0;
        public string AuracurseGainSelf2 = "";
        public int AuracurseGainSelfValue2 = 0;
        public string AuracurseGainSelf3 = "";
        public int AuracurseGainSelfValue3 = 0;

        // ── AC Dispel / Purge ────────────────────────────────────
        public string AuracurseHeal1 = "";    // AC ID to dispel/purge
        public string AuracurseHeal2 = "";
        public string AuracurseHeal3 = "";
        public bool AcHealFromTarget = false;
        public int StealAuras = 0;
        public int ChanceToDispel = 0;
        public int ChanceToDispelNum = 0;
        public int ChanceToPurge = 0;
        public int ChanceToPurgeNum = 0;
        public int ChanceToDispelSelf = 0;
        public int ChanceToDispelNumSelf = 0;

        // ── Passive AC Bonuses ───────────────────────────────────
        public string AuracurseBonus1 = "";
        public int AuracurseBonusValue1 = 0;
        public string AuracurseBonus2 = "";
        public int AuracurseBonusValue2 = 0;
        public int IncreaseAurasSelf = 0;

        // ── AC Immunities ────────────────────────────────────────
        public string AuracurseImmune1 = "";
        public string AuracurseImmune2 = "";

        // ── Card Gain ────────────────────────────────────────────
        public int CardNum = 0;
        public string CardToGain = "";        // Card ID

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardToGainType = Enums.CardType.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardPlace CardPlace = Enums.CardPlace.Hand;
        public List<string> CardToGainList = new();

        // ── Cost / Economy ───────────────────────────────────────
        public bool CostZero = false;
        public int CostReduction = 0;
        public int CardsReduced = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardToReduceType = Enums.CardType.None;
        public int CostReduceReduction = 0;
        public int CostReduceEnergyRequirement = 0;
        public bool CostReducePermanent = false;
        public bool ReduceHighestCost = false;

        // ── Rewards / Discounts ──────────────────────────────────
        public int PercentRetentionEndGame = 0;
        public int PercentDiscountShop = 0;

        // ── Damage To Target ─────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageToTargetType = Enums.DamageType.None;
        public int DamageToTarget = 0;
        public bool DttMultiplyByEnergyUsed = false;
        public SpecialValueDef DttSpecialValues1;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageToTargetType2 = Enums.DamageType.None;
        public int DamageToTarget2 = 0;
        public SpecialValueDef DttSpecialValues2;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ModifiedDamageType = Enums.DamageType.None;

        // ── Flags ────────────────────────────────────────────────
        public bool CursedItem = false;
        public bool DropOnly = false;
        public bool QuestItem = false;
        public bool DestroyAfterUse = false;
        public bool Vanish = false;
        public bool Permanent = false;
        public bool DuplicateActive = false;
        public bool PassSingleAndCharacterRolls = false;
        public bool OnlyAddItemToNPCs = false;
        public bool AddVanishToDeck = false;

        // ── Enchantment ──────────────────────────────────────────
        public bool IsEnchantment = false;
        public bool UseTheNextInsteadWhenYouPlay = false;
        public int DestroyAfterUses = 0;
        public bool DestroyStartOfTurn = false;
        public bool DestroyEndOfTurn = false;
        public bool CastEnchantmentOnFinishSelfCast = false;

        // ── Custom AC ────────────────────────────────────────────
        public string AuracurseCustomString = "";
        public string AuracurseCustomAC = "";  // AC ID
        public int AuracurseCustomModValue1 = 0;
        public int AuracurseCustomModValue2 = 0;

        // ── Debuff Conversion ────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ConvertReceivedDebuffsIntoDamage = Enums.DamageType.None;
        public bool ConvertReceivedDebuffsIntoCurse = false;

        // ── FX / Effects ─────────────────────────────────────────
        public string EffectItemOwner = "";
        public string EffectCaster = "";
        public float EffectCasterDelay = 0f;
        public string EffectTarget = "";
        public float EffectTargetDelay = 0f;
    }
}
