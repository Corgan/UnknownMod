using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  PERK
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class PerkDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
        public string CustomDescription = "";

        // ── Classification ───────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardClass CardClass = Enums.CardClass.None;
        public bool MainPerk = false;
        public bool ObeliskPerk = false;

        // ── Position ─────────────────────────────────────────────
        public int Level = 0;
        public int Row = 0;

        // ── Icon ─────────────────────────────────────────────────
        public string IconTextValue = "";

        // ── Currency ─────────────────────────────────────────────
        public int AdditionalCurrency = 0;
        public int AdditionalShards = 0;

        // ── Stats ────────────────────────────────────────────────
        public int MaxHealth = 0;
        public int EnergyBegin = 0;
        public int SpeedQuantity = 0;
        public int HealQuantity = 0;

        // ── Damage Bonus ─────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType DamageFlatBonus = Enums.DamageType.None;
        public int DamageFlatBonusValue = 0;

        // ── AuraCurse Bonus ──────────────────────────────────────
        public string AuracurseBonus = "";   // AC ID
        public int AuracurseBonusValue = 0;

        // ── Resist Modification ──────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.DamageType ResistModified = Enums.DamageType.None;
        public int ResistModifiedValue = 0;

        // ── Visuals ──────────────────────────────────────────────
        /// <summary>ID of an existing PerkData to copy the icon sprite from.</summary>
        public string SpriteSource = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  PERK NODE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class PerkNodeDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        // ── Layout ───────────────────────────────────────────────
        public int Type = 0;
        public int Column = 0;
        public int Row = 0;

        // ── Flags ────────────────────────────────────────────────
        public bool LockedInTown = false;
        public bool NotStack = false;

        // ── Cost ─────────────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.PerkCost Cost = Enums.PerkCost.PerkCostBase;

        // ── References ───────────────────────────────────────────
        public string Perk = "";              // PerkData ID
        public string PerkRequired = "";      // PerkNodeData ID
        public List<string> PerksConnected = new(); // PerkNodeData IDs

        // ── Visuals ──────────────────────────────────────────────
        /// <summary>ID of an existing PerkNodeData to copy the sprite from.</summary>
        public string SpriteSource = "";
    }
}
