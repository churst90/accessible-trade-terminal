using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The shared reader of a market's order rules (2026-11-10). Every crypto venue but Kraken
    /// sent the ticket's double as it came and checked no minimum; each provider now fills a
    /// <see cref="SymbolOrderRules"/> from its own exchange-information answer, and these pin
    /// the arithmetic they all share. The per-venue wiring has its own tests.
    /// </summary>
    public class SymbolOrderRulesTests
    {
        private static SymbolOrderRules Btc() => new()
        {
            Venue = "Gemini", Symbol = "BTC/USD", QuantityUnit = "BTC", QuoteAsset = "USD",
            MinQuantity = 0.00001m, QuantityStep = 0.00000001m, PriceTick = 0.01m, MinNotional = 0,
        };

        [Fact]
        public void A_quantity_is_cut_down_to_the_step_never_up()
        {
            var r = Btc() with { QuantityStep = 0.001m };
            Assert.Equal("0.123", r.FormatQuantity(0.1239));
            // In doubles 0.29 / 0.01 is 28.999…; flooring that would sell one step short.
            Assert.Equal("0.29", (r with { QuantityStep = 0.01m }).FormatQuantity(0.29));
        }

        [Fact]
        public void A_price_goes_to_the_nearest_tick()
        {
            Assert.Equal("65000.12", Btc().FormatPrice(65000.12345678));
            Assert.Equal("0.5", (Btc() with { PriceTick = 0.5m }).FormatPrice(0.4));
        }

        [Fact]
        public void A_small_number_is_never_written_in_exponent_form()
        {
            // 0.00001.ToString() is "1E-05", which the venues refuse.
            Assert.Equal("0.00001", SymbolOrderRules.Plain(0.00001));
            Assert.Equal("0.00001", Btc().FormatQuantity(0.00001));
            Assert.Equal("0.00000123", (Btc() with { PriceTick = 0.00000001m }).FormatPrice(0.00000123));
        }

        [Fact]
        public void An_order_under_the_smallest_quantity_is_refused_in_words()
        {
            string? why = Btc().Refusal(0.000005, 65000);
            Assert.Equal("Gemini's smallest BTC/USD order is 0.00001 BTC; this one is 0.000005 BTC", why);
            Assert.Null(Btc().Refusal(0.00001, 65000));
        }

        [Fact]
        public void A_quantity_that_rounds_to_nothing_is_refused()
        {
            var r = Btc() with { MinQuantity = 0, QuantityStep = 0.001m };
            Assert.NotNull(r.Refusal(0.0004, 100));
        }

        [Fact]
        public void One_contract_is_said_as_one_contract_and_asset_codes_never_change()
        {
            // Coinbase's BTC Perp said "smallest order is 1 contracts" (found 2026-11-16).
            var c = Btc() with { Venue = "Coinbase", Symbol = "BTC Perp", QuantityUnit = "contracts", MinQuantity = 1m, QuantityStep = 1m };
            Assert.Equal("Coinbase's smallest BTC Perp order is 1 contract; this one is 0.03 contracts", c.Refusal(0.03, 65000));
            Assert.Equal("Coinbase's smallest BTC Perp order is 2 contracts; this one is 1 contract", (c with { MinQuantity = 2m }).Refusal(1, 65000));
            Assert.Equal("BTC", SymbolOrderRules.Unit(1m, "BTC"));
        }

        [Fact]
        public void An_order_under_the_smallest_value_is_refused_and_one_without_a_price_is_not_judged()
        {
            var r = Btc() with { MinNotional = 10m };
            Assert.Equal("Gemini's smallest BTC/USD order is worth 10 USD; this one is worth 6.5 USD", r.Refusal(0.0001, 65000));
            Assert.Null(r.Refusal(0.0002, 65000));
            Assert.Null(r.Refusal(0.0001, null));
        }

        [Fact]
        public void A_market_that_takes_no_orders_says_why()
        {
            var r = Btc() with { NotTrading = "Gemini has BTC/USD closed" };
            Assert.Equal("Gemini has BTC/USD closed", r.Refusal(1, 65000));
        }

        [Theory]
        [InlineData("0.00001000", 0.00001)]
        [InlineData("1e-5", 0.00001)]
        [InlineData("", 0)]
        [InlineData("-1", 0)]
        public void Steps_are_read_from_the_venues_strings(string text, double expected) =>
            Assert.Equal((decimal)expected, SymbolOrderRules.ParseStep(text));

        [Fact]
        public void A_count_of_decimals_is_a_step() =>
            Assert.Equal(0.001m, SymbolOrderRules.StepFromDecimals(3));

        // ── Cash the risk sizer can be a percentage of ───────────────────────

        [Theory]
        [InlineData("GUSD")]      // Gemini's dollar
        [InlineData("PYUSD")]
        [InlineData("FDUSD")]
        [InlineData("USDC-PERPS")] // Hyperliquid's perps margin
        [InlineData("usd")]
        public void Venue_dollars_are_cash(string asset) => Assert.True(QuickTradeEquity.IsCashAsset(asset));

        [Theory]
        [InlineData("BTC")]
        [InlineData("USDC-SPOT")]
        public void Coins_are_not_cash(string asset) => Assert.False(QuickTradeEquity.IsCashAsset(asset));
    }
}
