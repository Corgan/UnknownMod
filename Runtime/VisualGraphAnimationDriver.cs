using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;

namespace UnknownMod.Runtime
{
    [DefaultExecutionOrder(-3)]
    public sealed class VisualGraphAnimationDriver : MonoBehaviour
    {
        public sealed class NodeBinding
        {
            internal readonly CompiledVisualNode Node;
            internal readonly GameObject Owner;
            internal readonly Transform Boundary;
            internal readonly Transform[] Transforms;
            internal readonly Vector3[] Positions;
            internal readonly Quaternion[] Rotations;
            internal readonly Vector3[] Scales;
            internal readonly Transform[] LockedAnchorPath;
            internal readonly Vector3[] LockedPositions;
            internal readonly Quaternion[] LockedRotations;
            internal readonly Vector3[] LockedScales;
            internal readonly SpriteRenderer[] Renderers;
            internal readonly Material[][] SharedMaterials;
            internal readonly Color[] ExternalColors;
            internal readonly Material[] ExternalPrimaryMaterials;
            internal readonly Sprite[] Sprites;
            internal readonly Color[] Colors;
            internal readonly bool[] Enabled;
            internal readonly bool[] FlipX;
            internal readonly bool[] FlipY;
            internal readonly int[] SortingLayers;
            internal readonly int[] SortingOrders;
            internal readonly SpriteDrawMode[] DrawModes;
            internal readonly Vector2[] Sizes;
            internal readonly SpriteMaskInteraction[] MaskInteractions;
            internal readonly SpriteSortPoint[] SpriteSortPoints;
            internal readonly SpriteTileMode[] TileModes;
            internal readonly float[] AdaptiveModeThresholds;
            internal readonly uint[] RenderingLayerMasks;
            internal readonly ShadowCastingMode[] ShadowCastingModes;
            internal readonly bool[] ReceiveShadows;
            internal readonly LightProbeUsage[] LightProbeUsages;
            internal readonly ReflectionProbeUsage[] ReflectionProbeUsages;
            internal readonly Transform[] ProbeAnchors;
            internal readonly bool[] AllowOcclusionWhenDynamic;
            internal readonly MotionVectorGenerationMode[] MotionVectorGenerationModes;
            internal readonly int[] RendererPriorities;
            internal readonly SpriteSkin[] SpriteSkins;
            internal readonly bool[] SpriteSkinEnabled;
            internal readonly bool[] SpriteSkinAutoRebind;
            internal readonly bool[] SpriteSkinAlwaysUpdate;
            internal readonly Transform[] SpriteSkinRootBones;
            internal readonly Transform[][] SpriteSkinBoneTransforms;
            internal readonly object[][] SpriteSkinRootBoneArguments;
            internal readonly object[][] SpriteSkinBoneTransformArguments;
            internal readonly Dictionary<string, AnimationClip> SourceClips;
            internal readonly Dictionary<string, string> ClipMap;
            internal readonly bool Visible;
            internal readonly Vector3 OwnerPosition;
            internal readonly Quaternion OwnerRotation;
            internal readonly Vector3 OwnerScale;
            internal bool HasSample;
            internal readonly HashSet<string> LoggedReasons = new HashSet<string>(StringComparer.Ordinal);

