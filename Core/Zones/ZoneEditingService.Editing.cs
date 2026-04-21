using System;
using System.Collections.Generic;
using System.Linq;
using UnknownMod.Definitions;
using UnknownMod.Editor;

namespace UnknownMod.Core
{
    public static partial class ZoneEditingService
    {
        // 
        //  NODE ADD / DELETE
        // 

        public static string AddNode(float posX, float posY)
        {
            if (CurrentZone == null) return null;

            string prefix;
            int nextNum;

            // In patch mode, use the patch's prefix and tracked NextNodeNumber
            if (CurrentPatch != null)
            {
                prefix = CurrentPatch.DetectedPrefix.TrimEnd('_');
                nextNum = CurrentPatch.NextNodeNumber;

                // Also scan CurrentZone to avoid collisions with base nodes
                foreach (var id in CurrentZone.Nodes.Keys)
                {
                    if (id.StartsWith(prefix + "_") && int.TryParse(id.Substring(prefix.Length + 1), out int num))
                        nextNum = Math.Max(nextNum, num + 1);
                }
            }
            else
            {
                prefix = CurrentZone.IdPrefix;
                if (string.IsNullOrEmpty(prefix))
                {
                    Plugin.Log.LogError("[ZoneEditing] Cannot add node: zone has no IdPrefix.");
                    return null;
                }

                nextNum = 0;
                foreach (var id in CurrentZone.Nodes.Keys)
                {
                    if (id.StartsWith(prefix + "_") && int.TryParse(id.Substring(prefix.Length + 1), out int num))
                        nextNum = Math.Max(nextNum, num + 1);
                }
            }

            string nodeId = $"{prefix}_{nextNum}";
            var nodeDef = new NodeDef { NodeId = nodeId, NodeName = $"New Node {nextNum}", PosX = posX, PosY = posY };

            CurrentZone.Nodes[nodeId] = nodeDef;

            // In patch mode, also add to the patch def directly
            if (CurrentPatch != null)
            {
                CurrentPatch.Nodes[nodeId] = nodeDef;
                CurrentPatch.NextNodeNumber = nextNum + 1;
            }

            // Build SO and register in Globals
            var node = BuildNodeSO(nodeDef);
            if (Globals.Instance != null)
            {
                var zoneData = DataHelper.GetExistingZone(CurrentZone.ZoneId);
                if (zoneData != null)
                    node.NodeZone = zoneData;
            }
            DataHelper.RegisterNode(node);

            MarkDirty();
            Plugin.Log.LogInfo($"[ZoneEditing] Added node '{nodeId}' at ({posX:F1}, {posY:F1})");
            return nodeId;
        }

        public static void DeleteNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;

            // In patch mode, only allow deleting patch-added nodes (not base-game nodes)
            if (CurrentPatch != null && !CurrentPatch.Nodes.ContainsKey(nodeId))
            {
                Plugin.Log.LogWarning($"[ZoneEditing] Cannot delete base-game node '{nodeId}' from a zone patch.");
                return;
            }

            // Clean up auto-generated combats for ALL combat IDs
            var proj = UnknownMod.Editor.Tabs.ModManagerPanel.ActiveProject;
            foreach (var cid in nodeDef.CombatIds)
            {
                if (string.IsNullOrEmpty(cid)) continue;
                string expectedCombatId = "c" + nodeId;
                if (cid == expectedCombatId)
                {
                    if (proj != null) proj.Combats.Remove(cid);
                }
            }

            // Clean up auto-generated events for ALL event IDs (prefix match, consistent with patch cleanup)
            foreach (var eid in nodeDef.EventIds)
            {
                if (string.IsNullOrEmpty(eid)) continue;
                if (eid.StartsWith($"e_{nodeId}_"))
                {
                    if (proj != null) proj.Events.Remove(eid);
                }
            }

            var roadsToRemove = CurrentZone.Roads.Keys
                .Where(k => k.StartsWith(nodeId + "-") || k.EndsWith("-" + nodeId))
                .ToList();
            foreach (var key in roadsToRemove)
                CurrentZone.Roads.Remove(key);

            foreach (var otherNode in CurrentZone.Nodes.Values)
                otherNode.Connections.Remove(nodeId);

