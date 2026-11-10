using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Bitstamp
{
    /// <summary>
    /// What Bitstamp says one pair will accept, read from <c>/api/v2/trading-pairs-info/</c>.
    ///
    /// <para>
    /// Until 2026-11-10 the amount and price went out through <c>ToString()</c> as the ticket's
    /// double came: unrounded, so an amount finer than the pair's decimals was refused, and a
    /// small one printed as "1E-05", which Bitstamp cannot read at all. No minimum was checked
    /// either. The fields, as Bitstamp answered on 2026-11-10 (252 pairs): BTC/USD
    /// base_decimals 8, counter_decimals 2, minimum_order "10.00 USD", trading "Enabled";
    /// SHIB/USD base_decimals 0, counter_decimals 8; ZK/EUR trading "Disabled". The minimum is
    /// an order VALUE in the counter currency, never a quantity.
    /// </para>
    /// </summary>
    internal sealed record BitstampPairRules(SymbolOrderRules Rules, bool MarketOrders)
    {
        /// <summary>Every pair in the answer, keyed by its url_symbol ("btcusd").</summary>
        public static Dictionary<string, BitstampPairRules> Parse(JArray pairs)
        {
            var result = new Dictionary<string, BitstampPairRules>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pairs.OfType<JObject>())
            {
                string key = p["url_symbol"]?.ToString() ?? "";
                if (key.Length == 0) continue;
                string name = p["name"]?.ToString() is { Length: > 0 } n ? n : key.ToUpperInvariant();
                string baseAsset = name.Contains('/') ? name.Split('/')[0] : "";

                // "10.00 USD": the amount, then the currency it is counted in.
                var minimum = (p["minimum_order"]?.ToString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string quoteAsset = minimum.Length > 1 ? minimum[1] : (name.Contains('/') ? name.Split('/')[1] : "");

                string trading = p["trading"]?.ToString() ?? "";
                string market  = p["instant_and_market_orders"]?.ToString() ?? "";

                result[key] = new BitstampPairRules(
                    new SymbolOrderRules
                    {
                        Venue        = "Bitstamp",
                        Symbol       = name,
                        QuantityUnit = baseAsset,
                        QuoteAsset   = quoteAsset,
                        QuantityStep = Decimals(p["base_decimals"]),
                        PriceTick    = Decimals(p["counter_decimals"]),
                        MinNotional  = SymbolOrderRules.ParseStep(minimum.Length > 0 ? minimum[0] : null),
                        NotTrading   = trading.Length > 0 && !trading.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                                           ? $"Bitstamp has trading on {name} switched off right now"
                                           : null,
                    },
                    MarketOrders: market.Length == 0 || market.Equals("Enabled", StringComparison.OrdinalIgnoreCase));
            }
            return result;
        }

        /// <summary>
        /// Why Bitstamp would refuse this order, in words, or null. The order value is checked
        /// at the limit price; a market order is checked at its limit_price ceiling when it
        /// carries one, and otherwise only its size is checked.
        /// </summary>
        public string? Refusal(TradeSignal signal)
        {
            if (Rules.NotTrading != null) return Rules.NotTrading;
            if (signal.Type == OrderType.Market && !MarketOrders)
                return $"Bitstamp takes no market orders on {Rules.Symbol} right now; a limit order is still accepted";
            return Rules.Refusal(signal.Quantity, signal.Price);
        }

        // A count of decimals, 0 included (SHIB trades in whole coins): 0 is a step of 1.
        // An absent or unreadable count is no step at all.
        private static decimal Decimals(JToken? token) =>
            token != null && int.TryParse(token.ToString(), out int d) && d >= 0
                ? SymbolOrderRules.StepFromDecimals(d)
                : 0m;
    }
}
