using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class ModProjectBuilder
    {
        // ═══════════════════════════════════════════════════════════════
        //  CARDS / ITEMS
        // ═══════════════════════════════════════════════════════════════

        private static void BuildCards(Dictionary<string, CardDef> defs, bool isNew)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var card = MakeFullCard(kvp.Value);
                    DataHelper.RegisterCard(card);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Card '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void BuildItems(Dictionary<string, ItemDef> defs)
        {
            foreach (var kvp in defs)
            {
                try
                {
                    var item = DataHelper.MakeFullItem(kvp.Value);
                    DataHelper.RegisterItem(item);
                    // Also register the paired equipment card
                    var card = DataHelper.MakeItemCard(kvp.Value, item);
                    DataHelper.RegisterCard(card);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Builder] Failed to build Item '{kvp.Key}': {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CARD: Full Build + Snapshot
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create a complete CardData SO from a CardDef, setting ALL fields.
        /// This replaces the old MakeCard+ApplyCardExtras flow for mod-level cards.
        /// </summary>
        public static CardData MakeFullCard(CardDef d)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            var t = Traverse.Create(card);

            // ── Identity ─────────────────────────────────────────
            t.Field("id").SetValue(d.Id);
            t.Field("internalId").SetValue(d.Id);
            t.Field("cardName").SetValue(d.Name ?? "");
            t.Field("description").SetValue(d.Description ?? "");
            t.Field("fluff").SetValue(d.Fluff ?? "");
            t.Field("fluffPercent").SetValue(d.FluffPercent);
            t.Field("sku").SetValue(d.Sku ?? "");

            // ── Classification ───────────────────────────────────
            t.Field("cardUpgraded").SetValue(d.CardUpgraded);
            t.Field("cardRarity").SetValue(d.CardRarity);
            t.Field("cardType").SetValue(d.CardType);
            t.Field("cardTypeAux").SetValue(d.CardTypeAux ?? Array.Empty<Enums.CardType>());
            t.Field("cardClass").SetValue(d.CardClass);
            t.Field("cardNumber").SetValue(d.CardNumber);
            t.Field("maxInDeck").SetValue(d.MaxInDeck);

            // ── Cost / Economy ───────────────────────────────────
            t.Field("energyCost").SetValue(d.EnergyCost);
            t.Field("energyCostOriginal").SetValue(d.EnergyCostOriginal);
            t.Field("energyCostForShow").SetValue(d.EnergyCostForShow);
            t.Field("energyReductionPermanent").SetValue(d.EnergyReductionPermanent);
            t.Field("energyReductionTemporal").SetValue(d.EnergyReductionTemporal);
            t.Field("energyReductionToZeroPermanent").SetValue(d.EnergyReductionToZeroPermanent);
            t.Field("energyReductionToZeroTemporal").SetValue(d.EnergyReductionToZeroTemporal);

            // ── Flags ────────────────────────────────────────────
            t.Field("playable").SetValue(d.Playable);
            t.Field("visible").SetValue(d.Visible);
            t.Field("showInTome").SetValue(d.ShowInTome);
            t.Field("autoplayDraw").SetValue(d.AutoplayDraw);
            t.Field("autoplayEndTurn").SetValue(d.AutoplayEndTurn);
            t.Field("vanish").SetValue(d.Vanish);
            t.Field("innate").SetValue(d.Innate);
            t.Field("lazy").SetValue(d.Lazy);
            t.Field("corrupted").SetValue(d.Corrupted);
            t.Field("endTurn").SetValue(d.EndTurn);
            t.Field("starter").SetValue(d.Starter);
            t.Field("flipSprite").SetValue(d.FlipSprite);
            t.Field("modifiedByTrait").SetValue(d.ModifiedByTrait);
            t.Field("onlyInWeekly").SetValue(d.OnlyInWeekly);

            // ── Upgrade Paths ────────────────────────────────────
            t.Field("upgradesTo1").SetValue(d.UpgradesTo1 ?? "");
            t.Field("upgradesTo2").SetValue(d.UpgradesTo2 ?? "");
            t.Field("upgradedFrom").SetValue(d.UpgradedFrom ?? "");
            t.Field("baseCard").SetValue(d.BaseCard ?? "");
            t.Field("relatedCard").SetValue(d.RelatedCard ?? "");
            t.Field("relatedCard2").SetValue(d.RelatedCard2 ?? "");
            t.Field("relatedCard3").SetValue(d.RelatedCard3 ?? "");
            if (!string.IsNullOrEmpty(d.UpgradesToRareId))
            {
                var rare = DataHelper.GetCard(d.UpgradesToRareId);
                if (rare != null) t.Field("upgradesToRare").SetValue(rare);
            }

            // ── Targeting ────────────────────────────────────────
            t.Field("targetSide").SetValue(d.TargetSide);
            t.Field("targetType").SetValue(d.TargetType);
            t.Field("targetPosition").SetValue(d.TargetPos);

            // ── Damage 1 ─────────────────────────────────────────
            t.Field("damage").SetValue(d.Damage);
            t.Field("damageType").SetValue(d.DamageType);
            t.Field("damageSides").SetValue(d.DamageSides);
            t.Field("damageEnergyBonus").SetValue(d.DamageEnergyBonus);
            t.Field("ignoreBlock").SetValue(d.IgnoreBlock);
            t.Field("damageSelf").SetValue(d.DamageSelf);
            t.Field("damageSpecialValueGlobal").SetValue(d.DamageSpecialValueGlobal);
            t.Field("damageSpecialValue1").SetValue(d.DamageSpecialValue1);
            t.Field("damageSpecialValue2").SetValue(d.DamageSpecialValue2);

            // ── Damage 2 ─────────────────────────────────────────
            t.Field("damage2").SetValue(d.Damage2);
            t.Field("damageType2").SetValue(d.DamageType2);
            t.Field("damageSides2").SetValue(d.DamageSides2);
            t.Field("ignoreBlock2").SetValue(d.IgnoreBlock2);
            t.Field("damageSelf2").SetValue(d.DamageSelf2);
            t.Field("damage2SpecialValueGlobal").SetValue(d.Damage2SpecialValueGlobal);
            t.Field("damage2SpecialValue1").SetValue(d.Damage2SpecialValue1);
            t.Field("damage2SpecialValue2").SetValue(d.Damage2SpecialValue2);

            // ── Self HP Loss ─────────────────────────────────────
            t.Field("selfHealthLoss").SetValue(d.SelfHealthLoss);
            t.Field("selfHealthLossSpecialGlobal").SetValue(d.SelfHealthLossSpecialGlobal);
            t.Field("selfHealthLossSpecialValue1").SetValue(d.SelfHealthLossSpecialValue1);
            t.Field("selfHealthLossSpecialValue2").SetValue(d.SelfHealthLossSpecialValue2);
            t.Field("selfKillHiddenSeconds").SetValue(d.SelfKillHiddenSeconds);

            // ── Curses (target) ──────────────────────────────────
            if (!string.IsNullOrEmpty(d.Curse))
                t.Field("curse").SetValue(DataHelper.GetAuraCurse(d.Curse));
            t.Field("curseCharges").SetValue(d.CurseCharges);
            t.Field("curseChargesSides").SetValue(d.CurseChargesSides);
            t.Field("curseChargesSpecialValueGlobal").SetValue(d.CurseChargesSpecialValueGlobal);
            t.Field("curseChargesSpecialValue1").SetValue(d.CurseChargesSpecialValue1);
            t.Field("curseChargesSpecialValue2").SetValue(d.CurseChargesSpecialValue2);
            if (!string.IsNullOrEmpty(d.CurseSelf))
                t.Field("curseSelf").SetValue(DataHelper.GetAuraCurse(d.CurseSelf));
            t.Field("curseCharges").SetValue(d.CurseCharges);

            if (!string.IsNullOrEmpty(d.Curse2))
                t.Field("curse2").SetValue(DataHelper.GetAuraCurse(d.Curse2));
            t.Field("curseCharges2").SetValue(d.Curse2Charges);
            t.Field("curseCharges2SpecialValueGlobal").SetValue(d.CurseCharges2SpecialValueGlobal);
            t.Field("curseCharges2SpecialValue1").SetValue(d.CurseCharges2SpecialValue1);
            t.Field("curseCharges2SpecialValue2").SetValue(d.CurseCharges2SpecialValue2);
            if (!string.IsNullOrEmpty(d.CurseSelf2))
                t.Field("curseSelf2").SetValue(DataHelper.GetAuraCurse(d.CurseSelf2));
            if (d.CurseSelf2Charges > 0 && d.Curse2Charges == 0)
                t.Field("curseCharges2").SetValue(d.CurseSelf2Charges);

            if (!string.IsNullOrEmpty(d.Curse3))
                t.Field("curse3").SetValue(DataHelper.GetAuraCurse(d.Curse3));
            t.Field("curseCharges3").SetValue(d.Curse3Charges);
            t.Field("curseCharges3SpecialValueGlobal").SetValue(d.CurseCharges3SpecialValueGlobal);
            t.Field("curseCharges3SpecialValue1").SetValue(d.CurseCharges3SpecialValue1);
            t.Field("curseCharges3SpecialValue2").SetValue(d.CurseCharges3SpecialValue2);
            if (!string.IsNullOrEmpty(d.CurseSelf3))
                t.Field("curseSelf3").SetValue(DataHelper.GetAuraCurse(d.CurseSelf3));
            if (d.CurseSelf3Charges > 0 && d.Curse3Charges == 0)
                t.Field("curseCharges3").SetValue(d.CurseSelf3Charges);

            // ── Auras (target) ───────────────────────────────────
            if (!string.IsNullOrEmpty(d.Aura))
                t.Field("aura").SetValue(DataHelper.GetAuraCurse(d.Aura));
            t.Field("auraCharges").SetValue(d.AuraCharges);
            t.Field("auraChargesSpecialValueGlobal").SetValue(d.AuraChargesSpecialValueGlobal);
            t.Field("auraChargesSpecialValue1").SetValue(d.AuraChargesSpecialValue1);
            t.Field("auraChargesSpecialValue2").SetValue(d.AuraChargesSpecialValue2);
            if (!string.IsNullOrEmpty(d.AuraSelf))
                t.Field("auraSelf").SetValue(DataHelper.GetAuraCurse(d.AuraSelf));
            if (d.AuraSelfCharges > 0 && d.AuraCharges == 0)
                t.Field("auraCharges").SetValue(d.AuraSelfCharges);

            if (!string.IsNullOrEmpty(d.Aura2))
                t.Field("aura2").SetValue(DataHelper.GetAuraCurse(d.Aura2));
            t.Field("auraCharges2").SetValue(d.Aura2Charges);
            t.Field("auraCharges2SpecialValueGlobal").SetValue(d.AuraCharges2SpecialValueGlobal);
            t.Field("auraCharges2SpecialValue1").SetValue(d.AuraCharges2SpecialValue1);
            t.Field("auraCharges2SpecialValue2").SetValue(d.AuraCharges2SpecialValue2);
            if (!string.IsNullOrEmpty(d.AuraSelf2))
                t.Field("auraSelf2").SetValue(DataHelper.GetAuraCurse(d.AuraSelf2));
            if (d.AuraSelf2Charges > 0 && d.Aura2Charges == 0)
                t.Field("auraCharges2").SetValue(d.AuraSelf2Charges);

            if (!string.IsNullOrEmpty(d.Aura3))
                t.Field("aura3").SetValue(DataHelper.GetAuraCurse(d.Aura3));
            t.Field("auraCharges3").SetValue(d.Aura3Charges);
            t.Field("auraCharges3SpecialValueGlobal").SetValue(d.AuraCharges3SpecialValueGlobal);
            t.Field("auraCharges3SpecialValue1").SetValue(d.AuraCharges3SpecialValue1);
            t.Field("auraCharges3SpecialValue2").SetValue(d.AuraCharges3SpecialValue2);
            if (!string.IsNullOrEmpty(d.AuraSelf3))
                t.Field("auraSelf3").SetValue(DataHelper.GetAuraCurse(d.AuraSelf3));
            if (d.AuraSelf3Charges > 0 && d.Aura3Charges == 0)
                t.Field("auraCharges3").SetValue(d.AuraSelf3Charges);

            // ── Heal ─────────────────────────────────────────────
            t.Field("heal").SetValue(d.Heal);
            t.Field("healSides").SetValue(d.HealSides);
            t.Field("healSelf").SetValue(d.HealSelf);
            t.Field("healEnergyBonus").SetValue(d.HealEnergyBonus);
            t.Field("healSelfPerDamageDonePercent").SetValue(d.HealSelfPerDamageDonePercent);
            t.Field("healCurses").SetValue(d.HealCurses);
            t.Field("dispelAuras").SetValue(d.DispelAuras);
            t.Field("healSpecialValueGlobal").SetValue(d.HealSpecialValueGlobal);
            t.Field("healSpecialValue1").SetValue(d.HealSpecialValue1);
            t.Field("healSpecialValue2").SetValue(d.HealSpecialValue2);
            t.Field("healSelfSpecialValueGlobal").SetValue(d.HealSelfSpecialValueGlobal);
            t.Field("healSelfSpecialValue1").SetValue(d.HealSelfSpecialValue1);
            t.Field("healSelfSpecialValue2").SetValue(d.HealSelfSpecialValue2);

            if (!string.IsNullOrEmpty(d.HealAuraCurseSelf))
                t.Field("healAuraCurseSelf").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseSelf));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName))
                t.Field("healAuraCurseName").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseName));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName2))
                t.Field("healAuraCurseName2").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseName2));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName3))
                t.Field("healAuraCurseName3").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseName3));
            if (!string.IsNullOrEmpty(d.HealAuraCurseName4))
                t.Field("healAuraCurseName4").SetValue(DataHelper.GetAuraCurse(d.HealAuraCurseName4));

            // ── AC Manipulation ──────────────────────────────────
            t.Field("transferCurses").SetValue(d.TransferCurses);
            t.Field("stealAuras").SetValue(d.StealAuras);
            t.Field("reduceCurses").SetValue(d.ReduceCurses);
            t.Field("reduceAuras").SetValue(d.ReduceAuras);
            t.Field("increaseCurses").SetValue(d.IncreaseCurses);
            t.Field("increaseAuras").SetValue(d.IncreaseAuras);

            // ── Effect Repeat ────────────────────────────────────
            t.Field("effectRepeat").SetValue(d.EffectRepeat);
            t.Field("effectRepeatDelay").SetValue(d.EffectRepeatDelay);
            t.Field("effectRepeatEnergyBonus").SetValue(d.EffectRepeatEnergyBonus);
            t.Field("effectRepeatMaxBonus").SetValue(d.EffectRepeatMaxBonus);
            t.Field("effectRepeatModificator").SetValue(d.EffectRepeatModificator);
            t.Field("effectRepeatTarget").SetValue(d.EffectRepeatTarget);

            // ── Misc Mechanics ───────────────────────────────────
            t.Field("moveToCenter").SetValue(d.MoveToCenter);
            t.Field("pushTarget").SetValue(d.PushTarget);
            t.Field("pullTarget").SetValue(d.PullTarget);
            t.Field("drawCard").SetValue(d.DrawCard);
            t.Field("drawCardSpecialValueGlobal").SetValue(d.DrawCardSpecialValueGlobal);
            t.Field("discardCard").SetValue(d.DiscardCard);
            t.Field("energyRecharge").SetValue(d.EnergyRecharge);
            t.Field("energyRechargeSpecialValueGlobal").SetValue(d.EnergyRechargeSpecialValueGlobal);
            t.Field("goldGainQuantity").SetValue(d.GoldGainQuantity);
            t.Field("shardsGainQuantity").SetValue(d.ShardsGainQuantity);
            t.Field("exhaustCounter").SetValue(d.ExhaustCounter);
            t.Field("effectRequired").SetValue(d.EffectRequired ?? "");

            // ── Discard Options ──────────────────────────────────
            t.Field("discardCardType").SetValue(d.DiscardCardType);
            t.Field("discardCardTypeAux").SetValue(d.DiscardCardTypeAux ?? Array.Empty<Enums.CardType>());
            t.Field("discardCardAutomatic").SetValue(d.DiscardCardAutomatic);
            t.Field("discardCardPlace").SetValue(d.DiscardCardPlace);

            // ── Add Card ─────────────────────────────────────────
            t.Field("addCard").SetValue(d.AddCard);
            t.Field("addCardId").SetValue(d.AddCardId ?? "");
            t.Field("addCardType").SetValue(d.AddCardType);
            t.Field("addCardTypeAux").SetValue(d.AddCardTypeAux ?? Array.Empty<Enums.CardType>());
            t.Field("addCardChoose").SetValue(d.AddCardChoose);
            t.Field("addCardFrom").SetValue(d.AddCardFrom);
            t.Field("addCardPlace").SetValue(d.AddCardPlace);
            t.Field("addCardReducedCost").SetValue(d.AddCardReducedCost);
            t.Field("addCardCostTurn").SetValue(d.AddCardCostTurn);
            t.Field("addCardVanish").SetValue(d.AddCardVanish);
            t.Field("addCardOnlyCheckAuxTypes").SetValue(d.AddCardOnlyCheckAuxTypes);
            card.AddCardFromVanishPile = d.AddCardFromVanishPile;
            t.Field("addVanishToDeck").SetValue(d.AddVanishToDeck);

            // AddCardList: resolve IDs to CardData[]
            if (d.AddCardList != null && d.AddCardList.Count > 0)
            {
                var resolved = new List<CardData>();
                foreach (var cid in d.AddCardList)
                {
                    var c = DataHelper.GetCard(cid);
                    if (c != null) resolved.Add(c);
                }
                t.Field("addCardList").SetValue(resolved.ToArray());
            }
            else
            {
                t.Field("addCardList").SetValue(new CardData[0]);
            }

            // ── Look / Scry ─────────────────────────────────────
            t.Field("lookCards").SetValue(d.LookCards);
            t.Field("lookCardsDiscardUpTo").SetValue(d.LookCardsDiscardUpTo);
            t.Field("lookCardsVanishUpTo").SetValue(d.LookCardsVanishUpTo);

            // ── Summon ───────────────────────────────────────────
            if (!string.IsNullOrEmpty(d.SummonUnitId))
            {
                var npc = DataHelper.GetExistingNPC(d.SummonUnitId);
                if (npc != null) t.Field("summonUnit").SetValue(npc);
            }
            t.Field("summonUnitNum").SetValue(d.SummonNum);
            t.Field("evolve").SetValue(d.Evolve);
            t.Field("metamorph").SetValue(d.Metamorph);
            if (!string.IsNullOrEmpty(d.SummonAura))
                t.Field("summonAura").SetValue(DataHelper.GetAuraCurse(d.SummonAura));
            t.Field("summonAuraCharges").SetValue(d.SummonAuraCharges);
            if (!string.IsNullOrEmpty(d.SummonAura2))
                t.Field("summonAura2").SetValue(DataHelper.GetAuraCurse(d.SummonAura2));
            t.Field("summonAuraCharges2").SetValue(d.SummonAuraCharges2);
            if (!string.IsNullOrEmpty(d.SummonAura3))
                t.Field("summonAura3").SetValue(DataHelper.GetAuraCurse(d.SummonAura3));
            t.Field("summonAuraCharges3").SetValue(d.SummonAuraCharges3);

            // ── AC Energy Bonus ──────────────────────────────────
            if (!string.IsNullOrEmpty(d.AcEnergyBonus))
                t.Field("acEnergyBonus").SetValue(DataHelper.GetAuraCurse(d.AcEnergyBonus));
            t.Field("acEnergyBonusQuantity").SetValue(d.AcEnergyBonusQuantity);
            if (!string.IsNullOrEmpty(d.AcEnergyBonus2))
                t.Field("acEnergyBonus2").SetValue(DataHelper.GetAuraCurse(d.AcEnergyBonus2));
            t.Field("acEnergyBonus2Quantity").SetValue(d.AcEnergyBonus2Quantity);
            t.Field("chooseOneOfAvailableAuras").SetValue(d.ChooseOneOfAvailableAuras);

            // ── Special Value System ─────────────────────────────
            t.Field("specialValueGlobal").SetValue(d.SpecialValueGlobal);
            t.Field("specialValueModifierGlobal").SetValue(d.SpecialValueModifierGlobal);
            if (!string.IsNullOrEmpty(d.SpecialAuraCurseNameGlobal))
                t.Field("specialAuraCurseNameGlobal").SetValue(DataHelper.GetAuraCurse(d.SpecialAuraCurseNameGlobal));
            t.Field("specialValue1").SetValue(d.SpecialValue1);
            t.Field("specialValueModifier1").SetValue(d.SpecialValueModifier1);
            if (!string.IsNullOrEmpty(d.SpecialAuraCurseName1))
                t.Field("specialAuraCurseName1").SetValue(DataHelper.GetAuraCurse(d.SpecialAuraCurseName1));
            t.Field("specialValue2").SetValue(d.SpecialValue2);
            t.Field("specialValueModifier2").SetValue(d.SpecialValueModifier2);
            if (!string.IsNullOrEmpty(d.SpecialAuraCurseName2))
                t.Field("specialAuraCurseName2").SetValue(DataHelper.GetAuraCurse(d.SpecialAuraCurseName2));

            // ── FX / Effects ─────────────────────────────────────
            t.Field("effectCaster").SetValue(d.EffectCaster ?? "");
            t.Field("effectTarget").SetValue(d.EffectTarget ?? "");
            t.Field("effectPreAction").SetValue(d.EffectPreAction ?? "");
            t.Field("effectPostCastDelay").SetValue(d.EffectPostCastDelay);
            t.Field("effectCasterRepeat").SetValue(d.EffectCasterRepeat);
            t.Field("effectCastCenter").SetValue(d.EffectCastCenter);
            t.Field("effectTrail").SetValue(d.EffectTrail ?? "");
            t.Field("effectTrailRepeat").SetValue(d.EffectTrailRepeat);
            t.Field("effectTrailSpeed").SetValue(d.EffectTrailSpeed);
            t.Field("effectTrailAngle").SetValue(d.EffectTrailAngle);
            t.Field("effectPostTargetDelay").SetValue(d.EffectPostTargetDelay);

            // ── Pet System ───────────────────────────────────────
            card.PetActivation = d.PetActivation;
            card.PetBonusDamageType = d.PetBonusDamageType;
            card.PetBonusDamageAmount = d.PetBonusDamageAmount;
            t.Field("isPetAttack").SetValue(d.IsPetAttack);
            t.Field("isPetCast").SetValue(d.IsPetCast);
            t.Field("killPet").SetValue(d.KillPet);
            t.Field("petTemporal").SetValue(d.PetTemporal);
            t.Field("petTemporalAttack").SetValue(d.PetTemporalAttack);
            t.Field("petTemporalCast").SetValue(d.PetTemporalCast);
            t.Field("petTemporalMoveToCenter").SetValue(d.PetTemporalMoveToCenter);
            t.Field("petTemporalMoveToBack").SetValue(d.PetTemporalMoveToBack);
            t.Field("petTemporalFadeOutDelay").SetValue(d.PetTemporalFadeOutDelay);
            t.Field("petFront").SetValue(d.PetFront);
            t.Field("petInvert").SetValue(d.PetInvert);
            t.Field("petOffset").SetValue(new UnityEngine.Vector2(d.PetOffsetX, d.PetOffsetY));
            t.Field("petSize").SetValue(new UnityEngine.Vector2(d.PetSizeX, d.PetSizeY));

            // Initialize remaining arrays to prevent NREs
            t.Field("preDescriptionArgs").SetValue(new string[0]);
            t.Field("descriptionArgs").SetValue(new string[0]);
            t.Field("postDescriptionArgs").SetValue(new string[0]);

            // Copy card art sprite from an existing card
            if (!string.IsNullOrEmpty(d.SpriteSource))
                DataHelper.CopyCardVisuals(card, d.SpriteSource);

            // Auto-generate description from card stats
            try { card.SetDescriptionNew(forceDescription: true); }
            catch { /* Texts not ready — description will be empty until next rebuild */ }

            return card;
        }

        /// <summary>
        /// Snapshot a live CardData SO into a CardDef for override editing.
        /// </summary>
        public static CardDef SnapshotCard(CardData card)
        {
            if (card == null) return null;
            var d = new CardDef();
            var t = Traverse.Create(card);

            // ── Identity ─────────────────────────────────────────
            d.Id = t.Field<string>("id").Value ?? "";
            d.Name = t.Field<string>("cardName").Value ?? "";
            d.Description = t.Field<string>("description").Value ?? "";
            d.Fluff = t.Field<string>("fluff").Value ?? "";
            d.FluffPercent = t.Field<float>("fluffPercent").Value;
            d.Sku = t.Field<string>("sku").Value ?? "";

            // ── Classification ───────────────────────────────────
            d.CardUpgraded = t.Field<Enums.CardUpgraded>("cardUpgraded").Value;
            d.IsUpgraded = d.CardUpgraded != Enums.CardUpgraded.No;
            d.CardRarity = t.Field<Enums.CardRarity>("cardRarity").Value;
            d.CardType = t.Field<Enums.CardType>("cardType").Value;
            d.CardTypeAux = t.Field<Enums.CardType[]>("cardTypeAux").Value ?? Array.Empty<Enums.CardType>();
            d.CardClass = t.Field<Enums.CardClass>("cardClass").Value;
            d.CardNumber = t.Field<int>("cardNumber").Value;
            d.MaxInDeck = t.Field<int>("maxInDeck").Value;

            // ── Cost / Economy ───────────────────────────────────
            d.EnergyCost = t.Field<int>("energyCost").Value;
            d.EnergyCostOriginal = t.Field<int>("energyCostOriginal").Value;
            d.EnergyCostForShow = t.Field<int>("energyCostForShow").Value;
            d.EnergyReductionPermanent = t.Field<int>("energyReductionPermanent").Value;
            d.EnergyReductionTemporal = t.Field<int>("energyReductionTemporal").Value;
            d.EnergyReductionToZeroPermanent = t.Field<bool>("energyReductionToZeroPermanent").Value;
            d.EnergyReductionToZeroTemporal = t.Field<bool>("energyReductionToZeroTemporal").Value;

            // ── Flags ────────────────────────────────────────────
            d.Playable = t.Field<bool>("playable").Value;
            d.Visible = t.Field<bool>("visible").Value;
            d.ShowInTome = t.Field<bool>("showInTome").Value;
            d.AutoplayDraw = t.Field<bool>("autoplayDraw").Value;
            d.AutoplayEndTurn = t.Field<bool>("autoplayEndTurn").Value;
            d.Vanish = t.Field<bool>("vanish").Value;
            d.Innate = t.Field<bool>("innate").Value;
            d.Lazy = t.Field<bool>("lazy").Value;
            d.Corrupted = t.Field<bool>("corrupted").Value;
            d.EndTurn = t.Field<bool>("endTurn").Value;
            d.Starter = t.Field<bool>("starter").Value;
            d.FlipSprite = t.Field<bool>("flipSprite").Value;
            d.ModifiedByTrait = t.Field<bool>("modifiedByTrait").Value;
            d.OnlyInWeekly = t.Field<bool>("onlyInWeekly").Value;

            // ── Upgrade Paths ────────────────────────────────────
            d.UpgradesTo1 = t.Field<string>("upgradesTo1").Value ?? "";
            d.UpgradesTo2 = t.Field<string>("upgradesTo2").Value ?? "";
            d.UpgradedFrom = t.Field<string>("upgradedFrom").Value ?? "";
            d.BaseCard = t.Field<string>("baseCard").Value ?? "";
            d.BaseCardId = d.BaseCard;
            d.RelatedCard = t.Field<string>("relatedCard").Value ?? "";
            d.RelatedCard2 = t.Field<string>("relatedCard2").Value ?? "";
            d.RelatedCard3 = t.Field<string>("relatedCard3").Value ?? "";

            // ── Targeting ────────────────────────────────────────
            d.TargetSide = t.Field<Enums.CardTargetSide>("targetSide").Value;
            d.TargetType = t.Field<Enums.CardTargetType>("targetType").Value;
            d.TargetPos = t.Field<Enums.CardTargetPosition>("targetPosition").Value;

            // ── Damage 1 ─────────────────────────────────────────
            d.Damage = t.Field<int>("damage").Value;
            d.DamageType = t.Field<Enums.DamageType>("damageType").Value;
            d.DamageSides = t.Field<int>("damageSides").Value;
            d.DamageEnergyBonus = t.Field<int>("damageEnergyBonus").Value;
            d.IgnoreBlock = t.Field<bool>("ignoreBlock").Value;
            d.DamageSelf = t.Field<int>("damageSelf").Value;
            d.DamageSpecialValueGlobal = t.Field<bool>("damageSpecialValueGlobal").Value;
            d.DamageSpecialValue1 = t.Field<bool>("damageSpecialValue1").Value;
            d.DamageSpecialValue2 = t.Field<bool>("damageSpecialValue2").Value;

            // ── Damage 2 ─────────────────────────────────────────
            d.Damage2 = t.Field<int>("damage2").Value;
            d.DamageType2 = t.Field<Enums.DamageType>("damageType2").Value;
            d.DamageSides2 = t.Field<int>("damageSides2").Value;
            d.IgnoreBlock2 = t.Field<bool>("ignoreBlock2").Value;
            d.DamageSelf2 = t.Field<int>("damageSelf2").Value;
            d.Damage2SpecialValueGlobal = t.Field<bool>("damage2SpecialValueGlobal").Value;
            d.Damage2SpecialValue1 = t.Field<bool>("damage2SpecialValue1").Value;
            d.Damage2SpecialValue2 = t.Field<bool>("damage2SpecialValue2").Value;

            // ── Self HP Loss ─────────────────────────────────────
            d.SelfHealthLoss = t.Field<int>("selfHealthLoss").Value;
            d.SelfHealthLossSpecialGlobal = t.Field<bool>("selfHealthLossSpecialGlobal").Value;
            d.SelfHealthLossSpecialValue1 = t.Field<bool>("selfHealthLossSpecialValue1").Value;
            d.SelfHealthLossSpecialValue2 = t.Field<bool>("selfHealthLossSpecialValue2").Value;
            d.SelfKillHiddenSeconds = t.Field<float>("selfKillHiddenSeconds").Value;

            // ── Curses ───────────────────────────────────────────
            d.Curse = GetACId(t.Field<AuraCurseData>("curse").Value);
            d.CurseCharges = t.Field<int>("curseCharges").Value;
            d.CurseChargesSides = t.Field<int>("curseChargesSides").Value;
            d.CurseChargesSpecialValueGlobal = t.Field<bool>("curseChargesSpecialValueGlobal").Value;
            d.CurseChargesSpecialValue1 = t.Field<bool>("curseChargesSpecialValue1").Value;
            d.CurseChargesSpecialValue2 = t.Field<bool>("curseChargesSpecialValue2").Value;
            d.CurseSelf = GetACId(t.Field<AuraCurseData>("curseSelf").Value);
            d.CurseSelfCharges = d.CurseCharges; // shared field

            d.Curse2 = GetACId(t.Field<AuraCurseData>("curse2").Value);
            d.Curse2Charges = t.Field<int>("curseCharges2").Value;
            d.CurseCharges2SpecialValueGlobal = t.Field<bool>("curseCharges2SpecialValueGlobal").Value;
            d.CurseCharges2SpecialValue1 = t.Field<bool>("curseCharges2SpecialValue1").Value;
            d.CurseCharges2SpecialValue2 = t.Field<bool>("curseCharges2SpecialValue2").Value;
            d.CurseSelf2 = GetACId(t.Field<AuraCurseData>("curseSelf2").Value);
            d.CurseSelf2Charges = d.Curse2Charges;

            d.Curse3 = GetACId(t.Field<AuraCurseData>("curse3").Value);
            d.Curse3Charges = t.Field<int>("curseCharges3").Value;
            d.CurseCharges3SpecialValueGlobal = t.Field<bool>("curseCharges3SpecialValueGlobal").Value;
            d.CurseCharges3SpecialValue1 = t.Field<bool>("curseCharges3SpecialValue1").Value;
            d.CurseCharges3SpecialValue2 = t.Field<bool>("curseCharges3SpecialValue2").Value;
            d.CurseSelf3 = GetACId(t.Field<AuraCurseData>("curseSelf3").Value);
            d.CurseSelf3Charges = d.Curse3Charges;

            // ── Auras ────────────────────────────────────────────
            d.Aura = GetACId(t.Field<AuraCurseData>("aura").Value);
            d.AuraCharges = t.Field<int>("auraCharges").Value;
            d.AuraChargesSpecialValueGlobal = t.Field<bool>("auraChargesSpecialValueGlobal").Value;
            d.AuraChargesSpecialValue1 = t.Field<bool>("auraChargesSpecialValue1").Value;
            d.AuraChargesSpecialValue2 = t.Field<bool>("auraChargesSpecialValue2").Value;
            d.AuraSelf = GetACId(t.Field<AuraCurseData>("auraSelf").Value);
            d.AuraSelfCharges = d.AuraCharges;

            d.Aura2 = GetACId(t.Field<AuraCurseData>("aura2").Value);
            d.Aura2Charges = t.Field<int>("auraCharges2").Value;
            d.AuraCharges2SpecialValueGlobal = t.Field<bool>("auraCharges2SpecialValueGlobal").Value;
            d.AuraCharges2SpecialValue1 = t.Field<bool>("auraCharges2SpecialValue1").Value;
            d.AuraCharges2SpecialValue2 = t.Field<bool>("auraCharges2SpecialValue2").Value;
            d.AuraSelf2 = GetACId(t.Field<AuraCurseData>("auraSelf2").Value);
            d.AuraSelf2Charges = d.Aura2Charges;

            d.Aura3 = GetACId(t.Field<AuraCurseData>("aura3").Value);
            d.Aura3Charges = t.Field<int>("auraCharges3").Value;
            d.AuraCharges3SpecialValueGlobal = t.Field<bool>("auraCharges3SpecialValueGlobal").Value;
            d.AuraCharges3SpecialValue1 = t.Field<bool>("auraCharges3SpecialValue1").Value;
            d.AuraCharges3SpecialValue2 = t.Field<bool>("auraCharges3SpecialValue2").Value;
            d.AuraSelf3 = GetACId(t.Field<AuraCurseData>("auraSelf3").Value);
            d.AuraSelf3Charges = d.Aura3Charges;

            // ── Heal ─────────────────────────────────────────────
            d.Heal = t.Field<int>("heal").Value;
            d.HealSides = t.Field<int>("healSides").Value;
            d.HealSelf = t.Field<int>("healSelf").Value;
            d.HealEnergyBonus = t.Field<int>("healEnergyBonus").Value;
            d.HealSelfPerDamageDonePercent = t.Field<float>("healSelfPerDamageDonePercent").Value;
            d.HealCurses = t.Field<int>("healCurses").Value;
            d.DispelAuras = t.Field<int>("dispelAuras").Value;
            d.HealSpecialValueGlobal = t.Field<bool>("healSpecialValueGlobal").Value;
            d.HealSpecialValue1 = t.Field<bool>("healSpecialValue1").Value;
            d.HealSpecialValue2 = t.Field<bool>("healSpecialValue2").Value;
            d.HealSelfSpecialValueGlobal = t.Field<bool>("healSelfSpecialValueGlobal").Value;
            d.HealSelfSpecialValue1 = t.Field<bool>("healSelfSpecialValue1").Value;
            d.HealSelfSpecialValue2 = t.Field<bool>("healSelfSpecialValue2").Value;

            d.HealAuraCurseSelf = GetACId(t.Field<AuraCurseData>("healAuraCurseSelf").Value);
            d.HealAuraCurseName = GetACId(t.Field<AuraCurseData>("healAuraCurseName").Value);
            d.HealAuraCurseName2 = GetACId(t.Field<AuraCurseData>("healAuraCurseName2").Value);
            d.HealAuraCurseName3 = GetACId(t.Field<AuraCurseData>("healAuraCurseName3").Value);
            d.HealAuraCurseName4 = GetACId(t.Field<AuraCurseData>("healAuraCurseName4").Value);

            // ── AC Manipulation ──────────────────────────────────
            d.TransferCurses = t.Field<int>("transferCurses").Value;
            d.StealAuras = t.Field<int>("stealAuras").Value;
            d.ReduceCurses = t.Field<int>("reduceCurses").Value;
            d.ReduceAuras = t.Field<int>("reduceAuras").Value;
            d.IncreaseCurses = t.Field<int>("increaseCurses").Value;
            d.IncreaseAuras = t.Field<int>("increaseAuras").Value;

            // ── Effect Repeat ────────────────────────────────────
            d.EffectRepeat = t.Field<int>("effectRepeat").Value;
            d.EffectRepeatDelay = t.Field<float>("effectRepeatDelay").Value;
            d.EffectRepeatEnergyBonus = t.Field<int>("effectRepeatEnergyBonus").Value;
            d.EffectRepeatMaxBonus = t.Field<int>("effectRepeatMaxBonus").Value;
            d.EffectRepeatModificator = t.Field<int>("effectRepeatModificator").Value;
            d.EffectRepeatTarget = t.Field<Enums.EffectRepeatTarget>("effectRepeatTarget").Value;

            // ── Misc Mechanics ───────────────────────────────────
            d.MoveToCenter = t.Field<bool>("moveToCenter").Value;
            d.PushTarget = t.Field<int>("pushTarget").Value;
            d.PullTarget = t.Field<int>("pullTarget").Value;
            d.DrawCard = t.Field<int>("drawCard").Value;
            d.DrawCardSpecialValueGlobal = t.Field<bool>("drawCardSpecialValueGlobal").Value;
            d.DiscardCard = t.Field<int>("discardCard").Value;
            d.EnergyRecharge = t.Field<int>("energyRecharge").Value;
            d.EnergyRechargeSpecialValueGlobal = t.Field<bool>("energyRechargeSpecialValueGlobal").Value;
            d.GoldGainQuantity = t.Field<int>("goldGainQuantity").Value;
            d.ShardsGainQuantity = t.Field<int>("shardsGainQuantity").Value;
            d.ExhaustCounter = t.Field<int>("exhaustCounter").Value;
            d.EffectRequired = t.Field<string>("effectRequired").Value ?? "";

            // ── Discard Options ──────────────────────────────────
            d.DiscardCardType = t.Field<Enums.CardType>("discardCardType").Value;
            d.DiscardCardTypeAux = t.Field<Enums.CardType[]>("discardCardTypeAux").Value ?? Array.Empty<Enums.CardType>();
            d.DiscardCardAutomatic = t.Field<bool>("discardCardAutomatic").Value;
            d.DiscardCardPlace = t.Field<Enums.CardPlace>("discardCardPlace").Value;

            // ── Add Card ─────────────────────────────────────────
            d.AddCard = t.Field<int>("addCard").Value;
            d.AddCardId = t.Field<string>("addCardId").Value ?? "";
            d.AddCardType = t.Field<Enums.CardType>("addCardType").Value;
            d.AddCardTypeAux = t.Field<Enums.CardType[]>("addCardTypeAux").Value ?? Array.Empty<Enums.CardType>();
            d.AddCardChoose = t.Field<int>("addCardChoose").Value;
            d.AddCardFrom = t.Field<Enums.CardFrom>("addCardFrom").Value;
            d.AddCardPlace = t.Field<Enums.CardPlace>("addCardPlace").Value;
            d.AddCardReducedCost = t.Field<int>("addCardReducedCost").Value;
            d.AddCardCostTurn = t.Field<bool>("addCardCostTurn").Value;
            d.AddCardVanish = t.Field<bool>("addCardVanish").Value;
            d.AddCardOnlyCheckAuxTypes = t.Field<bool>("addCardOnlyCheckAuxTypes").Value;
            d.AddCardFromVanishPile = card.AddCardFromVanishPile;
            d.AddVanishToDeck = t.Field<bool>("addVanishToDeck").Value;

            // AddCardList: extract IDs from CardData[]
            var addList = t.Field<CardData[]>("addCardList").Value;
            d.AddCardList = new List<string>();
            if (addList != null)
            {
                foreach (var c in addList)
                {
                    if (c != null)
                    {
                        var cid = Traverse.Create(c).Field<string>("id").Value ?? "";
                        if (!string.IsNullOrEmpty(cid)) d.AddCardList.Add(cid);
                    }
                }
            }

            // ── Look / Scry ─────────────────────────────────────
            d.LookCards = t.Field<int>("lookCards").Value;
            d.LookCardsDiscardUpTo = t.Field<int>("lookCardsDiscardUpTo").Value;
            d.LookCardsVanishUpTo = t.Field<int>("lookCardsVanishUpTo").Value;

            // ── Summon ───────────────────────────────────────────
            var sumNpc = t.Field<NPCData>("summonUnit").Value;
            d.SummonUnitId = sumNpc != null ? sumNpc.Id : "";
            d.SummonNum = t.Field<int>("summonUnitNum").Value;
            d.Evolve = t.Field<bool>("evolve").Value;
            d.Metamorph = t.Field<bool>("metamorph").Value;
            d.SummonAura = GetACId(t.Field<AuraCurseData>("summonAura").Value);
            d.SummonAuraCharges = t.Field<int>("summonAuraCharges").Value;
            d.SummonAura2 = GetACId(t.Field<AuraCurseData>("summonAura2").Value);
            d.SummonAuraCharges2 = t.Field<int>("summonAuraCharges2").Value;
            d.SummonAura3 = GetACId(t.Field<AuraCurseData>("summonAura3").Value);
            d.SummonAuraCharges3 = t.Field<int>("summonAuraCharges3").Value;

            // ── AC Energy Bonus ──────────────────────────────────
            d.AcEnergyBonus = GetACId(t.Field<AuraCurseData>("acEnergyBonus").Value);
            d.AcEnergyBonusQuantity = t.Field<int>("acEnergyBonusQuantity").Value;
            d.AcEnergyBonus2 = GetACId(t.Field<AuraCurseData>("acEnergyBonus2").Value);
            d.AcEnergyBonus2Quantity = t.Field<int>("acEnergyBonus2Quantity").Value;
            d.ChooseOneOfAvailableAuras = t.Field<bool>("chooseOneOfAvailableAuras").Value;

            // ── Special Value System ─────────────────────────────
            d.SpecialValueGlobal = t.Field<Enums.CardSpecialValue>("specialValueGlobal").Value;
            d.SpecialValueModifierGlobal = t.Field<float>("specialValueModifierGlobal").Value;
            d.SpecialAuraCurseNameGlobal = GetACId(t.Field<AuraCurseData>("specialAuraCurseNameGlobal").Value);
            d.SpecialValue1 = t.Field<Enums.CardSpecialValue>("specialValue1").Value;
            d.SpecialValueModifier1 = t.Field<float>("specialValueModifier1").Value;
            d.SpecialAuraCurseName1 = GetACId(t.Field<AuraCurseData>("specialAuraCurseName1").Value);
            d.SpecialValue2 = t.Field<Enums.CardSpecialValue>("specialValue2").Value;
            d.SpecialValueModifier2 = t.Field<float>("specialValueModifier2").Value;
            d.SpecialAuraCurseName2 = GetACId(t.Field<AuraCurseData>("specialAuraCurseName2").Value);

            // ── FX / Effects ─────────────────────────────────────
            d.EffectCaster = t.Field<string>("effectCaster").Value ?? "";
            d.EffectTarget = t.Field<string>("effectTarget").Value ?? "";
            d.EffectPreAction = t.Field<string>("effectPreAction").Value ?? "";
            d.EffectPostCastDelay = t.Field<float>("effectPostCastDelay").Value;
            d.EffectCasterRepeat = t.Field<bool>("effectCasterRepeat").Value;
            d.EffectCastCenter = t.Field<bool>("effectCastCenter").Value;
            d.EffectTrail = t.Field<string>("effectTrail").Value ?? "";
            d.EffectTrailRepeat = t.Field<bool>("effectTrailRepeat").Value;
            d.EffectTrailSpeed = t.Field<float>("effectTrailSpeed").Value;
            d.EffectTrailAngle = t.Field<Enums.EffectTrailAngle>("effectTrailAngle").Value;
            d.EffectPostTargetDelay = t.Field<float>("effectPostTargetDelay").Value;

            // ── Pet System ───────────────────────────────────────
            d.PetActivation = card.PetActivation;
            d.PetBonusDamageType = card.PetBonusDamageType;
            d.PetBonusDamageAmount = card.PetBonusDamageAmount;
            d.IsPetAttack = t.Field<bool>("isPetAttack").Value;
            d.IsPetCast = t.Field<bool>("isPetCast").Value;
            d.KillPet = t.Field<bool>("killPet").Value;
            d.PetTemporal = t.Field<bool>("petTemporal").Value;
            d.PetTemporalAttack = t.Field<bool>("petTemporalAttack").Value;
            d.PetTemporalCast = t.Field<bool>("petTemporalCast").Value;
            d.PetTemporalMoveToCenter = t.Field<bool>("petTemporalMoveToCenter").Value;
            d.PetTemporalMoveToBack = t.Field<bool>("petTemporalMoveToBack").Value;
            d.PetTemporalFadeOutDelay = t.Field<float>("petTemporalFadeOutDelay").Value;
            d.PetFront = t.Field<bool>("petFront").Value;
            d.PetInvert = t.Field<bool>("petInvert").Value;
            var petOff = t.Field<UnityEngine.Vector2>("petOffset").Value;
            d.PetOffsetX = petOff.x;
            d.PetOffsetY = petOff.y;
            var petSz = t.Field<UnityEngine.Vector2>("petSize").Value;
            d.PetSizeX = petSz.x;
            d.PetSizeY = petSz.y;

            // ── Upgrade to Rare ──────────────────────────────────
            var rareCard = t.Field<CardData>("upgradesToRare").Value;
            if (rareCard != null) d.UpgradesToRareId = rareCard.Id ?? "";

            return d;
        }
    }
}
