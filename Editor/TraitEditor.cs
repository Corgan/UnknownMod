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
    /// Supports creating new traits and overriding base-game ones.
    /// </summary>
    public class TraitEditor
    {
        private readonly ZoneEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secActivation = false;
        private bool _secCards = false;
        private bool _secStat = false;
        private bool _secResist = false;
        private bool _secImmunity = false;
        private bool _secACBonus = false;
        private bool _secHeal = false;
        private bool _secDmgFlat = false;
        private bool _secDmgPercent = false;
        private bool _secMisc = false;

        public TraitEditor(ZoneEditor parent) => _parent = parent;

        public string SelectedTraitId { get; set; }

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
            return GUI.changed && !string.IsNullOrEmpty(SelectedTraitId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Traits.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.TraitPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedTraitId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.Traits.TryGetValue(id, out var t))
                        name = t.TraitName;
                    else if (proj.TraitPatches.TryGetValue(id, out var tp))
                        name = tp.TraitName;
                    return $"{badge} {id}  {name}";
                },
                "trait_sel");
            if (sel != SelectedTraitId)
                SelectedTraitId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_trait";
                int suffix = 1;
                while (proj.Traits.ContainsKey(newId) || proj.TraitPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_trait{suffix++}";
                var def = new TraitDef
                {
                    Id = newId,
                    TraitName = "New Trait",
                };
                proj.Traits[newId] = def;
                SelectedTraitId = newId;
                ModProjectLoader.SaveEntity(proj, "traits", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(SelectedTraitId))
            {
                bool isNew = proj.Traits.ContainsKey(SelectedTraitId);
                bool isOvr = proj.TraitPatches.ContainsKey(SelectedTraitId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Traits.Remove(SelectedTraitId);
                        ModProjectLoader.DeleteEntity(proj, "traits", SelectedTraitId, false);
                        SelectedTraitId = allIds.FirstOrDefault(k => k != SelectedTraitId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.TraitPatches.Remove(SelectedTraitId);
                        ModProjectLoader.DeleteEntity(proj, "traits", SelectedTraitId, true);
                        SelectedTraitId = allIds.FirstOrDefault(k => k != SelectedTraitId);
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
            TraitDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedTraitId))
            {
                if (proj.Traits.TryGetValue(SelectedTraitId, out d))
                    isPatch = false;
                else if (proj.TraitPatches.TryGetValue(SelectedTraitId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a trait above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "traits", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game traits to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allIds = DataHelper.GetAllTraitIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.TraitPatches.ContainsKey(id) || proj.Traits.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetTrait(id);
                    var def = existing != null ? DataHelper.SnapshotTrait(existing) : new TraitDef { Id = id };
                    def.Id = id;
                    proj.TraitPatches[id] = def;
                    SelectedTraitId = id;
                    ModProjectLoader.SaveEntity(proj, "traits", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(TraitDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
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
                d.TraitName = EditorFields.TextField("Trait Name", d.TraitName);
                d.Description = EditorFields.TextArea("Description", d.Description);
            }

            // ── Activation ───────────────────────────────────────
            if (EditorFields.Section("Activation", ref _secActivation))
            {
                d.Activation = EditorFields.EnumField("Event Activation", d.Activation, "trait_activ");
                d.ActivateOnRuneTypeAdded = EditorFields.Toggle("On Rune Type Added", d.ActivateOnRuneTypeAdded);
                d.TryActivateOnEveryEvent = EditorFields.Toggle("Try Every Event", d.TryActivateOnEveryEvent);
                d.TimesPerTurn = EditorFields.IntField("Times/Turn", d.TimesPerTurn);
                d.TimesPerRound = EditorFields.IntField("Times/Round", d.TimesPerRound);
            }

            // ── Cards ────────────────────────────────────────────
            if (EditorFields.Section("Cards", ref _secCards))
            {
                var cardIds = BuildCardIdList(proj);
                d.TraitCard = EditorFields.IdDropdown("Trait Card", d.TraitCard, cardIds, "trait_card");
                d.TraitCardForAllHeroes = EditorFields.IdDropdown("Card For All Heroes", d.TraitCardForAllHeroes, cardIds, "trait_card_all");
            }

            // ── Character Stat ───────────────────────────────────
            if (EditorFields.Section("Character Stat Modifier", ref _secStat))
            {
                d.CharacterStatModified = EditorFields.EnumField("Stat", d.CharacterStatModified, "trait_stat");
                d.CharacterStatModifiedValue = EditorFields.IntField("Value", d.CharacterStatModifiedValue);
            }

            // ── Resist Modification ──────────────────────────────
            if (EditorFields.Section("Resist Modification", ref _secResist))
            {
                GUILayout.Label("<color=#aaa>Slot 1:</color>", EditorStyles.RichLabel);
                d.ResistModified1 = EditorFields.EnumField("Type", d.ResistModified1, "trait_res1");
                d.ResistModifiedValue1 = EditorFields.IntField("Value", d.ResistModifiedValue1);
                GUILayout.Label("<color=#aaa>Slot 2:</color>", EditorStyles.RichLabel);
                d.ResistModified2 = EditorFields.EnumField("Type", d.ResistModified2, "trait_res2");
                d.ResistModifiedValue2 = EditorFields.IntField("Value", d.ResistModifiedValue2);
                GUILayout.Label("<color=#aaa>Slot 3:</color>", EditorStyles.RichLabel);
                d.ResistModified3 = EditorFields.EnumField("Type", d.ResistModified3, "trait_res3");
                d.ResistModifiedValue3 = EditorFields.IntField("Value", d.ResistModifiedValue3);
            }

            // ── AC Immunity ──────────────────────────────────────
            if (EditorFields.Section("AuraCurse Immunity", ref _secImmunity))
            {
                d.AuracurseImmune1 = EditorFields.TextField("Immune 1", d.AuracurseImmune1);
                d.AuracurseImmune2 = EditorFields.TextField("Immune 2", d.AuracurseImmune2);
                d.AuracurseImmune3 = EditorFields.TextField("Immune 3", d.AuracurseImmune3);
            }

            // ── AC Bonus ─────────────────────────────────────────
            if (EditorFields.Section("AuraCurse Bonus", ref _secACBonus))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                GUILayout.Label("<color=#aaa>Slot 1:</color>", EditorStyles.RichLabel);
                d.AuracurseBonus1 = EditorFields.IdDropdown("AC", d.AuracurseBonus1, acIds, "trait_acb1");
                d.AuracurseBonusValue1 = EditorFields.IntField("Value", d.AuracurseBonusValue1);
                GUILayout.Label("<color=#aaa>Slot 2:</color>", EditorStyles.RichLabel);
                d.AuracurseBonus2 = EditorFields.IdDropdown("AC", d.AuracurseBonus2, acIds, "trait_acb2");
                d.AuracurseBonusValue2 = EditorFields.IntField("Value", d.AuracurseBonusValue2);
                GUILayout.Label("<color=#aaa>Slot 3:</color>", EditorStyles.RichLabel);
                d.AuracurseBonus3 = EditorFields.IdDropdown("AC", d.AuracurseBonus3, acIds, "trait_acb3");
                d.AuracurseBonusValue3 = EditorFields.IntField("Value", d.AuracurseBonusValue3);
            }

            // ── Heal Bonuses ─────────────────────────────────────
            if (EditorFields.Section("Heal Bonuses", ref _secHeal))
            {
                d.HealFlatBonus = EditorFields.IntField("Heal Flat", d.HealFlatBonus);
                d.HealPercentBonus = EditorFields.FloatField("Heal %", d.HealPercentBonus);
                d.HealReceivedFlatBonus = EditorFields.IntField("Heal Recv Flat", d.HealReceivedFlatBonus);
                d.HealReceivedPercentBonus = EditorFields.FloatField("Heal Recv %", d.HealReceivedPercentBonus);
            }

            // ── Damage Flat Bonus ────────────────────────────────
            if (EditorFields.Section("Damage Flat Bonus", ref _secDmgFlat))
            {
                GUILayout.Label("<color=#aaa>Slot 1:</color>", EditorStyles.RichLabel);
                d.DamageBonusFlat = EditorFields.EnumField("Type", d.DamageBonusFlat, "trait_dmgf1");
                d.DamageBonusFlatValue = EditorFields.IntField("Value", d.DamageBonusFlatValue);
                GUILayout.Label("<color=#aaa>Slot 2:</color>", EditorStyles.RichLabel);
                d.DamageBonusFlat2 = EditorFields.EnumField("Type", d.DamageBonusFlat2, "trait_dmgf2");
                d.DamageBonusFlatValue2 = EditorFields.IntField("Value", d.DamageBonusFlatValue2);
                GUILayout.Label("<color=#aaa>Slot 3:</color>", EditorStyles.RichLabel);
                d.DamageBonusFlat3 = EditorFields.EnumField("Type", d.DamageBonusFlat3, "trait_dmgf3");
                d.DamageBonusFlatValue3 = EditorFields.IntField("Value", d.DamageBonusFlatValue3);
            }

            // ── Damage Percent Bonus ─────────────────────────────
            if (EditorFields.Section("Damage Percent Bonus", ref _secDmgPercent))
            {
                GUILayout.Label("<color=#aaa>Slot 1:</color>", EditorStyles.RichLabel);
                d.DamageBonusPercent = EditorFields.EnumField("Type", d.DamageBonusPercent, "trait_dmgp1");
                d.DamageBonusPercentValue = EditorFields.FloatField("Value", d.DamageBonusPercentValue);
                GUILayout.Label("<color=#aaa>Slot 2:</color>", EditorStyles.RichLabel);
                d.DamageBonusPercent2 = EditorFields.EnumField("Type", d.DamageBonusPercent2, "trait_dmgp2");
                d.DamageBonusPercentValue2 = EditorFields.FloatField("Value", d.DamageBonusPercentValue2);
                GUILayout.Label("<color=#aaa>Slot 3:</color>", EditorStyles.RichLabel);
                d.DamageBonusPercent3 = EditorFields.EnumField("Type", d.DamageBonusPercent3, "trait_dmgp3");
                d.DamageBonusPercentValue3 = EditorFields.FloatField("Value", d.DamageBonusPercentValue3);
            }

            // ── Misc ─────────────────────────────────────────────
            if (EditorFields.Section("Misc", ref _secMisc))
            {
                d.MaxBleedDamagePerTurn = EditorFields.IntField("Max Bleed Dmg/Turn", d.MaxBleedDamagePerTurn);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static List<string> BuildCardIdList(ModProject proj)
        {
            var cardIds = new List<string>();
            cardIds.AddRange(proj.Cards.Keys.OrderBy(k => k));
            cardIds.AddRange(proj.CardPatches.Keys.OrderBy(k => k));
            cardIds.AddRange(DataHelper.GetAllCardIds());
            return cardIds.Distinct().ToList();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildTraitDescription(TraitDef d)
        {
            var sb = new StringBuilder();

            // Header
            sb.Append($"<b>{d.TraitName}</b>");
            if (!string.IsNullOrEmpty(d.Description))
                sb.Append($"\n<color=#aaa>{d.Description}</color>");

            // Activation
            if (d.Activation != Enums.EventActivation.None)
                sb.Append($"\n<color=#88ccff>Activation: {d.Activation}</color>");

            var modifiers = new List<string>();

            // Stat modifier
            if (d.CharacterStatModified != Enums.CharacterStat.None)
                modifiers.Add($"{d.CharacterStatModified} {d.CharacterStatModifiedValue:+#;-#;0}");

            // Resists
            void AddResist(Enums.DamageType dt, int val)
            {
                if (dt != Enums.DamageType.None && val != 0)
                    modifiers.Add($"{dt} Resist {val:+#;-#;0}");
            }
            AddResist(d.ResistModified1, d.ResistModifiedValue1);
            AddResist(d.ResistModified2, d.ResistModifiedValue2);
            AddResist(d.ResistModified3, d.ResistModifiedValue3);

            // Heal bonuses
            if (d.HealFlatBonus != 0) modifiers.Add($"Heal +{d.HealFlatBonus}");
            if (d.HealPercentBonus != 0f) modifiers.Add($"Heal +{d.HealPercentBonus:F0}%");
            if (d.HealReceivedFlatBonus != 0) modifiers.Add($"HealRecv +{d.HealReceivedFlatBonus}");
            if (d.HealReceivedPercentBonus != 0f) modifiers.Add($"HealRecv +{d.HealReceivedPercentBonus:F0}%");

            // Damage flat
            void AddDmgFlat(Enums.DamageType dt, int val)
            {
                if (dt != Enums.DamageType.None && val != 0)
                    modifiers.Add($"{dt} +{val} flat");
            }
            AddDmgFlat(d.DamageBonusFlat, d.DamageBonusFlatValue);
            AddDmgFlat(d.DamageBonusFlat2, d.DamageBonusFlatValue2);
            AddDmgFlat(d.DamageBonusFlat3, d.DamageBonusFlatValue3);

            // Damage percent
            void AddDmgPct(Enums.DamageType dt, float val)
            {
                if (dt != Enums.DamageType.None && val != 0f)
                    modifiers.Add($"{dt} +{val:F0}%");
            }
            AddDmgPct(d.DamageBonusPercent, d.DamageBonusPercentValue);
            AddDmgPct(d.DamageBonusPercent2, d.DamageBonusPercentValue2);
            AddDmgPct(d.DamageBonusPercent3, d.DamageBonusPercentValue3);

            if (modifiers.Count > 0)
                sb.Append($"\n<color=#44cc44>{string.Join(", ", modifiers)}</color>");

            // Cards
            var cardRefs = new List<string>();
            if (!string.IsNullOrEmpty(d.TraitCard)) cardRefs.Add(d.TraitCard);
            if (!string.IsNullOrEmpty(d.TraitCardForAllHeroes)) cardRefs.Add($"{d.TraitCardForAllHeroes} (all)");
            if (cardRefs.Count > 0)
                sb.Append($"\n<color=#dd88ff>Cards: {string.Join(", ", cardRefs)}</color>");

            // Immunities
            var immunes = new List<string>();
            if (!string.IsNullOrEmpty(d.AuracurseImmune1)) immunes.Add(d.AuracurseImmune1);
            if (!string.IsNullOrEmpty(d.AuracurseImmune2)) immunes.Add(d.AuracurseImmune2);
            if (!string.IsNullOrEmpty(d.AuracurseImmune3)) immunes.Add(d.AuracurseImmune3);
            if (immunes.Count > 0)
                sb.Append($"\n<color=#ff8888>Immune: {string.Join(", ", immunes)}</color>");

            // Misc
            if (d.MaxBleedDamagePerTurn >= 0)
                sb.Append($"\n<color=#888>MaxBleedDmg/Turn: {d.MaxBleedDamagePerTurn}</color>");

            return sb.ToString();
        }
    }
}
