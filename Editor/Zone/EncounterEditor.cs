using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for editing combat encounters (NPC lists, tier, background).
    /// Uses EntitySelector dropdown instead of a full list.
    /// </summary>
    public class EncounterEditor
    {
        private readonly ModEditor _parent;

        // Collapsible section state
        private bool _secNpcs = true;
        private bool _secEffects = false;
        private bool _secAdvanced = false;
        private bool _secUsedBy = false;

        // Patch browser state
        private bool _showPatchBrowser;
        private Vector2 _patchBrowserScroll;
        private string _patchFilter = "";

        public EncounterEditor(ModEditor parent) => _parent = parent;

        /// <summary>Build the combined NPC list from mod project + base game.</summary>
        private static List<string> GetAllNpcIds()
        {
            var ids = new List<string>();
            var proj = ModManagerPanel.ActiveProject;
            if (proj != null)
            {
                ids.AddRange(proj.Npcs.Keys);
                ids.AddRange(proj.NpcPatches.Keys);
            }
            ids.AddRange(EditorFields.CachedIds("npc", DataHelper.GetAllNpcIds));
            return ids.Distinct().OrderBy(k => k).ToList();
        }

        /// <summary>Ensure NpcIds list has exactly 4 entries (pad with "").</summary>
        private static void EnsureFourSlots(CombatDef cd)
        {
            while (cd.NpcIds.Count < 4) cd.NpcIds.Add("");
            if (cd.NpcIds.Count > 4) cd.NpcIds.RemoveRange(4, cd.NpcIds.Count - 4);
        }

        public void DrawPanel()
        {
            // Resolve from mod project
            Dictionary<string, CombatDef> combats = null;
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("No project loaded."); return; }

            combats = new Dictionary<string, CombatDef>();
            foreach (var kvp in proj.Combats) combats[kvp.Key] = kvp.Value;
            foreach (var kvp in proj.CombatPatches) combats[kvp.Key] = kvp.Value;

            //  Entity selector 
            var combatIds = combats.Keys.OrderBy(k => k).ToList();
            string sel = EditorFields.EntitySelector(
                _parent.SelectedCombatId, combatIds,
                id =>
                {
                    string badge = proj.Combats.ContainsKey(id) ? "[NEW] " :
                                   proj.CombatPatches.ContainsKey(id) ? "[PATCH] " : "";
                    return combats.TryGetValue(id, out var c) ? $"{badge}{id}  [{c.CombatTier}]" : $"{badge}{id}";
                },
                "enc_sel");
            if (sel != _parent.SelectedCombatId)
                _parent.SelectedCombatId = sel;

            // ── Action bar: New / Patch / Delete ─────────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New Encounter", EditorStyles.MiniButton, GUILayout.Width(110)))
            {
                string newId = $"{proj.ModId}_combat_{proj.Combats.Count}";
                int suffix = 0;
                while (proj.Combats.ContainsKey(newId) || proj.CombatPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_combat_{++suffix}";

                var def = new CombatDef { CombatId = newId, Description = "New Encounter" };
                proj.Combats[newId] = def;
                _parent.SelectedCombatId = newId;
                ModProjectLoader.SaveEntity(proj, "combats", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Patch Base \u25BE", EditorStyles.MiniButton, GUILayout.Width(100)))
                _showPatchBrowser = !_showPatchBrowser;

            if (!string.IsNullOrEmpty(_parent.SelectedCombatId))
            {
                bool isNew = proj.Combats.ContainsKey(_parent.SelectedCombatId);
                bool isPatch = proj.CombatPatches.ContainsKey(_parent.SelectedCombatId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Combats.Remove(_parent.SelectedCombatId);
                        ModProjectLoader.DeleteEntity(proj, "combats", _parent.SelectedCombatId);
                        _parent.SelectedCombatId = combatIds.FirstOrDefault(k => k != _parent.SelectedCombatId);
                        proj.IsDirty = true;
                    }
                }
                else if (isPatch)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.CombatPatches.Remove(_parent.SelectedCombatId);
                        ModProjectLoader.DeleteEntity(proj, "combats", _parent.SelectedCombatId, true);
                        _parent.SelectedCombatId = combatIds.FirstOrDefault(k => k != _parent.SelectedCombatId);
                        proj.IsDirty = true;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Patch browser ────────────────────────────────────
            if (_showPatchBrowser)
                DrawPatchBrowser(proj);

            EditorStyles.Separator();

            if (combats.Count == 0)
            {
                GUILayout.Label("<i>No encounters yet. Add one with + New Encounter or Patch Base.</i>", EditorStyles.RichLabel);
                return;
            }

            if (_parent.SelectedCombatId == null || !combats.TryGetValue(_parent.SelectedCombatId, out var cd))
            {
                GUILayout.Label("<i>Select a combat above.</i>", EditorStyles.RichLabel);
                return;
            }

            //  Basic fields 
            string prevCombatId = cd.CombatId;
            cd.CombatId = EditorFields.TextField("Combat ID", cd.CombatId);
            if (cd.CombatId != prevCombatId && !string.IsNullOrEmpty(cd.CombatId))
            {
                bool wasNew = proj.Combats.ContainsKey(prevCombatId);
                bool wasPatch = proj.CombatPatches.ContainsKey(prevCombatId);
                var dict = wasNew ? proj.Combats : wasPatch ? proj.CombatPatches : null;
                if (dict != null && !proj.Combats.ContainsKey(cd.CombatId) && !proj.CombatPatches.ContainsKey(cd.CombatId))
                {
                    dict.Remove(prevCombatId);
                    dict[cd.CombatId] = cd;
                    ModProjectLoader.DeleteEntity(proj, "combats", prevCombatId, wasPatch);
                    ModProjectLoader.SaveEntity(proj, "combats", cd.CombatId, cd, wasPatch);
                    _parent.SelectedCombatId = cd.CombatId;
                }
                else
                    cd.CombatId = prevCombatId;
            }
            cd.Description = EditorFields.TextArea("Description", cd.Description);
            cd.CombatTier = EditorFields.EnumField("Tier", cd.CombatTier, "enc_tier");

            // Background: game enum backgrounds + custom backgrounds from all mods
            {
                var bgNames = Enum.GetNames(typeof(Enums.CombatBackground)).ToList();
                // Collect custom backgrounds from all loaded mods (registered globally)
                var customBgIds = new List<string>();
                var activeProj = ModManagerPanel.ActiveProject;
                if (activeProj?.Backgrounds != null && activeProj.Backgrounds.Count > 0)
                    customBgIds.AddRange(activeProj.Backgrounds.Keys);
                // Also include backgrounds from other mods (already registered in DataHelper)
                foreach (var key in DataHelper.CustomBackgroundPrefabs.Keys)
                {
                    if (!customBgIds.Contains(key))
                        customBgIds.Add(key);
                }
                customBgIds.Sort();

                bool usingCustom = !string.IsNullOrEmpty(cd.CustomBackgroundId);
                string currentDisplay = usingCustom ? cd.CustomBackgroundId
                    : (Enum.GetName(typeof(Enums.CombatBackground), cd.Background) ?? "");

                // Build combined list: custom IDs first (prefixed), then game enum names
                var allBgIds = new List<string>();
                foreach (var cid in customBgIds)
                    allBgIds.Add("[Custom] " + cid);
                allBgIds.AddRange(bgNames);

                string displayVal = usingCustom ? "[Custom] " + cd.CustomBackgroundId : currentDisplay;
                string newBgName = EditorFields.IdDropdown("Background", displayVal, allBgIds,
                    "enc_bg", pickerMode: EntityPicker.Mode.Background);

                if (newBgName != displayVal)
                {
                    if (newBgName.StartsWith("[Custom] "))
                    {
                        cd.CustomBackgroundId = newBgName.Substring("[Custom] ".Length);
                    }
                    else
                    {
                        cd.CustomBackgroundId = "";
                        if (Enum.TryParse<Enums.CombatBackground>(newBgName, out var parsed))
                            cd.Background = parsed;
                    }
                }
            }

            cd.NpcRemoveInMadness0Index = EditorFields.IntField("Remove M0 idx", cd.NpcRemoveInMadness0Index, -1, 3);
            cd.HealHeroes = EditorFields.Toggle("Heal Heroes", cd.HealHeroes);
            cd.IsRift = EditorFields.Toggle("Is Rift", cd.IsRift);
            cd.NeverRandomizeEnemies = EditorFields.Toggle("Never Randomize", cd.NeverRandomizeEnemies);
            cd.RandomizeNpcPosition = EditorFields.Toggle("Random Positions", cd.RandomizeNpcPosition);
            cd.StepSound = EditorFields.EnumField("Step Sound", cd.StepSound, "enc_stepsnd");

            //  NPC Slots (always 4) 
            int filledCount = cd.NpcIds.Count(id => !string.IsNullOrEmpty(id));
            if (EditorFields.Section($"NPCs ({filledCount}/4)", ref _secNpcs))
            {
                EnsureFourSlots(cd);
                var npcIds = GetAllNpcIds();

                for (int i = 0; i < 4; i++)
                {
                    GUILayout.BeginHorizontal();

                    string current = cd.NpcIds[i];
                    bool isEmpty = string.IsNullOrEmpty(current);

                    // NPC dropdown with (none) option
                    string newVal = EditorFields.IdDropdown($"Slot {i}", current, npcIds,
                        $"enc_npc_{i}", pickerMode: EntityPicker.Mode.NPC);
                    if (newVal != current)
                    {
                        cd.NpcIds[i] = newVal ?? "";
                        GUI.changed = true;
                    }

                    // Inspect button (only when slot is filled)
                    if (!isEmpty)
                    {
                        if (GUILayout.Button("\u2192", UnknownMod.Editor.EditorStyles.MiniButton, GUILayout.Width(22)))
                            _parent.InspectNpc(cd.NpcIds[i]);
                    }

                    GUILayout.EndHorizontal();
                }
            }

            //  Combat Effects 
            if (EditorFields.Section($"Combat Effects ({cd.CombatEffects.Count})", ref _secEffects))
            {
                for (int i = 0; i < cd.CombatEffects.Count; i++)
                {
                    var eff = cd.CombatEffects[i];
                    GUILayout.BeginVertical(EditorStyles.CompactBox);
                    eff.AuraCurse = EditorFields.IdDropdown("AuraCurse", eff.AuraCurse, EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds), $"enc_eff_ac_{i}", pickerMode: EntityPicker.Mode.AuraCurse);
                    eff.Charges = EditorFields.IntFieldMin("Charges", eff.Charges, 0);
                    eff.Target = EditorFields.EnumField("Target", eff.Target, $"enc_eff_tgt_{i}");
                    if (GUILayout.Button("X", EditorStyles.MiniButton, GUILayout.Width(22)))
                    {
                        cd.CombatEffects.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndVertical();
                        break;
                    }
                    GUILayout.EndVertical();
                }
                if (GUILayout.Button("+ Combat Effect", EditorStyles.MiniButton))
                {
                    cd.CombatEffects.Add(new CombatEffectDef());
                    GUI.changed = true;
                }
            }

            //  Advanced 
            if (EditorFields.Section("Advanced", ref _secAdvanced))
            {
                var advNpcIds = GetAllNpcIds();
                cd.NpcToSummonOnKilledId = EditorFields.IdDropdown("Summon on Kill", cd.NpcToSummonOnKilledId, advNpcIds, "enc_summon", pickerMode: EntityPicker.Mode.NPC);

                var advEventIds = new List<string>();
                if (proj != null)
                {
                    advEventIds.AddRange(proj.Events.Keys);
                    advEventIds.AddRange(proj.EventPatches.Keys);
                }
                advEventIds.AddRange(EditorFields.CachedIds("event", DataHelper.GetAllEventIds));
                advEventIds = advEventIds.Distinct().OrderBy(k => k).ToList();
                cd.EventDataId = EditorFields.IdDropdown("Post-Combat Event", cd.EventDataId, advEventIds, "enc_postevt", pickerMode: EntityPicker.Mode.Event);
                cd.EventRequirementId = EditorFields.IdDropdown("Event Requirement", cd.EventRequirementId, DataHelper.GetAllEventRequirementIds(), "enc_evtreq", pickerMode: EntityPicker.Mode.Requirement);
            }

            //  Used by nodes 
            if (EditorFields.Section("Used by Nodes", ref _secUsedBy))
            {
                var usedByZone = ZoneEditingService.CurrentZone;
                if (usedByZone?.Nodes != null)
                {
                    foreach (var node in usedByZone.Nodes.Values)
                    {
                        if (node.CombatIds.Contains(cd.CombatId))
                        {
                            if (GUILayout.Button($"\u2192 {node.NodeId} ({node.NodeName})", UnknownMod.Editor.EditorStyles.LinkButton))
                                _parent.InspectNode(node.NodeId);
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PATCH BROWSER — list base-game combats to patch
        // ═══════════════════════════════════════════════════════════════

        private void DrawPatchBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Select a base-game combat to patch:</color>",
                EditorStyles.RichLabel);
            _patchFilter = EditorFields.TextField("Filter", _patchFilter);

            _patchBrowserScroll = GUILayout.BeginScrollView(_patchBrowserScroll, GUILayout.Height(180));
            string filterLow = (_patchFilter ?? "").ToLower();
            var allBaseIds = DataHelper.GetAllCombatIds();
            int shown = 0;
            foreach (var id in allBaseIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.Combats.ContainsKey(id) || proj.CombatPatches.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var baseCombat = DataHelper.GetExistingCombat(id);
                    CombatDef patch;
                    if (baseCombat != null)
                        patch = ZoneEditingService.SnapshotCombatDef(baseCombat);
                    else
                        patch = new CombatDef { CombatId = id };

                    proj.CombatPatches[id] = patch;
                    _parent.SelectedCombatId = id;
                    ModProjectLoader.SaveEntity(proj, "combats", id, patch, isPatch: true);
                    _showPatchBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
