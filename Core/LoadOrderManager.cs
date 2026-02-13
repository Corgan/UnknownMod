using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace UnknownMod.Core
{
    /// <summary>
    /// Manages the global load order config at <c>Data/loadorder.json</c>.
    /// Handles auto-discovery of new mod folders and provides reorder operations.
    /// </summary>
    public static class LoadOrderManager
    {
        // ── State ────────────────────────────────────────────────────

        private static List<string> _order = new();

        /// <summary>Current load order (read-only view).</summary>
        public static IReadOnlyList<string> Order => _order;

        private static string LoadOrderPath =>
            Path.Combine(ModProjectLoader.DataRoot, "loadorder.json");

        private static readonly JsonSerializerSettings _json = new()
        {
            Formatting = Formatting.Indented
        };

        // ═══════════════════════════════════════════════════════════════
        //  LOAD / SAVE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Load the load order from disk and auto-discover any new mod folders.
        /// </summary>
        public static void Load()
        {
            _order.Clear();

            // Read existing loadorder.json
            if (File.Exists(LoadOrderPath))
            {
                try
                {
                    var json = File.ReadAllText(LoadOrderPath);
                    var data = JsonConvert.DeserializeObject<LoadOrderFile>(json);
                    if (data?.order != null)
                        _order.AddRange(data.order);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[LoadOrder] Failed to read loadorder.json: {ex.Message}");
                }
            }

            // Auto-discover mod folders not in the list
            var discovered = ModProjectLoader.DiscoverMods();
            foreach (var modId in discovered)
            {
                if (!_order.Contains(modId))
                {
                    _order.Add(modId);
                    Plugin.Log.LogInfo($"[LoadOrder] Auto-discovered new mod: {modId}");
                }
            }

            // Remove entries for mods that no longer exist on disk
            _order.RemoveAll(id => !discovered.Contains(id));

            Plugin.Log.LogInfo($"[LoadOrder] Loaded {_order.Count} mod(s): {string.Join(", ", _order)}");

            // Save immediately to persist auto-discovery changes
            Save();
        }

        /// <summary>Write the current load order to disk.</summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ModProjectLoader.DataRoot);
                var data = new LoadOrderFile { order = _order };
                File.WriteAllText(LoadOrderPath, JsonConvert.SerializeObject(data, _json));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[LoadOrder] Failed to save loadorder.json: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  REORDER OPERATIONS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Move a mod up in the load order (loads earlier = lower priority).</summary>
        public static bool MoveUp(string modId)
        {
            int idx = _order.IndexOf(modId);
            if (idx <= 0) return false;
            _order.RemoveAt(idx);
            _order.Insert(idx - 1, modId);
            Save();
            return true;
        }

        /// <summary>Move a mod down in the load order (loads later = higher priority).</summary>
        public static bool MoveDown(string modId)
        {
            int idx = _order.IndexOf(modId);
            if (idx < 0 || idx >= _order.Count - 1) return false;
            _order.RemoveAt(idx);
            _order.Insert(idx + 1, modId);
            Save();
            return true;
        }

        /// <summary>Move a mod to a specific index.</summary>
        public static void MoveTo(string modId, int targetIndex)
        {
            if (!_order.Remove(modId)) return;
            targetIndex = Math.Max(0, Math.Min(targetIndex, _order.Count));
            _order.Insert(targetIndex, modId);
            Save();
        }

        /// <summary>Add a new mod to the end of the load order.</summary>
        public static void AddMod(string modId)
        {
            if (_order.Contains(modId)) return;
            _order.Add(modId);
            Save();
        }

        /// <summary>Remove a mod from the load order.</summary>
        public static void RemoveMod(string modId)
        {
            _order.Remove(modId);
            Save();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LOAD ALL MODS IN ORDER
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Load all mods from disk in load-order sequence and build them.
        /// Returns the list of loaded ModProject instances.
        /// </summary>
        public static List<ModProject> LoadAndBuildAll()
        {
            Load();

            var projects = new List<ModProject>();
            foreach (var modId in _order)
            {
                try
                {
                    var proj = ModProjectLoader.Load(modId);
                    projects.Add(proj);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[LoadOrder] Failed to load mod '{modId}': {ex.Message}");
                }
            }

            ModProjectBuilder.BuildAll(projects);
            return projects;
        }

        // ═══════════════════════════════════════════════════════════════
        //  JSON MODEL
        // ═══════════════════════════════════════════════════════════════

        [Serializable]
        private class LoadOrderFile
        {
            public List<string> order = new();
        }
    }
}
