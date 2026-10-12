using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Runs a chosen combat through the game's own Practice ("combat tool") mode: the same hero selection, town and combat scenes, but no run, no map and no save.
    /// A set of seeds can be queued ("seed tester"); each finished fight records its result and the next seed is applied when the hero selection comes back.
    /// </summary>
    public static class TestFight
    {
        public sealed class Run
        {
            public string Label, Seed, Result = "Started";
            public int Rounds;
            public DateTime When = DateTime.Now;
        }

        public static readonly List<Run> History = new();
        public static readonly Queue<string> SeedQueue = new();

        internal static CombatData PendingCombat;
        internal static string PendingLabel = "";
        internal static string PendingSeed = "";
        internal static bool Active;
        private static Run _current;

        public static bool IsPending => Active && PendingCombat != null;
        public static string PendingName => PendingLabel;

        /// <summary>Null when a new test can be started from here, otherwise why not.</summary>
        public static string CanStart()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (Active && AtOManager.Instance != null && AtOManager.Instance.IsCombatTool && scene == "HeroSelection") return null;
            if (scene != "MainMenu") return "Start a test fight from the main menu (current scene: " + scene + ").";
            if (AtOManager.Instance == null || PlayerManager.Instance == null || CombatToolManager.Instance == null || SandboxManager.Instance == null)
                return "The main menu isn't fully loaded yet.";
            if (!PlayerManager.Instance.IsNodeUnlocked("town") && !(GameManager.Instance?.Cheats.SkipTutorial ?? false))
                return "The game's Practice mode needs the tutorial completed first.";
            return null;
        }

        public static string CurrentSceneHint()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (!Active) return scene == "MainMenu" ? "Ready: pick a fight and launch it." : "Go to the main menu to start a test fight.";
            switch (scene)
            {
                case "HeroSelection": return "Pick your heroes and press Begin; the fight starts from the town's Ready button.";
                case "Town": return "Press Ready in the town to start the test fight.";
                case "Combat": case "Match": return "Test fight running.";
                default: return "Test mode is on (" + scene + ").";
            }
        }

        /// <summary>Starts (or, when already in the practice hero selection, re-arms) a test fight.</summary>
        public static string Launch(CombatData combat, string label, string seed)
        {
            string why = CanStart();
            if (why != null) return why;
            if (combat == null) return "Nothing to launch: the combat could not be built.";

            PendingCombat = combat;
            PendingLabel = label;
            PendingSeed = (seed ?? "").Trim().ToUpperInvariant();
            if (PendingSeed.Length == 0 && SeedQueue.Count > 0) PendingSeed = SeedQueue.Dequeue();

            string scene = SceneManager.GetActiveScene().name;
            if (Active && scene == "HeroSelection")
            {
                Apply();
                return "Armed: " + label + (PendingSeed.Length > 0 ? " (seed " + PendingSeed + ")" : "");
            }

            Active = true;
            try
            {
                AtOManager.Instance.IsCombatTool = true;
                PlayerManager.Instance.UnlockAllExceptHeroes();
                PlayerManager.Instance.UnlockHeroes();
                CombatToolManager.Instance.LoadCombatToolConfig();
                SandboxManager.Instance.SbReset();
                SandboxManager.Instance.CacheValues();
                SandboxManager.Instance.SetCombatToolCombo();
                SceneStatic.LoadByName("HeroSelection");
            }
            catch (Exception ex)
            {
                Cancel();
                Plugin.Log.LogError($"[TestFight] launch failed: {ex}");
                return "Launch failed: " + ex.Message;
            }
            return "Opening practice hero selection...";
        }

        /// <summary>Called when the practice hero selection is ready; the game just cleared the chosen combat, so it is set again here.</summary>
        internal static void Apply()
        {
            if (!Active || PendingCombat == null) return;
            try
            {
                if (CombatToolManager.Instance != null) CombatToolManager.Instance.CurrentCombat = PendingCombat;
                if (PendingSeed.Length > 0)
                {
                    AtOManager.Instance.SetGameId(PendingSeed);
                    var hs = HeroSelectionManager.Instance;
                    if (hs != null && hs.gameSeedTxt != null) hs.gameSeedTxt.text = PendingSeed;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TestFight] apply failed: {ex.Message}"); }
        }

        internal static void OnCombatLaunch(CombatData launched)
        {
            if (!Active || PendingCombat == null) return;
            string label = launched != null && launched != PendingCombat ? launched.CombatId : PendingLabel;
            if (PendingSeed.Length > 0) UnityEngine.Random.InitState((PendingSeed + label).GetDeterministicHashCode());
            _current = new Run { Label = label, Seed = PendingSeed.Length > 0 ? PendingSeed : (AtOManager.Instance?.GetGameId() ?? "") };
            History.Add(_current);
            if (History.Count > 200) History.RemoveAt(0);
        }

        internal static void OnCombatFinish(bool resigned)
        {
            if (!Active || _current == null) return;
            try
            {
                var team = AtOManager.Instance.team;
                bool allDead = team != null && team.All(h => h?.HeroData == null || !h.Alive);
                _current.Result = resigned ? "Resigned" : allDead ? "Defeat" : "Victory";
                _current.Rounds = MatchManager.Instance != null ? MatchManager.Instance.CurrentRound : 0;
                Plugin.Log.LogInfo($"[TestFight] {_current.Label} seed {_current.Seed}: {_current.Result} in {_current.Rounds} round(s)");
            }
            catch (Exception ex) { _current.Result = "Unknown"; Plugin.Log.LogWarning($"[TestFight] result: {ex.Message}"); }
            _current = null;
            if (SeedQueue.Count > 0) PendingSeed = SeedQueue.Dequeue();
        }

        /// <summary>Test mode ends by itself when the game goes back to a normal main menu.</summary>
        public static void Poll()
        {
            if (Active && AtOManager.Instance != null && !AtOManager.Instance.IsCombatTool && SceneManager.GetActiveScene().name == "MainMenu")
                Cancel();
        }

        public static void Cancel()
        {
            Active = false;
            PendingCombat = null;
            PendingLabel = "";
            PendingSeed = "";
            _current = null;
            SeedQueue.Clear();
        }

        // ── Combat building ───────────────────────────────────

        public static CombatData BuildCombat(ModProject proj, string combatId, out string error)
        {
            error = null;
            if (proj != null && proj.Combats.TryGetValue(combatId, out var def))
                return Build(def, out error);
            var existing = DataHelper.GetExistingCombat(combatId);
            if (existing == null) error = $"Combat '{combatId}' wasn't found.";
            return existing;
        }

        public static CombatData Build(CombatDef def, out string error)
        {
            error = null;
            var npcs = new List<NPCData>();
            foreach (var id in def.NpcIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var npc = DataHelper.GetExistingNPC(id, false);
                if (npc == null) { error = $"Enemy '{id}' wasn't found."; return null; }
                npcs.Add(npc);
            }
            if (npcs.Count == 0) { error = "The fight has no enemies."; return null; }
            return DataHelper.MakeCombat(def, npcs.ToArray());
        }
    }
}
