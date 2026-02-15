using System.Linq;
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

            return combat;
        }

    }
}
