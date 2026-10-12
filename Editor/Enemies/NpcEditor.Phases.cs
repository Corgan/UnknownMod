using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Boss phase editor. The game has no phase system: a "phase" is a group of AI cards added at the same round
    /// (AddCardRound), or an AI card that only fires below/above an HP threshold. This section shows those as a
    /// timeline and generates both patterns.
    /// </summary>
    public partial class NpcEditor
    {
        private enum PhaseGate
        {
            CasterHpAtOrBelow,
            CasterHpAtOrAbove,
            HeroHpAtOrBelow,
        }

        private bool _secPhases;
        private bool _phRampOpen, _phGateOpen, _phToolsOpen;
        private string _phRampCard = "", _phGateCard = "";
        private int _phRampFirst = 2, _phRampEvery = 2, _phRampCount = 3, _phRampUnits = 1, _phRampPriority = 5, _phRampMadness;
        private PhaseGate _phGateMode = PhaseGate.CasterHpAtOrBelow;
        private float _phGatePct = 50f;
        private int _phGatePriority = 1, _phGateRound, _phGateMadness;
        private int _phShiftFrom = 1, _phShiftBy = 1, _phCopyFrom = 0, _phCopyTo = 3;

        private readonly struct PhaseCardInfo
        {
            public readonly bool Found;
            public readonly string Name;
            public readonly int Cost;
            public readonly Enums.CardTargetSide Side;
            public PhaseCardInfo(bool found, string name, int cost, Enums.CardTargetSide side)
            { Found = found; Name = name; Cost = cost; Side = side; }
        }

        private static PhaseCardInfo LookupPhaseCard(ModProject proj, string id)
        {
            if (string.IsNullOrEmpty(id)) return new PhaseCardInfo(false, "(none)", 0, Enums.CardTargetSide.Enemy);
            CardDef cd = null;
            if (proj != null && (proj.Cards.TryGetValue(id, out cd) || proj.CardPatches.TryGetValue(id, out cd)) && cd != null)
                return new PhaseCardInfo(true, string.IsNullOrEmpty(cd.Name) ? id : cd.Name, cd.EnergyCost, cd.TargetSide);

            var g = Globals.Instance;
            var vd = g != null ? g.GetCardData(id, false) : null;
            if (vd != null)
                return new PhaseCardInfo(true, string.IsNullOrEmpty(vd.CardName) ? id : vd.CardName, vd.EnergyCost,
                    vd.TargetData != null ? vd.TargetData.TargetSide : Enums.CardTargetSide.Enemy);
            return new PhaseCardInfo(false, id, 0, Enums.CardTargetSide.Enemy);
        }

        private static string GateText(AiCardDef a)
        {
            var sb = new StringBuilder();
            if (a.OnlyCastIf != Enums.OnlyCastIf.Always)
                sb.Append(GateOne(a.OnlyCastIf, a.ValueCastIf, a.AuracurseCastIf));
            if (a.SecondOnlyCastIf != Enums.OnlyCastIf.Always)
            {
                if (sb.Length > 0) sb.Append(" & ");
                sb.Append(GateOne(a.SecondOnlyCastIf, a.SecondValueCastIf, a.AuracurseCastIf));
            }
            return sb.ToString();
        }

        private static string GateOne(Enums.OnlyCastIf c, float v, string ac)
        {
            switch (c)
            {
                case Enums.OnlyCastIf.TargetLifeLessThanPercent: return $"HP\u2264{v:0.#}%";
                case Enums.OnlyCastIf.TargetLifeHigherThanPercent: return $"HP\u2265{v:0.#}%";
                case Enums.OnlyCastIf.TargetHasAuraCurse: return $"has {ac}";
                case Enums.OnlyCastIf.TargetHasNotAuraCurse: return $"no {ac}";
                case Enums.OnlyCastIf.CasterHasMoreAuras: return $"self {ac}\u2265{v:0.#}";
                default: return c.ToString();
            }
        }

        private void DrawPhaseSection(NpcDef d, ModProject proj)
        {
            if (!EditorFields.Section($"Boss Phases ({PhaseRounds(d).Count})", ref _secPhases)) return;

            GUILayout.Label("<color=#888>The game has no phase system. A phase is a group of AI cards that join the deck at the same round, or a card that only fires while HP is above or below a threshold. This view groups your AI cards that way; the tools below create both patterns.</color>", EditorStyles.RichLabel);

            DrawPhaseTimeline(d, proj);
            DrawPhaseWarnings(d, proj);
            DrawPhaseRamp(d, proj);
            DrawPhaseGate(d, proj);
            DrawPhaseTools(d);
        }

        private static List<int> PhaseRounds(NpcDef d) =>
            d.AiCards.Select(a => a.AddCardRound).Distinct().OrderBy(r => r).ToList();

        private void DrawPhaseTimeline(NpcDef d, ModProject proj)
        {
            if (d.AiCards.Count == 0)
            {
                GUILayout.Label("<color=#aaa>No AI cards yet. Use the AI Cards section or the tools below.</color>", EditorStyles.RichLabel);
                return;
            }

            var groups = d.AiCards.Select((a, i) => (a, i)).GroupBy(x => x.a.AddCardRound).OrderBy(g => g.Key).ToList();
            int phaseNo = 0;
            foreach (var g in groups)
            {
                phaseNo++;
                int units = g.Sum(x => Math.Max(0, x.a.UnitsInDeck));
                string title = g.Key == 0
                    ? $"Phase {phaseNo}: opening deck (round 0)"
                    : $"Phase {phaseNo}: joins at round {g.Key}";
                GUILayout.Space(4);
                GUILayout.Label($"<b><color=#9cf>{title}</color></b>  <color=#888>{g.Count()} entries, {units} cards</color>", EditorStyles.RichLabel);

                foreach (var (a, i) in g)
                {
                    var info = LookupPhaseCard(proj, a.CardId);
                    string gate = GateText(a);
                    string name = info.Found ? info.Name : $"<color=#f88>{a.CardId} (missing)</color>";
                    string madness = a.StartsAtObeliskMadnessLevel > 0 ? $"  <color=#fc6>M{a.StartsAtObeliskMadnessLevel}+</color>" : "";
                    string line = $"{name} \u00D7{a.UnitsInDeck}  <color=#888>P{a.Priority}  cost {info.Cost}  {info.Side}</color>"
                        + (gate.Length > 0 ? $"  <color=#fa6>[{gate}]</color>" : "") + madness;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(line, EditorStyles.RichLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("\u25C0", "Join one round earlier"), EditorStyles.MiniButton, GUILayout.Width(24)) && a.AddCardRound > 0)
                    { a.AddCardRound--; GUI.changed = true; }
                    if (GUILayout.Button(new GUIContent("\u25B6", "Join one round later"), EditorStyles.MiniButton, GUILayout.Width(24)))
                    { a.AddCardRound++; GUI.changed = true; }
                    if (GUILayout.Button("Edit", EditorStyles.MiniButton, GUILayout.Width(40)))
                    { _expandedAiCard = i; _secAiCards = true; }
                    GUILayout.EndHorizontal();
                }
            }

            // Deck growth per round
            GUILayout.Space(6);
            GUILayout.Label("<color=#aaa>Deck by round (cards the NPC can draw from, before madness filters):</color>", EditorStyles.RichLabel);
            int maxRound = d.AiCards.Max(a => a.AddCardRound);
            int last = Math.Min(maxRound + 1, 15);
            var sb = new StringBuilder();
            for (int r = 1; r <= Math.Max(1, last); r++)
            {
                int total = d.AiCards.Where(a => a.AddCardRound <= r).Sum(a => Math.Max(0, a.UnitsInDeck));
                int added = d.AiCards.Where(a => a.AddCardRound == r && r > 0).Sum(a => Math.Max(0, a.UnitsInDeck));
                sb.Append($"R{r}: {total}");
                if (added > 0) sb.Append($" <color=#8d8>(+{added})</color>");
                sb.Append(r < last ? "   " : "");
            }
            GUILayout.Label(sb.ToString(), EditorStyles.RichLabel);
        }

        private void DrawPhaseWarnings(NpcDef d, ModProject proj)
        {
            var warns = new List<string>();
            int opening = d.AiCards.Where(a => a.AddCardRound == 0 && a.StartsAtObeliskMadnessLevel <= 0).Sum(a => Math.Max(0, a.UnitsInDeck));
            if (d.AiCards.Count > 0 && opening == 0)
                warns.Add("No cards in the opening deck (round 0): the NPC has nothing to play on round 1.");
            else if (d.AiCards.Count > 0 && opening < d.CardsInHand)
                warns.Add($"Opening deck has {opening} cards but the NPC draws {d.CardsInHand}. The discard pile is reshuffled, so it will repeat cards.");

            foreach (var a in d.AiCards)
            {
                var info = LookupPhaseCard(proj, a.CardId);
                if (!info.Found) { warns.Add($"AI card '{a.CardId}' does not exist."); continue; }
                if (info.Cost > d.Energy)
                    warns.Add($"'{info.Name}' costs {info.Cost} but the NPC has {d.Energy} energy.");
                bool hpGate = a.OnlyCastIf == Enums.OnlyCastIf.TargetLifeLessThanPercent || a.OnlyCastIf == Enums.OnlyCastIf.TargetLifeHigherThanPercent;
                if (hpGate && info.Side == Enums.CardTargetSide.Enemy)
                    warns.Add($"'{info.Name}' targets heroes, so its HP gate checks the hero's HP, not the boss's.");
                if (hpGate && a.ValueCastIf <= 0f && a.OnlyCastIf == Enums.OnlyCastIf.TargetLifeLessThanPercent)
                    warns.Add($"'{info.Name}' has an HP gate of 0%, so it can never fire.");
            }

            var dupes = d.AiCards.GroupBy(a => (a.CardId, a.AddCardRound, a.OnlyCastIf, a.ValueCastIf)).Where(g => g.Count() > 1).ToList();
            foreach (var g in dupes)
                warns.Add($"'{g.Key.CardId}' appears {g.Count()} times at round {g.Key.AddCardRound} with the same gate. Raise Units/Deck instead.");

            if (warns.Count == 0) return;
            GUILayout.Space(4);
            foreach (var w in warns.Take(8))
                GUILayout.Label("<color=#fc6>\u26A0 " + w + "</color>", EditorStyles.RichLabel);
            if (warns.Count > 8)
                GUILayout.Label($"<color=#888>...and {warns.Count - 8} more</color>", EditorStyles.RichLabel);
        }

        private void DrawPhaseRamp(NpcDef d, ModProject proj)
        {
            GUILayout.Space(6);
            if (!EditorFields.Section("Ramp a card over rounds", ref _phRampOpen)) return;

            GUILayout.Label("<color=#888>Adds copies of one card at regular round intervals, so the boss gets steadily more dangerous (an enrage ramp). Each step is a separate AI card entry.</color>", EditorStyles.RichLabel);
            var cardIds = EditorFields.BuildCardIdList(proj);
            _phRampCard = EditorFields.IdDropdown("Card", _phRampCard, cardIds, "ph_ramp_card", pickerMode: EntityPicker.Mode.Card);
            _phRampFirst = EditorFields.IntField("First round", _phRampFirst, 1, 99);
            _phRampEvery = EditorFields.IntField("Every N rounds", _phRampEvery, 1, 20);
            _phRampCount = EditorFields.IntField("Steps", _phRampCount, 1, 20);
            _phRampUnits = EditorFields.IntField("Copies per step", _phRampUnits, 1, 10);
            _phRampPriority = EditorFields.IntField("Priority", _phRampPriority, 0, 20);
            _phRampMadness = EditorFields.IntField("Min Obelisk Madness", _phRampMadness, 0, 20);

            if (!string.IsNullOrEmpty(_phRampCard))
            {
                var rounds = Enumerable.Range(0, _phRampCount).Select(k => "R" + (_phRampFirst + k * _phRampEvery));
                GUILayout.Label("<color=#9cf>Adds at: " + string.Join(", ", rounds) + "</color>", EditorStyles.RichLabel);
            }

            GUI.enabled = !string.IsNullOrEmpty(_phRampCard);
            if (GUILayout.Button("Add ramp", GUILayout.Width(100)))
            {
                int added = 0, merged = 0;
                for (int k = 0; k < _phRampCount; k++)
                {
                    int round = _phRampFirst + k * _phRampEvery;
                    var existing = d.AiCards.FirstOrDefault(a => a.CardId == _phRampCard && a.AddCardRound == round
                        && a.OnlyCastIf == Enums.OnlyCastIf.Always && a.StartsAtObeliskMadnessLevel == _phRampMadness);
                    if (existing != null) { existing.UnitsInDeck += _phRampUnits; merged++; continue; }
                    d.AiCards.Add(new AiCardDef
                    {
                        CardId = _phRampCard,
                        AddCardRound = round,
                        UnitsInDeck = _phRampUnits,
                        Priority = _phRampPriority,
                        StartsAtObeliskMadnessLevel = _phRampMadness,
                    });
                    added++;
                }
                GUI.changed = true;
                EditorStatus.Toast($"Ramp: {added} added" + (merged > 0 ? $", {merged} merged into existing entries" : "") + ". Ctrl+Z undoes it.");
            }
            GUI.enabled = true;
        }

        private void DrawPhaseGate(NpcDef d, ModProject proj)
        {
            GUILayout.Space(4);
            if (!EditorFields.Section("HP-gated card", ref _phGateOpen)) return;

            GUILayout.Label("<color=#888>Adds a card that only fires while HP is above or below a threshold. For the boss's own HP use a card that targets Self (the game only checks the HP of the card's target). A Friend card checks every ally. \"Hero HP\" uses a card that targets heroes, for a finisher.</color>", EditorStyles.RichLabel);
            var cardIds = EditorFields.BuildCardIdList(proj);
            _phGateCard = EditorFields.IdDropdown("Card", _phGateCard, cardIds, "ph_gate_card", pickerMode: EntityPicker.Mode.Card);
            _phGateMode = EditorFields.EnumField("Gate", _phGateMode, "ph_gate_mode");
            _phGatePct = EditorFields.FloatField("HP %", _phGatePct, 1f, 100f);
            _phGateRound = EditorFields.IntField("Join at round", _phGateRound, 0, 99);
            _phGatePriority = EditorFields.IntField("Priority", _phGatePriority, 0, 20);
            _phGateMadness = EditorFields.IntField("Min Obelisk Madness", _phGateMadness, 0, 20);

            string warn = null;
            if (!string.IsNullOrEmpty(_phGateCard))
            {
                var info = LookupPhaseCard(proj, _phGateCard);
                bool casterMode = _phGateMode != PhaseGate.HeroHpAtOrBelow;
                if (!info.Found) warn = "That card does not exist yet.";
                else if (casterMode && info.Side == Enums.CardTargetSide.Enemy)
                    warn = $"'{info.Name}' targets heroes, so this would test the hero's HP. Pick a card that targets Self, or use the Hero HP gate.";
                else if (!casterMode && info.Side != Enums.CardTargetSide.Enemy && info.Side != Enums.CardTargetSide.Anyone)
                    warn = $"'{info.Name}' does not target heroes, so the Hero HP gate would test the boss's HP.";
            }
            if (warn != null) GUILayout.Label("<color=#fc6>\u26A0 " + warn + "</color>", EditorStyles.RichLabel);

            GUI.enabled = !string.IsNullOrEmpty(_phGateCard);
            if (GUILayout.Button("Add gated card", GUILayout.Width(120)))
            {
                d.AiCards.Add(new AiCardDef
                {
                    CardId = _phGateCard,
                    AddCardRound = _phGateRound,
                    Priority = _phGatePriority,
                    UnitsInDeck = 1,
                    StartsAtObeliskMadnessLevel = _phGateMadness,
                    OnlyCastIf = _phGateMode == PhaseGate.CasterHpAtOrAbove
                        ? Enums.OnlyCastIf.TargetLifeHigherThanPercent
                        : Enums.OnlyCastIf.TargetLifeLessThanPercent,
                    ValueCastIf = _phGatePct,
                });
                GUI.changed = true;
                EditorStatus.Toast("Gated card added. Ctrl+Z undoes it.");
            }
            GUI.enabled = true;
        }

        private void DrawPhaseTools(NpcDef d)
        {
            GUILayout.Space(4);
            if (!EditorFields.Section("Shift / copy phases", ref _phToolsOpen)) return;

            GUILayout.Label("<color=#888>Shift moves every entry that joins at or after a round. Copy duplicates a phase's entries to another round.</color>", EditorStyles.RichLabel);

            _phShiftFrom = EditorFields.IntField("Shift from round", _phShiftFrom, 1, 99);
            _phShiftBy = EditorFields.IntField("Shift by", _phShiftBy, -20, 20);
            if (GUILayout.Button("Shift", GUILayout.Width(80)))
            {
                int moved = 0;
                foreach (var a in d.AiCards.Where(a => a.AddCardRound >= _phShiftFrom && a.AddCardRound > 0))
                {
                    int to = Math.Max(1, a.AddCardRound + _phShiftBy);
                    if (to != a.AddCardRound) { a.AddCardRound = to; moved++; }
                }
                if (moved > 0) GUI.changed = true;
                EditorStatus.Toast(moved > 0 ? $"Shifted {moved} entries." : "Nothing to shift.", moved == 0);
            }

            GUILayout.Space(4);
            _phCopyFrom = EditorFields.IntField("Copy from round", _phCopyFrom, 0, 99);
            _phCopyTo = EditorFields.IntField("Copy to round", _phCopyTo, 1, 99);
            if (GUILayout.Button("Copy phase", GUILayout.Width(100)))
            {
                var src = d.AiCards.Where(a => a.AddCardRound == _phCopyFrom).ToList();
                if (src.Count == 0 || _phCopyFrom == _phCopyTo)
                {
                    EditorStatus.Toast(src.Count == 0 ? "No entries at that round." : "Source and target are the same.", true);
                }
                else
                {
                    foreach (var a in src)
                    {
                        d.AiCards.Add(new AiCardDef
                        {
                            CardId = a.CardId, Priority = a.Priority, AddCardRound = _phCopyTo,
                            OnlyCastIf = a.OnlyCastIf, ValueCastIf = a.ValueCastIf, TargetCast = a.TargetCast,
                            UnitsInDeck = a.UnitsInDeck, PercentToCast = a.PercentToCast,
                            StartsAtObeliskMadnessLevel = a.StartsAtObeliskMadnessLevel,
                            StartsAtSingularityMadnessLevel = a.StartsAtSingularityMadnessLevel,
                            AuracurseCastIf = a.AuracurseCastIf, SecondOnlyCastIf = a.SecondOnlyCastIf,
                            SecondValueCastIf = a.SecondValueCastIf, SpecialSecondTargetId = a.SpecialSecondTargetId,
                        });
                    }
                    GUI.changed = true;
                    EditorStatus.Toast($"Copied {src.Count} entries to round {_phCopyTo}.");
                }
            }
        }
    }
}
