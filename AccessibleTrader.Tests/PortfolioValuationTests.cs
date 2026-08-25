using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The Balances tab showed asset, free and locked — quantities with no value,
    /// no total, no allocation, no day change. These pin the arithmetic that
    /// replaces that, and especially the rule it is built around:
    ///
    /// <para>
    /// **An asset that cannot be priced is never counted as zero.** It keeps its
    /// row, it is marked, and the total states how many assets it covers. A total
    /// that silently omits a holding is worse than no total, because it is a number
    /// the user will act on.
    /// </para>
    /// </summary>
    public class PortfolioValuationTests
    {
        /// <summary>A price source with fixed answers; null means "no such market".</summary>
        private sealed class Fixed : IAssetPriceSource
        {
            private readonly Dictionary<string, (double, double?)?> _prices;
            private readonly HashSet<string> _throws;
            public Fixed(Dictionary<string, (double, double?)?> prices, params string[] throws)
            {
                _prices = prices;
                _throws = new HashSet<string>(throws, StringComparer.OrdinalIgnoreCase);
            }
            public Task<(double Price, double? PreviousClose)?> TryGetDailyAsync(
                string provider, string asset, string quote, CancellationToken ct = default)
            {
                if (_throws.Contains(asset)) throw new InvalidOperationException("venue timed out");
                return Task.FromResult(_prices.TryGetValue(asset, out var p) ? p : null);
            }
        }

        private static PortfolioValuationService Svc(
            Dictionary<string, (double, double?)?> prices, params string[] throws) =>
            new(new Fixed(prices, throws));

        private static List<Balance> Held(params (string Asset, double Free, double Locked)[] rows) =>
            rows.Select(r => new Balance(r.Asset, r.Free, r.Locked)).ToList();

        [Fact]
        public async Task Values_holdings_and_totals_them()
        {
            var svc = Svc(new() { ["BTC"] = (100_000, 98_000), ["ETH"] = (4_000, 4_000) });

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 0.5, 0), ("ETH", 2, 0), ("USD", 1_000, 0)));

            Assert.Equal(50_000 + 8_000 + 1_000, snap.TotalValue, 6);
            Assert.True(snap.IsComplete);
            Assert.Equal(3, snap.PricedCount);
        }

        [Fact]
        public async Task Locked_balance_counts_toward_value()
        {
            // Staked or held-against-an-order funds are still yours; excluding them
            // would understate the account.
            var svc = Svc(new() { ["BTC"] = (100_000, null) });

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 0.5, 0.5)));

            Assert.Equal(100_000, snap.TotalValue, 6);
        }

        [Fact]
        public async Task An_unpriceable_asset_is_marked_not_zeroed()
        {
            var svc = Svc(new() { ["BTC"] = (100_000, null) });   // FOO has no market

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 1, 0), ("FOO", 500, 0)));

            Assert.Equal(100_000, snap.TotalValue, 6);   // FOO excluded from the total…
            Assert.False(snap.IsComplete);
            Assert.Equal(1, snap.PricedCount);
            Assert.Equal(2, snap.TotalCount);

            // …but present, with its quantity and a reason.
            var foo = Assert.Single(snap.Assets, a => a.Asset == "FOO");
            Assert.Equal(500, foo.Quantity);
            Assert.Null(foo.Value);
            Assert.False(string.IsNullOrWhiteSpace(foo.Unpriced));
        }

        [Fact]
        public async Task A_thrown_price_lookup_is_also_unpriced_rather_than_zero()
        {
            var svc = Svc(new() { ["BTC"] = (100_000, null) }, throws: "ETH");

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 1, 0), ("ETH", 10, 0)));

            var eth = Assert.Single(snap.Assets, a => a.Asset == "ETH");
            Assert.False(eth.IsPriced);
            Assert.Contains("timed out", eth.Unpriced);
            Assert.Equal(100_000, snap.TotalValue, 6);
        }

        [Fact]
        public async Task The_summary_says_the_total_is_partial_when_it_is()
        {
            var svc = Svc(new() { ["BTC"] = (100_000, null) });

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 1, 0), ("FOO", 5, 0), ("BAR", 5, 0)));
            string said = PortfolioValuationService.Summarise(snap);

            Assert.Contains("1 of 3 assets", said);
            Assert.Contains("FOO", said);
            Assert.Contains("BAR", said);
            Assert.Contains("could not be priced", said);
        }

        [Fact]
        public async Task The_summary_stays_plain_when_everything_is_priced()
        {
            var svc = Svc(new() { ["BTC"] = (100_000, null) });

            string said = PortfolioValuationService.Summarise(
                await svc.ValueAsync("Kraken", Held(("BTC", 1, 0))));

            Assert.DoesNotContain("could not be priced", said);
            Assert.Contains("across 1 asset", said);
        }

        [Fact]
        public async Task Day_change_comes_from_the_previous_close()
        {
            var svc = Svc(new() { ["BTC"] = (110.0, 100.0) });

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 1, 0)));

            Assert.Equal(10.0, snap.Assets[0].DayChangePct!.Value, 6);
        }

        [Fact]
        public async Task Day_change_is_null_rather_than_zero_without_a_previous_close()
        {
            // One bar of history is not a flat day; saying 0% would invent a fact.
            var svc = Svc(new() { ["BTC"] = (110.0, null) });

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 1, 0)));

            Assert.Null(snap.Assets[0].DayChangePct);
        }

        [Fact]
        public async Task Quote_currency_is_worth_its_face_value_without_a_lookup()
        {
            // Pricing USD in USD through a market would fail and mark real cash
            // unpriced, which is the most alarming possible way to be wrong.
            var svc = Svc(new());

            var snap = await svc.ValueAsync("Alpaca", Held(("USD", 5_000, 0)));

            Assert.True(snap.IsComplete);
            Assert.Equal(5_000, snap.TotalValue, 6);
        }

        [Fact]
        public async Task Allocation_is_null_for_an_unpriced_asset_not_zero_percent()
        {
            // 0% reads as "worth nothing"; the truth is "unknown".
            var svc = Svc(new() { ["BTC"] = (100_000, null) });

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 1, 0), ("FOO", 5, 0)));
            var foo = snap.Assets.Single(a => a.Asset == "FOO");
            var btc = snap.Assets.Single(a => a.Asset == "BTC");

            Assert.Null(PortfolioValuationService.AllocationPercent(foo, snap));
            Assert.Equal(100.0, PortfolioValuationService.AllocationPercent(btc, snap)!.Value, 6);
        }

        [Fact]
        public async Task Zero_balances_are_left_out_entirely()
        {
            var svc = Svc(new() { ["BTC"] = (100_000, null) });

            var snap = await svc.ValueAsync("Kraken", Held(("BTC", 1, 0), ("XRP", 0, 0)));

            Assert.Equal(1, snap.TotalCount);
        }

        [Fact]
        public async Task Priced_assets_sort_above_unpriced_ones()
        {
            var svc = Svc(new() { ["BTC"] = (100_000, null), ["ETH"] = (4_000, null) });

            var snap = await svc.ValueAsync("Kraken", Held(("FOO", 1, 0), ("ETH", 1, 0), ("BTC", 1, 0)));

            Assert.Equal(new[] { "BTC", "ETH", "FOO" }, snap.Assets.Select(a => a.Asset));
        }

        // ── Symbol resolution ────────────────────────────────────────────────

        [Theory]
        [InlineData("ETH", "USD", "ETHUSD")]
        [InlineData("ETH", "USDT", "ETHUSDT")]
        public void The_plainest_spelling_is_tried_first(string asset, string quote, string expected) =>
            Assert.Equal(expected, MarketDataPriceSource.CandidateSymbols(asset, quote).First());

        [Fact]
        public void Separated_spellings_are_tried_because_venues_differ()
        {
            var c = MarketDataPriceSource.CandidateSymbols("ETH", "USD").ToList();

            Assert.Contains("ETH/USD", c);
            Assert.Contains("ETH-USD", c);
        }

        [Fact]
        public void Bitcoin_is_also_tried_as_XBT()
        {
            // Kraken still prices bitcoin as XBT on many pairs, so BTC alone would
            // leave the largest holding in a crypto account unpriced.
            Assert.Contains("XBTUSD", MarketDataPriceSource.CandidateSymbols("BTC", "USD"));
        }

        [Fact]
        public void Dollar_stablecoins_are_interchangeable_for_valuation()
        {
            // Display figure, not a trade. Refusing to value a holding because the
            // book is quoted in USDT rather than USD costs the user a blank cell to
            // make a distinction that does not matter here.
            var c = MarketDataPriceSource.CandidateSymbols("ETH", "USD").ToList();

            Assert.Contains("ETHUSDT", c);
            Assert.Contains("ETHUSDC", c);
            Assert.True(c.IndexOf("ETHUSD") < c.IndexOf("ETHUSDT"),
                "the exact quote must be preferred over an equivalent one");
        }
    }
}
