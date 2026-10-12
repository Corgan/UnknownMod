using System;
using System.Collections.Generic;
using System.Linq;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    internal enum PresetParam { None, Aura, Curse, DamageType, Card }

    /// <summary>A ready-made effect for an item or enchantment. Strength runs 1 (small) to 5 (large); Param is the aura, curse, damage type or card the effect refers to.</summary>
    internal sealed class ItemPreset
    {
        public string Name;
        public string Tip;
        public PresetParam Param;
        public string DefaultParam = "";
        public Action<ItemFields, int, string, List<string>> Apply;
    }

    internal static class ItemPresets
    {
        public static readonly List<ItemPreset> All = Build();

        public static ItemPreset Find(string name) => All.FirstOrDefault(p => p.Name == name);

        /// <summary>A fresh ItemFields that keeps the flags, enchantment settings and FX of <paramref name="src"/> but none of its effects.</summary>
        public static ItemFields ClearEffects(ItemFields src)
        {
            return new ItemFields
            {
                IsEnchantment = src.IsEnchantment,
                UseTheNextInsteadWhenYouPlay = src.UseTheNextInsteadWhenYouPlay,
                DestroyAfterUses = src.DestroyAfterUses,
                DestroyStartOfTurn = src.DestroyStartOfTurn,
                DestroyEndOfTurn = src.DestroyEndOfTurn,
                CastEnchantmentOnFinishSelfCast = src.CastEnchantmentOnFinishSelfCast,
                CursedItem = src.CursedItem,
                DropOnly = src.DropOnly,
                QuestItem = src.QuestItem,
                DestroyAfterUse = src.DestroyAfterUse,
                Vanish = src.Vanish,
                Permanent = src.Permanent,
                DuplicateActive = src.DuplicateActive,
                PassSingleAndCharacterRolls = src.PassSingleAndCharacterRolls,
                OnlyAddItemToNPCs = src.OnlyAddItemToNPCs,
                AddVanishToDeck = src.AddVanishToDeck,
                SpriteBossDrop = src.SpriteBossDrop,
                PetActivation = src.PetActivation,
                EffectItemOwner = src.EffectItemOwner,
                EffectCaster = src.EffectCaster,
                EffectCasterDelay = src.EffectCasterDelay,
                EffectTarget = src.EffectTarget,
                EffectTargetDelay = src.EffectTargetDelay,
            };
        }

        // ── slot helpers: put a value in the first free slot, or stack onto a matching one ──

        private static bool Slot<TT, TV>(ref TT t1, ref TV v1, ref TT t2, ref TV v2, ref TT t3, ref TV v3,
            TT none, TT type, TV val, Func<TV, TV, TV> add)
        {
            var eq = EqualityComparer<TT>.Default;
            if (eq.Equals(t1, type)) { v1 = add(v1, val); return true; }
            if (eq.Equals(t2, type)) { v2 = add(v2, val); return true; }
            if (eq.Equals(t3, type)) { v3 = add(v3, val); return true; }
            if (eq.Equals(t1, none)) { t1 = type; v1 = val; return true; }
            if (eq.Equals(t2, none)) { t2 = type; v2 = val; return true; }
            if (eq.Equals(t3, none)) { t3 = type; v3 = val; return true; }
            return false;
        }

        private static int AddI(int a, int b) => a + b;
        private static float AddF(float a, float b) => a + b;

        private static bool SlotAc(ref string a1, ref int v1, ref string a2, ref int v2, ref string a3, ref int v3, string id, int val)
            => Slot(ref a1, ref v1, ref a2, ref v2, ref a3, ref v3, "", id, val, AddI);

        private static void Full(List<string> notes, string what) => notes.Add($"No free slot for {what}; clear existing effects or remove one first.");

        private static void FlatDamage(ItemFields f, Enums.DamageType t, int v, List<string> n)
        {
            if (!Slot(ref f.DamageFlatBonus, ref f.DamageFlatBonusValue, ref f.DamageFlatBonus2, ref f.DamageFlatBonusValue2,
                ref f.DamageFlatBonus3, ref f.DamageFlatBonusValue3, Enums.DamageType.None, t, v, AddI)) Full(n, "flat damage");
        }

        private static void PercentDamage(ItemFields f, Enums.DamageType t, float v, List<string> n)
        {
            if (!Slot(ref f.DamagePercentBonus, ref f.DamagePercentBonusValue, ref f.DamagePercentBonus2, ref f.DamagePercentBonusValue2,
                ref f.DamagePercentBonus3, ref f.DamagePercentBonusValue3, Enums.DamageType.None, t, v, AddF)) Full(n, "percent damage");
        }

        private static void Resist(ItemFields f, Enums.DamageType t, int v, List<string> n)
        {
            if (!Slot(ref f.ResistModified1, ref f.ResistModifiedValue1, ref f.ResistModified2, ref f.ResistModifiedValue2,
                ref f.ResistModified3, ref f.ResistModifiedValue3, Enums.DamageType.None, t, v, AddI)) Full(n, "resistance");
        }

        private static void Stat(ItemFields f, Enums.CharacterStat s, int v, List<string> n)
        {
            if (!Slot(ref f.CharacterStatModified, ref f.CharacterStatModifiedValue, ref f.CharacterStatModified2, ref f.CharacterStatModifiedValue2,
                ref f.CharacterStatModified3, ref f.CharacterStatModifiedValue3, Enums.CharacterStat.None, s, v, AddI)) Full(n, "character stat");
        }

        private static void GainTarget(ItemFields f, string id, int v, List<string> n)
        {
            if (!SlotAc(ref f.AuracurseGain1, ref f.AuracurseGainValue1, ref f.AuracurseGain2, ref f.AuracurseGainValue2,
                ref f.AuracurseGain3, ref f.AuracurseGainValue3, id, v)) Full(n, "aura/curse gain");
        }

        private static void GainSelf(ItemFields f, string id, int v, List<string> n)
        {
            if (!SlotAc(ref f.AuracurseGainSelf1, ref f.AuracurseGainSelfValue1, ref f.AuracurseGainSelf2, ref f.AuracurseGainSelfValue2,
                ref f.AuracurseGainSelf3, ref f.AuracurseGainSelfValue3, id, v)) Full(n, "self aura/curse gain");
        }

        private static void Trigger(ItemFields f, Enums.EventActivation act, Enums.ItemTarget target, int timesPerTurn = 0)
        {
            f.Activation = act;
            f.ItemTarget = target;
            f.TimesPerTurn = timesPerTurn;
        }

        private static ItemPreset P(string name, string tip, PresetParam param, string def, Action<ItemFields, int, string, List<string>> apply)
            => new ItemPreset { Name = name, Tip = tip, Param = param, DefaultParam = def, Apply = apply };

        private static List<ItemPreset> Build()
        {
            return new List<ItemPreset>
            {
                // ── Passive stats ──
                P("Passive: Flat damage", "Adds +Strength flat damage of the chosen type to every hit.", PresetParam.DamageType, "All",
                    (f, s, p, n) => FlatDamage(f, Enum.TryParse<Enums.DamageType>(p, out var t) ? t : Enums.DamageType.All, s, n)),
                P("Passive: Percent damage", "Adds +3% x Strength damage of the chosen type.", PresetParam.DamageType, "All",
                    (f, s, p, n) => PercentDamage(f, Enum.TryParse<Enums.DamageType>(p, out var t) ? t : Enums.DamageType.All, 3f * s, n)),
                P("Passive: Resistance", "Adds resistance of the chosen type (2% x Strength for All, 5% x Strength for one type).", PresetParam.DamageType, "All",
                    (f, s, p, n) =>
                    {
                        var t = Enum.TryParse<Enums.DamageType>(p, out var x) ? x : Enums.DamageType.All;
                        Resist(f, t, (t == Enums.DamageType.All ? 2 : 5) * s, n);
                    }),
                P("Passive: Max HP", "Adds +10 x Strength max HP.", PresetParam.None, "",
                    (f, s, p, n) => f.MaxHealth += 10 * s),
                P("Passive: Speed", "Adds +Strength speed.", PresetParam.None, "",
                    (f, s, p, n) => Stat(f, Enums.CharacterStat.Speed, s, n)),
                P("Passive: Energy per turn", "Adds +1 energy every turn (+2 at Strength 4 and above). Very strong, keep Strength low.", PresetParam.None, "",
                    (f, s, p, n) => Stat(f, Enums.CharacterStat.EnergyTurn, s >= 4 ? 2 : 1, n)),
                P("Passive: Healing power", "Adds +Strength flat healing done and +5% x Strength healing done.", PresetParam.None, "",
                    (f, s, p, n) => { f.HealFlatBonus += s; f.HealPercentBonus += 5f * s; }),
                P("Passive: Healing received", "Adds +Strength flat and +5% x Strength healing received.", PresetParam.None, "",
                    (f, s, p, n) => { f.HealReceivedFlatBonus += s; f.HealReceivedPercentBonus += 5f * s; }),
                P("Passive: Aura/curse immunity", "Makes the bearer immune to the chosen aura or curse.", PresetParam.Curse, "poison",
                    (f, s, p, n) =>
                    {
                        if (string.IsNullOrEmpty(f.AuracurseImmune1)) f.AuracurseImmune1 = p;
                        else if (string.IsNullOrEmpty(f.AuracurseImmune2)) f.AuracurseImmune2 = p;
                        else Full(n, "immunity");
                    }),
                P("Passive: Shop discount", "Shop prices -5% x Strength.", PresetParam.None, "",
                    (f, s, p, n) => f.PercentDiscountShop += 5 * s),

                // ── Combat start ──
                P("Combat start: Gain aura", "At the start of combat the bearer gains Strength charges of the chosen aura.", PresetParam.Aura, "powerful",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.BeginCombat, Enums.ItemTarget.Self); GainTarget(f, p, s, n); }),
                P("Combat start: Curse all enemies", "At the start of combat every enemy gets Strength charges of the chosen curse.", PresetParam.Curse, "weak",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.BeginCombat, Enums.ItemTarget.AllEnemy); GainTarget(f, p, s, n); }),
                P("Combat start: Extra energy", "Bearer starts combat with +Strength energy.", PresetParam.None, "",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.BeginCombat, Enums.ItemTarget.Self); f.EnergyQuantity = s; }),
                P("Combat start: Add card to hand", "Adds the chosen card to the bearer's hand at the start of combat.", PresetParam.Card, "",
                    (f, s, p, n) =>
                    {
                        Trigger(f, Enums.EventActivation.BeginCombat, Enums.ItemTarget.Self);
                        f.CardToGain = p;
                        f.CardNum = Math.Max(1, (s + 1) / 2);
                        f.CardPlace = Enums.CardPlace.Hand;
                    }),

                // ── Turn / round ──
                P("Each turn: Gain aura", "At the start of each turn the bearer gains Strength charges of the chosen aura.", PresetParam.Aura, "buffer",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.BeginTurn, Enums.ItemTarget.Self); GainSelf(f, p, s, n); }),
                P("Each turn: Draw cards", "Draw Strength cards at the start of each turn (capped at 2).", PresetParam.None, "",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.BeginTurn, Enums.ItemTarget.Self); f.DrawCards = Math.Min(2, s); }),
                P("End of turn: Heal self", "Heals the bearer 2 x Strength HP at the end of each turn.", PresetParam.None, "",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.EndTurn, Enums.ItemTarget.Self); f.HealQuantity = 2 * s; }),
                P("Round start: Damage random enemy", "At the start of each round a random enemy takes 3 x Strength damage of the chosen type.", PresetParam.DamageType, "Fire",
                    (f, s, p, n) =>
                    {
                        Trigger(f, Enums.EventActivation.BeginRound, Enums.ItemTarget.RandomEnemy);
                        f.DamageToTargetType = Enum.TryParse<Enums.DamageType>(p, out var t) ? t : Enums.DamageType.Fire;
                        f.DamageToTarget = 3 * s;
                    }),

                // ── Reactions ──
                P("On hit: Apply curse", "When the bearer hits, the target gets Strength charges of the chosen curse (once per turn).", PresetParam.Curse, "bleed",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.Hit, Enums.ItemTarget.CurrentTarget, 1); GainTarget(f, p, s, n); }),
                P("On hit: Extra damage", "When the bearer hits, the target takes 3 x Strength extra damage of the chosen type (once per turn).", PresetParam.DamageType, "Lightning",
                    (f, s, p, n) =>
                    {
                        Trigger(f, Enums.EventActivation.Hit, Enums.ItemTarget.CurrentTarget, 1);
                        f.DamageToTargetType = Enum.TryParse<Enums.DamageType>(p, out var t) ? t : Enums.DamageType.Lightning;
                        f.DamageToTarget = 3 * s;
                    }),
                P("When damaged: Gain block", "When the bearer takes damage they gain 3 x Strength block (once per turn).", PresetParam.None, "",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.Damaged, Enums.ItemTarget.Self, 1); GainTarget(f, "block", 3 * s, n); }),
                P("When hit: Gain aura", "When the bearer is hit they gain Strength charges of the chosen aura (once per turn).", PresetParam.Aura, "thorns",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.Hitted, Enums.ItemTarget.Self, 1); GainTarget(f, p, s, n); }),
                P("On kill: Heal self", "When the bearer kills an enemy they heal 3 x Strength HP.", PresetParam.None, "",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.Killed, Enums.ItemTarget.Self); f.HealQuantity = 3 * s; }),
                P("On kill: Gain aura", "When the bearer kills an enemy they gain Strength charges of the chosen aura.", PresetParam.Aura, "powerful",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.Killed, Enums.ItemTarget.Self); GainTarget(f, p, s, n); }),
                P("On card cast: Gain aura", "Each time the bearer plays a card they gain Strength charges of the chosen aura (once per turn).", PresetParam.Aura, "sharp",
                    (f, s, p, n) => { Trigger(f, Enums.EventActivation.CastCard, Enums.ItemTarget.Self, 1); GainTarget(f, p, s, n); }),

                // ── Cursed ──
                P("Cursed: Power for a price", "Marks the item cursed, adds +10% x Strength damage and gives the bearer the chosen curse at the start of combat.", PresetParam.Curse, "fatigue",
                    (f, s, p, n) =>
                    {
                        f.CursedItem = true;
                        PercentDamage(f, Enums.DamageType.All, 10f * s, n);
                        Trigger(f, Enums.EventActivation.BeginCombat, Enums.ItemTarget.Self);
                        GainTarget(f, p, Math.Max(1, s - 1), n);
                    }),
            };
        }
    }
}
