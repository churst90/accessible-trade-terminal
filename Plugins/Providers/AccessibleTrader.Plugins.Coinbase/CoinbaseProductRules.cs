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
    ///
    /// <para>
    /// A FUTURE product (2026-11-14) is sized in CONTRACTS: the live BIP-20DEC30-CDE ("BTC
    /// Perp") answer has base_increment "1", base_min_size "1", base_max_size "5000", and its
    /// future_product_details say contract_size "0.01", contract_root_unit "BTC". It has no
    /// base_currency_id and an empty status, so the spot reading named it "BIP/20DEC30"; it is
    /// named by its display_name instead, and <see cref="Contract"/> carries what one contract is.
    /// </para>
    /// </summary>
    internal sealed record CoinbaseProductRules(SymbolOrderRules Rules, bool LimitOnly, bool PostOnly, CoinbaseContract? Contract = null)
    {
        /// <summary>The rules from one product answer, or null when it names no product.</summary>
        public static CoinbaseProductRules? Parse(JObject product)
        {
            string id = product["product_id"]?.ToString() ?? "";
            if (id.Length == 0) return null;
            if (product["future_product_details"] is JObject
                || string.Equals(product["product_type"]?.ToString(), "FUTURE", StringComparison.OrdinalIgnoreCase))
                return ParseFuture(product, id);

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

        /// <summary>One futures contract's rules. Sizes are whole contracts; there is no order-value
        /// minimum (quote_min_size is "0").</summary>
        private static CoinbaseProductRules ParseFuture(JObject product, string id)
        {
            var f = product["future_product_details"] as JObject;
            string name = Text(product, "display_name") ?? Text(f ?? new JObject(), "contract_display_name", "display_name") ?? id;
            string unit = f?["contract_root_unit"]?.ToString() ?? "";
            decimal size = SymbolOrderRules.ParseStep(f?["contract_size"]?.ToString());

            bool Flag(JToken? o, string n) => o?[n]?.Type == JTokenType.Boolean && o[n]!.Value<bool>();
            var session = product["fcm_trading_session_details"] as JObject;
            string? notTrading = null;
            if (Flag(product, "view_only"))
                notTrading = $"Coinbase lists {name} as expired; it takes no new orders";
            else if (Flag(product, "trading_disabled") || Flag(product, "is_disabled"))
                notTrading = $"Coinbase has trading on {name} switched off right now";
            else if (Flag(product, "cancel_only"))
                notTrading = $"Coinbase has {name} in cancel-only mode: it takes no new orders right now";
            // A closed session still takes orders after hours unless Coinbase says it does not.
            else if (session != null && session["is_session_open"]?.Type == JTokenType.Boolean
                     && !Flag(session, "is_session_open") && Flag(session, "after_hours_order_entry_disabled"))
                notTrading = $"The {name} trading session is closed and Coinbase takes no orders until it opens"
                           + (OpenTime(session) is DateTime opens ? $" at {opens:yyyy-MM-dd HH:mm} UTC" : "");

            return new CoinbaseProductRules(
                new SymbolOrderRules
                {
                    Venue        = "Coinbase",
                    Symbol       = name,
                    QuantityUnit = "contracts",
                    QuoteAsset   = Text(product, "quote_currency_id", "quote_display_symbol") ?? "USD",
                    MinQuantity  = SymbolOrderRules.ParseStep(product["base_min_size"]?.ToString()),
                    QuantityStep = SymbolOrderRules.ParseStep(product["base_increment"]?.ToString()),
                    PriceTick    = SymbolOrderRules.ParseStep(product["price_increment"]?.ToString()) is var tick && tick > 0
                                       ? tick
                                       : SymbolOrderRules.ParseStep(product["quote_increment"]?.ToString()),
                    NotTrading   = notTrading,
                },
                Flag(product, "limit_only"),
                Flag(product, "post_only"),
                new CoinbaseContract(name, size, unit, SymbolOrderRules.ParseStep(product["base_max_size"]?.ToString())));
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
            if (Contract != null)
            {
                if (Rules.Refusal(signal.Quantity, price) is string small) return small + Contract.Note;
                if (Contract.MaxContracts > 0 && Rules.FloorQuantity(signal.Quantity) > Contract.MaxContracts)
                    return $"Coinbase's largest {Rules.Symbol} order is {SymbolOrderRules.Plain(Contract.MaxContracts)} contracts; "
                         + $"this one is {SymbolOrderRules.Plain(signal.Quantity)}{Contract.Note}";
                return null;
            }
            return Rules.Refusal(signal.Quantity, price);
        }

        private static DateTime? OpenTime(JObject session) => session["open_time"] switch
        {
            JValue { Type: JTokenType.Date } d => d.Value<DateTime>().ToUniversalTime(),
            JValue v when DateTime.TryParse(v.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t) => t,
            _ => null,
        };

        private static string? Text(JObject o, params string[] names)
        {
            foreach (var n in names)
                if (o[n]?.ToString() is { Length: > 0 } v) return v;
            return null;
        }
    }

    /// <summary>What one Coinbase futures contract is: <see cref="Size"/> of <see cref="Unit"/>
    /// (0.01 BTC on the BTC Perp), and the most contracts one order may carry (0 = unknown).</summary>
    internal sealed record CoinbaseContract(string Name, decimal Size, string Unit, decimal MaxContracts)
    {
        /// <summary>" (one contract is 0.01 BTC)", or nothing when the size is not known.</summary>
        public string Note => Size > 0 && Unit.Length > 0 ? $" (one contract is {SymbolOrderRules.Plain(Size)} {Unit})" : "";
    }
}
