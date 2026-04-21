using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel + interactive viewport for creating and editing custom combat backgrounds.
    /// Lists backgrounds in the current zone, allows adding/removing layers,
    /// reordering, and editing sprite/sorting/position/scale/color per layer.
    /// Supports cloning from existing game backgrounds.
    /// </summary>
    public partial class BackgroundEditor
    {
        private readonly ModEditor _parent;

        // Selection / UI state
        private string _selectedBgId;
        private int _expandedLayerIdx = -1;
        private int _selectedLayerIdx = -1;
        private string _layerRenameBuffer = "";
        private bool _secLayers = true;
        private bool _secClone = false;
        private Vector2 _cloneScroll;
        private string _cloneFilter = "";
        private Vector2 _layerScrollPos;

        // Patch browser state
        private bool _showPatchBrowser;
        private Vector2 _patchBrowserScroll;
        private string _patchFilter = "";

        // Viewport
        private ViewportRenderer _vp;
        private static readonly Vector3 ViewportOrigin = new(-7000f, -5000f, 0f);

        // Scene
        private GameObject _previewRoot;
        private Transform _bgRoot;
        private readonly Dictionary<int, GameObject> _layerGOs = new();
        private string _loadedBgId;
        internal bool _viewportDirty;

        // Viewport display state
        private int _hoveredLayerIdx = -1;
        private bool _showCameraBounds = true;
        private GUIStyle _vpCenteredStyle;

        // Layer drag state
        private int _dragLayerIdx = -1;
        private Vector2 _dragStartWorld;
        private Vector3 _dragLayerStartPos;

        // Layer scale state
        private int _scaleLayerIdx = -1;
        private int _scaleHandleIndex = -1;
        private Vector2 _scaleStartWorld;
        private float _scaleStartScaleX, _scaleStartScaleY;
        private float _scaleStartPosX, _scaleStartPosY;
        private Bounds _scaleStartBounds;
        private const float ScaleHandleSizePx = 8f;

        // Rotation drag state
        private int _rotateLayerIdx = -1;
        private float _rotateStartRotation;
        private float _rotateStartAngle;

        // Layer overlap cycling
        private readonly List<int> _layerOverlapCandidates = new();
        private int _layerOverlapCycleIndex;

        // Sprite picker state (viewport overlay)
        private int _spritePickerTargetIdx = -1;
        private string _spritePickerFilter = "";
        private Vector2 _spritePickerScroll;
        private int _spritePickerPage;
        private const int SpritePickerPageSize = 60;
        private readonly Dictionary<string, Texture2D> _spriteThumbCache = new();
        private string[] _spritePickerFiltered;
        private string _lastSpriteFilter;

        // PrefabFX picker state
        private bool _fxPickerOpen;
        private string _fxPickerFilter = "";
        private Vector2 _fxPickerScroll;
        private string[] _fxPickerFiltered;

        // Constants
        private const float GameOrthoSize = 5.4f;
        private const float GameAspect = 16f / 9f;

        public string SelectedBackgroundId
        {
            get => _selectedBgId;
            set { _selectedBgId = value; _expandedLayerIdx = -1; _selectedLayerIdx = -1; }
        }

        public BackgroundEditor(ModEditor parent)
        {
            _parent = parent;
            _vp = new ViewportRenderer(ViewportOrigin, 1280, 720, 5.4f,
                new Color(0.08f, 0.08f, 0.1f, 1f), -102f);
        }

        /// <summary>Mark the current background dirty: auto-save to disk.</summary>
        internal void MarkDirty()
        {
            var proj = Tabs.ModManagerPanel.ActiveProject;
            if (proj == null) return;
            if (!string.IsNullOrEmpty(_selectedBgId))
            {
                if (proj.Backgrounds.TryGetValue(_selectedBgId, out var bg))
                    ModProjectLoader.SaveEntity(proj, "backgrounds", bg.BackgroundId, bg);
                else if (proj.BackgroundPatches.TryGetValue(_selectedBgId, out var bgp))
                    ModProjectLoader.SaveEntity(proj, "backgrounds", bgp.BackgroundId, bgp, isPatch: true);
            }
            proj.IsDirty = true;
            proj.LastChangeTime = UnityEngine.Time.realtimeSinceStartup;
        }

        /// <summary>Force a full viewport rebuild on the next frame.</summary>
        public void ForceRebuild() { _loadedBgId = null; _viewportDirty = false; }

        // ═══════════════════════════════════════════════════════════════
        //  PANEL
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            var proj = Tabs.ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("No mod project active."); return; }

            // Build combined backgrounds dict (new + patches)
            var allBackgrounds = new Dictionary<string, BackgroundDef>();
            foreach (var kvp in proj.Backgrounds) allBackgrounds[kvp.Key] = kvp.Value;
            foreach (var kvp in proj.BackgroundPatches)
                if (!allBackgrounds.ContainsKey(kvp.Key)) allBackgrounds[kvp.Key] = kvp.Value;

            // ── Background selector ──────────────────────────────
            var bgIds = allBackgrounds.Keys.OrderBy(k => k).ToList();
            string sel = EditorFields.EntitySelector(
                _selectedBgId, bgIds,
                id =>
                {
                    string badge = proj.Backgrounds.ContainsKey(id) ? "[NEW] " :
                                   proj.BackgroundPatches.ContainsKey(id) ? "[PATCH] " : "";
                    return allBackgrounds.TryGetValue(id, out var bg) && !string.IsNullOrEmpty(bg.DisplayName)
                        ? $"{badge}{id}  ({bg.DisplayName})" : $"{badge}{id}";
                },
                "bg_sel");
            if (sel != _selectedBgId)
            {
                _selectedBgId = sel;
                _expandedLayerIdx = -1;
            }

            // ── Action bar ───────────────────────────────────────
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New BG", EditorStyles.MiniButton, GUILayout.Width(70)))
            {
                string prefix = proj.ModId;
                string newId = $"{prefix}_bg_{proj.Backgrounds.Count}";
                int suffix = 0;
                while (proj.Backgrounds.ContainsKey(newId) || proj.BackgroundPatches.ContainsKey(newId))
                    newId = $"{prefix}_bg_{++suffix}";

                var newBg = new BackgroundDef
                {
                    BackgroundId = newId,
                    DisplayName = "New Background",
                };
                proj.Backgrounds[newId] = newBg;

                _selectedBgId = newId;
                _expandedLayerIdx = -1;
                ModProjectLoader.SaveEntity(proj, "backgrounds", newId, newBg);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Patch Base \u25BE", EditorStyles.MiniButton, GUILayout.Width(100)))
                _showPatchBrowser = !_showPatchBrowser;

            if (!string.IsNullOrEmpty(_selectedBgId))
            {
                bool isNew = proj.Backgrounds.ContainsKey(_selectedBgId);
                bool isPatch = proj.BackgroundPatches.ContainsKey(_selectedBgId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Backgrounds.Remove(_selectedBgId);
                        ModProjectLoader.DeleteEntity(proj, "backgrounds", _selectedBgId);
                        _selectedBgId = bgIds.FirstOrDefault(k => k != _selectedBgId);
                        _expandedLayerIdx = -1;
                        proj.IsDirty = true;
                    }
                }
                else if (isPatch)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.BackgroundPatches.Remove(_selectedBgId);
                        ModProjectLoader.DeleteEntity(proj, "backgrounds", _selectedBgId, true);
                        _selectedBgId = bgIds.FirstOrDefault(k => k != _selectedBgId);
                        _expandedLayerIdx = -1;
                        proj.IsDirty = true;
                    }
                }
            }
            GUILayout.EndHorizontal();

            // ── Patch browser ────────────────────────────────────
            if (_showPatchBrowser)
                DrawPatchBrowser(proj);

            EditorStyles.Separator();

            if (string.IsNullOrEmpty(_selectedBgId) || !allBackgrounds.TryGetValue(_selectedBgId, out var def))
            {
                GUILayout.Label("<i>Select a background above, or create a new one.</i>", EditorStyles.RichLabel);
                return;
            }

            // ── Basic fields ────────────────────────────────────
            string prevBgId = def.BackgroundId;
            def.BackgroundId = EditorFields.TextField("Background ID", def.BackgroundId);
            if (def.BackgroundId != prevBgId && !string.IsNullOrEmpty(def.BackgroundId))
            {
                bool wasNew = proj.Backgrounds.ContainsKey(prevBgId);
                bool wasPatch = proj.BackgroundPatches.ContainsKey(prevBgId);
                var dict = wasNew ? proj.Backgrounds : wasPatch ? proj.BackgroundPatches : null;
                if (dict != null && !proj.Backgrounds.ContainsKey(def.BackgroundId) && !proj.BackgroundPatches.ContainsKey(def.BackgroundId))
                {
                    dict.Remove(prevBgId);
                    dict[def.BackgroundId] = def;
                    ModProjectLoader.DeleteEntity(proj, "backgrounds", prevBgId, wasPatch);
                    ModProjectLoader.SaveEntity(proj, "backgrounds", def.BackgroundId, def, wasPatch);
                    _selectedBgId = def.BackgroundId;
                }
                else
                    def.BackgroundId = prevBgId;
            }
            def.DisplayName = EditorFields.TextField("Display Name", def.DisplayName);

            // ── Clone from game ─────────────────────────────────
            if (EditorFields.Section("Clone from Game BG", ref _secClone))
                DrawCloneSection(def);

            // ── Layers ──────────────────────────────────────────
            if (EditorFields.Section($"Layers ({def.Layers.Count})", ref _secLayers))
                DrawLayersSection(def);
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLONE FROM GAME
        // ═══════════════════════════════════════════════════════════════

        private void DrawCloneSection(BackgroundDef def)
        {
            GUILayout.Label("<color=#888>Clone an existing game background's layers into this definition.</color>",
                EditorStyles.RichLabel);

            _cloneFilter = EditorFields.TextField("Filter", _cloneFilter);

            var allBgNames = EntityPreviewRenderer.GetAllBackgroundNames();
            if (allBgNames.Count == 0)
            {
                GUILayout.Label("<color=#cc6600>Background cache not ready (enter a combat first).</color>",
                    EditorStyles.RichLabel);
                return;
            }

            string filterLow = (_cloneFilter ?? "").ToLower();
            _cloneScroll = GUILayout.BeginScrollView(_cloneScroll, GUILayout.Height(140));
            int shown = 0;
            foreach (var bgName in allBgNames.OrderBy(n => n))
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !bgName.ToLower().Contains(filterLow)) continue;
                shown++;

                GUILayout.BeginHorizontal();
                GUILayout.Label(bgName.Replace('_', ' '), GUILayout.Width(180));
                if (GUILayout.Button("Clone", EditorStyles.MiniButton, GUILayout.Width(50)))
                {
                    CloneFromGameBackground(def, bgName);
                    _secClone = false;
                    _viewportDirty = true;
                    MarkDirty();
                }
                if (GUILayout.Button("Inspect", EditorStyles.MiniButton, GUILayout.Width(55)))
                    InspectGameBackground(bgName);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        /// <summary>Clone layers from a game background prefab into the def.</summary>
        private static void CloneFromGameBackground(BackgroundDef def, string bgName)
        {
            var prefab = EntityPreviewRenderer.GetBackgroundPrefab(bgName);
            if (prefab == null) return;

            def.Layers.Clear();
            for (int i = 0; i < prefab.transform.childCount; i++)
            {
                var child = prefab.transform.GetChild(i);
                // Skip holiday overlays
                if (child.name == "halloween" || child.name == "Lunar") continue;

                var sr = child.GetComponent<SpriteRenderer>();
                if (sr == null) continue;

                var layer = new BackgroundLayerDef
                {
                    Name = child.name,
                    SpriteName = sr.sprite != null ? sr.sprite.name : "",
                    SortingOrder = sr.sortingOrder,
                    SortingLayer = sr.sortingLayerName ?? "Default",
                    PosX = child.localPosition.x,
                    PosY = child.localPosition.y,
                    PosZ = child.localPosition.z,
                    ScaleX = child.localScale.x,
                    ScaleY = child.localScale.y,
                    ColorR = sr.color.r,
                    ColorG = sr.color.g,
                    ColorB = sr.color.b,
                    ColorA = sr.color.a,
                    FlipX = sr.flipX,
                    FlipY = sr.flipY,
                    Visible = sr.enabled,
                };
                def.Layers.Add(layer);
            }

            Plugin.Log.LogInfo($"[BackgroundEditor] Cloned {def.Layers.Count} layers from '{bgName}'");
        }

        // ═══════════════════════════════════════════════════════════════
        //  PATCH BROWSER — list game backgrounds to patch
        // ═══════════════════════════════════════════════════════════════

        private void DrawPatchBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Select a game background to patch:</color>",
                EditorStyles.RichLabel);
            _patchFilter = EditorFields.TextField("Filter", _patchFilter);

            var allBgNames = EntityPreviewRenderer.GetAllBackgroundNames();
            if (allBgNames.Count == 0)
            {
                GUILayout.Label("<color=#cc6600>Background cache not ready (enter a combat first).</color>",
                    EditorStyles.RichLabel);
                return;
            }

            _patchBrowserScroll = GUILayout.BeginScrollView(_patchBrowserScroll, GUILayout.Height(180));
            string filterLow = (_patchFilter ?? "").ToLower();
            int shown = 0;
            foreach (var bgName in allBgNames.OrderBy(n => n))
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !bgName.ToLower().Contains(filterLow)) continue;
                if (proj.Backgrounds.ContainsKey(bgName) || proj.BackgroundPatches.ContainsKey(bgName)) continue;
                shown++;
                if (GUILayout.Button(bgName, EditorStyles.LinkButton))
                {
                    var patchDef = new BackgroundDef
                    {
                        BackgroundId = bgName,
                        DisplayName = bgName,
                    };
                    CloneFromGameBackground(patchDef, bgName);
                    proj.BackgroundPatches[bgName] = patchDef;

                    _selectedBgId = bgName;
                    _expandedLayerIdx = -1;
                    ModProjectLoader.SaveEntity(proj, "backgrounds", bgName, patchDef, isPatch: true);
                    _showPatchBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        /// <summary>Log the hierarchy of a game background prefab to the console.</summary>
        private static void InspectGameBackground(string bgName)
        {
            var prefab = EntityPreviewRenderer.GetBackgroundPrefab(bgName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[BackgroundEditor] Prefab '{bgName}' not found in cache");
                return;
            }

            Plugin.Log.LogInfo($"[BackgroundEditor] === Inspecting '{bgName}' ({prefab.transform.childCount} children) ===");
            for (int i = 0; i < prefab.transform.childCount; i++)
            {
                var child = prefab.transform.GetChild(i);
                var sr = child.GetComponent<SpriteRenderer>();
                var anim = child.GetComponent<Animator>();
                string sprName = sr?.sprite?.name ?? "(no sprite)";
                string animTag = anim != null ? " [Animated]" : "";
                string sortInfo = sr != null ? $" sort={sr.sortingLayerName}:{sr.sortingOrder}" : "";
                Plugin.Log.LogInfo($"  [{i}] {child.name}  sprite={sprName}{sortInfo}{animTag}  pos={child.localPosition}  scale={child.localScale}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYERS SECTION
        // ═══════════════════════════════════════════════════════════════

        private void DrawLayersSection(BackgroundDef def)
        {
            // Header row
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#666>On</color>", EditorStyles.RichLabel, GUILayout.Width(16));
            GUILayout.Label("<color=#666>Vis</color>", EditorStyles.RichLabel, GUILayout.Width(22));
            GUILayout.Label("<color=#666># Layer</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#666>\u25B2\u25BC</color>", EditorStyles.RichLabel, GUILayout.Width(44));
            GUILayout.EndHorizontal();

            int removeIdx = -1;
            int duplicateIdx = -1;
            int moveUpIdx = -1;
            int moveDownIdx = -1;

            _layerScrollPos = GUILayout.BeginScrollView(_layerScrollPos);

            for (int i = 0; i < def.Layers.Count; i++)
            {
                var layer = def.Layers[i];
                bool isExpanded = _expandedLayerIdx == i;
                bool isSelected = _selectedLayerIdx == i;

                GUILayout.BeginVertical(isExpanded ? GUI.skin.box : GUIStyle.none);
                GUILayout.BeginHorizontal();

                // ── Enabled checkbox (viewport-only toggle) ──
                bool newEnabled = GUILayout.Toggle(layer.Enabled, "", GUILayout.Width(14));
                if (newEnabled != layer.Enabled)
                {
                    layer.Enabled = newEnabled;
                    if (_layerGOs.TryGetValue(i, out var go) && go != null)
                        go.SetActive(newEnabled && layer.Visible);
                }

                // ── Visible toggle (persisted — controls built prefab) ──
                {
                    string visLabel = layer.Visible
                        ? "<color=#8c8>V</color>"
                        : "<color=#664400>H</color>";
                    if (GUILayout.Button(visLabel, EditorStyles.MiniButton, GUILayout.Width(20)))
                    {
                        layer.Visible = !layer.Visible;
                        if (_layerGOs.TryGetValue(i, out var go) && go != null)
                            go.SetActive(layer.Enabled && layer.Visible);
                        MarkDirty();
                    }
                }

                // Order number
                GUILayout.Label($"<color=#555>{layer.SortingOrder}</color>", EditorStyles.RichLabel, GUILayout.Width(38));

                // Clickable label to expand/collapse + select
                string layerName = string.IsNullOrEmpty(layer.Name) ? $"(layer {i})" : layer.Name;
                string typeBadge = layer.Type != VisualLayerType.Sprite
                    ? $"<color=#666>[{layer.Type}]</color> " : "";
                string label = typeBadge + layerName;
                if (GUILayout.Button(isSelected ? $"<b>{label}</b>" : label, EditorStyles.ListItem))
                {
                    if (isExpanded)
                    {
                        _expandedLayerIdx = -1;
                        _selectedLayerIdx = -1;
                    }
                    else
                    {
                        _expandedLayerIdx = i;
                        _selectedLayerIdx = i;
                        _layerRenameBuffer = layer.Name;
                    }
                }

                // Move up/down
                if (GUILayout.Button("\u25B2", EditorStyles.MiniButton, GUILayout.Width(20)))
                    moveUpIdx = i;
                if (GUILayout.Button("\u25BC", EditorStyles.MiniButton, GUILayout.Width(20)))
                    moveDownIdx = i;

                // Remove
                if (GUILayout.Button("\u2716", EditorStyles.MiniButton, GUILayout.Width(20)))
                    removeIdx = i;

                GUILayout.EndHorizontal();

                // Expanded property editor
                if (isExpanded)
                {
                    DrawLayerProperties(def, i);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Duplicate", EditorStyles.MiniButton))
                        duplicateIdx = i;
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();

            // Process actions
            if (moveUpIdx > 0)
            {
                var tmp = def.Layers[moveUpIdx];
                def.Layers[moveUpIdx] = def.Layers[moveUpIdx - 1];
                def.Layers[moveUpIdx - 1] = tmp;
                if (_expandedLayerIdx == moveUpIdx) _expandedLayerIdx--;
                else if (_expandedLayerIdx == moveUpIdx - 1) _expandedLayerIdx++;
                if (_selectedLayerIdx == moveUpIdx) _selectedLayerIdx--;
                else if (_selectedLayerIdx == moveUpIdx - 1) _selectedLayerIdx++;
                _viewportDirty = true;
                MarkDirty();
            }
            if (moveDownIdx >= 0 && moveDownIdx < def.Layers.Count - 1)
            {
                var tmp = def.Layers[moveDownIdx];
                def.Layers[moveDownIdx] = def.Layers[moveDownIdx + 1];
                def.Layers[moveDownIdx + 1] = tmp;
                if (_expandedLayerIdx == moveDownIdx) _expandedLayerIdx++;
                else if (_expandedLayerIdx == moveDownIdx + 1) _expandedLayerIdx--;
                if (_selectedLayerIdx == moveDownIdx) _selectedLayerIdx++;
                else if (_selectedLayerIdx == moveDownIdx + 1) _selectedLayerIdx--;
                _viewportDirty = true;
                MarkDirty();
            }
            if (removeIdx >= 0)
            {
                def.Layers.RemoveAt(removeIdx);
                if (_expandedLayerIdx == removeIdx) _expandedLayerIdx = -1;
                else if (_expandedLayerIdx > removeIdx) _expandedLayerIdx--;
                if (_selectedLayerIdx == removeIdx) _selectedLayerIdx = -1;
                else if (_selectedLayerIdx > removeIdx) _selectedLayerIdx--;
                _viewportDirty = true;
                MarkDirty();
            }
            if (duplicateIdx >= 0)
            {
                var src = def.Layers[duplicateIdx];
                var dup = new BackgroundLayerDef
                {
                    Name = src.Name + "_copy",
                    Type = src.Type,
                    SpriteName = src.SpriteName,
                    SortingOrder = src.SortingOrder + 1,
                    SortingLayer = src.SortingLayer,
                    PosX = src.PosX, PosY = src.PosY, PosZ = src.PosZ,
                    ScaleX = src.ScaleX, ScaleY = src.ScaleY,
                    ColorR = src.ColorR, ColorG = src.ColorG, ColorB = src.ColorB, ColorA = src.ColorA,
                    FlipX = src.FlipX, FlipY = src.FlipY,
                    Rotation = src.Rotation,
                    Visible = src.Visible,
                    // Light
                    LightType = src.LightType, Intensity = src.Intensity,
                    FalloffIntensity = src.FalloffIntensity,
                    PointLightInnerAngle = src.PointLightInnerAngle,
                    PointLightOuterAngle = src.PointLightOuterAngle,
                    PointLightInnerRadius = src.PointLightInnerRadius,
                    PointLightOuterRadius = src.PointLightOuterRadius,
                    ShapeLightFalloffSize = src.ShapeLightFalloffSize,
                    LightOrder = src.LightOrder, BlendStyleIndex = src.BlendStyleIndex,
                    ShadowsEnabled = src.ShadowsEnabled, ShadowIntensity = src.ShadowIntensity,
                    // Particle
                    Duration = src.Duration, Loop = src.Loop, Prewarm = src.Prewarm,
                    StartLifetime = src.StartLifetime, StartSpeed = src.StartSpeed,
                    StartSize = src.StartSize, MaxParticles = src.MaxParticles,
                    SimulationSpeed = src.SimulationSpeed, PlayOnAwake = src.PlayOnAwake,
                    GravityModifier = src.GravityModifier, EmissionRate = src.EmissionRate,
                    // Mask
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
                def.Layers.Insert(duplicateIdx + 1, dup);
                _expandedLayerIdx = duplicateIdx + 1;
                _selectedLayerIdx = duplicateIdx + 1;
                _layerRenameBuffer = dup.Name;
                _viewportDirty = true;
                MarkDirty();
            }

            // Add layer buttons with type picker
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Sprite", EditorStyles.MiniButton))
                AddNewLayer(def, VisualLayerType.Sprite);
            if (GUILayout.Button("+ Light", EditorStyles.MiniButton))
                AddNewLayer(def, VisualLayerType.Light);
            if (GUILayout.Button("+ Particle", EditorStyles.MiniButton))
                AddNewLayer(def, VisualLayerType.ParticleSystem);
            if (GUILayout.Button("+ Mask", EditorStyles.MiniButton))
                AddNewLayer(def, VisualLayerType.SpriteMask);
            if (GUILayout.Button("+ Container", EditorStyles.MiniButton))
                AddNewLayer(def, VisualLayerType.Container);
            if (GUILayout.Button("+ Shader", EditorStyles.MiniButton))
                AddNewLayer(def, VisualLayerType.Shader);
            if (GUILayout.Button("+ PrefabFX", EditorStyles.MiniButton))
                AddNewLayer(def, VisualLayerType.PrefabEffect);
            GUILayout.EndHorizontal();
        }

        private void AddNewLayer(BackgroundDef def, VisualLayerType type)
        {
            int maxOrder = -1400;
            foreach (var l in def.Layers)
                if (l.SortingOrder > maxOrder) maxOrder = l.SortingOrder;

            var newLayer = new BackgroundLayerDef
            {
                Name = $"{type.ToString().ToLower()}_{def.Layers.Count}",
                Type = type,
                SortingOrder = maxOrder + 50,
                Visible = true,
            };
            def.Layers.Add(newLayer);

            int newIdx = def.Layers.Count - 1;
            _expandedLayerIdx = newIdx;
            _selectedLayerIdx = newIdx;
            _layerRenameBuffer = newLayer.Name;
            _viewportDirty = true;

            if (type == VisualLayerType.Sprite)
            {
                _spritePickerTargetIdx = newIdx;
                _spritePickerFilter = "";
                _spritePickerPage = 0;
            }
            if (type == VisualLayerType.Shader)
            {
                newLayer.ScaleX = 19.2f;
                newLayer.ScaleY = 10.8f;
                newLayer.ColorA = 0.5f;
            }
            MarkDirty();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYER PROPERTIES
        // ═══════════════════════════════════════════════════════════════

        private static readonly string[] LightTypeNames = { "Parametric", "Freeform", "Sprite", "Point", "Global" };

        private void DrawLayerProperties(BackgroundDef def, int layerIdx)
        {
            var layer = def.Layers[layerIdx];
            bool changed = false;

            // Name
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(36));
            string newName = GUILayout.TextField(_layerRenameBuffer);
            if (newName != _layerRenameBuffer)
                _layerRenameBuffer = newName;
            if (_layerRenameBuffer != layer.Name && GUILayout.Button("Apply", EditorStyles.MiniButton, GUILayout.Width(50)))
            {
                layer.Name = _layerRenameBuffer;
                changed = true;
            }
            GUILayout.EndHorizontal();

            // Type (read-only)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Type:", GUILayout.Width(36));
            GUILayout.Label($"<color=#aaa>{layer.Type}</color>", EditorStyles.RichLabel);
            GUILayout.EndHorizontal();

            // Dispatch to type-specific properties
            switch (layer.Type)
            {
                case VisualLayerType.Sprite:
                    changed |= DrawSpriteLayerProps(layer, layerIdx);
                    break;
                case VisualLayerType.Light:
                    changed |= DrawLightLayerProps(layer);
                    break;
                case VisualLayerType.ParticleSystem:
                    changed |= DrawParticleLayerProps(layer);
                    break;
                case VisualLayerType.SpriteMask:
                    changed |= DrawSpriteMaskLayerProps(layer, layerIdx);
                    break;
                case VisualLayerType.Container:
                    changed |= DrawContainerLayerProps(layer);
                    break;
                case VisualLayerType.Shader:
                    changed |= DrawShaderLayerProps(layer);
                    break;
                case VisualLayerType.PrefabEffect:
                    changed |= DrawPrefabEffectLayerProps(layer);
                    break;
            }

            if (changed)
            {
                _viewportDirty = true;
                MarkDirty();
            }
        }

        // ── Sprite ──────────────────────────────────────────────────

        private bool DrawSpriteLayerProps(BackgroundLayerDef layer, int layerIdx)
        {
            bool changed = false;

            // Sprite with picker button
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sprite:", GUILayout.Width(36));
            string newSprite = GUILayout.TextField(layer.SpriteName ?? "");
            if (newSprite != layer.SpriteName) { layer.SpriteName = newSprite; changed = true; }
            if (GUILayout.Button("\u25CE", EditorStyles.MiniButton, GUILayout.Width(22)))
            {
                _spritePickerTargetIdx = layerIdx;
                _spritePickerFilter = "";
                _spritePickerPage = 0;
            }
            GUILayout.EndHorizontal();

            // Sorting Layer + Order
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort:", GUILayout.Width(36));
            string newSortLayer = GUILayout.TextField(layer.SortingLayer ?? "Background");
            if (newSortLayer != layer.SortingLayer) { layer.SortingLayer = newSortLayer; changed = true; }
            GUILayout.Label("#", GUILayout.Width(10));
            string orderStr = GUILayout.TextField(layer.SortingOrder.ToString(), GUILayout.Width(50));
            if (int.TryParse(orderStr, out int newOrder) && newOrder != layer.SortingOrder)
            { layer.SortingOrder = newOrder; changed = true; }
            GUILayout.EndHorizontal();

            // Position
            changed |= DrawPosFields(layer);

            // Scale
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale:", GUILayout.Width(36));
            changed |= FloatField("X", ref layer.ScaleX);
            changed |= FloatField("Y", ref layer.ScaleY);
            GUILayout.EndHorizontal();

            // Color
            changed |= DrawColorFields(layer);

            // Flip
            GUILayout.BeginHorizontal();
            GUILayout.Label("Flip:", GUILayout.Width(36));
            bool newFlipX = GUILayout.Toggle(layer.FlipX, "X", GUILayout.Width(30));
            bool newFlipY = GUILayout.Toggle(layer.FlipY, "Y", GUILayout.Width(30));
            if (newFlipX != layer.FlipX) { layer.FlipX = newFlipX; changed = true; }
            if (newFlipY != layer.FlipY) { layer.FlipY = newFlipY; changed = true; }
            GUILayout.EndHorizontal();

            // Rotation
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rot:", GUILayout.Width(36));
            changed |= FloatField("Z", ref layer.Rotation);
            GUILayout.EndHorizontal();

            return changed;
        }

        // ── Light2D ─────────────────────────────────────────────────

        private bool DrawLightLayerProps(BackgroundLayerDef layer)
        {
            bool changed = false;

            // Light Type
            GUILayout.BeginHorizontal();
            GUILayout.Label("Light:", GUILayout.Width(36));
            string typeName = layer.LightType >= 0 && layer.LightType < LightTypeNames.Length
                ? LightTypeNames[layer.LightType] : layer.LightType.ToString();
            for (int i = 0; i < LightTypeNames.Length; i++)
            {
                if (GUILayout.Toggle(i == layer.LightType, LightTypeNames[i], EditorStyles.MiniButton, GUILayout.Width(58)))
                {
                    if (i != layer.LightType) { layer.LightType = i; changed = true; }
                }
            }
            GUILayout.EndHorizontal();

            // Color
            changed |= DrawColorFields(layer);

            // Intensity / Falloff
            GUILayout.BeginHorizontal();
            GUILayout.Label("Int:", GUILayout.Width(36));
            changed |= FloatField("", ref layer.Intensity);
            GUILayout.Label("Fall:", GUILayout.Width(28));
            changed |= FloatField("", ref layer.FalloffIntensity);
            GUILayout.EndHorizontal();

            // Point light params
            if (layer.LightType == 3)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Radius:", GUILayout.Width(36));
                changed |= FloatField("In", ref layer.PointLightInnerRadius);
                changed |= FloatField("Out", ref layer.PointLightOuterRadius);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Angle:", GUILayout.Width(36));
                changed |= FloatField("In", ref layer.PointLightInnerAngle);
                changed |= FloatField("Out", ref layer.PointLightOuterAngle);
                GUILayout.EndHorizontal();
            }

            // Shape light falloff
            if (layer.LightType == 0 || layer.LightType == 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("ShpFall:", GUILayout.Width(36));
                changed |= FloatField("", ref layer.ShapeLightFalloffSize);
                GUILayout.EndHorizontal();
            }

            // Order + Blend
            GUILayout.BeginHorizontal();
            GUILayout.Label("Order:", GUILayout.Width(36));
            string loStr = GUILayout.TextField(layer.LightOrder.ToString(), GUILayout.Width(40));
            if (int.TryParse(loStr, out int newLO) && newLO != layer.LightOrder)
            { layer.LightOrder = newLO; changed = true; }
            GUILayout.Label("Blend:", GUILayout.Width(36));
            string bsStr = GUILayout.TextField(layer.BlendStyleIndex.ToString(), GUILayout.Width(30));
            if (int.TryParse(bsStr, out int newBS) && newBS != layer.BlendStyleIndex)
            { layer.BlendStyleIndex = newBS; changed = true; }
            GUILayout.EndHorizontal();

            // Shadows
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shadow:", GUILayout.Width(36));
            bool newSh = GUILayout.Toggle(layer.ShadowsEnabled, "");
            if (newSh != layer.ShadowsEnabled) { layer.ShadowsEnabled = newSh; changed = true; }
            if (layer.ShadowsEnabled)
                changed |= FloatField("Int", ref layer.ShadowIntensity);
            GUILayout.EndHorizontal();

            // Position
            changed |= DrawPosFields(layer);

            return changed;
        }

        // ── ParticleSystem ──────────────────────────────────────────

        private bool DrawParticleLayerProps(BackgroundLayerDef layer)
        {
            bool changed = false;

            // Duration
            GUILayout.BeginHorizontal();
            GUILayout.Label("Dur:", GUILayout.Width(36));
            changed |= FloatField("", ref layer.Duration);
            GUILayout.EndHorizontal();

            // Loop + Prewarm
            GUILayout.BeginHorizontal();
            GUILayout.Label("Loop:", GUILayout.Width(36));
            bool newLoop = GUILayout.Toggle(layer.Loop, "", GUILayout.Width(20));
            if (newLoop != layer.Loop) { layer.Loop = newLoop; changed = true; }
            GUILayout.Label("Prewarm:", GUILayout.Width(48));
            bool newPw = GUILayout.Toggle(layer.Prewarm, "", GUILayout.Width(20));
            if (newPw != layer.Prewarm) { layer.Prewarm = newPw; changed = true; }
            GUILayout.EndHorizontal();

            // Start values
            GUILayout.BeginHorizontal();
            GUILayout.Label("Life:", GUILayout.Width(36));
            changed |= FloatField("", ref layer.StartLifetime);
            GUILayout.Label("Spd:", GUILayout.Width(28));
            changed |= FloatField("", ref layer.StartSpeed);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Size:", GUILayout.Width(36));
            changed |= FloatField("", ref layer.StartSize);
            GUILayout.EndHorizontal();

            // Color
            changed |= DrawColorFields(layer);

            // MaxParticles
            GUILayout.BeginHorizontal();
            GUILayout.Label("Max:", GUILayout.Width(36));
            string mpStr = GUILayout.TextField(layer.MaxParticles.ToString(), GUILayout.Width(60));
            if (int.TryParse(mpStr, out int newMP) && newMP != layer.MaxParticles)
            { layer.MaxParticles = newMP; changed = true; }
            GUILayout.EndHorizontal();

            // Sim speed + gravity
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sim:", GUILayout.Width(36));
            changed |= FloatField("", ref layer.SimulationSpeed);
            GUILayout.Label("Grav:", GUILayout.Width(28));
            changed |= FloatField("", ref layer.GravityModifier);
            GUILayout.EndHorizontal();

            // Emission
            GUILayout.BeginHorizontal();
            GUILayout.Label("Emit:", GUILayout.Width(36));
            changed |= FloatField("", ref layer.EmissionRate);
            GUILayout.EndHorizontal();

            // PlayOnAwake
            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto:", GUILayout.Width(36));
            bool newPlay = GUILayout.Toggle(layer.PlayOnAwake, "Play on Awake");
            if (newPlay != layer.PlayOnAwake) { layer.PlayOnAwake = newPlay; changed = true; }
            GUILayout.EndHorizontal();

            // Sorting
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort:", GUILayout.Width(36));
            string soStr = GUILayout.TextField(layer.SortingOrder.ToString(), GUILayout.Width(50));
            if (int.TryParse(soStr, out int newSO) && newSO != layer.SortingOrder)
            { layer.SortingOrder = newSO; changed = true; }
            GUILayout.EndHorizontal();

            // Position + Scale
            changed |= DrawPosFields(layer);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale:", GUILayout.Width(36));
            changed |= FloatField("X", ref layer.ScaleX);
            changed |= FloatField("Y", ref layer.ScaleY);
            GUILayout.EndHorizontal();

            return changed;
        }

        // ── SpriteMask ──────────────────────────────────────────────

        private bool DrawSpriteMaskLayerProps(BackgroundLayerDef layer, int layerIdx)
        {
            bool changed = false;

            // Sprite picker
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sprite:", GUILayout.Width(36));
            string newSprite = GUILayout.TextField(layer.SpriteName ?? "");
            if (newSprite != layer.SpriteName) { layer.SpriteName = newSprite; changed = true; }
            if (GUILayout.Button("\u25CE", EditorStyles.MiniButton, GUILayout.Width(22)))
            {
                _spritePickerTargetIdx = layerIdx;
                _spritePickerFilter = "";
                _spritePickerPage = 0;
            }
            GUILayout.EndHorizontal();

            // Alpha cutoff
            GUILayout.BeginHorizontal();
            GUILayout.Label("Cutoff:", GUILayout.Width(36));
            changed |= FloatField("", ref layer.AlphaCutoff);
            GUILayout.EndHorizontal();

            // Custom range
            GUILayout.BeginHorizontal();
            GUILayout.Label("Range:", GUILayout.Width(36));
            bool newCR = GUILayout.Toggle(layer.CustomRange, "Custom");
            if (newCR != layer.CustomRange) { layer.CustomRange = newCR; changed = true; }
            GUILayout.EndHorizontal();

            if (layer.CustomRange)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Front:", GUILayout.Width(36));
                string fsStr = GUILayout.TextField(layer.FrontSortingOrder.ToString(), GUILayout.Width(50));
                if (int.TryParse(fsStr, out int newFS) && newFS != layer.FrontSortingOrder)
                { layer.FrontSortingOrder = newFS; changed = true; }
                GUILayout.Label("Back:", GUILayout.Width(30));
                string bsStr = GUILayout.TextField(layer.BackSortingOrder.ToString(), GUILayout.Width(50));
                if (int.TryParse(bsStr, out int newBS) && newBS != layer.BackSortingOrder)
                { layer.BackSortingOrder = newBS; changed = true; }
                GUILayout.EndHorizontal();
            }

            // Sorting order
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort:", GUILayout.Width(36));
            string soStr = GUILayout.TextField(layer.SortingOrder.ToString(), GUILayout.Width(50));
            if (int.TryParse(soStr, out int newSO) && newSO != layer.SortingOrder)
            { layer.SortingOrder = newSO; changed = true; }
            GUILayout.EndHorizontal();

            // Position + Scale
            changed |= DrawPosFields(layer);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale:", GUILayout.Width(36));
            changed |= FloatField("X", ref layer.ScaleX);
            changed |= FloatField("Y", ref layer.ScaleY);
            GUILayout.EndHorizontal();

            return changed;
        }

        // ── Container ───────────────────────────────────────────────

        private static bool DrawContainerLayerProps(BackgroundLayerDef layer)
        {
            bool changed = false;
            changed |= DrawPosFields(layer);
            return changed;
        }

        // ── Shader ──────────────────────────────────────────────────

        private static readonly string[] MaskInteractionNames = { "None", "InsideMask", "OutsideMask" };
        private static readonly string[] ShaderPresetNames = { "None", "Scan", "Vign", "Noise", "Grad", "Check" };
        private static readonly string[] BgGradientDirNames = { "\u2193", "\u2191", "\u2192", "\u2190" };

        private bool DrawShaderLayerProps(BackgroundLayerDef layer)
        {
            bool changed = false;

            // Preset
            GUILayout.BeginHorizontal();
            GUILayout.Label("Preset:", GUILayout.Width(36));
            int presetIdx = (int)layer.Preset;
            for (int i = 0; i < ShaderPresetNames.Length; i++)
            {
                if (GUILayout.Toggle(i == presetIdx, ShaderPresetNames[i], EditorStyles.MiniButton))
                {
                    if (i != presetIdx)
                    {
                        layer.Preset = (ShaderPreset)i;
                        ApplyBgPresetDefaults(layer);
                        changed = true;
                    }
                }
            }
            GUILayout.EndHorizontal();

            // Preset parameters
            if (layer.Preset != ShaderPreset.None && layer.Preset != ShaderPreset.Gradient)
            {
                GetBgShaderParamLabels(layer.Preset, out string l1, out string l2);
                if (!string.IsNullOrEmpty(l1))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{l1}:", GUILayout.Width(36));
                    changed |= FloatField("", ref layer.PresetParam1);
                    GUILayout.EndHorizontal();
                }
                if (!string.IsNullOrEmpty(l2))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{l2}:", GUILayout.Width(36));
                    changed |= FloatField("", ref layer.PresetParam2);
                    GUILayout.EndHorizontal();
                }
            }
            else if (layer.Preset == ShaderPreset.Gradient)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Dir:", GUILayout.Width(36));
                int dir = Mathf.RoundToInt(layer.PresetParam1) % 4;
                if (dir < 0) dir += 4;
                for (int i = 0; i < BgGradientDirNames.Length; i++)
                {
                    if (GUILayout.Toggle(i == dir, BgGradientDirNames[i], EditorStyles.MiniButton, GUILayout.Width(24)))
                    {
                        if (i != dir) { layer.PresetParam1 = i; changed = true; }
                    }
                }
                GUILayout.EndHorizontal();
            }

            // Shader name
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shader:", GUILayout.Width(36));
            string newShader = GUILayout.TextField(layer.ShaderName ?? "Sprites/Default");
            if (newShader != layer.ShaderName) { layer.ShaderName = newShader; changed = true; }
            GUILayout.EndHorizontal();

            // Sorting Layer + Order
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort:", GUILayout.Width(36));
            string newSortLayer = GUILayout.TextField(layer.SortingLayer ?? "Background");
            if (newSortLayer != layer.SortingLayer) { layer.SortingLayer = newSortLayer; changed = true; }
            GUILayout.Label("#", GUILayout.Width(10));
            string orderStr = GUILayout.TextField(layer.SortingOrder.ToString(), GUILayout.Width(50));
            if (int.TryParse(orderStr, out int newOrder) && newOrder != layer.SortingOrder)
            { layer.SortingOrder = newOrder; changed = true; }
            GUILayout.EndHorizontal();

            // Position
            changed |= DrawPosFields(layer);

            // Scale (controls quad size)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Size:", GUILayout.Width(36));
            changed |= FloatField("W", ref layer.ScaleX);
            changed |= FloatField("H", ref layer.ScaleY);
            GUILayout.EndHorizontal();

            // Color
            changed |= DrawColorFields(layer);

            // Mask Interaction
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mask:", GUILayout.Width(36));
            for (int i = 0; i < MaskInteractionNames.Length; i++)
            {
                if (GUILayout.Toggle(i == layer.MaskInteraction, MaskInteractionNames[i], EditorStyles.MiniButton))
                {
                    if (i != layer.MaskInteraction) { layer.MaskInteraction = i; changed = true; }
                }
            }
            GUILayout.EndHorizontal();

            // Rotation
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rot:", GUILayout.Width(36));
            changed |= FloatField("Z", ref layer.Rotation);
            GUILayout.EndHorizontal();

            // ── Shader Effects (AllIn1SpriteShader keywords) ────────
            changed |= DrawBgShaderEffects(layer);

            return changed;
        }

        private static bool DrawBgShaderEffects(BackgroundLayerDef layer)
        {
            bool changed = false;
            GUILayout.Label("<b><size=10>Shader FX</size></b>", EditorStyles.RichLabel);

            var effects = ShaderEffectRegistry.Effects;
            string lastCat = null;
            for (int e = 0; e < effects.Length; e++)
            {
                ref readonly var fx = ref effects[e];
                bool active = layer.ShaderKeywords != null && layer.ShaderKeywords.Contains(fx.Keyword);

                // Category header
                if (fx.Category != lastCat)
                {
                    lastCat = fx.Category;
                    GUILayout.Label($"<color=#888><size=9>{lastCat}</size></color>", EditorStyles.RichLabel);
                }

                // Toggle
                GUILayout.BeginHorizontal();
                bool newActive = GUILayout.Toggle(active, fx.DisplayName, EditorStyles.MiniButton, GUILayout.Width(80));
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

                // Inline property fields when active
                if (newActive && fx.Props != null)
                {
                    foreach (var p in fx.Props)
                    {
                        GUILayout.Label(p.Label, GUILayout.Width(36));
                        if (layer.ShaderFloats == null) layer.ShaderFloats = new System.Collections.Generic.Dictionary<string, float>();
                        if (!layer.ShaderFloats.TryGetValue(p.Name, out float val)) val = p.Default;
                        string valStr = GUILayout.TextField(val.ToString("F2"), GUILayout.Width(40));
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

        // ── PrefabEffect ─────────────────────────────────────────────

        private bool DrawPrefabEffectLayerProps(BackgroundLayerDef layer)
        {
            bool changed = false;

            // Current effect name display + manual entry
            GUILayout.BeginHorizontal();
            GUILayout.Label("Effect:", GUILayout.Width(36));
            string newName = GUILayout.TextField(layer.EffectName ?? "");
            if (newName != (layer.EffectName ?? "")) { layer.EffectName = newName; changed = true; }
            if (GUILayout.Button(_fxPickerOpen ? "\u25B2" : "\u25BC", EditorStyles.MiniButton, GUILayout.Width(22)))
                _fxPickerOpen = !_fxPickerOpen;
            GUILayout.EndHorizontal();

            // Inline filterable picker
            if (_fxPickerOpen)
            {
                var allNames = MapEditor.GetVfxEffectNames();
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

        private static void ApplyBgPresetDefaults(BackgroundLayerDef layer)
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

        private static void GetBgShaderParamLabels(ShaderPreset p, out string l1, out string l2)
        {
            switch (p)
            {
                case ShaderPreset.Scanlines:    l1 = "Wid"; l2 = "Gap"; break;
                case ShaderPreset.Vignette:     l1 = "Int"; l2 = "Soft"; break;
                case ShaderPreset.Noise:        l1 = "Grn"; l2 = "Dens"; break;
                case ShaderPreset.Checkerboard: l1 = "Size"; l2 = "Int"; break;
                default: l1 = "P1"; l2 = "P2"; break;
            }
        }

        // ── Shared property helpers ─────────────────────────────────

        private static bool DrawPosFields(BackgroundLayerDef layer)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pos:", GUILayout.Width(36));
            changed |= FloatField("X", ref layer.PosX);
            changed |= FloatField("Y", ref layer.PosY);
            changed |= FloatField("Z", ref layer.PosZ);
            GUILayout.EndHorizontal();
            return changed;
        }

        private static bool DrawColorFields(BackgroundLayerDef layer)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Color:", GUILayout.Width(36));
            changed |= FloatField("R", ref layer.ColorR);
            changed |= FloatField("G", ref layer.ColorG);
            changed |= FloatField("B", ref layer.ColorB);
            changed |= FloatField("A", ref layer.ColorA);
            GUILayout.EndHorizontal();

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(layer.ColorR, layer.ColorG, layer.ColorB, layer.ColorA);
            GUILayout.Box("", GUILayout.Height(6), GUILayout.ExpandWidth(true));
            GUI.backgroundColor = prevBg;

            return changed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static bool FloatField(string label, ref float value)
        {
            GUILayout.Label(label, GUILayout.Width(12));
            string s = GUILayout.TextField(value.ToString("F2", CultureInfo.InvariantCulture), GUILayout.ExpandWidth(true));
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float newVal)
                && Mathf.Abs(newVal - value) > 0.001f)
            {
                value = newVal;
                return true;
            }
            return false;
        }
    }
}
