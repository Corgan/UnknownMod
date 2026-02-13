using UnityEngine;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the World category tab with sub-tabs: Perks, PerkNodes, Requirements, Cardbacks, TierRewards.
    /// </summary>
    public class WorldTabEditor
    {
        private readonly ZoneEditor _editor;
        public enum SubTab { Perks, PerkNodes, Requirements, Cardbacks, TierRewards }
        public SubTab ActiveSubTab { get; set; } = SubTab.Perks;

        public WorldTabEditor(ZoneEditor editor) => _editor = editor;

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

        /// <summary>Returns true if GUI.changed was set on an editor that needs hot-reload.</summary>
        public bool HandleChanges()
        {
            if (!GUI.changed) return false;

            switch (ActiveSubTab)
            {
                case SubTab.Perks:
                    return _editor.PerkEdit != null && _editor.PerkEdit.HandleChanges();
                case SubTab.PerkNodes:
                    return _editor.PerkNodeEdit != null && _editor.PerkNodeEdit.HandleChanges();
                case SubTab.Requirements:
                    return _editor.RequirementEdit != null && _editor.RequirementEdit.HandleChanges();
                case SubTab.Cardbacks:
                    return _editor.CardbackEdit != null && _editor.CardbackEdit.HandleChanges();
                case SubTab.TierRewards:
                    return _editor.TierRewardEdit != null && _editor.TierRewardEdit.HandleChanges();
            }
            return false;
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
