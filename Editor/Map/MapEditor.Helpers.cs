using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;


namespace UnknownMod.Editor
{
    public partial class MapEditor
    {
        // 
        //  HELPERS
        // 

        private Vector3 GetNodeWorldPos(string nodeId)
        {
            if (_nodeGOs.TryGetValue(nodeId, out var go) && go != null)
                return go.transform.position;
            return PreviewOrigin;
        }

        // 
        //  NODE SPRITE CACHE
        // 

        /// <summary>Names of sprites we want to cache from the game for node rendering.</summary>
        private static readonly string[] _wantedSprites =
        {
            "mapnode", "mapnode_active", "mapnode_visited", "mapnode_current", "mapnode_broken",
            "nodeIconCombat", "nodeIconEvent", "nodeIconShop", "nodeIconMap",
            "nodeIconBoss", "nodeIconHeal", "nodeIconCraft", "nodeIconCards",
            "nodeIconLock", "nodeIconQuestBegin", "nodeIconQuestEnd",
            "nodeIconEventGreen", "nodeIconEventBlue", "nodeIconEventPurple", "nodeIconEventTeal",
            "boss", "finalboss",
        };

        /// <summary>Populate the sprite cache from loaded game assets. Safe to call repeatedly.</summary>
        private static void EnsureNodeSprites()
        {
            if (_spriteCacheInit) return;
            _spriteCacheInit = true;

            var wanted = new HashSet<string>(_wantedSprites, System.StringComparer.OrdinalIgnoreCase);
            var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in allSprites)
            {
                if (s == null) continue;
                if (wanted.Contains(s.name) && !_spriteCache.ContainsKey(s.name))
                    _spriteCache[s.name] = s;
            }

            Plugin.Log.LogInfo($"[MapEditor] Cached {_spriteCache.Count}/{wanted.Count} node sprites");
        }

