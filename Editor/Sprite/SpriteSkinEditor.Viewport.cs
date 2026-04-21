using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    public partial class SpriteSkinEditor
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

                // Advance playback time when playing
                if (_previewAnimator != null && _animPlaying && _clips != null && _selectedClipIdx < _clips.Length)
                {
                    if (_triggerMode)
                    {
                        // Animator state machine is driving playback (trigger mode).
                        // Sync our timeline to the Animator's state rather than fighting.
                        if (_previewAnimator.isInitialized)
                        {
                            var stateInfo = _previewAnimator.GetCurrentAnimatorStateInfo(0);
                            float clipLen = stateInfo.length;
                            _timelineNormTime = clipLen > 0f ? (stateInfo.normalizedTime % 1f) : 0f;
                            _playbackTime = _timelineNormTime * clipLen;
                        }
                    }
                    else
                    {
                        // Manual playback — advance time.
                        var clip = _clips[_selectedClipIdx];
                        float clipLen = clip.length;
                        _playbackTime += Time.deltaTime * _animSpeed;
                        if (clipLen > 0f)
                        {
                            if (clip.isLooping || clip.wrapMode == WrapMode.Loop)
                                _playbackTime %= clipLen;
                            else
                                _playbackTime = Mathf.Min(_playbackTime, clipLen);
                        }
                        else
                        {
                            _playbackTime = 0f;
                        }
                        _timelineNormTime = clipLen > 0 ? _playbackTime / clipLen : 0f;
                    }
                }

                // Drive Animator + apply override pipeline every frame.
                // Without this, the Animator can overwrite RefreshPreviewOverrides
                // results on subsequent frames, leaving bones without overrides.
                if (_previewAnimator != null && _clips != null && _selectedClipIdx < _clips.Length)
                {
                    if (!_triggerMode)
                    {
                        // Drive host Animator to the selected clip at current scrub time
                        EnsureAnimatorDriven();
                        int clipHash = Animator.StringToHash(_clips[_selectedClipIdx].name);
                        _previewAnimator.Play(clipHash, 0, _timelineNormTime);
                        _previewAnimator.Update(0f);
                    }

                    RestoreSRPropertiesToRest();
                    RestoreSkinRootsToPreOverride();
                    SyncGraftAnimatorsToHost();
                    FreezeGraftAncestors();
                    AlignGraftClones();
                    ApplyBoneOverridesToPreview_Fast();
                    ApplyKeyframeOverridesToPreview(
                        _clips[_selectedClipIdx].name, _timelineNormTime);
                }

                // Replay the in-progress drag on top of overrides so the user
                // sees the dragged position (pipeline just overwrote it).
                ReplayDrag();

                // Always align graft clones so they follow rig bone movement
                // (sprites like Head are at root level — their Transform doesn't move
                // when rig bones are dragged, only their mesh deforms via SpriteSkin).
                AlignGraftClones();

                _cam.Render();

                // Restore SR properties + skinRoots after rendering so additive
                // overrides don't compound. Bone transforms are left alone —
                // the Animator overwrites them next frame.
                if (_previewAnimator != null && _clips != null)
                {
                    RestoreSRPropertiesToRest();
                    RestoreSkinRootsToPreOverride();
                }
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
                    if (overrides?.RemovedBones.Contains(h.Name) == true) continue;
                    if (!handleByTransform.TryGetValue(h.Transform.parent, out var parentHandle)) continue;
                    if (overrides?.RemovedBones.Contains(parentHandle.Name) == true) continue;

                    Vector2 from = WorldToViewport(parentHandle.Transform.position, drawn);
                    Vector2 to = WorldToViewport(h.Transform.position, drawn);

                    // Skip lines fully outside the drawn rect
                    if (!drawn.Contains(from) && !drawn.Contains(to)) continue;

                    // Color: cyan for sprite bones, dim gray for rigging bones
                    bool isSpriteConnection = h.HasSpriteRenderer || parentHandle.HasSpriteRenderer;
                    Color lineColor = isSpriteConnection
                        ? new Color(0.3f, 0.7f, 0.85f, 0.5f)
                        : new Color(0.5f, 0.5f, 0.5f, 0.35f);

                    // Highlight lines connected to selected bone
                    if (h.Path == _selectedBonePath || parentHandle.Path == _selectedBonePath)
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
                if (overrides?.RemovedBones.Contains(h.Name) == true) continue;
                // Hide rigging-only bones in viewport unless toggled or selected
                bool selected = h.Path == _selectedBonePath;
                if (!h.HasSpriteRenderer && !_showRigBones && !selected) continue;
                if (h.HasSpriteRenderer && !_showSpriteDots && !selected) continue;
                Vector2 sp = WorldToViewport(h.Transform.position, drawn);
                if (!drawn.Contains(sp)) continue;

                bool hasOvr = false;
                if (overrides != null)
                {
                    if (h.GraftIndex >= 0 && h.GraftIndex < overrides.Grafts.Count)
                        hasOvr = overrides.Grafts[h.GraftIndex].BoneOverrides.ContainsKey(h.Name);
                    else
                        hasOvr = overrides.BoneOverrides.ContainsKey(h.Name);
                }
                float size = selected ? HandleSizeSel : (h.HasSpriteRenderer ? HandleSize : HandleSize * 0.6f);
                Texture2D tex = selected ? _dotSelected :
                                hasOvr ? _dotOverride :
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

            // ── Draw overlap Tab indicator ──
            if (_overlapCandidates.Count > 1 && _selectedBonePath != null)
            {
                var selH = _handles.Find(bh => bh.Path == _selectedBonePath);
                if (selH?.Transform != null)
                {
                    Vector2 sp = WorldToViewport(selH.Transform.position, drawn);
                    if (_overlapTooltipStyle == null)
                    {
                        _overlapTooltipStyle = new GUIStyle(GUI.skin.label)
                        {
                            fontSize = 10,
                            normal = { textColor = new Color(0.6f, 0.9f, 1f) },
                            alignment = TextAnchor.UpperLeft
                        };
                    }
                    string tip = $"Tab: {_overlapIndex + 1}/{_overlapCandidates.Count}";
                    GUI.Label(new Rect(sp.x + HandleSizeSel, sp.y + 6, 120, 16), tip, _overlapTooltipStyle);
                }
            }

            // ── Draw pivot crosshair for selected non-SpriteSkin sprite bone ──
            DrawPivotCrosshair(drawn, overrides);
        }

        private static GUIStyle _overlapTooltipStyle;
        private static Texture2D _pivotCrosshairTex;
        private static GUIStyle _pivotLabelStyle;

        /// <summary>Draw a magenta crosshair at the pivot point of the selected sprite bone
        /// (non-SpriteSkin only). Shows current pivot UV and "Alt+Drag to move" hint.</summary>
        private void DrawPivotCrosshair(Rect drawn, CharacterOverrideDef overrides)
        {
            if (_selectedBonePath == null) return;
            var h = _handles.Find(bh => bh.Path == _selectedBonePath);
            if (h == null || !h.HasSpriteRenderer) return;
            // Only for non-SpriteSkin sprites (SpriteSkin deformation ignores pivot)
            bool isSkinDeformed = h.SkinRootTransform != null && h.SkinRootTransform != h.Transform;
            if (isSkinDeformed) return;

            var sr = h.Transform.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return;

            // Get current pivot in world space
            // The bone transform position IS the pivot point in world space
            Vector2 boneScreen = WorldToViewport(h.Transform.position, drawn);
            if (!drawn.Contains(boneScreen)) return;

            // Draw crosshair
            if (_pivotCrosshairTex == null)
                _pivotCrosshairTex = ModEditor.MakeTex(2, 2, new Color(1f, 0.3f, 0.8f, 0.9f));

            float armLen = 10f, thick = 2f;
            // Horizontal arm
            GUI.DrawTexture(new Rect(boneScreen.x - armLen, boneScreen.y - thick / 2, armLen * 2, thick), _pivotCrosshairTex);
            // Vertical arm
            GUI.DrawTexture(new Rect(boneScreen.x - thick / 2, boneScreen.y - armLen, thick, armLen * 2), _pivotCrosshairTex);

            // Pivot UV label
            var sprite = sr.sprite;
            float pu = sprite.pivot.x / sprite.rect.width;
            float pv = sprite.pivot.y / sprite.rect.height;

            // Show override values if set
            Dictionary<string, BoneOverride> boneOvrs = null;
            if (overrides != null)
            {
                if (h.GraftIndex >= 0 && h.GraftIndex < overrides.Grafts.Count)
                    boneOvrs = overrides.Grafts[h.GraftIndex].BoneOverrides;
                else
                    boneOvrs = overrides.BoneOverrides;
            }
            BoneOverride bo = null;
            boneOvrs?.TryGetValue(h.Name, out bo);
            bool hasOverride = bo != null && (bo.PivotX >= 0f || bo.PivotY >= 0f);
            string label = hasOverride
                ? $"Pivot ({(bo.PivotX >= 0f ? bo.PivotX : pu):F2}, {(bo.PivotY >= 0f ? bo.PivotY : pv):F2})  Alt+Drag"
                : $"Pivot ({pu:F2}, {pv:F2})  Alt+Drag";

            if (_pivotLabelStyle == null)
                _pivotLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    normal = { textColor = new Color(1f, 0.3f, 0.8f, 0.85f) }
                };
            GUI.Label(new Rect(boneScreen.x + 12, boneScreen.y - 18, 200, 16), label, _pivotLabelStyle);
        }

        private void DrawToolbar(Rect vp)
        {
            float bw = 32f, bh = 22f, gap = 2f;
            float tx = vp.x + 8, ty = vp.y + 6;

            var modes = new[] { ("W", EditMode.Move), ("E", EditMode.Rotate), ("R", EditMode.Scale) };
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

            if (GUI.Button(new Rect(tx, ty, 58, bh), _showSpriteDots ? "Dots ON" : "Dots OFF", EditorStyles.MiniButton))
                _showSpriteDots = !_showSpriteDots;
            tx += 62;

            if (GUI.Button(new Rect(tx, ty, 70, bh), "Reset View", EditorStyles.MiniButton))
            { _zoom = 2.5f; _pan = Vector2.zero; }

            // Show effective mode (accounts for modifier-drag override)
            EditMode displayMode = _dragging ? _dragEffectiveMode : _mode;
            string modeText = displayMode switch
            {
                EditMode.Move => "MOVE", EditMode.Rotate => "ROTATE",
                EditMode.Scale => "SCALE", _ => ""
            };
            GUI.Label(new Rect(vp.xMax - 76, ty, 70, bh),
                $"<color=cyan>{modeText}</color>", EditorStyles.RichLabel);

            // ── Shortcut hints (bottom-left of viewport) ─────────────
            float hy = vp.yMax - TimelineH - 18;
            if (_shortcutHintStyle == null)
                _shortcutHintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    normal = { textColor = new Color(0.45f, 0.45f, 0.5f) }
                };
            GUI.Label(new Rect(vp.x + 8, hy, vp.width - 16, 16),
                "W/E/R: Move/Rotate/Scale   Shift+Drag: Rotate   Ctrl+Drag: Scale   Alt+Drag: Pivot   Del: Remove   X: Clear   F: Frame",
                _shortcutHintStyle);
        }

        private GUIStyle _shortcutHintStyle;

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
            else
            {
                // Pausing: leave trigger mode and disable Animator so it stops
                // driving sprite property curves behind our back.
                LeaveTriggerMode();
            }
        }

        /// <summary>
        /// Exit trigger mode: re-evaluate the current clip at the current time
        /// to freeze the pose.
        /// </summary>
        private void LeaveTriggerMode()
        {
            if (!_triggerMode) return;
            _triggerMode = false;
            if (_previewAnimator != null)
                _previewAnimator.enabled = false;
            DisableGraftAnimators();

            // Resample via Animator so the preview reflects the paused state.
            if (_clips != null && _selectedClipIdx < _clips.Length && _previewGO != null)
            {
                var clip = _clips[_selectedClipIdx];
                EnsureAnimatorDriven();
                int clipHash = Animator.StringToHash(clip.name);
                float normTime = clip.length > 0f ? _playbackTime / clip.length : 0f;
                _previewAnimator.Play(clipHash, 0, normTime);
                _previewAnimator.Update(0f);
                RestoreSRPropertiesToRest();
                RestoreSkinRootsToPreOverride();
                SyncGraftAnimatorsToHost();
                FreezeGraftAncestors();
                AlignGraftClones();
                ApplyBoneOverridesToPreview_Fast();
                ApplyKeyframeOverridesToPreview(clip.name, _timelineNormTime);
            }
        }

        /// <summary>
        /// Enter trigger mode: enable the Animator state machine and fire a trigger.
        /// The Animator drives both bone transforms AND sprite property curves
        /// (m_Sprite bindings).
        /// </summary>
        private void EnterTriggerMode(string triggerName)
        {
            if (_previewAnimator == null) return;
            _triggerMode = true;
            _animPlaying = true;
            _previewAnimator.enabled = true;
            _previewAnimator.speed = _animSpeed;
            _previewAnimator.SetTrigger(triggerName);
        }

        /// <summary>Ensure the host Animator is enabled with speed=0 for manual driving
        /// via Play(hash, 0, normTime) + Update(0). Used by scrub and play paths.</summary>
        private void EnsureAnimatorDriven()
        {
            if (_previewAnimator == null) return;
            if (!_previewAnimator.enabled)
                _previewAnimator.enabled = true;
            _previewAnimator.speed = 0f;
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

        private void ScrubToNormTime(float normTime)
        {
            _timelineNormTime = Mathf.Clamp01(normTime);
            if (_previewGO == null || _clips == null || _selectedClipIdx >= _clips.Length) return;

            // Leave trigger mode if active — scrubbing uses direct Animator.Play
            LeaveTriggerMode();

            // Pause playback when scrubbing
            _animPlaying = false;
            var clip = _clips[_selectedClipIdx];
            _playbackTime = normTime * clip.length;

            // Drive the Animator to the selected clip at the scrubbed time
            EnsureAnimatorDriven();
            int clipHash = Animator.StringToHash(clip.name);
            _previewAnimator.Play(clipHash, 0, normTime);
            _previewAnimator.Update(0f);

            RestoreSRPropertiesToRest();
            RestoreSkinRootsToPreOverride();
            SyncGraftAnimatorsToHost();
            FreezeGraftAncestors();
            AlignGraftClones();
            ApplyBoneOverridesToPreview_Fast();
            ApplyKeyframeOverridesToPreview(clip.name, normTime);
        }

        /// <summary>Lightweight per-frame version of ApplyBoneOverridesToPreview.
        /// Called during playback/scrub after Animator evaluation to re-apply bone overrides.
        /// Falls through to the full method if an override exists.</summary>
        private void ApplyBoneOverridesToPreview_Fast()
        {
            var sprites = GetSpriteDict();
            if (sprites == null || _previewNpcId == null) return;
            if (!sprites.TryGetValue(_previewNpcId, out var ovr)) return;
            ApplyBoneOverridesToPreview(ovr);
            ReStampCustomSprites();
        }

        /// <summary>Re-stamp cached custom sprites after Animator overwrites
        /// sr.sprite each frame via animation curves. Matches the runtime fix in
        /// CharacterOverrideDriver.LateUpdate.</summary>
        private void ReStampCustomSprites()
        {
            // Host custom sprites
            foreach (var kvp in _edCustomSpriteCache)
            {
                if (_edSrMap.TryGetValue(kvp.Key, out var sr) && sr != null)
                    sr.sprite = kvp.Value;
            }

            // Graft custom sprites
            for (int gi = 0; gi < _graftCustomSpriteCache.Count && gi < _graftPreviewObjects.Count; gi++)
            {
                var clone = _graftPreviewObjects[gi];
                if (clone == null) continue;
                foreach (var kvp in _graftCustomSpriteCache[gi])
                {
                    var boneT = BoneHierarchyUtils.FindRecursive(clone.transform, kvp.Key);
                    if (boneT == null) continue;
                    var sr = boneT.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.sprite = kvp.Value;
                }
            }
        }

        /// <summary>Apply animation keyframe overrides to the preview after Animator evaluation.
        /// Delegates to the shared CharacterOverrideDriver.ApplyKeyframeOverrides static method
        /// which uses SET for non-Animator bones and ADDITIVE for Animator-driven bones,
        /// matching runtime behavior exactly.</summary>
        private void ApplyKeyframeOverridesToPreview(string clipName, float normTime)
        {
            var sprites = GetSpriteDict();
            if (sprites == null || _previewNpcId == null) return;
            if (!sprites.TryGetValue(_previewNpcId, out var ovr)) return;

            // Host-level keyframe overrides
            if (ovr.AnimOverrides != null && ovr.AnimOverrides.TryGetValue(clipName, out var animOvr))
            {
                CharacterOverrideDriver.ApplyKeyframeOverrides(
                    animOvr.BoneKeyframes, normTime, _edBoneMap, null, _edSkinRootMap);
            }

            // Graft-scoped keyframe overrides (additive on top of SET BoneOverrides)
            for (int gi = 0; gi < ovr.Grafts.Count && gi < _graftPreviewObjects.Count; gi++)
            {
                var graft = ovr.Grafts[gi];
                var clone = _graftPreviewObjects[gi];
                if (clone == null || graft.AnimOverrides == null || graft.AnimOverrides.Count == 0) continue;
                if (!graft.AnimOverrides.TryGetValue(clipName, out var gAnimOvr)) continue;

                // Build bone + skinRoot maps for this graft clone
                var gBoneMap = new Dictionary<string, Transform>();
                var gSkinRootMap = new Dictionary<string, Transform>();
                foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
                {
                    gBoneMap[t.name] = t;
                    var skin = t.GetComponent<SpriteSkin>();
                    if (skin != null && skin.rootBone != null)
                        gSkinRootMap[t.name] = skin.rootBone;
                }

                // Additive mode — keyframes layer deltas on top of the SET BoneOverride base.
                // skinRootMap redirects sprite names (e.g. Cabeza) to controlling bone (bone_3).
                CharacterOverrideDriver.ApplyKeyframeOverrides(
                    gAnimOvr.BoneKeyframes, normTime, gBoneMap, null, gSkinRootMap);
            }
        }

        /// <summary>Sample the current clip at 11 time points and record the selected bone's transform.</summary>
        private void SampleBaseKeyframes()
        {
            if (_clips == null || _selectedClipIdx >= _clips.Length || _selectedBone == null || _previewGO == null) return;
            if (_previewAnimator == null) return;
            var bh = _handles.Find(b => b.Path == _selectedBonePath);
            if (bh?.Transform == null) return;

            float savedNorm = _timelineNormTime;
            bool savedPlaying = _animPlaying;
            var clip = _clips[_selectedClipIdx];
            int clipHash = Animator.StringToHash(clip.name);

            EnsureAnimatorDriven();

            const int samples = 11;
            var results = new SampledKf[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                _previewAnimator.Play(clipHash, 0, t);
                _previewAnimator.Update(0f);

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
            _previewAnimator.Play(clipHash, 0, savedNorm);
            _previewAnimator.Update(0f);
            _animPlaying = savedPlaying;

            _sampledKeyframes = results;
            _sampledBone = _selectedBone;
            _sampledClip = _selectedClipIdx;

            Plugin.Log.LogInfo($"[SpriteSkinEditor] Sampled {samples} keyframes for bone '{_selectedBone}' in clip '{clip.name}'");
        }

    }
}
