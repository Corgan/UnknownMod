using System.Collections.Generic;
using System.Linq;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Structural checks on event definitions: combinations the game's event UI cannot handle (it throws while
    /// building or resolving the reply) rather than ids that point at nothing, which ReferenceValidator covers.
    /// </summary>
    public static class EventValidator
    {
        public static List<DiagMessage> Validate(ModProject p)
        {
            var result = new List<DiagMessage>();
            if (p == null) return result;
            foreach (var e in p.Events.Values.Concat(p.EventPatches.Values))
            {
                if (e == null) continue;
                string ev = $"Event/{e.EventId}";
                if (e.Replies == null || e.Replies.Count == 0)
                {
                    result.Add(new DiagMessage(DiagSeverity.Error, $"{ev}: has no replies, the player could never leave it"));
                    continue;
                }
                for (int i = 0; i < e.Replies.Count; i++)
                    CheckReply($"{ev}.Replies[{i}]", e.Replies[i], result);
            }
            return result;
        }

        private static bool HasClass(ReplyDef r) =>
            !string.IsNullOrEmpty(r.RequiredClassId) || r.RepeatForAllCharacters || r.RepeatForAllWarriors
            || r.RepeatForAllScouts || r.RepeatForAllMages || r.RepeatForAllHealers;

        private static void CheckReply(string at, ReplyDef r, List<DiagMessage> result)
        {
            if (r == null)
            {
                result.Add(new DiagMessage(DiagSeverity.Error, $"{at}: reply is null"));
                return;
            }
            bool hasClass = HasClass(r);
            const string fix = "set RequiredClassId or one of the RepeatForAll* flags";

            if (r.HasRoll && r.RollCard != Enums.CardType.None && !hasClass)
                result.Add(new DiagMessage(DiagSeverity.Error,
                    $"{at}: card-type roll ({r.RollCard}) has no hero. The game reads the reply's RequiredClass to show the odds and throws; {fix}"));
            if (r.HasRoll && r.RollTarget == Enums.RollTarget.Character && !hasClass)
                result.Add(new DiagMessage(DiagSeverity.Error,
                    $"{at}: RollTarget Character rolls for one hero but none is set; {fix}"));
            if (r.Ss == null)
                result.Add(new DiagMessage(DiagSeverity.Error, $"{at}: Ss (success result) is missing"));
            else if ((r.Ss.RemoveItemSlot != Enums.ItemSlot.None || r.Ss.CorruptItemSlot != Enums.ItemSlot.None) && !hasClass)
                result.Add(new DiagMessage(DiagSeverity.Error,
                    $"{at}: removing or corrupting an item slot needs a hero to take it from; {fix}"));

            if (!r.HasRoll && r.RollCard != Enums.CardType.None)
                result.Add(new DiagMessage(DiagSeverity.Warning, $"{at}: RollCard is set but HasRoll is false, so it is ignored"));
            if (r.HasRoll && r.RollCard == Enums.CardType.None && r.RollDC <= 0
                && (r.RollMode == Enums.RollMode.HigherOrEqual || r.RollMode == Enums.RollMode.LowerOrEqual))
                result.Add(new DiagMessage(DiagSeverity.Warning, $"{at}: roll with RollDC {r.RollDC} will always or never succeed"));
            if (r.Action == Enums.EventAction.CharacterName && string.IsNullOrEmpty(r.RequiredClassId))
                result.Add(new DiagMessage(DiagSeverity.Warning, $"{at}: Action CharacterName has no RequiredClassId, so no hero name is shown"));
            if (string.IsNullOrWhiteSpace(r.ReplyText))
                result.Add(new DiagMessage(DiagSeverity.Warning, $"{at}: ReplyText is empty"));
            if (string.IsNullOrWhiteSpace(r.Ss?.Text))
                result.Add(new DiagMessage(DiagSeverity.Warning, $"{at}: SsText (success result text) is empty"));
            if (r.HasRoll && string.IsNullOrWhiteSpace(r.Fl?.Text))
                result.Add(new DiagMessage(DiagSeverity.Warning, $"{at}: FlText (failure result text) is empty but the reply has a roll"));
        }

        public static int LogReport(ModProject p)
        {
            var msgs = Validate(p);
            foreach (var m in msgs)
            {
                if (m.Severity == DiagSeverity.Error) Plugin.Log.LogError($"[EventCheck:{p.ModId}] {m.Message}");
                else Plugin.Log.LogWarning($"[EventCheck:{p.ModId}] {m.Message}");
            }
            if (msgs.Count == 0) Plugin.Log.LogInfo($"[EventCheck:{p.ModId}] Events OK.");
            return msgs.Count;
        }
    }
}
