using HarmonyLib;
using UnknownMod.Editor;

namespace UnknownMod
{
    /// <summary>
    /// Block game node interaction when the mod editor is active.
    /// </summary>
    public static partial class Patches
    {
        [HarmonyPatch(typeof(Node), "OnMouseUp")]
        [HarmonyPrefix]
        public static bool OnMouseUp_BlockPrefix()
        {
            return !ModEditor.IsEditing; // skip original when editing
        }

        [HarmonyPatch(typeof(Node), "OnMouseEnter")]
        [HarmonyPrefix]
        public static bool OnMouseEnter_BlockPrefix()
        {
            return !ModEditor.IsEditing;
        }

        [HarmonyPatch(typeof(Node), "OnMouseExit")]
        [HarmonyPrefix]
        public static bool OnMouseExit_BlockPrefix()
        {
            return !ModEditor.IsEditing;
        }

        [HarmonyPatch(typeof(Node), "OnMouseOver")]
        [HarmonyPrefix]
        public static bool OnMouseOver_BlockPrefix()
        {
            return !ModEditor.IsEditing;
        }
    }
}
