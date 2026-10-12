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
    /// Host rig editing: move, rotate, scale, tint and add effects to the base model's own bones and sprites (HostParts),
    /// the same way grafted nodes can be edited. Offsets sit on top of the animation, so they follow it.
    /// </summary>
    public sealed partial class VisualGraphEditor
    {
        private bool _rigMode;
        private string _selectedPartPath;
        private HostPartDef _toolPart, _toolPartStart;
        private ResolvedHostPart _toolLive;
        private Transform _toolBone;

        private static readonly Color PartColor = new Color(.85f, .5f, 1f, .95f);
        private static readonly Color PartDimColor = new Color(.85f, .5f, 1f, .5f);

        private static HostPartDef FindPart(CharacterVisualDef graph, string path)
        {
            if (graph.HostParts == null || string.IsNullOrEmpty(path)) return null;
            return graph.HostParts.Find(p => p != null && p.HostRef != null && string.Equals(p.HostRef.Path, path, StringComparison.Ordinal));
        }

        private static HostPartDef EnsurePartDef(CharacterVisualDef graph, string path)
        {
            var part = FindPart(graph, path);
            if (part != null) return part;
            if (graph.HostParts == null) graph.HostParts = new List<HostPartDef>();
            part = new HostPartDef { HostRef = new CanonicalTransformRef { Path = path } };
            graph.HostParts.Add(part);
            return part;
        }

        // A host bone picked directly in the viewport (no rig mode): it stays the active part until another node is selected or Esc.
        private bool _partFocus;
        private int _partFocusNode = -1;

        private bool PartMode => _rigMode || (_partFocus && _selectedNodeIndex == _partFocusNode);

        private HostPartDef ActivePart(CharacterVisualDef graph) { return PartMode ? FindPart(graph, _selectedPartPath) : null; }

        private Transform PartBone(string path)
        {
            return path != null && _boneIndex.TryGetValue(path, out int i) && _hostBones[i].T != null ? _hostBones[i].T : null;
        }

        private static HostPartDef ClonePartTransform(HostPartDef p)
        {
            return new HostPartDef { HostRef = new CanonicalTransformRef { Path = p.HostRef.Path }, PositionX = p.PositionX, PositionY = p.PositionY, Rotation = p.Rotation, ScaleX = p.ScaleX, ScaleY = p.ScaleY };
        }

        private static void CopyPartTransform(HostPartDef from, HostPartDef to)
        {
            to.PositionX = from.PositionX; to.PositionY = from.PositionY; to.Rotation = from.Rotation; to.ScaleX = from.ScaleX; to.ScaleY = from.ScaleY;
        }

        private static void SyncLive(HostPartDef def, ResolvedHostPart live)
        {
            if (live == null) return;
            live.Position = new Vector2(def.PositionX, def.PositionY);
            live.Rotation = def.Rotation;
            live.Scale = new Vector2(def.ScaleX, def.ScaleY);
        }

        private void ClearModes() { _pickBone = false; _hideMode = false; _layerMode = false; _rigMode = false; _partFocus = false; _attachNode = -1; }

        private bool PruneNeutralParts(CharacterVisualDef graph)
        {
            if (graph.HostParts == null) return false;
            return graph.HostParts.RemoveAll(p => p == null || (p.IsNeutral() && !string.Equals(p.HostRef?.Path, _selectedPartPath, StringComparison.Ordinal))) > 0;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Picking and tools
        // ═══════════════════════════════════════════════════════════════

        private void RigClick(Vector2 m, Rect area, ModProject project, CharacterVisualDef graph, bool isPatch)
        {
            int bi = NearestBone(m, area, BonePickRadius);
            string path = bi >= 0 ? _hostBones[bi].Path : null;
            if (path == null)
            {
                var target = HideTarget(m, area, graph, out _, out _);
                if (target != null) _hostPathByT.TryGetValue(target.transform, out path);
            }
            _selectedPartPath = path;
            if (path == null) return;
            bool created = FindPart(graph, path) == null;
            var def = EnsurePartDef(graph, path);
            if (created) MarkDirtyLive(project, graph, isPatch);
            BeginPartTool(GizmoTool.Drag, m, area, project, graph, isPatch, def);
        }

        private bool BoneClick(Vector2 m, Rect area, ModProject project, CharacterVisualDef graph, bool isPatch)
        {
            if (!_showBones) return false;
            int bi = NearestBone(m, area, BonePickRadius);
            if (bi < 0) return false;
            string path = _hostBones[bi].Path;
            _selectedPartPath = path;
            _partFocus = true; _partFocusNode = _selectedNodeIndex;
            BeginPartTool(GizmoTool.Drag, m, area, project, graph, isPatch, EnsurePartDef(graph, path));
            return true;
        }

        private void BeginPartTool(GizmoTool tool, Vector2 mouse, Rect area, ModProject project, CharacterVisualDef graph, bool isPatch, HostPartDef def)
        {
            var bone = PartBone(def.HostRef.Path);
            if (bone == null || _previewHost == null) return;
            var live = VisualGraphHostParts.Ensure(_previewHost.transform, bone);
            if (live == null) return;
            _tool = tool; _axis = GizmoAxis.Free;
            _toolProject = project; _toolGraph = graph; _toolIsPatch = isPatch;
            _toolNode = null; _toolRoot = null;
            _toolPart = def; _toolPartStart = ClonePartTransform(def); _toolBone = bone; _toolLive = live;
            _toolStartMouse = mouse;
            _toolStartMouseWorld = GuiToWorld(mouse, area);
            _toolStartNodeWorld = bone.position;
            _toolCenterWorld = bone.position;
            _toolLastAngle = Mathf.Atan2(_toolStartMouseWorld.y - _toolCenterWorld.y, _toolStartMouseWorld.x - _toolCenterWorld.x) * Mathf.Rad2Deg;
            _toolAngleAccum = 0f;
            _toolStartPixelDistance = Mathf.Max((mouse - WorldToGui(bone.position, area)).magnitude, 20f);
            _toolMoved = false;
        }

        /// <summary>G/R/S in rig mode. Returns true when rig mode owns the key (even with nothing selected) so it never falls through to a node.</summary>
        private bool TryBeginPartTool(GizmoTool tool, Vector2 mouse, Rect area, ModProject project, CharacterVisualDef graph, bool isPatch)
        {
            if (!PartMode) return false;
            var def = ActivePart(graph);
            if (def != null) BeginPartTool(tool, mouse, area, project, graph, isPatch, def);
            return true;
        }

        private void UpdatePartTool(Vector2 mouse, Event e, Rect area)
        {
            var def = _toolPart; var bone = _toolBone; var live = _toolLive;
            if (def == null || bone == null || live == null) { _tool = GizmoTool.None; _toolPart = null; return; }
            if (_tool == GizmoTool.Drag && !_toolMoved && (mouse - _toolStartMouse).magnitude < DragThreshold) return;

            float fine = e.shift && _tool != GizmoTool.Drag ? .1f : 1f;
            bool snap = e.control;
            Vector3 mouseWorld = GuiToWorld(mouse, area);
            Transform parent = bone.parent;
            var start = _toolPartStart;

            switch (_tool)
            {
                case GizmoTool.Drag:
                case GizmoTool.Move:
                {
                    Vector3 delta = (mouseWorld - _toolStartMouseWorld) * fine;
                    if (_axis == GizmoAxis.X) delta.y = 0f;
                    if (_axis == GizmoAxis.Y) delta.x = 0f;
                    Vector3 target = _toolStartNodeWorld + delta;
                    Vector3 local = parent != null ? parent.InverseTransformPoint(target) - parent.InverseTransformPoint(_toolStartNodeWorld) : delta;
                    float px = start.PositionX + local.x, py = start.PositionY + local.y;
                    if (snap) { px = Snap(px, .05f); py = Snap(py, .05f); }
                    def.PositionX = px; def.PositionY = py;
                    break;
                }
                case GizmoTool.Rotate:
                {
                    float a = Mathf.Atan2(mouseWorld.y - _toolCenterWorld.y, mouseWorld.x - _toolCenterWorld.x) * Mathf.Rad2Deg;
                    _toolAngleAccum += Mathf.DeltaAngle(_toolLastAngle, a);
                    _toolLastAngle = a;
                    float d = _toolAngleAccum * fine;
                    float rot = start.Rotation + (IsMirrored(parent != null ? parent : bone) ? -d : d);
                    if (snap) rot = Snap(rot, 15f);
                    def.Rotation = rot;
                    break;
                }
                case GizmoTool.Scale:
                {
                    float dist = Mathf.Max((mouse - WorldToGui(_toolCenterWorld, area)).magnitude, 20f);
                    float ratio = 1f + (dist / _toolStartPixelDistance - 1f) * fine;
                    float sx = start.ScaleX * (_axis == GizmoAxis.Y ? 1f : ratio);
                    float sy = start.ScaleY * (_axis == GizmoAxis.X ? 1f : ratio);
                    if (snap) { sx = Snap(sx, .05f); sy = Snap(sy, .05f); }
                    def.ScaleX = Mathf.Abs(sx) < .01f ? .01f * Mathf.Sign(sx == 0f ? 1f : sx) : sx;
                    def.ScaleY = Mathf.Abs(sy) < .01f ? .01f * Mathf.Sign(sy == 0f ? 1f : sy) : sy;
                    break;
                }
            }

            SyncLive(def, live);
            _toolMoved = true;
            MarkDirtyLive(_toolProject, _toolGraph, _toolIsPatch);
        }

        private void CommitPartTool()
        {
            if (_toolMoved && _toolPart != null) PushPartUndo(_toolGraph, _toolPartStart);
            _tool = GizmoTool.None; _toolPart = null; _toolLive = null; _toolBone = null;
        }

        private void CancelPartTool()
        {
            if (_toolPart != null)
            {
                CopyPartTransform(_toolPartStart, _toolPart);
                SyncLive(_toolPart, _toolLive);
                if (_toolMoved) MarkDirtyLive(_toolProject, _toolGraph, _toolIsPatch);
            }
            _tool = GizmoTool.None; _toolPart = null; _toolLive = null; _toolBone = null;
        }

        private void NudgePart(ModProject project, CharacterVisualDef graph, bool isPatch, float dx, float dy, bool big)
        {
            var def = ActivePart(graph);
            var bone = def != null ? PartBone(def.HostRef.Path) : null;
            if (def == null || bone == null || _previewHost == null) return;
            float step = big ? .1f : .01f;
            PushPartUndo(graph, ClonePartTransform(def));
            Vector3 world = bone.position + new Vector3(dx * step, dy * step, 0f);
            Transform parent = bone.parent;
            Vector3 local = parent != null ? parent.InverseTransformPoint(world) - parent.InverseTransformPoint(bone.position) : new Vector3(dx * step, dy * step, 0f);
            def.PositionX += local.x; def.PositionY += local.y;
            SyncLive(def, VisualGraphHostParts.Ensure(_previewHost.transform, bone));
            MarkDirtyLive(project, graph, isPatch);
        }

        private void PushPartUndo(CharacterVisualDef graph, HostPartDef start)
        {
            if (graph == null || start == null) return;
            AddUndo(new EditSnapshot { GraphId = graph.Id, Part = ClonePartTransform(start) });
        }

        private void UndoPart(ModProject project, CharacterVisualDef graph, bool isPatch, EditSnapshot snap)
        {
            var def = EnsurePartDef(graph, snap.Part.HostRef.Path);
            CopyPartTransform(snap.Part, def);
            _selectedPartPath = def.HostRef.Path;
            MarkDirty(project, graph, isPatch);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Drawing
        // ═══════════════════════════════════════════════════════════════

        private void DrawPartGizmos(CharacterVisualDef graph, Rect area)
        {
            if (graph.HostParts == null) return;
            foreach (var part in graph.HostParts)
            {
                if (part == null || part.HostRef == null) continue;
                var bone = PartBone(part.HostRef.Path);
                if (bone == null) continue;
                bool isSel = PartMode && string.Equals(part.HostRef.Path, _selectedPartPath, StringComparison.Ordinal);
                if (!isSel && (part.IsNeutral() || !_showBones)) continue;
                Vector2 c = WorldToGui(bone.position, area);
                Color col = isSel ? PartColor : PartDimColor;
                DrawDiamond(c, isSel ? 7f : 4f, col);
                DrawLabel(c + new Vector2(10f, -18f), (isSel ? "rig: " : "") + ShortName(part.HostRef.Path), col);
                if (isSel)
                {
                    float arm = 46f * WorldPerPixel(area);
                    DrawLine(c, WorldToGui(bone.position + bone.right.normalized * arm, area), new Color(1f, .35f, .35f, 1f), 2f);
                    DrawLine(c, WorldToGui(bone.position + bone.up.normalized * arm, area), new Color(.4f, 1f, .4f, 1f), 2f);
                    var sr = bone.GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sprite != null) DrawRectOutline(RendererScreenRect(sr, area), PartColor, 1.5f);
                }
            }

            if (!_rigMode || _tool != GizmoTool.None) return;
            Vector2 mouse = Event.current.mousePosition;
            if (!area.Contains(mouse) || OverUi(mouse, area)) return;
            if (NearestBone(mouse, area, BonePickRadius) >= 0) return;
            var target = HideTarget(mouse, area, graph, out _, out _);
            if (target == null) return;
            DrawRectOutline(RendererScreenRect(target, area), HoverColor, 2f);
            _hostPathByT.TryGetValue(target.transform, out var tp);
            DrawLabel(new Vector2(mouse.x + 12f, mouse.y + 14f), "click: edit " + ShortName(tp), HoverColor);
        }

        private string RigStatus(CharacterVisualDef graph)
        {
            var def = ActivePart(graph);
            if (def == null) return "Rig edit: click a host bone dot or sprite, then drag it (G/R/S, arrows, Ctrl+Z). Esc/RMB/E exits.";
            return $"Rig '{ShortName(def.HostRef.Path)}'   offset ({def.PositionX:0.###}, {def.PositionY:0.###})   rot {def.Rotation:0.#}   scale ({def.ScaleX:0.###}, {def.ScaleY:0.###})   (on top of the animation)";
        }

        // ═══════════════════════════════════════════════════════════════
        //  Inspector
        // ═══════════════════════════════════════════════════════════════

        private bool DrawHostParts(CharacterVisualDef graph)
        {
            if (graph.HostParts == null) graph.HostParts = new List<HostPartDef>();
            bool changed = false;
            GUILayout.Label($"Host Parts ({graph.HostParts.Count})", EditorStyles.SectionHeader);
            GUILayout.Label("<color=#888>Edit the base model's own bones and sprites. In the viewport press E, click a part, then drag it (G/R/S). Offsets sit on top of the animation; tint and effects apply to that part's sprites.</color>", EditorStyles.RichLabel);
            var paths = HostPaths(graph.BasePresentation);
            for (int i = 0; i < graph.HostParts.Count; i++)
            {
                var part = graph.HostParts[i];
                if (part == null) { graph.HostParts.RemoveAt(i); i--; continue; }
                if (part.HostRef == null) part.HostRef = new CanonicalTransformRef();
                string path = part.HostRef.Path ?? "";
                bool isSel = string.Equals(path, _selectedPartPath, StringComparison.Ordinal);
                GUILayout.BeginHorizontal();
                string title = (i + 1) + ". " + (path.Length == 0 ? "(choose a part)" : ShortName(path)) + (part.IsNeutral() ? "  (no change)" : "");
                if (GUILayout.Button(title, isSel ? EditorStyles.DropdownItemSel : EditorStyles.DropdownItem)) { _selectedPartPath = path; }
                if (GUILayout.Button("-", EditorStyles.DangerButton, GUILayout.Width(24)))
                {
                    graph.HostParts.RemoveAt(i); i--; changed = true;
                    if (isSel) _selectedPartPath = null;
                    GUILayout.EndHorizontal();
                    continue;
                }
                GUILayout.EndHorizontal();
                if (!isSel) continue;

                string newPath = SelectPath("Host Part", path, paths, "visual_hostpart_path");
                if (!string.Equals(newPath, path, StringComparison.Ordinal) && FindPart(graph, newPath) == null)
                {
                    part.HostRef.Path = newPath; _selectedPartPath = newPath; changed = true;
                }
                GUI.changed = false;
                part.PositionX = EditorFields.FloatField("Part Pos X", part.PositionX);
                part.PositionY = EditorFields.FloatField("Part Pos Y", part.PositionY);
                part.Rotation = EditorFields.FloatField("Part Rotation", part.Rotation);
                part.ScaleX = EditorFields.FloatField("Part Scale X", part.ScaleX);
                part.ScaleY = EditorFields.FloatField("Part Scale Y", part.ScaleY);
                part.TintHex = EditorFields.TextField("Part Tint (#RRGGBB)", part.TintHex);
                part.Alpha = EditorFields.FloatField("Part Opacity", part.Alpha, 0f, 1f);
                part.HueShift = EditorFields.FloatField("Part Hue Shift", part.HueShift, -180f, 180f);
                changed |= GUI.changed;
                if (part.Effects == null) part.Effects = new List<RigEffectDef>();
                changed |= DrawEffectList(part.Effects, "visual_partfx_");
                if (GUILayout.Button("Reset transform", EditorStyles.MiniButton, GUILayout.Width(110))) { part.ResetTransform(); changed = true; }
            }
            if (GUILayout.Button("+ Host Part", EditorStyles.MiniButton, GUILayout.Width(90)))
            {
                string pick = paths.FirstOrDefault(p => FindPart(graph, p) == null);
                if (pick != null)
                {
                    graph.HostParts.Add(new HostPartDef { HostRef = new CanonicalTransformRef { Path = pick } });
                    _selectedPartPath = pick;
                    changed = true;
                }
            }
            return changed;
        }
    }
}
