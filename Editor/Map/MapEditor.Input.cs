using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;
using UnknownMod.Editor.Tabs;


namespace UnknownMod.Editor
{
    public partial class MapEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  INPUT HANDLING (gated by active subtab)
        //
        //  Map  tab → zoom/pan + layer drag/select (visual layers)
        //  Node tab → zoom/pan + node drag/add/delete + right-click
        //  Road tab → zoom/pan + connect mode + waypoint drag/insert/delete
        // ═══════════════════════════════════════════════════════════════

        private void HandleInput(Rect vp, Rect drawn, ZoneDef zone)
        {
            Event e = Event.current;
            Vector2 mp = e.mousePosition;
            if (!vp.Contains(mp) && _dragNodeId == null && _dragWPRoadKey == null && _dragLayerName == null && _scaleLayerName == null) return;

            var subTab = _parent?.ZoneTab?.ActiveZoneInnerTab ?? ZoneTabEditor.ZoneInnerTab.Map;

            // Update hover state (only for relevant subtabs)
            if (subTab == ZoneTabEditor.ZoneInnerTab.Node || subTab == ZoneTabEditor.ZoneInnerTab.Road)
                UpdateHover(mp, drawn, subTab);
            else if (subTab == ZoneTabEditor.ZoneInnerTab.Map)
                UpdateLayerHover(mp, drawn);

            //  Zoom / Pan (scroll + right-drag) — always available
            if (_vp.HandleZoomPan(vp, drawn, 0.3f, 0.5f, 20f))
                return;

            // ── Map subtab input (layer drag/select/scale) ────────
            if (subTab == ZoneTabEditor.ZoneInnerTab.Map)
            {
                // Scale handle drag (in progress)
                if (_scaleLayerName != null)
                {
                    if (e.type == EventType.MouseDrag && e.button == 0)
                    {
                        HandleScaleDrag(mp, drawn, zone);
                        e.Use(); return;
                    }
                    if (e.type == EventType.MouseUp && e.button == 0)
                    {
                        CommitLayerScale(_scaleLayerName, zone);
                        _scaleLayerName = null;
                        _scaleHandleIndex = -1;
                        e.Use(); return;
                    }
                }

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    // Check scale handles first (on the selected layer)
                    if (_selectedLayerName != null)
                    {
                        int hit = HitTestScaleHandle(mp, drawn, _selectedLayerName);
                        if (hit >= 0)
                        {
                            StartScaleDrag(_selectedLayerName, hit, mp, drawn);
                            e.Use(); return;
                        }
                    }

                    if (_hoveredLayerName != null)
                    {
                        StartDragLayer(_hoveredLayerName, mp, drawn);
                        e.Use(); return;
                    }
                    _selectedLayerName = null;
                }

                // Tab = cycle through overlapping layers and select in panel
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab && _layerOverlapCandidates.Count > 1)
                {
                    _layerOverlapCycleIndex = (_layerOverlapCycleIndex + 1) % _layerOverlapCandidates.Count;
                    string name = _layerOverlapCandidates[_layerOverlapCycleIndex];
                    _hoveredLayerName = name;
                    _selectedLayerName = name;
                    for (int i = 0; i < _activeLayers.Count; i++)
                    {
                        if (_activeLayers[i].Name == name)
                        {
                            _expandedLayerIdx = i;
                            _layerRenameBuffer = name;
                            break;
                        }
                    }
                    e.Use(); return;
                }

