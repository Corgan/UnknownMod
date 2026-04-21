using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  AURA / CURSE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class AuraCurseDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
        public string ACName = "";
        public bool IsAura = true;
        public string Description = "";
        public int MaxCharges = -1;
        public int MaxMadnessCharges = -1;
        public int AuraConsumed = 1;
        public int ChargesMultiplierDescription = 1;
        public float ChargesAuxNeedForOne1 = 1f;
        public int ChargesAuxNeedForOne2 = 1;
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
        public int IncreasedDirectDamageChargesMultiplierNeededForOne = 1;
        public int IncreasedDirectDamageReceivedPerTurn = 0;
        public float IncreasedDirectDamageReceivedPerStack = 0f;
        public int IncreasedPercentDamageReceivedPerTurn = 0;
        public int IncreasedPercentDamageReceivedPerStack = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType IncreasedDamageReceivedType2 = Enums.DamageType.None;
        public int IncreasedDirectDamageChargesMultiplierNeededForOne2 = 1;
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
        public int CharacterStatChargesMultiplierNeededForOne = 1;
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
        public bool SkipEndTurnRemovalIfNoBegin = false;

        // Charge bonuses (complex nested types — simplified for DTO)
        public List<AuraCurseChargesBonusDef> ACBonusData = new();
        public List<AuraDamageBonusDef> AuraDamageConditionalBonuses = new();
    }

    [Serializable]
    public class AuraCurseChargesBonusDef
    {
        public string AuraCurseId = "";          // AC ID reference
        public int ChargesBonus = 0;
        public int RequiredChargesForBonus = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public AuraCurseData.AuraCurseChargesBonus.BonusAmountType BonusType =
            AuraCurseData.AuraCurseChargesBonus.BonusAmountType.flatBonus;
    }

    [Serializable]
    public class AuraDamageBonusDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageType = Enums.DamageType.None;
        public string BasedOnACId = "";  // AC ID — maps to AuraDamageBasedOnAC
        public int FlatBonus = 0;
        public float FlatBonusPerStack = 0f;
        public int PercentBonus = 0;
        public float PercentBonusPerStack = 0f;
        public float PercentBonusPerStackPerEnergy = 0f;
    }
}
