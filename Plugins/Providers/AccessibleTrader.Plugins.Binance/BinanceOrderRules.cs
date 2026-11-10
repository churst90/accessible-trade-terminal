using System.Text.Json;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Plugins.Binance
{
    /// <summary>
    /// One Binance market's order rules, read from its exchange-information entry: the rules a
    /// market order must meet, and the rules every other order must meet.
    ///
    /// <para>
    /// <b>Why this exists.</b> Until 2026-11-10 every Binance order went out as the ticket's
    /// double printed to ten decimals, and nothing checked a minimum. Binance answers a quantity
    /// off its step with "-1013 Filter failure: LOT_SIZE", a price off its tick with
    /// "PRICE_FILTER", and an order worth less than its minimum with "NOTIONAL" — codes a person
    /// placing a real order cannot act on. The rules are in the same <c>exchangeInfo</c> answer
    /// the symbol list already reads, as filters:
    /// </para>
    /// <list type="bullet">
    /// <item><c>LOT_SIZE</c>: minQty and stepSize for every order.</item>
    /// <item><c>MARKET_LOT_SIZE</c>: the same, for MARKET orders. Spot BTCUSDT states it as
    /// minQty 0 and stepSize 0 ("none"), so a market order falls back to LOT_SIZE; futures
    /// states real values.</item>
    /// <item><c>PRICE_FILTER</c>: tickSize.</item>
    /// <item><c>NOTIONAL</c> (spot, minNotional), the older spot <c>MIN_NOTIONAL</c>
    /// (minNotional), and futures <c>MIN_NOTIONAL</c> (notional — a different key).</item>
    /// </list>
    /// <para>
    /// Shapes checked against Binance's live answers on 2026-11-10: spot from
    /// data-api.binance.vision (Binance's public market-data host, because api.binance.com
    /// answers 451 from the US) and futures from testnet.binancefuture.com (fapi.binance.com
    /// answers 451 too). The filter names and keys match Binance's filter documentation.
    /// </para>
    /// </summary>
    internal sealed record BinanceOrderRules(SymbolOrderRules Orders, SymbolOrderRules MarketOrders)
    {
        /// <summary>The rules this order type must meet.</summary>
        public SymbolOrderRules For(OrderType type) =>
            type == OrderType.Market ? MarketOrders : Orders;

        /// <summary>
        /// Every market in an <c>exchangeInfo</c> answer, by its wire symbol ("BTCUSDT").
        /// A market whose entry cannot be read is left out, and the venue judges its orders
        /// as it always did.
        /// </summary>
        public static Dictionary<string, BinanceOrderRules> Parse(JsonElement root, bool futures)
        {
            var map = new Dictionary<string, BinanceOrderRules>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("symbols", out var symbols) || symbols.ValueKind != JsonValueKind.Array)
                return map;
            foreach (var s in symbols.EnumerateArray())
            {
                try
                {
                    var rules = ParseOne(s, futures);
                    if (rules != null) map[Str(s, "symbol")] = rules;
                }
                catch (Exception)
                {
                    // One odd entry must not cost every other market its rules.
                }
            }
            return map;
        }

        private static BinanceOrderRules? ParseOne(JsonElement s, bool futures)
        {
            string symbol = Str(s, "symbol");
            if (symbol.Length == 0) return null;
            string baseAsset = Str(s, "baseAsset"), quote = Str(s, "quoteAsset");
            string spoken = baseAsset.Length > 0 && quote.Length > 0 ? $"{baseAsset}/{quote}" : symbol;
            if (futures) spoken += " futures";

            string status = Str(s, "status");
            string? notTrading = status.Length == 0 || status == "TRADING"
                ? null
                : $"Binance is not taking {spoken} orders right now (it says {status})";

            decimal minQty = 0, step = 0, mktMin = 0, mktStep = 0, tick = 0, minNotional = 0;
            if (s.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in filters.EnumerateArray())
                {
                    switch (Str(f, "filterType"))
                    {
                        case "LOT_SIZE":
                            minQty = Dec(f, "minQty"); step = Dec(f, "stepSize");
                            break;
                        case "MARKET_LOT_SIZE":
                            mktMin = Dec(f, "minQty"); mktStep = Dec(f, "stepSize");
                            break;
                        case "PRICE_FILTER":
                            tick = Dec(f, "tickSize");
                            break;
                        case "NOTIONAL":
                        case "MIN_NOTIONAL":
                            // Spot spells it minNotional in both filters; futures MIN_NOTIONAL
                            // spells it notional.
                            decimal n = Dec(f, "minNotional");
                            if (n == 0) n = Dec(f, "notional");
                            if (n > 0) minNotional = n;
                            break;
                    }
                }
            }

            var orders = new SymbolOrderRules
            {
                Venue = "Binance", Symbol = spoken, QuantityUnit = baseAsset, QuoteAsset = quote,
                MinQuantity = minQty, QuantityStep = step, PriceTick = tick, MinNotional = minNotional,
                NotTrading = notTrading,
            };
            // A market order meets both lot filters: the larger minimum, and the market step
            // where one is stated (0 means none).
            var market = orders with
            {
                MinQuantity = Math.Max(minQty, mktMin),
                QuantityStep = mktStep > 0 ? mktStep : step,
            };
            return new BinanceOrderRules(orders, market);
        }

        /// <summary>
        /// The most each market lends at, from <c>GET /fapi/v1/leverageBracket</c>: the highest
        /// <c>initialLeverage</c> across its brackets (the first bracket, the smallest
        /// positions). The answer is an array of markets, or one market's object when a symbol
        /// was named; both are read.
        /// </summary>
        public static Dictionary<string, int> ParseLeverageBrackets(JsonElement root)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<JsonElement> entries = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray(),
                JsonValueKind.Object => new[] { root },
                _ => Array.Empty<JsonElement>(),
            };
            foreach (var e in entries)
            {
                string symbol = Str(e, "symbol");
                if (symbol.Length == 0 || !e.TryGetProperty("brackets", out var brackets)
                    || brackets.ValueKind != JsonValueKind.Array) continue;
                int max = 0;
                foreach (var b in brackets.EnumerateArray())
                    if (b.TryGetProperty("initialLeverage", out var il) && il.TryGetInt32(out int v))
                        max = Math.Max(max, v);
                if (max > 0) map[symbol] = max;
            }
            return map;
        }

        private static string Str(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static decimal Dec(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v)) return 0m;
            return v.ValueKind switch
            {
                JsonValueKind.String => SymbolOrderRules.ParseStep(v.GetString()),
                JsonValueKind.Number => v.TryGetDecimal(out var d) && d > 0 ? d : 0m,
                _ => 0m,
            };
        }
    }
}
