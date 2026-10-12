using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;
using Newtonsoft.Json;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Base class for mod-project entity editors. Handles ~150 lines of
    /// boilerplate per editor: DrawPanel shell, entity selector,
    /// new/override/delete action bar, override browser, and auto-save.
    /// Subclasses only implement DrawAllSections and metadata overrides.
    /// </summary>
    public abstract class ModProjectEditorBase<TDef> : IEntityActions where TDef : class, IModEntity, new()
    {
        protected readonly ModEditor Parent;

        // ── Override browser state ─────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Selected ID ────────────────────────────────────────
        /// <summary>
        /// Currently selected entity ID. Override to delegate to ModEditor's
        /// SelectedXxxId properties for tab-switching / Inspect* integration.
        /// </summary>
        public virtual string SelectedId { get; set; }

        // ── Abstract: identity / metadata ──────────────────────

        /// <summary>Human-readable type label (e.g. "Item", "Card").</summary>
        protected abstract string TypeLabel { get; }

        /// <summary>Folder name for save/load (e.g. "items", "cards").</summary>
        protected abstract string FolderName { get; }

        /// <summary>Suffix for new entity IDs (e.g. "_new_item"). Prefixed with ModId.</summary>
        protected abstract string NewIdSuffix { get; }

        /// <summary>Lets editors clean the mod-id prefix used for generated ids (cards must not contain '_').</summary>
        protected virtual string SanitizeNewId(string modId) => ModProjectLoader.IdPrefix(modId);

        // ── Abstract: dict accessors ───────────────────────────

        protected abstract Dictionary<string, TDef> GetNewDict(ModProject proj);
        protected abstract Dictionary<string, TDef> GetPatchDict(ModProject proj);

        // ── Abstract: entity operations ────────────────────────

        /// <summary>Create a default entity with the given ID.</summary>
        protected abstract TDef CreateDefault(string id, ModProject proj);

        /// <summary>Return a display name for the entity selector list.</summary>
        protected abstract string GetDisplayName(TDef def);

        /// <summary>Return sorted list of all base-game IDs for the override browser.</summary>
        protected virtual List<string> GetAllBaseIds()
        {
            var info = EntityTypeRegistry.Get<TDef>();
            return info?.GetAllBaseIds?.Invoke() ?? new List<string>();
        }

        // ── Cache for GetAllBaseIds ────────────────────────────
        // Rebuilt per key change or every 2s (not per frame): the card version walks every game card.
        private List<string> _pickerIds;
        private List<string> _pickerBase;
        private int _pickerSig;

        private List<string> _cachedBaseIds;
        private string _cachedBaseIdsKey;
        private float _cachedBaseIdsTime = -100f;

        /// <summary>Changes when the filter behind GetAllBaseIds changes (e.g. the active sub-tab).</summary>
        protected virtual string BaseIdsCacheKey => "";

        private List<string> GetCachedBaseIds()
        {
            string key = BaseIdsCacheKey;
            float now = Time.realtimeSinceStartup;
            if (_cachedBaseIds == null || key != _cachedBaseIdsKey || now - _cachedBaseIdsTime > 2f)
            {
                _cachedBaseIds = GetAllBaseIds();
                _cachedBaseIdsKey = key;
                _cachedBaseIdsTime = now;
            }
            return _cachedBaseIds;
        }

        /// <summary>
        /// Snapshot a base-game entity into a TDef for overriding.
        /// Returns null if the entity doesn't exist.
        /// </summary>
        protected abstract TDef SnapshotBaseEntity(string id);

        /// <summary>
        /// EntityPicker mode for the visual browse grid. Override to enable the
        /// browse button on the entity selector. Return null for text-only types.
        /// </summary>
        protected virtual EntityPicker.Mode? PickerMode => null;

        /// <summary>
        /// When true, selecting a base-game ID auto-snapshots it as a patch.
        /// Override to false for editors that want to browse before committing.
        /// </summary>
        protected virtual bool AutoSnapshotOnSelect => true;

        /// <summary>Draw all type-specific field sections for the selected entity.</summary>
        protected abstract void DrawAllSections(TDef def, ModProject proj);

        /// <summary>
        /// Called when SelectedId is a base-game entity not in the project dictionaries
        /// and AutoSnapshotOnSelect is false. Override to show a browsing UI.
        /// </summary>
        protected virtual void DrawGameEntityBrowser(ModProject proj, string id) { }

        /// <summary>Filter the entity list before display. Override to hide entries
        /// (e.g. NpcEditor hides NPC variants that appear as tabs on their base entry).</summary>
        protected virtual List<string> FilterEntityList(List<string> allIds, ModProject proj) => allIds;

        /// <summary>Save an entity to disk. Override to customize folder routing.</summary>
        protected virtual void OnSaveEntity(ModProject proj, string id, TDef def, bool isPatch = false)
            => ModProjectLoader.SaveEntity(proj, FolderName, id, def, isPatch);

        /// <summary>Called after Duplicate copies an entity and gives the copy its new id. Override to fix up id-derived fields.</summary>
        protected virtual void OnDuplicated(TDef copy, string newId, ModProject proj) { }

        /// <summary>Delete an entity from disk. Override to customize folder routing.</summary>
        protected virtual void OnDeleteEntity(ModProject proj, string id, bool isPatch)
            => ModProjectLoader.DeleteEntity(proj, FolderName, id, isPatch);

        // ═══════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════

        protected ModProjectEditorBase(ModEditor parent)
        {
            Parent = parent;
            History = new EntityHistory<TDef>
            {
                NewDict = GetNewDict,
                PatchDict = GetPatchDict,
                Save = (proj, def, isPatch) => OnSaveEntity(proj, def.EntityId, def, isPatch),
                DeleteFiles = (proj, id, isPatch) => OnDeleteEntity(proj, id, isPatch),
                GetSelected = () => SelectedId,
                Select = id => SelectedId = id,
            };
        }

        // ═══════════════════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════════════════

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null)
            {
                GUILayout.Label("<color=#888>No mod project active. Open the Mods tab first.</color>",
                    EditorStyles.RichLabel);
                return;
            }
            EditorActions.Register(this);
            DrawModProjectPanel(proj);
        }

        // ═══════════════════════════════════════════════════════
        //  SHELL COMMANDS (Duplicate / Undo / Redo)
        // ═══════════════════════════════════════════════════════

        /// <summary>Undo/redo for this editor: edits to entries, and create / delete / override / rename.</summary>
        public readonly EntityHistory<TDef> History;

        public bool CanUndo => History.CanUndo;
        public bool CanRedo => History.CanRedo;

        public bool CanDuplicate
        {
            get
            {
                var proj = ModManagerPanel.ActiveProject;
                return proj != null && !string.IsNullOrEmpty(SelectedId)
                    && (GetNewDict(proj).ContainsKey(SelectedId) || GetPatchDict(proj).ContainsKey(SelectedId));
            }
        }

        public void Run(EditorCommand command)
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) return;
            switch (command)
            {
                case EditorCommand.Duplicate: DuplicateSelected(proj); break;
                case EditorCommand.Undo: History.Undo(); break;
                case EditorCommand.Redo: History.Redo(); break;
            }
        }

        /// <summary>Called before the selected entry's fields are drawn. Editors whose undo unit is bigger than the entry (cards + variants) override this.</summary>
        protected virtual void BeginTrack(ModProject proj, TDef d, bool isPatch) => History.BeginEdit(d.EntityId, isPatch, d);

        /// <summary>Called after the fields are drawn; records an undo step if something changed.</summary>
        protected virtual void EndTrack(ModProject proj, TDef d, bool isPatch) => History.EndEdit(d.EntityId, isPatch, d);

        /// <summary>Runs <paramref name="op"/> (which adds an entry) and records it so it can be undone.</summary>
        protected virtual void RunCreate(ModProject proj, string id, bool isPatch, string label, Action op)
        {
            op();
            History.RecordCreated(id, isPatch, label);
            InlineIssues.Invalidate();
        }

        /// <summary>Removes an entry (and anything it owns) and records it so it can be undone.</summary>
        protected virtual void DeleteEntry(ModProject proj, string id, bool isPatch, string label)
        {
            var dict = isPatch ? GetPatchDict(proj) : GetNewDict(proj);
            if (!dict.TryGetValue(id, out var def)) return;
            string state = EntityHistory<TDef>.StateOf(def);
            bool changed = GUI.changed;
            History.Remove(id, isPatch);
            GUI.changed = changed;
            History.RecordDeleted(id, isPatch, state, label);
            InlineIssues.Invalidate();
        }

        /// <summary>True when <paramref name="id"/> is unusable for a copy because the editor derives other ids from it (e.g. card upgrade variants).</summary>
        protected virtual bool IdCollides(string id, ModProject proj) => false;

        /// <summary>Copies the selected entry under a new id ({id}copy, {id}copy2, ...; no underscore so card ids stay valid).</summary>
        private void DuplicateSelected(ModProject proj)
        {
            var newDict = GetNewDict(proj);
            var patchDict = GetPatchDict(proj);
            if (string.IsNullOrEmpty(SelectedId)) return;
            if (!newDict.TryGetValue(SelectedId, out var source) && !patchDict.TryGetValue(SelectedId, out source)) return;

            var baseIds = new HashSet<string>(GetCachedBaseIds(), StringComparer.OrdinalIgnoreCase);
            string baseId = source.EntityId;
            string newId = baseId + "copy";
            int n = 2;
            while (newDict.ContainsKey(newId) || patchDict.ContainsKey(newId) || baseIds.Contains(newId) || IdCollides(newId, proj)) newId = baseId + "copy" + n++;

            var copy = JsonConvert.DeserializeObject<TDef>(EntityHistory<TDef>.StateOf(source));
            if (copy == null) return;
            copy.EntityId = newId;
            RunCreate(proj, newId, false, "duplicate", () =>
            {
                OnDuplicated(copy, newId, proj);
                newDict[newId] = copy;
                SelectedId = newId;
                OnSaveEntity(proj, newId, copy, false);
            });
            proj.IsDirty = true;
            proj.LastChangeTime = Time.realtimeSinceStartup;
            GUI.changed = true;
            EditorStatus.Toast($"Duplicated {baseId} \u2192 {newId}");
        }

        // ── Recent entries ─────────────────────────────────────
        private readonly List<string> _recent = new List<string>();
        private readonly List<string> _recentShown = new List<string>();
        private const int MaxRecent = 8;
        private const int ShownRecent = 4;

        /// <summary>Layout-time bookkeeping: remember the current selection and decide which recent entries to show this frame.</summary>
        private void UpdateRecents(ModProject proj, Dictionary<string, TDef> newDict, Dictionary<string, TDef> patchDict)
        {
            if (Event.current.type != EventType.Layout) return;
            if (!string.IsNullOrEmpty(SelectedId) && (newDict.ContainsKey(SelectedId) || patchDict.ContainsKey(SelectedId)))
            {
                _recent.Remove(SelectedId);
                _recent.Insert(0, SelectedId);
                if (_recent.Count > MaxRecent) _recent.RemoveAt(_recent.Count - 1);
            }
            _recentShown.Clear();
            foreach (var id in _recent)
            {
                if (id == SelectedId || !(newDict.ContainsKey(id) || patchDict.ContainsKey(id))) continue;
                _recentShown.Add(id);
                if (_recentShown.Count >= ShownRecent) break;
            }
        }

        private void DrawRecents()
        {
            if (_recentShown.Count == 0) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#777>Recent</color>", EditorStyles.RichLabel, GUILayout.Width(46));
            foreach (var id in _recentShown)
            {
                string shown = id.Length > 16 ? id.Substring(0, 15) + "\u2026" : id;
                if (GUILayout.Button(new GUIContent(shown, id), EditorStyles.MiniButton)) SelectedId = id;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static string NeighborOf(List<string> ids, string id)
        {
            int i = ids.IndexOf(id);
            if (i < 0) return ids.Count > 0 ? ids[0] : null;
            if (i + 1 < ids.Count) return ids[i + 1];
            return i > 0 ? ids[i - 1] : null;
        }

        // ── Inline validation ──────────────────────────────────
        private List<DiagMessage> _issuesShown = new List<DiagMessage>();
        private string _issuesShownFor;

        private void UpdateIssues(ModProject proj)
        {
            if (Event.current.type != EventType.Layout) return;
            InlineIssues.Refresh(proj);
            _issuesShown = InlineIssues.For(SelectedId);
            _issuesShownFor = SelectedId;
        }

        private void DrawIssues()
        {
            if (_issuesShown.Count == 0 || _issuesShownFor != SelectedId) return;
            int errors = _issuesShown.FindAll(m => m.Severity == DiagSeverity.Error).Count;
            string head = errors > 0 ? $"<color=#ff7777>\u26A0 {errors} error(s)</color>" : "";
            int warns = _issuesShown.Count - errors;
            if (warns > 0) head += (errors > 0 ? "  " : "") + $"<color=#ffcc66>\u26A0 {warns} warning(s)</color>";
            GUILayout.Label(head + " <color=#777>on this entry (see Mods tab > Validate for the whole mod)</color>", EditorStyles.RichLabel);
            int shown = Mathf.Min(_issuesShown.Count, 6);
            for (int i = 0; i < shown; i++)
            {
                var m = _issuesShown[i];
                string color = m.Severity == DiagSeverity.Error ? "#ff9999" : "#ffdd99";
                GUILayout.Label($"<color={color}>  \u2022 {InlineIssues.ShortText(m, _issuesShownFor)}</color>", EditorStyles.RichLabel);
            }
            if (_issuesShown.Count > shown) GUILayout.Label($"<color=#888>  \u2026and {_issuesShown.Count - shown} more</color>", EditorStyles.RichLabel);
        }

        public bool HandleChanges()
        {
            return GUI.changed && !string.IsNullOrEmpty(SelectedId);
        }

        // ═══════════════════════════════════════════════════════
        //  SHARED BOILERPLATE
        // ═══════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            var newDict = GetNewDict(proj);
            var patchDict = GetPatchDict(proj);

            // 1. Build combined entity list
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in newDict.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in patchDict.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // 1b. Track all project IDs before filtering for picker dedup
            var projectIdSet = new HashSet<string>(allIds);

            // 1c. Filter display list (subclass hook, e.g. NpcEditor hides variants)
            allIds = FilterEntityList(allIds, proj);

            // 2. Build picker list: project entities first, then all base-game IDs
            List<string> pickerIds = null;
            if (PickerMode.HasValue)
            {
                var baseIds = GetCachedBaseIds();
                int sig = allIds.Count;
                foreach (var id in allIds) sig = sig * 31 + id.GetHashCode();
                if (_pickerIds == null || !ReferenceEquals(baseIds, _pickerBase) || sig != _pickerSig)
                {
                    _pickerIds = new List<string>(allIds);
                    foreach (var baseId in baseIds)
                    {
                        if (!projectIdSet.Contains(baseId))
                            _pickerIds.Add(baseId);
                    }
                    _pickerBase = baseIds;
                    _pickerSig = sig;
                }
                pickerIds = _pickerIds;
            }

            UpdateRecents(proj, newDict, patchDict);
            UpdateIssues(proj);

            // 3. Entity selector
            string sel = EditorFields.EntitySelector(
                SelectedId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (newDict.TryGetValue(id, out var nd))
                        name = GetDisplayName(nd);
                    else if (patchDict.TryGetValue(id, out var pd))
                        name = GetDisplayName(pd);
                    return $"{badge} {id}  {name}";
                },
                $"{FolderName}_sel",
                PickerMode,
                pickerIds);

            // Auto-override: if picker returned a base-game ID not in the project, snapshot it
            if (sel != SelectedId)
            {
                if (!string.IsNullOrEmpty(sel) && !newDict.ContainsKey(sel) && !patchDict.ContainsKey(sel)
                    && AutoSnapshotOnSelect)
                {
                    var snapshot = SnapshotBaseEntity(sel);
                    if (snapshot != null)
                    {
                        snapshot.EntityId = sel;
                        RunCreate(proj, sel, true, "override", () =>
                        {
                            patchDict[sel] = snapshot;
                            ModProjectLoader.SaveEntity(proj, FolderName, sel, snapshot, true);
                        });
                        proj.IsDirty = true;
                    }
                }
                SelectedId = sel;
            }

            // 3. Action bar
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("+ New", $"Create a new {TypeLabel.ToLower()} in this mod"), EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string idPrefix = SanitizeNewId(proj.ModId);
                string newId = $"{idPrefix}{NewIdSuffix}";
                int suffix = 1;
                while (newDict.ContainsKey(newId) || patchDict.ContainsKey(newId))
                    newId = $"{idPrefix}{NewIdSuffix}{suffix++}";
                var def = CreateDefault(newId, proj);
                RunCreate(proj, newId, false, "new", () =>
                {
                    newDict[newId] = def;
                    SelectedId = newId;
                    OnSaveEntity(proj, def.EntityId, def);
                });
                proj.IsDirty = true;
            }

            if (GUILayout.Button(new GUIContent("Override \u25BE", $"Pick a base-game {TypeLabel.ToLower()} to change; only your changes are saved"), EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            bool hasSelection = !string.IsNullOrEmpty(SelectedId) && (newDict.ContainsKey(SelectedId) || patchDict.ContainsKey(SelectedId));
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && hasSelection;
            if (GUILayout.Button(new GUIContent("Duplicate", "Copy this entry as a new one (Ctrl+D)"), EditorStyles.MiniButton, GUILayout.Width(70)))
                EditorActions.Pending = EditorCommand.Duplicate;
            if (GUILayout.Button(new GUIContent("JSON", "Open this entry's file in your JSON editor; saving there hot-reloads it"), EditorStyles.MiniButton, GUILayout.Width(44)))
                OpenExternal(proj, newDict, patchDict, false);
            if (GUILayout.Button(new GUIContent("Show", "Show this entry's file in Explorer"), EditorStyles.MiniButton, GUILayout.Width(44)))
                OpenExternal(proj, newDict, patchDict, true);
            GUI.enabled = wasEnabled;

            if (!string.IsNullOrEmpty(SelectedId))
            {
                bool isNew = newDict.ContainsKey(SelectedId);
                bool isOvr = patchDict.ContainsKey(SelectedId);
                if (isNew)
                {
                    if (DeleteButton("Delete", proj, newDict))
                    {
                        RenameFlush.FlushAll();
                        string removed = SelectedId;
                        string next = NeighborOf(allIds, removed);
                        DeleteEntry(proj, removed, false, "delete");
                        SelectedId = next;
                        EditorStatus.Toast($"Deleted {removed} (Ctrl+Z to undo)");
                    }
                }
                else if (isOvr)
                {
                    bool armed = _pendingRevertId == SelectedId;
                    var revertLabel = new GUIContent(armed ? "Confirm?" : "Revert", "Discard your changes to this base-game entry and go back to the original");
                    if (GUILayout.Button(revertLabel, EditorStyles.DangerButton, GUILayout.Width(armed ? 70 : 60)))
                    {
                        if (!armed) _pendingRevertId = SelectedId;
                        else
                        {
                            RenameFlush.FlushAll();
                            string removed = SelectedId;
                            string next = NeighborOf(allIds, removed);
                            _pendingRevertId = null;
                            DeleteEntry(proj, removed, true, "revert");
                            SelectedId = next;
                            EditorStatus.Toast($"Reverted {removed} to the base game (Ctrl+Z to undo)");
                        }
                    }
                }
            }

            GUILayout.EndHorizontal();

            DrawRecents();
            Ren.ApplyUnlessSelected(SelectedId);
            Ren.Draw();
            DrawUsedBy(proj, newDict, patchDict);
            DrawIssues();

            // 4. Override browser
            if (_showOverrideBrowser)
                DrawOverrideBrowser(proj, newDict, patchDict);

            EditorStyles.Separator();

            // 5. Resolve selected def
            TDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedId))
            {
                if (newDict.TryGetValue(SelectedId, out d))
                    isPatch = false;
                else if (patchDict.TryGetValue(SelectedId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                if (!AutoSnapshotOnSelect && !string.IsNullOrEmpty(SelectedId))
                {
                    DrawGameEntityBrowser(proj, SelectedId);
                    return;
                }
                GUILayout.Label($"<i>Select a {TypeLabel.ToLower()} above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // 6. Draw type-specific sections
            string prevEntityId = d.EntityId;
            BeginTrack(proj, d, isPatch);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Expand all", "Open every section"), EditorStyles.MiniButton, GUILayout.Width(80))) EditorFields.QueueSectionCommand(1);
            if (GUILayout.Button(new GUIContent("Collapse all", "Close every section"), EditorStyles.MiniButton, GUILayout.Width(80))) EditorFields.QueueSectionCommand(-1);
            GUILayout.EndHorizontal();

            DrawAllSections(d, proj);

            // 7. Auto-save on change
            if (GUI.changed)
            {
                // Handle ID rename: re-key dictionary, delete old file
                var dict = isPatch ? patchDict : newDict;
                var otherDict = isPatch ? newDict : patchDict;
                // An empty id or one that another entity owns would overwrite or orphan data: keep the old id
                if (d.EntityId != prevEntityId
                    && (!ModProjectLoader.IsSafeName(d.EntityId) || dict.ContainsKey(d.EntityId) || otherDict.ContainsKey(d.EntityId)))
                    d.EntityId = prevEntityId;
                // Delete the old file only after the new one is written; a case-only rename is the same file on Windows, so it is overwritten in place
                bool deleteOldAfterSave = false;
                if (d.EntityId != prevEntityId)
                {
                    dict.Remove(prevEntityId);
                    dict[d.EntityId] = d;
                    deleteOldAfterSave = !string.Equals(prevEntityId, d.EntityId, StringComparison.OrdinalIgnoreCase);
                    SelectedId = d.EntityId;
                    Ren.Note(proj, prevEntityId, d.EntityId, isPatch, ModReferences.FamilyOf(proj, dict));
                    _usedByTime = 0f;
                    History.RecordRenamed(prevEntityId, d.EntityId, isPatch);
                }
                else
                    EndTrack(proj, d, isPatch);
                OnSaveEntity(proj, d.EntityId, d, isPatch);
                if (deleteOldAfterSave) OnDeleteEntity(proj, prevEntityId, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ── Reference safety ───────────────────────────────────

        private RenameTracker _ren;
        private RenameTracker Ren => _ren ??= new RenameTracker(TypeLabel,
            (p, patch) => patch ? (System.Collections.IDictionary)GetPatchDict(p) : GetNewDict(p),
            (cur, orig, patch) => History.Rename(cur, orig, patch));

        private void OpenExternal(ModProject proj, Dictionary<string, TDef> newDict, Dictionary<string, TDef> patchDict, bool reveal)
        {
            RenameFlush.FlushAll();
            bool patch = !newDict.ContainsKey(SelectedId ?? "") && patchDict.ContainsKey(SelectedId ?? "");
            string file = ExternalEditor.FindEntityFile(proj.ModId, FolderName, SelectedId, patch);
            if (file == null) { EditorStatus.Toast("No file on disk yet for " + SelectedId); return; }
            bool ok = reveal ? ExternalEditor.Reveal(file) : ExternalEditor.OpenFile(file);
            if (!ok) EditorStatus.Toast("Could not open " + file);
        }

        private string _pendingDeleteId;
        private string _pendingRevertId;
        private bool _foldUsedBy;
        private string _usedByKey;
        private float _usedByTime;
        private List<RefHit> _usedBy = new List<RefHit>();

        private List<RefHit> UsagesOfSelected(ModProject proj, Dictionary<string, TDef> newDict, Dictionary<string, TDef> patchDict, bool force = false)
        {
            string key = proj.ModId + "|" + SelectedId;
            if (force || key != _usedByKey || Time.realtimeSinceStartup - _usedByTime > 2f)
            {
                _usedByKey = key;
                _usedByTime = Time.realtimeSinceStartup;
                var dict = newDict.ContainsKey(SelectedId ?? "") ? newDict : patchDict;
                _usedBy = ModReferences.FindUsages(proj, SelectedId, ModReferences.FamilyOf(proj, dict), SelectedId);
            }
            return _usedBy;
        }

        /// <summary>Two-step delete: when other entities reference this one, the first click warns and the second confirms.</summary>
        private bool DeleteButton(string label, ModProject proj, Dictionary<string, TDef> ownDict)
        {
            var uses = UsagesOfSelected(proj, GetNewDict(proj), GetPatchDict(proj));
            bool armed = _pendingDeleteId == SelectedId;
            string text = uses.Count == 0 ? label : armed ? "Confirm" : $"{label} ({uses.Count})";
            if (!GUILayout.Button(text, EditorStyles.DangerButton, GUILayout.Width(uses.Count == 0 ? 60 : 90)))
                return false;
            if (uses.Count == 0 || armed) { _pendingDeleteId = null; return true; }
            _pendingDeleteId = SelectedId;
            _foldUsedBy = true;
            return false;
        }

        private void DrawUsedBy(ModProject proj, Dictionary<string, TDef> newDict, Dictionary<string, TDef> patchDict)
        {
            if (string.IsNullOrEmpty(SelectedId)) return;
            if (_pendingDeleteId != null && _pendingDeleteId != SelectedId) _pendingDeleteId = null;
            if (_pendingRevertId != null && _pendingRevertId != SelectedId) _pendingRevertId = null;
            var uses = UsagesOfSelected(proj, newDict, patchDict);
            string head = (_foldUsedBy ? "\u25BC" : "\u25B6") + $" Used by ({uses.Count})";
            if (GUILayout.Button(head, EditorStyles.MiniButton, GUILayout.Width(110))) _foldUsedBy = !_foldUsedBy;
            if (_pendingDeleteId == SelectedId)
                GUILayout.Label($"<color=#ffaa44>{uses.Count} reference(s) will dangle if you delete this. Click Confirm to delete anyway.</color>", EditorStyles.RichLabel);
            if (!_foldUsedBy) return;
            if (uses.Count == 0)
            {
                GUILayout.Label("<color=#888>Not referenced by anything in this mod.</color>", EditorStyles.RichLabel);
                return;
            }
            int shown = Mathf.Min(uses.Count, 40);
            for (int i = 0; i < shown; i++)
            {
                var hit = uses[i];
                string text = $"<color=#aaa>{hit.Family}</color> {hit.OwnerId} <color=#888>\u2192 {hit.Field}</color>";
                if (ModEditor.CanInspectFamily(hit.Family))
                {
                    if (GUILayout.Button(new GUIContent($"\u2192 {hit.Family}: {hit.OwnerId}  ({hit.Field})", "Open " + hit.OwnerId), EditorStyles.LinkButton))
                    {
                        Parent.InspectFamily(hit.Family, hit.OwnerId);
                        break;
                    }
                }
                else
                    GUILayout.Label(text, EditorStyles.RichLabel);
            }
            if (uses.Count > shown) GUILayout.Label($"<color=#888>\u2026and {uses.Count - shown} more</color>", EditorStyles.RichLabel);
        }

        private void DrawOverrideBrowser(ModProject proj,
            Dictionary<string, TDef> newDict, Dictionary<string, TDef> patchDict)
        {
            GUILayout.Label($"<color=#aaa>Search base-game {TypeLabel.ToLower()}s to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allBaseIds = GetCachedBaseIds();
            int shown = 0;
            foreach (var id in allBaseIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.ToLower().Contains(filterLow)) continue;
                if (patchDict.ContainsKey(id) || newDict.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var def = SnapshotBaseEntity(id) ?? new TDef();
                    def.EntityId = id;
                    RunCreate(proj, id, true, "override", () =>
                    {
                        patchDict[id] = def;
                        SelectedId = id;
                        OnSaveEntity(proj, id, def, true);
                    });
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
