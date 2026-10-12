using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    internal enum AcParam { None, DamageType, AuraCurse, CardType }

    /// <summary>A ready-made aura or curse. Strength runs 1 (small) to 5 (large).</summary>
    internal sealed class AuraCursePreset
    {
        public string Name;
        public string Tip;
        public AcParam Param;
        public string DefaultParam = "";
        public Action<AuraCurseDef, int, string> Apply;
    }

    internal static class AuraCursePresets
    {
        public static readonly List<AuraCursePreset> All = Build();

        public static AuraCursePreset Find(string name) => All.FirstOrDefault(p => p.Name == name);

        private static readonly HashSet<string> Identity = new HashSet<string>
        {
            nameof(AuraCurseDef.Id), nameof(AuraCurseDef.ACName), nameof(AuraCurseDef.Description), nameof(AuraCurseDef.Sprite),
            nameof(AuraCurseDef.EffectTick), nameof(AuraCurseDef.EffectTickSides), nameof(AuraCurseDef.Sound), nameof(AuraCurseDef.SoundRework),
        };

        /// <summary>Puts every behaviour field back to its default; name, text, icon and sounds are kept.</summary>
        public static void ResetBehaviour(AuraCurseDef d)
        {
            var fresh = new AuraCurseDef();
            foreach (var f in typeof(AuraCurseDef).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Identity.Contains(f.Name)) continue;
                f.SetValue(d, f.GetValue(fresh));
            }
        }

        private static Enums.DamageType Dmg(string p, Enums.DamageType fallback)
            => Enum.TryParse<Enums.DamageType>(p, out var t) && t != Enums.DamageType.None ? t : fallback;

        // Per-strength amounts, index 0 = strength 1.
        private static float At(int s, params float[] tbl) => tbl[Math.Max(0, Math.Min(tbl.Length - 1, s - 1))];

        private static string Fmt(float v) => v == (int)v ? ((int)v).ToString() : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        private static AuraCursePreset P(string name, string tip, AcParam param, string def, Action<AuraCurseDef, int, string> apply)
            => new AuraCursePreset { Name = name, Tip = tip, Param = param, DefaultParam = def, Apply = apply };

        private static List<AuraCursePreset> Build()
        {
            return new List<AuraCursePreset>
            {
                // ── Buffs ──
                P("Buff: Damage and heal % per charge", "Aura. Each charge adds 2% x Strength to damage and healing done; decays at end of turn.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.MaxCharges = 10; d.MaxMadnessCharges = 10; d.ConsumedAtTurn = true; d.PriorityOnConsumption = 30;
                        d.AuraDamageType = Enums.DamageType.All;
                        d.AuraDamageIncreasedPercentPerStack = 2f * s;
                        d.HealDonePercentPerStack = 2 * s;
                        d.Description = "Damage and heal done <y>+<AuraDamageIncreasedPercentPerStack>%</y>.<br3>Maximum charges <y><MaxCharges></y>.";
                    }),
                P("Buff: Flat damage per charge", "Aura. Each charge adds flat damage of the chosen type; decays at end of turn.", AcParam.DamageType, "Slashing",
                    (d, s, p) =>
                    {
                        var t = Dmg(p, Enums.DamageType.Slashing);
                        d.IsAura = true; d.MaxMadnessCharges = 50; d.ConsumedAtTurn = true; d.PriorityOnConsumption = 30;
                        d.AuraDamageType = t;
                        d.AuraDamageIncreasedPerStack = At(s, 1, 1, 2, 2, 3);
                        d.Description = $"{t} damage <y>+<AuraDamageIncreasedPerStack></y>.";
                    }),
                P("Buff: Max HP per charge", "Aura. Each charge adds max HP.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.MaxMadnessCharges = 50; d.ConsumedAtTurnBegin = true; d.PriorityOnConsumption = 30;
                        d.CharacterStatModified = Enums.CharacterStat.Hp;
                        d.CharacterStatModifiedValuePerStack = 2.5f * s;
                        d.Description = "Max HP <y><CharacterStatModifiedPerStack></y>.";
                    }),
                P("Buff: Speed", "Aura. Raises speed by Strength.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.ConsumedAtTurnBegin = true;
                        d.CharacterStatModified = Enums.CharacterStat.Speed;
                        d.CharacterStatModifiedValue = s;
                        d.Description = "Speed <g>+<CharacterStatModifiedValue></g>.";
                    }),
                P("Buff: Regeneration (heal over time)", "Aura. Heals per charge at the start of each turn.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.MaxMadnessCharges = 50; d.ConsumedAtTurnBegin = true; d.PriorityOnConsumption = 30;
                        d.ProduceHealWhenConsumed = true;
                        d.HealWhenConsumedPerCharge = At(s, 0.5f, 1, 1, 1.5f, 2);
                        d.Description = "Heal<g> <ChargesMultiplier> HP</g> at the <w>start of the turn</w>.";
                    }),
                P("Buff: Shield (block each round)", "Aura. Converts its charges into block at the start of each round, then is used up.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.MaxMadnessCharges = 200; d.ConsumedAtRoundBegin = true; d.ConsumeAll = true; d.PriorityOnConsumption = 30;
                        d.GainAuraCurseConsumption = "block";
                        d.GainAuraCurseConsumptionPerCharge = (int)At(s, 1, 1, 2, 2, 3);
                        d.Description = "Gain<b> <ChargesMultiplier> Block </b>at the <w>start of the round</w>.";
                    }),
                P("Buff: Block gained per charge", "Aura. Every time block is gained, each charge adds extra block.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.MaxMadnessCharges = 50; d.ConsumedAtTurnBegin = true; d.PriorityOnConsumption = 30;
                        d.BlockChargesGainedPerStack = s;
                        d.Description = $"Block gained <y>+{s}</y> per charge.";
                    }),
                P("Buff: Thorns (reflect damage)", "Aura. Attackers take damage of the chosen type per charge when they hit the bearer.", AcParam.DamageType, "Piercing",
                    (d, s, p) =>
                    {
                        var t = Dmg(p, Enums.DamageType.Piercing);
                        d.IsAura = true; d.MaxMadnessCharges = 200; d.ConsumedAtTurnBegin = true; d.PriorityOnConsumption = 30;
                        d.DamageReflectedType = t;
                        d.DamageReflectedConsumeCharges = 1;
                        d.DamageReflectedMultiplier = (int)At(s, 1, 1, 2, 2, 3);
                        d.Description = $"Deal <y><ChargesMultiplier> {t}</y> to the attacker when this <w>target is Hit</w> by an enemy.";
                    }),
                P("Buff: Stealth", "Aura. Cannot be targeted directly; each charge adds damage and healing %.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.MaxMadnessCharges = 50; d.ConsumedAtTurnBegin = true; d.PriorityOnConsumption = 30;
                        d.Stealth = true;
                        d.AuraDamageType = Enums.DamageType.All;
                        d.AuraDamageIncreasedPercentPerStack = 4f * s;
                        d.HealDonePercentPerStack = 4 * s;
                        d.Description = "Damage and heal <y>+<AuraDamageIncreasedPercentPerStack>%</y>.<br3><b>Cannot be targeted directly by enemies</b>.";
                    }),
                P("Buff: Taunt", "Aura. Enemies must target the bearer.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.ConsumedAtTurnBegin = true;
                        d.Taunt = true;
                        d.Description = "<w>Become the target</w> of all hostile opponent cards.";
                    }),
                P("Buff: Invulnerable", "Aura. Immune to damage and harmful effects until used up.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = true; d.ConsumedAtTurnBegin = true;
                        d.Invulnerable = true;
                        d.Description = "Immune to damage and harmful effects.";
                    }),

                // ── Debuffs and curses ──
                P("Debuff: Weak (damage and heal down)", "Curse. Reduces damage and healing done by 10% x Strength; decays at end of turn.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = false; d.ConsumedAtTurn = true;
                        d.AuraDamageType = Enums.DamageType.All;
                        d.AuraDamageIncreasedPercent = -10 * s;
                        d.HealDonePercent = -10 * s;
                        d.Description = $"Damage and heal done <r>-{10 * s}%</r>.";
                    }),
                P("Debuff: Vulnerable (resistance down)", "Curse. Each charge lowers the chosen resistance.", AcParam.DamageType, "All",
                    (d, s, p) =>
                    {
                        var t = Dmg(p, Enums.DamageType.All);
                        d.IsAura = false; d.MaxCharges = 10; d.MaxMadnessCharges = 10; d.AuraConsumed = 2; d.ConsumedAtTurnBegin = true;
                        d.ResistModified = t;
                        d.ResistModifiedPercentagePerStack = -2f * s;
                        d.Description = (t == Enums.DamageType.All ? "All resistances" : $"{t} resistance") + " <r>-<ResistAux1>%</r>.<br3>Maximum charges <y><MaxCharges></y>.";
                    }),
                P("Debuff: Slow", "Curse. Each charge lowers speed.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = false; d.ConsumedAtTurnBegin = true;
                        d.CharacterStatModified = Enums.CharacterStat.Speed;
                        d.CharacterStatModifiedValuePerStack = -s;
                        d.Description = "Speed <r><CharacterStatModifiedValuePerStackTotal></r>.";
                    }),
                P("Debuff: Insane (resistance and damage down)", "Curse. Each charge lowers one resistance and damage done.", AcParam.DamageType, "Mind",
                    (d, s, p) =>
                    {
                        var t = Dmg(p, Enums.DamageType.Mind);
                        d.IsAura = false; d.MaxMadnessCharges = 100; d.ConsumedAtTurn = true;
                        d.ResistModified = t;
                        d.ResistModifiedPercentagePerStack = -s;
                        d.AuraDamageType = Enums.DamageType.All;
                        d.AuraDamageIncreasedPercentPerStack = -s;
                        d.Description = $"{t} resistance <r>-<ResistAux1>%</r>.<br3>Damage done <r>-<DamageAux1>%</r>.";
                    }),
                P("Curse: Damage over time", "Curse. Deals damage of the chosen type per charge at the start of each turn.", AcParam.DamageType, "None",
                    (d, s, p) =>
                    {
                        var t = Dmg(p, Enums.DamageType.None);
                        d.IsAura = false; d.MaxMadnessCharges = 200; d.ConsumedAtTurnBegin = true;
                        d.ProduceDamageWhenConsumed = true;
                        d.DamageWhenConsumedPerCharge = At(s, 0.5f, 1, 1, 1.5f, 2);
                        d.DamageTypeWhenConsumed = t;
                        d.Description = t == Enums.DamageType.None
                            ? "<r>Lose <DamageWhenConsumedPerCharge> HP</r> at the <w>start of the turn</w>."
                            : $"Suffer <y><ChargesMultiplier> {t} damage</y> at the <w>start of the turn</w>.";
                    }),
                P("Curse: Skip next turn", "Curse. The target skips its next turn.", AcParam.None, "",
                    (d, s, p) =>
                    {
                        d.IsAura = false; d.ConsumedAtTurn = true;
                        d.SkipsNextTurn = true;
                        d.Description = "<w>Skip the next turn.</w>";
                    }),
                P("Curse: Disable a card type", "Curse. The bearer cannot play cards of the chosen type.", AcParam.CardType, "Spell",
                    (d, s, p) =>
                    {
                        var ct = Enum.TryParse<Enums.CardType>(p, out var x) && x != Enums.CardType.None ? x : Enums.CardType.Spell;
                        d.IsAura = false; d.ConsumedAtTurn = true;
                        d.DisabledCardTypes = new[] { ct };
                        d.Description = $"Cannot play <r>{ct.ToString().Replace('_', ' ')}</r> cards.";
                    }),
                P("Curse: Turns into another effect at N charges", "Reaching N charges (3 x Strength) swaps this effect for 1 charge of the chosen aura or curse.", AcParam.AuraCurse, "",
                    (d, s, p) =>
                    {
                        d.ExplodeAtStacks = 3 * s;
                        d.ACOnExplode = p ?? "";
                        d.ACTotalChargesOnExplode = 1;
                        d.Description = $"At <y>{3 * s}</y> charges becomes <y>{p}</y>.";
                    }),
            };
        }
    }
}
