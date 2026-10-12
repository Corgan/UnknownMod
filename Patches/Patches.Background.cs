using System;
using BattleMatch;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod
{
    /// <summary>
    /// Patches MatchBackground.DoBackground() to support custom mod-defined backgrounds.
    /// When a CombatData has a custom background ID registered in DataHelper.CombatCustomBackgrounds,
    /// it overrides the enum-based background lookup with the custom prefab name.
    /// </summary>
    public static partial class Patches
    {
        [HarmonyPatch(typeof(MatchBackground), "DoBackground", new[] { typeof(ICombatDataProvider) })]
        [HarmonyPrefix]
        public static bool DoBackground_Prefix(MatchBackground __instance, ICombatDataProvider combatDataProvider)
        {
            var cd = combatDataProvider?.CombatData;
            if (cd == null) return true;

            if (!DataHelper.CombatCustomBackgrounds.TryGetValue(DataHelper.NormalizeKey(cd.CombatId), out var customBgId))
                return true;
            customBgId = DataHelper.NormalizeBackgroundId(customBgId);
            if (string.IsNullOrEmpty(customBgId) ||
                !DataHelper.CustomBackgroundPrefabs.TryGetValue(customBgId, out var prefab) || prefab == null)
                return true;

            var backgroundTransform = __instance?.backgroundTransform;
            if (backgroundTransform == null) return true;

            var audioManager = AudioManager.Instance;
            var gameManager = GameManager.Instance;
            if (gameManager != null && audioManager != null)
                gameManager.PlayAudio(audioManager.soundCombatBegins);

            var traverse = Traverse.Create(__instance);
            for (int i = backgroundTransform.childCount - 1; i >= 0; i--)
            {
                var child = backgroundTransform.GetChild(i);
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                DataHelper.DestroyLiveCustomBackground(child.gameObject);
                if (child != null) UnityEngine.Object.Destroy(child.gameObject);
            }

            traverse.Field<GameObject>("backgroundPrefab").Value = null;
            traverse.Field<Transform>("backgroundSahtiAlive").Value = null;
            traverse.Field<Transform>("backgroundSahtiDead").Value = null;
            traverse.Field<string>("backgroundActive").Value = customBgId;

            var instance = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, backgroundTransform);
            instance.name = prefab.name;
            instance.transform.localScale = new Vector3(0.545f, 0.545f, 1f);
            instance.SetActive(true);
            DataHelper.RegisterLiveCustomBackground(instance);
            traverse.Field<GameObject>("backgroundPrefab").Value = instance;

            bool spooky = false;
            bool christmas = false;
            bool lunar = false;
            if (gameManager != null && gameManager.IsWeeklyChallenge())
            {
                var weeklyData = Globals.Instance != null && AtOManager.Instance != null
                    ? Globals.Instance.GetWeeklyData(AtOManager.Instance.GetWeekly())
                    : null;
                spooky = weeklyData?.IdSteam == "challengeSpooky";
                christmas = weeklyData?.IdSteam == "challengeChristmas";
                lunar = weeklyData?.IdSteam == "challengeLunar";
            }

            var halloween = instance.transform.Find("halloween");
            if (halloween != null)
            {
                if (spooky || christmas)
                {
                    halloween.gameObject.SetActive(true);
                    if (christmas && __instance.decorationsT != null)
                    {
                        for (int i = 0; i < halloween.childCount; i++)
                        {
                            var child = halloween.GetChild(i);
                            if (!child.gameObject.activeSelf) continue;
                            var source = child.GetComponent<SpriteRenderer>();
                            if (source == null && child.childCount > 0)
                                source = child.GetChild(0).GetComponent<SpriteRenderer>();
                            if (source == null || source.sprite == null) continue;

                            var decorationName = source.sprite.name.Replace("halloween", "christmas").Replace("_front", "");
                            foreach (var decoration in __instance.decorationsT)
                            {
                                if (decoration == null || decoration.name != decorationName) continue;
                                var replacement = UnityEngine.Object.Instantiate(decoration, child.position, Quaternion.identity, halloween);
                                replacement.transform.localScale = child.localScale;
                                var replacementRenderer = replacement.GetComponent<SpriteRenderer>();
                                if (replacementRenderer != null)
                                {
                                    replacementRenderer.sortingOrder = source.sortingOrder;
                                    replacementRenderer.sortingLayerName = source.sortingLayerName;
                                }
                                break;
                            }
                            child.gameObject.SetActive(false);
                        }
                    }
                }
                else
                {
                    halloween.gameObject.SetActive(false);
                }
            }

            var lunarTransform = instance.transform.Find("Lunar");
            if (lunarTransform != null)
                lunarTransform.gameObject.SetActive(lunar);

            if (cd.IsRift && __instance.riftPrefab != null)
                UnityEngine.Object.Instantiate(__instance.riftPrefab, Vector3.zero, Quaternion.identity, backgroundTransform);
            if (audioManager != null)
            {
                if (cd.CombatMusic != null) audioManager.DoBSOAudioClip(cd.CombatMusic);
                string nativeBackground = Enum.GetName(typeof(Enums.CombatBackground), cd.CombatBackground);
                if (nativeBackground == "Senenthia_Dia") audioManager.DoAmbience("forest_ambience_day");
                else if (nativeBackground == "Senenthia_Tarde" || nativeBackground == "Senenthia_LobosTarde" || nativeBackground == "Senenthia_Bosque" || nativeBackground == "Faeborg_Bosque" || nativeBackground == "Faeborg_BosqueCastor") audioManager.DoAmbience("forest_ambience_sunset");
                else if (nativeBackground == "Senenthia_LobosNoche" || nativeBackground == "Senenthia_BosqueEntrada" || nativeBackground == "Senenthia_BosqueBoss") audioManager.DoAmbience("forest_ambience_night");
                else if (nativeBackground == "Sectarium" || nativeBackground == "Spider_Lair" || nativeBackground == "Sewers") audioManager.DoAmbience("cave_ambience");
            }
            return false;
        }
    }
}
