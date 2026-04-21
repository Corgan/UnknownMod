using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    public partial class SpriteSkinEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  INPUT HANDLING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Effective edit mode for the current drag, accounting for modifier keys.</summary>
        private EditMode _dragEffectiveMode;

        private void HandleInput(Rect vp, Rect drawn)
        {
            Event e = Event.current;
            Vector2 mp = e.mousePosition;
            if (!vp.Contains(mp)) return;

            // Scroll = zoom
            if (e.type == EventType.ScrollWheel)
            {
                _zoom = Mathf.Clamp(_zoom + e.delta.y * 0.15f, 0.3f, 10f);
                e.Use(); return;
            }

            // Right drag = pan
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                _panning = true; _panMouseStart = mp; _panStart = _pan;
                e.Use(); return;
            }
            if (_panning)
            {
                if (e.type == EventType.MouseDrag && e.button == 1)
                {
                    float scale = _zoom * 2f / drawn.height;
                    Vector2 delta = mp - _panMouseStart;
                    _pan = _panStart + new Vector2(-delta.x * scale, delta.y * scale);
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 1)
                { _panning = false; e.Use(); return; }
            }

            // ── Keyboard shortcuts ───────────────────────────────────
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    // Tab = cycle through overlapping bones
                    case KeyCode.Tab when _overlapCandidates.Count > 1:
                        _overlapIndex = (_overlapIndex + 1) % _overlapCandidates.Count;
                        SelectFromOverlap(_overlapCandidates[_overlapIndex]);
                        e.Use(); return;

                    // W/E/R = switch edit mode
                    case KeyCode.W: _mode = EditMode.Move; e.Use(); return;
                    case KeyCode.E: _mode = EditMode.Rotate; e.Use(); return;
                    case KeyCode.R: _mode = EditMode.Scale; e.Use(); return;

                    // Delete/Backspace = toggle remove/restore selected bone
                    case KeyCode.Delete:
                    case KeyCode.Backspace:
                        if (_selectedBone != null) ToggleRemoveBone();
                        e.Use(); return;

                    // X = clear bone override for selected bone
                    case KeyCode.X:
                        if (_selectedBone != null) ClearSelectedBoneOverride();
                        e.Use(); return;

                    // Escape = deselect
                    case KeyCode.Escape:
                        _selectedBone = null;
                        _selectedBonePath = null;
                        _overlapCandidates.Clear();
                        e.Use(); return;

                    // F = frame selected bone (center viewport on it)
                    case KeyCode.F:
                        if (_selectedBonePath != null) FrameSelectedBone();
                        e.Use(); return;
                }
            }

            // Alt+Left click = start pivot drag (for non-SpriteSkin sprite bones)
            if (_pivotDragging)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    ApplyPivotDrag(mp, drawn);
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    _pivotDragging = false;
                    e.Use(); return;
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0 && e.alt && _selectedBonePath != null)
            {
                var pivH = _handles.Find(b => b.Path == _selectedBonePath);
                if (pivH != null && pivH.HasSpriteRenderer &&
                    (pivH.SkinRootTransform == null || pivH.SkinRootTransform == pivH.Transform))
                {
                    _pivotDragging = true;
                    _pivotDragStart = mp;
                    e.Use(); return;
                }
            }

            // Left click = select / drag bone
            // Shift+drag = rotate, Ctrl+drag = scale (overrides current mode for this drag)
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                CollectOverlapCandidates(mp, drawn);
                if (_overlapCandidates.Count > 0)
                {
                    _overlapIndex = 0;
                    _overlapScreenPos = mp;
                    var picked = _overlapCandidates[0];
                    SelectFromOverlap(picked);
                    _dragging = true;
                    _dragMouseStart = mp;
                    var dragT = picked.SkinRootTransform ?? picked.Transform;
                    _dragLocalStart = dragT.localPosition;
                    _dragRotStart = dragT.localEulerAngles.z;
                    _dragScaleStart = dragT.localScale;
                    // Determine effective mode: Shift=Rotate, Ctrl=Scale, else current mode
                    _dragEffectiveMode = e.shift ? EditMode.Rotate
                                       : e.control ? EditMode.Scale
                                       : _mode;
                    e.Use(); return;
                }
                _selectedBone = null;
                _selectedBonePath = null;
                _overlapCandidates.Clear();
            }
            if (_dragging)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    var h = _handles.Find(b => b.Path == _selectedBonePath);
                    if (h?.Transform != null) ApplyDrag(h, mp, drawn, _dragEffectiveMode);
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    if (_selectedBonePath != null) CommitBoneOverride(_selectedBonePath, _dragEffectiveMode);
                    _dragging = false; e.Use(); return;
                }
            }
        }

        /// <summary>Toggle the selected bone between removed and restored.</summary>
        private void ToggleRemoveBone()
        {
            var sprites = GetSpriteDict();
            if (sprites == null || _previewNpcId == null) return;
            if (!sprites.TryGetValue(_previewNpcId, out var ovr)) return;
            // Only for host bones (not graft)
            if (_selectedGraftIdx >= 0) return;

            if (ovr.RemovedBones.Contains(_selectedBone))
                ovr.RemovedBones.Remove(_selectedBone);
            else
                ovr.RemovedBones.Add(_selectedBone);
            RefreshPreviewOverrides();
            OnSpriteModified();
        }

        /// <summary>Clear the bone override for the selected bone (reset to base pose).</summary>
        private void ClearSelectedBoneOverride()
        {
            var sprites = GetSpriteDict();
            if (sprites == null || _previewNpcId == null) return;
            if (!sprites.TryGetValue(_previewNpcId, out var ovr)) return;

            // Determine which override dict to use (host vs graft)
            var boneOverrides = ovr.BoneOverrides;
            if (_selectedGraftIdx >= 0 && _selectedGraftIdx < ovr.Grafts.Count)
                boneOverrides = ovr.Grafts[_selectedGraftIdx].BoneOverrides;

            if (boneOverrides.Remove(_selectedBone))
            {
                RefreshPreviewOverrides();
                OnSpriteModified();
            }
        }

        /// <summary>Center the viewport on the currently selected bone.</summary>
        private void FrameSelectedBone()
        {
            var h = _handles.Find(b => b.Path == _selectedBonePath);
            if (h?.Transform == null) return;
            _pan = -(Vector2)h.Transform.position + (Vector2)PreviewOrigin;
        }

        /// <summary>Select a bone from the overlap candidates list.</summary>
        private void SelectFromOverlap(BoneHandle picked)
        {
            _selectedBone = picked.Name;
            _selectedBonePath = picked.Path;
            // If already dragging, update drag start for the new bone
            if (_dragging)
            {
                var dragT = picked.SkinRootTransform ?? picked.Transform;
                _dragLocalStart = dragT.localPosition;
                _dragRotStart = dragT.localEulerAngles.z;
                _dragScaleStart = dragT.localScale;
            }
        }

        /// <summary>Collect all bones within pick radius, sorted by distance.</summary>
        private void CollectOverlapCandidates(Vector2 mousePos, Rect drawn)
        {
            _overlapCandidates.Clear();
            var candidates = new List<(BoneHandle h, float dist)>();
            var sprites = GetSpriteDict();
            CharacterOverrideDef ovr = null;
            if (sprites != null && _previewNpcId != null)
                sprites.TryGetValue(_previewNpcId, out ovr);

            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                if (ovr?.RemovedBones.Contains(h.Name) == true) continue;
                if (!h.HasSpriteRenderer && !_showRigBones) continue;
                if (h.HasSpriteRenderer && !_showSpriteDots) continue;
                Vector2 sp = WorldToViewport(h.Transform.position, drawn);
                float dist = Vector2.Distance(sp, mousePos);
                if (dist < PickRadius)
                    candidates.Add((h, dist));
            }
            candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
            _overlapCandidates = candidates.Select(c => c.h).ToList();
        }

        // ═══════════════════════════════════════════════════════════════
        //  TIMELINE INPUT
        // ═══════════════════════════════════════════════════════════════

        private void HandleTimelineInput(Rect r)
        {
            if (_previewAnimator == null || _clipNames == null || _clipNames.Length == 0) return;

            Event e = Event.current;
            Vector2 mp = e.mousePosition;

            // Scrub track rect (must match DrawTimeline layout)
            float trackX = r.x + 8;
            float trackW = r.width - 16;
            float trackY = r.y + ScrubTrackY;
            float trackH = 12f;
            Rect trackRect = new Rect(trackX, trackY - 4, trackW, trackH + 8); // expanded hit area

            if (e.type == EventType.MouseDown && e.button == 0 && trackRect.Contains(mp))
            {
                _timelineDragging = true;
                float norm = Mathf.Clamp01((mp.x - trackX) / trackW);
                ScrubToNormTime(norm);
                e.Use(); return;
            }

            if (_timelineDragging)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    float norm = Mathf.Clamp01((mp.x - trackX) / trackW);
                    ScrubToNormTime(norm);
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    _timelineDragging = false;
                    e.Use(); return;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  DRAG & PICK
        // ═══════════════════════════════════════════════════════════════

        // Cached drag transform state — replayed between pipeline and render
        // so the override pipeline doesn't overwrite the user's in-progress drag.
        private Vector3 _dragCachedPos;
        private Vector3 _dragCachedEuler;
        private Vector3 _dragCachedScale;

        private void ApplyDrag(BoneHandle h, Vector2 mousePos, Rect drawn, EditMode mode)
        {
            // For SpriteSkin sprites where rootBone is a different GO,
            // manipulate the rootBone (visual deformation is bone-driven).
            var t = h.SkinRootTransform ?? h.Transform;
            Vector2 delta = mousePos - _dragMouseStart;
            float pixelScale = _zoom * 2f / drawn.height;
            switch (mode)
            {
                case EditMode.Move:
                    Vector3 worldDelta = new(delta.x * pixelScale, -delta.y * pixelScale, 0);
                    Vector3 localDelta = t.parent != null
                        ? t.parent.InverseTransformVector(worldDelta) : worldDelta;
                    t.localPosition = _dragLocalStart + localDelta;
                    break;
                case EditMode.Rotate:
                    t.localEulerAngles = new Vector3(0, 0, _dragRotStart - delta.x * 0.5f);
                    break;
                case EditMode.Scale:
                    float sf = Mathf.Clamp(1f + delta.x * 0.005f, 0.1f, 5f);
                    t.localScale = _dragScaleStart * sf;
                    break;
            }
            // Cache so ReplayDrag can restore after the pipeline overwrites
            _dragCachedPos = t.localPosition;
            _dragCachedEuler = t.localEulerAngles;
            _dragCachedScale = t.localScale;
        }

        /// <summary>Restore the drag target bone to its last drag-applied state.
        /// Called between the override pipeline and render so the user sees
        /// the dragged position, not the stored BoneOverride value.</summary>
        private void ReplayDrag()
        {
            if (!_dragging || _selectedBonePath == null) return;
            var h = _handles.Find(b => b.Path == _selectedBonePath);
            if (h?.Transform == null) return;
            var t = h.SkinRootTransform ?? h.Transform;
            t.localPosition = _dragCachedPos;
            t.localEulerAngles = _dragCachedEuler;
            t.localScale = _dragCachedScale;
        }

        /// <summary>Convert Alt+drag pixel delta into pivot UV delta, update the BoneOverride,
        /// and refresh the preview so the sprite re-creates with the new pivot.</summary>
        private void ApplyPivotDrag(Vector2 mousePos, Rect drawn)
        {
            var h = _handles.Find(b => b.Path == _selectedBonePath);
            if (h?.Transform == null) return;
            var sr = h.Transform.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return;

            var sprite = sr.sprite;
            // Pixels per world-unit at this zoom: how many viewport pixels = 1 sprite pixel
            float worldUnitsPerViewportPx = _zoom * 2f / drawn.height;
            float pixelsPerUnit = sprite.pixelsPerUnit;
            // 1 viewport pixel = worldUnitsPerViewportPx world units = worldUnitsPerViewportPx * pixelsPerUnit sprite pixels
            float spritePixelsPerViewportPx = worldUnitsPerViewportPx * pixelsPerUnit;

            Vector2 delta = mousePos - _pivotDragStart;
            _pivotDragStart = mousePos;

            // Convert viewport pixel delta to UV delta
            float duv_x = (delta.x * spritePixelsPerViewportPx) / sprite.rect.width;
            float duv_y = (-delta.y * spritePixelsPerViewportPx) / sprite.rect.height; // Y flipped

            // Get or create BoneOverride
            var sprites = GetSpriteDict();
            if (sprites == null || _previewNpcId == null) return;
            if (!sprites.TryGetValue(_previewNpcId, out var ovr)) return;

            Dictionary<string, BoneOverride> boneOvrs;
            if (h.GraftIndex >= 0 && h.GraftIndex < ovr.Grafts.Count)
                boneOvrs = ovr.Grafts[h.GraftIndex].BoneOverrides;
            else
                boneOvrs = ovr.BoneOverrides;

            if (!boneOvrs.TryGetValue(h.Name, out var bo))
            { bo = new BoneOverride(); boneOvrs[h.Name] = bo; }

            // Current pivot UV (from override or from sprite)
            float curPx = bo.PivotX >= 0f ? bo.PivotX : sprite.pivot.x / sprite.rect.width;
            float curPy = bo.PivotY >= 0f ? bo.PivotY : sprite.pivot.y / sprite.rect.height;

            bo.PivotX = Mathf.Clamp01(Mathf.Round((curPx + duv_x) * 100f) / 100f);
            bo.PivotY = Mathf.Clamp01(Mathf.Round((curPy + duv_y) * 100f) / 100f);

            RefreshPreviewOverrides();
            OnSpriteModified();
        }
    }
}
