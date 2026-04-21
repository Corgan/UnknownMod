using System;
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
        //  PANEL (right-side, drawn inside ModEditor's IMGUI area)
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            DrawZoneProperties(zone);
            EditorStyles.Separator();

            GUILayout.Label("<b>Visual Layers</b>", EditorStyles.RichLabel);
            GUILayout.Space(2);
            GUILayout.Label("<color=#888>Right-drag to pan, scroll to zoom</color>", EditorStyles.RichLabel);

            GUILayout.Space(4);
            if (GUILayout.Button("Rebuild Viewport", EditorStyles.MiniButton))
                _loadedZoneId = null;

            GUILayout.Space(4);
            DrawLayersPanel(zone);
        }

        // ═══════════════════════════════════════════════════════════════
        //  ZONE PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        private void DrawZoneProperties(ZoneDef zone)
        {
            if (!EditorFields.Section("Zone Properties", ref _secZoneProps)) return;

            zone.ZoneName = EditorFields.TextField("Zone Name", zone.ZoneName);
            zone.IdPrefix = EditorFields.TextField("ID Prefix", zone.IdPrefix);
            zone.CombatBackgroundSprite = EditorFields.TextField("Combat BG Sprite", zone.CombatBackgroundSprite);
            zone.Sku = EditorFields.TextField("SKU", zone.Sku);

            GUILayout.Space(2);
            var flagLabels = new[] { "Obelisk Low", "Obelisk High", "Obelisk Final", "Disable XP", "Disable Madness" };
            var flagVals = new[] { zone.ObeliskLow, zone.ObeliskHigh, zone.ObeliskFinal, zone.DisableExperience, zone.DisableMadness };
            EditorFields.ToggleGrid(flagLabels, flagVals, 3);
            zone.ObeliskLow = flagVals[0]; zone.ObeliskHigh = flagVals[1]; zone.ObeliskFinal = flagVals[2];
            zone.DisableExperience = flagVals[3]; zone.DisableMadness = flagVals[4];

            // ── Team Replacement ─────────────────────────────────
            if (EditorFields.Section("Team Replacement", ref _secZoneTeam))
            {
                zone.ChangeTeamOnEntrance = EditorFields.Toggle("Change Team On Entrance", zone.ChangeTeamOnEntrance);
                zone.RestoreTeamOnExit = EditorFields.Toggle("Restore Team On Exit", zone.RestoreTeamOnExit);

                if (zone.ChangeTeamOnEntrance)
                {
                    var scIds = EditorFields.CachedIds("subclass", DataHelper.GetAllSubClassIds);
                    GUILayout.Label($"<color=#aaa>New Team ({zone.NewTeam.Count}/4):</color>", EditorStyles.RichLabel);
                    int removeTeamIdx = -1;
                    for (int i = 0; i < zone.NewTeam.Count; i++)
                    {
                        GUILayout.BeginHorizontal();
                        zone.NewTeam[i] = EditorFields.IdDropdown($"#{i + 1}", zone.NewTeam[i], scIds, $"zone_team_{i}", pickerMode: EntityPicker.Mode.Hero);
                        if (GUILayout.Button("X", UnknownMod.Editor.EditorStyles.MiniButton, GUILayout.Width(22)))
                            removeTeamIdx = i;
                        GUILayout.EndHorizontal();
                    }
                    if (removeTeamIdx >= 0) zone.NewTeam.RemoveAt(removeTeamIdx);
                    if (zone.NewTeam.Count < 4 && GUILayout.Button("+ Add Hero", GUILayout.Height(22)))
                        zone.NewTeam.Add("");
                }
            }

            // ── Offsets ──────────────────────────────────────────
            if (EditorFields.Section("Offsets", ref _secZoneOffsets))
            {
                GUILayout.Label("<color=#aaa>Nodes Offset:</color>", EditorStyles.RichLabel);
                GUILayout.BeginHorizontal();
                zone.NodesOffsetX = EditorFields.FloatField("X", zone.NodesOffsetX);
                zone.NodesOffsetY = EditorFields.FloatField("Y", zone.NodesOffsetY);
                GUILayout.EndHorizontal();

                GUILayout.Label("<color=#aaa>Roads Offset:</color>", EditorStyles.RichLabel);
                GUILayout.BeginHorizontal();
                zone.RoadsOffsetX = EditorFields.FloatField("X", zone.RoadsOffsetX);
                zone.RoadsOffsetY = EditorFields.FloatField("Y", zone.RoadsOffsetY);
                GUILayout.EndHorizontal();
            }

            // ── Camera Bounds ────────────────────────────────────
            if (EditorFields.Section("Camera Bounds", ref _secZoneCameraBounds))
            {
                GUILayout.Label("<color=#aaa>Min:</color>", EditorStyles.RichLabel);
                GUILayout.BeginHorizontal();
                zone.CameraBoundsMinX = EditorFields.FloatField("X", zone.CameraBoundsMinX);
                zone.CameraBoundsMinY = EditorFields.FloatField("Y", zone.CameraBoundsMinY);
                GUILayout.EndHorizontal();

                GUILayout.Label("<color=#aaa>Max:</color>", EditorStyles.RichLabel);
                GUILayout.BeginHorizontal();
                zone.CameraBoundsMaxX = EditorFields.FloatField("X", zone.CameraBoundsMaxX);
                zone.CameraBoundsMaxY = EditorFields.FloatField("Y", zone.CameraBoundsMaxY);
                GUILayout.EndHorizontal();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYERS PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawLayersPanel(ZoneDef zone)
        {
            int mapPieceCount = zone.Nodes.Values.Sum(n => n.MapPieces.Count);
            GUILayout.Label($"<color=#888>Layers: {_activeLayers.Count}  |  MapPieces: {mapPieceCount}</color>", EditorStyles.RichLabel);

            // Header row
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#666>On</color>", EditorStyles.RichLabel, GUILayout.Width(16));
            GUILayout.Label("<color=#666>Vis</color>", EditorStyles.RichLabel, GUILayout.Width(22));
            GUILayout.Label("<color=#666># Layer</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#666>\u25B2\u25BC</color>", EditorStyles.RichLabel, GUILayout.Width(44));
            GUILayout.EndHorizontal();

            _layerScrollPos = GUILayout.BeginScrollView(_layerScrollPos);

            int removeIdx = -1, duplicateIdx = -1;
            bool toggledAny = false;

            // Sort display: first by sorting layer value (descending), then by SortingOrder (descending)
            var sortedIndices = Enumerable.Range(0, _activeLayers.Count)
                .OrderByDescending(i => GetSortingLayerValue(_activeLayers[i].SortingLayer))
                .ThenByDescending(i => _activeLayers[i].SortingOrder)
                .ToList();

            string lastGroupLayer = null;

            for (int si = 0; si < sortedIndices.Count; si++)
            {
                int i = sortedIndices[si];
                var layer = _activeLayers[i];
                bool hasGO = _layerGOs.TryGetValue(layer.Name, out var go) && go != null;
                bool isRendered = hasGO && go.activeSelf;
                bool isExpanded = _expandedLayerIdx == i;
                bool isDisabled = !layer.Visible; // prefab-disabled
                bool isHidden = layer.Hidden;     // mod-hidden

                bool isBuiltinContainer = layer.Name == "[Nodes]" || layer.Name == "[Roads]";

                // ── Sorting layer group header (collapsible) ──
                string layerGroup = layer.SortingLayer ?? "Map";
                if (layerGroup != lastGroupLayer)
                {
                    lastGroupLayer = layerGroup;
                    bool collapsed = _collapsedGroups.Contains(layerGroup);
                    string arrow = collapsed ? "\u25B6" : "\u25BC";
                    int groupCount = sortedIndices.Count(idx => (_activeLayers[idx].SortingLayer ?? "Map") == layerGroup);
                    if (GUILayout.Button($"{arrow} <b><color=#7799bb>{layerGroup}</color></b> <color=#555>({groupCount})</color>", EditorStyles.RichLabel))
                    {
                        if (collapsed) _collapsedGroups.Remove(layerGroup);
                        else _collapsedGroups.Add(layerGroup);
                    }
                }

                // Skip layers in collapsed groups
                if (_collapsedGroups.Contains(layerGroup))
                    continue;

                GUILayout.BeginVertical(isExpanded ? GUI.skin.box : GUIStyle.none);
                GUILayout.BeginHorizontal();

                // ── Enabled checkbox ──
                if (isBuiltinContainer || layer.Type == VisualLayerType.Container)
                {
                    // For containers/builtins: toggle sets the GO active state
                    bool containerEnabled = hasGO && go.activeSelf;
                    bool newContainerEnabled = GUILayout.Toggle(containerEnabled, "", GUILayout.Width(14));
                    if (newContainerEnabled != containerEnabled)
                    {
                        if (hasGO) go.SetActive(newContainerEnabled);
                        toggledAny = true;
                    }
                }
                else
                {
                    bool newEnabled = GUILayout.Toggle(layer.Enabled, "", GUILayout.Width(14));
                    if (newEnabled != layer.Enabled)
                    {
                        layer.Enabled = newEnabled;
                        ApplyEnabled(layer, hasGO ? go : null);
                        toggledAny = true;
                    }
                }

                // ── Visible/hidden toggle ──
                {
                    bool effectiveVis = isRendered && !isHidden;
                    string visLabel = effectiveVis ? "<color=#8c8>V</color>" : "<color=#664400>H</color>";
                    if (GUILayout.Button(visLabel, EditorStyles.MiniButton, GUILayout.Width(20)))
                    {
                        if (effectiveVis)
                        {
                            if (hasGO) go.SetActive(false);
                            if (!layer.IsOverride)
                                layer.Hidden = true;
                            else
                                layer.Visible = false;
                        }
                        else
                        {
                            layer.Visible = true;
                            layer.Hidden = false;
                            if (!hasGO)
                                _loadedZoneId = null; // rebuild to create the GO
                            else
                                go.SetActive(true);
                        }
                        toggledAny = true;
                    }
                }

                // Order number
                string orderStr = $"<color=#555>{layer.SortingOrder}</color>";

                // Type icon
                string typeIcon = layer.Type switch
                {
                    VisualLayerType.ParticleSystem => "<color=#ff8800>\u2022</color>",
                    VisualLayerType.Light => "<color=#ffff00>\u2600</color>",
                    VisualLayerType.SpriteMask => "<color=#8888ff>\u25A3</color>",
                    VisualLayerType.Container => "<color=#888>\u25A1</color>",
                    VisualLayerType.Shader => "<color=#ee55ee>\u25C6</color>",
                    VisualLayerType.PrefabEffect => "<color=#ff4444>\u2726</color>",
                    _ => "<color=#88ff88>\u25A0</color>",
                };

                // Status tags
                string statusTags = "";
                if (layer.IsOverride) statusTags += " <color=#44aaff>[MOD]</color>";

                // Clickable label to expand/collapse
                if (GUILayout.Button($"{orderStr} {typeIcon} {layer.Name}{statusTags}", EditorStyles.ListItem))
                {
                    _expandedLayerIdx = isExpanded ? -1 : i;
                    _layerRenameBuffer = layer.Name;
                }

                // Move order up/down (higher = draws on top)
                if (GUILayout.Button("\u25B2", EditorStyles.MiniButton, GUILayout.Width(20)))
                {
                    layer.SortingOrder++;
                    ApplySortingOrder(layer);
                    ZoneEditingService.MarkDirty();
                }
                if (GUILayout.Button("\u25BC", EditorStyles.MiniButton, GUILayout.Width(20)))
                {
                    layer.SortingOrder--;
                    ApplySortingOrder(layer);
                    ZoneEditingService.MarkDirty();
                }

                // Remove (mod layers only, not built-in)
                if (layer.IsOverride && !isBuiltinContainer)
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
                    if (!isBuiltinContainer && GUILayout.Button("Duplicate", EditorStyles.MiniButton))
                        duplicateIdx = i;
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

            // Process remove
            if (removeIdx >= 0)
            {
                var removed = _activeLayers[removeIdx];
                _activeLayers.RemoveAt(removeIdx);
                if (_layerGOs.TryGetValue(removed.Name, out var rgo))
                {
                    UnityEngine.Object.Destroy(rgo);
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
                    Enabled = src.Enabled,
                    // Light2D
                    LightType = src.LightType, Intensity = src.Intensity,
                    FalloffIntensity = src.FalloffIntensity,
                    PointLightInnerAngle = src.PointLightInnerAngle,
                    PointLightOuterAngle = src.PointLightOuterAngle,
                    PointLightInnerRadius = src.PointLightInnerRadius,
                    PointLightOuterRadius = src.PointLightOuterRadius,
                    ShapeLightFalloffSize = src.ShapeLightFalloffSize,
                    LightOrder = src.LightOrder, BlendStyleIndex = src.BlendStyleIndex,
                    ShadowsEnabled = src.ShadowsEnabled, ShadowIntensity = src.ShadowIntensity,
                    // ParticleSystem
                    Duration = src.Duration, Loop = src.Loop, Prewarm = src.Prewarm,
                    StartLifetime = src.StartLifetime, StartSpeed = src.StartSpeed,
                    StartSize = src.StartSize, MaxParticles = src.MaxParticles,
                    SimulationSpeed = src.SimulationSpeed, PlayOnAwake = src.PlayOnAwake,
                    GravityModifier = src.GravityModifier, EmissionRate = src.EmissionRate,
                    // SpriteMask
                    AlphaCutoff = src.AlphaCutoff, CustomRange = src.CustomRange,
                    FrontSortingOrder = src.FrontSortingOrder, BackSortingOrder = src.BackSortingOrder,
                    // Shader
                    ShaderName = src.ShaderName, MaskInteraction = src.MaskInteraction,
                    Preset = src.Preset, PresetParam1 = src.PresetParam1, PresetParam2 = src.PresetParam2,
                    ShaderKeywords = src.ShaderKeywords != null ? new System.Collections.Generic.List<string>(src.ShaderKeywords) : new System.Collections.Generic.List<string>(),
                    ShaderFloats = src.ShaderFloats != null ? new System.Collections.Generic.Dictionary<string, float>(src.ShaderFloats) : new System.Collections.Generic.Dictionary<string, float>(),
                    // PrefabEffect
                    EffectName = src.EffectName,
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
            if (GUILayout.Button("+ Sprite", EditorStyles.MiniButton))
                AddNewLayer(zone, VisualLayerType.Sprite);
            if (GUILayout.Button("+ Particle", EditorStyles.MiniButton))
                AddNewLayer(zone, VisualLayerType.ParticleSystem);
            if (GUILayout.Button("+ Light", EditorStyles.MiniButton))
                AddNewLayer(zone, VisualLayerType.Light);
            if (GUILayout.Button("+ Mask", EditorStyles.MiniButton))
                AddNewLayer(zone, VisualLayerType.SpriteMask);
            if (GUILayout.Button("+ Shader", EditorStyles.MiniButton))
                AddNewLayer(zone, VisualLayerType.Shader);
            if (GUILayout.Button("+ PrefabFX", EditorStyles.MiniButton))
                AddNewLayer(zone, VisualLayerType.PrefabEffect);
            GUILayout.EndHorizontal();
        }

        /// <summary>Draw editable properties for a single layer.</summary>
        private void DrawLayerProperties(VisualLayerDef layer, ZoneDef zone, GameObject go)
        {
            bool changed = false;
            bool isBuiltinContainer = layer.Name == "[Nodes]" || layer.Name == "[Roads]";

            // ── Name (rename — not for built-in containers) ────────
            if (!isBuiltinContainer)
            {
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
            }

            // ── Type (read-only) ───────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label("Type:", GUILayout.Width(60));
            GUILayout.Label($"<color=#aaa>{layer.Type}</color>", EditorStyles.RichLabel);
            GUILayout.EndHorizontal();

            // ── Type-specific properties ──────────────────────────
            switch (layer.Type)
            {
                case VisualLayerType.Sprite:
                    changed |= DrawSpriteProperties(layer, zone, go);
                    break;
                case VisualLayerType.Light:
                    changed |= DrawLightProperties(layer, go);
                    break;
                case VisualLayerType.ParticleSystem:
                    changed |= DrawParticleProperties(layer, go);
                    break;
                case VisualLayerType.SpriteMask:
                    changed |= DrawSpriteMaskProperties(layer, zone, go);
                    break;
                case VisualLayerType.Container:
                    changed |= DrawContainerProperties(layer, zone, go);
                    break;
                case VisualLayerType.Shader:
                    changed |= DrawShaderProperties(layer, go);
                    break;
                case VisualLayerType.PrefabEffect:
                    changed |= DrawPrefabEffectProperties(layer, go);
                    break;
            }

            if (changed) ZoneEditingService.MarkDirty();
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE LAYER PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        private bool DrawSpriteProperties(VisualLayerDef layer, ZoneDef zone, GameObject go)
        {
            bool changed = false;

            // Sprite picker (opens full viewport overlay)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sprite:", GUILayout.Width(60));
            string btnText = string.IsNullOrEmpty(layer.SpriteName) ? "(none)  ▶" : $"{layer.SpriteName}  ▶";
            if (GUILayout.Button(btnText, EditorStyles.DropdownButton))
            {
                _spritePickerTargetLayer = layer;
                _spritePickerTargetGO = go;
                _spritePickerFilter = "";
                _spritePickerScroll = Vector2.zero;
                _spritePickerPage = 0;
                _spritePickerFiltered = null;
            }
            GUILayout.EndHorizontal();

            // Sorting Layer (dropdown)
            GUILayout.BeginHorizontal();
            GUILayout.Label("SortLyr:", GUILayout.Width(60));
            if (layer.IsOverride)
            {
                int selIdx = System.Array.IndexOf(SortingLayerNames, layer.SortingLayer);
                if (selIdx < 0) selIdx = System.Array.IndexOf(SortingLayerNames, "Map");
                int newIdx = UnityEngine.GUILayout.SelectionGrid(selIdx, SortingLayerNames, SortingLayerNames.Length, EditorStyles.MiniButton);
                if (newIdx != selIdx && newIdx >= 0 && newIdx < SortingLayerNames.Length)
                {
                    layer.SortingLayer = SortingLayerNames[newIdx];
                    if (go != null)
                    {
                        var sr = go.GetComponent<SpriteRenderer>();
                        if (sr != null) try { sr.sortingLayerName = layer.SortingLayer; } catch { }
                    }
                    changed = true;
                }
            }
            else
            {
                GUILayout.Label($"<color=#aaa>{layer.SortingLayer}</color>", EditorStyles.RichLabel);
            }
            GUILayout.EndHorizontal();

            // Position
            changed |= DrawPositionFields(layer);

            // Scale
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale:", GUILayout.Width(60));
            changed |= FloatField("X", ref layer.ScaleX, 50);
            changed |= FloatField("Y", ref layer.ScaleY, 50);
            GUILayout.EndHorizontal();

            // Color
            changed |= DrawColorFields(layer);

            // Flip
            GUILayout.BeginHorizontal();
            GUILayout.Label("Flip:", GUILayout.Width(60));
            bool newFlipX = GUILayout.Toggle(layer.FlipX, "X", GUILayout.Width(30));
            bool newFlipY = GUILayout.Toggle(layer.FlipY, "Y", GUILayout.Width(30));
            if (newFlipX != layer.FlipX) { layer.FlipX = newFlipX; changed = true; }
            if (newFlipY != layer.FlipY) { layer.FlipY = newFlipY; changed = true; }
            GUILayout.EndHorizontal();

            // Sprite info
            if (layer.SpriteWidth > 0)
                GUILayout.Label($"<color=#666>{layer.SpriteWidth:F0}\u00d7{layer.SpriteHeight:F0} ppu={layer.PPU:F0}</color>",
                    EditorStyles.RichLabel);

            // Apply to GO
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

            return changed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIGHT2D LAYER PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        private bool DrawLightProperties(VisualLayerDef layer, GameObject go)
        {
            bool changed = false;
            var light = go != null ? go.GetComponent<Light2D>() : null;

            // Light Type dropdown
            GUILayout.BeginHorizontal();
            GUILayout.Label("Light:", GUILayout.Width(60));
            string typeName = layer.LightType >= 0 && layer.LightType < LightTypeNames.Length
                ? LightTypeNames[layer.LightType] : layer.LightType.ToString();
            if (GUILayout.Button($"{typeName}  \u25BE", EditorStyles.DropdownButton, GUILayout.Width(100)))
                PopupState.Toggle("layer_lighttype");
            GUILayout.EndHorizontal();

            if (PopupState.IsOpen("layer_lighttype"))
            {
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                for (int i = 0; i < LightTypeNames.Length; i++)
                {
                    var style = i == layer.LightType ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem;
                    if (GUILayout.Button(LightTypeNames[i], style, GUILayout.Height(22)))
                    {
                        layer.LightType = i;
                        if (light != null) light.lightType = (Light2D.LightType)i;
                        changed = true;
                        PopupState.Close();
                    }
                }
                GUILayout.EndVertical();
            }

            // Color
            changed |= DrawColorFields(layer);

            // Intensity
            GUILayout.BeginHorizontal();
            GUILayout.Label("Intensity:", GUILayout.Width(60));
            changed |= FloatField("", ref layer.Intensity, 60);
            GUILayout.EndHorizontal();

            // Falloff
            GUILayout.BeginHorizontal();
            GUILayout.Label("Falloff:", GUILayout.Width(60));
            changed |= FloatField("", ref layer.FalloffIntensity, 60);
            GUILayout.EndHorizontal();

            // Point light params (only for Point type = 3)
            if (layer.LightType == 3)
            {
                GUILayout.Label("<color=#888>Point Light</color>", EditorStyles.RichLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Radius:", GUILayout.Width(60));
                changed |= FloatField("In", ref layer.PointLightInnerRadius, 50);
                changed |= FloatField("Out", ref layer.PointLightOuterRadius, 50);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Angle:", GUILayout.Width(60));
                changed |= FloatField("In", ref layer.PointLightInnerAngle, 50);
                changed |= FloatField("Out", ref layer.PointLightOuterAngle, 50);
                GUILayout.EndHorizontal();
            }

            // Shape light falloff (for Parametric=0, Freeform=1)
            if (layer.LightType == 0 || layer.LightType == 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("ShpFall:", GUILayout.Width(60));
                changed |= FloatField("", ref layer.ShapeLightFalloffSize, 60);
                GUILayout.EndHorizontal();
            }

            // Light Order + Blend Style
            GUILayout.BeginHorizontal();
            GUILayout.Label("Order:", GUILayout.Width(60));
            string loStr = GUILayout.TextField(layer.LightOrder.ToString(), GUILayout.Width(40));
            if (int.TryParse(loStr, out int newLO) && newLO != layer.LightOrder)
            { layer.LightOrder = newLO; changed = true; }
            GUILayout.Label("Blend:", GUILayout.Width(40));
            string bsStr = GUILayout.TextField(layer.BlendStyleIndex.ToString(), GUILayout.Width(30));
            if (int.TryParse(bsStr, out int newBS) && newBS != layer.BlendStyleIndex)
            { layer.BlendStyleIndex = newBS; changed = true; }
            GUILayout.EndHorizontal();

            // Shadows
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shadows:", GUILayout.Width(60));
            bool newShadow = GUILayout.Toggle(layer.ShadowsEnabled, "");
            if (newShadow != layer.ShadowsEnabled)
            { layer.ShadowsEnabled = newShadow; changed = true; }
            if (layer.ShadowsEnabled)
            {
                changed |= FloatField("Int", ref layer.ShadowIntensity, 50);
            }
            GUILayout.EndHorizontal();

            // Position
            changed |= DrawPositionFields(layer);

            // Apply to Light2D
            if (changed && light != null)
            {
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
            }
            if (changed && go != null)
                go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);

            return changed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  PARTICLE SYSTEM LAYER PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        private bool DrawParticleProperties(VisualLayerDef layer, GameObject go)
        {
            bool changed = false;
            var ps = go != null ? go.GetComponent<ParticleSystem>() : null;

            // Duration
            GUILayout.BeginHorizontal();
            GUILayout.Label("Duration:", GUILayout.Width(60));
            changed |= FloatField("", ref layer.Duration, 60);
            GUILayout.EndHorizontal();

            // Loop + Prewarm
            GUILayout.BeginHorizontal();
            GUILayout.Label("Loop:", GUILayout.Width(60));
            bool newLoop = GUILayout.Toggle(layer.Loop, "", GUILayout.Width(20));
            if (newLoop != layer.Loop) { layer.Loop = newLoop; changed = true; }
            GUILayout.Label("Prewarm:", GUILayout.Width(55));
            bool newPrewarm = GUILayout.Toggle(layer.Prewarm, "", GUILayout.Width(20));
            if (newPrewarm != layer.Prewarm) { layer.Prewarm = newPrewarm; changed = true; }
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=#888>Start Values</color>", EditorStyles.RichLabel);

            // Lifetime + Speed
            GUILayout.BeginHorizontal();
            GUILayout.Label("Life:", GUILayout.Width(60));
            changed |= FloatField("", ref layer.StartLifetime, 50);
            GUILayout.Label("Spd:", GUILayout.Width(30));
            changed |= FloatField("", ref layer.StartSpeed, 50);
            GUILayout.EndHorizontal();

            // Size
            GUILayout.BeginHorizontal();
            GUILayout.Label("Size:", GUILayout.Width(60));
            changed |= FloatField("", ref layer.StartSize, 60);
            GUILayout.EndHorizontal();

            // Start Color (uses shared RGBA fields)
            changed |= DrawColorFields(layer);

            // Max Particles
            GUILayout.BeginHorizontal();
            GUILayout.Label("MaxPart:", GUILayout.Width(60));
            string mpStr = GUILayout.TextField(layer.MaxParticles.ToString(), GUILayout.Width(60));
            if (int.TryParse(mpStr, out int newMP) && newMP != layer.MaxParticles)
            { layer.MaxParticles = newMP; changed = true; }
            GUILayout.EndHorizontal();

            // Simulation Speed + Gravity
            GUILayout.BeginHorizontal();
            GUILayout.Label("SimSpd:", GUILayout.Width(60));
            changed |= FloatField("", ref layer.SimulationSpeed, 50);
            GUILayout.Label("Grav:", GUILayout.Width(30));
            changed |= FloatField("", ref layer.GravityModifier, 50);
            GUILayout.EndHorizontal();

            // Emission Rate
            GUILayout.BeginHorizontal();
            GUILayout.Label("Emission:", GUILayout.Width(60));
            changed |= FloatField("", ref layer.EmissionRate, 60);
            GUILayout.EndHorizontal();

            // Play on Awake
            GUILayout.BeginHorizontal();
            GUILayout.Label("AutoPlay:", GUILayout.Width(60));
            bool newPlay = GUILayout.Toggle(layer.PlayOnAwake, "");
            if (newPlay != layer.PlayOnAwake) { layer.PlayOnAwake = newPlay; changed = true; }
            GUILayout.EndHorizontal();

            // Position + Scale
            changed |= DrawPositionFields(layer);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale:", GUILayout.Width(60));
            changed |= FloatField("X", ref layer.ScaleX, 50);
            changed |= FloatField("Y", ref layer.ScaleY, 50);
            GUILayout.EndHorizontal();

            // Apply to ParticleSystem
            if (changed && ps != null)
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
                    psr.sortingOrder = layer.SortingOrder;
            }
            if (changed && go != null)
            {
                go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
                go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
            }

            return changed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE MASK LAYER PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        private bool DrawSpriteMaskProperties(VisualLayerDef layer, ZoneDef zone, GameObject go)
        {
            bool changed = false;
            var mask = go != null ? go.GetComponent<SpriteMask>() : null;

            // Sprite picker (opens full viewport overlay)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sprite:", GUILayout.Width(60));
            string maskBtnText = string.IsNullOrEmpty(layer.SpriteName) ? "(none)  \u25B6" : $"{layer.SpriteName}  \u25B6";
            if (GUILayout.Button(maskBtnText, EditorStyles.DropdownButton))
            {
                _spritePickerTargetLayer = layer;
                _spritePickerTargetGO = go;
                _spritePickerFilter = "";
                _spritePickerScroll = Vector2.zero;
                _spritePickerPage = 0;
                _spritePickerFiltered = null;
            }
            GUILayout.EndHorizontal();

            // Alpha Cutoff
            GUILayout.BeginHorizontal();
            GUILayout.Label("Cutoff:", GUILayout.Width(60));
            changed |= FloatField("", ref layer.AlphaCutoff, 60);
            GUILayout.EndHorizontal();

            // Custom Range
            GUILayout.BeginHorizontal();
            GUILayout.Label("Custom:", GUILayout.Width(60));
            bool newCR = GUILayout.Toggle(layer.CustomRange, "Range");
            if (newCR != layer.CustomRange) { layer.CustomRange = newCR; changed = true; }
            GUILayout.EndHorizontal();

            if (layer.CustomRange)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Front:", GUILayout.Width(60));
                string fsStr = GUILayout.TextField(layer.FrontSortingOrder.ToString(), GUILayout.Width(50));
                if (int.TryParse(fsStr, out int newFS) && newFS != layer.FrontSortingOrder)
                { layer.FrontSortingOrder = newFS; changed = true; }
                GUILayout.Label("Back:", GUILayout.Width(40));
                string bsStr = GUILayout.TextField(layer.BackSortingOrder.ToString(), GUILayout.Width(50));
                if (int.TryParse(bsStr, out int newBSO) && newBSO != layer.BackSortingOrder)
                { layer.BackSortingOrder = newBSO; changed = true; }
                GUILayout.EndHorizontal();
            }

            // Position + Scale
            changed |= DrawPositionFields(layer);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale:", GUILayout.Width(60));
            changed |= FloatField("X", ref layer.ScaleX, 50);
            changed |= FloatField("Y", ref layer.ScaleY, 50);
            GUILayout.EndHorizontal();

            // Apply to SpriteMask
            if (changed && mask != null)
            {
                mask.alphaCutoff = layer.AlphaCutoff;
                mask.isCustomRangeActive = layer.CustomRange;
                if (layer.CustomRange)
                {
                    mask.frontSortingOrder = layer.FrontSortingOrder;
                    mask.backSortingOrder = layer.BackSortingOrder;
                }
                mask.sortingOrder = layer.SortingOrder;
            }
            if (changed && go != null)
            {
                go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
                go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
            }

            return changed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  CONTAINER LAYER PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        private bool DrawContainerProperties(VisualLayerDef layer, ZoneDef zone, GameObject go)
        {
            bool changed = false;

            changed |= DrawPositionFields(layer);

            if (changed && go != null)
            {
                go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);

                // Persist container offsets back to ZoneDef
                if (layer.Name == "[Nodes]")
                {
                    zone.NodesOffsetX = layer.PosX;
                    zone.NodesOffsetY = layer.PosY;
                    _loadedZoneId = null; // rebuild roads
                }
            }

            return changed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SHADER LAYER PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        private static readonly string[] MaskInteractionNames = { "None", "InsideMask", "OutsideMask" };
        private static readonly string[] ShaderPresetNames = { "None", "Scanlines", "Vignette", "Noise", "Gradient", "Checker" };
        private static readonly string[] MapGradientDirNames = { "\u2193", "\u2191", "\u2192", "\u2190" };

        private bool DrawShaderProperties(VisualLayerDef layer, GameObject go)
        {
            bool changed = false;
            var sr = go != null ? go.GetComponent<SpriteRenderer>() : null;

            // Preset
            GUILayout.BeginHorizontal();
            GUILayout.Label("Preset:", GUILayout.Width(60));
            int presetIdx = (int)layer.Preset;
            for (int i = 0; i < ShaderPresetNames.Length; i++)
            {
                if (GUILayout.Toggle(i == presetIdx, ShaderPresetNames[i], EditorStyles.MiniButton))
                {
                    if (i != presetIdx)
                    {
                        layer.Preset = (ShaderPreset)i;
                        ApplyMapPresetDefaults(layer);
                        changed = true;
                    }
                }
            }
            GUILayout.EndHorizontal();

            // Preset parameters
            if (layer.Preset != ShaderPreset.None && layer.Preset != ShaderPreset.Gradient)
            {
                GetMapShaderParamLabels(layer.Preset, out string l1, out string l2);
                if (!string.IsNullOrEmpty(l1))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{l1}:", GUILayout.Width(60));
                    changed |= FloatField("", ref layer.PresetParam1, 50);
                    GUILayout.EndHorizontal();
                }
                if (!string.IsNullOrEmpty(l2))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{l2}:", GUILayout.Width(60));
                    changed |= FloatField("", ref layer.PresetParam2, 50);
                    GUILayout.EndHorizontal();
                }
            }
            else if (layer.Preset == ShaderPreset.Gradient)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Dir:", GUILayout.Width(60));
                int dir = Mathf.RoundToInt(layer.PresetParam1) % 4;
                if (dir < 0) dir += 4;
                for (int i = 0; i < MapGradientDirNames.Length; i++)
                {
                    if (GUILayout.Toggle(i == dir, MapGradientDirNames[i], EditorStyles.MiniButton, GUILayout.Width(28)))
                    {
                        if (i != dir) { layer.PresetParam1 = i; changed = true; }
                    }
                }
                GUILayout.EndHorizontal();
            }

            // Shader name
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shader:", GUILayout.Width(60));
            string newShader = GUILayout.TextField(layer.ShaderName ?? "Sprites/Default");
            if (newShader != layer.ShaderName) { layer.ShaderName = newShader; changed = true; }
            GUILayout.EndHorizontal();

            // Sorting Layer
            GUILayout.BeginHorizontal();
            GUILayout.Label("SortLyr:", GUILayout.Width(60));
            if (layer.IsOverride)
            {
                int selIdx = System.Array.IndexOf(SortingLayerNames, layer.SortingLayer);
                if (selIdx < 0) selIdx = System.Array.IndexOf(SortingLayerNames, "Map");
                int newIdx = GUILayout.SelectionGrid(selIdx, SortingLayerNames, SortingLayerNames.Length, EditorStyles.MiniButton);
                if (newIdx != selIdx && newIdx >= 0 && newIdx < SortingLayerNames.Length)
                {
                    layer.SortingLayer = SortingLayerNames[newIdx];
                    if (sr != null) try { sr.sortingLayerName = layer.SortingLayer; } catch { }
                    changed = true;
                }
            }
            else
            {
                GUILayout.Label($"<color=#aaa>{layer.SortingLayer}</color>", EditorStyles.RichLabel);
            }
            GUILayout.EndHorizontal();

            // Position
            changed |= DrawPositionFields(layer);

            // Scale (controls quad size)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Size:", GUILayout.Width(60));
            changed |= FloatField("W", ref layer.ScaleX, 50);
            changed |= FloatField("H", ref layer.ScaleY, 50);
            GUILayout.EndHorizontal();

            // Color
            changed |= DrawColorFields(layer);

            // Mask Interaction
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mask:", GUILayout.Width(60));
            for (int i = 0; i < MaskInteractionNames.Length; i++)
            {
                if (GUILayout.Toggle(i == layer.MaskInteraction, MaskInteractionNames[i], EditorStyles.MiniButton))
                {
                    if (i != layer.MaskInteraction) { layer.MaskInteraction = i; changed = true; }
                }
            }
            GUILayout.EndHorizontal();

            // ── Shader Effects (AllIn1SpriteShader keywords) ────────
            changed |= DrawMapShaderEffects(layer);

            // Apply to SpriteRenderer
            if (changed && go != null)
            {
                go.transform.localPosition = new Vector3(layer.PosX, layer.PosY, layer.PosZ);
                go.transform.localScale = new Vector3(layer.ScaleX, layer.ScaleY, 1f);
                if (sr != null)
                {
                    sr.sprite = ShaderPresetGenerator.GetPresetSprite(layer.Preset, layer.PresetParam1, layer.PresetParam2);
                    sr.color = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
                    sr.sortingOrder = layer.SortingOrder;
                    sr.maskInteraction = (SpriteMaskInteraction)layer.MaskInteraction;
                    if (!string.IsNullOrEmpty(layer.ShaderName))
                    {
                        var shader = Shader.Find(layer.ShaderName) ?? Resources.Load<Shader>(layer.ShaderName);
                        if (shader != null && (sr.sharedMaterial == null || sr.sharedMaterial.shader.name != layer.ShaderName))
                            sr.material = new Material(shader);
                    }
                    ShaderEffectRegistry.ApplyToMaterial(sr.material, layer.ShaderKeywords, layer.ShaderFloats);
                }
            }

            return changed;
        }

        private static bool DrawMapShaderEffects(VisualLayerDef layer)
        {
            bool changed = false;
            GUILayout.Label("<b><size=10>Shader FX</size></b>", EditorStyles.RichLabel);

            var effects = ShaderEffectRegistry.Effects;
            string lastCat = null;
            for (int e = 0; e < effects.Length; e++)
            {
                ref readonly var fx = ref effects[e];
                bool active = layer.ShaderKeywords != null && layer.ShaderKeywords.Contains(fx.Keyword);

                if (fx.Category != lastCat)
                {
                    lastCat = fx.Category;
                    GUILayout.Label($"<color=#888><size=9>{lastCat}</size></color>", EditorStyles.RichLabel);
                }

                GUILayout.BeginHorizontal();
                bool newActive = GUILayout.Toggle(active, fx.DisplayName, EditorStyles.MiniButton, GUILayout.Width(90));
                if (newActive != active)
                {
                    changed = true;
                    if (layer.ShaderKeywords == null) layer.ShaderKeywords = new System.Collections.Generic.List<string>();
                    if (newActive)
                    {
                        layer.ShaderKeywords.Add(fx.Keyword);
                        if (layer.ShaderFloats == null) layer.ShaderFloats = new System.Collections.Generic.Dictionary<string, float>();
                        foreach (var p in fx.Props)
                            if (!layer.ShaderFloats.ContainsKey(p.Name))
                                layer.ShaderFloats[p.Name] = p.Default;
                    }
                    else
                    {
                        layer.ShaderKeywords.Remove(fx.Keyword);
                        if (layer.ShaderFloats != null)
                            foreach (var p in fx.Props)
                                layer.ShaderFloats.Remove(p.Name);
                    }
                }

                if (newActive && fx.Props != null)
                {
                    foreach (var p in fx.Props)
                    {
                        GUILayout.Label(p.Label, GUILayout.Width(45));
                        if (layer.ShaderFloats == null) layer.ShaderFloats = new System.Collections.Generic.Dictionary<string, float>();
                        if (!layer.ShaderFloats.TryGetValue(p.Name, out float val)) val = p.Default;
                        string valStr = GUILayout.TextField(val.ToString("F2"), GUILayout.Width(45));
                        if (float.TryParse(valStr, out float nv) && System.Math.Abs(nv - val) > 0.0001f)
                        {
                            layer.ShaderFloats[p.Name] = Mathf.Clamp(nv, p.Min, p.Max);
                            changed = true;
                        }
                    }
                }
                GUILayout.EndHorizontal();
            }
            return changed;
        }

        private static void ApplyMapPresetDefaults(VisualLayerDef layer)
        {
            switch (layer.Preset)
            {
                case ShaderPreset.Scanlines:
                    layer.PresetParam1 = 2; layer.PresetParam2 = 2;
                    layer.ColorR = 0; layer.ColorG = 0; layer.ColorB = 0; layer.ColorA = 0.3f;
                    break;
                case ShaderPreset.Vignette:
                    layer.PresetParam1 = 0.8f; layer.PresetParam2 = 0.4f;
                    layer.ColorR = 0; layer.ColorG = 0; layer.ColorB = 0; layer.ColorA = 0.8f;
                    break;
                case ShaderPreset.Noise:
                    layer.PresetParam1 = 2; layer.PresetParam2 = 0.5f;
                    layer.ColorR = 0; layer.ColorG = 0; layer.ColorB = 0; layer.ColorA = 0.15f;
                    break;
                case ShaderPreset.Gradient:
                    layer.PresetParam1 = 0; layer.PresetParam2 = 0;
                    layer.ColorR = 0; layer.ColorG = 0; layer.ColorB = 0; layer.ColorA = 0.5f;
                    break;
                case ShaderPreset.Checkerboard:
                    layer.PresetParam1 = 8; layer.PresetParam2 = 0.3f;
                    layer.ColorR = 0.5f; layer.ColorG = 0.5f; layer.ColorB = 0.5f; layer.ColorA = 0.3f;
                    break;
            }
        }

        private static void GetMapShaderParamLabels(ShaderPreset p, out string l1, out string l2)
        {
            switch (p)
            {
                case ShaderPreset.Scanlines:    l1 = "Width"; l2 = "Gap"; break;
                case ShaderPreset.Vignette:     l1 = "Intensity"; l2 = "Softness"; break;
                case ShaderPreset.Noise:        l1 = "Grain"; l2 = "Density"; break;
                case ShaderPreset.Checkerboard: l1 = "Size"; l2 = "Intensity"; break;
                default: l1 = "P1"; l2 = "P2"; break;
            }
        }

        // ── PrefabEffect ─────────────────────────────────────────────

        private bool DrawPrefabEffectProperties(VisualLayerDef layer, GameObject go)
        {
            bool changed = false;

            // Current effect name display + manual entry
            GUILayout.BeginHorizontal();
            GUILayout.Label("Effect:", GUILayout.Width(60));
            string newName = GUILayout.TextField(layer.EffectName ?? "");
            if (newName != (layer.EffectName ?? "")) { layer.EffectName = newName; changed = true; }
            if (GUILayout.Button(_fxPickerOpen ? "\u25B2" : "\u25BC", EditorStyles.MiniButton, GUILayout.Width(22)))
                _fxPickerOpen = !_fxPickerOpen;
            GUILayout.EndHorizontal();

            // Inline filterable picker
            if (_fxPickerOpen)
            {
                var allNames = GetVfxEffectNames();
                if (allNames.Length == 0)
                {
                    GUILayout.Label("<color=#888><size=9>No effects loaded (game must be running)</size></color>", EditorStyles.RichLabel);
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("\u2315", GUILayout.Width(14));
                    string newFilter = GUILayout.TextField(_fxPickerFilter);
                    if (newFilter != _fxPickerFilter)
                    {
                        _fxPickerFilter = newFilter;
                        _fxPickerFiltered = null;
                    }
                    if (GUILayout.Button("Clear", EditorStyles.MiniButton, GUILayout.Width(42)))
                    {
                        _fxPickerFilter = "";
                        _fxPickerFiltered = null;
                    }
                    GUILayout.EndHorizontal();

                    if (_fxPickerFiltered == null)
                    {
                        string filt = (_fxPickerFilter ?? "").ToLowerInvariant();
                        var filtered = new System.Collections.Generic.List<string>();
                        foreach (var n in allNames)
                            if (string.IsNullOrEmpty(filt) || n.ToLower().Contains(filt))
                                filtered.Add(n);
                        _fxPickerFiltered = filtered.ToArray();
                    }

                    GUILayout.Label($"<color=#888><size=9>{_fxPickerFiltered.Length} effects</size></color>", EditorStyles.RichLabel);

                    _fxPickerScroll = GUILayout.BeginScrollView(_fxPickerScroll, GUILayout.Height(Mathf.Min(160, _fxPickerFiltered.Length * 18 + 4)));
                    for (int i = 0; i < _fxPickerFiltered.Length; i++)
                    {
                        string n = _fxPickerFiltered[i];
                        bool isCurrent = n == (layer.EffectName ?? "");
                        var style = isCurrent ? EditorStyles.DropdownItemSel : EditorStyles.ListItem;
                        if (GUILayout.Button(n, style))
                        {
                            layer.EffectName = n;
                            changed = true;
                            _fxPickerOpen = false;
                        }
                    }
                    GUILayout.EndScrollView();
                }
            }

            return changed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SHARED PROPERTY WIDGETS
        // ═══════════════════════════════════════════════════════════════

        private bool DrawPositionFields(VisualLayerDef layer)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pos:", GUILayout.Width(60));
            changed |= FloatField("X", ref layer.PosX, 50);
            changed |= FloatField("Y", ref layer.PosY, 50);
            changed |= FloatField("Z", ref layer.PosZ, 50);
            GUILayout.EndHorizontal();
            return changed;
        }

        private static bool DrawColorFields(VisualLayerDef layer)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Color:", GUILayout.Width(60));
            changed |= FloatField("R", ref layer.ColorR, 40);
            changed |= FloatField("G", ref layer.ColorG, 40);
            changed |= FloatField("B", ref layer.ColorB, 40);
            changed |= FloatField("A", ref layer.ColorA, 40);
            GUILayout.EndHorizontal();

            // Color preview swatch
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
            GUILayout.Box("", GUILayout.Height(6), GUILayout.ExpandWidth(true));
            GUI.backgroundColor = prevBg;

            return changed;
        }



        /// <summary>Apply the Enabled state to the appropriate component on the GO.</summary>
        private static void ApplyEnabled(VisualLayerDef layer, GameObject go)
        {
            if (go == null) return;
            switch (layer.Type)
            {
                case VisualLayerType.Sprite:
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.enabled = layer.Enabled;
                    break;
                case VisualLayerType.Light:
                    var light = go.GetComponent<Light2D>();
                    if (light != null) light.enabled = layer.Enabled;
                    break;
                case VisualLayerType.ParticleSystem:
                    var ps = go.GetComponent<ParticleSystem>();
                    if (ps != null) { if (layer.Enabled) ps.Play(); else ps.Stop(); }
                    var psr = go.GetComponent<ParticleSystemRenderer>();
                    if (psr != null) psr.enabled = layer.Enabled;
                    break;
                case VisualLayerType.SpriteMask:
                    var sm = go.GetComponent<SpriteMask>();
                    if (sm != null) sm.enabled = layer.Enabled;
                    break;
                case VisualLayerType.Shader:
                    var shaderSr = go.GetComponent<SpriteRenderer>();
                    if (shaderSr != null) shaderSr.enabled = layer.Enabled;
                    break;
            }
        }

        /// <summary>Draw a small labeled float field. Returns true if value changed.</summary>
        private static bool FloatField(string label, ref float value, float width)
        {
            GUILayout.Label(label, GUILayout.Width(12));
            string s = GUILayout.TextField(value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), GUILayout.Width(width));
            if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float newVal) && Mathf.Abs(newVal - value) > 0.001f)
            {
                value = newVal;
                return true;
            }
            return false;
        }

        private void AddNewLayer(ZoneDef zone, VisualLayerType type)
        {
            // New layers go on top (highest sorting order + 1)
            int maxOrder = 0;
            foreach (var l in _activeLayers)
                if (l.SortingOrder > maxOrder) maxOrder = l.SortingOrder;

            var newLayer = new VisualLayerDef
            {
                Name = $"custom_{type.ToString().ToLower()}_{_activeLayers.Count}",
                Type = type,
                SortingOrder = maxOrder + 1,
                IsOverride = true,
                Visible = true,
                Enabled = true,
            };

            // Set reasonable defaults per type
            if (type == VisualLayerType.Light)
            {
                newLayer.LightType = 3; // Point
                newLayer.Intensity = 1f;
                newLayer.FalloffIntensity = 0.5f;
                newLayer.PointLightInnerRadius = 0.5f;
                newLayer.PointLightOuterRadius = 3f;
                newLayer.ColorR = 1f; newLayer.ColorG = 0.95f; newLayer.ColorB = 0.8f; newLayer.ColorA = 1f;
            }
            else if (type == VisualLayerType.ParticleSystem)
            {
                newLayer.Duration = 5f;
                newLayer.Loop = true;
                newLayer.StartLifetime = 2f;
                newLayer.StartSpeed = 1f;
                newLayer.StartSize = 0.3f;
                newLayer.MaxParticles = 100;
                newLayer.EmissionRate = 10f;
                newLayer.PlayOnAwake = true;
                newLayer.SimulationSpeed = 1f;
            }
            else if (type == VisualLayerType.Shader)
            {
                newLayer.ShaderName = "Sprites/Default";
                newLayer.ScaleX = 19.2f;
                newLayer.ScaleY = 10.8f;
                newLayer.ColorA = 0.5f;
            }
            else if (type == VisualLayerType.PrefabEffect)
            {
                newLayer.EffectName = "";
            }

            zone.VisualLayers.Add(newLayer);
            _activeLayers.Add(newLayer);
            _expandedLayerIdx = _activeLayers.Count - 1;
            _layerRenameBuffer = newLayer.Name;
            _loadedZoneId = null; // rebuild to create the GO
            ZoneEditingService.MarkDirty();
        }

        /// <summary>Apply the current SortingOrder from a layer def to its live GO renderer.</summary>
        private void ApplySortingOrder(VisualLayerDef layer)
        {
            if (!_layerGOs.TryGetValue(layer.Name, out var go) || go == null) return;

            switch (layer.Type)
            {
                case VisualLayerType.Sprite:
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.sortingOrder = layer.SortingOrder;
                    break;
                case VisualLayerType.ParticleSystem:
                    var psr = go.GetComponent<ParticleSystemRenderer>();
                    if (psr != null) psr.sortingOrder = layer.SortingOrder;
                    break;
                case VisualLayerType.SpriteMask:
                    var sm = go.GetComponent<SpriteMask>();
                    if (sm != null) sm.sortingOrder = layer.SortingOrder;
                    break;
                case VisualLayerType.Shader:
                    var shaderSr = go.GetComponent<SpriteRenderer>();
                    if (shaderSr != null) shaderSr.sortingOrder = layer.SortingOrder;
                    break;
            }
        }
    }
}
