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
    /// <summary>Monte Carlo fight simulator: a hero team against a combat or hand-picked enemies, many runs, win rate and per-card / per-unit numbers.</summary>
    public sealed class CombatSimPanel
    {
        private enum Source { Combat, Enemies }
        private enum Upg { Base, A, B }

        private sealed class Slot { public string Hero = ""; public int Level = 1; public Upg Upgrade = Upg.Base; public string Extra = ""; }

        private readonly ModEditor _editor;
        private Source _source = Source.Combat;
        private string _combatId = "";
        private readonly string[] _npcs = { "", "", "", "" };
        private readonly Slot[] _slots = { new(), new(), new(), new() };
        private int _runs = 300, _seed = 1, _maxRounds = 30, _madness;
        private float _heroPower = 1f, _enemyHp = 1f, _enemyDmg = 1f;
        private bool _showAdvanced;

        private CombatSim _sim;
        private int _target;
        private string _error = "";
        private SimResult _pinned;
        private string _pinnedLabel = "";
        private string _label = "";
        private bool _showCards = true, _showWarnings;

        public CombatSimPanel(ModEditor editor) { _editor = editor; }

        public void Preset(string kind, string id)
        {
            if (kind == "combat") { _source = Source.Combat; _combatId = id; }
            else
            {
                _source = Source.Enemies;
                for (int i = 0; i < _npcs.Length; i++) _npcs[i] = i == 0 ? id : "";
            }
        }

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("Open a mod project first."); return; }
            Advance();

            GUILayout.Label("<color=#888>Plays the fight many times without starting the game: heroes pick the best card per energy, enemies follow their AI card lists. It is an estimate for comparing versions of a fight, not the game's combat engine. Effects it can't model are listed under the results.</color>", EditorStyles.RichLabel);

            _source = EditorFields.EnumField("Enemies from", _source, "sim_src");
            if (_source == Source.Combat)
                _combatId = EditorFields.IdDropdown("Combat", _combatId, RefIds.Combats(proj), "sim_combat", pickerMode: EntityPicker.Mode.Combat);
            else
                for (int i = 0; i < _npcs.Length; i++)
                    _npcs[i] = EditorFields.IdDropdown($"Enemy {i + 1}", _npcs[i], RefIds.Npcs(proj), "sim_npc" + i, pickerMode: EntityPicker.Mode.NPC);

            GUILayout.Space(4);
            GUILayout.Label("<b>Heroes</b>", EditorStyles.RichLabel);
            var heroIds = RefIds.SubClasses(proj);
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = _slots[i];
                s.Hero = EditorFields.IdDropdown($"Hero {i + 1}", s.Hero, heroIds, "sim_hero" + i);
                if (string.IsNullOrEmpty(s.Hero)) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Space(24);
                GUILayout.Label("Level", GUILayout.Width(40));
                s.Level = Mathf.RoundToInt(GUILayout.HorizontalSlider(s.Level, 1, 5, GUILayout.Width(80)));
                GUILayout.Label(s.Level.ToString(), GUILayout.Width(16));
                GUILayout.Label("Cards", GUILayout.Width(40));
                s.Upgrade = EditorFields.EnumField("", s.Upgrade, "sim_upg" + i);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Space(24);
                GUILayout.Label("Extra cards", GUILayout.Width(75));
                s.Extra = GUILayout.TextField(s.Extra);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("<color=#888>Extra cards: comma separated ids, \"id*2\" for two copies. Add your mod's new cards here to see how they change the fight.</color>", EditorStyles.RichLabel);

            _showAdvanced = GUILayout.Toggle(_showAdvanced, "Advanced settings");
            if (_showAdvanced)
            {
                _runs = EditorFields.IntField("Runs", _runs, 20, 3000);
                _seed = EditorFields.IntField("Seed", _seed, 0, 999999);
                _maxRounds = EditorFields.IntField("Round limit", _maxRounds, 5, 80);
                _madness = EditorFields.IntField("Madness level (enemy AI cards)", _madness, 0, 8);
                _heroPower = EditorFields.FloatField("Hero power (HP and damage ×)", _heroPower, 0.3f, 4f);
                _enemyHp = EditorFields.FloatField("Enemy HP ×", _enemyHp, 0.2f, 5f);
                _enemyDmg = EditorFields.FloatField("Enemy damage ×", _enemyDmg, 0.2f, 5f);
            }

            GUILayout.BeginHorizontal();
            bool running = _sim != null && _sim.Result.Runs < _target;
            GUI.enabled = !running;
            if (GUILayout.Button("\u25B6 Simulate", GUILayout.Width(120), GUILayout.Height(26))) Start(proj);
            GUI.enabled = true;
            if (running)
            {
                GUILayout.Label($"Running {_sim.Result.Runs}/{_target}");
                if (GUILayout.Button("Stop", EditorStyles.MiniButton, GUILayout.Width(50))) _target = _sim.Result.Runs;
            }
            else if (_sim != null && _sim.Result.Runs > 0)
            {
                if (GUILayout.Button("Pin result to compare", EditorStyles.MiniButton, GUILayout.Width(150))) { _pinned = _sim.Result; _pinnedLabel = _label; }
                if (_pinned != null && GUILayout.Button("Unpin", EditorStyles.MiniButton, GUILayout.Width(50))) _pinned = null;
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_error)) GUILayout.Label("<color=#f99>" + _error + "</color>", EditorStyles.RichLabel);

            if (_sim != null && _sim.Result.Runs > 0) DrawResult(_sim.Result);
        }

        private void Start(ModProject proj)
        {
            _error = "";
            var setup = new SimSetup { Madness = _madness, HeroPower = _heroPower, EnemyHp = _enemyHp, EnemyDamage = _enemyDmg, MaxRounds = _maxRounds, Seed = _seed };
            foreach (var s in _slots.Where(x => !string.IsNullOrEmpty(x.Hero)))
                setup.Heroes.Add(new SimHeroSlot { HeroId = s.Hero, Level = s.Level, Upgrade = (int)s.Upgrade, ExtraCards = s.Extra });

            if (_source == Source.Combat)
            {
                if (string.IsNullOrEmpty(_combatId)) { _error = "Pick a combat."; return; }
                if (proj.Combats.TryGetValue(_combatId, out var def) || proj.CombatPatches.TryGetValue(_combatId, out def))
                {
                    setup.Npcs.AddRange(def.NpcIds);
                    setup.StartEffects.AddRange(def.CombatEffects ?? new List<CombatEffectDef>());
                }
                if (setup.Npcs.Count == 0)
                {
                    var cd = DataHelper.GetExistingCombat(_combatId);
                    if (cd?.NPCList != null) setup.Npcs.AddRange(cd.NPCList.Where(n => n != null).Select(n => n.Id));
                }
                _label = _combatId;
            }
            else
            {
                setup.Npcs.AddRange(_npcs.Where(n => !string.IsNullOrEmpty(n)));
                _label = string.Join("+", setup.Npcs);
            }
            if (setup.Npcs.Count == 0) { _error = "No enemies to fight."; return; }

            var sim = new CombatSim(proj, setup);
            if (!sim.Prepare(out string err)) { _error = err; return; }
            _sim = sim;
            _target = _runs;
        }

        private void Advance()
        {
            if (_sim == null || _sim.Result.Runs >= _target) return;
            var sw = Stopwatch.StartNew();
            while (_sim.Result.Runs < _target && sw.ElapsedMilliseconds < 8) _sim.RunMany(1);
        }

        private static string Pct(double v) => (v * 100).ToString("0.#") + "%";

        private static string Delta(double now, double then, bool pct, bool higherIsGood)
        {
            double d = now - then;
            if (Math.Abs(d) < (pct ? 0.005 : 0.05)) return "";
            string col = (d > 0) == higherIsGood ? "#9f9" : "#f99";
            return $" <color={col}>({(d > 0 ? "+" : "")}{(pct ? (d * 100).ToString("0.#") + "%" : d.ToString("0.0"))})</color>";
        }

        private void DrawResult(SimResult r)
        {
            GUILayout.Space(8);
            var sb = new StringBuilder();
            string col = r.WinRate >= 0.9 ? "#9f9" : r.WinRate >= 0.5 ? "#fd6" : "#f99";
            bool cmp = _pinned != null && _pinned != r;
            sb.Append($"<b>{_label}</b>  {r.Runs} runs\n");
            sb.Append($"Win rate: <color={col}><b>{Pct(r.WinRate)}</b></color>");
            if (cmp) sb.Append(Delta(r.WinRate, _pinned.WinRate, true, true));
            sb.Append($"   Losses {r.Losses}   Timeouts {r.Timeouts} (over {_maxRounds} rounds)\n");
            sb.Append($"Rounds when won: {r.AvgWinRounds:0.0}");
            if (cmp) sb.Append(Delta(r.AvgWinRounds, _pinned.AvgWinRounds, false, false));
            sb.Append($"   Team HP left on win: {r.AvgHpLeftOnWin:0}%");
            if (cmp) sb.Append(Delta(r.AvgHpLeftOnWin / 100, _pinned.AvgHpLeftOnWin / 100, true, true));
            GUILayout.Label(sb.ToString(), EditorStyles.RichLabel);
            if (cmp) GUILayout.Label($"<color=#888>Compared with pinned: {_pinnedLabel} ({_pinned.Runs} runs, {Pct(_pinned.WinRate)} win)</color>", EditorStyles.RichLabel);

            // Round histogram
            int max = Math.Max(1, r.RoundHist.Max());
            var h = new StringBuilder("<b>Fight length</b> (rounds)\n");
            int last = Array.FindLastIndex(r.RoundHist, x => x > 0);
            for (int i = 1; i <= last; i++)
            {
                int n = r.RoundHist[i];
                if (n == 0 && i < last) { }
                h.Append($"{i,2} {new string('\u2588', (int)Math.Ceiling(24.0 * n / max))} {n}\n");
            }
            GUILayout.Label(h.ToString(), EditorStyles.RichLabel);

            GUILayout.Label("<b>Units</b>  (per fight averages)", EditorStyles.RichLabel);
            foreach (var u in r.Units)
            {
                string who = u.Hero ? "<color=#9cf>" + u.Name + "</color>" : "<color=#fc9>" + u.Name + "</color>";
                string dies = u.Deaths > 0 ? $", dies {100.0 * u.Deaths / r.Runs:0}% of fights (round {u.DeathRound / u.Deaths:0.0})" : "";
                GUILayout.Label($"{who}: deals {u.DamageDealt / r.Runs:0}, takes {u.DamageTaken / r.Runs:0}" + (u.Hero ? $", heals {u.Healed / r.Runs:0}" : "") + dies, EditorStyles.RichLabel);
            }

            _showCards = GUILayout.Toggle(_showCards, $"Cards played ({r.Cards.Count})");
            if (_showCards)
            {
                foreach (var c in r.Cards.Values.OrderByDescending(x => x.Damage + x.Heal).Take(25))
                    GUILayout.Label($"{c.Id}: played {c.Plays / r.Runs:0.0}×, damage {c.Damage / r.Runs:0.0}, heal {c.Heal / r.Runs:0.0} per fight");
            }

            int notes = r.Warnings.Count + r.UnmodelledAuras.Count + r.UnmodelledCards.Count;
            _showWarnings = GUILayout.Toggle(_showWarnings, $"Not modelled / warnings ({notes})");
            if (_showWarnings)
            {
                foreach (var w in r.Warnings) GUILayout.Label("<color=#fc6>" + w + "</color>", EditorStyles.RichLabel);
                foreach (var a in r.UnmodelledAuras) GUILayout.Label("Aura " + a);
                foreach (var c in r.UnmodelledCards) GUILayout.Label("Card " + c);
                GUILayout.Label("<color=#888>Also not modelled: card choosing, stealth targeting, hero items, traits and perks, enemy summons, resurrection and boss scripts.</color>", EditorStyles.RichLabel);
            }
        }
    }
}
