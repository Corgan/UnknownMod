using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Interactive Graft Sprite viewport: live preview, host bone overlay, node handles,
    /// drag/hotkey move-rotate-scale, attach mode (re-graft onto a bone), pan/zoom and undo.
    /// </summary>
    public sealed partial class VisualGraphEditor
    {
        private enum GizmoTool { None, Drag, Move, Rotate, Scale }
        private enum GizmoAxis { Free, X, Y }

        private sealed class HostBone { public string Path; public Transform T; public int ParentIndex = -1; }
        private sealed class EditSnapshot { public string GraphId; public string NodeId; public LocalAttachmentTransform Transform; public string SocketPath; public HostPartDef Part; public string AfterId; public string NodeJson; public int Index; public bool Created; public string GraphJson; public float Time; }

        private const int MaxUndo = 64;
        private const float BonePickRadius = 14f;
        private const float DragThreshold = 4f;

        private readonly List<HostBone> _hostBones = new();
        private readonly Dictionary<string, int> _boneIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Transform> _liveRoots = new(StringComparer.Ordinal);
        private readonly List<EditSnapshot> _undo = new();
        private readonly List<string> _redo = new();
        private string _undoGraphId;
        private string _panelBeforeJson;
        private const float PanelUndoCoalesce = 1.2f;

        private bool _showBones = true, _showLinks = true, _showHelp = true, _pickBone, _hideMode, _layerMode;
        private readonly Dictionary<Transform, string> _hostPathByT = new();
        private Vector2 _hideCycleAt;
        private int _hideCycle;
        private bool _panning, _needFrame;
        private string _viewKey;
        private int _hoverBone = -1;
        private int _attachNode = -1;
        private GUIStyle _gizmoLabel;

        private GizmoTool _tool;
        private GizmoAxis _axis;
        private CharacterVisualDef _toolGraph;
        private VisualNodeDef _toolNode;
        private Transform _toolRoot;
        private LocalAttachmentTransform _toolStart;
        private Vector2 _toolStartMouse;
        private Vector3 _toolStartMouseWorld, _toolStartNodeWorld, _toolCenterWorld;
        private float _toolLastAngle, _toolAngleAccum, _toolStartPixelDistance;
        private bool _toolMoved;
        private ModProject _toolProject;
        private bool _toolIsPatch;

        private static readonly Color BoneColor = new Color(.55f, .8f, 1f, .55f);
        private static readonly Color SocketColor = new Color(1f, .62f, .15f, 1f);
        private static readonly Color HoverColor = new Color(1f, .95f, .3f, 1f);
        private static readonly Color NodeColor = new Color(.45f, 1f, .6f, .9f);
        private static readonly Color NodeDimColor = new Color(.45f, 1f, .6f, .45f);
        private static readonly Color HiddenColor = new Color(1f, .3f, .3f, .95f);
        private static readonly Color LinkColor = new Color(.35f, .9f, 1f, .75f);

        // ═══════════════════════════════════════════════════════════════
        //  Entry point
        // ═══════════════════════════════════════════════════════════════

        public void DrawViewport(Rect rect)
        {
            var project = ModManagerPanel.ActiveProject;
            CharacterVisualDef graph = null;
            bool isPatch = false;
            if (project != null && _selectedGraphId != null)
            {
                if (!project.VisualGraphs.TryGetValue(_selectedGraphId, out graph))
                {
                    project.VisualGraphPatches.TryGetValue(_selectedGraphId, out graph);
                    isPatch = graph != null;
                }
            }
            if (graph == null) { GUI.Label(rect, "Select a graft sprite"); return; }

            if (!string.Equals(_undoGraphId, graph.Id, StringComparison.Ordinal))
            {
                _undo.Clear(); _redo.Clear(); _panelBeforeJson = null; _undoGraphId = graph.Id; _tool = GizmoTool.None; _toolPart = null; ClearModes(); _selectedPartPath = null; _panning = false;
            }

            GUI.BeginGroup(rect);
            Rect area = new Rect(0f, 0f, rect.width, rect.height);
            bool ready = EnsurePreview(graph, area);
            if (ready)
            {
                RefreshSourceVisibility(SelectedNode(graph));
                HandleSourceInput(project, graph, isPatch, area);
                HandleViewportInput(project, graph, isPatch, area);
                if (Event.current.type == EventType.Repaint)
                {
                    RenderPreview();
                    GUI.DrawTexture(area, _previewTexture, ScaleMode.StretchToFill, false);
                    DrawGizmos(graph, area);
                    DrawSourceInset(SelectedNode(graph), area);
                    DrawSourceDragGhost(graph, area);
                }
            }
            else if (Event.current.type == EventType.Repaint)
            {
                GUI.Label(new Rect(12f, 70f, area.width - 24f, 40f), "<color=#e07070>Host model could not be loaded. Pick a valid Host Model.</color>", EditorStyles.RichLabel);
            }
            if (ready) DrawSourceHeader(SelectedNode(graph), area);
            DrawViewportToolbar(graph, area, ready);
            GUI.EndGroup();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Preview lifetime
        // ═══════════════════════════════════════════════════════════════

        private bool EnsurePreview(CharacterVisualDef definition, Rect area)
        {
            var prefab = ResolvePrefab(definition.BasePresentation);
            var result = GetCompileResult(definition);
            string key = definition.Id + ":" + _revision + ":" + (_previewClipName ?? "");
            if (prefab == null) { DestroyPreview(); return false; }
            if (_previewHost == null || key != _lastPreviewKey)
            {
                DestroyPreview();
                _lastPreviewKey = key;
                _previewHost = UnityEngine.Object.Instantiate(prefab, PreviewOrigin, Quaternion.identity);
                _previewHost.SetActive(true);
                var animator = _previewHost.GetComponentInChildren<Animator>();
                _previewClip = animator?.runtimeAnimatorController?.animationClips?.FirstOrDefault(x => x != null && x.name == _previewClipName) ?? animator?.runtimeAnimatorController?.animationClips?.FirstOrDefault();
                BuildHostBones();
                // With compile errors the host still shows so bones can be inspected and a socket picked.
                if (result.Success)
                {
                    var created = VisualInstanceFactory.CreateAttachments(result.Graph, _previewHost.transform, null, animator, false);
                    _previewInstance = created.Instance;
                    foreach (var d in created.Diagnostics) Plugin.Log.LogWarning("[VisualGraphPreview] " + d);
                    CollectLiveRoots();
                }
                PreviewPose.DisableAnimators(_previewHost);
                EnsureCamera();
                string frameKey = definition.Id + ":" + definition.BasePresentation;
                if (frameKey != _viewKey) { _viewKey = frameKey; _needFrame = true; }
                _lastTime = Time.realtimeSinceStartup;
            }
            EnsureTexture(area);
            if (_needFrame) { AutoFrame(); _needFrame = false; }
            return true;
        }

        private void RenderPreview()
        {
            if (_previewCamera == null || _previewHost == null) return;
            if (_playing && _previewClip != null) _phase = Mathf.Repeat(_phase + (float)(Time.realtimeSinceStartup - _lastTime) * _speed / Mathf.Max(_previewClip.length, .01f), 1f);
            _lastTime = Time.realtimeSinceStartup;
            if (_previewClip != null)
            {
                _previewClip.SampleAnimation(_previewHost, AnimationPlaybackResolver.ToSampleTime(_previewClip, _phase));
                _previewInstance?.EvaluateAt(_previewClip, _phase);
                PreviewPose.RefreshSkins();
            }
            _previewCamera.Render();
        }

        private void EnsureCamera()
        {
            if (_previewCamera != null) return;
            var go = new GameObject("[VisualGraphPreviewCamera]");
            go.hideFlags = HideFlags.HideAndDontSave;
            _previewCamera = go.AddComponent<Camera>();
            _previewCamera.orthographic = true;
            _previewCamera.enabled = false;
            _previewCamera.backgroundColor = new Color(.08f, .08f, .1f, 1f);
            _previewCamera.clearFlags = CameraClearFlags.SolidColor;
        }

        private void EnsureTexture(Rect area)
        {
            if (_previewCamera == null) return;
            int w = Mathf.Clamp(Mathf.RoundToInt(area.width), 64, 2560);
            int h = Mathf.Clamp(Mathf.RoundToInt(area.height), 64, 2560);
            if (_previewTexture != null && _previewTexture.width == w && _previewTexture.height == h) return;
            _previewCamera.targetTexture = null;
            if (_previewTexture != null) { _previewTexture.Release(); UnityEngine.Object.DestroyImmediate(_previewTexture); }
            _previewTexture = new RenderTexture(w, h, 16);
            _previewTexture.Create();
            _previewCamera.targetTexture = _previewTexture;
        }

        private void AutoFrame()
        {
            if (_previewHost == null || _previewCamera == null) return;
            var renderers = _previewHost.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(_previewHost.transform.position, Vector3.one * 2f);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float aspect = _previewTexture != null ? _previewTexture.width / (float)_previewTexture.height : 1f;
            _previewCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
            _previewCamera.orthographicSize = Mathf.Max(.25f, Mathf.Max(bounds.extents.y * 1.35f, bounds.extents.x * 1.35f / aspect));
        }

        private void DestroyPreview()
        {
            _tool = GizmoTool.None; _toolPart = null; _toolLive = null; _toolBone = null; _panning = false;
            _previewInstance?.DestroyImmediate(); _previewInstance = null;
            if (_previewHost != null) UnityEngine.Object.DestroyImmediate(_previewHost);
            _previewHost = null; _previewClip = null; _lastPreviewKey = null;
            _hostBones.Clear(); _boneIndex.Clear(); _hostPathByT.Clear(); _liveRoots.Clear(); _hoverBone = -1;
        }

        private void BuildHostBones()
        {
            _hostBones.Clear(); _boneIndex.Clear(); _hostPathByT.Clear();
            var entries = BoneReferenceCatalog.Build(_previewHost.transform).GetCanonicalEntries();
            var byTransform = new Dictionary<Transform, int>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Value == null) continue;
                byTransform[entries[i].Value] = _hostBones.Count;
                _boneIndex[entries[i].Key] = _hostBones.Count;
                _hostPathByT[entries[i].Value] = entries[i].Key;
                _hostBones.Add(new HostBone { Path = entries[i].Key, T = entries[i].Value });
            }
            foreach (var bone in _hostBones)
                for (Transform p = bone.T.parent; p != null; p = p.parent)
                    if (byTransform.TryGetValue(p, out int pi)) { bone.ParentIndex = pi; break; }
        }

        private void CollectLiveRoots()
        {
            _liveRoots.Clear();
            if (_previewInstance == null) return;
            const string prefix = "VisualGraph_", suffix = "_AttachmentRoot";
            foreach (var root in _previewInstance.Roots)
            {
                if (root == null) continue;
                string n = root.name;
                if (n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(suffix, StringComparison.Ordinal) && n.Length > prefix.Length + suffix.Length)
                    _liveRoots[n.Substring(prefix.Length, n.Length - prefix.Length - suffix.Length)] = root.transform;
            }
        }

        private Transform LiveRoot(VisualNodeDef node)
        {
            return node != null && node.NodeId != null && _liveRoots.TryGetValue(node.NodeId, out var t) && t != null ? t : null;
        }

        private VisualNodeDef SelectedNode(CharacterVisualDef graph)
        {
            return graph.Nodes != null && _selectedNodeIndex >= 0 && _selectedNodeIndex < graph.Nodes.Count ? graph.Nodes[_selectedNodeIndex] : null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Coordinate helpers
        // ═══════════════════════════════════════════════════════════════

        private Vector2 WorldToGui(Vector3 world, Rect area)
        {
            Vector3 v = _previewCamera.WorldToViewportPoint(world);
            return new Vector2(area.x + v.x * area.width, area.y + (1f - v.y) * area.height);
        }

        private Vector3 GuiToWorld(Vector2 p, Rect area)
        {
            return _previewCamera.ViewportToWorldPoint(new Vector3((p.x - area.x) / area.width, 1f - (p.y - area.y) / area.height, 10f));
        }

        private float WorldPerPixel(Rect area) { return 2f * _previewCamera.orthographicSize / Mathf.Max(1f, area.height); }

        private static bool IsMirrored(Transform t) { return t.lossyScale.x * t.lossyScale.y < 0f; }

        private static float Snap(float v, float step) { return Mathf.Round(v / step) * step; }

        private bool OverUi(Vector2 p, Rect area)
        {
            if (new Rect(8f, 8f, area.width - 16f, 52f).Contains(p)) return true;
            if (_srcVisible && _showSource && SourceRect(area).Contains(p)) return true;
            if (_srcVisible && _showSource && _srcPickerOpen && PickerPanelRect(SourceRect(area), area).Contains(p)) return true;
            return _showHelp && HelpRect(area).Contains(p);
        }

        private static Rect HelpRect(Rect area) { return new Rect(8f, area.height - 328f, 300f, 320f); }

        private Rect NodeScreenRect(Transform root, Rect area)
        {
            bool any = false;
            Bounds b = default;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            Vector2 c = WorldToGui(root.position, area);
            if (!any) return new Rect(c.x - 8f, c.y - 8f, 16f, 16f);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector2 s = WorldToGui(corner, area);
                min = Vector2.Min(min, s); max = Vector2.Max(max, s);
            }
            return Rect.MinMaxRect(min.x - 3f, min.y - 3f, max.x + 3f, max.y + 3f);
        }

        private int PickNode(Vector2 mouse, Rect area, CharacterVisualDef graph)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                var root = LiveRoot(graph.Nodes[i]);
                if (root == null) continue;
                float dist = (WorldToGui(root.position, area) - mouse).magnitude;
                Rect b = NodeScreenRect(root, area);
                float score;
                if (dist <= 12f) score = dist;
                else if (b.Contains(mouse)) score = 100f + b.width * b.height * .001f;
                else continue;
                if (i == _selectedNodeIndex) score -= .5f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private int NearestBone(Vector2 mouse, Rect area, float radius)
        {
            int best = -1;
            float bestDist = radius;
            for (int i = 0; i < _hostBones.Count; i++)
            {
                if (_hostBones[i].T == null) continue;
                float d = (WorldToGui(_hostBones[i].T.position, area) - mouse).magnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Input
        // ═══════════════════════════════════════════════════════════════

        private void HandleViewportInput(ModProject project, CharacterVisualDef graph, bool isPatch, Rect area)
        {
            Event e = Event.current;
            Vector2 m = e.mousePosition;
            bool inside = area.Contains(m) && !OverUi(m, area);

            if (_tool != GizmoTool.None)
            {
                var node = _toolNode;
                if (_toolPart != null)
                {
                    if (_toolGraph != graph || _toolBone == null || !graph.HostParts.Contains(_toolPart)) { _tool = GizmoTool.None; _toolPart = null; }
                    else if (e.type == EventType.Repaint || e.type == EventType.MouseDrag || e.type == EventType.MouseMove) UpdatePartTool(m, e, area);
                }
                else if (_toolGraph != graph || node == null || !graph.Nodes.Contains(node) || _toolRoot == null) _tool = GizmoTool.None;
                else if (e.type == EventType.Repaint || e.type == EventType.MouseDrag || e.type == EventType.MouseMove) UpdateTool(m, e, area);
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!inside) break;
                    GUIUtility.keyboardControl = 0;
                    if (e.button == 0)
                    {
                        if (_tool != GizmoTool.None) { CommitTool(); }
                        else if (_rigMode) RigClick(m, area, project, graph, isPatch);
                        else if (_hideMode)
                        {
                            var target = HideTarget(m, area, graph, out _, out _);
                            if (target != null) ToggleHostHidden(project, graph, isPatch, target);
                        }
                        else if (_layerMode)
                        {
                            var target = HideTarget(m, area, graph, out _, out _);
                            var sel = SelectedNode(graph);
                            if (target != null && sel != null && _hostPathByT.TryGetValue(target.transform, out var refPath))
                            {
                                sel.SortingPolicy.Placement = e.shift ? VisualPlacement.BehindPart : VisualPlacement.InFrontOfPart;
                                sel.SortingPolicy.ReferencePath = refPath;
                                MarkDirty(project, graph, isPatch);
                            }
                        }
                        else if (_pickBone)
                        {
                            // Attach mode: press a node (or use the selected one), release on a bone. Only the attach point changes; the node stays where it is on screen.
                            int hit = PickNode(m, area, graph);
                            if (hit >= 0) { _selectedNodeIndex = hit; _pendingNodeDeleteId = null; }
                            _attachNode = _selectedNodeIndex;
                        }
                        else
                        {
                            // Alt+click prefers a host bone over a graft that covers it.
                            if (e.alt && BoneClick(m, area, project, graph, isPatch)) { }
                            else
                            {
                                int hit = PickNode(m, area, graph);
                                if (hit >= 0)
                                {
                                    _partFocus = false;
                                    _selectedNodeIndex = hit; _pendingNodeDeleteId = null;
                                    BeginTool(GizmoTool.Drag, m, area, project, graph, isPatch, graph.Nodes[hit]);
                                }
                                else if (!BoneClick(m, area, project, graph, isPatch)) _partFocus = false;
                            }
                        }
                        e.Use();
                    }
                    else if (e.button == 1 || e.button == 2)
                    {
                        if (_tool != GizmoTool.None) CancelTool();
                        else if (_rigMode) _rigMode = false;
                        else if (_partFocus) _partFocus = false;
                        else if (_pickBone) _pickBone = false;
                        else if (_hideMode) _hideMode = false;
                        else if (_layerMode) _layerMode = false;
                        else _panning = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_panning)
                    {
                        float upp = WorldPerPixel(area);
                        _previewCamera.transform.position += new Vector3(-e.delta.x * upp, e.delta.y * upp, 0f);
                        e.Use();
                    }
                    else if (_tool == GizmoTool.Drag || _attachNode >= 0) e.Use();
                    break;

                case EventType.MouseUp:
                    if (e.button == 0 && _tool == GizmoTool.Drag)
                    {
                        CommitTool();
                        e.Use();
                    }
                    else if (e.button == 0 && _attachNode >= 0)
                    {
                        var node = _attachNode < graph.Nodes.Count ? graph.Nodes[_attachNode] : null;
                        _attachNode = -1;
                        int bi = NearestBone(m, area, BonePickRadius * 1.5f);
                        if (node != null && bi >= 0 && !string.Equals(_hostBones[bi].Path, node.HostSocketRef?.Path, StringComparison.Ordinal))
                        {
                            Regraft(project, graph, isPatch, node, _hostBones[bi]);
                            _pickBone = false;
                        }
                        e.Use();
                    }
                    else if ((e.button == 1 || e.button == 2) && _panning) { _panning = false; e.Use(); }
                    break;

                case EventType.MouseLeaveWindow:
                    if (_tool == GizmoTool.Drag) CommitTool();
                    _attachNode = -1;
                    _panning = false;
                    break;

                case EventType.ScrollWheel:
                    if (inside)
                    {
                        Vector3 before = GuiToWorld(m, area);
                        _previewCamera.orthographicSize = Mathf.Clamp(_previewCamera.orthographicSize * (1f + e.delta.y * .08f), .05f, 200f);
                        Vector3 after = GuiToWorld(m, area);
                        _previewCamera.transform.position += before - after;
                        e.Use();
                    }
                    break;

                case EventType.KeyDown:
                    if (area.Contains(m)) HandleKey(e, project, graph, isPatch, area);
                    break;
            }
        }

        private void HandleKey(Event e, ModProject project, CharacterVisualDef graph, bool isPatch, Rect area)
        {
            if (GUIUtility.keyboardControl != 0) return;
            KeyCode k = e.keyCode;
            Vector2 m = e.mousePosition;

            if (_tool != GizmoTool.None)
            {
                switch (k)
                {
                    case KeyCode.X: _axis = _axis == GizmoAxis.X ? GizmoAxis.Free : GizmoAxis.X; break;
                    case KeyCode.Y: _axis = _axis == GizmoAxis.Y ? GizmoAxis.Free : GizmoAxis.Y; break;
                    case KeyCode.Escape: CancelTool(); break;
                    case KeyCode.Return: case KeyCode.KeypadEnter: CommitTool(); break;
                    default: return;
                }
                e.Use();
                return;
            }

            var node = SelectedNode(graph);
            var root = LiveRoot(node);
            if (e.control && k == KeyCode.Z) { if (e.shift) Redo(project, graph, isPatch); else Undo(project, graph, isPatch); e.Use(); return; }
            if (e.control && k == KeyCode.Y) { Redo(project, graph, isPatch); e.Use(); return; }
            if (e.control || e.alt) return;
            switch (k)
            {
                case KeyCode.G: if (!TryBeginPartTool(GizmoTool.Move, m, area, project, graph, isPatch) && root != null) BeginTool(GizmoTool.Move, m, area, project, graph, isPatch, node); break;
                case KeyCode.R: if (!TryBeginPartTool(GizmoTool.Rotate, m, area, project, graph, isPatch) && root != null) BeginTool(GizmoTool.Rotate, m, area, project, graph, isPatch, node); break;
                case KeyCode.S: if (!TryBeginPartTool(GizmoTool.Scale, m, area, project, graph, isPatch) && root != null) BeginTool(GizmoTool.Scale, m, area, project, graph, isPatch, node); break;
                case KeyCode.E: { bool on = !_rigMode; ClearModes(); _rigMode = on; break; }
                case KeyCode.A: case KeyCode.B: { bool on = !_pickBone && node != null; ClearModes(); _pickBone = on; break; }
                case KeyCode.X: { bool on = !_hideMode; ClearModes(); _hideMode = on; break; }
                case KeyCode.L: { bool on = !_layerMode && node != null; ClearModes(); _layerMode = on; break; }
                case KeyCode.Tab: if (_hideMode || _layerMode) _hideCycle++; else return; break;
                case KeyCode.D: if (!PartMode && node != null) { DuplicateNode(graph, _selectedNodeIndex); MarkDirty(project, graph, isPatch); } break;
                case KeyCode.F: case KeyCode.Home: _needFrame = true; break;
                case KeyCode.H: _showBones = !_showBones; break;
                case KeyCode.C: _showLinks = !_showLinks; break;
                case KeyCode.V: _showSource = !_showSource; break;
                case KeyCode.Space: _playing = !_playing; break;
                case KeyCode.Escape: ClearModes(); break;
                case KeyCode.LeftArrow: if (PartMode) NudgePart(project, graph, isPatch, -1f, 0f, e.shift); else Nudge(project, graph, isPatch, node, root, -1f, 0f, e.shift); break;
                case KeyCode.RightArrow: if (PartMode) NudgePart(project, graph, isPatch, 1f, 0f, e.shift); else Nudge(project, graph, isPatch, node, root, 1f, 0f, e.shift); break;
                case KeyCode.UpArrow: if (PartMode) NudgePart(project, graph, isPatch, 0f, 1f, e.shift); else Nudge(project, graph, isPatch, node, root, 0f, 1f, e.shift); break;
                case KeyCode.DownArrow: if (PartMode) NudgePart(project, graph, isPatch, 0f, -1f, e.shift); else Nudge(project, graph, isPatch, node, root, 0f, -1f, e.shift); break;
                default: return;
            }
            e.Use();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Transform tools
        // ═══════════════════════════════════════════════════════════════

        private void BeginTool(GizmoTool tool, Vector2 mouse, Rect area, ModProject project, CharacterVisualDef graph, bool isPatch, VisualNodeDef node)
        {
            var root = LiveRoot(node);
            if (root == null) return;
            _tool = tool; _axis = GizmoAxis.Free;
            _toolProject = project; _toolGraph = graph; _toolIsPatch = isPatch; _toolNode = node; _toolRoot = root; _toolPart = null;
            _toolStart = CloneTransform(node.LocalAttachmentTransform);
            _toolStartMouse = mouse;
            _toolStartMouseWorld = GuiToWorld(mouse, area);
            _toolStartNodeWorld = root.position;
            _toolCenterWorld = root.position;
            _toolLastAngle = Mathf.Atan2(_toolStartMouseWorld.y - _toolCenterWorld.y, _toolStartMouseWorld.x - _toolCenterWorld.x) * Mathf.Rad2Deg;
            _toolAngleAccum = 0f;
            _toolStartPixelDistance = Mathf.Max((mouse - WorldToGui(root.position, area)).magnitude, 20f);
            _toolMoved = false;
        }

        private void UpdateTool(Vector2 mouse, Event e, Rect area)
        {
            var node = _toolNode; var root = _toolRoot;
            if (node == null || root == null || root.parent == null) { _tool = GizmoTool.None; return; }
            if (_tool == GizmoTool.Drag && !_toolMoved && (mouse - _toolStartMouse).magnitude < DragThreshold) return;

            float fine = e.shift && _tool != GizmoTool.Drag ? .1f : 1f;
            bool snap = e.control;
            Vector3 mouseWorld = GuiToWorld(mouse, area);
            Transform socket = root.parent;
            var t = CloneTransform(_toolStart);

            switch (_tool)
            {
                case GizmoTool.Drag:
                case GizmoTool.Move:
                {
                    Vector3 delta = (mouseWorld - _toolStartMouseWorld) * fine;
                    if (_axis == GizmoAxis.X) delta.y = 0f;
                    if (_axis == GizmoAxis.Y) delta.x = 0f;
                    if (snap && _axis != GizmoAxis.Free) { delta.x = Snap(delta.x, .05f); delta.y = Snap(delta.y, .05f); }
                    Vector3 world = _toolStartNodeWorld + delta;
                    world.z = root.position.z;
                    Vector3 local = socket.InverseTransformPoint(world);
                    if (snap && _axis == GizmoAxis.Free) { local.x = Snap(local.x, .05f); local.y = Snap(local.y, .05f); }
                    t.PositionX = local.x; t.PositionY = local.y;
                    break;
                }
                case GizmoTool.Rotate:
                {
                    float a = Mathf.Atan2(mouseWorld.y - _toolCenterWorld.y, mouseWorld.x - _toolCenterWorld.x) * Mathf.Rad2Deg;
                    _toolAngleAccum += Mathf.DeltaAngle(_toolLastAngle, a);
                    _toolLastAngle = a;
                    float d = _toolAngleAccum * fine;
                    float rot = _toolStart.Rotation + (IsMirrored(socket) ? -d : d);
                    if (snap) rot = Snap(rot, 15f);
                    t.Rotation = rot;
                    break;
                }
                case GizmoTool.Scale:
                {
                    float dist = Mathf.Max((mouse - WorldToGui(_toolCenterWorld, area)).magnitude, 20f);
                    float ratio = 1f + (dist / _toolStartPixelDistance - 1f) * fine;
                    float sx = _toolStart.ScaleX * (_axis == GizmoAxis.Y ? 1f : ratio);
                    float sy = _toolStart.ScaleY * (_axis == GizmoAxis.X ? 1f : ratio);
                    if (snap) { sx = Snap(sx, .05f); sy = Snap(sy, .05f); }
                    t.ScaleX = Mathf.Abs(sx) < .01f ? .01f * Mathf.Sign(sx == 0f ? 1f : sx) : sx;
                    t.ScaleY = Mathf.Abs(sy) < .01f ? .01f * Mathf.Sign(sy == 0f ? 1f : sy) : sy;
                    break;
                }
            }

            node.LocalAttachmentTransform = t;
            ApplyLive(node, root);
            _toolMoved = true;
            MarkDirtyLive(_toolProject, _toolGraph, _toolIsPatch);
        }

        private void CommitTool()
        {
            if (_tool == GizmoTool.None) return;
            if (_toolPart != null) { CommitPartTool(); return; }
            if (_toolMoved && _toolNode != null)
                PushUndo(_toolGraph, _toolNode.NodeId, _toolStart, _toolNode.HostSocketRef.Path);
            _tool = GizmoTool.None;
        }

        private void CancelTool()
        {
            if (_tool == GizmoTool.None) return;
            if (_toolPart != null) { CancelPartTool(); return; }
            if (_toolNode != null)
            {
                _toolNode.LocalAttachmentTransform = CloneTransform(_toolStart);
                if (_toolRoot != null) ApplyLive(_toolNode, _toolRoot);
                if (_toolMoved) MarkDirtyLive(_toolProject, _toolGraph, _toolIsPatch);
            }
            _tool = GizmoTool.None;
        }

        private void Nudge(ModProject project, CharacterVisualDef graph, bool isPatch, VisualNodeDef node, Transform root, float dx, float dy, bool big)
        {
            if (node == null || root == null || root.parent == null) return;
            float step = big ? .1f : .01f;
            PushUndo(graph, node.NodeId, node.LocalAttachmentTransform, node.HostSocketRef.Path);
            Vector3 local = root.parent.InverseTransformPoint(root.position + new Vector3(dx * step, dy * step, 0f));
            node.LocalAttachmentTransform.PositionX = local.x;
            node.LocalAttachmentTransform.PositionY = local.y;
            ApplyLive(node, root);
            MarkDirtyLive(project, graph, isPatch);
        }

        private static void ApplyLive(VisualNodeDef node, Transform root)
        {
            var t = node.LocalAttachmentTransform;
            root.localPosition = new Vector3(t.PositionX, t.PositionY, 0f);
            root.localRotation = Quaternion.Euler(0f, 0f, t.Rotation);
            float sx = t.ScaleX * node.Overrides.Scale, sy = t.ScaleY * node.Overrides.Scale;
            if (node.FacingPolicy == VisualFacingPolicy.MirrorX) sx = -sx;
            if (node.FacingPolicy == VisualFacingPolicy.MirrorY) sy = -sy;
            root.localScale = new Vector3(sx, sy, 1f);
        }

        private static LocalAttachmentTransform CloneTransform(LocalAttachmentTransform t)
        {
            return new LocalAttachmentTransform { PositionX = t.PositionX, PositionY = t.PositionY, Rotation = t.Rotation, ScaleX = t.ScaleX, ScaleY = t.ScaleY };
        }

        /// <summary>Marks the graph for saving without invalidating the live preview (used while dragging).</summary>
        private void MarkDirtyLive(ModProject project, CharacterVisualDef graph, bool patch)
        {
            _dirtyProject = project;
            _dirtyGraph = graph;
            _dirtyIsPatch = patch;
        }

        /// <summary>Move a node onto another host bone, keeping its on-screen position and rotation.</summary>
        private void Regraft(ModProject project, CharacterVisualDef graph, bool isPatch, VisualNodeDef node, HostBone bone, LocalAttachmentTransform undoStart = null)
        {
            if (node == null || bone == null || string.Equals(node.HostSocketRef.Path, bone.Path, StringComparison.Ordinal)) return;
            var start = undoStart ?? CloneTransform(node.LocalAttachmentTransform);
            PushUndo(graph, node.NodeId, start, node.HostSocketRef.Path);
            var root = LiveRoot(node);
            if (root != null && bone.T != null)
            {
                Vector3 local = bone.T.InverseTransformPoint(root.position);
                float worldRot = root.eulerAngles.z - bone.T.eulerAngles.z;
                node.LocalAttachmentTransform.PositionX = local.x;
                node.LocalAttachmentTransform.PositionY = local.y;
                node.LocalAttachmentTransform.Rotation = Mathf.DeltaAngle(0f, IsMirrored(bone.T) ? -worldRot : worldRot);
            }
            node.HostSocketRef.Path = bone.Path;
            MarkDirty(project, graph, isPatch);
        }

        private void DuplicateNode(CharacterVisualDef graph, int index)
        {
            var node = graph.Nodes[index];
            var clone = Newtonsoft.Json.JsonConvert.DeserializeObject<VisualNodeDef>(Newtonsoft.Json.JsonConvert.SerializeObject(node));
            clone.NodeId = UniqueNodeId(graph, node.NodeId + "_copy");
            graph.Nodes.Insert(index + 1, clone);
            PushCreatedUndo(graph, clone);
            _selectedNodeIndex = index + 1;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Undo
        // ═══════════════════════════════════════════════════════════════

        private void AddUndo(EditSnapshot snap)
        {
            _undo.Add(snap);
            _undoSerial++;
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void PushUndo(CharacterVisualDef graph, string nodeId, LocalAttachmentTransform transform, string socketPath)
        {
            if (graph == null || nodeId == null) return;
            AddUndo(new EditSnapshot { GraphId = graph.Id, NodeId = nodeId, Transform = CloneTransform(transform), SocketPath = socketPath });
        }

        /// <summary>Snapshot a whole node before changing it (parts, kind, id ...). Set <see cref="EditSnapshot.AfterId"/> once the change is done.</summary>
        private EditSnapshot PushNodeUndo(CharacterVisualDef graph, VisualNodeDef node)
        {
            if (graph == null || node == null) return null;
            var snap = new EditSnapshot { GraphId = graph.Id, NodeId = node.NodeId, AfterId = node.NodeId, NodeJson = Newtonsoft.Json.JsonConvert.SerializeObject(node), Index = graph.Nodes.IndexOf(node) };
            AddUndo(snap);
            return snap;
        }

        /// <summary>Record that a node was just added, so undo removes it.</summary>
        private void PushCreatedUndo(CharacterVisualDef graph, VisualNodeDef node)
        {
            if (graph == null || node == null) return;
            AddUndo(new EditSnapshot { GraphId = graph.Id, NodeId = node.NodeId, AfterId = node.NodeId, Created = true });
        }

        private static string SerializeGraph(CharacterVisualDef graph) => Newtonsoft.Json.JsonConvert.SerializeObject(graph);

        /// <summary>
        /// Restores every field of the graph in place; the id never changes. Deserialising a fresh object and copying its fields (rather than
        /// PopulateObject) matters because default-valued members are omitted from the JSON and would otherwise keep their edited value.
        /// </summary>
        private static void RestoreGraph(CharacterVisualDef graph, string json)
        {
            var fresh = Newtonsoft.Json.JsonConvert.DeserializeObject<CharacterVisualDef>(json);
            if (fresh == null) return;
            foreach (var field in typeof(CharacterVisualDef).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (field.Name == nameof(CharacterVisualDef.Id) || field.IsInitOnly) continue;
                field.SetValue(graph, field.GetValue(fresh));
            }
            graph.Normalize();
        }

        private int _undoSerial;

        /// <summary>Call before the inspector draws: input events snapshot the graph so the edit they cause can be undone.</summary>
        private int BeginPanelEdit(CharacterVisualDef graph)
        {
            EventType type = Event.current.type;
            if (type == EventType.MouseDown || type == EventType.KeyDown) _panelBeforeJson = SerializeGraph(graph);
            return _undoSerial;
        }

        /// <summary>
        /// Call after the inspector drew: a change made through a field becomes one whole-graph undo step.
        /// Rapid edits (typing, slider drags) coalesce into the step that started them.
        /// </summary>
        private void EndPanelEdit(CharacterVisualDef graph, bool changed, int serialAtStart)
        {
            EventType type = Event.current.type;
            if (!changed || type == EventType.Layout || type == EventType.Repaint) return;
            if (_undoSerial != serialAtStart || _panelBeforeJson == null) { _panelBeforeJson = SerializeGraph(graph); return; }
            string after = SerializeGraph(graph);
            if (after == _panelBeforeJson) return;
            float now = Time.realtimeSinceStartup;
            var last = _undo.Count > 0 ? _undo[_undo.Count - 1] : null;
            bool coalesce = last != null && last.GraphJson != null && string.Equals(last.GraphId, graph.Id, StringComparison.Ordinal) && now - last.Time < PanelUndoCoalesce;
            if (coalesce) last.Time = now;
            else AddUndo(new EditSnapshot { GraphId = graph.Id, GraphJson = _panelBeforeJson, Time = now });
            _panelBeforeJson = after;
        }

        private bool CanUndo(CharacterVisualDef graph) => graph != null && _undo.Any(s => string.Equals(s.GraphId, graph.Id, StringComparison.Ordinal));
        private bool CanRedo => _redo.Count > 0;

        private void Undo(ModProject project, CharacterVisualDef graph, bool isPatch)
        {
            string current = SerializeGraph(graph);
            if (UndoCore(project, graph, isPatch)) { _redo.Add(current); _panelBeforeJson = null; }
        }

        private void Redo(ModProject project, CharacterVisualDef graph, bool isPatch)
        {
            if (_redo.Count == 0) return;
            string target = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(new EditSnapshot { GraphId = graph.Id, GraphJson = SerializeGraph(graph), Time = -100f });
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            RestoreGraph(graph, target);
            _selectedNodeIndex = Mathf.Clamp(_selectedNodeIndex, graph.Nodes.Count > 0 ? 0 : -1, graph.Nodes.Count - 1);
            _panelBeforeJson = null;
            MarkDirty(project, graph, isPatch);
        }

        private bool UndoCore(ModProject project, CharacterVisualDef graph, bool isPatch)
        {
            while (_undo.Count > 0)
            {
                var snap = _undo[_undo.Count - 1];
                _undo.RemoveAt(_undo.Count - 1);
                if (!string.Equals(snap.GraphId, graph.Id, StringComparison.Ordinal)) continue;
                if (snap.GraphJson != null)
                {
                    RestoreGraph(graph, snap.GraphJson);
                    _selectedNodeIndex = Mathf.Clamp(_selectedNodeIndex, graph.Nodes.Count > 0 ? 0 : -1, graph.Nodes.Count - 1);
                    MarkDirty(project, graph, isPatch);
                    return true;
                }
                if (snap.Part != null) { UndoPart(project, graph, isPatch, snap); return true; }
                if (snap.Created)
                {
                    int created = graph.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, snap.AfterId, StringComparison.Ordinal));
                    if (created < 0) continue;
                    graph.Nodes.RemoveAt(created);
                    _selectedNodeIndex = Mathf.Min(created, graph.Nodes.Count - 1);
                    MarkDirty(project, graph, isPatch);
                    return true;
                }
                if (snap.NodeJson != null)
                {
                    var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<VisualNodeDef>(snap.NodeJson);
                    if (restored == null) continue;
                    int at = graph.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, snap.AfterId, StringComparison.Ordinal));
                    if (at >= 0) graph.Nodes[at] = restored;
                    else { at = Mathf.Clamp(snap.Index, 0, graph.Nodes.Count); graph.Nodes.Insert(at, restored); }
                    _selectedNodeIndex = at;
                    MarkDirty(project, graph, isPatch);
                    return true;
                }
                int index = graph.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, snap.NodeId, StringComparison.Ordinal));
                if (index < 0) continue;
                var node = graph.Nodes[index];
                node.LocalAttachmentTransform = CloneTransform(snap.Transform);
                node.HostSocketRef.Path = snap.SocketPath;
                _selectedNodeIndex = index;
                MarkDirty(project, graph, isPatch);
                return true;
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Drawing
        // ═══════════════════════════════════════════════════════════════

        private void DrawViewportToolbar(CharacterVisualDef graph, Rect area, bool ready)
        {
            GUILayout.BeginArea(new Rect(8f, 8f, area.width - 16f, 24f));
            GUILayout.BeginHorizontal();
            _playing = GUILayout.Toggle(_playing, "Play", GUI.skin.button, GUILayout.Width(55));
            GUILayout.Label("Speed", GUILayout.Width(42));
            _speed = GUILayout.HorizontalSlider(_speed, 0f, 3f, GUILayout.Width(70));
            GUILayout.Space(10f);
            _phase = GUILayout.HorizontalSlider(_phase, 0f, 1f);
            GUILayout.Space(10f);
            bool unsaved = _dirtyGraph == graph;
            var prevColor = GUI.color;
            if (unsaved) GUI.color = new Color(1f, .8f, .4f, 1f);
            GUI.enabled = unsaved && _tool == GizmoTool.None;
            if (GUILayout.Button(unsaved ? "Save*" : "Saved", GUILayout.Width(60))) FlushPendingSave();
            GUI.enabled = true;
            GUI.color = prevColor;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(8f, 36f, area.width - 16f, 22f));
            GUILayout.BeginHorizontal();
            _showBones = GUILayout.Toggle(_showBones, "Bones (H)", GUI.skin.button, GUILayout.Width(80));
            _showLinks = GUILayout.Toggle(_showLinks, "Links (C)", GUI.skin.button, GUILayout.Width(75));
            _showSource = GUILayout.Toggle(_showSource, "Source (V)", GUI.skin.button, GUILayout.Width(80));
            _showHelp = GUILayout.Toggle(_showHelp, "Help", GUI.skin.button, GUILayout.Width(50));
            var sel = SelectedNode(graph);
            bool pick = GUILayout.Toggle(_pickBone, "Attach (A)", GUI.skin.button, GUILayout.Width(75));
            if (pick != _pickBone) { ClearModes(); _pickBone = pick && sel != null; }
            bool hide = GUILayout.Toggle(_hideMode, "Hide host part (X)", GUI.skin.button, GUILayout.Width(125));
            if (hide != _hideMode) { ClearModes(); _hideMode = hide; }
            bool layer = GUILayout.Toggle(_layerMode, "Layer vs part (L)", GUI.skin.button, GUILayout.Width(115));
            if (layer != _layerMode) { ClearModes(); _layerMode = layer && sel != null; }
            bool rig = GUILayout.Toggle(_rigMode, "Edit rig (E)", GUI.skin.button, GUILayout.Width(90));
            if (rig != _rigMode) { ClearModes(); _rigMode = rig; }
            if (GUILayout.Button("Frame (F)", GUILayout.Width(75))) _needFrame = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawGizmos(CharacterVisualDef graph, Rect area)
        {
            if (_previewCamera == null) return;
            Vector2 mouse = Event.current.mousePosition;
            var selected = SelectedNode(graph);
            string selectedSocket = selected?.HostSocketRef?.Path;
            int socketIndex = selectedSocket != null && _boneIndex.TryGetValue(selectedSocket, out int si) ? si : -1;

            if (_showBones)
            {
                for (int i = 0; i < _hostBones.Count; i++)
                {
                    var b = _hostBones[i];
                    if (b.T == null) continue;
                    Vector2 p = WorldToGui(b.T.position, area);
                    if (b.ParentIndex >= 0 && _hostBones[b.ParentIndex].T != null)
                        DrawLine(WorldToGui(_hostBones[b.ParentIndex].T.position, area), p, BoneColor, 1.25f);
                }
                for (int i = 0; i < _hostBones.Count; i++)
                {
                    if (_hostBones[i].T == null) continue;
                    DrawDot(WorldToGui(_hostBones[i].T.position, area), 2.5f, BoneColor);
                }
            }

            if (_hideMode) DrawHideOverlay(graph, area);
            else if (_layerMode) DrawLayerOverlay(graph, area);

            _hoverBone = -1;
            bool uiHover = !area.Contains(mouse) || OverUi(mouse, area);
            if (!uiHover && (_tool == GizmoTool.None) && (_showBones || _pickBone || _rigMode))
                _hoverBone = NearestBone(mouse, area, BonePickRadius);
            if (_attachNode >= 0 && area.Contains(mouse)) _hoverBone = NearestBone(mouse, area, BonePickRadius * 1.5f);

            if (socketIndex >= 0 && _hostBones[socketIndex].T != null)
            {
                Vector2 sp = WorldToGui(_hostBones[socketIndex].T.position, area);
                DrawDiamond(sp, 7f, SocketColor);
                DrawLabel(sp + new Vector2(10f, -18f), "graft bone: " + ShortName(_hostBones[socketIndex].Path), SocketColor);
            }

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                var node = graph.Nodes[i];
                var root = LiveRoot(node);
                if (root == null) continue;
                bool isSel = i == _selectedNodeIndex;
                Vector2 c = WorldToGui(root.position, area);
                Rect box = NodeScreenRect(root, area);
                Color col = isSel ? NodeColor : NodeDimColor;
                if (_showLinks && root.parent != null)
                {
                    Vector2 s = WorldToGui(root.parent.position, area);
                    DrawLine(s, c, isSel ? LinkColor : new Color(LinkColor.r, LinkColor.g, LinkColor.b, .35f), isSel ? 2f : 1f);
                    DrawDot(s, 3.5f, isSel ? SocketColor : new Color(SocketColor.r, SocketColor.g, SocketColor.b, .5f));
                }
                DrawRectOutline(box, col, isSel ? 2f : 1f);
                DrawDiamond(c, isSel ? 6f : 4f, col);
                DrawLabel(new Vector2(box.x, box.y - 16f), node.NodeId, col);
                if (isSel)
                {
                    float arm = 46f * WorldPerPixel(area);
                    DrawLine(c, WorldToGui(root.position + root.right.normalized * arm, area), new Color(1f, .35f, .35f, 1f), 2f);
                    DrawLine(c, WorldToGui(root.position + root.up.normalized * arm, area), new Color(.4f, 1f, .4f, 1f), 2f);
                }
            }

            if (_hoverBone >= 0 && _hostBones[_hoverBone].T != null)
            {
                Vector2 hp = WorldToGui(_hostBones[_hoverBone].T.position, area);
                DrawDiamond(hp, 6f, HoverColor);
                var attachNode = _attachNode >= 0 && _attachNode < graph.Nodes.Count ? graph.Nodes[_attachNode] : null;
                bool sameBone = attachNode != null && string.Equals(_hostBones[_hoverBone].Path, attachNode.HostSocketRef?.Path, StringComparison.Ordinal);
                var attachRoot = LiveRoot(attachNode);
                if (attachRoot != null) DrawLine(WorldToGui(attachRoot.position, area), hp, SocketColor, 1.5f);
                DrawLabel(hp + new Vector2(10f, 4f), attachNode != null ? (sameBone ? "already on " : "release to attach to ") + _hostBones[_hoverBone].Path : _hostBones[_hoverBone].Path, HoverColor);
            }

            DrawPartGizmos(graph, area);
            DrawStatus(graph, selected, area);
            if (_showHelp) DrawHelp(area);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Hide host parts
        // ═══════════════════════════════════════════════════════════════

        private bool IsHostHidden(CharacterVisualDef graph, string path)
        {
            if (graph.HostHiddenRenderers == null) return false;
            foreach (var r in graph.HostHiddenRenderers)
                if (r != null && string.Equals(r.Path, path, StringComparison.Ordinal)) return true;
            return false;
        }

        private static Bounds RendererBounds(SpriteRenderer sr)
        {
            var b = sr.bounds;
            // A disabled renderer can report empty bounds; fall back to the sprite's own bounds.
            if (b.size == Vector3.zero && sr.sprite != null)
                b = new Bounds(sr.transform.TransformPoint(sr.sprite.bounds.center), Vector3.Scale(sr.sprite.bounds.size, sr.transform.lossyScale));
            return b;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        private static bool HitsSprite(SpriteRenderer sr, Vector2 world)
        {
            var b = RendererBounds(sr);
            if (world.x < b.min.x || world.x > b.max.x || world.y < b.min.y || world.y > b.max.y) return false;
            // Skinned meshes are deformed at runtime, so their bounds are the best hit area available.
            if (sr.GetComponent<UnityEngine.U2D.Animation.SpriteSkin>() != null) return true;
            var verts = sr.sprite.vertices;
            var tris = sr.sprite.triangles;
            var t = sr.transform;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector2 a = SpriteWorld(sr, t, verts[tris[i]]), bb = SpriteWorld(sr, t, verts[tris[i + 1]]), cc = SpriteWorld(sr, t, verts[tris[i + 2]]);
                if (InTriangle(world, a, bb, cc)) return true;
            }
            return false;
        }

        private static Vector2 SpriteWorld(SpriteRenderer sr, Transform t, Vector2 v)
        {
            if (sr.flipX) v.x = -v.x;
            if (sr.flipY) v.y = -v.y;
            return t.TransformPoint(v);
        }

        /// <summary>Host sprites under the cursor, smallest first. Includes ones already hidden so they can be unhidden.</summary>
        private List<SpriteRenderer> HideCandidates(Vector2 mouse, Rect area, CharacterVisualDef graph)
        {
            var result = new List<SpriteRenderer>();
            if (_previewHost == null || _previewCamera == null) return result;
            Vector2 world = GuiToWorld(mouse, area);
            foreach (var sr in _previewHost.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null || !_hostPathByT.TryGetValue(sr.transform, out var path)) continue;
                if (!sr.gameObject.activeInHierarchy) continue;
                if (!sr.enabled && !IsHostHidden(graph, path)) continue;
                if (HitsSprite(sr, world)) result.Add(sr);
            }
            result.Sort((x, y) =>
            {
                var bx = RendererBounds(x).size; var by = RendererBounds(y).size;
                return (bx.x * bx.y).CompareTo(by.x * by.y);
            });
            return result;
        }

        private SpriteRenderer HideTarget(Vector2 mouse, Rect area, CharacterVisualDef graph, out int index, out int count)
        {
            if ((mouse - _hideCycleAt).magnitude > 6f) { _hideCycleAt = mouse; _hideCycle = 0; }
            var list = HideCandidates(mouse, area, graph);
            count = list.Count;
            index = count == 0 ? -1 : ((_hideCycle % count) + count) % count;
            return index < 0 ? null : list[index];
        }

        private void ToggleHostHidden(ModProject project, CharacterVisualDef graph, bool isPatch, SpriteRenderer sr)
        {
            if (sr == null || !_hostPathByT.TryGetValue(sr.transform, out var path)) return;
            if (graph.HostHiddenRenderers == null) graph.HostHiddenRenderers = new List<CanonicalTransformRef>();
            int at = graph.HostHiddenRenderers.FindIndex(r => r != null && string.Equals(r.Path, path, StringComparison.Ordinal));
            if (at >= 0) graph.HostHiddenRenderers.RemoveAt(at);
            else
            {
                graph.HostHiddenRenderers.RemoveAll(r => r == null || string.IsNullOrEmpty(r.Path));
                graph.HostHiddenRenderers.Add(new CanonicalTransformRef { Path = path });
            }
            MarkDirty(project, graph, isPatch);
        }

        private Rect RendererScreenRect(SpriteRenderer sr, Rect area)
        {
            var b = RendererBounds(sr);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, 0f));
                Vector2 s = WorldToGui(corner, area);
                min = Vector2.Min(min, s); max = Vector2.Max(max, s);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void DrawHideOverlay(CharacterVisualDef graph, Rect area)
        {
            if (graph.HostHiddenRenderers != null)
                foreach (var h in graph.HostHiddenRenderers)
                {
                    if (h == null || string.IsNullOrEmpty(h.Path) || !_boneIndex.TryGetValue(h.Path, out int bi)) continue;
                    var sr = _hostBones[bi].T != null ? _hostBones[bi].T.GetComponent<SpriteRenderer>() : null;
                    if (sr == null || sr.sprite == null) continue;
                    Rect r = RendererScreenRect(sr, area);
                    DrawRectOutline(r, HiddenColor, 1.5f);
                    DrawLabel(new Vector2(r.x, r.y - 15f), "hidden: " + ShortName(h.Path), HiddenColor);
                }
            Vector2 mouse = Event.current.mousePosition;
            if (!area.Contains(mouse) || OverUi(mouse, area) || _tool != GizmoTool.None) return;
            var target = HideTarget(mouse, area, graph, out int index, out int count);
            if (target == null) return;
            Rect tr = RendererScreenRect(target, area);
            DrawRectOutline(tr, HoverColor, 2f);
            _hostPathByT.TryGetValue(target.transform, out var tp);
            string verb = IsHostHidden(graph, tp) ? "unhide" : "hide";
            DrawLabel(new Vector2(mouse.x + 12f, mouse.y + 14f), "click: " + verb + " " + ShortName(tp) + (count > 1 ? "  (" + (index + 1) + "/" + count + ", Tab = next)" : ""), HoverColor);
        }

        private void DrawLayerOverlay(CharacterVisualDef graph, Rect area)
        {
            var sel = SelectedNode(graph);
            var policy = sel?.SortingPolicy;
            if (policy != null && !string.IsNullOrEmpty(policy.ReferencePath) && _boneIndex.TryGetValue(policy.ReferencePath, out int bi) && _hostBones[bi].T != null)
            {
                var sr = _hostBones[bi].T.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    Rect r = RendererScreenRect(sr, area);
                    DrawRectOutline(r, HiddenColor, 1.5f);
                    DrawLabel(new Vector2(r.x, r.y - 15f), (policy.Placement == VisualPlacement.BehindPart ? "behind " : "in front of ") + ShortName(policy.ReferencePath), HiddenColor);
                }
            }
            Vector2 mouse = Event.current.mousePosition;
            if (!area.Contains(mouse) || OverUi(mouse, area) || _tool != GizmoTool.None) return;
            var target = HideTarget(mouse, area, graph, out int index, out int count);
            if (target == null) return;
            DrawRectOutline(RendererScreenRect(target, area), HoverColor, 2f);
            _hostPathByT.TryGetValue(target.transform, out var tp);
            DrawLabel(new Vector2(mouse.x + 12f, mouse.y + 14f), "click: in front of " + ShortName(tp) + "   shift+click: behind" + (count > 1 ? "  (" + (index + 1) + "/" + count + ", Tab = next)" : ""), HoverColor);
        }

        private void DrawStatus(CharacterVisualDef graph, VisualNodeDef selected, Rect area)
        {
            string text;
            if (_tool != GizmoTool.None)
            {
                string axis = _axis == GizmoAxis.Free ? "" : "  [" + _axis + " locked]";
                text = (_tool == GizmoTool.Drag ? "Dragging" : _tool.ToString()) + axis + "  -  Enter/LMB confirm, Esc/RMB cancel, X/Y lock, Ctrl snap" + (_toolPart != null ? ", Shift fine" : ", Shift fine");
            }
            else if (_rigMode || (_partFocus && ActivePart(graph) != null)) text = RigStatus(graph);
            else if (_hideMode) text = "Hide host part: click a host sprite to hide/unhide it (Tab = next overlapping part, Esc/RMB exits). Red = hidden.";
            else if (_layerMode) text = "Layer '" + (selected?.NodeId ?? "?") + "': click a host part to draw it in front of that part, Shift+click for behind (Tab = next overlapping part, Esc/RMB exits).";
            else if (_pickBone) text = "Attach: drag '" + (selected?.NodeId ?? "?") + "' (or any node) onto a bone, or click it, to change only its attach point (Esc/RMB exits)";
            else if (!GetCompileResult(graph).Success) text = "Graft base settings are invalid - showing host only. See the side panel.";
            else if (graph.Nodes.Count == 0) text = "No nodes yet - pick a model in the \"Graft from\" box (top right), then drag a part onto the rig. Drag blue bone dots to pose the host.";
            else if (selected != null && LiveRoot(selected) == null && GetCompileResult(graph).Diagnostics.Any(d => d.Severity == VisualGraphDiagnosticSeverity.Error && string.Equals(d.NodeId, selected.NodeId, StringComparison.OrdinalIgnoreCase)))
                text = "Node '" + selected.NodeId + "' has errors and is skipped - see the side panel. Other nodes still render.";
            else if (selected != null && LiveRoot(selected) == null && selected.RendererRefs != null && selected.RendererRefs.Count == 0)
                text = "Node '" + selected.NodeId + "' has nothing grafted - click a part in the \"Graft from\" inset.";
            else if (selected != null && LiveRoot(selected) != null)
            {
                var t = selected.LocalAttachmentTransform;
                text = $"{selected.NodeId}   pos ({t.PositionX:0.###}, {t.PositionY:0.###})   rot {t.Rotation:0.#}   scale ({t.ScaleX:0.###}, {t.ScaleY:0.###})";
            }
            else return;
            DrawLabel(new Vector2(12f, 62f), text, Color.white);
        }

        private void DrawHelp(Rect area)
        {
            Rect r = HelpRect(area);
            DrawRect(r, new Color(0f, 0f, 0f, .6f));
            string[] lines =
            {
                "Drag node          move",
                "Drag a bone dot    pose the host (G/R/S, arrows; Alt+click",
                "  picks a bone under a graft; Esc/RMB deselects)",
                "G / R / S          move / rotate / scale",
                "  X / Y lock axis, Ctrl snap, Shift fine",
                "  Enter/LMB ok, Esc/RMB cancel",
                "A  attach mode: drag a node onto a bone to re-attach it",
                "   (position stays put; plain drag never changes the bone)",
                "X  hide/unhide host sprites (Tab cycles)",
                "L  layer node vs a host part (Shift = behind)",
                "E  edit the host rig: click a bone/sprite, drag, G/R/S",
                "D duplicate   Arrows nudge   Ctrl+Z undo   Ctrl+Y / Ctrl+Shift+Z redo",
                "Wheel zoom   RMB/MMB drag pan   F frame",
                "H bones   C links   V source   Space play",
                "Source box: pick any model from its dropdown,",
                "  drag a part onto the rig = new node on nearest bone",
                "  click = toggle part on selected node",
                "  (Shift+drag = whole piece, Esc cancels)",
                "Edits stay unsaved until Save* (top) / Ctrl+S",
            };
            for (int i = 0; i < lines.Length; i++) DrawLabel(new Vector2(r.x + 6f, r.y + 4f + i * 17f), lines[i], new Color(.85f, .9f, 1f, 1f));
        }

        private static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int i = path.LastIndexOf('/');
            return i >= 0 && i < path.Length - 1 ? path.Substring(i + 1) : path;
        }

        // ── Primitive drawing (Repaint only) ─────────────────────────────

        private void DrawLabel(Vector2 pos, string text, Color color)
        {
            if (_gizmoLabel == null)
                _gizmoLabel = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = false, wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0) };
            Vector2 size = _gizmoLabel.CalcSize(new GUIContent(text));
            var rect = new Rect(pos.x, pos.y, size.x + 2f, size.y);
            var old = GUI.color;
            _gizmoLabel.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _gizmoLabel);
            _gizmoLabel.normal.textColor = color;
            GUI.Label(rect, text, _gizmoLabel);
            GUI.color = old;
        }

        private static void DrawRect(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void DrawRectOutline(Rect r, Color c, float w)
        {
            DrawRect(new Rect(r.x, r.y, r.width, w), c);
            DrawRect(new Rect(r.x, r.yMax - w, r.width, w), c);
            DrawRect(new Rect(r.x, r.y, w, r.height), c);
            DrawRect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        private static void DrawDot(Vector2 c, float radius, Color col)
        {
            DrawRect(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), col);
        }

        private static void DrawDiamond(Vector2 c, float radius, Color col)
        {
            var oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            DrawRect(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), col);
            GUI.matrix = oldMatrix;
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color col, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < .5f) return;
            var oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            DrawRect(new Rect(a.x, a.y - width * .5f, len, width), col);
            GUI.matrix = oldMatrix;
        }
    }
}
