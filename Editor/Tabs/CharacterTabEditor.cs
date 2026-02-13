using UnknownMod.Core;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the Characters category tab with sub-tabs: Heroes, Traits, Skins.
    /// </summary>
    public class CharacterTabEditor
    {
        private readonly ZoneEditor _editor;
        public enum SubTab { Heroes, Traits, Skins }
        public SubTab ActiveSubTab { get; set; } = SubTab.Heroes;

        public CharacterTabEditor(ZoneEditor editor) => _editor = editor;

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
            }
        }

        /// <summary>Returns true if GUI.changed was set on an editor that needs hot-reload.</summary>
        public bool HandleChanges()
        {
            if (!GUI.changed) return false;

            switch (ActiveSubTab)
            {
                case SubTab.Heroes:
                    return _editor.HeroEdit != null && _editor.HeroEdit.HandleChanges();
                case SubTab.Traits:
                    return _editor.TraitEdit != null && _editor.TraitEdit.HandleChanges();
                case SubTab.Skins:
                    return _editor.SkinEdit != null && _editor.SkinEdit.HandleChanges();
            }
            return false;
        }

        private void DrawSubTabBar()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("Heroes", SubTab.Heroes);
            SubTabButton("Traits", SubTab.Traits);
            SubTabButton("Skins", SubTab.Skins);
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
