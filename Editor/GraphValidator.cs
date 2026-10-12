using System;
using System.Collections.Generic;
using System.Linq;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Structure checks that need the whole project in view: the zone node graph, event chains, combat slots, ids that
    /// will not survive the game's key normalisation, and new content nothing points at.
    /// </summary>
    public static class GraphValidator
    {
        public static List<DiagMessage> Validate(ModProject p)
        {
            var r = new List<DiagMessage>();
            if (p == null) return r;
            CheckIds(p, r);
            CheckCombats(p, r);
            CheckCardText(p, r);
            foreach (var z in p.Zones.Values) CheckZone(z, r);
            CheckEventChains(p, r);
            CheckUnreferenced(p, r);
            return r;
        }

        private static void Add(List<DiagMessage> r, DiagSeverity s, string at, string msg) => r.Add(new DiagMessage(s, $"{at}: {msg}"));

        // ── Ids ──────────────────────────────────────────────────

        private static void CheckIds(ModProject p, List<DiagMessage> r)
        {
            foreach (var e in ModReferences.Entities(p))
            {
                string at = $"{Singular(e.Family)}/{e.Id}";
                if (string.IsNullOrEmpty(e.Id)) { Add(r, DiagSeverity.Error, at, "empty id"); continue; }
                if (e.Id.Any(char.IsWhiteSpace))
                    Add(r, DiagSeverity.Error, at, "id contains whitespace; the game strips spaces when it normalises keys, so lookups by the typed id miss");
                if (e.Def is IModEntity me && !string.IsNullOrEmpty(me.EntityId) && !string.Equals(me.EntityId, e.Id, StringComparison.Ordinal))
                    Add(r, DiagSeverity.Warning, at, $"stored under key '{e.Id}' but the definition says id '{me.EntityId}'");
            }
        }

        private static string Singular(string family)
        {
            if (family.EndsWith("Patches", StringComparison.Ordinal)) return family.Substring(0, family.Length - 7) + "Patch";
            if (family.EndsWith("ies", StringComparison.Ordinal)) return family.Substring(0, family.Length - 3) + "y";
            return family.EndsWith("s", StringComparison.Ordinal) ? family.Substring(0, family.Length - 1) : family;
        }

        // ── Card text ────────────────────────────────────────────

        private static void CheckCardText(ModProject p, List<DiagMessage> r)
        {
            foreach (var c in p.Cards.Values.Concat(p.CardPatches.Values))
            {
                if (c == null) continue;
                foreach (var text in new[] { c.DescriptionText, c.PreDescriptionText, c.PostDescriptionText })
                {
                    if (string.IsNullOrEmpty(text)) continue;
                    var unknown = DescriptionCompiler.UnknownTokens(c, text);
                    if (unknown.Count > 0)
                        Add(r, DiagSeverity.Warning, $"Card/{c.Id}", $"description text has unknown token(s) {string.Join(", ", unknown)}; they will show as typed");
                }
            }
        }

        // ── Combats ──────────────────────────────────────────────

        private static void CheckCombats(ModProject p, List<DiagMessage> r)
        {
            foreach (var c in p.Combats.Values)
            {
                if (c == null) continue;
                string at = $"Combat/{c.CombatId}";
                if (c.NpcIds == null || c.NpcIds.Count == 0)
                {
                    Add(r, DiagSeverity.Error, at, "has no enemies");
                    continue;
                }
                int lastNpc = c.NpcIds.FindLastIndex(id => !string.IsNullOrWhiteSpace(id));
                for (int i = 0; i < lastNpc; i++)
                    if (string.IsNullOrWhiteSpace(c.NpcIds[i]))
                        Add(r, DiagSeverity.Warning, at, $"enemy slot {i + 1} is blank; blank slots are dropped, so later enemies move up a position");
                if (lastNpc < 0)
                    Add(r, DiagSeverity.Error, at, "has no enemies");
                if (c.NpcIds.Count > 4)
                    Add(r, DiagSeverity.Warning, at, $"{c.NpcIds.Count} enemies; the battlefield has 4 NPC positions");
            }
            foreach (var z in p.Zones.Values)
            {
                if (z?.Nodes == null) continue;
                foreach (var n in z.Nodes.Values)
                    if (n?.CombatIds != null && n.CombatIds.Any(string.IsNullOrWhiteSpace))
                        Add(r, DiagSeverity.Error, $"Zone/{z.ZoneId}", $"node '{n.NodeId}' has a blank combat entry");
            }
        }

        // ── Zone node graph ──────────────────────────────────────

        private static void CheckZone(ZoneDef z, List<DiagMessage> r)
        {
            if (z?.Nodes == null || z.Nodes.Count == 0) return;
            string at = $"Zone/{z.ZoneId}";
            var ids = new HashSet<string>(z.Nodes.Keys, StringComparer.OrdinalIgnoreCase);
            var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in z.Nodes.Keys) { edges[k] = new List<string>(); indegree[k] = 0; }

            foreach (var kv in z.Nodes)
            {
                var n = kv.Value;
                if (n == null) continue;
                string nid = kv.Key;
                if (n.Connections != null)
                {
                    foreach (var to in n.Connections)
                    {
                        if (string.IsNullOrEmpty(to)) { Add(r, DiagSeverity.Warning, at, $"node '{nid}' has a blank connection"); continue; }
                        if (string.Equals(to, nid, StringComparison.OrdinalIgnoreCase)) { Add(r, DiagSeverity.Warning, at, $"node '{nid}' connects to itself"); continue; }
                        if (!ids.Contains(to)) { Add(r, DiagSeverity.Error, at, $"node '{nid}' connects to '{to}' which does not exist in this zone"); continue; }
                        if (edges[nid].Contains(to, StringComparer.OrdinalIgnoreCase)) { Add(r, DiagSeverity.Warning, at, $"node '{nid}' lists '{to}' twice"); continue; }
                        edges[nid].Add(to);
                        indegree[to]++;
                    }
                }
                if (n.ConnectionRequirements != null)
                    foreach (var cr in n.ConnectionRequirements)
                    {
                        if (cr == null) continue;
                        if (!string.IsNullOrEmpty(cr.TargetNodeId) && !ids.Contains(cr.TargetNodeId))
                            Add(r, DiagSeverity.Error, at, $"node '{nid}' connection requirement targets missing node '{cr.TargetNodeId}'");
                        if (!string.IsNullOrEmpty(cr.IfNotNodeId) && !ids.Contains(cr.IfNotNodeId))
                            Add(r, DiagSeverity.Error, at, $"node '{nid}' connection requirement falls back to missing node '{cr.IfNotNodeId}'");
                    }
                if (n.NodeEventPercent != null && n.EventIds != null && n.NodeEventPercent.Count != 0 && n.NodeEventPercent.Count != n.EventIds.Count)
                    Add(r, DiagSeverity.Warning, at, $"node '{nid}' has {n.EventIds.Count} events but {n.NodeEventPercent.Count} event percents");
                if (n.NodeEventPriority != null && n.EventIds != null && n.NodeEventPriority.Count != 0 && n.NodeEventPriority.Count != n.EventIds.Count)
                    Add(r, DiagSeverity.Warning, at, $"node '{nid}' has {n.EventIds.Count} events but {n.NodeEventPriority.Count} event priorities");
                if (!string.IsNullOrEmpty(n.ReplacementEventId) && string.IsNullOrEmpty(n.ExistsSku))
                    Add(r, DiagSeverity.Info, at, $"node '{nid}' has a replacement event but no Exists SKU, so it is never used");
                if (!string.IsNullOrEmpty(n.ReplacementCombatId) && string.IsNullOrEmpty(n.ExistsSku))
                    Add(r, DiagSeverity.Info, at, $"node '{nid}' has a replacement combat but no Exists SKU, so it is never used");
            }

            var undirected = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in edges.Keys) undirected[k] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in edges) foreach (var to in kv.Value) { undirected[kv.Key].Add(to); undirected[to].Add(kv.Key); }

            foreach (var kv in z.Nodes)
            {
                var n = kv.Value;
                if (n == null) continue;
                bool isolated = undirected[kv.Key].Count == 0;
                if (isolated && z.Nodes.Count > 1)
                    Add(r, DiagSeverity.Warning, at, $"node '{kv.Key}' is not connected to anything");
                bool hasContent = (n.CombatIds != null && n.CombatIds.Count > 0) || (n.EventIds != null && n.EventIds.Count > 0)
                                  || n.TravelDestination || n.GoToTown || !string.IsNullOrEmpty(n.ReplacementEventId) || !string.IsNullOrEmpty(n.ReplacementCombatId);
                if (!hasContent && !isolated)
                    Add(r, DiagSeverity.Info, at, $"node '{kv.Key}' has no combat, event, town or travel target; stepping on it does nothing");
            }

            var components = new List<List<string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in z.Nodes.Keys)
            {
                if (seen.Contains(k) || undirected[k].Count == 0) continue;
                var comp = new List<string>();
                var stack = new Stack<string>();
                stack.Push(k); seen.Add(k);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop(); comp.Add(cur);
                    foreach (var nx in undirected[cur]) if (seen.Add(nx)) stack.Push(nx);
                }
                components.Add(comp);
            }
            if (components.Count > 1)
            {
                var sorted = components.OrderByDescending(c => c.Count).ToList();
                foreach (var c in sorted.Skip(1))
                    Add(r, DiagSeverity.Warning, at, $"{c.Count} node(s) form a separate island unreachable from the main map: {string.Join(", ", c.Take(6))}{(c.Count > 6 ? ", ..." : "")}");
            }

            // Directed: walk from nodes nothing leads into. Nodes only reachable inside a cycle are flagged.
            var roots = indegree.Where(kv => kv.Value == 0 && undirected[kv.Key].Count > 0).Select(kv => kv.Key).ToList();
            if (roots.Count == 0 && components.Count > 0)
            {
                Add(r, DiagSeverity.Info, at, "every node has an incoming connection, so the map has no clear entry point");
            }
            else
            {
                var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<string>(roots);
                foreach (var k in roots) reached.Add(k);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    foreach (var nx in edges[cur]) if (reached.Add(nx)) queue.Enqueue(nx);
                }
                var unreached = z.Nodes.Keys.Where(k => undirected[k].Count > 0 && !reached.Contains(k)).ToList();
                if (unreached.Count > 0)
                    Add(r, DiagSeverity.Info, at, $"{unreached.Count} node(s) can only be reached through a loop: {string.Join(", ", unreached.Take(6))}");
            }

            var deadEnds = z.Nodes.Where(kv => kv.Value != null && edges[kv.Key].Count == 0 && undirected[kv.Key].Count > 0
                                               && !kv.Value.TravelDestination && !kv.Value.GoToTown).Select(kv => kv.Key).ToList();
            if (deadEnds.Count > 0)
                Add(r, DiagSeverity.Info, at, $"dead-end node(s) with no outgoing connection and no travel/town exit: {string.Join(", ", deadEnds.Take(8))}");
        }

        // ── Event chains ─────────────────────────────────────────

        private static IEnumerable<OutcomeDef> ActiveOutcomes(ReplyDef rep)
        {
            if (rep.Ss != null) yield return rep.Ss;
            if (rep.HasRoll)
            {
                if (rep.Fl != null) yield return rep.Fl;
                if (rep.Ssc != null && rep.RollCrit >= 0) yield return rep.Ssc;
                if (rep.Flc != null && rep.RollCritFail >= 0) yield return rep.Flc;
            }
        }

        private static void CheckEventChains(ModProject p, List<DiagMessage> r)
        {
            // Effective events: new ones plus patches (a patch replaces the event with the same id)
            var all = new Dictionary<string, EventDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in p.Events) all[kv.Key] = kv.Value;
            foreach (var kv in p.EventPatches) all[kv.Key] = kv.Value;
            var ids = new HashSet<string>(all.Keys, StringComparer.OrdinalIgnoreCase);
            var exits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool changed = true;
            // An event can be left when one reply outcome ends it or hands off to an event that can be left (or one this mod does not own).
            while (changed)
            {
                changed = false;
                foreach (var e in all.Values)
                {
                    if (e == null || exits.Contains(e.EventId) || e.Replies == null) continue;
                    foreach (var rep in e.Replies)
                    {
                        if (rep == null) continue;
                        bool leaves = false;
                        foreach (var o in ActiveOutcomes(rep))
                        {
                            if (string.IsNullOrEmpty(o.EventId) || !ids.Contains(o.EventId) || exits.Contains(o.EventId)) { leaves = true; break; }
                        }
                        if (leaves) { exits.Add(e.EventId); changed = true; break; }
                    }
                }
            }
            foreach (var e in all.Values)
            {
                if (e == null || e.Replies == null || e.Replies.Count == 0) continue;
                if (!exits.Contains(e.EventId))
                    Add(r, DiagSeverity.Error, $"Event/{e.EventId}", "every reply leads on to another event that never ends; the player can never leave this chain");
                for (int i = 0; i < e.Replies.Count; i++)
                {
                    var rep = e.Replies[i];
                    if (rep == null) continue;
                    foreach (var o in ActiveOutcomes(rep))
                        if (string.Equals(o.EventId, e.EventId, StringComparison.OrdinalIgnoreCase) && !HasOtherExit(e, rep))
                            Add(r, DiagSeverity.Warning, $"Event/{e.EventId}", $"reply {i + 1} loops back to this event and no other reply ends it");
                }
            }
        }

        private static bool HasOtherExit(EventDef e, ReplyDef self)
        {
            foreach (var rep in e.Replies)
            {
                if (rep == null || ReferenceEquals(rep, self)) continue;
                foreach (var o in ActiveOutcomes(rep))
                    if (!string.Equals(o.EventId, e.EventId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ── Unreferenced content ─────────────────────────────────

        private static void CheckUnreferenced(ModProject p, List<DiagMessage> r)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in ModReferences.Entities(p))
            {
                if (e.Def == null) continue;
                ModReferences.Walk(e.Def, "", 0, true, (path, value, setter, owner) => used.Add(value));
            }
            void Check(string kind, IEnumerable<string> newIds)
            {
                foreach (var id in newIds)
                    if (!string.IsNullOrEmpty(id) && !used.Contains(id))
                        Add(r, DiagSeverity.Info, $"{kind}/{id}", "nothing in this mod references it (fine if another mod or a vanilla patch uses it)");
            }
            Check("Event", p.Events.Keys);
            Check("Combat", p.Combats.Keys);
            Check("Npc", p.Npcs.Keys);
            Check("Loot", p.Loot.Keys);
        }

        public static int LogReport(ModProject p)
        {
            var msgs = Validate(p);
            foreach (var m in msgs)
            {
                if (m.Severity == DiagSeverity.Error) Plugin.Log.LogError($"[GraphCheck:{p.ModId}] {m.Message}");
                else if (m.Severity == DiagSeverity.Warning) Plugin.Log.LogWarning($"[GraphCheck:{p.ModId}] {m.Message}");
            }
            if (msgs.Count == 0) Plugin.Log.LogInfo($"[GraphCheck:{p.ModId}] Structure OK.");
            return msgs.Count;
        }
    }
}
