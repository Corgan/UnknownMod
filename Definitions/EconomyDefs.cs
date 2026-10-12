using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  ECONOMY: prices as data
    //  Shop stock is already a loot table (LootDef), so what a shop sells is data. An economy set is
    //  the missing half: it changes what things COST. Crafting, upgrading, transforming and removing
    //  cards, buying items, corruption, divinations and shop rerolls all run through one price rule list.
    // ───────────────────────────────────────────────────────────────

    public enum PriceFunction
    {
        /// <summary>Crafting a card (dust).</summary>
        Craft,
        /// <summary>Upgrading a card (dust).</summary>
        Upgrade,
        Transform,
        /// <summary>Removing a card from the deck (gold).</summary>
        Remove,
        /// <summary>Buying an item or pet from a shop (gold).</summary>
        Item,
        Corruption,
        Divination,
        /// <summary>Rerolling the stock of a shop.</summary>
        Reroll
    }

    [Serializable]
    public class EconomyDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
        public string Description = "";
        public bool Enabled = true;
        public List<PriceRuleDef> Rules = new();
    }

    /// <summary>
    /// One price adjustment. Empty filters match everything. The adjusted price is: SetTo (if 0 or more),
    /// then Percent, then Flat, never below zero. Rules apply in order, across all active economy sets.
    /// </summary>
    [Serializable]
    public class PriceRuleDef
    {
        public string Name = "";
        public bool Enabled = true;

        [JsonConverter(typeof(StringEnumConverter))]
        public PriceFunction Function = PriceFunction.Item;

        /// <summary>Card rarity name (Common, Uncommon, Rare, Epic, Mythic). Empty = any.</summary>
        public string Rarity = "";
        /// <summary>Card type name (for example Weapon, Armor, Pet). Empty = any.</summary>
        public string CardType = "";
        /// <summary>One specific card or item. Empty = any.</summary>
        public string CardId = "";
        /// <summary>One specific shop (the loot table id). Empty = any, including crafting and town services.</summary>
        public string ShopId = "";

        /// <summary>Added to the price in percent: -100 is free, 50 is half again as much.</summary>
        public int Percent = 0;
        public int Flat = 0;
        /// <summary>Absolute price. -1 leaves the price alone.</summary>
        public int SetTo = -1;

        public bool ShouldSerializeName() => !string.IsNullOrEmpty(Name);
        public bool ShouldSerializeEnabled() => !Enabled;
        public bool ShouldSerializeRarity() => !string.IsNullOrEmpty(Rarity);
        public bool ShouldSerializeCardType() => !string.IsNullOrEmpty(CardType);
        public bool ShouldSerializeCardId() => !string.IsNullOrEmpty(CardId);
        public bool ShouldSerializeShopId() => !string.IsNullOrEmpty(ShopId);
        public bool ShouldSerializePercent() => Percent != 0;
        public bool ShouldSerializeFlat() => Flat != 0;
        public bool ShouldSerializeSetTo() => SetTo >= 0;
    }
}
