using System;
using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Component attached to control point handles for identification.
    /// Used by MapEditor's Physics2D hit-testing to identify which road/index a CP belongs to.
    /// </summary>
    public class CPHandle : MonoBehaviour
    {
        public string roadKey;
        public int pointIndex;
    }

    /// <summary>
    /// Manages road visual state: LineRenderers, control point handles, and the
    /// underlying data (road CPs, node-to-road index). Shared between MapEditor
    /// (world-space, operates on real scene objects) and MapViewport (preview-space,
    /// operates on its own isolated scene).
    ///
    /// The RoadEditor owns the visual objects (LRs, CP handle GOs) and data dictionaries.
    /// Consumers are responsible for DTO updates (saving road/connection data to ZoneDef).
    ///
    /// Coordinate convention: CPs are stored in whatever space the consumer provides.
    /// The <c>toLRSpace</c> delegate converts positions to the LineRenderer's coordinate
    /// space (identity for world-space LRs, InverseTransformPoint for local-space LRs).
    /// </summary>
    public class RoadEditor
    {
        /// <summary>Visual configuration for roads and CP handles.</summary>
        public struct Config
        {
            public Color RoadColor;
            public float RoadWidth;
            public int RoadSortingOrder;
            public string SortingLayer; // null = don't set
            public bool UseWorldSpace;
            public Color CPColor;
            public int CPSortingOrder;
            public float CPZ; // z position for CP handles
        }

        // ── Public data (read by consumers for hit-testing, syncing, etc.) ──
        public readonly Dictionary<string, List<Vector3>> RoadCPs = new();
        public readonly Dictionary<string, HashSet<string>> NodeToRoads = new();
        public readonly Dictionary<string, LineRenderer> RoadLRs = new();
        public readonly Dictionary<string, List<GameObject>> CPHandleGOs = new();

        // ── Dependencies ─────────────────────────────────────────────
        private Func<string, Vector3> _getNodePos;
        private Func<Vector3, Vector3> _toLRSpace;
        private Transform _roadsContainer;
        private Transform _cpContainer;
        private Material _roadMat;
        private Config _cfg;

        // ── CP sprite (shared) ───────────────────────────────────────
        private static Sprite _cpSprite;

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
        /// <param name="cpContainer">Parent transform for CP handle GOs.</param>
        /// <param name="roadMat">Material for LineRenderers.</param>
        /// <param name="getNodePos">Returns the position of a node by ID (in CP coordinate space).</param>
        /// <param name="toLRSpace">Converts a position to LineRenderer coordinate space. Null = identity.</param>
        public void Init(Transform roadsContainer, Transform cpContainer, Material roadMat,
                         Func<string, Vector3> getNodePos, Func<Vector3, Vector3> toLRSpace = null)
        {
            _roadsContainer = roadsContainer;
            _cpContainer = cpContainer;
            _roadMat = roadMat;
            _getNodePos = getNodePos;
            _toLRSpace = toLRSpace ?? (v => v);
        }

        /// <summary>Expose the CP container for consumers that do their own hit-testing.</summary>
        public Transform CPContainer => _cpContainer;

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

                var cps = new List<Vector3>();
                for (int i = 0; i < lr.positionCount; i++)
                    cps.Add(lr.GetPosition(i));

                RoadCPs[key] = cps;
                RoadLRs[key] = lr;
                TrackNodeRoad(fromId, key);
                TrackNodeRoad(toId, key);
            }
            Plugin.Log.LogInfo($"[RoadEditor] Imported {RoadCPs.Count} existing roads.");
        }

        /// <summary>
        /// Add a road with pre-computed CPs (from zone data or connections).
        /// Creates the LineRenderer and refreshes it, but does NOT build CP handles
        /// (call RebuildAllCPHandles after all roads are imported).
        /// </summary>
        public void AddRoadFromData(string key, string fromId, string toId, List<Vector3> cps)
        {
            RoadCPs[key] = cps;
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

            RoadCPs[key] = new List<Vector3> { posA, posB };
            TrackNodeRoad(fromId, key);
            TrackNodeRoad(toId, key);

            CreateRoadLR(key);
            RefreshRoadLR(key);
            RebuildCPHandles(key);

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

            DestroyCPHandles(key);
            RoadCPs.Remove(key);

            if (fromId != null && NodeToRoads.TryGetValue(fromId, out var froads))
                froads.Remove(key);
            if (toId != null && NodeToRoads.TryGetValue(toId, out var troads))
                troads.Remove(key);
        }

        // ═══════════════════════════════════════════════════════════════
        //  ROAD VISUAL REFRESH
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Recompute LineRenderer positions from all stored CPs (direct point-to-point, matching game methodology).</summary>
        public void RefreshRoadLR(string key)
        {
            if (!RoadLRs.TryGetValue(key, out var lr) || lr == null) return;
            if (!RoadCPs.TryGetValue(key, out var cps) || cps.Count < 2) return;

            lr.positionCount = cps.Count;
            for (int i = 0; i < cps.Count; i++)
                lr.SetPosition(i, _toLRSpace(cps[i]));
        }

        /// <summary>Refresh all roads connected to a node (call after moving a node).
        /// Updates the first or last CP of each connected road to stay at the node's new position.</summary>
        public void UpdateRoadsForNode(string nodeId)
        {
            if (!NodeToRoads.TryGetValue(nodeId, out var roads)) return;
            Vector3 newPos = _getNodePos(nodeId);
            foreach (string key in roads)
            {
                if (!RoadCPs.TryGetValue(key, out var cps) || cps.Count < 2) continue;
                ParseRoadKey(key, out var fromId, out var toId);
                if (fromId == nodeId)
                    cps[0] = newPos;
                else if (toId == nodeId)
                    cps[cps.Count - 1] = newPos;
                RefreshRoadLR(key);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CP OPERATIONS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Insert a new CP between afterIndex and the next point.</summary>
        public void InsertCPAfter(string key, int afterIndex)
        {
            if (!RoadCPs.TryGetValue(key, out var cps)) return;
            if (afterIndex < 0 || afterIndex >= cps.Count - 1) return;

            Vector3 posA = cps[afterIndex];
            Vector3 posB = cps[afterIndex + 1];
            cps.Insert(afterIndex + 1, (posA + posB) / 2f);

            RefreshRoadLR(key);
            RebuildCPHandles(key);
            Plugin.Log.LogInfo($"[RoadEditor] Inserted CP in '{key}' after index {afterIndex}");
        }

        /// <summary>
        /// Delete a control point. If the last CP is removed, the entire road is deleted.
        /// Returns true if the road was removed (last CP case).
        /// </summary>
        public bool DeleteCP(string key, int cpIndex)
        {
            if (!RoadCPs.TryGetValue(key, out var cps)) return false;

            // Need at least 2 points (from/to endpoints) for a valid road
            if (cps.Count <= 2)
            {
                RemoveRoad(key);
                Plugin.Log.LogInfo($"[RoadEditor] Too few CPs → removed road '{key}'");
                return true;
            }

            cps.RemoveAt(cpIndex);
            RefreshRoadLR(key);
            RebuildCPHandles(key);
            Plugin.Log.LogInfo($"[RoadEditor] Deleted CP {cpIndex} from '{key}'");
            return false;
        }

        /// <summary>Update a CP's position in data + visuals (used during drag).</summary>
        public void UpdateCPPosition(string key, int index, Vector3 pos)
        {
            if (!RoadCPs.TryGetValue(key, out var cps) || index >= cps.Count) return;
            cps[index] = pos;
            RefreshRoadLR(key);

            if (CPHandleGOs.TryGetValue(key, out var handles) && index < handles.Count && handles[index] != null)
                handles[index].transform.position = new Vector3(pos.x, pos.y, _cfg.CPZ);
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
        //  CP HANDLE MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        public void RebuildAllCPHandles()
        {
            foreach (var kvp in CPHandleGOs)
                foreach (var go in kvp.Value)
                    if (go != null) UnityEngine.Object.Destroy(go);
            CPHandleGOs.Clear();

            foreach (string key in RoadCPs.Keys)
                RebuildCPHandles(key);
        }

        public void RebuildCPHandles(string key)
        {
            DestroyCPHandles(key);
            if (!RoadCPs.TryGetValue(key, out var cps) || _cpContainer == null) return;

            var handles = new List<GameObject>();
            for (int i = 0; i < cps.Count; i++)
            {
                var handle = new GameObject($"cp_{key}_{i}");
                handle.transform.SetParent(_cpContainer, false);
                handle.transform.position = new Vector3(cps[i].x, cps[i].y, _cfg.CPZ);

                var sr = handle.AddComponent<SpriteRenderer>();
                sr.sprite = EnsureCPSprite();
                sr.color = _cfg.CPColor;
                sr.sortingOrder = _cfg.CPSortingOrder;
                if (_cfg.SortingLayer != null)
                    sr.sortingLayerName = _cfg.SortingLayer;

                var cpH = handle.AddComponent<CPHandle>();
                cpH.roadKey = key;
                cpH.pointIndex = i;

                handles.Add(handle);
            }
            CPHandleGOs[key] = handles;
        }

        public void DestroyCPHandles(string key)
        {
            if (!CPHandleGOs.TryGetValue(key, out var handles)) return;
            foreach (var go in handles)
                if (go != null) UnityEngine.Object.Destroy(go);
            CPHandleGOs.Remove(key);
        }

        // ═══════════════════════════════════════════════════════════════
        //  EDIT MODE (for MapEditor's world-space toggle)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Toggle edit-mode visuals: show/hide roads, apply edit color, show/hide CP handles.
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
                RebuildAllCPHandles();
                ShowCPs(true);
            }
            else
            {
                ShowCPs(false);
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

        /// <summary>Show or hide the CP handles container.</summary>
        public void ShowCPs(bool show)
        {
            if (_cpContainer != null)
                _cpContainer.gameObject.SetActive(show);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Destroy all visual objects and clear data.</summary>
        public void ClearAll()
        {
            foreach (var kvp in CPHandleGOs)
                foreach (var go in kvp.Value)
                    if (go != null) UnityEngine.Object.Destroy(go);
            CPHandleGOs.Clear();

            foreach (var kvp in RoadLRs)
                if (kvp.Value != null) UnityEngine.Object.Destroy(kvp.Value.gameObject);
            RoadLRs.Clear();

            RoadCPs.Clear();
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

        private static Sprite EnsureCPSprite()
        {
            if (_cpSprite != null) return _cpSprite;

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
            _cpSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 64f);
            return _cpSprite;
        }
    }
}
