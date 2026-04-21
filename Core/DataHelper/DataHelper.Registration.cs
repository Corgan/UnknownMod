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
            var g = Globals.Instance;

            // Remove old index entries if overriding an existing card
            if (g != null)
            {
                var oldCards = Traverse.Create(g).Field<Dictionary<string, CardData>>("_Cards").Value;
                if (oldCards != null && oldCards.TryGetValue(key, out var old))
                    RemoveCardFromIndexes(old, g);
            }

            RegisterInDict("_CardsSource", key, card);
            RegisterInDict("_Cards", key, card);

            // Add to index dictionaries (mirrors Globals.CreateCardClones logic)
            if (g != null)
                AddCardToIndexes(card, g);

            _cardIds = null; // invalidate cache
        }

        private static void RemoveCardFromIndexes(CardData card, Globals g)
        {
            var id = card.Id;
            if (string.IsNullOrEmpty(id)) return;

            g.CardListByClass?[card.CardClass]?.Remove(id);
            g.CardEnergyCost?.Remove(id);
            g.CardsDescriptionNormalized?.Remove(id);

            var types = card.GetCardTypes();
            if (types != null)
            {
                foreach (var ct in types)
                {
                    g.CardListByType?[ct]?.Remove(id);
                    var classTypeKey = card.CardClass + "_" + ct;
                    if (g.CardListByClassType != null && g.CardListByClassType.ContainsKey(classTypeKey))
                        g.CardListByClassType[classTypeKey].Remove(id);
                }
            }

            if (card.CardUpgraded == Enums.CardUpgraded.No)
            {
                g.CardListNotUpgraded?.Remove(id);
                g.CardListNotUpgradedByClass?[card.CardClass]?.Remove(id);
                if (card.CardClass == Enums.CardClass.Item)
                    g.CardItemByType?[card.CardType]?.Remove(id);
            }
        }

        private static void AddCardToIndexes(CardData card, Globals g)
        {
            var id = card.Id;
            // Skip quest items and cards hidden from tome (matches CreateCardClones filter)
            if ((card.CardClass == Enums.CardClass.Item && card.Item != null && card.Item.QuestItem) || !card.ShowInTome)
                return;

            if (g.CardEnergyCost != null) g.CardEnergyCost[id] = card.EnergyCost;
            g.CardListByClass?[card.CardClass]?.Add(id);

            if (card.CardUpgraded == Enums.CardUpgraded.No)
            {
                g.CardListNotUpgradedByClass?[card.CardClass]?.Add(id);
                g.CardListNotUpgraded?.Add(id);
                if (card.CardClass == Enums.CardClass.Item)
                {
                    if (g.CardItemByType != null)
                    {
                        if (!g.CardItemByType.ContainsKey(card.CardType))
                            g.CardItemByType[card.CardType] = new List<string>();
                        g.CardItemByType[card.CardType].Add(id);
                    }
                }
            }

            var types = card.GetCardTypes();
            if (types != null && g.CardListByType != null)
            {
                foreach (var ct in types)
                {
                    if (g.CardListByType.ContainsKey(ct))
                        g.CardListByType[ct].Add(id);
                    var classTypeKey = card.CardClass + "_" + ct;
                    if (g.CardListByClassType == null) continue;
                    if (!g.CardListByClassType.ContainsKey(classTypeKey))
                        g.CardListByClassType[classTypeKey] = new List<string>();
                    g.CardListByClassType[classTypeKey].Add(id);
                }
            }
        }

        public static void RegisterNPC(NPCData npc)
        {
            string key = NormalizeKey(npc.Id);
            RegisterInDict("_NPCsSource", key, npc);
            RegisterInDict("_NPCs", key, Object.Instantiate(npc));
            _npcIds = null; // invalidate cache
            _npcCache.Remove(key); // invalidate lookup cache
        }

        public static void RegisterCombat(CombatData combat)
        {
            RegisterInDict("_CombatDataSource", NormalizeKey(combat.CombatId), combat);
            // No cached list for combats currently
        }

        public static void RegisterEvent(EventData evt)
        {
            RegisterInDict("_Events", NormalizeKey(evt.EventId), evt);
            _eventIds = null; // invalidate cache
        }

        public static void RegisterNode(NodeData node)
        {
            RegisterInDict("_NodeDataSource", NormalizeKey(node.NodeId), node);
            // No cached list for nodes currently
        }

        public static void RegisterZone(ZoneData zone)
        {
            RegisterInDict("_ZoneDataSource", NormalizeKey(zone.ZoneId), zone);
            _zoneIds = null; // invalidate cache
        }

        public static void RegisterItem(ItemData item)
        {
            RegisterInDict("_ItemDataSource", NormalizeKey(item.Id), item);
            _itemIds = null; // invalidate cache
        }

        public static void RegisterAuraCurse(AuraCurseData ac)
        {
            string key = NormalizeKey(Traverse.Create(ac).Field<string>("id").Value);
            if (string.IsNullOrEmpty(key)) return;
            RegisterInDict("_AurasCursesSource", key, ac);
            _auraCurseIds = null; // invalidate cache
            _acCache.Remove(key); // invalidate lookup cache
        }

        public static void RegisterLoot(LootData loot)
        {
            RegisterInDict("_LootDataSource", NormalizeKey(loot.Id), loot);
            _lootIds = null; // invalidate cache
        }

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
            if (dict != null)
            {
                dict[tr.TierNum] = tr;
                ModRegistry.TrackTierRewardRegistration(tr.TierNum);
            }
            _tierRewardTiers = null; // invalidate cache
        }

        public static void RegisterPack(PackData pack)
        {
            RegisterInDict("_PackDataSource", NormalizeKey(pack.PackId), pack);
            _packIds = null; // invalidate cache
        }

        public static void RegisterCardPlayerPack(CardPlayerPackData pack)
        {
            RegisterInDict("_CardPlayerPackDataSource", NormalizeKey(pack.PackId), pack);
            _cardPlayerPackIds = null; // invalidate cache
        }

        public static void RegisterCardPlayerPairsPack(CardPlayerPairsPackData pack)
        {
            RegisterInDict("_CardPlayerPairsPackDataSource", NormalizeKey(pack.PackId), pack);
            _cardPlayerPairsPackIds = null; // invalidate cache
        }

        public static void RegisterHeroData(HeroData hero)
        {
            RegisterInDict("_Heroes", NormalizeKey(hero.Id), hero);
            _heroDataIds = null; // invalidate cache
        }
    }
}
