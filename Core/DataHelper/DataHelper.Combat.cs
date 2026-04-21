using System.Linq;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

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

            // Preserve fields from existing combat that aren't modeled in CombatDef (for patches)
            var existing = GetExistingCombat(d.CombatId);
            if (existing != null)
            {
                if (existing.ThermometerTierData != null)
                    combat.ThermometerTierData = existing.ThermometerTierData;

                var src = Traverse.Create(existing);
                var dst = Traverse.Create(combat);

                // CombatMusic — AudioClip, zone-overrideable per-combat music
                var music = src.Field("combatMusic").GetValue();
                if (music != null) dst.Field("combatMusic").SetValue(music);

                // CinematicData — boss intro cinematics
                var cine = src.Field("cinematicData").GetValue();
                if (cine != null) dst.Field("cinematicData").SetValue(cine);
            }

            return combat;
        }

    }
}
