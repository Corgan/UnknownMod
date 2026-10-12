using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
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
    public partial class EncounterEditor : IEntityActions
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

        private readonly EntityHistory<CombatDef> _history;
        private RenameTracker _ren;
        private RenameTracker Ren => _ren ??= new RenameTracker("combat", (p, patch) => patch ? p.CombatPatches : p.Combats, (cur, orig, patch) => _history.Rename(cur, orig, patch));

        public EncounterEditor(ModEditor parent)
        {
            _parent = parent;
            _history = new EntityHistory<CombatDef>
            {
                NewDict = p => p.Combats,
                PatchDict = p => p.CombatPatches,
                Save = (p, d, isPatch) => ModProjectLoader.SaveEntity(p, "combats", d.CombatId, d, isPatch),
                DeleteFiles = (p, id, isPatch) => ModProjectLoader.DeleteEntity(p, "combats", id, isPatch),
                GetSelected = () => _parent.SelectedCombatId,
                Select = id => _parent.SelectedCombatId = id,
            };
        }

        private string _pendingRemoveId;

        public bool CanDuplicate
        {
            get
            {
                var p = ModManagerPanel.ActiveProject;
                string id = _parent.SelectedCombatId;
                return p != null && !string.IsNullOrEmpty(id) && (p.Combats.ContainsKey(id) || p.CombatPatches.ContainsKey(id));
            }
        }
        public bool CanUndo => _history.CanUndo;
        public bool CanRedo => _history.CanRedo;
        public void Run(EditorCommand command)
        {
            switch (command)
            {
                case EditorCommand.Duplicate: DuplicateSelected(); break;
                case EditorCommand.Undo: _history.Undo(); break;
                case EditorCommand.Redo: _history.Redo(); break;
            }
        }

        private void DuplicateSelected()
        {
            var proj = ModManagerPanel.ActiveProject;
            string id = _parent.SelectedCombatId;
            if (proj == null || string.IsNullOrEmpty(id)) return;
            CombatDef src;
            if (!proj.Combats.TryGetValue(id, out src)) proj.CombatPatches.TryGetValue(id, out src);
            if (src == null) return;

            var copy = JsonConvert.DeserializeObject<CombatDef>(JsonConvert.SerializeObject(src));
            string newId = id + "copy";
            int n = 2;
            while (proj.Combats.ContainsKey(newId) || proj.CombatPatches.ContainsKey(newId) || DataHelper.GetExistingCombat(newId) != null)
                newId = id + "copy" + n++;

            copy.CombatId = newId;
            proj.Combats[newId] = copy;
            _parent.SelectedCombatId = newId;

            ModProjectLoader.SaveEntity(proj, "combats", newId, copy);
            _history.RecordCreated(newId, false, "duplicate");
            proj.IsDirty = true;
            InlineIssues.Invalidate();
            EditorStatus.Toast("Duplicated " + id + " as " + newId);
        }

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
            EditorActions.Register(this);

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
                string newId = $"{ModProjectLoader.IdPrefix(proj.ModId)}_combat_{proj.Combats.Count}";
                int suffix = 0;
                while (proj.Combats.ContainsKey(newId) || proj.CombatPatches.ContainsKey(newId))
                    newId = $"{ModProjectLoader.IdPrefix(proj.ModId)}_combat_{++suffix}";

                var def = new CombatDef { CombatId = newId, Description = "New Encounter" };
                proj.Combats[newId] = def;
                _parent.SelectedCombatId = newId;
                ModProjectLoader.SaveEntity(proj, "combats", newId, def);
                _history.RecordCreated(newId, false, "new encounter");
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Patch Base \u25BE", EditorStyles.MiniButton, GUILayout.Width(100)))
                _showPatchBrowser = !_showPatchBrowser;

            if (!string.IsNullOrEmpty(_parent.SelectedCombatId))
            {
                bool isNew = proj.Combats.ContainsKey(_parent.SelectedCombatId);
                bool isPatch = proj.CombatPatches.ContainsKey(_parent.SelectedCombatId);
                if (GUILayout.Button(new GUIContent("\u25B6 Test", "Launch this fight in Practice mode"), EditorStyles.MiniButton, GUILayout.Width(55)))
                    _parent.OpenTestFight("combat", _parent.SelectedCombatId);
                if (GUILayout.Button(new GUIContent("Simulate", "Run this fight many times in the simulator"), EditorStyles.MiniButton, GUILayout.Width(65)))
                    _parent.OpenSimulator("combat", _parent.SelectedCombatId);
                if (GUILayout.Button(new GUIContent("Duplicate", "Copy this encounter (Ctrl+D)"), EditorStyles.MiniButton, GUILayout.Width(70)))
                    EditorActions.Pending = EditorCommand.Duplicate;
                if (isNew || isPatch)
                {
                    bool armed = _pendingRemoveId == _parent.SelectedCombatId;
                    string lbl = armed ? "Confirm?" : (isNew ? "Delete" : "Revert");
                    if (GUILayout.Button(lbl, EditorStyles.DangerButton, GUILayout.Width(70)))
                    {
                        if (!armed) _pendingRemoveId = _parent.SelectedCombatId;
                        else
                        {
                            string rid = _parent.SelectedCombatId;
                            _history.Delete(rid, !isNew, isNew ? "delete" : "revert");
                            _parent.SelectedCombatId = combatIds.FirstOrDefault(k => k != rid);
                            _pendingRemoveId = null;
                            proj.IsDirty = true;
                            InlineIssues.Invalidate();
                            EditorStatus.Toast((isNew ? "Deleted " : "Reverted ") + rid + " (Ctrl+Z to undo)");
                        }
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
            bool cdIsPatch = !proj.Combats.ContainsKey(cd.CombatId);
            _history.BeginEdit(cd.CombatId, cdIsPatch, cd);
            Ren.ApplyUnlessSelected(cd.CombatId);
            Ren.Draw();
            bool renamed = false;
            string prevCombatId = cd.CombatId;
            cd.CombatId = EditorFields.TextField("Combat ID", cd.CombatId);
            if (cd.CombatId != prevCombatId && !ModProjectLoader.IsSafeName(cd.CombatId))
                cd.CombatId = prevCombatId; // would not be a valid file name
            if (cd.CombatId != prevCombatId && !string.IsNullOrEmpty(cd.CombatId))
            {
                bool wasNew = proj.Combats.ContainsKey(prevCombatId);
                bool wasPatch = proj.CombatPatches.ContainsKey(prevCombatId);
                var dict = wasNew ? proj.Combats : wasPatch ? proj.CombatPatches : null;
                if (dict != null && !proj.Combats.ContainsKey(cd.CombatId) && !proj.CombatPatches.ContainsKey(cd.CombatId))
                {
                    dict.Remove(prevCombatId);
                    dict[cd.CombatId] = cd;
                    ModProjectLoader.RenameEntity(proj, "combats", prevCombatId, cd.CombatId, cd, wasPatch);
                    _parent.SelectedCombatId = cd.CombatId;
                    Ren.Note(proj, prevCombatId, cd.CombatId, wasPatch, ModReferences.FamilyOf(proj, dict));
                    _history.RecordRenamed(prevCombatId, cd.CombatId, wasPatch);
                    renamed = true;
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

            DrawComposer(cd, proj);

            //  Combat Effects 
            if (EditorFields.Section($"Combat Effects ({cd.CombatEffects.Count})", ref _secEffects))
            {
                for (int i = 0; i < cd.CombatEffects.Count; i++)
                {
                    var eff = cd.CombatEffects[i];
                    GUILayout.BeginVertical(EditorStyles.CompactBox);
                    var effOp = EditorFields.RowHeader($"<b>#{i + 1}</b>  {eff.AuraCurse}", i, cd.CombatEffects.Count);
                    eff.AuraCurse = EditorFields.IdDropdown("AuraCurse", eff.AuraCurse, EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds), $"enc_eff_ac_{i}", pickerMode: EntityPicker.Mode.AuraCurse);
                    eff.Charges = EditorFields.IntFieldMin("Charges", eff.Charges, 0);
                    eff.Target = EditorFields.EnumField("Target", eff.Target, $"enc_eff_tgt_{i}");
                    bool effChanged = EditorFields.ApplyRowOp(cd.CombatEffects, i, effOp);
                    GUILayout.EndVertical();
                    if (effChanged) break;
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
                cd.EventRequirementId = EditorFields.IdDropdown("Event Requirement", cd.EventRequirementId, RefIds.Requirements(), "enc_evtreq", pickerMode: EntityPicker.Mode.Requirement);
                cd.CinematicId = EditorFields.IdDropdown("Cinematic", cd.CinematicId, RefIds.Cinematics(), "enc_cine");
                cd.CombatBasedCorruptionCardId = EditorFields.IdDropdown("Combat Corruption Card", cd.CombatBasedCorruptionCardId, EditorFields.BuildCardIdList(proj), "enc_corrcard", pickerMode: EntityPicker.Mode.Card);
                cd.ThermometerTierId = EditorFields.IdDropdown("Thermometer Tier", cd.ThermometerTierId, RefIds.ThermometerTiers(proj), "enc_thermo");
                cd.CombatMusic = AssetFields.AudioField("Combat Music", cd.CombatMusic, "enc_music");
            }

            DrawWaveGenerator(cd, proj);

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

            if (!renamed) _history.EndEdit(cd.CombatId, cdIsPatch, cd);
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
                    _history.RecordCreated(id, true, "patch");
                    _showPatchBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
