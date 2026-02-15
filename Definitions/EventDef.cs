using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnknownMod.Definitions
{

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //  EVENT
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        /// <summary>SubClass ID â€” limits this event to a specific hero class.</summary>
        public string RequiredClassId = "";
        public bool ShouldSerializeRequiredClassId() => !string.IsNullOrEmpty(RequiredClassId);

        /// <summary>Map icon shader used for this event node.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.MapIconShader EventIconShader = Enums.MapIconShader.None;
        public bool ShouldSerializeEventIconShader() => EventIconShader != Enums.MapIconShader.None;

        /// <summary>Whether this event is a history-mode event.</summary>
        public bool HistoryMode = false;
        public bool ShouldSerializeHistoryMode() => HistoryMode;

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

        /// <summary>Max card rarity allowed in craft UI.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardRarity CraftUIMaxType = Enums.CardRarity.Common;
        public bool ShouldSerializeCraftUIMaxType() => CraftUI && CraftUIMaxType != Enums.CardRarity.Common;

        /// <summary>Show item corruption UI.</summary>
        public bool ItemCorruptionUI = false;
        public bool ShouldSerializeItemCorruptionUI() => ItemCorruptionUI;

        /// <summary>Remove an item from the specified slot.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ItemSlot RemoveItemSlot = Enums.ItemSlot.None;
        public bool ShouldSerializeRemoveItemSlot() => RemoveItemSlot != Enums.ItemSlot.None;

        /// <summary>Corrupt an item in the specified slot.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.ItemSlot CorruptItemSlot = Enums.ItemSlot.None;
        public bool ShouldSerializeCorruptItemSlot() => CorruptItemSlot != Enums.ItemSlot.None;

        /// <summary>SubClass ID to unlock as a reward.</summary>
        public string UnlockClassId = "";
        public bool ShouldSerializeUnlockClassId() => !string.IsNullOrEmpty(UnlockClassId);

        /// <summary>Trigger the card player minigame.</summary>
        public bool CardPlayerGame = false;
        public bool ShouldSerializeCardPlayerGame() => CardPlayerGame;

        /// <summary>CardPlayerPackData ID for the minigame.</summary>
        public string CardPlayerGamePackId = "";
        public bool ShouldSerializeCardPlayerGamePackId() => !string.IsNullOrEmpty(CardPlayerGamePackId);

        /// <summary>Trigger the pairs card minigame.</summary>
        public bool CardPlayerPairsGame = false;
        public bool ShouldSerializeCardPlayerPairsGame() => CardPlayerPairsGame;

        /// <summary>PairsPackData ID for the pairs minigame.</summary>
        public string CardPlayerPairsGamePackId = "";
        public bool ShouldSerializeCardPlayerPairsGamePackId() => !string.IsNullOrEmpty(CardPlayerPairsGamePackId);

        /// <summary>Steam achievement ID to unlock.</summary>
        public string UnlockSteamAchievement = "";
        public bool ShouldSerializeUnlockSteamAchievement() => !string.IsNullOrEmpty(UnlockSteamAchievement);

        /// <summary>Character replacement (Ss-only in game). SubClass ID.</summary>
        public string CharacterReplacementId = "";
        public bool ShouldSerializeCharacterReplacementId() => !string.IsNullOrEmpty(CharacterReplacementId);

        /// <summary>Position index for character replacement (Ss-only).</summary>
        public int CharacterReplacementPosition = 0;
        public bool ShouldSerializeCharacterReplacementPosition() => !string.IsNullOrEmpty(CharacterReplacementId);
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

        /// <summary>SubClass ID â€” only show this reply for a specific hero class.</summary>
        public string RequiredClassId = "";
        public bool ShouldSerializeRequiredClassId() => !string.IsNullOrEmpty(RequiredClassId);

        /// <summary>Item card ID required to show this reply.</summary>
        public string RequirementItemId = "";
        public bool ShouldSerializeRequirementItemId() => !string.IsNullOrEmpty(RequirementItemId);

        /// <summary>Additional item card IDs required (multi-item check).</summary>
        public List<string> RequirementItemIds = new();
        public bool ShouldSerializeRequirementItemIds() => RequirementItemIds.Count > 0;

        /// <summary>Card IDs required to show this reply.</summary>
        public List<string> RequirementCardIds = new();
        public bool ShouldSerializeRequirementCardIds() => RequirementCardIds.Count > 0;

        /// <summary>Card ID shown alongside this reply option.</summary>
        public string ReplyShowCardId = "";
        public bool ShouldSerializeReplyShowCardId() => !string.IsNullOrEmpty(ReplyShowCardId);

        /// <summary>Only available in multiplayer.</summary>
        public bool RequirementMultiplayer = false;
        public bool ShouldSerializeRequirementMultiplayer() => RequirementMultiplayer;

        // Repeat for all heroes/classes
        public bool RepeatForAllCharacters = false;
        public bool ShouldSerializeRepeatForAllCharacters() => RepeatForAllCharacters;
        public bool RepeatForAllWarriors = false;
        public bool ShouldSerializeRepeatForAllWarriors() => RepeatForAllWarriors;
        public bool RepeatForAllScouts = false;
        public bool ShouldSerializeRepeatForAllScouts() => RepeatForAllScouts;
        public bool RepeatForAllMages = false;
        public bool ShouldSerializeRepeatForAllMages() => RepeatForAllMages;
        public bool RepeatForAllHealers = false;
        public bool ShouldSerializeRepeatForAllHealers() => RepeatForAllHealers;

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

        // â”€â”€ Outcome branches â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

            // New reply-level fields
            r.RequiredClassId = (string)jo["RequiredClassId"] ?? "";
            r.RequirementItemId = (string)jo["RequirementItemId"] ?? "";
            r.RequirementItemIds = jo["RequirementItemIds"]?.ToObject<List<string>>(serializer) ?? new List<string>();
            r.RequirementCardIds = jo["RequirementCardIds"]?.ToObject<List<string>>(serializer) ?? new List<string>();
            r.ReplyShowCardId = (string)jo["ReplyShowCardId"] ?? "";
            r.RequirementMultiplayer = (bool?)jo["RequirementMultiplayer"] ?? false;
            r.RepeatForAllCharacters = (bool?)jo["RepeatForAllCharacters"] ?? false;
            r.RepeatForAllWarriors = (bool?)jo["RepeatForAllWarriors"] ?? false;
            r.RepeatForAllScouts = (bool?)jo["RepeatForAllScouts"] ?? false;
            r.RepeatForAllMages = (bool?)jo["RepeatForAllMages"] ?? false;
            r.RepeatForAllHealers = (bool?)jo["RepeatForAllHealers"] ?? false;

            ReadOutcome(jo, "Ss", r.Ss, serializer);
            ReadOutcome(jo, "Fl", r.Fl, serializer);
            ReadOutcome(jo, "Ssc", r.Ssc, serializer);
            ReadOutcome(jo, "Flc", r.Flc, serializer);

            // Fields that only exist on certain prefixes
            r.Ss.FinishGame = (bool?)jo["SsFinishGame"] ?? false;
            r.Ss.FinishObeliskMap = (bool?)jo["SsFinishObeliskMap"] ?? false;
            r.Ssc.FinishGame = (bool?)jo["SscFinishGame"] ?? false;

            // Ss-only outcome fields
            r.Ss.CharacterReplacementId = (string)jo["SsCharacterReplacementId"] ?? "";
            r.Ss.CharacterReplacementPosition = (int?)jo["SsCharacterReplacementPosition"] ?? 0;

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

            // New reply-level fields
            writer.WritePropertyName("RequiredClassId"); writer.WriteValue(r.RequiredClassId);
            writer.WritePropertyName("RequirementItemId"); writer.WriteValue(r.RequirementItemId);
            if (r.RequirementItemIds != null && r.RequirementItemIds.Count > 0)
            { writer.WritePropertyName("RequirementItemIds"); serializer.Serialize(writer, r.RequirementItemIds); }
            if (r.RequirementCardIds != null && r.RequirementCardIds.Count > 0)
            { writer.WritePropertyName("RequirementCardIds"); serializer.Serialize(writer, r.RequirementCardIds); }
            writer.WritePropertyName("ReplyShowCardId"); writer.WriteValue(r.ReplyShowCardId);
            writer.WritePropertyName("RequirementMultiplayer"); writer.WriteValue(r.RequirementMultiplayer);
            writer.WritePropertyName("RepeatForAllCharacters"); writer.WriteValue(r.RepeatForAllCharacters);
            writer.WritePropertyName("RepeatForAllWarriors"); writer.WriteValue(r.RepeatForAllWarriors);
            writer.WritePropertyName("RepeatForAllScouts"); writer.WriteValue(r.RepeatForAllScouts);
            writer.WritePropertyName("RepeatForAllMages"); writer.WriteValue(r.RepeatForAllMages);
            writer.WritePropertyName("RepeatForAllHealers"); writer.WriteValue(r.RepeatForAllHealers);

            WriteOutcome(writer, "Ss", r.Ss, serializer);
            writer.WritePropertyName("SsFinishGame"); writer.WriteValue(r.Ss.FinishGame);
            writer.WritePropertyName("SsFinishObeliskMap"); writer.WriteValue(r.Ss.FinishObeliskMap);
            writer.WritePropertyName("SsCharacterReplacementId"); writer.WriteValue(r.Ss.CharacterReplacementId);
            writer.WritePropertyName("SsCharacterReplacementPosition"); writer.WriteValue(r.Ss.CharacterReplacementPosition);

            WriteOutcome(writer, "Fl", r.Fl, serializer);

            WriteOutcome(writer, "Ssc", r.Ssc, serializer);
            writer.WritePropertyName("SscFinishGame"); writer.WriteValue(r.Ssc.FinishGame);

            WriteOutcome(writer, "Flc", r.Flc, serializer);

            writer.WriteEndObject();
        }

        private static void ReadOutcome(JObject jo, string p, OutcomeDef o, JsonSerializer serializer)
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

            // New outcome fields
            o.CraftUIMaxType = jo[$"{p}CraftUIMaxType"]?.ToObject<Enums.CardRarity>(serializer) ?? Enums.CardRarity.Common;
            o.ItemCorruptionUI = (bool?)jo[$"{p}ItemCorruptionUI"] ?? false;
            o.RemoveItemSlot = jo[$"{p}RemoveItemSlot"]?.ToObject<Enums.ItemSlot>(serializer) ?? Enums.ItemSlot.None;
            o.CorruptItemSlot = jo[$"{p}CorruptItemSlot"]?.ToObject<Enums.ItemSlot>(serializer) ?? Enums.ItemSlot.None;
            o.UnlockClassId = (string)jo[$"{p}UnlockClassId"] ?? "";
            o.CardPlayerGame = (bool?)jo[$"{p}CardPlayerGame"] ?? false;
            o.CardPlayerGamePackId = (string)jo[$"{p}CardPlayerGamePackId"] ?? "";
            o.CardPlayerPairsGame = (bool?)jo[$"{p}CardPlayerPairsGame"] ?? false;
            o.CardPlayerPairsGamePackId = (string)jo[$"{p}CardPlayerPairsGamePackId"] ?? "";
            o.UnlockSteamAchievement = (string)jo[$"{p}UnlockSteamAchievement"] ?? "";
        }

        private static void WriteOutcome(JsonWriter writer, string p, OutcomeDef o, JsonSerializer serializer)
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

            // New outcome fields
            writer.WritePropertyName($"{p}CraftUIMaxType"); serializer.Serialize(writer, o.CraftUIMaxType);
            writer.WritePropertyName($"{p}ItemCorruptionUI"); writer.WriteValue(o.ItemCorruptionUI);
            writer.WritePropertyName($"{p}RemoveItemSlot"); serializer.Serialize(writer, o.RemoveItemSlot);
            writer.WritePropertyName($"{p}CorruptItemSlot"); serializer.Serialize(writer, o.CorruptItemSlot);
            writer.WritePropertyName($"{p}UnlockClassId"); writer.WriteValue(o.UnlockClassId);
            writer.WritePropertyName($"{p}CardPlayerGame"); writer.WriteValue(o.CardPlayerGame);
            writer.WritePropertyName($"{p}CardPlayerGamePackId"); writer.WriteValue(o.CardPlayerGamePackId);
            writer.WritePropertyName($"{p}CardPlayerPairsGame"); writer.WriteValue(o.CardPlayerPairsGame);
            writer.WritePropertyName($"{p}CardPlayerPairsGamePackId"); writer.WriteValue(o.CardPlayerPairsGamePackId);
            writer.WritePropertyName($"{p}UnlockSteamAchievement"); writer.WriteValue(o.UnlockSteamAchievement);
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //  NPC
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

}
