using System.Globalization;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Kraken
{
    /// <summary>
    /// What Kraken's AssetPairs endpoint says one pair will accept: the smallest order, how many
    /// decimals a volume and a price may carry, which leverage amounts it lends at, and whether
    /// it is trading at all.
    ///
    /// <para>
    /// <b>Why this exists.</b> Orders went out with the volume at eight decimals and the price
    /// unrounded whatever the pair, and with no minimum checked. Kraken refuses all three, and
    /// says so in its own codes — "EGeneral:Invalid arguments:volume" is not something a person
    /// placing their first real order can act on. KAS/USD's smallest order is 170 KAS; TAO/USD
    /// prices carry four decimals; XBT/USD prices carry one. The leverage field offered a fixed
    /// 1 to 5 times on every pair, while KAS/USD lends at 2 or 3 times only and XBT/USD at up
    /// to 10 (all checked live 2026-11-10).
    /// </para>
    /// </summary>
    internal sealed record KrakenPairRules(
        string Name,
        string Base,
        double OrderMin,
        double CostMin,
        int LotDecimals,
        int PairDecimals,
        double TickSize,
        IReadOnlyList<int> LeverageBuy,
        IReadOnlyList<int> LeverageSell,
        string Status)
    {
        /// <summary>Every spelling a pair can arrive in, mapped to its rules: Kraken's key
        /// (<c>XXBTZUSD</c>), its altname (<c>XBTUSD</c>) and its wsname (<c>XBT/USD</c>), each
        /// folded through <see cref="KrakenProvider.CanonicalKrakenPair"/> so the user's
        /// <c>BTC/USD</c> finds them too.</summary>
        internal static Dictionary<string, KrakenPairRules> Parse(JObject result)
        {
            var map = new Dictionary<string, KrakenPairRules>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in result.Properties())
            {
                if (p.Value is not JObject v || p.Name.EndsWith(".d", StringComparison.Ordinal)) continue;
                string ws = v["wsname"]?.ToString() ?? p.Name;
                var rules = new KrakenPairRules(
                    Name:         ws.Replace("XBT", "BTC", StringComparison.Ordinal).Replace("XDG", "DOGE", StringComparison.Ordinal),
                    Base:         KrakenBalances.AssetName(v["base"]?.ToString() ?? ""),
                    OrderMin:     Number(v["ordermin"]),
                    CostMin:      Number(v["costmin"]),
                    LotDecimals:  v["lot_decimals"]?.Value<int>() ?? 8,
                    PairDecimals: v["pair_decimals"]?.Value<int>() ?? 8,
                    TickSize:     Number(v["tick_size"]),
                    LeverageBuy:  Ints(v["leverage_buy"]),
                    LeverageSell: Ints(v["leverage_sell"]),
                    Status:       v["status"]?.ToString() ?? "online");
                foreach (string spelling in new[] { p.Name, v["altname"]?.ToString() ?? "", ws })
                    if (spelling.Length > 0)
                        map[KrakenProvider.CanonicalKrakenPair(spelling)] = rules;
            }
            return map;
        }

        /// <summary>The leverage amounts Kraken lends at on this pair for this side. Empty
        /// means it does not lend on this pair at all.</summary>
        internal IReadOnlyList<int> LeverageFor(OrderSide side) =>
            side == OrderSide.Buy ? LeverageBuy : LeverageSell;

        /// <summary>The volume Kraken will accept: cut DOWN to the pair's lot decimals, never
        /// up — rounding up would buy more than was asked for.</summary>
        internal double RoundVolume(double volume)
        {
            double scale = Math.Pow(10, LotDecimals);
            // The small epsilon keeps 0.29 (stored as 0.28999999…) from losing its last digit.
            return Math.Floor(volume * scale + 1e-9) / scale;
        }

        /// <summary>The nearest price the pair can carry: its tick, at its decimals.</summary>
        internal double RoundPrice(double price)
        {
            double p = TickSize > 0 ? Math.Round(price / TickSize, MidpointRounding.AwayFromZero) * TickSize : price;
            return Math.Round(p, PairDecimals, MidpointRounding.AwayFromZero);
        }

        internal string FormatVolume(double volume) =>
            RoundVolume(volume).ToString("F" + LotDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        internal string FormatPrice(double price) =>
            RoundPrice(price).ToString("F" + PairDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        /// <summary>
        /// Why Kraken would refuse this order, in words, or null when it would not on these
        /// grounds. <paramref name="price"/> is the limit or trigger when the order has one; a
        /// market order has none to check the cost minimum against, and its minimum in units
        /// is checked anyway.
        /// </summary>
        internal string? Refusal(TradeSignal signal, double? price)
        {
            switch (Status)
            {
                case "online":
                    break;
                case "cancel_only":
                    return $"Kraken has {Name} in cancel-only mode: orders can be cancelled but not placed";
                case "reduce_only":
                    if (!signal.ReduceOnly)
                        return $"Kraken has {Name} in reduce-only mode: only orders that close a position are accepted";
                    break;
                case "limit_only":
                case "post_only":
                    if (signal.Type != OrderType.Limit)
                        return $"Kraken has {Name} in {Status.Replace('_', '-')} mode: only limit orders are accepted";
                    break;
            }

            double volume = RoundVolume(signal.Quantity);
            if (volume <= 0 || volume + 1e-12 < OrderMin)
                return $"Kraken's smallest {Name} order is {Show(OrderMin)} {Base}; this one is {Show(signal.Quantity)} {Base}";

            if (price is > 0 && CostMin > 0 && volume * price.Value + 1e-12 < CostMin)
                return $"Kraken's smallest {Name} order is worth {Show(CostMin)} in {Quote}; this one is worth {Show(volume * price.Value)}";

            return null;
        }

        private string Quote => Name.Contains('/') ? Name[(Name.IndexOf('/') + 1)..] : "the quote currency";

        private static string Show(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);

        private static double Number(JToken? t) => KrakenBalances.Number(t);

        private static IReadOnlyList<int> Ints(JToken? t) =>
            (t as JArray)?.Select(x => x.Value<int>()).Where(x => x > 1).OrderBy(x => x).ToList() ?? new List<int>();
    }
}
