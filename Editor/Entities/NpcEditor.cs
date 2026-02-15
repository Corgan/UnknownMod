using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for editing NPC definitions at the mod-project level.
    /// Supports creating new NPCs and overriding base-game ones.
    /// </summary>
    public class NpcEditor : ModProjectEditorBase<NpcDef>
    {
        protected override string TypeLabel => "NPC";
        protected override string FolderName => "npcs";
        protected override string NewIdSuffix => "_new_npc";

        private int _expandedAiCard = -1;

        public override string SelectedId
        {
            get => Parent.SelectedNpcId;
            set
            {
                Parent.SelectedNpcId = value;
                _expandedAiCard = -1;
            }
        }

        protected override Dictionary<string, NpcDef> GetNewDict(ModProject proj) => proj.Npcs;
        protected override Dictionary<string, NpcDef> GetPatchDict(ModProject proj) => proj.NpcPatches;

        protected override NpcDef CreateDefault(string id, ModProject proj)
            => new NpcDef { Id = id, Name = "New NPC" };

        protected override string GetDisplayName(NpcDef def)
        {
            string bossTag = def.IsBoss ? " <color=#cc4444>[BOSS]</color>" : "";
            return $"{def.Name}{bossTag}";
        }

        protected override NpcDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetExistingNPC(id);
            return existing != null ? DataHelper.SnapshotNpc(existing) : null;
        }

        public NpcEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secStats = true;
        private bool _secResist = false;
        private bool _secRewards = false;
        private bool _secFlags = true;
        private bool _secVisual = false;
        private bool _secVariants = false;
        private bool _secImmunities = false;
        private bool _secAiCards = true;

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        protected override void DrawAllSections(NpcDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
            {
                string desc = BuildNpcDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.Name = EditorFields.TextField("Name", d.Name);
                d.Description = EditorFields.TextField("Description", d.Description);

                // SpriteSource: base-game NPC IDs
                var spriteChoices = DataHelper.GetAllNpcIds();
                d.SpriteSource = EditorFields.IdDropdown("Sprite Src", d.SpriteSource, spriteChoices, "npc_spritesrc");
            }

            // ── Stats ────────────────────────────────────────────
            if (EditorFields.Section("Stats", ref _secStats))
            {
                d.Hp = EditorFields.IntField("HP", d.Hp);
                d.Speed = EditorFields.IntField("Speed", d.Speed);
                d.Energy = EditorFields.IntField("Energy", d.Energy);
                d.EnergyTurn = EditorFields.IntField("Energy/Turn", d.EnergyTurn);
                d.CardsInHand = EditorFields.IntField("Cards/Hand", d.CardsInHand);
            }

            // ── Resistances ──────────────────────────────────────
            if (EditorFields.Section("Resistances", ref _secResist))
            {
                d.ResSlash  = EditorFields.IntField("Slash",  d.ResSlash);
                d.ResBlunt  = EditorFields.IntField("Blunt",  d.ResBlunt);
                d.ResPierce = EditorFields.IntField("Pierce", d.ResPierce);
                d.ResFire   = EditorFields.IntField("Fire",   d.ResFire);
                d.ResCold   = EditorFields.IntField("Cold",   d.ResCold);
                d.ResLight  = EditorFields.IntField("Light",  d.ResLight);
                d.ResMind   = EditorFields.IntField("Mind",   d.ResMind);
                d.ResHoly   = EditorFields.IntField("Holy",   d.ResHoly);
                d.ResShadow = EditorFields.IntField("Shadow", d.ResShadow);
            }

            // ── Rewards ──────────────────────────────────────────
            if (EditorFields.Section("Rewards", ref _secRewards))
            {
                d.XpReward   = EditorFields.IntField("XP",   d.XpReward);
                d.GoldReward = EditorFields.IntField("Gold", d.GoldReward);
                d.TierReward = EditorFields.IntField("Tier", d.TierReward);
            }

            // ── Flags ────────────────────────────────────────────
            if (EditorFields.Section("Flags", ref _secFlags))
            {
                d.IsBoss = EditorFields.Toggle("Is Boss", d.IsBoss);
                d.IsNamed = EditorFields.Toggle("Is Named", d.IsNamed);
                d.FinishCombatOnDead = EditorFields.Toggle("Finish on Dead", d.FinishCombatOnDead);
                d.BigModel = EditorFields.Toggle("Big Model", d.BigModel);
                d.Female = EditorFields.Toggle("Female", d.Female);
                d.OnlyKillBossWhenHpZero = EditorFields.Toggle("Only Kill Boss at 0 HP", d.OnlyKillBossWhenHpZero);
                d.Difficulty = EditorFields.IntField("Difficulty", d.Difficulty);
                d.PreferredPos = EditorFields.EnumField("Position", d.PreferredPos, "npc_pos");
                d.TierMob = EditorFields.EnumField("Tier Mob", d.TierMob, "npc_tier");
            }

            // ── Visual Offsets ───────────────────────────────────
            if (EditorFields.Section("Visual Offsets", ref _secVisual))
            {
                d.FluffOffsetX = EditorFields.FloatField("Fluff Offset X", d.FluffOffsetX);
                d.FluffOffsetY = EditorFields.FloatField("Fluff Offset Y", d.FluffOffsetY);
                d.PosBottom = EditorFields.FloatField("Pos Bottom", d.PosBottom);
            }

            // ── Variant Chain ────────────────────────────────────
            if (EditorFields.Section("Variant Chain", ref _secVariants))
            {
                // Combine mod + base-game NPC IDs for variant dropdowns
                var npcIds = new List<string>();
                npcIds.AddRange(proj.Npcs.Keys.OrderBy(k => k));
                npcIds.AddRange(proj.NpcPatches.Keys.OrderBy(k => k));
                npcIds.AddRange(DataHelper.GetAllNpcIds());
                npcIds = npcIds.Distinct().ToList();

                d.UpgradedMobId = EditorFields.IdDropdown("Upgraded (_b)", d.UpgradedMobId, npcIds, "npc_upg");
                d.NgPlusMobId   = EditorFields.IdDropdown("NG+ (_plus)",   d.NgPlusMobId,   npcIds, "npc_ng");
                d.HellModeMobId = EditorFields.IdDropdown("Hell Mode",     d.HellModeMobId, npcIds, "npc_hell");
                d.BaseNpcId     = EditorFields.IdDropdown("Base NPC",      d.BaseNpcId,     npcIds, "npc_base");

                GUILayout.Space(4);
                GUILayout.Label("<color=#888>Variant generation (if IS variant):</color>", EditorStyles.RichLabel);
                d.HpMult = EditorFields.FloatField("HP Mult", d.HpMult);
                d.SpeedBonus = EditorFields.IntField("Speed Bonus", d.SpeedBonus);
                d.ResistBonus = EditorFields.IntField("Resist Bonus", d.ResistBonus);
            }

            // ── Immunities ───────────────────────────────────────
            if (EditorFields.Section($"Immunities ({d.Immunities.Count})", ref _secImmunities))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                for (int i = 0; i < d.Immunities.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.Immunities[i] = EditorFields.IdDropdown("", d.Immunities[i], acIds, $"npc_immune_{i}");
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.Immunities.RemoveAt(i);
                        GUI.changed = true;
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Immunity", EditorStyles.MiniButton, GUILayout.Width(90)))
                {
                    d.Immunities.Add("");
                    GUI.changed = true;
                }
            }

            // ── AI Cards ─────────────────────────────────────────
            if (EditorFields.Section($"AI Cards ({d.AiCards.Count})", ref _secAiCards))
            {
                // Card dropdown from mod-project + base game
                var cardIds = new List<string>();
                cardIds.AddRange(proj.Cards.Keys.OrderBy(k => k));
                cardIds.AddRange(proj.CardPatches.Keys.OrderBy(k => k));
                cardIds.AddRange(DataHelper.GetAllCardIds());
                cardIds = cardIds.Distinct().ToList();

                for (int i = 0; i < d.AiCards.Count; i++)
                {
                    var ai = d.AiCards[i];
                    bool expanded = _expandedAiCard == i;
                    string hdr = $"{(expanded ? "\u25BC" : "\u25B6")} {ai.CardId} (P{ai.Priority}, R{ai.AddCardRound}+)";
                    if (GUILayout.Button(hdr, EditorStyles.ListItem))
                        _expandedAiCard = expanded ? -1 : i;

                    if (!expanded) continue;

                    GUILayout.BeginVertical(EditorStyles.CompactBox);

                    ai.CardId = EditorFields.IdDropdown("Card", ai.CardId, cardIds, $"ai_card_{i}");
                    ai.Priority = EditorFields.IntField("Priority", ai.Priority);
                    ai.AddCardRound = EditorFields.IntField("Add Round", ai.AddCardRound);
                    ai.UnitsInDeck = EditorFields.IntField("Units/Deck", ai.UnitsInDeck);
                    ai.OnlyCastIf = EditorFields.EnumField("Only Cast If", ai.OnlyCastIf, $"ai_castif_{i}");
                    ai.ValueCastIf = EditorFields.FloatField("Value If", ai.ValueCastIf);
                    ai.TargetCast = EditorFields.EnumField("Target Cast", ai.TargetCast, $"ai_tgt_{i}");
                    ai.PercentToCast = EditorFields.FloatField("% to Cast", ai.PercentToCast);
                    ai.StartsAtObeliskMadnessLevel = EditorFields.IntField("Min Obelisk Madness", ai.StartsAtObeliskMadnessLevel);
                    ai.StartsAtSingularityMadnessLevel = EditorFields.IntField("Min Singularity Madness", ai.StartsAtSingularityMadnessLevel);

                    var acIds = DataHelper.GetAllAuraCurseIds();
                    ai.AuracurseCastIf = EditorFields.IdDropdown("AC Cast If", ai.AuracurseCastIf, acIds, $"ai_ac_{i}");
                    ai.SecondOnlyCastIf = EditorFields.EnumField("2nd Cast If", ai.SecondOnlyCastIf, $"ai_castif2_{i}");
                    ai.SecondValueCastIf = EditorFields.FloatField("2nd Value If", ai.SecondValueCastIf);

                    GUILayout.Space(2);
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton))
                    {
                        d.AiCards.RemoveAt(i);
                        _expandedAiCard = -1;
                        GUI.changed = true;
                        break;
                    }

                    GUILayout.EndVertical();
                }

                GUILayout.Space(2);
                if (GUILayout.Button("+ AI Card", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    d.AiCards.Add(new AiCardDef
                    {
                        CardId = cardIds.Count > 0 ? cardIds[0] : "",
                        Priority = 5
                    });
                    _expandedAiCard = d.AiCards.Count - 1;
                    GUI.changed = true;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildNpcDescription(NpcDef d)
        {
            var sb = new StringBuilder();

            // Header
            sb.Append($"<b>{d.Name}</b>");
            if (d.IsBoss) sb.Append("  <color=#cc4444>[BOSS]</color>");
            if (d.IsNamed) sb.Append("  <color=#ffcc44>[NAMED]</color>");
            sb.Append($"  <color=#aaa>{d.TierMob}</color>");

            // Stats
            sb.Append($"\n<color=#44cc44>HP {d.Hp}</color>  Spd {d.Speed}  Energy {d.Energy}  Cards {d.CardsInHand}");

            // Resistances (only non-zero)
            var resists = new List<string>();
            if (d.ResSlash != 0) resists.Add($"Sl:{d.ResSlash}");
            if (d.ResBlunt != 0) resists.Add($"Bl:{d.ResBlunt}");
            if (d.ResPierce != 0) resists.Add($"Pi:{d.ResPierce}");
            if (d.ResFire != 0) resists.Add($"Fi:{d.ResFire}");
            if (d.ResCold != 0) resists.Add($"Co:{d.ResCold}");
            if (d.ResLight != 0) resists.Add($"Li:{d.ResLight}");
            if (d.ResMind != 0) resists.Add($"Mi:{d.ResMind}");
            if (d.ResHoly != 0) resists.Add($"Ho:{d.ResHoly}");
            if (d.ResShadow != 0) resists.Add($"Sh:{d.ResShadow}");
            if (resists.Count > 0)
                sb.Append($"\n<color=#88aacc>Resist: {string.Join(" ", resists)}</color>");

            // Rewards
            if (d.XpReward > 0 || d.GoldReward > 0)
                sb.Append($"\nXP {d.XpReward}  Gold {d.GoldReward}  Tier {d.TierReward}");

            // Immunities
            if (d.Immunities.Count > 0)
                sb.Append($"\n<color=#88ccff>Immune: {string.Join(", ", d.Immunities)}</color>");

            // AI cards count
            if (d.AiCards.Count > 0)
                sb.Append($"\n<color=#dd88ff>{d.AiCards.Count} AI card(s)</color>");

            // Flags
            var flags = new List<string>();
            if (d.FinishCombatOnDead) flags.Add("FinishOnDead");
            if (d.BigModel) flags.Add("BigModel");
            if (d.Female) flags.Add("Female");
            if (d.OnlyKillBossWhenHpZero) flags.Add("OnlyKillAt0HP");
            if (d.PreferredPos != Enums.CardTargetPosition.Anywhere)
                flags.Add($"Pos:{d.PreferredPos}");
            if (flags.Count > 0)
                sb.Append($"\n<color=#888>{string.Join(" | ", flags)}</color>");

            return sb.ToString();
        }
    }
}
