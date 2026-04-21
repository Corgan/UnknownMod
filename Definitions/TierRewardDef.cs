using System;
using Newtonsoft.Json;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  TIER REWARD
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class TierRewardDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }             // keyed by tier number as string
        public int Tier = 0;

        // ── Rewards ──────────────────────────────────────────────
        public int Common = 0;
        public int Uncommon = 0;
        public int Rare = 0;
        public int Epic = 0;
        public int Mythic = 0;
        public int Dust = 0;
    }
}
