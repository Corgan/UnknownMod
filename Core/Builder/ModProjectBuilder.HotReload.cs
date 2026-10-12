using System;
using System.Collections.Generic;
using System.Linq;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>What a hot reload did: how many entities were rebuilt in the running game, and what could not be.</summary>
    public sealed class ReloadReport
    {
        public int Reloaded;
        public List<string> Errors = new();
        public List<string> Notes = new();
        public bool HasErrors => Errors.Count > 0;
    }

    public static partial class ModProjectBuilder
    {
        /// <summary>
        /// Rebuilds just these entities in the running game, in dependency order, so an edit shows up without reloading the whole mod.
        /// Entities removed from a mod cannot be unregistered here; they stay until the next full rebuild.
        /// </summary>
        public static ReloadReport ReloadEntities(ModProject proj, IEnumerable<object> defs)
        {
            var report = new ReloadReport();
            var all = defs?.Where(d => d != null).Distinct().ToList() ?? new List<object>();
            if (all.Count == 0) return report;

            List<T> Of<T>() => all.OfType<T>().ToList();
            void Each<T>(string label, Action<T> act)
            {
                foreach (var d in Of<T>())
                {
                    try { act(d); report.Reloaded++; }
                    catch (Exception ex)
                    {
                        report.Errors.Add($"{label} '{IdOf(d)}': {ex.Message}");
                        Plugin.Log.LogWarning($"[HotReload] {label} '{IdOf(d)}' failed: {ex}");
                    }
                }
            }
            void Step(string label, Action act)
            {
                try { act(); }
                catch (Exception ex)
                {
                    report.Errors.Add($"{label}: {ex.Message}");
                    Plugin.Log.LogWarning($"[HotReload] {label} failed: {ex}");
                }
            }

            if (all.Any(d => d is CharacterVisualDef))
                Step("Visual graphs", () =>
                {
                    ModRegistry.RegisterModVisualGraphs(proj.VisualGraphs, proj.VisualGraphPatches);
                    ModRegistry.RegisterVisualGraphReferences(proj.Npcs, proj.NpcPatches, proj.Skins, proj.SkinPatches, proj.Cards, proj.CardPatches);
                    report.Reloaded += Of<CharacterVisualDef>().Count;
                });

            Each<AuraCurseDef>("Aura/curse", d => RegisterAuraCurse(MakeAuraCurse(d)));
            Each<RequirementDef>("Requirement", d => DataHelper.RegisterRequirement(DataHelper.MakeRequirement(d)));
            Each<KeyNoteDef>("Key note", d => DataHelper.RegisterKeyNote(DataHelper.MakeKeyNote(d)));
            Each<ThermometerDef>("Thermometer", d => DataHelper.RegisterThermometer(DataHelper.MakeThermometer(d)));
            Each<ThermometerTierDef>("Heat tier", d => DataHelper.RegisterThermometerTier(DataHelper.MakeThermometerTier(d)));

            var cards = Of<CardDef>();
            if (cards.Count > 0)
            {
                Step("Upgrade paths", () => WireUpgradePaths(proj));
                Each<CardDef>("Card", d =>
                {
                    if (d.HasItemData)
                    {
                        var itemDef = d.ToItemDef();
                        var item = DataHelper.MakeFullItem(itemDef);
                        DataHelper.RegisterItem(item);
                        DataHelper.RegisterCard(DataHelper.MakeItemCard(itemDef, item));
                    }
                    else DataHelper.RegisterCard(MakeFullCard(d));
                });
                Step("Card lists", () => DataHelper.ResolveCardLists(cards));
                Step("Pet models", () => WireVisualGraphPetModels(proj, cards.ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase)));
            }

            Each<TraitDef>("Trait", d => DataHelper.RegisterTrait(DataHelper.MakeTrait(d)));
            Each<PerkDef>("Perk", d => DataHelper.RegisterPerk(DataHelper.MakePerk(d)));
            Each<NpcDef>("NPC", d =>
            {
                DataHelper.RegisterNPC(DataHelper.MakeFullNpc(d, DataHelper.InheritedNpcPortrait(d, NpcLookup(proj))));
                DataHelper.WireNpcVariantRefs(d);
                ModRegistry.GlobalNpcs[d.Id] = d;
            });
            Each<HeroDef>("Hero", d => { DataHelper.RegisterHero(DataHelper.MakeFullHero(d)); DataHelper.WireHeroChallengePacks(d); });
            Each<CardbackDef>("Cardback", d => DataHelper.RegisterCardback(DataHelper.MakeCardback(d)));
            Each<PerkNodeDef>("Perk node", d => { DataHelper.RegisterPerkNode(DataHelper.MakePerkNode(d)); DataHelper.WirePerkNodeRefs(d); });
            Each<SkinDef>("Skin", d => DataHelper.RegisterSkin(DataHelper.MakeSkin(d)));
            Each<PackDef>("Pack", d => DataHelper.RegisterPack(DataHelper.MakePack(d)));
            Each<CardPlayerPackDef>("Card player pack", d => DataHelper.RegisterCardPlayerPack(DataHelper.MakeCardPlayerPack(d)));
            Each<CardPlayerPairsPackDef>("Card pairs pack", d => DataHelper.RegisterCardPlayerPairsPack(DataHelper.MakeCardPlayerPairsPack(d)));
            Each<HeroDataDef>("Hero data", d => DataHelper.RegisterHeroData(DataHelper.MakeHeroData(d)));
            Each<LootDef>("Loot", d => DataHelper.RegisterLoot(DataHelper.MakeLoot(d)));
            Each<TierRewardDef>("Tier reward", d => DataHelper.RegisterTierReward(DataHelper.MakeTierReward(d)));
            Each<CorruptionPackDef>("Corruption pack", d => DataHelper.RegisterCorruptionPack(DataHelper.MakeCorruptionPack(d)));
            Each<ChallengeTraitDef>("Challenge trait", d => DataHelper.RegisterChallengeTrait(DataHelper.MakeChallengeTrait(d)));
            Each<BackgroundDef>("Background", d => DataHelper.RegisterCustomBackground(d));

            var combats = Of<CombatDef>();
            if (combats.Count > 0)
            {
                var dict = combats.GroupBy(c => c.CombatId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
                Step("Combats", () => { BuildModCombats(dict); report.Reloaded += dict.Count; });
            }

            var events = Of<EventDef>();
            if (events.Count > 0)
            {
                var dict = events.GroupBy(e => e.EventId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
                Step("Events", () =>
                {
                    var built = BuildEventsFirstPass(dict, proj.ModId);
                    WireEventInterRefs(dict, built, proj.ModId);
                    InitEvents(built, dict);
                    report.Reloaded += built.Count;
                });
            }
            if (combats.Count > 0 || events.Count > 0)
            {
                var linkDict = combats.GroupBy(c => c.CombatId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
                Step("Combat/event links", () => WireCombatEventLinks(linkDict));
            }

            Each<CinematicDef>("Cinematic", d => { DataHelper.RegisterCinematic(DataHelper.MakeCinematic(d)); DataHelper.WireCinematicRefs(d); });
            Each<WeeklyChallengeDef>("Weekly challenge", d => DataHelper.RegisterWeekly(DataHelper.MakeWeekly(d)));

            if (all.Any(d => d is BettingTeamDef || d is MemorySequenceDef || d is CardPairRewardDef))
                Step("Minigame pools", () =>
                {
                    DataHelper.RefreshMinigamePools(proj);
                    report.Reloaded += all.Count(d => d is BettingTeamDef || d is MemorySequenceDef || d is CardPairRewardDef);
                });

            if (all.Any(d => d is RuleSetDef))
                Step("Rule sets", () =>
                {
                    RuleEngine.Register(proj);
                    report.Reloaded += all.Count(d => d is RuleSetDef);
                });

            if (all.Any(d => d is EconomyDef))
                Step("Economies", () =>
                {
                    EconomyEngine.Register(proj);
                    report.Reloaded += all.Count(d => d is EconomyDef);
                });

            Runtime.AnimRetarget.Register(proj);

            Each<ZoneDef>("Zone", d =>
            {
                BuildZone(d);
                ModRegistry.RegisterModZone(d, System.IO.Path.Combine(ModProjectLoader.ModFolder(proj.ModId), "zones", d.ZoneId));
            });
            Each<ZonePatchDef>("Zone patch", ApplyZonePatch);

            if (combats.Count > 0 || events.Count > 0 || all.Any(d => d is ZoneDef || d is ZonePatchDef))
                Step("Refresh replaced references", RefreshReplacedRefs);

            DataHelper.ClearEntityCaches();
            DataHelper.ClearIdListCaches();
            Editor.MapEditor.InvalidateSpriteNameCache();
            return report;
        }

        private static string IdOf(object def) => def switch
        {
            CardDef c => c.Id,
            AuraCurseDef a => a.Id,
            NpcDef n => n.Id,
            HeroDef h => h.Id,
            EventDef e => e.EventId,
            CombatDef c => c.CombatId,
            ZoneDef z => z.ZoneId,
            ZonePatchDef zp => zp.TargetZoneId,
            LootDef l => l.Id,
            RuleSetDef rs => rs.Id,
            EconomyDef ec => ec.Id,
            TraitDef t => t.Id,
            _ => def?.GetType().Name ?? "?"
        };
    }
}
