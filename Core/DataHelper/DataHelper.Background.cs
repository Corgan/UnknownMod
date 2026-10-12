using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BattleMatch;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class DataHelper
    {
        // ═══════════════════════════════════════════════════════════════
        //  CUSTOM BACKGROUND PREFAB BUILDER
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Registry of custom background prefabs built from BackgroundDefs.
        /// Keyed by normalized BackgroundId; MatchBackground.DoBackground is intercepted (Patches.Background) to instantiate them, since the game now resolves vanilla backgrounds by name through Globals.GetBackgroundPrefab.
        /// </summary>
        internal static readonly Dictionary<string, GameObject> CustomBackgroundPrefabs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps CombatId strings to custom background IDs.
        /// Used by the DoBackground patch to look up custom backgrounds.
        /// String-keyed so lookups survive SO recreation during hot-reload.
        /// </summary>
        internal static readonly Dictionary<string, string> CombatCustomBackgrounds = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Material> OwnedBackgroundMaterials = new();
        private sealed class GameObjectIdentityComparer : IEqualityComparer<GameObject>
        {
            public bool Equals(GameObject left, GameObject right) => ReferenceEquals(left, right);
            public int GetHashCode(GameObject value) => RuntimeHelpers.GetHashCode(value);
        }

        private static readonly HashSet<GameObject> LiveCustomBackgrounds =
            new(new GameObjectIdentityComparer());

        internal static string NormalizeBackgroundId(string id)
        {
            return id?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Build a background prefab GameObject from a BackgroundDef.
        /// The prefab structure mirrors the game's: root GO with child SpriteRenderers.
        /// The root is kept inactive and DontDestroyOnLoad'd as a template.
        /// </summary>
        public static GameObject BuildBackgroundPrefab(BackgroundDef def)
        {
            string backgroundId = NormalizeBackgroundId(def?.BackgroundId);
            if (string.IsNullOrEmpty(backgroundId)) return null;

            var root = new GameObject(backgroundId);
            root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.HideAndDontSave;

            foreach (var layer in def.Layers)
            {
                if (!layer.Visible) continue;

                var go = new GameObject(layer.Name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
                go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
                if (Mathf.Abs(layer.Rotation) > 0.001f)
                    go.transform.localEulerAngles = new Vector3(0, 0, layer.Rotation);

                switch (layer.Type)
                {
                    case VisualLayerType.Sprite:
                    {
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sortingOrder = layer.SortingOrder;
                        try { sr.sortingLayerName = layer.SortingLayer; } catch { }
                        sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        sr.flipX = layer.FlipX;
                        sr.flipY = layer.FlipY;
                        if (!string.IsNullOrEmpty(layer.SpriteName))
                        {
                            var sprite = FindGameSprite(layer.SpriteName);
                            if (sprite != null) sr.sprite = sprite;
                        }
                        break;
                    }
                    case VisualLayerType.Light:
                    {
                        var light = go.AddComponent<Light2D>();
                        light.lightType = (Light2D.LightType)layer.LightType;
                        light.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        light.intensity = layer.Intensity;
                        light.falloffIntensity = layer.FalloffIntensity;
                        light.lightOrder = layer.LightOrder;
                        light.blendStyleIndex = layer.BlendStyleIndex;
                        light.shadowsEnabled = layer.ShadowsEnabled;
                        light.shadowIntensity = layer.ShadowIntensity;
                        if (layer.LightType == 3)
                        {
                            light.pointLightInnerAngle = layer.PointLightInnerAngle;
                            light.pointLightOuterAngle = layer.PointLightOuterAngle;
                            light.pointLightInnerRadius = layer.PointLightInnerRadius;
                            light.pointLightOuterRadius = layer.PointLightOuterRadius;
                        }
                        if (layer.LightType == 0 || layer.LightType == 1)
                            light.shapeLightFalloffSize = layer.ShapeLightFalloffSize;
                        break;
                    }
                    case VisualLayerType.ParticleSystem:
                    {
                        var ps = go.AddComponent<ParticleSystem>();
                        var main = ps.main;
                        main.duration = layer.Duration;
                        main.loop = layer.Loop;
                        main.prewarm = layer.Prewarm;
                        main.startLifetime = layer.StartLifetime;
                        main.startSpeed = layer.StartSpeed;
                        main.startSize = layer.StartSize;
                        main.maxParticles = layer.MaxParticles;
                        main.simulationSpeed = layer.SimulationSpeed;
                        main.playOnAwake = layer.PlayOnAwake;
                        main.gravityModifier = layer.GravityModifier;
                        main.startColor = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);

                        var emission = ps.emission;
                        emission.rateOverTime = layer.EmissionRate;

                        var psr = go.GetComponent<ParticleSystemRenderer>();
                        if (psr != null)
                        {
                            psr.sortingOrder = layer.SortingOrder;
                            if (psr.sharedMaterial == null)
                            {
                                var defaultMat = new Material(Shader.Find("Particles/Standard Unlit"));
                                defaultMat.SetFloat("_Mode", 1);
                                psr.sharedMaterial = defaultMat;
                                OwnedBackgroundMaterials.Add(defaultMat);
                            }
                        }
                        break;
                    }
                    case VisualLayerType.SpriteMask:
                    {
                        var mask = go.AddComponent<SpriteMask>();
                        mask.alphaCutoff = layer.AlphaCutoff;
                        mask.isCustomRangeActive = layer.CustomRange;
                        if (layer.CustomRange)
                        {
                            mask.frontSortingOrder = layer.FrontSortingOrder;
                            mask.backSortingOrder = layer.BackSortingOrder;
                        }
                        mask.sortingOrder = layer.SortingOrder;
                        if (!string.IsNullOrEmpty(layer.SpriteName))
                        {
                            var sprite = FindGameSprite(layer.SpriteName);
                            if (sprite != null) mask.sprite = sprite;
                        }
                        break;
                    }
                    case VisualLayerType.Container:
                        // Empty transform container
                        break;

                    case VisualLayerType.Shader:
                    {
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = ShaderPresetGenerator.GetPresetSprite(layer.Preset, layer.PresetParam1, layer.PresetParam2);
                        sr.sortingOrder = layer.SortingOrder;
                        try { sr.sortingLayerName = layer.SortingLayer; } catch { }
                        sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        sr.maskInteraction = (SpriteMaskInteraction)layer.MaskInteraction;
                        if (!string.IsNullOrEmpty(layer.ShaderName))
                        {
                            var shader = Shader.Find(layer.ShaderName) ?? Resources.Load<Shader>(layer.ShaderName);
                            if (shader != null)
                            {
                                sr.material = new Material(shader);
                                OwnedBackgroundMaterials.Add(sr.material);
                            }
                        }
                        ShaderEffectRegistry.ApplyToMaterial(sr.material, layer.ShaderKeywords, layer.ShaderFloats);
                        break;
                    }

                    case VisualLayerType.PrefabEffect:
                    {
                        if (!string.IsNullOrEmpty(layer.EffectName))
                        {
                            var prefab = GameResources.GetEffectPrefab(layer.EffectName);
                            if (prefab != null)
                            {
                                var clone = UnityEngine.Object.Instantiate(prefab, go.transform);
                                clone.name = layer.EffectName;
                            }
                        }
                        break;
                    }
                }
            }

            return root;
        }

        /// <summary>A 1×1 unit white sprite used as the base for Shader layers.</summary>
        private static Sprite _shaderQuadSprite;
        public static Sprite GetShaderQuadSprite()
        {
            if (_shaderQuadSprite != null) return _shaderQuadSprite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    tex.SetPixel(x, y, Color.white);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            _shaderQuadSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _shaderQuadSprite;
        }

        /// <summary>Register a custom background prefab (built from BackgroundDef).</summary>
        public static void RegisterCustomBackground(BackgroundDef def)
        {
            string backgroundId = NormalizeBackgroundId(def?.BackgroundId);
            if (string.IsNullOrEmpty(backgroundId)) return;

            // Destroy previous if re-registering (hot-reload)
            if (CustomBackgroundPrefabs.TryGetValue(backgroundId, out var old))
            {
                if (old != null) UnityEngine.Object.Destroy(old);
            }

            var prefab = BuildBackgroundPrefab(def);
            if (prefab != null)
            {
                CustomBackgroundPrefabs[backgroundId] = prefab;
            }
        }

        internal static void RegisterLiveCustomBackground(GameObject instance)
        {
            if (instance != null) LiveCustomBackgrounds.Add(instance);
        }

        internal static void DestroyLiveCustomBackground(GameObject instance)
        {
            LiveCustomBackgrounds.Remove(instance);
            if (instance == null) return;
            instance.SetActive(false);
            instance.transform.SetParent(null, false);
            UnityEngine.Object.DestroyImmediate(instance);
        }

        public static void ClearCustomBackgrounds()
        {
            foreach (var instance in new List<GameObject>(LiveCustomBackgrounds))
                DestroyLiveCustomBackground(instance);
            LiveCustomBackgrounds.Clear();

            foreach (var prefab in new List<GameObject>(CustomBackgroundPrefabs.Values))
            {
                if (prefab == null) continue;
                prefab.SetActive(false);
                prefab.transform.SetParent(null, false);
                UnityEngine.Object.DestroyImmediate(prefab);
            }
            ShaderPresetGenerator.ClearCache();
            foreach (var material in OwnedBackgroundMaterials)
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (_shaderQuadSprite != null)
            {
                var texture = _shaderQuadSprite.texture;
                UnityEngine.Object.DestroyImmediate(_shaderQuadSprite);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                _shaderQuadSprite = null;
            }
            OwnedBackgroundMaterials.Clear();
            CustomBackgroundPrefabs.Clear();
            CombatCustomBackgrounds.Clear();
        }

        /// <summary>Find a game sprite by name — checks mod image sprites first, then loaded assets.</summary>
        internal static Sprite FindGameSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Check mod image sprites (exact match)
            if (ModRegistry.ModImageSprites.TryGetValue(name, out var exact) && exact != null)
                return exact;

            // Check mod image sprites (prefixed name: "<modId>_<name>")
            string suffix = "_" + name;
            foreach (var kvp in ModRegistry.ModImageSprites)
            {
                if (kvp.Value != null && kvp.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            // Fall through to all loaded assets (base-game sprites)
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in all)
            {
                if (string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }
    }
}
