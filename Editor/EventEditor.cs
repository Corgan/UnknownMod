using System.Linq;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;
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

        // Per-reply section toggles (indexed by reply index)
        private bool[] _secSs = new bool[16];
        private bool[] _secFl = new bool[16];
        private bool[] _secSsc = new bool[16];
        private bool[] _secFlc = new bool[16];

        public EventEditor(ModEditor parent) => _parent = parent;

        public void DrawPanel()
        {
            var zone = ZoneEditingService.CurrentZone;
            if (zone == null) { GUILayout.Label("No zone loaded."); return; }

            // ── Entity selector ──────────────────────────────────
            var eventIds = zone.Events.Keys.OrderBy(k => k).ToList();
            string sel = EditorFields.EntitySelector(
                _parent.SelectedEventId, eventIds,
                id => $"{id}  ({zone.Events[id].EventName})",
                "evt_sel");
            if (sel != _parent.SelectedEventId)
            {
                _parent.SelectedEventId = sel;
                _expandedReply = -1;
            }

            EditorStyles.Separator();

            if (_parent.SelectedEventId == null || !zone.Events.TryGetValue(_parent.SelectedEventId, out var ed))
            {
                GUILayout.Label("<i>Select an event above.</i>", EditorStyles.RichLabel);
                return;
            }

            // ── Basic fields ─────────────────────────────────────
            ed.EventName = EditorFields.TextField("Name", ed.EventName);
            ed.Description = EditorFields.TextArea("Description", ed.Description);
            ed.DescriptionAction = EditorFields.TextField("Action Text", ed.DescriptionAction);
            ed.EventTier = EditorFields.EnumField("Event Tier", ed.EventTier, "evt_tier");
            ed.ReplyRandom = EditorFields.IntField("Reply Random", ed.ReplyRandom);
            ed.RequirementId = EditorFields.IdDropdown("Requirement ID", ed.RequirementId, DataHelper.GetAllEventRequirementIds(), "evt_reqid");
            ed.SpriteSource = EditorFields.IdDropdown("Sprite Source", ed.SpriteSource, DataHelper.GetAllEventIds(), "evt_sprsrc");

            EditorStyles.Separator();
            GUILayout.Label($"<b>Replies ({ed.Replies.Count})</b>", EditorStyles.RichLabel);

            // ── Replies ──────────────────────────────────────────
            var combatIds = zone.Combats.Keys.OrderBy(k => k).ToList();
            var evtIds = zone.Events.Keys.OrderBy(k => k).ToList();
            var nodeIds = zone.Nodes.Keys.OrderBy(k => k).ToList();
            var lootIds = zone.Loot.Keys.OrderBy(k => k).ToList();

            // Grow toggle arrays if needed (preserve existing state)
            if (ed.Replies.Count > _secSs.Length)
            {
                int newLen = ed.Replies.Count + 4;
                var newSs = new bool[newLen]; System.Array.Copy(_secSs, newSs, _secSs.Length); _secSs = newSs;
                var newFl = new bool[newLen]; System.Array.Copy(_secFl, newFl, _secFl.Length); _secFl = newFl;
                var newSsc = new bool[newLen]; System.Array.Copy(_secSsc, newSsc, _secSsc.Length); _secSsc = newSsc;
                var newFlc = new bool[newLen]; System.Array.Copy(_secFlc, newFlc, _secFlc.Length); _secFlc = newFlc;
            }

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
                r.RequirementId = EditorFields.IdDropdown("Requirement", r.RequirementId, DataHelper.GetAllEventRequirementIds(), $"rep_req_{i}");
                r.RequirementBlockedId = EditorFields.IdDropdown("Blocked By", r.RequirementBlockedId, DataHelper.GetAllEventRequirementIds(), $"rep_blocked_{i}");

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
                    break;
                }

                GUILayout.EndVertical();
            }

            GUILayout.Space(4);
            if (GUILayout.Button("+ Add Reply", EditorStyles.MiniButton))
            {
                ed.Replies.Add(new ReplyDef { ReplyText = "New reply..." });
                _expandedReply = ed.Replies.Count - 1;
            }
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
            o.CombatId = EditorFields.IdDropdown("Combat", o.CombatId, combatIds, $"o_cmb_{k}");
            o.EventId = EditorFields.IdDropdown("Event", o.EventId, evtIds, $"o_evt_{k}");
            o.NodeTravelId = EditorFields.IdDropdown("Node Travel", o.NodeTravelId, nodeIds, $"o_node_{k}");

            // ── Requirements ─────────────────────────────────────
            o.RequirementUnlockId = EditorFields.IdDropdown("Req Unlock", o.RequirementUnlockId, DataHelper.GetAllEventRequirementIds(), $"o_requnlock_{k}");
            o.RequirementUnlock2Id = EditorFields.IdDropdown("Req Unlock 2", o.RequirementUnlock2Id, DataHelper.GetAllEventRequirementIds(), $"o_requnlock2_{k}");
            o.RequirementLockId = EditorFields.IdDropdown("Req Lock", o.RequirementLockId, DataHelper.GetAllEventRequirementIds(), $"o_reqlock_{k}");
            o.RequirementLock2Id = EditorFields.IdDropdown("Req Lock 2", o.RequirementLock2Id, DataHelper.GetAllEventRequirementIds(), $"o_reqlock2_{k}");

            // ── Loot / Shop ──────────────────────────────────────
            o.LootId = EditorFields.IdDropdown("Loot", o.LootId, lootIds, $"o_loot_{k}");
            o.ShopId = EditorFields.IdDropdown("Shop", o.ShopId, lootIds, $"o_shop_{k}");

            // ── Add items/cards ──────────────────────────────────
            o.AddItemId = EditorFields.IdDropdown("Add Item", o.AddItemId, DataHelper.GetAllItemIds(), $"o_item_{k}");
            o.AddCard1Id = EditorFields.IdDropdown("Add Card 1", o.AddCard1Id, DataHelper.GetAllCardIds(), $"o_card1_{k}");
            o.AddCard2Id = EditorFields.IdDropdown("Add Card 2", o.AddCard2Id, DataHelper.GetAllCardIds(), $"o_card2_{k}");
            o.AddCard3Id = EditorFields.IdDropdown("Add Card 3", o.AddCard3Id, DataHelper.GetAllCardIds(), $"o_card3_{k}");

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

            // Finish flags only on Ss/Ssc
            if (prefix == "Ss" || prefix == "Ssc")
            {
                o.FinishGame = EditorFields.Toggle("Finish Game", o.FinishGame);
                if (prefix == "Ss")
                    o.FinishObeliskMap = EditorFields.Toggle("Finish Obelisk", o.FinishObeliskMap);
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }
    }
}
