using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;


namespace UnknownMod.Editor
{
    public partial class MapViewport
    {
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  VIEWPORT (drawn on left side of screen by ModEditor)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  OVERLAYS (node labels, hover highlights, connection line)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  INPUT HANDLING
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private void HandleInput(Rect vp, Rect drawn, ZoneDef zone)
        {
            Event e = Event.current;
            Vector2 mp = e.mousePosition;
            if (!vp.Contains(mp) && _dragNodeId == null && _dragCPRoadKey == null) return;

            // Update hover state
            UpdateHover(mp, drawn);

            // â”€â”€ Zoom / Pan (scroll + right-drag) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

            // â”€â”€ Left click â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

            // â”€â”€ Drag (node) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

            // â”€â”€ Drag (CP) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

            // â”€â”€ Keyboard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  HOVER / PICK
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  NODE DRAGGING
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
            Plugin.Log.LogInfo($"[MapViewport] Node '{nodeId}' â†’ ({nd.PosX:F1}, {nd.PosY:F1})");
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  CP DRAGGING
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
                rd.Waypoints.Add(new float[] {
                    cp.x - PreviewOrigin.x - zone.NodesOffsetX,
                    cp.y - PreviewOrigin.y - zone.NodesOffsetY });
            ZoneEditingService.MarkDirty();
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  CONNECTION MODE
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  ROAD CREATION / REMOVAL
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private void CreateRoad(string fromId, string toId, ZoneDef zone)
        {
            string key = _roads.CreateRoad(fromId, toId);

            // Write to DTO
            if (!zone.Nodes.TryGetValue(fromId, out var fromNode)) return;
            if (!fromNode.Connections.Contains(toId))
                fromNode.Connections.Add(toId);

            // Save all CPs (which now include endpoints) to the zone def
            var rd = new RoadDef { FromNodeId = fromId, ToNodeId = toId };
            foreach (var cp in _roads.RoadCPs[key])
                rd.Waypoints.Add(new float[] {
                    cp.x - PreviewOrigin.x - zone.NodesOffsetX,
                    cp.y - PreviewOrigin.y - zone.NodesOffsetY });
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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  CP INSERT / DELETE
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
                // Road was completely removed â€” update DTO
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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  NODE ADD / DELETE
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private void AddNode(float localX, float localY, ZoneDef zone)
        {
            string nodeId = ZoneEditingService.AddNode(localX, localY);
            if (nodeId == null) return;

            CreateNodeVisual(nodeId, localX, localY, zone);
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
                _nodePlainSRs.Remove(nodeId);
            }

            ZoneEditingService.DeleteNode(nodeId);
            Plugin.Log.LogInfo($"[MapViewport] Deleted node '{nodeId}'");
        }

    }
}
