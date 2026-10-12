using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Extras (KeyNote, Cinematic, CorruptionPack,
    //               ChallengeTrait, WeeklyChallenge)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        private static List<string> _corruptionPackIds;
        private static List<string> _challengeTraitIds;
        private static List<string> _weeklyIds;

        // ── Reflection helpers (private serialized fields) ───────────────

        private static void SetPrivate(object target, string field, object value)
        {
            var fi = AccessTools.Field(target.GetType(), field);
            if (fi == null)
            {
                if (_reportedMissingFields.Add($"{target.GetType().Name}.{field}"))
                    Plugin.Log.LogError($"[DataHelper] {target.GetType().Name}.{field} not found - the game was probably updated; this value will not be applied");
                return;
            }
            fi.SetValue(target, value);
        }

        private static T GetPrivate<T>(object target, string field)
        {
            var fi = AccessTools.Field(target.GetType(), field);
            if (fi == null)
            {
                if (_reportedMissingFields.Add($"{target.GetType().Name}.{field}"))
                    Plugin.Log.LogError($"[DataHelper] {target.GetType().Name}.{field} not found - the game was probably updated");
                return default;
            }
            return fi.GetValue(target) is T v ? v : default;
        }

        private static List<string> SortedKeys<T>(string globalsField)
        {
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, T>>(globalsField).Value;
            return dict != null ? dict.Keys.OrderBy(k => k).ToList() : new List<string>();
        }

        // ── ID lists ─────────────────────────────────────────────────────

        public static List<string> GetAllCorruptionPackIds()
            => _corruptionPackIds ??= SortedKeys<CorruptionPackData>("_CorruptionPackDataSource");

        public static List<string> GetAllChallengeTraitIds()
            => _challengeTraitIds ??= SortedKeys<ChallengeTrait>("_ChallengeTraitsSource");

        public static List<string> GetAllWeeklyIds()
            => _weeklyIds ??= SortedKeys<ChallengeData>("_WeeklyDataSource");

        // ── Resolvers ────────────────────────────────────────────────────

        public static KeyNotesData GetKeyNote(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Globals.Instance != null && Globals.Instance.KeyNotes != null
                && Globals.Instance.KeyNotes.TryGetValue(NormalizeKey(id), out var found) ? found : null;
        }

        public static CorruptionPackData GetCorruptionPack(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, CorruptionPackData>>("_CorruptionPackDataSource").Value;
            return dict != null && dict.TryGetValue(id, out var v) ? v : null;
        }

        public static ChallengeTrait GetChallengeTrait(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ChallengeTrait>>("_ChallengeTraitsSource").Value;
            return dict != null && dict.TryGetValue(id.Trim().ToLower(), out var v) ? v : null;
        }

        public static ChallengeData GetWeekly(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, ChallengeData>>("_WeeklyDataSource").Value;
            return dict != null && dict.TryGetValue(id.Trim().ToLower(), out var v) ? v : null;
        }

        // ── Registration ─────────────────────────────────────────────────

        public static void RegisterKeyNote(KeyNotesData kn)
        {
            RegisterInDict("_KeyNotes", NormalizeKey(kn.Id), kn);
            _keyNoteIds = null;
        }

        public static void RegisterCinematic(CinematicData c)
        {
            RegisterInDict("_Cinematics", NormalizeKey(c.CinematicId), c);
            _cinematicIds = null;
        }

        public static void RegisterCorruptionPack(CorruptionPackData p)
        {
            RegisterInDict("_CorruptionPackDataSource", p.name, p);
            _corruptionPackIds = null;
        }

        public static void RegisterChallengeTrait(ChallengeTrait t)
        {
            RegisterInDict("_ChallengeTraitsSource", t.Id.ToLower(), t);
            _challengeTraitIds = null;
        }

        public static void RegisterWeekly(ChallengeData w)
        {
            RegisterInDict("_WeeklyDataSource", w.Id.ToLower(), w);
            _weeklyIds = null;
        }

        // ── KeyNote ──────────────────────────────────────────────────────

        public static KeyNotesData MakeKeyNote(KeyNoteDef d)
        {
            var kn = ScriptableObject.CreateInstance<KeyNotesData>();
            string key = NormalizeKey(d.Id);
            kn.name = key;
            SetPrivate(kn, "id", key);
            SetPrivate(kn, "keynoteName", d.Name ?? "");
            SetPrivate(kn, "description", d.Description ?? "");
            SetPrivate(kn, "descriptionExtended", d.DescriptionExtended ?? "");
            return kn;
        }

        public static KeyNoteDef SnapshotKeyNote(KeyNotesData kn) => new KeyNoteDef
        {
            Id = kn.Id ?? "",
            Name = kn.KeynoteName ?? "",
            Description = kn.Description ?? "",
            DescriptionExtended = kn.DescriptionExtended ?? "",
        };

        // ── Cinematic ────────────────────────────────────────────────────

        /// <summary>Builds the cinematic without its event/combat links; see <see cref="WireCinematicRefs"/>.</summary>
        public static CinematicData MakeCinematic(CinematicDef d)
        {
            var c = ScriptableObject.CreateInstance<CinematicData>();
            c.name = NormalizeKey(d.Id);
            SetPrivate(c, "cinematicId", d.Id);
            SetPrivate(c, "cinematicGoName", d.PrefabName ?? "");
            SetPrivate(c, "cinematicEndAdventure", d.EndAdventure);

            string soundSource = !string.IsNullOrEmpty(d.SoundSource) ? d.SoundSource : d.Id;
            var src = GetCinematic(soundSource);
            if (src != null && src.CinematicBSO != null)
                SetPrivate(c, "cinematicBSO", src.CinematicBSO);
            return c;
        }

        /// <summary>Resolve the event and combat a cinematic leads into. Runs after events and combats exist.</summary>
        public static void WireCinematicRefs(CinematicDef d)
        {
            var c = GetCinematic(d.Id);
            if (c == null) return;

            if (!string.IsNullOrEmpty(d.EventId))
            {
                var ev = GetExistingEvent(d.EventId);
                if (ev == null) Plugin.Log.LogWarning($"[Cinematic] '{d.Id}' references missing event '{d.EventId}'");
                SetPrivate(c, "cinematicEvent", ev);
            }
            if (!string.IsNullOrEmpty(d.CombatId))
            {
                var cb = GetExistingCombat(d.CombatId);
                if (cb == null) Plugin.Log.LogWarning($"[Cinematic] '{d.Id}' references missing combat '{d.CombatId}'");
                SetPrivate(c, "cinematicCombat", cb);
            }
        }

        public static CinematicDef SnapshotCinematic(CinematicData c)
        {
            var d = new CinematicDef
            {
                Id = c.CinematicId ?? "",
                PrefabName = GetPrivate<string>(c, "cinematicGoName") ?? "",
                EndAdventure = c.CinematicEndAdventure,
                EventId = c.CinematicEvent != null ? c.CinematicEvent.EventId ?? "" : "",
                CombatId = c.CinematicCombat != null ? c.CinematicCombat.CombatId ?? "" : "",
            };
            d.SoundSource = d.Id;
            return d;
        }

        // ── CorruptionPack ───────────────────────────────────────────────

        public static CorruptionPackData MakeCorruptionPack(CorruptionPackDef d)
        {
            var p = ScriptableObject.CreateInstance<CorruptionPackData>();
            p.name = d.Id;
            SetPrivate(p, "packName", d.PackName ?? "");
            SetPrivate(p, "packClass", d.PackClass);
            SetPrivate(p, "packTier", d.PackTier);
            SetPrivate(p, "lowPackList", ResolveCardIdList(d.LowPackCardIds, $"CorruptionPack '{d.Id}' low"));
            SetPrivate(p, "highPackList", ResolveCardIdList(d.HighPackCardIds, $"CorruptionPack '{d.Id}' high"));
            return p;
        }

        private static List<CardDataNew> ResolveCardIdList(List<string> ids, string owner)
        {
            var list = new List<CardDataNew>();
            if (ids == null) return list;
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var card = GetCardSource(id);
                if (card == null) Plugin.Log.LogWarning($"[{owner}] unknown card '{id}'");
                else list.Add(card);
            }
            return list;
        }

        private static List<string> CardIdsOf(List<CardDataNew> cards)
            => cards == null ? new List<string>() : cards.Where(c => c != null).Select(c => c.Id ?? "").ToList();

        public static CorruptionPackDef SnapshotCorruptionPack(CorruptionPackData p) => new CorruptionPackDef
        {
            Id = p.name ?? "",
            PackName = p.PackName ?? "",
            PackClass = p.PackClass,
            PackTier = p.PackTier,
            LowPackCardIds = CardIdsOf(p.LowPack),
            HighPackCardIds = CardIdsOf(p.HighPack),
        };

        // ── ChallengeTrait ───────────────────────────────────────────────

        public static ChallengeTrait MakeChallengeTrait(ChallengeTraitDef d)
        {
            var t = ScriptableObject.CreateInstance<ChallengeTrait>();
            t.name = d.Id;
            SetPrivate(t, "id", d.Id);
            SetPrivate(t, "name", d.Name ?? "");
            SetPrivate(t, "isMadnessTrait", d.IsMadnessTrait);
            SetPrivate(t, "order", d.Order);
            SetPrivate(t, "isSingularityTrait", d.IsSingularityTrait);
            SetPrivate(t, "orderSingularity", d.OrderSingularity);
            RegisterChallengeTraitTexts(d.Id, d.Name, d.Description);

            string iconSource = !string.IsNullOrEmpty(d.IconSource) ? d.IconSource : d.Id;
            var src = GetChallengeTrait(iconSource);
            if (src != null && src.Icon != null)
                SetPrivate(t, "icon", src.Icon);
            return t;
        }

        public static ChallengeTraitDef SnapshotChallengeTrait(ChallengeTrait t)
        {
            var d = new ChallengeTraitDef
            {
                Id = t.Id ?? "",
                Name = t.Name ?? "",
                IsMadnessTrait = t.IsMadnessTrait,
                Order = t.Order,
                IsSingularityTrait = t.IsSingularityTrait,
                OrderSingularity = t.OrderSingularity,
            };
            d.IconSource = d.Id;
            return d;
        }

        // ── WeeklyChallenge ──────────────────────────────────────────────

        /// <summary>Needs heroes, NPCs, combats, loot, traits and cards to exist, so it runs in the late build stage.</summary>
        public static ChallengeData MakeWeekly(WeeklyChallengeDef d)
        {
            var w = ScriptableObject.CreateInstance<ChallengeData>();
            w.name = d.Id;
            SetPrivate(w, "id", d.Id);
            SetPrivate(w, "idSteam", d.IdSteam ?? "");
            SetPrivate(w, "week", d.Week);
            SetPrivate(w, "seed", d.Seed ?? "");
            SetPrivate(w, "fromDay", d.FromDay ?? "");
            SetPrivate(w, "fromHour", d.FromHour ?? "");
            SetPrivate(w, "toDay", d.ToDay ?? "");
            SetPrivate(w, "toHour", d.ToHour ?? "");

            SetPrivate(w, "hero1", WeeklyRef(d, d.Hero1Id, GetSubClass));
            SetPrivate(w, "hero2", WeeklyRef(d, d.Hero2Id, GetSubClass));
            SetPrivate(w, "hero3", WeeklyRef(d, d.Hero3Id, GetSubClass));
            SetPrivate(w, "hero4", WeeklyRef(d, d.Hero4Id, GetSubClass));
            SetPrivate(w, "boss1", WeeklyRef(d, d.Boss1Id, id => GetExistingNPC(id)));
            SetPrivate(w, "boss2", WeeklyRef(d, d.Boss2Id, id => GetExistingNPC(id)));
            SetPrivate(w, "bossCombat", WeeklyRef(d, d.BossCombatId, GetExistingCombat));
            SetPrivate(w, "loot", WeeklyRef(d, d.LootId, GetLootData));

            var traits = new List<ChallengeTrait>();
            foreach (var id in d.TraitIds ?? new List<string>())
            {
                var t = WeeklyRef(d, id, GetChallengeTrait);
                if (t != null) traits.Add(t);
            }
            SetPrivate(w, "traits", traits);
            SetPrivate(w, "corruption", ResolveCardIdList(d.CorruptionCardIds, $"Weekly '{d.Id}' corruption"));
            return w;
        }

        private static T WeeklyRef<T>(WeeklyChallengeDef d, string id, System.Func<string, T> get) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            var v = get(id);
            if (v == null) Plugin.Log.LogWarning($"[Weekly] '{d.Id}' references missing {typeof(T).Name} '{id}'");
            return v;
        }

        public static WeeklyChallengeDef SnapshotWeekly(ChallengeData w) => new WeeklyChallengeDef
        {
            Id = w.Id ?? "",
            IdSteam = w.IdSteam ?? "",
            Week = w.Week,
            Seed = w.Seed ?? "",
            FromDay = GetPrivate<string>(w, "fromDay") ?? "",
            FromHour = GetPrivate<string>(w, "fromHour") ?? "",
            ToDay = GetPrivate<string>(w, "toDay") ?? "",
            ToHour = GetPrivate<string>(w, "toHour") ?? "",
            Hero1Id = w.Hero1 != null ? w.Hero1.Id ?? "" : "",
            Hero2Id = w.Hero2 != null ? w.Hero2.Id ?? "" : "",
            Hero3Id = w.Hero3 != null ? w.Hero3.Id ?? "" : "",
            Hero4Id = w.Hero4 != null ? w.Hero4.Id ?? "" : "",
            Boss1Id = w.Boss1 != null ? w.Boss1.Id ?? "" : "",
            Boss2Id = w.Boss2 != null ? w.Boss2.Id ?? "" : "",
            BossCombatId = w.BossCombat != null ? w.BossCombat.CombatId ?? "" : "",
            LootId = w.Loot != null ? w.Loot.Id ?? "" : "",
            TraitIds = w.Traits == null ? new List<string>() : w.Traits.Where(t => t != null).Select(t => t.Id ?? "").ToList(),
            CorruptionCardIds = CardIdsOf(w.CorruptionList),
        };
    }
}
