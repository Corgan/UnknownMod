using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for editing AuraCurse definitions at the mod-project level.
    /// Supports creating new AuraCurses and overriding base-game ones.
    /// This is the first non-zone data type editor and establishes the pattern
    /// for all subsequent mod-scope editors.
    /// </summary>
    public class AuraCurseEditor
    {
        private readonly ZoneEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secGeneral = true;
        private bool _secConfig = true;
        private bool _secExpiration = false;
        private bool _secDmg1 = false;
        private bool _secDmg2 = false;
        private bool _secDmg3 = false;
        private bool _secDmg4 = false;
        private bool _secHealBonus = false;
        private bool _secDraw = false;
        private bool _secReflected = false;
        private bool _secBlock = false;
        private bool _secPrevention = false;
        private bool _secDmgRecv1 = false;
        private bool _secDmgRecv2 = false;
        private bool _secDmgPrevented = false;
        private bool _secHealAttacker = false;
        private bool _secCharStat = false;
        private bool _secResist1 = false;
        private bool _secResist2 = false;
        private bool _secResist3 = false;
        private bool _secExplode = false;
        private bool _secConsumeDmg = false;
        private bool _secConsumeHeal = false;
        private bool _secRemoveGainAC = false;
        private bool _secRevealCost = false;
        private bool _secDisabledCards = false;
        private bool _secMisc = false;
        private bool _secChargeBonus = false;

        public AuraCurseEditor(ZoneEditor parent) => _parent = parent;

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null)
            {
                GUILayout.Label("<color=#888>No mod project active. Open the Mods tab first.</color>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Build combined entity list ───────────────────────
            // [NEW] = custom content, [OVR] = overrides of base-game
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.AuraCurses.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.AuraCursePatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                _parent.SelectedAuraCurseId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.AuraCurses.TryGetValue(id, out var ac))
                        name = ac.ACName;
                    else if (proj.AuraCursePatches.TryGetValue(id, out var acp))
                        name = acp.ACName;
                    string auraTag = "";
                    if (proj.AuraCurses.TryGetValue(id, out var ac2))
                        auraTag = ac2.IsAura ? " <color=#44cc88>A</color>" : " <color=#cc6644>C</color>";
                    else if (proj.AuraCursePatches.TryGetValue(id, out var acp2))
                        auraTag = acp2.IsAura ? " <color=#44cc88>A</color>" : " <color=#cc6644>C</color>";
                    return $"{badge} {id}  {name}{auraTag}";
                },
                "ac_sel");
            if (sel != _parent.SelectedAuraCurseId)
                _parent.SelectedAuraCurseId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_ac";
                int suffix = 1;
                while (proj.AuraCurses.ContainsKey(newId) || proj.AuraCursePatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_ac{suffix++}";
                var def = new AuraCurseDef { Id = newId, ACName = "New AuraCurse" };
                proj.AuraCurses[newId] = def;
                _parent.SelectedAuraCurseId = newId;
                ModProjectLoader.SaveEntity(proj, "auracurse", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
            {
                _showOverrideBrowser = !_showOverrideBrowser;
                _overrideFilter = "";
            }

            // Delete (for new) or Revert (for overrides)
            if (!string.IsNullOrEmpty(_parent.SelectedAuraCurseId))
            {
                string sid = _parent.SelectedAuraCurseId;
                if (proj.AuraCurses.ContainsKey(sid))
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(55)))
                    {
                        proj.AuraCurses.Remove(sid);
                        ModProjectLoader.DeleteEntity(proj, "auracurse", sid);
                        _parent.SelectedAuraCurseId = allIds.Count > 1 ? allIds.FirstOrDefault(k => k != sid) : null;
                        proj.IsDirty = true;
                    }
                }
                else if (proj.AuraCursePatches.ContainsKey(sid))
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(55)))
                    {
                        proj.AuraCursePatches.Remove(sid);
                        ModProjectLoader.DeleteEntity(proj, "auracurse", sid, isPatch: true);
                        _parent.SelectedAuraCurseId = allIds.Count > 1 ? allIds.FirstOrDefault(k => k != sid) : null;
                        proj.IsDirty = true;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Override browser popup ────────────────────────────
            if (_showOverrideBrowser)
            {
                DrawOverrideBrowser(proj);
            }

            EditorStyles.Separator();

            // ── Resolve which def to edit ─────────────────────────
            AuraCurseDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(_parent.SelectedAuraCurseId))
            {
                if (proj.AuraCurses.TryGetValue(_parent.SelectedAuraCurseId, out var newDef))
                    d = newDef;
                else if (proj.AuraCursePatches.TryGetValue(_parent.SelectedAuraCurseId, out var patchDef))
                {
                    d = patchDef;
                    isPatch = true;
                }
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select an AuraCurse above, or create/override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // Badge
            if (isPatch)
                GUILayout.Label("<color=#ffcc44>  \u26A0 Override of base-game AuraCurse</color>",
                    EditorStyles.RichLabel);

            // ── Draw all field sections ──────────────────────────
            DrawGeneralSection(d);
            DrawConfigSection(d);
            DrawExpirationSection(d);
            DrawAuraDamageBonusSection(d, 1, ref _secDmg1);
            DrawAuraDamageBonusSection(d, 2, ref _secDmg2);
            DrawAuraDamageBonusSection(d, 3, ref _secDmg3);
            DrawAuraDamageBonusSection(d, 4, ref _secDmg4);
            DrawHealBonusSection(d);
            DrawDrawSection(d);
            DrawDamageReflectedSection(d);
            DrawBlockSection(d);
            DrawPreventionSection(d);
            DrawDamageReceivedSection(d, 1, ref _secDmgRecv1);
            DrawDamageReceivedSection(d, 2, ref _secDmgRecv2);
            DrawDamagePreventedSection(d);
            DrawHealAttackerSection(d);
            DrawCharacterStatSection(d);
            DrawResistSection(d, 1, ref _secResist1);
            DrawResistSection(d, 2, ref _secResist2);
            DrawResistSection(d, 3, ref _secResist3);
            DrawExplodeSection(d);
            DrawConsumeDamageSection(d);
            DrawConsumeHealSection(d);
            DrawRemoveGainACSection(d);
            DrawRevealCostSection(d);
            DrawDisabledCardTypesSection(d);
            DrawMiscSection(d);
            DrawChargeBonusSection(d);

            // ── Auto-save on change ──────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "auracurse", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        /// <summary>Returns true if a change was made that needs hot-reload.</summary>
        public bool HandleChanges()
        {
            return GUI.changed && !string.IsNullOrEmpty(_parent.SelectedAuraCurseId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.BeginVertical(EditorStyles.CompactBox);
            GUILayout.Label("<b>Browse base-game AuraCurses to override:</b>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("\u2315", GUILayout.Width(16));
            _overrideFilter = GUILayout.TextField(_overrideFilter);
            GUILayout.EndHorizontal();

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(200));

            var baseIds = DataHelper.GetAllAuraCurseIds();
            string filt = _overrideFilter?.ToLower() ?? "";

            foreach (var id in baseIds)
            {
                if (filt.Length > 0 && !id.ToLower().Contains(filt)) continue;
                // Skip if already overridden
                if (proj.AuraCursePatches.ContainsKey(id) || proj.AuraCurses.ContainsKey(id))
                    continue;

                if (GUILayout.Button(id, EditorStyles.DropdownItem, GUILayout.Height(20)))
                {
                    // Snapshot the base-game SO into a DTO
                    var baseAC = DataHelper.GetAuraCurse(id);
                    if (baseAC != null)
                    {
                        var snapshot = ModProjectBuilder.SnapshotAuraCurse(baseAC);
                        proj.AuraCursePatches[id] = snapshot;
                        ModProjectLoader.SaveEntity(proj, "auracurse", id, snapshot, isPatch: true);
                        _parent.SelectedAuraCurseId = id;
                        proj.IsDirty = true;
                        _showOverrideBrowser = false;
                    }
                }
            }

            GUILayout.EndScrollView();

            if (GUILayout.Button("Cancel", EditorStyles.MiniButton))
                _showOverrideBrowser = false;

            GUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  FIELD SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawGeneralSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("General", ref _secGeneral)) return;

            d.Id = EditorFields.TextField("ID", d.Id);
            d.ACName = EditorFields.TextField("Name", d.ACName);
            d.IsAura = EditorFields.Toggle("Is Aura", d.IsAura);
            d.Description = EditorFields.TextArea("Description", d.Description);
            d.MaxCharges = EditorFields.IntField("Max Charges", d.MaxCharges);
            d.MaxMadnessCharges = EditorFields.IntField("Max Madness Chg", d.MaxMadnessCharges);
            d.AuraConsumed = EditorFields.IntField("Aura Consumed", d.AuraConsumed);
            d.ChargesMultiplierDescription = EditorFields.IntField("Chg Mult Desc", d.ChargesMultiplierDescription);
            d.ChargesAuxNeedForOne1 = EditorFields.FloatField("ChgAuxNeed1", d.ChargesAuxNeedForOne1);
            d.ChargesAuxNeedForOne2 = EditorFields.IntField("ChgAuxNeed2", d.ChargesAuxNeedForOne2);
            d.Sprite = EditorFields.TextField("Sprite", d.Sprite);
            d.EffectTick = EditorFields.TextField("EffectTick", d.EffectTick);
            d.EffectTickSides = EditorFields.TextField("EffectTickSides", d.EffectTickSides);
            d.Sound = EditorFields.TextField("Sound", d.Sound);
            d.SoundRework = EditorFields.TextField("SoundRework", d.SoundRework);
        }

        private void DrawConfigSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Config", ref _secConfig)) return;

            d.Removable = EditorFields.Toggle("Removable", d.Removable);
            d.GainCharges = EditorFields.Toggle("Gain Charges", d.GainCharges);
            d.IconShow = EditorFields.Toggle("Icon Show", d.IconShow);
            d.CombatlogShow = EditorFields.Toggle("Combatlog Show", d.CombatlogShow);
            d.Preventable = EditorFields.Toggle("Preventable", d.Preventable);
            d.CanBeAddedToImmunityDespiteNotBeingPreventable =
                EditorFields.Toggle("Immune Despite !Prev", d.CanBeAddedToImmunityDespiteNotBeingPreventable);
        }

        private void DrawExpirationSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Expiration", ref _secExpiration)) return;

            d.PriorityOnConsumption = EditorFields.IntField("Priority", d.PriorityOnConsumption);
            d.ConsumeAll = EditorFields.Toggle("Consume All", d.ConsumeAll);
            d.ConsumedAtCast = EditorFields.Toggle("At Cast", d.ConsumedAtCast);
            d.ConsumedAtTurnBegin = EditorFields.Toggle("At Turn Begin", d.ConsumedAtTurnBegin);
            d.ConsumedAtTurn = EditorFields.Toggle("At Turn End", d.ConsumedAtTurn);
            d.ConsumedAtRoundBegin = EditorFields.Toggle("At Round Begin", d.ConsumedAtRoundBegin);
            d.ConsumedAtRound = EditorFields.Toggle("At Round End", d.ConsumedAtRound);
            d.ProduceDamageWhenConsumed = EditorFields.Toggle("Dmg On Consume", d.ProduceDamageWhenConsumed);
            d.ProduceHealWhenConsumed = EditorFields.Toggle("Heal On Consume", d.ProduceHealWhenConsumed);
            d.DieWhenConsumedAll = EditorFields.Toggle("Die When All Gone", d.DieWhenConsumedAll);
        }

        private void DrawAuraDamageBonusSection(AuraCurseDef d, int slot, ref bool expanded)
        {
            if (!EditorFields.Section($"Aura Damage Bonus {slot}", ref expanded)) return;

            var acIds = DataHelper.GetAllAuraCurseIds();
            switch (slot)
            {
                case 1:
                    d.AuraDamageType = EditorFields.EnumField("Damage Type", d.AuraDamageType, "ac_adt1");
                    d.AuraDamageChargesBasedOnACCharges = EditorFields.IdDropdown(
                        "Chg Based On AC", d.AuraDamageChargesBasedOnACCharges, acIds, "ac_adcbac1");
                    d.AuraDamageIncreasedTotal = EditorFields.IntField("Total", d.AuraDamageIncreasedTotal);
                    d.AuraDamageIncreasedPerStack = EditorFields.FloatField("Per Stack", d.AuraDamageIncreasedPerStack);
                    d.AuraDamageIncreasedPercent = EditorFields.IntField("% Total", d.AuraDamageIncreasedPercent);
                    d.AuraDamageIncreasedPercentPerStack = EditorFields.FloatField("% Per Stack", d.AuraDamageIncreasedPercentPerStack);
                    d.AuraDamageIncreasedPercentPerStackPerEnergy = EditorFields.FloatField("% /Stack/Energy", d.AuraDamageIncreasedPercentPerStackPerEnergy);
                    break;
                case 2:
                    d.AuraDamageType2 = EditorFields.EnumField("Damage Type", d.AuraDamageType2, "ac_adt2");
                    d.AuraDamageIncreasedTotal2 = EditorFields.IntField("Total", d.AuraDamageIncreasedTotal2);
                    d.AuraDamageIncreasedPerStack2 = EditorFields.FloatField("Per Stack", d.AuraDamageIncreasedPerStack2);
                    d.AuraDamageIncreasedPercent2 = EditorFields.IntField("% Total", d.AuraDamageIncreasedPercent2);
                    d.AuraDamageIncreasedPercentPerStack2 = EditorFields.FloatField("% Per Stack", d.AuraDamageIncreasedPercentPerStack2);
                    d.AuraDamageIncreasedPercentPerStackPerEnergy2 = EditorFields.FloatField("% /Stack/Energy", d.AuraDamageIncreasedPercentPerStackPerEnergy2);
                    break;
                case 3:
                    d.AuraDamageType3 = EditorFields.EnumField("Damage Type", d.AuraDamageType3, "ac_adt3");
                    d.AuraDamageIncreasedTotal3 = EditorFields.IntField("Total", d.AuraDamageIncreasedTotal3);
                    d.AuraDamageIncreasedPerStack3 = EditorFields.FloatField("Per Stack", d.AuraDamageIncreasedPerStack3);
                    d.AuraDamageIncreasedPercent3 = EditorFields.IntField("% Total", d.AuraDamageIncreasedPercent3);
                    d.AuraDamageIncreasedPercentPerStack3 = EditorFields.FloatField("% Per Stack", d.AuraDamageIncreasedPercentPerStack3);
                    d.AuraDamageIncreasedPercentPerStackPerEnergy3 = EditorFields.FloatField("% /Stack/Energy", d.AuraDamageIncreasedPercentPerStackPerEnergy3);
                    break;
                case 4:
                    d.AuraDamageType4 = EditorFields.EnumField("Damage Type", d.AuraDamageType4, "ac_adt4");
                    d.AuraDamageIncreasedTotal4 = EditorFields.IntField("Total", d.AuraDamageIncreasedTotal4);
                    d.AuraDamageIncreasedPerStack4 = EditorFields.FloatField("Per Stack", d.AuraDamageIncreasedPerStack4);
                    d.AuraDamageIncreasedPercent4 = EditorFields.IntField("% Total", d.AuraDamageIncreasedPercent4);
                    d.AuraDamageIncreasedPercentPerStack4 = EditorFields.FloatField("% Per Stack", d.AuraDamageIncreasedPercentPerStack4);
                    d.AuraDamageIncreasedPercentPerStackPerEnergy4 = EditorFields.FloatField("% /Stack/Energy", d.AuraDamageIncreasedPercentPerStackPerEnergy4);
                    break;
            }
        }

        private void DrawHealBonusSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Heal Bonus", ref _secHealBonus)) return;

            d.HealDoneTotal = EditorFields.IntField("Done Total", d.HealDoneTotal);
            d.HealDonePerStack = EditorFields.IntField("Done /Stack", d.HealDonePerStack);
            d.HealDonePercent = EditorFields.IntField("Done %", d.HealDonePercent);
            d.HealDonePercentPerStack = EditorFields.IntField("Done % /Stack", d.HealDonePercentPerStack);
            d.HealDonePercentPerStackPerEnergy = EditorFields.IntField("Done %/Stack/Nrg", d.HealDonePercentPerStackPerEnergy);
            GUILayout.Space(4);
            d.HealReceivedTotal = EditorFields.IntField("Recv Total", d.HealReceivedTotal);
            d.HealReceivedPerStack = EditorFields.IntField("Recv /Stack", d.HealReceivedPerStack);
            d.HealReceivedPercent = EditorFields.IntField("Recv %", d.HealReceivedPercent);
            d.HealReceivedPercentPerStack = EditorFields.IntField("Recv % /Stack", d.HealReceivedPercentPerStack);
        }

        private void DrawDrawSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Card Draw", ref _secDraw)) return;

            d.CardsDrawPerStack = EditorFields.IntField("Cards /Stack", d.CardsDrawPerStack);
        }

        private void DrawDamageReflectedSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Damage Reflected", ref _secReflected)) return;

            d.ChargesPreReqForDamageReflection = EditorFields.IntField("Charges PreReq", d.ChargesPreReqForDamageReflection);
            d.DamageReflectedModifierType = EditorFields.EnumField("Modifier Type", d.DamageReflectedModifierType, "ac_drmt");
            d.DamageReflectedMultiplier = EditorFields.IntField("Multiplier", d.DamageReflectedMultiplier);
            d.DamageReflectedType = EditorFields.EnumField("Damage Type", d.DamageReflectedType, "ac_drt");
            d.DamageReflectedConsumeCharges = EditorFields.IntField("Consume Charges", d.DamageReflectedConsumeCharges);
        }

        private void DrawBlockSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Block", ref _secBlock)) return;

            d.BlockChargesGainedPerStack = EditorFields.IntField("Block /Stack", d.BlockChargesGainedPerStack);
            d.NoRemoveBlockAtTurnEnd = EditorFields.Toggle("Keep Block EOT", d.NoRemoveBlockAtTurnEnd);
            d.GrantBlockToTeamForAmountOfDamageBlocked =
                EditorFields.Toggle("Team Block on Dmg", d.GrantBlockToTeamForAmountOfDamageBlocked);
            d.ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked =
                EditorFields.IntField("Chg PreReq Team", d.ChargesPreReqForGrantBlockToTeamForAmountOfDamageBlocked);
        }

        private void DrawPreventionSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Prevention", ref _secPrevention)) return;

            var acIds = DataHelper.GetAllAuraCurseIds();
            d.DamagePreventedPerStack = EditorFields.IntField("Dmg Prev /Stack", d.DamagePreventedPerStack);
            d.CursePreventedPerStack = EditorFields.IntField("Curse Prev /Stack", d.CursePreventedPerStack);
            d.PreventedAuraCurse = EditorFields.IdDropdown("Prevented AC", d.PreventedAuraCurse, acIds, "ac_prevac");
            d.PreventedAuraCurseStackPerStack = EditorFields.IntField("Prev AC /Stack", d.PreventedAuraCurseStackPerStack);
        }

        private void DrawDamageReceivedSection(AuraCurseDef d, int slot, ref bool expanded)
        {
            if (!EditorFields.Section($"Damage Received {slot}", ref expanded)) return;

            switch (slot)
            {
                case 1:
                    d.IncreasedDamageReceivedType = EditorFields.EnumField("Damage Type", d.IncreasedDamageReceivedType, "ac_idrt1");
                    d.IncreasedDirectDamageChargesMultiplierNeededForOne = EditorFields.IntField("Chg Mult For 1", d.IncreasedDirectDamageChargesMultiplierNeededForOne);
                    d.IncreasedDirectDamageReceivedPerTurn = EditorFields.IntField("Direct /Turn", d.IncreasedDirectDamageReceivedPerTurn);
                    d.IncreasedDirectDamageReceivedPerStack = EditorFields.FloatField("Direct /Stack", d.IncreasedDirectDamageReceivedPerStack);
                    d.IncreasedPercentDamageReceivedPerTurn = EditorFields.IntField("% /Turn", d.IncreasedPercentDamageReceivedPerTurn);
                    d.IncreasedPercentDamageReceivedPerStack = EditorFields.IntField("% /Stack", d.IncreasedPercentDamageReceivedPerStack);
                    break;
                case 2:
                    d.IncreasedDamageReceivedType2 = EditorFields.EnumField("Damage Type", d.IncreasedDamageReceivedType2, "ac_idrt2");
                    d.IncreasedDirectDamageChargesMultiplierNeededForOne2 = EditorFields.IntField("Chg Mult For 1", d.IncreasedDirectDamageChargesMultiplierNeededForOne2);
                    d.IncreasedDirectDamageReceivedPerTurn2 = EditorFields.IntField("Direct /Turn", d.IncreasedDirectDamageReceivedPerTurn2);
                    d.IncreasedDirectDamageReceivedPerStack2 = EditorFields.FloatField("Direct /Stack", d.IncreasedDirectDamageReceivedPerStack2);
                    d.IncreasedPercentDamageReceivedPerTurn2 = EditorFields.IntField("% /Turn", d.IncreasedPercentDamageReceivedPerTurn2);
                    d.IncreasedPercentDamageReceivedPerStack2 = EditorFields.IntField("% /Stack", d.IncreasedPercentDamageReceivedPerStack2);
                    break;
            }
        }

        private void DrawDamagePreventedSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Damage Type Prevention", ref _secDmgPrevented)) return;

            d.PreventedDamageTypePerStack = EditorFields.EnumField("Damage Type", d.PreventedDamageTypePerStack, "ac_pdtps");
            d.PreventedDamagePerStack = EditorFields.IntField("Amount /Stack", d.PreventedDamagePerStack);
        }

        private void DrawHealAttackerSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Heal Attacker", ref _secHealAttacker)) return;

            d.HealAttackerPerStack = EditorFields.IntField("Heal /Stack", d.HealAttackerPerStack);
            d.HealAttackerConsumeCharges = EditorFields.IntField("Consume Chg", d.HealAttackerConsumeCharges);
        }

        private void DrawCharacterStatSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Character Stat", ref _secCharStat)) return;

            d.CharacterStatModified = EditorFields.EnumField("Stat", d.CharacterStatModified, "ac_csm");
            d.CharacterStatChargesMultiplierNeededForOne = EditorFields.IntField("Chg Mult For 1", d.CharacterStatChargesMultiplierNeededForOne);
            d.CharacterStatModifiedValue = EditorFields.IntField("Value", d.CharacterStatModifiedValue);
            d.CharacterStatModifiedValuePerStack = EditorFields.FloatField("Value /Stack", d.CharacterStatModifiedValuePerStack);
            d.CharacterStatAbsolute = EditorFields.Toggle("Absolute", d.CharacterStatAbsolute);
            d.CharacterStatAbsoluteValue = EditorFields.IntField("Abs Value", d.CharacterStatAbsoluteValue);
            d.CharacterStatAbsoluteValuePerStack = EditorFields.IntField("Abs Val /Stack", d.CharacterStatAbsoluteValuePerStack);
        }

        private void DrawResistSection(AuraCurseDef d, int slot, ref bool expanded)
        {
            if (!EditorFields.Section($"Resist Modification {slot}", ref expanded)) return;

            switch (slot)
            {
                case 1:
                    d.ResistModified = EditorFields.EnumField("Type", d.ResistModified, "ac_rm1");
                    d.ResistModifiedValue = EditorFields.FloatField("Value", d.ResistModifiedValue);
                    d.ResistModifiedPercentagePerStack = EditorFields.FloatField("% /Stack", d.ResistModifiedPercentagePerStack);
                    break;
                case 2:
                    d.ResistModified2 = EditorFields.EnumField("Type", d.ResistModified2, "ac_rm2");
                    d.ResistModifiedValue2 = EditorFields.FloatField("Value", d.ResistModifiedValue2);
                    d.ResistModifiedPercentagePerStack2 = EditorFields.FloatField("% /Stack", d.ResistModifiedPercentagePerStack2);
                    break;
                case 3:
                    d.ResistModified3 = EditorFields.EnumField("Type", d.ResistModified3, "ac_rm3");
                    d.ResistModifiedValue3 = EditorFields.FloatField("Value", d.ResistModifiedValue3);
                    d.ResistModifiedPercentagePerStack3 = EditorFields.FloatField("% /Stack", d.ResistModifiedPercentagePerStack3);
                    break;
            }
        }

        private void DrawExplodeSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Explode", ref _secExplode)) return;

            var acIds = DataHelper.GetAllAuraCurseIds();
            d.ExplodeAtStacks = EditorFields.IntField("At Stacks", d.ExplodeAtStacks);
            d.HealTotalOnExplode = EditorFields.IntField("Heal Total", d.HealTotalOnExplode);
            d.HealPerChargeOnExplode = EditorFields.FloatField("Heal /Charge", d.HealPerChargeOnExplode);
            d.HealTargetOnExplode = EditorFields.EnumField("Heal Target", d.HealTargetOnExplode, "ac_htoe");
            d.ACOnExplode = EditorFields.IdDropdown("AC On Explode", d.ACOnExplode, acIds, "ac_acoe");
            d.ACTotalChargesOnExplode = EditorFields.IntField("AC Total Chg", d.ACTotalChargesOnExplode);
            d.ACChargesPerStackChargeOnExplode = EditorFields.IntField("AC Chg /Stack", d.ACChargesPerStackChargeOnExplode);
        }

        private void DrawConsumeDamageSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Consume Damage", ref _secConsumeDmg)) return;

            var acIds = DataHelper.GetAllAuraCurseIds();
            d.DamageTypeWhenConsumed = EditorFields.EnumField("Damage Type", d.DamageTypeWhenConsumed, "ac_dtwc");
            d.ConsumedDamageChargesBasedOnACCharges = EditorFields.IdDropdown(
                "Chg Based On AC", d.ConsumedDamageChargesBasedOnACCharges, acIds, "ac_cdcbac");
            d.ConsumeDamageChargesIfACApplied = EditorFields.IdDropdown(
                "If AC Applied", d.ConsumeDamageChargesIfACApplied, acIds, "ac_cdciac");
            d.DamageWhenConsumed = EditorFields.IntField("Damage", d.DamageWhenConsumed);
            d.DamageWhenConsumedPerCharge = EditorFields.FloatField("Dmg /Charge", d.DamageWhenConsumedPerCharge);
            d.DamageSidesWhenConsumed = EditorFields.IntField("Sides", d.DamageSidesWhenConsumed);
            d.DamageSidesWhenConsumedPerCharge = EditorFields.IntField("Sides /Charge", d.DamageSidesWhenConsumedPerCharge);
            d.DoubleDamageIfCursesLessThan = EditorFields.IntField("2x If Curses <", d.DoubleDamageIfCursesLessThan);
        }

        private void DrawConsumeHealSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Consume Heal", ref _secConsumeHeal)) return;

            d.HealWhenConsumed = EditorFields.IntField("Heal", d.HealWhenConsumed);
            d.HealWhenConsumedPerCharge = EditorFields.FloatField("Heal /Charge", d.HealWhenConsumedPerCharge);
            d.HealSidesWhenConsumed = EditorFields.IntField("Sides", d.HealSidesWhenConsumed);
            d.HealSidesWhenConsumedPerCharge = EditorFields.FloatField("Sides /Charge", d.HealSidesWhenConsumedPerCharge);
        }

        private void DrawRemoveGainACSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Remove / Gain AC", ref _secRemoveGainAC)) return;

            var acIds = DataHelper.GetAllAuraCurseIds();

            GUILayout.Label("<color=#aaa>Remove on Consumption:</color>", EditorStyles.RichLabel);
            d.RemoveAuraCurse = EditorFields.IdDropdown("Remove AC 1", d.RemoveAuraCurse, acIds, "ac_rac1");
            d.RemoveAuraCurse2 = EditorFields.IdDropdown("Remove AC 2", d.RemoveAuraCurse2, acIds, "ac_rac2");

            GUILayout.Space(4);
            GUILayout.Label("<color=#aaa>Gain on Consumption (slot 1):</color>", EditorStyles.RichLabel);
            d.GainAuraCurseConsumption = EditorFields.IdDropdown("Gain AC", d.GainAuraCurseConsumption, acIds, "ac_gac1");
            d.GainAuraCurseConsumptionPerCharge = EditorFields.IntField("Chg /Stack", d.GainAuraCurseConsumptionPerCharge);
            d.GainChargesFromThisAuraCurse = EditorFields.IdDropdown("Chg From AC", d.GainChargesFromThisAuraCurse, acIds, "ac_gcfac1");

            GUILayout.Space(4);
            GUILayout.Label("<color=#aaa>Gain on Consumption (slot 2):</color>", EditorStyles.RichLabel);
            d.GainAuraCurseConsumption2 = EditorFields.IdDropdown("Gain AC 2", d.GainAuraCurseConsumption2, acIds, "ac_gac2");
            d.GainAuraCurseConsumptionPerCharge2 = EditorFields.IntField("Chg /Stack 2", d.GainAuraCurseConsumptionPerCharge2);
            d.GainChargesFromThisAuraCurse2 = EditorFields.IdDropdown("Chg From AC 2", d.GainChargesFromThisAuraCurse2, acIds, "ac_gcfac2");
        }

        private void DrawRevealCostSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Reveal / Card Cost", ref _secRevealCost)) return;

            d.RevealCardsPerCharge = EditorFields.IntField("Reveal /Charge", d.RevealCardsPerCharge);
            d.ModifyCardCostPerChargeNeededForOne = EditorFields.IntField("Cost Chg For 1", d.ModifyCardCostPerChargeNeededForOne);
        }

        private void DrawDisabledCardTypesSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Disabled Card Types", ref _secDisabledCards)) return;

            if (d.DisabledCardTypes == null)
                d.DisabledCardTypes = System.Array.Empty<Enums.CardType>();

            GUILayout.Label($"<color=#aaa>{d.DisabledCardTypes.Length} type(s) disabled</color>",
                EditorStyles.RichLabel);

            for (int i = 0; i < d.DisabledCardTypes.Length; i++)
            {
                GUILayout.BeginHorizontal();
                d.DisabledCardTypes[i] = EditorFields.EnumField($"Type {i + 1}", d.DisabledCardTypes[i], $"ac_dct_{i}");
                if (GUILayout.Button("X", EditorStyles.DangerButton, GUILayout.Width(22)))
                {
                    var list = new List<Enums.CardType>(d.DisabledCardTypes);
                    list.RemoveAt(i);
                    d.DisabledCardTypes = list.ToArray();
                    GUI.changed = true;
                }
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Type", EditorStyles.MiniButton, GUILayout.Width(80)))
            {
                var list = new List<Enums.CardType>(d.DisabledCardTypes);
                list.Add(Enums.CardType.None);
                d.DisabledCardTypes = list.ToArray();
                GUI.changed = true;
            }
        }

        private void DrawMiscSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Misc Flags", ref _secMisc)) return;

            d.Invulnerable = EditorFields.Toggle("Invulnerable", d.Invulnerable);
            d.Stealth = EditorFields.Toggle("Stealth", d.Stealth);
            d.Taunt = EditorFields.Toggle("Taunt", d.Taunt);
            d.SkipsNextTurn = EditorFields.Toggle("Skip Next Turn", d.SkipsNextTurn);
        }

        private void DrawChargeBonusSection(AuraCurseDef d)
        {
            if (!EditorFields.Section("Charge Bonuses", ref _secChargeBonus)) return;

            var acIds = DataHelper.GetAllAuraCurseIds();

            GUILayout.Label("<color=#aaa>AC Charge Bonuses:</color>", EditorStyles.RichLabel);
            if (d.ACBonusData == null)
                d.ACBonusData = new List<AuraCurseChargesBonusDef>();

            for (int i = 0; i < d.ACBonusData.Count; i++)
            {
                var b = d.ACBonusData[i];
                GUILayout.BeginHorizontal();
                b.AuraCurseId = EditorFields.IdDropdown($"AC {i + 1}", b.AuraCurseId, acIds, $"ac_acb_{i}");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                b.ChargesBonus = EditorFields.IntField("Charges", b.ChargesBonus);
                if (GUILayout.Button("X", EditorStyles.DangerButton, GUILayout.Width(22)))
                {
                    d.ACBonusData.RemoveAt(i);
                    GUI.changed = true;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            if (GUILayout.Button("+ Add AC Bonus", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                d.ACBonusData.Add(new AuraCurseChargesBonusDef());
                GUI.changed = true;
            }

            GUILayout.Space(8);
            GUILayout.Label("<color=#aaa>Aura Damage Conditional Bonuses:</color>", EditorStyles.RichLabel);
            if (d.AuraDamageConditionalBonuses == null)
                d.AuraDamageConditionalBonuses = new List<AuraDamageBonusDef>();

            for (int i = 0; i < d.AuraDamageConditionalBonuses.Count; i++)
            {
                var cb = d.AuraDamageConditionalBonuses[i];
                cb.DamageType = EditorFields.EnumField($"Type {i + 1}", cb.DamageType, $"ac_adcb_dt_{i}");
                cb.FlatBonus = EditorFields.IntField("Flat", cb.FlatBonus);
                cb.FlatBonusPerStack = EditorFields.FloatField("Flat /Stack", cb.FlatBonusPerStack);
                cb.PercentBonus = EditorFields.IntField("% Bonus", cb.PercentBonus);
                cb.PercentBonusPerStack = EditorFields.FloatField("% /Stack", cb.PercentBonusPerStack);

                if (GUILayout.Button($"Remove Bonus {i + 1}", EditorStyles.DangerButton, GUILayout.Width(120)))
                {
                    d.AuraDamageConditionalBonuses.RemoveAt(i);
                    GUI.changed = true;
                }
                GUILayout.Space(4);
            }

            if (GUILayout.Button("+ Add Dmg Bonus", EditorStyles.MiniButton, GUILayout.Width(110)))
            {
                d.AuraDamageConditionalBonuses.Add(new AuraDamageBonusDef());
                GUI.changed = true;
            }
        }
    }
}
