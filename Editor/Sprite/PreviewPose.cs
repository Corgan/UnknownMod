using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.U2D.Animation;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Helpers for editor previews that pose a prefab with AnimationClip.SampleAnimation instead of a running Animator.
    /// </summary>
    internal static class PreviewPose
    {
        private static MethodInfo _compositeLateUpdate;
        private static bool _compositeFailed;
        private static MethodInfo _instanceGetter;

        /// <summary>
        /// A live Animator rewrites the bones to its default state every frame before SpriteSkin deforms the meshes,
        /// so sampled bones and deformed sprites would disagree. Previews are driven by sampling only.
        /// </summary>
        public static void DisableAnimators(GameObject host)
        {
            if (host == null) return;
            foreach (var animator in host.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        }

        /// <summary>
        /// SpriteSkin deforms meshes in a LateUpdate that runs before OnGUI, so a pose sampled during OnGUI would
        /// otherwise render the previous frame's deformation.
        /// </summary>
        public static void RefreshSkins()
        {
            if (_compositeFailed) return;
            try
            {
                if (_compositeLateUpdate == null)
                {
                    var type = typeof(SpriteSkin).Assembly.GetType("UnityEngine.U2D.Animation.SpriteSkinComposite");
                    _compositeLateUpdate = type?.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var instance = type?.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_compositeLateUpdate == null || instance == null) { _compositeFailed = true; return; }
                    _instanceGetter = instance.GetGetMethod(true);
                }
                _compositeLateUpdate.Invoke(_instanceGetter.Invoke(null, null), null);
            }
            catch (Exception e)
            {
                _compositeFailed = true;
                Plugin.Log.LogWarning("[PreviewPose] Could not force SpriteSkin deformation: " + e.Message);
            }
        }

        /// <summary>Prefers an idle clip so the preview opens on a neutral pose rather than the alphabetically first clip.</summary>
        public static string DefaultClip(System.Collections.Generic.IList<string> names)
        {
            if (names == null || names.Count == 0) return null;
            return names.FirstOrDefault(n => n.EndsWith("idle", StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault(n => n.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? names[0];
        }
    }
}
