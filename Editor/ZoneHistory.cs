using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Undo/redo for the Zones sub-tab. Edits come from many places (viewport drags, node panel, map panel, road tools), so instead of
    /// instrumenting each one the zone is snapshotted whenever something may have changed and diffed against the previous snapshot.
    /// Create / delete / patch / revert are recorded as explicit steps.
    /// </summary>
    public sealed class ZoneHistory : IEntityActions
    {
        private sealed class State
        {
            public string Id;
            public bool Patch;
            public string Json;
            public Dictionary<string, string> Combats = new Dictionary<string, string>();
            public Dictionary<string, string> Events = new Dictionary<string, string>();
        }

        private sealed class Snap
        {
            public string Sig, Json;
        }

        private readonly EditHistory _stack = new EditHistory();
        private readonly ModEditor _editor;
        private readonly Func<string> _getSelected;
        private readonly Action<string> _select;

        private string _id;
        private Snap _last;
        private int _seenVersion;
        private int _lastFrame;
        private float _lastPoll;

        public ZoneHistory(ModEditor editor, Func<string> getSelected, Action<string> select)
        {
            _editor = editor;
            _getSelected = getSelected;
            _select = select;
        }

        public bool CanDuplicate => false;
        public bool CanUndo => _stack.CanUndo;
        public bool CanRedo => _stack.CanRedo;

        public void Run(EditorCommand command)
        {
            if (command != EditorCommand.Undo && command != EditorCommand.Redo) return;
            var p = ModManagerPanel.ActiveProject;
            if (p == null) return;
            Poll(p, _getSelected(), true);
            if (command == EditorCommand.Undo) _stack.Undo(); else _stack.Redo();
        }

        // ── Snapshots ────────────────────────────────────────────

        private static State CaptureState(ModProject p, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var st = new State { Id = id };
            Dictionary<string, NodeDef> nodes;
            if (p.Zones.TryGetValue(id, out var zone))
            {
                st.Json = JsonConvert.SerializeObject(zone);
                nodes = zone.Nodes;
            }
            else if (p.ZonePatches.TryGetValue(id, out var patch))
            {
                if (ZoneEditingService.CurrentPatch == patch) ZoneEditingService.SyncForSnapshot();
                st.Patch = true;
                st.Json = JsonConvert.SerializeObject(patch);
                nodes = patch.Nodes;
            }
            else return null;

            // Combats / events that "+ Combat" and "+ Event" generate for a node are deleted with it, so they travel with the snapshot.
            foreach (var n in nodes.Values)
            {
                foreach (var c in n.CombatIds)
                    if (c == "c" + n.NodeId && p.Combats.TryGetValue(c, out var cd)) st.Combats[c] = JsonConvert.SerializeObject(cd);
                foreach (var e in n.EventIds)
                    if (!string.IsNullOrEmpty(e) && e.StartsWith("e_" + n.NodeId + "_") && p.Events.TryGetValue(e, out var ed)) st.Events[e] = JsonConvert.SerializeObject(ed);
            }
            return st;
        }

        private static Snap Capture(ModProject p, string id)
        {
            var st = CaptureState(p, id);
            if (st == null) return null;
            // Contents of the generated combats / events are edited on their own tabs; only their presence is part of the zone's signature.
            string sig = st.Json + "|" + string.Join(",", st.Combats.Keys) + "|" + string.Join(",", st.Events.Keys);
            return new Snap { Sig = sig, Json = JsonConvert.SerializeObject(st) };
        }

        /// <summary>Call every frame while the Zones sub-tab is showing, and again after the panel was drawn (with force when GUI.changed).</summary>
        public void Poll(ModProject p, string id, bool force)
        {
            int frame = Time.frameCount;
            bool stale = _id != id || frame - _lastFrame > 3;
            _lastFrame = frame;
            if (stale || _last == null)
            {
                _id = id;
                _last = Capture(p, id);
                _seenVersion = ZoneEditingService.ChangeVersion;
                return;
            }

            if (!force)
            {
                if (_seenVersion == ZoneEditingService.ChangeVersion) return;
                if (Time.unscaledTime - _lastPoll < 0.1f) return;
            }
            _lastPoll = Time.unscaledTime;
            _seenVersion = ZoneEditingService.ChangeVersion;

            var cur = Capture(p, id);
            if (cur == null) return;
            if (cur.Sig != _last.Sig)
                _stack.RecordEdit("zone:" + id, $"edit zone '{id}'", _last.Json, cur.Json, Apply);
            _last = cur;
        }

        private void Resync(ModProject p, string id)
        {
            _id = id;
            _last = Capture(p, id);
            _seenVersion = ZoneEditingService.ChangeVersion;
            _lastFrame = Time.frameCount;
        }

        // ── Applying a snapshot ──────────────────────────────────

        private void Apply(string json)
        {
            var st = JsonConvert.DeserializeObject<State>(json);
            var p = ModManagerPanel.ActiveProject;
            if (p == null) throw new InvalidOperationException("no mod project is open");
            if (!p.Zones.ContainsKey(st.Id) && !p.ZonePatches.ContainsKey(st.Id))
                throw new InvalidOperationException($"zone '{st.Id}' no longer exists");

            var now = CaptureState(p, st.Id);

            if (st.Patch)
            {
                if (!p.ZonePatches.TryGetValue(st.Id, out var patch)) throw new InvalidOperationException($"'{st.Id}' is no longer a patch");
                EntityHistory<ZonePatchDef>.RestoreInto(patch, st.Json);
                ZoneEditingService.InvalidateSynthesizedZone(st.Id);
                ZoneEditingService.CurrentZone = null;
                ZoneEditingService.CurrentPatch = null;
                ZoneTabEditor.SaveZonePatch(p, patch);
            }
            else
            {
                if (!p.Zones.TryGetValue(st.Id, out var zone)) throw new InvalidOperationException($"'{st.Id}' is no longer a new zone");
                EntityHistory<ZoneDef>.RestoreInto(zone, st.Json);
                ModProjectLoader.SaveZone(p, zone);
                ModRegistry.LoadedZones[st.Id] = zone;
                if (_editor.SelectedNodeId != null && !zone.Nodes.ContainsKey(_editor.SelectedNodeId)) _editor.SelectedNodeId = null;
            }

            SyncGenerated(p, "combats", p.Combats, now?.Combats.Keys, st.Combats);
            SyncGenerated(p, "events", p.Events, now?.Events.Keys, st.Events);

            if (_getSelected() != st.Id) _select(st.Id);
            Touched(p);
            Resync(p, st.Id);
        }

        private static void SyncGenerated<T>(ModProject p, string folder, Dictionary<string, T> dict, ICollection<string> nowIds, Dictionary<string, string> want) where T : class
        {
            if (nowIds != null)
                foreach (var id in nowIds.Where(i => !want.ContainsKey(i)).ToList())
                {
                    dict.Remove(id);
                    ModProjectLoader.DeleteEntity(p, folder, id);
                }
            foreach (var kv in want)
            {
                if (dict.ContainsKey(kv.Key)) continue;
                var d = JsonConvert.DeserializeObject<T>(kv.Value);
                dict[kv.Key] = d;
                ModProjectLoader.SaveEntity(p, folder, kv.Key, d);
            }
        }

        private void Touched(ModProject p)
        {
            p.IsDirty = true;
            p.LastChangeTime = Time.realtimeSinceStartup;
            InlineIssues.Invalidate();
            _editor.MapEdit?.ForceRebuild();
            ModEditor.EntityPreview?.Invalidate();
            GUI.changed = true;
        }

        // ── Structural steps (new zone / patch / delete / revert) ──

        public void Created(ModProject p, string id, string what)
        {
            var snap = Capture(p, id)?.Json;
            if (snap == null) return;
            _stack.Record($"{what} '{id}'", () => Remove(p, id), () => Restore(p, snap));
            _id = null;
        }

        /// <summary>Deletes a new zone or reverts a patch, recording it so it can be undone.</summary>
        public void Delete(ModProject p, string id, string what)
        {
            var snap = Capture(p, id)?.Json;
            if (snap == null) return;
            bool changed = GUI.changed;
            Remove(p, id);
            GUI.changed = changed;
            _stack.Record($"{what} '{id}'", () => Restore(p, snap), () => Remove(p, id));
        }

        private void Remove(ModProject p, string id)
        {
            bool patch = p.ZonePatches.ContainsKey(id);
            if (patch)
                p.ZonePatches.Remove(id);
            else
            {
                p.Zones.Remove(id);
                ModRegistry.LoadedZones.Remove(id);
            }
            ZoneTabEditor.DeleteZoneFolder(p, id, patch);

            var cz = ZoneEditingService.CurrentZone;
            if (cz != null && cz.ZoneId == id)
            {
                ZoneEditingService.CurrentZone = null;
                ZoneEditingService.CurrentPatch = null;
            }
            ZoneEditingService.InvalidateSynthesizedZone(id);
            if (_getSelected() == id)
                _select(p.Zones.Keys.Concat(p.ZonePatches.Keys).FirstOrDefault(k => k != id));
            Touched(p);
            _id = null;
        }

        private void Restore(ModProject p, string json)
        {
            var st = JsonConvert.DeserializeObject<State>(json);
            if (st.Patch)
            {
                var patch = JsonConvert.DeserializeObject<ZonePatchDef>(st.Json);
                p.ZonePatches[st.Id] = patch;
                ZoneTabEditor.SaveZonePatch(p, patch);
                ZoneEditingService.InvalidateSynthesizedZone(st.Id);
            }
            else
            {
                var zone = JsonConvert.DeserializeObject<ZoneDef>(st.Json);
                p.Zones[st.Id] = zone;
                ModRegistry.LoadedZones[st.Id] = zone;
                ModProjectLoader.SaveZone(p, zone);
            }
            ZoneEditingService.CurrentZone = null;
            ZoneEditingService.CurrentPatch = null;
            _select(st.Id);
            Touched(p);
            _id = null;
        }
    }
}
