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
    public class PackDef
    {
        public string PackId = "";
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
    public class CardPlayerPackDef
    {
        public string PackId = "";

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
    public class CardPlayerPairsPackDef
    {
        public string PackId = "";

        /// <summary>Up to 6 card IDs (each doubled in-game).</summary>
        public List<string> CardIds = new();
    }

    // ───────────────────────────────────────────────────────────────
    //  HERO DATA (Hero Name → SubClass Mapping)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class HeroDataDef
    {
        /// <summary>Auto-generated from HeroName (lowercase, no spaces).</summary>
        public string Id = "";
        public string HeroName = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClass = Enums.HeroClass.Warrior;

        /// <summary>SubClass ID this hero maps to.</summary>
        public string HeroSubClassId = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  ZONE PATCH
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents modifications to a base-game zone.
    /// Contains new entities to add and existing entities to override.
    /// The zone's base data is read from Globals.Instance at build time.
    /// </summary>
    [Serializable]
    public class ZonePatchDef
    {
        /// <summary>Base-game zone ID being patched (e.g. "Aquarfall").</summary>
        public string TargetZoneId = "";

        /// <summary>Auto-detected prefix for new entity IDs (e.g. "aqua_").</summary>
        public string DetectedPrefix = "";

        /// <summary>Next available node number for new entities.</summary>
        public int NextNodeNumber = 0;

        /// <summary>Added or modified nodes.</summary>
        public Dictionary<string, NodeDef> Nodes = new();

        /// <summary>Added or modified encounters.</summary>
        public Dictionary<string, CombatDef> Encounters = new();

        /// <summary>Added or modified events.</summary>
        public Dictionary<string, EventDef> Events = new();

        /// <summary>Modified roads.</summary>
        public Dictionary<string, RoadDef> Roads = new();
    }
}
