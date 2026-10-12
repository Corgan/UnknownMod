using System;
using System.Collections.Generic;
using Cards;
using Cards.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    /// <summary>Mirror of the game's SpecialValueData (a per-effect value override/scaler).</summary>
    [Serializable]
    public class CardSpecialValueDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardSpecialValueType Type = Enums.CardSpecialValueType.None;
        /// <summary>Aura/curse id, used by AuraCurseTarget / AuraCurseYours value types.</summary>
        public string AuraCurse = "";
        public float Modifier = 0f;
        [JsonConverter(typeof(StringEnumConverter))]
        public SpecialValueData.OperationType Operation = SpecialValueData.OperationType.REPLACE;
        public bool IsGlobal = false;

        [JsonIgnore]
        public bool IsEmpty => Type == Enums.CardSpecialValueType.None && string.IsNullOrEmpty(AuraCurse) && Modifier == 0f;
    }

    /// <summary>Per-effect settings shared by damage/heal/aura/curse effects.</summary>
    [Serializable]
    public class CardEffectExtrasDef
    {
        public CardSpecialValueDef SpecialValue = new();
        [JsonConverter(typeof(StringEnumConverter))]
        public EffectData.ConditionType Condition = EffectData.ConditionType.None;
        public int RequireStanza = 0;
        public int RequireStanzaLead = 0;
        /// <summary>Heal effects only.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public HealType HealType = HealType.Normal;
        /// <summary>Curse effects only.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public CurseEffectData.CurseApplyMode CurseApplyMode = CurseEffectData.CurseApplyMode.Add;

        [JsonIgnore]
        public bool IsDefault =>
            (SpecialValue == null || SpecialValue.IsEmpty) && Condition == EffectData.ConditionType.None
            && RequireStanza == 0 && RequireStanzaLead == 0 && HealType == HealType.Normal
            && CurseApplyMode == CurseEffectData.CurseApplyMode.Add;
    }

    /// <summary>
    /// An effect that does not fit the fixed Damage/Heal/Aura/Curse slots of <see cref="CardDef"/>
    /// (side-targeted damage, team heals, 4th+ aura, ...). Kind is Damage, Heal, Aura or Curse.
    /// </summary>
    [Serializable]
    public class CardExtraEffectDef : CardEffectExtrasDef
    {
        public string Kind = "Damage";
        /// <summary>Damage amount, heal amount, or aura/curse charges.</summary>
        public int Amount = 0;
        public string AuraCurse = "";
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageType = Enums.DamageType.None;
        public bool IgnoreBlock = false;
        [JsonConverter(typeof(StringEnumConverter))]
        public DamageTarget DamageTarget = DamageTarget.Target;
        [JsonConverter(typeof(StringEnumConverter))]
        public HealTarget HealTarget = HealTarget.Target;
        [JsonConverter(typeof(StringEnumConverter))]
        public AuraTarget AuraTarget = AuraTarget.Target;
        [JsonConverter(typeof(StringEnumConverter))]
        public CurseTarget CurseTarget = CurseTarget.Target;
    }

    [Serializable]
    public class CardEnergyRechargeDef
    {
        public int Amount = 0;
        public CardSpecialValueDef SpecialValue = new();
        [JsonConverter(typeof(StringEnumConverter))]
        public EnergyRechargeData.TargetType Target = EnergyRechargeData.TargetType.TARGET;
        [JsonConverter(typeof(StringEnumConverter))]
        public EnergyRechargeData.TargetTeamType TargetTeam = EnergyRechargeData.TargetTeamType.HEROES;
    }

    [Serializable]
    public class CardSummonAuraDef
    {
        public string AuraCurse = "";
        public int Charges = 0;
    }

    /// <summary>One additional summoned unit type (CardDataNew.extraSummons).</summary>
    [Serializable]
    public class CardSummonDef
    {
        public string NpcId = "";
        public int Count = 1;
        public bool Metamorph = false;
        public bool Evolve = false;
        public List<CardSummonAuraDef> Auras = new();
    }

    [Serializable]
    public class CardHeroClassTypesDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClass = Enums.HeroClass.None;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public List<Enums.CardType> CardTypes = new();
    }

    [Serializable]
    public class CardHeroClassCardsDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClass = Enums.HeroClass.None;
        public List<string> CardIds = new();
    }
}
