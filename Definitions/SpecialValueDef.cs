using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnknownMod.Definitions
{
    // ───────────────────────────────────────────────────────────────
    //  SPECIAL VALUE (mirrors game SpecialValues struct)
    // ───────────────────────────────────────────────────────────────

    [Serializable]
    public class SpecialValueDef
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Enums.SpecialValueModifierName Name = Enums.SpecialValueModifierName.RuneCharges;
        public bool Use = false;
        public float Multiplier = 0f;
    }
}
