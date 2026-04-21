using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    public partial class SpriteSkinEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  PREVIEW MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        // Temporary state for CollectHandles — set before collection, cleared after
        private Dictionary<string, int> _graftLookup;
        private HashSet<string> _replacedBones;

        private void SpawnPreview(string spriteDefId, string baseSprite)
        {
            DestroyPreview();
            _previewNpcId = spriteDefId;

            GameObject prefab = null;

            if (ActiveMode == EditorMode.HeroSkin)
            {
                // Hero Skin mode: resolve from SkinData.SkinGo
                prefab = ResolveHeroSkinPrefab(spriteDefId, baseSprite);
            }
            else if (ActiveMode == EditorMode.Item)
            {
                // Item mode: resolve from CardData.PetModel
                prefab = ResolvePetPrefab(spriteDefId, baseSprite);
            }
            else
            {
                // NPC mode: resolve from NPCData.GameObjectAnimated
                prefab = ResolveNpcPrefab(spriteDefId, baseSprite);
            }

            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[SpriteSkinEditor] Cannot resolve animated prefab for '{spriteDefId}' (base='{baseSprite}', mode={ActiveMode})");
                // Keep _previewNpcId so the BaseSprite picker is shown
                return;
            }

            _previewGO = Object.Instantiate(prefab, PreviewOrigin, Quaternion.identity);
            _previewGO.name = $"[SpriteSkinEditor] {spriteDefId}";
            _previewGO.SetActive(true);
            SetLayerRecursive(_previewGO.transform, PreviewLayer);

            // Capture Animator and enumerate clips
            _previewAnimator = _previewGO.GetComponentInChildren<Animator>();
            _animPlaying = false;
            _animSpeed = 1f;
            _selectedClipIdx = 0;
            _clipNames = null;
            _clipLengths = null;
            _timelineNormTime = 0f;
            _timelineDragging = false;

            if (_previewAnimator != null)
            {
                _previewAnimator.enabled = false;
                if (_previewAnimator.runtimeAnimatorController != null)
                {
                    var clips = _previewAnimator.runtimeAnimatorController.animationClips;
                    if (clips != null && clips.Length > 0)
                    {
                        _clips = clips;
                        _clipNames = new string[clips.Length];
                        _clipLengths = new float[clips.Length];
                        int idleIdx = -1;
                        for (int i = 0; i < clips.Length; i++)
                        {
                            _clipNames[i] = clips[i].name;
                            _clipLengths[i] = clips[i].length;
                            if (clips[i].name.ToLower().Contains("idle") && idleIdx < 0)
                                idleIdx = i;
                        }
                        // Default to idle clip if found
                        _selectedClipIdx = idleIdx >= 0 ? idleIdx : 0;

                        // Show the first frame of the selected clip via Animator
                        _playbackTime = 0f;
                        _timelineNormTime = 0f;
                        _previewAnimator.enabled = true;
                        _previewAnimator.speed = 0f;
                        _previewAnimator.Play(
                            Animator.StringToHash(_clips[_selectedClipIdx].name), 0, 0f);
                        _previewAnimator.Update(0f);
                    }
                }
            }

            // Record rest pose before overrides
            _restPos.Clear(); _restRot.Clear(); _restScale.Clear();
            RecordRestPose(_previewGO.transform);

            // Collect bone handles (flat list with depth)
            _handles.Clear();
            CollectHandles(_previewGO.transform, "", 0);

            // Apply existing overrides so visual matches DTO state
            RefreshPreviewOverrides();

            _selectedBone = null;
            _selectedBonePath = null;
            _zoom = 2.5f;
            _pan = Vector2.zero;
            Plugin.Log.LogInfo($"[SpriteSkinEditor] Spawned preview for '{spriteDefId}': {_handles.Count} bones");
        }

        /// <summary>Resolve NPC animated prefab from NPCData.</summary>
        private GameObject ResolveNpcPrefab(string spriteDefId, string baseSprite)
        {
            NPCData npcData = null;
            var npcDict = GetNpcDict();
            if (npcDict != null)
            {
                foreach (var kvp in npcDict)
                {
                    if (kvp.Value.SpriteSource == spriteDefId)
                    {
                        var data = DataHelper.GetExistingNPC(kvp.Key);
                        if (data != null) { npcData = data; break; }
                    }
                }
            }
            if (npcData == null && !string.IsNullOrEmpty(baseSprite))
                npcData = DataHelper.GetExistingNPC(baseSprite);
            return npcData?.GameObjectAnimated;
        }

        /// <summary>Resolve Hero Skin animated prefab from SkinData.SkinGo.</summary>
        private GameObject ResolveHeroSkinPrefab(string spriteDefId, string baseSprite)
        {
            // Try direct skin lookup first
            if (!string.IsNullOrEmpty(baseSprite))
            {
                var skinData = DataHelper.GetSkin(baseSprite);
                if (skinData?.SkinGo != null) return skinData.SkinGo;
            }
            // Fall back to spriteDefId as a skin ID
            if (!string.IsNullOrEmpty(spriteDefId))
            {
                var skinData = DataHelper.GetSkin(spriteDefId);
                if (skinData?.SkinGo != null) return skinData.SkinGo;
            }
            // Last resort: try as NPC (for cross-type grafting base)
            return ResolveNpcPrefab(spriteDefId, baseSprite);
        }

        /// <summary>Resolve pet animated prefab from CardData.PetModel.</summary>
        private GameObject ResolvePetPrefab(string spriteDefId, string baseSprite)
        {
            // Try baseSprite as a card ID with a pet model
            if (!string.IsNullOrEmpty(baseSprite))
            {
                var card = DataHelper.GetCard(baseSprite);
                if (card?.PetModel != null) return card.PetModel;
            }
            // Try spriteDefId as a card ID
            if (!string.IsNullOrEmpty(spriteDefId))
            {
                var card = DataHelper.GetCard(spriteDefId);
                if (card?.PetModel != null) return card.PetModel;
            }
            // Fall back to NPC lookup (pet skeletons may reference NPCs)
            return ResolveNpcPrefab(spriteDefId, baseSprite);
        }

        public void DestroyPreview()
        {
            if (_previewGO != null)
            {
                Object.Destroy(_previewGO);
                _previewGO = null;
            }
            _previewNpcId = null;
            _handles.Clear();
            _restPos.Clear(); _restRot.Clear(); _restScale.Clear(); _restSprites.Clear();
            _basePreviewAlpha.Clear();
            _restMaterials.Clear();
            _skinRootRest.Clear();
            _edBoneMap.Clear(); _edSrMap.Clear(); _edSkinRootMap.Clear();
            _edBaseSortOrder.Clear(); _edBaseFlipX.Clear(); _edBaseFlipY.Clear();
            _edBaseAlpha.Clear();
            _edCustomSpriteCache.Clear();
            _selectedBone = null;
            _selectedBonePath = null;
            _dragging = false;
            _previewAnimator = null;
            _clipNames = null;
            _clipLengths = null;
            _clips = null;
            _animPlaying = false;
            _triggerMode = false;
            _playbackTime = 0f;
            _timelineNormTime = 0f;
            _timelineDragging = false;
            CleanupGraftPreviewObjects();
        }



        private void CleanupGraftPreviewObjects()
        {
            foreach (var go in _graftPreviewObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _graftPreviewObjects.Clear();
            _graftAnimators.Clear();
            _graftAlignments.Clear();
            _graftBaseSortOrder.Clear();
            _graftBaseFlipX.Clear();
            _graftBaseFlipY.Clear();
            _graftBaseAlpha.Clear();
            _graftCustomSpriteCache.Clear();
            _graftAncestorRest.Clear();
            _graftStateMaps.Clear();
        }

        private void RecordRestPose(Transform root, string parentPath = "")
        {
            foreach (Transform child in root)
            {
                string path = string.IsNullOrEmpty(parentPath) ? child.name : parentPath + "/" + child.name;
                _restPos[path] = child.localPosition;
                _restRot[path] = child.localEulerAngles.z;
                _restScale[path] = child.localScale;
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (sr.sprite != null)
                        _restSprites[path] = sr.sprite;
                    if (sr.sharedMaterial != null)
                        _restMaterials[path] = sr.sharedMaterial;
                    _basePreviewAlpha[path] = sr.color.a;
                    _restSortingOrder[path] = sr.sortingOrder;
                    _restFlipX[path] = sr.flipX;
                    _restFlipY[path] = sr.flipY;
                }
                RecordRestPose(child, path);
            }
        }

        /// <summary>Record rest poses for graft clone bones using the current handle list.
        /// Must be called after CollectHandles (which populates handle paths) and before
        /// overrides are applied, so that bone transforms are at their prefab defaults.</summary>
        private void RecordGraftRestPoses()
        {
            foreach (var h in _handles)
            {
                if (h.GraftIndex < 0 || h.Transform == null) continue;
                _restPos[h.Path] = h.Transform.localPosition;
                _restRot[h.Path] = h.Transform.localEulerAngles.z;
                _restScale[h.Path] = h.Transform.localScale;
                var sr = h.Transform.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (sr.sprite != null) _restSprites[h.Path] = sr.sprite;
                    if (sr.sharedMaterial != null) _restMaterials[h.Path] = sr.sharedMaterial;
                    _basePreviewAlpha[h.Path] = sr.color.a;
                    _restSortingOrder[h.Path] = sr.sortingOrder;
                    _restFlipX[h.Path] = sr.flipX;
                    _restFlipY[h.Path] = sr.flipY;
                }
            }
        }

        private void CollectHandles(Transform root, string parentPath, int depth)
        {
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                string path = string.IsNullOrEmpty(parentPath) ? child.name : parentPath + "/" + child.name;

                // Determine the controlling rig bone for SpriteSkin sprites.
                // SpriteSkin vertex deformation cancels the sprite GO's transform,
                // so pos/rot/scale/flip overrides must target the rig bone instead.
                string skinRoot = null;
                Transform skinRootTransform = null;
                var skin = child.GetComponent<SpriteSkin>();
                if (skin != null)
                {
                    var ctrlBone = BoneHierarchyUtils.GetControllingBone(skin);
                    if (ctrlBone != null && ctrlBone != child)
                    {
                        skinRoot = ctrlBone.name;
                        skinRootTransform = ctrlBone;
                        // Record rest pose for this bone (it may not be a handle itself)
                        if (!_skinRootRest.ContainsKey(skinRootTransform))
                            _skinRootRest[skinRootTransform] = (skinRootTransform.localPosition, skinRootTransform.localEulerAngles.z, skinRootTransform.localScale);
                    }
                    else if (ctrlBone == null && skin.rootBone != null && skin.rootBone != child)
                    {
                        // Fallback: redirect to self so flip uses negative local scale
                        // rather than sr.flipX (which SpriteSkin overrides).
                        skinRootTransform = child;
                    }
                }

                // Graft clone containers (GraftPreview~X): skip the container itself,
                // integrate visible graft content as siblings at this depth level.
                if (child.name.StartsWith("GraftPreview~") || child.name.StartsWith("GraftPuppet~"))
                {
                    string targetBone = child.name.Contains("~")
                        ? child.name.Substring(child.name.IndexOf('~') + 1) : null;
                    int graftIdx = -1;
                    if (targetBone != null && _graftLookup != null)
                        _graftLookup.TryGetValue(targetBone, out graftIdx);

                    // Collect the graft's visible sprites and their rig bones at this depth
                    CollectGraftContentHandles(child, path, depth, graftIdx, targetBone, root.name, i == childCount - 1);
                    continue;
                }

                // Host bones replaced by a graft with ReplaceTarget=true: skip from tree
                // but still recurse into children (the GraftPreview~ container lives there).
                if (_replacedBones != null && _replacedBones.Contains(child.name))
                {
                    if (child.childCount > 0)
                        CollectHandles(child, path, depth);
                    continue;
                }

                _handles.Add(new BoneHandle
                {
                    Name = child.name,
                    Path = path,
                    Transform = child,
                    Depth = depth,
                    HasSpriteRenderer = child.GetComponent<SpriteRenderer>() != null,
                    ParentName = depth > 0 ? root.name : null,
                    IsLastChild = (i == childCount - 1),
                    SkinRootBone = skinRoot,
                    SkinRootTransform = skinRootTransform,
                });

                if (child.childCount > 0)
                {
                    CollectHandles(child, path, depth + 1);
                }
            }
        }

        /// <summary>Collect the visible content of a graft clone (sprites + their rig bones)
        /// and integrate them directly into the parent's tree level, hiding the GraftPreview container.
        /// The grafted sprite appears as a sibling of the host target bone's parent.</summary>
        private void CollectGraftContentHandles(Transform graftRoot, string parentPath, int depth,
            int graftIdx, string targetBone, string parentName, bool isLastChild)
        {
            // Find the primary grafted sprite (enabled SR) and its rig bone subtree
            var visibleSprites = new List<Transform>();
            var rigRoot = (Transform)null;

            foreach (Transform child in graftRoot)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && sr.enabled)
                    visibleSprites.Add(child);

                // The rig root is typically named bone_1 (first non-sprite child with children)
                if (rigRoot == null && sr == null && child.childCount > 0)
                    rigRoot = child;
            }

            // Gather bone transforms referenced by SpriteSkin on visible sprites
            var allSkinBones = new HashSet<Transform>();
            foreach (var sprT in visibleSprites)
            {
                var spriteSkin = sprT.GetComponent<SpriteSkin>();
                if (spriteSkin == null) continue;
                if (spriteSkin.boneTransforms != null)
                    foreach (var b in spriteSkin.boneTransforms)
                        if (b != null) allSkinBones.Add(b);
                if (spriteSkin.rootBone != null)
                    allSkinBones.Add(spriteSkin.rootBone);
            }

            // Add visible sprite handles at the current depth (replaces the host target bone visually)
            for (int si = 0; si < visibleSprites.Count; si++)
            {
                var sprT = visibleSprites[si];
                string path = string.IsNullOrEmpty(parentPath) ? sprT.name : parentPath + "/" + sprT.name;
                bool isLast = (si == visibleSprites.Count - 1) && rigRoot == null;

                string skinRoot = null;
                Transform skinRootTransform = null;
                var skin = sprT.GetComponent<SpriteSkin>();
                if (skin != null)
                {
                    var ctrlBone = BoneHierarchyUtils.GetControllingBone(skin);
                    if (ctrlBone != null && ctrlBone != sprT)
                    {
                        skinRoot = ctrlBone.name;
                        skinRootTransform = ctrlBone;
                        if (!_skinRootRest.ContainsKey(skinRootTransform))
                            _skinRootRest[skinRootTransform] = (skinRootTransform.localPosition, skinRootTransform.localEulerAngles.z, skinRootTransform.localScale);
                    }
                    else if (ctrlBone == null && skin.rootBone != null && skin.rootBone != sprT)
                    {
                        skinRootTransform = sprT;
                    }
                }

                _handles.Add(new BoneHandle
                {
                    Name = sprT.name,
                    Path = path,
                    Transform = sprT,
                    Depth = depth,
                    HasSpriteRenderer = true,
                    ParentName = parentName,
                    IsLastChild = isLast && isLastChild,
                    SkinRootBone = skinRoot,
                    SkinRootTransform = skinRootTransform,
                    GraftIndex = graftIdx,
                    GraftTargetBone = targetBone,
                });
            }

            // Only show rig bones relevant to the grafted sprite (skin bone transforms + their descendants)
            if (rigRoot != null && allSkinBones.Count > 0)
            {
                var (traversalBones, displayBones) = BuildRelevantGraftBones(allSkinBones, graftRoot);
                string rigPath = string.IsNullOrEmpty(parentPath) ? rigRoot.name : parentPath + "/" + rigRoot.name;
                CollectGraftRigBones(rigRoot, rigPath, depth + 1, graftIdx, targetBone, traversalBones, displayBones);
            }
        }

        /// <summary>Build two sets of rig bones for a graft's visible sprites:
        /// <c>traversal</c> includes skin bones, descendants, AND ancestors (for recursive walk);
        /// <c>display</c> includes only skin bones + descendants (for BoneHandle creation).
        /// Ancestor-only bones are traversed but not shown in the editor tree.</summary>
        private static (HashSet<Transform> traversal, HashSet<Transform> display) BuildRelevantGraftBones(
            HashSet<Transform> skinBones, Transform stopParent)
        {
            var display = new HashSet<Transform>();
            foreach (var b in skinBones)
                AddWithDescendants(b, display);
            var traversal = new HashSet<Transform>(display);
            foreach (var b in skinBones)
            {
                var t = b.parent;
                while (t != null && t != stopParent && traversal.Add(t))
                    t = t.parent;
            }
            return (traversal, display);
        }

        private static void AddWithDescendants(Transform t, HashSet<Transform> set)
        {
            set.Add(t);
            for (int i = 0; i < t.childCount; i++)
                AddWithDescendants(t.GetChild(i), set);
        }

        /// <summary>Recursively collect relevant rig bones within a graft clone's skeleton.
        /// Bones in <paramref name="traversalBones"/> are entered for recursion; only bones
        /// also in <paramref name="displayBones"/> get a BoneHandle (ancestor-only bones are skipped).</summary>
        private void CollectGraftRigBones(Transform root, string parentPath, int depth,
            int graftIdx, string targetBone,
            HashSet<Transform> traversalBones, HashSet<Transform> displayBones)
        {
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!traversalBones.Contains(child)) continue;

                string path = string.IsNullOrEmpty(parentPath) ? child.name : parentPath + "/" + child.name;

                if (displayBones.Contains(child))
                {
                    bool isLast = true;
                    for (int j = i + 1; j < childCount; j++)
                        if (traversalBones.Contains(root.GetChild(j))) { isLast = false; break; }

                    _handles.Add(new BoneHandle
                    {
                        Name = child.name,
                        Path = path,
                        Transform = child,
                        Depth = depth,
                        HasSpriteRenderer = child.GetComponent<SpriteRenderer>() != null,
                        ParentName = root.name,
                        IsLastChild = isLast,
                        GraftIndex = graftIdx,
                        GraftTargetBone = targetBone,
                    });

                    if (child.childCount > 0)
                        CollectGraftRigBones(child, path, depth + 1, graftIdx, targetBone, traversalBones, displayBones);
                }
                else
                {
                    // Ancestor-only bone: traverse into children without adding a handle or incrementing depth
                    if (child.childCount > 0)
                        CollectGraftRigBones(child, path, depth, graftIdx, targetBone, traversalBones, displayBones);
                }
            }
        }

        /// <summary>Freeze ancestor bones in each graft clone to their rest poses.
        /// Called after Animator evaluation to prevent source body
        /// motion from leaking into the grafted bone's world transform.</summary>
        private void FreezeGraftAncestors()
        {
            for (int i = 0; i < _graftAncestorRest.Count; i++)
            {
                var ancestors = _graftAncestorRest[i];
                for (int j = 0; j < ancestors.Count; j++)
                {
                    var (bone, pos, rot, scale) = ancestors[j];
                    if (bone == null) continue;
                    bone.localPosition = pos;
                    bone.localRotation = rot;
                    bone.localScale = scale;
                }
            }
        }

        /// <summary>Shift each graft clone so its grafted source bone aligns with the host
        /// rig bone that controls the target sprite. Called every frame (not just during
        /// animation) so graft content follows when host rig bones are dragged.</summary>
        private void AlignGraftClones()
        {
            for (int i = 0; i < _graftAlignments.Count && i < _graftPreviewObjects.Count; i++)
            {
                var go = _graftPreviewObjects[i];
                if (go == null) continue;
                var (srcBone, rigBone, restOffset) = _graftAlignments[i];
                if (srcBone == null || rigBone == null) continue;
                Vector3 targetPos = rigBone.position + restOffset;
                Vector3 delta = targetPos - srcBone.position;
                go.transform.position += delta;
            }
        }

        /// <summary>Reset preview to rest pose then apply all current overrides from DTO.</summary>
        private void RefreshPreviewOverrides()
        {
            if (_previewGO == null || _previewNpcId == null) return;

            // Reset to rest pose (including original sprites and re-activate all bones)
            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                h.Transform.gameObject.SetActive(true); // re-activate in case it was deactivated by RemovedBones
                if (_restPos.TryGetValue(h.Path, out var rp)) h.Transform.localPosition = rp;
                if (_restRot.TryGetValue(h.Path, out var rr)) h.Transform.localEulerAngles = new Vector3(0, 0, rr);
                if (_restScale.TryGetValue(h.Path, out var rs)) h.Transform.localScale = rs;
                // Re-enable SpriteSkin in case it was disabled by removed-bone detach
                var skin0 = h.Transform.GetComponent<SpriteSkin>();
                if (skin0 != null) skin0.enabled = true;
                var sr0 = h.Transform.GetComponent<SpriteRenderer>();
                if (sr0 != null)
                {
                    sr0.enabled = true;
                    if (_restSprites.TryGetValue(h.Path, out var origSprite))
                        sr0.sprite = origSprite;
                    if (_restMaterials.TryGetValue(h.Path, out var origMat))
                        sr0.sharedMaterial = origMat;
                }
            }

            var refreshSprites = GetSpriteDict();
            if (refreshSprites?.TryGetValue(_previewNpcId, out var ovr) != true) return;

            // 1. Custom sprites (apply when defined + cache for per-frame re-stamping)
            _edCustomSpriteCache.Clear();
            if (ovr.CustomSprites.Count > 0)
            {
                foreach (var h in _handles)
                {
                    if (h.Transform == null) continue;
                    if (ovr.RemovedBones.Contains(h.Name)) continue; // skip removed bones
                    if (!ovr.CustomSprites.TryGetValue(h.Name, out var spriteDef)) continue;
                    var sr = h.Transform.GetComponent<SpriteRenderer>();
                    if (sr == null) continue;
                    var texModId = GetTextureModId();
                    var newSprite = SpriteUtils.CreateSpriteFromDef(spriteDef, texModId, ovr.Spritesheet, sr.sprite);
                    if (newSprite != null)
                    {
                        sr.sprite = newSprite;
                        _edCustomSpriteCache[h.Name] = newSprite;
                    }
                }
            }

            // 2. Graft puppets: clone source NPC prefab, show only the grafted sprite(s),
            //    and parent under the host target bone.
            CleanupGraftPreviewObjects();
            foreach (var graft in ovr.Grafts)
            {
                if (string.IsNullOrEmpty(graft.Source) || string.IsNullOrEmpty(graft.TargetBone))
                    continue;

                // Find host target bone directly in hierarchy (handles may exclude it
                // from a prior refresh when _replacedBones filtered it out).
                var targetTransform = BoneHierarchyUtils.FindRecursive(_previewGO.transform, graft.TargetBone);
                if (targetTransform == null) continue;

                // Parse source
                string sourceNpc, sourceBone;
                int slash = graft.Source.IndexOf('/');
                if (slash >= 0)
                {
                    sourceNpc = graft.Source.Substring(0, slash);
                    sourceBone = graft.Source.Substring(slash + 1);
                }
                else
                {
                    sourceNpc = graft.Source;
                    sourceBone = graft.TargetBone; // default: same bone name
                }

                // Resolve source prefab
                GameObject sourcePrefab = null;
                var npcData = DataHelper.GetExistingNPC(sourceNpc);
                if (npcData?.GameObjectAnimated != null)
                    sourcePrefab = npcData.GameObjectAnimated;
                else
                {
                    var skinData = DataHelper.GetSkin(sourceNpc);
                    if (skinData?.SkinGo != null) sourcePrefab = skinData.SkinGo;
                }
                if (sourcePrefab == null) continue;

                // Clone
                var clone = Object.Instantiate(sourcePrefab);
                clone.name = $"GraftPreview~{graft.TargetBone}";
                clone.SetActive(true);
                SetLayerRecursive(clone.transform, PreviewLayer);

                // Disable Animator on the clone (editor re-enables it during sync)
                var cloneAnim = clone.GetComponent<Animator>() ?? clone.GetComponentInChildren<Animator>();
                if (cloneAnim != null) cloneAnim.enabled = false;

                // Find source sprite bone in the clone
                Transform sourceSpriteT = BoneHierarchyUtils.FindRecursive(clone.transform, sourceBone);

                // Determine visible sprites (same logic as GraftPuppet.Create)
                var visibleSprites = new HashSet<string>();
                if (sourceSpriteT != null)
                {
                    visibleSprites.Add(sourceBone);
                    var sourceSkin = sourceSpriteT.GetComponent<SpriteSkin>();
                    if (sourceSkin != null && sourceSkin.boneTransforms != null)
                    {
                        var validBones = sourceSkin.boneTransforms.Where(b => b != null).ToArray();
                        if (validBones.Length > 0)
                        {
                            var branchRoot = BoneHierarchyUtils.FindLCA(validBones);
                            if (branchRoot == null || branchRoot == clone.transform)
                                branchRoot = validBones[0];

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
                }

                // Hide non-visible SpriteRenderers
                foreach (var sr in clone.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (!visibleSprites.Contains(sr.name))
                        sr.enabled = false;
                }

                // Hide original sprite at target if requested
                if (graft.ReplaceTarget)
                {
                    var targetSR = targetTransform.GetComponent<SpriteRenderer>();
                    if (targetSR != null) targetSR.enabled = false;
                }

                // Parent under host target bone
                clone.transform.SetParent(targetTransform, false);
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale = Vector3.one;

                _graftPreviewObjects.Add(clone);

                // Cache base SR state for this graft clone (before overrides, to avoid compounding)
                var gSortOrder = new Dictionary<string, int>();
                var gFlipX = new Dictionary<string, bool>();
                var gFlipY = new Dictionary<string, bool>();
                var gAlpha = new Dictionary<string, float>();
                foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
                {
                    var sr = t.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        gSortOrder[t.name] = sr.sortingOrder;
                        gFlipX[t.name] = sr.flipX;
                        gFlipY[t.name] = sr.flipY;
                        gAlpha[t.name] = sr.color.a;
                    }
                }
                _graftBaseSortOrder.Add(gSortOrder);
                _graftBaseFlipX.Add(gFlipX);
                _graftBaseFlipY.Add(gFlipY);
                _graftBaseAlpha.Add(gAlpha);

                // Track alignment: use the rig bone that controls the target sprite
                // so the graft follows when host rig bones are dragged.
                // Head (and other SpriteSkin sprites) sit at root level — their
                // Transform.position doesn't change when rig bones move, only
                // their mesh deforms. We track the rig bone + rest offset so
                // the graft clone follows rig bone movement.
                Transform alignBone = targetTransform;
                Vector3 alignOffset = Vector3.zero;
                var targetSkin = targetTransform.GetComponent<SpriteSkin>();
                if (targetSkin != null)
                {
                    var ctrlBone = BoneHierarchyUtils.GetControllingBone(targetSkin);
                    if (ctrlBone != null && ctrlBone != targetTransform)
                    {
                        alignBone = ctrlBone;
                        alignOffset = targetTransform.position - ctrlBone.position;
                    }
                }
                _graftAlignments.Add((sourceSpriteT, alignBone, alignOffset));

                // Cache ancestor rest poses (bones between source sprite's controlling
                // rig bone and clone root) so we can freeze them after animation to
                // isolate the graft from source body motion.
                // Sprite objects sit at the clone root level, but their rig bones
                // are deeper in the hierarchy — walk from the rig bone, not the sprite.
                var ancestorRest = new List<(Transform, Vector3, Quaternion, Vector3)>();
                if (sourceSpriteT != null)
                {
                    Transform startBone = sourceSpriteT;
                    var srcSkin = sourceSpriteT.GetComponent<SpriteSkin>();
                    if (srcSkin != null)
                    {
                        var srcCtrl = BoneHierarchyUtils.GetControllingBone(srcSkin);
                        if (srcCtrl != null)
                            startBone = srcCtrl;
                    }
                    // Exclude the controlling bone — graft BoneOverrides use SET
                    // mode and write absolute values. Only freeze ancestors above.
                    var anc = startBone.parent;
                    while (anc != null && anc != clone.transform)
                    {
                        ancestorRest.Add((anc, anc.localPosition, anc.localRotation, anc.localScale));
                        anc = anc.parent;
                    }
                }
                _graftAncestorRest.Add(ancestorRest);

                // Cache Animator + clips for animation sync
                AnimationClip[] graftClips = null;
                if (cloneAnim != null && cloneAnim.runtimeAnimatorController != null)
                    graftClips = cloneAnim.runtimeAnimatorController.animationClips;
                _graftAnimators.Add((cloneAnim, graftClips));

                // Build host→puppet state hash map for trigger-mode sync
                // (same suffix-based matching as AnimatorStateMirror)
                var stateMap = new Dictionary<int, int>();
                if (_clips != null && graftClips != null)
                {
                    var puppetSuffix = new Dictionary<string, int>();
                    foreach (var gc in graftClips)
                    {
                        string s = GetClipAction(gc.name);
                        if (s != null) puppetSuffix[s] = Animator.StringToHash(gc.name);
                    }
                    foreach (var hc in _clips)
                    {
                        string s = GetClipAction(hc.name);
                        if (s != null && puppetSuffix.TryGetValue(s, out int ph))
                            stateMap[Animator.StringToHash(hc.name)] = ph;
                    }
                }
                _graftStateMaps.Add(stateMap);
            }

            // Re-collect handles to include graft visible sprites + rig bones
            _handles.Clear();
            // Build graft lookup for CollectHandles to resolve graft indices
            _graftLookup = new Dictionary<string, int>();
            _replacedBones = new HashSet<string>();
            for (int gi = 0; gi < ovr.Grafts.Count; gi++)
            {
                var g = ovr.Grafts[gi];
                if (!string.IsNullOrEmpty(g.TargetBone))
                {
                    _graftLookup[g.TargetBone] = gi;
                    if (g.ReplaceTarget) _replacedBones.Add(g.TargetBone);
                }
            }
            CollectHandles(_previewGO.transform, "", 0);
            _graftLookup = null;
            _replacedBones = null;

            // Record rest poses for newly created graft bones so that
            // CommitBoneOverride computes correct deltas (drag).
            RecordGraftRestPoses();

            // 2d. Graft-scoped custom sprites: apply each graft's CustomSprites to bones
            //     within its preview clone (handles now include graft clone bones).
            //     Cache for per-frame re-stamping (Animator overwrites sr.sprite).
            _graftCustomSpriteCache.Clear();
            for (int gi = 0; gi < ovr.Grafts.Count && gi < _graftPreviewObjects.Count; gi++)
            {
                var graft = ovr.Grafts[gi];
                var clone = _graftPreviewObjects[gi];
                var gSprites = new Dictionary<string, Sprite>();
                if (clone != null && graft.CustomSprites.Count > 0)
                {
                    var texModId = GetTextureModId();
                    foreach (var kvp in graft.CustomSprites)
                    {
                        var boneT = BoneHierarchyUtils.FindRecursive(clone.transform, kvp.Key);
                        if (boneT == null) continue;
                        var sr = boneT.GetComponent<SpriteRenderer>();
                        if (sr == null) continue;
                        var newSprite = SpriteUtils.CreateSpriteFromDef(kvp.Value, texModId, ovr.Spritesheet, sr.sprite);
                        if (newSprite != null)
                        {
                            sr.sprite = newSprite;
                            gSprites[kvp.Key] = newSprite;
                        }
                    }
                }
                _graftCustomSpriteCache.Add(gSprites);
            }

            // 3. Removed bones: hide SpriteRenderer only, keep bone GO active.
            // Rig bones must stay active for Animator evaluation and SpriteSkin
            // deformation on other sprites that reference them.
            foreach (var removedName in ovr.RemovedBones)
            {
                var rh = _handles.Find(bh => bh.Name == removedName);
                if (rh?.Transform == null) continue;
                var rsr = rh.Transform.GetComponent<SpriteRenderer>();
                if (rsr != null) rsr.enabled = false;
            }

            // 3b-2. Removed rig bones stay active (SR hidden above) so SpriteSkin
            // deformation continues working on sprites that reference them.

            // 3c. Build name-keyed maps for the shared ApplyBoneOverrides static method
            RebuildEditorBoneMaps(ovr);

            // 4. Global model overrides
            Transform animRoot = _previewGO.GetComponentInChildren<Animator>()?.transform ?? _previewGO.transform;
            var model = ovr.Model;
            if (model.Scale != 1f)
                animRoot.localScale = new Vector3(model.Scale, model.Scale, 1f);
            if (model.OffsetX != 0f || model.OffsetY != 0f)
                animRoot.localPosition += new Vector3(model.OffsetX, model.OffsetY, 0f);

            // 4b. Model-wide flip
            if (model.FlipX || model.FlipY)
            {
                var s = animRoot.localScale;
                if (model.FlipX) s.x = -Mathf.Abs(s.x);
                if (model.FlipY) s.y = -Mathf.Abs(s.y);
                animRoot.localScale = s;
            }

            // 4c. Model-wide tint + alpha (applied to all SpriteRenderers)
            Color modelTint = Color.white;
            bool hasModelTint = !string.IsNullOrEmpty(model.TintHex) &&
                                ColorUtility.TryParseHtmlString(model.TintHex, out modelTint);
            float modelAlpha = Mathf.Clamp01(model.Alpha);
            bool applyModelColor = hasModelTint || modelAlpha < 1f;

            if (applyModelColor)
            {
                foreach (var h in _handles)
                {
                    if (h.Transform == null) continue;
                    var sr = h.Transform.GetComponent<SpriteRenderer>();
                    if (sr == null) continue;
                    Color c = hasModelTint ? modelTint : Color.white;
                    c.a = modelAlpha;
                    sr.color = c;
                }
            }

            // 5. Per-bone overrides (transform, visibility, color, flip, alpha, sort)
            ApplyBoneOverridesToPreview(ovr);

            // 6. Align graft clones so grafted bone sits at host target bone
            AlignGraftClones();
        }

        /// <summary>Restore sprite renderer properties (sortingOrder, flip, alpha, color)
        /// to their rest values. Called before override application so additive SR changes
        /// don't compound. Bone transforms are NOT reset — the Animator overwrites them
        /// each frame, matching runtime behavior.</summary>
        private void RestoreSRPropertiesToRest()
        {
            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                var sr = h.Transform.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (_restSortingOrder.TryGetValue(h.Path, out var so))
                        sr.sortingOrder = so;
                    if (_restFlipX.TryGetValue(h.Path, out var fx))
                        sr.flipX = fx;
                    if (_restFlipY.TryGetValue(h.Path, out var fy))
                        sr.flipY = fy;
                    if (_basePreviewAlpha.TryGetValue(h.Path, out var ba))
                    {
                        var c = sr.color;
                        c.a = ba;
                        sr.color = c;
                    }
                }
            }
        }

        /// <summary>Restore skinRoot transforms to their pre-override state.
        /// Mirrors runtime CharacterOverrideDriver.ApplyGlobalOverrides which restores
        /// skinRoots from _skinRootPreOffset before each frame's override application.</summary>
        private void RestoreSkinRootsToPreOverride()
        {
            foreach (var kvp in _skinRootRest)
            {
                if (kvp.Key == null) continue;
                kvp.Key.localPosition = kvp.Value.pos;
                kvp.Key.localEulerAngles = new Vector3(0, 0, kvp.Value.rot);
                kvp.Key.localScale = kvp.Value.scale;
            }
        }



        /// <summary>Build name-keyed lookup maps from the current handle list for the
        /// shared CharacterOverrideDriver.ApplyBoneOverrides static method. Called after
        /// handles are (re-)collected in RefreshPreviewOverrides.</summary>
        private void RebuildEditorBoneMaps(CharacterOverrideDef ovr)
        {
            _edBoneMap.Clear(); _edSrMap.Clear(); _edSkinRootMap.Clear();
            _edBaseSortOrder.Clear(); _edBaseFlipX.Clear(); _edBaseFlipY.Clear();
            _edBaseAlpha.Clear();

            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                // Skip graft bones — they have their own per-clone maps built in ApplyBoneOverridesToPreview
                if (h.GraftIndex >= 0) continue;
                _edBoneMap[h.Name] = h.Transform;
                var sr = h.Transform.GetComponent<SpriteRenderer>();
                if (sr != null) _edSrMap[h.Name] = sr;
                if (h.SkinRootTransform != null) _edSkinRootMap[h.Name] = h.SkinRootTransform;

                // Re-key path-based rest values to name-based for the shared method
                if (_restSortingOrder.TryGetValue(h.Path, out var so)) _edBaseSortOrder[h.Name] = so;
                if (_restFlipX.TryGetValue(h.Path, out var fx)) _edBaseFlipX[h.Name] = fx;
                if (_restFlipY.TryGetValue(h.Path, out var fy)) _edBaseFlipY[h.Name] = fy;
                if (_basePreviewAlpha.TryGetValue(h.Path, out var ba)) _edBaseAlpha[h.Name] = ba;
            }

        }

        /// <summary>Apply per-bone transform/visual overrides + removed bones to the preview.
        /// Delegates to the shared CharacterOverrideDriver.ApplyBoneOverrides static method
        /// (single source of truth for bone override logic across editor, runtime, and
        /// encounter preview). Called from RefreshPreviewOverrides (initial) and every
        /// frame during playback/scrub because the Animator resets transforms.</summary>
        private void ApplyBoneOverridesToPreview(CharacterOverrideDef ovr)
        {
            // Host-level overrides
            CharacterOverrideDriver.ApplyBoneOverrides(ovr.BoneOverrides, _edBoneMap, _edSrMap,
                _edSkinRootMap, _edBaseSortOrder, _edBaseFlipX, _edBaseFlipY,
                _edBaseAlpha, null);

            // Re-hide removed bones (Animator may re-enable them)
            foreach (var removedName in ovr.RemovedBones)
            {
                if (_edSrMap.TryGetValue(removedName, out var rsr) && rsr != null)
                    rsr.enabled = false;
            }

            // Graft-scoped overrides: apply each graft's BoneOverrides to its preview clone
            for (int gi = 0; gi < ovr.Grafts.Count && gi < _graftPreviewObjects.Count; gi++)
            {
                var graft = ovr.Grafts[gi];
                var clone = _graftPreviewObjects[gi];
                if (clone == null || graft.BoneOverrides.Count == 0) continue;

                // Build bone/SR maps scoped to this graft clone
                var gBoneMap = new Dictionary<string, Transform>();
                var gSrMap = new Dictionary<string, SpriteRenderer>();
                var gSkinRootMap = new Dictionary<string, Transform>();
                foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
                {
                    gBoneMap[t.name] = t;
                    var sr = t.GetComponent<SpriteRenderer>();
                    if (sr != null) gSrMap[t.name] = sr;
                    var skin = t.GetComponent<SpriteSkin>();
                    if (skin != null && skin.rootBone != null)
                        gSkinRootMap[t.name] = skin.rootBone;
                }

                // Use cached base SR state (populated once during clone creation) to avoid compounding
                var cachedSort = gi < _graftBaseSortOrder.Count ? _graftBaseSortOrder[gi] : new Dictionary<string, int>();
                var cachedFlipX = gi < _graftBaseFlipX.Count ? _graftBaseFlipX[gi] : new Dictionary<string, bool>();
                var cachedFlipY = gi < _graftBaseFlipY.Count ? _graftBaseFlipY[gi] : new Dictionary<string, bool>();
                var cachedAlpha = gi < _graftBaseAlpha.Count ? _graftBaseAlpha[gi] : new Dictionary<string, float>();

                // Graft overrides use SET mode for controlling bones (via
                // skinRootMap redirect). No additive compounding — SET writes
                // absolute values each frame.
                var gNonAnim = new HashSet<string>();
                foreach (var kvp2 in gSkinRootMap)
                    if (kvp2.Value != null) gNonAnim.Add(kvp2.Value.name);

                CharacterOverrideDriver.ApplyBoneOverrides(graft.BoneOverrides, gBoneMap, gSrMap,
                    gSkinRootMap, cachedSort, cachedFlipX, cachedFlipY,
                    cachedAlpha, gNonAnim);
            }
        }


        /// <summary>Known animation action suffixes used by AtO character clips.
        /// Order matters: first match wins, so more specific suffixes come first.</summary>
        private static readonly string[] AnimSuffixes = new[]
        {
            "hardmovement", "movement", "idle", "cast", "attack", "hit", "death", "skill"
        };

        /// <summary>Extract the canonical action suffix from a clip name.
        /// e.g. "sylvieIdle" → "idle", "buhomago_attack" → "attack".</summary>
        private static string GetClipAction(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            var lower = clipName.ToLowerInvariant();
            for (int i = 0; i < AnimSuffixes.Length; i++)
                if (lower.EndsWith(AnimSuffixes[i]))
                    return AnimSuffixes[i];
            return null;
        }

        /// <summary>Sync graft clone Animators to the host Animator state.
        /// Mirrors the host's current state hash + normalized time on each graft clone's
        /// Animator so they animate in lockstep.</summary>
        private void SyncGraftAnimatorsToHost()
        {
            if (_previewAnimator == null || !_previewAnimator.isActiveAndEnabled) return;
            var hostState = _previewAnimator.GetCurrentAnimatorStateInfo(0);
            int hostHash = hostState.shortNameHash;
            float hostNorm = hostState.normalizedTime;

            for (int i = 0; i < _graftAnimators.Count && i < _graftPreviewObjects.Count; i++)
            {
                var (anim, clips) = _graftAnimators[i];
                if (anim == null) continue;

                // Ensure Animator is enabled with speed=0 (we drive time manually)
                if (!anim.enabled)
                {
                    anim.enabled = true;
                    anim.speed = 0f;
                }

                // Map host state → graft state via suffix matching
                int targetHash = hostHash;
                if (i < _graftStateMaps.Count)
                {
                    var map = _graftStateMaps[i];
                    if (map.TryGetValue(hostHash, out int mapped))
                        targetHash = mapped;
                }

                anim.Play(targetHash, 0, hostNorm);
                anim.Update(0f); // force evaluation so bones update this frame
            }
        }

        /// <summary>Disable all graft clone Animators (when leaving trigger mode).</summary>
        private void DisableGraftAnimators()
        {
            for (int i = 0; i < _graftAnimators.Count; i++)
            {
                var (anim, _) = _graftAnimators[i];
                if (anim != null) anim.enabled = false;
            }
        }


        /// <summary>Commit the current bone transform to the override DTO (called after drag ends).</summary>
        private void CommitBoneOverride(string bonePath, EditMode dragMode)
        {
            var commitSprites = GetSpriteDict();
            if (commitSprites == null) return;
            if (!commitSprites.TryGetValue(_previewNpcId, out var ovr))
            {
                ovr = new CharacterOverrideDef { Id = _previewNpcId };
                commitSprites[_previewNpcId] = ovr;
            }

            // Find handle by path (unique), extract bone name for override data key
            var h = _handles.Find(b => b.Path == bonePath);
            if (h?.Transform == null) return;
            string boneName = h.Name;

            // Route to graft-scoped override dict if this is a graft bone
            Dictionary<string, BoneOverride> targetOverrides;
            if (h.GraftIndex >= 0 && h.GraftIndex < ovr.Grafts.Count)
                targetOverrides = ovr.Grafts[h.GraftIndex].BoneOverrides;
            else
                targetOverrides = ovr.BoneOverrides;

            // For SpriteSkin sprites whose rootBone is a different GO, the drag
            // actually moved the rootBone. Commit the delta from the rootBone's
            // rest pose, stored under the sprite's bone name.
            Transform commitT = h.SkinRootTransform ?? h.Transform;
            string commitRestPath = bonePath;
            bool useSkinRootRest = false;
            (Vector3 pos, float rot, Vector3 scale) skinRootRestPose = default;
            if (h.SkinRootTransform != null)
            {
                // Find the rootBone's handle to get its rest path
                var rootH = _handles.Find(bh => bh.Transform == h.SkinRootTransform);
                if (rootH != null)
                    commitRestPath = rootH.Path;
                else if (_skinRootRest.TryGetValue(h.SkinRootTransform, out skinRootRestPose))
                    useSkinRootRest = true;
                // ^ SkinRootTransform not in _handles (e.g. _previewGO.transform at
                //   PreviewOrigin). Use _skinRootRest which records its actual rest pose
                //   so the delta doesn't include the -5000 preview offset.
            }

            // ── Existing bone → write BoneOverride (delta from rest pose) ──
            Vector3 restP;
            float restR;
            Vector3 restS;
            if (useSkinRootRest)
            {
                restP = skinRootRestPose.pos;
                restR = skinRootRestPose.rot;
                restS = skinRootRestPose.scale;
            }
            else
            {
                restP = _restPos.TryGetValue(commitRestPath, out var rp) ? rp : Vector3.zero;
                restR = _restRot.TryGetValue(commitRestPath, out var rr) ? rr : 0f;
                restS = _restScale.TryGetValue(commitRestPath, out var rs) ? rs : Vector3.one;
            }

            // Graft bones redirected through skinRootMap use SET mode —
            // commit absolute local values directly (no delta, no world-align).
            bool isGraftSetMode = h.GraftIndex >= 0
                && h.SkinRootTransform != null
                && h.SkinRootTransform != h.Transform;

            float ox, oy, rotD, sx, sy;

            if (isGraftSetMode)
            {
                ox = commitT.localPosition.x;
                oy = commitT.localPosition.y;
                rotD = commitT.localEulerAngles.z;
                if (rotD > 180f) rotD -= 360f;
                sx = commitT.localScale.x;
                sy = commitT.localScale.y;
            }
            else
            {
                float localOx = commitT.localPosition.x - restP.x;
                float localOy = commitT.localPosition.y - restP.y;
                rotD = commitT.localEulerAngles.z - restR;
                // Wrap rotation delta to [-180, 180] to avoid 359.9 → -0.1 issues
                if (rotD > 180f) rotD -= 360f;
                else if (rotD < -180f) rotD += 360f;
                float localSx = restS.x != 0 ? commitT.localScale.x / restS.x : 1f;
                float localSy = restS.y != 0 ? commitT.localScale.y / restS.y : 1f;

                // Convert local-space offset to world-aligned offset
                // (ApplyBoneOverrides applies offsets in world-aligned space)
                if (commitT.parent != null)
                {
                    Vector3 worldOff = commitT.parent.TransformDirection(
                        new Vector3(localOx, localOy, 0f));
                    ox = worldOff.x; oy = worldOff.y;
                }
                else
                {
                    ox = localOx; oy = localOy;
                }

                // Convert local-space scale ratio to world-aligned scale ratio
                float pz = commitT.parent != null
                    ? commitT.parent.eulerAngles.z * Mathf.Deg2Rad : 0f;
                float c2 = Mathf.Cos(pz) * Mathf.Cos(pz);
                float s2 = Mathf.Sin(pz) * Mathf.Sin(pz);
                float det = c2 * c2 - s2 * s2;
                if (Mathf.Abs(det) > 0.001f)
                {
                    sx = (localSx * c2 - localSy * s2) / det;
                    sy = (localSy * c2 - localSx * s2) / det;
                }
                else
                {
                    sx = sy = (localSx + localSy) * 0.5f;
                }
            }

            // Only check/write fields relevant to the drag mode that was active
            bool changed;
            switch (dragMode)
            {
                case EditMode.Move:
                    changed = isGraftSetMode
                        ? (Mathf.Abs(ox - restP.x) > 0.001f || Mathf.Abs(oy - restP.y) > 0.001f)
                        : (Mathf.Abs(ox) > 0.001f || Mathf.Abs(oy) > 0.001f);
                    break;
                case EditMode.Rotate:
                    changed = Mathf.Abs(rotD) > 0.1f;
                    break;
                case EditMode.Scale:
                    changed = isGraftSetMode
                        ? (Mathf.Abs(sx - restS.x) > 0.001f || Mathf.Abs(sy - restS.y) > 0.001f)
                        : (Mathf.Abs(sx - 1f) > 0.001f || Mathf.Abs(sy - 1f) > 0.001f);
                    break;
                default:
                    changed = false;
                    break;
            }
            if (changed)
            {
                if (!targetOverrides.TryGetValue(boneName, out var bo))
                { bo = new BoneOverride(); targetOverrides[boneName] = bo; }
                switch (dragMode)
                {
                    case EditMode.Move:
                        bo.PosX = Mathf.Round(ox * 1000f) / 1000f;
                        bo.PosY = Mathf.Round(oy * 1000f) / 1000f;
                        break;
                    case EditMode.Rotate:
                        bo.Rotation = Mathf.Round(rotD * 10f) / 10f;
                        break;
                    case EditMode.Scale:
                        bo.ScaleX = Mathf.Round(sx * 100f) / 100f;
                        bo.ScaleY = Mathf.Round(sy * 100f) / 100f;
                        break;
                }
                OnSpriteModified();
            }
        }
    }
}
