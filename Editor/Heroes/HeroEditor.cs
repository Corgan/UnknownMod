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
    public class HeroEditor : ModProjectEditorBase<HeroDef>
    {
        protected override string TypeLabel => "Hero";
        protected override string FolderName => "heroes";
        protected override string NewIdSuffix => "_new_hero";
        protected override EntityPicker.Mode? PickerMode => EntityPicker.Mode.Hero;

        protected override Dictionary<string, HeroDef> GetNewDict(ModProject proj) => proj.Heroes;
        protected override Dictionary<string, HeroDef> GetPatchDict(ModProject proj) => proj.HeroPatches;

        protected override HeroDef CreateDefault(string id, ModProject proj)
            => new HeroDef
            {
                Id = id,
                SubClassName = "New Hero",
                CharacterName = "New Hero",
                Hp = 40,
                Speed = 5,
                Energy = 3,
                EnergyTurn = 3,
                Blocked = false,
            };

        protected override string GetDisplayName(HeroDef def) => def.SubClassName;

        protected override List<string> GetAllBaseIds()
            => DataHelper.GetAllSubClassIds();

        protected override HeroDef SnapshotBaseEntity(string id)
        {
            var existing = DataHelper.GetSubClass(id);
            return existing != null ? DataHelper.SnapshotHero(existing) : null;
        }

        public HeroEditor(ModEditor parent) : base(parent) { }

        //  Collapsible section state 
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
        private bool _secReplacement = false;

        // 
        //  ALL SECTIONS
        // 

        protected override void DrawAllSections(HeroDef d, ModProject proj)
        {
            //  Identity 
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

            //  Class 
            if (EditorFields.Section("Class", ref _secClass))
            {
                d.HeroClass = EditorFields.EnumField("Primary", d.HeroClass, "hero_class1");
                d.HeroClassSecondary = EditorFields.EnumField("Secondary", d.HeroClassSecondary, "hero_class2");
                d.HeroClassThird = EditorFields.EnumField("Third", d.HeroClassThird, "hero_class3");
            }

            //  Stats 
            if (EditorFields.Section("Stats", ref _secStats))
            {
                d.OrderInList = EditorFields.IntField("Order", d.OrderInList);
                d.Blocked = EditorFields.Toggle("Blocked", d.Blocked);
                d.Hp = EditorFields.IntFieldMin("HP", d.Hp, 1);
                d.Speed = EditorFields.IntFieldMin("Speed", d.Speed, 0);
                d.Energy = EditorFields.IntField("Energy", d.Energy, 0, 10);
                d.EnergyTurn = EditorFields.IntField("Energy/Turn", d.EnergyTurn, 0, 10);
            }

            //  Resistances 
            if (EditorFields.Section("Resistances", ref _secResist))
            {
                EditorFields.ResistGrid(
                    ref d.ResSlash,  ref d.ResBlunt,  ref d.ResPierce,
                    ref d.ResFire,   ref d.ResCold,   ref d.ResLight,
                    ref d.ResMind,   ref d.ResHoly,   ref d.ResShadow);
            }

            //  Visual 
            if (EditorFields.Section("Visuals", ref _secVisual))
            {
                var scIds = EditorFields.CachedIds("subclass", DataHelper.GetAllSubClassIds);
                d.SpriteSource = EditorFields.IdDropdown("Sprite Src", d.SpriteSource, scIds, "hero_spritesrc", pickerMode: EntityPicker.Mode.Hero);
                d.FluffOffsetX = EditorFields.FloatField("Fluff Offset X", d.FluffOffsetX);
                d.FluffOffsetY = EditorFields.FloatField("Fluff Offset Y", d.FluffOffsetY);
                d.Female = EditorFields.Toggle("Female", d.Female);
                d.StickerOffsetX = EditorFields.FloatField("Sticker Offset X", d.StickerOffsetX);
            }

            //  Starting Item 
            if (EditorFields.Section("Starting Item", ref _secItem))
            {
                var cardIds = EditorFields.BuildCardIdList(proj);
                d.ItemId = EditorFields.IdDropdown("Item Card", d.ItemId, cardIds, "hero_item", pickerMode: EntityPicker.Mode.Card);
            }

            //  Max HP per Level 
            if (EditorFields.Section($"Max HP per Level ({d.MaxHp.Count})", ref _secMaxHp))
            {
                for (int i = 0; i < d.MaxHp.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.MaxHp[i] = EditorFields.IntFieldMin($"Lv {i}", d.MaxHp[i], 1);
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.MaxHp.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
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

            //  Starting Cards 
            if (EditorFields.Section($"Starting Cards ({d.Cards.Count})", ref _secCards))
            {
                var cardIds = EditorFields.BuildCardIdList(proj);
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
                    hc.CardId = EditorFields.IdDropdown("Card", hc.CardId, cardIds, $"hero_card_{i}", pickerMode: EntityPicker.Mode.Card);
                    hc.UnitsInDeck = EditorFields.IntFieldMin("Copies", hc.UnitsInDeck, 1);
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
                if (GUILayout.Button("+ Card", EditorStyles.MiniButton, GUILayout.Width(70)))
                {
                    d.Cards.Add(new HeroCardDef { UnitsInDeck = 1 });
                    GUI.changed = true;
                }
            }

            //  Singularity Cards 
            if (EditorFields.Section($"Singularity Cards ({d.CardsSingularity.Count})", ref _secSingularity))
            {
                var cardIds = EditorFields.BuildCardIdList(proj);
                for (int i = 0; i < d.CardsSingularity.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.CardsSingularity[i] = EditorFields.IdDropdown("", d.CardsSingularity[i], cardIds, $"hero_sing_{i}", pickerMode: EntityPicker.Mode.Card);
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.CardsSingularity.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
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

            //  Trait Tree 
            if (EditorFields.Section("Trait Tree", ref _secTraits))
            {
                var traitIds = DataHelper.GetAllTraitIds();
                var cardIds = EditorFields.BuildCardIdList(proj);

                GUILayout.Label("<color=#aaa>Tier 0 (starting):</color>", EditorStyles.RichLabel);
                d.Trait0 = EditorFields.IdDropdown("Trait 0", d.Trait0, traitIds, "hero_t0", pickerMode: EntityPicker.Mode.Trait);

                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Tier 1 (choice A / B):</color>", EditorStyles.RichLabel);
                d.Trait1A = EditorFields.IdDropdown("Trait 1A", d.Trait1A, traitIds, "hero_t1a", pickerMode: EntityPicker.Mode.Trait);
                d.Trait1ACard = EditorFields.IdDropdown("  1A Card", d.Trait1ACard, cardIds, "hero_t1ac", pickerMode: EntityPicker.Mode.Card);
                d.Trait1B = EditorFields.IdDropdown("Trait 1B", d.Trait1B, traitIds, "hero_t1b", pickerMode: EntityPicker.Mode.Trait);
                d.Trait1BCard = EditorFields.IdDropdown("  1B Card", d.Trait1BCard, cardIds, "hero_t1bc", pickerMode: EntityPicker.Mode.Card);

                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Tier 2:</color>", EditorStyles.RichLabel);
                d.Trait2A = EditorFields.IdDropdown("Trait 2A", d.Trait2A, traitIds, "hero_t2a", pickerMode: EntityPicker.Mode.Trait);
                d.Trait2B = EditorFields.IdDropdown("Trait 2B", d.Trait2B, traitIds, "hero_t2b", pickerMode: EntityPicker.Mode.Trait);

                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Tier 3:</color>", EditorStyles.RichLabel);
                d.Trait3A = EditorFields.IdDropdown("Trait 3A", d.Trait3A, traitIds, "hero_t3a", pickerMode: EntityPicker.Mode.Trait);
                d.Trait3ACard = EditorFields.IdDropdown("  3A Card", d.Trait3ACard, cardIds, "hero_t3ac", pickerMode: EntityPicker.Mode.Card);
                d.Trait3B = EditorFields.IdDropdown("Trait 3B", d.Trait3B, traitIds, "hero_t3b", pickerMode: EntityPicker.Mode.Trait);
                d.Trait3BCard = EditorFields.IdDropdown("  3B Card", d.Trait3BCard, cardIds, "hero_t3bc", pickerMode: EntityPicker.Mode.Card);

                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Tier 4:</color>", EditorStyles.RichLabel);
                d.Trait4A = EditorFields.IdDropdown("Trait 4A", d.Trait4A, traitIds, "hero_t4a", pickerMode: EntityPicker.Mode.Trait);
                d.Trait4B = EditorFields.IdDropdown("Trait 4B", d.Trait4B, traitIds, "hero_t4b", pickerMode: EntityPicker.Mode.Trait);
            }

            //  Challenge Packs 
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

            //  Character Replacement 
            if (EditorFields.Section($"Replacement ({d.CardsOnReplaceCharacter.Count} cards)", ref _secReplacement))
            {
                d.UseXpFromOriginal = EditorFields.Toggle("Use XP From Original", d.UseXpFromOriginal);
                d.PerksOnReplace = EditorFields.TextField("Perks On Replace", d.PerksOnReplace);

                GUILayout.Space(4);
                GUILayout.Label($"<color=#aaa>Replacement Cards ({d.CardsOnReplaceCharacter.Count}):</color>", EditorStyles.RichLabel);
                var cardIds = EditorFields.BuildCardIdList(proj);
                for (int i = 0; i < d.CardsOnReplaceCharacter.Count; i++)
                {
                    var hc = d.CardsOnReplaceCharacter[i];
                    GUILayout.BeginVertical(EditorStyles.CompactBox);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>#{i}</b>", EditorStyles.RichLabel, GUILayout.Width(30));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.CardsOnReplaceCharacter.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                    GUILayout.EndHorizontal();
                    hc.CardId = EditorFields.IdDropdown("Card", hc.CardId, cardIds, $"hero_rcard_{i}", pickerMode: EntityPicker.Mode.Card);
                    hc.UnitsInDeck = EditorFields.IntFieldMin("Copies", hc.UnitsInDeck, 1);
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
                if (GUILayout.Button("+ Card", EditorStyles.MiniButton, GUILayout.Width(70)))
                {
                    d.CardsOnReplaceCharacter.Add(new HeroCardDef { UnitsInDeck = 1 });
                    GUI.changed = true;
                }
            }
        }

        // 
        //  LIVE DESCRIPTION BUILDER
        // 

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
