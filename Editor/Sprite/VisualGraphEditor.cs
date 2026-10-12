using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    public sealed partial class VisualGraphEditor
    {
        private readonly ModEditor _parent;
        private string _selectedGraphId;
        private Vector2 _diagnosticScroll;
        private VisualGraphInstance _previewInstance;
        private GameObject _previewHost;
        private Camera _previewCamera;
        private RenderTexture _previewTexture;
        private AnimationClip _previewClip;
        private bool _playing;
        private float _phase;
        private float _speed = 1f;
        private string _previewClipName;
        private double _lastTime;
        private string _lastPreviewKey;
        private readonly Dictionary<string, List<string>> _catalogCache = new(StringComparer.OrdinalIgnoreCase);
        private string _catalogProjectId;
        private string _renameFor;
        private string _renameBuffer;
        private bool _foldUsedBy;
        private string _usedByKey;
        private float _usedByTime;
        private List<RefHit> _usedBy = new List<RefHit>();
        private int _queuedHistory;
        private int _selectedNodeIndex = -1;
        private string _pendingGraphDeleteId;
        private string _pendingNodeDeleteId;
        private ModProject _dirtyProject;
        private CharacterVisualDef _dirtyGraph;
        private bool _dirtyIsPatch;
        private int _revision;
        private int _compiledRevision = -1;
        private CharacterVisualDef _compiledDefinition;
        private VisualGraphCompileResult _compiledResult;

        private const float GameRefreshDelay = 2.5f;
        private float _gameRefreshAt;
        private bool _gameRefreshPending;

        public string SelectedSkinId => _selectedGraphId;
        public string SelectedGraphId => _selectedGraphId;

        public VisualGraphEditor(ModEditor parent) { _parent = parent; }

        public void Tick()
        {
            // No autosave: edits stay in memory (the preview never reloads under the cursor) until Save is clicked, Ctrl+S, or the editor/graph is left.
            if (_gameRefreshPending && _dirtyGraph == null && _tool == GizmoTool.None && Time.realtimeSinceStartup >= _gameRefreshAt)
                RunGameRefresh();
        }

        public void FlushPendingChanges() { FlushPendingSave(); RunGameRefresh(); }

        // Unique stage position: every editor preview camera renders all layers, so a shared origin leaks models between tabs.
        private static readonly Vector3 PreviewOrigin = new Vector3(-6000f, -8000f, 0f);

        /// <summary>Destroy the live preview model so it cannot show up in other tabs' renders.</summary>
        public void ReleasePreview() { DestroyPreview(); DestroySource(); }

        public void Cleanup()
        {
            FlushPendingSave();
            RunGameRefresh();
            DestroyPreview();
            DestroySourceCamera();
            VisualGraphThumbnailCache.ClearAll();
            _catalogCache.Clear();
            if (_previewTexture != null) { _previewTexture.Release(); UnityEngine.Object.DestroyImmediate(_previewTexture); _previewTexture = null; }
            if (_previewCamera != null) UnityEngine.Object.DestroyImmediate(_previewCamera.gameObject);
            _previewCamera = null;
        }

        public void DrawPanel()
        {
            ModProject project = ModManagerPanel.ActiveProject;
            if (project == null)
            {
                GUILayout.Label("<color=#888>No mod project active.</color>", EditorStyles.RichLabel);
                GUILayout.Label("Select or create a mod from the Mods tab.", EditorStyles.RichLabel);
                return;
            }
            if (!string.Equals(_catalogProjectId, project.ModId, StringComparison.OrdinalIgnoreCase))
            {
                FlushPendingSave();
                _catalogProjectId = project.ModId;
                _selectedGraphId = null;
                _selectedNodeIndex = -1;
                _pendingGraphDeleteId = null;
                _pendingNodeDeleteId = null;
                _previewClipName = null;
                _catalogCache.Clear();
                VisualGraphThumbnailCache.ClearAll();
            }
            var ids = project.VisualGraphs.Keys.Concat(project.VisualGraphPatches.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            if (_selectedGraphId == null || !ids.Contains(_selectedGraphId)) _selectedGraphId = ids.FirstOrDefault();
            string selectedGraphId = EditorFields.IdDropdown("Graft Sprite", _selectedGraphId, ids, "visual_graph_select");
            if (!string.Equals(selectedGraphId, _selectedGraphId, StringComparison.OrdinalIgnoreCase))
            {
                FlushPendingSave();
                _selectedGraphId = selectedGraphId;
                _selectedNodeIndex = -1;
                _pendingGraphDeleteId = null;
                _pendingNodeDeleteId = null;
                DestroyPreview();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New Graft Sprite", EditorStyles.MiniButton, GUILayout.Width(95))) CreateGraph(project);
            bool isPatch = _selectedGraphId != null && project.VisualGraphPatches.ContainsKey(_selectedGraphId);
            if (!string.IsNullOrEmpty(_selectedGraphId) && GUILayout.Button(isPatch ? "Revert" : "Delete", EditorStyles.DangerButton, GUILayout.Width(65)))
                _pendingGraphDeleteId = _selectedGraphId;
            GUILayout.EndHorizontal();
            if (string.Equals(_pendingGraphDeleteId, _selectedGraphId, StringComparison.OrdinalIgnoreCase))
            {
                GUILayout.BeginHorizontal(EditorStyles.CompactBox);
                GUILayout.Label(isPatch ? $"Revert patch '{_selectedGraphId}'?" : $"Permanently delete '{_selectedGraphId}'?", EditorStyles.RichLabel);
                if (GUILayout.Button("Confirm", EditorStyles.DangerButton, GUILayout.Width(58))) DeleteGraph(project, isPatch);
                if (GUILayout.Button("Cancel", EditorStyles.MiniButton, GUILayout.Width(52))) _pendingGraphDeleteId = null;
                GUILayout.EndHorizontal();
            }
            CharacterVisualDef thumbnailGraph;
            if (!string.IsNullOrEmpty(_selectedGraphId) && (project.VisualGraphs.TryGetValue(_selectedGraphId, out thumbnailGraph) || project.VisualGraphPatches.TryGetValue(_selectedGraphId, out thumbnailGraph)))
            {
                var thumbnail = VisualGraphThumbnailCache.GetThumbnail(_selectedGraphId, thumbnailGraph);
                // Always reserve the slot: a thumbnail that appears and disappears around each save shifts the whole panel (scroll jumps, lost focus).
                GUILayout.Label((Texture)thumbnail, GUILayout.Width(96), GUILayout.Height(96));
            }
            if (string.IsNullOrEmpty(_selectedGraphId)) { GUILayout.Label("Create a graft sprite to begin.", EditorStyles.RichLabel); return; }
            CharacterVisualDef graph;
            if (!project.VisualGraphs.TryGetValue(_selectedGraphId, out graph)) project.VisualGraphPatches.TryGetValue(_selectedGraphId, out graph);
            if (graph == null) return;
            if (_queuedHistory != 0)
            {
                int queued = _queuedHistory; _queuedHistory = 0;
                if (queued < 0) Undo(project, graph, isPatch); else Redo(project, graph, isPatch);
            }
            int panelSerial = BeginPanelEdit(graph);
            bool changed = MigrateLegacyPivots(graph);
            GUILayout.BeginHorizontal();
            GUILayout.Label(isPatch ? "<color=#e6b35a>[PATCH] Graft Sprite</color>" : "<color=#75c8ff>[NEW] Graft Sprite</color>", EditorStyles.RichLabel);
            GUILayout.FlexibleSpace();
            // Always draw the same controls: IMGUI control ids are positional, so a button that appears only while dirty
            // shifts every field below it and drops keyboard focus on each edit and autosave.
            bool dirty = _dirtyGraph == graph;
            GUILayout.Label(dirty ? "<color=#e6b35a>Unsaved</color>" : "<color=#70d090>Saved</color>", EditorStyles.RichLabel);
            GUI.enabled = CanUndo(graph);
            if (GUILayout.Button("Undo", EditorStyles.MiniButton, GUILayout.Width(44))) _queuedHistory = -1;
            GUI.enabled = CanRedo;
            if (GUILayout.Button("Redo", EditorStyles.MiniButton, GUILayout.Width(44))) _queuedHistory = 1;
            GUI.enabled = dirty;
            if (GUILayout.Button("Save Now", EditorStyles.MiniButton, GUILayout.Width(62))) FlushPendingSave();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!isPatch) DrawRename(project, graph);
            graph.Normalize();
            GUI.changed = false;
            string oldBasePresentation = graph.BasePresentation;
            graph.BasePresentation = EditorFields.IdDropdown("Host Model (Base)", graph.BasePresentation, SourceIds(), "visual_graph_base");
            changed |= !string.Equals(oldBasePresentation, graph.BasePresentation, StringComparison.Ordinal);
            bool oldFlip = graph.FlipX;
            graph.FlipX = EditorFields.Toggle("Flip Horizontally", graph.FlipX);
            changed |= oldFlip != graph.FlipX;
            string oldTint = graph.TintHex; float oldAlpha = graph.Alpha, oldHue = graph.HueShift;
            graph.TintHex = EditorFields.TextField("Tint (#RRGGBB)", graph.TintHex);
            graph.Alpha = EditorFields.FloatField("Opacity", graph.Alpha, 0f, 1f);
            graph.HueShift = EditorFields.FloatField("Hue Shift", graph.HueShift, -180f, 180f);
            changed |= oldTint != graph.TintHex || !Mathf.Approximately(oldAlpha, graph.Alpha) || !Mathf.Approximately(oldHue, graph.HueShift);
            changed |= DrawEffects(graph);
            changed |= DrawHostHiddenRenderers(graph);
            changed |= DrawHostParts(graph);
            var previewClips = PreviewClipNames(graph.BasePresentation);
            if (previewClips.Count > 0 && !previewClips.Contains(_previewClipName))
            {
                _previewClipName = PreviewPose.DefaultClip(previewClips);
                DestroyPreview();
            }
            string oldPreviewClipName = _previewClipName;
            _previewClipName = EditorFields.IdDropdown("Preview Clip", _previewClipName, previewClips, "visual_graph_preview_clip");
            if (!string.Equals(oldPreviewClipName, _previewClipName, StringComparison.Ordinal)) DestroyPreview();
            if (graph.Nodes == null) graph.Nodes = new List<VisualNodeDef>();
            if (_selectedNodeIndex < 0 && graph.Nodes.Count > 0) _selectedNodeIndex = 0;
            var compileResult = GetCompileResult(graph);
            DrawNodeNavigator(graph, compileResult, ref changed);
            if (_selectedNodeIndex >= graph.Nodes.Count) _selectedNodeIndex = graph.Nodes.Count - 1;
            GUI.changed = false;
            if (_selectedNodeIndex >= 0 && _selectedNodeIndex < graph.Nodes.Count)
                changed |= DrawNode(graph, graph.Nodes[_selectedNodeIndex], _selectedNodeIndex);
            if (GUILayout.Button("+ Node", EditorStyles.MiniButton, GUILayout.Width(70)))
            {
                graph.Nodes.Add(NewNode(graph));
                _selectedNodeIndex = graph.Nodes.Count - 1;
                changed = true;
            }
            changed |= GUI.changed;
            EndPanelEdit(graph, changed, panelSerial);
            if (changed) MarkDirty(project, graph, isPatch);
            DrawDiagnostics(graph);
        }

        private void DrawNodeNavigator(CharacterVisualDef graph, VisualGraphCompileResult result, ref bool changed)
        {
            GUILayout.Label($"Nodes ({graph.Nodes.Count})", EditorStyles.SectionHeader);
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                var node = graph.Nodes[i];
                string nodeId = node?.NodeId ?? "(null)";
                int errors = result.Diagnostics.Count(d => d.Severity == VisualGraphDiagnosticSeverity.Error && string.Equals(d.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
                int warnings = result.Diagnostics.Count(d => d.Severity == VisualGraphDiagnosticSeverity.Warning && string.Equals(d.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
                string status = errors > 0 ? $"<color=#e07070>{errors} error{(errors == 1 ? "" : "s")}</color>" : warnings > 0 ? $"<color=#e6b35a>{warnings} warning{(warnings == 1 ? "" : "s")}</color>" : "<color=#70d090>valid</color>";
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"{i + 1}. {nodeId}", i == _selectedNodeIndex ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem))
                {
                    _selectedNodeIndex = i;
                    _pendingNodeDeleteId = null;
                }
                GUILayout.Label(status, EditorStyles.RichLabel, GUILayout.Width(65));
                GUILayout.EndHorizontal();
            }
        }

        private bool DrawNode(CharacterVisualDef graph, VisualNodeDef node, int index)
        {
            if (node == null) return false;
            bool changed = false;
            GUILayout.BeginVertical(EditorStyles.CompactBox);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Node {index + 1}: {node.NodeId}", EditorStyles.RichLabel);
            if (GUILayout.Button("Up", EditorStyles.MiniButton, GUILayout.Width(32)) && index > 0) { Swap(graph, index, index - 1); _selectedNodeIndex = index - 1; changed = true; }
            if (GUILayout.Button("Down", EditorStyles.MiniButton, GUILayout.Width(44)) && index + 1 < graph.Nodes.Count) { Swap(graph, index, index + 1); _selectedNodeIndex = index + 1; changed = true; }
            if (GUILayout.Button("Duplicate", EditorStyles.MiniButton, GUILayout.Width(58)))
            {
                DuplicateNode(graph, index);
                changed = true;

            }
            if (GUILayout.Button("Remove", EditorStyles.DangerButton, GUILayout.Width(55))) _pendingNodeDeleteId = node.NodeId;
            GUILayout.EndHorizontal();
            if (string.Equals(_pendingNodeDeleteId, node.NodeId, StringComparison.Ordinal))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Remove '{node.NodeId}'?", EditorStyles.RichLabel);
                if (GUILayout.Button("Confirm", EditorStyles.DangerButton, GUILayout.Width(58)))
                {
                    PushNodeUndo(graph, node).AfterId = null;
                    graph.Nodes.RemoveAt(index);
                    _selectedNodeIndex = Mathf.Min(index, graph.Nodes.Count - 1);
                    _pendingNodeDeleteId = null;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    return true;
                }
                if (GUILayout.Button("Cancel", EditorStyles.MiniButton, GUILayout.Width(52))) _pendingNodeDeleteId = null;
                GUILayout.EndHorizontal();
            }
            node.NodeId = EditorFields.TextField("Node ID", node.NodeId);
            DrawNodeIssues(graph, node);
            string previousSource = node.SourcePrefabRef;
            node.SourcePrefabRef = EditorFields.IdDropdown("Graft From (Source Model)", node.SourcePrefabRef, SourceIds(), "visual_source_" + index);
            if (!string.Equals(previousSource, node.SourcePrefabRef, StringComparison.Ordinal)) { ResetSource(node); changed = true; }
            changed |= DrawGraftedParts(node);
            node.HostSocketRef.Path = SelectPath("Attached To (Host Bone)", node.HostSocketRef.Path, HostPaths(graph.BasePresentation), "visual_host_" + index);
            if (GUILayout.Button(_pickBone ? "Click a host bone in the viewport... (Esc cancels)" : "Pick bone in viewport (B)", EditorStyles.MiniButton))
            {
                bool pickOn = !_pickBone;
                ClearModes();
                _pickBone = pickOn;
            }
            node.LocalAttachmentTransform.PositionX = EditorFields.FloatField("Position X", node.LocalAttachmentTransform.PositionX);
            node.LocalAttachmentTransform.PositionY = EditorFields.FloatField("Position Y", node.LocalAttachmentTransform.PositionY);
            node.LocalAttachmentTransform.Rotation = EditorFields.FloatField("Rotation", node.LocalAttachmentTransform.Rotation);
            node.LocalAttachmentTransform.ScaleX = EditorFields.FloatField("Scale X", node.LocalAttachmentTransform.ScaleX);
            node.LocalAttachmentTransform.ScaleY = EditorFields.FloatField("Scale Y", node.LocalAttachmentTransform.ScaleY);
            if (GUILayout.Button("Reset Transform", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                node.LocalAttachmentTransform = new LocalAttachmentTransform();
                changed = true;
            }
            node.SortingPolicy.Placement = EditorFields.EnumField("Layering", node.SortingPolicy.Placement, "visual_place_" + index);
            if (node.SortingPolicy.Placement == VisualPlacement.InFrontOfPart || node.SortingPolicy.Placement == VisualPlacement.BehindPart)
                node.SortingPolicy.ReferencePath = SelectPath("Relative To (Host Part)", node.SortingPolicy.ReferencePath, HostSpritePaths(graph.BasePresentation), "visual_placeref_" + index);
            GUILayout.Label("<color=#888>Front/Back = whole model. Nodes in the same slot stack by list order (lower in the list = nearer the viewer). Shortcut: press L in the viewport, then click a host part.</color>", EditorStyles.RichLabel);
            node.Overrides.Visible = EditorFields.Toggle("Visible", node.Overrides.Visible);
            node.Overrides.TintHex = EditorFields.TextField("Tint Hex", node.Overrides.TintHex);
            node.Overrides.Alpha = EditorFields.FloatField("Alpha", node.Overrides.Alpha, 0f, 1f);
            node.Overrides.HueShift = EditorFields.FloatField("Hue Shift", node.Overrides.HueShift, -180f, 180f);
            node.Overrides.Scale = EditorFields.FloatField("Override Scale", node.Overrides.Scale);
            // Everything the click-to-graft flow fills in lives last: its controls come and go with the foldout without shifting the fields above.
            if (GUILayout.Button((_foldAdvanced ? "\u25BC" : "\u25B6") + " Advanced", EditorStyles.MiniButton, GUILayout.Width(110))) _foldAdvanced = !_foldAdvanced;
            if (_foldAdvanced)
            {
                node.AttachmentKind = SelectAttachmentKind(node.AttachmentKind, "visual_kind_" + index);
                node.Pivot = EditorFields.EnumField("Pivot", node.Pivot, "visual_pivot_" + index);
                var sourcePaths = SourcePaths(node.SourcePrefabRef);
                node.SourceSubtreeRef.Path = SelectPath("Source Root", node.SourceSubtreeRef.Path, sourcePaths, "visual_subtree_" + index);
                if (node.Pivot == VisualPivotMode.Anchor)
                    node.SourceAnchorRef.Path = SelectPath("Source Anchor", node.SourceAnchorRef.Path, sourcePaths, "visual_anchor_" + index);
                node.RendererRefs = DrawRendererRefs(node, RendererPaths(node.SourcePrefabRef), index);
                node.FacingPolicy = EditorFields.EnumField("Facing", node.FacingPolicy, "visual_facing_" + index);
                node.AnimationPolicy.Mode = EditorFields.EnumField("Animation", node.AnimationPolicy.Mode, "visual_anim_" + index);
                node.AnimationPolicy.Fallback = EditorFields.EnumField("Fallback", node.AnimationPolicy.Fallback, "visual_fallback_" + index);
                node.AnimationPolicy.AnchorPolicy = EditorFields.EnumField("Anchor", node.AnimationPolicy.AnchorPolicy, "visual_anchorpolicy_" + index);
                node.AnimationPolicy.ProceduralControllerRef = EditorFields.TextField("Procedural Controller", node.AnimationPolicy.ProceduralControllerRef);
                DrawClipMap(node.AnimationPolicy);
            }
            GUILayout.EndVertical();
            return changed || GUI.changed;
        }

        private static void Swap(CharacterVisualDef graph, int a, int b)
        {
            if (graph == null || graph.Nodes == null) return;
            var node = graph.Nodes[a]; graph.Nodes[a] = graph.Nodes[b]; graph.Nodes[b] = node;
        }

        private static string UniqueNodeId(CharacterVisualDef graph, string preferred)
        {
            var used = new HashSet<string>(graph.Nodes.Where(x => x != null).Select(x => x.NodeId), StringComparer.OrdinalIgnoreCase);
            string candidate = preferred;
            int suffix = 2;
            while (used.Contains(candidate)) candidate = preferred + suffix++;
            return candidate;
        }

        /// <summary>Renderers on the base model to hide while the graph is active (for example the weapon being replaced).</summary>
        private static bool DrawEffects(CharacterVisualDef graph)
        {
            if (graph.Effects == null) graph.Effects = new List<RigEffectDef>();
            return DrawEffectList(graph.Effects, "visual_effect_");
        }

        private static bool DrawEffectList(List<RigEffectDef> effects, string popupPrefix)
        {
            bool changed = false;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null) { effects[i] = e = new RigEffectDef(); }
                GUILayout.BeginHorizontal();
                var type = EditorFields.EnumField("Effect " + (i + 1), e.Type, popupPrefix + i);
                if (GUILayout.Button("-", EditorStyles.DangerButton, GUILayout.Width(24))) { effects.RemoveAt(i); i--; changed = true; GUILayout.EndHorizontal(); continue; }
                GUILayout.EndHorizontal();
                string colour = e.ColorHex; float intensity = e.Intensity, speed = e.Speed;
                if (!RigEffectMath.IsTransform(type)) colour = EditorFields.TextField("   Colour (#RRGGBB)", e.ColorHex);
                intensity = EditorFields.FloatField("   Intensity", e.Intensity, 0f, 20f);
                speed = EditorFields.FloatField("   Speed", e.Speed, 0f, 20f);
                if (type != e.Type || colour != e.ColorHex || !Mathf.Approximately(intensity, e.Intensity) || !Mathf.Approximately(speed, e.Speed))
                {
                    e.Type = type; e.ColorHex = colour; e.Intensity = intensity; e.Speed = speed;
                    changed = true;
                }
            }
            if (GUILayout.Button("+ Effect (pulse, float, sparkles ...)", EditorStyles.MiniButton, GUILayout.Width(220)))
            {
                effects.Add(new RigEffectDef());
                changed = true;
            }
            return changed;
        }

        private bool DrawHostHiddenRenderers(CharacterVisualDef graph)
        {
            if (graph.HostHiddenRenderers == null) graph.HostHiddenRenderers = new List<CanonicalTransformRef>();
            var paths = HostSpritePaths(graph.BasePresentation);
            bool changed = false;
            for (int i = 0; i < graph.HostHiddenRenderers.Count; i++)
            {
                if (graph.HostHiddenRenderers[i] == null) graph.HostHiddenRenderers[i] = new CanonicalTransformRef();
                GUILayout.BeginHorizontal();
                graph.HostHiddenRenderers[i].Path = SelectPath("Hide Host " + (i + 1), graph.HostHiddenRenderers[i].Path, paths, "visual_hidehost_" + i);
                if (GUILayout.Button("-", EditorStyles.DangerButton, GUILayout.Width(24))) { graph.HostHiddenRenderers.RemoveAt(i); i--; changed = true; }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("<color=#888>Tip: in the viewport press X, then click a host part (e.g. its own staff) to hide it.</color>", EditorStyles.RichLabel);
            if (GUILayout.Button("+ Hide Host Renderer", EditorStyles.MiniButton, GUILayout.Width(140)))
            {
                graph.HostHiddenRenderers.Add(new CanonicalTransformRef());
                changed = true;
            }
            return changed;
        }

        private static List<CanonicalTransformRef> DrawRendererRefs(VisualNodeDef node, List<string> paths, int index)
        {
            if (node.RendererRefs == null) node.RendererRefs = new List<CanonicalTransformRef>();
            for (int i = 0; i < node.RendererRefs.Count; i++)
            {
                GUILayout.BeginHorizontal();
                node.RendererRefs[i].Path = SelectPath("Renderer " + (i + 1), node.RendererRefs[i].Path, paths, "visual_renderer_" + index + "_" + i);
                if (GUILayout.Button("-", EditorStyles.DangerButton, GUILayout.Width(24))) { node.RendererRefs.RemoveAt(i); i--; }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Renderer", EditorStyles.MiniButton, GUILayout.Width(85))) node.RendererRefs.Add(new CanonicalTransformRef());
            return node.RendererRefs;
        }

        private static void DrawClipMap(AnimationPolicyDef policy)
        {
            if (policy.ClipMap == null) policy.ClipMap = new Dictionary<string, string>();
            foreach (var key in policy.ClipMap.Keys.ToList())
            {
                GUILayout.BeginHorizontal();
                string value = policy.ClipMap[key];
                string host = EditorFields.TextField("Host Clip", key).Trim();
                value = EditorFields.TextField("Source Clip", value).Trim();
                if (GUILayout.Button("-", EditorStyles.DangerButton, GUILayout.Width(24))) policy.ClipMap.Remove(key);
                else if (!string.Equals(host, key, StringComparison.Ordinal))
                {
                    bool collision = policy.ClipMap.Keys.Any(existing => !string.Equals(existing, key, StringComparison.Ordinal) && string.Equals(existing.Trim(), host, StringComparison.OrdinalIgnoreCase));
                    if (collision)
                        GUILayout.Label("Host key already exists; rename refused.", EditorStyles.RichLabel);
                    else if (!string.IsNullOrEmpty(host)) { policy.ClipMap.Remove(key); policy.ClipMap[host] = value; }
                }
                else policy.ClipMap[key] = value;
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Clip Map", EditorStyles.MiniButton, GUILayout.Width(85)))
            {
                string key = "HostClip";
                int suffix = 2;
                while (policy.ClipMap.Keys.Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))) key = "HostClip" + suffix++;
                policy.ClipMap[key] = "SourceClip";
            }
        }

        private static string SelectPath(string label, string current, List<string> paths, string popup)
        {
            return EditorFields.IdDropdown(label, current, paths, popup);
        }

        private List<string> HostPaths(string id)
        {
            return CachedCatalog("host:" + id, () => { var prefab = ResolvePrefab(id); return prefab == null ? new List<string>() : CatalogPaths(prefab.transform); });
        }

        private List<string> SourcePaths(string id)
        {
            return CachedCatalog("source:" + id, () => { var prefab = ResolvePrefab(id); return prefab == null ? new List<string>() : CatalogPaths(prefab.transform); });
        }

        private List<string> RendererPaths(string id)
        {
            return CachedCatalog("renderer:" + id, () => { var prefab = ResolvePrefab(id); return prefab == null ? new List<string>() : BoneReferenceCatalog.Build(prefab.transform).GetCanonicalEntries()
                .Where(x => x.Value.GetComponent<SpriteRenderer>() != null && x.Value.GetComponent<UnityEngine.U2D.Animation.SpriteSkin>() != null).Select(x => x.Key).ToList(); });
        }

        /// <summary>Every sprite on the host model, skinned or not (a held weapon is often a plain SpriteRenderer).</summary>
        private List<string> HostSpritePaths(string id)
        {
            return CachedCatalog("hostsprites:" + id, () => { var prefab = ResolvePrefab(id); return prefab == null ? new List<string>() : BoneReferenceCatalog.Build(prefab.transform).GetCanonicalEntries()
                .Where(x => x.Value != null && x.Value.GetComponent<SpriteRenderer>() != null).Select(x => x.Key).ToList(); });
        }

        private List<string> PreviewClipNames(string id)
        {
            return CachedCatalog("clips:" + id, () => { var animator = ResolvePrefab(id)?.GetComponentInChildren<Animator>(); return animator?.runtimeAnimatorController?.animationClips?.Where(x => x != null).Select(x => x.name).Distinct().OrderBy(x => x).ToList() ?? new List<string>(); });
        }

        private static List<string> CatalogPaths(Transform root)
        {
            return BoneReferenceCatalog.Build(root).GetCanonicalEntries().Select(x => x.Key).ToList();
        }

        private static GameObject ResolvePrefab(string id)
        {
            return VisualInstanceFactory.ResolveSourcePrefab(id);
        }

        private List<string> SourceIds() => CachedCatalog("sources", () => DataHelper.GetAllNpcIds().Concat(DataHelper.GetAllSkinIds()).Concat(GetPetCardIds()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList());

        private static IEnumerable<string> GetPetCardIds()
        {
            foreach (var id in DataHelper.GetAllCardIds())
            {
                var card = DataHelper.GetCard(id);
                var data = card == null ? null : Traverse.Create(card).Field("card").GetValue<CardDataNew>();
                if (data?.PetData?.PetModel != null) yield return id;
            }
        }
        private List<string> CachedCatalog(string key, Func<List<string>> factory)
        {
            if (!_catalogCache.TryGetValue(key, out var values)) { values = factory(); _catalogCache[key] = values; }
            return values;
        }

        private static VisualAttachmentKind SelectAttachmentKind(VisualAttachmentKind current, string popup)
        {
            var values = new[] { VisualAttachmentKind.RigidAttachment, VisualAttachmentKind.ExtractedSourceSubRig };
            var names = values.Select(value => value.ToString()).ToList();
            int index = Array.IndexOf(values, current); if (index < 0) index = 0;
            string selected = EditorFields.IdDropdown("Attachment", names[index], names, popup);
            int selectedIndex = names.IndexOf(selected);
            return values[selectedIndex < 0 ? 0 : selectedIndex];
        }

        private static VisualNodeDef NewNode(CharacterVisualDef graph)
        {
            var used = new HashSet<string>(graph.Nodes.Where(x => x != null).Select(x => x.NodeId), StringComparer.OrdinalIgnoreCase);
            int i = 1; string id; do { id = "node_" + i++; } while (used.Contains(id));
            string baseId = graph.BasePresentation ?? "";
            string root = BoneReferenceCatalog.RootReference;
            return new VisualNodeDef { NodeId = id, Pivot = VisualPivotMode.SpriteCenter, SortingPolicy = new VisualSortingPolicy { Placement = VisualPlacement.Front }, HostSocketRef = new CanonicalTransformRef { Path = root }, SourcePrefabRef = baseId, SourceAnchorRef = new CanonicalTransformRef { Path = root }, SourceSubtreeRef = new CanonicalTransformRef { Path = root } };
        }

        private void DrawRename(ModProject project, CharacterVisualDef graph)
        {
            if (_renameFor != graph.Id) { _renameFor = graph.Id; _renameBuffer = graph.Id; _foldUsedBy = false; }
            GUILayout.BeginHorizontal();
            _renameBuffer = EditorFields.TextField("Name (ID)", _renameBuffer);
            string wanted = (_renameBuffer ?? "").Trim();
            bool valid = wanted.Length > 0 && wanted.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
            bool taken = !string.Equals(wanted, graph.Id, StringComparison.Ordinal) &&
                         (project.VisualGraphs.Keys.Any(k => string.Equals(k, wanted, StringComparison.OrdinalIgnoreCase)) ||
                          project.VisualGraphPatches.Keys.Any(k => string.Equals(k, wanted, StringComparison.OrdinalIgnoreCase)));
            GUI.enabled = valid && !taken && !string.Equals(wanted, graph.Id, StringComparison.Ordinal);
            if (GUILayout.Button("Rename", EditorStyles.MiniButton, GUILayout.Width(58))) RenameGraph(project, graph, wanted);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!valid) GUILayout.Label("<color=#e07070>Use letters, digits, '_' or '-'.</color>", EditorStyles.RichLabel);
            else if (taken) GUILayout.Label("<color=#e07070>That name is already used by another graft sprite.</color>", EditorStyles.RichLabel);

            if (GUILayout.Button((_foldUsedBy ? "\u25BC" : "\u25B6") + " Used by", EditorStyles.MiniButton, GUILayout.Width(110))) _foldUsedBy = !_foldUsedBy;
            if (!_foldUsedBy) return;
            if (_usedByKey != project.ModId + "|" + graph.Id || Time.realtimeSinceStartup - _usedByTime > 2f)
            {
                _usedByKey = project.ModId + "|" + graph.Id;
                _usedByTime = Time.realtimeSinceStartup;
                _usedBy = ModReferences.FindUsages(project, graph.Id, nameof(ModProject.VisualGraphs), graph.Id);
            }
            var uses = _usedBy;
            if (uses.Count == 0) GUILayout.Label("<color=#888>Not used by any skin, NPC or card in this mod.</color>", EditorStyles.RichLabel);
            foreach (var use in uses.Take(40))
                GUILayout.Label($"<color=#aaa>{use.Family}</color> {use.OwnerId} <color=#888>\u2192 {use.Field}</color>", EditorStyles.RichLabel);
        }

        /// <summary>Renames a graft sprite and repoints every skin, NPC and pet card that used it.</summary>
        private void RenameGraph(ModProject project, CharacterVisualDef graph, string newId)
        {
            FlushPendingSave();
            string oldId = graph.Id;
            project.VisualGraphs.Remove(oldId);
            ModProjectLoader.DeleteEntity(project, "visualgraphs", oldId);
            graph.Id = newId;
            project.VisualGraphs[newId] = graph;
            int rewired = ModReferences.Rewrite(project, oldId, newId, nameof(ModProject.VisualGraphs), newId);
            if (rewired > 0) Plugin.Log.LogInfo($"[References] Renamed Graft Sprite '{oldId}' -> '{newId}': updated {rewired} reference(s).");
            _selectedGraphId = newId;
            _renameFor = null;
            VisualGraphThumbnailCache.Invalidate(oldId);
            SaveGraph(project, graph, false);
        }

        private void CreateGraph(ModProject project)
        {
            FlushPendingSave();
            string id = project.ModId + "_visual_graph"; int suffix = 1;
            while (project.VisualGraphs.Keys.Any(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)) ||
                   project.VisualGraphPatches.Keys.Any(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))) id = project.ModId + "_visual_graph" + suffix++;
            var graph = new CharacterVisualDef { Id = id, BasePresentation = DataHelper.GetAllNpcIds().FirstOrDefault() ?? "" };
            graph.Nodes.Add(NewNode(graph));
            graph.Normalize();
            project.VisualGraphs[id] = graph;
            _selectedGraphId = id;
            _selectedNodeIndex = 0;
            SaveGraph(project, graph, false);
        }

        private void DeleteGraph(ModProject project, bool patch)
        {
            FlushPendingSave();
            if (patch) { project.VisualGraphPatches.Remove(_selectedGraphId); ModProjectLoader.DeleteEntity(project, "visualgraphs", _selectedGraphId, true); }
            else { project.VisualGraphs.Remove(_selectedGraphId); ModProjectLoader.DeleteEntity(project, "visualgraphs", _selectedGraphId); }
            string deleted = _selectedGraphId; _selectedGraphId = null; _selectedNodeIndex = -1; _pendingGraphDeleteId = null; DestroyPreview(); project.IsDirty = true; VisualGraphThumbnailCache.Invalidate(deleted); LoadOrderManager.RefreshVisualGraphs();
        }

        private void MarkDirty(ModProject project, CharacterVisualDef graph, bool patch)
        {
            _dirtyProject = project;
            _dirtyGraph = graph;
            _dirtyIsPatch = patch;
            _revision++;
            _compiledRevision = -1;
        }

        private void FlushPendingSave()
        {
            if (_dirtyProject == null || _dirtyGraph == null) return;
            var project = _dirtyProject;
            var graph = _dirtyGraph;
            bool patch = _dirtyIsPatch;
            _dirtyProject = null;
            _dirtyGraph = null;
            SaveGraph(project, graph, patch);
        }

        private void SaveGraph(ModProject project, CharacterVisualDef graph, bool patch)
        {
            bool pruned = PruneNeutralParts(graph);
            ModProjectLoader.SaveEntity(project, "visualgraphs", graph.Id, graph, patch);
            project.IsDirty = true; project.LastChangeTime = Time.realtimeSinceStartup;
            graph.Normalize();
            // The preview is already rebuilt on every edit (MarkDirty bumps the revision) and live drags move it directly, so an autosave must not
            // rebuild it again. Only recompile the diagnostics, and rebuild if pruning actually changed the graph.
            _compiledRevision = -1;
            if (pruned) _revision++;
            // Reloading every mod and rebuilding all live rigs is heavy; do it once editing settles instead of on each autosave.
            _gameRefreshAt = Time.realtimeSinceStartup + GameRefreshDelay;
            _gameRefreshPending = true;
            VisualGraphThumbnailCache.Invalidate(graph.Id);
            ModEditor.EntityPreview?.Invalidate();
        }

        private void RunGameRefresh()
        {
            if (!_gameRefreshPending) return;
            _gameRefreshPending = false;
            LoadOrderManager.RefreshVisualGraphs();
        }

        public void ApplyHotReload() { }

        private void DrawDiagnostics(CharacterVisualDef graph)
        {
            var result = GetCompileResult(graph);
            int errors = result.Diagnostics.Count(d => d.Severity == VisualGraphDiagnosticSeverity.Error);
            GUILayout.Label(!result.Success ? "<color=#e07070>Compiler: graph unusable (fix the base settings)</color>"
                : errors == 0 ? "<color=#70d090>Compiler: valid</color>"
                : $"<color=#e6b35a>Compiler: {errors} issue{(errors == 1 ? "" : "s")} - affected nodes are skipped, the rest still render</color>", EditorStyles.RichLabel);
            _diagnosticScroll = GUILayout.BeginScrollView(_diagnosticScroll, GUILayout.Height(100));
            foreach (var diagnostic in result.Diagnostics.OrderBy(x => x.NodeId).ThenBy(x => x.Field)) GUILayout.Label(diagnostic.ToString(), EditorStyles.RichLabel);
            GUILayout.EndScrollView();
        }

        private VisualGraphCompileResult GetCompileResult(CharacterVisualDef graph)
        {
            if (_compiledResult == null || _compiledDefinition != graph || _compiledRevision != _revision)
            {
                _compiledDefinition = graph;
                _compiledRevision = _revision;
                _compiledResult = VisualGraphCompiler.Compile(graph);
            }
            return _compiledResult;
        }
    }
}
