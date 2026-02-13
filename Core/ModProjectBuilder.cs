using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Converts mod DTOs into game ScriptableObjects and registers them in Globals.Instance.
    /// Called after <see cref="ModProjectLoader"/> has populated a <see cref="ModProject"/>.
    /// </summary>
    public static class ModProjectBuilder
    {
        // ═══════════════════════════════════════════════════════════════
        //  BUILD A SINGLE MOD
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build all new content + patches from a ModProject.
        /// New content is registered additively; patches replace existing entries.
        /// </summary>
        public static void Build(ModProject proj)
        {
            Plugin.Log.LogInfo($"[Builder] Building mod '{proj.ModId}'...");

            // ── AuraCurse (build first — referenced by cards, items, etc.) ──
            BuildAuraCurses(proj.AuraCurses, isNew: true);
            BuildAuraCurses(proj.AuraCursePatches, isNew: false);

            // ── Cards ────────────────────────────────────────────────
            BuildCards(proj.Cards, isNew: true);
            BuildCards(proj.CardPatches, isNew: false);

            // ── Items ────────────────────────────────────────────────
            BuildItems(proj.Items);
            BuildItems(proj.ItemPatches);

            // ── Loot ─────────────────────────────────────────────────
            BuildLoot(proj.Loot);
            BuildLoot(proj.LootPatches);

            // ── NPCs ─────────────────────────────────────────────────
            BuildNpcs(proj.Npcs);
            BuildNpcs(proj.NpcPatches);

            // ── Sprites ──────────────────────────────────────────────
            // Sprites are applied at runtime by NpcPrefabBuilder; just register defs.
            // No SO creation needed right now — the defs are stored in the project.

            // ── Zones ────────────────────────────────────────────────
            foreach (var zone in proj.Zones.Values)
                BuildZone(zone);

            // ── Zone patches ─────────────────────────────────────────
            foreach (var patch in proj.ZonePatches.Values)
                ApplyZonePatch(patch);

            // ── Heroes ────────────────────────────────────────────────
            BuildHeroes(proj.Heroes);
            BuildHeroes(proj.HeroPatches);

            // ── Traits ────────────────────────────────────────────────
            BuildTraits(proj.Traits);
            BuildTraits(proj.TraitPatches);

            // ── Skins ─────────────────────────────────────────────────
            BuildSkins(proj.Skins);
            BuildSkins(proj.SkinPatches);

            // ── Perks ─────────────────────────────────────────────────
            BuildPerks(proj.Perks);
            BuildPerks(proj.PerkPatches);

            // ── PerkNodes ─────────────────────────────────────────────
            BuildPerkNodes(proj.PerkNodes);
            BuildPerkNodes(proj.PerkNodePatches);

            // ── Requirements ─────────────────────────────────────────
            BuildRequirements(proj.Requirements);
            BuildRequirements(proj.RequirementPatches);

            // ── Cardbacks ──────────────────────────────────────────────
            BuildCardbacks(proj.Cardbacks);
            BuildCardbacks(proj.CardbackPatches);

            // ── TierRewards ───────────────────────────────────────────
            BuildTierRewards(proj.TierRewards);
            BuildTierRewards(proj.TierRewardPatches);

            Plugin.Log.LogInfo($"[Builder] Mod '{proj.ModId}' built successfully.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  BUILD ALL MODS (runtime load-order sequence)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build all mods in load-order sequence.
        /// Later mods override earlier ones' patches.
        /// </summary>
        public static void BuildAll(List<ModProject> mods)
        {
            Plugin.Log.LogInfo($"[Builder] Building {mods.Count} mod(s) in load order...");
            foreach (var mod in mods)
            {
                try
                {
                    Build(mod);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[Builder] Error building mod '{mod.ModId}': {ex.Message}");
                    Plugin.Log.LogError(ex.StackTrace);
                }
            }
            Plugin.Log.LogInfo("[Builder] All mods built.");
        }

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

        // ═══════════════════════════════════════════════════════════════
        //  CARDS / ITEMS / LOOT / NPCS
        // ═══════════════════════════════════════════════════════════════

        private static void BuildCards(Dictionary<string, CardDef> defs, bool isNew)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var card = MakeFullCard(kvp.Value);
                    DataHelper.RegisterCard(card);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Card '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildItems(Dictionary<string, ItemDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var item = DataHelper.MakeFullItem(kvp.Value);
                    DataHelper.RegisterItem(item);
                    // Also register the paired equipment card
                    var card = DataHelper.MakeItemCard(kvp.Value, item);
                    DataHelper.RegisterCard(card);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Item '{kvp.Key}': {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CARD: Full Build + Snapshot
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create a complete CardData SO from a CardDef, setting ALL fields.
        /// This replaces the old MakeCard+ApplyCardExtras flow for mod-level cards.
        /// </summary>
        public static CardData MakeFullCard(CardDef d)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            var t = Traverse.Create(card);

            // ── Identity ─────────────────────────────────────────
            t.Field("id").SetValue(d.Id);
            t.Field("internalId").SetValue(d.Id);
            t.Field("cardName").SetValue(d.Name ?? "");
            t.Field("description").SetValue(d.Description ?? "");
            t.Field("fluff").SetValue(d.Fluff ?? "");
            t.Field("fluffPercent").SetValue(d.FluffPercent);
            t.Field("sku").SetValue(d.Sku ?? "");

            // ── Classification ───────────────────────────────────
            t.Field("cardUpgraded").SetValue(d.CardUpgraded);
            t.Field("cardRarity").SetValue(d.CardRarity);
            t.Field("cardType").SetValue(d.CardType);
            t.Field("cardTypeAux").SetValue(d.CardTypeAux ?? Array.Empty<Enums.CardType>());
            t.Field("cardClass").SetValue(d.CardClass);
            t.Field("cardNumber").SetValue(d.CardNumber);
            t.Field("maxInDeck").SetValue(d.MaxInDeck);

            // ── Cost / Economy ───────────────────────────────────
            t.Field("energyCost").SetValue(d.EnergyCost);
            t.Field("energyCostOriginal").SetValue(d.EnergyCostOriginal);
            t.Field("energyCostForShow").SetValue(d.EnergyCostForShow);
            t.Field("energyReductionPermanent").SetValue(d.EnergyReductionPermanent);
            t.Field("energyReductionTemporal").SetValue(d.EnergyReductionTemporal);
            t.Field("energyReductionToZeroPermanent").SetValue(d.EnergyReductionToZeroPermanent);
            t.Field("energyReductionToZeroTemporal").SetValue(d.EnergyReductionToZeroTemporal);

            // ── Flags ────────────────────────────────────────────
            t.Field("playable").SetValue(d.Playable);
            t.Field("visible").SetValue(d.Visible);
            t.Field("showInTome").SetValue(d.ShowInTome);
            t.Field("autoplayDraw").SetValue(d.AutoplayDraw);
            t.Field("autoplayEndTurn").SetValue(d.AutoplayEndTurn);
            t.Field("vanish").SetValue(d.Vanish);
            t.Field("innate").SetValue(d.Innate);
            t.Field("lazy").SetValue(d.Lazy);
            t.Field("corrupted").SetValue(d.Corrupted);
            t.Field("endTurn").SetValue(d.EndTurn);
            t.Field("starter").SetValue(d.Starter);
            t.Field("flipSprite").SetValue(d.FlipSprite);
            t.Field("modifiedByTrait").SetValue(d.ModifiedByTrait);
            t.Field("onlyInWeekly").SetValue(d.OnlyInWeekly);

            // ── Upgrade Paths ────────────────────────────────────
            t.Field("upgradesTo1").SetValue(d.UpgradesTo1 ?? "");
            t.Field("upgradesTo2").SetValue(d.UpgradesTo2 ?? "");
            t.Field("upgradedFrom").SetValue(d.UpgradedFrom ?? "");
            t.Field("baseCard").SetValue(d.BaseCard ?? "");
            t.Field("relatedCard").SetValue(d.RelatedCard ?? "");
            t.Field("relatedCard2").SetValue(d.RelatedCard2 ?? "");
            t.Field("relatedCard3").SetValue(d.RelatedCard3 ?? "");

            // ── Targeting ────────────────────────────────────────
            t.Field("targetSide").SetValue(d.TargetSide);
            t.Field("targetType").SetValue(d.TargetType);
            t.Field("targetPosition").SetValue(d.TargetPos);

            // ── Damage 1 ─────────────────────────────────────────
            t.Field("damage").SetValue(d.Damage);
            t.Field("damageType").SetValue(d.DamageType);
            t.Field("damageSides").SetValue(d.DamageSides);
            t.Field("damageEnergyBonus").SetValue(d.DamageEnergyBonus);
            t.Field("ignoreBlock").SetValue(d.IgnoreBlock);
            t.Field("damageSelf").SetValue(d.DamageSelf);
            t.Field("damageSpecialValueGlobal").SetValue(d.DamageSpecialValueGlobal);
            t.Field("damageSpecialValue1").SetValue(d.DamageSpecialValue1);
            t.Field("damageSpecialValue2").SetValue(d.DamageSpecialValue2);

            // ── Damage 2 ─────────────────────────────────────────
            t.Field("damage2").SetValue(d.Damage2);
            t.Field("damageType2").SetValue(d.DamageType2);
            t.Field("damageSides2").SetValue(d.DamageSides2);
            t.Field("ignoreBlock2").SetValue(d.IgnoreBlock2);
            t.Field("damageSelf2").SetValue(d.DamageSelf2);
            t.Field("damage2SpecialValueGlobal").SetValue(d.Damage2SpecialValueGlobal);
            t.Field("damage2SpecialValue1").SetValue(d.Damage2SpecialValue1);
            t.Field("damage2SpecialValue2").SetValue(d.Damage2SpecialValue2);

            // ── Self HP Loss ─────────────────────────────────────
            t.Field("selfHealthLoss").SetValue(d.SelfHealthLoss);
            t.Field("selfHealthLossSpecialGlobal").SetValue(d.SelfHealthLossSpecialGlobal);
            t.Field("selfHealthLossSpecialValue1").SetValue(d.SelfHealthLossSpecialValue1);
            t.Field("selfHealthLossSpecialValue2").SetValue(d.SelfHealthLossSpecialValue2);
            t.Field("selfKillHiddenSeconds").SetValue(d.SelfKillHiddenSeconds);

            // ── Curses (target) ──────────────────────────────────
            if (!string.IsNullOrEmpty(d.Curse))
                t.Field("curse").SetValue(DataHelper.GetAuraCurse(d.Curse));
            t.Field("curseCharges").SetValue(d.CurseCharges);
            t.Field("curseChargesSides").SetValue(d.CurseChargesSides);
            t.Field("curseChargesSpecialValueGlobal").SetValue(d.CurseChargesSpecialValueGlobal);
            t.Field("curseChargesSpecialValue1").SetValue(d.CurseChargesSpecialValue1);
            t.Field("curseChargesSpecialValue2").SetValue(d.CurseChargesSpecialValue2);
            if (!string.IsNullOrEmpty(d.CurseSelf))
                t.Field("curseSelf").SetValue(DataHelper.GetAuraCurse(d.CurseSelf));
            t.Field("curseCharges").SetValue(d.CurseCharges);

            if (!string.IsNullOrEmpty(d.Curse2))
                t.Field("curse2").SetValue(DataHelper.GetAuraCurse(d.Curse2));
            t.Field("curseCharges2").SetValue(d.Curse2Charges);
            t.Field("curseCharges2SpecialValueGlobal").SetValue(d.CurseCharges2SpecialValueGlobal);
            t.Field("curseCharges2SpecialValue1").SetValue(d.CurseCharges2SpecialValue1);
            t.Field("curseCharges2SpecialValue2").SetValue(d.CurseCharges2SpecialValue2);
            if (!string.IsNullOrEmpty(d.CurseSelf2))
                t.Field("curseSelf2").SetValue(DataHelper.GetAuraCurse(d.CurseSelf2));
            if (d.CurseSelf2Charges > 0 && d.Curse2Charges == 0)
                t.Field("curseCharges2").SetValue(d.CurseSelf2Charges);

            if (!string.IsNullOrEmpty(d.Curse3))
                t.Field("curse3").SetValue(DataHelper.GetAuraCurse(d.Curse3));
            t.Field("curseCharges3").SetValue(d.Curse3Charges);
            t.Field("curseCharges3SpecialValueGlobal").SetValue(d.CurseCharges3SpecialValueGlobal);
            t.Field("curseCharges3SpecialValue1").SetValue(d.CurseCharges3SpecialValue1);
            t.Field("curseCharges3SpecialValue2").SetValue(d.CurseCharges3SpecialValue2);
            if (!string.IsNullOrEmpty(d.CurseSelf3))
                t.Field("curseSelf3").SetValue(DataHelper.GetAuraCurse(d.CurseSelf3));
            if (d.CurseSelf3Charges > 0 && d.Curse3Charges == 0)
                t.Field("curseCharges3").SetValue(d.CurseSelf3Charges);

            // ── Auras (target) ───────────────────────────────────
            if (!string.IsNullOrEmpty(d.Aura))
                t.Field("aura").SetValue(DataHelper.GetAuraCurse(d.Aura));
            t.Field("auraCharges").SetValue(d.AuraCharges);
            t.Field("auraChargesSpecialValueGlobal").SetValue(d.AuraChargesSpecialValueGlobal);
            t.Field("auraChargesSpecialValue1").SetValue(d.AuraChargesSpecialValue1);
            t.Field("auraChargesSpecialValue2").SetValue(d.AuraChargesSpecialValue2);
            if (!string.IsNullOrEmpty(d.AuraSelf))
                t.Field("auraSelf").SetValue(DataHelper.GetAuraCurse(d.AuraSelf));
            if (d.AuraSelfCharges > 0 && d.AuraCharges == 0)
                t.Field("auraCharges").SetValue(d.AuraSelfCharges);

            if (!string.IsNullOrEmpty(d.Aura2))
                t.Field("aura2").SetValue(DataHelper.GetAuraCurse(d.Aura2));
            t.Field("auraCharges2").SetValue(d.Aura2Charges);
            t.Field("auraCharges2SpecialValueGlobal").SetValue(d.AuraCharges2SpecialValueGlobal);
            t.Field("auraCharges2SpecialValue1").SetValue(d.AuraCharges2SpecialValue1);
            t.Field("auraCharges2SpecialValue2").SetValue(d.AuraCharges2SpecialValue2);
            if (!string.IsNullOrEmpty(d.AuraSelf2))
                t.Field("auraSelf2").SetValue(DataHelper.GetAuraCurse(d.AuraSelf2));
            if (d.AuraSelf2Charges > 0 && d.Aura2Charges == 0)
                t.Field("auraCharges2").SetValue(d.AuraSelf2Charges);

            if (!string.IsNullOrEmpty(d.Aura3))
                t.Field("aura3").SetValue(DataHelper.GetAuraCurse(d.Aura3));
            t.Field("auraCharges3").SetValue(d.Aura3Charges);
            t.Field("auraCharges3SpecialValueGlobal").SetValue(d.AuraCharges3SpecialValueGlobal);
            t.Field("auraCharges3SpecialValue1").SetValue(d.AuraCharges3SpecialValue1);
            t.Field("auraCharges3SpecialValue2").SetValue(d.AuraCharges3SpecialValue2);
            if (!string.IsNullOrEmpty(d.AuraSelf3))
                t.Field("auraSelf3").SetValue(DataHelper.GetAuraCurse(d.AuraSelf3));
            if (d.AuraSelf3Charges > 0 && d.Aura3Charges == 0)
                t.Field("auraCharges3").SetValue(d.AuraSelf3Charges);

            // ── Heal ─────────────────────────────────────────────
            t.Field("heal").SetValue(d.Heal);
            t.Field("healSides").SetValue(d.HealSides);
            t.Field("healSelf").SetValue(d.HealSelf);
            t.Field("healEnergyBonus").SetValue(d.HealEnergyBonus);
            t.Field("healSelfPerDamageDonePercent").SetValue(d.HealSelfPerDamageDonePercent);
            t.Field("healCurses").SetValue(d.HealCurses);
            t.Field("dispelAuras").SetValue(d.DispelAuras);
            t.Field("healSpecialValueGlobal").SetValue(d.HealSpecialValueGlobal);
            t.Field("healSpecialValue1").SetValue(d.HealSpecialValue1);
            t.Field("healSpecialValue2").SetValue(d.HealSpecialValue2);
            t.Field("healSelfSpecialValueGlobal").SetValue(d.HealSelfSpecialValueGlobal);
            t.Field("healSelfSpecialValue1").SetValue(d.HealSelfSpecialValue1);
            t.Field("healSelfSpecialValue2").SetValue(d.HealSelfSpecialValue2);

            if (!string.IsNullOrEmpty(d.HealAuraCurseSelf))
                t.Field("healAuraCurseSelf").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseSelf));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName))
                t.Field("healAuraCurseName").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseName));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName2))
                t.Field("healAuraCurseName2").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseName2));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName3))
                t.Field("healAuraCurseName3").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseName3));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName4))
                t.Field("healAuraCurseName4").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseName4));

            // ── AC Manipulation ──────────────────────────────────
            t.Field("transferCurses").SetValue(d.TransferCurses);
            t.Field("stealAuras").SetValue(d.StealAuras);
            t.Field("reduceCurses").SetValue(d.ReduceCurses);
            t.Field("reduceAuras").SetValue(d.ReduceAuras);
            t.Field("increaseCurses").SetValue(d.IncreaseCurses);
            t.Field("increaseAuras").SetValue(d.IncreaseAuras);

            // ── Effect Repeat ────────────────────────────────────
            t.Field("effectRepeat").SetValue(d.EffectRepeat);
            t.Field("effectRepeatDelay").SetValue(d.EffectRepeatDelay);
            t.Field("effectRepeatEnergyBonus").SetValue(d.EffectRepeatEnergyBonus);
            t.Field("effectRepeatMaxBonus").SetValue(d.EffectRepeatMaxBonus);
            t.Field("effectRepeatModificator").SetValue(d.EffectRepeatModificator);
            t.Field("effectRepeatTarget").SetValue(d.EffectRepeatTarget);

            // ── Misc Mechanics ───────────────────────────────────
            t.Field("moveToCenter").SetValue(d.MoveToCenter);
            t.Field("pushTarget").SetValue(d.PushTarget);
            t.Field("pullTarget").SetValue(d.PullTarget);
            t.Field("drawCard").SetValue(d.DrawCard);
            t.Field("drawCardSpecialValueGlobal").SetValue(d.DrawCardSpecialValueGlobal);
            t.Field("discardCard").SetValue(d.DiscardCard);
            t.Field("energyRecharge").SetValue(d.EnergyRecharge);
            t.Field("energyRechargeSpecialValueGlobal").SetValue(d.EnergyRechargeSpecialValueGlobal);
            t.Field("goldGainQuantity").SetValue(d.GoldGainQuantity);
            t.Field("shardsGainQuantity").SetValue(d.ShardsGainQuantity);
            t.Field("exhaustCounter").SetValue(d.ExhaustCounter);
            t.Field("effectRequired").SetValue(d.EffectRequired ?? "");

            // ── Discard Options ──────────────────────────────────
            t.Field("discardCardType").SetValue(d.DiscardCardType);
            t.Field("discardCardTypeAux").SetValue(d.DiscardCardTypeAux ?? Array.Empty<Enums.CardType>());
            t.Field("discardCardAutomatic").SetValue(d.DiscardCardAutomatic);
            t.Field("discardCardPlace").SetValue(d.DiscardCardPlace);

            // ── Add Card ─────────────────────────────────────────
            t.Field("addCard").SetValue(d.AddCard);
            t.Field("addCardId").SetValue(d.AddCardId ?? "");
            t.Field("addCardType").SetValue(d.AddCardType);
            t.Field("addCardTypeAux").SetValue(d.AddCardTypeAux ?? Array.Empty<Enums.CardType>());
            t.Field("addCardChoose").SetValue(d.AddCardChoose);
            t.Field("addCardFrom").SetValue(d.AddCardFrom);
            t.Field("addCardPlace").SetValue(d.AddCardPlace);
            t.Field("addCardReducedCost").SetValue(d.AddCardReducedCost);
            t.Field("addCardCostTurn").SetValue(d.AddCardCostTurn);
            t.Field("addCardVanish").SetValue(d.AddCardVanish);
            t.Field("addCardOnlyCheckAuxTypes").SetValue(d.AddCardOnlyCheckAuxTypes);
            card.AddCardFromVanishPile = d.AddCardFromVanishPile;
            t.Field("addVanishToDeck").SetValue(d.AddVanishToDeck);

            // AddCardList: resolve IDs to CardData[]
            if (d.AddCardList != null && d.AddCardList.Count > 0)
            {
                var resolved = new List<CardData>();
                foreach (var cid in d.AddCardList)
                {
                    var c = DataHelper.GetCard(cid);
                    if (c != null) resolved.Add(c);
                }
                t.Field("addCardList").SetValue(resolved.ToArray());
            }
            else
            {
                t.Field("addCardList").SetValue(new CardData[0]);
            }

            // ── Look / Scry ─────────────────────────────────────
            t.Field("lookCards").SetValue(d.LookCards);
            t.Field("lookCardsDiscardUpTo").SetValue(d.LookCardsDiscardUpTo);
            t.Field("lookCardsVanishUpTo").SetValue(d.LookCardsVanishUpTo);

            // ── Summon ───────────────────────────────────────────
            if (!string.IsNullOrEmpty(d.SummonUnitId))
            {
                var npc = DataHelper.GetExistingNPC(d.SummonUnitId);
                if (npc != null) t.Field("summonUnit").SetValue(npc);
            }
            t.Field("summonUnitNum").SetValue(d.SummonNum);
            t.Field("evolve").SetValue(d.Evolve);
            t.Field("metamorph").SetValue(d.Metamorph);
            if (!string.IsNullOrEmpty(d.SummonAura))
                t.Field("summonAura").SetValue(DataHelper.GetAuraCurse(d.SummonAura));
            t.Field("summonAuraCharges").SetValue(d.SummonAuraCharges);
            if (!string.IsNullOrEmpty(d.SummonAura2))
                t.Field("summonAura2").SetValue(DataHelper.GetAuraCurse(d.SummonAura2));
            t.Field("summonAuraCharges2").SetValue(d.SummonAuraCharges2);
            if (!string.IsNullOrEmpty(d.SummonAura3))
                t.Field("summonAura3").SetValue(DataHelper.GetAuraCurse(d.SummonAura3));
            t.Field("summonAuraCharges3").SetValue(d.SummonAuraCharges3);

            // ── AC Energy Bonus ──────────────────────────────────
            if (!string.IsNullOrEmpty(d.AcEnergyBonus))
                t.Field("acEnergyBonus").SetValue(DataHelper.GetAuraCurse(d.AcEnergyBonus));
            t.Field("acEnergyBonusQuantity").SetValue(d.AcEnergyBonusQuantity);
            if (!string.IsNullOrEmpty(d.AcEnergyBonus2))
                t.Field("acEnergyBonus2").SetValue(DataHelper.GetAuraCurse(d.AcEnergyBonus2));
            t.Field("acEnergyBonus2Quantity").SetValue(d.AcEnergyBonus2Quantity);
            t.Field("chooseOneOfAvailableAuras").SetValue(d.ChooseOneOfAvailableAuras);

            // ── Special Value System ─────────────────────────────
            t.Field("specialValueGlobal").SetValue(d.SpecialValueGlobal);
            t.Field("specialValueModifierGlobal").SetValue(d.SpecialValueModifierGlobal);
            if (!string.IsNullOrEmpty(d.SpecialAuraCurseNameGlobal))
                t.Field("specialAuraCurseNameGlobal").SetValue(DataHelper.GetAuraCurse(d.SpecialAuraCurseNameGlobal));
            t.Field("specialValue1").SetValue(d.SpecialValue1);
            t.Field("specialValueModifier1").SetValue(d.SpecialValueModifier1);
            if (!string.IsNullOrEmpty(d.SpecialAuraCurseName1))
                t.Field("specialAuraCurseName1").SetValue(DataHelper.GetAuraCurse(d.SpecialAuraCurseName1));
            t.Field("specialValue2").SetValue(d.SpecialValue2);
            t.Field("specialValueModifier2").SetValue(d.SpecialValueModifier2);
            if (!string.IsNullOrEmpty(d.SpecialAuraCurseName2))
                t.Field("specialAuraCurseName2").SetValue(DataHelper.GetAuraCurse(d.SpecialAuraCurseName2));

            // ── FX / Effects ─────────────────────────────────────
            t.Field("effectCaster").SetValue(d.EffectCaster ?? "");
            t.Field("effectTarget").SetValue(d.EffectTarget ?? "");
            t.Field("effectPreAction").SetValue(d.EffectPreAction ?? "");
            t.Field("effectPostCastDelay").SetValue(d.EffectPostCastDelay);
            t.Field("effectCasterRepeat").SetValue(d.EffectCasterRepeat);
            t.Field("effectCastCenter").SetValue(d.EffectCastCenter);
            t.Field("effectTrail").SetValue(d.EffectTrail ?? "");
            t.Field("effectTrailRepeat").SetValue(d.EffectTrailRepeat);
            t.Field("effectTrailSpeed").SetValue(d.EffectTrailSpeed);
            t.Field("effectTrailAngle").SetValue(d.EffectTrailAngle);
            t.Field("effectPostTargetDelay").SetValue(d.EffectPostTargetDelay);

            // ── Pet System ───────────────────────────────────────
            card.PetActivation = d.PetActivation;
            card.PetBonusDamageType = d.PetBonusDamageType;
            card.PetBonusDamageAmount = d.PetBonusDamageAmount;
            t.Field("isPetAttack").SetValue(d.IsPetAttack);
            t.Field("isPetCast").SetValue(d.IsPetCast);
            t.Field("killPet").SetValue(d.KillPet);
            t.Field("petTemporal").SetValue(d.PetTemporal);
            t.Field("petTemporalAttack").SetValue(d.PetTemporalAttack);
            t.Field("petTemporalCast").SetValue(d.PetTemporalCast);
            t.Field("petTemporalMoveToCenter").SetValue(d.PetTemporalMoveToCenter);
            t.Field("petTemporalMoveToBack").SetValue(d.PetTemporalMoveToBack);
            t.Field("petTemporalFadeOutDelay").SetValue(d.PetTemporalFadeOutDelay);

            // Initialize remaining arrays to prevent NREs
            t.Field("preDescriptionArgs").SetValue(new string[0]);
            t.Field("descriptionArgs").SetValue(new string[0]);
            t.Field("postDescriptionArgs").SetValue(new string[0]);

            return card;
        }

        /// <summary>
        /// Snapshot a live CardData SO into a CardDef for override editing.
        /// </summary>
        public static CardDef SnapshotCard(CardData card)
        {
            if (card == null) return null;
            var d = new CardDef();
            var t = Traverse.Create(card);

            // ── Identity ─────────────────────────────────────────
            d.Id = t.Field<string>("id").Value ?? "";
            d.Name = t.Field<string>("cardName").Value ?? "";
            d.Description = t.Field<string>("description").Value ?? "";
            d.Fluff = t.Field<string>("fluff").Value ?? "";
            d.FluffPercent = t.Field<float>("fluffPercent").Value;
            d.Sku = t.Field<string>("sku").Value ?? "";

            // ── Classification ───────────────────────────────────
            d.CardUpgraded = t.Field<Enums.CardUpgraded>("cardUpgraded").Value;
            d.IsUpgraded = d.CardUpgraded != Enums.CardUpgraded.No;
            d.CardRarity = t.Field<Enums.CardRarity>("cardRarity").Value;
            d.CardType = t.Field<Enums.CardType>("cardType").Value;
            d.CardTypeAux = t.Field<Enums.CardType[]>("cardTypeAux").Value ?? Array.Empty<Enums.CardType>();
            d.CardClass = t.Field<Enums.CardClass>("cardClass").Value;
            d.CardNumber = t.Field<int>("cardNumber").Value;
            d.MaxInDeck = t.Field<int>("maxInDeck").Value;

            // ── Cost / Economy ───────────────────────────────────
            d.EnergyCost = t.Field<int>("energyCost").Value;
            d.EnergyCostOriginal = t.Field<int>("energyCostOriginal").Value;
            d.EnergyCostForShow = t.Field<int>("energyCostForShow").Value;
            d.EnergyReductionPermanent = t.Field<int>("energyReductionPermanent").Value;
            d.EnergyReductionTemporal = t.Field<int>("energyReductionTemporal").Value;
            d.EnergyReductionToZeroPermanent = t.Field<bool>("energyReductionToZeroPermanent").Value;
            d.EnergyReductionToZeroTemporal = t.Field<bool>("energyReductionToZeroTemporal").Value;

            // ── Flags ────────────────────────────────────────────
            d.Playable = t.Field<bool>("playable").Value;
            d.Visible = t.Field<bool>("visible").Value;
            d.ShowInTome = t.Field<bool>("showInTome").Value;
            d.AutoplayDraw = t.Field<bool>("autoplayDraw").Value;
            d.AutoplayEndTurn = t.Field<bool>("autoplayEndTurn").Value;
            d.Vanish = t.Field<bool>("vanish").Value;
            d.Innate = t.Field<bool>("innate").Value;
            d.Lazy = t.Field<bool>("lazy").Value;
            d.Corrupted = t.Field<bool>("corrupted").Value;
            d.EndTurn = t.Field<bool>("endTurn").Value;
            d.Starter = t.Field<bool>("starter").Value;
            d.FlipSprite = t.Field<bool>("flipSprite").Value;
            d.ModifiedByTrait = t.Field<bool>("modifiedByTrait").Value;
            d.OnlyInWeekly = t.Field<bool>("onlyInWeekly").Value;

            // ── Upgrade Paths ────────────────────────────────────
            d.UpgradesTo1 = t.Field<string>("upgradesTo1").Value ?? "";
            d.UpgradesTo2 = t.Field<string>("upgradesTo2").Value ?? "";
            d.UpgradedFrom = t.Field<string>("upgradedFrom").Value ?? "";
            d.BaseCard = t.Field<string>("baseCard").Value ?? "";
            d.BaseCardId = d.BaseCard;
            d.RelatedCard = t.Field<string>("relatedCard").Value ?? "";
            d.RelatedCard2 = t.Field<string>("relatedCard2").Value ?? "";
            d.RelatedCard3 = t.Field<string>("relatedCard3").Value ?? "";

            // ── Targeting ────────────────────────────────────────
            d.TargetSide = t.Field<Enums.CardTargetSide>("targetSide").Value;
            d.TargetType = t.Field<Enums.CardTargetType>("targetType").Value;
            d.TargetPos = t.Field<Enums.CardTargetPosition>("targetPosition").Value;

            // ── Damage 1 ─────────────────────────────────────────
            d.Damage = t.Field<int>("damage").Value;
            d.DamageType = t.Field<Enums.DamageType>("damageType").Value;
            d.DamageSides = t.Field<int>("damageSides").Value;
            d.DamageEnergyBonus = t.Field<int>("damageEnergyBonus").Value;
            d.IgnoreBlock = t.Field<bool>("ignoreBlock").Value;
            d.DamageSelf = t.Field<int>("damageSelf").Value;
            d.DamageSpecialValueGlobal = t.Field<bool>("damageSpecialValueGlobal").Value;
            d.DamageSpecialValue1 = t.Field<bool>("damageSpecialValue1").Value;
            d.DamageSpecialValue2 = t.Field<bool>("damageSpecialValue2").Value;

            // ── Damage 2 ─────────────────────────────────────────
            d.Damage2 = t.Field<int>("damage2").Value;
            d.DamageType2 = t.Field<Enums.DamageType>("damageType2").Value;
            d.DamageSides2 = t.Field<int>("damageSides2").Value;
            d.IgnoreBlock2 = t.Field<bool>("ignoreBlock2").Value;
            d.DamageSelf2 = t.Field<int>("damageSelf2").Value;
            d.Damage2SpecialValueGlobal = t.Field<bool>("damage2SpecialValueGlobal").Value;
            d.Damage2SpecialValue1 = t.Field<bool>("damage2SpecialValue1").Value;
            d.Damage2SpecialValue2 = t.Field<bool>("damage2SpecialValue2").Value;

            // ── Self HP Loss ─────────────────────────────────────
            d.SelfHealthLoss = t.Field<int>("selfHealthLoss").Value;
            d.SelfHealthLossSpecialGlobal = t.Field<bool>("selfHealthLossSpecialGlobal").Value;
            d.SelfHealthLossSpecialValue1 = t.Field<bool>("selfHealthLossSpecialValue1").Value;
            d.SelfHealthLossSpecialValue2 = t.Field<bool>("selfHealthLossSpecialValue2").Value;
            d.SelfKillHiddenSeconds = t.Field<float>("selfKillHiddenSeconds").Value;

            // ── Curses ───────────────────────────────────────────
            d.Curse = GetACId(t.Field<AuraCurseData>("curse").Value);
            d.CurseCharges = t.Field<int>("curseCharges").Value;
            d.CurseChargesSides = t.Field<int>("curseChargesSides").Value;
            d.CurseChargesSpecialValueGlobal = t.Field<bool>("curseChargesSpecialValueGlobal").Value;
            d.CurseChargesSpecialValue1 = t.Field<bool>("curseChargesSpecialValue1").Value;
            d.CurseChargesSpecialValue2 = t.Field<bool>("curseChargesSpecialValue2").Value;
            d.CurseSelf = GetACId(t.Field<AuraCurseData>("curseSelf").Value);
            d.CurseSelfCharges = d.CurseCharges; // shared field

            d.Curse2 = GetACId(t.Field<AuraCurseData>("curse2").Value);
            d.Curse2Charges = t.Field<int>("curseCharges2").Value;
            d.CurseCharges2SpecialValueGlobal = t.Field<bool>("curseCharges2SpecialValueGlobal").Value;
            d.CurseCharges2SpecialValue1 = t.Field<bool>("curseCharges2SpecialValue1").Value;
            d.CurseCharges2SpecialValue2 = t.Field<bool>("curseCharges2SpecialValue2").Value;
            d.CurseSelf2 = GetACId(t.Field<AuraCurseData>("curseSelf2").Value);
            d.CurseSelf2Charges = d.Curse2Charges;

            d.Curse3 = GetACId(t.Field<AuraCurseData>("curse3").Value);
            d.Curse3Charges = t.Field<int>("curseCharges3").Value;
            d.CurseCharges3SpecialValueGlobal = t.Field<bool>("curseCharges3SpecialValueGlobal").Value;
            d.CurseCharges3SpecialValue1 = t.Field<bool>("curseCharges3SpecialValue1").Value;
            d.CurseCharges3SpecialValue2 = t.Field<bool>("curseCharges3SpecialValue2").Value;
            d.CurseSelf3 = GetACId(t.Field<AuraCurseData>("curseSelf3").Value);
            d.CurseSelf3Charges = d.Curse3Charges;

            // ── Auras ────────────────────────────────────────────
            d.Aura = GetACId(t.Field<AuraCurseData>("aura").Value);
            d.AuraCharges = t.Field<int>("auraCharges").Value;
            d.AuraChargesSpecialValueGlobal = t.Field<bool>("auraChargesSpecialValueGlobal").Value;
            d.AuraChargesSpecialValue1 = t.Field<bool>("auraChargesSpecialValue1").Value;
            d.AuraChargesSpecialValue2 = t.Field<bool>("auraChargesSpecialValue2").Value;
            d.AuraSelf = GetACId(t.Field<AuraCurseData>("auraSelf").Value);
            d.AuraSelfCharges = d.AuraCharges;

            d.Aura2 = GetACId(t.Field<AuraCurseData>("aura2").Value);
            d.Aura2Charges = t.Field<int>("auraCharges2").Value;
            d.AuraCharges2SpecialValueGlobal = t.Field<bool>("auraCharges2SpecialValueGlobal").Value;
            d.AuraCharges2SpecialValue1 = t.Field<bool>("auraCharges2SpecialValue1").Value;
            d.AuraCharges2SpecialValue2 = t.Field<bool>("auraCharges2SpecialValue2").Value;
            d.AuraSelf2 = GetACId(t.Field<AuraCurseData>("auraSelf2").Value);
            d.AuraSelf2Charges = d.Aura2Charges;

            d.Aura3 = GetACId(t.Field<AuraCurseData>("aura3").Value);
            d.Aura3Charges = t.Field<int>("auraCharges3").Value;
            d.AuraCharges3SpecialValueGlobal = t.Field<bool>("auraCharges3SpecialValueGlobal").Value;
            d.AuraCharges3SpecialValue1 = t.Field<bool>("auraCharges3SpecialValue1").Value;
            d.AuraCharges3SpecialValue2 = t.Field<bool>("auraCharges3SpecialValue2").Value;
            d.AuraSelf3 = GetACId(t.Field<AuraCurseData>("auraSelf3").Value);
            d.AuraSelf3Charges = d.Aura3Charges;

            // ── Heal ─────────────────────────────────────────────
            d.Heal = t.Field<int>("heal").Value;
            d.HealSides = t.Field<int>("healSides").Value;
            d.HealSelf = t.Field<int>("healSelf").Value;
            d.HealEnergyBonus = t.Field<int>("healEnergyBonus").Value;
            d.HealSelfPerDamageDonePercent = t.Field<float>("healSelfPerDamageDonePercent").Value;
            d.HealCurses = t.Field<int>("healCurses").Value;
            d.DispelAuras = t.Field<int>("dispelAuras").Value;
            d.HealSpecialValueGlobal = t.Field<bool>("healSpecialValueGlobal").Value;
            d.HealSpecialValue1 = t.Field<bool>("healSpecialValue1").Value;
            d.HealSpecialValue2 = t.Field<bool>("healSpecialValue2").Value;
            d.HealSelfSpecialValueGlobal = t.Field<bool>("healSelfSpecialValueGlobal").Value;
            d.HealSelfSpecialValue1 = t.Field<bool>("healSelfSpecialValue1").Value;
            d.HealSelfSpecialValue2 = t.Field<bool>("healSelfSpecialValue2").Value;

            d.HealAuraCurseSelf = GetACId(t.Field<AuraCurseData>("healAuraCurseSelf").Value);
            d.HealAuraCurseName = GetACId(t.Field<AuraCurseData>("healAuraCurseName").Value);
            d.HealAuraCurseName2 = GetACId(t.Field<AuraCurseData>("healAuraCurseName2").Value);
            d.HealAuraCurseName3 = GetACId(t.Field<AuraCurseData>("healAuraCurseName3").Value);
            d.HealAuraCurseName4 = GetACId(t.Field<AuraCurseData>("healAuraCurseName4").Value);

            // ── AC Manipulation ──────────────────────────────────
            d.TransferCurses = t.Field<int>("transferCurses").Value;
            d.StealAuras = t.Field<int>("stealAuras").Value;
            d.ReduceCurses = t.Field<int>("reduceCurses").Value;
            d.ReduceAuras = t.Field<int>("reduceAuras").Value;
            d.IncreaseCurses = t.Field<int>("increaseCurses").Value;
            d.IncreaseAuras = t.Field<int>("increaseAuras").Value;

            // ── Effect Repeat ────────────────────────────────────
            d.EffectRepeat = t.Field<int>("effectRepeat").Value;
            d.EffectRepeatDelay = t.Field<float>("effectRepeatDelay").Value;
            d.EffectRepeatEnergyBonus = t.Field<int>("effectRepeatEnergyBonus").Value;
            d.EffectRepeatMaxBonus = t.Field<int>("effectRepeatMaxBonus").Value;
            d.EffectRepeatModificator = t.Field<int>("effectRepeatModificator").Value;
            d.EffectRepeatTarget = t.Field<Enums.EffectRepeatTarget>("effectRepeatTarget").Value;

            // ── Misc Mechanics ───────────────────────────────────
            d.MoveToCenter = t.Field<bool>("moveToCenter").Value;
            d.PushTarget = t.Field<int>("pushTarget").Value;
            d.PullTarget = t.Field<int>("pullTarget").Value;
            d.DrawCard = t.Field<int>("drawCard").Value;
            d.DrawCardSpecialValueGlobal = t.Field<bool>("drawCardSpecialValueGlobal").Value;
            d.DiscardCard = t.Field<int>("discardCard").Value;
            d.EnergyRecharge = t.Field<int>("energyRecharge").Value;
            d.EnergyRechargeSpecialValueGlobal = t.Field<bool>("energyRechargeSpecialValueGlobal").Value;
            d.GoldGainQuantity = t.Field<int>("goldGainQuantity").Value;
            d.ShardsGainQuantity = t.Field<int>("shardsGainQuantity").Value;
            d.ExhaustCounter = t.Field<int>("exhaustCounter").Value;
            d.EffectRequired = t.Field<string>("effectRequired").Value ?? "";

            // ── Discard Options ──────────────────────────────────
            d.DiscardCardType = t.Field<Enums.CardType>("discardCardType").Value;
            d.DiscardCardTypeAux = t.Field<Enums.CardType[]>("discardCardTypeAux").Value ?? Array.Empty<Enums.CardType>();
            d.DiscardCardAutomatic = t.Field<bool>("discardCardAutomatic").Value;
            d.DiscardCardPlace = t.Field<Enums.CardPlace>("discardCardPlace").Value;

            // ── Add Card ─────────────────────────────────────────
            d.AddCard = t.Field<int>("addCard").Value;
            d.AddCardId = t.Field<string>("addCardId").Value ?? "";
            d.AddCardType = t.Field<Enums.CardType>("addCardType").Value;
            d.AddCardTypeAux = t.Field<Enums.CardType[]>("addCardTypeAux").Value ?? Array.Empty<Enums.CardType>();
            d.AddCardChoose = t.Field<int>("addCardChoose").Value;
            d.AddCardFrom = t.Field<Enums.CardFrom>("addCardFrom").Value;
            d.AddCardPlace = t.Field<Enums.CardPlace>("addCardPlace").Value;
            d.AddCardReducedCost = t.Field<int>("addCardReducedCost").Value;
            d.AddCardCostTurn = t.Field<bool>("addCardCostTurn").Value;
            d.AddCardVanish = t.Field<bool>("addCardVanish").Value;
            d.AddCardOnlyCheckAuxTypes = t.Field<bool>("addCardOnlyCheckAuxTypes").Value;
            d.AddCardFromVanishPile = card.AddCardFromVanishPile;
            d.AddVanishToDeck = t.Field<bool>("addVanishToDeck").Value;

            // AddCardList: extract IDs from CardData[]
            var addList = t.Field<CardData[]>("addCardList").Value;
            d.AddCardList = new List<string>();
            if (addList != null)
            {
                foreach (var c in addList)
                {
                    if (c != null)
                    {
                        var cid = Traverse.Create(c).Field<string>("id").Value ?? "";
                        if (!string.IsNullOrEmpty(cid)) d.AddCardList.Add(cid);
                    }
                }
            }

            // ── Look / Scry ─────────────────────────────────────
            d.LookCards = t.Field<int>("lookCards").Value;
            d.LookCardsDiscardUpTo = t.Field<int>("lookCardsDiscardUpTo").Value;
            d.LookCardsVanishUpTo = t.Field<int>("lookCardsVanishUpTo").Value;

            // ── Summon ───────────────────────────────────────────
            var sumNpc = t.Field<NPCData>("summonUnit").Value;
            d.SummonUnitId = sumNpc != null ? sumNpc.Id : "";
            d.SummonNum = t.Field<int>("summonUnitNum").Value;
            d.Evolve = t.Field<bool>("evolve").Value;
            d.Metamorph = t.Field<bool>("metamorph").Value;
            d.SummonAura = GetACId(t.Field<AuraCurseData>("summonAura").Value);
            d.SummonAuraCharges = t.Field<int>("summonAuraCharges").Value;
            d.SummonAura2 = GetACId(t.Field<AuraCurseData>("summonAura2").Value);
            d.SummonAuraCharges2 = t.Field<int>("summonAuraCharges2").Value;
            d.SummonAura3 = GetACId(t.Field<AuraCurseData>("summonAura3").Value);
            d.SummonAuraCharges3 = t.Field<int>("summonAuraCharges3").Value;

            // ── AC Energy Bonus ──────────────────────────────────
            d.AcEnergyBonus = GetACId(t.Field<AuraCurseData>("acEnergyBonus").Value);
            d.AcEnergyBonusQuantity = t.Field<int>("acEnergyBonusQuantity").Value;
            d.AcEnergyBonus2 = GetACId(t.Field<AuraCurseData>("acEnergyBonus2").Value);
            d.AcEnergyBonus2Quantity = t.Field<int>("acEnergyBonus2Quantity").Value;
            d.ChooseOneOfAvailableAuras = t.Field<bool>("chooseOneOfAvailableAuras").Value;

            // ── Special Value System ─────────────────────────────
            d.SpecialValueGlobal = t.Field<Enums.CardSpecialValue>("specialValueGlobal").Value;
            d.SpecialValueModifierGlobal = t.Field<float>("specialValueModifierGlobal").Value;
            d.SpecialAuraCurseNameGlobal = GetACId(t.Field<AuraCurseData>("specialAuraCurseNameGlobal").Value);
            d.SpecialValue1 = t.Field<Enums.CardSpecialValue>("specialValue1").Value;
            d.SpecialValueModifier1 = t.Field<float>("specialValueModifier1").Value;
            d.SpecialAuraCurseName1 = GetACId(t.Field<AuraCurseData>("specialAuraCurseName1").Value);
            d.SpecialValue2 = t.Field<Enums.CardSpecialValue>("specialValue2").Value;
            d.SpecialValueModifier2 = t.Field<float>("specialValueModifier2").Value;
            d.SpecialAuraCurseName2 = GetACId(t.Field<AuraCurseData>("specialAuraCurseName2").Value);

            // ── FX / Effects ─────────────────────────────────────
            d.EffectCaster = t.Field<string>("effectCaster").Value ?? "";
            d.EffectTarget = t.Field<string>("effectTarget").Value ?? "";
            d.EffectPreAction = t.Field<string>("effectPreAction").Value ?? "";
            d.EffectPostCastDelay = t.Field<float>("effectPostCastDelay").Value;
            d.EffectCasterRepeat = t.Field<bool>("effectCasterRepeat").Value;
            d.EffectCastCenter = t.Field<bool>("effectCastCenter").Value;
            d.EffectTrail = t.Field<string>("effectTrail").Value ?? "";
            d.EffectTrailRepeat = t.Field<bool>("effectTrailRepeat").Value;
            d.EffectTrailSpeed = t.Field<float>("effectTrailSpeed").Value;
            d.EffectTrailAngle = t.Field<Enums.EffectTrailAngle>("effectTrailAngle").Value;
            d.EffectPostTargetDelay = t.Field<float>("effectPostTargetDelay").Value;

            // ── Pet System ───────────────────────────────────────
            d.PetActivation = card.PetActivation;
            d.PetBonusDamageType = card.PetBonusDamageType;
            d.PetBonusDamageAmount = card.PetBonusDamageAmount;
            d.IsPetAttack = t.Field<bool>("isPetAttack").Value;
            d.IsPetCast = t.Field<bool>("isPetCast").Value;
            d.KillPet = t.Field<bool>("killPet").Value;
            d.PetTemporal = t.Field<bool>("petTemporal").Value;
            d.PetTemporalAttack = t.Field<bool>("petTemporalAttack").Value;
            d.PetTemporalCast = t.Field<bool>("petTemporalCast").Value;
            d.PetTemporalMoveToCenter = t.Field<bool>("petTemporalMoveToCenter").Value;
            d.PetTemporalMoveToBack = t.Field<bool>("petTemporalMoveToBack").Value;
            d.PetTemporalFadeOutDelay = t.Field<float>("petTemporalFadeOutDelay").Value;

            return d;
        }

        private static void BuildLoot(Dictionary<string, LootDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var loot = DataHelper.MakeLoot(kvp.Value);
                    DataHelper.RegisterLoot(loot);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Loot '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildNpcs(Dictionary<string, NpcDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var npc = DataHelper.MakeFullNpc(kvp.Value);
                    DataHelper.RegisterNPC(npc);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build NPC '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildHeroes(Dictionary<string, HeroDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var sc = DataHelper.MakeFullHero(kvp.Value);
                    DataHelper.RegisterHero(sc);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Hero '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildTraits(Dictionary<string, TraitDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var trait = DataHelper.MakeTrait(kvp.Value);
                    DataHelper.RegisterTrait(trait);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Trait '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildSkins(Dictionary<string, SkinDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var skin = DataHelper.MakeSkin(kvp.Value);
                    DataHelper.RegisterSkin(skin);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Skin '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildPerks(Dictionary<string, PerkDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var perk = DataHelper.MakePerk(kvp.Value);
                    DataHelper.RegisterPerk(perk);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Perk '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildPerkNodes(Dictionary<string, PerkNodeDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var node = DataHelper.MakePerkNode(kvp.Value);
                    DataHelper.RegisterPerkNode(node);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build PerkNode '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildRequirements(Dictionary<string, RequirementDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var req = DataHelper.MakeRequirement(kvp.Value);
                    DataHelper.RegisterRequirement(req);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Requirement '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildCardbacks(Dictionary<string, CardbackDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var cb = DataHelper.MakeCardback(kvp.Value);
                    DataHelper.RegisterCardback(cb);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Cardback '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildTierRewards(Dictionary<string, TierRewardDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var tr = DataHelper.MakeTierReward(kvp.Value);
                    DataHelper.RegisterTierReward(tr);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build TierReward '{kvp.Key}': {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ZONES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a complete new zone from a ZoneDef: creates all SOs and registers in Globals.
        /// Mirrors ZoneLoader.BuildFromDef but uses the mod-project pipeline.
        /// </summary>
        private static void BuildZone(ZoneDef zone)
        {
            try
            {
                // Temp dictionaries to resolve intra-zone references
                var cards = new Dictionary<string, CardData>();
                var npcs = new Dictionary<string, NPCData>();
                var combats = new Dictionary<string, CombatData>();
                var events = new Dictionary<string, EventData>();
                var nodes = new Dictionary<string, NodeData>();
                var items = new Dictionary<string, ItemData>();
                var loot = new Dictionary<string, LootData>();

                // ── 0. Loot ──────────────────────────────────────
                foreach (var d in zone.Loot.Values)
                {
                    var l = DataHelper.MakeLoot(d);
                    loot[d.Id] = l;
                    DataHelper.RegisterLoot(l);
                }

                // ── 1. Cards ─────────────────────────────────────
                foreach (var d in zone.Cards.Values.Where(c => !c.IsUpgraded))
                {
                    var c = MakeFullCard(d);
                    cards[d.Id] = c;
                    DataHelper.RegisterCard(c);
                }
                foreach (var d in zone.Cards.Values.Where(c => c.IsUpgraded))
                {
                    CardData c;
                    if (!string.IsNullOrEmpty(d.BaseCardId) && cards.TryGetValue(d.BaseCardId, out var bc))
                        c = DataHelper.MakeUpgradedCard(bc, d.Id, d.Name,
                            d.UpgDamageMult, d.UpgBonusCurseCharges,
                            d.UpgBonusAuraCharges, d.UpgBonusHeal);
                    else
                        c = MakeFullCard(d);
                    cards[d.Id] = c;
                    DataHelper.RegisterCard(c);
                }

                // ── 2. NPCs ──────────────────────────────────────
                foreach (var d in zone.Npcs.Values)
                {
                    string baseId = ResolveBaseNpcId(zone, d);
                    var npc = DataHelper.MakeFullNpc(d);
                    npcs[d.Id] = npc;
                    DataHelper.RegisterNPC(npc);
                }
                // Link variant chains
                foreach (var d in zone.Npcs.Values)
                {
                    if (!npcs.TryGetValue(d.Id, out var npc)) continue;
                    if (!string.IsNullOrEmpty(d.UpgradedMobId) && npcs.TryGetValue(d.UpgradedMobId, out var u))
                        npc.UpgradedMob = u;
                    if (!string.IsNullOrEmpty(d.NgPlusMobId) && npcs.TryGetValue(d.NgPlusMobId, out var n))
                        npc.NgPlusMob = n;
                    if (!string.IsNullOrEmpty(d.HellModeMobId) && npcs.TryGetValue(d.HellModeMobId, out var h))
                        npc.HellModeMob = h;
                }
                // Wire summon cards → NPCs
                foreach (var d in zone.Cards.Values)
                {
                    if (string.IsNullOrEmpty(d.SummonUnitId)) continue;
                    if (cards.TryGetValue(d.Id, out var card) && npcs.TryGetValue(d.SummonUnitId, out var sNpc))
                        Traverse.Create(card).Field("summonUnit").SetValue(sNpc);
                }

                // ── 3. Items ─────────────────────────────────────
                foreach (var d in zone.Items.Values)
                {
                    var item = DataHelper.MakeFullItem(d);
                    items[d.Id] = item;
                    DataHelper.RegisterItem(item);
                    var ic = DataHelper.MakeItemCard(d, item);
                    cards[d.Id] = ic;
                    DataHelper.RegisterCard(ic);
                }

                // ── 4. Combats ───────────────────────────────────
                foreach (var d in zone.Combats.Values)
                {
                    var npcArr = d.NpcIds
                        .Where(id => npcs.ContainsKey(id) || DataHelper.GetExistingNPC(id) != null)
                        .Select(id => npcs.TryGetValue(id, out var x) ? x : DataHelper.GetExistingNPC(id))
                        .Where(x => x != null)
                        .ToArray();
                    var combat = DataHelper.MakeCombat(d, npcArr);
                    combats[d.CombatId] = combat;
                    DataHelper.RegisterCombat(combat);
                }

                // ── 5. Zone + Nodes ──────────────────────────────
                var zoneData = ScriptableObject.CreateInstance<ZoneData>();
                zoneData.ZoneId = zone.ZoneId;
                zoneData.ZoneName = zone.ZoneName;
                zoneData.ObeliskLow = zone.ObeliskLow;
                zoneData.ObeliskHigh = zone.ObeliskHigh;
                zoneData.ObeliskFinal = zone.ObeliskFinal;
                zoneData.DisableExperienceOnThisZone = zone.DisableExperience;
                zoneData.DisableMadnessOnThisZone = zone.DisableMadness;
                zoneData.Sku = "";
                DataHelper.RegisterZone(zoneData);

                foreach (var d in zone.Nodes.Values)
                {
                    var nd = BuildNodeSO(d, combats, events);
                    nd.NodeZone = zoneData;
                    nodes[d.NodeId] = nd;
                    DataHelper.RegisterNode(nd);
                }
                // Resolve node connections
                foreach (var d in zone.Nodes.Values)
                {
                    if (!nodes.TryGetValue(d.NodeId, out var nd)) continue;
                    nd.NodesConnected = d.Connections
                        .Where(id => nodes.ContainsKey(id))
                        .Select(id => nodes[id])
                        .ToArray();

                    if (d.ConnectionRequirements != null && d.ConnectionRequirements.Count > 0)
                    {
                        var reqs = new List<NodesConnectedRequirement>();
                        foreach (var cr in d.ConnectionRequirements)
                        {
                            var ncr = new NodesConnectedRequirement();
                            if (!string.IsNullOrEmpty(cr.TargetNodeId) && nodes.TryGetValue(cr.TargetNodeId, out var tn))
                                ncr.NodeData = tn;
                            if (!string.IsNullOrEmpty(cr.RequirementId))
                                ncr.ConectionRequeriment = DataHelper.GetEventRequirement(cr.RequirementId);
                            if (!string.IsNullOrEmpty(cr.IfNotNodeId) && nodes.TryGetValue(cr.IfNotNodeId, out var inn))
                                ncr.ConectionIfNotNode = inn;
                            reqs.Add(ncr);
                        }
                        nd.NodesConnectedRequirement = reqs.ToArray();
                    }
                }

                // ── 6. Events (two-pass for inter-event refs) ────
                foreach (var d in zone.Events.Values)
                {
                    var replies = d.Replies.Select(r => BuildReply(r, combats, events, nodes, loot)).ToArray();
                    var evt = DataHelper.MakeEvent(d.EventId, d.EventName,
                        d.Description, d.DescriptionAction, replies,
                        d.EventTier, d.ReplyRandom);
                    if (!string.IsNullOrEmpty(d.RequirementId))
                    {
                        var req = DataHelper.GetEventRequirement(d.RequirementId);
                        if (req != null) Traverse.Create(evt).Field("requirement").SetValue(req);
                    }
                    events[d.EventId] = evt;
                    DataHelper.RegisterEvent(evt);
                }
                // Second pass: wire inter-event refs
                foreach (var d in zone.Events.Values)
                {
                    if (!events.TryGetValue(d.EventId, out var evt)) continue;
                    var replies = evt.Replys;
                    for (int i = 0; i < d.Replies.Count && i < replies.Length; i++)
                    {
                        var r = d.Replies[i];
                        var reply = replies[i];
                        if (reply.SsEvent == null && !string.IsNullOrEmpty(r.Ss.EventId) && events.TryGetValue(r.Ss.EventId, out var se))
                            reply.SsEvent = se;
                        if (reply.FlEvent == null && !string.IsNullOrEmpty(r.Fl.EventId) && events.TryGetValue(r.Fl.EventId, out var fe))
                            reply.FlEvent = fe;
                        if (reply.SscEvent == null && !string.IsNullOrEmpty(r.Ssc.EventId) && events.TryGetValue(r.Ssc.EventId, out var sse))
                            reply.SscEvent = sse;
                        if (reply.FlcEvent == null && !string.IsNullOrEmpty(r.Flc.EventId) && events.TryGetValue(r.Flc.EventId, out var fle))
                            reply.FlcEvent = fle;
                    }
                }
                // Wire combat→event post-combat
                foreach (var d in zone.Combats.Values)
                {
                    if (string.IsNullOrEmpty(d.EventDataId)) continue;
                    if (combats.TryGetValue(d.CombatId, out var combat) && events.TryGetValue(d.EventDataId, out var pe))
                        combat.EventData = pe;
                }
                // Wire node→event
                foreach (var d in zone.Nodes.Values)
                {
                    if (string.IsNullOrEmpty(d.EventId)) continue;
                    if (nodes.TryGetValue(d.NodeId, out var nd) && events.TryGetValue(d.EventId, out var ne))
                    {
                        nd.NodeEvent = new[] { ne };
                        nd.NodeEventPriority = new[] { 0 };
                        nd.NodeEventPercent = new[] { 100 };
                        nd.NodeEventTier = d.NodeEventTier;
                    }
                }

                Plugin.Log.LogInfo($"[Builder] Zone '{zone.ZoneId}' built: " +
                    $"{nodes.Count} nodes, {combats.Count} combats, {events.Count} events, " +
                    $"{npcs.Count} NPCs, {cards.Count} cards");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Builder] BuildZone '{zone.ZoneId}' failed: {ex.Message}");
                Plugin.Log.LogError(ex.StackTrace);
            }
        }

        /// <summary>
        /// Apply a zone patch: create new SOs for patch entities and inject them
        /// into the existing base-game zone registered in Globals.
        /// </summary>
        private static void ApplyZonePatch(ZonePatchDef patch)
        {
            try
            {
                // Get existing zone from Globals
                var zoneDict = Traverse.Create(Globals.Instance)
                    .Field<Dictionary<string, ZoneData>>("_ZoneDataSource").Value;
                ZoneData zoneData = null;
                if (zoneDict != null)
                    zoneDict.TryGetValue(patch.TargetZoneId.ToLower(), out zoneData);

                if (zoneData == null)
                {
                    Plugin.Log.LogWarning($"[Builder] Zone patch target '{patch.TargetZoneId}' not found in Globals.");
                    return;
                }

                var newNodes = new Dictionary<string, NodeData>();
                var newCombats = new Dictionary<string, CombatData>();
                var newEvents = new Dictionary<string, EventData>();

                // ── Build encounters ─────────────────────────────
                foreach (var d in patch.Encounters.Values)
                {
                    var npcArr = d.NpcIds
                        .Select(id => DataHelper.GetExistingNPC(id))
                        .Where(x => x != null)
                        .ToArray();
                    var combat = DataHelper.MakeCombat(d, npcArr);
                    newCombats[d.CombatId] = combat;
                    DataHelper.RegisterCombat(combat);
                }

                // ── Build events (first pass) ────────────────────
                foreach (var d in patch.Events.Values)
                {
                    var replies = d.Replies.Select(r =>
                        DataHelper.MakeReply(r,
                            getCombat: id => newCombats.TryGetValue(id, out var c) ? c : null,
                            getEvent: id => newEvents.TryGetValue(id, out var e) ? e : DataHelper.GetExistingEvent(id),
                            getNode: id => newNodes.TryGetValue(id, out var n) ? n : DataHelper.GetExistingNode(id),
                            getLoot: id => DataHelper.GetLootData(id)
                        )).ToArray();
                    var evt = DataHelper.MakeEvent(d.EventId, d.EventName,
                        d.Description, d.DescriptionAction, replies,
                        d.EventTier, d.ReplyRandom);
                    if (!string.IsNullOrEmpty(d.RequirementId))
                    {
                        var req = DataHelper.GetEventRequirement(d.RequirementId);
                        if (req != null) Traverse.Create(evt).Field("requirement").SetValue(req);
                    }
                    newEvents[d.EventId] = evt;
                    DataHelper.RegisterEvent(evt);
                }

                // ── Build nodes ──────────────────────────────────
                foreach (var d in patch.Nodes.Values)
                {
                    var nd = BuildNodeSO(d, newCombats, newEvents);
                    nd.NodeZone = zoneData;
                    newNodes[d.NodeId] = nd;
                    DataHelper.RegisterNode(nd);
                }
                // Resolve node connections (can reference both new and existing nodes)
                foreach (var d in patch.Nodes.Values)
                {
                    if (!newNodes.TryGetValue(d.NodeId, out var nd)) continue;
                    nd.NodesConnected = d.Connections
                        .Select(id =>
                        {
                            if (newNodes.TryGetValue(id, out var n)) return n;
                            return DataHelper.GetExistingNode(id);
                        })
                        .Where(x => x != null)
                        .ToArray();
                }
                // Wire node→event
                foreach (var d in patch.Nodes.Values)
                {
                    if (string.IsNullOrEmpty(d.EventId)) continue;
                    if (!newNodes.TryGetValue(d.NodeId, out var nd)) continue;
                    EventData evt = null;
                    if (!newEvents.TryGetValue(d.EventId, out evt))
                        evt = DataHelper.GetExistingEvent(d.EventId);
                    if (evt != null)
                    {
                        nd.NodeEvent = new[] { evt };
                        nd.NodeEventPriority = new[] { 0 };
                        nd.NodeEventPercent = new[] { 100 };
                        nd.NodeEventTier = d.NodeEventTier;
                    }
                }

                // ── Wire inter-event refs (second pass) ──────────
                foreach (var d in patch.Events.Values)
                {
                    if (!newEvents.TryGetValue(d.EventId, out var evt)) continue;
                    var replies = evt.Replys;
                    for (int i = 0; i < d.Replies.Count && i < replies.Length; i++)
                    {
                        var r = d.Replies[i];
                        var reply = replies[i];
                        if (reply.SsEvent == null && !string.IsNullOrEmpty(r.Ss.EventId))
                        {
                            if (newEvents.TryGetValue(r.Ss.EventId, out var se)) reply.SsEvent = se;
                            else { var sse = DataHelper.GetExistingEvent(r.Ss.EventId); if (sse != null) reply.SsEvent = sse; }
                        }
                        if (reply.FlEvent == null && !string.IsNullOrEmpty(r.Fl.EventId))
                        {
                            if (newEvents.TryGetValue(r.Fl.EventId, out var fe)) reply.FlEvent = fe;
                            else { var ffe = DataHelper.GetExistingEvent(r.Fl.EventId); if (ffe != null) reply.FlEvent = ffe; }
                        }
                        if (reply.SscEvent == null && !string.IsNullOrEmpty(r.Ssc.EventId))
                        {
                            if (newEvents.TryGetValue(r.Ssc.EventId, out var sce)) reply.SscEvent = sce;
                            else { var sce2 = DataHelper.GetExistingEvent(r.Ssc.EventId); if (sce2 != null) reply.SscEvent = sce2; }
                        }
                        if (reply.FlcEvent == null && !string.IsNullOrEmpty(r.Flc.EventId))
                        {
                            if (newEvents.TryGetValue(r.Flc.EventId, out var fce)) reply.FlcEvent = fce;
                            else { var fce2 = DataHelper.GetExistingEvent(r.Flc.EventId); if (fce2 != null) reply.FlcEvent = fce2; }
                        }
                    }
                }

                // Wire combat→event post-combat
                foreach (var d in patch.Encounters.Values)
                {
                    if (string.IsNullOrEmpty(d.EventDataId)) continue;
                    if (newCombats.TryGetValue(d.CombatId, out var combat))
                    {
                        EventData pe = null;
                        if (!newEvents.TryGetValue(d.EventDataId, out pe))
                            pe = DataHelper.GetExistingEvent(d.EventDataId);
                        if (pe != null) combat.EventData = pe;
                    }
                }

                Plugin.Log.LogInfo($"[Builder] Zone patch '{patch.TargetZoneId}' applied: " +
                    $"{newNodes.Count} nodes, {newCombats.Count} encounters, {newEvents.Count} events, " +
                    $"{patch.Roads.Count} roads");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Builder] ApplyZonePatch '{patch.TargetZoneId}' failed: {ex.Message}");
                Plugin.Log.LogError(ex.StackTrace);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ZONE HELPER METHODS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Build a NodeData SO from a NodeDef, wiring combat/event refs from provided dicts.</summary>
        private static NodeData BuildNodeSO(NodeDef d,
            Dictionary<string, CombatData> combats,
            Dictionary<string, EventData> events)
        {
            var node = ScriptableObject.CreateInstance<NodeData>();
            node.NodeId = d.NodeId;
            node.NodeName = d.NodeName;
            node.Description = d.Description ?? "";
            node.TravelDestination = d.TravelDestination;
            node.GoToTown = d.GoToTown;
            node.ExistsPercent = d.ExistsPercent;
            node.NodeGround = d.NodeGround;
            node.DisableCorruption = d.DisableCorruption;
            node.DisableRandom = d.DisableRandom;
            node.VisibleIfNotRequirement = d.VisibleIfNotRequirement;
            node.ExistsSku = "";
            node.SourceNodeName = "";
            node.NodesConnected = new NodeData[0];
            node.NodesConnectedRequirement = new NodesConnectedRequirement[0];

            if (!string.IsNullOrEmpty(d.NodeRequirementId))
                node.NodeRequirement = DataHelper.GetEventRequirement(d.NodeRequirementId);

            // Combat
            CombatData combat = null;
            if (!string.IsNullOrEmpty(d.CombatId))
            {
                combats?.TryGetValue(d.CombatId, out combat);
                if (combat == null)
                {
                    // Fallback: existing base-game combat
                    var combatDict = Traverse.Create(Globals.Instance)
                        .Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
                    combatDict?.TryGetValue(d.CombatId.Replace(" ", "").ToLower(), out combat);
                }
            }
            if (combat != null)
            {
                node.NodeCombat = new[] { combat };
                node.NodeCombatTier = d.CombatTier;
            }
            else
            {
                node.NodeCombat = new CombatData[0];
            }

            // Event (may be null on first pass; wired in second pass)
            EventData evt = null;
            if (!string.IsNullOrEmpty(d.EventId))
            {
                events?.TryGetValue(d.EventId, out evt);
                if (evt == null) evt = DataHelper.GetExistingEvent(d.EventId);
            }
            if (evt != null)
            {
                node.NodeEvent = new[] { evt };
                node.NodeEventPriority = new[] { 0 };
                node.NodeEventPercent = new[] { 100 };
                node.NodeEventTier = d.NodeEventTier;
            }
            else
            {
                node.NodeEvent = new EventData[0];
                node.NodeEventPriority = new int[0];
                node.NodeEventPercent = new int[0];
            }

            // Combat/event percentages
            bool hasCombat = node.NodeCombat.Length > 0;
            bool hasEvent = node.NodeEvent.Length > 0;
            if (d.CombatPercent >= 0)
            {
                node.CombatPercent = d.CombatPercent;
                node.EventPercent = d.EventPercent >= 0 ? d.EventPercent : 100 - d.CombatPercent;
            }
            else if (hasCombat && hasEvent)
            {
                node.CombatPercent = 50;
                node.EventPercent = 50;
            }
            else if (hasCombat) { node.CombatPercent = 100; node.EventPercent = 0; }
            else if (hasEvent) { node.CombatPercent = 0; node.EventPercent = 100; }

            return node;
        }

        /// <summary>Build an EventReplyData from a ReplyDef, resolving refs from local dicts.</summary>
        private static EventReplyData BuildReply(ReplyDef r,
            Dictionary<string, CombatData> combats,
            Dictionary<string, EventData> events,
            Dictionary<string, NodeData> nodes,
            Dictionary<string, LootData> loot)
        {
            return DataHelper.MakeReply(r,
                getCombat: id =>
                {
                    if (combats != null && combats.TryGetValue(id, out var c)) return c;
                    var cd = Traverse.Create(Globals.Instance)
                        .Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
                    CombatData existing = null;
                    cd?.TryGetValue(id.Replace(" ", "").ToLower(), out existing);
                    return existing;
                },
                getEvent: id =>
                {
                    if (events != null && events.TryGetValue(id, out var e)) return e;
                    return DataHelper.GetExistingEvent(id);
                },
                getNode: id =>
                {
                    if (nodes != null && nodes.TryGetValue(id, out var n)) return n;
                    return DataHelper.GetExistingNode(id);
                },
                getLoot: id =>
                {
                    if (loot != null && loot.TryGetValue(id, out var l)) return l;
                    return DataHelper.GetLootData(id);
                });
        }

        /// <summary>Resolve base NPC ID from a zone's sprite system.</summary>
        private static string ResolveBaseNpcId(ZoneDef zone, NpcDef npcDef)
        {
            if (zone != null && !string.IsNullOrEmpty(npcDef.SpriteSource) &&
                zone.Sprites.TryGetValue(npcDef.SpriteSource, out var spriteDef) &&
                !string.IsNullOrEmpty(spriteDef.BaseSprite))
                return spriteDef.BaseSprite;
            return npcDef.SpriteSource;
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Extract the ID string from an AuraCurseData reference (null-safe).</summary>
        private static string GetACId(AuraCurseData ac)
        {
            if (ac == null) return "";
            return Traverse.Create(ac).Field<string>("id").Value ?? "";
        }
    }
}
