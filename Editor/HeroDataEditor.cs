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
    /// IMGUI panel for editing HeroData definitions (playable hero character data).
    /// Supports creating new hero data and overriding base-game entries.
    /// </summary>
    public class HeroDataEditor
    {
        private readonly ModEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secClass = true;

        public HeroDataEditor(ModEditor parent) => _parent = parent;

        public string SelectedHeroDataId { get; set; }

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
            return GUI.changed && !string.IsNullOrEmpty(SelectedHeroDataId);
        }

        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.HeroDataEntries.Keys.OrderBy(k => k))
            { allIds.Add(id); badges[id] = "[NEW]"; }
            foreach (var id in proj.HeroDataPatches.Keys.OrderBy(k => k))
            { if (!allIds.Contains(id)) allIds.Add(id); badges[id] = "[OVR]"; }

            string sel = EditorFields.EntitySelector(SelectedHeroDataId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.HeroDataEntries.TryGetValue(id, out var h)) name = h.HeroName;
                    else if (proj.HeroDataPatches.TryGetValue(id, out var hp)) name = hp.HeroName;
                    return $"{badge} {id}" + (!string.IsNullOrEmpty(name) ? $"  \"{name}\"" : "");
                },
                "hd_sel");
            if (sel != SelectedHeroDataId) SelectedHeroDataId = sel;

            // Action bar
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = "newhero";
                int n = 1;
                while (proj.HeroDataEntries.ContainsKey(newId) || proj.HeroDataPatches.ContainsKey(newId))
                    newId = $"newhero{n++}";
                var def = new HeroDataDef { Id = newId, HeroName = "New Hero" };
                proj.HeroDataEntries[newId] = def;
                SelectedHeroDataId = newId;
                ModProjectLoader.SaveEntity(proj, "herodata", newId, def);
                proj.IsDirty = true;
            }
            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            if (!string.IsNullOrEmpty(SelectedHeroDataId))
            {
                bool isNew = proj.HeroDataEntries.ContainsKey(SelectedHeroDataId);
                bool isOvr = proj.HeroDataPatches.ContainsKey(SelectedHeroDataId);
                if (isNew && GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                {
                    proj.HeroDataEntries.Remove(SelectedHeroDataId);
                    ModProjectLoader.DeleteEntity(proj, "herodata", SelectedHeroDataId, false);
                    SelectedHeroDataId = allIds.FirstOrDefault(k => k != SelectedHeroDataId);
                    proj.IsDirty = true;
                }
                else if (isOvr && GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                {
                    proj.HeroDataPatches.Remove(SelectedHeroDataId);
                    ModProjectLoader.DeleteEntity(proj, "herodata", SelectedHeroDataId, true);
                    SelectedHeroDataId = allIds.FirstOrDefault(k => k != SelectedHeroDataId);
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndHorizontal();

            if (_showOverrideBrowser)
                DrawOverrideBrowser(proj);

            EditorStyles.Separator();

            HeroDataDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedHeroDataId))
            {
                if (proj.HeroDataEntries.TryGetValue(SelectedHeroDataId, out d)) isPatch = false;
                else if (proj.HeroDataPatches.TryGetValue(SelectedHeroDataId, out d)) isPatch = true;
            }
            if (d == null)
            {
                GUILayout.Label("<i>Select a hero data entry above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            DrawAllSections(d);

            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "herodata", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game hero data to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allIds = DataHelper.GetAllHeroDataIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.HeroDataPatches.ContainsKey(id) || proj.HeroDataEntries.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetHeroData(id);
                    var def = existing != null ? DataHelper.SnapshotHeroData(existing) : new HeroDataDef { Id = id };
                    proj.HeroDataPatches[id] = def;
                    SelectedHeroDataId = id;
                    ModProjectLoader.SaveEntity(proj, "herodata", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(HeroDataDef d)
        {
            if (EditorFields.Section("Preview", ref _secPreview))
            {
                var sb = new StringBuilder();
                sb.Append($"<b>{d.Id}</b>");
                if (!string.IsNullOrEmpty(d.HeroName))
                    sb.Append($"  \"{d.HeroName}\"");
                sb.Append($"\nClass: {d.HeroClass}");
                if (!string.IsNullOrEmpty(d.HeroSubClassId))
                    sb.Append($"  |  SubClass: {d.HeroSubClassId}");

                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{sb}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.HeroName = EditorFields.TextField("Hero Name", d.HeroName);
            }

            if (EditorFields.Section("Class", ref _secClass))
            {
                d.HeroClass = EditorFields.EnumField("Hero Class", d.HeroClass);
                d.HeroSubClassId = EditorFields.TextField("SubClass ID", d.HeroSubClassId);
            }
        }
    }
}
