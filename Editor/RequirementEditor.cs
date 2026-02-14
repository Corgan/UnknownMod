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
    /// IMGUI panel for editing event requirement definitions at the mod-project level.
    /// Supports creating new requirements and overriding base-game ones.
    /// </summary>
    public class RequirementEditor
    {
        private readonly ModEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secTracking = false;
        private bool _secZone = false;
        private bool _secCard = false;

        public RequirementEditor(ModEditor parent) => _parent = parent;

        public string SelectedRequirementId { get; set; }

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null)
            {
                GUILayout.Label("<color=#888>No mod project active. Open the Mods tab first.</color>",
                    EditorStyles.RichLabel);
                return;
            }

            DrawModProjectPanel(proj);
        }

        /// <summary>Returns true if a change was made that needs hot-reload.</summary>
        public bool HandleChanges()
        {
            return GUI.changed && !string.IsNullOrEmpty(SelectedRequirementId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Requirements.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.RequirementPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedRequirementId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.Requirements.TryGetValue(id, out var r))
                        name = r.RequirementName;
                    else if (proj.RequirementPatches.TryGetValue(id, out var rp))
                        name = rp.RequirementName;
                    return $"{badge} {id}  {name}";
                },
                "req_sel");
            if (sel != SelectedRequirementId)
                SelectedRequirementId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_req";
                int suffix = 1;
                while (proj.Requirements.ContainsKey(newId) || proj.RequirementPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_req{suffix++}";
                var def = new RequirementDef
                {
                    Id = newId,
                    RequirementName = "New Requirement",
                };
                proj.Requirements[newId] = def;
                SelectedRequirementId = newId;
                ModProjectLoader.SaveEntity(proj, "requirements", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(SelectedRequirementId))
            {
                bool isNew = proj.Requirements.ContainsKey(SelectedRequirementId);
                bool isOvr = proj.RequirementPatches.ContainsKey(SelectedRequirementId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Requirements.Remove(SelectedRequirementId);
                        ModProjectLoader.DeleteEntity(proj, "requirements", SelectedRequirementId, false);
                        SelectedRequirementId = allIds.FirstOrDefault(k => k != SelectedRequirementId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.RequirementPatches.Remove(SelectedRequirementId);
                        ModProjectLoader.DeleteEntity(proj, "requirements", SelectedRequirementId, true);
                        SelectedRequirementId = allIds.FirstOrDefault(k => k != SelectedRequirementId);
                        proj.IsDirty = true;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Override browser ─────────────────────────────────
            if (_showOverrideBrowser)
                DrawOverrideBrowser(proj);

            EditorStyles.Separator();

            // ── Resolve selected def ─────────────────────────────
            RequirementDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedRequirementId))
            {
                if (proj.Requirements.TryGetValue(SelectedRequirementId, out d))
                    isPatch = false;
                else if (proj.RequirementPatches.TryGetValue(SelectedRequirementId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a requirement above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "requirements", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game requirements to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allIds = DataHelper.GetAllEventRequirementIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.RequirementPatches.ContainsKey(id) || proj.Requirements.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetEventRequirement(id);
                    var def = existing != null ? DataHelper.SnapshotRequirement(existing) : new RequirementDef { Id = id };
                    def.Id = id;
                    proj.RequirementPatches[id] = def;
                    SelectedRequirementId = id;
                    ModProjectLoader.SaveEntity(proj, "requirements", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(RequirementDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
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
                d.Description = EditorFields.TextArea("Description", d.Description);
            }

            // ── Tracking Flags ───────────────────────────────────
            if (EditorFields.Section("Tracking", ref _secTracking))
            {
                d.AssignToPlayerAtBegin = EditorFields.Toggle("Assign At Begin", d.AssignToPlayerAtBegin);
                d.RequirementTrack = EditorFields.Toggle("Requirement Track", d.RequirementTrack);
                d.ItemTrack = EditorFields.Toggle("Item Track", d.ItemTrack);
            }

            // ── Zone Tracking ────────────────────────────────────
            if (EditorFields.Section("Zone Tracking", ref _secZone))
            {
                d.RequirementZoneFinishTrack = EditorFields.EnumField("Finish Zone", d.RequirementZoneFinishTrack, "req_zone");
                d.RequirementZoneFinishTrackAlternate = EditorFields.EnumField("Alt Final Act Zone", d.RequirementZoneFinishTrackAlternate, "req_zone_alt");
            }

            // ── Card Reference ───────────────────────────────────
            if (EditorFields.Section("Track Card", ref _secCard))
            {
                var cardIds = BuildCardIdList(proj);
                d.TrackCard = EditorFields.IdDropdown("Track Card", d.TrackCard, cardIds, "req_card");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static List<string> BuildCardIdList(ModProject proj)
        {
            var ids = new List<string>();
            ids.AddRange(proj.Cards.Keys.OrderBy(k => k));
            ids.AddRange(proj.CardPatches.Keys.OrderBy(k => k));
            ids.AddRange(DataHelper.GetAllCardIds());
            return ids.Distinct().ToList();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildRequirementDescription(RequirementDef d)
        {
            var sb = new StringBuilder();

            sb.Append($"<b>{d.RequirementName}</b>");
            if (!string.IsNullOrEmpty(d.Id))
                sb.Append($"  <color=#888>({d.Id})</color>");
            if (!string.IsNullOrEmpty(d.Description))
                sb.Append($"\n<color=#aaa>{d.Description}</color>");

            var flags = new List<string>();
            if (d.AssignToPlayerAtBegin) flags.Add("AssignAtBegin");
            if (d.RequirementTrack) flags.Add("ReqTrack");
            if (d.ItemTrack) flags.Add("ItemTrack");
            if (flags.Count > 0)
                sb.Append($"\n<color=#88ccff>{string.Join(", ", flags)}</color>");

            if (d.RequirementZoneFinishTrack != Enums.Zone.None)
                sb.Append($"\n<color=#44cc44>Finish Zone: {d.RequirementZoneFinishTrack}</color>");
            if (d.RequirementZoneFinishTrackAlternate != Enums.Zone.None)
                sb.Append($"\n<color=#44cc44>Alt Zone: {d.RequirementZoneFinishTrackAlternate}</color>");

            if (!string.IsNullOrEmpty(d.TrackCard))
                sb.Append($"\n<color=#dd88ff>Card: {d.TrackCard}</color>");

            return sb.ToString();
        }
    }
}
