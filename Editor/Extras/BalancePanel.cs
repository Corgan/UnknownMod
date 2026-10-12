using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Balance dashboard: scores the mod's cards, enemies and combats against the base-game distribution (median of cards with the same class, cost and
    /// upgrade level; enemies and combats by tier) and analyses loot tables with the game's own drop rules. All numbers are estimates, meant to catch
    /// outliers rather than to replace playtesting.
    /// </summary>
    public sealed class BalancePanel
    {
        private enum View { Cards, Enemies, Combats, Loot }
        private enum Sort { Id, StrongestFirst, WeakestFirst }
        private enum Phase { Idle, Cards, Npcs, Combats, Done }

        private struct Pw
        {
            public float Dmg, Heal, Aura, Util;
            public float Total => Dmg + Heal + Aura + Util;
        }

        private sealed class CardStat
        {
            public Enums.CardClass Class;
            public Enums.CardUpgraded Upg;
            public Enums.CardRarity Rarity;
            public int Cost;
            public Pw P;
        }

        private sealed class NpcStat { public float Hp, Dpt; public bool Boss; public int Tier; }

        private sealed class PoolItem { public string Id; public Enums.CardType Type; public Enums.CardRarity Rarity; public bool DropOnly; }

        private readonly ModEditor _editor;
        private View _view = View.Cards;
        private Sort _sort = Sort.StrongestFirst;
        private string _filter = "";
        private int _page;
        private const int PageSize = 25;

        // Baseline (built incrementally, a few ms per frame)
        private Phase _phase = Phase.Idle;
        private List<string> _queue;
        private int _qi;
        private readonly Dictionary<string, CardStat> _baseCards = new();
        private readonly Dictionary<string, NpcStat> _baseNpcs = new();
        private readonly List<PoolItem> _pool = new();
        private readonly Dictionary<string, List<float>> _cardGroups = new();
        private readonly Dictionary<string, List<float>> _npcHp = new(), _npcDpt = new();
        private readonly Dictionary<int, List<float>> _comboHp = new(), _comboDpt = new();

        // Project results, refreshed about once a second
        private float _projTime = -10f;
        private ModProject _projCached;
        private List<(string id, CardStat s, float ratio, float baseline, string note)> _cardRows = new();
        private List<(string id, NpcStat s, float hpRatio, float dptRatio, string note)> _npcRows = new();
        private List<(string id, int tier, float hp, float dpt, float hpRatio, float dptRatio, string note)> _comboRows = new();

        // Loot
        private string _lootId = "", _lootCompare = "";
        private bool _lootSim = true;
        private int _lootSeed = 1;
        private readonly Dictionary<string, int> _poolCounts = new();
        private readonly Dictionary<string, List<string>> _simCache = new();

        public BalancePanel(ModEditor editor) { _editor = editor; }

        // ═══════════════════════════════════════════════════════════
        //  POWER MODEL
        // ═══════════════════════════════════════════════════════════

        private static float Targets(Enums.CardTargetType t) => t == Enums.CardTargetType.Global ? 2.5f : t == Enums.CardTargetType.SingleSided ? 1.5f : 1f;

        private static Pw Power(CardDef c)
        {
            var p = new Pw();
            float reps = Math.Max(1, c.EffectRepeat);
            float tm = Targets(c.TargetType);
            bool foe = c.TargetSide == Enums.CardTargetSide.Enemy;
            bool friend = c.TargetSide == Enums.CardTargetSide.Friend || c.TargetSide == Enums.CardTargetSide.Anyone || c.TargetSide == Enums.CardTargetSide.FriendNotSelf;

            p.Dmg = (c.Damage + c.DamageSides * 0.5f + c.Damage2 + c.DamageSides2 * 0.5f) * reps * (foe ? tm : 1f);
            p.Heal = (c.Heal + c.HealSides * 0.5f) * reps * (friend ? tm : 1f) + c.HealSelf * reps;

            float aura = 0f;
            float targetAura = c.AuraCharges + c.Aura2Charges + c.Aura3Charges;
            float targetCurse = c.CurseCharges + c.Curse2Charges + c.Curse3Charges;
            aura += targetAura * (foe ? -0.5f : 1f) * tm;
            aura += targetCurse * (foe ? 1f : -1f) * tm;
            aura += c.AuraSelfCharges + c.AuraSelf2Charges + c.AuraSelf3Charges;
            aura -= c.CurseSelfCharges + c.CurseSelf2Charges + c.CurseSelf3Charges;
            p.Aura = aura * reps;

            float util = 0f;
            util += c.DrawCard * 3f + c.AddCard * 2.5f + c.LookCards * 0.7f;
            util += (c.HealCurses + c.DispelAuras + c.StealAuras + c.TransferCurses) * 1.5f;
            util += (c.ReduceCurses + c.ReduceAuras + c.IncreaseCurses + c.IncreaseAuras) * 1f;
            util += (c.PushTarget + c.PullTarget) * 0.5f + c.GrantStanza * 2f;
            util += c.SummonNum * 8f + (c.ExtraSummons?.Sum(s => s.Count) ?? 0) * 8f;
            if (c.EnergyRecharges != null) foreach (var r in c.EnergyRecharges) util += (r.Amount > 0 ? r.Amount : 1) * 3f;
            util -= (c.DamageSelf + c.DamageSelf2 + c.SelfHealthLoss) * 0.5f;
            if (c.Innate) util += 0.5f;
            if (c.ExtraEffects != null)
                foreach (var e in c.ExtraEffects)
                {
                    string k = e.Kind ?? "";
                    if (k.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0) p.Dmg += e.Amount;
                    else if (k.IndexOf("Heal", StringComparison.OrdinalIgnoreCase) >= 0) p.Heal += e.Amount;
                    else p.Aura += e.Amount * 0.8f;
                }
            p.Util = util;
            if (c.Vanish)
            {
                p.Dmg *= 1.1f; p.Heal *= 1.1f; p.Aura *= 1.1f; p.Util *= 1.1f;
            }
            return p;
        }

        private static bool IsHeroOrMonster(Enums.CardClass c) =>
            c == Enums.CardClass.Warrior || c == Enums.CardClass.Mage || c == Enums.CardClass.Healer || c == Enums.CardClass.Scout ||
            c == Enums.CardClass.MagicKnight || c == Enums.CardClass.Monster;

        private static string GroupKey(Enums.CardClass cls, int cost, Enums.CardUpgraded up) => $"{cls}|{cost}|{up}";
        private static string AnyKey(int cost, Enums.CardUpgraded up) => $"*|{cost}|{up}";

        private static float Median(List<float> v)
        {
            if (v == null || v.Count == 0) return 0f;
            var s = v.OrderBy(x => x).ToList();
            int m = s.Count / 2;
            return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2f;
        }

        private static void AddTo<K>(Dictionary<K, List<float>> d, K key, float v)
        {
            if (!d.TryGetValue(key, out var l)) d[key] = l = new List<float>();
            l.Add(v);
        }

        private float CardBaseline(CardDef c, out string basis)
        {
            int cost = c.EnergyCost;
            if (_cardGroups.TryGetValue(GroupKey(c.CardClass, cost, c.CardUpgraded), out var l) && l.Count >= 4) { basis = $"{c.CardClass} cost {cost}"; return Median(l); }
            if (_cardGroups.TryGetValue(AnyKey(cost, c.CardUpgraded), out l) && l.Count >= 4) { basis = $"any class cost {cost}"; return Median(l); }
            basis = "none";
            return 0f;
        }

        // ═══════════════════════════════════════════════════════════
        //  BASELINE BUILD
        // ═══════════════════════════════════════════════════════════

        private void StartBaseline()
        {
            _baseCards.Clear(); _baseNpcs.Clear(); _pool.Clear(); _cardGroups.Clear();
            _npcHp.Clear(); _npcDpt.Clear(); _comboHp.Clear(); _comboDpt.Clear();
            _poolCounts.Clear(); _simCache.Clear();
            _queue = DataHelper.GetAllCardIds().ToList();
            _qi = 0;
            _phase = Phase.Cards;
            _projTime = -10f;
        }

        private void Advance()
        {
            if (_phase == Phase.Idle || _phase == Phase.Done) return;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 8)
            {
                if (_qi >= _queue.Count)
                {
                    if (_phase == Phase.Cards) { _queue = DataHelper.GetAllNpcIds().ToList(); _phase = Phase.Npcs; _qi = 0; continue; }
                    if (_phase == Phase.Npcs) { _queue = DataHelper.GetAllCombatIds().ToList(); _phase = Phase.Combats; _qi = 0; continue; }
                    _phase = Phase.Done;
                    _projTime = -10f;
                    return;
                }
                string id = _queue[_qi++];
                try
                {
                    switch (_phase)
                    {
                        case Phase.Cards: StepCard(id); break;
                        case Phase.Npcs: StepNpc(id); break;
                        case Phase.Combats: StepCombat(id); break;
                    }
                }
                catch (Exception ex) { Plugin.Log.LogDebug($"[Balance] {_phase} {id}: {ex.Message}"); }
            }
        }

        private void StepCard(string id)
        {
            var rt = DataHelper.GetCard(id);
            if (rt == null) return;
            var def = ModProjectBuilder.SnapshotCard(rt);
            if (def == null) return;
            var st = new CardStat { Class = def.CardClass, Upg = def.CardUpgraded, Rarity = def.CardRarity, Cost = def.EnergyCost, P = Power(def) };
            _baseCards[id] = st;

            if (def.CardClass == Enums.CardClass.Item)
            {
                var item = DataHelper.GetItem(id);
                if (item != null && def.CardUpgraded != Enums.CardUpgraded.Rare)
                    _pool.Add(new PoolItem { Id = id, Type = def.CardType, Rarity = def.CardRarity, DropOnly = item.DropOnly });
                return;
            }
            if (!IsHeroOrMonster(def.CardClass) || !def.Playable || st.P.Total <= 0f) return;
            AddTo(_cardGroups, GroupKey(st.Class, st.Cost, st.Upg), st.P.Total);
            AddTo(_cardGroups, AnyKey(st.Cost, st.Upg), st.P.Total);
        }

        private float NpcDpt(NpcDef d, Func<string, Pw?> cardLookup)
        {
            if (d.AiCards == null || d.AiCards.Count == 0) return 0f;
            float weight = 0f, dmg = 0f;
            foreach (var a in d.AiCards)
            {
                int n = Math.Max(1, a.UnitsInDeck);
                var pw = cardLookup(a.CardId);
                weight += n;
                if (pw.HasValue) dmg += pw.Value.Dmg * n;
            }
            return weight > 0 ? dmg / weight * Math.Max(1, d.CardsInHand) : 0f;
        }

        private Pw? BaseCardPw(string id) => _baseCards.TryGetValue(id, out var s) ? s.P : (Pw?)null;

        private void StepNpc(string id)
        {
            var npc = DataHelper.GetExistingNPC(id, false);
            if (npc == null) return;
            var d = DataHelper.SnapshotNpc(npc);
            if (d == null) return;
            var st = new NpcStat { Hp = d.Hp, Boss = d.IsBoss, Tier = d.TierReward, Dpt = NpcDpt(d, BaseCardPw) };
            _baseNpcs[id] = st;
            if (st.Hp <= 0) return;
            AddTo(_npcHp, NpcKey(st.Boss, st.Tier), st.Hp);
            AddTo(_npcDpt, NpcKey(st.Boss, st.Tier), st.Dpt);
            AddTo(_npcHp, NpcKey(st.Boss, -1), st.Hp);
            AddTo(_npcDpt, NpcKey(st.Boss, -1), st.Dpt);
        }

        private static string NpcKey(bool boss, int tier) => $"{(boss ? "boss" : "mob")}|{tier}";

        private void StepCombat(string id)
        {
            var c = DataHelper.GetExistingCombat(id);
            if (c == null || c.NPCList == null) return;
            float hp = 0, dpt = 0;
            foreach (var n in c.NPCList)
            {
                if (n == null || !_baseNpcs.TryGetValue(n.Id, out var s)) continue;
                hp += s.Hp; dpt += s.Dpt;
            }
            if (hp <= 0) return;
            int tier = (int)c.CombatTier;
            AddTo(_comboHp, tier, hp);
            AddTo(_comboDpt, tier, dpt);
        }

        // ═══════════════════════════════════════════════════════════
        //  PROJECT RESULTS
        // ═══════════════════════════════════════════════════════════

        private void RefreshProject(ModProject proj)
        {
            if (proj == _projCached && Time.realtimeSinceStartup - _projTime < 1f) return;
            _projCached = proj;
            _projTime = Time.realtimeSinceStartup;
            _cardRows.Clear(); _npcRows.Clear(); _comboRows.Clear();
            if (proj == null || _phase != Phase.Done) return;

            CardDef Lookup(string id) => proj.Cards.TryGetValue(id, out var d) ? d : null;

            foreach (var kv in proj.Cards)
            {
                var c = kv.Value;
                if (!IsHeroOrMonster(c.CardClass) || !c.Playable) continue;
                var st = new CardStat { Class = c.CardClass, Upg = c.CardUpgraded, Rarity = c.CardRarity, Cost = c.EnergyCost, P = Power(c) };
                float b = CardBaseline(c, out string basis);
                float ratio = b > 0 ? st.P.Total / b : 0f;
                string note = b <= 0 ? "no baseline" : ratio >= 1.5f ? "strong" : ratio <= 0.6f ? "weak" : "";
                if (st.P.Total <= 0f && b > 0) note = "no scored effect";
                if (!string.IsNullOrEmpty(c.BaseCardId) && c.BaseCardId != c.Id)
                {
                    float bp = -1f;
                    var bd = Lookup(c.BaseCardId);
                    if (bd != null) bp = Power(bd).Total;
                    else if (_baseCards.TryGetValue(c.BaseCardId, out var bs)) bp = bs.P.Total;
                    if (bp > 0 && st.P.Total < bp * 0.9f) note = (note.Length > 0 ? note + ", " : "") + "weaker than its base card";
                }
                _cardRows.Add((kv.Key, st, ratio, b, note));
            }

            Pw? ProjCardPw(string id) { var d = Lookup(id); return d != null ? Power(d) : BaseCardPw(id); }

            foreach (var kv in proj.Npcs)
            {
                var d = kv.Value;
                var st = new NpcStat { Hp = d.Hp, Boss = d.IsBoss, Tier = d.TierReward, Dpt = NpcDpt(d, ProjCardPw) };
                float bh = Median(_npcHp.TryGetValue(NpcKey(st.Boss, st.Tier), out var l1) && l1.Count >= 4 ? l1 : _npcHp.GetValueOrNull(NpcKey(st.Boss, -1)));
                float bd = Median(_npcDpt.TryGetValue(NpcKey(st.Boss, st.Tier), out var l2) && l2.Count >= 4 ? l2 : _npcDpt.GetValueOrNull(NpcKey(st.Boss, -1)));
                float hr = bh > 0 ? st.Hp / bh : 0f, dr = bd > 0 ? st.Dpt / bd : 0f;
                string note = "";
                if (d.AiCards == null || d.AiCards.Count == 0) note = "no AI cards";
                else if (st.Dpt <= 0f) note = "deals no damage";
                else if (hr >= 1.8f && dr >= 1.5f) note = "tanky and hits hard";
                else if (hr >= 1.8f) note = "very tanky";
                else if (dr >= 1.8f) note = "hits very hard";
                else if (hr > 0 && hr <= 0.4f) note = "very fragile";
                _npcRows.Add((kv.Key, st, hr, dr, note));
            }

            NpcStat NpcFor(string id)
            {
                if (proj.Npcs.TryGetValue(id, out var d)) return new NpcStat { Hp = d.Hp, Boss = d.IsBoss, Tier = d.TierReward, Dpt = NpcDpt(d, ProjCardPw) };
                return _baseNpcs.TryGetValue(id, out var s) ? s : null;
            }

            foreach (var kv in proj.Combats)
            {
                float hp = 0, dpt = 0; int missing = 0;
                foreach (var nid in kv.Value.NpcIds)
                {
                    if (string.IsNullOrEmpty(nid)) continue;
                    var s = NpcFor(nid);
                    if (s == null) { missing++; continue; }
                    hp += s.Hp; dpt += s.Dpt;
                }
                int tier = (int)kv.Value.CombatTier;
                float bh = Median(_comboHp.GetValueOrNull(tier)), bd = Median(_comboDpt.GetValueOrNull(tier));
                float hr = bh > 0 ? hp / bh : 0f, dr = bd > 0 ? dpt / bd : 0f;
                string note = missing > 0 ? $"{missing} unknown enem{(missing == 1 ? "y" : "ies")}" : "";
                if (hp <= 0) note = "no enemies";
                else if (hr >= 1.6f && dr >= 1.4f) note = "much harder than " + kv.Value.CombatTier;
                else if (hr > 0 && hr <= 0.5f && dr <= 0.6f) note = "much easier than " + kv.Value.CombatTier;
                else if (hr >= 1.6f) note = "lots of HP for " + kv.Value.CombatTier;
                else if (dr >= 1.8f) note = "high damage for " + kv.Value.CombatTier;
                _comboRows.Add((kv.Key, tier, hp, dpt, hr, dr, note));
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  DRAW
        // ═══════════════════════════════════════════════════════════

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("Open a mod project first."); return; }

            Advance();
            GUILayout.Label("<color=#888>Compares your content with the base game using a rough power model (damage, healing, buffs/debuffs, card draw, summons, minus self damage). Estimates only: flags outliers, doesn't replace playtesting.</color>", EditorStyles.RichLabel);

            if (_phase == Phase.Idle)
            {
                if (GUILayout.Button("Build base-game baseline (takes a few seconds)", GUILayout.Height(26))) StartBaseline();
                DrawLootView(proj);
                return;
            }
            if (_phase != Phase.Done)
            {
                float done = _queue == null || _queue.Count == 0 ? 0 : _qi / (float)_queue.Count;
                GUILayout.Label($"Building baseline: {_phase} {(int)(done * 100)}%  (cards {_baseCards.Count}, enemies {_baseNpcs.Count})");
            }

            GUILayout.BeginHorizontal();
            ViewButton("Cards", View.Cards);
            ViewButton("Enemies", View.Enemies);
            ViewButton("Combats", View.Combats);
            ViewButton("Loot", View.Loot);
            if (_phase == Phase.Done && GUILayout.Button("\u21BB Rebuild", EditorStyles.MiniButton, GUILayout.Width(70))) StartBaseline();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_view == View.Loot) { DrawLootView(proj); return; }
            if (_phase != Phase.Done) return;

            RefreshProject(proj);
            switch (_view)
            {
                case View.Cards: DrawCards(); break;
                case View.Enemies: DrawEnemies(); break;
                case View.Combats: DrawCombats(); break;
            }
        }

        private void ViewButton(string label, View v)
        {
            bool on = _view == v;
            if (GUILayout.Button(on ? $"<b><color=cyan>{label}</color></b>" : label, on ? EditorStyles.SubTabActive : GUI.skin.button, GUILayout.ExpandWidth(false)))
            {
                _view = v; _page = 0;
            }
        }

        private void DrawListControls(int count, out int skip)
        {
            GUILayout.BeginHorizontal();
            string f = GUILayout.TextField(_filter, GUILayout.Width(140));
            if (f != _filter) { _filter = f; _page = 0; }
            _sort = EditorFields.EnumField("Sort", _sort, "bal_sort");
            GUILayout.EndHorizontal();
            int pages = Math.Max(1, (count + PageSize - 1) / PageSize);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("\u25C0", EditorStyles.MiniButton, GUILayout.Width(26))) _page = Math.Max(0, _page - 1);
            GUILayout.Label($"Page {_page + 1}/{pages}  ({count} rows)", GUILayout.Width(150));
            if (GUILayout.Button("\u25B6", EditorStyles.MiniButton, GUILayout.Width(26))) _page = Math.Min(pages - 1, _page + 1);
            GUILayout.EndHorizontal();
            skip = _page * PageSize;
        }

        private static string Tint(float ratio, string txt, float hi, float lo) =>
            ratio <= 0f ? txt : ratio >= hi ? $"<color=#f96>{txt}</color>" : ratio <= lo ? $"<color=#9bf>{txt}</color>" : txt;

        private void DrawCards()
        {
            var rows = _cardRows.Where(r => _filter.Length == 0 || r.id.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (_cardRows.Count == 0) { GUILayout.Label("This mod has no hero or monster cards yet."); return; }

            var scored = _cardRows.Where(r => r.baseline > 0).ToList();
            if (scored.Count > 0)
                GUILayout.Label($"<b>{_cardRows.Count}</b> cards. Average power vs base game: <b>{scored.Average(r => r.ratio):0.00}\u00D7</b>. {scored.Count(r => r.ratio >= 1.5f)} strong, {scored.Count(r => r.ratio <= 0.6f)} weak.", EditorStyles.RichLabel);

            rows = _sort == Sort.Id ? rows.OrderBy(r => r.id).ToList()
                : _sort == Sort.StrongestFirst ? rows.OrderByDescending(r => r.ratio).ToList() : rows.OrderBy(r => r.baseline > 0 ? r.ratio : 99f).ToList();
            DrawListControls(rows.Count, out int skip);

            GUILayout.Label("<color=#888>  card                      class      cost  power  base   ratio  notes</color>", EditorStyles.RichLabel);
            foreach (var r in rows.Skip(skip).Take(PageSize))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(r.id, EditorStyles.MiniButton, GUILayout.Width(170))) _editor.InspectCard(r.id);
                GUILayout.Label($"{r.s.Class} {r.s.Upg}", GUILayout.Width(110));
                GUILayout.Label(r.s.Cost.ToString(), GUILayout.Width(26));
                GUILayout.Label(r.s.P.Total.ToString("0.0"), GUILayout.Width(44));
                GUILayout.Label(r.baseline > 0 ? r.baseline.ToString("0.0") : "-", GUILayout.Width(44));
                GUILayout.Label(Tint(r.ratio, r.baseline > 0 ? $"{r.ratio:0.00}\u00D7" : "-", 1.5f, 0.6f), EditorStyles.RichLabel, GUILayout.Width(52));
                GUILayout.Label($"<color=#fc6>{r.note}</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            DrawBaselineTable();
        }

        private void DrawBaselineTable()
        {
            GUILayout.Label("<b>Base-game median power by cost</b> (all classes, no upgrade)", EditorStyles.RichLabel);
            var sb = new StringBuilder();
            for (int cost = 0; cost <= 6; cost++)
                if (_cardGroups.TryGetValue(AnyKey(cost, Enums.CardUpgraded.No), out var l) && l.Count > 0)
                    sb.Append($"cost {cost}: {Median(l):0.0} (n={l.Count})   ");
            GUILayout.Label(sb.ToString());
        }

        private void DrawEnemies()
        {
            if (_npcRows.Count == 0) { GUILayout.Label("This mod has no enemies yet."); return; }
            var rows = _npcRows.Where(r => _filter.Length == 0 || r.id.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            rows = _sort == Sort.Id ? rows.OrderBy(r => r.id).ToList()
                : _sort == Sort.StrongestFirst ? rows.OrderByDescending(r => r.hpRatio + r.dptRatio).ToList() : rows.OrderBy(r => r.hpRatio + r.dptRatio).ToList();
            DrawListControls(rows.Count, out int skip);
            GUILayout.Label("<color=#888>Damage/turn = average damage of the AI deck \u00D7 cards in hand. Compared with base enemies of the same kind and reward tier.</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>  enemy                     HP     \u00D7base   dmg/turn  \u00D7base  notes</color>", EditorStyles.RichLabel);
            foreach (var r in rows.Skip(skip).Take(PageSize))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(r.id + (r.s.Boss ? " \u2605" : ""), EditorStyles.MiniButton, GUILayout.Width(170))) _editor.InspectNpc(r.id);
                GUILayout.Label(r.s.Hp.ToString("0"), GUILayout.Width(50));
                GUILayout.Label(Tint(r.hpRatio, r.hpRatio > 0 ? $"{r.hpRatio:0.00}\u00D7" : "-", 1.8f, 0.4f), EditorStyles.RichLabel, GUILayout.Width(52));
                GUILayout.Label(r.s.Dpt.ToString("0.0"), GUILayout.Width(60));
                GUILayout.Label(Tint(r.dptRatio, r.dptRatio > 0 ? $"{r.dptRatio:0.00}\u00D7" : "-", 1.8f, 0.4f), EditorStyles.RichLabel, GUILayout.Width(52));
                GUILayout.Label($"<color=#fc6>{r.note}</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawCombats()
        {
            if (_comboRows.Count == 0) { GUILayout.Label("This mod has no combats yet."); return; }
            var rows = _comboRows.Where(r => _filter.Length == 0 || r.id.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            rows = _sort == Sort.Id ? rows.OrderBy(r => r.id).ToList()
                : _sort == Sort.StrongestFirst ? rows.OrderByDescending(r => r.hpRatio + r.dptRatio).ToList() : rows.OrderBy(r => r.hpRatio + r.dptRatio).ToList();
            DrawListControls(rows.Count, out int skip);
            GUILayout.Label("<color=#888>Totals of every enemy in the fight, compared with base combats of the same tier.</color>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>  combat                    tier   HP     \u00D7base   dmg/turn  \u00D7base  notes</color>", EditorStyles.RichLabel);
            foreach (var r in rows.Skip(skip).Take(PageSize))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(r.id, EditorStyles.MiniButton, GUILayout.Width(170))) _editor.InspectCombat(r.id);
                GUILayout.Label("T" + r.tier, GUILayout.Width(36));
                GUILayout.Label(r.hp.ToString("0"), GUILayout.Width(50));
                GUILayout.Label(Tint(r.hpRatio, r.hpRatio > 0 ? $"{r.hpRatio:0.00}\u00D7" : "-", 1.6f, 0.5f), EditorStyles.RichLabel, GUILayout.Width(52));
                GUILayout.Label(r.dpt.ToString("0.0"), GUILayout.Width(60));
                GUILayout.Label(Tint(r.dptRatio, r.dptRatio > 0 ? $"{r.dptRatio:0.00}\u00D7" : "-", 1.8f, 0.5f), EditorStyles.RichLabel, GUILayout.Width(52));
                GUILayout.Label($"<color=#fc6>{r.note}</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  LOOT ANALYZER
        // ═══════════════════════════════════════════════════════════

        private LootDef ResolveLoot(ModProject proj, string id, out string source)
        {
            source = "";
            if (string.IsNullOrEmpty(id)) return null;
            if (proj.Loot.TryGetValue(id, out var d)) { source = "your table"; return d; }
            var baseData = DataHelper.GetLootData(id);
            if (baseData == null) return null;
            if (proj.LootPatches.ContainsKey(id)) source = "base table (your patch isn't applied to this preview)";
            else source = "base-game table";
            return DataHelper.SnapshotLoot(baseData);
        }

        private void DrawLootView(ModProject proj)
        {
            var ids = RefIds.Loot(proj);
            _lootId = EditorFields.IdDropdown("Loot table", _lootId, ids, "bal_loot", pickerMode: EntityPicker.Mode.Loot);
            _lootCompare = EditorFields.IdDropdown("Compare with", _lootCompare, ids, "bal_loot2", pickerMode: EntityPicker.Mode.Loot);
            _lootSim = EditorFields.Toggle("Simulate 4000 drops", _lootSim);
            if (_phase != Phase.Done && _lootSim) GUILayout.Label("<color=#fc6>The item pool is still being scanned; wait for the baseline to finish for exact drop results.</color>", EditorStyles.RichLabel);

            var a = ResolveLoot(proj, _lootId, out string srcA);
            if (a == null) { GUILayout.Label("<color=#888>Pick a loot table to analyse.</color>", EditorStyles.RichLabel); return; }
            DrawLootStats(a, srcA);
            var b = ResolveLoot(proj, _lootCompare, out string srcB);
            if (b != null) { GUILayout.Space(8); DrawLootStats(b, srcB); }
        }

        private List<PoolItem> PoolFor(LootItemDef row, LootDef def) =>
            _pool.Where(p => (def.AllowDropOnlyItems || !p.DropOnly) && p.Rarity == row.LootRarity && (row.LootType == Enums.CardType.None || p.Type == row.LootType)).ToList();

        private void DrawLootStats(LootDef def, string source)
        {
            GUILayout.Label($"<b>{def.Id}</b>  <color=#888>({source})</color>", EditorStyles.RichLabel);
            float uncommon = def.PercentUncommon, rare = def.PercentRare, epic = def.PercentEpic, mythic = def.PercentMythic;
            float common = Mathf.Max(0f, 100f - uncommon - rare - epic - mythic);
            GUILayout.Label($"Items per drop: {def.NumItems}   Gold: {def.GoldQuantity}   Filler rarity: common {common:0}%, uncommon {uncommon:0}%, rare {rare:0}%, epic {epic:0}%, mythic {mythic:0}%");
            if (uncommon + rare + epic + mythic > 100f)
                GUILayout.Label("<color=#fc6>\u26A0 Filler rarity percents add up to more than 100; commons can never drop as filler.</color>", EditorStyles.RichLabel);

            bool poolReady = _phase == Phase.Done;
            float expectedRows = 0f;
            for (int i = 0; i < def.Items.Count; i++)
            {
                var row = def.Items[i];
                bool ignored = i >= def.NumItems;
                string kind;
                string flag = "";
                if (!string.IsNullOrEmpty(row.LootMisc)) kind = $"special '{row.LootMisc}'";
                else if (!string.IsNullOrEmpty(row.CardId))
                {
                    kind = $"card {row.CardId}";
                    var proj = ModManagerPanel.ActiveProject;
                    if (DataHelper.GetCard(row.CardId) == null && !(proj?.Cards.ContainsKey(row.CardId) ?? false)) flag = "card doesn't exist";
                }
                else
                {
                    kind = $"any {(row.LootType == Enums.CardType.None ? "item" : row.LootType + " item")}, {row.LootRarity}";
                    if (poolReady)
                    {
                        int n = PoolCount(row, def);
                        kind += $"  (pool {n})";
                        if (n == 0) flag = "empty pool, this row never drops";
                    }
                }
                if (ignored) flag = "ignored: only the first " + def.NumItems + " rows are rolled (all rows in Obelisk Challenge)";
                else expectedRows += Mathf.Clamp01(row.Percent / 100f);
                if (row.Percent <= 0f && !ignored) flag = (flag.Length > 0 ? flag + "; " : "") + "0% chance";
                GUILayout.Label($"  {i + 1}. {kind}  <b>{row.Percent:0.#}%</b>  <color=#fc6>{flag}</color>", EditorStyles.RichLabel);
            }
            if (def.Items.Count == 0) GUILayout.Label("  <color=#888>No table rows: every item is filler picked by the rarity percents.</color>", EditorStyles.RichLabel);
            GUILayout.Label($"Expected table items: {Math.Min(expectedRows, def.NumItems):0.00}; the remaining {Math.Max(0f, def.NumItems - expectedRows):0.00} are filler.");

            if (_lootSim && poolReady && _pool.Count > 0) DrawSim(def);
        }

        private int PoolCount(LootItemDef row, LootDef def)
        {
            string key = $"{row.LootType}|{row.LootRarity}|{def.AllowDropOnlyItems}";
            if (!_poolCounts.TryGetValue(key, out int n)) _poolCounts[key] = n = PoolFor(row, def).Count;
            return n;
        }

        private static string LootSignature(LootDef d) =>
            $"{d.Id}|{d.NumItems}|{d.AllowDropOnlyItems}|{d.PercentUncommon}|{d.PercentRare}|{d.PercentEpic}|{d.PercentMythic}|" +
            string.Join(";", d.Items.Select(i => $"{i.CardId},{i.Percent},{i.LootType},{i.LootRarity},{i.LootMisc}"));

        private void DrawSim(LootDef def)
        {
            string sig = LootSignature(def) + "|" + _pool.Count;
            if (!_simCache.TryGetValue(sig, out var lines))
            {
                if (_simCache.Count > 16) _simCache.Clear();
                _simCache[sig] = lines = Simulate(def);
            }
            foreach (var l in lines) GUILayout.Label(l, EditorStyles.RichLabel);
        }

        private List<string> Simulate(LootDef def)
        {
            var output = new List<string>();
            var rng = new System.Random(_lootSeed);
            var byRarity = new Dictionary<Enums.CardRarity, int>();
            var freq = new Dictionary<string, int>();
            int runs = 4000, tableHits = 0, short_ = 0;

            var fillPools = new Dictionary<Enums.CardRarity, List<PoolItem>>();
            foreach (Enums.CardRarity r in Enum.GetValues(typeof(Enums.CardRarity)))
                fillPools[r] = _pool.Where(p => !p.DropOnly && p.Rarity == r).ToList();

            for (int run = 0; run < runs; run++)
            {
                var got = new List<string>();
                for (int i = 0; i < def.Items.Count && i < def.NumItems && got.Count < def.NumItems; i++)
                {
                    var row = def.Items[i];
                    if (rng.Next(0, 100) >= row.Percent) continue;
                    tableHits++;
                    if (!string.IsNullOrEmpty(row.CardId)) { got.Add(row.CardId); Count(row.CardId, null); continue; }
                    if (!string.IsNullOrEmpty(row.LootMisc)) continue;
                    var pool = PoolFor(row, def).Where(p => !got.Contains(p.Id)).ToList();
                    if (pool.Count == 0) continue;
                    var pick = pool[rng.Next(pool.Count)];
                    got.Add(pick.Id); Count(pick.Id, pick.Rarity);
                }
                while (got.Count < def.NumItems)
                {
                    int roll = rng.Next(0, 100);
                    Enums.CardRarity want =
                        roll < def.PercentMythic ? Enums.CardRarity.Mythic :
                        roll < def.PercentMythic + def.PercentEpic ? Enums.CardRarity.Epic :
                        roll < def.PercentMythic + def.PercentEpic + def.PercentRare ? Enums.CardRarity.Rare :
                        roll < def.PercentMythic + def.PercentEpic + def.PercentRare + def.PercentUncommon ? Enums.CardRarity.Uncommon : Enums.CardRarity.Common;
                    var pool = fillPools[want].Where(p => !got.Contains(p.Id)).ToList();
                    if (pool.Count == 0) { short_++; break; }
                    var pick = pool[rng.Next(pool.Count)];
                    got.Add(pick.Id); Count(pick.Id, pick.Rarity);
                }
            }

            void Count(string id, Enums.CardRarity? rarity)
            {
                freq[id] = freq.TryGetValue(id, out int n) ? n + 1 : 1;
                if (rarity.HasValue) byRarity[rarity.Value] = byRarity.TryGetValue(rarity.Value, out int m) ? m + 1 : 1;
            }

            var sb = new StringBuilder("Simulated per drop:  ");
            foreach (var kv in byRarity.OrderBy(k => k.Key)) sb.Append($"{kv.Key} {kv.Value / (float)runs:0.00}   ");
            sb.Append($"table rows hit {tableHits / (float)runs:0.00}");
            output.Add(sb.ToString());
            if (short_ > 0) output.Add($"<color=#fc6>\u26A0 {short_ / (float)runs:P0} of drops couldn't fill every slot (a rarity had no items left).</color>");
            var top = freq.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key} {k.Value / (float)runs:P0}");
            output.Add("Most likely items: " + string.Join(", ", top));
            return output;
        }
    }

    internal static class DictExt
    {
        public static List<float> GetValueOrNull(this Dictionary<string, List<float>> d, string k) => d.TryGetValue(k, out var v) ? v : null;
        public static List<float> GetValueOrNull(this Dictionary<int, List<float>> d, int k) => d.TryGetValue(k, out var v) ? v : null;
    }
}
