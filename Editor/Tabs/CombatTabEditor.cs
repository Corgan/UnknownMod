using UnityEngine;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the Combat category tab with sub-tabs: Cards, Items, Loot, NPCs, AuraCurse.
    /// </summary>
    public class CombatTabEditor
    {
        private readonly ZoneEditor _editor;
        public enum SubTab { Cards, Items, Loot, NPCs, AuraCurse }
        public SubTab ActiveSubTab { get; set; } = SubTab.Cards;

        public CombatTabEditor(ZoneEditor editor) => _editor = editor;

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

        /// <summary>Returns true if GUI.changed was set on an editor that needs hot-reload.</summary>
        public bool HandleChanges()
        {
            if (!GUI.changed) return false;

            switch (ActiveSubTab)
            {
                case SubTab.Cards:
                    return _editor.CardEdit != null && _editor.CardEdit.HandleChanges();
                case SubTab.Items:
                    return _editor.ItemEdit != null && _editor.ItemEdit.HandleChanges();
                case SubTab.Loot:
                    return _editor.LootEdit != null && _editor.LootEdit.HandleChanges();
                case SubTab.NPCs:
                    return _editor.NpcEdit != null && _editor.NpcEdit.HandleChanges();
                case SubTab.AuraCurse:
                    return _editor.AuraCurseEdit != null && _editor.AuraCurseEdit.HandleChanges();
            }
            return false;
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
