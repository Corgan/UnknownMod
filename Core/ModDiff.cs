using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public enum DiffKind { Added, Removed, Changed }

    /// <summary>One mergeable unit of an entity: a top-level field (or one node/road of a zone).</summary>
    public sealed class FieldDiff
    {
        public string Key;                 // shown in the UI, e.g. "Name" or "Nodes/sen_3"
        public string[] Path;              // JSON path of the unit inside the entity
        public JToken Mine, Theirs;        // null when the field is missing on that side
        public List<string> Lines = new(); // leaf level changes, "path: mine -> theirs"
        public int LineCount;
    }

    public sealed class EntityDiff
    {
        public string Family;
        public string Id;
        public bool IsPatch;
        public DiffKind Kind;
        public object MineDef, TheirsDef;
        public List<FieldDiff> Fields = new();
        public string Key => Family + "/" + Id;
    }

    /// <summary>What <see cref="ModDiff.ApplyExternal"/> changed in the open project.</summary>
    public sealed class ExternalApply
    {
        public List<string> Meta = new(), Added = new(), Changed = new(), Removed = new(), SkippedRemovals = new();
        public List<object> Reload = new(); // entities to rebuild in the running game
        public List<string> Errors = new();  // entities that could not be applied; the rest were still applied
        public bool Any => Meta.Count + Added.Count + Changed.Count + Removed.Count > 0;
    }

    public sealed class ProjectDiff
    {
        public List<FieldDiff> Meta = new();
        public List<EntityDiff> Entities = new();
        public int Added => Entities.Count(e => e.Kind == DiffKind.Added);
        public int Removed => Entities.Count(e => e.Kind == DiffKind.Removed);
        public int Changed => Entities.Count(e => e.Kind == DiffKind.Changed);
        public bool IsEmpty => Meta.Count == 0 && Entities.Count == 0;
    }

    /// <summary>
    /// Field level comparison and merge between two mod projects (the open one, another mod, or a snapshot).
    /// "Mine" is the open project; "theirs" is what it is compared against, and merging always takes theirs into mine.
    /// </summary>
    public static class ModDiff
    {
        private static readonly JsonSerializerSettings Json = new() { NullValueHandling = NullValueHandling.Ignore };
        private static readonly string[] MetaFields = { "ModName", "Author", "Version", "Description", "Dependencies", "StarterNodeId" };

        private static JObject ToJson(object def) => JObject.Parse(JsonConvert.SerializeObject(def, Json));

        private static object FromJson(JObject jo, Type type) => JsonConvert.DeserializeObject(jo.ToString(), type, Json);

        private static object Clone(object def) => FromJson(ToJson(def), def.GetType());

        // ─────────────────────────────────────────────────────────────
        //  COMPARE
        // ─────────────────────────────────────────────────────────────

        private static JObject MetaJson(ModProject p) => new JObject
        {
            ["ModName"] = p.ModName ?? "",
            ["Author"] = p.Author ?? "",
            ["Version"] = p.Version ?? "",
            ["Description"] = p.Description ?? "",
            ["Dependencies"] = new JArray((p.Dependencies ?? new List<string>()).Cast<object>().ToArray()),
            ["StarterNodeId"] = p.StarterNodeId ?? ""
        };

        public static ProjectDiff Compare(ModProject mine, ModProject theirs)
        {
            var diff = new ProjectDiff();
            diff.Meta = Fields(MetaJson(mine), MetaJson(theirs), false);

            var a = ModReferences.Entities(mine).Where(e => e.Def != null).ToDictionary(e => e.Family + "/" + e.Id, e => e, StringComparer.Ordinal);
            var b = ModReferences.Entities(theirs).Where(e => e.Def != null).ToDictionary(e => e.Family + "/" + e.Id, e => e, StringComparer.Ordinal);

            foreach (var kv in a)
            {
                var e = kv.Value;
                if (!b.TryGetValue(kv.Key, out var other))
                {
                    diff.Entities.Add(new EntityDiff { Family = e.Family, Id = e.Id, IsPatch = e.IsPatch, Kind = DiffKind.Removed, MineDef = e.Def });
                    continue;
                }
                var mj = ToJson(e.Def); var tj = ToJson(other.Def);
                if (JToken.DeepEquals(mj, tj)) continue;
                diff.Entities.Add(new EntityDiff
                {
                    Family = e.Family, Id = e.Id, IsPatch = e.IsPatch, Kind = DiffKind.Changed,
                    MineDef = e.Def, TheirsDef = other.Def,
                    Fields = Fields(mj, tj, e.Def is ZoneDef || e.Def is ZonePatchDef)
                });
            }
            foreach (var kv in b)
                if (!a.ContainsKey(kv.Key))
                    diff.Entities.Add(new EntityDiff { Family = kv.Value.Family, Id = kv.Value.Id, IsPatch = kv.Value.IsPatch, Kind = DiffKind.Added, TheirsDef = kv.Value.Def });

            diff.Entities = diff.Entities.OrderBy(e => e.Family, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToList();
            return diff;
        }

        private static List<FieldDiff> Fields(JObject a, JObject b, bool splitMaps)
        {
            var result = new List<FieldDiff>();
            var names = a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name)).ToList();
            foreach (var name in names)
            {
                var av = a[name]; var bv = b[name];
                if (JToken.DeepEquals(av, bv)) continue;
                bool split = splitMaps && (name == "Nodes" || name == "Roads") && (av == null || av is JObject) && (bv == null || bv is JObject);
                if (!split) { result.Add(Unit(name, new[] { name }, av, bv)); continue; }
                var subs = ((av as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>())
                    .Union((bv as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>());
                foreach (var sub in subs)
                {
                    var sa = av?[sub]; var sb = bv?[sub];
                    if (!JToken.DeepEquals(sa, sb)) result.Add(Unit(name + "/" + sub, new[] { name, sub }, sa, sb));
                }
            }
            return result;
        }

        private static FieldDiff Unit(string key, string[] path, JToken mine, JToken theirs)
        {
            var f = new FieldDiff { Key = key, Path = path, Mine = mine, Theirs = theirs };
            var all = new List<string>();
            Leaves(mine, theirs, key.Replace('/', '.'), all, 60);
            f.LineCount = all.Count;
            f.Lines = all.Take(8).ToList();
            return f;
        }

        private static void Leaves(JToken a, JToken b, string path, List<string> sink, int cap)
        {
            if (sink.Count >= cap) return;
            if (a is JObject ao && b is JObject bo)
            {
                foreach (var n in ao.Properties().Select(p => p.Name).Union(bo.Properties().Select(p => p.Name)))
                    Leaves(ao[n], bo[n], path + "." + n, sink, cap);
                return;
            }
            if (a is JArray aa && b is JArray ba && aa.Count == ba.Count)
            {
                for (int i = 0; i < aa.Count; i++) Leaves(aa[i], ba[i], $"{path}[{i}]", sink, cap);
                return;
            }
            if (JToken.DeepEquals(a, b)) return;
            sink.Add($"{path}: {Show(a)} \u2192 {Show(b)}");
        }

        public static string Show(JToken t)
        {
            if (t == null) return "(not set)";
            string s = t is JValue v ? (v.Value?.ToString() ?? "null") : t.ToString(Formatting.None);
            if (s.Length == 0) return "\"\"";
            return s.Length > 70 ? s.Substring(0, 67) + "..." : s;
        }

        // ─────────────────────────────────────────────────────────────
        //  MERGE
        // ─────────────────────────────────────────────────────────────

        private static IDictionary DictOf(ModProject p, string family)
        {
            var f = typeof(ModProject).GetField(family, BindingFlags.Public | BindingFlags.Instance);
            return f?.GetValue(p) as IDictionary;
        }

        private static Type DefTypeOf(string family)
            => typeof(ModProject).GetField(family, BindingFlags.Public | BindingFlags.Instance)?.FieldType.GetGenericArguments()[1];

        /// <summary>The entity currently stored under a family and id, or null.</summary>
        public static object Get(ModProject p, string family, string id)
        {
            var d = DictOf(p, family);
            return d != null && d.Contains(id) ? d[id] : null;
        }

        /// <summary>Takes theirs for a whole entity: add it, replace it, or delete it.</summary>
        public static void TakeEntity(ModProject mine, EntityDiff d)
        {
            var dict = DictOf(mine, d.Family) ?? throw new InvalidOperationException("Unknown family " + d.Family);
            switch (d.Kind)
            {
                case DiffKind.Removed:
                    DeleteFiles(mine, d); // files first: if that fails the entry is still there
                    dict.Remove(d.Id);
                    break;
                default:
                    var clone = Clone(d.TheirsDef);
                    Prepare(clone);
                    Save(mine, d, clone);
                    dict[d.Id] = clone;
                    break;
            }
            mine.IsDirty = true;
        }

        /// <summary>Takes theirs for one field of a changed entity.</summary>
        public static void TakeField(ModProject mine, EntityDiff d, FieldDiff f)
        {
            if (d.Kind != DiffKind.Changed) { TakeEntity(mine, d); return; }
            var dict = DictOf(mine, d.Family);
            var current = dict[d.Id];
            var jo = ToJson(current);

            JObject parent = jo;
            for (int i = 0; i < f.Path.Length - 1; i++)
            {
                if (parent[f.Path[i]] is not JObject next)
                {
                    if (f.Theirs == null) { parent = null; break; }
                    next = new JObject();
                    parent[f.Path[i]] = next;
                }
                parent = next;
            }
            if (parent != null)
            {
                string leaf = f.Path[f.Path.Length - 1];
                if (f.Theirs == null) parent.Remove(leaf);
                else parent[leaf] = f.Theirs.DeepClone();
            }

            var updated = FromJson(jo, current.GetType());
            Prepare(updated);
            Save(mine, d, updated);
            dict[d.Id] = updated;
            mine.IsDirty = true;
        }

        private static void SetMeta(ModProject mine, FieldDiff f)
        {
            switch (f.Key)
            {
                case "ModName": mine.ModName = (string)f.Theirs ?? ""; break;
                case "Author": mine.Author = (string)f.Theirs ?? ""; break;
                case "Version": mine.Version = (string)f.Theirs ?? ""; break;
                case "Description": mine.Description = (string)f.Theirs ?? ""; break;
                case "StarterNodeId": mine.StarterNodeId = (string)f.Theirs ?? ""; break;
                case "Dependencies":
                    mine.Dependencies = f.Theirs is JArray arr ? arr.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>();
                    break;
            }
        }

        public static void TakeMeta(ModProject mine, FieldDiff f)
        {
            SetMeta(mine, f);
            ModProjectLoader.SaveMetadata(mine);
            mine.IsDirty = true;
        }


        /// <summary>
        /// Brings the open project in line with files that changed outside the editor, writing nothing back.
        /// Changed entities are updated in place so editors holding them keep working. Removals are skipped unless allowed.
        /// </summary>
        public static ExternalApply ApplyExternal(ModProject mine, ProjectDiff diff, bool allowRemovals)
        {
            var r = new ExternalApply();
            foreach (var f in diff.Meta) { SetMeta(mine, f); r.Meta.Add(f.Key); }
            foreach (var e in diff.Entities)
            {
                var dict = DictOf(mine, e.Family);
                if (dict == null) continue;
                try
                {
                switch (e.Kind)
                {
                    case DiffKind.Removed:
                        if (!allowRemovals) { r.SkippedRemovals.Add(e.Key); break; }
                        dict.Remove(e.Id);
                        r.Removed.Add(e.Key);
                        break;
                    case DiffKind.Added:
                        Prepare(e.TheirsDef);
                        dict[e.Id] = e.TheirsDef;
                        r.Added.Add(e.Key);
                        r.Reload.Add(e.TheirsDef);
                        break;
                    default:
                        if (e.MineDef is ZoneDef || e.MineDef is ZonePatchDef)
                        {
                            dict[e.Id] = e.TheirsDef;
                            r.Reload.Add(e.TheirsDef);
                        }
                        else
                        {
                            string before = JsonConvert.SerializeObject(e.MineDef);
                            try
                            {
                                DefCopy.CopyInto(e.MineDef, JsonConvert.DeserializeObject(JsonConvert.SerializeObject(e.TheirsDef), e.MineDef.GetType()));
                                Prepare(e.MineDef);
                            }
                            catch
                            {
                                try { DefCopy.RestoreFromJson(e.MineDef, before); } catch { /* best effort: put the old values back */ }
                                throw;
                            }
                            r.Reload.Add(e.MineDef);
                        }
                        r.Changed.Add(e.Key);
                        break;
                }
                }
                catch (Exception ex)
                {
                    r.Errors.Add($"{e.Key}: {ex.Message}");
                }
            }
            return r;
        }

        private static void Prepare(object def)
        {
            if (def is CharacterVisualDef g) g.Normalize();
        }

        private static void Save(ModProject p, EntityDiff d, object def)
            => ModReferences.SaveEntry(p, new ModReferences.Entry { Family = d.Family, Id = d.Id, IsPatch = d.IsPatch, Def = def });

        private static void DeleteFiles(ModProject p, EntityDiff d)
        {
            switch (d.MineDef)
            {
                case CardDef: ModProjectLoader.DeleteCard(p, d.Id, d.IsPatch); return;
                case ZoneDef: UnknownMod.Editor.Tabs.ZoneTabEditor.DeleteZoneFolder(p, d.Id, false); return;
                case ZonePatchDef: UnknownMod.Editor.Tabs.ZoneTabEditor.DeleteZoneFolder(p, d.Id, true); return;
            }
            string folder = ModReferences.FolderFor(d.MineDef.GetType());
            if (folder != null) ModProjectLoader.DeleteEntity(p, folder, d.Id, d.IsPatch);
        }
    }

    /// <summary>Copies of a mod's JSON data taken before risky changes, so they can be compared with and restored later.</summary>
    public static class ModSnapshots
    {
        private const int KeepPerMod = 40;

        public sealed class Info
        {
            public string Name, Path, Label;
            public DateTime Time;
        }

        private static string Root(string modId) => System.IO.Path.Combine(ModProjectLoader.DataRoot, "_snapshots", modId);

        public static string Take(ModProject p, string label)
        {
            string src = ModProjectLoader.ModFolder(p.ModId);
            if (!Directory.Exists(src)) return null;
            string clean = new string((label ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ').ToArray()).Trim().Replace(' ', '-');
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + (clean.Length > 0 ? "~" + clean : "");
            string dest = System.IO.Path.Combine(Root(p.ModId), name);
            foreach (var file in Directory.GetFiles(src, "*.json", SearchOption.AllDirectories))
            {
                string rel = file.Substring(src.Length).TrimStart('\\', '/');
                string target = System.IO.Path.Combine(dest, rel);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
            Prune(p.ModId);
            return name;
        }

        public static List<Info> List(string modId)
        {
            var result = new List<Info>();
            string root = Root(modId);
            if (!Directory.Exists(root)) return result;
            foreach (var dir in Directory.GetDirectories(root))
            {
                string name = System.IO.Path.GetFileName(dir);
                var parts = name.Split(new[] { '~' }, 2);
                DateTime.TryParseExact(parts[0], "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var t);
                result.Add(new Info { Name = name, Path = dir, Label = parts.Length > 1 ? parts[1] : "", Time = t });
            }
            return result.OrderByDescending(i => i.Name, StringComparer.Ordinal).ToList();
        }

        public static ModProject Open(string modId, Info info) => ModProjectLoader.LoadFrom(modId, info.Path);

        public static void Delete(Info info)
        {
            try { Directory.Delete(info.Path, true); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Snapshots] Could not delete {info.Path}: {ex.Message}"); }
        }

        private static void Prune(string modId)
        {
            var all = List(modId);
            foreach (var old in all.Skip(KeepPerMod)) Delete(old);
        }
    }
}
