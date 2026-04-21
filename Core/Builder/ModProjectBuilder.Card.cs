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

            // ── Auto-mapped fields (single source of truth: FieldMappings.Card) ──
            FieldMapper.Apply(FieldMappings.Card, d, card);

            // ── Edge cases: identity ─────────────────────────────
            t.Field("internalId").SetValue(d.Id);

            // ── Edge cases: arrays ───────────────────────────────
            t.Field("cardTypeAux").SetValue(d.CardTypeAux ?? Array.Empty<Enums.CardType>());
            t.Field("discardCardTypeAux").SetValue(d.DiscardCardTypeAux ?? Array.Empty<Enums.CardType>());
            t.Field("addCardTypeAux").SetValue(d.AddCardTypeAux ?? Array.Empty<Enums.CardType>());

            // ── Edge cases: public properties ────────────────────
            card.AddCardFromVanishPile = d.AddCardFromVanishPile;
            card.PetActivation = d.PetActivation;
            card.PetBonusDamageType = d.PetBonusDamageType;
            card.PetBonusDamageAmount = d.PetBonusDamageAmount;

            // ── Edge cases: Vector2 composition ──────────────────
            t.Field("petOffset").SetValue(new UnityEngine.Vector2(d.PetOffsetX, d.PetOffsetY));
            t.Field("petSize").SetValue(new UnityEngine.Vector2(d.PetSizeX, d.PetSizeY));

            // ── Edge cases: SelfCharges overrides ────────────────
            if (d.CurseSelfCharges > 0 && d.CurseCharges == 0)
                t.Field("curseCharges").SetValue(d.CurseSelfCharges);
            if (d.CurseSelf2Charges > 0 && d.Curse2Charges == 0)
                t.Field("curseCharges2").SetValue(d.CurseSelf2Charges);
            if (d.CurseSelf3Charges > 0 && d.Curse3Charges == 0)
                t.Field("curseCharges3").SetValue(d.CurseSelf3Charges);
            if (d.AuraSelfCharges > 0 && d.AuraCharges == 0)
                t.Field("auraCharges").SetValue(d.AuraSelfCharges);
            if (d.AuraSelf2Charges > 0 && d.Aura2Charges == 0)
                t.Field("auraCharges2").SetValue(d.AuraSelf2Charges);
            if (d.AuraSelf3Charges > 0 && d.Aura3Charges == 0)
                t.Field("auraCharges3").SetValue(d.AuraSelf3Charges);

            // ── Edge cases: CardData[] list ──────────────────────
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

            // ── Edge cases: init arrays ──────────────────────────
            t.Field("preDescriptionArgs").SetValue(new string[0]);
            t.Field("descriptionArgs").SetValue(new string[0]);
            t.Field("postDescriptionArgs").SetValue(new string[0]);

            // ── Edge cases: sprite + sounds + pet model ─────────
            if (!string.IsNullOrEmpty(d.SpriteSource))
                DataHelper.CopyCardVisuals(card, d.SpriteSource);
            if (!string.IsNullOrEmpty(d.SoundSource))
                DataHelper.CopyCardSounds(card, d.SoundSource);
            // Pet model from game card (SpriteSkin-based pets are wired in WirePetModels)
            if (!string.IsNullOrEmpty(d.PetModelSource) && DataHelper.GetCard(d.PetModelSource) != null)
                DataHelper.CopyPetModel(card, d.PetModelSource);

            // ── Full card init (precalc fields, KeyNotes, description, target) ─
            // InitClone sets: damagePreCalculated*, healPreCalculated*,
            //   energyCostOriginal, damageTypeOriginal, effectRequired,
            //   and all KeyNotes for auras/curses/innate/vanish/energy.
            // InitClone2 sets: effectRequired KeyNote, description, target, rarity.
            try { card.InitClone(d.Id); }
            catch { /* KeyNotes/Globals not ready */ }
            // Clear cached description so InitClone2 regenerates it
            Globals.Instance?.CardsDescriptionNormalized?.Remove(d.Id);
            try { card.InitClone2(); }
            catch { /* Texts not ready */ }

            return card;
        }

        /// <summary>
        /// Snapshot a live CardData SO into a CardDef for override editing.
        /// </summary>
        public static CardDef SnapshotCard(CardData card)
        {
            if (card == null) return null;
            var d = new CardDef();

            // ── Auto-mapped fields (single source of truth: FieldMappings.Card) ──
            FieldMapper.Snapshot(FieldMappings.Card, card, d);

            // ── Edge cases: arrays ───────────────────────────────
            var t = Traverse.Create(card);
            d.CardTypeAux = t.Field<Enums.CardType[]>("cardTypeAux").Value ?? Array.Empty<Enums.CardType>();
            d.DiscardCardTypeAux = t.Field<Enums.CardType[]>("discardCardTypeAux").Value ?? Array.Empty<Enums.CardType>();
            d.AddCardTypeAux = t.Field<Enums.CardType[]>("addCardTypeAux").Value ?? Array.Empty<Enums.CardType>();

            // ── Edge cases: public properties ────────────────────
            d.AddCardFromVanishPile = card.AddCardFromVanishPile;
            d.PetActivation = card.PetActivation;
            d.PetBonusDamageType = card.PetBonusDamageType;
            d.PetBonusDamageAmount = card.PetBonusDamageAmount;

            // ── Edge cases: Vector2 decomposition ────────────────
            var petOff = t.Field<UnityEngine.Vector2>("petOffset").Value;
            d.PetOffsetX = petOff.x;
            d.PetOffsetY = petOff.y;
            var petSz = t.Field<UnityEngine.Vector2>("petSize").Value;
            d.PetSizeX = petSz.x;
            d.PetSizeY = petSz.y;

            // ── Edge cases: SelfCharges aliases ──────────────────
            d.CurseSelfCharges = d.CurseCharges;
            d.CurseSelf2Charges = d.Curse2Charges;
            d.CurseSelf3Charges = d.Curse3Charges;
            d.AuraSelfCharges = d.AuraCharges;
            d.AuraSelf2Charges = d.Aura2Charges;
            d.AuraSelf3Charges = d.Aura3Charges;

            // ── Edge cases: computed / alias fields ───────────────
            d.IsUpgraded = d.CardUpgraded != Enums.CardUpgraded.No;
            // BaseCard from game data may be PascalCase; normalize to lowercase
            d.BaseCardId = (d.BaseCard ?? "").ToLower();

            // ── Edge case: sprite reference (not field-mapped) ───
            var sprite = card.Sprite;
            if (sprite != null && string.IsNullOrEmpty(d.SpriteSource))
                d.SpriteSource = sprite.name;

            // ── Edge cases: CardData[] list ──────────────────────
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

            return d;
        }
    }
}
