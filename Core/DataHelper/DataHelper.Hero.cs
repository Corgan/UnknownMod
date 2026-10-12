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
            // The game keys heroes by SubClassName with whitespace removed and derives SubClassData.id the same way,
            // so the def Id must equal that key or skins and hero data referencing it will not resolve.
            string gameKey = NormalizeKey(d.SubClassName);
            sc.Id = gameKey;
            if (NormalizeKey(d.Id) != gameKey)
                Plugin.Log.LogError($"[DataHelper] Hero id '{d.Id}' must equal the game key '{gameKey}' (SubClassName without spaces, lowercase); skins and hero data using '{d.Id}' will not resolve.");
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
            ApplyHeroVisualOverrides(sc, d);
            sc.FluffOffsetX = d.FluffOffsetX;
            sc.FluffOffsetY = d.FluffOffsetY;
            sc.Female = d.Female;
            t.Field("stickerOffsetX").SetValue(d.StickerOffsetX);

            // Item
            if (!string.IsNullOrEmpty(d.ItemId))
                sc.Item = GetCardSource(d.ItemId);

            // MaxHp
            sc.MaxHp = d.MaxHp != null && d.MaxHp.Count > 0 ? d.MaxHp.ToArray() : new int[0];

            // Cards (starting deck)
            if (d.Cards != null && d.Cards.Count > 0)
            {
                // The hero-selection screen dereferences Cards[i].Card.Id, so unresolved rows are dropped.
                var hcList = new List<HeroCards>();
                for (int i = 0; i < d.Cards.Count; i++)
                {
                    var row = d.Cards[i];
                    var card = row == null || string.IsNullOrEmpty(row.CardId) ? null : GetCardSource(row.CardId);
                    if (card == null)
                    {
                        Plugin.Log.LogWarning($"[DataHelper] Hero '{d.Id}' starting card #{i + 1} ('{row?.CardId}') is blank or unresolved; skipped.");
                        continue;
                    }
                    hcList.Add(new HeroCards { Card = card, UnitsInDeck = row.UnitsInDeck });
                }
                sc.Cards = hcList.ToArray();
            }
            else
            {
                sc.Cards = new HeroCards[0];
            }

            // Singularity cards
            if (d.CardsSingularity != null && d.CardsSingularity.Count > 0)
            {
                var singArr = new CardDataNew[d.CardsSingularity.Count];
                for (int i = 0; i < d.CardsSingularity.Count; i++)
                    singArr[i] = GetCardSource(d.CardsSingularity[i]);
                sc.CardsSingularity = singArr;
            }
            else
            {
                sc.CardsSingularity = new CardDataNew[0];
            }

            // Trait tree
            if (!string.IsNullOrEmpty(d.Trait0)) sc.Trait0 = GetTrait(d.Trait0);
            if (!string.IsNullOrEmpty(d.Trait1A)) sc.Trait1A = GetTrait(d.Trait1A);
            if (!string.IsNullOrEmpty(d.Trait1ACard)) sc.Trait1ACard = GetCardSource(d.Trait1ACard);
            if (!string.IsNullOrEmpty(d.Trait1B)) sc.Trait1B = GetTrait(d.Trait1B);
            if (!string.IsNullOrEmpty(d.Trait1BCard)) sc.Trait1BCard = GetCardSource(d.Trait1BCard);
            if (!string.IsNullOrEmpty(d.Trait2A)) sc.Trait2A = GetTrait(d.Trait2A);
            if (!string.IsNullOrEmpty(d.Trait2B)) sc.Trait2B = GetTrait(d.Trait2B);
            if (!string.IsNullOrEmpty(d.Trait3A)) sc.Trait3A = GetTrait(d.Trait3A);
            if (!string.IsNullOrEmpty(d.Trait3ACard)) sc.Trait3ACard = GetCardSource(d.Trait3ACard);
            if (!string.IsNullOrEmpty(d.Trait3B)) sc.Trait3B = GetTrait(d.Trait3B);
            if (!string.IsNullOrEmpty(d.Trait3BCard)) sc.Trait3BCard = GetCardSource(d.Trait3BCard);
            if (!string.IsNullOrEmpty(d.Trait4A)) sc.Trait4A = GetTrait(d.Trait4A);
            if (!string.IsNullOrEmpty(d.Trait4B)) sc.Trait4B = GetTrait(d.Trait4B);

            // Must stay empty: AtOManager.BeginAdventure assigns every trait in this list to the hero up front, which would
            // fill every level slot before the player has chosen anything (vanilla subclasses leave it empty).
            sc.Traits = new List<TraitData>();

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
                sc.ReplaceCharacterData.CardsOnReplaceCharacter = MakeHeroCards(d.CardsOnReplaceCharacter);
            }
            if (!string.IsNullOrEmpty(d.PerksOnReplace))
                sc.ReplaceCharacterData.PerksOnReplace = d.PerksOnReplace;
            sc.ReplaceCharacterData.UseXpFromOriginal = d.UseXpFromOriginal;

            if (d.CardsOnReplaceCharacterInSingularity != null && d.CardsOnReplaceCharacterInSingularity.Count > 0)
            {
                var rcSingArr = MakeHeroCards(d.CardsOnReplaceCharacterInSingularity);
                sc.ReplaceCharacterData.CardsOnReplaceCharacterInSingularity = rcSingArr;
            }
            else
            {
                sc.ReplaceCharacterData.CardsOnReplaceCharacterInSingularity = new HeroCards[0];
            }

            return sc;
        }

        /// <summary>
        /// Second pass: heroes build before packs (packs reference subclasses), so challenge packs
        /// of a mod hero must be resolved once the packs exist. Applied to both Globals subclass copies.
        /// </summary>
        public static void WireHeroChallengePacks(HeroDef d)
        {
            string key = NormalizeKey(d.SubClassName);
            string[] ids = { d.ChallengePack0, d.ChallengePack1, d.ChallengePack2, d.ChallengePack3, d.ChallengePack4, d.ChallengePack5, d.ChallengePack6 };
            foreach (var field in new[] { "_SubClassSource", "_SubClass" })
            {
                var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, SubClassData>>(field).Value;
                if (dict == null || !dict.TryGetValue(key, out var sc) || sc == null) continue;
                var t = Traverse.Create(sc);
                for (int i = 0; i < ids.Length; i++) SetPack(t, "challengePack" + i, ids[i]);
            }
        }

        private static void SetPack(Traverse t, string field, string packId)
        {
            if (string.IsNullOrEmpty(packId)) return;
            var dict = Traverse.Create(Globals.Instance).Field<Dictionary<string, PackData>>("_PackDataSource").Value;
            if (dict != null && dict.TryGetValue(NormalizeKey(packId), out var pack))
                t.Field(field).SetValue(pack);
        }

        private static string GetPackId(PackData p) => p != null ? p.PackId ?? "" : "";
        private static string GetTriId(TraitData tr) => tr != null ? tr.Id ?? "" : "";
        private static string GetSourceCardId(CardDataNew card) => card != null ? card.Id ?? "" : "";

        private static HeroCards[] MakeHeroCards(List<HeroCardDef> definitions)
        {
            var cards = new HeroCards[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
            {
                cards[i] = new HeroCards
                {
                    Card = definitions[i] == null || string.IsNullOrEmpty(definitions[i].CardId)
                        ? null
                        : GetCardSource(definitions[i].CardId),
                    UnitsInDeck = definitions[i] != null ? definitions[i].UnitsInDeck : 0,
                };
            }
            return cards;
        }
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
                Trait1ACard = GetSourceCardId(sc.Trait1ACard),
                Trait1B = GetTriId(sc.Trait1B),
                Trait1BCard = GetSourceCardId(sc.Trait1BCard),
                Trait2A = GetTriId(sc.Trait2A),
                Trait2B = GetTriId(sc.Trait2B),
                Trait3A = GetTriId(sc.Trait3A),
                Trait3ACard = GetSourceCardId(sc.Trait3ACard),
                Trait3B = GetTriId(sc.Trait3B),
                Trait3BCard = GetSourceCardId(sc.Trait3BCard),
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
            var replaceData = sc.ReplaceCharacterData;
            d.PerksOnReplace = replaceData != null ? replaceData.PerksOnReplace ?? "" : "";
            d.UseXpFromOriginal = replaceData != null && replaceData.UseXpFromOriginal;
            var cardsOnReplace = replaceData != null ? replaceData.CardsOnReplaceCharacter : null;
            if (cardsOnReplace != null)
            {
                foreach (var hc in cardsOnReplace)
                {
                    if (hc != null)
                    {
                        d.CardsOnReplaceCharacter.Add(new HeroCardDef
                        {
                            CardId = GetSourceCardId(hc.Card),
                            UnitsInDeck = hc.UnitsInDeck,
                        });
                    }
                }
            }

            if (replaceData?.CardsOnReplaceCharacterInSingularity != null)
            {
                foreach (var hc in replaceData.CardsOnReplaceCharacterInSingularity)
                {
                    if (hc != null)
                    {
                        d.CardsOnReplaceCharacterInSingularity.Add(new HeroCardDef
                        {
                            CardId = GetSourceCardId(hc.Card),
                            UnitsInDeck = hc.UnitsInDeck,
                        });
                    }
                }
            }

            // MaxHp
            if (sc.MaxHp != null && sc.MaxHp.Length > 0)
                d.MaxHp = new List<int>(sc.MaxHp);

            // Cards
            if (sc.Cards != null)
            {
                foreach (var hc in sc.Cards)
                {
                    if (hc != null)
                    {
                        d.Cards.Add(new HeroCardDef
                        {
                            CardId = GetSourceCardId(hc.Card),
                            UnitsInDeck = hc.UnitsInDeck,
                        });
                    }
                }
            }

            // Singularity cards
            if (sc.CardsSingularity != null)
            {
                foreach (var c in sc.CardsSingularity)
                {
                    if (c != null && !string.IsNullOrEmpty(c.Id))
                        d.CardsSingularity.Add(c.Id ?? "");
                }
            }

            return d;
        }
    }
}
