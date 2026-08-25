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

        [Fact]
        public async Task NullFetchResult_ReturnsEmpty_NeverNull()
        {
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with { Identity = Focused });
            var data = Substitute.For<IDataService>();
            data.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>())
                .Returns(((List<Ohlcv>?)null, new List<(long, double)>())!);
            var feeds = new MarketFeeds(store, data);

            var bars = await feeds.GetBarsAsync(Other, 100);
            Assert.NotNull(bars);
            Assert.Empty(bars);
        }
    }
}
