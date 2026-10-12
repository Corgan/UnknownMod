using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public sealed class ModLink
    {
        public string From;      // the mod that needs something
        public string To;        // the mod that provides it
        public bool Declared;    // listed in From's depends
        public bool Uses;        // From refers to ids defined by To
        public bool Patches;     // From patches entities defined by To
        public bool Missing;     // To isn't installed
        public int UseCount, PatchCount;
        public List<string> Examples = new();
        public bool Undeclared => (Uses || Patches) && !Declared;
    }

    public sealed class ModConflict
    {
        public string Kind;                 // "Duplicate id" or "Both patch"
        public string Family, Id;
        public List<string> Mods = new();   // in load order; the last one wins
        public List<string> Fields = new(); // for patches: fields set differently
    }

    public sealed class ModProblem
    {
        public ReachSeverity Severity;
        public string ModId;
        public string Message;
        public string AddDependency;        // offered as a one-click fix when set
    }

    public sealed class ModGraphNode
    {
        public string Id, Name, Version;
        public bool Enabled;
        public int OrderIndex, Level;
        public int Entities, Patches;
        public ModProject Project;
    }

    public sealed class ModGraphResult
    {
        public List<ModGraphNode> Nodes = new();
        public List<ModLink> Links = new();
        public List<ModConflict> Conflicts = new();
        public List<ModProblem> Problems = new();
    }

    /// <summary>Works out how installed mods depend on each other, from both their declared depends and the ids they actually use.</summary>
    public static class ModGraph
    {
        public static ModGraphResult Build(IList<ModProject> projects)
        {
            var r = new ModGraphResult();
            var order = LoadOrderManager.Order.ToList();
            int OrderOf(string id) { int i = order.FindIndex(o => string.Equals(o, id, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 10000 : i; }

            var ordered = projects.OrderBy(p => OrderOf(p.ModId)).ThenBy(p => p.ModId, StringComparer.OrdinalIgnoreCase).ToList();
            var byId = ordered.ToDictionary(p => p.ModId, p => p, StringComparer.OrdinalIgnoreCase);
            var entities = ordered.ToDictionary(p => p.ModId, p => ModReferences.Entities(p).Where(e => e.Def != null).ToList(), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                r.Nodes.Add(new ModGraphNode
                {
                    Id = p.ModId, Name = string.IsNullOrEmpty(p.ModName) ? p.ModId : p.ModName, Version = p.Version,
                    Enabled = LoadOrderManager.IsEnabled(p.ModId), OrderIndex = i, Project = p,
                    Entities = entities[p.ModId].Count(e => !e.IsPatch), Patches = entities[p.ModId].Count(e => e.IsPatch)
                });
            }
            var nodeById = r.Nodes.ToDictionary(n => n.Id, n => n, StringComparer.OrdinalIgnoreCase);

            // who defines which id
            var providers = new Dictionary<string, List<(string mod, string family)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ordered)
                foreach (var e in entities[p.ModId].Where(e => !e.IsPatch))
                {
                    if (!providers.TryGetValue(e.Id, out var l)) providers[e.Id] = l = new();
                    l.Add((p.ModId, e.Family));
                }

            var links = new Dictionary<(string, string), ModLink>();
            ModLink Link(string from, string to)
            {
                if (!links.TryGetValue((from, to), out var l))
                    links[(from, to)] = l = new ModLink { From = from, To = to };
                return l;
            }
            void Example(ModLink l, string text) { if (l.Examples.Count < 6 && !l.Examples.Contains(text)) l.Examples.Add(text); }

            foreach (var p in ordered)
            {
                foreach (var dep in p.Dependencies ?? new List<string>())
                {
                    var l = Link(p.ModId, dep);
                    l.Declared = true;
                    l.Missing = !byId.ContainsKey(dep);
                }

                foreach (var e in entities[p.ModId])
                {
                    if (e.IsPatch)
                    {
                        string baseFamily = e.Family.Substring(0, e.Family.Length - "Patches".Length);
                        if (providers.TryGetValue(e.Id, out var provs))
                            foreach (var (mod, fam) in provs)
                                if (fam == baseFamily && !string.Equals(mod, p.ModId, StringComparison.OrdinalIgnoreCase)
                                    && !provs.Any(x => string.Equals(x.mod, p.ModId, StringComparison.OrdinalIgnoreCase) && x.family == fam))
                                {
                                    var l = Link(p.ModId, mod);
                                    l.Patches = true; l.PatchCount++;
                                    Example(l, $"patches {fam}/{e.Id}");
                                }
                    }
                    ModReferences.Walk(e.Def, "", 0, true, (path, value, setter, owner) =>
                    {
                        if (!providers.TryGetValue(value, out var provs)) return;
                        if (provs.Any(x => string.Equals(x.mod, p.ModId, StringComparison.OrdinalIgnoreCase))) return;
                        foreach (var mod in provs.Select(x => x.mod).Distinct())
                        {
                            var l = Link(p.ModId, mod);
                            l.Uses = true; l.UseCount++;
                            Example(l, $"{e.Family}/{e.Id} \u2192 {value}");
                        }
                    });
                }
            }
            r.Links = links.Values.OrderBy(l => l.From, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.To, StringComparer.OrdinalIgnoreCase).ToList();

            FindProblems(r, byId, OrderOf);
            FindLevels(r);
            FindConflicts(r, ordered, entities, OrderOf);
            return r;
        }

        private static HashSet<string> DeclaredClosure(string from, Dictionary<string, ModProject> byId)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!byId.TryGetValue(cur, out var p)) continue;
                foreach (var d in p.Dependencies ?? new List<string>())
                    if (seen.Add(d)) stack.Push(d);
            }
            return seen;
        }

        private static void FindProblems(ModGraphResult r, Dictionary<string, ModProject> byId, Func<string, int> orderOf)
        {
            foreach (var l in r.Links)
            {
                bool fromEnabled = LoadOrderManager.IsEnabled(l.From);
                if (l.Declared && l.Missing)
                    r.Problems.Add(new ModProblem { Severity = ReachSeverity.Error, ModId = l.From, Message = $"depends on '{l.To}', which isn't installed. {l.From} will not load." });
                else if (l.Declared && !LoadOrderManager.IsEnabled(l.To) && fromEnabled)
                    r.Problems.Add(new ModProblem { Severity = ReachSeverity.Error, ModId = l.From, Message = $"depends on '{l.To}', which is disabled. {l.From} will not load." });
                else if (l.Declared && orderOf(l.To) > orderOf(l.From))
                    r.Problems.Add(new ModProblem { Severity = ReachSeverity.Info, ModId = l.From, Message = $"depends on '{l.To}', which is listed after it in the load order; it is moved ahead automatically." });

                if (l.Undeclared && !l.Missing && DeclaredClosure(l.From, byId).Contains(l.To) == false)
                {
                    string what = l.Uses && l.Patches ? "uses and patches content from" : l.Patches ? "patches content from" : "uses content from";
                    r.Problems.Add(new ModProblem
                    {
                        Severity = ReachSeverity.Warning, ModId = l.From, AddDependency = l.To,
                        Message = $"{what} '{l.To}' ({string.Join("; ", l.Examples.Take(2))}) but doesn't list it in depends, so load order isn't guaranteed."
                    });
                }
            }

            // cycles over declared links
            var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string id, List<string> stack)
            {
                if (!byId.TryGetValue(id, out var p)) return;
                state[id] = 1; stack.Add(id);
                foreach (var d in p.Dependencies ?? new List<string>())
                {
                    if (!byId.ContainsKey(d)) continue;
                    state.TryGetValue(d, out int s);
                    if (s == 1)
                    {
                        var cycle = stack.Skip(stack.FindIndex(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase))).Concat(new[] { d }).ToList();
                        string key = string.Join(">", cycle.Select(c => c.ToLowerInvariant()).OrderBy(c => c));
                        if (reported.Add(key))
                            r.Problems.Add(new ModProblem { Severity = ReachSeverity.Error, ModId = d, Message = "Dependency cycle: " + string.Join(" \u2192 ", cycle) });
                    }
                    else if (s == 0) Visit(d, stack);
                }
                stack.RemoveAt(stack.Count - 1); state[id] = 2;
            }
            foreach (var id in byId.Keys.ToList()) if (!state.ContainsKey(id)) Visit(id, new List<string>());

            r.Problems = r.Problems.OrderBy(p => p.Severity).ThenBy(p => p.ModId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Columns for drawing: a mod sits one column to the right of everything it needs.</summary>
        private static void FindLevels(ModGraphResult r)
        {
            var needs = r.Links.Where(l => !l.Missing && (l.Declared || l.Uses || l.Patches))
                .GroupBy(l => l.From, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(l => l.To).ToList(), StringComparer.OrdinalIgnoreCase);
            var memo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int Level(string id)
            {
                if (memo.TryGetValue(id, out int v)) return v;
                if (!visiting.Add(id)) return 0;
                int best = 0;
                if (needs.TryGetValue(id, out var list))
                    foreach (var d in list) best = Math.Max(best, Level(d) + 1);
                visiting.Remove(id);
                return memo[id] = best;
            }
            foreach (var n in r.Nodes) n.Level = Level(n.Id);
        }

        private static void FindConflicts(ModGraphResult r, List<ModProject> ordered,
            Dictionary<string, List<ModReferences.Entry>> entities, Func<string, int> orderOf)
        {
            var groups = new Dictionary<(string, string, bool), List<(string mod, object def)>>();
            foreach (var p in ordered)
                foreach (var e in entities[p.ModId])
                {
                    var key = (e.Family, e.Id.ToLowerInvariant(), e.IsPatch);
                    if (!groups.TryGetValue(key, out var l)) groups[key] = l = new();
                    l.Add((p.ModId, e.Def));
                }

            foreach (var kv in groups.Where(g => g.Value.Select(x => x.mod).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            {
                var c = new ModConflict
                {
                    Kind = kv.Key.Item3 ? "Both patch" : "Duplicate id",
                    Family = kv.Key.Item1,
                    Id = kv.Value[0].def is IModEntity me ? me.EntityId : kv.Key.Item2,
                    Mods = kv.Value.Select(x => x.mod).ToList()
                };
                if (kv.Key.Item3) c.Fields = DifferingFields(kv.Value.Select(x => x.def).ToList());
                r.Conflicts.Add(c);
            }
            r.Conflicts = r.Conflicts.OrderBy(c => c.Kind).ThenBy(c => c.Family).ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> DifferingFields(List<object> defs)
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            var jos = defs.Select(d => JObject.Parse(JsonConvert.SerializeObject(d, settings))).ToList();
            var result = new List<string>();
            foreach (var name in jos.SelectMany(j => j.Properties().Select(p => p.Name)).Distinct())
            {
                if (name == "Nodes" || name == "Roads")
                {
                    foreach (var sub in jos.SelectMany(j => (j[name] as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>()).Distinct())
                        if (Differs(jos.Select(j => (j[name] as JObject)?[sub]).ToList())) result.Add(name + "/" + sub);
                    continue;
                }
                if (Differs(jos.Select(j => j[name]).ToList())) result.Add(name);
            }
            return result;
        }

        private static bool Differs(List<JToken> values)
        {
            var present = values.Where(v => v != null).ToList();
            if (present.Count < 2) return false;
            return present.Any(v => !JToken.DeepEquals(v, present[0]));
        }
    }
}
