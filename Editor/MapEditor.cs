using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Interactive map editor for modded zones.
    /// Attach to the zone root GameObject.
    /// Operates as a sub-component managed by ZoneEditor.
    /// Road data and visuals are delegated to RoadEditor.
    ///
    /// In edit mode (all normal game interaction is blocked):
    ///   Left-drag node          Move node (roads auto-update)
    ///   Shift+click node A,B    Toggle connection between two nodes
    ///   Ctrl+click empty space   Add new node at mouse position
    ///   Backspace (hover node)    Delete node and its connections
    ///   Left-drag control point Reshape road curve
    ///   Ctrl+click on road CP   Insert new control point after this one
    ///   Backspace (hover CP)    Remove control point (last CP removes entire road)
    ///   Escape                  Cancel connection mode
    ///   Right-click node        Inspect node in NodeEditor panel
    /// </summary>
    public class MapEditor : MonoBehaviour
    {
        // ── Public state (now driven by ZoneEditor) ──────────────────
        public static bool IsEditing => ZoneEditor.IsEditing;

        // ── Road data + visuals (delegated to RoadEditor) ────────────
        private RoadEditor _roads;

        // ── Scene references ─────────────────────────────────────────
        private Transform nodesContainer;
        private Transform _cpContainer; // kept for HitTest iteration

        // ── State ────────────────────────────────────────────────────
        private string connectFirstId;
        private Transform dragTarget;
        private Vector3 dragOffset;
        private string dragNodeId;      // non-null when dragging a node
        private string dragCPRoadKey;   // non-null when dragging a control point
        private int dragCPIndex = -1;

        // ── Resources ────────────────────────────────────────────────
        private string sortingLayer = "Default";

        // ── Colors ───────────────────────────────────────────────────
        private static readonly Color EditRoadColor = new Color(0f, 1f, 1f, 0.8f); // cyan

        // ═══════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        void Awake()
        {
            nodesContainer = transform.Find("Nodes");
            var roadsContainer = transform.Find("Roads");

            _cpContainer = new GameObject("_EditorCPs").transform;
            _cpContainer.SetParent(transform, false);
            _cpContainer.gameObject.SetActive(false);

            // Sorting layer from first node SR
            if (nodesContainer != null && nodesContainer.childCount > 0)
            {
                var sr = nodesContainer.GetChild(0).GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sortingLayer = sr.sortingLayerName;
            }

            var roadMat = new Material(Shader.Find("Sprites/Default"));

            _roads = new RoadEditor(new RoadEditor.Config
            {
                RoadColor = EditRoadColor,
                RoadWidth = 0.06f,
                RoadSortingOrder = -5,
                SortingLayer = sortingLayer,
                UseWorldSpace = false,
                CPColor = new Color(1f, 0.9f, 0f, 1f),
                CPSortingOrder = 100,
                CPZ = -3f,
            });
            _roads.Init(roadsContainer, _cpContainer, roadMat, GetNodeWorldPos,
                        v => roadsContainer.InverseTransformPoint(v));
            _roads.ImportFromExistingLRs();
        }

        // ═══════════════════════════════════════════════════════════════
        //  HIT TESTING — uses Physics2D for nodes + distance for CPs
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Find what's under the mouse cursor.
        /// Uses distance-based check for CP handles and Physics2D for nodes
        /// (since nodes have 2D colliders from the vanilla prefab).
        /// </summary>
        private (CPHandle cpHandle, Node node) HitTest()
        {
            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 mousePos2D = new Vector2(mouseWorld.x, mouseWorld.y);

            // First check CP handles by distance (more reliable than colliders)
            CPHandle closestCP = null;
            float closestDist = 0.25f; // max pick radius
            if (_cpContainer != null && _cpContainer.gameObject.activeSelf)
            {
                foreach (Transform child in _cpContainer)
                {
                    var cpH = child.GetComponent<CPHandle>();
                    if (cpH == null) continue;
                    float dist = Vector2.Distance(mousePos2D, new Vector2(child.position.x, child.position.y));
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestCP = cpH;
                    }
                }
            }
            if (closestCP != null)
                return (closestCP, null);

            // Then check nodes via Physics2D (they have 2D colliders from the prefab)
            var hit2D = Physics2D.OverlapPoint(mousePos2D);
            if (hit2D != null)
            {
                var node = hit2D.GetComponent<Node>();
                if (node == null)
                    node = hit2D.GetComponentInParent<Node>();
                if (node != null)
                    return (null, node);
            }

            return (null, null);
        }

        // ═══════════════════════════════════════════════════════════════
        //  UPDATE — input handling
        // ═══════════════════════════════════════════════════════════════

        void Update()
        {
            if (!IsEditing) return;

            // Skip world-space input when mouse is over editor GUI
            if (ZoneEditor.IsMouseOverUI)
            {
                // Still allow releasing a drag already in progress
                if (Input.GetMouseButtonUp(0) && dragTarget != null)
                {
                    dragTarget = null;
                    dragNodeId = null;
                    dragCPRoadKey = null;
                    dragCPIndex = -1;
                }
                return;
            }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // ── Mouse down ───────────────────────────────────────────
            if (Input.GetMouseButtonDown(0))
            {
                var (cpH, node) = HitTest();

                if (cpH != null && !shift)
                {
                    if (ctrl)
                        _roads.InsertCPAfter(cpH.roadKey, cpH.pointIndex);
                    else
                        StartDragCP(cpH);
                    return;
                }

                if (node != null)
                {
                    string nodeId = node.gameObject.name;
                    if (shift)
                        HandleConnectClick(nodeId);
                    else
                        StartDragNode(node.transform, nodeId);
                    return;
                }

                // Ctrl+click on empty space → add new node
                if (ctrl)
                {
                    Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    float localX = mouseWorld.x;
                    float localY = mouseWorld.y;
                    if (nodesContainer != null)
                    {
                        Vector3 local = nodesContainer.InverseTransformPoint(mouseWorld);
                        localX = local.x;
                        localY = local.y;
                    }
                    AddNodeAtPosition(localX, localY);
                    return;
                }
            }

            // ── Drag ─────────────────────────────────────────────────
            if (Input.GetMouseButton(0) && dragTarget != null)
            {
                Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                mouseWorld.z = dragTarget.position.z;

                if (dragNodeId != null)
                {
                    dragTarget.position = mouseWorld + dragOffset;
                    _roads.UpdateRoadsForNode(dragNodeId);
                }
                else if (dragCPRoadKey != null)
                {
                    Vector3 newPos = mouseWorld + dragOffset;
                    dragTarget.position = newPos;
                    _roads.RoadCPs[dragCPRoadKey][dragCPIndex] = new Vector3(newPos.x, newPos.y, 0f);
                    _roads.RefreshRoadLR(dragCPRoadKey);
                }
            }

            // ── Mouse up ─────────────────────────────────────────────
            if (Input.GetMouseButtonUp(0) && dragTarget != null)
            {
                if (dragNodeId != null)
                {
                    Vector3 lp = dragTarget.localPosition;
                    Plugin.Log.LogInfo($"[MapEditor] Node '{dragNodeId}' → ({lp.x:F1}, {lp.y:F1})");
                }
                dragTarget = null;
                dragNodeId = null;
                dragCPRoadKey = null;
                dragCPIndex = -1;
            }

            // ── Backspace: delete hovered node ──────────────────────
            if (Input.GetKeyDown(KeyCode.Backspace))
                TryDeleteHoveredNode();

            // ── Delete: delete CP (or entire road if last CP) ────────
            if (Input.GetKeyDown(KeyCode.Delete))
                TryDeleteHoveredCP();

            // ── Right-click: inspect node in editor panel ────────────
            if (Input.GetMouseButtonDown(1))
            {
                var (_, node) = HitTest();
                if (node != null && ZoneEditor.Instance != null)
                    ZoneEditor.Instance.InspectNode(node.gameObject.name);
            }

            // ── Escape: cancel connection mode ───────────────────────
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (connectFirstId != null)
                {
                    Plugin.Log.LogInfo("[MapEditor] Connection cancelled.");
                    connectFirstId = null;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  EDIT MODE (driven by ZoneEditor)
        // ═══════════════════════════════════════════════════════════════

        public void SetEditMode(bool active)
        {
            Plugin.Log.LogInfo($"[MapEditor] Edit mode: {(active ? "ON" : "OFF")}");
            _roads.SetEditMode(active, EditRoadColor);
            if (!active) connectFirstId = null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SYNC TO DTO (for saving)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Write current node positions and road waypoints back to the ZoneDef DTO
        /// so they're persisted when saving to JSON.
        /// </summary>
        public void SyncToDto(ZoneDef def)
        {
            if (def == null || nodesContainer == null) return;

            // Sync node positions
            foreach (Transform t in nodesContainer)
            {
                if (def.Nodes.TryGetValue(t.name, out var nodeDef))
                {
                    nodeDef.PosX = t.localPosition.x;
                    nodeDef.PosY = t.localPosition.y;
                }
            }

            // Sync road waypoints
            def.Roads.Clear();
            foreach (var kvp in _roads.RoadCPs)
            {
                string key = kvp.Key;
                RoadEditor.ParseRoadKey(key, out string fromId, out string toId);
                if (fromId == null) continue;

                var road = new RoadDef
                {
                    FromNodeId = fromId,
                    ToNodeId = toId,
                };

                foreach (var wp in kvp.Value)
                    road.Waypoints.Add(new float[] { wp.x, wp.y });

                def.Roads[key] = road;
            }

            // Sync connections
            foreach (var nodeDef in def.Nodes.Values)
                nodeDef.Connections.Clear();

            foreach (var key in _roads.RoadCPs.Keys)
            {
                RoadEditor.ParseRoadKey(key, out string fromId, out string toId);
                if (fromId == null || toId == null) continue;
                if (def.Nodes.TryGetValue(fromId, out var fromNode))
                {
                    if (!fromNode.Connections.Contains(toId))
                        fromNode.Connections.Add(toId);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  MAP TAB PANEL (drawn inside ZoneEditor's IMGUI panel)
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            GUILayout.Label("<b>Map Controls</b>", EditorStyles.RichLabel);
            GUILayout.Space(4);
            GUILayout.Label("Drag nodes to reposition", EditorStyles.RichLabel);
            GUILayout.Label("Shift+click two nodes to connect/disconnect", EditorStyles.RichLabel);
            GUILayout.Label("Ctrl+click empty space to add node", EditorStyles.RichLabel);
            GUILayout.Label("Backspace on hovered node to remove", EditorStyles.RichLabel);
            GUILayout.Label("Ctrl+click CP to insert new control point", EditorStyles.RichLabel);
            GUILayout.Label("Delete on CP to delete it", EditorStyles.RichLabel);
            GUILayout.Label("Right-click node to inspect properties", EditorStyles.RichLabel);

            GUILayout.Space(8);
            GUILayout.Label($"<b>Nodes:</b> {(nodesContainer != null ? nodesContainer.childCount : 0)}", EditorStyles.RichLabel);
            GUILayout.Label($"<b>Roads:</b> {_roads.RoadCPs.Count}", EditorStyles.RichLabel);

            if (connectFirstId != null)
            {
                GUILayout.Space(4);
                GUILayout.Label($"<color=yellow>Connecting from: {connectFirstId}</color>", EditorStyles.RichLabel);
                GUILayout.Label("Shift+click another node or Esc to cancel", EditorStyles.RichLabel);
            }

            GUILayout.Space(12);
            EditorStyles.Separator();
            GUILayout.Label("<b>Zone Tools</b>", EditorStyles.RichLabel);
            GUILayout.Space(4);

            if (GUILayout.Button("Reflow Node IDs (BFS)", GUILayout.Height(28)))
            {
                ZoneLoader.ReflowNodeIds();
                if (ZoneLoader.CurrentZone != null && ZoneEditor.Instance != null)
                    Plugin.Log.LogInfo("[MapEditor] Run Reflow — reload zone to see updated IDs.");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  NODE DRAGGING
        // ═══════════════════════════════════════════════════════════════

        private void StartDragNode(Transform nodeTrans, string nodeId)
        {
            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            dragTarget = nodeTrans;
            dragNodeId = nodeId;
            dragOffset = nodeTrans.position - new Vector3(mouseWorld.x, mouseWorld.y, nodeTrans.position.z);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CONNECTION MODE
        // ═══════════════════════════════════════════════════════════════

        private void HandleConnectClick(string nodeId)
        {
            if (connectFirstId == null)
            {
                connectFirstId = nodeId;
                Plugin.Log.LogInfo($"[MapEditor] Connect: first node = '{nodeId}'. Shift+click another node (or Esc to cancel).");
            }
            else
            {
                if (connectFirstId == nodeId)
                {
                    Plugin.Log.LogInfo("[MapEditor] Same node — cancelled.");
                    connectFirstId = null;
                    return;
                }

                string from = connectFirstId;
                string to = nodeId;
                string key = from + "-" + to;
                string reverseKey = to + "-" + from;

                if (_roads.RoadCPs.ContainsKey(key))
                {
                    _roads.RemoveRoad(key);
                    Plugin.Log.LogInfo($"[MapEditor] Removed connection '{key}'");
                }
                else if (_roads.RoadCPs.ContainsKey(reverseKey))
                {
                    _roads.RemoveRoad(reverseKey);
                    Plugin.Log.LogInfo($"[MapEditor] Removed connection '{reverseKey}'");
                }
                else
                {
                    _roads.CreateRoad(from, to);
                    Plugin.Log.LogInfo($"[MapEditor] Created connection '{key}'");
                }
                connectFirstId = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CONTROL POINT DRAGGING
        // ═══════════════════════════════════════════════════════════════

        private void StartDragCP(CPHandle cpH)
        {
            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            dragTarget = cpH.transform;
            dragCPRoadKey = cpH.roadKey;
            dragCPIndex = cpH.pointIndex;
            dragOffset = cpH.transform.position - new Vector3(mouseWorld.x, mouseWorld.y, cpH.transform.position.z);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CONTROL POINT DELETE
        // ═══════════════════════════════════════════════════════════════

        private void TryDeleteHoveredCP()
        {
            var (cpH, _) = HitTest();
            if (cpH == null) return;
            _roads.DeleteCP(cpH.roadKey, cpH.pointIndex);
        }

        // ═══════════════════════════════════════════════════════════════
        //  NODE ADD / DELETE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Add a new node at the given local position and instantiate its GameObject.</summary>
        private void AddNodeAtPosition(float localX, float localY)
        {
            string nodeId = ZoneLoader.AddNode(localX, localY);
            if (nodeId == null) return;

            // Instantiate a node GO from the template
            GameObject template = null;
            if (nodesContainer != null && nodesContainer.childCount > 0)
                template = nodesContainer.GetChild(0).gameObject;

            if (template != null)
            {
                var nodeGO = Object.Instantiate(template, nodesContainer);
                nodeGO.name = nodeId;
                nodeGO.transform.localPosition = new Vector3(localX, localY, 0f);
                nodeGO.transform.localScale = Vector3.one;
                nodeGO.SetActive(true);

                var nodeComp = nodeGO.GetComponent<Node>();
                if (nodeComp != null)
                    nodeComp.nodeData = Globals.Instance.GetNodeData(nodeId);

                Plugin.Log.LogInfo($"[MapEditor] Created node GO '{nodeId}' at ({localX:F1}, {localY:F1})");
            }
        }

        /// <summary>Delete the node under the mouse cursor.</summary>
        private void TryDeleteHoveredNode()
        {
            var (_, node) = HitTest();
            if (node == null) return;

            string nodeId = node.gameObject.name;
            var zone = ZoneLoader.CurrentZone;
            if (zone == null || !zone.Nodes.ContainsKey(nodeId)) return;

            // Don't allow deleting entrance (_0) or town (_1)
            string prefix = zone.IdPrefix;
            if (nodeId == $"{prefix}_0" || nodeId == $"{prefix}_1")
            {
                Plugin.Log.LogWarning($"[MapEditor] Cannot delete entrance/town node '{nodeId}'.");
                return;
            }

            // Remove all roads connected to this node
            if (_roads.NodeToRoads.TryGetValue(nodeId, out var roads))
            {
                foreach (string key in roads.ToList())
                    _roads.RemoveRoad(key);
            }

            // Destroy the GO
            Object.Destroy(node.gameObject);

            // Remove from zone data
            ZoneLoader.DeleteNode(nodeId);

            Plugin.Log.LogInfo($"[MapEditor] Deleted node '{nodeId}'");
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private Vector3 GetNodeWorldPos(string nodeId)
        {
            if (nodesContainer == null) return Vector3.zero;
            var t = nodesContainer.Find(nodeId);
            return t != null ? t.position : Vector3.zero;
        }

        /// <summary>
        /// Parse a road key "fromNodeId-toNodeId" into its two component node IDs.
        /// </summary>
        public static void ParseRoadKey(string key, out string fromId, out string toId)
            => RoadEditor.ParseRoadKey(key, out fromId, out toId);
    }
}
