using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Runs the data-driven combat rules of every loaded mod (<see cref="RuleSetDef"/>).
    /// Game moments are noted the instant they happen, but the resulting actions are queued and run from the
    /// frame update while the game is idle. That keeps rules from changing the teams in the middle of the game's
    /// own loops. State (flags, "once" markers) lives for one fight and is not part of the game's save, so a fight
    /// that is saved and resumed starts its rules from scratch. Rules only run in single player: a random roll or a
    /// summon made on one machine would put the two players' fights out of step.
    /// </summary>
    public static class RuleEngine
    {
        private sealed class Pending
        {
            public string RuleKey;
            public string SetId;
            public RuleDef Rule;
            public Character Owner, Subject;
            public int Round;
        }

        private sealed class Active
        {
            public string ModId;
            public RuleSetDef Set;
            public Character Owner;
        }

        private sealed class Ctx
        {
            public Character Owner, Subject;
            public int Round;
        }

        private const int MaxErrorsPerRule = 3;
        private const int MaxPerTick = 8;
        private const int LogCap = 80;

        // The dictionaries are the projects' own, so edits in the editor apply to the next fight moment without a rebuild.
        private static readonly Dictionary<string, Dictionary<string, RuleSetDef>> Mods = new();

        private static MatchManager _match;
        private static bool _started;
        private static int _lastRound;
        private static int _roundEndDone;
        private static float _nextHpPoll;
        private static bool _mpNoted;
        private static readonly Queue<Pending> Queue = new();
        private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Once = new();
        private static readonly HashSet<string> Unique = new();
        private static readonly Dictionary<string, int> Errors = new();

        /// <summary>The most recent rule firings, newest last. Shown in the Rules panel.</summary>
        public static readonly List<string> Recent = new();

        // ── Catalog ────────────────────────────────────────────────

        public static void Register(ModProject proj)
        {
            if (proj == null) return;
            if (proj.RuleSets == null || proj.RuleSets.Count == 0) Mods.Remove(proj.ModId);
            else Mods[proj.ModId] = proj.RuleSets;
        }

        public static void Clear()
        {
            Mods.Clear();
            ResetFight();
        }

        public static bool HasRules
        {
            get
            {
                foreach (var d in Mods.Values) if (d.Count > 0) return true;
                return false;
            }
        }

        public static int RuleSetCount => Mods.Values.Sum(d => d.Count);

        /// <summary>Flags set in the current fight (for the panel).</summary>
        public static IEnumerable<string> CurrentFlags => Flags;

        // ── Fight state ────────────────────────────────────────────

        private static void ResetFight()
        {
            Queue.Clear();
            Flags.Clear();
            Once.Clear();
            Unique.Clear();
            Errors.Clear();
            _started = false;
            _lastRound = 0;
            _roundEndDone = 0;
            _nextHpPoll = 0;
        }

        /// <summary>The running fight, or null when there is none or the rules must stay idle. Starts fresh state for a new fight.</summary>
        private static MatchManager Sync()
        {
            MatchManager mm = null;
            try { mm = MatchManager.Instance; } catch { }
            if (mm == null)
            {
                if (_match != null) { _match = null; ResetFight(); }
                return null;
            }
            if (!ReferenceEquals(mm, _match)) { ResetFight(); _match = mm; }
            if (mm.MatchIsOver) { Queue.Clear(); return null; }
            if (IsMultiplayer())
            {
                if (!_mpNoted && HasRules)
                {
                    _mpNoted = true;
                    Plugin.Log.LogInfo("[Rules] Combat rules are single-player only and stay idle in multiplayer.");
                }
                return null;
            }
            return mm;
        }

        private static bool IsMultiplayer()
        {
            try { return GameManager.Instance != null && GameManager.Instance.IsMultiplayer(); }
            catch { return false; }
        }

        // ── Frame tick ─────────────────────────────────────────────

        /// <summary>Called every frame from the plugin.</summary>
        public static void Tick()
        {
            if (!HasRules) return;
            try
            {
                var mm = Sync();
                if (mm == null) return;

                if (!_started)
                {
                    if (!TeamsReady(mm)) return;
                    _started = true;
                    Fire(RuleTrigger.CombatStart, null, null);
                }

                int round = mm.GetCurrentRound();
                if (round > 0 && round > _lastRound)
                {
                    _lastRound = round;
                    Fire(RuleTrigger.RoundStart, null, null);
                }

                if (Time.unscaledTime >= _nextHpPoll)
                {
                    _nextHpPoll = Time.unscaledTime + 0.25f;
                    PollHp(mm);
                }

                if (Queue.Count > 0 && !mm.IsGameBusy())
                    Drain(mm);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Rules] Tick failed: {ex.Message}");
            }
        }

        private static bool TeamsReady(MatchManager mm)
        {
            try
            {
                return mm.GetTeamHero() != null && mm.GetTeamNPC() != null
                    && Living(mm.GetTeamHero()).Any() && Living(mm.GetTeamNPC()).Any();
            }
            catch { return false; }
        }

        // ── Game moments ───────────────────────────────────────────

        /// <summary>A combat event, fed from the SetEventCo hook.</summary>
        public static void OnEvent(Character who, Enums.EventActivation ev, string auxString)
        {
            if (!HasRules || who == null) return;
            if (ev != Enums.EventActivation.BeginTurn && ev != Enums.EventActivation.EndTurn &&
                ev != Enums.EventActivation.EndRound && ev != Enums.EventActivation.Damaged &&
                ev != Enums.EventActivation.CastCard) return;
            try
            {
                var mm = Sync();
                if (mm == null || !_started) return;
                switch (ev)
                {
                    case Enums.EventActivation.BeginTurn: Fire(RuleTrigger.TurnStart, who, null); break;
                    case Enums.EventActivation.EndTurn: Fire(RuleTrigger.TurnEnd, who, null); break;
                    case Enums.EventActivation.Damaged: Fire(RuleTrigger.Damaged, who, null); break;
                    case Enums.EventActivation.CastCard: Fire(RuleTrigger.CardPlayed, who, auxString); break;
                    case Enums.EventActivation.EndRound:
                        int round = mm.GetCurrentRound();
                        if (round > _roundEndDone)
                        {
                            _roundEndDone = round;
                            Fire(RuleTrigger.RoundEnd, null, null);
                        }
                        break;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Rules] Event {ev} failed: {ex.Message}"); }
        }

        /// <summary>A hero or enemy died, fed from the KillHero / KillNPC hooks.</summary>
        public static void OnKilled(Character c)
        {
            if (!HasRules || c == null) return;
            try
            {
                if (Sync() == null || !_started) return;
                Fire(RuleTrigger.CharacterKilled, c, null);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Rules] Kill hook failed: {ex.Message}"); }
        }

        // ── Resolving which rule sets apply ────────────────────────

        private static List<Active> ActiveSets()
        {
            var list = new List<Active>();
            foreach (var kv in Mods)
                foreach (var set in kv.Value.Values)
                {
                    if (set == null || !set.Enabled) continue;
                    if (IsActive(set, out var owner)) list.Add(new Active { ModId = kv.Key, Set = set, Owner = owner });
                }
            return list;
        }

        private static bool IsActive(RuleSetDef set, out Character owner)
        {
            owner = null;
            bool attached = false;
            if (!string.IsNullOrEmpty(set.ChallengeTraitId))
            {
                attached = true;
                if (!ChallengeActive(set.ChallengeTraitId)) return false;
            }
            if (!string.IsNullOrEmpty(set.NpcId))
            {
                attached = true;
                owner = FindNpc(set.NpcId);
                if (owner == null) return false;
            }
            if (!string.IsNullOrEmpty(set.CombatId))
            {
                attached = true;
                if (!string.Equals(CurrentCombatId(), set.CombatId, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return attached || set.AlwaysActive;
        }

        private static bool ChallengeActive(string id)
        {
            try { return AtOManager.Instance != null && AtOManager.Instance.IsChallengeTraitActive(id); }
            catch { return false; }
        }

        private static string CurrentCombatId()
        {
            try { return AtOManager.Instance?.GetCurrentCombatData()?.CombatId ?? ""; }
            catch { return ""; }
        }

        // ── Characters ─────────────────────────────────────────────

        private static IEnumerable<Character> Living(IEnumerable<Character> team) =>
            team == null ? Enumerable.Empty<Character>() : team.Where(c => c != null && c.Alive);

        private static List<Character> Heroes()
        {
            try { return Living(MatchManager.Instance.GetTeamHero()).ToList(); }
            catch { return new List<Character>(); }
        }

        private static List<Character> Enemies()
        {
            try { return Living(MatchManager.Instance.GetTeamNPC()).ToList(); }
            catch { return new List<Character>(); }
        }

        private static string NpcIdOf(Character c) => c?.NpcData?.Id ?? "";

        private static Character FindNpc(string id)
        {
            try
            {
                var all = MatchManager.Instance.GetTeamNPC();
                if (all == null) return null;
                Character dead = null;
                foreach (var c in all)
                {
                    if (c == null || !string.Equals(NpcIdOf(c), id, StringComparison.OrdinalIgnoreCase)) continue;
                    if (c.Alive) return c;
                    dead ??= c;
                }
                return dead;
            }
            catch { return null; }
        }

        private static bool Same(Character a, Character b) =>
            a != null && b != null && (ReferenceEquals(a, b) || (!string.IsNullOrEmpty(a.InternalId) && a.InternalId == b.InternalId));

        private static float HpPercent(Character c)
        {
            int max = c.GetMaxHP();
            return max <= 0 ? 0f : c.GetHp() * 100f / max;
        }

        // ── Matching ───────────────────────────────────────────────

        private static bool NeedsSubject(RuleTrigger t) =>
            t == RuleTrigger.Damaged || t == RuleTrigger.CharacterKilled || t == RuleTrigger.CardPlayed ||
            t == RuleTrigger.HpBelow || t == RuleTrigger.TurnStart || t == RuleTrigger.TurnEnd;

        private static string RuleKey(Active a, int index) => $"{a.ModId}:{a.Set.Id}#{index}";

        private static void Fire(RuleTrigger trigger, Character subject, string cardId)
        {
            var mm = MatchManager.Instance;
            int round = mm.GetCurrentRound();
            foreach (var a in ActiveSets())
            {
                var rules = a.Set.Rules;
                if (rules == null) continue;
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    if (r == null || r.Trigger != trigger) continue;
                    TryQueue(a, r, RuleKey(a, i), subject, cardId, round, null);
                }
            }
        }

        private static void PollHp(MatchManager mm)
        {
            List<Active> sets = null;
            foreach (var kv in Mods)
                foreach (var set in kv.Value.Values)
                    if (set != null && set.Enabled && set.Rules != null && set.Rules.Any(r => r != null && r.Trigger == RuleTrigger.HpBelow))
                    {
                        sets ??= ActiveSets();
                        break;
                    }
            if (sets == null) return;

            int round = mm.GetCurrentRound();
            var everyone = Heroes().Concat(Enemies()).ToList();
            foreach (var a in sets)
            {
                var rules = a.Set.Rules;
                if (rules == null) continue;
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    if (r == null || r.Trigger != RuleTrigger.HpBelow) continue;
                    string key = RuleKey(a, i);
                    foreach (var c in everyone)
                    {
                        if (HpPercent(c) > r.HpPercent) continue;
                        TryQueue(a, r, key, c, null, round, key + "|" + c.InternalId);
                    }
                }
            }
        }

        private static void TryQueue(Active a, RuleDef r, string ruleKey, Character subject, string cardId, int round, string uniqueKey)
        {
            if (!r.Enabled) return;
            if (Errors.TryGetValue(ruleKey, out var errs) && errs >= MaxErrorsPerRule) return;
            if (uniqueKey != null && Unique.Contains(uniqueKey)) return;
            if (r.FromRound > 0 && round < r.FromRound) return;
            if (r.ToRound > 0 && round > r.ToRound) return;
            if (r.EveryNRounds > 1)
            {
                int start = Math.Max(r.FromRound, 1);
                if (round < start || (round - start) % r.EveryNRounds != 0) return;
            }

            if (NeedsSubject(r.Trigger))
            {
                if (subject == null) return;
                switch (r.Subject)
                {
                    case RuleSubject.Owner: if (!Same(subject, a.Owner)) return; break;
                    case RuleSubject.Hero: if (!subject.IsHero) return; break;
                    case RuleSubject.Enemy: if (subject.IsHero) return; break;
                }
                if (!string.IsNullOrEmpty(r.SubjectNpcId) &&
                    !string.Equals(NpcIdOf(subject), r.SubjectNpcId, StringComparison.OrdinalIgnoreCase)) return;
            }
            if (r.Trigger == RuleTrigger.CardPlayed && !string.IsNullOrEmpty(r.CardId) &&
                !string.Equals(cardId, r.CardId, StringComparison.OrdinalIgnoreCase)) return;
            if (r.Once && Once.Contains(ruleKey)) return;

            var ctx = new Ctx { Owner = a.Owner, Subject = subject, Round = round };
            if (!AllHold(r.Conditions, ctx)) return;
            if (r.Chance < 100 && UnityEngine.Random.Range(0, 100) >= Math.Max(0, r.Chance)) return;

            if (uniqueKey != null) Unique.Add(uniqueKey);
            if (r.Once) Once.Add(ruleKey);
            Queue.Enqueue(new Pending { RuleKey = ruleKey, SetId = a.Set.Id, Rule = r, Owner = a.Owner, Subject = subject, Round = round });
        }

        // ── Conditions ─────────────────────────────────────────────

        private static bool AllHold(List<RuleConditionDef> conds, Ctx ctx)
        {
            if (conds == null) return true;
            foreach (var c in conds)
                if (c != null && !Holds(c, ctx)) return false;
            return true;
        }

        private static List<Character> Refs(RuleCharRef of, Ctx ctx)
        {
            switch (of)
            {
                case RuleCharRef.Owner: return ctx.Owner != null && ctx.Owner.Alive ? new List<Character> { ctx.Owner } : new List<Character>();
                case RuleCharRef.Subject: return ctx.Subject != null ? new List<Character> { ctx.Subject } : new List<Character>();
                case RuleCharRef.AnyHero: return Heroes();
                default: return Enemies();
            }
        }

        private static bool Holds(RuleConditionDef c, Ctx ctx)
        {
            switch (c.Kind)
            {
                case RuleCondKind.RoundAtLeast: return ctx.Round >= c.Value;
                case RuleCondKind.RoundAtMost: return ctx.Round <= c.Value;
                case RuleCondKind.RoundEvery: return c.Value > 0 && ctx.Round > 0 && ctx.Round % c.Value == 0;
                case RuleCondKind.HeroesAliveAtLeast: return Heroes().Count >= c.Value;
                case RuleCondKind.HeroesAliveAtMost: return Heroes().Count <= c.Value;
                case RuleCondKind.EnemiesAliveAtLeast: return Enemies().Count >= c.Value;
                case RuleCondKind.EnemiesAliveAtMost: return Enemies().Count <= c.Value;
                case RuleCondKind.HpBelowPercent: return Refs(c.Of, ctx).Any(x => HpPercent(x) < c.Value);
                case RuleCondKind.HpAbovePercent: return Refs(c.Of, ctx).Any(x => HpPercent(x) > c.Value);
                case RuleCondKind.HasAura: return Refs(c.Of, ctx).Any(x => x.GetAuraCharges(Aura(c.Id)) > 0);
                case RuleCondKind.LacksAura: return Refs(c.Of, ctx).Any(x => x.GetAuraCharges(Aura(c.Id)) <= 0);
                case RuleCondKind.NpcAlive: return Enemies().Count(x => string.Equals(NpcIdOf(x), c.Id, StringComparison.OrdinalIgnoreCase)) >= Math.Max(1, c.Value);
                case RuleCondKind.NpcDead: return !Enemies().Any(x => string.Equals(NpcIdOf(x), c.Id, StringComparison.OrdinalIgnoreCase));
                case RuleCondKind.FlagSet: return Flags.Contains(c.Id ?? "");
                case RuleCondKind.FlagNotSet: return !Flags.Contains(c.Id ?? "");
            }
            return true;
        }

        private static string Aura(string id) => (id ?? "").ToLowerInvariant();

        // ── Actions ────────────────────────────────────────────────

        private static void Drain(MatchManager mm)
        {
            int done = 0;
            while (Queue.Count > 0 && done++ < MaxPerTick && !mm.MatchIsOver)
            {
                var p = Queue.Dequeue();
                try { Run(p); }
                catch (Exception ex)
                {
                    Errors[p.RuleKey] = (Errors.TryGetValue(p.RuleKey, out var n) ? n : 0) + 1;
                    Plugin.Log.LogWarning($"[Rules] '{p.SetId}' rule '{Label(p.Rule)}' failed ({Errors[p.RuleKey]}/{MaxErrorsPerRule}): {ex.Message}");
                }
                if (mm.IsGameBusy()) break;
            }
        }

        private static string Label(RuleDef r) => string.IsNullOrEmpty(r.Name) ? r.Trigger.ToString() : r.Name;

        private static void Note(Pending p)
        {
            Recent.Add($"R{p.Round} {p.SetId}: {Label(p.Rule)}");
            if (Recent.Count > LogCap) Recent.RemoveRange(0, Recent.Count - LogCap);
        }

        private static void Run(Pending p)
        {
            Note(p);
            var summoned = new List<Character>();
            foreach (var a in p.Rule.Actions)
            {
                if (a == null) continue;
                var mm = MatchManager.Instance;
                if (mm == null || mm.MatchIsOver) return;
                Execute(a, p, summoned);
            }
        }

        private static List<Character> Targets(RuleTarget t, Pending p, List<Character> summoned)
        {
            var heroes = Heroes();
            var enemies = Enemies();
            switch (t)
            {
                case RuleTarget.Owner: return p.Owner != null && p.Owner.Alive ? new List<Character> { p.Owner } : new List<Character>();
                case RuleTarget.Subject: return p.Subject != null && p.Subject.Alive ? new List<Character> { p.Subject } : new List<Character>();
                case RuleTarget.AllHeroes: return heroes;
                case RuleTarget.AllEnemies: return enemies;
                case RuleTarget.RandomHero: return heroes.Count == 0 ? heroes : new List<Character> { heroes[UnityEngine.Random.Range(0, heroes.Count)] };
                case RuleTarget.RandomEnemy: return enemies.Count == 0 ? enemies : new List<Character> { enemies[UnityEngine.Random.Range(0, enemies.Count)] };
                case RuleTarget.LowestHpHero: return heroes.OrderBy(c => c.GetHp()).Take(1).ToList();
                case RuleTarget.HighestHpHero: return heroes.OrderByDescending(c => c.GetHp()).Take(1).ToList();
                case RuleTarget.LowestHpEnemy: return enemies.OrderBy(c => c.GetHp()).Take(1).ToList();
                case RuleTarget.Summoned: return summoned.Where(c => c != null && c.Alive).ToList();
            }
            return new List<Character>();
        }

        private static int Scaled(RuleActionDef a, Character t) =>
            a.Percent ? Math.Max(1, Mathf.RoundToInt(t.GetMaxHP() * a.Amount / 100f)) : a.Amount;

        private static void Execute(RuleActionDef a, Pending p, List<Character> summoned)
        {
            switch (a.Kind)
            {
                case RuleActionKind.AddAura:
                {
                    string id = Aura(a.Id);
                    if (Globals.Instance.GetAuraCurseData(id) == null) throw new InvalidOperationException($"unknown aura/curse '{a.Id}'");
                    foreach (var t in Targets(a.Target, p, summoned)) Scripting.GameApi.AddAura(t, id, a.Amount, p.Owner ?? t);
                    break;
                }
                case RuleActionKind.Damage:
                {
                    var type = a.DamageType == Enums.DamageType.None ? Enums.DamageType.Slashing : a.DamageType;
                    foreach (var t in Targets(a.Target, p, summoned)) Scripting.GameApi.Damage(t, type, Scaled(a, t), p.Owner);
                    break;
                }
                case RuleActionKind.Heal:
                    foreach (var t in Targets(a.Target, p, summoned)) Scripting.GameApi.Heal(t, Scaled(a, t), p.Owner);
                    break;
                case RuleActionKind.GainEnergy:
                    foreach (var t in Targets(a.Target, p, summoned)) t.ModifyEnergy(a.Amount, true);
                    break;
                case RuleActionKind.Kill:
                    foreach (var t in Targets(a.Target, p, summoned)) KillOff(t, p.Owner);
                    break;
                case RuleActionKind.Win:
                    ShowText(a.Text, p);
                    foreach (var t in Enemies()) KillOff(t, p.Owner);
                    Queue.Clear();
                    break;
                case RuleActionKind.Lose:
                    ShowText(a.Text, p);
                    foreach (var t in Heroes()) KillOff(t, p.Owner);
                    Queue.Clear();
                    break;
                case RuleActionKind.Message:
                    ShowText(a.Text, p, Targets(a.Target, p, summoned).FirstOrDefault());
                    break;
                case RuleActionKind.SetFlag:
                    if (!string.IsNullOrEmpty(a.Id)) Flags.Add(a.Id);
                    break;
                case RuleActionKind.ClearFlag:
                    if (!string.IsNullOrEmpty(a.Id)) Flags.Remove(a.Id);
                    break;
                case RuleActionKind.Summon:
                    Summon(a, summoned);
                    break;
            }
        }

        private static void KillOff(Character c, Character source)
        {
            if (c == null || !c.Alive) return;
            c.ModifyHp(-Math.Max(1, c.GetHp()), false, true, source);
        }

        private static void ShowText(string text, Pending p, Character on = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            on ??= p.Owner != null && p.Owner.Alive ? p.Owner : (Enemies().FirstOrDefault() ?? Heroes().FirstOrDefault());
            if (on == null) return;
            GameUtils.GetCharacterItem(on)?.ScrollCombatText(text, Enums.CombatScrollEffectType.Trait);
        }

        private static void Summon(RuleActionDef a, List<Character> summoned)
        {
            if (string.IsNullOrEmpty(a.Id)) return;
            var data = Globals.Instance.GetNPC(a.Id);
            if (data == null) throw new InvalidOperationException($"unknown enemy '{a.Id}'");
            var mm = MatchManager.Instance;
            int alive = Enemies().Count(c => string.Equals(NpcIdOf(c), a.Id, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < Math.Max(1, a.Count); i++)
            {
                if (a.MaxAlive > 0 && alive >= a.MaxAlive) break;
                var npc = mm.TeamManager.CreateNPC(data, "", -1, false, "", null, "", 0f, false, true);
                if (npc == null) break; // no free slot
                summoned.Add(npc);
                alive++;
            }
        }

        // ── Enemy AI ───────────────────────────────────────────────

        /// <summary>
        /// Whether the rule sets allow this enemy to play this card right now. Called from the AI patch after the game's
        /// own checks; only ever narrows what the game decided.
        /// </summary>
        public static bool AiAllows(Character caster, string cardId)
        {
            if (!HasRules || caster == null || caster.IsHero) return true;
            try
            {
                if (Sync() == null) return true;
                string npcId = NpcIdOf(caster);
                int round = MatchManager.Instance.GetCurrentRound();
                foreach (var kv in Mods)
                    foreach (var set in kv.Value.Values)
                    {
                        if (set == null || !set.Enabled || set.AiRules == null || set.AiRules.Count == 0) continue;
                        if (!string.Equals(set.NpcId, npcId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!IsActive(set, out _)) continue;
                        var ctx = new Ctx { Owner = caster, Round = round };
                        foreach (var r in set.AiRules)
                        {
                            if (r == null) continue;
                            if (!string.IsNullOrEmpty(r.CardId) && !string.Equals(r.CardId, cardId, StringComparison.OrdinalIgnoreCase)) continue;
                            bool hold = AllHold(r.Conditions, ctx);
                            if (r.Mode == AiRuleMode.OnlyIf && !hold) return false;
                            if (r.Mode == AiRuleMode.Never && hold) return false;
                        }
                    }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Rules] AI rule check failed: {ex.Message}"); }
            return true;
        }

        // ── Validation (used by the editor) ────────────────────────

        /// <summary>Problems in a rule set that would stop it working: unknown ids, empty rules, contradictory settings.</summary>
        public static List<string> Validate(RuleSetDef set, ModProject proj)
        {
            var issues = new List<string>();
            if (set == null) return issues;
            var npcs = new HashSet<string>(Editor.RefIds.Npcs(proj), StringComparer.OrdinalIgnoreCase);
            var auras = new HashSet<string>(Editor.RefIds.AuraCurses(proj), StringComparer.OrdinalIgnoreCase);
            var cards = new HashSet<string>(Editor.RefIds.Cards(proj), StringComparer.OrdinalIgnoreCase);
            var combats = new HashSet<string>(Editor.RefIds.Combats(proj), StringComparer.OrdinalIgnoreCase);
            var traits = new HashSet<string>(Editor.RefIds.ChallengeTraits(proj), StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(set.ChallengeTraitId) && string.IsNullOrEmpty(set.NpcId) && string.IsNullOrEmpty(set.CombatId) && !set.AlwaysActive)
                issues.Add("Not attached to anything, so it never runs. Pick a challenge trait, an enemy, a combat, or tick Always active.");
            if (!string.IsNullOrEmpty(set.ChallengeTraitId) && !traits.Contains(set.ChallengeTraitId)) issues.Add($"Challenge trait '{set.ChallengeTraitId}' does not exist.");
            if (!string.IsNullOrEmpty(set.NpcId) && !npcs.Contains(set.NpcId)) issues.Add($"Enemy '{set.NpcId}' does not exist.");
            if (!string.IsNullOrEmpty(set.CombatId) && !combats.Contains(set.CombatId)) issues.Add($"Combat '{set.CombatId}' does not exist.");

            void Conds(string where, List<RuleConditionDef> list)
            {
                if (list == null) return;
                foreach (var c in list)
                {
                    if (c == null) continue;
                    bool needsAura = c.Kind == RuleCondKind.HasAura || c.Kind == RuleCondKind.LacksAura;
                    bool needsNpc = c.Kind == RuleCondKind.NpcAlive || c.Kind == RuleCondKind.NpcDead;
                    bool needsFlag = c.Kind == RuleCondKind.FlagSet || c.Kind == RuleCondKind.FlagNotSet;
                    if (needsAura && !auras.Contains(c.Id ?? "")) issues.Add($"{where}: aura/curse '{c.Id}' does not exist.");
                    if (needsNpc && !npcs.Contains(c.Id ?? "")) issues.Add($"{where}: enemy '{c.Id}' does not exist.");
                    if (needsFlag && string.IsNullOrEmpty(c.Id)) issues.Add($"{where}: flag condition has no flag name.");
                    bool usesOwner = (c.Kind == RuleCondKind.HpBelowPercent || c.Kind == RuleCondKind.HpAbovePercent || needsAura) && c.Of == RuleCharRef.Owner;
                    if (usesOwner && string.IsNullOrEmpty(set.NpcId)) issues.Add($"{where}: refers to the Owner, but the set has no enemy attached.");
                }
            }

            for (int i = 0; i < set.Rules.Count; i++)
            {
                var r = set.Rules[i];
                if (r == null) continue;
                string w = $"Rule {i + 1} ({Label(r)})";
                if (r.Actions == null || r.Actions.Count == 0) issues.Add($"{w}: has no actions.");
                if (r.Trigger == RuleTrigger.HpBelow && (r.HpPercent < 1 || r.HpPercent > 99)) issues.Add($"{w}: HP percent should be 1-99.");
                if (r.FromRound > 0 && r.ToRound > 0 && r.ToRound < r.FromRound) issues.Add($"{w}: 'To round' is before 'From round'.");
                if (r.Subject == RuleSubject.Owner && string.IsNullOrEmpty(set.NpcId) && NeedsSubject(r.Trigger)) issues.Add($"{w}: subject is the Owner, but the set has no enemy attached.");
                if (!string.IsNullOrEmpty(r.SubjectNpcId) && !npcs.Contains(r.SubjectNpcId)) issues.Add($"{w}: enemy '{r.SubjectNpcId}' does not exist.");
                if (!string.IsNullOrEmpty(r.CardId) && !cards.Contains(r.CardId)) issues.Add($"{w}: card '{r.CardId}' does not exist.");
                Conds(w, r.Conditions);
                bool summons = false;
                if (r.Actions != null)
                    for (int j = 0; j < r.Actions.Count; j++)
                    {
                        var a = r.Actions[j];
                        if (a == null) continue;
                        string aw = $"{w}, action {j + 1}";
                        if (a.Kind == RuleActionKind.Summon)
                        {
                            summons = true;
                            if (!npcs.Contains(a.Id ?? "")) issues.Add($"{aw}: enemy '{a.Id}' does not exist.");
                        }
                        if (a.Kind == RuleActionKind.AddAura && !auras.Contains(a.Id ?? "")) issues.Add($"{aw}: aura/curse '{a.Id}' does not exist.");
                        if ((a.Kind == RuleActionKind.SetFlag || a.Kind == RuleActionKind.ClearFlag) && string.IsNullOrEmpty(a.Id)) issues.Add($"{aw}: no flag name.");
                        if (a.Target == RuleTarget.Summoned && !summons && a.Kind != RuleActionKind.Summon) issues.Add($"{aw}: targets Summoned but no earlier action summons anything.");
                        if (a.Target == RuleTarget.Owner && string.IsNullOrEmpty(set.NpcId) && TargetMatters(a.Kind)) issues.Add($"{aw}: targets the Owner, but the set has no enemy attached.");
                        if (a.Target == RuleTarget.Subject && !NeedsSubject(r.Trigger) && TargetMatters(a.Kind)) issues.Add($"{aw}: targets the Subject, but trigger {r.Trigger} has none.");
                    }
            }
            if (set.AiRules != null)
            {
                if (set.AiRules.Count > 0 && string.IsNullOrEmpty(set.NpcId)) issues.Add("AI rules only apply to the enemy attached to the set; attach one.");
                for (int i = 0; i < set.AiRules.Count; i++)
                {
                    var r = set.AiRules[i];
                    if (r == null) continue;
                    string w = $"AI rule {i + 1}";
                    if (!string.IsNullOrEmpty(r.CardId) && !cards.Contains(r.CardId)) issues.Add($"{w}: card '{r.CardId}' does not exist.");
                    if (r.Conditions == null || r.Conditions.Count == 0) issues.Add($"{w}: has no conditions, so it {(r.Mode == AiRuleMode.Never ? "always blocks the card" : "changes nothing")}.");
                    Conds(w, r.Conditions);
                }
            }
            return issues;
        }

        private static bool TargetMatters(RuleActionKind k) =>
            k != RuleActionKind.Win && k != RuleActionKind.Lose && k != RuleActionKind.Summon &&
            k != RuleActionKind.SetFlag && k != RuleActionKind.ClearFlag;
    }
}
