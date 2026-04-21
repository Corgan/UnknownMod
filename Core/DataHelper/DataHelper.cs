using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Core infrastructure for DataHelper: caches, helpers, and shared utilities.
    /// Builder methods live in partial files under Core/ and Core/Builders/.
    /// </summary>
    public static partial class DataHelper
    {
        private static readonly Dictionary<string, NPCData> _npcCache = new();
        private static readonly Dictionary<string, AuraCurseData> _acCache = new();
        private static readonly Dictionary<string, TierRewardData> _tierCache = new();

        // -- Cached ID lists (populated lazily from Globals.Instance) --

        private static List<string> _auraCurseIds;
        private static List<string> _eventReqIds;
        private static List<string> _npcIds;
        private static List<string> _cardIds;
        private static List<string> _petModelCardIds;
        private static List<string> _itemIds;
        private static List<string> _subClassIds;
        private static List<string> _traitIds;
        private static List<string> _perkIds;
        private static List<string> _perkNodeIds;
        private static List<string> _skinIds;
        private static List<string> _cardbackIds;
        private static List<string> _zoneIds;
        private static List<string> _eventIds;
        private static List<string> _lootIds;
        private static List<string> _packIds;
        private static List<string> _cardPlayerPackIds;
        private static List<string> _cardPlayerPairsPackIds;
        private static List<string> _heroDataIds;
        private static List<string> _combatIds;
        private static List<int> _tierRewardTiers;

        /// <summary>Clear entity caches (NPC, AC, TierReward). Call before a full rebuild.</summary>
        public static void ClearEntityCaches()
        {
            _npcCache.Clear();
            _acCache.Clear();
            _tierCache.Clear();
        }

        /// <summary>Invalidate cached ID lists (call if game data reloads).</summary>
        public static void ClearIdListCaches()
        {
            _auraCurseIds = null;
            _eventReqIds = null;
            _npcIds = null;
            _cardIds = null;
            _petModelCardIds = null;
            _itemIds = null;
            _subClassIds = null;
            _traitIds = null;
            _perkIds = null;
            _perkNodeIds = null;
            _skinIds = null;
            _cardbackIds = null;
            _zoneIds = null;
            _eventIds = null;
            _lootIds = null;
            _packIds = null;
            _cardPlayerPackIds = null;
            _cardPlayerPairsPackIds = null;
            _heroDataIds = null;
            _combatIds = null;
            _tierRewardTiers = null;
        }

        // -- Shared Helpers --

        private static void RegisterInDict<T>(string fieldName, string key, T value) where T : class
        {
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, T>>(fieldName).Value;
            if (dict != null)
            {
                dict[key] = value;
                ModRegistry.TrackRegistration(fieldName, key);
            }
        }

        private static SpecialValues MakeSV(SpecialValueDef sv)
        {
            if (sv == null) return default;
            return new SpecialValues(sv.Name, sv.Use, sv.Multiplier);
        }

        private static SpecialValueDef SnapSV(SpecialValues sv)
        {
            if (!sv.Use && sv.Multiplier == 0f) return null;
            return new SpecialValueDef { Name = sv.Name, Use = sv.Use, Multiplier = sv.Multiplier };
        }

        public static string GetACId(AuraCurseData ac) => ac != null ? ac.Id ?? "" : "";
        public static string GetCardId(CardData c) => c != null ? c.Id ?? "" : "";
    }
}
