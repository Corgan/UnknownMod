using System;
using System.Collections.Generic;
using System.Linq;
using UnknownMod.Definitions;
using UnknownMod.Editor;

namespace UnknownMod.Core
{
    public enum ReachStatus { Free, Gated, Unreachable, Absent }
    public enum ReachSeverity { Error, Warning, Info }

    public sealed class ReachIssue
    {
        public ReachSeverity Severity;
        public string NodeId;
        public string Message;
    }

    public sealed class ReachEdge
    {
        public string From, To;
        /// <summary>Empty when the edge is always open; otherwise a readable description of what it needs.</summary>
        public string Gate = "";
        public bool Dangling;
        public bool IsGated => Gate.Length > 0;
    }

    public sealed class ZoneReach
    {
        public string ZoneId;
        public string Entrance;
        public bool EntranceGuessed;
        public readonly Dictionary<string, ReachStatus> Status = new(StringComparer.Ordinal);
        public readonly List<ReachEdge> Edges = new();
        public readonly List<ReachIssue> Issues = new();
        public readonly Dictionary<string, ReachEdge> Parent = new(StringComparer.Ordinal);
        public int Free, Gated, Unreachable, Absent;

        public IEnumerable<ReachIssue> IssuesFor(string nodeId) => Issues.Where(i => i.NodeId == nodeId);

        /// <summary>Shortest route from the entrance, preferring ungated edges. Empty when the node can't be reached.</summary>
        public List<ReachEdge> PathTo(string nodeId)
        {
            var path = new List<ReachEdge>();
            int guard = 0;
            while (nodeId != null && Parent.TryGetValue(nodeId, out var e) && guard++ < 1000)
            {
                path.Add(e);
                nodeId = e.From;
            }
            path.Reverse();
            return path;
        }
    }

    public sealed class OrphanEntity
    {
        public string Family;
        public string Id;
        /// <summary>Other unreferenced entities that point at this one (so it is only "used" by dead content).</summary>
        public List<string> OnlyUsedBy = new();
    }

    /// <summary>
    /// Static checks for the world graph: which nodes the party can actually get to, which edges are gated,
    /// and which project entities nothing ever points at.
    /// </summary>
    public static class ReachabilityAnalyzer
    {
        public static ZoneReach Analyze(ZoneDef z, ModProject p)
        {
            var r = new ZoneReach { ZoneId = z.ZoneId };
            var nodes = z.Nodes ?? new Dictionary<string, NodeDef>();
            if (nodes.Count == 0)
            {
                r.Issues.Add(new ReachIssue { Severity = ReachSeverity.Error, Message = "The zone has no nodes." });
                return r;
            }

            var requirementIds = new HashSet<string>(RefIds.Requirements(p), StringComparer.OrdinalIgnoreCase);
            var combatIds = new HashSet<string>(RefIds.Combats(p), StringComparer.OrdinalIgnoreCase);
            var eventIds = new HashSet<string>(RefIds.Events(p), StringComparer.OrdinalIgnoreCase);

            FindEntrance(z, r);
            BuildEdges(nodes, r, requirementIds);
            ComputeStatus(nodes, r);
            CheckNodes(z, nodes, r, p, requirementIds, combatIds, eventIds);
            CheckRoads(z, r);
            return r;
        }

        private static void FindEntrance(ZoneDef z, ZoneReach r)
        {
            var nodes = z.Nodes;
            string Find(string id) => nodes.Keys.FirstOrDefault(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase));
            r.Entrance = Find(z.ZoneId.ToLowerInvariant() + "_0");
            if (r.Entrance == null && !string.IsNullOrEmpty(z.IdPrefix))
                r.Entrance = Find(z.IdPrefix + "_0") ?? Find(z.IdPrefix + "0");
            if (r.Entrance != null) return;

            r.EntranceGuessed = true;
            var incoming = new HashSet<string>(nodes.Values.SelectMany(n => n.Connections ?? new List<string>()), StringComparer.Ordinal);
            r.Entrance = nodes.Values.Where(n => !incoming.Contains(n.NodeId)).OrderBy(n => n.PosX).Select(n => n.NodeId).FirstOrDefault()
                         ?? nodes.Keys.First();
            bool obelisk = z.ObeliskLow || z.ObeliskHigh || z.ObeliskFinal;
            r.Issues.Add(new ReachIssue
            {
                Severity = obelisk ? ReachSeverity.Error : ReachSeverity.Info,
                NodeId = r.Entrance,
                Message = obelisk
                    ? $"Obelisk zones need an entrance node named '{z.ZoneId.ToLowerInvariant()}_0'. Using '{r.Entrance}' for this check."
                    : $"No '{z.ZoneId.ToLowerInvariant()}_0' node, so '{r.Entrance}' is assumed to be the entrance."
            });
        }

