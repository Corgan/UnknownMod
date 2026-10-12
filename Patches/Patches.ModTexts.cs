using HarmonyLib;

namespace UnknownMod
{
    public static partial class Patches
    {
        // Mod heroes/skins have no translation-file entries, so the hero info panels (CharPopupMini, CharPopup) would show blank
        // names, class lines and descriptions. Fill in only what the game itself could not resolve.
        // GetText rewrites its own _id to "<type>_<id>" before returning, and a postfix sees that rewritten value, so the lookup key
        // is captured in a prefix from the arguments as the caller passed them.
        [HarmonyPatch(typeof(Texts), nameof(Texts.GetText))]
        [HarmonyPrefix]
        public static void GetText_Key(string _id, string _type, out string __state)
        {
            __state = null;
            if (string.IsNullOrWhiteSpace(_id) || Core.DataHelper.ModTexts.Count == 0) return;
            string key = _id.Replace(" ", "").ToLower();
            __state = string.IsNullOrEmpty(_type) ? key : _type.ToLower() + "_" + key;
        }

        [HarmonyPatch(typeof(Texts), nameof(Texts.GetText))]
        [HarmonyPostfix]
        public static void GetText_Fallback(string __state, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result) || __state == null) return;
            if (Core.DataHelper.ModTexts.TryGetValue(__state, out var text)) __result = text;
        }
    }
}
