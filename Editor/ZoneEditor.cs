using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Top-level MonoBehaviour that manages the zone editing system.
    /// Coordinates between sub-editors (Map, Node, Event, Encounter, NPC, Sprite, Card, Item, Loot).
    ///
    /// Hot-reload: Each sub-editor modifies the DTO directly. After drawing,
    /// if GUI.changed is set, the corresponding Rebuild* method is called to
    /// update the live ScriptableObjects in Globals. Auto-save to JSON happens
    /// ~2s after the last change.
    ///
    /// Controls:
    ///   F9          Toggle editor on/off
    ///   F8          Force save zone to JSON
    ///   Tab 1-9     Switch between editor panels
    /// </summary>
    public class ZoneEditor : MonoBehaviour
    {
        // ── Static state ─────────────────────────────────────────────
        public static bool IsEditing { get; private set; }
        public static ZoneEditor Instance { get; private set; }

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
        private const float PanelWidth = 440f;
        private const float PanelTop = 50f;
        private const float TabBarHeight = 30f;

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

            // Auto-save tick
            ZoneLoader.TickAutoSave();

            // F8 = Force Save
            if (Input.GetKeyDown(KeyCode.F8))
            {
                SaveZone();
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
            Plugin.Log.LogInfo($"[ZoneEditor] Edit mode: {(IsEditing ? "ON" : "OFF")}");

            // Forward to MapEditor (it manages road CPs and visual state)
            if (MapEdit != null)
                MapEdit.SetEditMode(IsEditing);
        }

        // ═══════════════════════════════════════════════════════════════
        //  SAVE
        // ═══════════════════════════════════════════════════════════════

        private void SaveZone()
        {
            if (ZoneLoader.CurrentZone == null)
            {
                Plugin.Log.LogWarning("[ZoneEditor] No zone loaded to save.");
                return;
            }

            // Sync map positions/roads back to DTO before saving
            MapEdit?.SyncToDto(ZoneLoader.CurrentZone);

            ZoneLoader.SaveCurrentZone();
            Plugin.Log.LogInfo("[ZoneEditor] Zone saved successfully.");
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

            // ── Compute UI rects for hit-testing ─────────────────────
            Rect headerRect = new Rect(10, 10, screenW - 20, 35);
            float tabX = screenW - PanelWidth - 10;
            Rect tabBarRect = new Rect(tabX, PanelTop, PanelWidth, TabBarHeight);
            float panelY = PanelTop + TabBarHeight;
            Rect panelRect = new Rect(tabX, panelY, PanelWidth, panelH - TabBarHeight);

            // ── Header bar ───────────────────────────────────────────
            GUI.Box(headerRect, "", _boxStyle);
            string dirtyMark = ZoneLoader.IsDirty ? " <color=yellow>●</color>" : "";
            string modLabel = Tabs.ModManagerPanel.ActiveProject != null
                ? $"  <color=#aaa>mod: {Tabs.ModManagerPanel.ActiveProject.ModId}</color>" : "";
            GUI.Label(new Rect(15, 14, 600, 25),
                $"<b>MOD EDITOR</b>  |  `=Mods  1-5=tabs  |  F8=save  |  F9=close{dirtyMark}{modLabel}",
                _headerStyle);

            // ── Tab bar ──────────────────────────────────────────────
            var tabs = new[] { "Mods", "Combat", "Chars", "World", "Zones", "Sprites" };
            float tabW = PanelWidth / tabs.Length;

            for (int i = 0; i < tabs.Length; i++)
            {
                var style = (EditorTab)i == ActiveTab ? _tabActiveStyle : _tabStyle;
                if (GUI.Button(new Rect(tabX + i * tabW, PanelTop, tabW, TabBarHeight), tabs[i], style))
                {
                    ActiveTab = (EditorTab)i;
                    PopupState.Close();
                }
            }

            // ── Viewports (left side of screen) ─────────────────────
            bool hasViewport = (ActiveTab == EditorTab.Sprites) ||
                               (ActiveTab == EditorTab.Zones && ZoneTab.HasViewport);

            Rect vpRect = default;
            if (hasViewport)
            {
                float vpY = PanelTop + TabBarHeight + 2;
                vpRect = new Rect(10, vpY, tabX - 25, screenH - vpY - 15);
            }

            // Set the flag: is the mouse inside any editor GUI rect?
            Vector2 mouse = Event.current.mousePosition;
            IsMouseOverUI = headerRect.Contains(mouse) ||
                            tabBarRect.Contains(mouse) ||
                            panelRect.Contains(mouse) ||
                            (hasViewport && vpRect.Contains(mouse));

            if (hasViewport)
            {
                if (ActiveTab == EditorTab.Sprites)
                    SpriteEdit?.DrawViewport(vpRect);
                else if (ActiveTab == EditorTab.Zones)
                    ZoneTab.DrawViewport(vpRect);
            }

            // ── Panel ────────────────────────────────────────────────
            GUI.Box(panelRect, "", _boxStyle);

            Rect innerRect = new Rect(tabX + 5, panelY + 5, PanelWidth - 10, panelH - TabBarHeight - 10);

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
                    if (CombatTab.HandleChanges()) OnCombatTabChanged();
                    break;
                case EditorTab.Characters:
                    CharacterTab?.DrawPanel();
                    if (CharacterTab != null && CharacterTab.HandleChanges()) OnCharacterTabChanged();
                    break;
                case EditorTab.World:
                    WorldTab?.DrawPanel();
                    if (WorldTab != null && WorldTab.HandleChanges()) OnWorldTabChanged();
                    break;
                case EditorTab.Zones:
                    ZoneTab?.DrawPanel();
                    if (ZoneTab.HandleChanges()) OnZoneTabChanged();
                    break;
                case EditorTab.Sprites:
                    SpriteEdit?.DrawPanel();
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ═══════════════════════════════════════════════════════════════
        //  HOT-RELOAD TRIGGERS
        // ═══════════════════════════════════════════════════════════════

        private void OnZoneTabChanged()
        {
            switch (ZoneTab.ActiveSubTab)
            {
                case ZoneTabEditor.SubTab.Node:
                    if (SelectedNodeId != null) ZoneLoader.RebuildNode(SelectedNodeId);
                    break;
                case ZoneTabEditor.SubTab.Event:
                    if (SelectedEventId != null) ZoneLoader.RebuildEvent(SelectedEventId);
                    break;
                case ZoneTabEditor.SubTab.Encounter:
                    if (SelectedCombatId != null) ZoneLoader.RebuildCombat(SelectedCombatId);
                    break;
            }
        }

        private void OnCharacterTabChanged()
        {
            switch (CharacterTab.ActiveSubTab)
            {
                case CharacterTabEditor.SubTab.Heroes:
                    if (HeroEdit?.SelectedHeroId != null)
                    {
                        var proj = Tabs.ModManagerPanel.ActiveProject;
                        HeroDef heroDef = null;
                        if (proj != null)
                        {
                            if (!proj.Heroes.TryGetValue(HeroEdit.SelectedHeroId, out heroDef))
                                proj.HeroPatches.TryGetValue(HeroEdit.SelectedHeroId, out heroDef);
                        }
                        if (heroDef != null)
                        {
                            try
                            {
                                var sc = DataHelper.MakeFullHero(heroDef);
                                DataHelper.RegisterHero(sc);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Hero hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case CharacterTabEditor.SubTab.Traits:
                    if (TraitEdit?.SelectedTraitId != null)
                    {
                        var tProj = Tabs.ModManagerPanel.ActiveProject;
                        TraitDef traitDef = null;
                        if (tProj != null)
                        {
                            if (!tProj.Traits.TryGetValue(TraitEdit.SelectedTraitId, out traitDef))
                                tProj.TraitPatches.TryGetValue(TraitEdit.SelectedTraitId, out traitDef);
                        }
                        if (traitDef != null)
                        {
                            try
                            {
                                var trait = DataHelper.MakeTrait(traitDef);
                                DataHelper.RegisterTrait(trait);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Trait hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case CharacterTabEditor.SubTab.Skins:
                    if (SkinEdit?.SelectedSkinId != null)
                    {
                        var sProj = Tabs.ModManagerPanel.ActiveProject;
                        SkinDef skinDef = null;
                        if (sProj != null)
                        {
                            if (!sProj.Skins.TryGetValue(SkinEdit.SelectedSkinId, out skinDef))
                                sProj.SkinPatches.TryGetValue(SkinEdit.SelectedSkinId, out skinDef);
                        }
                        if (skinDef != null)
                        {
                            try
                            {
                                var skin = DataHelper.MakeSkin(skinDef);
                                DataHelper.RegisterSkin(skin);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Skin hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
            }
        }

        private void OnWorldTabChanged()
        {
            switch (WorldTab.ActiveSubTab)
            {
                case WorldTabEditor.SubTab.Perks:
                    if (PerkEdit?.SelectedPerkId != null)
                    {
                        var proj = Tabs.ModManagerPanel.ActiveProject;
                        PerkDef perkDef = null;
                        if (proj != null)
                        {
                            if (!proj.Perks.TryGetValue(PerkEdit.SelectedPerkId, out perkDef))
                                proj.PerkPatches.TryGetValue(PerkEdit.SelectedPerkId, out perkDef);
                        }
                        if (perkDef != null)
                        {
                            try
                            {
                                var perk = DataHelper.MakePerk(perkDef);
                                DataHelper.RegisterPerk(perk);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Perk hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case WorldTabEditor.SubTab.PerkNodes:
                    if (PerkNodeEdit?.SelectedPerkNodeId != null)
                    {
                        var pnProj = Tabs.ModManagerPanel.ActiveProject;
                        PerkNodeDef pnDef = null;
                        if (pnProj != null)
                        {
                            if (!pnProj.PerkNodes.TryGetValue(PerkNodeEdit.SelectedPerkNodeId, out pnDef))
                                pnProj.PerkNodePatches.TryGetValue(PerkNodeEdit.SelectedPerkNodeId, out pnDef);
                        }
                        if (pnDef != null)
                        {
                            try
                            {
                                var pn = DataHelper.MakePerkNode(pnDef);
                                DataHelper.RegisterPerkNode(pn);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] PerkNode hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case WorldTabEditor.SubTab.Requirements:
                    if (RequirementEdit?.SelectedRequirementId != null)
                    {
                        var rProj = Tabs.ModManagerPanel.ActiveProject;
                        RequirementDef rDef = null;
                        if (rProj != null)
                        {
                            if (!rProj.Requirements.TryGetValue(RequirementEdit.SelectedRequirementId, out rDef))
                                rProj.RequirementPatches.TryGetValue(RequirementEdit.SelectedRequirementId, out rDef);
                        }
                        if (rDef != null)
                        {
                            try
                            {
                                var req = DataHelper.MakeRequirement(rDef);
                                DataHelper.RegisterRequirement(req);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Requirement hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case WorldTabEditor.SubTab.Cardbacks:
                    if (CardbackEdit?.SelectedCardbackId != null)
                    {
                        var cbProj = Tabs.ModManagerPanel.ActiveProject;
                        CardbackDef cbDef = null;
                        if (cbProj != null)
                        {
                            if (!cbProj.Cardbacks.TryGetValue(CardbackEdit.SelectedCardbackId, out cbDef))
                                cbProj.CardbackPatches.TryGetValue(CardbackEdit.SelectedCardbackId, out cbDef);
                        }
                        if (cbDef != null)
                        {
                            try
                            {
                                var cb = DataHelper.MakeCardback(cbDef);
                                DataHelper.RegisterCardback(cb);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Cardback hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case WorldTabEditor.SubTab.TierRewards:
                    if (TierRewardEdit?.SelectedTierRewardId != null)
                    {
                        var trProj = Tabs.ModManagerPanel.ActiveProject;
                        TierRewardDef trDef = null;
                        if (trProj != null)
                        {
                            if (!trProj.TierRewards.TryGetValue(TierRewardEdit.SelectedTierRewardId, out trDef))
                                trProj.TierRewardPatches.TryGetValue(TierRewardEdit.SelectedTierRewardId, out trDef);
                        }
                        if (trDef != null)
                        {
                            try
                            {
                                var tr = DataHelper.MakeTierReward(trDef);
                                DataHelper.RegisterTierReward(tr);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] TierReward hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
            }
        }

        private void OnCombatTabChanged()
        {
            switch (CombatTab.ActiveSubTab)
            {
                case CombatTabEditor.SubTab.Cards:
                    if (SelectedCardId != null)
                    {
                        var proj = Tabs.ModManagerPanel.ActiveProject;
                        CardDef cardDef = null;
                        if (proj != null)
                        {
                            if (!proj.Cards.TryGetValue(SelectedCardId, out cardDef))
                                proj.CardPatches.TryGetValue(SelectedCardId, out cardDef);
                        }
                        if (cardDef != null)
                        {
                            try
                            {
                                var so = Core.ModProjectBuilder.MakeFullCard(cardDef);
                                DataHelper.RegisterCard(so);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Card hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case CombatTabEditor.SubTab.Items:
                    if (SelectedItemId != null)
                    {
                        var proj2 = Tabs.ModManagerPanel.ActiveProject;
                        ItemDef itemDef = null;
                        if (proj2 != null)
                        {
                            if (!proj2.Items.TryGetValue(SelectedItemId, out itemDef))
                                proj2.ItemPatches.TryGetValue(SelectedItemId, out itemDef);
                        }
                        if (itemDef != null)
                        {
                            try
                            {
                                var so = DataHelper.MakeFullItem(itemDef);
                                DataHelper.RegisterItem(so);
                                var card = DataHelper.MakeItemCard(itemDef, so);
                                DataHelper.RegisterCard(card);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Item hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case CombatTabEditor.SubTab.Loot:
                    if (SelectedLootId != null)
                    {
                        var proj4 = Tabs.ModManagerPanel.ActiveProject;
                        LootDef lootDef = null;
                        if (proj4 != null)
                        {
                            if (!proj4.Loot.TryGetValue(SelectedLootId, out lootDef))
                                proj4.LootPatches.TryGetValue(SelectedLootId, out lootDef);
                        }
                        if (lootDef != null)
                        {
                            try
                            {
                                var loot = DataHelper.MakeLoot(lootDef);
                                DataHelper.RegisterLoot(loot);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] Loot hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case CombatTabEditor.SubTab.NPCs:
                    if (SelectedNpcId != null)
                    {
                        var proj3 = Tabs.ModManagerPanel.ActiveProject;
                        NpcDef npcDef = null;
                        if (proj3 != null)
                        {
                            if (!proj3.Npcs.TryGetValue(SelectedNpcId, out npcDef))
                                proj3.NpcPatches.TryGetValue(SelectedNpcId, out npcDef);
                        }
                        if (npcDef != null)
                        {
                            try
                            {
                                var npc = DataHelper.MakeFullNpc(npcDef);
                                DataHelper.RegisterNPC(npc);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] NPC hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
                case CombatTabEditor.SubTab.AuraCurse:
                    // AuraCurse hot-reload: rebuild the SO in Globals
                    if (SelectedAuraCurseId != null)
                    {
                        var proj = Tabs.ModManagerPanel.ActiveProject;
                        AuraCurseDef acDef = null;
                        if (proj != null)
                        {
                            if (!proj.AuraCurses.TryGetValue(SelectedAuraCurseId, out acDef))
                                proj.AuraCursePatches.TryGetValue(SelectedAuraCurseId, out acDef);
                        }
                        if (acDef != null)
                        {
                            try
                            {
                                var so = Core.ModProjectBuilder.MakeAuraCurse(acDef);
                                var dict = HarmonyLib.Traverse.Create(Globals.Instance)
                                    .Field<System.Collections.Generic.Dictionary<string, AuraCurseData>>("_AurasCursesSource").Value;
                                if (dict != null) dict[acDef.Id.ToLower()] = so;
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogWarning($"[ZoneEditor] AC hot-reload failed: {ex.Message}");
                            }
                        }
                    }
                    break;
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
