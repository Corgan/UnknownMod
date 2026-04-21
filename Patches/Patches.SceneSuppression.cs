using HarmonyLib;
using Photon.Pun;
using UnityEngine.SceneManagement;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>
    /// Scene suppression guards — prevent MonoBehaviours from initializing
    /// during additive scene loads used for data extraction.
    /// </summary>
    public static partial class Patches
    {
        // ── Suppress PhotonView registration during additive scene load ──
        // PhotonView.Awake() fires before SceneManager.sceneLoaded, so we can't
        // deactivate roots in time. Suppress to prevent duplicate ViewID errors.

        [HarmonyPatch(typeof(PhotonView), "Awake")]
        [HarmonyPrefix]
        public static bool PhotonView_Awake_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
                return false;
            return true;
        }

        // ── Suppress MapManager initialization during additive scene load ──

        [HarmonyPatch(typeof(MapManager), "Awake")]
        [HarmonyPrefix]
        public static bool MapManager_Awake_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed MapManager.Awake during zone data extraction.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(MapManager), "Start")]
        [HarmonyPrefix]
        public static bool MapManager_Start_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed MapManager.Start during zone data extraction.");
                return false;
            }
            return true;
        }

        // ── Suppress MatchManager initialization during additive scene load ──

        [HarmonyPatch(typeof(MatchManager), "Awake")]
        [HarmonyPrefix]
        public static bool MatchManager_Awake_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed MatchManager.Awake during combat data extraction.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(MatchManager), "Start")]
        [HarmonyPrefix]
        public static bool MatchManager_Start_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed MatchManager.Start during combat data extraction.");
                return false;
            }
            return true;
        }

        // ── Suppress other Combat scene MonoBehaviours during additive load ──

        [HarmonyPatch(typeof(SideCharacters), "Start")]
        [HarmonyPrefix]
        public static bool SideCharacters_Start_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
                return false;
            return true;
        }

        [HarmonyPatch(typeof(SideCharacters), "Awake")]
        [HarmonyPrefix]
        public static bool SideCharacters_Awake_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
                return false;
            return true;
        }

        // ── Suppress EventManager initialization during additive scene load ──

        [HarmonyPatch(typeof(EventManager), "Awake")]
        [HarmonyPrefix]
        public static bool EventManager_Awake_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed EventManager.Awake during event data extraction.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(GameManager), "ChangeScene")]
        [HarmonyPrefix]
        public static bool GameManager_ChangeScene_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
                return false;
            return true;
        }

        // ── Nuclear guard: block ALL direct SceneManager.LoadScene calls during suppression ──
        // Components in the Combat scene may bypass SceneStatic/GameManager and call
        // SceneManager.LoadScene directly, which would unload the active scene (blank screen).
        // We allow our own additive load (LoadSceneMode.Additive) but block everything else.

        [HarmonyPatch(typeof(SceneManager), "LoadScene", typeof(string))]
        [HarmonyPrefix]
        public static bool SceneManager_LoadSceneStr_Prefix(string sceneName)
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
            {
                Plugin.Log.LogInfo($"[Patches] Blocked LoadScene('{sceneName}') during suppression.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(SceneManager), "LoadScene", typeof(string), typeof(LoadSceneMode))]
        [HarmonyPrefix]
        public static bool SceneManager_LoadSceneMode_Prefix(string sceneName, LoadSceneMode mode)
        {
            if (ZoneEditingService.SuppressSceneLoad > 0 && mode != LoadSceneMode.Additive)
            {
                Plugin.Log.LogInfo($"[Patches] Blocked non-additive LoadScene('{sceneName}') during suppression.");
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(SceneManager), "LoadScene", typeof(int))]
        [HarmonyPrefix]
        public static bool SceneManager_LoadSceneInt_Prefix(int sceneBuildIndex)
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
            {
                Plugin.Log.LogInfo($"[Patches] Blocked LoadScene(index={sceneBuildIndex}) during suppression.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// When the map scene loads normally, cache all base-game zone node positions
        /// and road waypoints so the editor can synthesize ZoneDefs for zone patches
        /// even after leaving the map scene.
        /// </summary>
        [HarmonyPatch(typeof(MapManager), "Start")]
        [HarmonyPostfix]
        public static void MapManager_Start_Postfix()
        {
            if (ZoneEditingService.SuppressSceneLoad == 0)
                ZoneEditingService.CacheAllBaseZones();
        }

        /// <summary>
        /// Prevent any scene redirects from firing during our additive scene load.
        /// Without this, other MonoBehaviours in the Map scene could trigger
        /// SceneStatic.LoadByName and cause unwanted scene switches.
        /// </summary>
        [HarmonyPatch(typeof(SceneStatic), "LoadByName")]
        [HarmonyPrefix]
        public static bool SceneStatic_LoadByName_Prefix()
        {
            if (ZoneEditingService.SuppressSceneLoad > 0)
            {
                Plugin.Log.LogInfo("[Patches] Suppressed SceneStatic.LoadByName during zone data extraction.");
                return false;
            }
            return true;
        }
    }
}
