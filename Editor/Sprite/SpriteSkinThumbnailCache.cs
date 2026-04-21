using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Renders and caches small thumbnail textures of SpriteSkin definitions
    /// in their idle pose with overrides applied. Used by grid pickers for
    /// visual selection of SpriteSkins.
    /// </summary>
    public static class SpriteSkinThumbnailCache
    {
        private static readonly Dictionary<string, Texture2D> _cache = new();
        private static Camera _thumbCam;
        private static RenderTexture _thumbRT;
        private const int ThumbSize = 128;
        private static readonly Vector3 ThumbOrigin = new(-6000f, 0f, 0f);

        /// <summary>Get or render a thumbnail for a SpriteSkin override def.</summary>
        public static Texture2D GetThumbnail(string defId, CharacterOverrideDef def)
        {
            if (_cache.TryGetValue(defId, out var cached))
                return cached;

            var tex = RenderThumbnail(defId, def);
            _cache[defId] = tex;  // cache null too to avoid repeated render attempts
            return tex;
        }

        /// <summary>Invalidate a specific thumbnail (e.g. after override edits).</summary>
        public static void Invalidate(string defId)
        {
            if (_cache.TryGetValue(defId, out var tex))
            {
                if (tex != null) Object.Destroy(tex);
                _cache.Remove(defId);
            }
        }

        /// <summary>Clear all cached thumbnails.</summary>
        public static void ClearAll()
        {
            foreach (var tex in _cache.Values)
                if (tex != null) Object.Destroy(tex);
            _cache.Clear();
            DestroyRenderResources();
        }

        /// <summary>Render thumbnail for a CharacterOverrideDef using its own BaseSprite.</summary>
        private static Texture2D RenderThumbnail(string defId, CharacterOverrideDef def)
        {
            string baseNpcId = !string.IsNullOrEmpty(def?.BaseSprite) ? def.BaseSprite : defId;
            return RenderThumbnailWithBase(defId, baseNpcId, def);
        }

        /// <summary>Core render: build overridden model, sample idle, snapshot.</summary>
        private static Texture2D RenderThumbnailWithBase(string cacheKey, string baseNpcId, CharacterOverrideDef overrideDef)
        {
            if (string.IsNullOrEmpty(baseNpcId)) return null;
            NPCData npcData = DataHelper.GetExistingNPC(baseNpcId);
            if (npcData?.GameObjectAnimated == null) return null;

            GameObject sourcePrefab = npcData.GameObjectAnimated;
            GameObject customPrefab = null;

            // Build custom prefab with overrides applied (if any)
            if (overrideDef != null)
            {
                string thumbEntityId = $"_thumb_{cacheKey}";
                NpcPrefabBuilder.InvalidateCache(thumbEntityId);
                customPrefab = NpcPrefabBuilder.BuildCustomPrefab(
                    thumbEntityId, sourcePrefab, overrideDef, "_thumbnails");
            }

            // Instantiate the final prefab (custom or base)
            var prefab = customPrefab ?? sourcePrefab;
            var go = Object.Instantiate(prefab, ThumbOrigin, Quaternion.identity);
            go.SetActive(true);
            go.name = $"[Thumb] {cacheKey}";

            // Sample idle animation at t=0
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
                var clips = animator.runtimeAnimatorController?.animationClips;
                if (clips != null && clips.Length > 0)
                {
                    AnimationClip idleClip = null;
                    foreach (var clip in clips)
                    {
                        if (clip != null && clip.name.ToLower().Contains("idle"))
                        { idleClip = clip; break; }
                    }
                    if (idleClip == null) idleClip = clips[0];
                    idleClip.SampleAnimation(go, 0f);
                }
            }

            // Compute bounds of all visible SpriteRenderers
            var renderers = go.GetComponentsInChildren<SpriteRenderer>(false);
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(go);
                CleanupThumbPrefab(cacheKey, customPrefab);
                return null;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i].sprite != null)
                    bounds.Encapsulate(renderers[i].bounds);
            }

            // Setup offscreen camera
            EnsureRenderResources();
            _thumbCam.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
            float aspect = (float)ThumbSize / ThumbSize;
            float sizeX = bounds.extents.x / aspect;
            float sizeY = bounds.extents.y;
            _thumbCam.orthographicSize = Mathf.Max(sizeX, sizeY) * 1.15f; // 15% padding
            _thumbCam.targetTexture = _thumbRT;

            // Render
            _thumbCam.Render();

            // Read pixels into Texture2D
            RenderTexture.active = _thumbRT;
            var thumb = new Texture2D(ThumbSize, ThumbSize, TextureFormat.RGBA32, false);
            thumb.ReadPixels(new Rect(0, 0, ThumbSize, ThumbSize), 0, 0);
            thumb.Apply();
            RenderTexture.active = null;

            // Cleanup
            Object.DestroyImmediate(go);
            CleanupThumbPrefab(cacheKey, customPrefab);
            return thumb;
        }

        /// <summary>Destroy the temporary custom prefab used for thumbnail rendering.</summary>
        private static void CleanupThumbPrefab(string cacheKey, GameObject customPrefab)
        {
            if (customPrefab != null)
            {
                string thumbEntityId = $"_thumb_{cacheKey}";
                NpcPrefabBuilder.InvalidateCache(thumbEntityId);
            }
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
                var camGO = new GameObject("[SpriteSkinThumbCam]");
                camGO.hideFlags = HideFlags.HideAndDontSave;
                _thumbCam = camGO.AddComponent<Camera>();
                _thumbCam.orthographic = true;
                _thumbCam.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
                _thumbCam.clearFlags = CameraClearFlags.SolidColor;
                _thumbCam.cullingMask = ~0;
                _thumbCam.nearClipPlane = 0.1f;
                _thumbCam.farClipPlane = 100f;
                _thumbCam.enabled = false; // manual render only
                Object.DontDestroyOnLoad(camGO);
            }
        }

        private static void DestroyRenderResources()
        {
            if (_thumbRT != null) { _thumbRT.Release(); Object.Destroy(_thumbRT); _thumbRT = null; }
            if (_thumbCam != null) { Object.Destroy(_thumbCam.gameObject); _thumbCam = null; }
        }
    }
}
