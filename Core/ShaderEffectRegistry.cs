using System.Collections.Generic;
using UnityEngine;

namespace UnknownMod.Core
{
    /// <summary>
    /// Registry of known AllIn1SpriteShader effects with their keywords, properties, and defaults.
    /// Used by editors to provide toggle + slider UI for shader layer effects.
    /// </summary>
    public static class ShaderEffectRegistry
    {
        public struct EffectProp
        {
            public string Name;   // shader property name (e.g. "_HologramStripesAmount")
            public string Label;  // short display label
            public float Min, Max, Default;

            public EffectProp(string name, string label, float min, float max, float def)
            { Name = name; Label = label; Min = min; Max = max; Default = def; }
        }

        public struct Effect
        {
            public string Keyword;     // e.g. "HOLOGRAM_ON"
            public string DisplayName; // e.g. "Hologram"
            public string Category;    // "Visual", "Color", "Anim", "Alpha"
            public EffectProp[] Props;
        }

        /// <summary>Curated list of useful AllIn1SpriteShader effects for overlay/background layers.</summary>
        public static readonly Effect[] Effects = new[]
        {
            // ── Visual ──────────────────────────────────────────
            new Effect { Keyword = "HOLOGRAM_ON", DisplayName = "Hologram", Category = "Visual", Props = new[] {
                new EffectProp("_HologramStripesAmount", "Stripes",  0f, 1f, 0.2f),
                new EffectProp("_HologramUnmodAmount",   "Unmod",    0f, 1f, 0.4f),
                new EffectProp("_HologramStripesSpeed",  "Speed",    0f, 20f, 5f),
                new EffectProp("_HologramMinAlpha",      "MinA",     0f, 1f, 0f),
                new EffectProp("_HologramMaxAlpha",      "MaxA",     0f, 10f, 1f),
            }},
            new Effect { Keyword = "GHOST_ON", DisplayName = "Ghost", Category = "Visual", Props = new[] {
                new EffectProp("_GhostColorBoost",    "Boost",       0f, 5f, 1f),
                new EffectProp("_GhostTransparency",  "Transp",      0f, 1f, 0f),
            }},
            new Effect { Keyword = "FLICKER_ON", DisplayName = "Flicker", Category = "Visual", Props = new[] {
                new EffectProp("_FlickerPercent", "Percent",  0f, 1f, 0.05f),
                new EffectProp("_FlickerFreq",    "Freq",     0f, 5f, 0.2f),
                new EffectProp("_FlickerAlpha",   "Alpha",    0f, 1f, 0f),
            }},
            new Effect { Keyword = "GLITCH_ON", DisplayName = "Glitch", Category = "Visual", Props = new[] {
                new EffectProp("_GlitchAmount", "Amount",  0f, 20f, 3f),
                new EffectProp("_GlitchSize",   "Size",    0.25f, 5f, 1f),
            }},
            new Effect { Keyword = "CHROMABERR_ON", DisplayName = "ChromAberr", Category = "Visual", Props = new[] {
                new EffectProp("_ChromAberrAmount", "Amount", 0f, 1f, 1f),
                new EffectProp("_ChromAberrAlpha",  "Alpha",  0f, 1f, 0.4f),
            }},
            new Effect { Keyword = "BLUR_ON", DisplayName = "Blur", Category = "Visual", Props = new[] {
                new EffectProp("_BlurIntensity", "Intensity", 0f, 100f, 10f),
                new EffectProp("_BlurHD",        "LowRes",    0f, 1f, 0f),
            }},
            new Effect { Keyword = "MOTIONBLUR_ON", DisplayName = "MotionBlur", Category = "Visual", Props = new[] {
                new EffectProp("_MotionBlurAngle", "Angle", -1f, 1f, 0.1f),
                new EffectProp("_MotionBlurDist",  "Dist",  -3f, 3f, 1.25f),
            }},
            new Effect { Keyword = "GLOW_ON", DisplayName = "Glow", Category = "Visual", Props = new[] {
                new EffectProp("_Glow", "Intensity", 0f, 100f, 10f),
            }},
            new Effect { Keyword = "SHADOW_ON", DisplayName = "Shadow", Category = "Visual", Props = new[] {
                new EffectProp("_ShadowX",     "X",     -0.5f, 0.5f, 0.1f),
                new EffectProp("_ShadowY",     "Y",     -0.5f, 0.5f, -0.05f),
                new EffectProp("_ShadowAlpha", "Alpha", 0f, 1f, 0.5f),
            }},

            // ── Color ───────────────────────────────────────────
            new Effect { Keyword = "HSV_ON", DisplayName = "HSV", Category = "Color", Props = new[] {
                new EffectProp("_HsvShift",      "Hue",  0f, 360f, 180f),
                new EffectProp("_HsvSaturation", "Sat",  0f, 2f, 1f),
                new EffectProp("_HsvBright",     "Brt",  0f, 2f, 1f),
            }},
            new Effect { Keyword = "GREYSCALE_ON", DisplayName = "Greyscale", Category = "Color", Props = new[] {
                new EffectProp("_GreyscaleLuminosity", "Lum", -1f, 1f, 0f),
            }},
            new Effect { Keyword = "NEGATIVE_ON", DisplayName = "Negative", Category = "Color", Props = new[] {
                new EffectProp("_NegativeAmount", "Amount", 0f, 1f, 1f),
            }},
            new Effect { Keyword = "POSTERIZE_ON", DisplayName = "Posterize", Category = "Color", Props = new[] {
                new EffectProp("_PosterizeNumColors", "Colors", 0f, 100f, 8f),
                new EffectProp("_PosterizeGamma",     "Gamma",  0.1f, 10f, 0.75f),
            }},
            new Effect { Keyword = "HITEFFECT_ON", DisplayName = "HitFX", Category = "Color", Props = new[] {
                new EffectProp("_HitEffectGlow",  "Glow",  1f, 100f, 5f),
                new EffectProp("_HitEffectBlend", "Blend", 0f, 1f, 1f),
            }},

            // ── Animation ───────────────────────────────────────
            new Effect { Keyword = "WAVEUV_ON", DisplayName = "Wave", Category = "Anim", Props = new[] {
                new EffectProp("_WaveAmount",   "Amount",   0f, 25f, 7f),
                new EffectProp("_WaveSpeed",    "Speed",    0f, 25f, 10f),
                new EffectProp("_WaveStrength", "Strength", 0f, 25f, 7.5f),
                new EffectProp("_WaveX",        "X",        0f, 1f, 0f),
                new EffectProp("_WaveY",        "Y",        0f, 1f, 0.5f),
            }},
            new Effect { Keyword = "SHAKEUV_ON", DisplayName = "Shake", Category = "Anim", Props = new[] {
                new EffectProp("_ShakeUvSpeed", "Speed", 0f, 20f, 2.5f),
                new EffectProp("_ShakeUvX",     "X",     0f, 5f, 1.5f),
                new EffectProp("_ShakeUvY",     "Y",     0f, 5f, 1f),
            }},
            new Effect { Keyword = "DOODLE_ON", DisplayName = "Doodle", Category = "Anim", Props = new[] {
                new EffectProp("_HandDrawnAmount", "Amount", 2f, 20f, 10f),
                new EffectProp("_HandDrawnSpeed",  "Speed",  1f, 15f, 5f),
            }},
            new Effect { Keyword = "WIND_ON", DisplayName = "Wind", Category = "Anim", Props = new[] {
                new EffectProp("_GrassSpeed", "Speed", 0f, 50f, 2f),
                new EffectProp("_GrassWind",  "Bend",  0f, 50f, 20f),
            }},

            // ── Alpha / Clip ────────────────────────────────────
            new Effect { Keyword = "FADE_ON", DisplayName = "Fade", Category = "Alpha", Props = new[] {
                new EffectProp("_FadeAmount",          "Amount",    -0.1f, 1f, -0.1f),
                new EffectProp("_FadeBurnWidth",       "BurnW",     0f, 1f, 0.025f),
                new EffectProp("_FadeBurnTransition",  "BurnSmth",  0.01f, 0.5f, 0.075f),
                new EffectProp("_FadeBurnGlow",        "BurnGlow",  1f, 50f, 2f),
            }},
            new Effect { Keyword = "ALPHACUTOFF_ON", DisplayName = "AlphaClip", Category = "Alpha", Props = new[] {
                new EffectProp("_AlphaCutoffValue", "Cutoff", 0f, 1f, 0.25f),
            }},
        };

        // Keyword → index lookup for fast access
        private static Dictionary<string, int> _keywordIndex;
        public static Dictionary<string, int> KeywordIndex
        {
            get
            {
                if (_keywordIndex == null)
                {
                    _keywordIndex = new Dictionary<string, int>();
                    for (int i = 0; i < Effects.Length; i++)
                        _keywordIndex[Effects[i].Keyword] = i;
                }
                return _keywordIndex;
            }
        }

        /// <summary>Find effect by keyword. Returns index or -1.</summary>
        public static int FindEffect(string keyword)
        {
            return KeywordIndex.TryGetValue(keyword, out int idx) ? idx : -1;
        }

        /// <summary>Apply shader keywords and float properties to a material.</summary>
        public static void ApplyToMaterial(Material mat, List<string> keywords, Dictionary<string, float> floats)
        {
            if (mat == null) return;
            if (keywords != null)
                foreach (var kw in keywords)
                    mat.EnableKeyword(kw);
            if (floats != null)
                foreach (var kvp in floats)
                    mat.SetFloat(kvp.Key, kvp.Value);
        }
    }
}
