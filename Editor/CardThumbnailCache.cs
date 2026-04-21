using System;
using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Renders and caches small thumbnail textures of cards and items using
    /// the actual CardItem prefab (full card frame, art, text, energy cost, etc.).
    /// Falls back to card art sprite when the CardItem prefab is unavailable.
    /// </summary>
    public static class CardThumbnailCache
    {
        private static readonly Dictionary<string, Texture2D> _cache = new();
        private static Camera _thumbCam;
        private static RenderTexture _thumbRT;
        private const int ThumbSize = 128;
        private static readonly Vector3 ThumbOrigin = new(-8000f, 0f, 0f);

        /// <summary>Get or render a thumbnail for a card/item ID.</summary>
        public static Texture2D GetThumbnail(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (_cache.TryGetValue(cardId, out var cached)) return cached; // null = already tried

            var tex = RenderThumbnail(cardId);
            _cache[cardId] = tex;
            return tex;
        }

        /// <summary>Invalidate a specific thumbnail (e.g. after edits).</summary>
        public static void Invalidate(string cardId)
        {
            if (_cache.TryGetValue(cardId, out var tex))
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                _cache.Remove(cardId);
            }
        }

        /// <summary>Clear all cached thumbnails.</summary>
        public static void ClearAll()
        {
            foreach (var tex in _cache.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            _cache.Clear();
            DestroyRenderResources();
        }

        private static Texture2D RenderThumbnail(string cardId)
        {
            // Try full CardItem prefab rendering first
            var gm = GameManager.Instance;
            if (gm != null && gm.CardPrefab != null)
            {
                try
                {
                    var go = UnityEngine.Object.Instantiate(
                        gm.CardPrefab, ThumbOrigin, Quaternion.identity);
                    go.name = $"[CardThumb] {cardId}";

                    var ci = go.GetComponent<CardItem>();
                    if (ci != null)
                    {
                        ci.Init();
                        ci.SetCard(cardId, false, null, null, true);
                        go.transform.localPosition = ThumbOrigin;
                        go.transform.localScale = Vector3.one;

                        // Compute bounds from all renderers
                        var renderers = go.GetComponentsInChildren<Renderer>(false);
                        if (renderers.Length > 0)
                        {
                            Bounds bounds = renderers[0].bounds;
                            for (int i = 1; i < renderers.Length; i++)
                                bounds.Encapsulate(renderers[i].bounds);

                            EnsureRenderResources();
                            _thumbCam.transform.position = new Vector3(
                                bounds.center.x, bounds.center.y, ThumbOrigin.z - 10f);
                            float sizeX = bounds.extents.x;
                            float sizeY = bounds.extents.y;
                            _thumbCam.orthographicSize = Mathf.Max(sizeX, sizeY) * 1.1f;
                            _thumbCam.targetTexture = _thumbRT;
                            _thumbCam.Render();

                            RenderTexture.active = _thumbRT;
                            var thumb = new Texture2D(ThumbSize, ThumbSize, TextureFormat.RGBA32, false);
                            thumb.ReadPixels(new Rect(0, 0, ThumbSize, ThumbSize), 0, 0);
                            thumb.Apply();
                            RenderTexture.active = null;

                            UnityEngine.Object.DestroyImmediate(go);
                            return thumb;
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(go);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug($"[CardThumb] Prefab render failed for '{cardId}': {ex.Message}");
                }
            }

            // Fallback: extract card art sprite as texture
            try
            {
                var cardData = DataHelper.GetCard(cardId);
                Sprite spr = cardData?.Sprite;
                if (spr != null)
                    return ViewportPreview.GetSpriteTexture(spr);
            }
            catch { }

            return null;
        }

        private static void EnsureRenderResources()
        {
            if (_thumbRT == null)
            {
                _thumbRT = new RenderTexture(ThumbSize, ThumbSize, 16, RenderTextureFormat.ARGB32);
                _thumbRT.Create();
            }

            if (_thumbCam == null)
            {
                var camGO = new GameObject("[CardThumbCam]");
                camGO.hideFlags = HideFlags.HideAndDontSave;
                _thumbCam = camGO.AddComponent<Camera>();
                _thumbCam.orthographic = true;
                _thumbCam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
                _thumbCam.clearFlags = CameraClearFlags.SolidColor;
                _thumbCam.cullingMask = ~0;
                _thumbCam.nearClipPlane = 0.1f;
                _thumbCam.farClipPlane = 100f;
                _thumbCam.enabled = false; // manual render only
                UnityEngine.Object.DontDestroyOnLoad(camGO);
            }
        }

        private static void DestroyRenderResources()
        {
            if (_thumbRT != null) { _thumbRT.Release(); UnityEngine.Object.Destroy(_thumbRT); _thumbRT = null; }
            if (_thumbCam != null) { UnityEngine.Object.Destroy(_thumbCam.gameObject); _thumbCam = null; }
        }
    }
}
