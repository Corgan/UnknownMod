using System;
using System.Collections.Generic;
using System.Linq;
using Cards;
using Cards.Data;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    public static partial class DataHelper
    {
        public static CardDataNew MakeCard(string id, string name, int damage = 0, Enums.DamageType dmgType = Enums.DamageType.None,
            int damage2 = 0, Enums.DamageType dmgType2 = Enums.DamageType.None, string curse = null, int curseCharges = 0,
            string curse2 = null, int curse2Charges = 0, string aura = null, int auraCharges = 0, string auraSelf = null,
            int auraSelfCharges = 0, string curseSelf = null, int curseSelfCharges = 0, int heal = 0, int healSelf = 0,
            Enums.CardTargetSide targetSide = Enums.CardTargetSide.Enemy, Enums.CardTargetType targetType = Enums.CardTargetType.Single,
            Enums.CardTargetPosition targetPos = Enums.CardTargetPosition.Anywhere, int effectRepeat = 1, bool moveToCenter = false,
            string summonUnit = null, int summonNum = 0, int selfHealthLoss = 0, int healCurses = 0, int dispelAuras = 0,
            Enums.CardUpgraded upgraded = Enums.CardUpgraded.No, string effectCaster = "", string effectTarget = "", int energyCost = 0)
        {
            return BuildCard(new CardDef
            {
                Id = id, Name = name, Damage = damage, DamageType = dmgType, Damage2 = damage2, DamageType2 = dmgType2,
                Curse = curse ?? "", CurseCharges = curseCharges, Curse2 = curse2 ?? "", Curse2Charges = curse2Charges,
                Aura = aura ?? "", AuraCharges = auraCharges, AuraSelf = auraSelf ?? "", AuraSelfCharges = auraSelfCharges,
                CurseSelf = curseSelf ?? "", CurseSelfCharges = curseSelfCharges, Heal = heal, HealSelf = healSelf,
                TargetSide = targetSide, TargetType = targetType, TargetPos = targetPos, EffectRepeat = effectRepeat,
                MoveToCenter = moveToCenter, SummonUnitId = summonUnit ?? "", SummonNum = summonNum,
                SelfHealthLoss = selfHealthLoss, HealCurses = healCurses, DispelAuras = dispelAuras,
                CardUpgraded = upgraded, EnergyCost = energyCost, EffectCaster = effectCaster ?? "", EffectTarget = effectTarget ?? ""
            });
        }

        public static CardDataNew BuildCard(CardDef d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var card = ScriptableObject.CreateInstance<CardDataNew>();
            var fields = Traverse.Create(card);
            fields.Field("id").SetValue(d.Id ?? "");
            fields.Field("internalId").SetValue(d.Id ?? "");
            fields.Field("cardName").SetValue(d.Name ?? "");
            fields.Field("sku").SetValue(d.Sku ?? "");
            fields.Field("baseCard").SetValue(d.BaseCard ?? d.BaseCardId ?? "");
            fields.Field("specialCardEnum").SetValue(d.SpecialCardEnum);
            fields.Field("cardClass").SetValue(d.CardClass);
            fields.Field("cardRarity").SetValue(d.CardRarity);
            fields.Field("cardType").SetValue(d.CardType);
            fields.Field("cardTypeAux").SetValue(d.CardTypeAux ?? Array.Empty<Enums.CardType>());
            fields.Field("maxInDeck").SetValue(d.MaxInDeck);
            fields.Field("energyCost").SetValue(d.EnergyCost);
            fields.Field("playable").SetValue(d.Playable);
            fields.Field("autoplayDraw").SetValue(d.AutoplayDraw);
            fields.Field("autoplayEndTurn").SetValue(d.AutoplayEndTurn);
            fields.Field("effectRequired").SetValue(d.EffectRequired ?? "");
            fields.Field("selfKillHiddenSeconds").SetValue(d.SelfKillHiddenSeconds);
            fields.Field("customFlags").SetValue(Flags(d));
            var description = Traverse.Create(card.Description);
            description.Field("description").SetValue(d.Description ?? "");
            description.Field("fluff").SetValue(d.Fluff ?? "");
            description.Field("fluffPercent").SetValue(d.FluffPercent);
            description.Field("flipSprite").SetValue(d.FlipSprite);
            description.Field("showInTome").SetValue(d.ShowInTome);
            // The game treats any entry as a related card, so empty ids must be left out
            var related = new List<string>();
            foreach (var id in new[] { d.RelatedCard, d.RelatedCard2, d.RelatedCard3 })
                if (!string.IsNullOrWhiteSpace(id)) related.Add(id);
            card.Description.RelatedCards = related;
            fields.Field("upgrade").SetValue(new UpgradeData(d.UpgradesTo1 ?? "", d.UpgradesTo2 ?? "", d.CardUpgraded, d.UpgradedFrom ?? "", null));
            fields.Field("targetData").SetValue(new CardTargetData(d.TargetSide, d.TargetType, d.TargetPos));
            card.RepeatCast.EffectRepeat = Math.Max(1, d.EffectRepeat);
            card.RepeatCast.EffectRepeatDelay = d.EffectRepeatDelay;
            card.RepeatCast.EffectRepeatEnergyBonus = d.EffectRepeatEnergyBonus;
            card.RepeatCast.EffectRepeatMaxBonus = d.EffectRepeatMaxBonus;
            card.RepeatCast.EffectRepeatModificator = d.EffectRepeatModificator;
            card.RepeatCast.EffectRepeatTarget = d.EffectRepeatTarget;
            card.Damage.Clear();
            Damage(card, d, "Damage", d.Damage, d.DamageType, DamageTarget.Target, d.IgnoreBlock);
            Damage(card, d, "Damage2", d.Damage2, d.DamageType2, DamageTarget.Target, d.IgnoreBlock2);
            Damage(card, d, "DamageSelf", d.DamageSelf, d.DamageSelfType ?? d.DamageType, DamageTarget.Self, d.IgnoreBlockSelf ?? d.IgnoreBlock);
            Damage(card, d, "DamageSelf2", d.DamageSelf2, d.DamageSelfType2 ?? d.DamageType2, DamageTarget.Self, d.IgnoreBlockSelf2 ?? d.IgnoreBlock2);
            SideDamage(card, d.DamageSides, d.DamageType, d.IgnoreBlock);
            SideDamage(card, d.DamageSides2, d.DamageType2, d.IgnoreBlock2);
            fields.Field("damageEnergyBonus").SetValue(d.DamageEnergyBonus);
            fields.Field("selfHealthLoss").SetValue(new SelfHealthLossData(d.SelfHealthLoss, BuildSpecialValue(d, "SelfHealthLoss", d.SelfHealthLossSpecialValue)));
            card.Heal.HealEffects = new List<HealEffectData>();
            card.Heal.HealEnergyBonus = d.HealEnergyBonus;
            Heal(card, d, "Heal", d.Heal, HealTarget.Target);
            Heal(card, d, "HealSelf", d.HealSelf, HealTarget.Self);
            if (d.HealSides != 0) card.Heal.HealEffects.Add(new HealEffectData { Heal = d.HealSides, HealTarget = HealTarget.Sides, HealType = HealType.Normal });
            card.Auras.Auras = new List<AuraEffectData>();
            Aura(card, d, "Aura", d.Aura, d.AuraCharges, AuraTarget.Target);
            Aura(card, d, "Aura2", d.Aura2, d.Aura2Charges, AuraTarget.Target);
            Aura(card, d, "Aura3", d.Aura3, d.Aura3Charges, AuraTarget.Target);
            Aura(card, d, "AuraSelf", d.AuraSelf, d.AuraSelfCharges, AuraTarget.Self);
            Aura(card, d, "AuraSelf2", d.AuraSelf2, d.AuraSelf2Charges, AuraTarget.Self);
            Aura(card, d, "AuraSelf3", d.AuraSelf3, d.AuraSelf3Charges, AuraTarget.Self);
            card.Curses.Clear();
            Curse(card, d, "Curse", d.Curse, d.CurseCharges, CurseTarget.Target);
            Curse(card, d, "Curse2", d.Curse2, d.Curse2Charges, CurseTarget.Target);
            Curse(card, d, "Curse3", d.Curse3, d.Curse3Charges, CurseTarget.Target);
            Curse(card, d, "CurseSelf", d.CurseSelf, d.CurseSelfCharges, CurseTarget.Self);
            Curse(card, d, "CurseSelf2", d.CurseSelf2, d.CurseSelf2Charges, CurseTarget.Self);
            Curse(card, d, "CurseSelf3", d.CurseSelf3, d.CurseSelf3Charges, CurseTarget.Self);
            CurseSides(card, d);
            fields.Field("auraCurseDispels").SetValue(Dispels(d));
            card.CardManagement.DrawCard = d.DrawCard;
            card.CardManagement.DrawCardSpecialValue = BuildSpecialValue(d, "DrawCard", d.DrawCardSpecialValue);
            card.CardManagement.DiscardCard = d.DiscardCard;
            card.CardManagement.DiscardCardType = d.DiscardCardType;
            card.CardManagement.DiscardCardTypeAux = d.DiscardCardTypeAux ?? Array.Empty<Enums.CardType>();
            card.CardManagement.DiscardCardAutomatic = d.DiscardCardAutomatic;
            card.CardManagement.DiscardCardPlace = d.DiscardCardPlace;
            card.CardManagement.AddCard = d.AddCard;
            card.CardManagement.AddCardId = d.AddCardId ?? "";
            card.CardManagement.AddCardType = d.AddCardType;
            card.CardManagement.AddCardTypeAux = d.AddCardTypeAux ?? Array.Empty<Enums.CardType>();
            card.CardManagement.AddCardChoose = d.AddCardChoose;
            card.CardManagement.AddCardFrom = d.AddCardFrom;
            card.CardManagement.AddCardPlace = d.AddCardPlace;
            card.CardManagement.AddCardReducedCost = d.AddCardReducedCost;
            card.CardManagement.AddCardCostTurn = d.AddCardCostTurn;
            card.CardManagement.AddCardVanish = d.AddCardVanish;
            card.CardManagement.AddCardOnlyCheckAuxTypes = d.AddCardOnlyCheckAuxTypes;
            card.CardManagement.AddCardFromVanishPile = d.AddCardFromVanishPile;
            card.CardManagement.AddVanishToDeck = d.AddVanishToDeck;
            card.CardManagement.AddCardList = Array.Empty<CardDataNew>();
            card.CardManagement.LookCards = d.LookCards;
            card.CardManagement.LookCardsDiscardUpTo = d.LookCardsDiscardUpTo;
            card.CardManagement.LookCardsVanishUpTo = d.LookCardsVanishUpTo;
            fields.Field("pushTarget").SetValue(d.PushTarget);
            fields.Field("pullTarget").SetValue(d.PullTarget);
            card.InGameEffects.EffectCaster = d.EffectCaster ?? "";
            card.InGameEffects.EffectTarget = d.EffectTarget ?? "";
            card.InGameEffects.EffectPreAction = d.EffectPreAction ?? "";
            card.InGameEffects.EffectCasterRepeat = d.EffectCasterRepeat;
            card.InGameEffects.EffectPostCastDelay = d.EffectPostCastDelay;
            card.InGameEffects.EffectCastCenter = d.EffectCastCenter;
            card.InGameEffects.EffectTrail = d.EffectTrail ?? "";
            card.InGameEffects.EffectTrailRepeat = d.EffectTrailRepeat;
            card.InGameEffects.EffectTrailSpeed = d.EffectTrailSpeed;
            card.InGameEffects.EffectTrailAngle = d.EffectTrailAngle;
            card.InGameEffects.EffectPostTargetDelay = d.EffectPostTargetDelay;
            card.InGameEffects.GoldGainQuantity = d.GoldGainQuantity;
            card.InGameEffects.ShardsGainQuantity = d.ShardsGainQuantity;
            card.PetData.PetActivation = d.PetActivation;
            card.PetData.PetBonusDamageType = d.PetBonusDamageType;
            card.PetData.PetBonusDamageAmount = d.PetBonusDamageAmount;
            card.PetData.IsPetAttack = d.IsPetAttack;
            card.PetData.IsPetCast = d.IsPetCast;
            card.PetData.KillPet = d.KillPet;
            card.PetData.PetTemporal = d.PetTemporal;
            card.PetData.PetTemporalAttack = d.PetTemporalAttack;
            card.PetData.PetTemporalCast = d.PetTemporalCast;
            card.PetData.PetTemporalMoveToCenter = d.PetTemporalMoveToCenter;
            card.PetData.PetTemporalMoveToBack = d.PetTemporalMoveToBack;
            card.PetData.PetTemporalFadeOutDelay = d.PetTemporalFadeOutDelay;
            card.PetData.PetFront = d.PetFront;
            card.PetData.PetInvert = d.PetInvert;
            card.PetData.PetOffset = new Vector2(d.PetOffsetX, d.PetOffsetY);
            card.PetData.PetSize = new Vector2(d.PetSizeX, d.PetSizeY);
            card.Summon.SummonUnit = string.IsNullOrEmpty(d.SummonUnitId) ? null : GetExistingNPC(d.SummonUnitId, false);
            card.Summon.SummonUnitNum = d.SummonNum;
            card.Summon.Metamorph = d.Metamorph;
            card.Summon.Evolve = d.Evolve;
            card.Summon.SummonAuras = new List<SummonAuraData>();
            SummonAura(card, d.SummonAura, d.SummonAuraCharges);
            SummonAura(card, d.SummonAura2, d.SummonAuraCharges2);
            SummonAura(card, d.SummonAura3, d.SummonAuraCharges3);
            fields.Field("acEnergyBonus").SetValue(GetAuraCurse(d.AcEnergyBonus));
            fields.Field("acEnergyBonusQuantity").SetValue(d.AcEnergyBonusQuantity);
            fields.Field("acEnergyBonus2").SetValue(GetAuraCurse(d.AcEnergyBonus2));
            fields.Field("acEnergyBonus2Quantity").SetValue(d.AcEnergyBonus2Quantity);
            ApplyAdvancedCardData(card, d);
            CopyCardAssets(card, d);
            return card;
        }

        private static CustomFlags Flags(CardDef d)
        {
            var flags = CustomFlags.None;
            if (d.Vanish) flags |= CustomFlags.Vanish;
            if (d.Innate) flags |= CustomFlags.Innate;
            if (d.Lazy) flags |= CustomFlags.Lazy;
            if (d.EndTurn) flags |= CustomFlags.EndTurn;
            if (d.MoveToCenter) flags |= CustomFlags.MoveToCenter;
            if (d.Corrupted) flags |= CustomFlags.Corrupted;
            if (d.Starter) flags |= CustomFlags.Starter;
            if (d.OnlyInWeekly) flags |= CustomFlags.OnlyInWeekly;
            if (d.CombatBased) flags |= CustomFlags.CombatBased;
            return flags;
        }

        private static void Damage(CardDataNew card, CardDef d, string slot, int amount, Enums.DamageType type, DamageTarget target, bool ignoreBlock)
        {
            var extras = ExtrasFor(d, slot);
            // An effect entry makes the game show and run it even at 0, so an empty slot must not create one.
            // A 0 base is still real when special values or the per-energy bonus supply the amount.
            if (amount == 0 && !HasExtras(extras) && (target == DamageTarget.Self || d.DamageEnergyBonus == 0)) return;
            if (type == Enums.DamageType.None && amount == 0 && !HasExtras(extras)) return;
            var effect = new DamageEffectData(amount, type, target, ignoreBlock);
            ApplyEffectExtras(effect, d, slot, extras);
            card.Damage.Add(effect);
        }

        /// <summary>Splash damage to both neighbours of the target (the game models it as a left + right effect pair).</summary>
        private static void SideDamage(CardDataNew card, int amount, Enums.DamageType type, bool ignoreBlock)
        {
            if (amount == 0) return;
            card.Damage.Add(new DamageEffectData(amount, type, DamageTarget.SideLeft, ignoreBlock));
            card.Damage.Add(new DamageEffectData(amount, type, DamageTarget.SideRight, ignoreBlock));
        }

        private static void CurseSides(CardDataNew card, CardDef d)
        {
            if (d.CurseChargesSides == 0 || string.IsNullOrEmpty(d.Curse)) return;
            var curse = GetAuraCurse(d.Curse);
            if (curse == null) { Plugin.Log.LogWarning($"[DataHelper] Card '{d.Id}' CurseChargesSides references missing curse '{d.Curse}'."); return; }
            card.Curses.Add(new CurseEffectData { Curse = curse, Charges = d.CurseChargesSides, Target = CurseTarget.Sides });
        }

        private static void Heal(CardDataNew card, CardDef d, string slot, int amount, HealTarget target)
        {
            var extras = ExtrasFor(d, slot);
            if (amount == 0 && !HasExtras(extras) && d.HealEnergyBonus == 0) return;
            var effect = new HealEffectData { Heal = amount, HealTarget = target, HealType = HealType.Normal };
            ApplyEffectExtras(effect, d, slot, extras);
            card.Heal.HealEffects.Add(effect);
        }

        private static void Aura(CardDataNew card, CardDef d, string slot, string id, int charges, AuraTarget target)
        {
            var aura = string.IsNullOrEmpty(id) ? null : GetAuraCurse(id);
            if (aura == null) return;
            if (charges == 0 && !HasExtras(ExtrasFor(d, slot)) && !EnergyBonusFor(d, aura)) return;
            var effect = new AuraEffectData(aura, charges, target);
            ApplyEffectExtras(effect, d, slot, ExtrasFor(d, slot));
            card.Auras.Auras.Add(effect);
        }

        private static void Curse(CardDataNew card, CardDef d, string slot, string id, int charges, CurseTarget target)
        {
            var curse = string.IsNullOrEmpty(id) ? null : GetAuraCurse(id);
            if (curse == null) return;
            if (charges == 0 && !HasExtras(ExtrasFor(d, slot)) && !EnergyBonusFor(d, curse)) return;
            var effect = new CurseEffectData { Curse = curse, Charges = charges, Target = target };
            ApplyEffectExtras(effect, d, slot, ExtrasFor(d, slot));
            card.Curses.Add(effect);
        }
        private static bool EnergyBonusFor(CardDef d, AuraCurseData ac)
            => ac != null && (string.Equals(d.AcEnergyBonus, ac.Id, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(d.AcEnergyBonus2, ac.Id, StringComparison.OrdinalIgnoreCase));

        private static void SummonAura(CardDataNew card, string id, int charges)
        {
            var aura = string.IsNullOrEmpty(id) ? null : GetAuraCurse(id);
            if (aura != null) card.Summon.SummonAuras.Add(new SummonAuraData { Aura = aura, Charges = charges });
        }

        private static AuraCurseDispelData Dispels(CardDef d)
        {
            var dispels = new AuraCurseDispelData
            {
                HealCurses = d.HealCurses, DispelAuras = d.DispelAuras, TransferCurses = d.TransferCurses,
                StealAuras = d.StealAuras, ReduceCurses = d.ReduceCurses, ReduceAuras = d.ReduceAuras,
                IncreaseCurses = d.IncreaseCurses, IncreaseAuras = d.IncreaseAuras,
                HealAuraCurses = new List<AuraCurseData>(),
                AuraCurseReduceIncreaseDatas = new List<AuraCurseDispelData.AuraCurseReduceIncreaseData>(),
                AuraCurseStealDatas = new List<AuraCurseDispelData.AuraCurseStealData>()
            };
            dispels.HealAuraCurseSelf = ResolveCardAuraCurse(d, nameof(d.HealAuraCurseSelf), d.HealAuraCurseSelf);
            foreach (var reference in new[]
            {
                (nameof(d.HealAuraCurseName), d.HealAuraCurseName),
                (nameof(d.HealAuraCurseName2), d.HealAuraCurseName2),
                (nameof(d.HealAuraCurseName3), d.HealAuraCurseName3),
                (nameof(d.HealAuraCurseName4), d.HealAuraCurseName4)
            })
            {
                var auraCurse = ResolveCardAuraCurse(d, reference.Item1, reference.Item2);
                if (auraCurse != null) dispels.HealAuraCurses.Add(auraCurse);
            }
            return dispels;
        }

        private static AuraCurseData ResolveCardAuraCurse(CardDef card, string fieldName, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var auraCurse = GetAuraCurse(id);
            if (auraCurse == null)
                Plugin.Log.LogWarning($"[DataHelper] Card '{card.Id}' field '{fieldName}' references missing aura/curse '{id}'.");
            return auraCurse;
        }

        public static void ResolveCardLists(IEnumerable<CardDef> defs)
        {
            var finalDefinitions = new Dictionary<string, CardDef>();
            foreach (var definition in defs ?? Enumerable.Empty<CardDef>())
            {
                if (definition != null)
                    finalDefinitions[NormalizeKey(definition.Id)] = definition;
            }

            foreach (var definition in finalDefinitions.Values)
            {
                var source = GetCardSource(definition.Id);
                var realtime = GetCard(definition.Id);
                if (source == null || realtime == null) continue;

                var rareUpgrade = string.IsNullOrEmpty(definition.UpgradesToRareId)
                    ? null
                    : GetCardSource(definition.UpgradesToRareId);
                if (!string.IsNullOrEmpty(definition.UpgradesToRareId) && rareUpgrade == null)
                    Plugin.Log.LogWarning($"[DataHelper] Card '{definition.Id}' rare upgrade reference '{definition.UpgradesToRareId}' was not found after card registration.");
                Traverse.Create(source).Field("upgrade").SetValue(new UpgradeData(
                    source.Upgrade.UpgradesTo1,
                    source.Upgrade.UpgradesTo2,
                    source.Upgrade.CardUpgraded,
                    source.Upgrade.UpgradedFrom,
                    rareUpgrade));
                realtime.Upgrade = source.Upgrade.Clone();

                var list = new List<CardDataNew>();
                foreach (var id in definition.AddCardList ?? new List<string>())
                {
                    var target = GetCardSource(id);
                    if (target == null)
                    {
                        Plugin.Log.LogWarning($"[DataHelper] Card '{definition.Id}' addCardList reference '{id}' was not found after card registration.");
                        continue;
                    }
                    list.Add(target);
                }
                source.CardManagement.AddCardList = list.ToArray();
                realtime.AddCardList = list.Select(card => card.Id).ToArray();
                ResolveLateCardRefs(definition, source);
                if (definition.HasItemData)
                {
                    var itemSo = GetItem(definition.Id);
                    if (itemSo != null) WireItemCardRefs(definition.ToItemDef(), itemSo);
                }
            }
        }

        private static void CopyCardAssets(CardDataNew card, CardDef d)
        {
            if (!string.IsNullOrEmpty(d.SpriteSource)) CopyCardVisuals(card, d.SpriteSource);
            if (!string.IsNullOrEmpty(d.SoundSource)) CopyCardSounds(card, d.SoundSource);
            if (!string.IsNullOrEmpty(d.PetModelSource)) CopyPetModel(card, d.PetModelSource);
        }

        public static CardDataNew MakeItemCard(ItemDef d, ItemData item)
        {
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CardDef>(Newtonsoft.Json.JsonConvert.SerializeObject(d.Card));
            copy.Id = d.Id;
            copy.Name = d.Name;
            bool isEnchantment = item != null && item.IsEnchantment;
            // Enchantments are Special-class cards in the base game; only plain equipment is forced to Item.
            if (!isEnchantment) copy.CardClass = Enums.CardClass.Item;
            else if (copy.CardClass == Enums.CardClass.Item) copy.CardClass = Enums.CardClass.Special;
            copy.CardRarity = d.Rarity;
            copy.CardType = d.CardType;
            copy.SpriteSource = d.SpriteSource;
            copy.SoundSource = d.SoundSource;
            copy.PetModelSource = d.PetModelSource;
            var card = BuildCard(copy);
            Traverse.Create(card).Field(item != null && item.IsEnchantment ? "itemEnchantment" : "item").SetValue(item);
            return card;
        }
    }
}
