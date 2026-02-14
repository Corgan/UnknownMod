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
    /// IMGUI panel for editing Item definitions at the mod-project level.
    /// Supports creating new items and overriding base-game ones.
    /// Each ItemDef produces an ItemData SO + a paired CardData (equipment card).
    /// </summary>
    public class ItemEditor
    {
        private readonly ModEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secActivation = false;
        private bool _secDamage = true;
        private bool _secResist = true;
        private bool _secStat = false;
        private bool _secHeal = false;
        private bool _secEnergyDraw = false;
        private bool _secAcGain = false;
        private bool _secAcSelf = false;
        private bool _secDispel = false;
        private bool _secAcBonus = false;
        private bool _secAcImmune = false;
        private bool _secCardGain = false;
        private bool _secCostEcon = false;
        private bool _secRewards = false;
        private bool _secDmgTarget = false;
        private bool _secFlags = false;
        private bool _secEnchant = false;
        private bool _secCustomAC = false;
        private bool _secFx = false;

        public ItemEditor(ModEditor parent) => _parent = parent;

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
            return GUI.changed && !string.IsNullOrEmpty(_parent.SelectedItemId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Items.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.ItemPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                _parent.SelectedItemId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.Items.TryGetValue(id, out var it))
                        name = it.Name;
                    else if (proj.ItemPatches.TryGetValue(id, out var itp))
                        name = itp.Name;
                    return $"{badge} {id}  {name}";
                },
                "item_sel");
            if (sel != _parent.SelectedItemId)
                _parent.SelectedItemId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_item";
                int suffix = 1;
                while (proj.Items.ContainsKey(newId) || proj.ItemPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_item{suffix++}";
                var def = new ItemDef { Id = newId, Name = "New Item" };
                proj.Items[newId] = def;
                _parent.SelectedItemId = newId;
                ModProjectLoader.SaveEntity(proj, "items", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(_parent.SelectedItemId))
            {
                bool isNew = proj.Items.ContainsKey(_parent.SelectedItemId);
                bool isOvr = proj.ItemPatches.ContainsKey(_parent.SelectedItemId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Items.Remove(_parent.SelectedItemId);
                        ModProjectLoader.DeleteEntity(proj, "items", _parent.SelectedItemId, false);
                        _parent.SelectedItemId = allIds.FirstOrDefault(k => k != _parent.SelectedItemId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.ItemPatches.Remove(_parent.SelectedItemId);
                        ModProjectLoader.DeleteEntity(proj, "items", _parent.SelectedItemId, true);
                        _parent.SelectedItemId = allIds.FirstOrDefault(k => k != _parent.SelectedItemId);
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
            ItemDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(_parent.SelectedItemId))
            {
                if (proj.Items.TryGetValue(_parent.SelectedItemId, out d))
                    isPatch = false;
                else if (proj.ItemPatches.TryGetValue(_parent.SelectedItemId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select an item above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "items", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game items to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allItemIds = DataHelper.GetAllItemIds();
            int shown = 0;
            foreach (var id in allItemIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.ItemPatches.ContainsKey(id) || proj.Items.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetItem(id);
                    var def = existing != null ? DataHelper.SnapshotItem(existing) : new ItemDef { Id = id };
                    def.Id = id;
                    proj.ItemPatches[id] = def;
                    _parent.SelectedItemId = id;
                    ModProjectLoader.SaveEntity(proj, "items", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(ItemDef d)
        {
            // ── Live Stat Preview ────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
            {
                string desc = BuildItemDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.Name = EditorFields.TextField("Name", d.Name);
                d.SpriteSource = EditorFields.IdDropdown("Sprite Source", d.SpriteSource, DataHelper.GetAllCardIds(), "item_sprsrc");
                d.CardType = EditorFields.EnumField("Card Type", d.CardType, "item_ctype");
                d.Rarity = EditorFields.EnumField("Rarity", d.Rarity, "item_rarity");
            }

            // ── Activation / Requisite ───────────────────────────
            if (EditorFields.Section("Activation / Requisite", ref _secActivation))
            {
                d.Activation = EditorFields.EnumField("Trigger", d.Activation, "item_act");
                d.ActivationOnlyOnHeroes = EditorFields.Toggle("Only On Heroes", d.ActivationOnlyOnHeroes);
                d.ItemTarget = EditorFields.EnumField("Target", d.ItemTarget, "item_tgt");
                d.DontTargetBoss = EditorFields.Toggle("Don't Target Boss", d.DontTargetBoss);
                d.TimesPerTurn = EditorFields.IntField("Per Turn", d.TimesPerTurn);
                d.TimesPerCombat = EditorFields.IntField("Per Combat", d.TimesPerCombat);
                d.ExactRound = EditorFields.IntField("Exact Round", d.ExactRound);
                d.RoundCycle = EditorFields.IntField("Round Cycle", d.RoundCycle);

                GUILayout.Space(4);
                GUILayout.Label("<color=#888>Requisite:</color>", EditorStyles.RichLabel);
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuraCurseSetted = EditorFields.IdDropdown("AC Setted", d.AuraCurseSetted, acIds, "it_acs");
                d.AuraCurseSetted2 = EditorFields.IdDropdown("AC Setted 2", d.AuraCurseSetted2, acIds, "it_acs2");
                d.AuraCurseSetted3 = EditorFields.IdDropdown("AC Setted 3", d.AuraCurseSetted3, acIds, "it_acs3");
                d.AuraCurseNumForOneEvent = EditorFields.IntField("AC Num For Event", d.AuraCurseNumForOneEvent);
                d.CastedCardType = EditorFields.EnumField("Casted Card Type", d.CastedCardType, "item_cct");
                d.UsedEnergy = EditorFields.Toggle("Used Energy", d.UsedEnergy);
                d.LowerOrEqualPercentHP = EditorFields.FloatField("HP \u2264 %", d.LowerOrEqualPercentHP);
                d.EmptyHand = EditorFields.Toggle("Empty Hand", d.EmptyHand);
                d.NotShowCharacterBonus = EditorFields.Toggle("Not Show Char Bonus", d.NotShowCharacterBonus);
                d.PetActivation = EditorFields.EnumField("Pet Activation", d.PetActivation, "item_pet");
            }

            // ── Damage Bonuses ───────────────────────────────────
            if (EditorFields.Section("Damage Bonuses", ref _secDamage))
            {
                d.DamageFlatBonus = EditorFields.EnumField("Flat Type", d.DamageFlatBonus, "item_dfb1");
                d.DamageFlatBonusValue = EditorFields.IntField("Flat Value", d.DamageFlatBonusValue);
                d.DamageFlatBonus2 = EditorFields.EnumField("Flat Type 2", d.DamageFlatBonus2, "item_dfb2");
                d.DamageFlatBonusValue2 = EditorFields.IntField("Flat Value 2", d.DamageFlatBonusValue2);
                d.DamageFlatBonus3 = EditorFields.EnumField("Flat Type 3", d.DamageFlatBonus3, "item_dfb3");
                d.DamageFlatBonusValue3 = EditorFields.IntField("Flat Value 3", d.DamageFlatBonusValue3);
                GUILayout.Space(4);
                d.DamagePercentBonus = EditorFields.EnumField("% Type", d.DamagePercentBonus, "item_dpb1");
                d.DamagePercentBonusValue = EditorFields.FloatField("% Value", d.DamagePercentBonusValue);
                d.DamagePercentBonus2 = EditorFields.EnumField("% Type 2", d.DamagePercentBonus2, "item_dpb2");
                d.DamagePercentBonusValue2 = EditorFields.FloatField("% Value 2", d.DamagePercentBonusValue2);
                d.DamagePercentBonus3 = EditorFields.EnumField("% Type 3", d.DamagePercentBonus3, "item_dpb3");
                d.DamagePercentBonusValue3 = EditorFields.FloatField("% Value 3", d.DamagePercentBonusValue3);
            }

            // ── Resist Bonuses ───────────────────────────────────
            if (EditorFields.Section("Resist Bonuses", ref _secResist))
            {
                d.ResistModified1 = EditorFields.EnumField("Resist Type", d.ResistModified1, "item_rm1");
                d.ResistModifiedValue1 = EditorFields.IntField("Resist Value", d.ResistModifiedValue1);
                d.ResistModified2 = EditorFields.EnumField("Resist Type 2", d.ResistModified2, "item_rm2");
                d.ResistModifiedValue2 = EditorFields.IntField("Resist Value 2", d.ResistModifiedValue2);
                d.ResistModified3 = EditorFields.EnumField("Resist Type 3", d.ResistModified3, "item_rm3");
                d.ResistModifiedValue3 = EditorFields.IntField("Resist Value 3", d.ResistModifiedValue3);
            }

            // ── Character Stat ───────────────────────────────────
            if (EditorFields.Section("Character Stat", ref _secStat))
            {
                d.CharacterStatModified = EditorFields.EnumField("Stat", d.CharacterStatModified, "item_csm");
                d.CharacterStatModifiedValue = EditorFields.IntField("Value", d.CharacterStatModifiedValue);
                d.CharacterStatModified2 = EditorFields.EnumField("Stat 2", d.CharacterStatModified2, "item_csm2");
                d.CharacterStatModifiedValue2 = EditorFields.IntField("Value 2", d.CharacterStatModifiedValue2);
                d.CharacterStatModified3 = EditorFields.EnumField("Stat 3", d.CharacterStatModified3, "item_csm3");
                d.CharacterStatModifiedValue3 = EditorFields.IntField("Value 3", d.CharacterStatModifiedValue3);
                d.MaxHealth = EditorFields.IntField("Max Health", d.MaxHealth);
            }

            // ── Heal Bonuses ─────────────────────────────────────
            if (EditorFields.Section("Heal Bonuses", ref _secHeal))
            {
                d.HealFlatBonus = EditorFields.IntField("Heal Flat Bonus", d.HealFlatBonus);
                d.HealPercentBonus = EditorFields.FloatField("Heal % Bonus", d.HealPercentBonus);
                d.HealReceivedFlatBonus = EditorFields.IntField("Heal Recv Flat", d.HealReceivedFlatBonus);
                d.HealReceivedPercentBonus = EditorFields.FloatField("Heal Recv %", d.HealReceivedPercentBonus);
                d.HealQuantity = EditorFields.IntField("Heal Quantity", d.HealQuantity);
                DrawSpecialValue("Heal SV", ref d.HealQuantitySpecialValue);
                d.HealPercentQuantity = EditorFields.IntField("Heal % Quantity", d.HealPercentQuantity);
                d.HealPercentQuantitySelf = EditorFields.IntField("Heal % Qty Self", d.HealPercentQuantitySelf);
                d.HealSelfPerDamageDonePercent = EditorFields.FloatField("Lifesteal %", d.HealSelfPerDamageDonePercent);
                d.HealSelfTeamPerDamageDonePercent = EditorFields.Toggle("Lifesteal Team", d.HealSelfTeamPerDamageDonePercent);
                d.HealBasedOnAuraCurse = EditorFields.IntField("Heal Based On AC", d.HealBasedOnAuraCurse);
            }

            // ── Energy / Draw ────────────────────────────────────
            if (EditorFields.Section("Energy / Draw", ref _secEnergyDraw))
            {
                d.EnergyQuantity = EditorFields.IntField("Energy", d.EnergyQuantity);
                d.DrawCards = EditorFields.IntField("Draw Cards", d.DrawCards);
                d.DrawMultiplyByEnergyUsed = EditorFields.Toggle("Draw \u00d7 Energy", d.DrawMultiplyByEnergyUsed);
            }

            // ── AC Gain (target) ─────────────────────────────────
            if (EditorFields.Section("AC Gain (Target)", ref _secAcGain))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseGain1 = EditorFields.IdDropdown("Apply AC", d.AuracurseGain1, acIds, "it_acg1");
                d.AuracurseGainValue1 = EditorFields.IntField("Charges", d.AuracurseGainValue1);
                DrawSpecialValue("SV 1", ref d.AuracurseGain1SpecialValue);
                d.Acg1MultiplyByEnergyUsed = EditorFields.Toggle("\u00d7 Energy", d.Acg1MultiplyByEnergyUsed);

                GUILayout.Space(2);
                d.AuracurseGain2 = EditorFields.IdDropdown("Apply AC 2", d.AuracurseGain2, acIds, "it_acg2");
                d.AuracurseGainValue2 = EditorFields.IntField("Charges 2", d.AuracurseGainValue2);
                DrawSpecialValue("SV 2", ref d.AuracurseGain2SpecialValue);
                d.Acg2MultiplyByEnergyUsed = EditorFields.Toggle("\u00d7 Energy 2", d.Acg2MultiplyByEnergyUsed);

                GUILayout.Space(2);
                d.AuracurseGain3 = EditorFields.IdDropdown("Apply AC 3", d.AuracurseGain3, acIds, "it_acg3");
                d.AuracurseGainValue3 = EditorFields.IntField("Charges 3", d.AuracurseGainValue3);
                DrawSpecialValue("SV 3", ref d.AuracurseGain3SpecialValue);
                d.Acg3MultiplyByEnergyUsed = EditorFields.Toggle("\u00d7 Energy 3", d.Acg3MultiplyByEnergyUsed);
                d.ChooseOneACToGain = EditorFields.Toggle("Choose One AC", d.ChooseOneACToGain);
            }

            // ── AC Gain (self) ───────────────────────────────────
            if (EditorFields.Section("AC Gain (Self)", ref _secAcSelf))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseGainSelf1 = EditorFields.IdDropdown("Self AC", d.AuracurseGainSelf1, acIds, "it_acs1");
                d.AuracurseGainSelfValue1 = EditorFields.IntField("Self Charges", d.AuracurseGainSelfValue1);
                d.AuracurseGainSelf2 = EditorFields.IdDropdown("Self AC 2", d.AuracurseGainSelf2, acIds, "it_acs2b");
                d.AuracurseGainSelfValue2 = EditorFields.IntField("Self Charges 2", d.AuracurseGainSelfValue2);
                d.AuracurseGainSelf3 = EditorFields.IdDropdown("Self AC 3", d.AuracurseGainSelf3, acIds, "it_acs3b");
                d.AuracurseGainSelfValue3 = EditorFields.IntField("Self Charges 3", d.AuracurseGainSelfValue3);
            }

            // ── Dispel / Purge ───────────────────────────────────
            if (EditorFields.Section("Dispel / Purge", ref _secDispel))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseHeal1 = EditorFields.IdDropdown("Dispel AC", d.AuracurseHeal1, acIds, "it_ach1");
                d.AuracurseHeal2 = EditorFields.IdDropdown("Dispel AC 2", d.AuracurseHeal2, acIds, "it_ach2");
                d.AuracurseHeal3 = EditorFields.IdDropdown("Dispel AC 3", d.AuracurseHeal3, acIds, "it_ach3");
                d.AcHealFromTarget = EditorFields.Toggle("Heal From Target", d.AcHealFromTarget);
                d.StealAuras = EditorFields.IntField("Steal Auras", d.StealAuras);
                GUILayout.Space(4);
                d.ChanceToDispel = EditorFields.IntField("Dispel %", d.ChanceToDispel);
                d.ChanceToDispelNum = EditorFields.IntField("Dispel Num", d.ChanceToDispelNum);
                d.ChanceToPurge = EditorFields.IntField("Purge %", d.ChanceToPurge);
                d.ChanceToPurgeNum = EditorFields.IntField("Purge Num", d.ChanceToPurgeNum);
                d.ChanceToDispelSelf = EditorFields.IntField("Self Dispel %", d.ChanceToDispelSelf);
                d.ChanceToDispelNumSelf = EditorFields.IntField("Self Dispel Num", d.ChanceToDispelNumSelf);
            }

            // ── Passive AC Bonuses ───────────────────────────────
            if (EditorFields.Section("AC Bonuses", ref _secAcBonus))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseBonus1 = EditorFields.IdDropdown("AC Bonus 1", d.AuracurseBonus1, acIds, "it_acb1");
                d.AuracurseBonusValue1 = EditorFields.IntField("Value 1", d.AuracurseBonusValue1);
                d.AuracurseBonus2 = EditorFields.IdDropdown("AC Bonus 2", d.AuracurseBonus2, acIds, "it_acb2");
                d.AuracurseBonusValue2 = EditorFields.IntField("Value 2", d.AuracurseBonusValue2);
                d.IncreaseAurasSelf = EditorFields.IntField("Increase Auras Self", d.IncreaseAurasSelf);
            }

            // ── AC Immunities ────────────────────────────────────
            if (EditorFields.Section("AC Immunities", ref _secAcImmune))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseImmune1 = EditorFields.IdDropdown("Immune 1", d.AuracurseImmune1, acIds, "it_imm1");
                d.AuracurseImmune2 = EditorFields.IdDropdown("Immune 2", d.AuracurseImmune2, acIds, "it_imm2");
            }

            // ── Card Gain ────────────────────────────────────────
            if (EditorFields.Section("Card Gain", ref _secCardGain))
            {
                d.CardNum = EditorFields.IntField("Card Num", d.CardNum);
                var cardIds = DataHelper.GetAllCardIds();
                d.CardToGain = EditorFields.IdDropdown("Card to Gain", d.CardToGain, cardIds, "it_ctg");
                d.CardToGainType = EditorFields.EnumField("Card Type", d.CardToGainType, "item_ctgt");
                d.CardPlace = EditorFields.EnumField("Card Place", d.CardPlace, "item_cp");

                GUILayout.Space(4);
                GUILayout.Label("<color=#888>Card Gain List:</color>", EditorStyles.RichLabel);
                if (d.CardToGainList == null) d.CardToGainList = new List<string>();
                for (int i = 0; i < d.CardToGainList.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.CardToGainList[i] = EditorFields.IdDropdown($"[{i}]", d.CardToGainList[i], cardIds, $"it_cgl{i}");
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.CardToGainList.RemoveAt(i);
                        GUI.changed = true;
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Add Card", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    d.CardToGainList.Add("");
                    GUI.changed = true;
                }
            }

            // ── Cost / Economy ───────────────────────────────────
            if (EditorFields.Section("Cost / Economy", ref _secCostEcon))
            {
                d.CostZero = EditorFields.Toggle("Cost Zero", d.CostZero);
                d.CostReduction = EditorFields.IntField("Cost Reduction", d.CostReduction);
                d.CardsReduced = EditorFields.IntField("Cards Reduced", d.CardsReduced);
                d.CardToReduceType = EditorFields.EnumField("Reduce Type", d.CardToReduceType, "item_crt");
                d.CostReduceReduction = EditorFields.IntField("Reduce Amount", d.CostReduceReduction);
                d.CostReduceEnergyRequirement = EditorFields.IntField("Energy Req", d.CostReduceEnergyRequirement);
                d.CostReducePermanent = EditorFields.Toggle("Reduce Permanent", d.CostReducePermanent);
                d.ReduceHighestCost = EditorFields.Toggle("Reduce Highest Cost", d.ReduceHighestCost);
            }

            // ── Rewards / Discounts ──────────────────────────────
            if (EditorFields.Section("Rewards / Discounts", ref _secRewards))
            {
                d.PercentRetentionEndGame = EditorFields.IntField("Retention %", d.PercentRetentionEndGame);
                d.PercentDiscountShop = EditorFields.IntField("Shop Discount %", d.PercentDiscountShop);
            }

            // ── Damage To Target ─────────────────────────────────
            if (EditorFields.Section("Damage To Target", ref _secDmgTarget))
            {
                d.DamageToTargetType = EditorFields.EnumField("Dmg Type", d.DamageToTargetType, "item_dtt1");
                d.DamageToTarget = EditorFields.IntField("Dmg Value", d.DamageToTarget);
                d.DttMultiplyByEnergyUsed = EditorFields.Toggle("\u00d7 Energy", d.DttMultiplyByEnergyUsed);
                DrawSpecialValue("DTT SV 1", ref d.DttSpecialValues1);

                GUILayout.Space(4);
                d.DamageToTargetType2 = EditorFields.EnumField("Dmg Type 2", d.DamageToTargetType2, "item_dtt2");
                d.DamageToTarget2 = EditorFields.IntField("Dmg Value 2", d.DamageToTarget2);
                DrawSpecialValue("DTT SV 2", ref d.DttSpecialValues2);

                GUILayout.Space(4);
                d.ModifiedDamageType = EditorFields.EnumField("Modified Dmg Type", d.ModifiedDamageType, "item_mdt");
            }

            // ── Flags ────────────────────────────────────────────
            if (EditorFields.Section("Flags", ref _secFlags))
            {
                d.CursedItem = EditorFields.Toggle("Cursed", d.CursedItem);
                d.DropOnly = EditorFields.Toggle("Drop Only", d.DropOnly);
                d.QuestItem = EditorFields.Toggle("Quest Item", d.QuestItem);
                d.DestroyAfterUse = EditorFields.Toggle("Destroy After Use", d.DestroyAfterUse);
                d.Vanish = EditorFields.Toggle("Vanish", d.Vanish);
                d.Permanent = EditorFields.Toggle("Permanent", d.Permanent);
                d.DuplicateActive = EditorFields.Toggle("Duplicate Active", d.DuplicateActive);
                d.PassSingleAndCharacterRolls = EditorFields.Toggle("Pass Rolls", d.PassSingleAndCharacterRolls);
                d.OnlyAddItemToNPCs = EditorFields.Toggle("Only Add To NPCs", d.OnlyAddItemToNPCs);
                d.AddVanishToDeck = EditorFields.Toggle("Add Vanish To Deck", d.AddVanishToDeck);
            }

            // ── Enchantment ──────────────────────────────────────
            if (EditorFields.Section("Enchantment", ref _secEnchant))
            {
                d.IsEnchantment = EditorFields.Toggle("Is Enchantment", d.IsEnchantment);
                d.UseTheNextInsteadWhenYouPlay = EditorFields.Toggle("Use Next Instead", d.UseTheNextInsteadWhenYouPlay);
                d.DestroyAfterUses = EditorFields.IntField("Destroy After Uses", d.DestroyAfterUses);
                d.DestroyStartOfTurn = EditorFields.Toggle("Destroy Start Of Turn", d.DestroyStartOfTurn);
                d.DestroyEndOfTurn = EditorFields.Toggle("Destroy End Of Turn", d.DestroyEndOfTurn);
                d.CastEnchantmentOnFinishSelfCast = EditorFields.Toggle("Cast On Self Finish", d.CastEnchantmentOnFinishSelfCast);
            }

            // ── Custom AC ────────────────────────────────────────
            if (EditorFields.Section("Custom AC", ref _secCustomAC))
            {
                d.AuracurseCustomString = EditorFields.TextField("Custom String", d.AuracurseCustomString);
                var acIds = DataHelper.GetAllAuraCurseIds();
                d.AuracurseCustomAC = EditorFields.IdDropdown("Custom AC", d.AuracurseCustomAC, acIds, "it_cac");
                d.AuracurseCustomModValue1 = EditorFields.IntField("Mod Value 1", d.AuracurseCustomModValue1);
                d.AuracurseCustomModValue2 = EditorFields.IntField("Mod Value 2", d.AuracurseCustomModValue2);
            }

            // ── FX / Effects ─────────────────────────────────────
            if (EditorFields.Section("FX / Effects", ref _secFx))
            {
                d.EffectItemOwner = EditorFields.TextField("Effect Owner", d.EffectItemOwner);
                d.EffectCaster = EditorFields.TextField("Effect Caster", d.EffectCaster);
                d.EffectCasterDelay = EditorFields.FloatField("Caster Delay", d.EffectCasterDelay);
                d.EffectTarget = EditorFields.TextField("Effect Target", d.EffectTarget);
                d.EffectTargetDelay = EditorFields.FloatField("Target Delay", d.EffectTargetDelay);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPECIAL VALUE WIDGET
        // ═══════════════════════════════════════════════════════════════

        private void DrawSpecialValue(string label, ref SpecialValueDef sv)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#888>{label}:</color>", EditorStyles.RichLabel, GUILayout.Width(80));
            bool hasSV = sv != null;
            bool newHas = EditorFields.Toggle("Use", hasSV);
            if (newHas && !hasSV)
                sv = new SpecialValueDef { Use = true };
            else if (!newHas && hasSV)
                sv = null;
            GUILayout.EndHorizontal();

            if (sv != null)
            {
                sv.Name = EditorFields.EnumField("  SV Name", sv.Name, $"sv_{label.GetHashCode()}");
                sv.Multiplier = EditorFields.FloatField("  Multiplier", sv.Multiplier);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildItemDescription(ItemDef d)
        {
            var sb = new StringBuilder();
            string sep = "";

            // ── Type badge ───────────────────────────────────────
            sb.Append($"<color=#aaa>[{d.CardType}]  {d.Rarity}</color>");
            if (d.IsEnchantment) sb.Append("  <color=#dd88ff>Enchantment</color>");
            sep = "\n";

            // ── Damage bonuses ───────────────────────────────────
            AppendDmgFlat(sb, ref sep, d.DamageFlatBonus, d.DamageFlatBonusValue);
            AppendDmgFlat(sb, ref sep, d.DamageFlatBonus2, d.DamageFlatBonusValue2);
            AppendDmgFlat(sb, ref sep, d.DamageFlatBonus3, d.DamageFlatBonusValue3);
            AppendDmgPct(sb, ref sep, d.DamagePercentBonus, d.DamagePercentBonusValue);
            AppendDmgPct(sb, ref sep, d.DamagePercentBonus2, d.DamagePercentBonusValue2);
            AppendDmgPct(sb, ref sep, d.DamagePercentBonus3, d.DamagePercentBonusValue3);

            // ── Resist bonuses ───────────────────────────────────
            AppendResist(sb, ref sep, d.ResistModified1, d.ResistModifiedValue1);
            AppendResist(sb, ref sep, d.ResistModified2, d.ResistModifiedValue2);
            AppendResist(sb, ref sep, d.ResistModified3, d.ResistModifiedValue3);

            // ── Character stat ───────────────────────────────────
            AppendStat(sb, ref sep, d.CharacterStatModified, d.CharacterStatModifiedValue);
            AppendStat(sb, ref sep, d.CharacterStatModified2, d.CharacterStatModifiedValue2);
            AppendStat(sb, ref sep, d.CharacterStatModified3, d.CharacterStatModifiedValue3);

            // ── Max HP ───────────────────────────────────────────
            if (d.MaxHealth != 0)
            {
                string sign = d.MaxHealth > 0 ? "+" : "";
                sb.Append($"{sep}<color=#44cc44>{sign}{d.MaxHealth}</color> Max HP");
                sep = "\n";
            }

            // ── Heal bonuses ─────────────────────────────────────
            if (d.HealFlatBonus != 0) { sb.Append($"{sep}+<color=#44cc44>{d.HealFlatBonus}</color> Heal"); sep = "\n"; }
            if (d.HealPercentBonus != 0f) { sb.Append($"{sep}+<color=#44cc44>{d.HealPercentBonus:0.#}%</color> Heal"); sep = "\n"; }
            if (d.HealQuantity != 0) { sb.Append($"{sep}Heal <color=#44cc44>{d.HealQuantity}</color>"); sep = "\n"; }
            if (d.HealReceivedPercentBonus != 0f) { sb.Append($"{sep}+<color=#44cc44>{d.HealReceivedPercentBonus:0.#}%</color> Heal recv"); sep = "\n"; }

            // ── AC bonuses ───────────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseBonus1) && d.AuracurseBonusValue1 != 0)
            { sb.Append($"{sep}+<color=#44cc88>{d.AuracurseBonusValue1}</color> {Cap(d.AuracurseBonus1)} bonus"); sep = "\n"; }
            if (!string.IsNullOrEmpty(d.AuracurseBonus2) && d.AuracurseBonusValue2 != 0)
            { sb.Append($"{sep}+<color=#44cc88>{d.AuracurseBonusValue2}</color> {Cap(d.AuracurseBonus2)} bonus"); sep = "\n"; }

            // ── AC immunities ────────────────────────────────────
            if (!string.IsNullOrEmpty(d.AuracurseImmune1))
            { sb.Append($"{sep}<color=#88ccff>Immune</color> to {Cap(d.AuracurseImmune1)}"); sep = "\n"; }
            if (!string.IsNullOrEmpty(d.AuracurseImmune2))
            { sb.Append($"{sep}<color=#88ccff>Immune</color> to {Cap(d.AuracurseImmune2)}"); sep = "\n"; }

            // ── Damage to target ─────────────────────────────────
            if (d.DamageToTarget != 0 && d.DamageToTargetType != Enums.DamageType.None)
            { sb.Append($"{sep}<color=#cc6644>{d.DamageToTarget}</color> {d.DamageToTargetType} to target"); sep = "\n"; }
            if (d.DamageToTarget2 != 0 && d.DamageToTargetType2 != Enums.DamageType.None)
            { sb.Append($"{sep}<color=#cc6644>{d.DamageToTarget2}</color> {d.DamageToTargetType2} to target"); sep = "\n"; }

            // ── Activation ───────────────────────────────────────
            if (d.Activation != Enums.EventActivation.None)
            {
                sb.Append($"{sep}<color=#dd88ff>On {d.Activation}</color>");
                if (d.ItemTarget != Enums.ItemTarget.Self)
                    sb.Append($" \u2192 {d.ItemTarget}");
                sep = "\n";

                if (!string.IsNullOrEmpty(d.AuracurseGain1) && d.AuracurseGainValue1 != 0)
                { sb.Append($"{sep}  Apply <color=#cc6644>{d.AuracurseGainValue1} {Cap(d.AuracurseGain1)}</color>"); sep = "\n"; }
                if (!string.IsNullOrEmpty(d.AuracurseGainSelf1) && d.AuracurseGainSelfValue1 != 0)
                { sb.Append($"{sep}  Gain <color=#44cc88>{d.AuracurseGainSelfValue1} {Cap(d.AuracurseGainSelf1)}</color>"); sep = "\n"; }

                if (d.EnergyQuantity != 0) { sb.Append($"{sep}  +<color=#ffcc44>{d.EnergyQuantity}</color> Energy"); sep = "\n"; }
                if (d.DrawCards != 0) { sb.Append($"{sep}  Draw <color=#ffcc44>{d.DrawCards}</color>"); sep = "\n"; }

                var limits = new List<string>();
                if (d.TimesPerTurn > 0) limits.Add($"{d.TimesPerTurn}/turn");
                if (d.TimesPerCombat > 0) limits.Add($"{d.TimesPerCombat}/combat");
                if (limits.Count > 0)
                    sb.Append($"{sep}  <color=#888>({string.Join(", ", limits)})</color>");
            }

            // ── Flags ────────────────────────────────────────────
            var flags = new List<string>();
            if (d.CursedItem) flags.Add("<color=#cc4444>Cursed</color>");
            if (d.DropOnly) flags.Add("Drop Only");
            if (d.QuestItem) flags.Add("<color=#ffcc44>Quest</color>");
            if (d.DestroyAfterUse) flags.Add("Consumable");
            if (d.Vanish) flags.Add("Vanish");
            if (d.CostZero) flags.Add("Cost Zero");
            if (d.Permanent) flags.Add("<color=#88ccff>Permanent</color>");
            if (d.EmptyHand) flags.Add("Empty Hand");
            if (flags.Count > 0)
                sb.Append($"\n<color=#888>{string.Join(" | ", flags)}</color>");

            return sb.ToString();
        }

        // ── Description helpers ──────────────────────────────────

        private static void AppendDmgFlat(StringBuilder sb, ref string sep, Enums.DamageType dt, int val)
        {
            if (dt != Enums.DamageType.None && val != 0)
            { sb.Append($"{sep}+<color=#e8c06a>{val}</color> {dt} damage"); sep = "\n"; }
        }

        private static void AppendDmgPct(StringBuilder sb, ref string sep, Enums.DamageType dt, float val)
        {
            if (dt != Enums.DamageType.None && val != 0f)
            { sb.Append($"{sep}+<color=#e8c06a>{val:0.#}%</color> {dt} damage"); sep = "\n"; }
        }

        private static void AppendResist(StringBuilder sb, ref string sep, Enums.DamageType dt, int val)
        {
            if (dt != Enums.DamageType.None && val != 0)
            { sb.Append($"{sep}+<color=#88aacc>{val}</color> {dt} resist"); sep = "\n"; }
        }

        private static void AppendStat(StringBuilder sb, ref string sep, Enums.CharacterStat stat, int val)
        {
            if (stat != Enums.CharacterStat.None && val != 0)
            {
                string sign = val > 0 ? "+" : "";
                sb.Append($"{sep}<color=#cccc88>{sign}{val}</color> {stat}");
                sep = "\n";
            }
        }

        private static string Cap(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}
