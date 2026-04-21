using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Converts mod DTOs into game ScriptableObjects and registers them in Globals.Instance.
    /// Called after <see cref="ModProjectLoader"/> has populated a <see cref="ModProject"/>.
    /// </summary>
    public static partial class ModProjectBuilder
    {
        // ---------------------------------------------------------------
        //  GENERIC BUILD HELPER
        // ---------------------------------------------------------------

        /// <summary>
        /// Iterate a dictionary of defs, create ScriptableObjects, and register them.
        /// Replaces all the identical per-type Build* wrappers.
        /// </summary>
        private static void BuildDict<TDef, TSO>(
            Dictionary<string, TDef> defs,
            Func<TDef, TSO> make,
            Action<TSO> register,
            string label,
            Action<TDef, TSO> afterBuild = null)
            where TDef : IModEntity
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var so = make(kvp.Value);
                    register(so);
                    afterBuild?.Invoke(kvp.Value, so);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build {label} '{kvp.Key}': {ex.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        //  NAMED BUILD PIPELINE
        // ---------------------------------------------------------------

        private struct PipelineStep
        {
            public string Name;
            public Action<ModProject> BuildNew;
            public Action<ModProject> BuildPatch;
        }

        /// <summary>
        /// Ordered build steps in dependency order. Each step has separate new/patch
        /// actions so BuildAll can run all-mods-new, then all-mods-patches.
        /// Combats/Events are handled separately (need multi-pass wiring).
        /// </summary>
        private static readonly PipelineStep[] Pipeline =
        {
            // Level 0: leaf types (no cross-entity deps)
            new() { Name = "AuraCurses",
                BuildNew   = p => BuildDict(p.AuraCurses,      MakeAuraCurse, RegisterAuraCurse, "AuraCurse"),
                BuildPatch = p => BuildDict(p.AuraCursePatches, MakeAuraCurse, RegisterAuraCurse, "AuraCurse"),
            },
            new() { Name = "Requirements",
                BuildNew   = p => BuildDict(p.Requirements,       DataHelper.MakeRequirement, DataHelper.RegisterRequirement, "Requirement"),
                BuildPatch = p => BuildDict(p.RequirementPatches, DataHelper.MakeRequirement, DataHelper.RegisterRequirement, "Requirement"),
            },
            new() { Name = "Cardbacks",
                BuildNew   = p => BuildDict(p.Cardbacks,       DataHelper.MakeCardback, DataHelper.RegisterCardback, "Cardback"),
                BuildPatch = p => BuildDict(p.CardbackPatches, DataHelper.MakeCardback, DataHelper.RegisterCardback, "Cardback"),
            },
            // Level 1: depends on AuraCurses
            new() { Name = "WireUpgradePaths",
                BuildNew   = p => WireUpgradePaths(p),
                BuildPatch = p => { }, // already handled both dicts in BuildNew
            },
            new() { Name = "Cards",
                BuildNew   = p => BuildDict(p.Cards,       MakeFullCard, DataHelper.RegisterCard, "Card"),
                BuildPatch = p => BuildDict(p.CardPatches, MakeFullCard, DataHelper.RegisterCard, "Card"),
            },
            new() { Name = "WirePetModels",
                BuildNew   = p => WirePetModels(p, p.Cards),
                BuildPatch = p => WirePetModels(p, p.CardPatches),
            },
            new() { Name = "Traits",
                BuildNew   = p => BuildDict(p.Traits,      DataHelper.MakeTrait, DataHelper.RegisterTrait, "Trait"),
                BuildPatch = p => BuildDict(p.TraitPatches, DataHelper.MakeTrait, DataHelper.RegisterTrait, "Trait"),
            },
            new() { Name = "Perks",
                BuildNew   = p => BuildDict(p.Perks,       DataHelper.MakePerk, DataHelper.RegisterPerk, "Perk"),
                BuildPatch = p => BuildDict(p.PerkPatches, DataHelper.MakePerk, DataHelper.RegisterPerk, "Perk"),
            },
            // Level 2: depends on Cards, Traits, Perks, AuraCurses
            new() { Name = "Equipment",
                BuildNew   = p => BuildEquipmentCards(p.Cards),
                BuildPatch = p => BuildEquipmentCards(p.CardPatches),
            },
            new() { Name = "NPCs",
                BuildNew   = p => BuildDict(p.Npcs,       DataHelper.MakeFullNpc, DataHelper.RegisterNPC, "NPC"),
                BuildPatch = p => BuildDict(p.NpcPatches, DataHelper.MakeFullNpc, DataHelper.RegisterNPC, "NPC"),
            },
            new() { Name = "Heroes",
                BuildNew   = p => BuildDict(p.Heroes,      DataHelper.MakeFullHero, DataHelper.RegisterHero, "Hero"),
                BuildPatch = p => BuildDict(p.HeroPatches, DataHelper.MakeFullHero, DataHelper.RegisterHero, "Hero"),
            },
            new() { Name = "PerkNodes",
                BuildNew   = p => BuildDict(p.PerkNodes,       DataHelper.MakePerkNode, DataHelper.RegisterPerkNode, "PerkNode"),
                BuildPatch = p => BuildDict(p.PerkNodePatches, DataHelper.MakePerkNode, DataHelper.RegisterPerkNode, "PerkNode"),
            },
            new() { Name = "Skins",
                BuildNew   = p => BuildDict(p.Skins,       DataHelper.MakeSkin, DataHelper.RegisterSkin, "Skin",
                    (skinDef, skinData) => ApplySkinOverride(skinDef, skinData, p)),
                BuildPatch = p => BuildDict(p.SkinPatches, DataHelper.MakeSkin, DataHelper.RegisterSkin, "Skin",
                    (skinDef, skinData) => ApplySkinOverride(skinDef, skinData, p)),
            },
            new() { Name = "Packs",
                BuildNew   = p => BuildDict(p.Packs,       DataHelper.MakePack, DataHelper.RegisterPack, "Pack"),
                BuildPatch = p => BuildDict(p.PackPatches, DataHelper.MakePack, DataHelper.RegisterPack, "Pack"),
            },
            new() { Name = "CardPlayerPacks",
                BuildNew   = p => BuildDict(p.CardPlayerPacks,       DataHelper.MakeCardPlayerPack, DataHelper.RegisterCardPlayerPack, "CardPlayerPack"),
                BuildPatch = p => BuildDict(p.CardPlayerPackPatches, DataHelper.MakeCardPlayerPack, DataHelper.RegisterCardPlayerPack, "CardPlayerPack"),
            },
            new() { Name = "CardPlayerPairsPacks",
                BuildNew   = p => BuildDict(p.CardPlayerPairsPacks,       DataHelper.MakeCardPlayerPairsPack, DataHelper.RegisterCardPlayerPairsPack, "CardPlayerPairsPack"),
                BuildPatch = p => BuildDict(p.CardPlayerPairsPackPatches, DataHelper.MakeCardPlayerPairsPack, DataHelper.RegisterCardPlayerPairsPack, "CardPlayerPairsPack"),
            },
            new() { Name = "HeroData",
                BuildNew   = p => BuildDict(p.HeroDataEntries, DataHelper.MakeHeroData, DataHelper.RegisterHeroData, "HeroData"),
                BuildPatch = p => BuildDict(p.HeroDataPatches, DataHelper.MakeHeroData, DataHelper.RegisterHeroData, "HeroData"),
            },
            // Level 3: depends on Cards, Items
            new() { Name = "Loot",
                BuildNew   = p => BuildDict(p.Loot,        DataHelper.MakeLoot, DataHelper.RegisterLoot, "Loot"),
                BuildPatch = p => BuildDict(p.LootPatches, DataHelper.MakeLoot, DataHelper.RegisterLoot, "Loot"),
            },
            new() { Name = "TierRewards",
                BuildNew   = p => BuildDict(p.TierRewards,       DataHelper.MakeTierReward, DataHelper.RegisterTierReward, "TierReward"),
                BuildPatch = p => BuildDict(p.TierRewardPatches, DataHelper.MakeTierReward, DataHelper.RegisterTierReward, "TierReward"),
            },
            // Level 4 (Combats, Events) and Level 5 (Zones) handled outside Pipeline
        };

        // ---------------------------------------------------------------
        //  BUILD A SINGLE MOD
        // ---------------------------------------------------------------

        /// <summary>
        /// Build a single mod (editor hot-reload). Runs new+patch in sequence.
        /// For cross-mod dependency-ordered loading, use BuildAll().
        /// </summary>
        public static void Build(ModProject proj)
        {
            Plugin.Log.LogInfo($"[Builder] Building mod '{proj.ModId}'...");

            // -- Standard pipeline (new then patch per type) ------
            foreach (var step in Pipeline)
            {
                try
                {
                    step.BuildNew(proj);
                    step.BuildPatch(proj);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[Builder] Pipeline step '{step.Name}' failed: {ex.Message}");
                }
            }

            // -- Sprites + NPC registry ---------------------------
            ModRegistry.RegisterModSpriteSkins(proj.SpriteSkins, proj.SpriteSkinPatches);
            ModRegistry.RegisterModNpcs(proj.Npcs, proj.NpcPatches);
            ModRegistry.LoadModImageSprites(proj.ModId, ModProjectLoader.ModFolder(proj.ModId));
            Editor.MapEditor.InvalidateSpriteNameCache();

            // -- Backgrounds (mod-level) --------------------------
            foreach (var bg in proj.Backgrounds.Values)
                DataHelper.RegisterCustomBackground(bg);
            foreach (var bg in proj.BackgroundPatches.Values)
                DataHelper.RegisterCustomBackground(bg);

            // -- Combats (mod-level, new + patches) ---------------
            BuildModCombats(proj.Combats);
            BuildModCombats(proj.CombatPatches);

            // -- Events (mod-level, two-pass wiring) --------------
            var builtEvents = BuildEventsFirstPass(proj.Events);
            var builtPatches = BuildEventsFirstPass(proj.EventPatches);
            foreach (var kvp in builtPatches) builtEvents[kvp.Key] = kvp.Value;
            WireEventInterRefs(proj.Events, builtEvents);
            WireEventInterRefs(proj.EventPatches, builtEvents);
            InitEvents(builtEvents);
            WireCombatEventLinks(proj.Combats);
            WireCombatEventLinks(proj.CombatPatches);

            // -- Zones --------------------------------------------
            foreach (var zone in proj.Zones.Values)
            {
                BuildZone(zone);
                string zoneFolder = System.IO.Path.Combine(
                    ModProjectLoader.ModFolder(proj.ModId), "zones", zone.ZoneId);
                ModRegistry.RegisterModZone(zone, zoneFolder);
            }

            // -- Zone patches -------------------------------------
            foreach (var patch in proj.ZonePatches.Values)
                ApplyZonePatch(patch);

            // -- Scripts ------------------------------------------
            ScriptLoader.LoadModScripts(proj.ModId);

            Plugin.Log.LogInfo($"[Builder] Mod '{proj.ModId}' built successfully.");
        }

        // ---------------------------------------------------------------
        //  BUILD ALL MODS (runtime load-order sequence)
        // ---------------------------------------------------------------

        /// <summary>
        /// Build all mods with cross-mod dependency ordering.
        /// Pass 1: For each data type → all mods' NEW entities (in load order).
        /// Pass 2: For each data type → all mods' PATCHES (in load order).
        /// This lets Mod B patch Mod A's entities regardless of declared dependencies.
        /// </summary>
        public static void BuildAll(List<ModProject> mods)
        {
            // Clear all registries before full rebuild
            ModRegistry.ClearAll();
            ScriptLoader.Clear();
            Runtime.MapBuilder.ClearCache();
            DataHelper.ClearEntityCaches();
            DataHelper.ClearIdListCaches();

            Plugin.Log.LogInfo($"[Builder] Building {mods.Count} mod(s) — cross-mod, dependency-ordered...");

            // ════════════════════════════════════════════════════════
            //  PASS 1: NEW content — each type across all mods
            // ════════════════════════════════════════════════════════

            foreach (var step in Pipeline)
            {
                foreach (var mod in mods)
                {
                    try { step.BuildNew(mod); }
                    catch (Exception ex) { Plugin.Log.LogError($"[Builder] New {step.Name} for '{mod.ModId}': {ex.Message}"); }
                }
            }

            // Per-mod specials (sprites, images, backgrounds, combats)
            foreach (var mod in mods)
            {
                try
                {
                    ModRegistry.RegisterModSpriteSkins(mod.SpriteSkins, null);
                    ModRegistry.RegisterModNpcs(mod.Npcs, null);
                    ModRegistry.LoadModImageSprites(mod.ModId, ModProjectLoader.ModFolder(mod.ModId));

                    foreach (var bg in mod.Backgrounds.Values)
                        DataHelper.RegisterCustomBackground(bg);
                    foreach (var bg in mod.BackgroundPatches.Values)
                        DataHelper.RegisterCustomBackground(bg);

                    BuildModCombats(mod.Combats);
                }
                catch (Exception ex) { Plugin.Log.LogError($"[Builder] New specials for '{mod.ModId}': {ex.Message}"); }
            }

            // Events (first pass across all mods, then second pass for inter-event refs)
            var allNewEvents = new Dictionary<string, Dictionary<string, EventData>>();
            foreach (var mod in mods)
                allNewEvents[mod.ModId] = BuildEventsFirstPass(mod.Events);
            foreach (var mod in mods)
                WireEventInterRefs(mod.Events, allNewEvents[mod.ModId]);
            foreach (var mod in mods)
                InitEvents(allNewEvents[mod.ModId]);
            foreach (var mod in mods)
                WireCombatEventLinks(mod.Combats);

            // Zones (after all global entities exist)
            foreach (var mod in mods)
            {
                try
                {
                    foreach (var zone in mod.Zones.Values)
                    {
                        BuildZone(zone);
                        string zoneFolder = System.IO.Path.Combine(
                            ModProjectLoader.ModFolder(mod.ModId), "zones", zone.ZoneId);
                        ModRegistry.RegisterModZone(zone, zoneFolder);
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError($"[Builder] Zones for '{mod.ModId}': {ex.Message}"); }
            }

            // ════════════════════════════════════════════════════════
            //  PASS 2: PATCHES — each type across all mods
            // ════════════════════════════════════════════════════════

            foreach (var step in Pipeline)
            {
                foreach (var mod in mods)
                {
                    try { step.BuildPatch(mod); }
                    catch (Exception ex) { Plugin.Log.LogError($"[Builder] Patch {step.Name} for '{mod.ModId}': {ex.Message}"); }
                }
            }

            // Per-mod patch specials
            foreach (var mod in mods)
            {
                try
                {
                    ModRegistry.RegisterModSpriteSkins(null, mod.SpriteSkinPatches);
                    ModRegistry.RegisterModNpcs(null, mod.NpcPatches);
                    BuildModCombats(mod.CombatPatches);
                }
                catch (Exception ex) { Plugin.Log.LogError($"[Builder] Patch specials for '{mod.ModId}': {ex.Message}"); }
            }

            // Event patches (same two-pass pattern)
            var allPatchEvents = new Dictionary<string, Dictionary<string, EventData>>();
            foreach (var mod in mods)
                allPatchEvents[mod.ModId] = BuildEventsFirstPass(mod.EventPatches);
            foreach (var mod in mods)
                WireEventInterRefs(mod.EventPatches, allPatchEvents[mod.ModId]);
            foreach (var mod in mods)
                InitEvents(allPatchEvents[mod.ModId]);
            foreach (var mod in mods)
                WireCombatEventLinks(mod.CombatPatches);

            // Zone patches (after all patch entities exist)
            foreach (var mod in mods)
            {
                try
                {
                    foreach (var patch in mod.ZonePatches.Values)
                        ApplyZonePatch(patch);
                }
                catch (Exception ex) { Plugin.Log.LogError($"[Builder] Zone patches for '{mod.ModId}': {ex.Message}"); }
            }

            // ════════════════════════════════════════════════════════
            //  FINALIZE
            // ════════════════════════════════════════════════════════

            Editor.MapEditor.InvalidateSpriteNameCache();

            foreach (var mod in mods)
            {
                ScriptLoader.LoadModScripts(mod.ModId);
                if (!string.IsNullOrEmpty(mod.StarterNodeId))
                    ModRegistry.StarterNodeId = mod.StarterNodeId;
            }

            Plugin.Log.LogInfo("[Builder] All mods built.");
        }

        // ---------------------------------------------------------------
        //  AUTO-WIRE UPGRADE PATHS (populates UpgradesTo1/2, UpgradedFrom, BaseCard)
        // ---------------------------------------------------------------

        private static void WireUpgradePaths(ModProject proj)
        {
            // Merge both dicts so cross-dict wiring works
            // (e.g., new A variant whose BaseCardId is a patched base card)
            var all = new Dictionary<string, CardDef>(proj.Cards, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in proj.CardPatches)
                all[kvp.Key] = kvp.Value;

            foreach (var kvp in all)
            {
                var v = kvp.Value;
                if (v.CardUpgraded == Enums.CardUpgraded.No)
                {
                    v.BaseCard = kvp.Key;   // convention: base card's BaseCard = self
                    // Preserve original game upgrade paths so patched base cards
                    // don't lose their A/B/Rare links (overwritten below if mod has variants)
                    var gameCard = DataHelper.GetCard(kvp.Key);
                    if (gameCard != null)
                    {
                        if (string.IsNullOrEmpty(v.UpgradesTo1))
                            v.UpgradesTo1 = gameCard.UpgradesTo1 ?? "";
                        if (string.IsNullOrEmpty(v.UpgradesTo2))
                            v.UpgradesTo2 = gameCard.UpgradesTo2 ?? "";
                        if (string.IsNullOrEmpty(v.UpgradesToRareId))
                            v.UpgradesToRareId = gameCard.UpgradesToRare != null ? gameCard.UpgradesToRare.Id ?? "" : "";
                    }
                    continue;
                }
                if (string.IsNullOrEmpty(v.BaseCardId)) continue;

                // Wire variant → base
                v.UpgradedFrom = v.BaseCardId;
                v.BaseCard = v.BaseCardId;

                // Wire base → variant
                if (!all.TryGetValue(v.BaseCardId, out var baseDef)) continue;
                switch (v.CardUpgraded)
                {
                    case Enums.CardUpgraded.A:    baseDef.UpgradesTo1 = v.Id; break;
                    case Enums.CardUpgraded.B:    baseDef.UpgradesTo2 = v.Id; break;
                    case Enums.CardUpgraded.Rare: baseDef.UpgradesToRareId = v.Id; break;
                }
            }
        }

        // ---------------------------------------------------------------
        //  WIRE PET MODELS FROM SPRITESKINS
        // ---------------------------------------------------------------

        /// <summary>
        /// For cards with PetModelSource referencing a project SpriteSkin,
        /// build the pet model via NpcPrefabBuilder.BuildCustomPrefab.
        /// </summary>
        private static void WirePetModels(ModProject proj, Dictionary<string, CardDef> defs)
        {
            foreach (var kvp in defs)
            {
                var d = kvp.Value;
                if (string.IsNullOrEmpty(d.PetModelSource)) continue;
                // Skip if already handled by CopyPetModel (source is a game card)
                if (DataHelper.GetCard(d.PetModelSource) != null) continue;

                // Look up SpriteSkin from project
                CharacterOverrideDef overrideDef = null;
                if (!proj.SpriteSkins.TryGetValue(d.PetModelSource, out overrideDef))
                    proj.SpriteSkinPatches.TryGetValue(d.PetModelSource, out overrideDef);
                if (overrideDef == null) continue;

                // Resolve the base NPC prefab from the SpriteSkin's BaseSprite
                var baseNpc = DataHelper.GetExistingNPC(overrideDef.BaseSprite);
                if (baseNpc == null || baseNpc.GameObjectAnimated == null)
                {
                    Plugin.Log.LogWarning($"[Builder] Pet model for '{kvp.Key}': SpriteSkin '{d.PetModelSource}' base NPC '{overrideDef.BaseSprite}' not found or has no prefab.");
                    continue;
                }

                try
                {
                    var petPrefab = Runtime.NpcPrefabBuilder.BuildCustomPrefab(
                        kvp.Key + "_pet", baseNpc.GameObjectAnimated, overrideDef, proj.ModId);
                    var card = DataHelper.GetCard(kvp.Key);
                    if (card != null)
                    {
                        var prefab = petPrefab ?? baseNpc.GameObjectAnimated;
                        Traverse.Create(card).Field("petModel").SetValue(prefab);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build pet model for '{kvp.Key}' from SpriteSkin '{d.PetModelSource}': {ex.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        //  EQUIPMENT CARD BUILDER (CardDef with ItemFields → ItemData + CardData)
        // ---------------------------------------------------------------

        private static void BuildEquipmentCards(Dictionary<string, CardDef> defs)
        {
            foreach (var kvp in defs)
            {
                if (!kvp.Value.HasItemData) continue;
                try
                {
                    var itemDef = kvp.Value.ToItemDef();
                    var so = DataHelper.MakeFullItem(itemDef);
                    DataHelper.RegisterItem(so);
                    var card = DataHelper.MakeItemCard(itemDef, so);
                    DataHelper.RegisterCard(card);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build equipment card '{kvp.Key}': {ex.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        //  MOD-LEVEL COMBAT / EVENT BUILDERS
        // ---------------------------------------------------------------

        /// <summary>Build and register CombatData SOs from mod-level CombatDefs.</summary>
        private static void BuildModCombats(Dictionary<string, CombatDef> combats)
        {
            foreach (var d in combats.Values)
            {
                try
                {
                    var npcArr = d.NpcIds
                        .Select(id => DataHelper.GetExistingNPC(id))
                        .Where(x => x != null)
                        .ToArray();
                    var combat = DataHelper.MakeCombat(d, npcArr);
                    DataHelper.RegisterCombat(combat);
                    if (!string.IsNullOrEmpty(d.CustomBackgroundId))
                        DataHelper.CombatCustomBackgrounds[d.CombatId] = d.CustomBackgroundId;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Combat '{d.CombatId}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Build EventData SOs from mod-level EventDefs (first pass).
        /// Returns built events so the second pass can wire inter-event refs.
        /// </summary>
        private static Dictionary<string, EventData> BuildEventsFirstPass(Dictionary<string, EventDef> events)
        {
            var built = new Dictionary<string, EventData>();
            foreach (var d in events.Values)
            {
                try
                {
                    var replies = d.Replies.Select(r =>
                        DataHelper.MakeReply(r,
                            getCombat: id => DataHelper.GetExistingCombat(id),
                            getEvent: id => DataHelper.GetExistingEvent(id),
                            getNode: id => DataHelper.GetExistingNode(id),
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
                    if (d.HistoryMode)
                        evt.HistoryMode = true;
                    if (!string.IsNullOrEmpty(d.EventUniqueId))
                        evt.EventUniqueId = d.EventUniqueId;
                    if (!string.IsNullOrEmpty(d.SpriteSource))
                        DataHelper.CopyEventVisuals(evt, d.SpriteSource);
                    // Set after CopyEventVisuals so the def's explicit value wins over the source's
                    if (d.EventIconShader != Enums.MapIconShader.None)
                        evt.EventIconShader = d.EventIconShader;
                    built[d.EventId] = evt;
                    DataHelper.RegisterEvent(evt);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Event '{d.EventId}': {ex.Message}");
                }
            }
            return built;
        }

        /// <summary>
        /// Second pass: wire inter-event references in reply outcomes.
        /// Called after all events are built and globally registered.
        /// </summary>
        private static void WireEventInterRefs(Dictionary<string, EventDef> events, Dictionary<string, EventData> built)
        {
            foreach (var d in events.Values)
            {
                if (!built.TryGetValue(d.EventId, out var evt)) continue;
                var replies = evt.Replys;
                for (int i = 0; i < d.Replies.Count && i < replies.Length; i++)
                {
                    var r = d.Replies[i];
                    var reply = replies[i];
                    if (reply.SsEvent == null && !string.IsNullOrEmpty(r.Ss.EventId))
                        reply.SsEvent = DataHelper.GetExistingEvent(r.Ss.EventId);
                    if (reply.FlEvent == null && !string.IsNullOrEmpty(r.Fl.EventId))
                        reply.FlEvent = DataHelper.GetExistingEvent(r.Fl.EventId);
                    if (reply.SscEvent == null && !string.IsNullOrEmpty(r.Ssc.EventId))
                        reply.SscEvent = DataHelper.GetExistingEvent(r.Ssc.EventId);
                    if (reply.FlcEvent == null && !string.IsNullOrEmpty(r.Flc.EventId))
                        reply.FlcEvent = DataHelper.GetExistingEvent(r.Flc.EventId);
                }
            }
        }

        /// <summary>
        /// Call EventData.Init() on all built events to expand RepeatForAll* replies.
        /// Must be called after WireEventInterRefs so all replies are fully wired.
        /// </summary>
        private static void InitEvents(Dictionary<string, EventData> built)
        {
            foreach (var evt in built.Values)
            {
                try { evt.Init(); }
                catch (Exception ex)
                {
                    Plugin.Log.LogDebug($"[Builder] EventData.Init() failed for '{evt.EventId}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Wire CombatData.EventData for post-combat events.
        /// Called after all events are globally registered.
        /// </summary>
        private static void WireCombatEventLinks(Dictionary<string, CombatDef> combats)
        {
            foreach (var d in combats.Values)
            {
                if (string.IsNullOrEmpty(d.EventDataId)) continue;
                var combat = DataHelper.GetExistingCombat(d.CombatId);
                if (combat == null) continue;
                var pe = DataHelper.GetExistingEvent(d.EventDataId);
                if (pe != null) combat.EventData = pe;
            }
        }

        // ---------------------------------------------------------------
        //  SKIN SPRITE OVERRIDE (afterBuild callback)
        // ---------------------------------------------------------------

        /// <summary>
        /// After a SkinData is built and registered, check if its SkinDef has a
        /// OverrideId. If so, resolve the CharacterOverrideDef from the project's
        /// Sprites dict, build a custom prefab for the SkinGo, and register the
        /// override for runtime resolution in HeroItem.Init.
        /// </summary>
        /// <summary>
        /// Public entry point for hot-reload: apply skin override to an already-built SkinData.
        /// </summary>
        public static void ApplySkinOverridePublic(SkinDef skinDef, SkinData skinData, ModProject proj)
            => ApplySkinOverride(skinDef, skinData, proj);

        private static void ApplySkinOverride(SkinDef skinDef, SkinData skinData, ModProject proj)
        {
            if (string.IsNullOrEmpty(skinDef.OverrideId)) return;

            // Resolve CharacterOverrideDef from project's Sprites (new + patches)
            CharacterOverrideDef overrideDef = null;
            if (proj.SpriteSkins.TryGetValue(skinDef.OverrideId, out overrideDef)) { }
            else if (proj.SpriteSkinPatches.TryGetValue(skinDef.OverrideId, out overrideDef)) { }

            if (overrideDef == null)
            {
                Plugin.Log.LogWarning($"[Builder] Skin '{skinDef.Id}' references OverrideId '{skinDef.OverrideId}' " +
                    "but no matching CharacterOverrideDef was found in project Sprites.");
                return;
            }

            // Build custom prefab if the skin has a SkinGo to modify
            if (skinData.SkinGo != null)
            {
                Runtime.NpcPrefabBuilder.InvalidateCache(skinDef.Id);
                var customPrefab = Runtime.NpcPrefabBuilder.BuildCustomPrefab(
                    skinDef.Id, skinData.SkinGo, overrideDef, proj.ModId);
                if (customPrefab != null)
                    skinData.SkinGo = customPrefab;
            }

            // Register for runtime resolution (HeroItem.Init postfix)
            ModRegistry.RegisterSkinOverride(skinDef.Id, overrideDef);
        }

    }
}
