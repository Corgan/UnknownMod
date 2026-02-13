using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Editor;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    /// <summary>
    /// Global runtime registry for all loaded mods.
    /// Owns zone lookups, sprite resolution, and NPC def tracking.
    /// Editor-side mutation lives in <see cref="ZoneEditingService"/>.
    /// </summary>
    public static class ModRegistry
    {
        // ── Zone tracking ────────────────────────────────────────────
        /// <summary>All loaded zone DTOs, keyed by zone ID.</summary>
        public static readonly Dictionary<string, ZoneDef> LoadedZones = new();

        /// <summary>Zone folder path map. Keyed by zone ID, value is disk folder path.</summary>
        public static readonly Dictionary<string, string> ZoneFolderMap = new();

        // ── Global cross-mod registries (last writer wins) ───────────
        /// <summary>Global sprite overrides from all loaded mods. Keyed by sprite def ID.</summary>
        public static readonly Dictionary<string, SpriteOverrideDef> GlobalSprites = new();

        /// <summary>Global NPC definitions from all loaded mods. Keyed by NPC ID.</summary>
        public static readonly Dictionary<string, NpcDef> GlobalNpcs = new();

        // ═══════════════════════════════════════════════════════════════
        //  ZONE QUERIES
        // ═══════════════════════════════════════════════════════════════

        public static bool IsModdedZone(string zoneId) => LoadedZones.ContainsKey(zoneId);

        public static string GetZoneFolder(string zoneId)
        {
            if (ZoneFolderMap.TryGetValue(zoneId, out var mapped))
                return mapped;
            return System.IO.Path.Combine(ModProjectLoader.DataRoot, zoneId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  REGISTRATION (called by ModProjectBuilder)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Register a mod-built zone so IsModdedZone/MapBuilder/GetZoneFolder work.
        /// </summary>
        public static void RegisterModZone(ZoneDef zoneDef, string folderPath)
        {
            LoadedZones[zoneDef.ZoneId] = zoneDef;
            ZoneFolderMap[zoneDef.ZoneId] = folderPath;
            Plugin.Log.LogInfo($"[ModRegistry] Registered mod zone '{zoneDef.ZoneId}' at {folderPath}");
        }

        /// <summary>
        /// Merge a mod's sprite defs into the global registries. Last writer wins.
        /// </summary>
        public static void RegisterModSprites(
            Dictionary<string, SpriteOverrideDef> sprites,
            Dictionary<string, SpriteOverrideDef> spritePatches)
        {
            if (sprites != null)
                foreach (var kvp in sprites)
                    GlobalSprites[kvp.Key] = kvp.Value;
            if (spritePatches != null)
                foreach (var kvp in spritePatches)
                    GlobalSprites[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Merge a mod's NPC defs into the global registry. Last writer wins.
        /// </summary>
        public static void RegisterModNpcs(
            Dictionary<string, NpcDef> npcs,
            Dictionary<string, NpcDef> npcPatches)
        {
            if (npcs != null)
                foreach (var kvp in npcs)
                    GlobalNpcs[kvp.Key] = kvp.Value;
            if (npcPatches != null)
                foreach (var kvp in npcPatches)
                    GlobalNpcs[kvp.Key] = kvp.Value;
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEAR
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Clear all registries. Called before a full rebuild.</summary>
        public static void ClearAll()
        {
            LoadedZones.Clear();
            ZoneFolderMap.Clear();
            GlobalSprites.Clear();
            GlobalNpcs.Clear();
            NpcPrefabBuilder.ClearCache();
        }

        // ═══════════════════════════════════════════════════════════════
        //  PERSISTENT EDITOR
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create the persistent ZoneEditor GameObject if it doesn't already exist.
        /// Called after mods are loaded so the editor is available from any scene.
        /// </summary>
        public static void EnsureEditorExists()
        {
            if (ZoneEditor.Instance != null) return;
            var go = new GameObject("[UnknownMod] ZoneEditor");
            go.AddComponent<ZoneEditor>(); // Awake calls DontDestroyOnLoad
            Plugin.Log.LogInfo("[ModRegistry] Created persistent ZoneEditor.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  SPRITE DEFINITION RESOLUTION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolve the base-game NPC ID to clone for skeleton/animations.
        /// If SpriteSource points to a sprite definition, returns its BaseSprite.
        /// Otherwise returns SpriteSource directly as a base-game NPC ID.
        /// </summary>
        public static string ResolveBaseNpcId(ZoneDef zone, NpcDef npcDef)
        {
            if (zone != null && !string.IsNullOrEmpty(npcDef.SpriteSource) &&
                zone.Sprites.TryGetValue(npcDef.SpriteSource, out var spriteDef) &&
                !string.IsNullOrEmpty(spriteDef.BaseSprite))
            {
                return spriteDef.BaseSprite;
            }
            return npcDef.SpriteSource;
        }

        /// <summary>
        /// Resolve the SpriteOverrideDef for an NPC by checking its SpriteSource
        /// against the zone's Sprites dictionary.
        /// </summary>
        public static SpriteOverrideDef ResolveSpriteDefForNpc(ZoneDef zone, NpcDef npcDef)
        {
            if (zone != null && !string.IsNullOrEmpty(npcDef.SpriteSource) &&
                zone.Sprites.TryGetValue(npcDef.SpriteSource, out var spriteDef))
                return spriteDef;
            return null;
        }

        /// <summary>
        /// Strip variant suffixes from an NPC ID to get the base NPC ID.
        /// Used for runtime resolution where variant NPCs share their base's sprite.
        /// </summary>
        public static string StripVariantSuffix(string npcId)
        {
            foreach (string suffix in new[] { "_plus_b", "_plus", "_b" })
            {
                if (npcId.EndsWith(suffix))
                    return npcId.Substring(0, npcId.Length - suffix.Length);
            }
            return npcId;
        }
    }
}
