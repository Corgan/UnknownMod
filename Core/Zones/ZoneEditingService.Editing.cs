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
        // 
        //  HOT RELOAD TO GAME
        // 

        /// <summary>
        /// Push live editor changes into the running game: re-build SOs in Globals
        /// and optionally rebuild the Map scene graph. Call after save completes.
        /// </summary>
        public static void HotReloadToGame()
        {
            if (CurrentZone == null)
            {
                Plugin.Log.LogWarning("[HotReload] No zone loaded  nothing to reload.");
                return;
            }

            string zoneId = CurrentZone.ZoneId;
            bool isPatch = CurrentPatch != null;
            Plugin.Log.LogInfo($"[HotReload] Reloading zone '{zoneId}' (patch={isPatch})...");

            //  1. Rebuild SOs in Globals 
            try
            {
                if (isPatch)
                {
                    // Re-apply the patch (creates/overwrites NodeData, CombatData, EventData in Globals)
                    ModProjectBuilder.ApplyZonePatchPublic(CurrentPatch);
                }
                else
                {
                    // Full zone rebuild
                    ModProjectBuilder.BuildZone(CurrentZone);
                }
                Plugin.Log.LogInfo("[HotReload] Globals dictionaries updated.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[HotReload] Failed to rebuild SOs: {ex.Message}");
                Plugin.Log.LogError(ex.StackTrace);
                return;
            }

            //  2. Rebuild map visuals if on Map scene 
            var mapMgr = MapManager.Instance;
            if (mapMgr != null && mapMgr.worldTransform != null)
            {
                // Destroy existing zone root if present
                for (int i = mapMgr.worldTransform.childCount - 1; i >= 0; i--)
                {
                    var child = mapMgr.worldTransform.GetChild(i);
                    if (child.gameObject.name == zoneId)
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                        Plugin.Log.LogInfo($"[HotReload] Destroyed old map for '{zoneId}'.");
                        break;
                    }
                }

                // Rebuild the visual map
                if (Runtime.MapBuilder.BuildAndInjectMap(zoneId, mapMgr.worldTransform))
                    Plugin.Log.LogInfo($"[HotReload] Map rebuilt for '{zoneId}'.");
            }

            //  3. Combat notice 
            if (MatchManager.Instance != null)
                Plugin.Log.LogWarning("[HotReload] In combat  changes will take effect after restarting the encounter.");

            Plugin.Log.LogInfo("[HotReload] Done.");
        }

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

            if (!string.IsNullOrEmpty(nodeDef.CombatId) && CurrentZone.Combats.ContainsKey(nodeDef.CombatId))
            {
                string expectedCombatId = "c" + nodeId;
                if (nodeDef.CombatId == expectedCombatId)
                    CurrentZone.Combats.Remove(nodeDef.CombatId);
            }

            if (!string.IsNullOrEmpty(nodeDef.EventId) && CurrentZone.Events.ContainsKey(nodeDef.EventId))
            {
                string expectedEventId = $"e_{nodeId}_a";
                if (nodeDef.EventId == expectedEventId)
                    CurrentZone.Events.Remove(nodeDef.EventId);
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

            string combatId = "c" + nodeId;
            var combatDef = new CombatDef { CombatId = combatId, Description = $"Combat at {nodeDef.NodeName}" };

            CurrentZone.Combats[combatId] = combatDef;
            nodeDef.CombatId = combatId;

            var combat = DataHelper.MakeCombat(combatDef, new NPCData[0]);
            DataHelper.RegisterCombat(combat);

            RebuildNode(nodeId);
            MarkDirty();

            Plugin.Log.LogInfo($"[ZoneEditing] Created combat '{combatId}' for node '{nodeId}'");
            return combatId;
        }

        public static string CreateEventForNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return null;
            if (!string.IsNullOrEmpty(nodeDef.EventId)) return nodeDef.EventId;

            string eventId = $"e_{nodeId}_a";
            var eventDef = new EventDef
            {
                EventId = eventId,
                EventName = nodeDef.NodeName,
                Description = "Describe what the player sees...",
                DescriptionAction = "What do you do?",
            };

            CurrentZone.Events[eventId] = eventDef;
            nodeDef.EventId = eventId;

            var evt = DataHelper.MakeEvent(eventId, eventDef.EventName,
                eventDef.Description, eventDef.DescriptionAction, new EventReplyData[0]);
            DataHelper.RegisterEvent(evt);

            RebuildNode(nodeId);
            MarkDirty();

            Plugin.Log.LogInfo($"[ZoneEditing] Created event '{eventId}' for node '{nodeId}'");
            return eventId;
        }

        public static void RemoveCombatFromNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;
            if (string.IsNullOrEmpty(nodeDef.CombatId)) return;

            string expectedId = "c" + nodeId;
            if (nodeDef.CombatId == expectedId)
                CurrentZone.Combats.Remove(nodeDef.CombatId);

            nodeDef.CombatId = "";
            nodeDef.CombatPercent = -1;
            RebuildNode(nodeId);
            MarkDirty();
        }

        public static void RemoveEventFromNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;
            if (string.IsNullOrEmpty(nodeDef.EventId)) return;

            string expectedId = $"e_{nodeId}_a";
            if (nodeDef.EventId == expectedId)
                CurrentZone.Events.Remove(nodeDef.EventId);

            nodeDef.EventId = "";
            nodeDef.EventPercent = -1;
            RebuildNode(nodeId);
            MarkDirty();
        }

        // 
        //  HOT-RELOAD: Rebuild individual entities from DTO
        //  Uses Globals.Instance directly  no local SO dicts.
        // 

        public static void RebuildCard(string cardId)
        {
            if (CurrentZone == null || !CurrentZone.Cards.TryGetValue(cardId, out var cardDef)) return;
            try
            {
                CardData card;
                if (cardDef.IsUpgraded && !string.IsNullOrEmpty(cardDef.BaseCardId))
                {
                    var baseCard = DataHelper.GetCard(cardDef.BaseCardId);
                    if (baseCard != null)
                        card = DataHelper.MakeUpgradedCard(baseCard, cardDef.Id, cardDef.Name,
                            cardDef.UpgDamageMult, cardDef.UpgBonusCurseCharges,
                            cardDef.UpgBonusAuraCharges, cardDef.UpgBonusHeal);
                    else
                        card = MakeCard(cardDef);
                }
                else
                {
                    card = MakeCard(cardDef);
                }

                if (!string.IsNullOrEmpty(cardDef.SummonUnitId))
                {
                    var summonNpc = DataHelper.GetExistingNPC(cardDef.SummonUnitId);
                    if (summonNpc != null)
                        Traverse.Create(card).Field("summonUnit").SetValue(summonNpc);
                }

                DataHelper.RegisterCard(card);
                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneEditing] RebuildCard '{cardId}' failed: {ex.Message}"); }
        }

        public static void RebuildNpc(string npcId)
        {
            if (CurrentZone == null || !CurrentZone.Npcs.TryGetValue(npcId, out var npcDef)) return;
            try
            {
                var npc = DataHelper.MakeNPC(npcDef, ModRegistry.ResolveBaseNpcId(CurrentZone, npcDef));
                var aiCards = ResolveAICards(npcDef.AiCards);
                npc.AICards = aiCards;

                if (!string.IsNullOrEmpty(npcDef.UpgradedMobId))
                {
                    var u = DataHelper.GetExistingNPC(npcDef.UpgradedMobId);
                    if (u != null) npc.UpgradedMob = u;
                }
                if (!string.IsNullOrEmpty(npcDef.NgPlusMobId))
                {
                    var n = DataHelper.GetExistingNPC(npcDef.NgPlusMobId);
                    if (n != null) npc.NgPlusMob = n;
                }
                if (!string.IsNullOrEmpty(npcDef.HellModeMobId))
                {
                    var h = DataHelper.GetExistingNPC(npcDef.HellModeMobId);
                    if (h != null) npc.HellModeMob = h;
                }

                // Build custom prefab if sprite definition exists
                var sprDef = ModRegistry.ResolveSpriteDefForNpc(CurrentZone, npcDef);
                if (sprDef != null)
                {
                    NpcPrefabBuilder.InvalidateCache(npcId);
                    var customPrefab = NpcPrefabBuilder.BuildCustomPrefab(npcId, npc, sprDef, CurrentZone.ZoneId);
                    if (customPrefab != null)
                        npc.GameObjectAnimated = customPrefab;
                }

                DataHelper.RegisterNPC(npc);

                // Update variant chain parents
                foreach (var kvp in CurrentZone.Npcs)
                {
                    if (kvp.Key == npcId) continue;
                    var parentNpc = DataHelper.GetExistingNPC(kvp.Key);
                    if (parentNpc == null) continue;
                    if (kvp.Value.UpgradedMobId == npcId) parentNpc.UpgradedMob = npc;
                    if (kvp.Value.NgPlusMobId == npcId) parentNpc.NgPlusMob = npc;
                    if (kvp.Value.HellModeMobId == npcId) parentNpc.HellModeMob = npc;
                }

                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneEditing] RebuildNpc '{npcId}' failed: {ex.Message}"); }
        }

        public static void RebuildCombat(string combatId)
        {
            if (CurrentZone == null || !CurrentZone.Combats.TryGetValue(combatId, out var combatDef)) return;
            try
            {
                var npcList = combatDef.NpcIds
                    .Select(id => DataHelper.GetExistingNPC(id))
                    .Where(n => n != null)
                    .ToArray();

                var combat = DataHelper.MakeCombat(combatDef, npcList);

                if (!string.IsNullOrEmpty(combatDef.EventDataId))
                {
                    var postEvt = DataHelper.GetExistingEvent(combatDef.EventDataId);
                    if (postEvt != null) combat.EventData = postEvt;
                }
                if (!string.IsNullOrEmpty(combatDef.NpcToSummonOnKilledId))
                {
                    var summonNpc = DataHelper.GetExistingNPC(combatDef.NpcToSummonOnKilledId);
                    if (summonNpc != null) combat.NpcToSummonOnNpcKilled = summonNpc;
                }

                DataHelper.RegisterCombat(combat);

                // Update nodecombat references
                foreach (var nodeDef in CurrentZone.Nodes.Values)
                {
                    if (nodeDef.CombatId != combatId) continue;
                    var node = DataHelper.GetExistingNode(nodeDef.NodeId);
                    if (node != null) node.NodeCombat = new[] { combat };
                }

                // Update event replycombat references
                foreach (var evtDef in CurrentZone.Events.Values)
                {
                    var evt = DataHelper.GetExistingEvent(evtDef.EventId);
                    if (evt?.Replys == null) continue;
                    foreach (var reply in evt.Replys)
                    {
                        if (reply.SsCombat != null && reply.SsCombat.CombatId == combatId) reply.SsCombat = combat;
                        if (reply.FlCombat != null && reply.FlCombat.CombatId == combatId) reply.FlCombat = combat;
                        if (reply.SscCombat != null && reply.SscCombat.CombatId == combatId) reply.SscCombat = combat;
                        if (reply.FlcCombat != null && reply.FlcCombat.CombatId == combatId) reply.FlcCombat = combat;
                    }
                }

                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneEditing] RebuildCombat '{combatId}' failed: {ex.Message}"); }
        }

        public static void RebuildEvent(string eventId)
        {
            if (CurrentZone == null || !CurrentZone.Events.TryGetValue(eventId, out var eventDef)) return;
            try
            {
                var replies = eventDef.Replies.Select(r => CreateReply(r)).ToArray();
                var evt = DataHelper.MakeEvent(
                    eventDef.EventId, eventDef.EventName,
                    eventDef.Description, eventDef.DescriptionAction, replies,
                    eventDef.EventTier, eventDef.ReplyRandom);

                if (!string.IsNullOrEmpty(eventDef.RequirementId))
                {
                    var req = DataHelper.GetEventRequirement(eventDef.RequirementId);
                    if (req != null)
                        Traverse.Create(evt).Field("requirement").SetValue(req);
                }

                if (!string.IsNullOrEmpty(eventDef.SpriteSource))
                    DataHelper.CopyEventVisuals(evt, eventDef.SpriteSource);

                DataHelper.RegisterEvent(evt);

                // Update nodeevent references
                foreach (var nodeDef in CurrentZone.Nodes.Values)
                {
                    if (nodeDef.EventId != eventId) continue;
                    var node = DataHelper.GetExistingNode(nodeDef.NodeId);
                    if (node != null) node.NodeEvent = new[] { evt };
                }

                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneEditing] RebuildEvent '{eventId}' failed: {ex.Message}"); }
        }

        public static void RebuildNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;
            try
            {
                var node = BuildNodeSO(nodeDef);
                var zoneData = DataHelper.GetExistingZone(CurrentZone.ZoneId);
                if (zoneData != null) node.NodeZone = zoneData;

                var connected = nodeDef.Connections
                    .Select(id => DataHelper.GetExistingNode(id))
                    .Where(n => n != null)
                    .ToArray();
                node.NodesConnected = connected;

                DataHelper.RegisterNode(node);
                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneEditing] RebuildNode '{nodeId}' failed: {ex.Message}"); }
        }

        public static void RebuildItem(string itemId)
        {
            if (CurrentZone == null || !CurrentZone.Items.TryGetValue(itemId, out var itemDef)) return;
            try
            {
                var item = DataHelper.MakeFullItem(itemDef);
                DataHelper.RegisterItem(item);

                var itemCard = DataHelper.MakeItemCard(itemDef, item);
                DataHelper.RegisterCard(itemCard);

                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneEditing] RebuildItem '{itemId}' failed: {ex.Message}"); }
        }

        public static void RebuildLoot(string lootId)
        {
            if (CurrentZone == null || !CurrentZone.Loot.TryGetValue(lootId, out var lootDef)) return;
            try
            {
                var loot = DataHelper.MakeLoot(lootDef);
                DataHelper.RegisterLoot(loot);
                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneEditing] RebuildLoot '{lootId}' failed: {ex.Message}"); }
        }

        // 
        //  BFS REFLOW
        // 

    }
}