            internal NodeBinding(CompiledVisualNode node, GameObject owner, Transform boundary,
                Transform[] transforms, Vector3[] positions, Quaternion[] rotations, Vector3[] scales,
                Transform[] lockedAnchorPath, Vector3[] lockedPositions, Quaternion[] lockedRotations, Vector3[] lockedScales,
                SpriteRenderer[] renderers, Sprite[] sprites, Color[] colors, bool[] enabled, bool[] flipX, bool[] flipY,
                Material[][] sharedMaterials,
                int[] sortingLayers, int[] sortingOrders, SpriteDrawMode[] drawModes, Vector2[] sizes,
                SpriteMaskInteraction[] maskInteractions, SpriteSortPoint[] spriteSortPoints, SpriteTileMode[] tileModes,
                float[] adaptiveModeThresholds, uint[] renderingLayerMasks, ShadowCastingMode[] shadowCastingModes,
                bool[] receiveShadows, LightProbeUsage[] lightProbeUsages, ReflectionProbeUsage[] reflectionProbeUsages,
                Transform[] probeAnchors, bool[] allowOcclusionWhenDynamic, MotionVectorGenerationMode[] motionVectorGenerationModes,
                int[] rendererPriorities, SpriteSkin[] spriteSkins, bool[] spriteSkinEnabled, bool[] spriteSkinAutoRebind,
                bool[] spriteSkinAlwaysUpdate, Transform[] spriteSkinRootBones, Transform[][] spriteSkinBoneTransforms,
                Dictionary<string, AnimationClip> sourceClips, Dictionary<string, string> clipMap,
                Color[] externalColors, Material[] externalPrimaryMaterials)
            {
                Node = node; Owner = owner; Boundary = boundary; Transforms = transforms;
                Positions = positions; Rotations = rotations; Scales = scales;
                LockedAnchorPath = lockedAnchorPath; LockedPositions = lockedPositions; LockedRotations = lockedRotations; LockedScales = lockedScales;
                Renderers = renderers; SharedMaterials = sharedMaterials; ExternalColors = externalColors; ExternalPrimaryMaterials = externalPrimaryMaterials; Sprites = sprites; Colors = colors; Enabled = enabled; FlipX = flipX; FlipY = flipY;
                SortingLayers = sortingLayers; SortingOrders = sortingOrders; SourceClips = sourceClips; ClipMap = clipMap;
                Visible = node.Overrides.Visible; OwnerPosition = owner.transform.localPosition;
                OwnerRotation = owner.transform.localRotation; OwnerScale = owner.transform.localScale;
                DrawModes = drawModes; Sizes = sizes; MaskInteractions = maskInteractions; SpriteSortPoints = spriteSortPoints;
                TileModes = tileModes; AdaptiveModeThresholds = adaptiveModeThresholds; RenderingLayerMasks = renderingLayerMasks;
                ShadowCastingModes = shadowCastingModes; ReceiveShadows = receiveShadows; LightProbeUsages = lightProbeUsages;
                ReflectionProbeUsages = reflectionProbeUsages; ProbeAnchors = probeAnchors;
                AllowOcclusionWhenDynamic = allowOcclusionWhenDynamic; MotionVectorGenerationModes = motionVectorGenerationModes;
                RendererPriorities = rendererPriorities; SpriteSkins = spriteSkins; SpriteSkinEnabled = spriteSkinEnabled;
                SpriteSkinAutoRebind = spriteSkinAutoRebind; SpriteSkinAlwaysUpdate = spriteSkinAlwaysUpdate;
                SpriteSkinRootBones = spriteSkinRootBones; SpriteSkinBoneTransforms = spriteSkinBoneTransforms;
                if (SpriteSkinRootBoneSetter == null || SpriteSkinBoneTransformsSetter == null)
                    throw new InvalidOperationException("SpriteSkin runtime binding setters are unavailable.");
                SpriteSkinRootBoneArguments = new object[spriteSkins.Length][];
                SpriteSkinBoneTransformArguments = new object[spriteSkins.Length][];
                for (int i = 0; i < spriteSkins.Length; i++)
                {
                    SpriteSkinRootBoneArguments[i] = new object[] { spriteSkinRootBones[i] };
                    SpriteSkinBoneTransformArguments[i] = new object[] { spriteSkinBoneTransforms[i] };
                }
            }
        }

        private static readonly MethodInfo SpriteSkinRootBoneSetter = GetSpriteSkinSetter("rootBone");
        private static readonly MethodInfo SpriteSkinBoneTransformsSetter = GetSpriteSkinSetter("boneTransforms");

