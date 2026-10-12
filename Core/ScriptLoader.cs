using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnknownMod.Scripting;

namespace UnknownMod.Core
{
    public enum ScriptStatus { Loaded, Blocked, Failed, Unloaded }

    /// <summary>Everything one mod's scripts.dll contributed, and what happened when it loaded.</summary>
    public sealed class ScriptSet
    {
        public string ModId;
        public string Path;
        public string Hash = "";
        public ScriptStatus Status = ScriptStatus.Unloaded;
        public string Message = "";
        public DateTime LoadedAt;
        public int ApiVersion;
        public List<SandboxFinding> Findings = new();
        public List<ScriptHandlerInfo> Handlers = new();
        public Assembly Assembly;
    }

    /// <summary>
    /// Loads scripts.dll from mod folders and registers what it finds: ModScript subclasses plus the aura, trait and boss
    /// interfaces. Handlers are tracked per mod, so one mod's scripts can be unloaded and reloaded without touching the rest.
    /// </summary>
    public static class ScriptLoader
    {
        private static readonly List<ScriptSet> _sets = new();
        private static readonly HashSet<string> _assemblyNames = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, IBossController> _bossControllers = new();
        private static readonly Dictionary<string, List<IAuraBehaviour>> _auraBehaviours = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<ITraitBehaviour>> _traitBehaviours = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every mod that has a scripts.dll, in the order they were first loaded.</summary>
        public static IReadOnlyList<ScriptSet> Sets => _sets;

        public static ScriptSet SetOf(string modId) => _sets.FirstOrDefault(s => string.Equals(s.ModId, modId, StringComparison.OrdinalIgnoreCase));

        // ═══════════════════════════════════════════════════════════════
        //  SANDBOX SETTINGS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Block (refuse scripts that use dangerous APIs), Warn (load and log) or Off.</summary>
        public static string SandboxMode => Plugin.ScriptSandbox?.Value ?? "Block";

        private static readonly char[] ListSeparators = { ',', ';' };

        public static bool IsTrusted(string modId)
            => (Plugin.ScriptTrusted?.Value ?? "").Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Any(t => string.Equals(t.Trim(), modId, StringComparison.OrdinalIgnoreCase));

