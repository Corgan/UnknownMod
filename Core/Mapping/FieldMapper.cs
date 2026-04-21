using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace UnknownMod.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  FIELD MAPPING — single source of truth for Make ↔ Snapshot
    // ═══════════════════════════════════════════════════════════════

    /// <summary>How to resolve a string ID ↔ ScriptableObject reference.</summary>
    public enum RefType
    {
        /// <summary>Direct value copy — int, float, bool, string, enum. No transformation.</summary>
        None,
        /// <summary>string ↔ AuraCurseData via GetAuraCurse / GetACId.</summary>
        AuraCurse,
        /// <summary>string ↔ CardData via GetCard / card.Id.</summary>
        Card,
        /// <summary>string ↔ NPCData via GetExistingNPC / npc.Id.</summary>
        NPC,
    }

    /// <summary>
    /// Declares a single field mapping between a Def DTO property and a ScriptableObject member.
    /// Used by <see cref="FieldMapper"/> to drive both Make (Def→SO) and Snapshot (SO→Def).
    /// </summary>
    public readonly struct FieldMapping
    {
        /// <summary>Member name on the ScriptableObject (field or property, any visibility).</summary>
        public readonly string SoField;

        /// <summary>Property name on the Def DTO.</summary>
        public readonly string DefProp;

        /// <summary>Reference resolver type. None = direct value copy.</summary>
        public readonly RefType Ref;

        public FieldMapping(string soField, string defProp, RefType refType = RefType.None)
        {
            SoField = soField;
            DefProp = defProp;
            Ref = refType;
        }
    }

    /// <summary>
    /// Drives bidirectional field mapping between Def DTOs and game ScriptableObjects.
    /// Uses AccessTools for SO member resolution (handles public/private fields and properties).
    /// All fields in a FieldMapping[] array are handled in both Make and Snapshot directions.
    /// Edge cases (lists, conditionals, Vector2 splits, asset names) are handled explicitly
    /// in per-type code blocks alongside the mapper calls.
    /// </summary>
    public static class FieldMapper
    {
        // ── Cached member accessors ──────────────────────────────

        private struct MemberAccessor
        {
            public FieldInfo Field;
            public PropertyInfo Property;

            public bool IsValid => Field != null || Property != null;

            public Type MemberType =>
                Field != null ? Field.FieldType :
                Property != null ? Property.PropertyType : null;

            public object Get(object obj)
            {
                if (Field != null) return Field.GetValue(obj);
                if (Property != null) return Property.GetValue(obj);
                return null;
            }

            public void Set(object obj, object value)
            {
                if (Field != null) Field.SetValue(obj, value);
                else if (Property != null && Property.CanWrite) Property.SetValue(obj, value);
            }
        }

        private static readonly Dictionary<(Type, string), MemberAccessor> _soCache
            = new Dictionary<(Type, string), MemberAccessor>();
        private static readonly Dictionary<(Type, string), MemberAccessor> _defCache
            = new Dictionary<(Type, string), MemberAccessor>();

        private static MemberAccessor GetSoAccessor(Type soType, string name)
        {
            var key = (soType, name);
            if (!_soCache.TryGetValue(key, out var acc))
            {
                acc.Field = AccessTools.Field(soType, name);
                if (acc.Field == null)
                    acc.Property = AccessTools.Property(soType, name);
                if (!acc.IsValid)
                    Plugin.Log.LogWarning($"[FieldMapper] SO member '{name}' not found on {soType.Name}");
                _soCache[key] = acc;
            }
            return acc;
        }

        private static MemberAccessor GetDefAccessor(Type defType, string name)
        {
            var key = (defType, name);
            if (!_defCache.TryGetValue(key, out var acc))
            {
                acc.Property = defType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (acc.Property == null)
                    acc.Field = defType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (!acc.IsValid)
                    Plugin.Log.LogWarning($"[FieldMapper] Def member '{name}' not found on {defType.Name}");
                _defCache[key] = acc;
            }
            return acc;
        }

        // ═══════════════════════════════════════════════════════════
        //  MAKE DIRECTION: Def → SO
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Apply all mapped fields from a Def DTO to a ScriptableObject.
        /// For AC/Card/NPC refs, resolves the string ID to the actual SO reference.
        /// Skips ref fields whose ID is null/empty (leaves SO default).
        /// </summary>
        public static void Apply<TDef>(FieldMapping[] mappings, TDef def, object so)
        {
            var defType = typeof(TDef);
            var soType = so.GetType();

            for (int i = 0; i < mappings.Length; i++)
            {
                var m = mappings[i];
                var defAcc = GetDefAccessor(defType, m.DefProp);
                if (!defAcc.IsValid) continue;
                var soAcc = GetSoAccessor(soType, m.SoField);
                if (!soAcc.IsValid) continue;

                object val = defAcc.Get(def);

                switch (m.Ref)
                {
                    case RefType.None:
                        // Null-coalesce strings to "" to match existing behavior
                        if (val == null && defAcc.MemberType == typeof(string))
                            val = "";
                        soAcc.Set(so, val);
                        break;

                    case RefType.AuraCurse:
                        var acId = val as string;
                        if (!string.IsNullOrEmpty(acId))
                            soAcc.Set(so, DataHelper.GetAuraCurse(acId));
                        break;

                    case RefType.Card:
                        var cardId = val as string;
                        if (!string.IsNullOrEmpty(cardId))
                        {
                            var card = DataHelper.GetCard(cardId);
                            if (card != null) soAcc.Set(so, card);
                        }
                        break;

                    case RefType.NPC:
                        var npcId = val as string;
                        if (!string.IsNullOrEmpty(npcId))
                        {
                            var npc = DataHelper.GetExistingNPC(npcId);
                            if (npc != null) soAcc.Set(so, npc);
                        }
                        break;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  SNAPSHOT DIRECTION: SO → Def
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Read all mapped fields from a ScriptableObject into a Def DTO.
        /// For AC/Card/NPC refs, extracts the string ID from the SO reference.
        /// String values are null-coalesced to "".
        /// </summary>
        public static void Snapshot<TDef>(FieldMapping[] mappings, object so, TDef def)
        {
            var defType = typeof(TDef);
            var soType = so.GetType();

            for (int i = 0; i < mappings.Length; i++)
            {
                var m = mappings[i];
                var defAcc = GetDefAccessor(defType, m.DefProp);
                if (!defAcc.IsValid) continue;
                var soAcc = GetSoAccessor(soType, m.SoField);
                if (!soAcc.IsValid) continue;

                switch (m.Ref)
                {
                    case RefType.None:
                        var raw = soAcc.Get(so);
                        if (defAcc.MemberType == typeof(string))
                            raw = (raw as string) ?? "";
                        defAcc.Set(def, raw);
                        break;

                    case RefType.AuraCurse:
                        var ac = soAcc.Get(so) as AuraCurseData;
                        defAcc.Set(def, DataHelper.GetACId(ac));
                        break;

                    case RefType.Card:
                        var card = soAcc.Get(so) as CardData;
                        defAcc.Set(def, card != null ? (card.Id ?? "") : "");
                        break;

                    case RefType.NPC:
                        var npc = soAcc.Get(so) as NPCData;
                        defAcc.Set(def, npc != null ? (npc.Id ?? "") : "");
                        break;
                }
            }
        }
    }
}
