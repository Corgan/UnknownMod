using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Definitions;
using UnknownMod.Editor;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    public static partial class ZoneEditingService
    {
        // 
        //  ZONE PATCH SYNTHESIS  build a full ZoneDef from base + patch
        // 

        // Cache: zoneId  { nodeId  (posX, posY) }
        private static readonly Dictionary<string, Dictionary<string, Vector2>> _positionCache = new(StringComparer.OrdinalIgnoreCase);
        // Cache: zoneId  { roadKey  all road points[] (in Nodes-parent-relative coords) }
        private static readonly Dictionary<string, Dictionary<string, List<float[]>>> _roadCache = new(StringComparer.OrdinalIgnoreCase);
        // Cache: synthesized ZoneDefs so we don't rebuild every frame
        private static readonly Dictionary<string, ZoneDef> _synthesizedCache = new(StringComparer.OrdinalIgnoreCase);
        // Cache: base-game zone background sprites
        private static readonly Dictionary<string, Sprite> _baseZoneBgCache = new(StringComparer.OrdinalIgnoreCase);
        // Cache: base-game zone visual layers (all sprites, particles, lights, etc.)
        private static readonly Dictionary<string, List<VisualLayerDef>> _baseVisualLayersCache = new(StringComparer.OrdinalIgnoreCase);
        // Cache: base-game zone visual layer sprites (Name  Sprite) for rendering
        private static readonly Dictionary<string, Dictionary<string, Sprite>> _baseLayerSpriteCache = new(StringComparer.OrdinalIgnoreCase);
        // Cache: cloned GameObjects for non-sprite visual layers (particles, animators, containers, lights)
        // These are inactive DontDestroyOnLoad clones that can be Instantiate'd into preview scenes.
        private static readonly Dictionary<string, Dictionary<string, GameObject>> _baseLayerGOCache = new(StringComparer.OrdinalIgnoreCase);
        private static GameObject _layerGOCacheRoot;
        // Cache: container offsets  zoneId  (nodesOffset, roadsOffset)
        private static readonly Dictionary<string, Vector2> _nodesOffsetCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Vector2> _roadsOffsetCache = new(StringComparer.OrdinalIgnoreCase);
        // Cache: base-game mapPiece data per node: zoneId  { nodeId  List<MapPieceDef> }
        private static readonly Dictionary<string, Dictionary<string, List<MapPieceDef>>> _mapPieceCache = new(StringComparer.OrdinalIgnoreCase);
        // Cache: mapPiece sprites: zoneId  { spriteName  Sprite }
        private static readonly Dictionary<string, Dictionary<string, Sprite>> _mapPieceSpriteCache = new(StringComparer.OrdinalIgnoreCase);
        // Negative cache: zones we've already tried and failed to cache
        private static readonly HashSet<string> _cacheFailures = new(StringComparer.OrdinalIgnoreCase);
        // Additive scene load state
        private static bool _sceneLoadRequested;
        private static bool _sceneLoadFailed;
        /// <summary>
        /// Ref-counted flag checked by Harmony patches to suppress MapManager/MatchManager/SceneStatic
        /// during additive scene loads. Incremented when a scene load starts, decremented when unloaded.
        /// Patches check > 0.
        /// </summary>
        internal static int SuppressSceneLoad;

        /// <summary>
        /// Human-readable status of the zone synthesis pipeline.
        /// Useful for showing a loading indicator in the viewport.
        /// </summary>
        public static string SynthesisStatus { get; private set; } = "";

        /// <summary>
        /// Synthesize a full ZoneDef from a base-game zone + a ZonePatchDef overlay.
        /// Returns the synthesized zone, or null if base data isn't available yet
        /// (e.g. MapManager hasn't loaded).
        /// </summary>
        public static ZoneDef SynthesizeZoneDef(ZonePatchDef patch)
        {
            if (patch == null) return null;
            string zoneId = patch.TargetZoneId;

            // Return cached synthesis if patch hasn't changed
            if (_synthesizedCache.TryGetValue(zoneId, out var cached))
            {
                SynthesisStatus = "";
                return cached;
            }

            // Ensure position/road cache is populated for this zone
            if (!_positionCache.ContainsKey(zoneId))
            {
                // Don't re-attempt if we already permanently failed (avoids per-frame perf hit)
                if (_cacheFailures.Contains(zoneId))
                {
                    SynthesisStatus = $"Failed to load base data for '{zoneId}'.";
                    return null;
                }

                if (!CacheBaseZoneData(zoneId))
                {
                    // Only mark as permanent failure if async load already completed and zone still missing.
                    // While async is in-progress (_sceneLoadRequested && !_sceneLoadFailed), just return null
                    // without caching failure  caller will retry next frame.
                    if (_sceneLoadFailed)
                    {
                        _cacheFailures.Add(zoneId);
                        SynthesisStatus = $"Failed to load base data for '{zoneId}'.";
                        Plugin.Log.LogWarning($"[ZoneEditing] Cannot synthesize '{zoneId}': base zone data not available.");
                    }
                    else
                    {
                        SynthesisStatus = $"Loading base zone data for '{zoneId}'...";
                    }
                    return null;
                }
            }

            var positions = _positionCache[zoneId];
            var baseRoads = _roadCache.ContainsKey(zoneId) ? _roadCache[zoneId] : new Dictionary<string, List<float[]>>();

            // Get base zone metadata from Globals
            var zoneData = DataHelper.GetExistingZone(zoneId);

            var synth = new ZoneDef
            {
                ZoneId = zoneId,
                ZoneName = zoneData?.ZoneName ?? zoneId,
                IdPrefix = patch.DetectedPrefix.TrimEnd('_'),
            };

            // Zone-level fields from ZoneData
            if (zoneData != null)
            {
                synth.ObeliskLow = zoneData.ObeliskLow;
                synth.ObeliskHigh = zoneData.ObeliskHigh;
                synth.ObeliskFinal = zoneData.ObeliskFinal;
                synth.DisableExperience = zoneData.DisableExperienceOnThisZone;
                synth.DisableMadness = zoneData.DisableMadnessOnThisZone;
                synth.Sku = zoneData.Sku ?? "";
                synth.ChangeTeamOnEntrance = zoneData.ChangeTeamOnEntrance;
                synth.RestoreTeamOnExit = zoneData.RestoreTeamOnExit;
                if (zoneData.CombatBackground != null)
                    synth.CombatBackgroundSprite = zoneData.CombatBackground.name;
                if (zoneData.NewTeam != null)
                {
                    foreach (var sc in zoneData.NewTeam)
                    {
                        if (sc != null && !string.IsNullOrEmpty(sc.SubClassName))
                            synth.NewTeam.Add(sc.SubClassName);
                    }
                }
            }

            // Container offsets
            if (_nodesOffsetCache.TryGetValue(zoneId, out var nodesOff))
            {
                synth.NodesOffsetX = nodesOff.x;
                synth.NodesOffsetY = nodesOff.y;
            }
            if (_roadsOffsetCache.TryGetValue(zoneId, out var roadsOff))
            {
                synth.RoadsOffsetX = roadsOff.x;
                synth.RoadsOffsetY = roadsOff.y;
            }

            //  Populate base-game nodes from Globals + position cache 
            var nodeDict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, NodeData>>("_NodeDataSource").Value;

            if (nodeDict != null)
            {
                foreach (var kvp in nodeDict)
                {
                    var nd = kvp.Value;
                    if (nd?.NodeZone == null) continue;
                    if (!nd.NodeZone.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase)) continue;

                    string nodeId = nd.NodeId;

                    // Skip nodes that the patch will override
                    if (patch.Nodes.ContainsKey(nodeId)) continue;

                    var nodeDef = new NodeDef
                    {
                        NodeId = nodeId,
                        NodeName = nd.NodeName ?? "",
                        Description = nd.Description ?? "",
                        TravelDestination = nd.TravelDestination,
                        GoToTown = nd.GoToTown,
                        ExistsPercent = nd.ExistsPercent,
                        ExistsSku = nd.ExistsSku ?? "",
                        DisableCorruption = nd.DisableCorruption,
                        DisableRandom = nd.DisableRandom,
                        NodeGround = nd.NodeGround,
                        VisibleIfNotRequirement = nd.VisibleIfNotRequirement,
                    };

                    // Position from cache
                    if (positions.TryGetValue(nodeId, out var pos))
                    {
                        nodeDef.PosX = pos.x;
                        nodeDef.PosY = pos.y;
                    }

                    // Connections
                    if (nd.NodesConnected != null)
                    {
                        foreach (var conn in nd.NodesConnected)
                        {
                            if (conn != null && !string.IsNullOrEmpty(conn.NodeId))
                                nodeDef.Connections.Add(conn.NodeId);
                        }
                    }

                    // Connection requirements
                    if (nd.NodesConnectedRequirement != null)
                    {
                        foreach (var req in nd.NodesConnectedRequirement)
                        {
                            if (req == null) continue;
                            var reqDef = new NodeConnectionReqDef
                            {
                                TargetNodeId = req.NodeData?.NodeId ?? "",
                                RequirementId = req.ConectionRequeriment?.RequirementId ?? "",
                                IfNotNodeId = req.ConectionIfNotNode?.NodeId ?? "",
                            };
                            nodeDef.ConnectionRequirements.Add(reqDef);
                        }
                    }

                    // Combat (all entries, not just first)
                    if (nd.NodeCombat != null && nd.NodeCombat.Length > 0)
                    {
                        foreach (var combat in nd.NodeCombat)
                        {
                            if (combat != null && !string.IsNullOrEmpty(combat.CombatId))
                                nodeDef.CombatIds.Add(combat.CombatId);
                        }
                        nodeDef.CombatPercent = nd.CombatPercent;
                        nodeDef.CombatTier = nd.NodeCombatTier;
                    }

                    // Event (all entries, not just first, plus parallel priority/percent arrays)
                    if (nd.NodeEvent != null && nd.NodeEvent.Length > 0)
                    {
                        foreach (var evt in nd.NodeEvent)
                        {
                            if (evt != null && !string.IsNullOrEmpty(evt.EventId))
                                nodeDef.EventIds.Add(evt.EventId);
                        }
                        nodeDef.EventPercent = nd.EventPercent;
                        nodeDef.NodeEventTier = nd.NodeEventTier;

                        if (nd.NodeEventPriority != null)
                            nodeDef.NodeEventPriority = new List<int>(nd.NodeEventPriority);
                        if (nd.NodeEventPercent != null)
                            nodeDef.NodeEventPercent = new List<int>(nd.NodeEventPercent);
                    }

                    // Requirement
                    if (nd.NodeRequirement != null && !string.IsNullOrEmpty(nd.NodeRequirement.RequirementId))
                        nodeDef.NodeRequirementId = nd.NodeRequirement.RequirementId;

                    // MapPieces from cache
                    if (_mapPieceCache.TryGetValue(zoneId, out var mpCache) && mpCache.TryGetValue(nodeId, out var mps))
                        nodeDef.MapPieces = new List<MapPieceDef>(mps);

                    synth.Nodes[nodeId] = nodeDef;
                }
            }

            //  Merge in patch nodes 
            foreach (var kvp in patch.Nodes)
                synth.Nodes[kvp.Key] = kvp.Value;

            //  Populate base-game roads from cache 
            foreach (var kvp in baseRoads)
            {
                string key = kvp.Key;
                int dash = key.IndexOf('-');
                if (dash < 0) continue;
                string fromId = key.Substring(0, dash);
                string toId = key.Substring(dash + 1);
                synth.Roads[key] = new RoadDef
                {
                    FromNodeId = fromId,
                    ToNodeId = toId,
                    Waypoints = new List<float[]>(kvp.Value),
                };
            }

            //  Merge in patch roads 
            foreach (var kvp in patch.Roads)
                synth.Roads[kvp.Key] = kvp.Value;

            _synthesizedCache[zoneId] = synth;
            SynthesisStatus = "";
            Plugin.Log.LogInfo($"[ZoneEditing] Synthesized ZoneDef for '{zoneId}': {synth.Nodes.Count} nodes, {synth.Roads.Count} roads");
            return synth;
        }

        /// <summary>Invalidate the synthesized cache for a zone (call when patch changes).</summary>
        public static void InvalidateSynthesizedZone(string zoneId)
        {
            _synthesizedCache.Remove(zoneId);
        }

        /// <summary>
        /// Cache base-game node positions and road waypoints by scanning the MapManager's
        /// worldTransform hierarchy. Returns false if MapManager is not available.
        /// </summary>
        private static bool CacheBaseZoneData(string zoneId)
        {
            // Try MapManager.Instance.worldTransform
            var mapMgr = MapManager.Instance;
            if (mapMgr != null && mapMgr.worldTransform != null)
            {
                foreach (Transform zoneT in mapMgr.worldTransform)
                {
                    if (!zoneT.gameObject.name.Equals(zoneId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    CacheZoneFromTransform(zoneId, zoneT);
                    return true;
                }
            }

            // Try mapList prefabs (serialized zone prefabs, available even before map loads)
            if (mapMgr != null && mapMgr.mapList != null)
            {
                foreach (var prefab in mapMgr.mapList)
                {
                    if (prefab == null) continue;
                    if (!prefab.name.Equals(zoneId, StringComparison.OrdinalIgnoreCase)) continue;

                    CacheZoneFromTransform(zoneId, prefab.transform);
                    return true;
                }
            }

            //  Async additive scene load: load the Map scene to access
            // MapManager's serialized mapList, extract zone data, then unload.
            // Harmony patches suppress MapManager.Awake/Start and SceneStatic.LoadByName.
            // Uses the same async pattern as EntityPreviewRenderer's Combat scene load.
            if (!_sceneLoadRequested && !_sceneLoadFailed)
            {
                _sceneLoadRequested = true;
                SuppressSceneLoad++;

                SceneManager.sceneLoaded += OnMapSceneLoaded;

                try
                {
                    Plugin.Log.LogInfo($"[ZoneEditing] Loading Map scene (additive, async) for base zone data (requested by '{zoneId}')...");
                    SceneManager.LoadSceneAsync("Map", LoadSceneMode.Additive);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ZoneEditing] Map scene load failed: {ex.Message}");
                    SceneManager.sceneLoaded -= OnMapSceneLoaded;
                    _sceneLoadFailed = true;
                    SuppressSceneLoad--;
                }
            }

            // Data may not be available yet (async); caller retries next frame
            if (_positionCache.ContainsKey(zoneId))
                return true;

            return false;
        }

        /// <summary>
        /// Callback fired when the Map scene finishes loading. Extract all zone data,
        /// destroy MonoBehaviours, then unload.
        /// </summary>
        private static void OnMapSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Map") return;
            SceneManager.sceneLoaded -= OnMapSceneLoaded;

            try
            {
                var rootObjects = scene.GetRootGameObjects();
                int scanned = 0;

                // Deactivate all roots to prevent Start/OnEnable on other components
                foreach (var root in rootObjects)
                    root.SetActive(false);

                Plugin.Log.LogInfo($"[ZoneEditing] Map scene loaded: {rootObjects.Length} root objects (all deactivated)");

                // Cache EventManager data (replyPrefab, layout template) from the Map scene
                EntityPreviewRenderer.CacheEventDataFromScene(rootObjects);

                // Find MapManager and scan its mapList + worldTransform
                foreach (var root in rootObjects)
                {
                    var mm = root.GetComponentInChildren<MapManager>(true);
                    if (mm == null) continue;

                    Plugin.Log.LogInfo($"[ZoneEditing]   Found MapManager, mapList={mm.mapList?.Count ?? 0}");

                    if (mm.mapList != null)
                    {
                        foreach (var prefab in mm.mapList)
                        {
                            if (prefab == null) continue;
                            string id = prefab.name;
                            if (_positionCache.ContainsKey(id)) continue;

                            var nodesChild = prefab.transform.Find("Nodes");
                            if (nodesChild == null || nodesChild.childCount == 0) continue;

                            CacheZoneFromTransform(id, prefab.transform);
                            scanned++;
                        }
                    }

                    if (mm.worldTransform != null)
                    {
                        foreach (Transform child in mm.worldTransform)
                        {
                            string id = child.gameObject.name;
                            if (_positionCache.ContainsKey(id)) continue;

                            var nodesChild = child.Find("Nodes");
                            if (nodesChild == null || nodesChild.childCount == 0) continue;

                            CacheZoneFromTransform(id, child);
                            scanned++;
                        }
                    }

                    break;
                }

                // Fallback: scan root objects directly
                if (scanned == 0)
                {
                    Plugin.Log.LogInfo("[ZoneEditing]   MapManager scan found nothing, scanning root objects...");
                    foreach (var root in rootObjects)
                    {
                        var nodesChild = root.transform.Find("Nodes");
                        if (nodesChild == null || nodesChild.childCount == 0) continue;

                        string id = root.name;
                        if (_positionCache.ContainsKey(id)) continue;

                        CacheZoneFromTransform(id, root.transform);
                        scanned++;
                    }
                }

                if (scanned > 0)
                {
                    Plugin.Log.LogInfo($"[ZoneEditing] Cached {scanned} base zone(s) from Map scene.");

                    // Invalidate synthesized cache and failure list so patch zones
                    // re-synthesize with the now-available base data on next frame.
                    _synthesizedCache.Clear();
                    _cacheFailures.Clear();

                    // Force CurrentZone re-evaluation: clear it so DrawPanel re-sets it
                    CurrentZone = null;
                }
                else
                {
                    Plugin.Log.LogWarning("[ZoneEditing] Map scene load found 0 zone prefabs.");
                    _sceneLoadFailed = true;
                }

                // Destroy ALL MonoBehaviours so nothing fires Start/coroutines
                int destroyed = 0;
                foreach (var root in rootObjects)
                {
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        UnityEngine.Object.DestroyImmediate(mb);
                        destroyed++;
                    }
                }
                Plugin.Log.LogInfo($"[ZoneEditing]   Destroyed {destroyed} MonoBehaviours.");

                // Unload the scene; release suppression when unload completes
                SceneManager.sceneUnloaded += OnMapSceneUnloaded;
                SceneManager.UnloadSceneAsync(scene);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ZoneEditing] Map scene extraction failed: {ex.Message}\n{ex.StackTrace}");
                _sceneLoadFailed = true;
                // Ensure the scene is unloaded even on failure
                try
                {
                    SceneManager.sceneUnloaded += OnMapSceneUnloaded;
                    SceneManager.UnloadSceneAsync(scene);
                }
                catch
                {
                    SuppressSceneLoad--;
                }
            }
        }

        private static void OnMapSceneUnloaded(Scene scene)
        {
            if (scene.name != "Map") return;
            SceneManager.sceneUnloaded -= OnMapSceneUnloaded;
            SuppressSceneLoad--;
            Plugin.Log.LogInfo("[ZoneEditing] Map scene unloaded, suppression released.");
        }

        /// <summary>Extract node positions, road waypoints, and visual layers from a zone Transform hierarchy.</summary>
    }
}
