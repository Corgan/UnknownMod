using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>Shows which nodes of a zone the party can really reach (with gates), flags dead ends and broken links, and lists project content nothing points at.</summary>
    public sealed class ReachabilityPanel
    {
        private enum View { Map, Content }

        private static readonly Color FreeCol = new(0.35f, 0.80f, 0.40f);
        private static readonly Color GatedCol = new(0.95f, 0.75f, 0.25f);
        private static readonly Color BadCol = new(0.90f, 0.30f, 0.30f);
        private static readonly Color AbsentCol = new(0.50f, 0.50f, 0.50f);

        private readonly ModEditor _editor;
        private View _view = View.Map;

        private string _zoneId = "";
        private ZoneDef _zone;
        private ZoneReach _reach;
        private float _lastRefresh;
        private string _lastRefreshedZone = "";

        private string _selected = "";
        private float _zoom = 1f, _fx, _fy;
        private bool _needFit = true, _dragging;
        private bool _showErrors = true, _showWarnings = true, _showInfo;

        private List<OrphanEntity> _orphans;
        private readonly HashSet<string> _families = new(new[] { "Combats", "Events", "Loot", "Npcs", "Requirements" });

        public ReachabilityPanel(ModEditor editor) { _editor = editor; }

        public void Preset(string zoneId)
        {
            _view = View.Map;
            _zoneId = zoneId ?? "";
            _selected = "";
            _needFit = true;
            _reach = null;
        }

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("Open a mod project first."); return; }

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_view == View.Map, "Map reachability", EditorStyles.MiniButton)) _view = View.Map;
            if (GUILayout.Toggle(_view == View.Content, "Unused content", EditorStyles.MiniButton)) _view = View.Content;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_view == View.Map) DrawMapView(proj); else DrawContentView(proj);
        }

        // ─────────────────────────────────────────────────────────────
        //  MAP
        // ─────────────────────────────────────────────────────────────

        private ZoneDef ResolveZone(ModProject proj)
        {
            if (proj.Zones.TryGetValue(_zoneId, out var z)) return z;
            if (proj.ZonePatches.TryGetValue(_zoneId, out var zp)) return ZoneEditingService.SynthesizeZoneDef(zp);
            return null;
        }

        private void DrawMapView(ModProject proj)
        {
            var zoneIds = proj.Zones.Keys.Concat(proj.ZonePatches.Keys).Distinct().ToList();
            if (zoneIds.Count == 0)
            {
                GUILayout.Label("<i>This project has no zones or zone patches. Create one in World \u203a Zones.</i>", EditorStyles.RichLabel);
                return;
            }
            if (!zoneIds.Contains(_zoneId))
            {
                var current = _editor.ZoneTab.SelectedZoneId;
                _zoneId = !string.IsNullOrEmpty(current) && zoneIds.Contains(current) ? current : zoneIds[0];
                _needFit = true; _selected = "";
            }

            GUILayout.BeginHorizontal();
            string picked = EditorFields.IdDropdown("Zone", _zoneId, zoneIds, "reach_zone");
            if (picked != _zoneId && !string.IsNullOrEmpty(picked)) { _zoneId = picked; _needFit = true; _selected = ""; _reach = null; }
            if (GUILayout.Button("Refresh", EditorStyles.MiniButton, GUILayout.Width(60))) _reach = null;
            GUILayout.EndHorizontal();

            // Recompute only on Layout so Layout and Repaint see identical control counts.
            if (Event.current.type == EventType.Layout)
            {
                var zone = ResolveZone(proj);
                bool stale = _reach == null || zone != _zone || _lastRefreshedZone != _zoneId || Time.realtimeSinceStartup - _lastRefresh > 1f;
                if (zone == null) { _zone = null; _reach = null; }
                else if (stale)
                {
                    _zone = zone;
                    _reach = ReachabilityAnalyzer.Analyze(zone, proj);
                    _lastRefresh = Time.realtimeSinceStartup;
                    _lastRefreshedZone = _zoneId;
                }
            }

            if (_zone == null || _reach == null)
            {
                string status = ZoneEditingService.SynthesisStatus;
                GUILayout.Label(string.IsNullOrEmpty(status) ? "<i>Zone data isn't available yet.</i>" : $"<i>{status}</i>", EditorStyles.RichLabel);
                return;
            }

            var r = _reach;
            GUILayout.Label(
                $"<color=#59cc66>\u25CF</color> {r.Free} reachable   <color=#f2bf40>\u25CF</color> {r.Gated} only through a gate   <color=#e64d4d>\u25CF</color> {r.Unreachable} unreachable   <color=#808080>\u25CF</color> {r.Absent} never exist" +
                $"   |   entrance: <b>{r.Entrance}</b>{(r.EntranceGuessed ? " (guessed)" : "")}",
                EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Drag the map to pan, scroll to zoom, click a node to inspect it. Lines run in the direction the party can travel; amber ones are gated; the small square marks the arrow end.</color>", EditorStyles.RichLabel);

            DrawMap(_zone, r);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fit", EditorStyles.MiniButton, GUILayout.Width(40))) _needFit = true;
            if (GUILayout.Button("+", EditorStyles.MiniButton, GUILayout.Width(28))) _zoom = Mathf.Min(_zoom * 1.25f, 20f);
            if (GUILayout.Button("\u2212", EditorStyles.MiniButton, GUILayout.Width(28))) _zoom = Mathf.Max(_zoom / 1.25f, 0.2f);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (!string.IsNullOrEmpty(_selected) && _zone.Nodes.ContainsKey(_selected)) DrawSelected(_zone, r);

            DrawIssues(r);
        }

        private static Color StatusColor(ReachStatus s) => s switch
        {
            ReachStatus.Free => FreeCol,
            ReachStatus.Gated => GatedCol,
            ReachStatus.Absent => AbsentCol,
            _ => BadCol
        };

        internal static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        internal static void Line(Vector2 a, Vector2 b, Color c, float w)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - w * 0.5f, len, w), c);
            GUI.matrix = m;
        }

        internal static void Dot(Vector2 c, float rad, Color col) => Fill(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), col);

        private void DrawMap(ZoneDef z, ZoneReach r)
        {
            var rect = GUILayoutUtility.GetRect(10, 330, GUILayout.ExpandWidth(true));
            var ev = Event.current;
            var local = new Rect(0, 0, rect.width, rect.height);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var n in z.Nodes.Values)
            {
                minX = Mathf.Min(minX, n.PosX); maxX = Mathf.Max(maxX, n.PosX);
                minY = Mathf.Min(minY, n.PosY); maxY = Mathf.Max(maxY, n.PosY);
            }
            if (minX > maxX) { minX = maxX = minY = maxY = 0; }
            float spanX = Mathf.Max(maxX - minX, 0.1f), spanY = Mathf.Max(maxY - minY, 0.1f);
            float fit = Mathf.Min((rect.width - 40f) / spanX, (rect.height - 40f) / spanY);
            if (_needFit && ev.type == EventType.Layout)
            {
                _fx = (minX + maxX) * 0.5f; _fy = (minY + maxY) * 0.5f; _zoom = 1f; _needFit = false;
            }
            float scale = Mathf.Max(fit, 1f) * _zoom;
            var center = new Vector2(local.width * 0.5f, local.height * 0.5f);
            Vector2 ToScreen(NodeDef n) => center + new Vector2((n.PosX - _fx) * scale, (_fy - n.PosY) * scale);

            GUI.Box(rect, GUIContent.none);
            GUI.BeginGroup(rect);

            string hovered = null;
            var mouse = ev.mousePosition;
            if (local.Contains(mouse))
                foreach (var n in z.Nodes.Values)
                    if ((ToScreen(n) - mouse).sqrMagnitude <= 11f * 11f) { hovered = n.NodeId; break; }

            if (ev.type == EventType.Repaint)
            {
                foreach (var e in r.Edges)
                {
                    if (e.Dangling || !z.Nodes.TryGetValue(e.From, out var fn) || !z.Nodes.TryGetValue(e.To, out var tn)) continue;
                    var a = ToScreen(fn); var b = ToScreen(tn);
                    r.Status.TryGetValue(e.From, out var fs);
                    var col = e.IsGated ? GatedCol : (fs == ReachStatus.Unreachable || fs == ReachStatus.Absent) ? new Color(0.7f, 0.3f, 0.3f) : new Color(0.65f, 0.65f, 0.7f);
                    col.a = 0.85f;
                    Line(a, b, col, e.IsGated ? 2.5f : 1.5f);
                    Dot(Vector2.Lerp(a, b, 0.78f), 3f, col);
                }

                var nodeIssues = r.Issues.Where(i => i.Severity == ReachSeverity.Error && i.NodeId != null).Select(i => i.NodeId).ToHashSet();
                foreach (var n in z.Nodes.Values)
                {
                    var p = ToScreen(n);
                    if (n.NodeId == _selected) Dot(p, 12f, Color.cyan);
                    else if (n.NodeId == hovered) Dot(p, 11f, Color.white);
                    if (n.NodeId == r.Entrance) Dot(p, 10f, Color.white);
                    if (nodeIssues.Contains(n.NodeId)) Dot(p, 9f, new Color(1f, 0.2f, 0.8f));
                    Dot(p, 7f, StatusColor(r.Status[n.NodeId]));
                    if (n.TravelDestination || n.GoToTown) Dot(p, 2.5f, Color.black);
                }

                if (hovered != null)
                {
                    var hn = z.Nodes[hovered];
                    string tip = $"{hovered}{(string.IsNullOrEmpty(hn.NodeName) ? "" : " \u2013 " + hn.NodeName)}\n{r.Status[hovered]}";
                    GUI.Box(new Rect(mouse.x + 14, mouse.y + 8, 230, 40), tip);
                }
            }

            if (local.Contains(mouse))
            {
                if (ev.type == EventType.MouseDown && ev.button == 0)
                {
                    if (hovered != null) _selected = hovered; else _dragging = true;
                    ev.Use();
                }
                else if (ev.type == EventType.MouseDrag && _dragging)
                {
                    _fx -= ev.delta.x / scale; _fy += ev.delta.y / scale;
                    ev.Use();
                }
                else if (ev.type == EventType.ScrollWheel)
                {
                    _zoom = Mathf.Clamp(_zoom * (ev.delta.y > 0 ? 0.88f : 1.14f), 0.2f, 20f);
                    ev.Use();
                }
            }
            if (ev.type == EventType.MouseUp) _dragging = false;

            GUI.EndGroup();
        }

        private void Select(string nodeId)
        {
            _selected = nodeId;
            if (_zone != null && _zone.Nodes.TryGetValue(nodeId, out var n)) { _fx = n.PosX; _fy = n.PosY; }
        }

        private void DrawSelected(ZoneDef z, ZoneReach r)
        {
            var n = z.Nodes[_selected];
            var st = r.Status[_selected];
            var c = StatusColor(st);
            string hex = ColorUtility.ToHtmlStringRGB(c);
            string name = string.IsNullOrEmpty(n.NodeName) ? "" : $"  {n.NodeName}";
            GUILayout.Label($"<b>{n.NodeId}</b>{name}  <color=#{hex}>{st}</color>", EditorStyles.RichLabel);

            var sb = new StringBuilder();
            if (n.CombatIds.Count > 0) sb.Append("combat: ").Append(string.Join(", ", n.CombatIds)).Append("   ");
            if (n.EventIds.Count > 0) sb.Append("event: ").Append(string.Join(", ", n.EventIds)).Append("   ");
            if (n.ExistsPercent < 100) sb.Append($"exists {n.ExistsPercent}%   ");
            if (n.TravelDestination) sb.Append("travel destination   ");
            if (n.GoToTown) sb.Append("town exit   ");
            if (!string.IsNullOrEmpty(n.NodeRequirementId)) sb.Append("needs ").Append(n.NodeRequirementId);
            if (sb.Length > 0) GUILayout.Label("<color=#aaa>" + sb + "</color>", EditorStyles.RichLabel);

            var path = r.PathTo(_selected);
            if (path.Count > 0)
            {
                var ps = new StringBuilder(r.Entrance);
                foreach (var e in path)
                    ps.Append(" \u2192 ").Append(e.To).Append(e.IsGated ? $" <color=#f2bf40>[{e.Gate}]</color>" : "");
                GUILayout.Label("Route: " + ps, EditorStyles.RichLabel);
            }

            var outs = r.Edges.Where(e => e.From == _selected && !e.Dangling).ToList();
            var ins = r.Edges.Where(e => e.To == _selected && !e.Dangling).ToList();
            NeighbourRow("Leads to", outs.Select(e => e.To));
            NeighbourRow("Reached from", ins.Select(e => e.From));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open in node editor", EditorStyles.MiniButton, GUILayout.Width(140)))
            {
                _editor.ZoneTab.SelectedZoneId = _zoneId;
                _editor.InspectNode(_selected);
            }
            if (GUILayout.Button("Deselect", EditorStyles.MiniButton, GUILayout.Width(70))) _selected = "";
            GUILayout.EndHorizontal();

            foreach (var i in r.IssuesFor(_selected)) IssueLine(i, false);
            GUILayout.Space(4);
        }

        private void NeighbourRow(string label, IEnumerable<string> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) { GUILayout.Label($"<color=#888>{label}: nothing</color>", EditorStyles.RichLabel); return; }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}:", EditorStyles.RichLabel, GUILayout.Width(90));
            foreach (var id in list.Take(8))
                if (GUILayout.Button(id, EditorStyles.MiniButton)) Select(id);
            if (list.Count > 8) GUILayout.Label($"+{list.Count - 8}", EditorStyles.RichLabel, GUILayout.Width(30));
            GUILayout.EndHorizontal();
        }

        private void DrawIssues(ZoneReach r)
        {
            int errors = r.Issues.Count(i => i.Severity == ReachSeverity.Error);
            int warns = r.Issues.Count(i => i.Severity == ReachSeverity.Warning);
            int infos = r.Issues.Count(i => i.Severity == ReachSeverity.Info);

            EditorStyles.Separator();
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Findings</b>", EditorStyles.RichLabel, GUILayout.Width(70));
            _showErrors = GUILayout.Toggle(_showErrors, $"{errors} errors", EditorStyles.MiniButton);
            _showWarnings = GUILayout.Toggle(_showWarnings, $"{warns} warnings", EditorStyles.MiniButton);
            _showInfo = GUILayout.Toggle(_showInfo, $"{infos} notes", EditorStyles.MiniButton);
            GUILayout.EndHorizontal();

            if (errors + warns == 0)
                GUILayout.Label("<color=#59cc66>No errors or warnings. Every node can be reached and every link points somewhere.</color>", EditorStyles.RichLabel);

            int shown = 0;
            foreach (var i in r.Issues.OrderBy(i => i.Severity))
            {
                if (i.Severity == ReachSeverity.Error && !_showErrors) continue;
                if (i.Severity == ReachSeverity.Warning && !_showWarnings) continue;
                if (i.Severity == ReachSeverity.Info && !_showInfo) continue;
                if (shown++ >= 80) { GUILayout.Label("<color=#888>\u2026more hidden. Fix the above first, or use the filters.</color>", EditorStyles.RichLabel); break; }
                IssueLine(i, true);
            }
        }

        private void IssueLine(ReachIssue i, bool withNodeButton)
        {
            string col = i.Severity == ReachSeverity.Error ? "#e64d4d" : i.Severity == ReachSeverity.Warning ? "#f2bf40" : "#8aa";
            string icon = i.Severity == ReachSeverity.Error ? "\u2716" : i.Severity == ReachSeverity.Warning ? "\u26A0" : "\u2139";
            GUILayout.BeginHorizontal();
            if (withNodeButton && !string.IsNullOrEmpty(i.NodeId) && _zone != null && _zone.Nodes.ContainsKey(i.NodeId))
            {
                if (GUILayout.Button(i.NodeId, EditorStyles.MiniButton, GUILayout.Width(90))) Select(i.NodeId);
            }
            GUILayout.Label($"<color={col}>{icon}</color> {i.Message}", EditorStyles.RichLabel);
            GUILayout.EndHorizontal();
        }

        // ─────────────────────────────────────────────────────────────
        //  CONTENT
        // ─────────────────────────────────────────────────────────────

        private void DrawContentView(ModProject proj)
        {
            GUILayout.Label("<color=#888>Finds content in this project that nothing leads to. An entry counts as used when a zone, card, hero, patch or another used entry refers to it, so a combat only used by an unused event is reported too. Things only the base game or another mod refer to also show up here.</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            foreach (var f in ReachabilityAnalyzer.OrphanFamilies)
            {
                bool on = _families.Contains(f);
                bool now = GUILayout.Toggle(on, f, EditorStyles.MiniButton);
                if (now != on) { if (now) _families.Add(f); else _families.Remove(f); _orphans = null; }
            }
            GUILayout.EndHorizontal();

            if (Event.current.type == EventType.Layout && _orphans == null)
                _orphans = ReachabilityAnalyzer.FindOrphans(proj, _families);
            if (GUILayout.Button("Rescan", EditorStyles.MiniButton, GUILayout.Width(70))) _orphans = null;
            if (_orphans == null) return;

            if (_orphans.Count == 0)
            {
                GUILayout.Label("<color=#59cc66>Nothing unused in the selected families.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label($"<b>{_orphans.Count} unused</b>", EditorStyles.RichLabel);
            string lastFamily = null;
            foreach (var o in _orphans)
            {
                if (o.Family != lastFamily)
                {
                    lastFamily = o.Family;
                    GUILayout.Space(4);
                    GUILayout.Label($"<b>{o.Family}</b> ({_orphans.Count(x => x.Family == o.Family)})", EditorStyles.RichLabel);
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(o.Id, EditorStyles.MiniButton, GUILayout.Width(190))) _editor.InspectFamily(o.Family, o.Id);
                string note = o.OnlyUsedBy.Count > 0 ? "<color=#f2bf40>only referenced by unused: " + string.Join(", ", o.OnlyUsedBy) + "</color>" : "<color=#888>nothing refers to it</color>";
                GUILayout.Label(note, EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
            }
        }
    }
}
