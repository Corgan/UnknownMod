using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class ZoneEditingService
    {
        // ═══════════════════════════════════════════════════════════════
        //  FACTORY HELPERS (used by AddNode / editing operations)
        // ═══════════════════════════════════════════════════════════════

        private static NodeData BuildNodeSO(NodeDef d)
        {
            var node = ScriptableObject.CreateInstance<NodeData>();
            node.NodeId = d.NodeId;
            node.NodeName = d.NodeName;
            node.Description = d.Description ?? "";
            node.TravelDestination = d.TravelDestination;
            node.GoToTown = d.GoToTown;
            node.ExistsPercent = d.ExistsPercent;
            node.NodeGround = d.NodeGround;
            node.DisableCorruption = d.DisableCorruption;
            node.DisableRandom = d.DisableRandom;
            node.VisibleIfNotRequirement = d.VisibleIfNotRequirement;
            node.ExistsSku = d.ExistsSku ?? "";
            node.SourceNodeName = "";
            node.NodesConnected = new NodeData[0];

            // Hero unlock gating
            if (!string.IsNullOrEmpty(d.HeroToDisableNodeWhenUnlockedId))
            {
                var hero = DataHelper.GetSubClass(d.HeroToDisableNodeWhenUnlockedId);
                if (hero != null)
                    Traverse.Create(node).Field("heroToDisableNodeWhenUnlocked").SetValue(hero);
            }

            // Conditional connection requirements
            if (d.ConnectionRequirements != null && d.ConnectionRequirements.Count > 0)
            {
                var reqs = new NodesConnectedRequirement[d.ConnectionRequirements.Count];
                for (int i = 0; i < d.ConnectionRequirements.Count; i++)
                {
                    var cr = d.ConnectionRequirements[i];
                    reqs[i] = new NodesConnectedRequirement();
                    if (!string.IsNullOrEmpty(cr.TargetNodeId))
                        reqs[i].NodeData = DataHelper.GetExistingNode(cr.TargetNodeId);
                    if (!string.IsNullOrEmpty(cr.RequirementId))
                        reqs[i].ConectionRequeriment = DataHelper.GetEventRequirement(cr.RequirementId);
                    if (!string.IsNullOrEmpty(cr.IfNotNodeId))
                        reqs[i].ConectionIfNotNode = DataHelper.GetExistingNode(cr.IfNotNodeId);
                }
                node.NodesConnectedRequirement = reqs;
            }
            else
            {
                node.NodesConnectedRequirement = new NodesConnectedRequirement[0];
            }

            // Node requirement
            if (!string.IsNullOrEmpty(d.NodeRequirementId))
                node.NodeRequirement = DataHelper.GetEventRequirement(d.NodeRequirementId);

            // Combat — lookup all combat IDs from Globals
            if (d.CombatIds.Count > 0)
            {
                var combatDict = Traverse.Create(Globals.Instance)
                    .Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
                var combats = new List<CombatData>();
                foreach (var cid in d.CombatIds)
                {
                    if (string.IsNullOrEmpty(cid)) continue;
                    CombatData combat = null;
                    combatDict?.TryGetValue(DataHelper.NormalizeKey(cid), out combat);
                    if (combat != null)
                        combats.Add(combat);
                }
                node.NodeCombat = combats.ToArray();
                node.NodeCombatTier = d.CombatTier;
            }
            else
                node.NodeCombat = new CombatData[0];

            // Event — lookup all event IDs from Globals
            if (d.EventIds.Count > 0)
            {
                var events = new List<EventData>();
                foreach (var eid in d.EventIds)
                {
                    if (string.IsNullOrEmpty(eid)) continue;
                    var evt = DataHelper.GetExistingEvent(eid);
                    if (evt != null)
                        events.Add(evt);
                }
                node.NodeEvent = events.ToArray();
                node.NodeEventTier = d.NodeEventTier;

                if (d.NodeEventPriority.Count >= events.Count)
                    node.NodeEventPriority = d.NodeEventPriority.ToArray();
                else
                {
                    node.NodeEventPriority = new int[events.Count];
                    for (int i = 0; i < events.Count; i++)
                        node.NodeEventPriority[i] = i < d.NodeEventPriority.Count ? d.NodeEventPriority[i] : 0;
                }

                if (d.NodeEventPercent.Count >= events.Count)
                    node.NodeEventPercent = d.NodeEventPercent.ToArray();
                else
                {
                    node.NodeEventPercent = new int[events.Count];
                    int defaultPct = events.Count > 0 ? 100 / events.Count : 100;
                    for (int i = 0; i < events.Count; i++)
                        node.NodeEventPercent[i] = i < d.NodeEventPercent.Count ? d.NodeEventPercent[i] : defaultPct;
                }
            }
            else
            {
                node.NodeEvent = new EventData[0];
                node.NodeEventPriority = new int[0];
                node.NodeEventPercent = new int[0];
            }

            // Combat/event percentages
            bool hasCombat = node.NodeCombat.Length > 0;
            bool hasEvent = node.NodeEvent.Length > 0;

            if (d.CombatPercent >= 0)
            {
                node.CombatPercent = d.CombatPercent;
                node.EventPercent = d.EventPercent >= 0 ? d.EventPercent : 100 - d.CombatPercent;
            }
            else if (hasCombat && hasEvent)
            {
                node.CombatPercent = 50;
                node.EventPercent = 50;
            }
            else if (hasCombat)
            {
                node.CombatPercent = 100;
                node.EventPercent = 0;
            }
            else if (hasEvent)
            {
                node.CombatPercent = 0;
                node.EventPercent = 100;
            }

            return node;
        }
    }
}
