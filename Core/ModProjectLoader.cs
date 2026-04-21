using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Disk I/O for mod projects.
    /// Reads/writes JSON files from the <c>Data/&lt;mod_id&gt;/</c> folder tree.
    /// </summary>
    public static class ModProjectLoader
    {
        // ── Paths ────────────────────────────────────────────────────

        public static string DataRoot =>
            Path.Combine(BepInEx.Paths.PluginPath, "UnknownMod_Data");

        public static string ModFolder(string modId) =>
            Path.Combine(DataRoot, modId);

        public static string MetadataPath(string modId) =>
            Path.Combine(ModFolder(modId), $"{modId}.json");

        private static readonly JsonSerializerSettings _json = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        // ═══════════════════════════════════════════════════════════════
        //  DISCOVERY
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns all mod IDs that have a valid metadata JSON in Data/.
        /// </summary>
        public static List<string> DiscoverMods()
        {
            var mods = new List<string>();
            if (!Directory.Exists(DataRoot)) return mods;

            foreach (var dir in Directory.GetDirectories(DataRoot))
            {
                string name = Path.GetFileName(dir);
                // Skip internal files/folders (loadorder.json lives at root level)
                if (name.StartsWith(".") || name.StartsWith("_")) continue;
                string metaPath = Path.Combine(dir, $"{name}.json");
                if (File.Exists(metaPath))
                    mods.Add(name);
            }
            return mods;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LOAD A FULL MOD PROJECT
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Load an entire mod project from disk into a ModProject.</summary>
        public static ModProject Load(string modId)
        {
            var proj = new ModProject { ModId = modId };
            string root = ModFolder(modId);

            if (!Directory.Exists(root))
            {
                Plugin.Log.LogWarning($"[ModProjectLoader] Mod folder not found: {root}");
                return proj;
            }

            // Metadata
            LoadMetadata(proj, root);

            // ── New content ──────────────────────────────────────────
            // Cards: load from all semantic subfolders under cards/
            LoadCardsRecursive(Path.Combine(root, "cards"), proj.Cards);
            LoadEntities(Path.Combine(root, "loot"), proj.Loot, d => d.Id);
            LoadEntities(Path.Combine(root, "npcs"), proj.Npcs, d => d.Id);
            LoadEntities(Path.Combine(root, "auracurse"), proj.AuraCurses, d => d.Id);
            LoadEntities(Path.Combine(root, "heroes"), proj.Heroes, d => d.Id);
            LoadEntities(Path.Combine(root, "traits"), proj.Traits, d => d.Id);
            LoadEntities(Path.Combine(root, "skins"), proj.Skins, d => d.Id);
            LoadEntities(Path.Combine(root, "perks"), proj.Perks, d => d.Id);
            LoadEntities(Path.Combine(root, "perknodes"), proj.PerkNodes, d => d.Id);
            LoadEntities(Path.Combine(root, "requirements"), proj.Requirements, d => d.Id);
            LoadEntities(Path.Combine(root, "cardbacks"), proj.Cardbacks, d => d.Id);
            LoadEntities(Path.Combine(root, "tierrewards"), proj.TierRewards, d => d.Id);
            LoadEntities(Path.Combine(root, "spriteskins"), proj.SpriteSkins, d => d.Id);
            LoadEntities(Path.Combine(root, "packs"), proj.Packs, d => d.PackId);
            LoadEntities(Path.Combine(root, "cardplayerpacks"), proj.CardPlayerPacks, d => d.PackId);
            LoadEntities(Path.Combine(root, "cardplayerpairspacks"), proj.CardPlayerPairsPacks, d => d.PackId);
            LoadEntities(Path.Combine(root, "herodata"), proj.HeroDataEntries, d => d.Id);
            LoadEntities(Path.Combine(root, "backgrounds"), proj.Backgrounds, d => d.BackgroundId);
            LoadEntities(Path.Combine(root, "events"), proj.Events, d => d.EventId);
            LoadEntities(Path.Combine(root, "combats"), proj.Combats, d => d.CombatId);

            // ── Patches (overrides of base-game) ─────────────────────
            string patches = Path.Combine(root, "_patches");
            LoadCardsRecursive(Path.Combine(patches, "cards"), proj.CardPatches);
            LoadEntities(Path.Combine(patches, "loot"), proj.LootPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "npcs"), proj.NpcPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "auracurse"), proj.AuraCursePatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "heroes"), proj.HeroPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "traits"), proj.TraitPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "skins"), proj.SkinPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "perks"), proj.PerkPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "perknodes"), proj.PerkNodePatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "requirements"), proj.RequirementPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "cardbacks"), proj.CardbackPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "tierrewards"), proj.TierRewardPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "spriteskins"), proj.SpriteSkinPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "packs"), proj.PackPatches, d => d.PackId);
            LoadEntities(Path.Combine(patches, "cardplayerpacks"), proj.CardPlayerPackPatches, d => d.PackId);
            LoadEntities(Path.Combine(patches, "cardplayerpairspacks"), proj.CardPlayerPairsPackPatches, d => d.PackId);
            LoadEntities(Path.Combine(patches, "herodata"), proj.HeroDataPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "events"), proj.EventPatches, d => d.EventId);
            LoadEntities(Path.Combine(patches, "combats"), proj.CombatPatches, d => d.CombatId);
            LoadEntities(Path.Combine(patches, "backgrounds"), proj.BackgroundPatches, d => d.BackgroundId);

            // ── New zones ────────────────────────────────────────────
            string zonesDir = Path.Combine(root, "zones");
            if (Directory.Exists(zonesDir))
            {
                foreach (var zoneDir in Directory.GetDirectories(zonesDir))
                {
                    string dirName = Path.GetFileName(zoneDir);
                    if (dirName == "_patches") continue;

                    string zoneJsonPath = Path.Combine(zoneDir, "zone.json");
                    if (!File.Exists(zoneJsonPath)) continue;

                    var zoneDef = LoadZoneFromFolder(zoneDir);
                    if (!string.IsNullOrEmpty(zoneDef.ZoneId))
                        proj.Zones[zoneDef.ZoneId] = zoneDef;
                }
            }

            // ── Zone patches ─────────────────────────────────────────
            string zonePatchDir = Path.Combine(zonesDir, "_patches");
            if (Directory.Exists(zonePatchDir))
            {
                foreach (var patchDir in Directory.GetDirectories(zonePatchDir))
                {
                    string targetZoneId = Path.GetFileName(patchDir);
                    var patch = LoadZonePatch(patchDir, targetZoneId);
                    proj.ZonePatches[targetZoneId] = patch;
                }
            }

            int totalNew = proj.Cards.Count + proj.Loot.Count +
                           proj.Npcs.Count + proj.AuraCurses.Count + proj.Heroes.Count +
                           proj.Traits.Count + proj.Skins.Count + proj.Perks.Count +
                           proj.PerkNodes.Count + proj.Requirements.Count +
                           proj.Cardbacks.Count + proj.TierRewards.Count + proj.SpriteSkins.Count +
                           proj.Packs.Count + proj.CardPlayerPacks.Count +
                           proj.CardPlayerPairsPacks.Count + proj.HeroDataEntries.Count +
                           proj.Backgrounds.Count +
                           proj.Events.Count + proj.Combats.Count;

            int totalPatches = proj.CardPatches.Count +
                               proj.LootPatches.Count + proj.NpcPatches.Count +
                               proj.AuraCursePatches.Count + proj.HeroPatches.Count +
                               proj.TraitPatches.Count + proj.SkinPatches.Count +
                               proj.PerkPatches.Count + proj.PerkNodePatches.Count +
                               proj.RequirementPatches.Count + proj.CardbackPatches.Count +
                               proj.TierRewardPatches.Count + proj.SpriteSkinPatches.Count +
                               proj.PackPatches.Count + proj.CardPlayerPackPatches.Count +
                               proj.CardPlayerPairsPackPatches.Count + proj.HeroDataPatches.Count +
                               proj.EventPatches.Count + proj.CombatPatches.Count +
                               proj.BackgroundPatches.Count;

            Plugin.Log.LogInfo($"[ModProjectLoader] Loaded '{modId}': " +
                $"{totalNew} new, {totalPatches} patches, " +
                $"{proj.Zones.Count} zones, {proj.ZonePatches.Count} zone patches");

            proj.IsDirty = false;
            return proj;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SAVE HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Save mod metadata (name, author, version, etc.).</summary>
        public static void SaveMetadata(ModProject proj)
        {
            string root = ModFolder(proj.ModId);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "textures"));

            var meta = new
            {
                modId = proj.ModId,
                modName = proj.ModName,
                author = proj.Author,
                version = proj.Version,
                description = proj.Description,
                depends = proj.Dependencies.Count > 0 ? proj.Dependencies : null
            };
            File.WriteAllText(MetadataPath(proj.ModId), JsonConvert.SerializeObject(meta, _json));
        }

        /// <summary>Save a single entity to the correct folder.</summary>
        public static void SaveEntity<T>(ModProject proj, string subfolder, string filename, T entity, bool isPatch = false)
        {
            string root = ModFolder(proj.ModId);
            string folder = isPatch
                ? Path.Combine(root, "_patches", subfolder)
                : Path.Combine(root, subfolder);

            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{filename}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(entity, _json));
        }

        /// <summary>Delete an entity JSON file.</summary>
        public static bool DeleteEntity(ModProject proj, string subfolder, string filename, bool isPatch = false)
        {
            string root = ModFolder(proj.ModId);
            string folder = isPatch
                ? Path.Combine(root, "_patches", subfolder)
                : Path.Combine(root, subfolder);

            string path = Path.Combine(folder, $"{filename}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            return false;
        }

        /// <summary>Save a zone's entity to its zone subfolder.</summary>
        public static void SaveZoneEntity<T>(ModProject proj, string zoneId, string subfolder, string filename, T entity)
        {
            string folder = Path.Combine(ModFolder(proj.ModId), "zones", zoneId, subfolder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"{filename}.json"), JsonConvert.SerializeObject(entity, _json));
        }

        /// <summary>Save a zone patch entity.</summary>
        public static void SaveZonePatchEntity<T>(ModProject proj, string targetZoneId, string subfolder, string filename, T entity)
        {
            string folder = Path.Combine(ModFolder(proj.ModId), "zones", "_patches", targetZoneId, subfolder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"{filename}.json"), JsonConvert.SerializeObject(entity, _json));
        }

        /// <summary>Save the roads.json for a zone or zone patch.</summary>
        public static void SaveRoads(ModProject proj, string zoneId, Dictionary<string, RoadDef> roads, bool isPatch = false)
        {
            string folder = isPatch
                ? Path.Combine(ModFolder(proj.ModId), "zones", "_patches", zoneId)
                : Path.Combine(ModFolder(proj.ModId), "zones", zoneId);

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "roads.json"), JsonConvert.SerializeObject(roads, _json));
        }

        /// <summary>Save a full zone definition (metadata + all sub-entities).</summary>
        public static void SaveZone(ModProject proj, ZoneDef zone)
        {
            string zoneDir = Path.Combine(ModFolder(proj.ModId), "zones", zone.ZoneId);
            Directory.CreateDirectory(zoneDir);

            // Zone metadata — copy ALL zone-level fields so nothing is lost
            var meta = new ZoneDef
            {
                ZoneId = zone.ZoneId,
                ZoneName = zone.ZoneName,
                IdPrefix = zone.IdPrefix,
                ObeliskLow = zone.ObeliskLow,
                ObeliskHigh = zone.ObeliskHigh,
                ObeliskFinal = zone.ObeliskFinal,
                DisableExperience = zone.DisableExperience,
                DisableMadness = zone.DisableMadness,
                Sku = zone.Sku,
                ChangeTeamOnEntrance = zone.ChangeTeamOnEntrance,
                NewTeam = zone.NewTeam,
                RestoreTeamOnExit = zone.RestoreTeamOnExit,
                CombatBackgroundSprite = zone.CombatBackgroundSprite,
                VisualLayers = zone.VisualLayers,
                NodesOffsetX = zone.NodesOffsetX,
                NodesOffsetY = zone.NodesOffsetY,
                RoadsOffsetX = zone.RoadsOffsetX,
                RoadsOffsetY = zone.RoadsOffsetY
            };
            File.WriteAllText(Path.Combine(zoneDir, "zone.json"), JsonConvert.SerializeObject(meta, _json));

            SaveAllEntities(Path.Combine(zoneDir, "nodes"), zone.Nodes, d => d.NodeId);

            if (zone.Roads.Count > 0)
                File.WriteAllText(Path.Combine(zoneDir, "roads.json"), JsonConvert.SerializeObject(zone.Roads, _json));
        }

        // ═══════════════════════════════════════════════════════════════
        //  CREATE NEW MOD
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Create a new empty mod project on disk and return it.</summary>
        public static ModProject CreateNew(string modId, string modName, string author)
        {
            var proj = new ModProject
            {
                ModId = modId,
                ModName = modName,
                Author = author
            };

            SaveMetadata(proj);
            return proj;
        }

        // ═══════════════════════════════════════════════════════════════
        //  INTERNAL HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static void LoadMetadata(ModProject proj, string root)
        {
            string metaPath = Path.Combine(root, $"{proj.ModId}.json");
            if (!File.Exists(metaPath)) return;

            try
            {
                var jo = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(metaPath));
                proj.ModName = (string)jo["modName"] ?? "";
                proj.Author = (string)jo["author"] ?? "";
                proj.Version = (string)jo["version"] ?? "1.0.0";
                proj.Description = (string)jo["description"] ?? "";
                var deps = jo["depends"] as Newtonsoft.Json.Linq.JArray;
                if (deps != null)
                    foreach (var d in deps)
                        proj.Dependencies.Add((string)d);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ModProjectLoader] Failed to read metadata: {ex.Message}");
            }
        }

        private static void LoadEntities<T>(string folder, Dictionary<string, T> dict, Func<T, string> getKey)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    var entity = JsonConvert.DeserializeObject<T>(File.ReadAllText(file));
                    if (entity != null)
                    {
                        string key = getKey(entity);
                        if (!string.IsNullOrEmpty(key))
                            dict[key] = entity;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[ModProjectLoader] Failed to load '{file}': {ex.Message}");
                }
            }
        }

        /// <summary>Load CardDef entities from a folder and all its subfolders recursively.</summary>
        private static void LoadCardsRecursive(string folder, Dictionary<string, CardDef> dict)
        {
            if (!Directory.Exists(folder)) return;
            // Load any JSONs directly in this folder
            LoadEntities(folder, dict, d => d.Id);
            // Recurse into subfolders (hero/, equipment/, enchantments/, pets/, etc.)
            foreach (var subDir in Directory.GetDirectories(folder))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith("_") || dirName.StartsWith(".")) continue;
                LoadCardsRecursive(subDir, dict);
            }
        }

        /// <summary>Save a card to the correct semantic subfolder under cards/.</summary>
        public static void SaveCard(ModProject proj, CardDef card, bool isPatch = false)
        {
            string root = ModFolder(proj.ModId);
            string cardsRoot = isPatch
                ? Path.Combine(root, "_patches", "cards")
                : Path.Combine(root, "cards");

            // Remove any existing file first (SemanticFolder may have changed)
            DeleteCardRecursive(cardsRoot, card.Id);

            string folder = Path.Combine(cardsRoot, card.SemanticFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{card.Id}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(card, _json));
        }

        /// <summary>Delete a card JSON file (checks all semantic subfolders).</summary>
        public static bool DeleteCard(ModProject proj, string cardId, bool isPatch = false)
        {
            string root = ModFolder(proj.ModId);
            string cardsRoot = isPatch
                ? Path.Combine(root, "_patches", "cards")
                : Path.Combine(root, "cards");
            if (!Directory.Exists(cardsRoot)) return false;

            // Search all subfolders recursively for the card
            return DeleteCardRecursive(cardsRoot, cardId);
        }

        private static bool DeleteCardRecursive(string folder, string cardId)
        {
            string path = Path.Combine(folder, $"{cardId}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            foreach (var subDir in Directory.GetDirectories(folder))
            {
                if (DeleteCardRecursive(subDir, cardId))
                    return true;
            }
            return false;
        }

        private static void SaveAllEntities<T>(string folder, Dictionary<string, T> dict, Func<T, string> getKey)
        {
            if (dict.Count == 0) return;
            Directory.CreateDirectory(folder);
            foreach (var kvp in dict)
            {
                string filename = getKey(kvp.Value);
                if (string.IsNullOrEmpty(filename)) filename = kvp.Key;
                File.WriteAllText(
                    Path.Combine(folder, $"{filename}.json"),
                    JsonConvert.SerializeObject(kvp.Value, _json));
            }
        }

        private static ZoneDef LoadZoneFromFolder(string folder)
        {
            ZoneDef def;

            string zonePath = Path.Combine(folder, "zone.json");
            if (File.Exists(zonePath))
            {
                def = JsonConvert.DeserializeObject<ZoneDef>(File.ReadAllText(zonePath)) ?? new ZoneDef();
                if (string.IsNullOrEmpty(def.ZoneId))
                    def.ZoneId = Path.GetFileName(folder);
            }
            else
            {
                def = new ZoneDef();
            }

            LoadEntities(Path.Combine(folder, "nodes"), def.Nodes, d => d.NodeId);

            string roadsPath = Path.Combine(folder, "roads.json");
            if (File.Exists(roadsPath))
            {
                var roads = JsonConvert.DeserializeObject<Dictionary<string, RoadDef>>(File.ReadAllText(roadsPath));
                if (roads != null) def.Roads = roads;
            }

            return def;
        }

        private static ZonePatchDef LoadZonePatch(string folder, string targetZoneId)
        {
            var patch = new ZonePatchDef { TargetZoneId = targetZoneId };

            LoadEntities(Path.Combine(folder, "nodes"), patch.Nodes, d => d.NodeId);

            string roadsPath = Path.Combine(folder, "roads.json");
            if (File.Exists(roadsPath))
            {
                var roads = JsonConvert.DeserializeObject<Dictionary<string, RoadDef>>(File.ReadAllText(roadsPath));
                if (roads != null) patch.Roads = roads;
            }

            Plugin.Log.LogInfo($"[ModProjectLoader] Loaded zone patch '{targetZoneId}': " +
                $"{patch.Nodes.Count} nodes, {patch.Roads.Count} roads");

            return patch;
        }

    }
}
