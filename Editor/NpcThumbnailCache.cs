using System;
using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Renders and caches small thumbnail textures of NPC models using the
    /// actual GameObjectAnimated prefab in its idle pose. Falls back to
    /// NPC portrait/sprite when no animated model is available.
    /// </summary>
    public static class NpcThumbnailCache
    {
        private static readonly Dictionary<string, Texture2D> _cache = new();
        private static Camera _thumbCam;
        private static RenderTexture _thumbRT;
        private const int ThumbSize = 128;
        private static readonly Vector3 ThumbOrigin = new(-9000f, 0f, 0f);

        /// <summary>Get or render a thumbnail for an NPC ID.</summary>
        public static Texture2D GetThumbnail(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            if (_cache.TryGetValue(npcId, out var cached)) return cached; // null = already tried

            var tex = RenderThumbnail(npcId);
            _cache[npcId] = tex;
            return tex;
        }

        /// <summary>Invalidate a specific thumbnail (e.g. after edits).</summary>
        public static void Invalidate(string npcId)
        {
            if (_cache.TryGetValue(npcId, out var tex))
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                _cache.Remove(npcId);
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

        private static Texture2D RenderThumbnail(string npcId)
        {
            try
            {
                var npcData = DataHelper.GetExistingNPC(npcId);
                if (npcData == null) return null;

                // Try animated model prefab first
                var model = npcData.GameObjectAnimated;
                if (model != null)
                {
                    var go = UnityEngine.Object.Instantiate(model, ThumbOrigin, Quaternion.identity);
                    go.SetActive(true);
                    go.name = $"[NpcThumb] {npcId}";

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

                    // Compute bounds from all visible SpriteRenderers
                    var renderers = go.GetComponentsInChildren<SpriteRenderer>(false);
                    if (renderers.Length > 0)
                    {
                        Bounds bounds = renderers[0].bounds;
                        for (int i = 1; i < renderers.Length; i++)
                        {
                            if (renderers[i].sprite != null)
                                bounds.Encapsulate(renderers[i].bounds);
                        }

                        EnsureRenderResources();
                        _thumbCam.transform.position = new Vector3(
                            bounds.center.x, bounds.center.y, ThumbOrigin.z - 10f);
                        float sizeX = bounds.extents.x;
                        float sizeY = bounds.extents.y;
                        _thumbCam.orthographicSize = Mathf.Max(sizeX, sizeY) * 1.15f;
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

                    UnityEngine.Object.DestroyImmediate(go);
                }

                // Fallback: portrait/sprite as texture
                Sprite spr = npcData.SpritePortrait ?? npcData.Sprite;
                if (spr != null)
                    return ViewportPreview.GetSpriteTexture(spr);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"[NpcThumb] Render failed for '{npcId}': {ex.Message}");
            }

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
                var camGO = new GameObject("[NpcThumbCam]");
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
