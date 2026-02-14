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
    /// IMGUI panel for editing loot tables at the mod-project level.
    /// Supports creating new loot tables and overriding base-game ones.
    /// </summary>
    public class LootEditor
    {
        private readonly ModEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // Collapsible section state
        private bool _secPreview = true;
        private bool _secRarity = true;
        private bool _secItems = true;

        public LootEditor(ModEditor parent) => _parent = parent;

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
            return GUI.changed && !string.IsNullOrEmpty(_parent.SelectedLootId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Loot.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.LootPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                _parent.SelectedLootId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    LootDef lt = null;
                    if (proj.Loot.TryGetValue(id, out lt) || proj.LootPatches.TryGetValue(id, out lt)) { }
                    int count = lt?.Items.Count ?? 0;
                    int gold = lt?.GoldQuantity ?? 0;
                    return $"{badge} {id}  ({count} items, {gold}g)";
                },
                "loot_sel");
            if (sel != _parent.SelectedLootId)
                _parent.SelectedLootId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_loot";
                int suffix = 1;
                while (proj.Loot.ContainsKey(newId) || proj.LootPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_loot{suffix++}";
                var def = new LootDef { Id = newId, NumItems = 1 };
                proj.Loot[newId] = def;
                _parent.SelectedLootId = newId;
                ModProjectLoader.SaveEntity(proj, "loot", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(_parent.SelectedLootId))
            {
                bool isNew = proj.Loot.ContainsKey(_parent.SelectedLootId);
                bool isOvr = proj.LootPatches.ContainsKey(_parent.SelectedLootId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Loot.Remove(_parent.SelectedLootId);
                        ModProjectLoader.DeleteEntity(proj, "loot", _parent.SelectedLootId, false);
                        _parent.SelectedLootId = allIds.FirstOrDefault(k => k != _parent.SelectedLootId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.LootPatches.Remove(_parent.SelectedLootId);
                        ModProjectLoader.DeleteEntity(proj, "loot", _parent.SelectedLootId, true);
                        _parent.SelectedLootId = allIds.FirstOrDefault(k => k != _parent.SelectedLootId);
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
            LootDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(_parent.SelectedLootId))
            {
                if (proj.Loot.TryGetValue(_parent.SelectedLootId, out d))
                    isPatch = false;
                else if (proj.LootPatches.TryGetValue(_parent.SelectedLootId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a loot table above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "loot", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game loot tables to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allLootIds = DataHelper.GetAllLootIds();
            int shown = 0;
            foreach (var id in allLootIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.LootPatches.ContainsKey(id) || proj.Loot.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetLootData(id);
                    var def = existing != null ? DataHelper.SnapshotLoot(existing) : new LootDef { Id = id };
                    def.Id = id;
                    proj.LootPatches[id] = def;
                    _parent.SelectedLootId = id;
                    ModProjectLoader.SaveEntity(proj, "loot", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(LootDef d, ModProject proj)
        {
            // ── Basic fields ─────────────────────────────────────
            d.Id = EditorFields.TextField("ID", d.Id);
            d.NumItems = EditorFields.IntField("Num Items", d.NumItems);
            d.GoldQuantity = EditorFields.IntField("Gold Qty", d.GoldQuantity);
            d.AllowDropOnlyItems = EditorFields.Toggle("Allow Drop-Only", d.AllowDropOnlyItems);

            // ── Live Preview ─────────────────────────────────────
            if (EditorFields.Section("Preview", ref _secPreview))
            {
                string desc = BuildLootDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Rarity Distribution ──────────────────────────────
            if (EditorFields.Section("Rarity Distribution", ref _secRarity))
            {
                d.PercentUncommon = EditorFields.FloatField("% Uncommon", d.PercentUncommon);
                d.PercentRare = EditorFields.FloatField("% Rare", d.PercentRare);
                d.PercentEpic = EditorFields.FloatField("% Epic", d.PercentEpic);
                d.PercentMythic = EditorFields.FloatField("% Mythic", d.PercentMythic);

                // Show computed Common %
                float totalUp = d.PercentUncommon + d.PercentRare + d.PercentEpic + d.PercentMythic;
                float commonPct = Mathf.Max(0f, 100f - totalUp);
                GUILayout.BeginHorizontal();
                GUILayout.Label("% Common", GUILayout.Width(100));
                GUILayout.Label($"<color=#888>{commonPct:F1}%  (computed)</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
            }

            // ── Loot Item Table ──────────────────────────────────
            if (EditorFields.Section($"Loot Items ({d.Items.Count})", ref _secItems))
            {
                // Card dropdown from mod-project + base game
                var cardIds = new List<string>();
                cardIds.AddRange(proj.Cards.Keys.OrderBy(k => k));
                cardIds.AddRange(proj.CardPatches.Keys.OrderBy(k => k));
                cardIds.AddRange(DataHelper.GetAllCardIds());
                cardIds = cardIds.Distinct().ToList();

                for (int i = 0; i < d.Items.Count; i++)
                {
                    var item = d.Items[i];
                    GUILayout.BeginVertical(EditorStyles.CompactBox);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>#{i}</b>", EditorStyles.RichLabel, GUILayout.Width(30));
                    GUILayout.FlexibleSpace();

                    // Move up
                    if (i > 0 && GUILayout.Button("\u2191", EditorStyles.MiniButton, GUILayout.Width(22)))
                    {
                        var tmp = d.Items[i];
                        d.Items[i] = d.Items[i - 1];
                        d.Items[i - 1] = tmp;
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                    // Move down
                    if (i < d.Items.Count - 1 && GUILayout.Button("\u2193", EditorStyles.MiniButton, GUILayout.Width(22)))
                    {
                        var tmp = d.Items[i];
                        d.Items[i] = d.Items[i + 1];
                        d.Items[i + 1] = tmp;
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                    // Delete
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.Items.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                    GUILayout.EndHorizontal();

                    // Card reference
                    item.CardId = EditorFields.IdDropdown("Card ID", item.CardId, cardIds, $"loot_card_{i}");

                    item.Percent = EditorFields.FloatField("Drop %", item.Percent);
                    item.LootType = EditorFields.EnumField("Type Filter", item.LootType, $"loot_type_{i}");
                    item.LootRarity = EditorFields.EnumField("Rarity Filter", item.LootRarity, $"loot_rar_{i}");
                    item.LootMisc = EditorFields.TextField("Misc", item.LootMisc);

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }

                if (GUILayout.Button("+ Add Loot Item", EditorStyles.MiniButton, GUILayout.Width(110)))
                {
                    d.Items.Add(new LootItemDef());
                    GUI.changed = true;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildLootDescription(LootDef d)
        {
            var sb = new StringBuilder();

            sb.Append($"<color=#e8c06a>Loot Table:</color> {d.Id}");
            sb.Append($"\nDraw <color=#ffcc44>{d.NumItems}</color> item(s)");

            if (d.GoldQuantity > 0)
                sb.Append($"\n+<color=#e8c06a>{d.GoldQuantity}</color> Gold");

            if (d.AllowDropOnlyItems)
                sb.Append("\n<color=#888>Drop-only items allowed</color>");

            // Rarity distribution
            float totalUp = d.PercentUncommon + d.PercentRare + d.PercentEpic + d.PercentMythic;
            if (totalUp > 0f)
            {
                sb.Append("\n\n<color=#aaa>Rarity Distribution:</color>");
                float commonPct = Mathf.Max(0f, 100f - totalUp);
                if (commonPct > 0f) sb.Append($"\n  Common: <color=#ccc>{commonPct:F1}%</color>");
                if (d.PercentUncommon > 0f) sb.Append($"\n  Uncommon: <color=#44cc44>{d.PercentUncommon:F1}%</color>");
                if (d.PercentRare > 0f) sb.Append($"\n  Rare: <color=#4488ff>{d.PercentRare:F1}%</color>");
                if (d.PercentEpic > 0f) sb.Append($"\n  Epic: <color=#cc44cc>{d.PercentEpic:F1}%</color>");
                if (d.PercentMythic > 0f) sb.Append($"\n  Mythic: <color=#ffcc44>{d.PercentMythic:F1}%</color>");
            }

            // Items
            if (d.Items.Count > 0)
            {
                sb.Append($"\n\n<color=#aaa>Specific Items ({d.Items.Count}):</color>");
                foreach (var item in d.Items)
                {
                    sb.Append("\n  ");
                    if (!string.IsNullOrEmpty(item.CardId))
                        sb.Append($"<color=#88ccff>{item.CardId}</color>");
                    else
                    {
                        var parts = new List<string>();
                        if (item.LootType != Enums.CardType.None)
                            parts.Add(item.LootType.ToString());
                        if (item.LootRarity != Enums.CardRarity.Common)
                            parts.Add(item.LootRarity.ToString());
                        sb.Append(parts.Count > 0
                            ? $"<color=#aaa>[{string.Join(" ", parts)}]</color>"
                            : "<color=#666>(any)</color>");
                    }

                    if (item.Percent > 0f)
                        sb.Append($" <color=#888>({item.Percent:F1}%)</color>");
                }
            }

            return sb.ToString();
        }
    }
}
