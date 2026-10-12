using System;
using Cards;
using HarmonyLib;
using UnknownMod.Core;
using UnknownMod.Definitions;

namespace UnknownMod
{
    /// <summary>Feeds the game's price calculations through economy sets. Own class so a game update cannot disable other patches.</summary>
    [HarmonyPatch]
    public static class EconomyPatches
    {
        // SetPrice is overloaded (a deck summary overload takes a list), so the signature is spelled out.
        [HarmonyPatch(typeof(CardCraftManager), "SetPrice", new[] { typeof(string), typeof(string), typeof(string), typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        public static void SetPrice_Post(string function, string rarity, string cardName, ref int __result,
            string ___itemListId, CardRealtimeData ___cardData)
        {
            if (__result < 0 || !EconomyEngine.HasRules) return;
            try
            {
                var q = new EconomyEngine.Query { Rarity = rarity };
                switch (function)
                {
                    case "Craft": q.Function = PriceFunction.Craft; break;
                    case "Upgrade": q.Function = PriceFunction.Upgrade; break;
                    case "Transform": q.Function = PriceFunction.Transform; break;
                    case "Remove": q.Function = PriceFunction.Remove; break;
                    case "Item": q.Function = PriceFunction.Item; q.ShopId = ___itemListId; break;
                    case "Corruption": q.Function = PriceFunction.Corruption; break;
                    default: return;
                }
                CardRealtimeData card = null;
                if (!string.IsNullOrEmpty(cardName)) card = Globals.Instance?.GetCardData(cardName, false);
                else if (q.Function == PriceFunction.Remove) card = ___cardData;
                if (card != null)
                {
                    q.CardId = card.Id;
                    q.CardType = card.CardType.ToString();
                    if (string.IsNullOrEmpty(q.Rarity)) q.Rarity = card.CardRarity.ToString();
                }
                __result = EconomyEngine.AdjustLogged(__result, q);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Economy] price rules failed for {function}: {e.Message}");
            }
        }

        [HarmonyPatch(typeof(Globals), nameof(Globals.GetDivinationCost))]
        [HarmonyPostfix]
        public static void DivinationCost_Post(ref int __result) => Simple(PriceFunction.Divination, ref __result);

        [HarmonyPatch(typeof(Globals), nameof(Globals.GetCostReroll))]
        [HarmonyPostfix]
        public static void RerollCost_Post(ref int __result) => Simple(PriceFunction.Reroll, ref __result);

        private static void Simple(PriceFunction f, ref int result)
        {
            if (result < 0 || !EconomyEngine.HasRules) return;
            try { result = EconomyEngine.AdjustLogged(result, new EconomyEngine.Query { Function = f }); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Economy] price rules failed for {f}: {e.Message}"); }
        }
    }
}
