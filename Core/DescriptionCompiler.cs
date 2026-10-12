using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnknownMod.Definitions;

namespace UnknownMod.Core
{
    /// <summary>
    /// Turns an author-friendly description template into the markup the game's card text expects.
    /// Tokens in braces read straight from the card definition ({Damage}, {Aura}, {DrawCard}...) so the text
    /// cannot drift out of sync with the numbers, and the game's own tags (&lt;c&gt;, &lt;gr&gt;, &lt;i=bleed&gt;, &lt;br1&gt;) pass through.
    /// Values are the card's base values; the game's own generated text additionally folds in hero scaling.
    /// </summary>
    public static class DescriptionCompiler
    {
        public struct Preset
        {
            public string Name;
            public string Template;
            public Preset(string name, string template) { Name = name; Template = template; }
        }

        public static readonly Preset[] Presets =
        {
            new("Deal damage", "Deal {Damage} {DamageType} damage."),
            new("Deal damage, apply curse", "Deal {Damage} {DamageType} damage.\nApply {CurseCharges} {Curse}."),
            new("Apply curse", "Apply {CurseCharges} {Curse}."),
            new("Apply aura to target", "Apply {AuraCharges} {Aura}."),
            new("Gain aura (self)", "Gain {AuraSelfCharges} {AuraSelf}."),
            new("Heal", "Heal {Heal}."),
            new("Draw cards", "Draw {DrawCard} cards."),
            new("Gain energy", "Gain {AcEnergyBonusQuantity} energy."),
            new("Add card to hand", "Add {card:AddCardId,AddCard} to your hand."),
            new("Summon", "Summon {SummonNum} {SummonUnitId}."),
            new("Repeat effect", "Repeat the effect {EffectRepeat} times."),
            new("Vanish", "<c>Vanish.</c>"),
            new("Conditional bonus", "If the target has {icon:bleed} <c>Bleed</c>, deal {Damage} extra damage."),
            new("Grey note", "<gr>A quiet note.</gr>"),
            new("Highlight gold", "<c>Highlighted text</c>"),
            new("Highlight blue", "<bl>Highlighted text</bl>"),
            new("Highlight purple", "<rd>Highlighted text</rd>"),
        };

        /// <summary>Tokens offered in the editor's palette.</summary>
        public static readonly string[] CommonTokens =
        {
            "{Damage}", "{Damage2}", "{DamageType}", "{DamageType2:name}", "{Heal}", "{HealSelf}", "{DamageSelf}", "{EnergyCost}",
            "{Aura}", "{AuraCharges}", "{Aura:name}", "{Aura:icon}", "{AuraSelf}", "{AuraSelfCharges}",
            "{Curse}", "{CurseCharges}", "{CurseSelf}", "{CurseSelfCharges}",
            "{DrawCard}", "{DiscardCard}", "{AddCard}", "{card:AddCardId,AddCard}", "{SummonNum}", "{SummonUnitId}",
            "{PushTarget}", "{PullTarget}", "{LookCards}", "{GoldGainQuantity}", "{ShardsGainQuantity}", "{EffectRepeat}",
            "{icon:energy}", "{n}", "{n1}", "{n2}", "{n3}"
        };

        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dmg"] = "Damage", ["dmg2"] = "Damage2", ["dmgtype"] = "DamageType", ["dmgtype2"] = "DamageType2",
            ["selfdmg"] = "DamageSelf", ["selfdmg2"] = "DamageSelf2", ["energy"] = "EnergyCost", ["draw"] = "DrawCard",
            ["discard"] = "DiscardCard", ["gold"] = "GoldGainQuantity", ["shards"] = "ShardsGainQuantity",
            ["repeat"] = "EffectRepeat", ["push"] = "PushTarget", ["pull"] = "PullTarget", ["look"] = "LookCards",
            ["add"] = "AddCard", ["summon"] = "SummonNum", ["curse1"] = "Curse", ["curse1charges"] = "CurseCharges",
            ["aura1"] = "Aura", ["aura1charges"] = "AuraCharges",
        };

        private static readonly Regex Token = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
        private static readonly Regex AuraField = new(@"^(Aura|Curse|AuraSelf|CurseSelf|SummonAura|AcEnergyBonus)\d?$|^HealAuraCurse(Self|Name\d?)$", RegexOptions.Compiled);

        private static Dictionary<string, FieldInfo> _fields;

