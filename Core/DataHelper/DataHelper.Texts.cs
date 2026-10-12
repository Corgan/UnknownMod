using System;
using System.Collections.Generic;

namespace UnknownMod.Core
{
    public static partial class DataHelper
    {
        // Text the game would look up in its translation files, keyed exactly as Texts.GetText resolves it
        // (spaces removed, lowercase, "<type>_" prefix). Served by a Texts.GetText fallback because mods have no translation files.
        internal static readonly Dictionary<string, string> ModTexts = new(StringComparer.Ordinal);

        private static string TextKey(string id, string type = "")
        {
            id = (id ?? "").Replace(" ", "").ToLower();
            return string.IsNullOrEmpty(type) ? id : type.ToLower() + "_" + id;
        }

        /// <summary>
        /// Register the strings the hero info panels read from the translation tables: the display name, the class line
        /// (subclass name), and the description / strength blurbs.
        /// </summary>
        internal static void RegisterHeroTexts(SubClassData sc)
        {
            if (sc == null) return;
            string nameKey = NormalizeKey(sc.SubClassName);
            if (nameKey == "") return;
            void Set(string key, string value) { if (!string.IsNullOrEmpty(value)) ModTexts[key] = value; else ModTexts.Remove(key); }
            Set(TextKey(sc.SubClassName), sc.SubClassName);
            Set(TextKey(nameKey + "_name", "class"), sc.CharacterName);
            string id = string.IsNullOrEmpty(sc.Id) ? nameKey : sc.Id;
            Set(TextKey(id + "_name", "class"), sc.CharacterName);
            Set(TextKey(id + "_description", "class"), sc.CharacterDescription);
            Set(TextKey(id + "_strength", "class"), sc.CharacterDescriptionStrength);
        }

        private static void SetModText(string key, string value)
        {
            if (!string.IsNullOrEmpty(value)) ModTexts[key] = value; else ModTexts.Remove(key);
        }

        // Zone names are looked up as GetText(ZoneName) by the node popup and the run tome.
        internal static void RegisterZoneTexts(ZoneData z)
        {
            if (z == null || string.IsNullOrEmpty(z.ZoneName)) return;
            SetModText(TextKey(z.ZoneName), z.ZoneName);
        }

        // The travel list in the tome reads "<id>_nm" from the events table with no data-field fallback.
        internal static void RegisterEventTexts(EventData e)
        {
            if (e == null || string.IsNullOrEmpty(e.EventId)) return;
            SetModText(TextKey(e.EventId + "_nm", "events"), e.EventName);
            SetModText(TextKey(e.EventId + "_dsc", "events"), e.Description);
            SetModText(TextKey(e.EventId + "_dsca", "events"), e.DescriptionAction);
        }

        // Weekly-challenge screens read the trait name from GetText(id) and the description from GetText(id + "desc").
        internal static void RegisterChallengeTraitTexts(string id, string name, string description)
        {
            if (string.IsNullOrEmpty(id)) return;
            SetModText(TextKey(id), name);
            SetModText(TextKey(id + "desc"), description);
        }

        // PopupManager resolves <ACOnExplode> through GetText(auraId).
        internal static void RegisterAuraTexts(AuraCurseData ac)
        {
            if (ac == null || string.IsNullOrEmpty(ac.Id)) return;
            SetModText(TextKey(ac.Id), ac.ACName);
        }
    }
}
