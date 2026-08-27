using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Whether the paper broker's simulation is honest about two things: WHICH book a
    /// bar belongs to, and WHEN a resting order became eligible to fill.
    ///
    /// <para>
    /// Both failures flatter the account, which is why they are grouped here. A ledger
    /// key the fill engine cannot see means the stop never fires and the short can never
    /// be liquidated — the position only ever gets better. A resting order tested
    /// against a whole bar it did not exist for means it fills at prices that had
    /// already gone — free money, minted by the simulator. A paper account that teaches
    /// either of these teaches the opposite of what a live venue does.
    /// </para>
    /// </summary>
    public sealed class PaperFillFidelityTests : IDisposable
    {
        private readonly string _dir = TestTemp.NewDir("att-paper-fidelity-");
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ } }

        private PaperTradingProvider Make(out MockWorkspaceStore store, IDataService? data = null,
                                          string? dir = null)
        {
            store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(dir ?? _dir);
            return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance,
                                            new SpyEventBus(), data);
        }

        private static WorkspaceState StateWith(string symbol, Ohlcv bar) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Venue", symbol, "1d"),
                Data = new TimeSeriesBuffer<Ohlcv>(bar),
            };

        private static Ohlcv Bar(double open, double high, double low, double close, DateTime? at = null) =>
            new(at ?? DateTime.UtcNow, open, high, low, close, 1000);

        // ── The ledger key the fill engine could not see ─────────────────────

        /// <summary>
        /// A position filed under one spelling is protected by bars arriving under
        /// another spelling of the same market.
        ///
        /// <para>
        /// The engine filtered resting orders with <c>string.Equals(o.Symbol, sym)</c>
        /// against the CHART's spelling, while the ledger keys everything under the
        /// existing position's. So the stop simply never fired.
        /// </para>
        /// </summary>
        [Fact]
        public async Task AStopOnASlashedSymbol_FiresOnBarsSpelledWithout()
        {
            var paper = Make(out var store);
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);

            store.EmitState(StateWith("BTC/USD", Bar(100, 101, 99, 100)));
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0, StopLoss: 95));
            updates.Clear();

            // The same market, spelled the way another venue's chart spells it.
            paper.ProcessBar("BTCUSD", Bar(99, 99, 90, 91));

            var fill = Assert.Single(updates, u => u.Status == OrderStatus.Filled);
            Assert.True(fill.StopTriggered);
            Assert.Empty(await paper.GetPositionsAsync());
        }

        /// <summary>
        /// The venue-taught alias — <c>BTC/USD</c> and <c>BTCUSDT</c> are one book on a
        /// venue that routes Tether quotes to its USD market — survives into the fill
        /// engine, which cannot ask a venue anything because it runs under a lock.
        ///
        /// <para>
        /// This is the case a separator-and-case comparison CANNOT catch, so it is what
        /// makes the persisted alias map earn its place.
        /// </para>
        /// </summary>
        [Fact]
        public async Task AVenueTaughtAlias_ReachesTheFillEngine()
        {
            var paper = Make(out var store, OneBookVenue());
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);

            store.EmitState(StateWith("BTC/USD", Bar(100, 101, 99, 100)));
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0));

            // Traded again by the other spelling: the ledger keeps BTC/USD (positions are
            // matched, never renamed), and the stop rides on that key.
            await paper.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0, StopLoss: 95));
            updates.Clear();

            paper.ProcessBar("BTCUSDT", Bar(99, 99, 90, 91));

            Assert.Contains(updates, u => u.Status == OrderStatus.Filled && u.StopTriggered);
        }

        /// <summary>
        /// A short filed under an aliased key can be liquidated. Before this it could
        /// not, at any price: <c>LiquidateIfCollateralExhausted</c> was handed the
        /// chart's spelling and found no position under it, so the one mechanism that
        /// stops a short running to infinity never ran.
        /// </summary>
        [Fact]
        public async Task AShortUnderAnAliasedKey_CanStillBeLiquidated()
        {
            var paper = Make(out var store, OneBookVenue());
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);

            store.EmitState(StateWith("BTC/USD", Bar(100, 101, 99, 100)));
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Sell, 1.0));
            // The alias is taught by a trade under the other spelling.
            await paper.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Sell, 0.0001));
            updates.Clear();

            // Collateral is proceeds plus 1x margin, so liquidation sits near twice entry.
            paper.ProcessBar("BTCUSDT", Bar(100, 400, 100, 400));

            Assert.Contains(updates, u => u.Status == OrderStatus.Filled
                                       && (u.Reason ?? "").Contains("LIQUIDATED", StringComparison.Ordinal));
        }

        /// <summary>
        /// A closed position's alias must not capture a later, unrelated trade under the
        /// same spelling. The alias is a pointer at live exposure, re-checked on every
        /// read — not a permanent rename.
        /// </summary>
        [Fact]
        public async Task AnAliasDies_WithTheExposureItPointedAt()
        {
            var paper = Make(out var store, OneBookVenue());

            store.EmitState(StateWith("BTC/USD", Bar(100, 101, 99, 100)));
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0));
            await paper.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0));   // teaches the alias
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Sell, 2.0, ReduceOnly: true));
            Assert.Empty(await paper.GetPositionsAsync());

            // A fresh trade by the aliased spelling is its own position now. Note the bar
            // has to be priced under BTCUSDT for the order to fill at all, which is the
            // same claim from the other side: the dead alias no longer routes it.
            paper.ProcessBar("BTCUSDT", Bar(100, 101, 99, 100));
            await paper.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal("BTCUSDT", pos.Symbol);
        }

        // ── Price action that predates the order ─────────────────────────────

        /// <summary>
        /// A buy limit at 99, typed while the market is 105 on a day that already printed
        /// 99 six hours ago, does not fill against that low.
        ///
        /// <para>
        /// The engine is driven by the newest, still-forming bar and orders carried no
        /// placement time, so <c>Crossed</c> tested the whole bar's accumulated extremes
        /// — including price action from before the order existed. On the 4h and 1d
        /// charts the demo exposes, that is free money on the very next tick.
        /// </para>
        /// </summary>
        [Fact]
        public async Task ALimitDoesNotFill_OnALowTheBarPrintedBeforeItWasPlaced()
        {
            var paper = Make(out var store);
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);

            // The day so far: opened at 100, dipped to 99 hours ago, now trading 105.
            var formingBar = Bar(100, 106, 99, 105);
            store.EmitState(StateWith("BTC/USD", formingBar));

            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0, OrderType.Limit, Price: 99));

            // The same bar ticks again, one cent higher. The 99 is still in its low.
            store.EmitState(StateWith("BTC/USD", formingBar with { Close = 105.01, High = 106 }));

            Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);
            Assert.Empty(await paper.GetPositionsAsync());
        }

        /// <summary>
        /// The complement, and the reason the test above is not merely "limits never
        /// fill": price coming back to the level AFTER placement fills exactly as before.
        /// </summary>
        [Fact]
        public async Task ALimitStillFills_WhenPriceComesBackToItAfterwards()
        {
            var paper = Make(out var store);
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);

            var formingBar = Bar(100, 106, 99, 105);
            store.EmitState(StateWith("BTC/USD", formingBar));
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0, OrderType.Limit, Price: 99));

            // A NEW low, made after the order was placed — real price action.
            store.EmitState(StateWith("BTC/USD", formingBar with { Low = 98, Close = 98.5 }));

            Assert.Contains(updates, u => u.Status == OrderStatus.Filled);
        }

        /// <summary>
        /// A bar that OPENED after the order was placed counts whole. This is the common
        /// case and the fast path, and without it every fill would be delayed by a bar.
        /// </summary>
        [Fact]
        public async Task ALimitFillsOnTheWholeOfABar_ThatOpenedAfterItWasPlaced()
        {
            var paper = Make(out var store);
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);

            store.EmitState(StateWith("BTC/USD", Bar(105, 106, 104, 105)));
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0, OrderType.Limit, Price: 99));

            // A later bar: its low is the whole of it, because none of it predates the order.
            store.EmitState(StateWith("BTC/USD", Bar(104, 104, 97, 103, DateTime.UtcNow.AddHours(1))));

            Assert.Contains(updates, u => u.Status == OrderStatus.Filled);
        }

        /// <summary>
        /// The gap reference follows the same rule. <see cref="AccessibleTrader.Core.Services.Trading.BarFill"/>
        /// fills at the bar's OPEN when the market was already through the level there —
        /// but for an order placed part-way through, that open is a price the order was
        /// never live at, and handing it over gives away the whole gap.
        /// </summary>
        [Fact]
        public async Task AGapFill_IsPricedFromWhereTheMarketWasWhenTheOrderWentLive()
        {
            var paper = Make(out var store);
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);

            // The bar opened at 90 — below a 99 buy limit — and has since traded up to 105.
            var formingBar = Bar(90, 106, 90, 105);
            store.EmitState(StateWith("BTC/USD", formingBar));
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0, OrderType.Limit, Price: 99));

            // Price falls back through the limit after placement.
            store.EmitState(StateWith("BTC/USD", formingBar with { Low = 89, Close = 89 }));

            var fill = Assert.Single(updates, u => u.Status == OrderStatus.Filled);
            Assert.Equal(99, fill.FilledPrice);   // NOT 90: the order never existed at the open
        }

        /// <summary>
        /// A trailing stop anchors on the extreme it actually rode, not on one the bar
        /// printed before it was attached — the same root cause, in the place where it
        /// silently widens or tightens a stop the user never chose.
        /// </summary>
        [Fact]
        public async Task ATrailingStopAnchors_OnlyOnPriceActionAfterItWasPlaced()
        {
            var paper = Make(out var store);

            // The day already spiked to 140 before this trade existed.
            var formingBar = Bar(100, 140, 99, 100);
            store.EmitState(StateWith("BTC/USD", formingBar));
            await paper.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0,
                TrailStopMode: TrailMode.Amount, TrailStopValue: 10));

            // Anchored at 140, the trigger would be 130 and this bar's close of 100 would
            // stop the trade out on the spot. Anchored honestly it is 90, and nothing happens.
            store.EmitState(StateWith("BTC/USD", formingBar with { Close = 100 }));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(1.0, pos.Quantity, 9);
        }

        /// <summary>
        /// The stamp survives a restart. Persisting the order but not WHEN it was placed
        /// would hand every restored order the whole of the current bar to fill against,
        /// which is the exploit re-opened once a day by whoever closes the app.
        /// </summary>
        [Fact]
        public async Task ThePlacementStampSurvives_ARestart()
        {
            var dir = TestTemp.NewDir("att-paper-restart-");
            try
            {
                var formingBar = Bar(100, 106, 99, 105);
                var first = Make(out var storeA, dir: dir);
                storeA.EmitState(StateWith("BTC/USD", formingBar));
                await first.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 1.0, OrderType.Limit, Price: 99));
                first.DisposeAccount();

                var second = Make(out var storeB, dir: dir);
                var updates = new List<OrderUpdate>();
                second.OrderUpdateStream.Subscribe(updates.Add);

                // The same bar, still forming, still carrying its pre-placement low of 99.
                storeB.EmitState(StateWith("BTC/USD", formingBar with { Close = 105.5 }));

                Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);
                second.DisposeAccount();
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        /// <summary>
        /// A venue that routes both spellings to one book, which is what makes the ledger
        /// file the second trade under the first's key.
        /// </summary>
        private static IDataService OneBookVenue()
        {
            var provider = Substitute.For<IMarketDataProvider>();
            provider.GetCanonicalSymbol(Arg.Any<string>()).Returns("BTCUSD");
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(Task.FromResult<IMarketDataProvider?>(provider));
            return data;
        }
    }
}
