using System;
using System.Collections.Generic;
using System.Linq;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public sealed class SimHeroSlot
    {
        public string HeroId = "";
        public int Level = 1;
        /// <summary>0 = base cards, 1 = upgrade A, 2 = upgrade B</summary>
        public int Upgrade;
        /// <summary>Comma separated card ids to add to the deck, "id*3" adds three copies.</summary>
        public string ExtraCards = "";
    }

    public sealed class SimSetup
    {
        public List<SimHeroSlot> Heroes = new();
        public List<string> Npcs = new();
        public List<CombatEffectDef> StartEffects = new();
        public int Madness;
        public float HeroPower = 1f;
        public float EnemyHp = 1f;
        public float EnemyDamage = 1f;
        public int MaxRounds = 30;
        public int Seed = 1;
    }

    public sealed class SimUnitStat
    {
        public string Name = "";
        public bool Hero;
        public double DamageDealt, DamageTaken, Healed, Deaths, DeathRound, FinalHpPct;
    }

    public sealed class SimCardStat
    {
        public string Id = "";
        public double Plays, Damage, Heal;
    }

    public sealed class SimResult
    {
        public int Runs, Wins, Losses, Timeouts;
        public double WinRounds, AllRounds, WinHeroHpPct;
        public int[] RoundHist = new int[0];
        public List<SimUnitStat> Units = new();
        public Dictionary<string, SimCardStat> Cards = new();
        public SortedSet<string> UnmodelledAuras = new();
        public SortedSet<string> UnmodelledCards = new();
        public List<string> Warnings = new();
        public double WinRate => Runs > 0 ? (double)Wins / Runs : 0;
        public double AvgWinRounds => Wins > 0 ? WinRounds / Wins : 0;
        public double AvgRounds => Runs > 0 ? AllRounds / Runs : 0;
        public double AvgHpLeftOnWin => Wins > 0 ? WinHeroHpPct / Wins : 0;
    }

    /// <summary>
    /// Headless Monte Carlo of a fight: a team of heroes with a greedy "play the best card per energy" policy against enemies that follow their AI card lists.
    /// It is a balance estimate, not the game's combat engine: summons, special values, card choosing, stealth, reflect and a few other effects are not modelled
    /// and are listed in the result.
    /// </summary>
    public sealed class CombatSim
    {
        // ── Cached definitions ──────────────────────────────────
        private sealed class AuraInfo
        {
            public string Id = "";
            public bool Known, IsAura, ConsumeAll, AtTurnBegin, AtTurn, AtRoundBegin, AtRound, ProduceDmg, ProduceHeal, Invulnerable, Taunt, SkipsTurn, NoRemoveBlock;
            public int Max = -1, Consumed = 1;
            public Enums.DamageType[] OutType = new Enums.DamageType[4];
            public float[] OutFlat = new float[4], OutFlatStack = new float[4], OutPct = new float[4], OutPctStack = new float[4];
            public Enums.DamageType[] InType = new Enums.DamageType[2];
            public float[] InFlat = new float[2], InFlatStack = new float[2], InPct = new float[2], InPctStack = new float[2];
            public Enums.DamageType[] ResType = new Enums.DamageType[3];
            public float[] ResFlat = new float[3], ResStack = new float[3];
            public float PreventPerStack;
            public Enums.DamageType PreventType;
            public Enums.DamageType DmgType;
            public float DmgFlat, DmgPerCharge, HealFlat, HealPerCharge;
            public float HealDoneFlat, HealDoneStack, HealDonePct, HealDonePctStack, HealRecFlat, HealRecStack, HealRecPct, HealRecPctStack;
            public bool SpeedStat, EnergyTurnStat; public float StatValue, StatPerStack, StatNeeded = 1;
            public bool IsBlock => Id == "block";
        }

        private sealed class HeroTpl { public HeroDef Def; public List<CardDef> Deck = new(); public int Hp; public string Name; }
        private sealed class NpcTpl { public NpcDef Def; public List<(CardDef card, AiCardDef ai)> Ai = new(); public string Name; }

        private sealed class Card
        {
            public CardDef Def;
            public int Cost;
            public bool Vanish;
        }

        private sealed class Unit
        {
            public string Name = "";
            public bool Hero;
            public int Side;
            public int Index;
            public int Hp, MaxHp, BaseSpeed, Energy, EnergyTurn;
            public float DmgMult = 1f;
            public float[] Res = new float[11];
            public bool Dead;
            public int DeadRound;
            public Dictionary<string, int> Auras = new();
            public List<Card> Draw = new(), Discard = new(), Hand = new();
            public List<(Card card, AiCardDef ai)> NpcDeck = new();
            public HeroTpl HeroT;
            public NpcTpl NpcT;
            public HashSet<string> Immune = new();
            public bool FinishOnDead;
            public SimUnitStat Stat;
        }

        private readonly ModProject _proj;
        private readonly SimSetup _s;
        private readonly Dictionary<string, CardDef> _cardCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AuraInfo> _auraCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<HeroTpl> _heroes = new();
        private readonly List<NpcTpl> _npcs = new();

        public SimResult Result { get; } = new();
        public bool Ready { get; private set; }
        private Random _rng;

        // Per-run state
        private List<Unit> _units, _heroUnits, _npcUnits;
        private int _round;
        private bool _finishFlag;

        public CombatSim(ModProject proj, SimSetup setup) { _proj = proj; _s = setup; }

        // ═══════════════════════════════════════════════════════════
        //  PREPARE
        // ═══════════════════════════════════════════════════════════

        public bool Prepare(out string error)
        {
            error = null;
            _rng = new Random(_s.Seed);
            var warn = Result.Warnings;

            foreach (var slot in _s.Heroes.Where(h => !string.IsNullOrEmpty(h.HeroId)))
            {
                var def = ResolveHero(slot.HeroId);
                if (def == null) { warn.Add($"Hero '{slot.HeroId}' not found, skipped."); continue; }
                var t = new HeroTpl { Def = def, Name = string.IsNullOrEmpty(def.SubClassName) ? slot.HeroId : def.SubClassName };
                int hp = def.Hp > 0 ? def.Hp : 60;
                if (def.MaxHp != null)
                    for (int i = 1; i < Math.Min(Math.Max(1, slot.Level), def.MaxHp.Count + 1); i++) hp += def.MaxHp[i];
                t.Hp = hp;
                foreach (var hc in def.Cards ?? new List<HeroCardDef>())
                {
                    var card = ResolveUpgraded(hc.CardId, slot.Upgrade);
                    if (card == null) { warn.Add($"{t.Name}: card '{hc.CardId}' not found."); continue; }
                    for (int i = 0; i < Math.Max(1, hc.UnitsInDeck); i++) t.Deck.Add(card);
                }
                foreach (var part in (slot.ExtraCards ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = part.Split('*');
                    int n = bits.Length > 1 && int.TryParse(bits[1], out var k) ? Math.Max(1, k) : 1;
                    var card = ResolveCard(bits[0]);
                    if (card == null) { warn.Add($"{t.Name}: extra card '{bits[0]}' not found."); continue; }
                    for (int i = 0; i < n; i++) t.Deck.Add(card);
                }
                if (t.Deck.Count == 0) warn.Add($"{t.Name} has no cards.");
                _heroes.Add(t);
            }
            if (_heroes.Count == 0) { error = "Pick at least one hero."; return false; }

            foreach (var nid in _s.Npcs.Where(n => !string.IsNullOrEmpty(n)))
            {
                var def = ResolveNpc(nid);
                if (def == null) { warn.Add($"Enemy '{nid}' not found, skipped."); continue; }
                var t = new NpcTpl { Def = def, Name = string.IsNullOrEmpty(def.Name) ? nid : def.Name };
                foreach (var ai in def.AiCards ?? new List<AiCardDef>())
                {
                    var card = ResolveCard(ai.CardId);
                    if (card == null) { warn.Add($"{t.Name}: AI card '{ai.CardId}' not found."); continue; }
                    t.Ai.Add((card, ai));
                }
                if (t.Ai.Count == 0) warn.Add($"{t.Name} has no usable AI cards, so it never acts.");
                _npcs.Add(t);
            }
            if (_npcs.Count == 0) { error = "Pick at least one enemy."; return false; }

            Result.RoundHist = new int[_s.MaxRounds + 2];
            foreach (var h in _heroes) Result.Units.Add(new SimUnitStat { Name = h.Name, Hero = true });
            foreach (var n in _npcs) Result.Units.Add(new SimUnitStat { Name = n.Name, Hero = false });
            Ready = true;
            return true;
        }

        private HeroDef ResolveHero(string id)
        {
            if (_proj != null)
            {
                if (_proj.Heroes.TryGetValue(id, out var d)) return d;
                if (_proj.HeroPatches.TryGetValue(id, out var p) && p != null && (p.Cards?.Count ?? 0) > 0) return p;
            }
            var sc = DataHelper.GetSubClass(id);
            return sc != null ? DataHelper.SnapshotHero(sc) : null;
        }

        private NpcDef ResolveNpc(string id)
        {
            if (_proj != null && _proj.Npcs.TryGetValue(id, out var d)) return d;
            var n = DataHelper.GetExistingNPC(id, false);
            return n != null ? DataHelper.SnapshotNpc(n) : null;
        }

        private CardDef ResolveCard(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_cardCache.TryGetValue(id, out var c)) return c;
            if (_proj != null && _proj.Cards.TryGetValue(id, out c)) { _cardCache[id] = c; return c; }
            var rt = DataHelper.GetCard(id);
            c = rt != null ? ModProjectBuilder.SnapshotCard(rt) : null;
            _cardCache[id] = c;
            return c;
        }

        private CardDef ResolveUpgraded(string id, int upgrade)
        {
            if (upgrade == 1) { var a = ResolveCard(id + "a"); if (a != null) return a; }
            if (upgrade == 2) { var b = ResolveCard(id + "b"); if (b != null) return b; }
            return ResolveCard(id);
        }

        private AuraInfo Aura(string id)
        {
            if (_auraCache.TryGetValue(id, out var ai)) return ai;
            ai = new AuraInfo { Id = id };
            _auraCache[id] = ai;
            var ac = DataHelper.GetAuraCurse(id, false);
            if (ac == null) { Result.UnmodelledAuras.Add(id + " (unknown aura/curse)"); return ai; }
            ai.Known = true;
            ai.IsAura = ac.IsAura;
            ai.Max = ac.MaxCharges;
            ai.Consumed = Math.Max(1, ac.AuraConsumed);
            ai.ConsumeAll = ac.ConsumeAll;
            ai.AtTurnBegin = ac.ConsumedAtTurnBegin; ai.AtTurn = ac.ConsumedAtTurn;
            ai.AtRoundBegin = ac.ConsumedAtRoundBegin; ai.AtRound = ac.ConsumedAtRound;
            ai.ProduceDmg = ac.ProduceDamageWhenConsumed; ai.ProduceHeal = ac.ProduceHealWhenConsumed;
            ai.Invulnerable = ac.Invulnerable; ai.Taunt = ac.Taunt; ai.SkipsTurn = ac.SkipsNextTurn;
            ai.NoRemoveBlock = ac.NoRemoveBlockAtTurnEnd;

            ai.OutType = new[] { ac.AuraDamageType, ac.AuraDamageType2, ac.AuraDamageType3, ac.AuraDamageType4 };
            ai.OutFlat = new float[] { ac.AuraDamageIncreasedTotal, ac.AuraDamageIncreasedTotal2, ac.AuraDamageIncreasedTotal3, ac.AuraDamageIncreasedTotal4 };
            ai.OutFlatStack = new[] { ac.AuraDamageIncreasedPerStack, ac.AuraDamageIncreasedPerStack2, ac.AuraDamageIncreasedPerStack3, ac.AuraDamageIncreasedPerStack4 };
            ai.OutPct = new float[] { ac.AuraDamageIncreasedPercent, ac.AuraDamageIncreasedPercent2, ac.AuraDamageIncreasedPercent3, ac.AuraDamageIncreasedPercent4 };
            ai.OutPctStack = new[] { ac.AuraDamageIncreasedPercentPerStack, ac.AuraDamageIncreasedPercentPerStack2, ac.AuraDamageIncreasedPercentPerStack3, ac.AuraDamageIncreasedPercentPerStack4 };

            ai.InType = new[] { ac.IncreasedDamageReceivedType, ac.IncreasedDamageReceivedType2 };
            float n1 = Math.Max(1, ac.IncreasedDirectDamageChargesMultiplierNeededForOne), n2 = Math.Max(1, ac.IncreasedDirectDamageChargesMultiplierNeededForOne2);
            ai.InFlat = new float[] { ac.IncreasedDirectDamageReceivedPerTurn, ac.IncreasedDirectDamageReceivedPerTurn2 };
            ai.InFlatStack = new[] { ac.IncreasedDirectDamageReceivedPerStack / n1, ac.IncreasedDirectDamageReceivedPerStack2 / n2 };
            ai.InPct = new float[] { ac.IncreasedPercentDamageReceivedPerTurn, ac.IncreasedPercentDamageReceivedPerTurn2 };
            ai.InPctStack = new float[] { ac.IncreasedPercentDamageReceivedPerStack, ac.IncreasedPercentDamageReceivedPerStack2 };

            ai.ResType = new[] { ac.ResistModified, ac.ResistModified2, ac.ResistModified3 };
            ai.ResFlat = new[] { ac.ResistModifiedValue, ac.ResistModifiedValue2, ac.ResistModifiedValue3 };
            ai.ResStack = new[] { ac.ResistModifiedPercentagePerStack, ac.ResistModifiedPercentagePerStack2, ac.ResistModifiedPercentagePerStack3 };

            ai.PreventPerStack = Math.Max(ac.DamagePreventedPerStack, ac.PreventedDamagePerStack);
            ai.PreventType = ac.PreventedDamageTypePerStack;
            ai.DmgType = ac.DamageTypeWhenConsumed;
            ai.DmgFlat = ac.DamageWhenConsumed; ai.DmgPerCharge = ac.DamageWhenConsumedPerCharge;
            ai.HealFlat = ac.HealWhenConsumed; ai.HealPerCharge = ac.HealWhenConsumedPerCharge;
            ai.HealDoneFlat = ac.HealDoneTotal; ai.HealDoneStack = ac.HealDonePerStack; ai.HealDonePct = ac.HealDonePercent; ai.HealDonePctStack = ac.HealDonePercentPerStack;
            ai.HealRecFlat = ac.HealReceivedTotal; ai.HealRecStack = ac.HealReceivedPerStack; ai.HealRecPct = ac.HealReceivedPercent; ai.HealRecPctStack = ac.HealReceivedPercentPerStack;

            var stat = ac.CharacterStatModified;
            ai.StatNeeded = Math.Max(1, ac.CharacterStatChargesMultiplierNeededForOne);
            ai.StatValue = ac.CharacterStatModifiedValue; ai.StatPerStack = ac.CharacterStatModifiedValuePerStack;
            ai.SpeedStat = stat == Enums.CharacterStat.Speed && !ac.CharacterStatAbsolute;
            ai.EnergyTurnStat = stat == Enums.CharacterStat.EnergyTurn && !ac.CharacterStatAbsolute;

            var notes = new List<string>();
            if (ac.DamageReflectedMultiplier > 0) notes.Add("reflects damage");
            if (ac.HealAttackerPerStack > 0) notes.Add("heals attackers");
            if (ac.ExplodeAtStacks > 0) notes.Add("explodes at stacks");
            if (ac.CardsDrawPerStack != 0) notes.Add("extra card draw");
            if (ac.DisabledCardTypes != null && ac.DisabledCardTypes.Length > 0) notes.Add("disables card types");
            if (ac.ModifyCardCostPerChargeNeededForOne != 0) notes.Add("card cost change");
            if (ac.Stealth) notes.Add("stealth");
            if (ac.CursePreventedPerStack != 0 || ac.PreventedAuraCurse != null) notes.Add("prevents curses");
            if (stat != Enums.CharacterStat.None && !ai.SpeedStat && !ai.EnergyTurnStat) notes.Add("stat change " + stat);
            if (notes.Count > 0) Result.UnmodelledAuras.Add($"{id}: {string.Join(", ", notes)}");
            return ai;
        }

        // ═══════════════════════════════════════════════════════════
        //  RUN
        // ═══════════════════════════════════════════════════════════

        public void RunMany(int count)
        {
            for (int i = 0; i < count; i++) RunOne();
        }

        private void RunOne()
        {
            _units = new List<Unit>(); _heroUnits = new List<Unit>(); _npcUnits = new List<Unit>();
            _finishFlag = false;
            for (int i = 0; i < _heroes.Count; i++)
            {
                var t = _heroes[i];
                var u = NewUnit(t.Name, true, 0, i);
                u.HeroT = t;
                u.MaxHp = u.Hp = Math.Max(1, (int)Math.Round(t.Hp * _s.HeroPower));
                u.BaseSpeed = t.Def.Speed; u.Energy = t.Def.Energy; u.EnergyTurn = t.Def.EnergyTurn;
                u.DmgMult = _s.HeroPower;
                SetRes(u, t.Def.ResSlash, t.Def.ResBlunt, t.Def.ResPierce, t.Def.ResFire, t.Def.ResCold, t.Def.ResLight, t.Def.ResMind, t.Def.ResHoly, t.Def.ResShadow);
                foreach (var c in t.Deck) u.Draw.Add(new Card { Def = c, Cost = c.EnergyCost, Vanish = c.Vanish });
                Shuffle(u.Draw);
                var innate = u.Draw.Where(c => c.Def.Innate).ToList();
                foreach (var c in innate) { u.Draw.Remove(c); u.Draw.Add(c); }
                u.Stat = Result.Units[i];
                _heroUnits.Add(u);
            }
            for (int i = 0; i < _npcs.Count; i++)
            {
                var t = _npcs[i];
                var u = NewUnit(t.Name, false, 1, i);
                u.NpcT = t;
                u.MaxHp = u.Hp = Math.Max(1, (int)Math.Round(t.Def.Hp * _s.EnemyHp));
                u.BaseSpeed = t.Def.Speed; u.EnergyTurn = 0;
                u.DmgMult = _s.EnemyDamage;
                u.FinishOnDead = t.Def.FinishCombatOnDead;
                SetRes(u, t.Def.ResSlash, t.Def.ResBlunt, t.Def.ResPierce, t.Def.ResFire, t.Def.ResCold, t.Def.ResLight, t.Def.ResMind, t.Def.ResHoly, t.Def.ResShadow);
                foreach (var im in t.Def.Immunities ?? new List<string>()) u.Immune.Add(im);
                foreach (var (card, ai) in t.Ai)
                {
                    if (ai.AddCardRound > 0 || ai.StartsAtObeliskMadnessLevel > _s.Madness) continue;
                    for (int k = 0; k < Math.Max(1, ai.UnitsInDeck); k++) u.NpcDeck.Add((new Card { Def = card, Cost = 0, Vanish = false }, ai));
                }
                ShuffleNpc(u.NpcDeck);
                u.Stat = Result.Units[_heroes.Count + i];
                _npcUnits.Add(u);
            }
            _units.AddRange(_heroUnits); _units.AddRange(_npcUnits);

            foreach (var fx in _s.StartEffects)
            {
                var targets = fx.Target == Enums.CombatUnit.Heroes ? _heroUnits : fx.Target == Enums.CombatUnit.Monsters ? _npcUnits : _units;
                foreach (var u in targets) AddAura(u, fx.AuraCurse, fx.Charges);
            }

            string outcome = "timeout";
            for (_round = 1; _round <= _s.MaxRounds; _round++)
            {
                outcome = PlayRound();
                if (outcome != null) break;
                outcome = "timeout";
            }
            Finish(outcome);
        }

        private Unit NewUnit(string name, bool hero, int side, int idx) => new Unit { Name = name, Hero = hero, Side = side, Index = idx };

        private static void SetRes(Unit u, int slash, int blunt, int pierce, int fire, int cold, int light, int mind, int holy, int shadow)
        {
            u.Res[(int)Enums.DamageType.Slashing] = slash; u.Res[(int)Enums.DamageType.Blunt] = blunt; u.Res[(int)Enums.DamageType.Piercing] = pierce;
            u.Res[(int)Enums.DamageType.Fire] = fire; u.Res[(int)Enums.DamageType.Cold] = cold; u.Res[(int)Enums.DamageType.Lightning] = light;
            u.Res[(int)Enums.DamageType.Mind] = mind; u.Res[(int)Enums.DamageType.Holy] = holy; u.Res[(int)Enums.DamageType.Shadow] = shadow;
        }

        private void Shuffle(List<Card> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
        }

        private void ShuffleNpc(List<(Card, AiCardDef)> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
        }

        private void Finish(string outcome)
        {
            var r = Result;
            r.Runs++;
            int rounds = Math.Min(_round, _s.MaxRounds);
            r.AllRounds += rounds;
            r.RoundHist[Math.Min(rounds, r.RoundHist.Length - 1)]++;
            if (outcome == "win") { r.Wins++; r.WinRounds += rounds; }
            else if (outcome == "loss") r.Losses++;
            else r.Timeouts++;

            double hpPct = 0;
            foreach (var u in _units)
            {
                if (u.Dead) { u.Stat.Deaths++; u.Stat.DeathRound += u.DeadRound; }
                u.Stat.FinalHpPct += u.Dead ? 0 : 100.0 * u.Hp / u.MaxHp;
                if (u.Hero) hpPct += u.Dead ? 0 : 100.0 * u.Hp / u.MaxHp;
            }
            if (outcome == "win") r.WinHeroHpPct += hpPct / _heroUnits.Count;
        }

        private bool HeroesDown => _heroUnits.All(h => h.Dead);
        private bool EnemiesDown => _npcUnits.All(n => n.Dead) || _finishFlag;

        private string CheckEnd()
        {
            if (EnemiesDown) return "win";
            if (HeroesDown) return "loss";
            return null;
        }

        private string PlayRound()
        {
            foreach (var u in _units.Where(x => !x.Dead).ToList()) Tick(u, AtWhen.RoundBegin);
            var end = CheckEnd();
            if (end != null) return end;

            var order = _units.Where(u => !u.Dead).OrderByDescending(Speed).ThenBy(u => u.Hero ? 0 : 1).ThenBy(u => u.Index).ToList();
            foreach (var u in order)
            {
                if (u.Dead) continue;
                BeginTurn(u);
                end = CheckEnd();
                if (end != null) return end;
                if (u.Dead) continue;
                if (!SkipsTurn(u))
                {
                    if (u.Hero) HeroAct(u); else NpcAct(u);
                }
                EndTurn(u);
                end = CheckEnd();
                if (end != null) return end;
            }
            foreach (var u in _units.Where(x => !x.Dead).ToList()) Tick(u, AtWhen.RoundEnd);
            return CheckEnd();
        }

        private float Speed(Unit u)
        {
            float s = u.BaseSpeed;
            foreach (var kv in u.Auras)
            {
                var ai = Aura(kv.Key);
                if (ai.SpeedStat) s += ai.StatValue + ai.StatPerStack * (kv.Value / ai.StatNeeded);
            }
            return s;
        }

        private bool SkipsTurn(Unit u)
        {
            foreach (var kv in u.Auras) if (Aura(kv.Key).SkipsTurn) return true;
            return false;
        }

        // ═══════════════════════════════════════════════════════════
        //  TURN FLOW
        // ═══════════════════════════════════════════════════════════

        private enum AtWhen { TurnBegin, TurnEnd, RoundBegin, RoundEnd }

        private void BeginTurn(Unit u)
        {
            bool keepBlock = u.Auras.Keys.Any(k => Aura(k).NoRemoveBlock);
            if (!keepBlock) u.Auras.Remove("block");
            Tick(u, AtWhen.TurnBegin);
            if (u.Dead) return;

            if (u.Hero)
            {
                int et = u.EnergyTurn;
                foreach (var kv in u.Auras) { var ai = Aura(kv.Key); if (ai.EnergyTurnStat) et += (int)(ai.StatValue + ai.StatPerStack * (kv.Value / ai.StatNeeded)); }
                u.Energy = Math.Min(10, u.Energy + Math.Max(0, et));
                DrawCards(u, 5);
            }
            else
            {
                foreach (var (card, ai) in u.NpcT.Ai)
                    if (ai.AddCardRound == _round && ai.StartsAtObeliskMadnessLevel <= _s.Madness)
                        for (int k = 0; k < Math.Max(1, ai.UnitsInDeck); k++) u.NpcDeck.Insert(0, (new Card { Def = card }, ai));
                int want = Math.Max(1, u.NpcT.Def.CardsInHand);
                for (int i = 0; i < want; i++)
                {
                    if (u.NpcDeck.Count == 0) { if (!RefillNpc(u)) break; }
                    var top = u.NpcDeck[0]; u.NpcDeck.RemoveAt(0);
                    u.Hand.Add(top.card);
                    _npcPick[top.card] = top.ai;
                }
            }
        }

        private readonly Dictionary<Card, AiCardDef> _npcPick = new();

        private bool RefillNpc(Unit u)
        {
            if (u.Discard.Count == 0) return false;
            foreach (var c in u.Discard) u.NpcDeck.Add((c, _npcPick.TryGetValue(c, out var ai) ? ai : new AiCardDef()));
            u.Discard.Clear();
            ShuffleNpc(u.NpcDeck);
            return u.NpcDeck.Count > 0;
        }

        private void EndTurn(Unit u)
        {
            if (u.Dead) return;
            if (u.Hero)
            {
                u.Discard.AddRange(u.Hand);
                u.Hand.Clear();
            }
            else
            {
                u.Discard.AddRange(u.Hand);
                u.Hand.Clear();
            }
            Tick(u, AtWhen.TurnEnd);
        }

        private void DrawCards(Unit u, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (u.Draw.Count == 0)
                {
                    if (u.Discard.Count == 0) return;
                    u.Draw.AddRange(u.Discard); u.Discard.Clear(); Shuffle(u.Draw);
                }
                var c = u.Draw[u.Draw.Count - 1];
                u.Draw.RemoveAt(u.Draw.Count - 1);
                u.Hand.Add(c);
            }
        }

        private void Tick(Unit u, AtWhen when)
        {
            if (u.Auras.Count == 0) return;
            foreach (var id in u.Auras.Keys.ToList())
            {
                if (u.Dead) return;
                var ai = Aura(id);
                bool hit = when == AtWhen.TurnBegin ? ai.AtTurnBegin : when == AtWhen.TurnEnd ? ai.AtTurn : when == AtWhen.RoundBegin ? ai.AtRoundBegin : ai.AtRound;
                if (!hit || !u.Auras.TryGetValue(id, out int charges)) continue;
                if (ai.ProduceDmg)
                {
                    float d = ai.DmgFlat + ai.DmgPerCharge * charges;
                    if (d > 0) Damage(null, u, d, ai.DmgType, true, false, true);
                }
                if (ai.ProduceHeal && !u.Dead)
                {
                    float h = ai.HealFlat + ai.HealPerCharge * charges;
                    if (h > 0) HealUnit(null, u, h, false);
                }
                if (u.Dead) return;
                int left = ai.ConsumeAll ? 0 : charges - ai.Consumed;
                if (left <= 0) u.Auras.Remove(id); else u.Auras[id] = left;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  AURAS, DAMAGE, HEAL
        // ═══════════════════════════════════════════════════════════

        private void AddAura(Unit u, string id, int charges)
        {
            if (string.IsNullOrEmpty(id) || charges <= 0 || u.Dead) return;
            if (u.Immune.Contains(id) || u.Immune.Contains(id.ToLowerInvariant())) return;
            var ai = Aura(id);
            if (!ai.Known) return;
            u.Auras.TryGetValue(id, out int cur);
            int now = cur + charges;
            if (ai.Max > 0) now = Math.Min(now, ai.Max);
            u.Auras[id] = now;
        }

        private bool TypeMatches(Enums.DamageType slot, Enums.DamageType dmg) => slot == Enums.DamageType.All || (slot != Enums.DamageType.None && slot == dmg);

        private float DamageAmount(Unit src, Unit tgt, float amount, Enums.DamageType type, bool dot)
        {
            float d = amount;
            if (src != null)
            {
                float flat = 0, pct = 0;
                foreach (var kv in src.Auras)
                {
                    var ai = Aura(kv.Key);
                    for (int i = 0; i < 4; i++)
                    {
                        if (!TypeMatches(ai.OutType[i], type)) continue;
                        flat += ai.OutFlat[i] + ai.OutFlatStack[i] * kv.Value;
                        pct += ai.OutPct[i] + ai.OutPctStack[i] * kv.Value;
                    }
                }
                d = (d + flat) * (1f + pct / 100f);
                d *= src.DmgMult;
            }
            float inFlat = 0, inPct = 0, res = type == Enums.DamageType.None || type == Enums.DamageType.All ? 0 : tgt.Res[(int)type];
            foreach (var kv in tgt.Auras)
            {
                var ai = Aura(kv.Key);
                for (int i = 0; i < 2; i++)
                {
                    if (!TypeMatches(ai.InType[i], type)) continue;
                    inFlat += ai.InFlat[i] + ai.InFlatStack[i] * kv.Value;
                    inPct += ai.InPct[i] + ai.InPctStack[i] * kv.Value;
                }
                for (int i = 0; i < 3; i++)
                    if (ai.ResType[i] == type && type != Enums.DamageType.None) res += ai.ResFlat[i] + ai.ResStack[i] * kv.Value;
                if (ai.Invulnerable) return 0f;
            }
            if (!dot) d += inFlat;
            d *= 1f + inPct / 100f;
            if (res != 0) d *= 1f - Math.Min(res, 100f) / 100f;
            return Math.Max(0f, (float)Math.Floor(d));
        }

        /// <summary>Applies damage after modifiers, block and resistances. Returns hp actually lost. dry = only predict.</summary>
        private float Damage(Unit src, Unit tgt, float amount, Enums.DamageType type, bool ignoreBlock, bool dry, bool dot = false, string cardId = null)
        {
            if (tgt.Dead) return 0;
            float d = DamageAmount(src, tgt, amount, type, dot);
            if (d <= 0) return 0;

            if (!ignoreBlock)
            {
                foreach (var id in tgt.Auras.Keys.ToList())
                {
                    var ai = Aura(id);
                    if (ai.PreventPerStack <= 0) continue;
                    if (ai.PreventType != Enums.DamageType.None && !TypeMatches(ai.PreventType, type)) continue;
                    int charges = tgt.Auras[id];
                    float cap = charges * ai.PreventPerStack;
                    float take = Math.Min(cap, d);
                    d -= take;
                    if (!dry)
                    {
                        int used = (int)Math.Ceiling(take / ai.PreventPerStack);
                        if (charges - used <= 0) tgt.Auras.Remove(id); else tgt.Auras[id] = charges - used;
                    }
                    if (d <= 0) break;
                }
            }
            if (d <= 0) return 0;
            float lost = Math.Min(d, tgt.Hp);
            if (dry) return lost;

            tgt.Hp -= (int)lost;
            tgt.Stat.DamageTaken += lost;
            if (src != null) src.Stat.DamageDealt += lost;
            if (cardId != null) CardStat(cardId).Damage += lost;
            if (tgt.Hp <= 0)
            {
                tgt.Dead = true; tgt.DeadRound = _round; tgt.Auras.Clear();
                if (tgt.FinishOnDead) _finishFlag = true;
            }
            return lost;
        }

        private float HealUnit(Unit src, Unit tgt, float amount, bool dry, string cardId = null)
        {
            if (tgt.Dead || amount <= 0) return 0;
            float h = amount;
            if (src != null)
            {
                float flat = 0, pct = 0;
                foreach (var kv in src.Auras)
                {
                    var ai = Aura(kv.Key);
                    flat += ai.HealDoneFlat + ai.HealDoneStack * kv.Value;
                    pct += ai.HealDonePct + ai.HealDonePctStack * kv.Value;
                }
                h = (h + flat) * (1f + pct / 100f);
            }
            {
                float flat = 0, pct = 0;
                foreach (var kv in tgt.Auras)
                {
                    var ai = Aura(kv.Key);
                    flat += ai.HealRecFlat + ai.HealRecStack * kv.Value;
                    pct += ai.HealRecPct + ai.HealRecPctStack * kv.Value;
                }
                h = (h + flat) * (1f + pct / 100f);
            }
            h = Math.Max(0f, (float)Math.Floor(h));
            float real = Math.Min(h, tgt.MaxHp - tgt.Hp);
            if (dry) return real;
            tgt.Hp += (int)real;
            tgt.Stat.Healed += real;
            if (cardId != null) CardStat(cardId).Heal += real;
            return real;
        }

        private SimCardStat CardStat(string id)
        {
            if (!Result.Cards.TryGetValue(id, out var s)) Result.Cards[id] = s = new SimCardStat { Id = id };
            return s;
        }

        // ═══════════════════════════════════════════════════════════
        //  TARGETING
        // ═══════════════════════════════════════════════════════════

        private IEnumerable<Unit> Allies(Unit u) => (u.Hero ? _heroUnits : _npcUnits).Where(x => !x.Dead);
        private IEnumerable<Unit> Foes(Unit u) => (u.Hero ? _npcUnits : _heroUnits).Where(x => !x.Dead);

        private List<Unit> Adjacent(Unit t)
        {
            var side = (t.Hero ? _heroUnits : _npcUnits).Where(x => !x.Dead).ToList();
            int i = side.IndexOf(t);
            var r = new List<Unit>();
            if (i > 0) r.Add(side[i - 1]);
            if (i >= 0 && i < side.Count - 1) r.Add(side[i + 1]);
            return r;
        }

        private bool HasTaunt(Unit u) => u.Auras.Keys.Any(k => Aura(k).Taunt);

        private IEnumerable<Unit> Candidates(Unit caster, CardDef c)
        {
            switch (c.TargetSide)
            {
                case Enums.CardTargetSide.Self: return new[] { caster };
                case Enums.CardTargetSide.Friend: return Allies(caster);
                case Enums.CardTargetSide.FriendNotSelf: return Allies(caster).Where(x => x != caster);
                case Enums.CardTargetSide.Anyone: return c.Damage > 0 || c.Damage2 > 0 ? FoesTaunted(caster) : Allies(caster);
                default: return FoesTaunted(caster);
            }
        }

        private IEnumerable<Unit> FoesTaunted(Unit caster)
        {
            var foes = Foes(caster).ToList();
            var taunters = foes.Where(HasTaunt).ToList();
            return taunters.Count > 0 ? taunters : foes;
        }

        private List<Unit> Affected(Unit caster, CardDef c, Unit chosen)
        {
            if (c.TargetSide == Enums.CardTargetSide.Self) return new List<Unit> { caster };
            if (c.TargetType == Enums.CardTargetType.Global)
            {
                bool foe = chosen.Hero != caster.Hero;
                return (foe ? Foes(caster) : Allies(caster)).ToList();
            }
            return new List<Unit> { chosen };
        }

        // ═══════════════════════════════════════════════════════════
        //  CARD EFFECTS
        // ═══════════════════════════════════════════════════════════

        /// <summary>Applies (or with dry = true, only scores) a card. Heroes score by value; the same code runs for real plays.</summary>
        private float Cast(Unit caster, CardDef c, Unit chosen, bool dry)
        {
            float score = 0;
            int reps = Math.Max(1, c.EffectRepeat);
            var main = Affected(caster, c, chosen);
            bool chosenFoe = chosen.Hero != caster.Hero;
            string id = c.Id;

            for (int rep = 0; rep < reps; rep++)
            {
                foreach (var t in main)
                {
                    if (t.Dead) continue;
                    bool foe = t.Hero != caster.Hero;
                    if (c.Damage > 0 && foe)
                    {
                        float lost = Damage(caster, t, c.Damage, c.DamageType, c.IgnoreBlock, dry, false, dry ? null : id);
                        score += Value(lost, t);
                        if (c.DamageSides > 0)
                            foreach (var a in Adjacent(t)) { float l = Damage(caster, a, c.DamageSides, c.DamageType, c.IgnoreBlock, dry, false, dry ? null : id); score += Value(l, a); }
                    }
                    if (c.Damage2 > 0 && foe)
                    {
                        float lost = Damage(caster, t, c.Damage2, c.DamageType2, c.IgnoreBlock2, dry, false, dry ? null : id);
                        score += Value(lost, t);
                        if (c.DamageSides2 > 0)
                            foreach (var a in Adjacent(t)) { float l = Damage(caster, a, c.DamageSides2, c.DamageType2, c.IgnoreBlock2, dry, false, dry ? null : id); score += Value(l, a); }
                    }
                    if ((c.Heal > 0 || c.HealSides > 0) && !foe)
                    {
                        score += HealUnit(caster, t, c.Heal, dry, dry ? null : id) * 1.1f;
                        if (c.HealSides > 0) foreach (var a in Adjacent(t)) score += HealUnit(caster, a, c.HealSides, dry, dry ? null : id) * 1.1f;
                    }
                    score += ApplyAura(t, caster, c.Aura, c.AuraCharges, dry);
                    score += ApplyAura(t, caster, c.Aura2, c.Aura2Charges, dry);
                    score += ApplyAura(t, caster, c.Aura3, c.Aura3Charges, dry);
                    score += ApplyAura(t, caster, c.Curse, c.CurseCharges, dry);
                    score += ApplyAura(t, caster, c.Curse2, c.Curse2Charges, dry);
                    score += ApplyAura(t, caster, c.Curse3, c.Curse3Charges, dry);
                }

                if (c.HealSelf > 0) score += HealUnit(caster, caster, c.HealSelf, dry, dry ? null : id) * 1.1f;
                score += ApplyAura(caster, caster, c.AuraSelf, c.AuraSelfCharges, dry);
                score += ApplyAura(caster, caster, c.AuraSelf2, c.AuraSelf2Charges, dry);
                score += ApplyAura(caster, caster, c.AuraSelf3, c.AuraSelf3Charges, dry);
                score += ApplyAura(caster, caster, c.CurseSelf, c.CurseSelfCharges, dry);
                score += ApplyAura(caster, caster, c.CurseSelf2, c.CurseSelf2Charges, dry);
                score += ApplyAura(caster, caster, c.CurseSelf3, c.CurseSelf3Charges, dry);

                int self = c.DamageSelf + c.SelfHealthLoss;
                if (self > 0)
                {
                    float lost = Damage(null, caster, self, c.DamageSelfType ?? Enums.DamageType.None, true, dry);
                    score -= lost * 0.6f;
                }
                if (c.DamageSelf2 > 0)
                {
                    float lost = Damage(null, caster, c.DamageSelf2, c.DamageSelfType2 ?? Enums.DamageType.None, true, dry);
                    score -= lost * 0.6f;
                }
            }

            score += c.DrawCard * 3f;
            if (c.EnergyRecharges != null) foreach (var r in c.EnergyRecharges) score += Math.Max(1, r.Amount) * 2.5f;
            if (c.AddCard > 0 && !string.IsNullOrEmpty(c.AddCardId)) score += c.AddCard * 2f;

            if (!dry)
            {
                if (caster.Hero)
                {
                    if (c.DrawCard > 0) DrawCards(caster, c.DrawCard);
                    if (c.EnergyRecharges != null) foreach (var r in c.EnergyRecharges) caster.Energy = Math.Min(10, caster.Energy + Math.Max(1, r.Amount));
                    if (c.AddCard > 0 && !string.IsNullOrEmpty(c.AddCardId)) AddCards(caster, c);
                }
                NoteUnmodelled(c);
            }
            return score;
        }

        private void AddCards(Unit u, CardDef c)
        {
            var def = ResolveCard(c.AddCardId);
            if (def == null) return;
            for (int i = 0; i < c.AddCard; i++)
            {
                var card = new Card { Def = def, Cost = Math.Max(0, def.EnergyCost - c.AddCardReducedCost), Vanish = c.AddCardVanish || def.Vanish };
                switch (c.AddCardPlace)
                {
                    case Enums.CardPlace.Hand: u.Hand.Add(card); break;
                    case Enums.CardPlace.TopDeck: u.Draw.Add(card); break;
                    case Enums.CardPlace.BottomDeck: u.Draw.Insert(0, card); break;
                    case Enums.CardPlace.RandomDeck: u.Draw.Insert(_rng.Next(u.Draw.Count + 1), card); break;
                    case Enums.CardPlace.Vanish: break;
                    default: u.Discard.Add(card); break;
                }
            }
        }

        private void NoteUnmodelled(CardDef c)
        {
            var notes = new List<string>();
            if (c.SummonNum > 0 || (c.ExtraSummons?.Count ?? 0) > 0) notes.Add("summons");
            if (c.ExtraEffects != null && c.ExtraEffects.Count > 0) notes.Add("extra effects");
            if (c.HealCurses > 0 || c.DispelAuras > 0 || c.StealAuras > 0 || c.TransferCurses > 0 || c.ReduceCurses > 0 || c.ReduceAuras > 0 || c.IncreaseCurses > 0 || c.IncreaseAuras > 0)
                notes.Add("aura/curse removal or transfer");
            if (c.PushTarget > 0 || c.PullTarget > 0) notes.Add("push/pull");
            if (c.GrantStanza > 0) notes.Add("stanza");
            if (c.DamageEnergyBonus > 0 || c.HealEnergyBonus > 0) notes.Add("energy bonus");
            if (c.LookCards > 0) notes.Add("look at cards");
            if (notes.Count > 0) Result.UnmodelledCards.Add($"{c.Id}: {string.Join(", ", notes)}");
        }

        private float Value(float hpLost, Unit t)
        {
            if (hpLost <= 0) return 0;
            float v = hpLost;
            if (t.Hp - hpLost <= 0) v += 6 + t.MaxHp * 0.1f;
            return v;
        }

        private float ApplyAura(Unit target, Unit caster, string id, int charges, bool dry)
        {
            if (string.IsNullOrEmpty(id) || charges <= 0) return 0;
            var ai = Aura(id);
            if (!ai.Known) return 0;
            if (dry)
            {
                bool friendly = target.Hero == caster.Hero;
                float w;
                if (ai.IsBlock) { target.Auras.TryGetValue("block", out int bl); w = bl > 18 ? 0.25f : 0.8f; }
                else if (ai.ProduceDmg) w = 0.9f;
                else w = 0.6f;
                if (ai.IsAura) return friendly ? charges * w : -charges * w;
                return friendly ? -charges * w : charges * w;
            }
            AddAura(target, id, charges);
            return 0;
        }

        // ═══════════════════════════════════════════════════════════
        //  ACTING
        // ═══════════════════════════════════════════════════════════

        private void HeroAct(Unit h)
        {
            for (int guard = 0; guard < 40; guard++)
            {
                if (h.Dead || CheckEnd() != null) return;
                Card bestCard = null; Unit bestTarget = null; float bestRatio = 0;
                foreach (var card in h.Hand)
                {
                    var c = card.Def;
                    if (!c.Playable || card.Cost > h.Energy) continue;
                    foreach (var t in Candidates(h, c))
                    {
                        float score = Cast(h, c, t, true);
                        if (score <= 0.5f) continue;
                        float ratio = score / (card.Cost + 0.7f);
                        if (ratio > bestRatio) { bestRatio = ratio; bestCard = card; bestTarget = t; }
                        if (c.TargetType == Enums.CardTargetType.Global || c.TargetSide == Enums.CardTargetSide.Self) break;
                    }
                }
                if (bestCard == null) return;
                h.Energy -= bestCard.Cost;
                h.Hand.Remove(bestCard);
                CardStat(bestCard.Def.Id).Plays++;
                Cast(h, bestCard.Def, bestTarget, false);
                if (bestCard.Vanish) { /* removed for the rest of the fight */ }
                else h.Discard.Add(bestCard);
            }
        }

        private void NpcAct(Unit n)
        {
            var hand = n.Hand.Select(c => (card: c, ai: _npcPick.TryGetValue(c, out var a) ? a : new AiCardDef())).OrderBy(x => x.ai.Priority).ToList();
            n.Hand.Clear();
            foreach (var (card, ai) in hand)
            {
                n.Discard.Add(card);
                if (n.Dead || CheckEnd() != null) continue;
                if (ai.PercentToCast > 0 && _rng.NextDouble() * 100.0 > ai.PercentToCast) continue;
                var c = card.Def;
                Unit chosen = PickNpcTarget(n, c, ai);
                if (chosen == null) continue;
                if (!ConditionOk(n, chosen, ai.OnlyCastIf, ai.ValueCastIf, ai) || !ConditionOk(n, chosen, ai.SecondOnlyCastIf, ai.SecondValueCastIf, ai)) continue;
                Cast(n, c, chosen, false);
            }
        }

        private Unit PickNpcTarget(Unit n, CardDef c, AiCardDef ai)
        {
            if (c.TargetSide == Enums.CardTargetSide.Self) return n;
            var pool = Candidates(n, c).ToList();
            if (pool.Count == 0) return null;
            switch (ai.TargetCast)
            {
                case Enums.TargetCast.Front: return pool[0];
                case Enums.TargetCast.Back: return pool[pool.Count - 1];
                case Enums.TargetCast.Middle: return pool[pool.Count / 2];
                case Enums.TargetCast.LessHealthPercent: return pool.OrderBy(u => (float)u.Hp / u.MaxHp).First();
                case Enums.TargetCast.LessHealthFlat:
                case Enums.TargetCast.LessHealthAbsolute: return pool.OrderBy(u => u.Hp).First();
                case Enums.TargetCast.MoreHealthPercent: return pool.OrderByDescending(u => (float)u.Hp / u.MaxHp).First();
                case Enums.TargetCast.MoreHealthFlat: return pool.OrderByDescending(u => u.Hp).First();
                case Enums.TargetCast.AnyButFront: return pool.Count > 1 ? pool[_rng.Next(1, pool.Count)] : pool[0];
                case Enums.TargetCast.AnyButBack: return pool.Count > 1 ? pool[_rng.Next(0, pool.Count - 1)] : pool[0];
                default: return pool[_rng.Next(pool.Count)];
            }
        }

        private bool ConditionOk(Unit caster, Unit target, Enums.OnlyCastIf cond, float value, AiCardDef ai)
        {
            switch (cond)
            {
                case Enums.OnlyCastIf.Always: return true;
                case Enums.OnlyCastIf.TargetLifeLessThanPercent: return 100f * target.Hp / target.MaxHp < value;
                case Enums.OnlyCastIf.TargetLifeHigherThanPercent: return 100f * target.Hp / target.MaxHp > value;
                case Enums.OnlyCastIf.TargetHasAuraCurse: return !string.IsNullOrEmpty(ai.AuracurseCastIf) && target.Auras.ContainsKey(ai.AuracurseCastIf);
                case Enums.OnlyCastIf.TargetHasNotAuraCurse: return string.IsNullOrEmpty(ai.AuracurseCastIf) || !target.Auras.ContainsKey(ai.AuracurseCastIf);
                case Enums.OnlyCastIf.TargetHasAnyAura: return target.Auras.Keys.Any(k => Aura(k).IsAura);
                case Enums.OnlyCastIf.TargetHasAnyCurse: return target.Auras.Keys.Any(k => !Aura(k).IsAura);
                case Enums.OnlyCastIf.TeamNpcAllAlive: return _npcUnits.All(u => !u.Dead);
                default: return true;
            }
        }
    }
}
