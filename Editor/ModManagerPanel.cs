using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Mod Manager tab: mod selector, metadata editor, load order management.
    /// </summary>
    public class ModManagerPanel
    {
        private readonly ModEditor _editor;
        private Vector2 _loadOrderScroll;
        private string _newModId = "";
        private string _newModName = "";
        private string _newModAuthor = "";
        private bool _foldSelector = true;
        private bool _foldMetadata = true;
        private bool _foldLoadOrder = true;
        private bool _foldCreateNew = false;
        private bool _foldRebuild = true;
        private bool _foldValidate = true;
        private System.Collections.Generic.List<DiagMessage> _refReport;

        // ── Active mod project ───────────────────────────────────────
        public static ModProject ActiveProject { get; private set; }

        public ModManagerPanel(ModEditor editor) => _editor = editor;

        /// <summary>Per-frame tick (no-op for mod manager tab).</summary>
        public void Tick() { }

        /// <summary>Draw a viewport for the mod manager tab (blank, or mod logo).</summary>
        public void DrawViewport(Rect rect)
        {
            EditorStyles.ViewportBackground(rect);
            string modName = ActiveProject != null ? ActiveProject.ModName : null;
            string label = !string.IsNullOrEmpty(modName)
                ? $"<size=18><b>{modName}</b></size>\n<size=11><color=#666>Mod Manager</color></size>"
                : "<color=#555>No mod loaded</color>";
            EditorStyles.ViewportPlaceholder(rect, label);
        }

        // ═══════════════════════════════════════════════════════════════
        //  DRAW
        // ═══════════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            if (EditorFields.Section("MOD SELECTOR", ref _foldSelector))
                DrawModSelector();
            GUILayout.Space(8);

            if (ActiveProject != null)
            {
                if (EditorFields.Section("METADATA", ref _foldMetadata))
                    DrawMetadata();
                GUILayout.Space(8);
            }

            if (EditorFields.Section("LOAD ORDER", ref _foldLoadOrder))
                DrawLoadOrder();
            GUILayout.Space(8);

            if (EditorFields.Section("CREATE NEW MOD", ref _foldCreateNew))
                DrawCreateNew();
            GUILayout.Space(8);

            if (ActiveProject != null)
            {
                if (EditorFields.Section("VALIDATE", ref _foldValidate))
                    DrawValidate();
                GUILayout.Space(8);
            }

            if (EditorFields.Section("REBUILD", ref _foldRebuild))
                DrawRebuild();
        }

        // ── Mod Selector ─────────────────────────────────────────────

        private void DrawModSelector()
        {
            var mods = ModProjectLoader.DiscoverMods();
            if (mods.Count == 0)
            {
                GUILayout.Label("No mods found in Data/ folder.");
                GUILayout.Label($"<color=#888>Path: {ModProjectLoader.DataRoot}</color>",
                    EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label($"<b>{mods.Count} mod(s)</b> found:",
                EditorStyles.RichLabel);

            foreach (var modId in mods)
            {
                GUILayout.BeginHorizontal();

                bool isActive = ActiveProject != null && ActiveProject.ModId == modId;
                string label = isActive ? $"<color=cyan>► {modId}</color>" : modId;

                if (GUILayout.Button(label, EditorStyles.ListItem, GUILayout.ExpandWidth(true)))
                {
                    LoadMod(modId);
                }

                GUILayout.EndHorizontal();
            }
        }

        // ── Metadata ─────────────────────────────────────────────────

        private void DrawMetadata()
        {
            var p = ActiveProject;
            if (p == null) return;

            GUI.enabled = false;
            EditorFields.TextField("Mod ID", p.ModId);
            GUI.enabled = true;
            p.ModName = EditorFields.TextField("Name", p.ModName);
            p.Author = EditorFields.TextField("Author", p.Author);
            p.Version = EditorFields.TextField("Version", p.Version);
            p.Description = EditorFields.TextField("Description", p.Description);

            GUILayout.Space(4);
            GUILayout.Label("<color=#aaa>Starter Override</color>", EditorStyles.RichLabel);
            p.StarterNodeId = EditorFields.TextField("Starter Node", p.StarterNodeId);
            if (!string.IsNullOrEmpty(p.StarterNodeId))
                GUILayout.Label($"<color=#888><size=10>New adventures will start at \"{p.StarterNodeId}\"</size></color>",
                    EditorStyles.RichLabel);

            GUILayout.Space(4);
            if (GUILayout.Button("Save Metadata", GUILayout.Height(25)))
            {
                ModProjectLoader.SaveMetadata(p);
                Plugin.Log.LogInfo($"[ModManager] Saved metadata for '{p.ModId}'");
            }
        }

        // ── Load Order ───────────────────────────────────────────────

        private void DrawLoadOrder()
        {
            var order = LoadOrderManager.Order;
            if (order.Count == 0)
            {
                GUILayout.Label("No mods in load order.");
                return;
            }

            GUILayout.Label("<color=#888>Top = loaded first (lowest priority). Bottom = loaded last (wins conflicts).</color>",
                EditorStyles.RichLabel);
            GUILayout.Space(4);

            _loadOrderScroll = GUILayout.BeginScrollView(_loadOrderScroll, GUILayout.MaxHeight(200));

            for (int i = 0; i < order.Count; i++)
            {
                GUILayout.BeginHorizontal();

                bool isActive = ActiveProject != null && ActiveProject.ModId == order[i];
                string label = isActive ? $"<color=cyan>{i + 1}. {order[i]}</color>" : $"{i + 1}. {order[i]}";
                GUILayout.Label(label, EditorStyles.RichLabel, GUILayout.ExpandWidth(true));

                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(25)))
                    LoadOrderManager.MoveUp(order[i]);
                GUI.enabled = i < order.Count - 1;
                if (GUILayout.Button("▼", GUILayout.Width(25)))
                    LoadOrderManager.MoveDown(order[i]);
                GUI.enabled = true;

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        // ── Create New ───────────────────────────────────────────────

        private bool _createLatched, _latchValid, _latchTaken, _latchInvalidHint;

        private void DrawCreateNew()
        {
            _newModId = EditorFields.TextField("Mod ID", _newModId);
            _newModName = EditorFields.TextField("Mod Name", _newModName);
            _newModAuthor = EditorFields.TextField("Author", _newModAuthor);

            GUILayout.Space(4);
            string sanitizedId = _newModId.Trim();
            // Latched on Layout so the hint rows cannot appear between Layout and Repaint
            if (Event.current.type == EventType.Layout || !_createLatched)
            {
                _createLatched = true;
                _latchValid = ModProjectLoader.IsValidNewModId(sanitizedId);
                _latchTaken = _latchValid && (System.IO.Directory.Exists(ModProjectLoader.ModFolder(sanitizedId))
                    || System.IO.File.Exists(ModProjectLoader.MetadataPath(sanitizedId)));
                _latchInvalidHint = sanitizedId.Length > 0 && !_latchValid;
            }
            bool validId = _latchValid;
            if (_latchInvalidHint)
                GUILayout.Label("<color=#f88>Use letters, digits and '_' only (ids are embedded in the ids of everything the mod creates).</color>", EditorStyles.RichLabel);
            bool idTaken = _latchTaken;
            if (idTaken)
                GUILayout.Label("<color=#f88>A mod with this id already exists.</color>", EditorStyles.RichLabel);
            GUI.enabled = validId && !idTaken;
            if (GUILayout.Button("Create Mod", GUILayout.Height(25)))
            {
                var proj = ModProjectLoader.CreateNew(
                    _newModId.Trim(),
                    string.IsNullOrWhiteSpace(_newModName) ? _newModId : _newModName.Trim(),
                    _newModAuthor.Trim());

                LoadOrderManager.SetEnabled(proj.ModId, true); // a deleted mod's old disabled flag must not carry over
                LoadOrderManager.AddMod(proj.ModId);
                ActiveProject = proj;

                Plugin.Log.LogInfo($"[ModManager] Created new mod: {proj.ModId}");

                _newModId = "";
                _newModName = "";
                _newModAuthor = "";
            }
            GUI.enabled = true;
        }

        // ── Validate ───────────────────────────────────────────────

        private void DrawValidate()
        {
            GUILayout.Label("<color=#aaa>Looks for ids that point at nothing (cards, NPCs, combats, events, auras, requirements, sounds, sprites, ...), event setups the game cannot run (a card roll with no hero, a reply chain with no exit), and map problems (dangling or duplicate connections, unreachable islands, blank combat slots, unreferenced content). Run after a rebuild so other mods' content is registered.</color>",
                EditorStyles.RichLabel);
            if (GUILayout.Button("Check References", GUILayout.Height(25)))
            {
                _refReport = ReferenceValidator.Validate(ActiveProject);
                _refReport.AddRange(EventValidator.Validate(ActiveProject));
                _refReport.AddRange(ContentValidator.Validate(ActiveProject));
                _refReport.AddRange(GraphValidator.Validate(ActiveProject));
                foreach (var m in _refReport) Plugin.Log.LogWarning($"[Check:{ActiveProject.ModId}] {m}");
            }
            if (_refReport == null) return;
            if (_refReport.Count == 0)
            {
                GUILayout.Label("<color=#88ff88>No problems found.</color>", EditorStyles.RichLabel);
                return;
            }
            GUILayout.Label($"<color=#ffaa44>{_refReport.Count(m => m.Severity != DiagSeverity.Info)} problem(s), {_refReport.Count(m => m.Severity == DiagSeverity.Info)} note(s):</color>", EditorStyles.RichLabel);
            for (int i = 0; i < _refReport.Count && i < 60; i++)
                GUILayout.Label((_refReport[i].Severity == DiagSeverity.Error ? "<color=#ff6666>ERROR " : "<color=#ffcc88>") + _refReport[i].Message + "</color>", EditorStyles.RichLabel);
            if (_refReport.Count > 60)
                GUILayout.Label($"<color=#888>\u2026and {_refReport.Count - 60} more (see LogOutput.log)</color>", EditorStyles.RichLabel);
        }

        // ── Rebuild ────────────────────────────────────────────────

        private void DrawRebuild()
        {
            string scene = SceneManager.GetActiveScene().name;
            bool onMainMenu = scene == "MainMenu";
            if (!onMainMenu)
            {
                GUILayout.Label($"<color=#ff8888>Rebuild is only available on the main menu.\nCurrent scene: {scene}</color>",
                    EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label("<color=#aaa>Re-loads all mods from disk and rebuilds every entity.\nChanges made in the editor take effect after rebuilding.</color>",
                EditorStyles.RichLabel);
            GUILayout.Space(4);

            if (GUILayout.Button("Rebuild All Mods", GUILayout.Height(30)))
            {
                Plugin.Log.LogInfo("[ModManager] Rebuild All requested.");
                LoadOrderManager.LoadAndBuildAll();
                Plugin.Log.LogInfo("[ModManager] Rebuild All complete.");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ACTIONS
        // ═══════════════════════════════════════════════════════════════

        private void LoadMod(string modId)
        {
            Plugin.Log.LogInfo($"[ModManager] Loading mod: {modId}");
            ActiveProject = ModProjectLoader.Load(modId);
            if (ActiveProject == null)
            {
                Plugin.Log.LogWarning($"[ModManager] Failed to load mod: {modId}");
                return;
            }
            Plugin.Log.LogInfo($"[ModManager] Active mod: {ActiveProject.ModName} ({ActiveProject.ModId})");
        }

        /// <summary>Initialize by loading the load order. Call once at startup.</summary>
        public void Initialize()
        {
            LoadOrderManager.Load();

            // Auto-select the first mod if available
            if (LoadOrderManager.Order.Count > 0 && ActiveProject == null)
                LoadMod(LoadOrderManager.Order[0]);
        }
    }
}
