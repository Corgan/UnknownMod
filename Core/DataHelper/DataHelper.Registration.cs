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

        public static void RegisterCard(CardDataNew card)
        {
            string key = NormalizeKey(card.Id);
            var g = Globals.Instance;

            // Globals.GetCardData does Split("_")[0] (instance ids are "<cardId>_<guid>"), so an
            // underscore in a card id makes it unresolvable at runtime ("Cannot find card with id X").
            if (key.Contains("_"))
                Plugin.Log.LogError($"[Register] Card id '{card.Id}' contains '_' - the game will look it up as '{key.Split('_')[0]}' and fail. Rename it (e.g. '{key.Replace("_", "")}').");

            // Remove old index entries if overriding an existing card
            if (g != null)
            {
                var oldCards = Traverse.Create(g).Field<Dictionary<string, Cards.CardRealtimeData>>("_Cards").Value;
                if (oldCards != null && oldCards.TryGetValue(key, out var old))
                    RemoveCardFromIndexes(old, g);
            }

            RegisterInDict("_CardsSource", key, card);
            var realtime = new Cards.CardRealtimeData(card, key);
            realtime.PostInit();
            RegisterInDict("_Cards", key, realtime);

            // Add to index dictionaries (mirrors Globals.CreateCardClones logic)
            if (g != null)
                AddCardToIndexes(realtime, g);

            _cardIds = null; // invalidate cache
        }

        /// <summary>Remove an id from a keyed list index without throwing when the key is absent.</summary>
        private static void RemoveId<TK>(Dictionary<TK, List<string>> index, TK key, string id)
        {
            if (index != null && index.TryGetValue(key, out var list)) list.Remove(id);
        }

        private static void RemoveCardFromIndexes(Cards.CardRealtimeData card, Globals g)
        {
            var id = card.Id;
            if (string.IsNullOrEmpty(id)) return;

            RemoveId(g.CardListByClass, card.CardClass, id);
            g.CardEnergyCost?.Remove(id);
            g.CardsDescriptionNormalized?.Remove(id);

            var types = card.GetCardTypes();
            if (types != null)
            {
                foreach (var ct in types)
                {
                    RemoveId(g.CardListByType, ct, id);
                    var classTypeKey = card.CardClass + "_" + ct;
                    if (g.CardListByClassType != null && g.CardListByClassType.ContainsKey(classTypeKey))
                        g.CardListByClassType[classTypeKey].Remove(id);
                }
            }

            if (card.CardUpgraded == Enums.CardUpgraded.No)
            {
                g.CardListNotUpgraded?.Remove(id);
                RemoveId(g.CardListNotUpgradedByClass, card.CardClass, id);
                if (card.CardClass == Enums.CardClass.Item)
                    RemoveId(g.CardItemByType, card.CardType, id);
            }
        }

        private static void AddCardToIndexes(Cards.CardRealtimeData card, Globals g)
        {
            var id = card.Id;
            g.IncludeInSearch(card.CardName, id);
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

        /// <summary>Before a mod card is dropped from _Cards on unload: remove it from the Globals card indexes and restore the vanilla card's entries.</summary>
        internal static void ReindexCardForUnload(System.Collections.IDictionary dict, string key, object original)
        {
            var g = Globals.Instance;
            if (g == null) return;
            if (dict.Contains(key) && dict[key] is Cards.CardRealtimeData current) RemoveCardFromIndexes(current, g);
            if (original is Cards.CardRealtimeData orig) AddCardToIndexes(orig, g);
        }

        public static void RegisterNPC(NPCData npc)
        {
            string key = NormalizeKey(npc.Id);
            RegisterInDict("_NPCsSource", key, npc);
            RegisterInDict("_NPCs", key, Object.Instantiate(npc));
            // The game picks random named NPCs (e.g. for events) from _NPCsNamed
            if (npc.IsNamed && npc.Difficulty > -1)
                RegisterInDict("_NPCsNamed", key, Object.Instantiate(npc));
            else
                UnnameNpc(key);
            _npcIds = null; // invalidate cache
            _npcCache.Remove(key); // invalidate lookup cache
        }

        /// <summary>A patch that un-names an NPC must also leave the named pool; the original is restored on cleanup.</summary>
        private static void UnnameNpc(string key)
        {
            var dict = Traverse.Create(Globals.Instance).Field("_NPCsNamed").GetValue() as IDictionary<string, NPCData>;
            if (dict == null || !dict.TryGetValue(key, out var existing)) return;
            ModRegistry.TrackRegistration("_NPCsNamed", key, existing);
            dict.Remove(key);
        }

        public static void RegisterCombat(CombatData combat)
        {
            RegisterInDict("_CombatDataSource", NormalizeKey(combat.CombatId), combat);
            _combatIds = null; // invalidate cache
        }

        public static void RegisterEvent(EventData evt)
        {
            RegisterInDict("_Events", NormalizeKey(evt.EventId), evt);
            _eventIds = null; // invalidate cache
            RegisterEventTexts(evt);
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
            RegisterZoneTexts(zone);
        }

        public static void RegisterItem(ItemData item)
        {
            RegisterInDict("_ItemDataSource", NormalizeKey(item.Id), item);
            _itemIds = null; // invalidate cache
        }

        public static void RegisterAuraCurse(AuraCurseData ac)
        {
            ac.Init();
            string key = NormalizeKey(Traverse.Create(ac).Field<string>("id").Value);
            if (string.IsNullOrEmpty(key)) return;
            // The game reads auras from _AurasCurses (lookup) and _AurasCursesIndex (id <-> index), not only the Source dict
            RegisterInDict("_AurasCursesSource", key, ac);
            RegisterInDict("_AurasCurses", key, ac);
            var index = Traverse.Create(Globals.Instance).Field<List<string>>("_AurasCursesIndex").Value;
            if (index != null && !index.Contains(key))
            {
                index.Add(key);
                ModRegistry.TrackAuraIndex(key);
            }
            _auraCurseIds = null; // invalidate cache
            _acCache.Remove(key); // invalidate lookup cache
            RegisterAuraTexts(ac);
            UnknownMod.Runtime.TmpSpriteRegistry.RegisterAuraIcon(key, ac.ACName, ac.Sprite);
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
            RegisterHeroTexts(sc);
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
            // The game dereferences SkinSubclass for every registered skin during boot (Globals.GetSkinBaseIdBySubclass).
            if (skin.SkinSubclass == null)
            {
                Plugin.Log.LogError($"[DataHelper] Skin '{skin.SkinId}' has no resolvable SkinSubclass; not registering it (it would crash game boot).");
                return;
            }
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
            RegisterInDict("_Requirements", key, req);
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
                dict.TryGetValue(tr.TierNum, out var existing);
                dict[tr.TierNum] = tr;
                ModRegistry.TrackTierRewardRegistration(tr.TierNum, existing);
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
