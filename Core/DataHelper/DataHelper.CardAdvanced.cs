using System;
using System.Collections.Generic;
using System.Linq;
using Cards;
using Cards.Data;
using HarmonyLib;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    // Special values, per-effect conditions, extra effects and newer card mechanics.
    public static partial class DataHelper
    {
        private static SpecialValueData BuildSpecialValue(CardDef card, string where, CardSpecialValueDef sv)
        {
            if (sv == null || sv.IsEmpty) return new SpecialValueData();
            AuraCurseData ac = null;
            if (!string.IsNullOrEmpty(sv.AuraCurse))
            {
                ac = GetAuraCurse(sv.AuraCurse);
                if (ac == null)
                    Plugin.Log.LogWarning($"[DataHelper] Card '{card.Id}' special value '{where}' references missing aura/curse '{sv.AuraCurse}'.");
            }
            return new SpecialValueData(ac, sv.Type, sv.Modifier, sv.Operation, sv.IsGlobal);
        }

        private static CardEffectExtrasDef ExtrasFor(CardDef d, string slot)
            => d.EffectExtras != null && d.EffectExtras.TryGetValue(slot, out var extras) ? extras : null;

        private static void ApplyEffectExtras(EffectData effect, CardDef d, string slot, CardEffectExtrasDef extras)
        {
            if (extras == null) return;
            effect.SpecialValue = BuildSpecialValue(d, slot, extras.SpecialValue);
            effect.Condition = extras.Condition;
            effect.RequireStanza = extras.RequireStanza;
            effect.RequireStanzaLead = extras.RequireStanzaLead;
            if (effect is HealEffectData heal) heal.HealType = extras.HealType;
            if (effect is CurseEffectData curse) Traverse.Create(curse).Field("applyMode").SetValue(extras.CurseApplyMode);
        }

        private static bool HasExtras(CardEffectExtrasDef extras) => extras != null && !extras.IsDefault;

        private static void AddExtraEffect(CardDataNew card, CardDef d, CardExtraEffectDef x)
        {
            string slot = "ExtraEffects:" + x.Kind;
            switch ((x.Kind ?? "").ToLowerInvariant())
            {
                case "damage":
                {
                    var effect = new DamageEffectData(x.Amount, x.DamageType, x.DamageTarget, x.IgnoreBlock);
                    ApplyEffectExtras(effect, d, slot, x);
                    card.Damage.Add(effect);
                    break;
                }
                case "heal":
                {
                    var effect = new HealEffectData { Heal = x.Amount, HealTarget = x.HealTarget, HealType = x.HealType };
                    ApplyEffectExtras(effect, d, slot, x);
                    card.Heal.HealEffects.Add(effect);
                    break;
                }
                case "aura":
                {
                    var aura = GetAuraCurse(x.AuraCurse);
                    if (aura == null) { WarnMissingExtraAura(d, x); return; }
                    var effect = new AuraEffectData(aura, x.Amount, x.AuraTarget);
                    ApplyEffectExtras(effect, d, slot, x);
                    card.Auras.Auras.Add(effect);
                    break;
                }
                case "curse":
                {
                    var curse = GetAuraCurse(x.AuraCurse);
                    if (curse == null) { WarnMissingExtraAura(d, x); return; }
                    var effect = new CurseEffectData { Curse = curse, Charges = x.Amount, Target = x.CurseTarget };
                    ApplyEffectExtras(effect, d, slot, x);
                    card.Curses.Add(effect);
                    break;
                }
                default:
                    Plugin.Log.LogWarning($"[DataHelper] Card '{d.Id}' has an ExtraEffects entry with unknown Kind '{x.Kind}' (expected Damage, Heal, Aura or Curse).");
                    break;
            }
        }

        private static void WarnMissingExtraAura(CardDef d, CardExtraEffectDef x)
            => Plugin.Log.LogWarning($"[DataHelper] Card '{d.Id}' ExtraEffects {x.Kind} references missing aura/curse '{x.AuraCurse}'.");

        /// <summary>Applies everything the fixed-slot card builder does not cover.</summary>
        internal static void ApplyAdvancedCardData(CardDataNew card, CardDef d)
        {
            var root = Traverse.Create(card);
            root.Field("grantStanza").SetValue(d.GrantStanza);
            root.Field("ignoreStealth").SetValue(d.IgnoreStealth);
            root.Field("cardSpecialValue").SetValue(BuildSpecialValue(d, "CardSpecialValue", d.CardSpecialValue));

            var echo = Traverse.Create(card.EchoData);
            echo.Field("echoCard").SetValue(d.EchoCard);
            echo.Field("echoCardResult").SetValue(d.EchoCardResult);
            echo.Field("echoCardResurge").SetValue(d.EchoCardResurge);

            Traverse.Create(card.CopyAurasData).Field("amount").SetValue(d.CopyAurasAmount);
            card.Auras.UseRandomAura = d.UseRandomAura;

            var parody = Traverse.Create(card.ParodyData);
            parody.Field("parodyActivation").SetValue(d.ParodyActivation);
            parody.Field("parodyCopyAuraCurse").SetValue(d.ParodyCopyAuraCurse);
            parody.Field("parodyPurgeDispelAuraCurseSelf").SetValue(d.ParodyPurgeDispelAuraCurseSelf);
            parody.Field("parodyCopyingTargetCardsAmount").SetValue(d.ParodyCopyingTargetCardsAmount);
            parody.Field("parodySkipModel").SetValue(d.ParodySkipModel);
            parody.Field("parodyCopyAurasOnly").SetValue(d.ParodyCopyAurasOnly);

            var recharges = new List<EnergyRechargeData>();
            foreach (var r in d.EnergyRecharges ?? new List<CardEnergyRechargeDef>())
            {
                if (r == null) continue;
                recharges.Add(new EnergyRechargeData
                {
                    EnergyRecharge = r.Amount,
                    EnergyRechargeSpecialValue = BuildSpecialValue(d, "EnergyRecharge", r.SpecialValue),
                    Target = r.Target,
                    TargetTeam = r.TargetTeam
                });
            }
            root.Field("energyRecharges").SetValue(recharges);

            foreach (var extra in d.ExtraEffects ?? new List<CardExtraEffectDef>())
                if (extra != null) AddExtraEffect(card, d, extra);

            ApplyCardManagementExtras(card, d);
            ApplyExtraSummons(card, d);
            ApplyDescriptionTemplate(card, d);
        }

        private static void ApplyCardManagementExtras(CardDataNew card, CardDef d)
        {
            var management = card.CardManagement;
            Traverse.Create(management).Field("skipCardTypes").SetValue(d.SkipCardTypes ?? Array.Empty<Enums.CardType>());

            var copy = (CopyConfig)Activator.CreateInstance(typeof(CopyConfig), true);
            var copyFields = Traverse.Create(copy);
            copyFields.Field("copyNameFromOriginal").SetValue(d.CopyNameFromOriginal);
            copyFields.Field("copyImageFromOriginal").SetValue(d.CopyImageFromOriginal);
            copyFields.Field("copyCardUpgradedFromOriginal").SetValue(d.CopyCardUpgradedFromOriginal);
            copyFields.Field("copyCardTypeFromOriginal").SetValue(d.CopyCardTypeFromOriginal);
            Traverse.Create(management).Field("copyConfig").SetValue(copy);

            management.AddCardTypeBasedOnHeroClass = (d.AddCardTypeByHeroClass ?? new List<CardHeroClassTypesDef>())
                .Where(e => e != null)
                .Select(e => new CardToGainTypeBasedOnHeroClass { heroClass = e.HeroClass, cardTypes = new List<Enums.CardType>(e.CardTypes ?? new List<Enums.CardType>()) })
                .ToList();
            management.AddCardListBasedOnHeroClass = (d.AddCardListByHeroClass ?? new List<CardHeroClassCardsDef>())
                .Where(e => e != null)
                .Select(e => new CardToGainListBasedOnHeroClass { heroClass = e.HeroClass, cardsList = new List<CardDataNew>() })
                .ToList();
        }

        private static void ApplyExtraSummons(CardDataNew card, CardDef d)
        {
            var list = new List<SummonData>();
            foreach (var s in d.ExtraSummons ?? new List<CardSummonDef>())
            {
                if (s == null) continue;
                var summon = new SummonData
                {
                    SummonUnit = string.IsNullOrEmpty(s.NpcId) ? null : GetExistingNPC(s.NpcId, false),
                    SummonUnitNum = s.Count,
                    Metamorph = s.Metamorph,
                    Evolve = s.Evolve,
                    SummonAuras = new List<SummonAuraData>()
                };
                foreach (var a in s.Auras ?? new List<CardSummonAuraDef>())
                {
                    var aura = string.IsNullOrEmpty(a?.AuraCurse) ? null : GetAuraCurse(a.AuraCurse);
                    if (aura != null) summon.SummonAuras.Add(new SummonAuraData { Aura = aura, Charges = a.Charges });
                }
                list.Add(summon);
            }
            Traverse.Create(card).Field("extraSummons").SetValue(list);
        }

        /// <summary>Compile a custom description text and register it as a mod text; returns the text key the card should use (the explicit id when no custom text).</summary>
        private static string ResolveDescriptionText(CardDef d, string autoPrefix, string explicitId, string template)
        {
            if (string.IsNullOrWhiteSpace(template)) return explicitId ?? "";
            string key = string.IsNullOrEmpty(explicitId) ? autoPrefix + d.Id : explicitId;
            var unknown = new List<string>();
            string compiled = DescriptionCompiler.Compile(d, template, unknown);
            if (unknown.Count > 0)
                Plugin.Log.LogWarning($"[DataHelper] Card '{d.Id}': unknown description token(s) {string.Join(", ", unknown.Distinct())} left as typed.");
            SetModText(TextKey(key), compiled);
            return key;
        }

        private static void ApplyDescriptionTemplate(CardDataNew card, CardDef d)
        {
            var description = Traverse.Create(card.Description);
            string preId = ResolveDescriptionText(d, "modpredesc_", d.PreDescriptionId, d.PreDescriptionText);
            string mainId = ResolveDescriptionText(d, "moddesc_", d.DescriptionId, d.DescriptionText);
            string postId = ResolveDescriptionText(d, "modpostdesc_", d.PostDescriptionId, d.PostDescriptionText);
            description.Field("preDescriptionId").SetValue(preId);
            description.Field("descriptionId").SetValue(mainId);
            description.Field("postDescriptionId").SetValue(postId);
            description.Field("useDescriptionFromCard").SetValue(d.UseDescriptionFromCard);
            description.Field("preDescriptionArgs").SetValue(d.PreDescriptionArgs ?? Array.Empty<string>());
            description.Field("descriptionArgs").SetValue(d.DescriptionArgs ?? Array.Empty<string>());
            description.Field("postDescriptionArgs").SetValue(d.PostDescriptionArgs ?? Array.Empty<string>());

            var notes = new List<KeyNotesData>();
            foreach (var id in d.KeyNoteIds ?? new List<string>())
            {
                if (string.IsNullOrEmpty(id)) continue;
                var note = Globals.Instance?.GetKeyNotesData(id);
                if (note == null) Plugin.Log.LogWarning($"[DataHelper] Card '{d.Id}' references missing key note '{id}'.");
                else notes.Add(note);
            }
            description.Field("keyNotes").SetValue(notes);
        }

        /// <summary>Re-resolve references to entities that are built after cards (NPCs, hero-class card lists).</summary>
        internal static void ResolveLateCardRefs(CardDef d, CardDataNew source)
        {
            if (!string.IsNullOrEmpty(d.SummonUnitId))
                source.Summon.SummonUnit = GetExistingNPC(d.SummonUnitId);

            var extras = d.ExtraSummons ?? new List<CardSummonDef>();
            for (int i = 0; i < extras.Count && i < source.ExtraSummons.Count; i++)
            {
                if (string.IsNullOrEmpty(extras[i]?.NpcId)) continue;
                source.ExtraSummons[i].SummonUnit = GetExistingNPC(extras[i].NpcId);
                if (source.ExtraSummons[i].SummonUnit == null)
                    Plugin.Log.LogWarning($"[DataHelper] Card '{d.Id}' extra summon NPC '{extras[i].NpcId}' was not found.");
            }

            var byClass = d.AddCardListByHeroClass ?? new List<CardHeroClassCardsDef>();
            var built = source.CardManagement.AddCardListBasedOnHeroClass;
            for (int i = 0; i < byClass.Count && built != null && i < built.Count; i++)
            {
                var cards = new List<CardDataNew>();
                foreach (var id in byClass[i]?.CardIds ?? new List<string>())
                {
                    var target = GetCardSource(id);
                    if (target == null)
                        Plugin.Log.LogWarning($"[DataHelper] Card '{d.Id}' AddCardListByHeroClass references missing card '{id}'.");
                    else cards.Add(target);
                }
                built[i] = new CardToGainListBasedOnHeroClass { heroClass = built[i].heroClass, cardsList = cards };
            }
        }
    }
}
