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
    /// IMGUI panel for editing event-requirement definitions at the mod-project level.
    /// </summary>
    public class RequirementEditor : ModProjectEditorBase<RequirementDef>
    {
        protected override string TypeLabel => "Requirement";
        protected override string FolderName => "requirements";
        protected override string NewIdSuffix => "_new_req";

        protected override Dictionary<string, RequirementDef> GetNewDict(ModProject proj) => proj.Requirements;
        protected override Dictionary<string, RequirementDef> GetPatchDict(ModProject proj) => proj.RequirementPatches;

        protected override RequirementDef CreateDefault(string id, ModProject proj)
            => new RequirementDef { Id = id, RequirementName = "New Requirement" };

        protected override string GetDisplayName(RequirementDef def)
            => def.RequirementName;

        protected override List<string> GetAllBaseIds()
            => DataHelper.GetAllEventRequirementIds();

        protected override RequirementDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetEventRequirement(id);
            return existing != null ? DataHelper.SnapshotRequirement(existing) : null;
        }

        public RequirementEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ─────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secTracking = false;
        private bool _secZone = false;
        private bool _secCardRef = false;

        // ═══════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════

        protected override void DrawAllSections(RequirementDef d, ModProject proj)
        {
            // ── Preview ──────────────────────────────────────────
            if (EditorFields.Section("Requirement Preview", ref _secPreview))
            {
                string desc = BuildRequirementDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.RequirementName = EditorFields.TextField("Name", d.RequirementName);
                d.Description = EditorFields.TextField("Description", d.Description);
            }

            // ── Tracking ─────────────────────────────────────────
            if (EditorFields.Section("Tracking", ref _secTracking))
            {
                d.AssignToPlayerAtBegin = EditorFields.Toggle("Assign At Begin", d.AssignToPlayerAtBegin);
                d.RequirementTrack = EditorFields.Toggle("Requirement Track", d.RequirementTrack);
                d.ItemTrack = EditorFields.Toggle("Item Track", d.ItemTrack);
            }

            // ── Zone Tracking ────────────────────────────────────
            if (EditorFields.Section("Zone Tracking", ref _secZone))
            {
                d.RequirementZoneFinishTrack = EditorFields.EnumField("Zone Finish", d.RequirementZoneFinishTrack, "req_zone");
                d.RequirementZoneFinishTrackAlternate = EditorFields.EnumField("Zone Finish Alt", d.RequirementZoneFinishTrackAlternate, "req_zone_alt");
            }

            // ── Card Reference ───────────────────────────────────
            if (EditorFields.Section("Card Reference", ref _secCardRef))
            {
                var cardIds = EditorFields.BuildCardIdList(proj);
                d.TrackCard = EditorFields.IdDropdown("Track Card", d.TrackCard, cardIds, "req_card");

                GUILayout.Space(4);
                var reqIds = GetAllBaseIds() ?? new List<string>();
                d.SpriteSource = EditorFields.IdDropdown("Sprite Source", d.SpriteSource, reqIds, "req_sprsrc", pickerMode: EntityPicker.Mode.Requirement);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════

        public static string BuildRequirementDescription(RequirementDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{d.RequirementName}</b>");
            if (!string.IsNullOrEmpty(d.Description))
                sb.Append($"\n{d.Description}");

            var flags = new List<string>();
            if (d.AssignToPlayerAtBegin) flags.Add("AssignAtBegin");
            if (d.RequirementTrack) flags.Add("Track");
            if (d.ItemTrack) flags.Add("ItemTrack");
            if (flags.Count > 0)
                sb.Append($"\n<color=#88ccff>{string.Join(" | ", flags)}</color>");

            if (d.RequirementZoneFinishTrack != Enums.Zone.None)
                sb.Append($"\nZone: {d.RequirementZoneFinishTrack}");
            if (d.RequirementZoneFinishTrackAlternate != Enums.Zone.None)
                sb.Append($"\nZone Alt: {d.RequirementZoneFinishTrackAlternate}");
            if (!string.IsNullOrEmpty(d.TrackCard))
                sb.Append($"\nCard: {d.TrackCard}");
            return sb.ToString();
        }
    }
}
