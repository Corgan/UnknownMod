using System;
using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Component attached to waypoint handles for identification.
    /// Used by hit-testing to identify which road/index a waypoint belongs to.
    /// </summary>
    public class WaypointHandle : MonoBehaviour
    {
        public string roadKey;
        public int pointIndex;
    }

    /// <summary>
    /// Manages road visual state: LineRenderers, waypoint handles, and the
    /// underlying data (road waypoints, node-to-road index). Used by MapEditor for
    /// interactive road editing in the map preview.
    ///
    /// The RoadEditor owns the visual objects (LRs, waypoint handle GOs) and data dictionaries.
    /// Consumers are responsible for DTO updates (saving road/connection data to ZoneDef).
    ///
    /// Coordinate convention: waypoints are stored in whatever space the consumer provides.
    /// The <c>toLRSpace</c> delegate converts positions to the LineRenderer's coordinate
    /// space (identity for world-space LRs, InverseTransformPoint for local-space LRs).
    /// </summary>
    public class RoadEditor
    {
        /// <summary>Visual configuration for roads and waypoint handles.</summary>
        public struct Config
        {
            public Color RoadColor;
            public float RoadWidth;
            public int RoadSortingOrder;
            public string SortingLayer; // null = don't set
            public bool UseWorldSpace;
            public Color WaypointColor;
            public int WaypointSortingOrder;
            public float WaypointZ; // z position for waypoint handles
        }

        // ── Public data (read by consumers for hit-testing, syncing, etc.) ──
        public readonly Dictionary<string, List<Vector3>> RoadWaypoints = new();
        public readonly Dictionary<string, HashSet<string>> NodeToRoads = new();
        public readonly Dictionary<string, LineRenderer> RoadLRs = new();
        public readonly Dictionary<string, List<GameObject>> WaypointHandleGOs = new();

        // ── Dependencies ─────────────────────────────────────────────
        private Func<string, Vector3> _getNodePos;
        private Func<Vector3, Vector3> _toLRSpace;
        private Transform _roadsContainer;
        private Transform _wpContainer;
        private Material _roadMat;
        private Config _cfg;

        // ── Waypoint sprite (shared) ─────────────────────────────────
        private static Sprite _wpSprite;

        // ═══════════════════════════════════════════════════════════════
        //  CONSTRUCTION
        // ═══════════════════════════════════════════════════════════════

        public RoadEditor(Config config)
        {
            _cfg = config;
        }

        /// <summary>
        /// Initialize with container transforms and dependency delegates.
        /// </summary>
        /// <param name="roadsContainer">Parent transform for road LineRenderer GOs.</param>
        /// <param name="wpContainer">Parent transform for waypoint handle GOs.</param>
        /// <param name="roadMat">Material for LineRenderers.</param>
        /// <param name="getNodePos">Returns the position of a node by ID (in waypoint coordinate space).</param>
        /// <param name="toLRSpace">Converts a position to LineRenderer coordinate space. Null = identity.</param>
        public void Init(Transform roadsContainer, Transform wpContainer, Material roadMat,
                         Func<string, Vector3> getNodePos, Func<Vector3, Vector3> toLRSpace = null)
        {
            _roadsContainer = roadsContainer;
            _wpContainer = wpContainer;
            _roadMat = roadMat;
            _getNodePos = getNodePos;
            _toLRSpace = toLRSpace ?? (v => v);
        }

        /// <summary>Expose the waypoint container for consumers that do their own hit-testing.</summary>
        public Transform WaypointContainer => _wpContainer;

        // ═══════════════════════════════════════════════════════════════
        //  IMPORT
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Read existing LineRenderer children of the roads container (built by MapBuilder)
        /// and populate the editor's data structures from them.
        /// </summary>
        public void ImportFromExistingLRs()
        {
            if (_roadsContainer == null) return;

            foreach (Transform roadT in _roadsContainer)
            {
                var lr = roadT.GetComponent<LineRenderer>();
                if (lr == null || lr.positionCount < 2) continue;

                string key = roadT.gameObject.name;
                ParseRoadKey(key, out string fromId, out string toId);
                if (fromId == null) continue;

                var wps = new List<Vector3>();
                for (int i = 0; i < lr.positionCount; i++)
                    wps.Add(lr.GetPosition(i));

                RoadWaypoints[key] = wps;
                RoadLRs[key] = lr;
                TrackNodeRoad(fromId, key);
                TrackNodeRoad(toId, key);
            }
            Plugin.Log.LogInfo($"[RoadEditor] Imported {RoadWaypoints.Count} existing roads.");
        }

        /// <summary>
        /// Add a road with pre-computed waypoints (from zone data or connections).
        /// Creates the LineRenderer and refreshes it, but does NOT build waypoint handles
        /// (call RebuildAllWaypointHandles after all roads are imported).
        /// </summary>
        public void AddRoadFromData(string key, string fromId, string toId, List<Vector3> cps)
        {
            RoadWaypoints[key] = cps;
            TrackNodeRoad(fromId, key);
            TrackNodeRoad(toId, key);
            CreateRoadLR(key);
            RefreshRoadLR(key);
        }

        // ═══════════════════════════════════════════════════════════════
        //  ROAD CRUD
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create a new road between two nodes with endpoints at their positions (matching game methodology).
        /// Returns the road key ("fromId-toId").
        /// </summary>
        public string CreateRoad(string fromId, string toId)
        {
            string key = $"{fromId}-{toId}";

            Vector3 posA = _getNodePos(fromId);
            Vector3 posB = _getNodePos(toId);

            RoadWaypoints[key] = new List<Vector3> { posA, posB };
            TrackNodeRoad(fromId, key);
            TrackNodeRoad(toId, key);

            CreateRoadLR(key);
            RefreshRoadLR(key);
            RebuildWaypointHandles(key);

            return key;
        }

        /// <summary>
        /// Remove a road: destroys visual objects and removes from data structures.
        /// Does NOT update the ZoneDef DTO — caller is responsible for that.
        /// </summary>
        public void RemoveRoad(string key)
        {
            ParseRoadKey(key, out string fromId, out string toId);

            if (RoadLRs.TryGetValue(key, out var lr) && lr != null)
            {
                UnityEngine.Object.Destroy(lr.gameObject);
                RoadLRs.Remove(key);
            }

            DestroyWaypointHandles(key);
            RoadWaypoints.Remove(key);

            if (fromId != null && NodeToRoads.TryGetValue(fromId, out var froads))
                froads.Remove(key);
            if (toId != null && NodeToRoads.TryGetValue(toId, out var troads))
                troads.Remove(key);
        }

        // ═══════════════════════════════════════════════════════════════
        //  ROAD VISUAL REFRESH
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Recompute LineRenderer positions from all stored waypoints (direct point-to-point, matching game methodology).</summary>
        public void RefreshRoadLR(string key)
        {
            if (!RoadLRs.TryGetValue(key, out var lr) || lr == null) return;
            if (!RoadWaypoints.TryGetValue(key, out var wps) || wps.Count < 2) return;

            lr.positionCount = wps.Count;
            for (int i = 0; i < wps.Count; i++)
                lr.SetPosition(i, _toLRSpace(wps[i]));
        }

        /// <summary>Refresh all roads connected to a node (call after moving a node).
        /// Updates the first or last waypoint of each connected road to stay at the node's new position.</summary>
        public void UpdateRoadsForNode(string nodeId)
        {
            if (!NodeToRoads.TryGetValue(nodeId, out var roads)) return;
            Vector3 newPos = _getNodePos(nodeId);
            foreach (string key in roads)
            {
                if (!RoadWaypoints.TryGetValue(key, out var wps) || wps.Count < 2) continue;
                ParseRoadKey(key, out var fromId, out var toId);
                if (fromId == nodeId)
                    wps[0] = newPos;
                else if (toId == nodeId)
                    wps[wps.Count - 1] = newPos;
                RefreshRoadLR(key);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  WAYPOINT OPERATIONS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Insert a new waypoint between afterIndex and the next point.</summary>
        public void InsertWaypointAfter(string key, int afterIndex)
        {
            if (!RoadWaypoints.TryGetValue(key, out var wps)) return;
            if (afterIndex < 0 || afterIndex >= wps.Count - 1) return;

            Vector3 posA = wps[afterIndex];
            Vector3 posB = wps[afterIndex + 1];
            wps.Insert(afterIndex + 1, (posA + posB) / 2f);

            RefreshRoadLR(key);
            RebuildWaypointHandles(key);
            Plugin.Log.LogInfo($"[RoadEditor] Inserted waypoint in '{key}' after index {afterIndex}");
        }

        /// <summary>
        /// Delete a waypoint. If the last waypoint is removed, the entire road is deleted.
        /// Returns true if the road was removed (last waypoint case).
        /// </summary>
        public bool DeleteWaypoint(string key, int wpIndex)
        {
            if (!RoadWaypoints.TryGetValue(key, out var wps)) return false;

            // Need at least 2 points (from/to endpoints) for a valid road
            if (wps.Count <= 2)
            {
                RemoveRoad(key);
                Plugin.Log.LogInfo($"[RoadEditor] Too few waypoints → removed road '{key}'");
                return true;
            }

            wps.RemoveAt(wpIndex);
            RefreshRoadLR(key);
            RebuildWaypointHandles(key);
            Plugin.Log.LogInfo($"[RoadEditor] Deleted waypoint {wpIndex} from '{key}'");
            return false;
        }

        /// <summary>Update a waypoint's position in data + visuals (used during drag).</summary>
        public void UpdateWaypointPosition(string key, int index, Vector3 pos)
        {
            if (!RoadWaypoints.TryGetValue(key, out var wps) || index >= wps.Count) return;
            wps[index] = pos;
            RefreshRoadLR(key);

            if (WaypointHandleGOs.TryGetValue(key, out var handles) && index < handles.Count && handles[index] != null)
                handles[index].transform.position = new Vector3(pos.x, pos.y, _cfg.WaypointZ);
        }

        // ═══════════════════════════════════════════════════════════════
        //  LR CREATION (private)
        // ═══════════════════════════════════════════════════════════════

        private void CreateRoadLR(string key)
        {
            if (_roadsContainer == null) return;

            var go = new GameObject(key);
            go.transform.SetParent(_roadsContainer, false);
            go.SetActive(true);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = _cfg.UseWorldSpace;
            lr.material = _roadMat;
            lr.startWidth = _cfg.RoadWidth;
            lr.endWidth = _cfg.RoadWidth;
            lr.startColor = _cfg.RoadColor;
            lr.endColor = _cfg.RoadColor;
            lr.sortingOrder = _cfg.RoadSortingOrder;
            if (_cfg.SortingLayer != null)
                lr.sortingLayerName = _cfg.SortingLayer;

            RoadLRs[key] = lr;
        }

        // ═══════════════════════════════════════════════════════════════
        //  WAYPOINT HANDLE MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        public void RebuildAllWaypointHandles()
        {
            foreach (var kvp in WaypointHandleGOs)
                foreach (var go in kvp.Value)
                    if (go != null) UnityEngine.Object.Destroy(go);
            WaypointHandleGOs.Clear();

            foreach (string key in RoadWaypoints.Keys)
                RebuildWaypointHandles(key);
        }

        public void RebuildWaypointHandles(string key)
        {
            DestroyWaypointHandles(key);
            if (!RoadWaypoints.TryGetValue(key, out var wps) || _wpContainer == null) return;

            var handles = new List<GameObject>();
            for (int i = 0; i < wps.Count; i++)
            {
                var handle = new GameObject($"cp_{key}_{i}");
                handle.transform.SetParent(_wpContainer, false);
                handle.transform.position = new Vector3(wps[i].x, wps[i].y, _cfg.WaypointZ);

                var sr = handle.AddComponent<SpriteRenderer>();
                sr.sprite = EnsureWaypointSprite();
                sr.color = _cfg.WaypointColor;
                sr.sortingOrder = _cfg.WaypointSortingOrder;
                if (_cfg.SortingLayer != null)
                    sr.sortingLayerName = _cfg.SortingLayer;

                var wpH = handle.AddComponent<WaypointHandle>();
                wpH.roadKey = key;
                wpH.pointIndex = i;

                handles.Add(handle);
            }
            WaypointHandleGOs[key] = handles;
        }

        public void DestroyWaypointHandles(string key)
        {
            if (!WaypointHandleGOs.TryGetValue(key, out var handles)) return;
            foreach (var go in handles)
                if (go != null) UnityEngine.Object.Destroy(go);
            WaypointHandleGOs.Remove(key);
        }

        // ═══════════════════════════════════════════════════════════════
        //  EDIT MODE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Toggle edit-mode visuals: show/hide roads, apply edit color, show/hide waypoint handles.
        /// </summary>
        public void SetEditMode(bool active, Color editColor)
        {
            if (active)
            {
                if (_roadsContainer != null)
                {
                    foreach (Transform roadT in _roadsContainer)
                    {
                        roadT.gameObject.SetActive(true);
                        var lr = roadT.GetComponent<LineRenderer>();
                        if (lr != null)
                        {
                            lr.startColor = editColor;
                            lr.endColor = editColor;
                        }
                    }
                }
                RebuildAllWaypointHandles();
                ShowWaypoints(true);
            }
            else
            {
                ShowWaypoints(false);
                if (_roadsContainer != null)
                {
                    foreach (Transform roadT in _roadsContainer)
                    {
                        roadT.gameObject.SetActive(false);
                        var lr = roadT.GetComponent<LineRenderer>();
                        if (lr != null)
                        {
                            lr.startColor = Color.white;
                            lr.endColor = Color.white;
                        }
                    }
                }
            }
        }

        /// <summary>Show or hide the waypoint handles container.</summary>
        public void ShowWaypoints(bool show)
        {
            if (_wpContainer != null)
                _wpContainer.gameObject.SetActive(show);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Destroy all visual objects and clear data.</summary>
        public void ClearAll()
        {
            foreach (var kvp in WaypointHandleGOs)
                foreach (var go in kvp.Value)
                    if (go != null) UnityEngine.Object.Destroy(go);
            WaypointHandleGOs.Clear();

            foreach (var kvp in RoadLRs)
                if (kvp.Value != null) UnityEngine.Object.Destroy(kvp.Value.gameObject);
            RoadLRs.Clear();

            RoadWaypoints.Clear();
            NodeToRoads.Clear();
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        public void TrackNodeRoad(string nodeId, string key)
        {
            if (!NodeToRoads.ContainsKey(nodeId))
                NodeToRoads[nodeId] = new HashSet<string>();
            NodeToRoads[nodeId].Add(key);
        }

        /// <summary>
        /// Parse a road key "fromNodeId-toNodeId" into its two component node IDs.
        /// </summary>
        public static void ParseRoadKey(string key, out string fromId, out string toId)
        {
            int dash = key.IndexOf('-');
            if (dash < 0) { fromId = toId = null; return; }
            fromId = key.Substring(0, dash);
            toId = key.Substring(dash + 1);
        }

        private static Sprite EnsureWaypointSprite()
        {
            if (_wpSprite != null) return _wpSprite;

            int size = 16;
            var tex = new Texture2D(size, size);
            float c = size / 2f;
            for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f;
                tex.SetPixel(x, y,
                    Mathf.Sqrt(dx * dx + dy * dy) <= c
                        ? Color.white : Color.clear);
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _wpSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 64f);
            return _wpSprite;
        }
    }
}
