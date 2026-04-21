using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    public partial class BackgroundEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  DRAW VIEWPORT (called by ZoneTabEditor for the background subtab)
        // ═══════════════════════════════════════════════════════════════

        public void DrawViewport(Rect vp)
        {
            var proj = Tabs.ModManagerPanel.ActiveProject;
            BackgroundDef def = null;
            if (proj != null && !string.IsNullOrEmpty(_selectedBgId))
            {
                if (!proj.Backgrounds.TryGetValue(_selectedBgId, out def))
                    proj.BackgroundPatches?.TryGetValue(_selectedBgId, out def);
            }

            if (def == null)
            {
                DrawVpEmpty(vp, string.IsNullOrEmpty(_selectedBgId)
                    ? "Select a background to preview"
                    : "Background not found");
                return;
            }

            // Rebuild if background changed or dirty
            if (_loadedBgId != _selectedBgId || _previewRoot == null || _viewportDirty)
                SpawnBackground(def);

            _vp.EnsureCamera();
            if (_vp.Cam == null || _previewRoot == null)
            {
                DrawVpEmpty(vp, "Viewport unavailable.");
                return;
            }

            _vp.Render();

            // BG + RT
            ViewportRenderer.DrawBackground(vp);
            _vp.DrawRT(vp);

            Rect drawn = _vp.GetDrawnRect(vp);
            DrawOverlays(drawn, def);
            DrawVpToolbar(vp);

            // Sprite picker overlay (covers viewport when active)
            if (_spritePickerTargetIdx >= 0 && _spritePickerTargetIdx < def.Layers.Count)
                DrawSpritePickerOverlay(vp, def);

            HandleVpInput(vp, drawn, def);
        }

        private void DrawVpEmpty(Rect vp, string msg)
        {
            GUI.Box(vp, "", GUI.skin.box);
            if (_vpCenteredStyle == null)
                _vpCenteredStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 13,
                    normal = { textColor = new Color(0.5f, 0.5f, 0.55f) }
                };
            GUI.Label(vp, msg, _vpCenteredStyle);
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPAWN / REBUILD SCENE
        // ═══════════════════════════════════════════════════════════════

        private void SpawnBackground(BackgroundDef def)
        {
            if (_previewRoot != null) UnityEngine.Object.Destroy(_previewRoot);
            _layerGOs.Clear();

            _previewRoot = new GameObject("[BgEditor] Root");
            UnityEngine.Object.DontDestroyOnLoad(_previewRoot);
            _previewRoot.transform.position = ViewportOrigin;

            var bgRootGO = new GameObject("BgRoot");
            bgRootGO.transform.SetParent(_previewRoot.transform, false);
            bgRootGO.transform.localScale = new Vector3(0.545f, 0.545f, 1f);
            _bgRoot = bgRootGO.transform;

            for (int i = 0; i < def.Layers.Count; i++)
                SpawnLayerGO(def.Layers[i], i);

            // Spawn slot ovals at character positions
            SpawnSlotOvals();

            _loadedBgId = _selectedBgId;
            _viewportDirty = false;
        }

        private void SpawnLayerGO(BackgroundLayerDef layer, int index)
        {
            var go = new GameObject(layer.Name);
            go.transform.SetParent(_bgRoot, false);
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
                    sr.enabled = layer.Enabled;
                    if (!string.IsNullOrEmpty(layer.SpriteName))
                    {
                        var sprite = MapEditor.FindGameSprite(layer.SpriteName);
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
                    light.enabled = layer.Enabled;
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
                        psr.enabled = layer.Enabled;
                        if (psr.sharedMaterial == null)
                        {
                            var defaultMat = new Material(Shader.Find("Particles/Standard Unlit"));
                            defaultMat.SetFloat("_Mode", 1);
                            psr.sharedMaterial = defaultMat;
                        }
                    }
                    if (layer.PlayOnAwake) ps.Play();
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
                    mask.enabled = layer.Enabled;
                    if (!string.IsNullOrEmpty(layer.SpriteName))
                    {
                        var sprite = MapEditor.FindGameSprite(layer.SpriteName);
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
                    sr.enabled = layer.Enabled;
                    if (!string.IsNullOrEmpty(layer.ShaderName))
                    {
                        var shader = Shader.Find(layer.ShaderName) ?? Resources.Load<Shader>(layer.ShaderName);
                        if (shader != null)
                            sr.material = new Material(shader);
                    }
                    ShaderEffectRegistry.ApplyToMaterial(sr.material, layer.ShaderKeywords, layer.ShaderFloats);
                    break;
                }

                case VisualLayerType.PrefabEffect:
                {
                    if (!string.IsNullOrEmpty(layer.EffectName))
                    {
                        var prefab = Globals.Instance?.GetResourceEffect(layer.EffectName);
                        if (prefab != null)
                        {
                            var clone = UnityEngine.Object.Instantiate(prefab, go.transform);
                            clone.name = layer.EffectName;
                        }
                    }
                    break;
                }
            }

            go.SetActive(layer.Enabled && layer.Visible);
            _layerGOs[index] = go;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SLOT OVALS (character position markers)
        // ═══════════════════════════════════════════════════════════════

        private static Texture2D _ovalTex;
        private static Sprite _ovalSprite;

        /// <summary>Spawn oval markers at hero/NPC slot positions in the viewport scene.</summary>
        private void SpawnSlotOvals()
        {
            EnsureOvalSprite();
            if (_ovalSprite == null || _previewRoot == null) return;

            // CharGroup sits at (0, -0.6, 5) in world but we're under _previewRoot.
            // _bgRoot is at (0,0,0) with scale 0.545 — slots exist in unscaled space.
            var charGroup = new GameObject("SlotOvals");
            charGroup.transform.SetParent(_previewRoot.transform, false);
            charGroup.transform.localPosition = new Vector3(0f, -0.6f, 5f);

            const float baseOff = 2.4f;
            const float spacing = 1.9f;
            const float ovalScaleX = 1.4f;
            const float ovalScaleY = 0.35f;

            for (int side = 0; side < 2; side++)
            {
                bool isHero = side == 0;
                Color col = isHero
                    ? new Color(0.3f, 0.6f, 1f, 0.35f)
                    : new Color(1f, 0.35f, 0.3f, 0.35f);
                string label = isHero ? "H" : "E";

                for (int slot = 0; slot < 4; slot++)
                {
                    float x = baseOff + slot * spacing;
                    if (isHero) x = -x;

                    var go = new GameObject($"SlotOval_{label}{slot}");
                    go.transform.SetParent(charGroup.transform, false);
                    go.transform.localPosition = new Vector3(x, -0.05f, 10f);
                    go.transform.localScale = new Vector3(ovalScaleX, ovalScaleY, 1f);

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = _ovalSprite;
                    sr.color = col;
                    sr.sortingOrder = 900;
                }
            }
        }

        private static void EnsureOvalSprite()
        {
            if (_ovalSprite != null) return;

            const int size = 64;
            _ovalTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = (size - 1) * 0.5f;
            float radius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float dist = dx * dx + dy * dy;
                    float alpha = dist <= 0.64f ? 1f :
                                  dist >= 1f ? 0f :
                                  1f - (dist - 0.64f) / 0.36f;
                    _ovalTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _ovalTex.Apply();
            _ovalTex.filterMode = FilterMode.Bilinear;

            _ovalSprite = Sprite.Create(_ovalTex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERLAYS
        // ═══════════════════════════════════════════════════════════════

        // ── Dot colors per layer type (matches zone map editor) ────
        private static readonly Dictionary<VisualLayerType, Color> DotColors = new()
        {
            { VisualLayerType.Sprite,         new Color(0.5f, 1f,   0.5f, 0.9f) },
            { VisualLayerType.ParticleSystem,  new Color(1f,   0.55f, 0f,  0.9f) },
            { VisualLayerType.Light,           new Color(1f,   1f,   0.3f, 0.9f) },
            { VisualLayerType.SpriteMask,      new Color(0.55f, 0.55f, 1f, 0.9f) },
            { VisualLayerType.Container,       new Color(0.6f, 0.6f, 0.6f, 0.7f) },
            { VisualLayerType.Shader,          new Color(0.9f, 0.3f, 0.9f, 0.9f) },
            { VisualLayerType.PrefabEffect,    new Color(1f,   0.25f, 0.25f, 0.9f) },
        };

        private const float DotRadiusPx = 6f;
        private const float DotHitRadiusPx = 10f;

        private void DrawOverlays(Rect drawn, BackgroundDef def)
        {
            // Camera bounds
            if (_showCameraBounds)
                DrawCameraBoundsOverlay(drawn);

            // Draggable dots for non-sprite layers
            DrawLayerDots(drawn, def);

            // Selected layer bounding box + scale handles
            if (_selectedLayerIdx >= 0 && _selectedLayerIdx < def.Layers.Count)
            {
                DrawLayerBoundingBox(drawn, _selectedLayerIdx, new Color(0f, 1f, 0.6f, 0.7f));
                DrawScaleHandles(drawn, _selectedLayerIdx);
            }

            // Hovered layer bounding box (if different from selected)
            if (_hoveredLayerIdx >= 0 && _hoveredLayerIdx != _selectedLayerIdx
                && _hoveredLayerIdx < def.Layers.Count)
                DrawLayerBoundingBox(drawn, _hoveredLayerIdx, new Color(1f, 1f, 1f, 0.4f));

            // Info overlay
            DrawInfoOverlay(drawn, def);
        }

        private void DrawLayerDots(Rect drawn, BackgroundDef def)
        {
            var lineMat = ViewportRenderer.LineMaterial;
            if (lineMat == null) return;

            GL.PushMatrix();
            lineMat.SetPass(0);
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

            for (int i = 0; i < def.Layers.Count; i++)
            {
                var layer = def.Layers[i];
                if (!HasBounds(layer)) // only draw dots for boundless layers
                {
                    if (!_layerGOs.TryGetValue(i, out var go) || go == null || !go.activeSelf) continue;
                    Vector2 sp = _vp.WorldToViewport(go.transform.position, drawn);
                    if (!drawn.Contains(sp)) continue;

                    if (!DotColors.TryGetValue(layer.Type, out var dotColor))
                        dotColor = new Color(0.8f, 0.8f, 0.8f, 0.7f);

                    bool isSel = i == _selectedLayerIdx;
                    bool isHov = i == _hoveredLayerIdx;
                    float r = isSel ? DotRadiusPx + 2f : (isHov ? DotRadiusPx + 1f : DotRadiusPx);

                    // Ring for selected/hovered
                    if (isSel || isHov)
                    {
                        Color ring = isSel ? new Color(0f, 1f, 0.6f, 0.9f) : new Color(1f, 1f, 1f, 0.7f);
                        DrawFilledCircleGL(sp.x, sp.y, r + 2f, ring, 14);
                    }

                    DrawFilledCircleGL(sp.x, sp.y, r, dotColor, 14);
                }
            }

            GL.PopMatrix();

            // Type labels next to dots
            for (int i = 0; i < def.Layers.Count; i++)
            {
                var layer = def.Layers[i];
                if (HasBounds(layer)) continue;
                if (!_layerGOs.TryGetValue(i, out var go2) || go2 == null || !go2.activeSelf) continue;
                Vector2 sp2 = _vp.WorldToViewport(go2.transform.position, drawn);
                if (!drawn.Contains(sp2)) continue;

                string shortType = layer.Type == VisualLayerType.ParticleSystem ? "PS"
                    : layer.Type == VisualLayerType.SpriteMask ? "Mask"
                    : layer.Type.ToString();
                string dotLabel = string.IsNullOrEmpty(layer.Name) ? shortType : layer.Name;
                GUI.Label(new Rect(sp2.x + DotRadiusPx + 4f, sp2.y - 8f, 120, 16),
                    $"<color=#ccc><size=10>{dotLabel}</size></color>", EditorStyles.RichLabel);
            }
        }

        private static bool HasBounds(BackgroundLayerDef layer)
        {
            return layer.Type == VisualLayerType.Sprite || layer.Type == VisualLayerType.Shader;
        }

        private static void DrawFilledCircleGL(float cx, float cy, float r, Color color, int segments)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            float step = 2f * Mathf.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                GL.Vertex3(cx, cy, 0);
                GL.Vertex3(cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, 0);
                GL.Vertex3(cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, 0);
            }
            GL.End();
        }

        private void DrawCameraBoundsOverlay(Rect drawn)
        {
            float halfH = GameOrthoSize;
            float halfW = GameOrthoSize * GameAspect;

            float cx = ViewportOrigin.x;
            float cy = ViewportOrigin.y;

            Vector3 wMin = new(cx - halfW, cy - halfH, 0f);
            Vector3 wMax = new(cx + halfW, cy + halfH, 0f);

            Vector2 sMin = _vp.WorldToViewport(wMin, drawn);
            Vector2 sMax = _vp.WorldToViewport(wMax, drawn);

            float x0 = Mathf.Min(sMin.x, sMax.x);
            float x1 = Mathf.Max(sMin.x, sMax.x);
            float y0 = Mathf.Min(sMin.y, sMax.y);
            float y1 = Mathf.Max(sMin.y, sMax.y);

            var lineMat = ViewportRenderer.LineMaterial;
            if (lineMat == null) return;

            Color camColor = new(1f, 0.6f, 0.2f, 0.55f);

            GL.PushMatrix();
            lineMat.SetPass(0);
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

            DrawDashedLine(x0, y0, x1, y0, camColor, 8f, 4f);
            DrawDashedLine(x1, y0, x1, y1, camColor, 8f, 4f);
            DrawDashedLine(x1, y1, x0, y1, camColor, 8f, 4f);
            DrawDashedLine(x0, y1, x0, y0, camColor, 8f, 4f);

            GL.PopMatrix();

            // "CAM" label at top-left corner
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = camColor },
                fontStyle = FontStyle.Bold,
            };
            GUI.Label(new Rect(x0 + 3, y0 + 1, 40, 16), "CAM", labelStyle);
        }

        private static void DrawDashedLine(float x0, float y0, float x1, float y1,
            Color color, float dashLen, float gapLen)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            float nx = dx / len, ny = dy / len;
            float t = 0f;
            bool drawing = true;

            GL.Begin(GL.LINES);
            GL.Color(color);
            while (t < len)
            {
                float seg = drawing ? dashLen : gapLen;
                float t1 = Mathf.Min(t + seg, len);
                if (drawing)
                {
                    GL.Vertex3(x0 + nx * t, y0 + ny * t, 0);
                    GL.Vertex3(x0 + nx * t1, y0 + ny * t1, 0);
                }
                t = t1;
                drawing = !drawing;
            }
            GL.End();
        }

        private void DrawLayerBoundingBox(Rect drawn, int layerIdx, Color color)
        {
            if (!_layerGOs.TryGetValue(layerIdx, out var go) || go == null) return;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null)
            {
                // Boundless layer — draw a highlight ring around its dot
                Vector2 sp = _vp.WorldToViewport(go.transform.position, drawn);
                var ringMat = ViewportRenderer.LineMaterial;
                if (ringMat == null) return;
                GL.PushMatrix();
                ringMat.SetPass(0);
                GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                DrawFilledCircleGL(sp.x, sp.y, DotRadiusPx + 4f, new Color(color.r, color.g, color.b, 0.35f), 14);
                GL.PopMatrix();
                return;
            }

            Bounds b = sr.bounds;
            Vector2 min = _vp.WorldToViewport(b.min, drawn);
            Vector2 max = _vp.WorldToViewport(b.max, drawn);

            float x0 = Mathf.Min(min.x, max.x);
            float x1 = Mathf.Max(min.x, max.x);
            float y0 = Mathf.Min(min.y, max.y);
            float y1 = Mathf.Max(min.y, max.y);

            var lineMat = ViewportRenderer.LineMaterial;
            if (lineMat == null) return;

            GL.PushMatrix();
            lineMat.SetPass(0);
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
            GL.Begin(GL.LINES);
            GL.Color(color);
            GL.Vertex3(x0, y0, 0); GL.Vertex3(x1, y0, 0);
            GL.Vertex3(x1, y0, 0); GL.Vertex3(x1, y1, 0);
            GL.Vertex3(x1, y1, 0); GL.Vertex3(x0, y1, 0);
            GL.Vertex3(x0, y1, 0); GL.Vertex3(x0, y0, 0);
            GL.End();
            GL.PopMatrix();
        }

        private void DrawInfoOverlay(Rect drawn, BackgroundDef def)
        {
            // Hovered layer tooltip
            if (_hoveredLayerIdx >= 0 && _hoveredLayerIdx < def.Layers.Count
                && _layerGOs.TryGetValue(_hoveredLayerIdx, out var hgo) && hgo != null)
            {
                var layer = def.Layers[_hoveredLayerIdx];
                Vector2 sp = _vp.WorldToViewport(hgo.transform.position, drawn);
                string detail = layer.Type == VisualLayerType.Sprite
                    ? (!string.IsNullOrEmpty(layer.SpriteName) ? layer.SpriteName : "(no sprite)")
                    : layer.Type.ToString();
                if (_layerOverlapCandidates.Count > 1)
                    detail += $"  Tab: {_layerOverlapCycleIndex + 1}/{_layerOverlapCandidates.Count}";
                EditorStyles.DrawTooltip(sp.x + 16, sp.y - 12, layer.Name, detail,
                    new Color(0.7f, 0.9f, 1f));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  TOOLBAR
        // ═══════════════════════════════════════════════════════════════

        private void DrawVpToolbar(Rect vp)
        {
            float bw = 70f, bh = 22f;
            float tx = vp.x + 8, ty = vp.y + 6;

            if (GUI.Button(new Rect(tx, ty, bw, bh), "Reset View", EditorStyles.MiniButton))
                _vp.ResetView(5.4f);
            tx += bw + 4;

            string camLabel = _showCameraBounds ? "Cam ON" : "Cam OFF";
            if (GUI.Button(new Rect(tx, ty, 54, bh), camLabel, EditorStyles.MiniButton))
                _showCameraBounds = !_showCameraBounds;
            tx += 58;

            if (GUI.Button(new Rect(tx, ty, 54, bh), "Rebuild", EditorStyles.MiniButton))
                ForceRebuild();
        }

        // ═══════════════════════════════════════════════════════════════
        //  INPUT HANDLING
        // ═══════════════════════════════════════════════════════════════

        private void HandleVpInput(Rect vp, Rect drawn, BackgroundDef def)
        {
            Event e = Event.current;
            Vector2 mp = e.mousePosition;

            // Allow input outside viewport during active drags
            if (!vp.Contains(mp) && _dragLayerIdx < 0 && _scaleLayerIdx < 0 && _rotateLayerIdx < 0)
                return;

            UpdateLayerHover(mp, drawn, def);

            // Zoom / Pan (scroll + right-drag)
            if (_vp.HandleZoomPan(vp, drawn, 0.3f, 0.5f, 20f))
                return;

            // ── Scale drag in progress ────────────────────────────
            if (_scaleLayerIdx >= 0)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    HandleScaleDrag(mp, drawn, def);
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    CommitLayerScale(def);
                    _scaleLayerIdx = -1;
                    _scaleHandleIndex = -1;
                    e.Use(); return;
                }
            }

            // ── Rotation drag in progress ─────────────────────────
            if (_rotateLayerIdx >= 0)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    HandleRotateDrag(mp, drawn, def);
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    CommitLayerRotation(def);
                    _rotateLayerIdx = -1;
                    e.Use(); return;
                }
            }

            // ── Position drag in progress ─────────────────────────
            if (_dragLayerIdx >= 0)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    if (_bgRoot == null) return;
                    Vector2 worldPos = _vp.ViewportToWorld(mp, drawn);
                    Vector2 worldDelta = worldPos - _dragStartWorld;
                    Vector3 localDelta = new(
                        worldDelta.x / _bgRoot.lossyScale.x,
                        worldDelta.y / _bgRoot.lossyScale.y, 0f);
                    Vector3 newPos = _dragLayerStartPos + localDelta;
                    if (_layerGOs.TryGetValue(_dragLayerIdx, out var lgo) && lgo != null)
                        lgo.transform.localPosition = newPos;
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    CommitLayerPosition(def);
                    _dragLayerIdx = -1;
                    e.Use(); return;
                }
            }

            // ── Mouse down: start interaction ─────────────────────
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // Check scale handles on selected layer
                if (_selectedLayerIdx >= 0 && _selectedLayerIdx < def.Layers.Count)
                {
                    int hit = HitTestScaleHandle(mp, drawn, _selectedLayerIdx);
                    if (hit >= 0)
                    {
                        StartScaleDrag(_selectedLayerIdx, hit, mp, drawn, def);
                        e.Use(); return;
                    }
                }

                if (_hoveredLayerIdx >= 0)
                {
                    if (e.shift)
                        StartRotateDrag(_hoveredLayerIdx, mp, drawn, def);
                    else if (e.control)
                        StartUniformScaleDrag(_hoveredLayerIdx, mp, drawn, def);
                    else
                        StartDragLayer(_hoveredLayerIdx, mp, drawn, def);
                    e.Use(); return;
                }

                // Click on nothing → deselect
                _selectedLayerIdx = -1;
                _expandedLayerIdx = -1;
                e.Use();
            }

            // Tab = cycle overlapping layers and select in panel
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab && _layerOverlapCandidates.Count > 1)
            {
                _layerOverlapCycleIndex = (_layerOverlapCycleIndex + 1) % _layerOverlapCandidates.Count;
                int idx = _layerOverlapCandidates[_layerOverlapCycleIndex];
                _hoveredLayerIdx = idx;
                _selectedLayerIdx = idx;
                _expandedLayerIdx = idx;
                _layerRenameBuffer = def.Layers[idx].Name;
                e.Use();
            }
        }

        private void UpdateLayerHover(Vector2 mp, Rect drawn, BackgroundDef def)
        {
            _hoveredLayerIdx = -1;
            if (!drawn.Contains(mp)) return;

            Vector2 worldMouse = _vp.ViewportToWorld(mp, drawn);

            // Build overlap candidates sorted by sorting order descending
            _layerOverlapCandidates.Clear();
            var candidates = new List<(int idx, int order)>();

            for (int i = 0; i < def.Layers.Count; i++)
            {
                if (!_layerGOs.TryGetValue(i, out var go) || go == null || !go.activeSelf) continue;

                var layer = def.Layers[i];
                if (HasBounds(layer))
                {
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr == null || sr.sprite == null) continue;
                    if (sr.bounds.Contains(new Vector3(worldMouse.x, worldMouse.y, sr.bounds.center.z)))
                        candidates.Add((i, layer.SortingOrder));
                }
                else
                {
                    // Boundless layer — hit-test against dot radius in screen space
                    Vector2 sp = _vp.WorldToViewport(go.transform.position, drawn);
                    if (Vector2.Distance(mp, sp) <= DotHitRadiusPx)
                        candidates.Add((i, layer.SortingOrder));
                }
            }

            candidates.Sort((a, b) => b.order.CompareTo(a.order));
            foreach (var c in candidates)
                _layerOverlapCandidates.Add(c.idx);

            if (_layerOverlapCandidates.Count > 0)
            {
                if (_layerOverlapCycleIndex >= _layerOverlapCandidates.Count)
                    _layerOverlapCycleIndex = 0;
                _hoveredLayerIdx = _layerOverlapCandidates[_layerOverlapCycleIndex];
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE PICKER OVERLAY
        // ═══════════════════════════════════════════════════════════════

        private static GUIStyle _spTitleStyle;
        private static Texture2D _spBgTex;

        private void DrawSpritePickerOverlay(Rect vp, BackgroundDef def)
        {
            if (_spBgTex == null)
                _spBgTex = ModEditor.MakeTex(2, 2, new Color(0.06f, 0.06f, 0.08f, 0.96f));
            if (_spTitleStyle == null)
                _spTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, fontStyle = FontStyle.Bold, richText = true,
                    normal = { textColor = new Color(0.9f, 0.85f, 0.7f) }
                };

            var layer = def.Layers[_spritePickerTargetIdx];

            // Cover viewport
            GUI.DrawTexture(vp, _spBgTex);

            float pad = 10f;
            Rect inner = new(vp.x + pad, vp.y + pad, vp.width - pad * 2, vp.height - pad * 2);

            // ── Header area (above the split) ────────────────────
            float headerH = 68f;
            Rect headerRect = new(inner.x, inner.y, inner.width, headerH);
            GUILayout.BeginArea(headerRect);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Select Sprite for: <color=cyan>{layer.Name}</color>", _spTitleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close  \u2716", EditorStyles.MiniButton, GUILayout.Width(70)))
                _spritePickerTargetIdx = -1;
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            string current = layer.SpriteName ?? "";
            GUILayout.Label($"Current: <color=#aaa>{(string.IsNullOrEmpty(current) ? "(none)" : current)}</color>",
                EditorStyles.RichLabel);

            GUILayout.EndArea();

            float bodyY = inner.y + headerH + 2f;
            float bodyH = inner.height - headerH - 2f;
            Rect gridRect = new(inner.x, bodyY, inner.width, bodyH);

            DrawSpriteGridPanel(gridRect, current, def);
        }

        private void DrawSpriteGridPanel(Rect rect, string current, BackgroundDef def)
        {
            GUILayout.BeginArea(rect);

            // Search field
            GUILayout.BeginHorizontal();
            GUILayout.Label("\u2315", GUILayout.Width(14));
            string newFilter = GUILayout.TextField(_spritePickerFilter);
            if (newFilter != _spritePickerFilter)
            {
                _spritePickerFilter = newFilter;
                _spritePickerPage = 0;
            }
            if (GUILayout.Button("Clear", EditorStyles.MiniButton, GUILayout.Width(42)))
            {
                _spritePickerFilter = "";
                _spritePickerPage = 0;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // Build filtered list (respects group + text filter)
            RebuildFilteredSprites();

            int totalMatches = _spritePickerFiltered.Length;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalMatches / SpritePickerPageSize));
            if (_spritePickerPage >= totalPages) _spritePickerPage = totalPages - 1;
            if (_spritePickerPage < 0) _spritePickerPage = 0;

            // Pagination
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#888>{totalMatches}</color>", EditorStyles.RichLabel, GUILayout.Width(48));
            GUI.enabled = _spritePickerPage > 0;
            if (GUILayout.Button("\u25C0", EditorStyles.MiniButton, GUILayout.Width(22)))
                _spritePickerPage--;
            GUI.enabled = true;
            GUILayout.Label($"<color=#aaa>{_spritePickerPage + 1}/{totalPages}</color>",
                EditorStyles.RichLabel, GUILayout.Width(48));
            GUI.enabled = _spritePickerPage < totalPages - 1;
            if (GUILayout.Button("\u25B6", EditorStyles.MiniButton, GUILayout.Width(22)))
                _spritePickerPage++;
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("(none)", EditorStyles.MiniButton, GUILayout.Width(50)))
                ApplySpritePick(def, "");
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // Scrollable grid
            _spritePickerScroll = GUILayout.BeginScrollView(_spritePickerScroll);

            int startIdx = _spritePickerPage * SpritePickerPageSize;
            int endIdx = Mathf.Min(startIdx + SpritePickerPageSize, totalMatches);
            const float cellSize = 96f;
            const float thumbSize = 80f;
            const float cellPad = 4f;
            float availWidth = rect.width - 20f;
            int cols = Mathf.Max(1, Mathf.FloorToInt(availWidth / (cellSize + cellPad)));

            int col = 0;
            GUILayout.BeginHorizontal();
            for (int idx = startIdx; idx < endIdx; idx++)
            {
                string name = _spritePickerFiltered[idx];
                bool isCurrent = name == current;

                var cellStyle = isCurrent ? GUI.skin.box : GUIStyle.none;
                GUILayout.BeginVertical(cellStyle, GUILayout.Width(cellSize), GUILayout.Height(cellSize + 14f));

                Texture2D thumb = GetSpriteThumb(name);
                Rect thumbRect = GUILayoutUtility.GetRect(thumbSize, thumbSize,
                    GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));
                if (thumb != null)
                    GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit);
                else
                    GUI.Label(thumbRect, "<color=#444>\u25A1</color>", EditorStyles.RichLabel);

                if (Event.current.type == EventType.MouseDown && thumbRect.Contains(Event.current.mousePosition))
                {
                    ApplySpritePick(def, name);
                    Event.current.Use();
                }

                string shortName = name.Length > 13 ? name.Substring(0, 12) + "\u2026" : name;
                var lblStyle = isCurrent ? EditorStyles.MiniLabelBold : EditorStyles.MiniLabel;
                GUILayout.Label(new GUIContent(shortName, name), lblStyle,
                    GUILayout.Width(cellSize), GUILayout.Height(12f));

                GUILayout.EndVertical();

                col++;
                if (col >= cols && idx < endIdx - 1)
                {
                    col = 0;
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();

            if (totalMatches == 0)
                GUILayout.Label("<color=#888>No matches</color>", EditorStyles.RichLabel);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void RebuildFilteredSprites()
        {
            string filt = (_spritePickerFilter ?? "").ToLower();
            if (_spritePickerFiltered != null && _lastSpriteFilter == filt)
                return;

            _lastSpriteFilter = filt;

            string[] source = MapEditor.GetAllSpriteNames();

            if (string.IsNullOrEmpty(filt))
            {
                _spritePickerFiltered = source;
            }
            else
            {
                var filtered = new List<string>();
                foreach (var n in source)
                    if (n.ToLower().Contains(filt)) filtered.Add(n);
                _spritePickerFiltered = filtered.ToArray();
            }
        }

        private void InvalidateFilteredSprites()
        {
            _spritePickerFiltered = null;
            _lastSpriteFilter = null;
            _spritePickerPage = 0;
            _spritePickerScroll = Vector2.zero;
        }

        private Texture2D GetSpriteThumb(string spriteName)
        {
            if (_spriteThumbCache.TryGetValue(spriteName, out var cached))
                return cached;

            var sprite = MapEditor.FindGameSprite(spriteName);
            Texture2D tex = null;
            if (sprite != null)
            {
                try { tex = ViewportPreview.GetSpriteTexture(sprite); }
                catch { }
            }
            _spriteThumbCache[spriteName] = tex;
            return tex;
        }

        private void ApplySpritePick(BackgroundDef def, string spriteName)
        {
            if (_spritePickerTargetIdx < 0 || _spritePickerTargetIdx >= def.Layers.Count) return;
            var layer = def.Layers[_spritePickerTargetIdx];

            layer.SpriteName = spriteName;

            // Update the viewport GO if it exists
            if (_layerGOs.TryGetValue(_spritePickerTargetIdx, out var go) && go != null)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (!string.IsNullOrEmpty(spriteName))
                    {
                        var newSprite = MapEditor.FindGameSprite(spriteName);
                        if (newSprite != null) sr.sprite = newSprite;
                    }
                    else
                    {
                        sr.sprite = null;
                    }
                }
            }
            else
            {
                _viewportDirty = true;
            }

            MarkDirty();
            _spritePickerTargetIdx = -1;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYER DRAGGING (position)
        // ═══════════════════════════════════════════════════════════════

        private void StartDragLayer(int layerIdx, Vector2 mousePos, Rect drawn, BackgroundDef def)
        {
            _dragLayerIdx = layerIdx;
            _selectedLayerIdx = layerIdx;
            _expandedLayerIdx = layerIdx;
            _layerRenameBuffer = def.Layers[layerIdx].Name;
            _dragStartWorld = _vp.ViewportToWorld(mousePos, drawn);
            if (_layerGOs.TryGetValue(layerIdx, out var go) && go != null)
                _dragLayerStartPos = go.transform.localPosition;
        }

        private void CommitLayerPosition(BackgroundDef def)
        {
            if (_dragLayerIdx < 0 || _dragLayerIdx >= def.Layers.Count) return;
            if (!_layerGOs.TryGetValue(_dragLayerIdx, out var go) || go == null) return;
            var layer = def.Layers[_dragLayerIdx];
            layer.PosX = go.transform.localPosition.x;
            layer.PosY = go.transform.localPosition.y;
            layer.PosZ = go.transform.localPosition.z;
            MarkDirty();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ROTATION DRAG
        // ═══════════════════════════════════════════════════════════════

        private void StartRotateDrag(int layerIdx, Vector2 mousePos, Rect drawn, BackgroundDef def)
        {
            _rotateLayerIdx = layerIdx;
            _selectedLayerIdx = layerIdx;
            _expandedLayerIdx = layerIdx;
            _layerRenameBuffer = def.Layers[layerIdx].Name;
            _rotateStartRotation = def.Layers[layerIdx].Rotation;

            if (!_layerGOs.TryGetValue(layerIdx, out var go) || go == null) return;
            Vector2 center = _vp.WorldToViewport(go.transform.position, drawn);
            Vector2 delta = mousePos - center;
            _rotateStartAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        }

        private void HandleRotateDrag(Vector2 mousePos, Rect drawn, BackgroundDef def)
        {
            if (_rotateLayerIdx < 0 || _rotateLayerIdx >= def.Layers.Count) return;
            if (!_layerGOs.TryGetValue(_rotateLayerIdx, out var go) || go == null) return;

            Vector2 center = _vp.WorldToViewport(go.transform.position, drawn);
            Vector2 delta = mousePos - center;
            float currentAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            // Screen Y is inverted relative to world rotation, so subtract
            float angleDelta = -(currentAngle - _rotateStartAngle);
            float newRot = _rotateStartRotation + angleDelta;

            // Snap to 15-degree increments when close
            float snapped = Mathf.Round(newRot / 15f) * 15f;
            if (Mathf.Abs(newRot - snapped) < 2f)
                newRot = snapped;

            def.Layers[_rotateLayerIdx].Rotation = newRot;
            go.transform.localEulerAngles = new Vector3(0, 0, newRot);
        }

        private void CommitLayerRotation(BackgroundDef def)
        {
            if (_rotateLayerIdx < 0 || _rotateLayerIdx >= def.Layers.Count) return;
            MarkDirty();
        }

        // ═══════════════════════════════════════════════════════════════
        //  UNIFORM SCALE DRAG (Ctrl+drag)
        // ═══════════════════════════════════════════════════════════════

        private void StartUniformScaleDrag(int layerIdx, Vector2 mousePos, Rect drawn, BackgroundDef def)
        {
            // Reuse the scale drag system with handle index 4 (BR corner) for uniform scaling
            _scaleLayerIdx = layerIdx;
            _scaleHandleIndex = 4; // BR = scales both X and Y
            _selectedLayerIdx = layerIdx;
            _expandedLayerIdx = layerIdx;
            _layerRenameBuffer = def.Layers[layerIdx].Name;
            _scaleStartWorld = _vp.ViewportToWorld(mousePos, drawn);

            var layer = def.Layers[layerIdx];
            _scaleStartScaleX = layer.ScaleX;
            _scaleStartScaleY = layer.ScaleY;
            _scaleStartPosX = layer.PosX;
            _scaleStartPosY = layer.PosY;

            if (_layerGOs.TryGetValue(layerIdx, out var go) && go != null)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                _scaleStartBounds = (sr != null && sr.sprite != null)
                    ? sr.bounds
                    : new Bounds(go.transform.position, Vector3.one);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SCALE HANDLES (8 handles around bounding box)
        // ═══════════════════════════════════════════════════════════════

        private int HitTestScaleHandle(Vector2 mousePos, Rect drawn, int layerIdx)
        {
            var positions = GetScaleHandlePositions(drawn, layerIdx);
            if (positions == null) return -1;
            float hitRadius = ScaleHandleSizePx + 3f;
            for (int i = 0; i < 8; i++)
            {
                if (Vector2.Distance(mousePos, positions[i]) < hitRadius)
                    return i;
            }
            return -1;
        }

        /// <summary>Compute the 8 handle screen positions for a layer's bounding box.
        /// Order: 0=TL, 1=T, 2=TR, 3=R, 4=BR, 5=B, 6=BL, 7=L</summary>
        private Vector2[] GetScaleHandlePositions(Rect drawn, int layerIdx)
        {
            if (!_layerGOs.TryGetValue(layerIdx, out var go) || go == null) return null;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return null;

            Bounds b = sr.bounds;
            Vector2 min = _vp.WorldToViewport(b.min, drawn);
            Vector2 max = _vp.WorldToViewport(b.max, drawn);

            float x0 = Mathf.Min(min.x, max.x);
            float x1 = Mathf.Max(min.x, max.x);
            float y0 = Mathf.Min(min.y, max.y);
            float y1 = Mathf.Max(min.y, max.y);
            float mx = (x0 + x1) * 0.5f;
            float my = (y0 + y1) * 0.5f;

            return new Vector2[]
            {
                new(x0, y0), // 0 TL
                new(mx, y0), // 1 T
                new(x1, y0), // 2 TR
                new(x1, my), // 3 R
                new(x1, y1), // 4 BR
                new(mx, y1), // 5 B
                new(x0, y1), // 6 BL
                new(x0, my), // 7 L
            };
        }

        private void DrawScaleHandles(Rect drawn, int layerIdx)
        {
            var positions = GetScaleHandlePositions(drawn, layerIdx);
            if (positions == null) return;

            var lineMat = ViewportRenderer.LineMaterial;
            if (lineMat == null) return;

            float hs = ScaleHandleSizePx * 0.5f;

            GL.PushMatrix();
            lineMat.SetPass(0);
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

            for (int i = 0; i < 8; i++)
            {
                var p = positions[i];
                bool active = _scaleHandleIndex == i && _scaleLayerIdx == layerIdx;
                Color fill = active ? new Color(1f, 1f, 0.3f, 1f) : new Color(0f, 1f, 0.6f, 0.9f);
                Color outline = new Color(0f, 0f, 0f, 0.8f);

                // Outline
                GL.Begin(GL.QUADS);
                GL.Color(outline);
                GL.Vertex3(p.x - hs - 1, p.y - hs - 1, 0);
                GL.Vertex3(p.x + hs + 1, p.y - hs - 1, 0);
                GL.Vertex3(p.x + hs + 1, p.y + hs + 1, 0);
                GL.Vertex3(p.x - hs - 1, p.y + hs + 1, 0);
                GL.End();

                // Fill
                GL.Begin(GL.QUADS);
                GL.Color(fill);
                GL.Vertex3(p.x - hs, p.y - hs, 0);
                GL.Vertex3(p.x + hs, p.y - hs, 0);
                GL.Vertex3(p.x + hs, p.y + hs, 0);
                GL.Vertex3(p.x - hs, p.y + hs, 0);
                GL.End();
            }

            GL.PopMatrix();
        }

        private void StartScaleDrag(int layerIdx, int handleIndex, Vector2 mousePos, Rect drawn, BackgroundDef def)
        {
            _scaleLayerIdx = layerIdx;
            _scaleHandleIndex = handleIndex;
            _selectedLayerIdx = layerIdx;
            _expandedLayerIdx = layerIdx;
            _layerRenameBuffer = def.Layers[layerIdx].Name;
            _scaleStartWorld = _vp.ViewportToWorld(mousePos, drawn);

            var layer = def.Layers[layerIdx];
            _scaleStartScaleX = layer.ScaleX;
            _scaleStartScaleY = layer.ScaleY;
            _scaleStartPosX = layer.PosX;
            _scaleStartPosY = layer.PosY;

            if (_layerGOs.TryGetValue(layerIdx, out var go) && go != null)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                _scaleStartBounds = (sr != null && sr.sprite != null)
                    ? sr.bounds
                    : new Bounds(go.transform.position, Vector3.one);
            }
        }

        private void HandleScaleDrag(Vector2 mousePos, Rect drawn, BackgroundDef def)
        {
            if (_scaleLayerIdx < 0 || _scaleLayerIdx >= def.Layers.Count) return;
            if (!_layerGOs.TryGetValue(_scaleLayerIdx, out var go) || go == null) return;

            var layer = def.Layers[_scaleLayerIdx];
            Vector2 worldMouse = _vp.ViewportToWorld(mousePos, drawn);

            // Camera bounds for snapping
            float camHalfH = GameOrthoSize;
            float camHalfW = GameOrthoSize * GameAspect;
            float camL = ViewportOrigin.x - camHalfW;
            float camR = ViewportOrigin.x + camHalfW;
            float camT = ViewportOrigin.y + camHalfH;
            float camB = ViewportOrigin.y - camHalfH;
            const float snapThreshWorld = 0.15f;

            float Snap(float val, float target) =>
                Mathf.Abs(val - target) < snapThreshWorld ? target : val;

            Bounds sb = _scaleStartBounds;
            float origMinX = sb.min.x, origMaxX = sb.max.x;
            float origMinY = sb.min.y, origMaxY = sb.max.y;
            float origW = origMaxX - origMinX;
            float origH = origMaxY - origMinY;

            float newMinX = origMinX, newMaxX = origMaxX;
            float newMinY = origMinY, newMaxY = origMaxY;

            bool moveLeft   = _scaleHandleIndex == 0 || _scaleHandleIndex == 6 || _scaleHandleIndex == 7;
            bool moveRight  = _scaleHandleIndex == 2 || _scaleHandleIndex == 3 || _scaleHandleIndex == 4;
            bool moveTop    = _scaleHandleIndex == 0 || _scaleHandleIndex == 1 || _scaleHandleIndex == 2;
            bool moveBottom = _scaleHandleIndex == 4 || _scaleHandleIndex == 5 || _scaleHandleIndex == 6;

            Vector2 delta = worldMouse - _scaleStartWorld;

            if (moveLeft)   { newMinX = Snap(origMinX + delta.x, camL); newMinX = Snap(newMinX, camR); }
            if (moveRight)  { newMaxX = Snap(origMaxX + delta.x, camR); newMaxX = Snap(newMaxX, camL); }
            if (moveTop)    { newMaxY = Snap(origMaxY + delta.y, camT); newMaxY = Snap(newMaxY, camB); }
            if (moveBottom) { newMinY = Snap(origMinY + delta.y, camB); newMinY = Snap(newMinY, camT); }

            float newW = newMaxX - newMinX;
            float newH = newMaxY - newMinY;
            if (Mathf.Abs(origW) < 0.001f || Mathf.Abs(origH) < 0.001f) return;

            float scaleRatioX = Mathf.Max(0.01f, newW / origW);
            float scaleRatioY = Mathf.Max(0.01f, newH / origH);

            float finalScaleX = _scaleStartScaleX;
            float finalScaleY = _scaleStartScaleY;
            if (moveLeft || moveRight) finalScaleX = _scaleStartScaleX * scaleRatioX;
            if (moveTop || moveBottom) finalScaleY = _scaleStartScaleY * scaleRatioY;

            // Position adjustment: anchored edge stays fixed
            // Convert world-space bound shifts to local space (divide by bgRoot lossyScale)
            float lsx = _bgRoot != null ? _bgRoot.lossyScale.x : 1f;
            float lsy = _bgRoot != null ? _bgRoot.lossyScale.y : 1f;

            float newPosX = _scaleStartPosX;
            float newPosY = _scaleStartPosY;

            if (moveLeft && !moveRight)
                newPosX = _scaleStartPosX + (newMinX - origMinX) * 0.5f / lsx;
            else if (moveRight && !moveLeft)
                newPosX = _scaleStartPosX + (newMaxX - origMaxX) * 0.5f / lsx;
            else if (moveLeft && moveRight)
                newPosX = _scaleStartPosX + ((newMinX - origMinX) + (newMaxX - origMaxX)) * 0.5f / lsx;

            if (moveBottom && !moveTop)
                newPosY = _scaleStartPosY + (newMinY - origMinY) * 0.5f / lsy;
            else if (moveTop && !moveBottom)
                newPosY = _scaleStartPosY + (newMaxY - origMaxY) * 0.5f / lsy;
            else if (moveTop && moveBottom)
                newPosY = _scaleStartPosY + ((newMinY - origMinY) + (newMaxY - origMaxY)) * 0.5f / lsy;

            layer.ScaleX = finalScaleX;
            layer.ScaleY = finalScaleY;
            layer.PosX = newPosX;
            layer.PosY = newPosY;

            go.transform.localScale = new Vector3(finalScaleX, finalScaleY, 1f);
            go.transform.localPosition = new Vector3(newPosX, newPosY, layer.PosZ);
        }

        private void CommitLayerScale(BackgroundDef def)
        {
            if (_scaleLayerIdx < 0 || _scaleLayerIdx >= def.Layers.Count) return;
            MarkDirty();
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════

        public void Cleanup()
        {
            _vp?.Cleanup();
            if (_previewRoot != null) UnityEngine.Object.Destroy(_previewRoot);
            _previewRoot = null;
            _layerGOs.Clear();
        }
    }
}
