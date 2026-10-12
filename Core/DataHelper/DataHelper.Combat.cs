using System.Linq;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;
using UnknownMod.Runtime;

namespace UnknownMod.Core
{
    public static partial class DataHelper
    {
        /// <summary>Create a CombatData encounter.</summary>
        public static CombatData MakeCombat(CombatDef d, NPCData[] npcList)
        {
            var combat = ScriptableObject.CreateInstance<CombatData>();
            combat.CombatId = d.CombatId;
            combat.Description = d.Description ?? "";
            combat.NPCList = npcList ?? new NPCData[0];
            combat.CombatTier = d.CombatTier;
            combat.CombatBackground = d.Background;
            combat.NpcRemoveInMadness0Index = d.NpcRemoveInMadness0Index;
            combat.HealHeroes = d.HealHeroes;
            combat.IsRift = d.IsRift;

            // StepSound (private field)
            if (d.StepSound != Enums.CombatStepSound.None)
                Traverse.Create(combat).Field("stepSound").SetValue(d.StepSound);
            combat.NeverRandomizeEnemies = d.NeverRandomizeEnemies;
            combat.RandomizeNpcPosition = d.RandomizeNpcPosition;

            // Event requirement
            if (!string.IsNullOrEmpty(d.EventRequirementId))
            {
                var req = GetEventRequirement(d.EventRequirementId);
                if (req != null) combat.EventRequirementData = req;
            }

            // Post-combat event
            if (!string.IsNullOrEmpty(d.EventDataId))
            {
                var postEvt = GetExistingEvent(d.EventDataId);
                if (postEvt != null) combat.EventData = postEvt;
            }

            // NPC summoned on kill
            if (!string.IsNullOrEmpty(d.NpcToSummonOnKilledId))
            {
                var summonNpc = GetExistingNPC(d.NpcToSummonOnKilledId);
                if (summonNpc != null) combat.NpcToSummonOnNpcKilled = summonNpc;
            }

            if (!string.IsNullOrEmpty(d.CinematicId))
                combat.CinematicData = GetCinematic(d.CinematicId);
            if (!string.IsNullOrEmpty(d.CombatBasedCorruptionCardId))
                combat.CombatBasedCorruption = GetCardSource(d.CombatBasedCorruptionCardId);
            if (!string.IsNullOrEmpty(d.ThermometerTierId))
            {
                combat.ThermometerTierData = GetThermometerTier(d.ThermometerTierId);
                if (combat.ThermometerTierData == null)
                    Plugin.Log.LogWarning($"[DataHelper] Combat '{d.CombatId}': thermometer tier '{d.ThermometerTierId}' not found");
            }

            // Combat effects
            if (d.CombatEffects != null && d.CombatEffects.Count > 0)
            {
                var effects = new CombatEffect[d.CombatEffects.Count];
                for (int i = 0; i < d.CombatEffects.Count; i++)
                {
                    effects[i] = new CombatEffect();
                    effects[i].AuraCurse = GetAuraCurse(d.CombatEffects[i].AuraCurse);
                    effects[i].AuraCurseCharges = d.CombatEffects[i].Charges;
                    effects[i].AuraCurseTarget = d.CombatEffects[i].Target;
                }
                combat.CombatEffect = effects;
            }
            else
            {
                combat.CombatEffect = new CombatEffect[0];
            }

            if (!string.IsNullOrEmpty(d.CombatMusic))
            {
                var clip = AudioUtils.Find(d.CombatMusic);
                if (clip != null) Traverse.Create(combat).Field("combatMusic").SetValue(clip);
                else Plugin.Log.LogWarning($"[DataHelper] Combat '{d.CombatId}': music '{d.CombatMusic}' not found.");
            }

            // Preserve fields from existing combat that aren't modeled in CombatDef (for patches)
            var existing = GetExistingCombat(d.CombatId);
            if (existing != null)
            {
                if (existing.ThermometerTierData != null && combat.ThermometerTierData == null)
                    combat.ThermometerTierData = existing.ThermometerTierData;

                var src = Traverse.Create(existing);
                var dst = Traverse.Create(combat);

                // CombatMusic — AudioClip, zone-overrideable per-combat music
                var music = src.Field("combatMusic").GetValue();
                if (music != null && string.IsNullOrEmpty(d.CombatMusic)) dst.Field("combatMusic").SetValue(music);

                // Patches without these fields keep the base-game values
                if (combat.CinematicData == null && string.IsNullOrEmpty(d.CinematicId))
                    combat.CinematicData = existing.CinematicData;
                if (combat.CombatBasedCorruption == null && string.IsNullOrEmpty(d.CombatBasedCorruptionCardId))
                    combat.CombatBasedCorruption = existing.CombatBasedCorruption;
            }

            return combat;
        }

    }
}
