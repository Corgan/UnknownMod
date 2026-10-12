using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// One-click rig setup for the Skin and NPC editors: pick any monster or hero as the model, and the builder
    /// creates the graft that turns it the right way round, applies a hue or tint, flips the portraits and sets the animation preset.
    /// </summary>
    internal static class RigBuilderFields
    {
        private sealed class State
        {
            public string Rig = "";
            public string LastRig = "";
            public bool Flip = true;
            public bool FlipPortrait = true;
            public float Hue;
            public string Tint = "";
            public bool AnimPreset = true;
            public string Message = "";
            public string ConfirmReplace = "";
        }

        private static readonly Dictionary<string, State> States = new();

        /// <param name="asHero">True for a skin (monster rigs face left and need flipping), false for an NPC (hero rigs face right).</param>
        public static void Draw(ModProject proj, string ownerId, string key, bool asHero, IList<string> rigChoices, EntityPicker.Mode pickMode,
            ref string spriteSource, ref string graphId, ref bool flipPortrait, Dictionary<string, string> anim, ref bool noFallback)
        {
            if (!States.TryGetValue(key, out var st)) States[key] = st = new State();
            if (st.LastRig == "" && st.Rig == "") st.Rig = spriteSource ?? "";

            GUILayout.Label("<color=#888>Pick any monster or hero and the model is turned into this " + (asHero ? "hero" : "enemy") + " in one click: it faces the right way, portraits match, and missing animations fall back to ones the model has.</color>", EditorStyles.RichLabel);
            st.Rig = EditorFields.IdDropdown("Model", st.Rig, rigChoices, key + "_rbrig", pickerMode: pickMode);

            bool isNpc = DataHelper.GetAllNpcIds().Contains(st.Rig);
            bool isSkin = !isNpc && DataHelper.GetAllSkinIds().Contains(st.Rig);
            if (st.Rig != st.LastRig && (isNpc || isSkin))
            {
                // Monsters face left and heroes face right, so a model of the other kind needs flipping.
                st.Flip = asHero ? isNpc : isSkin;
                st.FlipPortrait = st.Flip;
                st.AnimPreset = asHero && isNpc;
                st.LastRig = st.Rig;
            }

            if (isNpc || isSkin)
                GUILayout.Label($"<color=#888>{(isNpc ? "A monster model (faces left)." : "A hero model (faces right).")}</color>", EditorStyles.RichLabel);

            st.Flip = EditorFields.Toggle("Flip model", st.Flip);
            st.FlipPortrait = EditorFields.Toggle("Flip portraits", st.FlipPortrait);
            st.Hue = Mathf.Clamp(EditorFields.FloatField("Hue shift", st.Hue), -180f, 180f);
            st.Tint = EditorFields.TextField("Tint (#hex)", st.Tint);
            if (asHero) st.AnimPreset = EditorFields.Toggle("Casts use attack", st.AnimPreset);

            GUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(st.Rig) && (isNpc || isSkin) && !string.IsNullOrEmpty(ownerId);
            string gid = ownerId + "_rig";
            bool exists = proj != null && (proj.VisualGraphs.ContainsKey(gid) || proj.VisualGraphPatches.ContainsKey(gid));
            bool confirming = exists && st.ConfirmReplace == gid;
            if (GUILayout.Button(confirming ? "Replace?" : "Build model", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                bool needGraph = st.Flip || Mathf.Abs(st.Hue) > 0.01f || !string.IsNullOrWhiteSpace(st.Tint);
                if (needGraph && proj.VisualGraphPatches.ContainsKey(gid))
                    st.Message = $"'{gid}' already exists as a patch of a game graph; pick another owner id or delete the patch first.";
                else if (needGraph && exists && !confirming)
                {
                    // Rebuilding throws away any nodes or effects edited on this graph by hand
                    st.ConfirmReplace = gid;
                    st.Message = $"'{gid}' already exists. Click Replace? to rebuild it (hand edits to that graph are lost).";
                }
                else
                {
                st.ConfirmReplace = "";
                spriteSource = st.Rig;
                if (needGraph)
                {
                    var graph = new CharacterVisualDef { Id = gid, BasePresentation = st.Rig, FlipX = st.Flip, HueShift = st.Hue, TintHex = st.Tint?.Trim() ?? "" };
                    graph.Normalize();
                    proj.VisualGraphs[gid] = graph;
                    ModProjectLoader.SaveEntity(proj, "visualgraphs", gid, graph, false);
                    graphId = gid;
                }
                else if (graphId == gid) graphId = "";
                flipPortrait = st.FlipPortrait;
                if (asHero && anim != null)
                {
                    if (st.AnimPreset) { anim["cast"] = "attack"; noFallback = false; }
                    else if (anim.TryGetValue("cast", out var c) && c == "attack") anim.Remove("cast");
                }
                proj.IsDirty = true;
                GUI.changed = true; // lets the owning editor save the new source and graph id
                st.Message = needGraph ? $"Built graft '{gid}' on {st.Rig}. Rebuild the mod to see it in game." : $"Using {st.Rig} unchanged.";
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(st.Message)) GUILayout.Label("<color=#7c7>" + st.Message + "</color>", EditorStyles.RichLabel);
        }
    }
}
