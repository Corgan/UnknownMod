using System;
using System.Collections.Generic;
using System.Linq;
using BattleMatch;

namespace UnknownMod.Scripting
{
    /// <summary>
    /// Small, safe helpers over the game's fight state, so a script does not have to know where the teams live.
    /// Everything is null-safe and returns empty results outside a fight.
    /// </summary>
    public static class GameApi
    {
        /// <summary>True while a fight scene is running.</summary>
        public static bool InCombat
        {
            get { try { return MatchManager.Instance != null; } catch { return false; } }
        }

        /// <summary>The current round of the fight, or 0 outside one.</summary>
        public static int Round
        {
            get { try { return InCombat ? MatchManager.Instance.GetCurrentRound() : 0; } catch { return 0; } }
        }

        /// <summary>Living heroes in the fight.</summary>
        public static IReadOnlyList<Character> Heroes => Alive(() => MatchManager.Instance.GetTeamHero());

        /// <summary>Living enemies in the fight.</summary>
        public static IReadOnlyList<Character> Enemies => Alive(() => MatchManager.Instance.GetTeamNPC());

        private static IReadOnlyList<Character> Alive(Func<Team> team)
        {
            try
            {
                if (!InCombat) return Array.Empty<Character>();
                var t = team();
                return t == null ? Array.Empty<Character>() : t.Where(c => c != null && c.Alive).ToList();
            }
            catch { return Array.Empty<Character>(); }
        }

        /// <summary>Living characters on the same side as <paramref name="c"/>, not including it.</summary>
        public static IReadOnlyList<Character> AlliesOf(Character c)
        {
            if (c == null) return Array.Empty<Character>();
            return (c.IsHero ? Heroes : Enemies).Where(x => x != c).ToList();
        }

        /// <summary>The living character with this id (hero or NPC), or null.</summary>
        public static Character Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Heroes.Concat(Enemies).FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsHero(Character c) => c != null && c.IsHero;

        /// <summary>Hit points left, or 0 for null.</summary>
        public static int Hp(Character c) => c == null ? 0 : c.GetHp();

        /// <summary>Charges of an aura or curse on a character (0 when it has none).</summary>
        public static int AuraCharges(Character c, string auraId) => c == null || string.IsNullOrEmpty(auraId) ? 0 : c.GetAuraCharges(auraId);

        /// <summary>Deals damage that goes through the game's damage handling (shields, resistances, death) without a card.</summary>
        public static void Damage(Character target, Enums.DamageType type, int amount, Character source = null)
        {
            if (target == null || !target.Alive || amount <= 0) return;
            target.IndirectDamage(type, amount, source ?? target);
        }

        /// <summary>Restores hit points directly.</summary>
        public static void Heal(Character target, int amount, Character source = null)
        {
            if (target == null || !target.Alive || amount <= 0) return;
            target.ModifyHp(amount, true, true, source);
        }

        /// <summary>
        /// Applies charges of an aura or curse, the way a trait does. By default the source's own bonuses for that aura
        /// (traits, items, perks) are added on top, as for a card; pass <paramref name="applyCasterBonuses"/> = false
        /// to apply exactly <paramref name="charges"/>, which is what you want for a fixed amount a script chose itself.
        /// </summary>
        public static void AddAura(Character target, string auraId, int charges, Character source = null, bool applyCasterBonuses = true)
        {
            if (target == null || !target.Alive || charges <= 0 || string.IsNullOrEmpty(auraId)) return;
            if (applyCasterBonuses)
            {
                target.SetAuraTrait(source ?? target, auraId, charges);
                return;
            }
            var data = Globals.Instance?.GetAuraCurseData(auraId);
            if (data == null) return;
            target.SetAuraCurse(source ?? target, data, charges, fromTrait: true, useCharacterMods: false);
        }

        /// <summary>Removes charges of an aura or curse; with no amount (or at least the current stack) it removes the whole stack.</summary>
        public static void RemoveAura(Character target, string auraId, int charges = int.MaxValue)
        {
            if (target == null || charges <= 0 || string.IsNullOrEmpty(auraId)) return;
            int current = target.GetAuraCharges(auraId);
            if (current <= 0) return;
            var data = Globals.Instance?.GetAuraCurseData(auraId);
            if (data == null) return;
            if (charges >= current) target.HealAuraCurse(data);
            else target.ReduceAuraCurseCharges(data, charges);
        }
    }
}
