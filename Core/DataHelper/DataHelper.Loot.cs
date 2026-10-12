using System.Collections.Generic;
using UnknownMod.Definitions;
using HarmonyLib;
using UnityEngine;

namespace UnknownMod.Core
{
    public static partial class DataHelper
    {
        /// <summary>Create a LootData ScriptableObject from a LootDef.</summary>
        public static LootData MakeLoot(LootDef d)
        {
            var loot = ScriptableObject.CreateInstance<LootData>();
            loot.Id = d.Id;
            loot.NumItems = d.NumItems;
            loot.GoldQuantity = d.GoldQuantity;
            loot.AllowDropOnlyItems = d.AllowDropOnlyItems;
            loot.DefaultPercentUncommon = d.PercentUncommon;
            loot.DefaultPercentRare = d.PercentRare;
            loot.DefaultPercentEpic = d.PercentEpic;
            loot.DefaultPercentMythic = d.PercentMythic;
            loot.ShadyScaleX = d.ShadyScaleX;
            loot.ShadyScaleY = d.ShadyScaleY;
            loot.ShadyOffsetX = d.ShadyOffsetX;
            loot.ShadyOffsetY = d.ShadyOffsetY;

            if (d.Items != null && d.Items.Count > 0)
            {
                var items = new List<LootItem>(d.Items.Count);
                for (int i = 0; i < d.Items.Count; i++)
                {
                    if (d.Items[i] == null)
                        continue;

                    var item = new LootItem();
                    if (!string.IsNullOrEmpty(d.Items[i].CardId))
                        item.LootCard = GetCardSource(d.Items[i].CardId);
                    item.LootPercent = d.Items[i].Percent;
                    item.LootType = d.Items[i].LootType;
                    item.LootRarity = d.Items[i].LootRarity;
                    item.LootMisc = d.Items[i].LootMisc ?? "";
                    items.Add(item);
                }
                Traverse.Create(loot).Field("lootItemTable").SetValue(items.ToArray());
            }
            else
            {
                Traverse.Create(loot).Field("lootItemTable").SetValue(new LootItem[0]);
            }

            // Copy shady model from source loot
            if (!string.IsNullOrEmpty(d.ShadyModelSource))
                CopyLootVisuals(loot, d.ShadyModelSource);

            return loot;
        }

        /// <summary>Snapshot a LootData SO back into a LootDef for override editing.</summary>
        public static LootDef SnapshotLoot(LootData loot)
        {
            var d = new LootDef
            {
                Id = loot.Id ?? "",
                NumItems = loot.NumItems,
                GoldQuantity = loot.GoldQuantity,
                AllowDropOnlyItems = loot.AllowDropOnlyItems,
                PercentUncommon = loot.DefaultPercentUncommon,
                PercentRare = loot.DefaultPercentRare,
                PercentEpic = loot.DefaultPercentEpic,
                PercentMythic = loot.DefaultPercentMythic,
                ShadyScaleX = loot.ShadyScaleX,
                ShadyScaleY = loot.ShadyScaleY,
                ShadyOffsetX = loot.ShadyOffsetX,
                ShadyOffsetY = loot.ShadyOffsetY,
            };
            if (loot.LootItemTable != null)
            {
                foreach (var li in loot.LootItemTable)
                {
                    if (li == null)
                        continue;

                    d.Items.Add(new LootItemDef
                    {
                        CardId = li.LootCard != null ? li.LootCard.Id ?? "" : "",
                        Percent = li.LootPercent,
                        LootType = li.LootType,
                        LootRarity = li.LootRarity,
                        LootMisc = li.LootMisc ?? "",
                    });
                }
            }
            d.ShadyModelSource = d.Id;
            return d;
        }
    }
}