        private static MethodInfo GetSpriteSkinSetter(string propertyName)
        {
            PropertyInfo property = typeof(SpriteSkin).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property == null ? null : property.GetSetMethod(true);
        }

        private Animator _hostAnimator;
        private List<NodeBinding> _bindings;
        private readonly List<AnimatorClipInfo> _currentClipInfos = new List<AnimatorClipInfo>();
        private readonly List<AnimatorClipInfo> _nextClipInfos = new List<AnimatorClipInfo>();
        private bool _active;
        private bool _automaticEvaluation = true;
        private bool _shutdown;

        internal void Initialize(Animator hostAnimator, List<NodeBinding> bindings, bool automaticEvaluation = true)
        {
            _hostAnimator = hostAnimator;
            _bindings = bindings;
            _automaticEvaluation = automaticEvaluation;
        }

        internal void Activate()
        {
            if (_shutdown) return;
            gameObject.SetActive(true);
            _active = true;
            EvaluateNow();
        }

        /// <summary>Keeps a layered sort value across the per-frame invariant restore.</summary>
        internal void SetSorting(SpriteRenderer renderer, int layerId, int order)
        {
            if (_bindings == null) return;
            for (int b = 0; b < _bindings.Count; b++)
            {
                var binding = _bindings[b];
                if (binding == null) continue;
                for (int i = 0; i < binding.Renderers.Length; i++)
                    if (binding.Renderers[i] == renderer) { binding.SortingLayers[i] = layerId; binding.SortingOrders[i] = order; }
            }
        }

        public void EvaluateNow()
        {
            if (!_active || _bindings == null || _hostAnimator == null || !_hostAnimator || !gameObject || !gameObject.transform.parent)
            {
                if (_active) Shutdown(false);
                return;
            }
            AnimationPlayback playback;
            bool hasPlayback = AnimationPlaybackResolver.TryResolveActive(_hostAnimator, 0, _currentClipInfos, _nextClipInfos, out playback);
            for (int i = 0; i < _bindings.Count; i++)
            {
                NodeBinding binding = _bindings[i];
                if (binding == null || binding.Owner == null || binding.Boundary == null)
                {
                    Shutdown(false);
                    return;
                }
                EvaluateBinding(binding, hasPlayback, playback);
            }
        }

        private void LateUpdate()
        {
            if (_automaticEvaluation) EvaluateNow();
        }

        public void EvaluateAt(AnimationClip hostClip, float normalizedPhase)
        {
            if (!_active || _bindings == null) return;
            float phase = AnimationPlaybackResolver.ToNormalizedPhase(hostClip, normalizedPhase);
            var playback = new AnimationPlayback(hostClip, phase, false);
            for (int i = 0; i < _bindings.Count; i++)
                EvaluateBinding(_bindings[i], hostClip != null, playback);
        }

        private static void EvaluateBinding(NodeBinding binding, bool hasPlayback, AnimationPlayback playback)
        {
            for (int i = 0; i < binding.Renderers.Length; i++)
            {
                binding.ExternalColors[i] = binding.Renderers[i].color;
                binding.ExternalPrimaryMaterials[i] = binding.Renderers[i].sharedMaterial;
            }
            AnimationClip sourceClip = null;
            float phase = 0f;
            string reason = null;
            if (hasPlayback && playback.Clip != null)
            {
                string sourceName;
                if (binding.ClipMap.TryGetValue(playback.Clip.name, out sourceName))
                {
                    binding.SourceClips.TryGetValue(sourceName, out sourceClip);
                    phase = AnimationPlaybackResolver.ToNormalizedPhase(playback.Clip, playback.NormalizedTime);
                    if (sourceClip == null) reason = "missing-source:" + sourceName;
                }
                else reason = "unmapped-host:" + playback.Clip.name;
            }
            else reason = "no-host-playback";

            if (sourceClip == null)
            {
                ApplyFallback(binding, reason);
                return;
            }

            RestoreBindPose(binding);
            try
            {
                sourceClip.SampleAnimation(binding.Boundary.gameObject, phase * sourceClip.length);
                if (binding.Node.AnimationPolicy.AnchorPolicy == Definitions.VisualAnchorPolicy.LockedAnchor)
                    RestoreLockedAnchor(binding);
                RestoreInvariants(binding, false);
                binding.Owner.SetActive(binding.Visible);
                binding.HasSample = true;
            }
            catch (Exception exception)
            {
                LogOnce(binding, "sample-failure", exception.ToString());
                ApplyFallback(binding, "sample-failure");
            }
        }

