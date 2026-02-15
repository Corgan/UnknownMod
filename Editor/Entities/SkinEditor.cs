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
    /// </summary>
    public class SkinEditor : ModProjectEditorBase<SkinDef>
    {
        protected override string TypeLabel => "Skin";
        protected override string FolderName => "skins";
        protected override string NewIdSuffix => "_new_skin";

        protected override Dictionary<string, SkinDef> GetNewDict(ModProject proj) => proj.Skins;
        protected override Dictionary<string, SkinDef> GetPatchDict(ModProject proj) => proj.SkinPatches;

        protected override SkinDef CreateDefault(string id, ModProject proj)
            => new SkinDef { Id = id, SkinName = "New Skin" };

        protected override string GetDisplayName(SkinDef def) => def.SkinName;

        protected override SkinDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetSkin(id);
            return existing != null ? DataHelper.SnapshotSkin(existing) : null;
        }

        public SkinEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ─────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secConfig = false;
        private bool _secVisual = false;
        private bool _secSelection = false;

        // ═══════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════

        protected override void DrawAllSections(SkinDef d, ModProject proj)
        {
            // ── Preview ──────────────────────────────────────────
            if (EditorFields.Section("Skin Preview", ref _secPreview))
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
                d.SkinName = EditorFields.TextField("Name", d.SkinName);
                d.SkinTextId = EditorFields.TextField("Text ID", d.SkinTextId);
            }

            // ── Config ───────────────────────────────────────────
            if (EditorFields.Section("Config", ref _secConfig))
            {
                var heroIds = DataHelper.GetAllSubClassIds();
                d.SkinSubclass = EditorFields.IdDropdown("Subclass", d.SkinSubclass, heroIds, "skin_sub");
                d.SkinOrder = EditorFields.IntField("Sort Order", d.SkinOrder);
                d.BaseSkin = EditorFields.Toggle("Base Skin", d.BaseSkin);
                d.PerkLevel = EditorFields.IntField("Perk Level", d.PerkLevel);
                d.Sku = EditorFields.TextField("SKU", d.Sku);
                d.SteamStat = EditorFields.TextField("Steam Stat", d.SteamStat);
            }

            // ── Visual Source ────────────────────────────────────
            if (EditorFields.Section("Visual Source", ref _secVisual))
            {
                d.SpriteSource = EditorFields.TextField("Sprite Source", d.SpriteSource);
            }

            // ── Selection Screen ─────────────────────────────────
            if (EditorFields.Section("Selection Screen", ref _secSelection))
            {
                d.HeroSelectionScreenScale = EditorFields.FloatField("Scale", d.HeroSelectionScreenScale);
                d.HeroSelectionScreenOffsetX = EditorFields.FloatField("Offset X", d.HeroSelectionScreenOffsetX);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════

        public static string BuildSkinDescription(SkinDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{d.SkinName}</b>");
            if (d.BaseSkin) sb.Append("  <color=#44cc88>[BASE]</color>");
            if (!string.IsNullOrEmpty(d.SkinSubclass))
                sb.Append($"\n<color=#88ccff>Class: {d.SkinSubclass}</color>");
            sb.Append($"\nOrder {d.SkinOrder}  PerkLv {d.PerkLevel}");
            return sb.ToString();
        }
    }
}
