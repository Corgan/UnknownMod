using System;
using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Project-wide validation shown next to the entry being edited (instead of only in the Mods tab's VALIDATE section).
    /// The whole project is validated at most when something changed and the user paused; each editor then filters by id.
    /// Results only change during Layout events so the number of controls stays the same for the rest of a frame.
    /// </summary>
    public static class InlineIssues
    {
        private static string _modId;
        private static float _computedForChange = float.NaN;
        private static float _dirtyAt = -100f;
        private static bool _dirty = true;
        private static List<DiagMessage> _all = new List<DiagMessage>();

        private const float IdleSeconds = 1.2f;

        /// <summary>Force a re-check once the user pauses (call after New / Delete / Duplicate / Undo, which do not touch LastChangeTime).</summary>
        public static void Invalidate()
        {
            _dirty = true;
            _dirtyAt = Time.realtimeSinceStartup;
        }

        public static void Refresh(ModProject p)
        {
            if (p == null || Event.current.type != EventType.Layout) return;
            float now = Time.realtimeSinceStartup;
            bool otherMod = _modId != p.ModId;
            bool edited = !float.Equals(_computedForChange, p.LastChangeTime);
            if (!otherMod && !_dirty && !edited) return;
            if (!otherMod && now - Mathf.Max(p.LastChangeTime, _dirtyAt) < IdleSeconds) return;

            var result = new List<DiagMessage>();
            try
            {
                result.AddRange(ReferenceValidator.Validate(p));
                result.AddRange(EventValidator.Validate(p));
                result.AddRange(ContentValidator.Validate(p));
                result.AddRange(GraphValidator.Validate(p));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[InlineIssues] validation failed: {ex.Message}");
            }
            _all = result;
            _modId = p.ModId;
            _computedForChange = p.LastChangeTime;
            _dirty = false;
        }

        /// <summary>Messages that are about this id (validators write them as "Family/id: text" or "Family/id reply n: text").</summary>
        public static List<DiagMessage> For(string id)
        {
            var list = new List<DiagMessage>();
            if (string.IsNullOrEmpty(id)) return list;
            string a = "/" + id + ":";
            string b = "/" + id + " ";
            foreach (var m in _all)
                if (m.Message != null && (m.Message.Contains(a) || m.Message.Contains(b))) list.Add(m);
            return list;
        }

        /// <summary>Strips the "Family/id: " prefix so the message reads as a sentence under the entry it is about.</summary>
        public static string ShortText(DiagMessage m, string id)
        {
            string text = m.Message ?? "";
            int colon = text.IndexOf(": ", StringComparison.Ordinal);
            string head = colon > 0 ? text.Substring(0, colon) : "";
            if (colon > 0 && head.Contains("/" + id)) text = (head.Contains(" reply") ? head.Substring(head.IndexOf(" reply", StringComparison.Ordinal) + 1) + ": " : "") + text.Substring(colon + 2);
            return text;
        }
    }
}
