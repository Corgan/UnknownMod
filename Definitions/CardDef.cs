using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace UnknownMod.Definitions
{

    [Serializable]
    public class CardDef
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public string Fluff = "";
        public float FluffPercent = 0f;
        public bool IsUpgraded = false;
        public string BaseCardId = "";
        /// <summary>Base-game card ID to copy the card art sprite from.</summary>
        public string SpriteSource = "";

        // â”€â”€ Card Classification â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardUpgraded CardUpgraded = Enums.CardUpgraded.No;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardRarity CardRarity = Enums.CardRarity.Common;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardType = Enums.CardType.None;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Enums.CardType[] CardTypeAux = Array.Empty<Enums.CardType>();
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardClass CardClass = Enums.CardClass.Monster;
        public int CardNumber = 0;
        public int MaxInDeck = 0;

        // â”€â”€ Cost / Economy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int EnergyCost = 0;
        public int EnergyCostOriginal = 0;
        public int EnergyCostForShow = 0;
        public int EnergyReductionPermanent = 0;
        public int EnergyReductionTemporal = 0;
        public bool EnergyReductionToZeroPermanent = false;
        public bool EnergyReductionToZeroTemporal = false;

        // â”€â”€ Flags â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public bool Playable = false;
        public bool Visible = false;
        public bool ShowInTome = false;
        public bool AutoplayDraw = false;
        public bool AutoplayEndTurn = false;
        public bool Vanish = false;
        public bool Innate = false;
        public bool Lazy = false;
        public bool Corrupted = false;
        public bool EndTurn = false;
        public bool Starter = false;
        public bool FlipSprite = false;
        public bool ModifiedByTrait = false;
        public bool OnlyInWeekly = false;
        public string Sku = "";

        // â”€â”€ Upgrade Paths â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string UpgradesTo1 = "";
        public string UpgradesTo2 = "";
        public string UpgradedFrom = "";
        public string UpgradesToRareId = "";
        public bool ShouldSerializeUpgradesToRareId() => !string.IsNullOrEmpty(UpgradesToRareId);
        public string BaseCard = "";
        public string RelatedCard = "";
        public string RelatedCard2 = "";
        public string RelatedCard3 = "";

        // â”€â”€ Damage â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int Damage = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageType = Enums.DamageType.None;
        public int DamageSides = 0;
        public int DamageEnergyBonus = 0;

        public int Damage2 = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageType2 = Enums.DamageType.None;
        public int DamageSides2 = 0;

        public bool IgnoreBlock = false;
        public bool IgnoreBlock2 = false;
        public int DamageSelf = 0;
        public int DamageSelf2 = 0;

        // â”€â”€ Curses (applied to target) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string Curse = "";
        public int CurseCharges = 0;
        public int CurseChargesSides = 0;
        public string Curse2 = "";
        public int Curse2Charges = 0;
        public string Curse3 = "";
        public int Curse3Charges = 0;

        // â”€â”€ Auras (applied to target) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string Aura = "";
        public int AuraCharges = 0;
        public string Aura2 = "";
        public int Aura2Charges = 0;
        public string Aura3 = "";
        public int Aura3Charges = 0;

        // â”€â”€ Self auras/curses â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string AuraSelf = "";
        public int AuraSelfCharges = 0;
        public string AuraSelf2 = "";
        public int AuraSelf2Charges = 0;
        public string AuraSelf3 = "";
        public int AuraSelf3Charges = 0;
        public string CurseSelf = "";
        public int CurseSelfCharges = 0;
        public string CurseSelf2 = "";
        public int CurseSelf2Charges = 0;
        public string CurseSelf3 = "";
        public int CurseSelf3Charges = 0;

        // â”€â”€ Heal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int Heal = 0;
        public int HealSides = 0;
        public int HealSelf = 0;
        public int HealEnergyBonus = 0;
        public int HealCurses = 0;
        public int DispelAuras = 0;
        public float HealSelfPerDamageDonePercent = 0f;
        public string HealAuraCurseSelf = "";    // specific AC to remove from self
        public string HealAuraCurseName = "";    // specific AC to remove from target
        public string HealAuraCurseName2 = "";
        public string HealAuraCurseName3 = "";
        public string HealAuraCurseName4 = "";

        // â”€â”€ Aura/curse manipulation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int TransferCurses = 0;
        public int StealAuras = 0;
        public int ReduceCurses = 0;
        public int ReduceAuras = 0;
        public int IncreaseCurses = 0;
        public int IncreaseAuras = 0;

        // â”€â”€ Targeting â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetSide TargetSide = Enums.CardTargetSide.Enemy;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetType TargetType = Enums.CardTargetType.Single;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetPosition TargetPos = Enums.CardTargetPosition.Anywhere;

        // â”€â”€ Effect Repeat â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int EffectRepeat = 1;
        public float EffectRepeatDelay = 0f;
        public int EffectRepeatEnergyBonus = 0;
        public int EffectRepeatMaxBonus = 0;
        public int EffectRepeatModificator = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EffectRepeatTarget EffectRepeatTarget = Enums.EffectRepeatTarget.NoRepeat;

        // â”€â”€ Misc Mechanics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public bool MoveToCenter = false;
        public int SelfHealthLoss = 0;
        public int PushTarget = 0;
        public int PullTarget = 0;
        public int DrawCard = 0;
        public int DiscardCard = 0;
        public int EnergyRecharge = 0;
        public int GoldGainQuantity = 0;
        public int ShardsGainQuantity = 0;
        public int ExhaustCounter = 0;
        public string EffectRequired = "";
        public float SelfKillHiddenSeconds = 0f;

        // â”€â”€ Discard Options â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType DiscardCardType = Enums.CardType.None;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Enums.CardType[] DiscardCardTypeAux = Array.Empty<Enums.CardType>();
        public bool DiscardCardAutomatic = false;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardPlace DiscardCardPlace = Enums.CardPlace.Discard;

        // â”€â”€ Add Card â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int AddCard = 0;
        public string AddCardId = "";
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType AddCardType = Enums.CardType.None;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Enums.CardType[] AddCardTypeAux = Array.Empty<Enums.CardType>();
        public int AddCardChoose = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardFrom AddCardFrom = Enums.CardFrom.Game;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardPlace AddCardPlace = Enums.CardPlace.Hand;
        public int AddCardReducedCost = 0;
        public bool AddCardCostTurn = false;
        public bool AddCardVanish = false;
        public bool AddCardOnlyCheckAuxTypes = false;
        public bool AddCardFromVanishPile = false;
        public bool AddVanishToDeck = false;
        public List<string> AddCardList = new();  // card IDs

        // â”€â”€ Look (Scry) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int LookCards = 0;
        public int LookCardsDiscardUpTo = 0;
        public int LookCardsVanishUpTo = 0;

        // â”€â”€ Summon â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string SummonUnitId = "";
        public int SummonNum = 0;
        public string SummonAura = "";       // AC ID
        public int SummonAuraCharges = 0;
        public string SummonAura2 = "";
        public int SummonAuraCharges2 = 0;
        public string SummonAura3 = "";
        public int SummonAuraCharges3 = 0;
        public bool Evolve = false;
        public bool Metamorph = false;

        // â”€â”€ AC Energy Bonus â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string AcEnergyBonus = "";    // AC ID
        public int AcEnergyBonusQuantity = 0;
        public string AcEnergyBonus2 = "";   // AC ID
        public int AcEnergyBonus2Quantity = 0;
        public bool ChooseOneOfAvailableAuras = false;

        // â”€â”€ Special Value System â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardSpecialValue SpecialValueGlobal = Enums.CardSpecialValue.None;
        public float SpecialValueModifierGlobal = 0f;
        public string SpecialAuraCurseNameGlobal = "";  // AC ID
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardSpecialValue SpecialValue1 = Enums.CardSpecialValue.None;
        public float SpecialValueModifier1 = 0f;
        public string SpecialAuraCurseName1 = "";       // AC ID
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardSpecialValue SpecialValue2 = Enums.CardSpecialValue.None;
        public float SpecialValueModifier2 = 0f;
        public string SpecialAuraCurseName2 = "";       // AC ID

        // â”€â”€ Special Value Scaling Flags â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public bool DamageSpecialValueGlobal = false;
        public bool DamageSpecialValue1 = false;
        public bool DamageSpecialValue2 = false;
        public bool Damage2SpecialValueGlobal = false;
        public bool Damage2SpecialValue1 = false;
        public bool Damage2SpecialValue2 = false;
        public bool HealSpecialValueGlobal = false;
        public bool HealSpecialValue1 = false;
        public bool HealSpecialValue2 = false;
        public bool HealSelfSpecialValueGlobal = false;
        public bool HealSelfSpecialValue1 = false;
        public bool HealSelfSpecialValue2 = false;
        public bool SelfHealthLossSpecialGlobal = false;
        public bool SelfHealthLossSpecialValue1 = false;
        public bool SelfHealthLossSpecialValue2 = false;
        public bool EnergyRechargeSpecialValueGlobal = false;
        public bool DrawCardSpecialValueGlobal = false;

        // â”€â”€ Aura/Curse Charges Special Value Flags â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public bool AuraChargesSpecialValueGlobal = false;
        public bool AuraChargesSpecialValue1 = false;
        public bool AuraChargesSpecialValue2 = false;
        public bool AuraCharges2SpecialValueGlobal = false;
        public bool AuraCharges2SpecialValue1 = false;
        public bool AuraCharges2SpecialValue2 = false;
        public bool AuraCharges3SpecialValueGlobal = false;
        public bool AuraCharges3SpecialValue1 = false;
        public bool AuraCharges3SpecialValue2 = false;
        public bool CurseChargesSpecialValueGlobal = false;
        public bool CurseChargesSpecialValue1 = false;
        public bool CurseChargesSpecialValue2 = false;
        public bool CurseCharges2SpecialValueGlobal = false;
        public bool CurseCharges2SpecialValue1 = false;
        public bool CurseCharges2SpecialValue2 = false;
        public bool CurseCharges3SpecialValueGlobal = false;
        public bool CurseCharges3SpecialValue1 = false;
        public bool CurseCharges3SpecialValue2 = false;

        // â”€â”€ FX / Effects â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string EffectCaster = "";
        public string EffectTarget = "";
        public string EffectPreAction = "";
        public float EffectPostCastDelay = 0f;
        public bool EffectCasterRepeat = false;
        public bool EffectCastCenter = false;
        public string EffectTrail = "";
        public bool EffectTrailRepeat = false;
        public float EffectTrailSpeed = 0f;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EffectTrailAngle EffectTrailAngle = Enums.EffectTrailAngle.Horizontal;
        public float EffectPostTargetDelay = 0f;

        // â”€â”€ Pet System â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ActivePets PetActivation = Enums.ActivePets.None;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType PetBonusDamageType = Enums.DamageType.None;
        public int PetBonusDamageAmount = 0;
        public bool IsPetAttack = false;
        public bool IsPetCast = false;
        public bool KillPet = false;
        public bool PetTemporal = false;
        public bool PetTemporalAttack = false;
        public bool PetTemporalCast = false;
        public bool PetTemporalMoveToCenter = false;
        public bool PetTemporalMoveToBack = false;
        public float PetTemporalFadeOutDelay = 0f;

        // â”€â”€ Pet Visuals â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public bool PetFront = true;
        public bool PetInvert = true;
        public float PetOffsetX = 0f;
        public float PetOffsetY = 0f;
        public float PetSizeX = 1f;
        public float PetSizeY = 1f;

        // â”€â”€ Upgrade params (for auto-generated A variants) â”€â”€â”€â”€â”€â”€â”€
        public float UpgDamageMult = 1.3f;
        public int UpgBonusCurseCharges = 1;
        public int UpgBonusAuraCharges = 1;
        public int UpgBonusHeal = 3;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //  ITEM (equipment / reward)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //  SPECIAL VALUE (mirrors game SpecialValues struct)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Serializable]
    public class SpecialValueDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.SpecialValueModifierName Name = Enums.SpecialValueModifierName.RuneCharges;
        public bool Use = false;
        public float Multiplier = 0f;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //  ITEM
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

}