        public static void SetTrusted(string modId, bool trusted)
        {
            var list = (Plugin.ScriptTrusted.Value ?? "").Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => !string.Equals(t, modId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (trusted) list.Add(modId);
            Plugin.ScriptTrusted.Value = string.Join(",", list);
        }

        // ═══════════════════════════════════════════════════════════════
        //  LOADING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Load scripts.dll from a mod folder (if it exists), replacing anything this mod loaded before.
        /// </summary>
        public static ScriptSet LoadModScripts(string modId)
        {
            string dllPath = Path.Combine(ModProjectLoader.ModFolder(modId), "scripts.dll");
            int index = _sets.FindIndex(s => string.Equals(s.ModId, modId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Unload(_sets[index]);

            if (!File.Exists(dllPath))
            {
                if (index >= 0) _sets.RemoveAt(index);
                Rebuild();
                return null;
            }

            var set = new ScriptSet { ModId = modId, Path = dllPath };
            if (index >= 0) _sets[index] = set; else _sets.Add(set);
            Plugin.Log.LogInfo($"[ScriptLoader] {modId}: reading {dllPath}");

            try
            {
                // Load from bytes so the file is not locked and can be rebuilt while the game runs
                byte[] bytes = File.ReadAllBytes(dllPath);
                using (var sha = SHA256.Create())
                    set.Hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 12).ToLowerInvariant();

                string mode = SandboxMode;
                Plugin.Log.LogInfo($"[ScriptLoader] {modId}: analysing {bytes.Length} bytes (sandbox {mode})");
                var report = ScriptSandbox.Analyze(bytes, _assemblyNames);
                Plugin.Log.LogInfo($"[ScriptLoader] {modId}: analysis done, {report.Findings?.Count ?? 0} finding(s)");
                if (report.Error != null) { Fail(set, ScriptStatus.Failed, report.Error); return set; }

                set.ApiVersion = report.ApiVersion;
                set.Findings = report.Findings;
                if (report.ApiVersion > ScriptApiAttribute.Current)
                {
                    Fail(set, ScriptStatus.Failed, $"Written for script API {report.ApiVersion}, but this UnknownMod provides {ScriptApiAttribute.Current}. Update UnknownMod.");
                    return set;
                }

                if (!mode.Equals("Off", StringComparison.OrdinalIgnoreCase) && report.HasDanger)
                {
                    int danger = report.Findings.Count(f => f.Level == FindingLevel.Danger);
                    if (mode.Equals("Block", StringComparison.OrdinalIgnoreCase) && !IsTrusted(modId))
                    {
                        Fail(set, ScriptStatus.Blocked, $"Not loaded: it uses {danger} API(s) the sandbox blocks. Review them in Extras > Scripts, then trust the mod if you accept them.");
                        return set;
                    }
                    Plugin.Log.LogWarning($"[ScriptLoader] '{modId}' scripts.dll uses {danger} potentially dangerous API(s); loading anyway ({(IsTrusted(modId) ? "trusted" : "sandbox is " + mode)}).");
                }

                Plugin.Log.LogInfo($"[ScriptLoader] {modId}: Assembly.Load");
                var asm = Assembly.Load(report.Bytes);
                set.Assembly = asm;
                Plugin.Log.LogInfo($"[ScriptLoader] {modId}: scanning '{asm.GetName().Name}'");
                ScanAssembly(set, asm);
                set.Status = ScriptStatus.Loaded;
                set.LoadedAt = DateTime.Now;
                set.Message = $"{set.Handlers.Count} handler(s)";
                Plugin.Log.LogInfo($"[ScriptLoader] Loaded scripts.dll for mod '{modId}' ({set.Hash}): {set.Message}");
            }
            catch (Exception ex)
            {
                Fail(set, ScriptStatus.Failed, ex.Message);
                Plugin.Log.LogError($"[ScriptLoader] Failed to load scripts.dll for '{modId}': {ex}");
            }

            Rebuild();
            return set;
        }

        private static void Fail(ScriptSet set, ScriptStatus status, string message)
        {
            set.Status = status;
            set.Message = message;
            set.LoadedAt = DateTime.Now;
            Plugin.Log.LogWarning($"[ScriptLoader] '{set.ModId}': {message}");
            Rebuild();
        }

        /// <summary>Scan an assembly for all known script types and create one instance of each.</summary>
        private static void ScanAssembly(ScriptSet set, Assembly asm)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
                Plugin.Log.LogWarning($"[ScriptLoader] Some types in '{set.ModId}' scripts.dll failed to load");
            }

            var scripts = new List<ScriptHandlerInfo>();
            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: type {type.FullName}");

                if (typeof(ModScript).IsAssignableFrom(type))
                {
                    var info = Create(set, type, "ModScript");
                    if (info != null)
                    {
                        info.Hooks = ScriptHost.HooksOf(type);
                        ((ModScript)info.Instance).Context = new ScriptContext(set.ModId);
                        scripts.Add(info);
                    }
                }

                if (typeof(IBossController).IsAssignableFrom(type))
                {
                    var info = Create(set, type, "IBossController");
                    if (info != null)
                    {
                        Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: casting to IBossController");
                        var ctl = info.Instance as IBossController;
                        Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: cast ok ({ctl != null}), reading NpcIdPrefix directly");
                        Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: direct NpcIdPrefix = {ctl.NpcIdPrefix}");
                        string prefix = ScriptHost.Call(info.Instance, "NpcIdPrefix", () => ((IBossController)info.Instance).NpcIdPrefix);
                        Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: NpcIdPrefix = {prefix}");
                        if (string.IsNullOrEmpty(prefix)) Drop(set, info, "has an empty NpcIdPrefix");
                        else info.Target = prefix;
                    }
                }

                if (typeof(IAuraBehaviour).IsAssignableFrom(type))
                {
                    var info = Create(set, type, "IAuraBehaviour");
                    if (info != null)
                    {
                        string id = ScriptHost.Call(info.Instance, "AuraId", () => ((IAuraBehaviour)info.Instance).AuraId);
                        if (string.IsNullOrWhiteSpace(id)) Drop(set, info, "has an empty AuraId"); else info.Target = id;
                    }
                }

                if (typeof(ITraitBehaviour).IsAssignableFrom(type))
                {
                    var info = Create(set, type, "ITraitBehaviour");
                    if (info != null)
                    {
                        string id = ScriptHost.Call(info.Instance, "TraitId", () => ((ITraitBehaviour)info.Instance).TraitId);
                        if (string.IsNullOrWhiteSpace(id)) Drop(set, info, "has an empty TraitId"); else info.Target = id;
                    }
                }
            }

            Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: refreshing host");
            ScriptHost.Refresh();
            foreach (var info in scripts.Where(i => set.Handlers.Contains(i)))
                ScriptHost.Call(info.Instance, "OnLoad", () => ((ModScript)info.Instance).OnLoad());
        }

