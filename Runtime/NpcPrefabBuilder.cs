using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Builds custom NPC GameObjectAnimated prefabs at zone registration time.
    /// This is the SINGLE source of truth for all one-time NPC model modifications:
    ///   - Removed bones (deactivate GameObjects)
    ///   - Custom sprites (image swap from spritesheet/file)
    ///   - Visibility overrides (hide bones)
    ///   - Global offset (baked into prefab localPosition)
    ///   - Model tint + alpha (baked into SpriteRenderer colors)
    ///
    /// Grafts are handled at runtime by GraftPuppet (each with its own Animator
    /// synced via AnimatorStateMirror). No graft surgery or curve import needed.
    ///
    /// CharacterOverrideDriver handles ONLY per-frame LateUpdate work (transform
    /// overrides, animation keyframes, scale/flip, graft puppet spawning).
    ///
    /// Workflow:
    /// 1. Clone the skeleton donor NPC's GameObjectAnimated prefab
    /// 2. Apply all one-time modifications
    /// 3. Set the built prefab as npcData.GameObjectAnimated before registration
    /// 4. Game's NPCItem.Init() instantiates a clone for each combat appearance
    /// </summary>
    public static class NpcPrefabBuilder
    {
        // Cache of built prefabs keyed by NPC ID (so we only build once)
        private static readonly Dictionary<string, GameObject> _builtPrefabs = new();
        // Sprites created by Sprite.Create for prefab SRs (destroyed on cache clear)
        private static readonly Dictionary<string, List<Sprite>> _prefabSprites = new();

        /// <summary>
        /// Build a custom prefab for an NPC with sprite overrides.
        /// Returns null if no prefab modifications are needed.
        /// The built prefab is a persistent (DontDestroyOnLoad) hidden GameObject.
        /// </summary>
        public static GameObject BuildCustomPrefab(string npcId, NPCData baseNpcData, CharacterOverrideDef overrideDef, string modId)
        {
            if (baseNpcData == null) return null;
            return BuildCustomPrefab(npcId, baseNpcData.GameObjectAnimated, overrideDef, modId);
        }

        /// <summary>
        /// Build a custom prefab from a base GameObjectAnimated with sprite overrides.
        /// Works for both NPC and hero skin prefabs. Returns null if no modifications are needed.
        /// The built prefab is a persistent (DontDestroyOnLoad) hidden GameObject.
        /// </summary>
        public static GameObject BuildCustomPrefab(string entityId, GameObject sourcePrefab, CharacterOverrideDef overrideDef, string modId)
        {
            if (overrideDef == null) return null;

            // Check if ANY prefab-level modifications exist
            // Note: Model offset is NOT baked here — CharacterOverrideDriver handles
            // it at runtime in ApplyGlobalOverrides to avoid double-application.
            bool hasAny =
                overrideDef.CustomSprites.Count > 0 ||
                overrideDef.RemovedBones.Count > 0 ||
                overrideDef.BoneOverrides.Values.Any(b => !b.Visible) ||
                !string.IsNullOrEmpty(overrideDef.Model.TintHex) ||
                overrideDef.Model.Alpha < 1f;

            if (!hasAny)
            {
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] '{entityId}' has no prefab modifications — skipping build");
                return null;
            }

            // Return cached if already built
            if (_builtPrefabs.TryGetValue(entityId, out var cached) && cached != null)
            {
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] '{entityId}' returning cached prefab");
                return cached;
            }

            if (sourcePrefab == null)
            {
                Plugin.Log.LogWarning($"[NpcPrefabBuilder] '{entityId}' has no source GameObjectAnimated prefab");
                return null;
            }

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] Building custom prefab for '{entityId}'");

            // ── Clone the skeleton donor prefab ──
            var prefab = Object.Instantiate(sourcePrefab);
            prefab.name = $"{entityId}_customPrefab";
            prefab.SetActive(false); // hide from scene
            Object.DontDestroyOnLoad(prefab);

            // Build name→Transform and name→SpriteRenderer maps
            var boneMap = new Dictionary<string, Transform>();
            var srMap = new Dictionary<string, SpriteRenderer>();
            BoneHierarchyUtils.CollectBones(prefab.transform, boneMap, srMap);

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] Cloned prefab: {boneMap.Count} bones, {srMap.Count} SRs");

            // ════════════════════════════════════════════════════════════
            //  1. REMOVED BONES (first — skip these in all subsequent steps)
            //
            //  Hide the SpriteRenderer only, NOT the GameObject. Rig bones
            //  must stay active so other sprites' SpriteSkin components can
            //  still read their transforms for mesh deformation.
            // ════════════════════════════════════════════════════════════
            int removedCount = 0;
            foreach (var boneName in overrideDef.RemovedBones)
            {
                if (!boneMap.TryGetValue(boneName, out var bone)) continue;
                var sr = bone.GetComponent<SpriteRenderer>();
                if (sr != null) sr.enabled = false;
                srMap.Remove(boneName);
                removedCount++;
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] Hid sprite on bone '{boneName}'");
            }

            // Rig bones referenced by other sprites' SpriteSkin stay active
            // (we only hid the SpriteRenderer above), so no need to detach
            // SpriteSkin components that reference removed bones.

            // ════════════════════════════════════════════════════════════
            //  2. STRIP ANIMATOR SPRITE CURVES for custom + pivot-only sprites
            //     Any bone whose sprite we cache and re-stamp each frame needs
            //     its Animator sprite curves stripped, otherwise the re-stamp
            //     overwrites the Animator output and freezes the animation.
            //     Grafts are handled at runtime by GraftPuppet (no surgery needed).
            // ════════════════════════════════════════════════════════════
            var stripBoneSet = new HashSet<string>(overrideDef.CustomSprites.Keys);
            foreach (var kvp in overrideDef.BoneOverrides)
            {
                if ((kvp.Value.PivotX >= 0f || kvp.Value.PivotY >= 0f) &&
                    !overrideDef.CustomSprites.ContainsKey(kvp.Key) &&
                    !overrideDef.RemovedBones.Contains(kvp.Key))
                {
                    stripBoneSet.Add(kvp.Key);
                }
            }
            StripAnimatorSpriteCurves(prefab, stripBoneSet);

            // ════════════════════════════════════════════════════════════
            //  6. CUSTOM SPRITES (image swap from spritesheet/file)
            // ════════════════════════════════════════════════════════════
            int customCount = 0;
            var createdSprites = new List<Sprite>();
            foreach (var kvp in overrideDef.CustomSprites)
            {
                if (overrideDef.RemovedBones.Contains(kvp.Key)) continue;
                if (!srMap.TryGetValue(kvp.Key, out var sr)) continue;
                var newSprite = SpriteUtils.CreateSpriteFromDef(kvp.Value, modId, overrideDef.Spritesheet, sr.sprite);
                if (newSprite != null)
                {
                    // Custom sprite replaces the deformable sprite — destroy SpriteSkin
                    var spriteSkin = sr.GetComponent<SpriteSkin>();
                    if (spriteSkin != null)
                    {
                        Object.DestroyImmediate(spriteSkin);
                        sr.sprite = null;
                    }

                    sr.sprite = newSprite;
                    createdSprites.Add(newSprite);
                    customCount++;
                    Plugin.Log.LogDebug($"[NpcPrefabBuilder] Custom sprite on '{kvp.Key}': {kvp.Value.ImagePath}");
                }
            }

            // ════════════════════════════════════════════════════════════
            //  7. VISIBILITY OVERRIDES (hide bones via SR.enabled)
            // ════════════════════════════════════════════════════════════
            foreach (var kvp in overrideDef.BoneOverrides)
            {
                if (kvp.Value.Visible) continue;
                if (overrideDef.RemovedBones.Contains(kvp.Key)) continue;
                if (srMap.TryGetValue(kvp.Key, out var sr))
                    sr.enabled = false;
            }

            // Note: Global offset is NOT baked here — it's applied per-frame by
            // CharacterOverrideDriver.ApplyGlobalOverrides to avoid double-application.

            // ════════════════════════════════════════════════════════════
            //  9. MODEL TINT + ALPHA (baked into SpriteRenderer colors)
            // ════════════════════════════════════════════════════════════
            Color modelTint = Color.white;
            bool hasModelTint = !string.IsNullOrEmpty(overrideDef.Model.TintHex) &&
                                ColorUtility.TryParseHtmlString(overrideDef.Model.TintHex, out modelTint);
            float modelAlpha = Mathf.Clamp01(overrideDef.Model.Alpha);
            if (hasModelTint || modelAlpha < 1f)
            {
                foreach (var kvp in srMap)
                {
                    Color c = hasModelTint ? modelTint : kvp.Value.color;
                    // Only override alpha when explicitly set (< 1). Preserves
                    // alpha baked into TintHex (e.g. "#FF000080").
                    if (modelAlpha < 1f) c.a = modelAlpha;
                    kvp.Value.color = c;
                }
            }

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] Built prefab for '{entityId}': " +
                $"{removedCount} removed, {customCount} custom");

            _builtPrefabs[entityId] = prefab;
            if (createdSprites.Count > 0)
                _prefabSprites[entityId] = createdSprites;
            return prefab;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ANIMATOR SPRITE CURVE STRIPPING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Strip sprite property curves from the Animator's clips for custom sprite
        /// bones. The Animator overwrites sr.sprite each frame via curves like
        /// "bone_1 : SpriteRenderer.m_Sprite". By removing these curves at build time,
        /// the custom sprite stays in place without per-frame re-stamping.
        ///
        /// Works by creating an AnimatorOverrideController with modified clip copies.
        /// </summary>
        private static void StripAnimatorSpriteCurves(GameObject prefab, IEnumerable<string> customSpriteBones)
        {
            var stripBones = new HashSet<string>(customSpriteBones);

            if (stripBones.Count == 0) return;

            // binding.path is a hierarchy path (e.g. "body/Head"), but stripBones
            // contains bare bone names (e.g. "Head"). Extract the leaf name.
            string LeafName(string path)
            {
                int slash = path.LastIndexOf('/');
                return slash >= 0 ? path.Substring(slash + 1) : path;
            }

            var animator = prefab.GetComponent<Animator>() ?? prefab.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return;

            var clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0) return;

            int totalStripped = 0;
            bool needsOverride = false;

            // Check if any clips have sprite curves targeting our bones
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                var bindings = UnityEditor_Stub.GetObjectReferenceCurveBindings(clip);
                if (bindings == null) continue;

                foreach (var binding in bindings)
                {
                    if (binding.propertyName == "m_Sprite" && stripBones.Contains(LeafName(binding.path)))
                    {
                        needsOverride = true;
                        break;
                    }
                }
                if (needsOverride) break;
            }

            if (!needsOverride)
            {
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] No Animator sprite curves found for grafted/custom bones — skipping strip");
                return;
            }

            // Always create a NEW override controller — Object.Instantiate does not
            // deep-clone the runtimeAnimatorController, so modifying a cast-in-place AOC
            // would mutate the shared source prefab's controller.
            var baseController = animator.runtimeAnimatorController;
            var overrideController = new AnimatorOverrideController(baseController);

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
            overrideController.GetOverrides(overrides);

            for (int i = 0; i < overrides.Count; i++)
            {
                var origClip = overrides[i].Value ?? overrides[i].Key;
                if (origClip == null) continue;

                var bindings = UnityEditor_Stub.GetObjectReferenceCurveBindings(origClip);
                if (bindings == null) continue;

                bool hasTargetCurves = false;
                foreach (var binding in bindings)
                {
                    if (binding.propertyName == "m_Sprite" && stripBones.Contains(LeafName(binding.path)))
                    { hasTargetCurves = true; break; }
                }

                if (!hasTargetCurves) continue;

                // Clone the clip and remove the offending curves
                var newClip = Object.Instantiate(origClip);
                newClip.name = origClip.name;

                foreach (var binding in bindings)
                {
                    if (binding.propertyName == "m_Sprite" && stripBones.Contains(LeafName(binding.path)))
                    {
                        // Set the curve to null/empty to remove it
                        UnityEditor_Stub.SetObjectReferenceCurve(newClip, binding, null);
                        totalStripped++;
                    }
                }

                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, newClip);
            }

            overrideController.ApplyOverrides(overrides);
            animator.runtimeAnimatorController = overrideController;

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] Stripped {totalStripped} Animator sprite curves from {stripBones.Count} bones");
        }

        /// <summary>
        /// Slim stub for AnimationUtility methods — at runtime we use reflection since
        /// UnityEditor namespace is unavailable. Only needs object reference curve
        /// bindings for sprite curve stripping.
        /// </summary>
        internal static class UnityEditor_Stub
        {
            private static System.Type _animUtilType;
            private static MethodInfo _getORCBindings;
            private static MethodInfo _setORCurve;
            private static bool _searched;

            private static void EnsureReflection()
            {
                if (_searched) return;
                _searched = true;

                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    _animUtilType = asm.GetType("UnityEditor.AnimationUtility");
                    if (_animUtilType != null) break;
                }

                if (_animUtilType != null)
                {
                    _getORCBindings = _animUtilType.GetMethod("GetObjectReferenceCurveBindings",
                        BindingFlags.Static | BindingFlags.Public);
                    _setORCurve = _animUtilType.GetMethod("SetObjectReferenceCurve",
                        BindingFlags.Static | BindingFlags.Public);
                }

                if (_getORCBindings == null)
                    Plugin.Log.LogDebug("[NpcPrefabBuilder] AnimationUtility not available — sprite curve stripping disabled");
            }

            public static EditorCurveBindingStub[] GetObjectReferenceCurveBindings(AnimationClip clip)
            {
                EnsureReflection();
                if (_getORCBindings == null) return null;

                try
                {
                    var result = _getORCBindings.Invoke(null, new object[] { clip });
                    if (result == null) return null;

                    var arr = result as System.Array;
                    if (arr == null || arr.Length == 0) return null;

                    var stubs = new EditorCurveBindingStub[arr.Length];
                    var pathField = arr.GetType().GetElementType()?.GetField("path");
                    var propField = arr.GetType().GetElementType()?.GetField("propertyName");

                    for (int i = 0; i < arr.Length; i++)
                    {
                        var binding = arr.GetValue(i);
                        stubs[i] = new EditorCurveBindingStub
                        {
                            path = pathField?.GetValue(binding) as string ?? "",
                            propertyName = propField?.GetValue(binding) as string ?? "",
                            _raw = binding
                        };
                    }
                    return stubs;
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"[NpcPrefabBuilder] GetObjectReferenceCurveBindings failed: {ex.Message}");
                    return null;
                }
            }

            public static void SetObjectReferenceCurve(AnimationClip clip, EditorCurveBindingStub binding, object[] keyframes)
            {
                EnsureReflection();
                if (_setORCurve == null) return;

                try
                {
                    _setORCurve.Invoke(null, new object[] { clip, binding._raw, keyframes });
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"[NpcPrefabBuilder] SetObjectReferenceCurve failed: {ex.Message}");
                }
            }

            public struct EditorCurveBindingStub
            {
                public string path;
                public string propertyName;
                public object _raw;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  UTILITIES
        // ═══════════════════════════════════════════════════════════════

        private static void DestroyPrefabSprites()
        {
            foreach (var list in _prefabSprites.Values)
                foreach (var s in list)
                    if (s != null && SpriteUtils.FindSpriteByName(s.name) != s)
                        Object.Destroy(s);
            _prefabSprites.Clear();
        }

        private static void DestroyPrefabSprites(string npcId)
        {
            if (_prefabSprites.TryGetValue(npcId, out var list))
            {
                foreach (var s in list)
                    if (s != null && SpriteUtils.FindSpriteByName(s.name) != s)
                        Object.Destroy(s);
                _prefabSprites.Remove(npcId);
            }
        }



        /// <summary>Clear the prefab cache (e.g. on zone reload).\n        /// NOTE: We do NOT destroy AnimatorOverrideController/clip assets here because\n        /// live combat instances share the same AOC via Object.Instantiate (which does\n        /// not deep-clone RuntimeAnimatorControllers). Destroying them while live\n        /// instances reference them causes MissingReferenceException. The AOC + clips\n        /// are UnityEngine.Objects that will be collected on scene unload.</summary>
        public static void ClearCache()
        {
            DestroyPrefabSprites();
            foreach (var kvp in _builtPrefabs)
            {
                if (kvp.Value != null)
                    Object.Destroy(kvp.Value);
            }
            _builtPrefabs.Clear();
        }

        /// <summary>Invalidate a single cached prefab so the next BuildCustomPrefab rebuilds it.\n        /// Does not destroy AOC/clip assets — see ClearCache comment.</summary>
        public static void InvalidateCache(string npcId)
        {
            if (_builtPrefabs.TryGetValue(npcId, out var old))
            {
                DestroyPrefabSprites(npcId);
                if (old != null)
                    Object.Destroy(old);
                _builtPrefabs.Remove(npcId);
            }
        }

    }
}
