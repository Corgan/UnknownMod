using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Definitions;
using UnknownMod.Editor;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    public static partial class ZoneEditingService
    {
        public static void ReflowNodeIds()
        {
            if (CurrentZone == null || string.IsNullOrEmpty(CurrentZone.IdPrefix)) return;
            string prefix = CurrentZone.IdPrefix;

            string entranceId = $"{prefix}_0";
            if (!CurrentZone.Nodes.ContainsKey(entranceId))
            {
                Plugin.Log.LogError("[ZoneEditing] Reflow failed: no entrance node (_0) found.");
                return;
            }

            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            var order = new List<string>();

            queue.Enqueue(entranceId);
            visited.Add(entranceId);

            string townId = $"{prefix}_1";
            bool hasTown = CurrentZone.Nodes.ContainsKey(townId);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                order.Add(current);

                if (!CurrentZone.Nodes.TryGetValue(current, out var nd)) continue;

                var sorted = nd.Connections
                    .Where(c => !visited.Contains(c) && CurrentZone.Nodes.ContainsKey(c))
                    .OrderBy(c => CurrentZone.Nodes[c].PosX)
                    .ThenByDescending(c => CurrentZone.Nodes[c].PosY)
                    .ToList();

                if (current == entranceId && hasTown && sorted.Contains(townId))
                {
                    sorted.Remove(townId);
                    sorted.Insert(0, townId);
                }

                foreach (var neighbor in sorted)
                {
                    if (visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            foreach (var nodeId in CurrentZone.Nodes.Keys.ToList())
            {
                if (!visited.Contains(nodeId))
                    order.Add(nodeId);
            }

            var renameMap = new Dictionary<string, string>();
            for (int i = 0; i < order.Count; i++)
            {
                string oldId = order[i];
                string newId = $"{prefix}_{i}";
                if (oldId != newId) renameMap[oldId] = newId;
            }

            if (renameMap.Count == 0)
            {
                Plugin.Log.LogInfo("[ZoneEditing] Reflow: node IDs already in BFS order.");
                return;
            }

            ApplyRenameMap(renameMap);
            Plugin.Log.LogInfo($"[ZoneEditing] Reflow complete: renamed {renameMap.Count} node(s).");
        }

        private static void ApplyRenameMap(Dictionary<string, string> renameMap)
        {
            string Remap(string id) => renameMap.TryGetValue(id, out var newId) ? newId : id;

            var oldNodes = new Dictionary<string, NodeDef>(CurrentZone.Nodes);
            CurrentZone.Nodes.Clear();

            foreach (var kvp in oldNodes)
            {
                string oldId = kvp.Key;
                var nd = kvp.Value;
                string newId = Remap(oldId);
                nd.NodeId = newId;

                for (int i = 0; i < nd.Connections.Count; i++)
                    nd.Connections[i] = Remap(nd.Connections[i]);

                if (!string.IsNullOrEmpty(nd.CombatId))
                {
                    string expectedOldCombat = "c" + oldId;
                    if (nd.CombatId == expectedOldCombat)
                    {
                        string newCombatId = "c" + newId;
                        nd.CombatId = newCombatId;
                        if (CurrentZone.Combats.TryGetValue(expectedOldCombat, out var combatDef))
                        {
                            CurrentZone.Combats.Remove(expectedOldCombat);
                            combatDef.CombatId = newCombatId;
                            CurrentZone.Combats[newCombatId] = combatDef;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(nd.EventId))
                {
                    string oldEventPrefix = $"e_{oldId}_";
                    if (nd.EventId.StartsWith(oldEventPrefix))
                    {
                        string suffix = nd.EventId.Substring(oldEventPrefix.Length);
                        string newEventId = $"e_{newId}_{suffix}";
                        nd.EventId = newEventId;
                        if (CurrentZone.Events.TryGetValue($"e_{oldId}_{suffix}", out var eventDef))
                        {
                            CurrentZone.Events.Remove($"e_{oldId}_{suffix}");
                            eventDef.EventId = newEventId;
                            CurrentZone.Events[newEventId] = eventDef;
                        }
                    }
                }

                CurrentZone.Nodes[newId] = nd;
            }

            var oldRoads = new Dictionary<string, RoadDef>(CurrentZone.Roads);
            CurrentZone.Roads.Clear();
            foreach (var kvp in oldRoads)
            {
                var road = kvp.Value;
                road.FromNodeId = Remap(road.FromNodeId);
                road.ToNodeId = Remap(road.ToNodeId);
                CurrentZone.Roads[road.FromNodeId + "-" + road.ToNodeId] = road;
            }

            // Full rebuild through ModProjectBuilder to re-register all SOs in Globals
            try
            {
                ModProjectBuilder.BuildZone(CurrentZone);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ZoneEditing] Reflow rebuild failed: {ex.Message}");
            }
            MarkDirty();
        }

        public static void EnsureIdPrefix(string entityId, string currentId, out string correctedId)
        {
            if (CurrentZone == null || string.IsNullOrEmpty(CurrentZone.IdPrefix))
            {
                correctedId = currentId;
                return;
            }
            string prefix = CurrentZone.IdPrefix + "_";
            correctedId = currentId.StartsWith(prefix) ? currentId : prefix + currentId;
        }

        // 
        //  FACTORY HELPERS (for editor hot-reload)
        // 

        private static CardData MakeCard(CardDef d)
        {
            var card = DataHelper.MakeCard(
                d.Id, d.Name,
                d.Damage, d.DamageType,
                d.Damage2, d.DamageType2,
                d.Curse, d.CurseCharges,
                d.Curse2, d.Curse2Charges,
                d.Aura, d.AuraCharges,
                d.AuraSelf, d.AuraSelfCharges,
                d.CurseSelf, d.CurseSelfCharges,
                d.Heal, d.HealSelf,
                d.TargetSide, d.TargetType, d.TargetPos,
                d.EffectRepeat, d.MoveToCenter,
                d.SummonUnitId, d.SummonNum,
                d.SelfHealthLoss, d.HealCurses, d.DispelAuras,
                effectCaster: d.EffectCaster, effectTarget: d.EffectTarget,
                energyCost: d.EnergyCost);
            DataHelper.ApplyCardExtras(card, d);

            // Copy card art sprite from an existing card
            if (!string.IsNullOrEmpty(d.SpriteSource))
                DataHelper.CopyCardVisuals(card, d.SpriteSource);

            // Auto-generate description from card mechanics
            try { card.SetDescriptionNew(forceDescription: true); }
            catch
            {
                // If SetDescriptionNew fails (e.g. Texts not ready), fall back to manual description
                if (!string.IsNullOrEmpty(d.Description))
                    Traverse.Create(card).Field("descriptionNormalized").SetValue(d.Description);
            }

            return card;
        }

        private static EventReplyData CreateReply(ReplyDef r)
        {
            return DataHelper.MakeReply(r,
                getCombat: id =>
                {
                    // Try Globals via the _CombatDataSource field
                    var dict = Traverse.Create(Globals.Instance)
                        .Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
                    if (dict != null && dict.TryGetValue(DataHelper.NormalizeKey(id), out var c)) return c;
                    return null;
                },
                getEvent: id => DataHelper.GetExistingEvent(id),
                getNode: id => DataHelper.GetExistingNode(id),
                getLoot: id => DataHelper.GetLootData(id));
        }

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

            // Combat  lookup all combat IDs from Globals
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

            // Event  lookup all event IDs from Globals
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

                // Use stored priority/percent arrays if available, else generate defaults
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

        private static AICards[] ResolveAICards(List<AiCardDef> defs)
        {
            if (defs == null || defs.Count == 0) return new AICards[0];

            var result = new List<AICards>();
            foreach (var d in defs)
            {
                var card = DataHelper.GetCard(d.CardId);
                if (card == null)
                {
                    Plugin.Log.LogWarning($"[ZoneEditing] AICard references missing card '{d.CardId}'");
                    continue;
                }
                result.Add(DataHelper.MakeAICard(card, d));
            }
            return result.ToArray();
        }

    }
}
