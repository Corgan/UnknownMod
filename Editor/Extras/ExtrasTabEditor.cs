using System;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>
    /// Category tab for the remaining data families: KeyNotes, Cinematics, Corruption packs,
    /// Challenge traits and Weekly challenges.
    /// </summary>
    public class ExtrasTabEditor
    {
        private readonly ModEditor _editor;
        public enum SubTab { KeyNotes, Cinematics, CorruptionPacks, ChallengeTraits, Weekly, Thermometers, ThermometerTiers, BettingTeams, MemorySequences, CardPairRewards, BulkEdit, Usages, Clone, Balance, TestFight, Simulator, Reachability, DiffMerge, ModGraph, HotReload, Scripts, Rules, Economy }
        public SubTab ActiveSubTab { get; set; } = SubTab.KeyNotes;

        public KeyNoteEditor KeyNoteEdit { get; }
        public CinematicEditor CinematicEdit { get; }
        public CorruptionPackEditor CorruptionPackEdit { get; }
        public ChallengeTraitEditor ChallengeTraitEdit { get; }
        public WeeklyChallengeEditor WeeklyEdit { get; }
        public ThermometerEditor ThermometerEdit { get; }
        public ThermometerTierEditor ThermometerTierEdit { get; }
        public BettingTeamEditor BettingTeamEdit { get; }
        public MemorySequenceEditor MemorySequenceEdit { get; }
        public CardPairRewardEditor CardPairRewardEdit { get; }
        public BulkEditPanel BulkEdit { get; } = new BulkEditPanel();
        public UsageFinderPanel UsageFinder { get; }
        public CloneWizardPanel CloneWizard { get; }
        public BalancePanel Balance { get; }
        public TestFightPanel TestFight { get; }
        public CombatSimPanel Simulator { get; }
        public ReachabilityPanel Reachability { get; }
        public DiffMergePanel DiffMerge { get; }
        public ModGraphPanel ModGraph { get; }
        public HotReloadPanel HotReload { get; }
        public ScriptsPanel Scripts { get; }
        public RuleSetEditor RuleSetEdit { get; }
        public EconomyEditor EconomyEdit { get; }

        public ExtrasTabEditor(ModEditor editor)
        {
            _editor = editor;
            KeyNoteEdit = new KeyNoteEditor(editor);
            CinematicEdit = new CinematicEditor(editor);
            CorruptionPackEdit = new CorruptionPackEditor(editor);
            ChallengeTraitEdit = new ChallengeTraitEditor(editor);
            WeeklyEdit = new WeeklyChallengeEditor(editor);
            ThermometerEdit = new ThermometerEditor(editor);
            ThermometerTierEdit = new ThermometerTierEditor(editor);
            BettingTeamEdit = new BettingTeamEditor(editor);
            MemorySequenceEdit = new MemorySequenceEditor(editor);
            CardPairRewardEdit = new CardPairRewardEditor(editor);
            UsageFinder = new UsageFinderPanel(editor);
            CloneWizard = new CloneWizardPanel(editor);
            Balance = new BalancePanel(editor);
            TestFight = new TestFightPanel(editor);
            Simulator = new CombatSimPanel(editor);
            Reachability = new ReachabilityPanel(editor);
            DiffMerge = new DiffMergePanel(editor);
            ModGraph = new ModGraphPanel(editor);
            HotReload = new HotReloadPanel(editor);
            Scripts = new ScriptsPanel(editor);
            RuleSetEdit = new RuleSetEditor(editor);
            EconomyEdit = new EconomyEditor(editor);
        }

        public void Tick() { }

        public void DrawViewport(Rect rect)
        {
            var proj = ModManagerPanel.ActiveProject;
            switch (ActiveSubTab)
            {
                case SubTab.KeyNotes:
                    EntitySummaryView.DrawFrom(rect, "a key note", KeyNoteEdit.SelectedId, proj?.KeyNotes, proj?.KeyNotePatches);
                    break;
                case SubTab.Cinematics:
                    EntitySummaryView.DrawFrom(rect, "a cinematic", CinematicEdit.SelectedId, proj?.Cinematics, proj?.CinematicPatches);
                    break;
                case SubTab.CorruptionPacks:
                    EntitySummaryView.DrawFrom(rect, "a corruption pack", CorruptionPackEdit.SelectedId, proj?.CorruptionPacks, proj?.CorruptionPackPatches);
                    break;
                case SubTab.ChallengeTraits:
                    EntitySummaryView.DrawFrom(rect, "a challenge trait", ChallengeTraitEdit.SelectedId, proj?.ChallengeTraits, proj?.ChallengeTraitPatches);
                    break;
                case SubTab.Weekly:
                    EntitySummaryView.DrawFrom(rect, "a weekly challenge", WeeklyEdit.SelectedId, proj?.WeeklyChallenges, proj?.WeeklyChallengePatches);
                    break;
                case SubTab.Thermometers:
                    EntitySummaryView.DrawFrom(rect, "a thermometer", ThermometerEdit.SelectedId, proj?.Thermometers, null);
                    break;
                case SubTab.ThermometerTiers:
                    EntitySummaryView.DrawFrom(rect, "a thermometer tier", ThermometerTierEdit.SelectedId, proj?.ThermometerTiers, null);
                    break;
                case SubTab.BettingTeams:
                    EntitySummaryView.DrawFrom(rect, "a betting team", BettingTeamEdit.SelectedId, proj?.BettingTeams, null);
                    break;
                case SubTab.MemorySequences:
                    EntitySummaryView.DrawFrom(rect, "a memory sequence", MemorySequenceEdit.SelectedId, proj?.MemorySequences, null);
                    break;
                case SubTab.CardPairRewards:
                    EntitySummaryView.DrawFrom(rect, "a card-pair reward", CardPairRewardEdit.SelectedId, proj?.CardPairRewards, null);
                    break;
                case SubTab.Rules:
                    EntitySummaryView.DrawFrom(rect, "a rule set", RuleSetEdit.SelectedId, proj?.RuleSets, null);
                    break;
                case SubTab.Economy:
                    EntitySummaryView.DrawFrom(rect, "an economy set", EconomyEdit.SelectedId, proj?.Economies, null);
                    break;
            }
        }

        public void DrawPanel()
        {
            GUILayout.BeginHorizontal();
            SubTabButton("KeyNotes", SubTab.KeyNotes);
            SubTabButton("Cinematics", SubTab.Cinematics);
            SubTabButton("Corruption", SubTab.CorruptionPacks);
            SubTabButton("Challenge Traits", SubTab.ChallengeTraits);
            SubTabButton("Weekly", SubTab.Weekly);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            SubTabButton("Thermometers", SubTab.Thermometers);
            SubTabButton("Heat Tiers", SubTab.ThermometerTiers);
            SubTabButton("Betting", SubTab.BettingTeams);
            SubTabButton("Memory", SubTab.MemorySequences);
            SubTabButton("Card Pairs", SubTab.CardPairRewards);
            SubTabButton("Bulk Edit", SubTab.BulkEdit);
            SubTabButton("Find Usages", SubTab.Usages);
            SubTabButton("Clone", SubTab.Clone);
            SubTabButton("Balance", SubTab.Balance);
            SubTabButton("Test Fight", SubTab.TestFight);
            SubTabButton("Simulator", SubTab.Simulator);
            SubTabButton("Reachability", SubTab.Reachability);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            SubTabButton("Diff / Merge", SubTab.DiffMerge);
            SubTabButton("Mod Graph", SubTab.ModGraph);
            SubTabButton("Hot Reload", SubTab.HotReload);
            SubTabButton("Scripts", SubTab.Scripts);
            SubTabButton("Rules", SubTab.Rules);
            SubTabButton("Economy", SubTab.Economy);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            switch (ActiveSubTab)
            {
                case SubTab.KeyNotes: KeyNoteEdit.DrawPanel(); break;
                case SubTab.Cinematics: CinematicEdit.DrawPanel(); break;
                case SubTab.CorruptionPacks: CorruptionPackEdit.DrawPanel(); break;
                case SubTab.ChallengeTraits: ChallengeTraitEdit.DrawPanel(); break;
                case SubTab.Weekly: WeeklyEdit.DrawPanel(); break;
                case SubTab.Thermometers: ThermometerEdit.DrawPanel(); break;
                case SubTab.ThermometerTiers: ThermometerTierEdit.DrawPanel(); break;
                case SubTab.BettingTeams: BettingTeamEdit.DrawPanel(); break;
                case SubTab.MemorySequences: MemorySequenceEdit.DrawPanel(); break;
                case SubTab.CardPairRewards: CardPairRewardEdit.DrawPanel(); break;
                case SubTab.BulkEdit: BulkEdit.DrawPanel(); break;
                case SubTab.Usages: UsageFinder.DrawPanel(); break;
                case SubTab.Clone: CloneWizard.DrawPanel(); break;
                case SubTab.Balance: Balance.DrawPanel(); break;
                case SubTab.TestFight: TestFight.DrawPanel(); break;
                case SubTab.Simulator: Simulator.DrawPanel(); break;
                case SubTab.Reachability: Reachability.DrawPanel(); break;
                case SubTab.DiffMerge: DiffMerge.DrawPanel(); break;
                case SubTab.ModGraph: ModGraph.DrawPanel(); break;
                case SubTab.HotReload: HotReload.DrawPanel(); break;
                case SubTab.Scripts: Scripts.DrawPanel(); break;
                case SubTab.Rules: RuleSetEdit.DrawPanel(); break;
                case SubTab.Economy: EconomyEdit.DrawPanel(); break;
            }
        }

        /// <summary>Detect GUI.changed, save, and hot-reload the affected game object.</summary>
        public void HandleChanges()
        {
            if (!GUI.changed) return;

            var proj = ModManagerPanel.ActiveProject;
            switch (ActiveSubTab)
            {
                case SubTab.KeyNotes:
                    if (KeyNoteEdit.HandleChanges())
                        Reload(proj, KeyNoteEdit.SelectedId, proj?.KeyNotes, proj?.KeyNotePatches,
                            d => DataHelper.RegisterKeyNote(DataHelper.MakeKeyNote(d)));
                    break;
                case SubTab.Cinematics:
                    if (CinematicEdit.HandleChanges())
                        Reload(proj, CinematicEdit.SelectedId, proj?.Cinematics, proj?.CinematicPatches, d =>
                        {
                            DataHelper.RegisterCinematic(DataHelper.MakeCinematic(d));
                            DataHelper.WireCinematicRefs(d);
                        });
                    break;
                case SubTab.CorruptionPacks:
                    if (CorruptionPackEdit.HandleChanges())
                        Reload(proj, CorruptionPackEdit.SelectedId, proj?.CorruptionPacks, proj?.CorruptionPackPatches,
                            d => DataHelper.RegisterCorruptionPack(DataHelper.MakeCorruptionPack(d)));
                    break;
                case SubTab.ChallengeTraits:
                    if (ChallengeTraitEdit.HandleChanges())
                        Reload(proj, ChallengeTraitEdit.SelectedId, proj?.ChallengeTraits, proj?.ChallengeTraitPatches,
                            d => DataHelper.RegisterChallengeTrait(DataHelper.MakeChallengeTrait(d)));
                    break;
                case SubTab.Weekly:
                    if (WeeklyEdit.HandleChanges())
                        Reload(proj, WeeklyEdit.SelectedId, proj?.WeeklyChallenges, proj?.WeeklyChallengePatches,
                            d => DataHelper.RegisterWeekly(DataHelper.MakeWeekly(d)));
                    break;
                case SubTab.Thermometers:
                    if (ThermometerEdit.HandleChanges())
                        Reload(proj, ThermometerEdit.SelectedId, proj?.Thermometers, null,
                            d => DataHelper.RegisterThermometer(DataHelper.MakeThermometer(d)));
                    break;
                case SubTab.ThermometerTiers:
                    if (ThermometerTierEdit.HandleChanges())
                        Reload(proj, ThermometerTierEdit.SelectedId, proj?.ThermometerTiers, null,
                            d => DataHelper.RegisterThermometerTier(DataHelper.MakeThermometerTier(d)));
                    break;
                case SubTab.BettingTeams:
                    if (BettingTeamEdit.HandleChanges() && proj != null) DataHelper.RefreshMinigamePools(proj);
                    break;
                case SubTab.MemorySequences:
                    if (MemorySequenceEdit.HandleChanges() && proj != null) DataHelper.RefreshMinigamePools(proj);
                    break;
                case SubTab.CardPairRewards:
                    if (CardPairRewardEdit.HandleChanges() && proj != null) DataHelper.RefreshMinigamePools(proj);
                    break;
                case SubTab.Rules:
                    if (RuleSetEdit.HandleChanges() && proj != null) RuleEngine.Register(proj);
                    break;
                case SubTab.Economy:
                    if (EconomyEdit.HandleChanges() && proj != null) EconomyEngine.Register(proj);
                    break;
            }
        }

        private static void Reload<T>(ModProject proj, string id,
            System.Collections.Generic.Dictionary<string, T> news,
            System.Collections.Generic.Dictionary<string, T> patches, Action<T> apply) where T : class
        {
            if (proj == null || string.IsNullOrEmpty(id)) return;
            T def = null;
            if (news == null || !news.TryGetValue(id, out def)) patches?.TryGetValue(id, out def);
            if (def == null) return;
            try { apply(def); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[ExtrasTab] Hot-reload of '{id}' failed: {ex.Message}"); }
        }

        private void SubTabButton(string label, SubTab tab)
        {
            bool active = ActiveSubTab == tab;
            var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
            string text = active ? $"<b><color=cyan>{label}</color></b>" : label;
            if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                ActiveSubTab = tab;
        }
    }
}
