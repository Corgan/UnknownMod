using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  COMBAT ENCOUNTER
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CombatDef : IModEntity
    {
        public string CombatId = "";
        [JsonIgnore] public string EntityId { get => CombatId; set => CombatId = value; }
        public string Description = "";
        public List<string> NpcIds = new();

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatTier CombatTier = Enums.CombatTier.T3;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatBackground Background = Enums.CombatBackground.Spider_Lair;

        public int NpcRemoveInMadness0Index = -1;
        public bool HealHeroes = false;
        public bool IsRift = false;

        /// <summary>Prevent randomizing enemy positions in this combat.</summary>
        public bool NeverRandomizeEnemies = false;
        public bool ShouldSerializeNeverRandomizeEnemies() => NeverRandomizeEnemies;

        /// <summary>Allow NPC positions to be randomized.</summary>
        public bool RandomizeNpcPosition = false;
        public bool ShouldSerializeRandomizeNpcPosition() => RandomizeNpcPosition;

        /// <summary>EventRequirement ID that gates this combat.</summary>
        public string EventRequirementId = "";
        public bool ShouldSerializeEventRequirementId() => !string.IsNullOrEmpty(EventRequirementId);

        /// <summary>NPC ID summoned when a monster in this combat is killed.</summary>
        public string NpcToSummonOnKilledId = "";

        /// <summary>Event triggered after combat ends (post-combat event).</summary>
        public string EventDataId = "";

        /// <summary>Aura/curse effects applied at combat start.</summary>
        public List<CombatEffectDef> CombatEffects = new();
    }

    // ───────────────────────────────────────────────────────────────
    //  COMBAT EFFECT (start-of-combat aura/curse)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CombatEffectDef
    {
        public string AuraCurse = "";
        public int Charges = 0;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatUnit Target = Enums.CombatUnit.Heroes;
    }
}
