using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the Zones category tab with zone selector and sub-tabs:
    /// Map, Nodes, Events, Encounters, Roads.
    /// Supports mod-project scoped new zones and base-game zone patches.
    /// </summary>
    public class ZoneTabEditor
    {
        private readonly ZoneEditor _editor;
        public enum SubTab { Map, Node, Event, Encounter, Road }
        public SubTab ActiveSubTab { get; set; } = SubTab.Map;

        // ── Zone selector state ──────────────────────────────────
        public string SelectedZoneId { get; set; }
        private bool _showPatchBrowser;
        private Vector2 _patchBrowserScroll;
        private string _patchFilter = "";

        public ZoneTabEditor(ZoneEditor editor) => _editor = editor;

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null)
            {
                GUILayout.Label("<color=#888>No mod project active. Open the Mods tab first.</color>",
                    EditorStyles.RichLabel);
                return;
            }

            DrawZoneSelector(proj);
            EditorStyles.Separator();
            DrawSubTabBar();
            GUILayout.Space(4);

            // Resolve current zone/patch
            bool isNew = !string.IsNullOrEmpty(SelectedZoneId) && proj.Zones.ContainsKey(SelectedZoneId);
            bool isPatch = !string.IsNullOrEmpty(SelectedZoneId) && proj.ZonePatches.ContainsKey(SelectedZoneId);

            if (!isNew && !isPatch)
            {
                GUILayout.Label("<i>Select a zone above, or create / patch one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // Ensure ZoneLoader.CurrentZone is set for sub-editors
            if (isNew && proj.Zones.TryGetValue(SelectedZoneId, out var zoneDef))
            {
                if (ZoneLoader.CurrentZone != zoneDef)
                {
                    ZoneLoader.CurrentZone = zoneDef;
                    if (!ZoneLoader.LoadedZones.ContainsKey(zoneDef.ZoneId))
                        ZoneLoader.LoadedZones[zoneDef.ZoneId] = zoneDef;
                }
            }

            switch (ActiveSubTab)
            {
                case SubTab.Map:
                    _editor.MapView?.DrawPanel();
                    break;
                case SubTab.Node:
                    _editor.NodeEdit?.DrawPanel();
                    break;
                case SubTab.Event:
                    _editor.EventEdit?.DrawPanel();
                    break;
                case SubTab.Encounter:
                    _editor.EncounterEdit?.DrawPanel();
                    break;
                case SubTab.Road:
                    GUILayout.Label("<color=#888>Road Editor — use Map tab for visual road editing</color>",
                        EditorStyles.RichLabel);
                    break;
            }
        }

        /// <summary>Returns true if the active sub-tab shows a viewport (Map).</summary>
        public bool HasViewport => ActiveSubTab == SubTab.Map;

        /// <summary>Draw the left-side viewport if this tab has one.</summary>
        public void DrawViewport(Rect rect)
        {
            if (ActiveSubTab == SubTab.Map)
                _editor.MapView?.DrawViewport(rect);
        }

        /// <summary>Handle GUI.changed for hot-reload + auto-save.</summary>
        public bool HandleChanges()
        {
            if (!GUI.changed) return false;

            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) return false;

            bool changed = false;
            switch (ActiveSubTab)
            {
                case SubTab.Node:
                    changed = _editor.SelectedNodeId != null;
                    break;
                case SubTab.Event:
                    changed = _editor.SelectedEventId != null;
                    break;
                case SubTab.Encounter:
                    changed = _editor.SelectedCombatId != null;
                    break;
            }

            if (changed)
            {
                // Auto-save zone data
                bool isNew = !string.IsNullOrEmpty(SelectedZoneId) && proj.Zones.ContainsKey(SelectedZoneId);
                bool isPatch = !string.IsNullOrEmpty(SelectedZoneId) && proj.ZonePatches.ContainsKey(SelectedZoneId);

                if (isNew && proj.Zones.TryGetValue(SelectedZoneId, out var zone))
                {
                    ModProjectLoader.SaveZone(proj, zone);
                    proj.IsDirty = true;
                    proj.LastChangeTime = Time.realtimeSinceStartup;
                }
                else if (isPatch && proj.ZonePatches.TryGetValue(SelectedZoneId, out var patch))
                {
                    SaveZonePatch(proj, patch);
                    proj.IsDirty = true;
                    proj.LastChangeTime = Time.realtimeSinceStartup;
                }
            }
            return changed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ZONE SELECTOR
        // ═══════════════════════════════════════════════════════════════

        private void DrawZoneSelector(ModProject proj)
        {
            // Build combined zone list
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Zones.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.ZonePatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[PATCH]";
            }

            // Zone selector dropdown
            string sel = EditorFields.EntitySelector(
                SelectedZoneId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.Zones.TryGetValue(id, out var z))
                        name = z.ZoneName;
                    else if (proj.ZonePatches.TryGetValue(id, out var zp))
                        name = zp.TargetZoneId;
                    return $"{badge} {id}  {name}";
                },
                "zone_sel");
            if (sel != SelectedZoneId)
            {
                SelectedZoneId = sel;
                // Reset sub-editor selections when zone changes
                _editor.SelectedNodeId = null;
                _editor.SelectedEventId = null;
                _editor.SelectedCombatId = null;
            }

            // ── Action bar: New / Patch / Delete ─────────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New Zone", EditorStyles.MiniButton, GUILayout.Width(80)))
            {
                string newId = $"{proj.ModId}_new_zone";
                int suffix = 1;
                while (proj.Zones.ContainsKey(newId) || proj.ZonePatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_zone{suffix++}";

                var def = new ZoneDef
                {
                    ZoneId = newId,
                    ZoneName = "New Zone",
                    IdPrefix = newId.Replace("-", "_"),
                };
                proj.Zones[newId] = def;
                SelectedZoneId = newId;

                // Register in ZoneLoader so sub-editors work
                ZoneLoader.LoadedZones[newId] = def;
                ZoneLoader.CurrentZone = def;

                ModProjectLoader.SaveZone(proj, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Patch Base \u25BE", EditorStyles.MiniButton, GUILayout.Width(100)))
                _showPatchBrowser = !_showPatchBrowser;

            // Delete (new zone) / Revert (patch)
            if (!string.IsNullOrEmpty(SelectedZoneId))
            {
                bool isNew = proj.Zones.ContainsKey(SelectedZoneId);
                bool isPatch = proj.ZonePatches.ContainsKey(SelectedZoneId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Zones.Remove(SelectedZoneId);
                        ZoneLoader.LoadedZones.Remove(SelectedZoneId);
                        DeleteZoneFolder(proj, SelectedZoneId, false);
                        SelectedZoneId = allIds.FirstOrDefault(k => k != SelectedZoneId);
                        proj.IsDirty = true;
                    }
                }
                else if (isPatch)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.ZonePatches.Remove(SelectedZoneId);
                        DeleteZoneFolder(proj, SelectedZoneId, true);
                        SelectedZoneId = allIds.FirstOrDefault(k => k != SelectedZoneId);
                        proj.IsDirty = true;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Patch browser ────────────────────────────────────
            if (_showPatchBrowser)
                DrawPatchBrowser(proj);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PATCH BROWSER — list base-game zones to patch
        // ═══════════════════════════════════════════════════════════════

        private void DrawPatchBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Select a base-game zone to patch:</color>",
                EditorStyles.RichLabel);
            _patchFilter = EditorFields.TextField("Filter", _patchFilter);

            _patchBrowserScroll = GUILayout.BeginScrollView(_patchBrowserScroll, GUILayout.Height(180));
            string filterLow = (_patchFilter ?? "").ToLower();
            var allZoneIds = DataHelper.GetAllZoneIds();
            int shown = 0;
            foreach (var id in allZoneIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.ZonePatches.ContainsKey(id) || proj.Zones.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    // Detect prefix from existing node IDs in the zone
                    string prefix = DetectZonePrefix(id);
                    int nextNum = DetectNextNodeNumber(id, prefix);

                    var patch = new ZonePatchDef
                    {
                        TargetZoneId = id,
                        DetectedPrefix = prefix,
                        NextNodeNumber = nextNum,
                    };
                    proj.ZonePatches[id] = patch;
                    SelectedZoneId = id;
                    SaveZonePatch(proj, patch);
                    _showPatchBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  PREFIX DETECTION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Detect the prefix used by a base-game zone's nodes.
        /// Scans Globals._NodeDataSource for nodes that belong to the zone.
        /// </summary>
        private static string DetectZonePrefix(string zoneId)
        {
            var nodeDict = HarmonyLib.Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, NodeData>>("_NodeDataSource").Value;
            if (nodeDict == null) return zoneId.ToLower().Substring(0, System.Math.Min(4, zoneId.Length)) + "_";

            // Find nodes belonging to this zone and extract common prefix
            var zoneNodes = new List<string>();
            foreach (var kvp in nodeDict)
            {
                if (kvp.Value?.NodeZone != null &&
                    kvp.Value.NodeZone.ZoneId.Equals(zoneId, System.StringComparison.OrdinalIgnoreCase))
                    zoneNodes.Add(kvp.Value.NodeId);
            }

            if (zoneNodes.Count == 0)
                return zoneId.ToLower().Substring(0, System.Math.Min(4, zoneId.Length)) + "_";

            // Find the common prefix before the last underscore+number
            string firstNode = zoneNodes[0];
            int lastUnderscore = firstNode.LastIndexOf('_');
            if (lastUnderscore > 0)
                return firstNode.Substring(0, lastUnderscore + 1);

            return zoneId.ToLower() + "_";
        }

        /// <summary>Detect the next available node number for a zone prefix.</summary>
        private static int DetectNextNodeNumber(string zoneId, string prefix)
        {
            var nodeDict = HarmonyLib.Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, NodeData>>("_NodeDataSource").Value;
            if (nodeDict == null) return 100;

            int maxNum = -1;
            foreach (var kvp in nodeDict)
            {
                string nodeId = kvp.Key;
                if (!nodeId.StartsWith(prefix.ToLower())) continue;
                string remainder = nodeId.Substring(prefix.Length);
                if (int.TryParse(remainder, out int num))
                    maxNum = System.Math.Max(maxNum, num);
            }
            return maxNum + 1;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SAVE / DELETE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static void SaveZonePatch(ModProject proj, ZonePatchDef patch)
        {
            foreach (var kvp in patch.Nodes)
                ModProjectLoader.SaveZonePatchEntity(proj, patch.TargetZoneId, "nodes", kvp.Key, kvp.Value);
            foreach (var kvp in patch.Encounters)
                ModProjectLoader.SaveZonePatchEntity(proj, patch.TargetZoneId, "encounters", kvp.Key, kvp.Value);
            foreach (var kvp in patch.Events)
                ModProjectLoader.SaveZonePatchEntity(proj, patch.TargetZoneId, "events", kvp.Key, kvp.Value);
            if (patch.Roads.Count > 0)
                ModProjectLoader.SaveRoads(proj, patch.TargetZoneId, patch.Roads, true);
        }

        private static void DeleteZoneFolder(ModProject proj, string zoneId, bool isPatch)
        {
            string folder = isPatch
                ? System.IO.Path.Combine(ModProjectLoader.ModFolder(proj.ModId), "zones", "_patches", zoneId)
                : System.IO.Path.Combine(ModProjectLoader.ModFolder(proj.ModId), "zones", zoneId);
            if (System.IO.Directory.Exists(folder))
            {
                try { System.IO.Directory.Delete(folder, true); }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"[ZoneTab] Failed to delete zone folder: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SUB-TAB BAR
        // ═══════════════════════════════════════════════════════════════

        private void DrawSubTabBar()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("Map", SubTab.Map);
            SubTabButton("Nodes", SubTab.Node);
            SubTabButton("Events", SubTab.Event);
            SubTabButton("Encounters", SubTab.Encounter);
            SubTabButton("Roads", SubTab.Road);
            GUILayout.EndHorizontal();
        }

        private void SubTabButton(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.RichLabel : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;

            if (active)
                GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
            else if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                ActiveSubTab = tab;
        }
    }
}
