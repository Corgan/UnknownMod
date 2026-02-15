using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  EVENT REQUIREMENT
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class RequirementDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        public string RequirementName = "";
        public string Description = "";

        // ── Tracking ─────────────────────────────────────────────
        public bool AssignToPlayerAtBegin = false;
        public bool RequirementTrack = false;
        public bool ItemTrack = false;

        // ── Zone Tracking ────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.Zone RequirementZoneFinishTrack = Enums.Zone.None;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.Zone RequirementZoneFinishTrackAlternate = Enums.Zone.None;

        // ── Card Reference ───────────────────────────────────────
        public string TrackCard = "";     // CardData ID
    }

    // ───────────────────────────────────────────────────────────────
    //  CARDBACK
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CardbackDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        public string CardbackName = "";
        public string CardbackTextId = "";

        // ── Config ───────────────────────────────────────────────
        public int CardbackOrder = 1000;
        public bool BaseCardback = false;
        public bool Locked = false;
        public bool ShowIfLocked = false;

        // ── Requirements ─────────────────────────────────────────
        public int RankLevel = 0;
        public string Sku = "";
        public string SteamStat = "";
        public int AdventureLevel = 0;
        public int ObeliskLevel = 0;
        public int SingularityLevel = 0;
        public bool PdxAccountRequired = false;

        // ── Subclass Reference ───────────────────────────────────
        public string CardbackSubclass = "";   // SubClassData ID

        // ── Sprite Source ────────────────────────────────────────
        public string SpriteSource = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  TIER REWARD
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class TierRewardDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";             // keyed by tier number as string
        public int Tier = 0;

        // ── Rewards ──────────────────────────────────────────────
        public int Common = 0;
        public int Uncommon = 0;
        public int Rare = 0;
        public int Epic = 0;
        public int Mythic = 0;
        public int Dust = 0;
    }
}
