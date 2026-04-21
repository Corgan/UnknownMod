using System;
using System.Collections.Generic;
using System.Linq;
using Cards;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Unified IMGUI panel for editing ALL card types (hero, equipment, monster, special).
    /// Equipment cards (items, enchantments, pets) show additional ItemFields sections
    /// when cd.HasItemData is true.
    /// </summary>
    public class CardEditor : ModProjectEditorBase<CardDef>
    {
        protected override string TypeLabel => "Card";
        protected override string FolderName => "cards";
        protected override string NewIdSuffix => "newcard";
        protected override EntityPicker.Mode? PickerMode => EntityPicker.Mode.Card;
        protected override bool AutoSnapshotOnSelect => false;

        public override string SelectedId
        {
            get => Parent.SelectedCardId;
            set
            {
                Parent.SelectedCardId = value;
                _upgradeTab = 0;
            }
        }

        protected override Dictionary<string, CardDef> GetNewDict(ModProject proj) => proj.Cards;
        protected override Dictionary<string, CardDef> GetPatchDict(ModProject proj) => proj.CardPatches;

        protected override CardDef CreateDefault(string id, ModProject proj)
        {
            var cd = new CardDef { Id = id, Name = "New Card" };
            Parent.CardsTab?.ApplyDefaultsForActiveTab(cd);
            return cd;
        }

        protected override string GetDisplayName(CardDef def) => def.Name;

        protected override CardDef SnapshotBaseEntity(string id)
        {
            // Try equipment first (has paired ItemData)
            var item = DataHelper.GetItem(id);
            if (item != null)
            {
                var card = DataHelper.GetCard(id);
                return CardDef.SnapshotFromItem(item, card);
            }
            // Regular card
            var existing = DataHelper.GetCard(id);
            return existing != null ? ModProjectBuilder.SnapshotCard(existing) : null;
        }

        // ── Save/Delete overrides for semantic subfolders ─────────
        protected override void OnSaveEntity(ModProject proj, string id, CardDef def, bool isPatch = false)
            => ModProjectLoader.SaveCard(proj, def, isPatch);

        protected override void OnDeleteEntity(ModProject proj, string id, bool isPatch)
            => ModProjectLoader.DeleteCard(proj, id, isPatch);

        // ── Tab-based filtering ──────────────────────────────────
        protected override List<string> FilterEntityList(List<string> allIds, ModProject proj)
        {
            var tab = Parent.CardsTab;

            // Collect base IDs implied by variant-only overrides (normalized to lowercase)
            var impliedBaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in proj.CardPatches)
                if (kvp.Value.CardUpgraded != Enums.CardUpgraded.No && !string.IsNullOrEmpty(kvp.Value.BaseCardId))
                    impliedBaseIds.Add(kvp.Value.BaseCardId.ToLower());
            foreach (var kvp in proj.Cards)
                if (kvp.Value.CardUpgraded != Enums.CardUpgraded.No && !string.IsNullOrEmpty(kvp.Value.BaseCardId))
                    impliedBaseIds.Add(kvp.Value.BaseCardId.ToLower());

            // Add implied base IDs that aren't already in the list
            foreach (var baseId in impliedBaseIds)
                if (!allIds.Contains(baseId))
                    allIds.Add(baseId);

            return allIds.Where(id =>
            {
                // Always show implied base IDs (from variant-only overrides)
                if (impliedBaseIds.Contains(id) && !proj.Cards.ContainsKey(id) && !proj.CardPatches.ContainsKey(id))
                    return true;

                if (proj.Cards.TryGetValue(id, out var cd) || proj.CardPatches.TryGetValue(id, out cd))
                {
                    if (cd.CardUpgraded != Enums.CardUpgraded.No) return false;
                    return tab?.MatchesActiveFilter(cd) ?? true;
                }
                var baseCard = DataHelper.GetCard(id);
                if (baseCard != null)
                {
                    if (baseCard.CardUpgraded != Enums.CardUpgraded.No) return false;
                    return tab?.MatchesActiveFilter(baseCard.CardClass, baseCard.CardType) ?? true;
                }
                return true;
            }).ToList();
        }

        protected override List<string> GetAllBaseIds()
        {
            var tab = Parent.CardsTab;
            var all = DataHelper.GetAllCardIds();
            return all.Where(id =>
            {
                var c = DataHelper.GetCard(id);
                if (c == null) return true;
                if (c.CardUpgraded != Enums.CardUpgraded.No) return false;
                return tab?.MatchesActiveFilter(c.CardClass, c.CardType) ?? true;
            }).ToList();
        }

        public CardEditor(ModEditor parent) : base(parent) { }

        // ══════════════════════════════════════════════════════════
        //  UPGRADE VARIANT STATE
        // ══════════════════════════════════════════════════════════

        /// <summary>0=Base, 1=A, 2=B, 3=Rare (non-equipment) or 0=Base, 1=Rare (equipment)</summary>
        private int _upgradeTab = 0;
        private string _prevBaseId;
        private string _prevVariantId;
        private static readonly string[] _cardUpgradeLabels = { "Base", "A", "B", "Rare" };
        private static readonly string[] _equipUpgradeLabels = { "Base", "Rare" };
        private bool IsEditingVariant => _upgradeTab != 0;

        // ══════════════════════════════════════════════════════════
        //  SECTION TOGGLE STATE
        // ══════════════════════════════════════════════════════════

        // Card sections
        private bool _secIdentity = true;
        private bool _secClassify = false;
        private bool _secCost = false;
        private bool _secFlags = false;
        private bool _secDamage = true;
        private bool _subDmg2 = false;
        private bool _subDmgSelf = false;
        private bool _secCurses = true;
        private bool _secAuras = true;
        private bool _secHeal = false;
        private bool _secAcManip = false;
        private bool _secTarget = true;
        private bool _secRepeat = false;
        private bool _secMechanics = false;
        private bool _secDiscard = false;
        private bool _secAddCard = false;
        private bool _secLook = false;
        private bool _secSummon = false;
        private bool _secAcEnergy = false;
        private bool _secSpecialVal = false;
        private bool _secSpecialFlags = false;
        private bool _secFx = false;
        private bool _secPet = false;
        private bool _secUpgradeParams = false;

        // Item sections (only shown when HasItemData)
        private bool _secActivation = false;
        private bool _secItemDamage = true;
        private bool _secResist = true;
        private bool _secStat = false;
        private bool _secItemHeal = false;
        private bool _secEnergyDraw = false;
        private bool _secAcGain = false;
        private bool _secAcSelf = false;
        private bool _secDispel = false;
        private bool _secAcBonus = false;
        private bool _secAcImmune = false;
        private bool _secCardGain = false;
        private bool _secCostEcon = false;
        private bool _secRewards = false;
        private bool _secDmgTarget = false;
        private bool _secItemFlags = false;
        private bool _secEnchant = false;
        private bool _secCustomAC = false;
        private bool _secDebuffConvert = false;
        private bool _secItemFx = false;

        // ══════════════════════════════════════════════════════════
        //  DRAW ALL SECTIONS
        // ══════════════════════════════════════════════════════════

        protected override void DrawAllSections(CardDef cd, ModProject proj)
        {
            // ── Resolve base card + variant tab bar ──────────────
            var baseDef = ResolveBaseDef(cd, proj);
            bool isEquipment = baseDef.HasItemData;
            var labels = isEquipment ? _equipUpgradeLabels : _cardUpgradeLabels;

            // Sync tab when selected entity changes
            if (_prevBaseId != baseDef.Id)
            {
                _prevBaseId = baseDef.Id;
                _upgradeTab = TabIndexFor(cd.CardUpgraded, isEquipment);
            }
            if (_upgradeTab >= labels.Length) _upgradeTab = 0;

            DrawUpgradeTabBar(baseDef, proj, labels);
            GUILayout.Space(4);

            bool baseInProject = FindProjectCard(baseDef.Id, proj) != null;

            if (IsEditingVariant)
            {
                var level = TabIndexToLevel(_upgradeTab, isEquipment);
                var vDef = FindVariant(baseDef.Id, proj, level);
                if (vDef == null)
                {
                    DrawCreateVariantUI(baseDef, proj, level, labels[_upgradeTab]);
                    return;
                }

                // Show revert/delete for variant
                DrawVariantRemoveButton(vDef, proj, isBase: false);

                cd = vDef;
                _prevVariantId = vDef.Id;
            }
            else if (!baseInProject)
            {
                // Base tab selected but base is not overridden — show override prompt
                DrawCreateVariantUI(baseDef, proj, Enums.CardUpgraded.No, "Base");
                return;
            }
            else
            {
                // Show revert/delete for base card
                DrawVariantRemoveButton(baseDef, proj, isBase: true);
            }

            // ══════════════════════════════════════════════════════
            //  CARD IDENTITY
            // ══════════════════════════════════════════════════════

            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                cd.Id = EditorFields.TextField("ID", cd.Id).Replace("_", "");
                cd.Name = EditorFields.TextField("Name", cd.Name);
                cd.Description = EditorFields.TextArea("Description", cd.Description);
                cd.Fluff = EditorFields.TextField("Fluff", cd.Fluff);
                cd.FluffPercent = EditorFields.FloatField("Fluff %", cd.FluffPercent);
                cd.SpriteSource = EditorFields.IdDropdown("Sprite Source", cd.SpriteSource,
                    EditorFields.CachedIds("spriteName", () => MapEditor.GetAllSpriteNames()),
                    "card_sprsrc", pickerMode: EntityPicker.Mode.Sprite);
                cd.SoundSource = EditorFields.IdDropdown("Sound Source", cd.SoundSource, EditorFields.CachedIds("card", DataHelper.GetAllCardIds), "card_sndsrc", pickerMode: EntityPicker.Mode.Card);
                cd.PetModelSource = EditorFields.IdDropdown("Pet Model Source", cd.PetModelSource, GetPetModelSourceIds(proj), "card_petmdl");
                cd.EnergyCost = EditorFields.IntField("Energy Cost", cd.EnergyCost, 0, 10);
                cd.IsUpgraded = EditorFields.Toggle("Is Upgraded", cd.IsUpgraded);
                if (cd.IsUpgraded)
                    cd.BaseCardId = EditorFields.IdDropdown("Base Card", cd.BaseCardId, EditorFields.CachedIds("card", DataHelper.GetAllCardIds), "card_base", pickerMode: EntityPicker.Mode.Card);
            }

            // ══════════════════════════════════════════════════════
            //  CLASSIFICATION
            // ══════════════════════════════════════════════════════

            if (EditorFields.Section("Classification", ref _secClassify))
            {
                cd.CardUpgraded = EditorFields.EnumField("Upgraded Level", cd.CardUpgraded, "card_cupg");
                cd.CardRarity = EditorFields.EnumField("Rarity", cd.CardRarity, "card_rarity");
                cd.CardType = EditorFields.EnumField("Card Type", cd.CardType, "card_ctype");
                cd.CardClass = EditorFields.EnumField("Class", cd.CardClass, "card_cclass");
                cd.CardNumber = EditorFields.IntField("Card Number", cd.CardNumber);
                cd.MaxInDeck = EditorFields.IntFieldMin("Max In Deck", cd.MaxInDeck, 0);
                cd.Sku = EditorFields.TextField("SKU", cd.Sku);
                cd.SpecialCardEnum = EditorFields.EnumField("Special Card", cd.SpecialCardEnum, "card_speccard");

                if (cd.CardTypeAux == null) cd.CardTypeAux = Array.Empty<Enums.CardType>();
                GUILayout.Label($"<color=#aaa>Aux Types ({cd.CardTypeAux.Length}):</color>", EditorStyles.RichLabel);
                for (int i = 0; i < cd.CardTypeAux.Length; i++)
                {
                    GUILayout.BeginHorizontal();
                    cd.CardTypeAux[i] = EditorFields.EnumField($"Aux {i + 1}", cd.CardTypeAux[i], $"card_ctaux_{i}");
                    if (GUILayout.Button("X", EditorStyles.DangerButton, GUILayout.Width(22)))
                    {
                        var list = new List<Enums.CardType>(cd.CardTypeAux);
                        list.RemoveAt(i);
                        cd.CardTypeAux = list.ToArray();
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Aux Type", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    var list = new List<Enums.CardType>(cd.CardTypeAux);
                    list.Add(Enums.CardType.None);
                    cd.CardTypeAux = list.ToArray();
                    GUI.changed = true;
                }
            }

            // ══════════════════════════════════════════════════════
            //  ITEM FIELDS (only when equipment/enchantment/pet)
            // ══════════════════════════════════════════════════════

            if (cd.HasItemData)
                DrawItemFieldsSections(cd.Item);

            // ══════════════════════════════════════════════════════
            //  CARD SECTIONS
            // ══════════════════════════════════════════════════════

            //  Cost / Economy 
            if (EditorFields.Section("Cost / Economy", ref _secCost))
            {
                cd.EnergyCostOriginal = EditorFields.IntField("Cost Original", cd.EnergyCostOriginal, 0, 10);
                cd.EnergyCostForShow = EditorFields.IntField("Cost For Show", cd.EnergyCostForShow, 0, 10);
                cd.EnergyReductionPermanent = EditorFields.IntFieldMin("Perm Reduction", cd.EnergyReductionPermanent, 0);
                cd.EnergyReductionTemporal = EditorFields.IntFieldMin("Temp Reduction", cd.EnergyReductionTemporal, 0);
                cd.EnergyReductionToZeroPermanent = EditorFields.Toggle("Perm Zero Cost", cd.EnergyReductionToZeroPermanent);
                cd.EnergyReductionToZeroTemporal = EditorFields.Toggle("Temp Zero Cost", cd.EnergyReductionToZeroTemporal);
            }

            //  Flags 
            if (EditorFields.Section("Flags", ref _secFlags))
            {
                var flagLabels = new[] { "Playable", "Visible", "Show Tome", "Autoplay Draw", "Autoplay EOT", "Vanish", "Innate", "Lazy", "Corrupted", "End Turn", "Starter", "Flip Sprite", "Mod by Trait", "Weekly Only" };
                var flagVals = new[] { cd.Playable, cd.Visible, cd.ShowInTome, cd.AutoplayDraw, cd.AutoplayEndTurn, cd.Vanish, cd.Innate, cd.Lazy, cd.Corrupted, cd.EndTurn, cd.Starter, cd.FlipSprite, cd.ModifiedByTrait, cd.OnlyInWeekly };
                EditorFields.ToggleGrid(flagLabels, flagVals, 3);
                cd.Playable = flagVals[0]; cd.Visible = flagVals[1]; cd.ShowInTome = flagVals[2];
                cd.AutoplayDraw = flagVals[3]; cd.AutoplayEndTurn = flagVals[4]; cd.Vanish = flagVals[5];
                cd.Innate = flagVals[6]; cd.Lazy = flagVals[7]; cd.Corrupted = flagVals[8];
                cd.EndTurn = flagVals[9]; cd.Starter = flagVals[10]; cd.FlipSprite = flagVals[11];
                cd.ModifiedByTrait = flagVals[12]; cd.OnlyInWeekly = flagVals[13];
            }

            //  Damage 
            if (EditorFields.Section("Damage", ref _secDamage))
            {
                EditorFields.DamageRow("Damage 1", ref cd.Damage, ref cd.DamageType,
                    ref cd.DamageSides, ref cd.IgnoreBlock, "card_dmg1");
                cd.DamageEnergyBonus = EditorFields.IntFieldMin("+Dmg/Energy", cd.DamageEnergyBonus, 0);

                bool hasDmg2 = cd.Damage2 != 0 || cd.DamageType2 != Enums.DamageType.None;
                if (hasDmg2) _subDmg2 = true;
                string dmg2Lbl = hasDmg2
                    ? $"<color=#e8c06a>\u25BC Damage 2</color>"
                    : (_subDmg2 ? "\u25BC Damage 2" : "\u25BA Damage 2 (empty)");
                if (GUILayout.Button(dmg2Lbl, EditorStyles.RichLabel))
                    _subDmg2 = !_subDmg2;
                if (_subDmg2)
                {
                    EditorFields.DamageRow("Damage 2", ref cd.Damage2, ref cd.DamageType2,
                        ref cd.DamageSides2, ref cd.IgnoreBlock2, "card_dmg2");
                    if (cd.DamageType != Enums.DamageType.None && cd.DamageType == cd.DamageType2)
                        GUILayout.Label("<color=#cc8844>  \u26A0 Same type as Damage 1 \u2014 Damage 2 will be skipped in-game</color>", EditorStyles.RichLabel);
                }

                bool hasSelf = cd.DamageSelf != 0 || cd.DamageSelf2 != 0 || cd.SelfHealthLoss != 0;
                if (hasSelf) _subDmgSelf = true;
                string selfLbl = hasSelf
                    ? $"<color=#cc4444>\u25BC Self Damage</color>"
                    : (_subDmgSelf ? "\u25BC Self Damage" : "\u25BA Self Damage (empty)");
                if (GUILayout.Button(selfLbl, EditorStyles.RichLabel))
                    _subDmgSelf = !_subDmgSelf;
                if (_subDmgSelf)
                {
                    cd.DamageSelf = EditorFields.IntField("Damage Self", cd.DamageSelf);
                    cd.DamageSelf2 = EditorFields.IntField("Damage Self 2", cd.DamageSelf2);
                    cd.SelfHealthLoss = EditorFields.IntField("Self HP Loss", cd.SelfHealthLoss);
                    cd.SelfKillHiddenSeconds = EditorFields.FloatField("SelfKill Delay", cd.SelfKillHiddenSeconds);
                }
            }

            //  Curses 
            if (EditorFields.Section("Curses", ref _secCurses))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                cd.Curse = EditorFields.ACChargesField("Curse 1", cd.Curse, ref cd.CurseCharges, acIds, "cd_curse");
                if (!string.IsNullOrEmpty(cd.Curse))
                    cd.CurseChargesSides = EditorFields.IntField("  Charge Splash", cd.CurseChargesSides);
                cd.CurseSelf = EditorFields.IdDropdown("  Self 1", cd.CurseSelf, acIds, "cd_curseself", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.CurseSelfCharges = EditorFields.IntField("  Self 1 Charges", cd.CurseSelfCharges);
                if (!string.IsNullOrEmpty(cd.CurseSelf) && string.IsNullOrEmpty(cd.Curse))
                    GUILayout.Label("<color=#cc8844>  \u26A0 Self uses Curse 1 charges (set above)</color>", EditorStyles.RichLabel);
                cd.Curse2 = EditorFields.ACChargesField("Curse 2", cd.Curse2, ref cd.Curse2Charges, acIds, "cd_curse2");
                cd.CurseSelf2 = EditorFields.IdDropdown("  Self 2", cd.CurseSelf2, acIds, "cd_curseself2", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.CurseSelf2Charges = EditorFields.IntField("  Self 2 Charges", cd.CurseSelf2Charges);
                cd.Curse3 = EditorFields.ACChargesField("Curse 3", cd.Curse3, ref cd.Curse3Charges, acIds, "cd_curse3");
                cd.CurseSelf3 = EditorFields.IdDropdown("  Self 3", cd.CurseSelf3, acIds, "cd_curseself3", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.CurseSelf3Charges = EditorFields.IntField("  Self 3 Charges", cd.CurseSelf3Charges);
                GUILayout.Space(4);
                cd.ConvertAllDebuffsIntoCurse = EditorFields.Toggle("Convert Debuffs\u2192Curse", cd.ConvertAllDebuffsIntoCurse);
            }

            //  Auras 
            if (EditorFields.Section("Auras", ref _secAuras))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                cd.Aura = EditorFields.ACChargesField("Aura 1", cd.Aura, ref cd.AuraCharges, acIds, "cd_aura");
                cd.AuraSelf = EditorFields.IdDropdown("  Self 1", cd.AuraSelf, acIds, "cd_auraself", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.AuraSelfCharges = EditorFields.IntField("  Self 1 Charges", cd.AuraSelfCharges);
                if (!string.IsNullOrEmpty(cd.AuraSelf) && string.IsNullOrEmpty(cd.Aura))
                    GUILayout.Label("<color=#cc8844>  \u26A0 Self uses Aura 1 charges (set above)</color>", EditorStyles.RichLabel);
                cd.Aura2 = EditorFields.ACChargesField("Aura 2", cd.Aura2, ref cd.Aura2Charges, acIds, "cd_aura2");
                cd.AuraSelf2 = EditorFields.IdDropdown("  Self 2", cd.AuraSelf2, acIds, "cd_auraself2", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.AuraSelf2Charges = EditorFields.IntField("  Self 2 Charges", cd.AuraSelf2Charges);
                cd.Aura3 = EditorFields.ACChargesField("Aura 3", cd.Aura3, ref cd.Aura3Charges, acIds, "cd_aura3");
                cd.AuraSelf3 = EditorFields.IdDropdown("  Self 3", cd.AuraSelf3, acIds, "cd_auraself3", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.AuraSelf3Charges = EditorFields.IntField("  Self 3 Charges", cd.AuraSelf3Charges);
            }

            //  Heal 
            if (EditorFields.Section("Heal", ref _secHeal))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                cd.Heal = EditorFields.IntField("Heal (target)", cd.Heal);
                cd.HealSides = EditorFields.IntField("Heal Splash", cd.HealSides);
                cd.HealSelf = EditorFields.IntField("Heal Self", cd.HealSelf);
                cd.HealEnergyBonus = EditorFields.IntField("Heal Nrg Bonus", cd.HealEnergyBonus);
                cd.HealSelfPerDamageDonePercent = EditorFields.FloatField("Lifesteal %", cd.HealSelfPerDamageDonePercent);
                cd.HealCurses = EditorFields.IntField("Dispel Curses", cd.HealCurses);
                cd.DispelAuras = EditorFields.IntField("Purge Auras", cd.DispelAuras);
                GUILayout.Space(4);
                cd.HealAuraCurseSelf = EditorFields.IdDropdown("Heal AC Self", cd.HealAuraCurseSelf, acIds, "cd_hacself", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.HealAuraCurseName = EditorFields.IdDropdown("Heal AC 1", cd.HealAuraCurseName, acIds, "cd_hac1", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.HealAuraCurseName2 = EditorFields.IdDropdown("Heal AC 2", cd.HealAuraCurseName2, acIds, "cd_hac2", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.HealAuraCurseName3 = EditorFields.IdDropdown("Heal AC 3", cd.HealAuraCurseName3, acIds, "cd_hac3", pickerMode: EntityPicker.Mode.AuraCurse);
                cd.HealAuraCurseName4 = EditorFields.IdDropdown("Heal AC 4", cd.HealAuraCurseName4, acIds, "cd_hac4", pickerMode: EntityPicker.Mode.AuraCurse);
            }

            //  AC Manipulation 
            if (EditorFields.Section("AC Manipulation", ref _secAcManip))
            {
                cd.TransferCurses = EditorFields.IntField("Transfer Curses", cd.TransferCurses);
                cd.StealAuras = EditorFields.IntField("Steal Auras", cd.StealAuras);
                cd.ReduceCurses = EditorFields.IntField("Reduce Curses", cd.ReduceCurses);
                cd.ReduceAuras = EditorFields.IntField("Reduce Auras", cd.ReduceAuras);
                cd.IncreaseCurses = EditorFields.IntField("Increase Curses", cd.IncreaseCurses);
                cd.IncreaseAuras = EditorFields.IntField("Increase Auras", cd.IncreaseAuras);
            }

            //  Targeting 
            if (EditorFields.Section("Targeting", ref _secTarget))
            {
                EditorFields.TargetingBar(ref cd.TargetSide, ref cd.TargetType, ref cd.TargetPos,
                    "card_tside", "card_ttype", "card_tpos");
            }

            //  Effect Repeat 
            if (EditorFields.Section("Effect Repeat", ref _secRepeat))
            {
                cd.EffectRepeat = EditorFields.IntField("Repeat", cd.EffectRepeat, 0, 10);
                if (cd.EffectRepeat > 1)
                {
                    cd.EffectRepeatDelay = EditorFields.FloatField("Delay", cd.EffectRepeatDelay);
                    cd.EffectRepeatTarget = EditorFields.EnumField("Repeat Target", cd.EffectRepeatTarget, "card_ereptgt");
                    cd.EffectRepeatEnergyBonus = EditorFields.IntField("Nrg Bonus", cd.EffectRepeatEnergyBonus);
                    cd.EffectRepeatMaxBonus = EditorFields.IntFieldMin("Max Bonus", cd.EffectRepeatMaxBonus, 0);
                    cd.EffectRepeatModificator = EditorFields.IntField("Modificator", cd.EffectRepeatModificator);
                    cd.MoveToCenter = EditorFields.Toggle("Move to Center", cd.MoveToCenter);
                }
            }

            //  Misc Mechanics 
            if (EditorFields.Section("Mechanics", ref _secMechanics))
            {
                cd.PushTarget = EditorFields.IntField("Push Target", cd.PushTarget, 0, 3);
                cd.PullTarget = EditorFields.IntField("Pull Target", cd.PullTarget, 0, 3);
                cd.DrawCard = EditorFields.IntField("Draw Cards", cd.DrawCard, 0, 10);
                cd.DiscardCard = EditorFields.IntFieldMin("Discard Cards", cd.DiscardCard, 0);
                cd.EnergyRecharge = EditorFields.IntField("Energy Recharge", cd.EnergyRecharge, 0, 10);
                cd.GoldGainQuantity = EditorFields.IntFieldMin("Gold Gain", cd.GoldGainQuantity, 0);
                cd.ShardsGainQuantity = EditorFields.IntFieldMin("Shards Gain", cd.ShardsGainQuantity, 0);
                cd.ExhaustCounter = EditorFields.IntFieldMin("Exhaust Counter", cd.ExhaustCounter, 0);
                cd.EffectRequired = EditorFields.TextField("Effect Required", cd.EffectRequired);
            }

            //  Discard Options 
            if (EditorFields.Section("Discard Options", ref _secDiscard))
            {
                cd.DiscardCardType = EditorFields.EnumField("Discard Type", cd.DiscardCardType, "card_dct");
                cd.DiscardCardAutomatic = EditorFields.Toggle("Automatic", cd.DiscardCardAutomatic);
                cd.DiscardCardPlace = EditorFields.EnumField("Discard Place", cd.DiscardCardPlace, "card_dcp");

                if (cd.DiscardCardTypeAux == null) cd.DiscardCardTypeAux = Array.Empty<Enums.CardType>();
                GUILayout.Label($"<color=#aaa>Aux Discard Types ({cd.DiscardCardTypeAux.Length}):</color>", EditorStyles.RichLabel);
                for (int i = 0; i < cd.DiscardCardTypeAux.Length; i++)
                {
                    GUILayout.BeginHorizontal();
                    cd.DiscardCardTypeAux[i] = EditorFields.EnumField($"Aux {i + 1}", cd.DiscardCardTypeAux[i], $"card_dctaux_{i}");
                    if (GUILayout.Button("X", EditorStyles.DangerButton, GUILayout.Width(22)))
                    {
                        var list = new List<Enums.CardType>(cd.DiscardCardTypeAux);
                        list.RemoveAt(i);
                        cd.DiscardCardTypeAux = list.ToArray();
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Aux Type", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    var list = new List<Enums.CardType>(cd.DiscardCardTypeAux);
                    list.Add(Enums.CardType.None);
                    cd.DiscardCardTypeAux = list.ToArray();
                    GUI.changed = true;
                }
            }

            //  Add Card 
            if (EditorFields.Section("Add Card", ref _secAddCard))
            {
                var cardIds = EditorFields.CachedIds("card", DataHelper.GetAllCardIds);
                cd.AddCard = EditorFields.IntField("Add Card Count", cd.AddCard, 0, 10);
                if (cd.AddCard > 0)
                {
                cd.AddCardId = EditorFields.IdDropdown("Add Card ID", cd.AddCardId, cardIds, "card_acid", pickerMode: EntityPicker.Mode.Card);
                bool hasId = !string.IsNullOrEmpty(cd.AddCardId);
                if (hasId) GUI.enabled = false;
                cd.AddCardType = EditorFields.EnumField("Add Type", cd.AddCardType, "card_act");
                if (hasId) GUI.enabled = true;
                if (hasId) GUILayout.Label("<color=#888>  Type ignored \u2014 specific ID is set</color>", EditorStyles.RichLabel);
                cd.AddCardChoose = EditorFields.IntFieldMin("Choose", cd.AddCardChoose, 0);
                cd.AddCardFrom = EditorFields.EnumField("From", cd.AddCardFrom, "card_acfrom");
                cd.AddCardPlace = EditorFields.EnumField("Place", cd.AddCardPlace, "card_acplace");

                if (cd.AddCardTypeAux == null) cd.AddCardTypeAux = Array.Empty<Enums.CardType>();
                GUILayout.Label($"<color=#aaa>Aux Add Types ({cd.AddCardTypeAux.Length}):</color>", EditorStyles.RichLabel);
                for (int i = 0; i < cd.AddCardTypeAux.Length; i++)
                {
                    GUILayout.BeginHorizontal();
                    cd.AddCardTypeAux[i] = EditorFields.EnumField($"Aux {i + 1}", cd.AddCardTypeAux[i], $"card_actaux_{i}");
                    if (GUILayout.Button("X", EditorStyles.DangerButton, GUILayout.Width(22)))
                    {
                        var list = new List<Enums.CardType>(cd.AddCardTypeAux);
                        list.RemoveAt(i);
                        cd.AddCardTypeAux = list.ToArray();
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Aux Type", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    var list = new List<Enums.CardType>(cd.AddCardTypeAux);
                    list.Add(Enums.CardType.None);
                    cd.AddCardTypeAux = list.ToArray();
                    GUI.changed = true;
                }
                cd.AddCardReducedCost = EditorFields.IntField("Reduced Cost", cd.AddCardReducedCost);
                var acFlags = new[] { "Cost Turn", "Vanish", "Aux Only", "From Vanish", "Vanish\u2192Deck" };
                var acVals = new[] { cd.AddCardCostTurn, cd.AddCardVanish, cd.AddCardOnlyCheckAuxTypes, cd.AddCardFromVanishPile, cd.AddVanishToDeck };
                EditorFields.ToggleGrid(acFlags, acVals, 3);
                cd.AddCardCostTurn = acVals[0]; cd.AddCardVanish = acVals[1]; cd.AddCardOnlyCheckAuxTypes = acVals[2];
                cd.AddCardFromVanishPile = acVals[3]; cd.AddVanishToDeck = acVals[4];
                } else { GUILayout.Label("<color=#666>Set count > 0 to configure</color>", EditorStyles.RichLabel); }

                GUILayout.Space(4);
                GUILayout.Label($"<color=#aaa>Card List ({cd.AddCardList?.Count ?? 0}):</color>", EditorStyles.RichLabel);
                if (cd.AddCardList == null) cd.AddCardList = new List<string>();
                for (int i = 0; i < cd.AddCardList.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    cd.AddCardList[i] = EditorFields.IdDropdown($"Card {i + 1}", cd.AddCardList[i], cardIds, $"card_acl_{i}", pickerMode: EntityPicker.Mode.Card);
                    if (GUILayout.Button("X", EditorStyles.DangerButton, GUILayout.Width(22)))
                    {
                        cd.AddCardList.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Add to List", EditorStyles.MiniButton, GUILayout.Width(90)))
                {
                    cd.AddCardList.Add("");
                    GUI.changed = true;
                }
            }

            //  Look / Scry 
            if (EditorFields.Section("Look / Scry", ref _secLook))
            {
                cd.LookCards = EditorFields.IntFieldMin("Look Cards", cd.LookCards, 0);
                cd.LookCardsDiscardUpTo = EditorFields.IntFieldMin("Discard Up To", cd.LookCardsDiscardUpTo, 0);
                cd.LookCardsVanishUpTo = EditorFields.IntFieldMin("Vanish Up To", cd.LookCardsVanishUpTo, 0);
            }

            //  Summon 
            if (EditorFields.Section("Summon", ref _secSummon))
            {
                var npcIds = EditorFields.CachedIds("npc", DataHelper.GetAllNpcIds);
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                cd.SummonUnitId = EditorFields.IdDropdown("Summon NPC", cd.SummonUnitId, npcIds, "card_summon", pickerMode: EntityPicker.Mode.NPC);
                if (!string.IsNullOrEmpty(cd.SummonUnitId))
                {
                    cd.SummonNum = EditorFields.IntField("Count", cd.SummonNum, 1, 4);
                    cd.Evolve = EditorFields.Toggle("Evolve", cd.Evolve);
                    cd.Metamorph = EditorFields.Toggle("Metamorph", cd.Metamorph);
                    if (cd.Evolve && cd.Metamorph)
                        GUILayout.Label("<color=#cc8844>  \u26A0 Both Evolve and Metamorph set \u2014 Evolve takes priority</color>", EditorStyles.RichLabel);
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#44cc88>Summon Auras:</color>", EditorStyles.RichLabel);
                    cd.SummonAura = EditorFields.ACChargesField("Aura 1", cd.SummonAura, ref cd.SummonAuraCharges, acIds, "card_saura1");
                    cd.SummonAura2 = EditorFields.ACChargesField("Aura 2", cd.SummonAura2, ref cd.SummonAuraCharges2, acIds, "card_saura2");
                    cd.SummonAura3 = EditorFields.ACChargesField("Aura 3", cd.SummonAura3, ref cd.SummonAuraCharges3, acIds, "card_saura3");
                }
            }

            //  AC Energy Bonus 
            if (EditorFields.Section("AC Energy Bonus", ref _secAcEnergy))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                cd.AcEnergyBonus = EditorFields.ACChargesField("AC Bonus 1", cd.AcEnergyBonus, ref cd.AcEnergyBonusQuantity, acIds, "card_aceb1");
                cd.AcEnergyBonus2 = EditorFields.ACChargesField("AC Bonus 2", cd.AcEnergyBonus2, ref cd.AcEnergyBonus2Quantity, acIds, "card_aceb2");
                if (!string.IsNullOrEmpty(cd.AcEnergyBonus) || !string.IsNullOrEmpty(cd.AcEnergyBonus2))
                    GUILayout.Label("<color=#888>Each stack of the AC grants the listed amount as extra energy</color>", EditorStyles.RichLabel);
                cd.ChooseOneOfAvailableAuras = EditorFields.Toggle("Choose Aura", cd.ChooseOneOfAvailableAuras);
            }

            //  Special Value System 
            if (EditorFields.Section("Special Values", ref _secSpecialVal))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                GUILayout.Label("<color=#aaa>Global:</color>", EditorStyles.RichLabel);
                cd.SpecialValueGlobal = EditorFields.EnumField("SV Global", cd.SpecialValueGlobal, "card_svg");
                if (cd.SpecialValueGlobal != Enums.CardSpecialValue.None)
                {
                    cd.SpecialValueModifierGlobal = EditorFields.FloatField("  Scale %", cd.SpecialValueModifierGlobal);
                    if (cd.SpecialValueGlobal == Enums.CardSpecialValue.AuraCurseYours || cd.SpecialValueGlobal == Enums.CardSpecialValue.AuraCurseTarget)
                        cd.SpecialAuraCurseNameGlobal = EditorFields.IdDropdown("  AC Ref", cd.SpecialAuraCurseNameGlobal, acIds, "card_svacg", pickerMode: EntityPicker.Mode.AuraCurse);
                }
                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Slot 1:</color>", EditorStyles.RichLabel);
                cd.SpecialValue1 = EditorFields.EnumField("SV 1", cd.SpecialValue1, "card_sv1");
                if (cd.SpecialValue1 != Enums.CardSpecialValue.None)
                {
                    cd.SpecialValueModifier1 = EditorFields.FloatField("  Scale %", cd.SpecialValueModifier1);
                    if (cd.SpecialValue1 == Enums.CardSpecialValue.AuraCurseYours || cd.SpecialValue1 == Enums.CardSpecialValue.AuraCurseTarget)
                        cd.SpecialAuraCurseName1 = EditorFields.IdDropdown("  AC Ref", cd.SpecialAuraCurseName1, acIds, "card_svac1", pickerMode: EntityPicker.Mode.AuraCurse);
                }
                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Slot 2:</color>", EditorStyles.RichLabel);
                cd.SpecialValue2 = EditorFields.EnumField("SV 2", cd.SpecialValue2, "card_sv2");
                if (cd.SpecialValue2 != Enums.CardSpecialValue.None)
                {
                    cd.SpecialValueModifier2 = EditorFields.FloatField("  Scale %", cd.SpecialValueModifier2);
                    if (cd.SpecialValue2 == Enums.CardSpecialValue.AuraCurseYours || cd.SpecialValue2 == Enums.CardSpecialValue.AuraCurseTarget)
                        cd.SpecialAuraCurseName2 = EditorFields.IdDropdown("  AC Ref", cd.SpecialAuraCurseName2, acIds, "card_svac2", pickerMode: EntityPicker.Mode.AuraCurse);
                }
            }

            //  Special Value Scaling Flags 
            if (EditorFields.Section("SV Scaling Flags", ref _secSpecialFlags))
            {
                EditorFields.SVFlagsGrid(
                    ref cd.DamageSpecialValueGlobal, ref cd.DamageSpecialValue1, ref cd.DamageSpecialValue2,
                    ref cd.Damage2SpecialValueGlobal, ref cd.Damage2SpecialValue1, ref cd.Damage2SpecialValue2,
                    ref cd.HealSpecialValueGlobal, ref cd.HealSpecialValue1, ref cd.HealSpecialValue2,
                    ref cd.HealSelfSpecialValueGlobal, ref cd.HealSelfSpecialValue1, ref cd.HealSelfSpecialValue2,
                    ref cd.SelfHealthLossSpecialGlobal, ref cd.SelfHealthLossSpecialValue1, ref cd.SelfHealthLossSpecialValue2,
                    ref cd.EnergyRechargeSpecialValueGlobal, ref cd.DrawCardSpecialValueGlobal,
                    ref cd.AuraChargesSpecialValueGlobal, ref cd.AuraChargesSpecialValue1, ref cd.AuraChargesSpecialValue2,
                    ref cd.AuraCharges2SpecialValueGlobal, ref cd.AuraCharges2SpecialValue1, ref cd.AuraCharges2SpecialValue2,
                    ref cd.AuraCharges3SpecialValueGlobal, ref cd.AuraCharges3SpecialValue1, ref cd.AuraCharges3SpecialValue2,
                    ref cd.CurseChargesSpecialValueGlobal, ref cd.CurseChargesSpecialValue1, ref cd.CurseChargesSpecialValue2,
                    ref cd.CurseCharges2SpecialValueGlobal, ref cd.CurseCharges2SpecialValue1, ref cd.CurseCharges2SpecialValue2,
                    ref cd.CurseCharges3SpecialValueGlobal, ref cd.CurseCharges3SpecialValue1, ref cd.CurseCharges3SpecialValue2);
            }

            //  FX / Effects 
            if (EditorFields.Section("Effects / FX", ref _secFx))
            {
                cd.EffectCaster = EditorFields.TextField("FX Caster", cd.EffectCaster);
                cd.EffectTarget = EditorFields.TextField("FX Target", cd.EffectTarget);
                cd.EffectPreAction = EditorFields.TextField("FX PreAction", cd.EffectPreAction);
                cd.EffectPostCastDelay = EditorFields.FloatField("Post Cast Delay", cd.EffectPostCastDelay);
                cd.EffectCasterRepeat = EditorFields.Toggle("Caster Repeat", cd.EffectCasterRepeat);
                cd.EffectCastCenter = EditorFields.Toggle("Cast Center", cd.EffectCastCenter);
                cd.EffectTrail = EditorFields.TextField("Trail", cd.EffectTrail);
                cd.EffectTrailRepeat = EditorFields.Toggle("Trail Repeat", cd.EffectTrailRepeat);
                cd.EffectTrailSpeed = EditorFields.FloatField("Trail Speed", cd.EffectTrailSpeed);
                cd.EffectTrailAngle = EditorFields.EnumField("Trail Angle", cd.EffectTrailAngle, "card_eta");
                cd.EffectPostTargetDelay = EditorFields.FloatField("Post Tgt Delay", cd.EffectPostTargetDelay);
            }

            //  Pet System 
            if (EditorFields.Section("Pet System", ref _secPet))
            {
                cd.PetActivation = EditorFields.EnumField("Activation", cd.PetActivation, "card_petact");
                cd.PetBonusDamageType = EditorFields.EnumField("Bonus Dmg Type", cd.PetBonusDamageType, "card_petdt");
                cd.PetBonusDamageAmount = EditorFields.IntField("Bonus Dmg Amt", cd.PetBonusDamageAmount);
                cd.IsPetAttack = EditorFields.Toggle("Is Pet Attack", cd.IsPetAttack);
                cd.IsPetCast = EditorFields.Toggle("Is Pet Cast", cd.IsPetCast);
                cd.KillPet = EditorFields.Toggle("Kill Pet", cd.KillPet);
                cd.PetTemporal = EditorFields.Toggle("Temporal", cd.PetTemporal);
                cd.PetTemporalAttack = EditorFields.Toggle("Temp Attack", cd.PetTemporalAttack);
                cd.PetTemporalCast = EditorFields.Toggle("Temp Cast", cd.PetTemporalCast);
                cd.PetTemporalMoveToCenter = EditorFields.Toggle("Temp Center", cd.PetTemporalMoveToCenter);
                cd.PetTemporalMoveToBack = EditorFields.Toggle("Temp Back", cd.PetTemporalMoveToBack);
                cd.PetTemporalFadeOutDelay = EditorFields.FloatField("Fade Delay", cd.PetTemporalFadeOutDelay);
                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Pet Visuals:</color>", EditorStyles.RichLabel);
                cd.PetFront = EditorFields.Toggle("Front", cd.PetFront);
                cd.PetInvert = EditorFields.Toggle("Invert", cd.PetInvert);
                cd.PetOffsetX = EditorFields.FloatField("Offset X", cd.PetOffsetX);
                cd.PetOffsetY = EditorFields.FloatField("Offset Y", cd.PetOffsetY);
                cd.PetSizeX = EditorFields.FloatField("Size X", cd.PetSizeX);
                cd.PetSizeY = EditorFields.FloatField("Size Y", cd.PetSizeY);
            }

            //  Upgrade Params 
            if (EditorFields.Section("Upgrade Params", ref _secUpgradeParams))
            {
                GUILayout.Label("<color=#888>Used when auto-generating upgraded (A) variants</color>", EditorStyles.RichLabel);
                cd.UpgDamageMult = EditorFields.FloatField("Dmg Mult", cd.UpgDamageMult);
                cd.UpgBonusCurseCharges = EditorFields.IntField("+ Curse Chg", cd.UpgBonusCurseCharges);
                cd.UpgBonusAuraCharges = EditorFields.IntField("+ Aura Chg", cd.UpgBonusAuraCharges);
                cd.UpgBonusHeal = EditorFields.IntField("+ Heal", cd.UpgBonusHeal);
            }

            // ── Variant auto-save (for upgrade variants) ───
            if (IsEditingVariant && GUI.changed)
            {
                if (cd.Id != _prevVariantId && !string.IsNullOrEmpty(cd.Id))
                {
                    bool wasPatch = proj.CardPatches.ContainsKey(_prevVariantId);
                    var dict = wasPatch ? proj.CardPatches : proj.Cards;
                    dict.Remove(_prevVariantId);
                    dict[cd.Id] = cd;
                    ModProjectLoader.DeleteCard(proj, _prevVariantId, wasPatch);
                }
                bool isPatch = proj.CardPatches.ContainsKey(cd.Id);
                ModProjectLoader.SaveCard(proj, cd, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ══════════════════════════════════════════════════════════
        //  ITEM FIELDS SECTIONS (drawn when cd.HasItemData)
        // ══════════════════════════════════════════════════════════

        private void DrawItemFieldsSections(ItemFields d)
        {
            EditorStyles.Separator();
            GUILayout.Label("<b><color=#e8c06a>\u2666 Item Properties</color></b>", EditorStyles.RichLabel);
            GUILayout.Space(2);

            //  Activation / Requisite 
            if (EditorFields.Section("Item: Activation / Requisite", ref _secActivation))
            {
                d.Activation = EditorFields.EnumField("Trigger", d.Activation, "item_act");
                d.ActivationManual = EditorFields.EnumField("Manual", d.ActivationManual, "item_actman");
                d.ActivateOnReceive = EditorFields.Toggle("Activate On Receive", d.ActivateOnReceive);
                d.ActivationOnlyOnHeroes = EditorFields.Toggle("Only On Heroes", d.ActivationOnlyOnHeroes);
                d.ItemTarget = EditorFields.EnumField("Target", d.ItemTarget, "item_tgt");
                d.OverrideTargetText = EditorFields.EnumField("Override Target Text", d.OverrideTargetText, "item_ovrtgt");
                d.PreventApplyForHeroTarget = EditorFields.Toggle("No Hero Target Apply", d.PreventApplyForHeroTarget);
                d.DontTargetBoss = EditorFields.Toggle("Don't Target Boss", d.DontTargetBoss);
                d.TimesPerTurn = EditorFields.IntField("Per Turn", d.TimesPerTurn);
                d.TimesPerCombat = EditorFields.IntField("Per Combat", d.TimesPerCombat);
                d.ExactRound = EditorFields.IntField("Exact Round", d.ExactRound);
                d.RoundCycle = EditorFields.IntField("Round Cycle", d.RoundCycle);

                GUILayout.Space(4);
                GUILayout.Label("<color=#888>Requisite:</color>", EditorStyles.RichLabel);
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                d.AuraCurseSetted = EditorFields.IdDropdown("AC Setted", d.AuraCurseSetted, acIds, "it_acs", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuraCurseSetted2 = EditorFields.IdDropdown("AC Setted 2", d.AuraCurseSetted2, acIds, "it_acs2", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuraCurseSetted3 = EditorFields.IdDropdown("AC Setted 3", d.AuraCurseSetted3, acIds, "it_acs3", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuraCurseNumForOneEvent = EditorFields.IntField("AC Num For Event", d.AuraCurseNumForOneEvent);
                d.CastedCardType = EditorFields.EnumField("Casted Card Type", d.CastedCardType, "item_cct");
                d.UsedEnergy = EditorFields.Toggle("Used Energy", d.UsedEnergy);
                d.LowerOrEqualPercentHP = EditorFields.FloatField("HP \u2264 %", d.LowerOrEqualPercentHP);
                d.EmptyHand = EditorFields.Toggle("Empty Hand", d.EmptyHand);
                d.NotShowCharacterBonus = EditorFields.Toggle("Not Show Char Bonus", d.NotShowCharacterBonus);
                d.PetActivation = EditorFields.EnumField("Pet Activation", d.PetActivation, "item_pet");
            }

            //  Damage Bonuses 
            if (EditorFields.Section("Item: Damage Bonuses", ref _secItemDamage))
            {
                d.DamageFlatBonus = EditorFields.EnumField("Flat Type", d.DamageFlatBonus, "item_dfb1");
                d.DamageFlatBonusValue = EditorFields.IntField("Flat Value", d.DamageFlatBonusValue);
                d.DamageFlatBonus2 = EditorFields.EnumField("Flat Type 2", d.DamageFlatBonus2, "item_dfb2");
                d.DamageFlatBonusValue2 = EditorFields.IntField("Flat Value 2", d.DamageFlatBonusValue2);
                d.DamageFlatBonus3 = EditorFields.EnumField("Flat Type 3", d.DamageFlatBonus3, "item_dfb3");
                d.DamageFlatBonusValue3 = EditorFields.IntField("Flat Value 3", d.DamageFlatBonusValue3);
                GUILayout.Space(4);
                d.DamagePercentBonus = EditorFields.EnumField("% Type", d.DamagePercentBonus, "item_dpb1");
                d.DamagePercentBonusValue = EditorFields.FloatField("% Value", d.DamagePercentBonusValue);
                d.DamagePercentBonus2 = EditorFields.EnumField("% Type 2", d.DamagePercentBonus2, "item_dpb2");
                d.DamagePercentBonusValue2 = EditorFields.FloatField("% Value 2", d.DamagePercentBonusValue2);
                d.DamagePercentBonus3 = EditorFields.EnumField("% Type 3", d.DamagePercentBonus3, "item_dpb3");
                d.DamagePercentBonusValue3 = EditorFields.FloatField("% Value 3", d.DamagePercentBonusValue3);
            }

            //  Resist Bonuses 
            if (EditorFields.Section("Item: Resist Bonuses", ref _secResist))
            {
                d.ResistModified1 = EditorFields.EnumField("Resist Type", d.ResistModified1, "item_rm1");
                d.ResistModifiedValue1 = EditorFields.IntField("Resist Value", d.ResistModifiedValue1);
                d.ResistModified2 = EditorFields.EnumField("Resist Type 2", d.ResistModified2, "item_rm2");
                d.ResistModifiedValue2 = EditorFields.IntField("Resist Value 2", d.ResistModifiedValue2);
                d.ResistModified3 = EditorFields.EnumField("Resist Type 3", d.ResistModified3, "item_rm3");
                d.ResistModifiedValue3 = EditorFields.IntField("Resist Value 3", d.ResistModifiedValue3);
            }

            //  Character Stat 
            if (EditorFields.Section("Item: Character Stat", ref _secStat))
            {
                d.CharacterStatModified = EditorFields.EnumField("Stat", d.CharacterStatModified, "item_csm");
                d.CharacterStatModifiedValue = EditorFields.IntField("Value", d.CharacterStatModifiedValue);
                d.CharacterStatModified2 = EditorFields.EnumField("Stat 2", d.CharacterStatModified2, "item_csm2");
                d.CharacterStatModifiedValue2 = EditorFields.IntField("Value 2", d.CharacterStatModifiedValue2);
                d.CharacterStatModified3 = EditorFields.EnumField("Stat 3", d.CharacterStatModified3, "item_csm3");
                d.CharacterStatModifiedValue3 = EditorFields.IntField("Value 3", d.CharacterStatModifiedValue3);
                d.MaxHealth = EditorFields.IntField("Max Health", d.MaxHealth);
            }

            //  Heal Bonuses 
            if (EditorFields.Section("Item: Heal Bonuses", ref _secItemHeal))
            {
                d.HealFlatBonus = EditorFields.IntField("Heal Flat Bonus", d.HealFlatBonus);
                d.HealPercentBonus = EditorFields.FloatField("Heal % Bonus", d.HealPercentBonus);
                d.HealReceivedFlatBonus = EditorFields.IntField("Heal Recv Flat", d.HealReceivedFlatBonus);
                d.HealReceivedPercentBonus = EditorFields.FloatField("Heal Recv %", d.HealReceivedPercentBonus);
                d.HealQuantity = EditorFields.IntField("Heal Quantity", d.HealQuantity);
                DrawSpecialValue("Heal SV", ref d.HealQuantitySpecialValue);
                d.HealPercentQuantity = EditorFields.IntField("Heal % Quantity", d.HealPercentQuantity);
                d.HealPercentQuantitySelf = EditorFields.IntField("Heal % Qty Self", d.HealPercentQuantitySelf);
                d.HealSelfPerDamageDonePercent = EditorFields.FloatField("Lifesteal %", d.HealSelfPerDamageDonePercent);
                d.HealSelfTeamPerDamageDonePercent = EditorFields.Toggle("Lifesteal Team", d.HealSelfTeamPerDamageDonePercent);
                d.HealBasedOnAuraCurse = EditorFields.IntField("Heal Based On AC", d.HealBasedOnAuraCurse);
            }

            //  Energy / Draw 
            if (EditorFields.Section("Item: Energy / Draw", ref _secEnergyDraw))
            {
                d.EnergyQuantity = EditorFields.IntField("Energy", d.EnergyQuantity);
                d.DrawCards = EditorFields.IntField("Draw Cards", d.DrawCards);
                d.DrawMultiplyByEnergyUsed = EditorFields.Toggle("Draw \u00d7 Energy", d.DrawMultiplyByEnergyUsed);
            }

            //  AC Gain (target) 
            if (EditorFields.Section("Item: AC Gain (Target)", ref _secAcGain))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                d.AuracurseGain1 = EditorFields.IdDropdown("Apply AC", d.AuracurseGain1, acIds, "it_acg1", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseGainValue1 = EditorFields.IntField("Charges", d.AuracurseGainValue1);
                DrawSpecialValue("SV 1", ref d.AuracurseGain1SpecialValue);
                d.Acg1MultiplyByEnergyUsed = EditorFields.Toggle("\u00d7 Energy", d.Acg1MultiplyByEnergyUsed);

                GUILayout.Space(2);
                d.AuracurseGain2 = EditorFields.IdDropdown("Apply AC 2", d.AuracurseGain2, acIds, "it_acg2", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseGainValue2 = EditorFields.IntField("Charges 2", d.AuracurseGainValue2);
                DrawSpecialValue("SV 2", ref d.AuracurseGain2SpecialValue);
                d.Acg2MultiplyByEnergyUsed = EditorFields.Toggle("\u00d7 Energy 2", d.Acg2MultiplyByEnergyUsed);

                GUILayout.Space(2);
                d.AuracurseGain3 = EditorFields.IdDropdown("Apply AC 3", d.AuracurseGain3, acIds, "it_acg3", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseGainValue3 = EditorFields.IntField("Charges 3", d.AuracurseGainValue3);
                DrawSpecialValue("SV 3", ref d.AuracurseGain3SpecialValue);
                d.Acg3MultiplyByEnergyUsed = EditorFields.Toggle("\u00d7 Energy 3", d.Acg3MultiplyByEnergyUsed);
                d.ChooseOneACToGain = EditorFields.Toggle("Choose One AC", d.ChooseOneACToGain);
            }

            //  AC Gain (self) 
            if (EditorFields.Section("Item: AC Gain (Self)", ref _secAcSelf))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                d.AuracurseGainSelf1 = EditorFields.IdDropdown("Self AC", d.AuracurseGainSelf1, acIds, "it_acs1", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseGainSelfValue1 = EditorFields.IntField("Self Charges", d.AuracurseGainSelfValue1);
                d.AuracurseGainSelf2 = EditorFields.IdDropdown("Self AC 2", d.AuracurseGainSelf2, acIds, "it_acs2b", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseGainSelfValue2 = EditorFields.IntField("Self Charges 2", d.AuracurseGainSelfValue2);
                d.AuracurseGainSelf3 = EditorFields.IdDropdown("Self AC 3", d.AuracurseGainSelf3, acIds, "it_acs3b", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseGainSelfValue3 = EditorFields.IntField("Self Charges 3", d.AuracurseGainSelfValue3);
            }

            //  Dispel / Purge 
            if (EditorFields.Section("Item: Dispel / Purge", ref _secDispel))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                d.AuracurseHeal1 = EditorFields.IdDropdown("Dispel AC", d.AuracurseHeal1, acIds, "it_ach1", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseHeal2 = EditorFields.IdDropdown("Dispel AC 2", d.AuracurseHeal2, acIds, "it_ach2", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseHeal3 = EditorFields.IdDropdown("Dispel AC 3", d.AuracurseHeal3, acIds, "it_ach3", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AcHealFromTarget = EditorFields.Toggle("Heal From Target", d.AcHealFromTarget);
                d.StealAuras = EditorFields.IntField("Steal Auras", d.StealAuras);
                GUILayout.Space(4);
                d.ChanceToDispel = EditorFields.IntField("Dispel %", d.ChanceToDispel);
                d.ChanceToDispelNum = EditorFields.IntField("Dispel Num", d.ChanceToDispelNum);
                d.ChanceToPurge = EditorFields.IntField("Purge %", d.ChanceToPurge);
                d.ChanceToPurgeNum = EditorFields.IntField("Purge Num", d.ChanceToPurgeNum);
                d.ChanceToDispelSelf = EditorFields.IntField("Self Dispel %", d.ChanceToDispelSelf);
                d.ChanceToDispelNumSelf = EditorFields.IntField("Self Dispel Num", d.ChanceToDispelNumSelf);
            }

            //  Passive AC Bonuses 
            if (EditorFields.Section("Item: AC Bonuses", ref _secAcBonus))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                d.AuracurseBonus1 = EditorFields.IdDropdown("AC Bonus 1", d.AuracurseBonus1, acIds, "it_acb1", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseBonusValue1 = EditorFields.IntField("Value 1", d.AuracurseBonusValue1);
                d.AuracurseBonus2 = EditorFields.IdDropdown("AC Bonus 2", d.AuracurseBonus2, acIds, "it_acb2", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseBonusValue2 = EditorFields.IntField("Value 2", d.AuracurseBonusValue2);
                d.IncreaseAurasSelf = EditorFields.IntField("Increase Auras Self", d.IncreaseAurasSelf);
            }

            //  AC Immunities 
            if (EditorFields.Section("Item: AC Immunities", ref _secAcImmune))
            {
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                d.AuracurseImmune1 = EditorFields.IdDropdown("Immune 1", d.AuracurseImmune1, acIds, "it_imm1", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseImmune2 = EditorFields.IdDropdown("Immune 2", d.AuracurseImmune2, acIds, "it_imm2", pickerMode: EntityPicker.Mode.AuraCurse);
            }

            //  Card Gain 
            if (EditorFields.Section("Item: Card Gain", ref _secCardGain))
            {
                d.CardNum = EditorFields.IntField("Card Num", d.CardNum);
                var cardIds = EditorFields.CachedIds("card", DataHelper.GetAllCardIds);
                d.CardToGain = EditorFields.IdDropdown("Card to Gain", d.CardToGain, cardIds, "it_ctg", pickerMode: EntityPicker.Mode.Card);
                d.CardToGainType = EditorFields.EnumField("Card Type", d.CardToGainType, "item_ctgt");
                d.CardPlace = EditorFields.EnumField("Card Place", d.CardPlace, "item_cp");

                GUILayout.Space(4);
                GUILayout.Label("<color=#888>Card Gain List:</color>", EditorStyles.RichLabel);
                if (d.CardToGainList == null) d.CardToGainList = new List<string>();
                for (int i = 0; i < d.CardToGainList.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    d.CardToGainList[i] = EditorFields.IdDropdown($"[{i}]", d.CardToGainList[i], cardIds, $"it_cgl{i}", pickerMode: EntityPicker.Mode.Card);
                    if (GUILayout.Button("\u00d7", GUILayout.Width(22)))
                    {
                        d.CardToGainList.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Add Card", EditorStyles.MiniButton, GUILayout.Width(80)))
                {
                    d.CardToGainList.Add("");
                    GUI.changed = true;
                }
            }

            //  Cost / Economy 
            if (EditorFields.Section("Item: Cost / Economy", ref _secCostEcon))
            {
                d.CostZero = EditorFields.Toggle("Cost Zero", d.CostZero);
                d.CostReduction = EditorFields.IntField("Cost Reduction", d.CostReduction);
                d.CardsReduced = EditorFields.IntField("Cards Reduced", d.CardsReduced);
                d.CardToReduceType = EditorFields.EnumField("Reduce Type", d.CardToReduceType, "item_crt");
                d.CostReduceReduction = EditorFields.IntField("Reduce Amount", d.CostReduceReduction);
                d.CostReduceEnergyRequirement = EditorFields.IntField("Energy Req", d.CostReduceEnergyRequirement);
                d.CostReducePermanent = EditorFields.Toggle("Reduce Permanent", d.CostReducePermanent);
                d.ReduceHighestCost = EditorFields.Toggle("Reduce Highest Cost", d.ReduceHighestCost);
            }

            //  Rewards / Discounts 
            if (EditorFields.Section("Item: Rewards / Discounts", ref _secRewards))
            {
                d.PercentRetentionEndGame = EditorFields.IntField("Retention %", d.PercentRetentionEndGame);
                d.PercentDiscountShop = EditorFields.IntField("Shop Discount %", d.PercentDiscountShop);
            }

            //  Damage To Target 
            if (EditorFields.Section("Item: Damage To Target", ref _secDmgTarget))
            {
                d.DamageToTargetType = EditorFields.EnumField("Dmg Type", d.DamageToTargetType, "item_dtt1");
                d.DamageToTarget = EditorFields.IntField("Dmg Value", d.DamageToTarget);
                d.DttMultiplyByEnergyUsed = EditorFields.Toggle("\u00d7 Energy", d.DttMultiplyByEnergyUsed);
                DrawSpecialValue("DTT SV 1", ref d.DttSpecialValues1);

                GUILayout.Space(4);
                d.DamageToTargetType2 = EditorFields.EnumField("Dmg Type 2", d.DamageToTargetType2, "item_dtt2");
                d.DamageToTarget2 = EditorFields.IntField("Dmg Value 2", d.DamageToTarget2);
                DrawSpecialValue("DTT SV 2", ref d.DttSpecialValues2);

                GUILayout.Space(4);
                d.ModifiedDamageType = EditorFields.EnumField("Modified Dmg Type", d.ModifiedDamageType, "item_mdt");
            }

            //  Flags 
            if (EditorFields.Section("Item: Flags", ref _secItemFlags))
            {
                d.CursedItem = EditorFields.Toggle("Cursed", d.CursedItem);
                d.DropOnly = EditorFields.Toggle("Drop Only", d.DropOnly);
                d.QuestItem = EditorFields.Toggle("Quest Item", d.QuestItem);
                d.DestroyAfterUse = EditorFields.Toggle("Destroy After Use", d.DestroyAfterUse);
                d.Vanish = EditorFields.Toggle("Vanish", d.Vanish);
                d.Permanent = EditorFields.Toggle("Permanent", d.Permanent);
                d.DuplicateActive = EditorFields.Toggle("Duplicate Active", d.DuplicateActive);
                d.PassSingleAndCharacterRolls = EditorFields.Toggle("Pass Rolls", d.PassSingleAndCharacterRolls);
                d.OnlyAddItemToNPCs = EditorFields.Toggle("Only Add To NPCs", d.OnlyAddItemToNPCs);
                d.AddVanishToDeck = EditorFields.Toggle("Add Vanish To Deck", d.AddVanishToDeck);
            }

            //  Enchantment 
            if (EditorFields.Section("Item: Enchantment", ref _secEnchant))
            {
                d.IsEnchantment = EditorFields.Toggle("Is Enchantment", d.IsEnchantment);
                d.UseTheNextInsteadWhenYouPlay = EditorFields.Toggle("Use Next Instead", d.UseTheNextInsteadWhenYouPlay);
                d.DestroyAfterUses = EditorFields.IntField("Destroy After Uses", d.DestroyAfterUses);
                d.DestroyStartOfTurn = EditorFields.Toggle("Destroy Start Of Turn", d.DestroyStartOfTurn);
                d.DestroyEndOfTurn = EditorFields.Toggle("Destroy End Of Turn", d.DestroyEndOfTurn);
                d.CastEnchantmentOnFinishSelfCast = EditorFields.Toggle("Cast On Self Finish", d.CastEnchantmentOnFinishSelfCast);
            }

            //  Custom AC 
            if (EditorFields.Section("Item: Custom AC", ref _secCustomAC))
            {
                d.AuracurseCustomString = EditorFields.TextField("Custom String", d.AuracurseCustomString);
                var acIds = EditorFields.CachedIds("ac", DataHelper.GetAllAuraCurseIds);
                d.AuracurseCustomAC = EditorFields.IdDropdown("Custom AC", d.AuracurseCustomAC, acIds, "it_cac", pickerMode: EntityPicker.Mode.AuraCurse);
                d.AuracurseCustomModValue1 = EditorFields.IntField("Mod Value 1", d.AuracurseCustomModValue1);
                d.AuracurseCustomModValue2 = EditorFields.IntField("Mod Value 2", d.AuracurseCustomModValue2);
            }

            //  Debuff Conversion 
            if (EditorFields.Section("Item: Debuff Conversion", ref _secDebuffConvert))
            {
                d.ConvertReceivedDebuffsIntoDamage = EditorFields.EnumField("Debuffs\u2192Damage", d.ConvertReceivedDebuffsIntoDamage, "item_dcnvdmg");
                d.ConvertReceivedDebuffsIntoCurse = EditorFields.Toggle("Debuffs\u2192Curse", d.ConvertReceivedDebuffsIntoCurse);
            }

            //  FX / Effects 
            if (EditorFields.Section("Item: FX / Effects", ref _secItemFx))
            {
                d.EffectItemOwner = EditorFields.TextField("Effect Owner", d.EffectItemOwner);
                d.EffectCaster = EditorFields.TextField("Effect Caster", d.EffectCaster);
                d.EffectCasterDelay = EditorFields.FloatField("Caster Delay", d.EffectCasterDelay);
                d.EffectTarget = EditorFields.TextField("Effect Target", d.EffectTarget);
                d.EffectTargetDelay = EditorFields.FloatField("Target Delay", d.EffectTargetDelay);
            }

            EditorStyles.Separator();
        }

        // ══════════════════════════════════════════════════════════
        //  GAME ENTITY BROWSER (override without base snapshot)
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// When a game card is selected but not yet overridden, show variant tabs
        /// so the user can choose which variant(s) to override independently.
        /// </summary>
        protected override void DrawGameEntityBrowser(ModProject proj, string id)
        {
            // Check if any variant of this card is already in the project
            // If so, pass through to normal DrawAllSections by finding that def
            var existingDef = FindProjectCard(id, proj);
            if (existingDef == null)
            {
                // Look for any variant of this base card in the project
                foreach (var kvp in proj.Cards)
                    if (string.Equals(kvp.Value.BaseCardId, id, StringComparison.OrdinalIgnoreCase)) { existingDef = kvp.Value; break; }
                if (existingDef == null)
                    foreach (var kvp in proj.CardPatches)
                        if (string.Equals(kvp.Value.BaseCardId, id, StringComparison.OrdinalIgnoreCase)) { existingDef = kvp.Value; break; }
            }
            if (existingDef != null)
            {
                DrawAllSections(existingDef, proj);
                return;
            }

            // No project entry — show game card's tab bar for browsing
            var gameCard = DataHelper.GetCard(id);
            if (gameCard == null)
            {
                GUILayout.Label($"<i>Card <b>{id}</b> not found in game data.</i>", EditorStyles.RichLabel);
                return;
            }

            bool isEquipment = gameCard.CardClass == Enums.CardClass.Item;
            var labels = isEquipment ? _equipUpgradeLabels : _cardUpgradeLabels;

            if (_prevBaseId != id)
            {
                _prevBaseId = id;
                _upgradeTab = 0;
            }
            if (_upgradeTab >= labels.Length) _upgradeTab = 0;

            // Draw tab bar — all tabs show as game-only (green) since nothing is in project
            DrawGameBrowserTabBar(id, labels, isEquipment);
            GUILayout.Space(4);

            // Show override UI for the selected tab
            var level = TabIndexToLevel(_upgradeTab, isEquipment);
            string variantId = _upgradeTab == 0 ? id : FindGameVariantId(id, level);

            GUILayout.Space(20);

            if (variantId != null)
            {
                var variantCard = DataHelper.GetCard(variantId);
                if (variantCard != null)
                {
                    string btnLabel = _upgradeTab == 0 ? "Override Base" : $"Override {labels[_upgradeTab]} Variant";
                    GUILayout.Label($"<color=#8B8>Game card <b>{variantId}</b> — click to override and edit.</color>",
                        EditorStyles.RichLabel);
                    GUILayout.Space(8);
                    if (GUILayout.Button(btnLabel, UnknownMod.Editor.EditorStyles.MiniButton, GUILayout.Width(200)))
                    {
                        var snapshot = SnapshotBaseEntity(variantId);
                        if (snapshot != null)
                        {
                            proj.CardPatches[snapshot.Id] = snapshot;
                            ModProjectLoader.SaveCard(proj, snapshot, true);
                            proj.IsDirty = true;
                            proj.LastChangeTime = Time.realtimeSinceStartup;
                        }
                    }
                }
            }
            else
            {
                GUILayout.Label($"<color=#555>No <b>{labels[_upgradeTab]}</b> variant exists for <b>{id}</b>.</color>",
                    EditorStyles.RichLabel);
            }
        }

        private void DrawGameBrowserTabBar(string baseId, string[] labels, bool isEquipment)
        {
            GUILayout.BeginHorizontal();
            for (int t = 0; t < labels.Length; t++)
            {
                bool active = _upgradeTab == t;
                var level = TabIndexToLevel(t, isEquipment);
                bool exists = t == 0 || FindGameVariantId(baseId, level) != null;

                string label = labels[t];
                string text;
                if (active) text = $"<b><color=cyan>{label}</color></b>";
                else if (exists) text = $"<color=#8B8>{label}</color>";
                else text = $"<color=#555>{label}</color>";

                var style = active ? UnknownMod.Editor.EditorStyles.SubTabActive : GUI.skin.button;
                if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                    _upgradeTab = t;
            }
            GUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════════════
        //  UPGRADE VARIANT SUPPORT
        // ══════════════════════════════════════════════════════════

        /// <summary>If cd is a variant, find its base card; otherwise return cd itself.
        /// Falls back to game data if the base isn't in the project (variant-only override).</summary>
        private static CardDef ResolveBaseDef(CardDef cd, ModProject proj)
        {
            if (cd.CardUpgraded == Enums.CardUpgraded.No) return cd;
            if (!string.IsNullOrEmpty(cd.BaseCardId))
            {
                var baseDef = FindProjectCard(cd.BaseCardId, proj);
                if (baseDef != null) return baseDef;
                // Variant-only override — use game base card for tab bar reference
                var gameBase = DataHelper.GetCard(cd.BaseCardId);
                if (gameBase != null)
                {
                    var ghost = new CardDef { Id = cd.BaseCardId };
                    // Set CardClass so HasItemData / isEquipment resolves correctly
                    ghost.CardClass = gameBase.CardClass;
                    if (gameBase.CardClass == Enums.CardClass.Item)
                        ghost.Item = new ItemFields();
                    return ghost;
                }
            }
            return cd;
        }

        private static int TabIndexFor(Enums.CardUpgraded level, bool isEquipment)
        {
            if (isEquipment)
                return level == Enums.CardUpgraded.Rare ? 1 : 0;
            return level switch
            {
                Enums.CardUpgraded.A => 1,
                Enums.CardUpgraded.B => 2,
                Enums.CardUpgraded.Rare => 3,
                _ => 0,
            };
        }

        private static Enums.CardUpgraded TabIndexToLevel(int tab, bool isEquipment)
        {
            if (isEquipment)
                return tab == 1 ? Enums.CardUpgraded.Rare : Enums.CardUpgraded.No;
            return tab switch
            {
                1 => Enums.CardUpgraded.A,
                2 => Enums.CardUpgraded.B,
                3 => Enums.CardUpgraded.Rare,
                _ => Enums.CardUpgraded.No,
            };
        }

        private void DrawUpgradeTabBar(CardDef baseDef, ModProject proj, string[] labels)
        {
            bool baseInProject = FindProjectCard(baseDef.Id, proj) != null;
            GUILayout.BeginHorizontal();
            for (int t = 0; t < labels.Length; t++)
            {
                bool active = _upgradeTab == t;
                var level = TabIndexToLevel(t, baseDef.HasItemData);
                bool inProject = t == 0 ? baseInProject : FindVariant(baseDef.Id, proj, level) != null;
                bool inGame = !inProject && (t == 0 ? DataHelper.GetCard(baseDef.Id) != null : FindGameVariantId(baseDef.Id, level) != null);
                bool exists = inProject || inGame;

                string label = labels[t];
                string text;
                if (active) text = $"<b><color=cyan>{label}</color></b>";
                else if (inProject) text = label;
                else if (inGame) text = $"<color=#8B8>{label}</color>";  // dimmer = game-only
                else text = $"<color=#555>{label}</color>";

                var style = active ? EditorStyles.SubTabActive : GUI.skin.button;
                if (GUILayout.Button(text, style, GUILayout.ExpandWidth(false)))
                    _upgradeTab = t;
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Show a revert/delete button for the active variant card.</summary>
        private void DrawVariantRemoveButton(CardDef vDef, ModProject proj, bool isBase)
        {
            bool isPatch = proj.CardPatches.ContainsKey(vDef.Id);
            bool isNew = proj.Cards.ContainsKey(vDef.Id);
            if (!isPatch && !isNew) return;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            string btnLabel = isPatch ? "Revert Override"
                : isBase ? "Delete Card" : "Delete Variant";
            var btnStyle = EditorStyles.DangerButton;
            if (GUILayout.Button(btnLabel, btnStyle, GUILayout.Width(130)))
            {
                if (isPatch)
                    proj.CardPatches.Remove(vDef.Id);
                else
                    proj.Cards.Remove(vDef.Id);
                ModProjectLoader.DeleteCard(proj, vDef.Id, isPatch);

                // Deleting a new base card also removes all its variants
                if (isBase && isNew)
                {
                    var orphans = new List<string>();
                    foreach (var kvp in proj.Cards)
                        if (string.Equals(kvp.Value.BaseCardId, vDef.Id, StringComparison.OrdinalIgnoreCase)) orphans.Add(kvp.Key);
                    foreach (var id in orphans)
                    {
                        proj.Cards.Remove(id);
                        ModProjectLoader.DeleteCard(proj, id, false);
                    }
                    var patchOrphans = new List<string>();
                    foreach (var kvp in proj.CardPatches)
                        if (string.Equals(kvp.Value.BaseCardId, vDef.Id, StringComparison.OrdinalIgnoreCase)) patchOrphans.Add(kvp.Key);
                    foreach (var id in patchOrphans)
                    {
                        proj.CardPatches.Remove(id);
                        ModProjectLoader.DeleteCard(proj, id, true);
                    }
                }

                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
                _prevVariantId = null;
                // Only clear selection when deleting a new base card entirely
                if (isBase && isNew) SelectedId = null;
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Find a variant of the given base card with the specified upgrade level.</summary>
        private static CardDef FindVariant(string baseId, ModProject proj, Enums.CardUpgraded level)
        {
            foreach (var kvp in proj.Cards)
                if (string.Equals(kvp.Value.BaseCardId, baseId, StringComparison.OrdinalIgnoreCase) && kvp.Value.CardUpgraded == level)
                    return kvp.Value;
            foreach (var kvp in proj.CardPatches)
                if (string.Equals(kvp.Value.BaseCardId, baseId, StringComparison.OrdinalIgnoreCase) && kvp.Value.CardUpgraded == level)
                    return kvp.Value;
            return null;
        }

        private void DrawCreateVariantUI(CardDef baseDef, ModProject proj,
            Enums.CardUpgraded level, string label)
        {
            string suffix = level switch
            {
                Enums.CardUpgraded.A => "a",
                Enums.CardUpgraded.B => "b",
                Enums.CardUpgraded.Rare => "rare",
                _ => "",
            };

            // For the base card, the "game variant" is the base itself
            string gameVariantId = level == Enums.CardUpgraded.No
                ? (DataHelper.GetCard(baseDef.Id) != null ? baseDef.Id : null)
                : FindGameVariantId(baseDef.Id, level);

            GUILayout.Space(20);

            if (gameVariantId != null)
            {
                GUILayout.Label($"<color=#8B8>Game variant <b>{gameVariantId}</b> exists. Override it to edit.</color>",
                    EditorStyles.RichLabel);
                GUILayout.Space(8);

                if (GUILayout.Button(level == Enums.CardUpgraded.No ? $"Override {label}" : $"Override {label} Variant",
                    EditorStyles.MiniButton, GUILayout.Width(200)))
                {
                    var variant = SnapshotBaseEntity(gameVariantId);
                    if (variant != null)
                    {
                        proj.CardPatches[variant.Id] = variant;
                        ModProjectLoader.SaveCard(proj, variant, true);
                        proj.IsDirty = true;
                        proj.LastChangeTime = Time.realtimeSinceStartup;
                    }
                }
            }
            else
            {
                string variantId = baseDef.Id + suffix;
                GUILayout.Label($"<color=#888>No <b>{label}</b> variant exists for <b>{baseDef.Id}</b>.</color>",
                    EditorStyles.RichLabel);
                GUILayout.Space(8);

                if (GUILayout.Button($"Create {label} Variant",
                    EditorStyles.MiniButton, GUILayout.Width(180)))
                {
                    // Seed from project base card, or snapshot the game card if base is a ghost
                    CardDef seedDef = FindProjectCard(baseDef.Id, proj);
                    if (seedDef == null)
                        seedDef = SnapshotBaseEntity(baseDef.Id);
                    if (seedDef == null)
                        seedDef = baseDef;

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(seedDef,
                        Newtonsoft.Json.Formatting.None,
                        new Newtonsoft.Json.JsonSerializerSettings { DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include });
                    var variant = Newtonsoft.Json.JsonConvert.DeserializeObject<CardDef>(json);
                    variant.Id = variantId;
                    variant.BaseCardId = baseDef.Id;
                    variant.CardUpgraded = level;
                    variant.IsUpgraded = true;

                    bool isPatch = DataHelper.GetCard(variantId) != null;
                    if (isPatch)
                        proj.CardPatches[variantId] = variant;
                    else
                        proj.Cards[variantId] = variant;

                    ModProjectLoader.SaveCard(proj, variant, isPatch);
                    proj.IsDirty = true;
                    proj.LastChangeTime = Time.realtimeSinceStartup;
                }
            }
        }

        private static CardDef FindProjectCard(string id, ModProject proj)
        {
            if (proj.Cards.TryGetValue(id, out var d)) return d;
            if (proj.CardPatches.TryGetValue(id, out d)) return d;
            return null;
        }

        /// <summary>
        /// Find a game-only variant ID by checking the base card's UpgradesTo fields.
        /// Returns null if the variant doesn't exist in game data.
        /// </summary>
        private static string FindGameVariantId(string baseId, Enums.CardUpgraded level)
        {
            var baseCard = DataHelper.GetCard(baseId);
            if (baseCard == null) return null;

            string variantId = level switch
            {
                Enums.CardUpgraded.A => baseCard.UpgradesTo1,
                Enums.CardUpgraded.B => baseCard.UpgradesTo2,
                Enums.CardUpgraded.Rare => baseCard.UpgradesToRare?.Id,
                _ => null,
            };

            if (string.IsNullOrEmpty(variantId)) return null;
            return DataHelper.GetCard(variantId) != null ? variantId : null;
        }

        // ══════════════════════════════════════════════════════════
        //  SPECIAL VALUE WIDGET
        // ══════════════════════════════════════════════════════════

        /// <summary>Build combined list of pet model sources: project pet SpriteSkins + game cards with pet models.</summary>
        private static List<string> GetPetModelSourceIds(ModProject proj)
        {
            var ids = new List<string>();
            foreach (var kvp in proj.SpriteSkins)
                if (kvp.Value.SkinTarget == SkinTargetType.Item) ids.Add(kvp.Key);
            foreach (var kvp in proj.SpriteSkinPatches)
                if (kvp.Value.SkinTarget == SkinTargetType.Item && !ids.Contains(kvp.Key)) ids.Add(kvp.Key);
            foreach (var id in DataHelper.GetAllPetModelCardIds())
                if (!ids.Contains(id)) ids.Add(id);
            return ids;
        }

        private void DrawSpecialValue(string label, ref SpecialValueDef sv)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#888>{label}:</color>", EditorStyles.RichLabel, GUILayout.Width(80));
            bool hasSV = sv != null;
            bool newHas = EditorFields.Toggle("Use", hasSV);
            if (newHas && !hasSV)
                sv = new SpecialValueDef { Use = true };
            else if (!newHas && hasSV)
                sv = null;
            GUILayout.EndHorizontal();

            if (sv != null)
            {
                sv.Name = EditorFields.EnumField("  SV Name", sv.Name, $"sv_{label.GetHashCode()}");
                sv.Multiplier = EditorFields.FloatField("  Multiplier", sv.Multiplier);
            }
        }
    }
}
