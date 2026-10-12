using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Scripting;

namespace MyceliumAbyss
{
    internal static class Myc
    {
        public const string Rot = "myc_sporerot";
        public const string Symbiosis = "myc_symbiosis";

        public static int RotOn(Character c) => GameApi.AuraCharges(c, Rot);

        public static Character RandomOther(IEnumerable<Character> pool, Character not)
        {
            var list = pool.Where(c => c != not).ToList();
            return list.Count == 0 ? null : list[Random.Range(0, list.Count)];
        }
    }

    /// <summary>A dying enemy's Spore Rot jumps to a random survivor.</summary>
    public class SporebornTrait : TraitBehaviour
    {
        public override string TraitId => "myc_sporeborn";

        protected override void Run(TraitEvent e)
        {
            if (e.Activation != Enums.EventActivation.CharacterKilled) return;
            var dead = e.Target;
            if (dead == null || dead.IsHero) return;
            int rot = Myc.RotOn(dead);
            if (rot < 4) return;
            var next = Myc.RandomOther(GameApi.Enemies, dead);
            if (next == null) return;
            GameApi.AddAura(next, Myc.Rot, rot / 2, e.Owner, false);
            Announce(e.Owner);
        }
    }

    /// <summary>Healing an ally feeds them Symbiosis.</summary>
    public class FungalVigorTrait : TraitBehaviour
    {
        public override string TraitId => "myc_fungalvigor";

        protected override void Run(TraitEvent e)
        {
            if (e.Activation != Enums.EventActivation.Heal) return;
            var ally = e.Target;
            if (ally == null || !ally.IsHero || !ally.Alive || !TryUse(e.Owner)) return;
            GameApi.AddAura(ally, Myc.Symbiosis, 2, e.Owner, false);
            Announce(e.Owner);
        }
    }

    /// <summary>Piling Spore Rot onto an already rotting enemy leaves it Vulnerable.</summary>
    public class RotmasterTrait : TraitBehaviour
    {
        public override string TraitId => "myc_rotmaster";

        protected override void Run(TraitEvent e)
        {
            if (e.Activation != Enums.EventActivation.AuraCurseSet || e.Text != Myc.Rot) return;
            var enemy = e.Target;
            if (enemy == null || enemy.IsHero || !enemy.Alive || Myc.RotOn(enemy) < 6 || !TryUse(e.Owner)) return;
            GameApi.AddAura(enemy, "vulnerable", 2, e.Owner, false);
            Announce(e.Owner);
        }
    }

    /// <summary>Defense cards grow Thorns.</summary>
    public class RootedStanceTrait : TraitBehaviour
    {
        public override string TraitId => "myc_rootedstance";

        protected override void Run(TraitEvent e)
        {
            if (e.Activation != Enums.EventActivation.CastCard || e.Card == null) return;
            if (!e.Card.GetCardTypes().Contains(Enums.CardType.Defense) || !TryUse(e.Owner)) return;
            GameApi.AddAura(e.Owner, "thorns", 2, e.Owner, false);
            Announce(e.Owner);
        }
    }

    /// <summary>The first hit each turn on a rotting enemy deals Shadow damage equal to half its Spore Rot (the rot stays).</summary>
    public class CausticTouchTrait : TraitBehaviour
    {
        public override string TraitId => "myc_caustictouch";

        protected override void Run(TraitEvent e)
        {
            if (e.Activation != Enums.EventActivation.Hit) return;
            var enemy = e.Target;
            if (enemy == null || enemy.IsHero || !enemy.Alive) return;
            int burst = Myc.RotOn(enemy) / 2;
            if (burst < 1 || !TryUse(e.Owner)) return;
            GameApi.Damage(enemy, Enums.DamageType.Shadow, burst, e.Owner);
            Announce(e.Owner);
        }
    }

    /// <summary>Enemies that strike Morel are infected.</summary>
    public class ThickCapTrait : TraitBehaviour
    {
        public override string TraitId => "myc_thickcap";

        protected override void Run(TraitEvent e)
        {
            if (e.Activation != Enums.EventActivation.Hitted) return;
            var attacker = e.Target;
            if (attacker == null || attacker.IsHero || !attacker.Alive || !TryUse(e.Owner)) return;
            GameApi.AddAura(attacker, Myc.Rot, 3, e.Owner, false);
            Announce(e.Owner);
        }
    }

    /// <summary>A dying enemy bursts, hurting the rest by its Spore Rot.</summary>
    public class BlightedCoreTrait : TraitBehaviour
    {
        public override string TraitId => "myc_blightedcore";

        protected override void Run(TraitEvent e)
        {
            if (e.Activation != Enums.EventActivation.CharacterKilled) return;
            var dead = e.Target;
            if (dead == null || dead.IsHero) return;
            int burst = Mathf.Min(Myc.RotOn(dead), 15);
            if (burst < 1) return;
            var survivors = GameApi.Enemies.Where(c => c != dead).ToList();
            if (survivors.Count == 0) return;
            foreach (var c in survivors) GameApi.Damage(c, Enums.DamageType.Shadow, burst, e.Owner);
            Announce(e.Owner);
        }
    }

