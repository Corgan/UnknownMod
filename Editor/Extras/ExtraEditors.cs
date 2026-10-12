using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    // ═══════════════════════════════════════════════════════════════
    //  Editors for the "Extras" tab: KeyNote, Cinematic, CorruptionPack,
    //  ChallengeTrait, WeeklyChallenge
    // ═══════════════════════════════════════════════════════════════

    public class KeyNoteEditor : ModProjectEditorBase<KeyNoteDef>
    {
        protected override string TypeLabel => "KeyNote";
        protected override string FolderName => "keynotes";
        protected override string NewIdSuffix => "_keynote";

        protected override Dictionary<string, KeyNoteDef> GetNewDict(ModProject p) => p.KeyNotes;
        protected override Dictionary<string, KeyNoteDef> GetPatchDict(ModProject p) => p.KeyNotePatches;
        protected override KeyNoteDef CreateDefault(string id, ModProject proj) => new KeyNoteDef { Id = id, Name = id };
        protected override string GetDisplayName(KeyNoteDef d) => $"({d.Name})";

        protected override KeyNoteDef SnapshotBaseEntity(string id)
        {
            var kn = DataHelper.GetKeyNote(id);
            return kn != null ? DataHelper.SnapshotKeyNote(kn) : null;
        }

        public KeyNoteEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(KeyNoteDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);
            d.Name = EditorFields.TextField("Name", d.Name);
            GUILayout.Label("<size=10><color=#888>The ID must be the name in lowercase without spaces: that is how cards and traits look keynotes up.</color></size>", EditorStyles.RichLabel);
            d.Description = EditorFields.TextArea("Description", d.Description);
            d.DescriptionExtended = EditorFields.TextArea("Extended", d.DescriptionExtended);
        }
    }

    public class CinematicEditor : ModProjectEditorBase<CinematicDef>
    {
        protected override string TypeLabel => "Cinematic";
        protected override string FolderName => "cinematics";
        protected override string NewIdSuffix => "_cinematic";

        protected override Dictionary<string, CinematicDef> GetNewDict(ModProject p) => p.Cinematics;
        protected override Dictionary<string, CinematicDef> GetPatchDict(ModProject p) => p.CinematicPatches;
        protected override CinematicDef CreateDefault(string id, ModProject proj) => new CinematicDef { Id = id };
        protected override string GetDisplayName(CinematicDef d) => string.IsNullOrEmpty(d.PrefabName) ? "" : $"({d.PrefabName})";

        protected override CinematicDef SnapshotBaseEntity(string id)
        {
            var c = DataHelper.GetCinematic(id);
            return c != null ? DataHelper.SnapshotCinematic(c) : null;
        }

        public CinematicEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(CinematicDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);

            // Prefabs cannot be authored at runtime, so reuse the prefab of an existing cinematic.
            var prefabs = EditorFields.CachedIds("cinematic_prefabs", () =>
            {
                var set = new SortedSet<string>();
                foreach (var id in DataHelper.GetAllCinematicIds())
                {
                    var c = DataHelper.GetCinematic(id);
                    if (c != null) { var s = DataHelper.SnapshotCinematic(c).PrefabName; if (!string.IsNullOrEmpty(s)) set.Add(s); }
                }
                return new List<string>(set);
            });
            d.PrefabName = EditorFields.IdDropdown("Prefab", d.PrefabName, prefabs, "cin_prefab");
            d.SoundSource = EditorFields.IdDropdown("Soundtrack From", d.SoundSource, RefIds.Cinematics(proj), "cin_sound");
            d.EventId = EditorFields.IdDropdown("Then Event", d.EventId, RefIds.Events(proj), "cin_event", pickerMode: EntityPicker.Mode.Event);
            d.CombatId = EditorFields.IdDropdown("Then Combat", d.CombatId, RefIds.Combats(proj), "cin_combat", pickerMode: EntityPicker.Mode.Combat);
            d.EndAdventure = EditorFields.Toggle("Ends Adventure", d.EndAdventure);
        }
    }

    public class CorruptionPackEditor : ModProjectEditorBase<CorruptionPackDef>
    {
        protected override string TypeLabel => "CorruptionPack";
        protected override string FolderName => "corruptionpacks";
        protected override string NewIdSuffix => "_corruption";

        protected override Dictionary<string, CorruptionPackDef> GetNewDict(ModProject p) => p.CorruptionPacks;
        protected override Dictionary<string, CorruptionPackDef> GetPatchDict(ModProject p) => p.CorruptionPackPatches;
        protected override CorruptionPackDef CreateDefault(string id, ModProject proj) => new CorruptionPackDef { Id = id, PackName = id };
        protected override string GetDisplayName(CorruptionPackDef d) => $"({d.PackClass} T{d.PackTier})";

        protected override CorruptionPackDef SnapshotBaseEntity(string id)
        {
            var p = DataHelper.GetCorruptionPack(id);
            return p != null ? DataHelper.SnapshotCorruptionPack(p) : null;
        }

        public CorruptionPackEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(CorruptionPackDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);
            d.PackName = EditorFields.TextField("Name", d.PackName);
            d.PackClass = EditorFields.EnumField("Class", d.PackClass, "cp_class");
            d.PackTier = EditorFields.IntField("Town Tier", d.PackTier, 0, 10);
            var cards = RefIds.Cards(proj);
            GUILayout.Label("<b>Low pack</b>", EditorStyles.RichLabel);
            EditorFields.IdListField("Card", d.LowPackCardIds, cards, "cp_low", EntityPicker.Mode.Card);
            GUILayout.Label("<b>High pack</b>", EditorStyles.RichLabel);
            EditorFields.IdListField("Card", d.HighPackCardIds, cards, "cp_high", EntityPicker.Mode.Card);
        }
    }

    public class ChallengeTraitEditor : ModProjectEditorBase<ChallengeTraitDef>
    {
        protected override string TypeLabel => "ChallengeTrait";
        protected override string FolderName => "challengetraits";
        protected override string NewIdSuffix => "_challenge_trait";

        protected override Dictionary<string, ChallengeTraitDef> GetNewDict(ModProject p) => p.ChallengeTraits;
        protected override Dictionary<string, ChallengeTraitDef> GetPatchDict(ModProject p) => p.ChallengeTraitPatches;
        protected override ChallengeTraitDef CreateDefault(string id, ModProject proj) => new ChallengeTraitDef { Id = id, Name = id };
        protected override string GetDisplayName(ChallengeTraitDef d) => $"({d.Name})";

        protected override ChallengeTraitDef SnapshotBaseEntity(string id)
        {
            var t = DataHelper.GetChallengeTrait(id);
            return t != null ? DataHelper.SnapshotChallengeTrait(t) : null;
        }

        public ChallengeTraitEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(ChallengeTraitDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);
            d.Name = EditorFields.TextField("Name", d.Name);
            d.IconSource = EditorFields.IdDropdown("Icon From", d.IconSource, RefIds.ChallengeTraits(proj), "ct_icon");
            d.IsMadnessTrait = EditorFields.Toggle("Madness Trait", d.IsMadnessTrait);
            d.Order = EditorFields.IntField("Order", d.Order);
            d.IsSingularityTrait = EditorFields.Toggle("Singularity Trait", d.IsSingularityTrait);
            d.OrderSingularity = EditorFields.IntField("Singularity Order", d.OrderSingularity);

            DrawEffects(d, proj);
        }

        /// <summary>A trait has no effect of its own; rule sets attached to it supply the behaviour while it is active.</summary>
        private static void DrawEffects(ChallengeTraitDef d, ModProject proj)
        {
            if (proj == null || string.IsNullOrEmpty(d.Id)) return;
            EditorStyles.Separator();
            GUILayout.Label("<b>Effects</b>", EditorStyles.RichLabel);
            var sets = proj.RuleSets.Values.Where(s => string.Equals(s.ChallengeTraitId, d.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sets.Count == 0)
                GUILayout.Label("<size=10><color=#888>No rule set uses this trait yet, so it does nothing in a fight.</color></size>", EditorStyles.RichLabel);
            foreach (var s in sets)
                GUILayout.Label($"<size=10>\u2022 {s.Id}  <color=#888>({s.Rules.Count} rules, {s.AiRules.Count} AI)</color></size>", EditorStyles.RichLabel);
            if (GUILayout.Button("Create a rule set for this trait", EditorStyles.MiniButton, GUILayout.Width(200)))
            {
                string id = d.Id + "_rules";
                for (int n = 2; proj.RuleSets.ContainsKey(id); n++) id = d.Id + "_rules" + n;
                var set = new RuleSetDef { Id = id, ChallengeTraitId = d.Id, Description = "Effects of " + (string.IsNullOrEmpty(d.Name) ? d.Id : d.Name) };
                proj.RuleSets[id] = set;
                ModProjectLoader.SaveEntity(proj, "rulesets", id, set, false);
                proj.IsDirty = true;
                RuleEngine.Register(proj);
                EditorStatus.Toast($"Created rule set '{id}'. Edit it in the Rules tab.");
            }
        }
    }

    public class WeeklyChallengeEditor : ModProjectEditorBase<WeeklyChallengeDef>
    {
        protected override string TypeLabel => "WeeklyChallenge";
        protected override string FolderName => "weeklychallenges";
        protected override string NewIdSuffix => "_weekly";

        protected override Dictionary<string, WeeklyChallengeDef> GetNewDict(ModProject p) => p.WeeklyChallenges;
        protected override Dictionary<string, WeeklyChallengeDef> GetPatchDict(ModProject p) => p.WeeklyChallengePatches;
        protected override WeeklyChallengeDef CreateDefault(string id, ModProject proj) => new WeeklyChallengeDef { Id = id };
        protected override string GetDisplayName(WeeklyChallengeDef d) => $"(week {d.Week})";

        protected override WeeklyChallengeDef SnapshotBaseEntity(string id)
        {
            var w = DataHelper.GetWeekly(id);
            return w != null ? DataHelper.SnapshotWeekly(w) : null;
        }

        public WeeklyChallengeEditor(ModEditor parent) : base(parent) { }

        private bool _secSchedule = true, _secHeroes = true, _secBosses = true, _secModifiers = true;

        protected override void DrawAllSections(WeeklyChallengeDef d, ModProject proj)
        {
            if (EditorFields.Section("Schedule", ref _secSchedule))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.IdSteam = EditorFields.TextField("Steam ID", d.IdSteam);
                d.Week = EditorFields.IntField("Week", d.Week);
                d.Seed = EditorFields.TextField("Seed", d.Seed);
                d.FromDay = EditorFields.TextField("From Day (dd-mm-yyyy)", d.FromDay);
                d.FromHour = EditorFields.TextField("From Hour (hh:mm)", d.FromHour);
                d.ToDay = EditorFields.TextField("To Day (dd-mm-yyyy)", d.ToDay);
                d.ToHour = EditorFields.TextField("To Hour (hh:mm)", d.ToHour);
            }

            if (EditorFields.Section("Heroes", ref _secHeroes))
            {
                var heroes = RefIds.SubClasses(proj);
                d.Hero1Id = EditorFields.IdDropdown("Hero 1", d.Hero1Id, heroes, "wk_h1", pickerMode: EntityPicker.Mode.Hero);
                d.Hero2Id = EditorFields.IdDropdown("Hero 2", d.Hero2Id, heroes, "wk_h2", pickerMode: EntityPicker.Mode.Hero);
                d.Hero3Id = EditorFields.IdDropdown("Hero 3", d.Hero3Id, heroes, "wk_h3", pickerMode: EntityPicker.Mode.Hero);
                d.Hero4Id = EditorFields.IdDropdown("Hero 4", d.Hero4Id, heroes, "wk_h4", pickerMode: EntityPicker.Mode.Hero);
            }

            if (EditorFields.Section("Bosses & Loot", ref _secBosses))
            {
                var npcs = RefIds.Npcs(proj);
                d.Boss1Id = EditorFields.IdDropdown("Boss 1", d.Boss1Id, npcs, "wk_b1", pickerMode: EntityPicker.Mode.NPC);
                d.Boss2Id = EditorFields.IdDropdown("Boss 2", d.Boss2Id, npcs, "wk_b2", pickerMode: EntityPicker.Mode.NPC);
                d.BossCombatId = EditorFields.IdDropdown("Boss Combat", d.BossCombatId, RefIds.Combats(proj), "wk_bc", pickerMode: EntityPicker.Mode.Combat);
                d.LootId = EditorFields.IdDropdown("Loot", d.LootId, RefIds.Loot(proj), "wk_loot", pickerMode: EntityPicker.Mode.Loot);
            }

            if (EditorFields.Section("Modifiers", ref _secModifiers))
            {
                EditorFields.IdListField("Traits", d.TraitIds, RefIds.ChallengeTraits(proj), "wk_trait");
                EditorFields.IdListField("Corruption", d.CorruptionCardIds, RefIds.Cards(proj), "wk_corr", EntityPicker.Mode.Card);
            }
        }
    }
}
