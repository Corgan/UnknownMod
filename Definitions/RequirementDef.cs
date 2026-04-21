using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  EVENT REQUIREMENT
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class RequirementDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
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

        // ── Visuals ──────────────────────────────────────────────
        /// <summary>ID of an existing EventRequirementData to copy itemSprite/trackSprite from.</summary>
        public string SpriteSource = "";
    }
}
