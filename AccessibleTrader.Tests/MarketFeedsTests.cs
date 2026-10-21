using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// IMarketFeeds — the data-access seam (debt item 7). Contract: the focused
    /// identity answers from the live store with NO provider call; any other
    /// identity fetches through the provider path. When the keyed-feed refactor
    /// eventually lands behind this interface, these tests keep the contract.
    /// </summary>
    public class MarketFeedsTests
    {
        private static readonly ChartIdentity Focused = new("Spot", "Bitstamp", "BTC/USD", "1h");
        private static readonly ChartIdentity Other   = new("Spot", "Bitstamp", "ETH/USD", "1h");

        private static List<Ohlcv> Bars(int n) => Enumerable.Range(0, n)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
                100 + i, 101 + i, 99 + i, 100 + i, 1000))
            .ToList();

        private static (MarketFeeds feeds, IDataService data) Build(int storeBars = 50)
        {
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with
            {
                Identity = Focused,
                Data = new TimeSeriesBuffer<Ohlcv>(Bars(storeBars)),
            });
            var data = Substitute.For<IDataService>();
            data.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>())
                .Returns((Bars(10), new List<(long, double)>()));
            return (new MarketFeeds(store, data), data);
        }

        [Fact]
        public async Task FocusedIdentity_AnswersFromTheStore_NoProviderCall()
        {
            var (feeds, data) = Build(storeBars: 50);

            Assert.True(feeds.IsLive(Focused));
            var bars = await feeds.GetBarsAsync(Focused, 400);

            Assert.Equal(50, bars.Count);
            await data.DidNotReceive().FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>());
        }

        [Fact]
        public async Task FocusedIdentity_TruncatesToMostRecentBars()
        {
            var (feeds, _) = Build(storeBars: 50);
            var bars = await feeds.GetBarsAsync(Focused, 10);
            Assert.Equal(10, bars.Count);
            // Newest last, and it must be the newest 10 — not the oldest.
            Assert.Equal(149, bars[^1].Close); // bar 49: close = 100 + 49
        }

        [Fact]
        public async Task OtherIdentity_FetchesThroughTheProvider()
        {
            var (feeds, data) = Build();

            Assert.False(feeds.IsLive(Other));
            var bars = await feeds.GetBarsAsync(Other, 400);

            Assert.Equal(10, bars.Count);
            await data.Received(1).FetchOhlcvAsync("Bitstamp",
                Arg.Is<MarketDataRequest>(r => r.Symbol == "ETH/USD" && r.Timeframe == "1h"));
        }

        /// <summary>
        /// A weekly background tab on a provider with no weekly bars. Bitstamp's steps stop at 3
        /// days; the raw fetch sent step=604800 and got a 400 each time the background monitor
        /// started (Camel cycles workspace, 2026-10-21). Through the fetcher it asks for days and
        /// builds the weeks, as the chart does.
        /// </summary>
        [Fact]
        public async Task OtherIdentity_OnATimeframeTheProviderLacks_AsksForTheBaseAndResamples()
        {
            var weekly = new ChartIdentity("Crypto|Spot", "Bitstamp", "BTCUSDT", "1w");
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with { Identity = Focused });

            var provider = Substitute.For<AccessibleTrader.Sdk.Plugins.IMarketDataProvider>();
            provider.NativelySupportedTimeframes.Returns(new List<string> { "1m", "1h", "1d", "3d" });
            provider.MaxBarsPerRequest.Returns(1000);

            // 70 daily bars from a Monday: ten whole weeks.
            var days = Enumerable.Range(0, 70)
                .Select(i => new Ohlcv(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                    100 + i, 101 + i, 99 + i, 100 + i, 1000))
                .ToList();
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync("Bitstamp").Returns(provider);
            data.FetchOhlcvAsync("Bitstamp", Arg.Any<MarketDataRequest>(), Arg.Any<CancellationToken>())
                .Returns(ci => ((MarketDataRequest)ci[1]).Timeframe == "1d"
                    ? (days, new List<(long, double)>())
                    : throw new HttpRequestException("400 BadRequest"));

            var fetcher = new HistoricalDataFetcher(data, new ResamplerService(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<HistoricalDataFetcher>.Instance);
            var feeds = new MarketFeeds(store, data, hub: null, fetcher: fetcher);

            var bars = await feeds.GetBarsAsync(weekly, 400);

            await data.DidNotReceive().FetchOhlcvAsync("Bitstamp",
                Arg.Is<MarketDataRequest>(r => r.Timeframe == "1w"), Arg.Any<CancellationToken>());
            Assert.Equal(10, bars.Count);
            Assert.All(bars.Zip(bars.Skip(1)), p => Assert.Equal(TimeSpan.FromDays(7), p.Second.Date - p.First.Date));
        }

        [Fact]
        public async Task NullFetchResult_ReturnsEmpty_NeverNull()
        {
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with { Identity = Focused });
            var data = Substitute.For<IDataService>();
            data.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>())
                .Returns((null!, new List<(long, double)>()));
            var feeds = new MarketFeeds(store, data);

            var bars = await feeds.GetBarsAsync(Other, 100);
            Assert.NotNull(bars);
            Assert.Empty(bars);
        }
    }
}
