using Cards;
using UnknownMod.Core;

namespace UnknownMod.Scripting
{
    /// <summary>
    /// Base class for a mod script. Derive from it in scripts.dll and override only the hooks you need; every hook
    /// is optional and a hook you do not override costs nothing. Instances are created once when the mod's scripts load.
    /// Hooks run synchronously on the main thread, so keep them short. An exception is caught and logged, and a script
    /// that keeps throwing is switched off (it can be switched back on in the Scripts tab).
    /// </summary>
    public abstract class ModScript
    {
        /// <summary>Mod id, folder, logging and settings for this script. Set before <see cref="OnLoad"/>.</summary>
        public ScriptContext Context { get; internal set; }

        /// <summary>After the script was created. Set things up here rather than in a constructor.</summary>
        public virtual void OnLoad() { }

        /// <summary>Before the script is replaced by a reload or the mod is unloaded. Undo whatever <see cref="OnLoad"/> did.</summary>
        public virtual void OnUnload() { }

        /// <summary>Every frame. Only called if you override it, but then keep it tiny.</summary>
        public virtual void OnUpdate() { }

        /// <summary>A scene finished loading and became the active one (MainMenu, Map, Town, Combat, ...).</summary>
        public virtual void OnSceneChanged(string sceneName) { }

        /// <summary>A fight is about to start (AtOManager.LaunchCombat). Characters do not exist yet; <paramref name="combat"/> may be null.</summary>
        public virtual void OnCombatLaunch(CombatData combat) { }

        /// <summary>
        /// The game raised a combat event for a character (MatchManager.SetEventCo): BeginCombat, BeginRound, BeginTurn, EndTurn,
        /// CastCard, Damaged, Healed, Killed and the rest of <see cref="Enums.EventActivation"/>. This is the one to use for
        /// "at the start of combat", "at the start of each round" and similar. It fires once per character and event.
        /// </summary>
        public virtual void OnCombatEvent(Enums.EventActivation activation, Character owner, Character target, int auxInt, string auxString, Character caster) { }

        /// <summary>A card finished resolving. <paramref name="caster"/> is the hero or NPC that played it.</summary>
        public virtual void OnCardCast(CardRealtimeData card, Character caster) { }

        /// <summary>A hero or NPC was just killed.</summary>
        public virtual void OnCharacterKilled(Character character) { }

        /// <summary>The fight is over. <paramref name="won"/> is true when at least one hero survived.</summary>
        public virtual void OnCombatEnd(bool won) { }

        /// <summary>A map event was opened.</summary>
        public virtual void OnEventShown(EventData eventData) { }

        /// <summary>The player picked an answer in a map event. <paramref name="optionIndex"/> is the zero-based position of the reply.</summary>
        public virtual void OnEventOption(EventData eventData, int optionIndex) { }
    }
}