    /// <summary>
    /// Symbiosis ("Mycelial Network"): after healing at the start of the holder's turn, the stack jumps to a random
    /// living ally and loses charges on the way (half by default, 25% less with Apex Symbiont). With no living ally
    /// it stays on the holder but is still reduced.
    /// </summary>
    public class SymbiosisBehaviour : IAuraBehaviour
    {
        public string AuraId => Myc.Symbiosis;

        public void OnApplied(Character target, Character caster, int applied, int total) { }

        public void OnConsume(Character owner, string whenToConsume, int charges)
        {
            if (whenToConsume != "BeginTurn" || charges <= 0) return;
            // Hero perks and traits only improve the party's own Symbiosis, never an enemy's
            bool party = owner.IsHero;
            int keep = party && GameApi.Heroes.Any(h => h.HaveTrait("myc_apexsymbiont")) ? 75 : 50;
            int carried = charges * keep / 100;
            var allies = GameApi.AlliesOf(owner);

            if (allies.Count == 0)
            {
                GameApi.RemoveAura(owner, Myc.Symbiosis, Mathf.Max(0, GameApi.AuraCharges(owner, Myc.Symbiosis) - carried));
                return;
            }

            var next = party && MycPerks.TeamHas(MycPerks.SymSeek)
                ? allies.OrderBy(a => a.GetHpPercent()).First()
                : allies[UnityEngine.Random.Range(0, allies.Count)];

            // Bonuses are not applied: they would be added again on every jump and the stack would never fade
            GameApi.RemoveAura(owner, Myc.Symbiosis);
            GameApi.AddAura(next, Myc.Symbiosis, carried, owner, false);
            if (!party) return;
            if (MycPerks.TeamHas(MycPerks.SymShield)) GameApi.AddAura(next, "shield", 3, owner, false);
            if (MycPerks.TeamHas(MycPerks.SymMutualism)) GameApi.AddAura(next, "thorns", 2, owner, false);
        }
    }

    /// <summary>
    /// Spore Rot: at the end of the holder's turn it ticks, then 75% of the stack it had hops to a random other enemy
    /// (in place of the usual 1-charge decay). With no other enemy it shrinks by the same amount. The Contagion perk
    /// keeps what the hop would lose on the holder (enemy holders only). The Virulent Spores perk also seeds 1 Spore Rot elsewhere
    /// whenever a perk holder applies it.
    /// </summary>
    public class SporeRotBehaviour : IAuraBehaviour
    {
        private static bool _spreading;

        public string AuraId => Myc.Rot;

        public void OnApplied(Character target, Character caster, int applied, int total)
        {
            if (_spreading || applied <= 0 || target == null || target.IsHero) return;
            if (caster == null || !caster.IsHero || !caster.HavePerk(MycPerks.RotSpread)) return;
            var other = Myc.RandomOther(GameApi.Enemies, target);
            if (other == null) return;

            // The spread is itself an application of Spore Rot and would spread again
            _spreading = true;
            try { GameApi.AddAura(other, Myc.Rot, 1, caster, false); }
            finally { _spreading = false; }
        }

        public void OnConsume(Character owner, string whenToConsume, int charges)
        {
            if (whenToConsume != "EndTurn" || owner == null) return;
            int left = GameApi.AuraCharges(owner, Myc.Rot);
            if (left <= 0) return;

            // The hop replaces the usual 1-charge decay rather than stacking on it: 75% of the pre-tick stack is kept
            int carried = Mathf.Min(left, charges * 75 / 100);
            bool contagion = !owner.IsHero && MycPerks.TeamHas(MycPerks.RotContagion);
            var others = GameApi.AlliesOf(owner);

            // Alone, the stack shrinks exactly as much as when it hops, so a lone target never decays faster or slower
            if (others.Count == 0)
            {
                if (!contagion) GameApi.RemoveAura(owner, Myc.Rot, left - carried);
                return;
            }
            if (carried < 1) return;

            int leave = contagion ? left - carried : 0;
            GameApi.RemoveAura(owner, Myc.Rot, left - leave);
            GameApi.AddAura(others[UnityEngine.Random.Range(0, others.Count)], Myc.Rot, carried, owner, false);
        }
    }

    /// <summary>Each round, heavily rotted enemies seed Spore Rot into every other enemy.</summary>
    public class SporelordTrait : TraitBehaviour
    {
        public override string TraitId => "myc_sporelord";

        protected override void Run(TraitEvent e)
        {
            if (e.Activation != Enums.EventActivation.BeginRound) return;
            var enemies = GameApi.Enemies.ToList();
            var carriers = enemies.Where(c => Myc.RotOn(c) >= 5).ToList();
            if (carriers.Count == 0 || enemies.Count < 2 || !TryUse(e.Owner)) return;
            foreach (var carrier in carriers)
                foreach (var other in enemies)
                    if (other != carrier) GameApi.AddAura(other, Myc.Rot, 3, e.Owner, false);
            Announce(e.Owner);
        }
    }
}
