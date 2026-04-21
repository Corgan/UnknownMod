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

            // ── Auto-mapped fields (single source of truth: FieldMappings.Item) ──
            FieldMapper.Apply(FieldMappings.Item, d, item);

            // ── Edge cases: SpecialValue structs ─────────────────
            item.HealQuantitySpecialValue = MakeSV(d.HealQuantitySpecialValue);
            item.AuracurseGain1SpecialValue = MakeSV(d.AuracurseGain1SpecialValue);
            item.AuracurseGain2SpecialValue = MakeSV(d.AuracurseGain2SpecialValue);
            item.AuracurseGain3SpecialValue = MakeSV(d.AuracurseGain3SpecialValue);
            item.DttSpecialValues1 = MakeSV(d.DttSpecialValues1);
            item.DttSpecialValues2 = MakeSV(d.DttSpecialValues2);

            // ── Edge cases: CardData list ────────────────────────
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

            // ── Asset copy: spriteBossDrop + itemSound ───────────
            if (!string.IsNullOrEmpty(d.SpriteSource))
                CopyItemVisuals(item, d.SpriteSource);

            return item;
        }

        /// <summary>Snapshot an existing ItemData SO into an ItemDef DTO.</summary>
        public static ItemDef SnapshotItem(ItemData item)
        {
            if (item == null) return new ItemDef();
            var d = new ItemDef();

            // ── Auto-mapped fields (single source of truth: FieldMappings.Item) ──
            FieldMapper.Snapshot(FieldMappings.Item, item, d);

            // ── Edge cases: Name from paired CardData ───────────
            var pairedCard = GetCard(item.Id);
            d.Name = pairedCard?.CardName ?? item.Id;

            // ── Edge cases: SpecialValue structs ─────────────────
            d.HealQuantitySpecialValue = SnapSV(item.HealQuantitySpecialValue);
            d.AuracurseGain1SpecialValue = SnapSV(item.AuracurseGain1SpecialValue);
            d.AuracurseGain2SpecialValue = SnapSV(item.AuracurseGain2SpecialValue);
            d.AuracurseGain3SpecialValue = SnapSV(item.AuracurseGain3SpecialValue);
            d.DttSpecialValues1 = SnapSV(item.DttSpecialValues1);
            d.DttSpecialValues2 = SnapSV(item.DttSpecialValues2);

            // ── Edge cases: CardData list ────────────────────────
            d.CardToGainList = new List<string>();
            if (item.CardToGainList != null)
                foreach (var c in item.CardToGainList)
                    if (c != null) d.CardToGainList.Add(c.Id ?? "");

            // ── Snapshot paired card ─────────────────────────────
            if (pairedCard != null)
                d.Card = ModProjectBuilder.SnapshotCard(pairedCard);

            return d;
        }

        /// <summary>Alias for MakeFullItem.</summary>
        public static ItemData MakeItem(ItemDef d) => MakeFullItem(d);
    }
}
