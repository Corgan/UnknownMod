using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Core;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>Shows what the game is watching for changes, lets you push or pull a mod on demand, and lists what recently reloaded.</summary>
    public sealed class HotReloadPanel
    {
        private readonly ModEditor _editor;
        private Vector2 _scroll;
        private int _expanded = -1;

        public HotReloadPanel(ModEditor editor) { _editor = editor; }

        public void DrawPanel()
        {
            GUILayout.Label("<color=#888>Edit the mod's files in any program and the running game picks the changes up: entities rebuild in place, images and audio reload, scripts.dll reloads. Edits made in this editor are applied as you make them.</color>", EditorStyles.RichLabel);

            DrawStatus();
            EditorStyles.Separator();
            DrawActions();
            EditorStyles.Separator();
            DrawExternalEditing();
            EditorStyles.Separator();
            DrawHistory();
        }

        private static void DrawExternalEditing()
        {
            GUILayout.Label("<b>External editing</b>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Editors that understand JSON Schema (VS Code) get autocomplete and validation for every mod file. Schemas are rewritten each time the game starts.</color>", EditorStyles.RichLabel);
            var p = ModManagerPanel.ActiveProject;
            string folder = p != null ? ModProjectLoader.ModFolder(p.ModId) : ModProjectLoader.DataRoot;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Open in VS Code", "Opens the data folder (all mods) with the schemas active; falls back to Explorer if VS Code is not installed."), EditorStyles.MiniButton))
                if (!ExternalEditor.OpenFolderInCode(ModProjectLoader.DataRoot)) EditorStatus.Toast("Could not open the data folder.", true);
            if (GUILayout.Button(new GUIContent(p != null ? "Show mod folder" : "Show data folder", "Open the folder in Explorer."), EditorStyles.MiniButton))
                if (!ExternalEditor.Reveal(folder)) EditorStatus.Toast("That folder does not exist yet.", true);
            if (GUILayout.Button(new GUIContent("Update schemas", "Regenerate the JSON schemas and the VS Code settings now."), EditorStyles.MiniButton))
            {
                try { int n = ModSchema.WriteAll(ModProjectLoader.DataRoot); EditorStatus.Toast(n == 0 ? "Schemas are already up to date." : $"Updated {n} schema file(s)."); }
                catch (Exception ex) { EditorStatus.Toast("Schema update failed: " + ex.Message, true); }
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawStatus()
        {
            bool auto = Plugin.AutoHotReload.Value;
            bool now = GUILayout.Toggle(auto, " Apply changes made outside the game automatically");
            if (now != auto) Plugin.AutoHotReload.Value = now;

            string watch = HotReload.Watching
                ? $"<color=#66cc66>Watching</color> {HotReload.WatchPath}"
                : "<color=#e64d4d>Not watching.</color> The data folder could not be watched; use the buttons below after editing files.";
            GUILayout.Label(watch, EditorStyles.RichLabel);
            if (HotReload.Pending > 0)
                GUILayout.Label($"<color=#e6b84d>{HotReload.Pending} file change(s) waiting.</color> They apply once you stop editing in the game for a moment.", EditorStyles.RichLabel);
        }

        private static void DrawActions()
        {
            var p = ModManagerPanel.ActiveProject;
            if (p == null)
            {
                GUILayout.Label("<color=#888>Open a mod project to reload it.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label($"Open mod: <b>{p.ModId}</b>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Reload this mod from disk", "Reads the mod's files and applies anything that differs from what is open here."), EditorStyles.MiniButton))
            {
                var e = HotReload.ReloadFromDisk(p.ModId);
                if (string.IsNullOrEmpty(e.Summary)) EditorStatus.Toast("The files already match what is open.");
            }
            if (GUILayout.Button(new GUIContent("Push this mod to the game", "Rebuilds the mod in the running game from what is open in the editor, then the mods that depend on it."), EditorStyles.MiniButton))
                HotReload.PushMod(p, false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload scripts", EditorStyles.MiniButton))
            {
                var set = ScriptLoader.ReloadMod(p.ModId);
                EditorStatus.Toast(set == null ? "This mod has no scripts.dll." : $"Scripts: {set.Status.ToString().ToLowerInvariant()}, {set.Message}", set != null && set.Status != ScriptStatus.Loaded);
            }
            if (GUILayout.Button("Reload images", EditorStyles.MiniButton))
                ReloadImages(p.ModId);
            GUILayout.EndHorizontal();

            bool menu = SceneManager.GetActiveScene().name == "MainMenu";
            GUI.enabled = menu;
            if (GUILayout.Button(new GUIContent("Rebuild everything (main menu)", menu ? "Reloads every mod from disk and rebuilds all of them." : "Only available on the main menu."), EditorStyles.MiniButton))
                LoadOrderManager.LoadAndBuildAll();
            GUI.enabled = true;
        }

        private static void ReloadImages(string modId)
        {
            string dir = System.IO.Path.Combine(ModProjectLoader.ModFolder(modId), "sprites");
            if (!System.IO.Directory.Exists(dir)) { EditorStatus.Toast("This mod has no sprites folder."); return; }
            int made = 0;
            string failed = null;
            foreach (var f in System.IO.Directory.GetFiles(dir))
            {
                string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp" && ext != ".tga") continue;
                made += ModRegistry.ReloadImageFile(modId, f, out var err);
                if (err != null) failed = $"{System.IO.Path.GetFileName(f)}: {err}";
            }
            EditorStatus.Toast(failed ?? $"Reloaded images ({made} sprite(s)).", failed != null);
        }

        private void DrawHistory()
        {
            GUILayout.Label($"<b>Recent reloads</b> ({HotReload.History.Count})", EditorStyles.RichLabel);
            if (HotReload.History.Count == 0)
            {
                GUILayout.Label("<color=#888>Nothing has reloaded yet this session.</color>", EditorStyles.RichLabel);
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(320));
            for (int i = 0; i < HotReload.History.Count; i++)
            {
                var h = HotReload.History[i];
                string col = h.IsError ? "#e64d4d" : "#cccccc";
                GUILayout.BeginHorizontal();
                bool open = _expanded == i;
                if (h.Details.Count > 0 && GUILayout.Button(open ? "\u25BC" : "\u25B6", EditorStyles.MiniButton, GUILayout.Width(22)))
                    _expanded = open ? -1 : i;
                else if (h.Details.Count == 0) GUILayout.Space(26);
                GUILayout.Label($"<color=#888>{h.Time:HH:mm:ss}</color> <color={col}>{h.Summary}</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
                if (_expanded == i)
                    foreach (var d in h.Details.Take(80))
                        GUILayout.Label("      <color=#999>" + d + "</color>", EditorStyles.RichLabel);
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Clear history", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                HotReload.History.Clear();
                _expanded = -1;
            }
        }
    }
}
