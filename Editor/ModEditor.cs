using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnknownMod.Core;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Top-level MonoBehaviour that manages the mod editor UI.
    /// Coordinates between category tab coordinators (Mods, Cards, Heroes, Enemies, Player, Zones)
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
        /// Checked by sub-editors to suppress world-space input.</summary>
        public static bool IsMouseOverUI { get; private set; }

        // ── Active tab ───────────────────────────────────────────────
        public enum EditorTab { ModManager, Cards, Heroes, Enemies, Player, World, GraftSprites, Extras, Assets }
        public EditorTab ActiveTab { get; set; } = EditorTab.ModManager;

        // ── Category tab coordinators ────────────────────────────────
        public ModManagerPanel ModManager { get; private set; }
        public CardsTabEditor CardsTab { get; private set; }
        public HeroesTabEditor HeroesTab { get; private set; }
        public EnemiesTabEditor EnemiesTab { get; private set; }
        public PlayerTabEditor PlayerTab { get; private set; }
        public ZoneTabEditor ZoneTab { get; private set; }
        public ExtrasTabEditor ExtrasTab { get; private set; }
        public AssetsTabEditor AssetsTab { get; private set; }

        // ── Sub-editor references ────────────────────────────────────
        public MapEditor MapEdit { get; private set; }
        public NodeEditor NodeEdit { get; private set; }
        public EventEditor EventEdit { get; private set; }
        public EncounterEditor EncounterEdit { get; private set; }
        public NpcEditor NpcEdit { get; private set; }
        public VisualGraphEditor SpriteEdit { get; private set; }
        public CardEditor CardEdit { get; private set; }
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
        public PackEditor PackEdit { get; private set; }
        public CardPlayerPackEditor CardPlayerPackEdit { get; private set; }
        public HeroDataEditor HeroDataEdit { get; private set; }
        public BackgroundEditor BackgroundEdit { get; private set; }

        // ── Selection state (shared between sub-editors) ─────────────
        public string SelectedNodeId { get; set; }
        public string SelectedEventId { get; set; }
        public string SelectedCombatId { get; set; }
        public string SelectedNpcId { get; set; }
        public string SelectedCardId { get; set; }
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
        private int _splitterId;
        private bool _tabRestored;
        private static readonly string[] TabNames = { "Mods", "Cards", "Heroes", "Enemies", "Player", "World", "Graft Sprites", "Extras", "Assets" };

        /// <summary>Each tab keeps its own panel scroll position, so switching tabs does not lose your place.</summary>
        private readonly System.Collections.Generic.Dictionary<EditorTab, Vector2> _tabScroll = new System.Collections.Generic.Dictionary<EditorTab, Vector2>();

        // ── Navigation back stack ────────────────────────────────────
        private struct NavState
        {
            public EditorTab Tab;
            public ZoneTabEditor.SubTab Zone; public ZoneTabEditor.ZoneInnerTab ZoneInner;
            public EnemiesTabEditor.SubTab Enemies;
            public CardsTabEditor.SubTab Cards; public CardsTabEditor.EquipmentSub Equipment;
            public string Node, Event, Combat, Npc, Card, Loot, Aura;
            public bool SameAs(NavState o) =>
                Tab == o.Tab && Zone == o.Zone && ZoneInner == o.ZoneInner && Enemies == o.Enemies && Cards == o.Cards && Equipment == o.Equipment
                && Node == o.Node && Event == o.Event && Combat == o.Combat && Npc == o.Npc && Card == o.Card && Loot == o.Loot && Aura == o.Aura;
        }

        private readonly System.Collections.Generic.List<NavState> _back = new System.Collections.Generic.List<NavState>();
        private const int MaxBack = 30;

        public bool CanGoBack => _back.Count > 0;

        private NavState CaptureNav() => new NavState
        {
            Tab = ActiveTab,
            Zone = ZoneTab.ActiveSubTab, ZoneInner = ZoneTab.ActiveZoneInnerTab,
            Enemies = EnemiesTab.ActiveSubTab,
            Cards = CardsTab.ActiveSubTab, Equipment = CardsTab.ActiveEquipmentSub,
            Node = SelectedNodeId, Event = SelectedEventId, Combat = SelectedCombatId, Npc = SelectedNpcId,
            Card = SelectedCardId, Loot = SelectedLootId, Aura = SelectedAuraCurseId,
        };

        /// <summary>Remember where the user is before an Inspect* jump so Back can return there.</summary>
        private void PushNav()
        {
            if (ZoneTab == null || EnemiesTab == null || CardsTab == null) return;
            var state = CaptureNav();
            if (_back.Count > 0 && _back[_back.Count - 1].SameAs(state)) return;
            _back.Add(state);
            if (_back.Count > MaxBack) _back.RemoveAt(0);
        }

        public void GoBack()
        {
            if (_back.Count == 0) return;
            var s = _back[_back.Count - 1];
            _back.RemoveAt(_back.Count - 1);
            ActiveTab = s.Tab;
            ZoneTab.ActiveSubTab = s.Zone; ZoneTab.ActiveZoneInnerTab = s.ZoneInner;
            EnemiesTab.ActiveSubTab = s.Enemies;
            CardsTab.ActiveSubTab = s.Cards; CardsTab.ActiveEquipmentSub = s.Equipment;
            SelectedNodeId = s.Node; SelectedEventId = s.Event; SelectedCombatId = s.Combat; SelectedNpcId = s.Npc;
            SelectedCardId = s.Card; SelectedLootId = s.Loot; SelectedAuraCurseId = s.Aura;
            PopupState.Close();
        }

        /// <summary>Ctrl+S: flush everything that is debounced and rewrite the mod metadata.</summary>
        public void SaveAll()
        {
            RenameFlush.FlushAll();
            SpriteEdit?.FlushPendingChanges();
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null)
            {
                try { ModProjectLoader.SaveMetadata(proj); proj.IsDirty = false; }
                catch (System.Exception ex) { Plugin.Log.LogError($"[ModEditor] Save failed: {ex.Message}"); }
            }
            EditorStatus.Toast(proj != null ? $"Saved {proj.ModId}" : "Nothing to save", proj == null);
        }

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

            // Create sub-editors
            MapEdit = new MapEditor(this);
            NodeEdit = new NodeEditor(this);
            EventEdit = new EventEditor(this);
            EncounterEdit = new EncounterEditor(this);
            NpcEdit = new NpcEditor(this);
            SpriteEdit = new VisualGraphEditor(this);
            CardEdit = new CardEditor(this);
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
            PackEdit = new PackEditor(this);
            CardPlayerPackEdit = new CardPlayerPackEditor(this);
            HeroDataEdit = new HeroDataEditor(this);
            BackgroundEdit = new BackgroundEditor(this);

            // Create category tab coordinators
            ModManager = new ModManagerPanel(this);
            CardsTab = new CardsTabEditor(this);
            HeroesTab = new HeroesTabEditor(this);
            EnemiesTab = new EnemiesTabEditor(this);
            PlayerTab = new PlayerTabEditor(this);
            ZoneTab = new ZoneTabEditor(this);
            ExtrasTab = new ExtrasTabEditor(this);
            AssetsTab = new AssetsTabEditor(this);

            // Initialize mod system
            ModManager.Initialize();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            EntityPreview?.Dispose();
            EntityPreview = null;
            SpriteEdit?.Cleanup();
            MapEdit?.Cleanup();
            BackgroundEdit?.Cleanup();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9) && (IsEditing || !GameTextFieldFocused())) ToggleEditMode();

            if (!IsEditing) return;

            // Visual graph saves are debounced and must still flush after switching tabs.
            SpriteEdit?.Tick();
            if (ActiveTab != EditorTab.GraftSprites) SpriteEdit?.ReleasePreview();

            // Tick the active tab (auto-save timers, etc.)
            switch (ActiveTab)
            {
                case EditorTab.ModManager:  ModManager?.Tick(); break;
                case EditorTab.Cards:       CardsTab?.Tick(); break;
                case EditorTab.Heroes:      HeroesTab?.Tick(); break;
                case EditorTab.Enemies:     EnemiesTab?.Tick(); break;
                case EditorTab.Player:      PlayerTab?.Tick(); break;
                case EditorTab.World:       ZoneTab?.Tick(); break;
                case EditorTab.GraftSprites: break;
                case EditorTab.Extras:      ExtrasTab?.Tick(); break;
                case EditorTab.Assets:      AssetsTab?.Tick(); break;
            }

            // Number keys to switch tabs
            if (GUIUtility.keyboardControl == 0 && !_layoutMenuOpen)
            {
                if (Input.GetKeyDown(KeyCode.BackQuote)) ActiveTab = EditorTab.ModManager;
                if (Input.GetKeyDown(KeyCode.Alpha1)) ActiveTab = EditorTab.Cards;
                if (Input.GetKeyDown(KeyCode.Alpha2)) ActiveTab = EditorTab.Heroes;
                if (Input.GetKeyDown(KeyCode.Alpha3)) ActiveTab = EditorTab.Enemies;
                if (Input.GetKeyDown(KeyCode.Alpha4)) ActiveTab = EditorTab.Player;
                if (Input.GetKeyDown(KeyCode.Alpha5)) ActiveTab = EditorTab.World;
                if (Input.GetKeyDown(KeyCode.Alpha6)) ActiveTab = EditorTab.GraftSprites;
                if (Input.GetKeyDown(KeyCode.Alpha7)) ActiveTab = EditorTab.Extras;
                if (Input.GetKeyDown(KeyCode.Alpha8)) ActiveTab = EditorTab.Assets;
            }

            HandleShortcuts();
        }

        /// <summary>
        /// Ctrl+S save, Ctrl+D duplicate, Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) undo / redo, Alt+Left back.
        /// Undo, redo and duplicate are skipped while a text box has focus so they do not fight the box's own undo.
        /// The Graft Sprites tab has its own undo, which works on the viewport.
        /// </summary>
        private void HandleShortcuts()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool typing = GUIUtility.keyboardControl != 0;
            if (_layoutMenuOpen) return;

            if (ctrl && Input.GetKeyDown(KeyCode.S)) SaveAll();
            if (alt && Input.GetKeyDown(KeyCode.LeftArrow) && !typing) GoBack();
            if (typing || !ctrl || ActiveTab == EditorTab.GraftSprites) return;
            if (Input.GetKeyDown(KeyCode.D)) EditorActions.Pending = EditorCommand.Duplicate;
            else if (Input.GetKeyDown(KeyCode.Y) || (shift && Input.GetKeyDown(KeyCode.Z))) EditorActions.Pending = EditorCommand.Redo;
            else if (Input.GetKeyDown(KeyCode.Z)) EditorActions.Pending = EditorCommand.Undo;
        }

        // ═══════════════════════════════════════════════════════════════
        //  TOGGLE
        // ═══════════════════════════════════════════════════════════════

        private static bool GameTextFieldFocused()
        {
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (sel == null) return false;
            var tmp = sel.GetComponent<TMPro.TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var legacy = sel.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
        }

        private void ToggleEditMode()
        {
            if (IsEditing)
            {
                SpriteEdit?.FlushPendingChanges();
                SpriteEdit?.ReleasePreview();
            }

            IsEditing = !IsEditing;
            Plugin.Log.LogInfo($"[ModEditor] Edit mode: {(IsEditing ? "ON" : "OFF")}");

            // Entity preview renderer lifecycle
            if (IsEditing)
            {
                if (EntityPreview == null)
                    EntityPreview = new EntityPreviewRenderer();

                // Preload Map + Combat scenes to cache base-game zone data and
                // combat backgrounds eagerly, instead of waiting for user to open a zone
                ZoneEditingService.PreloadScenes();
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
            PushNav();
            SelectedNodeId = nodeId;
            ActiveTab = EditorTab.World;
            ZoneTab.ActiveSubTab = ZoneTabEditor.SubTab.Zones;
            ZoneTab.ActiveZoneInnerTab = ZoneTabEditor.ZoneInnerTab.Node;
        }

        public void InspectEvent(string eventId)
        {
            PushNav();
            SelectedEventId = eventId;
            ActiveTab = EditorTab.World;
            ZoneTab.ActiveSubTab = ZoneTabEditor.SubTab.Event;
        }

        public void InspectCombat(string combatId)
        {
            PushNav();
            SelectedCombatId = combatId;
            ActiveTab = EditorTab.World;
            ZoneTab.ActiveSubTab = ZoneTabEditor.SubTab.Encounter;
        }

        public void InspectNpc(string npcId)
        {
            PushNav();
            SelectedNpcId = npcId;
            ActiveTab = EditorTab.Enemies;
            EnemiesTab.ActiveSubTab = EnemiesTabEditor.SubTab.NPCs;
        }

        public void InspectCard(string cardId)
        {
            PushNav();
            SelectedCardId = cardId;
            ActiveTab = EditorTab.Cards;
            CardsTab.ActiveSubTab = CardsTabEditor.SubTab.Hero;
        }

        public void InspectItem(string itemId)
        {
            PushNav();
            SelectedCardId = itemId;
            ActiveTab = EditorTab.Cards;
            CardsTab.ActiveSubTab = CardsTabEditor.SubTab.Equipment;
            CardsTab.ActiveEquipmentSub = CardsTabEditor.EquipmentSub.Items;
        }

        public void InspectEnchantment(string enchantId)
        {
            PushNav();
            SelectedCardId = enchantId;
            ActiveTab = EditorTab.Cards;
            CardsTab.ActiveSubTab = CardsTabEditor.SubTab.Equipment;
            CardsTab.ActiveEquipmentSub = CardsTabEditor.EquipmentSub.Enchantments;
        }

        public void InspectPet(string petId)
        {
            PushNav();
            SelectedCardId = petId;
            ActiveTab = EditorTab.Cards;
            CardsTab.ActiveSubTab = CardsTabEditor.SubTab.Equipment;
            CardsTab.ActiveEquipmentSub = CardsTabEditor.EquipmentSub.Pets;
        }

        public void InspectLoot(string lootId)
        {
            PushNav();
            SelectedLootId = lootId;
            ActiveTab = EditorTab.Enemies;
            EnemiesTab.ActiveSubTab = EnemiesTabEditor.SubTab.Loot;
        }

        /// <summary>Opens the Test Fight panel with a combat ("combat") or an enemy ("npc") already chosen.</summary>
        public void OpenTestFight(string kind, string id)
        {
            PushNav();
            ActiveTab = EditorTab.Extras;
            ExtrasTab.ActiveSubTab = ExtrasTabEditor.SubTab.TestFight;
            ExtrasTab.TestFight.Preset(kind, id);
        }

        /// <summary>Opens the combat simulator with a combat ("combat") or an enemy ("npc") already chosen.</summary>
        public void OpenSimulator(string kind, string id)
        {
            PushNav();
            ActiveTab = EditorTab.Extras;
            ExtrasTab.ActiveSubTab = ExtrasTabEditor.SubTab.Simulator;
            ExtrasTab.Simulator.Preset(kind, id);
        }

        /// <summary>Opens the reachability checker on a zone.</summary>
        public void OpenReachability(string zoneId)
        {
            PushNav();
            ActiveTab = EditorTab.Extras;
            ExtrasTab.ActiveSubTab = ExtrasTabEditor.SubTab.Reachability;
            ExtrasTab.Reachability.Preset(zoneId);
        }

        public void InspectAuraCurse(string acId)
        {
            PushNav();
            SelectedAuraCurseId = acId;
            ActiveTab = EditorTab.Cards;
        }

        /// <summary>Whether a ModReferences family name (e.g. "Npcs", "CardPatches") has an editor we can jump to.</summary>
        public static bool CanInspectFamily(string family)
        {
            switch (family)
            {
                case "Cards": case "CardPatches":
                case "Npcs": case "NpcPatches":
                case "Events": case "EventPatches":
                case "Combats": case "CombatPatches":
                case "Loot": case "LootPatches":
                case "AuraCurses": case "AuraCursePatches":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Jump to the entity that owns a reference hit. Returns false if there is no editor for that family.</summary>
        public bool InspectFamily(string family, string ownerId)
        {
            switch (family)
            {
                case "Cards":
                case "CardPatches":
                    var proj = ModManagerPanel.ActiveProject;
                    UnknownMod.Definitions.CardDef def = null;
                    if (proj != null && !proj.Cards.TryGetValue(ownerId, out def)) proj.CardPatches.TryGetValue(ownerId, out def);
                    if (def != null && def.HasItemData) InspectItem(ownerId); else InspectCard(ownerId);
                    return true;
                case "Npcs": case "NpcPatches": InspectNpc(ownerId); return true;
                case "Events": case "EventPatches": InspectEvent(ownerId); return true;
                case "Combats": case "CombatPatches": InspectCombat(ownerId); return true;
                case "Loot": case "LootPatches": InspectLoot(ownerId); return true;
                case "AuraCurses": case "AuraCursePatches": InspectAuraCurse(ownerId); return true;
                default: return false;
            }
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

            RenameFlush.Tick();
            if (!_tabRestored)
            {
                _tabRestored = true;
                ActiveTab = (EditorTab)Mathf.Clamp(EditorLayout.LastTab, 0, TabNames.Length - 1);
            }
            if ((int)ActiveTab != EditorLayout.LastTab) EditorLayout.RememberTab((int)ActiveTab);
            // Clicks on the open Layout menu must not reach the controls underneath it: skip them for those events.
            var pointer = Event.current.type;
            bool blockUnder = _layoutMenuOpen
                && (pointer == EventType.MouseDown || pointer == EventType.MouseUp || pointer == EventType.MouseDrag || pointer == EventType.ScrollWheel)
                && LayoutMenuRect(screenW).Contains(Event.current.mousePosition);
            if (!blockUnder) HandleSplitter(screenW, screenH);
            EditorActions.Current = null;

            // ── Compute UI rects for hit-testing ─────────────────────
            Rect headerRect = new Rect(0, 10, screenW, 35);
            Rect tabBarRect = new Rect(0, PanelTop, screenW, TabBarHeight);
            float panelY = PanelTop + TabBarHeight;
            EditorLayout.Compute(screenW, screenH, panelY, 20f, EntityPicker.IsOpen, out Rect vpRect, out Rect panelRect);
            if (!blockUnder) HandleFloatingPanel(ref panelRect, screenW, screenH);

            // ── Header bar (full width) ──────────────────────────────
            GUI.Box(headerRect, "", _boxStyle);
            string dirtyMark = ZoneEditingService.IsDirty ? " <color=yellow>●</color>" : "";
            string modLabel = Tabs.ModManagerPanel.ActiveProject != null
                ? $"  <color=#aaa>mod: {Tabs.ModManagerPanel.ActiveProject.ModId}</color>" : "";
            GUI.Label(new Rect(5, 14, screenW * 0.42f, 25),
                $"<b>MOD EDITOR</b>  |  `=Mods  1-8=tabs  |  F9=close{dirtyMark}{modLabel}",
                _headerStyle);
            DrawHeaderActions(screenW);

            // ── Tab bar (full width) ─────────────────────────────
            float tabW = screenW / TabNames.Length;

            for (int i = 0; i < TabNames.Length; i++)
            {
                var style = (EditorTab)i == ActiveTab ? _tabActiveStyle : _tabStyle;
                if (!blockUnder && GUI.Button(new Rect(i * tabW, PanelTop, tabW, TabBarHeight), TabNames[i], style))
                {
                    ActiveTab = (EditorTab)i;
                    EditorLayout.RememberTab(i);
                    PopupState.Close();
                }
            }

            Rect layoutMenuRect = _layoutMenuOpen ? LayoutMenuRect(screenW) : Rect.zero;

            // Set the flag: is the mouse inside any editor GUI rect?
            Vector2 mouse = Event.current.mousePosition;
            IsMouseOverUI = headerRect.Contains(mouse) ||
                            tabBarRect.Contains(mouse) ||
                            panelRect.Contains(mouse) ||
                            vpRect.Contains(mouse) ||
                            layoutMenuRect.Contains(mouse);

            // Draw viewport: EntityPicker overlay takes priority when open
            if (blockUnder)
            {
            }
            else if (EntityPicker.IsOpen)
            {
                EntityPicker.Draw(vpRect);
            }
            else
            {
                switch (ActiveTab)
                {
                    case EditorTab.ModManager:
                        ModManager?.DrawViewport(vpRect);
                        break;
                    case EditorTab.Cards:
                        CardsTab?.DrawViewport(vpRect);
                        break;
                    case EditorTab.Heroes:
                        HeroesTab?.DrawViewport(vpRect);
                        break;
                    case EditorTab.Enemies:
                        EnemiesTab?.DrawViewport(vpRect);
                        break;
                    case EditorTab.Player:
                        PlayerTab?.DrawViewport(vpRect);
                        break;
                    case EditorTab.World:
                        ZoneTab?.DrawViewport(vpRect);
                        break;
                    case EditorTab.GraftSprites:
                        SpriteEdit?.DrawViewport(vpRect);
                        break;
                    case EditorTab.Extras:
                        ExtrasTab?.DrawViewport(vpRect);
                        break;
                    case EditorTab.Assets:
                        AssetsTab?.DrawViewport(vpRect);
                        break;
                }
            }

            // ── Panel ────────────────────────────────────────────────
            if (!blockUnder && panelRect.width > 0f) DrawPanelContents(panelRect);
            if (!blockUnder) DrawLayoutChips(vpRect, panelRect, screenW);
            if (_layoutMenuOpen) DrawLayoutMenu(screenW);

            if (!blockUnder) DrawSplitter(screenW, screenH);
            DrawTooltipOverlay();

            // A command nobody claimed during this Layout pass (e.g. Ctrl+D on a tab without duplicate support) must not fire later.
            if (Event.current.type == EventType.Layout) { EditorActions.Pending = EditorCommand.None; EditorFields.EndSectionPass(); }
            if (Event.current.type == EventType.Repaint) EditorActions.Previous = EditorActions.Current;

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

        /// <summary>The active tab's panel, inside the panel rectangle (a floating panel has a title strip above it).</summary>
        private void DrawPanelContents(Rect panelRect)
        {
            GUI.Box(panelRect, "", _boxStyle);
            bool floating = EditorLayout.State.Dock == DockMode.Float;
            float titleH = floating ? EditorLayout.FloatTitleH : 0f;
            if (floating) DrawFloatTitle(panelRect);
            Rect innerRect = new Rect(panelRect.x + 5, panelRect.y + 5 + titleH, panelRect.width - 10, panelRect.height - 10 - titleH);

            GUILayout.BeginArea(innerRect);
            EditorFields.BeginSectionPass();
            _tabScroll.TryGetValue(ActiveTab, out _panelScroll);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);

            // Track GUI changes for hot-reload
            GUI.changed = false;

            switch (ActiveTab)
            {
                case EditorTab.ModManager:
                    ModManager?.DrawPanel();
                    break;
                case EditorTab.Cards:
                    CardsTab?.DrawPanel();
                    CardsTab?.HandleChanges();
                    break;
                case EditorTab.Heroes:
                    HeroesTab?.DrawPanel();
                    HeroesTab?.HandleChanges();
                    break;
                case EditorTab.Enemies:
                    EnemiesTab?.DrawPanel();
                    EnemiesTab?.HandleChanges();
                    break;
                case EditorTab.Player:
                    PlayerTab?.DrawPanel();
                    PlayerTab?.HandleChanges();
                    break;
                case EditorTab.World:
                    ZoneTab?.DrawPanel();
                    ZoneTab?.HandleChanges();
                    break;
                case EditorTab.GraftSprites:
                    SpriteEdit?.DrawPanel();
                    if (GUI.changed) SpriteEdit?.ApplyHotReload();
                    break;
                case EditorTab.Extras:
                    ExtrasTab?.DrawPanel();
                    ExtrasTab?.HandleChanges();
                    break;
                case EditorTab.Assets:
                    AssetsTab?.DrawPanel();
                    break;
            }

            GUILayout.EndScrollView();
            _tabScroll[ActiveTab] = _panelScroll;
            GUILayout.EndArea();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYOUT MENU AND FLOATING PANEL
        // ═══════════════════════════════════════════════════════════════

        private bool _layoutMenuOpen;
        private int _floatDragId, _floatResizeId;
        private Vector2 _floatGrab;

        private Rect LayoutMenuRect(float screenW)
        {
            int userCount = 0;
            foreach (var _ in EditorLayout.UserPresets) userCount++;
            float h = 8f + 22f + 24f + 28f + 18f + (EditorLayout.BuiltInOrder.Length + userCount) * 22f + 34f;
            const float w = 240f;
            return new Rect(screenW - w - 8f, 46f, w, h);
        }

        /// <summary>The Layout menu: dock side, hidden panes and saved layouts. Drawn last so it sits over everything.</summary>
        private void DrawLayoutMenu(float screenW)
        {
            var rect = LayoutMenuRect(screenW);
            var e = Event.current;
            GUI.Box(rect, "", _boxStyle);
            var s = EditorLayout.State;
            float x = rect.x + 8f, w = rect.width - 16f, y = rect.y + 8f;

            GUI.Label(new Rect(x, y, w, 20f), "<b>Editor panel</b>", _headerStyle);
            y += 22f;
            float third = (w - 4f) / 3f;
            if (GUI.Toggle(new Rect(x, y, third, 22f), s.Dock == DockMode.Right && !s.PanelHidden, "Right", _tabStyle) && s.Dock != DockMode.Right) EditorLayout.SetDock(DockMode.Right);
            if (GUI.Toggle(new Rect(x + third + 2f, y, third, 22f), s.Dock == DockMode.Left && !s.PanelHidden, "Left", _tabStyle) && s.Dock != DockMode.Left) EditorLayout.SetDock(DockMode.Left);
            if (GUI.Toggle(new Rect(x + 2f * (third + 2f), y, third, 22f), s.Dock == DockMode.Float && !s.PanelHidden, "Float", _tabStyle) && s.Dock != DockMode.Float) EditorLayout.SetDock(DockMode.Float);
            y += 24f;

            float half = (w - 4f) / 2f;
            if (GUI.Button(new Rect(x, y, half, 22f), new GUIContent(s.PanelHidden ? "Show panel" : "Hide panel", "Give the viewport the whole screen"), _tabStyle)) EditorLayout.TogglePanel();
            if (GUI.Button(new Rect(x + half + 4f, y, half, 22f), new GUIContent(s.ViewportHidden ? "Show viewport" : "Hide viewport", "Give the panel the whole screen"), _tabStyle)) EditorLayout.ToggleViewport();
            y += 28f;

            GUI.Label(new Rect(x, y, w, 18f), "<b>Layouts</b>", _headerStyle);
            y += 18f;
            foreach (var name in EditorLayout.BuiltInOrder)
            {
                if (GUI.Button(new Rect(x, y, w, 20f), name, _tabStyle)) { EditorLayout.Apply(name); _layoutMenuOpen = false; return; }
                y += 22f;
            }
            string delete = null;
            foreach (var name in EditorLayout.UserPresets)
            {
                if (GUI.Button(new Rect(x, y, w - 24f, 20f), name, _tabStyle)) { EditorLayout.Apply(name); _layoutMenuOpen = false; return; }
                if (GUI.Button(new Rect(x + w - 22f, y, 22f, 20f), new GUIContent("X", "Delete this layout"), _tabStyle)) delete = name;
                y += 22f;
            }
            if (delete != null) EditorLayout.DeletePreset(delete);
            if (GUI.Button(new Rect(x, y + 4f, w, 22f), new GUIContent("Save current layout", "Remember this arrangement under a new name"), _tabStyle))
                EditorStatus.Toast("Saved layout '" + EditorLayout.SaveAsPreset() + "'");

            if (e.type == EventType.MouseDown && !rect.Contains(e.mousePosition)) _layoutMenuOpen = false;
        }

        /// <summary>Small buttons to bring back a hidden pane.</summary>
        private void DrawLayoutChips(Rect vpRect, Rect panelRect, float screenW)
        {
            var s = EditorLayout.State;
            if (s.PanelHidden && GUI.Button(new Rect(screenW - 82f, PanelTop + TabBarHeight + 6f, 76f, 22f), new GUIContent("\u25C0 Panel", "Show the editor panel again"), _tabStyle))
                EditorLayout.TogglePanel();
            if (s.ViewportHidden && vpRect.width <= 0f && GUI.Button(new Rect(screenW - 112f, Screen.height - 28f, 106f, 22f), new GUIContent("Show viewport", "Show the viewport again"), _tabStyle))
                EditorLayout.ToggleViewport();
        }

        private void DrawFloatTitle(Rect panelRect)
        {
            var title = new Rect(panelRect.x, panelRect.y, panelRect.width, EditorLayout.FloatTitleH);
            GUI.Box(title, "", _boxStyle);
            GUI.Label(new Rect(title.x + 6f, title.y + 1f, title.width - 80f, title.height), "<color=#aaa>Editor panel  (drag to move)</color>", _headerStyle);
            if (GUI.Button(new Rect(title.xMax - 74f, title.y + 1f, 36f, title.height - 2f), new GUIContent("Dock", "Dock the panel back on the right"), _tabStyle)) EditorLayout.SetDock(DockMode.Right);
            if (GUI.Button(new Rect(title.xMax - 36f, title.y + 1f, 34f, title.height - 2f), new GUIContent("Hide", "Hide the panel"), _tabStyle)) EditorLayout.TogglePanel();
            GUI.Label(new Rect(panelRect.xMax - 16f, panelRect.yMax - 18f, 16f, 16f), "\u25E2", _headerStyle);
        }

        /// <summary>Drag the title strip to move the floating panel; drag its corner to resize it.</summary>
        private void HandleFloatingPanel(ref Rect panelRect, float screenW, float screenH)
        {
            var s = EditorLayout.State;
            if (s.Dock != DockMode.Float || panelRect.width <= 0f) return;
            _floatDragId = GUIUtility.GetControlID(FocusType.Passive);
            _floatResizeId = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            var title = new Rect(panelRect.x, panelRect.y, panelRect.width - 78f, EditorLayout.FloatTitleH);
            var grip = new Rect(panelRect.xMax - 18f, panelRect.yMax - 18f, 18f, 18f);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0) break;
                    if (grip.Contains(e.mousePosition)) { GUIUtility.hotControl = _floatResizeId; _floatGrab = e.mousePosition - new Vector2(s.FloatW, s.FloatH); e.Use(); }
                    else if (title.Contains(e.mousePosition)) { GUIUtility.hotControl = _floatDragId; _floatGrab = e.mousePosition - new Vector2(s.FloatX, s.FloatY); e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == _floatDragId)
                    {
                        s.FloatX = e.mousePosition.x - _floatGrab.x;
                        s.FloatY = e.mousePosition.y - _floatGrab.y;
                        e.Use();
                    }
                    else if (GUIUtility.hotControl == _floatResizeId)
                    {
                        s.FloatW = e.mousePosition.x - _floatGrab.x;
                        s.FloatH = e.mousePosition.y - _floatGrab.y;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == _floatDragId || GUIUtility.hotControl == _floatResizeId)
                    {
                        GUIUtility.hotControl = 0;
                        EditorLayout.Save();
                        e.Use();
                    }
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HEADER ACTIONS, SPLITTER, TOOLTIP
        // ═══════════════════════════════════════════════════════════════

        private GUIStyle _statusStyle;
        private GUIStyle _tipStyle;
        private Texture2D _gripTex;
        private Texture2D _gripHotTex;

        /// <summary>Right side of the header: save status text and the Back / Undo / Redo / Duplicate / Save buttons.</summary>
        private void DrawHeaderActions(float screenW)
        {
            if (_statusStyle == null)
                _statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };

            const float bw = 44f, bh = 24f, y = 15f;
            float x = screenW - 8f;
            var actions = EditorActions.Previous;
            bool prevEnabled = GUI.enabled;

            x -= 58f;
            if (GUI.Button(new Rect(x, y, 58f, bh), new GUIContent("Save", "Save everything now (Ctrl+S). Entities already autosave as you edit."), _tabStyle)) SaveAll();
            x -= bw + 3f;
            GUI.enabled = prevEnabled && actions != null && actions.CanDuplicate;
            if (GUI.Button(new Rect(x, y, bw, bh), new GUIContent("Dup", "Duplicate the selected entry (Ctrl+D)"), _tabStyle)) EditorActions.Pending = EditorCommand.Duplicate;
            x -= bw + 3f;
            GUI.enabled = prevEnabled && actions != null && actions.CanRedo;
            if (GUI.Button(new Rect(x, y, bw, bh), new GUIContent("Redo", "Redo (Ctrl+Y)"), _tabStyle)) EditorActions.Pending = EditorCommand.Redo;
            x -= bw + 3f;
            GUI.enabled = prevEnabled && actions != null && actions.CanUndo;
            if (GUI.Button(new Rect(x, y, bw, bh), new GUIContent("Undo", "Undo the last edit to this entry (Ctrl+Z)"), _tabStyle)) EditorActions.Pending = EditorCommand.Undo;
            x -= bw + 3f;
            GUI.enabled = prevEnabled && CanGoBack;
            if (GUI.Button(new Rect(x, y, bw, bh), new GUIContent("Back", "Back to where you were before the last jump (Alt+Left)"), _tabStyle)) GoBack();
            GUI.enabled = prevEnabled;
            x -= 66f + 3f;
            if (GUI.Button(new Rect(x, y, 66f, bh), new GUIContent("Layout", "Dock, float or hide the panels, and save layouts"), _tabStyle)) _layoutMenuOpen = !_layoutMenuOpen;

            float left = screenW * 0.42f;
            GUI.Label(new Rect(left, 14, x - left - 6f, 25), EditorStatus.HeaderText(), _statusStyle);
        }

        private void HandleSplitter(float screenW, float screenH)
        {
            _splitterId = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            float top = PanelTop + TabBarHeight;
            float splitX = EditorLayout.SplitterX(screenW);
            if (splitX < 0f) return;
            var grip = new Rect(splitX - 4f, top, 8f, screenH - top);
            switch (e.GetTypeForControl(_splitterId))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && grip.Contains(e.mousePosition)) { GUIUtility.hotControl = _splitterId; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == _splitterId)
                    {
                        EditorLayout.DragSplitter(e.mousePosition.x, screenW);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == _splitterId)
                    {
                        GUIUtility.hotControl = 0;
                        EditorLayout.Save();
                        e.Use();
                    }
                    break;
            }
        }

        private void DrawSplitter(float screenW, float screenH)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_gripTex == null) _gripTex = MakeTex(2, 2, new Color(0.45f, 0.45f, 0.5f, 0.6f));
            if (_gripHotTex == null) _gripHotTex = MakeTex(2, 2, new Color(0.4f, 0.8f, 1f, 0.95f));
            float top = PanelTop + TabBarHeight;
            float x = EditorLayout.SplitterX(screenW);
            if (x < 0f) return;
            bool hot = GUIUtility.hotControl == _splitterId || new Rect(x - 4f, top, 8f, screenH - top).Contains(Event.current.mousePosition);
            GUI.DrawTexture(new Rect(x - 1f, top, 3f, screenH - top), hot ? _gripHotTex : _gripTex);
            if (hot)
            {
                // Three dots so the handle is discoverable
                float cy = top + (screenH - top) * 0.5f;
                for (int i = -1; i <= 1; i++) GUI.DrawTexture(new Rect(x - 2f, cy + i * 8f - 1.5f, 5f, 3f), _gripHotTex);
            }
        }

        /// <summary>Draws the wrapped tooltip for whichever control (field label, button) the mouse is resting on.</summary>
        private void DrawTooltipOverlay()
        {
            if (Event.current.type != EventType.Repaint) return;
            string tip = GUI.tooltip;
            if (string.IsNullOrEmpty(tip)) return;
            if (_tipStyle == null)
                _tipStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(8, 8, 5, 5),
                    normal = { textColor = new Color(0.95f, 0.93f, 0.85f), background = EditorFields.TooltipBackground },
                };
            const float w = 280f;
            float h = _tipStyle.CalcHeight(new GUIContent(tip), w);
            Vector2 m = Event.current.mousePosition;
            float x = Mathf.Min(m.x + 14f, Screen.width - w - 4f);
            float y = m.y + 22f;
            if (y + h > Screen.height - 4f) y = m.y - h - 8f;
            GUI.Box(new Rect(Mathf.Max(2f, x), Mathf.Max(2f, y), w, h), tip, _tipStyle);
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
            };
            _tabActiveStyle.normal.textColor = Color.cyan;

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
