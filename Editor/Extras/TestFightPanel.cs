using System;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>Launch any combat (yours or the base game's) or a hand-picked set of enemies in the game's Practice mode, optionally with a queue of seeds.</summary>
    public sealed class TestFightPanel
    {
        private enum Source { Combat, Enemies }

        private readonly ModEditor _editor;
        private Source _source = Source.Combat;
        private string _combatId = "";
        private readonly string[] _npcs = { "", "", "", "" };
        private Enums.CombatTier _tier = Enums.CombatTier.T3;
        private Enums.CombatBackground _bg = Enums.CombatBackground.Spider_Lair;
        private bool _heal;
        private string _seed = "";
        private string _seedList = "";
        private string _status = "";

        public TestFightPanel(ModEditor editor) { _editor = editor; }

        public void Preset(string kind, string id)
        {
            if (kind == "combat") { _source = Source.Combat; _combatId = id; }
            else
            {
                _source = Source.Enemies;
                for (int i = 0; i < _npcs.Length; i++) _npcs[i] = i == 0 ? id : "";
            }
        }

        private static string RandomSeed()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var r = new System.Random();
            return new string(Enumerable.Range(0, 7).Select(_ => chars[r.Next(chars.Length)]).ToArray());
        }

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("Open a mod project first."); return; }
            TestFight.Poll();

            GUILayout.Label("<color=#888>Runs a fight in the game's Practice mode (hero selection \u2192 town \u2192 combat) without touching a run or a save. Starts from the main menu. Practice mode unlocks all content in your profile, the same as the game's own Practice button.</color>", EditorStyles.RichLabel);

            _source = EditorFields.EnumField("Fight from", _source, "tf_src");
            if (_source == Source.Combat)
            {
                _combatId = EditorFields.IdDropdown("Combat", _combatId, RefIds.Combats(proj), "tf_combat", pickerMode: EntityPicker.Mode.Combat);
            }
            else
            {
                for (int i = 0; i < _npcs.Length; i++)
                    _npcs[i] = EditorFields.IdDropdown($"Enemy {i + 1}", _npcs[i], RefIds.Npcs(proj), "tf_npc" + i, pickerMode: EntityPicker.Mode.NPC);
                _tier = EditorFields.EnumField("Combat tier", _tier, "tf_tier");
                _bg = EditorFields.EnumField("Background", _bg, "tf_bg");
                _heal = EditorFields.Toggle("Heal heroes after", _heal);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", GUILayout.Width(60));
            _seed = GUILayout.TextField(_seed, GUILayout.Width(120));
            if (GUILayout.Button("\uD83C\uDFB2", EditorStyles.MiniButton, GUILayout.Width(28))) _seed = RandomSeed();
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#888>The seed sets the game id (hero rolls, random picks) and reseeds Unity's random generator when the fight starts. Best effort: the same seed plus the same plays repeats a fight closely, not guaranteed.</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed list", GUILayout.Width(60));
            _seedList = GUILayout.TextField(_seedList);
            if (GUILayout.Button("Queue", EditorStyles.MiniButton, GUILayout.Width(50)))
            {
                foreach (var s in _seedList.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)) TestFight.SeedQueue.Enqueue(s.ToUpperInvariant());
                _seedList = "";
            }
            if (GUILayout.Button("+5 random", EditorStyles.MiniButton, GUILayout.Width(72)))
                for (int i = 0; i < 5; i++) TestFight.SeedQueue.Enqueue(RandomSeed());
            GUILayout.EndHorizontal();
            if (TestFight.SeedQueue.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Queued seeds ({TestFight.SeedQueue.Count}): " + string.Join(", ", TestFight.SeedQueue.Take(8)) + (TestFight.SeedQueue.Count > 8 ? " ..." : ""));
                if (GUILayout.Button("Clear", EditorStyles.MiniButton, GUILayout.Width(50))) TestFight.SeedQueue.Clear();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            string why = TestFight.CanStart();
            GUILayout.Label("<color=#9cf>" + TestFight.CurrentSceneHint() + "</color>", EditorStyles.RichLabel);
            if (why != null) GUILayout.Label("<color=#fc6>" + why + "</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            GUI.enabled = why == null;
            string verb = TestFight.Active ? "\u25B6 Arm this fight" : "\u25B6 Launch test fight";
            if (GUILayout.Button(verb, GUILayout.Height(26), GUILayout.Width(170))) _status = Launch(proj);
            GUI.enabled = true;
            if (TestFight.Active && GUILayout.Button("Forget pending", EditorStyles.MiniButton, GUILayout.Width(100))) { TestFight.Cancel(); _status = "Pending test cleared."; }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);
            if (TestFight.IsPending) GUILayout.Label($"Pending: <b>{TestFight.PendingName}</b>" + (TestFight.PendingSeed.Length > 0 ? $"  seed {TestFight.PendingSeed}" : ""), EditorStyles.RichLabel);

            DrawHistory();
        }

        private string Launch(ModProject proj)
        {
            string error;
            CombatData combat;
            string label;
            if (_source == Source.Combat)
            {
                if (string.IsNullOrEmpty(_combatId)) return "Pick a combat first.";
                combat = TestFight.BuildCombat(proj, _combatId, out error);
                label = _combatId;
            }
            else
            {
                var def = new CombatDef { CombatId = "unknownmod_testfight", Description = "Test fight", CombatTier = _tier, Background = _bg, HealHeroes = _heal };
                def.NpcIds.AddRange(_npcs.Where(n => !string.IsNullOrEmpty(n)));
                combat = TestFight.Build(def, out error);
                label = "enemies: " + string.Join("+", def.NpcIds);
            }
            if (combat == null) return error ?? "Couldn't build the fight.";
            return TestFight.Launch(combat, label, _seed);
        }

        private void DrawHistory()
        {
            if (TestFight.History.Count == 0) return;
            GUILayout.Space(8);
            GUILayout.Label("<b>Results</b>", EditorStyles.RichLabel);
            foreach (var g in TestFight.History.Where(r => r.Result != "Started").GroupBy(r => r.Label))
            {
                int win = g.Count(r => r.Result == "Victory"), n = g.Count();
                GUILayout.Label($"{g.Key}: {win}/{n} won ({100f * win / n:0}%), avg {g.Average(r => (float)r.Rounds):0.0} rounds");
            }
            GUILayout.Space(2);
            foreach (var r in Enumerable.Reverse(TestFight.History).Take(15))
            {
                string col = r.Result == "Victory" ? "#9f9" : r.Result == "Started" ? "#aaa" : "#f99";
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{r.When:HH:mm}  {r.Label}  seed {r.Seed}  <color={col}>{r.Result}</color>  {(r.Rounds > 0 ? r.Rounds + " rounds" : "")}", EditorStyles.RichLabel);
                if (GUILayout.Button("Use seed", EditorStyles.MiniButton, GUILayout.Width(70))) _seed = r.Seed;
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Clear results", EditorStyles.MiniButton, GUILayout.Width(100))) TestFight.History.Clear();
        }
    }
}
