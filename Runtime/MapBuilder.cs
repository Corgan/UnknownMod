using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
        private static string _nodeSortingLayer = "Default";
        private static int _nodeSortingLayerID = 0;

        /// <summary>Clear cached sprites. Called during full rebuild.</summary>
        public static void ClearCache()
        {
            _mapPieceSpriteCache.Clear();
        }

        // ═══════════════════════════════════════════════════════════════
        //  TEXTURE PATH RESOLUTION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolve a texture filename to an absolute path on disk.
        /// Looks in {modRoot}/sprites/{filename} only.
        /// Returns null if not found.
        /// </summary>
        public static string ResolveTexturePath(string folder, string filename)
        {
            if (string.IsNullOrEmpty(filename)) return null;

            // If the filename is already an absolute path that exists, use it directly
            if (Path.IsPathRooted(filename) && File.Exists(filename))
                return filename;

            // Mod-root sprites/ folder: walk up from zone folder (zones/{zoneId}/) to mod root
            string modRoot = Path.GetDirectoryName(Path.GetDirectoryName(folder));
            if (modRoot != null)
            {
                string path = Path.Combine(modRoot, "sprites", filename);
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

            // Set CurrentZone for editor integration (only if not already editing another zone)
            if (ZoneEditingService.CurrentZone == null)
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

            // Spawn visual layers (background + map pieces)
            SpawnRuntimeVisualLayers(root.transform, zoneDef);

            var nodesGO = new GameObject("Nodes");
            nodesGO.transform.SetParent(root.transform, false);
            nodesGO.transform.localPosition = new Vector3(zoneDef.NodesOffsetX, zoneDef.NodesOffsetY, -2f);

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

                // Spawn mapPiece children (requirement-gated sprite overlays)
                if (zoneDef.Nodes.TryGetValue(kvp.Key, out var nd))
                    SpawnMapPieces(nodeGO.transform, nd);
            }

            var roadsGO = new GameObject("Roads");
            roadsGO.transform.SetParent(root.transform, false);
            roadsGO.transform.localPosition = new Vector3(zoneDef.RoadsOffsetX, zoneDef.RoadsOffsetY, 0f);
            CreateMapRoads(roadsGO.transform, nodePositions, zoneDef);

            Plugin.Log.LogInfo($"[MapBuilder] Map built: {zoneId} ({nodePositions.Count} nodes).");
            return true;
        }

        private static void SpawnMapPieces(Transform nodeT, NodeDef nd)
        {
            foreach (var mp in nd.MapPieces)
            {
                Sprite mpSprite = FindMapPieceSprite(mp.SpriteName);
                if (mpSprite == null) continue;

                var mpGO = new GameObject("mapPiece");
                mpGO.transform.SetParent(nodeT, false);
                mpGO.transform.localPosition = new Vector3(mp.PosX, mp.PosY, 0f);
                mpGO.transform.localScale = new Vector3(mp.ScaleX, mp.ScaleY, 1f);
                var sr = mpGO.AddComponent<SpriteRenderer>();
                sr.sprite = mpSprite;
                sr.sortingOrder = mp.SortingOrder;
                sr.color = new Color(mp.ColorR, mp.ColorG, mp.ColorB, mp.ColorA);
                sr.flipX = mp.FlipX;
                sr.flipY = mp.FlipY;
                if (!string.IsNullOrEmpty(mp.SortingLayer))
                {
                    try { sr.sortingLayerName = mp.SortingLayer; }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Apply a zone patch to the game's own prefab map right after it is instantiated: spawn nodes the
        /// prefab lacks, move patched nodes, and add roads. Must run before MapManager.GetMapNodes indexes nodes.
        /// </summary>
        public static void ApplyPatchToBaseMap(string zoneId, Transform worldTransform)
        {
            if (!ModRegistry.LoadedZonePatches.TryGetValue(zoneId, out var patch) || worldTransform == null) return;

            Transform root = null;
            for (int i = 0; i < worldTransform.childCount; i++)
                if (string.Equals(worldTransform.GetChild(i).gameObject.name, zoneId, System.StringComparison.OrdinalIgnoreCase))
                { root = worldTransform.GetChild(i); break; }
            if (root == null) return;
            var nodesT = root.Find("Nodes");
            var roadsT = root.Find("Roads");
            if (nodesT == null) return;

            try
            {
                GameObject template = FindNodeTemplate();
                var existing = new Dictionary<string, Transform>(System.StringComparer.OrdinalIgnoreCase);
                foreach (Transform c in nodesT) existing[c.gameObject.name] = c;

                int added = 0;
                foreach (var nd in patch.Nodes.Values)
                {
                    if (existing.TryGetValue(nd.NodeId, out var nodeT))
                    {
                        if (Mathf.Abs(nd.PosX) > 0.0001f || Mathf.Abs(nd.PosY) > 0.0001f)
                            nodeT.localPosition = new Vector3(nd.PosX, nd.PosY, nodeT.localPosition.z);
                        // The scene node still points at the prefab's NodeData; the patch registered a replacement
                        var existingComp = nodeT.GetComponent<Node>();
                        var patched = Globals.Instance.GetNodeData(nd.NodeId);
                        if (existingComp != null && patched != null) existingComp.nodeData = patched;
                        continue;
                    }
                    if (template == null) continue;
                    var go = Object.Instantiate(template, nodesT);
                    go.name = nd.NodeId;
                    go.transform.localPosition = new Vector3(nd.PosX, nd.PosY, 0f);
                    go.transform.localScale = Vector3.one;
                    go.SetActive(true);
                    var comp = go.GetComponent<Node>();
                    if (comp != null) comp.nodeData = Globals.Instance.GetNodeData(nd.NodeId);
                    SpawnMapPieces(go.transform, nd);
                    existing[nd.NodeId] = go.transform;
                    added++;
                }

                int roads = 0;
                if (roadsT != null)
                {
                    // Waypoints are stored in Nodes-parent space; convert into the Roads container's space
                    Vector3 delta = roadsT.localPosition - nodesT.localPosition;
                    Material mat = FindRoadMaterial() ?? new Material(Shader.Find("Sprites/Default"));
                    if (string.IsNullOrEmpty(_nodeSortingLayer) && nodesT.childCount > 0)
                    {
                        var sr = nodesT.GetComponentInChildren<SpriteRenderer>(true);
                        if (sr != null) _nodeSortingLayer = sr.sortingLayerName;
                    }

                    var have = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    foreach (Transform c in roadsT) have.Add(c.gameObject.name);

                    foreach (var kvp in patch.Roads)
                    {
                        var pts = kvp.Value.Waypoints.Select(wp => new Vector3(wp[0], wp[1], 0f) - delta).ToArray();
                        if (pts.Length < 2) continue;
                        // Replace a prefab road of the same name so edited waypoints win
                        for (int i = roadsT.childCount - 1; i >= 0; i--)
                            if (string.Equals(roadsT.GetChild(i).gameObject.name, kvp.Key, System.StringComparison.OrdinalIgnoreCase))
                                Object.Destroy(roadsT.GetChild(i).gameObject);
                        CreateRoadLR(roadsT, kvp.Key, pts, mat);
                        have.Add(kvp.Key);
                        roads++;
                    }

                    // Straight roads for new nodes' connections that have no explicit road
                    foreach (var nd in patch.Nodes.Values)
                    {
                        if (!existing.TryGetValue(nd.NodeId, out var a)) continue;
                        foreach (var conn in nd.Connections)
                        {
                            string key = nd.NodeId + "-" + conn, rev = conn + "-" + nd.NodeId;
                            if (have.Contains(key) || have.Contains(rev)) continue;
                            if (!existing.TryGetValue(conn, out var b)) continue;
                            CreateRoadLR(roadsT, key, new[] { a.localPosition - delta, b.localPosition - delta }, mat);
                            have.Add(key);
                            roads++;
                        }
                    }
                }

                Plugin.Log.LogInfo($"[MapBuilder] Patched base map '{zoneId}': +{added} nodes, {roads} roads.");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[MapBuilder] ApplyPatchToBaseMap '{zoneId}' failed: {ex}");
            }
        }
        // ═══════════════════════════════════════════════════════════════
        //  VISUAL LAYERS (runtime)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Spawn all visual layers from the zone definition at runtime.
        /// Handles all layer types: Sprite, SpriteMask, Light, ParticleSystem, Container.
        /// </summary>
        private static void SpawnRuntimeVisualLayers(Transform root, ZoneDef zoneDef)
        {
            if (zoneDef.VisualLayers == null || zoneDef.VisualLayers.Count == 0)
                return;

            foreach (var layer in zoneDef.VisualLayers)
            {
                if (!layer.Visible || layer.Hidden) continue;

                var go = new GameObject(layer.Name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
                go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);

                switch (layer.Type)
                {
                    case VisualLayerType.Sprite:
                    {
                        Sprite sprite = ResolveLayerSprite(layer.SpriteName);
                        if (sprite == null)
                        {
                            Object.Destroy(go);
                            continue;
                        }
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = sprite;
                        sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        sr.sortingOrder = layer.SortingOrder;
                        sr.flipX = layer.FlipX;
                        sr.flipY = layer.FlipY;
                        sr.enabled = layer.Enabled;
                        if (!string.IsNullOrEmpty(layer.SortingLayer))
                            try { sr.sortingLayerName = layer.SortingLayer; } catch { }
                        break;
                    }

                    case VisualLayerType.SpriteMask:
                    {
                        Sprite sprite = ResolveLayerSprite(layer.SpriteName);
                        if (sprite == null)
                        {
                            Object.Destroy(go);
                            continue;
                        }
                        var sm = go.AddComponent<SpriteMask>();
                        sm.sprite = sprite;
                        sm.alphaCutoff = layer.AlphaCutoff;
                        sm.isCustomRangeActive = layer.CustomRange;
                        if (layer.CustomRange)
                        {
                            sm.frontSortingOrder = layer.FrontSortingOrder;
                            sm.backSortingOrder = layer.BackSortingOrder;
                        }
                        sm.sortingOrder = layer.SortingOrder;
                        sm.enabled = layer.Enabled;
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
                        if (layer.LightType == 3) // Point
                        {
                            light.pointLightInnerAngle = layer.PointLightInnerAngle;
                            light.pointLightOuterAngle = layer.PointLightOuterAngle;
                            light.pointLightInnerRadius = layer.PointLightInnerRadius;
                            light.pointLightOuterRadius = layer.PointLightOuterRadius;
                        }
                        if (layer.LightType == 0 || layer.LightType == 1) // Parametric / Freeform
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
                                var mat = new Material(Shader.Find("Particles/Standard Unlit"));
                                mat.SetFloat("_Mode", 1); // Additive
                                psr.sharedMaterial = mat;
                            }
                        }

                        if (layer.PlayOnAwake)
                            ps.Play();
                        break;
                    }

                    case VisualLayerType.Container:
                        // Empty container — just the transform
                        break;

                    case VisualLayerType.Shader:
                    {
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = ShaderPresetGenerator.GetPresetSprite(layer.Preset, layer.PresetParam1, layer.PresetParam2);
                        sr.sortingOrder = layer.SortingOrder;
                        sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        sr.maskInteraction = (SpriteMaskInteraction)layer.MaskInteraction;
                        sr.enabled = layer.Enabled;
                        if (!string.IsNullOrEmpty(layer.SortingLayer))
                            try { sr.sortingLayerName = layer.SortingLayer; } catch { }
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
                            var prefab = GameResources.GetEffectPrefab(layer.EffectName);
                            if (prefab != null)
                            {
                                var clone = Object.Instantiate(prefab, go.transform);
                                clone.name = layer.EffectName;
                            }
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>Resolve a sprite name to a Sprite object: mod images first, then game resources.</summary>
        private static Sprite ResolveLayerSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            if (ModRegistry.ModImageSprites.TryGetValue(spriteName, out var modSprite))
                return modSprite;
            return FindMapPieceSprite(spriteName);
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static GameObject FindNodeTemplate()
        {
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

            foreach (var prefab in GameResources.EnumerateMapPrefabs())
            {
                if (prefab == null) continue;
                var nodesT = prefab.transform.Find("Nodes");
                if (nodesT == null || nodesT.childCount == 0) continue;
                var nodeComp = nodesT.GetChild(0).GetComponent<Node>();
                if (nodeComp != null) return nodesT.GetChild(0).gameObject;
            }

            return null;
        }

        private static void CreateMapRoads(Transform roadsParent, Dictionary<string, Vector3> nodePositions, ZoneDef zoneDef)
        {
            Material roadMat = FindRoadMaterial() ?? new Material(Shader.Find("Sprites/Default"));

            // Node positions and waypoints are in the nodes container's space; the roads container may sit elsewhere
            var toRoadSpace = new Vector3(zoneDef.NodesOffsetX - zoneDef.RoadsOffsetX, zoneDef.NodesOffsetY - zoneDef.RoadsOffsetY, 0f);

            // Track which connections already have explicit road data
            var createdRoads = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in zoneDef.Roads)
            {
                var road = kvp.Value;
                if (!nodePositions.ContainsKey(road.FromNodeId) || !nodePositions.ContainsKey(road.ToNodeId))
                    continue;

                Vector3 posA = nodePositions[road.FromNodeId];
                Vector3 posB = nodePositions[road.ToNodeId];
                // Waypoints now contain ALL road points (including endpoints)
                var pts = road.Waypoints.Select(wp => new Vector3(wp[0], wp[1], 0f)).ToArray();
                if (pts.Length < 2)
                {
                    // Fallback: if somehow no points, create endpoints at nodes
                    pts = new Vector3[] { posA, posB };
                }

                CreateRoadLR(roadsParent, kvp.Key, pts.Select(p => p + toRoadSpace).ToArray(), roadMat);
                createdRoads.Add(kvp.Key);
            }

            // Create straight-line roads for node connections without explicit road data
            foreach (var kvp in zoneDef.Nodes)
            {
                foreach (var conn in kvp.Value.Connections)
                {
                    string key = kvp.Key + "-" + conn;
                    string reverseKey = conn + "-" + kvp.Key;
                    if (createdRoads.Contains(key) || createdRoads.Contains(reverseKey)) continue;
                    if (!nodePositions.ContainsKey(kvp.Key) || !nodePositions.ContainsKey(conn)) continue;

                    Vector3 posA = nodePositions[kvp.Key];
                    Vector3 posB = nodePositions[conn];
                    CreateRoadLR(roadsParent, key, new[] { posA + toRoadSpace, posB + toRoadSpace }, roadMat);
                    createdRoads.Add(key);
                }
            }
        }

        private static void CreateRoadLR(Transform parent, string name, Vector3[] pts, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = pts.Length;
            for (int i = 0; i < pts.Length; i++) lr.SetPosition(i, pts[i]);
            lr.startWidth = 0.06f;
            lr.endWidth = 0.06f;
            lr.material = mat;
            lr.startColor = Color.white;
            lr.endColor = Color.white;
            lr.sortingLayerName = _nodeSortingLayer;
            lr.sortingOrder = -5;
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
                if (lr?.sharedMaterial != null) return new Material(lr.sharedMaterial);
            }
            return null;
        }

        /// <summary>Check if a map for this zone already exists in worldTransform.</summary>
        public static bool MapExists(string zoneId, Transform worldTransform)
        {
            if (worldTransform == null) return false;
            for (int i = 0; i < worldTransform.childCount; i++)
            {
                if (worldTransform.GetChild(i).gameObject.name == zoneId)
                    return true;
            }
            return false;
        }

        /// <summary>Find a sprite by name from game Resources for mapPiece rendering (cached).</summary>
        private static readonly Dictionary<string, Sprite> _mapPieceSpriteCache = new();
        private static Sprite FindMapPieceSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            if (_mapPieceSpriteCache.TryGetValue(spriteName, out var cached)) return cached;
            // Try mod-loaded image sprites first (exact match)
            if (ModRegistry.ModImageSprites.TryGetValue(spriteName, out var modSprite))
            {
                _mapPieceSpriteCache[spriteName] = modSprite;
                return modSprite;
            }
            // Try prefixed name ("<modId>_<name>")
            string suffix = "_" + spriteName;
            foreach (var kvp in ModRegistry.ModImageSprites)
            {
                if (kvp.Value != null && kvp.Key.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    _mapPieceSpriteCache[spriteName] = kvp.Value;
                    return kvp.Value;
                }
            }
            // Search all loaded sprites for a name match
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s != null && s.name == spriteName)
                {
                    _mapPieceSpriteCache[spriteName] = s;
                    return s;
                }
            }
            _mapPieceSpriteCache[spriteName] = null;
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
