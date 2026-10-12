using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnknownMod
{
    /// <summary>
    /// Startup diagnostics: timestamped milestones through the game's boot sequence, and an
    /// opt-in switch that turns on the base game's developer-mode logging.
    /// </summary>
    public static partial class Patches
    {
        internal static void Trace(string msg) =>
            Plugin.Log.LogInfo($"[Trace +{Time.realtimeSinceStartup:F2}s] {msg}");

        internal static void HookSceneTrace()
        {
            SceneManager.sceneLoaded += (s, mode) => Trace($"scene loaded: '{s.name}' ({mode})");
            SceneManager.sceneUnloaded += s => Trace($"scene unloaded: '{s.name}'");
        }

        // Developer mode is normally gated on hard-coded Steam IDs. Forcing it enables the
        // game's own Debug.Log output (and dev shortcuts), so it is opt-in via the config.
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.GetDeveloperMode))]
        [HarmonyPostfix]
        public static void GetDeveloperMode_Postfix(ref bool __result)
        {
            if (Plugin.ForceDeveloperMode != null && Plugin.ForceDeveloperMode.Value)
                __result = true;
        }

        [HarmonyPatch(typeof(GameManager), "GenerateHeroes")]
        [HarmonyPrefix]
        public static void GenerateHeroes_Pre() => Trace("GameManager.GenerateHeroes begin");

        [HarmonyPatch(typeof(GameManager), "GenerateHeroes")]
        [HarmonyPostfix]
        public static void GenerateHeroes_Post() => Trace("GameManager.GenerateHeroes done");

        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.DoSteam))]
        [HarmonyPrefix]
        public static void DoSteam_Pre() => Trace("SteamManager.DoSteam");

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadPlayerData))]
        [HarmonyPrefix]
        public static void LoadPlayerData_Pre() => Trace("GameManager.LoadPlayerData begin");

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.UnlockInitialHeroes))]
        [HarmonyPostfix]
        public static void UnlockInitialHeroes_Post() => Trace("PlayerManager.UnlockInitialHeroes done (boot sequence complete)");

        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.DoSteam))]
        [HarmonyPostfix]
        public static void DoSteam_Post(SteamManager __instance) =>
            Trace($"SteamManager.DoSteam returned (steamLoaded={__instance.steamLoaded}, steamConnected={__instance.steamConnected}, steamId={__instance.steamId})");

        [HarmonyPatch(typeof(AlertManager), nameof(AlertManager.AlertConfirm))]
        [HarmonyPrefix]
        public static void AlertConfirm_Pre(string text) => Trace($"AlertManager.AlertConfirm: \"{text}\"");

        [HarmonyPatch(typeof(TomeManager), nameof(TomeManager.InitTome))]
        [HarmonyPrefix]
        public static void InitTome_Pre() => Trace("TomeManager.InitTome");

        [HarmonyPatch(typeof(PerkTree), nameof(PerkTree.InitPerkTree))]
        [HarmonyPrefix]
        public static void InitPerkTree_Pre() => Trace("PerkTree.InitPerkTree");

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.PreBeginGame))]
        [HarmonyPrefix]
        public static void PreBeginGame_Pre() => Trace("PlayerManager.PreBeginGame");

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.BeginGame))]
        [HarmonyPrefix]
        public static void PlayerBeginGame_Pre() => Trace("PlayerManager.BeginGame");

        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadPlayerData))]
        [HarmonyPostfix]
        public static void SaveLoadPlayerData_Post(object __result) => Trace($"SaveManager.LoadPlayerData -> {(__result == null ? "null" : "ok")}");

        private static float _nextBeat;
        internal static void Heartbeat()
        {
            // Only useful for diagnosing a boot hang, so it stops after the first few minutes.
            if (Time.realtimeSinceStartup < _nextBeat || Time.realtimeSinceStartup > 300f) return;
            _nextBeat = Time.realtimeSinceStartup + 3f;
            Trace("heartbeat (main thread alive)");
        }

        // Coroutine exceptions vanish silently when Unity logging is off, so catch them at the source.
        internal static void InstallBootFinalizers(Harmony harmony)
        {
            var finalizer = new HarmonyMethod(typeof(Patches), nameof(BootFinalizer));
            foreach (var nested in typeof(GameManager).GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
            {
                if (!nested.Name.Contains("InitRoutine") && !nested.Name.Contains("WaitForSteam")) continue;
                var move = AccessTools.Method(nested, "MoveNext");
                if (move == null) continue;
                try { harmony.Patch(move, finalizer: finalizer); }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"[Diagnostics] Could not hook {nested.Name}: {ex.Message}"); }
            }
            foreach (var name in new[] { "PreBeginGame", "BeginGame", "UnlockInitialHeroes" })
            {
                var m = AccessTools.Method(typeof(PlayerManager), name);
                if (m != null) harmony.Patch(m, finalizer: finalizer);
            }
        }

        public static System.Exception BootFinalizer(System.Exception __exception, System.Reflection.MethodBase __originalMethod)
        {
            if (__exception != null)
                Plugin.Log.LogError($"[Boot] Exception in {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}: {__exception}");
            return __exception;
        }
    }
}