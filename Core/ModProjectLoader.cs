using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            LoadEntities(Path.Combine(root, "cards"), proj.Cards, d => d.Id);
            LoadEntities(Path.Combine(root, "items"), proj.Items, d => d.Id);
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
            LoadEntities(Path.Combine(root, "sprites"), proj.Sprites, d => d.NpcId);

            // ── Patches (overrides of base-game) ─────────────────────
            string patches = Path.Combine(root, "_patches");
            LoadEntities(Path.Combine(patches, "cards"), proj.CardPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "items"), proj.ItemPatches, d => d.Id);
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
            LoadEntities(Path.Combine(patches, "sprites"), proj.SpritePatches, d => d.NpcId);

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

            int totalNew = proj.Cards.Count + proj.Items.Count + proj.Loot.Count +
                           proj.Npcs.Count + proj.AuraCurses.Count + proj.Heroes.Count +
                           proj.Traits.Count + proj.Skins.Count + proj.Perks.Count +
                           proj.PerkNodes.Count + proj.Requirements.Count +
                           proj.Cardbacks.Count + proj.TierRewards.Count + proj.Sprites.Count;

            int totalPatches = proj.CardPatches.Count + proj.ItemPatches.Count +
                               proj.LootPatches.Count + proj.NpcPatches.Count +
                               proj.AuraCursePatches.Count + proj.HeroPatches.Count +
                               proj.TraitPatches.Count + proj.SkinPatches.Count +
                               proj.PerkPatches.Count + proj.PerkNodePatches.Count +
                               proj.RequirementPatches.Count + proj.CardbackPatches.Count +
                               proj.TierRewardPatches.Count + proj.SpritePatches.Count;

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

            // Zone metadata
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
                BackgroundImage = zone.BackgroundImage
            };
            File.WriteAllText(Path.Combine(zoneDir, "zone.json"), JsonConvert.SerializeObject(meta, _json));

            // Sub-entities
            SaveAllEntities(Path.Combine(zoneDir, "nodes"), zone.Nodes, d => d.NodeId);
            SaveAllEntities(Path.Combine(zoneDir, "combats"), zone.Combats, d => d.CombatId);
            SaveAllEntities(Path.Combine(zoneDir, "events"), zone.Events, d => d.EventId);
            SaveAllEntities(Path.Combine(zoneDir, "npcs"), zone.Npcs, d => d.Id);
            SaveAllEntities(Path.Combine(zoneDir, "cards"), zone.Cards, d => d.Id);
            SaveAllEntities(Path.Combine(zoneDir, "items"), zone.Items, d => d.Id);
            SaveAllEntities(Path.Combine(zoneDir, "loot"), zone.Loot, d => d.Id);
            SaveAllEntities(Path.Combine(zoneDir, "sprites"), zone.Sprites, d => d.NpcId);

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
            var def = new ZoneDef();

            string zonePath = Path.Combine(folder, "zone.json");
            if (File.Exists(zonePath))
            {
                var meta = JsonConvert.DeserializeObject<ZoneDef>(File.ReadAllText(zonePath));
                def.ZoneId = meta.ZoneId;
                def.ZoneName = meta.ZoneName;
                def.IdPrefix = meta.IdPrefix;
                def.ObeliskLow = meta.ObeliskLow;
                def.ObeliskHigh = meta.ObeliskHigh;
                def.ObeliskFinal = meta.ObeliskFinal;
                def.DisableExperience = meta.DisableExperience;
                def.DisableMadness = meta.DisableMadness;
                def.BackgroundImage = meta.BackgroundImage ?? "background.jpeg";
            }

            LoadEntities(Path.Combine(folder, "nodes"), def.Nodes, d => d.NodeId);
            LoadEntities(Path.Combine(folder, "combats"), def.Combats, d => d.CombatId);
            LoadEntities(Path.Combine(folder, "events"), def.Events, d => d.EventId);
            LoadEntities(Path.Combine(folder, "npcs"), def.Npcs, d => d.Id);
            LoadEntities(Path.Combine(folder, "cards"), def.Cards, d => d.Id);
            LoadEntities(Path.Combine(folder, "items"), def.Items, d => d.Id);
            LoadEntities(Path.Combine(folder, "loot"), def.Loot, d => d.Id);
            LoadEntities(Path.Combine(folder, "sprites"), def.Sprites, d => d.NpcId);

            string roadsPath = Path.Combine(folder, "roads.json");
            if (File.Exists(roadsPath))
            {
                var roads = JsonConvert.DeserializeObject<Dictionary<string, RoadDef>>(File.ReadAllText(roadsPath));
                if (roads != null) def.Roads = roads;
            }

            // Sort animation keyframes by Time
            foreach (var sprOvr in def.Sprites.Values)
            {
                if (sprOvr.AnimOverrides == null) continue;
                foreach (var animOvr in sprOvr.AnimOverrides.Values)
                    foreach (var kfList in animOvr.BoneKeyframes.Values)
                        kfList.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            return def;
        }

        private static ZonePatchDef LoadZonePatch(string folder, string targetZoneId)
        {
            var patch = new ZonePatchDef { TargetZoneId = targetZoneId };

            LoadEntities(Path.Combine(folder, "nodes"), patch.Nodes, d => d.NodeId);
            LoadEntities(Path.Combine(folder, "encounters"), patch.Encounters, d => d.CombatId);
            LoadEntities(Path.Combine(folder, "events"), patch.Events, d => d.EventId);

            string roadsPath = Path.Combine(folder, "roads.json");
            if (File.Exists(roadsPath))
            {
                var roads = JsonConvert.DeserializeObject<Dictionary<string, RoadDef>>(File.ReadAllText(roadsPath));
                if (roads != null) patch.Roads = roads;
            }

            Plugin.Log.LogInfo($"[ModProjectLoader] Loaded zone patch '{targetZoneId}': " +
                $"{patch.Nodes.Count} nodes, {patch.Encounters.Count} encounters, " +
                $"{patch.Events.Count} events, {patch.Roads.Count} roads");

            return patch;
        }
    }
}
