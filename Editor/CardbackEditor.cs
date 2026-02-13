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
    /// IMGUI panel for editing cardback definitions at the mod-project level.
    /// Supports creating new cardbacks and overriding base-game ones.
    /// </summary>
    public class CardbackEditor
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
        private bool _secRequirements = false;
        private bool _secVisual = false;

        public CardbackEditor(ZoneEditor parent) => _parent = parent;

        public string SelectedCardbackId { get; set; }

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
            return GUI.changed && !string.IsNullOrEmpty(SelectedCardbackId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Cardbacks.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.CardbackPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedCardbackId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.Cardbacks.TryGetValue(id, out var cb))
                        name = cb.CardbackName;
                    else if (proj.CardbackPatches.TryGetValue(id, out var cbp))
                        name = cbp.CardbackName;
                    return $"{badge} {id}  {name}";
                },
                "cb_sel");
            if (sel != SelectedCardbackId)
                SelectedCardbackId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_cardback";
                int suffix = 1;
                while (proj.Cardbacks.ContainsKey(newId) || proj.CardbackPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_cardback{suffix++}";
                var def = new CardbackDef
                {
                    Id = newId,
                    CardbackName = "New Cardback",
                };
                proj.Cardbacks[newId] = def;
                SelectedCardbackId = newId;
                ModProjectLoader.SaveEntity(proj, "cardbacks", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(SelectedCardbackId))
            {
                bool isNew = proj.Cardbacks.ContainsKey(SelectedCardbackId);
                bool isOvr = proj.CardbackPatches.ContainsKey(SelectedCardbackId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Cardbacks.Remove(SelectedCardbackId);
                        ModProjectLoader.DeleteEntity(proj, "cardbacks", SelectedCardbackId, false);
                        SelectedCardbackId = allIds.FirstOrDefault(k => k != SelectedCardbackId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.CardbackPatches.Remove(SelectedCardbackId);
                        ModProjectLoader.DeleteEntity(proj, "cardbacks", SelectedCardbackId, true);
                        SelectedCardbackId = allIds.FirstOrDefault(k => k != SelectedCardbackId);
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
            CardbackDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedCardbackId))
            {
                if (proj.Cardbacks.TryGetValue(SelectedCardbackId, out d))
                    isPatch = false;
                else if (proj.CardbackPatches.TryGetValue(SelectedCardbackId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a cardback above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "cardbacks", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game cardbacks to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allIds = DataHelper.GetAllCardbackIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.CardbackPatches.ContainsKey(id) || proj.Cardbacks.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetCardback(id);
                    var def = existing != null ? DataHelper.SnapshotCardback(existing) : new CardbackDef { Id = id };
                    def.Id = id;
                    proj.CardbackPatches[id] = def;
                    SelectedCardbackId = id;
                    ModProjectLoader.SaveEntity(proj, "cardbacks", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(CardbackDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
            {
                string desc = BuildCardbackDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.CardbackName = EditorFields.TextField("Name", d.CardbackName);
                d.CardbackTextId = EditorFields.TextField("Text ID", d.CardbackTextId);

                var subclassIds = DataHelper.GetAllSubClassIds();
                d.CardbackSubclass = EditorFields.IdDropdown("Subclass", d.CardbackSubclass, subclassIds, "cb_subclass");
            }

            // ── Config ───────────────────────────────────────────
            if (EditorFields.Section("Config", ref _secConfig))
            {
                d.CardbackOrder = EditorFields.IntField("Order", d.CardbackOrder);
                d.BaseCardback = EditorFields.Toggle("Base Cardback", d.BaseCardback);
                d.Locked = EditorFields.Toggle("Locked", d.Locked);
                d.ShowIfLocked = EditorFields.Toggle("Show If Locked", d.ShowIfLocked);
            }

            // ── Requirements ─────────────────────────────────────
            if (EditorFields.Section("Requirements", ref _secRequirements))
            {
                d.RankLevel = EditorFields.IntField("Rank Level", d.RankLevel);
                d.Sku = EditorFields.TextField("SKU (DLC)", d.Sku);
                d.SteamStat = EditorFields.TextField("Steam Stat", d.SteamStat);
                d.AdventureLevel = EditorFields.IntField("Adventure Level", d.AdventureLevel);
                d.ObeliskLevel = EditorFields.IntField("Obelisk Level", d.ObeliskLevel);
                d.SingularityLevel = EditorFields.IntField("Singularity Level", d.SingularityLevel);
                d.PdxAccountRequired = EditorFields.Toggle("PDX Account Required", d.PdxAccountRequired);
            }

            // ── Visual ───────────────────────────────────────────
            if (EditorFields.Section("Visual / Sprite", ref _secVisual))
            {
                var cbIds = DataHelper.GetAllCardbackIds();
                d.SpriteSource = EditorFields.IdDropdown("Sprite Source", d.SpriteSource, cbIds, "cb_sprite");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildCardbackDescription(CardbackDef d)
        {
            var sb = new StringBuilder();

            sb.Append($"<b>{d.CardbackName}</b>");
            if (!string.IsNullOrEmpty(d.Id))
                sb.Append($"  <color=#888>({d.Id})</color>");

            sb.Append($"\n<color=#aaa>Order: {d.CardbackOrder}</color>");

            var flags = new List<string>();
            if (d.BaseCardback) flags.Add("Base");
            if (d.Locked) flags.Add("Locked");
            if (d.ShowIfLocked) flags.Add("ShowIfLocked");
            if (d.PdxAccountRequired) flags.Add("PDX");
            if (flags.Count > 0)
                sb.Append($"\n<color=#88ccff>{string.Join(", ", flags)}</color>");

            var reqs = new List<string>();
            if (d.RankLevel > 0) reqs.Add($"Rank≥{d.RankLevel}");
            if (d.AdventureLevel > 0) reqs.Add($"Adv≥{d.AdventureLevel}");
            if (d.ObeliskLevel > 0) reqs.Add($"Obelisk≥{d.ObeliskLevel}");
            if (d.SingularityLevel > 0) reqs.Add($"Sing≥{d.SingularityLevel}");
            if (!string.IsNullOrEmpty(d.Sku)) reqs.Add($"DLC:{d.Sku}");
            if (!string.IsNullOrEmpty(d.SteamStat)) reqs.Add($"Stat:{d.SteamStat}");
            if (reqs.Count > 0)
                sb.Append($"\n<color=#dd88ff>{string.Join(", ", reqs)}</color>");

            if (!string.IsNullOrEmpty(d.CardbackSubclass))
                sb.Append($"\n<color=#44cc44>Subclass: {d.CardbackSubclass}</color>");

            return sb.ToString();
        }
    }
}
