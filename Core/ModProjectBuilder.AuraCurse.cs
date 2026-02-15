using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class ModProjectBuilder
    {
        // ═══════════════════════════════════════════════════════════════
        //  AURA / CURSE
        // ═══════════════════════════════════════════════════════════════

        private static void BuildAuraCurses(Dictionary<string, AuraCurseDef> defs, bool isNew)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var ac = MakeAuraCurse(kvp.Value);
                    RegisterAuraCurse(ac);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build AuraCurse '{kvp.Key}': {ex.Message}");
                }
            }
        }

        /// <summary>Create an AuraCurseData SO from an AuraCurseDef DTO.</summary>
        public static AuraCurseData MakeAuraCurse(AuraCurseDef d)
        {
            var ac = ScriptableObject.CreateInstance<AuraCurseData>();
            var t = Traverse.Create(ac);

            // General
            t.Field("id").SetValue(d.Id);
            ac.ACName = d.ACName;
            ac.IsAura = d.IsAura;
            ac.Description = d.Description;
            ac.MaxCharges = d.MaxCharges;
            ac.MaxMadnessCharges = d.MaxMadnessCharges;
            ac.AuraConsumed = d.AuraConsumed;
            ac.ChargesMultiplierDescription = d.ChargesMultiplierDescription;
            ac.ChargesAuxNeedForOne1 = d.ChargesAuxNeedForOne1;
            ac.ChargesAuxNeedForOne2 = d.ChargesAuxNeedForOne2;

            // Sprite & sound (resolve at runtime — store ID for now)
            // These would need asset lookup; stub as null for now
            ac.EffectTick = d.EffectTick ?? "";
            ac.EffectTickSides = d.EffectTickSides ?? "";

            // Config
            ac.Removable = d.Removable;
            ac.GainCharges = d.GainCharges;
            ac.IconShow = d.IconShow;
            ac.CombatlogShow = d.CombatlogShow;
            ac.Preventable = d.Preventable;
            ac.CanBeAddedToImmunityDespiteNotBeingPreventable = d.CanBeAddedToImmunityDespiteNotBeingPreventable;

            // Expiration
            ac.PriorityOnConsumption = d.PriorityOnConsumption;
            ac.ConsumeAll = d.ConsumeAll;
            ac.ConsumedAtCast = d.ConsumedAtCast;
            ac.ConsumedAtTurnBegin = d.ConsumedAtTurnBegin;
            ac.ConsumedAtTurn = d.ConsumedAtTurn;
            ac.ConsumedAtRoundBegin = d.ConsumedAtRoundBegin;
            ac.ConsumedAtRound = d.ConsumedAtRound;
            ac.ProduceDamageWhenConsumed = d.ProduceDamageWhenConsumed;
            ac.ProduceHealWhenConsumed = d.ProduceHealWhenConsumed;
            ac.DieWhenConsumedAll = d.DieWhenConsumedAll;

            // Aura Damage Bonus (slot 1)
            ac.AuraDamageType = d.AuraDamageType;
            ac.AuraDamageIncreasedTotal = d.AuraDamageIncreasedTotal;
            ac.AuraDamageIncreasedPerStack = d.AuraDamageIncreasedPerStack;
            ac.AuraDamageIncreasedPercent = d.AuraDamageIncreasedPercent;
            ac.AuraDamageIncreasedPercentPerStack = d.AuraDamageIncreasedPercentPerStack;
            ac.AuraDamageIncreasedPercentPerStackPerEnergy = d.AuraDamageIncreasedPercentPerStackPerEnergy;
            if (!string.IsNullOrEmpty(d.AuraDamageChargesBasedOnACCharges))
                ac.AuraDamageChargesBasedOnACCharges = DataHelper.GetAuraCurse(d.AuraDamageChargesBasedOnACCharges);

            // Aura Damage Bonus (slot 2)
            ac.AuraDamageType2 = d.AuraDamageType2;
            ac.AuraDamageIncreasedTotal2 = d.AuraDamageIncreasedTotal2;
            ac.AuraDamageIncreasedPerStack2 = d.AuraDamageIncreasedPerStack2;
            ac.AuraDamageIncreasedPercent2 = d.AuraDamageIncreasedPercent2;
            ac.AuraDamageIncreasedPercentPerStack2 = d.AuraDamageIncreasedPercentPerStack2;
            ac.AuraDamageIncreasedPercentPerStackPerEnergy2 = d.AuraDamageIncreasedPercentPerStackPerEnergy2;

            // Aura Damage Bonus (slot 3)
            ac.AuraDamageType3 = d.AuraDamageType3;
            ac.AuraDamageIncreasedTotal3 = d.AuraDamageIncreasedTotal3;
            ac.AuraDamageIncreasedPerStack3 = d.AuraDamageIncreasedPerStack3;
            ac.AuraDamageIncreasedPercent3 = d.AuraDamageIncreasedPercent3;
            ac.AuraDamageIncreasedPercentPerStack3 = d.AuraDamageIncreasedPercentPerStack3;
            ac.AuraDamageIncreasedPercentPerStackPerEnergy3 = d.AuraDamageIncreasedPercentPerStackPerEnergy3;

            // Aura Damage Bonus (slot 4)
            ac.AuraDamageType4 = d.AuraDamageType4;
            ac.AuraDamageIncreasedTotal4 = d.AuraDamageIncreasedTotal4;
            ac.AuraDamageIncreasedPerStack4 = d.AuraDamageIncreasedPerStack4;
            ac.AuraDamageIncreasedPercent4 = d.AuraDamageIncreasedPercent4;
            ac.AuraDamageIncreasedPercentPerStack4 = d.AuraDamageIncreasedPercentPerStack4;
            ac.AuraDamageIncreasedPercentPerStackPerEnergy4 = d.AuraDamageIncreasedPercentPerStackPerEnergy4;

            // Heal Bonus
            ac.HealDoneTotal = d.HealDoneTotal;
            ac.HealDonePerStack = d.HealDonePerStack;
            ac.HealDonePercent = d.HealDonePercent;
            ac.HealDonePercentPerStack = d.HealDonePercentPerStack;
            ac.HealDonePercentPerStackPerEnergy = d.HealDonePercentPerStackPerEnergy;
            ac.HealReceivedTotal = d.HealReceivedTotal;
            ac.HealReceivedPerStack = d.HealReceivedPerStack;
            ac.HealReceivedPercent = d.HealReceivedPercent;
            ac.HealReceivedPercentPerStack = d.HealReceivedPercentPerStack;

            // Draw
            ac.CardsDrawPerStack = d.CardsDrawPerStack;

            // Damage Reflected
            ac.ChargesPreReqForDamageReflection = d.ChargesPreReqForDamageReflection;
            ac.DamageReflectedModifierType = d.DamageReflectedModifierType;
            ac.DamageReflectedMultiplier = d.DamageReflectedMultiplier;
            ac.DamageReflectedType = d.DamageReflectedType;
            ac.DamageReflectedConsumeCharges = d.DamageReflectedConsumeCharges;

            // Block
            ac.BlockChargesGainedPerStack = d.BlockChargesGainedPerStack;
            ac.NoRemoveBlockAtTurnEnd = d.NoRemoveBlockAtTurnEnd;
            ac.GrantBlockToTeamForAmountOfDamageBlocked = d.GrantBlockToTeamForAmountOfDamageBlocked;
            ac.ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked =
                d.ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked;

            // Prevention
            ac.DamagePreventedPerStack = d.DamagePreventedPerStack;
            ac.CursePreventedPerStack = d.CursePreventedPerStack;
            if (!string.IsNullOrEmpty(d.PreventedAuraCurse))
                ac.PreventedAuraCurse = DataHelper.GetAuraCurse(d.PreventedAuraCurse);
            ac.PreventedAuraCurseStackPerStack = d.PreventedAuraCurseStackPerStack;

            // Damage Received (slot 1)
            ac.IncreasedDamageReceivedType = d.IncreasedDamageReceivedType;
            ac.IncreasedDirectDamageChargesMultiplierNeededForOne = d.IncreasedDirectDamageChargesMultiplierNeededForOne;
            ac.IncreasedDirectDamageReceivedPerTurn = d.IncreasedDirectDamageReceivedPerTurn;
            ac.IncreasedDirectDamageReceivedPerStack = d.IncreasedDirectDamageReceivedPerStack;
            ac.IncreasedPercentDamageReceivedPerTurn = d.IncreasedPercentDamageReceivedPerTurn;
            ac.IncreasedPercentDamageReceivedPerStack = d.IncreasedPercentDamageReceivedPerStack;

            // Damage Received (slot 2)
            ac.IncreasedDamageReceivedType2 = d.IncreasedDamageReceivedType2;
            ac.IncreasedDirectDamageChargesMultiplierNeededForOne2 = d.IncreasedDirectDamageChargesMultiplierNeededForOne2;
            ac.IncreasedDirectDamageReceivedPerTurn2 = d.IncreasedDirectDamageReceivedPerTurn2;
            ac.IncreasedDirectDamageReceivedPerStack2 = d.IncreasedDirectDamageReceivedPerStack2;
            ac.IncreasedPercentDamageReceivedPerTurn2 = d.IncreasedPercentDamageReceivedPerTurn2;
            ac.IncreasedPercentDamageReceivedPerStack2 = d.IncreasedPercentDamageReceivedPerStack2;

            // Damage Prevented
            ac.PreventedDamageTypePerStack = d.PreventedDamageTypePerStack;
            ac.PreventedDamagePerStack = d.PreventedDamagePerStack;

            // Heal Attacker
            ac.HealAttackerPerStack = d.HealAttackerPerStack;
            ac.HealAttackerConsumeCharges = d.HealAttackerConsumeCharges;

            // Character Stat
            ac.CharacterStatModified = d.CharacterStatModified;
            ac.CharacterStatChargesMultiplierNeededForOne = d.CharacterStatChargesMultiplierNeededForOne;
            ac.CharacterStatModifiedValue = d.CharacterStatModifiedValue;
            ac.CharacterStatModifiedValuePerStack = d.CharacterStatModifiedValuePerStack;
            ac.CharacterStatAbsolute = d.CharacterStatAbsolute;
            ac.CharacterStatAbsoluteValue = d.CharacterStatAbsoluteValue;
            ac.CharacterStatAbsoluteValuePerStack = d.CharacterStatAbsoluteValuePerStack;

            // Resist Modification (3 slots)
            ac.ResistModified = d.ResistModified;
            ac.ResistModifiedValue = d.ResistModifiedValue;
            ac.ResistModifiedPercentagePerStack = d.ResistModifiedPercentagePerStack;
            ac.ResistModified2 = d.ResistModified2;
            ac.ResistModifiedValue2 = d.ResistModifiedValue2;
            ac.ResistModifiedPercentagePerStack2 = d.ResistModifiedPercentagePerStack2;
            ac.ResistModified3 = d.ResistModified3;
            ac.ResistModifiedValue3 = d.ResistModifiedValue3;
            ac.ResistModifiedPercentagePerStack3 = d.ResistModifiedPercentagePerStack3;

            // Explode
            ac.ExplodeAtStacks = d.ExplodeAtStacks;
            ac.HealTotalOnExplode = d.HealTotalOnExplode;
            ac.HealPerChargeOnExplode = d.HealPerChargeOnExplode;
            ac.HealTargetOnExplode = d.HealTargetOnExplode;
            if (!string.IsNullOrEmpty(d.ACOnExplode))
                ac.ACOnExplode = DataHelper.GetAuraCurse(d.ACOnExplode);
            ac.ACTotalChargesOnExplode = d.ACTotalChargesOnExplode;
            ac.ACChargesPerStackChargeOnExplode = d.ACChargesPerStackChargeOnExplode;

            // Consume Damage
            ac.DamageTypeWhenConsumed = d.DamageTypeWhenConsumed;
            if (!string.IsNullOrEmpty(d.ConsumedDamageChargesBasedOnACCharges))
                ac.ConsumedDamageChargesBasedOnACCharges = DataHelper.GetAuraCurse(d.ConsumedDamageChargesBasedOnACCharges);
            if (!string.IsNullOrEmpty(d.ConsumeDamageChargesIfACApplied))
                ac.ConsumeDamageChargesIfACApplied = DataHelper.GetAuraCurse(d.ConsumeDamageChargesIfACApplied);
            ac.DamageWhenConsumed = d.DamageWhenConsumed;
            ac.DamageWhenConsumedPerCharge = d.DamageWhenConsumedPerCharge;
            ac.DamageSidesWhenConsumed = d.DamageSidesWhenConsumed;
            ac.DamageSidesWhenConsumedPerCharge = d.DamageSidesWhenConsumedPerCharge;
            ac.DoubleDamageIfCursesLessThan = d.DoubleDamageIfCursesLessThan;

            // Consume Heal
            ac.HealWhenConsumed = d.HealWhenConsumed;
            ac.HealWhenConsumedPerCharge = d.HealWhenConsumedPerCharge;
            ac.HealSidesWhenConsumed = d.HealSidesWhenConsumed;
            ac.HealSidesWhenConsumedPerCharge = d.HealSidesWhenConsumedPerCharge;

            // Remove AC
            if (!string.IsNullOrEmpty(d.RemoveAuraCurse))
                ac.RemoveAuraCurse = DataHelper.GetAuraCurse(d.RemoveAuraCurse);
            if (!string.IsNullOrEmpty(d.RemoveAuraCurse2))
                ac.RemoveAuraCurse2 = DataHelper.GetAuraCurse(d.RemoveAuraCurse2);

            // Gain AC on consumption
            if (!string.IsNullOrEmpty(d.GainAuraCurseConsumption))
                ac.GainAuraCurseConsumption = DataHelper.GetAuraCurse(d.GainAuraCurseConsumption);
            ac.GainAuraCurseConsumptionPerCharge = d.GainAuraCurseConsumptionPerCharge;
            if (!string.IsNullOrEmpty(d.GainChargesFromThisAuraCurse))
                ac.GainChargesFromThisAuraCurse = DataHelper.GetAuraCurse(d.GainChargesFromThisAuraCurse);
            if (!string.IsNullOrEmpty(d.GainAuraCurseConsumption2))
                ac.GainAuraCurseConsumption2 = DataHelper.GetAuraCurse(d.GainAuraCurseConsumption2);
            ac.GainAuraCurseConsumptionPerCharge2 = d.GainAuraCurseConsumptionPerCharge2;
            if (!string.IsNullOrEmpty(d.GainChargesFromThisAuraCurse2))
                ac.GainChargesFromThisAuraCurse2 = DataHelper.GetAuraCurse(d.GainChargesFromThisAuraCurse2);

            // Reveal / Cost
            ac.RevealCardsPerCharge = d.RevealCardsPerCharge;
            ac.ModifyCardCostPerChargeNeededForOne = d.ModifyCardCostPerChargeNeededForOne;

            // Disabled Card Types
            ac.DisabledCardTypes = d.DisabledCardTypes ?? Array.Empty<Enums.CardType>();

            // Misc
            ac.Invulnerable = d.Invulnerable;
            ac.Stealth = d.Stealth;
            ac.Taunt = d.Taunt;
            ac.SkipsNextTurn = d.SkipsNextTurn;

            // AC Bonus Data (charge bonuses)
            if (d.ACBonusData != null && d.ACBonusData.Count > 0)
            {
                var list = new List<AuraCurseData.AuraCurseChargesBonus>();
                foreach (var b in d.ACBonusData)
                {
                    var bonus = new AuraCurseData.AuraCurseChargesBonus();
                    bonus.requiredChargesForBonus = b.RequiredChargesForBonus;
                    bonus.bonusType = b.BonusType;
                    bonus.bonusCharges = b.ChargesBonus;
                    if (!string.IsNullOrEmpty(b.AuraCurseId))
                        bonus.acData = DataHelper.GetAuraCurse(b.AuraCurseId);
                    list.Add(bonus);
                }
                ac.ACBonusData = list;
            }

            // Aura Damage Conditional Bonuses
            if (d.AuraDamageConditionalBonuses != null && d.AuraDamageConditionalBonuses.Count > 0)
            {
                var arr = new AuraCurseData.AuraDamageBonus[d.AuraDamageConditionalBonuses.Count];
                for (int i = 0; i < d.AuraDamageConditionalBonuses.Count; i++)
                {
                    var b = d.AuraDamageConditionalBonuses[i];
                    arr[i] = new AuraCurseData.AuraDamageBonus
                    {
                        AuraDamageType = b.DamageType,
                        AuraDamageIncreasedTotal = b.FlatBonus,
                        AuraDamageIncreasedPerStack = b.FlatBonusPerStack,
                        AuraDamageIncreasedPercent = b.PercentBonus,
                        AuraDamageIncreasedPercentPerStack = b.PercentBonusPerStack,
                        AuraDamageIncreasedPercentPerStackPerEnergy = b.PercentBonusPerStackPerEnergy
                    };
                    if (!string.IsNullOrEmpty(b.BasedOnACId))
                        arr[i].AuraDamageBasedOnAC = DataHelper.GetAuraCurse(b.BasedOnACId);
                }
                ac.AuraDamageConditionalBonuses = arr;
            }

            return ac;
        }

        /// <summary>
        /// Snapshot a live AuraCurseData SO into an AuraCurseDef for override editing.
        /// </summary>
        public static AuraCurseDef SnapshotAuraCurse(AuraCurseData ac)
        {
            if (ac == null) return null;
            var d = new AuraCurseDef();
            var t = Traverse.Create(ac);

            d.Id = t.Field<string>("id").Value ?? "";
            d.ACName = ac.ACName ?? "";
            d.IsAura = ac.IsAura;
            d.Description = ac.Description ?? "";
            d.MaxCharges = ac.MaxCharges;
            d.MaxMadnessCharges = ac.MaxMadnessCharges;
            d.AuraConsumed = ac.AuraConsumed;
            d.ChargesMultiplierDescription = ac.ChargesMultiplierDescription;
            d.ChargesAuxNeedForOne1 = ac.ChargesAuxNeedForOne1;
            d.ChargesAuxNeedForOne2 = ac.ChargesAuxNeedForOne2;
            d.Sprite = ac.Sprite != null ? ac.Sprite.name : "";
            d.EffectTick = ac.EffectTick ?? "";
            d.EffectTickSides = ac.EffectTickSides ?? "";
            d.Sound = ac.Sound != null ? ac.Sound.name : "";
            d.SoundRework = ac.SoundRework != null ? ac.SoundRework.name : "";

            // Config
            d.Removable = ac.Removable;
            d.GainCharges = ac.GainCharges;
            d.IconShow = ac.IconShow;
            d.CombatlogShow = ac.CombatlogShow;
            d.Preventable = ac.Preventable;
            d.CanBeAddedToImmunityDespiteNotBeingPreventable = ac.CanBeAddedToImmunityDespiteNotBeingPreventable;

            // Expiration
            d.PriorityOnConsumption = ac.PriorityOnConsumption;
            d.ConsumeAll = ac.ConsumeAll;
            d.ConsumedAtCast = ac.ConsumedAtCast;
            d.ConsumedAtTurnBegin = ac.ConsumedAtTurnBegin;
            d.ConsumedAtTurn = ac.ConsumedAtTurn;
            d.ConsumedAtRoundBegin = ac.ConsumedAtRoundBegin;
            d.ConsumedAtRound = ac.ConsumedAtRound;
            d.ProduceDamageWhenConsumed = ac.ProduceDamageWhenConsumed;
            d.ProduceHealWhenConsumed = ac.ProduceHealWhenConsumed;
            d.DieWhenConsumedAll = ac.DieWhenConsumedAll;

            // Aura Damage Bonus (4 slots)
            d.AuraDamageType = ac.AuraDamageType;
            d.AuraDamageChargesBasedOnACCharges = ac.AuraDamageChargesBasedOnACCharges != null
                ? t.Field<string>("id").Value : "";
            d.AuraDamageIncreasedTotal = ac.AuraDamageIncreasedTotal;
            d.AuraDamageIncreasedPerStack = ac.AuraDamageIncreasedPerStack;
            d.AuraDamageIncreasedPercent = ac.AuraDamageIncreasedPercent;
            d.AuraDamageIncreasedPercentPerStack = ac.AuraDamageIncreasedPercentPerStack;
            d.AuraDamageIncreasedPercentPerStackPerEnergy = ac.AuraDamageIncreasedPercentPerStackPerEnergy;

            d.AuraDamageType2 = ac.AuraDamageType2;
            d.AuraDamageIncreasedTotal2 = ac.AuraDamageIncreasedTotal2;
            d.AuraDamageIncreasedPerStack2 = ac.AuraDamageIncreasedPerStack2;
            d.AuraDamageIncreasedPercent2 = ac.AuraDamageIncreasedPercent2;
            d.AuraDamageIncreasedPercentPerStack2 = ac.AuraDamageIncreasedPercentPerStack2;
            d.AuraDamageIncreasedPercentPerStackPerEnergy2 = ac.AuraDamageIncreasedPercentPerStackPerEnergy2;

            d.AuraDamageType3 = ac.AuraDamageType3;
            d.AuraDamageIncreasedTotal3 = ac.AuraDamageIncreasedTotal3;
            d.AuraDamageIncreasedPerStack3 = ac.AuraDamageIncreasedPerStack3;
            d.AuraDamageIncreasedPercent3 = ac.AuraDamageIncreasedPercent3;
            d.AuraDamageIncreasedPercentPerStack3 = ac.AuraDamageIncreasedPercentPerStack3;
            d.AuraDamageIncreasedPercentPerStackPerEnergy3 = ac.AuraDamageIncreasedPercentPerStackPerEnergy3;

            d.AuraDamageType4 = ac.AuraDamageType4;
            d.AuraDamageIncreasedTotal4 = ac.AuraDamageIncreasedTotal4;
            d.AuraDamageIncreasedPerStack4 = ac.AuraDamageIncreasedPerStack4;
            d.AuraDamageIncreasedPercent4 = ac.AuraDamageIncreasedPercent4;
            d.AuraDamageIncreasedPercentPerStack4 = ac.AuraDamageIncreasedPercentPerStack4;
            d.AuraDamageIncreasedPercentPerStackPerEnergy4 = ac.AuraDamageIncreasedPercentPerStackPerEnergy4;

            // Heal Bonus
            d.HealDoneTotal = ac.HealDoneTotal;
            d.HealDonePerStack = ac.HealDonePerStack;
            d.HealDonePercent = ac.HealDonePercent;
            d.HealDonePercentPerStack = ac.HealDonePercentPerStack;
            d.HealDonePercentPerStackPerEnergy = ac.HealDonePercentPerStackPerEnergy;
            d.HealReceivedTotal = ac.HealReceivedTotal;
            d.HealReceivedPerStack = ac.HealReceivedPerStack;
            d.HealReceivedPercent = ac.HealReceivedPercent;
            d.HealReceivedPercentPerStack = ac.HealReceivedPercentPerStack;

            // Draw
            d.CardsDrawPerStack = ac.CardsDrawPerStack;

            // Damage Reflected
            d.ChargesPreReqForDamageReflection = ac.ChargesPreReqForDamageReflection;
            d.DamageReflectedModifierType = ac.DamageReflectedModifierType;
            d.DamageReflectedMultiplier = ac.DamageReflectedMultiplier;
            d.DamageReflectedType = ac.DamageReflectedType;
            d.DamageReflectedConsumeCharges = ac.DamageReflectedConsumeCharges;

            // Block
            d.BlockChargesGainedPerStack = ac.BlockChargesGainedPerStack;
            d.NoRemoveBlockAtTurnEnd = ac.NoRemoveBlockAtTurnEnd;
            d.GrantBlockToTeamForAmountOfDamageBlocked = ac.GrantBlockToTeamForAmountOfDamageBlocked;
            d.ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked =
                ac.ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked;

            // Prevention
            d.DamagePreventedPerStack = ac.DamagePreventedPerStack;
            d.CursePreventedPerStack = ac.CursePreventedPerStack;
            d.PreventedAuraCurse = GetACId(ac.PreventedAuraCurse);
            d.PreventedAuraCurseStackPerStack = ac.PreventedAuraCurseStackPerStack;

            // Damage Received (2 slots)
            d.IncreasedDamageReceivedType = ac.IncreasedDamageReceivedType;
            d.IncreasedDirectDamageChargesMultiplierNeededForOne = ac.IncreasedDirectDamageChargesMultiplierNeededForOne;
            d.IncreasedDirectDamageReceivedPerTurn = ac.IncreasedDirectDamageReceivedPerTurn;
            d.IncreasedDirectDamageReceivedPerStack = ac.IncreasedDirectDamageReceivedPerStack;
            d.IncreasedPercentDamageReceivedPerTurn = ac.IncreasedPercentDamageReceivedPerTurn;
            d.IncreasedPercentDamageReceivedPerStack = ac.IncreasedPercentDamageReceivedPerStack;

            d.IncreasedDamageReceivedType2 = ac.IncreasedDamageReceivedType2;
            d.IncreasedDirectDamageChargesMultiplierNeededForOne2 = ac.IncreasedDirectDamageChargesMultiplierNeededForOne2;
            d.IncreasedDirectDamageReceivedPerTurn2 = ac.IncreasedDirectDamageReceivedPerTurn2;
            d.IncreasedDirectDamageReceivedPerStack2 = ac.IncreasedDirectDamageReceivedPerStack2;
            d.IncreasedPercentDamageReceivedPerTurn2 = ac.IncreasedPercentDamageReceivedPerTurn2;
            d.IncreasedPercentDamageReceivedPerStack2 = ac.IncreasedPercentDamageReceivedPerStack2;

            d.PreventedDamageTypePerStack = ac.PreventedDamageTypePerStack;
            d.PreventedDamagePerStack = ac.PreventedDamagePerStack;

            // Heal Attacker
            d.HealAttackerPerStack = ac.HealAttackerPerStack;
            d.HealAttackerConsumeCharges = ac.HealAttackerConsumeCharges;

            // Character Stat
            d.CharacterStatModified = ac.CharacterStatModified;
            d.CharacterStatChargesMultiplierNeededForOne = ac.CharacterStatChargesMultiplierNeededForOne;
            d.CharacterStatModifiedValue = ac.CharacterStatModifiedValue;
            d.CharacterStatModifiedValuePerStack = ac.CharacterStatModifiedValuePerStack;
            d.CharacterStatAbsolute = ac.CharacterStatAbsolute;
            d.CharacterStatAbsoluteValue = ac.CharacterStatAbsoluteValue;
            d.CharacterStatAbsoluteValuePerStack = ac.CharacterStatAbsoluteValuePerStack;

            // Resists
            d.ResistModified = ac.ResistModified;
            d.ResistModifiedValue = ac.ResistModifiedValue;
            d.ResistModifiedPercentagePerStack = ac.ResistModifiedPercentagePerStack;
            d.ResistModified2 = ac.ResistModified2;
            d.ResistModifiedValue2 = ac.ResistModifiedValue2;
            d.ResistModifiedPercentagePerStack2 = ac.ResistModifiedPercentagePerStack2;
            d.ResistModified3 = ac.ResistModified3;
            d.ResistModifiedValue3 = ac.ResistModifiedValue3;
            d.ResistModifiedPercentagePerStack3 = ac.ResistModifiedPercentagePerStack3;

            // Explode
            d.ExplodeAtStacks = ac.ExplodeAtStacks;
            d.HealTotalOnExplode = ac.HealTotalOnExplode;
            d.HealPerChargeOnExplode = ac.HealPerChargeOnExplode;
            d.HealTargetOnExplode = ac.HealTargetOnExplode;
            d.ACOnExplode = GetACId(ac.ACOnExplode);
            d.ACTotalChargesOnExplode = ac.ACTotalChargesOnExplode;
            d.ACChargesPerStackChargeOnExplode = ac.ACChargesPerStackChargeOnExplode;

            // Consume Damage
            d.DamageTypeWhenConsumed = ac.DamageTypeWhenConsumed;
            d.ConsumedDamageChargesBasedOnACCharges = GetACId(ac.ConsumedDamageChargesBasedOnACCharges);
            d.ConsumeDamageChargesIfACApplied = GetACId(ac.ConsumeDamageChargesIfACApplied);
            d.DamageWhenConsumed = ac.DamageWhenConsumed;
            d.DamageWhenConsumedPerCharge = ac.DamageWhenConsumedPerCharge;
            d.DamageSidesWhenConsumed = ac.DamageSidesWhenConsumed;
            d.DamageSidesWhenConsumedPerCharge = ac.DamageSidesWhenConsumedPerCharge;
            d.DoubleDamageIfCursesLessThan = ac.DoubleDamageIfCursesLessThan;

            // Consume Heal
            d.HealWhenConsumed = ac.HealWhenConsumed;
            d.HealWhenConsumedPerCharge = ac.HealWhenConsumedPerCharge;
            d.HealSidesWhenConsumed = ac.HealSidesWhenConsumed;
            d.HealSidesWhenConsumedPerCharge = ac.HealSidesWhenConsumedPerCharge;

            // Remove/Gain AC
            d.RemoveAuraCurse = GetACId(ac.RemoveAuraCurse);
            d.RemoveAuraCurse2 = GetACId(ac.RemoveAuraCurse2);
            d.GainAuraCurseConsumption = GetACId(ac.GainAuraCurseConsumption);
            d.GainAuraCurseConsumptionPerCharge = ac.GainAuraCurseConsumptionPerCharge;
            d.GainChargesFromThisAuraCurse = GetACId(ac.GainChargesFromThisAuraCurse);
            d.GainAuraCurseConsumption2 = GetACId(ac.GainAuraCurseConsumption2);
            d.GainAuraCurseConsumptionPerCharge2 = ac.GainAuraCurseConsumptionPerCharge2;
            d.GainChargesFromThisAuraCurse2 = GetACId(ac.GainChargesFromThisAuraCurse2);

            // Reveal / Cost
            d.RevealCardsPerCharge = ac.RevealCardsPerCharge;
            d.ModifyCardCostPerChargeNeededForOne = ac.ModifyCardCostPerChargeNeededForOne;

            // Disabled types
            d.DisabledCardTypes = ac.DisabledCardTypes ?? Array.Empty<Enums.CardType>();

            // Misc
            d.Invulnerable = ac.Invulnerable;
            d.Stealth = ac.Stealth;
            d.Taunt = ac.Taunt;
            d.SkipsNextTurn = ac.SkipsNextTurn;

            // AC Bonus Data
            if (ac.ACBonusData != null && ac.ACBonusData.Count > 0)
            {
                foreach (var b in ac.ACBonusData)
                {
                    d.ACBonusData.Add(new AuraCurseChargesBonusDef
                    {
                        AuraCurseId = b.acData != null ? GetACId(b.acData) : "",
                        ChargesBonus = b.bonusCharges,
                        RequiredChargesForBonus = b.requiredChargesForBonus,
                        BonusType = b.bonusType
                    });
                }
            }

            // Aura Damage Conditional Bonuses
            if (ac.AuraDamageConditionalBonuses != null && ac.AuraDamageConditionalBonuses.Length > 0)
            {
                foreach (var b in ac.AuraDamageConditionalBonuses)
                {
                    d.AuraDamageConditionalBonuses.Add(new AuraDamageBonusDef
                    {
                        DamageType = b.AuraDamageType,
                        BasedOnACId = b.AuraDamageBasedOnAC != null ? GetACId(b.AuraDamageBasedOnAC) : "",
                        FlatBonus = b.AuraDamageIncreasedTotal,
                        FlatBonusPerStack = b.AuraDamageIncreasedPerStack,
                        PercentBonus = b.AuraDamageIncreasedPercent,
                        PercentBonusPerStack = b.AuraDamageIncreasedPercentPerStack,
                        PercentBonusPerStackPerEnergy = b.AuraDamageIncreasedPercentPerStackPerEnergy
                    });
                }
            }

            return d;
        }

        private static void RegisterAuraCurse(AuraCurseData ac)
        {
            var t = Traverse.Create(ac);
            string id = t.Field<string>("id").Value?.ToLower() ?? "";
            if (string.IsNullOrEmpty(id)) return;

            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, AuraCurseData>>("_AurasCursesSource").Value;
            if (dict != null) dict[id] = ac;
        }
    }
}