        private static void ApplyFallback(NodeBinding binding, string reason)
        {
            switch (binding.Node.AnimationPolicy.Fallback)
            {
                case Definitions.VisualAnimationFallback.StaticPose:
                    RestoreBindPose(binding);
                    RestoreInvariants(binding, true);
                    binding.Owner.SetActive(binding.Visible);
                    break;
                case Definitions.VisualAnimationFallback.SkipNode:
                    RestoreBindPose(binding);
                    RestoreInvariants(binding, true);
                    binding.Owner.SetActive(false);
                    break;
                case Definitions.VisualAnimationFallback.None:
                    if (!binding.HasSample) RestoreBindPose(binding);
                    RestoreInvariants(binding, false);
                    binding.Owner.SetActive(binding.Visible);
                    break;
            }
            LogOnce(binding, reason, null);
        }

        private static void RestoreBindPose(NodeBinding binding)
        {
            for (int i = 0; i < binding.Transforms.Length; i++)
            {
                Transform transform = binding.Transforms[i];
                transform.localPosition = binding.Positions[i];
                transform.localRotation = binding.Rotations[i];
                transform.localScale = binding.Scales[i];
            }
            for (int i = 0; i < binding.Renderers.Length; i++)
            {
                SpriteRenderer renderer = binding.Renderers[i];
                renderer.sharedMaterials = binding.SharedMaterials[i];
                renderer.sprite = binding.Sprites[i];
                renderer.color = binding.Colors[i];
                renderer.enabled = binding.Enabled[i];
                renderer.flipX = binding.FlipX[i];
                renderer.flipY = binding.FlipY[i];
            }
            for (int i = 0; i < binding.SpriteSkins.Length; i++)
            {
                SpriteSkin skin = binding.SpriteSkins[i];
                try
                {
                    SpriteSkinRootBoneSetter.Invoke(skin, binding.SpriteSkinRootBoneArguments[i]);
                }
                catch (Exception exception)
                {
                    LogOnce(binding, "spriteskin-rootbone-restore-failure", exception.ToString());
                }
                try
                {
                    SpriteSkinBoneTransformsSetter.Invoke(skin, binding.SpriteSkinBoneTransformArguments[i]);
                }
                catch (Exception exception)
                {
                    LogOnce(binding, "spriteskin-bonetransforms-restore-failure", exception.ToString());
                }
                skin.autoRebind = binding.SpriteSkinAutoRebind[i];
                skin.alwaysUpdate = binding.SpriteSkinAlwaysUpdate[i];
                skin.enabled = binding.SpriteSkinEnabled[i];
            }
        }

        private static void RestoreLockedAnchor(NodeBinding binding)
        {
            for (int i = 0; i < binding.LockedAnchorPath.Length; i++)
            {
                Transform transform = binding.LockedAnchorPath[i];
                transform.localPosition = binding.LockedPositions[i];
                transform.localRotation = binding.LockedRotations[i];
                transform.localScale = binding.LockedScales[i];
            }
        }

