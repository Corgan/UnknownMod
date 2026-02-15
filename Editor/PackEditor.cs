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
    /// IMGUI panel for editing pack definitions (card reward packs).
    /// Supports creating new packs and overriding base-game ones.
    /// </summary>
    public class PackEditor
    {
        private readonly ModEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secCards = true;
        private bool _secSpecialCards = true;
        private bool _secPerks;

        public PackEditor(ModEditor parent) => _parent = parent;

        public string SelectedPackId { get; set; }

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

        public bool HandleChanges()
        {
            return GUI.changed && !string.IsNullOrEmpty(SelectedPackId);
        }

        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Packs.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.PackPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedPackId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    return $"{badge} {id}";
                },
                "pack_sel");
            if (sel != SelectedPackId)
                SelectedPackId = sel;

            // ── Action bar ───────────────────────────────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = "new_pack";
                int n = 1;
                while (proj.Packs.ContainsKey(newId) || proj.PackPatches.ContainsKey(newId))
                    newId = $"new_pack_{n++}";
                var def = new PackDef { PackId = newId };
                proj.Packs[newId] = def;
                SelectedPackId = newId;
                ModProjectLoader.SaveEntity(proj, "packs", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            if (!string.IsNullOrEmpty(SelectedPackId))
            {
                bool isNew = proj.Packs.ContainsKey(SelectedPackId);
                bool isOvr = proj.PackPatches.ContainsKey(SelectedPackId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Packs.Remove(SelectedPackId);
                        ModProjectLoader.DeleteEntity(proj, "packs", SelectedPackId, false);
                        SelectedPackId = allIds.FirstOrDefault(k => k != SelectedPackId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.PackPatches.Remove(SelectedPackId);
                        ModProjectLoader.DeleteEntity(proj, "packs", SelectedPackId, true);
                        SelectedPackId = allIds.FirstOrDefault(k => k != SelectedPackId);
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
            PackDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedPackId))
            {
                if (proj.Packs.TryGetValue(SelectedPackId, out d))
                    isPatch = false;
                else if (proj.PackPatches.TryGetValue(SelectedPackId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a pack above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            DrawAllSections(d, proj);

            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "packs", d.PackId, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game packs to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allPackIds = DataHelper.GetAllPackIds();
            int shown = 0;
            foreach (var id in allPackIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.PackPatches.ContainsKey(id) || proj.Packs.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetPackData(id);
                    var def = existing != null ? DataHelper.SnapshotPack(existing) : new PackDef { PackId = id };
                    proj.PackPatches[id] = def;
                    SelectedPackId = id;
                    ModProjectLoader.SaveEntity(proj, "packs", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(PackDef d, ModProject proj)
        {
            // Preview
            if (EditorFields.Section("Preview", ref _secPreview))
            {
                var sb = new StringBuilder();
                sb.Append($"<b>{d.PackId}</b>");
                if (!string.IsNullOrEmpty(d.PackName)) sb.Append($"  \"{d.PackName}\"");
                sb.Append($"\nClass: {d.PackClass}");
                if (!string.IsNullOrEmpty(d.RequiredClassId))
                    sb.Append($"  |  Required: {d.RequiredClassId}");
                int cardCount = d.CardIds?.Count(c => !string.IsNullOrEmpty(c)) ?? 0;
                int specCount = d.SpecialCardIds?.Count(c => !string.IsNullOrEmpty(c)) ?? 0;
                sb.Append($"\nCards: {cardCount}  |  Specials: {specCount}  |  Perks: {d.PerkIds?.Count ?? 0}");

                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{sb}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // Identity
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.PackId = EditorFields.TextField("Pack ID", d.PackId);
                d.PackName = EditorFields.TextField("Pack Name", d.PackName);
                d.PackClass = EditorFields.EnumField("Pack Class", d.PackClass);
                d.RequiredClassId = EditorFields.TextField("Required Class", d.RequiredClassId);
            }

            // Cards
            if (EditorFields.Section("Cards (0-5)", ref _secCards))
            {
                if (d.CardIds == null) d.CardIds = new List<string>();
                while (d.CardIds.Count < 6) d.CardIds.Add("");
                for (int i = 0; i < 6; i++)
                    d.CardIds[i] = EditorFields.TextField($"Card {i}", d.CardIds[i]);
            }

            // Special Cards
            if (EditorFields.Section("Special Cards (0-1)", ref _secSpecialCards))
            {
                if (d.SpecialCardIds == null) d.SpecialCardIds = new List<string>();
                while (d.SpecialCardIds.Count < 2) d.SpecialCardIds.Add("");
                for (int i = 0; i < 2; i++)
                    d.SpecialCardIds[i] = EditorFields.TextField($"Special {i}", d.SpecialCardIds[i]);
            }

            // Perks
            if (EditorFields.Section("Perks", ref _secPerks))
            {
                if (d.PerkIds == null) d.PerkIds = new List<string>();
                for (int i = 0; i < d.PerkIds.Count; i++)
                    d.PerkIds[i] = EditorFields.TextField($"Perk {i}", d.PerkIds[i]);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Perk", EditorStyles.MiniButton, GUILayout.Width(70)))
                    d.PerkIds.Add("");
                if (d.PerkIds.Count > 0 && GUILayout.Button("- Remove Last", EditorStyles.MiniButton, GUILayout.Width(100)))
                    d.PerkIds.RemoveAt(d.PerkIds.Count - 1);
                GUILayout.EndHorizontal();
            }
        }
    }
}
