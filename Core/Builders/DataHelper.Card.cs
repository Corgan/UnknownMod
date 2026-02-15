using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Card Builders (MakeCard, ApplyCardExtras, etc.)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        /// <summary>Create a monster NPC ability card.</summary>
        public static CardData MakeCard(
            string id, string name,
            int damage = 0, Enums.DamageType dmgType = Enums.DamageType.None,
            int damage2 = 0, Enums.DamageType dmgType2 = Enums.DamageType.None,
            string curse = null, int curseCharges = 0,
            string curse2 = null, int curse2Charges = 0,
            string aura = null, int auraCharges = 0,
            string auraSelf = null, int auraSelfCharges = 0,
            string curseSelf = null, int curseSelfCharges = 0,
            int heal = 0, int healSelf = 0,
            Enums.CardTargetSide targetSide = Enums.CardTargetSide.Enemy,
            Enums.CardTargetType targetType = Enums.CardTargetType.Single,
            Enums.CardTargetPosition targetPos = Enums.CardTargetPosition.Anywhere,
            int effectRepeat = 1,
            bool moveToCenter = false,
            string summonUnit = null, int summonNum = 0,
            int selfHealthLoss = 0,
            int healCurses = 0, int dispelAuras = 0,
            Enums.CardUpgraded upgraded = Enums.CardUpgraded.No,
            string effectCaster = "", string effectTarget = "",
            int energyCost = 0)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            var t = Traverse.Create(card);

            t.Field("id").SetValue(id);
            t.Field("cardName").SetValue(name);
            t.Field("cardClass").SetValue(Enums.CardClass.Monster);
            t.Field("cardRarity").SetValue(Enums.CardRarity.Common);
            t.Field("cardType").SetValue(Enums.CardType.None);
            t.Field("cardUpgraded").SetValue(upgraded);
            t.Field("playable").SetValue(false);
            t.Field("visible").SetValue(false);
            t.Field("showInTome").SetValue(false);
            t.Field("energyCost").SetValue(energyCost);

            // Initialize all nullable arrays/strings to prevent NREs
            t.Field("internalId").SetValue(id);
            t.Field("sku").SetValue("");
            t.Field("relatedCard").SetValue("");
            t.Field("relatedCard2").SetValue("");
            t.Field("relatedCard3").SetValue("");
            t.Field("cardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("discardCardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("addCardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("addCardList").SetValue(new CardData[0]);
            t.Field("preDescriptionArgs").SetValue(new string[0]);
            t.Field("descriptionArgs").SetValue(new string[0]);
            t.Field("postDescriptionArgs").SetValue(new string[0]);

            // Targeting
            t.Field("targetSide").SetValue(targetSide);
            t.Field("targetType").SetValue(targetType);
            t.Field("targetPosition").SetValue(targetPos);

            // Damage
            if (damage > 0 || dmgType != Enums.DamageType.None)
            {
                t.Field("damage").SetValue(damage);
                t.Field("damageType").SetValue(dmgType);
            }
            if (damage2 > 0 || dmgType2 != Enums.DamageType.None)
            {
                t.Field("damage2").SetValue(damage2);
                t.Field("damageType2").SetValue(dmgType2);
            }

            // Curses (applied to target)
            if (!string.IsNullOrEmpty(curse))
            {
                t.Field("curse").SetValue(GetAuraCurse(curse));
                t.Field("curseCharges").SetValue(curseCharges);
            }
            if (!string.IsNullOrEmpty(curse2))
            {
                t.Field("curse2").SetValue(GetAuraCurse(curse2));
                t.Field("curseCharges2").SetValue(curse2Charges);
            }

            // Auras (applied to target)
            if (!string.IsNullOrEmpty(aura))
            {
                t.Field("aura").SetValue(GetAuraCurse(aura));
                t.Field("auraCharges").SetValue(auraCharges);
            }

            // Self auras/curses
            if (!string.IsNullOrEmpty(auraSelf))
            {
                t.Field("auraSelf").SetValue(GetAuraCurse(auraSelf));
                t.Field("auraCharges").SetValue(System.Math.Max(auraCharges, auraSelfCharges));
            }
            if (!string.IsNullOrEmpty(curseSelf))
            {
                t.Field("curseSelf").SetValue(GetAuraCurse(curseSelf));
                t.Field("curseCharges").SetValue(System.Math.Max(curseCharges, curseSelfCharges));
            }

            // Heal
            if (heal > 0) t.Field("heal").SetValue(heal);
            if (healSelf > 0) t.Field("healSelf").SetValue(healSelf);

            // Dispels
            if (healCurses > 0) t.Field("healCurses").SetValue(healCurses);
            if (dispelAuras > 0) t.Field("dispelAuras").SetValue(dispelAuras);

            // Effects
            t.Field("effectRepeat").SetValue(effectRepeat);
            t.Field("moveToCenter").SetValue(moveToCenter);
            t.Field("selfHealthLoss").SetValue(selfHealthLoss);

            // Summon
            if (!string.IsNullOrEmpty(summonUnit))
                t.Field("summonUnitNum").SetValue(summonNum > 0 ? summonNum : 1);

            // FX
            if (!string.IsNullOrEmpty(effectCaster)) t.Field("effectCaster").SetValue(effectCaster);
            if (!string.IsNullOrEmpty(effectTarget)) t.Field("effectTarget").SetValue(effectTarget);

            return card;
        }

        /// <summary>Apply extended CardDef fields via Traverse (called after MakeCard).</summary>
        public static void ApplyCardExtras(CardData card, CardDef d)
        {
            var t = Traverse.Create(card);

            // ── Card classification (MakeCard hardcodes these; override from CardDef) ──
            t.Field("cardClass").SetValue(d.CardClass);
            t.Field("cardRarity").SetValue(d.CardRarity);
            t.Field("cardType").SetValue(d.CardType);
            if (d.CardTypeAux != null && d.CardTypeAux.Length > 0)
                t.Field("cardTypeAux").SetValue(d.CardTypeAux);
            if (d.CardNumber != 0) t.Field("cardNumber").SetValue(d.CardNumber);
            if (d.MaxInDeck != 0) t.Field("maxInDeck").SetValue(d.MaxInDeck);

            // ── Flags ──
            t.Field("playable").SetValue(d.Playable);
            t.Field("visible").SetValue(d.Visible);
            t.Field("showInTome").SetValue(d.ShowInTome);
            if (d.AutoplayDraw) t.Field("autoplayDraw").SetValue(true);
            if (d.AutoplayEndTurn) t.Field("autoplayEndTurn").SetValue(true);
            if (d.Vanish) t.Field("vanish").SetValue(true);
            if (d.Innate) t.Field("innate").SetValue(true);
            if (d.Lazy) t.Field("lazy").SetValue(true);
            if (d.Corrupted) t.Field("corrupted").SetValue(true);
            if (d.EndTurn) t.Field("endTurn").SetValue(true);
            if (d.Starter) t.Field("starter").SetValue(true);
            if (d.FlipSprite) t.Field("flipSprite").SetValue(true);
            if (d.ModifiedByTrait) t.Field("modifiedByTrait").SetValue(true);
            if (d.OnlyInWeekly) t.Field("onlyInWeekly").SetValue(true);
            if (!string.IsNullOrEmpty(d.Sku)) t.Field("sku").SetValue(d.Sku);

            // ── Cost / Economy extras ──
            if (d.EnergyCost != 0) t.Field("energyCost").SetValue(d.EnergyCost);
            if (d.EnergyCostOriginal != 0) t.Field("energyCostOriginal").SetValue(d.EnergyCostOriginal);
            if (d.EnergyCostForShow != 0) t.Field("energyCostForShow").SetValue(d.EnergyCostForShow);
            if (d.EnergyReductionPermanent != 0) t.Field("energyReductionPermanent").SetValue(d.EnergyReductionPermanent);
            if (d.EnergyReductionTemporal != 0) t.Field("energyReductionTemporal").SetValue(d.EnergyReductionTemporal);
            if (d.EnergyReductionToZeroPermanent) t.Field("energyReductionToZeroPermanent").SetValue(true);
            if (d.EnergyReductionToZeroTemporal) t.Field("energyReductionToZeroTemporal").SetValue(true);

            // ── Upgrade paths ──
            if (!string.IsNullOrEmpty(d.UpgradesTo1)) t.Field("upgradesTo1").SetValue(d.UpgradesTo1);
            if (!string.IsNullOrEmpty(d.UpgradesTo2)) t.Field("upgradesTo2").SetValue(d.UpgradesTo2);
            if (!string.IsNullOrEmpty(d.UpgradedFrom)) t.Field("upgradedFrom").SetValue(d.UpgradedFrom);
            if (!string.IsNullOrEmpty(d.BaseCard)) t.Field("baseCard").SetValue(d.BaseCard);
            if (!string.IsNullOrEmpty(d.RelatedCard)) t.Field("relatedCard").SetValue(d.RelatedCard);
            if (!string.IsNullOrEmpty(d.RelatedCard2)) t.Field("relatedCard2").SetValue(d.RelatedCard2);
            if (!string.IsNullOrEmpty(d.RelatedCard3)) t.Field("relatedCard3").SetValue(d.RelatedCard3);

            // ── Damage extras ──
            if (d.DamageSides > 0) t.Field("damageSides").SetValue(d.DamageSides);
            if (d.DamageSides2 > 0) t.Field("damageSides2").SetValue(d.DamageSides2);
            if (d.DamageEnergyBonus != 0) t.Field("damageEnergyBonus").SetValue(d.DamageEnergyBonus);
            if (d.IgnoreBlock) t.Field("ignoreBlock").SetValue(true);
            if (d.IgnoreBlock2) t.Field("ignoreBlock2").SetValue(true);
            if (d.DamageSelf != 0) t.Field("damageSelf").SetValue(d.DamageSelf);
            if (d.DamageSelf2 != 0) t.Field("damageSelf2").SetValue(d.DamageSelf2);

            // ── 3rd curse/aura slots ──
            if (!string.IsNullOrEmpty(d.Curse3))
            {
                t.Field("curse3").SetValue(GetAuraCurse(d.Curse3));
                t.Field("curseCharges3").SetValue(d.Curse3Charges);
            }
            if (!string.IsNullOrEmpty(d.Aura2))
            {
                t.Field("aura2").SetValue(GetAuraCurse(d.Aura2));
                t.Field("auraCharges2").SetValue(d.Aura2Charges);
            }
            if (!string.IsNullOrEmpty(d.AuraSelf2))
            {
                t.Field("auraSelf2").SetValue(GetAuraCurse(d.AuraSelf2));
                int charges2 = !string.IsNullOrEmpty(d.Aura2) ? System.Math.Max(d.Aura2Charges, d.AuraSelf2Charges) : d.AuraSelf2Charges;
                t.Field("auraCharges2").SetValue(charges2);
            }
            if (!string.IsNullOrEmpty(d.Aura3))
            {
                t.Field("aura3").SetValue(GetAuraCurse(d.Aura3));
                t.Field("auraCharges3").SetValue(d.Aura3Charges);
            }
            if (!string.IsNullOrEmpty(d.AuraSelf3))
            {
                t.Field("auraSelf3").SetValue(GetAuraCurse(d.AuraSelf3));
                int charges3 = !string.IsNullOrEmpty(d.Aura3) ? System.Math.Max(d.Aura3Charges, d.AuraSelf3Charges) : d.AuraSelf3Charges;
                t.Field("auraCharges3").SetValue(charges3);
            }
            if (!string.IsNullOrEmpty(d.CurseSelf2))
            {
                t.Field("curseSelf2").SetValue(GetAuraCurse(d.CurseSelf2));
                int ccharges2 = !string.IsNullOrEmpty(d.Curse2) ? System.Math.Max(d.Curse2Charges, d.CurseSelf2Charges) : d.CurseSelf2Charges;
                t.Field("curseCharges2").SetValue(ccharges2);
            }
            if (!string.IsNullOrEmpty(d.CurseSelf3))
            {
                t.Field("curseSelf3").SetValue(GetAuraCurse(d.CurseSelf3));
                t.Field("curseCharges3").SetValue(System.Math.Max(d.Curse3Charges, d.CurseSelf3Charges));
            }
            if (d.CurseChargesSides != 0) t.Field("curseChargesSides").SetValue(d.CurseChargesSides);

            // ── Heal extras ──
            if (d.HealSides != 0) t.Field("healSides").SetValue(d.HealSides);
            if (d.HealEnergyBonus != 0) t.Field("healEnergyBonus").SetValue(d.HealEnergyBonus);
            if (!string.IsNullOrEmpty(d.HealAuraCurseSelf)) t.Field("healAuraCurseSelf").SetValue(GetAuraCurse(d.HealAuraCurseSelf));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName)) t.Field("healAuraCurseName").SetValue(GetAuraCurse(d.HealAuraCurseName));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName2)) t.Field("healAuraCurseName2").SetValue(GetAuraCurse(d.HealAuraCurseName2));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName3)) t.Field("healAuraCurseName3").SetValue(GetAuraCurse(d.HealAuraCurseName3));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName4)) t.Field("healAuraCurseName4").SetValue(GetAuraCurse(d.HealAuraCurseName4));

            // ── Effect / targeting extras ──
            if (d.EffectRepeatTarget != Enums.EffectRepeatTarget.NoRepeat)
                t.Field("effectRepeatTarget").SetValue(d.EffectRepeatTarget);
            if (d.EffectRepeatDelay != 0f) t.Field("effectRepeatDelay").SetValue(d.EffectRepeatDelay);
            if (d.EffectRepeatEnergyBonus != 0) t.Field("effectRepeatEnergyBonus").SetValue(d.EffectRepeatEnergyBonus);
            if (d.EffectRepeatMaxBonus != 0) t.Field("effectRepeatMaxBonus").SetValue(d.EffectRepeatMaxBonus);
            if (d.EffectRepeatModificator != 0) t.Field("effectRepeatModificator").SetValue(d.EffectRepeatModificator);

            // ── Push/pull/draw ──
            if (d.PushTarget != 0) t.Field("pushTarget").SetValue(d.PushTarget);
            if (d.PullTarget != 0) t.Field("pullTarget").SetValue(d.PullTarget);
            if (d.DrawCard != 0) t.Field("drawCard").SetValue(d.DrawCard);
            if (d.DiscardCard != 0) t.Field("discardCard").SetValue(d.DiscardCard);
            if (d.EnergyRecharge != 0) t.Field("energyRecharge").SetValue(d.EnergyRecharge);
            if (d.GoldGainQuantity != 0) t.Field("goldGainQuantity").SetValue(d.GoldGainQuantity);
            if (d.ShardsGainQuantity != 0) t.Field("shardsGainQuantity").SetValue(d.ShardsGainQuantity);
            if (d.ExhaustCounter != 0) t.Field("exhaustCounter").SetValue(d.ExhaustCounter);
            if (!string.IsNullOrEmpty(d.EffectRequired)) t.Field("effectRequired").SetValue(d.EffectRequired);
            if (d.SelfKillHiddenSeconds != 0f) t.Field("selfKillHiddenSeconds").SetValue(d.SelfKillHiddenSeconds);

            // ── Lifesteal ──
            if (d.HealSelfPerDamageDonePercent != 0f) t.Field("healSelfPerDamageDonePercent").SetValue(d.HealSelfPerDamageDonePercent);

            // ── AC manipulation ──
            if (d.TransferCurses != 0) t.Field("transferCurses").SetValue(d.TransferCurses);
            if (d.StealAuras != 0) t.Field("stealAuras").SetValue(d.StealAuras);
            if (d.ReduceCurses != 0) t.Field("reduceCurses").SetValue(d.ReduceCurses);
            if (d.ReduceAuras != 0) t.Field("reduceAuras").SetValue(d.ReduceAuras);
            if (d.IncreaseCurses != 0) t.Field("increaseCurses").SetValue(d.IncreaseCurses);
            if (d.IncreaseAuras != 0) t.Field("increaseAuras").SetValue(d.IncreaseAuras);

            // ── Discard options ──
            if (d.DiscardCardType != Enums.CardType.None) t.Field("discardCardType").SetValue(d.DiscardCardType);
            if (d.DiscardCardTypeAux != null && d.DiscardCardTypeAux.Length > 0) t.Field("discardCardTypeAux").SetValue(d.DiscardCardTypeAux);
            if (d.DiscardCardAutomatic) t.Field("discardCardAutomatic").SetValue(true);
            if (d.DiscardCardPlace != Enums.CardPlace.Discard) t.Field("discardCardPlace").SetValue(d.DiscardCardPlace);

            // ── Add card ──
            if (d.AddCard != 0) t.Field("addCard").SetValue(d.AddCard);
            if (!string.IsNullOrEmpty(d.AddCardId)) t.Field("addCardId").SetValue(d.AddCardId);
            if (d.AddCardType != Enums.CardType.None) t.Field("addCardType").SetValue(d.AddCardType);
            if (d.AddCardTypeAux != null && d.AddCardTypeAux.Length > 0) t.Field("addCardTypeAux").SetValue(d.AddCardTypeAux);
            if (d.AddCardChoose != 0) t.Field("addCardChoose").SetValue(d.AddCardChoose);
            if (d.AddCardFrom != Enums.CardFrom.Game) t.Field("addCardFrom").SetValue(d.AddCardFrom);
            if (d.AddCardPlace != Enums.CardPlace.Hand) t.Field("addCardPlace").SetValue(d.AddCardPlace);
            if (d.AddCardReducedCost != 0) t.Field("addCardReducedCost").SetValue(d.AddCardReducedCost);
            if (d.AddCardCostTurn) t.Field("addCardCostTurn").SetValue(true);
            if (d.AddCardVanish) t.Field("addCardVanish").SetValue(true);
            if (d.AddCardOnlyCheckAuxTypes) t.Field("addCardOnlyCheckAuxTypes").SetValue(true);
            if (d.AddCardFromVanishPile) t.Field("addCardFromVanishPile").SetValue(true);
            if (d.AddVanishToDeck) t.Field("addVanishToDeck").SetValue(true);
            if (d.AddCardList != null && d.AddCardList.Count > 0)
            {
                var list = new CardData[d.AddCardList.Count];
                for (int i = 0; i < d.AddCardList.Count; i++)
                    list[i] = GetCard(d.AddCardList[i]);
                t.Field("addCardList").SetValue(list);
            }

            // ── Look (Scry) ──
            if (d.LookCards != 0) t.Field("lookCards").SetValue(d.LookCards);
            if (d.LookCardsDiscardUpTo != 0) t.Field("lookCardsDiscardUpTo").SetValue(d.LookCardsDiscardUpTo);
            if (d.LookCardsVanishUpTo != 0) t.Field("lookCardsVanishUpTo").SetValue(d.LookCardsVanishUpTo);

            // ── Summon extras ──
            if (!string.IsNullOrEmpty(d.SummonAura))
            {
                t.Field("summonAura").SetValue(GetAuraCurse(d.SummonAura));
                t.Field("summonAuraCharges").SetValue(d.SummonAuraCharges);
            }
            if (!string.IsNullOrEmpty(d.SummonAura2))
            {
                t.Field("summonAura2").SetValue(GetAuraCurse(d.SummonAura2));
                t.Field("summonAuraCharges2").SetValue(d.SummonAuraCharges2);
            }
            if (!string.IsNullOrEmpty(d.SummonAura3))
            {
                t.Field("summonAura3").SetValue(GetAuraCurse(d.SummonAura3));
                t.Field("summonAuraCharges3").SetValue(d.SummonAuraCharges3);
            }
            if (d.Evolve) t.Field("evolve").SetValue(true);
            if (d.Metamorph) t.Field("metamorph").SetValue(true);

            // ── AC Energy Bonus ──
            if (!string.IsNullOrEmpty(d.AcEnergyBonus))
            {
                t.Field("acEnergyBonus").SetValue(GetAuraCurse(d.AcEnergyBonus));
                t.Field("acEnergyBonusQuantity").SetValue(d.AcEnergyBonusQuantity);
            }
            if (!string.IsNullOrEmpty(d.AcEnergyBonus2))
            {
                t.Field("acEnergyBonus2").SetValue(GetAuraCurse(d.AcEnergyBonus2));
                t.Field("acEnergyBonus2Quantity").SetValue(d.AcEnergyBonus2Quantity);
            }
            if (d.ChooseOneOfAvailableAuras) t.Field("chooseOneOfAvailableAuras").SetValue(true);

            // ── Special Value System ──
            if (d.SpecialValueGlobal != Enums.CardSpecialValue.None)
            {
                t.Field("specialValueGlobal").SetValue(d.SpecialValueGlobal);
                t.Field("specialValueModifierGlobal").SetValue(d.SpecialValueModifierGlobal);
                if (!string.IsNullOrEmpty(d.SpecialAuraCurseNameGlobal))
                    t.Field("specialAuraCurseNameGlobal").SetValue(GetAuraCurse(d.SpecialAuraCurseNameGlobal));
            }
            if (d.SpecialValue1 != Enums.CardSpecialValue.None)
            {
                t.Field("specialValue1").SetValue(d.SpecialValue1);
                t.Field("specialValueModifier1").SetValue(d.SpecialValueModifier1);
                if (!string.IsNullOrEmpty(d.SpecialAuraCurseName1))
                    t.Field("specialAuraCurseName1").SetValue(GetAuraCurse(d.SpecialAuraCurseName1));
            }
            if (d.SpecialValue2 != Enums.CardSpecialValue.None)
            {
                t.Field("specialValue2").SetValue(d.SpecialValue2);
                t.Field("specialValueModifier2").SetValue(d.SpecialValueModifier2);
                if (!string.IsNullOrEmpty(d.SpecialAuraCurseName2))
                    t.Field("specialAuraCurseName2").SetValue(GetAuraCurse(d.SpecialAuraCurseName2));
            }

            // ── Special Value Scaling Flags ──
            if (d.DamageSpecialValueGlobal) t.Field("damageSpecialValueGlobal").SetValue(true);
            if (d.DamageSpecialValue1) t.Field("damageSpecialValue1").SetValue(true);
            if (d.DamageSpecialValue2) t.Field("damageSpecialValue2").SetValue(true);
            if (d.Damage2SpecialValueGlobal) t.Field("damage2SpecialValueGlobal").SetValue(true);
            if (d.Damage2SpecialValue1) t.Field("damage2SpecialValue1").SetValue(true);
            if (d.Damage2SpecialValue2) t.Field("damage2SpecialValue2").SetValue(true);
            if (d.HealSpecialValueGlobal) t.Field("healSpecialValueGlobal").SetValue(true);
            if (d.HealSpecialValue1) t.Field("healSpecialValue1").SetValue(true);
            if (d.HealSpecialValue2) t.Field("healSpecialValue2").SetValue(true);
            if (d.HealSelfSpecialValueGlobal) t.Field("healSelfSpecialValueGlobal").SetValue(true);
            if (d.HealSelfSpecialValue1) t.Field("healSelfSpecialValue1").SetValue(true);
            if (d.HealSelfSpecialValue2) t.Field("healSelfSpecialValue2").SetValue(true);
            if (d.SelfHealthLossSpecialGlobal) t.Field("selfHealthLossSpecialGlobal").SetValue(true);
            if (d.SelfHealthLossSpecialValue1) t.Field("selfHealthLossSpecialValue1").SetValue(true);
            if (d.SelfHealthLossSpecialValue2) t.Field("selfHealthLossSpecialValue2").SetValue(true);
            if (d.EnergyRechargeSpecialValueGlobal) t.Field("energyRechargeSpecialValueGlobal").SetValue(true);
            if (d.DrawCardSpecialValueGlobal) t.Field("drawCardSpecialValueGlobal").SetValue(true);
            if (d.AuraChargesSpecialValueGlobal) t.Field("auraChargesSpecialValueGlobal").SetValue(true);
            if (d.AuraChargesSpecialValue1) t.Field("auraChargesSpecialValue1").SetValue(true);
            if (d.AuraChargesSpecialValue2) t.Field("auraChargesSpecialValue2").SetValue(true);
            if (d.AuraCharges2SpecialValueGlobal) t.Field("auraCharges2SpecialValueGlobal").SetValue(true);
            if (d.AuraCharges2SpecialValue1) t.Field("auraCharges2SpecialValue1").SetValue(true);
            if (d.AuraCharges2SpecialValue2) t.Field("auraCharges2SpecialValue2").SetValue(true);
            if (d.AuraCharges3SpecialValueGlobal) t.Field("auraCharges3SpecialValueGlobal").SetValue(true);
            if (d.AuraCharges3SpecialValue1) t.Field("auraCharges3SpecialValue1").SetValue(true);
            if (d.AuraCharges3SpecialValue2) t.Field("auraCharges3SpecialValue2").SetValue(true);
            if (d.CurseChargesSpecialValueGlobal) t.Field("curseChargesSpecialValueGlobal").SetValue(true);
            if (d.CurseChargesSpecialValue1) t.Field("curseChargesSpecialValue1").SetValue(true);
            if (d.CurseChargesSpecialValue2) t.Field("curseChargesSpecialValue2").SetValue(true);
            if (d.CurseCharges2SpecialValueGlobal) t.Field("curseCharges2SpecialValueGlobal").SetValue(true);
            if (d.CurseCharges2SpecialValue1) t.Field("curseCharges2SpecialValue1").SetValue(true);
            if (d.CurseCharges2SpecialValue2) t.Field("curseCharges2SpecialValue2").SetValue(true);
            if (d.CurseCharges3SpecialValueGlobal) t.Field("curseCharges3SpecialValueGlobal").SetValue(true);
            if (d.CurseCharges3SpecialValue1) t.Field("curseCharges3SpecialValue1").SetValue(true);
            if (d.CurseCharges3SpecialValue2) t.Field("curseCharges3SpecialValue2").SetValue(true);

            // ── FX / Effects ──
            if (!string.IsNullOrEmpty(d.EffectPreAction)) t.Field("effectPreAction").SetValue(d.EffectPreAction);
            if (d.EffectPostCastDelay != 0f) t.Field("effectPostCastDelay").SetValue(d.EffectPostCastDelay);
            if (d.EffectCasterRepeat) t.Field("effectCasterRepeat").SetValue(true);
            if (d.EffectCastCenter) t.Field("effectCastCenter").SetValue(true);
            if (!string.IsNullOrEmpty(d.EffectTrail)) t.Field("effectTrail").SetValue(d.EffectTrail);
            if (d.EffectTrailRepeat) t.Field("effectTrailRepeat").SetValue(true);
            if (d.EffectTrailSpeed != 0f) t.Field("effectTrailSpeed").SetValue(d.EffectTrailSpeed);
            if (d.EffectTrailAngle != Enums.EffectTrailAngle.Horizontal) t.Field("effectTrailAngle").SetValue(d.EffectTrailAngle);
            if (d.EffectPostTargetDelay != 0f) t.Field("effectPostTargetDelay").SetValue(d.EffectPostTargetDelay);

            // ── Pet System ──
            if (d.PetActivation != Enums.ActivePets.None) t.Field("petActivation").SetValue(d.PetActivation);
            if (d.PetBonusDamageType != Enums.DamageType.None) t.Field("petBonusDamageType").SetValue(d.PetBonusDamageType);
            if (d.PetBonusDamageAmount != 0) t.Field("petBonusDamageAmount").SetValue(d.PetBonusDamageAmount);
            if (d.IsPetAttack) t.Field("isPetAttack").SetValue(true);
            if (d.IsPetCast) t.Field("isPetCast").SetValue(true);
            if (d.KillPet) t.Field("killPet").SetValue(true);
            if (d.PetTemporal) t.Field("petTemporal").SetValue(true);
            if (d.PetTemporalAttack) t.Field("petTemporalAttack").SetValue(true);
            if (d.PetTemporalCast) t.Field("petTemporalCast").SetValue(true);
            if (d.PetTemporalMoveToCenter) t.Field("petTemporalMoveToCenter").SetValue(true);
            if (d.PetTemporalMoveToBack) t.Field("petTemporalMoveToBack").SetValue(true);
            if (d.PetTemporalFadeOutDelay != 0f) t.Field("petTemporalFadeOutDelay").SetValue(d.PetTemporalFadeOutDelay);

            // ── Fluff / Description ──
            if (!string.IsNullOrEmpty(d.Fluff)) t.Field("fluff").SetValue(d.Fluff);
            if (d.FluffPercent != 0f) t.Field("fluffPercent").SetValue(d.FluffPercent);
        }

        /// <summary>Create an upgraded "A" variant of a base card with increased stats.</summary>
        public static CardData MakeUpgradedCard(CardData baseCard, string newId, string newName,
            float damageMult = 1.3f, int bonusCurseCharges = 1, int bonusAuraCharges = 1, int bonusHeal = 3)
        {
            var card = Object.Instantiate(baseCard);
            var t = Traverse.Create(card);

            t.Field("id").SetValue(newId);
            t.Field("cardName").SetValue(newName);
            t.Field("cardUpgraded").SetValue(Enums.CardUpgraded.A);

            int dmg = (int)(t.Field("damage").GetValue<int>() * damageMult);
            if (dmg > 0) t.Field("damage").SetValue(dmg);
            int dmg2 = (int)(t.Field("damage2").GetValue<int>() * damageMult);
            if (dmg2 > 0) t.Field("damage2").SetValue(dmg2);

            int cc = t.Field("curseCharges").GetValue<int>();
            if (cc > 0) t.Field("curseCharges").SetValue(cc + bonusCurseCharges);
            int cc2 = t.Field("curseCharges2").GetValue<int>();
            if (cc2 > 0) t.Field("curseCharges2").SetValue(cc2 + bonusCurseCharges);
            int ac = t.Field("auraCharges").GetValue<int>();
            if (ac > 0) t.Field("auraCharges").SetValue(ac + bonusAuraCharges);

            int h = t.Field("heal").GetValue<int>();
            if (h > 0) t.Field("heal").SetValue(h + bonusHeal);

            return card;
        }

        /// <summary>Create the paired CardData (equipment card) for an ItemDef + ItemData.</summary>
        public static CardData MakeItemCard(ItemDef d, ItemData itemData)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            var t = Traverse.Create(card);

            t.Field("id").SetValue(d.Id);
            t.Field("cardName").SetValue(d.Name);
            t.Field("cardClass").SetValue(Enums.CardClass.Item);
            t.Field("cardRarity").SetValue(d.Rarity);
            t.Field("cardType").SetValue(d.CardType);
            t.Field("cardUpgraded").SetValue(Enums.CardUpgraded.No);
            t.Field("playable").SetValue(false);
            t.Field("visible").SetValue(true);
            t.Field("showInTome").SetValue(false);
            t.Field("energyCost").SetValue(0);
            t.Field("item").SetValue(itemData);

            // Prevent NREs
            t.Field("internalId").SetValue(d.Id);
            t.Field("sku").SetValue("");
            t.Field("relatedCard").SetValue("");
            t.Field("relatedCard2").SetValue("");
            t.Field("relatedCard3").SetValue("");
            t.Field("cardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("discardCardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("addCardTypeAux").SetValue(new Enums.CardType[0]);
            t.Field("addCardList").SetValue(new CardData[0]);
            t.Field("preDescriptionArgs").SetValue(new string[0]);
            t.Field("descriptionArgs").SetValue(new string[0]);
            t.Field("postDescriptionArgs").SetValue(new string[0]);
            t.Field("targetSide").SetValue(Enums.CardTargetSide.Anyone);
            t.Field("targetType").SetValue(Enums.CardTargetType.Single);
            t.Field("targetPosition").SetValue(Enums.CardTargetPosition.Anywhere);

            // Copy card art sprite from an existing card
            if (!string.IsNullOrEmpty(d.SpriteSource))
                CopyCardVisuals(card, d.SpriteSource);

            // Auto-generate item description
            try { card.SetDescriptionNew(forceDescription: true); }
            catch { /* Texts not ready — description will be empty until next rebuild */ }

            return card;
        }
    }
}
