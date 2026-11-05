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
    /// A paper trade lives on the venue it was placed on.
    ///
    /// <para>
    /// <b>Found 2026-11-05</b> (multi-tab audit, after Cody: "I might have a tab with a trade on
    /// kraken, another tab with a trade on hyper liquid"). Paper mode sends every order to one
    /// simulated broker, keyed by symbol, which never learned which venue an order was for. With
    /// BTCUSDT bought on a Kraken tab and a Bitstamp BTCUSDT tab open, Bitstamp's bars filled the
    /// Kraken position's stop (the two venues print different lows), and a Bitstamp order merged
    /// into the Kraken position. One position per symbol stays the rule (separate accounts per
    /// venue is a question for Cody in TODO); what changes is that only its own venue moves it.
    /// </para>
    /// </summary>
    public sealed class PaperVenueTests : IDisposable
    {
        private readonly string _dir = TestTemp.NewDir("att-paper-venue-");
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ } }

        private static readonly ChartIdentity Kraken = new("Crypto", "Kraken", "BTCUSDT", "1h");
        private static readonly ChartIdentity Bitstamp = new("Crypto", "Bitstamp", "BTCUSDT", "1h");
        private static readonly DateTime Placed = DateTime.UtcNow;

        private PaperTradingProvider Make(out MockWorkspaceStore store, out List<OrderUpdate> updates)
        {
            store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_dir);
            var paper = new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance,
                                                 new SpyEventBus());
            var seen = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(u => { lock (seen) seen.Add(u); });
            updates = seen;
            return paper;
        }

        private static Ohlcv Bar(double low, double close, double minutesAgo = 30) =>
            new(Placed.AddMinutes(-minutesAgo), close, close + 50, low, close, 10);

        private static WorkspaceState Front(ChartIdentity id, double close, params TabSnapshot[] others) =>
            WorkspaceState.Initial with
            {
                Identity = id,
                Data = new TimeSeriesBuffer<Ohlcv>(Bar(close - 20, close)),
                TabSnapshots = System.Collections.Immutable.ImmutableList.Create(others),
            };

        private static TabSnapshot Behind(int index, ChartIdentity id, double close)
        {
            var s = WorkspaceState.Initial;
            return new TabSnapshot(
                TabIndex: index, Identity: id, Data: new TimeSeriesBuffer<Ohlcv>(Bar(close - 20, close)),
                ActiveSeries: s.ActiveSeries, FocusedSeriesIndex: s.FocusedSeriesIndex,
                FocusedSeriesId: s.FocusedSeriesId, FocusedComponentIndex: s.FocusedComponentIndex,
                FocusedBinIndex: s.FocusedBinIndex, CurrentDataIndex: s.CurrentDataIndex,
                ViewportStartIndex: s.ViewportStartIndex, ViewportLength: s.ViewportLength,
                RightMarginBars: s.RightMarginBars, ViewportRange: s.ViewportRange, PaneRanges: s.PaneRanges,
                IsHeikinAshi: false, IsLogScale: false, LastInteractionContext: s.LastInteractionContext,
                PaneHeightRatios: s.PaneHeightRatios, InitStatus: InitializationStatus.Ready,
                DataStatus: DataStatus.Ready, IsCoordinateEntryMode: false, PendingDrawingTool: null,
                CoordinateEntryAnchorCount: 0, CoordinateEntryAnchor1Index: -1, SymbolDisplayName: id.Symbol);
        }

        private static async Task<string> PlaceOn(PaperTradingProvider paper, string? venue, TradeSignal signal)
        {
            using (PaperOrderVenue.For(venue)) return await paper.PlaceOrderAsync(signal);
        }

        private static string Stream(ChartIdentity id) => IPaperTradingProvider.StreamOf(id);

        [Fact]
        public async Task Another_venues_bar_does_not_fire_the_stop_and_its_own_venues_does()
        {
            var paper = Make(out var store, out var updates);
            store.EmitState(Front(Kraken, 80_600));
            paper.ProcessBar("BTCUSDT", Bar(80_580, 80_600), Stream(Kraken));
            paper.ProcessBar("BTCUSDT", Bar(80_400, 80_450), Stream(Bitstamp));
            await PlaceOn(paper, "Kraken", new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0, StopLoss: 80_300));
            lock (updates) updates.Clear();

            // Bitstamp dips to 80,200; Kraken never goes below 80,550.
            paper.ProcessBar("BTCUSDT", Bar(80_200, 80_250, 1), Stream(Bitstamp));
            paper.ProcessBar("BTCUSDT", Bar(80_550, 80_600, 1), Stream(Kraken));

            lock (updates) Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);
            Assert.Equal(1.0, Assert.Single(await paper.GetPositionsAsync()).Quantity, 9);

            // Kraken reaching the stop still fires it.
            paper.ProcessBar("BTCUSDT", Bar(80_250, 80_260, 0), Stream(Kraken));
            lock (updates) Assert.True(Assert.Single(updates, u => u.Status == OrderStatus.Filled).StopTriggered);
        }

        [Fact]
        public async Task An_order_from_another_venues_chart_is_refused_not_merged()
        {
            var paper = Make(out var store, out _);
            store.EmitState(Front(Kraken, 80_600));
            await PlaceOn(paper, "Kraken", new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0));

            store.EmitState(Front(Bitstamp, 80_400, Behind(0, Kraken, 80_600)));
            var result = await PlaceOn(paper, "Bitstamp", new TradeSignal("BTCUSDT", OrderSide.Buy, 2.0));

            Assert.Equal("ORDER_FAILED:your paper BTCUSDT trade is on Kraken, and paper trading keeps one "
                       + "position per symbol, so this Bitstamp order was not placed. Trade it from the Kraken "
                       + "chart, or close it first", result);
            Assert.Equal(1.0, Assert.Single(await paper.GetPositionsAsync()).Quantity, 9);
        }

        [Fact]
        public async Task Its_own_venue_and_the_positions_screen_can_still_close_it()
        {
            var paper = Make(out var store, out _);
            store.EmitState(Front(Kraken, 80_600));
            await PlaceOn(paper, "Kraken", new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0));
            store.EmitState(Front(Bitstamp, 80_400, Behind(0, Kraken, 80_600)));

            // The Positions screen names the broker ("Paper"), not a chart: it is not a venue.
            var close = await PlaceOn(paper, "Paper", new TradeSignal("BTCUSDT", OrderSide.Sell, 0.5, ReduceOnly: true));
            Assert.DoesNotContain("ORDER_FAILED", close);
            var kraken = await PlaceOn(paper, "Kraken", new TradeSignal("BTCUSDT", OrderSide.Sell, 0.5, ReduceOnly: true));
            Assert.DoesNotContain("ORDER_FAILED", kraken);
            Assert.Empty(await paper.GetPositionsAsync());

            // Flat again, the symbol can be traded on any venue.
            var fresh = await PlaceOn(paper, "Bitstamp", new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0));
            Assert.DoesNotContain("ORDER_FAILED", fresh);
        }

        [Fact]
        public async Task An_order_for_a_tab_behind_is_priced_and_recorded_on_that_tabs_venue()
        {
            // A strategy bound to the Kraken tab fires while the Bitstamp tab is in front.
            var paper = Make(out var store, out var updates);
            store.EmitState(Front(Bitstamp, 80_400, Behind(0, Kraken, 80_600)));

            await PlaceOn(paper, "Kraken", new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0, StopLoss: 80_300));

            Assert.Equal(80_600, Assert.Single(await paper.GetPositionsAsync()).AveragePrice, 6);
            Assert.Equal(new[] { Kraken }, paper.ExposedIdentities());
            lock (updates) updates.Clear();
            paper.ProcessBar("BTCUSDT", Bar(80_200, 80_250, 0), Stream(Bitstamp));
            lock (updates) Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);
        }

        [Fact]
        public async Task A_close_from_another_venues_tab_closes_it_at_its_own_venues_price()
        {
            var paper = Make(out var store, out var updates);
            store.EmitState(Front(Kraken, 80_600));
            await PlaceOn(paper, "Kraken", new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0));
            store.EmitState(Front(Bitstamp, 80_400, Behind(0, Kraken, 80_700)));
            lock (updates) updates.Clear();

            var close = await PlaceOn(paper, "Bitstamp", new TradeSignal("BTCUSDT", OrderSide.Sell, 1.0, ReduceOnly: true));

            Assert.DoesNotContain("ORDER_FAILED", close);
            Assert.Empty(await paper.GetPositionsAsync());
            lock (updates) Assert.Equal(80_700, Assert.Single(updates, u => u.Status == OrderStatus.Filled).FilledPrice, 6);
        }

        [Fact]
        public async Task An_order_for_a_venue_with_no_open_tab_is_priced_from_that_venues_bars()
        {
            // A strategy whose Kraken tab was closed fires while a Bitstamp tab is in front.
            var paper = Make(out var store, out var updates);
            store.EmitState(Front(Bitstamp, 80_400));
            paper.ProcessBar("BTCUSDT", Bar(80_600, 80_650), Stream(Kraken));

            await PlaceOn(paper, "Kraken", new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0, StopLoss: 80_300));

            Assert.Equal(80_650, Assert.Single(await paper.GetPositionsAsync()).AveragePrice, 6);
            Assert.Equal("Kraken", Assert.Single(paper.ExposedIdentities()).Provider);
            lock (updates) updates.Clear();
            paper.ProcessBar("BTCUSDT", Bar(80_200, 80_250, 0), Stream(Bitstamp));
            lock (updates) Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);
        }

        [Fact]
        public async Task With_no_price_anywhere_the_orders_own_venue_is_asked()
        {
            var data = Substitute.For<IDataService>();
            data.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult((new List<Ohlcv> { Bar(0, ci.ArgAt<string>(0) == "Kraken" ? 80_700 : 80_400) },
                                                new List<(long Timestamp, double Volume)>())));
            var store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_dir);
            var paper = new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance,
                                                 new SpyEventBus(), data);
            store.EmitState(Front(Bitstamp, 80_400) with { Data = new TimeSeriesBuffer<Ohlcv>() });

            await PlaceOn(paper, "Kraken", new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0));

            Assert.Equal(80_700, Assert.Single(await paper.GetPositionsAsync()).AveragePrice, 6);
        }

        [Fact]
        public async Task An_order_naming_no_venue_behaves_as_before()
        {
            // Every older caller (and every older test) places with no venue: the chart in front.
            var paper = Make(out var store, out _);
            store.EmitState(Front(Bitstamp, 80_400));
            await paper.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 1.0));

            Assert.Equal(80_400, Assert.Single(await paper.GetPositionsAsync()).AveragePrice, 6);
            Assert.Equal(new[] { Bitstamp }, paper.ExposedIdentities());
        }

        [Fact]
        public async Task The_venue_scope_is_put_back_after_the_order()
        {
            Assert.Null(PaperOrderVenue.Current);
            using (PaperOrderVenue.For("Kraken"))
            {
                using (PaperOrderVenue.For("Bitstamp")) Assert.Equal("Bitstamp", PaperOrderVenue.Current);
                Assert.Equal("Kraken", PaperOrderVenue.Current);
                await Task.Yield();
                Assert.Equal("Kraken", PaperOrderVenue.Current);
            }
            Assert.Null(PaperOrderVenue.Current);
        }
    }
}
