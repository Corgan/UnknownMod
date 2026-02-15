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
    public partial class SpriteEditor
    {
        private readonly ModEditor _parent;

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

        public SpriteEditor(ModEditor parent) => _parent = parent;

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT INTEGRATION HELPERS
        // ═══════════════════════════════════════════════════════════════

        private bool _showOverrideBrowser;
        private Vector2 _overrideBrowserScroll;
        private string _overrideBrowserFilter = "";

        /// <summary>
        /// Get the active sprite dictionary. Returns mod-project sprites when
        /// a mod project is active (Sprites tab), otherwise falls back to
        /// ZoneEditingService.CurrentZone.Sprites for zone-scoped editing.
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
            return ZoneEditingService.CurrentZone?.Sprites;
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

        /// <summary>Mark sprite data as modified. Saves to mod project if active, else ZoneEditingService.</summary>
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
            return ZoneEditingService.CurrentZone?.Npcs;
        }

        /// <summary>Get a zone ID for texture paths. Uses mod project folder if active.</summary>
        private string GetTextureZoneId()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null) return proj.ModId;
            return ZoneEditingService.CurrentZone?.ZoneId ?? "";
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
