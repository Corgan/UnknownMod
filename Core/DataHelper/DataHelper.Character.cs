using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Character Builders (Trait, Skin, Perk, PerkNode)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        // ── Trait Factories ──────────────────────────────────────────────

        /// <summary>Create a complete TraitData SO from a TraitDef.</summary>
        public static TraitData MakeTrait(TraitDef d)
        {
            var trait = ScriptableObject.CreateInstance<TraitData>();

            // Identity
            trait.TraitName = d.TraitName;
            trait.Id = d.Id;
            trait.Description = d.Description ?? "";

            // Activation
            trait.Activation = d.Activation;
            trait.ActivateOnRuneTypeAdded = d.ActivateOnRuneTypeAdded;
            trait.TryActivateOnEveryEvent = d.TryActivateOnEveryEvent;
            trait.TimesPerTurn = d.TimesPerTurn;
            trait.TimesPerRound = d.TimesPerRound;

            // Cards
            if (!string.IsNullOrEmpty(d.TraitCard))
                trait.TraitCard = GetCard(d.TraitCard);
            if (!string.IsNullOrEmpty(d.TraitCardForAllHeroes))
                trait.TraitCardForAllHeroes = GetCard(d.TraitCardForAllHeroes);

            // Character Stat
            trait.CharacterStatModified = d.CharacterStatModified;
            trait.CharacterStatModifiedValue = d.CharacterStatModifiedValue;

            // Resist Modification
            trait.ResistModified1 = d.ResistModified1;
            trait.ResistModifiedValue1 = d.ResistModifiedValue1;
            trait.ResistModified2 = d.ResistModified2;
            trait.ResistModifiedValue2 = d.ResistModifiedValue2;
            trait.ResistModified3 = d.ResistModified3;
            trait.ResistModifiedValue3 = d.ResistModifiedValue3;

            // AC Immunity
            trait.AuracurseImmune1 = d.AuracurseImmune1 ?? "";
            trait.AuracurseImmune2 = d.AuracurseImmune2 ?? "";
            trait.AuracurseImmune3 = d.AuracurseImmune3 ?? "";

            // AC Bonus
            if (!string.IsNullOrEmpty(d.AuracurseBonus1))
                trait.AuracurseBonus1 = GetAuraCurse(d.AuracurseBonus1);
            trait.AuracurseBonusValue1 = d.AuracurseBonusValue1;
            if (!string.IsNullOrEmpty(d.AuracurseBonus2))
                trait.AuracurseBonus2 = GetAuraCurse(d.AuracurseBonus2);
            trait.AuracurseBonusValue2 = d.AuracurseBonusValue2;
            if (!string.IsNullOrEmpty(d.AuracurseBonus3))
                trait.AuracurseBonus3 = GetAuraCurse(d.AuracurseBonus3);
            trait.AuracurseBonusValue3 = d.AuracurseBonusValue3;

            // Heal Bonuses
            trait.HealFlatBonus = d.HealFlatBonus;
            trait.HealPercentBonus = d.HealPercentBonus;
            trait.HealReceivedFlatBonus = d.HealReceivedFlatBonus;
            trait.HealReceivedPercentBonus = d.HealReceivedPercentBonus;

            // Damage Flat Bonus
            trait.DamageBonusFlat = d.DamageBonusFlat;
            trait.DamageBonusFlatValue = d.DamageBonusFlatValue;
            trait.DamageBonusFlat2 = d.DamageBonusFlat2;
            trait.DamageBonusFlatValue2 = d.DamageBonusFlatValue2;
            trait.DamageBonusFlat3 = d.DamageBonusFlat3;
            trait.DamageBonusFlatValue3 = d.DamageBonusFlatValue3;

            // Damage Percent Bonus
            trait.DamageBonusPercent = d.DamageBonusPercent;
            trait.DamageBonusPercentValue = d.DamageBonusPercentValue;
            trait.DamageBonusPercent2 = d.DamageBonusPercent2;
            trait.DamageBonusPercentValue2 = d.DamageBonusPercentValue2;
            trait.DamageBonusPercent3 = d.DamageBonusPercent3;
            trait.DamageBonusPercentValue3 = d.DamageBonusPercentValue3;

            // Misc
            trait.MaxBleedDamagePerTurn = d.MaxBleedDamagePerTurn;

            return trait;
        }

        /// <summary>Snapshot a TraitData SO back into a TraitDef for override editing.</summary>
        public static TraitDef SnapshotTrait(TraitData trait)
        {
            var d = new TraitDef();
            d.Id = trait.Id ?? "";
            d.TraitName = trait.TraitName ?? "";
            d.Description = trait.Description ?? "";

            // Activation
            d.Activation = trait.Activation;
            d.ActivateOnRuneTypeAdded = trait.ActivateOnRuneTypeAdded;
            d.TryActivateOnEveryEvent = trait.TryActivateOnEveryEvent;
            d.TimesPerTurn = trait.TimesPerTurn;
            d.TimesPerRound = trait.TimesPerRound;

            // Cards
            d.TraitCard = trait.TraitCard != null ? trait.TraitCard.Id ?? "" : "";
            d.TraitCardForAllHeroes = trait.TraitCardForAllHeroes != null ? trait.TraitCardForAllHeroes.Id ?? "" : "";

            // Character Stat
            d.CharacterStatModified = trait.CharacterStatModified;
            d.CharacterStatModifiedValue = trait.CharacterStatModifiedValue;

            // Resist Modification
            d.ResistModified1 = trait.ResistModified1;
            d.ResistModifiedValue1 = trait.ResistModifiedValue1;
            d.ResistModified2 = trait.ResistModified2;
            d.ResistModifiedValue2 = trait.ResistModifiedValue2;
            d.ResistModified3 = trait.ResistModified3;
            d.ResistModifiedValue3 = trait.ResistModifiedValue3;

            // AC Immunity
            d.AuracurseImmune1 = trait.AuracurseImmune1 ?? "";
            d.AuracurseImmune2 = trait.AuracurseImmune2 ?? "";
            d.AuracurseImmune3 = trait.AuracurseImmune3 ?? "";

            // AC Bonus
            d.AuracurseBonus1 = GetACId(trait.AuracurseBonus1);
            d.AuracurseBonusValue1 = trait.AuracurseBonusValue1;
            d.AuracurseBonus2 = GetACId(trait.AuracurseBonus2);
            d.AuracurseBonusValue2 = trait.AuracurseBonusValue2;
            d.AuracurseBonus3 = GetACId(trait.AuracurseBonus3);
            d.AuracurseBonusValue3 = trait.AuracurseBonusValue3;

            // Heal Bonuses
            d.HealFlatBonus = trait.HealFlatBonus;
            d.HealPercentBonus = trait.HealPercentBonus;
            d.HealReceivedFlatBonus = trait.HealReceivedFlatBonus;
            d.HealReceivedPercentBonus = trait.HealReceivedPercentBonus;

            // Damage Flat Bonus
            d.DamageBonusFlat = trait.DamageBonusFlat;
            d.DamageBonusFlatValue = trait.DamageBonusFlatValue;
            d.DamageBonusFlat2 = trait.DamageBonusFlat2;
            d.DamageBonusFlatValue2 = trait.DamageBonusFlatValue2;
            d.DamageBonusFlat3 = trait.DamageBonusFlat3;
            d.DamageBonusFlatValue3 = trait.DamageBonusFlatValue3;

            // Damage Percent Bonus
            d.DamageBonusPercent = trait.DamageBonusPercent;
            d.DamageBonusPercentValue = trait.DamageBonusPercentValue;
            d.DamageBonusPercent2 = trait.DamageBonusPercent2;
            d.DamageBonusPercentValue2 = trait.DamageBonusPercentValue2;
            d.DamageBonusPercent3 = trait.DamageBonusPercent3;
            d.DamageBonusPercentValue3 = trait.DamageBonusPercentValue3;

            // Misc
            d.MaxBleedDamagePerTurn = trait.MaxBleedDamagePerTurn;

            return d;
        }

        // ── Skin Factories ───────────────────────────────────────────────

        /// <summary>Create a complete SkinData SO from a SkinDef.</summary>
        public static SkinData MakeSkin(SkinDef d)
        {
            var skin = ScriptableObject.CreateInstance<SkinData>();

            skin.SkinId = d.Id;
            skin.SkinName = d.SkinName ?? "";
            skin.BaseSkin = d.BaseSkin;
            skin.SkinOrder = d.SkinOrder;
            skin.PerkLevel = d.PerkLevel;
            skin.Sku = d.Sku ?? "";
            skin.SteamStat = d.SteamStat ?? "";
            skin.SkinTextId = d.SkinTextId ?? "";
            skin.HeroSelectionScreenScale = d.HeroSelectionScreenScale;
            skin.HeroSelectionScreenOffset_X = d.HeroSelectionScreenOffsetX;

            // Wire SubClass reference
            if (!string.IsNullOrEmpty(d.SkinSubclass))
                skin.SkinSubclass = GetSubClass(d.SkinSubclass);

            // Copy visuals from source skin
            if (!string.IsNullOrEmpty(d.SpriteSource))
                CopySkinVisuals(skin, d.SpriteSource);

            return skin;
        }

        /// <summary>Snapshot a SkinData SO back into a SkinDef for override editing.</summary>
        public static SkinDef SnapshotSkin(SkinData skin)
        {
            var d = new SkinDef();
            d.Id = skin.SkinId ?? "";
            d.SkinName = skin.SkinName ?? "";
            d.SkinSubclass = skin.SkinSubclass != null
                ? NormalizeKey(skin.SkinSubclass.SubClassName)
                : "";
            d.BaseSkin = skin.BaseSkin;
            d.SkinOrder = skin.SkinOrder;
            d.PerkLevel = skin.PerkLevel;
            d.Sku = skin.Sku ?? "";
            d.SteamStat = skin.SteamStat ?? "";
            d.SkinTextId = skin.SkinTextId ?? "";
            d.HeroSelectionScreenScale = skin.HeroSelectionScreenScale;
            d.HeroSelectionScreenOffsetX = skin.HeroSelectionScreenOffset_X;

            // Use self as sprite source for override snapshots
            d.SpriteSource = d.Id;

            return d;
        }

        // ── Perk Factories ───────────────────────────────────────────────

        /// <summary>Create a PerkData SO from a PerkDef.</summary>
        public static PerkData MakePerk(PerkDef d)
        {
            var perk = ScriptableObject.CreateInstance<PerkData>();

            perk.Id = d.Id;
            perk.CustomDescription = d.CustomDescription ?? "";
            perk.CardClass = d.CardClass;
            perk.MainPerk = d.MainPerk;
            perk.ObeliskPerk = d.ObeliskPerk;
            perk.Level = d.Level;
            perk.Row = d.Row;
            perk.IconTextValue = d.IconTextValue ?? "";
            perk.AdditionalCurrency = d.AdditionalCurrency;
            perk.AdditionalShards = d.AdditionalShards;
            perk.MaxHealth = d.MaxHealth;
            perk.EnergyBegin = d.EnergyBegin;
            perk.SpeedQuantity = d.SpeedQuantity;
            perk.HealQuantity = d.HealQuantity;
            perk.DamageFlatBonus = d.DamageFlatBonus;
            perk.DamageFlatBonusValue = d.DamageFlatBonusValue;

            if (!string.IsNullOrEmpty(d.AuracurseBonus))
                perk.AuracurseBonus = GetAuraCurse(d.AuracurseBonus);
            perk.AuracurseBonusValue = d.AuracurseBonusValue;

            perk.ResistModified = d.ResistModified;
            perk.ResistModifiedValue = d.ResistModifiedValue;

            perk.Init(); // lowercases id

            return perk;
        }

        /// <summary>Snapshot a PerkData SO back into a PerkDef for override editing.</summary>
        public static PerkDef SnapshotPerk(PerkData perk)
        {
            var d = new PerkDef();
            d.Id = perk.Id ?? "";
            d.CustomDescription = perk.CustomDescription ?? "";
            d.CardClass = perk.CardClass;
            d.MainPerk = perk.MainPerk;
            d.ObeliskPerk = perk.ObeliskPerk;
            d.Level = perk.Level;
            d.Row = perk.Row;
            d.IconTextValue = perk.IconTextValue ?? "";
            d.AdditionalCurrency = perk.AdditionalCurrency;
            d.AdditionalShards = perk.AdditionalShards;
            d.MaxHealth = perk.MaxHealth;
            d.EnergyBegin = perk.EnergyBegin;
            d.SpeedQuantity = perk.SpeedQuantity;
            d.HealQuantity = perk.HealQuantity;
            d.DamageFlatBonus = perk.DamageFlatBonus;
            d.DamageFlatBonusValue = perk.DamageFlatBonusValue;
            d.AuracurseBonus = GetACId(perk.AuracurseBonus);
            d.AuracurseBonusValue = perk.AuracurseBonusValue;
            d.ResistModified = perk.ResistModified;
            d.ResistModifiedValue = perk.ResistModifiedValue;
            return d;
        }

        // ── PerkNode Factories ───────────────────────────────────────────

        /// <summary>Create a PerkNodeData SO from a PerkNodeDef.</summary>
        public static PerkNodeData MakePerkNode(PerkNodeDef d)
        {
            var node = ScriptableObject.CreateInstance<PerkNodeData>();

            node.Id = d.Id;
            node.Type = d.Type;
            node.Column = d.Column;
            node.Row = d.Row;
            node.LockedInTown = d.LockedInTown;
            node.NotStack = d.NotStack;
            node.Cost = d.Cost;

            // Resolve PerkData reference
            if (!string.IsNullOrEmpty(d.Perk))
                node.Perk = GetPerk(d.Perk);

            // Resolve PerkNodeData prerequisite (may be null if not yet built)
            if (!string.IsNullOrEmpty(d.PerkRequired))
                node.PerkRequired = GetPerkNode(d.PerkRequired);

            // Resolve connected perk nodes
            if (d.PerksConnected != null && d.PerksConnected.Count > 0)
            {
                var connected = new List<PerkNodeData>();
                foreach (var cid in d.PerksConnected)
                {
                    var cn = GetPerkNode(cid);
                    if (cn != null) connected.Add(cn);
                }
                node.PerksConnected = connected.ToArray();
            }
            else
            {
                node.PerksConnected = new PerkNodeData[0];
            }

            return node;
        }

        /// <summary>Snapshot a PerkNodeData SO back into a PerkNodeDef for override editing.</summary>
        public static PerkNodeDef SnapshotPerkNode(PerkNodeData node)
        {
            var d = new PerkNodeDef();
            d.Id = node.Id ?? "";
            d.Type = node.Type;
            d.Column = node.Column;
            d.Row = node.Row;
            d.LockedInTown = node.LockedInTown;
            d.NotStack = node.NotStack;
            d.Cost = node.Cost;

            d.Perk = node.Perk != null ? node.Perk.Id ?? "" : "";
            d.PerkRequired = node.PerkRequired != null ? node.PerkRequired.Id ?? "" : "";

            d.PerksConnected = new List<string>();
            if (node.PerksConnected != null)
            {
                foreach (var cn in node.PerksConnected)
                {
                    if (cn != null && !string.IsNullOrEmpty(cn.Id))
                        d.PerksConnected.Add(cn.Id);
                }
            }

            return d;
        }
    }
}
