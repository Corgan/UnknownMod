using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnknownMod.Definitions;
using UnknownMod.Editor;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    /// <summary>
    /// Editor-side zone mutation service. Owns CurrentZone, dirty/auto-save state,
    /// node add/delete, combat/event creation, BFS reflow, hot-reload Rebuild methods,
    /// and folder-based save.
    /// </summary>
    public static partial class ZoneEditingService
    {
        //  Current zone DTO (source of truth for editor) 
        public static ZoneDef CurrentZone { get; set; }

        /// <summary>
        /// When editing a zone patch, this holds the underlying ZonePatchDef.
        /// Mutations to CurrentZone (the synthesized zone) are synced back here.
        /// Null when editing a new (custom) zone.
        /// </summary>
        public static ZonePatchDef CurrentPatch { get; set; }

        //  Dirty / auto-save state 
        private static bool _dirty;
        private static float _lastDirtyTime;
        private const float AutoSaveDelay = 2.0f;

        public static void MarkDirty()
        {
            _dirty = true;
            _lastDirtyTime = Time.unscaledTime;
        }

        public static void TickAutoSave()
        {
            if (!_dirty) return;
            if (Time.unscaledTime - _lastDirtyTime < AutoSaveDelay) return;
            _dirty = false;
            SaveCurrentZone();
            HotReloadToGame();
        }

        public static bool IsDirty => _dirty;

        //  JSON settings 
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        // 
        //  SAVE
        // 

        public static void SaveCurrentZone()
        {
            if (CurrentZone == null) return;

            // If editing a zone patch, sync changes back to the patch def
            if (CurrentPatch != null)
            {
                SyncSynthesizedToPatch();
                return;
            }

            string folder = ModRegistry.GetZoneFolder(CurrentZone.ZoneId);
            SaveToFolder(CurrentZone, folder);
            Plugin.Log.LogInfo($"[ZoneEditing] Saved zone to: {folder}");
        }

        /// <summary>
        /// Sync mutations from the synthesized CurrentZone back into CurrentPatch.
        /// Only patch-owned nodes (those in the patch's Nodes dict or newly added ones
        /// with the patch prefix) are persisted.
        /// </summary>
        private static void SyncSynthesizedToPatch()
        {
            if (CurrentPatch == null || CurrentZone == null) return;

            string zoneId = CurrentPatch.TargetZoneId;
            var basePositions = _positionCache.ContainsKey(zoneId) ? _positionCache[zoneId] : null;

            // Nodes: sync any node that is:
            //   (a) already in the patch (existing patch additions/modifications), or
            //   (b) NOT in the base position cache (newly added during this session)
            foreach (var kvp in CurrentZone.Nodes)
            {
                if (CurrentPatch.Nodes.ContainsKey(kvp.Key))
                {
                    // Update existing patch node
                    CurrentPatch.Nodes[kvp.Key] = kvp.Value;
                }
                else if (basePositions != null && !basePositions.ContainsKey(kvp.Key))
                {
                    // New node not in the base zone  add to patch
                    CurrentPatch.Nodes[kvp.Key] = kvp.Value;
                }
            }

            // Roads: sync roads that involve at least one patch node
            foreach (var kvp in CurrentZone.Roads)
            {
                if (CurrentPatch.Roads.ContainsKey(kvp.Key))
                    CurrentPatch.Roads[kvp.Key] = kvp.Value;
                else if (CurrentPatch.Nodes.ContainsKey(kvp.Value.FromNodeId) ||
                         CurrentPatch.Nodes.ContainsKey(kvp.Value.ToNodeId))
                    CurrentPatch.Roads[kvp.Key] = kvp.Value;
            }

            // Events/encounters: sync any that exist in the patch
            foreach (var kvp in CurrentZone.Events)
            {
                if (CurrentPatch.Events.ContainsKey(kvp.Key))
                    CurrentPatch.Events[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in CurrentZone.Combats)
            {
                if (CurrentPatch.Encounters.ContainsKey(kvp.Key))
                    CurrentPatch.Encounters[kvp.Key] = kvp.Value;
            }

            // Update NextNodeNumber
            string prefix = CurrentPatch.DetectedPrefix;
            int maxNum = CurrentPatch.NextNodeNumber;
            foreach (var nodeId in CurrentPatch.Nodes.Keys)
            {
                if (nodeId.StartsWith(prefix) && int.TryParse(nodeId.Substring(prefix.Length), out int n))
                    maxNum = Math.Max(maxNum, n + 1);
            }
            CurrentPatch.NextNodeNumber = maxNum;

            Plugin.Log.LogInfo($"[ZoneEditing] Synced patch '{CurrentPatch.TargetZoneId}': {CurrentPatch.Nodes.Count} nodes, {CurrentPatch.Roads.Count} roads");
        }

        private static void SaveToFolder(ZoneDef def, string folder)
        {
            foreach (string sub in new[] { "", "nodes", "combats", "events", "npcs", "cards", "items", "loot", "sprites" })
                Directory.CreateDirectory(Path.Combine(folder, sub));

            var meta = new
            {
                def.ZoneId, def.ZoneName, def.IdPrefix,
                def.ObeliskLow, def.ObeliskHigh, def.ObeliskFinal,
                def.DisableExperience, def.DisableMadness,
                def.BackgroundImage
            };
            WriteJson(Path.Combine(folder, "zone.json"), meta);

            SaveEntities(Path.Combine(folder, "nodes"), def.Nodes, kvp => kvp.Value.NodeId);
            SaveEntities(Path.Combine(folder, "combats"), def.Combats, kvp => kvp.Value.CombatId);
            SaveEntities(Path.Combine(folder, "events"), def.Events, kvp => kvp.Value.EventId);
            SaveEntities(Path.Combine(folder, "npcs"), def.Npcs, kvp => kvp.Value.Id);
            SaveEntities(Path.Combine(folder, "cards"), def.Cards, kvp => kvp.Value.Id);
            SaveEntities(Path.Combine(folder, "items"), def.Items, kvp => kvp.Value.Id);
            SaveEntities(Path.Combine(folder, "loot"), def.Loot, kvp => kvp.Value.Id);
            SaveEntities(Path.Combine(folder, "sprites"), def.Sprites, kvp => kvp.Value.NpcId);

            WriteJson(Path.Combine(folder, "roads.json"), def.Roads);
        }

        private static void SaveEntities<T>(string folder, Dictionary<string, T> dict,
            Func<KeyValuePair<string, T>, string> getFilename)
        {
            if (Directory.Exists(folder))
            {
                foreach (var existing in Directory.GetFiles(folder, "*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(existing);
                    if (!dict.ContainsKey(name))
                        File.Delete(existing);
                }
            }

            foreach (var kvp in dict)
            {
                string filename = getFilename(kvp) + ".json";
                WriteJson(Path.Combine(folder, filename), kvp.Value);
            }
        }

        private static void WriteJson(string path, object obj)
        {
            string json = JsonConvert.SerializeObject(obj, _jsonSettings);
            File.WriteAllText(path, json);
        }

    }
}
