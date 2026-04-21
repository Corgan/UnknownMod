using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;
using UnknownMod.Editor.Tabs;


namespace UnknownMod.Editor
{
    public partial class MapEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  VIEWPORT (drawn on left side of screen by ModEditor)
        // ═══════════════════════════════════════════════════════════════

        public void DrawViewport(Rect vp)
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null)
            {
                string status = ZoneEditingService.SynthesisStatus;
                DrawEmpty(vp, string.IsNullOrEmpty(status) ? "No zone loaded." : status);
                return;
            }

            // Rebuild preview if zone changed or root was destroyed
            if (_loadedZoneId != zone.ZoneId || _previewRoot == null)
                SpawnMap(zone);

            // Ensure camera exists before checking (it's created lazily by Render)
            _vp.EnsureCamera();

            if (_vp.Cam == null || _previewRoot == null)
            {
                DrawEmpty(vp, "Map unavailable.");
                return;
            }

            _vp.Render();

            // BG + RT
            ViewportRenderer.DrawBackground(vp);
            _vp.DrawRT(vp);

            Rect drawn = _vp.GetDrawnRect(vp);
            DrawOverlays(drawn, zone);
            DrawToolbar(vp);

            // Sprite picker overlay (covers viewport when active)
            if (_spritePickerTargetLayer != null)
                DrawSpritePickerOverlay(vp);

            HandleInput(vp, drawn, zone);
        }

        private void DrawEmpty(Rect vp, string msg)
        {
            GUI.Box(vp, "", GUI.skin.box);
            if (_centeredStyle == null)
                _centeredStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 13,
                    normal = { textColor = new Color(0.5f, 0.5f, 0.55f) }
                };
            GUI.Label(vp, msg, _centeredStyle);
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERLAYS (node labels, hover highlights, connection line)
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverlays(Rect drawn, ZoneDef zone)
        {
            string selectedNodeId = _parent?.SelectedNodeId;

            // Update node tint for hover/select and draw labels
            foreach (var kvp in _nodeGOs)
            {
                if (kvp.Value == null) continue;
                Vector2 sp = _vp.WorldToViewport(kvp.Value.transform.position, drawn);

                bool isSelected = kvp.Key == selectedNodeId;
                bool isHovered = kvp.Key == _hoveredNodeId;

                // Tint the plain SR based on state
                if (_nodePlainSRs.TryGetValue(kvp.Key, out var plainSR) && plainSR != null)
                {
                    Color baseColor = GetNodeBaseColor(kvp.Key, zone);
                    if (isSelected)
                        plainSR.color = NodeSelectedColor;
                    else if (isHovered)
                        plainSR.color = NodeHoveredColor;
                    else
                        plainSR.color = baseColor;
                }

                if (!drawn.Contains(sp)) continue;

                // Tooltip for hovered/selected node (game-style popup)
                if (isHovered || isSelected)
                {
                    zone.Nodes.TryGetValue(kvp.Key, out var nd);
                    string title = !string.IsNullOrEmpty(nd?.NodeName) ? nd.NodeName : kvp.Key;
                    string detail = BuildNodeDetail(kvp.Key, nd, zone);
                    Color titleCol = isSelected
                        ? new Color(1f, 1f, 0.4f)
                        : new Color(1f, 0.92f, 0.7f);
                    EditorStyles.DrawTooltipAbove(sp.x, sp.y, title, detail, titleCol);
                }
                else if (_showLabels)
                {
                    // Small inline label when labels are toggled on
                    string label = kvp.Key;
                    if (zone.Nodes.TryGetValue(kvp.Key, out var nd2) && !string.IsNullOrEmpty(nd2.NodeName))
                        label = nd2.NodeName;
                    EditorStyles.DrawTooltipAbove(sp.x, sp.y, label);
                }
            }

            // Snap-to-node indicator
            if (_snapActive && _snapNodeId != null && _dragWPRoadKey != null)
            {
                Vector2 snapSP = _vp.WorldToViewport(_snapNodePos, drawn);
                if (drawn.Contains(snapSP))
                {
                    var lineMat = ViewportRenderer.LineMaterial;
                    if (lineMat != null)
                    {
                        GL.PushMatrix();
                        lineMat.SetPass(0);
                        GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                        // Draw a diamond around the snap target
                        float r = 10f;
                        GL.Begin(GL.LINES);
                        GL.Color(new Color(0f, 1f, 0.5f, 0.9f));
                        GL.Vertex3(snapSP.x, snapSP.y - r, 0); GL.Vertex3(snapSP.x + r, snapSP.y, 0);
                        GL.Vertex3(snapSP.x + r, snapSP.y, 0); GL.Vertex3(snapSP.x, snapSP.y + r, 0);
                        GL.Vertex3(snapSP.x, snapSP.y + r, 0); GL.Vertex3(snapSP.x - r, snapSP.y, 0);
                        GL.Vertex3(snapSP.x - r, snapSP.y, 0); GL.Vertex3(snapSP.x, snapSP.y - r, 0);
                        GL.End();
                        GL.PopMatrix();
                    }
                    EditorStyles.DrawTooltip(snapSP.x + 16, snapSP.y - 12,
                        $"Snap: {_snapNodeId}", null, new Color(0f, 1f, 0.5f));
                }
            }

            // Overlapping waypoint handle indicator
            if (_overlapCandidates.Count > 1 && _hoveredWPRoadKey != null)
            {
                if (_roads.WaypointHandleGOs.TryGetValue(_hoveredWPRoadKey, out var handles)
                    && _hoveredWPIndex < handles.Count && handles[_hoveredWPIndex] != null)
                {
                    Vector2 wpSP = _vp.WorldToViewport(handles[_hoveredWPIndex].transform.position, drawn);
                    string endpointLabel = _hoveredWPIndex == 0 ? "start" : "end";
                    RoadEditor.ParseRoadKey(_hoveredWPRoadKey, out var rFrom, out var rTo);
                    string roadLabel = rFrom != null ? $"{rFrom} \u2192 {rTo}" : _hoveredWPRoadKey;
                    EditorStyles.DrawTooltip(wpSP.x + 16, wpSP.y - 16,
                        $"{roadLabel} [{endpointLabel}]",
                        $"Tab: cycle ({_overlapCycleIndex + 1}/{_overlapCandidates.Count})",
                        new Color(1f, 0.9f, 0.3f));
                }
            }

            // Layer bounding box (Map subtab — selected and hovered layers)
            var subTab = _parent?.ZoneTab?.ActiveZoneInnerTab ?? ZoneTabEditor.ZoneInnerTab.Map;
            if (subTab == ZoneTabEditor.ZoneInnerTab.Map)
            {
                // Camera bounds overlay
                if (_showCameraBounds)
                    DrawCameraBoundsOverlay(drawn, zone);

                DrawLayerBoundingBoxIfApplicable(drawn, _hoveredLayerName, new Color(1f, 1f, 1f, 0.4f));
                if (_selectedLayerName != null && _selectedLayerName != _hoveredLayerName)
                    DrawLayerBoundingBoxIfApplicable(drawn, _selectedLayerName, new Color(0f, 1f, 0.6f, 0.7f));

                // Scale handles on selected layer
                if (_selectedLayerName != null)
                    DrawScaleHandles(drawn, _selectedLayerName);

                // Tooltip for hovered layer
                if (_hoveredLayerName != null)
                {
                    var hLayer = _activeLayers.Find(l => l.Name == _hoveredLayerName);
                    if (hLayer != null && _layerGOs.TryGetValue(_hoveredLayerName, out var hgo) && hgo != null)
                    {
                        Vector2 hsp = _vp.WorldToViewport(hgo.transform.position, drawn);
                        string typeStr = hLayer.Type.ToString();
                        if (_layerOverlapCandidates.Count > 1)
                            typeStr += $"  Tab: {_layerOverlapCycleIndex + 1}/{_layerOverlapCandidates.Count}";
                        EditorStyles.DrawTooltip(hsp.x + 16, hsp.y - 12, hLayer.Name, typeStr, new Color(0.7f, 0.9f, 1f));
                    }
                }

                // Origin dots for all visible layers
                DrawLayerOriginDots(drawn);
            }

            // Connection mode indicator line
            if (_connectFirstId != null && _nodeGOs.TryGetValue(_connectFirstId, out var firstGO) && firstGO != null)
            {
                var lineMat = ViewportRenderer.LineMaterial;
                if (lineMat != null)
                {
                    Vector2 from = _vp.WorldToViewport(firstGO.transform.position, drawn);
                    Vector2 to = Event.current.mousePosition;

                    GL.PushMatrix();
                    lineMat.SetPass(0);
                    GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                    GL.Begin(GL.LINES);
                    GL.Color(new Color(1f, 1f, 0.3f, 0.6f));
                    GL.Vertex3(from.x, from.y, 0);
                    GL.Vertex3(to.x, to.y, 0);
                    GL.End();
                    GL.PopMatrix();
                }
            }
        }

        /// <summary>Draw bounding box only for Sprite/SpriteMask layers (others use origin dots only).</summary>
        private void DrawLayerBoundingBoxIfApplicable(Rect drawn, string layerName, Color color)
        {
            if (layerName == null) return;
            var layer = _activeLayers.Find(l => l.Name == layerName);
            if (layer == null) return;
            // Only sprites, masks, and shader quads have meaningful visual bounds
            if (layer.Type != VisualLayerType.Sprite && layer.Type != VisualLayerType.SpriteMask && layer.Type != VisualLayerType.Shader)
                return;
            DrawLayerBoundingBox(drawn, layerName, color);
        }

        /// <summary>Draw a bounding box outline around a layer in screen space.</summary>
        private void DrawLayerBoundingBox(Rect drawn, string layerName, Color color)
        {
            if (layerName == null) return;
            var layer = _activeLayers.Find(l => l.Name == layerName);
            if (layer == null || !_layerGOs.TryGetValue(layerName, out var go) || go == null) return;

            Bounds? bounds = GetLayerWorldBounds(layer, go);
            if (bounds == null) return;

            var b = bounds.Value;
            // Convert corners to screen space
            Vector2 min = _vp.WorldToViewport(b.min, drawn);
            Vector2 max = _vp.WorldToViewport(b.max, drawn);

            // Y is flipped (viewport coords have Y=0 at top)
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
            // Top
            GL.Vertex3(x0, y0, 0); GL.Vertex3(x1, y0, 0);
            // Right
            GL.Vertex3(x1, y0, 0); GL.Vertex3(x1, y1, 0);
            // Bottom
            GL.Vertex3(x1, y1, 0); GL.Vertex3(x0, y1, 0);
            // Left
            GL.Vertex3(x0, y1, 0); GL.Vertex3(x0, y0, 0);
            GL.End();
            GL.PopMatrix();
        }

        // ═══════════════════════════════════════════════════════════════
        //  SCALE HANDLES (8 handles around selected layer bounding box)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Compute the 8 handle screen positions for a layer's bounding box.
        /// Order: 0=TL, 1=T, 2=TR, 3=R, 4=BR, 5=B, 6=BL, 7=L</summary>
        private Vector2[] GetScaleHandlePositions(Rect drawn, string layerName)
        {
            var layer = _activeLayers.Find(l => l.Name == layerName);
            if (layer == null || !_layerGOs.TryGetValue(layerName, out var go) || go == null)
                return null;
            Bounds? bounds = GetLayerWorldBounds(layer, go);
            if (bounds == null) return null;

            var b = bounds.Value;
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

        /// <summary>Draw 8 scale handles as small filled squares at the bounding box corners and edges.</summary>
        private void DrawScaleHandles(Rect drawn, string layerName)
        {
            var positions = GetScaleHandlePositions(drawn, layerName);
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
                bool active = _scaleHandleIndex == i && _scaleLayerName == layerName;
                Color fill = active ? new Color(1f, 1f, 0.3f, 1f) : new Color(0f, 1f, 0.6f, 0.9f);
                Color outline = new Color(0f, 0f, 0f, 0.8f);

                // Outline (slightly larger)
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

        // ═══════════════════════════════════════════════════════════════
        //  CAMERA BOUNDS OVERLAY (16:9 @ 5.4 ortho)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Draw the game camera's visible area as a rectangle overlay.
        /// If ZoneDef has explicit CameraBounds, draws those; otherwise draws the default 5.4 viewport centred at origin.</summary>
        private void DrawCameraBoundsOverlay(Rect drawn, ZoneDef zone)
        {
            // Game camera visible half-extents
            float halfH = GameOrthoSize;          // 5.4
            float halfW = GameOrthoSize * GameAspect; // ~9.6

            // Camera centre in world space (relative to zone root)
            float cx = PreviewOrigin.x;
            float cy = PreviewOrigin.y;

            // World corners of the camera viewport
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

            // Dashed rect — draw as solid segments with gaps
            DrawDashedLine(x0, y0, x1, y0, camColor, 8f, 4f);
            DrawDashedLine(x1, y0, x1, y1, camColor, 8f, 4f);
            DrawDashedLine(x1, y1, x0, y1, camColor, 8f, 4f);
            DrawDashedLine(x0, y1, x0, y0, camColor, 8f, 4f);

            // Label
            GL.PopMatrix();

            // Draw "CAM" label at top-left corner
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = camColor },
                fontStyle = FontStyle.Bold,
            };
            GUI.Label(new Rect(x0 + 3, y0 + 1, 40, 16), "CAM", labelStyle);
        }

        /// <summary>Draw a dashed line using GL.LINES.</summary>
        private static void DrawDashedLine(float x0, float y0, float x1, float y1, Color color, float dashLen, float gapLen)
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

        // ── Layer type → dot color mapping ───────────────────────
        private static readonly Dictionary<VisualLayerType, Color> LayerDotColors = new()
        {
            { VisualLayerType.Sprite,         new Color(0.5f, 1f,   0.5f, 0.9f) }, // green
            { VisualLayerType.ParticleSystem,  new Color(1f,   0.55f, 0f,  0.9f) }, // orange
            { VisualLayerType.Light,           new Color(1f,   1f,   0.3f, 0.9f) }, // yellow
            { VisualLayerType.SpriteMask,      new Color(0.55f, 0.55f, 1f, 0.9f) }, // blue
            { VisualLayerType.Container,       new Color(0.6f, 0.6f, 0.6f, 0.7f) }, // grey
            { VisualLayerType.Shader,          new Color(0.9f, 0.3f, 0.9f, 0.9f) }, // purple
            { VisualLayerType.PrefabEffect,    new Color(1f,   0.25f, 0.25f, 0.9f) }, // red
        };

        /// <summary>Draw a small colored dot at each visible layer's origin position.</summary>
        private void DrawLayerOriginDots(Rect drawn)
        {
            var lineMat = ViewportRenderer.LineMaterial;
            if (lineMat == null) return;

            GL.PushMatrix();
            lineMat.SetPass(0);
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

            foreach (var layer in _activeLayers)
            {
                if (!_layerGOs.TryGetValue(layer.Name, out var go) || go == null) continue;
                if (!go.activeSelf) continue;

                Vector2 sp = _vp.WorldToViewport(go.transform.position, drawn);
                if (!drawn.Contains(sp)) continue;

                if (!LayerDotColors.TryGetValue(layer.Type, out var dotColor))
                    dotColor = new Color(0.8f, 0.8f, 0.8f, 0.7f);

                bool isSelected = layer.Name == _selectedLayerName;
                bool isHovered = layer.Name == _hoveredLayerName;
                float radius = isSelected ? 5f : (isHovered ? 4f : 3f);

                // Brighter outline for selected/hovered
                if (isSelected || isHovered)
                {
                    Color ringColor = isSelected
                        ? new Color(0f, 1f, 0.6f, 0.9f)
                        : new Color(1f, 1f, 1f, 0.7f);
                    DrawFilledCircleGL(sp.x, sp.y, radius + 1.5f, ringColor, 12);
                }

                DrawFilledCircleGL(sp.x, sp.y, radius, dotColor, 12);
            }

            GL.PopMatrix();
        }

        /// <summary>Draw a filled circle using GL.TRIANGLES (call between GL.PushMatrix/PopMatrix).</summary>
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

        /// <summary>Build a short detail string for the node tooltip (type + assignment).</summary>
        private string BuildNodeDetail(string nodeId, NodeDef nd, ZoneDef zone)
        {
            if (nd == null) return nodeId;

            string prefix = zone.IdPrefix;
            if (nodeId == $"{prefix}_0") return "Entrance";
            if (nd.GoToTown) return "Town";

            if (!string.IsNullOrEmpty(nd.CombatId))
                return $"Combat: {nd.CombatId}";
            if (!string.IsNullOrEmpty(nd.EventId))
                return $"Event: {nd.EventId}";

            return nodeId;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE PICKER OVERLAY (drawn over viewport)
        // ═══════════════════════════════════════════════════════════════

        private static GUIStyle _spritePickerTitleStyle;
        private static Texture2D _spritePickerBgTex;
        private string _lastSpritePickerFilter;
        private string[] _spritePickerFiltered;

        private void DrawSpritePickerOverlay(Rect vp)
        {
            if (_spritePickerBgTex == null)
                _spritePickerBgTex = ModEditor.MakeTex(2, 2, new Color(0.06f, 0.06f, 0.08f, 0.96f));
            if (_spritePickerTitleStyle == null)
                _spritePickerTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, fontStyle = FontStyle.Bold, richText = true,
                    normal = { textColor = new Color(0.9f, 0.85f, 0.7f) }
                };

            // Cover viewport
            GUI.DrawTexture(vp, _spritePickerBgTex);

            float pad = 10f;
            Rect inner = new Rect(vp.x + pad, vp.y + pad, vp.width - pad * 2, vp.height - pad * 2);

            // ── Header area (above the split) ────────────────────
            float headerH = 68f;
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerH);
            GUILayout.BeginArea(headerRect);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Select Sprite for: <color=cyan>{_spritePickerTargetLayer.Name}</color>",
                _spritePickerTitleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close  \u2716", EditorStyles.MiniButton, GUILayout.Width(70)))
            {
                _spritePickerTargetLayer = null;
                _spritePickerTargetGO = null;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            string current = _spritePickerTargetLayer?.SpriteName ?? "";
            GUILayout.Label($"Current: <color=#aaa>{(string.IsNullOrEmpty(current) ? "(none)" : current)}</color>",
                EditorStyles.RichLabel);

            GUILayout.EndArea();

            // ── Full-width sprite grid ───────────────────────────
            float bodyY = inner.y + headerH + 2f;
            float bodyH = inner.height - headerH - 2f;

            Rect gridRect = new Rect(inner.x, bodyY, inner.width, bodyH);
            DrawSpritePickerGridPanel(gridRect, current);
        }

        private void DrawSpritePickerGridPanel(Rect rect, string current)
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
            RebuildSpritePickerFiltered();

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
                ApplySpritePickerResult("");
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

                Texture2D thumb = GetSpritePickerThumb(name);
                Rect thumbRect = GUILayoutUtility.GetRect(thumbSize, thumbSize,
                    GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));
                if (thumb != null)
                    GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit);
                else
                    GUI.Label(thumbRect, "<color=#444>\u25A1</color>", EditorStyles.RichLabel);

                if (Event.current.type == EventType.MouseDown && thumbRect.Contains(Event.current.mousePosition))
                {
                    ApplySpritePickerResult(name);
                    Event.current.Use();
                }

                string shortName = name.Length > 13 ? name.Substring(0, 12) + "\u2026" : name;
                var labelStyle = isCurrent ? EditorStyles.MiniLabelBold : EditorStyles.MiniLabel;
                GUILayout.Label(new GUIContent(shortName, name), labelStyle,
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

        private void RebuildSpritePickerFiltered()
        {
            string filt = (_spritePickerFilter ?? "").ToLower();
            if (_spritePickerFiltered != null && _lastSpritePickerFilter == filt)
                return;

            _lastSpritePickerFilter = filt;

            string[] source = GetAllSpriteNames();

            if (string.IsNullOrEmpty(filt))
            {
                _spritePickerFiltered = source;
            }
            else
            {
                var filtered = new System.Collections.Generic.List<string>();
                foreach (var n in source)
                    if (n.ToLower().Contains(filt)) filtered.Add(n);
                _spritePickerFiltered = filtered.ToArray();
            }
        }

        private void InvalidateSpritePickerFiltered()
        {
            _spritePickerFiltered = null;
            _lastSpritePickerFilter = null;
            _spritePickerPage = 0;
            _spritePickerScroll = Vector2.zero;
        }

        /// <summary>Get or create a cached thumbnail texture for a sprite name.</summary>
        private Texture2D GetSpritePickerThumb(string spriteName)
        {
            if (_spritePickerThumbCache.TryGetValue(spriteName, out var cached))
                return cached; // null means "tried and failed"

            var sprite = FindGameSprite(spriteName);
            Texture2D tex = null;
            if (sprite != null)
            {
                try { tex = ViewportPreview.GetSpriteTexture(sprite); }
                catch { }
            }
            _spritePickerThumbCache[spriteName] = tex;
            return tex;
        }

        /// <summary>Apply sprite picker selection to the target layer and close.</summary>
        private void ApplySpritePickerResult(string spriteName)
        {
            var layer = _spritePickerTargetLayer;
            if (layer == null) return;

            layer.SpriteName = spriteName;

            if (!string.IsNullOrEmpty(spriteName))
            {
                var newSprite = FindGameSprite(spriteName);
                if (newSprite != null)
                {
                    layer.SpriteWidth = newSprite.rect.width;
                    layer.SpriteHeight = newSprite.rect.height;
                    layer.PPU = newSprite.pixelsPerUnit;

                    if (_spritePickerTargetGO != null)
                    {
                        var sr = _spritePickerTargetGO.GetComponent<SpriteRenderer>();
                        if (sr != null) sr.sprite = newSprite;
                        var sm = _spritePickerTargetGO.GetComponent<SpriteMask>();
                        if (sm != null) sm.sprite = newSprite;
                    }
                }
            }

            ZoneEditingService.MarkDirty();
            _spritePickerTargetLayer = null;
            _spritePickerTargetGO = null;
        }

        private void DrawToolbar(Rect vp)
        {
            float bw = 70f, bh = 22f;
            float tx = vp.x + 8, ty = vp.y + 6;

            if (GUI.Button(new Rect(tx, ty, bw, bh), "Reset View", EditorStyles.MiniButton))
                _vp.ResetView(5.4f);
            tx += bw + 4;

            if (GUI.Button(new Rect(tx, ty, 62, bh), _showLabels ? "Labels ON" : "Labels OFF", EditorStyles.MiniButton))
                _showLabels = !_showLabels;
            tx += 66;

            if (GUI.Button(new Rect(tx, ty, 50, bh), _showWaypoints ? "WP ON" : "WP OFF", EditorStyles.MiniButton))
            {
                _showWaypoints = !_showWaypoints;
                _roads?.ShowWaypoints(_showWaypoints);
            }
            tx += 54;

            string camLabel = _showCameraBounds ? "Cam ON" : "Cam OFF";
            if (GUI.Button(new Rect(tx, ty, 54, bh), camLabel, EditorStyles.MiniButton))
                _showCameraBounds = !_showCameraBounds;

            if (_connectFirstId != null)
            {
                GUI.Label(new Rect(vp.xMax - 240, ty, 230, bh),
                    $"<color=yellow>Connecting from: {_connectFirstId}</color>", EditorStyles.RichLabel);
            }
        }
    }
}
