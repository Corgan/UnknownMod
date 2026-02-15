using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  PACK (Card Reward Packs)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class PackDef : IModEntity
    {
        public string PackId = "";
        [JsonIgnore] public string EntityId { get => PackId; set => PackId = value; }
        public string PackName = "";

        /// <summary>SubClass ID that this pack belongs to (hero subclass).</summary>
        public string RequiredClassId = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardClass PackClass = Enums.CardClass.Warrior;

        /// <summary>Up to 6 regular card IDs.</summary>
        public List<string> CardIds = new();

        /// <summary>Up to 2 special card IDs.</summary>
        public List<string> SpecialCardIds = new();

        /// <summary>Associated perk IDs.</summary>
        public List<string> PerkIds = new();
    }

    // ───────────────────────────────────────────────────────────────
    //  CARD PLAYER PACK (Card-flip Minigame)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CardPlayerPackDef : IModEntity
    {
        public string PackId = "";
        [JsonIgnore] public string EntityId { get => PackId; set => PackId = value; }

        /// <summary>Card entries (up to 4). Each has a CardId + random boon/injury flags.</summary>
        public List<CardPlayerSlot> Slots = new();

        /// <summary>Speed difficulty modifier (base = 10).</summary>
        public int ModSpeed = 0;

        /// <summary>Iterations difficulty modifier.</summary>
        public int ModIterations = 0;
    }

    [Serializable]
    public class CardPlayerSlot
    {
        public string CardId = "";
        public bool RandomBoon = false;
        public bool RandomInjury = false;
    }

    // ───────────────────────────────────────────────────────────────
    //  CARD PLAYER PAIRS PACK (Memory Minigame)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CardPlayerPairsPackDef : IModEntity
    {
        public string PackId = "";
        [JsonIgnore] public string EntityId { get => PackId; set => PackId = value; }

        /// <summary>Up to 6 card IDs (each doubled in-game).</summary>
        public List<string> CardIds = new();
    }
}