        private static Dictionary<string, FieldInfo> Fields => _fields ??= typeof(CardDef)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(float) || f.FieldType == typeof(string)
                        || f.FieldType == typeof(bool) || f.FieldType.IsEnum)
            .ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

        /// <summary>Expand every token in the template against the card. Tokens that cannot be resolved are left as typed and listed in <paramref name="unknown"/>.</summary>
        public static string Compile(CardDef card, string template, List<string> unknown = null)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return Token.Replace(template, m =>
            {
                string resolved = ResolveToken(card, m.Groups[1].Value.Trim());
                if (resolved != null) return resolved;
                unknown?.Add(m.Value);
                return m.Value;
            });
        }

        public static List<string> UnknownTokens(CardDef card, string template)
        {
            var unknown = new List<string>();
            Compile(card, template, unknown);
            return unknown.Distinct().ToList();
        }

        private static string ResolveToken(CardDef card, string token)
        {
            string head = token, arg = null;
            int colon = token.IndexOf(':');
            if (colon >= 0) { head = token.Substring(0, colon).Trim(); arg = token.Substring(colon + 1).Trim(); }

            switch (head.ToLowerInvariant())
            {
                case "n": return "<br>";
                case "n1": return "<br1>";
                case "n2": return "<br2>";
                case "n3": return "<br3>";
                case "icon": return string.IsNullOrEmpty(arg) ? null : "<i=" + arg.ToLowerInvariant() + ">";
                case "card": return CardTag(card, arg);
            }

            if (Aliases.TryGetValue(head, out var aliased)) head = aliased;
            if (!Fields.TryGetValue(head, out var field)) return null;
            object value = field.GetValue(card);

            if (field.FieldType == typeof(string))
                return AuraField.IsMatch(field.Name) ? AuraText((string)value, arg) : (string)value ?? "";
            if (field.FieldType.IsEnum)
            {
                bool damageType = field.Name.IndexOf("DamageType", StringComparison.Ordinal) >= 0;
                string name = value.ToString();
                if (!damageType || string.Equals(arg, "name", StringComparison.OrdinalIgnoreCase)) return name;
                return name == "None" ? "" : "<i=" + name.ToLowerInvariant() + ">";
            }
            if (field.FieldType == typeof(float)) return ((float)value).ToString("0.##", CultureInfo.InvariantCulture);
            if (field.FieldType == typeof(bool)) return (bool)value ? "yes" : "no";
            return value.ToString();
        }

        private static string AuraText(string id, string mode)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string icon = "<i=" + id.ToLowerInvariant() + ">";
            string name = id;
            try { name = DataHelper.GetAuraCurse(id, false)?.ACName ?? id; } catch { }
            switch ((mode ?? "").ToLowerInvariant())
            {
                case "id": return id;
                case "icon": return icon;
                case "name": return name;
                default: return icon + name;
            }
        }

        /// <summary>{card:AddCardId,AddCard}: each part is a CardDef field name or a literal.</summary>
        private static string CardTag(CardDef card, string arg)
        {
            if (string.IsNullOrEmpty(arg)) return null;
            var parts = arg.Split(',');
            string id = Literal(card, parts[0].Trim());
            string count = parts.Length > 1 ? Literal(card, parts[1].Trim()) : "1";
            if (string.IsNullOrEmpty(id)) return "";
            if (!int.TryParse(count, out int n) || n < 1) n = 1;
            return "<crd=" + id + "," + n + ">";
        }

        private static string Literal(CardDef card, string s)
        {
            if (Fields.TryGetValue(s, out var f)) return f.GetValue(card)?.ToString() ?? "";
            return s;
        }

        /// <summary>Starting-point template written from the card's current effect fields.</summary>
        public static string Generate(CardDef c)
        {
            var lines = new List<string>();
            void Add(string s) { lines.Add(s); }

            if (c.Damage > 0) Add("Deal {Damage} {DamageType} damage" + (c.DamageSides > 0 ? " to the target and {DamageSides} to the sides." : "."));
            if (c.Damage2 > 0) Add("Deal {Damage2} {DamageType2} damage.");
            if (c.DamageSelf > 0) Add("Take {DamageSelf} damage.");
            if (c.Heal > 0) Add("Heal {Heal}.");
            if (c.HealSelf > 0) Add("Heal yourself for {HealSelf}.");
            AddAuras(c, lines, "Aura", "Apply", c.Aura, c.Aura2, c.Aura3);
            AddAuras(c, lines, "Curse", "Apply", c.Curse, c.Curse2, c.Curse3);
            AddAuras(c, lines, "AuraSelf", "Gain", c.AuraSelf, c.AuraSelf2, c.AuraSelf3);
            AddAuras(c, lines, "CurseSelf", "Suffer", c.CurseSelf, c.CurseSelf2, c.CurseSelf3);
            if (c.DrawCard > 0) Add("Draw {DrawCard} card" + (c.DrawCard == 1 ? "" : "s") + ".");
            if (c.DiscardCard > 0) Add("Discard {DiscardCard} card" + (c.DiscardCard == 1 ? "" : "s") + ".");
            if (c.AddCard > 0 && !string.IsNullOrEmpty(c.AddCardId)) Add("Add {card:AddCardId,AddCard} to your hand.");
            if (c.SummonNum > 0 && !string.IsNullOrEmpty(c.SummonUnitId)) Add("Summon {SummonNum} {SummonUnitId}.");
            if (c.PushTarget > 0) Add("Push the target {PushTarget}.");
            if (c.PullTarget > 0) Add("Pull the target {PullTarget}.");
            if (c.LookCards > 0) Add("Look at the top {LookCards} cards of your deck.");
            if (c.GoldGainQuantity > 0) Add("Gain {GoldGainQuantity} gold.");
            if (c.ShardsGainQuantity > 0) Add("Gain {ShardsGainQuantity} shards.");
            if (c.EffectRepeat > 1) Add("Repeat {EffectRepeat} times.");
            if (c.Vanish) Add("<c>Vanish.</c>");
            if (c.Innate) Add("<c>Innate.</c>");
            if (c.Lazy) Add("<c>Lazy.</c>");
            return string.Join("\n", lines);
        }

        private static void AddAuras(CardDef c, List<string> lines, string baseName, string verb, params string[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (string.IsNullOrEmpty(ids[i])) continue;
                string suffix = i == 0 ? "" : (i + 1).ToString();
                string chargesField = baseName == "Curse" ? "Curse" + (i == 0 ? "" : i == 1 ? "2" : "3") + "Charges"
                    : baseName == "CurseSelf" ? "CurseSelf" + suffix + "Charges"
                    : baseName == "Aura" ? "Aura" + suffix + "Charges"
                    : "AuraSelf" + suffix + "Charges";
                lines.Add(verb + " {" + chargesField + "} {" + baseName + suffix + "}.");
            }
        }

        // ── Editor preview ───────────────────────────────────────

        /// <summary>Flatten game description markup into tags IMGUI rich text can draw on a dark background.</summary>
        public static string ToPreview(string gameMarkup)
        {
            if (string.IsNullOrEmpty(gameMarkup)) return "";
            var sb = new StringBuilder(gameMarkup);
            sb.Replace("<gr>", "<color=#999999>").Replace("</gr>", "</color>")
              .Replace("<gl>", "<color=#e0a860>").Replace("</gl>", "</color>")
              .Replace("<bl>", "<color=#7c8cff>").Replace("</bl>", "</color>")
              .Replace("<rd>", "<color=#d070d0>").Replace("</rd>", "</color>")
              .Replace("<c>", "<color=#e0a860>").Replace("</c>", "</color>")
              .Replace("<act>", "<color=#999999>").Replace("</act>", "</color>")
              .Replace("<nb>", "").Replace("</nb>", "")
              .Replace("<br1>", "\n").Replace("<br2>", "\n\n").Replace("<br3>", "\n\n").Replace("<br>", "\n")
              .Replace("#5E3016", "#e0a860").Replace("#5e3016", "#e0a860").Replace("#444>", "#999999>");
            string s = sb.ToString();
            s = Regex.Replace(s, @"<i=(\w+)>", "[$1]");
            s = Regex.Replace(s, @"<crd=([^,>]+),(\d+)>", m => (m.Groups[2].Value == "1" ? "" : m.Groups[2].Value + "x ") + "[" + m.Groups[1].Value + "]");
            s = Regex.Replace(s, @"<sprite name=([^>]+)>", "[$1]");
            s = Regex.Replace(s, @"</?(size|voffset|space|line-height|nobr|align|indent|margin|mark|font|cspace|mspace|pos|rotate|style|link)[^>]*>", "");
            return s;
        }
    }
}
