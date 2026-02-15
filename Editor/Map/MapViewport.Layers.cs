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
        // 
        //  VISUAL LAYERS
        // 

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
                        // No override  include base layer (even if hidden/disabled)
                        SpawnLayer(baseLayer, zone, baseSprites);
                    }
                }
            }

            // 4. Add remaining override layers (new layers added by the mod)
            foreach (var ovr in overrideByName.Values)
            {
                SpawnLayer(ovr, zone, baseSprites);
            }

            // 5. Fallback: if no layers at all, try the old single-background path
            if (_activeLayers.Count == 0)
            {
                var bgSprite = MapBuilder.GetBackgroundSprite(zone.ZoneId);
                if (bgSprite == null)
                    bgSprite = ZoneEditingService.GetBaseZoneBackground(zone.ZoneId);
                if (bgSprite != null)
                {
                    _bgGO = new GameObject("Background");
                    _bgGO.transform.SetParent(_layersContainer, false);
                    _bgGO.transform.localPosition = Vector3.zero;
                    var sr = _bgGO.AddComponent<SpriteRenderer>();
                    sr.sprite = bgSprite;
                    sr.color = Color.white;
                    sr.sortingOrder = -10;
                }
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
                case VisualLayerType.Container:
                    SpawnClonedLayer(layer, zone);
                    break;

                case VisualLayerType.SpriteMask:
                    // SpriteMasks are metadata-only; track them in the panel but don't render
                    _activeLayers.Add(layer);
                    break;
            }
        }

        /// <summary>Spawn a non-sprite layer by cloning the cached base-game GameObject.</summary>
        private void SpawnClonedLayer(VisualLayerDef layer, ZoneDef zone)
        {
            var templateGO = ZoneEditingService.GetBaseLayerGameObject(zone.ZoneId, layer.Name);
            if (templateGO == null)
            {
                // No cached clone available  just track the def in the panel
                _activeLayers.Add(layer);
                return;
            }

            var go = Object.Instantiate(templateGO, _layersContainer);
            go.name = layer.Name;
            go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
            go.SetActive(true);

            // Activate all children recursively (the clone was inactive)
            ActivateRecursive(go.transform);

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

            // Try mod-provided texture first (override layers)
            if (layer.IsOverride && !string.IsNullOrEmpty(layer.SpriteName))
                sprite = MapBuilder.GetBackgroundSprite(zone.ZoneId); // TODO: per-layer texture loading

            // Try base-game sprite cache
            if (sprite == null && baseSprites != null)
                baseSprites.TryGetValue(layer.Name, out sprite);

            if (sprite == null)
            {
                Plugin.Log.LogWarning($"[MapViewport] No sprite for layer '{layer.Name}' (sprite={layer.SpriteName})");
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

        // 
        //  LAYERS PANEL
        // 

        private void DrawLayersPanel(ZoneDef zone)
        {
            GUILayout.BeginHorizontal();
            _showLayers = GUILayout.Toggle(_showLayers, "", GUILayout.Width(14));
            int mapPieceCount = zone.Nodes.Values.Sum(n => n.MapPieces.Count);
            GUILayout.Label($"<b>Visual Layers ({_activeLayers.Count})</b>  <color=#888>MapPieces: {mapPieceCount}</color>", EditorStyles.RichLabel);
            GUILayout.EndHorizontal();

            if (!_showLayers) return;

            _layerScrollPos = GUILayout.BeginScrollView(_layerScrollPos, GUILayout.MaxHeight(400));

            int moveUp = -1, moveDown = -1, removeIdx = -1, duplicateIdx = -1;
            bool toggledAny = false;

            for (int i = 0; i < _activeLayers.Count; i++)
            {
                var layer = _activeLayers[i];
                bool hasGO = _layerGOs.TryGetValue(layer.Name, out var go) && go != null;
                bool isRendered = hasGO && go.activeSelf;
                bool isExpanded = _expandedLayerIdx == i;
                bool isDisabled = !layer.Visible; // prefab-disabled
                bool isHidden = layer.Hidden;     // mod-hidden

                GUILayout.BeginVertical(isExpanded ? GUI.skin.box : GUIStyle.none);
                GUILayout.BeginHorizontal();

                // Visibility toggle
                bool effectiveOn = isRendered && !isHidden;
                bool newVisible = GUILayout.Toggle(effectiveOn, "", GUILayout.Width(14));
                if (newVisible != effectiveOn)
                {
                    if (newVisible)
                    {
                        layer.Visible = true;
                        layer.Hidden = false;
                        if (!hasGO)
                            _loadedZoneId = null; // rebuild to create the GO
                        else
                            go.SetActive(true);
                    }
                    else
                    {
                        if (hasGO) go.SetActive(false);
                        if (!layer.IsOverride)
                            layer.Hidden = true;
                        else
                            layer.Visible = false;
                    }
                    toggledAny = true;
                }

                // Type icon
                string typeIcon = layer.Type switch
                {
                    VisualLayerType.ParticleSystem => "<color=#ff8800>\u2022</color>",
                    VisualLayerType.Light => "<color=#ffff00>\u2600</color>",
                    VisualLayerType.SpriteMask => "<color=#8888ff>\u25A3</color>",
                    VisualLayerType.Container => "<color=#888>\u25A1</color>",
                    _ => "<color=#88ff88>\u25A0</color>",
                };

                // Status tags
                string statusTags = "";
                if (isDisabled && !isHidden) statusTags += " <color=#886600>[DISABLED]</color>";
                if (isHidden) statusTags += " <color=#884400>[HIDDEN]</color>";
                if (layer.IsOverride) statusTags += " <color=#44aaff>[MOD]</color>";
                string orderTag = layer.Type == VisualLayerType.Sprite
                    ? $" <color=#666>z={layer.SortingOrder}</color>" : "";

                // Clickable label to expand/collapse
                if (GUILayout.Button($"{typeIcon} {layer.Name}{orderTag}{statusTags}", EditorStyles.ListItem))
                {
                    _expandedLayerIdx = isExpanded ? -1 : i;
                    _layerRenameBuffer = layer.Name;
                }

                // Move up/down
                if (i > 0 && GUILayout.Button("\u25B2", EditorStyles.MiniButton, GUILayout.Width(20)))
                    moveUp = i;
                if (i < _activeLayers.Count - 1 && GUILayout.Button("\u25BC", EditorStyles.MiniButton, GUILayout.Width(20)))
                    moveDown = i;

                // Remove (mod layers only)
                if (layer.IsOverride)
                {
                    if (GUILayout.Button("\u2716", EditorStyles.MiniButton, GUILayout.Width(20)))
                        removeIdx = i;
                }

                GUILayout.EndHorizontal();

                //  Expanded property editor 
                if (isExpanded)
                {
                    DrawLayerProperties(layer, zone, hasGO ? go : null);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Duplicate", EditorStyles.MiniButton))
                        duplicateIdx = i;
                    if (!layer.IsOverride && GUILayout.Button(layer.Hidden ? "Unhide" : "Hide (mod)", EditorStyles.MiniButton))
                    {
                        layer.Hidden = !layer.Hidden;
                        if (hasGO) go.SetActive(!layer.Hidden);
                        toggledAny = true;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();
            }

            //  MapPieces section 
            bool anyMapPieces = zone.Nodes.Values.Any(n => n.MapPieces.Count > 0);
            if (anyMapPieces)
            {
                GUILayout.Space(4);
                GUILayout.Label("<b>Map Pieces</b> <color=#888>(node-attached)</color>", EditorStyles.RichLabel);
                foreach (var kvp in zone.Nodes.OrderBy(kv => kv.Key))
                {
                    if (kvp.Value.MapPieces.Count == 0) continue;
                    string reqTag = !string.IsNullOrEmpty(kvp.Value.NodeRequirementId)
                        ? $" <color=#aa8844>req={kvp.Value.NodeRequirementId}</color>" : "";
                    GUILayout.Label($"  <color=#aaa>\u25B8 {kvp.Key}</color>{reqTag}", EditorStyles.RichLabel);
                    foreach (var mp in kvp.Value.MapPieces)
                    {
                        string sizeTag = mp.SpriteWidth > 0
                            ? $" <color=#666>{mp.SpriteWidth:F0}x{mp.SpriteHeight:F0}</color>" : "";
                        GUILayout.Label($"    <color=#88ff88>\u25A0</color> {mp.SpriteName}{sizeTag} <color=#666>z={mp.SortingOrder}</color>",
                            EditorStyles.RichLabel);
                    }
                }
            }

            GUILayout.EndScrollView();

            // Process moves
            if (moveUp > 0)
                SwapLayers(moveUp, moveUp - 1, zone);
            else if (moveDown >= 0 && moveDown < _activeLayers.Count - 1)
                SwapLayers(moveDown, moveDown + 1, zone);

            // Process remove
            if (removeIdx >= 0)
            {
                var removed = _activeLayers[removeIdx];
                _activeLayers.RemoveAt(removeIdx);
                if (_layerGOs.TryGetValue(removed.Name, out var rgo))
                {
                    Object.Destroy(rgo);
                    _layerGOs.Remove(removed.Name);
                }
                zone.VisualLayers.Remove(removed);
                if (_expandedLayerIdx == removeIdx) _expandedLayerIdx = -1;
                else if (_expandedLayerIdx > removeIdx) _expandedLayerIdx--;
                ZoneEditingService.MarkDirty();
            }

            // Process duplicate
            if (duplicateIdx >= 0)
            {
                var src = _activeLayers[duplicateIdx];
                var dup = new VisualLayerDef
                {
                    Name = src.Name + "_copy",
                    Type = src.Type,
                    SpriteName = src.SpriteName,
                    SortingOrder = src.SortingOrder + 1,
                    SortingLayer = src.SortingLayer,
                    PosX = src.PosX, PosY = src.PosY, PosZ = src.PosZ,
                    ScaleX = src.ScaleX, ScaleY = src.ScaleY,
                    ColorR = src.ColorR, ColorG = src.ColorG, ColorB = src.ColorB, ColorA = src.ColorA,
                    SpriteWidth = src.SpriteWidth, SpriteHeight = src.SpriteHeight, PPU = src.PPU,
                    Visible = true,
                    IsOverride = true,
                    FlipX = src.FlipX, FlipY = src.FlipY,
                };
                zone.VisualLayers.Add(dup);
                _activeLayers.Insert(duplicateIdx + 1, dup);
                _expandedLayerIdx = duplicateIdx + 1;
                _layerRenameBuffer = dup.Name;
                _loadedZoneId = null; // rebuild to create the GO
                ZoneEditingService.MarkDirty();
            }

            if (toggledAny)
                ZoneEditingService.MarkDirty();

            // Add layer buttons with type picker
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Sprite Layer", EditorStyles.MiniButton))
                AddNewLayer(zone, VisualLayerType.Sprite);
            if (GUILayout.Button("+ Container", EditorStyles.MiniButton))
                AddNewLayer(zone, VisualLayerType.Container);
            GUILayout.EndHorizontal();
        }

        /// <summary>Draw editable properties for a single layer.</summary>
        private void DrawLayerProperties(VisualLayerDef layer, ZoneDef zone, GameObject go)
        {
            bool changed = false;

            // Name (rename)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(60));
            string newName = GUILayout.TextField(_layerRenameBuffer);
            if (newName != _layerRenameBuffer)
                _layerRenameBuffer = newName;
            if (_layerRenameBuffer != layer.Name && GUILayout.Button("Apply", EditorStyles.MiniButton, GUILayout.Width(50)))
            {
                string oldName = layer.Name;
                layer.Name = _layerRenameBuffer;
                if (_layerGOs.TryGetValue(oldName, out var layerGO))
                {
                    _layerGOs.Remove(oldName);
                    _layerGOs[layer.Name] = layerGO;
                    if (layerGO != null) layerGO.name = layer.Name;
                }
                changed = true;
            }
            GUILayout.EndHorizontal();

            // Type (read-only)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Type:", GUILayout.Width(60));
            GUILayout.Label($"<color=#aaa>{layer.Type}</color>", EditorStyles.RichLabel);
            GUILayout.EndHorizontal();

            if (layer.Type == VisualLayerType.Sprite)
            {
                // Sorting Order
                GUILayout.BeginHorizontal();
                GUILayout.Label("Order:", GUILayout.Width(60));
                string orderStr = GUILayout.TextField(layer.SortingOrder.ToString(), GUILayout.Width(50));
                if (int.TryParse(orderStr, out int newOrder) && newOrder != layer.SortingOrder)
                {
                    layer.SortingOrder = newOrder;
                    if (go != null)
                    {
                        var sr = go.GetComponent<SpriteRenderer>();
                        if (sr != null) sr.sortingOrder = newOrder;
                    }
                    changed = true;
                }
                GUILayout.Label("Layer:", GUILayout.Width(40));
                GUILayout.Label($"<color=#aaa>{layer.SortingLayer}</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();

                // Position
                GUILayout.BeginHorizontal();
                GUILayout.Label("Pos:", GUILayout.Width(60));
                changed |= FloatField("X", ref layer.PosX, 50);
                changed |= FloatField("Y", ref layer.PosY, 50);
                changed |= FloatField("Z", ref layer.PosZ, 50);
                GUILayout.EndHorizontal();

                // Scale
                GUILayout.BeginHorizontal();
                GUILayout.Label("Scale:", GUILayout.Width(60));
                changed |= FloatField("X", ref layer.ScaleX, 50);
                changed |= FloatField("Y", ref layer.ScaleY, 50);
                GUILayout.EndHorizontal();

                // Color
                GUILayout.BeginHorizontal();
                GUILayout.Label("Color:", GUILayout.Width(60));
                changed |= FloatField("R", ref layer.ColorR, 40);
                changed |= FloatField("G", ref layer.ColorG, 40);
                changed |= FloatField("B", ref layer.ColorB, 40);
                changed |= FloatField("A", ref layer.ColorA, 40);
                GUILayout.EndHorizontal();

                // Flip
                GUILayout.BeginHorizontal();
                GUILayout.Label("Flip:", GUILayout.Width(60));
                bool newFlipX = GUILayout.Toggle(layer.FlipX, "X", GUILayout.Width(30));
                bool newFlipY = GUILayout.Toggle(layer.FlipY, "Y", GUILayout.Width(30));
                if (newFlipX != layer.FlipX) { layer.FlipX = newFlipX; changed = true; }
                if (newFlipY != layer.FlipY) { layer.FlipY = newFlipY; changed = true; }
                GUILayout.EndHorizontal();

                // Sprite info (read-only)
                GUILayout.Label($"<color=#666>Sprite: {layer.SpriteName} ({layer.SpriteWidth:F0}x{layer.SpriteHeight:F0} ppu={layer.PPU:F0})</color>",
                    EditorStyles.RichLabel);

                // Apply position/scale/color changes to GO
                if (changed && go != null)
                {
                    go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
                    go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                        sr.flipX = layer.FlipX;
                        sr.flipY = layer.FlipY;
                    }
                }
            }
            else
            {
                // Non-sprite layers: position only
                GUILayout.BeginHorizontal();
                GUILayout.Label("Pos:", GUILayout.Width(60));
                changed |= FloatField("X", ref layer.PosX, 50);
                changed |= FloatField("Y", ref layer.PosY, 50);
                changed |= FloatField("Z", ref layer.PosZ, 50);
                GUILayout.EndHorizontal();

                if (changed && go != null)
                {
                    go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);

                    // Persist container offsets back to ZoneDef
                    if (layer.Name == "[Nodes]")
                    {
                        zone.NodesOffsetX = layer.PosX;
                        zone.NodesOffsetY = layer.PosY;
                        // Roads use world-space coords including NodesOffset  force rebuild
                        _loadedZoneId = null;
                    }
                }
            }

            if (changed) ZoneEditingService.MarkDirty();
        }

        /// <summary>Draw a small labeled float field. Returns true if value changed.</summary>
        private static bool FloatField(string label, ref float value, float width)
        {
            GUILayout.Label(label, GUILayout.Width(12));
            string s = GUILayout.TextField(value.ToString("F2"), GUILayout.Width(width));
            if (float.TryParse(s, out float newVal) && Mathf.Abs(newVal - value) > 0.001f)
            {
                value = newVal;
                return true;
            }
            return false;
        }

        private void AddNewLayer(ZoneDef zone, VisualLayerType type)
        {
            var newLayer = new VisualLayerDef
            {
                Name = $"custom_{type.ToString().ToLower()}_{_activeLayers.Count}",
                Type = type,
                SortingOrder = 0,
                IsOverride = true,
                Visible = true,
            };
            zone.VisualLayers.Add(newLayer);
            _activeLayers.Add(newLayer);
            _expandedLayerIdx = _activeLayers.Count - 1;
            _layerRenameBuffer = newLayer.Name;
            ZoneEditingService.MarkDirty();
        }

        private void SwapLayers(int idxA, int idxB, ZoneDef zone)
        {
            // Swap in active list
            var tmp = _activeLayers[idxA];
            _activeLayers[idxA] = _activeLayers[idxB];
            _activeLayers[idxB] = tmp;

            // Update sorting orders to match visual order
            // Lower index = further back (lower sorting order)
            for (int i = 0; i < _activeLayers.Count; i++)
            {
                var lay = _activeLayers[i];
                if (lay.Type != VisualLayerType.Sprite) continue;
                int newOrder = -10 + i;
                lay.SortingOrder = newOrder;
                if (_layerGOs.TryGetValue(lay.Name, out var go) && go != null)
                {
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.sortingOrder = newOrder;
                }
            }
            ZoneEditingService.MarkDirty();
        }

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

            // Entrance node  show map icon
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

        public void DestroyPreview()
        {
            _roads?.ClearAll();
            _roads = null;

            if (_previewRoot != null)
            {
                Object.Destroy(_previewRoot);
                _previewRoot = null;
            }
            _bgGO = null;
            _nodesContainer = null;
            _roadsContainer = null;
            _layersContainer = null;
            _layerGOs.Clear();
            _activeLayers.Clear();
            _nodeGOs.Clear();
            _nodePlainSRs.Clear();
            _loadedZoneId = null;
            _dragNodeId = null;
            _dragCPRoadKey = null;
            _connectFirstId = null;
        }

    }
}
