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
    /// IMGUI panel for editing perk node definitions at the mod-project level.
    /// Supports creating new perk nodes and overriding base-game ones.
    /// </summary>
    public class PerkNodeEditor
    {
        private readonly ModEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secLayout = false;
        private bool _secFlags = false;
        private bool _secRefs = false;
        private bool _secConnected = false;

        public PerkNodeEditor(ModEditor parent) => _parent = parent;

        public string SelectedPerkNodeId { get; set; }

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
            return GUI.changed && !string.IsNullOrEmpty(SelectedPerkNodeId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.PerkNodes.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.PerkNodePatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedPerkNodeId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    return $"{badge} {id}";
                },
                "perknode_sel");
            if (sel != SelectedPerkNodeId)
                SelectedPerkNodeId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_perknode";
                int suffix = 1;
                while (proj.PerkNodes.ContainsKey(newId) || proj.PerkNodePatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_perknode{suffix++}";
                var def = new PerkNodeDef { Id = newId };
                proj.PerkNodes[newId] = def;
                SelectedPerkNodeId = newId;
                ModProjectLoader.SaveEntity(proj, "perknodes", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(SelectedPerkNodeId))
            {
                bool isNew = proj.PerkNodes.ContainsKey(SelectedPerkNodeId);
                bool isOvr = proj.PerkNodePatches.ContainsKey(SelectedPerkNodeId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.PerkNodes.Remove(SelectedPerkNodeId);
                        ModProjectLoader.DeleteEntity(proj, "perknodes", SelectedPerkNodeId, false);
                        SelectedPerkNodeId = allIds.FirstOrDefault(k => k != SelectedPerkNodeId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.PerkNodePatches.Remove(SelectedPerkNodeId);
                        ModProjectLoader.DeleteEntity(proj, "perknodes", SelectedPerkNodeId, true);
                        SelectedPerkNodeId = allIds.FirstOrDefault(k => k != SelectedPerkNodeId);
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
            PerkNodeDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedPerkNodeId))
            {
                if (proj.PerkNodes.TryGetValue(SelectedPerkNodeId, out d))
                    isPatch = false;
                else if (proj.PerkNodePatches.TryGetValue(SelectedPerkNodeId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a perk node above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "perknodes", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game perk nodes to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allIds = DataHelper.GetAllPerkNodeIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.PerkNodePatches.ContainsKey(id) || proj.PerkNodes.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetPerkNode(id);
                    var def = existing != null ? DataHelper.SnapshotPerkNode(existing) : new PerkNodeDef { Id = id };
                    def.Id = id;
                    proj.PerkNodePatches[id] = def;
                    SelectedPerkNodeId = id;
                    ModProjectLoader.SaveEntity(proj, "perknodes", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(PerkNodeDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
            {
                string desc = BuildPerkNodeDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.Type = EditorFields.IntField("Type", d.Type);
            }

            // ── Layout ───────────────────────────────────────────
            if (EditorFields.Section("Layout", ref _secLayout))
            {
                d.Column = EditorFields.IntField("Column", d.Column);
                d.Row = EditorFields.IntField("Row", d.Row);
            }

            // ── Flags ────────────────────────────────────────────
            if (EditorFields.Section("Flags", ref _secFlags))
            {
                d.LockedInTown = EditorFields.Toggle("Locked In Town", d.LockedInTown);
                d.NotStack = EditorFields.Toggle("Not Stack", d.NotStack);
                d.Cost = EditorFields.EnumField("Cost", d.Cost, "perknode_cost");
            }

            // ── References ───────────────────────────────────────
            if (EditorFields.Section("References", ref _secRefs))
            {
                var perkIds = BuildPerkIdList(proj);
                d.Perk = EditorFields.IdDropdown("Perk", d.Perk, perkIds, "perknode_perk");

                var nodeIds = BuildPerkNodeIdList(proj);
                d.PerkRequired = EditorFields.IdDropdown("Required Node", d.PerkRequired, nodeIds, "perknode_req");
            }

            // ── Connected Nodes ──────────────────────────────────
            if (EditorFields.Section("Connected Nodes", ref _secConnected))
            {
                var nodeIds = BuildPerkNodeIdList(proj);

                if (d.PerksConnected == null)
                    d.PerksConnected = new List<string>();

                for (int i = 0; i < d.PerksConnected.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.PerksConnected[i] = EditorFields.IdDropdown($"#{i}", d.PerksConnected[i], nodeIds, $"perknode_conn{i}");
                    if (GUILayout.Button("X", EditorStyles.DangerButton, GUILayout.Width(22)))
                    {
                        d.PerksConnected.RemoveAt(i);
                        i--;
                    }
                    GUILayout.EndHorizontal();
                }

                if (GUILayout.Button("+ Add Connection", EditorStyles.MiniButton, GUILayout.Width(120)))
                    d.PerksConnected.Add("");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static List<string> BuildPerkIdList(ModProject proj)
        {
            var ids = new List<string>();
            ids.AddRange(proj.Perks.Keys.OrderBy(k => k));
            ids.AddRange(proj.PerkPatches.Keys.OrderBy(k => k));
            ids.AddRange(DataHelper.GetAllPerkIds());
            return ids.Distinct().ToList();
        }

        private static List<string> BuildPerkNodeIdList(ModProject proj)
        {
            var ids = new List<string>();
            ids.AddRange(proj.PerkNodes.Keys.OrderBy(k => k));
            ids.AddRange(proj.PerkNodePatches.Keys.OrderBy(k => k));
            ids.AddRange(DataHelper.GetAllPerkNodeIds());
            return ids.Distinct().ToList();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildPerkNodeDescription(PerkNodeDef d)
        {
            var sb = new StringBuilder();

            sb.Append($"<b>Node: {d.Id}</b>");
            sb.Append($"\n<color=#888>Type {d.Type}  Col {d.Column}  Row {d.Row}  Cost: {d.Cost}</color>");

            var flags = new List<string>();
            if (d.LockedInTown) flags.Add("LockedInTown");
            if (d.NotStack) flags.Add("NotStack");
            if (flags.Count > 0)
                sb.Append($"\n<color=#ff8888>{string.Join(", ", flags)}</color>");

            if (!string.IsNullOrEmpty(d.Perk))
                sb.Append($"\n<color=#44cc44>Perk: {d.Perk}</color>");

            if (!string.IsNullOrEmpty(d.PerkRequired))
                sb.Append($"\n<color=#88ccff>Requires: {d.PerkRequired}</color>");

            if (d.PerksConnected != null && d.PerksConnected.Count > 0)
            {
                var valid = d.PerksConnected.Where(c => !string.IsNullOrEmpty(c)).ToList();
                if (valid.Count > 0)
                    sb.Append($"\n<color=#dd88ff>Connected: {string.Join(", ", valid)}</color>");
            }

            return sb.ToString();
        }
    }
}
