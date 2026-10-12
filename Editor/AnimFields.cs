using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>Animation retarget fields shared by the NPC and Skin editors.</summary>
    public static class AnimFields
    {
        private static readonly Dictionary<string, string> Descriptions = new()
        {
            { "attack", "Weapon and melee card casts" },
            { "cast", "Spell and non-melee card casts" },
            { "hit", "When the character takes a hit" },
            { "pet", "Pet casts and idle tricks" },
        };

        /// <summary>Draws the retarget map as one row per game trigger. A blank row leaves that trigger alone.</summary>
        public static void Draw(Dictionary<string, string> map, ref bool noFallback, string idKey)
        {
            if (map == null) return;
            GUILayout.Label("<color=#888>The game fires four animator triggers. If the rig plays a different animation (or none, like a monster used as a hero), say which trigger it should fire instead. Blank = unchanged, <b>-</b> = play nothing.</color>", EditorStyles.RichLabel);
            foreach (var game in AnimRetarget.GameTriggers)
            {
                map.TryGetValue(game, out var cur);
                string tip = Descriptions.TryGetValue(game, out var t) ? t : "";
                var next = EditorFields.TextField($"{game} plays", cur ?? "");
                if (next != (cur ?? ""))
                {
                    if (string.IsNullOrWhiteSpace(next)) map.Remove(game); else map[game] = next.Trim();
                }
                if (tip.Length > 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(104f);
                    GUILayout.Label($"<color=#777>{tip}</color>", EditorStyles.RichLabel);
                    GUILayout.EndHorizontal();
                }
            }
            noFallback = EditorFields.Toggle("No automatic fallback", noFallback);
            GUILayout.Label("<color=#888>By default a rig without a cast animation attacks instead, and the reverse.</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Presets", EditorStyles.RichLabel, GUILayout.Width(100));
            if (GUILayout.Button(new GUIContent("Monster as hero", "Rigs with only an attack and a hit animation: casts reuse the attack."), EditorStyles.MiniButton))
            {
                map.Clear();
                map["cast"] = "attack";
                noFallback = false;
            }
            if (GUILayout.Button(new GUIContent("Always attack", "Every attack and cast plays the attack animation."), EditorStyles.MiniButton))
            {
                map.Clear();
                map["cast"] = "attack";
            }
            if (GUILayout.Button(new GUIContent("Always cast", "Every attack and cast plays the cast animation."), EditorStyles.MiniButton))
            {
                map.Clear();
                map["attack"] = "cast";
            }
            if (GUILayout.Button(new GUIContent("No hit reaction", "Swallow the hit animation."), EditorStyles.MiniButton))
                map["hit"] = AnimRetarget.Swallow;
            if (GUILayout.Button("Clear", EditorStyles.MiniButton))
            {
                map.Clear();
                noFallback = false;
            }
            GUILayout.EndHorizontal();

            if (AnimRetarget.Recent.Count > 0)
            {
                GUILayout.Label("<color=#888>Recent retargets this session:</color>", EditorStyles.RichLabel);
                for (int i = AnimRetarget.Recent.Count - 1; i >= 0 && i >= AnimRetarget.Recent.Count - 6; i--)
                    GUILayout.Label("  " + AnimRetarget.Recent[i], EditorStyles.RichLabel);
            }
        }
    }
}
