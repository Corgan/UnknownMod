using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;


namespace UnknownMod.Editor
{
    public partial class MapEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  PREVIEW SCENE (build / destroy preview from ZoneDef)
        // ═══════════════════════════════════════════════════════════════

        private void SpawnMap(ZoneDef zone)
        {
            DestroyPreview();
            _loadedZoneId = zone.ZoneId;

            // Root
            _previewRoot = new GameObject($"[MapEditor] {zone.ZoneId}");
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

            // Roads + Waypoint containers
            var roadsGO = new GameObject("Roads");
            roadsGO.transform.SetParent(_previewRoot.transform, false);
            _roadsContainer = roadsGO.transform;
            var wpGO = new GameObject("WaypointHandles");
            wpGO.transform.SetParent(_previewRoot.transform, false);
            wpGO.SetActive(_showWaypoints);

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

            // Register Roads container as a layer entry
            var roadsLayerDef = new VisualLayerDef
            {
                Name = "[Roads]",
                Type = VisualLayerType.Container,
                PosX = zone.RoadsOffsetX,
                PosY = zone.RoadsOffsetY,
                PosZ = 0f,
                Visible = true,
            };
            _activeLayers.Add(roadsLayerDef);
            _layerGOs["[Roads]"] = roadsGO;

            // Initialize road material + RoadEditor
            // Use the game's arrowSquares material for authentic animated road appearance
            var roadMat = FindArrowSquaresMaterial() ?? new Material(Shader.Find("Sprites/Default"));
            _roads = new RoadEditor(new RoadEditor.Config
            {
                RoadColor = RoadColor,
                RoadWidth = 0.1f,
                RoadSortingOrder = 1,
                SortingLayer = "Map",
                UseWorldSpace = true,
                WaypointColor = WaypointHandleColor,
                WaypointSortingOrder = 50,
                WaypointZ = PreviewOrigin.z - 1f,
            });
            _roads.Init(roadsGO.transform, wpGO.transform, roadMat, GetNodeWorldPos);

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
                    if (_roads.RoadWaypoints.ContainsKey(key) || _roads.RoadWaypoints.ContainsKey(reverseKey)) continue;
                    if (!zone.Nodes.ContainsKey(conn)) continue;

                    // No explicit road data — create a straight line between nodes
                    Vector3 posA = GetNodeWorldPos(kvp.Key);
                    Vector3 posB = GetNodeWorldPos(conn);
                    _roads.AddRoadFromData(key, kvp.Key, conn, new List<Vector3> { posA, posB });
                }
            }

            _roads.RebuildAllWaypointHandles();

            _vp.ResetView(5.4f);
            _dragNodeId = null;
            _dragWPRoadKey = null;
            _connectFirstId = null;

            Plugin.Log.LogInfo($"[MapEditor] Spawned map for '{zone.ZoneId}': {_nodeGOs.Count} nodes, {_roads.RoadWaypoints.Count} roads, {_activeLayers.Count} layers");
        }

        // ═══════════════════════════════════════════════════════════════
        //  VISUAL LAYERS
        // ═══════════════════════════════════════════════════════════════

        private void SpawnVisualLayers(ZoneDef zone)
        {
            // 1. Collect base-game layers (from cache)
            var baseLayers = ZoneEditingService.GetBaseVisualLayers(zone.ZoneId);
            var baseSprites = ZoneEditingService.GetBaseLayerSprites(zone.ZoneId);

            // 2. Build override lookup from zone def
            var overrideByName = new Dictionary<string, VisualLayerDef>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var layer in zone.VisualLayers)
                overrideByName[layer.Name] = layer;

            // 3. Start with base-game layers (if patching an existing zone)
            if (baseLayers != null)
            {
                foreach (var baseLayer in baseLayers)
                {
                    // Check if the mod overrides this layer
                    if (overrideByName.TryGetValue(baseLayer.Name, out var ovr))
                    {
                        SpawnLayer(ovr, zone, baseSprites);
                        overrideByName.Remove(baseLayer.Name); // consumed
                    }
                    else
                    {
                        // No override — include base layer (even if hidden/disabled)
                        SpawnLayer(baseLayer, zone, baseSprites);
                    }
                }
            }

            // 4. Add remaining override layers (new layers added by the mod)
            foreach (var ovr in overrideByName.Values)
            {
                SpawnLayer(ovr, zone, baseSprites);
            }

            // 5. Fallback: if no layers at all, create a background on the Map sorting layer.
            // The arrowSquares road shader is in the Transparent render queue and requires
            // opaque geometry in the Map sorting layer to composite onto.
            if (_activeLayers.Count == 0)
            {
                Sprite bgSprite = ZoneEditingService.GetBaseZoneBackground(zone.ZoneId);

                var bgGO = new GameObject("Background_Bg");
                bgGO.transform.SetParent(_layersContainer, false);
                bgGO.transform.localPosition = Vector3.zero;
                var sr = bgGO.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = "Map";
                sr.sortingOrder = -10;

                if (bgSprite != null)
                {
                    sr.sprite = bgSprite;
                    sr.color = Color.white;
                }
                else
                {
                    // No background sprite available — create a solid-color procedural background
                    // so the arrowSquares road shader has opaque geometry to render onto.
                    sr.sprite = EnsureFallbackBackground();
                    sr.color = new Color(0.12f, 0.12f, 0.15f, 1f);
                }

                // Track as a visual layer so it appears in the layers panel
                var bgLayerDef = new VisualLayerDef
                {
                    Name = "Background_Bg",
                    Type = VisualLayerType.Sprite,
                    SortingLayer = "Map",
                    SortingOrder = -10,
                    Visible = true,
                };
                _activeLayers.Add(bgLayerDef);
                _layerGOs["Background_Bg"] = bgGO;
            }

            // 6. Cache mapPiece sprites for this zone
            _mapPieceSprites.Clear();
            var mpSprites = ZoneEditingService.GetBaseMapPieces(zone.ZoneId);
            if (mpSprites != null)
            {
                foreach (var kvp in mpSprites)
                {
                    foreach (var mp in kvp.Value)
                    {
                        if (!_mapPieceSprites.ContainsKey(mp.SpriteName))
                        {
                            var s = ZoneEditingService.GetMapPieceSprite(zone.ZoneId, mp.SpriteName);
                            if (s != null) _mapPieceSprites[mp.SpriteName] = s;
                        }
                    }
                }
            }
        }

        /// <summary>Spawn a visual layer into the preview scene by type.
        /// Hidden/disabled layers are tracked in _activeLayers but not rendered.</summary>
        private void SpawnLayer(VisualLayerDef layer, ZoneDef zone, Dictionary<string, Sprite> baseSprites)
        {
            // Always track in _activeLayers even if hidden/disabled
            bool shouldRender = layer.Visible && !layer.Hidden;

            if (!shouldRender)
            {
                _activeLayers.Add(layer);
                return;
            }

            switch (layer.Type)
            {
                case VisualLayerType.Sprite:
                    SpawnSpriteLayer(layer, zone, baseSprites);
                    break;

                case VisualLayerType.ParticleSystem:
                case VisualLayerType.Light:
                case VisualLayerType.SpriteMask:
                case VisualLayerType.Container:
                case VisualLayerType.Shader:
                case VisualLayerType.PrefabEffect:
                    SpawnClonedLayer(layer, zone);
                    break;
            }
        }

        /// <summary>Spawn a non-sprite layer by cloning the cached base-game GameObject.
        /// If no template is found, creates a new GO from scratch with the appropriate component.</summary>
        private void SpawnClonedLayer(VisualLayerDef layer, ZoneDef zone)
        {
            var templateGO = ZoneEditingService.GetBaseLayerGameObject(zone.ZoneId, layer.Name);
            if (templateGO == null)
            {
                // No template — create from scratch for mod-added layers
                SpawnNewLayer(layer);
                return;
            }

            var go = Object.Instantiate(templateGO, _layersContainer);
            go.name = layer.Name;
            go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
            go.SetActive(true);

            // Activate all children recursively (the clone was inactive)
            ActivateRecursive(go.transform);

            // Apply saved properties from the layer def to the live components
            ApplyLayerProperties(layer, go);

            _layerGOs[layer.Name] = go;
            _activeLayers.Add(layer);
        }

        /// <summary>Create a brand-new GO from scratch for mod-added layers without a base-game template.</summary>
        private void SpawnNewLayer(VisualLayerDef layer)
        {
            var go = new GameObject(layer.Name);
            go.transform.SetParent(_layersContainer, false);
            go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
            go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);

            switch (layer.Type)
            {
                case VisualLayerType.Sprite:
                {
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = layer.SortingOrder;
                    sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                    sr.flipX = layer.FlipX;
                    sr.flipY = layer.FlipY;
                    sr.enabled = layer.Enabled;
                    if (!string.IsNullOrEmpty(layer.SortingLayer))
                        try { sr.sortingLayerName = layer.SortingLayer; } catch { }
                    if (!string.IsNullOrEmpty(layer.SpriteName))
                    {
                        var spr = FindGameSprite(layer.SpriteName);
                        if (spr != null) sr.sprite = spr;
                    }
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
                    if (layer.LightType == 0 || layer.LightType == 1)
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
                        // Use default-particle material if available
                        if (psr.sharedMaterial == null)
                        {
                            var defaultMat = new Material(Shader.Find("Particles/Standard Unlit"));
                            defaultMat.SetFloat("_Mode", 1); // Additive
                            psr.sharedMaterial = defaultMat;
                        }
                    }

                    if (layer.PlayOnAwake)
                        ps.Play();
                    break;
                }
                case VisualLayerType.SpriteMask:
                {
                    var mask = go.AddComponent<SpriteMask>();
                    mask.alphaCutoff = layer.AlphaCutoff;
                    mask.isCustomRangeActive = layer.CustomRange;
                    if (layer.CustomRange)
                    {
                        mask.frontSortingOrder = layer.FrontSortingOrder;
                        mask.backSortingOrder = layer.BackSortingOrder;
                    }
                    mask.sortingOrder = layer.SortingOrder;
                    mask.enabled = layer.Enabled;
                    if (!string.IsNullOrEmpty(layer.SpriteName))
                    {
                        var spr = FindGameSprite(layer.SpriteName);
                        if (spr != null) mask.sprite = spr;
                    }
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
                        var prefab = Globals.Instance?.GetResourceEffect(layer.EffectName);
                        if (prefab != null)
                        {
                            var clone = Object.Instantiate(prefab, go.transform);
                            clone.name = layer.EffectName;
                        }
                    }
                    break;
                }
            }

            _layerGOs[layer.Name] = go;
            _activeLayers.Add(layer);
        }

        /// <summary>Recursively activate GameObjects and restart particle systems.</summary>
        private static void ActivateRecursive(Transform t)
        {
            t.gameObject.SetActive(true);

            // Restart any particle systems
            var ps = t.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Clear();
                ps.Play();
            }

            // Reset animators to their default state
            var anim = t.GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                anim.enabled = true;
                anim.Play(0, -1, 0f);
            }

            foreach (Transform child in t)
                ActivateRecursive(child);
        }

        /// <summary>Apply saved VisualLayerDef properties to a live GameObject's components.
        /// Called when spawning a cloned layer to reflect any user edits.</summary>
        private static void ApplyLayerProperties(VisualLayerDef layer, GameObject go)
        {
            if (go == null) return;

            switch (layer.Type)
            {
                case VisualLayerType.Light:
                    var light = go.GetComponent<Light2D>();
                    if (light != null)
                    {
                        light.lightType = (Light2D.LightType)layer.LightType;
                        light.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        light.intensity = layer.Intensity;
                        light.falloffIntensity = layer.FalloffIntensity;
                        light.lightOrder = layer.LightOrder;
                        light.blendStyleIndex = layer.BlendStyleIndex;
                        light.shadowsEnabled = layer.ShadowsEnabled;
                        light.shadowIntensity = layer.ShadowIntensity;
                        if (layer.LightType == 3) // Point
                        {
                            light.pointLightInnerAngle = layer.PointLightInnerAngle;
                            light.pointLightOuterAngle = layer.PointLightOuterAngle;
                            light.pointLightInnerRadius = layer.PointLightInnerRadius;
                            light.pointLightOuterRadius = layer.PointLightOuterRadius;
                        }
                        if (layer.LightType == 0 || layer.LightType == 1)
                            light.shapeLightFalloffSize = layer.ShapeLightFalloffSize;
                        light.enabled = layer.Enabled;
                    }
                    break;

                case VisualLayerType.ParticleSystem:
                    var ps = go.GetComponent<ParticleSystem>();
                    if (ps != null)
                    {
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
                        }
                    }
                    go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
                    break;

                case VisualLayerType.SpriteMask:
                    var mask = go.GetComponent<SpriteMask>();
                    if (mask != null)
                    {
                        mask.alphaCutoff = layer.AlphaCutoff;
                        mask.isCustomRangeActive = layer.CustomRange;
                        if (layer.CustomRange)
                        {
                            mask.frontSortingOrder = layer.FrontSortingOrder;
                            mask.backSortingOrder = layer.BackSortingOrder;
                        }
                        mask.sortingOrder = layer.SortingOrder;
                        mask.enabled = layer.Enabled;

                        if (!string.IsNullOrEmpty(layer.SpriteName))
                        {
                            var spr = FindGameSprite(layer.SpriteName);
                            if (spr != null) mask.sprite = spr;
                        }
                    }
                    go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
                    break;

                case VisualLayerType.Sprite:
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        sr.sortingOrder = layer.SortingOrder;
                        sr.flipX = layer.FlipX;
                        sr.flipY = layer.FlipY;
                        sr.enabled = layer.Enabled;

                        if (!string.IsNullOrEmpty(layer.SpriteName))
                        {
                            var spr = FindGameSprite(layer.SpriteName);
                            if (spr != null) sr.sprite = spr;
                        }
                    }
                    go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
                    break;

                case VisualLayerType.Shader:
                    var shaderSr = go.GetComponent<SpriteRenderer>();
                    if (shaderSr != null)
                    {
                        shaderSr.sprite = ShaderPresetGenerator.GetPresetSprite(layer.Preset, layer.PresetParam1, layer.PresetParam2);
                        shaderSr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        shaderSr.sortingOrder = layer.SortingOrder;
                        shaderSr.maskInteraction = (SpriteMaskInteraction)layer.MaskInteraction;
                        shaderSr.enabled = layer.Enabled;
                        if (!string.IsNullOrEmpty(layer.ShaderName))
                        {
                            var shader = Shader.Find(layer.ShaderName) ?? Resources.Load<Shader>(layer.ShaderName);
                            if (shader != null && (shaderSr.sharedMaterial == null || shaderSr.sharedMaterial.shader.name != layer.ShaderName))
                                shaderSr.material = new Material(shader);
                        }
                        ShaderEffectRegistry.ApplyToMaterial(shaderSr.material, layer.ShaderKeywords, layer.ShaderFloats);
                    }
                    go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
                    break;

                case VisualLayerType.PrefabEffect:
                    // PrefabEffect: just update position/scale, can't hot-swap the prefab
                    break;
            }
        }

        private void SpawnSpriteLayer(VisualLayerDef layer, ZoneDef zone, Dictionary<string, Sprite> baseSprites)
        {
            if (!layer.Visible) return;

            // If a cached GO clone exists for this sprite layer (e.g. animated sprite with Animator),
            // prefer instantiating the clone to preserve animator controllers and hierarchy.
            var templateGO = ZoneEditingService.GetBaseLayerGameObject(zone.ZoneId, layer.Name);
            if (templateGO != null)
            {
                SpawnClonedLayer(layer, zone);
                return;
            }

            // Resolve sprite
            Sprite sprite = null;

            // Try base-game sprite cache (original layer name → captured sprite)
            if (baseSprites != null)
                baseSprites.TryGetValue(layer.Name, out sprite);

            // For override/mod layers, look up by SpriteName in all loaded game sprites
            if (sprite == null && !string.IsNullOrEmpty(layer.SpriteName))
                sprite = FindGameSprite(layer.SpriteName);

            if (sprite == null)
            {
                Plugin.Log.LogWarning($"[MapEditor] No sprite for layer '{layer.Name}' (sprite={layer.SpriteName})");
                // Still track the layer def even without a sprite
                _activeLayers.Add(layer);
                return;
            }

            var go = new GameObject(layer.Name);
            go.transform.SetParent(_layersContainer, false);
            go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
            go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
            sr.sortingOrder = layer.SortingOrder;
            sr.flipX = layer.FlipX;
            sr.flipY = layer.FlipY;

            // Set sorting layer if available
            if (!string.IsNullOrEmpty(layer.SortingLayer))
            {
                try { sr.sortingLayerName = layer.SortingLayer; }
                catch { } // Sorting layer might not exist in this context
            }

            _layerGOs[layer.Name] = go;
            _activeLayers.Add(layer);
        }

        // ═══════════════════════════════════════════════════════════════
        //  NODE VISUALS
        // ═══════════════════════════════════════════════════════════════

        private void CreateNodeVisual(string nodeId, float localX, float localY, ZoneDef zone)
        {
            EnsureNodeSprites();

            var go = new GameObject(nodeId);
            go.transform.SetParent(_nodesContainer, false);
            go.transform.localPosition = new Vector3(localX, localY, 0f);

            //  Base node shape ("plain") 
            var plainGO = new GameObject("plain");
            plainGO.transform.SetParent(go.transform, false);
            var plainSR = plainGO.AddComponent<SpriteRenderer>();
            plainSR.sprite = FindCachedSprite("mapnode") ?? EnsureNodeFallback();
            plainSR.sortingOrder = 5;
            plainSR.color = GetNodeBaseColor(nodeId, zone);
            _nodePlainSRs[nodeId] = plainSR;

            //  Node type icon 
            Sprite iconSprite = GetNodeIconSprite(nodeId, zone);
            if (iconSprite != null)
            {
                var iconGO = new GameObject("nodeIcon");
                iconGO.transform.SetParent(go.transform, false);
                iconGO.transform.localPosition = NodeIconOffset;
                var iconSR = iconGO.AddComponent<SpriteRenderer>();
                iconSR.sprite = iconSprite;
                iconSR.sortingOrder = 150;
                iconSR.color = Color.white;
            }

            //  MapPiece children 
            if (zone.Nodes.TryGetValue(nodeId, out var nodeDef))
            {
                foreach (var mp in nodeDef.MapPieces)
                {
                    Sprite mpSprite = null;
                    _mapPieceSprites.TryGetValue(mp.SpriteName, out mpSprite);
                    if (mpSprite == null) continue;

                    var mpGO = new GameObject($"mapPiece_{mp.SpriteName}");
                    mpGO.transform.SetParent(go.transform, false);
                    mpGO.transform.localPosition = new Vector3(mp.PosX, mp.PosY, 0f);
                    mpGO.transform.localScale = new Vector3(mp.ScaleX, mp.ScaleY, 1f);
                    var mpSR = mpGO.AddComponent<SpriteRenderer>();
                    mpSR.sprite = mpSprite;
                    mpSR.sortingOrder = mp.SortingOrder;
                    mpSR.color = new Color(mp.ColorR, mp.ColorG, mp.ColorB, mp.ColorA);
                    mpSR.flipX = mp.FlipX;
                    mpSR.flipY = mp.FlipY;

                    if (!string.IsNullOrEmpty(mp.SortingLayer))
                    {
                        try { mpSR.sortingLayerName = mp.SortingLayer; }
                        catch { }
                    }
                }
            }

            _nodeGOs[nodeId] = go;
        }

        /// <summary>Determine the icon sprite for a node based on its assignment.</summary>
        private Sprite GetNodeIconSprite(string nodeId, ZoneDef zone)
        {
            if (!zone.Nodes.TryGetValue(nodeId, out var nd)) return null;

            if (nd.GoToTown)
                return FindCachedSprite("nodeIconShop");
            if (!string.IsNullOrEmpty(nd.CombatId))
                return FindCachedSprite("nodeIconCombat");
            if (!string.IsNullOrEmpty(nd.EventId))
                return FindCachedSprite("nodeIconEvent");

            // Entrance node — show map icon
            if (nodeId == $"{zone.IdPrefix}_0")
                return FindCachedSprite("nodeIconMap");

            return null;
        }

        /// <summary>Determine the base (untinted) color for a node.</summary>
        private Color GetNodeBaseColor(string nodeId, ZoneDef zone)
        {
            if (!zone.Nodes.TryGetValue(nodeId, out var nd))
                return NodeEmptyColor;

            string prefix = zone.IdPrefix;
            if (nodeId == $"{prefix}_0") return NodeEntranceColor;
            if (nd.GoToTown) return NodeTownColor;

            // Nodes without any assignment are dimmed
            if (string.IsNullOrEmpty(nd.CombatId) && string.IsNullOrEmpty(nd.EventId) && !nd.GoToTown)
                return NodeEmptyColor;

            return NodeNormalColor;
        }

        // ═══════════════════════════════════════════════════════════════
        //  DESTROY
        // ═══════════════════════════════════════════════════════════════

        public void DestroyPreview()
        {
            _roads?.ClearAll();
            _roads = null;

            if (_previewRoot != null)
            {
                Object.Destroy(_previewRoot);
                _previewRoot = null;
            }
            _nodesContainer = null;
            _roadsContainer = null;
            _layersContainer = null;
            _layerGOs.Clear();
            _activeLayers.Clear();
            _nodeGOs.Clear();
            _nodePlainSRs.Clear();
            _loadedZoneId = null;
            _dragNodeId = null;
            _dragWPRoadKey = null;
            _connectFirstId = null;
        }
    }
}
