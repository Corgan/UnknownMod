using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Shared sprite utilities used by both the editor (SpriteSkinEditor) and runtime
    /// (NpcPrefabBuilder). Provides NPC sprite extraction, custom sprite loading,
    /// shader material creation, and caching.
    /// </summary>
    public static class SpriteUtils
    {
        // ═══════════════════════════════════════════════════════════════
        //  SPRITE EXTRACTION (from NPC prefabs)
        // ═══════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, Dictionary<string, Sprite>> _graftSpriteCache = new();

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

        /// <summary>Build a Sprite from a SpriteDef. First tries to resolve ImagePath as a
        /// sprite name (base game + mod sprites). Falls back to loading from disk.</summary>
        public static Sprite CreateSpriteFromDef(SpriteDef def, string modId, string fallbackSheet, Sprite originalSprite)
        {
            string imagePath = !string.IsNullOrEmpty(def.ImagePath) ? def.ImagePath : fallbackSheet;
            if (string.IsNullOrEmpty(imagePath)) return null;

            // 1. Try resolving as a named sprite (returns the already-cropped Sprite)
            var named = FindSpriteByName(imagePath);
            if (named != null)
            {
                // If the SpriteDef has non-default properties, re-create the sprite
                // with the requested parameters instead of returning the shared reference.
                bool hasCustomRect = def.Rect != null && def.Rect.Length >= 4;
                bool hasCustomPPU = def.PPU > 0 && Mathf.Abs(def.PPU - named.pixelsPerUnit) > 0.01f;
                bool hasCustomPivot = Mathf.Abs(def.PivotX - 0.5f) > 0.001f || Mathf.Abs(def.PivotY - 0.5f) > 0.001f;
                if (!hasCustomRect && !hasCustomPPU && !hasCustomPivot)
                    return named;

                Rect r = hasCustomRect
                    ? ClampRect(def.Rect, named.texture.width, named.texture.height)
                    : named.rect;
                float namedPpu = def.PPU > 0 ? def.PPU : named.pixelsPerUnit;
                return Sprite.Create(named.texture, r, new Vector2(def.PivotX, def.PivotY), namedPpu);
            }

            // 2. Fall back to loading texture from disk (requires a valid modId for folder lookup)
            if (string.IsNullOrEmpty(modId)) return null;
            string fullPath = Path.Combine(ModProjectLoader.ModFolder(modId), "sprites", imagePath);
            var tex = LoadTexture(fullPath);
            if (tex == null) return null;

            Rect rect;
            if (def.Rect != null && def.Rect.Length >= 4)
                rect = ClampRect(def.Rect, tex.width, tex.height);
            else
                rect = new Rect(0, 0, tex.width, tex.height);

            float ppu = def.PPU > 0 ? def.PPU : (originalSprite?.pixelsPerUnit ?? 100f);
            var pivot = new Vector2(def.PivotX, def.PivotY);

            return Sprite.Create(tex, rect, pivot, ppu);
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
