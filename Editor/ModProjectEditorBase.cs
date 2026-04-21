using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Base class for mod-project entity editors. Handles ~150 lines of
    /// boilerplate per editor: DrawPanel shell, entity selector,
    /// new/override/delete action bar, override browser, and auto-save.
    /// Subclasses only implement DrawAllSections and metadata overrides.
    /// </summary>
    public abstract class ModProjectEditorBase<TDef> where TDef : class, IModEntity, new()
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

        // ── Per-frame cache for GetAllBaseIds ──────────────────
        private List<string> _cachedBaseIds;
        private int _cachedBaseIdsFrame = -1;

        private List<string> GetCachedBaseIds()
        {
            int frame = Time.frameCount;
            if (_cachedBaseIds == null || frame != _cachedBaseIdsFrame)
            {
                _cachedBaseIds = GetAllBaseIds();
                _cachedBaseIdsFrame = frame;
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

        /// <summary>Delete an entity from disk. Override to customize folder routing.</summary>
        protected virtual void OnDeleteEntity(ModProject proj, string id, bool isPatch)
            => ModProjectLoader.DeleteEntity(proj, FolderName, id, isPatch);

        // ═══════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════

        protected ModProjectEditorBase(ModEditor parent)
        {
            Parent = parent;
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
            DrawModProjectPanel(proj);
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
                pickerIds = new List<string>(allIds);
                foreach (var baseId in baseIds)
                {
                    if (!projectIdSet.Contains(baseId))
                        pickerIds.Add(baseId);
                }
            }

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
                        patchDict[sel] = snapshot;
                        ModProjectLoader.SaveEntity(proj, FolderName, sel, snapshot, true);
                        proj.IsDirty = true;
                    }
                }
                SelectedId = sel;
            }

            // 3. Action bar
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}{NewIdSuffix}";
                int suffix = 1;
                while (newDict.ContainsKey(newId) || patchDict.ContainsKey(newId))
                    newId = $"{proj.ModId}{NewIdSuffix}{suffix++}";
                var def = CreateDefault(newId, proj);
                newDict[newId] = def;
                SelectedId = newId;
                OnSaveEntity(proj, def.EntityId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            if (!string.IsNullOrEmpty(SelectedId))
            {
                bool isNew = newDict.ContainsKey(SelectedId);
                bool isOvr = patchDict.ContainsKey(SelectedId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        newDict.Remove(SelectedId);
                        OnDeleteEntity(proj, SelectedId, false);
                        SelectedId = allIds.FirstOrDefault(k => k != SelectedId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        patchDict.Remove(SelectedId);
                        OnDeleteEntity(proj, SelectedId, true);
                        SelectedId = allIds.FirstOrDefault(k => k != SelectedId);
                        proj.IsDirty = true;
                    }
                }
            }

            GUILayout.EndHorizontal();

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
            DrawAllSections(d, proj);

            // 7. Auto-save on change
            if (GUI.changed)
            {
                // Handle ID rename: re-key dictionary, delete old file
                if (d.EntityId != prevEntityId && !string.IsNullOrEmpty(d.EntityId))
                {
                    var dict = isPatch ? patchDict : newDict;
                    var otherDict = isPatch ? newDict : patchDict;
                    if (otherDict.ContainsKey(d.EntityId))
                    {
                        d.EntityId = prevEntityId; // collision with other dict, revert
                    }
                    else
                    {
                        dict.Remove(prevEntityId);
                        dict[d.EntityId] = d;
                        OnDeleteEntity(proj, prevEntityId, isPatch);
                        SelectedId = d.EntityId;
                    }
                }
                OnSaveEntity(proj, d.EntityId, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
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
                    patchDict[id] = def;
                    SelectedId = id;
                    OnSaveEntity(proj, id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
