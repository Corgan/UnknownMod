using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — Hero (SubClass) Builders
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        /// <summary>Create a complete SubClassData SO from a HeroDef.</summary>
        public static SubClassData MakeFullHero(HeroDef d)
        {
            var sc = ScriptableObject.CreateInstance<SubClassData>();
            var t = Traverse.Create(sc);

            // Identity
            sc.SubClassName = d.SubClassName;
            sc.Id = d.Id;
            sc.CharacterName = d.CharacterName;
            sc.CharacterDescription = d.CharacterDescription;
            sc.CharacterDescriptionStrength = d.CharacterDescriptionStrength;
            sc.MainCharacter = d.MainCharacter;
            sc.InitialUnlock = d.InitialUnlock;
            sc.SourceCharacterName = d.CharacterName;
            t.Field("sku").SetValue(d.Sku ?? "");

            // Class
            sc.HeroClass = d.HeroClass;
            sc.HeroClassSecondary = d.HeroClassSecondary;
            sc.HeroClassThird = d.HeroClassThird;

            // Stats
            sc.OrderInList = d.OrderInList;
            sc.Blocked = d.Blocked;
            sc.Speed = d.Speed;
            sc.Hp = d.Hp;
            sc.Energy = d.Energy;
            sc.EnergyTurn = d.EnergyTurn;

            // Resistances
            sc.ResistSlashing = d.ResSlash;
            sc.ResistBlunt = d.ResBlunt;
            sc.ResistPiercing = d.ResPierce;
            sc.ResistFire = d.ResFire;
            sc.ResistCold = d.ResCold;
            sc.ResistLightning = d.ResLight;
            sc.ResistMind = d.ResMind;
            sc.ResistHoly = d.ResHoly;
            sc.ResistShadow = d.ResShadow;

            // Visuals
            if (!string.IsNullOrEmpty(d.SpriteSource))
                CopyHeroVisuals(sc, d.SpriteSource);
            sc.FluffOffsetX = d.FluffOffsetX;
            sc.FluffOffsetY = d.FluffOffsetY;
            sc.Female = d.Female;
            t.Field("stickerOffsetX").SetValue(d.StickerOffsetX);

            // Item
            if (!string.IsNullOrEmpty(d.ItemId))
                sc.Item = GetCard(d.ItemId);

            // MaxHp
            sc.MaxHp = d.MaxHp != null && d.MaxHp.Count > 0 ? d.MaxHp.ToArray() : new int[0];

            // Cards (starting deck)
            if (d.Cards != null && d.Cards.Count > 0)
            {
                var hcArr = new HeroCards[d.Cards.Count];
                for (int i = 0; i < d.Cards.Count; i++)
                {
                    hcArr[i] = new HeroCards();
                    if (!string.IsNullOrEmpty(d.Cards[i].CardId))
                        hcArr[i].Card = GetCard(d.Cards[i].CardId);
                    hcArr[i].UnitsInDeck = d.Cards[i].UnitsInDeck;
                }
                sc.Cards = hcArr;
            }
            else
            {
                sc.Cards = new HeroCards[0];
            }

            // Singularity cards
            if (d.CardsSingularity != null && d.CardsSingularity.Count > 0)
            {
                var singArr = new CardData[d.CardsSingularity.Count];
                for (int i = 0; i < d.CardsSingularity.Count; i++)
                    singArr[i] = GetCard(d.CardsSingularity[i]);
                sc.CardsSingularity = singArr;
            }
            else
            {
                sc.CardsSingularity = new CardData[0];
            }

            // Trait tree
            if (!string.IsNullOrEmpty(d.Trait0)) sc.Trait0 = GetTrait(d.Trait0);
            if (!string.IsNullOrEmpty(d.Trait1A)) sc.Trait1A = GetTrait(d.Trait1A);
            if (!string.IsNullOrEmpty(d.Trait1ACard)) sc.Trait1ACard = GetCard(d.Trait1ACard);
            if (!string.IsNullOrEmpty(d.Trait1B)) sc.Trait1B = GetTrait(d.Trait1B);
            if (!string.IsNullOrEmpty(d.Trait1BCard)) sc.Trait1BCard = GetCard(d.Trait1BCard);
            if (!string.IsNullOrEmpty(d.Trait2A)) sc.Trait2A = GetTrait(d.Trait2A);
            if (!string.IsNullOrEmpty(d.Trait2B)) sc.Trait2B = GetTrait(d.Trait2B);
            if (!string.IsNullOrEmpty(d.Trait3A)) sc.Trait3A = GetTrait(d.Trait3A);
            if (!string.IsNullOrEmpty(d.Trait3ACard)) sc.Trait3ACard = GetCard(d.Trait3ACard);
            if (!string.IsNullOrEmpty(d.Trait3B)) sc.Trait3B = GetTrait(d.Trait3B);
            if (!string.IsNullOrEmpty(d.Trait3BCard)) sc.Trait3BCard = GetCard(d.Trait3BCard);
            if (!string.IsNullOrEmpty(d.Trait4A)) sc.Trait4A = GetTrait(d.Trait4A);
            if (!string.IsNullOrEmpty(d.Trait4B)) sc.Trait4B = GetTrait(d.Trait4B);

            // Challenge packs
            SetPack(t, "challengePack0", d.ChallengePack0);
            SetPack(t, "challengePack1", d.ChallengePack1);
            SetPack(t, "challengePack2", d.ChallengePack2);
            SetPack(t, "challengePack3", d.ChallengePack3);
            SetPack(t, "challengePack4", d.ChallengePack4);
            SetPack(t, "challengePack5", d.ChallengePack5);
            SetPack(t, "challengePack6", d.ChallengePack6);

            // Character replacement
            if (d.CardsOnReplaceCharacter != null && d.CardsOnReplaceCharacter.Count > 0)
            {
                var rcArr = new HeroCards[d.CardsOnReplaceCharacter.Count];
                for (int i = 0; i < d.CardsOnReplaceCharacter.Count; i++)
                {
                    rcArr[i] = new HeroCards();
                    if (!string.IsNullOrEmpty(d.CardsOnReplaceCharacter[i].CardId))
                        rcArr[i].Card = GetCard(d.CardsOnReplaceCharacter[i].CardId);
                    rcArr[i].UnitsInDeck = d.CardsOnReplaceCharacter[i].UnitsInDeck;
                }
                t.Field("cardsOnReplaceCharacter").SetValue(rcArr);
            }
            if (!string.IsNullOrEmpty(d.PerksOnReplace))
                t.Field("perksOnReplace").SetValue(d.PerksOnReplace);
            if (d.UseXpFromOriginal)
                t.Field("useXpFromOriginal").SetValue(true);

            return sc;
        }

        private static void SetPack(Traverse t, string field, string packId)
        {
            if (string.IsNullOrEmpty(packId)) return;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PackData>>("_PackSource").Value;
            if (dict != null && dict.TryGetValue(NormalizeKey(packId), out var pack))
                t.Field(field).SetValue(pack);
        }

        private static string GetPackId(PackData p) => p != null ? p.PackId ?? "" : "";
        private static string GetTriId(TraitData tr) => tr != null ? tr.Id ?? "" : "";

        /// <summary>Snapshot a SubClassData SO back into a HeroDef.</summary>
        public static HeroDef SnapshotHero(SubClassData sc)
        {
            var t = Traverse.Create(sc);
            var d = new HeroDef
            {
                Id = sc.Id ?? "",
                SubClassName = sc.SubClassName ?? "",
                CharacterName = sc.CharacterName ?? "",
                CharacterDescription = sc.CharacterDescription ?? "",
                CharacterDescriptionStrength = sc.CharacterDescriptionStrength ?? "",
                MainCharacter = sc.MainCharacter,
                InitialUnlock = sc.InitialUnlock,
                Sku = t.Field<string>("sku").Value ?? "",

                SpriteSource = sc.SubClassName != null
                    ? NormalizeKey(sc.SubClassName)
                    : "",
                FluffOffsetX = sc.FluffOffsetX,
                FluffOffsetY = sc.FluffOffsetY,
                Female = sc.Female,
                StickerOffsetX = t.Field<float>("stickerOffsetX").Value,

                HeroClass = sc.HeroClass,
                HeroClassSecondary = sc.HeroClassSecondary,
                HeroClassThird = sc.HeroClassThird,

                OrderInList = sc.OrderInList,
                Blocked = sc.Blocked,
                Speed = sc.Speed,
                Hp = sc.Hp,
                Energy = sc.Energy,
                EnergyTurn = sc.EnergyTurn,

                ResSlash = sc.ResistSlashing,
                ResBlunt = sc.ResistBlunt,
                ResPierce = sc.ResistPiercing,
                ResFire = sc.ResistFire,
                ResCold = sc.ResistCold,
                ResLight = sc.ResistLightning,
                ResMind = sc.ResistMind,
                ResHoly = sc.ResistHoly,
                ResShadow = sc.ResistShadow,

                ItemId = sc.Item != null ? sc.Item.Id ?? "" : "",

                Trait0 = GetTriId(sc.Trait0),
                Trait1A = GetTriId(sc.Trait1A),
                Trait1ACard = GetCardId(sc.Trait1ACard),
                Trait1B = GetTriId(sc.Trait1B),
                Trait1BCard = GetCardId(sc.Trait1BCard),
                Trait2A = GetTriId(sc.Trait2A),
                Trait2B = GetTriId(sc.Trait2B),
                Trait3A = GetTriId(sc.Trait3A),
                Trait3ACard = GetCardId(sc.Trait3ACard),
                Trait3B = GetTriId(sc.Trait3B),
                Trait3BCard = GetCardId(sc.Trait3BCard),
                Trait4A = GetTriId(sc.Trait4A),
                Trait4B = GetTriId(sc.Trait4B),

                ChallengePack0 = GetPackId(sc.ChallengePack0),
                ChallengePack1 = GetPackId(sc.ChallengePack1),
                ChallengePack2 = GetPackId(sc.ChallengePack2),
                ChallengePack3 = GetPackId(sc.ChallengePack3),
                ChallengePack4 = GetPackId(sc.ChallengePack4),
                ChallengePack5 = GetPackId(sc.ChallengePack5),
                ChallengePack6 = GetPackId(sc.ChallengePack6),
            };

            // Character replacement
            var cardsOnReplace = t.Field<HeroCards[]>("cardsOnReplaceCharacter").Value;
            if (cardsOnReplace != null)
            {
                foreach (var hc in cardsOnReplace)
                {
                    d.CardsOnReplaceCharacter.Add(new HeroCardDef
                    {
                        CardId = hc.Card != null ? hc.Card.Id ?? "" : "",
                        UnitsInDeck = hc.UnitsInDeck,
                    });
                }
            }
            d.PerksOnReplace = t.Field<string>("perksOnReplace").Value ?? "";
            d.UseXpFromOriginal = t.Field<bool>("useXpFromOriginal").Value;

            // MaxHp
            if (sc.MaxHp != null && sc.MaxHp.Length > 0)
                d.MaxHp = new List<int>(sc.MaxHp);

            // Cards
            if (sc.Cards != null)
            {
                foreach (var hc in sc.Cards)
                {
                    d.Cards.Add(new HeroCardDef
                    {
                        CardId = hc.Card != null ? hc.Card.Id ?? "" : "",
                        UnitsInDeck = hc.UnitsInDeck,
                    });
                }
            }

            // Singularity cards
            if (sc.CardsSingularity != null)
            {
                foreach (var c in sc.CardsSingularity)
                {
                    if (c != null && !string.IsNullOrEmpty(c.Id))
                        d.CardsSingularity.Add(c.Id);
                }
            }

            return d;
        }
    }
}
