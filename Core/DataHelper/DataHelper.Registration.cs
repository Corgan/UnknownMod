using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Registration (register SOs into Globals dicts)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        /// <summary>
        /// Normalize a registration/lookup key: trim, strip spaces, lowercase.
        /// All Register and Get methods should use this consistently.
        /// </summary>
        public static string NormalizeKey(string key)
            => string.IsNullOrEmpty(key) ? "" : key.Trim().Replace(" ", "").ToLower();

        public static void RegisterCard(CardData card)
        {
            string key = NormalizeKey(card.Id);
            RegisterInDict("_CardsSource", key, card);
            RegisterInDict("_Cards", key, card);
        }

        public static void RegisterNPC(NPCData npc)
        {
            string key = NormalizeKey(npc.Id);
            RegisterInDict("_NPCsSource", key, npc);
            RegisterInDict("_NPCs", key, Object.Instantiate(npc));
        }

        public static void RegisterCombat(CombatData combat)
            => RegisterInDict("_CombatDataSource", NormalizeKey(combat.CombatId), combat);

        public static void RegisterEvent(EventData evt)
            => RegisterInDict("_Events", NormalizeKey(evt.EventId), evt);

        public static void RegisterNode(NodeData node)
            => RegisterInDict("_NodeDataSource", NormalizeKey(node.NodeId), node);

        public static void RegisterZone(ZoneData zone)
            => RegisterInDict("_ZoneDataSource", NormalizeKey(zone.ZoneId), zone);

        public static void RegisterItem(ItemData item)
            => RegisterInDict("_ItemDataSource", NormalizeKey(item.Id), item);

        public static void RegisterLoot(LootData loot)
            => RegisterInDict("_LootDataSource", NormalizeKey(loot.Id), loot);

        public static void RegisterHero(SubClassData sc)
        {
            string key = NormalizeKey(sc.SubClassName);
            RegisterInDict("_SubClassSource", key, sc);
            RegisterInDict("_SubClass", key, Object.Instantiate(sc));
            _subClassIds = null; // invalidate cache
        }

        public static void RegisterTrait(TraitData trait)
        {
            string key = NormalizeKey(trait.Id);
            RegisterInDict("_TraitsSource", key, trait);
            RegisterInDict("_Traits", key, Object.Instantiate(trait));
            _traitIds = null; // invalidate cache
        }

        public static void RegisterSkin(SkinData skin)
        {
            string key = NormalizeKey(skin.SkinId);
            RegisterInDict("_SkinDataSource", key, skin);
            _skinIds = null; // invalidate cache
        }

        public static void RegisterPerk(PerkData perk)
        {
            string key = NormalizeKey(perk.Id);
            RegisterInDict("_PerksSource", key, perk);
            _perkIds = null; // invalidate cache
        }

        public static void RegisterPerkNode(PerkNodeData node)
        {
            string key = NormalizeKey(node.Id);
            RegisterInDict("_PerksNodesSource", key, node);
            _perkNodeIds = null; // invalidate cache
        }

        public static void RegisterRequirement(EventRequirementData req)
        {
            string key = NormalizeKey(req.RequirementId);
            RegisterInDict("_EventRequirementSource", key, req);
            _eventReqIds = null; // invalidate cache
        }

        public static void RegisterCardback(CardbackData cb)
        {
            string key = NormalizeKey(cb.CardbackId);
            RegisterInDict("_CardbackDataSource", key, cb);
            _cardbackIds = null; // invalidate cache
        }

        public static void RegisterTierReward(TierRewardData tr)
        {
            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<int, TierRewardData>>("_TierRewardDataSource").Value;
            if (dict != null) dict[tr.TierNum] = tr;
            _tierRewardTiers = null; // invalidate cache
        }

        public static void RegisterPack(PackData pack)
            => RegisterInDict("_PackDataSource", NormalizeKey(pack.PackId), pack);

        public static void RegisterCardPlayerPack(CardPlayerPackData pack)
            => RegisterInDict("_CardPlayerPackDataSource", NormalizeKey(pack.PackId), pack);

        public static void RegisterCardPlayerPairsPack(CardPlayerPairsPackData pack)
            => RegisterInDict("_CardPlayerPairsPackDataSource", NormalizeKey(pack.PackId), pack);

        public static void RegisterHeroData(HeroData hero)
            => RegisterInDict("_Heroes", NormalizeKey(hero.Id), hero);
    }
}
