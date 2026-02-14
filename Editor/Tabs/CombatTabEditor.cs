using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the Combat category tab with sub-tabs: Cards, Items, Loot, NPCs, AuraCurse.
    /// </summary>
    public class CombatTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { Cards, Items, Loot, NPCs, AuraCurse }
        public SubTab ActiveSubTab { get; set; } = SubTab.Cards;

        public CombatTabEditor(ModEditor editor) => _editor = editor;

        /// <summary>Per-frame tick (no-op for combat tab — saves immediately on change).</summary>
        public void Tick() { }

        /// <summary>Draw a preview viewport for the active combat sub-tab.</summary>
        public void DrawViewport(Rect rect)
        {
            switch (ActiveSubTab)
            {
                case SubTab.Cards:
                    DrawCardPreview(rect);
                    break;
                case SubTab.Items:
                    DrawItemPreview(rect);
                    break;
                case SubTab.Loot:
                    DrawLootPreview(rect);
                    break;
                case SubTab.NPCs:
                    DrawNpcPreview(rect);
                    break;
                case SubTab.AuraCurse:
                    DrawAuraCursePreview(rect);
                    break;
            }
        }

        // ── Viewport previews ────────────────────────────────────

        private void DrawCardPreview(Rect rect)
        {
            string id = _editor.SelectedCardId;
            CardDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Cards.TryGetValue(id, out def))
                    proj.CardPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawCard(rect, id, def);
        }

        private void DrawItemPreview(Rect rect)
        {
            string id = _editor.SelectedItemId;
            ItemDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Items.TryGetValue(id, out def))
                    proj.ItemPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawItem(rect, id, def);
        }

        private void DrawLootPreview(Rect rect)
        {
            string id = _editor.SelectedLootId;
            LootDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Loot.TryGetValue(id, out def))
                    proj.LootPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawLoot(rect, id, def);
        }

        private void DrawNpcPreview(Rect rect)
        {
            string id = _editor.SelectedNpcId;
            NpcDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Npcs.TryGetValue(id, out def))
                    proj.NpcPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawNpc(rect, id, def);
        }

        private void DrawAuraCursePreview(Rect rect)
        {
            string id = _editor.SelectedAuraCurseId;
            AuraCurseDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.AuraCurses.TryGetValue(id, out def))
                    proj.AuraCursePatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawAuraCurse(rect, id, def);
        }

        public void DrawPanel()
        {
            DrawSubTabBar();
            GUILayout.Space(4);

            switch (ActiveSubTab)
            {
                case SubTab.Cards:
                    _editor.CardEdit?.DrawPanel();
                    break;
                case SubTab.Items:
                    _editor.ItemEdit?.DrawPanel();
                    break;
                case SubTab.Loot:
                    _editor.LootEdit?.DrawPanel();
                    break;
                case SubTab.NPCs:
                    _editor.NpcEdit?.DrawPanel();
                    break;
                case SubTab.AuraCurse:
                    _editor.AuraCurseEdit?.DrawPanel();
                    break;
            }
        }

        /// <summary>Detect GUI.changed, save, and hot-reload affected SOs.</summary>
        public void HandleChanges()
        {
            if (!GUI.changed) return;

            bool changed = false;
            switch (ActiveSubTab)
            {
                case SubTab.Cards:
                    changed = _editor.CardEdit != null && _editor.CardEdit.HandleChanges();
                    break;
                case SubTab.Items:
                    changed = _editor.ItemEdit != null && _editor.ItemEdit.HandleChanges();
                    break;
                case SubTab.Loot:
                    changed = _editor.LootEdit != null && _editor.LootEdit.HandleChanges();
                    break;
                case SubTab.NPCs:
                    changed = _editor.NpcEdit != null && _editor.NpcEdit.HandleChanges();
                    break;
                case SubTab.AuraCurse:
                    changed = _editor.AuraCurseEdit != null && _editor.AuraCurseEdit.HandleChanges();
                    break;
            }

            if (changed) HotReload();
        }

        private void HotReload()
        {
            ModEditor.EntityPreview?.Invalidate();
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) return;

            switch (ActiveSubTab)
            {
                case SubTab.Cards:
                    if (_editor.SelectedCardId != null)
                    {
                        CardDef cardDef = null;
                        if (!proj.Cards.TryGetValue(_editor.SelectedCardId, out cardDef))
                            proj.CardPatches.TryGetValue(_editor.SelectedCardId, out cardDef);
                        if (cardDef != null)
                        {
                            try { var so = ModProjectBuilder.MakeFullCard(cardDef); DataHelper.RegisterCard(so); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CombatTab] Card hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.Items:
                    if (_editor.SelectedItemId != null)
                    {
                        ItemDef itemDef = null;
                        if (!proj.Items.TryGetValue(_editor.SelectedItemId, out itemDef))
                            proj.ItemPatches.TryGetValue(_editor.SelectedItemId, out itemDef);
                        if (itemDef != null)
                        {
                            try
                            {
                                var so = DataHelper.MakeFullItem(itemDef);
                                DataHelper.RegisterItem(so);
                                var card = DataHelper.MakeItemCard(itemDef, so);
                                DataHelper.RegisterCard(card);
                            }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CombatTab] Item hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.Loot:
                    if (_editor.SelectedLootId != null)
                    {
                        LootDef lootDef = null;
                        if (!proj.Loot.TryGetValue(_editor.SelectedLootId, out lootDef))
                            proj.LootPatches.TryGetValue(_editor.SelectedLootId, out lootDef);
                        if (lootDef != null)
                        {
                            try { var loot = DataHelper.MakeLoot(lootDef); DataHelper.RegisterLoot(loot); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CombatTab] Loot hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.NPCs:
                    if (_editor.SelectedNpcId != null)
                    {
                        NpcDef npcDef = null;
                        if (!proj.Npcs.TryGetValue(_editor.SelectedNpcId, out npcDef))
                            proj.NpcPatches.TryGetValue(_editor.SelectedNpcId, out npcDef);
                        if (npcDef != null)
                        {
                            try { var npc = DataHelper.MakeFullNpc(npcDef); DataHelper.RegisterNPC(npc); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CombatTab] NPC hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.AuraCurse:
                    if (_editor.SelectedAuraCurseId != null)
                    {
                        AuraCurseDef acDef = null;
                        if (!proj.AuraCurses.TryGetValue(_editor.SelectedAuraCurseId, out acDef))
                            proj.AuraCursePatches.TryGetValue(_editor.SelectedAuraCurseId, out acDef);
                        if (acDef != null)
                        {
                            try
                            {
                                var so = ModProjectBuilder.MakeAuraCurse(acDef);
                                var dict = HarmonyLib.Traverse.Create(Globals.Instance)
                                    .Field<Dictionary<string, AuraCurseData>>("_AurasCursesSource").Value;
                                if (dict != null) dict[acDef.Id.ToLower()] = so;
                            }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CombatTab] AC hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
            }
        }

        private void DrawSubTabBar()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("Cards", SubTab.Cards);
            SubTabButton("Items", SubTab.Items);
            SubTabButton("Loot", SubTab.Loot);
            SubTabButton("NPCs", SubTab.NPCs);
            SubTabButton("AuraCurse", SubTab.AuraCurse);
            GUILayout.EndHorizontal();
        }

        private void SubTabButton(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.RichLabel : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;

            if (active)
            {
                GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
            }
            else if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
            {
                ActiveSubTab = tab;
            }
        }
    }
}
