using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  HERO (SubClass)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class HeroCardDef
    {
        public string CardId = "";
        public int UnitsInDeck = 1;
    }

    [Serializable]
    public class HeroDef
    {
        // ── Identity ─────────────────────────────────────────────
        public string Id = "";                 // subClassName → Id via Init()
        public string SubClassName = "";       // display name (e.g. "Liam I")
        public string CharacterName = "";      // base hero name (e.g. "Liam")
        public string CharacterDescription = "";
        public string CharacterDescriptionStrength = "";
        public bool MainCharacter = false;
        public bool InitialUnlock = false;
        public string Sku = "";                // DLC requirement

        // ── Visual / Sprite ──────────────────────────────────────
        public string SpriteSource = "";       // base-game subclass ID to copy visuals from
        public float FluffOffsetX = 0f;
        public float FluffOffsetY = 0f;
        public bool Female = false;
        public float StickerOffsetX = 0f;

        // ── Class ────────────────────────────────────────────────
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClass = Enums.HeroClass.Warrior;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClassSecondary = Enums.HeroClass.None;

        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.HeroClass HeroClassThird = Enums.HeroClass.None;

        // ── Stats ────────────────────────────────────────────────
        public int OrderInList = 0;
        public bool Blocked = true;
        public int Speed = 0;
        public int Hp = 0;
        public int Energy = 0;
        public int EnergyTurn = 0;

        // ── Resistances ──────────────────────────────────────────
        public int ResSlash = 0;
        public int ResBlunt = 0;
        public int ResPierce = 0;
        public int ResFire = 0;
        public int ResCold = 0;
        public int ResLight = 0;
        public int ResMind = 0;
        public int ResHoly = 0;
        public int ResShadow = 0;

        // ── Item ─────────────────────────────────────────────────
        public string ItemId = "";             // starting item (CardData ref)

        // ── Level HP ─────────────────────────────────────────────
        public List<int> MaxHp = new();

        // ── Starting Cards ───────────────────────────────────────
        public List<HeroCardDef> Cards = new();

        // ── Singularity Cards ────────────────────────────────────
        public List<string> CardsSingularity = new();  // CardData IDs

        // ── Trait Tree ───────────────────────────────────────────
        public string Trait0 = "";
        public string Trait1A = "";
        public string Trait1ACard = "";        // CardData ID
        public string Trait1B = "";
        public string Trait1BCard = "";
        public string Trait2A = "";
        public string Trait2B = "";
        public string Trait3A = "";
        public string Trait3ACard = "";
        public string Trait3B = "";
        public string Trait3BCard = "";
        public string Trait4A = "";
        public string Trait4B = "";

        // ── Challenge Packs ──────────────────────────────────────
        public string ChallengePack0 = "";
        public string ChallengePack1 = "";
        public string ChallengePack2 = "";
        public string ChallengePack3 = "";
        public string ChallengePack4 = "";
        public string ChallengePack5 = "";
        public string ChallengePack6 = "";
    }
}
