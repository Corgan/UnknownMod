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

        /// <summary>
        /// Snapshot a base-game entity into a TDef for overriding.
        /// Returns null if the entity doesn't exist.
        /// </summary>
        protected abstract TDef SnapshotBaseEntity(string id);

        /// <summary>Draw all type-specific field sections for the selected entity.</summary>
        protected abstract void DrawAllSections(TDef def, ModProject proj);

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

            // 2. Entity selector
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
                $"{FolderName}_sel");
            if (sel != SelectedId)
                SelectedId = sel;

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
                ModProjectLoader.SaveEntity(proj, FolderName, def.EntityId, def);
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
                        ModProjectLoader.DeleteEntity(proj, FolderName, SelectedId, false);
                        SelectedId = allIds.FirstOrDefault(k => k != SelectedId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        patchDict.Remove(SelectedId);
                        ModProjectLoader.DeleteEntity(proj, FolderName, SelectedId, true);
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
                GUILayout.Label($"<i>Select a {TypeLabel.ToLower()} above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // 6. Draw type-specific sections
            DrawAllSections(d, proj);

            // 7. Auto-save on change
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, FolderName, d.EntityId, d, isPatch);
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
            var allBaseIds = GetAllBaseIds();
            int shown = 0;
            foreach (var id in allBaseIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (patchDict.ContainsKey(id) || newDict.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var def = SnapshotBaseEntity(id) ?? new TDef();
                    def.EntityId = id;
                    patchDict[id] = def;
                    SelectedId = id;
                    ModProjectLoader.SaveEntity(proj, FolderName, id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
