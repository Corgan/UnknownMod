using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UnknownMod.Core
{
    /// <summary>
    /// Access to the game's lazily loaded Maps/Backgrounds/Effects prefabs. Since the ResourceCatalog
    /// change the game no longer keeps these in serialized lists (MapManager.mapList,
    /// MatchBackground.backgroundPrefabs, Globals.vfxAux), so they are resolved by name through Globals.
    /// </summary>
    internal static class GameResources
    {
        public static GameObject GetEffectPrefab(string name) =>
            string.IsNullOrEmpty(name) ? null : Globals.Instance?.GetEffectPrefab(name);

        public static GameObject GetMapPrefab(string name) =>
            string.IsNullOrEmpty(name) ? null : Globals.Instance?.GetMapPrefab(name);

        public static GameObject GetBackgroundPrefab(string name) =>
            string.IsNullOrEmpty(name) ? null : Globals.Instance?.GetBackgroundPrefab(name);

        public static List<GameObject> GetAllMapPrefabs() =>
            new List<GameObject>(EnumerateMapPrefabs());

        public static List<GameObject> GetAllBackgroundPrefabs() =>
            new List<GameObject>(EnumerateBackgroundPrefabs());

        /// <summary>Lazily yields each map prefab so callers can stop at the first match.</summary>
        public static IEnumerable<GameObject> EnumerateMapPrefabs() =>
            Enumerate("MapNames", "Maps", GetMapPrefab);

        public static IEnumerable<GameObject> EnumerateBackgroundPrefabs() =>
            Enumerate("BackgroundNames", "Backgrounds", GetBackgroundPrefab);

        private static IEnumerable<GameObject> Enumerate(string catalogProperty, string resourceFolder, Func<string, GameObject> byName)
        {
            string[] names = null;
            try
            {
                var catalog = Globals.Instance != null ? Traverse.Create(Globals.Instance).Field("resourceCatalog").GetValue<ResourceCatalog>() : null;
                if (catalog != null)
                    names = Traverse.Create(catalog).Property(catalogProperty).GetValue<string[]>();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[GameResources] Could not read ResourceCatalog.{catalogProperty}: {ex.Message}");
            }

            if (names != null && names.Length > 0)
            {
                foreach (var name in names)
                {
                    var prefab = byName(name);
                    if (prefab != null) yield return prefab;
                }
                yield break;
            }

            // Catalog not loaded yet (Globals content still being created): fall back to the Resources folder.
            foreach (var prefab in Resources.LoadAll<GameObject>(resourceFolder))
                yield return prefab;
        }
    }
}
