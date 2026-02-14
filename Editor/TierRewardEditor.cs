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
    /// IMGUI panel for editing tier reward definitions at the mod-project level.
    /// Supports creating new tier rewards and overriding base-game ones.
    /// TierRewards are keyed by tier number (stored as string ID "tier_N").
    /// </summary>
    public class TierRewardEditor
    {
        private readonly ModEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secRewards = true;

        public TierRewardEditor(ModEditor parent) => _parent = parent;

        public string SelectedTierRewardId { get; set; }

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
            return GUI.changed && !string.IsNullOrEmpty(SelectedTierRewardId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.TierRewards.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.TierRewardPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedTierRewardId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    int tier = 0;
                    if (proj.TierRewards.TryGetValue(id, out var tr))
                        tier = tr.Tier;
                    else if (proj.TierRewardPatches.TryGetValue(id, out var trp))
                        tier = trp.Tier;
                    return $"{badge} {id}  (Tier {tier})";
                },
                "tr_sel");
            if (sel != SelectedTierRewardId)
                SelectedTierRewardId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                // Find next available tier number
                int nextTier = 100;
                while (proj.TierRewards.ContainsKey($"tier_{nextTier}") ||
                       proj.TierRewardPatches.ContainsKey($"tier_{nextTier}"))
                    nextTier++;
                string newId = $"tier_{nextTier}";
                var def = new TierRewardDef
                {
                    Id = newId,
                    Tier = nextTier,
                };
                proj.TierRewards[newId] = def;
                SelectedTierRewardId = newId;
                ModProjectLoader.SaveEntity(proj, "tierrewards", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(SelectedTierRewardId))
            {
                bool isNew = proj.TierRewards.ContainsKey(SelectedTierRewardId);
                bool isOvr = proj.TierRewardPatches.ContainsKey(SelectedTierRewardId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.TierRewards.Remove(SelectedTierRewardId);
                        ModProjectLoader.DeleteEntity(proj, "tierrewards", SelectedTierRewardId, false);
                        SelectedTierRewardId = allIds.FirstOrDefault(k => k != SelectedTierRewardId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.TierRewardPatches.Remove(SelectedTierRewardId);
                        ModProjectLoader.DeleteEntity(proj, "tierrewards", SelectedTierRewardId, true);
                        SelectedTierRewardId = allIds.FirstOrDefault(k => k != SelectedTierRewardId);
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
            TierRewardDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedTierRewardId))
            {
                if (proj.TierRewards.TryGetValue(SelectedTierRewardId, out d))
                    isPatch = false;
                else if (proj.TierRewardPatches.TryGetValue(SelectedTierRewardId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a tier reward above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "tierrewards", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game tier rewards to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allTiers = DataHelper.GetAllTierRewardTiers();
            int shown = 0;
            foreach (var tier in allTiers)
            {
                if (shown >= 50) break;
                string tierId = $"tier_{tier}";
                if (!string.IsNullOrEmpty(filterLow) && !tierId.Contains(filterLow)) continue;
                if (proj.TierRewardPatches.ContainsKey(tierId) || proj.TierRewards.ContainsKey(tierId)) continue;
                shown++;
                if (GUILayout.Button($"Tier {tier}", EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetTierRewardData(tier);
                    var def = existing != null ? DataHelper.SnapshotTierReward(existing) : new TierRewardDef { Id = tierId, Tier = tier };
                    def.Id = tierId;
                    proj.TierRewardPatches[tierId] = def;
                    SelectedTierRewardId = tierId;
                    ModProjectLoader.SaveEntity(proj, "tierrewards", tierId, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(TierRewardDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
            {
                string desc = BuildTierRewardDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.Tier = EditorFields.IntField("Tier Number", d.Tier);
            }

            // ── Rewards ──────────────────────────────────────────
            if (EditorFields.Section("Rewards", ref _secRewards))
            {
                d.Common = EditorFields.IntField("Common", d.Common);
                d.Uncommon = EditorFields.IntField("Uncommon", d.Uncommon);
                d.Rare = EditorFields.IntField("Rare", d.Rare);
                d.Epic = EditorFields.IntField("Epic", d.Epic);
                d.Mythic = EditorFields.IntField("Mythic", d.Mythic);
                d.Dust = EditorFields.IntField("Dust", d.Dust);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildTierRewardDescription(TierRewardDef d)
        {
            var sb = new StringBuilder();

            sb.Append($"<b>Tier {d.Tier}</b>");
            if (!string.IsNullOrEmpty(d.Id))
                sb.Append($"  <color=#888>({d.Id})</color>");

            var rewards = new List<string>();
            if (d.Common > 0) rewards.Add($"<color=#aaa>Common:{d.Common}</color>");
            if (d.Uncommon > 0) rewards.Add($"<color=#44cc44>Uncommon:{d.Uncommon}</color>");
            if (d.Rare > 0) rewards.Add($"<color=#4488ff>Rare:{d.Rare}</color>");
            if (d.Epic > 0) rewards.Add($"<color=#cc44ff>Epic:{d.Epic}</color>");
            if (d.Mythic > 0) rewards.Add($"<color=#ffaa00>Mythic:{d.Mythic}</color>");
            if (d.Dust > 0) rewards.Add($"<color=#dddd88>Dust:{d.Dust}</color>");

            if (rewards.Count > 0)
                sb.Append($"\n{string.Join("  ", rewards)}");
            else
                sb.Append("\n<color=#888>(no rewards set)</color>");

            return sb.ToString();
        }
    }
}
