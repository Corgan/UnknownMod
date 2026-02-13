using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// MonoBehaviour attached to instantiated NPC animated models at runtime.
    /// Handles ONLY per-frame work in LateUpdate (AFTER the Animator writes
    /// transforms each frame): bone transform overrides, scale/flip,
    /// grafted sprite re-stamping, and animation keyframe interpolation.
    /// All one-time modifications are baked into the prefab by NpcPrefabBuilder.
    /// </summary>
    public class NpcSpriteOverride : MonoBehaviour
    {
        // ── Configuration (set once by the patch) ────────────────────
        private SpriteOverrideDef _def;
        private Animator _anim;

        // ── Cached bone references ───────────────────────────────────
        private Dictionary<string, Transform> _boneMap = new();
        private Dictionary<string, SpriteRenderer> _srMap = new();

        // ── Rest pose (recorded at init, before any overrides) ───────
        private Dictionary<string, Vector3> _restPos = new();
        private Dictionary<string, float> _restRot = new();
        private Dictionary<string, Vector3> _restScale = new();

        // ── Cached base SR state (to avoid per-frame drift) ──────────
        private Dictionary<string, int> _baseSortingOrder = new();
        private Dictionary<string, bool> _baseFlipX = new();
        private Dictionary<string, bool> _baseFlipY = new();
        private Dictionary<string, float> _baseAlpha = new();

        // ── Grafted sprites (must be re-applied every frame after Animator) ──
        private Dictionary<string, Sprite> _graftedSprites = new();

        // ── Bones not driven by the Animator (use SET, not additive) ──────
        private HashSet<string> _nonAnimatorBones = new();

        // ── Track current anim state for keyframe interpolation ──────
        private float _currentNormTime;

    // ── Cached clip name → hash for runtime matching ──────────
        private Dictionary<string, int> _clipNameHash = new();

        /// <summary>
        /// Initialize the component with override data. Called once from the
        /// NPCItem.Init postfix patch after the animated model is instantiated.
        /// </summary>
        public void Init(SpriteOverrideDef def)
        {
            _def = def;
            _anim = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();

            // Build bone lookup maps
            _boneMap.Clear();
            _srMap.Clear();
            _restPos.Clear();
            _restRot.Clear();
            _restScale.Clear();
            _baseSortingOrder.Clear();
            _baseFlipX.Clear();
            _baseFlipY.Clear();
            _baseAlpha.Clear();
            _graftedSprites.Clear();
            _nonAnimatorBones.Clear();
            _clipNameHash.Clear();
            CacheBones(transform);

            // Build clip name → hash mapping for reliable runtime clip matching
            if (_anim != null && _anim.runtimeAnimatorController != null)
            {
                var clips = _anim.runtimeAnimatorController.animationClips;
                if (clips != null)
                {
                    foreach (var clip in clips)
                    {
                        if (clip != null && !string.IsNullOrEmpty(clip.name))
                            _clipNameHash[clip.name] = Animator.StringToHash(clip.name);
                    }
                    Plugin.Log.LogInfo($"[NpcSpriteOverride] Cached {_clipNameHash.Count} clip hashes: [{string.Join(", ", _clipNameHash.Keys)}]");
                }
            }

            Plugin.Log.LogInfo($"[NpcSpriteOverride] Init on '{gameObject.name}': " +
                $"{_boneMap.Count} bones, {_srMap.Count} SRs, " +
                $"{_def.Bones.Count} bone overrides, anim={(_anim != null)}");

            // Identify non-Animator-driven bones (use SET, not additive in LateUpdate)
            foreach (var boneName in _def.AddedBones.Keys)
                _nonAnimatorBones.Add(boneName);
            foreach (var boneName in _def.AddedSprites.Keys)
                _nonAnimatorBones.Add(boneName);

            // Snapshot grafted sprites from the already-correct prefab clone.
            // The Animator overwrites sr.sprite each frame, so we re-stamp in LateUpdate.
            foreach (var kvp in _def.Bones)
            {
                if (string.IsNullOrEmpty(kvp.Value.SpriteFrom)) continue;
                if (_def.RemovedBones.Contains(kvp.Key)) continue;
                if (_srMap.TryGetValue(kvp.Key, out var sr) && sr.sprite != null)
                    _graftedSprites[kvp.Key] = sr.sprite;
            }
            foreach (var kvp in _def.CustomSprites)
            {
                if (_def.RemovedBones.Contains(kvp.Key)) continue;
                if (_srMap.TryGetValue(kvp.Key, out var sr) && sr.sprite != null)
                    _graftedSprites[kvp.Key] = sr.sprite;
            }

            Plugin.Log.LogInfo($"[NpcSpriteOverride] {_nonAnimatorBones.Count} non-Animator bones, " +
                $"{_graftedSprites.Count} grafted sprites to re-stamp");
        }

        /// <summary>
        /// Walk the transform hierarchy and cache all bones + SpriteRenderers.
        /// Also record rest pose from the original animation state.
        /// </summary>
        private void CacheBones(Transform root)
        {
            BoneHierarchyUtils.CollectBones(root, _boneMap, _srMap);

            // Record rest pose and base SR state from the populated maps
            foreach (var kvp in _boneMap)
            {
                _restPos[kvp.Key] = kvp.Value.localPosition;
                _restRot[kvp.Key] = kvp.Value.localEulerAngles.z;
                _restScale[kvp.Key] = kvp.Value.localScale;
            }
            foreach (var kvp in _srMap)
            {
                _baseSortingOrder[kvp.Key] = kvp.Value.sortingOrder;
                _baseFlipX[kvp.Key] = kvp.Value.flipX;
                _baseFlipY[kvp.Key] = kvp.Value.flipY;
                _baseAlpha[kvp.Key] = kvp.Value.color.a;
            }
        }

        /// <summary>
        /// Called every frame AFTER the Animator updates.
        /// Re-applies transform overrides so they persist through animations.
        /// </summary>
        private void LateUpdate()
        {
            if (_def == null) return;

            // Track current animation state for keyframe interpolation
            if (_anim != null && _anim.enabled)
            {
                var stateInfo = _anim.GetCurrentAnimatorStateInfo(0);
                _currentNormTime = stateInfo.normalizedTime % 1f;
            }

            // 1. Global model transform
            ApplyGlobalOverrides();

            // 2. Re-apply grafted sprites (Animator overwrites sr.sprite each frame)
            ApplyGraftedSprites();

            // 3. Per-bone transform overrides (additive for existing, SET for added — re-applied every frame)
            ApplyBoneTransformOverrides();

            // 4. Per-bone animation keyframe overrides (interpolated)
            ApplyAnimationKeyframes();
        }

        /// <summary>Apply model-wide scale, offset, flip.</summary>
        private void ApplyGlobalOverrides()
        {
            Vector3 s = transform.localScale;

            // Scale — Animator may reset root scale each frame, so we always
            // re-derive the correct value from the Animator's base (abs value)
            // and multiply by our ScaleMultiplier.
            if (_def.ScaleMultiplier != 1f)
            {
                float baseAbs = Mathf.Max(Mathf.Abs(s.x), 0.001f);
                // The Animator sets scale each frame. We assume the Animator's
                // intended absolute value is what we see, and we multiply it.
                // To avoid compounding, we store the rest scale and use that.
                float restAbs = Mathf.Max(Mathf.Abs(_restScale.TryGetValue(transform.name, out var rs) ? rs.x : s.x), 0.001f);
                float target = restAbs * _def.ScaleMultiplier;
                float signX = _def.FlipX ? -1f : Mathf.Sign(s.x);
                float signY = _def.FlipY ? -1f : Mathf.Sign(s.y);
                transform.localScale = new Vector3(
                    signX * target,
                    signY * target,
                    s.z);
            }
            else if (_def.FlipX || _def.FlipY)
            {
                if (_def.FlipX) s.x = -Mathf.Abs(s.x);
                if (_def.FlipY) s.y = -Mathf.Abs(s.y);
                transform.localScale = s;
            }
        }

        /// <summary>Re-apply grafted sprites that the Animator overwrites each frame.</summary>
        private void ApplyGraftedSprites()
        {
            foreach (var kvp in _graftedSprites)
            {
                if (_srMap.TryGetValue(kvp.Key, out var sr) && sr != null)
                    sr.sprite = kvp.Value;
            }
        }

        /// <summary>Apply static per-bone transform overrides after the Animator.</summary>
        private void ApplyBoneTransformOverrides()
        {
            foreach (var kvp in _def.Bones)
            {
                string boneName = kvp.Key;
                BoneOverride bo = kvp.Value;
                if (!_boneMap.TryGetValue(boneName, out var bone) || bone == null) continue;

                // Bones not driven by the Animator use SET to avoid per-frame drift.
                // This includes: added sprites, added rig bones, and grafted branch bones.
                bool isAdded = _nonAnimatorBones.Contains(boneName);

                // Position
                if (isAdded)
                {
                    bone.localPosition = new Vector3(bo.PosX, bo.PosY, 0f);
                }
                else if (bo.PosX != 0f || bo.PosY != 0f)
                {
                    Vector3 pos = bone.localPosition;
                    pos.x += bo.PosX;
                    pos.y += bo.PosY;
                    bone.localPosition = pos;
                }

                // Rotation
                if (isAdded)
                {
                    bone.localEulerAngles = new Vector3(0, 0, bo.Rotation);
                }
                else if (bo.Rotation != 0f)
                {
                    Vector3 euler = bone.localEulerAngles;
                    euler.z += bo.Rotation;
                    bone.localEulerAngles = euler;
                }

                // Scale
                if (isAdded)
                {
                    bone.localScale = new Vector3(bo.ScaleX, bo.ScaleY, 1f);
                }
                else if (bo.ScaleX != 1f || bo.ScaleY != 1f)
                {
                    Vector3 scale = bone.localScale;
                    scale.x *= bo.ScaleX;
                    scale.y *= bo.ScaleY;
                    bone.localScale = scale;
                }

                // Visibility / flip / alpha / color (on SpriteRenderer)
                // Uses cached base values to avoid per-frame drift
                if (_srMap.TryGetValue(boneName, out var sr) && sr != null)
                {
                    if (!bo.Visible)
                        sr.enabled = false;

                    // Flip: set based on cached base state XOR override
                    if (bo.FlipX)
                        sr.flipX = !(_baseFlipX.TryGetValue(boneName, out var bfx) ? bfx : false);
                    if (bo.FlipY)
                        sr.flipY = !(_baseFlipY.TryGetValue(boneName, out var bfy) ? bfy : false);

                    // Sorting: set from cached base + offset (not additive per-frame)
                    if (bo.SortingOffset != 0)
                        sr.sortingOrder = (_baseSortingOrder.TryGetValue(boneName, out var bso) ? bso : sr.sortingOrder) + bo.SortingOffset;

                    // Color/alpha applied only if specified
                    if (!string.IsNullOrEmpty(bo.ColorHex))
                    {
                        if (ColorUtility.TryParseHtmlString(bo.ColorHex, out var col))
                            sr.color = col;
                    }
                    if (bo.Alpha < 1f)
                    {
                        Color c = sr.color;
                        float baseA = _baseAlpha.TryGetValue(boneName, out var ba) ? ba : c.a;
                        c.a = baseA * Mathf.Clamp01(bo.Alpha);
                        sr.color = c;
                    }
                }
            }
        }

        /// <summary>Apply animation keyframe overrides (interpolated by current anim time).</summary>
        private void ApplyAnimationKeyframes()
        {
            if (_def.AnimOverrides == null || _def.AnimOverrides.Count == 0) return;

            // Identify the currently playing clip by comparing state hash
            string currentClip = null;
            if (_anim != null && _anim.enabled)
            {
                var stateInfo = _anim.GetCurrentAnimatorStateInfo(0);

                // Try matching by clip name hash (state shortNameHash == Animator.StringToHash(clipName))
                foreach (var kvp in _def.AnimOverrides)
                {
                    if (_clipNameHash.TryGetValue(kvp.Key, out int hash) &&
                        stateInfo.shortNameHash == hash)
                    {
                        currentClip = kvp.Key;
                        break;
                    }
                }

                // Fallback: try IsName (works if state and clip share the same name)
                if (currentClip == null)
                {
                    foreach (var clipName in _def.AnimOverrides.Keys)
                    {
                        if (stateInfo.IsName(clipName))
                        {
                            currentClip = clipName;
                            break;
                        }
                    }
                }

                if (currentClip == null && _def.AnimOverrides.Count > 0)
                {
                    Plugin.Log.LogWarning($"[NpcSpriteOverride] No clip match! stateHash={stateInfo.shortNameHash}, " +
                        $"overrideClips=[{string.Join(", ", _def.AnimOverrides.Keys)}]");
                }
            }

            if (currentClip == null) return; // Don't apply if we can't identify the clip

            if (!_def.AnimOverrides.TryGetValue(currentClip, out var animOvr)) return;
            if (animOvr.BoneKeyframes == null || animOvr.BoneKeyframes.Count == 0) return;

            foreach (var boneKvp in animOvr.BoneKeyframes)
            {
                string boneName = boneKvp.Key;
                var keyframes = boneKvp.Value;
                if (keyframes == null || keyframes.Count == 0) continue;
                if (!_boneMap.TryGetValue(boneName, out var bone)) continue;

                // Interpolate keyframes at current normalized time
                var interpolated = InterpolateKeyframes(keyframes, _currentNormTime);
                if (interpolated == null) continue;

                // SET mode — animation keyframes define the pose, not offsets
                bone.localPosition = new Vector3(interpolated.PosX, interpolated.PosY, 0f);
                bone.localEulerAngles = new Vector3(0, 0, interpolated.Rotation);
                bone.localScale = new Vector3(interpolated.ScaleX, interpolated.ScaleY, 1f);
            }
        }

        /// <summary>
        /// Linearly interpolate between the two nearest keyframes at given time.
        /// Single keyframe: constant hold (the keyframe IS the pose).
        /// Multiple keyframes: lerps between adjacent keyframes; clamps outside range.
        /// All values are absolute — they SET the bone transform, not offset it.
        /// </summary>
        internal static BoneKeyframe InterpolateKeyframes(List<BoneKeyframe> keyframes, float normTime)
        {
            if (keyframes.Count == 0) return null;

            if (keyframes.Count == 1)
            {
                // Single keyframe → constant hold (SET mode: this IS the pose)
                return keyframes[0];
            }

            // Keyframes should be sorted by time
            BoneKeyframe before = keyframes[0], after = keyframes[keyframes.Count - 1];

            for (int i = 0; i < keyframes.Count - 1; i++)
            {
                if (keyframes[i].Time <= normTime && keyframes[i + 1].Time >= normTime)
                {
                    before = keyframes[i];
                    after = keyframes[i + 1];
                    break;
                }
            }

            if (Mathf.Abs(after.Time - before.Time) < 0.0001f)
                return before;

            float t = (normTime - before.Time) / (after.Time - before.Time);
            return new BoneKeyframe
            {
                Time = normTime,
                PosX = Mathf.Lerp(before.PosX, after.PosX, t),
                PosY = Mathf.Lerp(before.PosY, after.PosY, t),
                Rotation = Mathf.Lerp(before.Rotation, after.Rotation, t),
                ScaleX = Mathf.Lerp(before.ScaleX, after.ScaleX, t),
                ScaleY = Mathf.Lerp(before.ScaleY, after.ScaleY, t),
            };
        }

        /// <summary>Get the cached bone Transform map.</summary>
        public Dictionary<string, Transform> GetBoneMap() => _boneMap;

        /// <summary>Get the cached SpriteRenderer map.</summary>
        public Dictionary<string, SpriteRenderer> GetSrMap() => _srMap;
    }
}
