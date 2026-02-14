using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Builds runtime zone maps (background + nodes + roads) for modded zones.
    /// Called from <see cref="Patches"/> when the game tries to load a modded zone's map prefab.
    /// </summary>
    public static class MapBuilder
    {
        private static readonly Dictionary<string, Sprite> _backgroundSprites = new();
        private static string _nodeSortingLayer = "Default";
        private static int _nodeSortingLayerID = 0;

        /// <summary>Clear cached background sprites. Called during full rebuild.</summary>
        public static void ClearCache()
        {
            _backgroundSprites.Clear();
        }

        // ═══════════════════════════════════════════════════════════════
        //  BACKGROUND SPRITE
        // ═══════════════════════════════════════════════════════════════

        public static Sprite GetBackgroundSprite(string zoneId)
        {
            if (_backgroundSprites.TryGetValue(zoneId, out var cached) && cached != null)
                return cached;

            if (!ModRegistry.LoadedZones.TryGetValue(zoneId, out var zoneDef))
            {
                Plugin.Log.LogError($"[MapBuilder] GetBackgroundSprite: zone '{zoneId}' not loaded!");
                return null;
            }

            string folder = ModRegistry.GetZoneFolder(zoneId);
            string bgName = zoneDef.BackgroundImage ?? "background.jpeg";
            string bgFile = ResolveTexturePath(folder, bgName);

            if (bgFile == null)
            {
                Plugin.Log.LogError($"[MapBuilder] Background image not found: {bgName} (searched textures/ in mod root for {folder})");
                return null;
            }

            byte[] data = File.ReadAllBytes(bgFile);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.filterMode = FilterMode.Bilinear;

            float ppuH = tex.width / 19.2f;
            float ppuV = tex.height / 10.8f;
            float ppu = Mathf.Min(ppuH, ppuV);

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), ppu);

            _backgroundSprites[zoneId] = sprite;
            return sprite;
        }

        // ═══════════════════════════════════════════════════════════════
        //  TEXTURE PATH RESOLUTION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolve a texture filename to an absolute path on disk.
        /// Looks in {modRoot}/textures/{filename} only.
        /// Returns null if not found.
        /// </summary>
        public static string ResolveTexturePath(string folder, string filename)
        {
            if (string.IsNullOrEmpty(filename)) return null;

            // If the filename is already an absolute path that exists, use it directly
            if (Path.IsPathRooted(filename) && File.Exists(filename))
                return filename;

            // Mod-root textures/ folder: walk up from zone folder (zones/{zoneId}/) to mod root
            string modRoot = Path.GetDirectoryName(Path.GetDirectoryName(folder));
            if (modRoot != null)
            {
                string path = Path.Combine(modRoot, "textures", filename);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  BUILD & INJECT MAP
        // ═══════════════════════════════════════════════════════════════

        public static bool BuildAndInjectMap(string zoneId, Transform worldTransform)
        {
            if (!ModRegistry.LoadedZones.TryGetValue(zoneId, out var zoneDef))
            {
                Plugin.Log.LogError($"[MapBuilder] BuildAndInjectMap: zone '{zoneId}' not loaded!");
                return false;
            }

            // Don't rebuild if already present
            for (int i = 0; i < worldTransform.childCount; i++)
            {
                if (worldTransform.GetChild(i).gameObject.name == zoneId)
                    return false;
            }

            // Set CurrentZone for editor integration
            ZoneEditingService.CurrentZone = zoneDef;

            GameObject nodeTemplate = FindNodeTemplate();
            if (nodeTemplate == null)
            {
                Plugin.Log.LogError("[MapBuilder] Could not find a Node template!");
                return false;
            }

            var templateSRs = nodeTemplate.GetComponentsInChildren<SpriteRenderer>(true);
            if (templateSRs.Length > 0)
            {
                _nodeSortingLayer = templateSRs[0].sortingLayerName;
                _nodeSortingLayerID = templateSRs[0].sortingLayerID;
            }

            var root = new GameObject(zoneId);
            root.transform.SetParent(worldTransform, false);

            var bgSprite = GetBackgroundSprite(zoneId);
            if (bgSprite != null)
            {
                var bgGO = new GameObject("Background");
                bgGO.transform.SetParent(root.transform, false);
                bgGO.transform.localPosition = Vector3.zero;
                var sr = bgGO.AddComponent<SpriteRenderer>();
                sr.sprite = bgSprite;
                sr.color = Color.white;
                sr.sortingLayerName = _nodeSortingLayer;
                sr.sortingOrder = -10;
            }

            var nodesGO = new GameObject("Nodes");
            nodesGO.transform.SetParent(root.transform, false);
            nodesGO.transform.localPosition = new Vector3(0f, 0f, -2f);

            var nodePositions = GetNodePositions(zoneDef);
            foreach (var kvp in nodePositions)
            {
                var nodeGO = Object.Instantiate(nodeTemplate, nodesGO.transform);
                nodeGO.name = kvp.Key;
                nodeGO.transform.localPosition = kvp.Value;
                nodeGO.transform.localScale = Vector3.one;
                nodeGO.SetActive(true);

                var nodeComp = nodeGO.GetComponent<Node>();
                if (nodeComp != null)
                    nodeComp.nodeData = Globals.Instance.GetNodeData(kvp.Key);
            }

            var roadsGO = new GameObject("Roads");
            roadsGO.transform.SetParent(root.transform, false);
            CreateMapRoads(roadsGO.transform, nodePositions, zoneDef);

            // Attach MapEditor to the zone map root (ModEditor is persistent)
            ModEditor.Instance?.AttachMapEditor(root);

            Plugin.Log.LogInfo($"[MapBuilder] Map built: {zoneId} ({nodePositions.Count} nodes).");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static GameObject FindNodeTemplate()
        {
            var mapList = MapManager.Instance?.mapList;
            if (mapList != null)
            {
                for (int i = 0; i < mapList.Count; i++)
                {
                    if (mapList[i] == null) continue;
                    var nodesT = mapList[i].transform.Find("Nodes");
                    if (nodesT == null || nodesT.childCount == 0) continue;
                    var nodeComp = nodesT.GetChild(0).GetComponent<Node>();
                    if (nodeComp != null) return nodesT.GetChild(0).gameObject;
                }
            }

            var worldT = MapManager.Instance?.worldTransform;
            if (worldT != null)
            {
                foreach (Transform z in worldT)
                {
                    var nodesT = z.Find("Nodes");
                    if (nodesT == null || nodesT.childCount == 0) continue;
                    var nodeComp = nodesT.GetChild(0).GetComponent<Node>();
                    if (nodeComp != null) return nodesT.GetChild(0).gameObject;
                }
            }

            return null;
        }

        private static void CreateMapRoads(Transform roadsParent, Dictionary<string, Vector3> nodePositions, ZoneDef zoneDef)
        {
            Material roadMat = FindRoadMaterial() ?? new Material(Shader.Find("Sprites/Default"));

            foreach (var kvp in zoneDef.Roads)
            {
                var road = kvp.Value;
                if (!nodePositions.ContainsKey(road.FromNodeId) || !nodePositions.ContainsKey(road.ToNodeId))
                    continue;

                Vector3 posA = nodePositions[road.FromNodeId];
                Vector3 posB = nodePositions[road.ToNodeId];
                var waypoints = road.Waypoints.Select(wp => new Vector3(wp[0], wp[1], 0f)).ToArray();

                int total = 2 + waypoints.Length;
                var pts = new Vector3[total];
                pts[0] = posA;
                for (int i = 0; i < waypoints.Length; i++) pts[i + 1] = waypoints[i];
                pts[total - 1] = posB;

                var go = new GameObject(kvp.Key);
                go.transform.SetParent(roadsParent, false);
                go.SetActive(false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = total;
                for (int i = 0; i < total; i++) lr.SetPosition(i, pts[i]);
                lr.startWidth = 0.06f;
                lr.endWidth = 0.06f;
                lr.material = roadMat;
                lr.startColor = Color.white;
                lr.endColor = Color.white;
                lr.sortingLayerName = _nodeSortingLayer;
                lr.sortingOrder = -5;
            }
        }

        private static Material FindRoadMaterial()
        {
            var worldT = MapManager.Instance?.worldTransform;
            if (worldT == null) return null;
            foreach (Transform z in worldT)
            {
                var roadsT = z.Find("Roads");
                if (roadsT == null || roadsT.childCount == 0) continue;
                var lr = roadsT.GetChild(0).GetComponent<LineRenderer>();
                if (lr?.material != null) return lr.material;
            }
            return null;
        }

        private static Dictionary<string, Vector3> GetNodePositions(ZoneDef zoneDef)
        {
            var result = new Dictionary<string, Vector3>();
            if (zoneDef?.Nodes == null) return result;
            foreach (var kvp in zoneDef.Nodes)
                result[kvp.Key] = new Vector3(kvp.Value.PosX, kvp.Value.PosY, 0f);
            return result;
        }
    }
}
