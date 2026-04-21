using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for editing perk-node definitions at the mod-project level.
    /// </summary>
    public class PerkNodeEditor : ModProjectEditorBase<PerkNodeDef>
    {
        protected override string TypeLabel => "PerkNode";
        protected override string FolderName => "perknodes";
        protected override string NewIdSuffix => "_new_perknode";        protected override EntityPicker.Mode? PickerMode => EntityPicker.Mode.PerkNode;
        protected override Dictionary<string, PerkNodeDef> GetNewDict(ModProject proj) => proj.PerkNodes;
        protected override Dictionary<string, PerkNodeDef> GetPatchDict(ModProject proj) => proj.PerkNodePatches;

        protected override PerkNodeDef CreateDefault(string id, ModProject proj)
            => new PerkNodeDef { Id = id };

        protected override string GetDisplayName(PerkNodeDef def)
            => !string.IsNullOrEmpty(def.Perk) ? $"({def.Perk})" : def.Id;

        protected override PerkNodeDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetPerkNode(id);
            return existing != null ? DataHelper.SnapshotPerkNode(existing) : null;
        }

        public PerkNodeEditor(ModEditor parent) : base(parent) { }

        //  Collapsible section state 
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secLayout = false;
        private bool _secFlags = false;
        private bool _secRefs = false;
        private bool _secConnected = false;

        // 
        //  ALL SECTIONS
        // 

        protected override void DrawAllSections(PerkNodeDef d, ModProject proj)
        {
            //  Preview 
            if (EditorFields.Section("PerkNode Preview", ref _secPreview))
            {
                string desc = BuildPerkNodeDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            //  Identity 
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);

                var perkIds = BuildPerkIdList(proj);
                d.Perk = EditorFields.IdDropdown("Perk", d.Perk, perkIds, "pn_perk", pickerMode: EntityPicker.Mode.Perk);

                var pnIds = BuildPerkNodeIdList(proj);
                d.SpriteSource = EditorFields.IdDropdown("Sprite Source", d.SpriteSource, pnIds, "pn_sprsrc", pickerMode: EntityPicker.Mode.PerkNode);
            }

            //  Layout 
            if (EditorFields.Section("Layout", ref _secLayout))
            {
                d.Row = EditorFields.IntField("Row", d.Row, 0, 6);
                d.Column = EditorFields.IntField("Column", d.Column, 0, 11);
                d.Type = EditorFields.IntField("Type", d.Type, 0, 3);
            }

            //  Flags 
            if (EditorFields.Section("Flags", ref _secFlags))
            {
                d.LockedInTown = EditorFields.Toggle("Locked In Town", d.LockedInTown);
                d.NotStack = EditorFields.Toggle("Not Stack", d.NotStack);
                d.Cost = EditorFields.EnumField("Cost", d.Cost, "pn_cost");
            }

            //  References 
            if (EditorFields.Section("References", ref _secRefs))
            {
                var pnIds = BuildPerkNodeIdList(proj);
                d.PerkRequired = EditorFields.IdDropdown("Required Node", d.PerkRequired, pnIds, "pn_reqnode", pickerMode: EntityPicker.Mode.PerkNode);
            }

            //  Connected Nodes 
            if (EditorFields.Section($"Connected Nodes ({d.PerksConnected.Count})", ref _secConnected))
            {
                var pnIds = BuildPerkNodeIdList(proj);
                for (int i = 0; i < d.PerksConnected.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.PerksConnected[i] = EditorFields.IdDropdown("", d.PerksConnected[i], pnIds, $"pn_cn_{i}", pickerMode: EntityPicker.Mode.PerkNode);
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.PerksConnected.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Node", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    d.PerksConnected.Add("");
                    GUI.changed = true;
                }
            }
        }

        // 
        //  HELPERS
        // 

        /// <summary>Build a combined list of perk IDs (mod + base game).</summary>
        public static List<string> BuildPerkIdList(ModProject proj)
        {
            var list = new List<string>();
            list.AddRange(proj.Perks.Keys.OrderBy(k => k));
            list.AddRange(proj.PerkPatches.Keys.OrderBy(k => k));
            list.AddRange(EditorFields.CachedIds("perk", DataHelper.GetAllPerkIds));
            return list.Distinct().ToList();
        }

        /// <summary>Build a combined list of perk-node IDs (mod + base game).</summary>
        public static List<string> BuildPerkNodeIdList(ModProject proj)
        {
            var list = new List<string>();
            list.AddRange(proj.PerkNodes.Keys.OrderBy(k => k));
            list.AddRange(proj.PerkNodePatches.Keys.OrderBy(k => k));
            list.AddRange(EditorFields.CachedIds("perknode", DataHelper.GetAllPerkNodeIds));
            return list.Distinct().ToList();
        }

        // 
        //  LIVE DESCRIPTION BUILDER
        // 

        public static string BuildPerkNodeDescription(PerkNodeDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{d.Id}</b>  <color=#aaa>Type {d.Type}</color>");
            if (!string.IsNullOrEmpty(d.Perk))
                sb.Append($"\n<color=#88ccff>Perk: {d.Perk}</color>");
            sb.Append($"\nRow {d.Row}  Col {d.Column}");
            if (d.LockedInTown) sb.Append("  <color=#cc4444>[LOCKED]</color>");
            if (d.PerksConnected.Count > 0)
                sb.Append($"\n<color=#88aacc>{d.PerksConnected.Count} connection(s)</color>");
            return sb.ToString();
        }
    }
}
