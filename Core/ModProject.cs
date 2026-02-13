using System.Collections.Generic;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Top-level state container for a single mod project.
    /// Holds all new content + overrides of base-game entities + zones.
    /// One ModProject is active in the editor at a time; all are loaded at runtime.
    /// </summary>
    public class ModProject
    {
        // ── Metadata ─────────────────────────────────────────────
        public string ModId = "";
        public string ModName = "";
        public string Author = "";
        public string Version = "1.0.0";
        public string Description = "";

        /// <summary>Mod IDs this mod depends on (must load before this one).</summary>
        public List<string> Dependencies = new();

        // ── New content (keyed by entity ID) ─────────────────────

        public Dictionary<string, CardDef> Cards = new();
        public Dictionary<string, ItemDef> Items = new();
        public Dictionary<string, LootDef> Loot = new();
        public Dictionary<string, NpcDef> Npcs = new();
        public Dictionary<string, AuraCurseDef> AuraCurses = new();
        public Dictionary<string, HeroDef> Heroes = new();
        public Dictionary<string, TraitDef> Traits = new();
        public Dictionary<string, SkinDef> Skins = new();
        public Dictionary<string, PerkDef> Perks = new();
        public Dictionary<string, PerkNodeDef> PerkNodes = new();
        public Dictionary<string, RequirementDef> Requirements = new();
        public Dictionary<string, CardbackDef> Cardbacks = new();
        public Dictionary<string, TierRewardDef> TierRewards = new();
        public Dictionary<string, SpriteOverrideDef> Sprites = new();

        // ── Overrides of base-game entities (stored in _patches/) ─

        public Dictionary<string, CardDef> CardPatches = new();
        public Dictionary<string, ItemDef> ItemPatches = new();
        public Dictionary<string, LootDef> LootPatches = new();
        public Dictionary<string, NpcDef> NpcPatches = new();
        public Dictionary<string, AuraCurseDef> AuraCursePatches = new();
        public Dictionary<string, HeroDef> HeroPatches = new();
        public Dictionary<string, TraitDef> TraitPatches = new();
        public Dictionary<string, SkinDef> SkinPatches = new();
        public Dictionary<string, PerkDef> PerkPatches = new();
        public Dictionary<string, PerkNodeDef> PerkNodePatches = new();
        public Dictionary<string, RequirementDef> RequirementPatches = new();
        public Dictionary<string, CardbackDef> CardbackPatches = new();
        public Dictionary<string, TierRewardDef> TierRewardPatches = new();
        public Dictionary<string, SpriteOverrideDef> SpritePatches = new();

        // ── Zones ────────────────────────────────────────────────

        /// <summary>New custom zones (full zone data).</summary>
        public Dictionary<string, ZoneDef> Zones = new();

        /// <summary>Patches to base-game zones.</summary>
        public Dictionary<string, ZonePatchDef> ZonePatches = new();

        // ── Dirty tracking ───────────────────────────────────────

        /// <summary>Has unsaved changes.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsDirty { get; set; }

        /// <summary>Timestamp of last change (for auto-save debounce).</summary>
        [Newtonsoft.Json.JsonIgnore]
        public float LastChangeTime { get; set; }
    }
}
