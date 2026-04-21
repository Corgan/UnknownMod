using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// MonoBehaviour attached to instantiated NPC/Hero animated models at runtime.
    /// Handles:
    ///   - Spawning GraftPuppets for each GraftDef
    ///   - Per-frame bone transform overrides (LateUpdate, after Animator)
    ///   - Animation keyframe interpolation
    ///   - Model-wide scale/flip
    ///   - Re-enforcing removed bones
    ///
    /// Grafts are handled by GraftPuppet instances (each with its own Animator
    /// synced via AnimatorStateMirror). This driver only manages the HOST skeleton.
    /// </summary>
    [DefaultExecutionOrder(-2)] // Run BEFORE SpriteSkin (-1)
    public class CharacterOverrideDriver : MonoBehaviour
    {
        // ── Static instance registry (for cleanup of inactive GOs) ────
        private static readonly HashSet<CharacterOverrideDriver> _allDrivers = new();
        public static IReadOnlyCollection<CharacterOverrideDriver> AllDrivers => _allDrivers;

        // ── Configuration ────────────────────────────────────────────
        private CharacterOverrideDef _def;
        private Animator _anim;

        // ── Cached bone references (host skeleton only) ──────────────
        private Dictionary<string, Transform> _boneMap = new();
        private Dictionary<string, SpriteRenderer> _srMap = new();

        // ── Rest pose (recorded at init) ─────────────────────────────
        private Dictionary<string, Vector3> _restPos = new();
        private Dictionary<string, float> _restRot = new();
        private Dictionary<string, Vector3> _restScale = new();

        // ── Cached base SR state ─────────────────────────────────────
        private Dictionary<string, int> _baseSortingOrder = new();
        private Dictionary<string, bool> _baseFlipX = new();
        private Dictionary<string, bool> _baseFlipY = new();
        private Dictionary<string, float> _baseAlpha = new();


        // ── SpriteSkin rootBone redirect ──────────────────────────────
        private Dictionary<string, Transform> _skinRootMap = new();

        // ── SkinRoot compounding prevention ──────────────────────────
        private Dictionary<Transform, (Vector3 pos, float rot, Vector3 scale)> _skinRootInitial = new();
        private Dictionary<Transform, (Vector3 pos, float rot, Vector3 scale)> _skinRootPreOffset = new();

        // ── Model root rest pose (prevents offset compounding) ────────
        private Vector3 _restModelPos;
        private Vector3 _restModelScale;

        // ── Last applied model offset (for undo without hard-reset) ───
        private Vector3 _lastAppliedModelOffset;

        // ── Animation state ──────────────────────────────────────────
        private float _currentNormTime;
        private Dictionary<string, int> _clipNameHash = new();
        // ── Cached custom sprites (re-stamped each LateUpdate) ────
        private Dictionary<string, Sprite> _customSpriteCache = new();
        // ── Spawned graft puppets ────────────────────────────────────
        private List<GraftPuppet> _puppets = new();
        // ── Reusable scratch set for ApplyBoneOverrides (avoids per-frame alloc) ──
        private static HashSet<Transform> _sharedSkinRootTargets = new();

        // ── Deferred collider recalc ─────────────────────────────────
        private CharacterItem _pendingColliderRecalcTarget;

        /// <summary>Get spawned graft puppets (for editor handle collection).</summary>
        public List<GraftPuppet> Puppets => _puppets;
        /// <summary>Get the host bone map.</summary>
        public Dictionary<string, Transform> BoneMap => _boneMap;
        /// <summary>Get the host SR map.</summary>
        public Dictionary<string, SpriteRenderer> SrMap => _srMap;

        /// <summary>Collect all visible SpriteRenderers from spawned graft puppets.
        /// Used by patches to register puppet sprites with the host CharacterItem
        /// so that game effects (hover outline, darken, material swaps) include them.</summary>
        public List<SpriteRenderer> GetPuppetVisibleSprites()
        {
            var result = new List<SpriteRenderer>();
            foreach (var puppet in _puppets)
            {
                if (puppet == null) continue;
                foreach (var kvp in puppet.SrMap)
                {
                    if (kvp.Value != null && kvp.Value.enabled && puppet.VisibleSpriteNames.Contains(kvp.Key))
                        result.Add(kvp.Value);
                }
            }
            return result;
        }

        /// <summary>Schedule a collider recalculation for the end of the first LateUpdate frame,
        /// when all bone positions (host + graft puppets) are finalized.</summary>
        public void ScheduleColliderRecalc(CharacterItem charItem)
        {
            _pendingColliderRecalcTarget = charItem;
        }

        /// <summary>
        /// Initialize the driver. Called once from the NPCItem.Init / HeroItem.Init
        /// postfix patch after the animated model is instantiated.
        /// </summary>
        public void Init(CharacterOverrideDef def)
        {
            _allDrivers.Add(this);
            _def = def;
            _anim = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();

            // Build bone lookup maps (host skeleton)
            _boneMap.Clear(); _srMap.Clear();
            _restPos.Clear(); _restRot.Clear(); _restScale.Clear();
            _baseSortingOrder.Clear(); _baseFlipX.Clear(); _baseFlipY.Clear(); _baseAlpha.Clear();
            _clipNameHash.Clear();
            _skinRootMap.Clear(); _skinRootInitial.Clear(); _skinRootPreOffset.Clear();

            CacheBones(transform);

            // Record model root rest pose for offset/scale restoration
            _restModelPos = transform.localPosition;
            _restModelScale = transform.localScale;

            // Build clip name → hash mapping
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
                }
            }

            // ── One-time init steps (order matters) ──────────────

            // 0. Sort animation keyframes (required by InterpolateKeyframes' linear scan)
            if (_def.AnimOverrides != null)
            {
                foreach (var animOvr in _def.AnimOverrides.Values)
                    foreach (var kfList in animOvr.BoneKeyframes.Values)
                        kfList.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            // 1. Custom sprites: swap SpriteRenderer sprites on matching bones
            ApplyCustomSprites();

            // 1b. For bones with pivot overrides but NO custom sprite, pre-create
            // the pivot-shifted sprite and add it to the re-stamp cache. Without this,
            // the Animator resets sr.sprite each frame (via sprite curves) causing
            // per-frame Sprite.Create + Destroy churn in ApplyBoneOverrides.
            CachePivotOnlySprites();

            // 2. Spawn GraftPuppets
            SpawnGraftPuppets();

            // 3. Model-wide tint + alpha (one-time, before shader materials)
            ApplyModelColor();

            Plugin.Log.LogInfo($"[CharacterOverrideDriver] Init on '{gameObject.name}': " +
                $"{_boneMap.Count} bones, {_srMap.Count} SRs, " +
                $"{_def.BoneOverrides.Count} bone overrides, " +
                $"{_puppets.Count} grafts, anim={(_anim != null)}");
        }

        /// <summary>Swap SpriteRenderer sprites for bones with custom sprite defs.
        /// Caches the created sprites for per-frame re-stamping in LateUpdate,
        /// since Animator sprite curves overwrite sr.sprite each frame and
        /// curve stripping via UnityEditor.AnimationUtility is unavailable at runtime.</summary>
        private void ApplyCustomSprites()
        {
            _customSpriteCache.Clear();
            if (_def.CustomSprites.Count == 0) return;
            foreach (var kvp in _def.CustomSprites)
            {
                if (_def.RemovedBones.Contains(kvp.Key)) continue;
                if (!_srMap.TryGetValue(kvp.Key, out var sr) || sr == null) continue;
                var newSprite = SpriteUtils.CreateSpriteFromDef(kvp.Value, null, _def.Spritesheet, sr.sprite);
                if (newSprite != null)
                {
                    // If this bone also has a pivot override, pre-bake the shifted
                    // pivot into the cached sprite so the per-frame re-stamp in
                    // LateUpdate doesn't clobber the pivot that ApplyBoneOverrides sets.
                    if (_def.BoneOverrides.TryGetValue(kvp.Key, out var bo) &&
                        (bo.PivotX >= 0f || bo.PivotY >= 0f))
                    {
                        float px = bo.PivotX >= 0f ? Mathf.Clamp01(bo.PivotX) : newSprite.pivot.x / newSprite.rect.width;
                        float py = bo.PivotY >= 0f ? Mathf.Clamp01(bo.PivotY) : newSprite.pivot.y / newSprite.rect.height;
                        var pivotBaked = Sprite.Create(newSprite.texture, newSprite.rect, new Vector2(px, py), newSprite.pixelsPerUnit);
                        // Destroy the intermediate sprite if it was a Sprite.Create result
                        // (i.e. not a shared named sprite from the game's atlas)
                        if (!IsSharedAsset(newSprite))
                            Object.Destroy(newSprite);
                        newSprite = pivotBaked;
                    }
                    sr.sprite = newSprite;
                    _customSpriteCache[kvp.Key] = newSprite;
                }
                else if (sr.sprite != null)
                {
                    // CreateSpriteFromDef returned null (e.g. disk-path sprite but no modId
                    // at runtime). NpcPrefabBuilder already baked the correct sprite at build
                    // time — clone it so each instance owns its own copy (Object.Instantiate
                    // doesn't deep-clone sprite assets, so all clones share the prefab's sprite).
                    var existing = Object.Instantiate(sr.sprite);
                    if (_def.BoneOverrides.TryGetValue(kvp.Key, out var bo2) &&
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

        /// <summary>For bones with pivot overrides but no custom sprite, pre-create
        /// a pivot-shifted sprite and add it to _customSpriteCache. This prevents
        /// per-frame Sprite.Create churn in ApplyBoneOverrides when the Animator
        /// resets sr.sprite via sprite curves each frame.</summary>
        private void CachePivotOnlySprites()
        {
            foreach (var kvp in _def.BoneOverrides)
            {
                if (kvp.Value.PivotX < 0f && kvp.Value.PivotY < 0f) continue;
                if (_customSpriteCache.ContainsKey(kvp.Key)) continue; // already has custom sprite
                if (_def.RemovedBones.Contains(kvp.Key)) continue;
                if (!_srMap.TryGetValue(kvp.Key, out var sr) || sr == null || sr.sprite == null) continue;

                bool isSkinDeformed = _skinRootMap.ContainsKey(kvp.Key);
                if (isSkinDeformed) continue; // pivot override doesn't apply to SpriteSkin-deformed bones

                var orig = sr.sprite;
                float px = kvp.Value.PivotX >= 0f ? Mathf.Clamp01(kvp.Value.PivotX) : orig.pivot.x / orig.rect.width;
                float py = kvp.Value.PivotY >= 0f ? Mathf.Clamp01(kvp.Value.PivotY) : orig.pivot.y / orig.rect.height;
                var pivotSprite = Sprite.Create(orig.texture, orig.rect, new Vector2(px, py), orig.pixelsPerUnit);
                sr.sprite = pivotSprite;
                _customSpriteCache[kvp.Key] = pivotSprite;
            }
        }

        /// <summary>Apply model-wide tint + alpha to all SpriteRenderers (one-time at init).</summary>
        private void ApplyModelColor()
        {
            var model = _def.Model;
            Color modelTint = Color.white;
            bool hasModelTint = !string.IsNullOrEmpty(model.TintHex) &&
                                ColorUtility.TryParseHtmlString(model.TintHex, out modelTint);
            float modelAlpha = Mathf.Clamp01(model.Alpha);
            if (!hasModelTint && modelAlpha >= 1f) return;

            foreach (var kvp in _srMap)
            {
                var sr = kvp.Value;
                if (sr == null) continue;
                Color c = hasModelTint ? modelTint : sr.color;
                // Use flat assignment (not multiply) — prefab builder may have
                // already baked alpha into sr.color, so multiplying would square it.
                if (modelAlpha < 1f) c.a = modelAlpha;
                sr.color = c;
            }

            // Update _baseAlpha so per-bone alpha multiplies against the model-adjusted value
            foreach (var kvp in _srMap)
            {
                if (kvp.Value != null)
                    _baseAlpha[kvp.Key] = kvp.Value.color.a;
            }
        }

        /// <summary>Spawn one GraftPuppet per GraftDef.</summary>
        private void SpawnGraftPuppets()
        {
            foreach (var graft in _def.Grafts)
            {
                if (string.IsNullOrEmpty(graft.Source) || string.IsNullOrEmpty(graft.TargetBone))
                    continue;

                // Find the host target bone
                if (!_boneMap.TryGetValue(graft.TargetBone, out var targetBone) || targetBone == null)
                {
                    Plugin.Log.LogWarning($"[CharacterOverrideDriver] Graft target bone '{graft.TargetBone}' not found in host");
                    continue;
                }

                var puppet = GraftPuppet.Create(graft, targetBone, _anim);
                if (puppet != null)
                {
                    _puppets.Add(puppet);

                    // Hide original sprite at target if requested
                    if (graft.ReplaceTarget && _srMap.TryGetValue(graft.TargetBone, out var sr))
                        sr.enabled = false;
                }
            }
        }

        private void CacheBones(Transform root)
        {
            BoneHierarchyUtils.CollectBones(root, _boneMap, _srMap);

            // Record rest pose and base SR state
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

            // Map SpriteSkin sprites to their controlling rig bone
            foreach (var kvp in _boneMap)
            {
                var skin = kvp.Value.GetComponent<SpriteSkin>();
                if (skin == null) continue;
                var ctrlBone = BoneHierarchyUtils.GetControllingBone(skin);
                if (ctrlBone != kvp.Value)
                {
                    _skinRootMap[kvp.Key] = ctrlBone;
                    if (!_skinRootInitial.ContainsKey(ctrlBone))
                        _skinRootInitial[ctrlBone] = (ctrlBone.localPosition, ctrlBone.localEulerAngles.z, ctrlBone.localScale);
                }
            }
        }

        /// <summary>
        /// Runs BEFORE the Animator evaluates. Restores skinRoot targets to their
        /// last pre-offset value to prevent additive compounding.
        /// </summary>
        private void Update()
        {
            if (_def == null) return;

            // Undo the model offset that ApplyGlobalOverrides added last frame,
            // WITHOUT hard-resetting localPosition. Hard-resetting would fight
            // the game's attack movement coroutine (SetPositionCO lerps charImageT
            // to screen center for MoveToCenter attacks). By subtracting only our
            // own offset, external position changes are preserved.
            transform.localPosition -= _lastAppliedModelOffset;
            _lastAppliedModelOffset = Vector3.zero;

            // Scale can be hard-reset — the game doesn't modify model scale during attacks
            transform.localScale = _restModelScale;

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

        /// <summary>
        /// Called every frame AFTER the Animator updates.
        /// Applies host bone overrides, keyframes, model transform, and removed bones.
        /// </summary>
        private void LateUpdate()
        {
            if (_def == null) return;

            // Track current animation state
            if (_anim != null && _anim.enabled)
            {
                var stateInfo = _anim.GetCurrentAnimatorStateInfo(0);
                _currentNormTime = stateInfo.normalizedTime % 1f;
            }

            // 1. Global model transform
            ApplyGlobalOverrides();

            // 2. Snapshot skinRoot targets BEFORE additive overrides
            foreach (var kvp in _skinRootInitial)
            {
                var rootT = kvp.Key;
                if (rootT == null) continue;
                _skinRootPreOffset[rootT] = (rootT.localPosition, rootT.localEulerAngles.z, rootT.localScale);
            }

            // 3. Re-stamp custom sprites BEFORE bone overrides. Animator sprite
            //    curves reset sr.sprite each frame; re-stamping here means custom
            //    sprites (with pre-baked pivots) are in place when ApplyBoneOverrides
            //    runs, so its pivot comparison sees a match and skips Sprite.Create.
            foreach (var kvp in _customSpriteCache)
            {
                if (_srMap.TryGetValue(kvp.Key, out var sr) && sr != null)
                    sr.sprite = kvp.Value;
            }

            // 4. Per-bone transform overrides
            if (_def.BoneOverrides.Count > 0)
            {
                ApplyBoneOverrides(_def.BoneOverrides, _boneMap, _srMap, _skinRootMap,
                    _baseSortingOrder, _baseFlipX, _baseFlipY, _baseAlpha, null);
            }

            // 5. Host animation keyframe overrides
            ApplyAnimationKeyframes();

            // 6. Re-enforce removed bones — hide the SpriteRenderer only,
            // NOT the GameObject. Rig bones must stay active for SpriteSkin
            // deformation on other sprites that reference them.
            foreach (var boneName in _def.RemovedBones)
            {
                if (_srMap.TryGetValue(boneName, out var sr) && sr != null)
                    sr.enabled = false;
            }

            // 7. Deferred collider recalc — runs once on the first LateUpdate frame
            // when all bone positions (host + graft puppets) are finalized.
            if (_pendingColliderRecalcTarget != null)
            {
                RecalcCollider(_pendingColliderRecalcTarget);
                _pendingColliderRecalcTarget = null;
            }
        }

        /// <summary>Recalculate the BoxCollider2D to encompass all visible sprites (host + grafts).</summary>
        private void RecalcCollider(CharacterItem charItem)
        {
            // The collider lives on the CharacterItem GO (parent), not on
            // animatedTransform (where this driver is attached).
            var collider = charItem.GetComponent<BoxCollider2D>()
                        ?? charItem.GetComponentInChildren<BoxCollider2D>();
            if (collider == null) return;

            var renderers = GetComponentsInChildren<SpriteRenderer>(false);
            if (renderers.Length == 0) return;

            bool first = true;
            Bounds combined = default;
            foreach (var sr in renderers)
            {
                if (sr == null || !sr.enabled || sr.sprite == null) continue;
                if (sr.gameObject.name.ToLower() == "shadow") continue;
                var worldBounds = sr.bounds;
                var localMin = transform.InverseTransformPoint(worldBounds.min);
                var localMax = transform.InverseTransformPoint(worldBounds.max);
                var localBounds = new Bounds((localMin + localMax) * 0.5f, localMax - localMin);
                if (first) { combined = localBounds; first = false; }
                else combined.Encapsulate(localBounds);
            }
            if (first) return;

            collider.offset = new Vector2(combined.center.x, combined.center.y);
            collider.size = new Vector2(combined.size.x, combined.size.y);
            charItem.heightModel = combined.size.y;
        }

        private void OnDestroy()
        {
            _allDrivers.Remove(this);
            CleanupPivotCache(_srMap.Values);
            foreach (var sprite in _customSpriteCache.Values)
            {
                if (sprite != null && !IsSharedAsset(sprite))
                    Object.Destroy(sprite);
            }
            _customSpriteCache.Clear();
        }

        /// <summary>Returns true if this sprite is a shared/named asset that should NOT be destroyed.</summary>
        private static bool IsSharedAsset(Sprite sprite)
        {
            return SpriteUtils.FindSpriteByName(sprite.name) == sprite;
        }

        private void ApplyGlobalOverrides()
        {
            Vector3 s = transform.localScale;
            var model = _def.Model;

            // Scale + flip
            if (model.Scale != 1f)
            {
                float restAbs = Mathf.Max(Mathf.Abs(_restModelScale.x), 0.001f);
                float target = restAbs * model.Scale;
                float signX = model.FlipX ? -1f : Mathf.Sign(s.x);
                float signY = model.FlipY ? -1f : Mathf.Sign(s.y);
                transform.localScale = new Vector3(signX * target, signY * target, s.z);
            }
            else if (model.FlipX || model.FlipY)
            {
                if (model.FlipX) s.x = -Mathf.Abs(s.x);
                if (model.FlipY) s.y = -Mathf.Abs(s.y);
                transform.localScale = s;
            }

            // Offset (additive each frame — undone by Update via _lastAppliedModelOffset)
            if (model.OffsetX != 0f || model.OffsetY != 0f)
            {
                var offset = new Vector3(model.OffsetX, model.OffsetY, 0f);
                transform.localPosition += offset;
                _lastAppliedModelOffset = offset;
            }
        }

        private void ApplyAnimationKeyframes()
        {
            if (_def.AnimOverrides == null || _def.AnimOverrides.Count == 0) return;

            string currentClip = null;
            if (_anim != null && _anim.enabled)
            {
                var stateInfo = _anim.GetCurrentAnimatorStateInfo(0);
                foreach (var kvp in _def.AnimOverrides)
                {
                    if (_clipNameHash.TryGetValue(kvp.Key, out int hash) &&
                        stateInfo.shortNameHash == hash)
                    {
                        currentClip = kvp.Key;
                        break;
                    }
                }

                if (currentClip == null)
                {
                    foreach (var clipName in _def.AnimOverrides.Keys)
                    {
                        if (stateInfo.IsName(clipName))
                        { currentClip = clipName; break; }
                    }
                }
            }

            if (currentClip == null) return;
            if (!_def.AnimOverrides.TryGetValue(currentClip, out var animOvr)) return;

            ApplyKeyframeOverrides(animOvr.BoneKeyframes, _currentNormTime,
                _boneMap, null, _skinRootMap);
        }

        // ═══════════════════════════════════════════════════════════════
        //  SHARED BONE OVERRIDE APPLICATION (used by editor + runtime + puppets)
        // ═══════════════════════════════════════════════════════════════

        // Track Sprite.Create results to prevent leaks when Animator resets sr.sprite
        private static Dictionary<int, Sprite> _pivotSpriteCache = new();

        /// <summary>Remove and destroy pivot sprite cache entries for the given SpriteRenderers.</summary>
        internal static void CleanupPivotCache(IEnumerable<SpriteRenderer> srs)
        {
            foreach (var sr in srs)
            {
                if (sr == null) continue;
                int srId = sr.GetInstanceID();
                if (_pivotSpriteCache.TryGetValue(srId, out var pivotSprite))
                {
                    if (pivotSprite != null) Object.Destroy(pivotSprite);
                    _pivotSpriteCache.Remove(srId);
                }
            }
        }

        /// <summary>Destroy all cached pivot sprites and clear the static cache.
        /// Call during zone cleanup / scene teardown to prevent leaks from
        /// drivers that were destroyed via scene unload rather than explicit DestroyImmediate.</summary>
        public static void ClearAllPivotCaches()
        {
            foreach (var kvp in _pivotSpriteCache)
            {
                if (kvp.Value != null) Object.Destroy(kvp.Value);
            }
            _pivotSpriteCache.Clear();
        }

        /// <summary>
        /// Core per-bone override application logic. Single source of truth used by:
        ///   - CharacterOverrideDriver (host bones, LateUpdate)
        ///   - GraftPuppet (graft bones, LateUpdate)
        ///   - Editor sprite preview (after SampleAnimation)
        /// </summary>
        public static void ApplyBoneOverrides(
            Dictionary<string, BoneOverride> boneOverrides,
            Dictionary<string, Transform> boneMap,
            Dictionary<string, SpriteRenderer> srMap,
            Dictionary<string, Transform> skinRootMap,
            Dictionary<string, int> baseSortingOrder,
            Dictionary<string, bool> baseFlipX,
            Dictionary<string, bool> baseFlipY,
            Dictionary<string, float> baseAlpha,
            HashSet<string> nonAnimatorBones)
        {
            _sharedSkinRootTargets.Clear();

            foreach (var kvp in boneOverrides)
            {
                string boneName = kvp.Key;
                BoneOverride bo = kvp.Value;
                if (!boneMap.TryGetValue(boneName, out var bone) || bone == null) continue;

                Transform skinRoot = null;
                bool hasSkinRoot = skinRootMap != null && skinRootMap.TryGetValue(boneName, out skinRoot);
                Transform xform = hasSkinRoot ? skinRoot : bone;

                bool isNonAnimator = nonAnimatorBones != null && nonAnimatorBones.Contains(
                    hasSkinRoot ? xform.name : boneName);

                bool skipTransform = false;
                if (_sharedSkinRootTargets.Contains(xform))
                {
                    skipTransform = true;
                    Plugin.Log.LogDebug($"[CharacterOverrideDriver] Bone '{boneName}' shares skinRoot " +
                        $"'{xform.name}' with another bone — transform overrides skipped for this bone");
                }

                if (hasSkinRoot && !skipTransform)
                    _sharedSkinRootTargets.Add(skinRoot);

                if (!skipTransform)
                {
                    // Position
                    if (isNonAnimator)
                    {
                        Vector3 pos = xform.localPosition;
                        pos.x = bo.PosX; pos.y = bo.PosY;
                        xform.localPosition = pos;
                    }
                    else if (bo.PosX != 0f || bo.PosY != 0f)
                    {
                        // World-aligned offset: rotate into parent's local frame
                        Vector3 pos = xform.localPosition;
                        if (xform.parent != null)
                        {
                            Vector3 local = xform.parent.InverseTransformDirection(
                                new Vector3(bo.PosX, bo.PosY, 0f));
                            pos.x += local.x; pos.y += local.y;
                        }
                        else
                        {
                            pos.x += bo.PosX; pos.y += bo.PosY;
                        }
                        xform.localPosition = pos;
                    }

                    // Rotation
                    if (isNonAnimator)
                        xform.localEulerAngles = new Vector3(0, 0, bo.Rotation);
                    else if (bo.Rotation != 0f)
                    {
                        Vector3 euler = xform.localEulerAngles;
                        euler.z += bo.Rotation;
                        xform.localEulerAngles = euler;
                    }

                    // Scale — world-aligned via rotated diagonal approximation.
                    // Sign (flip) is separated from magnitude: the cos²/sin²
                    // blend only handles positive magnitudes. Mixing +1 and −1
                    // through the blend collapses to zero at 45° parent rotation.
                    if (isNonAnimator)
                        xform.localScale = new Vector3(bo.ScaleX, bo.ScaleY, 1f);
                    else if (bo.ScaleX != 1f || bo.ScaleY != 1f)
                    {
                        float signX = bo.ScaleX < 0f ? -1f : 1f;
                        float signY = bo.ScaleY < 0f ? -1f : 1f;
                        float magX = Mathf.Abs(bo.ScaleX);
                        float magY = Mathf.Abs(bo.ScaleY);
                        float pz = xform.parent != null
                            ? xform.parent.eulerAngles.z * Mathf.Deg2Rad : 0f;
                        float c2 = Mathf.Cos(pz) * Mathf.Cos(pz);
                        float s2 = Mathf.Sin(pz) * Mathf.Sin(pz);
                        float lsx = magX * c2 + magY * s2;
                        float lsy = magX * s2 + magY * c2;
                        Vector3 scale = xform.localScale;
                        scale.x *= lsx * signX;
                        scale.y *= lsy * signY;
                        xform.localScale = scale;
                    }
                }

                // Flip via sign inversion (preserves current scale magnitude)
                if (!skipTransform && (bo.FlipX || bo.FlipY))
                {
                    var s = xform.localScale;
                    if (bo.FlipX) s.x = -s.x;
                    if (bo.FlipY) s.y = -s.y;
                    xform.localScale = s;
                }

                // Visibility / alpha / color (on SpriteRenderer)
                if (srMap.TryGetValue(boneName, out var sr) && sr != null)
                {
                    if (!bo.Visible) sr.enabled = false;

                    if (bo.SortingOffset != 0)
                        sr.sortingOrder = (baseSortingOrder != null && baseSortingOrder.TryGetValue(boneName, out var bso) ? bso : sr.sortingOrder) + bo.SortingOffset;

                    if (!string.IsNullOrEmpty(bo.ColorHex))
                    {
                        if (ColorUtility.TryParseHtmlString(bo.ColorHex, out var col))
                            sr.color = col;
                    }
                    // Apply per-bone alpha AND preserve model-wide alpha for recolored bones.
                    // Without the ColorHex check, a bone with ColorHex but default Alpha=1.0
                    // would lose the model-wide alpha set by ApplyModelColor.
                    if (bo.Alpha < 1f || !string.IsNullOrEmpty(bo.ColorHex))
                    {
                        Color c = sr.color;
                        float baseA = baseAlpha != null && baseAlpha.TryGetValue(boneName, out var ba) ? ba : c.a;
                        c.a = baseA * Mathf.Clamp01(bo.Alpha);
                        sr.color = c;
                    }

                    // Pivot override: recreate sprite with shifted pivot.
                    // Only meaningful for plain SpriteRenderers (not SpriteSkin-deformed).
                    if (bo.PivotX >= 0f || bo.PivotY >= 0f)
                    {
                        bool isSkinDeformed = skinRootMap != null && skinRootMap.ContainsKey(boneName);
                        if (!isSkinDeformed && sr.sprite != null)
                        {
                            var orig = sr.sprite;
                            float px = bo.PivotX >= 0f ? Mathf.Clamp01(bo.PivotX) : orig.pivot.x / orig.rect.width;
                            float py = bo.PivotY >= 0f ? Mathf.Clamp01(bo.PivotY) : orig.pivot.y / orig.rect.height;
                            // Only recreate if pivot actually differs (avoid per-frame allocation)
                            float curPx = orig.pivot.x / orig.rect.width;
                            float curPy = orig.pivot.y / orig.rect.height;
                            if (Mathf.Abs(px - curPx) > 0.001f || Mathf.Abs(py - curPy) > 0.001f)
                            {
                                // Destroy any previously created pivot sprite for this SR
                                int srId = sr.GetInstanceID();
                                if (_pivotSpriteCache.TryGetValue(srId, out var prevCreated) && prevCreated != null)
                                    Object.Destroy(prevCreated);
                                sr.sprite = Sprite.Create(
                                    orig.texture, orig.rect,
                                    new Vector2(px, py),
                                    orig.pixelsPerUnit);
                                _pivotSpriteCache[srId] = sr.sprite;
                            }
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SHARED KEYFRAME OVERRIDE APPLICATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Core per-bone animation keyframe application. Single source of truth.</summary>
        public static void ApplyKeyframeOverrides(
            Dictionary<string, List<BoneKeyframe>> boneKeyframes,
            float normTime,
            Dictionary<string, Transform> boneMap,
            HashSet<string> nonAnimatorBones,
            Dictionary<string, Transform> skinRootMap = null)
        {
            if (boneKeyframes == null || boneKeyframes.Count == 0) return;

            foreach (var boneKvp in boneKeyframes)
            {
                string boneName = boneKvp.Key;
                var keyframes = boneKvp.Value;
                if (keyframes == null || keyframes.Count == 0) continue;
                if (!boneMap.TryGetValue(boneName, out var bone) || bone == null) continue;

                var interpolated = InterpolateKeyframes(keyframes, normTime);
                if (interpolated == null) continue;

                Transform xform = bone;
                Transform skinRoot = null;
                bool hasSkinRoot = skinRootMap != null && skinRootMap.TryGetValue(boneName, out skinRoot) && skinRoot != null;
                if (hasSkinRoot) xform = skinRoot;

                bool isNonAnimator = nonAnimatorBones != null && nonAnimatorBones.Contains(
                    hasSkinRoot ? xform.name : boneName);

                if (isNonAnimator)
                {
                    Vector3 pos = xform.localPosition;
                    pos.x = interpolated.PosX; pos.y = interpolated.PosY;
                    xform.localPosition = pos;
                    xform.localEulerAngles = new Vector3(0, 0, interpolated.Rotation);
                    xform.localScale = new Vector3(interpolated.ScaleX, interpolated.ScaleY, 1f);
                }
                else
                {
                    // World-aligned offset
                    Vector3 pos = xform.localPosition;
                    if (xform.parent != null)
                    {
                        Vector3 local = xform.parent.InverseTransformDirection(
                            new Vector3(interpolated.PosX, interpolated.PosY, 0f));
                        pos.x += local.x; pos.y += local.y;
                    }
                    else
                    {
                        pos.x += interpolated.PosX; pos.y += interpolated.PosY;
                    }
                    xform.localPosition = pos;

                    Vector3 euler = xform.localEulerAngles;
                    euler.z += interpolated.Rotation;
                    xform.localEulerAngles = euler;

                    // World-aligned scale via rotated diagonal
                    float pz = xform.parent != null
                        ? xform.parent.eulerAngles.z * Mathf.Deg2Rad : 0f;
                    float c2 = Mathf.Cos(pz) * Mathf.Cos(pz);
                    float s2 = Mathf.Sin(pz) * Mathf.Sin(pz);
                    float lsx = interpolated.ScaleX * c2 + interpolated.ScaleY * s2;
                    float lsy = interpolated.ScaleX * s2 + interpolated.ScaleY * c2;
                    Vector3 scale = xform.localScale;
                    scale.x *= lsx; scale.y *= lsy;
                    xform.localScale = scale;
                }
            }
        }

        /// <summary>Linearly interpolate between the two nearest keyframes.</summary>
        internal static BoneKeyframe InterpolateKeyframes(List<BoneKeyframe> keyframes, float normTime)
        {
            if (keyframes.Count == 0) return null;
            if (keyframes.Count == 1) return keyframes[0];
            if (normTime <= keyframes[0].Time) return keyframes[0];
            if (normTime >= keyframes[keyframes.Count - 1].Time) return keyframes[keyframes.Count - 1];

            BoneKeyframe before = keyframes[0], after = keyframes[1];
            for (int i = 0; i < keyframes.Count - 1; i++)
            {
                if (keyframes[i].Time <= normTime && keyframes[i + 1].Time >= normTime)
                { before = keyframes[i]; after = keyframes[i + 1]; break; }
            }

            if (Mathf.Abs(after.Time - before.Time) < 0.0001f) return before;

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
    }
}
