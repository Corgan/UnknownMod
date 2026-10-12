using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>One place in a project that holds an id.</summary>
    public class RefHit
    {
        public string Family;     // ModProject dictionary name, e.g. "Cards" or "CardPatches"
        public string OwnerId;
        public bool IsPatch;
        public string Field;      // dotted path, e.g. "Nodes[n1].CombatIds[0]"
        public object Owner;
        public string Value;
        public override string ToString() => $"{Family}/{OwnerId} \u2192 {Field}";
    }

    /// <summary>
    /// Reflection-based reference scanning over every Def in a project. Powers "where used",
    /// reference-safe delete and rename, and the dangling-reference validator.
    /// </summary>
    public static class ModReferences
    {
        // Fields that hold display text, own identity, or non-entity strings and so never count as references.
        private static readonly HashSet<string> SkipFields = new(StringComparer.Ordinal)
        {
            "Name","Description","DescriptionExtended","Text","Message","ReplyText","Fluff","DisplayName",
            "CharacterName","CardbackName","SkinName","HeroName","NodeName","ZoneName","PackName","SubClassName",
            "TraitName","RequirementName","EventName","EffectName","CharacterDescription","CharacterDescriptionStrength",
            "Connections","NodeId","FromNodeId","ToNodeId","IfNotNodeId","TargetNodeId","IdPrefix","DetectedPrefix",
            "Kind","Path","SemanticFolder","ColorHex","TintHex","ShaderName","ClipName","SortingLayer","Roads",
            "Sku","ExistsSku","RequirementSku","SteamStat","UnlockSteamAchievement","IdSteam","Seed","CustomDescription",
            "IconTextValue","DescriptionAction","DescriptionArgs","PreDescriptionArgs","PostDescriptionArgs","DescriptionText","PreDescriptionText","PostDescriptionText"
        };

        private static readonly HashSet<string> IdentityFields = new(StringComparer.Ordinal)
        {
            "Id","PackId","ZoneId","TargetZoneId","BackgroundId"
        };

        private const int MaxDepth = 8;

        public struct Entry
        {
            public string Family;
            public string Id;
            public bool IsPatch;
            public object Def;
        }

        /// <summary>Every entity dictionary on <see cref="ModProject"/>, with its entries.</summary>
        public static IEnumerable<Entry> Entities(ModProject p)
        {
            foreach (var f in typeof(ModProject).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!f.FieldType.IsGenericType || f.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>)) continue;
                var args = f.FieldType.GetGenericArguments();
                if (args[0] != typeof(string) || !args[1].IsClass) continue;
                if (!typeof(IModEntity).IsAssignableFrom(args[1])) continue;
                var dict = f.GetValue(p) as IDictionary;
                if (dict == null) continue;
                bool patch = f.Name.EndsWith("Patches", StringComparison.Ordinal);
                foreach (DictionaryEntry e in dict)
                    yield return new Entry { Family = f.Name, Id = (string)e.Key, IsPatch = patch, Def = e.Value };
            }
        }

        /// <summary>Name of the <see cref="ModProject"/> dictionary field that holds <paramref name="dict"/>.</summary>
        public static string FamilyOf(ModProject p, object dict)
        {
            foreach (var f in typeof(ModProject).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (ReferenceEquals(f.GetValue(p), dict)) return f.Name;
            return null;
        }

        public static List<RefHit> FindUsages(ModProject p, string id, string selfFamily = null, string selfId = null)
        {
            var hits = new List<RefHit>();
            if (p == null || string.IsNullOrEmpty(id)) return hits;
            foreach (var e in Entities(p))
            {
                if (e.Def == null) continue;
                if (selfFamily != null && e.Family == selfFamily && string.Equals(e.Id, selfId, StringComparison.OrdinalIgnoreCase)) continue;
                Walk(e.Def, "", 0, true, (path, value, setter, owner) =>
                {
                    if (string.Equals(value, id, StringComparison.OrdinalIgnoreCase))
                        hits.Add(new RefHit { Family = e.Family, OwnerId = e.Id, IsPatch = e.IsPatch, Field = path, Owner = e.Def, Value = value });
                });
            }
            return hits;
        }

        /// <summary>Rewrite every reference to <paramref name="oldId"/> and persist the touched entities.</summary>
        public static int Rewrite(ModProject p, string oldId, string newId, string selfFamily, string selfId)
        {
            if (p == null || string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId)) return 0;
            int total = 0;
            foreach (var e in Entities(p).ToList())
            {
                if (e.Def == null) continue;
                if (selfFamily != null && e.Family == selfFamily && string.Equals(e.Id, selfId, StringComparison.OrdinalIgnoreCase)) continue;
                int changed = 0;
                Walk(e.Def, "", 0, true, (path, value, setter, owner) =>
                {
                    if (string.Equals(value, oldId, StringComparison.OrdinalIgnoreCase)) { setter(newId); changed++; }
                });
                if (changed == 0) continue;
                total += changed;
                SaveEntry(p, e);
            }
            if (total > 0) p.IsDirty = true;
            return total;
        }

        public static void SaveEntry(ModProject p, Entry e)
        {
            switch (e.Def)
            {
                case CardDef card: ModProjectLoader.SaveCard(p, card, e.IsPatch); return;
                case ZoneDef zone: ModProjectLoader.SaveZone(p, zone); return;
                case ZonePatchDef zp: UnknownMod.Editor.Tabs.ZoneTabEditor.SaveZonePatch(p, zp); return;
            }
            string folder = FolderFor(e.Def.GetType());
            if (folder == null)
            {
                Plugin.Log.LogWarning($"[References] No save route for {e.Def.GetType().Name} '{e.Id}'; change kept in memory only.");
                return;
            }
            ModProjectLoader.SaveEntity(p, folder, e.Id, e.Def, e.IsPatch);
        }

        public static string FolderFor(Type t)
        {
            var info = EntityTypeRegistry.Get(t);
            if (info != null) return info.FolderName;
            if (t == typeof(EventDef)) return "events";
            if (t == typeof(CombatDef)) return "combats";
            if (t == typeof(BackgroundDef)) return "backgrounds";
            if (t == typeof(CharacterVisualDef)) return "visualgraphs";
            if (t == typeof(ThermometerDef)) return "thermometers";
            if (t == typeof(ThermometerTierDef)) return "thermometertiers";
            if (t == typeof(BettingTeamDef)) return "bettingteams";
            if (t == typeof(MemorySequenceDef)) return "memorysequences";
            if (t == typeof(CardPairRewardDef)) return "cardpairrewards";
            if (t == typeof(RuleSetDef)) return "rulesets";
            if (t == typeof(EconomyDef)) return "economies";
            return null;
        }

        /// <summary>
        /// Walks every string reference in <paramref name="obj"/>. The visitor receives the dotted path, the value,
        /// a setter to replace it, and the containing object.
        /// </summary>
        public static void Walk(object obj, string path, int depth, bool isRoot,
            Action<string, string, Action<string>, object> visit)
        {
            if (obj == null || depth > MaxDepth) return;
            var type = obj.GetType();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.IsLiteral || SkipFields.Contains(f.Name)) continue;
                if (f.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), true).Length > 0) continue;
                string fp = path.Length == 0 ? f.Name : path + "." + f.Name;
                var ft = f.FieldType;
                var val = f.GetValue(obj);
                if (ft == typeof(string))
                {
                    string s = (string)val;
                    if (string.IsNullOrEmpty(s)) continue;
                    if (isRoot && IsIdentity(obj, f, s)) continue;
                    var fld = f; var own = obj;
                    visit(fp, s, v => fld.SetValue(own, v), obj);
                }
                else if (val is IList list && !(val is Array))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        if (item == null) continue;
                        if (item is string str)
                        {
                            if (str.Length == 0) continue;
                            int idx = i; var l = list;
                            visit($"{fp}[{i}]", str, v => l[idx] = v, obj);
                        }
                        else if (IsDefClass(item.GetType()))
                            Walk(item, $"{fp}[{i}]", depth + 1, false, visit);
                    }
                }
                else if (val is IDictionary dict)
                {
                    foreach (DictionaryEntry de in dict)
                        if (de.Value != null && IsDefClass(de.Value.GetType()))
                            Walk(de.Value, $"{fp}[{de.Key}]", depth + 1, false, visit);
                }
                else if (val != null && IsDefClass(ft))
                    Walk(val, fp, depth + 1, false, visit);
            }
        }

        private static bool IsIdentity(object owner, FieldInfo f, string value)
        {
            if (IdentityFields.Contains(f.Name)) return true;
            if (owner is CombatDef && f.Name == "CombatId") return true;
            if (owner is EventDef && f.Name == "EventId") return true;
            return false;
        }

        private static bool IsDefClass(Type t)
            => t.IsClass && t != typeof(string) && t.Namespace != null && t.Namespace.StartsWith("UnknownMod", StringComparison.Ordinal);
    }
}
