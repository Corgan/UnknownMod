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
                // Temp dictionaries to resolve intra-zone references
                var cards = new Dictionary<string, CardData>();
                var npcs = new Dictionary<string, NPCData>();
                var combats = new Dictionary<string, CombatData>();
                var events = new Dictionary<string, EventData>();
                var nodes = new Dictionary<string, NodeData>();
                var items = new Dictionary<string, ItemData>();
                var loot = new Dictionary<string, LootData>();

                // ── 0. Loot ──────────────────────────────────────
                foreach (var d in zone.Loot.Values)
                {
                    var l = DataHelper.MakeLoot(d);
                    loot[d.Id] = l;
                    DataHelper.RegisterLoot(l);
                }

                // ── 1. Cards ─────────────────────────────────────
                foreach (var d in zone.Cards.Values.Where(c => !c.IsUpgraded))
                {
                    var c = MakeFullCard(d);
                    cards[d.Id] = c;
                    DataHelper.RegisterCard(c);
                }
                foreach (var d in zone.Cards.Values.Where(c => c.IsUpgraded))
                {
                    CardData c;
                    if (!string.IsNullOrEmpty(d.BaseCardId) && cards.TryGetValue(d.BaseCardId, out var bc))
                        c = DataHelper.MakeUpgradedCard(bc, d.Id, d.Name,
                            d.UpgDamageMult, d.UpgBonusCurseCharges,
                            d.UpgBonusAuraCharges, d.UpgBonusHeal);
                    else
                        c = MakeFullCard(d);
                    cards[d.Id] = c;
                    DataHelper.RegisterCard(c);
                }

                // ── 2. NPCs ──────────────────────────────────────
                foreach (var d in zone.Npcs.Values)
                {
                    string baseId = ResolveBaseNpcId(zone, d);
                    var npc = DataHelper.MakeFullNpc(d);
                    npcs[d.Id] = npc;
                    DataHelper.RegisterNPC(npc);
                }
                // Link variant chains
                foreach (var d in zone.Npcs.Values)
                {
                    if (!npcs.TryGetValue(d.Id, out var npc)) continue;
                    if (!string.IsNullOrEmpty(d.UpgradedMobId) && npcs.TryGetValue(d.UpgradedMobId, out var u))
                        npc.UpgradedMob = u;
                    if (!string.IsNullOrEmpty(d.NgPlusMobId) && npcs.TryGetValue(d.NgPlusMobId, out var n))
                        npc.NgPlusMob = n;
                    if (!string.IsNullOrEmpty(d.HellModeMobId) && npcs.TryGetValue(d.HellModeMobId, out var h))
                        npc.HellModeMob = h;
                }
                // Wire summon cards → NPCs
                foreach (var d in zone.Cards.Values)
                {
                    if (string.IsNullOrEmpty(d.SummonUnitId)) continue;
                    if (cards.TryGetValue(d.Id, out var card) && npcs.TryGetValue(d.SummonUnitId, out var sNpc))
                        Traverse.Create(card).Field("summonUnit").SetValue(sNpc);
                }

                // ── 3. Items ─────────────────────────────────────
                foreach (var d in zone.Items.Values)
                {
                    var item = DataHelper.MakeFullItem(d);
                    items[d.Id] = item;
                    DataHelper.RegisterItem(item);
                    var ic = DataHelper.MakeItemCard(d, item);
                    cards[d.Id] = ic;
                    DataHelper.RegisterCard(ic);
                }

                // ── 4. Combats ───────────────────────────────────
                foreach (var d in zone.Combats.Values)
                {
                    var npcArr = d.NpcIds
                        .Where(id => npcs.ContainsKey(id) || DataHelper.GetExistingNPC(id) != null)
                        .Select(id => npcs.TryGetValue(id, out var x) ? x : DataHelper.GetExistingNPC(id))
                        .Where(x => x != null)
                        .ToArray();
                    var combat = DataHelper.MakeCombat(d, npcArr);
                    combats[d.CombatId] = combat;
                    DataHelper.RegisterCombat(combat);
                }

                // ── 5. Zone + Nodes ──────────────────────────────
                var zoneData = ScriptableObject.CreateInstance<ZoneData>();
                zoneData.ZoneId = zone.ZoneId;
                zoneData.ZoneName = zone.ZoneName;
                zoneData.ObeliskLow = zone.ObeliskLow;
                zoneData.ObeliskHigh = zone.ObeliskHigh;
                zoneData.ObeliskFinal = zone.ObeliskFinal;
                zoneData.DisableExperienceOnThisZone = zone.DisableExperience;
                zoneData.DisableMadnessOnThisZone = zone.DisableMadness;
                zoneData.Sku = "";
                DataHelper.RegisterZone(zoneData);

                foreach (var d in zone.Nodes.Values)
                {
                    var nd = BuildNodeSO(d, combats, events);
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

                // ── 6. Events (two-pass for inter-event refs) ────
                foreach (var d in zone.Events.Values)
                {
                    var replies = d.Replies.Select(r => BuildReply(r, combats, events, nodes, loot)).ToArray();
                    var evt = DataHelper.MakeEvent(d.EventId, d.EventName,
                        d.Description, d.DescriptionAction, replies,
                        d.EventTier, d.ReplyRandom);
                    if (!string.IsNullOrEmpty(d.RequirementId))
                    {
                        var req = DataHelper.GetEventRequirement(d.RequirementId);
                        if (req != null) Traverse.Create(evt).Field("requirement").SetValue(req);
                    }
                    if (!string.IsNullOrEmpty(d.RequiredClassId))
                        evt.RequiredClass = DataHelper.GetSubClass(d.RequiredClassId);
                    if (d.EventIconShader != Enums.MapIconShader.None)
                        evt.EventIconShader = d.EventIconShader;
                    if (d.HistoryMode)
                        evt.HistoryMode = true;
                    if (!string.IsNullOrEmpty(d.SpriteSource))
                        DataHelper.CopyEventVisuals(evt, d.SpriteSource);
                    events[d.EventId] = evt;
                    DataHelper.RegisterEvent(evt);
                }
                // Second pass: wire inter-event refs
                foreach (var d in zone.Events.Values)
                {
                    if (!events.TryGetValue(d.EventId, out var evt)) continue;
                    var replies = evt.Replys;
                    for (int i = 0; i < d.Replies.Count && i < replies.Length; i++)
                    {
                        var r = d.Replies[i];
                        var reply = replies[i];
                        if (reply.SsEvent == null && !string.IsNullOrEmpty(r.Ss.EventId) && events.TryGetValue(r.Ss.EventId, out var se))
                            reply.SsEvent = se;
                        if (reply.FlEvent == null && !string.IsNullOrEmpty(r.Fl.EventId) && events.TryGetValue(r.Fl.EventId, out var fe))
                            reply.FlEvent = fe;
                        if (reply.SscEvent == null && !string.IsNullOrEmpty(r.Ssc.EventId) && events.TryGetValue(r.Ssc.EventId, out var sse))
                            reply.SscEvent = sse;
                        if (reply.FlcEvent == null && !string.IsNullOrEmpty(r.Flc.EventId) && events.TryGetValue(r.Flc.EventId, out var fle))
                            reply.FlcEvent = fle;
                    }
                }
                // Wire combat→event post-combat
                foreach (var d in zone.Combats.Values)
                {
                    if (string.IsNullOrEmpty(d.EventDataId)) continue;
                    if (combats.TryGetValue(d.CombatId, out var combat) && events.TryGetValue(d.EventDataId, out var pe))
                        combat.EventData = pe;
                }
                // Wire node→event
                foreach (var d in zone.Nodes.Values)
                {
                    if (string.IsNullOrEmpty(d.EventId)) continue;
                    if (nodes.TryGetValue(d.NodeId, out var nd) && events.TryGetValue(d.EventId, out var ne))
                    {
                        nd.NodeEvent = new[] { ne };
                        nd.NodeEventPriority = new[] { 0 };
                        nd.NodeEventPercent = new[] { 100 };
                        nd.NodeEventTier = d.NodeEventTier;
                    }
                }

                Plugin.Log.LogInfo($"[Builder] Zone '{zone.ZoneId}' built: " +
                    $"{nodes.Count} nodes, {combats.Count} combats, {events.Count} events, " +
                    $"{npcs.Count} NPCs, {cards.Count} cards");
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

        /// <summary>Public entry point for hot-reload to re-apply a zone patch at runtime.</summary>
        public static void ApplyZonePatchPublic(ZonePatchDef patch) => ApplyZonePatch(patch);

        private static void ApplyZonePatch(ZonePatchDef patch)
        {
            try
            {
                // Get existing zone from Globals
                var zoneDict = Traverse.Create(Globals.Instance)
                    .Field<Dictionary<string, ZoneData>>("_ZoneDataSource").Value;
                ZoneData zoneData = null;
                if (zoneDict != null)
                    zoneDict.TryGetValue(patch.TargetZoneId.ToLower(), out zoneData);

                if (zoneData == null)
                {
                    Plugin.Log.LogWarning($"[Builder] Zone patch target '{patch.TargetZoneId}' not found in Globals.");
                    return;
                }

                var newNodes = new Dictionary<string, NodeData>();
                var newCombats = new Dictionary<string, CombatData>();
                var newEvents = new Dictionary<string, EventData>();

                // ── Build encounters ─────────────────────────────
                foreach (var d in patch.Encounters.Values)
                {
                    var npcArr = d.NpcIds
                        .Select(id => DataHelper.GetExistingNPC(id))
                        .Where(x => x != null)
                        .ToArray();
                    var combat = DataHelper.MakeCombat(d, npcArr);
                    newCombats[d.CombatId] = combat;
                    DataHelper.RegisterCombat(combat);
                }

                // ── Build events (first pass) ────────────────────
                foreach (var d in patch.Events.Values)
                {
                    var replies = d.Replies.Select(r =>
                        DataHelper.MakeReply(r,
                            getCombat: id => newCombats.TryGetValue(id, out var c) ? c : null,
                            getEvent: id => newEvents.TryGetValue(id, out var e) ? e : DataHelper.GetExistingEvent(id),
                            getNode: id => newNodes.TryGetValue(id, out var n) ? n : DataHelper.GetExistingNode(id),
                            getLoot: id => DataHelper.GetLootData(id)
                        )).ToArray();
                    var evt = DataHelper.MakeEvent(d.EventId, d.EventName,
                        d.Description, d.DescriptionAction, replies,
                        d.EventTier, d.ReplyRandom);
                    if (!string.IsNullOrEmpty(d.RequirementId))
                    {
                        var req = DataHelper.GetEventRequirement(d.RequirementId);
                        if (req != null) Traverse.Create(evt).Field("requirement").SetValue(req);
                    }
                    if (!string.IsNullOrEmpty(d.RequiredClassId))
                        evt.RequiredClass = DataHelper.GetSubClass(d.RequiredClassId);
                    if (d.EventIconShader != Enums.MapIconShader.None)
                        evt.EventIconShader = d.EventIconShader;
                    if (d.HistoryMode)
                        evt.HistoryMode = true;
                    if (!string.IsNullOrEmpty(d.SpriteSource))
                        DataHelper.CopyEventVisuals(evt, d.SpriteSource);
                    newEvents[d.EventId] = evt;
                    DataHelper.RegisterEvent(evt);
                }

                // ── Build nodes ──────────────────────────────────
                foreach (var d in patch.Nodes.Values)
                {
                    var nd = BuildNodeSO(d, newCombats, newEvents);
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
                // Wire node→event
                foreach (var d in patch.Nodes.Values)
                {
                    if (string.IsNullOrEmpty(d.EventId)) continue;
                    if (!newNodes.TryGetValue(d.NodeId, out var nd)) continue;
                    EventData evt = null;
                    if (!newEvents.TryGetValue(d.EventId, out evt))
                        evt = DataHelper.GetExistingEvent(d.EventId);
                    if (evt != null)
                    {
                        nd.NodeEvent = new[] { evt };
                        nd.NodeEventPriority = new[] { 0 };
                        nd.NodeEventPercent = new[] { 100 };
                        nd.NodeEventTier = d.NodeEventTier;
                    }
                }

                // ── Wire inter-event refs (second pass) ──────────
                foreach (var d in patch.Events.Values)
                {
                    if (!newEvents.TryGetValue(d.EventId, out var evt)) continue;
                    var replies = evt.Replys;
                    for (int i = 0; i < d.Replies.Count && i < replies.Length; i++)
                    {
                        var r = d.Replies[i];
                        var reply = replies[i];
                        if (reply.SsEvent == null && !string.IsNullOrEmpty(r.Ss.EventId))
                        {
                            if (newEvents.TryGetValue(r.Ss.EventId, out var se)) reply.SsEvent = se;
                            else { var sse = DataHelper.GetExistingEvent(r.Ss.EventId); if (sse != null) reply.SsEvent = sse; }
                        }
                        if (reply.FlEvent == null && !string.IsNullOrEmpty(r.Fl.EventId))
                        {
                            if (newEvents.TryGetValue(r.Fl.EventId, out var fe)) reply.FlEvent = fe;
                            else { var ffe = DataHelper.GetExistingEvent(r.Fl.EventId); if (ffe != null) reply.FlEvent = ffe; }
                        }
                        if (reply.SscEvent == null && !string.IsNullOrEmpty(r.Ssc.EventId))
                        {
                            if (newEvents.TryGetValue(r.Ssc.EventId, out var sce)) reply.SscEvent = sce;
                            else { var sce2 = DataHelper.GetExistingEvent(r.Ssc.EventId); if (sce2 != null) reply.SscEvent = sce2; }
                        }
                        if (reply.FlcEvent == null && !string.IsNullOrEmpty(r.Flc.EventId))
                        {
                            if (newEvents.TryGetValue(r.Flc.EventId, out var fce)) reply.FlcEvent = fce;
                            else { var fce2 = DataHelper.GetExistingEvent(r.Flc.EventId); if (fce2 != null) reply.FlcEvent = fce2; }
                        }
                    }
                }

                // Wire combat→event post-combat
                foreach (var d in patch.Encounters.Values)
                {
                    if (string.IsNullOrEmpty(d.EventDataId)) continue;
                    if (newCombats.TryGetValue(d.CombatId, out var combat))
                    {
                        EventData pe = null;
                        if (!newEvents.TryGetValue(d.EventDataId, out pe))
                            pe = DataHelper.GetExistingEvent(d.EventDataId);
                        if (pe != null) combat.EventData = pe;
                    }
                }

                Plugin.Log.LogInfo($"[Builder] Zone patch '{patch.TargetZoneId}' applied: " +
                    $"{newNodes.Count} nodes, {newCombats.Count} encounters, {newEvents.Count} events, " +
                    $"{patch.Roads.Count} roads");
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
        private static NodeData BuildNodeSO(NodeDef d,
            Dictionary<string, CombatData> combats,
            Dictionary<string, EventData> events)
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

            if (!string.IsNullOrEmpty(d.NodeRequirementId))
                node.NodeRequirement = DataHelper.GetEventRequirement(d.NodeRequirementId);

            // Combat — resolve all combat IDs
            var combatList = new List<CombatData>();
            foreach (var cid in d.CombatIds)
            {
                if (string.IsNullOrEmpty(cid)) continue;
                CombatData combat = null;
                combats?.TryGetValue(cid, out combat);
                if (combat == null)
                {
                    // Fallback: existing base-game combat
                    var combatDict = Traverse.Create(Globals.Instance)
                        .Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
                    combatDict?.TryGetValue(cid.Replace(" ", "").ToLower(), out combat);
                }
                if (combat != null)
                    combatList.Add(combat);
            }
            node.NodeCombat = combatList.ToArray();
            node.NodeCombatTier = d.CombatTier;

            // Event — resolve all event IDs (may be null on first pass; wired in second pass)
            var eventList = new List<EventData>();
            foreach (var eid in d.EventIds)
            {
                if (string.IsNullOrEmpty(eid)) continue;
                EventData evt = null;
                events?.TryGetValue(eid, out evt);
                if (evt == null) evt = DataHelper.GetExistingEvent(eid);
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

        /// <summary>Build an EventReplyData from a ReplyDef, resolving refs from local dicts.</summary>
        private static EventReplyData BuildReply(ReplyDef r,
            Dictionary<string, CombatData> combats,
            Dictionary<string, EventData> events,
            Dictionary<string, NodeData> nodes,
            Dictionary<string, LootData> loot)
        {
            return DataHelper.MakeReply(r,
                getCombat: id =>
                {
                    if (combats != null && combats.TryGetValue(id, out var c)) return c;
                    var cd = Traverse.Create(Globals.Instance)
                        .Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
                    CombatData existing = null;
                    cd?.TryGetValue(id.Replace(" ", "").ToLower(), out existing);
                    return existing;
                },
                getEvent: id =>
                {
                    if (events != null && events.TryGetValue(id, out var e)) return e;
                    return DataHelper.GetExistingEvent(id);
                },
                getNode: id =>
                {
                    if (nodes != null && nodes.TryGetValue(id, out var n)) return n;
                    return DataHelper.GetExistingNode(id);
                },
                getLoot: id =>
                {
                    if (loot != null && loot.TryGetValue(id, out var l)) return l;
                    return DataHelper.GetLootData(id);
                });
        }

        /// <summary>Resolve base NPC ID from a zone's sprite system.</summary>
        private static string ResolveBaseNpcId(ZoneDef zone, NpcDef npcDef)
        {
            if (zone != null && !string.IsNullOrEmpty(npcDef.SpriteSource) &&
                zone.Sprites.TryGetValue(npcDef.SpriteSource, out var spriteDef) &&
                !string.IsNullOrEmpty(spriteDef.BaseSprite))
                return spriteDef.BaseSprite;
            return npcDef.SpriteSource;
        }
    }
}
