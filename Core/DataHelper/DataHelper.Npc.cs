using System.Collections.Generic;
using HarmonyLib;
using UnknownMod.Definitions;
using UnityEngine;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  DataHelper — NPC Builders (MakeNPC, MakeFullNpc, AICards, etc.)
    // ═══════════════════════════════════════════════════════════════

    public static partial class DataHelper
    {
        /// <summary>Create an AICards entry linking a card to NPC behavior.</summary>
        public static AICards MakeAICard(CardData card, AiCardDef d)
        {
            var ai = new AICards();
            ai.Card = card;
            ai.Priority = d.Priority;
            ai.AddCardRound = d.AddCardRound;
            ai.OnlyCastIf = d.OnlyCastIf;
            ai.ValueCastIf = d.ValueCastIf;
            ai.TargetCast = d.TargetCast;
            ai.UnitsInDeck = d.UnitsInDeck;
            ai.PercentToCast = d.PercentToCast;

            if (d.StartsAtObeliskMadnessLevel > 0)
                Traverse.Create(ai).Field("_startsAtObeliskMadnessLevel").SetValue(d.StartsAtObeliskMadnessLevel);
            if (d.StartsAtSingularityMadnessLevel > 0)
                Traverse.Create(ai).Field("_startsAtSingularityMadnessLevel").SetValue(d.StartsAtSingularityMadnessLevel);

            if (!string.IsNullOrEmpty(d.AuracurseCastIf))
                ai.AuracurseCastIf = GetAuraCurse(d.AuracurseCastIf);

            if (d.SecondOnlyCastIf != Enums.OnlyCastIf.Always)
            {
                ai.SecondOnlyCastIf = d.SecondOnlyCastIf;
                ai.SecondValueCastIf = d.SecondValueCastIf;
            }

            if (!string.IsNullOrEmpty(d.SpecialSecondTargetId))
                Traverse.Create(ai).Field("_specialSecondTargetID").SetValue(d.SpecialSecondTargetId);

            return ai;
        }

        /// <summary>Create an NPCData with the given stats.</summary>
        public static NPCData MakeNPC(NpcDef d, string spriteSource)
        {
            // Apply variant multipliers
            int hp = d.HpMult != 1f ? (int)(d.Hp * d.HpMult) : d.Hp;
            int speed = d.Speed + d.SpeedBonus;

            var npc = ScriptableObject.CreateInstance<NPCData>();
            npc.Id = d.Id;
            npc.NPCName = d.Name;
            npc.ScriptableObjectName = d.Name;
            npc.Description = d.Description ?? "";
            npc.Hp = hp;
            npc.Speed = speed;
            npc.Energy = d.Energy;
            npc.EnergyTurn = d.EnergyTurn;
            npc.CardsInHand = d.CardsInHand;

            npc.ResistSlashing = d.ResSlash + d.ResistBonus;
            npc.ResistBlunt = d.ResBlunt + d.ResistBonus;
            npc.ResistPiercing = d.ResPierce + d.ResistBonus;
            npc.ResistFire = d.ResFire + d.ResistBonus;
            npc.ResistCold = d.ResCold + d.ResistBonus;
            npc.ResistLightning = d.ResLight + d.ResistBonus;
            npc.ResistMind = d.ResMind + d.ResistBonus;
            npc.ResistHoly = d.ResHoly + d.ResistBonus;
            npc.ResistShadow = d.ResShadow + d.ResistBonus;

            npc.AICards = new AICards[0]; // set later
            npc.ExperienceReward = d.XpReward;
            npc.GoldReward = d.GoldReward;
            npc.TierReward = GetTierReward(d.TierReward);

            npc.IsBoss = d.IsBoss;
            npc.IsNamed = d.IsNamed;
            npc.FinishCombatOnDead = d.FinishCombatOnDead;
            npc.PreferredPosition = d.PreferredPos;
            npc.TierMob = d.TierMob;
            npc.Difficulty = d.Difficulty;
            npc.BigModel = d.BigModel;
            npc.Female = d.Female;
            npc.OnlyKillBossWhenHpZero = d.OnlyKillBossWhenHpZero;

            if (d.Immunities != null && d.Immunities.Count > 0)
                npc.AuracurseImmune = new List<string>(d.Immunities);
            else
                npc.AuracurseImmune = new List<string>();

            CopyVisuals(npc, spriteSource);

            // Override visual offsets if explicitly set on def
            if (d.FluffOffsetX != 0f) npc.FluffOffsetX = d.FluffOffsetX;
            if (d.FluffOffsetY != 0f) npc.FluffOffsetY = d.FluffOffsetY;
            if (d.PosBottom != 0f) npc.PosBottom = d.PosBottom;

            return npc;
        }

        /// <summary>
        /// Comprehensive NPC builder: creates NPCData, builds AI cards, wires
        /// variant chain refs. Consolidated entry point for mod-project builds.
        /// </summary>
        public static NPCData MakeFullNpc(NpcDef d)
        {
            string spriteSource = !string.IsNullOrEmpty(d.SpriteSource) ? d.SpriteSource : d.BaseNpcId;
            if (string.IsNullOrEmpty(spriteSource)) spriteSource = "yourfirstslime";

            var npc = MakeNPC(d, spriteSource);

            // Build AI cards
            if (d.AiCards != null && d.AiCards.Count > 0)
            {
                var aiList = new List<AICards>();
                foreach (var aiDef in d.AiCards)
                {
                    var cardData = GetCard(aiDef.CardId);
                    if (cardData != null)
                        aiList.Add(MakeAICard(cardData, aiDef));
                }
                npc.AICards = aiList.ToArray();
            }

            // Wire variant chain references (NPCs must already be registered)
            if (!string.IsNullOrEmpty(d.UpgradedMobId))
                npc.UpgradedMob = GetExistingNPC(d.UpgradedMobId);
            if (!string.IsNullOrEmpty(d.NgPlusMobId))
                npc.NgPlusMob = GetExistingNPC(d.NgPlusMobId);
            if (!string.IsNullOrEmpty(d.HellModeMobId))
                npc.HellModeMob = GetExistingNPC(d.HellModeMobId);
            if (!string.IsNullOrEmpty(d.BaseNpcId))
                npc.BaseMonster = GetExistingNPC(d.BaseNpcId);

            return npc;
        }

        /// <summary>
        /// Snapshot an NPCData SO back into an NpcDef DTO (for override browser).
        /// </summary>
        public static NpcDef SnapshotNpc(NPCData npc)
        {
            var d = new NpcDef();
            d.Id = npc.Id ?? "";
            d.Name = npc.NPCName ?? "";
            d.Description = npc.Description ?? "";
            d.Hp = npc.Hp;
            d.Speed = npc.Speed;
            d.Energy = npc.Energy;
            d.EnergyTurn = npc.EnergyTurn;
            d.CardsInHand = npc.CardsInHand;

            d.ResSlash = npc.ResistSlashing;
            d.ResBlunt = npc.ResistBlunt;
            d.ResPierce = npc.ResistPiercing;
            d.ResFire = npc.ResistFire;
            d.ResCold = npc.ResistCold;
            d.ResLight = npc.ResistLightning;
            d.ResMind = npc.ResistMind;
            d.ResHoly = npc.ResistHoly;
            d.ResShadow = npc.ResistShadow;

            d.XpReward = npc.ExperienceReward;
            d.GoldReward = npc.GoldReward;
            d.TierReward = npc.TierReward != null ? npc.TierReward.TierNum : 3;

            d.IsBoss = npc.IsBoss;
            d.IsNamed = npc.IsNamed;
            d.FinishCombatOnDead = npc.FinishCombatOnDead;
            d.BigModel = npc.BigModel;
            d.Female = npc.Female;
            d.OnlyKillBossWhenHpZero = npc.OnlyKillBossWhenHpZero;
            d.Difficulty = npc.Difficulty;
            d.PreferredPos = npc.PreferredPosition;
            d.TierMob = npc.TierMob;

            d.FluffOffsetX = npc.FluffOffsetX;
            d.FluffOffsetY = npc.FluffOffsetY;
            d.PosBottom = npc.PosBottom;

            // Immunities
            d.Immunities = npc.AuracurseImmune != null
                ? new List<string>(npc.AuracurseImmune)
                : new List<string>();

            // Variant chain IDs
            d.UpgradedMobId = npc.UpgradedMob != null ? npc.UpgradedMob.Id : "";
            d.NgPlusMobId = npc.NgPlusMob != null ? npc.NgPlusMob.Id : "";
            d.HellModeMobId = npc.HellModeMob != null ? npc.HellModeMob.Id : "";
            d.BaseNpcId = npc.BaseMonster != null ? npc.BaseMonster.Id : "";

            // Sprite source: use base monster or self
            d.SpriteSource = npc.BaseMonster != null ? npc.BaseMonster.Id : npc.Id;

            // Snapshot AI cards
            d.AiCards = new List<AiCardDef>();
            if (npc.AICards != null)
            {
                foreach (var ai in npc.AICards)
                {
                    if (ai == null || ai.Card == null) continue;
                    var aiDef = new AiCardDef();
                    aiDef.CardId = ai.Card.Id ?? "";
                    aiDef.Priority = ai.Priority;
                    aiDef.AddCardRound = ai.AddCardRound;
                    aiDef.UnitsInDeck = ai.UnitsInDeck;
                    aiDef.OnlyCastIf = ai.OnlyCastIf;
                    aiDef.ValueCastIf = ai.ValueCastIf;
                    aiDef.TargetCast = ai.TargetCast;
                    aiDef.PercentToCast = ai.PercentToCast;
                    aiDef.StartsAtObeliskMadnessLevel = ai.StartsAtObeliskMadnessLevel;
                    aiDef.StartsAtSingularityMadnessLevel = ai.StartsAtSingularityMadnessLevel;
                    aiDef.AuracurseCastIf = ai.AuracurseCastIf != null ? ai.AuracurseCastIf.Id : "";
                    aiDef.SecondOnlyCastIf = ai.SecondOnlyCastIf;
                    aiDef.SecondValueCastIf = ai.SecondValueCastIf;
                    aiDef.SpecialSecondTargetId = Traverse.Create(ai).Field("_specialSecondTargetID").GetValue<string>() ?? "";
                    d.AiCards.Add(aiDef);
                }
            }

            return d;
        }

        /// <summary>Create an upgraded variant of an NPC with scaled stats.</summary>
        public static NPCData MakeNPCVariant(NPCData baseNpc, string newId, string suffix,
            float hpMult, int speedBonus, int resistBonus,
            AICards[] upgradedCards, Enums.CombatTier tier,
            int xpReward, int goldReward, int tierReward)
        {
            var npc = ScriptableObject.CreateInstance<NPCData>();
            npc.Id = newId;
            npc.NPCName = baseNpc.NPCName;
            npc.ScriptableObjectName = baseNpc.ScriptableObjectName;
            npc.Hp = (int)(baseNpc.Hp * hpMult);
            npc.Speed = baseNpc.Speed + speedBonus;
            npc.Energy = baseNpc.Energy;
            npc.EnergyTurn = baseNpc.EnergyTurn;
            npc.CardsInHand = baseNpc.CardsInHand;

            npc.ResistSlashing = baseNpc.ResistSlashing + resistBonus;
            npc.ResistBlunt = baseNpc.ResistBlunt + resistBonus;
            npc.ResistPiercing = baseNpc.ResistPiercing + resistBonus;
            npc.ResistFire = baseNpc.ResistFire + resistBonus;
            npc.ResistCold = baseNpc.ResistCold + resistBonus;
            npc.ResistLightning = baseNpc.ResistLightning + resistBonus;
            npc.ResistMind = baseNpc.ResistMind + resistBonus;
            npc.ResistHoly = baseNpc.ResistHoly + resistBonus;
            npc.ResistShadow = baseNpc.ResistShadow + resistBonus;

            npc.AICards = upgradedCards ?? baseNpc.AICards;
            npc.ExperienceReward = xpReward;
            npc.GoldReward = goldReward;
            npc.TierReward = GetTierReward(tierReward);

            npc.IsBoss = baseNpc.IsBoss;
            npc.IsNamed = baseNpc.IsNamed;
            npc.FinishCombatOnDead = baseNpc.FinishCombatOnDead;
            npc.PreferredPosition = baseNpc.PreferredPosition;
            npc.TierMob = tier;
            npc.Difficulty = -1;
            npc.BigModel = baseNpc.BigModel;
            npc.Description = baseNpc.Description ?? "";
            npc.AuracurseImmune = baseNpc.AuracurseImmune ?? new List<string>();

            npc.GameObjectAnimated = baseNpc.GameObjectAnimated;
            npc.SpriteSpeed = baseNpc.SpriteSpeed;
            npc.SpritePortrait = baseNpc.SpritePortrait;
            npc.PosBottom = baseNpc.PosBottom;
            npc.FluffOffsetX = baseNpc.FluffOffsetX;
            npc.FluffOffsetY = baseNpc.FluffOffsetY;
            npc.HitSound = baseNpc.HitSound;
            Traverse.Create(npc).Field("sprite").SetValue(baseNpc.Sprite);

            return npc;
        }
    }
}
