using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Quick-graft authoring: clicking a source part configures everything else (attachment kind, roots, animation, pivot),
    /// old anchor-pivoted nodes are migrated to sprite-centre pivots, and the node inspector hides the plumbing.
    /// </summary>
    public sealed partial class VisualGraphEditor
    {
        private static readonly Regex DefaultNodeId = new Regex(@"^node_\d+$", RegexOptions.Compiled);

        private readonly HashSet<CharacterVisualDef> _pivotChecked = new HashSet<CharacterVisualDef>();
        private bool _foldAdvanced;

        // ═══════════════════════════════════════════════════════════════
        //  Legacy pivot migration
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Old nodes pivot on the source anchor (usually the source root, i.e. the feet), which makes rotate/scale swing wildly.
        /// Moves each one onto the sprite centre and shifts its position so it renders exactly where it did before.
        /// </summary>
        private bool MigrateLegacyPivots(CharacterVisualDef graph)
        {
            if (graph == null || graph.Nodes == null || _pivotChecked.Contains(graph)) return false;
            bool changed = false, retry = false;
            foreach (var node in graph.Nodes)
            {
                if (node == null || node.Pivot == VisualPivotMode.SpriteCenter) continue;
                if (node.RendererRefs == null || node.RendererRefs.Count == 0) continue;
                try { changed |= TryMigratePivot(node, ref retry); }
                catch (Exception ex) { Plugin.Log.LogWarning("[VisualGraphEditor] Pivot migration skipped for '" + node.NodeId + "': " + ex.Message); }
            }
            if (!retry) _pivotChecked.Add(graph);
            return changed;
        }

        private bool TryMigratePivot(VisualNodeDef node, ref bool retry)
        {
            var prefab = ResolvePrefab(node.SourcePrefabRef);
            if (prefab == null) { retry = true; return false; }
            var catalog = BoneReferenceCatalog.Build(prefab.transform);
            bool extracted = node.AttachmentKind == VisualAttachmentKind.ExtractedSourceSubRig;

            Transform subtree;
            string subtreePath = node.SourceSubtreeRef?.Path;
            if (string.IsNullOrEmpty(subtreePath)) { if (!extracted) return false; subtree = catalog.Root; }
            else if (!catalog.TryResolveCanonical(subtreePath, out subtree) || subtree == null) return false;

            var renderers = new List<SpriteRenderer>();
            foreach (var reference in node.RendererRefs)
            {
                Transform t;
                SpriteRenderer sr = null;
                if (reference == null || !catalog.TryResolveCanonical(reference.Path, out t) || t == null || (sr = t.GetComponent<SpriteRenderer>()) == null) return false;
                renderers.Add(sr);
            }

            Vector3 anchorLocal = Vector3.zero;
            string anchorPath = node.SourceAnchorRef?.Path;
            if (!string.IsNullOrEmpty(anchorPath))
            {
                Transform anchor;
                if (!catalog.TryResolveCanonical(anchorPath, out anchor) || anchor == null) return false;
                Matrix4x4 m = subtree.worldToLocalMatrix * anchor.localToWorldMatrix;
                anchorLocal = m.MultiplyPoint3x4(Vector3.zero);
                // An extracted node with a rotated or scaled anchor cannot be reproduced by a pure translation.
                if (extracted && ((m.MultiplyVector(Vector3.right) - Vector3.right).sqrMagnitude > 1e-6f || (m.MultiplyVector(Vector3.up) - Vector3.up).sqrMagnitude > 1e-6f)) return false;
            }
            else if (extracted) return false;

            Vector3 centre = VisualInstanceFactory.ComputeSpritePivot(subtree, renderers);
            var t2 = node.LocalAttachmentTransform;
            float sx = t2.ScaleX * node.Overrides.Scale, sy = t2.ScaleY * node.Overrides.Scale;
            if (node.FacingPolicy == VisualFacingPolicy.MirrorX) sx = -sx;
            if (node.FacingPolicy == VisualFacingPolicy.MirrorY) sy = -sy;
            float lx = (centre.x - anchorLocal.x) * sx, ly = (centre.y - anchorLocal.y) * sy;
            float rad = t2.Rotation * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            t2.PositionX += cos * lx - sin * ly;
            t2.PositionY += sin * lx + cos * ly;
            node.Pivot = VisualPivotMode.SpriteCenter;
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Click-to-graft inference
        // ═══════════════════════════════════════════════════════════════

        private SourcePart FindSourcePart(string path)
        {
            for (int i = 0; i < _srcParts.Count; i++) if (string.Equals(_srcParts[i].Path, path, StringComparison.Ordinal)) return _srcParts[i];
            return null;
        }

        private bool PartIsSkinned(string path)
        {
            var part = FindSourcePart(path);
            return part != null && part.R != null && part.R.GetComponent<UnityEngine.U2D.Animation.SpriteSkin>() != null;
        }

        /// <summary>True when <paramref name="path"/> is the subtree itself or lies below it. The root reference is an ancestor of everything.</summary>
        private static bool IsUnder(string path, string ancestor)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(ancestor)) return false;
            if (string.Equals(ancestor, BoneReferenceCatalog.RootReference, StringComparison.Ordinal)) return true;
            if (string.Equals(path, BoneReferenceCatalog.RootReference, StringComparison.Ordinal)) return false;
            return string.Equals(path, ancestor, StringComparison.Ordinal) || path.StartsWith(ancestor + "/", StringComparison.Ordinal);
        }

        private bool SkinBonesUnder(string partPath, string subtreePath)
        {
            var part = FindSourcePart(partPath);
            var skin = part?.R == null ? null : part.R.GetComponent<UnityEngine.U2D.Animation.SpriteSkin>();
            if (skin == null) return true;
            if (skin.rootBone != null && !IsUnder(_srcCatalog.GetCanonicalKey(skin.rootBone), subtreePath)) return false;
            if (skin.boneTransforms != null)
                foreach (var bone in skin.boneTransforms)
                    if (bone != null && !IsUnder(_srcCatalog.GetCanonicalKey(bone), subtreePath)) return false;
            return true;
        }

        /// <summary>Fills in everything a node needs from the parts it has grafted, so the user only picks parts.</summary>
        private void AutoConfigureNode(VisualNodeDef node)
        {
            if (node == null || node.RendererRefs == null || node.RendererRefs.Count == 0 || _srcCatalog == null) return;
            var refs = node.RendererRefs.Where(r => r != null && !string.IsNullOrEmpty(r.Path)).ToList();
            if (refs.Count == 0) return;
            bool skinned = refs.Any(r => PartIsSkinned(r.Path));
            node.Pivot = VisualPivotMode.SpriteCenter;
            node.AttachmentKind = skinned ? VisualAttachmentKind.ExtractedSourceSubRig : VisualAttachmentKind.RigidAttachment;

            string root = BoneReferenceCatalog.RootReference;
            string subtree = string.IsNullOrEmpty(node.SourceSubtreeRef?.Path) ? root : node.SourceSubtreeRef.Path;
            Transform ignored;
            bool subtreeOk = _srcCatalog.TryResolveCanonical(subtree, out ignored) && refs.All(r => IsUnder(r.Path, subtree)) && (!skinned || refs.All(r => SkinBonesUnder(r.Path, subtree)));
            if (!subtreeOk) subtree = root;
            node.SourceSubtreeRef = new CanonicalTransformRef { Path = subtree };

            string anchor = node.SourceAnchorRef?.Path;
            if (string.IsNullOrEmpty(anchor) || !IsUnder(anchor, subtree) || !_srcCatalog.TryResolveCanonical(anchor, out ignored)) anchor = subtree;
            node.SourceAnchorRef = new CanonicalTransformRef { Path = anchor };

            var policy = node.AnimationPolicy;
            if (policy == null) policy = node.AnimationPolicy = new AnimationPolicyDef();
            if (!skinned || policy.Mode != VisualAnimationMode.SourceSynced)
            {
                policy.Mode = VisualAnimationMode.Static;
                policy.Fallback = VisualAnimationFallback.None;
                if (!skinned && policy.ClipMap != null) policy.ClipMap.Clear();
            }
            else if (policy.Fallback == VisualAnimationFallback.HostBound) policy.Fallback = VisualAnimationFallback.None;
        }

        /// <summary>Changing the source model invalidates every path that pointed into the old one.</summary>
        private static void ResetSource(VisualNodeDef node)
        {
            string root = BoneReferenceCatalog.RootReference;
            node.RendererRefs = new List<CanonicalTransformRef>();
            node.SourceSubtreeRef = new CanonicalTransformRef { Path = root };
            node.SourceAnchorRef = new CanonicalTransformRef { Path = root };
            node.AttachmentKind = VisualAttachmentKind.RigidAttachment;
            node.Pivot = VisualPivotMode.SpriteCenter;
            node.AnimationPolicy = new AnimationPolicyDef();
        }

        private static string NodeIdFromPart(string path)
        {
            string name = ShortName(path);
            int bracket = name.LastIndexOf('[');
            if (bracket > 0) name = name.Substring(0, bracket);
            var sb = new StringBuilder();
            foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            string id = sb.ToString().Trim('_');
            return id.Length == 0 ? "part" : id.ToLowerInvariant();
        }

        /// <summary>
        /// Source-inset click: toggles the part and then configures the node. A part whose rig type (skinned vs plain)
        /// differs from the node's existing parts goes on a new sibling node, because one node can only be one attachment kind.
        /// </summary>
        private void ToggleSourcePart(ModProject project, CharacterVisualDef graph, bool isPatch, VisualNodeDef node, string path)
        {
            if (node == null || string.IsNullOrEmpty(_srcKey)) return;
            var refs = node.RendererRefs ?? new List<CanonicalTransformRef>();
            bool sameSource = string.Equals(node.SourcePrefabRef, _srcKey, StringComparison.Ordinal);
            // A node with parts keeps its model; a model from the picker goes on the rig by dragging (that makes a new node).
            if (!sameSource && refs.Count > 0) return;
            int existing = refs.FindIndex(r => r != null && string.Equals(r.Path, path, StringComparison.Ordinal));
            if (existing >= 0)
            {
                var removeSnap = PushNodeUndo(graph, node);
                refs.RemoveAt(existing);
                node.RendererRefs = refs;
                AutoConfigureNode(node);
                if (removeSnap != null) removeSnap.AfterId = node.NodeId;
                MarkDirty(project, graph, isPatch);
                return;
            }

            bool hitSkinned = PartIsSkinned(path);
            bool mixes = refs.Count > 0 && refs.Any(r => r != null && PartIsSkinned(r.Path) != hitSkinned);
            if (mixes)
            {
                var sibling = NewNode(graph);
                sibling.NodeId = UniqueNodeId(graph, NodeIdFromPart(path));
                sibling.HostSocketRef = new CanonicalTransformRef { Path = node.HostSocketRef?.Path ?? BoneReferenceCatalog.RootReference };
                sibling.SourcePrefabRef = node.SourcePrefabRef;
                sibling.SortingPolicy.Placement = node.SortingPolicy.Placement;
                sibling.SortingPolicy.ReferencePath = node.SortingPolicy.ReferencePath;
                sibling.RendererRefs = new List<CanonicalTransformRef> { new CanonicalTransformRef { Path = path } };
                AutoConfigureNode(sibling);
                int at = graph.Nodes.IndexOf(node);
                graph.Nodes.Insert(at < 0 ? graph.Nodes.Count : at + 1, sibling);
                PushCreatedUndo(graph, sibling);
                _selectedNodeIndex = graph.Nodes.IndexOf(sibling);
                _pendingNodeDeleteId = null;
                MarkDirty(project, graph, isPatch);
                return;
            }

            bool wasEmpty = refs.Count == 0;
            var addSnap = PushNodeUndo(graph, node);
            if (!sameSource) { node.SourcePrefabRef = _srcKey; ResetSource(node); refs = node.RendererRefs; }
            refs.Add(new CanonicalTransformRef { Path = path });
            node.RendererRefs = refs;
            if (wasEmpty && DefaultNodeId.IsMatch(node.NodeId ?? "")) node.NodeId = UniqueNodeId(graph, NodeIdFromPart(path));
            AutoConfigureNode(node);
            if (addSnap != null) addSnap.AfterId = node.NodeId;
            MarkDirty(project, graph, isPatch);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Drag a source part onto the rig
        // ═══════════════════════════════════════════════════════════════

        private string _srcPressPath;
        private Vector2 _srcPressPos;
        private bool _srcPressShift, _srcDragging;
        private const float SourceDragThreshold = 6f;
        private static bool _dropKeepSize = true;

        private void ClearSrcPress() { _srcPressPath = null; _srcDragging = false; }

        /// <summary>Press on a source part: a click toggles it on the selected node, a drag carries it onto the rig. Returns true when the event was consumed.</summary>
        private bool HandleSourceDrag(ModProject project, CharacterVisualDef graph, bool isPatch, Rect area)
        {
            Event e = Event.current;
            var node = SelectedNode(graph);
            if (_srcHost == null || FindSourcePart(_srcPressPath) == null) { ClearSrcPress(); return false; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { ClearSrcPress(); e.Use(); return true; }
            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                if (!_srcDragging && (e.mousePosition - _srcPressPos).magnitude > SourceDragThreshold) _srcDragging = true;
                e.Use();
                return true;
            }
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                string path = _srcPressPath;
                bool dragged = _srcDragging, multi = _srcPressShift || e.shift;
                Vector2 m = e.mousePosition;
                ClearSrcPress();
                e.Use();
                if (!dragged) ToggleSourcePart(project, graph, isPatch, node, path);
                else if (area.Contains(m) && !OverUi(m, area)) DropSourcePart(project, graph, isPatch, path, multi, m, area);
                return true;
            }
            return false;
        }

        /// <summary>Shift-drag grabs the whole piece: skinned parts that share a root bone, or plain parts under the same parent (unless that parent is the model root).</summary>
        private List<string> SourceGroup(string path, bool multi)
        {
            var group = new List<string> { path };
            var hit = FindSourcePart(path);
            if (!multi || hit == null || hit.R == null || _srcCatalog == null) return group;
            var hitSkin = hit.R.GetComponent<UnityEngine.U2D.Animation.SpriteSkin>();
            Transform parent = hit.R.transform.parent;
            bool useParent = hitSkin == null && parent != null && parent != _srcCatalog.Root;
            foreach (var p in _srcParts)
            {
                if (p == hit || !Selectable(p)) continue;
                var skin = p.R.GetComponent<UnityEngine.U2D.Animation.SpriteSkin>();
                if ((skin != null) != (hitSkin != null)) continue;
                bool same = hitSkin != null ? hitSkin.rootBone != null && skin.rootBone == hitSkin.rootBone : useParent && p.R.transform.IsChildOf(parent);
                if (same) group.Add(p.Path);
            }
            return group;
        }

        private Bounds GroupBounds(List<string> paths)
        {
            bool any = false;
            Bounds b = default;
            foreach (var path in paths)
            {
                var p = FindSourcePart(path);
                if (p == null || p.R == null) continue;
                if (!any) { b = p.R.bounds; any = true; } else b.Encapsulate(p.R.bounds);
            }
            return b;
        }

        private void DropSourcePart(ModProject project, CharacterVisualDef graph, bool isPatch, string path, bool multi, Vector2 mouse, Rect area)
        {
            int boneIndex = NearestBone(mouse, area, float.MaxValue);
            if (boneIndex < 0 || _hostBones[boneIndex].T == null || string.IsNullOrEmpty(_srcKey)) return;
            var bone = _hostBones[boneIndex];
            var paths = SourceGroup(path, multi);

            var created = NewNode(graph);
            created.NodeId = UniqueNodeId(graph, NodeIdFromPart(path));
            created.SourcePrefabRef = _srcKey;
            created.HostSocketRef = new CanonicalTransformRef { Path = bone.Path };
            created.RendererRefs = paths.Select(p => new CanonicalTransformRef { Path = p }).ToList();
            AutoConfigureNode(created);

            // The sprite-centre pivot sits on the attachment root, so the drop point is the position; rotate it back upright against the bone.
            Vector3 world = GuiToWorld(mouse, area);
            Vector3 local = bone.T.InverseTransformPoint(world);
            float worldRot = -bone.T.eulerAngles.z;
            created.LocalAttachmentTransform.PositionX = local.x;
            created.LocalAttachmentTransform.PositionY = local.y;
            created.LocalAttachmentTransform.Rotation = Mathf.DeltaAngle(0f, IsMirrored(bone.T) ? -worldRot : worldRot);

            // The attachment sits under the bone, so the bone's scale multiplies the part; undo it so the part lands the size the inset (and the ghost) showed.
            if (_dropKeepSize && _srcCatalog?.Root != null)
            {
                Vector3 src = _srcCatalog.Root.lossyScale, host = bone.T.lossyScale;
                if (Mathf.Abs(host.x) > 1e-4f) created.LocalAttachmentTransform.ScaleX = Mathf.Clamp(Mathf.Abs(src.x / host.x), .01f, 100f);
                if (Mathf.Abs(host.y) > 1e-4f) created.LocalAttachmentTransform.ScaleY = Mathf.Clamp(Mathf.Abs(src.y / host.y), .01f, 100f);
            }
            graph.Nodes.Add(created);
            PushCreatedUndo(graph, created);
            _selectedNodeIndex = graph.Nodes.Count - 1;
            _pendingNodeDeleteId = null;
            MarkDirty(project, graph, isPatch);
        }

        private void DrawSourceDragGhost(CharacterVisualDef graph, Rect area)
        {
            if (_srcPressPath == null || !_srcDragging) return;
            // A release outside the window or panel never reaches us, so drop a stale drag as soon as the button is up.
            if (!Input.GetMouseButton(0)) { ClearSrcPress(); return; }
            if (Event.current.type != EventType.Repaint) return;
            Vector2 m = Event.current.mousePosition;
            if (!area.Contains(m) || OverUi(m, area)) return;
            bool multi = _srcPressShift || Event.current.shift;
            var paths = SourceGroup(_srcPressPath, multi);
            Bounds union = GroupBounds(paths);
            float ppu = 1f / WorldPerPixel(area);
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, .7f);
            foreach (var path in paths)
            {
                var part = FindSourcePart(path);
                var sprite = part?.R == null ? null : part.R.sprite;
                if (sprite == null || sprite.texture == null) continue;
                Vector3 size = part.R.bounds.size;
                Vector3 offset = part.R.bounds.center - union.center;
                Vector2 centre = m + new Vector2(offset.x, -offset.y) * ppu;
                Rect dest = new Rect(centre.x - size.x * ppu * .5f, centre.y - size.y * ppu * .5f, size.x * ppu, size.y * ppu);
                Rect tr = sprite.textureRect;
                Texture2D tex = sprite.texture;
                GUI.DrawTextureWithTexCoords(dest, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
            }
            GUI.color = prevColor;
            int bi = NearestBone(m, area, float.MaxValue);
            if (bi >= 0 && _hostBones[bi].T != null)
            {
                Vector2 bp = WorldToGui(_hostBones[bi].T.position, area);
                DrawLine(bp, m, SocketColor, 1.5f);
                DrawDiamond(bp, 7f, SocketColor);
                DrawLabel(m + new Vector2(14f, 14f), "graft " + ShortName(_srcPressPath) + (paths.Count > 1 ? " +" + (paths.Count - 1) : "") + " onto " + ShortName(_hostBones[bi].Path), HoverColor);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Node inspector pieces
        // ═══════════════════════════════════════════════════════════════

        private void DrawNodeIssues(CharacterVisualDef graph, VisualNodeDef node)
        {
            var result = GetCompileResult(graph);
            foreach (var d in result.Diagnostics)
                if (d.Severity == VisualGraphDiagnosticSeverity.Error && string.Equals(d.NodeId, node.NodeId, StringComparison.OrdinalIgnoreCase))
                    GUILayout.Label("<color=#e07070>" + d.Field + ": " + d.Message + "</color>", EditorStyles.RichLabel);
        }

        /// <summary>The grafted parts as labels (labels take no control id, so the list can grow without disturbing focus), plus a fixed Clear button.</summary>
        private bool DrawGraftedParts(VisualNodeDef node)
        {
            if (node.RendererRefs == null) node.RendererRefs = new List<CanonicalTransformRef>();
            if (node.RendererRefs.Count == 0)
                GUILayout.Label("<color=#e6b35a>Nothing grafted yet - click a part in the \"Graft from\" inset in the viewport.</color>", EditorStyles.RichLabel);
            else
                foreach (var r in node.RendererRefs)
                    GUILayout.Label("   \u2022 " + ShortName(r?.Path), EditorStyles.RichLabel);
            bool changed = false;
            if (GUILayout.Button("Clear parts", EditorStyles.MiniButton, GUILayout.Width(80)) && node.RendererRefs.Count > 0)
            {
                node.RendererRefs.Clear();
                changed = true;
            }
            return changed;
        }
    }
}
