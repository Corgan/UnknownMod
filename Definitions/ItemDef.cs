using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace UnknownMod.Definitions
{
    [Serializable]
    public class ItemDef
    {
        // â”€â”€ Identity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string Id = "";
        public string Name = "";
        /// <summary>Base-game card ID to copy the item card art sprite from.</summary>
        public string SpriteSource = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardType = Enums.CardType.Weapon;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardRarity Rarity = Enums.CardRarity.Common;

        // â”€â”€ Activation / Requisite â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EventActivation Activation = Enums.EventActivation.None;
        public bool ActivationOnlyOnHeroes = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ItemTarget ItemTarget = Enums.ItemTarget.Self;
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

        // â”€â”€ Damage Bonuses (passive stat) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Resist Bonuses â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified1 = Enums.DamageType.None;
        public int ResistModifiedValue1 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified2 = Enums.DamageType.None;
        public int ResistModifiedValue2 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified3 = Enums.DamageType.None;
        public int ResistModifiedValue3 = 0;

        // â”€â”€ Character Stat Mods â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Heal Bonuses â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Energy / Draw â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int EnergyQuantity = 0;
        public int DrawCards = 0;
        public bool DrawMultiplyByEnergyUsed = false;

        // â”€â”€ On-activation AuraCurse (target) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ On-activation AuraCurse (self) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string AuracurseGainSelf1 = "";
        public int AuracurseGainSelfValue1 = 0;
        public string AuracurseGainSelf2 = "";
        public int AuracurseGainSelfValue2 = 0;
        public string AuracurseGainSelf3 = "";
        public int AuracurseGainSelfValue3 = 0;

        // â”€â”€ AC Dispel / Purge â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Passive AC Bonuses â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string AuracurseBonus1 = "";
        public int AuracurseBonusValue1 = 0;
        public string AuracurseBonus2 = "";
        public int AuracurseBonusValue2 = 0;
        public int IncreaseAurasSelf = 0;

        // â”€â”€ AC Immunities â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string AuracurseImmune1 = "";
        public string AuracurseImmune2 = "";

        // â”€â”€ Card Gain â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int CardNum = 0;
        public string CardToGain = "";        // Card ID

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardToGainType = Enums.CardType.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardPlace CardPlace = Enums.CardPlace.Hand;
        public List<string> CardToGainList = new();

        // â”€â”€ Cost / Economy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public bool CostZero = false;
        public int CostReduction = 0;
        public int CardsReduced = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardToReduceType = Enums.CardType.None;
        public int CostReduceReduction = 0;
        public int CostReduceEnergyRequirement = 0;
        public bool CostReducePermanent = false;
        public bool ReduceHighestCost = false;

        // â”€â”€ Rewards / Discounts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int PercentRetentionEndGame = 0;
        public int PercentDiscountShop = 0;

        // â”€â”€ Damage To Target (enchantment) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Flags â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Enchantment â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public bool IsEnchantment = false;
        public bool UseTheNextInsteadWhenYouPlay = false;
        public int DestroyAfterUses = 0;
        public bool DestroyStartOfTurn = false;
        public bool DestroyEndOfTurn = false;
        public bool CastEnchantmentOnFinishSelfCast = false;

        // â”€â”€ Custom AC â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string AuracurseCustomString = "";
        public string AuracurseCustomAC = "";  // AC ID
        public int AuracurseCustomModValue1 = 0;
        public int AuracurseCustomModValue2 = 0;

        // â”€â”€ FX / Effects â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string EffectItemOwner = "";
        public string EffectCaster = "";
        public float EffectCasterDelay = 0f;
        public string EffectTarget = "";
        public float EffectTargetDelay = 0f;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //  LOOT TABLE
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Serializable]
    public class LootDef
    {
        public string Id = "";
        public int NumItems = 1;
        public int GoldQuantity = 0;
        public bool AllowDropOnlyItems = false;
        public float PercentUncommon = 0f;
        public float PercentRare = 0f;
        public float PercentEpic = 0f;
        public float PercentMythic = 0f;
        public List<LootItemDef> Items = new();
    }

    [Serializable]
    public class LootItemDef
    {
        public string CardId = "";
        public float Percent = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType LootType = Enums.CardType.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardRarity LootRarity = Enums.CardRarity.Common;

        public string LootMisc = "";
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //  ROAD (editor visual data)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

}
