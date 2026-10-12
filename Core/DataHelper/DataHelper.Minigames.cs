using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Thermometers and town-minigame pools
    //
    //  Thermometer assets are not in any Globals dictionary, so mod ones live in a mod-side
    //  registry and base-game ones are found by scanning loaded ScriptableObjects.
    //
    //  Betting teams, memory sequences and card-pair rewards are serialised into town scenes
    //  with no registry at all, so mod entries are stored here and appended to the live
    //  manager when it starts (Patches.Minigames).
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        private static readonly Dictionary<string, ThermometerData> _modThermometers = new();
        private static readonly Dictionary<string, ThermometerTierData> _modThermometerTiers = new();
        private static Dictionary<string, ThermometerData> _baseThermometers;
        private static Dictionary<string, ThermometerTierData> _baseThermometerTiers;

        private static readonly Dictionary<string, BettingTeamDef> _bettingTeams = new();
        private static readonly Dictionary<string, MemorySequenceDef> _memorySequences = new();
        private static readonly Dictionary<string, CardPairRewardDef> _cardPairRewards = new();
        private static readonly Dictionary<string, BettingTeamData> _bettingTeamAssets = new();

        // First-seen contents of every shared list a mod appended to, so a rebuild can put the vanilla data back
        private static readonly Dictionary<IList, object[]> _listSnapshots = new();

        // Managers that already received mod content, so an editor refresh can re-apply it without waiting for their next callback
        private static readonly List<object> _liveManagers = new();

        private static void TrackLive(object manager)
        {
            _liveManagers.RemoveAll(m => m is UnityEngine.Object o && o == null);
            if (manager != null && !_liveManagers.Contains(manager)) _liveManagers.Add(manager);
        }

        internal static void ReapplyToLiveManagers()
        {
            foreach (var m in _liveManagers.ToList())
            {
                if (m is UnityEngine.Object o && o == null) continue;
                try
                {
                    if (m is BettingGameManager) ApplyBettingTeams(m);
                    else if (m is MemorySequenceManager) ApplyMemorySequences(m);
                    else if (m is CardPairMemoryManager) ApplyCardPairRewards(m);
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Minigames] Re-apply failed: {ex.Message}"); }
            }
        }

        private static void SnapshotList(IList list)
        {
            if (list != null && !_listSnapshots.ContainsKey(list))
                _listSnapshots[list] = list.Cast<object>().ToArray();
        }

        /// <summary>Undo everything registered outside Globals: minigame pool additions, mod thermometers and betting teams, skin portrait overrides. Called before a full rebuild.</summary>
        public static void ResetRuntimeState()
        {
            RestoreMinigameLists();

            foreach (var team in _bettingTeamAssets.Values)
                if (team != null) UnityEngine.Object.Destroy(team);
            _bettingTeamAssets.Clear();
            _bettingTeams.Clear();
            _memorySequences.Clear();
            _cardPairRewards.Clear();
            _modThermometers.Clear();
            _modThermometerTiers.Clear();

            foreach (var kv in _subclassPortraitOriginals)
                if (kv.Key != null) Traverse.Create(kv.Key).Field("spritePortrait").SetValue(kv.Value);
            _subclassPortraitOriginals.Clear();
        }

        /// <summary>Puts the shared minigame lists back to their vanilla contents; the next manager start re-applies whatever is still registered.</summary>
        private static void RestoreMinigameLists()
        {
            foreach (var kv in _listSnapshots)
            {
                kv.Key.Clear();
                foreach (var item in kv.Value) kv.Key.Add(item);
            }
            _listSnapshots.Clear();
        }

        // ── Thermometers ─────────────────────────────────────────

        public static ThermometerData MakeThermometer(ThermometerDef d)
        {
            var t = ScriptableObject.CreateInstance<ThermometerData>();
            t.name = d.Id;
            t.ThermometerId = d.Id;
            t.ThermometerColor = ColorUtility.TryParseHtmlString(d.ColorHex ?? "", out var c) ? c : Color.white;
            t.GoldBonus = d.GoldBonus;
            t.ExpBonus = d.ExpBonus;
            t.CardBonus = d.CardBonus;
            t.CardReward = d.CardReward;
            t.ExpertiseBonus = d.ExpertiseBonus;
            t.UiGold = d.UiGold;
            t.UiExp = d.UiExp;
            t.UiCard = d.UiCard;
            return t;
        }

        public static void RegisterThermometer(ThermometerData t)
        {
            if (!string.IsNullOrEmpty(t?.ThermometerId)) _modThermometers[t.ThermometerId] = t;
        }

        public static ThermometerTierData MakeThermometerTier(ThermometerTierDef d)
        {
            var tier = ScriptableObject.CreateInstance<ThermometerTierData>();
            tier.name = d.Id;
            tier.ThermometerTierId = d.Id;
            var rounds = new List<RoundThermometer>();
            foreach (var r in d.Rounds ?? new List<RoundThermometerDef>())
            {
                var data = GetThermometer(r.ThermometerId);
                if (data == null)
                {
                    Plugin.Log.LogWarning($"[DataHelper] ThermometerTier '{d.Id}' round {r.Round}: thermometer '{r.ThermometerId}' not found");
                    continue;
                }
                rounds.Add(new RoundThermometer { Round = r.Round, ThermometerData = data });
            }
            tier.RoundThermometer = rounds.OrderBy(r => r.Round).ToArray();
            return tier;
        }

        public static void RegisterThermometerTier(ThermometerTierData t)
        {
            if (!string.IsNullOrEmpty(t?.ThermometerTierId)) _modThermometerTiers[t.ThermometerTierId] = t;
        }

        public static ThermometerData GetThermometer(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_modThermometers.TryGetValue(id, out var m)) return m;
            if (_baseThermometers == null || _baseThermometers.Count == 0) _baseThermometers = IndexAssets<ThermometerData>(t => t.ThermometerId);
            return _baseThermometers.TryGetValue(id, out var b) ? b : null;
        }

        public static ThermometerTierData GetThermometerTier(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_modThermometerTiers.TryGetValue(id, out var m)) return m;
            if (_baseThermometerTiers == null || _baseThermometerTiers.Count == 0) _baseThermometerTiers = IndexAssets<ThermometerTierData>(t => t.ThermometerTierId);
            return _baseThermometerTiers.TryGetValue(id, out var b) ? b : null;
        }

        public static List<string> GetAllThermometerTierIds()
        {
            _baseThermometerTiers ??= IndexAssets<ThermometerTierData>(t => t.ThermometerTierId);
            return _baseThermometerTiers.Keys.OrderBy(k => k).ToList();
        }

        public static List<string> GetAllThermometerIds()
        {
            _baseThermometers ??= IndexAssets<ThermometerData>(t => t.ThermometerId);
            return _baseThermometers.Keys.OrderBy(k => k).ToList();
        }

        private static Dictionary<string, T> IndexAssets<T>(Func<T, string> idOf) where T : ScriptableObject
        {
            var result = new Dictionary<string, T>();
            foreach (var asset in Resources.FindObjectsOfTypeAll<T>())
            {
                if (asset == null || _modThermometers.Values.Cast<ScriptableObject>().Contains(asset)
                    || _modThermometerTiers.Values.Cast<ScriptableObject>().Contains(asset)) continue;
                string id = idOf(asset);
                if (!string.IsNullOrEmpty(id)) result[id] = asset;
            }
            return result;
        }

        // ── Minigame pools ───────────────────────────────────────

        public static void RegisterMinigamePools(ModProject p)
        {
            foreach (var kv in p.BettingTeams) _bettingTeams[$"{p.ModId}:{kv.Key}"] = kv.Value;
            foreach (var kv in p.MemorySequences) _memorySequences[$"{p.ModId}:{kv.Key}"] = kv.Value;
            foreach (var kv in p.CardPairRewards) _cardPairRewards[$"{p.ModId}:{kv.Key}"] = kv.Value;
        }

        /// <summary>Re-reads one project's minigame entries after an editor change; betting teams are rebuilt from their NPC lists.</summary>
        public static void RefreshMinigamePools(ModProject p)
        {
            string prefix = p.ModId + ":";
            // Drop this project's old entries first so deleted definitions and their live additions disappear too
            foreach (var key in _bettingTeams.Keys.Where(k => k.StartsWith(prefix)).ToList()) _bettingTeams.Remove(key);
            foreach (var key in _memorySequences.Keys.Where(k => k.StartsWith(prefix)).ToList()) _memorySequences.Remove(key);
            foreach (var key in _cardPairRewards.Keys.Where(k => k.StartsWith(prefix)).ToList()) _cardPairRewards.Remove(key);
            RestoreMinigameLists();
            foreach (var key in _bettingTeamAssets.Keys.Where(k => k.StartsWith(prefix)).ToList())
            {
                if (_bettingTeamAssets[key] != null) UnityEngine.Object.Destroy(_bettingTeamAssets[key]);
                _bettingTeamAssets.Remove(key);
            }
            RegisterMinigamePools(p);
            ReapplyToLiveManagers();
        }

        /// <summary>Append mod teams to the betting game's two team pools (idempotent).</summary>
        public static void ApplyBettingTeams(object manager)
        {
            TrackLive(manager);
            if (_bettingTeams.Count == 0) return;
            var tr = Traverse.Create(manager);
            var pool1 = tr.Field("TeamPool1").GetValue<List<BettingTeamData>>();
            var pool2 = tr.Field("TeamPool2").GetValue<List<BettingTeamData>>();
            SnapshotList(pool1);
            SnapshotList(pool2);
            foreach (var kv in _bettingTeams)
            {
                var team = GetBettingTeamAsset(kv.Key, kv.Value);
                if (team == null) continue;
                if (kv.Value.Pool != BettingPool.Pool2 && pool1 != null && !pool1.Contains(team)) pool1.Add(team);
                if (kv.Value.Pool != BettingPool.Pool1 && pool2 != null && !pool2.Contains(team)) pool2.Add(team);
            }
        }

        private static BettingTeamData GetBettingTeamAsset(string key, BettingTeamDef d)
        {
            if (_bettingTeamAssets.TryGetValue(key, out var cached) && cached != null) return cached;
            var npcs = new List<NPCData>();
            foreach (var id in d.NpcIds ?? new List<string>())
            {
                var npc = GetExistingNPC(id);
                if (npc != null) npcs.Add(npc);
            }
            if (npcs.Count == 0)
            {
                Plugin.Log.LogWarning($"[Minigames] Betting team '{d.TeamName}' has no resolvable NPCs; skipped");
                return null;
            }
            var team = ScriptableObject.CreateInstance<BettingTeamData>();
            team.name = d.TeamName;
            team.TeamName = d.TeamName;
            team.NpcList = npcs;
            _bettingTeamAssets[key] = team;
            return team;
        }

        /// <summary>
        /// Merge mod sequence content into the memory minigame's per-zone packs.
        /// A zone with no pack gets a new one. For a zone that has one, the extra pool grows and a
        /// group whose CardType is not present yet is added; existing groups are left alone.
        /// </summary>
        public static void ApplyMemorySequences(object manager)
        {
            TrackLive(manager);
            if (_memorySequences.Count == 0) return;
            var list = Traverse.Create(manager).Field("zoneSequencePairs").GetValue() as IList;
            var pairType = AccessTools.Inner(manager.GetType(), "ZoneSequencePair");
            if (list == null || pairType == null) return;
            SnapshotList(list);
            var zoneField = pairType.GetField("zoneData");
            var dataField = pairType.GetField("memorySequenceData");

            foreach (var def in _memorySequences.Values)
            {
                if (string.IsNullOrEmpty(def.ZoneId)) continue;
                MemorySequenceData target = null;
                foreach (var pair in list)
                {
                    var zone = zoneField.GetValue(pair) as ZoneData;
                    if (zone != null && string.Equals(zone.ZoneId, def.ZoneId, StringComparison.OrdinalIgnoreCase))
                    {
                        target = dataField.GetValue(pair) as MemorySequenceData;
                        break;
                    }
                }

                if (target == null)
                {
                    var zone = GetExistingZone(def.ZoneId);
                    if (zone == null)
                    {
                        Plugin.Log.LogWarning($"[Minigames] Memory sequence: zone '{def.ZoneId}' not found");
                        continue;
                    }
                    target = ScriptableObject.CreateInstance<MemorySequenceData>();
                    target.name = "memseq_" + def.ZoneId;
                    Traverse.Create(target).Field("packId").SetValue(def.ZoneId);
                    var boxed = Activator.CreateInstance(pairType);
                    zoneField.SetValue(boxed, zone);
                    dataField.SetValue(boxed, target);
                    list.Add(boxed);
                }

                target.cardGroups ??= new List<MemorySequenceData.CardGroup>();
                target.extraPool ??= new List<CardDataNew>();
                SnapshotList(target.cardGroups);
                SnapshotList(target.extraPool);

                foreach (var g in def.CardGroups ?? new List<MemorySequenceGroupDef>())
                {
                    if (target.cardGroups.Any(x => x.cardType == g.CardType)) continue;
                    target.cardGroups.Add(new MemorySequenceData.CardGroup
                    {
                        cardType = g.CardType,
                        count = Math.Max(1, g.Count),
                        pool = ResolveCards(g.PoolCardIds),
                    });
                }
                foreach (var card in ResolveCards(def.ExtraPoolCardIds))
                    if (!target.extraPool.Any(c => c != null && c.Id == card.Id)) target.extraPool.Add(card);
            }
        }

        /// <summary>Append mod reward cards to the card-pair memory pack (idempotent).</summary>
        public static void ApplyCardPairRewards(object manager)
        {
            TrackLive(manager);
            if (_cardPairRewards.Count == 0) return;
            var tr = Traverse.Create(manager).Field("packData");
            var pack = tr.GetValue<CardPairMemoryData>();
            if (pack == null) return;
            string packId = Traverse.Create(pack).Field("packId").GetValue<string>() ?? "";
            pack.Rewards ??= new List<CardDataNew>();
            SnapshotList(pack.Rewards);

            foreach (var def in _cardPairRewards.Values)
            {
                if (!string.IsNullOrEmpty(def.TargetPackId)
                    && !string.Equals(def.TargetPackId, packId, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var card in ResolveCards(def.CardIds))
                    if (!pack.Rewards.Any(c => c != null && c.Id == card.Id)) pack.Rewards.Add(card);
            }
        }

        private static List<CardDataNew> ResolveCards(IEnumerable<string> ids)
        {
            var result = new List<CardDataNew>();
            foreach (var id in ids ?? Enumerable.Empty<string>())
            {
                var card = GetCardSource(id);
                if (card != null) result.Add(card);
            }
            return result;
        }
    }
}
