using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnknownMod.Definitions;
using UnknownMod.Editor;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    /// <summary>
    /// Global runtime registry for all loaded mods.
    /// Owns zone lookups, sprite resolution, NPC def tracking,
    /// and Globals key tracking for clean teardown on rebuild.
    /// Editor-side mutation lives in <see cref="ZoneEditingService"/>.
    /// </summary>
    public static class ModRegistry
    {
        // ── Zone tracking ────────────────────────────────────────────
        /// <summary>All loaded zone DTOs, keyed by zone ID.</summary>
        public static readonly Dictionary<string, ZoneDef> LoadedZones = new();

        /// <summary>Zone folder path map. Keyed by zone ID, value is disk folder path.</summary>
        public static readonly Dictionary<string, string> ZoneFolderMap = new();
        // ── Starter map override (set by last mod with a non-empty StarterNodeId) ─
        /// <summary>If non-empty, BeginAdventure is redirected to this node instead of "sen_0".</summary>
        public static string StarterNodeId { get; set; } = "";

        /// <summary>Called at runtime by the transpiled BeginAdventure IL.
        /// Returns StarterNodeId if configured, otherwise falls back to "sen_0".</summary>
        public static string GetStarterNode()
        {
            if (!string.IsNullOrEmpty(StarterNodeId))
            {
                Plugin.Log.LogInfo($"[ModRegistry] Starter node override: '{StarterNodeId}'");
                return StarterNodeId;
            }
            return "sen_0";
        }
        // ── Global cross-mod registries (last writer wins) ───────────
        /// <summary>Global sprite overrides from all loaded mods. Keyed by sprite def ID.</summary>
        public static readonly Dictionary<string, CharacterOverrideDef> GlobalSpriteSkins = new();

        /// <summary>Global NPC definitions from all loaded mods. Keyed by NPC ID.</summary>
        public static readonly Dictionary<string, NpcDef> GlobalNpcs = new();

        // ── Mod image sprites (loaded from sprites/ folder on disk) ──
        /// <summary>Sprites loaded from image files in mod sprites/ folders. Keyed by sprite name.</summary>
        public static readonly Dictionary<string, Sprite> ModImageSprites = new();

        // ── Skin → CharacterOverrideDef mapping (for runtime hero sprite resolution) ──
        /// <summary>Maps skin ID → CharacterOverrideDef for skins with runtime sprite overrides.
        /// Populated at build time; queried by HeroItem.Init postfix patch.</summary>
        public static readonly Dictionary<string, CharacterOverrideDef> SkinOverrides = new();

        // ── Globals key tracking (for clean teardown) ────────────────
        /// <summary>Keys registered in each Globals dict field by mod builds. Used to remove stale entries on rebuild.</summary>
        private static readonly Dictionary<string, HashSet<string>> _registeredKeys = new();

        /// <summary>Tier reward keys (int-keyed dict) registered by mod builds.</summary>
        private static readonly HashSet<int> _registeredTierKeys = new();

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
        public static void RegisterModSpriteSkins(
            Dictionary<string, CharacterOverrideDef> sprites,
            Dictionary<string, CharacterOverrideDef> spritePatches)
        {
            if (sprites != null)
                foreach (var kvp in sprites)
                    GlobalSpriteSkins[kvp.Key] = kvp.Value;
            if (spritePatches != null)
                foreach (var kvp in spritePatches)
                    GlobalSpriteSkins[kvp.Key] = kvp.Value;
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

        /// <summary>
        /// Register a skin's sprite override def for runtime resolution.
        /// Called from the Skins build step afterBuild callback.
        /// </summary>
        public static void RegisterSkinOverride(string skinId, CharacterOverrideDef overrideDef)
        {
            if (string.IsNullOrEmpty(skinId) || overrideDef == null) return;
            SkinOverrides[skinId] = overrideDef;
            Plugin.Log.LogInfo($"[ModRegistry] Registered skin sprite override for '{skinId}'");
        }

        /// <summary>
        /// Resolve the CharacterOverrideDef for a hero skin at runtime.
        /// Returns null if no modded sprite override exists for this skin.
        /// </summary>
        public static CharacterOverrideDef ResolveOverrideForSkin(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            SkinOverrides.TryGetValue(skinId, out var def);
            return def;
        }

        // ═══════════════════════════════════════════════════════════════
        //  GLOBALS KEY TRACKING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Track a key registered in a Globals dict field for later cleanup.
        /// Called by DataHelper.Register* methods.
        /// </summary>
        public static void TrackRegistration(string fieldName, string key)
        {
            if (!_registeredKeys.TryGetValue(fieldName, out var keys))
            {
                keys = new HashSet<string>();
                _registeredKeys[fieldName] = keys;
            }
            keys.Add(key);
        }

        /// <summary>Track a tier reward registration for later cleanup (int-keyed dict).</summary>
        public static void TrackTierRewardRegistration(int tierNum)
        {
            _registeredTierKeys.Add(tierNum);
        }

        /// <summary>
        /// Remove all mod-registered keys from Globals dictionaries.
        /// Called during ClearAll before a full rebuild.
        /// </summary>
        private static void UnregisterFromGlobals()
        {
            if (Globals.Instance == null) return;

            foreach (var kvp in _registeredKeys)
            {
                var fieldTraverse = Traverse.Create(Globals.Instance).Field(kvp.Key);
                if (fieldTraverse?.GetValue() is System.Collections.IDictionary dict)
                {
                    foreach (var key in kvp.Value)
                    {
                        if (dict.Contains(key))
                            dict.Remove(key);
                    }
                }
            }

            Plugin.Log.LogInfo($"[ModRegistry] Unregistered {_registeredKeys.Sum(k => k.Value.Count)} keys from Globals.");
            _registeredKeys.Clear();

            // Remove tier reward int-keyed registrations
            if (_registeredTierKeys.Count > 0)
            {
                var tierDict = Traverse.Create(Globals.Instance)
                    .Field<Dictionary<int, TierRewardData>>("_TierRewardDataSource").Value;
                if (tierDict != null)
                {
                    foreach (var tier in _registeredTierKeys)
                        tierDict.Remove(tier);
                }
                Plugin.Log.LogInfo($"[ModRegistry] Unregistered {_registeredTierKeys.Count} tier reward keys.");
                _registeredTierKeys.Clear();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLEAR
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Clear all registries and remove stale entries from Globals. Called before a full rebuild.</summary>
        public static void ClearAll()
        {
            UnregisterFromGlobals();
            LoadedZones.Clear();
            ZoneFolderMap.Clear();
            GlobalSpriteSkins.Clear();
            GlobalNpcs.Clear();
            SkinOverrides.Clear();
            Editor.MapEditor.InvalidateSpriteNameCache();
            StarterNodeId = "";

            // Destroy all active override drivers BEFORE clearing sprite/texture caches.
            // Drivers hold references to Sprites backed by cached textures — destroying
            // textures first would cause magenta rendering on any surviving drivers.
            DestroyActiveOverrideDrivers();

            NpcPrefabBuilder.ClearCache();
            ClearModImageSprites();
            SpriteUtils.ClearSpriteCache();
            CharacterOverrideDriver.ClearAllPivotCaches();
            DataHelper.CustomBackgroundPrefabs.Clear();
            DataHelper.CombatCustomBackgrounds.Clear();
        }

        /// <summary>Find and destroy all active CharacterOverrideDriver + GraftPuppet components
        /// so their OnDestroy cleans up sprite caches before textures are destroyed.</summary>
        private static void DestroyActiveOverrideDrivers()
        {
            // Use the static registry instead of FindObjectsOfType, which misses inactive GOs.
            // Copy to list first since OnDestroy modifies the collection.
            var drivers = new List<Runtime.CharacterOverrideDriver>(Runtime.CharacterOverrideDriver.AllDrivers);

            // Collect puppet GOs from each driver BEFORE destroying drivers.
            // This catches puppets on inactive GOs that FindObjectsOfType would miss.
            var puppetGOs = new List<GameObject>();
            foreach (var driver in drivers)
            {
                if (driver == null) continue;
                foreach (var puppet in driver.Puppets)
                    if (puppet != null) puppetGOs.Add(puppet.gameObject);
            }

            // Destroy puppets first so their OnDestroy runs while textures still exist.
            foreach (var go in puppetGOs)
                if (go != null) Object.DestroyImmediate(go);

            // Now destroy drivers.
            foreach (var driver in drivers)
            {
                if (driver != null) Object.DestroyImmediate(driver);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD IMAGE SPRITES
        // ═══════════════════════════════════════════════════════════════

        private static readonly string[] _imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

        /// <summary>
        /// Scan a mod's sprites/ folder for image files and load them as named Sprites.
        /// If a sprite name conflicts with a base-game sprite, the mod ID is prepended.
        /// </summary>
        public static void LoadModImageSprites(string modId, string modRoot)
        {
            string spritesDir = System.IO.Path.Combine(modRoot, "sprites");
            if (!System.IO.Directory.Exists(spritesDir)) return;

            // Build a set of existing sprite names ONCE (instead of per-file)
            var existingNames = new HashSet<string>();
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null) existingNames.Add(s.name);

            int loaded = 0;
            foreach (var file in System.IO.Directory.GetFiles(spritesDir))
            {
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                bool isImage = false;
                foreach (var valid in _imageExtensions)
                    if (ext == valid) { isImage = true; break; }
                if (!isImage) continue;

                try
                {
                    byte[] data = System.IO.File.ReadAllBytes(file);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(data);
                    tex.filterMode = FilterMode.Bilinear;

                    float ppu = Mathf.Max(tex.width, tex.height) / 10f; // reasonable default
                    string baseName = System.IO.Path.GetFileNameWithoutExtension(file);

                    // Check for conflict with base-game sprites
                    string spriteName = baseName;
                    bool conflict = existingNames.Contains(baseName);
                    if (conflict)
                        spriteName = $"{modId}_{baseName}";

                    var sprite = Sprite.Create(tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), ppu);
                    sprite.name = spriteName;

                    ModImageSprites[spriteName] = sprite;
                    loaded++;

                    if (conflict)
                        Plugin.Log.LogInfo($"[ModRegistry] Loaded mod sprite '{baseName}' as '{spriteName}' (name conflict with base game)");
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError($"[ModRegistry] Failed to load image sprite '{file}': {ex.Message}");
                }
            }

            if (loaded > 0)
                Plugin.Log.LogInfo($"[ModRegistry] Loaded {loaded} image sprite(s) from '{spritesDir}'");
        }

        /// <summary>Destroy all mod-loaded image sprites and their textures.</summary>
        private static void ClearModImageSprites()
        {
            foreach (var sprite in ModImageSprites.Values)
            {
                if (sprite != null)
                {
                    var tex = sprite.texture;
                    Object.DestroyImmediate(sprite);
                    if (tex != null) Object.DestroyImmediate(tex);
                }
            }
            ModImageSprites.Clear();
        }

        // ═══════════════════════════════════════════════════════════════
        //  PERSISTENT EDITOR
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create the persistent ModEditor GameObject if it doesn't already exist.
        /// Called after mods are loaded so the editor is available from any scene.
        /// </summary>
        public static void EnsureEditorExists()
        {
            if (ModEditor.Instance != null) return;
            var go = new GameObject("[UnknownMod] ModEditor");
            go.AddComponent<ModEditor>(); // Awake calls DontDestroyOnLoad
            Plugin.Log.LogInfo("[ModRegistry] Created persistent ModEditor.");
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
            if (!string.IsNullOrEmpty(npcDef.SpriteSource) &&
                GlobalSpriteSkins.TryGetValue(npcDef.SpriteSource, out var spriteDef) &&
                !string.IsNullOrEmpty(spriteDef.BaseSprite))
            {
                return spriteDef.BaseSprite;
            }
            return npcDef.SpriteSource;
        }

        /// <summary>
        /// Resolve the CharacterOverrideDef for an NPC by checking its SpriteSkinId
        /// → GlobalSpriteSkins directly.
        /// </summary>
        public static CharacterOverrideDef ResolveOverrideForNpc(NpcDef npcDef)
        {
            // Direct path: SpriteSkinId → GlobalSpriteSkins
            if (!string.IsNullOrEmpty(npcDef.SpriteSkinId) &&
                GlobalSpriteSkins.TryGetValue(npcDef.SpriteSkinId, out var overrideDef))
            {
                return overrideDef;
            }

            // Legacy fallback: SpriteSource → GlobalSpriteSkins
            if (!string.IsNullOrEmpty(npcDef.SpriteSource) &&
                GlobalSpriteSkins.TryGetValue(npcDef.SpriteSource, out var legacyDef))
                return legacyDef;

            return null;
        }

        /// <summary>Variant suffixes checked longest-first to avoid partial matches
        /// (e.g. "_plus_b" must be checked before "_b" or "_plus").</summary>
        private static readonly string[] VariantSuffixes = { "_plush_b", "_plus_b", "_plush", "_plus", "_b" };

        /// <summary>Strip variant suffixes from NPC IDs (e.g. "spider_plus_b" → "spider").
        /// Used for runtime resolution where variant NPCs share their base's sprite.
        /// </summary>
        public static string StripVariantSuffix(string npcId)
        {
            foreach (string suffix in VariantSuffixes)
            {
                if (npcId.EndsWith(suffix))
                    return npcId.Substring(0, npcId.Length - suffix.Length);
            }
            return npcId;
        }
    }
}
