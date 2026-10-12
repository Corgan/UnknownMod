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
        private static readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True unless the mod is listed under "disabled" in loadorder.json.</summary>
        public static bool IsEnabled(string modId) => !_disabled.Contains(modId);

        public static void SetEnabled(string modId, bool enabled)
        {
            if (enabled ? _disabled.Remove(modId) : _disabled.Add(modId))
                Save();
        }

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
            _disabled.Clear();

            // Read existing loadorder.json
            if (File.Exists(LoadOrderPath))
            {
                try
                {
                    var json = File.ReadAllText(LoadOrderPath);
                    var data = JsonConvert.DeserializeObject<LoadOrderFile>(json);
                    if (data?.order != null)
                        _order.AddRange(data.order.Where(id => !string.IsNullOrWhiteSpace(id)));
                    if (data?.disabled != null)
                        foreach (var id in data.disabled) _disabled.Add(id);
                }
                catch (Exception ex)
                {
                    // Keep the unreadable file so the user's order is not lost when we re-save below
                    string backup = LoadOrderPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Copy(LoadOrderPath, backup, true); } catch { }
                    Plugin.Log.LogWarning($"[LoadOrder] Failed to read loadorder.json ({ex.Message}); backed up to {backup}");
                }
            }

            // Collapse duplicate entries (case-insensitive), keeping the first occurrence
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _order.RemoveAll(id => !seen.Add(id));

            // Auto-discover mod folders not in the list (case-insensitive on Windows)
            var discovered = ModProjectLoader.DiscoverMods();
            foreach (var modId in discovered)
            {
                if (!_order.Any(existing => string.Equals(existing, modId, StringComparison.OrdinalIgnoreCase)))
                {
                    _order.Add(modId);
                    Plugin.Log.LogInfo($"[LoadOrder] Auto-discovered new mod: {modId}");
                }
            }

            // Remove entries for mods that no longer exist on disk
            _order.RemoveAll(id => !discovered.Any(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase)));
            // A recreated mod must not inherit the disabled flag of a deleted one
            _disabled.RemoveWhere(id => !discovered.Any(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase)));

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
                if (_disabled.Count > 0) data.disabled = _disabled.ToList();
                ModProjectLoader.AtomicWrite(LoadOrderPath, JsonConvert.SerializeObject(data, _json));
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

            var projects = LoadProjects("load");
            ModProjectBuilder.BuildAll(projects);
            HotReload.MarkBuilt();
            foreach (var proj in projects)
            {
                try { UnknownMod.Editor.ReferenceValidator.LogReport(proj); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[RefCheck:{proj.ModId}] Skipped: {ex.Message}"); }
                try { UnknownMod.Editor.EventValidator.LogReport(proj); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[EventCheck:{proj.ModId}] Skipped: {ex.Message}"); }
                try { UnknownMod.Editor.GraphValidator.LogReport(proj); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[GraphCheck:{proj.ModId}] Skipped: {ex.Message}"); }
                try { UnknownMod.Editor.ContentValidator.LogReport(proj); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[ContentCheck:{proj.ModId}] Skipped: {ex.Message}"); }
            }
            return projects;
        }

        /// <summary>Load every enabled mod in order, isolating per-mod failures, then enforce dependencies.</summary>
        private static List<ModProject> LoadProjects(string verb)
        {
            var projects = new List<ModProject>();
            foreach (var modId in _order)
            {
                if (!IsEnabled(modId))
                {
                    Plugin.Log.LogInfo($"[LoadOrder] Skipping disabled mod '{modId}'");
                    continue;
                }
                try
                {
                    var loaded = ModProjectLoader.Load(modId);
                    // Its dependency list lives in the metadata, so without it the mod cannot be loaded safely
                    if (loaded.MetadataUnreadable)
                    {
                        Plugin.Log.LogError($"[LoadOrder] Mod '{modId}' NOT loaded: {modId}.json could not be read, so its dependencies are unknown");
                        continue;
                    }
                    projects.Add(loaded);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[LoadOrder] Failed to {verb} mod '{modId}': {ex.Message}");
                }
            }
            return ResolveDependencies(projects);
        }

        /// <summary>
        /// Whether a mod may be built into the running game right now: readable metadata, every (transitive) dependency
        /// enabled and loaded, and no dependency cycle. Start-up applies the same rules through <see cref="ResolveDependencies"/>.
        /// </summary>
        public static bool CanBuild(ModProject proj, out string why)
        {
            why = null;
            if (proj.MetadataUnreadable) { why = $"{proj.ModId}.json could not be read, so its dependencies are unknown"; return false; }
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { proj.ModId };
            string problem = null;
            bool Ok(IEnumerable<string> deps)
            {
                foreach (var d in deps)
                {
                    if (visiting.Contains(d)) { problem = $"dependency cycle through '{d}'"; return false; }
                    if (!IsEnabled(d) || !Order.Contains(d, StringComparer.OrdinalIgnoreCase)) { problem = $"missing or disabled dependency '{d}'"; return false; }
                    visiting.Add(d);
                    var sub = HotReload.ReadDepends(d);
                    if (sub == null) { problem = $"metadata of dependency '{d}' could not be read"; visiting.Remove(d); return false; }
                    bool ok = Ok(sub);
                    visiting.Remove(d);
                    if (!ok) return false;
                }
                return true;
            }
            bool result = Ok(proj.Dependencies);
            why = problem;
            return result;
        }

        /// <summary>
        /// Drops mods whose (transitive) dependencies are missing and reorders the rest so every
        /// dependency loads before its dependents. The stored order is otherwise preserved.
        /// </summary>
        internal static List<ModProject> ResolveDependencies(List<ModProject> projects)
        {
            var byId = new Dictionary<string, ModProject>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in projects) byId[p.ModId] = p;

            // Remove mods with missing dependencies until stable (handles transitive chains)
            bool removed;
            do
            {
                removed = false;
                foreach (var p in projects.ToList())
                {
                    var missing = p.Dependencies.Where(d => !byId.ContainsKey(d)).ToList();
                    if (missing.Count == 0) continue;
                    Plugin.Log.LogError($"[LoadOrder] Mod '{p.ModId}' NOT loaded: missing/disabled dependenc{(missing.Count == 1 ? "y" : "ies")} {string.Join(", ", missing)}");
                    projects.Remove(p);
                    byId.Remove(p.ModId);
                    removed = true;
                }
            } while (removed);

            // Repeatedly take the first mod in stored order whose dependencies are all placed. Whatever is left can never
            // be satisfied (a dependency cycle, or a dependency on a cycle member), so it is not loaded.
            var result = new List<ModProject>();
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var waiting = projects.ToList();
            while (waiting.Count > 0)
            {
                var next = waiting.FirstOrDefault(p => p.Dependencies.All(d => placed.Contains(d)));
                if (next == null) break;
                waiting.Remove(next);
                placed.Add(next.ModId);
                result.Add(next);
            }
            foreach (var p in waiting)
                Plugin.Log.LogError($"[LoadOrder] Mod '{p.ModId}' NOT loaded: dependency cycle ({string.Join(", ", p.Dependencies.Where(d => !placed.Contains(d)))})");

            if (waiting.Count > 0)
                Plugin.Log.LogInfo($"[LoadOrder] Not loaded because of dependency cycles: {string.Join(", ", waiting.Select(p => p.ModId))}");
            if (!result.Select(p => p.ModId).SequenceEqual(projects.Where(p => !waiting.Contains(p)).Select(p => p.ModId), StringComparer.OrdinalIgnoreCase))
                Plugin.Log.LogInfo($"[LoadOrder] Reordered for dependencies: {string.Join(", ", result.Select(p => p.ModId))}");
            return result;
        }

        public static List<ModProject> RefreshVisualGraphs()
        {
            Load();
            var projects = LoadProjects("refresh");
            ModRegistry.RefreshVisualGraphs(projects);
            return projects;
        }

        // ═══════════════════════════════════════════════════════════════
        //  JSON MODEL
        // ═══════════════════════════════════════════════════════════════

        [Serializable]
        private class LoadOrderFile
        {
            public List<string> order = new();
            public List<string> disabled;
        }
    }
}
