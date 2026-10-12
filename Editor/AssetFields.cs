using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>Pickers for audio clips and VFX effects that every entity editor shares.</summary>
    public static class AssetFields
    {
        public static IList<string> AudioNames => EditorFields.CachedIds("audio", AudioUtils.AllNames);
        public static IList<string> EffectNames => EditorFields.CachedIds("vfx", () => MapEditor.GetVfxEffectNames());

        /// <summary>Audio clip dropdown (mod clips first, then game clips) with play and stop buttons and a jump to the Assets tab.</summary>
        public static string AudioField(string label, string current, string popupId = null)
        {
            string id = popupId ?? "audio_" + label;
            GUILayout.BeginVertical();
            current = EditorFields.IdDropdown(label, current, AudioNames, id);
            if (!string.IsNullOrEmpty(current))
            {
                bool known = AudioUtils.Find(current) != null;
                GUILayout.BeginHorizontal();
                GUILayout.Space(104f);
                bool playing = AudioUtils.IsPlaying && AudioUtils.PlayingName == current;
                if (GUILayout.Button(playing ? "\u25A0 Stop" : "\u25B6 Play", EditorStyles.MiniButton, GUILayout.Width(60)))
                {
                    if (playing) AudioUtils.StopPreview(); else AudioUtils.Preview(current);
                }
                if (GUILayout.Button("Open in Assets", EditorStyles.MiniButton, GUILayout.Width(100)))
                    ModEditor.Instance?.AssetsTab.OpenAudio(current);
                if (!known) GUILayout.Label("<color=#f96>clip not loaded</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            return current;
        }

        /// <summary>A list of audio clips (variants), each with its own picker and preview. Returns true if the list changed shape this frame.</summary>
        public static void AudioListField(string label, List<string> list, string popupId)
        {
            if (list == null) return;
            GUILayout.Label(label, EditorStyles.RichLabel);
            for (int i = 0; i < list.Count; i++)
            {
                GUILayout.BeginHorizontal();
                list[i] = AudioField($"  #{i + 1}", list[i], $"{popupId}_{i}");
                var op = EditorFields.InlineRowOps(i, list.Count);
                GUILayout.EndHorizontal();
                if (EditorFields.ApplyRowOp(list, i, op)) return;
            }
            if (GUILayout.Button("+ Add variant", EditorStyles.MiniButton, GUILayout.Width(110))) list.Add("");
        }

        /// <summary>VFX effect dropdown with a jump to the Assets tab where it plays on a loop.</summary>
        public static string VfxField(string label, string current, string popupId = null)
        {
            string id = popupId ?? "vfx_" + label;
            GUILayout.BeginVertical();
            current = EditorFields.IdDropdown(label, current, EffectNames, id);
            if (!string.IsNullOrEmpty(current))
            {
                bool known = GameResources.GetEffectPrefab(current) != null;
                GUILayout.BeginHorizontal();
                GUILayout.Space(104f);
                if (GUILayout.Button("\u25B6 Preview", EditorStyles.MiniButton, GUILayout.Width(80)))
                    ModEditor.Instance?.AssetsTab.OpenEffect(current);
                if (!known) GUILayout.Label("<color=#f96>no such effect</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            return current;
        }
    }
}
