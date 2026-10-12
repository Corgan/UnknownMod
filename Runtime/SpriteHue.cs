using System.Collections.Generic;
using UnityEngine;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Hue-rotated copies of textures (cached). A SpriteRenderer.color can only multiply, so a true hue shift swaps the texture through a
    /// MaterialPropertyBlock instead; the shifted copy has the same size and layout, so sprite UVs and skinning are unaffected.
    /// The rotation keeps luminance (the usual 3x3 hue matrix), so a small shift nudges colours without washing them out.
    /// </summary>
    internal static class SpriteHue
    {
        private const int MaxCached = 64;
        private const long MaxPixels = 4096L * 4096L;
        private static readonly Dictionary<long, Texture2D> Cache = new Dictionary<long, Texture2D>();
        private static readonly Queue<long> Order = new Queue<long>();

        public static Texture2D Get(Texture source, int degrees)
        {
            if (source == null || degrees == 0) return null;
            long key = ((long)source.GetInstanceID() << 16) ^ (uint)(degrees + 360);
            Texture2D cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            RenderTexture rt = null;
            var previous = RenderTexture.active;
            try
            {
                int w = source.width, h = source.height;
                if (w < 1 || h < 1 || (long)w * h > MaxPixels) { Plugin.Log.LogWarning("[SpriteHue] Texture '" + source.name + "' is too large to hue-shift."); return null; }
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var temp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                temp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                var pixels = temp.GetPixels32();
                Object.Destroy(temp);

                Rotate(pixels, degrees);

                var result = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = source.name + "_hue" + degrees, filterMode = source.filterMode, wrapMode = source.wrapMode };
                result.SetPixels32(pixels);
                result.Apply(true, true);

                Cache[key] = result;
                Order.Enqueue(key);
                while (Order.Count > MaxCached)
                {
                    long old = Order.Dequeue();
                    Texture2D evicted;
                    if (Cache.TryGetValue(old, out evicted)) { Cache.Remove(old); if (evicted != null) Object.Destroy(evicted); }
                }
                return result;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[SpriteHue] Could not hue-shift '" + source.name + "': " + ex.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void Rotate(Color32[] pixels, int degrees)
        {
            float rad = degrees * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            float m00 = .213f + c * .787f - s * .213f, m01 = .715f - c * .715f - s * .715f, m02 = .072f - c * .072f + s * .928f;
            float m10 = .213f - c * .213f + s * .143f, m11 = .715f + c * .285f + s * .140f, m12 = .072f - c * .072f - s * .283f;
            float m20 = .213f - c * .213f - s * .787f, m21 = .715f - c * .715f + s * .715f, m22 = .072f + c * .928f + s * .072f;
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.a == 0) continue;
                float r = p.r, g = p.g, b = p.b;
                pixels[i] = new Color32(Clamp(m00 * r + m01 * g + m02 * b), Clamp(m10 * r + m11 * g + m12 * b), Clamp(m20 * r + m21 * g + m22 * b), p.a);
            }
        }

        private static byte Clamp(float v) { return v <= 0f ? (byte)0 : v >= 255f ? (byte)255 : (byte)(v + .5f); }
    }

    /// <summary>
    /// Keeps one SpriteRenderer hue-shifted. The node's own shift and the rig/part shift (set by the tint behaviour) add together.
    /// The sprite is checked every frame because the game and Animator swap sprites, and the shifted texture must match the sprite's texture.
    /// </summary>
    [DefaultExecutionOrder(50)]
    internal sealed class VisualGraphSpriteHue : MonoBehaviour
    {
        public float Node;
        public float External;

        private SpriteRenderer _renderer;
        private Texture _source, _failed;
        private Texture2D _applied;
        private int _appliedDegrees;
        private MaterialPropertyBlock _block;

        private void LateUpdate() { Refresh(); }

        public void Refresh()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null) return;
            int degrees = Mathf.RoundToInt(Mathf.Repeat(Node + External + 180f, 360f) - 180f);
            var sprite = _renderer.sprite;
            Texture source = sprite != null ? sprite.texture : null;
            if (degrees == 0 || source == null) { Clear(); return; }
            if (_applied != null && source == _source && degrees == _appliedDegrees) return;
            if (source == _failed && degrees == _appliedDegrees) return;

            var shifted = SpriteHue.Get(source, degrees);
            _appliedDegrees = degrees;
            if (shifted == null) { _failed = source; return; }
            _failed = null;
            if (_block == null) _block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetTexture("_MainTex", shifted);
            _renderer.SetPropertyBlock(_block);
            _source = source;
            _applied = shifted;
        }

        private void Clear()
        {
            _failed = null;
            if (_source == null && _applied == null) return;
            if (_renderer != null) _renderer.SetPropertyBlock(null);
            _source = null; _applied = null; _appliedDegrees = 0;
        }

        private void OnDestroy() { Clear(); }
    }
}
