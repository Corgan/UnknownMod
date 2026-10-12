using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Linq;
using UnknownMod.Core;

namespace UnknownMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInProcess("AcrossTheObelisk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        private readonly Harmony harmony = new(PluginInfo.PLUGIN_GUID);
        internal static ManualLogSource Log;
        internal static Plugin Instance { get; private set; }
        internal static BepInEx.Configuration.ConfigEntry<bool> ForceDeveloperMode;
        internal static BepInEx.Configuration.ConfigEntry<bool> EnableGameLogging;
        internal static BepInEx.Configuration.ConfigEntry<bool> UnhideUnreleasedContent;
        internal static BepInEx.Configuration.ConfigEntry<bool> AutoHotReload;
        internal static BepInEx.Configuration.ConfigEntry<string> ScriptSandbox;
        internal static BepInEx.Configuration.ConfigEntry<string> ScriptTrusted;
        private static readonly Action<UnknownMod.Scripting.ModScript> _scriptTick = s => s.OnUpdate();

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo($"{PluginInfo.PLUGIN_GUID} {PluginInfo.PLUGIN_VERSION} has loaded!");

            ForceDeveloperMode = Config.Bind("Debug", "ForceGameDeveloperMode", false,
                "Force the base game's developer mode on so its own debug logging is emitted. " +
                "Also enables the game's dev shortcuts/cheats and skips the Steam user id in save data.");
            EnableGameLogging = Config.Bind("Debug", "EnableGameLogging", true,
                "The game disables Unity logging in release builds (Globals: logEnabled = isDebugBuild), which hides " +
                "every Debug.Log and exception, including ones thrown inside coroutines. Re-enable it so they reach LogOutput.log.");
            UnhideUnreleasedContent = Config.Bind("Content", "UnhideUnreleasedContent", false,
                "Show content the game ships but hides from players (ReleaseContent): the Beastmaster class, the Savannah, " +
                "Shaman Cave, Zambora Plains, Frog Fight, Boss3 and BossTests zones, and their NPCs and cards. " +
                "Unfinished content may be broken. Requires a game restart.");
            AutoHotReload = Config.Bind("Editor", "AutoHotReload", true,
                "Watch the mod data folder and apply edits made outside the game (a text editor, an image program) to the running game. " +
                "Entities rebuild in place, images and audio reload, and scripts.dll reloads. Entities deleted on disk stay until the next full rebuild.");
            ScriptSandbox = Config.Bind("Scripts", "Sandbox", "Block",
                new BepInEx.Configuration.ConfigDescription(
                    "How to treat a mod scripts.dll that uses dangerous APIs (starting programs, network, native code, runtime code generation). " +
                    "Block = refuse to load it, Warn = load it and log what it uses, Off = no check. This is a scan for mistakes and careless code, not a security boundary; only install mods you trust.",
                    new BepInEx.Configuration.AcceptableValueList<string>("Block", "Warn", "Off")));
            ScriptTrusted = Config.Bind("Scripts", "TrustedMods", "",
                "Comma-separated mod ids whose scripts.dll is loaded even if the sandbox would block it.");
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (_, scene) =>
                ScriptHost.Dispatch(ScriptHook.Scene, s => s.OnSceneChanged(scene.name));
            Patches.HookSceneTrace();

            ApplyPatches();
            try { Patches.InstallBootFinalizers(harmony); }
            catch (Exception ex) { Log.LogWarning($"[Diagnostics] Boot finalizers not installed: {ex.Message}"); }

            // Register entity types for the override browser
            EntityTypeRegistry.Initialize();

            // Start runtime inspector (file-based IPC for external tooling)
            try
            {
                Core.RuntimeInspector.Init();
            }
            catch (Exception ex)
            {
                Log.LogWarning($"RuntimeInspector failed to start: {ex.Message}");
            }

            // In-game sprite stack inspector (toggle with F9)
            gameObject.AddComponent<Core.SpriteStackInspector>();

            Core.HotReload.Start();

            try { Core.ModSchema.WriteAll(Core.ModProjectLoader.DataRoot); }
            catch (Exception ex) { Log.LogWarning($"Editor schemas not written: {ex.Message}"); }
        }

        /// <summary>Patch class-by-class so one broken target cannot stop every other patch from applying.</summary>
        private void ApplyPatches()
        {
            int failed = 0, total = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try
                {
                    var processor = harmony.CreateClassProcessor(type);
                    if (!type.IsDefined(typeof(HarmonyPatch), false) &&
                        !type.GetMethods(AccessTools.all).Any(m => m.IsDefined(typeof(HarmonyPatch), false)))
                        continue;
                    total++;
                    processor.Patch();
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.LogError($"Patch class {type.FullName} failed - game update may have changed a patched method: {ex}");
                }
            }

            if (failed == 0)
                Log.LogInfo($"All patches applied successfully ({total} patch class(es), {harmony.GetPatchedMethods().Count()} patched method(s)).");
            else
                Log.LogError($"{failed}/{total} patch class(es) FAILED - UnknownMod is running in a degraded state.");
        }

        private void Update()
        {
            Core.RuntimeInspector.Poll();
            Core.HotReload.Poll();
            Core.RuleEngine.Tick();
            if (ScriptHost.Wants(ScriptHook.Update)) ScriptHost.Dispatch(ScriptHook.Update, _scriptTick);
            Patches.Heartbeat();
            if (EnableGameLogging.Value && !UnityEngine.Debug.unityLogger.logEnabled)
                UnityEngine.Debug.unityLogger.logEnabled = true;
        }
    }
}
