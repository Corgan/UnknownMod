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
        protected override EntityPicker.Mode? PickerMode => EntityPicker.Mode.NPC;

        private int _expandedAiCard = -1;

        // ── Variant grid state ────────────────────────────────────────
        /// <summary>0=Normal, 1=NG+, 2=Hell Mode</summary>
        private int _tierTab = 0;
        /// <summary>0=Standard, 1=Upgraded (_b)</summary>
        private int _upgradedTab = 0;
        private string _prevVariantId;

        private static readonly string[] _tierLabels = { "Normal", "NG+", "Hell Mode" };
        private static readonly string[] _upgradedLabels = { "Standard", "Upgraded" };

        /// <summary>Longest-first for correct suffix stripping in list filtering.</summary>
        private static readonly string[] _variantCheckSuffixes = { "_plush_b", "_plus_b", "_plush", "_plus", "_b" };

        /// <summary>Map (tier, upgraded) → suffix. Empty string = base NPC.</summary>
        private static string GetVariantSuffix(int tier, int upgraded)
        {
            if (tier == 0) return upgraded == 0 ? "" : "_b";
            if (tier == 1) return upgraded == 0 ? "_plus" : "_plus_b";
            if (tier == 2) return upgraded == 0 ? "_plush" : "_plush_b";
            return "";
        }

        private bool IsEditingVariant => _tierTab != 0 || _upgradedTab != 0;

        public override string SelectedId
        {
            get => Parent.SelectedNpcId;
            set
            {
                Parent.SelectedNpcId = value;
                _expandedAiCard = -1;
                _tierTab = 0;
                _upgradedTab = 0;
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

        // ── Variant list filtering ─────────────────────────────
        protected override List<string> FilterEntityList(List<string> allIds, ModProject proj)
        {
            var idSet = new HashSet<string>(allIds);
            return allIds.Where(id => !IsProjectVariant(id, idSet)).ToList();
        }

        private static bool IsProjectVariant(string id, HashSet<string> projectIds)
        {
            foreach (var suffix in _variantCheckSuffixes)
            {
                if (id.EndsWith(suffix))
                {
                    string baseId = id.Substring(0, id.Length - suffix.Length);
                    if (projectIds.Contains(baseId))
                        return true;
                }
            }
            return false;
        }

        //  Collapsible section state 
        private bool _secIdentity = true;
        private bool _secStats = true;
        private bool _secResist = false;
        private bool _secRewards = false;
        private bool _secFlags = true;
        private bool _secVisual = false;
        private bool _secVariants = false;
        private bool _secImmunities = false;
        private bool _secAiCards = true;

        // ── SpriteSkin picker state ─────────────────────────────
        // (uses IdDropdown + EntityPicker now)

        // 
        //  ALL SECTIONS
        // 

        protected override void DrawAllSections(NpcDef d, ModProject proj)
        {
            // ── Variant tabs ─────────────────────────────────────────────
            DrawVariantTabBar(d, proj);
            GUILayout.Space(4);

            if (IsEditingVariant)
            {
                string suffix = GetVariantSuffix(_tierTab, _upgradedTab);
                string variantId = d.Id + suffix;
                var vDef = FindProjectNpc(variantId, proj, out _);
                if (vDef == null)
                {
                    DrawCreateVariantUI(d, variantId, suffix, proj);
                    return;
                }
                _prevVariantId = vDef.Id;
                d = vDef;
            }

            //  Identity 
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.Name = EditorFields.TextField("Name", d.Name);
                d.Description = EditorFields.TextField("Description", d.Description);

                // SpriteSource: base-game NPC whose model/skeleton to clone
                var spriteChoices = EditorFields.CachedIds("npc", DataHelper.GetAllNpcIds);
                d.SpriteSource = EditorFields.IdDropdown("Sprite Src", d.SpriteSource, spriteChoices, "npc_spritesrc", pickerMode: EntityPicker.Mode.NPC);

                // SpriteSkinId: optional SpriteSkin override (NPC skins only)
                var skinIds = proj.SpriteSkins
                    .Where(kvp => kvp.Value.SkinTarget == SkinTargetType.NPC)
                    .Select(kvp => kvp.Key)
                    .Concat(proj.SpriteSkinPatches
                        .Where(kvp => kvp.Value.SkinTarget == SkinTargetType.NPC)
                        .Select(kvp => kvp.Key))
                    .Distinct().OrderBy(k => k).ToList();
                d.SpriteSkinId = EditorFields.IdDropdown("SpriteSkin", d.SpriteSkinId, skinIds, "npc_spriteskin",
                    pickerMode: EntityPicker.Mode.SpriteSkin);
            }

            //  Stats 
            if (EditorFields.Section("Stats", ref _secStats))
            {
                d.Hp = EditorFields.IntFieldMin("HP", d.Hp, 1);
                d.Speed = EditorFields.IntFieldMin("Speed", d.Speed, 0);
                d.Energy = EditorFields.IntField("Energy", d.Energy, 0, 10);
                d.EnergyTurn = EditorFields.IntField("Energy/Turn", d.EnergyTurn, 0, 10);
                d.CardsInHand = EditorFields.IntFieldMin("Cards/Hand", d.CardsInHand, 1);
            }

            //  Resistances 
            if (EditorFields.Section("Resistances", ref _secResist))
            {
                EditorFields.ResistGrid(
                    ref d.ResSlash,  ref d.ResBlunt,  ref d.ResPierce,
                    ref d.ResFire,   ref d.ResCold,   ref d.ResLight,
                    ref d.ResMind,   ref d.ResHoly,   ref d.ResShadow);
            }

            //  Rewards 
            if (EditorFields.Section("Rewards", ref _secRewards))
            {
                d.XpReward   = EditorFields.IntFieldMin("XP",   d.XpReward, 0);
                d.GoldReward = EditorFields.IntFieldMin("Gold", d.GoldReward, 0);
                d.TierReward = EditorFields.IntFieldMin("Tier", d.TierReward, 0);
            }

            //  Flags 
            if (EditorFields.Section("Flags", ref _secFlags))
            {
                d.IsBoss = EditorFields.Toggle("Is Boss", d.IsBoss);
                d.IsNamed = EditorFields.Toggle("Is Named", d.IsNamed);
                d.FinishCombatOnDead = EditorFields.Toggle("Finish on Dead", d.FinishCombatOnDead);
                d.BigModel = EditorFields.Toggle("Big Model", d.BigModel);
                d.Female = EditorFields.Toggle("Female", d.Female);
                d.OnlyKillBossWhenHpZero = EditorFields.Toggle("Only Kill Boss at 0 HP", d.OnlyKillBossWhenHpZero);
                d.Difficulty = EditorFields.IntField("Difficulty", d.Difficulty, -1, 99);
                d.PreferredPos = EditorFields.EnumField("Position", d.PreferredPos, "npc_pos");
                d.TierMob = EditorFields.EnumField("Tier Mob", d.TierMob, "npc_tier");
            }

            //  Visual Offsets 
            if (EditorFields.Section("Visual Offsets", ref _secVisual))
            {
                d.FluffOffsetX = EditorFields.FloatField("Fluff Offset X", d.FluffOffsetX);
                d.FluffOffsetY = EditorFields.FloatField("Fluff Offset Y", d.FluffOffsetY);
                d.PosBottom = EditorFields.FloatField("Pos Bottom", d.PosBottom);
            }

            //  Variant Chain 
            if (EditorFields.Section("Variant Chain", ref _secVariants))
            {
                // Combine mod + base-game NPC IDs for variant dropdowns
                var npcIds = new List<string>();
                npcIds.AddRange(proj.Npcs.Keys.OrderBy(k => k));
                npcIds.AddRange(proj.NpcPatches.Keys.OrderBy(k => k));
                npcIds.AddRange(EditorFields.CachedIds("npc", DataHelper.GetAllNpcIds));
                npcIds = npcIds.Distinct().ToList();

                d.UpgradedMobId = EditorFields.IdDropdown("Upgraded (_b)", d.UpgradedMobId, npcIds, "npc_upg", pickerMode: EntityPicker.Mode.NPC);
                d.NgPlusMobId   = EditorFields.IdDropdown("NG+ (_plus)",   d.NgPlusMobId,   npcIds, "npc_ng", pickerMode: EntityPicker.Mode.NPC);
                d.HellModeMobId = EditorFields.IdDropdown("Hell Mode",     d.HellModeMobId, npcIds, "npc_hell", pickerMode: EntityPicker.Mode.NPC);
                d.BaseNpcId     = EditorFields.IdDropdown("Base NPC",      d.BaseNpcId,     npcIds, "npc_base", pickerMode: EntityPicker.Mode.NPC);

                GUILayout.Space(4);
                GUILayout.Label("<color=#888>Variant generation (if IS variant):</color>", EditorStyles.RichLabel);
                d.HpMult = EditorFields.FloatField("HP Mult", d.HpMult);
                d.SpeedBonus = EditorFields.IntField("Speed Bonus", d.SpeedBonus);
                d.ResistBonus = EditorFields.IntField("Resist Bonus", d.ResistBonus);
            }

            //  Immunities 
            if (EditorFields.Section($"Immunities ({d.Immunities.Count})", ref _secImmunities))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                for (int i = 0; i < d.Immunities.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.Immunities[i] = EditorFields.IdDropdown("", d.Immunities[i], acIds, $"npc_immune_{i}", pickerMode: EntityPicker.Mode.AuraCurse);
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.Immunities.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
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

            //  AI Cards 
            if (EditorFields.Section($"AI Cards ({d.AiCards.Count})", ref _secAiCards))
            {
                var cardIds = EditorFields.BuildCardIdList(proj);

                for (int i = 0; i < d.AiCards.Count; i++)
                {
                    var ai = d.AiCards[i];
                    bool expanded = _expandedAiCard == i;
                    string hdr = $"{(expanded ? "\u25BC" : "\u25B6")} {ai.CardId} (P{ai.Priority}, R{ai.AddCardRound}+)";
                    if (GUILayout.Button(hdr, EditorStyles.ListItem))
                        _expandedAiCard = expanded ? -1 : i;

                    if (!expanded) continue;

                    GUILayout.BeginVertical(EditorStyles.CompactBox);

                    ai.CardId = EditorFields.IdDropdown("Card", ai.CardId, cardIds, $"ai_card_{i}", pickerMode: EntityPicker.Mode.Card);
                    ai.Priority = EditorFields.IntFieldMin("Priority", ai.Priority, 0);
                    ai.AddCardRound = EditorFields.IntFieldMin("Add Round", ai.AddCardRound, 0);
                    ai.UnitsInDeck = EditorFields.IntFieldMin("Units/Deck", ai.UnitsInDeck, 1);
                    ai.PercentToCast = EditorFields.PercentSlider("% to Cast", ai.PercentToCast);
                    ai.TargetCast = EditorFields.EnumField("Target Cast", ai.TargetCast, $"ai_tgt_{i}");
                    ai.StartsAtObeliskMadnessLevel = EditorFields.IntField("Min Obelisk Madness", ai.StartsAtObeliskMadnessLevel);
                    ai.StartsAtSingularityMadnessLevel = EditorFields.IntField("Min Singularity Madness", ai.StartsAtSingularityMadnessLevel);

                    GUILayout.Space(4);
                    GUILayout.Label("<color=#aaa>Cast Conditions:</color>", EditorStyles.RichLabel);
                    ai.OnlyCastIf = EditorFields.EnumField("Only Cast If", ai.OnlyCastIf, $"ai_castif_{i}");

                    // Show ValueCastIf only for threshold-based conditions
                    bool needsValue = ai.OnlyCastIf != Enums.OnlyCastIf.Always
                        && ai.OnlyCastIf != Enums.OnlyCastIf.TeamNpcAllAlive
                        && ai.OnlyCastIf != Enums.OnlyCastIf.TargetHasAnyAura
                        && ai.OnlyCastIf != Enums.OnlyCastIf.TargetHasAnyCurse
                        && ai.OnlyCastIf != Enums.OnlyCastIf.TargetNotIllusion;
                    if (needsValue)
                        ai.ValueCastIf = EditorFields.FloatField("Value If", ai.ValueCastIf);

                    // Show AC reference only for AC-based conditions
                    bool needsAC = ai.OnlyCastIf == Enums.OnlyCastIf.TargetHasAuraCurse
                        || ai.OnlyCastIf == Enums.OnlyCastIf.TargetHasNotAuraCurse;
                    if (needsAC)
                    {
                        var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                        ai.AuracurseCastIf = EditorFields.IdDropdown("AC Cast If", ai.AuracurseCastIf, acIds, $"ai_ac_{i}", pickerMode: EntityPicker.Mode.AuraCurse);
                    }

                    // Show 2nd target only for HP comparison conditions
                    bool needsTarget = ai.OnlyCastIf == Enums.OnlyCastIf.LifeInMainTargetHigherThanInSpecialTarget
                        || ai.OnlyCastIf == Enums.OnlyCastIf.LifeInMainTargetLessThanInSpecialTarget;
                    if (needsTarget)
                    {
                        var npcIds = EditorFields.CachedIds("npc", DataHelper.GetAllNpcIds);
                        ai.SpecialSecondTargetId = EditorFields.IdDropdown("2nd Target NPC", ai.SpecialSecondTargetId, npcIds, $"ai_2nd_{i}", pickerMode: EntityPicker.Mode.NPC);
                    }

                    // Secondary condition (AND)
                    ai.SecondOnlyCastIf = EditorFields.EnumField("AND Cast If", ai.SecondOnlyCastIf, $"ai_castif2_{i}");
                    if (ai.SecondOnlyCastIf != Enums.OnlyCastIf.Always)
                        ai.SecondValueCastIf = EditorFields.FloatField("2nd Value If", ai.SecondValueCastIf);

                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    if (i > 0 && GUILayout.Button("▲", EditorStyles.MiniButton, GUILayout.Width(24)))
                    {
                        var tmp = d.AiCards[i]; d.AiCards[i] = d.AiCards[i - 1]; d.AiCards[i - 1] = tmp;
                        _expandedAiCard = i - 1; GUI.changed = true;
                        GUILayout.EndHorizontal(); GUILayout.EndVertical(); break;
                    }
                    if (i < d.AiCards.Count - 1 && GUILayout.Button("▼", EditorStyles.MiniButton, GUILayout.Width(24)))
                    {
                        var tmp = d.AiCards[i]; d.AiCards[i] = d.AiCards[i + 1]; d.AiCards[i + 1] = tmp;
                        _expandedAiCard = i + 1; GUI.changed = true;
                        GUILayout.EndHorizontal(); GUILayout.EndVertical(); break;
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        d.AiCards.RemoveAt(i);
                        _expandedAiCard = -1;
                        GUI.changed = true;
                        GUILayout.EndHorizontal(); GUILayout.EndVertical(); break;
                    }
                    GUILayout.EndHorizontal();

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

            // ── Variant auto-save ─────────────────────────────────────
            if (IsEditingVariant && GUI.changed)
            {
                if (d.Id != _prevVariantId && !string.IsNullOrEmpty(d.Id))
                {
                    bool wasPatch = proj.NpcPatches.ContainsKey(_prevVariantId);
                    var dict = wasPatch ? proj.NpcPatches : proj.Npcs;
                    dict.Remove(_prevVariantId);
                    dict[d.Id] = d;
                    ModProjectLoader.DeleteEntity(proj, "npcs", _prevVariantId, wasPatch);
                }
                bool isPatch = proj.NpcPatches.ContainsKey(d.Id);
                ModProjectLoader.SaveEntity(proj, "npcs", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═════════════════════════════════════════════════════
        //  VARIANT TAB HELPERS
        // ═════════════════════════════════════════════════════

        private void DrawVariantTabBar(NpcDef d, ModProject proj)
        {
            // Check which variants exist
            bool plusExists = VariantExists(d.Id, "_plus", proj);

            // Row 1: Difficulty tier
            GUILayout.BeginHorizontal();
            for (int t = 0; t < _tierLabels.Length; t++)
            {
                bool active = _tierTab == t;
                // Hell mode requires _plus to exist (despair needs NG+)
                bool enabled = t < 2 || plusExists;

                string suffix = GetVariantSuffix(t, _upgradedTab);
                bool exists = string.IsNullOrEmpty(suffix) || VariantExists(d.Id, suffix, proj);

                string label = _tierLabels[t];
                string text;
                if (!enabled) text = $"<color=#333>{label}</color>";
                else if (active) text = $"<b><color=cyan>{label}</color></b>";
                else if (exists) text = label;
                else text = $"<color=#555>{label}</color>";

                var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
                if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)) && enabled)
                {
                    _tierTab = t;
                    _expandedAiCard = -1;
                }
            }
            GUILayout.EndHorizontal();

            // Row 2: Upgraded toggle
            GUILayout.BeginHorizontal();
            for (int u = 0; u < _upgradedLabels.Length; u++)
            {
                bool active = _upgradedTab == u;

                string suffix = GetVariantSuffix(_tierTab, u);
                bool exists = string.IsNullOrEmpty(suffix) || VariantExists(d.Id, suffix, proj);

                string label = _upgradedLabels[u];
                string text;
                if (active) text = $"<b><color=cyan>{label}</color></b>";
                else if (exists) text = label;
                else text = $"<color=#555>{label}</color>";

                var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
                if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                {
                    _upgradedTab = u;
                    _expandedAiCard = -1;
                }
            }
            GUILayout.EndHorizontal();
        }

        private static bool VariantExists(string baseId, string suffix, ModProject proj)
        {
            string id = baseId + suffix;
            return proj.Npcs.ContainsKey(id) || proj.NpcPatches.ContainsKey(id);
        }

        private void DrawCreateVariantUI(NpcDef baseDef, string variantId, string suffix, ModProject proj)
        {
            string displayName = $"{_tierLabels[_tierTab]} / {_upgradedLabels[_upgradedTab]}";
            GUILayout.Space(20);
            GUILayout.Label($"<color=#888>No <b>{displayName}</b> variant (<b>{suffix}</b>) exists for <b>{baseDef.Id}</b>.</color>",
                EditorStyles.RichLabel);
            GUILayout.Space(8);

            if (GUILayout.Button($"Create {suffix} Variant",
                EditorStyles.MiniButton, GUILayout.Width(180)))
            {
                // Clone base via JSON round-trip
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(baseDef,
                    Newtonsoft.Json.Formatting.None,
                    new Newtonsoft.Json.JsonSerializerSettings { DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include });
                var variant = Newtonsoft.Json.JsonConvert.DeserializeObject<NpcDef>(json);
                variant.Id = variantId;
                variant.BaseNpcId = baseDef.Id;
                variant.UpgradedMobId = "";
                variant.NgPlusMobId = "";
                variant.HellModeMobId = "";

                proj.Npcs[variantId] = variant;

                // Auto-link on base / intermediary variants
                AutoLinkVariant(baseDef, variant, _tierTab, _upgradedTab, proj);

                SaveNpcIfInProject(variant, proj);
                SaveNpcIfInProject(baseDef, proj);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        /// <summary>
        /// Auto-link the new variant into the chain based on the game's sequential swap logic:
        /// 1. Tier2 → UpgradedMob  2. NG+ → NgPlusMob  3. Despair → HellModeMob
        /// </summary>
        private void AutoLinkVariant(NpcDef baseDef, NpcDef variant, int tier, int upgraded, ModProject proj)
        {
            // _b (Normal/Upgraded) → base.UpgradedMobId = variant
            if (tier == 0 && upgraded == 1)
            {
                baseDef.UpgradedMobId = variant.Id;
            }
            // _plus (NG+/Standard) → base.NgPlusMobId = variant
            else if (tier == 1 && upgraded == 0)
            {
                baseDef.NgPlusMobId = variant.Id;
            }
            // _plus_b (NG+/Upgraded) → _plus.UpgradedMobId + _b.NgPlusMobId = variant
            else if (tier == 1 && upgraded == 1)
            {
                var plusDef = FindProjectNpc(baseDef.Id + "_plus", proj, out _);
                if (plusDef != null) { plusDef.UpgradedMobId = variant.Id; SaveNpcIfInProject(plusDef, proj); }
                var bDef = FindProjectNpc(baseDef.Id + "_b", proj, out _);
                if (bDef != null) { bDef.NgPlusMobId = variant.Id; SaveNpcIfInProject(bDef, proj); }
            }
            // _plush (Hell/Standard) → _plus.HellModeMobId = variant (only _plus, NOT _plus_b)
            else if (tier == 2 && upgraded == 0)
            {
                var plusDef = FindProjectNpc(baseDef.Id + "_plus", proj, out _);
                if (plusDef != null) { plusDef.HellModeMobId = variant.Id; SaveNpcIfInProject(plusDef, proj); }
            }
            // _plush_b (Hell/Upgraded) → _plush.UpgradedMobId + _plus_b.HellModeMobId = variant
            else if (tier == 2 && upgraded == 1)
            {
                var plushDef = FindProjectNpc(baseDef.Id + "_plush", proj, out _);
                if (plushDef != null) { plushDef.UpgradedMobId = variant.Id; SaveNpcIfInProject(plushDef, proj); }
                var plusBDef = FindProjectNpc(baseDef.Id + "_plus_b", proj, out _);
                if (plusBDef != null) { plusBDef.HellModeMobId = variant.Id; SaveNpcIfInProject(plusBDef, proj); }
            }
        }

        private NpcDef FindProjectNpc(string id, ModProject proj, out bool isPatch)
        {
            if (proj.Npcs.TryGetValue(id, out var d)) { isPatch = false; return d; }
            if (proj.NpcPatches.TryGetValue(id, out d)) { isPatch = true; return d; }
            isPatch = false;
            return null;
        }

        private void SaveNpcIfInProject(NpcDef def, ModProject proj)
        {
            bool isPatch = proj.NpcPatches.ContainsKey(def.Id);
            ModProjectLoader.SaveEntity(proj, "npcs", def.Id, def, isPatch);
        }

        // 
        //  LIVE DESCRIPTION BUILDER
        // 

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
