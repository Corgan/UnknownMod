using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{

    // ───────────────────────────────────────────────────────────────
    //  NODE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class NodeDef : IModEntity
    {
        public string NodeId = "";
        [JsonIgnore] public string EntityId { get => NodeId; set => NodeId = value; }
        public string NodeName = "";
        public string Description = "";
        public float PosX = 0f;
        public float PosY = 0f;

        public bool TravelDestination = false;
        public bool GoToTown = false;
        public int ExistsPercent = 100;
        public string ExistsSku = "";
        public bool ShouldSerializeExistsSku() => !string.IsNullOrEmpty(ExistsSku);
        public bool DisableCorruption = false;
        public bool DisableRandom = false;
        public bool VisibleIfNotRequirement = false;

        /// <summary>SubClass ID — disable this node when this hero is unlocked.</summary>
        public string HeroToDisableNodeWhenUnlockedId = "";
        public bool ShouldSerializeHeroToDisableNodeWhenUnlockedId() => !string.IsNullOrEmpty(HeroToDisableNodeWhenUnlockedId);

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.NodeGround NodeGround = Enums.NodeGround.None;

        /// <summary>EventRequirement ID that must be met to enter this node.</summary>
        public string NodeRequirementId = "";

        // ── Combat assignment (mirrors NodeData.NodeCombat[]) ────────
        /// <summary>Combat IDs assigned to this node (game uses CombatData[]).</summary>
        public List<string> CombatIds = new();
        public bool ShouldSerializeCombatIds() => CombatIds.Count > 0;

        /// <summary>Backward compat: old format wrote a single "CombatId" string.</summary>
        [JsonProperty("CombatId")]
        private string _legacyCombatId { set { if (!string.IsNullOrEmpty(value) && CombatIds.Count == 0) CombatIds.Add(value); } }

        /// <summary>Convenience: get/set first combat ID (most nodes have 0-1 combats).</summary>
        [JsonIgnore]
        public string CombatId
        {
            get => CombatIds.Count > 0 ? CombatIds[0] : "";
            set
            {
                if (string.IsNullOrEmpty(value)) { CombatIds.Clear(); return; }
                if (CombatIds.Count == 0) CombatIds.Add(value);
                else CombatIds[0] = value;
            }
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatTier CombatTier = Enums.CombatTier.T0;
        public int CombatPercent = -1;

        // ── Event assignment (mirrors NodeData.NodeEvent[]) ──────────
        /// <summary>Event IDs assigned to this node (game uses EventData[]).</summary>
        public List<string> EventIds = new();
        public bool ShouldSerializeEventIds() => EventIds.Count > 0;

        /// <summary>Backward compat: old format wrote a single "EventId" string.</summary>
        [JsonProperty("EventId")]
        private string _legacyEventId { set { if (!string.IsNullOrEmpty(value) && EventIds.Count == 0) EventIds.Add(value); } }

        /// <summary>Convenience: get/set first event ID (most nodes have 0-1 events).</summary>
        [JsonIgnore]
        public string EventId
        {
            get => EventIds.Count > 0 ? EventIds[0] : "";
            set
            {
                if (string.IsNullOrEmpty(value)) { EventIds.Clear(); return; }
                if (EventIds.Count == 0) EventIds.Add(value);
                else EventIds[0] = value;
            }
        }

        public int EventPercent = -1;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatTier NodeEventTier = Enums.CombatTier.T0;

        /// <summary>Per-event priority (parallel to EventIds; lowest = highest priority).</summary>
        public List<int> NodeEventPriority = new();
        public bool ShouldSerializeNodeEventPriority() => NodeEventPriority.Count > 0;

        /// <summary>Per-event percent weight (parallel to EventIds).</summary>
        public List<int> NodeEventPercent = new();
        public bool ShouldSerializeNodeEventPercent() => NodeEventPercent.Count > 0;

        // Connections — list of target node IDs
        public List<string> Connections = new();

        // Conditional connections
        public List<NodeConnectionReqDef> ConnectionRequirements = new();
        public bool ShouldSerializeConnectionRequirements() => ConnectionRequirements.Count > 0;

        /// <summary>Map pieces attached to this node (large sprite overlays shown when the node is visible).
        /// These are rendered as children of the node GO and inherit its requirement-gated visibility.</summary>
        public List<MapPieceDef> MapPieces = new();
        public bool ShouldSerializeMapPieces() => MapPieces.Count > 0;
    }

    // ───────────────────────────────────────────────────────────────
    //  MAP PIECE (node-attached background/overlay sprite)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A large sprite attached as a child of a node (named "mapPiece" in the game).
    /// Inherits the node's requirement-gated visibility — when the node is shown/hidden,
    /// the map piece follows. Used for background overlays (e.g. Dreadnought upper deck),
    /// room reveals, rift icons, etc.
    /// </summary>
    [Serializable]
    public class MapPieceDef
    {
        /// <summary>Name of the sprite from game resources or a custom texture path.</summary>
        public string SpriteName = "";

        /// <summary>Sorting order for the SpriteRenderer.</summary>
        public int SortingOrder = 0;

        /// <summary>Sorting layer name.</summary>
        public string SortingLayer = "Map";

        /// <summary>Local position relative to the parent node.</summary>
        public float PosX = 0f;
        public float PosY = 0f;

        /// <summary>Local scale.</summary>
        public float ScaleX = 1f;
        public float ScaleY = 1f;

        /// <summary>Color tint (RGBA, 0-1).</summary>
        public float ColorR = 1f;
        public float ColorG = 1f;
        public float ColorB = 1f;
        public float ColorA = 1f;

        /// <summary>Sprite dimensions in pixels (informational).</summary>
        public float SpriteWidth = 0f;
        public float SpriteHeight = 0f;

        public bool FlipX = false;
        public bool FlipY = false;
    }

    // ───────────────────────────────────────────────────────────────
    //  NODE CONNECTION REQUIREMENT
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class NodeConnectionReqDef
    {
        public string TargetNodeId = "";
        public string RequirementId = "";
        public string IfNotNodeId = "";
    }
}
