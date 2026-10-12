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

        /// <summary>Custom background ID (from zone's Backgrounds dict). Overrides Background enum when set.</summary>
        public string CustomBackgroundId = "";
        public bool ShouldSerializeCustomBackgroundId() => !string.IsNullOrEmpty(CustomBackgroundId);

        public int NpcRemoveInMadness0Index = -1;
        public bool HealHeroes = false;
        public bool IsRift = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CombatStepSound StepSound = Enums.CombatStepSound.None;
        public bool ShouldSerializeStepSound() => StepSound != Enums.CombatStepSound.None;

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

        /// <summary>CinematicData ID played for this combat (boss intros).</summary>
        public string CinematicId = "";
        public bool ShouldSerializeCinematicId() => !string.IsNullOrEmpty(CinematicId);

        /// <summary>Card ID applied as the combat-based corruption.</summary>
        public string CombatBasedCorruptionCardId = "";
        public bool ShouldSerializeCombatBasedCorruptionCardId() => !string.IsNullOrEmpty(CombatBasedCorruptionCardId);

        /// <summary>Thermometer tier (per-round gold/xp/card bonus curve). Empty keeps the base-game value, or none for new combats.</summary>
        public string ThermometerTierId = "";
        public bool ShouldSerializeThermometerTierId() => !string.IsNullOrEmpty(ThermometerTierId);

        /// <summary>Audio clip name (game clip or file in the mod's audio/ folder) played as the combat music. Blank keeps the base value.</summary>
        public string CombatMusic = "";
        public bool ShouldSerializeCombatMusic() => !string.IsNullOrEmpty(CombatMusic);

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
