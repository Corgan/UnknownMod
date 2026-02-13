using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnknownMod.Definitions;

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
            string folder = ModRegistry.GetZoneFolder(CurrentZone.ZoneId);
            SaveToFolder(CurrentZone, folder);
            Plugin.Log.LogInfo($"[ZoneEditing] Saved zone to: {folder}");
        }

        private static void SaveToFolder(ZoneDef def, string folder)
        {
            foreach (string sub in new[] { "", "nodes", "combats", "events", "npcs", "cards", "items", "loot", "sprites", "textures" })
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
        //  NODE ADD / DELETE
        // ═══════════════════════════════════════════════════════════════

        public static string AddNode(float posX, float posY)
        {
            if (CurrentZone == null) return null;
            string prefix = CurrentZone.IdPrefix;
            if (string.IsNullOrEmpty(prefix))
            {
                Plugin.Log.LogError("[ZoneEditing] Cannot add node: zone has no IdPrefix.");
                return null;
            }

            int nextNum = 0;
            foreach (var id in CurrentZone.Nodes.Keys)
            {
                if (id.StartsWith(prefix + "_") && int.TryParse(id.Substring(prefix.Length + 1), out int num))
                    nextNum = Math.Max(nextNum, num + 1);
            }

            string nodeId = $"{prefix}_{nextNum}";
            var nodeDef = new NodeDef { NodeId = nodeId, NodeName = $"New Node {nextNum}", PosX = posX, PosY = posY };

            CurrentZone.Nodes[nodeId] = nodeDef;

            // Build SO and register in Globals
            var node = BuildNodeSO(nodeDef);
            if (Globals.Instance != null)
            {
                var zoneData = Globals.Instance.GetZoneData(CurrentZone.ZoneId);
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
                var zoneData = Globals.Instance?.GetZoneData(CurrentZone.ZoneId);
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
            node.NodesConnectedRequirement = new NodesConnectedRequirement[0];

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
    }
}
