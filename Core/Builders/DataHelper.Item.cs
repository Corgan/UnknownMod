using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Item Builders (MakeFullItem, SnapshotItem)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        /// <summary>Create a complete ItemData SO from an ItemDef, setting ALL fields.</summary>
        public static ItemData MakeFullItem(ItemDef d)
        {
            var item = ScriptableObject.CreateInstance<ItemData>();
            var t = Traverse.Create(item);

            // ── Identity ─────────────────────────────────────────
            item.Id = d.Id;

            // ── Activation / Requisite ───────────────────────────
            item.Activation = d.Activation;
            t.Field("activationOnlyOnHeroes").SetValue(d.ActivationOnlyOnHeroes);
            item.ItemTarget = d.ItemTarget;
            t.Field("dontTargetBoss").SetValue(d.DontTargetBoss);
            item.TimesPerTurn = d.TimesPerTurn;
            item.TimesPerCombat = d.TimesPerCombat;
            item.ExactRound = d.ExactRound;
            item.RoundCycle = d.RoundCycle;

            if (!string.IsNullOrEmpty(d.AuraCurseSetted))
                item.AuraCurseSetted = GetAuraCurse(d.AuraCurseSetted);
            if (!string.IsNullOrEmpty(d.AuraCurseSetted2))
                item.AuraCurseSetted2 = GetAuraCurse(d.AuraCurseSetted2);
            if (!string.IsNullOrEmpty(d.AuraCurseSetted3))
                item.AuraCurseSetted3 = GetAuraCurse(d.AuraCurseSetted3);
            item.AuraCurseNumForOneEvent = d.AuraCurseNumForOneEvent;

            item.CastedCardType = d.CastedCardType;
            t.Field("usedEnergy").SetValue(d.UsedEnergy);
            item.LowerOrEqualPercentHP = d.LowerOrEqualPercentHP;
            item.EmptyHand = d.EmptyHand;
            t.Field("notShowCharacterBonus").SetValue(d.NotShowCharacterBonus);
            item.PetActivation = d.PetActivation;

            // ── Damage Bonuses ───────────────────────────────────
            item.DamageFlatBonus = d.DamageFlatBonus;
            item.DamageFlatBonusValue = d.DamageFlatBonusValue;
            item.DamageFlatBonus2 = d.DamageFlatBonus2;
            item.DamageFlatBonusValue2 = d.DamageFlatBonusValue2;
            item.DamageFlatBonus3 = d.DamageFlatBonus3;
            item.DamageFlatBonusValue3 = d.DamageFlatBonusValue3;

            item.DamagePercentBonus = d.DamagePercentBonus;
            item.DamagePercentBonusValue = d.DamagePercentBonusValue;
            item.DamagePercentBonus2 = d.DamagePercentBonus2;
            item.DamagePercentBonusValue2 = d.DamagePercentBonusValue2;
            item.DamagePercentBonus3 = d.DamagePercentBonus3;
            item.DamagePercentBonusValue3 = d.DamagePercentBonusValue3;

            // ── Resist Bonuses ───────────────────────────────────
            item.ResistModified1 = d.ResistModified1;
            item.ResistModifiedValue1 = d.ResistModifiedValue1;
            item.ResistModified2 = d.ResistModified2;
            item.ResistModifiedValue2 = d.ResistModifiedValue2;
            item.ResistModified3 = d.ResistModified3;
            item.ResistModifiedValue3 = d.ResistModifiedValue3;

            // ── Character Stat ───────────────────────────────────
            item.CharacterStatModified = d.CharacterStatModified;
            item.CharacterStatModifiedValue = d.CharacterStatModifiedValue;
            item.CharacterStatModified2 = d.CharacterStatModified2;
            item.CharacterStatModifiedValue2 = d.CharacterStatModifiedValue2;
            item.CharacterStatModified3 = d.CharacterStatModified3;
            item.CharacterStatModifiedValue3 = d.CharacterStatModifiedValue3;
            item.MaxHealth = d.MaxHealth;

            // ── Heal Bonuses ─────────────────────────────────────
            item.HealFlatBonus = d.HealFlatBonus;
            item.HealPercentBonus = d.HealPercentBonus;
            item.HealReceivedFlatBonus = d.HealReceivedFlatBonus;
            item.HealReceivedPercentBonus = d.HealReceivedPercentBonus;
            item.HealQuantity = d.HealQuantity;
            item.HealQuantitySpecialValue = MakeSV(d.HealQuantitySpecialValue);
            item.HealPercentQuantity = d.HealPercentQuantity;
            item.HealPercentQuantitySelf = d.HealPercentQuantitySelf;
            item.HealSelfPerDamageDonePercent = d.HealSelfPerDamageDonePercent;
            item.HealSelfTeamPerDamageDonePercent = d.HealSelfTeamPerDamageDonePercent;
            t.Field("healBasedOnAuraCurse").SetValue(d.HealBasedOnAuraCurse);

            // ── Energy / Draw ────────────────────────────────────
            item.EnergyQuantity = d.EnergyQuantity;
            item.DrawCards = d.DrawCards;
            item.DrawMultiplyByEnergyUsed = d.DrawMultiplyByEnergyUsed;

            // ── AC Gain (target) ─────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseGain1))
                item.AuracurseGain1 = GetAuraCurse(d.AuracurseGain1);
            item.AuracurseGainValue1 = d.AuracurseGainValue1;
            item.AuracurseGain1SpecialValue = MakeSV(d.AuracurseGain1SpecialValue);
            item.Acg1MultiplyByEnergyUsed = d.Acg1MultiplyByEnergyUsed;

            if (!string.IsNullOrEmpty(d.AuracurseGain2))
                item.AuracurseGain2 = GetAuraCurse(d.AuracurseGain2);
            item.AuracurseGainValue2 = d.AuracurseGainValue2;
            item.AuracurseGain2SpecialValue = MakeSV(d.AuracurseGain2SpecialValue);
            item.Acg2MultiplyByEnergyUsed = d.Acg2MultiplyByEnergyUsed;

            if (!string.IsNullOrEmpty(d.AuracurseGain3))
                item.AuracurseGain3 = GetAuraCurse(d.AuracurseGain3);
            item.AuracurseGainValue3 = d.AuracurseGainValue3;
            item.AuracurseGain3SpecialValue = MakeSV(d.AuracurseGain3SpecialValue);
            item.Acg3MultiplyByEnergyUsed = d.Acg3MultiplyByEnergyUsed;
            item.ChooseOneACToGain = d.ChooseOneACToGain;

            // ── AC Gain (self) ───────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseGainSelf1))
                item.AuracurseGainSelf1 = GetAuraCurse(d.AuracurseGainSelf1);
            item.AuracurseGainSelfValue1 = d.AuracurseGainSelfValue1;
            if (!string.IsNullOrEmpty(d.AuracurseGainSelf2))
                item.AuracurseGainSelf2 = GetAuraCurse(d.AuracurseGainSelf2);
            item.AuracurseGainSelfValue2 = d.AuracurseGainSelfValue2;
            if (!string.IsNullOrEmpty(d.AuracurseGainSelf3))
                item.AuracurseGainSelf3 = GetAuraCurse(d.AuracurseGainSelf3);
            item.AuracurseGainSelfValue3 = d.AuracurseGainSelfValue3;

            // ── Dispel / Purge ───────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseHeal1))
                item.AuracurseHeal1 = GetAuraCurse(d.AuracurseHeal1);
            if (!string.IsNullOrEmpty(d.AuracurseHeal2))
                item.AuracurseHeal2 = GetAuraCurse(d.AuracurseHeal2);
            if (!string.IsNullOrEmpty(d.AuracurseHeal3))
                item.AuracurseHeal3 = GetAuraCurse(d.AuracurseHeal3);
            item.AcHealFromTarget = d.AcHealFromTarget;
            item.StealAuras = d.StealAuras;
            item.ChanceToDispel = d.ChanceToDispel;
            item.ChanceToDispelNum = d.ChanceToDispelNum;
            item.ChanceToPurge = d.ChanceToPurge;
            item.ChanceToPurgeNum = d.ChanceToPurgeNum;
            item.ChanceToDispelSelf = d.ChanceToDispelSelf;
            item.ChanceToDispelNumSelf = d.ChanceToDispelNumSelf;

            // ── Passive AC Bonuses ───────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseBonus1))
                item.AuracurseBonus1 = GetAuraCurse(d.AuracurseBonus1);
            item.AuracurseBonusValue1 = d.AuracurseBonusValue1;
            if (!string.IsNullOrEmpty(d.AuracurseBonus2))
                item.AuracurseBonus2 = GetAuraCurse(d.AuracurseBonus2);
            item.AuracurseBonusValue2 = d.AuracurseBonusValue2;
            item.IncreaseAurasSelf = d.IncreaseAurasSelf;

            // ── AC Immunities ────────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseImmune1))
                item.AuracurseImmune1 = GetAuraCurse(d.AuracurseImmune1);
            if (!string.IsNullOrEmpty(d.AuracurseImmune2))
                item.AuracurseImmune2 = GetAuraCurse(d.AuracurseImmune2);

            // ── Card Gain ────────────────────────────────────────
            item.CardNum = d.CardNum;
            if (!string.IsNullOrEmpty(d.CardToGain))
                item.CardToGain = GetCard(d.CardToGain);
            item.CardToGainType = d.CardToGainType;
            item.CardPlace = d.CardPlace;
            if (d.CardToGainList != null && d.CardToGainList.Count > 0)
            {
                var list = new System.Collections.Generic.List<CardData>();
                foreach (var cid in d.CardToGainList)
                {
                    var c = GetCard(cid);
                    if (c != null) list.Add(c);
                }
                item.CardToGainList = list;
            }

            // ── Cost / Economy ───────────────────────────────────
            item.CostZero = d.CostZero;
            item.CostReduction = d.CostReduction;
            item.CardsReduced = d.CardsReduced;
            item.CardToReduceType = d.CardToReduceType;
            item.CostReduceReduction = d.CostReduceReduction;
            item.CostReduceEnergyRequirement = d.CostReduceEnergyRequirement;
            item.CostReducePermanent = d.CostReducePermanent;
            item.ReduceHighestCost = d.ReduceHighestCost;

            // ── Rewards / Discounts ──────────────────────────────
            item.PercentRetentionEndGame = d.PercentRetentionEndGame;
            item.PercentDiscountShop = d.PercentDiscountShop;

            // ── Damage To Target (enchantment) ───────────────────
            item.DamageToTarget1 = d.DamageToTarget;
            t.Field("damageToTargetType").SetValue(d.DamageToTargetType);
            item.DttMultiplyByEnergyUsed = d.DttMultiplyByEnergyUsed;
            item.DttSpecialValues1 = MakeSV(d.DttSpecialValues1);
            item.DamageToTarget2 = d.DamageToTarget2;
            t.Field("damageToTargetType2").SetValue(d.DamageToTargetType2);
            item.DttSpecialValues2 = MakeSV(d.DttSpecialValues2);
            item.ModifiedDamageType = d.ModifiedDamageType;

            // ── Flags ────────────────────────────────────────────
            item.CursedItem = d.CursedItem;
            item.DropOnly = d.DropOnly;
            item.QuestItem = d.QuestItem;
            item.DestroyAfterUse = d.DestroyAfterUse;
            item.Vanish = d.Vanish;
            item.Permanent = d.Permanent;
            item.DuplicateActive = d.DuplicateActive;
            item.PassSingleAndCharacterRolls = d.PassSingleAndCharacterRolls;
            item.OnlyAddItemToNPCs = d.OnlyAddItemToNPCs;
            item.AddVanishToDeck = d.AddVanishToDeck;

            // ── Enchantment ──────────────────────────────────────
            item.IsEnchantment = d.IsEnchantment;
            item.UseTheNextInsteadWhenYouPlay = d.UseTheNextInsteadWhenYouPlay;
            item.DestroyAfterUses = d.DestroyAfterUses;
            item.DestroyStartOfTurn = d.DestroyStartOfTurn;
            item.DestroyEndOfTurn = d.DestroyEndOfTurn;
            item.CastEnchantmentOnFinishSelfCast = d.CastEnchantmentOnFinishSelfCast;

            // ── Custom AC ────────────────────────────────────────
            item.AuracurseCustomString = d.AuracurseCustomString ?? "";
            if (!string.IsNullOrEmpty(d.AuracurseCustomAC))
                item.AuracurseCustomAC = GetAuraCurse(d.AuracurseCustomAC);
            item.AuracurseCustomModValue1 = d.AuracurseCustomModValue1;
            item.AuracurseCustomModValue2 = d.AuracurseCustomModValue2;

            // ── FX / Effects ─────────────────────────────────────
            item.EffectItemOwner = d.EffectItemOwner ?? "";
            item.EffectCaster = d.EffectCaster ?? "";
            item.EffectCasterDelay = d.EffectCasterDelay;
            item.EffectTarget = d.EffectTarget ?? "";
            item.EffectTargetDelay = d.EffectTargetDelay;

            return item;
        }

        /// <summary>Snapshot an existing ItemData SO into an ItemDef DTO.</summary>
        public static ItemDef SnapshotItem(ItemData item)
        {
            if (item == null) return new ItemDef();
            var t = Traverse.Create(item);
            var d = new ItemDef();

            d.Id = item.Id ?? "";
            d.Name = "";

            d.Activation = item.Activation;
            d.ActivationOnlyOnHeroes = t.Field<bool>("activationOnlyOnHeroes").Value;
            d.ItemTarget = item.ItemTarget;
            d.DontTargetBoss = t.Field<bool>("dontTargetBoss").Value;
            d.TimesPerTurn = item.TimesPerTurn;
            d.TimesPerCombat = item.TimesPerCombat;
            d.ExactRound = item.ExactRound;
            d.RoundCycle = item.RoundCycle;
            d.AuraCurseSetted = GetACId(item.AuraCurseSetted);
            d.AuraCurseSetted2 = GetACId(item.AuraCurseSetted2);
            d.AuraCurseSetted3 = GetACId(item.AuraCurseSetted3);
            d.AuraCurseNumForOneEvent = item.AuraCurseNumForOneEvent;
            d.CastedCardType = item.CastedCardType;
            d.UsedEnergy = t.Field<bool>("usedEnergy").Value;
            d.LowerOrEqualPercentHP = item.LowerOrEqualPercentHP;
            d.EmptyHand = item.EmptyHand;
            d.NotShowCharacterBonus = t.Field<bool>("notShowCharacterBonus").Value;
            d.PetActivation = item.PetActivation;

            d.DamageFlatBonus = item.DamageFlatBonus;
            d.DamageFlatBonusValue = item.DamageFlatBonusValue;
            d.DamageFlatBonus2 = item.DamageFlatBonus2;
            d.DamageFlatBonusValue2 = item.DamageFlatBonusValue2;
            d.DamageFlatBonus3 = item.DamageFlatBonus3;
            d.DamageFlatBonusValue3 = item.DamageFlatBonusValue3;
            d.DamagePercentBonus = item.DamagePercentBonus;
            d.DamagePercentBonusValue = item.DamagePercentBonusValue;
            d.DamagePercentBonus2 = item.DamagePercentBonus2;
            d.DamagePercentBonusValue2 = item.DamagePercentBonusValue2;
            d.DamagePercentBonus3 = item.DamagePercentBonus3;
            d.DamagePercentBonusValue3 = item.DamagePercentBonusValue3;

            d.ResistModified1 = item.ResistModified1;
            d.ResistModifiedValue1 = item.ResistModifiedValue1;
            d.ResistModified2 = item.ResistModified2;
            d.ResistModifiedValue2 = item.ResistModifiedValue2;
            d.ResistModified3 = item.ResistModified3;
            d.ResistModifiedValue3 = item.ResistModifiedValue3;

            d.CharacterStatModified = item.CharacterStatModified;
            d.CharacterStatModifiedValue = item.CharacterStatModifiedValue;
            d.CharacterStatModified2 = item.CharacterStatModified2;
            d.CharacterStatModifiedValue2 = item.CharacterStatModifiedValue2;
            d.CharacterStatModified3 = item.CharacterStatModified3;
            d.CharacterStatModifiedValue3 = item.CharacterStatModifiedValue3;
            d.MaxHealth = item.MaxHealth;

            d.HealFlatBonus = item.HealFlatBonus;
            d.HealPercentBonus = item.HealPercentBonus;
            d.HealReceivedFlatBonus = item.HealReceivedFlatBonus;
            d.HealReceivedPercentBonus = item.HealReceivedPercentBonus;
            d.HealQuantity = item.HealQuantity;
            d.HealQuantitySpecialValue = SnapSV(item.HealQuantitySpecialValue);
            d.HealPercentQuantity = item.HealPercentQuantity;
            d.HealPercentQuantitySelf = item.HealPercentQuantitySelf;
            d.HealSelfPerDamageDonePercent = item.HealSelfPerDamageDonePercent;
            d.HealSelfTeamPerDamageDonePercent = item.HealSelfTeamPerDamageDonePercent;
            d.HealBasedOnAuraCurse = item.HealBasedOnAuraCurse;

            d.EnergyQuantity = item.EnergyQuantity;
            d.DrawCards = item.DrawCards;
            d.DrawMultiplyByEnergyUsed = item.DrawMultiplyByEnergyUsed;

            d.AuracurseGain1 = GetACId(item.AuracurseGain1);
            d.AuracurseGainValue1 = item.AuracurseGainValue1;
            d.AuracurseGain1SpecialValue = SnapSV(item.AuracurseGain1SpecialValue);
            d.Acg1MultiplyByEnergyUsed = item.Acg1MultiplyByEnergyUsed;
            d.AuracurseGain2 = GetACId(item.AuracurseGain2);
            d.AuracurseGainValue2 = item.AuracurseGainValue2;
            d.AuracurseGain2SpecialValue = SnapSV(item.AuracurseGain2SpecialValue);
            d.Acg2MultiplyByEnergyUsed = item.Acg2MultiplyByEnergyUsed;
            d.AuracurseGain3 = GetACId(item.AuracurseGain3);
            d.AuracurseGainValue3 = item.AuracurseGainValue3;
            d.AuracurseGain3SpecialValue = SnapSV(item.AuracurseGain3SpecialValue);
            d.Acg3MultiplyByEnergyUsed = item.Acg3MultiplyByEnergyUsed;
            d.ChooseOneACToGain = item.ChooseOneACToGain;

            d.AuracurseGainSelf1 = GetACId(item.AuracurseGainSelf1);
            d.AuracurseGainSelfValue1 = item.AuracurseGainSelfValue1;
            d.AuracurseGainSelf2 = GetACId(item.AuracurseGainSelf2);
            d.AuracurseGainSelfValue2 = item.AuracurseGainSelfValue2;
            d.AuracurseGainSelf3 = GetACId(item.AuracurseGainSelf3);
            d.AuracurseGainSelfValue3 = item.AuracurseGainSelfValue3;

            d.AuracurseHeal1 = GetACId(item.AuracurseHeal1);
            d.AuracurseHeal2 = GetACId(item.AuracurseHeal2);
            d.AuracurseHeal3 = GetACId(item.AuracurseHeal3);
            d.AcHealFromTarget = item.AcHealFromTarget;
            d.StealAuras = item.StealAuras;
            d.ChanceToDispel = item.ChanceToDispel;
            d.ChanceToDispelNum = item.ChanceToDispelNum;
            d.ChanceToPurge = item.ChanceToPurge;
            d.ChanceToPurgeNum = item.ChanceToPurgeNum;
            d.ChanceToDispelSelf = item.ChanceToDispelSelf;
            d.ChanceToDispelNumSelf = item.ChanceToDispelNumSelf;

            d.AuracurseBonus1 = GetACId(item.AuracurseBonus1);
            d.AuracurseBonusValue1 = item.AuracurseBonusValue1;
            d.AuracurseBonus2 = GetACId(item.AuracurseBonus2);
            d.AuracurseBonusValue2 = item.AuracurseBonusValue2;
            d.IncreaseAurasSelf = item.IncreaseAurasSelf;

            d.AuracurseImmune1 = GetACId(item.AuracurseImmune1);
            d.AuracurseImmune2 = GetACId(item.AuracurseImmune2);

            d.CardNum = item.CardNum;
            d.CardToGain = GetCardId(item.CardToGain);
            d.CardToGainType = item.CardToGainType;
            d.CardPlace = item.CardPlace;
            d.CardToGainList = new List<string>();
            if (item.CardToGainList != null)
                foreach (var c in item.CardToGainList)
                    if (c != null) d.CardToGainList.Add(c.Id ?? "");

            d.CostZero = item.CostZero;
            d.CostReduction = item.CostReduction;
            d.CardsReduced = item.CardsReduced;
            d.CardToReduceType = item.CardToReduceType;
            d.CostReduceReduction = item.CostReduceReduction;
            d.CostReduceEnergyRequirement = item.CostReduceEnergyRequirement;
            d.CostReducePermanent = item.CostReducePermanent;
            d.ReduceHighestCost = item.ReduceHighestCost;

            d.PercentRetentionEndGame = item.PercentRetentionEndGame;
            d.PercentDiscountShop = item.PercentDiscountShop;

            d.DamageToTarget = item.DamageToTarget1;
            d.DamageToTargetType = item.DamageToTargetType1;
            d.DttMultiplyByEnergyUsed = item.DttMultiplyByEnergyUsed;
            d.DttSpecialValues1 = SnapSV(item.DttSpecialValues1);
            d.DamageToTarget2 = item.DamageToTarget2;
            d.DamageToTargetType2 = item.DamageToTargetType2;
            d.DttSpecialValues2 = SnapSV(item.DttSpecialValues2);
            d.ModifiedDamageType = item.ModifiedDamageType;

            d.CursedItem = item.CursedItem;
            d.DropOnly = item.DropOnly;
            d.QuestItem = item.QuestItem;
            d.DestroyAfterUse = item.DestroyAfterUse;
            d.Vanish = item.Vanish;
            d.Permanent = item.Permanent;
            d.DuplicateActive = item.DuplicateActive;
            d.PassSingleAndCharacterRolls = item.PassSingleAndCharacterRolls;
            d.OnlyAddItemToNPCs = item.OnlyAddItemToNPCs;
            d.AddVanishToDeck = item.AddVanishToDeck;

            d.IsEnchantment = item.IsEnchantment;
            d.UseTheNextInsteadWhenYouPlay = item.UseTheNextInsteadWhenYouPlay;
            d.DestroyAfterUses = item.DestroyAfterUses;
            d.DestroyStartOfTurn = item.DestroyStartOfTurn;
            d.DestroyEndOfTurn = item.DestroyEndOfTurn;
            d.CastEnchantmentOnFinishSelfCast = item.CastEnchantmentOnFinishSelfCast;

            d.AuracurseCustomString = item.AuracurseCustomString ?? "";
            d.AuracurseCustomAC = GetACId(item.AuracurseCustomAC);
            d.AuracurseCustomModValue1 = item.AuracurseCustomModValue1;
            d.AuracurseCustomModValue2 = item.AuracurseCustomModValue2;

            d.EffectItemOwner = item.EffectItemOwner ?? "";
            d.EffectCaster = item.EffectCaster ?? "";
            d.EffectCasterDelay = item.EffectCasterDelay;
            d.EffectTarget = item.EffectTarget ?? "";
            d.EffectTargetDelay = item.EffectTargetDelay;

            return d;
        }

        /// <summary>Alias for MakeFullItem.</summary>
        public static ItemData MakeItem(ItemDef d) => MakeFullItem(d);
    }
}
