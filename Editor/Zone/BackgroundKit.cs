using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Quick-build tools for combat backgrounds: image to full-screen layer, parallax stacks from sliced sprites,
    /// one-click mood overlays, and a baked recolor of every sprite layer.
    /// </summary>
    public partial class BackgroundEditor
    {
        private const float ScreenW = 19.2f, ScreenH = 10.8f;

        private bool _secKit;
        private string _kitImage = "";
        private string _kitSeries = "";
        private float _kitHue;
        private float _kitSat = 1f;
        private float _kitVal = 1f;
        private string _kitStatus = "";

        private static int TopOrder(BackgroundDef def) => def.Layers.Count == 0 ? -1400 : def.Layers.Max(l => l.SortingOrder);
        private static int BottomOrder(BackgroundDef def) => def.Layers.Count == 0 ? -1400 : def.Layers.Min(l => l.SortingOrder);

        private void KitAdded(BackgroundDef def, string status)
        {
            _selectedLayerIdx = def.Layers.Count - 1;
            _expandedLayerIdx = _selectedLayerIdx;
            _layerRenameBuffer = def.Layers[_selectedLayerIdx].Name;
            _viewportDirty = true;
            _kitStatus = status;
            GUI.changed = true;
            MarkDirty();
        }

        private static float CoverScale(Sprite s)
        {
            var b = s.bounds.size;
            if (b.x <= 0.0001f || b.y <= 0.0001f) return 1f;
            return Mathf.Max(ScreenW / b.x, ScreenH / b.y);
        }

        private void DrawKitSection(BackgroundDef def)
        {
            GUILayout.Label("<color=#888>Fast ways to build or restyle a background. Everything lands as normal layers you can keep editing.</color>", EditorStyles.RichLabel);

            // ── Image to layer ───────────────────────────────────
            GUILayout.Label("<b>Image \u2192 full-screen layer</b>", EditorStyles.RichLabel);
            _kitImage = EditorFields.IdDropdown("Sprite", _kitImage, EditorFields.CachedIds("kit_sprites", () => MapEditor.GetAllSpriteNames().ToList()), "bgkit_img");
            if (GUILayout.Button("Add as backdrop (behind everything)", EditorStyles.MiniButton))
            {
                var spr = string.IsNullOrEmpty(_kitImage) ? null : MapEditor.FindGameSprite(_kitImage);
                if (spr == null) _kitStatus = "<color=#f96>Pick a sprite first.</color>";
                else
                {
                    float k = CoverScale(spr);
                    def.Layers.Insert(0, new BackgroundLayerDef { Name = _kitImage, SpriteName = _kitImage, SortingOrder = BottomOrder(def) - 50, ScaleX = k, ScaleY = k });
                    _selectedLayerIdx = 0; _expandedLayerIdx = 0; _layerRenameBuffer = _kitImage;
                    _viewportDirty = true; _kitStatus = $"<color=#8f8>Added '{_kitImage}' scaled to cover the screen.</color>";
                    GUI.changed = true; MarkDirty();
                }
            }

            // ── Parallax stack ───────────────────────────────────
            GUILayout.Space(4);
            GUILayout.Label("<b>Sprite series \u2192 stacked layers</b>  <color=#888>name_0 is the back, higher numbers in front (use Assets \u2192 Sprites grid slice)</color>", EditorStyles.RichLabel);
            _kitSeries = EditorFields.TextField("Series prefix", _kitSeries);
            if (GUILayout.Button("Build stack", EditorStyles.MiniButton))
            {
                var found = ModRegistry.ModImageSprites.Keys
                    .Select(k => (k, idx: SeriesIndex(k, _kitSeries))).Where(t => t.idx >= 0).OrderBy(t => t.idx).ToList();
                if (string.IsNullOrWhiteSpace(_kitSeries) || found.Count == 0)
                    _kitStatus = $"<color=#f96>No mod sprites named '{_kitSeries}_0', '{_kitSeries}_1', \u2026 found.</color>";
                else
                {
                    int order = TopOrder(def);
                    foreach (var (name, _) in found)
                    {
                        var spr = ModRegistry.ModImageSprites[name];
                        float k = spr != null ? CoverScale(spr) : 1f;
                        order += 50;
                        def.Layers.Add(new BackgroundLayerDef { Name = name, SpriteName = name, SortingOrder = order, ScaleX = k, ScaleY = k });
                    }
                    KitAdded(def, $"<color=#8f8>Added {found.Count} layer(s).</color>");
                }
            }

            // ── Mood overlays ────────────────────────────────────
            GUILayout.Space(6);
            GUILayout.Label("<b>Mood overlays</b>  <color=#888>added on top of the existing layers</color>", EditorStyles.RichLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Vignette", EditorStyles.MiniButton)) { AddShaderOverlay(def, "vignette", ShaderPreset.Vignette, Color.black, 0.8f); }
            if (GUILayout.Button("Darken", EditorStyles.MiniButton)) { AddShaderOverlay(def, "darken", ShaderPreset.Gradient, Color.black, 0.45f); }
            if (GUILayout.Button("Scanlines", EditorStyles.MiniButton)) { AddShaderOverlay(def, "scanlines", ShaderPreset.Scanlines, Color.black, 0.25f); }
            if (GUILayout.Button("Grain", EditorStyles.MiniButton)) { AddShaderOverlay(def, "grain", ShaderPreset.Noise, Color.white, 0.2f); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Warm light", EditorStyles.MiniButton)) AddGlobalLight(def, "warm_light", new Color(1f, 0.78f, 0.5f), 0.6f);
            if (GUILayout.Button("Cold light", EditorStyles.MiniButton)) AddGlobalLight(def, "cold_light", new Color(0.55f, 0.7f, 1f), 0.6f);
            if (GUILayout.Button("Blood light", EditorStyles.MiniButton)) AddGlobalLight(def, "blood_light", new Color(1f, 0.25f, 0.2f), 0.5f);
            if (GUILayout.Button("Toxic light", EditorStyles.MiniButton)) AddGlobalLight(def, "toxic_light", new Color(0.5f, 1f, 0.35f), 0.5f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Drifting dust", EditorStyles.MiniButton)) AddParticles(def, "dust", 3f, 0.5f, 0.07f, 12f, new Color(1f, 0.95f, 0.8f, 0.5f));
            if (GUILayout.Button("Embers", EditorStyles.MiniButton)) AddParticles(def, "embers", 2.5f, 1.2f, 0.09f, 8f, new Color(1f, 0.55f, 0.15f, 0.9f), -0.05f);
            if (GUILayout.Button("Snow", EditorStyles.MiniButton)) AddParticles(def, "snow", 6f, 1.5f, 0.1f, 20f, new Color(1f, 1f, 1f, 0.8f), 0.15f);
            GUILayout.EndHorizontal();

            // ── Bake recolor ─────────────────────────────────────
            GUILayout.Space(6);
            GUILayout.Label("<b>Recolor sprite layers</b>  <color=#888>bakes new sprites into the mod and repoints the layers</color>", EditorStyles.RichLabel);
            _kitHue = EditorFields.FloatField("Hue shift", _kitHue, -180f, 180f);
            _kitSat = EditorFields.FloatField("Saturation", _kitSat, 0f, 2f);
            _kitVal = EditorFields.FloatField("Brightness", _kitVal, 0f, 2f);
            var proj = Tabs.ModManagerPanel.ActiveProject;
            if (GUILayout.Button("Bake recolor onto all sprite layers", EditorStyles.MiniButton) && proj != null)
                BakeRecolor(proj, def);

            if (!string.IsNullOrEmpty(_kitStatus)) GUILayout.Label(_kitStatus, EditorStyles.RichLabel);
        }

        private static int SeriesIndex(string name, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return -1;
            int at = name.LastIndexOf(prefix + "_", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            return int.TryParse(name.Substring(at + prefix.Length + 1), out int n) ? n : -1;
        }

        private void AddShaderOverlay(BackgroundDef def, string name, ShaderPreset preset, Color c, float alpha)
        {
            var layer = new BackgroundLayerDef
            {
                Name = UniqueLayerName(def, name),
                Type = VisualLayerType.Shader,
                SortingOrder = TopOrder(def) + 50,
                ScaleX = ScreenW, ScaleY = ScreenH,
                ColorR = c.r, ColorG = c.g, ColorB = c.b, ColorA = alpha,
                Preset = preset,
            };
            ApplyBgPresetDefaults(layer);
            def.Layers.Add(layer);
            KitAdded(def, $"<color=#8f8>Added '{layer.Name}'.</color>");
        }

        private void AddGlobalLight(BackgroundDef def, string name, Color c, float intensity)
        {
            def.Layers.Add(new BackgroundLayerDef
            {
                Name = UniqueLayerName(def, name),
                Type = VisualLayerType.Light,
                LightType = 4,
                Intensity = intensity,
                ColorR = c.r, ColorG = c.g, ColorB = c.b, ColorA = 1f,
                SortingOrder = TopOrder(def) + 50,
            });
            KitAdded(def, $"<color=#8f8>Added a global light. Lights only show in game if the scene uses 2D lighting.</color>");
        }

        private void AddParticles(BackgroundDef def, string name, float lifetime, float speed, float size, float rate, Color c, float gravity = 0f)
        {
            def.Layers.Add(new BackgroundLayerDef
            {
                Name = UniqueLayerName(def, name),
                Type = VisualLayerType.ParticleSystem,
                StartLifetime = lifetime, StartSpeed = speed, StartSize = size, EmissionRate = rate,
                GravityModifier = gravity, Prewarm = true, MaxParticles = 300,
                ColorR = c.r, ColorG = c.g, ColorB = c.b, ColorA = c.a,
                SortingOrder = TopOrder(def) + 50,
                PosY = gravity < 0f ? -5f : (gravity > 0f ? 5f : 0f),
            });
            KitAdded(def, $"<color=#8f8>Added '{name}' particles.</color>");
        }

        private static string UniqueLayerName(BackgroundDef def, string baseName)
        {
            string n = baseName;
            for (int i = 2; def.Layers.Any(l => l.Name == n); i++) n = baseName + "_" + i;
            return n;
        }

        private void BakeRecolor(ModProject proj, BackgroundDef def)
        {
            var p = new RecolorParams { Hue = _kitHue, Saturation = _kitSat, Value = _kitVal };
            if (p.IsNeutral) { _kitStatus = "<color=#f96>Set a hue, saturation or brightness change first.</color>"; return; }

            string root = ModProjectLoader.ModFolder(proj.ModId);
            var baked = new Dictionary<string, string>();
            int layers = 0, failed = 0;
            string firstError = null;
            foreach (var layer in def.Layers)
            {
                if (layer.Type != VisualLayerType.Sprite || string.IsNullOrEmpty(layer.SpriteName)) continue;
                if (!baked.TryGetValue(layer.SpriteName, out var outName))
                {
                    var src = MapEditor.FindGameSprite(layer.SpriteName);
                    string baseName = SpriteRecolor.Sanitize(def.BackgroundId + "_" + layer.SpriteName);
                    string err = null;
                    outName = src == null ? null : SpriteRecolor.Export(proj.ModId, root, src, baseName, p, out err);
                    if (outName == null && firstError == null) firstError = src == null ? $"'{layer.SpriteName}' not found" : err;
                    baked[layer.SpriteName] = outName;
                }
                if (outName != null) { layer.SpriteName = outName; layers++; }
                else failed++;
            }
            if (layers > 0)
            {
                MapEditor.InvalidateSpriteNameCache();
                EditorFields.ResetIdCache();
                _viewportDirty = true;
                GUI.changed = true;
                MarkDirty();
            }
            _kitStatus = layers == 0 && failed == 0
                ? "<color=#f96>No sprite layers to recolor.</color>"
                : $"<color=#{(failed == 0 ? "8f8" : "fc6")}>Recolored {layers} layer(s) into {baked.Values.Count(v => v != null)} new sprite(s){(failed > 0 ? $"; {failed} failed ({firstError})" : "")}.</color>";
        }
    }
}
