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

            foreach (var dir in SortedDirs(DataRoot))
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
        public static ModProject Load(string modId) => LoadFrom(modId, ModFolder(modId));

        /// <summary>Files the most recent load could not read, as "file: reason". A file listed here is missing from that project, not deleted.</summary>
        public static List<string> LastLoadProblems { get; private set; } = new();

        /// <summary>Load a project from any folder laid out like a mod (used to open snapshots).</summary>
        public static ModProject LoadFrom(string modId, string root)
        {
            var proj = new ModProject { ModId = modId };
            LastLoadProblems = new List<string>();

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
            LoadEntities(Path.Combine(root, "keynotes"), proj.KeyNotes, d => d.Id);
            LoadEntities(Path.Combine(root, "cinematics"), proj.Cinematics, d => d.Id);
            LoadEntities(Path.Combine(root, "corruptionpacks"), proj.CorruptionPacks, d => d.Id);
            LoadEntities(Path.Combine(root, "challengetraits"), proj.ChallengeTraits, d => d.Id);
            LoadEntities(Path.Combine(root, "weeklychallenges"), proj.WeeklyChallenges, d => d.Id);
            LoadEntities(Path.Combine(root, "visualgraphs"), proj.VisualGraphs, d => d.Id);
            LoadEntities(Path.Combine(root, "packs"), proj.Packs, d => d.PackId);
            LoadEntities(Path.Combine(root, "cardplayerpacks"), proj.CardPlayerPacks, d => d.PackId);
            LoadEntities(Path.Combine(root, "cardplayerpairspacks"), proj.CardPlayerPairsPacks, d => d.PackId);
            LoadEntities(Path.Combine(root, "herodata"), proj.HeroDataEntries, d => d.Id);
            LoadEntities(Path.Combine(root, "backgrounds"), proj.Backgrounds, d => d.BackgroundId);
            LoadEntities(Path.Combine(root, "events"), proj.Events, d => d.EventId);
            LoadEntities(Path.Combine(root, "combats"), proj.Combats, d => d.CombatId);
            LoadEntities(Path.Combine(root, "thermometers"), proj.Thermometers, d => d.Id);
            LoadEntities(Path.Combine(root, "thermometertiers"), proj.ThermometerTiers, d => d.Id);
            LoadEntities(Path.Combine(root, "bettingteams"), proj.BettingTeams, d => d.TeamName);
            LoadEntities(Path.Combine(root, "memorysequences"), proj.MemorySequences, d => d.ZoneId);
            LoadEntities(Path.Combine(root, "cardpairrewards"), proj.CardPairRewards, d => d.Id);
            LoadEntities(Path.Combine(root, "rulesets"), proj.RuleSets, d => d.Id);
            LoadEntities(Path.Combine(root, "economies"), proj.Economies, d => d.Id);

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
            LoadEntities(Path.Combine(patches, "keynotes"), proj.KeyNotePatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "cinematics"), proj.CinematicPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "corruptionpacks"), proj.CorruptionPackPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "challengetraits"), proj.ChallengeTraitPatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "weeklychallenges"), proj.WeeklyChallengePatches, d => d.Id);
            LoadEntities(Path.Combine(patches, "visualgraphs"), proj.VisualGraphPatches, d => d.Id);
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
                foreach (var zoneDir in SortedDirs(zonesDir))
                {
                    string dirName = Path.GetFileName(zoneDir);
                    if (dirName == "_patches") continue;

                    string zoneJsonPath = Path.Combine(zoneDir, "zone.json");
                    if (!File.Exists(zoneJsonPath)) continue;

                    try
                    {
                        var zoneDef = LoadZoneFromFolder(zoneDir);
                        if (!string.IsNullOrEmpty(zoneDef.ZoneId))
                        {
                            if (proj.Zones.ContainsKey(zoneDef.ZoneId))
                            {
                                Plugin.Log.LogWarning($"[ModProjectLoader] Duplicate zone id '{zoneDef.ZoneId}' in mod '{modId}' (folder '{dirName}'); keeping the first and skipping this one.");
                                LastLoadProblems.Add($"zones/{dirName}: duplicate zone id '{zoneDef.ZoneId}'");
                            }
                            else
                                proj.Zones[zoneDef.ZoneId] = zoneDef;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[ModProjectLoader] Failed to load zone '{dirName}' in mod '{modId}': {ex.Message}");
                        LastLoadProblems.Add($"zones/{dirName}: {ex.Message}");
                    }
                }
            }

            // ── Zone patches ─────────────────────────────────────────
            string zonePatchDir = Path.Combine(zonesDir, "_patches");
            if (Directory.Exists(zonePatchDir))
            {
                foreach (var patchDir in SortedDirs(zonePatchDir))
                {
                    string targetZoneId = Path.GetFileName(patchDir);
                    try
                    {
                        proj.ZonePatches[targetZoneId] = LoadZonePatch(patchDir, targetZoneId);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[ModProjectLoader] Failed to load zone patch '{targetZoneId}' in mod '{modId}': {ex.Message}");
                        LastLoadProblems.Add($"zones/_patches/{targetZoneId}: {ex.Message}");
                    }
                }
            }

            int totalNew = proj.Cards.Count + proj.Loot.Count +
                           proj.Npcs.Count + proj.AuraCurses.Count + proj.Heroes.Count +
                           proj.Traits.Count + proj.Skins.Count + proj.Perks.Count +
                           proj.PerkNodes.Count + proj.Requirements.Count +
                           proj.Cardbacks.Count + proj.TierRewards.Count +
                           proj.Packs.Count + proj.CardPlayerPacks.Count +
                           proj.CardPlayerPairsPacks.Count + proj.HeroDataEntries.Count +
                           proj.Backgrounds.Count + proj.VisualGraphs.Count +
                           proj.Events.Count + proj.Combats.Count +
                           proj.Thermometers.Count + proj.ThermometerTiers.Count +
                           proj.BettingTeams.Count + proj.MemorySequences.Count + proj.CardPairRewards.Count +
                           proj.KeyNotes.Count + proj.Cinematics.Count + proj.CorruptionPacks.Count +
                           proj.ChallengeTraits.Count + proj.WeeklyChallenges.Count + proj.RuleSets.Count + proj.Economies.Count;

            int totalPatches = proj.CardPatches.Count +
                               proj.LootPatches.Count + proj.NpcPatches.Count +
                               proj.AuraCursePatches.Count + proj.HeroPatches.Count +
                               proj.TraitPatches.Count + proj.SkinPatches.Count +
                               proj.PerkPatches.Count + proj.PerkNodePatches.Count +
                               proj.RequirementPatches.Count + proj.CardbackPatches.Count +
                               proj.TierRewardPatches.Count +
                               proj.PackPatches.Count + proj.CardPlayerPackPatches.Count +
                               proj.CardPlayerPairsPackPatches.Count + proj.HeroDataPatches.Count +
                               proj.EventPatches.Count + proj.CombatPatches.Count +
                               proj.BackgroundPatches.Count + proj.VisualGraphPatches.Count +
                               proj.KeyNotePatches.Count + proj.CinematicPatches.Count + proj.CorruptionPackPatches.Count +
                               proj.ChallengeTraitPatches.Count + proj.WeeklyChallengePatches.Count;

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
            if (proj.MetadataUnreadable)
            {
                // The file on disk could not be parsed; keep a copy so a typo in it is never silently lost
                try
                {
                    string broken = MetadataPath(proj.ModId);
                    if (File.Exists(broken))
                    {
                        string backup = broken + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(broken, backup, true);
                        Plugin.Log.LogWarning($"[ModProjectLoader] {proj.ModId}.json was unreadable; saved a copy as {Path.GetFileName(backup)} before replacing it");
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[ModProjectLoader] Could not back up the unreadable metadata: {ex.Message}"); }
                proj.MetadataUnreadable = false;
            }
            Directory.CreateDirectory(Path.Combine(root, "textures"));

            var meta = new
            {
                modId = proj.ModId,
                modName = proj.ModName,
                author = proj.Author,
                version = proj.Version,
                description = proj.Description,
                depends = proj.Dependencies.Count > 0 ? proj.Dependencies : null,
                starterNodeId = string.IsNullOrEmpty(proj.StarterNodeId) ? null : proj.StarterNodeId
            };
            AtomicWrite(MetadataPath(proj.ModId), JsonConvert.SerializeObject(meta, _json));
        }

        /// <summary>Save a single entity to the correct folder.</summary>
        public static void SaveEntity<T>(ModProject proj, string subfolder, string filename, T entity, bool isPatch = false)
        {
            if (!SafeOrReport(filename)) return;
            string root = ModFolder(proj.ModId);
            string folder = isPatch
                ? Path.Combine(root, "_patches", subfolder)
                : Path.Combine(root, subfolder);

            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{filename}.json");
            AtomicWrite(path, JsonConvert.SerializeObject(entity, _json));
        }

        /// <summary>Delete an entity JSON file.</summary>
        public static bool DeleteEntity(ModProject proj, string subfolder, string filename, bool isPatch = false)
        {
            if (!IsSafeName(filename)) return false;
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
            if (!SafeOrReport(zoneId) || !SafeOrReport(filename)) return;
            string folder = Path.Combine(ModFolder(proj.ModId), "zones", zoneId, subfolder);
            Directory.CreateDirectory(folder);
            AtomicWrite(Path.Combine(folder, $"{filename}.json"), JsonConvert.SerializeObject(entity, _json));
        }

        /// <summary>Save a zone patch entity.</summary>
        public static void SaveZonePatchEntity<T>(ModProject proj, string targetZoneId, string subfolder, string filename, T entity)
        {
            if (!SafeOrReport(targetZoneId) || !SafeOrReport(filename)) return;
            string folder = Path.Combine(ModFolder(proj.ModId), "zones", "_patches", targetZoneId, subfolder);
            Directory.CreateDirectory(folder);
            AtomicWrite(Path.Combine(folder, $"{filename}.json"), JsonConvert.SerializeObject(entity, _json));
        }

        /// <summary>Persist the zone patch's id-generation state (prefix and next node number).</summary>
        public static void SaveZonePatchMeta(ModProject proj, ZonePatchDef patch)
        {
            if (!SafeOrReport(patch.TargetZoneId)) return;
            string folder = Path.Combine(ModFolder(proj.ModId), "zones", "_patches", patch.TargetZoneId);
            Directory.CreateDirectory(folder);
            var meta = new { patch.TargetZoneId, patch.DetectedPrefix, patch.NextNodeNumber };
            AtomicWrite(Path.Combine(folder, "patch.json"), JsonConvert.SerializeObject(meta, _json));
        }

        /// <summary>Save the roads.json for a zone or zone patch.</summary>
        public static void SaveRoads(ModProject proj, string zoneId, Dictionary<string, RoadDef> roads, bool isPatch = false)
        {
            if (!SafeOrReport(zoneId)) return;
            string folder = isPatch
                ? Path.Combine(ModFolder(proj.ModId), "zones", "_patches", zoneId)
                : Path.Combine(ModFolder(proj.ModId), "zones", zoneId);

            Directory.CreateDirectory(folder);
            AtomicWrite(Path.Combine(folder, "roads.json"), JsonConvert.SerializeObject(roads, _json));
        }

        /// <summary>Save a full zone definition (metadata + all sub-entities).</summary>
        public static void SaveZone(ModProject proj, ZoneDef zone)
        {
            if (!SafeOrReport(zone.ZoneId)) return;
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
                RoadsOffsetY = zone.RoadsOffsetY,
                CameraBoundsMinX = zone.CameraBoundsMinX,
                CameraBoundsMinY = zone.CameraBoundsMinY,
                CameraBoundsMaxX = zone.CameraBoundsMaxX,
                CameraBoundsMaxY = zone.CameraBoundsMaxY
            };
            AtomicWrite(Path.Combine(zoneDir, "zone.json"), JsonConvert.SerializeObject(meta, _json));

            string nodesDir = Path.Combine(zoneDir, "nodes");
            SaveAllEntities(nodesDir, zone.Nodes, d => d.NodeId);
            PruneStaleJson(nodesDir, zone.Nodes.Select(kv => string.IsNullOrEmpty(kv.Value.NodeId) ? kv.Key : kv.Value.NodeId));

            string roadsPath = Path.Combine(zoneDir, "roads.json");
            if (zone.Roads.Count > 0)
                AtomicWrite(roadsPath, JsonConvert.SerializeObject(zone.Roads, _json));
            else if (File.Exists(roadsPath))
                File.Delete(roadsPath);
        }

        /// <summary>Deletes *.json files in a folder whose names are not in the keep list (entities removed in the editor).</summary>
        internal static void PruneStaleJson(string folder, IEnumerable<string> keepNames)
        {
            if (!Directory.Exists(folder)) return;
            var keep = new HashSet<string>(keepNames, StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                if (keep.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                try { File.Delete(file); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[ProjectLoader] Could not delete stale {file}: {ex.Message}"); }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CREATE NEW MOD
        // ═══════════════════════════════════════════════════════════════

        /// <summary>True when the id can be used as a file or folder name without leaving its folder.</summary>
        public static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == "..") return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            // Windows drops trailing spaces and dots from folder names, so the id and its folder would disagree
            if (name[name.Length - 1] == ' ' || name[name.Length - 1] == '.') return false;
            int dot = name.IndexOf('.');
            string stem = (dot < 0 ? name : name.Substring(0, dot)).ToUpperInvariant();
            if (ReservedDeviceNames.Contains(stem)) return false;
            return true;
        }

        /// <summary>New mod ids are letters, digits and '_' only: generated entity ids embed the mod id, and the game
        /// normalises ids in type-specific ways (spaces, apostrophes, '_' and '-' are all special somewhere).</summary>
        public static bool IsValidNewModId(string modId)
        {
            if (string.IsNullOrEmpty(modId) || !IsSafeName(modId)) return false;
            foreach (char ch in modId)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_')) return false;
            return true;
        }

        /// <summary>The mod id as a prefix for generated entity ids: whitespace and apostrophes dropped, since registration
        /// strips them but most game lookups only lowercase.</summary>
        public static string IdPrefix(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return "";
            var sb = new System.Text.StringBuilder(modId.Length);
            foreach (char ch in modId)
                if (!char.IsWhiteSpace(ch) && ch != '\'') sb.Append(ch);
            return sb.ToString();
        }

        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        // Backstop for every disk path built from an id: refuse and report instead of writing outside the entity folder
        private static bool SafeOrReport(string name)
        {
            if (IsSafeName(name)) return true;
            Plugin.Log.LogError($"[ModProjectLoader] Refused unsafe file name '{name}'.");
            ReportWrite(name ?? "", new ArgumentException($"'{name}' cannot be used as a file name"));
            return false;
        }

        /// <summary>Writes the renamed entity first and only then removes the old file, so a failed write never loses it. A case-only rename overwrites the same file.</summary>
        public static void RenameEntity<T>(ModProject proj, string subfolder, string oldId, string newId, T entity, bool isPatch)
        {
            bool caseOnly = string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase);
            SaveEntity(proj, subfolder, newId, entity, isPatch);
            if (!caseOnly) DeleteEntity(proj, subfolder, oldId, isPatch);
        }

        /// <summary>Create a new empty mod project on disk and return it.</summary>
        public static ModProject CreateNew(string modId, string modName, string author)
        {
            if (!IsValidNewModId(modId)) throw new ArgumentException($"'{modId}' is not a valid mod id (letters, digits and '_' only)");
            if (Directory.Exists(ModFolder(modId)) || File.Exists(MetadataPath(modId)))
                throw new InvalidOperationException($"A mod with id '{modId}' already exists");
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

        /// <summary>Write via temp file + replace so a crash mid-write can never leave truncated JSON.</summary>
        internal static void AtomicWrite(string path, string content)
        {
            try
            {
                WriteAtomic(path, content);
            }
            catch (Exception ex)
            {
                ReportWrite(path, ex);
                throw;
            }
            ReportWrite(path, null);
        }

        private static void ReportWrite(string path, Exception error)
        {
            try
            {
                if (error == null) UnknownMod.Editor.EditorStatus.Saved(path);
                else UnknownMod.Editor.EditorStatus.Failed(path, error);
            }
            catch { /* status is cosmetic; it is unavailable outside the game (offline tooling) */ }
        }

        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            HotReload.NoteWrite(path, content);
            File.WriteAllText(tmp, content);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException || ex is NotSupportedException)
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }

        private static string[] SortedFiles(string folder, string pattern)
        {
            var files = Directory.GetFiles(folder, pattern);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static string[] SortedDirs(string folder)
        {
            var dirs = Directory.GetDirectories(folder);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            return dirs;
        }

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
                proj.StarterNodeId = (string)jo["starterNodeId"] ?? "";
                var deps = jo["depends"] as Newtonsoft.Json.Linq.JArray;
                if (deps != null)
                    foreach (var d in deps)
                    {
                        string dep = (string)d;
                        if (!string.IsNullOrWhiteSpace(dep)) proj.Dependencies.Add(dep.Trim());
                    }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ModProjectLoader] Failed to read metadata: {ex.Message}");
                LastLoadProblems.Add($"{proj.ModId}.json: {ex.Message}");
                proj.MetadataUnreadable = true;
            }
        }

        /// <summary>
        /// Loads leniently but reports unknown JSON keys (typos, fields the mod does not support) instead of
        /// silently dropping them. Other deserialization errors still fail the file.
        /// </summary>
        private static T DeserializeReportingUnknownKeys<T>(string file)
        {
            // Ignore explicit nulls so collection initialisers survive ("Replies": null)
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Ignore
            };
            settings.Error = (sender, args) =>
            {
                var msg = args.ErrorContext.Error?.Message;
                if (msg != null && msg.StartsWith("Could not find member"))
                {
                    Plugin.Log.LogWarning($"[ModProjectLoader] {Path.GetFileName(file)}: unknown field ignored - {msg}");
                    args.ErrorContext.Handled = true;
                }
            };
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(file), settings);
        }

        private static void LoadEntities<T>(string folder, Dictionary<string, T> dict, Func<T, string> getKey)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in SortedFiles(folder, "*.json"))
            {
                try
                {
                    var entity = DeserializeReportingUnknownKeys<T>(file);
                    if (entity != null)
                    {
                            if (entity is CharacterVisualDef visualGraph) visualGraph.Normalize();

                        string key = getKey(entity);
                        if (!string.IsNullOrEmpty(key))
                        {
                            if (dict.ContainsKey(key))
                                Plugin.Log.LogWarning($"[ModProjectLoader] Duplicate id '{key}' in '{file}' - overriding earlier definition");
                            dict[key] = entity;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[ModProjectLoader] Failed to load '{file}': {ex.Message}");
                    LastLoadProblems.Add($"{Path.GetFileName(Path.GetDirectoryName(file))}/{Path.GetFileName(file)}: {ex.Message}");
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
            foreach (var subDir in SortedDirs(folder))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith("_") || dirName.StartsWith(".")) continue;
                LoadCardsRecursive(subDir, dict);
            }
        }

        /// <summary>Save a card to the correct semantic subfolder under cards/.</summary>
        public static void SaveCard(ModProject proj, CardDef card, bool isPatch = false)
        {
            if (!SafeOrReport(card.Id)) return;
            string root = ModFolder(proj.ModId);
            string cardsRoot = isPatch
                ? Path.Combine(root, "_patches", "cards")
                : Path.Combine(root, "cards");

            string json = JsonConvert.SerializeObject(card, _json);
            string folder = Path.Combine(cardsRoot, card.SemanticFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{card.Id}.json");
            // SemanticFolder may have changed: write the new file first, then remove the old one, so a failed write never loses the card
            var old = new List<string>();
            FindCardFiles(cardsRoot, card.Id, old);
            AtomicWrite(path, json);
            string dest = Path.GetFullPath(path);
            foreach (var f in old)
                if (!string.Equals(Path.GetFullPath(f), dest, StringComparison.OrdinalIgnoreCase))
                    File.Delete(f);
        }

        private static void FindCardFiles(string folder, string cardId, List<string> found)
        {
            if (!Directory.Exists(folder)) return;
            string path = Path.Combine(folder, $"{cardId}.json");
            if (File.Exists(path)) found.Add(path);
            foreach (var subDir in SortedDirs(folder))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith("_") || dirName.StartsWith(".")) continue; // ignored backups are not cards
                FindCardFiles(subDir, cardId, found);
            }
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
            if (!Directory.Exists(folder)) return false;
            bool deleted = false;
            string path = Path.Combine(folder, $"{cardId}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted = true;
            }
            // Delete every duplicate so a leftover copy cannot resurrect the card on reload
            foreach (var subDir in SortedDirs(folder))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith("_") || dirName.StartsWith(".")) continue; // ignored backups are not cards
                if (DeleteCardRecursive(subDir, cardId))
                    deleted = true;
            }
            return deleted;
        }

        private static void SaveAllEntities<T>(string folder, Dictionary<string, T> dict, Func<T, string> getKey)
        {
            if (dict.Count == 0) return;
            Directory.CreateDirectory(folder);
            foreach (var kvp in dict)
            {
                string filename = getKey(kvp.Value);
                if (string.IsNullOrEmpty(filename)) filename = kvp.Key;
                if (!SafeOrReport(filename)) continue;
                AtomicWrite(
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
                def = JsonConvert.DeserializeObject<ZoneDef>(File.ReadAllText(zonePath),
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }) ?? new ZoneDef();
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

            string metaPath = Path.Combine(folder, "patch.json");
            if (File.Exists(metaPath))
            {
                var meta = JsonConvert.DeserializeObject<ZonePatchDef>(File.ReadAllText(metaPath),
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                if (meta != null)
                {
                    patch.DetectedPrefix = meta.DetectedPrefix ?? "";
                    patch.NextNodeNumber = meta.NextNodeNumber;
                }
            }

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
