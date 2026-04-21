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
    public class CardbackEditor : ModProjectEditorBase<CardbackDef>
    {
        protected override string TypeLabel => "Cardback";
        protected override string FolderName => "cardbacks";
        protected override string NewIdSuffix => "_new_cardback";
        protected override EntityPicker.Mode? PickerMode => EntityPicker.Mode.Cardback;

        protected override Dictionary<string, CardbackDef> GetNewDict(ModProject proj) => proj.Cardbacks;
        protected override Dictionary<string, CardbackDef> GetPatchDict(ModProject proj) => proj.CardbackPatches;

        protected override CardbackDef CreateDefault(string id, ModProject proj)
            => new CardbackDef { Id = id, CardbackName = "New Cardback" };

        protected override string GetDisplayName(CardbackDef def) => def.CardbackName;

        protected override CardbackDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetCardback(id);
            return existing != null ? DataHelper.SnapshotCardback(existing) : null;
        }

        public CardbackEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secConfig = false;
        private bool _secRequirements = false;
        private bool _secVisual = false;

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        protected override void DrawAllSections(CardbackDef d, ModProject proj)
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