            CurrentZone.Nodes.Remove(nodeId);

            // Also remove from patch def if in patch mode
            if (CurrentPatch != null)
            {
                CurrentPatch.Nodes.Remove(nodeId);
                var patchRoadsToRemove = CurrentPatch.Roads.Keys
                    .Where(k => k.StartsWith(nodeId + "-") || k.EndsWith("-" + nodeId))
                    .ToList();
                foreach (var key in patchRoadsToRemove)
                    CurrentPatch.Roads.Remove(key);
            }

            MarkDirty();
            Plugin.Log.LogInfo($"[ZoneEditing] Deleted node '{nodeId}'");
        }

        // 
        //  AUTO-ID: Generate combat/event IDs from node IDs
        // 

        public static string CreateCombatForNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return null;
            if (!string.IsNullOrEmpty(nodeDef.CombatId)) return nodeDef.CombatId;

            var proj = UnknownMod.Editor.Tabs.ModManagerPanel.ActiveProject;
            if (proj == null) return null;

            string combatId = "c" + nodeId;
            var combatDef = new CombatDef { CombatId = combatId, Description = $"Combat at {nodeDef.NodeName}" };

            proj.Combats[combatId] = combatDef;
            nodeDef.CombatId = combatId;



            var combat = DataHelper.MakeCombat(combatDef, new NPCData[0]);
            DataHelper.RegisterCombat(combat);

            ModProjectLoader.SaveEntity(proj, "combats", combatId, combatDef);

            MarkDirty();

            Plugin.Log.LogInfo($"[ZoneEditing] Created combat '{combatId}' for node '{nodeId}'");
            return combatId;
        }

        public static string CreateEventForNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return null;
            if (!string.IsNullOrEmpty(nodeDef.EventId)) return nodeDef.EventId;

            var proj = UnknownMod.Editor.Tabs.ModManagerPanel.ActiveProject;
            if (proj == null) return null;

            string eventId = $"e_{nodeId}_a";
            var eventDef = new EventDef
            {
                EventId = eventId,
                EventName = nodeDef.NodeName,
                Description = "Describe what the player sees...",
                DescriptionAction = "What do you do?",
            };

            proj.Events[eventId] = eventDef;
            nodeDef.EventId = eventId;



            var evt = DataHelper.MakeEvent(eventId, eventDef.EventName,
                eventDef.Description, eventDef.DescriptionAction, new EventReplyData[0]);
            DataHelper.RegisterEvent(evt);

            ModProjectLoader.SaveEntity(proj, "events", eventId, eventDef);

            MarkDirty();

            Plugin.Log.LogInfo($"[ZoneEditing] Created event '{eventId}' for node '{nodeId}'");
            return eventId;
        }

        public static void RemoveCombatFromNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;
            if (string.IsNullOrEmpty(nodeDef.CombatId)) return;

            var proj = UnknownMod.Editor.Tabs.ModManagerPanel.ActiveProject;
            string expectedId = "c" + nodeId;
            if (nodeDef.CombatId == expectedId)
            {
                if (proj != null) proj.Combats.Remove(nodeDef.CombatId);
            }



            // Remove only the first combat ID — don't use the CombatId setter
            // which calls CombatIds.Clear() and would wipe multi-combat nodes.
            if (nodeDef.CombatIds.Count > 0)
                nodeDef.CombatIds.RemoveAt(0);
            nodeDef.CombatPercent = -1;
            MarkDirty();
        }

        public static void RemoveEventFromNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;
            if (string.IsNullOrEmpty(nodeDef.EventId)) return;

            var proj = UnknownMod.Editor.Tabs.ModManagerPanel.ActiveProject;
            string expectedId = $"e_{nodeId}_a";
            if (nodeDef.EventId == expectedId)
            {
                if (proj != null) proj.Events.Remove(nodeDef.EventId);
            }



            // Remove only the first event ID — don't use the EventId setter
            // which calls EventIds.Clear() and would wipe multi-event nodes.
            if (nodeDef.EventIds.Count > 0)
                nodeDef.EventIds.RemoveAt(0);
            nodeDef.EventPercent = -1;
            MarkDirty();
        }

        // 
        //  BFS REFLOW
        // 

    }
}
