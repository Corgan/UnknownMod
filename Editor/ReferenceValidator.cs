using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Finds ids in a project that point at nothing. The id universe is the project plus everything registered
    /// in the game, so run it after a build when other mods' content is already registered.
    /// Only fields whose meaning is unambiguous are checked.
    /// </summary>
    public static class ReferenceValidator
    {
        public enum Kind { Card, Npc, Combat, Event, AuraCurse, Loot, SubClass, Requirement, Pack, Perk, Cinematic,
            ChallengeTrait, Tier, Skin, CardPlayerPack, CardPlayerPairsPack, VisualGraph, Audio, Sprite, Vfx }

        private static readonly (Regex pattern, Kind kind)[] Rules =
        {
            (R(@"^(BaseCardId|CardId|CardIds|AddCard\dId|AddCardId|UpgradesTo1|UpgradesTo2|UpgradesToRareId|UpgradedFrom|RelatedCard\d?|CardToGain|TraitCard|Trait\d[AB]Card|TraitCardForAllHeroes|SpecialCardIds|RequirementCardIds|CardsSingularity|HighPackCardIds|LowPackCardIds|CorruptionCardIds|CombatBasedCorruptionCardId|ReplyShowCardId|BaseItemId|ItemId|AddItemId|RequirementItemId|RequirementItemIds)$"), Kind.Card),
            (R(@"^(NpcId|NpcIds|BaseNpcId|UpgradedMobId|NgPlusMobId|HellModeMobId|SummonUnitId|NpcToSummonOnKilledId|Boss1Id|Boss2Id)$"), Kind.Npc),
            (R(@"^(CombatId|CombatIds|BossCombatId|ReplacementCombatId)$"), Kind.Combat),
            (R(@"^(EventId|EventIds|ReplacementEventId|EventDataId)$"), Kind.Event),
            (R(@"^(Aura\d?|Curse\d?|SummonAura\d?|AuraSelf\d?|CurseSelf\d?|AuracurseGain(Self)?\d|AuracurseImmune\d|AuracurseHeal\d|AuracurseBonus\d?|AuraCurseSetted\d?|RemoveAuraCurse\d?|PreventedAuraCurse|AuracurseCastIf|AuracurseCustomAC|HealAuraCurseName\d?|HealAuraCurseSelf|GainAuraCurseConsumption\d?|GainChargesFromThisAuraCurse\d?|AuraCurseId|BasedOnACId|ACOnExplode|Immunities)$"), Kind.AuraCurse),
            (R(@"^LootId$"), Kind.Loot),
            (R(@"^(Hero\dId|HeroSubClassId|CardbackSubclass|SkinSubclass|HeroToDisableNodeWhenUnlockedId)$"), Kind.SubClass),
            (R(@"^(RequirementId|EventRequirementId|NodeRequirementId|RequirementBlockedId|RequirementLockId|RequirementLock2Id|RequirementUnlockId|RequirementUnlock2Id)$"), Kind.Requirement),
            (R(@"^ChallengePack\d$"), Kind.Pack),
            (R(@"^(PerkDataId|PerkData1Id)$"), Kind.Perk),
            (R(@"^CinematicId$"), Kind.Cinematic),
            (R(@"^RewardTier$"), Kind.Tier),
            (R(@"^UnlockSkinId$"), Kind.Skin),
            (R(@"^CardPlayerGamePackId$"), Kind.CardPlayerPack),
            (R(@"^CardPlayerPairsGamePackId$"), Kind.CardPlayerPairsPack),
            (R(@"^(VisualGraphId|PetVisualGraphId)$"), Kind.VisualGraph),
            (R(@"^(Sound|SoundRework|ActionSound|HitSound|HitSounds|CombatMusic)$"), Kind.Audio),
            (R(@"^(EffectCaster|EffectTarget|EffectPreAction|EffectTrail|EffectTick|EffectTickSides)$"), Kind.Vfx),
            (R(@"^(SpriteBorderLocked|Sticker(Base|Love|Surprise|Angry|Indifferent)|SpriteBossDrop|NodeBackgroundSprite|SpriteMap|SpriteDecor|SpriteBook)$"), Kind.Sprite),
        };

        private static Regex R(string p) => new Regex(p, RegexOptions.Compiled);

        /// <summary>Offline tooling hook: supplies the id universe for a kind instead of the live game registries.</summary>
        public static Func<Kind, IEnumerable<string>> UniverseOverride;

        public static List<DiagMessage> Validate(ModProject p)
        {
            var result = new List<DiagMessage>();
            if (p == null) return result;

            var sets = new Dictionary<Kind, HashSet<string>>();
            HashSet<string> Set(Kind k)
            {
                if (sets.TryGetValue(k, out var s)) return s;
                IEnumerable<string> ids = UniverseOverride != null ? UniverseOverride(k) : k switch
                {
                    Kind.Card => RefIds.Cards(p).Concat(RefIds.Items(p)),
                    Kind.Npc => RefIds.Npcs(p),
                    Kind.Combat => RefIds.Combats(p),
                    Kind.Event => RefIds.Events(p),
                    Kind.AuraCurse => RefIds.AuraCurses(p),
                    Kind.Loot => RefIds.Loot(p),
                    Kind.SubClass => RefIds.SubClasses(p),
                    Kind.Requirement => RefIds.Requirements(p),
                    Kind.Pack => RefIds.Packs(p),
                    Kind.Perk => RefIds.Perks(p),
                    Kind.Cinematic => RefIds.Cinematics(p),
                    Kind.Tier => RefIds.Tiers(p),
                    Kind.ChallengeTrait => RefIds.ChallengeTraits(p),
                    Kind.Skin => RefIds.Skins(p),
                    Kind.CardPlayerPack => RefIds.CardPlayerPacks(p),
                    Kind.CardPlayerPairsPack => RefIds.CardPlayerPairsPacks(p),
                    Kind.VisualGraph => p.VisualGraphs.Keys.Concat(p.VisualGraphPatches.Keys),
                    Kind.Audio => UnknownMod.Runtime.AudioUtils.AllNames(),
                    Kind.Sprite => MapEditor.GetAllSpriteNames(),
                    Kind.Vfx => MapEditor.GetVfxEffectNames(),
                    _ => Enumerable.Empty<string>()
                };
                s = new HashSet<string>(ids.Where(i => !string.IsNullOrEmpty(i)), StringComparer.OrdinalIgnoreCase);
                sets[k] = s;
                return s;
            }

            var seen = new HashSet<string>();
            foreach (var e in ModReferences.Entities(p))
            {
                if (e.Def == null) continue;
                ModReferences.Walk(e.Def, "", 0, true, (path, value, setter, owner) =>
                {
                    string leaf = Leaf(path);
                    Kind? kind = KindFor(leaf, e.Def, owner);
                    if (kind == null) return;
                    if (Set(kind.Value).Contains(value)) return;
                    string msg = $"{e.Family}/{e.Id}: {path} = '{value}' does not match any known {kind.Value}";
                    // Clips and sprites only exist once their scene has loaded, so a miss here is a hint, not a fault.
                    var sev = kind.Value == Kind.Audio || kind.Value == Kind.Sprite ? DiagSeverity.Info : DiagSeverity.Warning;
                    if (seen.Add(msg)) result.Add(new DiagMessage(sev, msg));
                });
            }
            return result;
        }

        private static Kind? KindFor(string leaf, object rootDef, object owner)
        {
            if (rootDef is WeeklyChallengeDef && leaf == "TraitIds") return Kind.ChallengeTrait;
            foreach (var (pattern, kind) in Rules)
                if (pattern.IsMatch(leaf)) return kind;
            return null;
        }

        private static string Leaf(string path)
        {
            int dot = path.LastIndexOf('.');
            string leaf = dot >= 0 ? path.Substring(dot + 1) : path;
            int br = leaf.IndexOf('[');
            return br >= 0 ? leaf.Substring(0, br) : leaf;
        }

        /// <summary>Validate and write a summary to the BepInEx log.</summary>
        public static int LogReport(ModProject p)
        {
            var msgs = Validate(p);
            foreach (var m in msgs.Where(x => x.Severity != DiagSeverity.Info)) Plugin.Log.LogWarning($"[RefCheck:{p.ModId}] {m.Message}");
            if (msgs.Count == 0) Plugin.Log.LogInfo($"[RefCheck:{p.ModId}] No dangling references.");
            return msgs.Count;
        }
    }
}
