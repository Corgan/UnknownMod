using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnknownMod.Core;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Top-level MonoBehaviour that manages the mod editor UI.
    /// Coordinates between category tab coordinators (Mods, Combat, Characters, World, Zones, Sprites)
    /// and sub-editors. Owns the viewport split, tab bar, and input routing.
    ///
    /// Each tab coordinator owns its own save/hot-reload logic via HandleChanges() and Tick().
    ///
    /// Controls:
    ///   F9          Toggle editor on/off
    ///   `           Mods tab
    ///   1-5         Switch between category tabs
    /// </summary>
    public class ModEditor : MonoBehaviour
    {
        // ── Static state ─────────────────────────────────────────────
        public static bool IsEditing { get; private set; }
        public static ModEditor Instance { get; private set; }

        /// <summary>Shared renderer for game-accurate entity previews (cards, NPCs, etc.).</summary>
        public static EntityPreviewRenderer EntityPreview { get; private set; }

        /// <summary>True during frames where the mouse is over any editor GUI rect.
        /// Checked by MapEditor to suppress world-space input.</summary>
        public static bool IsMouseOverUI { get; private set; }

        // ── Active tab ───────────────────────────────────────────────
        public enum EditorTab { ModManager, Combat, Characters, World, Zones, Sprites }
        public EditorTab ActiveTab { get; set; } = EditorTab.Zones;

        // ── Category tab coordinators ────────────────────────────────
        public ModManagerPanel ModManager { get; private set; }
        public CombatTabEditor CombatTab { get; private set; }
        public CharacterTabEditor CharacterTab { get; private set; }
        public WorldTabEditor WorldTab { get; private set; }
        public ZoneTabEditor ZoneTab { get; private set; }

        // ── Sub-editor references ────────────────────────────────────
        public MapEditor MapEdit { get; private set; }
        public MapViewport MapView { get; private set; }
        public NodeEditor NodeEdit { get; private set; }
        public EventEditor EventEdit { get; private set; }
        public EncounterEditor EncounterEdit { get; private set; }
        public NpcEditor NpcEdit { get; private set; }
        public SpriteEditor SpriteEdit { get; private set; }
        public CardEditor CardEdit { get; private set; }
        public ItemEditor ItemEdit { get; private set; }
        public LootEditor LootEdit { get; private set; }
        public AuraCurseEditor AuraCurseEdit { get; private set; }
        public HeroEditor HeroEdit { get; private set; }
        public TraitEditor TraitEdit { get; private set; }
        public SkinEditor SkinEdit { get; private set; }
        public PerkEditor PerkEdit { get; private set; }
        public PerkNodeEditor PerkNodeEdit { get; private set; }
        public RequirementEditor RequirementEdit { get; private set; }
        public CardbackEditor CardbackEdit { get; private set; }
        public TierRewardEditor TierRewardEdit { get; private set; }

        // ── Selection state (shared between sub-editors) ─────────────
        public string SelectedNodeId { get; set; }
        public string SelectedEventId { get; set; }
        public string SelectedCombatId { get; set; }
        public string SelectedNpcId { get; set; }
        public string SelectedCardId { get; set; }
        public string SelectedItemId { get; set; }
        public string SelectedLootId { get; set; }
        public string SelectedAuraCurseId { get; set; }

        // ── GUI state ────────────────────────────────────────────────
        private GUIStyle _headerStyle;
        private GUIStyle _tabStyle;
        private GUIStyle _tabActiveStyle;
        private GUIStyle _boxStyle;
        private Vector2 _panelScroll;
        private bool _stylesInitialized;
        private static Texture2D _boxTex;

        // ── Panel layout ─────────────────────────────────────────────
        private const float PanelTop = 50f;
        private const float TabBarHeight = 30f;

        // ── Layout ────────────────────────────────────────────────────
        /// <summary>Fraction of screen width used by the viewport (left side). The editor panel fills the rest.</summary>
        private const float ViewportFraction = 0.70f;

        /// <summary>Full-screen Canvas + Image that blocks all Unity UI raycasts
        /// (EventSystem) when the editor is active. Sits at a high sort order so
        /// game buttons (logout, etc.) can't be clicked through the overlay.</summary>
        private GameObject _uiBlocker;

        // ═══════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        void Awake()
        {
            // Ensure only one instance exists (persistent across scenes)
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Create sub-editors (MapEditor is lazily attached to zone map root)
            MapView = new MapViewport(this);
            NodeEdit = new NodeEditor(this);
            EventEdit = new EventEditor(this);
            EncounterEdit = new EncounterEditor(this);
            NpcEdit = new NpcEditor(this);
            SpriteEdit = new SpriteEditor(this);
            CardEdit = new CardEditor(this);
            ItemEdit = new ItemEditor(this);
            LootEdit = new LootEditor(this);
            AuraCurseEdit = new AuraCurseEditor(this);
            HeroEdit = new HeroEditor(this);
            TraitEdit = new TraitEditor(this);
            SkinEdit = new SkinEditor(this);
            PerkEdit = new PerkEditor(this);
            PerkNodeEdit = new PerkNodeEditor(this);
            RequirementEdit = new RequirementEditor(this);
            CardbackEdit = new CardbackEditor(this);
            TierRewardEdit = new TierRewardEditor(this);

            // Create category tab coordinators
            ModManager = new ModManagerPanel(this);
            CombatTab = new CombatTabEditor(this);
            CharacterTab = new CharacterTabEditor(this);
            WorldTab = new WorldTabEditor(this);
            ZoneTab = new ZoneTabEditor(this);

            // Initialize mod system
            ModManager.Initialize();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            EntityPreview?.Dispose();
            EntityPreview = null;
            SpriteEdit?.Cleanup();
            MapView?.Cleanup();
        }

        /// <summary>
        /// Attach or re-attach MapEditor to a zone map root GameObject.
        /// Called by map builders after constructing the zone map.
        /// </summary>
        public void AttachMapEditor(GameObject mapRoot)
        {
            // Clean up old MapEditor if it was on a previous (destroyed) GO
            if (MapEdit != null && MapEdit.gameObject == null)
                MapEdit = null;

            if (MapEdit == null)
            {
                MapEdit = mapRoot.AddComponent<MapEditor>();
                if (IsEditing)
                    MapEdit.SetEditMode(true);
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9)) ToggleEditMode();

            if (!IsEditing) return;

            // Tick the active tab (auto-save timers, etc.)
            switch (ActiveTab)
            {
                case EditorTab.ModManager:  ModManager?.Tick(); break;
                case EditorTab.Combat:      CombatTab?.Tick(); break;
                case EditorTab.Characters:  CharacterTab?.Tick(); break;
                case EditorTab.World:       WorldTab?.Tick(); break;
                case EditorTab.Zones:       ZoneTab?.Tick(); break;
            }

            // Number keys to switch tabs
            if (Input.GetKeyDown(KeyCode.BackQuote)) ActiveTab = EditorTab.ModManager;
            if (Input.GetKeyDown(KeyCode.Alpha1)) ActiveTab = EditorTab.Combat;
            if (Input.GetKeyDown(KeyCode.Alpha2)) ActiveTab = EditorTab.Characters;
            if (Input.GetKeyDown(KeyCode.Alpha3)) ActiveTab = EditorTab.World;
            if (Input.GetKeyDown(KeyCode.Alpha4)) ActiveTab = EditorTab.Zones;
            if (Input.GetKeyDown(KeyCode.Alpha5)) ActiveTab = EditorTab.Sprites;
        }

        // ═══════════════════════════════════════════════════════════════
        //  TOGGLE
        // ═══════════════════════════════════════════════════════════════

        private void ToggleEditMode()
        {
            IsEditing = !IsEditing;
            Plugin.Log.LogInfo($"[ModEditor] Edit mode: {(IsEditing ? "ON" : "OFF")}");

            // Forward to MapEditor (it manages road CPs and visual state)
            if (MapEdit != null)
                MapEdit.SetEditMode(IsEditing);

            // Entity preview renderer lifecycle
            if (IsEditing)
            {
                if (EntityPreview == null)
                    EntityPreview = new EntityPreviewRenderer();
            }
            else
            {
                EntityPreview?.Dispose();
                EntityPreview = null;
            }

            // Show/hide full-screen UI blocker
            SetUIBlocker(IsEditing);
        }

        /// <summary>
        /// Create or toggle a full-screen Canvas overlay that intercepts all
        /// Unity EventSystem raycasts, preventing game UI (buttons, etc.) from
        /// receiving clicks while the editor is open.
        /// </summary>
        private void SetUIBlocker(bool active)
        {
            if (_uiBlocker == null)
            {
                _uiBlocker = new GameObject("[ModEditor] UIBlocker");
                _uiBlocker.transform.SetParent(transform);

                var canvas = _uiBlocker.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000; // above all game canvases

                _uiBlocker.AddComponent<GraphicRaycaster>();

                // Full-screen transparent image as raycast target
                var imgGO = new GameObject("Blocker");
                imgGO.transform.SetParent(_uiBlocker.transform, false);
                var img = imgGO.AddComponent<Image>();
                img.color = Color.clear; // invisible
                img.raycastTarget = true;

                var rt = imgGO.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            _uiBlocker.SetActive(active);
        }

        // ═══════════════════════════════════════════════════════════════
        //  SELECTION HELPERS
        // ═══════════════════════════════════════════════════════════════

        public void SelectNode(string nodeId)
        {
            SelectedNodeId = nodeId;
        }

        public void InspectNode(string nodeId)
        {
            SelectedNodeId = nodeId;
            ActiveTab = EditorTab.Zones;
            ZoneTab.ActiveSubTab = ZoneTabEditor.SubTab.Node;
        }

        public void InspectEvent(string eventId)
        {
            SelectedEventId = eventId;
            ActiveTab = EditorTab.Zones;
            ZoneTab.ActiveSubTab = ZoneTabEditor.SubTab.Event;
        }

        public void InspectCombat(string combatId)
        {
            SelectedCombatId = combatId;
            ActiveTab = EditorTab.Zones;
            ZoneTab.ActiveSubTab = ZoneTabEditor.SubTab.Encounter;
        }

        public void InspectNpc(string npcId)
        {
            SelectedNpcId = npcId;
            ActiveTab = EditorTab.Combat;
            CombatTab.ActiveSubTab = CombatTabEditor.SubTab.NPCs;
        }

        public void InspectCard(string cardId)
        {
            SelectedCardId = cardId;
            ActiveTab = EditorTab.Combat;
            CombatTab.ActiveSubTab = CombatTabEditor.SubTab.Cards;
        }

        public void InspectItem(string itemId)
        {
            SelectedItemId = itemId;
            ActiveTab = EditorTab.Combat;
            CombatTab.ActiveSubTab = CombatTabEditor.SubTab.Items;
        }

        public void InspectLoot(string lootId)
        {
            SelectedLootId = lootId;
            ActiveTab = EditorTab.Combat;
            CombatTab.ActiveSubTab = CombatTabEditor.SubTab.Loot;
        }

        public void InspectAuraCurse(string acId)
        {
            SelectedAuraCurseId = acId;
            ActiveTab = EditorTab.Combat;
            CombatTab.ActiveSubTab = CombatTabEditor.SubTab.AuraCurse;
        }

        // ═══════════════════════════════════════════════════════════════
        //  GUI
        // ═══════════════════════════════════════════════════════════════

        void OnGUI()
        {
            if (!IsEditing) return;
            InitStyles();

            float screenW = Screen.width;
            float screenH = Screen.height;
            float panelH = screenH - PanelTop - 20f;

            // ── Editor lives in the right portion of the screen ──────
            float editorLeft = screenW * ViewportFraction;
            float editorWidth = screenW - editorLeft;

            // ── Compute UI rects for hit-testing ─────────────────────
            Rect headerRect = new Rect(editorLeft, 10, editorWidth, 35);
            float tabX = editorLeft;
            Rect tabBarRect = new Rect(tabX, PanelTop, editorWidth, TabBarHeight);
            float panelY = PanelTop + TabBarHeight;
            Rect panelRect = new Rect(tabX, panelY, editorWidth, panelH - TabBarHeight);

            // ── Header bar ───────────────────────────────────────────
            GUI.Box(headerRect, "", _boxStyle);
            string dirtyMark = ZoneEditingService.IsDirty ? " <color=yellow>●</color>" : "";
            string modLabel = Tabs.ModManagerPanel.ActiveProject != null
                ? $"  <color=#aaa>mod: {Tabs.ModManagerPanel.ActiveProject.ModId}</color>" : "";
            GUI.Label(new Rect(editorLeft + 5, 14, editorWidth - 10, 25),
                $"<b>MOD EDITOR</b>  |  `=Mods  1-5=tabs  |  F9=close{dirtyMark}{modLabel}",
                _headerStyle);

            // ── Tab bar ──────────────────────────────────────────────
            var tabs = new[] { "Mods", "Combat", "Chars", "World", "Zones", "Sprites" };
            float tabW = editorWidth / tabs.Length;

            for (int i = 0; i < tabs.Length; i++)
            {
                var style = (EditorTab)i == ActiveTab ? _tabActiveStyle : _tabStyle;
                if (GUI.Button(new Rect(tabX + i * tabW, PanelTop, tabW, TabBarHeight), tabs[i], style))
                {
                    ActiveTab = (EditorTab)i;
                    PopupState.Close();
                }
            }

            // ── Viewport (left side of screen, always visible) ───────
            float vpY = PanelTop + TabBarHeight + 2;
            Rect vpRect = new Rect(10, vpY, editorLeft - 25, screenH - vpY - 10);

            // Set the flag: is the mouse inside any editor GUI rect?
            Vector2 mouse = Event.current.mousePosition;
            IsMouseOverUI = headerRect.Contains(mouse) ||
                            tabBarRect.Contains(mouse) ||
                            panelRect.Contains(mouse) ||
                            vpRect.Contains(mouse);

            // Draw viewport for active tab
            switch (ActiveTab)
            {
                case EditorTab.ModManager:
                    ModManager?.DrawViewport(vpRect);
                    break;
                case EditorTab.Combat:
                    CombatTab?.DrawViewport(vpRect);
                    break;
                case EditorTab.Characters:
                    CharacterTab?.DrawViewport(vpRect);
                    break;
                case EditorTab.World:
                    WorldTab?.DrawViewport(vpRect);
                    break;
                case EditorTab.Zones:
                    ZoneTab?.DrawViewport(vpRect);
                    break;
                case EditorTab.Sprites:
                    SpriteEdit?.DrawViewport(vpRect);
                    break;
            }

            // ── Panel ────────────────────────────────────────────────
            GUI.Box(panelRect, "", _boxStyle);

            Rect innerRect = new Rect(tabX + 5, panelY + 5, editorWidth - 10, panelH - TabBarHeight - 10);

            GUILayout.BeginArea(innerRect);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);

            // Track GUI changes for hot-reload
            GUI.changed = false;

            switch (ActiveTab)
            {
                case EditorTab.ModManager:
                    ModManager?.DrawPanel();
                    break;
                case EditorTab.Combat:
                    CombatTab?.DrawPanel();
                    CombatTab?.HandleChanges();
                    break;
                case EditorTab.Characters:
                    CharacterTab?.DrawPanel();
                    CharacterTab?.HandleChanges();
                    break;
                case EditorTab.World:
                    WorldTab?.DrawPanel();
                    WorldTab?.HandleChanges();
                    break;
                case EditorTab.Zones:
                    ZoneTab?.DrawPanel();
                    ZoneTab?.HandleChanges();
                    break;
                case EditorTab.Sprites:
                    SpriteEdit?.DrawPanel();
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // ── Block click-through ──────────────────────────────────
            // IMGUI GUI.Box / BeginArea don't consume mouse events.
            // Eat any unclaimed mouse/scroll events over our UI so they
            // don't pass through to the game world behind the panel.
            if (IsMouseOverUI)
            {
                switch (Event.current.type)
                {
                    case EventType.MouseDown:
                    case EventType.MouseUp:
                    case EventType.MouseDrag:
                    case EventType.ScrollWheel:
                        Event.current.Use();
                        break;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  STYLES
        // ═══════════════════════════════════════════════════════════════

        private void InitStyles()
        {
            // Re-init styles when the GUI skin is rebuilt (e.g., after resize or scene change)
            if (_stylesInitialized && _boxStyle?.normal?.background != null) return;
            _stylesInitialized = true;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                richText = true,
                normal = { textColor = Color.white }
            };

            _tabStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
            };

            _tabActiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };

            if (_boxTex == null)
                _boxTex = MakeTex(2, 2, new Color(0.1f, 0.1f, 0.12f, 0.92f));

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _boxTex }
            };
        }

        public static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
    }
}
