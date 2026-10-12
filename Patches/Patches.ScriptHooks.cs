using HarmonyLib;
using Cards;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>
    /// Feeds game moments to <see cref="UnknownMod.Scripting.ModScript"/> hooks. Own class so a game update that
    /// changes one of these methods cannot disable the main Patches class. Every hook returns immediately when no
    /// script listens to it, so a game without scripts pays nothing.
    /// </summary>
    [HarmonyPatch]
    public static class ScriptHookPatches
    {
        [HarmonyPatch(typeof(AtOManager), nameof(AtOManager.LaunchCombat))]
        [HarmonyPrefix]
        public static void LaunchCombat_Pre(CombatData _combatData)
        {
            if (ScriptHost.Wants(ScriptHook.CombatLaunch))
                ScriptHost.Dispatch(ScriptHook.CombatLaunch, s => s.OnCombatLaunch(_combatData));
        }

        [HarmonyPatch(typeof(MatchManager), nameof(MatchManager.SetEventCo))]
        [HarmonyPrefix]
        public static void SetEventCo_Pre(Character theChar, Enums.EventActivation theEvent, Character target, int auxInt, string auxString, Character caster)
        {
            RuleEngine.OnEvent(theChar, theEvent, auxString);
            if (ScriptHost.Wants(ScriptHook.CombatEvent))
                ScriptHost.Dispatch(ScriptHook.CombatEvent, s => s.OnCombatEvent(theEvent, theChar, target, auxInt, auxString, caster));
        }

        [HarmonyPatch(typeof(MatchManager), "CastCardEnd")]
        [HarmonyPrefix]
        public static void CastCardEnd_Pre(CardRealtimeData _cardActive, Hero _hero, NPC _npc) => CardCast(_cardActive, _hero, _npc);

        [HarmonyPatch(typeof(MatchManager), "CastCardEndAutomatic")]
        [HarmonyPrefix]
        public static void CastCardEndAutomatic_Pre(CardRealtimeData _cardActive, Hero _hero, NPC _npc) => CardCast(_cardActive, _hero, _npc);

        private static void CardCast(CardRealtimeData card, Hero hero, NPC npc)
        {
            if (!ScriptHost.Wants(ScriptHook.CardCast) || card == null) return;
            Character caster = hero != null ? hero : npc;
            ScriptHost.Dispatch(ScriptHook.CardCast, s => s.OnCardCast(card, caster));
        }

        [HarmonyPatch(typeof(MatchManager), nameof(MatchManager.KillHero))]
        [HarmonyPostfix]
        public static void KillHero_Post(Hero _hero)
        {
            RuleEngine.OnKilled(_hero);
            if (_hero != null && ScriptHost.Wants(ScriptHook.Killed))
                ScriptHost.Dispatch(ScriptHook.Killed, s => s.OnCharacterKilled(_hero));
        }

        [HarmonyPatch(typeof(MatchManager), nameof(MatchManager.KillNPC))]
        [HarmonyPostfix]
        public static void KillNPC_Post(NPC _npc)
        {
            RuleEngine.OnKilled(_npc);
            if (_npc != null && ScriptHost.Wants(ScriptHook.Killed))
                ScriptHost.Dispatch(ScriptHook.Killed, s => s.OnCharacterKilled(_npc));
        }

        [HarmonyPatch(typeof(NewTurn), nameof(NewTurn.FinishCombat))]
        [HarmonyPrefix]
        public static void FinishCombat_Pre(bool won)
        {
            if (ScriptHost.Wants(ScriptHook.CombatEnd))
                ScriptHost.Dispatch(ScriptHook.CombatEnd, s => s.OnCombatEnd(won));
        }

        [HarmonyPatch(typeof(EventManager), nameof(EventManager.SetEvent))]
        [HarmonyPostfix]
        public static void SetEvent_Post(EventData _eventData)
        {
            if (ScriptHost.Wants(ScriptHook.EventShown))
                ScriptHost.Dispatch(ScriptHook.EventShown, s => s.OnEventShown(_eventData));
        }

        [HarmonyPatch(typeof(EventManager), nameof(EventManager.SelectOption))]
        [HarmonyPrefix]
        public static void SelectOption_Pre(int _index, int ___optionSelected, EventData ___currentEvent)
        {
            // The game ignores a second click while an answer is being processed; so do we
            if (___optionSelected > -1 || !ScriptHost.Wants(ScriptHook.EventOption)) return;
            ScriptHost.Dispatch(ScriptHook.EventOption, s => s.OnEventOption(___currentEvent, _index));
        }
    }
}
