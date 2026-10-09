using System.Reflection;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Tradier's live candle starts on the period boundary, so it merges with the REST bars.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// The first trade tick after a subscription did <c>_lastCandleStart = now</c>, and every
    /// subsequent bucket rolled forward by exactly <c>interval</c> from that seed. There was no
    /// floor-to-period call anywhere — contrast <c>BarBucketConsolidator</c>, which is what
    /// every other provider's keyed feeds use.
    /// </para>
    ///
    /// <para>
    /// So a 5-minute Tradier subscription started at 10:03:47 emitted bars stamped 10:03:47,
    /// 10:08:47, 10:13:47 — none of which line up with the REST <c>timesales</c> bars at 10:00,
    /// 10:05, 10:10 that <c>FetchIntradayAsync</c> returns <i>from the same provider</i>. The
    /// live bar never merged with the historical buffer. It appended as a phantom bar at the
    /// wrong timestamp, and every indicator over that buffer recomputed across it.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// A real subscription is driven through the real SSE loop with a canned stream body, and
    /// the emitted bar's <c>Date</c> is checked against the period grid. The tick is fed at a
    /// deliberately off-grid wall-clock instant, because a tick that happens to arrive on the
    /// boundary cannot tell a floored seed from an unfloored one.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class TradierLiveBarAlignmentTests
    {
        private static void SwapBothClients(object provider, FakeHttpMessageHandler handler)
        {
            // Tradier holds two HttpClients: _httpClient for REST and _streamClient for the
            // long-lived SSE body. Both have to be faked or the subscription never starts.
            foreach (var f in provider.GetType()
                                      .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                      .Where(f => f.FieldType == typeof(HttpClient)))
            {
                f.SetValue(provider, new HttpClient(handler));
            }
        }

        /// <summary>
        /// The bar the app charts from Tradier's first live trade: the provider's tick folded
        /// through the same consolidator LiveStreamManager builds for the provider's declared
        /// tick style. Bucketing is the consolidator's job; a provider that does its own (and
        /// declares the wrong style) is what this file exists to catch.
        /// </summary>
        private static async Task<Ohlcv?> FirstLiveBarAsync(string timeframe)
        {
            var (ticks, style) = await LiveTicksAsync(timeframe,
                """{"type":"trade","symbol":"AAPL","price":101.5,"size":10}""" + "\n");
            if (ticks.Count == 0) return null;
            return new BarBucketConsolidator(timeframe, style).Apply(ticks[0]);
        }

        private static async Task<(List<Ohlcv> Ticks, AccessibleTrader.Sdk.Plugins.LiveTickStyle Style)> LiveTicksAsync(
            string timeframe, string body, int expected = 1)
        {
            var h = new FakeHttpMessageHandler()
                .Post(@"/markets/events/session", """{"stream":{"sessionid":"SID","url":"x"}}""")
                // The trade lines, then the body ends.
                .Post(@"stream\.tradier\.com|/markets/events", body);

            var p = new AccessibleTrader.Plugins.Tradier.TradierProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["AccessToken"] = "t",
                ["AccountId"] = "ACC1",
            });
            SwapBothClients(p, h);

            var ticks = new List<Ohlcv>();
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sub = p.LiveStream.Subscribe(bar =>
            {
                lock (ticks) { ticks.Add(bar); if (ticks.Count >= expected) seen.TrySetResult(); }
            });

            await p.SetSubscriptionAsync("Stock", "AAPL", timeframe);

            await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            await p.DisconnectAsync();

            lock (ticks) return (ticks.Take(expected).ToList(), p.LiveTickStyle);
        }

        [Fact]
        public async Task Each_trade_reaches_the_bar_once_with_its_own_size()
        {
            // Tradier emitted its own RUNNING candle on every trade while declaring TradeDeltas,
            // so the consolidator added the running total each time: trades of 10 and 5 charted
            // as 10 + 15 = 25. Two trades are two trades' worth of volume.
            var (ticks, style) = await LiveTicksAsync("1h",
                """{"type":"trade","symbol":"AAPL","price":101.5,"size":10,"date":"1791554400000"}""" + "\n"
                + """{"type":"trade","symbol":"AAPL","price":101.0,"size":5,"date":"1791554460000"}""" + "\n",
                expected: 2);

            Assert.Equal(2, ticks.Count);
            var consolidator = new BarBucketConsolidator("1h", style);
            consolidator.Apply(ticks[0]);
            var bar = consolidator.Apply(ticks[1])!.Value;

            Assert.Equal(15, bar.Volume);
            Assert.Equal(101.5, bar.Open);
            Assert.Equal(101.0, bar.Close);
            // Stamped with the exchange's trade time, not the moment it was read.
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1791554400000).UtcDateTime, ticks[0].Date);
        }

        [Theory]
        [InlineData("1m")]
        [InlineData("5m")]
        [InlineData("15m")]
        [InlineData("1h")]
        public async Task A_live_bar_is_stamped_on_the_period_grid_not_the_wall_clock(string timeframe)
        {
            var bar = await FirstLiveBarAsync(timeframe);

            Assert.NotNull(bar);
            var expected = TimeframeUtility.GetPeriodStart(bar!.Value.Date, timeframe);
            Assert.Equal(expected, bar.Value.Date);
        }

        [Fact]
        public async Task A_five_minute_bar_lands_on_a_five_minute_boundary_with_no_stray_seconds()
        {
            // Stated as the symptom rather than as the implementation: 10:03:47 was the shape
            // of the bug, and seconds-or-odd-minutes is exactly what a wall-clock seed leaves
            // behind. This holds regardless of when the test happens to run.
            var bar = await FirstLiveBarAsync("5m");

            Assert.NotNull(bar);
            Assert.Equal(0, bar!.Value.Date.Second);
            Assert.Equal(0, bar.Value.Date.Millisecond);
            Assert.Equal(0, bar.Value.Date.Minute % 5);
        }
    }
}
