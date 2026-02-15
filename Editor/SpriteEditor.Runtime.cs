using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    public partial class SpriteEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  CAMERA & COORDINATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void EnsureCamera()
        {
            // Re-create RT if it was released or became invalid
            if (_rt != null && !_rt.IsCreated())
            {
                Object.Destroy(_rt);
                _rt = null;
                if (_cam != null) _cam.targetTexture = null;
            }

            if (_rt == null)
            {
                _rt = new RenderTexture(RT_W, RT_H, 16);
                _rt.Create();
                if (_cam != null) _cam.targetTexture = _rt;
            }

            if (_cam != null) return;
            var go = new GameObject("[SpriteEditor] Camera");
            Object.DontDestroyOnLoad(go);
            go.transform.position = new Vector3(PreviewOrigin.x, PreviewOrigin.y, PreviewOrigin.z - 10f);
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 1f);
            _cam.orthographic = true;
            _cam.orthographicSize = _zoom;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 100f;
            _cam.depth = -100;
            _cam.enabled = false; // manual render only
            _cam.targetTexture = _rt;
        }

        private Vector2 WorldToViewport(Vector3 worldPos, Rect drawn)
        {
            Vector3 sp = _cam.WorldToScreenPoint(worldPos);
            return new Vector2(
                drawn.x + (sp.x / RT_W) * drawn.width,
                drawn.y + (1f - sp.y / RT_H) * drawn.height);
        }

        private Rect GetDrawnRect(Rect vp)
        {
            float vpAspect = vp.width / vp.height;
            float rtAspect = (float)RT_W / RT_H;
            if (rtAspect > vpAspect)
            {
                float w = vp.width, h = w / rtAspect;
                return new Rect(vp.x, vp.y + (vp.height - h) / 2, w, h);
            }
            float fh = vp.height, fw = fh * rtAspect;
            return new Rect(vp.x + (vp.width - fw) / 2, vp.y, fw, fh);
        }

        private static void EnsureTextures()
        {
            if (_dotDefault != null) return;
            _dotDefault  = MakeDot(new Color(0.6f, 0.6f, 0.6f, 0.9f));
            _dotSprite   = MakeDot(new Color(0.3f, 0.8f, 0.9f, 0.9f));
            _dotSelected = MakeDot(Color.yellow, 12);
            _dotOverride = MakeDot(new Color(1f, 0.6f, 0.2f, 0.9f));

            // Line material for GL bone connections
            if (_lineMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    _lineMaterial = new Material(shader);
                    _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                    _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _lineMaterial.SetInt("_Cull", 0);
                    _lineMaterial.SetInt("_ZWrite", 0);
                }
            }
        }

        private static Texture2D MakeDot(Color color, int size = 8)
        {
            var tex = new Texture2D(size, size);
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f;
                tex.SetPixel(x, y, Mathf.Sqrt(dx * dx + dy * dy) <= c ? color : Color.clear);
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        // ═══════════════════════════════════════════════════════════════
        //  NPC BUILDER HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Fill all sprite bones of the current NPC with grafts from sourceNpcId.
        /// For each sprite bone, sets SpriteFrom = "sourceNpcId/boneName" if the
        /// source NPC has a sprite with a matching name, or "sourceNpcId" (same bone
        /// name) otherwise. Existing grafts/custom sprites are overwritten.
        /// </summary>
        private void FillAllSpritesFrom(SpriteOverrideDef ovr, string sourceNpcId)
        {
            // Get source NPC's sprite bone names
            var sourceSprites = ExtractNpcSprites(sourceNpcId);
            var sourceBoneNames = new HashSet<string>(sourceSprites.Keys);
            int filled = 0, matched = 0, unmatched = 0;

            foreach (var h in _handles)
            {
                if (!h.HasSpriteRenderer) continue;

                if (!ovr.Bones.ContainsKey(h.Name))
                    ovr.Bones[h.Name] = new BoneOverride();

                if (sourceBoneNames.Contains(h.Name))
                {
                    // Exact name match — graft same-named bone
                    ovr.Bones[h.Name].SpriteFrom = $"{sourceNpcId}/{h.Name}";
                    matched++;
                }
                else
                {
                    // No match — set to source NPC (will try same bone name at runtime)
                    ovr.Bones[h.Name].SpriteFrom = sourceNpcId;
                    unmatched++;
                }
                filled++;
            }

            Plugin.Log.LogInfo($"[SpriteEditor] FillAllSpritesFrom '{sourceNpcId}': " +
                $"{filled} sprite bones, {matched} matched, {unmatched} unmatched");
        }

        /// <summary>
        /// Import all bone names and their default transforms from a source NPC
        /// into the sprite definition's Bones dictionary. Existing bone overrides
        /// are preserved; only new (missing) bones are added with identity transforms.
        /// </summary>
        private void ImportBonesFrom(SpriteOverrideDef ovr, string sourceNpcId)
        {
            NPCData srcNpc = DataHelper.GetExistingNPC(sourceNpcId);
            if (srcNpc?.GameObjectAnimated == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] ImportBonesFrom: no prefab for '{sourceNpcId}'");
                return;
            }

            var temp = Object.Instantiate(srcNpc.GameObjectAnimated);
            temp.SetActive(false);

            var allBones = new Dictionary<string, Transform>();
            var allSRs = new Dictionary<string, SpriteRenderer>();
            BoneHierarchyUtils.CollectBones(temp.transform, allBones, allSRs);
            int imported = 0, skipped = 0;

            foreach (var kvp in allBones)
            {
                string boneName = kvp.Key;
                if (ovr.Bones.ContainsKey(boneName))
                { skipped++; continue; }

                // Add with identity override (no delta from rest pose)
                ovr.Bones[boneName] = new BoneOverride();
                imported++;
            }

            Object.Destroy(temp);
            Plugin.Log.LogInfo($"[SpriteEditor] ImportBonesFrom '{sourceNpcId}': " +
                $"{imported} bones imported, {skipped} already present");
        }

        // ═══════════════════════════════════════════════════════════════
        //  BRANCH GRAFTING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Clone the entire bone branch for a sprite from a source NPC.
        /// If the sprite has a SpriteSkin, clones from the SpriteSkin's rootBone down,
        /// bringing all deformation bones along so the sprite renders correctly.
        /// If no SpriteSkin, clones just the sprite GameObject.
        /// The returned GO is unparented — caller must parent it and track it for cleanup.
        /// </summary>
        public static GameObject CloneNpcBranch(string sourceNpcId, string sourceBoneName)
        {
            NPCData npcData = DataHelper.GetExistingNPC(sourceNpcId);
            if (npcData?.GameObjectAnimated == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] CloneNpcBranch: no prefab for NPC '{sourceNpcId}'");
                return null;
            }

            var temp = Object.Instantiate(npcData.GameObjectAnimated);
            temp.SetActive(false);

            // Find the source sprite bone in the temp hierarchy
            Transform spriteT = BoneHierarchyUtils.FindRecursive(temp.transform, sourceBoneName);
            if (spriteT == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] CloneNpcBranch: bone '{sourceBoneName}' not found in NPC '{sourceNpcId}'");
                Object.Destroy(temp);
                return null;
            }

            // Check for SpriteSkin to determine the branch root
            var skin = spriteT.GetComponent<SpriteSkin>();
            Transform branchRoot;

            if (skin != null && skin.rootBone != null)
            {
                // The SpriteSkin rootBone is the top of the bone subtree that drives this sprite.
                // Clone from rootBone down — this brings all deformation bones.
                branchRoot = skin.rootBone;

                // If the sprite is NOT a descendant of rootBone, reparent it under rootBone
                // so it's included in the clone.
                if (!IsDescendantOf(spriteT, branchRoot))
                {
                    spriteT.SetParent(branchRoot, true);
                }

                Plugin.Log.LogInfo($"[SpriteEditor] CloneNpcBranch: '{sourceBoneName}' has SpriteSkin, branch root='{branchRoot.name}'");
            }
            else
            {
                // No SpriteSkin — just the sprite node itself
                branchRoot = spriteT;
                Plugin.Log.LogInfo($"[SpriteEditor] CloneNpcBranch: '{sourceBoneName}' has no SpriteSkin, cloning just the sprite GO");
            }

            // Detach branch from the temp hierarchy so Instantiate only clones this subtree
            branchRoot.SetParent(null, true);

            // Clone the branch — Unity auto-remaps internal references (SpriteSkin.boneTransforms etc)
            var clone = Object.Instantiate(branchRoot.gameObject);
            clone.name = branchRoot.name; // remove "(Clone)" suffix

            // Enable all SpriteRenderers in the clone (the temp was SetActive(false))
            clone.SetActive(true);

            // Cleanup
            Object.Destroy(branchRoot.gameObject); // the detached original
            Object.Destroy(temp); // the rest of the temp prefab

            return clone;
        }

        /// <summary>Check if child is a descendant of ancestor.</summary>
        private static bool IsDescendantOf(Transform child, Transform ancestor)
        {
            Transform current = child.parent;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = current.parent;
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE LOADING & CACHING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Extract all bone sprites from an existing NPC's prefab (cached).</summary>
        public static Dictionary<string, Sprite> ExtractNpcSprites(string sourceNpcId)
        {
            if (_graftSpriteCache.TryGetValue(sourceNpcId, out var cached))
                return cached;

            NPCData npcData = DataHelper.GetExistingNPC(sourceNpcId);

            if (npcData?.GameObjectAnimated == null)
            {
                _graftSpriteCache[sourceNpcId] = new Dictionary<string, Sprite>();
                return _graftSpriteCache[sourceNpcId];
            }

            var temp = Object.Instantiate(npcData.GameObjectAnimated);
            temp.SetActive(false);
            var sprites = new Dictionary<string, Sprite>();
            CollectSpritesRecursive(temp.transform, sprites);
            Object.Destroy(temp);

            _graftSpriteCache[sourceNpcId] = sprites;
            return sprites;
        }

        private static void CollectSpritesRecursive(Transform parent, Dictionary<string, Sprite> dict)
        {
            foreach (Transform child in parent)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                    dict[child.name] = sr.sprite;
                if (child.childCount > 0)
                    CollectSpritesRecursive(child, dict);
            }
        }

        /// <summary>Load a Texture2D from disk (cached).</summary>
        private static Texture2D LoadTexture(string fullPath)
        {
            if (_textureCache.TryGetValue(fullPath, out var cached) && cached != null)
                return cached;

            if (!File.Exists(fullPath))
            {
                Plugin.Log.LogWarning($"[SpriteEditor] Texture not found: {fullPath}");
                return null;
            }

            byte[] data = File.ReadAllBytes(fullPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.filterMode = FilterMode.Bilinear;

            _textureCache[fullPath] = tex;
            return tex;
        }

        /// <summary>Build a Sprite from a SpriteDef, loading the texture from disk.</summary>
        public static Sprite CreateSpriteFromDef(SpriteDef def, string zoneId, string fallbackSheet, Sprite originalSprite)
        {
            string imagePath = !string.IsNullOrEmpty(def.ImagePath) ? def.ImagePath : fallbackSheet;
            if (string.IsNullOrEmpty(imagePath)) return null;

            string fullPath = Path.Combine(ModRegistry.GetZoneFolder(zoneId), "textures", imagePath);
            var tex = LoadTexture(fullPath);
            if (tex == null) return null;

            Rect rect;
            if (def.Rect != null && def.Rect.Length >= 4)
                rect = new Rect(def.Rect[0], def.Rect[1], def.Rect[2], def.Rect[3]);
            else
                rect = new Rect(0, 0, tex.width, tex.height);

            float ppu = def.PPU > 0 ? def.PPU : (originalSprite?.pixelsPerUnit ?? 100f);
            var pivot = new Vector2(def.PivotX, def.PivotY);

            return Sprite.Create(tex, rect, pivot, ppu);
        }

        /// <summary>Extract bone names (that have SpriteRenderers) from an NPC prefab.</summary>
        public static List<string> ExtractNpcBoneNames(string npcId)
        {
            var sprites = ExtractNpcSprites(npcId);
            return sprites.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>Apply AllIn1SpriteShader effects to a collection of SpriteRenderers at runtime.</summary>
        public static void ApplyShaderEffectsToRenderers(System.Collections.Generic.IEnumerable<SpriteRenderer> renderers, SpriteOverrideDef ovr)
        {
            FindAllIn1Shader();
            if (_allIn1Shader == null) return;

            foreach (var sr in renderers)
            {
                if (sr == null || sr.sprite == null) continue;
                sr.sharedMaterial = CreateShaderMaterial(sr, ovr);
            }
        }

        /// <summary>Clear all sprite caches. Call on zone reload or cleanup.</summary>
        public static void ClearSpriteCache()
        {
            _graftSpriteCache.Clear();
            _textureCache.Clear();
        }

        // ═══════════════════════════════════════════════════════════════
        //  STATIC: Apply overrides to any NPC's transform hierarchy
        //  Called from the NPCItem.Init() postfix patch
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply sprite overrides from the ZoneDef to an NPCItem's animated model.
        /// Called after NPCItem.Init() via Harmony postfix.
        /// </summary>
        public static void ApplyOverridesToNpcItem(NPCItem npcItem)
        {
            if (ZoneEditingService.CurrentZone == null) return;
            if (npcItem?.NPC == null) return;

            string npcId = npcItem.NPC.GameName;
            string zoneId = ZoneEditingService.CurrentZone.ZoneId;

            // Resolve sprite definition: NPC → NpcDef → SpriteSource → Sprites dict
            SpriteOverrideDef overrideDef = null;
            string baseId = ModRegistry.StripVariantSuffix(npcId);
            if (ZoneEditingService.CurrentZone.Npcs.TryGetValue(baseId, out var npcDef))
                overrideDef = ModRegistry.ResolveSpriteDefForNpc(ZoneEditingService.CurrentZone, npcDef);

            if (overrideDef == null) return;

            // Skip if no actual overrides exist
            if (overrideDef.Bones.Count == 0 && overrideDef.CustomSprites.Count == 0 &&
                overrideDef.ScaleMultiplier == 1f &&
                overrideDef.OffsetX == 0f && overrideDef.OffsetY == 0f &&
                !overrideDef.FlipX && !overrideDef.FlipY &&
                string.IsNullOrEmpty(overrideDef.ModelTintHex) &&
                overrideDef.ModelAlpha >= 1f &&
                !overrideDef.UseShaderEffects &&
                (overrideDef.AnimOverrides == null || overrideDef.AnimOverrides.Count == 0))
                return;

            ApplyOverridesToTransform(npcItem.transform, overrideDef, zoneId);
        }

        /// <summary>Walk the transform hierarchy and apply all sprite overrides.</summary>
        private static void ApplyOverridesToTransform(Transform root, SpriteOverrideDef overrideDef, string zoneId)
        {
            // Find the animated model root (first child with Animator or named after the NPC)
            Transform animRoot = null;
            foreach (Transform child in root)
            {
                if (child.GetComponent<Animator>() != null)
                {
                    animRoot = child;
                    break;
                }
            }

            // If no direct Animator child, search deeper
            if (animRoot == null)
            {
                var anim = root.GetComponentInChildren<Animator>();
                if (anim != null)
                    animRoot = anim.transform;
            }

            if (animRoot == null) return;

            // 1. Apply custom sprites (when defined)
            if (overrideDef.CustomSprites.Count > 0)
            {
                ApplyCustomSpritesRecursive(animRoot, overrideDef, zoneId);
            }

            // 2. Apply grafts (any bone with SpriteFrom, works in any mode)
            bool hasGrafts = overrideDef.Bones.Values.Any(b => !string.IsNullOrEmpty(b.SpriteFrom));
            if (hasGrafts)
            {
                ApplyGraftsRecursive(animRoot, overrideDef.Bones);
            }

            // 3. Apply global overrides to the animated root
            if (overrideDef.ScaleMultiplier != 1f)
            {
                animRoot.localScale *= overrideDef.ScaleMultiplier;
            }
            if (overrideDef.OffsetX != 0f || overrideDef.OffsetY != 0f)
            {
                animRoot.localPosition += new Vector3(overrideDef.OffsetX, overrideDef.OffsetY, 0f);
            }

            // 3b. Model-wide flip
            if (overrideDef.FlipX || overrideDef.FlipY)
            {
                var s = animRoot.localScale;
                if (overrideDef.FlipX) s.x = -Mathf.Abs(s.x);
                if (overrideDef.FlipY) s.y = -Mathf.Abs(s.y);
                animRoot.localScale = s;
            }

            // 3c. Model-wide tint + alpha
            Color modelTint = Color.white;
            bool hasModelTint = !string.IsNullOrEmpty(overrideDef.ModelTintHex) &&
                                ColorUtility.TryParseHtmlString(overrideDef.ModelTintHex, out modelTint);
            float modelAlpha = Mathf.Clamp01(overrideDef.ModelAlpha);
            if (hasModelTint || modelAlpha < 1f)
            {
                ApplyModelColorRecursive(animRoot, hasModelTint ? modelTint : Color.white, modelAlpha);
            }

            // 4. Apply per-bone overrides (transform, color, visibility, flip, alpha, sort)
            if (overrideDef.Bones.Count > 0)
            {
                ApplyBoneOverridesRecursive(animRoot, overrideDef.Bones);
            }

            // 5. Apply shader effects (AllIn1SpriteShader material swap)
            if (overrideDef.UseShaderEffects)
            {
                ApplyShaderEffectsRuntime(animRoot, overrideDef);
            }
        }

        /// <summary>Apply model-wide tint color and alpha to all SpriteRenderers.</summary>
        private static void ApplyModelColorRecursive(Transform parent, Color tint, float alpha)
        {
            foreach (Transform child in parent)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    Color c = tint;
                    c.a = alpha;
                    sr.color = c;
                }
                if (child.childCount > 0)
                    ApplyModelColorRecursive(child, tint, alpha);
            }
        }

        /// <summary>Apply AllIn1SpriteShader effects at runtime (for in-game NPCs).
        /// Walks the full transform hierarchy recursively.</summary>
        private static void ApplyShaderEffectsRuntime(Transform parent, SpriteOverrideDef ovr)
        {
            FindAllIn1Shader();
            if (_allIn1Shader == null) return;
            ApplyShaderEffectsRecursive(parent, ovr);
        }

        private static void ApplyShaderEffectsRecursive(Transform parent, SpriteOverrideDef ovr)
        {
            foreach (Transform child in parent)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    sr.sharedMaterial = CreateShaderMaterial(sr, ovr);
                }
                if (child.childCount > 0)
                    ApplyShaderEffectsRecursive(child, ovr);
            }
        }

        /// <summary>Replace bone sprites from custom PNG files on disk.</summary>
        private static void ApplyCustomSpritesRecursive(Transform parent, SpriteOverrideDef overrideDef, string zoneId)
        {
            foreach (Transform child in parent)
            {
                if (overrideDef.CustomSprites.TryGetValue(child.name, out var spriteDef))
                {
                    var sr = child.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        var newSprite = CreateSpriteFromDef(spriteDef, zoneId, overrideDef.Spritesheet, sr.sprite);
                        if (newSprite != null)
                            sr.sprite = newSprite;
                    }
                }
                if (child.childCount > 0)
                    ApplyCustomSpritesRecursive(child, overrideDef, zoneId);
            }
        }

        /// <summary>Replace bone sprites by grafting from other NPC prefabs.</summary>
        private static void ApplyGraftsRecursive(Transform parent, Dictionary<string, BoneOverride> overrides)
        {
            foreach (Transform child in parent)
            {
                if (overrides.TryGetValue(child.name, out var bo) && !string.IsNullOrEmpty(bo.SpriteFrom))
                {
                    var sr = child.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
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
                            sourceBone = child.name;
                        }

                        var sprites = ExtractNpcSprites(sourceNpc);
                        if (sprites.TryGetValue(sourceBone, out var sprite))
                            sr.sprite = sprite;
                    }
                }
                if (child.childCount > 0)
                    ApplyGraftsRecursive(child, overrides);
            }
        }

        private static void ApplyBoneOverridesRecursive(Transform parent, Dictionary<string, BoneOverride> overrides)
        {
            foreach (Transform child in parent)
            {
                if (overrides.TryGetValue(child.name, out var bo))
                {
                    // Position offset (additive)
                    if (bo.PosX != 0f || bo.PosY != 0f)
                        child.localPosition += new Vector3(bo.PosX, bo.PosY, 0f);

                    // Rotation (additive)
                    if (bo.Rotation != 0f)
                        child.localEulerAngles = new Vector3(0f, 0f, child.localEulerAngles.z + bo.Rotation);

                    // Scale (multiplicative, 1.0 = no change)
                    if (bo.ScaleX != 1f || bo.ScaleY != 1f)
                        child.localScale = new Vector3(
                            child.localScale.x * bo.ScaleX,
                            child.localScale.y * bo.ScaleY,
                            child.localScale.z);

                    // Visibility
                    var sr = child.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        if (!bo.Visible)
                            sr.enabled = false;

                        // Sorting offset
                        if (bo.SortingOffset != 0)
                            sr.sortingOrder += bo.SortingOffset;

                        // Color tint
                        if (!string.IsNullOrEmpty(bo.ColorHex))
                        {
                            if (ColorUtility.TryParseHtmlString(bo.ColorHex, out var color))
                                sr.color = color;
                        }

                        // Flip
                        if (bo.FlipX) sr.flipX = !sr.flipX;
                        if (bo.FlipY) sr.flipY = !sr.flipY;

                        // Per-bone alpha (one-time application at init — multiplicative is safe)
                        if (bo.Alpha < 1f)
                        {
                            Color c = sr.color;
                            c.a *= Mathf.Clamp01(bo.Alpha);
                            sr.color = c;
                        }
                    }
                }

                // Recurse into children
                if (child.childCount > 0)
                    ApplyBoneOverridesRecursive(child, overrides);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════

        public void Cleanup()
        {
            DestroyPreview();
            ClearSpriteCache();
            if (_cam != null)
            {
                _cam.targetTexture = null;
                Object.Destroy(_cam.gameObject);
                _cam = null;
            }
            if (_rt != null)
            {
                if (_rt.IsCreated()) _rt.Release();
                Object.Destroy(_rt);
                _rt = null;
            }
        }
    }
}
