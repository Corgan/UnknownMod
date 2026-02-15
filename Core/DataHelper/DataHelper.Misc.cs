using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Misc Builders (Requirement, Cardback, TierReward,
    //               Pack, CardPlayerPack, CardPlayerPairsPack, HeroData)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        // ── Requirement Factories ────────────────────────────────────────

        /// <summary>Create an EventRequirementData SO from a RequirementDef.</summary>
        public static EventRequirementData MakeRequirement(RequirementDef d)
        {
            var req = ScriptableObject.CreateInstance<EventRequirementData>();

            req.RequirementId = d.Id;
            req.RequirementName = d.RequirementName ?? "";
            req.Description = d.Description ?? "";
            req.AssignToPlayerAtBegin = d.AssignToPlayerAtBegin;
            req.RequirementTrack = d.RequirementTrack;
            req.ItemTrack = d.ItemTrack;
            req.RequirementZoneFinishTrack = d.RequirementZoneFinishTrack;

            // Private field — alternate final act zone
            Traverse.Create(req).Field("requirementZoneFinishTrackAlternateFinalAct")
                .SetValue(d.RequirementZoneFinishTrackAlternate);

            // Resolve card reference
            if (!string.IsNullOrEmpty(d.TrackCard))
                req.TrackCard = GetCard(d.TrackCard);

            return req;
        }

        /// <summary>Snapshot an EventRequirementData SO back into a RequirementDef.</summary>
        public static RequirementDef SnapshotRequirement(EventRequirementData req)
        {
            var d = new RequirementDef();
            d.Id = req.RequirementId ?? "";
            d.RequirementName = req.RequirementName ?? "";
            d.Description = req.Description ?? "";
            d.AssignToPlayerAtBegin = req.AssignToPlayerAtBegin;
            d.RequirementTrack = req.RequirementTrack;
            d.ItemTrack = req.ItemTrack;
            d.RequirementZoneFinishTrack = req.RequirementZoneFinishTrack;

            d.RequirementZoneFinishTrackAlternate = Traverse.Create(req)
                .Field<Enums.Zone>("requirementZoneFinishTrackAlternateFinalAct").Value;

            d.TrackCard = req.TrackCard != null ? GetCardId(req.TrackCard) : "";

            return d;
        }

        // ── Cardback Factories ───────────────────────────────────────────

        /// <summary>Create a CardbackData SO from a CardbackDef.</summary>
        public static CardbackData MakeCardback(CardbackDef d)
        {
            var cb = ScriptableObject.CreateInstance<CardbackData>();

            cb.CardbackId = d.Id;
            cb.CardbackName = d.CardbackName ?? "";
            cb.CardbackTextId = d.CardbackTextId ?? "";
            cb.CardbackOrder = d.CardbackOrder;
            cb.BaseCardback = d.BaseCardback;
            cb.Locked = d.Locked;
            cb.ShowIfLocked = d.ShowIfLocked;
            cb.RankLevel = d.RankLevel;
            cb.Sku = d.Sku ?? "";
            cb.SteamStat = d.SteamStat ?? "";
            cb.AdventureLevel = d.AdventureLevel;
            cb.ObeliskLevel = d.ObeliskLevel;
            cb.SingularityLevel = d.SingularityLevel;
            cb.PdxAccountRequired = d.PdxAccountRequired;

            // Wire SubClass reference
            if (!string.IsNullOrEmpty(d.CardbackSubclass))
                cb.CardbackSubclass = GetSubClass(d.CardbackSubclass);

            // Copy sprite from source cardback
            if (!string.IsNullOrEmpty(d.SpriteSource))
            {
                var src = GetCardback(d.SpriteSource);
                if (src != null)
                    cb.CardbackSprite = src.CardbackSprite;
            }

            return cb;
        }

        /// <summary>Snapshot a CardbackData SO back into a CardbackDef for override editing.</summary>
        public static CardbackDef SnapshotCardback(CardbackData cb)
        {
            var d = new CardbackDef();
            d.Id = cb.CardbackId ?? "";
            d.CardbackName = cb.CardbackName ?? "";
            d.CardbackTextId = cb.CardbackTextId ?? "";
            d.CardbackOrder = cb.CardbackOrder;
            d.BaseCardback = cb.BaseCardback;
            d.Locked = cb.Locked;
            d.ShowIfLocked = cb.ShowIfLocked;
            d.RankLevel = cb.RankLevel;
            d.Sku = cb.Sku ?? "";
            d.SteamStat = cb.SteamStat ?? "";
            d.AdventureLevel = cb.AdventureLevel;
            d.ObeliskLevel = cb.ObeliskLevel;
            d.SingularityLevel = cb.SingularityLevel;
            d.PdxAccountRequired = cb.PdxAccountRequired;
            d.CardbackSubclass = cb.CardbackSubclass != null
                ? NormalizeKey(cb.CardbackSubclass.SubClassName)
                : "";
            d.SpriteSource = d.Id; // self as sprite source for overrides
            return d;
        }

        // ── TierReward Factories ─────────────────────────────────────────

        /// <summary>Create a TierRewardData SO from a TierRewardDef.</summary>
        public static TierRewardData MakeTierReward(TierRewardDef d)
        {
            var tr = ScriptableObject.CreateInstance<TierRewardData>();

            tr.TierNum = d.Tier;
            tr.Common = d.Common;
            tr.Uncommon = d.Uncommon;
            tr.Rare = d.Rare;
            tr.Epic = d.Epic;
            tr.Mythic = d.Mythic;
            tr.Dust = d.Dust;

            return tr;
        }

        /// <summary>Snapshot a TierRewardData SO back into a TierRewardDef for override editing.</summary>
        public static TierRewardDef SnapshotTierReward(TierRewardData tr)
        {
            var d = new TierRewardDef();
            d.Id = $"tier_{tr.TierNum}";
            d.Tier = tr.TierNum;
            d.Common = tr.Common;
            d.Uncommon = tr.Uncommon;
            d.Rare = tr.Rare;
            d.Epic = tr.Epic;
            d.Mythic = tr.Mythic;
            d.Dust = tr.Dust;
            return d;
        }

        // ── Pack (Card Reward Pack) ──────────────────────────────

        /// <summary>Create a PackData SO from a PackDef DTO.</summary>
        public static PackData MakePack(PackDef d)
        {
            var pack = ScriptableObject.CreateInstance<PackData>();
            pack.PackId = d.PackId;
            pack.PackName = d.PackName ?? "";
            pack.PackClass = d.PackClass;

            if (!string.IsNullOrEmpty(d.RequiredClassId))
                pack.RequiredClass = GetSubClass(d.RequiredClassId);

            // Card slots 0–5
            for (int i = 0; i < 6; i++)
            {
                string cardId = i < d.CardIds.Count ? d.CardIds[i] : "";
                CardData card = !string.IsNullOrEmpty(cardId) ? GetCard(cardId) : null;
                switch (i)
                {
                    case 0: pack.Card0 = card; break;
                    case 1: pack.Card1 = card; break;
                    case 2: pack.Card2 = card; break;
                    case 3: pack.Card3 = card; break;
                    case 4: pack.Card4 = card; break;
                    case 5: pack.Card5 = card; break;
                }
            }
            // Special card slots 0–1
            for (int i = 0; i < 2; i++)
            {
                string cardId = i < d.SpecialCardIds.Count ? d.SpecialCardIds[i] : "";
                CardData card = !string.IsNullOrEmpty(cardId) ? GetCard(cardId) : null;
                if (i == 0) pack.CardSpecial0 = card;
                else pack.CardSpecial1 = card;
            }
            // Perks
            if (d.PerkIds != null && d.PerkIds.Count > 0)
            {
                var perks = new List<PerkData>();
                foreach (var pid in d.PerkIds)
                {
                    var p = GetPerk(pid);
                    if (p != null) perks.Add(p);
                }
                pack.PerkList = perks;
            }
            else
            {
                pack.PerkList = new List<PerkData>();
            }
            return pack;
        }

        /// <summary>Snapshot a PackData SO back into a PackDef for editing.</summary>
        public static PackDef SnapshotPack(PackData pack)
        {
            var d = new PackDef();
            d.PackId = pack.PackId ?? "";
            d.PackName = pack.PackName ?? "";
            d.PackClass = pack.PackClass;
            d.RequiredClassId = pack.RequiredClass != null ? pack.RequiredClass.SubClassName ?? "" : "";

            d.CardIds = new List<string>();
            CardData[] cards = { pack.Card0, pack.Card1, pack.Card2, pack.Card3, pack.Card4, pack.Card5 };
            foreach (var c in cards)
                d.CardIds.Add(c != null ? c.Id ?? "" : "");
            // Trim trailing empties
            while (d.CardIds.Count > 0 && string.IsNullOrEmpty(d.CardIds[d.CardIds.Count - 1]))
                d.CardIds.RemoveAt(d.CardIds.Count - 1);

            d.SpecialCardIds = new List<string>();
            CardData[] specials = { pack.CardSpecial0, pack.CardSpecial1 };
            foreach (var c in specials)
                d.SpecialCardIds.Add(c != null ? c.Id ?? "" : "");
            while (d.SpecialCardIds.Count > 0 && string.IsNullOrEmpty(d.SpecialCardIds[d.SpecialCardIds.Count - 1]))
                d.SpecialCardIds.RemoveAt(d.SpecialCardIds.Count - 1);

            d.PerkIds = new List<string>();
            if (pack.PerkList != null)
                foreach (var p in pack.PerkList)
                    if (p != null) d.PerkIds.Add(p.Id ?? "");

            return d;
        }

        // ── CardPlayerPack ───────────────────────────────────────

        /// <summary>Create a CardPlayerPackData SO from a CardPlayerPackDef DTO.</summary>
        public static CardPlayerPackData MakeCardPlayerPack(CardPlayerPackDef d)
        {
            var pack = ScriptableObject.CreateInstance<CardPlayerPackData>();
            pack.PackId = d.PackId;
            pack.ModSpeed = d.ModSpeed;
            pack.ModIterations = d.ModIterations;

            for (int i = 0; i < 4; i++)
            {
                var slot = i < d.Slots.Count ? d.Slots[i] : null;
                CardData card = slot != null && !string.IsNullOrEmpty(slot.CardId) ? GetCard(slot.CardId) : null;
                bool boon = slot?.RandomBoon ?? false;
                bool injury = slot?.RandomInjury ?? false;
                switch (i)
                {
                    case 0: pack.Card0 = card; pack.Card0RandomBoon = boon; pack.Card0RandomInjury = injury; break;
                    case 1: pack.Card1 = card; pack.Card1RandomBoon = boon; pack.Card1RandomInjury = injury; break;
                    case 2: pack.Card2 = card; pack.Card2RandomBoon = boon; pack.Card2RandomInjury = injury; break;
                    case 3: pack.Card3 = card; pack.Card3RandomBoon = boon; pack.Card3RandomInjury = injury; break;
                }
            }
            return pack;
        }

        /// <summary>Snapshot a CardPlayerPackData SO back into a CardPlayerPackDef.</summary>
        public static CardPlayerPackDef SnapshotCardPlayerPack(CardPlayerPackData pack)
        {
            var d = new CardPlayerPackDef();
            d.PackId = pack.PackId ?? "";
            d.ModSpeed = pack.ModSpeed;
            d.ModIterations = pack.ModIterations;
            d.Slots = new List<CardPlayerSlot>();

            CardData[] cards = { pack.Card0, pack.Card1, pack.Card2, pack.Card3 };
            bool[] boons = { pack.Card0RandomBoon, pack.Card1RandomBoon, pack.Card2RandomBoon, pack.Card3RandomBoon };
            bool[] injuries = { pack.Card0RandomInjury, pack.Card1RandomInjury, pack.Card2RandomInjury, pack.Card3RandomInjury };
            for (int i = 0; i < 4; i++)
            {
                d.Slots.Add(new CardPlayerSlot
                {
                    CardId = cards[i] != null ? cards[i].Id ?? "" : "",
                    RandomBoon = boons[i],
                    RandomInjury = injuries[i]
                });
            }
            // Trim trailing empty slots
            while (d.Slots.Count > 0 && string.IsNullOrEmpty(d.Slots[d.Slots.Count - 1].CardId)
                && !d.Slots[d.Slots.Count - 1].RandomBoon && !d.Slots[d.Slots.Count - 1].RandomInjury)
                d.Slots.RemoveAt(d.Slots.Count - 1);

            return d;
        }

        // ── CardPlayerPairsPack ──────────────────────────────────

        /// <summary>Create a CardPlayerPairsPackData SO from a CardPlayerPairsPackDef DTO.</summary>
        public static CardPlayerPairsPackData MakeCardPlayerPairsPack(CardPlayerPairsPackDef d)
        {
            var pack = ScriptableObject.CreateInstance<CardPlayerPairsPackData>();
            pack.PackId = d.PackId;

            for (int i = 0; i < 6; i++)
            {
                string cardId = i < d.CardIds.Count ? d.CardIds[i] : "";
                CardData card = !string.IsNullOrEmpty(cardId) ? GetCard(cardId) : null;
                switch (i)
                {
                    case 0: pack.Card0 = card; break;
                    case 1: pack.Card1 = card; break;
                    case 2: pack.Card2 = card; break;
                    case 3: pack.Card3 = card; break;
                    case 4: pack.Card4 = card; break;
                    case 5: pack.Card5 = card; break;
                }
            }
            return pack;
        }

        /// <summary>Snapshot a CardPlayerPairsPackData SO back into a CardPlayerPairsPackDef.</summary>
        public static CardPlayerPairsPackDef SnapshotCardPlayerPairsPack(CardPlayerPairsPackData pack)
        {
            var d = new CardPlayerPairsPackDef();
            d.PackId = pack.PackId ?? "";
            d.CardIds = new List<string>();
            CardData[] cards = { pack.Card0, pack.Card1, pack.Card2, pack.Card3, pack.Card4, pack.Card5 };
            foreach (var c in cards)
                d.CardIds.Add(c != null ? c.Id ?? "" : "");
            while (d.CardIds.Count > 0 && string.IsNullOrEmpty(d.CardIds[d.CardIds.Count - 1]))
                d.CardIds.RemoveAt(d.CardIds.Count - 1);
            return d;
        }

        // ── HeroData ─────────────────────────────────────────────

        /// <summary>Create a HeroData SO from a HeroDataDef DTO.</summary>
        public static HeroData MakeHeroData(HeroDataDef d)
        {
            var hero = ScriptableObject.CreateInstance<HeroData>();
            hero.Id = d.Id;
            hero.HeroName = d.HeroName ?? "";
            hero.HeroClass = d.HeroClass;

            if (!string.IsNullOrEmpty(d.HeroSubClassId))
                hero.HeroSubClass = GetSubClass(d.HeroSubClassId);

            return hero;
        }

        /// <summary>Snapshot a HeroData SO back into a HeroDataDef for editing.</summary>
        public static HeroDataDef SnapshotHeroData(HeroData hero)
        {
            var d = new HeroDataDef();
            d.Id = hero.Id ?? "";
            d.HeroName = hero.HeroName ?? "";
            d.HeroClass = hero.HeroClass;
            d.HeroSubClassId = hero.HeroSubClass != null ? hero.HeroSubClass.SubClassName ?? "" : "";
            return d;
        }
    }
}
