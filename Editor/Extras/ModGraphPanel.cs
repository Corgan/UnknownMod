using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>Shows how all installed mods depend on each other, which dependencies are missing, and where two mods collide.</summary>
    public sealed class ModGraphPanel
    {
        private readonly ModEditor _editor;
        private ModGraphResult _graph;
        private string _error = "";
        private string _selected;
        private Vector2 _scroll;
        private bool _showWarnings = true, _showNotes = true;

        private static readonly Color DeclaredCol = new(0.35f, 0.8f, 0.4f, 0.9f);
        private static readonly Color UndeclaredCol = new(0.95f, 0.7f, 0.2f, 0.95f);
        private static readonly Color PatchCol = new(0.4f, 0.65f, 1f, 0.9f);
        private static readonly Color MissingCol = new(0.9f, 0.3f, 0.3f, 0.95f);

        public ModGraphPanel(ModEditor editor) { _editor = editor; }

        public void DrawPanel()
        {
            GUILayout.Label("<color=#888>Every installed mod, linked by what it declares in <b>depends</b> and by the ids it actually uses or patches. Green = declared, amber = used but not declared, blue = patches another mod, red = the mod isn't installed.</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan all mods", EditorStyles.MiniButton, GUILayout.Width(110))) Scan();
            if (_graph != null) GUILayout.Label($"{_graph.Nodes.Count} mods, {_graph.Problems.Count} issue(s), {_graph.Conflicts.Count} conflict(s)", EditorStyles.RichLabel);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_error)) GUILayout.Label($"<color=#e64d4d>{_error}</color>", EditorStyles.RichLabel);
            if (_graph == null) { GUILayout.Label("<color=#888>Press Scan to read every mod on disk. Your open mod is used as it is right now, not as last saved.</color>", EditorStyles.RichLabel); return; }
            if (_graph.Nodes.Count == 0) { GUILayout.Label("No mods found."); return; }

            DrawGraph();
            DrawSelected();
            EditorStyles.Separator();
            DrawProblems();
            DrawConflicts();
        }

        private void Scan()
        {
            _error = "";
            try
            {
                var active = ModManagerPanel.ActiveProject;
                if (active != null) _editor.SaveAll();
                var list = new List<ModProject>();
                foreach (var id in ModProjectLoader.DiscoverMods())
                {
                    if (active != null && string.Equals(id, active.ModId, StringComparison.OrdinalIgnoreCase)) { list.Add(active); continue; }
                    try { list.Add(ModProjectLoader.Load(id)); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[ModGraph] Could not load {id}: {ex.Message}"); }
                }
                _graph = ModGraph.Build(list);
                if (_selected != null && _graph.Nodes.All(n => n.Id != _selected)) _selected = null;
            }
            catch (Exception ex)
            {
                _error = "Scan failed: " + ex.Message;
                Plugin.Log.LogError($"[ModGraph] {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  GRAPH
        // ─────────────────────────────────────────────────────────────

        private void DrawGraph()
        {
            var g = _graph;
            int cols = g.Nodes.Max(n => n.Level) + 1;
            var rows = new Dictionary<string, int>();
            int maxRows = 1;
            foreach (var grp in g.Nodes.GroupBy(n => n.Level))
            {
                int i = 0;
                foreach (var n in grp.OrderBy(n => n.OrderIndex)) rows[n.Id] = i++;
                maxRows = Mathf.Max(maxRows, i);
            }

            const float boxW = 150f, boxH = 40f, gapX = 60f, gapY = 16f;
            float contentW = cols * boxW + (cols - 1) * gapX + 20f;
            float contentH = maxRows * boxH + (maxRows - 1) * gapY + 20f;
            float viewH = Mathf.Min(contentH + 4f, 320f);

            var outer = GUILayoutUtility.GetRect(10, viewH, GUILayout.ExpandWidth(true));
            GUI.Box(outer, GUIContent.none);
            var view = new Rect(0, 0, Mathf.Max(contentW, outer.width - 20f), contentH);
            _scroll = GUI.BeginScrollView(outer, _scroll, view);

            Rect BoxOf(ModGraphNode n) => new(10f + n.Level * (boxW + gapX), 10f + rows[n.Id] * (boxH + gapY), boxW, boxH);
            var byId = g.Nodes.ToDictionary(n => n.Id, n => n, StringComparer.OrdinalIgnoreCase);
            var ev = Event.current;

            if (ev.type == EventType.Repaint)
            {
                foreach (var l in g.Links)
                {
                    if (!byId.TryGetValue(l.From, out var a)) continue;
                    Color col = l.Missing ? MissingCol : l.Undeclared ? UndeclaredCol : l.Declared ? DeclaredCol : PatchCol;
                    if (l.Missing) continue;
                    if (!byId.TryGetValue(l.To, out var b)) continue;
                    var ra = BoxOf(a); var rb = BoxOf(b);
                    var pa = new Vector2(ra.xMin, ra.center.y); // arrows point from the needing mod to its provider
                    var pb = new Vector2(rb.xMax, rb.center.y);
                    if (b.Level >= a.Level) { pa = ra.center; pb = rb.center; }
                    bool hot = _selected != null && (l.From == _selected || l.To == _selected);
                    ReachabilityPanel.Line(pa, pb, hot ? col : new Color(col.r, col.g, col.b, col.a * 0.55f), hot ? 3f : 1.6f);
                    var dir = (pb - pa).normalized;
                    ReachabilityPanel.Dot(pb - dir * 6f, hot ? 4f : 3f, col);
                }
            }

            foreach (var n in g.Nodes)
            {
                var r = BoxOf(n);
                if (ev.type == EventType.Repaint)
                {
                    bool bad = g.Problems.Any(p => p.ModId == n.Id && p.Severity == ReachSeverity.Error);
                    bool warn = g.Problems.Any(p => p.ModId == n.Id && p.Severity == ReachSeverity.Warning);
                    var fill = n.Enabled ? new Color(0.17f, 0.2f, 0.26f, 1f) : new Color(0.14f, 0.14f, 0.14f, 1f);
                    var edge = bad ? MissingCol : warn ? UndeclaredCol : n.Id == _selected ? new Color(0.3f, 0.85f, 1f) : new Color(0.35f, 0.38f, 0.45f);
                    ReachabilityPanel.Fill(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), edge);
                    ReachabilityPanel.Fill(r, fill);
                }
                bool active = ModManagerPanel.ActiveProject != null && string.Equals(ModManagerPanel.ActiveProject.ModId, n.Id, StringComparison.OrdinalIgnoreCase);
                string title = Shorten(n.Name, 22);
                string col2 = n.Enabled ? "#ddd" : "#777";
                GUI.Label(new Rect(r.x + 4, r.y + 2, r.width - 8, 18), $"<color={col2}><b>{title}</b></color>{(active ? " <color=#59cc66>\u25CF</color>" : "")}", EditorStyles.RichLabel);
                GUI.Label(new Rect(r.x + 4, r.y + 19, r.width - 8, 18), $"<color=#888>{n.Entities} entities, {n.Patches} patches</color>", EditorStyles.RichLabel);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) _selected = n.Id;
            }
            GUI.EndScrollView();
        }

        private static string Shorten(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026";

        private void DrawSelected()
        {
            if (_selected == null) return;
            var n = _graph.Nodes.FirstOrDefault(x => x.Id == _selected);
            if (n == null) return;
            GUILayout.Space(4);
            GUILayout.Label($"<b>{n.Name}</b> <color=#888>({n.Id}{(string.IsNullOrEmpty(n.Version) ? "" : " v" + n.Version)}) load order #{n.OrderIndex + 1}{(n.Enabled ? "" : ", disabled")}</color>", EditorStyles.RichLabel);

            foreach (var l in _graph.Links.Where(l => l.From == n.Id))
            {
                string kind = l.Missing ? "<color=#e64d4d>needs (not installed)</color>"
                    : l.Undeclared ? "<color=#f2bf40>uses without declaring</color>"
                    : l.Declared && !l.Uses && !l.Patches ? "<color=#59cc66>declares</color>"
                    : l.Patches ? "<color=#66a6ff>patches</color>" : "<color=#59cc66>uses</color>";
                string detail = l.Uses || l.Patches ? $"  <color=#888>{(l.UseCount > 0 ? l.UseCount + " ref(s)" : "")}{(l.PatchCount > 0 ? " " + l.PatchCount + " patch(es)" : "")}{(l.Examples.Count > 0 ? ": " + string.Join(", ", l.Examples.Take(4)) : "")}</color>" : "";
                GUILayout.Label($"  {kind} <b>{l.To}</b>{detail}", EditorStyles.RichLabel);
            }
            foreach (var l in _graph.Links.Where(l => l.To == n.Id && !l.Missing))
                GUILayout.Label($"  <color=#888>needed by</color> {l.From}", EditorStyles.RichLabel);
        }

        // ─────────────────────────────────────────────────────────────
        //  PROBLEMS + CONFLICTS
        // ─────────────────────────────────────────────────────────────

        private void DrawProblems()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Problems</b>", EditorStyles.RichLabel);
            _showWarnings = GUILayout.Toggle(_showWarnings, "warnings", EditorStyles.MiniButton, GUILayout.Width(70));
            _showNotes = GUILayout.Toggle(_showNotes, "notes", EditorStyles.MiniButton, GUILayout.Width(55));
            GUILayout.EndHorizontal();

            var shown = _graph.Problems.Where(p => p.Severity == ReachSeverity.Error || p.Severity == ReachSeverity.Warning && _showWarnings || p.Severity == ReachSeverity.Info && _showNotes)
                .OrderBy(p => p.Severity).ToList();
            if (shown.Count == 0) { GUILayout.Label("<color=#59cc66>No dependency problems.</color>", EditorStyles.RichLabel); return; }

            ModProblem fix = null;
            foreach (var p in shown)
            {
                GUILayout.BeginHorizontal();
                string tag = p.Severity == ReachSeverity.Error ? "<color=#e64d4d>\u25A0</color>" : p.Severity == ReachSeverity.Warning ? "<color=#f2bf40>\u25B2</color>" : "<color=#7aa>\u25CF</color>";
                GUILayout.Label($"{tag} <b>{p.ModId}</b>: {p.Message}", EditorStyles.RichLabel);
                if (!string.IsNullOrEmpty(p.AddDependency) && GUILayout.Button("Add dependency", EditorStyles.MiniButton, GUILayout.Width(100))) fix = p;
                GUILayout.EndHorizontal();
            }
            if (fix != null) ApplyFix(fix);
        }

        private void ApplyFix(ModProblem p)
        {
            var node = _graph.Nodes.FirstOrDefault(n => string.Equals(n.Id, p.ModId, StringComparison.OrdinalIgnoreCase));
            var proj = node?.Project;
            if (proj == null) return;
            try
            {
                if (!proj.Dependencies.Any(d => string.Equals(d, p.AddDependency, StringComparison.OrdinalIgnoreCase)))
                    proj.Dependencies.Add(p.AddDependency);
                ModProjectLoader.SaveMetadata(proj);
                EditorStatus.Toast($"{proj.ModId} now depends on {p.AddDependency}");
                Scan();
            }
            catch (Exception ex)
            {
                _error = "Could not save the dependency: " + ex.Message;
            }
        }

        private void DrawConflicts()
        {
            if (_graph.Conflicts.Count == 0) return;
            EditorStyles.Separator();
            GUILayout.Label("<b>Conflicts between mods</b> <color=#888>(the mod later in load order wins)</color>", EditorStyles.RichLabel);
            foreach (var c in _graph.Conflicts.Take(100))
            {
                string fields = c.Fields.Count > 0 ? $"  <color=#888>differs in: {string.Join(", ", c.Fields.Take(6))}{(c.Fields.Count > 6 ? "\u2026" : "")}</color>" : "";
                GUILayout.Label($"<color=#f2bf40>{c.Kind}</color> {c.Family}/<b>{c.Id}</b>: {string.Join(" \u2192 ", c.Mods)}{fields}", EditorStyles.RichLabel);
            }
            if (_graph.Conflicts.Count > 100) GUILayout.Label($"<color=#888>\u2026{_graph.Conflicts.Count - 100} more.</color>", EditorStyles.RichLabel);
        }
    }
}
