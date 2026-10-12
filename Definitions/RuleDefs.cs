using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  RULE SETS: combat behaviour as data
    //  A rule set says "when X happens in a fight (and these conditions hold), do Y". It covers
    //  challenge and madness trait effects, boss phases, extra summons, per-round combat rules,
    //  custom win/lose conditions and enemy AI restrictions without writing a scripts.dll.
    // ───────────────────────────────────────────────────────────────

    public enum RuleTrigger
    {
        CombatStart, RoundStart, RoundEnd, TurnStart, TurnEnd,
        /// <summary>A character took damage (Subject filter picks whose).</summary>
        Damaged,
        CharacterKilled,
        /// <summary>A card was played (Subject filter picks who played it, CardId narrows it).</summary>
        CardPlayed,
        /// <summary>A character's health fell to or under <see cref="RuleDef.HpPercent"/>. Fires once per character per fight.</summary>
        HpBelow
    }

    public enum RuleSubject { Any, Owner, Hero, Enemy }

    /// <summary>Which character a condition looks at.</summary>
    public enum RuleCharRef { Owner, Subject, AnyHero, AnyEnemy }

    public enum RuleCondKind
    {
        RoundAtLeast, RoundAtMost, RoundEvery,
        HeroesAliveAtLeast, HeroesAliveAtMost, EnemiesAliveAtLeast, EnemiesAliveAtMost,
        HpBelowPercent, HpAbovePercent,
        HasAura, LacksAura,
        NpcAlive, NpcDead,
        FlagSet, FlagNotSet
    }

    public enum RuleActionKind
    {
        AddAura, Damage, Heal, Summon, Kill, Win, Lose, Message, GainEnergy, SetFlag, ClearFlag
    }

    public enum RuleTarget
    {
        Owner, Subject, AllHeroes, AllEnemies, RandomHero, RandomEnemy,
        LowestHpHero, HighestHpHero, LowestHpEnemy,
        /// <summary>Characters summoned by an earlier Summon action of the same rule run.</summary>
        Summoned
    }

    public enum AiRuleMode { OnlyIf, Never }

    /// <summary>A named group of combat rules, switched on by a challenge trait, an enemy, a combat or always.</summary>
    [Serializable]
    public class RuleSetDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        public string Description = "";
        public bool Enabled = true;

        /// <summary>Active while this challenge or madness trait is active in the run (an obelisk challenge or singularity).</summary>
        public string ChallengeTraitId = "";
        /// <summary>Active while an enemy with this id is in the fight. That enemy is the rule's Owner.</summary>
        public string NpcId = "";
        /// <summary>Active only in this combat.</summary>
        public string CombatId = "";
        /// <summary>Active in every fight, when no other attachment is set.</summary>
        public bool AlwaysActive;

        public List<RuleDef> Rules = new();
        public List<AiRuleDef> AiRules = new();

        public bool ShouldSerializeDescription() => !string.IsNullOrEmpty(Description);
        public bool ShouldSerializeEnabled() => !Enabled;
        public bool ShouldSerializeChallengeTraitId() => !string.IsNullOrEmpty(ChallengeTraitId);
        public bool ShouldSerializeNpcId() => !string.IsNullOrEmpty(NpcId);
        public bool ShouldSerializeCombatId() => !string.IsNullOrEmpty(CombatId);
        public bool ShouldSerializeAlwaysActive() => AlwaysActive;
        public bool ShouldSerializeAiRules() => AiRules != null && AiRules.Count > 0;
    }

    [Serializable]
    public class RuleDef
    {
        public string Name = "";
        public bool Enabled = true;

        [JsonConverter(typeof(StringEnumConverter))]
        public RuleTrigger Trigger = RuleTrigger.RoundStart;
        [JsonConverter(typeof(StringEnumConverter))]
        public RuleSubject Subject = RuleSubject.Any;

        /// <summary>Narrow Damaged / CharacterKilled / HpBelow / CardPlayed to one enemy id.</summary>
        public string SubjectNpcId = "";
        /// <summary>Narrow CardPlayed to one card id.</summary>
        public string CardId = "";
        /// <summary>HpBelow threshold, in percent of max health.</summary>
        public int HpPercent = 50;

        /// <summary>First round the rule may run (0 = no limit).</summary>
        public int FromRound;
        /// <summary>Last round the rule may run (0 = no limit).</summary>
        public int ToRound;
        /// <summary>Run on rounds that are a multiple of this, counted from FromRound (0 = every round).</summary>
        public int EveryNRounds;
        /// <summary>Run at most once per fight.</summary>
        public bool Once;
        /// <summary>Chance to run, in percent.</summary>
        public int Chance = 100;

        public List<RuleConditionDef> Conditions = new();
        public List<RuleActionDef> Actions = new();

        public bool ShouldSerializeName() => !string.IsNullOrEmpty(Name);
        public bool ShouldSerializeEnabled() => !Enabled;
        public bool ShouldSerializeSubject() => Subject != RuleSubject.Any;
        public bool ShouldSerializeSubjectNpcId() => !string.IsNullOrEmpty(SubjectNpcId);
        public bool ShouldSerializeCardId() => !string.IsNullOrEmpty(CardId);
        public bool ShouldSerializeHpPercent() => Trigger == RuleTrigger.HpBelow;
        public bool ShouldSerializeFromRound() => FromRound != 0;
        public bool ShouldSerializeToRound() => ToRound != 0;
        public bool ShouldSerializeEveryNRounds() => EveryNRounds != 0;
        public bool ShouldSerializeOnce() => Once;
        public bool ShouldSerializeChance() => Chance != 100;
    }

    [Serializable]
    public class RuleConditionDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public RuleCondKind Kind = RuleCondKind.RoundAtLeast;
        [JsonConverter(typeof(StringEnumConverter))]
        public RuleCharRef Of = RuleCharRef.Owner;

        /// <summary>Number to compare with (round, count or percent).</summary>
        public int Value;
        /// <summary>Aura id, enemy id or flag name, depending on Kind.</summary>
        public string Id = "";

        public bool ShouldSerializeOf() => Kind == RuleCondKind.HpBelowPercent || Kind == RuleCondKind.HpAbovePercent
                                           || Kind == RuleCondKind.HasAura || Kind == RuleCondKind.LacksAura;
        public bool ShouldSerializeId() => !string.IsNullOrEmpty(Id);
    }

    [Serializable]
    public class RuleActionDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public RuleActionKind Kind = RuleActionKind.Message;
        [JsonConverter(typeof(StringEnumConverter))]
        public RuleTarget Target = RuleTarget.Owner;

        /// <summary>Aura id (AddAura), enemy id (Summon) or flag name (SetFlag / ClearFlag).</summary>
        public string Id = "";
        /// <summary>Charges, damage, healing or energy.</summary>
        public int Amount = 1;
        /// <summary>Damage / Heal: Amount is a percent of the target's max health instead of a flat number.</summary>
        public bool Percent;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageType = Enums.DamageType.None;
        /// <summary>Summon: how many to add.</summary>
        public int Count = 1;
        /// <summary>Summon: do nothing while this many of that enemy are alive (0 = no limit).</summary>
        public int MaxAlive;
        /// <summary>Message text.</summary>
        public string Text = "";

        public bool ShouldSerializeTarget() => Kind != RuleActionKind.Win && Kind != RuleActionKind.Lose
                                               && Kind != RuleActionKind.Summon && Kind != RuleActionKind.SetFlag
                                               && Kind != RuleActionKind.ClearFlag;
        public bool ShouldSerializeId() => !string.IsNullOrEmpty(Id);
        public bool ShouldSerializePercent() => Percent;
        public bool ShouldSerializeDamageType() => Kind == RuleActionKind.Damage;
        public bool ShouldSerializeCount() => Kind == RuleActionKind.Summon;
        public bool ShouldSerializeMaxAlive() => Kind == RuleActionKind.Summon && MaxAlive != 0;
        public bool ShouldSerializeText() => !string.IsNullOrEmpty(Text);
    }

    /// <summary>Restricts when an enemy of the rule set's NpcId may play a card.</summary>
    [Serializable]
    public class AiRuleDef
    {
        /// <summary>Card id the rule applies to. Empty = every card the enemy would play.</summary>
        public string CardId = "";
        [JsonConverter(typeof(StringEnumConverter))]
        public AiRuleMode Mode = AiRuleMode.OnlyIf;
        public List<RuleConditionDef> Conditions = new();

        public bool ShouldSerializeCardId() => !string.IsNullOrEmpty(CardId);
    }
}
