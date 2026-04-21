using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the Cards category tab with a two-level subtab hierarchy:
    ///   Hero (Warrior|Mage|Healer|Scout)
    ///   Equipment (Items|Enchants|Pets)
    ///   Monster (NPC Cards|Corruptions)
    ///   Special (Boons|Injuries|Food|Special)
    /// The CardEditor handles all subtabs — filtering by CardClass/CardType.
    /// </summary>
    public class CardsTabEditor
    {
        private readonly ModEditor _editor;

        // ── Level 1 subtabs ──────────────────────────────────────
        public enum SubTab { Hero, Equipment, Monster, Special }
        public SubTab ActiveSubTab = SubTab.Hero;

        // ── Level 2 subtabs per category ─────────────────────────
        public enum HeroSub { Warrior, Mage, Healer, Scout }
        public HeroSub ActiveHeroSub = HeroSub.Warrior;

        public enum EquipmentSub { Items, Enchantments, Pets }
        public EquipmentSub ActiveEquipmentSub = EquipmentSub.Items;

        public enum MonsterSub { NpcCards, Corruptions }
        public MonsterSub ActiveMonsterSub = MonsterSub.NpcCards;

        public enum SpecialSub { Boons, Injuries, Food, Special }
        public SpecialSub ActiveSpecialSub = SpecialSub.Boons;

        public CardsTabEditor(ModEditor editor) => _editor = editor;

        public void Tick() { }

        // ═════════════════════════════════════════════════════════
        //  VIEWPORT
        // ═════════════════════════════════════════════════════════

        public void DrawViewport(Rect rect)
        {
            // All card subtabs use the same card preview
            string id = _editor.SelectedCardId;
            CardDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Cards.TryGetValue(id, out def))
                    proj.CardPatches.TryGetValue(id, out def);
            }

            // Equipment cards get item preview, others get card preview
            bool isItem = def != null ? def.HasItemData
                : ActiveSubTab == SubTab.Equipment || DataHelper.GetItem(id) != null;
            if (isItem)
                ViewportPreview.DrawItemCard(rect, id, def);
            else
                ViewportPreview.DrawCard(rect, id, def);
        }

        // ═════════════════════════════════════════════════════════
        //  PANEL
        // ═════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            DrawLevel1TabBar();
            DrawLevel2TabBar();
            GUILayout.Space(4);

            // All subtabs delegate to the unified CardEditor
            _editor.CardEdit?.DrawPanel();
        }

        // ═════════════════════════════════════════════════════════
        //  CHANGES & HOT-RELOAD
        // ═════════════════════════════════════════════════════════

        public void HandleChanges()
        {
            if (!GUI.changed) return;

            bool changed = _editor.CardEdit != null && _editor.CardEdit.HandleChanges();

            if (changed) HotReload();
        }

        private void HotReload()
        {
            ModEditor.EntityPreview?.Invalidate();
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null || string.IsNullOrEmpty(_editor.SelectedCardId)) return;

            CardDef cardDef = null;
            if (!proj.Cards.TryGetValue(_editor.SelectedCardId, out cardDef))
                proj.CardPatches.TryGetValue(_editor.SelectedCardId, out cardDef);
            if (cardDef == null) return;

            try
            {
                if (cardDef.HasItemData)
                {
                    // Equipment/enchantment/pet — build ItemData + CardData
                    var itemDef = cardDef.ToItemDef();
                    var so = DataHelper.MakeFullItem(itemDef);
                    DataHelper.RegisterItem(so);
                    var card = DataHelper.MakeItemCard(itemDef, so);
                    DataHelper.RegisterCard(card);
                }
                else
                {
                    // Regular card — build CardData directly
                    var so = ModProjectBuilder.MakeFullCard(cardDef);
                    DataHelper.RegisterCard(so);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[CardsTab] Hot-reload failed: {ex.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════
        //  TAB BAR DRAWING
        // ═════════════════════════════════════════════════════════

        private void DrawLevel1TabBar()
        {
            GUILayout.BeginHorizontal();
            L1Button("Hero", SubTab.Hero);
            L1Button("Equipment", SubTab.Equipment);
            L1Button("Monster", SubTab.Monster);
            L1Button("Special", SubTab.Special);
            GUILayout.EndHorizontal();
        }

        private void DrawLevel2TabBar()
        {
            GUILayout.BeginHorizontal();
            switch (ActiveSubTab)
            {
                case SubTab.Hero:
                    L2Button("Warrior", HeroSub.Warrior, ref ActiveHeroSub);
                    L2Button("Mage", HeroSub.Mage, ref ActiveHeroSub);
                    L2Button("Healer", HeroSub.Healer, ref ActiveHeroSub);
                    L2Button("Scout", HeroSub.Scout, ref ActiveHeroSub);
                    break;
                case SubTab.Equipment:
                    L2Button("Items", EquipmentSub.Items, ref ActiveEquipmentSub);
                    L2Button("Enchantments", EquipmentSub.Enchantments, ref ActiveEquipmentSub);
                    L2Button("Pets", EquipmentSub.Pets, ref ActiveEquipmentSub);
                    break;
                case SubTab.Monster:
                    L2Button("NPC Cards", MonsterSub.NpcCards, ref ActiveMonsterSub);
                    L2Button("Corruptions", MonsterSub.Corruptions, ref ActiveMonsterSub);
                    break;
                case SubTab.Special:
                    L2Button("Boons", SpecialSub.Boons, ref ActiveSpecialSub);
                    L2Button("Injuries", SpecialSub.Injuries, ref ActiveSpecialSub);
                    L2Button("Food", SpecialSub.Food, ref ActiveSpecialSub);
                    L2Button("Special", SpecialSub.Special, ref ActiveSpecialSub);
                    break;
            }
            GUILayout.EndHorizontal();
        }

        // ═════════════════════════════════════════════════════════
        //  CARD FILTER (used by CardEditor to filter defs)
        // ═════════════════════════════════════════════════════════

        /// <summary>
        /// Returns true if this CardDef matches the currently active subtab filter.
        /// </summary>
        public bool MatchesActiveFilter(CardDef cd)
        {
            if (cd == null) return false;
            return MatchesActiveFilter(cd.CardClass, cd.CardType);
        }

        /// <summary>
        /// Returns true if this CardClass/CardType combination matches the active subtab.
        /// </summary>
        public bool MatchesActiveFilter(Enums.CardClass cls, Enums.CardType type)
        {
            switch (ActiveSubTab)
            {
                case SubTab.Hero:
                    return ActiveHeroSub switch
                    {
                        HeroSub.Warrior => cls == Enums.CardClass.Warrior,
                        HeroSub.Mage    => cls == Enums.CardClass.Mage,
                        HeroSub.Healer  => cls == Enums.CardClass.Healer,
                        HeroSub.Scout   => cls == Enums.CardClass.Scout,
                        _ => false
                    };
                case SubTab.Equipment:
                    return ActiveEquipmentSub switch
                    {
                        EquipmentSub.Items => type == Enums.CardType.Weapon || type == Enums.CardType.Armor ||
                                              type == Enums.CardType.Jewelry || type == Enums.CardType.Accesory,
                        EquipmentSub.Enchantments => type == Enums.CardType.Enchantment,
                        EquipmentSub.Pets => type == Enums.CardType.Pet || type == Enums.CardType.Petrare,
                        _ => false
                    };
                case SubTab.Monster:
                    return ActiveMonsterSub switch
                    {
                        MonsterSub.NpcCards => cls == Enums.CardClass.Monster &&
                                              type != Enums.CardType.Corruption &&
                                              type != Enums.CardType.Enchantment,
                        MonsterSub.Corruptions => type == Enums.CardType.Corruption,
                        _ => false
                    };
                case SubTab.Special:
                    return ActiveSpecialSub switch
                    {
                        SpecialSub.Boons    => cls == Enums.CardClass.Boon,
                        SpecialSub.Injuries => cls == Enums.CardClass.Injury,
                        SpecialSub.Food     => type == Enums.CardType.Food,
                        SpecialSub.Special  => cls == Enums.CardClass.Special && type != Enums.CardType.Enchantment && type != Enums.CardType.Food,
                        _ => false
                    };
                default:
                    return true;
            }
        }

        /// <summary>
        /// Auto-set CardClass and CardType on a newly created CardDef to match the active subtab.
        /// Also initializes ItemFields when creating equipment/enchantment/pet cards.
        /// </summary>
        public void ApplyDefaultsForActiveTab(CardDef cd)
        {
            switch (ActiveSubTab)
            {
                case SubTab.Hero:
                    cd.CardClass = ActiveHeroSub switch
                    {
                        HeroSub.Warrior => Enums.CardClass.Warrior,
                        HeroSub.Mage    => Enums.CardClass.Mage,
                        HeroSub.Healer  => Enums.CardClass.Healer,
                        HeroSub.Scout   => Enums.CardClass.Scout,
                        _ => Enums.CardClass.Warrior
                    };
                    break;
                case SubTab.Equipment:
                    cd.CardClass = Enums.CardClass.Item;
                    cd.Item ??= new ItemFields();
                    switch (ActiveEquipmentSub)
                    {
                        case EquipmentSub.Items:
                            cd.CardType = Enums.CardType.Weapon;
                            break;
                        case EquipmentSub.Enchantments:
                            cd.CardType = Enums.CardType.Enchantment;
                            cd.Item.IsEnchantment = true;
                            // Enchantments in the game use Monster or Special class
                            cd.CardClass = Enums.CardClass.Monster;
                            break;
                        case EquipmentSub.Pets:
                            cd.CardType = Enums.CardType.Pet;
                            break;
                    }
                    break;
                case SubTab.Monster:
                    cd.CardClass = Enums.CardClass.Monster;
                    if (ActiveMonsterSub == MonsterSub.Corruptions)
                        cd.CardType = Enums.CardType.Corruption;
                    break;
                case SubTab.Special:
                    switch (ActiveSpecialSub)
                    {
                        case SpecialSub.Boons:
                            cd.CardClass = Enums.CardClass.Boon;
                            break;
                        case SpecialSub.Injuries:
                            cd.CardClass = Enums.CardClass.Injury;
                            break;
                        case SpecialSub.Food:
                            cd.CardClass = Enums.CardClass.Special;
                            cd.CardType = Enums.CardType.Food;
                            break;
                        case SpecialSub.Special:
                            cd.CardClass = Enums.CardClass.Special;
                            break;
                    }
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════
        //  UI HELPERS
        // ═════════════════════════════════════════════════════════

        private void L1Button(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;
            if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                ActiveSubTab = tab;
        }

        private void L2Button<T>(string label, T tab, ref T current) where T : System.Enum
        {
            bool active = EqualityComparer<T>.Default.Equals(current, tab);
            var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
            string text = active ? $"<b><color=#8cf>{label}</color></b>" : label;
            if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                current = tab;
        }
    }
}
