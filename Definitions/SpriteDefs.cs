using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  SPRITE OVERRIDE (NPC visual customization)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class SpriteOverrideDef : IModEntity
    {
        /// <summary>Unique ID of this sprite definition.</summary>
        public string NpcId = ""; // kept as "NpcId" for JSON backward compat
        [JsonIgnore] public string EntityId { get => NpcId; set => NpcId = value; }

        /// <summary>Base-game NPC ID providing the skeleton, animations, and default sprites.
        /// When an NpcDef.SpriteSource points to this sprite def, BaseSprite is used
        /// to clone the starting model via CopyVisuals.</summary>
        public string BaseSprite = "";
        public bool ShouldSerializeBaseSprite() => !string.IsNullOrEmpty(BaseSprite);

        public Dictionary<string, BoneOverride> Bones = new();
        public float ScaleMultiplier = 1f;
        public float OffsetX = 0f;
        public float OffsetY = 0f;

        /// <summary>DEPRECATED: Mode is ignored. All features are always available.
        /// Kept only for backward-compatible deserialization of old zone files.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public SpriteMode Mode = SpriteMode.Override;
        public bool ShouldSerializeMode() => false; // never write to JSON

        public string Spritesheet = "";
        public Dictionary<string, SpriteDef> CustomSprites = new();

        // Model-wide visual overrides
        public string ModelTintHex = "";
        public float ModelAlpha = 1f;
        public bool FlipX = false;
        public bool FlipY = false;

        // AllIn1SpriteShader effects (applied via material swap at runtime)
        public bool UseShaderEffects = false;
        public float HueShift = 0f;        // 0-1 (wraps 360°)
        public float Saturation = 1f;      // 0-2, default 1 = no change
        public float Brightness = 1f;      // 0-2, default 1 = no change
        public bool GlowEnabled = false;
        public string GlowColorHex = "#FFFFFF";
        public float GlowIntensity = 1f;   // 0-10
        public bool OutlineEnabled = false;
        public string OutlineColorHex = "#000000";
        public float OutlineSize = 1f;     // 0-10
        public float GreyscaleBlend = 0f;  // 0-1
        public float GhostTransparency = 0f; // 0-1, 0 = off

        /// <summary>Per-animation-clip keyframe overrides. Key = clip name.</summary>
        public Dictionary<string, AnimOverrideDef> AnimOverrides = new();

        /// <summary>
        /// Optional: NPC ID to source the AnimatorController from.
        /// Uses AnimatorOverrideController to wrap the source NPC's controller,
        /// keeping the state machine (idle/attack/cast/hit) but allowing clip swaps.
        /// If empty, uses the skeleton donor's (SpriteSource) original controller.
        /// </summary>
        public string AnimationSource = "";
        public bool ShouldSerializeAnimationSource() => !string.IsNullOrEmpty(AnimationSource);

        /// <summary>
        /// Sprites to add to the NPC that don't exist on the original model.
        /// Key = a unique user-chosen name for the new sprite bone.
        /// </summary>
        public Dictionary<string, AddedSpriteDef> AddedSprites = new();
        public bool ShouldSerializeAddedSprites() => AddedSprites.Count > 0;

        /// <summary>
        /// Bone names to completely remove (destroy the SpriteRenderer + SpriteSkin).
        /// More aggressive than Hidden — the bone's sprite is fully destroyed at runtime.
        /// </summary>
        public HashSet<string> RemovedBones = new();
        public bool ShouldSerializeRemovedBones() => RemovedBones.Count > 0;

        /// <summary>
        /// Rig bones to add to the NPC skeleton at runtime.
        /// These are pure transform bones (no SpriteRenderer) that can
        /// be referenced by SpriteSkin boneTransforms[] for mesh deformation.
        /// Key = unique user-chosen name for the new bone.
        /// </summary>
        public Dictionary<string, AddedBoneDef> AddedBones = new();
        public bool ShouldSerializeAddedBones() => AddedBones.Count > 0;
    }

    // ───────────────────────────────────────────────────────────────
    //  SPRITE HELPER TYPES
    // ───────────────────────────────────────────────────────────────

    /// <summary>Defines a sprite to be added to the NPC at runtime on an existing parent bone.</summary>
    [Serializable]
    public class AddedSpriteDef
    {
        /// <summary>Name of the existing rig bone to attach this sprite to as a child.
        /// All other properties (source, transform, visual) are stored in
        /// SpriteOverrideDef.Bones[name] just like any existing bone.</summary>
        public string ParentBone = "";
    }

    /// <summary>Defines a pure rig bone to add to the NPC skeleton at runtime.</summary>
    [Serializable]
    public class AddedBoneDef
    {
        /// <summary>Name of the existing bone to attach this new bone to as a child.</summary>
        public string ParentBone = "";

        /// <summary>Local position relative to parent.</summary>
        public float PosX = 0f;
        public float PosY = 0f;
        /// <summary>Local rotation in degrees.</summary>
        public float Rotation = 0f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        /// <summary>Bone length (visual hint in editor, used for auto-weight radius).</summary>
        public float Length = 0.5f;

        /// <summary>
        /// Optional: list of sprite bone names that this new bone should influence.
        /// For each sprite, auto-weight will assign vertex influence based on distance.
        /// </summary>
        public List<string> InfluenceSprites = new();
        public bool ShouldSerializeInfluenceSprites() => InfluenceSprites.Count > 0;

        /// <summary>Auto-weight radius: vertices within this distance get blended influence.</summary>
        public float WeightRadius = 0.5f;
        /// <summary>Auto-weight falloff: 0=sharp cutoff, 1=linear, 2=smooth quadratic.</summary>
        public float WeightFalloff = 1f;
    }

    [Serializable]
    public class BoneOverride
    {
        public float PosX = 0f;
        public float PosY = 0f;
        public float Rotation = 0f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public bool Visible = true;
        public int SortingOffset = 0;
        public string ColorHex = "";
        public string SpriteFrom = "";
        public bool FlipX = false;
        public bool FlipY = false;
        public float Alpha = 1f;

        /// <summary>
        /// DEPRECATED: Kept for backward-compatible deserialization.
        /// Branch grafting now imports source bones directly, making manual remap unnecessary.
        /// </summary>
        public Dictionary<string, string> BoneRemap = new();
        public bool ShouldSerializeBoneRemap() => false; // never serialize
    }

    /// <summary>DEPRECATED: kept only for backward-compatible deserialization.
    /// The mode system has been removed — all features are always available.</summary>
    public enum SpriteMode { Override, Graft, CustomSprite }

    [Serializable]
    public class SpriteDef
    {
        public string ImagePath = "";
        public float[] Rect = null;
        public float PivotX = 0.5f;
        public float PivotY = 0.5f;
        public float PPU = 0f;
    }

    /// <summary>Per-clip animation override. Stores bone keyframes for one animation clip.</summary>
    [Serializable]
    public class AnimOverrideDef
    {
        public string ClipName = "";
        /// <summary>Key = bone name, Value = list of keyframes sorted by Time.</summary>
        public Dictionary<string, List<BoneKeyframe>> BoneKeyframes = new();
    }

    /// <summary>A single keyframe for a bone within an animation override.</summary>
    [Serializable]
    public class BoneKeyframe
    {
        /// <summary>Normalized time within the clip (0 = start, 1 = end).</summary>
        public float Time = 0f;
        public float PosX = 0f;
        public float PosY = 0f;
        public float Rotation = 0f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
    }
}
