using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Builds custom NPC GameObjectAnimated prefabs at zone registration time.
    /// This is the SINGLE source of truth for all one-time NPC model modifications:
    ///   - Removed bones (deactivate GameObjects)
    ///   - Added rig bones (empty GameObjects)
    ///   - Added sprites (GameObjects with SpriteRenderers)
    ///   - Auto-weight (distance-based vertex weight assignment for added bones)
    ///   - Sprite grafts (subtree clone from source NPC)
    ///   - Custom sprites (image swap from spritesheet/file)
    ///   - Visibility overrides (hide bones)
    ///   - Global offset (baked into prefab localPosition)
    ///   - Model tint + alpha (baked into SpriteRenderer colors)
    ///   - Shader effects (material swap via AllIn1SpriteShader)
    ///   - AnimatorOverrideController (clip swap from source NPC)
    ///
    /// NpcSpriteOverride handles ONLY per-frame LateUpdate work (transform overrides,
    /// animation keyframes, grafted sprite re-stamping, scale/flip).
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

        /// <summary>
        /// Build a custom prefab for an NPC with sprite overrides.
        /// Returns null if no prefab modifications are needed.
        /// The built prefab is a persistent (DontDestroyOnLoad) hidden GameObject.
        /// </summary>
        public static GameObject BuildCustomPrefab(string npcId, NPCData baseNpcData, SpriteOverrideDef overrideDef, string zoneId)
        {
            if (overrideDef == null) return null;

            // Check if ANY prefab-level modifications exist
            bool hasAny =
                overrideDef.Bones.Values.Any(b => !string.IsNullOrEmpty(b.SpriteFrom)) ||
                overrideDef.CustomSprites.Count > 0 ||
                overrideDef.RemovedBones.Count > 0 ||
                overrideDef.AddedBones.Count > 0 ||
                overrideDef.AddedSprites.Count > 0 ||
                overrideDef.Bones.Values.Any(b => !b.Visible) ||
                !string.IsNullOrEmpty(overrideDef.AnimationSource) ||
                overrideDef.UseShaderEffects ||
                !string.IsNullOrEmpty(overrideDef.ModelTintHex) ||
                overrideDef.ModelAlpha < 1f ||
                overrideDef.OffsetX != 0f || overrideDef.OffsetY != 0f;

            if (!hasAny)
            {
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] '{npcId}' has no prefab modifications — skipping build");
                return null;
            }

            // Return cached if already built
            if (_builtPrefabs.TryGetValue(npcId, out var cached) && cached != null)
            {
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] '{npcId}' returning cached prefab");
                return cached;
            }

            var sourcePrefab = baseNpcData.GameObjectAnimated;
            if (sourcePrefab == null)
            {
                Plugin.Log.LogWarning($"[NpcPrefabBuilder] '{npcId}' base NPC has no GameObjectAnimated prefab");
                return null;
            }

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] Building custom prefab for '{npcId}' (base='{baseNpcData.Id}')");

            // ── Clone the skeleton donor prefab ──
            var prefab = Object.Instantiate(sourcePrefab);
            prefab.name = $"{npcId}_customPrefab";
            prefab.SetActive(false); // hide from scene
            Object.DontDestroyOnLoad(prefab);

            // Build name→Transform and name→SpriteRenderer maps
            var boneMap = new Dictionary<string, Transform>();
            var srMap = new Dictionary<string, SpriteRenderer>();
            BoneHierarchyUtils.CollectBones(prefab.transform, boneMap, srMap);

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] Cloned prefab: {boneMap.Count} bones, {srMap.Count} SRs");

            // ════════════════════════════════════════════════════════════
            //  1. REMOVED BONES (first — skip these in all subsequent steps)
            // ════════════════════════════════════════════════════════════
            int removedCount = 0;
            foreach (var boneName in overrideDef.RemovedBones)
            {
                if (!boneMap.TryGetValue(boneName, out var bone)) continue;
                bone.gameObject.SetActive(false);
                srMap.Remove(boneName);
                removedCount++;
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] Deactivated bone '{boneName}'");
            }

            // ════════════════════════════════════════════════════════════
            //  2. ADDED RIG BONES (pure transform bones, no SpriteRenderer)
            // ════════════════════════════════════════════════════════════
            int addedBoneCount = 0;
            foreach (var kvp in overrideDef.AddedBones)
            {
                string boneName = kvp.Key;
                var boneDef = kvp.Value;
                Transform parentT = null;
                if (!string.IsNullOrEmpty(boneDef.ParentBone))
                    boneMap.TryGetValue(boneDef.ParentBone, out parentT);
                if (parentT == null)
                {
                    Plugin.Log.LogWarning($"[NpcPrefabBuilder] AddedBone '{boneName}': parent '{boneDef.ParentBone}' not found");
                    continue;
                }

                var go = new GameObject(boneName);
                go.transform.SetParent(parentT, false);
                go.transform.localPosition = new Vector3(boneDef.PosX, boneDef.PosY, 0f);
                go.transform.localEulerAngles = new Vector3(0, 0, boneDef.Rotation);
                go.transform.localScale = new Vector3(boneDef.ScaleX, boneDef.ScaleY, 1f);

                boneMap[boneName] = go.transform;
                addedBoneCount++;
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] Added rig bone '{boneName}' on '{boneDef.ParentBone}'");
            }

            // ════════════════════════════════════════════════════════════
            //  3. ADDED SPRITES (GameObjects with SpriteRenderers)
            // ════════════════════════════════════════════════════════════
            int addedSpriteCount = 0;
            foreach (var kvp in overrideDef.AddedSprites)
            {
                string name = kvp.Key;
                var def = kvp.Value;
                Transform parentT = null;
                if (!string.IsNullOrEmpty(def.ParentBone))
                    boneMap.TryGetValue(def.ParentBone, out parentT);
                if (parentT == null)
                {
                    Plugin.Log.LogWarning($"[NpcPrefabBuilder] AddedSprite '{name}': parent bone '{def.ParentBone}' not found");
                    continue;
                }

                var go = new GameObject(name);
                go.transform.SetParent(parentT, false);
                var sr = go.AddComponent<SpriteRenderer>();
                if (srMap.TryGetValue(def.ParentBone, out var parentSR))
                    sr.sortingLayerID = parentSR.sortingLayerID;

                srMap[name] = sr;
                boneMap[name] = go.transform;
                addedSpriteCount++;
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] Added sprite '{name}' on bone '{def.ParentBone}'");
            }

            // ════════════════════════════════════════════════════════════
            //  4. AUTO-WEIGHTS (distance-based vertex weights for added bones)
            // ════════════════════════════════════════════════════════════
            foreach (var kvp in overrideDef.AddedBones)
            {
                string boneName = kvp.Key;
                var boneDef = kvp.Value;
                if (boneDef.InfluenceSprites == null || boneDef.InfluenceSprites.Count == 0) continue;
                if (!boneMap.TryGetValue(boneName, out var boneTransform)) continue;

                foreach (var spriteBoneName in boneDef.InfluenceSprites)
                {
                    if (!srMap.TryGetValue(spriteBoneName, out var sr) || sr.sprite == null) continue;
                    try { AddBoneInfluenceToSprite(sr, boneTransform, boneName, spriteBoneName, boneDef); }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.LogError($"[NpcPrefabBuilder] AutoWeight failed for bone '{boneName}' on sprite '{spriteBoneName}': {ex}");
                    }
                }
            }

            // ════════════════════════════════════════════════════════════
            //  5. SPRITE GRAFTS (subtree clone from source NPC)
            // ════════════════════════════════════════════════════════════
            int graftCount = 0;
            foreach (var kvp in overrideDef.Bones)
            {
                if (string.IsNullOrEmpty(kvp.Value.SpriteFrom)) continue;
                if (overrideDef.RemovedBones.Contains(kvp.Key)) continue;
                if (!srMap.TryGetValue(kvp.Key, out var sr)) continue;

                string sourceNpc, sourceBone;
                int slash = kvp.Value.SpriteFrom.IndexOf('/');
                if (slash >= 0)
                {
                    sourceNpc = kvp.Value.SpriteFrom.Substring(0, slash);
                    sourceBone = kvp.Value.SpriteFrom.Substring(slash + 1);
                }
                else
                {
                    sourceNpc = kvp.Value.SpriteFrom;
                    sourceBone = kvp.Key;
                }

                var sprites = SpriteEditor.ExtractNpcSprites(sourceNpc);
                if (sprites.TryGetValue(sourceBone, out var sprite))
                {
                    var spriteSkin = sr.GetComponent<Component>();
                    bool hasSpriteSkin = false;
                    if (spriteSkin != null && spriteSkin.GetType().Name == "SpriteSkin")
                    {
                        hasSpriteSkin = true;
                        bool grafted = TryGraftBoneSubtree(sr.gameObject, sourceNpc, sourceBone, kvp.Key, boneMap);
                        if (!grafted)
                        {
                            Object.DestroyImmediate(spriteSkin);
                            sr.sprite = null;
                        }
                    }

                    sr.sprite = sprite;
                    graftCount++;
                    Plugin.Log.LogDebug($"[NpcPrefabBuilder] Grafted '{kvp.Key}' <- '{sourceNpc}/{sourceBone}' (SpriteSkin={hasSpriteSkin})");
                }
                else
                {
                    Plugin.Log.LogWarning($"[NpcPrefabBuilder] Graft failed: bone '{sourceBone}' not found in '{sourceNpc}'");
                }
            }

            // ════════════════════════════════════════════════════════════
            //  6. CUSTOM SPRITES (image swap from spritesheet/file)
            // ════════════════════════════════════════════════════════════
            int customCount = 0;
            foreach (var kvp in overrideDef.CustomSprites)
            {
                if (overrideDef.RemovedBones.Contains(kvp.Key)) continue;
                if (!srMap.TryGetValue(kvp.Key, out var sr)) continue;
                var newSprite = SpriteEditor.CreateSpriteFromDef(kvp.Value, zoneId, overrideDef.Spritesheet, sr.sprite);
                if (newSprite != null)
                {
                    var spriteSkin = sr.GetComponent<Component>();
                    if (spriteSkin != null && spriteSkin.GetType().Name == "SpriteSkin")
                    {
                        Object.DestroyImmediate(spriteSkin);
                        sr.sprite = null;
                    }

                    sr.sprite = newSprite;
                    customCount++;
                    Plugin.Log.LogDebug($"[NpcPrefabBuilder] Custom sprite on '{kvp.Key}': {kvp.Value.ImagePath}");
                }
            }

            // ════════════════════════════════════════════════════════════
            //  7. VISIBILITY OVERRIDES (hide bones via SR.enabled)
            // ════════════════════════════════════════════════════════════
            foreach (var kvp in overrideDef.Bones)
            {
                if (kvp.Value.Visible) continue;
                if (overrideDef.RemovedBones.Contains(kvp.Key)) continue;
                if (srMap.TryGetValue(kvp.Key, out var sr))
                    sr.enabled = false;
            }

            // ════════════════════════════════════════════════════════════
            //  8. GLOBAL OFFSET (baked into prefab localPosition)
            // ════════════════════════════════════════════════════════════
            if (overrideDef.OffsetX != 0f || overrideDef.OffsetY != 0f)
            {
                prefab.transform.localPosition += new Vector3(overrideDef.OffsetX, overrideDef.OffsetY, 0f);
            }

            // ════════════════════════════════════════════════════════════
            //  9. MODEL TINT + ALPHA (baked into SpriteRenderer colors)
            // ════════════════════════════════════════════════════════════
            Color modelTint = Color.white;
            bool hasModelTint = !string.IsNullOrEmpty(overrideDef.ModelTintHex) &&
                                ColorUtility.TryParseHtmlString(overrideDef.ModelTintHex, out modelTint);
            float modelAlpha = Mathf.Clamp01(overrideDef.ModelAlpha);
            if (hasModelTint || modelAlpha < 1f)
            {
                foreach (var kvp in srMap)
                {
                    Color c = hasModelTint ? modelTint : Color.white;
                    c.a = modelAlpha;
                    kvp.Value.color = c;
                }
            }

            // ════════════════════════════════════════════════════════════
            //  10. SHADER EFFECTS (AllIn1SpriteShader material swap)
            // ════════════════════════════════════════════════════════════
            if (overrideDef.UseShaderEffects)
            {
                SpriteEditor.ApplyShaderEffectsToRenderers(srMap.Values, overrideDef);
            }

            // ════════════════════════════════════════════════════════════
            //  11. ANIMATOR OVERRIDE CONTROLLER
            // ════════════════════════════════════════════════════════════
            if (!string.IsNullOrEmpty(overrideDef.AnimationSource))
            {
                ApplyAnimatorOverride(prefab, overrideDef.AnimationSource);
            }

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] Built prefab for '{npcId}': " +
                $"{removedCount} removed, {addedBoneCount} added bones, {addedSpriteCount} added sprites, " +
                $"{graftCount} grafts, {customCount} custom" +
                $"{(!string.IsNullOrEmpty(overrideDef.AnimationSource) ? $", anim from '{overrideDef.AnimationSource}'" : "")}");

            _builtPrefabs[npcId] = prefab;
            return prefab;
        }

        // ═══════════════════════════════════════════════════════════════
        //  AUTO-WEIGHT: distance-based vertex weight assignment
        // ═══════════════════════════════════════════════════════════════

        private static void AddBoneInfluenceToSprite(SpriteRenderer sr, Transform newBoneT, string newBoneName, string spriteBoneName, AddedBoneDef boneDef)
        {
            var spriteSkin = sr.gameObject.GetComponent<SpriteSkin>();
            if (spriteSkin == null)
            {
                Plugin.Log.LogDebug($"[NpcPrefabBuilder] AutoWeight: No SpriteSkin on '{spriteBoneName}', skipping");
                return;
            }

            Transform[] currentBones = spriteSkin.boneTransforms;
            if (currentBones == null || currentBones.Length == 0) return;

            for (int i = 0; i < currentBones.Length; i++)
                if (currentBones[i] != null && currentBones[i].name == newBoneName) return;

            int newBoneIdx = currentBones.Length;

            var expandedBones = new Transform[newBoneIdx + 1];
            System.Array.Copy(currentBones, expandedBones, currentBones.Length);
            expandedBones[newBoneIdx] = newBoneT;

            var btProp = typeof(SpriteSkin).GetProperty("boneTransforms",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            btProp?.SetValue(spriteSkin, expandedBones);

            Sprite sprite = sr.sprite;
            SpriteBone[] existingBones = sprite.GetBones();

            int parentIdx = -1;
            if (!string.IsNullOrEmpty(boneDef.ParentBone))
            {
                for (int i = 0; i < existingBones.Length; i++)
                    if (existingBones[i].name == boneDef.ParentBone) { parentIdx = i; break; }
            }

            var newBones = new SpriteBone[existingBones.Length + 1];
            System.Array.Copy(existingBones, newBones, existingBones.Length);
            newBones[existingBones.Length] = new SpriteBone
            {
                name = newBoneName,
                position = new Vector3(newBoneT.localPosition.x, newBoneT.localPosition.y, 0f),
                rotation = newBoneT.localRotation,
                length = boneDef.Length,
                parentId = parentIdx
            };
            sprite.SetBones(newBones);

            using (var oldPoses = new NativeArray<Matrix4x4>(sprite.GetBindPoses().ToArray(), Allocator.Temp))
            {
                var newPoses = new NativeArray<Matrix4x4>(oldPoses.Length + 1, Allocator.Temp);
                NativeArray<Matrix4x4>.Copy(oldPoses, newPoses, oldPoses.Length);
                newPoses[oldPoses.Length] = newBoneT.worldToLocalMatrix;
                sprite.SetBindPoses(newPoses);
                newPoses.Dispose();
            }

            AssignDistanceWeights(sr, newBoneT, newBoneIdx, existingBones.Length, boneDef.WeightRadius, boneDef.WeightFalloff);

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] AutoWeight: bone '{newBoneName}' added to sprite '{spriteBoneName}' at idx={newBoneIdx}");
        }

        private static void AssignDistanceWeights(SpriteRenderer sr, Transform boneT, int newBoneIdx, int oldBoneCount, float radius, float falloff)
        {
            Sprite sprite = sr.sprite;
            if (sprite == null) return;

            int vertCount = sprite.GetVertexCount();
            if (vertCount == 0) return;

            NativeSlice<Vector3> positions;
            try { positions = sprite.GetVertexAttribute<Vector3>(UnityEngine.Rendering.VertexAttribute.Position); }
            catch { Plugin.Log.LogWarning("[NpcPrefabBuilder] AutoWeight: GetVertexAttribute<Position> failed"); return; }

            NativeSlice<BoneWeight1> weights;
            try { weights = sprite.GetVertexAttribute<BoneWeight1>(UnityEngine.Rendering.VertexAttribute.BlendWeight); }
            catch { Plugin.Log.LogWarning("[NpcPrefabBuilder] AutoWeight: GetVertexAttribute<BlendWeight> failed"); return; }

            int weightsPerVert = (weights.Length > 0 && vertCount > 0) ? weights.Length / vertCount : 0;
            if (weightsPerVert == 0) return;

            Vector3 boneWorldPos = boneT.position;
            Vector3 boneLocalPos = sr.transform.InverseTransformPoint(boneWorldPos);
            Vector2 bonePos2D = new Vector2(boneLocalPos.x, boneLocalPos.y);

            int affected = 0;
            for (int v = 0; v < vertCount; v++)
            {
                Vector2 vpos = new Vector2(positions[v].x, positions[v].y);
                float dist = Vector2.Distance(vpos, bonePos2D);
                if (dist > radius) continue;

                float influence = Mathf.Pow(1f - Mathf.Clamp01(dist / Mathf.Max(radius, 0.001f)), Mathf.Max(falloff, 0.01f));
                if (influence < 0.01f) continue;
                affected++;
                influence = Mathf.Min(influence, 0.7f);

                int baseIdx = v * weightsPerVert;

                int minSlot = -1;
                float minWeight = float.MaxValue;
                for (int w = 0; w < weightsPerVert; w++)
                {
                    var bw = weights[baseIdx + w];
                    bool isAddedBone = bw.boneIndex >= oldBoneCount && bw.weight > 0.001f;
                    if (isAddedBone) continue;
                    if (bw.weight < minWeight) { minWeight = bw.weight; minSlot = w; }
                }
                if (minSlot < 0)
                {
                    for (int w = 0; w < weightsPerVert; w++)
                    {
                        if (weights[baseIdx + w].weight < minWeight)
                        { minWeight = weights[baseIdx + w].weight; minSlot = w; }
                    }
                }
                if (minSlot < 0) continue;

                float scale = Mathf.Max(0f, 1f - influence);
                for (int w = 0; w < weightsPerVert; w++)
                {
                    if (w == minSlot) continue;
                    var bw = weights[baseIdx + w];
                    bw.weight *= scale;
                    weights[baseIdx + w] = bw;
                }

                weights[baseIdx + minSlot] = new BoneWeight1 { boneIndex = newBoneIdx, weight = influence };

                float total = 0f;
                for (int w = 0; w < weightsPerVert; w++)
                    total += weights[baseIdx + w].weight;
                if (total > 0.001f && Mathf.Abs(total - 1f) > 0.001f)
                {
                    float norm = 1f / total;
                    for (int w = 0; w < weightsPerVert; w++)
                    {
                        var bw = weights[baseIdx + w];
                        bw.weight *= norm;
                        weights[baseIdx + w] = bw;
                    }
                }
            }

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] AutoWeight: sprite '{sprite.name}' — {affected}/{vertCount} vertices influenced");
        }

        // ═══════════════════════════════════════════════════════════════
        //  ANIMATOR OVERRIDE
        // ═══════════════════════════════════════════════════════════════

        private static void ApplyAnimatorOverride(GameObject prefab, string sourceNpcId)
        {
            var animator = prefab.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Plugin.Log.LogWarning($"[NpcPrefabBuilder] Cannot apply AnimatorOverride: prefab has no Animator/Controller");
                return;
            }

            NPCData sourceNpc = ZoneLoader.Npcs.TryGetValue(sourceNpcId, out var ours)
                ? ours : DataHelper.GetExistingNPC(sourceNpcId);
            if (sourceNpc?.GameObjectAnimated == null)
            {
                Plugin.Log.LogWarning($"[NpcPrefabBuilder] AnimationSource NPC '{sourceNpcId}' has no GameObjectAnimated");
                return;
            }

            var sourceAnimator = sourceNpc.GameObjectAnimated.GetComponent<Animator>();
            if (sourceAnimator?.runtimeAnimatorController == null)
            {
                Plugin.Log.LogWarning($"[NpcPrefabBuilder] AnimationSource NPC '{sourceNpcId}' has no AnimatorController");
                return;
            }

            var baseController = animator.runtimeAnimatorController;
            var overrideController = new AnimatorOverrideController(baseController);

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
            overrideController.GetOverrides(overrides);

            var sourceClips = new Dictionary<string, AnimationClip>();
            foreach (var clip in sourceAnimator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && !sourceClips.ContainsKey(clip.name))
                    sourceClips[clip.name] = clip;
            }

            int swapped = 0;
            for (int i = 0; i < overrides.Count; i++)
            {
                var original = overrides[i].Key;
                if (original == null) continue;

                if (sourceClips.TryGetValue(original.name, out var replacement))
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, replacement);
                    swapped++;
                }
                else
                {
                    string[] keywords = { "idle", "attack", "cast", "hit" };
                    foreach (var kw in keywords)
                    {
                        if (!original.name.ToLower().Contains(kw)) continue;
                        var match = sourceClips.FirstOrDefault(c => c.Key.ToLower().Contains(kw));
                        if (match.Value != null)
                        {
                            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, match.Value);
                            swapped++;
                            break;
                        }
                    }
                }
            }

            overrideController.ApplyOverrides(overrides);
            animator.runtimeAnimatorController = overrideController;

            Plugin.Log.LogInfo($"[NpcPrefabBuilder] AnimatorOverrideController: {swapped}/{overrides.Count} clips swapped from '{sourceNpcId}'");
        }

        // ═══════════════════════════════════════════════════════════════
        //  BONE SUBTREE GRAFT
        // ═══════════════════════════════════════════════════════════════

        private static bool TryGraftBoneSubtree(GameObject targetBone, string sourceNpcId, string sourceBoneName, string targetBoneName, Dictionary<string, Transform> targetBoneMap)
        {
            NPCData npcData = ZoneLoader.Npcs.TryGetValue(sourceNpcId, out var ours)
                ? ours : DataHelper.GetExistingNPC(sourceNpcId);
            if (npcData?.GameObjectAnimated == null) return false;

            var tempPrefab = Object.Instantiate(npcData.GameObjectAnimated);
            tempPrefab.SetActive(false);

            try
            {
                var sourceBone = BoneHierarchyUtils.FindRecursive(tempPrefab.transform, sourceBoneName);
                if (sourceBone == null) return false;

                var spriteSkin = targetBone.GetComponents<Component>()
                    .FirstOrDefault(c => c.GetType().Name == "SpriteSkin");
                if (spriteSkin == null) return false;

                var skinType = spriteSkin.GetType();
                var boneTransformsProp = skinType.GetProperty("boneTransforms",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var rootBoneProp = skinType.GetProperty("rootBone",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (boneTransformsProp == null || rootBoneProp == null) return false;

                var currentBoneTransforms = boneTransformsProp.GetValue(spriteSkin) as Transform[];
                if (currentBoneTransforms == null || currentBoneTransforms.Length == 0) return false;

                var sourceSpriteSkin = sourceBone.GetComponents<Component>()
                    .FirstOrDefault(c => c.GetType().Name == "SpriteSkin");
                if (sourceSpriteSkin == null) return false;

                var sourceBoneTransforms = boneTransformsProp.GetValue(sourceSpriteSkin) as Transform[];
                var sourceRootBone = rootBoneProp.GetValue(sourceSpriteSkin) as Transform;
                if (sourceBoneTransforms == null || sourceRootBone == null) return false;

                var cloneMap = new Dictionary<string, Transform>();
                var targetParent = targetBone.transform.parent ?? targetBone.transform;

                if (!targetBoneMap.ContainsKey(sourceRootBone.name))
                {
                    var clonedRoot = new GameObject(sourceRootBone.name).transform;
                    clonedRoot.SetParent(targetParent);
                    clonedRoot.localPosition = sourceRootBone.localPosition;
                    clonedRoot.localRotation = sourceRootBone.localRotation;
                    clonedRoot.localScale = sourceRootBone.localScale;
                    cloneMap[sourceRootBone.name] = clonedRoot;
                }
                else
                {
                    cloneMap[sourceRootBone.name] = targetBoneMap[sourceRootBone.name];
                }

                foreach (var srcBt in sourceBoneTransforms)
                {
                    if (srcBt == null) continue;
                    if (cloneMap.ContainsKey(srcBt.name)) continue;
                    if (targetBoneMap.ContainsKey(srcBt.name))
                    {
                        cloneMap[srcBt.name] = targetBoneMap[srcBt.name];
                        continue;
                    }

                    var cloned = new GameObject(srcBt.name).transform;
                    cloned.SetParent(cloneMap.TryGetValue(sourceRootBone.name, out var r) ? r : targetParent);
                    cloned.localPosition = srcBt.localPosition;
                    cloned.localRotation = srcBt.localRotation;
                    cloned.localScale = srcBt.localScale;
                    cloneMap[srcBt.name] = cloned;
                }

                var newBoneTransforms = new Transform[sourceBoneTransforms.Length];
                for (int i = 0; i < sourceBoneTransforms.Length; i++)
                {
                    if (sourceBoneTransforms[i] == null) continue;
                    cloneMap.TryGetValue(sourceBoneTransforms[i].name, out newBoneTransforms[i]);
                }

                rootBoneProp.SetValue(spriteSkin, cloneMap[sourceRootBone.name]);
                boneTransformsProp.SetValue(spriteSkin, newBoneTransforms);

                try
                {
                    var utilType = skinType.Assembly.GetType("UnityEngine.U2D.Animation.SpriteSkinUtility");
                    var resetMethod = utilType?.GetMethod("ResetBindPose",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    resetMethod?.Invoke(null, new object[] { spriteSkin });
                }
                catch { /* ResetBindPose not critical */ }

                Plugin.Log.LogInfo($"[NpcPrefabBuilder] Subtree graft on '{targetBoneName}': cloned {cloneMap.Count} bones from '{sourceNpcId}/{sourceBoneName}'");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(tempPrefab);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  UTILITIES
        // ═══════════════════════════════════════════════════════════════



        /// <summary>Clear the prefab cache (e.g. on zone reload).</summary>
        public static void ClearCache()
        {
            foreach (var kvp in _builtPrefabs)
            {
                if (kvp.Value != null)
                    Object.Destroy(kvp.Value);
            }
            _builtPrefabs.Clear();
        }

        /// <summary>Invalidate a single cached prefab so the next BuildCustomPrefab rebuilds it.</summary>
        public static void InvalidateCache(string npcId)
        {
            if (_builtPrefabs.TryGetValue(npcId, out var old))
            {
                if (old != null) Object.Destroy(old);
                _builtPrefabs.Remove(npcId);
            }
        }
    }
}
