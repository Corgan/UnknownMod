using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Runtime
{
    /// <summary>Pure maths for the rig effects, kept free of scene state so it is cheap to reason about.</summary>
    internal static class RigEffectMath
    {
        public static bool IsColour(RigEffectType t) => t == RigEffectType.Pulse || t == RigEffectType.Rainbow || t == RigEffectType.Flicker || t == RigEffectType.FadePulse;
        public static bool IsTransform(RigEffectType t) => t == RigEffectType.Float || t == RigEffectType.Shake || t == RigEffectType.Breathe || t == RigEffectType.Sway;
        public static bool IsParticle(RigEffectType t) => !IsColour(t) && !IsTransform(t);

        public static Color ParseColour(string hex, Color fallback)
            => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

        /// <summary>Multiplier applied on top of the rig tint this frame. Alpha is returned separately so it can multiply the opacity.</summary>
        public static Color ColourFactor(IList<RigEffectDef> effects, float time, out float alpha)
        {
            Color factor = Color.white;
            alpha = 1f;
            if (effects == null) return factor;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null || !IsColour(e.Type)) continue;
                float speed = e.Speed <= 0f ? 1f : e.Speed;
                float k = Mathf.Clamp01(e.Intensity);
                switch (e.Type)
                {
                    case RigEffectType.Pulse:
                    {
                        float wave = 0.5f + 0.5f * Mathf.Sin(time * speed * Mathf.PI * 2f);
                        Color target = ParseColour(e.ColorHex, new Color(1f, 0.85f, 0.4f));
                        Color c = Color.Lerp(Color.white, target, wave * k);
                        factor = new Color(factor.r * c.r, factor.g * c.g, factor.b * c.b, 1f);
                        break;
                    }
                    case RigEffectType.Rainbow:
                    {
                        Color hue = Color.HSVToRGB(Mathf.Repeat(time * speed * 0.2f, 1f), 0.75f, 1f);
                        Color c = Color.Lerp(Color.white, hue, k);
                        factor = new Color(factor.r * c.r, factor.g * c.g, factor.b * c.b, 1f);
                        break;
                    }
                    case RigEffectType.Flicker:
                        alpha *= 1f - k * Mathf.PerlinNoise(time * speed * 8f, 0.37f);
                        break;
                    case RigEffectType.FadePulse:
                        alpha *= 1f - k * 0.5f * (1f + Mathf.Sin(time * speed * Mathf.PI * 2f));
                        break;
                }
            }
            return factor;
        }

        /// <summary>Combined transform offsets for this frame. unit is the rig height in the root's parent space.</summary>
        public static void TransformOffsets(IList<RigEffectDef> effects, float time, float unit, out Vector3 position, out Vector3 scale, out float rotationZ)
        {
            position = Vector3.zero;
            scale = Vector3.one;
            rotationZ = 0f;
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null || !IsTransform(e.Type)) continue;
                float speed = e.Speed <= 0f ? 1f : e.Speed;
                float k = Mathf.Max(0f, e.Intensity);
                switch (e.Type)
                {
                    case RigEffectType.Float:
                        position.y += Mathf.Sin(time * speed * Mathf.PI * 2f * 0.5f) * 0.03f * unit * k;
                        break;
                    case RigEffectType.Shake:
                        position.x += (Mathf.PerlinNoise(time * speed * 25f, 0.1f) - 0.5f) * 0.02f * unit * k;
                        position.y += (Mathf.PerlinNoise(0.7f, time * speed * 25f) - 0.5f) * 0.02f * unit * k;
                        break;
                    case RigEffectType.Breathe:
                    {
                        float s = 1f + Mathf.Sin(time * speed * Mathf.PI * 2f * 0.4f) * 0.03f * k;
                        scale = new Vector3(scale.x * s, scale.y * s, scale.z);
                        break;
                    }
                    case RigEffectType.Sway:
                        rotationZ += Mathf.Sin(time * speed * Mathf.PI * 2f * 0.3f) * 3f * k;
                        break;
                }
            }
        }
    }

    /// <summary>Builds the particle auras. They are plain ParticleSystems parented under the rig so they follow it and are destroyed with it.</summary>
    internal static class RigParticles
    {
        private static Texture2D _soft;
        private static Material _material;

        private static Texture2D SoftDot()
        {
            if (_soft != null) return _soft;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "UnknownMod_ParticleDot", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            return _soft = tex;
        }

        private static Material SharedMaterial()
        {
            if (_material != null) return _material;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            _material = new Material(shader) { name = "UnknownMod_Particles", hideFlags = HideFlags.HideAndDontSave, mainTexture = SoftDot() };
            return _material;
        }

        public static GameObject Create(Transform root, RigEffectDef e, Bounds worldBounds, bool hasBounds, int sortingLayerId, int sortingOrder)
        {
            var material = SharedMaterial();
            if (material == null) return null;

            var go = new GameObject("UnknownMod_RigEffect_" + e.Type);
            go.transform.SetParent(root, false);
            Vector3 lossy = root.lossyScale;
            Vector3 inv = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)), 1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)), 1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
            Vector3 size = hasBounds ? worldBounds.size : Vector3.one;
            if (size.y < 0.01f) size = Vector3.one;
            go.transform.position = hasBounds ? worldBounds.center : root.position;
            float h = size.y * inv.y;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float intensity = Mathf.Max(0.05f, e.Intensity);
            float speed = e.Speed <= 0f ? 1f : e.Speed;

            float rate, life, dot, rise, drift; Color colour; bool grow = false; float baseAlpha = 1f;
            switch (e.Type)
            {
                case RigEffectType.Embers: rate = 14; life = 1.8f; dot = 0.03f; rise = 0.35f; drift = 0.05f; colour = ParseOrDefault(e, "#FF8A30"); break;
                case RigEffectType.Spores: rate = 10; life = 3f; dot = 0.03f; rise = 0.08f; drift = 0.08f; colour = ParseOrDefault(e, "#B8FF9A"); break;
                case RigEffectType.Smoke: rate = 6; life = 2.5f; dot = 0.12f; rise = 0.15f; drift = 0.05f; colour = ParseOrDefault(e, "#888888"); grow = true; baseAlpha = 0.35f; break;
                case RigEffectType.Snow: rate = 14; life = 3f; dot = 0.025f; rise = -0.22f; drift = 0.05f; colour = ParseOrDefault(e, "#FFFFFF"); break;
                case RigEffectType.Bubbles: rate = 6; life = 2.5f; dot = 0.05f; rise = 0.28f; drift = 0.04f; colour = ParseOrDefault(e, "#A0D8FF"); baseAlpha = 0.6f; break;
                default: rate = 12; life = 1.2f; dot = 0.035f; rise = 0f; drift = 0.02f; colour = ParseOrDefault(e, "#FFF2A0"); break;
            }

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = life;
            main.startSize = dot * h;
            main.startSpeed = 0f;
            main.maxParticles = 200;
            main.simulationSpeed = speed;
            main.startColor = new Color(colour.r, colour.g, colour.b, colour.a * baseAlpha);

            var emission = ps.emission;
            emission.rateOverTime = rate * intensity;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(size.x * inv.x * 0.6f, size.y * inv.y * 0.8f, 0.01f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-drift * h, drift * h);
            velocity.y = new ParticleSystem.MinMaxCurve(rise * h * 0.8f, rise * h * 1.2f);
            velocity.z = 0f;

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = fade;

            if (grow)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.6f)));
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingLayerID = sortingLayerId;
            renderer.sortingOrder = sortingOrder;

            ps.Play(true);
            return go;
        }

        private static Color ParseOrDefault(RigEffectDef e, string fallbackHex)
            => RigEffectMath.ParseColour(e.ColorHex, RigEffectMath.ParseColour(fallbackHex, Color.white));
    }
}
