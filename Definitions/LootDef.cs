using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  LOOT TABLE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class LootDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
        public int NumItems = 1;
        public int GoldQuantity = 0;
        public bool AllowDropOnlyItems = false;
        public float PercentUncommon = 0f;
        public float PercentRare = 0f;
        public float PercentEpic = 0f;
        public float PercentMythic = 0f;
        public float ShadyScaleX = 1f;
        public float ShadyScaleY = 1f;
        public float ShadyOffsetX = 0f;
        public float ShadyOffsetY = 0f;
        /// <summary>ID of an existing LootData to copy the shadyModel GameObject from.</summary>
        public string ShadyModelSource = "";
        public List<LootItemDef> Items = new();
    }

    [Serializable]
    public class LootItemDef
    {
        public string CardId = "";
        public float Percent = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType LootType = Enums.CardType.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardRarity LootRarity = Enums.CardRarity.Common;

        public string LootMisc = "";
    }
}