        private static void BuildEdges(Dictionary<string, NodeDef> nodes, ZoneReach r, HashSet<string> requirementIds)
        {
            foreach (var n in nodes.Values)
            {
                var targets = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in n.Connections ?? new List<string>())
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    if (!seen.Add(t))
                        r.Issues.Add(new ReachIssue { Severity = ReachSeverity.Info, NodeId = n.NodeId, Message = $"Connection to '{t}' is listed twice." });
                    else targets.Add(t);
                }
                foreach (var cr in n.ConnectionRequirements ?? new List<NodeConnectionReqDef>())
                    if (!string.IsNullOrEmpty(cr.TargetNodeId) && seen.Add(cr.TargetNodeId)) targets.Add(cr.TargetNodeId);

                foreach (var t in targets)
                {
                    var edge = new ReachEdge { From = n.NodeId, To = t, Dangling = !nodes.ContainsKey(t) };
                    var gates = new List<string>();
                    foreach (var cr in (n.ConnectionRequirements ?? new List<NodeConnectionReqDef>()).Where(c => c.TargetNodeId == t))
                    {
                        if (!string.IsNullOrEmpty(cr.RequirementId))
                        {
                            gates.Add("needs " + cr.RequirementId);
                            if (!requirementIds.Contains(cr.RequirementId))
                                r.Issues.Add(new ReachIssue { Severity = ReachSeverity.Error, NodeId = n.NodeId, Message = $"Connection to '{t}' needs requirement '{cr.RequirementId}', which doesn't exist." });
                        }
                        if (!string.IsNullOrEmpty(cr.IfNotNodeId))
                        {
                            gates.Add("only if '" + cr.IfNotNodeId + "' not visited");
                            if (!nodes.ContainsKey(cr.IfNotNodeId))
                                r.Issues.Add(new ReachIssue { Severity = ReachSeverity.Warning, NodeId = n.NodeId, Message = $"Connection to '{t}' depends on node '{cr.IfNotNodeId}', which isn't in this zone." });
                        }
                    }
                    if (!edge.Dangling && nodes[t].NodeRequirementId is { Length: > 0 } nreq)
                        gates.Add("node needs " + nreq);
                    edge.Gate = string.Join(", ", gates);
                    r.Edges.Add(edge);

                    if (edge.Dangling)
                        r.Issues.Add(new ReachIssue { Severity = ReachSeverity.Error, NodeId = n.NodeId, Message = $"Connects to '{t}', which isn't a node in this zone." });
                    else if (t == n.NodeId)
                        r.Issues.Add(new ReachIssue { Severity = ReachSeverity.Warning, NodeId = n.NodeId, Message = "Connects to itself." });
                }
            }
        }

        private static bool Absent(NodeDef n) => n.ExistsPercent <= 0;

        /// <summary>BFS from the entrance. Returns visited nodes and fills parent edges for new ones.</summary>
        private static HashSet<string> Bfs(Dictionary<string, NodeDef> nodes, ZoneReach r, IEnumerable<string> starts,
            bool allowGated, string excluded, Dictionary<string, ReachEdge> parents, HashSet<string> already = null)
        {
            var visited = already != null ? new HashSet<string>(already, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var s in starts)
            {
                if (s == excluded || !nodes.TryGetValue(s, out var sn) || Absent(sn)) continue;
                if (visited.Add(s) || already != null) queue.Enqueue(s);
            }
            var outgoing = r.Edges.Where(e => !e.Dangling).ToLookup(e => e.From, StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var e in outgoing[cur])
                {
                    if (e.To == excluded || visited.Contains(e.To)) continue;
                    if (!allowGated && e.IsGated) continue;
                    if (Absent(nodes[e.To])) continue;
                    visited.Add(e.To);
                    if (parents != null && !parents.ContainsKey(e.To)) parents[e.To] = e;
                    queue.Enqueue(e.To);
                }
            }
            return visited;
        }

        private static void ComputeStatus(Dictionary<string, NodeDef> nodes, ZoneReach r)
        {
            var starts = new[] { r.Entrance };
            var free = Bfs(nodes, r, starts, false, null, r.Parent);
            var all = Bfs(nodes, r, free, true, null, r.Parent, free);
            foreach (var id in nodes.Keys)
            {
                var st = Absent(nodes[id]) ? ReachStatus.Absent
                    : free.Contains(id) ? ReachStatus.Free
                    : all.Contains(id) ? ReachStatus.Gated
                    : ReachStatus.Unreachable;
                r.Status[id] = st;
                switch (st)
                {
                    case ReachStatus.Free: r.Free++; break;
                    case ReachStatus.Gated: r.Gated++; break;
                    case ReachStatus.Absent: r.Absent++; break;
                    default: r.Unreachable++; break;
                }
            }
        }

        private static void CheckNodes(ZoneDef z, Dictionary<string, NodeDef> nodes, ZoneReach r, ModProject p,
            HashSet<string> requirementIds, HashSet<string> combatIds, HashSet<string> eventIds)
        {
            var heroIds = new HashSet<string>(RefIds.SubClasses(p), StringComparer.OrdinalIgnoreCase);
            var allReach = r.Status.Where(kv => kv.Value == ReachStatus.Free || kv.Value == ReachStatus.Gated).Select(kv => kv.Key).ToList();
            var positions = new Dictionary<(int, int), string>();

            foreach (var n in nodes.Values)
            {
                string id = n.NodeId;
                void Add(ReachSeverity s, string m) => r.Issues.Add(new ReachIssue { Severity = s, NodeId = id, Message = m });
                var st = r.Status[id];

                foreach (var c in n.CombatIds ?? new List<string>())
                    if (!string.IsNullOrEmpty(c) && !combatIds.Contains(c)) Add(ReachSeverity.Error, $"Combat '{c}' doesn't exist.");
                foreach (var e in n.EventIds ?? new List<string>())
                    if (!string.IsNullOrEmpty(e) && !eventIds.Contains(e)) Add(ReachSeverity.Error, $"Event '{e}' doesn't exist.");
                if (!string.IsNullOrEmpty(n.NodeRequirementId) && !requirementIds.Contains(n.NodeRequirementId))
                    Add(ReachSeverity.Error, $"Node requirement '{n.NodeRequirementId}' doesn't exist.");
                if (!string.IsNullOrEmpty(n.ReplacementCombatId) && !combatIds.Contains(n.ReplacementCombatId))
                    Add(ReachSeverity.Warning, $"Replacement combat '{n.ReplacementCombatId}' doesn't exist.");
                if (!string.IsNullOrEmpty(n.ReplacementEventId) && !eventIds.Contains(n.ReplacementEventId))
                    Add(ReachSeverity.Warning, $"Replacement event '{n.ReplacementEventId}' doesn't exist.");
                if (!string.IsNullOrEmpty(n.HeroToDisableNodeWhenUnlockedId) && !heroIds.Contains(n.HeroToDisableNodeWhenUnlockedId))
                    Add(ReachSeverity.Warning, $"Hero '{n.HeroToDisableNodeWhenUnlockedId}' (disables this node when unlocked) doesn't exist.");

                var key = ((int)Math.Round(n.PosX * 20), (int)Math.Round(n.PosY * 20));
                if (positions.TryGetValue(key, out var other))
                    Add(ReachSeverity.Info, $"Sits on top of '{other}'.");
                else positions[key] = id;

                switch (st)
                {
                    case ReachStatus.Absent:
                        Add(ReachSeverity.Warning, "ExistsPercent is 0, so this node never appears.");
                        continue;
                    case ReachStatus.Unreachable:
                        Add(ReachSeverity.Error, "Can't be reached from the entrance: no chain of connections leads here.");
                        continue;
                    case ReachStatus.Gated:
                        var gates = r.PathTo(id).Where(e => e.IsGated).Select(e => $"{e.From}\u2192{e.To} ({e.Gate})");
                        Add(ReachSeverity.Info, "Only reachable through gated connections: " + string.Join("; ", gates));
                        break;
                }

                bool hasExit = r.Edges.Any(e => e.From == id && !e.Dangling && e.To != id);
                if (!hasExit && !n.TravelDestination && !n.GoToTown)
                    Add(ReachSeverity.Warning, "Dead end: the party can arrive but there is no connection out, and it isn't a travel destination or town exit.");

                bool empty = (n.CombatIds?.Count ?? 0) == 0 && (n.EventIds?.Count ?? 0) == 0
                             && !n.TravelDestination && !n.GoToTown && id != r.Entrance;
                if (empty) Add(ReachSeverity.Info, "Has no combat, event or travel link, so nothing happens here.");

                if (n.ExistsPercent < 100 && n.ExistsPercent > 0 && allReach.Count < 400)
                {
                    var without = Bfs(nodes, r, new[] { r.Entrance }, true, id, null);
                    int lost = allReach.Count(k => k != id && !without.Contains(k));
                    if (lost > 0)
                        Add(ReachSeverity.Warning, $"Only exists {n.ExistsPercent}% of the time, and {lost} node(s) can only be reached through it.");
                }
            }
        }

        private static void CheckRoads(ZoneDef z, ZoneReach r)
        {
            if (z.Roads == null) return;
            var linked = new HashSet<(string, string)>(r.Edges.Select(e => (e.From, e.To)));
            foreach (var kv in z.Roads)
            {
                var road = kv.Value;
                if (road == null) continue;
                bool fromOk = z.Nodes.ContainsKey(road.FromNodeId ?? ""), toOk = z.Nodes.ContainsKey(road.ToNodeId ?? "");
                if (!fromOk || !toOk)
                {
                    r.Issues.Add(new ReachIssue { Severity = ReachSeverity.Warning, NodeId = fromOk ? road.FromNodeId : toOk ? road.ToNodeId : null, Message = $"Road '{kv.Key}' points at a missing node ({(fromOk ? road.ToNodeId : road.FromNodeId)})." });
                    continue;
                }
                if (!linked.Contains((road.FromNodeId, road.ToNodeId)) && !linked.Contains((road.ToNodeId, road.FromNodeId)))
                    r.Issues.Add(new ReachIssue { Severity = ReachSeverity.Info, NodeId = road.FromNodeId, Message = $"Road '{kv.Key}' is drawn between '{road.FromNodeId}' and '{road.ToNodeId}', but they aren't connected." });
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  CONTENT REACHABILITY
        // ─────────────────────────────────────────────────────────────

        public static readonly string[] OrphanFamilies =
            { "Combats", "Events", "Loot", "Npcs", "Requirements", "AuraCurses", "Traits", "Cinematics", "KeyNotes" };

        /// <summary>
        /// Project entities in <paramref name="families"/> that nothing reachable refers to. Everything outside those families
        /// (cards, heroes, zones, patches...) counts as an entry point, and references spread from there.
        /// </summary>
        public static List<OrphanEntity> FindOrphans(ModProject p, ICollection<string> families)
        {
            var entries = ModReferences.Entities(p).Where(e => e.Def != null).ToList();
            string Key(ModReferences.Entry e) => e.Family + "/" + e.Id;
            bool Checked(ModReferences.Entry e) => !e.IsPatch && families.Contains(e.Family);

            var refs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ModReferences.Walk(e.Def, "", 0, true, (path, value, setter, owner) => set.Add(value));
                refs[Key(e)] = set;
            }

            var byId = new Dictionary<string, List<ModReferences.Entry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries.Where(Checked))
            {
                if (!byId.TryGetValue(e.Id, out var l)) byId[e.Id] = l = new List<ModReferences.Entry>();
                l.Add(e);
            }

            var reached = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<ModReferences.Entry>();
            foreach (var e in entries.Where(e => !Checked(e))) queue.Enqueue(e);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var v in refs[Key(cur)])
                {
                    if (!byId.TryGetValue(v, out var targets)) continue;
                    foreach (var t in targets)
                        if (reached.Add(Key(t))) queue.Enqueue(t);
                }
            }

            var result = new List<OrphanEntity>();
            var orphans = entries.Where(e => Checked(e) && !reached.Contains(Key(e))).ToList();
            foreach (var o in orphans)
            {
                var item = new OrphanEntity { Family = o.Family, Id = o.Id };
                foreach (var other in orphans)
                    if (Key(other) != Key(o) && refs[Key(other)].Contains(o.Id) && item.OnlyUsedBy.Count < 6)
                        item.OnlyUsedBy.Add(Key(other));
                result.Add(item);
            }
            return result.OrderBy(o => o.Family).ThenBy(o => o.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