        private static void RestoreInvariants(NodeBinding binding, bool restoreSprites)
        {
            Transform owner = binding.Owner.transform;
            owner.localPosition = binding.OwnerPosition;
            owner.localRotation = binding.OwnerRotation;
            owner.localScale = binding.OwnerScale;
            for (int i = 0; i < binding.Renderers.Length; i++)
            {
                SpriteRenderer renderer = binding.Renderers[i];
                renderer.sharedMaterials = binding.SharedMaterials[i];
                renderer.color = binding.Colors[i];
                renderer.enabled = binding.Enabled[i];
                renderer.flipX = binding.FlipX[i];
                renderer.flipY = binding.FlipY[i];
                renderer.sortingLayerID = binding.SortingLayers[i];
                renderer.sortingOrder = binding.SortingOrders[i];
                renderer.drawMode = binding.DrawModes[i];
                renderer.size = binding.Sizes[i];
                renderer.maskInteraction = binding.MaskInteractions[i];
                renderer.spriteSortPoint = binding.SpriteSortPoints[i];
                renderer.tileMode = binding.TileModes[i];
                renderer.adaptiveModeThreshold = binding.AdaptiveModeThresholds[i];
                renderer.renderingLayerMask = binding.RenderingLayerMasks[i];
                renderer.shadowCastingMode = binding.ShadowCastingModes[i];
                renderer.receiveShadows = binding.ReceiveShadows[i];
                renderer.lightProbeUsage = binding.LightProbeUsages[i];
                renderer.reflectionProbeUsage = binding.ReflectionProbeUsages[i];
                renderer.probeAnchor = binding.ProbeAnchors[i];
                renderer.allowOcclusionWhenDynamic = binding.AllowOcclusionWhenDynamic[i];
                renderer.motionVectorGenerationMode = binding.MotionVectorGenerationModes[i];
                renderer.rendererPriority = binding.RendererPriorities[i];
                if (restoreSprites) renderer.sprite = binding.Sprites[i];
                renderer.sharedMaterial = binding.ExternalPrimaryMaterials[i];
                renderer.color = binding.ExternalColors[i];
            }
            for (int i = 0; i < binding.SpriteSkins.Length; i++)
            {
                SpriteSkin skin = binding.SpriteSkins[i];
                try
                {
                    SpriteSkinRootBoneSetter.Invoke(skin, binding.SpriteSkinRootBoneArguments[i]);
                }
                catch (Exception exception)
                {
                    LogOnce(binding, "spriteskin-rootbone-restore-failure", exception.ToString());
                }
                try
                {
                    SpriteSkinBoneTransformsSetter.Invoke(skin, binding.SpriteSkinBoneTransformArguments[i]);
                }
                catch (Exception exception)
                {
                    LogOnce(binding, "spriteskin-bonetransforms-restore-failure", exception.ToString());
                }
                skin.autoRebind = binding.SpriteSkinAutoRebind[i];
                skin.alwaysUpdate = binding.SpriteSkinAlwaysUpdate[i];
                skin.enabled = binding.SpriteSkinEnabled[i];
            }
        }

        private static void LogOnce(NodeBinding binding, string reason, string detail)
        {
            if (!binding.LoggedReasons.Add(reason)) return;
            if (Plugin.Log != null)
                Plugin.Log.LogWarning("[VisualGraphAnimation] Node '" + binding.Node.NodeId + "' unresolved (" + reason + ")." + (detail == null ? "" : " " + detail));
        }

        internal void Shutdown()
        {
            Shutdown(true);
        }

        internal void ShutdownImmediate()
        {
            if (_shutdown) return;
            _shutdown = true;
            _active = false;
            _bindings = null;
            _hostAnimator = null;
            if (gameObject)
            {
                gameObject.SetActive(false);
                DestroyImmediate(gameObject);
            }
        }

        private void Shutdown(bool destroyObject)
        {
            if (_shutdown) return;
            _shutdown = true;
            _active = false;
            _bindings = null;
            _hostAnimator = null;
            if (destroyObject && gameObject) Destroy(gameObject);
        }
    }
}
