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
    /// IMGUI panel for editing card-pack definitions at the mod-project level.
    /// </summary>
    public class PackEditor : ModProjectEditorBase<PackDef>
    {
        protected override string TypeLabel => "Pack";
        protected override string FolderName => "packs";
        protected override string NewIdSuffix => "_pack";

        protected override Dictionary<string, PackDef> GetNewDict(ModProject proj) => proj.Packs;
        protected override Dictionary<string, PackDef> GetPatchDict(ModProject proj) => proj.PackPatches;

        protected override PackDef CreateDefault(string id, ModProject proj)
            => new PackDef { PackId = id };

        protected override string GetDisplayName(PackDef def) => def.PackName;

        protected override PackDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetPackData(id);
            return existing != null ? DataHelper.SnapshotPack(existing) : null;
        }

        public PackEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ─────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secCards = true;
        private bool _secSpecialCards = false;
        private bool _secPerks = false;

        // ═══════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════

        protected override void DrawAllSections(PackDef d, ModProject proj)
        {
            // ── Preview ──────────────────────────────────────────
            if (EditorFields.Section("Pack Preview", ref _secPreview))
            {
                string desc = BuildPackDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.PackId = EditorFields.TextField("Pack ID", d.PackId);
                d.PackName = EditorFields.TextField("Pack Name", d.PackName);
                d.PackClass = EditorFields.EnumField("Class", d.PackClass, "pack_class");

                var heroIds = DataHelper.GetAllSubClassIds();
                d.RequiredClassId = EditorFields.IdDropdown("Required Class", d.RequiredClassId, heroIds, "pack_reqclass");
            }

            // ── Cards ────────────────────────────────────────────
            if (EditorFields.Section($"Cards ({d.CardIds.Count})", ref _secCards))
            {
                var cardIds = EditorFields.BuildCardIdList(proj);
                for (int i = 0; i < d.CardIds.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.CardIds[i] = EditorFields.IdDropdown("", d.CardIds[i], cardIds, $"pk_card_{i}");
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.CardIds.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Card", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    d.CardIds.Add("");
                    GUI.changed = true;
                }
            }

            // ── Special Cards ────────────────────────────────────
            if (EditorFields.Section($"Special Cards ({d.SpecialCardIds.Count})", ref _secSpecialCards))
            {
                var cardIds = EditorFields.BuildCardIdList(proj);
                for (int i = 0; i < d.SpecialCardIds.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.SpecialCardIds[i] = EditorFields.IdDropdown("", d.SpecialCardIds[i], cardIds, $"pk_sp_{i}");
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.SpecialCardIds.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Special Card", EditorStyles.MiniButton, GUILayout.Width(110)))
                {
                    d.SpecialCardIds.Add("");
                    GUI.changed = true;
                }
            }

            // ── Perks ────────────────────────────────────────────
            if (EditorFields.Section($"Perks ({d.PerkIds.Count})", ref _secPerks))
            {
                var perkIds = PerkNodeEditor.BuildPerkIdList(proj);
                for (int i = 0; i < d.PerkIds.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.PerkIds[i] = EditorFields.IdDropdown("", d.PerkIds[i], perkIds, $"pk_perk_{i}");
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.PerkIds.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Perk", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    d.PerkIds.Add("");
                    GUI.changed = true;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════

        public static string BuildPackDescription(PackDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{(string.IsNullOrEmpty(d.PackName) ? d.PackId : d.PackName)}</b>");
            sb.Append($"  Class={d.PackClass}");
            sb.Append($"\n<color=#88ccff>{d.CardIds.Count} card(s)  |  {d.SpecialCardIds.Count} special  |  {d.PerkIds.Count} perk(s)</color>");
            return sb.ToString();
        }
    }
}
