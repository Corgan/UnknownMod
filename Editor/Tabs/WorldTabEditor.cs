using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the World category tab with sub-tabs: Perks, PerkNodes, Requirements, Cardbacks, TierRewards.
    /// </summary>
    public class WorldTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { Perks, PerkNodes, Requirements, Cardbacks, TierRewards }
        public SubTab ActiveSubTab { get; set; } = SubTab.Perks;

        public WorldTabEditor(ModEditor editor) => _editor = editor;

        /// <summary>Per-frame tick (no-op — saves immediately on change).</summary>
        public void Tick() { }

        /// <summary>Draw a preview viewport for the active world sub-tab.</summary>
        public void DrawViewport(Rect rect)
        {
            switch (ActiveSubTab)
            {
                case SubTab.Perks:
                    DrawPerkPreview(rect);
                    break;
                case SubTab.PerkNodes:
                    DrawPerkNodePreview(rect);
                    break;
                case SubTab.Requirements:
                    // Blank viewport — still rendered, not transparent
                    EditorStyles.ViewportBackground(rect);
                    break;
                case SubTab.Cardbacks:
                    DrawCardbackPreview(rect);
                    break;
                case SubTab.TierRewards:
                    DrawTierRewardPreview(rect);
                    break;
            }
        }

        // ── Viewport previews ────────────────────────────────────

        private void DrawPerkPreview(Rect rect)
        {
            string id = _editor.PerkEdit?.SelectedPerkId;
            PerkDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Perks.TryGetValue(id, out def))
                    proj.PerkPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawPerk(rect, id, def);
        }

        private void DrawPerkNodePreview(Rect rect)
        {
            string id = _editor.PerkNodeEdit?.SelectedPerkNodeId;
            PerkNodeDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.PerkNodes.TryGetValue(id, out def))
                    proj.PerkNodePatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawPerkNode(rect, id, def);
        }

        private void DrawCardbackPreview(Rect rect)
        {
            string id = _editor.CardbackEdit?.SelectedCardbackId;
            CardbackDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Cardbacks.TryGetValue(id, out def))
                    proj.CardbackPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawCardback(rect, id, def);
        }

        private void DrawTierRewardPreview(Rect rect)
        {
            string id = _editor.TierRewardEdit?.SelectedTierRewardId;
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
                case SubTab.Perks:
                    _editor.PerkEdit?.DrawPanel();
                    break;
                case SubTab.PerkNodes:
                    _editor.PerkNodeEdit?.DrawPanel();
                    break;
                case SubTab.Requirements:
                    _editor.RequirementEdit?.DrawPanel();
                    break;
                case SubTab.Cardbacks:
                    _editor.CardbackEdit?.DrawPanel();
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
                case SubTab.Perks:
                    changed = _editor.PerkEdit != null && _editor.PerkEdit.HandleChanges();
                    break;
                case SubTab.PerkNodes:
                    changed = _editor.PerkNodeEdit != null && _editor.PerkNodeEdit.HandleChanges();
                    break;
                case SubTab.Requirements:
                    changed = _editor.RequirementEdit != null && _editor.RequirementEdit.HandleChanges();
                    break;
                case SubTab.Cardbacks:
                    changed = _editor.CardbackEdit != null && _editor.CardbackEdit.HandleChanges();
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
                case SubTab.Perks:
                    if (_editor.PerkEdit?.SelectedPerkId != null)
                    {
                        PerkDef perkDef = null;
                        if (!proj.Perks.TryGetValue(_editor.PerkEdit.SelectedPerkId, out perkDef))
                            proj.PerkPatches.TryGetValue(_editor.PerkEdit.SelectedPerkId, out perkDef);
                        if (perkDef != null)
                        {
                            try { var p = DataHelper.MakePerk(perkDef); DataHelper.RegisterPerk(p); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[WorldTab] Perk hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.PerkNodes:
                    if (_editor.PerkNodeEdit?.SelectedPerkNodeId != null)
                    {
                        PerkNodeDef pnDef = null;
                        if (!proj.PerkNodes.TryGetValue(_editor.PerkNodeEdit.SelectedPerkNodeId, out pnDef))
                            proj.PerkNodePatches.TryGetValue(_editor.PerkNodeEdit.SelectedPerkNodeId, out pnDef);
                        if (pnDef != null)
                        {
                            try { var pn = DataHelper.MakePerkNode(pnDef); DataHelper.RegisterPerkNode(pn); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[WorldTab] PerkNode hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.Requirements:
                    if (_editor.RequirementEdit?.SelectedRequirementId != null)
                    {
                        RequirementDef rDef = null;
                        if (!proj.Requirements.TryGetValue(_editor.RequirementEdit.SelectedRequirementId, out rDef))
                            proj.RequirementPatches.TryGetValue(_editor.RequirementEdit.SelectedRequirementId, out rDef);
                        if (rDef != null)
                        {
                            try { var req = DataHelper.MakeRequirement(rDef); DataHelper.RegisterRequirement(req); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[WorldTab] Requirement hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.Cardbacks:
                    if (_editor.CardbackEdit?.SelectedCardbackId != null)
                    {
                        CardbackDef cbDef = null;
                        if (!proj.Cardbacks.TryGetValue(_editor.CardbackEdit.SelectedCardbackId, out cbDef))
                            proj.CardbackPatches.TryGetValue(_editor.CardbackEdit.SelectedCardbackId, out cbDef);
                        if (cbDef != null)
                        {
                            try { var cb = DataHelper.MakeCardback(cbDef); DataHelper.RegisterCardback(cb); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[WorldTab] Cardback hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.TierRewards:
                    if (_editor.TierRewardEdit?.SelectedTierRewardId != null)
                    {
                        TierRewardDef trDef = null;
                        if (!proj.TierRewards.TryGetValue(_editor.TierRewardEdit.SelectedTierRewardId, out trDef))
                            proj.TierRewardPatches.TryGetValue(_editor.TierRewardEdit.SelectedTierRewardId, out trDef);
                        if (trDef != null)
                        {
                            try { var tr = DataHelper.MakeTierReward(trDef); DataHelper.RegisterTierReward(tr); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[WorldTab] TierReward hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
            }
        }

        private void DrawSubTabBar()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("Perks", SubTab.Perks);
            SubTabButton("PerkNodes", SubTab.PerkNodes);
            SubTabButton("Reqs", SubTab.Requirements);
            SubTabButton("Cardbacks", SubTab.Cardbacks);
            SubTabButton("Tiers", SubTab.TierRewards);
            GUILayout.EndHorizontal();
        }

        private void SubTabButton(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.RichLabel : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;

            if (active)
                GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
            else if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                ActiveSubTab = tab;
        }
    }
}
