using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  THERMOMETER (combat difficulty / bonus-reward tiers)
    // ───────────────────────────────────────────────────────────────

    /// <summary>One thermometer step: the bonuses granted while a combat is at that heat level.</summary>
    [Serializable]
    public class ThermometerDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        /// <summary>HTML colour, e.g. "#FF8800".</summary>
        public string ColorHex = "#FFFFFF";
        public float GoldBonus;
        public float ExpBonus;
        public int CardBonus;
        public bool CardReward;
        public int ExpertiseBonus;

        /// <summary>Numbers shown in the thermometer UI.</summary>
        public int UiGold;
        public int UiExp;
        public int UiCard;
    }

    /// <summary>Maps combat rounds to thermometer steps. Referenced by <see cref="CombatDef.ThermometerTierId"/>.</summary>
    [Serializable]
    public class ThermometerTierDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        public List<RoundThermometerDef> Rounds = new();
    }

    [Serializable]
    public class RoundThermometerDef
    {
        public int Round;
        public string ThermometerId = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  TOWN MINIGAMES
    //  These are scene-serialised in the base game (no registry), so mod
    //  entries are appended to the manager lists at scene start.
    // ───────────────────────────────────────────────────────────────

    public enum BettingPool { Both, Pool1, Pool2 }

    /// <summary>A team that can appear in the town betting game.</summary>
    [Serializable]
    public class BettingTeamDef : IModEntity
    {
        public string TeamName = "";
        [JsonIgnore] public string EntityId { get => TeamName; set => TeamName = value; }

        public List<string> NpcIds = new();

        [JsonConverter(typeof(StringEnumConverter))]
        public BettingPool Pool = BettingPool.Both;
    }

    /// <summary>Card-sequence memory minigame content for a town zone.</summary>
    [Serializable]
    public class MemorySequenceDef : IModEntity
    {
        /// <summary>Town zone this applies to. If the zone has no sequence pack in the base game a new one is created.</summary>
        public string ZoneId = "";
        [JsonIgnore] public string EntityId { get => ZoneId; set => ZoneId = value; }

        /// <summary>Groups drawn for the sequence. Empty Pool = every base card of CardType.</summary>
        public List<MemorySequenceGroupDef> CardGroups = new();

        /// <summary>Distractor cards. Added to the zone's existing extra pool.</summary>
        public List<string> ExtraPoolCardIds = new();
    }

    [Serializable]
    public class MemorySequenceGroupDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardType = Enums.CardType.Food;
        public int Count = 1;
        public List<string> PoolCardIds = new();
    }

    /// <summary>Extra reward cards for the town card-pair memory minigame.</summary>
    [Serializable]
    public class CardPairRewardDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        /// <summary>Base-game pack id to extend. Empty = every card-pair pack.</summary>
        public string TargetPackId = "";

        public List<string> CardIds = new();
    }
}
