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
    /// IMGUI panel for editing tier-reward definitions at the mod-project level.
    /// Tier rewards use numeric tiers rather than plain string IDs, so several
    /// base-class hooks are overridden to handle the mapping.
    /// </summary>
    public class TierRewardEditor : ModProjectEditorBase<TierRewardDef>
    {
        protected override string TypeLabel => "TierReward";
        protected override string FolderName => "tierrewards";
        protected override string NewIdSuffix => "_tier";

        protected override Dictionary<string, TierRewardDef> GetNewDict(ModProject proj) => proj.TierRewards;
        protected override Dictionary<string, TierRewardDef> GetPatchDict(ModProject proj) => proj.TierRewardPatches;

        protected override TierRewardDef CreateDefault(string id, ModProject proj)
        {
            // Find next available tier number starting at 100.
            int nextTier = 100;
            while (proj.TierRewards.Values.Any(t => t.Tier == nextTier)
                || proj.TierRewardPatches.Values.Any(t => t.Tier == nextTier))
                nextTier++;

            return new TierRewardDef { Id = id, Tier = nextTier };
        }

        protected override string GetDisplayName(TierRewardDef def)
            => $"(Tier {def.Tier})";

        protected override List<string> GetAllBaseIds()
            => DataHelper.GetAllTierRewardTiers().Select(t => $"tier_{t}").ToList();

        protected override TierRewardDef SnapshotBaseEntity(string id)
        {
            if (int.TryParse(id.Replace("tier_", ""), out int tier))
            {
                var existing = DataHelper.GetTierRewardData(tier);
                return existing != null ? DataHelper.SnapshotTierReward(existing) : null;
            }
            return null;
        }

        public TierRewardEditor(ModEditor parent) : base(parent) { }

        // ── Collapsible section state ─────────────────────────────
        private bool _secPreview = true;
        private bool _secIdentity = true;
        private bool _secRewards = true;

        // ═══════════════════════════════════════════════════════════
        //  ALL SECTIONS
        // ═══════════════════════════════════════════════════════════

        protected override void DrawAllSections(TierRewardDef d, ModProject proj)
        {
            // ── Preview ──────────────────────────────────────────
            if (EditorFields.Section("TierReward Preview", ref _secPreview))
            {
                string desc = BuildTierRewardDescription(d);
                GUILayout.BeginVertical(EditorStyles.CompactBox);
                GUILayout.Label($"<size=11>{desc}</size>", EditorStyles.RichLabel);
                GUILayout.EndVertical();
            }

            // ── Identity ─────────────────────────────────────────
            if (EditorFields.Section("Identity", ref _secIdentity))
            {
                d.Id = EditorFields.TextField("ID", d.Id);
                d.Tier = EditorFields.IntField("Tier", d.Tier);
            }

            // ── Rewards ──────────────────────────────────────────
            if (EditorFields.Section("Rewards", ref _secRewards))
            {
                d.Common   = EditorFields.IntField("Common",   d.Common);
                d.Uncommon = EditorFields.IntField("Uncommon", d.Uncommon);
                d.Rare     = EditorFields.IntField("Rare",     d.Rare);
                d.Epic     = EditorFields.IntField("Epic",     d.Epic);
                d.Mythic   = EditorFields.IntField("Mythic",   d.Mythic);
                d.Dust     = EditorFields.IntField("Dust",     d.Dust);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  LIVE DESCRIPTION BUILDER
        // ═══════════════════════════════════════════════════════════

        public static string BuildTierRewardDescription(TierRewardDef d)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>Tier {d.Tier}</b>");

            var parts = new List<string>();
            if (d.Common > 0) parts.Add($"Common:{d.Common}");
            if (d.Uncommon > 0) parts.Add($"Uncommon:{d.Uncommon}");
            if (d.Rare > 0) parts.Add($"Rare:{d.Rare}");
            if (d.Epic > 0) parts.Add($"Epic:{d.Epic}");
            if (d.Mythic > 0) parts.Add($"Mythic:{d.Mythic}");
            if (d.Dust > 0) parts.Add($"Dust:{d.Dust}");

            if (parts.Count > 0)
                sb.Append($"\n<color=#88ccff>{string.Join("  ", parts)}</color>");

            return sb.ToString();
        }
    }
}
