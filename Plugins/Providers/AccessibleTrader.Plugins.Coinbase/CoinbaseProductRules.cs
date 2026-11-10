using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Coinbase
{
    /// <summary>
    /// What Coinbase says one product will accept, read from its product answer.
    ///
    /// <para>
    /// Until 2026-11-10 every size and price went out at "F8" whatever the product, and no
    /// minimum was checked: a BTC-USD limit at 74348.12345678 (the tick there is 0.01) or an
    /// order under the product's quote_min_size came back in Coinbase's own codes
    /// ("INVALID_PRICE_PRECISION", "INVALID_LIMIT_PRICE"). The fields, as the public BTC-USD
    /// product answered on 2026-11-10: base_increment "0.00000001", base_min_size
    /// "0.00000001", price_increment "0.01", quote_increment "0.01", quote_min_size "1",
    /// status "online", and the switches trading_disabled, is_disabled, cancel_only,
    /// limit_only and post_only, all false.
    /// </para>
    /// </summary>
    internal sealed record CoinbaseProductRules(SymbolOrderRules Rules, bool LimitOnly, bool PostOnly)
    {
        /// <summary>The rules from one product answer, or null when it names no product.</summary>
        public static CoinbaseProductRules? Parse(JObject product)
        {
            string id = product["product_id"]?.ToString() ?? "";
            if (id.Length == 0) return null;

            string baseAsset  = Text(product, "base_currency_id",  "base_display_symbol")  ?? id.Split('-')[0];
            string quoteAsset = Text(product, "quote_currency_id", "quote_display_symbol") ?? (id.Contains('-') ? id.Split('-')[1] : "");
            string symbol = baseAsset + "/" + quoteAsset;

            bool Flag(string name) => product[name]?.Type == JTokenType.Boolean && product[name]!.Value<bool>();
            string status = product["status"]?.ToString() ?? "";

            string? notTrading = null;
            if (Flag("trading_disabled") || Flag("is_disabled"))
                notTrading = $"Coinbase has trading on {symbol} switched off right now";
            else if (status.Length > 0 && !status.Equals("online", StringComparison.OrdinalIgnoreCase))
                notTrading = $"Coinbase is not taking orders on {symbol} right now (it says the market is {status})";
            else if (Flag("cancel_only"))
                notTrading = $"Coinbase has {symbol} in cancel-only mode: it takes no new orders right now";

            return new CoinbaseProductRules(
                new SymbolOrderRules
                {
                    Venue        = "Coinbase",
                    Symbol       = symbol,
                    QuantityUnit = baseAsset,
                    QuoteAsset   = quoteAsset,
                    MinQuantity  = SymbolOrderRules.ParseStep(product["base_min_size"]?.ToString()),
                    QuantityStep = SymbolOrderRules.ParseStep(product["base_increment"]?.ToString()),
                    // price_increment is the tick; quote_increment is the step of an amount of
                    // money and the same on most products, so it stands in when the tick is absent.
                    PriceTick    = SymbolOrderRules.ParseStep(product["price_increment"]?.ToString()) is var tick && tick > 0
                                       ? tick
                                       : SymbolOrderRules.ParseStep(product["quote_increment"]?.ToString()),
                    MinNotional  = SymbolOrderRules.ParseStep(product["quote_min_size"]?.ToString()),
                    NotTrading   = notTrading,
                },
                Flag("limit_only"),
                Flag("post_only"));
        }

        /// <summary>
        /// Why Coinbase would refuse this order, in words, or null. The order value is checked
        /// against quote_min_size at the limit price, or at the stop's trigger; a market order
        /// carries no price, so only its size is checked.
        /// </summary>
        public string? Refusal(TradeSignal signal)
        {
            if (Rules.NotTrading != null) return Rules.NotTrading;
            // Every order this terminal sends to Coinbase has post_only false.
            if (PostOnly)
                return $"Coinbase takes only post-only (maker) orders on {Rules.Symbol} right now, which the terminal does not send";
            if (LimitOnly && signal.Type == OrderType.Market)
                return $"Coinbase takes only limit orders on {Rules.Symbol} right now; a market order would be refused";

            double? price = signal.Type switch
            {
                OrderType.Limit or OrderType.StopLimit => signal.Price,
                OrderType.StopMarket => signal.TriggerPrice ?? signal.StopLoss,
                _ => null,
            };
            return Rules.Refusal(signal.Quantity, price);
        }

        private static string? Text(JObject o, params string[] names)
        {
            foreach (var n in names)
                if (o[n]?.ToString() is { Length: > 0 } v) return v;
            return null;
        }
    }
}
