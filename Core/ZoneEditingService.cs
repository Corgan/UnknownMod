using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    /// <summary>
    /// Editor-side zone mutation service. Owns CurrentZone, dirty/auto-save state,
    /// node add/delete, combat/event creation, BFS reflow, hot-reload Rebuild methods,
    /// and folder-based save.
    /// </summary>
    public static class ZoneEditingService
    {
        // ── Current zone DTO (source of truth for editor) ────────────
        public static ZoneDef CurrentZone { get; set; }

        /// <summary>
        /// When editing a zone patch, this holds the underlying ZonePatchDef.
        /// Mutations to CurrentZone (the synthesized zone) are synced back here.
        /// Null when editing a new (custom) zone.
        /// </summary>
        public static ZonePatchDef CurrentPatch { get; set; }

        // ── Dirty / auto-save state ──────────────────────────────────
        private static bool _dirty;
        private static float _lastDirtyTime;
        private const float AutoSaveDelay = 2.0f;

        public static void MarkDirty()
        {
            _dirty = true;
            _lastDirtyTime = Time.unscaledTime;
        }

        public static void TickAutoSave()
        {
            if (!_dirty) return;
            if (Time.unscaledTime - _lastDirtyTime < AutoSaveDelay) return;
            _dirty = false;
            SaveCurrentZone();
            HotReloadToGame();
        }

        public static bool IsDirty => _dirty;

        // ── JSON settings ────────────────────────────────────────────
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        // ═══════════════════════════════════════════════════════════════
        //  SAVE
        // ═══════════════════════════════════════════════════════════════

        public static void SaveCurrentZone()
        {
            if (CurrentZone == null) return;

            // If editing a zone patch, sync changes back to the patch def
            if (CurrentPatch != null)
            {
                SyncSynthesizedToPatch();
                return;
            }

            string folder = ModRegistry.GetZoneFolder(CurrentZone.ZoneId);
            SaveToFolder(CurrentZone, folder);
            Plugin.Log.LogInfo($"[ZoneEditing] Saved zone to: {folder}");
        }

        /// <summary>
        /// Sync mutations from the synthesized CurrentZone back into CurrentPatch.
        /// Only patch-owned nodes (those in the patch's Nodes dict or newly added ones
        /// with the patch prefix) are persisted.
        /// </summary>
        private static void SyncSynthesizedToPatch()
        {
            if (CurrentPatch == null || CurrentZone == null) return;

            string zoneId = CurrentPatch.TargetZoneId;
            var basePositions = _positionCache.ContainsKey(zoneId) ? _positionCache[zoneId] : null;

            // Nodes: sync any node that is:
            //   (a) already in the patch (existing patch additions/modifications), or
            //   (b) NOT in the base position cache (newly added during this session)
            foreach (var kvp in CurrentZone.Nodes)
            {
                if (CurrentPatch.Nodes.ContainsKey(kvp.Key))
                {
                    // Update existing patch node
                    CurrentPatch.Nodes[kvp.Key] = kvp.Value;
                }
                else if (basePositions != null && !basePositions.ContainsKey(kvp.Key))
                {
                    // New node not in the base zone — add to patch
                    CurrentPatch.Nodes[kvp.Key] = kvp.Value;
                }
            }

            // Roads: sync roads that involve at least one patch node
            foreach (var kvp in CurrentZone.Roads)
            {
                if (CurrentPatch.Roads.ContainsKey(kvp.Key))
                    CurrentPatch.Roads[kvp.Key] = kvp.Value;
                else if (CurrentPatch.Nodes.ContainsKey(kvp.Value.FromNodeId) ||
                         CurrentPatch.Nodes.ContainsKey(kvp.Value.ToNodeId))
                    CurrentPatch.Roads[kvp.Key] = kvp.Value;
            }

            // Events/encounters: sync any that exist in the patch
            foreach (var kvp in CurrentZone.Events)
            {
                if (CurrentPatch.Events.ContainsKey(kvp.Key))
                    CurrentPatch.Events[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in CurrentZone.Combats)
            {
                if (CurrentPatch.Encounters.ContainsKey(kvp.Key))
                    CurrentPatch.Encounters[kvp.Key] = kvp.Value;
            }

            // Update NextNodeNumber
            string prefix = CurrentPatch.DetectedPrefix;
            int maxNum = CurrentPatch.NextNodeNumber;
            foreach (var nodeId in CurrentPatch.Nodes.Keys)
            {
                if (nodeId.StartsWith(prefix) && int.TryParse(nodeId.Substring(prefix.Length), out int n))
                    maxNum = Math.Max(maxNum, n + 1);
            }
            CurrentPatch.NextNodeNumber = maxNum;

            Plugin.Log.LogInfo($"[ZoneEditing] Synced patch '{CurrentPatch.TargetZoneId}': {CurrentPatch.Nodes.Count} nodes, {CurrentPatch.Roads.Count} roads");
        }

        private static void SaveToFolder(ZoneDef def, string folder)
        {
            foreach (string sub in new[] { "", "nodes", "combats", "events", "npcs", "cards", "items", "loot", "sprites" })
                Directory.CreateDirectory(Path.Combine(folder, sub));

            var meta = new
            {
                def.ZoneId, def.ZoneName, def.IdPrefix,
                def.ObeliskLow, def.ObeliskHigh, def.ObeliskFinal,
                def.DisableExperience, def.DisableMadness,
                def.BackgroundImage
            };
            WriteJson(Path.Combine(folder, "zone.json"), meta);

            SaveEntities(Path.Combine(folder, "nodes"), def.Nodes, kvp => kvp.Value.NodeId);
            SaveEntities(Path.Combine(folder, "combats"), def.Combats, kvp => kvp.Value.CombatId);
            SaveEntities(Path.Combine(folder, "events"), def.Events, kvp => kvp.Value.EventId);
            SaveEntities(Path.Combine(folder, "npcs"), def.Npcs, kvp => kvp.Value.Id);
            SaveEntities(Path.Combine(folder, "cards"), def.Cards, kvp => kvp.Value.Id);
            SaveEntities(Path.Combine(folder, "items"), def.Items, kvp => kvp.Value.Id);
            SaveEntities(Path.Combine(folder, "loot"), def.Loot, kvp => kvp.Value.Id);
            SaveEntities(Path.Combine(folder, "sprites"), def.Sprites, kvp => kvp.Value.NpcId);

            WriteJson(Path.Combine(folder, "roads.json"), def.Roads);
        }

        private static void SaveEntities<T>(string folder, Dictionary<string, T> dict,
            Func<KeyValuePair<string, T>, string> getFilename)
        {
            if (Directory.Exists(folder))
            {
                foreach (var existing in Directory.GetFiles(folder, "*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(existing);
                    if (!dict.ContainsKey(name))
                        File.Delete(existing);
                }
            }

            foreach (var kvp in dict)
            {
                string filename = getFilename(kvp) + ".json";
                WriteJson(Path.Combine(folder, filename), kvp.Value);
            }
        }

        private static void WriteJson(string path, object obj)
        {
            string json = JsonConvert.SerializeObject(obj, _jsonSettings);
            File.WriteAllText(path, json);
        }

        // ═══════════════════════════════════════════════════════════════
        //  HOT RELOAD TO GAME
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Push live editor changes into the running game: re-build SOs in Globals
        /// and optionally rebuild the Map scene graph. Call after save completes.
        /// </summary>
        public static void HotReloadToGame()
        {
            if (CurrentZone == null)
            {
                Plugin.Log.LogWarning("[HotReload] No zone loaded — nothing to reload.");
                return;
            }

            string zoneId = CurrentZone.ZoneId;
            bool isPatch = CurrentPatch != null;
            Plugin.Log.LogInfo($"[HotReload] Reloading zone '{zoneId}' (patch={isPatch})...");

            // ── 1. Rebuild SOs in Globals ────────────────────────────
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

            // ── 2. Rebuild map visuals if on Map scene ───────────────
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

            // ── 3. Combat notice ─────────────────────────────────────
            if (MatchManager.Instance != null)
                Plugin.Log.LogWarning("[HotReload] In combat — changes will take effect after restarting the encounter.");

            Plugin.Log.LogInfo("[HotReload] Done.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  NODE ADD / DELETE
        // ═══════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════
        //  AUTO-ID: Generate combat/event IDs from node IDs
        // ═══════════════════════════════════════════════════════════════

        public static string CreateCombatForNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return null;
            if (!string.IsNullOrEmpty(nodeDef.CombatId)) return nodeDef.CombatId;

            string combatId = "c" + nodeId;
            var combatDef = new CombatDef { CombatId = combatId, Description = $"Combat at {nodeDef.NodeName}" };

            CurrentZone.Combats[combatId] = combatDef;
            nodeDef.CombatId = combatId;

            var combat = DataHelper.MakeCombat(combatId, new NPCData[0],
                combatDef.CombatTier, combatDef.Background, combatDef.Description, -1);
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

        // ═══════════════════════════════════════════════════════════════
        //  HOT-RELOAD: Rebuild individual entities from DTO
        //  Uses Globals.Instance directly — no local SO dicts.
        // ═══════════════════════════════════════════════════════════════

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

                // Update node→combat references
                foreach (var nodeDef in CurrentZone.Nodes.Values)
                {
                    if (nodeDef.CombatId != combatId) continue;
                    var node = DataHelper.GetExistingNode(nodeDef.NodeId);
                    if (node != null) node.NodeCombat = new[] { combat };
                }

                // Update event reply→combat references
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

                // Update node→event references
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
                var item = DataHelper.MakeItem(itemDef);
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

        // ═══════════════════════════════════════════════════════════════
        //  BFS REFLOW
        // ═══════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════
        //  FACTORY HELPERS (for editor hot-reload)
        // ═══════════════════════════════════════════════════════════════

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
                    if (dict != null && dict.TryGetValue(id.Replace(" ", "").ToLower(), out var c)) return c;
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
            node.ExistsSku = "";
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

            // Combat — lookup from Globals
            if (!string.IsNullOrEmpty(d.CombatId))
            {
                var combatDict = Traverse.Create(Globals.Instance)
                    .Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
                CombatData combat = null;
                combatDict?.TryGetValue(d.CombatId.Replace(" ", "").ToLower(), out combat);
                if (combat != null)
                {
                    node.NodeCombat = new[] { combat };
                    node.NodeCombatTier = d.CombatTier;
                }
                else
                    node.NodeCombat = new CombatData[0];
            }
            else
                node.NodeCombat = new CombatData[0];

            // Event — lookup from Globals
            if (!string.IsNullOrEmpty(d.EventId))
            {
                var evt = DataHelper.GetExistingEvent(d.EventId);
                if (evt != null)
                {
                    node.NodeEvent = new[] { evt };
                    node.NodeEventPriority = new[] { 0 };
                    node.NodeEventPercent = new[] { 100 };
                    node.NodeEventTier = d.NodeEventTier;
                }
                else
                {
                    node.NodeEvent = new EventData[0];
                    node.NodeEventPriority = new int[0];
                    node.NodeEventPercent = new int[0];
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

        // ═══════════════════════════════════════════════════════════════
        //  ZONE PATCH SYNTHESIS — build a full ZoneDef from base + patch
        // ═══════════════════════════════════════════════════════════════

        // Cache: zoneId → { nodeId → (posX, posY) }
        private static readonly Dictionary<string, Dictionary<string, Vector2>> _positionCache = new();
        // Cache: zoneId → { roadKey → waypoints[] }
        private static readonly Dictionary<string, Dictionary<string, List<float[]>>> _roadCache = new();
        // Cache: synthesized ZoneDefs so we don't rebuild every frame
        private static readonly Dictionary<string, ZoneDef> _synthesizedCache = new();
        // Cache: base-game zone background sprites
        private static readonly Dictionary<string, Sprite> _baseZoneBgCache = new();
        // Negative cache: zones we've already tried and failed to cache
        private static readonly HashSet<string> _cacheFailures = new();
        // Additive scene load state
        private static bool _sceneLoadRequested;
        private static bool _sceneLoadFailed;
        /// <summary>Flag checked by Harmony patches to suppress MapManager/SceneStatic during additive load.</summary>
        internal static bool SuppressSceneLoad;

        /// <summary>
        /// Synthesize a full ZoneDef from a base-game zone + a ZonePatchDef overlay.
        /// Returns the synthesized zone, or null if base data isn't available yet
        /// (e.g. MapManager hasn't loaded).
        /// </summary>
        public static ZoneDef SynthesizeZoneDef(ZonePatchDef patch)
        {
            if (patch == null) return null;
            string zoneId = patch.TargetZoneId;

            // Return cached synthesis if patch hasn't changed
            if (_synthesizedCache.TryGetValue(zoneId, out var cached))
                return cached;

            // Ensure position/road cache is populated for this zone
            if (!_positionCache.ContainsKey(zoneId))
            {
                // Don't re-attempt if we already failed (avoids per-frame perf hit)
                if (_cacheFailures.Contains(zoneId))
                    return null;

                if (!CacheBaseZoneData(zoneId))
                {
                    _cacheFailures.Add(zoneId);
                    Plugin.Log.LogWarning($"[ZoneEditing] Cannot synthesize '{zoneId}': base zone data not available. Visit the map screen first.");
                    return null;
                }
            }

            var positions = _positionCache[zoneId];
            var baseRoads = _roadCache.ContainsKey(zoneId) ? _roadCache[zoneId] : new Dictionary<string, List<float[]>>();

            // Get base zone metadata from Globals
            var zoneData = DataHelper.GetExistingZone(zoneId);

            var synth = new ZoneDef
            {
                ZoneId = zoneId,
                ZoneName = zoneData?.ZoneName ?? zoneId,
                IdPrefix = patch.DetectedPrefix.TrimEnd('_'),
                BackgroundImage = "", // base zones use prefab backgrounds, not file images
            };

            // ── Populate base-game nodes from Globals + position cache ──
            var nodeDict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, NodeData>>("_NodeDataSource").Value;

            if (nodeDict != null)
            {
                foreach (var kvp in nodeDict)
                {
                    var nd = kvp.Value;
                    if (nd?.NodeZone == null) continue;
                    if (!nd.NodeZone.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase)) continue;

                    string nodeId = nd.NodeId;

                    // Skip nodes that the patch will override
                    if (patch.Nodes.ContainsKey(nodeId)) continue;

                    var nodeDef = new NodeDef
                    {
                        NodeId = nodeId,
                        NodeName = nd.NodeName ?? "",
                        Description = nd.Description ?? "",
                        TravelDestination = nd.TravelDestination,
                        GoToTown = nd.GoToTown,
                        ExistsPercent = nd.ExistsPercent,
                        DisableCorruption = nd.DisableCorruption,
                        DisableRandom = nd.DisableRandom,
                        NodeGround = nd.NodeGround,
                        VisibleIfNotRequirement = nd.VisibleIfNotRequirement,
                    };

                    // Position from cache
                    if (positions.TryGetValue(nodeId, out var pos))
                    {
                        nodeDef.PosX = pos.x;
                        nodeDef.PosY = pos.y;
                    }

                    // Connections
                    if (nd.NodesConnected != null)
                    {
                        foreach (var conn in nd.NodesConnected)
                        {
                            if (conn != null && !string.IsNullOrEmpty(conn.NodeId))
                                nodeDef.Connections.Add(conn.NodeId);
                        }
                    }

                    // Combat
                    if (nd.NodeCombat != null && nd.NodeCombat.Length > 0 && nd.NodeCombat[0] != null)
                    {
                        nodeDef.CombatId = nd.NodeCombat[0].CombatId;
                        nodeDef.CombatPercent = nd.CombatPercent;
                    }

                    // Event
                    if (nd.NodeEvent != null && nd.NodeEvent.Length > 0 && nd.NodeEvent[0] != null)
                    {
                        nodeDef.EventId = nd.NodeEvent[0].EventId;
                        nodeDef.EventPercent = nd.EventPercent;
                    }

                    synth.Nodes[nodeId] = nodeDef;
                }
            }

            // ── Merge in patch nodes ────────────────────────────────────
            foreach (var kvp in patch.Nodes)
                synth.Nodes[kvp.Key] = kvp.Value;

            // ── Populate base-game roads from cache ─────────────────────
            foreach (var kvp in baseRoads)
            {
                string key = kvp.Key;
                int dash = key.IndexOf('-');
                if (dash < 0) continue;
                string fromId = key.Substring(0, dash);
                string toId = key.Substring(dash + 1);
                synth.Roads[key] = new RoadDef
                {
                    FromNodeId = fromId,
                    ToNodeId = toId,
                    Waypoints = new List<float[]>(kvp.Value),
                };
            }

            // ── Merge in patch roads ────────────────────────────────────
            foreach (var kvp in patch.Roads)
                synth.Roads[kvp.Key] = kvp.Value;

            // ═══════════════════════════════════════════════════════════
            //  SYNTHESIZE BASE-GAME EVENTS, COMBATS, NPCS, LOOT
            // ═══════════════════════════════════════════════════════════

            // ── Events: snapshot from Globals ────────────────────────────
            var eventDict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, EventData>>("_Events").Value;
            if (eventDict != null)
            {
                // Collect event IDs referenced by zone nodes
                var referencedEventIds = new HashSet<string>();
                foreach (var nd in synth.Nodes.Values)
                {
                    if (!string.IsNullOrEmpty(nd.EventId))
                        referencedEventIds.Add(nd.EventId.ToLower());
                }

                foreach (var evtId in referencedEventIds)
                {
                    if (patch.Events.ContainsKey(evtId)) continue; // patch overrides
                    if (!eventDict.TryGetValue(evtId, out var evt) || evt == null) continue;

                    synth.Events[evt.EventId] = SnapshotEventDef(evt);
                }
            }

            // ── Combats: snapshot from Globals ──────────────────────────
            var combatDict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
            if (combatDict != null)
            {
                var referencedCombatIds = new HashSet<string>();
                foreach (var nd in synth.Nodes.Values)
                {
                    if (!string.IsNullOrEmpty(nd.CombatId))
                        referencedCombatIds.Add(nd.CombatId.Replace(" ", "").ToLower());
                }
                // Also gather combats referenced by event reply outcomes
                foreach (var evtDef in synth.Events.Values)
                {
                    foreach (var r in evtDef.Replies)
                    {
                        AddCombatRef(referencedCombatIds, r.Ss?.CombatId);
                        AddCombatRef(referencedCombatIds, r.Fl?.CombatId);
                        AddCombatRef(referencedCombatIds, r.Ssc?.CombatId);
                        AddCombatRef(referencedCombatIds, r.Flc?.CombatId);
                    }
                }

                foreach (var cId in referencedCombatIds)
                {
                    if (patch.Encounters.ContainsKey(cId)) continue;
                    if (!combatDict.TryGetValue(cId, out var combat) || combat == null) continue;

                    synth.Combats[combat.CombatId] = SnapshotCombatDef(combat);
                }
            }

            // ── Merge in patch additions (override base) ────────────────
            foreach (var kvp in patch.Encounters)
                synth.Combats[kvp.Key] = kvp.Value;
            foreach (var kvp in patch.Events)
                synth.Events[kvp.Key] = kvp.Value;

            _synthesizedCache[zoneId] = synth;
            Plugin.Log.LogInfo($"[ZoneEditing] Synthesized ZoneDef for '{zoneId}': {synth.Nodes.Count} nodes, {synth.Roads.Count} roads, {synth.Events.Count} events, {synth.Combats.Count} combats");
            return synth;
        }

        /// <summary>Invalidate the synthesized cache for a zone (call when patch changes).</summary>
        public static void InvalidateSynthesizedZone(string zoneId)
        {
            _synthesizedCache.Remove(zoneId);
        }

        /// <summary>
        /// Cache base-game node positions and road waypoints by scanning the MapManager's
        /// worldTransform hierarchy. Returns false if MapManager is not available.
        /// </summary>
        private static bool CacheBaseZoneData(string zoneId)
        {
            // Try MapManager.Instance.worldTransform
            var mapMgr = MapManager.Instance;
            if (mapMgr != null && mapMgr.worldTransform != null)
            {
                foreach (Transform zoneT in mapMgr.worldTransform)
                {
                    if (!zoneT.gameObject.name.Equals(zoneId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    CacheZoneFromTransform(zoneId, zoneT);
                    return true;
                }
            }

            // Try mapList prefabs (serialized zone prefabs, available even before map loads)
            if (mapMgr != null && mapMgr.mapList != null)
            {
                foreach (var prefab in mapMgr.mapList)
                {
                    if (prefab == null) continue;
                    if (!prefab.name.Equals(zoneId, StringComparison.OrdinalIgnoreCase)) continue;

                    CacheZoneFromTransform(zoneId, prefab.transform);
                    return true;
                }
            }

            // ── Synchronous additive scene load: load the Map scene to access
            // MapManager's serialized mapList, extract zone data, then unload.
            // Harmony patches suppress MapManager.Awake/Start and SceneStatic.LoadByName.
            if (!_sceneLoadRequested && !_sceneLoadFailed)
            {
                _sceneLoadRequested = true;
                SuppressSceneLoad = true;

                try
                {
                    Plugin.Log.LogInfo("[ZoneEditing] Loading Map scene (additive, sync) for base zone data...");
                    SceneManager.LoadScene("Map", LoadSceneMode.Additive);

                    var mapScene = SceneManager.GetSceneByName("Map");
                    int scanned = 0;

                    if (mapScene.IsValid() && mapScene.isLoaded)
                    {
                        var rootObjects = mapScene.GetRootGameObjects();

                        // Deactivate all roots to prevent Start/OnEnable on other components
                        foreach (var root in rootObjects)
                            root.SetActive(false);

                        Plugin.Log.LogInfo($"[ZoneEditing] Map scene loaded: {rootObjects.Length} root objects (all deactivated)");

                        // Find MapManager and scan its mapList + worldTransform
                        foreach (var root in rootObjects)
                        {
                            var mm = root.GetComponentInChildren<MapManager>(true);
                            if (mm == null) continue;

                            Plugin.Log.LogInfo($"[ZoneEditing]   Found MapManager, mapList={mm.mapList?.Count ?? 0}");

                            if (mm.mapList != null)
                            {
                                foreach (var prefab in mm.mapList)
                                {
                                    if (prefab == null) continue;
                                    string id = prefab.name;
                                    if (_positionCache.ContainsKey(id)) continue;

                                    var nodesChild = prefab.transform.Find("Nodes");
                                    if (nodesChild == null || nodesChild.childCount == 0) continue;

                                    CacheZoneFromTransform(id, prefab.transform);
                                    scanned++;
                                }
                            }

                            if (mm.worldTransform != null)
                            {
                                foreach (Transform child in mm.worldTransform)
                                {
                                    string id = child.gameObject.name;
                                    if (_positionCache.ContainsKey(id)) continue;

                                    var nodesChild = child.Find("Nodes");
                                    if (nodesChild == null || nodesChild.childCount == 0) continue;

                                    CacheZoneFromTransform(id, child);
                                    scanned++;
                                }
                            }

                            break;
                        }

                        // Fallback: scan root objects directly
                        if (scanned == 0)
                        {
                            Plugin.Log.LogInfo("[ZoneEditing]   MapManager scan found nothing, scanning root objects...");
                            foreach (var root in rootObjects)
                            {
                                var nodesChild = root.transform.Find("Nodes");
                                if (nodesChild == null || nodesChild.childCount == 0) continue;

                                string id = root.name;
                                if (_positionCache.ContainsKey(id)) continue;

                                CacheZoneFromTransform(id, root.transform);
                                scanned++;
                            }
                        }

                        // Unload the Map scene
                        SceneManager.UnloadSceneAsync(mapScene);
                    }
                    else
                    {
                        Plugin.Log.LogWarning("[ZoneEditing] Map scene did not load correctly.");
                    }

                    if (scanned > 0)
                        Plugin.Log.LogInfo($"[ZoneEditing] Cached {scanned} base zone(s) from Map scene.");
                    else
                    {
                        Plugin.Log.LogWarning("[ZoneEditing] Map scene load found 0 zone prefabs.");
                        _sceneLoadFailed = true;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ZoneEditing] Map scene load failed: {ex.Message}");
                    _sceneLoadFailed = true;
                }
                finally
                {
                    SuppressSceneLoad = false;
                }

                // The target zone should now be cached
                if (_positionCache.ContainsKey(zoneId))
                    return true;
            }

            return false;
        }

        /// <summary>Extract node positions and road waypoints from a zone Transform hierarchy.</summary>
        private static void CacheZoneFromTransform(string zoneId, Transform zoneT)
        {
            var nodePositions = new Dictionary<string, Vector2>();
            var roads = new Dictionary<string, List<float[]>>();

            // ── Background sprite ────────────────────────────────────
            var bgT = zoneT.Find("Background");
            if (bgT != null)
            {
                var sr = bgT.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                    _baseZoneBgCache[zoneId] = sr.sprite;
            }
            // Also check for a direct SpriteRenderer on children named with the zone
            if (!_baseZoneBgCache.ContainsKey(zoneId))
            {
                foreach (Transform child in zoneT)
                {
                    if (child.name == "Nodes" || child.name == "Roads") continue;
                    var sr = child.GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sprite != null)
                    {
                        _baseZoneBgCache[zoneId] = sr.sprite;
                        break;
                    }
                }
            }

            // ── Nodes ────────────────────────────────────────────────
            var nodesT = zoneT.Find("Nodes");
            if (nodesT != null)
            {
                foreach (Transform nodeT in nodesT)
                {
                    string nodeId = nodeT.gameObject.name.ToLower();
                    var lp = nodeT.localPosition;
                    nodePositions[nodeId] = new Vector2(lp.x, lp.y);
                }
            }

            // ── Roads ────────────────────────────────────────────────
            var roadsT = zoneT.Find("Roads");
            if (roadsT != null)
            {
                foreach (Transform roadT in roadsT)
                {
                    var lr = roadT.GetComponent<LineRenderer>();
                    if (lr == null || lr.positionCount < 2) continue;

                    string key = roadT.gameObject.name;
                    var waypoints = new List<float[]>();

                    // Skip first and last points (those are the node positions)
                    for (int i = 1; i < lr.positionCount - 1; i++)
                    {
                        Vector3 p = lr.GetPosition(i);
                        waypoints.Add(new float[] { p.x, p.y });
                    }

                    roads[key] = waypoints;
                }
            }

            _positionCache[zoneId] = nodePositions;
            _roadCache[zoneId] = roads;

            Plugin.Log.LogInfo($"[ZoneEditing] Cached base zone '{zoneId}': {nodePositions.Count} node positions, {roads.Count} roads");
        }

        /// <summary>
        /// Pre-cache ALL base-game zones from MapManager. Call this when MapManager becomes
        /// available to ensure synthesis works even after leaving the map scene.
        /// </summary>
        public static void CacheAllBaseZones()
        {
            var mapMgr = MapManager.Instance;
            if (mapMgr == null || mapMgr.worldTransform == null) return;

            int cached = 0;
            foreach (Transform zoneT in mapMgr.worldTransform)
            {
                string zoneId = zoneT.gameObject.name;
                if (_positionCache.ContainsKey(zoneId)) continue;

                CacheZoneFromTransform(zoneId, zoneT);
                cached++;
            }

            // Also scan mapList prefabs
            if (mapMgr.mapList != null)
            {
                foreach (var prefab in mapMgr.mapList)
                {
                    if (prefab == null) continue;
                    string zoneId = prefab.name;
                    if (_positionCache.ContainsKey(zoneId)) continue;

                    CacheZoneFromTransform(zoneId, prefab.transform);
                    cached++;
                }
            }

            if (cached > 0)
                Plugin.Log.LogInfo($"[ZoneEditing] Pre-cached {cached} base zone(s) from MapManager.");
        }

        /// <summary>Get the cached background sprite for a base-game zone (from MapManager prefab).</summary>
        public static Sprite GetBaseZoneBackground(string zoneId)
        {
            _baseZoneBgCache.TryGetValue(zoneId, out var sprite);
            return sprite;
        }

        /// <summary>Clear all synthesis caches. Call during full rebuild.</summary>
        public static void ClearSynthesisCache()
        {
            _positionCache.Clear();
            _roadCache.Clear();
            _synthesizedCache.Clear();
            _baseZoneBgCache.Clear();
            _cacheFailures.Clear();
            _sceneLoadRequested = false;
            _sceneLoadFailed = false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SNAPSHOT HELPERS — convert game SOs into lightweight defs
        // ═══════════════════════════════════════════════════════════════

        private static void AddCombatRef(HashSet<string> set, string id)
        {
            if (!string.IsNullOrEmpty(id))
                set.Add(id.Replace(" ", "").ToLower());
        }

        /// <summary>Snapshot a CombatData SO into a CombatDef.</summary>
        private static CombatDef SnapshotCombatDef(CombatData c)
        {
            var d = new CombatDef
            {
                CombatId = c.CombatId ?? "",
                Description = c.Description ?? "",
                CombatTier = c.CombatTier,
                Background = c.CombatBackground,
                NpcRemoveInMadness0Index = c.NpcRemoveInMadness0Index,
                HealHeroes = c.HealHeroes,
                IsRift = c.IsRift,
            };

            if (c.NPCList != null)
            {
                foreach (var npc in c.NPCList)
                {
                    if (npc != null)
                        d.NpcIds.Add(npc.Id ?? "");
                }
            }

            d.NpcToSummonOnKilledId = c.NpcToSummonOnNpcKilled != null ? c.NpcToSummonOnNpcKilled.Id ?? "" : "";
            d.EventDataId = c.EventData != null ? c.EventData.EventId ?? "" : "";

            if (c.CombatEffect != null)
            {
                foreach (var eff in c.CombatEffect)
                {
                    d.CombatEffects.Add(new CombatEffectDef
                    {
                        AuraCurse = eff.AuraCurse != null ? eff.AuraCurse.name ?? "" : "",
                        Charges = eff.AuraCurseCharges,
                        Target = eff.AuraCurseTarget,
                    });
                }
            }

            return d;
        }

        /// <summary>Snapshot an EventData SO into an EventDef.</summary>
        private static EventDef SnapshotEventDef(EventData e)
        {
            var d = new EventDef
            {
                EventId = e.EventId ?? "",
                EventName = e.EventName ?? "",
                Description = e.Description ?? "",
                DescriptionAction = e.DescriptionAction ?? "",
                EventTier = e.EventTier,
                ReplyRandom = e.ReplyRandom,
            };

            if (e.Requirement != null)
                d.RequirementId = e.Requirement.RequirementId ?? "";

            if (e.Replys != null)
            {
                foreach (var reply in e.Replys)
                {
                    if (reply == null) continue;
                    d.Replies.Add(SnapshotReplyDef(reply));
                }
            }
            return d;
        }

        private static ReplyDef SnapshotReplyDef(EventReplyData r)
        {
            var d = new ReplyDef
            {
                ReplyText = r.ReplyText ?? "",
                Action = r.ReplyActionText,
                GoldCost = r.GoldCost,
                DustCost = r.DustCost,
            };

            if (r.Requirement != null) d.RequirementId = r.Requirement.RequirementId ?? "";
            if (r.RequirementBlocked != null) d.RequirementBlockedId = r.RequirementBlocked.RequirementId ?? "";

            d.HasRoll = r.SsRoll;
            d.RollDC = r.SsRollNumber;
            d.RollCrit = r.SsRollNumberCritical;
            d.RollCritFail = r.SsRollNumberCriticalFail;
            d.RollMode = r.SsRollMode;
            d.RollTarget = r.SsRollTarget;
            d.RollCard = r.SsRollCard;

            d.Ss = SnapshotOutcomeSs(r);
            d.Fl = SnapshotOutcomeFl(r);
            d.Ssc = SnapshotOutcomeSsc(r);
            d.Flc = SnapshotOutcomeFlc(r);

            return d;
        }

        private static OutcomeDef SnapshotOutcomeSs(EventReplyData r)
        {
            var d = new OutcomeDef();
            d.Text = r.SsRewardText ?? "";
            d.HealPercent = r.SsRewardHealthPercent;
            d.HealFlat = r.SsRewardHealthFlat;
            d.Gold = r.SsGoldReward;
            d.Dust = r.SsDustReward;
            d.Supply = r.SsSupplyReward;
            d.XP = r.SsExperienceReward;
            d.CombatId = r.SsCombat != null ? r.SsCombat.CombatId ?? "" : "";
            d.EventId = r.SsEvent != null ? r.SsEvent.EventId ?? "" : "";
            d.NodeTravelId = r.SsNodeTravel != null ? r.SsNodeTravel.NodeId ?? "" : "";
            d.RequirementUnlockId = r.SsRequirementUnlock != null ? r.SsRequirementUnlock.RequirementId ?? "" : "";
            d.RequirementUnlock2Id = r.SsRequirementUnlock2 != null ? r.SsRequirementUnlock2.RequirementId ?? "" : "";
            d.RequirementLockId = r.SsRequirementLock != null ? r.SsRequirementLock.RequirementId ?? "" : "";
            d.RequirementLock2Id = r.SsRequirementLock2 != null ? r.SsRequirementLock2.RequirementId ?? "" : "";
            d.LootId = r.SsLootList != null ? r.SsLootList.Id ?? "" : "";
            d.ShopId = r.SsShopList != null ? r.SsShopList.Id ?? "" : "";
            d.AddItemId = r.SsAddItem != null ? r.SsAddItem.Id ?? "" : "";
            d.AddCard1Id = r.SsAddCard1 != null ? r.SsAddCard1.Id ?? "" : "";
            d.AddCard2Id = r.SsAddCard2 != null ? r.SsAddCard2.Id ?? "" : "";
            d.AddCard3Id = r.SsAddCard3 != null ? r.SsAddCard3.Id ?? "" : "";
            d.RewardTier = r.SsRewardTier != null ? r.SsRewardTier.name ?? "" : "";
            d.Discount = r.SsDiscount;
            d.MaxQuantity = r.SsMaxQuantity;
            d.HealerUI = r.SsHealerUI;
            d.UpgradeUI = r.SsUpgradeUI;
            d.CraftUI = r.SsCraftUI;
            d.MerchantUI = r.SsMerchantUI;
            d.CorruptionUI = r.SsCorruptionUI;
            d.UpgradeRandomCard = r.SsUpgradeRandomCard;
            d.FinishGame = r.SsFinishGame;
            d.FinishObeliskMap = r.SsFinishObeliskMap;
            return d;
        }

        private static OutcomeDef SnapshotOutcomeFl(EventReplyData r)
        {
            var d = new OutcomeDef();
            d.Text = r.FlRewardText ?? "";
            d.HealPercent = r.FlRewardHealthPercent;
            d.HealFlat = r.FlRewardHealthFlat;
            d.Gold = r.FlGoldReward;
            d.Dust = r.FlDustReward;
            d.Supply = r.FlSupplyReward;
            d.XP = r.FlExperienceReward;
            d.CombatId = r.FlCombat != null ? r.FlCombat.CombatId ?? "" : "";
            d.EventId = r.FlEvent != null ? r.FlEvent.EventId ?? "" : "";
            d.NodeTravelId = r.FlNodeTravel != null ? r.FlNodeTravel.NodeId ?? "" : "";
            d.RequirementUnlockId = r.FlRequirementUnlock != null ? r.FlRequirementUnlock.RequirementId ?? "" : "";
            d.RequirementUnlock2Id = r.FlRequirementUnlock2 != null ? r.FlRequirementUnlock2.RequirementId ?? "" : "";
            d.RequirementLockId = r.FlRequirementLock != null ? r.FlRequirementLock.RequirementId ?? "" : "";
            // Fl has no RequirementLock2
            d.LootId = r.FlLootList != null ? r.FlLootList.Id ?? "" : "";
            d.ShopId = r.FlShopList != null ? r.FlShopList.Id ?? "" : "";
            d.AddItemId = r.FlAddItem != null ? r.FlAddItem.Id ?? "" : "";
            d.AddCard1Id = r.FlAddCard1 != null ? r.FlAddCard1.Id ?? "" : "";
            d.AddCard2Id = r.FlAddCard2 != null ? r.FlAddCard2.Id ?? "" : "";
            d.AddCard3Id = r.FlAddCard3 != null ? r.FlAddCard3.Id ?? "" : "";
            d.RewardTier = r.FlRewardTier != null ? r.FlRewardTier.name ?? "" : "";
            d.Discount = r.FlDiscount;
            d.MaxQuantity = r.FlMaxQuantity;
            d.HealerUI = r.FlHealerUI;
            d.UpgradeUI = r.FlUpgradeUI;
            d.CraftUI = r.FlCraftUI;
            d.MerchantUI = r.FlMerchantUI;
            d.CorruptionUI = r.FlCorruptionUI;
            d.UpgradeRandomCard = r.FlUpgradeRandomCard;
            // Fl has no FinishGame / FinishObeliskMap
            return d;
        }

        private static OutcomeDef SnapshotOutcomeSsc(EventReplyData r)
        {
            var d = new OutcomeDef();
            d.Text = r.SscRewardText ?? "";
            d.HealPercent = r.SscRewardHealthPercent;
            d.HealFlat = r.SscRewardHealthFlat;
            d.Gold = r.SscGoldReward;
            d.Dust = r.SscDustReward;
            d.Supply = r.SscSupplyReward;
            d.XP = r.SscExperienceReward;
            d.CombatId = r.SscCombat != null ? r.SscCombat.CombatId ?? "" : "";
            d.EventId = r.SscEvent != null ? r.SscEvent.EventId ?? "" : "";
            d.NodeTravelId = r.SscNodeTravel != null ? r.SscNodeTravel.NodeId ?? "" : "";
            d.RequirementUnlockId = r.SscRequirementUnlock != null ? r.SscRequirementUnlock.RequirementId ?? "" : "";
            d.RequirementUnlock2Id = r.SscRequirementUnlock2 != null ? r.SscRequirementUnlock2.RequirementId ?? "" : "";
            d.RequirementLockId = r.SscRequirementLock != null ? r.SscRequirementLock.RequirementId ?? "" : "";
            // Ssc has no RequirementLock2
            d.LootId = r.SscLootList != null ? r.SscLootList.Id ?? "" : "";
            d.ShopId = r.SscShopList != null ? r.SscShopList.Id ?? "" : "";
            d.AddItemId = r.SscAddItem != null ? r.SscAddItem.Id ?? "" : "";
            d.AddCard1Id = r.SscAddCard1 != null ? r.SscAddCard1.Id ?? "" : "";
            d.AddCard2Id = r.SscAddCard2 != null ? r.SscAddCard2.Id ?? "" : "";
            d.AddCard3Id = r.SscAddCard3 != null ? r.SscAddCard3.Id ?? "" : "";
            d.RewardTier = r.SscRewardTier != null ? r.SscRewardTier.name ?? "" : "";
            d.Discount = r.SscDiscount;
            d.MaxQuantity = r.SscMaxQuantity;
            d.HealerUI = r.SscHealerUI;
            d.UpgradeUI = r.SscUpgradeUI;
            d.CraftUI = r.SscCraftUI;
            d.MerchantUI = r.SscMerchantUI;
            d.CorruptionUI = r.SscCorruptionUI;
            d.UpgradeRandomCard = r.SscUpgradeRandomCard;
            d.FinishGame = r.SscFinishGame;
            // Ssc has no FinishObeliskMap
            return d;
        }

        private static OutcomeDef SnapshotOutcomeFlc(EventReplyData r)
        {
            var d = new OutcomeDef();
            d.Text = r.FlcRewardText ?? "";
            d.HealPercent = r.FlcRewardHealthPercent;
            d.HealFlat = r.FlcRewardHealthFlat;
            d.Gold = r.FlcGoldReward;
            d.Dust = r.FlcDustReward;
            d.Supply = r.FlcSupplyReward;
            d.XP = r.FlcExperienceReward;
            d.CombatId = r.FlcCombat != null ? r.FlcCombat.CombatId ?? "" : "";
            d.EventId = r.FlcEvent != null ? r.FlcEvent.EventId ?? "" : "";
            d.NodeTravelId = r.FlcNodeTravel != null ? r.FlcNodeTravel.NodeId ?? "" : "";
            d.RequirementUnlockId = r.FlcRequirementUnlock != null ? r.FlcRequirementUnlock.RequirementId ?? "" : "";
            d.RequirementUnlock2Id = r.FlcRequirementUnlock2 != null ? r.FlcRequirementUnlock2.RequirementId ?? "" : "";
            d.RequirementLockId = r.FlcRequirementLock != null ? r.FlcRequirementLock.RequirementId ?? "" : "";
            // Flc has no RequirementLock2
            d.LootId = r.FlcLootList != null ? r.FlcLootList.Id ?? "" : "";
            d.ShopId = r.FlcShopList != null ? r.FlcShopList.Id ?? "" : "";
            d.AddItemId = r.FlcAddItem != null ? r.FlcAddItem.Id ?? "" : "";
            d.AddCard1Id = r.FlcAddCard1 != null ? r.FlcAddCard1.Id ?? "" : "";
            d.AddCard2Id = r.FlcAddCard2 != null ? r.FlcAddCard2.Id ?? "" : "";
            d.AddCard3Id = r.FlcAddCard3 != null ? r.FlcAddCard3.Id ?? "" : "";
            d.RewardTier = r.FlcRewardTier != null ? r.FlcRewardTier.name ?? "" : "";
            d.Discount = r.FlcDiscount;
            d.MaxQuantity = r.FlcMaxQuantity;
            d.HealerUI = r.FlcHealerUI;
            d.UpgradeUI = r.FlcUpgradeUI;
            d.CraftUI = r.FlcCraftUI;
            d.MerchantUI = r.FlcMerchantUI;
            d.CorruptionUI = r.FlcCorruptionUI;
            d.UpgradeRandomCard = r.FlcUpgradeRandomCard;
            // Flc has no FinishGame / FinishObeliskMap
            return d;
        }

    }
}
