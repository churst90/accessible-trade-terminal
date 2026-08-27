// Razor components live in this namespace. An "unused using" sweep run before
// BlazorClient.Components has generated its component types will not see them and
// will offer to delete this line; it is used. See the same note in WebHost/Program.cs.
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Regression cover for the 2026-08-21 report: "I see a position in my positions tab and I
    /// can't close it."
    ///
    /// <para>
    /// The account held a long under <c>BTC/USD</c> and a short under <c>BTCUSDT</c>. Both are the
    /// same Bitstamp book — the venue routes Tether-quoted symbols to its USD market — but the
    /// ledger keyed on the display string, so one market had become two offsetting positions that
    /// no net exposure or risk check could see as related. Neither could be closed: a market order
    /// needs a price, and the only price the account had was the focused chart's, which was some
    /// other symbol. The in-memory price table is deliberately never persisted, so a restart left
    /// every position but the charted one unclosable — and the refusal reached the user as a bare
    /// "Close failed", with the reason discarded by the caller.
    /// </para>
    /// </summary>
    public sealed class UnclosablePositionTests : IDisposable
    {
        private readonly string _dir = TestTemp.NewDir("att-unclosable-");

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
        }

        // ── Fix 2: a position is closable without its chart loaded ───────────

        [Fact]
        public async Task Closing_WhenTheChartIsNotLoaded_FetchesAPriceInsteadOfRefusing()
        {
            // The chart on screen is some other symbol, so PriceFor() knows nothing about ETHUSDT.
            // Before the fix this returned "no live price for symbol — load its chart first" and
            // the position could not be closed from that screen at all.
            var (paper, store, data) = Make();
            Price(data, "Venue", "ETHUSDT", 3000);

            store.EmitState(ChartOf("Venue", "SOMETHINGELSE", 1.0));
            string open = await paper.PlaceOrderAsync(Buy("ETHUSDT", 1.0));
            Assert.DoesNotContain("ORDER_FAILED", open);

            // Still on the other chart; now close it.
            string close = await paper.PlaceOrderAsync(Sell("ETHUSDT", 1.0));

            Assert.DoesNotContain("ORDER_FAILED", close);
            var positions = await paper.GetPositionsAsync();
            Assert.DoesNotContain(positions, p => p.Symbol.Contains("ETH", StringComparison.OrdinalIgnoreCase)
                                               && Math.Abs(p.Quantity) > 1e-9);
        }

        [Fact]
        public async Task AnImpossibleRecordedIdentity_FallsThroughToTheLiveChart()
        {
            // The recorded identity is the exact shape that caused this: a symbol paired with a
            // venue that does not list it (BTCUSDT against Bitstamp came out of a workspace
            // restore). A venue that answers nothing must not be taken as proof there is no price.
            var (paper, store, data) = Make();
            Price(data, "Bitstamp", "BTCUSDT", 0);        // the impossible pairing answers nothing
            Price(data, "MEXC", "BTCUSDT", 75_000);

            store.EmitState(ChartOf("MEXC", "BTCUSDT", 75_000));
            Assert.DoesNotContain("ORDER_FAILED", await paper.PlaceOrderAsync(Buy("BTCUSDT", 0.1)));

            // Move the chart away and make the recorded identity the bad one.
            store.EmitState(ChartOf("Bitstamp", "OTHER", 1.0));
            string close = await paper.PlaceOrderAsync(Sell("BTCUSDT", 0.1));

            Assert.DoesNotContain("ORDER_FAILED", close);
        }

        [Fact]
        public async Task WithNoPriceAnywhere_TheRefusalSaysWhatToDo()
        {
            // Still refused when nothing can price it — but the reason has to be actionable, since
            // this is the sentence the user hears.
            var (paper, store, _) = Make();
            store.EmitState(ChartOf("Venue", "OTHER", 1.0));

            string result = await paper.PlaceOrderAsync(Buy("NOSUCHTHING", 1.0));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("NOSUCHTHING", result);
            Assert.Contains("open its chart", result);
        }

        // ── Fix 3: one market cannot become two positions ────────────────────

        [Fact]
        public async Task TwoSpellingsOfOneBitstampMarket_MakeOnePosition()
        {
            // BTC/USD and BTCUSDT are the same book on Bitstamp. Buying under one spelling and
            // selling under the other must net off, not stand as a long and a short at once.
            var (paper, store, data) = Make();
            var bitstamp = VenueThatRemapsUsdtToUsd("Bitstamp");
            data.GetProviderAsync("Bitstamp").Returns(Task.FromResult<IMarketDataProvider?>(bitstamp));
            Price(data, "Bitstamp", "BTC/USD", 60_000);
            Price(data, "Bitstamp", "BTCUSDT", 60_000);

            store.EmitState(ChartOf("Bitstamp", "BTC/USD", 60_000));
            await paper.PlaceOrderAsync(Buy("BTC/USD", 1.0));
            await paper.PlaceOrderAsync(Sell("BTCUSDT", 0.25));

            var positions = (await paper.GetPositionsAsync())
                .Where(p => Math.Abs(p.Quantity) > 1e-9).ToList();

            var single = Assert.Single(positions);
            Assert.Equal(0.75, single.Quantity, 6);   // netted, not +1.0 and -0.25 side by side
        }

        [Fact]
        public async Task DifferentQuoteAssets_StayDifferentMarkets()
        {
            // The counterweight: normalisation must not conflate genuinely different books. On a
            // venue with no remap, USD and USDT are different quote assets and stay separate.
            var (paper, store, data) = Make();
            Price(data, "Venue", "BTC/USD", 60_000);
            Price(data, "Venue", "BTCUSDT", 60_000);

            store.EmitState(ChartOf("Venue", "BTC/USD", 60_000));
            // Small sizes: two 1-BTC buys at 60k exceed the 100k starting balance, and a rejected
            // second order would make this test pass for the wrong reason.
            Assert.DoesNotContain("ORDER_FAILED", await paper.PlaceOrderAsync(Buy("BTC/USD", 0.1)));
            Assert.DoesNotContain("ORDER_FAILED", await paper.PlaceOrderAsync(Buy("BTCUSDT", 0.1)));

            var positions = (await paper.GetPositionsAsync())
                .Where(p => Math.Abs(p.Quantity) > 1e-9).ToList();

            Assert.Equal(2, positions.Count);
        }

        [Fact]
        public async Task AnExistingPositionKeepsItsKey_SoItStaysClosable()
        {
            // Stored accounts predate canonical keying. Re-keying them on load would mean merging
            // a long against a short — booking realised profit at a price no trade happened at —
            // so an existing key is reused rather than renamed. What must never happen is the
            // close landing on a NEW key and leaving the original stranded.
            var (paper, store, data) = Make();
            var bitstamp = VenueThatRemapsUsdtToUsd("Bitstamp");
            data.GetProviderAsync("Bitstamp").Returns(Task.FromResult<IMarketDataProvider?>(bitstamp));
            Price(data, "Bitstamp", "BTCUSDT", 60_000);

            store.EmitState(ChartOf("Bitstamp", "BTCUSDT", 60_000));
            await paper.PlaceOrderAsync(Buy("BTCUSDT", 1.0));

            string key = (await paper.GetPositionsAsync()).Single(p => Math.Abs(p.Quantity) > 1e-9).Symbol;
            await paper.PlaceOrderAsync(Sell(key, 1.0));

            Assert.DoesNotContain(await paper.GetPositionsAsync(),
                p => Math.Abs(p.Quantity) > 1e-9);
        }

        // ── Fix 1: the refusal reaches the user with its reason ──────────────

        [Theory]
        // "insufficient" is the one reason the recogniser does NOT pass through verbatim:
        // OrderPlacement replaces it with what the user can act on, because "insufficient paper
        // balance" is true and useless — the size grew because the stop is tight. That behaviour
        // predates this patch and is pinned by QuickTradeFailureReportingTests; the expectation
        // here was written against a private second translator that has since been folded into
        // the shared one.
        [InlineData("ORDER_FAILED:insufficient paper balance — that position needs 51,970.00 USDT",
                    "choose a stop further away")]
        [InlineData("ORDER_FAILED:no price available for BTC/USD — open its chart", "No price available")]
        [InlineData("PROVIDER_NOT_CONNECTED:Bitstamp is not connected", "Bitstamp is not connected")]
        [InlineData("PROVIDER_NOT_SUPPORTED:Polygon does not support trading", "does not support trading")]
        public void AFailureCode_IsAnnouncedWithItsReason(string code, string expected)
        {
            // The dashboard announced a bare "Close failed for BTC/USD." and dropped everything
            // after the colon — which is the only part saying what to do about it. For a
            // screen-reader user that was the whole message gone. This is the exact call the
            // Close button makes.
            string spoken = OrderPlacement.Parse(code).RefusalAnnouncement("Close failed for BTC/USD.");

            Assert.StartsWith("Close failed for BTC/USD.", spoken, StringComparison.Ordinal);
            Assert.Contains(expected, spoken, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".", spoken);
        }

        /// <summary>
        /// There must be exactly ONE recogniser of an order result. The patch that fixed the
        /// dropped-reason bug added a private copy inside the dashboard, four lines away from an
        /// existing call to the shared one; a THIRD lived in the ticket as a
        /// <c>StartsWith</c> pair that missed <c>ORDER_DUPLICATE_SUPPRESSED</c> entirely, so a
        /// screen-reader double-Enter announced "Order placed" for an order the dedup gate had
        /// just refused to send.
        ///
        /// <para>
        /// The typed <see cref="OrderPlacement"/> makes a private copy impossible to write by
        /// accident — the component never sees a status string now — but it is still possible to
        /// write one on purpose, and this is where that would be noticed.
        /// </para>
        /// </summary>
        [Fact]
        public void TheDashboardCannotClassifyAnOrderResultItself()
        {
            string src = File.ReadAllText(Path.Combine(
                RepoPaths.RepoRoot(), "AccessibleTrader.BlazorClient.Components", "TradingDashboardModal.razor"));

            // Strip comments: this file EXPLAINS the old prefix test in prose, and a guard that
            // cannot tell an explanation from a call would fire on its own documentation.
            string code = string.Join("\n", src.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            Assert.DoesNotContain("StartsWith(\"ORDER_", code, StringComparison.Ordinal);
            Assert.DoesNotContain("StartsWith(\"PROVIDER_", code, StringComparison.Ordinal);
            Assert.Contains(".Succeeded", code, StringComparison.Ordinal);
            Assert.Contains("RefusalAnnouncement(", code, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("ORDER_FAILED")]          // a bare code with nothing after it
        [InlineData("ORDER_FAILED:")]
        [InlineData("")]
        public void AReasonlessCode_StillProducesASentence(string code)
        {
            // Never announce a dangling "Close failed for BTC/USD." with nothing following it.
            string spoken = OrderPlacement.Parse(code).RefusalAnnouncement("Close failed for BTC/USD.");

            Assert.False(string.IsNullOrWhiteSpace(spoken));
            Assert.EndsWith(".", spoken);
        }

        /// <summary>
        /// The outcome the old <c>StartsWith("ORDER_FAILED") || StartsWith("PROVIDER_NOT")</c>
        /// pair got WRONG, stated as the announcement a user would hear. A suppressed duplicate
        /// matched neither prefix, so the ticket announced "Order placed" for an order that was
        /// never sent — after the very key press the dedup gate exists to absorb.
        /// </summary>
        [Fact]
        public void ASuppressedDuplicate_IsNeverAnnouncedAsPlaced()
        {
            var placement = OrderPlacement.Parse("ORDER_DUPLICATE_SUPPRESSED");

            Assert.False(placement.Succeeded);
            Assert.Equal(OrderOutcome.Duplicate, placement.Outcome);
            Assert.Contains("duplicate", placement.RefusalAnnouncement("Order rejected for BTC/USD."),
                            StringComparison.OrdinalIgnoreCase);
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private (PaperTradingProvider Paper, MockWorkspaceStore Store, IDataService Data) Make()
        {
            var store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_dir);
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(Task.FromResult<IMarketDataProvider?>(null));
            data.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>())
                .Returns(Task.FromResult((new List<Ohlcv>(), new List<(long, double)>())));
            var paper = new PaperTradingProvider(store, paths,
                NullLogger<PaperTradingProvider>.Instance, null, data);
            return (paper, store, data);
        }

        /// <summary>Make a venue answer with a price for a symbol (0 = answers nothing).</summary>
        private static void Price(IDataService data, string provider, string symbol, double price)
        {
            var bars = price > 0
                ? new List<Ohlcv> { new(DateTime.UtcNow, price, price, price, price, 1) }
                : new List<Ohlcv>();
            data.FetchOhlcvAsync(provider, Arg.Is<MarketDataRequest>(r =>
                    string.Equals(r.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
                .Returns(Task.FromResult((bars, new List<(long, double)>())));
        }

        /// <summary>A provider whose canonical form collapses a Tether quote onto USD, as Bitstamp
        /// genuinely does — the behaviour that made one book look like two markets.</summary>
        private static IMarketDataProvider VenueThatRemapsUsdtToUsd(string name)
        {
            var p = Substitute.For<IMarketDataProvider>();
            p.Name.Returns(name);
            p.GetCanonicalSymbol(Arg.Any<string>()).Returns(ci =>
            {
                var s = (ci.Arg<string>() ?? "").Replace("/", "").Replace("-", "").ToUpperInvariant();
                if (s.EndsWith("USDT")) s = s[..^4] + "USD";
                return s;
            });
            return p;
        }

        private static WorkspaceState ChartOf(string provider, string symbol, double price) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", provider, symbol, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, price, price, price, price, 1)),
            };

        private static TradeSignal Buy(string symbol, double qty) =>
            new(Symbol: symbol, Side: OrderSide.Buy, Quantity: qty, Type: OrderType.Market);

        private static TradeSignal Sell(string symbol, double qty) =>
            new(Symbol: symbol, Side: OrderSide.Sell, Quantity: qty, Type: OrderType.Market);
    }
}
