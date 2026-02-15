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
        public static void RegisterCard(CardData card)
        {
            string key = card.Id.ToLower();
            RegisterInDict("_CardsSource", key, card);
            RegisterInDict("_Cards", key, card);
        }

        public static void RegisterNPC(NPCData npc)
        {
            string key = npc.Id.ToLower();
            RegisterInDict("_NPCsSource", key, npc);
            RegisterInDict("_NPCs", key, Object.Instantiate(npc));
        }

        public static void RegisterCombat(CombatData combat)
            => RegisterInDict("_CombatDataSource", combat.CombatId.Replace(" ", "").ToLower(), combat);

        public static void RegisterEvent(EventData evt)
            => RegisterInDict("_Events", evt.EventId.ToLower(), evt);

        public static void RegisterNode(NodeData node)
            => RegisterInDict("_NodeDataSource", node.NodeId.ToLower(), node);

        public static void RegisterZone(ZoneData zone)
            => RegisterInDict("_ZoneDataSource", zone.ZoneId.ToLower(), zone);

        public static void RegisterItem(ItemData item)
            => RegisterInDict("_ItemDataSource", item.Id.ToLower(), item);

        public static void RegisterLoot(LootData loot)
            => RegisterInDict("_LootDataSource", loot.Id.ToLower(), loot);

        public static void RegisterHero(SubClassData sc)
        {
            string key = sc.SubClassName.Replace(" ", "").ToLower();
            RegisterInDict("_SubClassSource", key, sc);
            RegisterInDict("_SubClass", key, Object.Instantiate(sc));
            _subClassIds = null; // invalidate cache
        }

        public static void RegisterTrait(TraitData trait)
        {
            string key = trait.Id.ToLower();
            RegisterInDict("_TraitsSource", key, trait);
            RegisterInDict("_Traits", key, Object.Instantiate(trait));
            _traitIds = null; // invalidate cache
        }

        public static void RegisterSkin(SkinData skin)
        {
            string key = skin.SkinId.ToLower();
            RegisterInDict("_SkinDataSource", key, skin);
            _skinIds = null; // invalidate cache
        }

        public static void RegisterPerk(PerkData perk)
        {
            string key = perk.Id.ToLower();
            RegisterInDict("_PerksSource", key, perk);
            _perkIds = null; // invalidate cache
        }

        public static void RegisterPerkNode(PerkNodeData node)
        {
            string key = node.Id.ToLower();
            RegisterInDict("_PerksNodesSource", key, node);
            _perkNodeIds = null; // invalidate cache
        }

        public static void RegisterRequirement(EventRequirementData req)
        {
            string key = req.RequirementId.ToLower();
            RegisterInDict("_EventRequirementSource", key, req);
            _eventReqIds = null; // invalidate cache
        }

        public static void RegisterCardback(CardbackData cb)
        {
            string key = cb.CardbackId.ToLower();
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
            => RegisterInDict("_PackDataSource", pack.PackId.ToLower(), pack);

        public static void RegisterCardPlayerPack(CardPlayerPackData pack)
            => RegisterInDict("_CardPlayerPackDataSource", pack.PackId.ToLower(), pack);

        public static void RegisterCardPlayerPairsPack(CardPlayerPairsPackData pack)
            => RegisterInDict("_CardPlayerPairsPackDataSource", pack.PackId.ToLower(), pack);

        public static void RegisterHeroData(HeroData hero)
            => RegisterInDict("_Heroes", hero.Id.ToLower(), hero);
    }
}
