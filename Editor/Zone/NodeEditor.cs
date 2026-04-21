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

        public void DrawPanel()
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            // ── Spatial instructions ─────────────────────────────
            GUILayout.Label("<b>Node Controls</b>", EditorStyles.RichLabel);
            GUILayout.Space(2);
            GUILayout.Label("<color=#888>Drag node to reposition</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Ctrl+click empty space to add node</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Backspace on hovered node to remove</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Right-click node to inspect</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Right-drag to pan, scroll to zoom</color>", EditorStyles.RichLabel);

            GUILayout.Space(4);
            GUILayout.Label($"<b>Nodes:</b> {zone.Nodes.Count}", EditorStyles.RichLabel);

            GUILayout.Space(4);
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
            nd.NodeId = EditorFields.TextField("Node ID", nd.NodeId);
            if (nd.NodeId != prevNodeId && !string.IsNullOrEmpty(nd.NodeId) && !zone.Nodes.ContainsKey(nd.NodeId))
            {
                // Re-key in zone dictionary
                zone.Nodes.Remove(prevNodeId);
                zone.Nodes[nd.NodeId] = nd;
                // Update road references
                foreach (var road in zone.Roads.Values)
                {
                    if (road.FromNodeId == prevNodeId) road.FromNodeId = nd.NodeId;
                    if (road.ToNodeId == prevNodeId) road.ToNodeId = nd.NodeId;
                }
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
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>{nd.CombatId}</b>", EditorStyles.RichLabel);
                    if (GUILayout.Button("\u2192 Edit", EditorStyles.LinkButton, GUILayout.Width(60)))
                        _parent.InspectCombat(nd.CombatId);
                    if (GUILayout.Button("X", EditorStyles.MiniButton, GUILayout.Width(22)))
                        ZoneEditingService.RemoveCombatFromNode(_parent.SelectedNodeId);
                    GUILayout.EndHorizontal();

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
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>{nd.EventId}</b>", EditorStyles.RichLabel);
                    if (GUILayout.Button("\u2192 Edit", EditorStyles.LinkButton, GUILayout.Width(60)))
                        _parent.InspectEvent(nd.EventId);
                    if (GUILayout.Button("X", EditorStyles.MiniButton, GUILayout.Width(22)))
                        ZoneEditingService.RemoveEventFromNode(_parent.SelectedNodeId);
                    GUILayout.EndHorizontal();

                    nd.EventPercent = EditorFields.IntField("Event %", nd.EventPercent, 0, 100);
                    nd.NodeEventTier = EditorFields.EnumField("Event Tier", nd.NodeEventTier, "node_etier");

                    // Multi-event support
                    if (nd.EventIds.Count > 1)
                    {
                        GUILayout.Space(4);
                        GUILayout.Label($"<color=#aaa>Additional Events ({nd.EventIds.Count - 1}):</color>", EditorStyles.RichLabel);
                        var evtIds = new List<string>(DataHelper.GetAllEventIds());
                        for (int i = 1; i < nd.EventIds.Count; i++)
                        {
                            GUILayout.BeginHorizontal();
                            nd.EventIds[i] = EditorFields.IdDropdown($"Event {i + 1}", nd.EventIds[i], evtIds, $"nd_evt_{i}", pickerMode: EntityPicker.Mode.Event);
                            // Priority & Percent inline
                            while (nd.NodeEventPriority.Count <= i) nd.NodeEventPriority.Add(0);
                            while (nd.NodeEventPercent.Count <= i) nd.NodeEventPercent.Add(100);
                            nd.NodeEventPriority[i] = EditorFields.IntField("Pri", nd.NodeEventPriority[i]);
                            nd.NodeEventPercent[i] = EditorFields.IntField("%", nd.NodeEventPercent[i], 0, 100);
                            if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                            {
                                nd.EventIds.RemoveAt(i);
                                if (nd.NodeEventPriority.Count > i) nd.NodeEventPriority.RemoveAt(i);
                                if (nd.NodeEventPercent.Count > i) nd.NodeEventPercent.RemoveAt(i);
                                GUI.changed = true;
                                GUILayout.EndHorizontal();
                                break;
                            }
                            GUILayout.EndHorizontal();
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
                nd.NodeRequirementId = EditorFields.IdDropdown("Requirement ID", nd.NodeRequirementId, DataHelper.GetAllEventRequirementIds(), "nd_reqid");
                nd.VisibleIfNotRequirement = EditorFields.Toggle("Visible If Not Req", nd.VisibleIfNotRequirement);
                nd.ExistsSku = EditorFields.TextField("Exists SKU", nd.ExistsSku);
                nd.HeroToDisableNodeWhenUnlockedId = EditorFields.IdDropdown("Disable When Unlocked", nd.HeroToDisableNodeWhenUnlockedId, DataHelper.GetAllSubClassIds(), "nd_herodisable", pickerMode: EntityPicker.Mode.Hero);
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
                for (int i = 0; i < nd.Connections.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    var connIds = nodeIds.Where(x => x != nd.NodeId).ToList();
                    nd.Connections[i] = EditorFields.IdDropdown("", nd.Connections[i], connIds, $"conn_{i}");
                    if (GUILayout.Button("X", EditorStyles.MiniButton, GUILayout.Width(22)))
                    {
                        nd.Connections.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
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
                    cr.TargetNodeId = EditorFields.IdDropdown("Target", cr.TargetNodeId, nodeIds, $"cr_tgt_{i}");
                    cr.RequirementId = EditorFields.IdDropdown("Req ID", cr.RequirementId, DataHelper.GetAllEventRequirementIds(), $"cr_req_{i}");
                    cr.IfNotNodeId = EditorFields.IdDropdown("If Not Node", cr.IfNotNodeId, nodeIds, $"cr_ifnot_{i}");
                    if (GUILayout.Button("X", EditorStyles.MiniButton, GUILayout.Width(22)))
                    {
                        nd.ConnectionRequirements.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndVertical();
                        break;
                    }
                    GUILayout.EndVertical();
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