                // Layer drag (position)
                if (_dragLayerName != null)
                {
                    if (e.type == EventType.MouseDrag && e.button == 0)
                    {
                        Vector2 worldPos = _vp.ViewportToWorld(mp, drawn);
                        Vector2 delta = worldPos - _dragLayerStartWorld;
                        Vector3 newPos = _dragLayerStartPos + new Vector3(delta.x, delta.y, 0f);
                        if (_layerGOs.TryGetValue(_dragLayerName, out var lgo) && lgo != null)
                            lgo.transform.localPosition = newPos;
                        e.Use(); return;
                    }
                    if (e.type == EventType.MouseUp && e.button == 0)
                    {
                        CommitLayerPosition(_dragLayerName, zone);
                        _dragLayerName = null;
                        e.Use(); return;
                    }
                }
            }

            // ── Node subtab input ────────────────────────────────
            if (subTab == ZoneTabEditor.ZoneInnerTab.Node)
            {
                // Right-click without drag = inspect hovered node
                if (e.type == EventType.MouseUp && e.button == 1 && _hoveredNodeId != null)
                {
                    _parent?.InspectNode(_hoveredNodeId);
                    e.Use(); return;
                }

                bool ctrl = e.control;

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (_hoveredNodeId != null)
                    {
                        StartDragNode(_hoveredNodeId, mp, drawn);
                        e.Use(); return;
                    }
                    if (ctrl)
                    {
                        Vector2 worldPos = _vp.ViewportToWorld(mp, drawn);
                        Vector3 local = new Vector3(worldPos.x, worldPos.y, 0f) - PreviewOrigin
                            - new Vector3(zone.NodesOffsetX, zone.NodesOffsetY, 0f);
                        AddNode(local.x, local.y, zone);
                        e.Use(); return;
                    }
                    _parent.SelectedNodeId = null;
                }

                // Node drag
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
                        // Also commit connected road waypoints so endpoints stay in sync
                        if (_roads != null && _roads.NodeToRoads.TryGetValue(_dragNodeId, out var connRoads))
                        {
                            foreach (string rk in connRoads)
                                CommitRoadWaypoints(rk, zone);
                        }
                        _dragNodeId = null;
                        e.Use(); return;
                    }
                }

                // Backspace = delete node
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Backspace && _hoveredNodeId != null)
                {
                    DeleteNode(_hoveredNodeId, zone);
                    e.Use(); return;
                }
            }

            // ── Road subtab input ────────────────────────────────
            if (subTab == ZoneTabEditor.ZoneInnerTab.Road)
            {
                bool shift = e.shift;
                bool ctrl = e.control;

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    // Waypoint handles first
                    if (_hoveredWPRoadKey != null && !shift)
                    {
                        if (ctrl)
                        {
                            InsertWaypointAfter(_hoveredWPRoadKey, _hoveredWPIndex);
                            e.Use(); return;
                        }
                        else
                        {
                            StartDragWaypoint(_hoveredWPRoadKey, _hoveredWPIndex, mp, drawn);
                            e.Use(); return;
                        }
                    }

                    // Shift+click node = connect/disconnect
                    if (_hoveredNodeId != null && shift)
                    {
                        HandleConnectClick(_hoveredNodeId, zone);
                        e.Use(); return;
                    }

                    // Click node without shift = select it (for road context)
                    if (_hoveredNodeId != null)
                    {
                        _parent.SelectedNodeId = _hoveredNodeId;
                        e.Use(); return;
                    }

                    _connectFirstId = null;
                }

                // Waypoint drag (with snap-to-node)
                if (_dragWPRoadKey != null)
                {
                    if (e.type == EventType.MouseDrag && e.button == 0)
                    {
                        Vector2 worldPos = _vp.ViewportToWorld(mp, drawn);
                        Vector2 delta = worldPos - _dragWPStartWorld;
                        Vector3 newPos = _dragWPStartPos + new Vector3(delta.x, delta.y, 0f);

                        // Snap-to-node: only for start/end waypoints
                        _snapActive = false;
                        _snapNodeId = null;
                        bool isEndpoint = _dragWPIndex == 0
                            || (_roads.RoadWaypoints.TryGetValue(_dragWPRoadKey, out var dragWps)
                                && _dragWPIndex == dragWps.Count - 1);
                        if (isEndpoint)
                        {
                            float bestSnapDist = SnapRadiusPx;
                            foreach (var kvp in _nodeGOs)
                            {
                                if (kvp.Value == null) continue;
                                Vector2 nodeSP = _vp.WorldToViewport(kvp.Value.transform.position, drawn);
                                float dist = Vector2.Distance(nodeSP, mp);
                                if (dist < bestSnapDist)
                                {
                                    bestSnapDist = dist;
                                    _snapActive = true;
                                    _snapNodeId = kvp.Key;
                                    _snapNodePos = kvp.Value.transform.position;
                                }
                            }
                        }

                        if (_snapActive)
                            newPos = _snapNodePos;

                        _roads.UpdateWaypointPosition(_dragWPRoadKey, _dragWPIndex, newPos);
                        e.Use(); return;
                    }
                    if (e.type == EventType.MouseUp && e.button == 0)
                    {
                        // Final snap commit
                        if (_snapActive)
                            _roads.UpdateWaypointPosition(_dragWPRoadKey, _dragWPIndex, _snapNodePos);
                        _snapActive = false;
                        _snapNodeId = null;

                        CommitRoadWaypoints(_dragWPRoadKey, zone);
                        _dragWPRoadKey = null;
                        _dragWPIndex = -1;
                        e.Use(); return;
                    }
                }

                // Keyboard
                if (e.type == EventType.KeyDown)
                {
                    // Tab = cycle through overlapping waypoint handles
                    if (e.keyCode == KeyCode.Tab && _overlapCandidates.Count > 1)
                    {
                        _overlapCycleIndex = (_overlapCycleIndex + 1) % _overlapCandidates.Count;
                        var pick = _overlapCandidates[_overlapCycleIndex];
                        _hoveredWPRoadKey = pick.roadKey;
                        _hoveredWPIndex = pick.index;
                        e.Use(); return;
                    }
                    if (e.keyCode == KeyCode.Backspace && _hoveredWPRoadKey != null)
                    {
                        DeleteWaypoint(_hoveredWPRoadKey, _hoveredWPIndex, zone);
                        e.Use(); return;
                    }
                    if (e.keyCode == KeyCode.Escape && _connectFirstId != null)
                    {
                        _connectFirstId = null;
                        e.Use(); return;
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HOVER / PICK
        // ═══════════════════════════════════════════════════════════════

        private void UpdateHover(Vector2 mousePos, Rect drawn, ZoneTabEditor.ZoneInnerTab subTab)
        {
            _hoveredNodeId = null;
            _hoveredWPRoadKey = null;
            _hoveredWPIndex = -1;

            if (!drawn.Contains(mousePos)) return;

            // Check waypoint handles first (smaller, on top) — Road tab only
            if (subTab == ZoneTabEditor.ZoneInnerTab.Road && _showWaypoints && _roads != null)
            {
                // Build list of ALL candidates within pick radius (for overlap cycling)
                _overlapCandidates.Clear();
                foreach (var kvp in _roads.WaypointHandleGOs)
                {
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        var go = kvp.Value[i];
                        if (go == null) continue;
                        Vector2 sp = _vp.WorldToViewport(go.transform.position, drawn);
                        float dist = Vector2.Distance(sp, mousePos);
                        if (dist < WPPickRadiusPx)
                            _overlapCandidates.Add((kvp.Key, i));
                    }
                }

                if (_overlapCandidates.Count > 0)
                {
                    // Clamp cycle index
                    if (_overlapCycleIndex >= _overlapCandidates.Count)
                        _overlapCycleIndex = 0;

                    var pick = _overlapCandidates[_overlapCycleIndex];
                    _hoveredWPRoadKey = pick.roadKey;
                    _hoveredWPIndex = pick.index;
                    return;
                }
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
        //  LAYER HOVER / PICK (Map subtab)
        // ═══════════════════════════════════════════════════════════════

        private void UpdateLayerHover(Vector2 mousePos, Rect drawn)
        {
            _hoveredLayerName = null;
            if (!drawn.Contains(mousePos)) return;

            Vector2 worldPos = _vp.ViewportToWorld(mousePos, drawn);

            // Build list of all layers under the cursor (for Tab cycling)
            _layerOverlapCandidates.Clear();

            // Collect all candidate layers, sorted by SortingOrder descending
            var candidates = new List<(string name, int order)>();
            foreach (var layer in _activeLayers)
            {
                if (!_layerGOs.TryGetValue(layer.Name, out var go) || go == null) continue;
                if (!go.activeSelf) continue;

                Bounds? bounds = GetLayerWorldBounds(layer, go);
                if (bounds == null) continue;

                if (bounds.Value.Contains(new Vector3(worldPos.x, worldPos.y, bounds.Value.center.z)))
                    candidates.Add((layer.Name, layer.SortingOrder));
            }

            // Sort by SortingOrder descending (topmost first)
            candidates.Sort((a, b) => b.order.CompareTo(a.order));
            foreach (var c in candidates)
                _layerOverlapCandidates.Add(c.name);

            if (_layerOverlapCandidates.Count > 0)
            {
                if (_layerOverlapCycleIndex >= _layerOverlapCandidates.Count)
                    _layerOverlapCycleIndex = 0;
                _hoveredLayerName = _layerOverlapCandidates[_layerOverlapCycleIndex];
            }
        }

        /// <summary>Get world-space bounds for a layer's renderable component.</summary>
        private static Bounds? GetLayerWorldBounds(VisualLayerDef layer, GameObject go)
        {
            switch (layer.Type)
            {
                case VisualLayerType.Sprite:
                {
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sprite != null) return sr.bounds;
                    break;
                }
                case VisualLayerType.SpriteMask:
                {
                    var sm = go.GetComponent<SpriteMask>();
                    if (sm != null && sm.sprite != null) return sm.bounds;
                    break;
                }
                case VisualLayerType.Light:
                {
                    // Point-pickable via origin dot only — no bounding box
                    var light = go.GetComponent<Light2D>();
                    if (light != null)
                    {
                        float r = layer.LightType == 3 ? layer.PointLightOuterRadius
                            : (layer.LightType == 4 ? 2f : 1f);
                        var pos = go.transform.position;
                        return new Bounds(pos, new Vector3(r * 2, r * 2, 0.1f));
                    }
                    break;
                }
                case VisualLayerType.ParticleSystem:
                {
                    // Point-pickable via origin dot only — no bounding box
                    var pos = go.transform.position;
                    return new Bounds(pos, new Vector3(1f, 1f, 0.1f));
                }
                case VisualLayerType.Container:
                {
                    // Point-pickable via origin dot only — no bounding box
                    var pos = go.transform.position;
                    bool isBuiltin = layer.Name == "[Nodes]" || layer.Name == "[Roads]";
                    float size = isBuiltin ? 8f : 1f;
                    return new Bounds(pos, new Vector3(size, size, 0.1f));
                }
                case VisualLayerType.Shader:
                {
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sprite != null) return sr.bounds;
                    break;
                }
                case VisualLayerType.PrefabEffect:
                {
                    var pos = go.transform.position;
                    return new Bounds(pos, new Vector3(1f, 1f, 0.1f));
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYER SCALE HANDLES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Test if mouse is over one of the 8 scale handles. Returns handle index (0-7) or -1.</summary>
        private int HitTestScaleHandle(Vector2 mousePos, Rect drawn, string layerName)
        {
            var positions = GetScaleHandlePositions(drawn, layerName);
            if (positions == null) return -1;
            float hitRadius = ScaleHandleSizePx + 3f;
            for (int i = 0; i < 8; i++)
            {
                if (Vector2.Distance(mousePos, positions[i]) < hitRadius)
                    return i;
            }
            return -1;
        }

        /// <summary>Begin a scale handle drag.</summary>
        private void StartScaleDrag(string layerName, int handleIndex, Vector2 mousePos, Rect drawn)
        {
            _scaleLayerName = layerName;
            _scaleHandleIndex = handleIndex;
            _scaleStartWorld = _vp.ViewportToWorld(mousePos, drawn);

            var layer = _activeLayers.Find(l => l.Name == layerName);
            if (layer != null)
            {
                _scaleStartScaleX = layer.ScaleX;
                _scaleStartScaleY = layer.ScaleY;
                _scaleStartPosX = layer.PosX;
                _scaleStartPosY = layer.PosY;
            }

            if (_layerGOs.TryGetValue(layerName, out var go) && go != null)
            {
                var b = GetLayerWorldBounds(layer, go);
                _scaleStartBounds = b ?? new Bounds(go.transform.position, Vector3.one);
            }
        }

        /// <summary>Process mouse drag for an active scale handle.
        /// Handles snap to camera bounding box edges.</summary>
        private void HandleScaleDrag(Vector2 mousePos, Rect drawn, ZoneDef zone)
        {
            var layer = _activeLayers.Find(l => l.Name == _scaleLayerName);
            if (layer == null) return;
            if (!_layerGOs.TryGetValue(_scaleLayerName, out var go) || go == null) return;

            Vector2 worldMouse = _vp.ViewportToWorld(mousePos, drawn);

            // Camera bounds edges for snapping
            float camHalfH = GameOrthoSize;
            float camHalfW = GameOrthoSize * GameAspect;
            float camL = PreviewOrigin.x - camHalfW;
            float camR = PreviewOrigin.x + camHalfW;
            float camT = PreviewOrigin.y + camHalfH;
            float camB = PreviewOrigin.y - camHalfH;
            const float snapThreshWorld = 0.15f; // world-space snap distance

            // Snap helper: snaps a value to a target if within threshold
            float Snap(float val, float target) =>
                Mathf.Abs(val - target) < snapThreshWorld ? target : val;

            // Compute how the bounding box edges move based on handle index
            // 0=TL, 1=T, 2=TR, 3=R, 4=BR, 5=B, 6=BL, 7=L
            Bounds sb = _scaleStartBounds;
            float origMinX = sb.min.x, origMaxX = sb.max.x;
            float origMinY = sb.min.y, origMaxY = sb.max.y;
            float origW = origMaxX - origMinX;
            float origH = origMaxY - origMinY;

            // Current target edges (start from original)
            float newMinX = origMinX, newMaxX = origMaxX;
            float newMinY = origMinY, newMaxY = origMaxY;

            // Which edges are being moved?
            bool moveLeft   = _scaleHandleIndex == 0 || _scaleHandleIndex == 6 || _scaleHandleIndex == 7;
            bool moveRight  = _scaleHandleIndex == 2 || _scaleHandleIndex == 3 || _scaleHandleIndex == 4;
            bool moveTop    = _scaleHandleIndex == 0 || _scaleHandleIndex == 1 || _scaleHandleIndex == 2;
            bool moveBottom = _scaleHandleIndex == 4 || _scaleHandleIndex == 5 || _scaleHandleIndex == 6;

            Vector2 delta = worldMouse - _scaleStartWorld;

            if (moveLeft)   newMinX = Snap(origMinX + delta.x, camL);
            if (moveRight)  newMaxX = Snap(origMaxX + delta.x, camR);
            if (moveTop)    newMaxY = Snap(origMaxY + delta.y, camT);
            if (moveBottom) newMinY = Snap(origMinY + delta.y, camB);

            // Also snap to opposite camera edges (e.g. left edge snaps to camR too)
            if (moveLeft)   { newMinX = Snap(newMinX, camR); }
            if (moveRight)  { newMaxX = Snap(newMaxX, camL); }
            if (moveTop)    { newMaxY = Snap(newMaxY, camB); }
            if (moveBottom) { newMinY = Snap(newMinY, camT); }

            // Compute new width/height and derive scale ratios
            float newW = newMaxX - newMinX;
            float newH = newMaxY - newMinY;
            if (Mathf.Abs(origW) < 0.001f || Mathf.Abs(origH) < 0.001f) return;

            float scaleRatioX = newW / origW;
            float scaleRatioY = newH / origH;

            // Prevent inversion (min scale)
            if (scaleRatioX < 0.01f) scaleRatioX = 0.01f;
            if (scaleRatioY < 0.01f) scaleRatioY = 0.01f;

            // For edge handles, only change the relevant axis
            float finalScaleX = _scaleStartScaleX;
            float finalScaleY = _scaleStartScaleY;
            if (moveLeft || moveRight) finalScaleX = _scaleStartScaleX * scaleRatioX;
            if (moveTop || moveBottom) finalScaleY = _scaleStartScaleY * scaleRatioY;

            // Compute new position: the anchored edge stays fixed.
            // For each axis, the anchor is the edge NOT being dragged.
            float newPosX = _scaleStartPosX;
            float newPosY = _scaleStartPosY;

            // Position adjustment: if dragging left edge, the right edge is anchored.
            // The centre shifts by half the delta in bounds.
            if (moveLeft && !moveRight)
                newPosX = _scaleStartPosX + (newMinX - origMinX) * 0.5f;
            else if (moveRight && !moveLeft)
                newPosX = _scaleStartPosX + (newMaxX - origMaxX) * 0.5f;
            else if (moveLeft && moveRight)
                newPosX = _scaleStartPosX + ((newMinX - origMinX) + (newMaxX - origMaxX)) * 0.5f;

            if (moveBottom && !moveTop)
                newPosY = _scaleStartPosY + (newMinY - origMinY) * 0.5f;
            else if (moveTop && !moveBottom)
                newPosY = _scaleStartPosY + (newMaxY - origMaxY) * 0.5f;
            else if (moveTop && moveBottom)
                newPosY = _scaleStartPosY + ((newMinY - origMinY) + (newMaxY - origMaxY)) * 0.5f;

            // Apply to layer def and GO
            layer.ScaleX = finalScaleX;
            layer.ScaleY = finalScaleY;
            layer.PosX = newPosX;
            layer.PosY = newPosY;

            go.transform.localScale = new Vector3(finalScaleX, finalScaleY, 1f);
            go.transform.localPosition = new Vector3(newPosX, newPosY, layer.PosZ);
        }

        /// <summary>Commit scale changes to the layer def and mark dirty.</summary>
        private void CommitLayerScale(string layerName, ZoneDef zone)
        {
            var layer = _activeLayers.Find(l => l.Name == layerName);
            if (layer == null) return;
            ZoneEditingService.MarkDirty();
            Plugin.Log.LogInfo($"[MapEditor] Layer '{layerName}' scaled to ({layer.ScaleX:F3}, {layer.ScaleY:F3}) pos ({layer.PosX:F2}, {layer.PosY:F2})");
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYER DRAGGING
        // ═══════════════════════════════════════════════════════════════

        private void StartDragLayer(string layerName, Vector2 mousePos, Rect drawn)
        {
            _dragLayerName = layerName;
            _selectedLayerName = layerName;
            _dragLayerStartWorld = _vp.ViewportToWorld(mousePos, drawn);
            if (_layerGOs.TryGetValue(layerName, out var go) && go != null)
                _dragLayerStartPos = go.transform.localPosition;

            // Expand in panel
            for (int i = 0; i < _activeLayers.Count; i++)
            {
                if (_activeLayers[i].Name == layerName)
                {
                    _expandedLayerIdx = i;
                    _layerRenameBuffer = layerName;
                    break;
                }
            }
        }

        private void CommitLayerPosition(string layerName, ZoneDef zone)
        {
            if (!_layerGOs.TryGetValue(layerName, out var go) || go == null) return;
            var layer = _activeLayers.Find(l => l.Name == layerName);
            if (layer == null) return;

            layer.PosX = go.transform.localPosition.x;
            layer.PosY = go.transform.localPosition.y;
            layer.PosZ = go.transform.localPosition.z;

            // Persist container offsets back to ZoneDef
            if (layer.Name == "[Nodes]")
            {
                zone.NodesOffsetX = layer.PosX;
                zone.NodesOffsetY = layer.PosY;
                _loadedZoneId = null; // rebuild roads
            }
            else if (layer.Name == "[Roads]")
            {
                zone.RoadsOffsetX = layer.PosX;
                zone.RoadsOffsetY = layer.PosY;
            }

            ZoneEditingService.MarkDirty();
            Plugin.Log.LogInfo($"[MapEditor] Layer '{layerName}' → ({layer.PosX:F2}, {layer.PosY:F2})");
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
            Plugin.Log.LogInfo($"[MapEditor] Node '{nodeId}' → ({nd.PosX:F1}, {nd.PosY:F1})");
        }

        // ═══════════════════════════════════════════════════════════════
        //  WAYPOINT DRAGGING
        // ═══════════════════════════════════════════════════════════════

        private void StartDragWaypoint(string roadKey, int wpIndex, Vector2 mousePos, Rect drawn)
        {
            _dragWPRoadKey = roadKey;
            _dragWPIndex = wpIndex;
            _dragWPStartWorld = _vp.ViewportToWorld(mousePos, drawn);
            if (_roads.RoadWaypoints.TryGetValue(roadKey, out var wps) && wpIndex < wps.Count)
                _dragWPStartPos = wps[wpIndex];
        }

        private void CommitRoadWaypoints(string roadKey, ZoneDef zone)
        {
            if (!_roads.RoadWaypoints.TryGetValue(roadKey, out var wps)) return;
            RoadEditor.ParseRoadKey(roadKey, out var fromId, out var toId);
            if (fromId == null) return;

            if (!zone.Roads.TryGetValue(roadKey, out var rd))
            {
                rd = new RoadDef { FromNodeId = fromId, ToNodeId = toId };
                zone.Roads[roadKey] = rd;
            }
            rd.Waypoints.Clear();
            foreach (var wp in wps)
                rd.Waypoints.Add(new float[] {
                    wp.x - PreviewOrigin.x - zone.NodesOffsetX,
                    wp.y - PreviewOrigin.y - zone.NodesOffsetY });
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

            if (_roads.RoadWaypoints.ContainsKey(key))
            {
                RemoveRoad(key, zone);
                Plugin.Log.LogInfo($"[MapEditor] Removed road '{key}'");
            }
            else if (_roads.RoadWaypoints.ContainsKey(reverseKey))
            {
                RemoveRoad(reverseKey, zone);
                Plugin.Log.LogInfo($"[MapEditor] Removed road '{reverseKey}'");
            }
            else
            {
                CreateRoad(from, to, zone);
                Plugin.Log.LogInfo($"[MapEditor] Created road '{key}'");
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
            if (zone.Nodes.TryGetValue(toId, out var toNode) && !toNode.Connections.Contains(fromId))
                toNode.Connections.Add(fromId);

            // Save all waypoints (including endpoints) to the zone def
            var rd = new RoadDef { FromNodeId = fromId, ToNodeId = toId };
            foreach (var wp in _roads.RoadWaypoints[key])
                rd.Waypoints.Add(new float[] {
                    wp.x - PreviewOrigin.x - zone.NodesOffsetX,
                    wp.y - PreviewOrigin.y - zone.NodesOffsetY });
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
            if (toId != null && zone.Nodes.TryGetValue(toId, out var toNode))
                toNode.Connections.Remove(fromId);
            ZoneEditingService.MarkDirty();
        }

        // ═══════════════════════════════════════════════════════════════
        //  WAYPOINT INSERT / DELETE
        // ═══════════════════════════════════════════════════════════════

        private void InsertWaypointAfter(string roadKey, int afterIndex)
        {
            _roads.InsertWaypointAfter(roadKey, afterIndex);
            CommitRoadWaypoints(roadKey, ZoneEditingService.CurrentZone);
        }

        private void DeleteWaypoint(string roadKey, int wpIndex, ZoneDef zone)
        {
            bool roadRemoved = _roads.DeleteWaypoint(roadKey, wpIndex);
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
                CommitRoadWaypoints(roadKey, zone);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  NODE ADD / DELETE
        // ═══════════════════════════════════════════════════════════════

        private void AddNode(float localX, float localY, ZoneDef zone)
        {
            string nodeId = ZoneEditingService.AddNode(localX, localY);
            if (nodeId == null) return;

            CreateNodeVisual(nodeId, localX, localY, zone);
            Plugin.Log.LogInfo($"[MapEditor] Added node '{nodeId}' at ({localX:F1}, {localY:F1})");
        }

        private void DeleteNode(string nodeId, ZoneDef zone)
        {
            string prefix = zone.IdPrefix;
            if (nodeId == $"{prefix}_0" || nodeId == $"{prefix}_1")
            {
                Plugin.Log.LogWarning($"[MapEditor] Cannot delete entrance/town node '{nodeId}'.");
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
            Plugin.Log.LogInfo($"[MapEditor] Deleted node '{nodeId}'");
        }
    }
}
