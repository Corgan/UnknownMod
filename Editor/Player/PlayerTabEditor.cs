using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the Player category tab with sub-tabs: Perks, PerkNodes, Cardbacks, PlayerPacks.
    /// Shared player-level progression and cosmetics (not hero-specific).
    /// </summary>
    public class PlayerTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { Perks, PerkNodes, Cardbacks, PlayerPacks }
        public SubTab ActiveSubTab { get; set; } = SubTab.Perks;

        public PlayerTabEditor(ModEditor editor) => _editor = editor;

        /// <summary>Per-frame tick (no-op — saves immediately on change).</summary>
        public void Tick() { }

        /// <summary>Draw a preview viewport for the active player sub-tab.</summary>
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
                case SubTab.Cardbacks:
                    DrawCardbackPreview(rect);
                    break;
                case SubTab.PlayerPacks:
                    EditorStyles.ViewportBackground(rect);
                    break;
            }
        }

        // ── Viewport previews ────────────────────────────────────

        private void DrawPerkPreview(Rect rect)
        {
            string id = _editor.PerkEdit?.SelectedId;
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
            string id = _editor.PerkNodeEdit?.SelectedId;
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
            string id = _editor.CardbackEdit?.SelectedId;
            CardbackDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Cardbacks.TryGetValue(id, out def))
                    proj.CardbackPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawCardback(rect, id, def);
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
                case SubTab.Cardbacks:
                    _editor.CardbackEdit?.DrawPanel();
                    break;
                case SubTab.PlayerPacks:
                    _editor.CardPlayerPackEdit?.DrawPanel();
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
                case SubTab.Cardbacks:
                    changed = _editor.CardbackEdit != null && _editor.CardbackEdit.HandleChanges();
                    break;
                case SubTab.PlayerPacks:
                    changed = _editor.CardPlayerPackEdit != null && _editor.CardPlayerPackEdit.HandleChanges();
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
                    if (_editor.PerkEdit?.SelectedId != null)
                    {
                        PerkDef perkDef = null;
                        if (!proj.Perks.TryGetValue(_editor.PerkEdit.SelectedId, out perkDef))
                            proj.PerkPatches.TryGetValue(_editor.PerkEdit.SelectedId, out perkDef);
                        if (perkDef != null)
                        {
                            try { var p = DataHelper.MakePerk(perkDef); DataHelper.RegisterPerk(p); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[PlayerTab] Perk hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.PerkNodes:
                    if (_editor.PerkNodeEdit?.SelectedId != null)
                    {
                        PerkNodeDef pnDef = null;
                        if (!proj.PerkNodes.TryGetValue(_editor.PerkNodeEdit.SelectedId, out pnDef))
                            proj.PerkNodePatches.TryGetValue(_editor.PerkNodeEdit.SelectedId, out pnDef);
                        if (pnDef != null)
                        {
                            try { var pn = DataHelper.MakePerkNode(pnDef); DataHelper.RegisterPerkNode(pn); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[PlayerTab] PerkNode hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.Cardbacks:
                    if (_editor.CardbackEdit?.SelectedId != null)
                    {
                        CardbackDef cbDef = null;
                        if (!proj.Cardbacks.TryGetValue(_editor.CardbackEdit.SelectedId, out cbDef))
                            proj.CardbackPatches.TryGetValue(_editor.CardbackEdit.SelectedId, out cbDef);
                        if (cbDef != null)
                        {
                            try { var cb = DataHelper.MakeCardback(cbDef); DataHelper.RegisterCardback(cb); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[PlayerTab] Cardback hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.PlayerPacks:
                    // Hot-reload player packs
                    if (_editor.CardPlayerPackEdit?.SelectedPlayerPackId != null)
                    {
                        CardPlayerPackDef cppDef = null;
                        if (!proj.CardPlayerPacks.TryGetValue(_editor.CardPlayerPackEdit.SelectedPlayerPackId, out cppDef))
                            proj.CardPlayerPackPatches.TryGetValue(_editor.CardPlayerPackEdit.SelectedPlayerPackId, out cppDef);
                        if (cppDef != null)
                        {
                            try { var cp = DataHelper.MakeCardPlayerPack(cppDef); DataHelper.RegisterCardPlayerPack(cp); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[PlayerTab] CardPlayerPack hot-reload failed: {ex.Message}"); }
                        }
                    }
                    // Hot-reload pairs packs
                    if (_editor.CardPlayerPackEdit?.SelectedPairsPackId != null)
                    {
                        CardPlayerPairsPackDef cppDef2 = null;
                        if (!proj.CardPlayerPairsPacks.TryGetValue(_editor.CardPlayerPackEdit.SelectedPairsPackId, out cppDef2))
                            proj.CardPlayerPairsPackPatches.TryGetValue(_editor.CardPlayerPackEdit.SelectedPairsPackId, out cppDef2);
                        if (cppDef2 != null)
                        {
                            try { var cpp = DataHelper.MakeCardPlayerPairsPack(cppDef2); DataHelper.RegisterCardPlayerPairsPack(cpp); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[PlayerTab] CardPlayerPairsPack hot-reload failed: {ex.Message}"); }
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
            SubTabButton("Cardbacks", SubTab.Cardbacks);
            SubTabButton("PlrPacks", SubTab.PlayerPacks);
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
