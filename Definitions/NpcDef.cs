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

        /// <summary>Audio clip name played when this NPC is hit (game clip or file in the mod audio/ folder). Blank keeps the SpriteSource NPC's sound.</summary>
        public string HitSound = "";
        public bool ShouldSerializeHitSound() => !string.IsNullOrEmpty(HitSound);

        /// <summary>Extra hit sounds picked in turn (game clip names or files in the mod audio/ folder). Added to HitSound.</summary>
        public List<string> HitSounds = new();
        public bool ShouldSerializeHitSounds() => HitSounds != null && HitSounds.Count > 0;

        /// <summary>Animation retarget: game trigger (attack, cast, hit, pet) to the rig's own trigger name. "-" plays nothing.</summary>
        public Dictionary<string, string> AnimTriggers = new();
        public bool ShouldSerializeAnimTriggers() => AnimTriggers != null && AnimTriggers.Count > 0;

        /// <summary>Do not borrow a neighbouring animation (cast for attack and back) when the rig lacks a trigger.</summary>
        public bool AnimNoFallback = false;
        public bool ShouldSerializeAnimNoFallback() => AnimNoFallback;

        /// <summary>Optional Graft Sprite (visual graph) id. A graft replaces SpriteSource as the sprite; its Host Model is the rig.</summary>

        public string VisualGraphId = "";
        public bool ShouldSerializeVisualGraphId() => !string.IsNullOrEmpty(VisualGraphId);

        /// <summary>Sprite name (mod PNG file name or game sprite name) used as the portrait instead of the one copied from SpriteSource.</summary>
        public string Portrait = "";
        public bool ShouldSerializePortrait() => !string.IsNullOrEmpty(Portrait);

        /// <summary>Mirror the portrait horizontally.</summary>
        public bool FlipPortrait = false;
        public bool ShouldSerializeFlipPortrait() => FlipPortrait;

        /// <summary>Zoom of the portrait content inside its frame (above 1 zooms in and crops, below 1 shrinks). 1 = unchanged.</summary>
        public float PortraitScale = 1f;
        public float PortraitOffsetX = 0f;
        public float PortraitOffsetY = 0f;
        public bool ShouldSerializePortraitScale() => PortraitScale != 1f;
        public bool ShouldSerializePortraitOffsetX() => PortraitOffsetX != 0f;
        public bool ShouldSerializePortraitOffsetY() => PortraitOffsetY != 0f;

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
