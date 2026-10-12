using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using UnknownMod.Runtime;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  COMPILED CHARACTER VISUAL GRAPH (new schema)
    // ───────────────────────────────────────────────────────────────

    public enum VisualAttachmentKind
    {
        RigidAttachment,
        ExtractedSourceSubRig,
        HostBoundContent,
        FullPresentationReplacement
    }

    public enum VisualFacingPolicy
    {
        InheritHost,
        MirrorX,
        MirrorY,
        Authored
    }

    public enum VisualSortingStrategy
    {
        HostLayer,
        ExplicitLayerOrder,
        SourceLayerOffset
    }

    /// <summary>
    /// Where a node's sprites sit in the character's draw stack. The stack is the host's own sprites in the game's
    /// order (front to back) with each node inserted as one block, so placement is independent of raw sort numbers.
    /// Auto is the legacy behaviour: in front when OrderOffset &gt;= 0, otherwise behind.
    /// </summary>
    public enum VisualPlacement
    {
        Auto,
        Front,
        Back,
        InFrontOfPart,
        BehindPart
    }

    public enum VisualAnimationMode
    {
        Static,
        HostBound,
        SourceSynced,
        Procedural
    }

    public enum VisualAnimationFallback
    {
        None,
        StaticPose,
        HostBound,
        SkipNode
    }

    /// <summary>
    /// What a graft rotates and scales about. SpriteCenter pivots on the middle of the grafted sprites, so the gizmo sits on what you see.
    /// Anchor is the legacy behaviour (the source anchor bone, usually the source root). Missing in old files, which therefore stay Anchor until migrated.
    /// </summary>
    public enum VisualPivotMode
    {
        Anchor,
        SpriteCenter
    }

    public enum VisualAnchorPolicy
    {
        LockedAnchor,
        PreserveSourceMotion
    }

    public enum RigEffectType
    {
        // Colour (multiplies the rig tint)
        Pulse, Rainbow, Flicker, FadePulse,
        // Transform (offsets the rig root)
        Float, Shake, Breathe, Sway,
        // Particle auras
        Sparkles, Embers, Spores, Smoke, Snow, Bubbles
    }

    /// <summary>One live effect on a whole rig. Intensity and Speed scale each type's built-in amplitude and rate (1 = default).</summary>
    [Serializable]
    public class RigEffectDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public RigEffectType Type = RigEffectType.Pulse;
        /// <summary>Effect colour (HTML). Empty uses the type's default. Ignored by Rainbow and the transform effects.</summary>
        public string ColorHex = "";
        public float Intensity = 1f;
        public float Speed = 1f;
        public bool ShouldSerializeColorHex() => !string.IsNullOrEmpty(ColorHex);
    }

    [Serializable]
    public class CharacterVisualDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
        public string BasePresentation = "";

        /// <summary>Mirrors the whole model horizontally. Use it to turn a monster rig into a hero (or the reverse); a graft with no nodes is enough.</summary>
        public bool FlipX = false;
        public bool ShouldSerializeFlipX() => FlipX;

        /// <summary>Whole-rig colour tint (HTML colour, multiplied into every renderer) and opacity 0..1.</summary>
        public string TintHex = "";
        public float Alpha = 1f;
        public bool ShouldSerializeTintHex() => !string.IsNullOrEmpty(TintHex);
        public bool ShouldSerializeAlpha() => !Mathf.Approximately(Alpha, 1f);

        /// <summary>Whole-rig hue rotation in degrees (-180..180). Unlike Tint it shifts the existing colours instead of multiplying them.</summary>
        public float HueShift = 0f;
        public bool ShouldSerializeHueShift() => !Mathf.Approximately(HueShift, 0f);

        /// <summary>Animated colour, motion and particle effects applied to the whole rig.</summary>
        public List<RigEffectDef> Effects = new();
        public bool ShouldSerializeEffects() => Effects != null && Effects.Count > 0;

        public List<VisualNodeDef> Nodes = new();

        /// <summary>Edits to the host rig's own transforms (bones and sprite parts): offset, rotate, scale, tint and effects. Applied on top of the animation every frame.</summary>
        public List<HostPartDef> HostParts = new();
        public bool ShouldSerializeHostParts() => HostParts != null && HostParts.Count > 0;

        /// <summary>Canonical host paths of SpriteRenderers to keep disabled while this graph is applied
        /// (replaces the host sprite a node stands in for). Re-enforced every frame.</summary>
        public List<CanonicalTransformRef> HostHiddenRenderers = new();
        public bool ShouldSerializeHostHiddenRenderers() => HostHiddenRenderers != null && HostHiddenRenderers.Count > 0;

        public void Normalize()
        {
            if (Nodes == null) Nodes = new List<VisualNodeDef>();
            if (Effects == null) Effects = new List<RigEffectDef>();
            Effects.RemoveAll(e => e == null);
            if (HostHiddenRenderers == null) HostHiddenRenderers = new List<CanonicalTransformRef>();
            for (int i = 0; i < HostHiddenRenderers.Count; i++)
                if (HostHiddenRenderers[i] == null) HostHiddenRenderers[i] = new CanonicalTransformRef();
            if (HostParts == null) HostParts = new List<HostPartDef>();
            HostParts.RemoveAll(p => p == null);
            foreach (var part in HostParts) part.Normalize();
            foreach (var node in Nodes)
            {
                if (node == null) continue;
                if (node.HostSocketRef == null) node.HostSocketRef = new CanonicalTransformRef();
                if (node.SourceAnchorRef == null) node.SourceAnchorRef = new CanonicalTransformRef();
                if (node.SourceSubtreeRef == null) node.SourceSubtreeRef = new CanonicalTransformRef();
                if (node.RendererRefs == null) node.RendererRefs = new List<CanonicalTransformRef>();
                if (node.LocalAttachmentTransform == null) node.LocalAttachmentTransform = new LocalAttachmentTransform();
                if (node.SortingPolicy == null) node.SortingPolicy = new VisualSortingPolicy();
                if (node.AnimationPolicy == null) node.AnimationPolicy = new AnimationPolicyDef();
                if (node.AnimationPolicy.ClipMap == null) node.AnimationPolicy.ClipMap = new Dictionary<string, string>();
                if (node.Overrides == null) node.Overrides = new VisualNodeOverrides();
                for (int i = node.RendererRefs.Count - 1; i >= 0; i--)
                    if (node.RendererRefs[i] == null) node.RendererRefs[i] = new CanonicalTransformRef();
            }
        }
    }

    /// <summary>An edit to one transform of the host rig. Position, rotation and scale are offsets from whatever the animation produces, in the parent's space.
    /// Tint, opacity and effects apply to the part's sprites and everything beneath it.</summary>
    [Serializable]
    public class HostPartDef
    {
        public CanonicalTransformRef HostRef = new();
        public float PositionX = 0f;
        public float PositionY = 0f;
        public float Rotation = 0f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public float HueShift = 0f;
        public string TintHex = "";
        public float Alpha = 1f;
        public List<RigEffectDef> Effects = new();

        public bool ShouldSerializePositionX() => !Mathf.Approximately(PositionX, 0f);
        public bool ShouldSerializePositionY() => !Mathf.Approximately(PositionY, 0f);
        public bool ShouldSerializeRotation() => !Mathf.Approximately(Rotation, 0f);
        public bool ShouldSerializeScaleX() => !Mathf.Approximately(ScaleX, 1f);
        public bool ShouldSerializeScaleY() => !Mathf.Approximately(ScaleY, 1f);
        public bool ShouldSerializeTintHex() => !string.IsNullOrEmpty(TintHex);
        public bool ShouldSerializeAlpha() => !Mathf.Approximately(Alpha, 1f);
        public bool ShouldSerializeHueShift() => !Mathf.Approximately(HueShift, 0f);
        public bool ShouldSerializeEffects() => Effects != null && Effects.Count > 0;

        /// <summary>True when the part changes nothing, so it can be dropped without altering the rig.</summary>
        public bool IsNeutral()
        {
            return Mathf.Approximately(PositionX, 0f) && Mathf.Approximately(PositionY, 0f) && Mathf.Approximately(Rotation, 0f)
                && Mathf.Approximately(ScaleX, 1f) && Mathf.Approximately(ScaleY, 1f)
                && string.IsNullOrEmpty(TintHex) && Mathf.Approximately(Alpha, 1f) && Mathf.Approximately(HueShift, 0f) && (Effects == null || Effects.Count == 0);
        }

        public void ResetTransform() { PositionX = 0f; PositionY = 0f; Rotation = 0f; ScaleX = 1f; ScaleY = 1f; }

        public void Normalize()
        {
            if (HostRef == null) HostRef = new CanonicalTransformRef();
            if (Effects == null) Effects = new List<RigEffectDef>();
            Effects.RemoveAll(e => e == null);
        }
    }

    [Serializable]
    public class VisualNodeDef
    {
        public string NodeId = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public VisualAttachmentKind AttachmentKind = VisualAttachmentKind.RigidAttachment;

        public CanonicalTransformRef HostSocketRef = new();
        public string SourcePrefabRef = "";
        public CanonicalTransformRef SourceAnchorRef = new();
        public CanonicalTransformRef SourceSubtreeRef = new();

        [JsonConverter(typeof(StringEnumConverter))]
        public VisualPivotMode Pivot = VisualPivotMode.Anchor;
        public bool ShouldSerializePivot() => Pivot != VisualPivotMode.Anchor;

        public List<CanonicalTransformRef> RendererRefs = new();
        public LocalAttachmentTransform LocalAttachmentTransform = new();

        [JsonConverter(typeof(StringEnumConverter))]
        public VisualFacingPolicy FacingPolicy = VisualFacingPolicy.InheritHost;

        public VisualSortingPolicy SortingPolicy = new();
        public AnimationPolicyDef AnimationPolicy = new();
        public VisualNodeOverrides Overrides = new();
    }

    [Serializable]
    public class CanonicalTransformRef
    {
        public string Path = "";
    }

    [Serializable]
    public class LocalAttachmentTransform
    {
        public float PositionX = 0f;
        public float PositionY = 0f;
        public float Rotation = 0f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
    }

    [Serializable]
    public class VisualSortingPolicy
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public VisualSortingStrategy Strategy = VisualSortingStrategy.HostLayer;
        public int Layer = 0;
        public int Order = 0;
        public int OrderOffset = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public VisualPlacement Placement = VisualPlacement.Auto;
        /// <summary>Canonical path of the host SpriteRenderer used by InFrontOfPart / BehindPart.</summary>
        public string ReferencePath = "";
    }

    [Serializable]
    public class AnimationPolicyDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public VisualAnimationMode Mode = VisualAnimationMode.Static;
        public Dictionary<string, string> ClipMap = new();

        [JsonConverter(typeof(StringEnumConverter))]
        public VisualAnimationFallback Fallback = VisualAnimationFallback.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public VisualAnchorPolicy AnchorPolicy = VisualAnchorPolicy.LockedAnchor;
        public string ProceduralControllerRef = "";
    }

    [Serializable]
    public class VisualNodeOverrides
    {
        public bool Visible = true;
        public string TintHex = "";
        public float Alpha = 1f;
        public float Scale = 1f;
        public float HueShift = 0f;
        public bool ShouldSerializeHueShift() => !Mathf.Approximately(HueShift, 0f);
    }


    public enum DiagSeverity { Error, Warning, Info }

    [Serializable]
    public class DiagMessage
    {
        public DiagSeverity Severity;
        public string Message;
        public DiagMessage(DiagSeverity sev, string msg) { Severity = sev; Message = msg; }
        public override string ToString() => $"[{Severity}] {Message}";
    }
}
