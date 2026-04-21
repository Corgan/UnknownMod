using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// A self-contained grafted sprite branch. Owns a cloned prefab from the
    /// source NPC with its own Animator + SpriteSkin + bone hierarchy.
    /// Parented under the host's target bone. Uses AnimatorStateMirror to
    /// sync animations with the host NPC.
    ///
    /// Key benefit: sprites stay on their original skeleton with original
    /// bind poses — no SetBindPoses mutation, no RecomputeBindPoses, no
    /// curve import, no bone surgery.
    ///
    /// Lifecycle:
    ///   1. Spawned by CharacterOverrideDriver.Init() for each GraftDef
    ///   2. Per-frame: AnimatorStateMirror syncs state → Animator evaluates →
    ///      LateUpdate applies GraftDef.BoneOverrides + AnimOverrides
    ///   3. Destroyed when the host NPC is destroyed (child of host hierarchy)
    /// </summary>
    [DefaultExecutionOrder(-1)] // After CharacterOverrideDriver (-2), before SpriteSkin (0)
    public class GraftPuppet : MonoBehaviour
    {
        private GraftDef _graftDef;
        private Animator _puppetAnimator;
        private Animator _hostAnimator;
        private AnimatorStateMirror _mirror;
        private Transform _sourceBoneTransform;  // The grafted bone in the clone
        private Transform _hostTargetBone;        // The host bone we're replacing
        private CharacterOverrideDriver _hostDriver;  // Host driver for sorting reference

        // Bone maps for this puppet's hierarchy
        private Dictionary<string, Transform> _boneMap = new();
        private Dictionary<string, SpriteRenderer> _srMap = new();
        private Dictionary<string, int> _baseSortingOrder = new();
        private Dictionary<string, bool> _baseFlipX = new();
        private Dictionary<string, bool> _baseFlipY = new();
        private Dictionary<string, float> _baseAlpha = new();
        private Dictionary<string, Transform> _skinRootMap = new();

        // Controlling bone names that use SET mode (not additive) for BoneOverrides.
        // Built once at Create from _skinRootMap values.
        private HashSet<string> _graftSetModeBones = new();

        // SkinRoot compounding prevention (mirrors CharacterOverrideDriver)
        private Dictionary<Transform, (Vector3 pos, float rot, Vector3 scale)> _skinRootInitial = new();
        private Dictionary<Transform, (Vector3 pos, float rot, Vector3 scale)> _skinRootPreOffset = new();

        // Clip name → hash for keyframe matching
        private Dictionary<string, int> _clipNameHash = new();
        private float _currentNormTime;

        // Cached custom sprites for per-frame re-stamping (Animator overwrites sr.sprite)
        private Dictionary<string, Sprite> _customSpriteCache = new();

        // Ancestor bones above the grafted sprite bone. These are frozen to rest
        // pose after the Animator evaluates so that source body motion doesn't leak
        // into the grafted bone's world transform (only the graft's local animation
        // and the host anchor determine its final position).
        private List<(Transform bone, Vector3 pos, Quaternion rot, Vector3 scale)> _ancestorRestPoses = new();

        // List of visible sprite names (only the grafted sprite + siblings are shown)
        private HashSet<string> _visibleSprites = new();



        /// <summary>Get the puppet's bone map (for editor handle collection).</summary>
        public Dictionary<string, Transform> BoneMap => _boneMap;
        /// <summary>Get the puppet's SR map (for editor handle collection).</summary>
        public Dictionary<string, SpriteRenderer> SrMap => _srMap;
        /// <summary>Get the GraftDef this puppet was created from.</summary>
        public GraftDef Def => _graftDef;
        /// <summary>Get the set of visible sprite names for this puppet.</summary>
        public HashSet<string> VisibleSpriteNames => _visibleSprites;

        /// <summary>
        /// Create a GraftPuppet for a single GraftDef.
        /// </summary>
        /// <param name="graftDef">The graft definition.</param>
        /// <param name="hostTargetBone">The host bone to parent under.</param>
        /// <param name="hostAnimator">The host NPC's Animator for state mirroring.</param>
        /// <returns>The puppet GameObject, or null on failure.</returns>
        public static GraftPuppet Create(GraftDef graftDef, Transform hostTargetBone, Animator hostAnimator)
        {
            if (graftDef == null || string.IsNullOrEmpty(graftDef.Source)) return null;

            // Parse source: "npc_id/bone_name"
            string sourceNpc, sourceBone;
            int slash = graftDef.Source.IndexOf('/');
            if (slash >= 0)
            {
                sourceNpc = graftDef.Source.Substring(0, slash);
                sourceBone = graftDef.Source.Substring(slash + 1);
            }
            else
            {
                sourceNpc = graftDef.Source;
                sourceBone = graftDef.TargetBone; // default: same bone name
            }

            // Resolve source prefab (NPC or skin)
            GameObject sourcePrefab = ResolveSourcePrefab(sourceNpc);
            if (sourcePrefab == null)
            {
                Plugin.Log.LogWarning($"[GraftPuppet] No prefab found for source '{sourceNpc}'");
                return null;
            }

            // Clone the entire source prefab
            var clone = Object.Instantiate(sourcePrefab);
            clone.name = $"GraftPuppet~{graftDef.TargetBone}";
            clone.SetActive(true);

            // Find the source sprite bone in the clone
            Transform sourceSpriteT = BoneHierarchyUtils.FindRecursive(clone.transform, sourceBone);
            if (sourceSpriteT == null)
            {
                Plugin.Log.LogWarning($"[GraftPuppet] Source bone '{sourceBone}' not found in '{sourceNpc}'");
                Object.Destroy(clone);
                return null;
            }

            // Determine which sprites to show: the target sprite + siblings sharing the same bones
            var visibleSprites = new HashSet<string> { sourceBone };
            var sourceSkin = sourceSpriteT.GetComponent<SpriteSkin>();
            if (sourceSkin != null && sourceSkin.boneTransforms != null)
            {
                var validBones = sourceSkin.boneTransforms.Where(b => b != null).ToArray();
                if (validBones.Length > 0)
                {
                    var branchRoot = BoneHierarchyUtils.FindLCA(validBones);
                    if (branchRoot == null || branchRoot == clone.transform)
                        branchRoot = validBones[0];

                    // Find sibling sprites whose bones are all within the branch
                    foreach (var otherSkin in clone.GetComponentsInChildren<SpriteSkin>(true))
                    {
                        if (otherSkin.transform == sourceSpriteT) continue;
                        var otherSR = otherSkin.GetComponent<SpriteRenderer>();
                        if (otherSR == null || otherSR.sprite == null) continue;
                        if (otherSkin.boneTransforms == null) continue;

                        bool allInSubtree = otherSkin.boneTransforms
                            .Where(b => b != null)
                            .All(b => b == branchRoot || BoneHierarchyUtils.IsDescendantOf(b, branchRoot));

                        if (allInSubtree)
                            visibleSprites.Add(otherSkin.name);
                    }
                }
            }

            // Hide all SpriteRenderers EXCEPT the grafted ones
            foreach (var sr in clone.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!visibleSprites.Contains(sr.name))
                    sr.enabled = false;
            }

            // Remove all colliders from the clone — they would intercept mouse
            // events meant for the host's CharacterGOItem (hover, click, etc.)
            foreach (var col in clone.GetComponentsInChildren<Collider2D>(true))
                Object.Destroy(col);

            // Also remove CharacterGOItem if cloned from a prefab that had one
            foreach (var goItem in clone.GetComponentsInChildren<CharacterGOItem>(true))
                Object.Destroy(goItem);

            // Parent the clone under the host target bone
            clone.transform.SetParent(hostTargetBone, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            // Add GraftPuppet component
            var puppet = clone.AddComponent<GraftPuppet>();
            puppet._graftDef = graftDef;
            puppet._hostAnimator = hostAnimator;
            puppet._visibleSprites = visibleSprites;
            puppet._sourceBoneTransform = sourceSpriteT;
            puppet._hostTargetBone = hostTargetBone;
            puppet._hostDriver = hostAnimator != null
                ? hostAnimator.GetComponentInParent<CharacterOverrideDriver>()
                : null;

            // Cache puppet Animator and set up state mirroring
            puppet._puppetAnimator = clone.GetComponent<Animator>()
                ?? clone.GetComponentInChildren<Animator>();

            // Prevent culling when the clone is off-screen (bones must always evaluate)
            if (puppet._puppetAnimator != null)
                puppet._puppetAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (puppet._puppetAnimator != null && hostAnimator != null)
            {
                puppet._mirror = clone.AddComponent<AnimatorStateMirror>();
                puppet._mirror.Init(hostAnimator, puppet._puppetAnimator);
            }

            // Build bone maps
            BoneHierarchyUtils.CollectBones(clone.transform, puppet._boneMap, puppet._srMap);

            // Record base SR state
            foreach (var kvp in puppet._srMap)
            {
                puppet._baseSortingOrder[kvp.Key] = kvp.Value.sortingOrder;
                puppet._baseFlipX[kvp.Key] = kvp.Value.flipX;
                puppet._baseFlipY[kvp.Key] = kvp.Value.flipY;
                puppet._baseAlpha[kvp.Key] = kvp.Value.color.a;
            }

            // Map SpriteSkin sprites to their controlling rig bone
            foreach (var kvp in puppet._boneMap)
            {
                var skin = kvp.Value.GetComponent<SpriteSkin>();
                if (skin == null) continue;
                var ctrlBone = BoneHierarchyUtils.GetControllingBone(skin);
                if (ctrlBone != kvp.Value)
                {
                    puppet._skinRootMap[kvp.Key] = ctrlBone;
                    puppet._graftSetModeBones.Add(ctrlBone.name);
                    if (!puppet._skinRootInitial.ContainsKey(ctrlBone))
                        puppet._skinRootInitial[ctrlBone] = (ctrlBone.localPosition, ctrlBone.localEulerAngles.z, ctrlBone.localScale);
                }
            }

            // Build clip name → hash mapping
            if (puppet._puppetAnimator?.runtimeAnimatorController != null)
            {
                foreach (var clip in puppet._puppetAnimator.runtimeAnimatorController.animationClips)
                {
                    if (clip != null && !string.IsNullOrEmpty(clip.name))
                        puppet._clipNameHash[clip.name] = Animator.StringToHash(clip.name);
                }
            }

            // Cache rest poses for ancestor bones (source sprite bone → clone root).
            // These get frozen after Animator evaluation to isolate the graft from
            // source body motion.
            puppet.CacheAncestorRestPoses();

            Plugin.Log.LogInfo($"[GraftPuppet] Created for '{graftDef.TargetBone}' <- '{graftDef.Source}': " +
                $"{puppet._boneMap.Count} bones, {visibleSprites.Count} visible sprites");

            // Sort animation keyframes (required by InterpolateKeyframes' linear scan)
            if (graftDef.AnimOverrides != null)
            {
                foreach (var animOvr in graftDef.AnimOverrides.Values)
                    foreach (var kfList in animOvr.BoneKeyframes.Values)
                        kfList.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            // Apply and cache graft-scoped custom sprites
            puppet.ApplyCustomSprites();

            // Pre-create pivot sprites for bones that have pivot overrides but
            // no custom sprite (mirrors CharacterOverrideDriver.CachePivotOnlySprites)
            puppet.CachePivotOnlySprites();

            return puppet;
        }

        /// <summary>Resolve source prefab from NPC ID or skin ID.</summary>
        private static GameObject ResolveSourcePrefab(string sourceId)
        {
            var npcData = DataHelper.GetExistingNPC(sourceId);
            if (npcData?.GameObjectAnimated != null)
                return npcData.GameObjectAnimated;

            var skinData = DataHelper.GetSkin(sourceId);
            if (skinData?.SkinGo != null)
                return skinData.SkinGo;

            return null;
        }

        private void Update()
        {
            if (_graftDef == null) return;

            // Restore skinRoot targets to prevent additive compounding
            foreach (var kvp in _skinRootInitial)
            {
                var rootT = kvp.Key;
                if (rootT == null) continue;
                var restore = _skinRootPreOffset.TryGetValue(rootT, out var prev)
                    ? prev : kvp.Value;
                rootT.localPosition = restore.pos;
                rootT.localEulerAngles = new Vector3(0, 0, restore.rot);
                rootT.localScale = restore.scale;
            }
        }

        /// <summary>Cache rest poses for all ancestor bones between the source sprite's
        /// controlling rig bone and the clone root. Called once at Create time.</summary>
        private void CacheAncestorRestPoses()
        {
            _ancestorRestPoses.Clear();
            if (_sourceBoneTransform == null) return;

            // The source transform is a sprite object (SpriteRenderer + SpriteSkin),
            // which typically sits as a direct child of the clone root. Its *rig bones*
            // (the ones that actually animate it) live in a separate bone sub-hierarchy.
            // We need to freeze the rig bone ancestors, not the sprite object's parent.
            Transform startBone = _sourceBoneTransform;
            var skin = _sourceBoneTransform.GetComponent<SpriteSkin>();
            if (skin != null)
            {
                var ctrlBone = BoneHierarchyUtils.GetControllingBone(skin);
                if (ctrlBone != null)
                    startBone = ctrlBone;
            }

            // Exclude the controlling bone — graft BoneOverrides use SET mode
            // and write absolute values directly. Only freeze ancestors above it
            // to prevent source body motion from leaking through.
            var t = startBone.parent;
            while (t != null && t != transform)
            {
                _ancestorRestPoses.Add((t, t.localPosition, t.localRotation, t.localScale));
                t = t.parent;
            }
        }

        /// <summary>Freeze ancestor bones to their rest pose so source body
        /// animation doesn't leak into the grafted bone's world transform.</summary>
        private void FreezeAncestors()
        {
            for (int i = 0; i < _ancestorRestPoses.Count; i++)
            {
                var (bone, pos, rot, scale) = _ancestorRestPoses[i];
                if (bone == null) continue;
                bone.localPosition = pos;
                bone.localRotation = rot;
                bone.localScale = scale;
            }
        }

        private void LateUpdate()
        {
            if (_graftDef == null) return;

            // Track current animation state
            if (_puppetAnimator != null && _puppetAnimator.isActiveAndEnabled)
            {
                var state = _puppetAnimator.GetCurrentAnimatorStateInfo(0);
                _currentNormTime = state.normalizedTime % 1f;
            }

            // Freeze ancestor bones to rest pose. The Animator just evaluated the
            // full source skeleton, but we only want the grafted bone's LOCAL animation
            // (how the sprite bends). Ancestor motion (source body sway, neck nod) must
            // not leak through — the graft is anchored to the HOST's bone instead.
            FreezeAncestors();

            // Align clone so the grafted source bone sits at the host target bone.
            // The clone root is parented under the target bone at (0,0,0), but the
            // Animator places the source bone at some offset within the clone skeleton.
            // We shift the entire clone so the source bone ends up at the target.
            if (_sourceBoneTransform != null && _hostTargetBone != null)
            {
                Vector3 delta = _hostTargetBone.position - _sourceBoneTransform.position;
                transform.position += delta;
            }
            else if (_sourceBoneTransform == null || _hostTargetBone == null)
                return; // Host or source bone destroyed (e.g. character died) — skip overrides

            // Snapshot skinRoot targets BEFORE additive overrides
            foreach (var kvp in _skinRootInitial)
            {
                var rootT = kvp.Key;
                if (rootT == null) continue;
                _skinRootPreOffset[rootT] = (rootT.localPosition, rootT.localEulerAngles.z, rootT.localScale);
            }

            // Re-stamp custom sprites BEFORE bone overrides (see CharacterOverrideDriver)
            foreach (var kvp in _customSpriteCache)
            {
                if (_srMap.TryGetValue(kvp.Key, out var sr) && sr != null)
                    sr.sprite = kvp.Value;
            }

            // Apply graft-scoped bone overrides. Bones redirected through
            // skinRootMap (e.g. Cabeza → bone_3) use SET mode — absolute values,
            // no additive compounding, no freeze needed on the controlling bone.
            if (_graftDef.BoneOverrides.Count > 0)
            {
                CharacterOverrideDriver.ApplyBoneOverrides(
                    _graftDef.BoneOverrides, _boneMap, _srMap,
                    _skinRootMap,
                    _baseSortingOrder, _baseFlipX, _baseFlipY,
                    _baseAlpha, _graftSetModeBones);
            }

            // Apply graft-scoped animation keyframe overrides
            if (_graftDef.AnimOverrides.Count > 0)
                ApplyGraftKeyframes();

            // Ensure graft sprites sort above host sprites. The graft's base
            // sortingOrders come from the source prefab, unrelated to the host's
            // Animator-driven ordering. If the host pushes sprites above the
            // graft's range during certain animations, the graft disappears behind.
            EnsureSortingAboveHost();
        }

        /// <summary>
        /// Shift all visible graft sprites up if any of them are at or below
        /// the host's current max sortingOrder.
        /// </summary>
        private void EnsureSortingAboveHost()
        {
            if (_hostDriver == null) return;

            int hostMax = int.MinValue;
            foreach (var kvp in _hostDriver.SrMap)
            {
                if (kvp.Value != null && kvp.Value.enabled)
                    hostMax = Mathf.Max(hostMax, kvp.Value.sortingOrder);
            }
            if (hostMax == int.MinValue) return;

            int graftMin = int.MaxValue;
            foreach (var kvp in _srMap)
            {
                if (kvp.Value != null && kvp.Value.enabled && _visibleSprites.Contains(kvp.Key))
                    graftMin = Mathf.Min(graftMin, kvp.Value.sortingOrder);
            }
            if (graftMin == int.MaxValue) return;

            if (graftMin <= hostMax)
            {
                int shift = hostMax - graftMin + 1;
                foreach (var kvp in _srMap)
                {
                    if (kvp.Value != null && kvp.Value.enabled && _visibleSprites.Contains(kvp.Key))
                        kvp.Value.sortingOrder += shift;
                }
            }
        }

        private void OnDestroy()
        {
            CharacterOverrideDriver.CleanupPivotCache(_srMap.Values);
            foreach (var sprite in _customSpriteCache.Values)
            {
                if (sprite != null && SpriteUtils.FindSpriteByName(sprite.name) != sprite)
                    Object.Destroy(sprite);
            }
            _customSpriteCache.Clear();
        }

        /// <summary>Apply and cache graft-scoped custom sprites. Called once at Create time.
        /// Cached sprites are re-stamped each LateUpdate to prevent Animator sprite curves
        /// from overwriting them (mirrors CharacterOverrideDriver.ApplyCustomSprites).</summary>
        private void ApplyCustomSprites()
        {
            _customSpriteCache.Clear();
            if (_graftDef.CustomSprites.Count == 0) return;
            foreach (var kvp in _graftDef.CustomSprites)
            {
                if (!_srMap.TryGetValue(kvp.Key, out var sr) || sr == null) continue;
                var newSprite = SpriteUtils.CreateSpriteFromDef(kvp.Value, null, null, sr.sprite);
                if (newSprite != null)
                {
                    // Pre-bake pivot override into cached sprite so the per-frame
                    // re-stamp doesn't clobber the pivot set by ApplyBoneOverrides.
                    if (_graftDef.BoneOverrides.TryGetValue(kvp.Key, out var bo) &&
                        (bo.PivotX >= 0f || bo.PivotY >= 0f))
                    {
                        float px = bo.PivotX >= 0f ? Mathf.Clamp01(bo.PivotX) : newSprite.pivot.x / newSprite.rect.width;
                        float py = bo.PivotY >= 0f ? Mathf.Clamp01(bo.PivotY) : newSprite.pivot.y / newSprite.rect.height;
                        var pivotBaked = Sprite.Create(newSprite.texture, newSprite.rect, new Vector2(px, py), newSprite.pixelsPerUnit);
                        if (SpriteUtils.FindSpriteByName(newSprite.name) != newSprite)
                            Object.Destroy(newSprite);
                        newSprite = pivotBaked;
                    }
                    sr.sprite = newSprite;
                    _customSpriteCache[kvp.Key] = newSprite;
                }
                else if (sr.sprite != null)
                {
                    // Fallback: clone existing sr.sprite for re-stamping so each
                    // instance owns its own copy (see CharacterOverrideDriver)
                    var existing = Object.Instantiate(sr.sprite);
                    if (_graftDef.BoneOverrides.TryGetValue(kvp.Key, out var bo2) &&
                        (bo2.PivotX >= 0f || bo2.PivotY >= 0f))
                    {
                        float px = bo2.PivotX >= 0f ? Mathf.Clamp01(bo2.PivotX) : existing.pivot.x / existing.rect.width;
                        float py = bo2.PivotY >= 0f ? Mathf.Clamp01(bo2.PivotY) : existing.pivot.y / existing.rect.height;
                        var intermediate = existing;
                        existing = Sprite.Create(intermediate.texture, intermediate.rect, new Vector2(px, py), intermediate.pixelsPerUnit);
                        Object.Destroy(intermediate);
                        sr.sprite = existing;
                    }
                    _customSpriteCache[kvp.Key] = existing;
                }
            }
        }

        /// <summary>Pre-create pivot-shifted sprites for bones with pivot overrides
        /// but no custom sprite. Mirrors CharacterOverrideDriver.CachePivotOnlySprites.</summary>
        private void CachePivotOnlySprites()
        {
            foreach (var kvp in _graftDef.BoneOverrides)
            {
                if (kvp.Value.PivotX < 0f && kvp.Value.PivotY < 0f) continue;
                if (_customSpriteCache.ContainsKey(kvp.Key)) continue;
                if (!_srMap.TryGetValue(kvp.Key, out var sr) || sr == null || sr.sprite == null) continue;

                bool isSkinDeformed = _skinRootMap.ContainsKey(kvp.Key);
                if (isSkinDeformed) continue;

                var orig = sr.sprite;
                float px = kvp.Value.PivotX >= 0f ? Mathf.Clamp01(kvp.Value.PivotX) : orig.pivot.x / orig.rect.width;
                float py = kvp.Value.PivotY >= 0f ? Mathf.Clamp01(kvp.Value.PivotY) : orig.pivot.y / orig.rect.height;
                var pivotSprite = Sprite.Create(orig.texture, orig.rect, new Vector2(px, py), orig.pixelsPerUnit);
                sr.sprite = pivotSprite;
                _customSpriteCache[kvp.Key] = pivotSprite;
            }
        }

        private void ApplyGraftKeyframes()
        {
            string currentClip = null;
            if (_puppetAnimator != null && _puppetAnimator.isActiveAndEnabled)
            {
                var stateInfo = _puppetAnimator.GetCurrentAnimatorStateInfo(0);
                foreach (var kvp in _graftDef.AnimOverrides)
                {
                    if (_clipNameHash.TryGetValue(kvp.Key, out int hash) &&
                        stateInfo.shortNameHash == hash)
                    {
                        currentClip = kvp.Key;
                        break;
                    }
                }

                // IsName fallback (mirrors CharacterOverrideDriver.ApplyAnimationKeyframes)
                if (currentClip == null)
                {
                    foreach (var clipName in _graftDef.AnimOverrides.Keys)
                    {
                        if (stateInfo.IsName(clipName))
                        { currentClip = clipName; break; }
                    }
                }
            }

            if (currentClip == null) return;
            if (!_graftDef.AnimOverrides.TryGetValue(currentClip, out var animOvr)) return;

            CharacterOverrideDriver.ApplyKeyframeOverrides(
                animOvr.BoneKeyframes, _currentNormTime,
                _boneMap, null, _skinRootMap);
        }
    }
}
