using System;
using System.Collections.Generic;
using System.Reflection;
using Cards;
using Cards.Data;
using Cards.Data.Effects;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class ModProjectBuilder
    {
        public static CardDataNew MakeFullCard(CardDef d) => DataHelper.BuildCard(d);

        public static CardDef SnapshotCard(CardRealtimeData runtime)
        {
            if (runtime == null) return null;
            var template = Traverse.Create(runtime).Field<CardDataNew>("card").Value;
            if (template == null)
            {
                Plugin.Log.LogWarning("[ModProjectBuilder] Could not unwrap CardRealtimeData.card for snapshot.");
                return null;
            }
            return SnapshotCard(template);
        }

        public static CardDef SnapshotCard(CardDataNew template)
        {
            if (template == null) return null;
            var d = new CardDef();
            CopyFields(template, d);
            CopyFields(template.Description, d);
            CopyFields(template.Upgrade, d);
            CopyFields(template.TargetData, d);
            CopyFields(template.RepeatCast, d);
            CopyFields(template.Heal, d);
            CopyFields(template.CardManagement, d);
            CopyFields(template.InGameEffects, d);
            CopyFields(template.PetData, d);
            CopyFields(template.Summon, d);
            CopyFields(template.AuraCurseDispels, d);

            d.Name = template.CardName ?? "";
            d.Id = template.Id ?? "";
            d.BaseCard = template.BaseCard ?? "";
            d.BaseCardId = d.BaseCard.ToLowerInvariant();
            d.CardUpgraded = template.Upgrade?.CardUpgraded ?? Enums.CardUpgraded.No;
            d.IsUpgraded = d.CardUpgraded != Enums.CardUpgraded.No;

            d.CardTypeAux = (template.CardTypeAux ?? Array.Empty<Enums.CardType>()).Clone() as Enums.CardType[];
            d.DiscardCardTypeAux = (template.CardManagement?.DiscardCardTypeAux ?? Array.Empty<Enums.CardType>()).Clone() as Enums.CardType[];
            d.AddCardTypeAux = (template.CardManagement?.AddCardTypeAux ?? Array.Empty<Enums.CardType>()).Clone() as Enums.CardType[];

            var description = template.Description;
            if (description != null)
            {
                var relatedCards = description.RelatedCards;
                d.RelatedCard = relatedCards != null && relatedCards.Count > 0 ? relatedCards[0] : "";
                d.RelatedCard2 = relatedCards != null && relatedCards.Count > 1 ? relatedCards[1] : "";
                d.RelatedCard3 = relatedCards != null && relatedCards.Count > 2 ? relatedCards[2] : "";
                if (description.Sprite != null) d.SpriteSource = description.Sprite.name;
            }

            var flags = template.CustomFlags;
            d.Vanish = flags.HasFlag(CustomFlags.Vanish);
            d.Innate = flags.HasFlag(CustomFlags.Innate);
            d.Lazy = flags.HasFlag(CustomFlags.Lazy);
            d.EndTurn = flags.HasFlag(CustomFlags.EndTurn);
            d.MoveToCenter = flags.HasFlag(CustomFlags.MoveToCenter);
            d.Corrupted = flags.HasFlag(CustomFlags.Corrupted);
            d.Starter = flags.HasFlag(CustomFlags.Starter);
            d.OnlyInWeekly = flags.HasFlag(CustomFlags.OnlyInWeekly);
            d.CombatBased = flags.HasFlag(CustomFlags.CombatBased);

            var target = template.TargetData;
            if (target != null)
            {
                d.TargetSide = target.TargetSide;
                d.TargetType = target.TargetType;
                d.TargetPos = target.TargetPosition;
            }

            var repeat = template.RepeatCast;
            if (repeat != null)
            {
                d.EffectRepeat = repeat.EffectRepeat;
                d.EffectRepeatDelay = repeat.EffectRepeatDelay;
                d.EffectRepeatEnergyBonus = repeat.EffectRepeatEnergyBonus;
                d.EffectRepeatMaxBonus = repeat.EffectRepeatMaxBonus;
                d.EffectRepeatModificator = repeat.EffectRepeatModificator;
                d.EffectRepeatTarget = repeat.EffectRepeatTarget;
            }

            SnapshotDamage(template.Damage, d);
            SnapshotHealing(template.Heal, d);
            if (template.SelfHealthLoss != null) d.SelfHealthLoss = template.SelfHealthLoss.SelfHealthLoss;
            d.AcEnergyBonus = AuraId(template.AcEnergyBonus);
            d.AcEnergyBonusQuantity = template.AcEnergyBonusQuantity;
            d.AcEnergyBonus2 = AuraId(template.AcEnergyBonus2);
            d.AcEnergyBonus2Quantity = template.AcEnergyBonus2Quantity;
            SnapshotAuras(template.Auras, d);
            SnapshotCurses(template.Curses, d);
            SnapshotDispels(template.AuraCurseDispels, d);
            SnapshotSummon(template.Summon, d);
            SnapshotCardManagement(template.CardManagement, d);
            SnapshotPet(template.PetData, d);
            SnapshotInGameEffects(template.InGameEffects, d);
            SnapshotAdvanced(template, d);

            return d;
        }

        private static void CopyFields(object source, CardDef destination)
        {
            if (source == null) return;
            foreach (var destinationField in typeof(CardDef).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var sourceFieldName = char.ToLowerInvariant(destinationField.Name[0]) + destinationField.Name.Substring(1);
                var sourceField = AccessTools.Field(source.GetType(), sourceFieldName);
                if (sourceField != null && sourceField.FieldType == destinationField.FieldType)
                    destinationField.SetValue(destination, sourceField.GetValue(source));
            }
        }

        private static string AuraId(AuraCurseData value) => value?.Id ?? "";

        private static CardSpecialValueDef SnapshotSpecialValue(SpecialValueData sv)
        {
            if (sv == null) return new CardSpecialValueDef();
            return new CardSpecialValueDef
            {
                Type = sv.ValueType,
                AuraCurse = sv.AuraCurseName?.Id ?? "",
                Modifier = sv.Modifier,
                Operation = sv.Operation,
                IsGlobal = sv.IsGlobal
            };
        }

        private static CardEffectExtrasDef SnapshotExtras(EffectData effect)
        {
            var extras = new CardEffectExtrasDef
            {
                SpecialValue = SnapshotSpecialValue(effect.SpecialValue),
                Condition = effect.Condition,
                RequireStanza = effect.RequireStanza,
                RequireStanzaLead = effect.RequireStanzaLead
            };
            if (effect is HealEffectData heal) extras.HealType = heal.HealType;
            if (effect is CurseEffectData curse) extras.CurseApplyMode = curse.ApplyMode;
            return extras;
        }

        private static void KeepExtras(CardDef d, string slot, EffectData effect)
        {
            var extras = SnapshotExtras(effect);
            if (!extras.IsDefault) d.EffectExtras[slot] = extras;
        }

        private static void AddExtraEffect(CardDef d, string kind, EffectData effect, Action<CardExtraEffectDef> fill)
        {
            var extra = new CardExtraEffectDef { Kind = kind };
            var extras = SnapshotExtras(effect);
            extra.SpecialValue = extras.SpecialValue;
            extra.Condition = extras.Condition;
            extra.RequireStanza = extras.RequireStanza;
            extra.RequireStanzaLead = extras.RequireStanzaLead;
            extra.HealType = extras.HealType;
            extra.CurseApplyMode = extras.CurseApplyMode;
            fill(extra);
            d.ExtraEffects.Add(extra);
        }

        private static void SnapshotDamage(List<DamageEffectData> effects, CardDef d)
        {
            var target = new List<DamageEffectData>();
            var self = new List<DamageEffectData>();
            foreach (var effect in effects ?? new List<DamageEffectData>())
            {
                if (effect == null) continue;
                if (effect.DamageTarget == DamageTarget.Target && target.Count < 2) target.Add(effect);
                else if (effect.DamageTarget == DamageTarget.Self && self.Count < 2) self.Add(effect);
                else AddExtraEffect(d, "Damage", effect, x =>
                {
                    x.Amount = effect.Damage; x.DamageType = effect.DamageType;
                    x.DamageTarget = effect.DamageTarget; x.IgnoreBlock = effect.IgnoreBlock;
                });
            }
            if (target.Count > 0) { d.Damage = target[0].Damage; d.DamageType = target[0].DamageType; d.IgnoreBlock = target[0].IgnoreBlock; KeepExtras(d, "Damage", target[0]); }
            if (target.Count > 1) { d.Damage2 = target[1].Damage; d.DamageType2 = target[1].DamageType; d.IgnoreBlock2 = target[1].IgnoreBlock; KeepExtras(d, "Damage2", target[1]); }
            if (self.Count > 0) { d.DamageSelf = self[0].Damage; d.DamageSelfType = self[0].DamageType; d.IgnoreBlockSelf = self[0].IgnoreBlock; KeepExtras(d, "DamageSelf", self[0]); }
            if (self.Count > 1) { d.DamageSelf2 = self[1].Damage; d.DamageSelfType2 = self[1].DamageType; d.IgnoreBlockSelf2 = self[1].IgnoreBlock; KeepExtras(d, "DamageSelf2", self[1]); }
        }

        private static void SnapshotHealing(HealData heal, CardDef d)
        {
            bool hasTarget = false, hasSelf = false;
            foreach (var effect in heal?.HealEffects ?? new List<HealEffectData>())
            {
                if (effect == null) continue;
                if (effect.HealTarget == HealTarget.Target && !hasTarget)
                {
                    hasTarget = true; d.Heal = effect.Heal; KeepExtras(d, "Heal", effect);
                }
                else if (effect.HealTarget == HealTarget.Self && !hasSelf)
                {
                    hasSelf = true; d.HealSelf = effect.Heal; KeepExtras(d, "HealSelf", effect);
                }
                else AddExtraEffect(d, "Heal", effect, x => { x.Amount = effect.Heal; x.HealTarget = effect.HealTarget; });
            }
        }

        private static void SnapshotAuras(AuraData auras, CardDef d)
        {
            d.UseRandomAura = auras?.UseRandomAura ?? false;
            var target = new List<AuraEffectData>();
            var self = new List<AuraEffectData>();
            foreach (var effect in auras?.Auras ?? new List<AuraEffectData>())
            {
                if (effect == null) continue;
                if (effect.Target == AuraTarget.Target && target.Count < 3) target.Add(effect);
                else if (effect.Target == AuraTarget.Self && self.Count < 3) self.Add(effect);
                else AddExtraEffect(d, "Aura", effect, x => { x.AuraCurse = AuraId(effect.Aura); x.Amount = effect.Charges; x.AuraTarget = effect.Target; });
            }
            SetAura(d, target, 0, "Aura", ref d.Aura, ref d.AuraCharges);
            SetAura(d, target, 1, "Aura2", ref d.Aura2, ref d.Aura2Charges);
            SetAura(d, target, 2, "Aura3", ref d.Aura3, ref d.Aura3Charges);
            SetAura(d, self, 0, "AuraSelf", ref d.AuraSelf, ref d.AuraSelfCharges);
            SetAura(d, self, 1, "AuraSelf2", ref d.AuraSelf2, ref d.AuraSelf2Charges);
            SetAura(d, self, 2, "AuraSelf3", ref d.AuraSelf3, ref d.AuraSelf3Charges);
        }

        private static void SetAura(CardDef d, List<AuraEffectData> effects, int index, string slot, ref string id, ref int charges)
        {
            if (effects.Count <= index) return;
            id = AuraId(effects[index].Aura);
            charges = effects[index].Charges;
            KeepExtras(d, slot, effects[index]);
        }

        private static void SnapshotCurses(List<CurseEffectData> curses, CardDef d)
        {
            var target = new List<CurseEffectData>();
            var self = new List<CurseEffectData>();
            foreach (var effect in curses ?? new List<CurseEffectData>())
            {
                if (effect == null) continue;
                if (effect.Target == CurseTarget.Target && target.Count < 3) target.Add(effect);
                else if (effect.Target == CurseTarget.Self && self.Count < 3) self.Add(effect);
                else AddExtraEffect(d, "Curse", effect, x => { x.AuraCurse = AuraId(effect.Curse); x.Amount = effect.Charges; x.CurseTarget = effect.Target; });
            }
            SetCurse(d, target, 0, "Curse", ref d.Curse, ref d.CurseCharges);
            SetCurse(d, target, 1, "Curse2", ref d.Curse2, ref d.Curse2Charges);
            SetCurse(d, target, 2, "Curse3", ref d.Curse3, ref d.Curse3Charges);
            SetCurse(d, self, 0, "CurseSelf", ref d.CurseSelf, ref d.CurseSelfCharges);
            SetCurse(d, self, 1, "CurseSelf2", ref d.CurseSelf2, ref d.CurseSelf2Charges);
            SetCurse(d, self, 2, "CurseSelf3", ref d.CurseSelf3, ref d.CurseSelf3Charges);
        }

        private static void SetCurse(CardDef d, List<CurseEffectData> effects, int index, string slot, ref string id, ref int charges)
        {
            if (effects.Count <= index) return;
            id = AuraId(effects[index].Curse);
            charges = effects[index].Charges;
            KeepExtras(d, slot, effects[index]);
        }

        private static void SnapshotAdvanced(CardDataNew template, CardDef d)
        {
            CopyFields(template.EchoData, d);
            CopyFields(template.ParodyData, d);
            CopyFields(template.CardManagement?.CopyConfig, d);
            d.CopyAurasAmount = template.CopyAurasData?.Amount ?? 0;
            d.CardSpecialValue = SnapshotSpecialValue(template.CardSpecialValue);
            d.SelfHealthLossSpecialValue = SnapshotSpecialValue(template.SelfHealthLoss?.SpecialValue);
            d.DrawCardSpecialValue = SnapshotSpecialValue(template.CardManagement?.DrawCardSpecialValue);

            foreach (var r in template.EnergyRecharges ?? new List<EnergyRechargeData>())
            {
                if (r == null) continue;
                d.EnergyRecharges.Add(new CardEnergyRechargeDef
                {
                    Amount = r.EnergyRecharge,
                    SpecialValue = SnapshotSpecialValue(r.EnergyRechargeSpecialValue),
                    Target = r.Target,
                    TargetTeam = r.TargetTeam
                });
            }

            foreach (var s in template.ExtraSummons ?? new List<SummonData>())
            {
                if (s == null) continue;
                var summon = new CardSummonDef { NpcId = s.SummonUnit?.Id ?? "", Count = s.SummonUnitNum, Metamorph = s.Metamorph, Evolve = s.Evolve };
                foreach (var a in s.SummonAuras ?? new List<SummonAuraData>())
                    if (a != null) summon.Auras.Add(new CardSummonAuraDef { AuraCurse = AuraId(a.Aura), Charges = a.Charges });
                d.ExtraSummons.Add(summon);
            }

            var management = template.CardManagement;
            d.SkipCardTypes = (management?.SkipCardTypes ?? Array.Empty<Enums.CardType>()).Clone() as Enums.CardType[];
            foreach (var e in management?.AddCardTypeBasedOnHeroClass ?? new List<CardToGainTypeBasedOnHeroClass>())
                d.AddCardTypeByHeroClass.Add(new CardHeroClassTypesDef { HeroClass = e.heroClass, CardTypes = new List<Enums.CardType>(e.cardTypes ?? new List<Enums.CardType>()) });
            foreach (var e in management?.AddCardListBasedOnHeroClass ?? new List<CardToGainListBasedOnHeroClass>())
            {
                var entry = new CardHeroClassCardsDef { HeroClass = e.heroClass };
                foreach (var c in e.cardsList ?? new List<CardDataNew>())
                    if (c != null && !string.IsNullOrEmpty(c.Id)) entry.CardIds.Add(c.Id);
                d.AddCardListByHeroClass.Add(entry);
            }

            foreach (var note in template.Description?.KeyNotes ?? new List<KeyNotesData>())
                if (note != null && !string.IsNullOrEmpty(note.Id)) d.KeyNoteIds.Add(note.Id);
        }
        private static void SnapshotDispels(AuraCurseDispelData dispels, CardDef d)
        {
            if (dispels == null) return;
            d.HealAuraCurseSelf = AuraId(dispels.HealAuraCurseSelf);
            var names = dispels.HealAuraCurses ?? new List<AuraCurseData>();
            if (names.Count > 0) d.HealAuraCurseName = AuraId(names[0]);
            if (names.Count > 1) d.HealAuraCurseName2 = AuraId(names[1]);
            if (names.Count > 2) d.HealAuraCurseName3 = AuraId(names[2]);
            if (names.Count > 3) d.HealAuraCurseName4 = AuraId(names[3]);
        }

        private static void SnapshotSummon(SummonData summon, CardDef d)
        {
            if (summon == null) return;
            d.SummonUnitId = summon.SummonUnit?.Id ?? "";
            d.SummonNum = summon.SummonUnitNum;
            d.Evolve = summon.Evolve;
            d.Metamorph = summon.Metamorph;
            var auras = summon.SummonAuras ?? new List<SummonAuraData>();
            var auraIndex = 0;
            foreach (var aura in auras)
            {
                if (aura == null) continue;
                if (auraIndex == 0) { d.SummonAura = AuraId(aura.Aura); d.SummonAuraCharges = aura.Charges; }
                else if (auraIndex == 1) { d.SummonAura2 = AuraId(aura.Aura); d.SummonAuraCharges2 = aura.Charges; }
                else if (auraIndex == 2) { d.SummonAura3 = AuraId(aura.Aura); d.SummonAuraCharges3 = aura.Charges; }
                auraIndex++;
                if (auraIndex == 3) break;
            }
        }

        private static void SnapshotCardManagement(CardManagementData management, CardDef d)
        {
            if (management == null) return;
            d.AddCardList = new List<string>();
            foreach (var card in management.AddCardList ?? Array.Empty<CardDataNew>())
                if (card != null && !string.IsNullOrEmpty(card.Id)) d.AddCardList.Add(card.Id);
        }

        private static void SnapshotPet(PetData pet, CardDef d)
        {
            if (pet == null) return;
            d.PetOffsetX = pet.PetOffset.x;
            d.PetOffsetY = pet.PetOffset.y;
            d.PetSizeX = pet.PetSize.x;
            d.PetSizeY = pet.PetSize.y;
        }

        private static void SnapshotInGameEffects(InGameEffectsData effects, CardDef d)
        {
            if (effects == null) return;
            d.EffectCaster = effects.EffectCaster;
            d.EffectTarget = effects.EffectTarget;
            d.EffectPreAction = effects.EffectPreAction;
            d.EffectCasterRepeat = effects.EffectCasterRepeat;
            d.EffectPostCastDelay = effects.EffectPostCastDelay;
            d.EffectCastCenter = effects.EffectCastCenter;
            d.EffectTrail = effects.EffectTrail;
            d.EffectTrailRepeat = effects.EffectTrailRepeat;
            d.EffectTrailSpeed = effects.EffectTrailSpeed;
            d.EffectTrailAngle = effects.EffectTrailAngle;
            d.EffectPostTargetDelay = effects.EffectPostTargetDelay;
            d.GoldGainQuantity = effects.GoldGainQuantity;
            d.ShardsGainQuantity = effects.ShardsGainQuantity;
        }
    }
}
