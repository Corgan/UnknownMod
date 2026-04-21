using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
using UnknownMod.Editor.Tabs;
using UnknownMod.Runtime;

namespace UnknownMod.Editor
{
    /// <summary>
    /// IMGUI panel for editing events and their reply options.
    /// Supports full 4-branch outcomes (Success, Fail, Crit Success, Crit Fail)
    /// with all game-matching fields.
    /// </summary>
    public class EventEditor
    {
        private readonly ModEditor _parent;
        private int _expandedReply = -1;

        // Per-reply section toggles (indexed by reply index, resized dynamically)
        private bool[] _secSs = new bool[16];
        private bool[] _secFl = new bool[16];
        private bool[] _secSsc = new bool[16];
        private bool[] _secFlc = new bool[16];

        // Patch browser state
        private bool _showPatchBrowser;
        private Vector2 _patchBrowserScroll;
        private string _patchFilter = "";

        private void EnsureToggleCapacity(int count)
        {
            if (count <= _secSs.Length) return;
            int newSize = System.Math.Max(count, _secSs.Length * 2);
            System.Array.Resize(ref _secSs, newSize);
            System.Array.Resize(ref _secFl, newSize);
            System.Array.Resize(ref _secSsc, newSize);
            System.Array.Resize(ref _secFlc, newSize);
        }

        public EventEditor(ModEditor parent) => _parent = parent;