        private static ScriptHandlerInfo Create(ScriptSet set, Type type, string kind)
        {
            try
            {
                Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: creating {kind} {type.Name}");
                var instance = Activator.CreateInstance(type);
                Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: created {type.Name}");
                var info = new ScriptHandlerInfo { Instance = instance, ModId = set.ModId, TypeName = type.Name, Kind = kind };
                set.Handlers.Add(info);
                ScriptHost.Register(info);
                Plugin.Log.LogInfo($"[ScriptLoader] {set.ModId}: registered {type.Name}");
                return info;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Plugin.Log.LogError($"[ScriptLoader] '{set.ModId}': could not create {kind} '{type.FullName}': {inner.Message}");
                return null;
            }
        }

        private static void Drop(ScriptSet set, ScriptHandlerInfo info, string why)
        {
            Plugin.Log.LogWarning($"[ScriptLoader] '{set.ModId}': {info.TypeName} {why} - skipped");
            set.Handlers.Remove(info);
            ScriptHost.Unregister(info);
        }

        // ═══════════════════════════════════════════════════════════════
        //  UNLOADING / RELOADING
        // ═══════════════════════════════════════════════════════════════

        private static void Unload(ScriptSet set)
        {
            foreach (var info in set.Handlers.ToList())
            {
                if (info.Instance is ModScript script)
                    ScriptHost.Call(info.Instance, "OnUnload", () => script.OnUnload());
                ScriptHost.Unregister(info);
            }
            set.Handlers.Clear();
            set.Status = ScriptStatus.Unloaded;
            ScriptHost.Refresh();
        }

        /// <summary>Unload and load again just this mod's scripts.dll.</summary>
        public static ScriptSet ReloadMod(string modId) => LoadModScripts(modId);

        /// <summary>Drop every handler and load each mod's scripts.dll again, in the order they first loaded.</summary>
        public static int ReloadAll()
        {
            var mods = _sets.Select(s => s.ModId).ToList();
            Clear();
            foreach (var id in mods) LoadModScripts(id);
            return mods.Count;
        }

        /// <summary>Unload everything (e.g. before a full rebuild).</summary>
        public static void Clear()
        {
            foreach (var set in _sets) Unload(set);
            _sets.Clear();
            Rebuild();
        }

        /// <summary>Rebuilds the id lookups from the loaded sets. Later mods in load order win a clash, as before.</summary>
        private static void Rebuild()
        {
            _bossControllers.Clear();
            _auraBehaviours.Clear();
            _traitBehaviours.Clear();
            foreach (var set in _sets)
            {
                foreach (var info in set.Handlers)
                {
                    switch (info.Instance)
                    {
                        case IBossController boss when info.Target != null:
                            if (_bossControllers.TryGetValue(info.Target, out var prev))
                                Plugin.Log.LogWarning($"[ScriptLoader] Prefix '{info.Target}' already handled by {prev.GetType().FullName}; {info.TypeName} (mod '{set.ModId}') replaces it");
                            _bossControllers[info.Target] = boss;
                            break;
                        case IAuraBehaviour aura when info.Target != null:
                            Add(_auraBehaviours, info.Target, aura);
                            break;
                        case ITraitBehaviour trait when info.Target != null:
                            Add(_traitBehaviours, info.Target, trait);
                            break;
                    }
                }
            }
            ScriptHost.Refresh();
        }

        private static void Add<T>(Dictionary<string, List<T>> into, string id, T handler)
        {
            if (!into.TryGetValue(id, out var list)) into[id] = list = new List<T>();
            list.Add(handler);
        }

        // ═══════════════════════════════════════════════════════════════
        //  QUERIES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Try to find a boss controller whose NpcIdPrefix matches the given NPC ID.
        /// Returns null if no mod provides a controller for this NPC.
        /// </summary>
        public static IBossController FindBossController(string npcId)
        {
            IBossController best = null;
            if (string.IsNullOrEmpty(npcId)) return null;
            int bestLen = -1;
            foreach (var kvp in _bossControllers)
            {
                if (npcId.StartsWith(kvp.Key, StringComparison.Ordinal) && kvp.Key.Length > bestLen && ScriptHost.Info(kvp.Value)?.Disabled != true)
                {
                    best = kvp.Value;
                    bestLen = kvp.Key.Length;
                }
            }
            return best;
        }

        public static IReadOnlyList<IAuraBehaviour> FindAuraBehaviours(string auraId)
            => !string.IsNullOrEmpty(auraId) && _auraBehaviours.TryGetValue(auraId, out var l) ? l : null;

        public static IReadOnlyList<ITraitBehaviour> FindTraitBehaviours(string traitId)
            => !string.IsNullOrEmpty(traitId) && _traitBehaviours.TryGetValue(traitId, out var l) ? l : null;

        public static bool HasAuraBehaviours => _auraBehaviours.Count > 0;
    }
}