        /// <summary>Look up a sprite by name from the cache. Returns null if not found.</summary>
        private static Sprite FindCachedSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _spriteCache.TryGetValue(name, out var s);
            return s;
        }

        /// <summary>Fallback procedural sprite when game sprites aren't available.</summary>
        private static Sprite EnsureNodeFallback()
        {
            if (_nodeFallbackSprite != null) return _nodeFallbackSprite;
            // Create a small ellipse roughly matching mapnode proportions (64x40)
            int w = 64, h = 40;
            var tex = new Texture2D(w, h);
            float cx = w / 2f, cy = h / 2f;
            float rx = cx - 1f, ry = cy - 1f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x - cx + 0.5f) / rx;
                float dy = (y - cy + 0.5f) / ry;
                float dist = dx * dx + dy * dy;
                if (dist <= 0.85f)
                    tex.SetPixel(x, y, Color.white);
                else if (dist <= 1f)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - dist) / 0.15f)));
                else
                    tex.SetPixel(x, y, Color.clear);
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _nodeFallbackSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _nodeFallbackSprite;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE NAME CACHE (for sprite picker)
        // ═══════════════════════════════════════════════════════════════

        private static string[] _allSpriteNames;
        private static bool _allSpriteNamesInit;
        private static Dictionary<string, List<string>> _spriteGroups;
        private static string[] _spriteGroupNames;

        /// <summary>Invalidate the sprite name cache so it rebuilds on next access.
        /// Called after mod image sprites are loaded/cleared.</summary>
        public static void InvalidateSpriteNameCache()
        {
            _allSpriteNamesInit = false;
            _allSpriteNames = null;
            _spriteGroups = null;
            _spriteGroupNames = null;
        }

        /// <summary>Get all unique sprite names available in the game (lazy-cached).
        /// Used by the sprite picker searchable dropdown.</summary>
        public static string[] GetAllSpriteNames()
        {
            if (_allSpriteNamesInit) return _allSpriteNames;
            _allSpriteNamesInit = true;

            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var groups = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s == null || string.IsNullOrEmpty(s.name)) continue;
                if (!names.Add(s.name)) continue;

                string groupName = s.texture != null ? CleanGroupName(s.texture.name) : "(unknown)";
                if (!groups.TryGetValue(groupName, out var list))
                {
                    list = new List<string>();
                    groups[groupName] = list;
                }
                list.Add(s.name);
            }
            // Include mod-loaded image sprites
            foreach (var kvp in ModRegistry.ModImageSprites)
            {
                if (!names.Add(kvp.Key)) continue;
                string groupName = "(mod sprites)";
                if (!groups.TryGetValue(groupName, out var list))
                {
                    list = new List<string>();
                    groups[groupName] = list;
                }
                list.Add(kvp.Key);
            }

            _allSpriteNames = names.OrderBy(n => n).ToArray();

            // Sort sprite names within each group and build sorted group name list
            foreach (var list in groups.Values)
                list.Sort(System.StringComparer.OrdinalIgnoreCase);
            _spriteGroups = groups;
            _spriteGroupNames = groups.Keys.OrderBy(k => k).ToArray();

            Plugin.Log.LogInfo($"[MapEditor] Sprite picker: {_allSpriteNames.Length} sprites in {_spriteGroupNames.Length} groups");
            return _allSpriteNames;
        }

        /// <summary>Clean up a texture/atlas name into a readable group label.</summary>
        private static string CleanGroupName(string textureName)
        {
            if (string.IsNullOrEmpty(textureName)) return "(unknown)";

            // Strip Unity sprite atlas prefixes like "sactx-0-4096x4096-BC7-" or "sactx-0-2048x1024-Crunch-"
            if (textureName.StartsWith("sactx-"))
            {
                // Format: sactx-N-WxH-Format-descriptive-name-hash
                // Find the descriptive part after the format tag
                int dashCount = 0;
                int startIdx = 0;
                for (int i = 0; i < textureName.Length; i++)
                {
                    if (textureName[i] == '-')
                    {
                        dashCount++;
                        if (dashCount == 4) { startIdx = i + 1; break; }
                    }
                }
                if (startIdx > 0 && startIdx < textureName.Length)
                {
                    string desc = textureName.Substring(startIdx);
                    // Strip the trailing hash (last segment after -)
                    int lastDash = desc.LastIndexOf('-');
                    if (lastDash > 0)
                        desc = desc.Substring(0, lastDash);
                    return desc.Replace('-', ' ');
                }
            }
            return textureName;
        }

        /// <summary>Get the texture-based sprite groups (lazy-built alongside sprite names).</summary>
        public static Dictionary<string, List<string>> GetSpriteGroups()
        {
            if (_spriteGroups == null) GetAllSpriteNames();
            return _spriteGroups;
        }

        /// <summary>Get sorted group names for the sprite picker.</summary>
        public static string[] GetSpriteGroupNames()
        {
            if (_spriteGroupNames == null) GetAllSpriteNames();
            return _spriteGroupNames;
        }

        /// <summary>Find a sprite by name from all loaded game sprites and mod image sprites.</summary>
        public static Sprite FindGameSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // Try node cache first — but validate the sprite is still alive
            if (_spriteCache.TryGetValue(name, out var cached))
            {
                if (cached != null) return cached;
                _spriteCache.Remove(name); // stale destroyed reference — evict
            }
            // Try mod-loaded image sprites (exact match)
            if (ModRegistry.ModImageSprites.TryGetValue(name, out var modSprite))
            {
                _spriteCache[name] = modSprite;
                return modSprite;
            }
            // Try mod-loaded image sprites (prefixed: "<modId>_<name>")
            string suffix = "_" + name;
            foreach (var kvp in ModRegistry.ModImageSprites)
            {
                if (kvp.Value != null && kvp.Key.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    _spriteCache[name] = kvp.Value;
                    return kvp.Value;
                }
            }
            // Search all loaded sprites
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s != null && s.name == name)
                {
                    _spriteCache[name] = s; // cache for next time
                    return s;
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  VFX EFFECT NAME CACHE
        // ═══════════════════════════════════════════════════════════════

        private static string[] _vfxEffectNames;

        /// <summary>Get sorted list of all effect prefab names from Resources/Effects/.</summary>
        public static string[] GetVfxEffectNames()
        {
            if (_vfxEffectNames != null) return _vfxEffectNames;
            var prefabs = Resources.LoadAll<GameObject>("Effects");
            var names = new HashSet<string>();
            foreach (var go in prefabs)
                if (go != null) names.Add(go.name);
            var sorted = new List<string>(names);
            sorted.Sort(System.StringComparer.OrdinalIgnoreCase);
            _vfxEffectNames = sorted.ToArray();
            Plugin.Log.LogInfo($"[MapEditor] Cached {_vfxEffectNames.Length} VFX effect names");
            return _vfxEffectNames;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ROAD MATERIAL
        // ═══════════════════════════════════════════════════════════════

        private static Material _arrowSquaresMat;

        /// <summary>Find the game's arrowSquares material used for road rendering.</summary>
        private static Material FindArrowSquaresMaterial()
        {
            if (_arrowSquaresMat != null) return _arrowSquaresMat;

            // Try to find from existing zone road LineRenderers
            foreach (var lr in Resources.FindObjectsOfTypeAll<LineRenderer>())
            {
                if (lr.sharedMaterial != null && lr.sharedMaterial.name == "arrowSquares")
                {
                    _arrowSquaresMat = lr.sharedMaterial;
                    Plugin.Log.LogInfo("[MapEditor] Found arrowSquares material from game.");
                    return _arrowSquaresMat;
                }
            }

            // Fallback: search Materials directly
            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (mat.name == "arrowSquares")
                {
                    _arrowSquaresMat = mat;
                    Plugin.Log.LogInfo("[MapEditor] Found arrowSquares material from Resources.");
                    return _arrowSquaresMat;
                }
            }

            Plugin.Log.LogWarning("[MapEditor] arrowSquares material not found, using fallback.");
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  FALLBACK BACKGROUND
        // ═══════════════════════════════════════════════════════════════

        private static Sprite _fallbackBgSprite;

        /// <summary>Create a large solid-white sprite for use as a background when no map sprite exists.
        /// Sized to cover the typical viewport area so the arrowSquares road shader has opaque geometry.</summary>
        private static Sprite EnsureFallbackBackground()
        {
            if (_fallbackBgSprite != null) return _fallbackBgSprite;
            // 256x256 at 25 PPU = ~10x10 world units, large enough for the default viewport
            int size = 256;
            var tex = new Texture2D(size, size);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            _fallbackBgSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 25f);
            return _fallbackBgSprite;
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════

        public void Cleanup()
        {
            DestroyPreview();
            _vp?.Cleanup();
        }
    }
}
