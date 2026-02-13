using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Custom boss handler for The Mycelarch.
    /// Phase 1 (100%–60%): AICards handle card rotation normally.
    /// Phase 2 (60%–30%): Gains Powerful×2 + Thorns×3 + comic text.
    /// Phase 3 (0%-30%): Gains Fury×4, spawns 2 extra Shamblers.
    /// </summary>
    public class MycelarchBoss : BossNPC
    {
        private bool _phase2Triggered;
        private bool _phase3Triggered;
        private readonly NPC _npcRef; // own ref since base.npc is internal

        public MycelarchBoss(NPC npc) : base(npc)
        {
            _npcRef = npc;

            // Subscribe to damage events
            MatchManager.OnCharacterDamaged = (Action<Character, int, int>)Delegate.Combine(
                MatchManager.OnCharacterDamaged,
                new Action<Character, int, int>(OnCharacterDamaged));

            // Phase 1 entrance comic
            MatchManager.Instance.DoComic(_npcRef,
                "The cavern trembles. Ancient roots stir with malice.", 6f);
        }

        public override void Dispose()
        {
            base.Dispose();
            MatchManager.OnCharacterDamaged = (Action<Character, int, int>)Delegate.Remove(
                MatchManager.OnCharacterDamaged,
                new Action<Character, int, int>(OnCharacterDamaged));
        }

        public override void OnCharacterDamaged(Character character, int damage, int hpCurrent)
        {
            // Only respond to damage on Mycelarch NPCs
            if (character.NpcData == null || !character.NpcData.Id.StartsWith("myc_mycelarch"))
                return;

            int maxHp = character.GetMaxHP();
            if (maxHp <= 0) return;
            float hpPercent = (float)character.HpCurrent / (float)maxHp;

            // Phase 2: Symbiosis (at 60% HP)
            if (!_phase2Triggered && hpPercent < 0.6f)
            {
                _phase2Triggered = true;
                GameManager.Instance.StartCoroutine(Phase2Sequence(character));
            }

            // Phase 3: Total Assimilation (at 30% HP)
            if (!_phase3Triggered && hpPercent < 0.3f)
            {
                _phase3Triggered = true;
                GameManager.Instance.StartCoroutine(Phase3Sequence(character));
            }
        }

        private IEnumerator Phase2Sequence(Character boss)
        {
            MatchManager.Instance.DoComic(_npcRef,
                "The network awakens. We are one.", 5f);
            yield return new WaitForSeconds(0.5f);

            // Apply buffs
            boss.SetAuraTrait(boss, "powerful", 2);
            boss.SetAuraTrait(boss, "thorns", 3);

            // Visual feedback
            if (_npcRef?.NPCItem?.CharImageT != null)
                EffectsManager.Instance.PlayEffect("meleecastbuff",
                    _npcRef.NPCItem.CharImageT);
        }

        private IEnumerator Phase3Sequence(Character boss)
        {
            MatchManager.Instance.DoComic(_npcRef,
                "ASSIMILATE. CONSUME. BECOME.", 5f);
            yield return new WaitForSeconds(0.5f);

            // Apply fury
            boss.SetAuraTrait(boss, "fury", 4);
            boss.SetAuraTrait(boss, "fortify", 3);

            // Spawn 2 Shamblers via MatchManager
            var shambler = Globals.Instance.GetNPC("myc_shambler");
            if (shambler != null)
            {
                NPC[] team = MatchManager.Instance.GetTeamNPC();
                int spawned = 0;
                for (int i = 0; i < team.Length && spawned < 2; i++)
                {
                    if (team[i] == null || !team[i].Alive)
                    {
                        MatchManager.Instance.CreateNPC(shambler, "", i);
                        spawned++;
                    }
                }
            }

            // Visual feedback
            if (_npcRef?.NPCItem?.CharImageT != null)
                EffectsManager.Instance.PlayEffect("meleecastpoison",
                    _npcRef.NPCItem.CharImageT);
        }
    }
}
