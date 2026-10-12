using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Shared sprite utilities used by both the editor (VisualGraphEditor) and runtime. Provides NPC sprite extraction, custom sprite loading,
    /// shader material creation, and caching.
    /// </summary>
    public static class SpriteUtils
    {
        // ═══════════════════════════════════════════════════════════════
        //  SPRITE EXTRACTION (from NPC prefabs)
        // ═══════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, Dictionary<string, Sprite>> _graftSpriteCache = new();
        private static readonly Dictionary<(Sprite, bool, float, float, float), Sprite> _portraitSprites = new();

        /// <summary>A horizontally mirrored copy of a sprite (cached). Returns null for null input.</summary>
        public static Sprite FlipHorizontal(Sprite source) => TransformPortrait(source, true, 1f, 0f, 0f);

        /// <summary>
        /// A re-framed copy of a sprite with the same pixel size (cached): optionally mirrored, content zoomed by <paramref name="scale"/>
        /// about the centre (above 1 zooms in and crops, below 1 shrinks and pads with transparency), then shifted by
        /// <paramref name="offsetX"/>/<paramref name="offsetY"/> as fractions of the sprite size (Y is up). The visible rect is copied through a
        /// RenderTexture, so it works on non-readable and atlas textures. Returns the original sprite if nothing changes or the copy fails.
        /// </summary>
        public static Sprite TransformPortrait(Sprite source, bool flip, float scale, float offsetX, float offsetY)
        {
            if (source == null || source.texture == null) return source;
            if (scale <= 0.01f) scale = 1f;
            scale = Mathf.Clamp(scale, 0.1f, 4f);
            if (!flip && Mathf.Approximately(scale, 1f) && Mathf.Approximately(offsetX, 0f) && Mathf.Approximately(offsetY, 0f)) return source;
            var key = (source, flip, scale, offsetX, offsetY);
            if (_portraitSprites.TryGetValue(key, out var cached) && cached != null) return cached;
            RenderTexture rt = null;
            var previous = RenderTexture.active;
            try
            {
                // A tight-mesh sprite's textureRect is its trimmed opaque area; only atlas-packed sprites need it, the rest use the full rect.
                var rect = source.packed ? source.textureRect : source.rect;
                int w = Mathf.Max(1, (int)rect.width), h = Mathf.Max(1, (int)rect.height);
                var tex = source.texture;
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(tex, rt, new Vector2(w / (float)tex.width, h / (float)tex.height),
                    new Vector2(rect.x / tex.width, rect.y / tex.height));
                RenderTexture.active = rt;
                var temp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                temp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                var src = temp.GetPixels32();
                Object.Destroy(temp);

                var dst = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = ((y + 0.5f) / h - 0.5f - offsetY) / scale + 0.5f;
                    for (int x = 0; x < w; x++)
                    {
                        float u = ((x + 0.5f) / w - 0.5f - offsetX) / scale + 0.5f;
                        if (flip) u = 1f - u;
                        dst[y * w + x] = SampleBilinear(src, w, h, u * w - 0.5f, v * h - 0.5f, u, v);
                    }
                }

                var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = tex.name + "_portrait", filterMode = tex.filterMode, wrapMode = TextureWrapMode.Clamp };
                outTex.SetPixels32(dst);
                outTex.Apply(false, false);

                float px = source.pivot.x / w, py = source.pivot.y / h;
                if (flip) px = 1f - px;
                px = 0.5f + offsetX + (px - 0.5f) * scale;
                py = 0.5f + offsetY + (py - 0.5f) * scale;
                var border = source.border;
                bool plain = Mathf.Approximately(scale, 1f) && Mathf.Approximately(offsetX, 0f) && Mathf.Approximately(offsetY, 0f);
                var newBorder = !plain ? Vector4.zero : flip ? new Vector4(border.z, border.y, border.x, border.w) : border;
                var result = Sprite.Create(outTex, new Rect(0, 0, w, h), new Vector2(px, py), source.pixelsPerUnit, 0, SpriteMeshType.FullRect, newBorder);
                result.name = source.name + (flip ? "_flipped" : "_framed");
                _portraitSprites[key] = result;
                return result;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[SpriteUtils] Could not reframe sprite '{source.name}': {e.Message}");
                return source;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        // Bilinear sample at pixel coordinates (fx, fy); (u, v) are the normalised coordinates used for the out-of-bounds test.
        private static Color32 SampleBilinear(Color32[] src, int w, int h, float fx, float fy, float u, float v)
        {
            if (u < 0f || u >= 1f || v < 0f || v >= 1f) return new Color32(0, 0, 0, 0);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, h - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float tx = Mathf.Clamp01(fx - x0), ty = Mathf.Clamp01(fy - y0);
            var a = src[y0 * w + x0]; var b = src[y0 * w + x1]; var c = src[y1 * w + x0]; var d = src[y1 * w + x1];
            // Weight colour by alpha so transparent texels don't bleed dark fringes.
            float wa = (1 - tx) * (1 - ty) * a.a, wb = tx * (1 - ty) * b.a, wc = (1 - tx) * ty * c.a, wd = tx * ty * d.a;
            float alpha = wa + wb + wc + wd;
            if (alpha <= 0.0001f) return new Color32(0, 0, 0, 0);
            byte R(float f) => (byte)Mathf.Clamp(Mathf.RoundToInt(f / alpha), 0, 255);
            return new Color32(R(wa * a.r + wb * b.r + wc * c.r + wd * d.r), R(wa * a.g + wb * b.g + wc * c.g + wd * d.g),
                R(wa * a.b + wb * b.b + wc * c.b + wd * d.b), (byte)Mathf.Clamp(Mathf.RoundToInt(alpha), 0, 255));
        }

        /// <summary>
        /// Extract all bone sprites from an existing NPC's prefab (cached).
        /// NOTE: The returned Sprites are references to the prefab's shared sprite assets.
        /// Do NOT modify them directly — use Object.Instantiate(sprite) if mutation is needed.
        /// Instantiates a temp prefab to read sprites (necessary since prefab assets
        /// aren't activated and SpriteRenderers may not be populated until Instantiate).
        /// </summary>
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

        /// <summary>Extract bone names (that have SpriteRenderers) from an NPC prefab.</summary>
        public static List<string> ExtractNpcBoneNames(string npcId)
        {
            var sprites = ExtractNpcSprites(npcId);
            return sprites.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>Extract bone names (that have SpriteRenderers) from a hero skin prefab (SkinData.SkinGo).</summary>
        public static List<string> ExtractSkinBoneNames(string skinId)
        {
            var sprites = ExtractSkinSprites(skinId);
            return sprites.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>Extract sprites from a Hero Skin prefab (cached).</summary>
        public static Dictionary<string, Sprite> ExtractSkinSprites(string skinId)
        {
            string cacheKey = $"skin:{skinId}";
            if (_graftSpriteCache.TryGetValue(cacheKey, out var cached))
                return cached;

            SkinData skinData = DataHelper.GetSkin(skinId);
            if (skinData?.SkinGo == null)
            {
                _graftSpriteCache[cacheKey] = new Dictionary<string, Sprite>();
                return _graftSpriteCache[cacheKey];
            }

            var temp = Object.Instantiate(skinData.SkinGo);
            temp.SetActive(false);
            var sprites = new Dictionary<string, Sprite>();
            CollectSpritesRecursive(temp.transform, sprites);
            Object.Destroy(temp);

            _graftSpriteCache[cacheKey] = sprites;
            return sprites;
        }

        private static void CollectSpritesRecursive(Transform parent, Dictionary<string, Sprite> dict)
        {
            // Include root transform's own SpriteRenderer (CollectBones includes it)
            var rootSr = parent.GetComponent<SpriteRenderer>();
            if (rootSr != null && rootSr.sprite != null)
                dict[parent.name] = rootSr.sprite;

            foreach (Transform child in parent)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                    dict[child.name] = sr.sprite;
                if (child.childCount > 0)
                    CollectSpritesRecursive(child, dict);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE NAME LOOKUP
        // ═══════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, Sprite> _spriteNameCache = new();

        /// <summary>Find a sprite by name from mod image sprites and all loaded game sprites (cached).
        /// Returns null for names that don't exist (also cached to avoid repeated expensive lookups).</summary>
        public static Sprite FindSpriteByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // Check cache (may contain null for negative lookups)
            if (_spriteNameCache.TryGetValue(name, out var cached)) return cached;

            // Mod-loaded image sprites first (exact match)
            if (ModRegistry.ModImageSprites.TryGetValue(name, out var modSprite))
            {
                _spriteNameCache[name] = modSprite;
                return modSprite;
            }
            // Try prefixed name ("<modId>_<name>")
            string suffix = "_" + name;
            foreach (var kvp in ModRegistry.ModImageSprites)
            {
                if (kvp.Value != null && kvp.Key.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    _spriteNameCache[name] = kvp.Value;
                    return kvp.Value;
                }
            }

            // Search all loaded sprites in memory
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s != null && s.name == name)
                {
                    _spriteNameCache[name] = s;
                    return s;
                }
            }

            // Cache the miss to avoid repeating the expensive FindObjectsOfTypeAll scan
            _spriteNameCache[name] = null;
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  TEXTURE LOADING & CUSTOM SPRITES
        // ═══════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, Texture2D> _textureCache = new();

        /// <summary>Load a Texture2D from disk (cached).</summary>
        public static Texture2D LoadTexture(string fullPath)
        {
            if (_textureCache.TryGetValue(fullPath, out var cached) && cached != null)
                return cached;

            if (!File.Exists(fullPath))
            {
                Plugin.Log.LogWarning($"[SpriteUtils] Texture not found: {fullPath}");
                return null;
            }

            byte[] data = File.ReadAllBytes(fullPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.filterMode = FilterMode.Bilinear;

            _textureCache[fullPath] = tex;
            return tex;
        }

        /// <summary>Clamp a user-supplied rect to valid texture bounds.</summary>
        private static Rect ClampRect(float[] r, int texW, int texH)
        {
            float rx = Mathf.Clamp(r[0], 0, texW - 1);
            float ry = Mathf.Clamp(r[1], 0, texH - 1);
            float rw = Mathf.Clamp(r[2], 1, texW - rx);
            float rh = Mathf.Clamp(r[3], 1, texH - ry);
            return new Rect(rx, ry, rw, rh);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CACHE MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Clear all sprite caches. Call on zone reload or cleanup.</summary>
        public static void ClearSpriteCache()
        {
            _graftSpriteCache.Clear();
            _spriteNameCache.Clear();
            foreach (var tex in _textureCache.Values)
                if (tex != null) Object.Destroy(tex);
            _textureCache.Clear();
        }
    }
}
