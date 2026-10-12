using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnknownMod.Definitions;
using UnknownMod.Runtime;

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
                    Plugin.Log.LogWarning($"[Builder] Failed to build {label} '{kvp.Key}': {ex}");
                }
            }
        }

        /// <summary>Finds an NPC def by id among a project's new NPCs and NPC patches, then among every loaded mod's NPCs.</summary>
        private static Func<string, NpcDef> NpcLookup(ModProject p) => id =>
            p.Npcs.TryGetValue(id, out var n) ? n
            : p.NpcPatches.TryGetValue(id, out n) ? n
            : ModRegistry.GlobalNpcs.TryGetValue(id, out n) ? n : null;

        /// <summary>
        /// A patch whose target is neither a base-game id nor a new entity of any loaded mod is built as brand-new content,
        /// which is almost always a typo or a removed/disabled dependency. Only warns; the entity is still registered.
        /// </summary>
        private static void WarnMissingPatchTargets(IList<ModProject> mods)
        {
            void Check<T>(string label, Func<ModProject, Dictionary<string, T>> patches, Func<ModProject, Dictionary<string, T>> news, IEnumerable<string> baseIds)
            {
                var known = new HashSet<string>(baseIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var m in mods) foreach (var id in news(m).Keys) known.Add(id);
                if (known.Count == 0) return;
                foreach (var m in mods)
                    foreach (var id in patches(m).Keys)
                        if (!known.Contains(id))
                            Plugin.Log.LogWarning($"[Builder] {m.ModId}: {label} patch '{id}' has no target (not a base-game id or a new {label} of any loaded mod); it is added as a new {label}.");
            }
            Check("AuraCurse", m => m.AuraCursePatches, m => m.AuraCurses, DataHelper.GetAllAuraCurseIds());
            Check("Card", m => m.CardPatches, m => m.Cards, DataHelper.GetAllCardIds());
            Check("Trait", m => m.TraitPatches, m => m.Traits, DataHelper.GetAllTraitIds());
            Check("Perk", m => m.PerkPatches, m => m.Perks, DataHelper.GetAllPerkIds());
            Check("NPC", m => m.NpcPatches, m => m.Npcs, DataHelper.GetAllNpcIds());
            Check("Skin", m => m.SkinPatches, m => m.Skins, DataHelper.GetAllSkinIds());
            Check("Pack", m => m.PackPatches, m => m.Packs, DataHelper.GetAllPackIds());
            Check("Loot", m => m.LootPatches, m => m.Loot, DataHelper.GetAllLootIds());
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
            // Thermometers are referenced by combats, so they build before everything else
            new() { Name = "Thermometers",
                BuildNew   = p => { BuildDict(p.Thermometers, DataHelper.MakeThermometer, DataHelper.RegisterThermometer, "Thermometer");
                                    BuildDict(p.ThermometerTiers, DataHelper.MakeThermometerTier, DataHelper.RegisterThermometerTier, "ThermometerTier"); },
                BuildPatch = _ => { },
            },
            // Town minigame pools are applied to the scene managers lazily (see Patches.Minigames)
            new() { Name = "MinigamePools",
                BuildNew   = p => DataHelper.RegisterMinigamePools(p),
                BuildPatch = _ => { },
            },
            // Level 0: leaf types (no cross-entity deps)
            new() { Name = "AuraCurses",
                BuildNew   = p => BuildDict(p.AuraCurses,      MakeAuraCurse, RegisterAuraCurse, "AuraCurse"),
                BuildPatch = p => BuildDict(p.AuraCursePatches, MakeAuraCurse, RegisterAuraCurse, "AuraCurse"),
            },
            new() { Name = "Requirements",
                BuildNew   = p => BuildDict(p.Requirements,       DataHelper.MakeRequirement, DataHelper.RegisterRequirement, "Requirement"),
                BuildPatch = p => BuildDict(p.RequirementPatches, DataHelper.MakeRequirement, DataHelper.RegisterRequirement, "Requirement"),
            },
            // KeyNotes are referenced by cards and traits, so they build before Cards
            new() { Name = "KeyNotes",
                BuildNew   = p => BuildDict(p.KeyNotes,       DataHelper.MakeKeyNote, DataHelper.RegisterKeyNote, "KeyNote"),
                BuildPatch = p => BuildDict(p.KeyNotePatches, DataHelper.MakeKeyNote, DataHelper.RegisterKeyNote, "KeyNote"),
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
                BuildNew   = p => BuildDict(p.Npcs,       d => DataHelper.MakeFullNpc(d, DataHelper.InheritedNpcPortrait(d, NpcLookup(p))), DataHelper.RegisterNPC, "NPC"),
                BuildPatch = p => BuildDict(p.NpcPatches, d => DataHelper.MakeFullNpc(d, DataHelper.InheritedNpcPortrait(d, NpcLookup(p))), DataHelper.RegisterNPC, "NPC"),
            },
            new() { Name = "NpcVariantRefs",
                BuildNew   = p => { foreach (var n in p.Npcs.Values) DataHelper.WireNpcVariantRefs(n); },
                BuildPatch = p => { foreach (var n in p.NpcPatches.Values) DataHelper.WireNpcVariantRefs(n); },
            },
            new() { Name = "Heroes",
                BuildNew   = p => BuildDict(p.Heroes,      DataHelper.MakeFullHero, DataHelper.RegisterHero, "Hero"),
                BuildPatch = p => BuildDict(p.HeroPatches, DataHelper.MakeFullHero, DataHelper.RegisterHero, "Hero"),
            },
            // Cardbacks reference a SubClass, so they build after Heroes
            new() { Name = "Cardbacks",
                BuildNew   = p => BuildDict(p.Cardbacks,       DataHelper.MakeCardback, DataHelper.RegisterCardback, "Cardback"),
                BuildPatch = p => BuildDict(p.CardbackPatches, DataHelper.MakeCardback, DataHelper.RegisterCardback, "Cardback"),
            },
            new() { Name = "PerkNodes",
                BuildNew   = p => BuildDict(p.PerkNodes,       DataHelper.MakePerkNode, DataHelper.RegisterPerkNode, "PerkNode"),
                BuildPatch = p => BuildDict(p.PerkNodePatches, DataHelper.MakePerkNode, DataHelper.RegisterPerkNode, "PerkNode"),
            },
            new() { Name = "PerkNodeRefs",
                BuildNew   = p => { foreach (var n in p.PerkNodes.Values) DataHelper.WirePerkNodeRefs(n); },
                BuildPatch = p => { foreach (var n in p.PerkNodePatches.Values) DataHelper.WirePerkNodeRefs(n); },
            },
            new() { Name = "Skins",
                BuildNew   = p => BuildDict(p.Skins,       DataHelper.MakeSkin, DataHelper.RegisterSkin, "Skin"),
                BuildPatch = p => BuildDict(p.SkinPatches, DataHelper.MakeSkin, DataHelper.RegisterSkin, "Skin"),
            },
            new() { Name = "WireVisualGraphPetModels",
                BuildNew   = p => WireVisualGraphPetModels(p, p.Cards),
                BuildPatch = p => WireVisualGraphPetModels(p, p.CardPatches),
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
            new() { Name = "HeroChallengePacks",
                BuildNew   = p => { foreach (var h in p.Heroes.Values) DataHelper.WireHeroChallengePacks(h); },
                BuildPatch = p => { foreach (var h in p.HeroPatches.Values) DataHelper.WireHeroChallengePacks(h); },
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
            new() { Name = "CorruptionPacks",
                BuildNew   = p => BuildDict(p.CorruptionPacks,       DataHelper.MakeCorruptionPack, DataHelper.RegisterCorruptionPack, "CorruptionPack"),
                BuildPatch = p => BuildDict(p.CorruptionPackPatches, DataHelper.MakeCorruptionPack, DataHelper.RegisterCorruptionPack, "CorruptionPack"),
            },
            new() { Name = "ChallengeTraits",
                BuildNew   = p => BuildDict(p.ChallengeTraits,       DataHelper.MakeChallengeTrait, DataHelper.RegisterChallengeTrait, "ChallengeTrait"),
                BuildPatch = p => BuildDict(p.ChallengeTraitPatches, DataHelper.MakeChallengeTrait, DataHelper.RegisterChallengeTrait, "ChallengeTrait"),
            },
            // Level 4 (Combats, Events), late extras (Cinematics, Weekly challenges) and Level 5 (Zones) handled outside Pipeline
        };

        // ---------------------------------------------------------------
        //  BUILD A SINGLE MOD
        // ---------------------------------------------------------------

        /// <summary>
        /// Build a single mod (editor hot-reload). Runs new+patch in sequence.
        /// For cross-mod dependency-ordered loading, use BuildAll().
        /// </summary>
        public static void Build(ModProject proj, bool loadScripts = true)
        {
            Plugin.Log.LogInfo($"[Builder] Building mod '{proj.ModId}'...");
            ModRegistry.RegisterModVisualGraphs(proj.VisualGraphs, proj.VisualGraphPatches);
            ModRegistry.RegisterVisualGraphReferences(proj.Npcs, proj.NpcPatches, proj.Skins, proj.SkinPatches, proj.Cards, proj.CardPatches);

            // Image sprites must exist before the pipeline so cards/auras can resolve them by name.
            ModRegistry.LoadModImageSprites(proj.ModId, ModProjectLoader.ModFolder(proj.ModId));
            AudioUtils.ReloadMod(proj.ModId, ModProjectLoader.ModFolder(proj.ModId));

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

            DataHelper.ResolveCardLists(proj.Cards.Values.Concat(proj.CardPatches.Values));

            // -- Sprites + NPC registry ---------------------------
            ModRegistry.RegisterModNpcs(proj.Npcs, proj.NpcPatches);
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
            var builtEvents = BuildEventsFirstPass(proj.Events, proj.ModId);
            var builtPatches = BuildEventsFirstPass(proj.EventPatches, proj.ModId);
            foreach (var kvp in builtPatches) builtEvents[kvp.Key] = kvp.Value;
            WireEventInterRefs(proj.Events, builtEvents, proj.ModId);
            WireEventInterRefs(proj.EventPatches, builtEvents, proj.ModId);
            InitEvents(builtEvents, proj.Events, proj.EventPatches);
            WireCombatEventLinks(proj.Combats);
            WireCombatEventLinks(proj.CombatPatches);

            BuildLateExtras(proj);

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

            Guard("Refresh replaced references", proj.ModId, RefreshReplacedRefs);

            // -- Scripts ------------------------------------------
            if (loadScripts) ScriptLoader.LoadModScripts(proj.ModId);

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
            DataHelper.PreflightGlobals();

            // Clear all registries before full rebuild
            ModRegistry.ClearAll();
            ScriptLoader.Clear();
            RuleEngine.Clear();
            EconomyEngine.Clear();
            AnimRetarget.Clear();
            Runtime.MapBuilder.ClearCache();
            DataHelper.ClearEntityCaches();
            DataHelper.ClearIdListCaches();

            Plugin.Log.LogInfo($"[Builder] Building {mods.Count} mod(s) — cross-mod, dependency-ordered...");

            foreach (var mod in mods)
                ModRegistry.RegisterModVisualGraphs(mod.VisualGraphs, mod.VisualGraphPatches);
            foreach (var mod in mods)
                ModRegistry.RegisterVisualGraphReferences(mod.Npcs, mod.NpcPatches, mod.Skins, mod.SkinPatches, mod.Cards, mod.CardPatches);

            // Image sprites must exist before the pipeline so cards/auras can resolve them by name.
            foreach (var mod in mods)
            {
                ModRegistry.LoadModImageSprites(mod.ModId, ModProjectLoader.ModFolder(mod.ModId));
                AudioUtils.LoadModAudio(mod.ModId, ModProjectLoader.ModFolder(mod.ModId));
            }

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
                    ModRegistry.RegisterModNpcs(mod.Npcs, null);

                    foreach (var bg in mod.Backgrounds.Values)
                        DataHelper.RegisterCustomBackground(bg);

                    BuildModCombats(mod.Combats);
                }
                catch (Exception ex) { Plugin.Log.LogError($"[Builder] New specials for '{mod.ModId}': {ex.Message}"); }
            }

            // Events (first pass across all mods, then second pass for inter-event refs)
            BuildEventStages(mods, m => m.Events, m => m.Combats, "New");

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

            Guard("Patch target check", "*", () => WarnMissingPatchTargets(mods));

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
                    ModRegistry.RegisterModNpcs(null, mod.NpcPatches);
                    foreach (var bg in mod.BackgroundPatches.Values)
                        DataHelper.RegisterCustomBackground(bg);
                    BuildModCombats(mod.CombatPatches);
                }
                catch (Exception ex) { Plugin.Log.LogError($"[Builder] Patch specials for '{mod.ModId}': {ex.Message}"); }
            }

            Guard("Resolve card lists", "*", () => DataHelper.ResolveCardLists(
                mods.SelectMany(mod => mod.Cards.Values)
                    .Concat(mods.SelectMany(mod => mod.CardPatches.Values))));

            // Event patches (same two-pass pattern)
            BuildEventStages(mods, m => m.EventPatches, m => m.CombatPatches, "Patch");

            foreach (var mod in mods)
                Guard("Late extras", mod.ModId, () => BuildLateExtras(mod));

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

            Guard("Refresh replaced references", "*", RefreshReplacedRefs);
            // ResetRuntimeState stripped minigame additions from managers that were already live; put the rebuilt pools back
            Guard("Reapply minigame pools", "*", DataHelper.ReapplyToLiveManagers);

            // ════════════════════════════════════════════════════════
            //  FINALIZE
            // ════════════════════════════════════════════════════════

            Guard("Invalidate sprite cache", "*", Editor.MapEditor.InvalidateSpriteNameCache);

            foreach (var mod in mods)
            {
                Guard("Scripts", mod.ModId, () => ScriptLoader.LoadModScripts(mod.ModId));
                if (!string.IsNullOrEmpty(mod.StarterNodeId))
                    ModRegistry.StarterNodeId = mod.StarterNodeId;
            }

            Plugin.Log.LogInfo("[Builder] All mods built.");
        }

        /// <summary>
        /// Event and combat patches register new objects under existing ids, so references wired earlier
        /// (reply outcomes, post-combat events, node events/combats) can still point at the replaced objects.
        /// Re-point every such reference at whatever is registered under that id now.
        /// </summary>
        internal static void RefreshReplacedRefs()
        {
            var g = Globals.Instance;
            if (g == null) return;
            var events = Traverse.Create(g).Field<Dictionary<string, EventData>>("_Events").Value;
            var combats = Traverse.Create(g).Field<Dictionary<string, CombatData>>("_CombatDataSource").Value;
            var nodes = Traverse.Create(g).Field<Dictionary<string, NodeData>>("_NodeDataSource").Value;
            int n = 0;

            // Globals instantiates clones of every vanilla asset, so vanilla references never equal the registered
            // objects. Only ids a mod registered are re-pointed; everything else must stay untouched.
            EventData CurE(EventData e) => e == null || string.IsNullOrEmpty(e.EventId) || !ModRegistry.IsTracked("_Events", DataHelper.NormalizeKey(e.EventId)) ? e : DataHelper.GetExistingEvent(e.EventId) ?? e;
            CombatData CurC(CombatData c) => c == null || string.IsNullOrEmpty(c.CombatId) || !ModRegistry.IsTracked("_CombatDataSource", DataHelper.NormalizeKey(c.CombatId)) ? c : DataHelper.GetExistingCombat(c.CombatId) ?? c;
            NodeData CurN(NodeData nd) => nd == null || string.IsNullOrEmpty(nd.NodeId) || !ModRegistry.IsTracked("_NodeDataSource", DataHelper.NormalizeKey(nd.NodeId)) ? nd : DataHelper.GetExistingNode(nd.NodeId) ?? nd;
            void FixN(NodeData old, Action<NodeData> set)
            {
                var cur = CurN(old);
                if (!ReferenceEquals(cur, old)) { set(cur); n++; }
            }
            void FixE(EventData old, Action<EventData> set)
            {
                var cur = CurE(old);
                if (!ReferenceEquals(cur, old)) { set(cur); n++; }
            }
            void FixC(CombatData old, Action<CombatData> set)
            {
                var cur = CurC(old);
                if (!ReferenceEquals(cur, old)) { set(cur); n++; }
            }

            if (events != null)
                foreach (var evt in events.Values.ToList())
                {
                    if (evt?.Replys == null) continue;
                    foreach (var r in evt.Replys)
                    {
                        if (r == null) continue;
                        var reply = r;
                        int before = n;
                        FixE(reply.SsEvent, v => reply.SsEvent = v);
                        FixE(reply.FlEvent, v => reply.FlEvent = v);
                        FixE(reply.SscEvent, v => reply.SscEvent = v);
                        FixE(reply.FlcEvent, v => reply.FlcEvent = v);
                        FixC(reply.SsCombat, v => reply.SsCombat = v);
                        FixC(reply.FlCombat, v => reply.FlCombat = v);
                        FixC(reply.SscCombat, v => reply.SscCombat = v);
                        FixC(reply.FlcCombat, v => reply.FlcCombat = v);
                        FixN(reply.SsNodeTravel, v => reply.SsNodeTravel = v);
                        FixN(reply.FlNodeTravel, v => reply.FlNodeTravel = v);
                        FixN(reply.SscNodeTravel, v => reply.SscNodeTravel = v);
                        FixN(reply.FlcNodeTravel, v => reply.FlcNodeTravel = v);
                        // The per-outcome data was snapshotted from these fields, so refresh it when one was re-pointed
                        if (n != before) reply.Init();
                    }
                }
            if (combats != null)
                foreach (var combat in combats.Values.ToList())
                    if (combat != null) { var cb = combat; FixE(cb.EventData, v => cb.EventData = v); }
            if (nodes != null)
                foreach (var node in nodes.Values.ToList())
                {
                    if (node == null) continue;
                    var nd = node;
                    if (nd.NodeEvent != null)
                        for (int i = 0; i < nd.NodeEvent.Length; i++) { int k = i; FixE(nd.NodeEvent[k], v => nd.NodeEvent[k] = v); }
                    if (nd.NodeCombat != null)
                        for (int i = 0; i < nd.NodeCombat.Length; i++) { int k = i; FixC(nd.NodeCombat[k], v => nd.NodeCombat[k] = v); }
                    FixE(nd.ReplacementEventIfSkuMissing, v => nd.ReplacementEventIfSkuMissing = v);
                    FixC(nd.ReplacementCombatIfSkuMissing, v => nd.ReplacementCombatIfSkuMissing = v);
                }
            if (n > 0) Plugin.Log.LogInfo($"[Builder] Re-pointed {n} event/combat reference(s) at patched replacements.");
        }

        /// <summary>
        /// Cinematics and weekly challenges reference combats, events, heroes and NPCs, so they build after
        /// those exist. Both define full objects, so new and patch entries use the same path.
        /// </summary>
        private static void BuildLateExtras(ModProject proj)
        {
            var cinematics = proj.Cinematics.Values.Concat(proj.CinematicPatches.Values).ToList();
            BuildDict(proj.Cinematics, DataHelper.MakeCinematic, DataHelper.RegisterCinematic, "Cinematic");
            BuildDict(proj.CinematicPatches, DataHelper.MakeCinematic, DataHelper.RegisterCinematic, "Cinematic");
            foreach (var c in cinematics)
                Guard("Wire cinematic " + c.Id, proj.ModId, () => DataHelper.WireCinematicRefs(c));
            BuildDict(proj.WeeklyChallenges, DataHelper.MakeWeekly, DataHelper.RegisterWeekly, "WeeklyChallenge");
            BuildDict(proj.WeeklyChallengePatches, DataHelper.MakeWeekly, DataHelper.RegisterWeekly, "WeeklyChallenge");
            RuleEngine.Register(proj);
            EconomyEngine.Register(proj);
            AnimRetarget.Register(proj);
        }

        /// <summary>Run one build stage; a failure is logged with its stack and never aborts later stages.</summary>
        private static void Guard(string stage, string modId, Action action)
        {
            // Logged before running so a native crash still leaves the stage name as the last line.
            Plugin.Log.LogInfo($"[Builder] stage: {stage} ({modId})");
            try { action(); }
            catch (Exception ex) { Plugin.Log.LogError($"[Builder] {stage} for '{modId}' failed: {ex}"); }
        }

        /// <summary>Event first pass, inter-event wiring, init and combat links - each isolated per mod.</summary>
        private static void BuildEventStages(
            List<ModProject> mods,
            Func<ModProject, Dictionary<string, EventDef>> events,
            Func<ModProject, Dictionary<string, CombatDef>> combats,
            string label)
        {
            var built = new Dictionary<string, Dictionary<string, EventData>>();
            foreach (var mod in mods)
            {
                built[mod.ModId] = new Dictionary<string, EventData>();
                Guard($"{label} events (build)", mod.ModId, () => built[mod.ModId] = BuildEventsFirstPass(events(mod), mod.ModId));
            }
            foreach (var mod in mods)
                Guard($"{label} events (wire)", mod.ModId, () => WireEventInterRefs(events(mod), built[mod.ModId], mod.ModId));
            foreach (var mod in mods)
                Guard($"{label} events (init)", mod.ModId, () => InitEvents(built[mod.ModId], events(mod)));
            foreach (var mod in mods)
                Guard($"{label} combat-event links", mod.ModId, () => WireCombatEventLinks(combats(mod)));
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
                if (!all.TryGetValue(v.BaseCardId, out var baseDef))
                {
                    if (DataHelper.GetCard(v.BaseCardId) == null)
                        Plugin.Log.LogWarning($"[Builder] Card '{v.Id}' has BaseCardId '{v.BaseCardId}' which does not exist (not in this mod or the base game).");
                    else
                        Plugin.Log.LogWarning($"[Builder] Card '{v.Id}' upgrades '{v.BaseCardId}', which is not defined or patched in this mod, so that card will not offer the upgrade. Add a card patch for '{v.BaseCardId}' to link it.");
                    continue;
                }
                switch (v.CardUpgraded)
                {
                    case Enums.CardUpgraded.A:
                        baseDef.UpgradesTo1 = v.Id;
                        break;
                    case Enums.CardUpgraded.B:
                        baseDef.UpgradesTo2 = v.Id;
                        break;
                    case Enums.CardUpgraded.Rare:
                        baseDef.UpgradesToRareId = v.Id;
                        break;
                }
            }
        }

        private static void WireVisualGraphPetModels(ModProject proj, Dictionary<string, CardDef> defs)
        {
            foreach (var kvp in defs)
            {
                var d = kvp.Value;
                if (string.IsNullOrEmpty(d.PetVisualGraphId)) continue;
                CompiledCharacterVisualGraph graph;
                if (!ModRegistry.GlobalCompiledVisualGraphs.TryGetValue(d.PetVisualGraphId, out graph)) continue;
                var card = DataHelper.GetCard(kvp.Key);
                var cardData = card == null ? null : Traverse.Create(card).Field("card").GetValue<CardDataNew>();
                if (cardData == null || cardData.PetData == null) continue;
                var prefab = VisualInstanceFactory.ResolveBasePresentationPrefab(graph.BasePresentation, cardData.PetData.PetModel);
                if (prefab != null)
                    cardData.PetData.PetModel = prefab;
                else
                    Plugin.Log.LogWarning($"[Builder] Card '{kvp.Key}' visual graph '{graph.Id}' base presentation '{graph.BasePresentation}' has no prefab.");
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
                    if (d == null)
                    {
                        Plugin.Log.LogWarning("[Builder] Skipping null Combat definition.");
                        continue;
                    }
                    string combatId = DataHelper.NormalizeKey(d.CombatId);
                    var npcArr = d.NpcIds
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => (id, npc: DataHelper.GetExistingNPC(id)))
                        .Where(x =>
                        {
                            if (x.npc == null)
                                Plugin.Log.LogWarning($"[Builder] Combat '{d.CombatId}': NPC '{x.id}' not found");
                            return x.npc != null;
                        })
                        .Select(x => x.npc)
                        .ToArray();
                    if (npcArr.Length == 0 && d.NpcIds.Count > 0)
                    {
                        Plugin.Log.LogError($"[Builder] Combat '{d.CombatId}' skipped: none of its NPCs could be resolved");
                        continue;
                    }
                    var combat = DataHelper.MakeCombat(d, npcArr);
                    DataHelper.RegisterCombat(combat);
                    // Only replace the mapping once the new combat is registered, so a skipped rebuild keeps the live one intact
                    DataHelper.CombatCustomBackgrounds.Remove(combatId);
                    string customBackgroundId = DataHelper.NormalizeBackgroundId(d.CustomBackgroundId);
                    if (!string.IsNullOrEmpty(customBackgroundId))
                        DataHelper.CombatCustomBackgrounds[combatId] = customBackgroundId;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Combat '{d?.CombatId ?? "<null>"}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Build EventData SOs from mod-level EventDefs (first pass).
        /// Returns built events so the second pass can wire inter-event refs.
        /// </summary>
        private static Dictionary<string, EventData> BuildEventsFirstPass(Dictionary<string, EventDef> events, string modId)
        {
            var built = new Dictionary<string, EventData>();
            foreach (var d in events.Values)
            {
                try
                {
                    if (d.AppendReplies)
                    {
                        var appended = AppendEventReplies(d, modId);
                        if (appended != null) built[d.EventId] = appended;
                        continue;
                    }
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
                    DataHelper.ApplyEventSpriteOverrides(evt, d);
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

        /// <summary>Replies added to existing events by AppendReplies patches, with the mod that added each.</summary>
        internal static readonly Dictionary<EventReplyData, string> AppendedReplyOwner = new();
        private const int AppendedReplyIndexBase = 1000;

        /// <summary>
        /// Registers a copy of the live event with this mod's replies added after the existing ones. The copy is
        /// what mods built later start from, so appends from several mods stack. ClearAll restores the original,
        /// and a rebuild of one mod first drops the replies that mod added before.
        /// </summary>
        private static EventData AppendEventReplies(EventDef d, string modId)
        {
            var target = DataHelper.GetExistingEvent(d.EventId);
            if (target == null)
            {
                Plugin.Log.LogWarning($"[Builder] {modId}: event '{d.EventId}' has AppendReplies but no such event exists; skipped.");
                return null;
            }
            var added = d.Replies.Select(r =>
                DataHelper.MakeReply(r,
                    getCombat: id => DataHelper.GetExistingCombat(id),
                    getEvent: id => DataHelper.GetExistingEvent(id),
                    getNode: id => DataHelper.GetExistingNode(id),
                    getLoot: id => DataHelper.GetLootData(id))).ToArray();
            var kept = (target.Replys ?? new EventReplyData[0])
                .Where(r => !(AppendedReplyOwner.TryGetValue(r, out var owner) && owner == modId)).ToArray();
            // The reply text and outcome text are looked up by "<event>_rp<index>"; an unset index is 0, which would show the
            // original event's first reply text. Indexes past the vanilla range have no translation, so the def's own text is used.
            for (int i = 0; i < added.Length; i++)
                added[i].IndexForAnswerTranslation = AppendedReplyIndexBase + kept.Length + i;
            var evt = UnityEngine.Object.Instantiate(target);
            evt.name = target.name;
            evt.Replys = kept.Concat(added).ToArray();
            foreach (var r in added) AppendedReplyOwner[r] = modId;
            DataHelper.RegisterEvent(evt);
            return evt;
        }

        /// <summary>
        /// Second pass: wire inter-event references in reply outcomes.
        /// Called after all events are built and globally registered.
        /// </summary>
        private static void WireEventInterRefs(Dictionary<string, EventDef> events, Dictionary<string, EventData> built, string modId)
        {
            foreach (var d in events.Values)
            {
                if (!built.TryGetValue(d.EventId, out var evt)) continue;
                var replies = evt.Replys;
                if (d.AppendReplies)
                    replies = replies.Where(r => AppendedReplyOwner.TryGetValue(r, out var owner) && owner == modId).ToArray();
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
        private static void InitEvents(Dictionary<string, EventData> built, params Dictionary<string, EventDef>[] defs)
        {
            foreach (var evt in built.Values)
            {
                // Appended-to events were already initialised, and expanding them again would repeat their repeat-replies,
                // but the replies this mod appended have never been initialised (that builds their per-outcome data).
                if (defs.Any(m => m.TryGetValue(evt.EventId, out var d) && d.AppendReplies))
                {
                    foreach (var r in evt.Replys)
                        if (r != null && AppendedReplyOwner.ContainsKey(r)) r.Init();
                    continue;
                }
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

    }
}
