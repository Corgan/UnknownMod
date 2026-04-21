using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnknownMod.Scripting;

namespace UnknownMod.Core
{
    /// <summary>
    /// Loads optional scripts.dll from mod folders and discovers
    /// classes implementing well-known interfaces (IBossController, etc.).
    /// </summary>
    public static class ScriptLoader
    {
        // ── Boss controllers keyed by NPC ID prefix ──────────────
        private static readonly Dictionary<string, IBossController> _bossControllers = new();
        private static readonly List<Assembly> _loadedAssemblies = new();

        /// <summary>
        /// Load scripts.dll from a mod folder (if it exists) and register
        /// any discovered interface implementations.
        /// </summary>
        public static void LoadModScripts(string modId)
        {
            string modFolder = ModProjectLoader.ModFolder(modId);
            string dllPath = Path.Combine(modFolder, "scripts.dll");

            if (!File.Exists(dllPath))
                return;

            try
            {
                // Load into the current AppDomain
                var asm = Assembly.LoadFrom(dllPath);
                _loadedAssemblies.Add(asm);
                Plugin.Log.LogInfo($"[ScriptLoader] Loaded scripts.dll for mod '{modId}'");

                ScanAssembly(asm, modId);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ScriptLoader] Failed to load scripts.dll for '{modId}': {ex.Message}");
            }
        }

        /// <summary>Scan an assembly for all known script interfaces.</summary>
        private static void ScanAssembly(Assembly asm, string modId)
        {
            int found = 0;

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
                Plugin.Log.LogWarning($"[ScriptLoader] Some types in '{modId}' scripts.dll failed to load");
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                // ── IBossController ──────────────────────────────
                if (typeof(IBossController).IsAssignableFrom(type))
                {
                    try
                    {
                        var instance = (IBossController)Activator.CreateInstance(type);
                        string prefix = instance.NpcIdPrefix;

                        if (string.IsNullOrEmpty(prefix))
                        {
                            Plugin.Log.LogWarning($"[ScriptLoader] {type.FullName} has empty NpcIdPrefix — skipped");
                            continue;
                        }

                        _bossControllers[prefix] = instance;
                        found++;
                        Plugin.Log.LogInfo($"[ScriptLoader] Registered IBossController '{type.Name}' for prefix '{prefix}'");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[ScriptLoader] Failed to instantiate IBossController '{type.FullName}': {ex.Message}");
                    }
                }

                // Future: ICardEffect, IAuraTickHandler, IEventScript, etc.
            }

            Plugin.Log.LogInfo($"[ScriptLoader] Mod '{modId}': {found} script handler(s) registered");
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
            int bestLen = -1;
            foreach (var kvp in _bossControllers)
            {
                if (npcId.StartsWith(kvp.Key) && kvp.Key.Length > bestLen)
                {
                    best = kvp.Value;
                    bestLen = kvp.Key.Length;
                }
            }
            return best;
        }

        /// <summary>Clear all registered script handlers (e.g. on reload).</summary>
        public static void Clear()
        {
            _bossControllers.Clear();
            _loadedAssemblies.Clear();
        }
    }
}
