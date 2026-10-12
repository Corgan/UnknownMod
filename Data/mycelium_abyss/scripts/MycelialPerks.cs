using System.Linq;
using UnknownMod.Scripting;

namespace MyceliumAbyss
{
    /// <summary>Ids of the special-effect perks (lowercase, as the game stores them in a hero's perk list).</summary>
    internal static class MycPerks
    {
        public const string RotSpread = "mainperkmycrot2b";
        public const string RotDecompose = "mainperkmycrot2c";
        public const string RotContagion = "mainperkmycrot2d";
        public const string SymShield = "mainperkmycsym2b";
        public const string SymSeek = "mainperkmycsym2c";
        public const string SymMutualism = "mainperkmycsym2d";

        public static bool TeamHas(string perkId) => GameApi.Heroes.Any(h => h.HavePerk(perkId));
    }

    /// <summary>Spore Rot perks that react to combat events: Decomposition.</summary>
    public class MycelialPerks : ModScript
    {
        public override void OnCombatEvent(Enums.EventActivation activation, Character owner, Character target, int auxInt, string auxString, Character caster)
        {
            if (owner == null || !owner.IsHero || !owner.Alive) return;

            if (activation != Enums.EventActivation.CharacterKilled) return;
            if (target == null || target.IsHero || !owner.HavePerk(MycPerks.RotDecompose)) return;
            if (Myc.RotOn(target) >= 4) GameApi.Heal(owner, 4, owner);
        }
    }
}
