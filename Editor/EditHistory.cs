using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Undo/redo stack of items. An item is either a state edit (before/after strings applied through a callback; consecutive edits
    /// with the same key merge) or a structural step with explicit undo/redo actions (create, delete, rename, ...).
    /// </summary>
    public sealed class EditHistory
    {
        private sealed class Item
        {
            public string Key, Label, Before, After;
            public Action<string> ApplyState;
            public Action Undo, Redo;
            public object Tag;
            public float Time;
        }

        private readonly List<Item> _undo = new List<Item>();
        private readonly List<Item> _redo = new List<Item>();

        public int MaxItems = 80;
        public float CoalesceSeconds = 1.2f;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Clear() { _undo.Clear(); _redo.Clear(); }

        /// <summary>A null key never merges with the previous item (use it for one-off structural changes expressed as state).</summary>
        public void RecordEdit(string key, string label, string before, string after, Action<string> apply)
        {
            float now = Time.realtimeSinceStartup;
            var top = _undo.Count > 0 ? _undo[_undo.Count - 1] : null;
            if (key != null && top != null && top.ApplyState != null && top.Key == key && now - top.Time < CoalesceSeconds)
            {
                top.After = after;
                top.Time = now;
            }
            else
                Push(new Item { Key = key, Label = label, Before = before, After = after, ApplyState = apply, Time = now });
            _redo.Clear();
        }

        public void Record(string label, Action undo, Action redo, object tag = null)
        {
            Push(new Item { Label = label, Undo = undo, Redo = redo, Tag = tag, Time = Time.realtimeSinceStartup });
            _redo.Clear();
        }

        /// <summary>Tag of the newest item if it was recorded within the coalescing window, otherwise null.</summary>
        public object RecentTopTag
        {
            get
            {
                if (_undo.Count == 0) return null;
                var top = _undo[_undo.Count - 1];
                return Time.realtimeSinceStartup - top.Time < CoalesceSeconds ? top.Tag : null;
            }
        }

        public void RetouchTop(string label)
        {
            if (_undo.Count == 0) return;
            var top = _undo[_undo.Count - 1];
            top.Label = label;
            top.Time = Time.realtimeSinceStartup;
        }

        /// <summary>Discard the newest undo item (a rename that ended where it started has nothing to undo).</summary>
        public void DropTop()
        {
            if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
        }

        private void Push(Item item)
        {
            _undo.Add(item);
            if (_undo.Count > MaxItems) _undo.RemoveAt(0);
        }

        public void Undo() => Step(_undo, _redo, true);
        public void Redo() => Step(_redo, _undo, false);

        private static void Step(List<Item> from, List<Item> to, bool undo)
        {
            string verb = undo ? "Undo" : "Redo";
            if (from.Count == 0) { EditorStatus.Toast("Nothing to " + verb.ToLower(), true); return; }
            var item = from[from.Count - 1];
            from.RemoveAt(from.Count - 1); // out of the stack before the callback, which may record history itself
            try
            {
                if (item.ApplyState != null) item.ApplyState(undo ? item.Before : item.After);
                else (undo ? item.Undo : item.Redo)();
                item.Time = -1000f;
                to.Add(item);
                EditorStatus.Toast($"{verb}: {item.Label}");
            }
            catch (Exception ex)
            {
                from.Add(item); // keep it so the step can be retried
                Plugin.Log.LogWarning($"[EditHistory] {verb} '{item.Label}' failed: {ex}");
                EditorStatus.Toast($"{verb} failed: {ex.Message}", true);
            }
        }
    }

    /// <summary>
    /// Entity-level history for the mod-project editors: tracks edits to the selected entity and records create / delete /
    /// override / rename so they can be undone. The owning editor supplies how to find, save, delete and select entities.
    /// </summary>
    public sealed class EntityHistory<TDef> where TDef : class, IModEntity, new()
    {
        public Func<ModProject, Dictionary<string, TDef>> NewDict;
        public Func<ModProject, Dictionary<string, TDef>> PatchDict;
        public Action<ModProject, TDef, bool> Save;
        public Action<ModProject, string, bool> DeleteFiles;
        public Func<string> GetSelected;
        public Action<string> Select;
        /// <summary>Called after undo/redo changed an entity, so owners can refresh previews.</summary>
        public Action Restored;

        public readonly EditHistory Stack = new EditHistory();

        private string _before;
        private string _beforeKey;

        public bool CanUndo => Stack.CanUndo;
        public bool CanRedo => Stack.CanRedo;

        private static ModProject Proj => ModManagerPanel.ActiveProject;
        private Dictionary<string, TDef> Dict(ModProject p, bool patch) => patch ? PatchDict(p) : NewDict(p);

        public static string StateOf(TDef d) => JsonConvert.SerializeObject(d);

        /// <summary>
        /// Writes a state back into the live object (other code holds references to it). A fresh instance is deserialised and its fields
        /// copied over, because default-valued members are omitted from the JSON and would otherwise keep their edited value.
        /// </summary>
        public static void RestoreInto(TDef target, string json)
        {
            var fresh = JsonConvert.DeserializeObject<TDef>(json);
            if (fresh != null) UnknownMod.Core.DefCopy.CopyInto(target, fresh);
        }

        private void Touch(ModProject p)
        {
            p.IsDirty = true;
            p.LastChangeTime = Time.realtimeSinceStartup;
            _before = null;
            InlineIssues.Invalidate();
            GUI.changed = true;
            Restored?.Invoke();
        }

        private void SelectIfDifferent(string id)
        {
            if (Select != null && GetSelected?.Invoke() != id) Select(id);
        }

        // ── Edit tracking ──────────────────────────────────────
        /// <summary>Call before drawing the selected entity's fields.</summary>
        public void BeginEdit(string id, bool isPatch, TDef d)
        {
            string key = (isPatch ? "p:" : "n:") + id;
            var ev = Event.current.type;
            if (_before == null || _beforeKey != key || ev == EventType.MouseDown || ev == EventType.KeyDown)
            {
                _before = StateOf(d);
                _beforeKey = key;
            }
        }

        /// <summary>Call after drawing; records an undo step when GUI.changed and the entity really differs.</summary>
        public void EndEdit(string id, bool isPatch, TDef d)
        {
            if (!GUI.changed || _before == null) return;
            string after = StateOf(d);
            if (after == _before) return;
            string key = (isPatch ? "p:" : "n:") + id;
            Stack.RecordEdit(key, id, _before, after, state => ApplyEdit(id, isPatch, state));
            _before = after;
        }

        public void ResetBefore() => _before = null;

        /// <summary>
        /// For editors whose edits arrive from several places (panel fields, viewport drags). Call whenever the entity may have changed:
        /// the first call per entity only takes a baseline; later calls record a step if the state differs from the baseline.
        /// </summary>
        public void Track(string id, bool isPatch, TDef d)
        {
            string key = (isPatch ? "p:" : "n:") + id;
            string after = StateOf(d);
            if (_before == null || _beforeKey != key) { _before = after; _beforeKey = key; return; }
            if (after == _before) return;
            Stack.RecordEdit(key, id, _before, after, state => ApplyEdit(id, isPatch, state));
            _before = after;
        }

        private void ApplyEdit(string id, bool isPatch, string state)
        {
            var p = Proj;
            if (p == null || !Dict(p, isPatch).TryGetValue(id, out var live)) return;
            RestoreInto(live, state);
            live.EntityId = id;
            Save(p, live, isPatch);
            SelectIfDifferent(id);
            Touch(p);
        }

        // ── Structural steps ───────────────────────────────────
        public void Restore(string id, bool isPatch, string state)
        {
            var p = Proj;
            if (p == null) return;
            var dict = Dict(p, isPatch);
            if (dict.ContainsKey(id)) return;
            var def = JsonConvert.DeserializeObject<TDef>(state);
            if (def == null) return;
            def.EntityId = id;
            dict[id] = def;
            Save(p, def, isPatch);
            SelectIfDifferent(id);
            Touch(p);
        }

        public void Remove(string id, bool isPatch)
        {
            var p = Proj;
            if (p == null) return;
            if (!Dict(p, isPatch).Remove(id)) return;
            DeleteFiles(p, id, isPatch);
            if (GetSelected != null && GetSelected() == id) Select?.Invoke(null);
            Touch(p);
        }

        /// <summary>Removes an entry and records it (state captured first) so the removal can be undone. Safe to call mid-draw.</summary>
        public void Delete(string id, bool isPatch, string what)
        {
            RenameFlush.FlushAll();
            var p = Proj;
            if (p == null || !Dict(p, isPatch).TryGetValue(id, out var def)) return;
            string state = StateOf(def);
            bool changed = GUI.changed;
            Remove(id, isPatch);
            GUI.changed = changed;
            RecordDeleted(id, isPatch, state, what);
        }

        public void Rename(string from, string to, bool isPatch)
        {
            var p = Proj;
            if (p == null) return;
            var dict = Dict(p, isPatch);
            if (!dict.TryGetValue(from, out var live) || dict.ContainsKey(to)) return;
            dict.Remove(from);
            live.EntityId = to;
            dict[to] = live;
            DeleteFiles(p, from, isPatch);
            Save(p, live, isPatch);
            ModReferences.Rewrite(p, from, to, ModReferences.FamilyOf(p, dict), to);
            SelectIfDifferent(to);
            Touch(p);
        }

        /// <summary>Entity just created (new, duplicate or override). Undo removes it; redo brings back what it looked like when undone.</summary>
        public void RecordCreated(string id, bool isPatch, string what = "create")
        {
            string saved = null;
            Stack.Record($"{what} {id}",
                () =>
                {
                    var p = Proj;
                    if (p != null && Dict(p, isPatch).TryGetValue(id, out var live)) saved = StateOf(live);
                    Remove(id, isPatch);
                },
                () => { if (saved != null) Restore(id, isPatch, saved); });
            _before = null;
        }

        /// <summary>Entity just removed. <paramref name="state"/> is its <see cref="StateOf"/> captured before removal.</summary>
        public void RecordDeleted(string id, bool isPatch, string state, string what = "delete")
        {
            Stack.Record($"{what} {id}", () => Restore(id, isPatch, state), () => Remove(id, isPatch));
            _before = null;
        }

        private sealed class RenameTag { public string Orig, Cur; public bool IsPatch; }

        /// <summary>Typing a new id renames on every keystroke; consecutive renames of the same entry collapse into one step.</summary>
        public void RecordRenamed(string from, string to, bool isPatch)
        {
            _before = null;
            if (Stack.RecentTopTag is RenameTag top && top.Cur == from && top.IsPatch == isPatch)
            {
                top.Cur = to;
                if (top.Cur == top.Orig) Stack.DropTop();
                else Stack.RetouchTop($"rename {top.Orig} \u2192 {to}");
                return;
            }
            var tag = new RenameTag { Orig = from, Cur = to, IsPatch = isPatch };
            Stack.Record($"rename {from} \u2192 {to}",
                () => Rename(tag.Cur, tag.Orig, isPatch),
                () => Rename(tag.Orig, tag.Cur, isPatch),
                tag);
        }

        public void Undo() => Stack.Undo();
        public void Redo() => Stack.Redo();
    }
}
