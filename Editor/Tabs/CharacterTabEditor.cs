using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor;
using UnityEngine;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the Characters category tab with sub-tabs: Heroes, Traits, Skins.
    /// </summary>
    public class CharacterTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { Heroes, Traits, Skins, HeroData }
        public SubTab ActiveSubTab { get; set; } = SubTab.Heroes;

        public CharacterTabEditor(ModEditor editor) => _editor = editor;

        /// <summary>Per-frame tick (no-op — saves immediately on change).</summary>
        public void Tick() { }

        /// <summary>Draw a preview viewport for the active character sub-tab.</summary>
        public void DrawViewport(Rect rect)
        {
            switch (ActiveSubTab)
            {
                case SubTab.Heroes:
                    DrawHeroPreview(rect);
                    break;
                case SubTab.Traits:
                    DrawTraitPreview(rect);
                    break;
                case SubTab.Skins:
                    DrawSkinPreview(rect);
                    break;
                case SubTab.HeroData:
                    EditorStyles.ViewportBackground(rect);
                    break;
            }
        }

        // ── Viewport previews ────────────────────────────────────

        private void DrawHeroPreview(Rect rect)
        {
            string id = _editor.HeroEdit?.SelectedHeroId;
            HeroDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Heroes.TryGetValue(id, out def))
                    proj.HeroPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawHero(rect, id, def);
        }

        private void DrawTraitPreview(Rect rect)
        {
            string id = _editor.TraitEdit?.SelectedTraitId;
            TraitDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Traits.TryGetValue(id, out def))
                    proj.TraitPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawTrait(rect, id, def);
        }

        private void DrawSkinPreview(Rect rect)
        {
            string id = _editor.SkinEdit?.SelectedSkinId;
            SkinDef def = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(id))
            {
                if (!proj.Skins.TryGetValue(id, out def))
                    proj.SkinPatches.TryGetValue(id, out def);
            }
            ViewportPreview.DrawSkin(rect, id, def);
        }

        public void DrawPanel()
        {
            DrawSubTabBar();
            GUILayout.Space(4);

            switch (ActiveSubTab)
            {
                case SubTab.Heroes:
                    _editor.HeroEdit?.DrawPanel();
                    break;
                case SubTab.Traits:
                    _editor.TraitEdit?.DrawPanel();
                    break;
                case SubTab.Skins:
                    _editor.SkinEdit?.DrawPanel();
                    break;
                case SubTab.HeroData:
                    _editor.HeroDataEdit?.DrawPanel();
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
                case SubTab.Heroes:
                    changed = _editor.HeroEdit != null && _editor.HeroEdit.HandleChanges();
                    break;
                case SubTab.Traits:
                    changed = _editor.TraitEdit != null && _editor.TraitEdit.HandleChanges();
                    break;
                case SubTab.Skins:
                    changed = _editor.SkinEdit != null && _editor.SkinEdit.HandleChanges();
                    break;
                case SubTab.HeroData:
                    changed = _editor.HeroDataEdit != null && _editor.HeroDataEdit.HandleChanges();
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
                case SubTab.Heroes:
                    if (_editor.HeroEdit?.SelectedHeroId != null)
                    {
                        HeroDef heroDef = null;
                        if (!proj.Heroes.TryGetValue(_editor.HeroEdit.SelectedHeroId, out heroDef))
                            proj.HeroPatches.TryGetValue(_editor.HeroEdit.SelectedHeroId, out heroDef);
                        if (heroDef != null)
                        {
                            try { var sc = DataHelper.MakeFullHero(heroDef); DataHelper.RegisterHero(sc); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CharTab] Hero hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.Traits:
                    if (_editor.TraitEdit?.SelectedTraitId != null)
                    {
                        TraitDef traitDef = null;
                        if (!proj.Traits.TryGetValue(_editor.TraitEdit.SelectedTraitId, out traitDef))
                            proj.TraitPatches.TryGetValue(_editor.TraitEdit.SelectedTraitId, out traitDef);
                        if (traitDef != null)
                        {
                            try { var t = DataHelper.MakeTrait(traitDef); DataHelper.RegisterTrait(t); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CharTab] Trait hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.Skins:
                    if (_editor.SkinEdit?.SelectedSkinId != null)
                    {
                        SkinDef skinDef = null;
                        if (!proj.Skins.TryGetValue(_editor.SkinEdit.SelectedSkinId, out skinDef))
                            proj.SkinPatches.TryGetValue(_editor.SkinEdit.SelectedSkinId, out skinDef);
                        if (skinDef != null)
                        {
                            try { var s = DataHelper.MakeSkin(skinDef); DataHelper.RegisterSkin(s); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CharTab] Skin hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
                case SubTab.HeroData:
                    if (_editor.HeroDataEdit?.SelectedHeroDataId != null)
                    {
                        HeroDataDef hdDef = null;
                        if (!proj.HeroDataEntries.TryGetValue(_editor.HeroDataEdit.SelectedHeroDataId, out hdDef))
                            proj.HeroDataPatches.TryGetValue(_editor.HeroDataEdit.SelectedHeroDataId, out hdDef);
                        if (hdDef != null)
                        {
                            try { var hd = DataHelper.MakeHeroData(hdDef); DataHelper.RegisterHeroData(hd); }
                            catch (System.Exception ex) { Plugin.Log.LogWarning($"[CharTab] HeroData hot-reload failed: {ex.Message}"); }
                        }
                    }
                    break;
            }
        }

        private void DrawSubTabBar()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("Heroes", SubTab.Heroes);
            SubTabButton("Traits", SubTab.Traits);
            SubTabButton("Skins", SubTab.Skins);
            SubTabButton("HeroData", SubTab.HeroData);
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
