using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// "Graft From" inset: shows the source model of the selected node with the grafted renderers
    /// highlighted. Clicking a part toggles it in the node's RendererRefs.
    /// </summary>
    public sealed partial class VisualGraphEditor
    {
        private sealed class SourcePart { public string Path; public SpriteRenderer R; public Color Color; }

        // Unique stage position (see PreviewOrigin): every preview camera renders all layers.
        private static readonly Vector3 SourceOrigin = new Vector3(-6000f, -10000f, 0f);
        private const float SourceSize = 300f;

        private GameObject _srcHost;
        private Camera _srcCamera;
        private RenderTexture _srcTexture;
        private AnimationClip _srcClip;
        private BoneReferenceCatalog _srcCatalog;
        private string _srcKey, _srcFailKey;
        private readonly List<SourcePart> _srcParts = new();
        private bool _showSource = true, _srcVisible;
        private int _srcHover = -1;

        // The inset's model is picked in the viewport, independent of any node. Null follows the selected node's source.
        private string _srcPickId;
        private VisualNodeDef _srcFollowNode;
        private bool _srcPickerOpen, _srcFocusFilter;
        private string _srcFilter = "";
        private Vector2 _srcPickScroll;
        private const float SourceHeaderHeight = 20f;
        private const float SourceRowHeight = 20f;

        private static Rect SourceRect(Rect area) { return new Rect(area.width - SourceSize - 8f, 64f, SourceSize, SourceSize + SourceHeaderHeight); }
        private static Rect SourceImageRect(Rect area) { Rect r = SourceRect(area); return new Rect(r.x, r.y + SourceHeaderHeight, SourceSize, SourceSize); }

        /// <summary>The model the inset shows and drag-drops from: an explicit pick, else the selected node's source.</summary>
        private string ActiveSourceId(VisualNodeDef node)
        {
            return !string.IsNullOrEmpty(_srcPickId) ? _srcPickId : node?.SourcePrefabRef;
        }

        private string _srcFollowRef;

        /// <summary>Selecting another node, or changing that node's source model, makes the inset follow it again.</summary>
        private void SyncSourceFollow(VisualNodeDef selected)
        {
            string reference = selected?.SourcePrefabRef;
            if (ReferenceEquals(selected, _srcFollowNode) && string.Equals(reference, _srcFollowRef, StringComparison.Ordinal)) return;
            _srcFollowNode = selected;
            _srcFollowRef = reference;
            if (!string.IsNullOrEmpty(reference)) _srcPickId = null;
        }

        private bool EnsureSource(string id)
        {
            if (string.IsNullOrEmpty(id)) { DestroySource(); return false; }
            if (_srcHost != null && _srcKey == id) return true;
            if (_srcFailKey == id) return false;
            DestroySource();
            var prefab = ResolvePrefab(id);
            if (prefab == null) { _srcFailKey = id; return false; }
            _srcKey = id;
            _srcHost = UnityEngine.Object.Instantiate(prefab, SourceOrigin, Quaternion.identity);
            _srcHost.SetActive(true);
            var animator = _srcHost.GetComponentInChildren<Animator>();
            var srcClips = animator?.runtimeAnimatorController?.animationClips?.Where(x => x != null).ToList();
            _srcClip = srcClips?.FirstOrDefault(x => x.name == PreviewPose.DefaultClip(srcClips.Select(c => c.name).ToList())) ?? srcClips?.FirstOrDefault();
            PreviewPose.DisableAnimators(_srcHost);
            _srcCatalog = BoneReferenceCatalog.Build(_srcHost.transform);
            _srcParts.Clear();
            foreach (var entry in _srcCatalog.GetCanonicalEntries())
            {
                var sr = entry.Value != null ? entry.Value.GetComponent<SpriteRenderer>() : null;
                if (sr != null) _srcParts.Add(new SourcePart { Path = entry.Key, R = sr, Color = sr.color });
            }
            EnsureSourceCamera();
            FrameSource();
            return true;
        }

        private void EnsureSourceCamera()
        {
            if (_srcCamera == null)
            {
                var go = new GameObject("[VisualGraphSourceCamera]");
                go.hideFlags = HideFlags.HideAndDontSave;
                _srcCamera = go.AddComponent<Camera>();
                _srcCamera.orthographic = true;
                _srcCamera.enabled = false;
                _srcCamera.backgroundColor = new Color(.1f, .1f, .13f, 1f);
                _srcCamera.clearFlags = CameraClearFlags.SolidColor;
            }
            if (_srcTexture == null)
            {
                _srcTexture = new RenderTexture(384, 384, 16);
                _srcTexture.Create();
                _srcCamera.targetTexture = _srcTexture;
            }
        }

        private void FrameSource()
        {
            if (_srcHost == null || _srcCamera == null) return;
            var renderers = _srcHost.GetComponentsInChildren<Renderer>();
            Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(_srcHost.transform.position, Vector3.one * 2f);
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            _srcCamera.transform.position = new Vector3(b.center.x, b.center.y, b.center.z - 10f);
            _srcCamera.orthographicSize = Mathf.Max(.25f, Mathf.Max(b.extents.x, b.extents.y) * 1.15f);
        }

        private void DestroySource()
        {
            if (_srcHost != null) UnityEngine.Object.DestroyImmediate(_srcHost);
            _srcHost = null; _srcClip = null; _srcCatalog = null; _srcKey = null; _srcFailKey = null; _srcHover = -1;
            _srcParts.Clear();
        }

        private void DestroySourceCamera()
        {
            DestroySource();
            if (_srcTexture != null) { _srcTexture.Release(); UnityEngine.Object.DestroyImmediate(_srcTexture); _srcTexture = null; }
            if (_srcCamera != null) UnityEngine.Object.DestroyImmediate(_srcCamera.gameObject);
            _srcCamera = null;
        }

        private Vector2 SrcWorldToGui(Vector3 world, Rect img)
        {
            Vector3 v = _srcCamera.WorldToViewportPoint(world);
            return new Vector2(img.x + v.x * img.width, img.y + (1f - v.y) * img.height);
        }

        private Vector3 SrcGuiToWorld(Vector2 p, Rect img)
        {
            return _srcCamera.ViewportToWorldPoint(new Vector3((p.x - img.x) / img.width, 1f - (p.y - img.y) / img.height, 10f));
        }

        private Rect SrcBoundsRect(Bounds b, Rect img)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, 0f));
                Vector2 s = SrcWorldToGui(c, img);
                min = Vector2.Min(min, s); max = Vector2.Max(max, s);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static bool Selectable(SourcePart p) { return p.R != null && p.R.sprite != null && p.R.enabled && p.R.gameObject.activeInHierarchy; }

        private HashSet<string> SelectedSourcePaths(VisualNodeDef node)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (node == null || !string.Equals(node.SourcePrefabRef, ActiveSourceId(node), StringComparison.Ordinal)) return set;
            if (node.RendererRefs != null) foreach (var r in node.RendererRefs) if (r != null && r.Path != null) set.Add(r.Path);
            return set;
        }

        private int PickSourcePart(Vector2 mouse, Rect img)
        {
            Vector3 w = SrcGuiToWorld(mouse, img);
            int best = -1;
            int bestOrder = int.MinValue;
            float bestArea = float.MaxValue;
            for (int i = 0; i < _srcParts.Count; i++)
            {
                var p = _srcParts[i];
                if (!Selectable(p)) continue;
                Bounds b = p.R.bounds;
                if (!b.Contains(new Vector3(w.x, w.y, b.center.z))) continue;
                float area = b.size.x * b.size.y;
                int order = p.R.sortingOrder;
                if (order > bestOrder || (order == bestOrder && area < bestArea)) { best = i; bestOrder = order; bestArea = area; }
            }
            return best;
        }

        /// <summary>Handles clicks, zoom and pan inside the source inset, consuming those events.</summary>
        private void HandleSourceInput(ModProject project, CharacterVisualDef graph, bool isPatch, Rect area)
        {
            if (_srcPressPath != null && HandleSourceDrag(project, graph, isPatch, area)) return;
            if (!_srcVisible || !_showSource) return;
            Event e = Event.current;
            Rect box = SourceRect(area), img = SourceImageRect(area);
            if (!box.Contains(e.mousePosition)) return;
            // The header controls and the open picker are plain IMGUI controls (DrawSourceHeader) and need these events.
            if (_srcPickerOpen || e.mousePosition.y < img.y) return;
            if (_srcHost == null || _srcCamera == null)
            {
                if (e.type == EventType.MouseDown || e.type == EventType.ScrollWheel) e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && img.Contains(e.mousePosition))
            {
                GUIUtility.keyboardControl = 0;
                int hit = PickSourcePart(e.mousePosition, img);
                // Toggle on release so the same press can instead become a drag onto the rig.
                if (hit >= 0) { _srcPressPath = _srcParts[hit].Path; _srcPressPos = e.mousePosition; _srcPressShift = e.shift; _srcDragging = false; }
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && img.Contains(e.mousePosition))
            {
                Vector3 before = SrcGuiToWorld(e.mousePosition, img);
                _srcCamera.orthographicSize = Mathf.Clamp(_srcCamera.orthographicSize * (1f + e.delta.y * .08f), .05f, 200f);
                _srcCamera.transform.position += before - SrcGuiToWorld(e.mousePosition, img);
                e.Use();
            }
            else if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            {
                if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2)) _srcCamera.transform.position += new Vector3(-e.delta.x, e.delta.y, 0f) * (2f * _srcCamera.orthographicSize / SourceSize);
                e.Use();
            }
        }

        private void DrawSourceInset(VisualNodeDef node, Rect area)
        {
            if (!_showSource || !_srcVisible) return;
            Rect box = SourceRect(area), img = SourceImageRect(area);
            DrawRect(box, new Color(0f, 0f, 0f, .65f));
            if (_srcHost == null || _srcCamera == null)
            {
                string msg = !string.IsNullOrEmpty(_srcFailKey) ? "Could not load '" + _srcFailKey + "'.\nPick another model above." : "Pick a model above, then drag\nits parts onto the rig.";
                GUI.Label(img, "<color=#b8c4d8>" + msg + "</color>", EditorStyles.RichLabel);
                return;
            }

            var selected = SelectedSourcePaths(node);
            bool follows = node != null && string.Equals(node.SourcePrefabRef, _srcKey, StringComparison.Ordinal);
            bool canToggle = node != null && (follows || node.RendererRefs == null || node.RendererRefs.Count == 0);
            bool any = selected.Count > 0;
            foreach (var p in _srcParts)
            {
                if (p.R == null) continue;
                bool on = !any || selected.Contains(p.Path);
                p.R.color = on ? p.Color : new Color(p.Color.r * .45f, p.Color.g * .45f, p.Color.b * .45f, p.Color.a * .25f);
            }
            if (_srcClip != null)
            {
                try { _srcClip.SampleAnimation(_srcHost, AnimationPlaybackResolver.ToSampleTime(_srcClip, _phase)); PreviewPose.RefreshSkins(); } catch { }
            }
            _srcCamera.Render();
            GUI.DrawTexture(img, _srcTexture, ScaleMode.StretchToFill, false);
            foreach (var p in _srcParts) if (p.R != null) p.R.color = p.Color;

            Vector2 mouse = Event.current.mousePosition;
            _srcHover = img.Contains(mouse) ? PickSourcePart(mouse, img) : -1;
            GUI.BeginGroup(img);
            // Overlays below are drawn in img-local coordinates.
            for (int i = 0; i < _srcParts.Count; i++)
            {
                var p = _srcParts[i];
                if (!Selectable(p)) continue;
                bool isSel = selected.Contains(p.Path);
                if (!isSel && i != _srcHover) continue;
                Rect r = SrcBoundsRect(p.R.bounds, img);
                r.x -= img.x; r.y -= img.y;
                DrawRectOutline(r, i == _srcHover ? HoverColor : NodeColor, isSel ? 2f : 1f);
            }
            Transform tr = null;
            if (follows && _srcCatalog.TryResolveCanonical(node.SourceSubtreeRef?.Path, out tr) && tr != null && !string.Equals(node.SourceSubtreeRef.Path, BoneReferenceCatalog.RootReference, StringComparison.Ordinal))
            {
                Vector2 s = SrcWorldToGui(tr.position, img) - img.position;
                DrawDiamond(s, 5f, new Color(.4f, .7f, 1f, 1f));
                DrawLabel(s + new Vector2(8f, -14f), "source root", new Color(.4f, .7f, 1f, 1f));
            }
            if (follows && node.Pivot == VisualPivotMode.SpriteCenter)
            {
                bool anyPart = false;
                Bounds union = default;
                for (int i = 0; i < _srcParts.Count; i++)
                {
                    var p = _srcParts[i];
                    if (!Selectable(p) || !selected.Contains(p.Path)) continue;
                    if (!anyPart) { union = p.R.bounds; anyPart = true; } else union.Encapsulate(p.R.bounds);
                }
                if (anyPart)
                {
                    Vector2 s = SrcWorldToGui(union.center, img) - img.position;
                    DrawDiamond(s, 5f, SocketColor);
                    DrawLabel(s + new Vector2(8f, 2f), "pivot (centre)", SocketColor);
                }
            }
            else if (follows && !string.IsNullOrEmpty(node.SourceAnchorRef?.Path) && _srcCatalog.TryResolveCanonical(node.SourceAnchorRef.Path, out tr) && tr != null)
            {
                Vector2 s = SrcWorldToGui(tr.position, img) - img.position;
                DrawDiamond(s, 5f, SocketColor);
                DrawLabel(s + new Vector2(8f, 2f), "anchor (pivot)", SocketColor);
            }
            if (_srcHover >= 0)
            {
                string path = _srcParts[_srcHover].Path;
                string hint = canToggle ? (selected.Contains(path) ? "click: remove  " : "click: graft  ") + ShortName(path) + "  |  drag onto rig" : ShortName(path) + "  |  drag onto rig";
                DrawLabel(new Vector2(4f, img.height - 18f), hint, HoverColor);
            }
            GUI.EndGroup();
        }

        private void RefreshSourceVisibility(VisualNodeDef node)
        {
            SyncSourceFollow(node);
            _srcVisible = _showSource;
            if (!_srcVisible) { DestroySource(); _srcPickerOpen = false; return; }
            EnsureSource(ActiveSourceId(node));
        }

        /// <summary>The inset's controls: model picker, keep-size toggle and the picker list. Runs on every event type so the buttons receive clicks.</summary>
        private void DrawSourceHeader(VisualNodeDef node, Rect area)
        {
            if (!_srcVisible || !_showSource) return;
            Rect box = SourceRect(area);
            string id = ActiveSourceId(node);
            int count = SelectedSourcePaths(node).Count;
            string label = "Graft from: " + (string.IsNullOrEmpty(id) ? "(click to pick a model)" : id + (count > 0 ? " (" + count + ")" : "")) + " \u25BE";
            if (GUI.Button(new Rect(box.x + 2f, box.y + 2f, box.width - 104f, 16f), new GUIContent(label, "Choose which model to graft parts from. Independent of the selected node."), EditorStyles.MiniButton))
            {
                _srcPickerOpen = !_srcPickerOpen;
                _srcFocusFilter = _srcPickerOpen;
                _srcFilter = "";
                ClearSrcPress();
            }
            _dropKeepSize = GUI.Toggle(new Rect(box.xMax - 98f, box.y + 2f, 96f, 16f), _dropKeepSize, _dropKeepSize ? "keep size: on" : "keep size: off", EditorStyles.MiniButton);
            if (_srcPickerOpen) DrawSourcePicker(node, box, area);
        }

        private const float PickCellW = 100f, PickThumb = 96f, PickLabelH = 14f, PickCellH = PickThumb + PickLabelH + 4f;
        private static GUIStyle _pickLabelStyle;

        private static Rect PickerPanelRect(Rect box, Rect area)
        {
            float panelW = Mathf.Min(468f, area.width - 16f);
            float panelH = Mathf.Max(SourceSize, Mathf.Min(540f, area.height - (box.y + SourceHeaderHeight) - 8f));
            return new Rect(box.xMax - panelW, box.y + SourceHeaderHeight, panelW, panelH);
        }

        /// <summary>A larger panel than the inset, anchored to its right edge: a filterable, scrolling grid of rendered models.</summary>
        private void DrawSourcePicker(VisualNodeDef node, Rect box, Rect area)
        {
            Rect panel = PickerPanelRect(box, area);
            if (Event.current.type == EventType.Repaint) DrawRect(panel, new Color(.07f, .07f, .1f, 1f));
            _pickLabelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };

            GUI.SetNextControlName("srcfilter");
            _srcFilter = GUI.TextField(new Rect(panel.x + 4f, panel.y + 4f, panel.width - 92f, 20f), _srcFilter ?? "");
            if (_srcFocusFilter && Event.current.type == EventType.Repaint) { GUI.FocusControl("srcfilter"); _srcFocusFilter = false; }
            bool canFollow = _srcPickId != null && node != null && !string.IsNullOrEmpty(node.SourcePrefabRef);
            GUI.enabled = canFollow;
            if (GUI.Button(new Rect(panel.xMax - 84f, panel.y + 4f, 80f, 20f), new GUIContent("Follow node", "Show the selected node's own source model again"), EditorStyles.MiniButton))
            {
                _srcPickId = null; _srcPickerOpen = false; GUIUtility.keyboardControl = 0;
            }
            GUI.enabled = true;

            var ids = SourceIds();
            string filter = (_srcFilter ?? "").Trim();
            var shown = filter.Length == 0 ? ids : ids.Where(x => x.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Rect list = new Rect(panel.x + 4f, panel.y + 30f, panel.width - 8f, panel.height - 52f);
            float inner = list.width - 18f;
            int cols = Mathf.Max(1, Mathf.FloorToInt(inner / PickCellW));
            int rows = Mathf.CeilToInt(shown.Count / (float)cols);
            Rect content = new Rect(0f, 0f, inner, Mathf.Max(list.height, rows * PickCellH));
            _srcPickScroll = GUI.BeginScrollView(list, _srcPickScroll, content);
            string active = ActiveSourceId(node);
            string hovered = null;
            int firstRow = Mathf.Max(0, (int)(_srcPickScroll.y / PickCellH));
            int lastRow = Mathf.Min(rows, firstRow + (int)(list.height / PickCellH) + 2);
            float startX = Mathf.Max(0f, (inner - cols * PickCellW) * 0.5f);
            for (int row = firstRow; row < lastRow; row++)
                for (int col = 0; col < cols; col++)
                {
                    int i = row * cols + col;
                    if (i >= shown.Count) break;
                    string id = shown[i];
                    var cell = new Rect(startX + col * PickCellW, row * PickCellH, PickCellW - 2f, PickCellH - 2f);
                    var thumbRect = new Rect(cell.x + 1f, cell.y, PickThumb, PickThumb);
                    bool isActive = string.Equals(id, active, StringComparison.Ordinal);
                    bool hover = cell.Contains(Event.current.mousePosition);
                    if (hover) hovered = id;
                    if (Event.current.type == EventType.Repaint)
                        DrawRect(thumbRect, isActive ? new Color(.18f, .32f, .5f, 1f) : hover ? new Color(.2f, .2f, .26f, 1f) : new Color(.12f, .12f, .15f, 1f));
                    var tex = RigThumbnailCache.Get(id, out bool pending);
                    if (tex != null) GUI.DrawTexture(thumbRect, tex, ScaleMode.ScaleToFit, true);
                    else if (Event.current.type == EventType.Repaint) GUI.Label(thumbRect, pending ? "..." : "no model", _pickLabelStyle);
                    GUI.Label(new Rect(cell.x, cell.y + PickThumb, PickCellW - 2f, PickLabelH), (isActive ? "\u2713 " : "") + id, _pickLabelStyle);
                    if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                    {
                        _srcPickId = id;
                        _srcPickerOpen = false;
                        GUIUtility.keyboardControl = 0;
                    }
                }
            GUI.EndScrollView();
            if (shown.Count == 0) DrawLabel(new Vector2(panel.x + 10f, panel.y + 40f), "No models match", new Color(.8f, .8f, .8f, 1f));
            DrawLabel(new Vector2(panel.x + 6f, panel.yMax - 18f), hovered ?? (shown.Count + " models"), new Color(.8f, .8f, .8f, 1f));
        }
    }
}
