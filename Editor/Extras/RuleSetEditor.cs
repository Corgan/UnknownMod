using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Editor for rule sets: combat behaviour as data (challenge trait effects, boss phases, extra summons,
    /// per-round rules, custom win and lose conditions, enemy AI limits). New-only: the base game has nothing to patch.
    /// </summary>
    public class RuleSetEditor : ModProjectEditorBase<RuleSetDef>
    {
        private static readonly Dictionary<string, RuleSetDef> NoPatches = new();
        private readonly HashSet<string> _closed = new();
        private bool _templatesOpen;
        private bool _liveOpen;

        // Template inputs
        private int _tRounds = 10;
        private int _tHp = 50;
        private int _tEvery = 3;
        private string _tNpc = "";
        private string _tAura = "fury";
        private int _tAuraCharges = 2;

        protected override string TypeLabel => "RuleSet";
        protected override string FolderName => "rulesets";
        protected override string NewIdSuffix => "_rules";

        protected override Dictionary<string, RuleSetDef> GetNewDict(ModProject p) => p.RuleSets;
        protected override Dictionary<string, RuleSetDef> GetPatchDict(ModProject p) => NoPatches;
        protected override RuleSetDef CreateDefault(string id, ModProject proj) => new RuleSetDef { Id = id };
        protected override string GetDisplayName(RuleSetDef d) => $"({d.Rules?.Count ?? 0} rules{((d.AiRules?.Count ?? 0) > 0 ? $", {d.AiRules.Count} AI" : "")})";
        protected override RuleSetDef SnapshotBaseEntity(string id) => null;

        public RuleSetEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(RuleSetDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);
            d.Description = EditorFields.TextField("Description", d.Description);
            d.Enabled = EditorFields.Toggle("Enabled", d.Enabled);

            GUILayout.Label("<b>Active when</b>", EditorStyles.RichLabel);
            GUILayout.Label("<size=10><color=#888>Every field you fill in must hold. A challenge trait switches the set on for runs that picked it; an enemy makes the set follow that enemy (it becomes the rule's Owner); a combat limits it to one fight.</color></size>", EditorStyles.RichLabel);
            d.ChallengeTraitId = EditorFields.IdDropdown("Challenge trait", d.ChallengeTraitId, RefIds.ChallengeTraits(proj), "rs_trait");
            d.NpcId = EditorFields.IdDropdown("Enemy (Owner)", d.NpcId, RefIds.Npcs(proj), "rs_npc", null, EntityPicker.Mode.NPC);
            d.CombatId = EditorFields.IdDropdown("Combat", d.CombatId, RefIds.Combats(proj), "rs_combat");
            d.AlwaysActive = EditorFields.Toggle("Always active", d.AlwaysActive);

            EditorStyles.Separator();
            GUILayout.Label("<b>Rules</b>", EditorStyles.RichLabel);
            d.Rules ??= new List<RuleDef>();
            for (int i = 0; i < d.Rules.Count; i++)
            {
                if (DrawRule(d, d.Rules[i], i, proj)) break;
            }
            if (GUILayout.Button("+ Rule", EditorStyles.MiniButton, GUILayout.Width(70)))
            {
                d.Rules.Add(new RuleDef { Actions = new List<RuleActionDef> { new RuleActionDef() } });
                GUI.changed = true;
            }

            EditorStyles.Separator();
            GUILayout.Label("<b>Enemy AI rules</b>", EditorStyles.RichLabel);
            GUILayout.Label("<size=10><color=#888>Limit when the set's enemy may play a card. They only ever narrow what the enemy's own AI decided; they cannot make it play a card it could not.</color></size>", EditorStyles.RichLabel);
            d.AiRules ??= new List<AiRuleDef>();
            for (int i = 0; i < d.AiRules.Count; i++)
            {
                if (DrawAiRule(d, d.AiRules[i], i, proj)) break;
            }
            if (GUILayout.Button("+ AI Rule", EditorStyles.MiniButton, GUILayout.Width(80)))
            {
                d.AiRules.Add(new AiRuleDef());
                GUI.changed = true;
            }

            EditorStyles.Separator();
            DrawTemplates(d, proj);
            DrawChecks(d, proj);
            DrawLive();
        }

        // ── Rules ──────────────────────────────────────────────────

        private static bool NeedsSubject(RuleTrigger t) =>
            t == RuleTrigger.Damaged || t == RuleTrigger.CharacterKilled || t == RuleTrigger.CardPlayed ||
            t == RuleTrigger.HpBelow || t == RuleTrigger.TurnStart || t == RuleTrigger.TurnEnd;

        private bool DrawRule(RuleSetDef set, RuleDef r, int i, ModProject proj)
        {
            string key = $"{set.Id}#{i}";
            string title = $"<color=#ffaa44>Rule {i + 1}</color>  {r.Trigger}{(string.IsNullOrEmpty(r.Name) ? "" : "  \u2014 " + r.Name)}{(r.Enabled ? "" : "  <color=#888>(off)</color>")}";
            var op = EditorFields.RowHeader(title, i, set.Rules.Count);
            if (EditorFields.ApplyRowOp(set.Rules, i, op)) return true;

            bool open = !_closed.Contains(key);
            if (GUILayout.Button(open ? "\u25BE hide" : "\u25B8 show", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                if (open) _closed.Add(key); else _closed.Remove(key);
            }
            if (!open) return false;

            r.Name = EditorFields.TextField("Name", r.Name);
            r.Enabled = EditorFields.Toggle("Enabled", r.Enabled);
            r.Trigger = EditorFields.EnumField("When", r.Trigger, $"rs_trig_{i}");
            if (NeedsSubject(r.Trigger))
            {
                r.Subject = EditorFields.EnumField("Whose", r.Subject, $"rs_subj_{i}");
                r.SubjectNpcId = EditorFields.IdDropdown("Only enemy", r.SubjectNpcId, RefIds.Npcs(proj), $"rs_snpc_{i}", null, EntityPicker.Mode.NPC);
            }
            if (r.Trigger == RuleTrigger.CardPlayed)
                r.CardId = EditorFields.IdDropdown("Only card", r.CardId, RefIds.Cards(proj), $"rs_card_{i}", null, EntityPicker.Mode.Card);
            if (r.Trigger == RuleTrigger.HpBelow)
                r.HpPercent = EditorFields.IntField("HP at or below %", r.HpPercent, 1, 99);
            r.FromRound = EditorFields.IntFieldMin("From round (0 = any)", r.FromRound, 0);
            r.ToRound = EditorFields.IntFieldMin("To round (0 = any)", r.ToRound, 0);
            r.EveryNRounds = EditorFields.IntFieldMin("Every N rounds (0 = every)", r.EveryNRounds, 0);
            r.Once = EditorFields.Toggle("Once per fight", r.Once);
            r.Chance = EditorFields.IntField("Chance %", r.Chance, 1, 100);

            GUILayout.Label("<color=#88ccff>Only if all of these hold</color>", EditorStyles.RichLabel);
            r.Conditions ??= new List<RuleConditionDef>();
            DrawConditions(r.Conditions, proj, $"rs{i}c");

            GUILayout.Label("<color=#88ff88>Then do</color>", EditorStyles.RichLabel);
            r.Actions ??= new List<RuleActionDef>();
            for (int j = 0; j < r.Actions.Count; j++)
                if (DrawAction(r, r.Actions[j], i, j, proj)) break;
            if (GUILayout.Button("+ Action", EditorStyles.MiniButton, GUILayout.Width(80)))
            {
                r.Actions.Add(new RuleActionDef());
                GUI.changed = true;
            }
            EditorStyles.Separator();
            return false;
        }

        private bool DrawAiRule(RuleSetDef set, AiRuleDef r, int i, ModProject proj)
        {
            var op = EditorFields.RowHeader($"<color=#ffaa44>AI rule {i + 1}</color>", i, set.AiRules.Count);
            if (EditorFields.ApplyRowOp(set.AiRules, i, op)) return true;
            r.CardId = EditorFields.IdDropdown("Card (empty = all)", r.CardId, RefIds.Cards(proj), $"rs_ai_card{i}", null, EntityPicker.Mode.Card);
            r.Mode = EditorFields.EnumField("Mode", r.Mode, $"rs_ai_mode{i}");
            GUILayout.Label(r.Mode == AiRuleMode.OnlyIf
                ? "<size=10><color=#888>The enemy may play this card only while all conditions hold.</color></size>"
                : "<size=10><color=#888>The enemy may not play this card while all conditions hold.</color></size>", EditorStyles.RichLabel);
            r.Conditions ??= new List<RuleConditionDef>();
            DrawConditions(r.Conditions, proj, $"rs_ai{i}c");
            EditorStyles.Separator();
            return false;
        }

        private void DrawConditions(List<RuleConditionDef> list, ModProject proj, string key)
        {
            for (int k = 0; k < list.Count; k++)
            {
                var c = list[k];
                var op = EditorFields.RowHeader($"<size=10>Condition {k + 1}</size>", k, list.Count, false);
                if (EditorFields.ApplyRowOp(list, k, op)) break;
                c.Kind = EditorFields.EnumField("Check", c.Kind, $"{key}{k}k");
                switch (c.Kind)
                {
                    case RuleCondKind.RoundAtLeast:
                    case RuleCondKind.RoundAtMost:
                    case RuleCondKind.RoundEvery:
                    case RuleCondKind.HeroesAliveAtLeast:
                    case RuleCondKind.HeroesAliveAtMost:
                    case RuleCondKind.EnemiesAliveAtLeast:
                    case RuleCondKind.EnemiesAliveAtMost:
                        c.Value = EditorFields.IntField("Value", c.Value);
                        break;
                    case RuleCondKind.HpBelowPercent:
                    case RuleCondKind.HpAbovePercent:
                        c.Of = EditorFields.EnumField("Of", c.Of, $"{key}{k}o");
                        c.Value = EditorFields.IntField("Percent", c.Value, 0, 100);
                        break;
                    case RuleCondKind.HasAura:
                    case RuleCondKind.LacksAura:
                        c.Of = EditorFields.EnumField("Of", c.Of, $"{key}{k}o");
                        c.Id = EditorFields.IdDropdown("Aura / curse", c.Id, RefIds.AuraCurses(proj), $"{key}{k}i", null, EntityPicker.Mode.AuraCurse);
                        break;
                    case RuleCondKind.NpcAlive:
                        c.Id = EditorFields.IdDropdown("Enemy", c.Id, RefIds.Npcs(proj), $"{key}{k}i", null, EntityPicker.Mode.NPC);
                        c.Value = EditorFields.IntFieldMin("At least", c.Value, 1);
                        break;
                    case RuleCondKind.NpcDead:
                        c.Id = EditorFields.IdDropdown("Enemy", c.Id, RefIds.Npcs(proj), $"{key}{k}i", null, EntityPicker.Mode.NPC);
                        break;
                    case RuleCondKind.FlagSet:
                    case RuleCondKind.FlagNotSet:
                        c.Id = EditorFields.TextField("Flag", c.Id);
                        break;
                }
            }
            if (GUILayout.Button("+ Condition", EditorStyles.MiniButton, GUILayout.Width(90)))
            {
                list.Add(new RuleConditionDef());
                GUI.changed = true;
            }
        }

        private bool DrawAction(RuleDef r, RuleActionDef a, int i, int j, ModProject proj)
        {
            var op = EditorFields.RowHeader($"<size=10>Action {j + 1}</size>", j, r.Actions.Count, false);
            if (EditorFields.ApplyRowOp(r.Actions, j, op)) return true;
            string key = $"rs{i}a{j}";
            a.Kind = EditorFields.EnumField("Do", a.Kind, key + "k");
            bool hasTarget = a.Kind != RuleActionKind.Win && a.Kind != RuleActionKind.Lose && a.Kind != RuleActionKind.Summon
                             && a.Kind != RuleActionKind.SetFlag && a.Kind != RuleActionKind.ClearFlag;
            if (hasTarget) a.Target = EditorFields.EnumField("To", a.Target, key + "t");
            switch (a.Kind)
            {
                case RuleActionKind.AddAura:
                    a.Id = EditorFields.IdDropdown("Aura / curse", a.Id, RefIds.AuraCurses(proj), key + "i", null, EntityPicker.Mode.AuraCurse);
                    a.Amount = EditorFields.IntFieldMin("Charges", a.Amount, 1);
                    break;
                case RuleActionKind.Damage:
                    a.DamageType = EditorFields.EnumField("Damage type", a.DamageType, key + "d");
                    a.Amount = EditorFields.IntFieldMin("Amount", a.Amount, 1);
                    a.Percent = EditorFields.Toggle("Percent of max HP", a.Percent);
                    break;
                case RuleActionKind.Heal:
                    a.Amount = EditorFields.IntFieldMin("Amount", a.Amount, 1);
                    a.Percent = EditorFields.Toggle("Percent of max HP", a.Percent);
                    break;
                case RuleActionKind.GainEnergy:
                    a.Amount = EditorFields.IntField("Energy", a.Amount);
                    break;
                case RuleActionKind.Summon:
                    a.Id = EditorFields.IdDropdown("Enemy", a.Id, RefIds.Npcs(proj), key + "i", null, EntityPicker.Mode.NPC);
                    a.Count = EditorFields.IntFieldMin("How many", a.Count, 1);
                    a.MaxAlive = EditorFields.IntFieldMin("Max alive (0 = no limit)", a.MaxAlive, 0);
                    GUILayout.Label("<size=10><color=#888>Nothing is summoned while the board has no free slot.</color></size>", EditorStyles.RichLabel);
                    break;
                case RuleActionKind.Message:
                    a.Text = EditorFields.TextField("Text", a.Text);
                    break;
                case RuleActionKind.Win:
                case RuleActionKind.Lose:
                    a.Text = EditorFields.TextField("Text (optional)", a.Text);
                    GUILayout.Label(a.Kind == RuleActionKind.Win
                        ? "<size=10><color=#888>Defeats every enemy the normal way, so loot and stats work as usual.</color></size>"
                        : "<size=10><color=#888>Defeats every hero the normal way.</color></size>", EditorStyles.RichLabel);
                    break;
                case RuleActionKind.SetFlag:
                case RuleActionKind.ClearFlag:
                    a.Id = EditorFields.TextField("Flag", a.Id);
                    GUILayout.Label("<size=10><color=#888>Flags last for one fight. Use them to build phases: set one in a rule, test it in another.</color></size>", EditorStyles.RichLabel);
                    break;
            }
            return false;
        }

        // ── Templates ──────────────────────────────────────────────

        private void DrawTemplates(RuleSetDef d, ModProject proj)
        {
            if (!EditorFields.Section("Quick templates", ref _templatesOpen)) return;
            GUILayout.Label("<size=10><color=#888>Each button adds a ready-made rule to this set. Adjust it afterwards.</color></size>", EditorStyles.RichLabel);
            _tRounds = EditorFields.IntFieldMin("Rounds", _tRounds, 1);
            _tHp = EditorFields.IntField("HP %", _tHp, 1, 99);
            _tEvery = EditorFields.IntFieldMin("Every N rounds", _tEvery, 2);
            _tNpc = EditorFields.IdDropdown("Enemy", _tNpc, RefIds.Npcs(proj), "rs_tpl_npc", null, EntityPicker.Mode.NPC);
            _tAura = EditorFields.IdDropdown("Aura / curse", _tAura, RefIds.AuraCurses(proj), "rs_tpl_aura", null, EntityPicker.Mode.AuraCurse);
            _tAuraCharges = EditorFields.IntFieldMin("Charges", _tAuraCharges, 1);

            if (Tpl("Win after surviving N rounds", "Win after N rounds"))
                d.Rules.Add(new RuleDef
                {
                    Name = $"Survive {_tRounds} rounds", Trigger = RuleTrigger.RoundStart, FromRound = _tRounds + 1, Once = true,
                    Actions = { new RuleActionDef { Kind = RuleActionKind.Message, Text = "Survived!" }, new RuleActionDef { Kind = RuleActionKind.Win } }
                });
            if (Tpl("Win when the chosen enemy dies", "Kill the enemy to win"))
                d.Rules.Add(new RuleDef
                {
                    Name = $"Win when {_tNpc} dies", Trigger = RuleTrigger.CharacterKilled, Subject = RuleSubject.Enemy, SubjectNpcId = _tNpc, Once = true,
                    Actions = { new RuleActionDef { Kind = RuleActionKind.Win } }
                });
            if (Tpl("Lose if any hero dies", "Hero death loses"))
                d.Rules.Add(new RuleDef
                {
                    Name = "A hero fell", Trigger = RuleTrigger.CharacterKilled, Subject = RuleSubject.Hero, Once = true,
                    Actions = { new RuleActionDef { Kind = RuleActionKind.Lose, Text = "A hero has fallen" } }
                });
            if (Tpl("Owner enrages at HP %", "Enrage at HP %"))
                d.Rules.Add(new RuleDef
                {
                    Name = $"Enrage at {_tHp}%", Trigger = RuleTrigger.HpBelow, Subject = RuleSubject.Owner, HpPercent = _tHp, Once = true,
                    Actions =
                    {
                        new RuleActionDef { Kind = RuleActionKind.Message, Target = RuleTarget.Owner, Text = "Enraged!" },
                        new RuleActionDef { Kind = RuleActionKind.AddAura, Target = RuleTarget.Owner, Id = _tAura, Amount = _tAuraCharges },
                        new RuleActionDef { Kind = RuleActionKind.SetFlag, Id = "enraged" }
                    }
                });
            if (Tpl("Summon the chosen enemy every N rounds", "Summon every N rounds"))
                d.Rules.Add(new RuleDef
                {
                    Name = $"Summon {_tNpc} every {_tEvery} rounds", Trigger = RuleTrigger.RoundStart, FromRound = _tEvery, EveryNRounds = _tEvery,
                    Actions = { new RuleActionDef { Kind = RuleActionKind.Summon, Id = _tNpc, Count = 1, MaxAlive = 2 } }
                });
            if (Tpl("Give every enemy an aura at combat start", "Enemies start buffed"))
                d.Rules.Add(new RuleDef
                {
                    Name = "Enemies start buffed", Trigger = RuleTrigger.CombatStart, Once = true,
                    Actions = { new RuleActionDef { Kind = RuleActionKind.AddAura, Target = RuleTarget.AllEnemies, Id = _tAura, Amount = _tAuraCharges } }
                });
            if (Tpl("Hurt every hero each round after round N", "Rising pressure"))
                d.Rules.Add(new RuleDef
                {
                    Name = $"Pressure from round {_tRounds}", Trigger = RuleTrigger.RoundStart, FromRound = _tRounds,
                    Actions = { new RuleActionDef { Kind = RuleActionKind.Damage, Target = RuleTarget.AllHeroes, Amount = 5, Percent = true, DamageType = Enums.DamageType.Mind } }
                });
            if (Tpl("Adds an AI rule: allowed only once the owner is hurt. It starts for ALL cards; pick the card in the rule afterwards", "AI: gate cards on HP"))
                d.AiRules.Add(new AiRuleDef
                {
                    Mode = AiRuleMode.OnlyIf,
                    Conditions = { new RuleConditionDef { Kind = RuleCondKind.HpBelowPercent, Of = RuleCharRef.Owner, Value = _tHp } }
                });
        }

        private static bool Tpl(string tip, string label)
        {
            if (!GUILayout.Button(new GUIContent(label, tip), EditorStyles.MiniButton)) return false;
            GUI.changed = true;
            return true;
        }

        // ── Checks and live view ───────────────────────────────────

        private void DrawChecks(RuleSetDef d, ModProject proj)
        {
            var issues = RuleEngine.Validate(d, proj);
            if (issues.Count == 0)
            {
                GUILayout.Label("<color=#88ff88>No problems found.</color>", EditorStyles.RichLabel);
                return;
            }
            GUILayout.Label($"<color=#ffcc44><b>{issues.Count} problem(s)</b></color>", EditorStyles.RichLabel);
            foreach (var s in issues)
                GUILayout.Label("<size=10><color=#ff9966>\u2022 " + s + "</color></size>", EditorStyles.RichLabel);
        }

        private void DrawLive()
        {
            if (!EditorFields.Section("Live (this fight)", ref _liveOpen)) return;
            GUILayout.Label($"<size=10><color=#888>{RuleEngine.RuleSetCount} rule set(s) loaded. Rules only run in single player.</color></size>", EditorStyles.RichLabel);
            var flags = RuleEngine.CurrentFlags.ToList();
            GUILayout.Label("<size=10>Flags: " + (flags.Count == 0 ? "<color=#888>none</color>" : string.Join(", ", flags)) + "</size>", EditorStyles.RichLabel);
            int from = Math.Max(0, RuleEngine.Recent.Count - 12);
            if (RuleEngine.Recent.Count == 0)
                GUILayout.Label("<size=10><color=#888>Nothing has fired yet.</color></size>", EditorStyles.RichLabel);
            for (int i = RuleEngine.Recent.Count - 1; i >= from; i--)
                GUILayout.Label("<size=10>" + RuleEngine.Recent[i] + "</size>", EditorStyles.RichLabel);
            if (GUILayout.Button("Clear log", EditorStyles.MiniButton, GUILayout.Width(80))) RuleEngine.Recent.Clear();
        }
    }
}
