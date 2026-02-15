using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    //  NPC
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class NpcDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
        public string Name = "";
        public string Description = "";
        public string SpriteSource = "";

        public int Hp = 100;
        public int Speed = 10;
        public int Energy = 10;
        public int EnergyTurn = 0;
        public int CardsInHand = 2;

        // Resistances
        public int ResSlash = 0;
        public int ResBlunt = 0;
        public int ResPierce = 0;
        public int ResFire = 0;
        public int ResCold = 0;
        public int ResLight = 0;
        public int ResMind = 0;
        public int ResHoly = 0;
        public int ResShadow = 0;

        // AI Cards
        public List<AiCardDef> AiCards = new();

        // Rewards
        public int XpReward = 0;
        public int GoldReward = 0;
        public int TierReward = 3;

        // Flags
        public bool IsBoss = false;
        public bool IsNamed = false;
        public bool FinishCombatOnDead = false;
        public bool BigModel = false;
        public bool Female = false;
        public bool OnlyKillBossWhenHpZero = false;
        public int Difficulty = -1;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetPosition PreferredPos = Enums.CardTargetPosition.Anywhere;

        // Visual offsets (inherited from sprite source if zero)
        public float FluffOffsetX = 0f;
        public float FluffOffsetY = 0f;
        public float PosBottom = 0f;

        public List<string> Immunities = new();

        // Variant chain (IDs, not inline definitions)
        public string UpgradedMobId = "";
        public string NgPlusMobId = "";
        public string HellModeMobId = "";

        // Variant generation params (if this IS a variant)
        public string BaseNpcId = "";
        public float HpMult = 1f;
        public int SpeedBonus = 0;
        public int ResistBonus = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatTier TierMob = Enums.CombatTier.T1;
    }

    [Serializable]
    public class AiCardDef
    {
        public string CardId = "";
        public int Priority = 5;
        public int AddCardRound = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.OnlyCastIf OnlyCastIf = Enums.OnlyCastIf.Always;

        public float ValueCastIf = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.TargetCast TargetCast = Enums.TargetCast.Random;

        public int UnitsInDeck = 1;
        public float PercentToCast = 0f;
        public int StartsAtObeliskMadnessLevel = 0;
        public int StartsAtSingularityMadnessLevel = 0;

        /// <summary>AuraCurse ID that must be present to allow casting.</summary>
        public string AuracurseCastIf = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.OnlyCastIf SecondOnlyCastIf = Enums.OnlyCastIf.Always;

        public float SecondValueCastIf = 0f;

        /// <summary>Secondary NPC target ID for special targeting logic.</summary>
        public string SpecialSecondTargetId = "";
        public bool ShouldSerializeSpecialSecondTargetId() => !string.IsNullOrEmpty(SpecialSecondTargetId);
    }
}
