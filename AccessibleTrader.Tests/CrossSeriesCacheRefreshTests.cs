using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A cross-series value keeps arriving after the first fetch. Before 2026-11-26 the cache
    /// fetched once per session, so an hourly series (Hyperliquid funding) stopped at the hour the
    /// chart opened. Now, once a timeframe has passed, a read starts a fetch of the newest page in
    /// the background and merges it in; the read itself never waits for it.
    /// </summary>
    public class CrossSeriesCacheRefreshTests
    {
        private static readonly CrossSeriesRequest Req = new("Derivatives", "Hyperliquid", "BTC-PERP_FUNDING", "1h", MaxPages: 1);
        private const long Hour = 3_600_000;
        private const long T0 = 1_795_687_200_000;

        private static Ohlcv Bar(long ms, double v) =>
            new(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, v, v, v, v, 0);

        private sealed class Rig
        {
            public long Now = T0 + 10 * Hour;
            public readonly IDataOrchestrator Orch = Substitute.For<IDataOrchestrator>();
            public readonly CrossSeriesCache Cache;
            public int Fetches;

            public Rig(params List<Ohlcv>[] replies)
            {
                var queue = new Queue<List<Ohlcv>>(replies);
                Orch.FetchOhlcvAsync(default!, default!, default!, default!, default, default, default, default, default)
                    .ReturnsForAnyArgs(_ => { Fetches++; return Task.FromResult(queue.Count > 0 ? queue.Dequeue() : new List<Ohlcv>()); });
                Cache = new CrossSeriesCache(Orch, () => Now);
            }

            public async Task<IReadOnlyList<(long Ts, double Value)>> SettledRead()
            {
                Cache.GetOrFetch(Req);
                for (int i = 0; i < 100 && Cache.RefreshInFlight(Req); i++) await Task.Delay(10);
                return Cache.GetOrFetch(Req);
            }
        }

        [Fact]
        public async Task After_a_timeframe_the_newest_page_is_fetched_and_merged()
        {
            var rig = new Rig(
                new() { Bar(T0, 1), Bar(T0 + Hour, 2) },
                new() { Bar(T0 + Hour, 2.5), Bar(T0 + 2 * Hour, 3) });
            Assert.Equal(2, rig.Cache.GetOrFetch(Req).Count);

            rig.Now += Hour;
            var after = await rig.SettledRead();

            Assert.Equal(new[] { T0, T0 + Hour, T0 + 2 * Hour }, after.Select(t => t.Ts));
            Assert.Equal(new[] { 1, 2.5, 3 }, after.Select(t => t.Value));   // the newer reading of an hour wins
            Assert.Equal(2, rig.Fetches);
        }

        [Fact]
        public async Task Within_a_timeframe_nothing_is_fetched_again()
        {
            var rig = new Rig(new() { Bar(T0, 1) }, new() { Bar(T0 + Hour, 2) });
            rig.Cache.GetOrFetch(Req);

            rig.Now += Hour - 1;
            var same = await rig.SettledRead();

            Assert.Single(same);
            Assert.Equal(1, rig.Fetches);
        }

        [Fact]
        public async Task A_failed_refresh_keeps_what_was_there_and_waits_another_timeframe()
        {
            var rig = new Rig(new List<Ohlcv> { Bar(T0, 1) });            // later fetches come back empty
            rig.Cache.GetOrFetch(Req);

            rig.Now += Hour;
            Assert.Single(await rig.SettledRead());
            Assert.Single(await rig.SettledRead());              // not asked again at once

            Assert.Equal(2, rig.Fetches);
        }
    }
}
