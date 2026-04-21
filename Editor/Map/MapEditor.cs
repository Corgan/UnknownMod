using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Core;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Self-contained map viewport that renders the zone map (background, nodes, roads)
    /// into its own RenderTexture + Camera. Works from anywhere  main menu, combat, etc.
    /// Uses ViewportRenderer for camera/RT management and RoadEditor for road visuals/data.
    ///
    /// Controls (inside viewport):
    ///   Left-drag node           Move node
    ///   Shift+click node A,B     Toggle road between two nodes
    ///   Ctrl+click empty space   Add new node
    ///   Backspace (hover node)   Delete node and its roads
    ///   Left-drag waypoint        Reshape road curve
    ///   Ctrl+click waypoint      Insert new waypoint
    ///   Backspace (hover WP)     Remove waypoint (last WP deletes road)
    ///   Right-drag               Pan view
    ///   Scroll                   Zoom
    ///   Right-click node         Inspect in NodeEditor
    ///   Escape                   Cancel connection mode
    /// </summary>
    public partial class MapEditor
    {
        private readonly ModEditor _parent;

        //  Viewport (camera + RT + zoom/pan) 
        private ViewportRenderer _vp;

        //  Road data + visuals 
        private RoadEditor _roads;

        //  Preview scene 
        private GameObject _previewRoot;
        private Transform _nodesContainer;
        private Transform _roadsContainer;
        private string _loadedZoneId;

        //  Visual layers 
        private List<VisualLayerDef> _activeLayers = new();
        private Dictionary<string, GameObject> _layerGOs = new();
        private Transform _layersContainer;

        //  Node visuals 
        private Dictionary<string, GameObject> _nodeGOs = new();

        //  Node dragging 
        private string _dragNodeId;
        private Vector2 _dragStartWorld;
        private Vector3 _dragNodeStartLocal;

        //  Waypoint dragging 
        private string _dragWPRoadKey;
        private int _dragWPIndex = -1;
        private Vector2 _dragWPStartWorld;
        private Vector3 _dragWPStartPos;

        //  Connection mode 
        private string _connectFirstId;

        //  Hover state 
        private string _hoveredNodeId;
        private string _hoveredWPRoadKey;
        private int _hoveredWPIndex = -1;

        //  Resources 
        private static readonly Dictionary<string, Sprite> _spriteCache = new(System.StringComparer.OrdinalIgnoreCase);
        private static bool _spriteCacheInit;
        private static Sprite _nodeFallbackSprite;

        //  Node SpriteRenderers (for hover/select tinting) 
        private readonly Dictionary<string, SpriteRenderer> _nodePlainSRs = new();

        //  Panel state 
        private GUIStyle _centeredStyle;
        private bool _showWaypoints = true;
        private bool _showLabels;
        private int _expandedLayerIdx = -1;

        //  Zone properties section collapse state 
        private bool _secZoneProps = false;
        private bool _secZoneOffsets = false;
        private bool _secZoneCameraBounds = false;
        private bool _secZoneTeam = false;
        private string _layerRenameBuffer = "";
        private Vector2 _layerScrollPos;

        //  Sorting layer group collapse state 
        private HashSet<string> _collapsedGroups = new();

        //  Sprite picker state (viewport overlay) 
        private VisualLayerDef _spritePickerTargetLayer;
        private GameObject _spritePickerTargetGO;
        private string _spritePickerFilter = "";
        private Vector2 _spritePickerScroll;
        private int _spritePickerPage;
        private const int SpritePickerPageSize = 60;
        private readonly Dictionary<string, Texture2D> _spritePickerThumbCache = new();

        // PrefabFX picker state
        private bool _fxPickerOpen;
        private string _fxPickerFilter = "";
        private Vector2 _fxPickerScroll;
        private string[] _fxPickerFiltered;

        //  Layer drag state (Map subtab — visual layer repositioning) 
        private string _dragLayerName;
        private Vector2 _dragLayerStartWorld;
        private Vector3 _dragLayerStartPos;
        private string _selectedLayerName;
        private string _hoveredLayerName;

        //  Layer scale handle drag state (Map subtab) 
        private string _scaleLayerName;
        private int _scaleHandleIndex = -1; // 0-7: TL,T,TR,R,BR,B,BL,L
        private Vector2 _scaleStartWorld;
        private float _scaleStartScaleX, _scaleStartScaleY;
        private float _scaleStartPosX, _scaleStartPosY;
        private Bounds _scaleStartBounds;
        private const float ScaleHandleSizePx = 8f;

        //  Camera bounds overlay 
        private bool _showCameraBounds = true;
        /// <summary>Game camera ortho size (fixed at 5.4).</summary>
        private const float GameOrthoSize = 5.4f;
        /// <summary>Game aspect ratio (16:9).</summary>
        private const float GameAspect = 16f / 9f;

        //  Layer overlap cycling (Map subtab) 
        private List<string> _layerOverlapCandidates = new();
        private int _layerOverlapCycleIndex;

        //  Snap-to-node state (road editing) 
        private bool _snapActive;
        private string _snapNodeId;
        private Vector3 _snapNodePos;

        //  Overlapping endpoint cycling 
        private List<(string roadKey, int index)> _overlapCandidates = new();
        private int _overlapCycleIndex;

        //  Light2D type names (matches enum in UnityEngine.Rendering.Universal) 
        internal static readonly string[] LightTypeNames = { "Parametric", "Freeform", "Sprite", "Point", "Global" };

        //  Game sorting layers (back → front) with their Unity values 
        //  Only the 3 relevant to map editing are offered in the dropdown.
        internal static readonly string[] SortingLayerNames = { "Map", "Default", "UI" };
        private static readonly Dictionary<string, int> SortingLayerValues = new()
        {
            { "Background", -6 }, { "Characters", -5 }, { "Map", -4 },
            { "GameObjetcs", -3 }, { "Discards", -2 }, { "Cards", -1 },
            { "Default", 0 }, { "Book", 1 }, { "UI", 2 },
        };

        /// <summary>Get the rendering priority value for a sorting layer name (higher = renders later/on top).</summary>
        internal static int GetSortingLayerValue(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return -4; // default to Map
            return SortingLayerValues.TryGetValue(layerName, out var val) ? val : -4;
        }

        //  MapPiece sprite cache 
        private readonly Dictionary<string, Sprite> _mapPieceSprites = new(System.StringComparer.OrdinalIgnoreCase);

        //  Constants 
        private static readonly Vector3 PreviewOrigin = new(-5000f, -5000f, 0f);
        private static readonly Vector3 NodeIconOffset = new(0.01f, 0.24f, 0f);
        private const float NodePickRadiusPx = 24f;
        private const float WPPickRadiusPx = 12f;
        private const float SnapRadiusPx = 28f;

        //  Colors 
        private static readonly Color NodeNormalColor    = Color.white;
        private static readonly Color NodeEntranceColor  = new(1f,   0.92f, 0.6f,  1f);
        private static readonly Color NodeTownColor      = new(0.7f, 0.85f, 1f,    1f);
        private static readonly Color NodeEmptyColor     = new(0.55f, 0.55f, 0.55f, 0.8f);
        private static readonly Color NodeSelectedColor  = new(1f,   1f,    0.4f,  1f);
        private static readonly Color NodeHoveredColor   = new(1f,   1f,    0.8f,  1f);
        private static readonly Color RoadColor          = new(0f,   0.972f, 1f,   0.863f);
        private static readonly Color WaypointHandleColor = new(1f,   0.9f,  0f,    0.9f);

        public MapEditor(ModEditor parent)
        {
            _parent = parent;
            _vp = new ViewportRenderer(PreviewOrigin, 1280, 720, 5.4f,
                new Color(0.1f, 0.1f, 0.12f, 1f), -101f);
        }

        /// <summary>Force a full viewport rebuild on the next frame.</summary>
        public void ForceRebuild() => _loadedZoneId = null;

        /// <summary>Number of roads currently in the viewport.</summary>
        public int RoadCount => _roads?.RoadWaypoints?.Count ?? 0;

        /// <summary>First node in an active connection operation (null if not connecting).</summary>
        public string ConnectFirstId => _connectFirstId;

    }
}
