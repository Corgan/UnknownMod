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
    public class LootEditor : ModProjectEditorBase<LootDef>
    {
        // ── Base class overrides ─────────────────────────────────
        protected override string TypeLabel => "Loot";
        protected override string FolderName => "loot";
        protected override string NewIdSuffix => "_new_loot";

        public override string SelectedId
        {
            get => Parent.SelectedLootId;
            set => Parent.SelectedLootId = value;
        }

        protected override Dictionary<string, LootDef> GetNewDict(ModProject proj) => proj.Loot;
        protected override Dictionary<string, LootDef> GetPatchDict(ModProject proj) => proj.LootPatches;

        protected override LootDef CreateDefault(string id, ModProject proj)
            => new LootDef { Id = id, NumItems = 1 };

        protected override string GetDisplayName(LootDef def)
            => $"({def.Items.Count} items, {def.GoldQuantity}g)";

        protected override LootDef SnapshotBaseEntity(string id)
        {
            var loot = DataHelper.GetLootData(id);
            return loot != null ? DataHelper.SnapshotLoot(loot) : null;
        }

        // Collapsible section state
        private bool _secPreview = true;
        private bool _secRarity = true;
        private bool _secItems = true;

        public LootEditor(ModEditor parent) : base(parent) { }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        protected override void DrawAllSections(LootDef d, ModProject proj)
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
