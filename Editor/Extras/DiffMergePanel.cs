using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>Compare the open mod with its saved copy, a snapshot or another mod, and take over entities or single fields. Snapshots double as an undo for big changes.</summary>
    public sealed class DiffMergePanel
    {
        private readonly ModEditor _editor;

        private ModProject _theirs;
        private string _theirsLabel = "";
        private ProjectDiff _diff;
        private string _error = "";

        private string _snapLabel = "";
        private List<ModSnapshots.Info> _snaps;
        private string _otherMod = "";

        private bool _showAdded = true, _showRemoved = true, _showChanged = true;
        private string _filter = "";
        private readonly HashSet<string> _expanded = new();
        private readonly HashSet<string> _checked = new();
        private string _confirm = "";
        private string _modLoadedFor = "";

        public DiffMergePanel(ModEditor editor) { _editor = editor; }

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("Open a mod project first."); return; }
            if (_modLoadedFor != proj.ModId)
            {
                _modLoadedFor = proj.ModId; _theirs = null; _diff = null; _snaps = null; _checked.Clear(); _expanded.Clear(); _confirm = "";
            }
            if (_snaps == null && Event.current.type == EventType.Layout) _snaps = ModSnapshots.List(proj.ModId);

            GUILayout.Label("<color=#888>Compare this mod with an earlier snapshot, its saved files, or another mod, then take over whole entities or single fields. A snapshot is taken automatically before anything is merged, so a merge can always be walked back.</color>", EditorStyles.RichLabel);

            DrawSnapshots(proj);
            EditorStyles.Separator();
            DrawSources(proj);

            if (!string.IsNullOrEmpty(_error)) GUILayout.Label($"<color=#e64d4d>{_error}</color>", EditorStyles.RichLabel);
            if (_diff == null) return;
            EditorStyles.Separator();
            DrawDiff(proj);
        }

        // ─────────────────────────────────────────────────────────────
        //  SNAPSHOTS + SOURCES
        // ─────────────────────────────────────────────────────────────

        private void DrawSnapshots(ModProject proj)
        {
            GUILayout.Label("<b>Snapshots</b>", EditorStyles.RichLabel);
            GUILayout.BeginHorizontal();
            _snapLabel = EditorFields.TextField("Label", _snapLabel);
            if (GUILayout.Button("Take snapshot", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                string name = TakeSnapshot(proj, _snapLabel);
                if (name != null) { EditorStatus.Toast("Snapshot saved: " + name); _snapLabel = ""; }
                else EditorStatus.Toast("Could not take a snapshot", true);
                _snaps = null;
            }
            GUILayout.EndHorizontal();

            if (_snaps == null) return;
            if (_snaps.Count == 0) { GUILayout.Label("<color=#888>No snapshots of this mod yet.</color>", EditorStyles.RichLabel); return; }

            ModSnapshots.Info toDelete = null;
            foreach (var s in _snaps.Take(12))
            {
                GUILayout.BeginHorizontal();
                string when = s.Time == default ? s.Name : s.Time.ToString("yyyy-MM-dd HH:mm:ss");
                GUILayout.Label(when + (s.Label.Length > 0 ? $"  <color=#aaa>{s.Label}</color>" : ""), EditorStyles.RichLabel);
                if (GUILayout.Button("Compare", EditorStyles.MiniButton, GUILayout.Width(70))) CompareSnapshot(proj, s);
                if (GUILayout.Button(_confirm == "del" + s.Name ? "Sure?" : "Delete", EditorStyles.MiniButton, GUILayout.Width(55)))
                {
                    if (_confirm == "del" + s.Name) { toDelete = s; _confirm = ""; } else _confirm = "del" + s.Name;
                }
                GUILayout.EndHorizontal();
            }
            if (_snaps.Count > 12) GUILayout.Label($"<color=#888>{_snaps.Count - 12} older snapshots not listed.</color>", EditorStyles.RichLabel);
            if (toDelete != null) { ModSnapshots.Delete(toDelete); _snaps = null; }
        }

        private string TakeSnapshot(ModProject proj, string label)
        {
            try
            {
                if (ZoneEditingService.IsDirty) ZoneEditingService.SaveCurrentZone();
                _editor.SaveAll();
                return ModSnapshots.Take(proj, label);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DiffMerge] Snapshot failed: {ex}");
                return null;
            }
        }

        private void DrawSources(ModProject proj)
        {
            GUILayout.Label("<b>Compare with</b>", EditorStyles.RichLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Saved files", EditorStyles.MiniButton, GUILayout.Width(90))) CompareDisk(proj);
            var others = ModProjectLoader.DiscoverMods().Where(m => !string.Equals(m, proj.ModId, StringComparison.OrdinalIgnoreCase)).ToList();
            GUILayout.EndHorizontal();

            if (others.Count > 0)
            {
                GUILayout.BeginHorizontal();
                _otherMod = EditorFields.IdDropdown("Another mod", _otherMod, others, "diff_other");
                if (GUILayout.Button("Compare", EditorStyles.MiniButton, GUILayout.Width(70)) && !string.IsNullOrEmpty(_otherMod)) CompareMod(proj, _otherMod);
                GUILayout.EndHorizontal();
            }

            if (_diff != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Comparing against: <b>{_theirsLabel}</b>", EditorStyles.RichLabel);
                if (GUILayout.Button("Recompare", EditorStyles.MiniButton, GUILayout.Width(80))) Recompute(proj);
                if (GUILayout.Button("Close", EditorStyles.MiniButton, GUILayout.Width(50))) { _diff = null; _theirs = null; _checked.Clear(); }
                GUILayout.EndHorizontal();
            }
        }

        private void Begin(ModProject proj, ModProject theirs, string label)
        {
            _error = "";
            _theirs = theirs; _theirsLabel = label;
            _checked.Clear(); _expanded.Clear(); _confirm = "";
            Recompute(proj);
        }

        private void Recompute(ModProject proj)
        {
            if (_theirs == null) return;
            try { _diff = ModDiff.Compare(proj, _theirs); }
            catch (Exception ex) { _error = "Comparison failed: " + ex.Message; _diff = null; Plugin.Log.LogError($"[DiffMerge] {ex}"); }
        }

        private void CompareDisk(ModProject proj)
        {
            try
            {
                if (ZoneEditingService.IsDirty) ZoneEditingService.SaveCurrentZone();
                Begin(proj, ModProjectLoader.Load(proj.ModId), "saved files");
            }
            catch (Exception ex) { _error = "Could not read the saved files: " + ex.Message; }
        }

        private void CompareSnapshot(ModProject proj, ModSnapshots.Info s)
        {
            try { Begin(proj, ModSnapshots.Open(proj.ModId, s), "snapshot " + (s.Time == default ? s.Name : s.Time.ToString("yyyy-MM-dd HH:mm:ss")) + (s.Label.Length > 0 ? " (" + s.Label + ")" : "")); }
            catch (Exception ex) { _error = "Could not open the snapshot: " + ex.Message; }
        }

        private void CompareMod(ModProject proj, string modId)
        {
            try { Begin(proj, ModProjectLoader.Load(modId), "mod " + modId); }
            catch (Exception ex) { _error = "Could not load that mod: " + ex.Message; }
        }

        // ─────────────────────────────────────────────────────────────
        //  RESULT
        // ─────────────────────────────────────────────────────────────

        private static string KindIcon(DiffKind k) => k switch
        {
            DiffKind.Added => "<color=#59cc66>+</color>",
            DiffKind.Removed => "<color=#e64d4d>\u2212</color>",
            _ => "<color=#f2bf40>~</color>"
        };

        private void DrawDiff(ModProject proj)
        {
            var d = _diff;
            if (d.IsEmpty)
            {
                GUILayout.Label("<color=#59cc66>No differences: this mod matches it exactly.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label("<color=#888>\u201cTheirs\u201d is what you compare against. + exists only there, \u2212 exists only in your mod, ~ differs. Taking theirs makes your mod look like theirs for that item.</color>", EditorStyles.RichLabel);

            if (d.Meta.Count > 0)
            {
                GUILayout.Label("<b>Mod settings</b>", EditorStyles.RichLabel);
                foreach (var f in d.Meta.ToList())
                    DrawField(proj, null, f, true);
            }

            GUILayout.BeginHorizontal();
            _showAdded = GUILayout.Toggle(_showAdded, $"+ {d.Added} only theirs", EditorStyles.MiniButton);
            _showRemoved = GUILayout.Toggle(_showRemoved, $"\u2212 {d.Removed} only mine", EditorStyles.MiniButton);
            _showChanged = GUILayout.Toggle(_showChanged, $"~ {d.Changed} changed", EditorStyles.MiniButton);
            GUILayout.EndHorizontal();
            _filter = EditorFields.TextField("Filter", _filter);

            var shown = d.Entities.Where(e =>
                (e.Kind == DiffKind.Added && _showAdded || e.Kind == DiffKind.Removed && _showRemoved || e.Kind == DiffKind.Changed && _showChanged)
                && (string.IsNullOrWhiteSpace(_filter) || e.Key.IndexOf(_filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Check all shown", EditorStyles.MiniButton, GUILayout.Width(110))) foreach (var e in shown) _checked.Add(e.Key);
            if (GUILayout.Button("Check none", EditorStyles.MiniButton, GUILayout.Width(80))) _checked.Clear();
            int nChecked = d.Entities.Count(e => _checked.Contains(e.Key));
            GUI.enabled = nChecked > 0;
            string applyLabel = _confirm == "apply" ? $"Sure? Take theirs for {nChecked}" : $"Take theirs for {nChecked} checked";
            if (GUILayout.Button(applyLabel, EditorStyles.MiniButton, GUILayout.Width(190)))
            {
                if (_confirm == "apply") { ApplyChecked(proj); _confirm = ""; } else _confirm = "apply";
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string allLabel = _confirm == "all" ? "Sure? Make my mod identical to theirs" : "Make my mod identical to theirs";
            if (GUILayout.Button(allLabel, EditorStyles.MiniButton, GUILayout.Width(250)))
            {
                if (_confirm == "all") { ApplyAll(proj); _confirm = ""; } else _confirm = "all";
            }
            GUILayout.Label("<color=#888>(a snapshot of your current state is taken first)</color>", EditorStyles.RichLabel);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            int count = 0;
            foreach (var e in shown.ToList())
            {
                if (count++ >= 150) { GUILayout.Label($"<color=#888>\u2026{shown.Count - 150} more. Use the filter to narrow the list.</color>", EditorStyles.RichLabel); break; }
                DrawEntity(proj, e);
            }
        }

        private void DrawEntity(ModProject proj, EntityDiff e)
        {
            GUILayout.BeginHorizontal();
            bool on = _checked.Contains(e.Key);
            bool now = GUILayout.Toggle(on, GUIContent.none, GUILayout.Width(18));
            if (now != on) { if (now) _checked.Add(e.Key); else _checked.Remove(e.Key); }

            bool open = _expanded.Contains(e.Key);
            string arrow = e.Kind == DiffKind.Changed ? (open ? "\u25BE " : "\u25B8 ") : "";
            string summary = e.Kind == DiffKind.Changed ? $"  <color=#888>{e.Fields.Count} field(s)</color>" : "";
            if (GUILayout.Button($"{KindIcon(e.Kind)} {arrow}{e.Family}/<b>{e.Id}</b>{summary}", EditorStyles.RichLabel))
            {
                if (e.Kind == DiffKind.Changed) { if (open) _expanded.Remove(e.Key); else _expanded.Add(e.Key); }
            }
            if (e.Kind != DiffKind.Added && GUILayout.Button("Open", EditorStyles.MiniButton, GUILayout.Width(46))) _editor.InspectFamily(e.Family, e.Id);
            string take = e.Kind == DiffKind.Added ? "Import" : e.Kind == DiffKind.Removed ? "Remove mine" : "Take theirs";
            if (GUILayout.Button(take, EditorStyles.MiniButton, GUILayout.Width(90))) ApplyEntities(proj, new List<EntityDiff> { e }, "merge " + e.Key);
            GUILayout.EndHorizontal();

            if (e.Kind == DiffKind.Changed && open)
                foreach (var f in e.Fields.ToList())
                    DrawField(proj, e, f, false);
        }

        private void DrawField(ModProject proj, EntityDiff e, FieldDiff f, bool meta)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(meta ? 6 : 28);
            GUILayout.Label($"<b>{f.Key}</b>", EditorStyles.RichLabel, GUILayout.Width(150));
            if (GUILayout.Button("Take theirs", EditorStyles.MiniButton, GUILayout.Width(80)))
            {
                if (meta) { MergeAction(proj, "merge settings", () => ModDiff.TakeMeta(proj, f)); }
                else MergeAction(proj, "merge " + e.Key, () => ModDiff.TakeField(proj, e, f), e);
            }
            GUILayout.EndHorizontal();

            if (f.Lines.Count == 0)
            {
                GUILayout.Label($"      <color=#aaa>{ModDiff.Show(f.Mine)} \u2192 {ModDiff.Show(f.Theirs)}</color>", EditorStyles.RichLabel);
                return;
            }
            foreach (var line in f.Lines) GUILayout.Label("      <color=#aaa>" + line + "</color>", EditorStyles.RichLabel);
            if (f.LineCount > f.Lines.Count) GUILayout.Label($"      <color=#888>\u2026and {(f.LineCount >= 60 ? "many" : (f.LineCount - f.Lines.Count).ToString())} more changes</color>", EditorStyles.RichLabel);
        }

        // ─────────────────────────────────────────────────────────────
        //  APPLY
        // ─────────────────────────────────────────────────────────────

        private void ApplyChecked(ModProject proj)
        {
            var list = _diff.Entities.Where(e => _checked.Contains(e.Key)).ToList();
            ApplyEntities(proj, list, $"merge {list.Count} entities");
            _checked.Clear();
        }

        private void ApplyAll(ModProject proj)
        {
            var diff = _diff;
            MergeAction(proj, "restore", () =>
            {
                foreach (var f in diff.Meta) ModDiff.TakeMeta(proj, f);
                foreach (var e in diff.Entities) ModDiff.TakeEntity(proj, e);
            }, diff.Entities.ToArray());
            _checked.Clear();
        }

        private void ApplyEntities(ModProject proj, List<EntityDiff> list, string label)
        {
            MergeAction(proj, label, () => { foreach (var e in list) ModDiff.TakeEntity(proj, e); }, list.ToArray());
        }

        private void MergeAction(ModProject proj, string label, Action action, params EntityDiff[] touched)
        {
            try
            {
                if (TakeSnapshot(proj, "before-" + label) == null)
                {
                    _error = $"{label} cancelled: the safety snapshot could not be written, so nothing was changed.";
                    EditorStatus.Toast("Merge cancelled: snapshot failed", true);
                    return;
                }
                _snaps = null;
                action();
                AfterMerge(proj, touched);
                EditorStatus.Toast($"Done: {label}. A snapshot was taken first.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DiffMerge] {label} failed: {ex}");
                _error = $"{label} failed: {ex.Message}";
                EditorStatus.Toast("Merge failed, see the log", true);
            }
            Recompute(proj);
        }

        private void AfterMerge(ModProject proj, IEnumerable<EntityDiff> touched)
        {
            var defs = new List<object>();
            foreach (var e in touched)
            {
                var def = ModDiff.Get(proj, e.Family, e.Id);
                if (def != null) defs.Add(def);
                if (e.Family == "Zones" || e.Family == "ZonePatches")
                {
                    ZoneEditingService.CurrentZone = null;
                    ZoneEditingService.CurrentPatch = null;
                }
            }
            HotReload.Push(proj, defs);
            proj.LastChangeTime = Time.realtimeSinceStartup;
            InlineIssues.Invalidate();
        }
    }
}
