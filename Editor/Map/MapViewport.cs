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
    ///   Left-drag CP handle      Reshape road curve
    ///   Ctrl+click CP            Insert new control point
    ///   Delete (hover CP)        Remove control point (last CP deletes road)
    ///   Right-drag               Pan view
    ///   Scroll                   Zoom
    ///   Right-click node         Inspect in NodeEditor
    ///   Escape                   Cancel connection mode
    /// </summary>
    public partial class MapViewport
    {
        private readonly ModEditor _parent;

        //  Viewport (camera + RT + zoom/pan) 
        private ViewportRenderer _vp;

        //  Road data + visuals 
        private RoadEditor _roads;

        //  Preview scene 
        private GameObject _previewRoot;
        private GameObject _bgGO;
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

        //  CP dragging 
        private string _dragCPRoadKey;
        private int _dragCPIndex = -1;
        private Vector2 _dragCPStartWorld;
        private Vector3 _dragCPStartPos;

        //  Connection mode 
        private string _connectFirstId;

        //  Hover state 
        private string _hoveredNodeId;
        private string _hoveredCPRoadKey;
        private int _hoveredCPIndex = -1;

        //  Resources 
        private static readonly Dictionary<string, Sprite> _spriteCache = new(System.StringComparer.OrdinalIgnoreCase);
        private static bool _spriteCacheInit;
        private static Sprite _nodeFallbackSprite;

        //  Node SpriteRenderers (for hover/select tinting) 
        private readonly Dictionary<string, SpriteRenderer> _nodePlainSRs = new();

        //  Panel state 
        private GUIStyle _centeredStyle;
        private bool _showCPs = true;
        private bool _showLabels;
        private bool _showLayers = true;
        private int _expandedLayerIdx = -1;
        private string _layerRenameBuffer = "";
        private Vector2 _layerScrollPos;

        //  MapPiece sprite cache 
        private readonly Dictionary<string, Sprite> _mapPieceSprites = new(System.StringComparer.OrdinalIgnoreCase);

        //  Constants 
        private static readonly Vector3 PreviewOrigin = new(-5000f, -5000f, 0f);
        private static readonly Vector3 NodeIconOffset = new(0.01f, 0.24f, 0f);
        private const float NodePickRadiusPx = 24f;
        private const float CPPickRadiusPx = 12f;

        //  Colors 
        private static readonly Color NodeNormalColor    = Color.white;
        private static readonly Color NodeEntranceColor  = new(1f,   0.92f, 0.6f,  1f);
        private static readonly Color NodeTownColor      = new(0.7f, 0.85f, 1f,    1f);
        private static readonly Color NodeEmptyColor     = new(0.55f, 0.55f, 0.55f, 0.8f);
        private static readonly Color NodeSelectedColor  = new(1f,   1f,    0.4f,  1f);
        private static readonly Color NodeHoveredColor   = new(1f,   1f,    0.8f,  1f);
        private static readonly Color RoadColor          = new(0f,   0.972f, 1f,   0.863f);
        private static readonly Color CPHandleColor      = new(1f,   0.9f,  0f,    0.9f);

        public MapViewport(ModEditor parent)
        {
            _parent = parent;
            _vp = new ViewportRenderer(PreviewOrigin, 1280, 720, 5.4f,
                new Color(0.1f, 0.1f, 0.12f, 1f), -101f);
        }

    }
}
