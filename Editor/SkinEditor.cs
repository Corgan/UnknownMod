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
    /// IMGUI panel for editing skin definitions at the mod-project level.
    /// Supports creating new skins and overriding base-game ones.
    /// </summary>
    public class SkinEditor
    {
        private readonly ZoneEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secConfig = false;
        private bool _secVisual = false;
        private bool _secScreen = false;

        public SkinEditor(ZoneEditor parent) => _parent = parent;

        public string SelectedSkinId { get; set; }

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
            return GUI.changed && !string.IsNullOrEmpty(SelectedSkinId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Skins.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.SkinPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedSkinId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.Skins.TryGetValue(id, out var s))
                        name = s.SkinName;
                    else if (proj.SkinPatches.TryGetValue(id, out var sp))
                        name = sp.SkinName;
                    return $"{badge} {id}  {name}";
                },
                "skin_sel");
            if (sel != SelectedSkinId)
                SelectedSkinId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_skin";
                int suffix = 1;
                while (proj.Skins.ContainsKey(newId) || proj.SkinPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_skin{suffix++}";
                var def = new SkinDef
                {
                    Id = newId,
                    SkinName = "New Skin",
                };
                proj.Skins[newId] = def;
                SelectedSkinId = newId;
                ModProjectLoader.SaveEntity(proj, "skins", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(SelectedSkinId))
            {
                bool isNew = proj.Skins.ContainsKey(SelectedSkinId);
                bool isOvr = proj.SkinPatches.ContainsKey(SelectedSkinId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Skins.Remove(SelectedSkinId);
                        ModProjectLoader.DeleteEntity(proj, "skins", SelectedSkinId, false);
                        SelectedSkinId = allIds.FirstOrDefault(k => k != SelectedSkinId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.SkinPatches.Remove(SelectedSkinId);
                        ModProjectLoader.DeleteEntity(proj, "skins", SelectedSkinId, true);
                        SelectedSkinId = allIds.FirstOrDefault(k => k != SelectedSkinId);
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
            SkinDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedSkinId))
            {
                if (proj.Skins.TryGetValue(SelectedSkinId, out d))
                    isPatch = false;
                else if (proj.SkinPatches.TryGetValue(SelectedSkinId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a skin above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "skins", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game skins to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allIds = DataHelper.GetAllSkinIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.SkinPatches.ContainsKey(id) || proj.Skins.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetSkin(id);
                    var def = existing != null ? DataHelper.SnapshotSkin(existing) : new SkinDef { Id = id };
                    def.Id = id;
                    proj.SkinPatches[id] = def;
                    SelectedSkinId = id;
                    ModProjectLoader.SaveEntity(proj, "skins", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(SkinDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Preview", ref _secPreview))
            {
                string desc = BuildSkinDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.SkinName = EditorFields.TextField("Skin Name", d.SkinName);
                var scIds = DataHelper.GetAllSubClassIds();
                d.SkinSubclass = EditorFields.IdDropdown("Subclass", d.SkinSubclass, scIds, "skin_sc");
            }

            // ── Config ───────────────────────────────────────────
            if (EditorFields.Section("Config", ref _secConfig))
            {
                d.BaseSkin = EditorFields.Toggle("Base Skin", d.BaseSkin);
                d.SkinOrder = EditorFields.IntField("Order", d.SkinOrder);
                d.PerkLevel = EditorFields.IntField("Required Perk Level", d.PerkLevel);
                d.Sku = EditorFields.TextField("SKU (DLC)", d.Sku);
                d.SteamStat = EditorFields.TextField("Steam Stat", d.SteamStat);
                d.SkinTextId = EditorFields.TextField("Text ID", d.SkinTextId);
            }

            // ── Visuals ──────────────────────────────────────────
            if (EditorFields.Section("Visual Source", ref _secVisual))
            {
                var skinIds = DataHelper.GetAllSkinIds();
                d.SpriteSource = EditorFields.IdDropdown("Copy From Skin", d.SpriteSource, skinIds, "skin_src");
                GUILayout.Label("<color=#666>Copies the prefab and 4 sprites from the source skin at build time.</color>",
                    EditorStyles.RichLabel);
            }

            // ── Selection Screen ─────────────────────────────────
            if (EditorFields.Section("Selection Screen", ref _secScreen))
            {
                d.HeroSelectionScreenScale = EditorFields.FloatField("Scale", d.HeroSelectionScreenScale);
                d.HeroSelectionScreenOffsetX = EditorFields.FloatField("Offset X", d.HeroSelectionScreenOffsetX);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildSkinDescription(SkinDef d)
        {
            var sb = new StringBuilder();

            sb.Append($"<b>{d.SkinName}</b>");
            if (!string.IsNullOrEmpty(d.SkinSubclass))
                sb.Append($"  <color=#aaa>({d.SkinSubclass})</color>");

            var flags = new List<string>();
            if (d.BaseSkin) flags.Add("Base");
            if (d.PerkLevel > 0) flags.Add($"PerkLv:{d.PerkLevel}");
            if (!string.IsNullOrEmpty(d.Sku)) flags.Add($"DLC:{d.Sku}");
            if (!string.IsNullOrEmpty(d.SteamStat)) flags.Add($"Stat:{d.SteamStat}");
            if (d.SkinOrder != 0) flags.Add($"Order:{d.SkinOrder}");
            if (flags.Count > 0)
                sb.Append($"\n<color=#88ccff>{string.Join(" | ", flags)}</color>");

            if (!string.IsNullOrEmpty(d.SpriteSource))
                sb.Append($"\n<color=#44cc44>Sprites from: {d.SpriteSource}</color>");

            return sb.ToString();
        }
    }
}
