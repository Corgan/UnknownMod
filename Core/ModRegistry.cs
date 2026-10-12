using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityObject = UnityEngine.Object;
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
        public static readonly Dictionary<string, CharacterVisualDef> GlobalVisualGraphs = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, CompiledCharacterVisualGraph> GlobalCompiledVisualGraphs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Global NPC definitions from all loaded mods. Keyed by NPC ID.</summary>
        public static readonly Dictionary<string, NpcDef> GlobalNpcs = new();

        // ── Mod image sprites (loaded from sprites/ folder on disk) ──
        /// <summary>Sprites loaded from image files in mod sprites/ folders. Keyed by sprite name.</summary>
        public static readonly Dictionary<string, Sprite> ModImageSprites = new();

        public static bool IsModImageSprite(Sprite sprite)
        {
            if (sprite == null) return false;
            return ModImageSprites.TryGetValue(sprite.name, out var s) && s == sprite;
        }

        public static readonly Dictionary<string, string> SkinVisualGraphs = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, string> PetVisualGraphs = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, string> NpcVisualGraphs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _npcVisualGraphKeys = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> _petAliasesByCardKey = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _petMappingOwnerByKey = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _petAliasOwnerByAlias = new(StringComparer.OrdinalIgnoreCase);

        // ── Globals key tracking (for clean teardown) ────────────────
        /// <summary>Keys registered in each Globals dict field by mod builds. Used to remove stale entries on rebuild.</summary>
        private static readonly Dictionary<string, HashSet<string>> _registeredKeys = new();

        /// <summary>Tier reward keys (int-keyed dict) registered by mod builds.</summary>
        private static readonly HashSet<int> _registeredTierKeys = new();

        /// <summary>Aura/curse ids appended to Globals._AurasCursesIndex by mod builds.</summary>
        private static readonly List<string> _registeredAuraIndex = new();

        // ═══════════════════════════════════════════════════════════════
        //  ZONE QUERIES
        // ═══════════════════════════════════════════════════════════════

        public static bool IsModdedZone(string zoneId) => LoadedZones.ContainsKey(zoneId);

        /// <summary>Zone patches by base zone id; applied to the game's prefab map when it is instantiated.</summary>
        public static readonly Dictionary<string, ZonePatchDef> LoadedZonePatches = new(StringComparer.OrdinalIgnoreCase);

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

        public static void RegisterModVisualGraphs(
            Dictionary<string, CharacterVisualDef> graphs,
            Dictionary<string, CharacterVisualDef> graphPatches)
        {
            RegisterVisualGraphSet(graphs);
            RegisterVisualGraphSet(graphPatches);
        }

        public static void RefreshVisualGraphs(IList<ModProject> projects)
        {
            GlobalVisualGraphs.Clear();
            GlobalCompiledVisualGraphs.Clear();
            SkinVisualGraphs.Clear();
            PetVisualGraphs.Clear();
            NpcVisualGraphs.Clear();
            _npcVisualGraphKeys.Clear();
            _petAliasesByCardKey.Clear();
            _petMappingOwnerByKey.Clear();
            _petAliasOwnerByAlias.Clear();
            if (projects != null)
                foreach (var project in projects)
                {
                    RegisterModVisualGraphs(project.VisualGraphs, project.VisualGraphPatches);
                    RegisterVisualGraphReferences(project.Npcs, project.NpcPatches, project.Skins, project.SkinPatches, project.Cards, project.CardPatches);
                }
            Runtime.VisualGraphHost.RefreshAll();
        }

        private static void RegisterVisualGraphSet(Dictionary<string, CharacterVisualDef> graphs)
        {
            if (graphs == null) return;
            foreach (var kvp in graphs)
            {
                var result = VisualGraphCompiler.Compile(kvp.Value);
                LogVisualGraphDiagnostics(kvp.Key, result.Diagnostics);
                if (!result.Success) continue;
                if (GlobalCompiledVisualGraphs.ContainsKey(kvp.Key))
                    Plugin.Log.LogInfo($"[ModRegistry] Visual graph '{kvp.Key}' overridden by a later valid definition.");
                GlobalVisualGraphs[kvp.Key] = kvp.Value;
                GlobalCompiledVisualGraphs[kvp.Key] = result.Graph;
            }
        }

        private static void LogVisualGraphDiagnostics(string graphId, IReadOnlyList<VisualGraphDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics.OrderBy(d => d.NodeId).ThenBy(d => d.Field).ThenBy(d => d.Message))
            {
                string text = $"[ModRegistry] Visual graph '{graphId}' {diagnostic}";
                if (diagnostic.Severity == VisualGraphDiagnosticSeverity.Error) Plugin.Log.LogError(text);
                else Plugin.Log.LogWarning(text);
            }
        }

        public static void RegisterVisualGraphReferences(
            Dictionary<string, NpcDef> npcs, Dictionary<string, NpcDef> npcPatches,
            Dictionary<string, SkinDef> skins, Dictionary<string, SkinDef> skinPatches,
            Dictionary<string, CardDef> cards, Dictionary<string, CardDef> cardPatches)
        {
            RegisterNpcVisualGraphReferences(npcs);
            RegisterNpcVisualGraphReferences(npcPatches);
            RegisterSkinVisualGraphReferences(skins);
            RegisterSkinVisualGraphReferences(skinPatches);
            RegisterPetVisualGraphReferences(cards);
            RegisterPetVisualGraphReferences(cardPatches);
        }

        private static void RegisterNpcVisualGraphReferences(Dictionary<string, NpcDef> defs)
        {
            if (defs == null) return;
            foreach (var kvp in defs)
            {
                _npcVisualGraphKeys.Add(kvp.Key);
                NpcVisualGraphs.Remove(kvp.Key);
                ApplyGraftSource(ref kvp.Value.SpriteSource, ref kvp.Value.VisualGraphId);
                if (string.IsNullOrEmpty(kvp.Value.VisualGraphId)) continue;
                if (GlobalCompiledVisualGraphs.ContainsKey(kvp.Value.VisualGraphId))
                {
                    NpcVisualGraphs[kvp.Key] = kvp.Value.VisualGraphId;
                    LogReferenceResult("NPC", kvp.Key, kvp.Value.VisualGraphId);
                    ValidateBasePresentation("NPC", kvp.Key, kvp.Value.SpriteSource, kvp.Value.VisualGraphId);
                }
                else
                    LogReferenceResult("NPC", kvp.Key, kvp.Value.VisualGraphId);
            }
        }

        private static void RegisterSkinVisualGraphReferences(Dictionary<string, SkinDef> defs)
        {
            if (defs == null) return;
            foreach (var kvp in defs)
            {
                SkinVisualGraphs.Remove(kvp.Key);
                ApplyGraftSource(ref kvp.Value.SpriteSource, ref kvp.Value.VisualGraphId);
                if (string.IsNullOrEmpty(kvp.Value.VisualGraphId)) continue;
                if (GlobalCompiledVisualGraphs.ContainsKey(kvp.Value.VisualGraphId))
                {
                    SkinVisualGraphs[kvp.Key] = kvp.Value.VisualGraphId;
                    ValidateBasePresentation("Skin", kvp.Key, kvp.Value.SpriteSource, kvp.Value.VisualGraphId);
                }
                else
                    Plugin.Log.LogWarning($"[ModRegistry] Skin '{kvp.Key}' references missing visual graph '{kvp.Value.VisualGraphId}'.");
            }
        }

        /// <summary>
        /// A Graft Sprite is a mod-authored sprite source: SpriteSource may name one directly, and a graft
        /// always supplies the rig (its Host Model).
        /// </summary>
        private static void ApplyGraftSource(ref string spriteSource, ref string graphId)
        {
            if (string.IsNullOrEmpty(graphId) && !string.IsNullOrEmpty(spriteSource) && GlobalVisualGraphs.ContainsKey(spriteSource))
            {
                graphId = spriteSource;
                spriteSource = "";
            }
            if (string.IsNullOrEmpty(graphId)) return;
            if (GlobalVisualGraphs.TryGetValue(graphId, out var g) && !string.IsNullOrEmpty(g.BasePresentation))
                spriteSource = g.BasePresentation;
        }

        private static void RegisterPetVisualGraphReferences(Dictionary<string, CardDef> defs)
        {
            if (defs == null) return;
            foreach (var kvp in defs)
            {
                HashSet<string> previousAliases;
                if (_petAliasesByCardKey.TryGetValue(kvp.Key, out previousAliases))
                {
                    foreach (var alias in previousAliases)
                    {
                        if (_petMappingOwnerByKey.TryGetValue(alias, out var owner) &&
                            _petAliasOwnerByAlias.TryGetValue(alias, out var aliasOwner) &&
                            string.Equals(aliasOwner, kvp.Key, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(owner, kvp.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            PetVisualGraphs.Remove(alias);
                            _petMappingOwnerByKey.Remove(alias);
                            _petAliasOwnerByAlias.Remove(alias);
                        }
                    }
                    previousAliases.Clear();
                }
                if (string.IsNullOrEmpty(kvp.Value.PetVisualGraphId)) continue;
                if (!GlobalCompiledVisualGraphs.ContainsKey(kvp.Value.PetVisualGraphId))
                {
                    Plugin.Log.LogWarning($"[ModRegistry] Card '{kvp.Key}' references missing visual graph '{kvp.Value.PetVisualGraphId}'.");
                    continue;
                }
                PetVisualGraphs[kvp.Key] = kvp.Value.PetVisualGraphId;
                _petMappingOwnerByKey[kvp.Key] = kvp.Key;
                _petAliasOwnerByAlias[kvp.Key] = kvp.Key;
                if (previousAliases == null) previousAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                previousAliases.Add(kvp.Key);
                if (!string.IsNullOrEmpty(kvp.Value.BaseCardId))
                {
                    PetVisualGraphs[kvp.Value.BaseCardId] = kvp.Value.PetVisualGraphId;
                    _petMappingOwnerByKey[kvp.Value.BaseCardId] = kvp.Key;
                    _petAliasOwnerByAlias[kvp.Value.BaseCardId] = kvp.Key;
                    previousAliases.Add(kvp.Value.BaseCardId);
                }
                _petAliasesByCardKey[kvp.Key] = previousAliases;
            }
        }

        private static void LogReferenceResult(string kind, string id, string graphId)
        {
            if (!GlobalCompiledVisualGraphs.ContainsKey(graphId))
                Plugin.Log.LogWarning($"[ModRegistry] {kind} '{id}' references missing visual graph '{graphId}'.");
        }

            private static void ValidateBasePresentation(string kind, string id, string spriteSource, string graphId)
            {
                CompiledCharacterVisualGraph graph;
                if (string.IsNullOrEmpty(spriteSource) || !TryResolveVisualGraph(graphId, out graph)) return;
                if (!string.Equals(spriteSource, graph.BasePresentation, StringComparison.OrdinalIgnoreCase))
                Plugin.Log.LogWarning($"[ModRegistry] {kind} '{id}' visual graph '{graphId}' base presentation '{graph.BasePresentation}' conflicts with SpriteSource '{spriteSource}'; native base presentation retained.");
            }

        public static bool TryResolveVisualGraphForNpc(NpcDef npcDef, out CompiledCharacterVisualGraph graph)
        {
            graph = null;
            if (npcDef == null) return false;
            return TryResolveVisualGraph(npcDef.VisualGraphId, out graph);
        }

        public static bool TryResolveVisualGraphForNpc(string npcId, out CompiledCharacterVisualGraph graph)
        {
            graph = null;
            if (string.IsNullOrEmpty(npcId)) return false;
            string baseId = StripVariantSuffix(npcId);
            string graphId;
            if (_npcVisualGraphKeys.Contains(npcId))
                return NpcVisualGraphs.TryGetValue(npcId, out graphId) && TryResolveVisualGraph(graphId, out graph);
            if (!NpcVisualGraphs.TryGetValue(baseId, out graphId)) return false;
            return TryResolveVisualGraph(graphId, out graph);
        }

        public static bool TryResolveVisualGraphForSkin(string skinId, out CompiledCharacterVisualGraph graph)
        {
            graph = null;
            string graphId;
            return SkinVisualGraphs.TryGetValue(skinId ?? "", out graphId) && TryResolveVisualGraph(graphId, out graph);
        }

        public static bool TryResolveVisualGraphForPet(string cardId, string internalId, out CompiledCharacterVisualGraph graph)
        {
            graph = null;
            string graphId;
            if (PetVisualGraphs.TryGetValue(cardId ?? "", out graphId) && TryResolveVisualGraph(graphId, out graph)) return true;
            return PetVisualGraphs.TryGetValue(internalId ?? "", out graphId) && TryResolveVisualGraph(graphId, out graph);
        }

        private static bool TryResolveVisualGraph(string graphId, out CompiledCharacterVisualGraph graph)
        {
            graph = null;
            return !string.IsNullOrEmpty(graphId) && GlobalCompiledVisualGraphs.TryGetValue(graphId, out graph);
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
        //  GLOBALS KEY TRACKING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Track a key registered in a Globals dict field for later cleanup.
        /// Called by DataHelper.Register* methods.
        /// </summary>
        public static void TrackRegistration(string fieldName, string key, object original = null)
        {
            if (!_registeredKeys.TryGetValue(fieldName, out var keys))
            {
                keys = new HashSet<string>();
                _registeredKeys[fieldName] = keys;
            }
            // First write to this key: remember any pre-existing (vanilla) value so cleanup can restore it
            if (keys.Add(key) && original != null)
                _originalValues[(fieldName, key)] = original;
        }

        private static readonly Dictionary<(string field, string key), object> _originalValues = new();

        /// <summary>True when a mod registered (or replaced) this key in the given Globals dictionary.</summary>
        public static bool IsTracked(string fieldName, string key) =>
            _registeredKeys.TryGetValue(fieldName, out var keys) && keys.Contains(key);

        public static void TrackAuraIndex(string id) => _registeredAuraIndex.Add(id);

        /// <summary>Track a tier reward registration for later cleanup (int-keyed dict).</summary>
        public static void TrackTierRewardRegistration(int tierNum, TierRewardData original = null)
        {
            // First write to this tier: remember the vanilla value so cleanup can restore it
            if (_registeredTierKeys.Add(tierNum) && original != null)
                _tierOriginals[tierNum] = original;
        }

        private static readonly Dictionary<int, TierRewardData> _tierOriginals = new();

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
                        _originalValues.TryGetValue((kvp.Key, key), out var originalForIndex);
                        if (kvp.Key == "_Cards") DataHelper.ReindexCardForUnload(dict, key, originalForIndex);
                        if (_originalValues.TryGetValue((kvp.Key, key), out var original))
                            dict[key] = original;
                        else if (dict.Contains(key))
                            dict.Remove(key);
                    }
                }
            }

            Plugin.Log.LogInfo($"[ModRegistry] Unregistered {_registeredKeys.Sum(k => k.Value.Count)} keys from Globals.");
            _registeredKeys.Clear();
            _originalValues.Clear();

            if (_registeredAuraIndex.Count > 0)
            {
                var index = Traverse.Create(Globals.Instance).Field<List<string>>("_AurasCursesIndex").Value;
                if (index != null)
                    foreach (var id in _registeredAuraIndex) index.Remove(id);
                _registeredAuraIndex.Clear();
            }

            // Remove tier reward int-keyed registrations
            if (_registeredTierKeys.Count > 0)
            {
                var tierDict = Traverse.Create(Globals.Instance)
                    .Field<Dictionary<int, TierRewardData>>("_TierRewardDataSource").Value;
                if (tierDict != null)
                {
                    foreach (var tier in _registeredTierKeys)
                    {
                        if (_tierOriginals.TryGetValue(tier, out var vanilla)) tierDict[tier] = vanilla;
                        else tierDict.Remove(tier);
                    }
                }
                _tierOriginals.Clear();
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
            Runtime.VisualGraphHost.DestroyAll();
            ModProjectBuilder.AppendedReplyOwner.Clear();
            LoadedZones.Clear();
            LoadedZonePatches.Clear();
            ZoneFolderMap.Clear();
            GlobalVisualGraphs.Clear();
            GlobalCompiledVisualGraphs.Clear();
            GlobalNpcs.Clear();
            SkinVisualGraphs.Clear();
            PetVisualGraphs.Clear();
            NpcVisualGraphs.Clear();
            _npcVisualGraphKeys.Clear();
            _petAliasesByCardKey.Clear();
            _petMappingOwnerByKey.Clear();
            _petAliasOwnerByAlias.Clear();
            Editor.MapEditor.InvalidateSpriteNameCache();
            StarterNodeId = "";

            DataHelper.ClearCustomBackgrounds();
            DataHelper.ModTexts.Clear();
            DataHelper.ResetRuntimeState();
            Runtime.TmpSpriteRegistry.Clear();
            DestroyDerivedSprites();
            ClearModImageSprites();
            AudioUtils.ClearModAudio();
            SpriteUtils.ClearSpriteCache();
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
            // Sprites of images that were deleted since the last build must not keep resolving
            string prefix = System.IO.Path.GetFullPath(spritesDir) + System.IO.Path.DirectorySeparatorChar;
            foreach (var file in ModSpriteFiles.Values.Distinct().ToList())
                if (System.IO.Path.GetFullPath(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !System.IO.File.Exists(file))
                    RemoveImageSprites(file);
            if (!System.IO.Directory.Exists(spritesDir)) return;

            // Build a set of existing sprite names ONCE (instead of per-file)
            var existingNames = BaseSpriteNames();

            int loaded = 0;
            foreach (var file in System.IO.Directory.GetFiles(spritesDir))
            {
                RemoveImageSprites(file); // drops stale sprites, e.g. a grid that shrank
                loaded += LoadImageFile(modId, file, existingNames);
            }

            if (loaded > 0)
                Plugin.Log.LogInfo($"[ModRegistry] Loaded {loaded} image sprite(s) from '{spritesDir}'");
        }

        /// <summary>Optional "&lt;image&gt;.json" next to a mod sprite: pixels per unit, pivot, and a grid to slice into sub-sprites.</summary>
        public class ImageSpriteMeta
        {
            public float Ppu;
            public float PivotX = 0.5f;
            public float PivotY = 0.5f;
            public int Columns = 1;
            public int Rows = 1;
            /// <summary>The Capture-from-rig settings (zoom, offsets, ...) this sprite was captured with, so the editor can restore them.</summary>
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public Newtonsoft.Json.Linq.JObject Capture;
        }

        /// <summary>Source file of each mod image sprite, keyed by sprite name.</summary>
        public static readonly Dictionary<string, string> ModSpriteFiles = new(StringComparer.OrdinalIgnoreCase);

        public static string MetaPath(string imageFile) => System.IO.Path.ChangeExtension(imageFile, ".json");

        public static ImageSpriteMeta ReadMeta(string imageFile)
        {
            try
            {
                string mp = MetaPath(imageFile);
                if (System.IO.File.Exists(mp))
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<ImageSpriteMeta>(System.IO.File.ReadAllText(mp)) ?? new ImageSpriteMeta();
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[ModRegistry] Bad sprite meta for '{imageFile}': {ex.Message}"); }
            return new ImageSpriteMeta();
        }

        /// <summary>Load one image file (plus its optional meta) as one sprite, or as a grid of "&lt;name&gt;_&lt;index&gt;" sprites. Returns the number of sprites created.</summary>
        private static int LoadImageFile(string modId, string file, HashSet<string> existingNames)
        {
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (Array.IndexOf(_imageExtensions, ext) < 0) return 0;
            Texture2D tex = null;
            int made = 0;
            try
            {
                var meta = ReadMeta(file);
                byte[] data = System.IO.File.ReadAllBytes(file);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(data)) throw new Exception("not a readable image");
                tex.filterMode = FilterMode.Bilinear;

                string baseName = System.IO.Path.GetFileNameWithoutExtension(file);
                int cols = Mathf.Max(1, meta.Columns), rows = Mathf.Max(1, meta.Rows);
                string NameOf(string s, int i) => cols * rows == 1 ? s : $"{s}_{i}";
                // Any generated name that a base-game sprite or another file's sprite already uses moves the whole image under the mod id
                bool Clashes(string s)
                {
                    for (int i = 0; i < cols * rows; i++)
                    {
                        string n = NameOf(s, i);
                        if ((existingNames != null && existingNames.Contains(n))
                            || (ModSpriteFiles.TryGetValue(n, out var owner) && !string.Equals(owner, file, StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                    return false;
                }
                bool conflict = Clashes(baseName);
                string stem = baseName;
                if (conflict)
                {
                    stem = $"{modId}_{baseName}";
                    for (int k = 2; Clashes(stem) && k < 100; k++) stem = $"{modId}_{baseName}{k}";
                }

                float cw = tex.width / (float)cols, ch = tex.height / (float)rows;
                float ppu = meta.Ppu > 0f ? meta.Ppu : Mathf.Max(cw, ch) / 10f;
                var pivot = new Vector2(meta.PivotX, meta.PivotY);
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        string name = NameOf(stem, r * cols + c);
                        var rect = new Rect(c * cw, tex.height - (r + 1) * ch, cw, ch);
                        var sprite = Sprite.Create(tex, rect, pivot, ppu);
                        sprite.name = name;
                        if (ModImageSprites.TryGetValue(name, out var previous) && previous != null)
                        {
                            if (previous.texture != null && previous.texture != tex) UnityObject.Destroy(previous.texture);
                            UnityObject.Destroy(previous);
                        }
                        ModImageSprites[name] = sprite;
                        ModSpriteFiles[name] = file;
                        made++;
                    }
                }
                if (conflict)
                    Plugin.Log.LogInfo($"[ModRegistry] Loaded mod sprite '{baseName}' as '{stem}' (name conflict with base game)");
                return made;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModRegistry] Failed to load image sprite '{file}': {ex.Message}");
                if (made == 0 && tex != null) UnityObject.Destroy(tex); // no sprite owns it
                return made;
            }
        }

        /// <summary>Copy an image into the mod's sprites/ folder, write its meta, and load it. Returns the number of sprites created, or 0 with <paramref name="error"/> set.</summary>
        public static int ImportImageSprite(string modId, string modRoot, string sourceFile, ImageSpriteMeta meta, out string error)
        {
            error = null;
            sourceFile = (sourceFile ?? "").Trim().Trim('"');
            if (sourceFile.Length == 0) { error = "Enter a file path first."; return 0; }
            if (!System.IO.File.Exists(sourceFile)) { error = "File not found: " + sourceFile; return 0; }
            if (Array.IndexOf(_imageExtensions, System.IO.Path.GetExtension(sourceFile).ToLowerInvariant()) < 0)
            { error = "Unsupported type. Use png, jpg, bmp or tga."; return 0; }

            string dir = System.IO.Path.Combine(modRoot, "sprites");
            System.IO.Directory.CreateDirectory(dir);
            string dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(sourceFile));
            if (!string.Equals(System.IO.Path.GetFullPath(sourceFile), System.IO.Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                System.IO.File.Copy(sourceFile, dest, true);
            WriteMeta(dest, meta);
            return ReloadImageFile(modId, dest, out error);
        }

        public static void WriteMeta(string imageFile, ImageSpriteMeta meta)
        {
            string mp = MetaPath(imageFile);
            bool plain = meta == null || (meta.Ppu <= 0f && meta.Columns <= 1 && meta.Rows <= 1 && Mathf.Approximately(meta.PivotX, 0.5f) && Mathf.Approximately(meta.PivotY, 0.5f));
            if (plain) { if (System.IO.File.Exists(mp)) System.IO.File.Delete(mp); return; }
            System.IO.File.WriteAllText(mp, Newtonsoft.Json.JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented));
        }

        /// <summary>Drop the sprites made from one file and load it again (after editing its meta or replacing the image).</summary>
        public static int ReloadImageFile(string modId, string file, out string error)
        {
            error = null;
            RemoveImageSprites(file);
            int n = LoadImageFile(modId, file, BaseSpriteNames());
            if (n == 0) error = "Could not load the image (see the log).";
            Editor.MapEditor.InvalidateSpriteNameCache();
            SpriteUtils.ClearSpriteCache();
            return n;
        }

        // Destroy() is deferred to the end of the frame, so these still show up in FindObjectsOfTypeAll until then.
        private static readonly HashSet<Sprite> _dyingSprites = new();

        // Re-wrapped copies of mod sprites (card art / aura icon fitting) keep the mod sprite's name.
        private static readonly HashSet<Sprite> _derivedSprites = new();

        /// <summary>Record a sprite made from a mod image sprite so its shared name is not mistaken for a base-game sprite.</summary>
        public static Sprite MarkDerivedSprite(Sprite s)
        {
            if (s != null) _derivedSprites.Add(s);
            return s;
        }

        private static void DestroyDerivedSprites()
        {
            foreach (var s in _derivedSprites) if (s != null) UnityObject.Destroy(s);
            _derivedSprites.Clear();
        }

        /// <summary>Names of every loaded sprite that is not a mod image sprite (or one being replaced), so mod files that reuse one get a mod-id prefix.</summary>
        private static HashSet<string> BaseSpriteNames()
        {
            _dyingSprites.RemoveWhere(s => s == null);
            _derivedSprites.RemoveWhere(s => s == null);
            var names = new HashSet<string>();
            var modOwned = new HashSet<Sprite>(ModImageSprites.Values);
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && !modOwned.Contains(s) && !_dyingSprites.Contains(s) && !_derivedSprites.Contains(s)) names.Add(s.name);
            return names;
        }

        /// <summary>Unload every sprite that came from <paramref name="file"/>.</summary>
        public static void RemoveImageSprites(string file)
        {
            var names = ModSpriteFiles.Where(kv => string.Equals(kv.Value, file, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            Texture tex = null;
            foreach (var n in names)
            {
                if (ModImageSprites.TryGetValue(n, out var sp) && sp != null)
                {
                    tex = sp.texture;
                    _dyingSprites.Add(sp);
                    UnityObject.Destroy(sp);
                }
                ModImageSprites.Remove(n);
                ModSpriteFiles.Remove(n);
            }
            if (tex != null)
            {
                Runtime.TmpSpriteRegistry.DropTexture(tex);
                UnityObject.Destroy(tex);
            }
            if (names.Count > 0)
            {
                Editor.MapEditor.InvalidateSpriteNameCache();
                SpriteUtils.ClearSpriteCache();
            }
        }

        /// <summary>Unload and delete an imported image and its meta.</summary>
        public static void DeleteImageFile(string file)
        {
            RemoveImageSprites(file);
            try { if (System.IO.File.Exists(file)) System.IO.File.Delete(file); } catch (Exception ex) { Plugin.Log.LogWarning($"[ModRegistry] Could not delete '{file}': {ex.Message}"); }
            try { if (System.IO.File.Exists(MetaPath(file))) System.IO.File.Delete(MetaPath(file)); } catch { }
            Editor.MapEditor.InvalidateSpriteNameCache();
            SpriteUtils.ClearSpriteCache();
        }

        /// <summary>Destroy all mod-loaded image sprites and their textures.</summary>
        private static void ClearModImageSprites()
        {
            foreach (var sprite in ModImageSprites.Values)
            {
                if (sprite != null)
                {
                    var tex = sprite.texture;
                    UnityObject.DestroyImmediate(sprite);
                    if (tex != null) UnityObject.DestroyImmediate(tex);
                }
            }
            ModImageSprites.Clear();
            ModSpriteFiles.Clear();
            Editor.MapEditor.InvalidateSpriteNameCache();
            SpriteUtils.ClearSpriteCache();
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

        /// <summary>The base-game NPC id to clone for skeleton/animations (a graft's Host Model is already folded into SpriteSource).</summary>
        public static string ResolveBaseNpcId(ZoneDef zone, NpcDef npcDef) => npcDef.SpriteSource;

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
