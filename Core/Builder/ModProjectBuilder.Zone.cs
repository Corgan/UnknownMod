using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class ModProjectBuilder
    {
        // ═══════════════════════════════════════════════════════════════
        //  ZONES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a complete new zone from a ZoneDef: creates all SOs and registers in Globals.
        /// Builds all SOs for a zone and registers them in Globals.
        /// </summary>
        public static void BuildZone(ZoneDef zone)
        {
            try
            {
                // Cards, NPCs, Items, Loot, Combats, Events, Backgrounds
                // are all registered at mod-level via the Pipeline / specials.

                var nodes = new Dictionary<string, NodeData>();

                // ── Zone + Nodes ─────────────────────────────────
                var zoneData = ScriptableObject.CreateInstance<ZoneData>();
                zoneData.ZoneId = zone.ZoneId;
                zoneData.ZoneName = zone.ZoneName;
                zoneData.ObeliskLow = zone.ObeliskLow;
                zoneData.ObeliskHigh = zone.ObeliskHigh;
                zoneData.ObeliskFinal = zone.ObeliskFinal;
                zoneData.DisableExperienceOnThisZone = zone.DisableExperience;
                zoneData.DisableMadnessOnThisZone = zone.DisableMadness;
                zoneData.Sku = zone.Sku ?? "";
                zoneData.ChangeTeamOnEntrance = zone.ChangeTeamOnEntrance;
                zoneData.RestoreTeamOnExit = zone.RestoreTeamOnExit;
                if (zone.NewTeam.Count > 0)
                {
                    var teamList = new List<SubClassData>();
                    foreach (var scId in zone.NewTeam)
                    {
                        var sc = DataHelper.GetSubClass(scId);
                        if (sc != null) teamList.Add(sc);
                    }
                    zoneData.NewTeam = teamList;
                }
                DataHelper.RegisterZone(zoneData);

                foreach (var d in zone.Nodes.Values)
                {
                    var nd = BuildNodeSO(d);
                    nd.NodeZone = zoneData;
                    nodes[d.NodeId] = nd;
                    DataHelper.RegisterNode(nd);
                }
                // Resolve node connections
                foreach (var d in zone.Nodes.Values)
                {
                    if (!nodes.TryGetValue(d.NodeId, out var nd)) continue;
                    nd.NodesConnected = d.Connections
                        .Where(id => nodes.ContainsKey(id))
                        .Select(id => nodes[id])
                        .ToArray();

                    if (d.ConnectionRequirements != null && d.ConnectionRequirements.Count > 0)
                    {
                        var reqs = new List<NodesConnectedRequirement>();
                        foreach (var cr in d.ConnectionRequirements)
                        {
                            var ncr = new NodesConnectedRequirement();
                            if (!string.IsNullOrEmpty(cr.TargetNodeId) && nodes.TryGetValue(cr.TargetNodeId, out var tn))
                                ncr.NodeData = tn;
                            if (!string.IsNullOrEmpty(cr.RequirementId))
                                ncr.ConectionRequeriment = DataHelper.GetEventRequirement(cr.RequirementId);
                            if (!string.IsNullOrEmpty(cr.IfNotNodeId) && nodes.TryGetValue(cr.IfNotNodeId, out var inn))
                                ncr.ConectionIfNotNode = inn;
                            reqs.Add(ncr);
                        }
                        nd.NodesConnectedRequirement = reqs.ToArray();
                    }
                }

                Plugin.Log.LogInfo($"[Builder] Zone '{zone.ZoneId}' built: {nodes.Count} nodes");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Builder] BuildZone '{zone.ZoneId}' failed: {ex.Message}");
                Plugin.Log.LogError(ex.StackTrace);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ZONE PATCHES
        // ═══════════════════════════════════════════════════════════════

        private static void ApplyZonePatch(ZonePatchDef patch)
        {
            try
            {
                // Get existing zone from Globals
                var zoneDict = Traverse.Create(Globals.Instance)
                    .Field<Dictionary<string, ZoneData>>("_ZoneDataSource").Value;
                ZoneData zoneData = null;
                if (zoneDict != null)
                    zoneDict.TryGetValue(DataHelper.NormalizeKey(patch.TargetZoneId), out zoneData);

                if (zoneData == null)
                {
                    Plugin.Log.LogWarning($"[Builder] Zone patch target '{patch.TargetZoneId}' not found in Globals.");
                    return;
                }

                var newNodes = new Dictionary<string, NodeData>();

                // ── Build nodes ──────────────────────────────────
                foreach (var d in patch.Nodes.Values)
                {
                    var nd = BuildNodeSO(d);
                    nd.NodeZone = zoneData;
                    newNodes[d.NodeId] = nd;
                    DataHelper.RegisterNode(nd);
                }
                // Resolve node connections (can reference both new and existing nodes)
                foreach (var d in patch.Nodes.Values)
                {
                    if (!newNodes.TryGetValue(d.NodeId, out var nd)) continue;
                    nd.NodesConnected = d.Connections
                        .Select(id =>
                        {
                            if (newNodes.TryGetValue(id, out var n)) return n;
                            return DataHelper.GetExistingNode(id);
                        })
                        .Where(x => x != null)
                        .ToArray();
                }

                Plugin.Log.LogInfo($"[Builder] Zone patch '{patch.TargetZoneId}' applied: " +
                    $"{newNodes.Count} nodes, {patch.Roads.Count} roads");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Builder] ApplyZonePatch '{patch.TargetZoneId}' failed: {ex.Message}");
                Plugin.Log.LogError(ex.StackTrace);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ZONE HELPER METHODS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Build a NodeData SO from a NodeDef, wiring combat/event refs from provided dicts.</summary>
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

            if (!string.IsNullOrEmpty(d.NodeRequirementId))
                node.NodeRequirement = DataHelper.GetEventRequirement(d.NodeRequirementId);

            // Combat — resolve all combat IDs from global registry
            var combatList = new List<CombatData>();
            foreach (var cid in d.CombatIds)
            {
                if (string.IsNullOrEmpty(cid)) continue;
                var combat = DataHelper.GetExistingCombat(cid);
                if (combat != null)
                    combatList.Add(combat);
            }
            node.NodeCombat = combatList.ToArray();
            node.NodeCombatTier = d.CombatTier;

            // Event — resolve all event IDs from global registry
            var eventList = new List<EventData>();
            foreach (var eid in d.EventIds)
            {
                if (string.IsNullOrEmpty(eid)) continue;
                var evt = DataHelper.GetExistingEvent(eid);
                if (evt != null)
                    eventList.Add(evt);
            }
            node.NodeEvent = eventList.ToArray();
            node.NodeEventTier = d.NodeEventTier;

            // Per-event priority/percent arrays
            if (eventList.Count > 0)
            {
                if (d.NodeEventPriority.Count >= eventList.Count)
                    node.NodeEventPriority = d.NodeEventPriority.ToArray();
                else
                {
                    node.NodeEventPriority = new int[eventList.Count];
                    for (int i = 0; i < eventList.Count; i++)
                        node.NodeEventPriority[i] = i < d.NodeEventPriority.Count ? d.NodeEventPriority[i] : 0;
                }

                if (d.NodeEventPercent.Count >= eventList.Count)
                    node.NodeEventPercent = d.NodeEventPercent.ToArray();
                else
                {
                    node.NodeEventPercent = new int[eventList.Count];
                    int defaultPct = eventList.Count > 0 ? 100 / eventList.Count : 100;
                    for (int i = 0; i < eventList.Count; i++)
                        node.NodeEventPercent[i] = i < d.NodeEventPercent.Count ? d.NodeEventPercent[i] : defaultPct;
                }
            }
            else
            {
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
            else if (hasCombat) { node.CombatPercent = 100; node.EventPercent = 0; }
            else if (hasEvent) { node.CombatPercent = 0; node.EventPercent = 100; }

            return node;
        }

    }
}
