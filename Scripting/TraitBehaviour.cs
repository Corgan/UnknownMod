using Cards;

namespace UnknownMod.Scripting
{
    /// <summary>What a trait activation carries. <see cref="Card"/> is the card being cast for card events, otherwise null.</summary>
    public readonly struct TraitEvent
    {
        public readonly Enums.EventActivation Activation;
        public readonly Character Owner;
        public readonly Character Target;
        public readonly int Amount;
        public readonly string Text;
        public readonly CardRealtimeData Card;

        public TraitEvent(Enums.EventActivation activation, Character owner, Character target, int amount, string text, CardRealtimeData card)
        {
            Activation = activation;
            Owner = owner;
            Target = target;
            Amount = amount;
            Text = text ?? "";
            Card = card;
        }
    }

    /// <summary>The card of the trait activation in progress, for <see cref="ITraitBehaviour"/> handlers that need it.</summary>
    public static class TraitContext
    {
        public static CardRealtimeData Card { get; internal set; }
    }

    /// <summary>
    /// Convenient base for a trait script. Set the trait's Activations (and TimesPerTurn / TimesPerRound) in its json, then
    /// override <see cref="Run"/>. Event meanings follow the game: AuraCurseSet has Target = who received it, Text = aura id
    /// and Amount = charges; Heal has Target = who was healed; Hitted has Target = the attacker; CharacterKilled has Target = who died;
    /// CastCard has Text = card id and Card set. The trait never runs inside itself, so applying an aura from <see cref="Run"/>
    /// cannot loop.
    /// </summary>
    public abstract class TraitBehaviour : ITraitBehaviour
    {
        private bool _running;

        public abstract string TraitId { get; }

        protected abstract void Run(TraitEvent e);

        public bool OnActivate(Enums.EventActivation activation, Character owner, Character target, int auxInt, string auxString)
        {
            if (_running || owner == null || !owner.Alive || !GameApi.InCombat) return false;
            _running = true;
            try { Run(new TraitEvent(activation, owner, target, auxInt, auxString, TraitContext.Card)); }
            finally { _running = false; }
            return false;
        }

        /// <summary>
        /// Counts one use against the trait's TimesPerTurn or TimesPerRound, the same counters the game's own traits use
        /// (so the combat UI shows "used/limit"). Returns false once the limit is reached; always true for an unlimited trait.
        /// </summary>
        protected bool TryUse(Character owner)
        {
            var mm = MatchManager.Instance;
            if (mm == null) return false;
            var data = Globals.Instance.GetTraitData(TraitId, owner);
            if (data == null) return true;
            int limit = data.TimesPerTurn > 0 ? data.TimesPerTurn : data.TimesPerRound;
            if (limit <= 0) return true;
            var counts = data.TimesPerTurn > 0 ? mm.activatedTraits : mm.activatedTraitsRound;
            string key = Globals.Instance.GetCanonicalTraitId(TraitId, owner);
            counts.TryGetValue(key, out int used);
            if (used >= limit) return false;
            counts[key] = used + 1;
            mm.SetTraitInfoText();
            return true;
        }

        /// <summary>Floats the trait's name over a hero.</summary>
        protected void Announce(Character who)
        {
            var data = Globals.Instance.GetTraitData(TraitId, who);
            if (data != null && who != null && who.HeroItem != null)
                who.HeroItem.ScrollCombatText(data.TraitName, Enums.CombatScrollEffectType.Trait);
        }
    }
}
