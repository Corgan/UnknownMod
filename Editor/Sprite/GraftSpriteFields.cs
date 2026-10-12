using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod.Editor
{
    /// <summary>
    /// One "Sprite Source" control. A Graft Sprite is a mod-authored sprite source (pieces of other
    /// sources or custom assets), so the choice is either a base-game source or a graft - never both.
    /// A graft's Host Model (BasePresentation) is the rig, so the stored SpriteSource follows it.
    /// </summary>
    internal static class GraftSpriteFields
    {
        /// <summary>Host model of the project's graft sprite, or null when none is set/found.</summary>
        public static string HostModel(ModProject proj, string graphId)
        {
            if (proj == null || string.IsNullOrEmpty(graphId)) return null;
            if (!proj.VisualGraphs.TryGetValue(graphId, out var g) && !proj.VisualGraphPatches.TryGetValue(graphId, out g)) return null;
            return string.IsNullOrEmpty(g.BasePresentation) ? null : g.BasePresentation;
        }

        private static readonly string[] ModeNames = { "Base Game", "Graft Sprite" };
        private static readonly Dictionary<string, int> _pendingMode = new Dictionary<string, int>();

        /// <summary>
        /// Draws the Sprite Source choice. Base Game picks an existing source (spriteSource).
        /// Graft Sprite picks a project graft (graphId); spriteSource then becomes its rig.
        /// </summary>
        public static void DrawSource(ModProject proj, string label, ref string spriteSource, ref string graphId,
            IList<string> baseChoices, string popupPrefix, EntityPicker.Mode basePicker)
        {
            bool hasGraft = !string.IsNullOrEmpty(graphId);
            int mode = hasGraft ? 1 : 0;
            // Mode persists in editor state so choosing "Graft Sprite" before picking one doesn't snap back.
            string key = popupPrefix + "_srcmode";
            if (_pendingMode.TryGetValue(key, out var pending) && !hasGraft && pending == 1) mode = 1;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100f));
            int next = GUILayout.Toolbar(mode, ModeNames, EditorStyles.MiniButton);
            GUILayout.EndHorizontal();

            if (next != mode)
            {
                mode = next;
                if (mode == 0) { graphId = ""; _pendingMode.Remove(key); }
                else _pendingMode[key] = 1;
            }

            if (mode == 0)
            {
                spriteSource = EditorFields.IdDropdown("Source", spriteSource, baseChoices, popupPrefix + "_sprsrc", pickerMode: basePicker);
                return;
            }

            var graphIds = proj.VisualGraphs.Keys.Concat(proj.VisualGraphPatches.Keys).Distinct().OrderBy(k => k).ToList();
            graphId = EditorFields.IdDropdown("Graft", graphId, graphIds, popupPrefix + "_graftsprite");
            string host = HostModel(proj, graphId);
            if (host != null && baseChoices != null && baseChoices.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                spriteSource = host;
                GUILayout.Label($"<color=#888>Rig: <b>{host}</b> (the graft's Host Model)</color>", EditorStyles.RichLabel);
            }
            else if (string.IsNullOrEmpty(graphId))
                GUILayout.Label("<color=#888>Pick a graft from the Graft Sprites tab. It becomes this entity's sprite source.</color>", EditorStyles.RichLabel);
        }    }
}
