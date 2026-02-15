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
    /// IMGUI panel for editing trait definitions at the mod-project level.
    /// </summary>
    public class TraitEditor : ModProjectEditorBase<TraitDef>
    {
        protected override string TypeLabel => "Trait";
        protected override string FolderName => "traits";
        protected override string NewIdSuffix => "_new_trait";

        protected override Dictionary<string, TraitDef> GetNewDict(ModProject proj) => proj.Traits;
        protected override Dictionary<string, TraitDef> GetPatchDict(ModProject proj) => proj.TraitPatches;

        protected override TraitDef CreateDefault(string id, ModProject proj)
            => new TraitDef { Id = id, TraitName = "New Trait" };

        protected override string GetDisplayName(TraitDef def) => def.TraitName;

        protected override TraitDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetTrait(id);
            return existing != null ? DataHelper.SnapshotTrait(existing) : null;
        }

        public TraitEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ─────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secActivation = false;
        private bool _secCards = false;
        private bool _secCharStat = false;
        private bool _secResist = false;
        private bool _secImmunity = false;
        private bool _secAcBonus = false;
        private bool _secHealBonus = false;
        private bool _secDmgFlat = false;
        private bool _secDmgPct = false;
        private bool _secMisc = false;

        // ═══════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════

        protected override void DrawAllSections(TraitDef d, ModProject proj)
        {
            // ── Preview ──────────────────────────────────────────
            if (EditorFields.Section("Trait Preview", ref _secPreview))
            {
                string desc = BuildTraitDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.TraitName = EditorFields.TextField("Name", d.TraitName);
                d.Description = EditorFields.TextField("Description", d.Description);
            }

            // ── Activation ───────────────────────────────────────
            if (EditorFields.Section("Activation", ref _secActivation))
            {
                d.Activation = EditorFields.EnumField("Activation", d.Activation, "tr_activation");
                d.ActivateOnRuneTypeAdded = EditorFields.Toggle("On Rune Added", d.ActivateOnRuneTypeAdded);
                d.TryActivateOnEveryEvent = EditorFields.Toggle("Try Every Event", d.TryActivateOnEveryEvent);
                d.TimesPerTurn = EditorFields.IntField("Times / Turn", d.TimesPerTurn);
                d.TimesPerRound = EditorFields.IntField("Times / Round", d.TimesPerRound);
            }

            // ── Cards ────────────────────────────────────────────
            if (EditorFields.Section("Cards", ref _secCards))
            {
                var cardIds = EditorFields.BuildCardIdList(proj);
                d.TraitCard = EditorFields.IdDropdown("Trait Card", d.TraitCard, cardIds, "tr_card");
                d.TraitCardForAllHeroes = EditorFields.IdDropdown("Card (All Heroes)", d.TraitCardForAllHeroes, cardIds, "tr_cardall");
            }

            // ── Character Stat ───────────────────────────────────
            if (EditorFields.Section("Character Stat", ref _secCharStat))
            {
                d.CharacterStatModified = EditorFields.EnumField("Stat", d.CharacterStatModified, "tr_charstat");
                d.CharacterStatModifiedValue = EditorFields.IntField("Value", d.CharacterStatModifiedValue);
            }

            // ── Resist Modification (3 slots) ────────────────────
            if (EditorFields.Section("Resist Modification", ref _secResist))
            {
                d.ResistModified1 = EditorFields.EnumField("Resist 1", d.ResistModified1, "tr_res1");
                d.ResistModifiedValue1 = EditorFields.IntField("Value 1", d.ResistModifiedValue1);
                GUILayout.Space(4);
                d.ResistModified2 = EditorFields.EnumField("Resist 2", d.ResistModified2, "tr_res2");
                d.ResistModifiedValue2 = EditorFields.IntField("Value 2", d.ResistModifiedValue2);
                GUILayout.Space(4);
                d.ResistModified3 = EditorFields.EnumField("Resist 3", d.ResistModified3, "tr_res3");
                d.ResistModifiedValue3 = EditorFields.IntField("Value 3", d.ResistModifiedValue3);
            }

            // ── AuraCurse Immunity (3 slots) ─────────────────────
            if (EditorFields.Section("AC Immunity", ref _secImmunity))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseImmune1 = EditorFields.IdDropdown("Immune 1", d.AuracurseImmune1, acIds, "tr_imm1");
                d.AuracurseImmune2 = EditorFields.IdDropdown("Immune 2", d.AuracurseImmune2, acIds, "tr_imm2");
                d.AuracurseImmune3 = EditorFields.IdDropdown("Immune 3", d.AuracurseImmune3, acIds, "tr_imm3");
            }

            // ── AuraCurse Bonus (3 slots) ────────────────────────
            if (EditorFields.Section("AC Bonus", ref _secAcBonus))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseBonus1 = EditorFields.IdDropdown("AC 1", d.AuracurseBonus1, acIds, "tr_acb1");
                d.AuracurseBonusValue1 = EditorFields.IntField("Value 1", d.AuracurseBonusValue1);
                GUILayout.Space(4);
                d.AuracurseBonus2 = EditorFields.IdDropdown("AC 2", d.AuracurseBonus2, acIds, "tr_acb2");
                d.AuracurseBonusValue2 = EditorFields.IntField("Value 2", d.AuracurseBonusValue2);
                GUILayout.Space(4);
                d.AuracurseBonus3 = EditorFields.IdDropdown("AC 3", d.AuracurseBonus3, acIds, "tr_acb3");
                d.AuracurseBonusValue3 = EditorFields.IntField("Value 3", d.AuracurseBonusValue3);
            }

            // ── Heal Bonuses ─────────────────────────────────────
            if (EditorFields.Section("Heal Bonuses", ref _secHealBonus))
            {
                d.HealFlatBonus = EditorFields.IntField("Heal Flat", d.HealFlatBonus);
                d.HealPercentBonus = EditorFields.FloatField("Heal %", d.HealPercentBonus);
                d.HealReceivedFlatBonus = EditorFields.IntField("Recv Flat", d.HealReceivedFlatBonus);
                d.HealReceivedPercentBonus = EditorFields.FloatField("Recv %", d.HealReceivedPercentBonus);
            }

            // ── Damage Flat Bonus (3 slots) ──────────────────────
            if (EditorFields.Section("Damage Flat Bonus", ref _secDmgFlat))
            {
                d.DamageBonusFlat = EditorFields.EnumField("Type 1", d.DamageBonusFlat, "tr_df1");
                d.DamageBonusFlatValue = EditorFields.IntField("Value 1", d.DamageBonusFlatValue);
                GUILayout.Space(4);
                d.DamageBonusFlat2 = EditorFields.EnumField("Type 2", d.DamageBonusFlat2, "tr_df2");
                d.DamageBonusFlatValue2 = EditorFields.IntField("Value 2", d.DamageBonusFlatValue2);
                GUILayout.Space(4);
                d.DamageBonusFlat3 = EditorFields.EnumField("Type 3", d.DamageBonusFlat3, "tr_df3");
                d.DamageBonusFlatValue3 = EditorFields.IntField("Value 3", d.DamageBonusFlatValue3);
            }

            // ── Damage Percent Bonus (3 slots) ───────────────────
            if (EditorFields.Section("Damage Percent Bonus", ref _secDmgPct))
            {
                d.DamageBonusPercent = EditorFields.EnumField("Type 1", d.DamageBonusPercent, "tr_dp1");
                d.DamageBonusPercentValue = EditorFields.FloatField("Value 1", d.DamageBonusPercentValue);
                GUILayout.Space(4);
                d.DamageBonusPercent2 = EditorFields.EnumField("Type 2", d.DamageBonusPercent2, "tr_dp2");
                d.DamageBonusPercentValue2 = EditorFields.FloatField("Value 2", d.DamageBonusPercentValue2);
                GUILayout.Space(4);
                d.DamageBonusPercent3 = EditorFields.EnumField("Type 3", d.DamageBonusPercent3, "tr_dp3");
                d.DamageBonusPercentValue3 = EditorFields.FloatField("Value 3", d.DamageBonusPercentValue3);
            }

            // ── Misc ─────────────────────────────────────────────
            if (EditorFields.Section("Misc", ref _secMisc))
            {
                d.MaxBleedDamagePerTurn = EditorFields.IntField("Max Bleed Dmg/Turn", d.MaxBleedDamagePerTurn);
                GUILayout.Label("<color=#888>(-1 = unlimited)</color>", EditorStyles.RichLabel);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════

        public static string BuildTraitDescription(TraitDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{d.TraitName}</b>  <color=#aaa>{d.Activation}</color>");

            // Activation limits
            var limits = new List<string>();
            if (d.TimesPerTurn > 0) limits.Add($"{d.TimesPerTurn}/turn");
            if (d.TimesPerRound > 0) limits.Add($"{d.TimesPerRound}/round");
            if (limits.Count > 0) sb.Append($"  ({string.Join(", ", limits)})");

            // Cards
            if (!string.IsNullOrEmpty(d.TraitCard))
                sb.Append($"\n<color=#ccccff>Card: {d.TraitCard}</color>");
            if (!string.IsNullOrEmpty(d.TraitCardForAllHeroes))
                sb.Append($"\n<color=#ccccff>AllHeroes: {d.TraitCardForAllHeroes}</color>");

            // Character stat
            if (d.CharacterStatModified != Enums.CharacterStat.None)
                sb.Append($"\n<color=#44cc88>{d.CharacterStatModified} {d.CharacterStatModifiedValue:+#;-#;0}</color>");

            // Resist modifications
            void AddResist(Enums.DamageType type, int val, int slot)
            {
                if (type != Enums.DamageType.None)
                    sb.Append($"\n  Resist {slot}: {type} {val:+#;-#;0}");
            }
            bool hasResist = d.ResistModified1 != Enums.DamageType.None
                || d.ResistModified2 != Enums.DamageType.None
                || d.ResistModified3 != Enums.DamageType.None;
            if (hasResist)
            {
                sb.Append("\n<color=#88aacc>Resists:");
                AddResist(d.ResistModified1, d.ResistModifiedValue1, 1);
                AddResist(d.ResistModified2, d.ResistModifiedValue2, 2);
                AddResist(d.ResistModified3, d.ResistModifiedValue3, 3);
                sb.Append("</color>");
            }

            // Damage flat bonus
            void AddDmgFlat(Enums.DamageType type, int val, int slot)
            {
                if (type != Enums.DamageType.None)
                    sb.Append($"\n  Flat {slot}: {type} {val:+#;-#;0}");
            }
            bool hasDmgFlat = d.DamageBonusFlat != Enums.DamageType.None
                || d.DamageBonusFlat2 != Enums.DamageType.None
                || d.DamageBonusFlat3 != Enums.DamageType.None;
            if (hasDmgFlat)
            {
                sb.Append("\n<color=#cc8844>Dmg Flat:");
                AddDmgFlat(d.DamageBonusFlat, d.DamageBonusFlatValue, 1);
                AddDmgFlat(d.DamageBonusFlat2, d.DamageBonusFlatValue2, 2);
                AddDmgFlat(d.DamageBonusFlat3, d.DamageBonusFlatValue3, 3);
                sb.Append("</color>");
            }

            // Damage percent bonus
            void AddDmgPct(Enums.DamageType type, float val, int slot)
            {
                if (type != Enums.DamageType.None)
                    sb.Append($"\n  Pct {slot}: {type} {val:+#.##;-#.##;0}%");
            }
            bool hasDmgPct = d.DamageBonusPercent != Enums.DamageType.None
                || d.DamageBonusPercent2 != Enums.DamageType.None
                || d.DamageBonusPercent3 != Enums.DamageType.None;
            if (hasDmgPct)
            {
                sb.Append("\n<color=#dd88ff>Dmg %:");
                AddDmgPct(d.DamageBonusPercent, d.DamageBonusPercentValue, 1);
                AddDmgPct(d.DamageBonusPercent2, d.DamageBonusPercentValue2, 2);
                AddDmgPct(d.DamageBonusPercent3, d.DamageBonusPercentValue3, 3);
                sb.Append("</color>");
            }

            // Heal bonuses
            {
                var lines = new List<string>();
                if (d.HealFlatBonus != 0) lines.Add($"Heal {d.HealFlatBonus:+#;-#;0}");
                if (d.HealPercentBonus != 0) lines.Add($"Heal {d.HealPercentBonus:+#.##;-#.##;0}%");
                if (d.HealReceivedFlatBonus != 0) lines.Add($"Recv {d.HealReceivedFlatBonus:+#;-#;0}");
                if (d.HealReceivedPercentBonus != 0) lines.Add($"Recv {d.HealReceivedPercentBonus:+#.##;-#.##;0}%");
                if (lines.Count > 0)
                    sb.Append($"\n<color=#88ff88>{string.Join("  ", lines)}</color>");
            }

            // AC bonuses
            if (!string.IsNullOrEmpty(d.AuracurseBonus1))
                sb.Append($"\nAC1: {d.AuracurseBonus1} {d.AuracurseBonusValue1:+#;-#;0}");
            if (!string.IsNullOrEmpty(d.AuracurseBonus2))
                sb.Append($"\nAC2: {d.AuracurseBonus2} {d.AuracurseBonusValue2:+#;-#;0}");
            if (!string.IsNullOrEmpty(d.AuracurseBonus3))
                sb.Append($"\nAC3: {d.AuracurseBonus3} {d.AuracurseBonusValue3:+#;-#;0}");

            // AC immunities
            {
                var imms = new List<string>();
                if (!string.IsNullOrEmpty(d.AuracurseImmune1)) imms.Add(d.AuracurseImmune1);
                if (!string.IsNullOrEmpty(d.AuracurseImmune2)) imms.Add(d.AuracurseImmune2);
                if (!string.IsNullOrEmpty(d.AuracurseImmune3)) imms.Add(d.AuracurseImmune3);
                if (imms.Count > 0)
                    sb.Append($"\n<color=#88ccff>Immune: {string.Join(", ", imms)}</color>");
            }

            return sb.ToString();
        }
    }
}
