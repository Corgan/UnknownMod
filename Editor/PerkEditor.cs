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
    /// Supports creating new perks and overriding base-game ones.
    /// </summary>
    public class PerkEditor
    {
        private readonly ZoneEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secClass = false;
        private bool _secPosition = false;
        private bool _secCurrency = false;
        private bool _secStats = false;
        private bool _secDamage = false;
        private bool _secAC = false;
        private bool _secResist = false;

        public PerkEditor(ZoneEditor parent) => _parent = parent;

        public string SelectedPerkId { get; set; }

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
            return GUI.changed && !string.IsNullOrEmpty(SelectedPerkId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Perks.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.PerkPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedPerkId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    return $"{badge} {id}";
                },
                "perk_sel");
            if (sel != SelectedPerkId)
                SelectedPerkId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_perk";
                int suffix = 1;
                while (proj.Perks.ContainsKey(newId) || proj.PerkPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_perk{suffix++}";
                var def = new PerkDef { Id = newId };
                proj.Perks[newId] = def;
                SelectedPerkId = newId;
                ModProjectLoader.SaveEntity(proj, "perks", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(SelectedPerkId))
            {
                bool isNew = proj.Perks.ContainsKey(SelectedPerkId);
                bool isOvr = proj.PerkPatches.ContainsKey(SelectedPerkId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Perks.Remove(SelectedPerkId);
                        ModProjectLoader.DeleteEntity(proj, "perks", SelectedPerkId, false);
                        SelectedPerkId = allIds.FirstOrDefault(k => k != SelectedPerkId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.PerkPatches.Remove(SelectedPerkId);
                        ModProjectLoader.DeleteEntity(proj, "perks", SelectedPerkId, true);
                        SelectedPerkId = allIds.FirstOrDefault(k => k != SelectedPerkId);
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
            PerkDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedPerkId))
            {
                if (proj.Perks.TryGetValue(SelectedPerkId, out d))
                    isPatch = false;
                else if (proj.PerkPatches.TryGetValue(SelectedPerkId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a perk above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "perks", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game perks to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allIds = DataHelper.GetAllPerkIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.PerkPatches.ContainsKey(id) || proj.Perks.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetPerk(id);
                    var def = existing != null ? DataHelper.SnapshotPerk(existing) : new PerkDef { Id = id };
                    def.Id = id;
                    proj.PerkPatches[id] = def;
                    SelectedPerkId = id;
                    ModProjectLoader.SaveEntity(proj, "perks", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(PerkDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
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
                d.CustomDescription = EditorFields.TextArea("Description", d.CustomDescription);
                d.IconTextValue = EditorFields.TextField("Icon Text", d.IconTextValue);
            }

            // ── Classification ───────────────────────────────────
            if (EditorFields.Section("Classification", ref _secClass))
            {
                d.CardClass = EditorFields.EnumField("Card Class", d.CardClass, "perk_class");
                d.MainPerk = EditorFields.Toggle("Main Perk", d.MainPerk);
                d.ObeliskPerk = EditorFields.Toggle("Obelisk Perk", d.ObeliskPerk);
            }

            // ── Position ─────────────────────────────────────────
            if (EditorFields.Section("Position", ref _secPosition))
            {
                d.Level = EditorFields.IntField("Level", d.Level);
                d.Row = EditorFields.IntField("Row", d.Row);
            }

            // ── Currency ─────────────────────────────────────────
            if (EditorFields.Section("Currency", ref _secCurrency))
            {
                d.AdditionalCurrency = EditorFields.IntField("Add. Currency", d.AdditionalCurrency);
                d.AdditionalShards = EditorFields.IntField("Add. Shards", d.AdditionalShards);
            }

            // ── Stats ────────────────────────────────────────────
            if (EditorFields.Section("Stats", ref _secStats))
            {
                d.MaxHealth = EditorFields.IntField("Max Health", d.MaxHealth);
                d.EnergyBegin = EditorFields.IntField("Energy Begin", d.EnergyBegin);
                d.SpeedQuantity = EditorFields.IntField("Speed", d.SpeedQuantity);
                d.HealQuantity = EditorFields.IntField("Heal Quantity", d.HealQuantity);
            }

            // ── Damage Bonus ─────────────────────────────────────
            if (EditorFields.Section("Damage Bonus", ref _secDamage))
            {
                d.DamageFlatBonus = EditorFields.EnumField("Type", d.DamageFlatBonus, "perk_dmg");
                d.DamageFlatBonusValue = EditorFields.IntField("Value", d.DamageFlatBonusValue);
            }

            // ── AuraCurse Bonus ──────────────────────────────────
            if (EditorFields.Section("AuraCurse Bonus", ref _secAC))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseBonus = EditorFields.IdDropdown("AC", d.AuracurseBonus, acIds, "perk_ac");
                d.AuracurseBonusValue = EditorFields.IntField("Value", d.AuracurseBonusValue);
            }

            // ── Resist Modification ──────────────────────────────
            if (EditorFields.Section("Resist Modification", ref _secResist))
            {
                d.ResistModified = EditorFields.EnumField("Type", d.ResistModified, "perk_res");
                d.ResistModifiedValue = EditorFields.IntField("Value", d.ResistModifiedValue);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildPerkDescription(PerkDef d)
        {
            var sb = new StringBuilder();

            sb.Append($"<b>Perk: {d.Id}</b>");
            if (!string.IsNullOrEmpty(d.CustomDescription))
                sb.Append($"\n<color=#aaa>{d.CustomDescription}</color>");

            // Classification
            var tags = new List<string>();
            if (d.CardClass != Enums.CardClass.None) tags.Add(d.CardClass.ToString());
            if (d.MainPerk) tags.Add("Main");
            if (d.ObeliskPerk) tags.Add("Obelisk");
            if (tags.Count > 0)
                sb.Append($"\n<color=#88ccff>{string.Join(", ", tags)}</color>");

            // Position
            if (d.Level != 0 || d.Row != 0)
                sb.Append($"\n<color=#888>Lv{d.Level} Row{d.Row}</color>");

            var modifiers = new List<string>();

            // Stats
            if (d.MaxHealth != 0) modifiers.Add($"HP {d.MaxHealth:+#;-#;0}");
            if (d.EnergyBegin != 0) modifiers.Add($"Energy {d.EnergyBegin:+#;-#;0}");
            if (d.SpeedQuantity != 0) modifiers.Add($"Speed {d.SpeedQuantity:+#;-#;0}");
            if (d.HealQuantity != 0) modifiers.Add($"Heal {d.HealQuantity:+#;-#;0}");

            // Damage
            if (d.DamageFlatBonus != Enums.DamageType.None && d.DamageFlatBonusValue != 0)
                modifiers.Add($"{d.DamageFlatBonus} +{d.DamageFlatBonusValue}");

            // AC
            if (!string.IsNullOrEmpty(d.AuracurseBonus) && d.AuracurseBonusValue != 0)
                modifiers.Add($"{d.AuracurseBonus} {d.AuracurseBonusValue:+#;-#;0}");

            // Resist
            if (d.ResistModified != Enums.DamageType.None && d.ResistModifiedValue != 0)
                modifiers.Add($"{d.ResistModified} Resist {d.ResistModifiedValue:+#;-#;0}");

            if (modifiers.Count > 0)
                sb.Append($"\n<color=#44cc44>{string.Join(", ", modifiers)}</color>");

            // Currency
            if (d.AdditionalCurrency != 0 || d.AdditionalShards != 0)
                sb.Append($"\n<color=#ddbb44>Currency +{d.AdditionalCurrency}, Shards +{d.AdditionalShards}</color>");

            return sb.ToString();
        }
    }
}
