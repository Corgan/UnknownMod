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
    /// IMGUI panel for editing perk definitions at the mod-project level.
    /// </summary>
    public class PerkEditor : ModProjectEditorBase<PerkDef>
    {
        protected override string TypeLabel => "Perk";
        protected override string FolderName => "perks";
        protected override string NewIdSuffix => "_new_perk";

        protected override Dictionary<string, PerkDef> GetNewDict(ModProject proj) => proj.Perks;
        protected override Dictionary<string, PerkDef> GetPatchDict(ModProject proj) => proj.PerkPatches;

        protected override PerkDef CreateDefault(string id, ModProject proj)
            => new PerkDef { Id = id };

        protected override string GetDisplayName(PerkDef def) => "";

        protected override PerkDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetPerk(id);
            return existing != null ? DataHelper.SnapshotPerk(existing) : null;
        }

        public PerkEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ─────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secClassification = true;
        private bool _secPosition = false;
        private bool _secCurrency = false;
        private bool _secStats = false;
        private bool _secDmgBonus = false;
        private bool _secAcBonus = false;
        private bool _secResist = false;

        // ═══════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════

        protected override void DrawAllSections(PerkDef d, ModProject proj)
        {
            // ── Preview ──────────────────────────────────────────
            if (EditorFields.Section("Perk Preview", ref _secPreview))
            {
                string desc = BuildPerkDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.CustomDescription = EditorFields.TextField("Description", d.CustomDescription);
                d.IconTextValue = EditorFields.TextField("Icon Text", d.IconTextValue);
            }

            // ── Classification ───────────────────────────────────
            if (EditorFields.Section("Classification", ref _secClassification))
            {
                d.CardClass = EditorFields.EnumField("Class", d.CardClass, "perk_class");
                d.MainPerk = EditorFields.Toggle("Main Perk", d.MainPerk);
                d.ObeliskPerk = EditorFields.Toggle("Obelisk Perk", d.ObeliskPerk);
                d.Level = EditorFields.IntField("Level", d.Level);
            }

            // ── Position ─────────────────────────────────────────
            if (EditorFields.Section("Position", ref _secPosition))
            {
                d.Row = EditorFields.IntField("Row", d.Row);
            }

            // ── Currency ─────────────────────────────────────────
            if (EditorFields.Section("Currency", ref _secCurrency))
            {
                d.AdditionalCurrency = EditorFields.IntField("Currency", d.AdditionalCurrency);
                d.AdditionalShards = EditorFields.IntField("Shards", d.AdditionalShards);
            }

            // ── Stats ────────────────────────────────────────────
            if (EditorFields.Section("Stats", ref _secStats))
            {
                d.MaxHealth = EditorFields.IntField("Max Health", d.MaxHealth);
                d.SpeedQuantity = EditorFields.IntField("Speed", d.SpeedQuantity);
                d.EnergyBegin = EditorFields.IntField("Energy Begin", d.EnergyBegin);
                d.HealQuantity = EditorFields.IntField("Heal", d.HealQuantity);
            }

            // ── Damage Bonus ─────────────────────────────────────
            if (EditorFields.Section("Damage Bonus", ref _secDmgBonus))
            {
                d.DamageFlatBonus = EditorFields.EnumField("Type", d.DamageFlatBonus, "perk_dmgtype");
                d.DamageFlatBonusValue = EditorFields.IntField("Value", d.DamageFlatBonusValue);
            }

            // ── AC Bonus ─────────────────────────────────────────
            if (EditorFields.Section("Aura/Curse Bonus", ref _secAcBonus))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseBonus = EditorFields.IdDropdown("AC ID", d.AuracurseBonus, acIds, "perk_acbonus");
                d.AuracurseBonusValue = EditorFields.IntField("AC Value", d.AuracurseBonusValue);
            }

            // ── Resist ───────────────────────────────────────────
            if (EditorFields.Section("Resist", ref _secResist))
            {
                d.ResistModified = EditorFields.EnumField("Type", d.ResistModified, "perk_restype");
                d.ResistModifiedValue = EditorFields.IntField("Value", d.ResistModifiedValue);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════

        public static string BuildPerkDescription(PerkDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{d.Id}</b>");
            if (d.MainPerk) sb.Append("  <color=#44cc88>[MAIN]</color>");
            if (d.ObeliskPerk) sb.Append("  <color=#ffcc44>[OBELISK]</color>");
            sb.Append($"  <color=#aaa>Lv{d.Level}  {d.CardClass}</color>");

            var lines = new List<string>();
            if (d.SpeedQuantity != 0) lines.Add($"Speed {d.SpeedQuantity:+#;-#;0}");
            if (d.EnergyBegin != 0) lines.Add($"Energy +{d.EnergyBegin}");
            if (d.HealQuantity != 0) lines.Add($"Heal {d.HealQuantity}");
            if (d.MaxHealth != 0) lines.Add($"HP {d.MaxHealth:+#;-#;0}");
            if (d.DamageFlatBonus != Enums.DamageType.None) lines.Add($"Dmg:{d.DamageFlatBonus} +{d.DamageFlatBonusValue}");
            if (!string.IsNullOrEmpty(d.AuracurseBonus)) lines.Add($"AC:{d.AuracurseBonus} {d.AuracurseBonusValue:+#;-#;0}");
            if (d.ResistModified != Enums.DamageType.None) lines.Add($"Res:{d.ResistModified} {d.ResistModifiedValue:+#;-#;0}");

            if (lines.Count > 0)
                sb.Append($"\n<color=#88ccff>{string.Join("  ", lines)}</color>");

            return sb.ToString();
        }
    }
}
