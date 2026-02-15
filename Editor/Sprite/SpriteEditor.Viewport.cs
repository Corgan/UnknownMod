using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    public partial class SpriteEditor
    {
        // ═══════════════════════════════════════════════════════════════
        //  VIEWPORT (drawn on left side of screen by ModEditor)
        // ═══════════════════════════════════════════════════════════════

        public void DrawViewport(Rect vp)
        {
            EnsureTextures();
            EnsureCamera();

            // If preview was destroyed by scene transition, try to re-spawn
            if (_previewGO == null && _previewNpcId != null)
            {
                var curSprites = GetSpriteDict();
                if (curSprites != null && curSprites.TryGetValue(_previewNpcId, out var sprDef))
                {
                    string baseNpc = !string.IsNullOrEmpty(sprDef.BaseSprite) ? sprDef.BaseSprite : _previewNpcId;
                    SpawnPreview(_previewNpcId, baseNpc);
                }
                else
                {
                    _previewNpcId = null;
                    _handles.Clear();
                }
            }

            // If camera was destroyed, null check
            if (_cam == null || _rt == null)
            {
                GUI.Box(vp, "", GUI.skin.box);
                return;
            }

            if (_previewGO == null || _cam == null)
            {
                GUI.Box(vp, "", GUI.skin.box);
                if (_centeredStyle == null)
                    _centeredStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter, fontSize = 13,
                        normal = { textColor = new Color(0.5f, 0.5f, 0.55f) }
                    };
                GUI.Label(vp, "Select an NPC in the panel to preview.", _centeredStyle);
                return;
            }

            // Render once per frame
            if (Time.frameCount != _lastRenderFrame)
            {
                _lastRenderFrame = Time.frameCount;
                _cam.orthographicSize = _zoom;
                _cam.transform.position = new Vector3(
                    PreviewOrigin.x + _pan.x,
                    PreviewOrigin.y + _pan.y,
                    PreviewOrigin.z - 10f);

                // Update animation timeline tracking
                if (_previewAnimator != null && _animPlaying && _clips != null && _selectedClipIdx < _clips.Length)
                {
                    var clip = _clips[_selectedClipIdx];
                    float clipLen = clip.length;
                    _playbackTime += Time.deltaTime * _animSpeed;
                    if (clip.isLooping || clip.wrapMode == WrapMode.Loop)
                        _playbackTime %= clipLen;
                    else
                        _playbackTime = Mathf.Min(_playbackTime, clipLen);
                    _timelineNormTime = clipLen > 0 ? _playbackTime / clipLen : 0f;
                    clip.SampleAnimation(_previewGO, _playbackTime);
                    ApplyBoneOverridesToPreview_Fast();
                    ApplyKeyframeOverridesToPreview(clip.name, _timelineNormTime);
                }

                _cam.Render();
            }

            // Background + RT
            if (_vpBgTex == null) _vpBgTex = ModEditor.MakeTex(2, 2, new Color(0.1f, 0.1f, 0.13f, 1f));
            GUI.DrawTexture(vp, _vpBgTex);

            // Reserve space for timeline at the bottom
            Rect vpContent = new Rect(vp.x, vp.y, vp.width, vp.height - TimelineH);
            Rect timelineRect = new Rect(vp.x, vp.yMax - TimelineH, vp.width, TimelineH);

            GUI.DrawTexture(vpContent, _rt, ScaleMode.ScaleToFit);

            Rect drawn = GetDrawnRect(vpContent);
            DrawHandles(drawn);
            DrawToolbar(vpContent);
            DrawTimeline(timelineRect);
            HandleInput(vpContent, drawn);
            HandleTimelineInput(timelineRect);
        }

        private bool _showBoneLines = true;

        private void DrawHandles(Rect drawn)
        {
            var sprites = GetSpriteDict();
            var overrides = (sprites?.ContainsKey(_previewNpcId) == true)
                ? sprites[_previewNpcId] : null;

            // ── Draw bone connection lines (parent → child) ──
            if (_showBoneLines && _lineMaterial != null)
            {
                // Build Transform → BoneHandle lookup for O(1) parent resolution
                var handleByTransform = new Dictionary<Transform, BoneHandle>(_handles.Count);
                foreach (var bh in _handles)
                    if (bh.Transform != null) handleByTransform[bh.Transform] = bh;

                // Draw in absolute screen coordinates (GUI.BeginClip doesn't affect GL)
                GL.PushMatrix();
                _lineMaterial.SetPass(0);
                GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                GL.Begin(GL.LINES);

                foreach (var h in _handles)
                {
                    if (h.Transform == null || h.Transform.parent == null) continue;
                    if (!handleByTransform.TryGetValue(h.Transform.parent, out var parentHandle)) continue;

                    Vector2 from = WorldToViewport(parentHandle.Transform.position, drawn);
                    Vector2 to = WorldToViewport(h.Transform.position, drawn);

                    // Skip lines fully outside the drawn rect
                    if (!drawn.Contains(from) && !drawn.Contains(to)) continue;

                    // Color: green for added items, cyan for sprite bones, dim gray for rigging bones
                    bool isAdded = overrides?.AddedBones.ContainsKey(h.Name) == true ||
                                   overrides?.AddedSprites.ContainsKey(h.Name) == true;
                    bool isSpriteConnection = h.HasSpriteRenderer || parentHandle.HasSpriteRenderer;
                    Color lineColor = isAdded
                        ? new Color(0.4f, 0.9f, 0.5f, 0.55f) // green for added items
                        : isSpriteConnection
                            ? new Color(0.3f, 0.7f, 0.85f, 0.5f)
                            : new Color(0.5f, 0.5f, 0.5f, 0.35f);

                    // Highlight lines connected to selected bone
                    if (h.Name == _selectedBone || parentHandle.Name == _selectedBone)
                        lineColor = new Color(1f, 1f, 0.3f, 0.7f);

                    GL.Color(lineColor);
                    GL.Vertex3(from.x, from.y, 0);
                    GL.Vertex3(to.x, to.y, 0);
                }

                GL.End();
                GL.PopMatrix();
            }

            // ── Draw handle dots ──

            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                // Hide rigging-only bones in viewport unless toggled or selected
                bool selected = h.Name == _selectedBone;
                if (!h.HasSpriteRenderer && !_showRigBones && !selected) continue;
                Vector2 sp = WorldToViewport(h.Transform.position, drawn);
                if (!drawn.Contains(sp)) continue;

                bool hasOvr = overrides?.Bones.ContainsKey(h.Name) == true;
                bool isAdded = overrides?.AddedBones.ContainsKey(h.Name) == true ||
                               overrides?.AddedSprites.ContainsKey(h.Name) == true;
                float size = selected ? HandleSizeSel : (h.HasSpriteRenderer ? HandleSize : HandleSize * 0.6f);
                Texture2D tex = selected ? _dotSelected :
                                (hasOvr || isAdded) ? _dotOverride :
                                h.HasSpriteRenderer ? _dotSprite : _dotDefault;

                GUI.DrawTexture(new Rect(sp.x - size / 2, sp.y - size / 2, size, size), tex);

                if (selected || _showAllLabels)
                {
                    if (_boneLabelSelStyle == null)
                        _boneLabelSelStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, normal = { textColor = Color.yellow } };
                    if (_boneLabelStyle == null)
                        _boneLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } };
                    var style = selected ? _boneLabelSelStyle : _boneLabelStyle;
                    GUI.Label(new Rect(sp.x + size, sp.y - 7, 140, 16), h.Name, style);
                }
            }

            // ── Draw auto-weight radius circle for selected added bone ──
            if (_lineMaterial != null && overrides != null &&
                !string.IsNullOrEmpty(_selectedBone) &&
                overrides.AddedBones.TryGetValue(_selectedBone, out var selBoneDef) &&
                selBoneDef.InfluenceSprites.Count > 0 && selBoneDef.WeightRadius > 0.001f)
            {
                var selHandle = _handles.Find(bh => bh.Name == _selectedBone);
                if (selHandle?.Transform != null)
                {
                    Vector2 center = WorldToViewport(selHandle.Transform.position, drawn);
                    // Convert world radius to viewport pixels
                    Vector3 edgeWorld = selHandle.Transform.position + Vector3.right * selBoneDef.WeightRadius;
                    Vector2 edgeVP = WorldToViewport(edgeWorld, drawn);
                    float radiusPx = Vector2.Distance(center, edgeVP);

                    if (radiusPx > 2f)
                    {
                        GL.PushMatrix();
                        _lineMaterial.SetPass(0);
                        GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                        GL.Begin(GL.LINES);
                        GL.Color(new Color(0.4f, 0.9f, 0.5f, 0.4f));

                        const int segments = 48;
                        for (int i = 0; i < segments; i++)
                        {
                            float a0 = (float)i / segments * Mathf.PI * 2f;
                            float a1 = (float)(i + 1) / segments * Mathf.PI * 2f;
                            GL.Vertex3(center.x + Mathf.Cos(a0) * radiusPx, center.y + Mathf.Sin(a0) * radiusPx, 0);
                            GL.Vertex3(center.x + Mathf.Cos(a1) * radiusPx, center.y + Mathf.Sin(a1) * radiusPx, 0);
                        }

                        GL.End();
                        GL.PopMatrix();
                    }
                }
            }
        }

        private void DrawToolbar(Rect vp)
        {
            float bw = 32f, bh = 22f, gap = 2f;
            float tx = vp.x + 8, ty = vp.y + 6;

            var modes = new[] { ("M", EditMode.Move), ("R", EditMode.Rotate), ("S", EditMode.Scale) };
            foreach (var (label, mode) in modes)
            {
                Color prev = GUI.color;
                if (mode == _mode) GUI.color = new Color(0.5f, 0.8f, 1f);
                if (GUI.Button(new Rect(tx, ty, bw, bh), label, EditorStyles.MiniButton))
                    _mode = mode;
                GUI.color = prev;
                tx += bw + gap;
            }

            tx += 10;
            if (GUI.Button(new Rect(tx, ty, 68, bh), _showAllLabels ? "Labels ON" : "Labels OFF", EditorStyles.MiniButton))
                _showAllLabels = !_showAllLabels;
            tx += 72;

            if (GUI.Button(new Rect(tx, ty, 62, bh), _showBoneLines ? "Lines ON" : "Lines OFF", EditorStyles.MiniButton))
                _showBoneLines = !_showBoneLines;
            tx += 66;

            if (GUI.Button(new Rect(tx, ty, 52, bh), _showRigBones ? "Rig ON" : "Rig OFF", EditorStyles.MiniButton))
                _showRigBones = !_showRigBones;
            tx += 56;

            if (GUI.Button(new Rect(tx, ty, 70, bh), "Reset View", EditorStyles.MiniButton))
            { _zoom = 2.5f; _pan = Vector2.zero; }

            string modeText = _mode switch
            {
                EditMode.Move => "MOVE", EditMode.Rotate => "ROTATE",
                EditMode.Scale => "SCALE", _ => ""
            };
            GUI.Label(new Rect(vp.xMax - 76, ty, 70, bh),
                $"<color=cyan>{modeText}</color>", EditorStyles.RichLabel);
        }

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

            // Left click = select / drag bone
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var picked = PickBone(mp, drawn);
                if (picked != null)
                {
                    _selectedBone = picked.Name;
                    _dragging = true;
                    _dragMouseStart = mp;
                    _dragLocalStart = picked.Transform.localPosition;
                    _dragRotStart = picked.Transform.localEulerAngles.z;
                    _dragScaleStart = picked.Transform.localScale;
                    e.Use(); return;
                }
                _selectedBone = null;
            }
            if (_dragging)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    var h = _handles.Find(b => b.Name == _selectedBone);
                    if (h?.Transform != null) ApplyDrag(h, mp, drawn);
                    e.Use(); return;
                }
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    if (_selectedBone != null) CommitBoneOverride(_selectedBone);
                    _dragging = false; e.Use(); return;
                }
            }
        }

        // ── Timeline Bar (drawn at bottom of viewport) ───────────────

        private void DrawTimeline(Rect r)
        {
            // Dark background (cached)
            if (_tlBgTex == null) _tlBgTex = ModEditor.MakeTex(2, 2, new Color(0.08f, 0.08f, 0.10f, 1f));
            GUI.DrawTexture(r, _tlBgTex);

            // Top border line (cached)
            if (_tlBorderTex == null) _tlBorderTex = ModEditor.MakeTex(2, 1, new Color(0.25f, 0.25f, 0.3f, 1f));
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), _tlBorderTex);

            if (_previewAnimator == null || _clipNames == null || _clipNames.Length == 0)
            {
                if (_noAnimStyle == null)
                    _noAnimStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter, fontSize = 10,
                        normal = { textColor = new Color(0.4f, 0.4f, 0.45f) }
                    };
                GUI.Label(r, "No animation clips", _noAnimStyle);
                return;
            }

            float clipLen = (_selectedClipIdx < _clipLengths.Length) ? _clipLengths[_selectedClipIdx] : 1f;
            float curTime = _timelineNormTime * clipLen;
            string clipName = _clipNames[_selectedClipIdx];

            // ── Row 1: Controls + Info ─────────────────────────────
            float bx = r.x + 4, by = r.y + 2, bh = 16f;

            // Play/Pause button
            string playLabel = _animPlaying ? "\u275A\u275A" : "\u25B6";
            if (GUI.Button(new Rect(bx, by, 24, bh), playLabel, EditorStyles.MiniButton))
                TogglePlayPause();
            bx += 26;

            // Frame step buttons
            if (GUI.Button(new Rect(bx, by, 20, bh), "\u25C0", EditorStyles.MiniButton))
                StepFrame(-1);
            bx += 22;
            if (GUI.Button(new Rect(bx, by, 20, bh), "\u25B6", EditorStyles.MiniButton))
                StepFrame(1);
            bx += 24;

            // Clip name (clickable to cycle)
            float clipLabelW = Mathf.Min(120, r.width * 0.25f);
            if (_clipStyle == null)
                _clipStyle = new GUIStyle(EditorStyles.MiniButton)
                {
                    alignment = TextAnchor.MiddleLeft, fontSize = 10,
                    normal = { textColor = new Color(0.7f, 0.85f, 1f) }
                };
            if (GUI.Button(new Rect(bx, by, clipLabelW, bh), clipName, _clipStyle))
            {
                // Cycle to next clip
                _selectedClipIdx = (_selectedClipIdx + 1) % _clipNames.Length;
                ScrubToNormTime(_timelineNormTime);
            }
            bx += clipLabelW + 4;

            // Time display
            if (_timeStyle == null)
                _timeStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10, alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = new Color(0.6f, 0.6f, 0.65f) }
                };
            GUI.Label(new Rect(bx, by, 120, bh), $"{curTime:F2}s / {clipLen:F2}s", _timeStyle);

            // Speed display (right-aligned)
            if (_speedStyle == null)
                _speedStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10, alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(0.5f, 0.5f, 0.55f) }
                };
            GUI.Label(new Rect(r.xMax - 64, by, 60, bh), $"{_animSpeed:F1}x", _speedStyle);

            // ── Row 2: Scrub Track ─────────────────────────────────
            float trackX = r.x + 8;
            float trackW = r.width - 16;
            float trackY = r.y + ScrubTrackY;
            float trackH = 12f;

            // Track background (cached)
            if (_tlTrackBgTex == null) _tlTrackBgTex = ModEditor.MakeTex(2, 2, new Color(0.15f, 0.15f, 0.18f, 1f));
            GUI.DrawTexture(new Rect(trackX, trackY, trackW, trackH), _tlTrackBgTex);

            // Filled portion (progress)
            float fillW = trackW * Mathf.Clamp01(_timelineNormTime);
            if (fillW > 1f)
            {
                if (_tlFillTex == null) _tlFillTex = ModEditor.MakeTex(2, 2, new Color(0.3f, 0.55f, 0.8f, 0.8f));
                GUI.DrawTexture(new Rect(trackX, trackY, fillW, trackH), _tlFillTex);
            }

            // Playhead (scrubber handle)
            float headX = trackX + fillW - 4f;
            float headW = 8f, headH = trackH + 4f;
            if (_tlHeadTex == null) _tlHeadTex = ModEditor.MakeTex(2, 2, new Color(0.9f, 0.9f, 0.95f, 1f));
            GUI.DrawTexture(new Rect(headX, trackY - 2, headW, headH), _tlHeadTex);

            // Keyframe diamonds on track (for current clip + selected bone)
            var tlSprites = GetSpriteDict();
            if (tlSprites != null && _previewNpcId != null &&
                tlSprites.TryGetValue(_previewNpcId, out var kfOvr) &&
                kfOvr.AnimOverrides.TryGetValue(clipName, out var kfAnimOvr))
            {
                if (_tlKfDiamondTex == null) _tlKfDiamondTex = ModEditor.MakeTex(2, 2, new Color(1f, 0.7f, 0.2f, 0.9f));
                if (_tlKfDiamondSelTex == null) _tlKfDiamondSelTex = ModEditor.MakeTex(2, 2, new Color(0.3f, 0.9f, 1f, 1f));

                foreach (var boneKf in kfAnimOvr.BoneKeyframes)
                {
                    bool isSelectedBone = boneKf.Key == _selectedBone;
                    foreach (var kf in boneKf.Value)
                    {
                        float kfX = trackX + trackW * Mathf.Clamp01(kf.Time);
                        float dSize = isSelectedBone ? 8f : 5f;
                        var diamTex = isSelectedBone ? _tlKfDiamondSelTex : _tlKfDiamondTex;
                        GUI.DrawTexture(new Rect(kfX - dSize / 2, trackY + trackH / 2 - dSize / 2, dSize, dSize), diamTex);
                    }
                }
            }

            // Track border (cached)
            if (_tlTrackBorderTex == null) _tlTrackBorderTex = ModEditor.MakeTex(2, 2, new Color(0.3f, 0.3f, 0.35f, 1f));
            GUI.DrawTexture(new Rect(trackX, trackY, trackW, 1), _tlTrackBorderTex);
            GUI.DrawTexture(new Rect(trackX, trackY + trackH - 1, trackW, 1), _tlTrackBorderTex);
        }

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

        private void TogglePlayPause()
        {
            if (_clips == null || _clips.Length == 0) return;

            _animPlaying = !_animPlaying;
            if (_animPlaying)
            {
                // Resume from current scrub position
                var clip = _clips[_selectedClipIdx];
                _playbackTime = _timelineNormTime * clip.length;
            }
        }

        private void ScrubToNormTime(float normTime)
        {
            _timelineNormTime = Mathf.Clamp01(normTime);
            if (_previewGO == null || _clips == null || _selectedClipIdx >= _clips.Length) return;

            // Pause playback when scrubbing
            _animPlaying = false;
            var clip = _clips[_selectedClipIdx];
            _playbackTime = normTime * clip.length;
            clip.SampleAnimation(_previewGO, _playbackTime);
            ApplyBoneOverridesToPreview_Fast();
            ApplyKeyframeOverridesToPreview(clip.name, normTime);
        }

        /// <summary>Lightweight per-frame version of ApplyBoneOverridesToPreview.
        /// Called during playback/scrub after SampleAnimation to re-apply bone overrides
        /// that the Animator resets. Falls through to the full method if an override exists.</summary>
        private void ApplyBoneOverridesToPreview_Fast()
        {
            var sprites = GetSpriteDict();
            if (sprites == null || _previewNpcId == null) return;
            if (!sprites.TryGetValue(_previewNpcId, out var ovr)) return;
            ApplyBoneOverridesToPreview(ovr);
        }

        /// <summary>Apply animation keyframe overrides to the preview after SampleAnimation.
        /// Uses SET mode — keyframe values define the absolute bone pose.</summary>
        private void ApplyKeyframeOverridesToPreview(string clipName, float normTime)
        {
            var sprites = GetSpriteDict();
            if (sprites == null || _previewNpcId == null) return;
            if (!sprites.TryGetValue(_previewNpcId, out var ovr)) return;
            if (ovr.AnimOverrides == null || !ovr.AnimOverrides.TryGetValue(clipName, out var animOvr)) return;
            if (animOvr.BoneKeyframes == null) return;

            foreach (var boneKvp in animOvr.BoneKeyframes)
            {
                var bh = _handles.Find(h => h.Name == boneKvp.Key);
                if (bh?.Transform == null) continue;

                var interpolated = NpcSpriteOverride.InterpolateKeyframes(boneKvp.Value, normTime);
                if (interpolated == null) continue;

                bh.Transform.localPosition = new Vector3(interpolated.PosX, interpolated.PosY, 0f);
                bh.Transform.localEulerAngles = new Vector3(0, 0, interpolated.Rotation);
                bh.Transform.localScale = new Vector3(interpolated.ScaleX, interpolated.ScaleY, 1f);
            }
        }

        private void StepFrame(int direction)
        {
            if (_previewAnimator == null || _clipNames == null || _clipNames.Length == 0) return;

            float clipLen = (_selectedClipIdx < _clipLengths.Length) ? _clipLengths[_selectedClipIdx] : 1f;
            // Assume ~30fps for frame stepping
            float frameDelta = (1f / 30f) / clipLen;
            float newTime = Mathf.Clamp01(_timelineNormTime + direction * frameDelta);
            ScrubToNormTime(newTime);
        }

        /// <summary>Sample the current clip at 11 time points and record the selected bone's transform.</summary>
        private void SampleBaseKeyframes()
        {
            if (_clips == null || _selectedClipIdx >= _clips.Length || _selectedBone == null || _previewGO == null) return;
            var bh = _handles.Find(b => b.Name == _selectedBone);
            if (bh?.Transform == null) return;

            float savedNorm = _timelineNormTime;
            bool savedPlaying = _animPlaying;
            var clip = _clips[_selectedClipIdx];

            const int samples = 11;
            var results = new SampledKf[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                clip.SampleAnimation(_previewGO, t * clip.length);

                results[i] = new SampledKf
                {
                    Time = t,
                    PosX = bh.Transform.localPosition.x,
                    PosY = bh.Transform.localPosition.y,
                    Rot = bh.Transform.localEulerAngles.z,
                    ScaleX = bh.Transform.localScale.x,
                    ScaleY = bh.Transform.localScale.y,
                };
            }

            // Restore previous time
            clip.SampleAnimation(_previewGO, savedNorm * clip.length);
            _animPlaying = savedPlaying;

            _sampledKeyframes = results;
            _sampledBone = _selectedBone;
            _sampledClip = _selectedClipIdx;

            Plugin.Log.LogInfo($"[SpriteEditor] Sampled {samples} keyframes for bone '{_selectedBone}' in clip '{clip.name}'");
        }

        private void ApplyDrag(BoneHandle h, Vector2 mousePos, Rect drawn)
        {
            Vector2 delta = mousePos - _dragMouseStart;
            float pixelScale = _zoom * 2f / drawn.height;
            switch (_mode)
            {
                case EditMode.Move:
                    Vector3 worldDelta = new(delta.x * pixelScale, -delta.y * pixelScale, 0);
                    Vector3 localDelta = h.Transform.parent != null
                        ? h.Transform.parent.InverseTransformVector(worldDelta) : worldDelta;
                    h.Transform.localPosition = _dragLocalStart + localDelta;
                    break;
                case EditMode.Rotate:
                    h.Transform.localEulerAngles = new Vector3(0, 0, _dragRotStart - delta.x * 0.5f);
                    break;
                case EditMode.Scale:
                    float sf = Mathf.Clamp(1f + delta.x * 0.005f, 0.1f, 5f);
                    h.Transform.localScale = _dragScaleStart * sf;
                    break;
            }
        }

        private BoneHandle PickBone(Vector2 mousePos, Rect drawn)
        {
            BoneHandle best = null; float bestDist = PickRadius;
            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                Vector2 sp = WorldToViewport(h.Transform.position, drawn);
                float dist = Vector2.Distance(sp, mousePos);
                if (dist < bestDist) { bestDist = dist; best = h; }
            }

            return best;
        }
    }
}
