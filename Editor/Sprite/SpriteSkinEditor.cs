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
    /// Visual sprite editor for animated character models (NPCs and Hero Skins).
    /// Renders the character's animated prefab into a RenderTexture viewport with
    /// draggable bone handles for adjusting transforms.
    /// Operates in two modes: NPC (source = NPCData.GameObjectAnimated) and
    /// HeroSkin (source = SkinData.SkinGo).
    /// </summary>
    public partial class SpriteSkinEditor
    {
        private readonly ModEditor _parent;

        /// <summary>Determines the source model resolution and panel UI.</summary>
        public enum EditorMode { NPC, HeroSkin, Item }
        public EditorMode ActiveMode { get; set; } = EditorMode.NPC;

        // ── Preview ──────────────────────────────────────────────────
        private Camera _cam;
        private RenderTexture _rt;
        private GameObject _previewGO;
        private string _previewNpcId;

        /// <summary>Currently selected sprite skin ID (for hot-reload integration).</summary>
        public string SelectedSkinId => _previewNpcId;
        private int _lastRenderFrame = -1;

        // ── Bones ────────────────────────────────────────────────────
        private List<BoneHandle> _handles = new();
        private Dictionary<string, Vector3> _restPos = new();
        private Dictionary<string, float> _restRot = new();
        private Dictionary<string, Vector3> _restScale = new();
        private Dictionary<string, Sprite> _restSprites = new();
        private Dictionary<string, Material> _restMaterials = new();
        private Dictionary<string, int> _restSortingOrder = new();
        private Dictionary<string, bool> _restFlipX = new();
        private Dictionary<string, bool> _restFlipY = new();

        /// <summary>Rest poses for SkinRootTransform targets (e.g. _previewGO.transform)
        /// that may not appear in _handles. Keyed by Transform instance.</summary>
        private Dictionary<Transform, (Vector3 pos, float rot, Vector3 scale)> _skinRootRest = new();

        // ── Cached name-keyed maps for shared CharacterOverrideDriver.ApplyBoneOverrides ──
        // Built from _handles in RebuildEditorBoneMaps() after each RefreshPreviewOverrides.
        // Re-keyed from path→name so the shared static method can use flat name lookups.
        private Dictionary<string, Transform> _edBoneMap = new();
        private Dictionary<string, SpriteRenderer> _edSrMap = new();
        private Dictionary<string, Transform> _edSkinRootMap = new();
        private Dictionary<string, int> _edBaseSortOrder = new();
        private Dictionary<string, bool> _edBaseFlipX = new();
        private Dictionary<string, bool> _edBaseFlipY = new();
        private Dictionary<string, float> _edBaseAlpha = new();

        private string _selectedBone;
        private string _selectedBonePath; // full hierarchy path for unique identification (avoids name collisions)
        private int _pendingSrcIdx = -1;   // remembers source button click while dropdown value is empty
        private string _pendingSrcBone;   // which bone the pending source applies to
        // (graft sub-tab removed — grafts now use GraftDef list)

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
        private bool _showSpriteDots = true;

        // ── Pivot drag ───────────────────────────────────────────────
        private bool _pivotDragging;
        private Vector2 _pivotDragStart;

        // ── Overlap pick (Tab to cycle) ───────────────────────────────
        private List<BoneHandle> _overlapCandidates = new();
        private int _overlapIndex;
        private Vector2 _overlapScreenPos; // screen pos where the overlap was detected (for tooltip)

        // ── NPC Builder ──────────────────────────────────────────────


        // ── Animation playback (editor-only, not persisted) ──────────
        private bool _animPlaying;
        private float _animSpeed = 1f;
        private string[] _clipNames;
        private float[] _clipLengths;
        private AnimationClip[] _clips; // clip objects (used for Animator.Play hash + length)
        private int _selectedClipIdx;
        private Animator _previewAnimator;
        private float _playbackTime; // manual time counter for playback
        private bool _triggerMode;   // true when Animator state machine is driving (after Attack/Cast/Hit)

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
        private bool _secGrafts = false;
        private int _graftSrcTab;          // 0=NPC, 1=Hero (source tab for new graft)
        private bool _secBones = true;
        private bool _secAnim = true;
        private bool _secBaseKf = false;
        private bool _secEffects = false;
        private bool _secRemoved = false;
        private List<GameObject> _graftPreviewObjects = new();
        private List<(Animator anim, AnimationClip[] clips)> _graftAnimators = new();
        private List<(Transform sourceBone, Transform rigBone, Vector3 restOffset)> _graftAlignments = new();
        // Per-graft cached base SR state (populated once, reused each frame to avoid compounding)
        private List<Dictionary<string, int>> _graftBaseSortOrder = new();
        private List<Dictionary<string, bool>> _graftBaseFlipX = new();
        private List<Dictionary<string, bool>> _graftBaseFlipY = new();
        private List<Dictionary<string, float>> _graftBaseAlpha = new();
        // Cached custom sprites for per-frame re-stamping (Animator overwrites sr.sprite)
        private Dictionary<string, Sprite> _edCustomSpriteCache = new();
        private List<Dictionary<string, Sprite>> _graftCustomSpriteCache = new();
        // Per-graft ancestor rest poses: bones above grafted sprite, frozen after
        // animation to prevent source body motion leaking into the graft.
        private List<List<(Transform bone, Vector3 pos, Quaternion rot, Vector3 scale)>> _graftAncestorRest = new();
        // Per-graft host→puppet state hash maps for trigger-mode sync
        private List<Dictionary<int, int>> _graftStateMaps = new();
        private int _selectedGraftIdx = -1;   // -1 = host bone, >=0 = index into ovr.Grafts
        private bool _secValidation = false;
        private List<Definitions.DiagMessage> _validationResults = null;

        private static Texture2D _vpBgTex;

        // ── Constants ────────────────────────────────────────────────
        private enum EditMode { Move, Rotate, Scale }
        private static readonly Vector3 PreviewOrigin = Vector3.zero;
        private const int PreviewLayer = 31; // unused layer for preview isolation
        private const int RT_W = 1024, RT_H = 768;
        private const float HandleSize = 8f, HandleSizeSel = 12f, PickRadius = 14f;

        // ── Handle textures ──────────────────────────────────────────
        private static Texture2D _dotDefault, _dotSprite, _dotSelected, _dotOverride;
        private static Material _lineMaterial;


        // ── Cached base alpha per bone (to avoid per-frame alpha drift) ──
        private Dictionary<string, float> _basePreviewAlpha = new();

        // ── Cached textures & styles (avoid per-frame allocation) ────
        private static Texture2D _tlBgTex, _tlBorderTex, _tlTrackBgTex, _tlFillTex;
        private static Texture2D _tlHeadTex, _tlKfDiamondTex, _tlKfDiamondSelTex, _tlTrackBorderTex;
        private GUIStyle _centeredStyle, _noAnimStyle, _clipStyle, _timeStyle, _speedStyle;
        private GUIStyle _boneLabelStyle, _boneLabelSelStyle;

        public SpriteSkinEditor(ModEditor parent) => _parent = parent;

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT INTEGRATION HELPERS
        // ═══════════════════════════════════════════════════════════════

        private bool _showOverrideBrowser;
        private Vector2 _overrideBrowserScroll;
        private string _overrideBrowserFilter = "";

        /// <summary>
        /// Get the active sprite dictionary. Returns mod-project sprites when
        /// a mod project is active (merged new + patches).
        /// </summary>
        private Dictionary<string, CharacterOverrideDef> GetSpriteDict()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null)
            {
                if (_mergedSprites == null) RebuildMergedSprites(proj);
                return _mergedSprites;
            }
            return null;
        }

        private Dictionary<string, CharacterOverrideDef> _mergedSprites;

        private void RebuildMergedSprites(ModProject proj)
        {
            _mergedSprites = new Dictionary<string, CharacterOverrideDef>();
            foreach (var kvp in proj.SpriteSkins)
                _mergedSprites[kvp.Key] = kvp.Value;
            foreach (var kvp in proj.SpriteSkinPatches)
                _mergedSprites[kvp.Key] = kvp.Value;
        }

        /// <summary>Mark sprite data as modified. Saves to mod project if active, else ZoneEditingService.</summary>
        private void OnSpriteModified()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(_previewNpcId))
            {
                bool isPatch = proj.SpriteSkinPatches.ContainsKey(_previewNpcId);
                CharacterOverrideDef def = null;
                if (proj.SpriteSkins.TryGetValue(_previewNpcId, out def) ||
                    proj.SpriteSkinPatches.TryGetValue(_previewNpcId, out def))
                {
                    ModProjectLoader.SaveEntity(proj, "spriteskins", _previewNpcId, def, isPatch);
                }
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
                _mergedSprites = null; // force rebuild on next access
            }
            else
            {
                ZoneEditingService.MarkDirty();
            }
        }

        /// <summary>Get NPC IDs for the "used by" display. Uses mod project NPCs.</summary>
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
            return null;
        }

        /// <summary>Get a mod ID for texture disk-path fallback.</summary>
        private string GetTextureModId()
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
        /// <summary>For SpriteSkin sprites whose rootBone is a DIFFERENT Transform:
        /// the actual bone that controls deformation. Null for non-SpriteSkin handles
        /// or SpriteSkin handles where the sprite IS its own bone.</summary>
        public Transform SkinRootTransform;
        /// <summary>Index into ovr.Grafts if this bone belongs to a graft clone, -1 for host bones.</summary>
        public int GraftIndex = -1;
        /// <summary>The host target bone this graft replaces (e.g. "Head"). Null for host bones.</summary>
        public string GraftTargetBone;
    }
}
