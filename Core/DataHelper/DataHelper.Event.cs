using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Event Builders (MakeEvent, MakeReply, ApplyOutcome)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        /// <summary>Create an EventData with reply options.</summary>
        public static EventData MakeEvent(string id, string name, string description,
            string actionText, EventReplyData[] replies,
            Enums.CombatTier eventTier = Enums.CombatTier.T0, int replyRandom = 0)
        {
            var evt = ScriptableObject.CreateInstance<EventData>();
            evt.EventId = id;
            evt.EventName = name ?? "";
            evt.Description = description ?? "";
            evt.DescriptionAction = actionText ?? "";
            evt.Replys = replies ?? new EventReplyData[0];
            evt.EventTier = eventTier;
            evt.ReplyRandom = replyRandom;
            evt.EventUniqueId = "";
            return evt;
        }

        /// <summary>Create a fully wired EventData from an EventDef.</summary>
        public static EventData MakeFullEvent(EventDef d, EventReplyData[] replies)
        {
            var evt = MakeEvent(d.EventId, d.EventName, d.Description,
                d.DescriptionAction, replies, d.EventTier, d.ReplyRandom);
            if (!string.IsNullOrEmpty(d.EventUniqueId))
                evt.EventUniqueId = d.EventUniqueId;
            return evt;
        }

        /// <summary>Create a fully wired EventReplyData from a ReplyDef.</summary>
        public static EventReplyData MakeReply(ReplyDef r,
            System.Func<string, CombatData> getCombat = null,
            System.Func<string, EventData> getEvent = null,
            System.Func<string, NodeData> getNode = null,
            System.Func<string, LootData> getLoot = null)
        {
            var reply = new EventReplyData();
            reply.ReplyText = r.ReplyText ?? "";
            reply.ReplyActionText = r.Action;
            reply.GoldCost = r.GoldCost;
            reply.DustCost = r.DustCost;

            // Requirements
            if (!string.IsNullOrEmpty(r.RequirementId))
                reply.Requirement = GetEventRequirement(r.RequirementId);
            if (!string.IsNullOrEmpty(r.RequirementBlockedId))
                reply.RequirementBlocked = GetEventRequirement(r.RequirementBlockedId);

            // New reply-level fields
            if (!string.IsNullOrEmpty(r.RequiredClassId))
                reply.RequiredClass = GetSubClass(r.RequiredClassId);
            if (!string.IsNullOrEmpty(r.RequirementItemId))
                reply.RequirementItem = GetCard(r.RequirementItemId);
            if (r.RequirementItemIds != null && r.RequirementItemIds.Count > 0)
            {
                var items = new List<CardData>();
                foreach (var id in r.RequirementItemIds)
                {
                    var card = GetCard(id);
                    if (card != null) items.Add(card);
                }
                reply.RequirementItems = items;
            }
            if (r.RequirementCardIds != null && r.RequirementCardIds.Count > 0)
            {
                var cards = new List<CardData>();
                foreach (var id in r.RequirementCardIds)
                {
                    var card = GetCard(id);
                    if (card != null) cards.Add(card);
                }
                reply.RequirementCard = cards;
            }
            if (!string.IsNullOrEmpty(r.ReplyShowCardId))
                reply.ReplyShowCard = GetCard(r.ReplyShowCardId);
            reply.RequirementMultiplayer = r.RequirementMultiplayer;
            reply.RepeatForAllCharacters = r.RepeatForAllCharacters;
            reply.RepeatForAllWarriors = r.RepeatForAllWarriors;
            reply.RepeatForAllScouts = r.RepeatForAllScouts;
            reply.RepeatForAllMages = r.RepeatForAllMages;
            reply.RepeatForAllHealers = r.RepeatForAllHealers;

            // Initialize nullable strings not covered by ApplyOutcome
            reply.RequirementSku = "";
            reply.SsUnlockSteamAchievement = "";
            reply.FlUnlockSteamAchievement = "";
            reply.SscUnlockSteamAchievement = "";
            reply.FlcUnlockSteamAchievement = "";
            reply.SsSteamStat = "";

            // New reply-level fields from game update
            if (!string.IsNullOrEmpty(r.RequiredClassForBlockedId))
                Traverse.Create(reply).Field("requiredClassForBlocked").SetValue(GetSubClass(r.RequiredClassForBlockedId));
            if (!string.IsNullOrEmpty(r.RequirementSku))
                reply.RequirementSku = r.RequirementSku;
            if (r.ChooseReplacementHero)
                Traverse.Create(reply).Field("chooseReplacementHero").SetValue(true);

            // Roll
            if (r.HasRoll)
            {
                reply.SsRoll = true;
                reply.SsRollNumber = r.RollDC;
                reply.SsRollTarget = r.RollTarget;
                reply.SsRollMode = r.RollMode;
                reply.SsRollNumberCritical = r.RollCrit;
                reply.SsRollNumberCriticalFail = r.RollCritFail;
                if (r.RollCard != Enums.CardType.None)
                    Traverse.Create(reply).Field("ssRollCard").SetValue(r.RollCard);
            }

            // Apply all four outcome branches
            ApplyOutcome(reply, "ss", r.Ss, getCombat, getEvent, getNode, getLoot);
            ApplyOutcome(reply, "fl", r.Fl, getCombat, getEvent, getNode, getLoot);
            ApplyOutcome(reply, "ssc", r.Ssc, getCombat, getEvent, getNode, getLoot);
            ApplyOutcome(reply, "flc", r.Flc, getCombat, getEvent, getNode, getLoot);

            return reply;
        }

        /// <summary>
        /// Apply one outcome branch (Ss/Fl/Ssc/Flc) from an OutcomeDef
        /// to an EventReplyData using Traverse, keyed by camelCase prefix.
        /// </summary>
        private static void ApplyOutcome(
            EventReplyData reply, string p, OutcomeDef o,
            System.Func<string, CombatData> getCombat,
            System.Func<string, EventData> getEvent,
            System.Func<string, NodeData> getNode,
            System.Func<string, LootData> getLoot)
        {
            var t = Traverse.Create(reply);

            // Scalar values
            t.Field($"{p}RewardText").SetValue(o.Text ?? "");
            t.Field($"{p}RewardHealthPercent").SetValue(o.HealPercent);
            t.Field($"{p}RewardHealthFlat").SetValue(o.HealFlat);
            t.Field($"{p}GoldReward").SetValue(o.Gold);
            t.Field($"{p}DustReward").SetValue(o.Dust);
            t.Field($"{p}SupplyReward").SetValue(o.Supply);
            t.Field($"{p}ExperienceReward").SetValue(o.XP);
            t.Field($"{p}Discount").SetValue(o.Discount);
            t.Field($"{p}MaxQuantity").SetValue(o.MaxQuantity);
            t.Field($"{p}HealerUI").SetValue(o.HealerUI);
            t.Field($"{p}UpgradeUI").SetValue(o.UpgradeUI);
            t.Field($"{p}CraftUI").SetValue(o.CraftUI);
            t.Field($"{p}MerchantUI").SetValue(o.MerchantUI);
            t.Field($"{p}CorruptionUI").SetValue(o.CorruptionUI);
            t.Field($"{p}UpgradeRandomCard").SetValue(o.UpgradeRandomCard);

            // Fields that only exist on certain prefixes
            if (p == "ss" || p == "ssc")
                t.Field($"{p}FinishGame").SetValue(o.FinishGame);
            if (p == "ss")
                t.Field($"{p}FinishObeliskMap").SetValue(o.FinishObeliskMap);

            // New extended fields (all branches)
            t.Field($"{p}CraftUIMaxType").SetValue(o.CraftUIMaxType);
            t.Field($"{p}ItemCorruptionUI").SetValue(o.ItemCorruptionUI);
            t.Field($"{p}RemoveItemSlot").SetValue(o.RemoveItemSlot);
            t.Field($"{p}CorruptItemSlot").SetValue(o.CorruptItemSlot);
            t.Field($"{p}CardPlayerGame").SetValue(o.CardPlayerGame);
            t.Field($"{p}CardPlayerPairsGame").SetValue(o.CardPlayerPairsGame);
            if (!string.IsNullOrEmpty(o.UnlockClassId))
                t.Field($"{p}UnlockClass").SetValue(GetSubClass(o.UnlockClassId));
            if (!string.IsNullOrEmpty(o.CardPlayerGamePackId))
            {
                var pack = Globals.Instance?.GetCardPlayerPackData(NormalizeKey(o.CardPlayerGamePackId));
                if (pack != null) t.Field($"{p}CardPlayerGamePackData").SetValue(pack);
            }
            if (!string.IsNullOrEmpty(o.CardPlayerPairsGamePackId))
            {
                var pack = Globals.Instance?.GetCardPlayerPairsPackData(NormalizeKey(o.CardPlayerPairsGamePackId));
                if (pack != null) t.Field($"{p}CardPlayerPairsGamePackData").SetValue(pack);
            }
            if (!string.IsNullOrEmpty(o.UnlockSteamAchievement))
                t.Field($"{p}UnlockSteamAchievement").SetValue(o.UnlockSteamAchievement);

            // Ss-only fields
            if (p == "ss")
            {
                if (!string.IsNullOrEmpty(o.CharacterReplacementId))
                    t.Field($"{p}CharacterReplacement").SetValue(GetSubClass(o.CharacterReplacementId));
                t.Field($"{p}CharacterReplacementPosition").SetValue(o.CharacterReplacementPosition);

                if (!string.IsNullOrEmpty(o.PerkDataId))
                {
                    var perk = GetPerk(o.PerkDataId);
                    if (perk != null) t.Field($"{p}PerkData").SetValue(perk);
                }
                if (!string.IsNullOrEmpty(o.PerkData1Id))
                {
                    var perk1 = GetPerk(o.PerkData1Id);
                    if (perk1 != null) t.Field($"{p}PerkData1").SetValue(perk1);
                }
                if (!string.IsNullOrEmpty(o.SteamStat))
                    t.Field($"{p}SteamStat").SetValue(o.SteamStat);
                if (!string.IsNullOrEmpty(o.UnlockSkinId))
                {
                    var skin = GetSkin(o.UnlockSkinId);
                    if (skin != null) t.Field($"{p}UnlockSkin").SetValue(skin);
                }
            }

            // Ss and Ssc: FinishEarlyAccess
            if (p == "ss" || p == "ssc")
            {
                if (o.FinishEarlyAccess)
                    t.Field($"{p}FinishEarlyAccess").SetValue(true);
            }

            // Object references (resolved from string IDs)
            if (getCombat != null && !string.IsNullOrEmpty(o.CombatId))
                t.Field($"{p}Combat").SetValue(getCombat(o.CombatId));
            if (getEvent != null && !string.IsNullOrEmpty(o.EventId))
                t.Field($"{p}Event").SetValue(getEvent(o.EventId));
            if (getNode != null && !string.IsNullOrEmpty(o.NodeTravelId))
                t.Field($"{p}NodeTravel").SetValue(getNode(o.NodeTravelId));
            if (getLoot != null)
            {
                if (!string.IsNullOrEmpty(o.LootId)) t.Field($"{p}LootList").SetValue(getLoot(o.LootId));
                if (!string.IsNullOrEmpty(o.ShopId)) t.Field($"{p}ShopList").SetValue(getLoot(o.ShopId));
            }
            if (!string.IsNullOrEmpty(o.RequirementUnlockId))
                t.Field($"{p}RequirementUnlock").SetValue(GetEventRequirement(o.RequirementUnlockId));
            if (!string.IsNullOrEmpty(o.RequirementUnlock2Id))
                t.Field($"{p}RequirementUnlock2").SetValue(GetEventRequirement(o.RequirementUnlock2Id));
            if (!string.IsNullOrEmpty(o.RequirementLockId))
                t.Field($"{p}RequirementLock").SetValue(GetEventRequirement(o.RequirementLockId));
            if (!string.IsNullOrEmpty(o.RequirementLock2Id))
                t.Field($"{p}RequirementLock2").SetValue(GetEventRequirement(o.RequirementLock2Id));
            if (!string.IsNullOrEmpty(o.AddItemId))
                t.Field($"{p}AddItem").SetValue(GetCard(o.AddItemId));
            if (!string.IsNullOrEmpty(o.AddCard1Id))
                t.Field($"{p}AddCard1").SetValue(GetCard(o.AddCard1Id));
            if (!string.IsNullOrEmpty(o.AddCard2Id))
                t.Field($"{p}AddCard2").SetValue(GetCard(o.AddCard2Id));
            if (!string.IsNullOrEmpty(o.AddCard3Id))
                t.Field($"{p}AddCard3").SetValue(GetCard(o.AddCard3Id));
            if (!string.IsNullOrEmpty(o.RewardTier) && int.TryParse(o.RewardTier, out int tier))
                t.Field($"{p}RewardTier").SetValue(GetTierReward(tier));
        }
    }
}
