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
        private static void CacheZoneFromTransform(string zoneId, Transform zoneT)
        {
            var nodePositions = new Dictionary<string, Vector2>();
            var roads = new Dictionary<string, List<float[]>>();

            // â”€â”€ Visual Layers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Capture ALL visual elements (sprites, particles, lights, etc.)
            // from the zone hierarchy, excluding Nodes and Roads subtrees.
            {
                var layers = new List<VisualLayerDef>();
                var layerSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
                Sprite bestBgSprite = null;
                float bestBgArea = 0;

                // Check SpriteRenderer directly on the zone root (e.g. "Map" on Obelisk zones)
                var rootSr = zoneT.GetComponent<SpriteRenderer>();
                if (rootSr != null && rootSr.sprite != null)
                {
                    var layer = SpriteRendererToLayer("Map", rootSr, zoneT, zoneT);
                    layers.Add(layer);
                    layerSprites[layer.Name] = rootSr.sprite;
                    float area = rootSr.sprite.rect.width * rootSr.sprite.rect.height;
                    if (area > bestBgArea) { bestBgArea = area; bestBgSprite = rootSr.sprite; }
                }

                // Iterate all direct children of the zone root
                foreach (Transform child in zoneT)
                {
                    string childName = child.gameObject.name;
                    if (childName == "Nodes" || childName == "Roads") continue;

                    var sr = child.GetComponent<SpriteRenderer>();
                    var ps = child.GetComponent<ParticleSystem>();
                    var mask = child.GetComponent<SpriteMask>();

                    if (sr != null && sr.sprite != null)
                    {
                        // Direct sprite layer
                        var layer = SpriteRendererToLayer(childName, sr, child, zoneT);
                        layers.Add(layer);
                        layerSprites[layer.Name] = sr.sprite;
                        float area = sr.sprite.rect.width * sr.sprite.rect.height;
                        if (area > bestBgArea) { bestBgArea = area; bestBgSprite = sr.sprite; }
                    }
                    else if (ps != null)
                    {
                        // Particle system
                        layers.Add(new VisualLayerDef
                        {
                            Name = childName,
                            Type = VisualLayerType.ParticleSystem,
                            PosX = child.localPosition.x,
                            PosY = child.localPosition.y,
                            PosZ = child.localPosition.z,
                            Visible = child.gameObject.activeSelf,
                        });
                    }
                    else if (mask != null)
                    {
                        // Sprite mask
                        layers.Add(new VisualLayerDef
                        {
                            Name = childName,
                            Type = VisualLayerType.SpriteMask,
                            PosX = child.localPosition.x,
                            PosY = child.localPosition.y,
                            Visible = child.gameObject.activeSelf,
                        });
                    }
                    else
                    {
                        // Check for Light2D component
                        bool hasLight = false;
                        foreach (var comp in child.GetComponents<Component>())
                        {
                            if (comp != null && comp.GetType().Name == "Light2D")
                            { hasLight = true; break; }
                        }

                        if (hasLight)
                        {
                            layers.Add(new VisualLayerDef
                            {
                                Name = childName,
                                Type = VisualLayerType.Light,
                                PosX = child.localPosition.x,
                                PosY = child.localPosition.y,
                                PosZ = child.localPosition.z,
                                Visible = child.gameObject.activeSelf,
                            });
                        }
                        else if (child.childCount > 0)
                        {
                            // Container with children â€” scan children for sprites too
                            var containerLayer = new VisualLayerDef
                            {
                                Name = childName,
                                Type = VisualLayerType.Container,
                                PosX = child.localPosition.x,
                                PosY = child.localPosition.y,
                                PosZ = child.localPosition.z,
                                Visible = child.gameObject.activeSelf,
                            };
                            layers.Add(containerLayer);

                            // Also cache child sprites within containers
                            foreach (var childSr in child.GetComponentsInChildren<SpriteRenderer>(true))
                            {
                                if (childSr.sprite == null) continue;
                                string subName = $"{childName}/{childSr.gameObject.name}";
                                if (!layerSprites.ContainsKey(subName))
                                {
                                    var subLayer = SpriteRendererToLayer(subName, childSr, childSr.transform, zoneT);
                                    layers.Add(subLayer);
                                    layerSprites[subName] = childSr.sprite;
                                    float area = childSr.sprite.rect.width * childSr.sprite.rect.height;
                                    if (area > bestBgArea) { bestBgArea = area; bestBgSprite = childSr.sprite; }
                                }
                            }
                        }
                    }
                }

                _baseVisualLayersCache[zoneId] = layers;
                _baseLayerSpriteCache[zoneId] = layerSprites;

                // Also maintain the legacy single-bg cache for backward compat
                if (bestBgSprite != null)
                {
                    _baseZoneBgCache[zoneId] = bestBgSprite;
                    Plugin.Log.LogInfo($"[ZoneEditing] Cached {layers.Count} visual layers for '{zoneId}' (bg: {bestBgSprite.name} {bestBgSprite.rect.width}x{bestBgSprite.rect.height})");
                }
                else
                {
                    Plugin.Log.LogWarning($"[ZoneEditing] No background sprite found for '{zoneId}' ({layers.Count} non-sprite layers)");
                }

                // â”€â”€ Clone non-sprite visual GameObjects â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                // Create inactive clones for particles, animated sprites, lights, containers
                // so the viewport can Instantiate them later.
                var goCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
                foreach (Transform child in zoneT)
                {
                    string childName = child.gameObject.name;
                    if (childName == "Nodes" || childName == "Roads") continue;

                    var sr = child.GetComponent<SpriteRenderer>();
                    var ps = child.GetComponent<ParticleSystem>();
                    var anim = child.GetComponent<Animator>();
                    bool hasLight = false;
                    foreach (var comp in child.GetComponents<Component>())
                    {
                        if (comp != null && comp.GetType().Name == "Light2D")
                        { hasLight = true; break; }
                    }

                    // Clone if it has particles, animator, light, or is a container with animated/particle children
                    bool shouldClone = ps != null || anim != null || hasLight;
                    if (!shouldClone && child.childCount > 0)
                    {
                        // Check children for particle systems or animators
                        shouldClone = child.GetComponentInChildren<ParticleSystem>(true) != null
                                   || child.GetComponentInChildren<Animator>(true) != null;
                    }

                    // Also clone sprite layers that have an Animator (animated sprites like holyray)
                    if (!shouldClone && sr != null && anim != null)
                        shouldClone = true;

                    if (shouldClone)
                    {
                        try
                        {
                            if (_layerGOCacheRoot == null)
                            {
                                _layerGOCacheRoot = new GameObject("[ZoneEditing] LayerGOCache");
                                _layerGOCacheRoot.SetActive(false);
                                UnityEngine.Object.DontDestroyOnLoad(_layerGOCacheRoot);
                            }

                            var clone = UnityEngine.Object.Instantiate(child.gameObject, _layerGOCacheRoot.transform);
                            clone.name = childName;
                            clone.SetActive(false);

                            // Strip Node, MapManager, etc. components that might cause issues
                            foreach (var badComp in clone.GetComponentsInChildren<Node>(true))
                                UnityEngine.Object.DestroyImmediate(badComp);
                            foreach (var badComp in clone.GetComponentsInChildren<MapManager>(true))
                                UnityEngine.Object.DestroyImmediate(badComp);

                            goCache[childName] = clone;
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning($"[ZoneEditing] Failed to clone visual GO '{childName}' for zone '{zoneId}': {ex.Message}");
                        }
                    }
                }

                if (goCache.Count > 0)
                {
                    _baseLayerGOCache[zoneId] = goCache;
                    Plugin.Log.LogInfo($"[ZoneEditing] Cloned {goCache.Count} visual GameObjects for '{zoneId}'");
                }
            }

            // â”€â”€ Nodes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var nodesT = zoneT.Find("Nodes");
            // Cache container offsets (Nodes/Roads local positions relative to zone root)
            _nodesOffsetCache[zoneId] = nodesT != null ? new Vector2(nodesT.localPosition.x, nodesT.localPosition.y) : Vector2.zero;
            var mapPieces = new Dictionary<string, List<MapPieceDef>>(StringComparer.OrdinalIgnoreCase);
            var mapPieceSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            if (nodesT != null)
            {
                foreach (Transform nodeT in nodesT)
                {
                    string nodeId = nodeT.gameObject.name.ToLower();
                    var lp = nodeT.localPosition;
                    nodePositions[nodeId] = new Vector2(lp.x, lp.y);

                    // Scan children for mapPiece elements
                    for (int ci = 0; ci < nodeT.childCount; ci++)
                    {
                        var child = nodeT.GetChild(ci);
                        if (!child.name.Equals("mapPiece", StringComparison.OrdinalIgnoreCase)) continue;

                        var sr = child.GetComponent<SpriteRenderer>();
                        if (sr == null || sr.sprite == null) continue;

                        var mpDef = new MapPieceDef
                        {
                            SpriteName = sr.sprite.name,
                            SortingOrder = sr.sortingOrder,
                            SortingLayer = sr.sortingLayerName,
                            PosX = child.localPosition.x,
                            PosY = child.localPosition.y,
                            ScaleX = child.localScale.x,
                            ScaleY = child.localScale.y,
                            ColorR = sr.color.r,
                            ColorG = sr.color.g,
                            ColorB = sr.color.b,
                            ColorA = sr.color.a,
                            SpriteWidth = sr.sprite.rect.width,
                            SpriteHeight = sr.sprite.rect.height,
                            FlipX = sr.flipX,
                            FlipY = sr.flipY,
                        };

                        if (!mapPieces.ContainsKey(nodeId))
                            mapPieces[nodeId] = new List<MapPieceDef>();
                        mapPieces[nodeId].Add(mpDef);

                        if (!mapPieceSprites.ContainsKey(sr.sprite.name))
                            mapPieceSprites[sr.sprite.name] = sr.sprite;
                    }
                }
            }
            _mapPieceCache[zoneId] = mapPieces;
            _mapPieceSpriteCache[zoneId] = mapPieceSprites;

            // â”€â”€ Roads â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // The game stores road positions in each road GO's local space.
            // Nodes and Roads parents can have different offsets from the zone root,
            // so we transform all road points into the Nodes parent's coordinate
            // space (same as node PosX/PosY) for consistent rendering.
            var roadsT = zoneT.Find("Roads");
            _roadsOffsetCache[zoneId] = roadsT != null ? new Vector2(roadsT.localPosition.x, roadsT.localPosition.y) : Vector2.zero;
            if (roadsT != null)
            {
                // Compute offset: Roads-parent-local â†’ Nodes-parent-local
                Vector3 nodesParentPos = nodesT != null ? nodesT.localPosition : Vector3.zero;
                Vector3 roadsParentPos = roadsT.localPosition;
                Vector3 parentDelta = roadsParentPos - nodesParentPos; // add to LR positions

                foreach (Transform roadT in roadsT)
                {
                    var lr = roadT.GetComponent<LineRenderer>();
                    if (lr == null || lr.positionCount < 2) continue;

                    string key = roadT.gameObject.name;
                    var points = new List<float[]>();

                    // Store ALL LineRenderer positions (including endpoints).
                    // Transform from road GO local space â†’ Nodes parent space:
                    // nodeSpace = lrPos + roadGO.localPosition + (Roads.localPos - Nodes.localPos)
                    Vector3 roadOffset = roadT.localPosition + parentDelta;
                    for (int i = 0; i < lr.positionCount; i++)
                    {
                        Vector3 p = lr.GetPosition(i) + roadOffset;
                        points.Add(new float[] { p.x, p.y });
                    }

                    roads[key] = points;
                }
            }

            _positionCache[zoneId] = nodePositions;
            _roadCache[zoneId] = roads;

            // â”€â”€ Dump visual hierarchy (non-Node/Road elements) â”€â”€â”€â”€â”€â”€
            DumpZoneVisualHierarchy(zoneId, zoneT);

            Plugin.Log.LogInfo($"[ZoneEditing] Cached base zone '{zoneId}': {nodePositions.Count} node positions, {roads.Count} roads");
        }

        /// <summary>
        /// Log the full visual hierarchy of a zone prefab (excluding Nodes/Roads).
        /// Shows all GameObjects with their components, positions, sprites, etc.
        /// Only runs for a few example zones to avoid log spam.
        /// </summary>
        private static readonly HashSet<string> _dumpZones = new(StringComparer.OrdinalIgnoreCase)
        {
            "CastleSpire", "CastleCourtyard", "WitchWoods", "Voidhigh", "Voidlow", "Aquarfall"
        };

        private static void DumpZoneVisualHierarchy(string zoneId, Transform zoneT)
        {
            if (!_dumpZones.Contains(zoneId)) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[ZoneDump] === {zoneId} hierarchy (non-Node/Road) ===");

            foreach (Transform child in zoneT)
            {
                if (child.name == "Nodes" || child.name == "Roads")
                {
                    sb.AppendLine($"  [{child.name}] ({child.childCount} children) â€” skipped");
                    continue;
                }

                DumpTransformRecursive(sb, child, 1);
            }

            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void DumpTransformRecursive(System.Text.StringBuilder sb, Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            string active = t.gameObject.activeSelf ? "" : " [INACTIVE]";
            var lp = t.localPosition;
            var ls = t.localScale;

            // Gather component info
            var components = new List<string>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "Transform") continue;

                string extra = "";
                if (c is SpriteRenderer sr)
                {
                    string spriteName = sr.sprite != null ? sr.sprite.name : "null";
                    string spriteSize = sr.sprite != null ? $"{sr.sprite.rect.width}x{sr.sprite.rect.height}" : "?";
                    extra = $" sprite={spriteName}({spriteSize}) order={sr.sortingOrder} color={sr.color}";
                }
                else if (c is LineRenderer lr)
                {
                    extra = $" points={lr.positionCount} width={lr.startWidth:F2}";
                }
                else if (c is ParticleSystem)
                {
                    extra = " (particles)";
                }
                else if (c is Animator anim)
                {
                    extra = $" controller={anim.runtimeAnimatorController?.name ?? "null"}";
                }
                else if (c is MeshRenderer)
                {
                    extra = " (mesh)";
                }

                components.Add($"{typeName}{extra}");
            }

            string comps = components.Count > 0 ? $" â€” {string.Join(", ", components)}" : "";
            sb.AppendLine($"{indent}{t.name}{active} pos=({lp.x:F2},{lp.y:F2},{lp.z:F2}) scale=({ls.x:F2},{ls.y:F2},{ls.z:F2}){comps}");

            foreach (Transform child in t)
                DumpTransformRecursive(sb, child, depth + 1);
        }

        /// <summary>
        /// Pre-cache ALL base-game zones from MapManager. Call this when MapManager becomes
        /// available to ensure synthesis works even after leaving the map scene.
        /// </summary>
        public static void CacheAllBaseZones()
        {
            var mapMgr = MapManager.Instance;
            if (mapMgr == null || mapMgr.worldTransform == null) return;

            int cached = 0;
            foreach (Transform zoneT in mapMgr.worldTransform)
            {
                string zoneId = zoneT.gameObject.name;
                if (_positionCache.ContainsKey(zoneId)) continue;

                CacheZoneFromTransform(zoneId, zoneT);
                cached++;
            }

            // Also scan mapList prefabs
            if (mapMgr.mapList != null)
            {
                foreach (var prefab in mapMgr.mapList)
                {
                    if (prefab == null) continue;
                    string zoneId = prefab.name;
                    if (_positionCache.ContainsKey(zoneId)) continue;

                    CacheZoneFromTransform(zoneId, prefab.transform);
                    cached++;
                }
            }

            if (cached > 0)
                Plugin.Log.LogInfo($"[ZoneEditing] Pre-cached {cached} base zone(s) from MapManager.");
        }

        /// <summary>
        /// Eagerly preload the Map and Combat scenes to cache all base-game data
        /// (zone prefabs, combat backgrounds) before the user needs them.
        /// Safe to call multiple times â€” no-ops if already loaded/loading.
        /// Called from ModEditor.ToggleEditMode when the dev panel opens.
        /// </summary>
        public static void PreloadScenes()
        {
            // Map scene â†’ zone backgrounds, node positions, roads, visual layers
            if (_positionCache.Count == 0 && !_sceneLoadRequested && !_sceneLoadFailed)
            {
                Plugin.Log.LogInfo("[ZoneEditing] Preloading Map scene for base zone data...");
                CacheBaseZoneData("__preload__"); // triggers the async additive load path
            }

            // Combat scene â†’ combat background prefabs
            EntityPreviewRenderer.EnsureBgCache();

            // Event scene â†’ reply prefab, event layout template
            EntityPreviewRenderer.EnsureEventCache();
        }

        /// <summary>Get the cached background sprite for a base-game zone (from MapManager prefab).</summary>
        public static Sprite GetBaseZoneBackground(string zoneId)
        {
            _baseZoneBgCache.TryGetValue(zoneId, out var sprite);
            return sprite;
        }

        /// <summary>Get all cached visual layers for a base-game zone.</summary>
        public static List<VisualLayerDef> GetBaseVisualLayers(string zoneId)
        {
            _baseVisualLayersCache.TryGetValue(zoneId, out var layers);
            return layers;
        }

        /// <summary>Get the cached Sprite for a specific visual layer of a base-game zone.</summary>
        public static Sprite GetBaseLayerSprite(string zoneId, string layerName)
        {
            if (_baseLayerSpriteCache.TryGetValue(zoneId, out var sprites))
                if (sprites.TryGetValue(layerName, out var sprite))
                    return sprite;
            return null;
        }

        /// <summary>Get all cached layer sprites for a zone (name â†’ Sprite).</summary>
        public static Dictionary<string, Sprite> GetBaseLayerSprites(string zoneId)
        {
            _baseLayerSpriteCache.TryGetValue(zoneId, out var sprites);
            return sprites;
        }

        /// <summary>Get a cached cloned GameObject for a non-sprite visual layer.
        /// Returns null if no clone was cached for this layer. Use Object.Instantiate() on the result.</summary>
        public static GameObject GetBaseLayerGameObject(string zoneId, string layerName)
        {
            if (_baseLayerGOCache.TryGetValue(zoneId, out var dict))
                if (dict.TryGetValue(layerName, out var go))
                    return go;
            return null;
        }

        /// <summary>Get cached mapPiece definitions for a zone's nodes. Key = nodeId (lowercase).</summary>
        public static Dictionary<string, List<MapPieceDef>> GetBaseMapPieces(string zoneId)
        {
            _mapPieceCache.TryGetValue(zoneId, out var pieces);
            return pieces;
        }

        /// <summary>Get a cached mapPiece sprite by name.</summary>
        public static Sprite GetMapPieceSprite(string zoneId, string spriteName)
        {
            if (_mapPieceSpriteCache.TryGetValue(zoneId, out var sprites))
                if (sprites.TryGetValue(spriteName, out var sprite))
                    return sprite;
            return null;
        }

        /// <summary>Convert a SpriteRenderer into a VisualLayerDef.</summary>
        private static VisualLayerDef SpriteRendererToLayer(string name, SpriteRenderer sr, Transform t, Transform zoneRoot)
        {
            // Compute position relative to zone root
            Vector3 localPos = zoneRoot.InverseTransformPoint(t.position);
            Vector3 localScale = t.lossyScale;
            if (zoneRoot.lossyScale.x != 0) localScale.x /= zoneRoot.lossyScale.x;
            if (zoneRoot.lossyScale.y != 0) localScale.y /= zoneRoot.lossyScale.y;

            return new VisualLayerDef
            {
                Name = name,
                Type = VisualLayerType.Sprite,
                SpriteName = sr.sprite.name,
                SortingOrder = sr.sortingOrder,
                SortingLayer = sr.sortingLayerName,
                PosX = localPos.x,
                PosY = localPos.y,
                PosZ = localPos.z,
                ScaleX = localScale.x,
                ScaleY = localScale.y,
                ColorR = sr.color.r,
                ColorG = sr.color.g,
                ColorB = sr.color.b,
                ColorA = sr.color.a,
                SpriteWidth = sr.sprite.rect.width,
                SpriteHeight = sr.sprite.rect.height,
                PPU = sr.sprite.pixelsPerUnit,
                Visible = t.gameObject.activeSelf && sr.enabled,
                FlipX = sr.flipX,
                FlipY = sr.flipY,
            };
        }

        /// <summary>Return summary info about a cache for the runtime inspector.</summary>
        public static object GetCacheInfo(string cache)
        {
            return cache switch
            {
                "positions" => _positionCache.Keys.OrderBy(k => k).ToArray(),
                "roads" => _roadCache.Keys.OrderBy(k => k).ToArray(),
                "backgrounds" => _baseZoneBgCache.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value != null ? $"{kvp.Value.name} ({kvp.Value.rect.width}x{kvp.Value.rect.height})" : "null"),
                "layers" => _baseVisualLayersCache.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Select(l => $"{l.Name}({l.Type}, order={l.SortingOrder})").ToArray()),
                "synthesized" => _synthesizedCache.Keys.OrderBy(k => k).ToArray(),
                "failures" => _cacheFailures.OrderBy(k => k).ToArray(),
                _ => null,
            };
        }

        /// <summary>Clear all synthesis caches. Call during full rebuild.</summary>
        public static void ClearSynthesisCache()
        {
            _positionCache.Clear();
            _roadCache.Clear();
            _synthesizedCache.Clear();
            _baseZoneBgCache.Clear();
            _baseVisualLayersCache.Clear();
            _baseLayerSpriteCache.Clear();
            _nodesOffsetCache.Clear();
            _roadsOffsetCache.Clear();
            _mapPieceCache.Clear();
            _mapPieceSpriteCache.Clear();
            _cacheFailures.Clear();
            _sceneLoadRequested = false;
            _sceneLoadFailed = false;
            SynthesisStatus = "";
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  SNAPSHOT HELPERS â€” convert game SOs into lightweight defs
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private static void AddCombatRef(HashSet<string> set, string id)
        {
            if (!string.IsNullOrEmpty(id))
                set.Add(id.Replace(" ", "").ToLower());
        }

        /// <summary>Snapshot a CombatData SO into a CombatDef.</summary>
        private static CombatDef SnapshotCombatDef(CombatData c)
        {
            var d = new CombatDef
            {
                CombatId = c.CombatId ?? "",
                Description = c.Description ?? "",
                CombatTier = c.CombatTier,
                Background = c.CombatBackground,
                NpcRemoveInMadness0Index = c.NpcRemoveInMadness0Index,
                HealHeroes = c.HealHeroes,
                IsRift = c.IsRift,
                NeverRandomizeEnemies = c.NeverRandomizeEnemies,
                RandomizeNpcPosition = c.RandomizeNpcPosition,
            };

            if (c.NPCList != null)
            {
                foreach (var npc in c.NPCList)
                {
                    if (npc != null)
                        d.NpcIds.Add(npc.Id ?? "");
                }
            }

            d.NpcToSummonOnKilledId = c.NpcToSummonOnNpcKilled != null ? c.NpcToSummonOnNpcKilled.Id ?? "" : "";
            d.EventDataId = c.EventData != null ? c.EventData.EventId ?? "" : "";
            d.EventRequirementId = c.EventRequirementData != null ? c.EventRequirementData.RequirementId ?? "" : "";

            if (c.CombatEffect != null)
            {
                foreach (var eff in c.CombatEffect)
                {
                    d.CombatEffects.Add(new CombatEffectDef
                    {
                        AuraCurse = eff.AuraCurse != null ? eff.AuraCurse.name ?? "" : "",
                        Charges = eff.AuraCurseCharges,
                        Target = eff.AuraCurseTarget,
                    });
                }
            }

            return d;
        }

        /// <summary>Snapshot an EventData SO into an EventDef.</summary>
        private static EventDef SnapshotEventDef(EventData e)
        {
            var d = new EventDef
            {
                EventId = e.EventId ?? "",
                EventName = e.EventName ?? "",
                Description = e.Description ?? "",
                DescriptionAction = e.DescriptionAction ?? "",
                EventTier = e.EventTier,
                ReplyRandom = e.ReplyRandom,
            };

            if (e.Requirement != null)
                d.RequirementId = e.Requirement.RequirementId ?? "";
            if (e.RequiredClass != null)
                d.RequiredClassId = e.RequiredClass.SubClassName ?? "";
            d.EventIconShader = e.EventIconShader;
            d.HistoryMode = e.HistoryMode;

            if (e.Replys != null)
            {
                foreach (var reply in e.Replys)
                {
                    if (reply == null) continue;
                    d.Replies.Add(SnapshotReplyDef(reply));
                }
            }
            return d;
        }

        private static ReplyDef SnapshotReplyDef(EventReplyData r)
        {
            var d = new ReplyDef
            {
                ReplyText = r.ReplyText ?? "",
                Action = r.ReplyActionText,
                GoldCost = r.GoldCost,
                DustCost = r.DustCost,
            };

            if (r.Requirement != null) d.RequirementId = r.Requirement.RequirementId ?? "";
            if (r.RequirementBlocked != null) d.RequirementBlockedId = r.RequirementBlocked.RequirementId ?? "";

            // Reply-level fields
            if (r.RequiredClass != null) d.RequiredClassId = r.RequiredClass.SubClassName ?? "";
            if (r.RequirementItem != null) d.RequirementItemId = r.RequirementItem.Id ?? "";
            if (r.RequirementItems != null)
            {
                foreach (var item in r.RequirementItems)
                {
                    if (item != null && !string.IsNullOrEmpty(item.Id))
                        d.RequirementItemIds.Add(item.Id);
                }
            }
            if (r.RequirementCard != null)
            {
                foreach (var card in r.RequirementCard)
                {
                    if (card != null && !string.IsNullOrEmpty(card.Id))
                        d.RequirementCardIds.Add(card.Id);
                }
            }
            if (r.ReplyShowCard != null) d.ReplyShowCardId = r.ReplyShowCard.Id ?? "";
            d.RequirementMultiplayer = r.RequirementMultiplayer;
            d.RepeatForAllCharacters = r.RepeatForAllCharacters;
            d.RepeatForAllWarriors = r.RepeatForAllWarriors;
            d.RepeatForAllScouts = r.RepeatForAllScouts;
            d.RepeatForAllMages = r.RepeatForAllMages;
            d.RepeatForAllHealers = r.RepeatForAllHealers;

            d.HasRoll = r.SsRoll;
            d.RollDC = r.SsRollNumber;
            d.RollCrit = r.SsRollNumberCritical;
            d.RollCritFail = r.SsRollNumberCriticalFail;
            d.RollMode = r.SsRollMode;
            d.RollTarget = r.SsRollTarget;
            d.RollCard = r.SsRollCard;

            d.Ss = SnapshotOutcomeSs(r);
            d.Fl = SnapshotOutcomeFl(r);
            d.Ssc = SnapshotOutcomeSsc(r);
            d.Flc = SnapshotOutcomeFlc(r);

            return d;
        }

        private static OutcomeDef SnapshotOutcomeSs(EventReplyData r)
        {
            var d = new OutcomeDef();
            d.Text = r.SsRewardText ?? "";
            d.HealPercent = r.SsRewardHealthPercent;
            d.HealFlat = r.SsRewardHealthFlat;
            d.Gold = r.SsGoldReward;
            d.Dust = r.SsDustReward;
            d.Supply = r.SsSupplyReward;
            d.XP = r.SsExperienceReward;
            d.CombatId = r.SsCombat != null ? r.SsCombat.CombatId ?? "" : "";
            d.EventId = r.SsEvent != null ? r.SsEvent.EventId ?? "" : "";
            d.NodeTravelId = r.SsNodeTravel != null ? r.SsNodeTravel.NodeId ?? "" : "";
            d.RequirementUnlockId = r.SsRequirementUnlock != null ? r.SsRequirementUnlock.RequirementId ?? "" : "";
            d.RequirementUnlock2Id = r.SsRequirementUnlock2 != null ? r.SsRequirementUnlock2.RequirementId ?? "" : "";
            d.RequirementLockId = r.SsRequirementLock != null ? r.SsRequirementLock.RequirementId ?? "" : "";
            d.RequirementLock2Id = r.SsRequirementLock2 != null ? r.SsRequirementLock2.RequirementId ?? "" : "";
            d.LootId = r.SsLootList != null ? r.SsLootList.Id ?? "" : "";
            d.ShopId = r.SsShopList != null ? r.SsShopList.Id ?? "" : "";
            d.AddItemId = r.SsAddItem != null ? r.SsAddItem.Id ?? "" : "";
            d.AddCard1Id = r.SsAddCard1 != null ? r.SsAddCard1.Id ?? "" : "";
            d.AddCard2Id = r.SsAddCard2 != null ? r.SsAddCard2.Id ?? "" : "";
            d.AddCard3Id = r.SsAddCard3 != null ? r.SsAddCard3.Id ?? "" : "";
            d.RewardTier = r.SsRewardTier != null ? r.SsRewardTier.name ?? "" : "";
            d.Discount = r.SsDiscount;
            d.MaxQuantity = r.SsMaxQuantity;
            d.HealerUI = r.SsHealerUI;
            d.UpgradeUI = r.SsUpgradeUI;
            d.CraftUI = r.SsCraftUI;
            d.MerchantUI = r.SsMerchantUI;
            d.CorruptionUI = r.SsCorruptionUI;
            d.UpgradeRandomCard = r.SsUpgradeRandomCard;
            d.FinishGame = r.SsFinishGame;
            d.FinishObeliskMap = r.SsFinishObeliskMap;
            d.CraftUIMaxType = r.SsCraftUIMaxType;
            d.ItemCorruptionUI = r.SsItemCorruptionUI;
            d.RemoveItemSlot = r.SsRemoveItemSlot;
            d.CorruptItemSlot = r.SsCorruptItemSlot;
            if (r.SsUnlockClass != null) d.UnlockClassId = r.SsUnlockClass.SubClassName ?? "";
            d.CardPlayerGame = r.SsCardPlayerGame;
            if (r.SsCardPlayerGamePackData != null) d.CardPlayerGamePackId = r.SsCardPlayerGamePackData.PackId ?? "";
            d.CardPlayerPairsGame = r.SsCardPlayerPairsGame;
            if (r.SsCardPlayerPairsGamePackData != null) d.CardPlayerPairsGamePackId = r.SsCardPlayerPairsGamePackData.PackId ?? "";
            d.UnlockSteamAchievement = r.SsUnlockSteamAchievement ?? "";
            if (r.SsCharacterReplacement != null) d.CharacterReplacementId = r.SsCharacterReplacement.SubClassName ?? "";
            d.CharacterReplacementPosition = r.SsCharacterReplacementPosition;
            return d;
        }

        private static OutcomeDef SnapshotOutcomeFl(EventReplyData r)
        {
            var d = new OutcomeDef();
            d.Text = r.FlRewardText ?? "";
            d.HealPercent = r.FlRewardHealthPercent;
            d.HealFlat = r.FlRewardHealthFlat;
            d.Gold = r.FlGoldReward;
            d.Dust = r.FlDustReward;
            d.Supply = r.FlSupplyReward;
            d.XP = r.FlExperienceReward;
            d.CombatId = r.FlCombat != null ? r.FlCombat.CombatId ?? "" : "";
            d.EventId = r.FlEvent != null ? r.FlEvent.EventId ?? "" : "";
            d.NodeTravelId = r.FlNodeTravel != null ? r.FlNodeTravel.NodeId ?? "" : "";
            d.RequirementUnlockId = r.FlRequirementUnlock != null ? r.FlRequirementUnlock.RequirementId ?? "" : "";
            d.RequirementUnlock2Id = r.FlRequirementUnlock2 != null ? r.FlRequirementUnlock2.RequirementId ?? "" : "";
            d.RequirementLockId = r.FlRequirementLock != null ? r.FlRequirementLock.RequirementId ?? "" : "";
            // Fl has no RequirementLock2
            d.LootId = r.FlLootList != null ? r.FlLootList.Id ?? "" : "";
            d.ShopId = r.FlShopList != null ? r.FlShopList.Id ?? "" : "";
            d.AddItemId = r.FlAddItem != null ? r.FlAddItem.Id ?? "" : "";
            d.AddCard1Id = r.FlAddCard1 != null ? r.FlAddCard1.Id ?? "" : "";
            d.AddCard2Id = r.FlAddCard2 != null ? r.FlAddCard2.Id ?? "" : "";
            d.AddCard3Id = r.FlAddCard3 != null ? r.FlAddCard3.Id ?? "" : "";
            d.RewardTier = r.FlRewardTier != null ? r.FlRewardTier.name ?? "" : "";
            d.Discount = r.FlDiscount;
            d.MaxQuantity = r.FlMaxQuantity;
            d.HealerUI = r.FlHealerUI;
            d.UpgradeUI = r.FlUpgradeUI;
            d.CraftUI = r.FlCraftUI;
            d.MerchantUI = r.FlMerchantUI;
            d.CorruptionUI = r.FlCorruptionUI;
            d.UpgradeRandomCard = r.FlUpgradeRandomCard;
            // Fl has no FinishGame / FinishObeliskMap
            d.CraftUIMaxType = r.FlCraftUIMaxType;
            d.ItemCorruptionUI = r.FlItemCorruptionUI;
            d.RemoveItemSlot = r.FlRemoveItemSlot;
            d.CorruptItemSlot = r.FlCorruptItemSlot;
            if (r.FlUnlockClass != null) d.UnlockClassId = r.FlUnlockClass.SubClassName ?? "";
            d.CardPlayerGame = r.FlCardPlayerGame;
            if (r.FlCardPlayerGamePackData != null) d.CardPlayerGamePackId = r.FlCardPlayerGamePackData.PackId ?? "";
            d.CardPlayerPairsGame = r.FlCardPlayerPairsGame;
            if (r.FlCardPlayerPairsGamePackData != null) d.CardPlayerPairsGamePackId = r.FlCardPlayerPairsGamePackData.PackId ?? "";
            d.UnlockSteamAchievement = r.FlUnlockSteamAchievement ?? "";
            return d;
        }

        private static OutcomeDef SnapshotOutcomeSsc(EventReplyData r)
        {
            var d = new OutcomeDef();
            d.Text = r.SscRewardText ?? "";
            d.HealPercent = r.SscRewardHealthPercent;
            d.HealFlat = r.SscRewardHealthFlat;
            d.Gold = r.SscGoldReward;
            d.Dust = r.SscDustReward;
            d.Supply = r.SscSupplyReward;
            d.XP = r.SscExperienceReward;
            d.CombatId = r.SscCombat != null ? r.SscCombat.CombatId ?? "" : "";
            d.EventId = r.SscEvent != null ? r.SscEvent.EventId ?? "" : "";
            d.NodeTravelId = r.SscNodeTravel != null ? r.SscNodeTravel.NodeId ?? "" : "";
            d.RequirementUnlockId = r.SscRequirementUnlock != null ? r.SscRequirementUnlock.RequirementId ?? "" : "";
            d.RequirementUnlock2Id = r.SscRequirementUnlock2 != null ? r.SscRequirementUnlock2.RequirementId ?? "" : "";
            d.RequirementLockId = r.SscRequirementLock != null ? r.SscRequirementLock.RequirementId ?? "" : "";
            // Ssc has no RequirementLock2
            d.LootId = r.SscLootList != null ? r.SscLootList.Id ?? "" : "";
            d.ShopId = r.SscShopList != null ? r.SscShopList.Id ?? "" : "";
            d.AddItemId = r.SscAddItem != null ? r.SscAddItem.Id ?? "" : "";
            d.AddCard1Id = r.SscAddCard1 != null ? r.SscAddCard1.Id ?? "" : "";
            d.AddCard2Id = r.SscAddCard2 != null ? r.SscAddCard2.Id ?? "" : "";
            d.AddCard3Id = r.SscAddCard3 != null ? r.SscAddCard3.Id ?? "" : "";
            d.RewardTier = r.SscRewardTier != null ? r.SscRewardTier.name ?? "" : "";
            d.Discount = r.SscDiscount;
            d.MaxQuantity = r.SscMaxQuantity;
            d.HealerUI = r.SscHealerUI;
            d.UpgradeUI = r.SscUpgradeUI;
            d.CraftUI = r.SscCraftUI;
            d.MerchantUI = r.SscMerchantUI;
            d.CorruptionUI = r.SscCorruptionUI;
            d.UpgradeRandomCard = r.SscUpgradeRandomCard;
            d.FinishGame = r.SscFinishGame;
            // Ssc has no FinishObeliskMap
            d.CraftUIMaxType = r.SscCraftUIMaxType;
            d.ItemCorruptionUI = r.SscItemCorruptionUI;
            d.RemoveItemSlot = r.SscRemoveItemSlot;
            d.CorruptItemSlot = r.SscCorruptItemSlot;
            if (r.SscUnlockClass != null) d.UnlockClassId = r.SscUnlockClass.SubClassName ?? "";
            d.CardPlayerGame = r.SscCardPlayerGame;
            if (r.SscCardPlayerGamePackData != null) d.CardPlayerGamePackId = r.SscCardPlayerGamePackData.PackId ?? "";
            d.CardPlayerPairsGame = r.SscCardPlayerPairsGame;
            if (r.SscCardPlayerPairsGamePackData != null) d.CardPlayerPairsGamePackId = r.SscCardPlayerPairsGamePackData.PackId ?? "";
            d.UnlockSteamAchievement = r.SscUnlockSteamAchievement ?? "";
            return d;
        }

        private static OutcomeDef SnapshotOutcomeFlc(EventReplyData r)
        {
            var d = new OutcomeDef();
            d.Text = r.FlcRewardText ?? "";
            d.HealPercent = r.FlcRewardHealthPercent;
            d.HealFlat = r.FlcRewardHealthFlat;
            d.Gold = r.FlcGoldReward;
            d.Dust = r.FlcDustReward;
            d.Supply = r.FlcSupplyReward;
            d.XP = r.FlcExperienceReward;
            d.CombatId = r.FlcCombat != null ? r.FlcCombat.CombatId ?? "" : "";
            d.EventId = r.FlcEvent != null ? r.FlcEvent.EventId ?? "" : "";
            d.NodeTravelId = r.FlcNodeTravel != null ? r.FlcNodeTravel.NodeId ?? "" : "";
            d.RequirementUnlockId = r.FlcRequirementUnlock != null ? r.FlcRequirementUnlock.RequirementId ?? "" : "";
            d.RequirementUnlock2Id = r.FlcRequirementUnlock2 != null ? r.FlcRequirementUnlock2.RequirementId ?? "" : "";
            d.RequirementLockId = r.FlcRequirementLock != null ? r.FlcRequirementLock.RequirementId ?? "" : "";
            // Flc has no RequirementLock2
            d.LootId = r.FlcLootList != null ? r.FlcLootList.Id ?? "" : "";
            d.ShopId = r.FlcShopList != null ? r.FlcShopList.Id ?? "" : "";
            d.AddItemId = r.FlcAddItem != null ? r.FlcAddItem.Id ?? "" : "";
            d.AddCard1Id = r.FlcAddCard1 != null ? r.FlcAddCard1.Id ?? "" : "";
            d.AddCard2Id = r.FlcAddCard2 != null ? r.FlcAddCard2.Id ?? "" : "";
            d.AddCard3Id = r.FlcAddCard3 != null ? r.FlcAddCard3.Id ?? "" : "";
            d.RewardTier = r.FlcRewardTier != null ? r.FlcRewardTier.name ?? "" : "";
            d.Discount = r.FlcDiscount;
            d.MaxQuantity = r.FlcMaxQuantity;
            d.HealerUI = r.FlcHealerUI;
            d.UpgradeUI = r.FlcUpgradeUI;
            d.CraftUI = r.FlcCraftUI;
            d.MerchantUI = r.FlcMerchantUI;
            d.CorruptionUI = r.FlcCorruptionUI;
            d.UpgradeRandomCard = r.FlcUpgradeRandomCard;
            // Flc has no FinishGame / FinishObeliskMap
            d.CraftUIMaxType = r.FlcCraftUIMaxType;
            d.ItemCorruptionUI = r.FlcItemCorruptionUI;
            d.RemoveItemSlot = r.FlcRemoveItemSlot;
            d.CorruptItemSlot = r.FlcCorruptItemSlot;
            if (r.FlcUnlockClass != null) d.UnlockClassId = r.FlcUnlockClass.SubClassName ?? "";
            d.CardPlayerGame = r.FlcCardPlayerGame;
            if (r.FlcCardPlayerGamePackData != null) d.CardPlayerGamePackId = r.FlcCardPlayerGamePackData.PackId ?? "";
            d.CardPlayerPairsGame = r.FlcCardPlayerPairsGame;
            if (r.FlcCardPlayerPairsGamePackData != null) d.CardPlayerPairsGamePackId = r.FlcCardPlayerPairsGamePackData.PackId ?? "";
            d.UnlockSteamAchievement = r.FlcUnlockSteamAchievement ?? "";
            return d;
        }

    }
}
