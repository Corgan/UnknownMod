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

        /// <summary>Create an AuraCurseData SO from an AuraCurseDef DTO.</summary>
        public static AuraCurseData MakeAuraCurse(AuraCurseDef d)
        {
            var ac = ScriptableObject.CreateInstance<AuraCurseData>();

            // ── Auto-mapped fields (single source of truth: FieldMappings.AuraCurse) ──
            FieldMapper.Apply(FieldMappings.AuraCurse, d, ac);

            // ── Edge cases: array ────────────────────────────────
            ac.DisabledCardTypes = d.DisabledCardTypes ?? Array.Empty<Enums.CardType>();

            // ── Edge cases: ACBonusData list ─────────────────────
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

            // ── Edge cases: AuraDamageConditionalBonuses array ───
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

            // ── Auto-mapped fields (single source of truth: FieldMappings.AuraCurse) ──
            FieldMapper.Snapshot(FieldMappings.AuraCurse, ac, d);

            // ── Edge cases: asset names (Snapshot only) ──────────
            d.Sprite = ac.Sprite != null ? ac.Sprite.name : "";
            d.Sound = ac.Sound != null ? ac.Sound.name : "";
            d.SoundRework = ac.SoundRework != null ? ac.SoundRework.name : "";

            // ── Edge cases: array ────────────────────────────────
            d.DisabledCardTypes = ac.DisabledCardTypes ?? Array.Empty<Enums.CardType>();

            // ── Edge cases: ACBonusData list ─────────────────────
            if (ac.ACBonusData != null && ac.ACBonusData.Count > 0)
            {
                foreach (var b in ac.ACBonusData)
                {
                    d.ACBonusData.Add(new AuraCurseChargesBonusDef
                    {
                        AuraCurseId = b.acData != null ? DataHelper.GetACId(b.acData) : "",
                        ChargesBonus = b.bonusCharges,
                        RequiredChargesForBonus = b.requiredChargesForBonus,
                        BonusType = b.bonusType
                    });
                }
            }

            // ── Edge cases: AuraDamageConditionalBonuses array ───
            if (ac.AuraDamageConditionalBonuses != null && ac.AuraDamageConditionalBonuses.Length > 0)
            {
                foreach (var b in ac.AuraDamageConditionalBonuses)
                {
                    d.AuraDamageConditionalBonuses.Add(new AuraDamageBonusDef
                    {
                        DamageType = b.AuraDamageType,
                        BasedOnACId = b.AuraDamageBasedOnAC != null ? DataHelper.GetACId(b.AuraDamageBasedOnAC) : "",
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

        internal static void RegisterAuraCurse(AuraCurseData ac)
        {
            var t = Traverse.Create(ac);
            string id = DataHelper.NormalizeKey(t.Field<string>("id").Value);
            if (string.IsNullOrEmpty(id)) return;

            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, AuraCurseData>>("_AurasCursesSource").Value;
            if (dict != null) dict[id] = ac;
        }
    }
}
