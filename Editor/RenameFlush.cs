using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Renames update references after a short pause instead of on every keystroke. Editors register an apply action
    /// while a rename is pending; anything that reads project files from disk (saving, building, hot reload) calls
    /// <see cref="FlushAll"/> first so it never sees half-renamed data.
    /// </summary>
    public static class RenameFlush
    {
        public const float IdleSeconds = 2f;
        private static readonly List<Action> Pending = new List<Action>();
        private static float _lastTouch;

        public static bool Any => Pending.Count > 0;

        public static void Touch() => _lastTouch = Time.realtimeSinceStartup;

        public static void Register(Action apply)
        {
            if (!Pending.Contains(apply)) Pending.Add(apply);
            Touch();
        }

        public static void Unregister(Action apply) => Pending.Remove(apply);

        public static void FlushAll()
        {
            if (Pending.Count == 0) return;
            var list = Pending.ToArray();
            Pending.Clear();
            foreach (var apply in list)
            {
                try { apply(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[References] Rename flush failed: {ex.Message}"); }
            }
        }

        /// <summary>Called every frame by the editor; applies pending renames once typing has stopped.</summary>
        public static void Tick()
        {
            // Layout only: the banner and the controls must be identical between the Layout and Repaint passes of a frame
            var e = Event.current;
            if (e == null || e.type != EventType.Layout) return;
            if (Pending.Count > 0 && Time.realtimeSinceStartup - _lastTouch > IdleSeconds) FlushAll();
        }
    }

    /// <summary>
    /// One editor's pending id rename. The entry is re-keyed on every keystroke, but references to it are only rewritten
    /// once typing pauses (or on Apply / save / hot reload), with a preview of what will change.
    /// </summary>
    public sealed class RenameTracker
    {
        private readonly string _label;
        private readonly Func<Core.ModProject, bool, System.Collections.IDictionary> _dictOf;
        private readonly Action<string, string, bool> _revert;
        private string _from, _to, _family;
        private bool _patch;
        private Core.ModProject _proj;
        private List<Core.RefHit> _hits;
        // Banner state latched on Layout so Layout and Repaint draw the same controls; clicks are queued for the next Layout
        private bool _shown;
        private string _sFrom, _sTo;
        private List<Core.RefHit> _sHits;
        private int _queued; // 1 = apply, 2 = cancel

        /// <param name="dictOf">The project dictionary that holds the entries (patch or new).</param>
        /// <param name="revert">Renames an entry (current id, id to restore, isPatch) without touching references.</param>
        public RenameTracker(string label, Func<Core.ModProject, bool, System.Collections.IDictionary> dictOf, Action<string, string, bool> revert)
        {
            _label = label; _dictOf = dictOf; _revert = revert;
        }

        public bool Pending => _from != null;

        /// <summary>The id was just edited and the entry already re-keyed.</summary>
        public void Note(Core.ModProject proj, string prev, string next, bool isPatch, string family)
        {
            if (_from != null && _to == prev && _proj == proj && _patch == isPatch) _to = next;
            else
            {
                Apply();
                _from = prev; _to = next; _proj = proj; _patch = isPatch;
            }
            _family = family;
            if (_from == _to) { Clear(); return; }
            _hits = Core.ModReferences.FindUsages(proj, _from, family, _to);
            if (_hits.Count == 0) { Clear(); return; }
            RenameFlush.Register(Apply);
        }

        /// <summary>Apply a pending rename unless it belongs to the entry that is selected now.</summary>
        public void ApplyUnlessSelected(string selectedId)
        {
            if (_to != null && (selectedId != _to || _proj != Tabs.ModManagerPanel.ActiveProject)) Apply();
        }

        private void Clear()
        {
            if (_from != null) RenameFlush.Unregister(Apply);
            _from = _to = _family = null; _proj = null; _hits = null;
        }

        public void Apply()
        {
            string from = _from, to = _to, family = _family;
            var proj = _proj;
            bool patch = _patch;
            Clear();
            if (from == null || to == null || proj == null) return;
            var dict = _dictOf(proj, patch);
            // Undone or replaced in the meantime: nothing to rewrite
            if (dict == null || !dict.Contains(to) || dict.Contains(from)) return;
            int n = Core.ModReferences.Rewrite(proj, from, to, family, to);
            if (n == 0) return;
            Plugin.Log.LogInfo($"[References] Renamed {_label} '{from}' -> '{to}': updated {n} reference(s).");
            EditorStatus.Toast($"Renamed {from} \u2192 {to}  ({n} reference(s) updated)");
        }

        private void Cancel()
        {
            if (_from == null) return;
            string from = _from, to = _to;
            var p = _proj; bool patch = _patch;
            Clear();
            _revert(to, from, patch);
            p.IsDirty = true;
        }

        public void Draw()
        {
            if (Event.current.type == EventType.Layout)
            {
                int q = _queued;
                _queued = 0;
                if (q == 1) Apply();
                else if (q == 2) Cancel();
                // A different mod is open now: settle the rename for the project it belongs to rather than offering to undo it here
                if (_from != null && _proj != Tabs.ModManagerPanel.ActiveProject) Apply();
                _shown = _from != null && _hits != null;
                if (_shown) { _sFrom = _from; _sTo = _to; _sHits = _hits; }
            }
            if (!_shown) return;
            var hits = _sHits;
            string from = _sFrom, to = _sTo;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<color=#ffcc66>Renaming <b>{from}</b> \u2192 <b>{to}</b>: {hits.Count} reference(s) will be updated when you stop typing.</color>", EditorStyles.RichLabel);
            int shown = Mathf.Min(hits.Count, 12);
            for (int i = 0; i < shown; i++)
                GUILayout.Label($"  <color=#aaa>{hits[i].Family}</color> {hits[i].OwnerId} <color=#888>\u2192 {hits[i].Field}</color>", EditorStyles.RichLabel);
            if (hits.Count > shown) GUILayout.Label($"  <color=#888>\u2026and {hits.Count - shown} more</color>", EditorStyles.RichLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Apply now", "Update the references immediately"), EditorStyles.MiniButton, GUILayout.Width(80))) _queued = 1;
            if (GUILayout.Button(new GUIContent("Cancel rename", "Go back to the old id and leave the references alone"), EditorStyles.MiniButton, GUILayout.Width(100)))
                _queued = 2;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
