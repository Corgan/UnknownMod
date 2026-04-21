using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
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

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo($"{PluginInfo.PLUGIN_GUID} {PluginInfo.PLUGIN_VERSION} has loaded!");

            try
            {
                harmony.PatchAll();
                Log.LogInfo("All patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.LogError($"Patch error: {ex.Message}");
                Log.LogError(ex.StackTrace);
            }

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
        }

        private void Update()
        {
            Core.RuntimeInspector.Poll();
        }
    }
}
