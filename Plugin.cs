using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;

namespace UnknownMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInProcess("AcrossTheObelisk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        private readonly Harmony harmony = new(PluginInfo.PLUGIN_GUID);
        internal static ManualLogSource Log;

        private void Awake()
        {
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
        }
    }
}
