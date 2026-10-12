using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>Item effect presets and the triad / discover / tutor card template.</summary>
    public partial class CardEditor
    {
        // ══════════════════════════════════════════════════════════
        //  ITEM PRESETS
        // ══════════════════════════════════════════════════════════

        private bool _secItemPresets;
        private string _ipName = "";
        private string _ipParam = "";
        private int _ipStrength = 2;
        private bool _ipReplace;
        private static List<string> _ipNames;

        private void DrawItemPresets(CardDef cd)
        {
            if (!EditorFields.Section("Item Presets", ref _secItemPresets)) return;

            _ipNames ??= ItemPresets.All.Select(p => p.Name).ToList();
            GUILayout.Label("<color=#888>Adds a common effect to this item. By default it is added next to what the item already does (the trigger and target are replaced). Strength 1 is small and 5 is large; the numbers are starting points, tune them afterwards. Ctrl+Z undoes it.</color>", EditorStyles.RichLabel);

            string picked = EditorFields.IdDropdown("Preset", _ipName, _ipNames, "ip_preset");
            if (picked != _ipName)
            {
                _ipName = picked;
                _ipParam = ItemPresets.Find(picked)?.DefaultParam ?? "";
            }

            var preset = ItemPresets.Find(_ipName);
            if (preset == null)
            {
                GUILayout.Label("<color=#666>Choose a preset above.</color>", EditorStyles.RichLabel);
                return;
            }

            GUILayout.Label($"<color=#aaa>{preset.Tip}</color>", EditorStyles.RichLabel);
            switch (preset.Param)
            {
                case PresetParam.DamageType:
                {
                    var t = Enum.TryParse<Enums.DamageType>(_ipParam, out var x) ? x : Enums.DamageType.All;
                    _ipParam = EditorFields.EnumField("Damage type", t, "ip_dmg").ToString();
                    break;
                }
                case PresetParam.Aura:
                    _ipParam = EditorFields.IdDropdown("Aura", _ipParam, EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds), "ip_aura", pickerMode: EntityPicker.Mode.AuraCurse);
                    break;
                case PresetParam.Curse:
                    _ipParam = EditorFields.IdDropdown("Curse", _ipParam, EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds), "ip_curse", pickerMode: EntityPicker.Mode.AuraCurse);
                    break;
                case PresetParam.Card:
                    _ipParam = EditorFields.IdDropdown("Card", _ipParam, EditorFields.CachedIds("card", DataHelper.GetAllCardIds), "ip_card", pickerMode: EntityPicker.Mode.Card);
                    break;
            }
            _ipStrength = EditorFields.IntField("Strength (1-5)", _ipStrength, 1, 5);
            _ipReplace = EditorFields.Toggle("Replace existing effects", _ipReplace);

            bool needsParam = preset.Param != PresetParam.None && preset.Param != PresetParam.DamageType && string.IsNullOrEmpty(_ipParam);
            bool was = GUI.enabled;
            GUI.enabled = was && !needsParam;
            if (GUILayout.Button($"Apply \"{preset.Name}\"", EditorStyles.MiniButton))
            {
                if (_ipReplace) cd.Item = ItemPresets.ClearEffects(cd.Item);
                var notes = new List<string>();
                preset.Apply(cd.Item, _ipStrength, _ipParam, notes);
                GUI.changed = true;
                EditorStatus.Toast(notes.Count > 0 ? notes[0] : $"Applied {preset.Name}", notes.Count > 0);
            }
            GUI.enabled = was;
            if (needsParam) GUILayout.Label("<color=#cc8844>Pick the aura, curse or card this preset uses.</color>", EditorStyles.RichLabel);
        }

        // ══════════════════════════════════════════════════════════
        //  TRIAD / DISCOVER / TUTOR TEMPLATE
        // ══════════════════════════════════════════════════════════

        private enum DiscoverMode { Triad, ChooseFromList, DiscoverByType, Tutor }

        private bool _secDiscover;
        private DiscoverMode _dcMode = DiscoverMode.Triad;
        private readonly List<string> _dcCards = new List<string> { "", "", "" };
        private int _dcAdd = 1;
        private int _dcOptions = 3;
        private int _dcCost = 1;
        private Enums.CardType _dcType = Enums.CardType.None;
        private Enums.CardFrom _dcFrom = Enums.CardFrom.Deck;
        private Enums.CardPlace _dcPlace = Enums.CardPlace.Hand;
        private bool _dcVanish;
        private Enums.CardClass _dcPoolClass = Enums.CardClass.Warrior;
        private static readonly System.Random _dcRng = new System.Random();

        private static string ModeLabel(DiscoverMode m) => m switch
        {
            DiscoverMode.Triad => "Triad: pick 1 of 3 named cards",
            DiscoverMode.ChooseFromList => "Choose from a list of cards",
            DiscoverMode.DiscoverByType => "Discover: choose from random cards of a type",
            _ => "Tutor: search deck, discard or vanish pile",
        };

        private void DrawDiscoverTemplate(CardDef cd)
        {
            if (!EditorFields.Section("Triad / Discover Template", ref _secDiscover)) return;

            GUILayout.Label("<color=#888>Sets up the Add Card fields for the usual \"choose a card\" patterns. Triad: the player picks 1 of 3 fixed cards (Shield Stance style). Discover: pick from random cards of a type (Spells style). Tutor: pick from your own piles. Ctrl+Z undoes it.</color>", EditorStyles.RichLabel);

            var modes = Enum.GetValues(typeof(DiscoverMode)).Cast<DiscoverMode>().ToList();
            string cur = EditorFields.IdDropdown("Pattern", ModeLabel(_dcMode), modes.Select(ModeLabel).ToList(), "dc_mode");
            var found = modes.FirstOrDefault(m => ModeLabel(m) == cur);
            _dcMode = found;

            bool listMode = _dcMode == DiscoverMode.Triad || _dcMode == DiscoverMode.ChooseFromList;
            if (_dcMode == DiscoverMode.Triad)
            {
                while (_dcCards.Count < 3) _dcCards.Add("");
                while (_dcCards.Count > 3) _dcCards.RemoveAt(_dcCards.Count - 1);
            }
            else if (_dcMode == DiscoverMode.ChooseFromList)
            {
                while (_dcCards.Count < 2) _dcCards.Add("");
            }

            if (listMode)
            {
                var ids = EditorFields.CachedIds("card", DataHelper.GetAllCardIds);
                for (int i = 0; i < _dcCards.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    _dcCards[i] = EditorFields.IdDropdown($"Card {i + 1}", _dcCards[i], ids, $"dc_card_{i}", pickerMode: EntityPicker.Mode.Card);
                    if (_dcMode == DiscoverMode.ChooseFromList)
                    {
                        var op = EditorFields.InlineRowOps(i, _dcCards.Count);
                        GUILayout.EndHorizontal();
                        if (op == EditorFields.RowOp.Remove && _dcCards.Count <= 2) op = EditorFields.RowOp.None;
                        if (EditorFields.ApplyRowOp(_dcCards, i, op)) break;
                    }
                    else GUILayout.EndHorizontal();
                }
                if (_dcMode == DiscoverMode.ChooseFromList && _dcCards.Count < 10 && GUILayout.Button("+ Card", EditorStyles.MiniButton, GUILayout.Width(70)))
                    _dcCards.Add("");

                GUILayout.BeginHorizontal();
                _dcPoolClass = EditorFields.EnumField("Random pool class", _dcPoolClass, "dc_poolcls");
                if (GUILayout.Button("Fill randomly", EditorStyles.MiniButton, GUILayout.Width(100))) FillRandom();
                GUILayout.EndHorizontal();
                _dcType = EditorFields.EnumField("Random pool type (optional)", _dcType, "dc_pooltype");
            }
            else
            {
                _dcType = EditorFields.EnumField(_dcMode == DiscoverMode.Tutor ? "Card type (None = any)" : "Card type", _dcType, "dc_type");
                _dcOptions = EditorFields.IntField("Cards shown", _dcOptions, 2, 12);
                if (_dcMode == DiscoverMode.Tutor)
                    _dcFrom = EditorFields.EnumField("Search in", _dcFrom, "dc_from");
            }

            if (_dcMode != DiscoverMode.Triad) _dcAdd = EditorFields.IntField("Cards added", _dcAdd, 1, 5);
            _dcPlace = EditorFields.EnumField("Put in", _dcPlace, "dc_place");
            _dcCost = EditorFields.IntField("Cost reduction (vanilla triads use 1)", _dcCost, -3, 5);
            _dcVanish = EditorFields.Toggle("Added cards vanish", _dcVanish);

            string problem = DiscoverProblem();
            if (problem != null) GUILayout.Label($"<color=#cc8844>{problem}</color>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            bool was = GUI.enabled;
            GUI.enabled = was && problem == null;
            if (GUILayout.Button("Apply to this card", EditorStyles.MiniButton))
            {
                ApplyDiscover(cd);
                GUI.changed = true;
                EditorStatus.Toast($"Applied {ModeLabel(_dcMode).Split(':')[0]} template to {cd.Id}");
            }
            GUI.enabled = was;
            if (GUILayout.Button("Load from this card", EditorStyles.MiniButton))
            {
                LoadDiscover(cd);
            }
            GUILayout.EndHorizontal();
        }

        private string DiscoverProblem()
        {
            if (_dcMode == DiscoverMode.Triad || _dcMode == DiscoverMode.ChooseFromList)
            {
                var picked = _dcCards.Where(c => !string.IsNullOrEmpty(c)).ToList();
                if (picked.Count != _dcCards.Count) return "Pick a card for every slot (or press Fill randomly).";
                if (picked.Distinct().Count() != picked.Count) return "The same card appears more than once.";
            }
            else if (_dcMode == DiscoverMode.DiscoverByType && _dcType == Enums.CardType.None)
                return "Pick a card type to discover from.";
            return null;
        }

        private void FillRandom()
        {
            var pool = new List<string>();
            try
            {
                var g = Globals.Instance;
                if (g?.CardListNotUpgradedByClass != null && g.CardListNotUpgradedByClass.TryGetValue(_dcPoolClass, out var list))
                    pool.AddRange(list);
                if (_dcType != Enums.CardType.None)
                    pool = pool.Where(id => { var c = g.GetCardData(id, false); return c != null && c.CardType == _dcType; }).ToList();
            }
            catch (Exception e)
            {
                EditorStatus.Toast("Could not read the card pool: " + e.Message, true);
                return;
            }
            if (pool.Count == 0) { EditorStatus.Toast("No cards match that class and type.", true); return; }

            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = _dcRng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            for (int i = 0; i < _dcCards.Count; i++)
                _dcCards[i] = i < pool.Count ? pool[i] : "";
            if (pool.Count < _dcCards.Count) EditorStatus.Toast($"Only {pool.Count} cards match; fill the rest by hand.", true);
        }

        private void ApplyDiscover(CardDef cd)
        {
            cd.AddCardId = "";
            cd.AddCardList = new List<string>();
            cd.AddCardType = Enums.CardType.None;
            cd.AddCardPlace = _dcPlace;
            cd.AddCardReducedCost = _dcCost;
            cd.AddCardVanish = _dcVanish;
            cd.AddCardCostTurn = false;

            switch (_dcMode)
            {
                case DiscoverMode.Triad:
                case DiscoverMode.ChooseFromList:
                    cd.AddCard = _dcMode == DiscoverMode.Triad ? 1 : _dcAdd;
                    cd.AddCardChoose = _dcCards.Count;
                    cd.AddCardFrom = Enums.CardFrom.Game;
                    cd.AddCardList = new List<string>(_dcCards);
                    cd.RelatedCard = _dcCards.Count > 0 ? _dcCards[0] : "";
                    cd.RelatedCard2 = _dcCards.Count > 1 ? _dcCards[1] : "";
                    cd.RelatedCard3 = _dcCards.Count > 2 ? _dcCards[2] : "";
                    break;
                case DiscoverMode.DiscoverByType:
                    cd.AddCard = _dcAdd;
                    cd.AddCardChoose = _dcOptions;
                    cd.AddCardFrom = Enums.CardFrom.Game;
                    cd.AddCardType = _dcType;
                    break;
                case DiscoverMode.Tutor:
                    cd.AddCard = _dcAdd;
                    cd.AddCardChoose = _dcOptions;
                    cd.AddCardFrom = _dcFrom;
                    cd.AddCardType = _dcType;
                    break;
            }
        }

        private void LoadDiscover(CardDef cd)
        {
            _dcPlace = cd.AddCardPlace;
            _dcCost = cd.AddCardReducedCost;
            _dcVanish = cd.AddCardVanish;
            _dcAdd = Mathf.Clamp(cd.AddCard, 1, 5);
            _dcOptions = Mathf.Clamp(cd.AddCardChoose, 2, 12);
            _dcType = cd.AddCardType;
            _dcFrom = cd.AddCardFrom;
            if (cd.AddCardList != null && cd.AddCardList.Count > 0)
            {
                _dcCards.Clear();
                _dcCards.AddRange(cd.AddCardList);
                _dcMode = _dcCards.Count == 3 && cd.AddCard == 1 ? DiscoverMode.Triad : DiscoverMode.ChooseFromList;
            }
            else _dcMode = cd.AddCardFrom == Enums.CardFrom.Game ? DiscoverMode.DiscoverByType : DiscoverMode.Tutor;
            EditorStatus.Toast("Loaded this card's Add Card settings into the template.");
        }
    }
}
