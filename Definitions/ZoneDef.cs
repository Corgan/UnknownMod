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

        public Dictionary<string, NodeDef> Nodes = new();
        public Dictionary<string, RoadDef> Roads = new();

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

        /// <summary>Camera bounding box (world-space). Constrains the runtime camera to this area.
        /// When all zeros, defaults to the background sprite bounds.</summary>
        public float CameraBoundsMinX, CameraBoundsMinY;
        public float CameraBoundsMaxX, CameraBoundsMaxY;
        public bool ShouldSerializeCameraBoundsMinX() => Mathf.Abs(CameraBoundsMinX) > 0.001f;
        public bool ShouldSerializeCameraBoundsMinY() => Mathf.Abs(CameraBoundsMinY) > 0.001f;
        public bool ShouldSerializeCameraBoundsMaxX() => Mathf.Abs(CameraBoundsMaxX) > 0.001f;
        public bool ShouldSerializeCameraBoundsMaxY() => Mathf.Abs(CameraBoundsMaxY) > 0.001f;

        /// <summary>Whether camera bounds have been explicitly set.</summary>
        public bool HasCameraBounds => Mathf.Abs(CameraBoundsMaxX - CameraBoundsMinX) > 0.01f
                                   || Mathf.Abs(CameraBoundsMaxY - CameraBoundsMinY) > 0.01f;

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

        // ── Light2D properties ──────────────────────────────────────
        /// <summary>Light2D light type: Parametric=0, Freeform=1, Sprite=2, Point=3, Global=4.</summary>
        public int LightType = 3;
        public float Intensity = 1f;
        public float FalloffIntensity = 0.5f;
        public float PointLightInnerAngle = 360f;
        public float PointLightOuterAngle = 360f;
        public float PointLightInnerRadius = 0f;
        public float PointLightOuterRadius = 1f;
        public float ShapeLightFalloffSize = 0.5f;
        public int LightOrder = 0;
        public int BlendStyleIndex = 0;
        public bool ShadowsEnabled = false;
        public float ShadowIntensity = 0.5f;

        // ── ParticleSystem properties ────────────────────────────────
        public float Duration = 5f;
        public bool Loop = true;
        public bool Prewarm = false;
        public float StartLifetime = 5f;
        public float StartSpeed = 5f;
        public float StartSize = 1f;
        public int MaxParticles = 1000;
        public float SimulationSpeed = 1f;
        public bool PlayOnAwake = true;
        public float GravityModifier = 0f;
        public float EmissionRate = 10f;

        // ── PrefabEffect properties ──────────────────────────────────
        /// <summary>Name of a prefab in Resources/Effects/ (e.g. "smoke", "burn", "lightningstrike").</summary>
        public string EffectName = "";

        // ── SpriteMask properties ────────────────────────────────────
        public float AlphaCutoff = 0.5f;
        public bool CustomRange = false;
        public int FrontSortingOrder = 0;
        public int BackSortingOrder = 0;

        // ── Shader properties ────────────────────────────────────────
        /// <summary>Unity shader name (e.g. "Sprites/Default"). Applied to the quad's material.</summary>
        public string ShaderName = "Sprites/Default";
        /// <summary>SpriteMaskInteraction: 0=None, 1=VisibleInsideMask, 2=VisibleOutsideMask.</summary>
        public int MaskInteraction = 0;

        /// <summary>Shader preset (procedural texture pattern).</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ShaderPreset Preset = ShaderPreset.None;
        /// <summary>Preset-specific parameter 1 (e.g. line width, intensity).</summary>
        public float PresetParam1 = 1f;
        /// <summary>Preset-specific parameter 2 (e.g. gap width, softness).</summary>
        public float PresetParam2 = 1f;

        /// <summary>Shader keywords to enable on the material (e.g. "HOLOGRAM_ON").</summary>
        public List<string> ShaderKeywords = new List<string>();
        /// <summary>Shader float properties to set on the material (e.g. "_HologramStripesAmount": 0.2).</summary>
        public Dictionary<string, float> ShaderFloats = new Dictionary<string, float>();

        // ── Shared ───────────────────────────────────────────────────
        /// <summary>Component enabled state (applies to all renderable types).</summary>
        public bool Enabled = true;

        // ── Conditional serialization (keep JSON clean per type) ─────
        public bool ShouldSerializeLightType() => Type == VisualLayerType.Light;
        public bool ShouldSerializeIntensity() => Type == VisualLayerType.Light;
        public bool ShouldSerializeFalloffIntensity() => Type == VisualLayerType.Light;
        public bool ShouldSerializePointLightInnerAngle() => Type == VisualLayerType.Light;
        public bool ShouldSerializePointLightOuterAngle() => Type == VisualLayerType.Light;
        public bool ShouldSerializePointLightInnerRadius() => Type == VisualLayerType.Light;
        public bool ShouldSerializePointLightOuterRadius() => Type == VisualLayerType.Light;
        public bool ShouldSerializeShapeLightFalloffSize() => Type == VisualLayerType.Light;
        public bool ShouldSerializeLightOrder() => Type == VisualLayerType.Light;
        public bool ShouldSerializeBlendStyleIndex() => Type == VisualLayerType.Light;
        public bool ShouldSerializeShadowsEnabled() => Type == VisualLayerType.Light;
        public bool ShouldSerializeShadowIntensity() => Type == VisualLayerType.Light && ShadowsEnabled;
        public bool ShouldSerializeDuration() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeLoop() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializePrewarm() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeStartLifetime() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeStartSpeed() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeStartSize() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeMaxParticles() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeSimulationSpeed() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializePlayOnAwake() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeGravityModifier() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeEmissionRate() => Type == VisualLayerType.ParticleSystem;
        public bool ShouldSerializeAlphaCutoff() => Type == VisualLayerType.SpriteMask;
        public bool ShouldSerializeCustomRange() => Type == VisualLayerType.SpriteMask;
        public bool ShouldSerializeFrontSortingOrder() => Type == VisualLayerType.SpriteMask && CustomRange;
        public bool ShouldSerializeBackSortingOrder() => Type == VisualLayerType.SpriteMask && CustomRange;
        public bool ShouldSerializeShaderName() => Type == VisualLayerType.Shader;
        public bool ShouldSerializeMaskInteraction() => (Type == VisualLayerType.Shader || Type == VisualLayerType.Sprite) && MaskInteraction != 0;
        public bool ShouldSerializePreset() => Type == VisualLayerType.Shader && Preset != ShaderPreset.None;
        public bool ShouldSerializePresetParam1() => Type == VisualLayerType.Shader && Preset != ShaderPreset.None;
        public bool ShouldSerializePresetParam2() => Type == VisualLayerType.Shader && Preset != ShaderPreset.None;
        public bool ShouldSerializeShaderKeywords() => Type == VisualLayerType.Shader && ShaderKeywords != null && ShaderKeywords.Count > 0;
        public bool ShouldSerializeShaderFloats() => Type == VisualLayerType.Shader && ShaderFloats != null && ShaderFloats.Count > 0;
        public bool ShouldSerializeEffectName() => Type == VisualLayerType.PrefabEffect;
        public bool ShouldSerializeEnabled() => !Enabled;
    }

    public enum VisualLayerType
    {
        Sprite,         // SpriteRenderer (backgrounds, overlays, decorations)
        ParticleSystem, // Particle effects (cascades, smoke, fire, fog)
        Light,          // Light2D
        SpriteMask,     // SpriteMask (used for cloud masking in Uprising/Void)
        Container,      // Empty transform with children (thunder group, Castle Zoom, etc.)
        Shader,         // Procedural shader quad (resizable, maskable via SpriteMask)
        PrefabEffect,   // Cloned VFX prefab from Resources/Effects/ (EpicToonFX, combat VFX, etc.)
    }

    public enum ShaderPreset
    {
        None,           // White quad, shader name only
        Scanlines,      // Horizontal dark lines
        Vignette,       // Dark edges, bright center
        Noise,          // Random static grain
        Gradient,       // Linear gradient
        Checkerboard,   // Alternating squares
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

        /// <summary>Modified roads.</summary>
        public Dictionary<string, RoadDef> Roads = new();
    }
}
