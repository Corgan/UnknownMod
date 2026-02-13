using System;
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
    /// IMGUI panel for editing Card definitions at the mod-project level.
    /// Supports creating new cards and overriding base-game ones.
    /// </summary>
    public class CardEditor
    {
        private readonly ZoneEditor _parent;

        // ── Override browser state ───────────────────────────────
        private bool _showOverrideBrowser;
        private Vector2 _overrideScroll;
        private string _overrideFilter = "";

        // ── Collapsible section state ────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secClassify = false;
        private bool _secCost = false;
        private bool _secFlags = false;
        private bool _secUpgradePaths = false;
        private bool _secDamage = true;
        private bool _secCurses = true;
        private bool _secAuras = true;
        private bool _secSelf = false;
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

        public CardEditor(ZoneEditor parent) => _parent = parent;

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
            return GUI.changed && !string.IsNullOrEmpty(_parent.SelectedCardId);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOD-PROJECT PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DrawModProjectPanel(ModProject proj)
        {
            // ── Build combined entity list ───────────────────────
            var allIds = new List<string>();
            var badges = new Dictionary<string, string>();

            foreach (var id in proj.Cards.Keys.OrderBy(k => k))
            {
                allIds.Add(id);
                badges[id] = "[NEW]";
            }
            foreach (var id in proj.CardPatches.Keys.OrderBy(k => k))
            {
                if (!allIds.Contains(id))
                    allIds.Add(id);
                badges[id] = "[OVR]";
            }

            // ── Entity selector ──────────────────────────────────
            string sel = EditorFields.EntitySelector(
                _parent.SelectedCardId, allIds,
                id =>
                {
                    string badge = badges.ContainsKey(id) ? badges[id] : "";
                    string name = "";
                    if (proj.Cards.TryGetValue(id, out var c))
                        name = c.Name;
                    else if (proj.CardPatches.TryGetValue(id, out var cp))
                        name = cp.Name;
                    return $"{badge} {id}  {name}";
                },
                "card_sel");
            if (sel != _parent.SelectedCardId)
                _parent.SelectedCardId = sel;

            // ── Action bar: New / Override / Delete ───────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                string newId = $"{proj.ModId}_new_card";
                int suffix = 1;
                while (proj.Cards.ContainsKey(newId) || proj.CardPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_new_card{suffix++}";
                var def = new CardDef { Id = newId, Name = "New Card" };
                proj.Cards[newId] = def;
                _parent.SelectedCardId = newId;
                ModProjectLoader.SaveEntity(proj, "cards", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Override \u25BE", EditorStyles.MiniButton, GUILayout.Width(80)))
            {
                _showOverrideBrowser = !_showOverrideBrowser;
                _overrideFilter = "";
            }

            if (!string.IsNullOrEmpty(_parent.SelectedCardId))
            {
                string sid = _parent.SelectedCardId;
                if (proj.Cards.ContainsKey(sid))
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(55)))
                    {
                        proj.Cards.Remove(sid);
                        ModProjectLoader.DeleteEntity(proj, "cards", sid);
                        _parent.SelectedCardId = allIds.Count > 1 ? allIds.FirstOrDefault(k => k != sid) : null;
                        proj.IsDirty = true;
                    }
                }
                else if (proj.CardPatches.ContainsKey(sid))
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(55)))
                    {
                        proj.CardPatches.Remove(sid);
                        ModProjectLoader.DeleteEntity(proj, "cards", sid, isPatch: true);
                        _parent.SelectedCardId = allIds.Count > 1 ? allIds.FirstOrDefault(k => k != sid) : null;
                        proj.IsDirty = true;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Override browser popup ────────────────────────────
            if (_showOverrideBrowser)
                DrawOverrideBrowser(proj);

            EditorStyles.Separator();

            // ── Resolve which def to edit ────────────────────────
            CardDef d = null;
            bool isPatch = false;
            if (!string.IsNullOrEmpty(_parent.SelectedCardId))
            {
                if (proj.Cards.TryGetValue(_parent.SelectedCardId, out var newDef))
                    d = newDef;
                else if (proj.CardPatches.TryGetValue(_parent.SelectedCardId, out var patchDef))
                {
                    d = patchDef;
                    isPatch = true;
                }
            }

            if (d == null)
            {
                GUILayout.Label("<i>Select a card above, or create/override one.</i>",
                    EditorStyles.RichLabel);
                return;
            }

            if (isPatch)
                GUILayout.Label("<color=#ffcc44>  \u26A0 Override of base-game Card</color>",
                    EditorStyles.RichLabel);

            // ── Draw all field sections ──────────────────────────
            DrawAllSections(d);

            // ── Auto-save on change ──────────────────────────────
            if (GUI.changed)
            {
                ModProjectLoader.SaveEntity(proj, "cards", d.Id, d, isPatch);
                proj.IsDirty = true;
                proj.LastChangeTime = Time.realtimeSinceStartup;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  OVERRIDE BROWSER
        // ═══════════════════════════════════════════════════════════════

        private void DrawOverrideBrowser(ModProject proj)
        {
            GUILayout.BeginVertical(EditorStyles.CompactBox);
            GUILayout.Label("<b>Browse base-game Cards to override:</b>", EditorStyles.RichLabel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("\u2315", GUILayout.Width(16));
            _overrideFilter = GUILayout.TextField(_overrideFilter);
            GUILayout.EndHorizontal();

            _overrideScroll = GUILayout.BeginScrollView(_overrideScroll, GUILayout.Height(200));

            var baseIds = DataHelper.GetAllCardIds();
            string filt = _overrideFilter?.ToLower() ?? "";

            foreach (var id in baseIds)
            {
                if (filt.Length > 0 && !id.ToLower().Contains(filt)) continue;
                if (proj.CardPatches.ContainsKey(id) || proj.Cards.ContainsKey(id)) continue;

                if (GUILayout.Button(id, EditorStyles.DropdownItem, GUILayout.Height(20)))
                {
                    var baseCard = DataHelper.GetCard(id);
                    if (baseCard != null)
                    {
                        var snapshot = ModProjectBuilder.SnapshotCard(baseCard);
                        proj.CardPatches[id] = snapshot;
                        ModProjectLoader.SaveEntity(proj, "cards", id, snapshot, isPatch: true);
                        _parent.SelectedCardId = id;
                        proj.IsDirty = true;
                        _showOverrideBrowser = false;
                    }
                }
            }

            GUILayout.EndScrollView();

            if (GUILayout.Button("Cancel", EditorStyles.MiniButton))
                _showOverrideBrowser = false;

            GUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  FIELD SECTIONS
        // ═══════════════════════════════════════════════════════════════

        private void DrawAllSections(CardDef cd)
        {
            // ── Live Description Preview ─────────────────────────
            if (EditorFields.Section("Description Preview", ref _secPreview))
            {
                string desc = BuildDescription(cd);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                cd.Id = EditorFields.TextField("ID", cd.Id);
                cd.Name = EditorFields.TextField("Name", cd.Name);
                cd.Description = EditorFields.TextArea("Description", cd.Description);
                cd.Fluff = EditorFields.TextField("Fluff", cd.Fluff);
                cd.FluffPercent = EditorFields.FloatField("Fluff %", cd.FluffPercent);
                cd.EnergyCost = EditorFields.IntField("Energy Cost", cd.EnergyCost);
                cd.IsUpgraded = EditorFields.Toggle("Is Upgraded", cd.IsUpgraded);
                if (cd.IsUpgraded)
                    cd.BaseCardId = EditorFields.IdDropdown("Base Card", cd.BaseCardId, DataHelper.GetAllCardIds(), "card_base");
            }

            // ── Classification ───────────────────────────────────
            if (EditorFields.Section("Classification", ref _secClassify))
            {
                cd.CardUpgraded = EditorFields.EnumField("Upgraded Level", cd.CardUpgraded, "card_cupg");
                cd.CardRarity = EditorFields.EnumField("Rarity", cd.CardRarity, "card_rarity");
                cd.CardType = EditorFields.EnumField("Card Type", cd.CardType, "card_ctype");
                cd.CardClass = EditorFields.EnumField("Class", cd.CardClass, "card_cclass");
                cd.CardNumber = EditorFields.IntField("Card Number", cd.CardNumber);
                cd.MaxInDeck = EditorFields.IntField("Max In Deck", cd.MaxInDeck);
                cd.Sku = EditorFields.TextField("SKU", cd.Sku);
            }

            // ── Cost / Economy ───────────────────────────────────
            if (EditorFields.Section("Cost / Economy", ref _secCost))
            {
                cd.EnergyCostOriginal = EditorFields.IntField("Cost Original", cd.EnergyCostOriginal);
                cd.EnergyCostForShow = EditorFields.IntField("Cost For Show", cd.EnergyCostForShow);
                cd.EnergyReductionPermanent = EditorFields.IntField("Perm Reduction", cd.EnergyReductionPermanent);
                cd.EnergyReductionTemporal = EditorFields.IntField("Temp Reduction", cd.EnergyReductionTemporal);
                cd.EnergyReductionToZeroPermanent = EditorFields.Toggle("Perm Zero Cost", cd.EnergyReductionToZeroPermanent);
                cd.EnergyReductionToZeroTemporal = EditorFields.Toggle("Temp Zero Cost", cd.EnergyReductionToZeroTemporal);
            }

            // ── Flags ────────────────────────────────────────────
            if (EditorFields.Section("Flags", ref _secFlags))
            {
                cd.Playable = EditorFields.Toggle("Playable", cd.Playable);
                cd.Visible = EditorFields.Toggle("Visible", cd.Visible);
                cd.ShowInTome = EditorFields.Toggle("Show In Tome", cd.ShowInTome);
                cd.AutoplayDraw = EditorFields.Toggle("Autoplay Draw", cd.AutoplayDraw);
                cd.AutoplayEndTurn = EditorFields.Toggle("Autoplay EOT", cd.AutoplayEndTurn);
                cd.Vanish = EditorFields.Toggle("Vanish", cd.Vanish);
                cd.Innate = EditorFields.Toggle("Innate", cd.Innate);
                cd.Lazy = EditorFields.Toggle("Lazy", cd.Lazy);
                cd.Corrupted = EditorFields.Toggle("Corrupted", cd.Corrupted);
                cd.EndTurn = EditorFields.Toggle("End Turn", cd.EndTurn);
                cd.Starter = EditorFields.Toggle("Starter", cd.Starter);
                cd.FlipSprite = EditorFields.Toggle("Flip Sprite", cd.FlipSprite);
                cd.ModifiedByTrait = EditorFields.Toggle("Modified by Trait", cd.ModifiedByTrait);
                cd.OnlyInWeekly = EditorFields.Toggle("Only In Weekly", cd.OnlyInWeekly);
            }

            // ── Upgrade Paths ────────────────────────────────────
            if (EditorFields.Section("Upgrade Paths", ref _secUpgradePaths))
            {
                var cardIds = DataHelper.GetAllCardIds();
                cd.UpgradesTo1 = EditorFields.IdDropdown("Upgrades To 1", cd.UpgradesTo1, cardIds, "card_upto1");
                cd.UpgradesTo2 = EditorFields.IdDropdown("Upgrades To 2", cd.UpgradesTo2, cardIds, "card_upto2");
                cd.UpgradedFrom = EditorFields.IdDropdown("Upgraded From", cd.UpgradedFrom, cardIds, "card_upfrom");
                cd.BaseCard = EditorFields.IdDropdown("Base Card", cd.BaseCard, cardIds, "card_basec");
                cd.RelatedCard = EditorFields.IdDropdown("Related 1", cd.RelatedCard, cardIds, "card_rel1");
                cd.RelatedCard2 = EditorFields.IdDropdown("Related 2", cd.RelatedCard2, cardIds, "card_rel2");
                cd.RelatedCard3 = EditorFields.IdDropdown("Related 3", cd.RelatedCard3, cardIds, "card_rel3");
            }

            // ── Damage ───────────────────────────────────────────
            if (EditorFields.Section("Damage", ref _secDamage))
            {
                cd.Damage = EditorFields.IntField("Damage", cd.Damage);
                cd.DamageType = EditorFields.EnumField("Type", cd.DamageType, "card_dmg1");
                cd.DamageSides = EditorFields.IntField("Sides", cd.DamageSides);
                cd.DamageEnergyBonus = EditorFields.IntField("Energy Bonus", cd.DamageEnergyBonus);
                cd.IgnoreBlock = EditorFields.Toggle("Ignore Block", cd.IgnoreBlock);
                GUILayout.Space(4);
                cd.Damage2 = EditorFields.IntField("Damage 2", cd.Damage2);
                cd.DamageType2 = EditorFields.EnumField("Type 2", cd.DamageType2, "card_dmg2");
                cd.DamageSides2 = EditorFields.IntField("Sides 2", cd.DamageSides2);
                cd.IgnoreBlock2 = EditorFields.Toggle("Ignore Block 2", cd.IgnoreBlock2);
                GUILayout.Space(4);
                cd.DamageSelf = EditorFields.IntField("Damage Self", cd.DamageSelf);
                cd.DamageSelf2 = EditorFields.IntField("Damage Self 2", cd.DamageSelf2);
                cd.SelfHealthLoss = EditorFields.IntField("Self HP Loss", cd.SelfHealthLoss);
                cd.SelfKillHiddenSeconds = EditorFields.FloatField("SelfKill Delay", cd.SelfKillHiddenSeconds);
            }

            // ── Curses (target) ──────────────────────────────────
            if (EditorFields.Section("Curses (Target)", ref _secCurses))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                cd.Curse = EditorFields.IdDropdown("Curse", cd.Curse, acIds, "cd_curse");
                cd.CurseCharges = EditorFields.IntField("Charges", cd.CurseCharges);
                cd.CurseChargesSides = EditorFields.IntField("Charge Sides", cd.CurseChargesSides);
                cd.Curse2 = EditorFields.IdDropdown("Curse 2", cd.Curse2, acIds, "cd_curse2");
                cd.Curse2Charges = EditorFields.IntField("Charges 2", cd.Curse2Charges);
                cd.Curse3 = EditorFields.IdDropdown("Curse 3", cd.Curse3, acIds, "cd_curse3");
                cd.Curse3Charges = EditorFields.IntField("Charges 3", cd.Curse3Charges);
            }

            // ── Auras (target) ───────────────────────────────────
            if (EditorFields.Section("Auras (Target)", ref _secAuras))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                cd.Aura = EditorFields.IdDropdown("Aura", cd.Aura, acIds, "cd_aura");
                cd.AuraCharges = EditorFields.IntField("Charges", cd.AuraCharges);
                cd.Aura2 = EditorFields.IdDropdown("Aura 2", cd.Aura2, acIds, "cd_aura2");
                cd.Aura2Charges = EditorFields.IntField("Charges 2", cd.Aura2Charges);
                cd.Aura3 = EditorFields.IdDropdown("Aura 3", cd.Aura3, acIds, "cd_aura3");
                cd.Aura3Charges = EditorFields.IntField("Charges 3", cd.Aura3Charges);
            }

            // ── Self Auras/Curses ────────────────────────────────
            if (EditorFields.Section("Self Effects", ref _secSelf))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                cd.AuraSelf = EditorFields.IdDropdown("Aura Self", cd.AuraSelf, acIds, "cd_auraself");
                cd.AuraSelfCharges = EditorFields.IntField("Charges", cd.AuraSelfCharges);
                cd.AuraSelf2 = EditorFields.IdDropdown("Aura Self 2", cd.AuraSelf2, acIds, "cd_auraself2");
                cd.AuraSelf2Charges = EditorFields.IntField("Charges 2", cd.AuraSelf2Charges);
                cd.AuraSelf3 = EditorFields.IdDropdown("Aura Self 3", cd.AuraSelf3, acIds, "cd_auraself3");
                cd.AuraSelf3Charges = EditorFields.IntField("Charges 3", cd.AuraSelf3Charges);
                GUILayout.Space(4);
                cd.CurseSelf = EditorFields.IdDropdown("Curse Self", cd.CurseSelf, acIds, "cd_curseself");
                cd.CurseSelfCharges = EditorFields.IntField("Charges", cd.CurseSelfCharges);
                cd.CurseSelf2 = EditorFields.IdDropdown("Curse Self 2", cd.CurseSelf2, acIds, "cd_curseself2");
                cd.CurseSelf2Charges = EditorFields.IntField("Charges 2", cd.CurseSelf2Charges);
                cd.CurseSelf3 = EditorFields.IdDropdown("Curse Self 3", cd.CurseSelf3, acIds, "cd_curseself3");
                cd.CurseSelf3Charges = EditorFields.IntField("Charges 3", cd.CurseSelf3Charges);
            }

            // ── Heal ─────────────────────────────────────────────
            if (EditorFields.Section("Heal", ref _secHeal))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                cd.Heal = EditorFields.IntField("Heal (target)", cd.Heal);
                cd.HealSides = EditorFields.IntField("Heal Sides", cd.HealSides);
                cd.HealSelf = EditorFields.IntField("Heal Self", cd.HealSelf);
                cd.HealEnergyBonus = EditorFields.IntField("Heal Nrg Bonus", cd.HealEnergyBonus);
                cd.HealSelfPerDamageDonePercent = EditorFields.FloatField("Lifesteal %", cd.HealSelfPerDamageDonePercent);
                cd.HealCurses = EditorFields.IntField("Dispel Curses", cd.HealCurses);
                cd.DispelAuras = EditorFields.IntField("Purge Auras", cd.DispelAuras);
                GUILayout.Space(4);
                cd.HealAuraCurseSelf = EditorFields.IdDropdown("Heal AC Self", cd.HealAuraCurseSelf, acIds, "cd_hacself");
                cd.HealAuraCurseName = EditorFields.IdDropdown("Heal AC 1", cd.HealAuraCurseName, acIds, "cd_hac1");
                cd.HealAuraCurseName2 = EditorFields.IdDropdown("Heal AC 2", cd.HealAuraCurseName2, acIds, "cd_hac2");
                cd.HealAuraCurseName3 = EditorFields.IdDropdown("Heal AC 3", cd.HealAuraCurseName3, acIds, "cd_hac3");
                cd.HealAuraCurseName4 = EditorFields.IdDropdown("Heal AC 4", cd.HealAuraCurseName4, acIds, "cd_hac4");
            }

            // ── AC Manipulation ──────────────────────────────────
            if (EditorFields.Section("AC Manipulation", ref _secAcManip))
            {
                cd.TransferCurses = EditorFields.IntField("Transfer Curses", cd.TransferCurses);
                cd.StealAuras = EditorFields.IntField("Steal Auras", cd.StealAuras);
                cd.ReduceCurses = EditorFields.IntField("Reduce Curses", cd.ReduceCurses);
                cd.ReduceAuras = EditorFields.IntField("Reduce Auras", cd.ReduceAuras);
                cd.IncreaseCurses = EditorFields.IntField("Increase Curses", cd.IncreaseCurses);
                cd.IncreaseAuras = EditorFields.IntField("Increase Auras", cd.IncreaseAuras);
            }

            // ── Targeting ────────────────────────────────────────
            if (EditorFields.Section("Targeting", ref _secTarget))
            {
                cd.TargetSide = EditorFields.EnumField("Side", cd.TargetSide, "card_tside");
                cd.TargetType = EditorFields.EnumField("Type", cd.TargetType, "card_ttype");
                cd.TargetPos = EditorFields.EnumField("Position", cd.TargetPos, "card_tpos");
            }

            // ── Effect Repeat ────────────────────────────────────
            if (EditorFields.Section("Effect Repeat", ref _secRepeat))
            {
                cd.EffectRepeat = EditorFields.IntField("Repeat", cd.EffectRepeat);
                cd.EffectRepeatDelay = EditorFields.FloatField("Delay", cd.EffectRepeatDelay);
                cd.EffectRepeatTarget = EditorFields.EnumField("Repeat Target", cd.EffectRepeatTarget, "card_ereptgt");
                cd.EffectRepeatEnergyBonus = EditorFields.IntField("Nrg Bonus", cd.EffectRepeatEnergyBonus);
                cd.EffectRepeatMaxBonus = EditorFields.IntField("Max Bonus", cd.EffectRepeatMaxBonus);
                cd.EffectRepeatModificator = EditorFields.IntField("Modificator", cd.EffectRepeatModificator);
                cd.MoveToCenter = EditorFields.Toggle("Move to Center", cd.MoveToCenter);
            }

            // ── Misc Mechanics ───────────────────────────────────
            if (EditorFields.Section("Mechanics", ref _secMechanics))
            {
                cd.PushTarget = EditorFields.IntField("Push Target", cd.PushTarget);
                cd.PullTarget = EditorFields.IntField("Pull Target", cd.PullTarget);
                cd.DrawCard = EditorFields.IntField("Draw Cards", cd.DrawCard);
                cd.DiscardCard = EditorFields.IntField("Discard Cards", cd.DiscardCard);
                cd.EnergyRecharge = EditorFields.IntField("Energy Recharge", cd.EnergyRecharge);
                cd.GoldGainQuantity = EditorFields.IntField("Gold Gain", cd.GoldGainQuantity);
                cd.ShardsGainQuantity = EditorFields.IntField("Shards Gain", cd.ShardsGainQuantity);
                cd.ExhaustCounter = EditorFields.IntField("Exhaust Counter", cd.ExhaustCounter);
                cd.EffectRequired = EditorFields.TextField("Effect Required", cd.EffectRequired);
            }

            // ── Discard Options ──────────────────────────────────
            if (EditorFields.Section("Discard Options", ref _secDiscard))
            {
                cd.DiscardCardType = EditorFields.EnumField("Discard Type", cd.DiscardCardType, "card_dct");
                cd.DiscardCardAutomatic = EditorFields.Toggle("Automatic", cd.DiscardCardAutomatic);
                cd.DiscardCardPlace = EditorFields.EnumField("Discard Place", cd.DiscardCardPlace, "card_dcp");
            }

            // ── Add Card ─────────────────────────────────────────
            if (EditorFields.Section("Add Card", ref _secAddCard))
            {
                var cardIds = DataHelper.GetAllCardIds();
                cd.AddCard = EditorFields.IntField("Add Card Count", cd.AddCard);
                cd.AddCardId = EditorFields.IdDropdown("Add Card ID", cd.AddCardId, cardIds, "card_acid");
                cd.AddCardType = EditorFields.EnumField("Add Type", cd.AddCardType, "card_act");
                cd.AddCardChoose = EditorFields.IntField("Choose", cd.AddCardChoose);
                cd.AddCardFrom = EditorFields.EnumField("From", cd.AddCardFrom, "card_acfrom");
                cd.AddCardPlace = EditorFields.EnumField("Place", cd.AddCardPlace, "card_acplace");
                cd.AddCardReducedCost = EditorFields.IntField("Reduced Cost", cd.AddCardReducedCost);
                cd.AddCardCostTurn = EditorFields.Toggle("Cost This Turn", cd.AddCardCostTurn);
                cd.AddCardVanish = EditorFields.Toggle("Vanish", cd.AddCardVanish);
                cd.AddCardOnlyCheckAuxTypes = EditorFields.Toggle("Aux Types Only", cd.AddCardOnlyCheckAuxTypes);
                cd.AddCardFromVanishPile = EditorFields.Toggle("From Vanish Pile", cd.AddCardFromVanishPile);
                cd.AddVanishToDeck = EditorFields.Toggle("Vanish To Deck", cd.AddVanishToDeck);

                GUILayout.Space(4);
                GUILayout.Label($"<color=#aaa>Card List ({cd.AddCardList?.Count ?? 0}):</color>", EditorStyles.RichLabel);
                if (cd.AddCardList == null) cd.AddCardList = new List<string>();
                for (int i = 0; i < cd.AddCardList.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    cd.AddCardList[i] = EditorFields.IdDropdown($"Card {i + 1}", cd.AddCardList[i], cardIds, $"card_acl_{i}");
                    if (GUILayout.Button("X", EditorStyles.DangerButton, GUILayout.Width(22)))
                    {
                        cd.AddCardList.RemoveAt(i);
                        GUI.changed = true;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ Add to List", EditorStyles.MiniButton, GUILayout.Width(90)))
                {
                    cd.AddCardList.Add("");
                    GUI.changed = true;
                }
            }

            // ── Look / Scry ─────────────────────────────────────
            if (EditorFields.Section("Look / Scry", ref _secLook))
            {
                cd.LookCards = EditorFields.IntField("Look Cards", cd.LookCards);
                cd.LookCardsDiscardUpTo = EditorFields.IntField("Discard Up To", cd.LookCardsDiscardUpTo);
                cd.LookCardsVanishUpTo = EditorFields.IntField("Vanish Up To", cd.LookCardsVanishUpTo);
            }

            // ── Summon ───────────────────────────────────────────
            if (EditorFields.Section("Summon", ref _secSummon))
            {
                var npcIds = DataHelper.GetAllNpcIds();
                var acIds = DataHelper.GetAllAuraCurseIds();
                cd.SummonUnitId = EditorFields.IdDropdown("Summon NPC", cd.SummonUnitId, npcIds, "card_summon");
                cd.SummonNum = EditorFields.IntField("Count", cd.SummonNum);
                cd.Evolve = EditorFields.Toggle("Evolve", cd.Evolve);
                cd.Metamorph = EditorFields.Toggle("Metamorph", cd.Metamorph);
                GUILayout.Space(4);
                cd.SummonAura = EditorFields.IdDropdown("Summon Aura", cd.SummonAura, acIds, "card_saura1");
                cd.SummonAuraCharges = EditorFields.IntField("Charges", cd.SummonAuraCharges);
                cd.SummonAura2 = EditorFields.IdDropdown("Summon Aura 2", cd.SummonAura2, acIds, "card_saura2");
                cd.SummonAuraCharges2 = EditorFields.IntField("Charges 2", cd.SummonAuraCharges2);
                cd.SummonAura3 = EditorFields.IdDropdown("Summon Aura 3", cd.SummonAura3, acIds, "card_saura3");
                cd.SummonAuraCharges3 = EditorFields.IntField("Charges 3", cd.SummonAuraCharges3);
            }

            // ── AC Energy Bonus ──────────────────────────────────
            if (EditorFields.Section("AC Energy Bonus", ref _secAcEnergy))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                cd.AcEnergyBonus = EditorFields.IdDropdown("AC Nrg Bonus", cd.AcEnergyBonus, acIds, "card_aceb1");
                cd.AcEnergyBonusQuantity = EditorFields.IntField("Quantity", cd.AcEnergyBonusQuantity);
                cd.AcEnergyBonus2 = EditorFields.IdDropdown("AC Nrg Bonus 2", cd.AcEnergyBonus2, acIds, "card_aceb2");
                cd.AcEnergyBonus2Quantity = EditorFields.IntField("Quantity 2", cd.AcEnergyBonus2Quantity);
                cd.ChooseOneOfAvailableAuras = EditorFields.Toggle("Choose Aura", cd.ChooseOneOfAvailableAuras);
            }

            // ── Special Value System ─────────────────────────────
            if (EditorFields.Section("Special Values", ref _secSpecialVal))
            {
                var acIds = DataHelper.GetAllAuraCurseIds();
                GUILayout.Label("<color=#aaa>Global:</color>", EditorStyles.RichLabel);
                cd.SpecialValueGlobal = EditorFields.EnumField("SV Global", cd.SpecialValueGlobal, "card_svg");
                cd.SpecialValueModifierGlobal = EditorFields.FloatField("Modifier", cd.SpecialValueModifierGlobal);
                cd.SpecialAuraCurseNameGlobal = EditorFields.IdDropdown("AC Global", cd.SpecialAuraCurseNameGlobal, acIds, "card_svacg");
                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Slot 1:</color>", EditorStyles.RichLabel);
                cd.SpecialValue1 = EditorFields.EnumField("SV 1", cd.SpecialValue1, "card_sv1");
                cd.SpecialValueModifier1 = EditorFields.FloatField("Modifier 1", cd.SpecialValueModifier1);
                cd.SpecialAuraCurseName1 = EditorFields.IdDropdown("AC 1", cd.SpecialAuraCurseName1, acIds, "card_svac1");
                GUILayout.Space(4);
                GUILayout.Label("<color=#aaa>Slot 2:</color>", EditorStyles.RichLabel);
                cd.SpecialValue2 = EditorFields.EnumField("SV 2", cd.SpecialValue2, "card_sv2");
                cd.SpecialValueModifier2 = EditorFields.FloatField("Modifier 2", cd.SpecialValueModifier2);
                cd.SpecialAuraCurseName2 = EditorFields.IdDropdown("AC 2", cd.SpecialAuraCurseName2, acIds, "card_svac2");
            }

            // ── Special Value Scaling Flags ──────────────────────
            if (EditorFields.Section("SV Scaling Flags", ref _secSpecialFlags))
            {
                GUILayout.Label("<color=#aaa>Damage:</color>", EditorStyles.RichLabel);
                cd.DamageSpecialValueGlobal = EditorFields.Toggle("Dmg SV Global", cd.DamageSpecialValueGlobal);
                cd.DamageSpecialValue1 = EditorFields.Toggle("Dmg SV 1", cd.DamageSpecialValue1);
                cd.DamageSpecialValue2 = EditorFields.Toggle("Dmg SV 2", cd.DamageSpecialValue2);
                cd.Damage2SpecialValueGlobal = EditorFields.Toggle("Dmg2 SV Global", cd.Damage2SpecialValueGlobal);
                cd.Damage2SpecialValue1 = EditorFields.Toggle("Dmg2 SV 1", cd.Damage2SpecialValue1);
                cd.Damage2SpecialValue2 = EditorFields.Toggle("Dmg2 SV 2", cd.Damage2SpecialValue2);
                GUILayout.Space(2);
                GUILayout.Label("<color=#aaa>Heal:</color>", EditorStyles.RichLabel);
                cd.HealSpecialValueGlobal = EditorFields.Toggle("Heal SV Global", cd.HealSpecialValueGlobal);
                cd.HealSpecialValue1 = EditorFields.Toggle("Heal SV 1", cd.HealSpecialValue1);
                cd.HealSpecialValue2 = EditorFields.Toggle("Heal SV 2", cd.HealSpecialValue2);
                cd.HealSelfSpecialValueGlobal = EditorFields.Toggle("HealSelf SV G", cd.HealSelfSpecialValueGlobal);
                cd.HealSelfSpecialValue1 = EditorFields.Toggle("HealSelf SV 1", cd.HealSelfSpecialValue1);
                cd.HealSelfSpecialValue2 = EditorFields.Toggle("HealSelf SV 2", cd.HealSelfSpecialValue2);
                GUILayout.Space(2);
                GUILayout.Label("<color=#aaa>Self HP / Energy / Draw:</color>", EditorStyles.RichLabel);
                cd.SelfHealthLossSpecialGlobal = EditorFields.Toggle("SelfHP SV G", cd.SelfHealthLossSpecialGlobal);
                cd.SelfHealthLossSpecialValue1 = EditorFields.Toggle("SelfHP SV 1", cd.SelfHealthLossSpecialValue1);
                cd.SelfHealthLossSpecialValue2 = EditorFields.Toggle("SelfHP SV 2", cd.SelfHealthLossSpecialValue2);
                cd.EnergyRechargeSpecialValueGlobal = EditorFields.Toggle("Nrg SV Global", cd.EnergyRechargeSpecialValueGlobal);
                cd.DrawCardSpecialValueGlobal = EditorFields.Toggle("Draw SV Global", cd.DrawCardSpecialValueGlobal);
                GUILayout.Space(2);
                GUILayout.Label("<color=#aaa>Aura Charges:</color>", EditorStyles.RichLabel);
                cd.AuraChargesSpecialValueGlobal = EditorFields.Toggle("Aura1 SV G", cd.AuraChargesSpecialValueGlobal);
                cd.AuraChargesSpecialValue1 = EditorFields.Toggle("Aura1 SV 1", cd.AuraChargesSpecialValue1);
                cd.AuraChargesSpecialValue2 = EditorFields.Toggle("Aura1 SV 2", cd.AuraChargesSpecialValue2);
                cd.AuraCharges2SpecialValueGlobal = EditorFields.Toggle("Aura2 SV G", cd.AuraCharges2SpecialValueGlobal);
                cd.AuraCharges2SpecialValue1 = EditorFields.Toggle("Aura2 SV 1", cd.AuraCharges2SpecialValue1);
                cd.AuraCharges2SpecialValue2 = EditorFields.Toggle("Aura2 SV 2", cd.AuraCharges2SpecialValue2);
                cd.AuraCharges3SpecialValueGlobal = EditorFields.Toggle("Aura3 SV G", cd.AuraCharges3SpecialValueGlobal);
                cd.AuraCharges3SpecialValue1 = EditorFields.Toggle("Aura3 SV 1", cd.AuraCharges3SpecialValue1);
                cd.AuraCharges3SpecialValue2 = EditorFields.Toggle("Aura3 SV 2", cd.AuraCharges3SpecialValue2);
                GUILayout.Space(2);
                GUILayout.Label("<color=#aaa>Curse Charges:</color>", EditorStyles.RichLabel);
                cd.CurseChargesSpecialValueGlobal = EditorFields.Toggle("Curse1 SV G", cd.CurseChargesSpecialValueGlobal);
                cd.CurseChargesSpecialValue1 = EditorFields.Toggle("Curse1 SV 1", cd.CurseChargesSpecialValue1);
                cd.CurseChargesSpecialValue2 = EditorFields.Toggle("Curse1 SV 2", cd.CurseChargesSpecialValue2);
                cd.CurseCharges2SpecialValueGlobal = EditorFields.Toggle("Curse2 SV G", cd.CurseCharges2SpecialValueGlobal);
                cd.CurseCharges2SpecialValue1 = EditorFields.Toggle("Curse2 SV 1", cd.CurseCharges2SpecialValue1);
                cd.CurseCharges2SpecialValue2 = EditorFields.Toggle("Curse2 SV 2", cd.CurseCharges2SpecialValue2);
                cd.CurseCharges3SpecialValueGlobal = EditorFields.Toggle("Curse3 SV G", cd.CurseCharges3SpecialValueGlobal);
                cd.CurseCharges3SpecialValue1 = EditorFields.Toggle("Curse3 SV 1", cd.CurseCharges3SpecialValue1);
                cd.CurseCharges3SpecialValue2 = EditorFields.Toggle("Curse3 SV 2", cd.CurseCharges3SpecialValue2);
            }

            // ── FX / Effects ─────────────────────────────────────
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

            // ── Pet System ───────────────────────────────────────
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
            }

            // ── Upgrade Params ───────────────────────────────────
            if (EditorFields.Section("Upgrade Params", ref _secUpgradeParams))
            {
                GUILayout.Label("<color=#888>Used when auto-generating upgraded (A) variants</color>", EditorStyles.RichLabel);
                cd.UpgDamageMult = EditorFields.FloatField("Dmg Mult", cd.UpgDamageMult);
                cd.UpgBonusCurseCharges = EditorFields.IntField("+ Curse Chg", cd.UpgBonusCurseCharges);
                cd.UpgBonusAuraCharges = EditorFields.IntField("+ Aura Chg", cd.UpgBonusAuraCharges);
                cd.UpgBonusHeal = EditorFields.IntField("+ Heal", cd.UpgBonusHeal);
            }

        }

        // ═══════════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a human-readable description from a CardDef's fields,
        /// mimicking the game's AppendCardDescription logic.
        /// </summary>
        public static string BuildDescription(CardDef cd)
        {
            var sb = new StringBuilder();
            string sep = "";

            // ── Damage ───────────────────────────────────────────
            if (cd.Damage > 0 && cd.DamageType != Enums.DamageType.None)
            {
                sb.Append($"{sep}Deal <color=#e8c06a>{cd.Damage}</color> {DmgColor(cd.DamageType)} damage");
                if (cd.DamageSides > 0) sb.Append($" ({cd.DamageSides} sides)");
                if (cd.IgnoreBlock) sb.Append(" <color=#ff6666>(ignores block)</color>");
                sep = "\n";
            }
            if (cd.Damage2 > 0 && cd.DamageType2 != Enums.DamageType.None)
            {
                if (cd.Damage > 0)
                {
                    sb.Append($" + <color=#e8c06a>{cd.Damage2}</color> {DmgColor(cd.DamageType2)}");
                    if (cd.DamageSides2 > 0) sb.Append($" ({cd.DamageSides2} sides)");
                    if (cd.IgnoreBlock2) sb.Append(" <color=#ff6666>(ignores block)</color>");
                }
                else
                {
                    sb.Append($"{sep}Deal <color=#e8c06a>{cd.Damage2}</color> {DmgColor(cd.DamageType2)} damage");
                    if (cd.DamageSides2 > 0) sb.Append($" ({cd.DamageSides2} sides)");
                    if (cd.IgnoreBlock2) sb.Append(" <color=#ff6666>(ignores block)</color>");
                    sep = "\n";
                }
            }

            // ── Curses on target ─────────────────────────────────
            AppendAC(sb, ref sep, cd.Curse, cd.CurseCharges, "Apply", "#cc4444");
            AppendAC(sb, ref sep, cd.Curse2, cd.Curse2Charges, "Apply", "#cc4444");
            AppendAC(sb, ref sep, cd.Curse3, cd.Curse3Charges, "Apply", "#cc4444");

            // ── Auras on target ──────────────────────────────────
            string auraVerb = cd.TargetSide == Enums.CardTargetSide.Friend ? "Gain" : "Grant";
            AppendAC(sb, ref sep, cd.Aura, cd.AuraCharges, auraVerb, "#44aa44");
            AppendAC(sb, ref sep, cd.Aura2, cd.Aura2Charges, "Grant", "#44aa44");
            AppendAC(sb, ref sep, cd.Aura3, cd.Aura3Charges, "Grant", "#44aa44");

            // ── Self effects ─────────────────────────────────────
            AppendAC(sb, ref sep, cd.AuraSelf, cd.AuraSelfCharges, "Gain", "#44cc88");
            AppendAC(sb, ref sep, cd.AuraSelf2, cd.AuraSelf2Charges, "Gain", "#44cc88");
            AppendAC(sb, ref sep, cd.AuraSelf3, cd.AuraSelf3Charges, "Gain", "#44cc88");
            AppendAC(sb, ref sep, cd.CurseSelf, cd.CurseSelfCharges, "Suffer", "#cc6644");
            AppendAC(sb, ref sep, cd.CurseSelf2, cd.CurseSelf2Charges, "Suffer", "#cc6644");
            AppendAC(sb, ref sep, cd.CurseSelf3, cd.CurseSelf3Charges, "Suffer", "#cc6644");

            // ── Heal ─────────────────────────────────────────────
            if (cd.Heal > 0) { sb.Append($"{sep}Heal <color=#44cc44>{cd.Heal}</color>"); sep = "\n"; }
            if (cd.HealSelf > 0) { sb.Append($"{sep}Heal self <color=#44cc44>{cd.HealSelf}</color>"); sep = "\n"; }
            if (cd.HealCurses > 0) { sb.Append($"{sep}Dispel <color=#88aacc>{cd.HealCurses}</color> curse(s)"); sep = "\n"; }
            if (cd.DispelAuras > 0) { sb.Append($"{sep}Purge <color=#88aacc>{cd.DispelAuras}</color> aura(s)"); sep = "\n"; }
            if (cd.SelfHealthLoss != 0) { sb.Append($"{sep}Lose <color=#cc4444>{cd.SelfHealthLoss}</color> HP"); sep = "\n"; }
            if (cd.DamageSelf != 0) { sb.Append($"{sep}Damage self <color=#cc4444>{cd.DamageSelf}</color>"); sep = "\n"; }
            if (cd.HealSelfPerDamageDonePercent != 0f) { sb.Append($"{sep}Lifesteal <color=#44cc44>{cd.HealSelfPerDamageDonePercent:0.#}%</color>"); sep = "\n"; }

            // ── Mechanics ────────────────────────────────────────
            if (cd.PushTarget != 0) { sb.Append($"{sep}Push <color=#88aacc>{cd.PushTarget}</color>"); sep = "\n"; }
            if (cd.PullTarget != 0) { sb.Append($"{sep}Pull <color=#88aacc>{cd.PullTarget}</color>"); sep = "\n"; }
            if (cd.DrawCard != 0) { sb.Append($"{sep}Draw <color=#ffcc44>{cd.DrawCard}</color> card(s)"); sep = "\n"; }
            if (cd.DiscardCard != 0) { sb.Append($"{sep}Discard <color=#cc6644>{cd.DiscardCard}</color> card(s)"); sep = "\n"; }
            if (cd.EnergyRecharge != 0) { sb.Append($"{sep}+<color=#ffcc44>{cd.EnergyRecharge}</color> Energy"); sep = "\n"; }
            if (cd.GoldGainQuantity != 0) { sb.Append($"{sep}+<color=#e8c06a>{cd.GoldGainQuantity}</color> Gold"); sep = "\n"; }
            if (cd.TransferCurses != 0) { sb.Append($"{sep}Transfer <color=#cc6644>{cd.TransferCurses}</color> curse(s)"); sep = "\n"; }
            if (cd.StealAuras != 0) { sb.Append($"{sep}Steal <color=#44cc88>{cd.StealAuras}</color> aura(s)"); sep = "\n"; }

            // ── Summon ───────────────────────────────────────────
            if (!string.IsNullOrEmpty(cd.SummonUnitId) && cd.SummonNum > 0)
            {
                sb.Append($"{sep}Summon <color=#dd88ff>{cd.SummonNum}x {cd.SummonUnitId}</color>");
                sep = "\n";
            }

            // ── Repeat / Targeting ───────────────────────────────
            if (cd.EffectRepeat > 1) { sb.Append($"{sep}<color=#888>Repeat {cd.EffectRepeat}x</color>"); sep = "\n"; }

            var parts = new List<string>();
            if (cd.TargetSide != Enums.CardTargetSide.Enemy) parts.Add(cd.TargetSide.ToString());
            if (cd.TargetType != Enums.CardTargetType.Single) parts.Add(cd.TargetType.ToString());
            if (cd.TargetPos != Enums.CardTargetPosition.Anywhere) parts.Add(cd.TargetPos.ToString());
            if (parts.Count > 0) sb.Append($"{sep}<color=#666>[{string.Join(", ", parts)}]</color>");

            if (sb.Length == 0) sb.Append("<color=#666>(no effects)</color>");
            return sb.ToString();
        }

        private static void AppendAC(StringBuilder sb, ref string sep, string acId, int charges, string verb, string color)
        {
            if (!string.IsNullOrEmpty(acId) && charges > 0)
            {
                sb.Append($"{sep}{verb} <color={color}>{charges}</color> <color={color}>{CapFirst(acId)}</color>");
                sep = "\n";
            }
        }

        private static string DmgColor(Enums.DamageType dt)
        {
            return dt switch
            {
                Enums.DamageType.Slashing => "<color=#ff9933>Slash</color>",
                Enums.DamageType.Blunt => "<color=#cc9966>Blunt</color>",
                Enums.DamageType.Piercing => "<color=#cccc66>Pierce</color>",
                Enums.DamageType.Fire => "<color=#ff6633>Fire</color>",
                Enums.DamageType.Cold => "<color=#66ccff>Cold</color>",
                Enums.DamageType.Lightning => "<color=#ffff66>Light</color>",
                Enums.DamageType.Mind => "<color=#cc66ff>Mind</color>",
                Enums.DamageType.Holy => "<color=#ffffcc>Holy</color>",
                Enums.DamageType.Shadow => "<color=#9966cc>Shadow</color>",
                _ => dt.ToString()
            };
        }

        private static string CapFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}
