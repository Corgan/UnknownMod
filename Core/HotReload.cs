using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnknownMod.Editor;
using UnknownMod.Editor.Tabs;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    public sealed class HotReloadEntry
    {
        public DateTime Time;
        public string ModId;
        public string Summary;
        public bool IsError;
        public List<string> Details = new();
    }

    /// <summary>
    /// Keeps the running game in step with the mod files. A watcher on the data folder picks up edits made outside the game,
    /// works out what really changed, and rebuilds only that. The editor also uses it to push entities it edited.
    /// </summary>
    public static class HotReload
    {
        private enum Kind { Ignore, Data, Image, ImageMeta, Audio, Script }

        private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
        private const long QuietMs = 800;
        private const int MaxRetries = 6;
        private const int HistoryMax = 60;

        private static FileSystemWatcher _watcher;
        private static readonly ConcurrentQueue<string> _incoming = new();
        private static readonly Dictionary<string, Dictionary<string, int>> _pending = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _own = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _ownLock = new();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static long _lastEventMs;
        private static bool _built;
        private static int _overflow;

        public static readonly List<HotReloadEntry> History = new();
        public static string WatchPath { get; private set; }
        public static bool Watching => _watcher != null && _watcher.EnableRaisingEvents;
        public static int Pending => _incoming.Count + _pending.Values.Sum(v => v.Count);

        public static void MarkBuilt() => _built = true;

        // ─────────────────────────────────────────────────────────────
        //  WATCHING
        // ─────────────────────────────────────────────────────────────

        public static void Start()
        {
            try
            {
                WatchPath = ModProjectLoader.DataRoot;
                Directory.CreateDirectory(WatchPath);
                _watcher = new FileSystemWatcher(WatchPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                _watcher.Changed += (_, e) => Enqueue(e.FullPath);
                _watcher.Created += (_, e) => Enqueue(e.FullPath);
                _watcher.Deleted += (_, e) => Enqueue(e.FullPath);
                _watcher.Renamed += (_, e) => { Enqueue(e.OldFullPath); Enqueue(e.FullPath); };
                _watcher.Error += (_, e) =>
                {
                    Plugin.Log.LogWarning($"[HotReload] Watcher error (events may have been lost, rescanning): {e.GetException()?.Message}");
                    Interlocked.Exchange(ref _overflow, 1);
                    Interlocked.Exchange(ref _lastEventMs, _clock.ElapsedMilliseconds);
                };
                _watcher.EnableRaisingEvents = true;
                Plugin.Log.LogInfo($"[HotReload] Watching {WatchPath}");
            }
            catch (Exception ex)
            {
                _watcher = null;
                Plugin.Log.LogWarning($"[HotReload] Could not watch the data folder, external edits will need a manual reload: {ex.Message}");
            }
        }

        private static void Enqueue(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _incoming.Enqueue(path);
            Interlocked.Exchange(ref _lastEventMs, _clock.ElapsedMilliseconds);
        }

        /// <summary>The editor calls this just before it writes a file, so the watcher can tell its own saves from outside edits.</summary>
        public static void NoteWrite(string path, string content)
        {
            if (path == null || content == null) return;
            try
            {
                lock (_ownLock) _own[Path.GetFullPath(path)] = content.GetHashCode();
            }
            catch { /* only an optimisation: an unrecorded write just costs one extra comparison */ }
        }

        /// <summary>True when the file still holds exactly what the editor last wrote; null when it is locked right now.</summary>
        private static bool? IsOwnWrite(string path)
        {
            int expected;
            lock (_ownLock) if (!_own.TryGetValue(Path.GetFullPath(path), out expected)) return false;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd().GetHashCode() == expected;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static bool TryRel(string full, out string modId, out string rel)
        {
            modId = rel = null;
            try
            {
                string root = Path.GetFullPath(ModProjectLoader.DataRoot).TrimEnd('\\', '/');
                full = Path.GetFullPath(full);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
                string r = full.Substring(root.Length + 1).Replace('\\', '/');
                int i = r.IndexOf('/');
                if (i <= 0) return false;
                modId = r.Substring(0, i);
                rel = r.Substring(i + 1);
                if (modId.StartsWith("_") || modId.StartsWith(".")) return false;
                return rel.Length > 0 && !rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static Kind Classify(string rel)
        {
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            // Script project sources, build output and the scripts' private data are not mod data
            foreach (var skip in new[] { "scripts/", "scriptdata/", "obj/", "bin/", ".vs/" })
                if (rel.StartsWith(skip, StringComparison.OrdinalIgnoreCase)) return Kind.Ignore;
            if (rel.Equals("scripts.dll", StringComparison.OrdinalIgnoreCase)) return Kind.Script;
            bool inSprites = rel.StartsWith("sprites/", StringComparison.OrdinalIgnoreCase) && rel.IndexOf('/', 8) < 0;
            if (inSprites && Array.IndexOf(ImageExt, ext) >= 0) return Kind.Image;
            if (inSprites && ext == ".json") return Kind.ImageMeta;
            if (rel.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) && Array.IndexOf(AudioUtils.Extensions, ext) >= 0) return Kind.Audio;
            if (ext == ".json" || ext.Length == 0) return Kind.Data;
            return Kind.Ignore;
        }

        // ─────────────────────────────────────────────────────────────
        //  POLLING (main thread, from Plugin.Update)
        // ─────────────────────────────────────────────────────────────

        public static void Poll()
        {
            if (_watcher == null) return;

            // Lost events (buffer overflow): reload every enabled mod from disk once the editor is idle
            if (Volatile.Read(ref _overflow) == 1 && _built && !Busy()
                && _clock.ElapsedMilliseconds - Interlocked.Read(ref _lastEventMs) >= QuietMs)
            {
                Interlocked.Exchange(ref _overflow, 0);
                _pending.Clear();
                while (_incoming.TryDequeue(out _)) { }
                if (Plugin.AutoHotReload.Value)
                    foreach (var id in LoadOrderManager.Order.ToList())
                    {
                        if (!LoadOrderManager.IsEnabled(id)) continue;
                        try { ReloadFromDisk(id); }
                        catch (Exception ex) { Plugin.Log.LogError($"[HotReload] Rescan of '{id}' failed: {ex}"); }
                    }
                return;
            }

            while (_incoming.TryDequeue(out var path))
            {
                if (!TryRel(path, out var modId, out _)) continue;
                if (!_pending.TryGetValue(modId, out var files)) _pending[modId] = files = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (!files.ContainsKey(path)) files[path] = 0;
            }
            if (_pending.Count == 0) return;
            if (_clock.ElapsedMilliseconds - Interlocked.Read(ref _lastEventMs) < QuietMs) return;

            if (!Plugin.AutoHotReload.Value) { _pending.Clear(); return; }
            if (!_built || Busy()) return;

            var work = _pending.ToList();
            _pending.Clear();
            foreach (var kv in work)
            {
                try { ProcessExternal(kv.Key, kv.Value); }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[HotReload] Reloading '{kv.Key}' failed: {ex}");
                    Add(new HotReloadEntry { Time = DateTime.Now, ModId = kv.Key, IsError = true, Summary = "Reload failed: " + ex.Message });
                }
            }
        }

        /// <summary>Wait while the editor is mid-edit, so a reload never lands between a change and its save.</summary>
        private static bool Busy()
        {
            if (ZoneEditingService.IsDirty || RenameFlush.Any) return true;
            var a = ModManagerPanel.ActiveProject;
            return a != null && Time.realtimeSinceStartup - a.LastChangeTime < 2.5f;
        }

        private static void ProcessExternal(string modId, Dictionary<string, int> files)
        {
            bool data = false, audio = false, script = false;
            var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in files.ToList())
            {
                string f = kv.Key;
                if (!TryRel(f, out _, out var rel)) continue;
                switch (Classify(rel))
                {
                    case Kind.Data:
                        if (File.Exists(f) && Path.GetExtension(f).Length > 0)
                        {
                            var own = IsOwnWrite(f);
                            if (own == null)
                            {
                                if (kv.Value < MaxRetries) Requeue(modId, f, kv.Value + 1);
                                continue;
                            }
                            if (own == true) continue;
                        }
                        data = true;
                        break;
                    case Kind.Image: images.Add(f); break;
                    case Kind.ImageMeta:
                        foreach (var ext in ImageExt)
                        {
                            string sibling = Path.ChangeExtension(f, ext);
                            if (File.Exists(sibling)) images.Add(sibling);
                        }
                        break;
                    case Kind.Audio: audio = true; break;
                    case Kind.Script: script = true; break;
                }
            }
            if (!data && images.Count == 0 && !audio && !script) return;
            Reload(modId, data, images, audio, script, "changed on disk");
        }

        private static void Requeue(string modId, string file, int tries)
        {
            if (!_pending.TryGetValue(modId, out var files)) _pending[modId] = files = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            files[file] = tries;
            Interlocked.Exchange(ref _lastEventMs, _clock.ElapsedMilliseconds);
        }

        // ─────────────────────────────────────────────────────────────
        //  RELOADING
        // ─────────────────────────────────────────────────────────────

        /// <summary>Reloads everything of a mod from its files: data, images, audio and scripts.</summary>
        public static HotReloadEntry ReloadFromDisk(string modId)
        {
            string root = ModProjectLoader.ModFolder(modId);
            var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string sprites = Path.Combine(root, "sprites");
            if (Directory.Exists(sprites))
                foreach (var f in Directory.GetFiles(sprites))
                    if (Array.IndexOf(ImageExt, Path.GetExtension(f).ToLowerInvariant()) >= 0) images.Add(f);
            return Reload(modId, true, images, Directory.Exists(Path.Combine(root, "audio")), File.Exists(Path.Combine(root, "scripts.dll")) || ScriptLoader.SetOf(modId) != null, "reloaded from disk");
        }

        private static HotReloadEntry Reload(string modId, bool data, HashSet<string> images, bool audio, bool script, string why)
        {
            var entry = new HotReloadEntry { Time = DateTime.Now, ModId = modId };
            var parts = new List<string>();
            try
            {
                if (data) parts.Add(ApplyData(modId, entry));
                if (images.Count > 0)
                {
                    int made = 0;
                    foreach (var f in images)
                    {
                        if (File.Exists(f))
                        {
                            made += ModRegistry.ReloadImageFile(modId, f, out var err);
                            if (err != null) { entry.IsError = true; entry.Details.Add($"{Path.GetFileName(f)}: {err}"); }
                        }
                        else ModRegistry.RemoveImageSprites(f);
                    }
                    parts.Add($"{images.Count} image(s)");
                }
                if (audio)
                {
                    AudioUtils.ReloadMod(modId, ModProjectLoader.ModFolder(modId));
                    parts.Add("audio");
                }
                if (script)
                {
                    var set = ScriptLoader.ReloadMod(modId);
                    if (set == null) parts.Add("scripts removed");
                    else
                    {
                        parts.Add($"scripts ({set.Status.ToString().ToLowerInvariant()}: {set.Message})");
                        if (set.Status != ScriptStatus.Loaded) entry.IsError = true;
                    }
                }
            }
            catch (Exception ex)
            {
                entry.IsError = true;
                entry.Details.Add(ex.Message);
                Plugin.Log.LogError($"[HotReload] {modId}: {ex}");
            }

            parts.RemoveAll(string.IsNullOrEmpty);
            if (parts.Count == 0 && !entry.IsError && entry.Details.Count == 0) return entry;
            entry.Summary = entry.IsError && parts.Count == 0 ? "Reload failed" : $"{modId}: {string.Join(", ", parts)} ({why})";
            RefreshEditor();
            Add(entry);
            return entry;
        }

        /// <summary>Applies changed data files: to the open project in place, or by rebuilding a mod that is not open.</summary>
        private static string ApplyData(string modId, HotReloadEntry entry)
        {
            var active = ModManagerPanel.ActiveProject;
            return active != null && string.Equals(active.ModId, modId, StringComparison.OrdinalIgnoreCase)
                ? ApplyToActive(active, entry)
                : RebuildFromDisk(modId, entry);
        }

        private static string ApplyToActive(ModProject active, HotReloadEntry entry)
        {
            ModProject disk;
            List<string> problems;
            try
            {
                disk = ModProjectLoader.Load(active.ModId);
                problems = ModProjectLoader.LastLoadProblems.ToList();
            }
            catch (Exception ex)
            {
                entry.IsError = true;
                entry.Details.Add("Could not read the mod: " + ex.Message);
                return "";
            }

            foreach (var p in problems)
            {
                entry.IsError = true;
                entry.Details.Add("Kept the previous version, could not read " + p);
            }

            if (!LoadOrderManager.CanBuild(disk, out var whyNot))
            {
                entry.IsError = true;
                entry.Details.Add("Kept the current version: " + whyNot);
                return "nothing applied";
            }

            var diff = ModDiff.Compare(active, disk);
            if (diff.IsEmpty) return problems.Count > 0 ? "nothing applied" : "";

            var applied = ModDiff.ApplyExternal(active, diff, problems.Count == 0);
            if (applied.Reload.Any(d => d is Definitions.ZoneDef || d is Definitions.ZonePatchDef))
            {
                ZoneEditingService.CurrentZone = null;
                ZoneEditingService.CurrentPatch = null;
            }

            foreach (var e in applied.Errors) { entry.IsError = true; entry.Details.Add("Could not apply " + e); }
            var report = ModProjectBuilder.ReloadEntities(active, applied.Reload);
            foreach (var e in report.Errors) { entry.IsError = true; entry.Details.Add(e); }
            entry.Details.AddRange(applied.Added.Select(k => "+ " + k));
            entry.Details.AddRange(applied.Changed.Select(k => "~ " + k));
            entry.Details.AddRange(applied.Removed.Select(k => "\u2212 " + k + " (removed here, stays in the running game until a full rebuild)"));
            entry.Details.AddRange(applied.SkippedRemovals.Select(k => "? " + k + " (not read, left alone)"));
            if (applied.Meta.Count > 0) entry.Details.Add("Mod settings: " + string.Join(", ", applied.Meta));

            string dependents = RebuildDependents(active.ModId, entry);
            var bits = new List<string>();
            if (applied.Added.Count > 0) bits.Add($"{applied.Added.Count} added");
            if (applied.Changed.Count > 0) bits.Add($"{applied.Changed.Count} changed");
            if (applied.Removed.Count > 0) bits.Add($"{applied.Removed.Count} removed");
            if (applied.Meta.Count > 0) bits.Add("settings");
            return (bits.Count > 0 ? string.Join(", ", bits) : "no change") + dependents;
        }

        private static string RebuildFromDisk(string modId, HotReloadEntry entry)
        {
            if (!LoadOrderManager.IsEnabled(modId) || !LoadOrderManager.Order.Contains(modId, StringComparer.OrdinalIgnoreCase))
            {
                entry.Details.Add("This mod is not loaded in the game, so there is nothing to reload.");
                return "";
            }
            var disk = ModProjectLoader.Load(modId);
            foreach (var p in ModProjectLoader.LastLoadProblems)
            {
                entry.IsError = true;
                entry.Details.Add("Could not read " + p);
            }
            if (!LoadOrderManager.CanBuild(disk, out var why))
            {
                entry.IsError = true;
                entry.Details.Add($"Not rebuilt: {why}. The game keeps the version it started with.");
                return "";
            }
            ModProjectBuilder.Build(disk, false);
            string dependents = RebuildDependents(modId, entry);
            return "rebuilt" + dependents;
        }

        /// <summary>Mods that depend on this one patch or use its entities, so they are rebuilt afterwards to keep their changes on top.</summary>
        private static string RebuildDependents(string modId, HotReloadEntry entry)
        {
            var ids = DependentsOf(modId);
            if (ids.Count == 0) return "";
            var active = ModManagerPanel.ActiveProject;
            foreach (var id in ids)
            {
                try
                {
                    var proj = active != null && string.Equals(active.ModId, id, StringComparison.OrdinalIgnoreCase) ? active : ModProjectLoader.Load(id);
                    if (!LoadOrderManager.CanBuild(proj, out var why))
                    {
                        entry.IsError = true;
                        entry.Details.Add($"Did not rebuild {id}: {why}");
                        continue;
                    }
                    ModProjectBuilder.Build(proj, false);
                }
                catch (Exception ex)
                {
                    entry.IsError = true;
                    entry.Details.Add($"Could not rebuild {id}: {ex.Message}");
                }
            }
            entry.Details.Add("Also rebuilt: " + string.Join(", ", ids));
            return $", plus {ids.Count} dependent mod(s)";
        }

        private static List<string> DependentsOf(string modId)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { modId };
            var result = new List<string>();
            bool grew;
            do
            {
                grew = false;
                foreach (var id in LoadOrderManager.Order)
                {
                    if (found.Contains(id) || !LoadOrderManager.IsEnabled(id)) continue;
                    if (!(ReadDepends(id) ?? new List<string>()).Any(found.Contains)) continue;
                    found.Add(id);
                    result.Add(id);
                    grew = true;
                }
            } while (grew);
            return result.OrderBy(id => LoadOrderManager.Order.ToList().FindIndex(o => string.Equals(o, id, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        /// <summary>The mod's dependency ids, or null when its metadata exists but cannot be read.</summary>
        internal static List<string> ReadDepends(string modId)
        {
            try
            {
                string path = ModProjectLoader.MetadataPath(modId);
                if (!File.Exists(path)) return new List<string>();
                var jo = JObject.Parse(File.ReadAllText(path));
                return (jo["depends"] as JArray)?.Select(t => ((string)t)?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();
            }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────
        //  USED BY THE EDITOR
        // ─────────────────────────────────────────────────────────────

        /// <summary>Rebuilds entities the editor just changed in the running game. Cheap enough to call after every edit.</summary>
        public static ReloadReport Push(ModProject proj, params object[] defs) => Push(proj, (IEnumerable<object>)defs);

        public static ReloadReport Push(ModProject proj, IEnumerable<object> defs)
        {
            RenameFlush.FlushAll();
            var list = defs?.Where(d => d != null).ToList() ?? new List<object>();
            var report = ModProjectBuilder.ReloadEntities(proj, list);
            if (list.Count > 0) RefreshEditor();
            if (report.HasErrors)
                Add(new HotReloadEntry { Time = DateTime.Now, ModId = proj.ModId, IsError = true, Summary = $"{proj.ModId}: {report.Errors.Count} entit{(report.Errors.Count == 1 ? "y" : "ies")} could not be rebuilt", Details = report.Errors.ToList() });
            return report;
        }

        /// <summary>Rebuilds the whole mod from what is in the editor right now, then the mods that depend on it.</summary>
        public static HotReloadEntry PushMod(ModProject proj, bool withScripts)
        {
            RenameFlush.FlushAll();
            var entry = new HotReloadEntry { Time = DateTime.Now, ModId = proj.ModId };
            try
            {
                if (!LoadOrderManager.CanBuild(proj, out var why))
                {
                    entry.IsError = true;
                    entry.Summary = $"{proj.ModId} not rebuilt: {why}";
                    Add(entry);
                    return entry;
                }
                ModProjectBuilder.Build(proj, withScripts);
                string dependents = RebuildDependents(proj.ModId, entry);
                entry.Summary = $"{proj.ModId}: rebuilt from the editor{dependents}";
            }
            catch (Exception ex)
            {
                entry.IsError = true;
                entry.Summary = "Rebuild failed: " + ex.Message;
                Plugin.Log.LogError($"[HotReload] {ex}");
            }
            RefreshEditor();
            Add(entry);
            return entry;
        }

        private static void RefreshEditor()
        {
            InlineIssues.Invalidate();
            try { CardThumbnailCache.ClearAll(); } catch { }
            try { NpcThumbnailCache.ClearAll(); } catch { }
            try { BackgroundThumbnailCache.ClearAll(); } catch { }
        }

        private static void Add(HotReloadEntry entry)
        {
            History.Insert(0, entry);
            if (History.Count > HistoryMax) History.RemoveRange(HistoryMax, History.Count - HistoryMax);
            Plugin.Log.LogInfo($"[HotReload] {entry.Summary}");
            foreach (var d in entry.Details) Plugin.Log.LogInfo($"[HotReload]   {d}");
            try { EditorStatus.Toast(entry.Summary, entry.IsError); } catch { }
        }
    }
}
