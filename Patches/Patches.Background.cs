using System;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>
    /// Patches MatchManager.DoBackground() to support custom mod-defined backgrounds.
    /// When a CombatData has a custom background ID registered in DataHelper.CombatCustomBackgrounds,
    /// it overrides the enum-based background lookup with the custom prefab name.
    /// </summary>
    public static partial class Patches
    {
        [HarmonyPatch(typeof(MatchManager), "DoBackground")]
        [HarmonyPrefix]
        public static void DoBackground_Prefix(MatchManager __instance)
        {
            var trav = Traverse.Create(__instance);
            var cd = trav.Field<CombatData>("combatData").Value;
            if (cd == null) return;

            if (!DataHelper.CombatCustomBackgrounds.TryGetValue(cd.CombatId, out var customBgId))
                return;
            if (string.IsNullOrEmpty(customBgId)) return;

            Plugin.Log.LogInfo($"[BG Prefix] Found custom bg '{customBgId}' for combat '{cd.CombatId}'");

            // Clean up all existing background children to prevent stacking
            // on rollback/reload. The vanilla code only checks for duplicates
            // by name but never destroys old backgrounds, so a reload would
            // stack the new vanilla BG on top of the old custom BG.
            var bgTransform = __instance.backgroundTransform;
            if (bgTransform != null)
            {
                for (int i = bgTransform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(bgTransform.GetChild(i).gameObject);
                // Clear the stale reference so vanilla code doesn't skip instantiation
                trav.Field<GameObject>("backgroundPrefab").Value = null;
            }

            // Ensure the custom prefab is in backgroundPrefabs
            if (DataHelper.CustomBackgroundPrefabs.TryGetValue(customBgId, out var prefab) && prefab != null)
            {
                bool found = false;
                for (int i = 0; i < __instance.backgroundPrefabs.Count; i++)
                {
                    if (__instance.backgroundPrefabs[i] != null &&
                        string.Equals(__instance.backgroundPrefabs[i].name, customBgId, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    __instance.backgroundPrefabs.Add(prefab);
                    Plugin.Log.LogInfo($"[BG Prefix] Injected prefab '{customBgId}' into backgroundPrefabs (now {__instance.backgroundPrefabs.Count})");
                }
                else
                {
                    Plugin.Log.LogInfo($"[BG Prefix] Prefab '{customBgId}' already in backgroundPrefabs");
                }
            }
            else
            {
                Plugin.Log.LogWarning($"[BG Prefix] Custom prefab '{customBgId}' not found in CustomBackgroundPrefabs!");
            }
        }

        /// <summary>
        /// After the vanilla DoBackground sets backgroundActive from the enum,
        /// override it with our custom background ID and re-run the instantiation
        /// if the vanilla logic didn't find a match.
        /// </summary>
        [HarmonyPatch(typeof(MatchManager), "DoBackground")]
        [HarmonyPostfix]
        public static void DoBackground_Postfix(MatchManager __instance)
        {
            var trav = Traverse.Create(__instance);
            var cd = trav.Field<CombatData>("combatData").Value;
            if (cd == null) return;

            if (!DataHelper.CombatCustomBackgrounds.TryGetValue(cd.CombatId, out var customBgId))
                return;
            if (string.IsNullOrEmpty(customBgId)) return;

            var bgActiveField = trav.Field<string>("backgroundActive");
            string currentBgActive = bgActiveField.Value;
            Plugin.Log.LogInfo($"[BG Postfix] currentBgActive='{currentBgActive}', customBgId='{customBgId}'");

            // If vanilla already instantiated the right background, nothing to do
            if (string.Equals(currentBgActive, customBgId, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogInfo("[BG Postfix] Vanilla already set correct bg, skipping");
                return;
            }

            // Override backgroundActive
            bgActiveField.Value = customBgId;

            var bgPrefabField = trav.Field<GameObject>("backgroundPrefab");

            // Find and instantiate the custom prefab
            for (int i = 0; i < __instance.backgroundPrefabs.Count; i++)
            {
                var bp = __instance.backgroundPrefabs[i];
                if (bp == null) continue;
                if (!string.Equals(bp.name, customBgId, StringComparison.OrdinalIgnoreCase)) continue;

                Plugin.Log.LogInfo($"[BG Postfix] Found custom prefab at index {i}, name='{bp.name}', children={bp.transform.childCount}");

                // Check if already instantiated under backgroundTransform
                bool exists = false;
                for (int j = 0; j < __instance.backgroundTransform.childCount; j++)
                {
                    if (__instance.backgroundTransform.GetChild(j).gameObject.name == bp.name)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists)
                {
                    Plugin.Log.LogInfo("[BG Postfix] Custom bg already instantiated, skipping");
                    break;
                }

                // Destroy the vanilla-instantiated background if it's wrong
                var oldPrefab = bgPrefabField.Value;
                if (oldPrefab != null)
                {
                    Plugin.Log.LogInfo($"[BG Postfix] Destroying vanilla bg '{oldPrefab.name}'");
                    UnityEngine.Object.Destroy(oldPrefab);
                    bgPrefabField.Value = null;
                }

                // Instantiate our custom background
                var inst = UnityEngine.Object.Instantiate(
                    bp, Vector3.zero, Quaternion.identity, __instance.backgroundTransform);
                inst.name = bp.name;
                inst.transform.localScale = new Vector3(0.545f, 0.545f, 1f);
                inst.SetActive(true);
                bgPrefabField.Value = inst;

                // Log child state
                for (int c = 0; c < inst.transform.childCount; c++)
                {
                    var child = inst.transform.GetChild(c);
                    var sr = child.GetComponent<SpriteRenderer>();
                    Plugin.Log.LogInfo($"[BG Postfix]   child[{c}] '{child.name}' active={child.gameObject.activeSelf} sprite={sr?.sprite?.name ?? "(null)"} order={sr?.sortingOrder}");
                }

                Plugin.Log.LogInfo($"[BG Postfix] Custom bg instantiated: '{inst.name}' active={inst.activeSelf} scale={inst.transform.localScale}");
                break;
            }
        }
    }
}
