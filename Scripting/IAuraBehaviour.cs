namespace UnknownMod.Scripting
{
    /// <summary>
    /// Custom behaviour for an aura/curse. Implementations live in a mod's scripts.dll and are
    /// discovered by <see cref="UnknownMod.Core.ScriptLoader"/>. The game ties aura logic to hardcoded ids,
    /// so a mod aura only gets its data-driven fields; this is how it gets code too.
    /// Calls are synchronous and run on the main thread; do not block.
    /// </summary>
    public interface IAuraBehaviour
    {
        /// <summary>Aura/curse id this handles (case-insensitive). May be a vanilla id to extend it.</summary>
        string AuraId { get; }

        /// <summary>
        /// After the aura was added to or increased on <paramref name="target"/> (Character.SetAuraCurse).
        /// <paramref name="applied"/> is the charges added by this call, <paramref name="total"/> the new total.
        /// </summary>
        void OnApplied(Character target, Character caster, int applied, int total);

        /// <summary>
        /// After the game finished processing this character's auras for <paramref name="whenToConsume"/>
        /// (MatchManager.ConsumeAuraCurse, e.g. "BeginTurn", "EndTurn"): damage, healing and decay are already done.
        /// <paramref name="charges"/> is the stack the aura had before that processing. Only called for an aura the
        /// character had at that moment; not called if the character died or the processing was cancelled.
        /// </summary>
        void OnConsume(Character owner, string whenToConsume, int charges);
    }
}
