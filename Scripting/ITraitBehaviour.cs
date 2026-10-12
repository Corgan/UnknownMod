namespace UnknownMod.Scripting
{
    /// <summary>
    /// Custom behaviour for a trait. Implementations live in a mod's scripts.dll and are discovered by
    /// <see cref="UnknownMod.Core.ScriptLoader"/>. Called from Trait.DoTrait for every activation of the
    /// trait the game has already decided should fire (the TraitData's ActivationEvent matched).
    /// </summary>
    public interface ITraitBehaviour
    {
        /// <summary>Trait id this handles (case-insensitive). May be a vanilla id to extend or replace it.</summary>
        string TraitId { get; }

        /// <summary>
        /// Run the trait. Return true to skip the game's own handling (use for a vanilla id you replace),
        /// false to let it continue (the normal case for a new mod trait, which the game ignores anyway).
        /// </summary>
        bool OnActivate(Enums.EventActivation activation, Character owner, Character target, int auxInt, string auxString);
    }
}
