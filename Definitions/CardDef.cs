using System;
using System.Collections.Generic;
using Cards;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{

    [Serializable]
    public class CardDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
        public string Name = "";
        public string Description = "";
        public string Fluff = "";
        public float FluffPercent = 0f;
        public bool IsUpgraded = false;
        public string BaseCardId = "";
        /// <summary>Base-game card ID to copy the card art sprite from.</summary>
        public string SpriteSource = "";
        /// <summary>Base-game card ID to copy all sound clips from (drag, release, hit, sr exceptions).</summary>
        public string SoundSource = "";
        /// <summary>Base-game pet card ID to copy the pet model prefab from.</summary>
        public string PetModelSource = "";

        // ── Card Classification ──────────────────────────────────
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
        [JsonConverter(typeof(StringEnumConverter))]
        public SpecialCardEnum SpecialCardEnum = SpecialCardEnum.None;

        // ── Cost / Economy ───────────────────────────────────────
        public int EnergyCost = 0;
        public int EnergyCostOriginal = 0;
        public int EnergyCostForShow = 0;
        public int EnergyReductionPermanent = 0;
        public int EnergyReductionTemporal = 0;
        public bool EnergyReductionToZeroPermanent = false;
        public bool EnergyReductionToZeroTemporal = false;

        // ── Flags ────────────────────────────────────────────────
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

        // ── Upgrade Paths (auto-wired at build time, not serialized) ──
        [JsonIgnore] public string UpgradesTo1 = "";
        [JsonIgnore] public string UpgradesTo2 = "";
        [JsonIgnore] public string UpgradedFrom = "";
        [JsonIgnore] public string UpgradesToRareId = "";
        [JsonIgnore] public string BaseCard = "";
        public string RelatedCard = "";
        public string RelatedCard2 = "";
        public string RelatedCard3 = "";

        // ── Damage ───────────────────────────────────────────────
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

        // ── Curses (applied to target) ───────────────────────────
        public string Curse = "";
        public int CurseCharges = 0;
        public int CurseChargesSides = 0;
        public string Curse2 = "";
        public int Curse2Charges = 0;
        public string Curse3 = "";
        public int Curse3Charges = 0;

        // ── Auras (applied to target) ────────────────────────────
        public string Aura = "";
        public int AuraCharges = 0;
        public string Aura2 = "";
        public int Aura2Charges = 0;
        public string Aura3 = "";
        public int Aura3Charges = 0;

        // ── Self auras/curses ────────────────────────────────────
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
        // ── Debuff Conversion ────────────────────────────────────
        public bool ConvertAllDebuffsIntoCurse = false;
        // ── Heal ─────────────────────────────────────────────────
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

        // ── Aura/curse manipulation ──────────────────────────────
        public int TransferCurses = 0;
        public int StealAuras = 0;
        public int ReduceCurses = 0;
        public int ReduceAuras = 0;
        public int IncreaseCurses = 0;
        public int IncreaseAuras = 0;

        // ── Targeting ────────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetSide TargetSide = Enums.CardTargetSide.Enemy;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetType TargetType = Enums.CardTargetType.Single;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetPosition TargetPos = Enums.CardTargetPosition.Anywhere;

        // ── Effect Repeat ────────────────────────────────────────
        public int EffectRepeat = 1;
        public float EffectRepeatDelay = 0f;
        public int EffectRepeatEnergyBonus = 0;
        public int EffectRepeatMaxBonus = 0;
        public int EffectRepeatModificator = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EffectRepeatTarget EffectRepeatTarget = Enums.EffectRepeatTarget.NoRepeat;

        // ── Misc Mechanics ───────────────────────────────────────
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

        // ── Discard Options ──────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType DiscardCardType = Enums.CardType.None;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Enums.CardType[] DiscardCardTypeAux = Array.Empty<Enums.CardType>();
        public bool DiscardCardAutomatic = false;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardPlace DiscardCardPlace = Enums.CardPlace.Discard;

        // ── Add Card ─────────────────────────────────────────────
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

        // ── Look (Scry) ─────────────────────────────────────────
        public int LookCards = 0;
        public int LookCardsDiscardUpTo = 0;
        public int LookCardsVanishUpTo = 0;

        // ── Summon ───────────────────────────────────────────────
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

        // ── AC Energy Bonus ──────────────────────────────────────
        public string AcEnergyBonus = "";    // AC ID
        public int AcEnergyBonusQuantity = 0;
        public string AcEnergyBonus2 = "";   // AC ID
        public int AcEnergyBonus2Quantity = 0;
        public bool ChooseOneOfAvailableAuras = false;

        // ── Special Value System ─────────────────────────────────
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

        // ── Special Value Scaling Flags ──────────────────────────
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

        // ── Aura/Curse Charges Special Value Flags ───────────────
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

        // ── FX / Effects ─────────────────────────────────────────
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

        // ── Pet System ───────────────────────────────────────────
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

        // ── Pet Visuals ──────────────────────────────────────────
        public bool PetFront = true;
        public bool PetInvert = true;
        public float PetOffsetX = 0f;
        public float PetOffsetY = 0f;
        public float PetSizeX = 1f;
        public float PetSizeY = 1f;

        // ── Upgrade params (for auto-generated A variants) ───────
        public float UpgDamageMult = 1.3f;
        public int UpgBonusCurseCharges = 1;
        public int UpgBonusAuraCharges = 1;
        public int UpgBonusHeal = 3;

        // ── Item / Equipment Data ────────────────────────────────
        /// <summary>
        /// Item-specific fields for equipment cards (weapons, armor, jewelry,
        /// accessories, pets, enchantments). Null for non-equipment cards.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ItemFields Item = null;

        /// <summary>True if this card has item data (is equipment/enchantment/pet).</summary>
        [JsonIgnore]
        public bool HasItemData => Item != null;

        // ── Build Pipeline Conversion ────────────────────────────

        /// <summary>
        /// Convert this CardDef into an ItemDef for the build pipeline.
        /// Only valid when <see cref="HasItemData"/> is true.
        /// </summary>
        public ItemDef ToItemDef()
        {
            var it = Item ?? new ItemFields();
            return new ItemDef
            {
                // Identity (from card-level fields)
                Id = Id,
                Name = Name,
                SpriteSource = SpriteSource,
                SoundSource = SoundSource,
                PetModelSource = PetModelSource,
                CardType = CardType,
                Rarity = CardRarity,
                CardUpgraded = CardUpgraded,
                UpgradesToRareId = UpgradesToRareId,
                BaseItemId = UpgradedFrom,
                // Activation
                Activation = it.Activation,
                ActivationManual = it.ActivationManual,
                ActivationOnlyOnHeroes = it.ActivationOnlyOnHeroes,
                ActivateOnReceive = it.ActivateOnReceive,
                PreventApplyForHeroTarget = it.PreventApplyForHeroTarget,
                ItemTarget = it.ItemTarget,
                OverrideTargetText = it.OverrideTargetText,
                DontTargetBoss = it.DontTargetBoss,
                TimesPerTurn = it.TimesPerTurn,
                TimesPerCombat = it.TimesPerCombat,
                ExactRound = it.ExactRound,
                RoundCycle = it.RoundCycle,
                AuraCurseSetted = it.AuraCurseSetted,
                AuraCurseSetted2 = it.AuraCurseSetted2,
                AuraCurseSetted3 = it.AuraCurseSetted3,
                AuraCurseNumForOneEvent = it.AuraCurseNumForOneEvent,
                CastedCardType = it.CastedCardType,
                UsedEnergy = it.UsedEnergy,
                LowerOrEqualPercentHP = it.LowerOrEqualPercentHP,
                EmptyHand = it.EmptyHand,
                NotShowCharacterBonus = it.NotShowCharacterBonus,
                PetActivation = it.PetActivation,
                // Damage Bonuses
                DamageFlatBonus = it.DamageFlatBonus,
                DamageFlatBonusValue = it.DamageFlatBonusValue,
                DamageFlatBonus2 = it.DamageFlatBonus2,
                DamageFlatBonusValue2 = it.DamageFlatBonusValue2,
                DamageFlatBonus3 = it.DamageFlatBonus3,
                DamageFlatBonusValue3 = it.DamageFlatBonusValue3,
                DamagePercentBonus = it.DamagePercentBonus,
                DamagePercentBonusValue = it.DamagePercentBonusValue,
                DamagePercentBonus2 = it.DamagePercentBonus2,
                DamagePercentBonusValue2 = it.DamagePercentBonusValue2,
                DamagePercentBonus3 = it.DamagePercentBonus3,
                DamagePercentBonusValue3 = it.DamagePercentBonusValue3,
                // Resist Bonuses
                ResistModified1 = it.ResistModified1,
                ResistModifiedValue1 = it.ResistModifiedValue1,
                ResistModified2 = it.ResistModified2,
                ResistModifiedValue2 = it.ResistModifiedValue2,
                ResistModified3 = it.ResistModified3,
                ResistModifiedValue3 = it.ResistModifiedValue3,
                // Character Stat Mods
                CharacterStatModified = it.CharacterStatModified,
                CharacterStatModifiedValue = it.CharacterStatModifiedValue,
                CharacterStatModified2 = it.CharacterStatModified2,
                CharacterStatModifiedValue2 = it.CharacterStatModifiedValue2,
                CharacterStatModified3 = it.CharacterStatModified3,
                CharacterStatModifiedValue3 = it.CharacterStatModifiedValue3,
                MaxHealth = it.MaxHealth,
                // Heal Bonuses
                HealFlatBonus = it.HealFlatBonus,
                HealPercentBonus = it.HealPercentBonus,
                HealReceivedFlatBonus = it.HealReceivedFlatBonus,
                HealReceivedPercentBonus = it.HealReceivedPercentBonus,
                HealQuantity = it.HealQuantity,
                HealQuantitySpecialValue = it.HealQuantitySpecialValue,
                HealPercentQuantity = it.HealPercentQuantity,
                HealPercentQuantitySelf = it.HealPercentQuantitySelf,
                HealSelfPerDamageDonePercent = it.HealSelfPerDamageDonePercent,
                HealSelfTeamPerDamageDonePercent = it.HealSelfTeamPerDamageDonePercent,
                HealBasedOnAuraCurse = it.HealBasedOnAuraCurse,
                // Energy / Draw
                EnergyQuantity = it.EnergyQuantity,
                DrawCards = it.DrawCards,
                DrawMultiplyByEnergyUsed = it.DrawMultiplyByEnergyUsed,
                // AC Gain (target)
                AuracurseGain1 = it.AuracurseGain1,
                AuracurseGainValue1 = it.AuracurseGainValue1,
                AuracurseGain1SpecialValue = it.AuracurseGain1SpecialValue,
                Acg1MultiplyByEnergyUsed = it.Acg1MultiplyByEnergyUsed,
                AuracurseGain2 = it.AuracurseGain2,
                AuracurseGainValue2 = it.AuracurseGainValue2,
                AuracurseGain2SpecialValue = it.AuracurseGain2SpecialValue,
                Acg2MultiplyByEnergyUsed = it.Acg2MultiplyByEnergyUsed,
                AuracurseGain3 = it.AuracurseGain3,
                AuracurseGainValue3 = it.AuracurseGainValue3,
                AuracurseGain3SpecialValue = it.AuracurseGain3SpecialValue,
                Acg3MultiplyByEnergyUsed = it.Acg3MultiplyByEnergyUsed,
                ChooseOneACToGain = it.ChooseOneACToGain,
                // AC Gain (self)
                AuracurseGainSelf1 = it.AuracurseGainSelf1,
                AuracurseGainSelfValue1 = it.AuracurseGainSelfValue1,
                AuracurseGainSelf2 = it.AuracurseGainSelf2,
                AuracurseGainSelfValue2 = it.AuracurseGainSelfValue2,
                AuracurseGainSelf3 = it.AuracurseGainSelf3,
                AuracurseGainSelfValue3 = it.AuracurseGainSelfValue3,
                // AC Dispel / Purge
                AuracurseHeal1 = it.AuracurseHeal1,
                AuracurseHeal2 = it.AuracurseHeal2,
                AuracurseHeal3 = it.AuracurseHeal3,
                AcHealFromTarget = it.AcHealFromTarget,
                StealAuras = it.StealAuras,
                ChanceToDispel = it.ChanceToDispel,
                ChanceToDispelNum = it.ChanceToDispelNum,
                ChanceToPurge = it.ChanceToPurge,
                ChanceToPurgeNum = it.ChanceToPurgeNum,
                ChanceToDispelSelf = it.ChanceToDispelSelf,
                ChanceToDispelNumSelf = it.ChanceToDispelNumSelf,
                // Passive AC Bonuses
                AuracurseBonus1 = it.AuracurseBonus1,
                AuracurseBonusValue1 = it.AuracurseBonusValue1,
                AuracurseBonus2 = it.AuracurseBonus2,
                AuracurseBonusValue2 = it.AuracurseBonusValue2,
                IncreaseAurasSelf = it.IncreaseAurasSelf,
                // AC Immunities
                AuracurseImmune1 = it.AuracurseImmune1,
                AuracurseImmune2 = it.AuracurseImmune2,
                // Card Gain
                CardNum = it.CardNum,
                CardToGain = it.CardToGain,
                CardToGainType = it.CardToGainType,
                CardPlace = it.CardPlace,
                CardToGainList = it.CardToGainList != null ? new List<string>(it.CardToGainList) : new(),
                // Cost / Economy
                CostZero = it.CostZero,
                CostReduction = it.CostReduction,
                CardsReduced = it.CardsReduced,
                CardToReduceType = it.CardToReduceType,
                CostReduceReduction = it.CostReduceReduction,
                CostReduceEnergyRequirement = it.CostReduceEnergyRequirement,
                CostReducePermanent = it.CostReducePermanent,
                ReduceHighestCost = it.ReduceHighestCost,
                // Rewards
                PercentRetentionEndGame = it.PercentRetentionEndGame,
                PercentDiscountShop = it.PercentDiscountShop,
                // Damage To Target
                DamageToTargetType = it.DamageToTargetType,
                DamageToTarget = it.DamageToTarget,
                DttMultiplyByEnergyUsed = it.DttMultiplyByEnergyUsed,
                DttSpecialValues1 = it.DttSpecialValues1,
                DamageToTargetType2 = it.DamageToTargetType2,
                DamageToTarget2 = it.DamageToTarget2,
                DttSpecialValues2 = it.DttSpecialValues2,
                ModifiedDamageType = it.ModifiedDamageType,
                // Flags
                CursedItem = it.CursedItem,
                DropOnly = it.DropOnly,
                QuestItem = it.QuestItem,
                DestroyAfterUse = it.DestroyAfterUse,
                Vanish = it.Vanish,
                Permanent = it.Permanent,
                DuplicateActive = it.DuplicateActive,
                PassSingleAndCharacterRolls = it.PassSingleAndCharacterRolls,
                OnlyAddItemToNPCs = it.OnlyAddItemToNPCs,
                AddVanishToDeck = it.AddVanishToDeck,
                // Enchantment
                IsEnchantment = it.IsEnchantment,
                UseTheNextInsteadWhenYouPlay = it.UseTheNextInsteadWhenYouPlay,
                DestroyAfterUses = it.DestroyAfterUses,
                DestroyStartOfTurn = it.DestroyStartOfTurn,
                DestroyEndOfTurn = it.DestroyEndOfTurn,
                CastEnchantmentOnFinishSelfCast = it.CastEnchantmentOnFinishSelfCast,
                // Custom AC
                AuracurseCustomString = it.AuracurseCustomString,
                AuracurseCustomAC = it.AuracurseCustomAC,
                AuracurseCustomModValue1 = it.AuracurseCustomModValue1,
                AuracurseCustomModValue2 = it.AuracurseCustomModValue2,
                // Debuff Conversion
                ConvertReceivedDebuffsIntoDamage = it.ConvertReceivedDebuffsIntoDamage,
                ConvertReceivedDebuffsIntoCurse = it.ConvertReceivedDebuffsIntoCurse,
                // FX
                EffectItemOwner = it.EffectItemOwner,
                EffectCaster = it.EffectCaster,
                EffectCasterDelay = it.EffectCasterDelay,
                EffectTarget = it.EffectTarget,
                EffectTargetDelay = it.EffectTargetDelay,
                // Paired card — reference to this CardDef
                Card = this,
            };
        }

        /// <summary>
        /// Populate this CardDef from a base-game ItemData + paired CardData snapshot.
        /// </summary>
        public static CardDef SnapshotFromItem(ItemData itemData, CardData pairedCard)
        {
            var cd = pairedCard != null
                ? UnknownMod.Core.ModProjectBuilder.SnapshotCard(pairedCard)
                : new CardDef();

            if (itemData == null) return cd;

            cd.Id = itemData.Id;
            cd.Item = new ItemFields();
            var it = cd.Item;

            // Use the SnapshotItem helper for field extraction, then copy into ItemFields
            var snap = UnknownMod.Core.DataHelper.SnapshotItem(itemData);

            it.Activation = snap.Activation;
            it.ActivationManual = snap.ActivationManual;
            it.ActivationOnlyOnHeroes = snap.ActivationOnlyOnHeroes;
            it.ActivateOnReceive = snap.ActivateOnReceive;
            it.PreventApplyForHeroTarget = snap.PreventApplyForHeroTarget;
            it.ItemTarget = snap.ItemTarget;
            it.OverrideTargetText = snap.OverrideTargetText;
            it.DontTargetBoss = snap.DontTargetBoss;
            it.TimesPerTurn = snap.TimesPerTurn;
            it.TimesPerCombat = snap.TimesPerCombat;
            it.ExactRound = snap.ExactRound;
            it.RoundCycle = snap.RoundCycle;
            it.AuraCurseSetted = snap.AuraCurseSetted;
            it.AuraCurseSetted2 = snap.AuraCurseSetted2;
            it.AuraCurseSetted3 = snap.AuraCurseSetted3;
            it.AuraCurseNumForOneEvent = snap.AuraCurseNumForOneEvent;
            it.CastedCardType = snap.CastedCardType;
            it.UsedEnergy = snap.UsedEnergy;
            it.LowerOrEqualPercentHP = snap.LowerOrEqualPercentHP;
            it.EmptyHand = snap.EmptyHand;
            it.NotShowCharacterBonus = snap.NotShowCharacterBonus;
            it.PetActivation = snap.PetActivation;
            it.DamageFlatBonus = snap.DamageFlatBonus;
            it.DamageFlatBonusValue = snap.DamageFlatBonusValue;
            it.DamageFlatBonus2 = snap.DamageFlatBonus2;
            it.DamageFlatBonusValue2 = snap.DamageFlatBonusValue2;
            it.DamageFlatBonus3 = snap.DamageFlatBonus3;
            it.DamageFlatBonusValue3 = snap.DamageFlatBonusValue3;
            it.DamagePercentBonus = snap.DamagePercentBonus;
            it.DamagePercentBonusValue = snap.DamagePercentBonusValue;
            it.DamagePercentBonus2 = snap.DamagePercentBonus2;
            it.DamagePercentBonusValue2 = snap.DamagePercentBonusValue2;
            it.DamagePercentBonus3 = snap.DamagePercentBonus3;
            it.DamagePercentBonusValue3 = snap.DamagePercentBonusValue3;
            it.ResistModified1 = snap.ResistModified1;
            it.ResistModifiedValue1 = snap.ResistModifiedValue1;
            it.ResistModified2 = snap.ResistModified2;
            it.ResistModifiedValue2 = snap.ResistModifiedValue2;
            it.ResistModified3 = snap.ResistModified3;
            it.ResistModifiedValue3 = snap.ResistModifiedValue3;
            it.CharacterStatModified = snap.CharacterStatModified;
            it.CharacterStatModifiedValue = snap.CharacterStatModifiedValue;
            it.CharacterStatModified2 = snap.CharacterStatModified2;
            it.CharacterStatModifiedValue2 = snap.CharacterStatModifiedValue2;
            it.CharacterStatModified3 = snap.CharacterStatModified3;
            it.CharacterStatModifiedValue3 = snap.CharacterStatModifiedValue3;
            it.MaxHealth = snap.MaxHealth;
            it.HealFlatBonus = snap.HealFlatBonus;
            it.HealPercentBonus = snap.HealPercentBonus;
            it.HealReceivedFlatBonus = snap.HealReceivedFlatBonus;
            it.HealReceivedPercentBonus = snap.HealReceivedPercentBonus;
            it.HealQuantity = snap.HealQuantity;
            it.HealQuantitySpecialValue = snap.HealQuantitySpecialValue;
            it.HealPercentQuantity = snap.HealPercentQuantity;
            it.HealPercentQuantitySelf = snap.HealPercentQuantitySelf;
            it.HealSelfPerDamageDonePercent = snap.HealSelfPerDamageDonePercent;
            it.HealSelfTeamPerDamageDonePercent = snap.HealSelfTeamPerDamageDonePercent;
            it.HealBasedOnAuraCurse = snap.HealBasedOnAuraCurse;
            it.EnergyQuantity = snap.EnergyQuantity;
            it.DrawCards = snap.DrawCards;
            it.DrawMultiplyByEnergyUsed = snap.DrawMultiplyByEnergyUsed;
            it.AuracurseGain1 = snap.AuracurseGain1;
            it.AuracurseGainValue1 = snap.AuracurseGainValue1;
            it.AuracurseGain1SpecialValue = snap.AuracurseGain1SpecialValue;
            it.Acg1MultiplyByEnergyUsed = snap.Acg1MultiplyByEnergyUsed;
            it.AuracurseGain2 = snap.AuracurseGain2;
            it.AuracurseGainValue2 = snap.AuracurseGainValue2;
            it.AuracurseGain2SpecialValue = snap.AuracurseGain2SpecialValue;
            it.Acg2MultiplyByEnergyUsed = snap.Acg2MultiplyByEnergyUsed;
            it.AuracurseGain3 = snap.AuracurseGain3;
            it.AuracurseGainValue3 = snap.AuracurseGainValue3;
            it.AuracurseGain3SpecialValue = snap.AuracurseGain3SpecialValue;
            it.Acg3MultiplyByEnergyUsed = snap.Acg3MultiplyByEnergyUsed;
            it.ChooseOneACToGain = snap.ChooseOneACToGain;
            it.AuracurseGainSelf1 = snap.AuracurseGainSelf1;
            it.AuracurseGainSelfValue1 = snap.AuracurseGainSelfValue1;
            it.AuracurseGainSelf2 = snap.AuracurseGainSelf2;
            it.AuracurseGainSelfValue2 = snap.AuracurseGainSelfValue2;
            it.AuracurseGainSelf3 = snap.AuracurseGainSelf3;
            it.AuracurseGainSelfValue3 = snap.AuracurseGainSelfValue3;
            it.AuracurseHeal1 = snap.AuracurseHeal1;
            it.AuracurseHeal2 = snap.AuracurseHeal2;
            it.AuracurseHeal3 = snap.AuracurseHeal3;
            it.AcHealFromTarget = snap.AcHealFromTarget;
            it.StealAuras = snap.StealAuras;
            it.ChanceToDispel = snap.ChanceToDispel;
            it.ChanceToDispelNum = snap.ChanceToDispelNum;
            it.ChanceToPurge = snap.ChanceToPurge;
            it.ChanceToPurgeNum = snap.ChanceToPurgeNum;
            it.ChanceToDispelSelf = snap.ChanceToDispelSelf;
            it.ChanceToDispelNumSelf = snap.ChanceToDispelNumSelf;
            it.AuracurseBonus1 = snap.AuracurseBonus1;
            it.AuracurseBonusValue1 = snap.AuracurseBonusValue1;
            it.AuracurseBonus2 = snap.AuracurseBonus2;
            it.AuracurseBonusValue2 = snap.AuracurseBonusValue2;
            it.IncreaseAurasSelf = snap.IncreaseAurasSelf;
            it.AuracurseImmune1 = snap.AuracurseImmune1;
            it.AuracurseImmune2 = snap.AuracurseImmune2;
            it.CardNum = snap.CardNum;
            it.CardToGain = snap.CardToGain;
            it.CardToGainType = snap.CardToGainType;
            it.CardPlace = snap.CardPlace;
            it.CardToGainList = snap.CardToGainList ?? new();
            it.CostZero = snap.CostZero;
            it.CostReduction = snap.CostReduction;
            it.CardsReduced = snap.CardsReduced;
            it.CardToReduceType = snap.CardToReduceType;
            it.CostReduceReduction = snap.CostReduceReduction;
            it.CostReduceEnergyRequirement = snap.CostReduceEnergyRequirement;
            it.CostReducePermanent = snap.CostReducePermanent;
            it.ReduceHighestCost = snap.ReduceHighestCost;
            it.PercentRetentionEndGame = snap.PercentRetentionEndGame;
            it.PercentDiscountShop = snap.PercentDiscountShop;
            it.DamageToTargetType = snap.DamageToTargetType;
            it.DamageToTarget = snap.DamageToTarget;
            it.DttMultiplyByEnergyUsed = snap.DttMultiplyByEnergyUsed;
            it.DttSpecialValues1 = snap.DttSpecialValues1;
            it.DamageToTargetType2 = snap.DamageToTargetType2;
            it.DamageToTarget2 = snap.DamageToTarget2;
            it.DttSpecialValues2 = snap.DttSpecialValues2;
            it.ModifiedDamageType = snap.ModifiedDamageType;
            it.CursedItem = snap.CursedItem;
            it.DropOnly = snap.DropOnly;
            it.QuestItem = snap.QuestItem;
            it.DestroyAfterUse = snap.DestroyAfterUse;
            it.Vanish = snap.Vanish;
            it.Permanent = snap.Permanent;
            it.DuplicateActive = snap.DuplicateActive;
            it.PassSingleAndCharacterRolls = snap.PassSingleAndCharacterRolls;
            it.OnlyAddItemToNPCs = snap.OnlyAddItemToNPCs;
            it.AddVanishToDeck = snap.AddVanishToDeck;
            it.IsEnchantment = snap.IsEnchantment;
            it.UseTheNextInsteadWhenYouPlay = snap.UseTheNextInsteadWhenYouPlay;
            it.DestroyAfterUses = snap.DestroyAfterUses;
            it.DestroyStartOfTurn = snap.DestroyStartOfTurn;
            it.DestroyEndOfTurn = snap.DestroyEndOfTurn;
            it.CastEnchantmentOnFinishSelfCast = snap.CastEnchantmentOnFinishSelfCast;
            it.AuracurseCustomString = snap.AuracurseCustomString;
            it.AuracurseCustomAC = snap.AuracurseCustomAC;
            it.AuracurseCustomModValue1 = snap.AuracurseCustomModValue1;
            it.AuracurseCustomModValue2 = snap.AuracurseCustomModValue2;
            it.ConvertReceivedDebuffsIntoDamage = snap.ConvertReceivedDebuffsIntoDamage;
            it.ConvertReceivedDebuffsIntoCurse = snap.ConvertReceivedDebuffsIntoCurse;
            it.EffectItemOwner = snap.EffectItemOwner;
            it.EffectCaster = snap.EffectCaster;
            it.EffectCasterDelay = snap.EffectCasterDelay;
            it.EffectTarget = snap.EffectTarget;
            it.EffectTargetDelay = snap.EffectTargetDelay;

            return cd;
        }

        /// <summary>
        /// Returns the semantic subfolder name for this card based on its CardClass/CardType.
        /// Used by the loader/saver to determine which subfolder to use.
        /// </summary>
        [JsonIgnore]
        public string SemanticFolder
        {
            get
            {
                if (CardType == Enums.CardType.Pet || CardType == Enums.CardType.Petrare) return "pets";
                if (CardType == Enums.CardType.Enchantment) return "enchantments";
                if (CardType == Enums.CardType.Weapon || CardType == Enums.CardType.Armor ||
                    CardType == Enums.CardType.Jewelry || CardType == Enums.CardType.Accesory) return "equipment";
                if (CardType == Enums.CardType.Corruption) return "corruptions";
                if (CardType == Enums.CardType.Food) return "food";
                switch (CardClass)
                {
                    case Enums.CardClass.Warrior:
                    case Enums.CardClass.Mage:
                    case Enums.CardClass.Healer:
                    case Enums.CardClass.Scout: return "hero";
                    case Enums.CardClass.Boon: return "boons";
                    case Enums.CardClass.Injury: return "injuries";
                    case Enums.CardClass.Monster: return "monster";
                    default: return "special";
                }
            }
        }
    }
}
