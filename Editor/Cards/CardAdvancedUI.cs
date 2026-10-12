using System;
using System.Collections.Generic;
using System.Linq;
using Cards;
using Cards.Data;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>Editor widgets for the card mechanics that the fixed effect slots do not cover.</summary>
    internal static class CardAdvancedUI
    {
        private static readonly string[] SlotOrder =
        {
            "Damage", "Damage2", "DamageSelf", "DamageSelf2", "Heal", "HealSelf",
            "Aura", "Aura2", "Aura3", "AuraSelf", "AuraSelf2", "AuraSelf3",
            "Curse", "Curse2", "Curse3", "CurseSelf", "CurseSelf2", "CurseSelf3"
        };

        private static IList<string> AcIds => EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
        private static IList<string> CardIds => EditorFields.CachedIds("card", DataHelper.GetAllCardIds);
        private static IList<string> NpcIds => EditorFields.CachedIds("npc", DataHelper.GetAllNpcIds);

        private static bool SlotInUse(CardDef c, string slot) => slot switch
        {
            "Damage" => c.Damage != 0 || c.DamageType != Enums.DamageType.None,
            "Damage2" => c.Damage2 != 0 || c.DamageType2 != Enums.DamageType.None,
            "DamageSelf" => c.DamageSelf != 0,
            "DamageSelf2" => c.DamageSelf2 != 0,
            "Heal" => c.Heal != 0,
            "HealSelf" => c.HealSelf != 0,
            "Aura" => !string.IsNullOrEmpty(c.Aura),
            "Aura2" => !string.IsNullOrEmpty(c.Aura2),
            "Aura3" => !string.IsNullOrEmpty(c.Aura3),
            "AuraSelf" => !string.IsNullOrEmpty(c.AuraSelf),
            "AuraSelf2" => !string.IsNullOrEmpty(c.AuraSelf2),
            "AuraSelf3" => !string.IsNullOrEmpty(c.AuraSelf3),
            "Curse" => !string.IsNullOrEmpty(c.Curse),
            "Curse2" => !string.IsNullOrEmpty(c.Curse2),
            "Curse3" => !string.IsNullOrEmpty(c.Curse3),
            "CurseSelf" => !string.IsNullOrEmpty(c.CurseSelf),
            "CurseSelf2" => !string.IsNullOrEmpty(c.CurseSelf2),
            "CurseSelf3" => !string.IsNullOrEmpty(c.CurseSelf3),
            _ => false
        };

        // ── Special values ──────────────────────────────────────────

        public static CardSpecialValueDef SpecialValue(string label, CardSpecialValueDef sv, string key)
        {
            sv ??= new CardSpecialValueDef();
            sv.Type = EditorFields.EnumField(label, sv.Type, key + "_t");
            if (sv.Type == Enums.CardSpecialValueType.None) return sv;

            sv.Modifier = EditorFields.FloatField("  Modifier", sv.Modifier);
            sv.Operation = EditorFields.EnumField("  Operation", sv.Operation, key + "_op");
            sv.IsGlobal = EditorFields.Toggle("  Global", sv.IsGlobal);
            if (sv.Type == Enums.CardSpecialValueType.AuraCurseYours || sv.Type == Enums.CardSpecialValueType.AuraCurseTarget)
                sv.AuraCurse = EditorFields.IdDropdown("  Aura/Curse", sv.AuraCurse, AcIds, key + "_ac", pickerMode: EntityPicker.Mode.AuraCurse);
            return sv;
        }

        private static void ExtrasBody(CardEffectExtrasDef x, string key, bool heal, bool curse)
        {
            x.SpecialValue = SpecialValue("  Special Value", x.SpecialValue, key + "_sv");
            x.Condition = EditorFields.EnumField("  Condition", x.Condition, key + "_cond");
            x.RequireStanza = EditorFields.IntFieldMin("  Require Stanza", x.RequireStanza, 0);
            x.RequireStanzaLead = EditorFields.IntFieldMin("  Require Stanza Lead", x.RequireStanzaLead, 0);
            if (heal) x.HealType = EditorFields.EnumField("  Heal Type", x.HealType, key + "_ht");
            if (curse) x.CurseApplyMode = EditorFields.EnumField("  Curse Mode", x.CurseApplyMode, key + "_cm");
        }

        /// <summary>One collapsible block per effect slot in use: special value, condition, stanza requirements.</summary>
        public static void EffectModifiers(CardDef cd, HashSet<string> open)
        {
            cd.EffectExtras ??= new Dictionary<string, CardEffectExtrasDef>();
            bool any = false;
            foreach (var slot in SlotOrder)
            {
                cd.EffectExtras.TryGetValue(slot, out var x);
                if (!SlotInUse(cd, slot) && (x == null || x.IsDefault)) continue;
                any = true;
                bool expanded = open.Contains(slot);
                string tag = x != null && !x.IsDefault ? "  <color=#44cc88>(modified)</color>" : "";
                if (GUILayout.Button($"{(expanded ? "\u25BC" : "\u25B6")} {slot}{tag}", EditorStyles.MiniButton))
                {
                    if (expanded) open.Remove(slot); else open.Add(slot);
                }
                if (!expanded) continue;

                bool created = x == null;
                x ??= new CardEffectExtrasDef();
                ExtrasBody(x, "card_fx_" + slot, slot.StartsWith("Heal", StringComparison.Ordinal), slot.StartsWith("Curse", StringComparison.Ordinal));
                if (x.IsDefault) cd.EffectExtras.Remove(slot);
                else cd.EffectExtras[slot] = x;
                if (created && !x.IsDefault) GUI.changed = true;
            }
            if (!any)
                GUILayout.Label("<color=#666>No damage, heal, aura or curse slots are in use yet.</color>", EditorStyles.RichLabel);
        }

        // ── Extra effects ───────────────────────────────────────────

        private static readonly string[] Kinds = { "Damage", "Heal", "Aura", "Curse" };

        public static void ExtraEffects(CardDef cd)
        {
            cd.ExtraEffects ??= new List<CardExtraEffectDef>();
            GUILayout.Label("<color=#888>Effects the fixed slots cannot express: side-targeted damage, team heals, 4th+ auras, etc.</color>", EditorStyles.RichLabel);
            for (int i = 0; i < cd.ExtraEffects.Count; i++)
            {
                var x = cd.ExtraEffects[i];
                string key = "card_xe" + i;
                if (EditorFields.ApplyRowOp(cd.ExtraEffects, i, EditorFields.RowHeader($"<color=#44cc88>Extra {i + 1}</color>", i, cd.ExtraEffects.Count))) break;

                int kind = Math.Max(0, Array.FindIndex(Kinds, k => string.Equals(k, x.Kind, StringComparison.OrdinalIgnoreCase)));
                x.Kind = EditorFields.IdDropdown("Kind", Kinds[kind], Kinds, key + "_kind");
                if (string.IsNullOrEmpty(x.Kind)) x.Kind = "Damage";

                switch (x.Kind)
                {
                    case "Damage":
                        x.Amount = EditorFields.IntField("Amount", x.Amount);
                        x.DamageType = EditorFields.EnumField("Damage Type", x.DamageType, key + "_dt");
                        x.DamageTarget = EditorFields.EnumField("Target", x.DamageTarget, key + "_dtg");
                        x.IgnoreBlock = EditorFields.Toggle("Ignore Block", x.IgnoreBlock);
                        break;
                    case "Heal":
                        x.Amount = EditorFields.IntField("Amount", x.Amount);
                        x.HealTarget = EditorFields.EnumField("Target", x.HealTarget, key + "_htg");
                        break;
                    case "Aura":
                        x.AuraCurse = EditorFields.IdDropdown("Aura", x.AuraCurse, AcIds, key + "_ac", pickerMode: EntityPicker.Mode.AuraCurse);
                        x.Amount = EditorFields.IntField("Charges", x.Amount);
                        x.AuraTarget = EditorFields.EnumField("Target", x.AuraTarget, key + "_atg");
                        break;
                    case "Curse":
                        x.AuraCurse = EditorFields.IdDropdown("Curse", x.AuraCurse, AcIds, key + "_ac", pickerMode: EntityPicker.Mode.AuraCurse);
                        x.Amount = EditorFields.IntField("Charges", x.Amount);
                        x.CurseTarget = EditorFields.EnumField("Target", x.CurseTarget, key + "_ctg");
                        break;
                }
                ExtrasBody(x, key, x.Kind == "Heal", x.Kind == "Curse");
                GUILayout.Space(6);
            }
            if (GUILayout.Button("+ Extra Effect", EditorStyles.MiniButton, GUILayout.Width(110)))
            {
                cd.ExtraEffects.Add(new CardExtraEffectDef());
                GUI.changed = true;
            }
        }

        // ── Energy recharge ─────────────────────────────────────────

        public static void EnergyRecharges(CardDef cd)
        {
            cd.EnergyRecharges ??= new List<CardEnergyRechargeDef>();
            for (int i = 0; i < cd.EnergyRecharges.Count; i++)
            {
                var r = cd.EnergyRecharges[i];
                string key = "card_er" + i;
                if (EditorFields.ApplyRowOp(cd.EnergyRecharges, i, EditorFields.RowHeader($"<color=#44cc88>Energy Recharge {i + 1}</color>", i, cd.EnergyRecharges.Count))) break;
                r.Amount = EditorFields.IntField("Amount", r.Amount);
                r.Target = EditorFields.EnumField("Target", r.Target, key + "_tg");
                r.TargetTeam = EditorFields.EnumField("Target Team", r.TargetTeam, key + "_tt");
                r.SpecialValue = SpecialValue("Special Value", r.SpecialValue, key + "_sv");
            }
            if (GUILayout.Button("+ Energy Recharge", EditorStyles.MiniButton, GUILayout.Width(130)))
            {
                cd.EnergyRecharges.Add(new CardEnergyRechargeDef());
                GUI.changed = true;
            }
        }

        // ── Echo / copy / parody / stanza ───────────────────────────

        public static void AdvancedMechanics(CardDef cd)
        {
            cd.GrantStanza = EditorFields.IntFieldMin("Grant Stanza", cd.GrantStanza, 0);
            cd.IgnoreStealth = EditorFields.Toggle("Ignore Stealth", cd.IgnoreStealth);
            cd.UseRandomAura = EditorFields.Toggle("Random Aura", cd.UseRandomAura);
            cd.CopyAurasAmount = EditorFields.IntFieldMin("Copy Auras Amount", cd.CopyAurasAmount, 0);
            cd.CardSpecialValue = SpecialValue("Card Special Value", cd.CardSpecialValue, "card_csv");
            cd.SelfHealthLossSpecialValue = SpecialValue("Self Health Loss SV", cd.SelfHealthLossSpecialValue, "card_shlsv");
            cd.DrawCardSpecialValue = SpecialValue("Draw Cards SV", cd.DrawCardSpecialValue, "card_dcsv");

            GUILayout.Space(4);
            GUILayout.Label("<color=#aaa>Echo:</color>", EditorStyles.RichLabel);
            cd.EchoCard = EditorFields.Toggle("Echo Card", cd.EchoCard);
            cd.EchoCardResult = EditorFields.Toggle("Echo Result", cd.EchoCardResult);
            cd.EchoCardResurge = EditorFields.Toggle("Echo Resurge", cd.EchoCardResurge);

            GUILayout.Space(4);
            GUILayout.Label("<color=#aaa>Parody:</color>", EditorStyles.RichLabel);
            cd.ParodyActivation = EditorFields.Toggle("Activation", cd.ParodyActivation);
            if (cd.ParodyActivation)
            {
                cd.ParodyCopyAuraCurse = EditorFields.Toggle("Copy Aura/Curse", cd.ParodyCopyAuraCurse);
                cd.ParodyPurgeDispelAuraCurseSelf = EditorFields.Toggle("Purge/Dispel Self", cd.ParodyPurgeDispelAuraCurseSelf);
                cd.ParodyCopyingTargetCardsAmount = EditorFields.IntFieldMin("Target Cards Amount", cd.ParodyCopyingTargetCardsAmount, 0);
                cd.ParodySkipModel = EditorFields.Toggle("Skip Model", cd.ParodySkipModel);
                cd.ParodyCopyAurasOnly = EditorFields.Toggle("Copy Auras Only", cd.ParodyCopyAurasOnly);
            }

            GUILayout.Space(4);
            GUILayout.Label("<color=#aaa>Copy From Original:</color>", EditorStyles.RichLabel);
            var labels = new[] { "Name", "Image", "Upgraded Level", "Card Type" };
            var vals = new[] { cd.CopyNameFromOriginal, cd.CopyImageFromOriginal, cd.CopyCardUpgradedFromOriginal, cd.CopyCardTypeFromOriginal };
            EditorFields.ToggleGrid(labels, vals, 2);
            cd.CopyNameFromOriginal = vals[0]; cd.CopyImageFromOriginal = vals[1];
            cd.CopyCardUpgradedFromOriginal = vals[2]; cd.CopyCardTypeFromOriginal = vals[3];

            GUILayout.Space(4);
            GUILayout.Label($"<color=#aaa>Skip Card Types ({cd.SkipCardTypes?.Length ?? 0}):</color>", EditorStyles.RichLabel);
            var skip = (cd.SkipCardTypes ?? Array.Empty<Enums.CardType>()).ToList();
            for (int i = 0; i < skip.Count; i++)
            {
                GUILayout.BeginHorizontal();
                skip[i] = EditorFields.EnumField($"Type {i + 1}", skip[i], $"card_skip_{i}");
                var removeOp = EditorFields.InlineRowOps(i, skip.Count);
                GUILayout.EndHorizontal();
                if (EditorFields.ApplyRowOp(skip, i, removeOp)) break;
            }
            if (GUILayout.Button("+ Skip Type", EditorStyles.MiniButton, GUILayout.Width(90)))
            {
                skip.Add(Enums.CardType.None);
                GUI.changed = true;
            }
            cd.SkipCardTypes = skip.ToArray();
        }

        // ── Hero-class conditional gains ────────────────────────────

        public static void HeroClassGains(CardDef cd)
        {
            cd.AddCardTypeByHeroClass ??= new List<CardHeroClassTypesDef>();
            cd.AddCardListByHeroClass ??= new List<CardHeroClassCardsDef>();

            GUILayout.Label("<color=#aaa>Gain card types by hero class:</color>", EditorStyles.RichLabel);
            for (int i = 0; i < cd.AddCardTypeByHeroClass.Count; i++)
            {
                var e = cd.AddCardTypeByHeroClass[i];
                e.CardTypes ??= new List<Enums.CardType>();
                string key = "card_hct" + i;
                GUILayout.BeginHorizontal();
                e.HeroClass = EditorFields.EnumField("Hero Class", e.HeroClass, key + "_hc");
                var removeOp = EditorFields.InlineRowOps(i, cd.AddCardTypeByHeroClass.Count);
                GUILayout.EndHorizontal();
                if (EditorFields.ApplyRowOp(cd.AddCardTypeByHeroClass, i, removeOp)) break;
                for (int j = 0; j < e.CardTypes.Count; j++)
                {
                    GUILayout.BeginHorizontal();
                    e.CardTypes[j] = EditorFields.EnumField($"  Type {j + 1}", e.CardTypes[j], $"{key}_t{j}");
                    var removeTypeOp = EditorFields.InlineRowOps(j, e.CardTypes.Count);
                    GUILayout.EndHorizontal();
                    if (EditorFields.ApplyRowOp(e.CardTypes, j, removeTypeOp)) break;
                }
                if (GUILayout.Button("+ Type", EditorStyles.MiniButton, GUILayout.Width(60)))
                {
                    e.CardTypes.Add(Enums.CardType.None);
                    GUI.changed = true;
                }
            }
            if (GUILayout.Button("+ Class Type Entry", EditorStyles.MiniButton, GUILayout.Width(130)))
            {
                cd.AddCardTypeByHeroClass.Add(new CardHeroClassTypesDef());
                GUI.changed = true;
            }

            GUILayout.Space(6);
            GUILayout.Label("<color=#aaa>Gain specific cards by hero class:</color>", EditorStyles.RichLabel);
            for (int i = 0; i < cd.AddCardListByHeroClass.Count; i++)
            {
                var e = cd.AddCardListByHeroClass[i];
                e.CardIds ??= new List<string>();
                string key = "card_hcc" + i;
                GUILayout.BeginHorizontal();
                e.HeroClass = EditorFields.EnumField("Hero Class", e.HeroClass, key + "_hc");
                var removeOp = EditorFields.InlineRowOps(i, cd.AddCardListByHeroClass.Count);
                GUILayout.EndHorizontal();
                if (EditorFields.ApplyRowOp(cd.AddCardListByHeroClass, i, removeOp)) break;
                for (int j = 0; j < e.CardIds.Count; j++)
                {
                    GUILayout.BeginHorizontal();
                    e.CardIds[j] = EditorFields.IdDropdown($"  Card {j + 1}", e.CardIds[j], CardIds, $"{key}_c{j}", pickerMode: EntityPicker.Mode.Card);
                    var removeCardOp = EditorFields.InlineRowOps(j, e.CardIds.Count);
                    GUILayout.EndHorizontal();
                    if (EditorFields.ApplyRowOp(e.CardIds, j, removeCardOp)) break;
                }
                if (GUILayout.Button("+ Card", EditorStyles.MiniButton, GUILayout.Width(60)))
                {
                    e.CardIds.Add("");
                    GUI.changed = true;
                }
            }
            if (GUILayout.Button("+ Class Card Entry", EditorStyles.MiniButton, GUILayout.Width(130)))
            {
                cd.AddCardListByHeroClass.Add(new CardHeroClassCardsDef());
                GUI.changed = true;
            }
        }

        // ── Extra summons ───────────────────────────────────────────

        public static void ExtraSummons(CardDef cd)
        {
            cd.ExtraSummons ??= new List<CardSummonDef>();
            for (int i = 0; i < cd.ExtraSummons.Count; i++)
            {
                var s = cd.ExtraSummons[i];
                s.Auras ??= new List<CardSummonAuraDef>();
                string key = "card_xs" + i;
                if (EditorFields.ApplyRowOp(cd.ExtraSummons, i, EditorFields.RowHeader($"<color=#44cc88>Extra Summon {i + 1}</color>", i, cd.ExtraSummons.Count))) break;

                s.NpcId = EditorFields.IdDropdown("NPC", s.NpcId, NpcIds, key + "_npc", pickerMode: EntityPicker.Mode.NPC);
                s.Count = EditorFields.IntField("Count", s.Count, 1, 4);
                s.Evolve = EditorFields.Toggle("Evolve", s.Evolve);
                s.Metamorph = EditorFields.Toggle("Metamorph", s.Metamorph);
                for (int j = 0; j < s.Auras.Count; j++)
                {
                    var a = s.Auras[j];
                    GUILayout.BeginHorizontal();
                    a.AuraCurse = EditorFields.ACChargesField($"  Aura {j + 1}", a.AuraCurse, ref a.Charges, AcIds, $"{key}_a{j}");
                    var removeAuraOp = EditorFields.InlineRowOps(j, s.Auras.Count);
                    GUILayout.EndHorizontal();
                    if (EditorFields.ApplyRowOp(s.Auras, j, removeAuraOp)) break;
                }
                if (GUILayout.Button("+ Aura", EditorStyles.MiniButton, GUILayout.Width(60)))
                {
                    s.Auras.Add(new CardSummonAuraDef());
                    GUI.changed = true;
                }
            }
            if (GUILayout.Button("+ Extra Summon", EditorStyles.MiniButton, GUILayout.Width(110)))
            {
                cd.ExtraSummons.Add(new CardSummonDef());
                GUI.changed = true;
            }
        }

        // ── Description template / key notes ────────────────────────

        public static string[] StringArray(string title, string[] values, string key)
        {
            var list = (values ?? Array.Empty<string>()).ToList();
            GUILayout.Label($"<color=#aaa>{title} ({list.Count}):</color>", EditorStyles.RichLabel);
            for (int i = 0; i < list.Count; i++)
            {
                GUILayout.BeginHorizontal();
                list[i] = EditorFields.TextField($"  {i + 1}", list[i]);
                var removeOp = EditorFields.InlineRowOps(i, list.Count);
                GUILayout.EndHorizontal();
                if (EditorFields.ApplyRowOp(list, i, removeOp)) break;
            }
            if (GUILayout.Button("+ " + title, EditorStyles.MiniButton, GUILayout.Width(150)))
            {
                list.Add("");
                GUI.changed = true;
            }
            return list.ToArray();
        }

        private static string _gameTextFor, _gameText;

        /// <summary>Plain-text editor for the card text, with presets, a token palette, a generator and a live preview.</summary>
        private static void CustomDescriptionText(CardDef cd)
        {
            GUILayout.Label("<b>Custom description text</b>", EditorStyles.RichLabel);
            GUILayout.Label("<color=#888>Write the card text directly. Tokens such as {Damage}, {Aura} or {card:AddCardId,AddCard} read this card's own fields, so text and numbers stay in sync. Description Text replaces the game's generated text; Pre and Post lines go before and after it. The game's own tags work too (c, gl, bl, rd and gr colours, i=name icons, br1 line breaks). Values are base values (no hero scaling).</color>", EditorStyles.RichLabel);
            cd.DescriptionText = EditorFields.TextArea("Description Text", cd.DescriptionText);
            cd.PreDescriptionText = EditorFields.TextArea("Pre Text", cd.PreDescriptionText);
            cd.PostDescriptionText = EditorFields.TextArea("Post Text", cd.PostDescriptionText);

            string preset = EditorFields.IdDropdown("Insert Preset", "", DescriptionCompiler.Presets.Select(p => p.Name).ToList(), "card_descpreset");
            if (!string.IsNullOrEmpty(preset))
            {
                var found = DescriptionCompiler.Presets.First(p => p.Name == preset);
                cd.DescriptionText = string.IsNullOrEmpty(cd.DescriptionText) ? found.Template : cd.DescriptionText + "\n" + found.Template;
                GUI.changed = true;
            }
            string token = EditorFields.IdDropdown("Insert Token", "", DescriptionCompiler.CommonTokens, "card_desctoken");
            if (!string.IsNullOrEmpty(token))
            {
                cd.DescriptionText = (cd.DescriptionText ?? "") + token;
                GUI.changed = true;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate from card fields", EditorStyles.MiniButton))
            {
                cd.DescriptionText = DescriptionCompiler.Generate(cd);
                GUI.changed = true;
            }
            if (GUILayout.Button("Clear text", EditorStyles.MiniButton))
            {
                cd.DescriptionText = cd.PreDescriptionText = cd.PostDescriptionText = "";
                GUI.changed = true;
            }
            if (GUILayout.Button("Show game text", EditorStyles.MiniButton))
            {
                _gameTextFor = cd.Id;
                _gameText = ReadGameDescription(cd.Id);
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(cd.DescriptionText + cd.PreDescriptionText + cd.PostDescriptionText))
            {
                var unknown = new List<string>();
                string main = string.IsNullOrWhiteSpace(cd.DescriptionText) ? "<color=#666>(game-generated text)</color>" : DescriptionCompiler.Compile(cd, cd.DescriptionText, unknown);
                string pre = DescriptionCompiler.Compile(cd, cd.PreDescriptionText, unknown);
                string post = DescriptionCompiler.Compile(cd, cd.PostDescriptionText, unknown);
                var all = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(pre)) all.Append(pre).Append("\n");
                all.Append(main);
                if (!string.IsNullOrEmpty(post)) all.Append("\n").Append(post);
                GUILayout.Label("<color=#aaa>Preview:</color>", EditorStyles.RichLabel);
                GUILayout.Box(DescriptionCompiler.ToPreview(all.ToString()), EditorStyles.RichLabel);
                if (unknown.Count > 0)
                    GUILayout.Label("<color=#ffcc66>Unknown token(s) left as typed: " + string.Join(", ", unknown.Distinct()) + "</color>", EditorStyles.RichLabel);
            }

            if (_gameTextFor == cd.Id && !string.IsNullOrEmpty(_gameText))
            {
                GUILayout.Label("<color=#aaa>Game text (as of the last rebuild):</color>", EditorStyles.RichLabel);
                GUILayout.Box(DescriptionCompiler.ToPreview(_gameText), EditorStyles.RichLabel);
            }
            EditorStyles.Separator();
        }

        private static string ReadGameDescription(string cardId)
        {
            try
            {
                var data = Globals.Instance?.GetCardData(cardId, false);
                if (data == null) return "(card is not built yet: rebuild from the Mods tab)";
                global::Cards.Utility.CardDescriptionUtility.SetDescriptionNew(data, true, null, false);
                return data.DescriptionNormalized;
            }
            catch (Exception ex) { return "(could not read: " + ex.Message + ")"; }
        }

        public static void DescriptionTemplate(CardDef cd, IList<string> keyNoteIds)
        {
            CustomDescriptionText(cd);
            GUILayout.Label("<color=#888>Optional templated description (overrides the plain Description text when set).</color>", EditorStyles.RichLabel);
            cd.PreDescriptionId = EditorFields.TextField("Pre Description Id", cd.PreDescriptionId);
            cd.DescriptionId = EditorFields.TextField("Description Id", cd.DescriptionId);
            cd.PostDescriptionId = EditorFields.TextField("Post Description Id", cd.PostDescriptionId);
            cd.UseDescriptionFromCard = EditorFields.Toggle("Use Description From Card", cd.UseDescriptionFromCard);
            cd.PreDescriptionArgs = StringArray("Pre Args", cd.PreDescriptionArgs, "card_pda");
            cd.DescriptionArgs = StringArray("Args", cd.DescriptionArgs, "card_da");
            cd.PostDescriptionArgs = StringArray("Post Args", cd.PostDescriptionArgs, "card_poda");

            cd.KeyNoteIds ??= new List<string>();
            GUILayout.Label($"<color=#aaa>Key Notes ({cd.KeyNoteIds.Count}):</color>", EditorStyles.RichLabel);
            for (int i = 0; i < cd.KeyNoteIds.Count; i++)
            {
                GUILayout.BeginHorizontal();
                cd.KeyNoteIds[i] = EditorFields.IdDropdown($"  Note {i + 1}", cd.KeyNoteIds[i], keyNoteIds, "card_kn" + i);
                var removeOp = EditorFields.InlineRowOps(i, cd.KeyNoteIds.Count);
                GUILayout.EndHorizontal();
                if (EditorFields.ApplyRowOp(cd.KeyNoteIds, i, removeOp)) break;
            }
            if (GUILayout.Button("+ Key Note", EditorStyles.MiniButton, GUILayout.Width(90)))
            {
                cd.KeyNoteIds.Add("");
                GUI.changed = true;
            }
        }
    }
}
