using System;
using System.Collections;
using UnityEngine;
using UnknownMod.Scripting;

namespace MyceliumAbyss
{
    /// <summary>
    /// IBossController implementation — tells UnknownMod which NPC ID prefix
    /// this controller handles and how to create the BossNPC instance.
    /// </summary>
    public class MycelarchController : IBossController
    {
        public string NpcIdPrefix => "myc_mycelarch";

        public BossNPC CreateBossNpc(NPC npc) => new MycelarchBoss(npc);
    }

    /// <summary>
    /// Custom boss handler for The Mycelarch.
    /// Phase 1 (100%–60%): AICards handle card rotation normally.
    /// Phase 2 (60%–30%): Gains Powerful×2 + Thorns×3 + Symbiosis×6 (hops to its adds) + comic text.
    /// Phase 3 (0%–30%): Gains Fury×4, rots every hero (the rot hops between them), spawns 2 extra Shamblers.
    /// </summary>
    public class MycelarchBoss : BossNPC
    {
        private bool _phase2Triggered;
        private bool _phase3Triggered;
        private readonly NPC _npcRef;

        public MycelarchBoss(NPC npc) : base(npc)
        {
            _npcRef = npc;

            MatchManager.Instance.DoComic(_npcRef,
                "The cavern trembles. Ancient roots stir with malice.", 6f);
        }

        // The game calls this on MatchManager.BossNpc for every character whose HP changes.
        public override void OnCharacterDamaged(Character character, int damageTaken)
        {
            if (character.NpcData == null || !character.NpcData.Id.StartsWith("myc_mycelarch"))
                return;

            int maxHp = character.GetMaxHP();
            if (maxHp <= 0) return;
            float hpPercent = (float)character.HpCurrent / (float)maxHp;

            if (!_phase2Triggered && hpPercent < 0.6f)
            {
                _phase2Triggered = true;
                GameManager.Instance.StartCoroutine(Phase2Sequence(character));
            }

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
            if (!boss.Alive) yield break;

            boss.SetAuraTrait(boss, "powerful", 2);
            boss.SetAuraTrait(boss, "thorns", 3);
            // The network shares the boss's wounds: its Symbiosis heals it, then hops to an add.
            GameApi.AddAura(boss, Myc.Symbiosis, 6, boss, false);

            if (_npcRef?.NPCItem?.CharImageT != null)
                EffectsManager.Instance.PlayEffect("healerself",
                    _npcRef.NPCItem.CharImageT);
        }

        private IEnumerator Phase3Sequence(Character boss)
        {
            MatchManager.Instance.DoComic(_npcRef,
                "ASSIMILATE. CONSUME. BECOME.", 5f);
            yield return new WaitForSeconds(0.5f);
            if (!boss.Alive) yield break;

            boss.SetAuraTrait(boss, "fury", 4);
            boss.SetAuraTrait(boss, "fortify", 3);

            // Spore storm: every hero is rotted, and the rot hops between the party.
            foreach (var hero in GameApi.Heroes)
                GameApi.AddAura(hero, Myc.Rot, 4, boss, false);

            var shambler = Globals.Instance.GetNPC("myc_shambler");
            if (shambler != null)
            {
                var team = MatchManager.Instance.GetTeamNPC();
                int spawned = 0;
                for (int i = 0; i < team.Count && spawned < 2; i++)
                {
                    if (team[i] == null || !team[i].Alive)
                    {
                        MatchManager.Instance.TeamManager.CreateNPC(shambler, "", i, isSummon: true);
                        spawned++;
                    }
                }
            }

            if (_npcRef?.NPCItem?.CharImageT != null)
                EffectsManager.Instance.PlayEffect("meleecastpoison",
                    _npcRef.NPCItem.CharImageT);
        }
    }
}
