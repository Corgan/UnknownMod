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
        //  PANEL (right-side, drawn inside ModEditor's IMGUI area)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
                Plugin.Log.LogInfo("[MapViewport] Reflow complete â€” viewport will rebuild.");
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Rebuild Viewport", EditorStyles.MiniButton))
                _loadedZoneId = null;

            // â”€â”€ Visual Layers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            GUILayout.Space(8);
            EditorStyles.Separator();
            DrawLayersPanel(zone);

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

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  SPAWN MAP (build preview scene from ZoneDef)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private void SpawnMap(ZoneDef zone)
        {
            DestroyPreview();
            _loadedZoneId = zone.ZoneId;

            // Root
            _previewRoot = new GameObject($"[MapViewport] {zone.ZoneId}");
            _previewRoot.transform.position = PreviewOrigin;
            Object.DontDestroyOnLoad(_previewRoot);

            // Visual layers container (everything except nodes/roads)
            var layersGO = new GameObject("Layers");
            layersGO.transform.SetParent(_previewRoot.transform, false);
            _layersContainer = layersGO.transform;
            _layerGOs.Clear();
            _activeLayers.Clear();

            // Resolve visual layers: merge base-game layers with zone overrides
            SpawnVisualLayers(zone);

            // Nodes container
            var nodesGO = new GameObject("Nodes");
            nodesGO.transform.SetParent(_previewRoot.transform, false);
            nodesGO.transform.localPosition = new Vector3(zone.NodesOffsetX, zone.NodesOffsetY, -2f);
            _nodesContainer = nodesGO.transform;

            // Roads + CP containers
            var roadsGO = new GameObject("Roads");
            roadsGO.transform.SetParent(_previewRoot.transform, false);
            _roadsContainer = roadsGO.transform;
            var cpGO = new GameObject("CPHandles");
            cpGO.transform.SetParent(_previewRoot.transform, false);
            cpGO.SetActive(_showCPs);

            // Register Nodes container as a layer entry so its offset can be adjusted
            var nodesLayerDef = new VisualLayerDef
            {
                Name = "[Nodes]",
                Type = VisualLayerType.Container,
                PosX = zone.NodesOffsetX,
                PosY = zone.NodesOffsetY,
                PosZ = -2f,
                Visible = true,
            };
            _activeLayers.Add(nodesLayerDef);
            _layerGOs["[Nodes]"] = nodesGO;

            // Initialize road material + RoadEditor
            // Try to use the game's arrowSquares material for authentic road appearance
            var roadMat = FindArrowSquaresMaterial() ?? new Material(Shader.Find("Sprites/Default"));
            _roads = new RoadEditor(new RoadEditor.Config
            {
                RoadColor = RoadColor,
                RoadWidth = 0.1f,
                RoadSortingOrder = 1,
                SortingLayer = "Map",
                UseWorldSpace = true,
                CPColor = CPHandleColor,
                CPSortingOrder = 50,
                CPZ = PreviewOrigin.z - 1f,
            });
            _roads.Init(roadsGO.transform, cpGO.transform, roadMat, GetNodeWorldPos);

            // Create node visuals
            foreach (var kvp in zone.Nodes)
                CreateNodeVisual(kvp.Key, kvp.Value.PosX, kvp.Value.PosY, zone);

            // Create roads from explicit Road entries
            // Waypoints now contain ALL road points (including endpoints near nodes)
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
                        cps.Add(new Vector3(
                            wp[0] + PreviewOrigin.x + zone.NodesOffsetX,
                            wp[1] + PreviewOrigin.y + zone.NodesOffsetY, 0f));
                }
                if (cps.Count < 2)
                {
                    // Fallback: if somehow no points, create endpoints at nodes
                    cps.Clear();
                    cps.Add(GetNodeWorldPos(fromId));
                    cps.Add(GetNodeWorldPos(toId));
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

                    // No explicit road data â€” create a straight line between nodes
                    Vector3 posA = GetNodeWorldPos(kvp.Key);
                    Vector3 posB = GetNodeWorldPos(conn);
                    _roads.AddRoadFromData(key, kvp.Key, conn, new List<Vector3> { posA, posB });
                }
            }

            _roads.RebuildAllCPHandles();

            _vp.ResetView(5.4f);
            _dragNodeId = null;
            _dragCPRoadKey = null;
            _connectFirstId = null;

            Plugin.Log.LogInfo($"[MapViewport] Spawned map for '{zone.ZoneId}': {_nodeGOs.Count} nodes, {_roads.RoadCPs.Count} roads, {_activeLayers.Count} layers");
        }

    }
}
