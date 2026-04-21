using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Renders and caches small thumbnail textures of combat background prefabs.
    /// Uses the background prefab cache from EntityPreviewRenderer after the
    /// Combat scene has been additively loaded and extracted.
    /// </summary>
    public static class BackgroundThumbnailCache
    {
        private static readonly Dictionary<string, Texture2D> _cache = new();
        private static Camera _thumbCam;
        private static RenderTexture _thumbRT;
        // 16:9 aspect to match the game's combat camera
        private const int ThumbWidth = 192;
        private const int ThumbHeight = 108;
        private const float CombatOrthoSize = 5.4f;
        private static readonly Vector3 ThumbOrigin = new(-7000f, 0f, 0f);

        /// <summary>Get or render a thumbnail for a background enum name.</summary>
        public static Texture2D GetThumbnail(string bgName)
        {
            if (string.IsNullOrEmpty(bgName)) return null;
            if (_cache.TryGetValue(bgName, out var cached)) return cached; // null = already tried

            var tex = RenderThumbnail(bgName);
            _cache[bgName] = tex; // cache null too to avoid retries
            return tex;
        }

        /// <summary>Clear all cached thumbnails.</summary>
        public static void ClearAll()
        {
            foreach (var tex in _cache.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            _cache.Clear();
            DestroyRenderResources();
        }

        private static Texture2D RenderThumbnail(string bgName)
        {
            // Get background prefab from EntityPreviewRenderer's cache
            var prefab = EntityPreviewRenderer.GetBackgroundPrefab(bgName);
            if (prefab == null) return null;

            // Instantiate at offscreen position (different from SpriteSkin thumbs)
            var go = UnityEngine.Object.Instantiate(prefab, ThumbOrigin, Quaternion.identity);
            go.SetActive(true);
            go.name = $"[BgThumb] {bgName}";
            // Match game scale
            go.transform.localScale = new Vector3(0.545f, 0.545f, 1f);

            // Disable holiday overlays
            var halloween = go.transform.Find("halloween");
            if (halloween != null) halloween.gameObject.SetActive(false);
            var lunar = go.transform.Find("Lunar");
            if (lunar != null) lunar.gameObject.SetActive(false);

            // Disable Animators to prevent coroutine errors; sample first frame
            foreach (var animator in go.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;

            // Use the game's combat camera framing: centered at origin, orthoSize 5.4
            EnsureRenderResources();
            _thumbCam.transform.position = new Vector3(ThumbOrigin.x, ThumbOrigin.y, -10f);
            _thumbCam.orthographicSize = CombatOrthoSize;
            _thumbCam.targetTexture = _thumbRT;

            // Render
            _thumbCam.Render();

            // Read pixels
            RenderTexture.active = _thumbRT;
            var thumb = new Texture2D(ThumbWidth, ThumbHeight, TextureFormat.RGBA32, false);
            thumb.ReadPixels(new Rect(0, 0, ThumbWidth, ThumbHeight), 0, 0);
            thumb.Apply();
            RenderTexture.active = null;

            // Cleanup
            UnityEngine.Object.DestroyImmediate(go);
            return thumb;
        }

        private static void EnsureRenderResources()
        {
            if (_thumbRT == null)
            {
                _thumbRT = new RenderTexture(ThumbWidth, ThumbHeight, 16, RenderTextureFormat.ARGB32);
                _thumbRT.Create();
            }

            if (_thumbCam == null)
            {
                var camGO = new GameObject("[BgThumbCam]");
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
