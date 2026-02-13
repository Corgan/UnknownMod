using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Self-contained map viewport that renders the zone map (background, nodes, roads)
    /// into its own RenderTexture + Camera. Works from anywhere — main menu, combat, etc.
    /// Uses ViewportRenderer for camera/RT management and RoadEditor for road visuals/data.
    ///
    /// Controls (inside viewport):
    ///   Left-drag node           Move node
    ///   Shift+click node A,B     Toggle road between two nodes
    ///   Ctrl+click empty space   Add new node
    ///   Backspace (hover node)   Delete node and its roads
    ///   Left-drag CP handle      Reshape road curve
    ///   Ctrl+click CP            Insert new control point
    ///   Delete (hover CP)        Remove control point (last CP deletes road)
    ///   Right-drag               Pan view
    ///   Scroll                   Zoom
    ///   Right-click node         Inspect in NodeEditor
    ///   Escape                   Cancel connection mode
    /// </summary>
    public class MapViewport
    {
        private readonly ZoneEditor _parent;

        // ── Viewport (camera + RT + zoom/pan) ────────────────────────
        private ViewportRenderer _vp;

        // ── Road data + visuals ──────────────────────────────────────
        private RoadEditor _roads;

        // ── Preview scene ────────────────────────────────────────────
        private GameObject _previewRoot;
        private GameObject _bgGO;
        private Transform _nodesContainer;
        private string _loadedZoneId;

        // ── Node visuals ─────────────────────────────────────────────
        private Dictionary<string, GameObject> _nodeGOs = new();

        // ── Node dragging ────────────────────────────────────────────
        private string _dragNodeId;
        private Vector2 _dragStartWorld;
        private Vector3 _dragNodeStartLocal;

        // ── CP dragging ──────────────────────────────────────────────
        private string _dragCPRoadKey;
        private int _dragCPIndex = -1;
        private Vector2 _dragCPStartWorld;
        private Vector3 _dragCPStartPos;

        // ── Connection mode ──────────────────────────────────────────
        private string _connectFirstId;

        // ── Hover state ──────────────────────────────────────────────
        private string _hoveredNodeId;
        private string _hoveredCPRoadKey;
        private int _hoveredCPIndex = -1;

        // ── Resources ────────────────────────────────────────────────
        private static Sprite _nodeDotSprite;

        // ── Panel state ──────────────────────────────────────────────
        private GUIStyle _centeredStyle;
        private bool _showCPs = true;
        private bool _showLabels;

        // ── Constants ────────────────────────────────────────────────
        private static readonly Vector3 PreviewOrigin = new(-5000f, -5000f, 0f);
        private const float NodeDotRadius = 0.16f;
        private const float NodePickRadiusPx = 16f;
        private const float CPPickRadiusPx = 12f;

        // ── Colors ───────────────────────────────────────────────────
        private static readonly Color NodeColor          = new(0.15f, 0.85f, 0.35f, 0.92f);
        private static readonly Color NodeEntranceColor  = new(1f,   0.85f, 0.15f, 0.95f);
        private static readonly Color NodeTownColor      = new(0.3f, 0.6f,  1f,    0.95f);
        private static readonly Color NodeSelectedColor  = Color.white;
        private static readonly Color NodeHoveredColor   = new(1f,   1f,    0.6f,  1f);
        private static readonly Color RoadColor          = new(0f,   1f,    1f,    0.65f);
        private static readonly Color CPHandleColor      = new(1f,   0.9f,  0f,    0.9f);

        public MapViewport(ZoneEditor parent)
        {
            _parent = parent;
            _vp = new ViewportRenderer(PreviewOrigin, 1280, 720, 5.4f,
                new Color(0.1f, 0.1f, 0.12f, 1f), -101f);
        }

        // ═══════════════════════════════════════════════════════════════
        //  VIEWPORT (drawn on left side of screen by ZoneEditor)
        // ═══════════════════════════════════════════════════════════════

        public void DrawViewport(Rect vp)
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null)
            {
                DrawEmpty(vp, "No zone loaded.");
                return;
            }

            // Rebuild preview if zone changed or root was destroyed
            if (_loadedZoneId != zone.ZoneId || _previewRoot == null)
                SpawnMap(zone);

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

        private GUIStyle _labelStyle, _labelSelStyle;

        private void DrawOverlays(Rect drawn, ZoneDef zone)
        {
            if (_labelStyle == null)
                _labelStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 9, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            if (_labelSelStyle == null)
                _labelSelStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } };

            string selectedNodeId = _parent?.SelectedNodeId;

            // Draw node labels and highlights
            foreach (var kvp in _nodeGOs)
            {
                if (kvp.Value == null) continue;
                Vector2 sp = _vp.WorldToViewport(kvp.Value.transform.position, drawn);
                if (!drawn.Contains(sp)) continue;

                bool isSelected = kvp.Key == selectedNodeId;
                bool isHovered = kvp.Key == _hoveredNodeId;

                if (_showLabels || isSelected || isHovered)
                {
                    var style = isSelected ? _labelSelStyle : _labelStyle;
                    string label = kvp.Key;
                    if (zone.Nodes.TryGetValue(kvp.Key, out var nd) && !string.IsNullOrEmpty(nd.NodeName))
                        label = nd.NodeName;
                    GUI.Label(new Rect(sp.x + 10, sp.y - 8, 140, 18), label, style);
                }
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

            if (GUI.Button(new Rect(tx, ty, 50, bh), _showCPs ? "CP ON" : "CP OFF", EditorStyles.MiniButton))
            {
                _showCPs = !_showCPs;
                _roads?.ShowCPs(_showCPs);
            }

            if (_connectFirstId != null)
            {
                GUI.Label(new Rect(vp.xMax - 240, ty, 230, bh),
                    $"<color=yellow>Connecting from: {_connectFirstId}</color>", EditorStyles.RichLabel);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  INPUT HANDLING
        // ═══════════════════════════════════════════════════════════════

        private void HandleInput(Rect vp, Rect drawn, ZoneDef zone)
        {
            Event e = Event.current;
            Vector2 mp = e.mousePosition;
            if (!vp.Contains(mp) && _dragNodeId == null && _dragCPRoadKey == null) return;

            // Update hover state
            UpdateHover(mp, drawn);

            // ── Zoom / Pan (scroll + right-drag) ─────────────────────
            if (_vp.HandleZoomPan(vp, drawn, 0.3f, 0.5f, 20f))
                return;

            // Right-click without drag = inspect hovered node
            if (e.type == EventType.MouseUp && e.button == 1 && _hoveredNodeId != null)
            {
                _parent?.InspectNode(_hoveredNodeId);
                e.Use();
                return;
            }

            bool shift = e.shift;
            bool ctrl = e.control;

            // ── Left click ───────────────────────────────────────────
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // CP handles first
                if (_hoveredCPRoadKey != null && !shift)
                {
                    if (ctrl)
                    {
                        InsertCPAfter(_hoveredCPRoadKey, _hoveredCPIndex);
                        e.Use(); return;
                    }
                    else
                    {
                        StartDragCP(_hoveredCPRoadKey, _hoveredCPIndex, mp, drawn);
                        e.Use(); return;
                    }
                }

                if (_hoveredNodeId != null)
                {
                    if (shift)
                    {
                        HandleConnectClick(_hoveredNodeId, zone);
                        e.Use(); return;
                    }
                    else
                    {
                        StartDragNode(_hoveredNodeId, mp, drawn);
                        e.Use(); return;
                    }
                }

                // Ctrl+click empty = add node
                if (ctrl)
                {
                    Vector2 worldPos = _vp.ViewportToWorld(mp, drawn);
                    Vector3 local = new Vector3(worldPos.x, worldPos.y, 0f) - PreviewOrigin;
                    AddNode(local.x, local.y, zone);
                    e.Use(); return;
                }

                // Click empty = deselect
                _parent.SelectedNodeId = null;
                _connectFirstId = null;
            }

            // ── Drag (node) ──────────────────────────────────────────
            if (_dragNodeId != null)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    Vector2 worldPos = _vp.ViewportToWorld(mp, drawn);
                    Vector2 delta = worldPos - _dragStartWorld;
                    Vector3 newLocal = _dragNodeStartLocal + new Vector3(delta.x, delta.y, 0f);
                    if (_nodeGOs.TryGetValue(_dragNodeId, out var ngo) && ngo != null)
                    {
                        ngo.transform.localPosition = new Vector3(newLocal.x, newLocal.y, 0f);
                        _roads.UpdateRoadsForNode(_dragNodeId);
                    }
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    CommitNodePosition(_dragNodeId, zone);
                    _dragNodeId = null;
                    e.Use(); return;
                }
            }

            // ── Drag (CP) ────────────────────────────────────────────
            if (_dragCPRoadKey != null)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    Vector2 worldPos = _vp.ViewportToWorld(mp, drawn);
                    Vector2 delta = worldPos - _dragCPStartWorld;
                    Vector3 newPos = _dragCPStartPos + new Vector3(delta.x, delta.y, 0f);
                    _roads.UpdateCPPosition(_dragCPRoadKey, _dragCPIndex, newPos);
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    CommitRoadCPs(_dragCPRoadKey, zone);
                    _dragCPRoadKey = null;
                    _dragCPIndex = -1;
                    e.Use(); return;
                }
            }

            // ── Keyboard ─────────────────────────────────────────────
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && _hoveredNodeId != null)
                {
                    DeleteNode(_hoveredNodeId, zone);
                    e.Use(); return;
                }
                if (e.keyCode == KeyCode.Delete && _hoveredCPRoadKey != null)
                {
                    DeleteCP(_hoveredCPRoadKey, _hoveredCPIndex, zone);
                    e.Use(); return;
                }
                if (e.keyCode == KeyCode.Escape && _connectFirstId != null)
                {
                    _connectFirstId = null;
                    e.Use(); return;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HOVER / PICK
        // ═══════════════════════════════════════════════════════════════

        private void UpdateHover(Vector2 mousePos, Rect drawn)
        {
            _hoveredNodeId = null;
            _hoveredCPRoadKey = null;
            _hoveredCPIndex = -1;

            if (!drawn.Contains(mousePos)) return;

            // Check CP handles first (smaller, on top)
            if (_showCPs && _roads != null)
            {
                float bestCPDist = CPPickRadiusPx;
                foreach (var kvp in _roads.CPHandleGOs)
                {
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        var go = kvp.Value[i];
                        if (go == null) continue;
                        Vector2 sp = _vp.WorldToViewport(go.transform.position, drawn);
                        float dist = Vector2.Distance(sp, mousePos);
                        if (dist < bestCPDist)
                        {
                            bestCPDist = dist;
                            _hoveredCPRoadKey = kvp.Key;
                            _hoveredCPIndex = i;
                        }
                    }
                }
                if (_hoveredCPRoadKey != null) return;
            }

            // Check nodes
            float bestDist = NodePickRadiusPx;
            foreach (var kvp in _nodeGOs)
            {
                if (kvp.Value == null) continue;
                Vector2 sp = _vp.WorldToViewport(kvp.Value.transform.position, drawn);
                float dist = Vector2.Distance(sp, mousePos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    _hoveredNodeId = kvp.Key;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  NODE DRAGGING
        // ═══════════════════════════════════════════════════════════════

        private void StartDragNode(string nodeId, Vector2 mousePos, Rect drawn)
        {
            _dragNodeId = nodeId;
            _dragStartWorld = _vp.ViewportToWorld(mousePos, drawn);
            if (_nodeGOs.TryGetValue(nodeId, out var go) && go != null)
                _dragNodeStartLocal = go.transform.localPosition;
            _parent.SelectedNodeId = nodeId;
        }

        private void CommitNodePosition(string nodeId, ZoneDef zone)
        {
            if (!_nodeGOs.TryGetValue(nodeId, out var go) || go == null) return;
            if (!zone.Nodes.TryGetValue(nodeId, out var nd)) return;

            nd.PosX = go.transform.localPosition.x;
            nd.PosY = go.transform.localPosition.y;
            ZoneEditingService.MarkDirty();
            Plugin.Log.LogInfo($"[MapViewport] Node '{nodeId}' → ({nd.PosX:F1}, {nd.PosY:F1})");
        }

        // ═══════════════════════════════════════════════════════════════
        //  CP DRAGGING
        // ═══════════════════════════════════════════════════════════════

        private void StartDragCP(string roadKey, int cpIndex, Vector2 mousePos, Rect drawn)
        {
            _dragCPRoadKey = roadKey;
            _dragCPIndex = cpIndex;
            _dragCPStartWorld = _vp.ViewportToWorld(mousePos, drawn);
            if (_roads.RoadCPs.TryGetValue(roadKey, out var cps) && cpIndex < cps.Count)
                _dragCPStartPos = cps[cpIndex];
        }

        private void CommitRoadCPs(string roadKey, ZoneDef zone)
        {
            if (!_roads.RoadCPs.TryGetValue(roadKey, out var cps)) return;
            RoadEditor.ParseRoadKey(roadKey, out var fromId, out var toId);
            if (fromId == null) return;

            if (!zone.Roads.TryGetValue(roadKey, out var rd))
            {
                rd = new RoadDef { FromNodeId = fromId, ToNodeId = toId };
                zone.Roads[roadKey] = rd;
            }
            rd.Waypoints.Clear();
            foreach (var cp in cps)
                rd.Waypoints.Add(new float[] { cp.x - PreviewOrigin.x, cp.y - PreviewOrigin.y });
            ZoneEditingService.MarkDirty();
        }

        // ═══════════════════════════════════════════════════════════════
        //  CONNECTION MODE
        // ═══════════════════════════════════════════════════════════════

        private void HandleConnectClick(string nodeId, ZoneDef zone)
        {
            if (_connectFirstId == null)
            {
                _connectFirstId = nodeId;
                return;
            }

            if (_connectFirstId == nodeId)
            {
                _connectFirstId = null;
                return;
            }

            string from = _connectFirstId;
            string to = nodeId;
            string key = from + "-" + to;
            string reverseKey = to + "-" + from;

            if (_roads.RoadCPs.ContainsKey(key))
            {
                RemoveRoad(key, zone);
                Plugin.Log.LogInfo($"[MapViewport] Removed road '{key}'");
            }
            else if (_roads.RoadCPs.ContainsKey(reverseKey))
            {
                RemoveRoad(reverseKey, zone);
                Plugin.Log.LogInfo($"[MapViewport] Removed road '{reverseKey}'");
            }
            else
            {
                CreateRoad(from, to, zone);
                Plugin.Log.LogInfo($"[MapViewport] Created road '{key}'");
            }
            _connectFirstId = null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ROAD CREATION / REMOVAL
        // ═══════════════════════════════════════════════════════════════

        private void CreateRoad(string fromId, string toId, ZoneDef zone)
        {
            string key = _roads.CreateRoad(fromId, toId);

            // Write to DTO
            if (!zone.Nodes.TryGetValue(fromId, out var fromNode)) return;
            if (!fromNode.Connections.Contains(toId))
                fromNode.Connections.Add(toId);

            Vector3 mid = _roads.RoadCPs[key][0];
            var rd = new RoadDef { FromNodeId = fromId, ToNodeId = toId };
            rd.Waypoints.Add(new float[] { mid.x - PreviewOrigin.x, mid.y - PreviewOrigin.y });
            zone.Roads[key] = rd;
            ZoneEditingService.MarkDirty();
        }

        private void RemoveRoad(string key, ZoneDef zone)
        {
            RoadEditor.ParseRoadKey(key, out var fromId, out var toId);
            _roads.RemoveRoad(key);

            // DTO cleanup
            zone.Roads.Remove(key);
            if (fromId != null && zone.Nodes.TryGetValue(fromId, out var fromNode))
                fromNode.Connections.Remove(toId);
            ZoneEditingService.MarkDirty();
        }

        // ═════════════════════════════════════════════════════════════════
        //  CP INSERT / DELETE
        // ═════════════════════════════════════════════════════════════════

        private void InsertCPAfter(string roadKey, int afterIndex)
        {
            _roads.InsertCPAfter(roadKey, afterIndex);
            CommitRoadCPs(roadKey, ZoneEditingService.CurrentZone);
        }

        private void DeleteCP(string roadKey, int cpIndex, ZoneDef zone)
        {
            bool roadRemoved = _roads.DeleteCP(roadKey, cpIndex);
            if (roadRemoved)
            {
                // Road was completely removed — update DTO
                RoadEditor.ParseRoadKey(roadKey, out var fromId, out var toId);
                zone.Roads.Remove(roadKey);
                if (fromId != null && zone.Nodes.TryGetValue(fromId, out var fromNode))
                    fromNode.Connections.Remove(toId);
                ZoneEditingService.MarkDirty();
            }
            else
            {
                CommitRoadCPs(roadKey, zone);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  NODE ADD / DELETE
        // ═══════════════════════════════════════════════════════════════

        private void AddNode(float localX, float localY, ZoneDef zone)
        {
            string nodeId = ZoneEditingService.AddNode(localX, localY);
            if (nodeId == null) return;

            CreateNodeDot(nodeId, localX, localY, zone);
            Plugin.Log.LogInfo($"[MapViewport] Added node '{nodeId}' at ({localX:F1}, {localY:F1})");
        }

        private void DeleteNode(string nodeId, ZoneDef zone)
        {
            string prefix = zone.IdPrefix;
            if (nodeId == $"{prefix}_0" || nodeId == $"{prefix}_1")
            {
                Plugin.Log.LogWarning($"[MapViewport] Cannot delete entrance/town node '{nodeId}'.");
                return;
            }

            // Remove connected roads
            if (_roads.NodeToRoads.TryGetValue(nodeId, out var roads))
            {
                foreach (string key in roads.ToList())
                    RemoveRoad(key, zone);
            }

            // Destroy visual
            if (_nodeGOs.TryGetValue(nodeId, out var go))
            {
                Object.Destroy(go);
                _nodeGOs.Remove(nodeId);
            }

            ZoneEditingService.DeleteNode(nodeId);
            Plugin.Log.LogInfo($"[MapViewport] Deleted node '{nodeId}'");
        }

        // ═══════════════════════════════════════════════════════════════
        //  PANEL (right-side, drawn inside ZoneEditor's IMGUI area)
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            GUILayout.Label("<b>Map Controls</b>", EditorStyles.RichLabel);
            GUILayout.Space(4);
            GUILayout.Label("Drag nodes to reposition", EditorStyles.RichLabel);
            GUILayout.Label("Shift+click two nodes to connect/disconnect", EditorStyles.RichLabel);
            GUILayout.Label("Ctrl+click empty space to add node", EditorStyles.RichLabel);
            GUILayout.Label("Backspace on hovered node to remove", EditorStyles.RichLabel);
            GUILayout.Label("Ctrl+click CP to insert control point", EditorStyles.RichLabel);
            GUILayout.Label("Delete on CP to delete it", EditorStyles.RichLabel);
            GUILayout.Label("Right-click node to inspect properties", EditorStyles.RichLabel);
            GUILayout.Label("Right-drag to pan, scroll to zoom", EditorStyles.RichLabel);

            GUILayout.Space(8);
            GUILayout.Label($"<b>Nodes:</b> {zone.Nodes.Count}", EditorStyles.RichLabel);
            GUILayout.Label($"<b>Roads:</b> {(_roads != null ? _roads.RoadCPs.Count : 0)}", EditorStyles.RichLabel);

            if (_connectFirstId != null)
            {
                GUILayout.Space(4);
                GUILayout.Label($"<color=yellow>Connecting from: {_connectFirstId}</color>", EditorStyles.RichLabel);
                GUILayout.Label("Shift+click another node or Esc to cancel", EditorStyles.RichLabel);
            }

            // Selected node quick info
            if (_parent?.SelectedNodeId != null && zone.Nodes.TryGetValue(_parent.SelectedNodeId, out var selNode))
            {
                GUILayout.Space(8);
                EditorStyles.Separator();
                GUILayout.Label($"<b>Selected: {_parent.SelectedNodeId}</b>", EditorStyles.RichLabel);
                GUILayout.Label($"<color=#888>Name:</color> {selNode.NodeName}", EditorStyles.RichLabel);
                GUILayout.Label($"<color=#888>Pos:</color> ({selNode.PosX:F1}, {selNode.PosY:F1})", EditorStyles.RichLabel);
                GUILayout.Label($"<color=#888>Connections:</color> {selNode.Connections.Count}", EditorStyles.RichLabel);
                if (!string.IsNullOrEmpty(selNode.CombatId))
                    GUILayout.Label($"<color=#888>Combat:</color> {selNode.CombatId}", EditorStyles.RichLabel);
                if (!string.IsNullOrEmpty(selNode.EventId))
                    GUILayout.Label($"<color=#888>Event:</color> {selNode.EventId}", EditorStyles.RichLabel);

                if (GUILayout.Button("Edit in Node Tab", EditorStyles.MiniButton))
                    _parent.InspectNode(_parent.SelectedNodeId);
            }

            GUILayout.Space(12);
            EditorStyles.Separator();
            GUILayout.Label("<b>Zone Tools</b>", EditorStyles.RichLabel);
            GUILayout.Space(4);

            if (GUILayout.Button("Reflow Node IDs (BFS)", GUILayout.Height(28)))
            {
                ZoneEditingService.ReflowNodeIds();
                _loadedZoneId = null; // forces rebuild
                Plugin.Log.LogInfo("[MapViewport] Reflow complete — viewport will rebuild.");
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Rebuild Viewport", EditorStyles.MiniButton))
                _loadedZoneId = null;

            // Node list
            GUILayout.Space(8);
            EditorStyles.Separator();
            GUILayout.Label("<b>Nodes</b>", EditorStyles.RichLabel);
            foreach (var kvp in zone.Nodes.OrderBy(kv => kv.Key))
            {
                string nodeId = kvp.Key;
                bool isSelected = nodeId == _parent?.SelectedNodeId;
                string prefix2 = isSelected ? "<color=cyan>\u25B6 </color>" : "  ";
                string nameTag = !string.IsNullOrEmpty(kvp.Value.NodeName) ? $"  <color=#888>{kvp.Value.NodeName}</color>" : "";
                if (GUILayout.Button($"{prefix2}<b>{nodeId}</b>{nameTag}", EditorStyles.ListItem))
                {
                    _parent.SelectedNodeId = nodeId;
                    // Center viewport on this node
                    if (zone.Nodes.TryGetValue(nodeId, out var nd))
                        _vp.Pan = new Vector2(nd.PosX, nd.PosY);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPAWN MAP (build preview scene from ZoneDef)
        // ═══════════════════════════════════════════════════════════════

        private void SpawnMap(ZoneDef zone)
        {
            DestroyPreview();
            _loadedZoneId = zone.ZoneId;

            // Root
            _previewRoot = new GameObject($"[MapViewport] {zone.ZoneId}");
            _previewRoot.transform.position = PreviewOrigin;
            Object.DontDestroyOnLoad(_previewRoot);

            // Background
            var bgSprite = MapBuilder.GetBackgroundSprite(zone.ZoneId);
            if (bgSprite != null)
            {
                _bgGO = new GameObject("Background");
                _bgGO.transform.SetParent(_previewRoot.transform, false);
                _bgGO.transform.localPosition = Vector3.zero;
                var sr = _bgGO.AddComponent<SpriteRenderer>();
                sr.sprite = bgSprite;
                sr.color = Color.white;
                sr.sortingOrder = -10;
            }

            // Nodes container
            var nodesGO = new GameObject("Nodes");
            nodesGO.transform.SetParent(_previewRoot.transform, false);
            _nodesContainer = nodesGO.transform;

            // Roads + CP containers
            var roadsGO = new GameObject("Roads");
            roadsGO.transform.SetParent(_previewRoot.transform, false);
            var cpGO = new GameObject("CPHandles");
            cpGO.transform.SetParent(_previewRoot.transform, false);
            cpGO.SetActive(_showCPs);

            // Initialize road material + RoadEditor
            var roadMat = new Material(Shader.Find("Sprites/Default"));
            _roads = new RoadEditor(new RoadEditor.Config
            {
                RoadColor = RoadColor,
                RoadWidth = 0.04f,
                RoadSortingOrder = -5,
                SortingLayer = null,
                UseWorldSpace = true,
                CPColor = CPHandleColor,
                CPSortingOrder = 50,
                CPZ = PreviewOrigin.z - 1f,
            });
            _roads.Init(roadsGO.transform, cpGO.transform, roadMat, GetNodeWorldPos);

            // Create node dots
            foreach (var kvp in zone.Nodes)
                CreateNodeDot(kvp.Key, kvp.Value.PosX, kvp.Value.PosY, zone);

            // Create roads from explicit Road entries
            foreach (var kvp in zone.Roads)
            {
                string key = kvp.Key;
                var rd = kvp.Value;
                RoadEditor.ParseRoadKey(key, out var fromId, out var toId);
                if (fromId == null) continue;

                var cps = new List<Vector3>();
                foreach (var wp in rd.Waypoints)
                {
                    if (wp.Length >= 2)
                        cps.Add(new Vector3(wp[0] + PreviewOrigin.x, wp[1] + PreviewOrigin.y, 0f));
                }
                if (cps.Count == 0)
                {
                    Vector3 posA = GetNodeWorldPos(fromId);
                    Vector3 posB = GetNodeWorldPos(toId);
                    cps.Add((posA + posB) / 2f);
                }

                _roads.AddRoadFromData(key, fromId, toId, cps);
            }

            // Also create roads from Connections without explicit road entries
            foreach (var kvp in zone.Nodes)
            {
                foreach (var conn in kvp.Value.Connections)
                {
                    string key = kvp.Key + "-" + conn;
                    string reverseKey = conn + "-" + kvp.Key;
                    if (_roads.RoadCPs.ContainsKey(key) || _roads.RoadCPs.ContainsKey(reverseKey)) continue;
                    if (!zone.Nodes.ContainsKey(conn)) continue;

                    Vector3 posA = GetNodeWorldPos(kvp.Key);
                    Vector3 posB = GetNodeWorldPos(conn);
                    _roads.AddRoadFromData(key, kvp.Key, conn, new List<Vector3> { (posA + posB) / 2f });
                }
            }

            _roads.RebuildAllCPHandles();

            _vp.ResetView(5.4f);
            _dragNodeId = null;
            _dragCPRoadKey = null;
            _connectFirstId = null;

            Plugin.Log.LogInfo($"[MapViewport] Spawned map for '{zone.ZoneId}': {_nodeGOs.Count} nodes, {_roads.RoadCPs.Count} roads");
        }

        private void CreateNodeDot(string nodeId, float localX, float localY, ZoneDef zone)
        {
            var go = new GameObject(nodeId);
            go.transform.SetParent(_nodesContainer, false);
            go.transform.localPosition = new Vector3(localX, localY, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = EnsureNodeDot();
            sr.sortingOrder = 10;

            // Color by type
            string prefix = zone.IdPrefix;
            if (nodeId == $"{prefix}_0")
                sr.color = NodeEntranceColor;
            else if (nodeId == $"{prefix}_1")
                sr.color = NodeTownColor;
            else
                sr.color = NodeColor;

            _nodeGOs[nodeId] = go;
        }

        public void DestroyPreview()
        {
            _roads?.ClearAll();
            _roads = null;

            if (_previewRoot != null)
            {
                Object.Destroy(_previewRoot);
                _previewRoot = null;
            }
            _bgGO = null;
            _nodesContainer = null;
            _nodeGOs.Clear();
            _loadedZoneId = null;
            _dragNodeId = null;
            _dragCPRoadKey = null;
            _connectFirstId = null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private Vector3 GetNodeWorldPos(string nodeId)
        {
            if (_nodeGOs.TryGetValue(nodeId, out var go) && go != null)
                return go.transform.position;
            return PreviewOrigin;
        }

        // ═══════════════════════════════════════════════════════════════
        //  PROCEDURAL SPRITES
        // ═══════════════════════════════════════════════════════════════

        private static Sprite EnsureNodeDot()
        {
            if (_nodeDotSprite != null) return _nodeDotSprite;
            _nodeDotSprite = MakeCircleSprite(24, Color.white, NodeDotRadius);
            return _nodeDotSprite;
        }

        private static Sprite MakeCircleSprite(int size, Color color, float worldRadius)
        {
            var tex = new Texture2D(size, size);
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist <= c - 0.5f)
                    tex.SetPixel(x, y, color);
                else if (dist <= c + 0.5f)
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, 0.5f));
                else
                    tex.SetPixel(x, y, Color.clear);
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;

            float ppu = size / (worldRadius * 2f);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════

        public void Cleanup()
        {
            DestroyPreview();
            _vp?.Cleanup();
        }
    }
}
