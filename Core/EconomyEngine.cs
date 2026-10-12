using System;
using System.Collections.Generic;
using System.Linq;
using UnknownMod.Definitions;
using UnknownMod.Editor;

namespace UnknownMod.Core
{
    /// <summary>
    /// Applies economy sets (price rules) to the game's prices. The game computes every price on the client that
    /// shows it and sends the amount along with the purchase, so applying the same mod on every client keeps them in step.
    /// </summary>
    public static class EconomyEngine
    {
        private const int LogCap = 40;

        // The projects' own dictionaries, so editor edits take effect on the next price shown.
        private static readonly Dictionary<string, Dictionary<string, EconomyDef>> Mods = new();

        /// <summary>The most recent adjustments, newest last. Shown in the Economy panel.</summary>
        public static readonly List<string> Recent = new();

        public static void Register(ModProject proj)
        {
            if (proj == null) return;
            if (proj.Economies == null || proj.Economies.Count == 0) Mods.Remove(proj.ModId);
            else Mods[proj.ModId] = proj.Economies;
        }

        public static void Clear() => Mods.Clear();

        public static bool HasRules
        {
            get
            {
                foreach (var d in Mods.Values) if (d.Count > 0) return true;
                return false;
            }
        }

        public static int SetCount => Mods.Values.Sum(d => d.Count);

        /// <summary>What a price rule looks at.</summary>
        public struct Query
        {
            public PriceFunction Function;
            public string Rarity;
            public string CardType;
            public string CardId;
            public string ShopId;
        }

        /// <summary>The price after every active rule. <paramref name="price"/> is returned unchanged when nothing matches.</summary>
        public static int Adjust(int price, Query q, List<string> trace = null)
        {
            int result = price;
            foreach (var kv in Mods)
                foreach (var set in kv.Value.Values)
                    result = AdjustWith(set, result, q, trace);
            return result;
        }

        /// <summary>Applies one set only. Used by the editor's price calculator.</summary>
        public static int AdjustWith(EconomyDef set, int price, Query q, List<string> trace = null)
        {
            int result = price;
            if (set == null || !set.Enabled || set.Rules == null) return result;
            foreach (var r in set.Rules)
            {
                if (r == null || !r.Enabled || !Matches(r, q)) continue;
                int before = result;
                result = Apply(r, result);
                trace?.Add($"{(string.IsNullOrEmpty(r.Name) ? r.Function.ToString() : r.Name)}:  {before} \u2192 {result}");
            }
            return result;
        }

        /// <summary>Same as <see cref="Adjust"/> but also writes a line to the log when the price moved.</summary>
        public static int AdjustLogged(int price, Query q)
        {
            if (price < 0) return price;
            int result = Adjust(price, q);
            if (result != price)
            {
                string what = string.IsNullOrEmpty(q.CardId) ? "" : " " + q.CardId;
                string shop = string.IsNullOrEmpty(q.ShopId) ? "" : " @" + q.ShopId;
                string line = $"{q.Function}{what}{shop}: {price} \u2192 {result}";
                if (Recent.Count == 0 || Recent[Recent.Count - 1] != line)
                {
                    Recent.Add(line);
                    if (Recent.Count > LogCap) Recent.RemoveAt(0);
                }
            }
            return result;
        }

        private static bool Matches(PriceRuleDef r, Query q)
        {
            if (r.Function != q.Function) return false;
            if (!string.IsNullOrEmpty(r.Rarity) && !string.Equals(r.Rarity, q.Rarity, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(r.CardType) && !string.Equals(r.CardType, q.CardType, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(r.CardId) && !string.Equals(r.CardId, q.CardId, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(r.ShopId) && !string.Equals(r.ShopId, q.ShopId, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static int Apply(PriceRuleDef r, int price)
        {
            long p = r.SetTo >= 0 ? r.SetTo : price;
            if (r.Percent != 0) p += (long)Math.Round(p * (double)r.Percent / 100.0);
            p += r.Flat;
            if (p < 0) p = 0;
            if (p > int.MaxValue / 2) p = int.MaxValue / 2;
            return (int)p;
        }

        // ── Checks ─────────────────────────────────────────────────

        private static readonly string[] Rarities = { "Common", "Uncommon", "Rare", "Epic", "Mythic" };

        public static List<string> Validate(EconomyDef set, ModProject proj)
        {
            var issues = new List<string>();
            if (set == null) return issues;
            if (string.IsNullOrWhiteSpace(set.Id)) issues.Add("The set has no id.");
            if (set.Rules == null || set.Rules.Count == 0) { issues.Add("The set has no rules, so it changes nothing."); return issues; }
            var cards = new HashSet<string>(RefIds.Cards(proj), StringComparer.OrdinalIgnoreCase);
            var shops = new HashSet<string>(RefIds.Loot(proj), StringComparer.OrdinalIgnoreCase);
            var types = new HashSet<string>(Enum.GetNames(typeof(Enums.CardType)), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < set.Rules.Count; i++)
            {
                var r = set.Rules[i];
                string at = $"Rule {i + 1}: ";
                if (r.SetTo < 0 && r.Percent == 0 && r.Flat == 0) issues.Add(at + "changes nothing (no price, percent or flat amount).");
                if (r.Percent < -100) issues.Add(at + "a percent under -100 would make the price negative; it is clamped to free.");
                if (r.SetTo >= 0 && r.Percent != 0) issues.Add(at + "has both a fixed price and a percent. The percent is applied to the fixed price.");
                if (!string.IsNullOrEmpty(r.Rarity) && !Rarities.Any(x => string.Equals(x, r.Rarity, StringComparison.OrdinalIgnoreCase)))
                    issues.Add(at + $"unknown rarity '{r.Rarity}'.");
                if (!string.IsNullOrEmpty(r.CardType) && !types.Contains(r.CardType)) issues.Add(at + $"unknown card type '{r.CardType}'.");
                if (!string.IsNullOrEmpty(r.CardId) && !cards.Contains(r.CardId)) issues.Add(at + $"card '{r.CardId}' does not exist.");
                if (!string.IsNullOrEmpty(r.ShopId) && !shops.Contains(r.ShopId)) issues.Add(at + $"shop '{r.ShopId}' is not a loot table.");
                bool hasCardFilter = !string.IsNullOrEmpty(r.Rarity) || !string.IsNullOrEmpty(r.CardType) || !string.IsNullOrEmpty(r.CardId);
                if (hasCardFilter && (r.Function == PriceFunction.Corruption || r.Function == PriceFunction.Divination || r.Function == PriceFunction.Reroll))
                    issues.Add(at + $"{r.Function} has no card, so rarity, type and card filters stop it from ever matching.");
                if (!string.IsNullOrEmpty(r.ShopId) && r.Function != PriceFunction.Item)
                    issues.Add(at + "only Item prices belong to a shop; this shop filter stops it from ever matching.");
                if (r.Function == PriceFunction.Item && !string.IsNullOrEmpty(r.CardId) && !string.IsNullOrEmpty(r.CardType))
                    issues.Add(at + "both a card and a card type are set; the card's own type must match.");
            }
            return issues;
        }
    }
}