        public void DrawPanel()
        {
            var proj = ModManagerPanel.ActiveProject;
            if (proj == null) { GUILayout.Label("No project loaded."); return; }

            // Build combined events dict (new + patches)
            var allEvents = new Dictionary<string, EventDef>();
            foreach (var kvp in proj.Events) allEvents[kvp.Key] = kvp.Value;
            foreach (var kvp in proj.EventPatches)
                if (!allEvents.ContainsKey(kvp.Key)) allEvents[kvp.Key] = kvp.Value;

            // ── Entity selector ──────────────────────────────────
            var eventIds = allEvents.Keys.OrderBy(k => k).ToList();
            string sel = EditorFields.EntitySelector(
                _parent.SelectedEventId, eventIds,
                id =>
                {
                    string badge = proj.Events.ContainsKey(id) ? "[NEW] " :
                                   proj.EventPatches.ContainsKey(id) ? "[PATCH] " : "";
                    return allEvents.TryGetValue(id, out var e) ? $"{badge}{id}  ({e.EventName})" : $"{badge}{id}";
                },
                "evt_sel");
            if (sel != _parent.SelectedEventId)
            {
                _parent.SelectedEventId = sel;
                _expandedReply = -1;
                System.Array.Clear(_secSs, 0, _secSs.Length);
                System.Array.Clear(_secFl, 0, _secFl.Length);
                System.Array.Clear(_secSsc, 0, _secSsc.Length);
                System.Array.Clear(_secFlc, 0, _secFlc.Length);
            }

            // ── Action bar: New / Patch / Delete ─────────────────
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ New Event", EditorStyles.MiniButton, GUILayout.Width(90)))
            {
                string newId = $"{proj.ModId}_evt_{proj.Events.Count}";
                int suffix = 0;
                while (proj.Events.ContainsKey(newId) || proj.EventPatches.ContainsKey(newId))
                    newId = $"{proj.ModId}_evt_{++suffix}";

                var def = new EventDef { EventId = newId, EventName = "New Event" };
                proj.Events[newId] = def;
                _parent.SelectedEventId = newId;
                _expandedReply = -1;
                ModProjectLoader.SaveEntity(proj, "events", newId, def);
                proj.IsDirty = true;
            }

            if (GUILayout.Button("Patch Base \u25BE", EditorStyles.MiniButton, GUILayout.Width(100)))
                _showPatchBrowser = !_showPatchBrowser;

            if (!string.IsNullOrEmpty(_parent.SelectedEventId))
            {
                bool isNew = proj.Events.ContainsKey(_parent.SelectedEventId);
                bool isPatch = proj.EventPatches.ContainsKey(_parent.SelectedEventId);
                if (isNew)
                {
                    if (GUILayout.Button("Delete", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.Events.Remove(_parent.SelectedEventId);
                        ModProjectLoader.DeleteEntity(proj, "events", _parent.SelectedEventId);
                        _parent.SelectedEventId = eventIds.FirstOrDefault(k => k != _parent.SelectedEventId);
                        _expandedReply = -1;
                        proj.IsDirty = true;
                    }
                }
                else if (isPatch)
                {
                    if (GUILayout.Button("Revert", EditorStyles.DangerButton, GUILayout.Width(60)))
                    {
                        proj.EventPatches.Remove(_parent.SelectedEventId);
                        ModProjectLoader.DeleteEntity(proj, "events", _parent.SelectedEventId, true);
                        _parent.SelectedEventId = eventIds.FirstOrDefault(k => k != _parent.SelectedEventId);
                        _expandedReply = -1;
                        proj.IsDirty = true;
                    }
                }
            }

            GUILayout.EndHorizontal();

            // ── Patch browser ────────────────────────────────────
            if (_showPatchBrowser)
                DrawPatchBrowser(proj);

            EditorStyles.Separator();

            if (_parent.SelectedEventId == null || !allEvents.TryGetValue(_parent.SelectedEventId, out var ed))
            {
                GUILayout.Label("<i>Select an event above.</i>", EditorStyles.RichLabel);
                return;
            }

            // ── Basic fields ─────────────────────────────────────
            string prevEventId = ed.EventId;
            ed.EventId = EditorFields.TextField("Event ID", ed.EventId);
            if (ed.EventId != prevEventId && !string.IsNullOrEmpty(ed.EventId))
            {
                bool wasNew = proj.Events.ContainsKey(prevEventId);
                bool wasPatch = proj.EventPatches.ContainsKey(prevEventId);
                var dict = wasNew ? proj.Events : wasPatch ? proj.EventPatches : null;
                if (dict != null && !proj.Events.ContainsKey(ed.EventId) && !proj.EventPatches.ContainsKey(ed.EventId))
                {
                    dict.Remove(prevEventId);
                    dict[ed.EventId] = ed;
                    ModProjectLoader.DeleteEntity(proj, "events", prevEventId, wasPatch);
                    ModProjectLoader.SaveEntity(proj, "events", ed.EventId, ed, wasPatch);
                    _parent.SelectedEventId = ed.EventId;
                }
                else
                    ed.EventId = prevEventId;
            }
            ed.EventName = EditorFields.TextField("Name", ed.EventName);
            ed.Description = EditorFields.TextArea("Description", ed.Description);
            ed.DescriptionAction = EditorFields.TextField("Action Text", ed.DescriptionAction);
            ed.EventTier = EditorFields.EnumField("Event Tier", ed.EventTier, "evt_tier");
            ed.ReplyRandom = EditorFields.IntField("Reply Random", ed.ReplyRandom);
            ed.RequirementId = EditorFields.IdDropdown("Requirement ID", ed.RequirementId, DataHelper.GetAllEventRequirementIds(), "evt_reqid", pickerMode: EntityPicker.Mode.Requirement);
            ed.SpriteSource = EditorFields.IdDropdown("Sprite Source", ed.SpriteSource, DataHelper.GetAllEventIds(), "evt_sprsrc", pickerMode: EntityPicker.Mode.Event);
            ed.RequiredClassId = EditorFields.IdDropdown("Required Class", ed.RequiredClassId, DataHelper.GetAllSubClassIds(), "evt_reqclass", pickerMode: EntityPicker.Mode.Hero);
            ed.EventIconShader = EditorFields.EnumField("Icon Shader", ed.EventIconShader, "evt_shader");
            ed.HistoryMode = EditorFields.Toggle("History Mode", ed.HistoryMode);
            ed.EventUniqueId = EditorFields.TextField("Unique ID", ed.EventUniqueId);

            EditorStyles.Separator();
            GUILayout.Label($"<b>Replies ({ed.Replies.Count})</b>", EditorStyles.RichLabel);

            // ── Replies ──────────────────────────────────────────
            var combatIds = proj.Combats.Keys
                .Concat(proj.CombatPatches.Keys)
                .Distinct().OrderBy(k => k).ToList();
            var evtIds = allEvents.Keys.OrderBy(k => k).ToList();
            var nodeIds = ZoneEditingService.CurrentZone?.Nodes?.Keys?.OrderBy(k => k)?.ToList()
                ?? new System.Collections.Generic.List<string>();
            var lootIds = proj.Loot.Keys
                .Concat(proj.LootPatches.Keys)
                .Distinct().OrderBy(k => k).ToList();

            // Grow toggle arrays if needed (preserve existing state)\n            EnsureToggleCapacity(ed.Replies.Count);

            for (int i = 0; i < ed.Replies.Count; i++)
            {
                var r = ed.Replies[i];
                bool expanded = _expandedReply == i;
                string hdr = $"{(expanded ? "\u25BC" : "\u25B6")} Reply {i + 1}: {Truncate(r.ReplyText, 28)}";
                if (GUILayout.Button(hdr, EditorStyles.ListItem))
                    _expandedReply = expanded ? -1 : i;

                if (!expanded) continue;

                GUILayout.BeginVertical(EditorStyles.CompactBox);

                r.ReplyText = EditorFields.TextField("Text", r.ReplyText);
                r.Action = EditorFields.EnumField("Action", r.Action, $"reply_act_{i}");

                // ── Costs ────────────────────────────────────────
                r.GoldCost = EditorFields.IntField("Gold Cost", r.GoldCost);
                r.DustCost = EditorFields.IntField("Dust Cost", r.DustCost);

                // ── Requirements ─────────────────────────────────
                r.RequirementId = EditorFields.IdDropdown("Requirement", r.RequirementId, DataHelper.GetAllEventRequirementIds(), $"rep_req_{i}", pickerMode: EntityPicker.Mode.Requirement);
                r.RequirementBlockedId = EditorFields.IdDropdown("Blocked By", r.RequirementBlockedId, DataHelper.GetAllEventRequirementIds(), $"rep_blocked_{i}", pickerMode: EntityPicker.Mode.Requirement);
                r.RequiredClassId = EditorFields.IdDropdown("Required Class", r.RequiredClassId, DataHelper.GetAllSubClassIds(), $"rep_reqclass_{i}", pickerMode: EntityPicker.Mode.Hero);
                r.RequiredClassForBlockedId = EditorFields.IdDropdown("Blocked Class", r.RequiredClassForBlockedId, DataHelper.GetAllSubClassIds(), $"rep_reqclassblk_{i}", pickerMode: EntityPicker.Mode.Hero);
                r.RequirementSku = EditorFields.TextField("Requirement SKU", r.RequirementSku);
                r.RequirementItemId = EditorFields.IdDropdown("Req Item", r.RequirementItemId, DataHelper.GetAllItemIds(), $"rep_reqitem_{i}", pickerMode: EntityPicker.Mode.Item);
                DrawIdList("Req Items", r.RequirementItemIds, DataHelper.GetAllItemIds(), $"rep_reqitems_{i}", EntityPicker.Mode.Item);
                DrawIdList("Req Cards", r.RequirementCardIds, DataHelper.GetAllCardIds(), $"rep_reqcards_{i}", EntityPicker.Mode.Card);
                r.ReplyShowCardId = EditorFields.IdDropdown("Show Card", r.ReplyShowCardId, DataHelper.GetAllCardIds(), $"rep_showcard_{i}", pickerMode: EntityPicker.Mode.Card);
                r.RequirementMultiplayer = EditorFields.Toggle("Multiplayer Only", r.RequirementMultiplayer);
                r.ChooseReplacementHero = EditorFields.Toggle("Choose Replacement Hero", r.ChooseReplacementHero);

                // Repeat-for-all toggles
                bool hasRepeat = r.RepeatForAllCharacters || r.RepeatForAllWarriors || r.RepeatForAllScouts || r.RepeatForAllMages || r.RepeatForAllHealers;
                if (hasRepeat || GUILayout.Button("+ Repeat For All...", EditorStyles.MiniButton))
                {
                    r.RepeatForAllCharacters = EditorFields.Toggle("Repeat All Heroes", r.RepeatForAllCharacters);
                    r.RepeatForAllWarriors = EditorFields.Toggle("  Warriors", r.RepeatForAllWarriors);
                    r.RepeatForAllScouts = EditorFields.Toggle("  Scouts", r.RepeatForAllScouts);
                    r.RepeatForAllMages = EditorFields.Toggle("  Mages", r.RepeatForAllMages);
                    r.RepeatForAllHealers = EditorFields.Toggle("  Healers", r.RepeatForAllHealers);
                }

                EditorStyles.Separator();

                // ── Roll ─────────────────────────────────────────
                r.HasRoll = EditorFields.Toggle("Has Roll", r.HasRoll);
                if (r.HasRoll)
                {
                    r.RollDC = EditorFields.IntField("Roll DC", r.RollDC);
                    r.RollTarget = EditorFields.EnumField("Roll Target", r.RollTarget, $"reply_rtgt_{i}");
                    r.RollCard = EditorFields.EnumField("Roll Card", r.RollCard, $"reply_rcard_{i}");
                    r.RollMode = EditorFields.EnumField("Roll Mode", r.RollMode, $"reply_rmode_{i}");
                    r.RollCrit = EditorFields.IntField("Crit DC", r.RollCrit);
                    r.RollCritFail = EditorFields.IntField("Crit Fail DC", r.RollCritFail);
                }

                // ── Success Outcome ──────────────────────────────
                EditorStyles.Separator();
                if (EditorFields.Section("<color=#88cc88>Success</color>", ref _secSs[i]))
                    DrawOutcome(r.Ss, "Ss", i, combatIds, evtIds, nodeIds, lootIds);

                // ── Fail Outcome ─────────────────────────────────
                if (EditorFields.Section("<color=#cc8888>Fail</color>", ref _secFl[i]))
                    DrawOutcome(r.Fl, "Fl", i, combatIds, evtIds, nodeIds, lootIds);

                // ── Crit Success Outcome ─────────────────────────
                if (r.HasRoll && r.RollCrit >= 0)
                {
                    if (EditorFields.Section("<color=#88ccff>Crit Success</color>", ref _secSsc[i]))
                        DrawOutcome(r.Ssc, "Ssc", i, combatIds, evtIds, nodeIds, lootIds);
                }

                // ── Crit Fail Outcome ────────────────────────────
                if (r.HasRoll && r.RollCritFail >= 0)
                {
                    if (EditorFields.Section("<color=#ff8888>Crit Fail</color>", ref _secFlc[i]))
                        DrawOutcome(r.Flc, "Flc", i, combatIds, evtIds, nodeIds, lootIds);
                }

                GUILayout.Space(4);
                if (GUILayout.Button("Delete Reply", EditorStyles.DangerButton))
                {
                    ed.Replies.RemoveAt(i);
                    _expandedReply = -1;
                    GUI.changed = true;
                    GUILayout.EndVertical();
                    break;
                }

                GUILayout.EndVertical();
            }

            GUILayout.Space(4);
            if (GUILayout.Button("+ Add Reply", EditorStyles.MiniButton))
            {
                ed.Replies.Add(new ReplyDef { ReplyText = "New reply..." });
                _expandedReply = ed.Replies.Count - 1;
                GUI.changed = true;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PATCH BROWSER — list base-game events to patch
        // ═══════════════════════════════════════════════════════════════

        private void DrawPatchBrowser(ModProject proj)
        {
            GUILayout.Label("<color=#aaa>Select a base-game event to patch:</color>",
                EditorStyles.RichLabel);
            _patchFilter = EditorFields.TextField("Filter", _patchFilter);

            _patchBrowserScroll = GUILayout.BeginScrollView(_patchBrowserScroll, GUILayout.Height(180));
            string filterLow = (_patchFilter ?? "").ToLower();
            var allBaseIds = DataHelper.GetAllEventIds();
            int shown = 0;
            foreach (var id in allBaseIds)
            {
                if (shown >= 50) break;
                if (!string.IsNullOrEmpty(filterLow) && !id.Contains(filterLow)) continue;
                if (proj.Events.ContainsKey(id) || proj.EventPatches.ContainsKey(id)) continue;
                shown++;
                if (GUILayout.Button(id, EditorStyles.LinkButton))
                {
                    // Snapshot the base-game event into an EventDef
                    var baseEvt = DataHelper.GetExistingEvent(id);
                    EventDef patch;
                    if (baseEvt != null)
                        patch = ZoneEditingService.SnapshotEventDef(baseEvt);
                    else
                        patch = new EventDef { EventId = id, EventName = id };

                    proj.EventPatches[id] = patch;
                    _parent.SelectedEventId = id;
                    _expandedReply = -1;
                    ModProjectLoader.SaveEntity(proj, "events", id, patch, isPatch: true);
                    _showPatchBrowser = false;
                    proj.IsDirty = true;
                }
            }
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        //  OUTCOME BRANCH DRAWER (used for Ss, Fl, Ssc, Flc)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw all fields for one outcome branch using direct OutcomeDef field access.
        /// The prefix string is used only for FinishGame/FinishObeliskMap conditionals
        /// and unique IMGUI key generation.
        /// </summary>
        private void DrawOutcome(OutcomeDef o, string prefix, int idx,
            System.Collections.Generic.List<string> combatIds,
            System.Collections.Generic.List<string> evtIds,
            System.Collections.Generic.List<string> nodeIds,
            System.Collections.Generic.List<string> lootIds)
        {
            string k = $"{prefix}_{idx}"; // unique key suffix

            // ── Text & Rewards ───────────────────────────────────
            o.Text = EditorFields.TextArea("Text", o.Text);
            o.HealPercent = EditorFields.FloatField("Heal %", o.HealPercent);
            o.HealFlat = EditorFields.IntField("Heal Flat", o.HealFlat);
            o.Gold = EditorFields.IntField("Gold", o.Gold);
            o.Dust = EditorFields.IntField("Dust", o.Dust);
            o.Supply = EditorFields.IntField("Supply", o.Supply);
            o.XP = EditorFields.IntField("XP", o.XP);

            // ── Combat / Event / Node Travel ─────────────────────
            o.CombatId = EditorFields.IdDropdown("Combat", o.CombatId, combatIds, $"o_cmb_{k}", pickerMode: EntityPicker.Mode.Combat);
            o.EventId = EditorFields.IdDropdown("Event", o.EventId, evtIds, $"o_evt_{k}", pickerMode: EntityPicker.Mode.Event);
            o.NodeTravelId = EditorFields.IdDropdown("Node Travel", o.NodeTravelId, nodeIds, $"o_node_{k}", pickerMode: EntityPicker.Mode.Node);

            // ── Requirements ─────────────────────────────────────
            o.RequirementUnlockId = EditorFields.IdDropdown("Req Unlock", o.RequirementUnlockId, DataHelper.GetAllEventRequirementIds(), $"o_requnlock_{k}", pickerMode: EntityPicker.Mode.Requirement);
            o.RequirementUnlock2Id = EditorFields.IdDropdown("Req Unlock 2", o.RequirementUnlock2Id, DataHelper.GetAllEventRequirementIds(), $"o_requnlock2_{k}", pickerMode: EntityPicker.Mode.Requirement);
            o.RequirementLockId = EditorFields.IdDropdown("Req Lock", o.RequirementLockId, DataHelper.GetAllEventRequirementIds(), $"o_reqlock_{k}", pickerMode: EntityPicker.Mode.Requirement);
            if (prefix == "Ss")
                o.RequirementLock2Id = EditorFields.IdDropdown("Req Lock 2", o.RequirementLock2Id, DataHelper.GetAllEventRequirementIds(), $"o_reqlock2_{k}", pickerMode: EntityPicker.Mode.Requirement);

            // ── Loot / Shop ──────────────────────────────────────
            o.LootId = EditorFields.IdDropdown("Loot", o.LootId, lootIds, $"o_loot_{k}", pickerMode: EntityPicker.Mode.Loot);
            o.ShopId = EditorFields.IdDropdown("Shop", o.ShopId, lootIds, $"o_shop_{k}", pickerMode: EntityPicker.Mode.Loot);

            // ── Add items/cards ──────────────────────
            o.AddItemId = EditorFields.IdDropdown("Add Item", o.AddItemId, DataHelper.GetAllItemIds(), $"o_item_{k}", pickerMode: EntityPicker.Mode.Item);
            o.AddCard1Id = EditorFields.IdDropdown("Add Card 1", o.AddCard1Id, DataHelper.GetAllCardIds(), $"o_card1_{k}", pickerMode: EntityPicker.Mode.Card);
            o.AddCard2Id = EditorFields.IdDropdown("Add Card 2", o.AddCard2Id, DataHelper.GetAllCardIds(), $"o_card2_{k}", pickerMode: EntityPicker.Mode.Card);
            o.AddCard3Id = EditorFields.IdDropdown("Add Card 3", o.AddCard3Id, DataHelper.GetAllCardIds(), $"o_card3_{k}", pickerMode: EntityPicker.Mode.Card);

            // ── Reward tier / shop params ────────────────────────
            o.RewardTier = EditorFields.TextField("Reward Tier", o.RewardTier);
            o.Discount = EditorFields.IntField("Discount", o.Discount);
            o.MaxQuantity = EditorFields.IntField("Max Quantity", o.MaxQuantity);

            // ── UI toggles ───────────────────────────────────────
            o.HealerUI = EditorFields.Toggle("Healer UI", o.HealerUI);
            o.UpgradeUI = EditorFields.Toggle("Upgrade UI", o.UpgradeUI);
            o.CraftUI = EditorFields.Toggle("Craft UI", o.CraftUI);
            o.MerchantUI = EditorFields.Toggle("Merchant UI", o.MerchantUI);
            o.CorruptionUI = EditorFields.Toggle("Corruption UI", o.CorruptionUI);
            o.UpgradeRandomCard = EditorFields.Toggle("Upgrade Random", o.UpgradeRandomCard);
            if (o.CraftUI)
                o.CraftUIMaxType = EditorFields.EnumField("Craft Max Rarity", o.CraftUIMaxType, $"o_craftmax_{k}");
            o.ItemCorruptionUI = EditorFields.Toggle("Item Corrupt UI", o.ItemCorruptionUI);
            o.RemoveItemSlot = EditorFields.EnumField("Remove Item Slot", o.RemoveItemSlot, $"o_remslot_{k}");
            o.CorruptItemSlot = EditorFields.EnumField("Corrupt Item Slot", o.CorruptItemSlot, $"o_corslot_{k}");
            o.UnlockClassId = EditorFields.IdDropdown("Unlock Class", o.UnlockClassId, DataHelper.GetAllSubClassIds(), $"o_unlock_{k}", pickerMode: EntityPicker.Mode.Hero);
            o.UnlockSteamAchievement = EditorFields.TextField("Unlock Achievement", o.UnlockSteamAchievement);
            o.CardPlayerGame = EditorFields.Toggle("Card Game", o.CardPlayerGame);
            if (o.CardPlayerGame)
                o.CardPlayerGamePackId = EditorFields.IdDropdown("Card Game Pack", o.CardPlayerGamePackId, DataHelper.GetAllPackIds(), $"o_cgpack_{k}", pickerMode: EntityPicker.Mode.Pack);
            o.CardPlayerPairsGame = EditorFields.Toggle("Pairs Game", o.CardPlayerPairsGame);
            if (o.CardPlayerPairsGame)
                o.CardPlayerPairsGamePackId = EditorFields.IdDropdown("Pairs Pack", o.CardPlayerPairsGamePackId, DataHelper.GetAllPackIds(), $"o_pppack_{k}", pickerMode: EntityPicker.Mode.Pack);
            o.CharacterReplacementId = EditorFields.IdDropdown("Char Replace", o.CharacterReplacementId, DataHelper.GetAllSubClassIds(), $"o_charrep_{k}", pickerMode: EntityPicker.Mode.Hero);
            if (!string.IsNullOrEmpty(o.CharacterReplacementId))
                o.CharacterReplacementPosition = EditorFields.IntField("Replace Position", o.CharacterReplacementPosition, 0, 3);

            // Finish flags only on Ss/Ssc
            if (prefix == "Ss" || prefix == "Ssc")
            {
                o.FinishGame = EditorFields.Toggle("Finish Game", o.FinishGame);
                o.FinishEarlyAccess = EditorFields.Toggle("Finish Early Access", o.FinishEarlyAccess);
                if (prefix == "Ss")
                {
                    o.FinishObeliskMap = EditorFields.Toggle("Finish Obelisk", o.FinishObeliskMap);
                    o.PerkDataId = EditorFields.TextField("Perk Data ID", o.PerkDataId);
                    o.PerkData1Id = EditorFields.TextField("Perk Data 1 ID", o.PerkData1Id);
                    o.SteamStat = EditorFields.TextField("Steam Stat", o.SteamStat);
                    o.UnlockSkinId = EditorFields.IdDropdown("Unlock Skin", o.UnlockSkinId, DataHelper.GetAllSkinIds(), $"o_unlockskin_{k}", pickerMode: EntityPicker.Mode.Skin);
                }
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        /// <summary>Inline editable list of entity IDs with add/remove.</summary>
        private static void DrawIdList(string label, System.Collections.Generic.List<string> list,
            System.Collections.Generic.List<string> allIds, string keyPrefix, EntityPicker.Mode mode)
        {
            if (list.Count == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, GUILayout.Width(100f));
                if (GUILayout.Button("+", EditorStyles.MiniButton, GUILayout.Width(22)))
                {
                    list.Add("");
                    GUI.changed = true;
                }
                GUILayout.Label("<color=#666>(none)</color>", EditorStyles.RichLabel);
                GUILayout.EndHorizontal();
                return;
            }

            for (int j = 0; j < list.Count; j++)
            {
                string jLabel = j == 0 ? label : "";
                list[j] = EditorFields.IdDropdown(jLabel, list[j], allIds, $"{keyPrefix}_{j}", pickerMode: mode);
                GUILayout.BeginHorizontal();
                GUILayout.Space(100f);
                if (GUILayout.Button("X", EditorStyles.MiniButton, GUILayout.Width(22)))
                {
                    list.RemoveAt(j);
                    GUI.changed = true;
                    GUILayout.EndHorizontal();
                    break;
                }
                if (j == list.Count - 1 && GUILayout.Button("+", EditorStyles.MiniButton, GUILayout.Width(22)))
                {
                    list.Add("");
                    GUI.changed = true;
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
