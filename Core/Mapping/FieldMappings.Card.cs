namespace UnknownMod.Core
{
    public static partial class FieldMappings
    {
        // ─────────────────────────────────────────────────────────
        //  CARD  (SO fields = camelCase private via AccessTools)
        // ─────────────────────────────────────────────────────────

        public static readonly FieldMapping[] Card = new[]
        {
            // ── Identity ─────────────────────────────────────────
            new FieldMapping("id",          "Id"),
            new FieldMapping("cardName",    "Name"),
            new FieldMapping("description", "Description"),
            new FieldMapping("fluff",       "Fluff"),
            new FieldMapping("fluffPercent","FluffPercent"),
            new FieldMapping("sku",         "Sku"),

            // ── Classification ───────────────────────────────────
            new FieldMapping("cardUpgraded","CardUpgraded"),
            new FieldMapping("cardRarity",  "CardRarity"),
            new FieldMapping("cardType",    "CardType"),
            // cardTypeAux: array — edge case
            new FieldMapping("cardClass",   "CardClass"),
            new FieldMapping("cardNumber",  "CardNumber"),
            new FieldMapping("maxInDeck",   "MaxInDeck"),
            new FieldMapping("specialCardEnum", "SpecialCardEnum"),

            // ── Cost / Economy ───────────────────────────────────
            new FieldMapping("energyCost",                    "EnergyCost"),
            new FieldMapping("energyCostOriginal",            "EnergyCostOriginal"),
            new FieldMapping("energyCostForShow",             "EnergyCostForShow"),
            new FieldMapping("energyReductionPermanent",      "EnergyReductionPermanent"),
            new FieldMapping("energyReductionTemporal",       "EnergyReductionTemporal"),
            new FieldMapping("energyReductionToZeroPermanent","EnergyReductionToZeroPermanent"),
            new FieldMapping("energyReductionToZeroTemporal", "EnergyReductionToZeroTemporal"),

            // ── Flags ────────────────────────────────────────────
            new FieldMapping("playable",        "Playable"),
            new FieldMapping("visible",         "Visible"),
            new FieldMapping("showInTome",      "ShowInTome"),
            new FieldMapping("autoplayDraw",    "AutoplayDraw"),
            new FieldMapping("autoplayEndTurn", "AutoplayEndTurn"),
            new FieldMapping("vanish",          "Vanish"),
            new FieldMapping("innate",          "Innate"),
            new FieldMapping("lazy",            "Lazy"),
            new FieldMapping("corrupted",       "Corrupted"),
            new FieldMapping("endTurn",         "EndTurn"),
            new FieldMapping("starter",         "Starter"),
            new FieldMapping("flipSprite",      "FlipSprite"),
            new FieldMapping("modifiedByTrait", "ModifiedByTrait"),
            new FieldMapping("onlyInWeekly",    "OnlyInWeekly"),

            // ── Upgrade Paths ────────────────────────────────────
            new FieldMapping("upgradesTo1",     "UpgradesTo1"),
            new FieldMapping("upgradesTo2",     "UpgradesTo2"),
            new FieldMapping("upgradedFrom",    "UpgradedFrom"),
            new FieldMapping("baseCard",        "BaseCard"),
            new FieldMapping("relatedCard",     "RelatedCard"),
            new FieldMapping("relatedCard2",    "RelatedCard2"),
            new FieldMapping("relatedCard3",    "RelatedCard3"),
            new FieldMapping("upgradesToRare",  "UpgradesToRareId", RefType.Card),

            // ── Targeting ────────────────────────────────────────
            new FieldMapping("targetSide",      "TargetSide"),
            new FieldMapping("targetType",      "TargetType"),
            new FieldMapping("targetPosition",  "TargetPos"),

            // ── Damage 1 ─────────────────────────────────────────
            new FieldMapping("damage",                    "Damage"),
            new FieldMapping("damageType",                "DamageType"),
            new FieldMapping("damageSides",               "DamageSides"),
            new FieldMapping("damageEnergyBonus",         "DamageEnergyBonus"),
            new FieldMapping("ignoreBlock",               "IgnoreBlock"),
            new FieldMapping("damageSelf",                "DamageSelf"),
            new FieldMapping("damageSpecialValueGlobal",  "DamageSpecialValueGlobal"),
            new FieldMapping("damageSpecialValue1",       "DamageSpecialValue1"),
            new FieldMapping("damageSpecialValue2",       "DamageSpecialValue2"),

            // ── Damage 2 ─────────────────────────────────────────
            new FieldMapping("damage2",                     "Damage2"),
            new FieldMapping("damageType2",                 "DamageType2"),
            new FieldMapping("damageSides2",                "DamageSides2"),
            new FieldMapping("ignoreBlock2",                "IgnoreBlock2"),
            new FieldMapping("damageSelf2",                 "DamageSelf2"),
            new FieldMapping("damage2SpecialValueGlobal",   "Damage2SpecialValueGlobal"),
            new FieldMapping("damage2SpecialValue1",        "Damage2SpecialValue1"),
            new FieldMapping("damage2SpecialValue2",        "Damage2SpecialValue2"),

            // ── Self HP Loss ─────────────────────────────────────
            new FieldMapping("selfHealthLoss",                "SelfHealthLoss"),
            new FieldMapping("selfHealthLossSpecialGlobal",   "SelfHealthLossSpecialGlobal"),
            new FieldMapping("selfHealthLossSpecialValue1",   "SelfHealthLossSpecialValue1"),
            new FieldMapping("selfHealthLossSpecialValue2",   "SelfHealthLossSpecialValue2"),
            new FieldMapping("selfKillHiddenSeconds",         "SelfKillHiddenSeconds"),

            // ── Debuff Conversion ─────────────────────────────────
            new FieldMapping("convertAllDebuffsIntoCurse",    "ConvertAllDebuffsIntoCurse"),

            // ── Curses (target) ──────────────────────────────────
            new FieldMapping("curse",     "Curse",     RefType.AuraCurse),
            new FieldMapping("curseCharges",                       "CurseCharges"),
            new FieldMapping("curseChargesSides",                  "CurseChargesSides"),
            new FieldMapping("curseChargesSpecialValueGlobal",     "CurseChargesSpecialValueGlobal"),
            new FieldMapping("curseChargesSpecialValue1",          "CurseChargesSpecialValue1"),
            new FieldMapping("curseChargesSpecialValue2",          "CurseChargesSpecialValue2"),
            new FieldMapping("curseSelf",  "CurseSelf", RefType.AuraCurse),
            // CurseSelfCharges override: edge case (conditional)

            new FieldMapping("curse2",    "Curse2",    RefType.AuraCurse),
            new FieldMapping("curseCharges2",                      "Curse2Charges"),
            new FieldMapping("curseCharges2SpecialValueGlobal",    "CurseCharges2SpecialValueGlobal"),
            new FieldMapping("curseCharges2SpecialValue1",         "CurseCharges2SpecialValue1"),
            new FieldMapping("curseCharges2SpecialValue2",         "CurseCharges2SpecialValue2"),
            new FieldMapping("curseSelf2", "CurseSelf2", RefType.AuraCurse),

            new FieldMapping("curse3",    "Curse3",    RefType.AuraCurse),
            new FieldMapping("curseCharges3",                      "Curse3Charges"),
            new FieldMapping("curseCharges3SpecialValueGlobal",    "CurseCharges3SpecialValueGlobal"),
            new FieldMapping("curseCharges3SpecialValue1",         "CurseCharges3SpecialValue1"),
            new FieldMapping("curseCharges3SpecialValue2",         "CurseCharges3SpecialValue2"),
            new FieldMapping("curseSelf3", "CurseSelf3", RefType.AuraCurse),

            // ── Auras (target) ───────────────────────────────────
            new FieldMapping("aura",      "Aura",      RefType.AuraCurse),
            new FieldMapping("auraCharges",                        "AuraCharges"),
            new FieldMapping("auraChargesSpecialValueGlobal",      "AuraChargesSpecialValueGlobal"),
            new FieldMapping("auraChargesSpecialValue1",           "AuraChargesSpecialValue1"),
            new FieldMapping("auraChargesSpecialValue2",           "AuraChargesSpecialValue2"),
            new FieldMapping("auraSelf",   "AuraSelf",  RefType.AuraCurse),

            new FieldMapping("aura2",     "Aura2",     RefType.AuraCurse),
            new FieldMapping("auraCharges2",                       "Aura2Charges"),
            new FieldMapping("auraCharges2SpecialValueGlobal",     "AuraCharges2SpecialValueGlobal"),
            new FieldMapping("auraCharges2SpecialValue1",          "AuraCharges2SpecialValue1"),
            new FieldMapping("auraCharges2SpecialValue2",          "AuraCharges2SpecialValue2"),
            new FieldMapping("auraSelf2",  "AuraSelf2", RefType.AuraCurse),

            new FieldMapping("aura3",     "Aura3",     RefType.AuraCurse),
            new FieldMapping("auraCharges3",                       "Aura3Charges"),
            new FieldMapping("auraCharges3SpecialValueGlobal",     "AuraCharges3SpecialValueGlobal"),
            new FieldMapping("auraCharges3SpecialValue1",          "AuraCharges3SpecialValue1"),
            new FieldMapping("auraCharges3SpecialValue2",          "AuraCharges3SpecialValue2"),
            new FieldMapping("auraSelf3",  "AuraSelf3", RefType.AuraCurse),

            // ── Heal ─────────────────────────────────────────────
            new FieldMapping("heal",                           "Heal"),
            new FieldMapping("healSides",                      "HealSides"),
            new FieldMapping("healSelf",                       "HealSelf"),
            new FieldMapping("healEnergyBonus",                "HealEnergyBonus"),
            new FieldMapping("healSelfPerDamageDonePercent",   "HealSelfPerDamageDonePercent"),
            new FieldMapping("healCurses",                     "HealCurses"),
            new FieldMapping("dispelAuras",                    "DispelAuras"),
            new FieldMapping("healSpecialValueGlobal",         "HealSpecialValueGlobal"),
            new FieldMapping("healSpecialValue1",              "HealSpecialValue1"),
            new FieldMapping("healSpecialValue2",              "HealSpecialValue2"),
            new FieldMapping("healSelfSpecialValueGlobal",     "HealSelfSpecialValueGlobal"),
            new FieldMapping("healSelfSpecialValue1",          "HealSelfSpecialValue1"),
            new FieldMapping("healSelfSpecialValue2",          "HealSelfSpecialValue2"),
            new FieldMapping("healAuraCurseSelf",  "HealAuraCurseSelf",  RefType.AuraCurse),
            new FieldMapping("healAuraCurseName",  "HealAuraCurseName",  RefType.AuraCurse),
            new FieldMapping("healAuraCurseName2", "HealAuraCurseName2", RefType.AuraCurse),
            new FieldMapping("healAuraCurseName3", "HealAuraCurseName3", RefType.AuraCurse),
            new FieldMapping("healAuraCurseName4", "HealAuraCurseName4", RefType.AuraCurse),

            // ── AC Manipulation ──────────────────────────────────
            new FieldMapping("transferCurses",  "TransferCurses"),
            new FieldMapping("stealAuras",      "StealAuras"),
            new FieldMapping("reduceCurses",    "ReduceCurses"),
            new FieldMapping("reduceAuras",     "ReduceAuras"),
            new FieldMapping("increaseCurses",  "IncreaseCurses"),
            new FieldMapping("increaseAuras",   "IncreaseAuras"),

            // ── Effect Repeat ────────────────────────────────────
            new FieldMapping("effectRepeat",            "EffectRepeat"),
            new FieldMapping("effectRepeatDelay",       "EffectRepeatDelay"),
            new FieldMapping("effectRepeatEnergyBonus", "EffectRepeatEnergyBonus"),
            new FieldMapping("effectRepeatMaxBonus",    "EffectRepeatMaxBonus"),
            new FieldMapping("effectRepeatModificator", "EffectRepeatModificator"),
            new FieldMapping("effectRepeatTarget",      "EffectRepeatTarget"),

            // ── Misc Mechanics ───────────────────────────────────
            new FieldMapping("moveToCenter",                  "MoveToCenter"),
            new FieldMapping("pushTarget",                    "PushTarget"),
            new FieldMapping("pullTarget",                    "PullTarget"),
            new FieldMapping("drawCard",                      "DrawCard"),
            new FieldMapping("drawCardSpecialValueGlobal",    "DrawCardSpecialValueGlobal"),
            new FieldMapping("discardCard",                   "DiscardCard"),
            new FieldMapping("energyRecharge",                "EnergyRecharge"),
            new FieldMapping("energyRechargeSpecialValueGlobal","EnergyRechargeSpecialValueGlobal"),
            new FieldMapping("goldGainQuantity",              "GoldGainQuantity"),
            new FieldMapping("shardsGainQuantity",            "ShardsGainQuantity"),
            new FieldMapping("exhaustCounter",                "ExhaustCounter"),
            new FieldMapping("effectRequired",                "EffectRequired"),

            // ── Discard Options ──────────────────────────────────
            new FieldMapping("discardCardType",       "DiscardCardType"),
            // discardCardTypeAux: array — edge case
            new FieldMapping("discardCardAutomatic",  "DiscardCardAutomatic"),
            new FieldMapping("discardCardPlace",      "DiscardCardPlace"),

            // ── Add Card ─────────────────────────────────────────
            new FieldMapping("addCard",                  "AddCard"),
            new FieldMapping("addCardId",                "AddCardId"),
            new FieldMapping("addCardType",              "AddCardType"),
            // addCardTypeAux: array — edge case
            new FieldMapping("addCardChoose",            "AddCardChoose"),
            new FieldMapping("addCardFrom",              "AddCardFrom"),
            new FieldMapping("addCardPlace",             "AddCardPlace"),
            new FieldMapping("addCardReducedCost",       "AddCardReducedCost"),
            new FieldMapping("addCardCostTurn",          "AddCardCostTurn"),
            new FieldMapping("addCardVanish",            "AddCardVanish"),
            new FieldMapping("addCardOnlyCheckAuxTypes", "AddCardOnlyCheckAuxTypes"),
            new FieldMapping("addVanishToDeck",          "AddVanishToDeck"),
            // AddCardFromVanishPile: public prop — edge case
            // addCardList: CardData[] list — edge case

            // ── Look / Scry ─────────────────────────────────────
            new FieldMapping("lookCards",              "LookCards"),
            new FieldMapping("lookCardsDiscardUpTo",   "LookCardsDiscardUpTo"),
            new FieldMapping("lookCardsVanishUpTo",    "LookCardsVanishUpTo"),

            // ── Summon ───────────────────────────────────────────
            new FieldMapping("summonUnit",          "SummonUnitId", RefType.NPC),
            new FieldMapping("summonUnitNum",       "SummonNum"),
            new FieldMapping("evolve",              "Evolve"),
            new FieldMapping("metamorph",           "Metamorph"),
            new FieldMapping("summonAura",          "SummonAura",          RefType.AuraCurse),
            new FieldMapping("summonAuraCharges",   "SummonAuraCharges"),
            new FieldMapping("summonAura2",         "SummonAura2",         RefType.AuraCurse),
            new FieldMapping("summonAuraCharges2",  "SummonAuraCharges2"),
            new FieldMapping("summonAura3",         "SummonAura3",         RefType.AuraCurse),
            new FieldMapping("summonAuraCharges3",  "SummonAuraCharges3"),

            // ── AC Energy Bonus ──────────────────────────────────
            new FieldMapping("acEnergyBonus",           "AcEnergyBonus",     RefType.AuraCurse),
            new FieldMapping("acEnergyBonusQuantity",   "AcEnergyBonusQuantity"),
            new FieldMapping("acEnergyBonus2",          "AcEnergyBonus2",    RefType.AuraCurse),
            new FieldMapping("acEnergyBonus2Quantity",  "AcEnergyBonus2Quantity"),
            new FieldMapping("chooseOneOfAvailableAuras","ChooseOneOfAvailableAuras"),

            // ── Special Value System ─────────────────────────────
            new FieldMapping("specialValueGlobal",           "SpecialValueGlobal"),
            new FieldMapping("specialValueModifierGlobal",   "SpecialValueModifierGlobal"),
            new FieldMapping("specialAuraCurseNameGlobal",   "SpecialAuraCurseNameGlobal", RefType.AuraCurse),
            new FieldMapping("specialValue1",                "SpecialValue1"),
            new FieldMapping("specialValueModifier1",        "SpecialValueModifier1"),
            new FieldMapping("specialAuraCurseName1",        "SpecialAuraCurseName1",      RefType.AuraCurse),
            new FieldMapping("specialValue2",                "SpecialValue2"),
            new FieldMapping("specialValueModifier2",        "SpecialValueModifier2"),
            new FieldMapping("specialAuraCurseName2",        "SpecialAuraCurseName2",      RefType.AuraCurse),

            // ── FX / Effects ─────────────────────────────────────
            new FieldMapping("effectCaster",          "EffectCaster"),
            new FieldMapping("effectTarget",          "EffectTarget"),
            new FieldMapping("effectPreAction",       "EffectPreAction"),
            new FieldMapping("effectPostCastDelay",   "EffectPostCastDelay"),
            new FieldMapping("effectCasterRepeat",    "EffectCasterRepeat"),
            new FieldMapping("effectCastCenter",      "EffectCastCenter"),
            new FieldMapping("effectTrail",           "EffectTrail"),
            new FieldMapping("effectTrailRepeat",     "EffectTrailRepeat"),
            new FieldMapping("effectTrailSpeed",      "EffectTrailSpeed"),
            new FieldMapping("effectTrailAngle",      "EffectTrailAngle"),
            new FieldMapping("effectPostTargetDelay", "EffectPostTargetDelay"),

            // ── Pet System ───────────────────────────────────────
            new FieldMapping("isPetAttack",                "IsPetAttack"),
            new FieldMapping("isPetCast",                  "IsPetCast"),
            new FieldMapping("killPet",                    "KillPet"),
            new FieldMapping("petTemporal",                "PetTemporal"),
            new FieldMapping("petTemporalAttack",          "PetTemporalAttack"),
            new FieldMapping("petTemporalCast",            "PetTemporalCast"),
            new FieldMapping("petTemporalMoveToCenter",    "PetTemporalMoveToCenter"),
            new FieldMapping("petTemporalMoveToBack",      "PetTemporalMoveToBack"),
            new FieldMapping("petTemporalFadeOutDelay",    "PetTemporalFadeOutDelay"),
            new FieldMapping("petFront",                   "PetFront"),
            new FieldMapping("petInvert",                  "PetInvert"),
            // petOffset, petSize: Vector2 ↔ 2 floats — edge case
            // PetActivation, PetBonusDamageType, PetBonusDamageAmount: public props — edge case
        };
    }
}
