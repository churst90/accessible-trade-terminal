using System.Net;
using System.Reflection;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A request for a date range gets that range, or is told why not.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>KrakenFuturesProvider</c> and <c>GeminiProvider</c> dropped <c>Since</c>/<c>Until</c>
    /// on the floor: both endpoints serve "the most recent window" and take no date range, and
    /// both providers then trimmed with <c>Skip(count - Limit)</c>, which keeps the NEWEST bars.
    /// So a chart scrolled back to 2019, or any date-ranged backtest, was quietly served this
    /// morning's data wearing 2019's request. Nothing was empty and nothing was flagged.
    /// </para>
    ///
    /// <para>
    /// The second half is the one a user notices. A window genuinely older than what the venue
    /// keeps still comes back empty, and it must SAY so, naming the dates that are available —
    /// the only fact that lets someone pick a window that works. A blank chart and a flat one
    /// are the same picture for a user who cannot see the axis.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class WindowedBarsTests
    {
        private static long Ms(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

        /// <summary>Ten daily bars, closes 0..9, ending on <paramref name="lastDay"/>.</summary>
        private static (List<Ohlcv>, List<(long, double)>) TenDaysEnding(DateTime lastDay)
        {
            var bars = Enumerable.Range(0, 10)
                .Select(i =>
                {
                    var d = lastDay.AddDays(-(9 - i));
                    return new Ohlcv(d, i, i, i, i, i);
                })
                .ToList();
            var vols = bars.Select(b => (new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), b.Volume)).ToList();
            return (bars, vols);
        }

        [Fact]
        public void Bars_outside_the_requested_window_are_dropped()
        {
            var last = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
            var (bars, vols) = TenDaysEnding(last);
            var request = new MarketDataRequest("Futures", "PI_XBTUSD", "1d", 500,
                Since: Ms(last.AddDays(-4)), Until: Ms(last.AddDays(-2)));
            var said = new List<string>();

            var (kept, keptVols) = WindowedBars.Apply(request, bars, vols, "Venue", "PI_XBTUSD", said.Add);

            // Days -4, -3, -2 — closes 5, 6, 7.
            Assert.Equal(new[] { 5.0, 6.0, 7.0 }, kept.Select(b => b.Close));
            Assert.Equal(kept.Count, keptVols.Count);
            Assert.Empty(said);
        }

        /// <summary>
        /// The defect itself: without a window filter this returns the newest bars, and the
        /// requested ones are nowhere in the result.
        /// </summary>
        [Fact]
        public void An_old_window_is_never_answered_with_the_newest_bars()
        {
            var last = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
            var (bars, vols) = TenDaysEnding(last);
            var request = new MarketDataRequest("Futures", "PI_XBTUSD", "1d", 3,
                Since: Ms(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Until: Ms(new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
            var said = new List<string>();

            var (kept, _) = WindowedBars.Apply(request, bars, vols, "Kraken Futures", "PI_XBTUSD", said.Add);

            Assert.Empty(kept);
            // …and it is SAID, naming the dates that do exist.
            Assert.Single(said);
            Assert.Contains("2026-08-11", said[0], StringComparison.Ordinal);
            Assert.Contains("2026-08-20", said[0], StringComparison.Ordinal);
            Assert.Contains("PI_XBTUSD", said[0], StringComparison.Ordinal);
        }

        [Fact]
        public void With_no_window_the_limit_still_keeps_the_newest_bars()
        {
            var last = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
            var (bars, vols) = TenDaysEnding(last);
            var request = new MarketDataRequest("Futures", "PI_XBTUSD", "1d", 3);
            var said = new List<string>();

            var (kept, keptVols) = WindowedBars.Apply(request, bars, vols, "Venue", "PI_XBTUSD", said.Add);

            Assert.Equal(new[] { 7.0, 8.0, 9.0 }, kept.Select(b => b.Close));
            Assert.Equal(kept.Count, keptVols.Count);
            Assert.Empty(said);
        }

        /// <summary>
        /// Bars and volumes are filtered as PAIRS. Filtering them independently is how a series
        /// and its volume overlay drift apart by one bar, which is a silently wrong chart rather
        /// than an empty one.
        /// </summary>
        [Fact]
        public void The_volume_series_stays_aligned_with_the_bars()
        {
            var last = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
            var (bars, vols) = TenDaysEnding(last);
            var request = new MarketDataRequest("Futures", "PI_XBTUSD", "1d", 2,
                Since: Ms(last.AddDays(-5)));
            var said = new List<string>();

            var (kept, keptVols) = WindowedBars.Apply(request, bars, vols, "Venue", "PI_XBTUSD", said.Add);

            Assert.Equal(kept.Count, keptVols.Count);
            for (int i = 0; i < kept.Count; i++)
            {
                Assert.Equal(new DateTimeOffset(kept[i].Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), keptVols[i].Timestamp);
                Assert.Equal(kept[i].Volume, keptVols[i].Volume);
            }
        }

        [Fact]
        public void An_empty_fetch_is_left_alone_and_not_reported_as_a_window_miss()
        {
            // A fetch that returned nothing is a fetch FAILURE and the caller has already said
            // so. Reporting it again as "no data for those dates" would be a second, wrong,
            // explanation for the same silence.
            var request = new MarketDataRequest("Futures", "PI_XBTUSD", "1d", 10, Since: Ms(DateTime.UtcNow.AddDays(-5)));
            var said = new List<string>();

            var (kept, _) = WindowedBars.Apply(request, new List<Ohlcv>(), new List<(long, double)>(), "Venue", "PI_XBTUSD", said.Add);

            Assert.Empty(kept);
            Assert.Empty(said);
        }
    }

    /// <summary>
    /// <b>Kraken Futures and Gemini actually route through the window filter.</b>
    ///
    /// <para>
    /// The unit tests above prove the helper is right. These prove the two providers use it —
    /// which is the half that was broken, and which a helper test cannot reach.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class SnapshotWindowRoutingTests
    {
        private static long Ms(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

        private static void SwapHttpClient(object provider, HttpMessageHandler handler)
        {
            foreach (var field in provider.GetType()
                         .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(f => f.FieldType == typeof(HttpClient)))
            {
                field.SetValue(provider, new HttpClient(handler));
            }
        }

        /// <summary>Recent daily candles in Kraken Futures' /api/charts/v1 shape.</summary>
        private static string KrakenFuturesCandles()
        {
            var rows = Enumerable.Range(0, 5).Select(i =>
            {
                long ms = Ms(DateTime.UtcNow.Date.AddDays(-(4 - i)));
                return $$"""{"time":{{ms}},"open":1,"high":2,"low":0.5,"close":1.5,"volume":10}""";
            });
            return $$"""{"candles":[{{string.Join(",", rows)}}]}""";
        }

        /// <summary>Recent daily candles in Gemini's /v2/candles shape — newest first.</summary>
        private static string GeminiCandles()
        {
            var rows = Enumerable.Range(0, 5).Select(i =>
            {
                long ms = Ms(DateTime.UtcNow.Date.AddDays(-i));
                return $"[{ms},1,2,0.5,1.5,10]";
            });
            return $"[{string.Join(",", rows)}]";
        }

        [Fact]
        public async Task Kraken_futures_refuses_a_window_older_than_its_history_and_says_so()
        {
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Get(".*charts/v1.*", KrakenFuturesCandles());
            var provider = new AccessibleTrader.Plugins.KrakenFutures.KrakenFuturesProvider();
            SwapHttpClient(provider, handler);
            var said = new List<string>();
            provider.ErrorStream.Subscribe(said.Add);

            var (bars, _) = await provider.FetchOhlcvAsync(new MarketDataRequest(
                "Futures", "pi_xbtusd", "1d", 100,
                Since: Ms(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Until: Ms(new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc))));

            Assert.Empty(bars);
            Assert.Contains(said, m => m.Contains("requested dates", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Gemini_refuses_a_window_older_than_its_history_and_says_so()
        {
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Get(".*candles.*", GeminiCandles());
            var provider = new AccessibleTrader.Plugins.Gemini.GeminiProvider();
            SwapHttpClient(provider, handler);
            var said = new List<string>();
            provider.ErrorStream.Subscribe(said.Add);

            var (bars, _) = await provider.FetchOhlcvAsync(new MarketDataRequest(
                "Crypto", "btcusd", "1d", 100,
                Since: Ms(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Until: Ms(new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc))));

            Assert.Empty(bars);
            Assert.Contains(said, m => m.Contains("requested dates", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Kraken_futures_still_serves_the_live_edge_when_no_window_is_asked_for()
        {
            // Vacuity check for the two tests above: without this, "return nothing at all"
            // would satisfy both of them.
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Get(".*charts/v1.*", KrakenFuturesCandles());
            var provider = new AccessibleTrader.Plugins.KrakenFutures.KrakenFuturesProvider();
            SwapHttpClient(provider, handler);
            var said = new List<string>();
            provider.ErrorStream.Subscribe(said.Add);

            var (bars, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest("Futures", "pi_xbtusd", "1d", 100));

            Assert.Equal(5, bars.Count);
            Assert.Empty(said);
        }
    }

    /// <summary>
    /// <b>CoinGecko's global breadth is a reading, not a history — and now refuses to pretend
    /// otherwise.</b>
    ///
    /// <para>
    /// <c>GLOBAL_TOTAL_CAP</c> / <c>GLOBAL_BTC_DOM</c> / <c>GLOBAL_ETH_DOM</c> ignored
    /// <c>Since</c>/<c>Until</c> and handed back today's reading whatever window was asked for,
    /// so a request for last March charted one dot dated today — and <c>DataService</c>'s
    /// analytics cache keys by the window, accumulating one entry per historical range, each
    /// holding a point it was never from. Unlike Etherscan's version of this there was never any
    /// look-ahead: the stamp is <c>UtcNow</c>, not today's midnight.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class CoinGeckoSnapshotHonestyTests
    {
        private const string GlobalBody =
            """{"data":{"total_market_cap":{"usd":2500000000000},"market_cap_percentage":{"btc":55.5,"eth":14.2}}}""";

        private static (AccessibleTrader.Plugins.CoinGecko.CoinGeckoProvider, FakeHttpMessageHandler) NewProvider()
        {
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Get(".*/global.*", GlobalBody);
            var provider = new AccessibleTrader.Plugins.CoinGecko.CoinGeckoProvider();
            var field = provider.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType == typeof(HttpClient));
            field.SetValue(provider, new HttpClient(handler));
            return (provider, handler);
        }

        private static long Ms(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

        [Fact]
        public async Task A_closed_window_is_refused_and_costs_no_http_call()
        {
            var (provider, handler) = NewProvider();
            var said = new List<string>();
            provider.ErrorStream.Subscribe(said.Add);

            var (bars, _) = await provider.FetchOhlcvAsync(new MarketDataRequest(
                "OnChain", "GLOBAL_BTC_DOM", "1d", 100,
                Since: Ms(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
                Until: Ms(new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc))));

            Assert.Empty(bars);
            Assert.Contains(said, m => m.Contains("current reading", StringComparison.OrdinalIgnoreCase));
            // The refusal must not spend the user's rate-limited quota discovering itself.
            Assert.Empty(handler.Captured);
        }

        [Fact]
        public async Task A_live_request_still_returns_the_current_reading()
        {
            // Vacuity check: without this, refusing everything would pass the test above.
            var (provider, _) = NewProvider();
            var said = new List<string>();
            provider.ErrorStream.Subscribe(said.Add);

            var (bars, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest("OnChain", "GLOBAL_BTC_DOM", "1d", 100));

            Assert.Single(bars);
            Assert.Equal(55.5, bars[0].Close);
            Assert.Empty(said);
        }
    }
}
