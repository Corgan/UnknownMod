using System;
using Newtonsoft.Json;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  CARDBACK
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class CardbackDef : IModEntity
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";
        [JsonIgnore] public string EntityId { get => Id; set => Id = value; }
        public string CardbackName = "";
        public string CardbackTextId = "";

        // ── Config ───────────────────────────────────────────────
        public int CardbackOrder = 1000;
        public bool BaseCardback = false;
        public bool Locked = false;
        public bool ShowIfLocked = false;

        // ── Requirements ─────────────────────────────────────────
        public int RankLevel = 0;
        public string Sku = "";
        public string SteamStat = "";
        public int AdventureLevel = 0;
        public int ObeliskLevel = 0;
        public int SingularityLevel = 0;
        public bool PdxAccountRequired = false;

        // ── Subclass Reference ───────────────────────────────────
        public string CardbackSubclass = "";   // SubClassData ID

        // ── Sprite Source ────────────────────────────────────────
        public string SpriteSource = "";
    }
}
