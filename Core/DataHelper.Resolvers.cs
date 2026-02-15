using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Resolvers (cached ID lists + single-entity lookups)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        // ── Cached ID List Resolvers ─────────────────────────────

        /// <summary>Sorted list of all base-game AuraCurse IDs.</summary>
        public static List<string> GetAllAuraCurseIds()
        {
            if (_auraCurseIds != null) return _auraCurseIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, AuraCurseData>>("_AurasCursesSource").Value;
            _auraCurseIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _auraCurseIds;
        }

        /// <summary>Sorted list of all base-game EventRequirement IDs.</summary>
        public static List<string> GetAllEventRequirementIds()
        {
            if (_eventReqIds != null) return _eventReqIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, EventRequirementData>>("_EventRequirementSource").Value;
            _eventReqIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _eventReqIds;
        }

        /// <summary>Sorted list of all base-game NPC IDs.</summary>
        public static List<string> GetAllNpcIds()
        {
            if (_npcIds != null) return _npcIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, NPCData>>("_NPCsSource").Value;
            _npcIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _npcIds;
        }

        /// <summary>Sorted list of all base-game Card IDs.</summary>
        public static List<string> GetAllCardIds()
        {
            if (_cardIds != null) return _cardIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CardData>>("_CardsSource").Value;
            _cardIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _cardIds;
        }

        /// <summary>Sorted list of all base-game Item IDs.</summary>
        public static List<string> GetAllItemIds()
        {
            if (_itemIds != null) return _itemIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ItemData>>("_ItemDataSource").Value;
            _itemIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _itemIds;
        }

        /// <summary>Sorted list of all base-game SubClass IDs.</summary>
        public static List<string> GetAllSubClassIds()
        {
            if (_subClassIds != null) return _subClassIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SubClassData>>("_SubClassSource").Value;
            _subClassIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _subClassIds;
        }

        /// <summary>Sorted list of all base-game Trait IDs.</summary>
        public static List<string> GetAllTraitIds()
        {
            if (_traitIds != null) return _traitIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, TraitData>>("_TraitsSource").Value;
            _traitIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _traitIds;
        }

        /// <summary>Sorted list of all base-game Perk IDs.</summary>
        public static List<string> GetAllPerkIds()
        {
            if (_perkIds != null) return _perkIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PerkData>>("_PerksSource").Value;
            _perkIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _perkIds;
        }

        /// <summary>Sorted list of all base-game PerkNode IDs.</summary>
        public static List<string> GetAllPerkNodeIds()
        {
            if (_perkNodeIds != null) return _perkNodeIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PerkNodeData>>("_PerksNodesSource").Value;
            _perkNodeIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _perkNodeIds;
        }

        /// <summary>Sorted list of all base-game Skin IDs.</summary>
        public static List<string> GetAllSkinIds()
        {
            if (_skinIds != null) return _skinIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SkinData>>("_SkinDataSource").Value;
            _skinIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _skinIds;
        }

        /// <summary>Sorted list of all base-game Cardback IDs.</summary>
        public static List<string> GetAllCardbackIds()
        {
            if (_cardbackIds != null) return _cardbackIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CardbackData>>("_CardbackDataSource").Value;
            _cardbackIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _cardbackIds;
        }

        /// <summary>Sorted list of all base-game TierReward tier numbers.</summary>
        public static List<int> GetAllTierRewardTiers()
        {
            if (_tierRewardTiers != null) return _tierRewardTiers;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<int, TierRewardData>>("_TierRewardDataSource").Value;
            _tierRewardTiers = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<int>();
            return _tierRewardTiers;
        }

        /// <summary>Sorted list of all base-game Zone IDs.</summary>
        public static List<string> GetAllZoneIds()
        {
            if (_zoneIds != null) return _zoneIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ZoneData>>("_ZoneDataSource").Value;
            _zoneIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _zoneIds;
        }

        public static List<string> GetAllLootIds()
        {
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, LootData>>("_LootDataSource").Value;
            return dict != null ? dict.Keys.ToList() : new List<string>();
        }

        /// <summary>Sorted list of all base-game Event IDs.</summary>
        public static List<string> GetAllEventIds()
        {
            if (_eventIds != null) return _eventIds;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, EventData>>("_Events").Value;
            _eventIds = dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
            return _eventIds;
        }

        public static List<string> GetAllPackIds()
        {
            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, PackData>>("_PackDataSource").Value;
            return dict != null ? new List<string>(dict.Keys) : new List<string>();
        }

        public static List<string> GetAllCardPlayerPackIds()
        {
            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, CardPlayerPackData>>("_CardPlayerPackDataSource").Value;
            return dict != null ? new List<string>(dict.Keys) : new List<string>();
        }

        public static List<string> GetAllCardPlayerPairsPackIds()
        {
            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, CardPlayerPairsPackData>>("_CardPlayerPairsPackDataSource").Value;
            return dict != null ? new List<string>(dict.Keys) : new List<string>();
        }

        public static List<string> GetAllHeroDataIds()
        {
            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, HeroData>>("_Heroes").Value;
            return dict != null ? new List<string>(dict.Keys) : new List<string>();
        }

        // ── Single-Entity Lookups ────────────────────────────────

        public static NPCData GetExistingNPC(string id)
        {
            id = id.ToLower();
            if (_npcCache.TryGetValue(id, out var cached)) return cached;

            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, NPCData>>("_NPCsSource").Value;
            if (dict != null && dict.TryGetValue(id, out var npc))
            {
                _npcCache[id] = npc;
                return npc;
            }
            Plugin.Log.LogWarning($"[DataHelper] NPC '{id}' not found for sprite borrowing");
            return null;
        }

        public static AuraCurseData GetAuraCurse(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            if (_acCache.TryGetValue(id, out var cached)) return cached;

            var ac = Globals.Instance.GetAuraCurseData(id);
            if (ac != null) _acCache[id] = ac;
            else Plugin.Log.LogWarning($"[DataHelper] AuraCurse '{id}' not found");
            return ac;
        }

        public static TierRewardData GetTierReward(int tier)
        {
            return Globals.Instance.GetTierRewardData(tier);
        }

        public static EventRequirementData GetEventRequirement(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, EventRequirementData>>("_EventRequirementSource").Value;
            if (dict != null && dict.TryGetValue(id, out var req))
                return req;
            Plugin.Log.LogWarning($"[DataHelper] EventRequirement '{id}' not found");
            return null;
        }

        public static LootData GetLootData(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, LootData>>("_LootDataSource").Value;
            if (dict != null && dict.TryGetValue(id, out var loot))
                return loot;
            return null;
        }

        /// <summary>Get an existing EventData from Globals by ID.</summary>
        public static EventData GetExistingEvent(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, EventData>>("_Events").Value;
            if (dict != null && dict.TryGetValue(id.ToLower(), out var evt))
                return evt;
            return null;
        }

        /// <summary>Get an existing NodeData from Globals by ID.</summary>
        public static NodeData GetExistingNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, NodeData>>("_NodeDataSource").Value;
            if (dict != null && dict.TryGetValue(id.ToLower(), out var node))
                return node;
            return null;
        }

        public static ZoneData GetExistingZone(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ZoneData>>("_ZoneDataSource").Value;
            if (dict != null && dict.TryGetValue(id.ToLower(), out var zone))
                return zone;
            return null;
        }

        public static CardData GetCard(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CardData>>("_CardsSource").Value;
            if (dict != null && dict.TryGetValue(id, out var card))
                return card;
            return null;
        }

        public static ItemData GetItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ItemData>>("_ItemDataSource").Value;
            if (dict != null && dict.TryGetValue(id, out var item))
                return item;
            return null;
        }

        public static SubClassData GetSubClass(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SubClassData>>("_SubClassSource").Value;
            if (dict != null && dict.TryGetValue(id, out var sc))
                return sc;
            return null;
        }

        public static TraitData GetTrait(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, TraitData>>("_TraitsSource").Value;
            if (dict != null && dict.TryGetValue(id, out var trait))
                return trait;
            return null;
        }

        public static PerkData GetPerk(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PerkData>>("_PerksSource").Value;
            if (dict != null && dict.TryGetValue(id, out var perk))
                return perk;
            return null;
        }

        public static PerkNodeData GetPerkNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PerkNodeData>>("_PerksNodesSource").Value;
            if (dict != null && dict.TryGetValue(id, out var pn))
                return pn;
            return null;
        }

        public static SkinData GetSkin(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SkinData>>("_SkinDataSource").Value;
            if (dict != null && dict.TryGetValue(id, out var skin))
                return skin;
            return null;
        }

        public static CardbackData GetCardback(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.ToLower();
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CardbackData>>("_CardbackDataSource").Value;
            if (dict != null && dict.TryGetValue(id, out var cb))
                return cb;
            return null;
        }

        public static TierRewardData GetTierRewardData(int tier)
        {
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<int, TierRewardData>>("_TierRewardDataSource").Value;
            if (dict != null && dict.TryGetValue(tier, out var tr))
                return tr;
            return null;
        }

        public static PackData GetPackData(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, PackData>>("_PackDataSource").Value;
            if (dict != null && dict.TryGetValue(id.ToLower(), out var pack)) return pack;
            return null;
        }

        public static CardPlayerPackData GetCardPlayerPackData(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Globals.Instance?.GetCardPlayerPackData(id.ToLower());
        }

        public static CardPlayerPairsPackData GetCardPlayerPairsPackData(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Globals.Instance?.GetCardPlayerPairsPackData(id.ToLower());
        }

        public static HeroData GetHeroData(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance)
                .Field<Dictionary<string, HeroData>>("_Heroes").Value;
            if (dict != null && dict.TryGetValue(id.ToLower(), out var hero)) return hero;
            return null;
        }
    }
}
