using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    public partial class SpriteEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  PREVIEW MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        private void SpawnPreview(string spriteDefId, string baseSprite)
        {
            DestroyPreview();
            _previewNpcId = spriteDefId;

            // Resolve base NPC data: first check if a zone NPC uses this sprite def
            // (it would have a custom prefab already built), then fall back to base game NPC
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

            if (npcData == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] Cannot find NPC data for sprite '{spriteDefId}' (base='{baseSprite}')");
                _previewNpcId = null; return;
            }

            var prefab = npcData.GameObjectAnimated;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] Base NPC '{baseSprite}' has no animated prefab.");
                _previewNpcId = null; return;
            }

            _previewGO = Object.Instantiate(prefab, PreviewOrigin, Quaternion.identity);
            _previewGO.name = $"[SpriteEditor] {spriteDefId}";
            _previewGO.SetActive(true);

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

                        // Show the first frame of the selected clip using SampleAnimation
                        _playbackTime = 0f;
                        _timelineNormTime = 0f;
                        _clips[_selectedClipIdx].SampleAnimation(_previewGO, 0f);
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
            _zoom = 2.5f;
            _pan = Vector2.zero;
            Plugin.Log.LogInfo($"[SpriteEditor] Spawned preview for '{spriteDefId}': {_handles.Count} bones");
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
            _selectedBone = null;
            _dragging = false;
            _previewAnimator = null;
            _clipNames = null;
            _clipLengths = null;
            _clips = null;
            _animPlaying = false;
            _playbackTime = 0f;
            _timelineNormTime = 0f;
            _timelineDragging = false;
            CleanupShaderMaterials();
            CleanupAddedPreviewObjects();
        }

        private void CleanupAddedPreviewObjects()
        {
            foreach (var go in _addedPreviewObjects)
            {
                if (go != null) Object.Destroy(go);
            }
            _addedPreviewObjects.Clear();
            foreach (var go in _graftedBranchObjects)
            {
                if (go != null) Object.Destroy(go);
            }
            _graftedBranchObjects.Clear();
        }

        private void RecordRestPose(Transform root)
        {
            foreach (Transform child in root)
            {
                _restPos[child.name] = child.localPosition;
                _restRot[child.name] = child.localEulerAngles.z;
                _restScale[child.name] = child.localScale;
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (sr.sprite != null)
                        _restSprites[child.name] = sr.sprite;
                    if (sr.sharedMaterial != null)
                        _restMaterials[child.name] = sr.sharedMaterial;
                    _basePreviewAlpha[child.name] = sr.color.a;
                }
                RecordRestPose(child);
            }
        }

        private void CollectHandles(Transform root, string parentPath, int depth)
        {
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                string path = string.IsNullOrEmpty(parentPath) ? child.name : parentPath + "/" + child.name;

                // Read SpriteSkin rootBone if present (via reflection)
                string skinRoot = null;
                var skin = child.GetComponent<SpriteSkin>();
                if (skin != null)
                    skinRoot = skin.rootBone?.name;

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
                });
                if (child.childCount > 0) CollectHandles(child, path, depth + 1);
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
                if (_restPos.TryGetValue(h.Name, out var rp)) h.Transform.localPosition = rp;
                if (_restRot.TryGetValue(h.Name, out var rr)) h.Transform.localEulerAngles = new Vector3(0, 0, rr);
                if (_restScale.TryGetValue(h.Name, out var rs)) h.Transform.localScale = rs;
                var sr0 = h.Transform.GetComponent<SpriteRenderer>();
                if (sr0 != null)
                {
                    sr0.enabled = true;
                    if (_restSprites.TryGetValue(h.Name, out var origSprite))
                        sr0.sprite = origSprite;
                    if (_restMaterials.TryGetValue(h.Name, out var origMat))
                        sr0.sharedMaterial = origMat;
                }
            }

            var refreshSprites = GetSpriteDict();
            if (refreshSprites?.TryGetValue(_previewNpcId, out var ovr) != true) return;

            // 1. Custom sprites (apply when defined)
            if (ovr.CustomSprites.Count > 0)
            {
                foreach (var h in _handles)
                {
                    if (h.Transform == null) continue;
                    if (ovr.RemovedBones.Contains(h.Name)) continue; // skip removed bones
                    if (!ovr.CustomSprites.TryGetValue(h.Name, out var spriteDef)) continue;
                    var sr = h.Transform.GetComponent<SpriteRenderer>();
                    if (sr == null) continue;
                    var texZoneId = GetTextureZoneId();
                    var newSprite = CreateSpriteFromDef(spriteDef, texZoneId, ovr.Spritesheet, sr.sprite);
                    if (newSprite != null) sr.sprite = newSprite;
                }
            }

            // 2. Create added sprite/bone GameObjects BEFORE applying grafts/overrides.
            //    All override properties (source, transform, visual) come from ovr.Bones
            //    just like any existing bone — AddedSpriteDef only stores ParentBone.
            CleanupAddedPreviewObjects();

            // 2a. Added sprites: bare GO with SpriteRenderer parented to existing bone
            //     If no parent specified yet, parent to preview root so it still shows in the list.
            foreach (var kvp in ovr.AddedSprites)
            {
                var aDef = kvp.Value;
                Transform parentT = null;
                if (!string.IsNullOrEmpty(aDef.ParentBone))
                {
                    var parentH = _handles.Find(bh => bh.Name == aDef.ParentBone);
                    if (parentH?.Transform != null) parentT = parentH.Transform;
                }
                if (parentT == null) parentT = _previewGO.transform; // fallback to root

                var go = new GameObject(kvp.Key);
                go.transform.SetParent(parentT, false);
                var asr = go.AddComponent<SpriteRenderer>();
                // Inherit sorting layer from parent so it renders in the correct layer
                var parentSR = parentT.GetComponent<SpriteRenderer>();
                if (parentSR != null)
                    asr.sortingLayerID = parentSR.sortingLayerID;
                _addedPreviewObjects.Add(go);
            }

            // 2b. Added rig bones: empty GameObjects on parent handles
            //     If no parent specified yet, parent to preview root so it still shows in the list.
            foreach (var kvp in ovr.AddedBones)
            {
                var bDef = kvp.Value;
                Transform parentT = null;
                if (!string.IsNullOrEmpty(bDef.ParentBone))
                {
                    var parentH = _handles.Find(bh => bh.Name == bDef.ParentBone);
                    if (parentH?.Transform != null) parentT = parentH.Transform;
                    else
                    {
                        var prev = _addedPreviewObjects.Find(g => g != null && g.name == bDef.ParentBone);
                        if (prev != null) parentT = prev.transform;
                    }
                }
                if (parentT == null) parentT = _previewGO.transform; // fallback to root

                var bgo = new GameObject(kvp.Key);
                bgo.transform.SetParent(parentT, false);
                bgo.transform.localPosition = new Vector3(bDef.PosX, bDef.PosY, 0f);
                bgo.transform.localEulerAngles = new Vector3(0, 0, bDef.Rotation);
                bgo.transform.localScale = new Vector3(bDef.ScaleX, bDef.ScaleY, 1f);
                _addedPreviewObjects.Add(bgo);
            }

            // Re-collect handles to include added items in the unified bone list
            _handles.Clear();
            CollectHandles(_previewGO.transform, "", 0);

            // 3. Branch grafts (SpriteFrom on any bone — existing or added).
            //    Instead of swapping the sprite image and remapping SpriteSkin bones,
            //    clone the entire bone branch from the source NPC so the sprite renders
            //    correctly with its own bones.
            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                if (ovr.RemovedBones.Contains(h.Name)) continue; // skip removed bones
                if (!ovr.Bones.TryGetValue(h.Name, out var bo) || string.IsNullOrEmpty(bo.SpriteFrom)) continue;

                string sourceNpc, sourceBone;
                int slash = bo.SpriteFrom.IndexOf('/');
                if (slash >= 0)
                {
                    sourceNpc = bo.SpriteFrom.Substring(0, slash);
                    sourceBone = bo.SpriteFrom.Substring(slash + 1);
                }
                else
                {
                    sourceNpc = bo.SpriteFrom;
                    sourceBone = h.Name;
                }

                var branch = CloneNpcBranch(sourceNpc, sourceBone);
                if (branch != null)
                {
                    // Parent the cloned branch to the same parent as the target bone
                    branch.transform.SetParent(h.Transform.parent, false);
                    // Position at same local position as target bone
                    branch.transform.localPosition = h.Transform.localPosition;
                    branch.transform.localRotation = h.Transform.localRotation;
                    branch.transform.localScale = h.Transform.localScale;
                    _graftedBranchObjects.Add(branch);

                    // Hide the original sprite on this bone (the branch replaces it)
                    var sr = h.Transform.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.enabled = false;
                }
            }

            // 3b. Removed bones: deactivate bone GO in preview
            foreach (var removedName in ovr.RemovedBones)
            {
                var rh = _handles.Find(bh => bh.Name == removedName);
                if (rh?.Transform == null) continue;
                rh.Transform.gameObject.SetActive(false);
            }

            // 3c. Re-collect handles so imported branch bones appear in the bone tree
            if (_graftedBranchObjects.Count > 0)
            {
                _handles.Clear();
                CollectHandles(_previewGO.transform, "", 0);
            }

            // 4. Global model overrides
            Transform animRoot = _previewGO.GetComponentInChildren<Animator>()?.transform ?? _previewGO.transform;
            if (ovr.ScaleMultiplier != 1f)
                animRoot.localScale = new Vector3(ovr.ScaleMultiplier, ovr.ScaleMultiplier, 1f);
            if (ovr.OffsetX != 0f || ovr.OffsetY != 0f)
                animRoot.localPosition += new Vector3(ovr.OffsetX, ovr.OffsetY, 0f);

            // 4b. Model-wide flip
            if (ovr.FlipX || ovr.FlipY)
            {
                var s = animRoot.localScale;
                if (ovr.FlipX) s.x = -Mathf.Abs(s.x);
                if (ovr.FlipY) s.y = -Mathf.Abs(s.y);
                animRoot.localScale = s;
            }

            // 4c. Model-wide tint + alpha (applied to all SpriteRenderers)
            Color modelTint = Color.white;
            bool hasModelTint = !string.IsNullOrEmpty(ovr.ModelTintHex) &&
                                ColorUtility.TryParseHtmlString(ovr.ModelTintHex, out modelTint);
            float modelAlpha = Mathf.Clamp01(ovr.ModelAlpha);
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

            // 6. Shader effects (AllIn1SpriteShader material swap)
            ApplyShaderEffectsToPreview(ovr);
        }

        /// <summary>Apply per-bone transform/visual overrides + removed bones to the preview.
        /// Called from RefreshPreviewOverrides (initial) and every frame during playback/scrub
        /// because SampleAnimation resets transform values each frame.</summary>
        private void ApplyBoneOverridesToPreview(SpriteOverrideDef ovr)
        {
            foreach (var h in _handles)
            {
                if (h.Transform == null || !ovr.Bones.TryGetValue(h.Name, out var bo)) continue;
                if (bo.PosX != 0f || bo.PosY != 0f)
                    h.Transform.localPosition += new Vector3(bo.PosX, bo.PosY, 0f);
                if (bo.Rotation != 0f)
                    h.Transform.localEulerAngles = new Vector3(0, 0, h.Transform.localEulerAngles.z + bo.Rotation);
                if (bo.ScaleX != 1f || bo.ScaleY != 1f)
                    h.Transform.localScale = new Vector3(
                        h.Transform.localScale.x * bo.ScaleX,
                        h.Transform.localScale.y * bo.ScaleY,
                        h.Transform.localScale.z);

                var bsr = h.Transform.GetComponent<SpriteRenderer>();
                if (bsr != null)
                {
                    if (!bo.Visible)
                        bsr.enabled = false;
                    if (!string.IsNullOrEmpty(bo.ColorHex) && ColorUtility.TryParseHtmlString(bo.ColorHex, out var col))
                        bsr.color = col;
                    if (bo.FlipX) bsr.flipX = !bsr.flipX;
                    if (bo.FlipY) bsr.flipY = !bsr.flipY;
                    if (bo.Alpha < 1f)
                    {
                        Color c = bsr.color;
                        float baseA = _basePreviewAlpha.TryGetValue(h.Name, out var bpa) ? bpa : c.a;
                        c.a = baseA * Mathf.Clamp01(bo.Alpha);
                        bsr.color = c;
                    }
                    if (bo.SortingOffset != 0)
                        bsr.sortingOrder += bo.SortingOffset;
                }
            }

            // Re-hide removed bones (SampleAnimation may re-enable them)
            foreach (var removedName in ovr.RemovedBones)
            {
                var rh = _handles.Find(bh => bh.Name == removedName);
                if (rh?.Transform == null) continue;
                rh.Transform.gameObject.SetActive(false);
            }
        }

        /// <summary>Apply AllIn1SpriteShader effects to the preview (HSV, Glow, Outline, etc).</summary>
        private void ApplyShaderEffectsToPreview(SpriteOverrideDef ovr)
        {
            // Clean up previously created shader materials
            CleanupShaderMaterials();

            if (!ovr.UseShaderEffects || _previewGO == null)
                return;

            FindAllIn1Shader();
            if (_allIn1Shader == null)
                return;

            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                var sr = h.Transform.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;

                var mat = CreateShaderMaterial(sr, ovr);
                sr.sharedMaterial = mat;
                _shaderMaterials.Add(mat);
            }
        }

        /// <summary>Create an AllIn1SpriteShader material with the specified effects.
        /// Properly transfers essential sprite properties without cross-shader CopyProperties.</summary>
        private static Material CreateShaderMaterial(SpriteRenderer sr, SpriteOverrideDef ovr)
        {
            var mat = new Material(_allIn1Shader);

            // Transfer essential sprite properties manually (don't CopyPropertiesFromMaterial cross-shader)
            mat.SetTexture("_MainTex", sr.sprite.texture);
            mat.SetColor("_Color", sr.color);
            mat.renderQueue = sr.sharedMaterial != null ? sr.sharedMaterial.renderQueue : 3000;

            // HSV
            if (ovr.HueShift != 0f || ovr.Saturation != 1f || ovr.Brightness != 1f)
            {
                mat.EnableKeyword("HSV_ON");
                mat.SetFloat("_HsvShift", ovr.HueShift);
                mat.SetFloat("_HsvSaturation", ovr.Saturation);
                mat.SetFloat("_HsvBright", ovr.Brightness);
            }

            // Glow
            if (ovr.GlowEnabled)
            {
                mat.EnableKeyword("GLOW_ON");
                Color glowCol = Color.white;
                if (!string.IsNullOrEmpty(ovr.GlowColorHex))
                    ColorUtility.TryParseHtmlString(ovr.GlowColorHex, out glowCol);
                mat.SetColor("_GlowColor", glowCol);
                mat.SetFloat("_Glow", ovr.GlowIntensity);
            }

            // Outline
            if (ovr.OutlineEnabled)
            {
                mat.EnableKeyword("OUTBASE_ON");
                Color outCol = Color.black;
                if (!string.IsNullOrEmpty(ovr.OutlineColorHex))
                    ColorUtility.TryParseHtmlString(ovr.OutlineColorHex, out outCol);
                mat.SetColor("_OutlineColor", outCol);
                mat.SetFloat("_OutlineSize", ovr.OutlineSize);
                mat.SetFloat("_OutlineAlpha", 1f);
            }

            // Greyscale
            if (ovr.GreyscaleBlend > 0f)
            {
                mat.EnableKeyword("GREYSCALE_ON");
                mat.SetFloat("_GreyscaleBlend", ovr.GreyscaleBlend);
            }

            // Ghost
            if (ovr.GhostTransparency > 0f)
            {
                mat.EnableKeyword("GHOST_ON");
                mat.SetFloat("_GhostTransparency", ovr.GhostTransparency);
                mat.SetFloat("_GhostColorBoost", 1f);
            }

            return mat;
        }

        /// <summary>Destroy preview shader materials to avoid leaks.</summary>
        private void CleanupShaderMaterials()
        {
            foreach (var mat in _shaderMaterials)
            {
                if (mat != null)
                    Object.Destroy(mat);
            }
            _shaderMaterials.Clear();
        }

        /// <summary>Find the AllIn1SpriteShader in Resources (cached, searched once).</summary>
        private static void FindAllIn1Shader()
        {
            if (_shaderSearched) return;
            _shaderSearched = true;

            _allIn1Shader = Resources.Load<Shader>("AllIn1SpriteShader");
            if (_allIn1Shader == null)
            {
                // Fallback: try Shader.Find
                _allIn1Shader = Shader.Find("AllIn1SpriteShader");
            }
            if (_allIn1Shader != null)
                Plugin.Log.LogInfo("[SpriteEditor] Found AllIn1SpriteShader.");
            else
                Plugin.Log.LogWarning("[SpriteEditor] AllIn1SpriteShader not found. Shader effects unavailable.");
        }

        /// <summary>Commit the current bone transform to the override DTO (called after drag ends).</summary>
        private void CommitBoneOverride(string boneName)
        {
            var commitSprites = GetSpriteDict();
            if (commitSprites == null) return;
            if (!commitSprites.TryGetValue(_previewNpcId, out var ovr))
            {
                ovr = new SpriteOverrideDef { NpcId = _previewNpcId };
                commitSprites[_previewNpcId] = ovr;
            }

            // ── Added Rig Bone → write to AddedBoneDef ──
            if (ovr.AddedBones.TryGetValue(boneName, out var abDef))
            {
                var bh = _handles.Find(b => b.Name == boneName);
                if (bh?.Transform != null)
                {
                    abDef.PosX = Mathf.Round(bh.Transform.localPosition.x * 1000f) / 1000f;
                    abDef.PosY = Mathf.Round(bh.Transform.localPosition.y * 1000f) / 1000f;
                    abDef.Rotation = Mathf.Round(bh.Transform.localEulerAngles.z * 10f) / 10f;
                    abDef.ScaleX = Mathf.Round(bh.Transform.localScale.x * 100f) / 100f;
                    abDef.ScaleY = Mathf.Round(bh.Transform.localScale.y * 100f) / 100f;
                    OnSpriteModified();
                }
                return;
            }

            // ── Added Sprite → write transform to BoneOverride (same as existing bones) ──
            if (ovr.AddedSprites.ContainsKey(boneName))
            {
                var bh = _handles.Find(b => b.Name == boneName);
                if (bh?.Transform != null)
                {
                    if (!ovr.Bones.TryGetValue(boneName, out var addBO))
                    { addBO = new BoneOverride(); ovr.Bones[boneName] = addBO; }
                    addBO.PosX = Mathf.Round(bh.Transform.localPosition.x * 1000f) / 1000f;
                    addBO.PosY = Mathf.Round(bh.Transform.localPosition.y * 1000f) / 1000f;
                    addBO.Rotation = Mathf.Round(bh.Transform.localEulerAngles.z * 10f) / 10f;
                    addBO.ScaleX = Mathf.Round(bh.Transform.localScale.x * 100f) / 100f;
                    addBO.ScaleY = Mathf.Round(bh.Transform.localScale.y * 100f) / 100f;
                    OnSpriteModified();
                }
                return;
            }

            // ── Existing bone → write BoneOverride (delta from rest pose) ──
            var h = _handles.Find(b => b.Name == boneName);
            if (h?.Transform == null) return;

            Vector3 restP = _restPos.TryGetValue(boneName, out var rp) ? rp : Vector3.zero;
            float restR = _restRot.TryGetValue(boneName, out var rr) ? rr : 0f;
            Vector3 restS = _restScale.TryGetValue(boneName, out var rs) ? rs : Vector3.one;

            float ox = h.Transform.localPosition.x - restP.x;
            float oy = h.Transform.localPosition.y - restP.y;
            float rotD = h.Transform.localEulerAngles.z - restR;
            float sx = restS.x != 0 ? h.Transform.localScale.x / restS.x : 1f;
            float sy = restS.y != 0 ? h.Transform.localScale.y / restS.y : 1f;

            bool changed = Mathf.Abs(ox) > 0.001f || Mathf.Abs(oy) > 0.001f ||
                           Mathf.Abs(rotD) > 0.1f ||
                           Mathf.Abs(sx - 1f) > 0.001f || Mathf.Abs(sy - 1f) > 0.001f;
            if (changed)
            {
                if (!ovr.Bones.TryGetValue(boneName, out var bo))
                { bo = new BoneOverride(); ovr.Bones[boneName] = bo; }
                bo.PosX = Mathf.Round(ox * 1000f) / 1000f;
                bo.PosY = Mathf.Round(oy * 1000f) / 1000f;
                bo.Rotation = Mathf.Round(rotD * 10f) / 10f;
                bo.ScaleX = Mathf.Round(sx * 100f) / 100f;
                bo.ScaleY = Mathf.Round(sy * 100f) / 100f;
                OnSpriteModified();
            }
        }
    }
}
