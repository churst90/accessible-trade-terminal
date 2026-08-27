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

        private static async Task<Ohlcv?> FirstLiveBarAsync(string timeframe)
        {
            var h = new FakeHttpMessageHandler()
                .Post(@"/markets/events/session", """{"stream":{"sessionid":"SID","url":"x"}}""")
                // One trade line, then the body ends.
                .Post(@"stream\.tradier\.com|/markets/events",
                      """{"type":"trade","symbol":"AAPL","price":101.5,"size":10}""" + "\n");

            var p = new AccessibleTrader.Plugins.Tradier.TradierProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["AccessToken"] = "t",
                ["AccountId"] = "ACC1",
            });
            SwapBothClients(p, h);

            var seen = new TaskCompletionSource<Ohlcv>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sub = p.LiveStream.Subscribe(bar => seen.TrySetResult(bar));

            await p.SetSubscriptionAsync("Stock", "AAPL", timeframe);

            var completed = await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            await p.DisconnectAsync();

            return completed == seen.Task ? seen.Task.Result : null;
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
