using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace UnknownMod.Definitions
{
    /// <summary>Top-level container for a complete zone definition.</summary>
    [Serializable]
    public class ZoneDef : IModEntity
    {
        public string ZoneId = "";
        [JsonIgnore] public string EntityId { get => ZoneId; set => ZoneId = value; }
        public string ZoneName = "";

        /// <summary>Short prefix used for entity IDs (e.g. "myc" for mycelium_abyss).</summary>
        public string IdPrefix = "";

        public bool ObeliskLow = false;
        public bool ObeliskHigh = false;
        public bool ObeliskFinal = false;
        public bool DisableExperience = false;
        public bool DisableMadness = false;

        /// <summary>DLC SKU requirement (empty = no DLC required).</summary>
        public string Sku = "";
        public bool ShouldSerializeSku() => !string.IsNullOrEmpty(Sku);

        /// <summary>Whether to swap the player's team when entering this zone.</summary>
        public bool ChangeTeamOnEntrance = false;
        public bool ShouldSerializeChangeTeamOnEntrance() => ChangeTeamOnEntrance;

        /// <summary>SubClass IDs for the replacement team (used when ChangeTeamOnEntrance is true).</summary>
        public List<string> NewTeam = new();
        public bool ShouldSerializeNewTeam() => NewTeam.Count > 0;

        /// <summary>Whether to restore the original team when leaving this zone.</summary>
        public bool RestoreTeamOnExit = false;
        public bool ShouldSerializeRestoreTeamOnExit() => RestoreTeamOnExit;

        /// <summary>Combat background sprite name (from ZoneData.CombatBackground).</summary>
        public string CombatBackgroundSprite = "";
        public bool ShouldSerializeCombatBackgroundSprite() => !string.IsNullOrEmpty(CombatBackgroundSprite);

        /// <summary>Filename of the background image (relative to zone folder).</summary>
        public string BackgroundImage = "background.jpeg";

        public Dictionary<string, NodeDef> Nodes = new();
        public Dictionary<string, CombatDef> Combats = new();
        public Dictionary<string, EventDef> Events = new();
        public Dictionary<string, NpcDef> Npcs = new();
        public Dictionary<string, CardDef> Cards = new();
        public Dictionary<string, ItemDef> Items = new();
        public Dictionary<string, LootDef> Loot = new();
        public Dictionary<string, RoadDef> Roads = new();

        /// <summary>Reusable sprite definitions. Key = sprite def ID, referenced by NpcDef.SpriteSource.</summary>
        public Dictionary<string, SpriteOverrideDef> Sprites = new();

        /// <summary>Visual layers (backgrounds, overlays, decorations) rendered in the map viewport.
        /// When overriding a base-game zone, only layers explicitly listed here are changed;
        /// unlisted base-game layers render unchanged.</summary>
        public List<VisualLayerDef> VisualLayers = new();

        /// <summary>Offset of the Nodes container from the zone root (matches base-game prefab).</summary>
        public float NodesOffsetX, NodesOffsetY;
        public bool ShouldSerializeNodesOffsetX() => Mathf.Abs(NodesOffsetX) > 0.001f;
        public bool ShouldSerializeNodesOffsetY() => Mathf.Abs(NodesOffsetY) > 0.001f;

        /// <summary>Offset of the Roads container from the zone root.</summary>
        public float RoadsOffsetX, RoadsOffsetY;
        public bool ShouldSerializeRoadsOffsetX() => Mathf.Abs(RoadsOffsetX) > 0.001f;
        public bool ShouldSerializeRoadsOffsetY() => Mathf.Abs(RoadsOffsetY) > 0.001f;

        /// <summary>DEPRECATED: per-NPC overrides from old format. Migrated into Sprites on load.</summary>
        public Dictionary<string, SpriteOverrideDef> SpriteOverrides = new();
        public bool ShouldSerializeSpriteOverrides() => false; // never write to JSON
    }

    // ───────────────────────────────────────────────────────────────
    //  VISUAL LAYER (map background / overlay / decoration)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class VisualLayerDef
    {
        /// <summary>Unique name within the zone (matches the GameObject name from the prefab).</summary>
        public string Name = "";

        /// <summary>Layer type for grouping and behavior.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public VisualLayerType Type = VisualLayerType.Sprite;

        /// <summary>Sprite name (from the game's asset bundle or a custom texture path).</summary>
        public string SpriteName = "";

        /// <summary>Sorting order for SpriteRenderer.</summary>
        public int SortingOrder = 0;

        /// <summary>Sorting layer name.</summary>
        public string SortingLayer = "Map";

        /// <summary>Local position relative to zone root.</summary>
        public float PosX = 0f;
        public float PosY = 0f;
        public float PosZ = 0f;

        /// <summary>Local scale.</summary>
        public float ScaleX = 1f;
        public float ScaleY = 1f;

        /// <summary>Sprite color (RGBA, 0-1).</summary>
        public float ColorR = 1f;
        public float ColorG = 1f;
        public float ColorB = 1f;
        public float ColorA = 1f;

        /// <summary>Sprite dimensions in pixels (informational, from source sprite).</summary>
        public float SpriteWidth = 0f;
        public float SpriteHeight = 0f;
        public float PPU = 100f;

        /// <summary>Whether this layer is visible in the preview.</summary>
        public bool Visible = true;

        /// <summary>Whether this layer is a mod override (true) or inherited from base game (false).</summary>
        public bool IsOverride = false;

        /// <summary>If true, this layer replaces a same-named base-game layer; if false, it's additive.</summary>
        public bool ReplacesBase = false;

        /// <summary>If true, this base-game layer should be hidden (removed by the mod).</summary>
        public bool Hidden = false;

        public bool FlipX = false;
        public bool FlipY = false;
    }

    public enum VisualLayerType
    {
        Sprite,         // SpriteRenderer (backgrounds, overlays, decorations)
        ParticleSystem, // Particle effects (cascades, smoke, fire, fog)
        Light,          // Light2D
        SpriteMask,     // SpriteMask (used for cloud masking in Uprising/Void)
        Container,      // Empty transform with children (thunder group, Castle Zoom, etc.)
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
    public class ZonePatchDef : IModEntity
    {
        /// <summary>Base-game zone ID being patched (e.g. "Aquarfall").</summary>
        public string TargetZoneId = "";
        [JsonIgnore] public string EntityId { get => TargetZoneId; set => TargetZoneId = value; }

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
