using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the Enemies category tab with sub-tabs: NPCs, Loot, TierRewards.
    /// Enemy definitions and their drop/reward systems.
    /// </summary>
    public class EnemiesTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { NPCs, Loot, TierRewards }
        public SubTab ActiveSubTab { get; set; } = SubTab.NPCs;

        public EnemiesTabEditor(ModEditor editor) => _editor = editor;

        /// <summary>Per-frame tick (no-op — saves immediately on change).</summary>
        public void Tick() { }

        /// <summary>Draw a preview viewport for the active enemies sub-tab.</summary>
        public void DrawViewport(Rect rect)
        {
            switch (ActiveSubTab)
            {
                case SubTab.NPCs:
                    DrawNpcPreview(rect);
                    break;
                case SubTab.Loot:
                    DrawLootPreview(rect);
                    break;
                case SubTab.TierRewards:
                    DrawTierRewardPreview(rect);
                    break;
            }
        }

        // ── Viewport previews ────────────────────────────────────

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

        private void DrawTierRewardPreview(Rect rect)
        {
            string id = _editor.TierRewardEdit?.SelectedId;
            TierRewardDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.TierRewards.TryGetValue(id, out def))
                    proj.TierRewardPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawTierReward(rect, id, def);
        }

        public void DrawPanel()
        {
            DrawSubTabBar();
            GUILayout.Space(4);

            switch (ActiveSubTab)
            {
                case SubTab.NPCs:
                    _editor.NpcEdit?.DrawPanel();
                    break;
                case SubTab.Loot:
                    _editor.LootEdit?.DrawPanel();
                    break;
                case SubTab.TierRewards:
                    _editor.TierRewardEdit?.DrawPanel();
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
                case SubTab.NPCs:
                    changed = _editor.NpcEdit != null && _editor.NpcEdit.HandleChanges();
                    break;
                case SubTab.Loot:
                    changed = _editor.LootEdit != null && _editor.LootEdit.HandleChanges();
                    break;
                case SubTab.TierRewards:
                    changed = _editor.TierRewardEdit != null && _editor.TierRewardEdit.HandleChanges();
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
                case SubTab.NPCs:
                    if (_editor.SelectedNpcId != null)
                    {
                        NpcDef npcDef = null;
                        if (!proj.Npcs.TryGetValue(_editor.SelectedNpcId, out npcDef))
                            proj.NpcPatches.TryGetValue(_editor.SelectedNpcId, out npcDef);
                        if (npcDef != null)
                        {
                            try { var npc = DataHelper.MakeFullNpc(npcDef); DataHelper.RegisterNPC(npc); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[EnemiesTab] NPC hot-reload failed: {ex.Message}"); }
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
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[EnemiesTab] Loot hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.TierRewards:
                    if (_editor.TierRewardEdit?.SelectedId != null)
                    {
                        TierRewardDef trDef = null;
                        if (!proj.TierRewards.TryGetValue(_editor.TierRewardEdit.SelectedId, out trDef))
                            proj.TierRewardPatches.TryGetValue(_editor.TierRewardEdit.SelectedId, out trDef);
                        if (trDef != null)
                        {
                            try { var tr = DataHelper.MakeTierReward(trDef); DataHelper.RegisterTierReward(tr); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[EnemiesTab] TierReward hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
            }
        }

        private void DrawSubTabBar()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("NPCs", SubTab.NPCs);
            SubTabButton("Loot", SubTab.Loot);
            SubTabButton("TierRewards", SubTab.TierRewards);
            GUILayout.EndHorizontal();
        }

        private void SubTabButton(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;

            if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                ActiveSubTab = tab;
        }
    }
}
