using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for inspecting and editing node properties.
    /// Uses EntitySelector dropdown instead of a full list.
    /// </summary>
    public class NodeEditor
    {
        private readonly ModEditor _parent;

        // Collapsible section state
        private bool _secCombat = true;
        private bool _secEvent = true;
        private bool _secPosition = false;
        private bool _secConnections = false;
        private bool _secCondConnections = false;
        private bool _secAdvanced = false;

        public NodeEditor(ModEditor parent) => _parent = parent;

        private static string NodeLabel(ZoneDef zone, string id)
        {
            if (string.IsNullOrEmpty(id)) return "<i>(none)</i>";
            return zone.Nodes.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n.NodeName) ? $"{id}  <color=#aaa>{n.NodeName}</color>" : id;
        }

        /// <summary>Add / link / delete actions for the selected node. Structural changes run at the next Layout.</summary>
        private void DrawToolbar(ZoneDef zone)
        {
            string selId = _parent.SelectedNodeId;
            bool hasSel = selId != null && zone.Nodes.ContainsKey(selId);
            var map = _parent.MapEdit;
            bool enabled = GUI.enabled;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("+ Node", "Add a node next to the selected one (Ctrl+click on the map also works)"), EditorStyles.MiniButton))
            {
                string near = hasSel ? selId : null;
                EditorActions.PendingAction = () =>
                {
                    string id = map.AddNodeNear(near, false);
                    if (id != null) EditorStatus.Toast("Added " + id);
                };
            }
            GUI.enabled = enabled && hasSel;
            if (GUILayout.Button(new GUIContent("+ Linked Node", "Add a node next to the selected one and connect them with a road"), EditorStyles.MiniButton))
            {
                string near = selId;
                EditorActions.PendingAction = () =>
                {
                    string id = map.AddNodeNear(near, true);
                    if (id != null) EditorStatus.Toast("Added " + id + " linked to " + near);
                };
            }
            GUI.enabled = enabled && hasSel && !map.IsProtectedNode(zone, selId);
            if (GUILayout.Button(new GUIContent("Delete Node", "Delete the selected node and its roads (the entrance and town nodes cannot be deleted)"), EditorStyles.DangerButton))
            {
                string del = selId;
                EditorActions.PendingAction = () =>
                {
                    map.DeleteNodeById(del);
                    EditorStatus.Toast("Deleted " + del + " (Ctrl+Z to undo)");
                };
            }
            GUI.enabled = enabled;
            GUILayout.EndHorizontal();

            if (hasSel)
            {
                var linked = zone.Nodes[selId].Connections;
                var candidates = zone.Nodes.Keys.Where(k => k != selId && !linked.Contains(k)).OrderBy(k => k).ToList();
                string target = EditorFields.IdDropdown("Connect To", "", candidates, "nd_connect_new");
                if (!string.IsNullOrEmpty(target))
                {
                    string from = selId;
                    EditorActions.PendingAction = () =>
                    {
                        map.ConnectNodes(from, target);
                        EditorStatus.Toast("Connected " + from + " \u2194 " + target);
                    };
                }
            }
        }

        public void DrawPanel()
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            // ── Spatial instructions ─────────────────────────────
            GUILayout.Label($"<b>Nodes:</b> {zone.Nodes.Count}", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Map: drag = move, Ctrl+click = add, Backspace = delete, right-click = inspect, right-drag = pan, scroll = zoom.</color>", EditorStyles.RichLabel);
            GUILayout.Space(2);
            DrawToolbar(zone);
            EditorStyles.Separator();

            // ── Entity selector ──────────────────────────────────
            var nodeIds = zone.Nodes.Keys.OrderBy(k => k).ToList();

            // Clear stale selection if it belongs to a different zone
            if (!string.IsNullOrEmpty(_parent.SelectedNodeId) && !zone.Nodes.ContainsKey(_parent.SelectedNodeId))
                _parent.SelectedNodeId = null;

            string sel = EditorFields.EntitySelector(
                _parent.SelectedNodeId, nodeIds,
                id => zone.Nodes.TryGetValue(id, out var n) ? $"{id}  ({n.NodeName})" : id,
                "node_sel");
            if (sel != _parent.SelectedNodeId)
                _parent.SelectedNodeId = sel;

            EditorStyles.Separator();

            if (_parent.SelectedNodeId == null || !zone.Nodes.TryGetValue(_parent.SelectedNodeId, out var nd))
            {
                GUILayout.Label("<i>Select a node above.</i>", EditorStyles.RichLabel);
                return;
            }

            // ── Basic fields ─────────────────────────────────────
            string prevNodeId = nd.NodeId;
            if (ZoneEditingService.IsBaseNodeInPatch(prevNodeId)) EditorFields.ReadOnlyField("Node ID", nd.NodeId); // a patch cannot rename a vanilla node
            else nd.NodeId = EditorFields.TextField("Node ID", nd.NodeId);
            // '-' is the separator in road keys ("from-to"), so it cannot be part of a node id
            if (nd.NodeId != prevNodeId && !string.IsNullOrEmpty(nd.NodeId) && !nd.NodeId.Contains("-") && !zone.Nodes.ContainsKey(nd.NodeId))
            {
                // Re-key in zone dictionary
                zone.Nodes.Remove(prevNodeId);
                zone.Nodes[nd.NodeId] = nd;
                // Update road references and re-key roads ("from-to") that touch this node
                foreach (var road in zone.Roads.Values)
                {
                    if (road.FromNodeId == prevNodeId) road.FromNodeId = nd.NodeId;
                    if (road.ToNodeId == prevNodeId) road.ToNodeId = nd.NodeId;
                }
                foreach (var kv in zone.Roads.ToList())
                {
                    RoadEditor.ParseRoadKey(kv.Key, out var rf, out var rt);
                    if (rf == null || (rf != prevNodeId && rt != prevNodeId)) continue;
                    string newKey = (rf == prevNodeId ? nd.NodeId : rf) + "-" + (rt == prevNodeId ? nd.NodeId : rt);
                    zone.Roads.Remove(kv.Key);
                    zone.Roads[newKey] = kv.Value;
                }
                _parent.MapEdit?.ForceRebuild();
                // Update connection lists in other nodes
                foreach (var other in zone.Nodes.Values)
                {
                    for (int ci = 0; ci < other.Connections.Count; ci++)
                        if (other.Connections[ci] == prevNodeId) other.Connections[ci] = nd.NodeId;
                    foreach (var cr in other.ConnectionRequirements)
                    {
                        if (cr.TargetNodeId == prevNodeId) cr.TargetNodeId = nd.NodeId;
                        if (cr.IfNotNodeId == prevNodeId) cr.IfNotNodeId = nd.NodeId;
                    }
                }
                _parent.SelectedNodeId = nd.NodeId;
            }
            else if (nd.NodeId != prevNodeId)
                nd.NodeId = prevNodeId; // collision or empty, revert
            nd.NodeName = EditorFields.TextField("Name", nd.NodeName);
            nd.Description = EditorFields.TextField("Description", nd.Description);
            nd.TravelDestination = EditorFields.Toggle("Travel Dest", nd.TravelDestination);
            nd.GoToTown = EditorFields.Toggle("Go To Town", nd.GoToTown);
            nd.ExistsPercent = EditorFields.IntField("Exists %", nd.ExistsPercent, 0, 100);
            nd.DisableCorruption = EditorFields.Toggle("No Corruption", nd.DisableCorruption);
            nd.NodeGround = EditorFields.EnumField("Ground", nd.NodeGround, "node_ground");

            // ── Combat ───────────────────────────────────────────
            if (EditorFields.Section("Combat", ref _secCombat))
            {
                if (string.IsNullOrEmpty(nd.CombatId))
                {
                    if (GUILayout.Button("+ Add Combat", GUILayout.Height(24)))
                    {
                        string combatId = ZoneEditingService.CreateCombatForNode(_parent.SelectedNodeId);
                        if (combatId != null)
                            _parent.InspectCombat(combatId);
                    }
                    string existingCombat = EditorFields.IdDropdown("Use Existing", "", RefIds.Combats(), "nd_cmb_existing", pickerMode: EntityPicker.Mode.Combat);
                    if (!string.IsNullOrEmpty(existingCombat))
                    {
                        nd.CombatId = existingCombat;
                        GUI.changed = true;
                    }
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>{nd.CombatId}</b>", EditorStyles.RichLabel);
                    if (GUILayout.Button("\u2192 Edit", EditorStyles.LinkButton, GUILayout.Width(60)))
                        _parent.InspectCombat(nd.CombatId);
                    if (GUILayout.Button(new GUIContent("X", "Detach the combat from this node (an auto-generated combat is deleted)"), EditorStyles.DangerButton, GUILayout.Width(22)))
                        ZoneEditingService.RemoveCombatFromNode(_parent.SelectedNodeId);
                    GUILayout.EndHorizontal();

                    var combatChoices = RefIds.Combats();
                    nd.CombatIds[0] = EditorFields.IdDropdown("Combat", nd.CombatIds[0], combatChoices, "nd_cmb_0", pickerMode: EntityPicker.Mode.Combat);
                    for (int ci = 1; ci < nd.CombatIds.Count; ci++)
                    {
                        var rowOp = EditorFields.RowHeader($"<b>Combat {ci + 1}</b>", ci - 1, nd.CombatIds.Count - 1, allowDup: false);
                        nd.CombatIds[ci] = EditorFields.IdDropdown("Combat", nd.CombatIds[ci], combatChoices, $"nd_cmb_{ci}", pickerMode: EntityPicker.Mode.Combat);
                        if (EditorFields.ApplyRowOp(nd.CombatIds, ci, rowOp)) break;
                    }
                    if (GUILayout.Button("+ Combat", EditorStyles.MiniButton, GUILayout.Width(70)))
                    {
                        nd.CombatIds.Add("");
                        GUI.changed = true;
                    }

                    nd.CombatTier = EditorFields.EnumField("Combat Tier", nd.CombatTier, "node_ctier");
                    nd.CombatPercent = EditorFields.IntField("Combat %", nd.CombatPercent, 0, 100);
                }
            }

            // ── Event ────────────────────────────────────────────
            if (EditorFields.Section("Event", ref _secEvent))
            {
                if (string.IsNullOrEmpty(nd.EventId))
                {
                    if (GUILayout.Button("+ Add Event", GUILayout.Height(24)))
                    {
                        string eventId = ZoneEditingService.CreateEventForNode(_parent.SelectedNodeId);
                        if (eventId != null)
                            _parent.InspectEvent(eventId);
                    }
                    string existingEvent = EditorFields.IdDropdown("Use Existing", "", RefIds.Events(), "nd_evt_existing", pickerMode: EntityPicker.Mode.Event);
                    if (!string.IsNullOrEmpty(existingEvent))
                    {
                        nd.EventId = existingEvent;
                        GUI.changed = true;
                    }
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>{nd.EventId}</b>", EditorStyles.RichLabel);
                    if (GUILayout.Button("\u2192 Edit", EditorStyles.LinkButton, GUILayout.Width(60)))
                        _parent.InspectEvent(nd.EventId);
                    if (GUILayout.Button(new GUIContent("X", "Detach the event from this node (an auto-generated event is deleted)"), EditorStyles.DangerButton, GUILayout.Width(22)))
                        ZoneEditingService.RemoveEventFromNode(_parent.SelectedNodeId);
                    GUILayout.EndHorizontal();

                    nd.EventIds[0] = EditorFields.IdDropdown("Event", nd.EventIds[0], RefIds.Events(), "nd_evt_0", pickerMode: EntityPicker.Mode.Event);
                    nd.EventPercent = EditorFields.IntField("Event %", nd.EventPercent, 0, 100);
                    nd.NodeEventTier = EditorFields.EnumField("Event Tier", nd.NodeEventTier, "node_etier");

                    // Multi-event support
                    if (nd.EventIds.Count > 1)
                    {
                        GUILayout.Space(4);
                        GUILayout.Label($"<color=#aaa>Additional Events ({nd.EventIds.Count - 1}):</color>", EditorStyles.RichLabel);
                        var evtIds = RefIds.Events();
                        for (int i = 1; i < nd.EventIds.Count; i++)
                        {
                            var rowOp = EditorFields.RowHeader($"<b>Event {i + 1}</b>", i - 1, nd.EventIds.Count - 1, allowDup: false);
                            nd.EventIds[i] = EditorFields.IdDropdown("Event", nd.EventIds[i], evtIds, $"nd_evt_{i}", pickerMode: EntityPicker.Mode.Event);
                            while (nd.NodeEventPriority.Count < nd.EventIds.Count) nd.NodeEventPriority.Add(0);
                            while (nd.NodeEventPercent.Count < nd.EventIds.Count) nd.NodeEventPercent.Add(100);
                            nd.NodeEventPriority[i] = EditorFields.IntField("Priority", nd.NodeEventPriority[i]);
                            nd.NodeEventPercent[i] = EditorFields.IntField("Chance %", nd.NodeEventPercent[i], 0, 100);
                            if (rowOp != EditorFields.RowOp.None)
                            {
                                // Priority and chance are parallel to the event list, so they move with it
                                EditorFields.ApplyRowOp(nd.NodeEventPriority, i, rowOp);
                                EditorFields.ApplyRowOp(nd.NodeEventPercent, i, rowOp);
                                EditorFields.ApplyRowOp(nd.EventIds, i, rowOp);
                                break;
                            }
                        }
                    }
                    if (GUILayout.Button("+ Event", EditorStyles.MiniButton, GUILayout.Width(70)))
                    {
                        nd.EventIds.Add("");
                        nd.NodeEventPriority.Add(0);
                        nd.NodeEventPercent.Add(100);
                        GUI.changed = true;
                    }
                }
            }

            // ── Advanced ─────────────────────────────────────────
            if (EditorFields.Section("Advanced", ref _secAdvanced))
            {
                nd.DisableRandom = EditorFields.Toggle("Disable Random", nd.DisableRandom);
                nd.NodeRequirementId = EditorFields.IdDropdown("Requirement ID", nd.NodeRequirementId, RefIds.Requirements(), "nd_reqid");
                nd.VisibleIfNotRequirement = EditorFields.Toggle("Visible If Not Req", nd.VisibleIfNotRequirement);
                nd.ExistsSku = EditorFields.TextField("Exists SKU", nd.ExistsSku);
                nd.ReplacementEventId = EditorFields.IdDropdown("Replacement Event (no SKU)", nd.ReplacementEventId, RefIds.Events(), "nd_repevt", pickerMode: EntityPicker.Mode.Event);
                nd.ReplacementCombatId = EditorFields.IdDropdown("Replacement Combat (no SKU)", nd.ReplacementCombatId, RefIds.Combats(), "nd_repcmb", pickerMode: EntityPicker.Mode.Combat);
                nd.NodeBackgroundSprite = EditorFields.TextField("Map Background Sprite", nd.NodeBackgroundSprite);
                nd.HeroToDisableNodeWhenUnlockedId = EditorFields.IdDropdown("Disable When Hero In Team", nd.HeroToDisableNodeWhenUnlockedId, RefIds.SubClasses(), "nd_herodisable", pickerMode: EntityPicker.Mode.Hero);
            }

            // ── Position ─────────────────────────────────────────
            if (EditorFields.Section("Position", ref _secPosition))
            {
                nd.PosX = EditorFields.FloatField("X", nd.PosX);
                nd.PosY = EditorFields.FloatField("Y", nd.PosY);
            }

            // ── Connections ──────────────────────────────────────
            if (EditorFields.Section($"Connections ({nd.Connections.Count})", ref _secConnections))
            {
                var connIds = nodeIds.Where(x => x != nd.NodeId).ToList();
                for (int i = 0; i < nd.Connections.Count; i++)
                {
                    string cid = nd.Connections[i];
                    var rowOp = EditorFields.RowHeader($"<b>\u2192</b> {NodeLabel(zone, cid)}", i, nd.Connections.Count, allowDup: false);
                    nd.Connections[i] = EditorFields.IdDropdown("Node", nd.Connections[i], connIds, $"conn_{i}");
                    if (rowOp == EditorFields.RowOp.Remove)
                    {
                        _parent.MapEdit.DisconnectNodes(nd.NodeId, cid);
                        if (i < nd.Connections.Count && nd.Connections[i] == cid) nd.Connections.RemoveAt(i);
                        GUI.changed = true;
                        break;
                    }
                    if (EditorFields.ApplyRowOp(nd.Connections, i, rowOp)) break;
                }
                if (GUILayout.Button("+ Connection", EditorStyles.MiniButton))
                {
                    nd.Connections.Add("");
                    GUI.changed = true;
                }
            }

            // ── Connection Requirements ──────────────────────────
            if (EditorFields.Section($"Conn. Requirements ({nd.ConnectionRequirements.Count})", ref _secCondConnections))
            {
                for (int i = 0; i < nd.ConnectionRequirements.Count; i++)
                {
                    var cr = nd.ConnectionRequirements[i];
                    GUILayout.BeginVertical(EditorStyles.CompactBox);
                    var rowOp = EditorFields.RowHeader($"<b>#{i + 1}</b>  {NodeLabel(zone, cr.TargetNodeId)}", i, nd.ConnectionRequirements.Count);
                    cr.TargetNodeId = EditorFields.IdDropdown("Target", cr.TargetNodeId, nodeIds, $"cr_tgt_{i}");
                    cr.RequirementId = EditorFields.IdDropdown("Req ID", cr.RequirementId, RefIds.Requirements(), $"cr_req_{i}");
                    cr.IfNotNodeId = EditorFields.IdDropdown("If Not Node", cr.IfNotNodeId, nodeIds, $"cr_ifnot_{i}");
                    bool listChanged = EditorFields.ApplyRowOp(nd.ConnectionRequirements, i, rowOp);
                    GUILayout.EndVertical();
                    if (listChanged) break;
                }
                if (GUILayout.Button("+ Conn Requirement", EditorStyles.MiniButton))
                {
                    nd.ConnectionRequirements.Add(new NodeConnectionReqDef());
                    GUI.changed = true;
                }
            }
        }
    }
}
