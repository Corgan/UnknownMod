namespace UnknownMod.Core
{
    public static partial class FieldMappings
    {
        // ─────────────────────────────────────────────────────────
        //  ITEM  (SO fields = PascalCase public, some camelCase private)
        // ─────────────────────────────────────────────────────────

        public static readonly FieldMapping[] Item = new[]
        {
            // ── Identity ─────────────────────────────────────────
            new FieldMapping("Id", "Id"),

            // ── Activation / Requisite ───────────────────────────
            new FieldMapping("Activation",              "Activation"),
            new FieldMapping("activationManual",         "ActivationManual"),
            new FieldMapping("activationOnlyOnHeroes",  "ActivationOnlyOnHeroes"),  // private field
            new FieldMapping("activateOnReceive",        "ActivateOnReceive"),       // private field
            new FieldMapping("preventApplyForHeroTarget","PreventApplyForHeroTarget"), // private field
            new FieldMapping("ItemTarget",              "ItemTarget"),
            new FieldMapping("overrideTargetText",       "OverrideTargetText"),      // private field
            new FieldMapping("dontTargetBoss",          "DontTargetBoss"),           // private field
            new FieldMapping("TimesPerTurn",            "TimesPerTurn"),
            new FieldMapping("TimesPerCombat",          "TimesPerCombat"),
            new FieldMapping("ExactRound",              "ExactRound"),
            new FieldMapping("RoundCycle",              "RoundCycle"),
            new FieldMapping("AuraCurseSetted",         "AuraCurseSetted",  RefType.AuraCurse),
            new FieldMapping("AuraCurseSetted2",        "AuraCurseSetted2", RefType.AuraCurse),
            new FieldMapping("AuraCurseSetted3",        "AuraCurseSetted3", RefType.AuraCurse),
            new FieldMapping("AuraCurseNumForOneEvent", "AuraCurseNumForOneEvent"),
            new FieldMapping("CastedCardType",          "CastedCardType"),
            new FieldMapping("usedEnergy",              "UsedEnergy"),              // private field
            new FieldMapping("LowerOrEqualPercentHP",   "LowerOrEqualPercentHP"),
            new FieldMapping("EmptyHand",               "EmptyHand"),
            new FieldMapping("notShowCharacterBonus",   "NotShowCharacterBonus"),   // private field
            new FieldMapping("PetActivation",           "PetActivation"),

            // ── Damage Bonuses ───────────────────────────────────
            new FieldMapping("DamageFlatBonus",         "DamageFlatBonus"),
            new FieldMapping("DamageFlatBonusValue",    "DamageFlatBonusValue"),
            new FieldMapping("DamageFlatBonus2",        "DamageFlatBonus2"),
            new FieldMapping("DamageFlatBonusValue2",   "DamageFlatBonusValue2"),
            new FieldMapping("DamageFlatBonus3",        "DamageFlatBonus3"),
            new FieldMapping("DamageFlatBonusValue3",   "DamageFlatBonusValue3"),
            new FieldMapping("DamagePercentBonus",      "DamagePercentBonus"),
            new FieldMapping("DamagePercentBonusValue", "DamagePercentBonusValue"),
            new FieldMapping("DamagePercentBonus2",     "DamagePercentBonus2"),
            new FieldMapping("DamagePercentBonusValue2","DamagePercentBonusValue2"),
            new FieldMapping("DamagePercentBonus3",     "DamagePercentBonus3"),
            new FieldMapping("DamagePercentBonusValue3","DamagePercentBonusValue3"),

            // ── Resist Bonuses ───────────────────────────────────
            new FieldMapping("ResistModified1",      "ResistModified1"),
            new FieldMapping("ResistModifiedValue1", "ResistModifiedValue1"),
            new FieldMapping("ResistModified2",      "ResistModified2"),
            new FieldMapping("ResistModifiedValue2", "ResistModifiedValue2"),
            new FieldMapping("ResistModified3",      "ResistModified3"),
            new FieldMapping("ResistModifiedValue3", "ResistModifiedValue3"),

            // ── Character Stat ───────────────────────────────────
            new FieldMapping("CharacterStatModified",       "CharacterStatModified"),
            new FieldMapping("CharacterStatModifiedValue",  "CharacterStatModifiedValue"),
            new FieldMapping("CharacterStatModified2",      "CharacterStatModified2"),
            new FieldMapping("CharacterStatModifiedValue2", "CharacterStatModifiedValue2"),
            new FieldMapping("CharacterStatModified3",      "CharacterStatModified3"),
            new FieldMapping("CharacterStatModifiedValue3", "CharacterStatModifiedValue3"),
            new FieldMapping("MaxHealth",                   "MaxHealth"),

            // ── Heal Bonuses ─────────────────────────────────────
            new FieldMapping("HealFlatBonus",           "HealFlatBonus"),
            new FieldMapping("HealPercentBonus",        "HealPercentBonus"),
            new FieldMapping("HealReceivedFlatBonus",   "HealReceivedFlatBonus"),
            new FieldMapping("HealReceivedPercentBonus","HealReceivedPercentBonus"),
            new FieldMapping("HealQuantity",            "HealQuantity"),
            // HealQuantitySpecialValue: SpecialValue struct — edge case
            new FieldMapping("HealPercentQuantity",     "HealPercentQuantity"),
            new FieldMapping("HealPercentQuantitySelf",         "HealPercentQuantitySelf"),
            new FieldMapping("HealSelfPerDamageDonePercent",    "HealSelfPerDamageDonePercent"),
            new FieldMapping("HealSelfTeamPerDamageDonePercent","HealSelfTeamPerDamageDonePercent"),
            new FieldMapping("healBasedOnAuraCurse",            "HealBasedOnAuraCurse"), // private field

            // ── Energy / Draw ────────────────────────────────────
            new FieldMapping("EnergyQuantity",          "EnergyQuantity"),
            new FieldMapping("DrawCards",               "DrawCards"),
            new FieldMapping("DrawMultiplyByEnergyUsed","DrawMultiplyByEnergyUsed"),

            // ── AC Gain (target) ─────────────────────────────────
            new FieldMapping("AuracurseGain1",      "AuracurseGain1", RefType.AuraCurse),
            new FieldMapping("AuracurseGainValue1", "AuracurseGainValue1"),
            // AuracurseGain1SpecialValue: SpecialValue struct — edge case
            new FieldMapping("Acg1MultiplyByEnergyUsed","Acg1MultiplyByEnergyUsed"),
            new FieldMapping("AuracurseGain2",      "AuracurseGain2", RefType.AuraCurse),
            new FieldMapping("AuracurseGainValue2", "AuracurseGainValue2"),
            // AuracurseGain2SpecialValue: SpecialValue struct — edge case
            new FieldMapping("Acg2MultiplyByEnergyUsed","Acg2MultiplyByEnergyUsed"),
            new FieldMapping("AuracurseGain3",      "AuracurseGain3", RefType.AuraCurse),
            new FieldMapping("AuracurseGainValue3", "AuracurseGainValue3"),
            // AuracurseGain3SpecialValue: SpecialValue struct — edge case
            new FieldMapping("Acg3MultiplyByEnergyUsed","Acg3MultiplyByEnergyUsed"),
            new FieldMapping("ChooseOneACToGain",       "ChooseOneACToGain"),

            // ── AC Gain (self) ───────────────────────────────────
            new FieldMapping("AuracurseGainSelf1",      "AuracurseGainSelf1", RefType.AuraCurse),
            new FieldMapping("AuracurseGainSelfValue1", "AuracurseGainSelfValue1"),
            new FieldMapping("AuracurseGainSelf2",      "AuracurseGainSelf2", RefType.AuraCurse),
            new FieldMapping("AuracurseGainSelfValue2", "AuracurseGainSelfValue2"),
            new FieldMapping("AuracurseGainSelf3",      "AuracurseGainSelf3", RefType.AuraCurse),
            new FieldMapping("AuracurseGainSelfValue3", "AuracurseGainSelfValue3"),

            // ── Dispel / Purge ───────────────────────────────────
            new FieldMapping("AuracurseHeal1",       "AuracurseHeal1", RefType.AuraCurse),
            new FieldMapping("AuracurseHeal2",       "AuracurseHeal2", RefType.AuraCurse),
            new FieldMapping("AuracurseHeal3",       "AuracurseHeal3", RefType.AuraCurse),
            new FieldMapping("AcHealFromTarget",     "AcHealFromTarget"),
            new FieldMapping("StealAuras",           "StealAuras"),
            new FieldMapping("ChanceToDispel",       "ChanceToDispel"),
            new FieldMapping("ChanceToDispelNum",    "ChanceToDispelNum"),
            new FieldMapping("ChanceToPurge",        "ChanceToPurge"),
            new FieldMapping("ChanceToPurgeNum",     "ChanceToPurgeNum"),
            new FieldMapping("ChanceToDispelSelf",   "ChanceToDispelSelf"),
            new FieldMapping("ChanceToDispelNumSelf","ChanceToDispelNumSelf"),

            // ── Passive AC Bonuses ───────────────────────────────
            new FieldMapping("AuracurseBonus1",      "AuracurseBonus1", RefType.AuraCurse),
            new FieldMapping("AuracurseBonusValue1", "AuracurseBonusValue1"),
            new FieldMapping("AuracurseBonus2",      "AuracurseBonus2", RefType.AuraCurse),
            new FieldMapping("AuracurseBonusValue2", "AuracurseBonusValue2"),
            new FieldMapping("IncreaseAurasSelf",    "IncreaseAurasSelf"),

            // ── AC Immunities ────────────────────────────────────
            new FieldMapping("AuracurseImmune1", "AuracurseImmune1", RefType.AuraCurse),
            new FieldMapping("AuracurseImmune2", "AuracurseImmune2", RefType.AuraCurse),

            // ── Card Gain ────────────────────────────────────────
            new FieldMapping("CardNum",         "CardNum"),
            new FieldMapping("CardToGain",      "CardToGain",     RefType.Card),
            new FieldMapping("CardToGainType",  "CardToGainType"),
            new FieldMapping("CardPlace",       "CardPlace"),
            // CardToGainList: List<CardData> — edge case

            // ── Cost / Economy ───────────────────────────────────
            new FieldMapping("CostZero",                     "CostZero"),
            new FieldMapping("CostReduction",                "CostReduction"),
            new FieldMapping("CardsReduced",                 "CardsReduced"),
            new FieldMapping("CardToReduceType",             "CardToReduceType"),
            new FieldMapping("CostReduceReduction",          "CostReduceReduction"),
            new FieldMapping("CostReduceEnergyRequirement",  "CostReduceEnergyRequirement"),
            new FieldMapping("CostReducePermanent",          "CostReducePermanent"),
            new FieldMapping("ReduceHighestCost",            "ReduceHighestCost"),

            // ── Rewards / Discounts ──────────────────────────────
            new FieldMapping("PercentRetentionEndGame", "PercentRetentionEndGame"),
            new FieldMapping("PercentDiscountShop",     "PercentDiscountShop"),

            // ── Damage To Target ─────────────────────────────────
            new FieldMapping("DamageToTarget1",       "DamageToTarget"),
            new FieldMapping("damageToTargetType",    "DamageToTargetType"),    // private field
            new FieldMapping("DttMultiplyByEnergyUsed","DttMultiplyByEnergyUsed"),
            // DttSpecialValues1: SpecialValue struct — edge case
            new FieldMapping("DamageToTarget2",       "DamageToTarget2"),
            new FieldMapping("damageToTargetType2",   "DamageToTargetType2"),   // private field
            // DttSpecialValues2: SpecialValue struct — edge case
            new FieldMapping("ModifiedDamageType",    "ModifiedDamageType"),

            // ── Flags ────────────────────────────────────────────
            new FieldMapping("CursedItem",                  "CursedItem"),
            new FieldMapping("DropOnly",                    "DropOnly"),
            new FieldMapping("QuestItem",                   "QuestItem"),
            new FieldMapping("DestroyAfterUse",             "DestroyAfterUse"),
            new FieldMapping("Vanish",                      "Vanish"),
            new FieldMapping("Permanent",                   "Permanent"),
            new FieldMapping("DuplicateActive",             "DuplicateActive"),
            new FieldMapping("PassSingleAndCharacterRolls", "PassSingleAndCharacterRolls"),
            new FieldMapping("OnlyAddItemToNPCs",           "OnlyAddItemToNPCs"),
            new FieldMapping("AddVanishToDeck",             "AddVanishToDeck"),

            // ── Enchantment ──────────────────────────────────────
            new FieldMapping("IsEnchantment",                    "IsEnchantment"),
            new FieldMapping("UseTheNextInsteadWhenYouPlay",     "UseTheNextInsteadWhenYouPlay"),
            new FieldMapping("DestroyAfterUses",                 "DestroyAfterUses"),
            new FieldMapping("DestroyStartOfTurn",               "DestroyStartOfTurn"),
            new FieldMapping("DestroyEndOfTurn",                 "DestroyEndOfTurn"),
            new FieldMapping("CastEnchantmentOnFinishSelfCast",  "CastEnchantmentOnFinishSelfCast"),

            // ── Custom AC ────────────────────────────────────────
            new FieldMapping("AuracurseCustomString",    "AuracurseCustomString"),
            new FieldMapping("AuracurseCustomAC",        "AuracurseCustomAC", RefType.AuraCurse),
            new FieldMapping("AuracurseCustomModValue1", "AuracurseCustomModValue1"),
            new FieldMapping("AuracurseCustomModValue2", "AuracurseCustomModValue2"),

            // ── FX / Effects ─────────────────────────────────────
            new FieldMapping("EffectItemOwner",  "EffectItemOwner"),
            new FieldMapping("EffectCaster",     "EffectCaster"),
            new FieldMapping("EffectCasterDelay","EffectCasterDelay"),
            new FieldMapping("EffectTarget",     "EffectTarget"),
            new FieldMapping("EffectTargetDelay","EffectTargetDelay"),

            // ── Debuff Conversion ───────────────────────────────
            new FieldMapping("convertReceivedDebuffsIntoDamage","ConvertReceivedDebuffsIntoDamage"), // private field
            new FieldMapping("convertReceivedDebuffsIntoCurse", "ConvertReceivedDebuffsIntoCurse"),  // private field
        };
    }
}
