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
    /// IMGUI panel for editing hero (SubClass) definitions at the mod-project level.
    /// Supports creating new heroes and overriding base-game ones.
    /// </summary>
    public class HeroEditor
    {
        private readonly ModEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secClass = true;
        private bool _secStats = true;
        private bool _secResist = false;
        private bool _secVisual = false;
        private bool _secItem = false;
        private bool _secMaxHp = false;
        private bool _secCards = true;
        private bool _secSingularity = false;
        private bool _secTraits = false;
        private bool _secPacks = false;

        public HeroEditor(ModEditor parent) => _parent = parent;

        public string SelectedHeroId { get; set; }

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
            return GUI.changed && !string.IsNullOrEmpty(SelectedHeroId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Heroes.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.HeroPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                SelectedHeroId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.Heroes.TryGetValue(id, out var h))
                        name = h.SubClassName;
                    else if (proj.HeroPatches.TryGetValue(id, out var hp))
                        name = hp.SubClassName;
                    return $"{badge} {id}  {name}";
                },
                "hero_sel");
            if (sel != SelectedHeroId)
                SelectedHeroId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_hero";
                int suffix = 1;
                while (proj.Heroes.ContainsKey(newId) || proj.HeroPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_hero{suffix++}";
                var def = new HeroDef
                {
                    Id = newId,
                    SubClassName = "New Hero",
                    CharacterName = "New Hero",
                    Hp = 40,
                    Speed = 5,
                    Energy = 3,
                    EnergyTurn = 3,
                    Blocked = false,
                };
                proj.Heroes[newId] = def;
                SelectedHeroId = newId;
                ModProjectLoader.SaveEntity(proj, "heroes", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
                _showOverrideBrowser = !_showOverrideBrowser;

            // Delete (new) / Revert (override)
            if (!string.IsNullOrEmpty(SelectedHeroId))
            {
                bool isNew = proj.Heroes.ContainsKey(SelectedHeroId);
                bool isOvr = proj.HeroPatches.ContainsKey(SelectedHeroId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Heroes.Remove(SelectedHeroId);
                        ModProjectLoader.DeleteEntity(proj, "heroes", SelectedHeroId, false);
                        SelectedHeroId = allIds.FirstOrDefault(k => k != SelectedHeroId);
                        proj.IsDirty = true;
                    }
                }
                else if (isOvr)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.HeroPatches.Remove(SelectedHeroId);
                        ModProjectLoader.DeleteEntity(proj, "heroes", SelectedHeroId, true);
                        SelectedHeroId = allIds.FirstOrDefault(k => k != SelectedHeroId);
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
            HeroDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(SelectedHeroId))
            {
                if (proj.Heroes.TryGetValue(SelectedHeroId, out d))
                    isPatch = false;
                else if (proj.HeroPatches.TryGetValue(SelectedHeroId, out d))
                    isPatch = true;
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a hero above, or create / override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            // ── Draw all sections ────────────────────────────────
            DrawAllSections(d, proj);

            // ── Auto-save ────────────────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "heroes", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Search base-game heroes (subclasses) to override:</color>",
                EditorStyles.RichLabel);
            _overrideFilter = EditorFields.TextField("Filter", _overrideFilter);

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(180));
            string filterLow = (_overrideFilter ?? "").ToLower();
            var allIds = DataHelper.GetAllSubClassIds();
            int shown = 0;
            foreach (var id in allIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.HeroPatches.ContainsKey(id) || proj.Heroes.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    var existing = DataHelper.GetSubClass(id);
                    var def = existing != null ? DataHelper.SnapshotHero(existing) : new HeroDef { Id = id };
                    def.Id = id;
                    proj.HeroPatches[id] = def;
                    SelectedHeroId = id;
                    ModProjectLoader.SaveEntity(proj, "heroes", id, def, true);
                    _showOverrideBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(HeroDef d, ModProject proj)
        {
            // ── Stat Preview ─────────────────────────────────────
            if (EditorFields.Section("Stat Preview", ref _secPreview))
            {
                string desc = BuildHeroDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.SubClassName = EditorFields.TextField("SubClass Name", d.SubClassName);
                d.CharacterName = EditorFields.TextField("Character Name", d.CharacterName);
                d.CharacterDescription = EditorFields.TextArea("Description", d.CharacterDescription);
                d.CharacterDescriptionStrength = EditorFields.TextArea("Strength Desc", d.CharacterDescriptionStrength);
                d.MainCharacter = EditorFields.Toggle("Main Character", d.MainCharacter);
                d.InitialUnlock = EditorFields.Toggle("Initial Unlock", d.InitialUnlock);
                d.Sku = EditorFields.TextField("SKU (DLC)", d.Sku);
            }

            // ── Class ────────────────────────────────────────────
            if (EditorFields.Section("Class", ref _secClass))
            {
                d.HeroClass = EditorFields.EnumField("Primary", d.HeroClass, "hero_class1");
                d.HeroClassSecondary = EditorFields.EnumField("Secondary", d.HeroClassSecondary, "hero_class2");
                d.HeroClassThird = EditorFields.EnumField("Third", d.HeroClassThird, "hero_class3");
            }

            // ── Stats ────────────────────────────────────────────
            if (EditorFields.Section("Stats", ref _secStats))
            {
                d.OrderInList = EditorFields.IntField("Order", d.OrderInList);
                d.Blocked = EditorFields.Toggle("Blocked", d.Blocked);
                d.Hp = EditorFields.IntField("HP", d.Hp);
                d.Speed = EditorFields.IntField("Speed", d.Speed);
                d.Energy = EditorFields.IntField("Energy", d.Energy);
                d.EnergyTurn = EditorFields.IntField("Energy/Turn", d.EnergyTurn);
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

            // ── Visual ───────────────────────────────────────────
            if (EditorFields.Section("Visuals", ref _secVisual))
            {
                var scIds = DataHelper.GetAllSubClassIds();
                d.SpriteSource = EditorFields.IdDropdown("Sprite Src", d.SpriteSource, scIds, "hero_spritesrc");
                d.FluffOffsetX = EditorFields.FloatField("Fluff Offset X", d.FluffOffsetX);
                d.FluffOffsetY = EditorFields.FloatField("Fluff Offset Y", d.FluffOffsetY);
                d.Female = EditorFields.Toggle("Female", d.Female);
                d.StickerOffsetX = EditorFields.FloatField("Sticker Offset X", d.StickerOffsetX);
            }

            // ── Starting Item ────────────────────────────────────
            if (EditorFields.Section("Starting Item", ref _secItem))
            {
                var cardIds = BuildCardIdList(proj);
                d.ItemId = EditorFields.IdDropdown("Item Card", d.ItemId, cardIds, "hero_item");
            }

            // ── Max HP per Level ─────────────────────────────────
            if (EditorFields.Section($"Max HP per Level ({d.MaxHp.Count})", ref _secMaxHp))
            {
                for (int i = 0; i < d.MaxHp.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.MaxHp[i] = EditorFields.IntField($"Lv {i}", d.MaxHp[i]);
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.MaxHp.RemoveAt(i);
                        GUI.changed = true;
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Level", EditorStyles.MiniButton, GUILayout.Width(70)))
                {
                    d.MaxHp.Add(d.Hp + d.MaxHp.Count * 5);
                    GUI.changed = true;
                }
            }

            // ── Starting Cards ───────────────────────────────────
            if (EditorFields.Section($"Starting Cards ({d.Cards.Count})", ref _secCards))
            {
                var cardIds = BuildCardIdList(proj);
                for (int i = 0; i < d.Cards.Count; i++)
                {
                    var hc = d.Cards[i];
                    GUILayout.BeginVertical(EditorStyles.CompactBox);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>#{i}</b>", EditorStyles.RichLabel, GUILayout.Width(30));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.Cards.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                    GUILayout.EndHorizontal();
                    hc.CardId = EditorFields.IdDropdown("Card", hc.CardId, cardIds, $"hero_card_{i}");
                    hc.UnitsInDeck = EditorFields.IntField("Copies", hc.UnitsInDeck);
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
                if (GUILayout.Button("+ Card", EditorStyles.MiniButton, GUILayout.Width(70)))
                {
                    d.Cards.Add(new HeroCardDef { UnitsInDeck = 1 });
                    GUI.changed = true;
                }
            }

            // ── Singularity Cards ────────────────────────────────
            if (EditorFields.Section($"Singularity Cards ({d.CardsSingularity.Count})", ref _secSingularity))
            {
                var cardIds = BuildCardIdList(proj);
                for (int i = 0; i < d.CardsSingularity.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.CardsSingularity[i] = EditorFields.IdDropdown("", d.CardsSingularity[i], cardIds, $"hero_sing_{i}");
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.CardsSingularity.RemoveAt(i);
                        GUI.changed = true;
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Singularity Card", EditorStyles.MiniButton, GUILayout.Width(130)))
                {
                    d.CardsSingularity.Add("");
                    GUI.changed = true;
                }
            }

            // ── Trait Tree ───────────────────────────────────────
            if (EditorFields.Section("Trait Tree", ref _secTraits))
            {
                var traitIds = DataHelper.GetAllTraitIds();
                var cardIds = BuildCardIdList(proj);

                GUILayout.Label("<color=#aaa>Tier 0 (starting):</color>", EditorStyles.RichLabel);
                d.Trait0 = EditorFields.IdDropdown("Trait 0", d.Trait0, traitIds, "hero_t0");

                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Tier 1 (choice A / B):</color>", EditorStyles.RichLabel);
                d.Trait1A = EditorFields.IdDropdown("Trait 1A", d.Trait1A, traitIds, "hero_t1a");
                d.Trait1ACard = EditorFields.IdDropdown("  1A Card", d.Trait1ACard, cardIds, "hero_t1ac");
                d.Trait1B = EditorFields.IdDropdown("Trait 1B", d.Trait1B, traitIds, "hero_t1b");
                d.Trait1BCard = EditorFields.IdDropdown("  1B Card", d.Trait1BCard, cardIds, "hero_t1bc");

                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Tier 2:</color>", EditorStyles.RichLabel);
                d.Trait2A = EditorFields.IdDropdown("Trait 2A", d.Trait2A, traitIds, "hero_t2a");
                d.Trait2B = EditorFields.IdDropdown("Trait 2B", d.Trait2B, traitIds, "hero_t2b");

                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Tier 3:</color>", EditorStyles.RichLabel);
                d.Trait3A = EditorFields.IdDropdown("Trait 3A", d.Trait3A, traitIds, "hero_t3a");
                d.Trait3ACard = EditorFields.IdDropdown("  3A Card", d.Trait3ACard, cardIds, "hero_t3ac");
                d.Trait3B = EditorFields.IdDropdown("Trait 3B", d.Trait3B, traitIds, "hero_t3b");
                d.Trait3BCard = EditorFields.IdDropdown("  3B Card", d.Trait3BCard, cardIds, "hero_t3bc");

                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Tier 4:</color>", EditorStyles.RichLabel);
                d.Trait4A = EditorFields.IdDropdown("Trait 4A", d.Trait4A, traitIds, "hero_t4a");
                d.Trait4B = EditorFields.IdDropdown("Trait 4B", d.Trait4B, traitIds, "hero_t4b");
            }

            // ── Challenge Packs ──────────────────────────────────
            if (EditorFields.Section("Challenge Packs", ref _secPacks))
            {
                d.ChallengePack0 = EditorFields.TextField("Pack 0", d.ChallengePack0);
                d.ChallengePack1 = EditorFields.TextField("Pack 1", d.ChallengePack1);
                d.ChallengePack2 = EditorFields.TextField("Pack 2", d.ChallengePack2);
                d.ChallengePack3 = EditorFields.TextField("Pack 3", d.ChallengePack3);
                d.ChallengePack4 = EditorFields.TextField("Pack 4", d.ChallengePack4);
                d.ChallengePack5 = EditorFields.TextField("Pack 5", d.ChallengePack5);
                d.ChallengePack6 = EditorFields.TextField("Pack 6", d.ChallengePack6);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static List<string> BuildCardIdList(ModProject proj)
        {
            var cardIds = new List<string>();
            cardIds.AddRange(proj.Cards.Keys.OrderBy(k => k));
            cardIds.AddRange(proj.CardPatches.Keys.OrderBy(k => k));
            cardIds.AddRange(DataHelper.GetAllCardIds());
            return cardIds.Distinct().ToList();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        public static string BuildHeroDescription(HeroDef d)
        {
            var sb = new StringBuilder();

            // Header
            sb.Append($"<b>{d.SubClassName}</b>");
            if (!string.IsNullOrEmpty(d.CharacterName) && d.CharacterName != d.SubClassName)
                sb.Append($"  <color=#aaa>({d.CharacterName})</color>");

            // Class
            sb.Append($"\n<color=#88ccff>{d.HeroClass}</color>");
            if (d.HeroClassSecondary != Enums.HeroClass.None)
                sb.Append($" / {d.HeroClassSecondary}");
            if (d.HeroClassThird != Enums.HeroClass.None)
                sb.Append($" / {d.HeroClassThird}");

            // Stats
            sb.Append($"\n<color=#44cc44>HP {d.Hp}</color>  Spd {d.Speed}  Energy {d.Energy}  E/Turn {d.EnergyTurn}");

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

            // Deck
            if (d.Cards.Count > 0)
                sb.Append($"\n<color=#dd88ff>{d.Cards.Count} starting card(s), {d.Cards.Sum(c => c.UnitsInDeck)} total</color>");

            // Flags
            var flags = new List<string>();
            if (d.MainCharacter) flags.Add("Main");
            if (d.InitialUnlock) flags.Add("Unlocked");
            if (d.Female) flags.Add("Female");
            if (d.Blocked) flags.Add("Blocked");
            if (!string.IsNullOrEmpty(d.Sku)) flags.Add($"DLC:{d.Sku}");
            if (flags.Count > 0)
                sb.Append($"\n<color=#888>{string.Join(" | ", flags)}</color>");

            return sb.ToString();
        }
    }
}
