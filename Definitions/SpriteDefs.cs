using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  CHARACTER OVERRIDE (NPC / Hero Skin visual customization)
    // ───────────────────────────────────────────────────────────────

    /// <summary>Whether a CharacterOverrideDef targets an NPC or a Hero Skin.</summary>
    public enum SkinTargetType { NPC, HeroSkin, Item }

    /// <summary>
    /// Top-level definition for all visual overrides on an NPC or Hero Skin.
    /// Serialized as one JSON file per entity. Sections:
    ///   - Grafts: ordered list of sprite branch imports from other NPCs/skins
    ///   - BoneOverrides: per-bone transform/visual overrides on the host skeleton
    ///   - RemovedBones: structural modifications
    ///   - CustomSprites: texture replacements
    ///   - Model: scale, offset, flip, tint, alpha
    ///   - AnimOverrides: per-clip keyframe overrides (host skeleton only)
    /// </summary>
    [Serializable]
    public class CharacterOverrideDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        [JsonConverter(typeof(StringEnumConverter))]
        public SkinTargetType SkinTarget = SkinTargetType.NPC;
        public bool ShouldSerializeSkinTarget() => SkinTarget != SkinTargetType.NPC;

        /// <summary>Base-game NPC/Skin ID providing the skeleton, animations, and default sprites.</summary>
        public string BaseSprite = "";
        public bool ShouldSerializeBaseSprite() => !string.IsNullOrEmpty(BaseSprite);

        // ── Grafts ───────────────────────────────────────────────
        /// <summary>Ordered list of sprite branch imports from other NPCs/skins.
        /// Each graft clones the source sprite's bone branch as a GraftPuppet
        /// with its own Animator synced via AnimatorStateMirror.</summary>
        public List<GraftDef> Grafts = new();
        public bool ShouldSerializeGrafts() => Grafts.Count > 0;

        // ── Host Bone Overrides ──────────────────────────────────
        /// <summary>Per-bone transform/visual overrides for the host skeleton.
        /// Additive for Animator-driven bones, absolute for added bones.</summary>
        public Dictionary<string, BoneOverride> BoneOverrides = new();

        public HashSet<string> RemovedBones = new();
        public bool ShouldSerializeRemovedBones() => RemovedBones.Count > 0;

        // ── Custom Sprites ───────────────────────────────────────
        public string Spritesheet = "";
        public Dictionary<string, SpriteDef> CustomSprites = new();

        // ── Model ────────────────────────────────────────────────
        public ModelOverrides Model = new();
        public bool ShouldSerializeModel() => !Model.IsDefault();

        // ── Animation Overrides (host skeleton only) ─────────────
        /// <summary>Per-animation-clip keyframe overrides. Key = clip name.</summary>
        public Dictionary<string, AnimOverrideDef> AnimOverrides = new();

    }

    // ───────────────────────────────────────────────────────────────
    //  GRAFT DEFINITION (self-contained graft unit)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Defines a sprite branch import from another NPC/skin. At runtime, creates
    /// a GraftPuppet with its own Animator synced to the host via AnimatorStateMirror.
    /// All overrides within the graft are scoped to this GraftDef.
    /// </summary>
    [Serializable]
    public class GraftDef
    {
        /// <summary>Host bone this graft attaches to.</summary>
        public string TargetBone = "";

        /// <summary>Source in "npc_id/bone_name" or "skin_id/bone_name" format.</summary>
        public string Source = "";

        /// <summary>Hide the original sprite at the target bone?</summary>
        public bool ReplaceTarget = true;

        // ── Scoped overrides (only affect bones within this graft) ──
        public Dictionary<string, BoneOverride> BoneOverrides = new();
        public bool ShouldSerializeBoneOverrides() => BoneOverrides.Count > 0;

        public Dictionary<string, SpriteDef> CustomSprites = new();
        public bool ShouldSerializeCustomSprites() => CustomSprites.Count > 0;

        public Dictionary<string, AnimOverrideDef> AnimOverrides = new();
        public bool ShouldSerializeAnimOverrides() => AnimOverrides.Count > 0;
    }

    // ───────────────────────────────────────────────────────────────
    //  MODEL OVERRIDES (scale, offset, flip, tint, alpha)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class ModelOverrides
    {
        public float Scale = 1f;
        public float OffsetX = 0f;
        public float OffsetY = 0f;
        public bool FlipX = false;
        public bool FlipY = false;
        public string TintHex = "";
        public float Alpha = 1f;

        public bool IsDefault() =>
            Scale == 1f && OffsetX == 0f && OffsetY == 0f &&
            !FlipX && !FlipY &&
            string.IsNullOrEmpty(TintHex) && Alpha >= 1f;
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
        public bool FlipX = false;
        public bool FlipY = false;
        public float Alpha = 1f;
        /// <summary>Pivot X override (0–1). -1 means use original sprite pivot.</summary>
        public float PivotX = -1f;
        /// <summary>Pivot Y override (0–1). -1 means use original sprite pivot.</summary>
        public float PivotY = -1f;
    }

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

    // ───────────────────────────────────────────────────────────────
    //  VALIDATION
    // ───────────────────────────────────────────────────────────────

    public enum DiagSeverity { Error, Warning, Info }

    [Serializable]
    public class DiagMessage
    {
        public DiagSeverity Severity;
        public string Message;
        public DiagMessage(DiagSeverity sev, string msg) { Severity = sev; Message = msg; }
        public override string ToString() => $"[{Severity}] {Message}";
    }

    public static class OverrideValidator
    {
        /// <summary>Validate a CharacterOverrideDef and return a list of diagnostics.</summary>
        public static List<DiagMessage> Validate(CharacterOverrideDef ovr, ICollection<string> knownBoneNames = null)
        {
            var diags = new List<DiagMessage>();
            if (ovr == null) { diags.Add(new DiagMessage(DiagSeverity.Error, "Override definition is null.")); return diags; }

            // ── Identity ──
            if (string.IsNullOrEmpty(ovr.Id))
                diags.Add(new DiagMessage(DiagSeverity.Error, "Id is empty."));

            // ── Grafts ──
            for (int i = 0; i < ovr.Grafts.Count; i++)
            {
                var g = ovr.Grafts[i];
                string prefix = $"Graft #{i + 1}";
                if (string.IsNullOrEmpty(g.TargetBone))
                    diags.Add(new DiagMessage(DiagSeverity.Error, $"{prefix}: TargetBone is empty."));
                else if (knownBoneNames != null && !knownBoneNames.Contains(g.TargetBone))
                    diags.Add(new DiagMessage(DiagSeverity.Warning, $"{prefix}: TargetBone '{g.TargetBone}' not found in host skeleton."));
                if (string.IsNullOrEmpty(g.Source))
                    diags.Add(new DiagMessage(DiagSeverity.Error, $"{prefix}: Source is empty."));
                ValidateBoneOverrides(g.BoneOverrides, $"{prefix} BoneOverrides", diags);
                ValidateCustomSprites(g.CustomSprites, $"{prefix} CustomSprites", diags);
            }

            // Check duplicate graft targets
            var graftTargets = new HashSet<string>();
            for (int i = 0; i < ovr.Grafts.Count; i++)
            {
                var tb = ovr.Grafts[i].TargetBone;
                if (!string.IsNullOrEmpty(tb) && !graftTargets.Add(tb))
                    diags.Add(new DiagMessage(DiagSeverity.Warning, $"Graft #{i + 1}: duplicate TargetBone '{tb}' (multiple grafts on same bone)."));
            }

            // ── Host BoneOverrides ──
            ValidateBoneOverrides(ovr.BoneOverrides, "BoneOverrides", diags);

            // ── CustomSprites ──
            ValidateCustomSprites(ovr.CustomSprites, "CustomSprites", diags);

            // ── RemovedBones ──
            if (knownBoneNames != null)
            {
                foreach (var rb in ovr.RemovedBones)
                {
                    if (!knownBoneNames.Contains(rb))
                        diags.Add(new DiagMessage(DiagSeverity.Info, $"RemovedBone '{rb}' not found in current skeleton (may be from a different base)."));
                }
            }

            // ── Model ──
            if (ovr.Model.Scale <= 0f)
                diags.Add(new DiagMessage(DiagSeverity.Warning, "Model.Scale is <= 0."));
            if (ovr.Model.Alpha < 0f || ovr.Model.Alpha > 1f)
                diags.Add(new DiagMessage(DiagSeverity.Warning, $"Model.Alpha ({ovr.Model.Alpha}) is outside [0, 1]."));
            if (!string.IsNullOrEmpty(ovr.Model.TintHex))
            {
                if (!ovr.Model.TintHex.StartsWith("#"))
                    diags.Add(new DiagMessage(DiagSeverity.Warning, $"Model.TintHex '{ovr.Model.TintHex}' should start with '#'."));
            }

            // ── AnimOverrides ──
            foreach (var kvp in ovr.AnimOverrides)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                    diags.Add(new DiagMessage(DiagSeverity.Error, "AnimOverride has empty clip name key."));
                foreach (var bkvp in kvp.Value.BoneKeyframes)
                {
                    for (int k = 1; k < bkvp.Value.Count; k++)
                    {
                        if (bkvp.Value[k].Time <= bkvp.Value[k - 1].Time)
                            diags.Add(new DiagMessage(DiagSeverity.Warning,
                                $"AnimOverride '{kvp.Key}' bone '{bkvp.Key}': keyframes not sorted by time at index {k}."));
                    }
                }
            }

            return diags;
        }

        private static void ValidateBoneOverrides(Dictionary<string, BoneOverride> overrides, string context, List<DiagMessage> diags)
        {
            foreach (var kvp in overrides)
            {
                var bo = kvp.Value;
                if (bo.ScaleX == 0f || bo.ScaleY == 0f)
                    diags.Add(new DiagMessage(DiagSeverity.Warning, $"{context}['{kvp.Key}']: scale is zero (bone will be invisible)."));
                if (bo.Alpha < 0f || bo.Alpha > 1f)
                    diags.Add(new DiagMessage(DiagSeverity.Warning, $"{context}['{kvp.Key}']: Alpha ({bo.Alpha}) is outside [0, 1]."));
                if (!string.IsNullOrEmpty(bo.ColorHex) && !bo.ColorHex.StartsWith("#"))
                    diags.Add(new DiagMessage(DiagSeverity.Warning, $"{context}['{kvp.Key}']: ColorHex '{bo.ColorHex}' should start with '#'."));
            }
        }

        private static void ValidateCustomSprites(Dictionary<string, SpriteDef> custom, string context, List<DiagMessage> diags)
        {
            foreach (var kvp in custom)
            {
                if (string.IsNullOrEmpty(kvp.Value.ImagePath))
                    diags.Add(new DiagMessage(DiagSeverity.Warning, $"{context}['{kvp.Key}']: ImagePath is empty."));
                if (kvp.Value.PPU < 0f)
                    diags.Add(new DiagMessage(DiagSeverity.Warning, $"{context}['{kvp.Key}']: PPU ({kvp.Value.PPU}) is negative."));
            }
        }
    }
}
