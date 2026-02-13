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
        private readonly ZoneEditor _parent;

        // Collapsible section state
        private bool _secCombat = true;
        private bool _secEvent = true;
        private bool _secPosition = false;
        private bool _secConnections = false;
        private bool _secCondConnections = false;
        private bool _secAdvanced = false;

        public NodeEditor(ZoneEditor parent) => _parent = parent;

        public void DrawPanel()
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            // ── Entity selector ──────────────────────────────────
            var nodeIds = zone.Nodes.Keys.OrderBy(k => k).ToList();
            string sel = EditorFields.EntitySelector(
                _parent.SelectedNodeId, nodeIds,
                id => $"{id}  ({zone.Nodes[id].NodeName})",
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
            GUILayout.Label($"<b>ID:</b> {nd.NodeId}", EditorStyles.RichLabel);
            nd.NodeName = EditorFields.TextField("Name", nd.NodeName);
            nd.Description = EditorFields.TextField("Description", nd.Description);
            nd.TravelDestination = EditorFields.Toggle("Travel Dest", nd.TravelDestination);
            nd.GoToTown = EditorFields.Toggle("Go To Town", nd.GoToTown);
            nd.ExistsPercent = EditorFields.IntField("Exists %", nd.ExistsPercent);
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
                    nd.CombatPercent = EditorFields.IntField("Combat %", nd.CombatPercent);
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

                    nd.EventPercent = EditorFields.IntField("Event %", nd.EventPercent);
                    nd.NodeEventTier = EditorFields.EnumField("Event Tier", nd.NodeEventTier, "node_etier");
                }
            }

            // ── Advanced ─────────────────────────────────────────
            if (EditorFields.Section("Advanced", ref _secAdvanced))
            {
                nd.DisableRandom = EditorFields.Toggle("Disable Random", nd.DisableRandom);
                nd.NodeRequirementId = EditorFields.IdDropdown("Requirement ID", nd.NodeRequirementId, DataHelper.GetAllEventRequirementIds(), "nd_reqid");
                nd.VisibleIfNotRequirement = EditorFields.Toggle("Visible If Not Req", nd.VisibleIfNotRequirement);
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
                        break;
                    }
                    GUILayout.EndHorizontal();
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
                        break;
                    }
                    GUILayout.EndVertical();
                }
                if (GUILayout.Button("+ Conn Requirement", EditorStyles.MiniButton))
                    nd.ConnectionRequirements.Add(new NodeConnectionReqDef());
            }
        }
    }
}
