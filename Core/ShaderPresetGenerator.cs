using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Generates procedural textures/sprites for ShaderPreset layer effects.
    /// Sprites are 1×1 world units (PPU matches texture size) — use transform scale to size.
    /// </summary>
    public static class ShaderPresetGenerator
    {
        private const int TexSize = 512;
        private static readonly Dictionary<string, Sprite> _cache = new();

        public static void ClearCache()
        {
            foreach (var kvp in _cache)
            {
                if (kvp.Value != null && kvp.Value.texture != null)
                    Object.Destroy(kvp.Value.texture);
            }
            _cache.Clear();
        }

        public static Sprite GetPresetSprite(ShaderPreset preset, float p1, float p2)
        {
            if (preset == ShaderPreset.None)
                return DataHelper.GetShaderQuadSprite();

            string key = $"{preset}_{p1:F2}_{p2:F2}";
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var tex = GenerateTexture(preset, p1, p2);
            if (tex == null)
                return DataHelper.GetShaderQuadSprite();

            var sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), tex.width);
            _cache[key] = sprite;
            return sprite;
        }

        private static Texture2D GenerateTexture(ShaderPreset preset, float p1, float p2)
        {
            switch (preset)
            {
                case ShaderPreset.Scanlines:    return GenScanlines(p1, p2);
                case ShaderPreset.Vignette:     return GenVignette(p1, p2);
                case ShaderPreset.Noise:        return GenNoise(p1, p2);
                case ShaderPreset.Gradient:     return GenGradient(p1, p2);
                case ShaderPreset.Checkerboard: return GenCheckerboard(p1, p2);
                default: return null;
            }
        }

        /// <summary>Horizontal scanlines. P1=line width (pixels), P2=gap width (pixels).</summary>
        private static Texture2D GenScanlines(float lineWidth, float gapWidth)
        {
            int lw = Mathf.Max(1, Mathf.RoundToInt(lineWidth));
            int gw = Mathf.Max(1, Mathf.RoundToInt(gapWidth));
            int period = lw + gw;

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[TexSize * TexSize];
            var opaque = new Color32(255, 255, 255, 255);
            var clear = new Color32(255, 255, 255, 0);

            for (int y = 0; y < TexSize; y++)
            {
                var c = (y % period) < lw ? opaque : clear;
                int row = y * TexSize;
                for (int x = 0; x < TexSize; x++)
                    pixels[row + x] = c;
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Dark-edge vignette. P1=intensity (0-2), P2=softness (0-1).</summary>
        private static Texture2D GenVignette(float intensity, float softness)
        {
            intensity = Mathf.Clamp(intensity, 0f, 2f);
            softness = Mathf.Clamp(softness, 0.01f, 1f);

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[TexSize * TexSize];
            float center = (TexSize - 1) * 0.5f;

            for (int y = 0; y < TexSize; y++)
            {
                float dy = (y - center) / center;
                int row = y * TexSize;
                for (int x = 0; x < TexSize; x++)
                {
                    float dx = (x - center) / center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float inner = 1f - softness;
                    float a = dist <= inner ? 0f
                            : dist >= 1f ? intensity
                            : Mathf.Lerp(0f, intensity, (dist - inner) / softness);
                    a = Mathf.Clamp01(a);
                    pixels[row + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Random static grain. P1=grain size (1-8), P2=density (0-1).</summary>
        private static Texture2D GenNoise(float grainSize, float density)
        {
            int grain = Mathf.Max(1, Mathf.RoundToInt(grainSize));
            density = Mathf.Clamp01(density);
            int size = Mathf.Max(8, TexSize / grain);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = grain > 1 ? FilterMode.Point : FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var rng = new System.Random(42);

            for (int i = 0; i < pixels.Length; i++)
            {
                float v = (float)rng.NextDouble();
                byte a = (byte)(v * density * 255);
                pixels[i] = new Color32(255, 255, 255, a);
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Linear gradient. P1=direction (0=down, 1=up, 2=right, 3=left).</summary>
        private static Texture2D GenGradient(float direction, float unused)
        {
            int dir = Mathf.RoundToInt(direction) % 4;
            if (dir < 0) dir += 4;

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[TexSize * TexSize];
            float invSize = 1f / (TexSize - 1);

            for (int y = 0; y < TexSize; y++)
            {
                int row = y * TexSize;
                for (int x = 0; x < TexSize; x++)
                {
                    float t;
                    switch (dir)
                    {
                        case 0: t = 1f - y * invSize; break;
                        case 1: t = y * invSize; break;
                        case 2: t = x * invSize; break;
                        default: t = 1f - x * invSize; break;
                    }
                    pixels[row + x] = new Color32(255, 255, 255, (byte)(t * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Alternating squares. P1=square size (pixels), P2=intensity (0-1).</summary>
        private static Texture2D GenCheckerboard(float squareSize, float intensity)
        {
            int sz = Mathf.Max(1, Mathf.RoundToInt(squareSize));
            intensity = Mathf.Clamp01(intensity);
            byte iAlpha = (byte)(intensity * 255);

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[TexSize * TexSize];
            var dark = new Color32(255, 255, 255, iAlpha);
            var clear = new Color32(255, 255, 255, 0);

            for (int y = 0; y < TexSize; y++)
            {
                int row = y * TexSize;
                for (int x = 0; x < TexSize; x++)
                {
                    bool isDark = ((x / sz) + (y / sz)) % 2 == 0;
                    pixels[row + x] = isDark ? dark : clear;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
