using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Item Builders (MakeFullItem, SnapshotItem)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        /// <summary>
        /// Resolves CardToGain / CardToGainList from the registry. The game reads CardToGainList.Count
        /// whenever CardToGain is set, so the list must never be left null.
        /// </summary>
        internal static void WireItemCardRefs(ItemDef d, ItemData item)
        {
            var cardsToGain = new List<CardDataNew>();
            foreach (var cid in d.CardToGainList ?? new List<string>())
            {
                var card = GetCardSource(cid);
                if (card != null) cardsToGain.Add(card);
            }
            item.CardToGainList = cardsToGain;
            if (!string.IsNullOrEmpty(d.CardToGain))
            {
                var single = GetCardSource(d.CardToGain);
                if (single != null) item.CardToGain = single;
            }
        }

        /// <summary>Create a complete ItemData SO from an ItemDef, setting ALL fields.</summary>
        public static ItemData MakeFullItem(ItemDef d)
        {
            var item = ScriptableObject.CreateInstance<ItemData>();

            // ── Auto-mapped fields (single source of truth: FieldMappings.Item) ──
            FieldMapper.Apply(FieldMappings.Item, d, item);

            // ── Edge cases: SpecialValue structs ─────────────────
            item.HealQuantitySpecialValue = MakeSV(d.HealQuantitySpecialValue);
            item.AuracurseGain1SpecialValue = MakeSV(d.AuracurseGain1SpecialValue);
            item.AuracurseGain2SpecialValue = MakeSV(d.AuracurseGain2SpecialValue);
            item.AuracurseGain3SpecialValue = MakeSV(d.AuracurseGain3SpecialValue);
            item.DttSpecialValues1 = MakeSV(d.DttSpecialValues1);
            item.DttSpecialValues2 = MakeSV(d.DttSpecialValues2);

            // ── Edge cases: EchoAdaptationValues struct array ───
            if (d.EchoAdaptationValues != null && d.EchoAdaptationValues.Count > 0)
            {
                var echo = new EchoAdaptationValues[d.EchoAdaptationValues.Count];
                for (int i = 0; i < echo.Length; i++)
                {
                    var e = d.EchoAdaptationValues[i];
                    echo[i] = new EchoAdaptationValues
                    {
                        ResistanceFromPlayerDamagePercentBonuses = e?.ResistanceFromPlayerDamagePercentBonuses ?? new int[0],
                        CursesPurgeFromPlayerCastsPercent = e?.CursesPurgeFromPlayerCastsPercent ?? new int[0],
                    };
                }
                item.EchoAdaptationValues = echo;
            }

            // ── Edge cases: CardDataNew list ────────────────────
            WireItemCardRefs(d, item);

            // ── Asset copy: spriteBossDrop + itemSound ───────────
            if (!string.IsNullOrEmpty(d.SpriteSource))
                CopyItemVisuals(item, d.SpriteSource);
            if (!string.IsNullOrEmpty(d.SpriteBossDrop))
            {
                var drop = SpriteUtils.FindSpriteByName(d.SpriteBossDrop);
                if (drop != null) item.SpriteBossDrop = drop;
                else Plugin.Log.LogWarning($"[DataHelper] Item '{d.Id}': boss drop sprite '{d.SpriteBossDrop}' not found.");
            }

            return item;
        }

        /// <summary>Snapshot an existing ItemData SO into an ItemDef DTO.</summary>
        public static ItemDef SnapshotItem(ItemData item)
        {
            if (item == null) return new ItemDef();
            var d = new ItemDef();

            // ── Auto-mapped fields (single source of truth: FieldMappings.Item) ──
            FieldMapper.Snapshot(FieldMappings.Item, item, d);

            // ── Edge cases: Name from paired card ───────────────
            var pairedCard = GetCard(item.Id);
            d.Name = pairedCard?.CardName ?? item.Id;

            // ── Edge cases: SpecialValue structs ─────────────────
            d.HealQuantitySpecialValue = SnapSV(item.HealQuantitySpecialValue);
            d.AuracurseGain1SpecialValue = SnapSV(item.AuracurseGain1SpecialValue);
            d.AuracurseGain2SpecialValue = SnapSV(item.AuracurseGain2SpecialValue);
            d.AuracurseGain3SpecialValue = SnapSV(item.AuracurseGain3SpecialValue);
            d.DttSpecialValues1 = SnapSV(item.DttSpecialValues1);
            d.DttSpecialValues2 = SnapSV(item.DttSpecialValues2);

            // ── Edge cases: EchoAdaptationValues struct array ───
            if (item.EchoAdaptationValues != null && item.EchoAdaptationValues.Length > 0)
            {
                d.EchoAdaptationValues = new List<EchoAdaptationDef>();
                foreach (var e in item.EchoAdaptationValues)
                    d.EchoAdaptationValues.Add(new EchoAdaptationDef
                    {
                        ResistanceFromPlayerDamagePercentBonuses = e.ResistanceFromPlayerDamagePercentBonuses ?? new int[0],
                        CursesPurgeFromPlayerCastsPercent = e.CursesPurgeFromPlayerCastsPercent ?? new int[0],
                    });
            }

            // ── Edge cases: CardDataNew list ────────────────────
            if (item.CardToGainList != null)
            {
                d.CardToGainList = new List<string>();
                foreach (var c in item.CardToGainList)
                    if (c != null) d.CardToGainList.Add(c.Id ?? "");
            }
            else
            {
                d.CardToGainList = null;
            }

            // ── Snapshot paired card ─────────────────────────────
            if (pairedCard != null)
                d.Card = ModProjectBuilder.SnapshotCard(pairedCard);

            return d;
        }

        /// <summary>Alias for MakeFullItem.</summary>
        public static ItemData MakeItem(ItemDef d) => MakeFullItem(d);
    }
}
