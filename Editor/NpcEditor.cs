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
    public class NpcEditor
    {
        private readonly ZoneEditor _parent;
        private int _expandedAiCard = -1;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

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

        public NpcEditor(ZoneEditor parent) => _parent = parent;

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null)
            {
                GUILayout.Label("<color=#888>No mod project active. Open the Mods tab first.</color>",
                    EditorStyles.RichLabel);
                return;
            }

            DrawModProjectPanel(proj);
        }

        /// <summary>Returns true if a change was made that needs hot-reload.</summary>
        public bool HandleChanges()
        {
            return GUI.changed && !string.IsNullOrEmpty(_parent.SelectedNpcId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Npcs.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.NpcPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                _parent.SelectedNpcId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    bool boss = false;
                    if (proj.Npcs.TryGetValue(id, out var n))
                    { name = n.Name; boss = n.IsBoss; }
                    else if (proj.NpcPatches.TryGetValue(id, out var np))
                    { name = np.Name; boss = np.IsBoss; }
                    string bossTag = boss ? " <color=#cc4444>[BOSS]</color>" : "";
                    return $"{badge} {id}  {name}{bossTag}";
                },
                "npc_sel");
            if (sel != _parent.SelectedNpcId)
            {
                _parent.SelectedNpcId = sel;
                _expandedAiCard = -1;
            }

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_npc";
                int suffix = 1;
                while (proj.Npcs.ContainsKey(newId) || proj.NpcPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_npc{suffix++}";
                var def = new NpcDef { Id = newId, Name = "New NPC" };
                proj.Npcs[newId] = def;
                _parent.SelectedNpcId = newId;
                ModProjectLoader.SaveEntity(proj, "npcs", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(_parent.SelectedNpcId))
            {
                bool isNew = proj.Npcs.ContainsKey(_parent.SelectedNpcId);
                bool isOvr = proj.NpcPatches.ContainsKey(_parent.SelectedNpcId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Npcs.Remove(_parent.SelectedNpcId);
                        ModProjectLoader.DeleteEntity(proj, "npcs", _parent.SelectedNpcId, false);
                        _parent.SelectedNpcId = allIds.FirstOrDefault(k => k != _parent.SelectedNpcId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.NpcPatches.Remove(_parent.SelectedNpcId);
                        ModProjectLoader.DeleteEntity(proj, "npcs", _parent.SelectedNpcId, true);
                        _parent.SelectedNpcId = allIds.FirstOrDefault(k => k != _parent.SelectedNpcId);
                        proj.IsDirty = true;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Override browser ─────────────────────────────────
            if (_showOverrideBrowser)
                DrawOverrideBrowser(proj);

            EditorStyles.Separator();

            // ── Resolve selected def ─────────────────────────────
            NpcDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(_parent.SelectedNpcId))
            {
                if (proj.Npcs.TryGetValue(_parent.SelectedNpcId, out d))
                    isPatch = false;
                else if (proj.NpcPatches.TryGetValue(_parent.SelectedNpcId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select an NPC above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "npcs", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game NPCs to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allNpcIds = DataHelper.GetAllNpcIds();
            int shown = 0;
            foreach (var id in allNpcIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.NpcPatches.ContainsKey(id) || proj.Npcs.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetExistingNPC(id);
                    var def = existing != null ? DataHelper.SnapshotNpc(existing) : new NpcDef { Id = id };
                    def.Id = id;
                    proj.NpcPatches[id] = def;
                    _parent.SelectedNpcId = id;
                    ModProjectLoader.SaveEntity(proj, "npcs", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(NpcDef d, ModProject proj)
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
