using System.Collections.Generic;
using UnityEngine;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod.Editor
{
    // ═══════════════════════════════════════════════════════════════
    //  Thermometers and town minigames. These are additive only: the base game has no
    //  override model for them, so the patch dictionary is an always-empty stand-in.
    // ═══════════════════════════════════════════════════════════════

    public class ThermometerEditor : ModProjectEditorBase<ThermometerDef>
    {
        private static readonly Dictionary<string, ThermometerDef> NoPatches = new();

        protected override string TypeLabel => "Thermometer";
        protected override string FolderName => "thermometers";
        protected override string NewIdSuffix => "_heat";

        protected override Dictionary<string, ThermometerDef> GetNewDict(ModProject p) => p.Thermometers;
        protected override Dictionary<string, ThermometerDef> GetPatchDict(ModProject p) => NoPatches;
        protected override ThermometerDef CreateDefault(string id, ModProject proj) => new ThermometerDef { Id = id };
        protected override string GetDisplayName(ThermometerDef d) => $"(+{d.GoldBonus:0.##} gold, +{d.ExpBonus:0.##} xp)";
        protected override ThermometerDef SnapshotBaseEntity(string id) => null;

        public ThermometerEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(ThermometerDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);
            GUILayout.Label("<size=10><color=#888>One heat step. Group steps into rounds with a Thermometer Tier, then assign the tier to a combat.</color></size>", EditorStyles.RichLabel);
            d.ColorHex = EditorFields.TextField("Colour (#RRGGBB)", d.ColorHex);
            if (ColorUtility.TryParseHtmlString(d.ColorHex ?? "", out var c))
            {
                var r = GUILayoutUtility.GetRect(10, 14, GUILayout.ExpandWidth(true));
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, c, 0, 0);
            }
            else GUILayout.Label("<color=#ff6666>Not a valid HTML colour.</color>", EditorStyles.RichLabel);

            GUILayout.Label("<b>Bonuses</b>", EditorStyles.RichLabel);
            d.GoldBonus = EditorFields.FloatField("Gold Bonus", d.GoldBonus);
            d.ExpBonus = EditorFields.FloatField("XP Bonus", d.ExpBonus);
            d.CardBonus = EditorFields.IntField("Card Bonus", d.CardBonus);
            d.CardReward = EditorFields.Toggle("Card Reward", d.CardReward);
            d.ExpertiseBonus = EditorFields.IntField("Expertise Bonus", d.ExpertiseBonus);

            GUILayout.Label("<b>Shown in the UI</b>", EditorStyles.RichLabel);
            d.UiGold = EditorFields.IntField("Gold", d.UiGold);
            d.UiExp = EditorFields.IntField("XP", d.UiExp);
            d.UiCard = EditorFields.IntField("Cards", d.UiCard);
        }
    }

    public class ThermometerTierEditor : ModProjectEditorBase<ThermometerTierDef>
    {
        private static readonly Dictionary<string, ThermometerTierDef> NoPatches = new();

        protected override string TypeLabel => "ThermometerTier";
        protected override string FolderName => "thermometertiers";
        protected override string NewIdSuffix => "_tier";

        protected override Dictionary<string, ThermometerTierDef> GetNewDict(ModProject p) => p.ThermometerTiers;
        protected override Dictionary<string, ThermometerTierDef> GetPatchDict(ModProject p) => NoPatches;
        protected override ThermometerTierDef CreateDefault(string id, ModProject proj) => new ThermometerTierDef { Id = id };
        protected override string GetDisplayName(ThermometerTierDef d) => $"({d.Rounds?.Count ?? 0} rounds)";
        protected override ThermometerTierDef SnapshotBaseEntity(string id) => null;

        public ThermometerTierEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(ThermometerTierDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);
            GUILayout.Label("<size=10><color=#888>Which heat step applies from each combat round onward.</color></size>", EditorStyles.RichLabel);
            d.Rounds ??= new List<RoundThermometerDef>();
            var steps = RefIds.Thermometers(proj);
            for (int i = 0; i < d.Rounds.Count; i++)
            {
                var r = d.Rounds[i];
                if (EditorFields.ApplyRowOp(d.Rounds, i, EditorFields.RowHeader($"<color=#ffaa44>Step {i + 1}</color>", i, d.Rounds.Count))) break;
                r.Round = EditorFields.IntFieldMin("From Round", r.Round, 0);
                r.ThermometerId = EditorFields.IdDropdown("Thermometer", r.ThermometerId, steps, "tt_step" + i);
            }
            if (GUILayout.Button("+ Round Step", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                d.Rounds.Add(new RoundThermometerDef { Round = d.Rounds.Count > 0 ? d.Rounds[d.Rounds.Count - 1].Round + 1 : 0 });
                GUI.changed = true;
            }
        }
    }

    public class BettingTeamEditor : ModProjectEditorBase<BettingTeamDef>
    {
        private static readonly Dictionary<string, BettingTeamDef> NoPatches = new();

        protected override string TypeLabel => "BettingTeam";
        protected override string FolderName => "bettingteams";
        protected override string NewIdSuffix => "_team";

        protected override Dictionary<string, BettingTeamDef> GetNewDict(ModProject p) => p.BettingTeams;
        protected override Dictionary<string, BettingTeamDef> GetPatchDict(ModProject p) => NoPatches;
        protected override BettingTeamDef CreateDefault(string id, ModProject proj) => new BettingTeamDef { TeamName = id };
        protected override string GetDisplayName(BettingTeamDef d) => $"({d.NpcIds?.Count ?? 0} NPCs, {d.Pool})";
        protected override BettingTeamDef SnapshotBaseEntity(string id) => null;

        public BettingTeamEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(BettingTeamDef d, ModProject proj)
        {
            d.TeamName = EditorFields.TextField("Team Name", d.TeamName);
            d.Pool = EditorFields.EnumField("Pool", d.Pool, "bt_pool");
            GUILayout.Label("<size=10><color=#888>Teams are added to the town betting game's pool(s). A team with no resolvable NPCs is skipped.</color></size>", EditorStyles.RichLabel);
            d.NpcIds ??= new List<string>();
            EditorFields.IdListField("NPC", d.NpcIds, RefIds.Npcs(proj), "bt_npc", EntityPicker.Mode.NPC);
        }
    }

    public class MemorySequenceEditor : ModProjectEditorBase<MemorySequenceDef>
    {
        private static readonly Dictionary<string, MemorySequenceDef> NoPatches = new();

        protected override string TypeLabel => "MemorySequence";
        protected override string FolderName => "memorysequences";
        protected override string NewIdSuffix => "_memory";

        protected override Dictionary<string, MemorySequenceDef> GetNewDict(ModProject p) => p.MemorySequences;
        protected override Dictionary<string, MemorySequenceDef> GetPatchDict(ModProject p) => NoPatches;
        protected override MemorySequenceDef CreateDefault(string id, ModProject proj) => new MemorySequenceDef { ZoneId = id };
        protected override string GetDisplayName(MemorySequenceDef d) => $"({d.CardGroups?.Count ?? 0} groups)";
        protected override MemorySequenceDef SnapshotBaseEntity(string id) => null;

        public MemorySequenceEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(MemorySequenceDef d, ModProject proj)
        {
            var zones = EditorFields.CachedIds("memseq_zones", () => DataHelper.GetAllZoneIds());
            d.ZoneId = EditorFields.IdDropdown("Town Zone", d.ZoneId, zones, "ms_zone");
            GUILayout.Label("<size=10><color=#888>A zone with no sequence pack in the base game gets a new one. Existing groups of the same card type are left alone.</color></size>", EditorStyles.RichLabel);

            d.CardGroups ??= new List<MemorySequenceGroupDef>();
            for (int i = 0; i < d.CardGroups.Count; i++)
            {
                var g = d.CardGroups[i];
                if (EditorFields.ApplyRowOp(d.CardGroups, i, EditorFields.RowHeader($"<color=#44ccff>Group {i + 1}</color>", i, d.CardGroups.Count))) break;
                g.CardType = EditorFields.EnumField("Card Type", g.CardType, "ms_ct" + i);
                g.Count = EditorFields.IntField("Count", g.Count, 1, 12);
                g.PoolCardIds ??= new List<string>();
                GUILayout.Label("<size=10><color=#888>Empty pool = every base card of this type.</color></size>", EditorStyles.RichLabel);
                EditorFields.IdListField("Pool Card", g.PoolCardIds, RefIds.Cards(proj), "ms_pool" + i, EntityPicker.Mode.Card);
            }
            if (GUILayout.Button("+ Card Group", EditorStyles.MiniButton, GUILayout.Width(100)))
            {
                d.CardGroups.Add(new MemorySequenceGroupDef());
                GUI.changed = true;
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Distractor cards</b>", EditorStyles.RichLabel);
            d.ExtraPoolCardIds ??= new List<string>();
            EditorFields.IdListField("Card", d.ExtraPoolCardIds, RefIds.Cards(proj), "ms_extra", EntityPicker.Mode.Card);
        }
    }

    public class CardPairRewardEditor : ModProjectEditorBase<CardPairRewardDef>
    {
        private static readonly Dictionary<string, CardPairRewardDef> NoPatches = new();

        protected override string TypeLabel => "CardPairReward";
        protected override string FolderName => "cardpairrewards";
        protected override string NewIdSuffix => "_pairreward";

        protected override Dictionary<string, CardPairRewardDef> GetNewDict(ModProject p) => p.CardPairRewards;
        protected override Dictionary<string, CardPairRewardDef> GetPatchDict(ModProject p) => NoPatches;
        protected override CardPairRewardDef CreateDefault(string id, ModProject proj) => new CardPairRewardDef { Id = id };
        protected override string GetDisplayName(CardPairRewardDef d) => $"({d.CardIds?.Count ?? 0} cards)";
        protected override CardPairRewardDef SnapshotBaseEntity(string id) => null;

        public CardPairRewardEditor(ModEditor parent) : base(parent) { }

        protected override void DrawAllSections(CardPairRewardDef d, ModProject proj)
        {
            d.Id = EditorFields.TextField("ID", d.Id);
            d.TargetPackId = EditorFields.TextField("Target Pack Id", d.TargetPackId);
            GUILayout.Label("<size=10><color=#888>Leave the pack id empty to extend every card-pair pack.</color></size>", EditorStyles.RichLabel);
            d.CardIds ??= new List<string>();
            EditorFields.IdListField("Reward Card", d.CardIds, RefIds.Cards(proj), "cpr_card", EntityPicker.Mode.Card);
        }
    }
}
