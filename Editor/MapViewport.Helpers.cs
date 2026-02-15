using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;


namespace UnknownMod.Editor
{
    public partial class MapViewport
    {
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  HELPERS
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private Vector3 GetNodeWorldPos(string nodeId)
        {
            if (_nodeGOs.TryGetValue(nodeId, out var go) && go != null)
                return go.transform.position;
            return PreviewOrigin;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  NODE SPRITE CACHE
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

            Plugin.Log.LogInfo($"[MapViewport] Cached {_spriteCache.Count}/{wanted.Count} node sprites");
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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  ROAD MATERIAL
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
                    Plugin.Log.LogInfo("[MapViewport] Found arrowSquares material from game.");
                    return _arrowSquaresMat;
                }
            }

            // Fallback: search Materials directly
            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (mat.name == "arrowSquares")
                {
                    _arrowSquaresMat = mat;
                    Plugin.Log.LogInfo("[MapViewport] Found arrowSquares material from Resources.");
                    return _arrowSquaresMat;
                }
            }

            Plugin.Log.LogWarning("[MapViewport] arrowSquares material not found, using fallback.");
            return null;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  CLEANUP
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        public void Cleanup()
        {
            DestroyPreview();
            _vp?.Cleanup();
        }
    }
}
