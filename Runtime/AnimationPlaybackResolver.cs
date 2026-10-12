using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnknownMod.Runtime
{
    public readonly struct AnimationPlayback
    {
        public AnimationClip Clip { get; }
        public float NormalizedTime { get; }
        public bool IsTransition { get; }

        public AnimationPlayback(AnimationClip clip, float normalizedTime, bool isTransition)
        {
            Clip = clip;
            NormalizedTime = normalizedTime;
            IsTransition = isTransition;
        }
    }

    public enum AnimationClipSelectionReason
    {
        None,
        Exact,
        Semantic,
        IdleFallback,
        Ambiguous
    }

    public static class AnimationPlaybackResolver
    {
        private static readonly string[] KnownSuffixes =
        {
            "hardmovement", "movement", "idle", "attack", "cast", "hit", "death", "skill", "stun"
        };

        /// <summary>
        /// Resolves the highest-weight active clip. If a transition has no usable next clip,
        /// this deliberately falls back to the current clip with IsTransition set to false.
        /// </summary>
        public static bool TryResolveActive(Animator animator, int layer, out AnimationPlayback playback)
        {
            playback = default(AnimationPlayback);
            try
            {
                if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized
                    || layer < 0 || layer >= animator.layerCount)
                    return false;

                if (animator.IsInTransition(layer))
                {
                    AnimationClip nextClip = FindHighestWeightClip(animator.GetNextAnimatorClipInfo(layer));
                    if (nextClip != null)
                    {
                        playback = new AnimationPlayback(
                            nextClip,
                            animator.GetNextAnimatorStateInfo(layer).normalizedTime,
                            true);
                        return true;
                    }
                }

                AnimationClip currentClip = FindHighestWeightClip(animator.GetCurrentAnimatorClipInfo(layer));
                if (currentClip == null)
                    return false;

                playback = new AnimationPlayback(
                    currentClip,
                    animator.GetCurrentAnimatorStateInfo(layer).normalizedTime,
                    false);
                return true;
            }
            catch
            {
                playback = default(AnimationPlayback);
                return false;
            }
        }

        public static bool TryResolveActive(Animator animator, int layer, List<AnimatorClipInfo> currentClipInfos, List<AnimatorClipInfo> nextClipInfos, out AnimationPlayback playback)
        {
            playback = default(AnimationPlayback);
            if (currentClipInfos == null || nextClipInfos == null)
                return TryResolveActive(animator, layer, out playback);

            currentClipInfos.Clear();
            nextClipInfos.Clear();
            try
            {
                if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized
                    || layer < 0 || layer >= animator.layerCount)
                    return false;

                if (animator.IsInTransition(layer))
                {
                    animator.GetNextAnimatorClipInfo(layer, nextClipInfos);
                    AnimationClip nextClip = FindHighestWeightClip(nextClipInfos);
                    if (nextClip != null)
                    {
                        playback = new AnimationPlayback(
                            nextClip,
                            animator.GetNextAnimatorStateInfo(layer).normalizedTime,
                            true);
                        return true;
                    }
                }

                animator.GetCurrentAnimatorClipInfo(layer, currentClipInfos);
                AnimationClip currentClip = FindHighestWeightClip(currentClipInfos);
                if (currentClip == null)
                    return false;

                playback = new AnimationPlayback(
                    currentClip,
                    animator.GetCurrentAnimatorStateInfo(layer).normalizedTime,
                    false);
                return true;
            }
            catch
            {
                playback = default(AnimationPlayback);
                return false;
            }
            finally
            {
                currentClipInfos.Clear();
                nextClipInfos.Clear();
            }
        }

        /// <summary>
        /// Converts raw Animator state time into a phase in the clip's [0, 1] range.
        /// A null clip has no looping metadata, so valid input is conservatively clamped.
        /// </summary>
        public static float ToNormalizedPhase(AnimationClip clip, float normalizedTime)
        {
            if (float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime))
                return 0f;

            return clip != null && clip.isLooping
                ? Mathf.Repeat(normalizedTime, 1f)
                : Mathf.Clamp01(normalizedTime);
        }

        public static float ToSampleTime(AnimationClip clip, float normalizedTime)
        {
            if (clip == null || clip.length <= 0f)
                return 0f;

            return ToNormalizedPhase(clip, normalizedTime) * clip.length;
        }

        /// <summary>
        /// Selects an exact match, then a uniquely recognized action suffix. Exact or semantic
        /// ambiguity fails immediately; idle is used only when no semantic match is recognized.
        /// Recognized suffixes require a start, underscore, digit, or lower-to-TitleCase boundary.
        /// </summary>
        public static bool TrySelectCompatibleClip(
            IEnumerable<AnimationClip> clips,
            string preferredClipName,
            out AnimationClip clip,
            out AnimationClipSelectionReason reason)
        {
            clip = null;
            reason = AnimationClipSelectionReason.None;
            if (clips == null)
                return false;

            var catalog = new List<AnimationClip>();
            foreach (AnimationClip candidate in clips)
            {
                if (candidate != null && !ContainsReference(catalog, candidate))
                    catalog.Add(candidate);
            }

            if (catalog.Count == 0)
                return false;

            AnimationClip exact = null;
            int exactCount = 0;
            if (!string.IsNullOrEmpty(preferredClipName))
            {
                foreach (AnimationClip candidate in catalog)
                {
                    if (string.Equals(candidate.name, preferredClipName, StringComparison.OrdinalIgnoreCase))
                    {
                        exact = candidate;
                        exactCount++;
                    }
                }
            }

            if (exactCount == 1)
            {
                clip = exact;
                reason = AnimationClipSelectionReason.Exact;
                return true;
            }

            if (exactCount > 1)
            {
                reason = AnimationClipSelectionReason.Ambiguous;
                return false;
            }

            string preferredSuffix = ExtractActionSuffix(preferredClipName);
            if (preferredSuffix != null)
            {
                AnimationClip semantic = null;
                int semanticCount = 0;
                foreach (AnimationClip candidate in catalog)
                {
                    if (string.Equals(ExtractActionSuffix(candidate.name), preferredSuffix,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        semantic = candidate;
                        semanticCount++;
                    }
                }

                if (semanticCount == 1)
                {
                    clip = semantic;
                    reason = AnimationClipSelectionReason.Semantic;
                    return true;
                }

                if (semanticCount > 1)
                {
                    reason = AnimationClipSelectionReason.Ambiguous;
                    return false;
                }
            }

            AnimationClip idle = null;
            int idleCount = 0;
            foreach (AnimationClip candidate in catalog)
            {
                if (string.Equals(ExtractActionSuffix(candidate.name), "idle",
                    StringComparison.OrdinalIgnoreCase))
                {
                    idle = candidate;
                    idleCount++;
                }
            }

            if (idleCount == 1)
            {
                clip = idle;
                reason = AnimationClipSelectionReason.IdleFallback;
                return true;
            }

            if (idleCount > 1)
                reason = AnimationClipSelectionReason.Ambiguous;
            return false;
        }

        /// <summary>
        /// Samples a clip and persistently disables the supplied Animator so it cannot
        /// immediately overwrite the sampled pose.
        /// </summary>
        public static bool Sample(GameObject root, Animator animator, AnimationClip clip, float normalizedTime)
        {
            if (root == null || clip == null)
                return false;

            try
            {
                if (animator != null && animator.enabled)
                    animator.enabled = false;
                clip.SampleAnimation(root, ToSampleTime(clip, normalizedTime));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static AnimationClip FindHighestWeightClip(AnimatorClipInfo[] clipInfos)
        {
            AnimationClip best = null;
            float bestWeight = float.NegativeInfinity;
            if (clipInfos == null)
                return null;

            foreach (AnimatorClipInfo clipInfo in clipInfos)
            {
                if (clipInfo.clip != null && clipInfo.weight > bestWeight)
                {
                    best = clipInfo.clip;
                    bestWeight = clipInfo.weight;
                }
            }
            return best;
        }
        private static AnimationClip FindHighestWeightClip(List<AnimatorClipInfo> clipInfos)
        {
            AnimationClip best = null;
            float bestWeight = float.NegativeInfinity;
            for (int i = 0; i < clipInfos.Count; i++)
            {
                AnimatorClipInfo clipInfo = clipInfos[i];
                if (clipInfo.clip != null && clipInfo.weight > bestWeight)
                {
                    best = clipInfo.clip;
                    bestWeight = clipInfo.weight;
                }
            }
            return best;
        }

        private static bool ContainsReference(List<AnimationClip> clips, AnimationClip candidate)
        {
            foreach (AnimationClip clip in clips)
            {
                if (clip == candidate)
                    return true;
            }
            return false;
        }

        private static string ExtractActionSuffix(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return null;

            string lowerName = clipName.ToLowerInvariant();
            foreach (string suffix in KnownSuffixes)
            {
                if (!lowerName.EndsWith(suffix, StringComparison.Ordinal))
                    continue;

                int boundary = clipName.Length - suffix.Length;
                if (boundary == 0 || clipName[boundary - 1] == '_'
                    || char.IsDigit(clipName[boundary - 1])
                    || (char.IsLower(clipName[boundary - 1])
                        && char.IsUpper(clipName[boundary])
                        && char.IsLower(clipName[boundary + 1])))
                    return suffix;
            }
            return null;
        }
    }
}