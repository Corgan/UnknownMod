using System.Collections.Generic;
using System.Linq;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Semantic checks on cards, heroes, skins, NPCs, traits and auras: combinations that load without error but break
    /// at runtime or show blank/wrong text in game. ReferenceValidator covers ids that point at nothing and
    /// EventValidator covers events.
    /// </summary>
    public static class ContentValidator
    {
        public static List<DiagMessage> Validate(ModProject p)
        {
            var r = new List<DiagMessage>();
            if (p == null) return r;
            foreach (var c in p.Cards.Values.Concat(p.CardPatches.Values)) CheckCard(c, r);
            foreach (var h in p.Heroes.Values) CheckHero(p, h, r, false);
            foreach (var h in p.HeroPatches.Values) CheckHero(p, h, r, true);
            foreach (var s in p.Skins.Values.Concat(p.SkinPatches.Values)) CheckSkin(p, s, r);
            foreach (var n in p.Npcs.Values.Concat(p.NpcPatches.Values)) CheckNpc(n, r);
            foreach (var t in p.Traits.Values.Concat(p.TraitPatches.Values)) CheckTrait(t, r);
            foreach (var a in p.AuraCurses.Values.Concat(p.AuraCursePatches.Values)) CheckAura(a, r);
            foreach (var z in p.Zones.Values) CheckZone(z, r);
            foreach (var t in p.ChallengeTraits.Values.Concat(p.ChallengeTraitPatches.Values)) CheckChallengeTrait(t, r);
            foreach (var e in p.Events.Values.Concat(p.EventPatches.Values)) CheckEvent(e, r);
            foreach (var n in p.PerkNodes.Values.Concat(p.PerkNodePatches.Values)) CheckPerkNode(p, n, r);
            foreach (var l in p.Loot.Values.Concat(p.LootPatches.Values)) CheckLoot(p, l, r);
            return r;
        }

        private static void Err(List<DiagMessage> r, string at, string msg) => r.Add(new DiagMessage(DiagSeverity.Error, $"{at}: {msg}"));
        private static void Warn(List<DiagMessage> r, string at, string msg) => r.Add(new DiagMessage(DiagSeverity.Warning, $"{at}: {msg}"));

        private static bool IsItemType(Enums.CardType t) =>
            t == Enums.CardType.Weapon || t == Enums.CardType.Armor || t == Enums.CardType.Jewelry
            || t == Enums.CardType.Accesory || t == Enums.CardType.Pet || t == Enums.CardType.Enchantment
            || t == Enums.CardType.Food || t == Enums.CardType.Flask;

        private static void CheckCard(CardDef c, List<DiagMessage> r)
        {
            if (c == null) return;
            string at = $"Card/{c.Id}";
            if (string.IsNullOrEmpty(c.Id)) { Err(r, at, "has no id"); return; }
            // Globals.GetCardData does Sanitize(id).Split("_")[0], so an id with an underscore resolves to the wrong card.
            if (c.Id.Contains("_"))
                Err(r, at, "card ids must not contain '_' (the game splits instance ids '<cardId>_<guid>' on it and would look up the wrong card)");
            if (c.Id.Contains("'"))
                Err(r, at, "card ids must not contain an apostrophe (the game strips it when looking cards up, so the registered id is never found)");
            if (c.Id.StartsWith("nightmareechotemplate-", System.StringComparison.OrdinalIgnoreCase))
                Err(r, at, "'NightmareEchoTemplate-' is reserved by the game for generated echo cards");
            if (c.Id != c.Id.ToLowerInvariant() || c.Id.Contains(" "))
                Warn(r, at, "id should be lowercase with no spaces; the game normalises it and lookups by the original text may miss");
            if (string.IsNullOrWhiteSpace(c.Name)) Warn(r, at, "Name is empty, the card shows no title");
            if (string.IsNullOrWhiteSpace(c.Description) && c.CardType != Enums.CardType.None)
                Warn(r, at, "Description is empty");
            if (IsItemType(c.CardType) && c.Item == null)
                Warn(r, at, $"CardType {c.CardType} is an item type but the card has no Item block, so it has no stats or effects");
            if (!IsItemType(c.CardType) && c.Item != null)
                Warn(r, at, $"has an Item block but CardType is {c.CardType}; item effects only run for item card types");
            if (c.Playable && c.EnergyCost < 0)
                Warn(r, at, "negative EnergyCost");
            CheckCardDependents(c, at, r);
            if (c.Item != null)
            {
                if (c.Item.ActivationSelf != Enums.EventActivation.None && c.Item.ActivationSelf != Enums.EventActivation.OnKill)
                    Warn(r, at, $"Item.ActivationSelf {c.Item.ActivationSelf} is only read by the game for OnKill; other values do nothing");
                if (c.Item.ShiftElementalResistance)
                    Warn(r, at, "Item.ShiftElementalResistance is declared by the game but not implemented, so it has no effect");
            }
        }

        // Values that only matter alongside another field. They load fine but are ignored (or, worse, create a 0-value
        // effect line), so flag them as leftovers.
        private static void CheckCardDependents(CardDef c, string at, List<DiagMessage> r)
        {
            bool Has(string slot) => c.EffectExtras != null && c.EffectExtras.TryGetValue(slot, out var x) && x != null && !x.IsDefault;
            void Orphan(string what) => Warn(r, at, what + " (ignored; clear it)");

            if (c.Damage == 0 && c.DamageType != Enums.DamageType.None && !Has("Damage") && c.DamageEnergyBonus == 0 && c.DamageSelf == 0)
                Orphan($"DamageType {c.DamageType} is set but Damage is 0");
            if (c.Damage2 == 0 && c.DamageType2 != Enums.DamageType.None && !Has("Damage2") && c.DamageEnergyBonus == 0)
                Orphan($"DamageType2 {c.DamageType2} is set but Damage2 is 0");
            if (c.DamageSelf == 0 && (c.DamageSelfType != null && c.DamageSelfType != Enums.DamageType.None) && !Has("DamageSelf"))
                Orphan("DamageSelfType is set but DamageSelf is 0");

            void Slot(string slot, string id, int charges)
            {
                if (string.IsNullOrEmpty(id) && charges != 0) Orphan($"{slot}Charges is {charges} but {slot} is empty");
                else if (!string.IsNullOrEmpty(id) && charges == 0 && !Has(slot)
                         && !string.Equals(c.AcEnergyBonus, id, System.StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(c.AcEnergyBonus2, id, System.StringComparison.OrdinalIgnoreCase))
                    Warn(r, at, $"{slot} '{id}' has 0 charges, so the effect is skipped; set charges or remove it");
            }
            Slot("Aura", c.Aura, c.AuraCharges); Slot("Aura2", c.Aura2, c.Aura2Charges); Slot("Aura3", c.Aura3, c.Aura3Charges);
            Slot("AuraSelf", c.AuraSelf, c.AuraSelfCharges); Slot("AuraSelf2", c.AuraSelf2, c.AuraSelf2Charges); Slot("AuraSelf3", c.AuraSelf3, c.AuraSelf3Charges);
            Slot("Curse", c.Curse, c.CurseCharges); Slot("Curse2", c.Curse2, c.Curse2Charges); Slot("Curse3", c.Curse3, c.Curse3Charges);
            Slot("CurseSelf", c.CurseSelf, c.CurseSelfCharges); Slot("CurseSelf2", c.CurseSelf2, c.CurseSelf2Charges); Slot("CurseSelf3", c.CurseSelf3, c.CurseSelf3Charges);

            if (c.DiscardCard == 0 && (c.DiscardCardType != Enums.CardType.None || c.DiscardCardAutomatic))
                Orphan("DiscardCardType/Automatic is set but DiscardCard is 0");
            if (c.AddCard == 0 && (c.AddCardType != Enums.CardType.None || !string.IsNullOrEmpty(c.AddCardId) || c.AddCardChoose != 0))
                Orphan("AddCardType/AddCardId/AddCardChoose is set but AddCard is 0");
            // EffectRepeatTarget is deliberately not checked: the game reads it even for single casts (Same vs NoRepeat changes the cast flow).
            if (c.EffectRepeat <= 1 && c.EffectRepeatModificator != 0)
                Orphan("EffectRepeatModificator is set but EffectRepeat is 1 or less");
            if (string.IsNullOrEmpty(c.SummonUnitId) && c.SummonNum != 0)
                Orphan("SummonNum is set but SummonUnitId is empty");
            if (c.PetActivation == Enums.ActivePets.None && (c.PetBonusDamageAmount != 0 || c.PetBonusDamageType != Enums.DamageType.None))
                Orphan("PetBonusDamage is set but PetActivation is None");
        }

        private static bool KnownCard(ModProject p, string id)
        {
            string key = DataHelper.NormalizeKey(id);
            return RefIds.Cards(p).Concat(RefIds.Items(p)).Any(c => DataHelper.NormalizeKey(c) == key);
        }

        private static void CheckPerkNode(ModProject p, PerkNodeDef n, List<DiagMessage> r)
        {
            if (n == null) return;
            string at = $"PerkNode/{n.Id}";
            if (string.IsNullOrEmpty(n.Perk)) return;
            string key = DataHelper.NormalizeKey(n.Perk);
            if (!RefIds.Perks(p).Any(x => DataHelper.NormalizeKey(x) == key))
                Err(r, at, $"Perk '{n.Perk}' matches no perk; the perk tree reads Perk.Id on connected nodes and fails on a missing perk");
        }

        private static void CheckLoot(ModProject p, LootDef l, List<DiagMessage> r)
        {
            if (l?.Items == null) return;
            string at = $"Loot/{l.Id}";
            for (int i = 0; i < l.Items.Count; i++)
            {
                var it = l.Items[i];
                if (it == null || string.IsNullOrEmpty(it.CardId)) continue;
                if (!KnownCard(p, it.CardId))
                    Err(r, at, $"item #{i + 1} CardId '{it.CardId}' matches no card; the game would roll random loot for that row instead");
            }
        }

        private static void CheckHero(ModProject p, HeroDef h, List<DiagMessage> r, bool isPatch)
        {
            if (h == null) return;
            string at = $"Hero/{h.Id}";
            string expect = DataHelper.NormalizeKey(h.SubClassName);
            if (string.IsNullOrEmpty(h.SubClassName))
                Err(r, at, "SubClassName is empty; the hero id is derived from it");
            else if (h.Id != expect)
                Err(r, at, $"id must equal SubClassName with spaces removed and lowercased ('{expect}'); otherwise skin.SkinSubclass resolves to null and the game fails at boot");

            if (string.IsNullOrWhiteSpace(h.CharacterName)) Warn(r, at, "CharacterName is empty; the hero panel and roster show a blank name");
            if (string.IsNullOrWhiteSpace(h.CharacterDescription)) Warn(r, at, "CharacterDescription is empty; the hero panel text is blank");
            if (h.Hp <= 0 && (h.MaxHp == null || h.MaxHp.Count == 0)) Warn(r, at, "Hp is 0 and MaxHp is empty, the hero would die immediately");
            if (h.Cards == null || h.Cards.Count == 0) Warn(r, at, "has no starting Cards");
            else
                for (int i = 0; i < h.Cards.Count; i++)
                    if (h.Cards[i] == null || string.IsNullOrEmpty(h.Cards[i].CardId))
                        Err(r, at, $"starting card #{i + 1} has no card selected; the row is skipped at build time");
            if (!isPatch && string.IsNullOrEmpty(h.ItemId))
                Err(r, at, "ItemId is empty; the hero-selection screen reads the starting item and fails without it");
            else if (!string.IsNullOrEmpty(h.ItemId) && !KnownCard(p, h.ItemId))
                Err(r, at, $"ItemId '{h.ItemId}' matches no card or item; the hero-selection screen reads the starting item and fails without it");
            if (isPatch) return; // the skin and HeroData of a patched base-game hero live in the base game

            var skins = p.Skins.Values.Where(s => s != null && DataHelper.NormalizeKey(s.SkinSubclass) == h.Id).ToList();
            if (skins.Count == 0)
                Warn(r, at, "no skin points to this hero (SkinSubclass); it will have no model or portrait");
            else if (!skins.Any(s => s.BaseSkin))
                Warn(r, at, "none of its skins is marked BaseSkin, the game's default skin lookup finds nothing");
            else if (skins.Count(s => s.BaseSkin) > 1)
                Warn(r, at, "more than one skin is marked BaseSkin");

            if (!p.HeroDataEntries.Values.Any(d => d != null && DataHelper.NormalizeKey(d.HeroSubClassId) == h.Id))
                Warn(r, at, "no HeroData entry references this hero (HeroSubClassId); it cannot be bought or selected as a character");
        }

        private static void CheckSkin(ModProject p, SkinDef s, List<DiagMessage> r)
        {
            if (s == null) return;
            string at = $"Skin/{s.Id}";
            if (string.IsNullOrEmpty(s.SkinSubclass))
                Err(r, at, "SkinSubclass is empty; the skin belongs to no hero");
            bool hasRig = !string.IsNullOrEmpty(s.SpriteSource) || !string.IsNullOrEmpty(s.VisualGraphId);
            if (!hasRig) Warn(r, at, "no SpriteSource or VisualGraphId; the hero has no animated model");
            bool hasArt = !string.IsNullOrEmpty(s.PortraitSource) || !string.IsNullOrEmpty(s.Portrait)
                          || !string.IsNullOrEmpty(s.PortraitLarge) || !string.IsNullOrEmpty(s.SpriteSource);
            if (!hasArt) Warn(r, at, "no portrait: set PortraitSource or a Portrait sprite name");
            if (!string.IsNullOrEmpty(s.VisualGraphId) && string.IsNullOrEmpty(s.PortraitSource)
                && string.IsNullOrEmpty(s.Portrait) && string.IsNullOrEmpty(s.PortraitLarge))
                Warn(r, at, "uses a graft rig with no PortraitSource/Portrait, so the portrait follows the graft's host model; set PortraitSource to keep it fixed");
            if (!string.IsNullOrEmpty(s.VisualGraphId) && !p.VisualGraphs.ContainsKey(s.VisualGraphId) && !p.VisualGraphPatches.ContainsKey(s.VisualGraphId))
                Warn(r, at, $"VisualGraphId '{s.VisualGraphId}' is not defined in this mod (fine only if another mod provides it)");
            if (s.PortraitScale <= 0f) Err(r, at, "PortraitScale must be above 0");
            if (s.HeroSelectionScreenScale <= 0f) Err(r, at, "HeroSelectionScreenScale must be above 0");
        }

        private static void CheckNpc(NpcDef n, List<DiagMessage> r)
        {
            if (n == null) return;
            string at = $"Npc/{n.Id}";
            bool derived = !string.IsNullOrEmpty(n.BaseNpcId);
            if (!derived)
            {
                if (n.Hp <= 0) Err(r, at, "Hp must be above 0");
                if (n.AiCards == null || n.AiCards.Count == 0)
                    Err(r, at, "has no AiCards; the NPC would have nothing to play (SetInitialCards)");
                if (string.IsNullOrEmpty(n.SpriteSource) && string.IsNullOrEmpty(n.VisualGraphId))
                    Warn(r, at, "no SpriteSource or VisualGraphId; the NPC has no model");
            }
            if (string.IsNullOrWhiteSpace(n.Name)) Warn(r, at, "Name is empty");
            if (n.PortraitScale <= 0f) Err(r, at, "PortraitScale must be above 0");
            if (n.HpMult <= 0f) Err(r, at, "HpMult must be above 0");
            if (n.IsBoss && string.IsNullOrEmpty(n.Id)) Err(r, at, "boss without id");
        }

        private static void CheckTrait(TraitDef t, List<DiagMessage> r)
        {
            if (t == null) return;
            string at = $"Trait/{t.Id}";
            if (string.IsNullOrWhiteSpace(t.TraitName)) Warn(r, at, "TraitName is empty");
            if (string.IsNullOrWhiteSpace(t.Description)) Warn(r, at, "Description is empty");
        }

        private static void CheckAura(AuraCurseDef a, List<DiagMessage> r)
        {
            if (a == null) return;
            string at = $"AuraCurse/{a.Id}";
            if (string.IsNullOrWhiteSpace(a.ACName)) Warn(r, at, "ACName is empty");
            if (string.IsNullOrWhiteSpace(a.Description)) Warn(r, at, "Description is empty, tooltips show nothing");
            if (string.IsNullOrEmpty(a.Sprite)) Warn(r, at, "no Sprite, the status icon is blank");
        }

        private static void CheckZone(ZoneDef z, List<DiagMessage> r)
        {
            if (z == null) return;
            if (string.IsNullOrWhiteSpace(z.ZoneName)) Warn(r, $"Zone/{z.ZoneId}", "ZoneName is empty; node popups and the run tome show a blank zone");
        }

        // Reply and outcome values that only take effect with another field set. The builder copies them regardless.
        private static void CheckEvent(EventDef e, List<DiagMessage> r)
        {
            if (e == null || e.Replies == null) return;
            for (int i = 0; i < e.Replies.Count; i++)
            {
                var rep = e.Replies[i];
                if (rep == null) continue;
                string at = $"Event/{e.EventId} reply {i + 1}";
                if (!rep.HasRoll)
                {
                    if (rep.RollDC != 0 || rep.RollCrit != -1 || rep.RollCritFail != -1 || rep.RollCard != Enums.CardType.None)
                        Warn(r, at, "has roll values (RollDC/RollCrit/RollCritFail/RollCard) but HasRoll is false, so none are used");
                }
                foreach (var kv in new[] { ("Ss", rep.Ss), ("Fl", rep.Fl), ("Ssc", rep.Ssc), ("Flc", rep.Flc) })
                {
                    var o = kv.Item2;
                    if (o == null) continue;
                    if (!o.CardDestruction && o.CardDestructionAmount != 0)
                        Warn(r, at, $"{kv.Item1}.CardDestructionAmount is set but CardDestruction is false");
                    if (!o.CraftUI && o.CraftUIMaxType != Enums.CardRarity.Common)
                        Warn(r, at, $"{kv.Item1}.CraftUIMaxType is set but CraftUI is false");
                    if (string.IsNullOrEmpty(o.CharacterReplacementId) && o.CharacterReplacementPosition != 0)
                        Warn(r, at, $"{kv.Item1}.CharacterReplacementPosition is set but there is no CharacterReplacementId");
                }
            }
        }

        private static void CheckChallengeTrait(ChallengeTraitDef t, List<DiagMessage> r)
        {
            if (t == null) return;
            string at = $"ChallengeTrait/{t.Id}";
            if (string.IsNullOrWhiteSpace(t.Name)) Warn(r, at, "Name is empty");
            if (string.IsNullOrWhiteSpace(t.Description)) Warn(r, at, "Description is empty; the weekly-challenge screen shows no text for it");
        }

        public static int LogReport(ModProject p)
        {
            var msgs = Validate(p);
            foreach (var m in msgs)
            {
                if (m.Severity == DiagSeverity.Error) Plugin.Log.LogError($"[ContentCheck:{p.ModId}] {m.Message}");
                else Plugin.Log.LogWarning($"[ContentCheck:{p.ModId}] {m.Message}");
            }
            if (msgs.Count == 0) Plugin.Log.LogInfo($"[ContentCheck:{p.ModId}] Content OK.");
            return msgs.Count;
        }
    }
}
