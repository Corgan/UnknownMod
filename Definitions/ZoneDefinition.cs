using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace UnknownMod.Definitions
{
    // ═══════════════════════════════════════════════════════════════
    //  JSON-serializable DTOs for all zone entity types.
    //  These mirror the game ScriptableObjects but use plain C#
    //  classes + string IDs for cross-references.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Top-level container for a complete zone definition.</summary>
    [Serializable]
    public class ZoneDef
    {
        public string ZoneId = "";
        public string ZoneName = "";

        /// <summary>Short prefix used for entity IDs (e.g. "myc" for mycelium_abyss).</summary>
        public string IdPrefix = "";

        public bool ObeliskLow = false;
        public bool ObeliskHigh = false;
        public bool ObeliskFinal = false;
        public bool DisableExperience = false;
        public bool DisableMadness = false;

        /// <summary>Filename of the background image (relative to zone folder).</summary>
        public string BackgroundImage = "background.jpeg";

        public Dictionary<string, NodeDef> Nodes = new();
        public Dictionary<string, CombatDef> Combats = new();
        public Dictionary<string, EventDef> Events = new();
        public Dictionary<string, NpcDef> Npcs = new();
        public Dictionary<string, CardDef> Cards = new();
        public Dictionary<string, ItemDef> Items = new();
        public Dictionary<string, LootDef> Loot = new();
        public Dictionary<string, RoadDef> Roads = new();

        /// <summary>Reusable sprite definitions. Key = sprite def ID, referenced by NpcDef.SpriteSource.</summary>
        public Dictionary<string, SpriteOverrideDef> Sprites = new();

        /// <summary>DEPRECATED: per-NPC overrides from old format. Migrated into Sprites on load.</summary>
        public Dictionary<string, SpriteOverrideDef> SpriteOverrides = new();
        public bool ShouldSerializeSpriteOverrides() => false; // never write to JSON
    }

    // ───────────────────────────────────────────────────────────────
    //  NODE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class NodeDef
    {
        public string NodeId = "";
        public string NodeName = "";
        public string Description = "";
        public float PosX = 0f;
        public float PosY = 0f;

        public bool TravelDestination = false;
        public bool GoToTown = false;
        public int ExistsPercent = 100;
        public bool DisableCorruption = false;
        public bool DisableRandom = false;
        public bool VisibleIfNotRequirement = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.NodeGround NodeGround = Enums.NodeGround.None;

        /// <summary>EventRequirement ID that must be met to enter this node.</summary>
        public string NodeRequirementId = "";

        // Combat assignment (empty = no combat)
        public string CombatId = "";
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatTier CombatTier = Enums.CombatTier.T0;
        public int CombatPercent = -1;

        // Event assignment (empty = no event)
        public string EventId = "";
        public int EventPercent = -1;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatTier NodeEventTier = Enums.CombatTier.T0;

        // Connections — list of target node IDs
        public List<string> Connections = new();

        // Conditional connections
        public List<NodeConnectionReqDef> ConnectionRequirements = new();
    }

    // ───────────────────────────────────────────────────────────────
    //  NODE CONNECTION REQUIREMENT
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class NodeConnectionReqDef
    {
        public string TargetNodeId = "";
        public string RequirementId = "";
        public string IfNotNodeId = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  COMBAT ENCOUNTER
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CombatDef
    {
        public string CombatId = "";
        public string Description = "";
        public List<string> NpcIds = new();

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatTier CombatTier = Enums.CombatTier.T3;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatBackground Background = Enums.CombatBackground.Spider_Lair;

        public int NpcRemoveInMadness0Index = -1;
        public bool HealHeroes = false;
        public bool IsRift = false;

        /// <summary>NPC ID summoned when a monster in this combat is killed.</summary>
        public string NpcToSummonOnKilledId = "";

        /// <summary>Event triggered after combat ends (post-combat event).</summary>
        public string EventDataId = "";

        /// <summary>Aura/curse effects applied at combat start.</summary>
        public List<CombatEffectDef> CombatEffects = new();
    }

    // ───────────────────────────────────────────────────────────────
    //  COMBAT EFFECT (start-of-combat aura/curse)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CombatEffectDef
    {
        public string AuraCurse = "";
        public int Charges = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatUnit Target = Enums.CombatUnit.Heroes;
    }

    // ───────────────────────────────────────────────────────────────
    //  EVENT
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class EventDef
    {
        public string EventId = "";
        public string EventName = "";
        public string Description = "";
        public string DescriptionAction = "";

        /// <summary>Base-game event ID to borrow book/decor/map sprites from.</summary>
        public string SpriteSource = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatTier EventTier = Enums.CombatTier.T0;

        public int ReplyRandom = 0;

        /// <summary>EventRequirement ID needed to trigger this event.</summary>
        public string RequirementId = "";

        public List<ReplyDef> Replies = new();
    }

    [Serializable]
    public class OutcomeDef
    {
        public string Text = "";
        public float HealPercent = 0f;
        public int HealFlat = 0;
        public int Gold = 0;
        public int Dust = 0;
        public int Supply = 0;
        public int XP = 0;
        public string CombatId = "";
        public string EventId = "";
        public string NodeTravelId = "";
        public string RequirementUnlockId = "";
        public string RequirementUnlock2Id = "";
        public string RequirementLockId = "";
        public string RequirementLock2Id = "";
        public string LootId = "";
        public string ShopId = "";
        public string AddItemId = "";
        public string AddCard1Id = "";
        public string AddCard2Id = "";
        public string AddCard3Id = "";
        public string RewardTier = "";
        public int Discount = 0;
        public int MaxQuantity = 0;
        public bool HealerUI = false;
        public bool UpgradeUI = false;
        public bool CraftUI = false;
        public bool MerchantUI = false;
        public bool CorruptionUI = false;
        public bool UpgradeRandomCard = false;
        public bool FinishGame = false;
        public bool FinishObeliskMap = false;
    }

    [Serializable]
    [JsonConverter(typeof(ReplyDefConverter))]
    public class ReplyDef
    {
        public string ReplyText = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EventAction Action = Enums.EventAction.None;

        // Costs
        public int GoldCost = 0;
        public int DustCost = 0;

        // Requirements
        public string RequirementId = "";
        public string RequirementBlockedId = "";

        // Roll
        public bool HasRoll = false;
        public int RollDC = 0;
        public int RollCrit = -1;
        public int RollCritFail = -1;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.RollMode RollMode = Enums.RollMode.HigherOrEqual;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.RollTarget RollTarget = Enums.RollTarget.Single;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType RollCard = Enums.CardType.None;

        // ── Outcome branches ─────────────────────────────────────
        public OutcomeDef Ss = new();
        public OutcomeDef Fl = new();
        public OutcomeDef Ssc = new();
        public OutcomeDef Flc = new();
    }

    /// <summary>
    /// Reads/writes ReplyDef using flat prefixed JSON keys (SsText, FlGold, etc.)
    /// for backward compatibility with existing event JSON files.
    /// </summary>
    public class ReplyDefConverter : JsonConverter<ReplyDef>
    {
        public override ReplyDef ReadJson(JsonReader reader, Type objectType, ReplyDef existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var jo = JObject.Load(reader);
            var r = new ReplyDef();

            r.ReplyText = (string)jo["ReplyText"] ?? "";
            r.Action = jo["Action"]?.ToObject<Enums.EventAction>(serializer) ?? Enums.EventAction.None;
            r.GoldCost = (int?)jo["GoldCost"] ?? 0;
            r.DustCost = (int?)jo["DustCost"] ?? 0;
            r.RequirementId = (string)jo["RequirementId"] ?? "";
            r.RequirementBlockedId = (string)jo["RequirementBlockedId"] ?? "";
            r.HasRoll = (bool?)jo["HasRoll"] ?? false;
            r.RollDC = (int?)jo["RollDC"] ?? 0;
            r.RollCrit = (int?)jo["RollCrit"] ?? -1;
            r.RollCritFail = (int?)jo["RollCritFail"] ?? -1;
            r.RollMode = jo["RollMode"]?.ToObject<Enums.RollMode>(serializer) ?? Enums.RollMode.HigherOrEqual;
            r.RollTarget = jo["RollTarget"]?.ToObject<Enums.RollTarget>(serializer) ?? Enums.RollTarget.Single;
            r.RollCard = jo["RollCard"]?.ToObject<Enums.CardType>(serializer) ?? Enums.CardType.None;

            ReadOutcome(jo, "Ss", r.Ss);
            ReadOutcome(jo, "Fl", r.Fl);
            ReadOutcome(jo, "Ssc", r.Ssc);
            ReadOutcome(jo, "Flc", r.Flc);

            // Fields that only exist on certain prefixes
            r.Ss.FinishGame = (bool?)jo["SsFinishGame"] ?? false;
            r.Ss.FinishObeliskMap = (bool?)jo["SsFinishObeliskMap"] ?? false;
            r.Ssc.FinishGame = (bool?)jo["SscFinishGame"] ?? false;

            return r;
        }

        public override void WriteJson(JsonWriter writer, ReplyDef r, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("ReplyText"); writer.WriteValue(r.ReplyText);
            writer.WritePropertyName("Action"); serializer.Serialize(writer, r.Action);
            writer.WritePropertyName("GoldCost"); writer.WriteValue(r.GoldCost);
            writer.WritePropertyName("DustCost"); writer.WriteValue(r.DustCost);
            writer.WritePropertyName("RequirementId"); writer.WriteValue(r.RequirementId);
            writer.WritePropertyName("RequirementBlockedId"); writer.WriteValue(r.RequirementBlockedId);
            writer.WritePropertyName("HasRoll"); writer.WriteValue(r.HasRoll);
            writer.WritePropertyName("RollDC"); writer.WriteValue(r.RollDC);
            writer.WritePropertyName("RollCrit"); writer.WriteValue(r.RollCrit);
            writer.WritePropertyName("RollCritFail"); writer.WriteValue(r.RollCritFail);
            writer.WritePropertyName("RollMode"); serializer.Serialize(writer, r.RollMode);
            writer.WritePropertyName("RollTarget"); serializer.Serialize(writer, r.RollTarget);
            writer.WritePropertyName("RollCard"); serializer.Serialize(writer, r.RollCard);

            WriteOutcome(writer, "Ss", r.Ss);
            writer.WritePropertyName("SsFinishGame"); writer.WriteValue(r.Ss.FinishGame);
            writer.WritePropertyName("SsFinishObeliskMap"); writer.WriteValue(r.Ss.FinishObeliskMap);

            WriteOutcome(writer, "Fl", r.Fl);

            WriteOutcome(writer, "Ssc", r.Ssc);
            writer.WritePropertyName("SscFinishGame"); writer.WriteValue(r.Ssc.FinishGame);

            WriteOutcome(writer, "Flc", r.Flc);

            writer.WriteEndObject();
        }

        private static void ReadOutcome(JObject jo, string p, OutcomeDef o)
        {
            o.Text = (string)jo[$"{p}Text"] ?? "";
            o.HealPercent = (float?)jo[$"{p}HealPercent"] ?? 0f;
            o.HealFlat = (int?)jo[$"{p}HealFlat"] ?? 0;
            o.Gold = (int?)jo[$"{p}Gold"] ?? 0;
            o.Dust = (int?)jo[$"{p}Dust"] ?? 0;
            o.Supply = (int?)jo[$"{p}Supply"] ?? 0;
            o.XP = (int?)jo[$"{p}XP"] ?? 0;
            o.CombatId = (string)jo[$"{p}CombatId"] ?? "";
            o.EventId = (string)jo[$"{p}EventId"] ?? "";
            o.NodeTravelId = (string)jo[$"{p}NodeTravelId"] ?? "";
            o.RequirementUnlockId = (string)jo[$"{p}RequirementUnlockId"] ?? "";
            o.RequirementUnlock2Id = (string)jo[$"{p}RequirementUnlock2Id"] ?? "";
            o.RequirementLockId = (string)jo[$"{p}RequirementLockId"] ?? "";
            o.RequirementLock2Id = (string)jo[$"{p}RequirementLock2Id"] ?? "";
            o.LootId = (string)jo[$"{p}LootId"] ?? "";
            o.ShopId = (string)jo[$"{p}ShopId"] ?? "";
            o.AddItemId = (string)jo[$"{p}AddItemId"] ?? "";
            o.AddCard1Id = (string)jo[$"{p}AddCard1Id"] ?? "";
            o.AddCard2Id = (string)jo[$"{p}AddCard2Id"] ?? "";
            o.AddCard3Id = (string)jo[$"{p}AddCard3Id"] ?? "";
            o.RewardTier = (string)jo[$"{p}RewardTier"] ?? "";
            o.Discount = (int?)jo[$"{p}Discount"] ?? 0;
            o.MaxQuantity = (int?)jo[$"{p}MaxQuantity"] ?? 0;
            o.HealerUI = (bool?)jo[$"{p}HealerUI"] ?? false;
            o.UpgradeUI = (bool?)jo[$"{p}UpgradeUI"] ?? false;
            o.CraftUI = (bool?)jo[$"{p}CraftUI"] ?? false;
            o.MerchantUI = (bool?)jo[$"{p}MerchantUI"] ?? false;
            o.CorruptionUI = (bool?)jo[$"{p}CorruptionUI"] ?? false;
            o.UpgradeRandomCard = (bool?)jo[$"{p}UpgradeRandomCard"] ?? false;
        }

        private static void WriteOutcome(JsonWriter writer, string p, OutcomeDef o)
        {
            writer.WritePropertyName($"{p}Text"); writer.WriteValue(o.Text);
            writer.WritePropertyName($"{p}HealPercent"); writer.WriteValue(o.HealPercent);
            writer.WritePropertyName($"{p}HealFlat"); writer.WriteValue(o.HealFlat);
            writer.WritePropertyName($"{p}Gold"); writer.WriteValue(o.Gold);
            writer.WritePropertyName($"{p}Dust"); writer.WriteValue(o.Dust);
            writer.WritePropertyName($"{p}Supply"); writer.WriteValue(o.Supply);
            writer.WritePropertyName($"{p}XP"); writer.WriteValue(o.XP);
            writer.WritePropertyName($"{p}CombatId"); writer.WriteValue(o.CombatId);
            writer.WritePropertyName($"{p}EventId"); writer.WriteValue(o.EventId);
            writer.WritePropertyName($"{p}NodeTravelId"); writer.WriteValue(o.NodeTravelId);
            writer.WritePropertyName($"{p}RequirementUnlockId"); writer.WriteValue(o.RequirementUnlockId);
            writer.WritePropertyName($"{p}RequirementUnlock2Id"); writer.WriteValue(o.RequirementUnlock2Id);
            writer.WritePropertyName($"{p}RequirementLockId"); writer.WriteValue(o.RequirementLockId);
            writer.WritePropertyName($"{p}RequirementLock2Id"); writer.WriteValue(o.RequirementLock2Id);
            writer.WritePropertyName($"{p}LootId"); writer.WriteValue(o.LootId);
            writer.WritePropertyName($"{p}ShopId"); writer.WriteValue(o.ShopId);
            writer.WritePropertyName($"{p}AddItemId"); writer.WriteValue(o.AddItemId);
            writer.WritePropertyName($"{p}AddCard1Id"); writer.WriteValue(o.AddCard1Id);
            writer.WritePropertyName($"{p}AddCard2Id"); writer.WriteValue(o.AddCard2Id);
            writer.WritePropertyName($"{p}AddCard3Id"); writer.WriteValue(o.AddCard3Id);
            writer.WritePropertyName($"{p}RewardTier"); writer.WriteValue(o.RewardTier);
            writer.WritePropertyName($"{p}Discount"); writer.WriteValue(o.Discount);
            writer.WritePropertyName($"{p}MaxQuantity"); writer.WriteValue(o.MaxQuantity);
            writer.WritePropertyName($"{p}HealerUI"); writer.WriteValue(o.HealerUI);
            writer.WritePropertyName($"{p}UpgradeUI"); writer.WriteValue(o.UpgradeUI);
            writer.WritePropertyName($"{p}CraftUI"); writer.WriteValue(o.CraftUI);
            writer.WritePropertyName($"{p}MerchantUI"); writer.WriteValue(o.MerchantUI);
            writer.WritePropertyName($"{p}CorruptionUI"); writer.WriteValue(o.CorruptionUI);
            writer.WritePropertyName($"{p}UpgradeRandomCard"); writer.WriteValue(o.UpgradeRandomCard);
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  NPC
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class NpcDef
    {
        public string Id = "";
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
    }

    // ───────────────────────────────────────────────────────────────
    //  CARD (monster ability)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CardDef
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public string Fluff = "";
        public float FluffPercent = 0f;
        public bool IsUpgraded = false;
        public string BaseCardId = "";
        /// <summary>Base-game card ID to copy the card art sprite from.</summary>
        public string SpriteSource = "";

        // ── Card Classification ──────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardUpgraded CardUpgraded = Enums.CardUpgraded.No;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardRarity CardRarity = Enums.CardRarity.Common;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardType = Enums.CardType.None;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Enums.CardType[] CardTypeAux = Array.Empty<Enums.CardType>();
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardClass CardClass = Enums.CardClass.Monster;
        public int CardNumber = 0;
        public int MaxInDeck = 0;

        // ── Cost / Economy ───────────────────────────────────────
        public int EnergyCost = 0;
        public int EnergyCostOriginal = 0;
        public int EnergyCostForShow = 0;
        public int EnergyReductionPermanent = 0;
        public int EnergyReductionTemporal = 0;
        public bool EnergyReductionToZeroPermanent = false;
        public bool EnergyReductionToZeroTemporal = false;

        // ── Flags ────────────────────────────────────────────────
        public bool Playable = false;
        public bool Visible = false;
        public bool ShowInTome = false;
        public bool AutoplayDraw = false;
        public bool AutoplayEndTurn = false;
        public bool Vanish = false;
        public bool Innate = false;
        public bool Lazy = false;
        public bool Corrupted = false;
        public bool EndTurn = false;
        public bool Starter = false;
        public bool FlipSprite = false;
        public bool ModifiedByTrait = false;
        public bool OnlyInWeekly = false;
        public string Sku = "";

        // ── Upgrade Paths ────────────────────────────────────────
        public string UpgradesTo1 = "";
        public string UpgradesTo2 = "";
        public string UpgradedFrom = "";
        public string BaseCard = "";
        public string RelatedCard = "";
        public string RelatedCard2 = "";
        public string RelatedCard3 = "";

        // ── Damage ───────────────────────────────────────────────
        public int Damage = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageType = Enums.DamageType.None;
        public int DamageSides = 0;
        public int DamageEnergyBonus = 0;

        public int Damage2 = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageType2 = Enums.DamageType.None;
        public int DamageSides2 = 0;

        public bool IgnoreBlock = false;
        public bool IgnoreBlock2 = false;
        public int DamageSelf = 0;
        public int DamageSelf2 = 0;

        // ── Curses (applied to target) ───────────────────────────
        public string Curse = "";
        public int CurseCharges = 0;
        public int CurseChargesSides = 0;
        public string Curse2 = "";
        public int Curse2Charges = 0;
        public string Curse3 = "";
        public int Curse3Charges = 0;

        // ── Auras (applied to target) ────────────────────────────
        public string Aura = "";
        public int AuraCharges = 0;
        public string Aura2 = "";
        public int Aura2Charges = 0;
        public string Aura3 = "";
        public int Aura3Charges = 0;

        // ── Self auras/curses ────────────────────────────────────
        public string AuraSelf = "";
        public int AuraSelfCharges = 0;
        public string AuraSelf2 = "";
        public int AuraSelf2Charges = 0;
        public string AuraSelf3 = "";
        public int AuraSelf3Charges = 0;
        public string CurseSelf = "";
        public int CurseSelfCharges = 0;
        public string CurseSelf2 = "";
        public int CurseSelf2Charges = 0;
        public string CurseSelf3 = "";
        public int CurseSelf3Charges = 0;

        // ── Heal ─────────────────────────────────────────────────
        public int Heal = 0;
        public int HealSides = 0;
        public int HealSelf = 0;
        public int HealEnergyBonus = 0;
        public int HealCurses = 0;
        public int DispelAuras = 0;
        public float HealSelfPerDamageDonePercent = 0f;
        public string HealAuraCurseSelf = "";    // specific AC to remove from self
        public string HealAuraCurseName = "";    // specific AC to remove from target
        public string HealAuraCurseName2 = "";
        public string HealAuraCurseName3 = "";
        public string HealAuraCurseName4 = "";

        // ── Aura/curse manipulation ──────────────────────────────
        public int TransferCurses = 0;
        public int StealAuras = 0;
        public int ReduceCurses = 0;
        public int ReduceAuras = 0;
        public int IncreaseCurses = 0;
        public int IncreaseAuras = 0;

        // ── Targeting ────────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetSide TargetSide = Enums.CardTargetSide.Enemy;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetType TargetType = Enums.CardTargetType.Single;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardTargetPosition TargetPos = Enums.CardTargetPosition.Anywhere;

        // ── Effect Repeat ────────────────────────────────────────
        public int EffectRepeat = 1;
        public float EffectRepeatDelay = 0f;
        public int EffectRepeatEnergyBonus = 0;
        public int EffectRepeatMaxBonus = 0;
        public int EffectRepeatModificator = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EffectRepeatTarget EffectRepeatTarget = Enums.EffectRepeatTarget.NoRepeat;

        // ── Misc Mechanics ───────────────────────────────────────
        public bool MoveToCenter = false;
        public int SelfHealthLoss = 0;
        public int PushTarget = 0;
        public int PullTarget = 0;
        public int DrawCard = 0;
        public int DiscardCard = 0;
        public int EnergyRecharge = 0;
        public int GoldGainQuantity = 0;
        public int ShardsGainQuantity = 0;
        public int ExhaustCounter = 0;
        public string EffectRequired = "";
        public float SelfKillHiddenSeconds = 0f;

        // ── Discard Options ──────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType DiscardCardType = Enums.CardType.None;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Enums.CardType[] DiscardCardTypeAux = Array.Empty<Enums.CardType>();
        public bool DiscardCardAutomatic = false;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardPlace DiscardCardPlace = Enums.CardPlace.Discard;

        // ── Add Card ─────────────────────────────────────────────
        public int AddCard = 0;
        public string AddCardId = "";
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType AddCardType = Enums.CardType.None;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Enums.CardType[] AddCardTypeAux = Array.Empty<Enums.CardType>();
        public int AddCardChoose = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardFrom AddCardFrom = Enums.CardFrom.Game;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardPlace AddCardPlace = Enums.CardPlace.Hand;
        public int AddCardReducedCost = 0;
        public bool AddCardCostTurn = false;
        public bool AddCardVanish = false;
        public bool AddCardOnlyCheckAuxTypes = false;
        public bool AddCardFromVanishPile = false;
        public bool AddVanishToDeck = false;
        public List<string> AddCardList = new();  // card IDs

        // ── Look (Scry) ─────────────────────────────────────────
        public int LookCards = 0;
        public int LookCardsDiscardUpTo = 0;
        public int LookCardsVanishUpTo = 0;

        // ── Summon ───────────────────────────────────────────────
        public string SummonUnitId = "";
        public int SummonNum = 0;
        public string SummonAura = "";       // AC ID
        public int SummonAuraCharges = 0;
        public string SummonAura2 = "";
        public int SummonAuraCharges2 = 0;
        public string SummonAura3 = "";
        public int SummonAuraCharges3 = 0;
        public bool Evolve = false;
        public bool Metamorph = false;

        // ── AC Energy Bonus ──────────────────────────────────────
        public string AcEnergyBonus = "";    // AC ID
        public int AcEnergyBonusQuantity = 0;
        public string AcEnergyBonus2 = "";   // AC ID
        public int AcEnergyBonus2Quantity = 0;
        public bool ChooseOneOfAvailableAuras = false;

        // ── Special Value System ─────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardSpecialValue SpecialValueGlobal = Enums.CardSpecialValue.None;
        public float SpecialValueModifierGlobal = 0f;
        public string SpecialAuraCurseNameGlobal = "";  // AC ID
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardSpecialValue SpecialValue1 = Enums.CardSpecialValue.None;
        public float SpecialValueModifier1 = 0f;
        public string SpecialAuraCurseName1 = "";       // AC ID
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardSpecialValue SpecialValue2 = Enums.CardSpecialValue.None;
        public float SpecialValueModifier2 = 0f;
        public string SpecialAuraCurseName2 = "";       // AC ID

        // ── Special Value Scaling Flags ──────────────────────────
        public bool DamageSpecialValueGlobal = false;
        public bool DamageSpecialValue1 = false;
        public bool DamageSpecialValue2 = false;
        public bool Damage2SpecialValueGlobal = false;
        public bool Damage2SpecialValue1 = false;
        public bool Damage2SpecialValue2 = false;
        public bool HealSpecialValueGlobal = false;
        public bool HealSpecialValue1 = false;
        public bool HealSpecialValue2 = false;
        public bool HealSelfSpecialValueGlobal = false;
        public bool HealSelfSpecialValue1 = false;
        public bool HealSelfSpecialValue2 = false;
        public bool SelfHealthLossSpecialGlobal = false;
        public bool SelfHealthLossSpecialValue1 = false;
        public bool SelfHealthLossSpecialValue2 = false;
        public bool EnergyRechargeSpecialValueGlobal = false;
        public bool DrawCardSpecialValueGlobal = false;

        // ── Aura/Curse Charges Special Value Flags ───────────────
        public bool AuraChargesSpecialValueGlobal = false;
        public bool AuraChargesSpecialValue1 = false;
        public bool AuraChargesSpecialValue2 = false;
        public bool AuraCharges2SpecialValueGlobal = false;
        public bool AuraCharges2SpecialValue1 = false;
        public bool AuraCharges2SpecialValue2 = false;
        public bool AuraCharges3SpecialValueGlobal = false;
        public bool AuraCharges3SpecialValue1 = false;
        public bool AuraCharges3SpecialValue2 = false;
        public bool CurseChargesSpecialValueGlobal = false;
        public bool CurseChargesSpecialValue1 = false;
        public bool CurseChargesSpecialValue2 = false;
        public bool CurseCharges2SpecialValueGlobal = false;
        public bool CurseCharges2SpecialValue1 = false;
        public bool CurseCharges2SpecialValue2 = false;
        public bool CurseCharges3SpecialValueGlobal = false;
        public bool CurseCharges3SpecialValue1 = false;
        public bool CurseCharges3SpecialValue2 = false;

        // ── FX / Effects ─────────────────────────────────────────
        public string EffectCaster = "";
        public string EffectTarget = "";
        public string EffectPreAction = "";
        public float EffectPostCastDelay = 0f;
        public bool EffectCasterRepeat = false;
        public bool EffectCastCenter = false;
        public string EffectTrail = "";
        public bool EffectTrailRepeat = false;
        public float EffectTrailSpeed = 0f;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EffectTrailAngle EffectTrailAngle = Enums.EffectTrailAngle.Horizontal;
        public float EffectPostTargetDelay = 0f;

        // ── Pet System ───────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ActivePets PetActivation = Enums.ActivePets.None;
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType PetBonusDamageType = Enums.DamageType.None;
        public int PetBonusDamageAmount = 0;
        public bool IsPetAttack = false;
        public bool IsPetCast = false;
        public bool KillPet = false;
        public bool PetTemporal = false;
        public bool PetTemporalAttack = false;
        public bool PetTemporalCast = false;
        public bool PetTemporalMoveToCenter = false;
        public bool PetTemporalMoveToBack = false;
        public float PetTemporalFadeOutDelay = 0f;

        // ── Upgrade params (for auto-generated A variants) ───────
        public float UpgDamageMult = 1.3f;
        public int UpgBonusCurseCharges = 1;
        public int UpgBonusAuraCharges = 1;
        public int UpgBonusHeal = 3;
    }

    // ───────────────────────────────────────────────────────────────
    //  ITEM (equipment / reward)
    // ───────────────────────────────────────────────────────────────
    //  SPECIAL VALUE (mirrors game SpecialValues struct)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class SpecialValueDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.SpecialValueModifierName Name = Enums.SpecialValueModifierName.RuneCharges;
        public bool Use = false;
        public float Multiplier = 0f;
    }

    // ───────────────────────────────────────────────────────────────
    //  ITEM
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class ItemDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        public string Name = "";
        /// <summary>Base-game card ID to copy the item card art sprite from.</summary>
        public string SpriteSource = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardType = Enums.CardType.Weapon;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardRarity Rarity = Enums.CardRarity.Common;

        // ── Activation / Requisite ───────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.EventActivation Activation = Enums.EventActivation.None;
        public bool ActivationOnlyOnHeroes = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ItemTarget ItemTarget = Enums.ItemTarget.Self;
        public bool DontTargetBoss = false;

        public int TimesPerTurn = 0;
        public int TimesPerCombat = 0;
        public int ExactRound = 0;
        public int RoundCycle = 0;

        public string AuraCurseSetted = "";   // AC ID
        public string AuraCurseSetted2 = "";  // AC ID
        public string AuraCurseSetted3 = "";  // AC ID
        public int AuraCurseNumForOneEvent = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CastedCardType = Enums.CardType.None;
        public bool UsedEnergy = false;
        public float LowerOrEqualPercentHP = 100f;
        public bool EmptyHand = false;
        public bool NotShowCharacterBonus = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ActivePets PetActivation = Enums.ActivePets.None;

        // ── Damage Bonuses (passive stat) ────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageFlatBonus = Enums.DamageType.None;
        public int DamageFlatBonusValue = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageFlatBonus2 = Enums.DamageType.None;
        public int DamageFlatBonusValue2 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageFlatBonus3 = Enums.DamageType.None;
        public int DamageFlatBonusValue3 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamagePercentBonus = Enums.DamageType.None;
        public float DamagePercentBonusValue = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamagePercentBonus2 = Enums.DamageType.None;
        public float DamagePercentBonusValue2 = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamagePercentBonus3 = Enums.DamageType.None;
        public float DamagePercentBonusValue3 = 0f;

        // ── Resist Bonuses ───────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified1 = Enums.DamageType.None;
        public int ResistModifiedValue1 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified2 = Enums.DamageType.None;
        public int ResistModifiedValue2 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified3 = Enums.DamageType.None;
        public int ResistModifiedValue3 = 0;

        // ── Character Stat Mods ──────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CharacterStat CharacterStatModified = Enums.CharacterStat.None;
        public int CharacterStatModifiedValue = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CharacterStat CharacterStatModified2 = Enums.CharacterStat.None;
        public int CharacterStatModifiedValue2 = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CharacterStat CharacterStatModified3 = Enums.CharacterStat.None;
        public int CharacterStatModifiedValue3 = 0;

        public int MaxHealth = 0;

        // ── Heal Bonuses ─────────────────────────────────────────
        public int HealFlatBonus = 0;
        public float HealPercentBonus = 0f;
        public int HealReceivedFlatBonus = 0;
        public float HealReceivedPercentBonus = 0f;
        public int HealQuantity = 0;
        public SpecialValueDef HealQuantitySpecialValue;
        public int HealPercentQuantity = 0;
        public int HealPercentQuantitySelf = 0;
        public float HealSelfPerDamageDonePercent = 0f;
        public bool HealSelfTeamPerDamageDonePercent = false;
        public int HealBasedOnAuraCurse = 0;

        // ── Energy / Draw ────────────────────────────────────────
        public int EnergyQuantity = 0;
        public int DrawCards = 0;
        public bool DrawMultiplyByEnergyUsed = false;

        // ── On-activation AuraCurse (target) ─────────────────────
        public string AuracurseGain1 = "";
        public int AuracurseGainValue1 = 0;
        public SpecialValueDef AuracurseGain1SpecialValue;
        public bool Acg1MultiplyByEnergyUsed = false;

        public string AuracurseGain2 = "";
        public int AuracurseGainValue2 = 0;
        public SpecialValueDef AuracurseGain2SpecialValue;
        public bool Acg2MultiplyByEnergyUsed = false;

        public string AuracurseGain3 = "";
        public int AuracurseGainValue3 = 0;
        public SpecialValueDef AuracurseGain3SpecialValue;
        public bool Acg3MultiplyByEnergyUsed = false;
        public bool ChooseOneACToGain = false;

        // ── On-activation AuraCurse (self) ───────────────────────
        public string AuracurseGainSelf1 = "";
        public int AuracurseGainSelfValue1 = 0;
        public string AuracurseGainSelf2 = "";
        public int AuracurseGainSelfValue2 = 0;
        public string AuracurseGainSelf3 = "";
        public int AuracurseGainSelfValue3 = 0;

        // ── AC Dispel / Purge ────────────────────────────────────
        public string AuracurseHeal1 = "";    // AC ID to dispel/purge
        public string AuracurseHeal2 = "";
        public string AuracurseHeal3 = "";
        public bool AcHealFromTarget = false;
        public int StealAuras = 0;
        public int ChanceToDispel = 0;
        public int ChanceToDispelNum = 0;
        public int ChanceToPurge = 0;
        public int ChanceToPurgeNum = 0;
        public int ChanceToDispelSelf = 0;
        public int ChanceToDispelNumSelf = 0;

        // ── Passive AC Bonuses ───────────────────────────────────
        public string AuracurseBonus1 = "";
        public int AuracurseBonusValue1 = 0;
        public string AuracurseBonus2 = "";
        public int AuracurseBonusValue2 = 0;
        public int IncreaseAurasSelf = 0;

        // ── AC Immunities ────────────────────────────────────────
        public string AuracurseImmune1 = "";
        public string AuracurseImmune2 = "";

        // ── Card Gain ────────────────────────────────────────────
        public int CardNum = 0;
        public string CardToGain = "";        // Card ID

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardToGainType = Enums.CardType.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardPlace CardPlace = Enums.CardPlace.Hand;
        public List<string> CardToGainList = new();

        // ── Cost / Economy ───────────────────────────────────────
        public bool CostZero = false;
        public int CostReduction = 0;
        public int CardsReduced = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType CardToReduceType = Enums.CardType.None;
        public int CostReduceReduction = 0;
        public int CostReduceEnergyRequirement = 0;
        public bool CostReducePermanent = false;
        public bool ReduceHighestCost = false;

        // ── Rewards / Discounts ──────────────────────────────────
        public int PercentRetentionEndGame = 0;
        public int PercentDiscountShop = 0;

        // ── Damage To Target (enchantment) ───────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageToTargetType = Enums.DamageType.None;
        public int DamageToTarget = 0;
        public bool DttMultiplyByEnergyUsed = false;
        public SpecialValueDef DttSpecialValues1;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageToTargetType2 = Enums.DamageType.None;
        public int DamageToTarget2 = 0;
        public SpecialValueDef DttSpecialValues2;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ModifiedDamageType = Enums.DamageType.None;

        // ── Flags ────────────────────────────────────────────────
        public bool CursedItem = false;
        public bool DropOnly = false;
        public bool QuestItem = false;
        public bool DestroyAfterUse = false;
        public bool Vanish = false;
        public bool Permanent = false;
        public bool DuplicateActive = false;
        public bool PassSingleAndCharacterRolls = false;
        public bool OnlyAddItemToNPCs = false;
        public bool AddVanishToDeck = false;

        // ── Enchantment ──────────────────────────────────────────
        public bool IsEnchantment = false;
        public bool UseTheNextInsteadWhenYouPlay = false;
        public int DestroyAfterUses = 0;
        public bool DestroyStartOfTurn = false;
        public bool DestroyEndOfTurn = false;
        public bool CastEnchantmentOnFinishSelfCast = false;

        // ── Custom AC ────────────────────────────────────────────
        public string AuracurseCustomString = "";
        public string AuracurseCustomAC = "";  // AC ID
        public int AuracurseCustomModValue1 = 0;
        public int AuracurseCustomModValue2 = 0;

        // ── FX / Effects ─────────────────────────────────────────
        public string EffectItemOwner = "";
        public string EffectCaster = "";
        public float EffectCasterDelay = 0f;
        public string EffectTarget = "";
        public float EffectTargetDelay = 0f;
    }

    // ───────────────────────────────────────────────────────────────
    //  LOOT TABLE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class LootDef
    {
        public string Id = "";
        public int NumItems = 1;
        public int GoldQuantity = 0;
        public bool AllowDropOnlyItems = false;
        public float PercentUncommon = 0f;
        public float PercentRare = 0f;
        public float PercentEpic = 0f;
        public float PercentMythic = 0f;
        public List<LootItemDef> Items = new();
    }

    [Serializable]
    public class LootItemDef
    {
        public string CardId = "";
        public float Percent = 0f;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardType LootType = Enums.CardType.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardRarity LootRarity = Enums.CardRarity.Common;

        public string LootMisc = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  ROAD (editor visual data)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class RoadDef
    {
        public string FromNodeId = "";
        public string ToNodeId = "";
        public List<float[]> Waypoints = new();
    }

    // ───────────────────────────────────────────────────────────────
    //  SPRITE DEFINITION (reusable visual template for NPCs)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class SpriteOverrideDef
    {
        /// <summary>Unique ID of this sprite definition.</summary>
        public string NpcId = ""; // kept as "NpcId" for JSON backward compat

        /// <summary>Base-game NPC ID providing the skeleton, animations, and default sprites.
        /// When an NpcDef.SpriteSource points to this sprite def, BaseSprite is used
        /// to clone the starting model via CopyVisuals.</summary>
        public string BaseSprite = "";
        public bool ShouldSerializeBaseSprite() => !string.IsNullOrEmpty(BaseSprite);

        public Dictionary<string, BoneOverride> Bones = new();
        public float ScaleMultiplier = 1f;
        public float OffsetX = 0f;
        public float OffsetY = 0f;

        /// <summary>DEPRECATED: Mode is ignored. All features are always available.
        /// Kept only for backward-compatible deserialization of old zone files.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public SpriteMode Mode = SpriteMode.Override;
        public bool ShouldSerializeMode() => false; // never write to JSON

        public string Spritesheet = "";
        public Dictionary<string, SpriteDef> CustomSprites = new();

        // Model-wide visual overrides
        public string ModelTintHex = "";
        public float ModelAlpha = 1f;
        public bool FlipX = false;
        public bool FlipY = false;

        // AllIn1SpriteShader effects (applied via material swap at runtime)
        public bool UseShaderEffects = false;
        public float HueShift = 0f;        // 0-1 (wraps 360°)
        public float Saturation = 1f;      // 0-2, default 1 = no change
        public float Brightness = 1f;      // 0-2, default 1 = no change
        public bool GlowEnabled = false;
        public string GlowColorHex = "#FFFFFF";
        public float GlowIntensity = 1f;   // 0-10
        public bool OutlineEnabled = false;
        public string OutlineColorHex = "#000000";
        public float OutlineSize = 1f;     // 0-10
        public float GreyscaleBlend = 0f;  // 0-1
        public float GhostTransparency = 0f; // 0-1, 0 = off

        /// <summary>Per-animation-clip keyframe overrides. Key = clip name.</summary>
        public Dictionary<string, AnimOverrideDef> AnimOverrides = new();

        /// <summary>
        /// Optional: NPC ID to source the AnimatorController from.
        /// Uses AnimatorOverrideController to wrap the source NPC's controller,
        /// keeping the state machine (idle/attack/cast/hit) but allowing clip swaps.
        /// If empty, uses the skeleton donor's (SpriteSource) original controller.
        /// </summary>
        public string AnimationSource = "";
        public bool ShouldSerializeAnimationSource() => !string.IsNullOrEmpty(AnimationSource);

        /// <summary>
        /// Sprites to add to the NPC that don't exist on the original model.
        /// Key = a unique user-chosen name for the new sprite bone.
        /// </summary>
        public Dictionary<string, AddedSpriteDef> AddedSprites = new();
        public bool ShouldSerializeAddedSprites() => AddedSprites.Count > 0;

        /// <summary>
        /// Bone names to completely remove (destroy the SpriteRenderer + SpriteSkin).
        /// More aggressive than Hidden — the bone's sprite is fully destroyed at runtime.
        /// </summary>
        public HashSet<string> RemovedBones = new();
        public bool ShouldSerializeRemovedBones() => RemovedBones.Count > 0;

        /// <summary>
        /// Rig bones to add to the NPC skeleton at runtime.
        /// These are pure transform bones (no SpriteRenderer) that can
        /// be referenced by SpriteSkin boneTransforms[] for mesh deformation.
        /// Key = unique user-chosen name for the new bone.
        /// </summary>
        public Dictionary<string, AddedBoneDef> AddedBones = new();
        public bool ShouldSerializeAddedBones() => AddedBones.Count > 0;
    }

    /// <summary>Defines a sprite to be added to the NPC at runtime on an existing parent bone.</summary>
    [Serializable]
    public class AddedSpriteDef
    {
        /// <summary>Name of the existing rig bone to attach this sprite to as a child.
        /// All other properties (source, transform, visual) are stored in
        /// SpriteOverrideDef.Bones[name] just like any existing bone.</summary>
        public string ParentBone = "";
    }

    /// <summary>Defines a pure rig bone to add to the NPC skeleton at runtime.</summary>
    [Serializable]
    public class AddedBoneDef
    {
        /// <summary>Name of the existing bone to attach this new bone to as a child.</summary>
        public string ParentBone = "";

        /// <summary>Local position relative to parent.</summary>
        public float PosX = 0f;
        public float PosY = 0f;
        /// <summary>Local rotation in degrees.</summary>
        public float Rotation = 0f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        /// <summary>Bone length (visual hint in editor, used for auto-weight radius).</summary>
        public float Length = 0.5f;

        /// <summary>
        /// Optional: list of sprite bone names that this new bone should influence.
        /// For each sprite, auto-weight will assign vertex influence based on distance.
        /// </summary>
        public List<string> InfluenceSprites = new();
        public bool ShouldSerializeInfluenceSprites() => InfluenceSprites.Count > 0;

        /// <summary>Auto-weight radius: vertices within this distance get blended influence.</summary>
        public float WeightRadius = 0.5f;
        /// <summary>Auto-weight falloff: 0=sharp cutoff, 1=linear, 2=smooth quadratic.</summary>
        public float WeightFalloff = 1f;
    }

    [Serializable]
    public class BoneOverride
    {
        public float PosX = 0f;
        public float PosY = 0f;
        public float Rotation = 0f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public bool Visible = true;
        public int SortingOffset = 0;
        public string ColorHex = "";
        public string SpriteFrom = "";
        public bool FlipX = false;
        public bool FlipY = false;
        public float Alpha = 1f;

        /// <summary>
        /// DEPRECATED: Kept for backward-compatible deserialization.
        /// Branch grafting now imports source bones directly, making manual remap unnecessary.
        /// </summary>
        public Dictionary<string, string> BoneRemap = new();
        public bool ShouldSerializeBoneRemap() => false; // never serialize
    }

    /// <summary>DEPRECATED: kept only for backward-compatible deserialization.
    /// The mode system has been removed — all features are always available.</summary>
    public enum SpriteMode { Override, Graft, CustomSprite }

    [Serializable]
    public class SpriteDef
    {
        public string ImagePath = "";
        public float[] Rect = null;
        public float PivotX = 0.5f;
        public float PivotY = 0.5f;
        public float PPU = 0f;
    }

    /// <summary>Per-clip animation override. Stores bone keyframes for one animation clip.</summary>
    [Serializable]
    public class AnimOverrideDef
    {
        public string ClipName = "";
        /// <summary>Key = bone name, Value = list of keyframes sorted by Time.</summary>
        public Dictionary<string, List<BoneKeyframe>> BoneKeyframes = new();
    }

    /// <summary>A single keyframe for a bone within an animation override.</summary>
    [Serializable]
    public class BoneKeyframe
    {
        /// <summary>Normalized time within the clip (0 = start, 1 = end).</summary>
        public float Time = 0f;
        public float PosX = 0f;
        public float PosY = 0f;
        public float Rotation = 0f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
    }
}
