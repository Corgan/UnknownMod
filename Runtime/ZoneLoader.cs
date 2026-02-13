using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Loads modded zone data from per-entity JSON folders and creates game ScriptableObjects.
    /// Also builds runtime zone maps (background + nodes + roads) and provides save functionality.
    /// </summary>
    public static class ZoneLoader
    {
        // ── Current zone DTO (source of truth for editor) ────────────
        public static ZoneDef CurrentZone { get; set; }

        // ── All loaded zone DTOs ─────────────────────────────────────
        public static readonly Dictionary<string, ZoneDef> LoadedZones = new();

        // ── Global mod registries (all mods merged, last wins) ───────
        /// <summary>Global sprite overrides from all loaded mods. Keyed by sprite def ID (usually NPC ID).</summary>
        public static readonly Dictionary<string, SpriteOverrideDef> GlobalSprites = new();

        /// <summary>Global NPC definitions from all loaded mods. Keyed by NPC ID.</summary>
        public static readonly Dictionary<string, NpcDef> GlobalNpcs = new();

        /// <summary>Zone folder path map. Keyed by zone ID, value is the disk folder path.</summary>
        public static readonly Dictionary<string, string> ZoneFolderMap = new();

        // ── Runtime ScriptableObject maps (after loading) ────────────
        public static readonly Dictionary<string, CardData> Cards = new();
        public static readonly Dictionary<string, NPCData> Npcs = new();
        public static readonly Dictionary<string, CombatData> Combats = new();
        public static readonly Dictionary<string, EventData> Events = new();
        public static readonly Dictionary<string, NodeData> Nodes = new();
        public static readonly Dictionary<string, ZoneData> Zones = new();
        public static readonly Dictionary<string, ItemData> Items = new();
        public static readonly Dictionary<string, LootData> LootTables = new();

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

        // ── Paths ────────────────────────────────────────────────────
        private static string DataFolder =>
            Path.Combine(BepInEx.Paths.PluginPath, "UnknownMod_Data");

        public static string GetZoneFolder(string zoneId)
        {
            if (ZoneFolderMap.TryGetValue(zoneId, out var mapped))
                return mapped;
            return Path.Combine(DataFolder, zoneId);
        }

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        // ═══════════════════════════════════════════════════════════════
        //  MAIN ENTRY POINT
        // ═══════════════════════════════════════════════════════════════

        public static void LoadAll()
        {
            LoadedZones.Clear();
            Cards.Clear(); Npcs.Clear(); Combats.Clear(); Events.Clear();
            Nodes.Clear(); Zones.Clear(); Items.Clear(); LootTables.Clear();
            GlobalSprites.Clear(); GlobalNpcs.Clear(); ZoneFolderMap.Clear();
            _backgroundSprites.Clear();
            NpcPrefabBuilder.ClearCache();

            if (!Directory.Exists(DataFolder))
            {
                Plugin.Log.LogError($"[ZoneLoader] Data folder not found: {DataFolder}");
                return;
            }

            foreach (var dir in Directory.GetDirectories(DataFolder))
            {
                string zoneJsonPath = Path.Combine(dir, "zone.json");
                if (!File.Exists(zoneJsonPath)) continue;

                Plugin.Log.LogInfo($"[ZoneLoader] Loading zone from: {dir}");
                var zoneDef = LoadFromFolder(dir);
                if (string.IsNullOrEmpty(zoneDef.ZoneId))
                {
                    Plugin.Log.LogWarning($"[ZoneLoader] Skipping folder with empty ZoneId: {dir}");
                    continue;
                }

                LoadedZones[zoneDef.ZoneId] = zoneDef;
                BuildFromDef(zoneDef);
                Plugin.Log.LogInfo($"[ZoneLoader] Loaded zone: {zoneDef.ZoneId}");
            }

            if (LoadedZones.Count > 0)
                CurrentZone = LoadedZones.Values.First();

            Plugin.Log.LogInfo($"[ZoneLoader] Loaded {LoadedZones.Count} zone(s) from {DataFolder}");

            // Ensure the persistent ZoneEditor exists (accessible from anywhere)
            EnsureEditorExists();
        }

        // ═══════════════════════════════════════════════════════════════
        //  FOLDER-BASED LOADING
        // ═══════════════════════════════════════════════════════════════

        private static ZoneDef LoadFromFolder(string folder)
        {
            var def = new ZoneDef();

            string zonePath = Path.Combine(folder, "zone.json");
            if (File.Exists(zonePath))
            {
                var meta = JsonConvert.DeserializeObject<ZoneDef>(File.ReadAllText(zonePath));
                def.ZoneId = meta.ZoneId;
                def.ZoneName = meta.ZoneName;
                def.IdPrefix = meta.IdPrefix;
                def.ObeliskLow = meta.ObeliskLow;
                def.ObeliskHigh = meta.ObeliskHigh;
                def.ObeliskFinal = meta.ObeliskFinal;
                def.DisableExperience = meta.DisableExperience;
                def.DisableMadness = meta.DisableMadness;
                def.BackgroundImage = meta.BackgroundImage ?? "background.jpeg";
            }

            LoadEntities<NodeDef>(Path.Combine(folder, "nodes"), def.Nodes, d => d.NodeId);
            LoadEntities<CombatDef>(Path.Combine(folder, "combats"), def.Combats, d => d.CombatId);
            LoadEntities<EventDef>(Path.Combine(folder, "events"), def.Events, d => d.EventId);
            LoadEntities<NpcDef>(Path.Combine(folder, "npcs"), def.Npcs, d => d.Id);
            LoadEntities<CardDef>(Path.Combine(folder, "cards"), def.Cards, d => d.Id);
            LoadEntities<ItemDef>(Path.Combine(folder, "items"), def.Items, d => d.Id);
            LoadEntities<LootDef>(Path.Combine(folder, "loot"), def.Loot, d => d.Id);
            // Load sprite definitions into Sprites dict
            LoadEntities<SpriteOverrideDef>(Path.Combine(folder, "sprites"), def.Sprites, d => d.NpcId);

            // Backward compat: infer BaseSprite for old-format entries that lack it.
            // Old format keyed sprites by NPC ID with no BaseSprite field — we can
            // recover it from the matching NpcDef's SpriteSource.
            foreach (var kvp in def.Sprites)
            {
                if (string.IsNullOrEmpty(kvp.Value.BaseSprite) &&
                    def.Npcs.TryGetValue(kvp.Key, out var matchNpc))
                    kvp.Value.BaseSprite = matchNpc.SpriteSource;
            }

            string roadsPath = Path.Combine(folder, "roads.json");
            if (File.Exists(roadsPath))
            {
                var roads = JsonConvert.DeserializeObject<Dictionary<string, RoadDef>>(File.ReadAllText(roadsPath));
                if (roads != null) def.Roads = roads;
            }

            Plugin.Log.LogInfo($"[ZoneLoader] Folder loaded: {def.Nodes.Count} nodes, {def.Combats.Count} combats, " +
                $"{def.Events.Count} events, {def.Npcs.Count} npcs, {def.Cards.Count} cards, " +
                $"{def.Items.Count} items, {def.Loot.Count} loot, {def.Roads.Count} roads, {def.Sprites.Count} sprites");

            // Sort animation keyframes by Time after deserialization (JSON order not guaranteed)
            foreach (var sprOvr in def.Sprites.Values)
            {
                if (sprOvr.AnimOverrides == null) continue;
                foreach (var animOvr in sprOvr.AnimOverrides.Values)
                {
                    foreach (var kfList in animOvr.BoneKeyframes.Values)
                        kfList.Sort((a, b) => a.Time.CompareTo(b.Time));
                }
            }

            return def;
        }

        private static void LoadEntities<T>(string folder, Dictionary<string, T> dict, Func<T, string> getKey)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    var entity = JsonConvert.DeserializeObject<T>(File.ReadAllText(file));
                    if (entity != null)
                    {
                        string key = getKey(entity);
                        if (!string.IsNullOrEmpty(key))
                            dict[key] = entity;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[ZoneLoader] Failed to load '{file}': {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  FOLDER-BASED SAVING
        // ═══════════════════════════════════════════════════════════════

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
        //  BUILD FROM JSON DTO → ScriptableObjects
        // ═══════════════════════════════════════════════════════════════

        private static void BuildFromDef(ZoneDef def)
        {
            // ── 0. Loot ──────────────────────────────────────────
            foreach (var lootDef in def.Loot.Values)
            {
                var loot = DataHelper.MakeLoot(lootDef);
                LootTables[lootDef.Id] = loot;
                DataHelper.RegisterLoot(loot);
            }
            if (def.Loot.Count > 0)
                Plugin.Log.LogInfo($"[ZoneLoader] Created {def.Loot.Count} loot tables.");

            // ── 1. Cards ─────────────────────────────────────────
            var baseCards = def.Cards.Values.Where(c => !c.IsUpgraded).ToList();
            foreach (var cardDef in baseCards)
            {
                var card = CreateCard(cardDef);
                Cards[cardDef.Id] = card;
                DataHelper.RegisterCard(card);
            }

            var upgradedCards = def.Cards.Values.Where(c => c.IsUpgraded).ToList();
            foreach (var cardDef in upgradedCards)
            {
                CardData card;
                if (!string.IsNullOrEmpty(cardDef.BaseCardId) && Cards.TryGetValue(cardDef.BaseCardId, out var baseCard))
                {
                    card = DataHelper.MakeUpgradedCard(baseCard, cardDef.Id, cardDef.Name,
                        cardDef.UpgDamageMult, cardDef.UpgBonusCurseCharges,
                        cardDef.UpgBonusAuraCharges, cardDef.UpgBonusHeal);
                }
                else
                {
                    card = CreateCard(cardDef);
                }
                Cards[cardDef.Id] = card;
                DataHelper.RegisterCard(card);
            }

            Plugin.Log.LogInfo($"[ZoneLoader] Created {Cards.Count} cards.");

            // ── 2. NPCs ──────────────────────────────────────────
            foreach (var npcDef in def.Npcs.Values)
            {
                string baseNpcId = ResolveBaseNpcId(def, npcDef);
                var npc = DataHelper.MakeNPC(npcDef, baseNpcId);
                var aiCards = ResolveAICards(npcDef.AiCards);
                npc.AICards = aiCards;

                // Build custom prefab if sprite definition exists
                var sprDef = ResolveSpriteDefForNpc(def, npcDef);
                if (sprDef != null)
                {
                    var customPrefab = NpcPrefabBuilder.BuildCustomPrefab(npcDef.Id, npc, sprDef, def.ZoneId);
                    if (customPrefab != null)
                        npc.GameObjectAnimated = customPrefab;
                }

                Npcs[npcDef.Id] = npc;
                DataHelper.RegisterNPC(npc);
            }

            // Link variant chains
            foreach (var npcDef in def.Npcs.Values)
            {
                if (!Npcs.TryGetValue(npcDef.Id, out var npc)) continue;

                if (!string.IsNullOrEmpty(npcDef.UpgradedMobId) && Npcs.TryGetValue(npcDef.UpgradedMobId, out var upgMob))
                    npc.UpgradedMob = upgMob;
                if (!string.IsNullOrEmpty(npcDef.NgPlusMobId) && Npcs.TryGetValue(npcDef.NgPlusMobId, out var ngPlus))
                    npc.NgPlusMob = ngPlus;
                if (!string.IsNullOrEmpty(npcDef.HellModeMobId) && Npcs.TryGetValue(npcDef.HellModeMobId, out var hellMob))
                    npc.HellModeMob = hellMob;
            }

            // Wire summon cards
            foreach (var cardDef in def.Cards.Values)
            {
                if (string.IsNullOrEmpty(cardDef.SummonUnitId)) continue;
                if (!Cards.TryGetValue(cardDef.Id, out var card)) continue;
                if (!Npcs.TryGetValue(cardDef.SummonUnitId, out var summonNpc)) continue;
                Traverse.Create(card).Field("summonUnit").SetValue(summonNpc);
            }

            Plugin.Log.LogInfo($"[ZoneLoader] Created {Npcs.Count} NPCs.");

            // ── 3. Items ─────────────────────────────────────────
            // (built before events so event replies can reference modded item cards)
            foreach (var itemDef in def.Items.Values)
            {
                var item = DataHelper.MakeItem(itemDef);
                Items[itemDef.Id] = item;
                DataHelper.RegisterItem(item);

                var itemCard = DataHelper.MakeItemCard(itemDef, item);
                Cards[itemDef.Id] = itemCard;
                DataHelper.RegisterCard(itemCard);
            }

            if (def.Items.Count > 0)
                Plugin.Log.LogInfo($"[ZoneLoader] Created {def.Items.Count} items.");

            // ── 4. Combats ───────────────────────────────────────
            foreach (var combatDef in def.Combats.Values)
            {
                var npcList = combatDef.NpcIds
                    .Where(id => Npcs.ContainsKey(id))
                    .Select(id => Npcs[id])
                    .ToArray();

                var combat = DataHelper.MakeCombat(combatDef, npcList);

                // Wire summon-on-kill NPC
                if (!string.IsNullOrEmpty(combatDef.NpcToSummonOnKilledId) && Npcs.TryGetValue(combatDef.NpcToSummonOnKilledId, out var summonNpc))
                    combat.NpcToSummonOnNpcKilled = summonNpc;

                Combats[combatDef.CombatId] = combat;
                DataHelper.RegisterCombat(combat);
            }

            Plugin.Log.LogInfo($"[ZoneLoader] Created {Combats.Count} combats.");

            // ── 5. Zone + Nodes ──────────────────────────────────
            // (built before events so event replies can reference node travel targets)
            var zone = ScriptableObject.CreateInstance<ZoneData>();
            zone.ZoneId = def.ZoneId;
            zone.ZoneName = def.ZoneName;
            zone.ObeliskLow = def.ObeliskLow;
            zone.ObeliskHigh = def.ObeliskHigh;
            zone.ObeliskFinal = def.ObeliskFinal;
            zone.DisableExperienceOnThisZone = def.DisableExperience;
            zone.DisableMadnessOnThisZone = def.DisableMadness;
            zone.Sku = "";
            Zones[def.ZoneId] = zone;
            DataHelper.RegisterZone(zone);

            foreach (var nodeDef in def.Nodes.Values)
            {
                var node = CreateNode(nodeDef);
                node.NodeZone = zone;
                Nodes[nodeDef.NodeId] = node;
                DataHelper.RegisterNode(node);
            }

            // Resolve connections
            foreach (var nodeDef in def.Nodes.Values)
            {
                if (!Nodes.TryGetValue(nodeDef.NodeId, out var node)) continue;
                var connected = nodeDef.Connections
                    .Where(id => Nodes.ContainsKey(id))
                    .Select(id => Nodes[id])
                    .ToArray();
                node.NodesConnected = connected;

                // Conditional connection requirements
                if (nodeDef.ConnectionRequirements != null && nodeDef.ConnectionRequirements.Count > 0)
                {
                    var reqs = new List<NodesConnectedRequirement>();
                    foreach (var cr in nodeDef.ConnectionRequirements)
                    {
                        var ncr = new NodesConnectedRequirement();
                        if (!string.IsNullOrEmpty(cr.TargetNodeId) && Nodes.TryGetValue(cr.TargetNodeId, out var targetNode))
                            ncr.NodeData = targetNode;
                        if (!string.IsNullOrEmpty(cr.RequirementId))
                            ncr.ConectionRequeriment = DataHelper.GetEventRequirement(cr.RequirementId);
                        if (!string.IsNullOrEmpty(cr.IfNotNodeId) && Nodes.TryGetValue(cr.IfNotNodeId, out var ifNotNode))
                            ncr.ConectionIfNotNode = ifNotNode;
                        reqs.Add(ncr);
                    }
                    node.NodesConnectedRequirement = reqs.ToArray();
                }
            }

            Plugin.Log.LogInfo($"[ZoneLoader] Created zone '{def.ZoneId}' with {Nodes.Count} nodes.");

            // ── 6. Events ────────────────────────────────────────
            // First pass: create all events (inter-event refs may be null if target not yet built)
            foreach (var eventDef in def.Events.Values)
            {
                var replies = eventDef.Replies.Select(r => CreateReply(r)).ToArray();
                var evt = DataHelper.MakeEvent(
                    eventDef.EventId, eventDef.EventName,
                    eventDef.Description, eventDef.DescriptionAction, replies,
                    eventDef.EventTier, eventDef.ReplyRandom);

                // Wire requirement
                if (!string.IsNullOrEmpty(eventDef.RequirementId))
                {
                    var req = DataHelper.GetEventRequirement(eventDef.RequirementId);
                    if (req != null)
                        Traverse.Create(evt).Field("requirement").SetValue(req);
                }

                Events[eventDef.EventId] = evt;
                DataHelper.RegisterEvent(evt);
            }

            // Second pass: wire inter-event reply references that were null during first pass
            foreach (var eventDef in def.Events.Values)
            {
                if (!Events.TryGetValue(eventDef.EventId, out var evt)) continue;
                var replies = evt.Replys;
                for (int i = 0; i < eventDef.Replies.Count && i < replies.Length; i++)
                {
                    var rDef = eventDef.Replies[i];
                    var reply = replies[i];
                    if (reply.SsEvent == null && !string.IsNullOrEmpty(rDef.Ss.EventId) && Events.TryGetValue(rDef.Ss.EventId, out var ssEvt))
                        reply.SsEvent = ssEvt;
                    if (reply.FlEvent == null && !string.IsNullOrEmpty(rDef.Fl.EventId) && Events.TryGetValue(rDef.Fl.EventId, out var flEvt))
                        reply.FlEvent = flEvt;
                    if (reply.SscEvent == null && !string.IsNullOrEmpty(rDef.Ssc.EventId) && Events.TryGetValue(rDef.Ssc.EventId, out var sscEvt))
                        reply.SscEvent = sscEvt;
                    if (reply.FlcEvent == null && !string.IsNullOrEmpty(rDef.Flc.EventId) && Events.TryGetValue(rDef.Flc.EventId, out var flcEvt))
                        reply.FlcEvent = flcEvt;
                }
            }

            // Third pass: wire combat→event post-combat events
            foreach (var combatDef in def.Combats.Values)
            {
                if (string.IsNullOrEmpty(combatDef.EventDataId)) continue;
                if (Combats.TryGetValue(combatDef.CombatId, out var combat) &&
                    Events.TryGetValue(combatDef.EventDataId, out var postEvt))
                    combat.EventData = postEvt;
            }

            // Fourth pass: wire node→event references (nodes were created before events)
            foreach (var nodeDef in def.Nodes.Values)
            {
                if (string.IsNullOrEmpty(nodeDef.EventId)) continue;
                if (!Nodes.TryGetValue(nodeDef.NodeId, out var node)) continue;
                if (Events.TryGetValue(nodeDef.EventId, out var nodeEvt))
                {
                    node.NodeEvent = new[] { nodeEvt };
                    node.NodeEventPriority = new[] { 0 };
                    node.NodeEventPercent = new[] { 100 };
                    node.NodeEventTier = nodeDef.NodeEventTier;
                }
            }

            Plugin.Log.LogInfo($"[ZoneLoader] Created {Events.Count} events.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  FACTORY METHODS (DTO → ScriptableObject)
        // ═══════════════════════════════════════════════════════════════

        private static CardData CreateCard(CardDef d)
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

            // Apply extended fields
            DataHelper.ApplyCardExtras(card, d);

            return card;
        }

        private static EventReplyData CreateReply(ReplyDef r)
        {
            return DataHelper.MakeReply(r,
                getCombat: id => Combats.TryGetValue(id, out var c) ? c : null,
                getEvent: id => Events.TryGetValue(id, out var e) ? e : null,
                getNode: id => Nodes.TryGetValue(id, out var n) ? n : null,
                getLoot: id =>
                {
                    if (LootTables.TryGetValue(id, out var l)) return l;
                    return DataHelper.GetLootData(id); // fallback to base game loot
                });
        }

        private static NodeData CreateNode(NodeDef d)
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

            // Combat
            if (!string.IsNullOrEmpty(d.CombatId) && Combats.TryGetValue(d.CombatId, out var combat))
            {
                node.NodeCombat = new[] { combat };
                node.NodeCombatTier = d.CombatTier;
            }
            else
            {
                node.NodeCombat = new CombatData[0];
            }

            // Event
            if (!string.IsNullOrEmpty(d.EventId) && Events.TryGetValue(d.EventId, out var evt))
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
                if (!Cards.TryGetValue(d.CardId, out var card))
                {
                    Plugin.Log.LogWarning($"[ZoneLoader] AICard references missing card '{d.CardId}'");
                    continue;
                }
                result.Add(DataHelper.MakeAICard(card, d));
            }
            return result.ToArray();
        }

        // ═══════════════════════════════════════════════════════════════
        //  HOT-RELOAD: Rebuild individual entities from DTO
        // ═══════════════════════════════════════════════════════════════

        public static void RebuildCard(string cardId)
        {
            if (CurrentZone == null || !CurrentZone.Cards.TryGetValue(cardId, out var cardDef)) return;
            try
            {
                CardData card;
                if (cardDef.IsUpgraded && !string.IsNullOrEmpty(cardDef.BaseCardId) && Cards.TryGetValue(cardDef.BaseCardId, out var baseCard))
                {
                    card = DataHelper.MakeUpgradedCard(baseCard, cardDef.Id, cardDef.Name,
                        cardDef.UpgDamageMult, cardDef.UpgBonusCurseCharges,
                        cardDef.UpgBonusAuraCharges, cardDef.UpgBonusHeal);
                }
                else
                {
                    card = CreateCard(cardDef);
                }

                if (!string.IsNullOrEmpty(cardDef.SummonUnitId) && Npcs.TryGetValue(cardDef.SummonUnitId, out var summonNpc))
                    Traverse.Create(card).Field("summonUnit").SetValue(summonNpc);

                Cards[cardId] = card;
                DataHelper.RegisterCard(card);
                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneLoader] RebuildCard '{cardId}' failed: {ex.Message}"); }
        }

        public static void RebuildNpc(string npcId)
        {
            if (CurrentZone == null || !CurrentZone.Npcs.TryGetValue(npcId, out var npcDef)) return;
            try
            {
                var npc = DataHelper.MakeNPC(npcDef, ResolveBaseNpcId(CurrentZone, npcDef));
                var aiCards = ResolveAICards(npcDef.AiCards);
                npc.AICards = aiCards;

                if (!string.IsNullOrEmpty(npcDef.UpgradedMobId) && Npcs.TryGetValue(npcDef.UpgradedMobId, out var upgMob))
                    npc.UpgradedMob = upgMob;
                if (!string.IsNullOrEmpty(npcDef.NgPlusMobId) && Npcs.TryGetValue(npcDef.NgPlusMobId, out var ngPlus))
                    npc.NgPlusMob = ngPlus;
                if (!string.IsNullOrEmpty(npcDef.HellModeMobId) && Npcs.TryGetValue(npcDef.HellModeMobId, out var hellMob))
                    npc.HellModeMob = hellMob;

                ApplySpriteOverrides(npc, npcDef);

                // Build custom prefab if sprite definition exists
                var sprDef = ResolveSpriteDefForNpc(CurrentZone, npcDef);
                if (sprDef != null)
                {
                    NpcPrefabBuilder.InvalidateCache(npcId);
                    var customPrefab = NpcPrefabBuilder.BuildCustomPrefab(npcId, npc, sprDef, CurrentZone.ZoneId);
                    if (customPrefab != null)
                        npc.GameObjectAnimated = customPrefab;
                }

                Npcs[npcId] = npc;
                DataHelper.RegisterNPC(npc);

                foreach (var kvp in CurrentZone.Npcs)
                {
                    if (kvp.Key == npcId) continue;
                    if (kvp.Value.UpgradedMobId == npcId && Npcs.TryGetValue(kvp.Key, out var parent1))
                        parent1.UpgradedMob = npc;
                    if (kvp.Value.NgPlusMobId == npcId && Npcs.TryGetValue(kvp.Key, out var parent2))
                        parent2.NgPlusMob = npc;
                    if (kvp.Value.HellModeMobId == npcId && Npcs.TryGetValue(kvp.Key, out var parent3))
                        parent3.HellModeMob = npc;
                }

                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneLoader] RebuildNpc '{npcId}' failed: {ex.Message}"); }
        }

        public static void RebuildCombat(string combatId)
        {
            if (CurrentZone == null || !CurrentZone.Combats.TryGetValue(combatId, out var combatDef)) return;
            try
            {
                var npcList = combatDef.NpcIds
                    .Where(id => Npcs.ContainsKey(id))
                    .Select(id => Npcs[id])
                    .ToArray();

                var combat = DataHelper.MakeCombat(combatDef, npcList);

                if (!string.IsNullOrEmpty(combatDef.EventDataId) && Events.TryGetValue(combatDef.EventDataId, out var postEvt))
                    combat.EventData = postEvt;
                if (!string.IsNullOrEmpty(combatDef.NpcToSummonOnKilledId) && Npcs.TryGetValue(combatDef.NpcToSummonOnKilledId, out var summonNpc))
                    combat.NpcToSummonOnNpcKilled = summonNpc;

                Combats[combatId] = combat;
                DataHelper.RegisterCombat(combat);

                foreach (var node in Nodes.Values)
                {
                    if (node.NodeCombat != null && node.NodeCombat.Length > 0 &&
                        node.NodeCombat[0] != null && node.NodeCombat[0].CombatId == combatId)
                        node.NodeCombat = new[] { combat };
                }

                foreach (var evt in Events.Values)
                {
                    if (evt.Replys == null) continue;
                    foreach (var reply in evt.Replys)
                    {
                        if (reply.SsCombat != null && reply.SsCombat.CombatId == combatId)
                            reply.SsCombat = combat;
                        if (reply.FlCombat != null && reply.FlCombat.CombatId == combatId)
                            reply.FlCombat = combat;
                        if (reply.SscCombat != null && reply.SscCombat.CombatId == combatId)
                            reply.SscCombat = combat;
                        if (reply.FlcCombat != null && reply.FlcCombat.CombatId == combatId)
                            reply.FlcCombat = combat;
                    }
                }

                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneLoader] RebuildCombat '{combatId}' failed: {ex.Message}"); }
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

                Events[eventId] = evt;
                DataHelper.RegisterEvent(evt);

                foreach (var node in Nodes.Values)
                {
                    if (node.NodeEvent != null && node.NodeEvent.Length > 0 &&
                        node.NodeEvent[0] != null && node.NodeEvent[0].EventId == eventId)
                        node.NodeEvent = new[] { evt };
                }

                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneLoader] RebuildEvent '{eventId}' failed: {ex.Message}"); }
        }

        public static void RebuildNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;
            try
            {
                var node = CreateNode(nodeDef);
                if (CurrentZone != null && Zones.TryGetValue(CurrentZone.ZoneId, out var zone))
                    node.NodeZone = zone;

                var connected = nodeDef.Connections
                    .Where(id => Nodes.ContainsKey(id))
                    .Select(id => Nodes[id])
                    .ToArray();
                node.NodesConnected = connected;

                Nodes[nodeId] = node;
                DataHelper.RegisterNode(node);
                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneLoader] RebuildNode '{nodeId}' failed: {ex.Message}"); }
        }

        public static void RebuildItem(string itemId)
        {
            if (CurrentZone == null || !CurrentZone.Items.TryGetValue(itemId, out var itemDef)) return;
            try
            {
                var item = DataHelper.MakeItem(itemDef);
                Items[itemId] = item;
                DataHelper.RegisterItem(item);

                var itemCard = DataHelper.MakeItemCard(itemDef, item);
                Cards[itemId] = itemCard;
                DataHelper.RegisterCard(itemCard);

                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneLoader] RebuildItem '{itemId}' failed: {ex.Message}"); }
        }

        public static void RebuildLoot(string lootId)
        {
            if (CurrentZone == null || !CurrentZone.Loot.TryGetValue(lootId, out var lootDef)) return;
            try
            {
                var loot = DataHelper.MakeLoot(lootDef);
                LootTables[lootId] = loot;
                DataHelper.RegisterLoot(loot);
                MarkDirty();
            }
            catch (Exception ex) { Plugin.Log.LogError($"[ZoneLoader] RebuildLoot '{lootId}' failed: {ex.Message}"); }
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
                Plugin.Log.LogError("[ZoneLoader] Cannot add node: zone has no IdPrefix.");
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

            var node = CreateNode(nodeDef);
            if (Zones.TryGetValue(CurrentZone.ZoneId, out var zone))
                node.NodeZone = zone;
            Nodes[nodeId] = node;
            DataHelper.RegisterNode(node);

            MarkDirty();
            Plugin.Log.LogInfo($"[ZoneLoader] Added node '{nodeId}' at ({posX:F1}, {posY:F1})");
            return nodeId;
        }

        public static void DeleteNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;

            if (!string.IsNullOrEmpty(nodeDef.CombatId) && CurrentZone.Combats.ContainsKey(nodeDef.CombatId))
            {
                string expectedCombatId = "c" + nodeId;
                if (nodeDef.CombatId == expectedCombatId)
                {
                    CurrentZone.Combats.Remove(nodeDef.CombatId);
                    Combats.Remove(nodeDef.CombatId);
                }
            }

            if (!string.IsNullOrEmpty(nodeDef.EventId) && CurrentZone.Events.ContainsKey(nodeDef.EventId))
            {
                string expectedEventId = $"e_{nodeId}_a";
                if (nodeDef.EventId == expectedEventId)
                {
                    CurrentZone.Events.Remove(nodeDef.EventId);
                    Events.Remove(nodeDef.EventId);
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
            Nodes.Remove(nodeId);

            MarkDirty();
            Plugin.Log.LogInfo($"[ZoneLoader] Deleted node '{nodeId}'");
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
            Combats[combatId] = combat;
            DataHelper.RegisterCombat(combat);

            RebuildNode(nodeId);
            MarkDirty();

            Plugin.Log.LogInfo($"[ZoneLoader] Created combat '{combatId}' for node '{nodeId}'");
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
            Events[eventId] = evt;
            DataHelper.RegisterEvent(evt);

            RebuildNode(nodeId);
            MarkDirty();

            Plugin.Log.LogInfo($"[ZoneLoader] Created event '{eventId}' for node '{nodeId}'");
            return eventId;
        }

        public static void RemoveCombatFromNode(string nodeId)
        {
            if (CurrentZone == null || !CurrentZone.Nodes.TryGetValue(nodeId, out var nodeDef)) return;
            if (string.IsNullOrEmpty(nodeDef.CombatId)) return;

            string expectedId = "c" + nodeId;
            if (nodeDef.CombatId == expectedId)
            {
                CurrentZone.Combats.Remove(nodeDef.CombatId);
                Combats.Remove(nodeDef.CombatId);
            }

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
            {
                CurrentZone.Events.Remove(nodeDef.EventId);
                Events.Remove(nodeDef.EventId);
            }

            nodeDef.EventId = "";
            nodeDef.EventPercent = -1;
            RebuildNode(nodeId);
            MarkDirty();
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
                Plugin.Log.LogError("[ZoneLoader] Reflow failed: no entrance node (_0) found.");
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
                Plugin.Log.LogInfo("[ZoneLoader] Reflow: node IDs already in BFS order.");
                return;
            }

            ApplyRenameMap(renameMap);
            Plugin.Log.LogInfo($"[ZoneLoader] Reflow complete: renamed {renameMap.Count} node(s).");
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

            foreach (var id in Nodes.Keys.ToList()) Nodes.Remove(id);
            foreach (var id in Combats.Keys.ToList()) Combats.Remove(id);
            foreach (var id in Events.Keys.ToList()) Events.Remove(id);

            BuildFromDef(CurrentZone);
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

        private static void ApplySpriteOverrides(NPCData npc, NpcDef npcDef)
        {
            // Sprite overrides are applied at runtime via SpriteEditor.ApplyOverrides()
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT REGISTRATION (called by ModProjectBuilder)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Register a mod-built zone so IsModdedZone/BuildAndInjectMap/GetBackgroundSprite work.
        /// </summary>
        public static void RegisterModZone(ZoneDef zoneDef, string folderPath)
        {
            LoadedZones[zoneDef.ZoneId] = zoneDef;
            ZoneFolderMap[zoneDef.ZoneId] = folderPath;
            Plugin.Log.LogInfo($"[ZoneLoader] Registered mod zone '{zoneDef.ZoneId}' at {folderPath}");
        }

        /// <summary>
        /// Merge a mod's sprite defs into the global registries. Last writer wins.
        /// </summary>
        public static void RegisterModSprites(
            Dictionary<string, SpriteOverrideDef> sprites,
            Dictionary<string, SpriteOverrideDef> spritePatches)
        {
            if (sprites != null)
                foreach (var kvp in sprites)
                    GlobalSprites[kvp.Key] = kvp.Value;
            if (spritePatches != null)
                foreach (var kvp in spritePatches)
                    GlobalSprites[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Merge a mod's NPC defs into the global registry. Last writer wins.
        /// </summary>
        public static void RegisterModNpcs(
            Dictionary<string, NpcDef> npcs,
            Dictionary<string, NpcDef> npcPatches)
        {
            if (npcs != null)
                foreach (var kvp in npcs)
                    GlobalNpcs[kvp.Key] = kvp.Value;
            if (npcPatches != null)
                foreach (var kvp in npcPatches)
                    GlobalNpcs[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Clear all global registries. Called before a full rebuild.
        /// </summary>
        public static void ClearAll()
        {
            LoadedZones.Clear();
            Cards.Clear(); Npcs.Clear(); Combats.Clear(); Events.Clear();
            Nodes.Clear(); Zones.Clear(); Items.Clear(); LootTables.Clear();
            GlobalSprites.Clear(); GlobalNpcs.Clear(); ZoneFolderMap.Clear();
            _backgroundSprites.Clear();
            NpcPrefabBuilder.ClearCache();
        }

        // ═══════════════════════════════════════════════════════════════
        //  PERSISTENT EDITOR
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create the persistent ZoneEditor GameObject if it doesn't already exist.
        /// Called after zones are loaded so the editor is available from any scene.
        /// </summary>
        private static void EnsureEditorExists()
        {
            if (ZoneEditor.Instance != null) return;
            var go = new GameObject("[UnknownMod] ZoneEditor");
            go.AddComponent<ZoneEditor>(); // Awake calls DontDestroyOnLoad
            Plugin.Log.LogInfo("[ZoneLoader] Created persistent ZoneEditor.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE DEFINITION RESOLUTION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolve the base-game NPC ID to clone for skeleton/animations.
        /// If SpriteSource points to a sprite definition, returns its BaseSprite.
        /// Otherwise returns SpriteSource directly as a base-game NPC ID.
        /// </summary>
        public static string ResolveBaseNpcId(ZoneDef zone, NpcDef npcDef)
        {
            if (zone != null && !string.IsNullOrEmpty(npcDef.SpriteSource) &&
                zone.Sprites.TryGetValue(npcDef.SpriteSource, out var spriteDef) &&
                !string.IsNullOrEmpty(spriteDef.BaseSprite))
            {
                return spriteDef.BaseSprite;
            }
            return npcDef.SpriteSource;
        }

        /// <summary>
        /// Resolve the SpriteOverrideDef for an NPC by checking its SpriteSource
        /// against the zone's Sprites dictionary.
        /// </summary>
        public static SpriteOverrideDef ResolveSpriteDefForNpc(ZoneDef zone, NpcDef npcDef)
        {
            if (zone != null && !string.IsNullOrEmpty(npcDef.SpriteSource) &&
                zone.Sprites.TryGetValue(npcDef.SpriteSource, out var spriteDef))
                return spriteDef;
            return null;
        }

        /// <summary>
        /// Strip variant suffixes from an NPC ID to get the base NPC ID.
        /// Used for runtime resolution where variant NPCs share their base's sprite.
        /// </summary>
        public static string StripVariantSuffix(string npcId)
        {
            foreach (string suffix in new[] { "_plus_b", "_plus", "_b" })
            {
                if (npcId.EndsWith(suffix))
                    return npcId.Substring(0, npcId.Length - suffix.Length);
            }
            return npcId;
        }

        // ═══════════════════════════════════════════════════════════════
        //  MAP BUILDING
        // ═══════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, Sprite> _backgroundSprites = new();
        private static string _nodeSortingLayer = "Default";
        private static int _nodeSortingLayerID = 0;

        public static bool IsModdedZone(string zoneId) => LoadedZones.ContainsKey(zoneId);

        public static Sprite GetBackgroundSprite(string zoneId)
        {
            if (_backgroundSprites.TryGetValue(zoneId, out var cached) && cached != null)
                return cached;

            if (!LoadedZones.TryGetValue(zoneId, out var zoneDef))
            {
                Plugin.Log.LogError($"[ZoneLoader] GetBackgroundSprite: zone '{zoneId}' not loaded!");
                return null;
            }

            string folder = GetZoneFolder(zoneId);
            string bgFile = Path.Combine(folder, zoneDef.BackgroundImage ?? "background.jpeg");

            if (!File.Exists(bgFile))
            {
                Plugin.Log.LogError($"[ZoneLoader] Background image not found: {bgFile}");
                return null;
            }

            byte[] data = File.ReadAllBytes(bgFile);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.filterMode = FilterMode.Bilinear;

            float ppuH = tex.width / 19.2f;
            float ppuV = tex.height / 10.8f;
            float ppu = Mathf.Min(ppuH, ppuV);

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), ppu);

            _backgroundSprites[zoneId] = sprite;
            return sprite;
        }

        public static bool BuildAndInjectMap(string zoneId, Transform worldTransform)
        {
            if (!LoadedZones.TryGetValue(zoneId, out var zoneDef))
            {
                Plugin.Log.LogError($"[ZoneLoader] BuildAndInjectMap: zone '{zoneId}' not loaded!");
                return false;
            }

            for (int i = 0; i < worldTransform.childCount; i++)
            {
                if (worldTransform.GetChild(i).gameObject.name == zoneId)
                    return false;
            }

            CurrentZone = zoneDef;

            GameObject nodeTemplate = FindNodeTemplate();
            if (nodeTemplate == null)
            {
                Plugin.Log.LogError("[ZoneLoader] Could not find a Node template!");
                return false;
            }

            var templateSRs = nodeTemplate.GetComponentsInChildren<SpriteRenderer>(true);
            if (templateSRs.Length > 0)
            {
                _nodeSortingLayer = templateSRs[0].sortingLayerName;
                _nodeSortingLayerID = templateSRs[0].sortingLayerID;
            }

            var root = new GameObject(zoneId);
            root.transform.SetParent(worldTransform, false);

            var bgSprite = GetBackgroundSprite(zoneId);
            if (bgSprite != null)
            {
                var bgGO = new GameObject("Background");
                bgGO.transform.SetParent(root.transform, false);
                bgGO.transform.localPosition = Vector3.zero;
                var sr = bgGO.AddComponent<SpriteRenderer>();
                sr.sprite = bgSprite;
                sr.color = Color.white;
                sr.sortingLayerName = _nodeSortingLayer;
                sr.sortingOrder = -10;
            }

            var nodesGO = new GameObject("Nodes");
            nodesGO.transform.SetParent(root.transform, false);
            nodesGO.transform.localPosition = new Vector3(0f, 0f, -2f);

            var nodePositions = GetNodePositions(zoneDef);
            foreach (var kvp in nodePositions)
            {
                var nodeGO = UnityEngine.Object.Instantiate(nodeTemplate, nodesGO.transform);
                nodeGO.name = kvp.Key;
                nodeGO.transform.localPosition = kvp.Value;
                nodeGO.transform.localScale = Vector3.one;
                nodeGO.SetActive(true);

                var nodeComp = nodeGO.GetComponent<Node>();
                if (nodeComp != null)
                    nodeComp.nodeData = Globals.Instance.GetNodeData(kvp.Key);
            }

            var roadsGO = new GameObject("Roads");
            roadsGO.transform.SetParent(root.transform, false);
            CreateMapRoads(roadsGO.transform, nodePositions, zoneDef);

            // Attach MapEditor to the zone map root (ZoneEditor is persistent)
            ZoneEditor.Instance?.AttachMapEditor(root);

            Plugin.Log.LogInfo($"[ZoneLoader] Map built: {zoneId} ({nodePositions.Count} nodes).");
            return true;
        }

        private static GameObject FindNodeTemplate()
        {
            var mapList = MapManager.Instance?.mapList;
            if (mapList != null)
            {
                for (int i = 0; i < mapList.Count; i++)
                {
                    if (mapList[i] == null) continue;
                    var nodesT = mapList[i].transform.Find("Nodes");
                    if (nodesT == null || nodesT.childCount == 0) continue;
                    var nodeComp = nodesT.GetChild(0).GetComponent<Node>();
                    if (nodeComp != null) return nodesT.GetChild(0).gameObject;
                }
            }

            var worldT = MapManager.Instance?.worldTransform;
            if (worldT != null)
            {
                foreach (Transform z in worldT)
                {
                    var nodesT = z.Find("Nodes");
                    if (nodesT == null || nodesT.childCount == 0) continue;
                    var nodeComp = nodesT.GetChild(0).GetComponent<Node>();
                    if (nodeComp != null) return nodesT.GetChild(0).gameObject;
                }
            }

            return null;
        }

        private static void CreateMapRoads(Transform roadsParent, Dictionary<string, Vector3> nodePositions, ZoneDef zoneDef)
        {
            Material roadMat = FindRoadMaterial() ?? new Material(Shader.Find("Sprites/Default"));

            foreach (var kvp in zoneDef.Roads)
            {
                var road = kvp.Value;
                if (!nodePositions.ContainsKey(road.FromNodeId) || !nodePositions.ContainsKey(road.ToNodeId))
                    continue;

                Vector3 posA = nodePositions[road.FromNodeId];
                Vector3 posB = nodePositions[road.ToNodeId];
                var waypoints = road.Waypoints.Select(wp => new Vector3(wp[0], wp[1], 0f)).ToArray();

                int total = 2 + waypoints.Length;
                var pts = new Vector3[total];
                pts[0] = posA;
                for (int i = 0; i < waypoints.Length; i++) pts[i + 1] = waypoints[i];
                pts[total - 1] = posB;

                var go = new GameObject(kvp.Key);
                go.transform.SetParent(roadsParent, false);
                go.SetActive(false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = total;
                for (int i = 0; i < total; i++) lr.SetPosition(i, pts[i]);
                lr.startWidth = 0.06f;
                lr.endWidth = 0.06f;
                lr.material = roadMat;
                lr.startColor = Color.white;
                lr.endColor = Color.white;
                lr.sortingLayerName = _nodeSortingLayer;
                lr.sortingOrder = -5;
            }
        }

        private static Material FindRoadMaterial()
        {
            var worldT = MapManager.Instance?.worldTransform;
            if (worldT == null) return null;
            foreach (Transform z in worldT)
            {
                var roadsT = z.Find("Roads");
                if (roadsT == null || roadsT.childCount == 0) continue;
                var lr = roadsT.GetChild(0).GetComponent<LineRenderer>();
                if (lr?.material != null) return lr.material;
            }
            return null;
        }

        private static Dictionary<string, Vector3> GetNodePositions(ZoneDef zoneDef)
        {
            var result = new Dictionary<string, Vector3>();
            if (zoneDef?.Nodes == null) return result;
            foreach (var kvp in zoneDef.Nodes)
                result[kvp.Key] = new Vector3(kvp.Value.PosX, kvp.Value.PosY, 0f);
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SAVE
        // ═══════════════════════════════════════════════════════════════

        public static void SaveCurrentZone()
        {
            if (CurrentZone == null) return;
            string folder = GetZoneFolder(CurrentZone.ZoneId);
            SaveToFolder(CurrentZone, folder);
            Plugin.Log.LogInfo($"[ZoneLoader] Saved zone to: {folder}");
        }
    }
}
