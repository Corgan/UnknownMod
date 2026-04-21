using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;
using UnknownMod.Editor;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Manages the World category tab with sub-tabs:
    /// Zones (Map/Nodes/Roads), Events, Encounters, Backgrounds.
    /// Supports mod-project scoped new zones and base-game zone patches.
    /// </summary>
    public class ZoneTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { Zones, Event, Encounter, Background }
        public enum ZoneInnerTab { Map, Node, Road }
        public SubTab ActiveSubTab { get; set; } = SubTab.Zones;
        public ZoneInnerTab ActiveZoneInnerTab { get; set; } = ZoneInnerTab.Map;

        // ── Zone selector state ──────────────────────────────────
        public string SelectedZoneId { get; set; }
        private bool _showPatchBrowser;
        private Vector2 _patchBrowserScroll;
        private string _patchFilter = "";

        public ZoneTabEditor(ModEditor editor) => _editor = editor;

        /// <summary>Per-frame tick — handles zone auto-save timer.</summary>
        public void Tick()
        {
            ZoneEditingService.TickAutoSave();
        }

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null)
            {
                GUILayout.Label("<color=#888>No mod project active. Open the Mods tab first.</color>",
                    EditorStyles.RichLabel);
                return;
            }

            DrawSubTabBar();
            GUILayout.Space(4);

            switch (ActiveSubTab)
            {
                case SubTab.Zones:
                    DrawZonesPanel(proj);
                    break;
                case SubTab.Event:
                    _editor.EventEdit?.DrawPanel();
                    break;
                case SubTab.Encounter:
                    _editor.EncounterEdit?.DrawPanel();
                    break;
                case SubTab.Background:
                    _editor.BackgroundEdit?.DrawPanel();
                    break;
            }
        }

        /// <summary>Draw the Zones sub-panel with zone selector + inner tabs (Map/Nodes/Roads).</summary>
        private void DrawZonesPanel(ModProject proj)
        {
            DrawZoneSelector(proj);
            EditorStyles.Separator();
            DrawZoneInnerTabBar();
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

            // Ensure ZoneEditingService.CurrentZone is set for sub-editors
            if (isNew && proj.Zones.TryGetValue(SelectedZoneId, out var zoneDef))
            {
                if (ZoneEditingService.CurrentZone != zoneDef)
                {
                    ZoneEditingService.CurrentZone = zoneDef;
                    ZoneEditingService.CurrentPatch = null;
                    if (!ModRegistry.LoadedZones.ContainsKey(zoneDef.ZoneId))
                        ModRegistry.LoadedZones[zoneDef.ZoneId] = zoneDef;
                }
            }
            else if (isPatch && proj.ZonePatches.TryGetValue(SelectedZoneId, out var patchDef))
            {
                var synth = ZoneEditingService.SynthesizeZoneDef(patchDef);
                if (synth != null)
                {
                    if (ZoneEditingService.CurrentZone != synth)
                    {
                        ZoneEditingService.CurrentZone = synth;
                        ZoneEditingService.CurrentPatch = patchDef;
                    }
                }
                else if (ZoneEditingService.CurrentZone != null
                         && ZoneEditingService.CurrentZone.ZoneId != patchDef.TargetZoneId)
                {
                    // Synthesis not ready yet (async base data loading).
                    // Clear CurrentZone so we show "loading" instead of stale data
                    // from a completely different zone.
                    ZoneEditingService.CurrentZone = null;
                    ZoneEditingService.CurrentPatch = null;
                }
            }

            switch (ActiveZoneInnerTab)
            {
                case ZoneInnerTab.Map:
                    _editor.MapEdit?.DrawPanel();
                    break;
                case ZoneInnerTab.Node:
                    _editor.NodeEdit?.DrawPanel();
                    break;
                case ZoneInnerTab.Road:
                    DrawRoadPanel();
                    break;
            }
        }

        /// <summary>Returns true if the active sub-tab shows a viewport.</summary>
        public bool HasViewport => true;

        // ═══════════════════════════════════════════════════════════════
        //  ROAD TAB PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawRoadPanel()
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            GUILayout.Label("<b>Road Controls</b>", EditorStyles.RichLabel);
            GUILayout.Space(2);
            GUILayout.Label("<color=#888>Shift+click two nodes to connect/disconnect</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Ctrl+click waypoint to insert new point</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Drag waypoint to reshape road</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Backspace on waypoint to remove it</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Click node to select for context</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Escape to cancel connection mode</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Right-drag to pan, scroll to zoom</color>", EditorStyles.RichLabel);

            GUILayout.Space(4);

            int roadCount = _editor.MapEdit?.RoadCount ?? 0;
            GUILayout.Label($"<b>Roads:</b> {roadCount}", EditorStyles.RichLabel);

            var connectId = _editor.MapEdit?.ConnectFirstId;
            if (connectId != null)
            {
                GUILayout.Space(4);
                GUILayout.Label($"<color=yellow>Connecting from: {connectId}</color>", EditorStyles.RichLabel);
                GUILayout.Label("<color=#888>Shift+click another node or Esc to cancel</color>", EditorStyles.RichLabel);
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Rebuild Viewport", EditorStyles.MiniButton))
                _editor.MapEdit?.ForceRebuild();
        }

        /// <summary>Draw the left-side viewport for the active sub-tab.</summary>
        public void DrawViewport(Rect rect)
        {
            switch (ActiveSubTab)
            {
                case SubTab.Zones:
                    EnsureCurrentZone();
                    switch (ActiveZoneInnerTab)
                    {
                        case ZoneInnerTab.Map:
                        case ZoneInnerTab.Node:
                        case ZoneInnerTab.Road:
                            _editor.MapEdit?.DrawViewport(rect);
                            break;
                    }
                    break;
                case SubTab.Event:
                    DrawEventPreview(rect);
                    break;
                case SubTab.Encounter:
                    DrawEncounterPreview(rect);
                    break;
                case SubTab.Background:
                    _editor.BackgroundEdit?.DrawViewport(rect);
                    break;
            }
        }

        // ── Viewport previews ────────────────────────────────────

        private void DrawEventPreview(Rect rect)
        {
            string evtId = _editor.SelectedEventId;
            EventDef def = null;

            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(evtId))
            {
                if (!proj.Events.TryGetValue(evtId, out def))
                    proj.EventPatches.TryGetValue(evtId, out def);
            }

            ViewportPreview.DrawEvent(rect, evtId, def);
        }

        private void DrawEncounterPreview(Rect rect)
        {
            string combatId = _editor.SelectedCombatId;
            CombatDef def = null;

            var proj = ModManagerPanel.ActiveProject;
            if (proj != null && !string.IsNullOrEmpty(combatId))
            {
                if (!proj.Combats.TryGetValue(combatId, out def))
                    proj.CombatPatches.TryGetValue(combatId, out def);
            }

            ViewportPreview.DrawEncounter(rect, combatId, def);
        }

        /// <summary>Set CurrentZone from the selected zone. Called before viewport draw.</summary>
        private void EnsureCurrentZone()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) return;

            // Auto-select first zone if nothing selected
            if (string.IsNullOrEmpty(SelectedZoneId))
            {
                if (proj.Zones.Count > 0)
                    SelectedZoneId = proj.Zones.Keys.First();
                else if (proj.ZonePatches.Count > 0)
                    SelectedZoneId = proj.ZonePatches.Keys.First();
            }

            if (string.IsNullOrEmpty(SelectedZoneId)) return;

            // New (custom) zone
            if (proj.Zones.TryGetValue(SelectedZoneId, out var zoneDef))
            {
                if (ZoneEditingService.CurrentZone != zoneDef)
                {
                    ZoneEditingService.CurrentZone = zoneDef;
                    ZoneEditingService.CurrentPatch = null;
                    if (!ModRegistry.LoadedZones.ContainsKey(zoneDef.ZoneId))
                        ModRegistry.LoadedZones[zoneDef.ZoneId] = zoneDef;
                }
                return;
            }

            // Zone patch — synthesize a full ZoneDef from base + patch
            if (proj.ZonePatches.TryGetValue(SelectedZoneId, out var patch))
            {
                var synth = ZoneEditingService.SynthesizeZoneDef(patch);
                if (synth != null)
                {
                    if (ZoneEditingService.CurrentZone != synth)
                    {
                        ZoneEditingService.CurrentZone = synth;
                        ZoneEditingService.CurrentPatch = patch;
                    }
                }
                else if (ZoneEditingService.CurrentZone != null
                         && ZoneEditingService.CurrentZone.ZoneId != patch.TargetZoneId)
                {
                    ZoneEditingService.CurrentZone = null;
                    ZoneEditingService.CurrentPatch = null;
                }
            }
        }

        /// <summary>Handle GUI.changed for hot-reload + auto-save.</summary>
        public void HandleChanges()
        {
            if (!GUI.changed) return;

            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) return;

            bool changed = false;
            switch (ActiveSubTab)
            {
                case SubTab.Zones:
                    changed = (ActiveZoneInnerTab == ZoneInnerTab.Node && _editor.SelectedNodeId != null)
                           || ActiveZoneInnerTab == ZoneInnerTab.Map;
                    break;
                case SubTab.Event:
                    changed = _editor.SelectedEventId != null;
                    break;
                case SubTab.Encounter:
                    changed = _editor.SelectedCombatId != null;
                    break;
            }

            if (!changed) return;

            if (ActiveSubTab == SubTab.Event && _editor.SelectedEventId != null)
            {
                EventDef evtDef = null;
                if (proj.Events.TryGetValue(_editor.SelectedEventId, out evtDef))
                    ModProjectLoader.SaveEntity(proj, "events", _editor.SelectedEventId, evtDef);
                else if (proj.EventPatches.TryGetValue(_editor.SelectedEventId, out evtDef))
                    ModProjectLoader.SaveEntity(proj, "events", _editor.SelectedEventId, evtDef, isPatch: true);
            }
            else if (ActiveSubTab == SubTab.Encounter && _editor.SelectedCombatId != null)
            {
                CombatDef combatDef = null;
                if (proj.Combats.TryGetValue(_editor.SelectedCombatId, out combatDef))
                    ModProjectLoader.SaveEntity(proj, "combats", _editor.SelectedCombatId, combatDef);
                else if (proj.CombatPatches.TryGetValue(_editor.SelectedCombatId, out combatDef))
                    ModProjectLoader.SaveEntity(proj, "combats", _editor.SelectedCombatId, combatDef, isPatch: true);
            }
            else
            {
                // Node/Road changes: save zone data
                bool isNew = !string.IsNullOrEmpty(SelectedZoneId) && proj.Zones.ContainsKey(SelectedZoneId);
                bool isPatch = !string.IsNullOrEmpty(SelectedZoneId) && proj.ZonePatches.ContainsKey(SelectedZoneId);

                if (isNew && proj.Zones.TryGetValue(SelectedZoneId, out var zone))
                {
                    ModProjectLoader.SaveZone(proj, zone);
                }
                else if (isPatch && proj.ZonePatches.TryGetValue(SelectedZoneId, out var patch))
                {
                    SaveZonePatch(proj, patch);
                    ZoneEditingService.InvalidateSynthesizedZone(patch.TargetZoneId);
                }
            }

            proj.IsDirty = true;
            proj.LastChangeTime = Time.realtimeSinceStartup;
            ModEditor.EntityPreview?.Invalidate();
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
                // Clear CurrentZone immediately so viewport/sub-editors
                // don't keep rendering the previous zone's data while
                // the new zone (or its synthesis) is being resolved.
                ZoneEditingService.CurrentZone = null;
                ZoneEditingService.CurrentPatch = null;
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
                    VisualLayers = new System.Collections.Generic.List<VisualLayerDef>
                    {
                        new VisualLayerDef
                        {
                            Name = "Background_Bg",
                            Type = VisualLayerType.Sprite,
                            SpriteName = "background.jpeg",
                            SortingOrder = -10,
                            SortingLayer = "Map",
                            Visible = true,
                            IsOverride = true,
                        }
                    },
                };
                proj.Zones[newId] = def;
                SelectedZoneId = newId;

                // Register in ModRegistry so sub-editors work
                ModRegistry.LoadedZones[newId] = def;
                ZoneEditingService.CurrentZone = def;

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
                        ModRegistry.LoadedZones.Remove(SelectedZoneId);
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

        internal static void SaveZonePatch(ModProject proj, ZonePatchDef patch)
        {
            foreach (var kvp in patch.Nodes)
                ModProjectLoader.SaveZonePatchEntity(proj, patch.TargetZoneId, "nodes", kvp.Key, kvp.Value);
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
            SubTabButton("Zones", SubTab.Zones);
            SubTabButton("Events", SubTab.Event);
            SubTabButton("Encounters", SubTab.Encounter);
            SubTabButton("BGs", SubTab.Background);
            GUILayout.EndHorizontal();
        }

        private void DrawZoneInnerTabBar()
        {
            GUILayout.BeginHorizontal();
            ZoneInnerTabButton("Map", ZoneInnerTab.Map);
            ZoneInnerTabButton("Nodes", ZoneInnerTab.Node);
            ZoneInnerTabButton("Roads", ZoneInnerTab.Road);
            GUILayout.EndHorizontal();
        }

        private void SubTabButton(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;

            if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                ActiveSubTab = tab;
        }

        private void ZoneInnerTabButton(string label, ZoneInnerTab tab)
        {
            bool active = ActiveZoneInnerTab == tab;
            var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;

            if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                ActiveZoneInnerTab = tab;
        }
    }
}
