using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Visual sprite editor for NPC models. Renders the NPC's animated prefab
    /// into a RenderTexture viewport with draggable bone handles for adjusting
    /// transforms. Overrides stored in ZoneDef.Sprites.
    /// </summary>
    public class SpriteEditor
    {
        private readonly ZoneEditor _parent;

        // ── Preview ──────────────────────────────────────────────────
        private Camera _cam;
        private RenderTexture _rt;
        private GameObject _previewGO;
        private string _previewNpcId;
        private int _lastRenderFrame = -1;

        // ── Bones ────────────────────────────────────────────────────
        private List<BoneHandle> _handles = new();
        private Dictionary<string, Vector3> _restPos = new();
        private Dictionary<string, float> _restRot = new();
        private Dictionary<string, Vector3> _restScale = new();
        private Dictionary<string, Sprite> _restSprites = new();
        private Dictionary<string, Material> _restMaterials = new();
        private string _selectedBone;
        private int _pendingSrcIdx = -1;   // remembers source button click while dropdown value is empty
        private string _pendingSrcBone;   // which bone the pending source applies to

        // ── Viewport interaction ─────────────────────────────────────
        private float _zoom = 2.5f;
        private Vector2 _pan;
        private bool _dragging;
        private Vector2 _dragMouseStart;
        private Vector3 _dragLocalStart;
        private float _dragRotStart;
        private Vector3 _dragScaleStart;
        private bool _panning;
        private Vector2 _panMouseStart;
        private Vector2 _panStart;
        private EditMode _mode = EditMode.Move;
        private bool _showAllLabels;
        private bool _showRigBones = true;

        // ── NPC Builder ──────────────────────────────────────────────
        private string _fillFromNpc = "";
        private string _newSpriteId = "";

        // ── Animation playback (editor-only, not persisted) ──────────
        private bool _animPlaying;
        private float _animSpeed = 1f;
        private string[] _clipNames;
        private float[] _clipLengths;
        private AnimationClip[] _clips; // actual clip objects for SampleAnimation
        private int _selectedClipIdx;
        private Animator _previewAnimator;
        private float _playbackTime; // manual time counter for playback

        // ── Timeline state ───────────────────────────────────────────
        private float _timelineNormTime;      // 0..1 scrub position
        private bool _timelineDragging;
        private const float TimelineH = 36f;  // height of the timeline bar
        private const float ScrubTrackY = 20f; // y-offset of scrub track within timeline

        // ── Base keyframe sampling ───────────────────────────────────
        private struct SampledKf { public float Time, PosX, PosY, Rot, ScaleX, ScaleY; }
        private SampledKf[] _sampledKeyframes;
        private string _sampledBone;
        private int _sampledClip = -1;

        // ── Panel state ──────────────────────────────────────────────
        private bool _secModel = true;
        private bool _secBone = true;
        private bool _secBuilder = true;
        private bool _secBones = true;
        private bool _secAnim = true;
        private bool _secBaseKf = false;
        private bool _secEffects = false;
        private bool _secShader = false;
        private bool _secAdded = false;
        private string _addedSpriteName = "";
        private List<GameObject> _addedPreviewObjects = new();
        private List<GameObject> _graftedBranchObjects = new();
        private bool _secAddBone = false;
        private string _addedBoneName = "";

        private static Texture2D _vpBgTex;

        // ── Constants ────────────────────────────────────────────────
        private enum EditMode { Move, Rotate, Scale }
        private static readonly Vector3 PreviewOrigin = new(-5000f, 0f, 0f);
        private const int RT_W = 1024, RT_H = 768;
        private const float HandleSize = 8f, HandleSizeSel = 12f, PickRadius = 14f;

        // ── Handle textures ──────────────────────────────────────────
        private static Texture2D _dotDefault, _dotSprite, _dotSelected, _dotOverride;
        private static Material _lineMaterial;

        // ── Sprite caches (static, shared across editor & runtime) ──
        private static readonly Dictionary<string, Dictionary<string, Sprite>> _graftSpriteCache = new();
        private static readonly Dictionary<string, Texture2D> _textureCache = new();

        // ── Shader cache ─────────────────────────────────────────────
        private static Shader _allIn1Shader;
        private static bool _shaderSearched;
        private List<Material> _shaderMaterials = new(); // shader mats created for preview (for cleanup)

        // ── Cached base alpha per bone (to avoid per-frame alpha drift) ──
        private Dictionary<string, float> _basePreviewAlpha = new();

        // ── Cached textures & styles (avoid per-frame allocation) ────
        private static Texture2D _tlBgTex, _tlBorderTex, _tlTrackBgTex, _tlFillTex;
        private static Texture2D _tlHeadTex, _tlKfDiamondTex, _tlKfDiamondSelTex, _tlTrackBorderTex;
        private GUIStyle _centeredStyle, _noAnimStyle, _clipStyle, _timeStyle, _speedStyle;
        private GUIStyle _boneLabelStyle, _boneLabelSelStyle;

        public SpriteEditor(ZoneEditor parent) => _parent = parent;

        // ── Mod-project integration ──────────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideBrowserScroll;
        private string _overrideBrowserFilter = "";

        /// <summary>
        /// Get the active sprite dictionary. Returns mod-project sprites when
        /// a mod project is active (Sprites tab), otherwise falls back to
        /// ZoneLoader.CurrentZone.Sprites for zone-scoped editing.
        /// </summary>
        private Dictionary<string, SpriteOverrideDef> GetSpriteDict()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null)
            {
                // Provide a unified view: new sprites + patch sprites
                // The panel resolves which dict to use per-entity
                if (_mergedSprites == null) RebuildMergedSprites(proj);
                return _mergedSprites;
            }
            return ZoneLoader.CurrentZone?.Sprites;
        }

        private Dictionary<string, SpriteOverrideDef> _mergedSprites;

        private void RebuildMergedSprites(ModProject proj)
        {
            _mergedSprites = new Dictionary<string, SpriteOverrideDef>();
            foreach (var kvp in proj.Sprites)
                _mergedSprites[kvp.Key] = kvp.Value;
            foreach (var kvp in proj.SpritePatches)
                _mergedSprites[kvp.Key] = kvp.Value;
        }

        /// <summary>Mark sprite data as modified. Saves to mod project if active, else ZoneLoader.</summary>
        private void OnSpriteModified()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(_previewNpcId))
            {
                bool isPatch = proj.SpritePatches.ContainsKey(_previewNpcId);
                SpriteOverrideDef def = null;
                if (proj.Sprites.TryGetValue(_previewNpcId, out def) ||
                    proj.SpritePatches.TryGetValue(_previewNpcId, out def))
                {
                    ModProjectLoader.SaveEntity(proj, "sprites", _previewNpcId, def, isPatch);
                }
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
                _mergedSprites = null; // force rebuild on next access
            }
            else
            {
                OnSpriteModified();
            }
        }

        /// <summary>Get NPC IDs for the "used by" display. Uses zone or mod project.</summary>
        private Dictionary<string, NpcDef> GetNpcDict()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null)
            {
                // Merge new + patched NPCs
                var merged = new Dictionary<string, NpcDef>();
                foreach (var kvp in proj.Npcs) merged[kvp.Key] = kvp.Value;
                foreach (var kvp in proj.NpcPatches) merged[kvp.Key] = kvp.Value;
                return merged;
            }
            return ZoneLoader.CurrentZone?.Npcs;
        }

        /// <summary>Get a zone ID for texture paths. Uses mod project folder if active.</summary>
        private string GetTextureZoneId()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null) return proj.ModId;
            return ZoneLoader.CurrentZone?.ZoneId ?? "";
        }

        // ═══════════════════════════════════════════════════════════════
        //  VIEWPORT (drawn on left side of screen by ZoneEditor)
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
            if (_vpBgTex == null) _vpBgTex = ZoneEditor.MakeTex(2, 2, new Color(0.1f, 0.1f, 0.13f, 1f));
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
            if (_tlBgTex == null) _tlBgTex = ZoneEditor.MakeTex(2, 2, new Color(0.08f, 0.08f, 0.10f, 1f));
            GUI.DrawTexture(r, _tlBgTex);

            // Top border line (cached)
            if (_tlBorderTex == null) _tlBorderTex = ZoneEditor.MakeTex(2, 1, new Color(0.25f, 0.25f, 0.3f, 1f));
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
            if (_tlTrackBgTex == null) _tlTrackBgTex = ZoneEditor.MakeTex(2, 2, new Color(0.15f, 0.15f, 0.18f, 1f));
            GUI.DrawTexture(new Rect(trackX, trackY, trackW, trackH), _tlTrackBgTex);

            // Filled portion (progress)
            float fillW = trackW * Mathf.Clamp01(_timelineNormTime);
            if (fillW > 1f)
            {
                if (_tlFillTex == null) _tlFillTex = ZoneEditor.MakeTex(2, 2, new Color(0.3f, 0.55f, 0.8f, 0.8f));
                GUI.DrawTexture(new Rect(trackX, trackY, fillW, trackH), _tlFillTex);
            }

            // Playhead (scrubber handle)
            float headX = trackX + fillW - 4f;
            float headW = 8f, headH = trackH + 4f;
            if (_tlHeadTex == null) _tlHeadTex = ZoneEditor.MakeTex(2, 2, new Color(0.9f, 0.9f, 0.95f, 1f));
            GUI.DrawTexture(new Rect(headX, trackY - 2, headW, headH), _tlHeadTex);

            // Keyframe diamonds on track (for current clip + selected bone)
            var tlSprites = GetSpriteDict();
            if (tlSprites != null && _previewNpcId != null &&
                tlSprites.TryGetValue(_previewNpcId, out var kfOvr) &&
                kfOvr.AnimOverrides.TryGetValue(clipName, out var kfAnimOvr))
            {
                if (_tlKfDiamondTex == null) _tlKfDiamondTex = ZoneEditor.MakeTex(2, 2, new Color(1f, 0.7f, 0.2f, 0.9f));
                if (_tlKfDiamondSelTex == null) _tlKfDiamondSelTex = ZoneEditor.MakeTex(2, 2, new Color(0.3f, 0.9f, 1f, 1f));

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
            if (_tlTrackBorderTex == null) _tlTrackBorderTex = ZoneEditor.MakeTex(2, 2, new Color(0.3f, 0.3f, 0.35f, 1f));
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

        // ═══════════════════════════════════════════════════════════════
        //  PANEL (right side)
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null)
            {
                DrawModProjectPanel(proj);
                return;
            }

            // Legacy zone-scoped mode (no mod project active)
            var zone = ZoneLoader.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            // ── Sprite definition selector ───────────────────────────
            var spriteIds = zone.Sprites.Keys.OrderBy(k => k).ToList();
            string sel = EditorFields.EntitySelector(
                _previewNpcId, spriteIds,
                id => {
                    var s = zone.Sprites[id];
                    return $"{id}  [{(string.IsNullOrEmpty(s.BaseSprite) ? "?" : s.BaseSprite)}]";
                },
                "spr_sel");
            if (sel != _previewNpcId)
            {
                if (!string.IsNullOrEmpty(sel) && zone.Sprites.TryGetValue(sel, out var sDef))
                {
                    string baseNpc = !string.IsNullOrEmpty(sDef.BaseSprite) ? sDef.BaseSprite : sel;
                    SpawnPreview(sel, baseNpc);
                }
                else
                    DestroyPreview();
            }

            // ── Create new sprite definition ─────────────────────────
            GUILayout.BeginHorizontal();
            _newSpriteId = EditorFields.TextField("New Sprite", _newSpriteId ?? "");
            if (GUILayout.Button("Create", GUILayout.Width(60)) &&
                !string.IsNullOrEmpty(_newSpriteId) && !zone.Sprites.ContainsKey(_newSpriteId))
            {
                var newDef = new SpriteOverrideDef { NpcId = _newSpriteId };
                zone.Sprites[_newSpriteId] = newDef;
                OnSpriteModified();
                SpawnPreview(_newSpriteId, "");
            }
            GUILayout.EndHorizontal();

            EditorStyles.Separator();

            DrawSpriteEditorBody();
        }

        /// <summary>Mod-project-scoped sprite panel with entity selector, badges, override browser.</summary>
        private void DrawModProjectPanel(ModProject proj)
        {
            _mergedSprites = null; // force rebuild

            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Sprites.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.SpritePatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                _previewNpcId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    SpriteOverrideDef s = null;
                    if (proj.Sprites.TryGetValue(id, out s) || proj.SpritePatches.TryGetValue(id, out s))
                    {
                        string baseSpr = !string.IsNullOrEmpty(s?.BaseSprite) ? s.BaseSprite : "?";
                        return $"{badge} {id}  [{baseSpr}]";
                    }
                    return $"{badge} {id}";
                },
                "spr_sel");
            if (sel != _previewNpcId)
            {
                SpriteOverrideDef sDef = null;
                if (!string.IsNullOrEmpty(sel))
                {
                    if (!proj.Sprites.TryGetValue(sel, out sDef))
                        proj.SpritePatches.TryGetValue(sel, out sDef);
                }
                if (sDef != null)
                {
                    string baseNpc = !string.IsNullOrEmpty(sDef.BaseSprite) ? sDef.BaseSprite : sel;
                    SpawnPreview(sel, baseNpc);
                }
                else
                    DestroyPreview();
            }

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();


            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_sprite";
                int suffix = 1;
                while (proj.Sprites.ContainsKey(newId) || proj.SpritePatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_sprite{suffix++}";
                var def = new SpriteOverrideDef { NpcId = newId };
                proj.Sprites[newId] = def;
                _previewNpcId = newId;
                ModProjectLoader.SaveEntity(proj, "sprites", newId, def);
                proj.IsDirty = true;
                _mergedSprites = null;
                SpawnPreview(newId, "");
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(_previewNpcId))
            {
                bool isNew = proj.Sprites.ContainsKey(_previewNpcId);
                bool isOvr = proj.SpritePatches.ContainsKey(_previewNpcId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Sprites.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "sprites", _previewNpcId, false);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != _previewNpcId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.SpritePatches.Remove(_previewNpcId);
                        ModProjectLoader.DeleteEntity(proj, "sprites", _previewNpcId, true);
                        DestroyPreview();
                        _previewNpcId = allIds.FirstOrDefault(k => k != _previewNpcId);
                        proj.IsDirty = true;
                        _mergedSprites = null;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Override browser ─────────────────────────────────
            if (_showOverrideBrowser)
                DrawSpriteOverrideBrowser(proj);

            EditorStyles.Separator();

            DrawSpriteEditorBody();
        }

        /// <summary>Override browser for base-game NPC sprites.</summary>
        private void DrawSpriteOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game NPCs to override sprite:</color>",
                EditorStyles.RichLabel);
            _overrideBrowserFilter = EditorFields.TextField("Filter", _overrideBrowserFilter);

            _overrideBrowserScroll = GUILayout.BeginScrollView(_overrideBrowserScroll, GUILayout.Height(180));
            string filterLow = (_overrideBrowserFilter ?? "").ToLower();
            var allNpcIds = DataHelper.GetAllNpcIds();
            int shown = 0;
            foreach (var id in allNpcIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.SpritePatches.ContainsKey(id) || proj.Sprites.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    // Create a new patch sprite def for this NPC
                    var def = new SpriteOverrideDef { NpcId = id, BaseSprite = id };
                    proj.SpritePatches[id] = def;
                    _previewNpcId = id;
                    ModProjectLoader.SaveEntity(proj, "sprites", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                    _mergedSprites = null;
                    SpawnPreview(id, id);
                }
            }
            GUILayout.EndScrollView();
        }

        /// <summary>
        /// The main body of the sprite editor (bone editing, shader, animation, etc.)
        /// Shared between zone-scoped and mod-project-scoped modes.
        /// </summary>
        private void DrawSpriteEditorBody()
        {
            if (_previewGO == null || _previewNpcId == null)
            {
                GUILayout.Label("<i>Select or create a sprite definition above.</i>", EditorStyles.RichLabel);
                return;
            }

            // Get/create sprite def from active data source
            var sprites = GetSpriteDict();
            if (sprites == null)
            {
                GUILayout.Label("<i>No sprite storage available.</i>", EditorStyles.RichLabel);
                return;
            }
            if (!sprites.TryGetValue(_previewNpcId, out var ovr))
            {
                ovr = new SpriteOverrideDef { NpcId = _previewNpcId };
                sprites[_previewNpcId] = ovr;
                // Also add to the correct mod project dict if active
                var proj = ModManagerPanel.ActiveProject;
                if (proj != null && !proj.Sprites.ContainsKey(_previewNpcId) && !proj.SpritePatches.ContainsKey(_previewNpcId))
                    proj.Sprites[_previewNpcId] = ovr;
            }

            // NPC Builder section handles spritesheet display

            EditorStyles.Separator();

            // ── Sprite Builder ───────────────────────────────────────
            if (EditorFields.Section("Sprite Builder", ref _secBuilder))
            {
                // BaseSprite (skeleton donor NPC)
                string prevBase = ovr.BaseSprite;
                ovr.BaseSprite = EditorFields.IdDropdown("Base Sprite", ovr.BaseSprite, DataHelper.GetAllNpcIds(), "spr_base");
                if (ovr.BaseSprite != prevBase)
                {
                    OnSpriteModified();
                    if (!string.IsNullOrEmpty(ovr.BaseSprite))
                        SpawnPreview(_previewNpcId, ovr.BaseSprite);
                }

                // Show which NPCs reference this sprite definition
                var npcDict = GetNpcDict();
                var referencingNpcs = npcDict != null
                    ? npcDict.Where(kvp => kvp.Value.SpriteSource == _previewNpcId).Select(kvp => kvp.Key).ToList()
                    : new List<string>();
                GUILayout.Label($"<color=#888>Used by NPCs:</color> <b>{(referencingNpcs.Count > 0 ? string.Join(", ", referencingNpcs) : "<color=#666>none</color>")}</b>",
                    EditorStyles.RichLabel);

                // Sprite bones summary
                var sprBones = _handles.Where(h => h.HasSpriteRenderer).ToList();
                int graftCount = sprBones.Count(h => ovr.Bones.ContainsKey(h.Name) && !string.IsNullOrEmpty(ovr.Bones[h.Name].SpriteFrom));
                int customCount = sprBones.Count(h => ovr.CustomSprites.ContainsKey(h.Name));
                int hiddenCount = sprBones.Count(h => ovr.Bones.ContainsKey(h.Name) && !ovr.Bones[h.Name].Visible);
                int originalCount = sprBones.Count - graftCount - customCount;
                GUILayout.Label(
                    $"<color=#888>Sprites: <color=#ddd>{sprBones.Count}</color> total · " +
                    $"<color=#aaa>{originalCount}</color> original · " +
                    $"<color=#f80>{graftCount}</color> grafted · " +
                    $"<color=#8cf>{customCount}</color> custom · " +
                    $"<color=#666>{hiddenCount}</color> hidden</color>",
                    EditorStyles.RichLabel);

                GUILayout.Space(4);

                // Spritesheet (for custom sprites)
                bool hasCS = ovr.CustomSprites.Count > 0 || !string.IsNullOrEmpty(ovr.Spritesheet);
                if (hasCS)
                {
                    GUILayout.Label("<color=#888>Place PNGs in: <b>textures/</b> under zone folder</color>", EditorStyles.RichLabel);
                    string prevSheet = ovr.Spritesheet;
                    ovr.Spritesheet = EditorFields.TextField("Spritesheet", ovr.Spritesheet);
                    if (ovr.Spritesheet != prevSheet)
                    { RefreshPreviewOverrides(); OnSpriteModified(); }
                    GUILayout.Space(4);
                }

                // ── Bulk Actions ──
                GUILayout.Label("<color=#aad><b>Bulk Actions</b></color>", EditorStyles.RichLabel);

                // Fill All Sprites From NPC
                GUILayout.BeginHorizontal();
                GUILayout.Label("Fill from NPC:", GUILayout.Width(90));
                var allNpcIds = DataHelper.GetAllNpcIds();
                var localNpcDict = GetNpcDict();
                var localNpcIds = localNpcDict != null ? localNpcDict.Keys.OrderBy(k => k).ToList() : new List<string>();
                var mergedIds = new List<string>(localNpcIds);
                foreach (var nid in allNpcIds)
                    if (!mergedIds.Contains(nid)) mergedIds.Add(nid);
                string fillNpc = EditorFields.IdDropdown("", _fillFromNpc, mergedIds, "builder_fill_npc");
                if (fillNpc != _fillFromNpc) _fillFromNpc = fillNpc;
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_fillFromNpc))
                {
                    if (GUILayout.Button($"Fill All Sprites from '{_fillFromNpc}'", EditorStyles.MiniButton))
                    {
                        FillAllSpritesFrom(ovr, _fillFromNpc);
                        RefreshPreviewOverrides(); OnSpriteModified();
                    }
                    if (GUILayout.Button($"Import Bones from '{_fillFromNpc}'", EditorStyles.MiniButton))
                    {
                        ImportBonesFrom(ovr, _fillFromNpc);
                        RefreshPreviewOverrides(); OnSpriteModified();
                    }
                }

                // Clear all grafts
                if (graftCount > 0 || customCount > 0)
                {
                    GUILayout.Space(2);
                    if (GUILayout.Button("Clear All Sprite Sources", EditorStyles.DangerButton))
                    {
                        foreach (var bo in ovr.Bones.Values)
                            bo.SpriteFrom = "";
                        ovr.CustomSprites.Clear();
                        RefreshPreviewOverrides(); OnSpriteModified();
                    }
                }

                // Hide all sprites (make invisible base)
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Hide All Sprites", EditorStyles.MiniButton))
                {
                    foreach (var h in sprBones)
                    {
                        if (!ovr.Bones.ContainsKey(h.Name))
                            ovr.Bones[h.Name] = new BoneOverride();
                        ovr.Bones[h.Name].Visible = false;
                    }
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                if (GUILayout.Button("Show All Sprites", EditorStyles.MiniButton))
                {
                    foreach (var h in sprBones)
                    {
                        if (ovr.Bones.ContainsKey(h.Name))
                            ovr.Bones[h.Name].Visible = true;
                    }
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                GUILayout.EndHorizontal();

                // Remove all bones (populate RemovedBones — deactivates bone GOs at runtime)
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Remove All Bones", EditorStyles.DangerButton))
                {
                    foreach (var h in _handles)
                        ovr.RemovedBones.Add(h.Name);
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                if (ovr.RemovedBones.Count > 0 && GUILayout.Button("Restore All Bones", EditorStyles.MiniButton))
                {
                    ovr.RemovedBones.Clear();
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // ── Animation Source ──
                GUILayout.Label("<color=#aad><b>Animation Source</b></color>", EditorStyles.RichLabel);
                GUILayout.Label("<color=#888>Use animations from a different NPC (keeps skeleton's state machine)</color>", EditorStyles.RichLabel);
                string prevAnimSrc = ovr.AnimationSource;
                ovr.AnimationSource = EditorFields.IdDropdown("Anim From", ovr.AnimationSource, mergedIds, "builder_anim_src");
                if (ovr.AnimationSource != prevAnimSrc)
                    OnSpriteModified();
            }

            // ── Model overrides ──────────────────────────────────────
            if (EditorFields.Section("Model Overrides", ref _secModel))
            {
                float ps = ovr.ScaleMultiplier, pox = ovr.OffsetX, poy = ovr.OffsetY;
                ovr.ScaleMultiplier = EditorFields.FloatField("Scale", ovr.ScaleMultiplier);
                ovr.OffsetX = EditorFields.FloatField("Offset X", ovr.OffsetX);
                ovr.OffsetY = EditorFields.FloatField("Offset Y", ovr.OffsetY);
                if (ovr.ScaleMultiplier != ps || ovr.OffsetX != pox || ovr.OffsetY != poy)
                { RefreshPreviewOverrides(); OnSpriteModified(); }
            }

            // ── Selected bone ────────────────────────────────────────
            if (EditorFields.Section("Selected Bone", ref _secBone))
            {
                if (_selectedBone == null)
                {
                    GUILayout.Label("<i>Click a bone in the viewport.</i>", EditorStyles.RichLabel);
                }
                else
                {
                    GUILayout.Label($"<b>{_selectedBone}</b>", EditorStyles.RichLabel);
                    var h = _handles.Find(b => b.Name == _selectedBone);
                    if (h?.Transform != null)
                    {
                        GUILayout.Label(
                            $"<color=#888>pos=({h.Transform.localPosition.x:F3}, {h.Transform.localPosition.y:F3}) " +
                            $"rot={h.Transform.localEulerAngles.z:F1} " +
                            $"scale=({h.Transform.localScale.x:F2}, {h.Transform.localScale.y:F2})</color>",
                            EditorStyles.RichLabel);
                    }

                    // ── Sprite Source (unified dropdown for sprite bones) ──
                    if (h?.HasSpriteRenderer == true)
                    {
                        if (!ovr.Bones.TryGetValue(_selectedBone, out var boCur))
                            boCur = null;

                        // Determine current source type
                        bool hasGraft = boCur != null && !string.IsNullOrEmpty(boCur.SpriteFrom);
                        bool hasCustom = ovr.CustomSprites.ContainsKey(_selectedBone);
                        bool isHidden = boCur != null && !boCur.Visible;

                        // Clear pending if bone changed
                        if (_pendingSrcBone != _selectedBone) _pendingSrcIdx = -1;

                        int srcIdx = 0; // Original
                        if (hasGraft) srcIdx = 1;      // NPC
                        else if (hasCustom) srcIdx = 2; // Custom
                        else if (isHidden) srcIdx = 3;  // Hidden

                        // Override with pending source (user clicked button but value not yet chosen)
                        if (_pendingSrcIdx >= 0 && _pendingSrcBone == _selectedBone)
                            srcIdx = _pendingSrcIdx;

                        var srcNames = new[] { "Original", "NPC", "Custom", "Hidden" };
                        var srcColors = new[] { "#8d8", "#f80", "#8cf", "#888" };

                        GUILayout.Space(4);
                        GUILayout.BeginHorizontal(GUILayout.Height(22));
                        GUILayout.Label("Source:", GUILayout.Width(50));
                        for (int si = 0; si < srcNames.Length; si++)
                        {
                            Color prevCol = GUI.color;
                            if (si == srcIdx) GUI.color = new Color(0.5f, 0.8f, 1f);
                            if (GUILayout.Button(srcNames[si], EditorStyles.MiniButton, GUILayout.Width(60)))
                            {
                                if (si != srcIdx)
                                {
                                    // Clear previous source
                                    if (hasGraft && boCur != null) boCur.SpriteFrom = "";
                                    if (hasCustom) ovr.CustomSprites.Remove(_selectedBone);
                                    if (isHidden && boCur != null) boCur.Visible = true;

                                    // Set new source
                                    switch (si)
                                    {
                                        case 1: // NPC
                                            if (boCur == null) { boCur = new BoneOverride(); ovr.Bones[_selectedBone] = boCur; }
                                            boCur.SpriteFrom = ""; // dropdown will set it
                                            break;
                                        case 2: // Custom
                                            ovr.CustomSprites[_selectedBone] = new SpriteDef();
                                            break;
                                        case 3: // Hidden
                                            if (boCur == null) { boCur = new BoneOverride(); ovr.Bones[_selectedBone] = boCur; }
                                            boCur.Visible = false;
                                            break;
                                    }
                                    RefreshPreviewOverrides(); OnSpriteModified();
                                    srcIdx = si;
                                    // Remember pending source so mode sticks until value is picked
                                    _pendingSrcIdx = si;
                                    _pendingSrcBone = _selectedBone;
                                }
                            }
                            GUI.color = prevCol;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Label($"<color={srcColors[srcIdx]}>{srcNames[srcIdx]}</color>", EditorStyles.RichLabel);

                        // ── NPC source sub-fields ──
                        if (srcIdx == 1)
                        {
                            if (boCur == null) { boCur = new BoneOverride(); ovr.Bones[_selectedBone] = boCur; }
                            string prevFrom = boCur.SpriteFrom ?? "";

                            string fromNpc = "", fromBone = "";
                            if (!string.IsNullOrEmpty(prevFrom))
                            {
                                int slash = prevFrom.IndexOf('/');
                                if (slash >= 0)
                                {
                                    fromNpc = prevFrom.Substring(0, slash);
                                    fromBone = prevFrom.Substring(slash + 1);
                                }
                                else
                                    fromNpc = prevFrom;
                            }

                            var allNpcIds = DataHelper.GetAllNpcIds();
                            var srcNpcDict = GetNpcDict();
                            var srcNpcIds = srcNpcDict != null ? srcNpcDict.Keys.OrderBy(k => k).ToList() : new List<string>();
                            var mergedNpcIds = new List<string>(srcNpcIds);
                            foreach (var nid in allNpcIds)
                                if (!mergedNpcIds.Contains(nid)) mergedNpcIds.Add(nid);

                            string newNpc = EditorFields.IdDropdown("NPC", fromNpc, mergedNpcIds, "spr_from_npc");
                            string newBone = "";
                            if (!string.IsNullOrEmpty(newNpc))
                            {
                                var boneNames = ExtractNpcBoneNames(newNpc);
                                newBone = EditorFields.IdDropdown("  Bone", fromBone, boneNames, "spr_from_bone");
                            }

                            string newFrom = "";
                            if (!string.IsNullOrEmpty(newNpc))
                                newFrom = string.IsNullOrEmpty(newBone) ? newNpc : $"{newNpc}/{newBone}";

                            if (newFrom != prevFrom)
                            {
                                boCur.SpriteFrom = newFrom;
                                // Clear pending once a real value is committed
                                if (!string.IsNullOrEmpty(newFrom)) _pendingSrcIdx = -1;
                                RefreshPreviewOverrides(); OnSpriteModified();
                            }
                        }

                        // ── Custom source sub-fields ──
                        if (srcIdx == 2 && ovr.CustomSprites.TryGetValue(_selectedBone, out var spDef))
                        {
                            GUILayout.BeginVertical(EditorStyles.CompactBox);

                            string prevImg = spDef.ImagePath;
                            spDef.ImagePath = EditorFields.TextField("Image Path", spDef.ImagePath);

                            if (spDef.Rect != null && spDef.Rect.Length >= 4)
                            {
                                GUILayout.Label("<color=#888>Atlas Rect:</color>", EditorStyles.RichLabel);
                                GUILayout.BeginHorizontal();
                                GUILayout.Label("X", GUILayout.Width(14));
                                string rx = GUILayout.TextField(spDef.Rect[0].ToString("F0"), GUILayout.Width(48));
                                GUILayout.Label("Y", GUILayout.Width(14));
                                string ry = GUILayout.TextField(spDef.Rect[1].ToString("F0"), GUILayout.Width(48));
                                GUILayout.Label("W", GUILayout.Width(14));
                                string rw = GUILayout.TextField(spDef.Rect[2].ToString("F0"), GUILayout.Width(48));
                                GUILayout.Label("H", GUILayout.Width(14));
                                string rh = GUILayout.TextField(spDef.Rect[3].ToString("F0"), GUILayout.Width(48));
                                GUILayout.EndHorizontal();
                                float.TryParse(rx, out spDef.Rect[0]);
                                float.TryParse(ry, out spDef.Rect[1]);
                                float.TryParse(rw, out spDef.Rect[2]);
                                float.TryParse(rh, out spDef.Rect[3]);

                                if (GUILayout.Button("Remove Rect", EditorStyles.MiniButton))
                                    spDef.Rect = null;
                            }
                            else
                            {
                                if (GUILayout.Button("+ Atlas Rect", EditorStyles.MiniButton))
                                    spDef.Rect = new float[] { 0, 0, 64, 64 };
                            }

                            spDef.PPU = EditorFields.FloatField("PPU", spDef.PPU);
                            spDef.PivotX = EditorFields.FloatField("Pivot X", spDef.PivotX);
                            spDef.PivotY = EditorFields.FloatField("Pivot Y", spDef.PivotY);

                            if (spDef.ImagePath != prevImg)
                            { RefreshPreviewOverrides(); OnSpriteModified(); }

                            GUILayout.EndVertical();
                        }
                    }

                    // ── Transform Override ────────────────────────────────
                    if (!ovr.Bones.TryGetValue(_selectedBone, out var bo))
                    {
                        if (GUILayout.Button("+ Add Override", EditorStyles.MiniButton))
                        {
                            bo = new BoneOverride();
                            ovr.Bones[_selectedBone] = bo;
                            OnSpriteModified();
                        }
                    }

                    if (ovr.Bones.TryGetValue(_selectedBone, out bo))
                    {
                        float ppx = bo.PosX, ppy = bo.PosY, pr = bo.Rotation;
                        float psx = bo.ScaleX, psy = bo.ScaleY; bool pv = bo.Visible;

                        bo.PosX = EditorFields.FloatField("Offset X", bo.PosX);
                        bo.PosY = EditorFields.FloatField("Offset Y", bo.PosY);
                        bo.Rotation = EditorFields.FloatField("Rotation", bo.Rotation);
                        bo.ScaleX = EditorFields.FloatField("Scale X", bo.ScaleX);
                        bo.ScaleY = EditorFields.FloatField("Scale Y", bo.ScaleY);
                        bo.Visible = EditorFields.Toggle("Visible", bo.Visible);
                        bo.SortingOffset = EditorFields.IntField("Sort Offset", bo.SortingOffset);
                        bo.ColorHex = EditorFields.TextField("Color Hex", bo.ColorHex);
                        bo.FlipX = EditorFields.Toggle("Flip X", bo.FlipX);
                        bo.FlipY = EditorFields.Toggle("Flip Y", bo.FlipY);
                        bo.Alpha = EditorFields.FloatField("Alpha", bo.Alpha);

                        if (bo.PosX != ppx || bo.PosY != ppy || bo.Rotation != pr ||
                            bo.ScaleX != psx || bo.ScaleY != psy || bo.Visible != pv)
                        { RefreshPreviewOverrides(); OnSpriteModified(); }

                        GUILayout.Space(2);
                        if (GUILayout.Button("Remove Override", EditorStyles.DangerButton))
                        { ovr.Bones.Remove(_selectedBone); RefreshPreviewOverrides(); OnSpriteModified(); }
                    }

                    // ── Remove / Restore Bone ─────────────────────
                    {
                        GUILayout.Space(4);
                        if (ovr.RemovedBones.Contains(_selectedBone))
                        {
                            if (GUILayout.Button("Restore Bone", EditorStyles.MiniButton))
                            {
                                ovr.RemovedBones.Remove(_selectedBone);
                                RefreshPreviewOverrides(); OnSpriteModified();
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("\u2716 Remove Bone", EditorStyles.DangerButton))
                            {
                                ovr.RemovedBones.Add(_selectedBone);
                                // Also clear any graft/custom on this bone
                                if (ovr.Bones.TryGetValue(_selectedBone, out var rbo))
                                    rbo.SpriteFrom = "";
                                ovr.CustomSprites.Remove(_selectedBone);
                                RefreshPreviewOverrides(); OnSpriteModified();
                            }
                        }
                    }

                    // ── Added Sprite fields (Parent Bone + Delete only) ──
                    // All other properties (source, transform, visual) are edited via the
                    // unified bone panel above which reads/writes ovr.Bones[name].
                    if (ovr.AddedSprites.TryGetValue(_selectedBone, out var addedDef))
                    {
                        GUILayout.Space(6);
                        GUILayout.Label("<color=#8cf><b>Added Sprite</b></color>", EditorStyles.RichLabel);

                        var rigBoneNames = _handles
                            .Where(bh => bh.Transform != null && bh.Name != _selectedBone)
                            .Select(bh => bh.Name)
                            .OrderBy(n => n).ToList();

                        string newParent = EditorFields.IdDropdown("Parent Bone", addedDef.ParentBone, rigBoneNames, "added_parent");
                        if (newParent != addedDef.ParentBone)
                        { addedDef.ParentBone = newParent; RefreshPreviewOverrides(); OnSpriteModified(); }

                        GUILayout.Space(2);
                        if (GUILayout.Button("\u2716 Delete Added Sprite", EditorStyles.DangerButton))
                        {
                            ovr.AddedSprites.Remove(_selectedBone);
                            ovr.Bones.Remove(_selectedBone);
                            ovr.CustomSprites.Remove(_selectedBone);
                            _selectedBone = null;
                            RefreshPreviewOverrides(); OnSpriteModified();
                        }
                    }

                    // ── Added Rig Bone fields (if selected bone is an added rig bone) ──
                    if (ovr.AddedBones.TryGetValue(_selectedBone ?? "", out var addedBoneDef))
                    {
                        GUILayout.Space(6);
                        GUILayout.Label("<color=#da8><b>Added Rig Bone Settings</b></color>", EditorStyles.RichLabel);

                        var rigBoneNames = _handles
                            .Where(bh => bh.Transform != null && bh.Name != _selectedBone)
                            .Select(bh => bh.Name)
                            .OrderBy(n => n).ToList();
                        // Also include other added bones as possible parents
                        foreach (var abk in ovr.AddedBones.Keys)
                            if (abk != _selectedBone && !rigBoneNames.Contains(abk))
                                rigBoneNames.Add(abk);

                        string newBoneParent = EditorFields.IdDropdown("Parent Bone", addedBoneDef.ParentBone, rigBoneNames, "addbone_parent");
                        if (newBoneParent != addedBoneDef.ParentBone)
                        { addedBoneDef.ParentBone = newBoneParent; RefreshPreviewOverrides(); OnSpriteModified(); }

                        float pbx = addedBoneDef.PosX, pby = addedBoneDef.PosY, pbr = addedBoneDef.Rotation;
                        float pbsx = addedBoneDef.ScaleX, pbsy = addedBoneDef.ScaleY;
                        addedBoneDef.PosX = EditorFields.FloatField("Offset X", addedBoneDef.PosX);
                        addedBoneDef.PosY = EditorFields.FloatField("Offset Y", addedBoneDef.PosY);
                        addedBoneDef.Rotation = EditorFields.FloatField("Rotation", addedBoneDef.Rotation);
                        addedBoneDef.ScaleX = EditorFields.FloatField("Scale X", addedBoneDef.ScaleX);
                        addedBoneDef.ScaleY = EditorFields.FloatField("Scale Y", addedBoneDef.ScaleY);
                        addedBoneDef.Length = EditorFields.FloatField("Bone Length", addedBoneDef.Length);

                        if (addedBoneDef.PosX != pbx || addedBoneDef.PosY != pby || addedBoneDef.Rotation != pbr ||
                            addedBoneDef.ScaleX != pbsx || addedBoneDef.ScaleY != pbsy)
                        { RefreshPreviewOverrides(); OnSpriteModified(); }

                        // ── Auto-Weight Influence ──
                        GUILayout.Space(4);
                        GUILayout.Label("<color=#da8><b>Auto-Weight Influence</b></color>", EditorStyles.RichLabel);
                        GUILayout.Label("<color=#888>Sprites this bone should deform via distance-based weights.</color>", EditorStyles.RichLabel);

                        var spriteBoneNames = _handles
                            .Where(bh => bh.HasSpriteRenderer)
                            .Select(bh => bh.Name)
                            .OrderBy(n => n).ToList();

                        // List current influences with remove buttons
                        string toRemove = null;
                        foreach (var infSprite in addedBoneDef.InfluenceSprites)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"  \u25C6 {infSprite}", EditorStyles.RichLabel);
                            if (GUILayout.Button("\u2716", EditorStyles.MiniButton, GUILayout.Width(24)))
                                toRemove = infSprite;
                            GUILayout.EndHorizontal();
                        }
                        if (toRemove != null) { addedBoneDef.InfluenceSprites.Remove(toRemove); OnSpriteModified(); }

                        // Add influence dropdown
                        var available = spriteBoneNames.Where(s => !addedBoneDef.InfluenceSprites.Contains(s)).ToList();
                        if (available.Count > 0)
                        {
                            string addInf = EditorFields.IdDropdown("+ Influence", "", available, "addbone_influence");
                            if (!string.IsNullOrEmpty(addInf))
                            {
                                addedBoneDef.InfluenceSprites.Add(addInf);
                                OnSpriteModified();
                            }
                        }

                        addedBoneDef.WeightRadius = EditorFields.FloatField("Weight Radius", addedBoneDef.WeightRadius);
                        addedBoneDef.WeightFalloff = EditorFields.FloatField("Weight Falloff", addedBoneDef.WeightFalloff);

                        GUILayout.Space(2);
                        if (GUILayout.Button("\u2716 Delete Added Bone", EditorStyles.DangerButton))
                        {
                            ovr.AddedBones.Remove(_selectedBone);
                            _selectedBone = null;
                            RefreshPreviewOverrides(); OnSpriteModified();
                        }
                    }
                }
            }

            // ── Animation Controls ────────────────────────────────
            if (EditorFields.Section("Animation", ref _secAnim))
            {
                if (_previewAnimator == null)
                {
                    GUILayout.Label("<color=#888><i>No Animator found on prefab.</i></color>", EditorStyles.RichLabel);
                }
                else if (_clipNames == null || _clipNames.Length == 0)
                {
                    GUILayout.Label("<color=#888><i>No animation clips found.</i></color>", EditorStyles.RichLabel);
                }
                else
                {
                    // Status
                    string status = _animPlaying ? "<color=#8f8>Playing</color>" : "<color=#fa0>Paused</color>";
                    float clipLen = (_selectedClipIdx < _clipLengths.Length) ? _clipLengths[_selectedClipIdx] : 1f;
                    float curTime = _timelineNormTime * clipLen;
                    GUILayout.Label($"{status}  <color=#888>{_clipNames[_selectedClipIdx]}  {curTime:F2}s / {clipLen:F2}s</color>", EditorStyles.RichLabel);

                    // Play / Pause
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(_animPlaying ? "Pause" : "Play", EditorStyles.MiniButton, GUILayout.Width(50)))
                        TogglePlayPause();
                    if (GUILayout.Button("|<", EditorStyles.MiniButton, GUILayout.Width(28)))
                        ScrubToNormTime(0f);
                    if (GUILayout.Button("<", EditorStyles.MiniButton, GUILayout.Width(22)))
                        StepFrame(-1);
                    if (GUILayout.Button(">", EditorStyles.MiniButton, GUILayout.Width(22)))
                        StepFrame(1);
                    if (GUILayout.Button(">|", EditorStyles.MiniButton, GUILayout.Width(28)))
                        ScrubToNormTime(1f);
                    GUILayout.EndHorizontal();

                    // Speed
                    float prevSpeed = _animSpeed;
                    _animSpeed = EditorFields.FloatField("Speed", _animSpeed);
                    if (_animSpeed != prevSpeed && _previewAnimator.enabled)
                        _previewAnimator.speed = _animSpeed;

                    // Clip selector
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#888>Clips:</color>", EditorStyles.RichLabel);
                    for (int i = 0; i < _clipNames.Length; i++)
                    {
                        string prefix = i == _selectedClipIdx ? "<color=cyan>\u25B6 </color>" : "  ";
                        string lenStr = i < _clipLengths.Length ? $"  <color=#666>({_clipLengths[i]:F2}s)</color>" : "";
                        if (GUILayout.Button($"{prefix}{_clipNames[i]}{lenStr}", EditorStyles.ListItem))
                        {
                            _selectedClipIdx = i;
                            _timelineNormTime = 0f;
                            ScrubToNormTime(0f);
                        }
                    }

                    // Trigger buttons (common NPC triggers)
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#888>Triggers:</color>", EditorStyles.RichLabel);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Attack", EditorStyles.MiniButton))
                    { _previewAnimator.enabled = true; _animPlaying = true; _previewAnimator.SetTrigger("attack"); _previewAnimator.speed = _animSpeed; }
                    if (GUILayout.Button("Cast", EditorStyles.MiniButton))
                    { _previewAnimator.enabled = true; _animPlaying = true; _previewAnimator.SetTrigger("cast"); _previewAnimator.speed = _animSpeed; }
                    if (GUILayout.Button("Hit", EditorStyles.MiniButton))
                    { _previewAnimator.enabled = true; _animPlaying = true; _previewAnimator.SetTrigger("hit"); _previewAnimator.speed = _animSpeed; }
                    GUILayout.EndHorizontal();

                    // ── Keyframe Editor ───────────────────────────────────
                    GUILayout.Space(6);
                    GUILayout.Label("<color=#aad><b>Keyframe Overrides</b></color>", EditorStyles.RichLabel);

                    string currentClip = _clipNames[_selectedClipIdx];

                    // Show/add keyframe for selected bone at current time
                    if (_selectedBone != null)
                    {
                        GUILayout.Label($"<color=#888>Bone: <b>{_selectedBone}</b>  Time: <b>{_timelineNormTime:F3}</b></color>", EditorStyles.RichLabel);

                        // Get or create anim override for current clip
                        if (!ovr.AnimOverrides.TryGetValue(currentClip, out var animOvr))
                            animOvr = null;

                        BoneKeyframe existingKf = null;
                        List<BoneKeyframe> kfList = null;
                        if (animOvr != null && animOvr.BoneKeyframes.TryGetValue(_selectedBone, out kfList))
                        {
                            existingKf = kfList.Find(k => Mathf.Abs(k.Time - _timelineNormTime) < 0.005f);
                        }

                        if (existingKf != null)
                        {
                            // Edit existing keyframe
                            GUILayout.BeginVertical(EditorStyles.CompactBox);
                            GUILayout.Label("<color=#fc0>Keyframe</color>", EditorStyles.RichLabel);
                            float kpx = existingKf.PosX, kpy = existingKf.PosY, kr = existingKf.Rotation;
                            float ksx = existingKf.ScaleX, ksy = existingKf.ScaleY;

                            existingKf.PosX = EditorFields.FloatField("Pos X", existingKf.PosX);
                            existingKf.PosY = EditorFields.FloatField("Pos Y", existingKf.PosY);
                            existingKf.Rotation = EditorFields.FloatField("Rotation", existingKf.Rotation);
                            existingKf.ScaleX = EditorFields.FloatField("Scale X", existingKf.ScaleX);
                            existingKf.ScaleY = EditorFields.FloatField("Scale Y", existingKf.ScaleY);

                            if (existingKf.PosX != kpx || existingKf.PosY != kpy ||
                                existingKf.Rotation != kr || existingKf.ScaleX != ksx || existingKf.ScaleY != ksy)
                            { RefreshPreviewOverrides(); OnSpriteModified(); }

                            if (GUILayout.Button("Delete Keyframe", EditorStyles.DangerButton))
                            {
                                kfList.Remove(existingKf);
                                if (kfList.Count == 0) animOvr.BoneKeyframes.Remove(_selectedBone);
                                if (animOvr.BoneKeyframes.Count == 0) ovr.AnimOverrides.Remove(currentClip);
                                RefreshPreviewOverrides(); OnSpriteModified();
                            }
                            GUILayout.EndVertical();
                        }
                        else
                        {
                            // Option to add keyframe at current position
                            if (GUILayout.Button($"+ Add Keyframe at t={_timelineNormTime:F3}", EditorStyles.MiniButton))
                            {
                                if (animOvr == null)
                                {
                                    animOvr = new AnimOverrideDef { ClipName = currentClip };
                                    ovr.AnimOverrides[currentClip] = animOvr;
                                }
                                if (!animOvr.BoneKeyframes.ContainsKey(_selectedBone))
                                    animOvr.BoneKeyframes[_selectedBone] = new List<BoneKeyframe>();

                                // Capture current bone transform as absolute values (SET mode)
                                var h = _handles.Find(b => b.Name == _selectedBone);

                                var newKf = new BoneKeyframe
                                {
                                    Time = Mathf.Round(_timelineNormTime * 1000f) / 1000f,
                                    PosX = h?.Transform != null ? h.Transform.localPosition.x : 0f,
                                    PosY = h?.Transform != null ? h.Transform.localPosition.y : 0f,
                                    Rotation = h?.Transform != null ? h.Transform.localEulerAngles.z : 0f,
                                    ScaleX = h?.Transform != null ? h.Transform.localScale.x : 1f,
                                    ScaleY = h?.Transform != null ? h.Transform.localScale.y : 1f,
                                };
                                animOvr.BoneKeyframes[_selectedBone].Add(newKf);
                                animOvr.BoneKeyframes[_selectedBone].Sort((a, b) => a.Time.CompareTo(b.Time));
                                OnSpriteModified();
                            }
                        }

                        // Show all keyframes for the selected bone in this clip
                        if (animOvr?.BoneKeyframes.TryGetValue(_selectedBone, out var allKfs) == true && allKfs.Count > 0)
                        {
                            GUILayout.Space(2);
                            GUILayout.Label($"<color=#666>Keyframes ({allKfs.Count}):</color>", EditorStyles.RichLabel);
                            foreach (var kf in allKfs)
                            {
                                bool isCurrent = Mathf.Abs(kf.Time - _timelineNormTime) < 0.005f;
                                string kfColor = isCurrent ? "cyan" : "#888";
                                string kfLabel = $"<color={kfColor}>t={kf.Time:F3}  pos=({kf.PosX:F2},{kf.PosY:F2})  rot={kf.Rotation:F1}  s=({kf.ScaleX:F2},{kf.ScaleY:F2})</color>";
                                if (GUILayout.Button(kfLabel, EditorStyles.ListItem))
                                    ScrubToNormTime(kf.Time);
                            }
                        }
                    }
                    else
                    {
                        GUILayout.Label("<color=#666><i>Select a bone to edit keyframes.</i></color>", EditorStyles.RichLabel);
                    }

                    // Info
                    GUILayout.Space(2);
                    GUILayout.Label("<color=#555><size=9>Timeline bar at bottom of viewport. Drag to scrub.</size></color>", EditorStyles.RichLabel);

                    // ── Base Animation Keyframes (read-only, sampled from clip) ──
                    if (EditorFields.Section("Base Keyframes", ref _secBaseKf))
                    {
                        if (_selectedBone != null)
                        {
                            string clipName = _clipNames[_selectedClipIdx];
                            GUILayout.Label($"<color=#888>Bone: <b>{_selectedBone}</b>  Clip: <b>{clipName}</b></color>", EditorStyles.RichLabel);
                            GUILayout.Label("<color=#666>Sampled bone transforms at 10 time intervals:</color>", EditorStyles.RichLabel);

                            if (GUILayout.Button("Sample Now", EditorStyles.MiniButton))
                                SampleBaseKeyframes();

                            if (_sampledKeyframes != null && _sampledBone == _selectedBone && _sampledClip == _selectedClipIdx)
                            {
                                foreach (var skf in _sampledKeyframes)
                                {
                                    bool isCur = Mathf.Abs(skf.Time - _timelineNormTime) < 0.06f;
                                    string kc = isCur ? "cyan" : "#999";
                                    string kfLine = $"<color={kc}>{skf.Time:F2}  pos=({skf.PosX:F3},{skf.PosY:F3})  rot={skf.Rot:F1}  s=({skf.ScaleX:F2},{skf.ScaleY:F2})</color>";
                                    if (GUILayout.Button(kfLine, EditorStyles.ListItem))
                                        ScrubToNormTime(skf.Time);
                                }
                            }
                        }
                        else
                        {
                            GUILayout.Label("<color=#666><i>Select a bone to see its base keyframes.</i></color>", EditorStyles.RichLabel);
                        }
                    }
                }
            }

            // ── Model Effects (tint, alpha, flip) ────────────────────
            if (EditorFields.Section("Model Effects", ref _secEffects))
            {
                string prevTint = ovr.ModelTintHex;
                float prevAlpha = ovr.ModelAlpha;
                bool prevFx = ovr.FlipX, prevFy = ovr.FlipY;

                ovr.ModelTintHex = EditorFields.TextField("Tint Color", ovr.ModelTintHex);
                ovr.ModelAlpha = EditorFields.FloatField("Alpha", ovr.ModelAlpha);
                ovr.FlipX = EditorFields.Toggle("Flip X", ovr.FlipX);
                ovr.FlipY = EditorFields.Toggle("Flip Y", ovr.FlipY);

                // Preview tint color swatch
                if (!string.IsNullOrEmpty(ovr.ModelTintHex) && ColorUtility.TryParseHtmlString(ovr.ModelTintHex, out var tintPrev))
                {
                    Rect swatchRect = GUILayoutUtility.GetRect(30, 16);
                    var old = GUI.color;
                    GUI.color = tintPrev;
                    GUI.DrawTexture(new Rect(swatchRect.x + 102, swatchRect.y, 60, 14), Texture2D.whiteTexture);
                    GUI.color = old;
                }

                if (ovr.ModelTintHex != prevTint || ovr.ModelAlpha != prevAlpha ||
                    ovr.FlipX != prevFx || ovr.FlipY != prevFy)
                { RefreshPreviewOverrides(); OnSpriteModified(); }
            }

            // ── Shader Effects (HSV, Glow, Outline, Greyscale, Ghost) ─
            if (EditorFields.Section("Shader Effects", ref _secShader))
            {
                bool prevUse = ovr.UseShaderEffects;
                ovr.UseShaderEffects = EditorFields.Toggle("Enable Shader FX", ovr.UseShaderEffects);

                if (ovr.UseShaderEffects)
                {
                    if (!_shaderSearched) FindAllIn1Shader();

                    if (_allIn1Shader == null)
                    {
                        GUILayout.Label("<color=#cc4444>AllIn1SpriteShader not found in Resources!</color>", EditorStyles.RichLabel);
                    }
                    else
                    {
                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>HSV Adjustment</b></color>", EditorStyles.RichLabel);
                        ovr.HueShift = EditorFields.FloatField("Hue Shift", ovr.HueShift);
                        ovr.Saturation = EditorFields.FloatField("Saturation", ovr.Saturation);
                        ovr.Brightness = EditorFields.FloatField("Brightness", ovr.Brightness);

                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>Glow</b></color>", EditorStyles.RichLabel);
                        ovr.GlowEnabled = EditorFields.Toggle("Enable Glow", ovr.GlowEnabled);
                        if (ovr.GlowEnabled)
                        {
                            ovr.GlowColorHex = EditorFields.TextField("Glow Color", ovr.GlowColorHex);
                            ovr.GlowIntensity = EditorFields.FloatField("Glow Intensity", ovr.GlowIntensity);
                        }

                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>Outline</b></color>", EditorStyles.RichLabel);
                        ovr.OutlineEnabled = EditorFields.Toggle("Enable Outline", ovr.OutlineEnabled);
                        if (ovr.OutlineEnabled)
                        {
                            ovr.OutlineColorHex = EditorFields.TextField("Outline Color", ovr.OutlineColorHex);
                            ovr.OutlineSize = EditorFields.FloatField("Outline Size", ovr.OutlineSize);
                        }

                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>Greyscale</b></color>", EditorStyles.RichLabel);
                        ovr.GreyscaleBlend = EditorFields.FloatField("Blend (0-1)", ovr.GreyscaleBlend);

                        GUILayout.Space(4);
                        GUILayout.Label("<color=#aad><b>Ghost</b></color>", EditorStyles.RichLabel);
                        ovr.GhostTransparency = EditorFields.FloatField("Transparency", ovr.GhostTransparency);
                    }
                }

                if (ovr.UseShaderEffects != prevUse || GUI.changed)
                { RefreshPreviewOverrides(); OnSpriteModified(); }
            }

            // ── Bone Hierarchy (unified tree) ─────────────────────
            int spriteCount = _handles.Count(h => h.HasSpriteRenderer);
            int rigCount = _handles.Count - spriteCount;
            if (EditorFields.Section($"Bones ({spriteCount} sprite, {rigCount} rig)", ref _secBones))
            {
                // Build reverse map: rig bone name → attached sprite names (via SpriteSkin rootBone)
                var rigToSprites = new Dictionary<string, List<string>>();
                foreach (var h2 in _handles)
                {
                    if (h2.HasSpriteRenderer && !string.IsNullOrEmpty(h2.SkinRootBone))
                    {
                        if (!rigToSprites.TryGetValue(h2.SkinRootBone, out var list))
                        {
                            list = new List<string>();
                            rigToSprites[h2.SkinRootBone] = list;
                        }
                        list.Add(h2.Name);
                    }
                }

                var depthHasContinuation = new bool[32];

                for (int i = 0; i < _handles.Count; i++)
                {
                    var h = _handles[i];
                    bool hasOvr = ovr.Bones.ContainsKey(h.Name);
                    bool isAddedBone = ovr.AddedBones.ContainsKey(h.Name);
                    bool isAddedSprite = ovr.AddedSprites.ContainsKey(h.Name);

                    // Tree connector prefix
                    var sb = new System.Text.StringBuilder();
                    for (int d = 0; d < h.Depth; d++)
                    {
                        if (d < h.Depth - 1)
                            sb.Append(depthHasContinuation[d] ? "\u2502 " : "  ");
                    }
                    if (h.Depth > 0)
                        sb.Append(h.IsLastChild ? "\u2514\u2500" : "\u251C\u2500");
                    depthHasContinuation[h.Depth] = !h.IsLastChild;

                    // Removed check
                    bool isRemoved = ovr.RemovedBones.Contains(h.Name);

                    // Icon + color: distinguish added vs existing, sprite vs rig
                    string icon = isAddedSprite ? "\u2726"  // ✦ for added sprites
                        : isAddedBone ? "\u25CB"           // ○ for added rig bones
                        : h.HasSpriteRenderer ? "\u25C6"   // ◆ for existing sprite bones
                        : "\u25CB";                         // ○ for existing rig bones
                    string nameColor = h.Name == _selectedBone ? "cyan"
                        : isRemoved ? "#944"
                        : isAddedSprite ? "#8cf"
                        : isAddedBone ? "#da8"
                        : h.HasSpriteRenderer ? "#ddd" : "#888";

                    // Source indicator (grafts, custom sprites, removed)
                    string sourceTag = "";
                    if (isRemoved)
                    {
                        sourceTag = " <color=#d44><s>removed</s></color>";
                    }
                    else if (isAddedSprite)
                    {
                        // Added sprites use ovr.Bones for source, same as existing bones
                        if (ovr.Bones.TryGetValue(h.Name, out var addBO) && !string.IsNullOrEmpty(addBO.SpriteFrom))
                            sourceTag = $" <color=#f80>\u2190 {addBO.SpriteFrom}</color>";
                        else if (ovr.CustomSprites.ContainsKey(h.Name))
                            sourceTag = " <color=#8cf>\u2190 custom</color>";
                    }
                    else if (isAddedBone)
                    {
                        var abDef = ovr.AddedBones[h.Name];
                        if (abDef.InfluenceSprites.Count > 0)
                            sourceTag = $" <color=#666>\u2192 {string.Join(", ", abDef.InfluenceSprites)}</color>";
                    }
                    else if (h.HasSpriteRenderer)
                    {
                        bool hasGraft = hasOvr && !string.IsNullOrEmpty(ovr.Bones[h.Name].SpriteFrom);
                        bool hasCustom = ovr.CustomSprites.ContainsKey(h.Name);
                        if (hasGraft)
                            sourceTag = $" <color=#f80>\u2190 {ovr.Bones[h.Name].SpriteFrom}</color>";
                        else if (hasCustom)
                            sourceTag = " <color=#8cf>\u2190 custom</color>";
                    }

                    // SpriteSkin root bone annotation
                    string skinTag = "";
                    if (h.HasSpriteRenderer && !string.IsNullOrEmpty(h.SkinRootBone))
                    {
                        // Sprite: show which rig bone it's attached to
                        skinTag = $" <color=#666>\u2192 {h.SkinRootBone}</color>";
                    }
                    else if (!h.HasSpriteRenderer && rigToSprites.TryGetValue(h.Name, out var attachedSprites))
                    {
                        // Rig bone: show which sprite(s) are deformed by it
                        skinTag = $" <color=#997>\u25C6 {string.Join(", ", attachedSprites)}</color>";
                    }

                    string marker = (hasOvr || isAddedBone || isAddedSprite) ? " <color=yellow>*</color>" : "";
                    string label = $"<color=#555>{sb}</color><color={nameColor}>{icon} <b>{h.Name}</b></color>{sourceTag}{skinTag}{marker}";
                    if (GUILayout.Button(label, EditorStyles.ListItem))
                        _selectedBone = h.Name;
                }
            }

            // ── Add New Sprite ────────────────────────────────────
            if (EditorFields.Section($"Add Sprite ({ovr.AddedSprites.Count})", ref _secAdded))
            {
                GUILayout.Label("<color=#888>Create a new sprite child on an existing bone.</color>", EditorStyles.RichLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", GUILayout.Width(50));
                _addedSpriteName = GUILayout.TextField(_addedSpriteName);
                GUILayout.EndHorizontal();

                bool nameValid = !string.IsNullOrEmpty(_addedSpriteName) &&
                    !ovr.AddedSprites.ContainsKey(_addedSpriteName) &&
                    !_handles.Any(bh => bh.Name == _addedSpriteName);

                GUI.enabled = nameValid;
                if (GUILayout.Button("+ Add Sprite", EditorStyles.MiniButton))
                {
                    ovr.AddedSprites[_addedSpriteName] = new AddedSpriteDef();
                    _selectedBone = _addedSpriteName;
                    _addedSpriteName = "";
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                GUI.enabled = true;

                if (!nameValid && !string.IsNullOrEmpty(_addedSpriteName))
                {
                    if (ovr.AddedSprites.ContainsKey(_addedSpriteName) || _handles.Any(bh => bh.Name == _addedSpriteName))
                        GUILayout.Label("<color=#d88>Name already in use.</color>", EditorStyles.RichLabel);
                }
            }

            // ── Add Rig Bone ────────────────────────────────────────
            if (EditorFields.Section($"Add Rig Bone ({ovr.AddedBones.Count})", ref _secAddBone))
            {
                GUILayout.Label("<color=#888>Create a new rig bone on an existing parent.</color>", EditorStyles.RichLabel);

                // Auto-assign a unique name if the field is empty
                if (string.IsNullOrEmpty(_addedBoneName))
                {
                    for (int n = 1; ; n++)
                    {
                        string candidate = $"bone_{n}";
                        if (!ovr.AddedBones.ContainsKey(candidate) &&
                            !ovr.AddedSprites.ContainsKey(candidate) &&
                            !_handles.Any(bh => bh.Name == candidate))
                        { _addedBoneName = candidate; break; }
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", GUILayout.Width(50));
                _addedBoneName = GUILayout.TextField(_addedBoneName);
                GUILayout.EndHorizontal();

                bool boneNameValid = !string.IsNullOrEmpty(_addedBoneName) &&
                    !ovr.AddedBones.ContainsKey(_addedBoneName) &&
                    !ovr.AddedSprites.ContainsKey(_addedBoneName) &&
                    !_handles.Any(bh => bh.Name == _addedBoneName);

                GUI.enabled = boneNameValid;
                if (GUILayout.Button("+ Add Bone", EditorStyles.MiniButton))
                {
                    ovr.AddedBones[_addedBoneName] = new AddedBoneDef();
                    _selectedBone = _addedBoneName;
                    _addedBoneName = "";
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
                GUI.enabled = true;

                if (!boneNameValid && !string.IsNullOrEmpty(_addedBoneName))
                {
                    if (ovr.AddedBones.ContainsKey(_addedBoneName) || ovr.AddedSprites.ContainsKey(_addedBoneName) || _handles.Any(bh => bh.Name == _addedBoneName))
                        GUILayout.Label("<color=#d88>Name already in use.</color>", EditorStyles.RichLabel);
                }
            }

            // ── Actions ──────────────────────────────────────────────
            EditorStyles.Separator();
            if (GUILayout.Button("Refresh Preview", EditorStyles.MiniButton))
            { ClearSpriteCache(); SpawnPreview(_previewNpcId, ovr.BaseSprite); }

            bool hasAnyOverrides = ovr.Bones.Count > 0 || ovr.CustomSprites.Count > 0 ||
                ovr.AnimOverrides.Count > 0 || ovr.AddedSprites.Count > 0 || ovr.RemovedBones.Count > 0 ||
                ovr.AddedBones.Count > 0 ||
                ovr.ScaleMultiplier != 1f || ovr.OffsetX != 0f || ovr.OffsetY != 0f ||
                !string.IsNullOrEmpty(ovr.Spritesheet) ||
                !string.IsNullOrEmpty(ovr.ModelTintHex) || ovr.ModelAlpha < 1f ||
                ovr.FlipX || ovr.FlipY || ovr.UseShaderEffects;
            if (hasAnyOverrides)
            {
                if (GUILayout.Button("Reset All Overrides", EditorStyles.DangerButton))
                {
                    ovr.Bones.Clear(); ovr.CustomSprites.Clear();
                    ovr.AnimOverrides.Clear(); ovr.AddedSprites.Clear(); ovr.RemovedBones.Clear();
                    ovr.AddedBones.Clear();
                    ovr.ScaleMultiplier = 1f; ovr.OffsetX = 0f; ovr.OffsetY = 0f;
                    ovr.Spritesheet = "";
                    ovr.ModelTintHex = ""; ovr.ModelAlpha = 1f;
                    ovr.FlipX = false; ovr.FlipY = false;
                    ovr.UseShaderEffects = false; ovr.HueShift = 0f;
                    ovr.Saturation = 1f; ovr.Brightness = 1f;
                    ovr.GlowEnabled = false; ovr.OutlineEnabled = false;
                    ovr.GreyscaleBlend = 0f; ovr.GhostTransparency = 0f;
                    ClearSpriteCache();
                    RefreshPreviewOverrides(); OnSpriteModified();
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PREVIEW MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        private void SpawnPreview(string spriteDefId, string baseSprite)
        {
            DestroyPreview();
            _previewNpcId = spriteDefId;

            // Resolve base NPC data: first check if a zone NPC uses this sprite def
            // (it would have a custom prefab already built), then fall back to base game NPC
            NPCData npcData = null;
            var npcDict = GetNpcDict();
            if (npcDict != null)
            {
                foreach (var kvp in npcDict)
                {
                    if (kvp.Value.SpriteSource == spriteDefId && ZoneLoader.Npcs.TryGetValue(kvp.Key, out var data))
                    { npcData = data; break; }
                }
            }
            if (npcData == null && !string.IsNullOrEmpty(baseSprite))
                npcData = DataHelper.GetExistingNPC(baseSprite);

            if (npcData == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] Cannot find NPC data for sprite '{spriteDefId}' (base='{baseSprite}')");
                _previewNpcId = null; return;
            }

            var prefab = npcData.GameObjectAnimated;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] Base NPC '{baseSprite}' has no animated prefab.");
                _previewNpcId = null; return;
            }

            _previewGO = Object.Instantiate(prefab, PreviewOrigin, Quaternion.identity);
            _previewGO.name = $"[SpriteEditor] {spriteDefId}";
            _previewGO.SetActive(true);

            // Capture Animator and enumerate clips
            _previewAnimator = _previewGO.GetComponentInChildren<Animator>();
            _animPlaying = false;
            _animSpeed = 1f;
            _selectedClipIdx = 0;
            _clipNames = null;
            _clipLengths = null;
            _timelineNormTime = 0f;
            _timelineDragging = false;

            if (_previewAnimator != null)
            {
                _previewAnimator.enabled = false;
                if (_previewAnimator.runtimeAnimatorController != null)
                {
                    var clips = _previewAnimator.runtimeAnimatorController.animationClips;
                    if (clips != null && clips.Length > 0)
                    {
                        _clips = clips;
                        _clipNames = new string[clips.Length];
                        _clipLengths = new float[clips.Length];
                        int idleIdx = -1;
                        for (int i = 0; i < clips.Length; i++)
                        {
                            _clipNames[i] = clips[i].name;
                            _clipLengths[i] = clips[i].length;
                            if (clips[i].name.ToLower().Contains("idle") && idleIdx < 0)
                                idleIdx = i;
                        }
                        // Default to idle clip if found
                        _selectedClipIdx = idleIdx >= 0 ? idleIdx : 0;

                        // Show the first frame of the selected clip using SampleAnimation
                        _playbackTime = 0f;
                        _timelineNormTime = 0f;
                        _clips[_selectedClipIdx].SampleAnimation(_previewGO, 0f);
                    }
                }
            }

            // Record rest pose before overrides
            _restPos.Clear(); _restRot.Clear(); _restScale.Clear();
            RecordRestPose(_previewGO.transform);

            // Collect bone handles (flat list with depth)
            _handles.Clear();
            CollectHandles(_previewGO.transform, "", 0);

            // Apply existing overrides so visual matches DTO state
            RefreshPreviewOverrides();

            _selectedBone = null;
            _zoom = 2.5f;
            _pan = Vector2.zero;
            Plugin.Log.LogInfo($"[SpriteEditor] Spawned preview for '{spriteDefId}': {_handles.Count} bones");
        }

        public void DestroyPreview()
        {
            if (_previewGO != null)
            {
                Object.Destroy(_previewGO);
                _previewGO = null;
            }
            _previewNpcId = null;
            _handles.Clear();
            _restPos.Clear(); _restRot.Clear(); _restScale.Clear(); _restSprites.Clear();
            _basePreviewAlpha.Clear();
            _restMaterials.Clear();
            _selectedBone = null;
            _dragging = false;
            _previewAnimator = null;
            _clipNames = null;
            _clipLengths = null;
            _clips = null;
            _animPlaying = false;
            _playbackTime = 0f;
            _timelineNormTime = 0f;
            _timelineDragging = false;
            CleanupShaderMaterials();
            CleanupAddedPreviewObjects();
        }

        private void CleanupAddedPreviewObjects()
        {
            foreach (var go in _addedPreviewObjects)
            {
                if (go != null) Object.Destroy(go);
            }
            _addedPreviewObjects.Clear();
            foreach (var go in _graftedBranchObjects)
            {
                if (go != null) Object.Destroy(go);
            }
            _graftedBranchObjects.Clear();
        }

        private void RecordRestPose(Transform root)
        {
            foreach (Transform child in root)
            {
                _restPos[child.name] = child.localPosition;
                _restRot[child.name] = child.localEulerAngles.z;
                _restScale[child.name] = child.localScale;
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (sr.sprite != null)
                        _restSprites[child.name] = sr.sprite;
                    if (sr.sharedMaterial != null)
                        _restMaterials[child.name] = sr.sharedMaterial;
                    _basePreviewAlpha[child.name] = sr.color.a;
                }
                RecordRestPose(child);
            }
        }

        private void CollectHandles(Transform root, string parentPath, int depth)
        {
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                string path = string.IsNullOrEmpty(parentPath) ? child.name : parentPath + "/" + child.name;

                // Read SpriteSkin rootBone if present (via reflection)
                string skinRoot = null;
                var skin = child.GetComponent<SpriteSkin>();
                if (skin != null)
                    skinRoot = skin.rootBone?.name;

                _handles.Add(new BoneHandle
                {
                    Name = child.name,
                    Path = path,
                    Transform = child,
                    Depth = depth,
                    HasSpriteRenderer = child.GetComponent<SpriteRenderer>() != null,
                    ParentName = depth > 0 ? root.name : null,
                    IsLastChild = (i == childCount - 1),
                    SkinRootBone = skinRoot,
                });
                if (child.childCount > 0) CollectHandles(child, path, depth + 1);
            }
        }

        /// <summary>Reset preview to rest pose then apply all current overrides from DTO.</summary>
        private void RefreshPreviewOverrides()
        {
            if (_previewGO == null || _previewNpcId == null) return;

            // Reset to rest pose (including original sprites and re-activate all bones)
            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                h.Transform.gameObject.SetActive(true); // re-activate in case it was deactivated by RemovedBones
                if (_restPos.TryGetValue(h.Name, out var rp)) h.Transform.localPosition = rp;
                if (_restRot.TryGetValue(h.Name, out var rr)) h.Transform.localEulerAngles = new Vector3(0, 0, rr);
                if (_restScale.TryGetValue(h.Name, out var rs)) h.Transform.localScale = rs;
                var sr0 = h.Transform.GetComponent<SpriteRenderer>();
                if (sr0 != null)
                {
                    sr0.enabled = true;
                    if (_restSprites.TryGetValue(h.Name, out var origSprite))
                        sr0.sprite = origSprite;
                    if (_restMaterials.TryGetValue(h.Name, out var origMat))
                        sr0.sharedMaterial = origMat;
                }
            }

            var refreshSprites = GetSpriteDict();
            if (refreshSprites?.TryGetValue(_previewNpcId, out var ovr) != true) return;

            // 1. Custom sprites (apply when defined)
            if (ovr.CustomSprites.Count > 0)
            {
                foreach (var h in _handles)
                {
                    if (h.Transform == null) continue;
                    if (ovr.RemovedBones.Contains(h.Name)) continue; // skip removed bones
                    if (!ovr.CustomSprites.TryGetValue(h.Name, out var spriteDef)) continue;
                    var sr = h.Transform.GetComponent<SpriteRenderer>();
                    if (sr == null) continue;
                    var texZoneId = GetTextureZoneId();
                    var newSprite = CreateSpriteFromDef(spriteDef, texZoneId, ovr.Spritesheet, sr.sprite);
                    if (newSprite != null) sr.sprite = newSprite;
                }
            }

            // 2. Create added sprite/bone GameObjects BEFORE applying grafts/overrides.
            //    All override properties (source, transform, visual) come from ovr.Bones
            //    just like any existing bone — AddedSpriteDef only stores ParentBone.
            CleanupAddedPreviewObjects();

            // 2a. Added sprites: bare GO with SpriteRenderer parented to existing bone
            //     If no parent specified yet, parent to preview root so it still shows in the list.
            foreach (var kvp in ovr.AddedSprites)
            {
                var aDef = kvp.Value;
                Transform parentT = null;
                if (!string.IsNullOrEmpty(aDef.ParentBone))
                {
                    var parentH = _handles.Find(bh => bh.Name == aDef.ParentBone);
                    if (parentH?.Transform != null) parentT = parentH.Transform;
                }
                if (parentT == null) parentT = _previewGO.transform; // fallback to root

                var go = new GameObject(kvp.Key);
                go.transform.SetParent(parentT, false);
                var asr = go.AddComponent<SpriteRenderer>();
                // Inherit sorting layer from parent so it renders in the correct layer
                var parentSR = parentT.GetComponent<SpriteRenderer>();
                if (parentSR != null)
                    asr.sortingLayerID = parentSR.sortingLayerID;
                _addedPreviewObjects.Add(go);
            }

            // 2b. Added rig bones: empty GameObjects on parent handles
            //     If no parent specified yet, parent to preview root so it still shows in the list.
            foreach (var kvp in ovr.AddedBones)
            {
                var bDef = kvp.Value;
                Transform parentT = null;
                if (!string.IsNullOrEmpty(bDef.ParentBone))
                {
                    var parentH = _handles.Find(bh => bh.Name == bDef.ParentBone);
                    if (parentH?.Transform != null) parentT = parentH.Transform;
                    else
                    {
                        var prev = _addedPreviewObjects.Find(g => g != null && g.name == bDef.ParentBone);
                        if (prev != null) parentT = prev.transform;
                    }
                }
                if (parentT == null) parentT = _previewGO.transform; // fallback to root

                var bgo = new GameObject(kvp.Key);
                bgo.transform.SetParent(parentT, false);
                bgo.transform.localPosition = new Vector3(bDef.PosX, bDef.PosY, 0f);
                bgo.transform.localEulerAngles = new Vector3(0, 0, bDef.Rotation);
                bgo.transform.localScale = new Vector3(bDef.ScaleX, bDef.ScaleY, 1f);
                _addedPreviewObjects.Add(bgo);
            }

            // Re-collect handles to include added items in the unified bone list
            _handles.Clear();
            CollectHandles(_previewGO.transform, "", 0);

            // 3. Branch grafts (SpriteFrom on any bone — existing or added).
            //    Instead of swapping the sprite image and remapping SpriteSkin bones,
            //    clone the entire bone branch from the source NPC so the sprite renders
            //    correctly with its own bones.
            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                if (ovr.RemovedBones.Contains(h.Name)) continue; // skip removed bones
                if (!ovr.Bones.TryGetValue(h.Name, out var bo) || string.IsNullOrEmpty(bo.SpriteFrom)) continue;

                string sourceNpc, sourceBone;
                int slash = bo.SpriteFrom.IndexOf('/');
                if (slash >= 0)
                {
                    sourceNpc = bo.SpriteFrom.Substring(0, slash);
                    sourceBone = bo.SpriteFrom.Substring(slash + 1);
                }
                else
                {
                    sourceNpc = bo.SpriteFrom;
                    sourceBone = h.Name;
                }

                var branch = CloneNpcBranch(sourceNpc, sourceBone);
                if (branch != null)
                {
                    // Parent the cloned branch to the same parent as the target bone
                    branch.transform.SetParent(h.Transform.parent, false);
                    // Position at same local position as target bone
                    branch.transform.localPosition = h.Transform.localPosition;
                    branch.transform.localRotation = h.Transform.localRotation;
                    branch.transform.localScale = h.Transform.localScale;
                    _graftedBranchObjects.Add(branch);

                    // Hide the original sprite on this bone (the branch replaces it)
                    var sr = h.Transform.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.enabled = false;
                }
            }

            // 3b. Removed bones: deactivate bone GO in preview
            foreach (var removedName in ovr.RemovedBones)
            {
                var rh = _handles.Find(bh => bh.Name == removedName);
                if (rh?.Transform == null) continue;
                rh.Transform.gameObject.SetActive(false);
            }

            // 3c. Re-collect handles so imported branch bones appear in the bone tree
            if (_graftedBranchObjects.Count > 0)
            {
                _handles.Clear();
                CollectHandles(_previewGO.transform, "", 0);
            }

            // 4. Global model overrides
            Transform animRoot = _previewGO.GetComponentInChildren<Animator>()?.transform ?? _previewGO.transform;
            if (ovr.ScaleMultiplier != 1f)
                animRoot.localScale = new Vector3(ovr.ScaleMultiplier, ovr.ScaleMultiplier, 1f);
            if (ovr.OffsetX != 0f || ovr.OffsetY != 0f)
                animRoot.localPosition += new Vector3(ovr.OffsetX, ovr.OffsetY, 0f);

            // 4b. Model-wide flip
            if (ovr.FlipX || ovr.FlipY)
            {
                var s = animRoot.localScale;
                if (ovr.FlipX) s.x = -Mathf.Abs(s.x);
                if (ovr.FlipY) s.y = -Mathf.Abs(s.y);
                animRoot.localScale = s;
            }

            // 4c. Model-wide tint + alpha (applied to all SpriteRenderers)
            Color modelTint = Color.white;
            bool hasModelTint = !string.IsNullOrEmpty(ovr.ModelTintHex) &&
                                ColorUtility.TryParseHtmlString(ovr.ModelTintHex, out modelTint);
            float modelAlpha = Mathf.Clamp01(ovr.ModelAlpha);
            bool applyModelColor = hasModelTint || modelAlpha < 1f;

            if (applyModelColor)
            {
                foreach (var h in _handles)
                {
                    if (h.Transform == null) continue;
                    var sr = h.Transform.GetComponent<SpriteRenderer>();
                    if (sr == null) continue;
                    Color c = hasModelTint ? modelTint : Color.white;
                    c.a = modelAlpha;
                    sr.color = c;
                }
            }

            // 5. Per-bone overrides (transform, visibility, color, flip, alpha, sort)
            ApplyBoneOverridesToPreview(ovr);

            // 6. Shader effects (AllIn1SpriteShader material swap)
            ApplyShaderEffectsToPreview(ovr);
        }

        /// <summary>Apply per-bone transform/visual overrides + removed bones to the preview.
        /// Called from RefreshPreviewOverrides (initial) and every frame during playback/scrub
        /// because SampleAnimation resets transform values each frame.</summary>
        private void ApplyBoneOverridesToPreview(SpriteOverrideDef ovr)
        {
            foreach (var h in _handles)
            {
                if (h.Transform == null || !ovr.Bones.TryGetValue(h.Name, out var bo)) continue;
                if (bo.PosX != 0f || bo.PosY != 0f)
                    h.Transform.localPosition += new Vector3(bo.PosX, bo.PosY, 0f);
                if (bo.Rotation != 0f)
                    h.Transform.localEulerAngles = new Vector3(0, 0, h.Transform.localEulerAngles.z + bo.Rotation);
                if (bo.ScaleX != 1f || bo.ScaleY != 1f)
                    h.Transform.localScale = new Vector3(
                        h.Transform.localScale.x * bo.ScaleX,
                        h.Transform.localScale.y * bo.ScaleY,
                        h.Transform.localScale.z);

                var bsr = h.Transform.GetComponent<SpriteRenderer>();
                if (bsr != null)
                {
                    if (!bo.Visible)
                        bsr.enabled = false;
                    if (!string.IsNullOrEmpty(bo.ColorHex) && ColorUtility.TryParseHtmlString(bo.ColorHex, out var col))
                        bsr.color = col;
                    if (bo.FlipX) bsr.flipX = !bsr.flipX;
                    if (bo.FlipY) bsr.flipY = !bsr.flipY;
                    if (bo.Alpha < 1f)
                    {
                        Color c = bsr.color;
                        float baseA = _basePreviewAlpha.TryGetValue(h.Name, out var bpa) ? bpa : c.a;
                        c.a = baseA * Mathf.Clamp01(bo.Alpha);
                        bsr.color = c;
                    }
                    if (bo.SortingOffset != 0)
                        bsr.sortingOrder += bo.SortingOffset;
                }
            }

            // Re-hide removed bones (SampleAnimation may re-enable them)
            foreach (var removedName in ovr.RemovedBones)
            {
                var rh = _handles.Find(bh => bh.Name == removedName);
                if (rh?.Transform == null) continue;
                rh.Transform.gameObject.SetActive(false);
            }
        }

        /// <summary>Apply AllIn1SpriteShader effects to the preview (HSV, Glow, Outline, etc).</summary>
        /// <summary>Apply AllIn1SpriteShader effects to the preview (HSV, Glow, Outline, etc).</summary>
        private void ApplyShaderEffectsToPreview(SpriteOverrideDef ovr)
        {
            // Clean up previously created shader materials
            CleanupShaderMaterials();

            if (!ovr.UseShaderEffects || _previewGO == null)
                return;

            FindAllIn1Shader();
            if (_allIn1Shader == null)
                return;

            foreach (var h in _handles)
            {
                if (h.Transform == null) continue;
                var sr = h.Transform.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;

                var mat = CreateShaderMaterial(sr, ovr);
                sr.sharedMaterial = mat;
                _shaderMaterials.Add(mat);
            }
        }

        /// <summary>Create an AllIn1SpriteShader material with the specified effects.
        /// Properly transfers essential sprite properties without cross-shader CopyProperties.</summary>
        private static Material CreateShaderMaterial(SpriteRenderer sr, SpriteOverrideDef ovr)
        {
            var mat = new Material(_allIn1Shader);

            // Transfer essential sprite properties manually (don't CopyPropertiesFromMaterial cross-shader)
            mat.SetTexture("_MainTex", sr.sprite.texture);
            mat.SetColor("_Color", sr.color);
            mat.renderQueue = sr.sharedMaterial != null ? sr.sharedMaterial.renderQueue : 3000;

            // HSV
            if (ovr.HueShift != 0f || ovr.Saturation != 1f || ovr.Brightness != 1f)
            {
                mat.EnableKeyword("HSV_ON");
                mat.SetFloat("_HsvShift", ovr.HueShift);
                mat.SetFloat("_HsvSaturation", ovr.Saturation);
                mat.SetFloat("_HsvBright", ovr.Brightness);
            }

            // Glow
            if (ovr.GlowEnabled)
            {
                mat.EnableKeyword("GLOW_ON");
                Color glowCol = Color.white;
                if (!string.IsNullOrEmpty(ovr.GlowColorHex))
                    ColorUtility.TryParseHtmlString(ovr.GlowColorHex, out glowCol);
                mat.SetColor("_GlowColor", glowCol);
                mat.SetFloat("_Glow", ovr.GlowIntensity);
            }

            // Outline
            if (ovr.OutlineEnabled)
            {
                mat.EnableKeyword("OUTBASE_ON");
                Color outCol = Color.black;
                if (!string.IsNullOrEmpty(ovr.OutlineColorHex))
                    ColorUtility.TryParseHtmlString(ovr.OutlineColorHex, out outCol);
                mat.SetColor("_OutlineColor", outCol);
                mat.SetFloat("_OutlineSize", ovr.OutlineSize);
                mat.SetFloat("_OutlineAlpha", 1f);
            }

            // Greyscale
            if (ovr.GreyscaleBlend > 0f)
            {
                mat.EnableKeyword("GREYSCALE_ON");
                mat.SetFloat("_GreyscaleBlend", ovr.GreyscaleBlend);
            }

            // Ghost
            if (ovr.GhostTransparency > 0f)
            {
                mat.EnableKeyword("GHOST_ON");
                mat.SetFloat("_GhostTransparency", ovr.GhostTransparency);
                mat.SetFloat("_GhostColorBoost", 1f);
            }

            return mat;
        }

        /// <summary>Destroy preview shader materials to avoid leaks.</summary>
        private void CleanupShaderMaterials()
        {
            foreach (var mat in _shaderMaterials)
            {
                if (mat != null)
                    Object.Destroy(mat);
            }
            _shaderMaterials.Clear();
        }

        /// <summary>Find the AllIn1SpriteShader in Resources (cached, searched once).</summary>
        private static void FindAllIn1Shader()
        {
            if (_shaderSearched) return;
            _shaderSearched = true;

            _allIn1Shader = Resources.Load<Shader>("AllIn1SpriteShader");
            if (_allIn1Shader == null)
            {
                // Fallback: try Shader.Find
                _allIn1Shader = Shader.Find("AllIn1SpriteShader");
            }
            if (_allIn1Shader != null)
                Plugin.Log.LogInfo("[SpriteEditor] Found AllIn1SpriteShader.");
            else
                Plugin.Log.LogWarning("[SpriteEditor] AllIn1SpriteShader not found. Shader effects unavailable.");
        }

        /// <summary>Commit the current bone transform to the override DTO (called after drag ends).</summary>
        private void CommitBoneOverride(string boneName)
        {
            var commitSprites = GetSpriteDict();
            if (commitSprites == null) return;
            if (!commitSprites.TryGetValue(_previewNpcId, out var ovr))
            {
                ovr = new SpriteOverrideDef { NpcId = _previewNpcId };
                commitSprites[_previewNpcId] = ovr;
            }

            // ── Added Rig Bone → write to AddedBoneDef ──
            if (ovr.AddedBones.TryGetValue(boneName, out var abDef))
            {
                var bh = _handles.Find(b => b.Name == boneName);
                if (bh?.Transform != null)
                {
                    abDef.PosX = Mathf.Round(bh.Transform.localPosition.x * 1000f) / 1000f;
                    abDef.PosY = Mathf.Round(bh.Transform.localPosition.y * 1000f) / 1000f;
                    abDef.Rotation = Mathf.Round(bh.Transform.localEulerAngles.z * 10f) / 10f;
                    abDef.ScaleX = Mathf.Round(bh.Transform.localScale.x * 100f) / 100f;
                    abDef.ScaleY = Mathf.Round(bh.Transform.localScale.y * 100f) / 100f;
                    OnSpriteModified();
                }
                return;
            }

            // ── Added Sprite → write transform to BoneOverride (same as existing bones) ──
            if (ovr.AddedSprites.ContainsKey(boneName))
            {
                var bh = _handles.Find(b => b.Name == boneName);
                if (bh?.Transform != null)
                {
                    if (!ovr.Bones.TryGetValue(boneName, out var addBO))
                    { addBO = new BoneOverride(); ovr.Bones[boneName] = addBO; }
                    addBO.PosX = Mathf.Round(bh.Transform.localPosition.x * 1000f) / 1000f;
                    addBO.PosY = Mathf.Round(bh.Transform.localPosition.y * 1000f) / 1000f;
                    addBO.Rotation = Mathf.Round(bh.Transform.localEulerAngles.z * 10f) / 10f;
                    addBO.ScaleX = Mathf.Round(bh.Transform.localScale.x * 100f) / 100f;
                    addBO.ScaleY = Mathf.Round(bh.Transform.localScale.y * 100f) / 100f;
                    OnSpriteModified();
                }
                return;
            }

            // ── Existing bone → write BoneOverride (delta from rest pose) ──
            var h = _handles.Find(b => b.Name == boneName);
            if (h?.Transform == null) return;

            Vector3 restP = _restPos.TryGetValue(boneName, out var rp) ? rp : Vector3.zero;
            float restR = _restRot.TryGetValue(boneName, out var rr) ? rr : 0f;
            Vector3 restS = _restScale.TryGetValue(boneName, out var rs) ? rs : Vector3.one;

            float ox = h.Transform.localPosition.x - restP.x;
            float oy = h.Transform.localPosition.y - restP.y;
            float rotD = h.Transform.localEulerAngles.z - restR;
            float sx = restS.x != 0 ? h.Transform.localScale.x / restS.x : 1f;
            float sy = restS.y != 0 ? h.Transform.localScale.y / restS.y : 1f;

            bool changed = Mathf.Abs(ox) > 0.001f || Mathf.Abs(oy) > 0.001f ||
                           Mathf.Abs(rotD) > 0.1f ||
                           Mathf.Abs(sx - 1f) > 0.001f || Mathf.Abs(sy - 1f) > 0.001f;
            if (changed)
            {
                if (!ovr.Bones.TryGetValue(boneName, out var bo))
                { bo = new BoneOverride(); ovr.Bones[boneName] = bo; }
                bo.PosX = Mathf.Round(ox * 1000f) / 1000f;
                bo.PosY = Mathf.Round(oy * 1000f) / 1000f;
                bo.Rotation = Mathf.Round(rotD * 10f) / 10f;
                bo.ScaleX = Mathf.Round(sx * 100f) / 100f;
                bo.ScaleY = Mathf.Round(sy * 100f) / 100f;
                OnSpriteModified();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CAMERA & COORDINATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void EnsureCamera()
        {
            // Re-create RT if it was released or became invalid
            if (_rt != null && !_rt.IsCreated())
            {
                Object.Destroy(_rt);
                _rt = null;
                if (_cam != null) _cam.targetTexture = null;
            }

            if (_rt == null)
            {
                _rt = new RenderTexture(RT_W, RT_H, 16);
                _rt.Create();
                if (_cam != null) _cam.targetTexture = _rt;
            }

            if (_cam != null) return;
            var go = new GameObject("[SpriteEditor] Camera");
            Object.DontDestroyOnLoad(go);
            go.transform.position = new Vector3(PreviewOrigin.x, PreviewOrigin.y, PreviewOrigin.z - 10f);
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 1f);
            _cam.orthographic = true;
            _cam.orthographicSize = _zoom;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 100f;
            _cam.depth = -100;
            _cam.enabled = false; // manual render only
            _cam.targetTexture = _rt;
        }

        private Vector2 WorldToViewport(Vector3 worldPos, Rect drawn)
        {
            Vector3 sp = _cam.WorldToScreenPoint(worldPos);
            return new Vector2(
                drawn.x + (sp.x / RT_W) * drawn.width,
                drawn.y + (1f - sp.y / RT_H) * drawn.height);
        }

        private Rect GetDrawnRect(Rect vp)
        {
            float vpAspect = vp.width / vp.height;
            float rtAspect = (float)RT_W / RT_H;
            if (rtAspect > vpAspect)
            {
                float w = vp.width, h = w / rtAspect;
                return new Rect(vp.x, vp.y + (vp.height - h) / 2, w, h);
            }
            float fh = vp.height, fw = fh * rtAspect;
            return new Rect(vp.x + (vp.width - fw) / 2, vp.y, fw, fh);
        }

        private static void EnsureTextures()
        {
            if (_dotDefault != null) return;
            _dotDefault  = MakeDot(new Color(0.6f, 0.6f, 0.6f, 0.9f));
            _dotSprite   = MakeDot(new Color(0.3f, 0.8f, 0.9f, 0.9f));
            _dotSelected = MakeDot(Color.yellow, 12);
            _dotOverride = MakeDot(new Color(1f, 0.6f, 0.2f, 0.9f));

            // Line material for GL bone connections
            if (_lineMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    _lineMaterial = new Material(shader);
                    _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                    _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _lineMaterial.SetInt("_Cull", 0);
                    _lineMaterial.SetInt("_ZWrite", 0);
                }
            }
        }

        private static Texture2D MakeDot(Color color, int size = 8)
        {
            var tex = new Texture2D(size, size);
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f;
                tex.SetPixel(x, y, Mathf.Sqrt(dx * dx + dy * dy) <= c ? color : Color.clear);
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        // ═══════════════════════════════════════════════════════════════
        //  NPC BUILDER HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Fill all sprite bones of the current NPC with grafts from sourceNpcId.
        /// For each sprite bone, sets SpriteFrom = "sourceNpcId/boneName" if the
        /// source NPC has a sprite with a matching name, or "sourceNpcId" (same bone
        /// name) otherwise. Existing grafts/custom sprites are overwritten.
        /// </summary>
        private void FillAllSpritesFrom(SpriteOverrideDef ovr, string sourceNpcId)
        {
            // Get source NPC's sprite bone names
            var sourceSprites = ExtractNpcSprites(sourceNpcId);
            var sourceBoneNames = new HashSet<string>(sourceSprites.Keys);
            int filled = 0, matched = 0, unmatched = 0;

            foreach (var h in _handles)
            {
                if (!h.HasSpriteRenderer) continue;

                if (!ovr.Bones.ContainsKey(h.Name))
                    ovr.Bones[h.Name] = new BoneOverride();

                if (sourceBoneNames.Contains(h.Name))
                {
                    // Exact name match — graft same-named bone
                    ovr.Bones[h.Name].SpriteFrom = $"{sourceNpcId}/{h.Name}";
                    matched++;
                }
                else
                {
                    // No match — set to source NPC (will try same bone name at runtime)
                    ovr.Bones[h.Name].SpriteFrom = sourceNpcId;
                    unmatched++;
                }
                filled++;
            }

            Plugin.Log.LogInfo($"[SpriteEditor] FillAllSpritesFrom '{sourceNpcId}': " +
                $"{filled} sprite bones, {matched} matched, {unmatched} unmatched");
        }

        /// <summary>
        /// Import all bone names and their default transforms from a source NPC
        /// into the sprite definition's Bones dictionary. Existing bone overrides
        /// are preserved; only new (missing) bones are added with identity transforms.
        /// This is useful to populate the bone list for a sprite definition so all bones
        /// are explicitly present and customizable.
        /// </summary>
        private void ImportBonesFrom(SpriteOverrideDef ovr, string sourceNpcId)
        {
            NPCData srcNpc = ZoneLoader.Npcs.TryGetValue(sourceNpcId, out var ours)
                ? ours : DataHelper.GetExistingNPC(sourceNpcId);
            if (srcNpc?.GameObjectAnimated == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] ImportBonesFrom: no prefab for '{sourceNpcId}'");
                return;
            }

            var temp = Object.Instantiate(srcNpc.GameObjectAnimated);
            temp.SetActive(false);

            var allBones = new Dictionary<string, Transform>();
            var allSRs = new Dictionary<string, SpriteRenderer>();
            BoneHierarchyUtils.CollectBones(temp.transform, allBones, allSRs);
            int imported = 0, skipped = 0;

            foreach (var kvp in allBones)
            {
                string boneName = kvp.Key;
                if (ovr.Bones.ContainsKey(boneName))
                { skipped++; continue; }

                // Add with identity override (no delta from rest pose)
                ovr.Bones[boneName] = new BoneOverride();
                imported++;
            }

            Object.Destroy(temp);
            Plugin.Log.LogInfo($"[SpriteEditor] ImportBonesFrom '{sourceNpcId}': " +
                $"{imported} bones imported, {skipped} already present");
        }

        // ═══════════════════════════════════════════════════════════════
        //  BRANCH GRAFTING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Clone the entire bone branch for a sprite from a source NPC.
        /// If the sprite has a SpriteSkin, clones from the SpriteSkin's rootBone down,
        /// bringing all deformation bones along so the sprite renders correctly.
        /// If no SpriteSkin, clones just the sprite GameObject.
        /// The returned GO is unparented — caller must parent it and track it for cleanup.
        /// </summary>
        public static GameObject CloneNpcBranch(string sourceNpcId, string sourceBoneName)
        {
            NPCData npcData = ZoneLoader.Npcs.TryGetValue(sourceNpcId, out var ours)
                ? ours : DataHelper.GetExistingNPC(sourceNpcId);
            if (npcData?.GameObjectAnimated == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] CloneNpcBranch: no prefab for NPC '{sourceNpcId}'");
                return null;
            }

            var temp = Object.Instantiate(npcData.GameObjectAnimated);
            temp.SetActive(false);

            // Find the source sprite bone in the temp hierarchy
            Transform spriteT = BoneHierarchyUtils.FindRecursive(temp.transform, sourceBoneName);
            if (spriteT == null)
            {
                Plugin.Log.LogWarning($"[SpriteEditor] CloneNpcBranch: bone '{sourceBoneName}' not found in NPC '{sourceNpcId}'");
                Object.Destroy(temp);
                return null;
            }

            // Check for SpriteSkin to determine the branch root
            var skin = spriteT.GetComponent<SpriteSkin>();
            Transform branchRoot;

            if (skin != null && skin.rootBone != null)
            {
                // The SpriteSkin rootBone is the top of the bone subtree that drives this sprite.
                // Clone from rootBone down — this brings all deformation bones.
                branchRoot = skin.rootBone;

                // If the sprite is NOT a descendant of rootBone, reparent it under rootBone
                // so it's included in the clone.
                if (!IsDescendantOf(spriteT, branchRoot))
                {
                    spriteT.SetParent(branchRoot, true);
                }

                Plugin.Log.LogInfo($"[SpriteEditor] CloneNpcBranch: '{sourceBoneName}' has SpriteSkin, branch root='{branchRoot.name}'");
            }
            else
            {
                // No SpriteSkin — just the sprite node itself
                branchRoot = spriteT;
                Plugin.Log.LogInfo($"[SpriteEditor] CloneNpcBranch: '{sourceBoneName}' has no SpriteSkin, cloning just the sprite GO");
            }

            // Detach branch from the temp hierarchy so Instantiate only clones this subtree
            branchRoot.SetParent(null, true);

            // Clone the branch — Unity auto-remaps internal references (SpriteSkin.boneTransforms etc)
            var clone = Object.Instantiate(branchRoot.gameObject);
            clone.name = branchRoot.name; // remove "(Clone)" suffix

            // Enable all SpriteRenderers in the clone (the temp was SetActive(false))
            clone.SetActive(true);

            // Cleanup
            Object.Destroy(branchRoot.gameObject); // the detached original
            Object.Destroy(temp); // the rest of the temp prefab

            return clone;
        }

        /// <summary>Check if child is a descendant of ancestor.</summary>
        private static bool IsDescendantOf(Transform child, Transform ancestor)
        {
            Transform current = child.parent;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = current.parent;
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE LOADING & CACHING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Extract all bone sprites from an existing NPC's prefab (cached).</summary>
        public static Dictionary<string, Sprite> ExtractNpcSprites(string sourceNpcId)
        {
            if (_graftSpriteCache.TryGetValue(sourceNpcId, out var cached))
                return cached;

            NPCData npcData = ZoneLoader.Npcs.TryGetValue(sourceNpcId, out var ours)
                ? ours : DataHelper.GetExistingNPC(sourceNpcId);

            if (npcData?.GameObjectAnimated == null)
            {
                _graftSpriteCache[sourceNpcId] = new Dictionary<string, Sprite>();
                return _graftSpriteCache[sourceNpcId];
            }

            var temp = Object.Instantiate(npcData.GameObjectAnimated);
            temp.SetActive(false);
            var sprites = new Dictionary<string, Sprite>();
            CollectSpritesRecursive(temp.transform, sprites);
            Object.Destroy(temp);

            _graftSpriteCache[sourceNpcId] = sprites;
            return sprites;
        }

        private static void CollectSpritesRecursive(Transform parent, Dictionary<string, Sprite> dict)
        {
            foreach (Transform child in parent)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                    dict[child.name] = sr.sprite;
                if (child.childCount > 0)
                    CollectSpritesRecursive(child, dict);
            }
        }

        /// <summary>Load a Texture2D from disk (cached).</summary>
        private static Texture2D LoadTexture(string fullPath)
        {
            if (_textureCache.TryGetValue(fullPath, out var cached) && cached != null)
                return cached;

            if (!File.Exists(fullPath))
            {
                Plugin.Log.LogWarning($"[SpriteEditor] Texture not found: {fullPath}");
                return null;
            }

            byte[] data = File.ReadAllBytes(fullPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.filterMode = FilterMode.Bilinear;

            _textureCache[fullPath] = tex;
            return tex;
        }

        /// <summary>Build a Sprite from a SpriteDef, loading the texture from disk.</summary>
        public static Sprite CreateSpriteFromDef(SpriteDef def, string zoneId, string fallbackSheet, Sprite originalSprite)
        {
            string imagePath = !string.IsNullOrEmpty(def.ImagePath) ? def.ImagePath : fallbackSheet;
            if (string.IsNullOrEmpty(imagePath)) return null;

            string fullPath = Path.Combine(ZoneLoader.GetZoneFolder(zoneId), "textures", imagePath);
            var tex = LoadTexture(fullPath);
            if (tex == null) return null;

            Rect rect;
            if (def.Rect != null && def.Rect.Length >= 4)
                rect = new Rect(def.Rect[0], def.Rect[1], def.Rect[2], def.Rect[3]);
            else
                rect = new Rect(0, 0, tex.width, tex.height);

            float ppu = def.PPU > 0 ? def.PPU : (originalSprite?.pixelsPerUnit ?? 100f);
            var pivot = new Vector2(def.PivotX, def.PivotY);

            return Sprite.Create(tex, rect, pivot, ppu);
        }

        /// <summary>Extract bone names (that have SpriteRenderers) from an NPC prefab.</summary>
        public static List<string> ExtractNpcBoneNames(string npcId)
        {
            var sprites = ExtractNpcSprites(npcId);
            return sprites.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>Apply AllIn1SpriteShader effects to a collection of SpriteRenderers at runtime.</summary>
        public static void ApplyShaderEffectsToRenderers(System.Collections.Generic.IEnumerable<SpriteRenderer> renderers, SpriteOverrideDef ovr)
        {
            FindAllIn1Shader();
            if (_allIn1Shader == null) return;

            foreach (var sr in renderers)
            {
                if (sr == null || sr.sprite == null) continue;
                sr.sharedMaterial = CreateShaderMaterial(sr, ovr);
            }
        }

        /// <summary>Clear all sprite caches. Call on zone reload or cleanup.</summary>
        public static void ClearSpriteCache()
        {
            _graftSpriteCache.Clear();
            _textureCache.Clear();
        }

        // ═══════════════════════════════════════════════════════════════
        //  STATIC: Apply overrides to any NPC's transform hierarchy
        //  Called from the NPCItem.Init() postfix patch
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply sprite overrides from the ZoneDef to an NPCItem's animated model.
        /// Called after NPCItem.Init() via Harmony postfix.
        /// </summary>
        public static void ApplyOverridesToNpcItem(NPCItem npcItem)
        {
            if (ZoneLoader.CurrentZone == null) return;
            if (npcItem?.NPC == null) return;

            string npcId = npcItem.NPC.GameName;
            string zoneId = ZoneLoader.CurrentZone.ZoneId;

            // Resolve sprite definition: NPC → NpcDef → SpriteSource → Sprites dict
            SpriteOverrideDef overrideDef = null;
            string baseId = ZoneLoader.StripVariantSuffix(npcId);
            if (ZoneLoader.CurrentZone.Npcs.TryGetValue(baseId, out var npcDef))
                overrideDef = ZoneLoader.ResolveSpriteDefForNpc(ZoneLoader.CurrentZone, npcDef);

            if (overrideDef == null) return;

            // Skip if no actual overrides exist
            if (overrideDef.Bones.Count == 0 && overrideDef.CustomSprites.Count == 0 &&
                overrideDef.ScaleMultiplier == 1f &&
                overrideDef.OffsetX == 0f && overrideDef.OffsetY == 0f &&
                !overrideDef.FlipX && !overrideDef.FlipY &&
                string.IsNullOrEmpty(overrideDef.ModelTintHex) &&
                overrideDef.ModelAlpha >= 1f &&
                !overrideDef.UseShaderEffects &&
                (overrideDef.AnimOverrides == null || overrideDef.AnimOverrides.Count == 0))
                return;

            ApplyOverridesToTransform(npcItem.transform, overrideDef, zoneId);
        }

        /// <summary>Walk the transform hierarchy and apply all sprite overrides.</summary>
        private static void ApplyOverridesToTransform(Transform root, SpriteOverrideDef overrideDef, string zoneId)
        {
            // Find the animated model root (first child with Animator or named after the NPC)
            Transform animRoot = null;
            foreach (Transform child in root)
            {
                if (child.GetComponent<Animator>() != null)
                {
                    animRoot = child;
                    break;
                }
            }

            // If no direct Animator child, search deeper
            if (animRoot == null)
            {
                var anim = root.GetComponentInChildren<Animator>();
                if (anim != null)
                    animRoot = anim.transform;
            }

            if (animRoot == null) return;

            // 1. Apply custom sprites (when defined)
            if (overrideDef.CustomSprites.Count > 0)
            {
                ApplyCustomSpritesRecursive(animRoot, overrideDef, zoneId);
            }

            // 2. Apply grafts (any bone with SpriteFrom, works in any mode)
            bool hasGrafts = overrideDef.Bones.Values.Any(b => !string.IsNullOrEmpty(b.SpriteFrom));
            if (hasGrafts)
            {
                ApplyGraftsRecursive(animRoot, overrideDef.Bones);
            }

            // 3. Apply global overrides to the animated root
            if (overrideDef.ScaleMultiplier != 1f)
            {
                animRoot.localScale *= overrideDef.ScaleMultiplier;
            }
            if (overrideDef.OffsetX != 0f || overrideDef.OffsetY != 0f)
            {
                animRoot.localPosition += new Vector3(overrideDef.OffsetX, overrideDef.OffsetY, 0f);
            }

            // 3b. Model-wide flip
            if (overrideDef.FlipX || overrideDef.FlipY)
            {
                var s = animRoot.localScale;
                if (overrideDef.FlipX) s.x = -Mathf.Abs(s.x);
                if (overrideDef.FlipY) s.y = -Mathf.Abs(s.y);
                animRoot.localScale = s;
            }

            // 3c. Model-wide tint + alpha
            Color modelTint = Color.white;
            bool hasModelTint = !string.IsNullOrEmpty(overrideDef.ModelTintHex) &&
                                ColorUtility.TryParseHtmlString(overrideDef.ModelTintHex, out modelTint);
            float modelAlpha = Mathf.Clamp01(overrideDef.ModelAlpha);
            if (hasModelTint || modelAlpha < 1f)
            {
                ApplyModelColorRecursive(animRoot, hasModelTint ? modelTint : Color.white, modelAlpha);
            }

            // 4. Apply per-bone overrides (transform, color, visibility, flip, alpha, sort)
            if (overrideDef.Bones.Count > 0)
            {
                ApplyBoneOverridesRecursive(animRoot, overrideDef.Bones);
            }

            // 5. Apply shader effects (AllIn1SpriteShader material swap)
            if (overrideDef.UseShaderEffects)
            {
                ApplyShaderEffectsRuntime(animRoot, overrideDef);
            }
        }

        /// <summary>Apply model-wide tint color and alpha to all SpriteRenderers.</summary>
        private static void ApplyModelColorRecursive(Transform parent, Color tint, float alpha)
        {
            foreach (Transform child in parent)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    Color c = tint;
                    c.a = alpha;
                    sr.color = c;
                }
                if (child.childCount > 0)
                    ApplyModelColorRecursive(child, tint, alpha);
            }
        }

        /// <summary>Apply AllIn1SpriteShader effects at runtime (for in-game NPCs).
        /// Walks the full transform hierarchy recursively.</summary>
        private static void ApplyShaderEffectsRuntime(Transform parent, SpriteOverrideDef ovr)
        {
            FindAllIn1Shader();
            if (_allIn1Shader == null) return;
            ApplyShaderEffectsRecursive(parent, ovr);
        }

        private static void ApplyShaderEffectsRecursive(Transform parent, SpriteOverrideDef ovr)
        {
            foreach (Transform child in parent)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    sr.sharedMaterial = CreateShaderMaterial(sr, ovr);
                }
                if (child.childCount > 0)
                    ApplyShaderEffectsRecursive(child, ovr);
            }
        }

        /// <summary>Replace bone sprites from custom PNG files on disk.</summary>
        private static void ApplyCustomSpritesRecursive(Transform parent, SpriteOverrideDef overrideDef, string zoneId)
        {
            foreach (Transform child in parent)
            {
                if (overrideDef.CustomSprites.TryGetValue(child.name, out var spriteDef))
                {
                    var sr = child.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        var newSprite = CreateSpriteFromDef(spriteDef, zoneId, overrideDef.Spritesheet, sr.sprite);
                        if (newSprite != null)
                            sr.sprite = newSprite;
                    }
                }
                if (child.childCount > 0)
                    ApplyCustomSpritesRecursive(child, overrideDef, zoneId);
            }
        }

        /// <summary>Replace bone sprites by grafting from other NPC prefabs.</summary>
        private static void ApplyGraftsRecursive(Transform parent, Dictionary<string, BoneOverride> overrides)
        {
            foreach (Transform child in parent)
            {
                if (overrides.TryGetValue(child.name, out var bo) && !string.IsNullOrEmpty(bo.SpriteFrom))
                {
                    var sr = child.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        string sourceNpc, sourceBone;
                        int slash = bo.SpriteFrom.IndexOf('/');
                        if (slash >= 0)
                        {
                            sourceNpc = bo.SpriteFrom.Substring(0, slash);
                            sourceBone = bo.SpriteFrom.Substring(slash + 1);
                        }
                        else
                        {
                            sourceNpc = bo.SpriteFrom;
                            sourceBone = child.name;
                        }

                        var sprites = ExtractNpcSprites(sourceNpc);
                        if (sprites.TryGetValue(sourceBone, out var sprite))
                            sr.sprite = sprite;
                    }
                }
                if (child.childCount > 0)
                    ApplyGraftsRecursive(child, overrides);
            }
        }

        private static void ApplyBoneOverridesRecursive(Transform parent, Dictionary<string, BoneOverride> overrides)
        {
            foreach (Transform child in parent)
            {
                if (overrides.TryGetValue(child.name, out var bo))
                {
                    // Position offset (additive)
                    if (bo.PosX != 0f || bo.PosY != 0f)
                        child.localPosition += new Vector3(bo.PosX, bo.PosY, 0f);

                    // Rotation (additive)
                    if (bo.Rotation != 0f)
                        child.localEulerAngles = new Vector3(0f, 0f, child.localEulerAngles.z + bo.Rotation);

                    // Scale (multiplicative, 1.0 = no change)
                    if (bo.ScaleX != 1f || bo.ScaleY != 1f)
                        child.localScale = new Vector3(
                            child.localScale.x * bo.ScaleX,
                            child.localScale.y * bo.ScaleY,
                            child.localScale.z);

                    // Visibility
                    var sr = child.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        if (!bo.Visible)
                            sr.enabled = false;

                        // Sorting offset
                        if (bo.SortingOffset != 0)
                            sr.sortingOrder += bo.SortingOffset;

                        // Color tint
                        if (!string.IsNullOrEmpty(bo.ColorHex))
                        {
                            if (ColorUtility.TryParseHtmlString(bo.ColorHex, out var color))
                                sr.color = color;
                        }

                        // Flip
                        if (bo.FlipX) sr.flipX = !sr.flipX;
                        if (bo.FlipY) sr.flipY = !sr.flipY;

                        // Per-bone alpha (one-time application at init — multiplicative is safe)
                        if (bo.Alpha < 1f)
                        {
                            Color c = sr.color;
                            c.a *= Mathf.Clamp01(bo.Alpha);
                            sr.color = c;
                        }
                    }
                }

                // Recurse into children
                if (child.childCount > 0)
                    ApplyBoneOverridesRecursive(child, overrides);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════

        public void Cleanup()
        {
            DestroyPreview();
            ClearSpriteCache();
            if (_cam != null)
            {
                _cam.targetTexture = null;
                Object.Destroy(_cam.gameObject);
                _cam = null;
            }
            if (_rt != null)
            {
                if (_rt.IsCreated()) _rt.Release();
                Object.Destroy(_rt);
                _rt = null;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  BONE HANDLE (lightweight, for visual editor viewport)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Tracks a single bone/transform for the visual sprite editor.</summary>
    public class BoneHandle
    {
        public string Name;
        public string Path;
        public Transform Transform;
        public int Depth;
        public bool HasSpriteRenderer;
        public string ParentName;
        public bool IsLastChild;
        /// <summary>Name of the SpriteSkin rootBone (which rig bone this sprite deforms from). Null if no SpriteSkin.</summary>
        public string SkinRootBone;
    }
}
