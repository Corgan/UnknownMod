using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  KEY NOTE (tooltip keyword shown on cards/traits)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class KeyNoteDef : IModEntity
    {
        /// <summary>Lookup id. Stored lowercase without spaces (the game keys KeyNotes by name without spaces).</summary>
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        /// <summary>Display name of the keyword.</summary>
        public string Name = "";
        public string Description = "";
        public string DescriptionExtended = "";
    }

    // ───────────────────────────────────────────────────────────────
    //  CINEMATIC (boss intros / endings)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CinematicDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        /// <summary>Name of a prefab under Resources/CinematicPrefabs. Custom prefabs cannot be created at runtime, so this must be an existing one.</summary>
        public string PrefabName = "";

        /// <summary>Cinematic ID to copy the background soundtrack from.</summary>
        public string SoundSource = "";
        public bool ShouldSerializeSoundSource() => !string.IsNullOrEmpty(SoundSource);

        /// <summary>Event started when the cinematic ends.</summary>
        public string EventId = "";
        public bool ShouldSerializeEventId() => !string.IsNullOrEmpty(EventId);

        /// <summary>Combat started when the cinematic ends.</summary>
        public string CombatId = "";
        public bool ShouldSerializeCombatId() => !string.IsNullOrEmpty(CombatId);

        /// <summary>Ends the adventure when the cinematic finishes.</summary>
        public bool EndAdventure = false;
    }

    // ───────────────────────────────────────────────────────────────
    //  CORRUPTION PACK (cards offered by the corruption altar)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CorruptionPackDef : IModEntity
    {
        /// <summary>Registry key. The game keys these by asset name; the lookup itself is by class + tier.</summary>
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        public string PackName = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.CardClass PackClass = Enums.CardClass.Warrior;

        public int PackTier = 0;

        public List<string> LowPackCardIds = new();
        public List<string> HighPackCardIds = new();
    }

    // ───────────────────────────────────────────────────────────────
    //  CHALLENGE TRAIT (madness / singularity modifiers)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class ChallengeTraitDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        public string Name = "";
        /// <summary>Shown under the trait name on the weekly-challenge screen (the game keeps this only in its translation files).</summary>
        public string Description = "";
        public bool ShouldSerializeDescription() => !string.IsNullOrEmpty(Description);
        public bool IsMadnessTrait = false;
        public int Order = 0;
        public bool IsSingularityTrait = false;
        public int OrderSingularity = 0;

        /// <summary>ChallengeTrait ID to copy the icon from.</summary>
        public string IconSource = "";
        public bool ShouldSerializeIconSource() => !string.IsNullOrEmpty(IconSource);
    }

    // ───────────────────────────────────────────────────────────────
    //  WEEKLY CHALLENGE
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class WeeklyChallengeDef : IModEntity
    {
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }

        public string IdSteam = "";
        public int Week = 0;
        public string Seed = "";

        /// <summary>dd-mm-yyyy. Leave empty for a non-fixed date.</summary>
        public string FromDay = "";
        /// <summary>hh:mm</summary>
        public string FromHour = "";
        /// <summary>dd-mm-yyyy</summary>
        public string ToDay = "";
        /// <summary>hh:mm</summary>
        public string ToHour = "";

        /// <summary>SubClass IDs of the four heroes.</summary>
        public string Hero1Id = "";
        public string Hero2Id = "";
        public string Hero3Id = "";
        public string Hero4Id = "";

        /// <summary>NPC IDs of the bosses.</summary>
        public string Boss1Id = "";
        public string Boss2Id = "";
        public string BossCombatId = "";
        public string LootId = "";

        /// <summary>ChallengeTrait IDs.</summary>
        public List<string> TraitIds = new();

        /// <summary>Corruption card IDs.</summary>
        public List<string> CorruptionCardIds = new();
    }
}
