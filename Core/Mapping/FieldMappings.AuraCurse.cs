namespace UnknownMod.Core
{
    public static partial class FieldMappings
    {
        // ─────────────────────────────────────────────────────────
        //  AURA / CURSE  (SO fields = PascalCase public, except "id")
        // ─────────────────────────────────────────────────────────

        public static readonly FieldMapping[] AuraCurse = new[]
        {
            // ── Identity ─────────────────────────────────────────
            new FieldMapping("id",          "Id"),
            new FieldMapping("ACName",      "ACName"),
            new FieldMapping("IsAura",      "IsAura"),
            new FieldMapping("Description", "Description"),
            new FieldMapping("MaxCharges",  "MaxCharges"),
            new FieldMapping("MaxMadnessCharges","MaxMadnessCharges"),
            new FieldMapping("AuraConsumed","AuraConsumed"),
            new FieldMapping("ChargesMultiplierDescription","ChargesMultiplierDescription"),
            new FieldMapping("ChargesAuxNeedForOne1","ChargesAuxNeedForOne1"),
            new FieldMapping("ChargesAuxNeedForOne2","ChargesAuxNeedForOne2"),
            // Sprite: asset name — edge case (Snapshot only)
            new FieldMapping("EffectTick",      "EffectTick"),
            new FieldMapping("EffectTickSides",  "EffectTickSides"),
            // Sound, SoundRework: asset names — edge case (Snapshot only)

            // ── Config ───────────────────────────────────────────
            new FieldMapping("Removable",       "Removable"),
            new FieldMapping("GainCharges",     "GainCharges"),
            new FieldMapping("IconShow",        "IconShow"),
            new FieldMapping("CombatlogShow",   "CombatlogShow"),
            new FieldMapping("Preventable",     "Preventable"),
            new FieldMapping("CanBeAddedToImmunityDespiteNotBeingPreventable",
                             "CanBeAddedToImmunityDespiteNotBeingPreventable"),

            // ── Expiration ───────────────────────────────────────
            new FieldMapping("PriorityOnConsumption",      "PriorityOnConsumption"),
            new FieldMapping("ConsumeAll",                 "ConsumeAll"),
            new FieldMapping("ConsumedAtCast",             "ConsumedAtCast"),
            new FieldMapping("ConsumedAtTurnBegin",        "ConsumedAtTurnBegin"),
            new FieldMapping("ConsumedAtTurn",             "ConsumedAtTurn"),
            new FieldMapping("ConsumedAtRoundBegin",       "ConsumedAtRoundBegin"),
            new FieldMapping("ConsumedAtRound",            "ConsumedAtRound"),
            new FieldMapping("ProduceDamageWhenConsumed",  "ProduceDamageWhenConsumed"),
            new FieldMapping("ProduceHealWhenConsumed",    "ProduceHealWhenConsumed"),
            new FieldMapping("DieWhenConsumedAll",         "DieWhenConsumedAll"),

            // ── Aura Damage Bonus (slot 1) ───────────────────────
            new FieldMapping("AuraDamageType",                              "AuraDamageType"),
            new FieldMapping("AuraDamageIncreasedTotal",                    "AuraDamageIncreasedTotal"),
            new FieldMapping("AuraDamageIncreasedPerStack",                 "AuraDamageIncreasedPerStack"),
            new FieldMapping("AuraDamageIncreasedPercent",                  "AuraDamageIncreasedPercent"),
            new FieldMapping("AuraDamageIncreasedPercentPerStack",          "AuraDamageIncreasedPercentPerStack"),
            new FieldMapping("AuraDamageIncreasedPercentPerStackPerEnergy", "AuraDamageIncreasedPercentPerStackPerEnergy"),
            new FieldMapping("AuraDamageChargesBasedOnACCharges",           "AuraDamageChargesBasedOnACCharges", RefType.AuraCurse),

            // ── Aura Damage Bonus (slot 2) ───────────────────────
            new FieldMapping("AuraDamageType2",                              "AuraDamageType2"),
            new FieldMapping("AuraDamageIncreasedTotal2",                    "AuraDamageIncreasedTotal2"),
            new FieldMapping("AuraDamageIncreasedPerStack2",                 "AuraDamageIncreasedPerStack2"),
            new FieldMapping("AuraDamageIncreasedPercent2",                  "AuraDamageIncreasedPercent2"),
            new FieldMapping("AuraDamageIncreasedPercentPerStack2",          "AuraDamageIncreasedPercentPerStack2"),
            new FieldMapping("AuraDamageIncreasedPercentPerStackPerEnergy2", "AuraDamageIncreasedPercentPerStackPerEnergy2"),

            // ── Aura Damage Bonus (slot 3) ───────────────────────
            new FieldMapping("AuraDamageType3",                              "AuraDamageType3"),
            new FieldMapping("AuraDamageIncreasedTotal3",                    "AuraDamageIncreasedTotal3"),
            new FieldMapping("AuraDamageIncreasedPerStack3",                 "AuraDamageIncreasedPerStack3"),
            new FieldMapping("AuraDamageIncreasedPercent3",                  "AuraDamageIncreasedPercent3"),
            new FieldMapping("AuraDamageIncreasedPercentPerStack3",          "AuraDamageIncreasedPercentPerStack3"),
            new FieldMapping("AuraDamageIncreasedPercentPerStackPerEnergy3", "AuraDamageIncreasedPercentPerStackPerEnergy3"),

            // ── Aura Damage Bonus (slot 4) ───────────────────────
            new FieldMapping("AuraDamageType4",                              "AuraDamageType4"),
            new FieldMapping("AuraDamageIncreasedTotal4",                    "AuraDamageIncreasedTotal4"),
            new FieldMapping("AuraDamageIncreasedPerStack4",                 "AuraDamageIncreasedPerStack4"),
            new FieldMapping("AuraDamageIncreasedPercent4",                  "AuraDamageIncreasedPercent4"),
            new FieldMapping("AuraDamageIncreasedPercentPerStack4",          "AuraDamageIncreasedPercentPerStack4"),
            new FieldMapping("AuraDamageIncreasedPercentPerStackPerEnergy4", "AuraDamageIncreasedPercentPerStackPerEnergy4"),

            // ── Heal Bonus ───────────────────────────────────────
            new FieldMapping("HealDoneTotal",                   "HealDoneTotal"),
            new FieldMapping("HealDonePerStack",                "HealDonePerStack"),
            new FieldMapping("HealDonePercent",                 "HealDonePercent"),
            new FieldMapping("HealDonePercentPerStack",         "HealDonePercentPerStack"),
            new FieldMapping("HealDonePercentPerStackPerEnergy","HealDonePercentPerStackPerEnergy"),
            new FieldMapping("HealReceivedTotal",               "HealReceivedTotal"),
            new FieldMapping("HealReceivedPerStack",            "HealReceivedPerStack"),
            new FieldMapping("HealReceivedPercent",             "HealReceivedPercent"),
            new FieldMapping("HealReceivedPercentPerStack",     "HealReceivedPercentPerStack"),

            // ── Draw ─────────────────────────────────────────────
            new FieldMapping("CardsDrawPerStack", "CardsDrawPerStack"),

            // ── Damage Reflected ─────────────────────────────────
            new FieldMapping("ChargesPreReqForDamageReflection","ChargesPreReqForDamageReflection"),
            new FieldMapping("DamageReflectedModifierType",     "DamageReflectedModifierType"),
            new FieldMapping("DamageReflectedMultiplier",       "DamageReflectedMultiplier"),
            new FieldMapping("DamageReflectedType",             "DamageReflectedType"),
            new FieldMapping("DamageReflectedConsumeCharges",   "DamageReflectedConsumeCharges"),

            // ── Block ────────────────────────────────────────────
            new FieldMapping("BlockChargesGainedPerStack",   "BlockChargesGainedPerStack"),
            new FieldMapping("NoRemoveBlockAtTurnEnd",       "NoRemoveBlockAtTurnEnd"),
            new FieldMapping("GrantBlockToTeamForAmountOfDamageBlocked",
                             "GrantBlockToTeamForAmountOfDamageBlocked"),
            new FieldMapping("ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked",
                             "ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked"),

            // ── Prevention ───────────────────────────────────────
            new FieldMapping("DamagePreventedPerStack",          "DamagePreventedPerStack"),
            new FieldMapping("CursePreventedPerStack",           "CursePreventedPerStack"),
            new FieldMapping("PreventedAuraCurse",               "PreventedAuraCurse", RefType.AuraCurse),
            new FieldMapping("PreventedAuraCurseStackPerStack",  "PreventedAuraCurseStackPerStack"),

            // ── Damage Received (slot 1) ─────────────────────────
            new FieldMapping("IncreasedDamageReceivedType",                       "IncreasedDamageReceivedType"),
            new FieldMapping("IncreasedDirectDamageChargesMultiplierNeededForOne", "IncreasedDirectDamageChargesMultiplierNeededForOne"),
            new FieldMapping("IncreasedDirectDamageReceivedPerTurn",              "IncreasedDirectDamageReceivedPerTurn"),
            new FieldMapping("IncreasedDirectDamageReceivedPerStack",             "IncreasedDirectDamageReceivedPerStack"),
            new FieldMapping("IncreasedPercentDamageReceivedPerTurn",             "IncreasedPercentDamageReceivedPerTurn"),
            new FieldMapping("IncreasedPercentDamageReceivedPerStack",            "IncreasedPercentDamageReceivedPerStack"),

            // ── Damage Received (slot 2) ─────────────────────────
            new FieldMapping("IncreasedDamageReceivedType2",                       "IncreasedDamageReceivedType2"),
            new FieldMapping("IncreasedDirectDamageChargesMultiplierNeededForOne2", "IncreasedDirectDamageChargesMultiplierNeededForOne2"),
            new FieldMapping("IncreasedDirectDamageReceivedPerTurn2",              "IncreasedDirectDamageReceivedPerTurn2"),
            new FieldMapping("IncreasedDirectDamageReceivedPerStack2",             "IncreasedDirectDamageReceivedPerStack2"),
            new FieldMapping("IncreasedPercentDamageReceivedPerTurn2",             "IncreasedPercentDamageReceivedPerTurn2"),
            new FieldMapping("IncreasedPercentDamageReceivedPerStack2",            "IncreasedPercentDamageReceivedPerStack2"),

            // ── Damage Prevented ─────────────────────────────────
            new FieldMapping("PreventedDamageTypePerStack", "PreventedDamageTypePerStack"),
            new FieldMapping("PreventedDamagePerStack",     "PreventedDamagePerStack"),

            // ── Heal Attacker ────────────────────────────────────
            new FieldMapping("HealAttackerPerStack",       "HealAttackerPerStack"),
            new FieldMapping("HealAttackerConsumeCharges", "HealAttackerConsumeCharges"),

            // ── Character Stat ───────────────────────────────────
            new FieldMapping("CharacterStatModified",                       "CharacterStatModified"),
            new FieldMapping("CharacterStatChargesMultiplierNeededForOne",   "CharacterStatChargesMultiplierNeededForOne"),
            new FieldMapping("CharacterStatModifiedValue",                  "CharacterStatModifiedValue"),
            new FieldMapping("CharacterStatModifiedValuePerStack",          "CharacterStatModifiedValuePerStack"),
            new FieldMapping("CharacterStatAbsolute",                       "CharacterStatAbsolute"),
            new FieldMapping("CharacterStatAbsoluteValue",                  "CharacterStatAbsoluteValue"),
            new FieldMapping("CharacterStatAbsoluteValuePerStack",          "CharacterStatAbsoluteValuePerStack"),

            // ── Resist Modification (3 slots) ────────────────────
            new FieldMapping("ResistModified",                    "ResistModified"),
            new FieldMapping("ResistModifiedValue",               "ResistModifiedValue"),
            new FieldMapping("ResistModifiedPercentagePerStack",  "ResistModifiedPercentagePerStack"),
            new FieldMapping("ResistModified2",                   "ResistModified2"),
            new FieldMapping("ResistModifiedValue2",              "ResistModifiedValue2"),
            new FieldMapping("ResistModifiedPercentagePerStack2", "ResistModifiedPercentagePerStack2"),
            new FieldMapping("ResistModified3",                   "ResistModified3"),
            new FieldMapping("ResistModifiedValue3",              "ResistModifiedValue3"),
            new FieldMapping("ResistModifiedPercentagePerStack3", "ResistModifiedPercentagePerStack3"),

            // ── Explode ──────────────────────────────────────────
            new FieldMapping("ExplodeAtStacks",                  "ExplodeAtStacks"),
            new FieldMapping("HealTotalOnExplode",               "HealTotalOnExplode"),
            new FieldMapping("HealPerChargeOnExplode",           "HealPerChargeOnExplode"),
            new FieldMapping("HealTargetOnExplode",              "HealTargetOnExplode"),
            new FieldMapping("ACOnExplode",                      "ACOnExplode",     RefType.AuraCurse),
            new FieldMapping("ACTotalChargesOnExplode",          "ACTotalChargesOnExplode"),
            new FieldMapping("ACChargesPerStackChargeOnExplode", "ACChargesPerStackChargeOnExplode"),

            // ── Consume Damage ───────────────────────────────────
            new FieldMapping("DamageTypeWhenConsumed",                "DamageTypeWhenConsumed"),
            new FieldMapping("ConsumedDamageChargesBasedOnACCharges", "ConsumedDamageChargesBasedOnACCharges", RefType.AuraCurse),
            new FieldMapping("ConsumeDamageChargesIfACApplied",       "ConsumeDamageChargesIfACApplied",       RefType.AuraCurse),
            new FieldMapping("DamageWhenConsumed",                    "DamageWhenConsumed"),
            new FieldMapping("DamageWhenConsumedPerCharge",           "DamageWhenConsumedPerCharge"),
            new FieldMapping("DamageSidesWhenConsumed",               "DamageSidesWhenConsumed"),
            new FieldMapping("DamageSidesWhenConsumedPerCharge",      "DamageSidesWhenConsumedPerCharge"),
            new FieldMapping("DoubleDamageIfCursesLessThan",          "DoubleDamageIfCursesLessThan"),

            // ── Consume Heal ─────────────────────────────────────
            new FieldMapping("HealWhenConsumed",              "HealWhenConsumed"),
            new FieldMapping("HealWhenConsumedPerCharge",     "HealWhenConsumedPerCharge"),
            new FieldMapping("HealSidesWhenConsumed",         "HealSidesWhenConsumed"),
            new FieldMapping("HealSidesWhenConsumedPerCharge","HealSidesWhenConsumedPerCharge"),

            // ── Remove AC ────────────────────────────────────────
            new FieldMapping("RemoveAuraCurse",  "RemoveAuraCurse",  RefType.AuraCurse),
            new FieldMapping("RemoveAuraCurse2", "RemoveAuraCurse2", RefType.AuraCurse),

            // ── Gain AC on Consumption ───────────────────────────
            new FieldMapping("GainAuraCurseConsumption",          "GainAuraCurseConsumption",          RefType.AuraCurse),
            new FieldMapping("GainAuraCurseConsumptionPerCharge", "GainAuraCurseConsumptionPerCharge"),
            new FieldMapping("GainChargesFromThisAuraCurse",      "GainChargesFromThisAuraCurse",      RefType.AuraCurse),
            new FieldMapping("GainAuraCurseConsumption2",         "GainAuraCurseConsumption2",         RefType.AuraCurse),
            new FieldMapping("GainAuraCurseConsumptionPerCharge2","GainAuraCurseConsumptionPerCharge2"),
            new FieldMapping("GainChargesFromThisAuraCurse2",     "GainChargesFromThisAuraCurse2",     RefType.AuraCurse),

            // ── Reveal / Cost ────────────────────────────────────
            new FieldMapping("RevealCardsPerCharge",                "RevealCardsPerCharge"),
            new FieldMapping("ModifyCardCostPerChargeNeededForOne", "ModifyCardCostPerChargeNeededForOne"),

            // ── Misc ─────────────────────────────────────────────
            new FieldMapping("Invulnerable",   "Invulnerable"),
            new FieldMapping("Stealth",        "Stealth"),
            new FieldMapping("Taunt",          "Taunt"),
            new FieldMapping("SkipsNextTurn",  "SkipsNextTurn"),
            new FieldMapping("skipEndTurnRemovalIfNoBegin", "SkipEndTurnRemovalIfNoBegin"),
            // DisabledCardTypes: array — edge case
            // ACBonusData: list of structs — edge case
            // AuraDamageConditionalBonuses: array of structs — edge case
        };
    }
}
