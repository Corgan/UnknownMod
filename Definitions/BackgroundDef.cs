using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    /// <summary>
    /// Definition for a custom combat background.
    /// Each background is composed of layered sprites with sorting,
    /// position, scale, and color properties — mirroring the structure
    /// of the game's background prefabs.
    /// </summary>
    [Serializable]
    public class BackgroundDef : IModEntity
    {
        public string BackgroundId = "";
        [JsonIgnore] public string EntityId { get => BackgroundId; set => BackgroundId = value; }

        /// <summary>Display name (for the editor UI).</summary>
        public string DisplayName = "";
        public bool ShouldSerializeDisplayName() => !string.IsNullOrEmpty(DisplayName);

        /// <summary>Ordered list of sprite layers (back-to-front by SortingOrder).</summary>
        public List<BackgroundLayerDef> Layers = new();
    }

    /// <summary>
    /// A single layer within a combat background.
    /// Supports Sprite, Light, ParticleSystem, SpriteMask, and Container types.
    /// </summary>
    [Serializable]
    public class BackgroundLayerDef
    {
        /// <summary>Layer name (becomes the child GameObject name).</summary>
        public string Name = "";

        /// <summary>Layer type.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public VisualLayerType Type = VisualLayerType.Sprite;
        public bool ShouldSerializeType() => Type != VisualLayerType.Sprite;

        /// <summary>Sprite reference — game sprite name or mod texture path.</summary>
        public string SpriteName = "";

        /// <summary>Whether this layer is enabled in the editor viewport (transient, not serialized).</summary>
        [JsonIgnore] public bool Enabled = true;

        /// <summary>SpriteRenderer sorting order (higher draws on top). Base game uses -1000 (front) to -1400 (back).</summary>
        public int SortingOrder = -1400;

        /// <summary>SpriteRenderer sorting layer name. Must be "Background" to render behind combat UI.</summary>
        public string SortingLayer = "Background";

        /// <summary>Local position relative to background root.</summary>
        public float PosX = 0f;
        public float PosY = 0f;
        public float PosZ = 0f;

        /// <summary>Local scale.</summary>
        public float ScaleX = 1f;
        public float ScaleY = 1f;

        /// <summary>Local Z-rotation in degrees.</summary>
        public float Rotation = 0f;

        /// <summary>Tint color (RGBA, 0-1).</summary>
        public float ColorR = 1f;
        public float ColorG = 1f;
        public float ColorB = 1f;
        public float ColorA = 1f;

        public bool FlipX = false;
        public bool FlipY = false;

        /// <summary>Whether this layer is visible (enabled).</summary>
        public bool Visible = true;

        // ── Light2D properties ──────────────────────────────────────
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
        public string EffectName = "";

        // ── SpriteMask properties ────────────────────────────────────
        public float AlphaCutoff = 0.5f;
        public bool CustomRange = false;
        public int FrontSortingOrder = 0;
        public int BackSortingOrder = 0;

        // ── Shader properties ────────────────────────────────────────
        /// <summary>Unity shader name for the quad's material.</summary>
        public string ShaderName = "Sprites/Default";
        /// <summary>SpriteMaskInteraction: 0=None, 1=VisibleInsideMask, 2=VisibleOutsideMask.</summary>
        public int MaskInteraction = 0;

        /// <summary>Shader preset (procedural texture pattern).</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ShaderPreset Preset = ShaderPreset.None;
        public float PresetParam1 = 1f;
        public float PresetParam2 = 1f;

        /// <summary>Shader keywords to enable on the material.</summary>
        public List<string> ShaderKeywords = new List<string>();
        /// <summary>Shader float properties to set on the material.</summary>
        public Dictionary<string, float> ShaderFloats = new Dictionary<string, float>();

        // ── Conditional serialization ────────────────────────────────
        public bool ShouldSerializeVisible() => !Visible;
        public bool ShouldSerializeFlipX() => FlipX;
        public bool ShouldSerializeFlipY() => FlipY;
        public bool ShouldSerializePosZ() => Math.Abs(PosZ) > 0.001f;
        public bool ShouldSerializeColorR() => Math.Abs(ColorR - 1f) > 0.001f;
        public bool ShouldSerializeColorG() => Math.Abs(ColorG - 1f) > 0.001f;
        public bool ShouldSerializeColorB() => Math.Abs(ColorB - 1f) > 0.001f;
        public bool ShouldSerializeColorA() => Math.Abs(ColorA - 1f) > 0.001f;
        public bool ShouldSerializeRotation() => Math.Abs(Rotation) > 0.001f;

        // Light
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

        // ParticleSystem
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

        // SpriteMask
        public bool ShouldSerializeAlphaCutoff() => Type == VisualLayerType.SpriteMask;
        public bool ShouldSerializeCustomRange() => Type == VisualLayerType.SpriteMask;
        public bool ShouldSerializeFrontSortingOrder() => Type == VisualLayerType.SpriteMask && CustomRange;
        public bool ShouldSerializeBackSortingOrder() => Type == VisualLayerType.SpriteMask && CustomRange;

        // Shader
        public bool ShouldSerializeShaderName() => Type == VisualLayerType.Shader;
        public bool ShouldSerializeMaskInteraction() => (Type == VisualLayerType.Shader || Type == VisualLayerType.Sprite) && MaskInteraction != 0;
        public bool ShouldSerializePreset() => Type == VisualLayerType.Shader && Preset != ShaderPreset.None;
        public bool ShouldSerializePresetParam1() => Type == VisualLayerType.Shader && Preset != ShaderPreset.None;
        public bool ShouldSerializePresetParam2() => Type == VisualLayerType.Shader && Preset != ShaderPreset.None;
        public bool ShouldSerializeShaderKeywords() => Type == VisualLayerType.Shader && ShaderKeywords != null && ShaderKeywords.Count > 0;
        public bool ShouldSerializeShaderFloats() => Type == VisualLayerType.Shader && ShaderFloats != null && ShaderFloats.Count > 0;
        public bool ShouldSerializeEffectName() => Type == VisualLayerType.PrefabEffect;
    }
}
