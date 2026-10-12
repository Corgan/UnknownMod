using System.Collections.Generic;
using System.Linq;
using UnknownMod.Core;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Id lists for reference pickers: entities defined in the active project (new + patched)
    /// followed by everything already registered in the game. Using these instead of the raw
    /// DataHelper.GetAll*Ids lists means a field can point at content that only exists in the mod being edited.
    /// Every method has a no-argument overload that uses the active project, and results are cached until
    /// the project's ids or the underlying game list change.
    /// </summary>
    public static class RefIds
    {
        private class Entry { public object GameRef; public int Signature; public List<string> Result; }
        private static readonly Dictionary<string, Entry> _cache = new();

        private static List<string> Merge(string key, ModProject p, IEnumerable<string> projectNew,
            IEnumerable<string> projectPatches, IEnumerable<string> game, object gameRef)
        {
            var newIds = projectNew?.ToList() ?? new List<string>();
            var patchIds = projectPatches?.ToList() ?? new List<string>();

            int sig = p == null ? 0 : p.ModId.GetHashCode();
            unchecked
            {
                foreach (var id in newIds) sig = sig * 31 + id.GetHashCode();
                sig = sig * 31 + 7;
                foreach (var id in patchIds) sig = sig * 31 + id.GetHashCode();
            }

            if (_cache.TryGetValue(key, out var e) && ReferenceEquals(e.GameRef, gameRef) && e.Signature == sig)
                return e.Result;

            var list = new List<string>();
            list.AddRange(newIds.OrderBy(k => k));
            list.AddRange(patchIds.OrderBy(k => k));
            if (game != null) list.AddRange(game);
            var result = list.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            _cache[key] = new Entry { GameRef = gameRef, Signature = sig, Result = result };
            return result;
        }

        private static List<string> Merge(string key, ModProject p, IEnumerable<string> projectNew,
            IEnumerable<string> projectPatches, List<string> game)
            => Merge(key, p, projectNew, projectPatches, game, game);

        private static ModProject Active => ModManagerPanel.ActiveProject;

        public static List<string> Cards(ModProject p) => Merge("cards", p, p?.Cards.Keys, p?.CardPatches.Keys, DataHelper.GetAllCardIds());
        public static List<string> Npcs(ModProject p) => Merge("npcs", p, p?.Npcs.Keys, p?.NpcPatches.Keys, DataHelper.GetAllNpcIds());
        public static List<string> Events(ModProject p) => Merge("events", p, p?.Events.Keys, p?.EventPatches.Keys, DataHelper.GetAllEventIds());
        public static List<string> Combats(ModProject p) => Merge("combats", p, p?.Combats.Keys, p?.CombatPatches.Keys, DataHelper.GetAllCombatIds());
        public static List<string> Loot(ModProject p) => Merge("loot", p, p?.Loot.Keys, p?.LootPatches.Keys, DataHelper.GetAllLootIds());
        public static List<string> SubClasses(ModProject p) => Merge("subclasses", p, p?.Heroes.Keys, p?.HeroPatches.Keys, DataHelper.GetAllSubClassIds());
        public static List<string> AuraCurses(ModProject p) => Merge("auracurses", p, p?.AuraCurses.Keys, p?.AuraCursePatches.Keys, DataHelper.GetAllAuraCurseIds());
        public static List<string> Traits(ModProject p) => Merge("traits", p, p?.Traits.Keys, p?.TraitPatches.Keys, DataHelper.GetAllTraitIds());
        public static List<string> Perks(ModProject p) => Merge("perks", p, p?.Perks.Keys, p?.PerkPatches.Keys, DataHelper.GetAllPerkIds());
        public static List<string> PerkNodes(ModProject p) => Merge("perknodes", p, p?.PerkNodes.Keys, p?.PerkNodePatches.Keys, DataHelper.GetAllPerkNodeIds());
        public static List<string> Skins(ModProject p) => Merge("skins", p, p?.Skins.Keys, p?.SkinPatches.Keys, DataHelper.GetAllSkinIds());
        public static List<string> Cardbacks(ModProject p) => Merge("cardbacks", p, p?.Cardbacks.Keys, p?.CardbackPatches.Keys, DataHelper.GetAllCardbackIds());
        public static List<string> Requirements(ModProject p) => Merge("requirements", p, p?.Requirements.Keys, p?.RequirementPatches.Keys, DataHelper.GetAllEventRequirementIds());
        public static List<string> Packs(ModProject p) => Merge("packs", p, p?.Packs.Keys, p?.PackPatches.Keys, DataHelper.GetAllPackIds());
        public static List<string> CardPlayerPacks(ModProject p) => Merge("cpp", p, p?.CardPlayerPacks.Keys, p?.CardPlayerPackPatches.Keys, DataHelper.GetAllCardPlayerPackIds());
        public static List<string> CardPlayerPairsPacks(ModProject p) => Merge("cppp", p, p?.CardPlayerPairsPacks.Keys, p?.CardPlayerPairsPackPatches.Keys, DataHelper.GetAllCardPlayerPairsPackIds());
        public static List<string> Thermometers(ModProject p) => Merge("thermometers", p, p?.Thermometers.Keys, null, DataHelper.GetAllThermometerIds());
        public static List<string> ThermometerTiers(ModProject p) => Merge("thermotiers", p, p?.ThermometerTiers.Keys, null, DataHelper.GetAllThermometerTierIds());
        public static List<string> KeyNotes(ModProject p) => Merge("keynotes", p, p?.KeyNotes.Keys, p?.KeyNotePatches.Keys, DataHelper.GetAllKeyNoteIds());
        public static List<string> Cinematics(ModProject p) => Merge("cinematics", p, p?.Cinematics.Keys, p?.CinematicPatches.Keys, DataHelper.GetAllCinematicIds());
        public static List<string> ChallengeTraits(ModProject p) => Merge("challengetraits", p, p?.ChallengeTraits.Keys, p?.ChallengeTraitPatches.Keys, DataHelper.GetAllChallengeTraitIds());
        public static List<string> CorruptionPacks(ModProject p) => Merge("corruptionpacks", p, p?.CorruptionPacks.Keys, p?.CorruptionPackPatches.Keys, DataHelper.GetAllCorruptionPackIds());
        public static List<string> HeroData(ModProject p) => Merge("herodata", p, p?.HeroDataEntries.Keys, p?.HeroDataPatches.Keys, DataHelper.GetAllHeroDataIds());

        public static List<string> Items(ModProject p)
            => Merge("items", p,
                p?.Cards.Values.Where(c => c.HasItemData).Select(c => c.Id),
                p?.CardPatches.Values.Where(c => c.HasItemData).Select(c => c.Id),
                DataHelper.GetAllItemIds());

        /// <summary>Tier numbers as strings: project tiers first, then base-game tiers.</summary>
        public static List<string> Tiers(ModProject p)
        {
            var tiers = DataHelper.GetAllTierRewardTiers();
            return Merge("tiers", p,
                p?.TierRewards.Values.Select(t => t.Tier.ToString()),
                p?.TierRewardPatches.Values.Select(t => t.Tier.ToString()),
                tiers.Select(t => t.ToString()), tiers);
        }

        public static List<string> Cards() => Cards(Active);
        public static List<string> Npcs() => Npcs(Active);
        public static List<string> Events() => Events(Active);
        public static List<string> Combats() => Combats(Active);
        public static List<string> Loot() => Loot(Active);
        public static List<string> SubClasses() => SubClasses(Active);
        public static List<string> AuraCurses() => AuraCurses(Active);
        public static List<string> Traits() => Traits(Active);
        public static List<string> Perks() => Perks(Active);
        public static List<string> PerkNodes() => PerkNodes(Active);
        public static List<string> Skins() => Skins(Active);
        public static List<string> Cardbacks() => Cardbacks(Active);
        public static List<string> Requirements() => Requirements(Active);
        public static List<string> Packs() => Packs(Active);
        public static List<string> CardPlayerPacks() => CardPlayerPacks(Active);
        public static List<string> CardPlayerPairsPacks() => CardPlayerPairsPacks(Active);
        public static List<string> KeyNotes() => KeyNotes(Active);
        public static List<string> Cinematics() => Cinematics(Active);
        public static List<string> ChallengeTraits() => ChallengeTraits(Active);
        public static List<string> CorruptionPacks() => CorruptionPacks(Active);
        public static List<string> HeroData() => HeroData(Active);
        public static List<string> Items() => Items(Active);
        public static List<string> Tiers() => Tiers(Active);
    }
}
