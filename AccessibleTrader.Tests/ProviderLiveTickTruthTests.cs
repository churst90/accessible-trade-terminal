using System.Reflection;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// What each provider's live ticks MEAN, checked the way the app uses them: folded through
    /// the <see cref="BarBucketConsolidator"/> that <c>LiveStreamManager</c> builds for the
    /// provider's declared <see cref="LiveTickStyle"/>.
    ///
    /// <para>Found 2026-10-09 while chasing Cody's "the newest candle starts flat": five
    /// providers (Tradier, Oanda, Coinbase, Twelve Data, Finnhub) built a running candle of
    /// their own and emitted THAT on every tick, cleared on subscribe or seeded from whichever
    /// fetch ran last, while declaring a style that made the consolidator re-add its volume
    /// each tick. They now emit the tick itself. And two candle feeds stamped bars with the
    /// wrong end of the interval (Kraken's <c>timestamp</c>, Polygon's <c>e</c>).</para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderLiveTickTruthTests
    {
        private static void Invoke(object provider, string method, string json)
        {
            var m = provider.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance,
                        null, new[] { typeof(string) }, null)
                    ?? throw new InvalidOperationException($"{method}(string) not found on {provider.GetType().Name}.");
            m.Invoke(provider, new object[] { json });
        }

        private static List<Ohlcv> Capture(IMarketDataProvider p, Action action)
        {
            var seen = new List<Ohlcv>();
            using var sub = p.LiveStream.Subscribe(t => { lock (seen) seen.Add(t); });
            action();
            lock (seen) return seen.ToList();
        }

        private static Ohlcv Consolidate(IMarketDataProvider p, string timeframe, IEnumerable<Ohlcv> ticks)
        {
            var c = new BarBucketConsolidator(timeframe, p.LiveTickStyle);
            Ohlcv? bar = null;
            foreach (var t in ticks) bar = c.Apply(t) ?? bar;
            Assert.NotNull(bar);
            return bar!.Value;
        }

        // ── Twelve Data ──────────────────────────────────────────────────────────

        [Fact]
        public async Task TwelveData_price_ticks_after_a_fetch_do_not_re_add_the_fetched_volume()
        {
            // The running candle was seeded from the fetch — 1000 of volume — and re-emitted on
            // every price event under TradeDeltas, so two ticks charted 2000 of volume that
            // never traded. A price event is a price: it adds none.
            var h = new FakeHttpMessageHandler().Get(@"twelvedata\.com/time_series", """
                {"values":[{"datetime":"2026-10-09 00:00:00","open":"150","high":"152","low":"149","close":"151","volume":"1000"}]}
                """);
            var p = new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
            HttpClientSwap.ReplaceAll(p, h);
            await p.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1d", 10));
            // A daily chart's subscription, without opening a socket: the old running candle
            // rolled by this timeframe, so it must be the chart's for the seed to be reached.
            typeof(AccessibleTrader.Plugins.TwelveData.TwelveDataProvider)
                .GetField("_currentTimeframe", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(p, "1d");

            long ts = new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var ticks = Capture(p, () =>
            {
                Invoke(p, "HandleWebSocketMessage", $$"""{"event":"price","symbol":"AAPL","price":151.2,"timestamp":{{ts}}}""");
                Invoke(p, "HandleWebSocketMessage", $$"""{"event":"price","symbol":"AAPL","price":151.4,"timestamp":{{ts + 5}}}""");
            });

            Assert.Equal(2, ticks.Count);
            var bar = Consolidate(p, "1d", ticks);
            Assert.Equal(0, bar.Volume);
            Assert.Equal(151.4, bar.Close);
            Assert.Equal(new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), bar.Date);
        }

        // ── Coinbase ─────────────────────────────────────────────────────────────

        [Fact]
        public void Coinbase_ticker_emits_a_tick_without_needing_a_fetch_to_seed_a_candle()
        {
            // The ticker handler only emitted when a REST fetch had seeded its running candle —
            // any fetch, of any symbol — so before one there was no live price at all, and
            // after one of another symbol the live bar was that symbol's.
            var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            var ticks = Capture(p, () =>
            {
                Invoke(p, "HandleWebSocketMessage", """{"channel":"ticker","events":[{"tickers":[{"price":"62000.5"}]}]}""");
                Invoke(p, "HandleWebSocketMessage", """{"channel":"ticker","events":[{"tickers":[{"price":"61990.0"}]}]}""");
            });

            Assert.Equal(2, ticks.Count);
            var bar = Consolidate(p, "1w", ticks);
            Assert.Equal(62000.5, bar.Open);
            Assert.Equal(61990.0, bar.Close);
            Assert.Equal(0, bar.Volume);
            Assert.Equal(TimeframeUtility.GetPeriodStart(ticks[0].Date, "1w"), bar.Date);
        }

        // ── Oanda ────────────────────────────────────────────────────────────────

        [Fact]
        public void Oanda_price_line_is_one_mid_quote_at_its_own_time()
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(
                """{"type":"PRICE","time":"1791554400.123","bids":[{"price":"1.0850"}],"asks":[{"price":"1.0852"}]}""");

            Assert.True(AccessibleTrader.Plugins.Oanda.OandaProvider.TryParsePrice(json, DateTime.UtcNow, out var q));
            Assert.Equal(1.0851, q.Close, 6);
            Assert.Equal(q.Close, q.Open);
            Assert.Equal(0, q.Volume);
            Assert.Equal(new DateTime(2026, 10, 9, 14, 0, 0, DateTimeKind.Utc), q.Date.AddMilliseconds(-q.Date.Millisecond));
        }

        [Fact]
        public async Task Oanda_candles_are_requested_on_utc_days_and_monday_weeks()
        {
            // Oanda's defaults are a 17:00 New York day and a Friday week, which the live
            // bucket (00:00Z days, Monday weeks) cannot line up with.
            var h = new FakeHttpMessageHandler().Get(@"oanda\.com", """{"candles":[]}""");
            var p = new AccessibleTrader.Plugins.Oanda.OandaProvider();
            HttpClientSwap.ReplaceAll(p, h);
            p.Configure(new Dictionary<string, string> { ["AccessToken"] = "t", ["AccountId"] = "a" });

            await p.FetchOhlcvAsync(new MarketDataRequest("Forex", "EUR_USD", "1d", 10));

            var query = h.Captured.Single().RequestUri!.Query;
            Assert.Contains("dailyAlignment=0", query);
            Assert.Contains("alignmentTimezone=UTC", query);
            Assert.Contains("weeklyAlignment=Monday", query);
        }

        // ── Kraken ───────────────────────────────────────────────────────────────

        // Verbatim from wss://ws.kraken.com/v2, 2026-10-09 (5-minute BTC/USD update).
        private const string KrakenFiveMinuteFrame =
            """{"channel":"ohlc","type":"update","data":[{"symbol":"BTC/USD","open":82624.1,"high":82681.9,"low":82596.6,"close":82621.6,"trades":505,"volume":2.02938487,"vwap":82643.9,"interval_begin":"2026-10-09T17:40:00.000000000Z","interval":5,"timestamp":"2026-10-09T17:45:00.000000Z"}]}""";

        [Fact]
        public void Kraken_live_candle_is_stamped_at_interval_begin_not_its_end()
        {
            var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            var bars = Capture(p, () => Invoke(p, "HandlePublicMessage", KrakenFiveMinuteFrame));

            var bar = Assert.Single(bars);
            Assert.Equal(new DateTime(2026, 10, 9, 17, 40, 0, DateTimeKind.Utc), bar.Date);
            Assert.Equal(DateTimeKind.Utc, bar.Date.Kind);
        }

        [Fact]
        public void Kraken_candle_without_interval_begin_falls_back_to_timestamp_minus_interval()
        {
            var item = Newtonsoft.Json.Linq.JObject.Parse(
                """{"open":1,"high":2,"low":1,"close":2,"volume":3,"interval":5,"timestamp":"2026-10-09T17:45:00.000000Z"}""");
            Assert.True(AccessibleTrader.Plugins.Kraken.KrakenProvider.TryParseOhlcItem(item, out var bar));
            Assert.Equal(new DateTime(2026, 10, 9, 17, 40, 0, DateTimeKind.Utc), bar.Date);
        }

        [Fact]
        public void Kraken_weekly_is_built_from_daily_candles_into_monday_weeks()
        {
            // Kraken's own weekly candle begins on THURSDAY (live: interval_begin 2026-10-08).
            // The app's weeks begin on Monday, so weekly is resampled from daily and the live
            // weekly chart subscribes daily candles.
            var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            Assert.DoesNotContain("1w", p.NativelySupportedTimeframes);
            var map = typeof(AccessibleTrader.Plugins.Kraken.KrakenProvider)
                .GetMethod("MapWsInterval", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(1440, (int)map.Invoke(null, new object[] { "1w" })!);

            const string dailyFrame =
                """{"channel":"ohlc","type":"update","data":[{"symbol":"BTC/USD","open":83000.0,"high":83467.7,"low":80328.6,"close":82621.7,"trades":9000,"volume":1500.5,"vwap":82055.2,"interval_begin":"2026-10-09T00:00:00.000000000Z","interval":1440,"timestamp":"2026-10-10T00:00:00.000000Z"}]}""";
            var bars = Capture(p, () => Invoke(p, "HandlePublicMessage", dailyFrame));

            var week = Consolidate(p, "1w", bars);
            Assert.Equal(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), week.Date);
        }

        // ── Polygon ──────────────────────────────────────────────────────────────

        [Fact]
        public void Polygon_minute_aggregate_is_stamped_at_its_window_start()
        {
            // `s` is the window start (what REST bars' `t` is); `e` is its end. Stamping at `e`
            // put every live minute one bar late.
            long s = new DateTimeOffset(2026, 10, 9, 14, 5, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var p = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
            var bars = Capture(p, () => Invoke(p, "HandleWebSocketMessage",
                $$"""[{"ev":"AM","sym":"AAPL","o":228.1,"h":228.4,"l":228.0,"c":228.3,"v":12000,"s":{{s}},"e":{{s + 60_000}}}]"""));

            var bar = Assert.Single(bars);
            Assert.Equal(new DateTime(2026, 10, 9, 14, 5, 0, DateTimeKind.Utc), bar.Date);
        }

        // ── Bitstamp ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Bitstamp_throttle_folds_trades_together_instead_of_discarding_them()
        {
            // One tick per 250 ms is kept for the chart's sake, but the trades inside the
            // window used to be DROPPED — their size and any high or low between them gone.
            var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
            long ts = new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            string Trade(string price, string amount) =>
                $$$"""{"event":"trade","channel":"live_trades_btcusd","data":{"price":{{{price}}},"amount":{{{amount}}},"timestamp":"{{{ts}}}"}}""";

            var seen = new List<Ohlcv>();
            using var sub = p.LiveStream.Subscribe(t => { lock (seen) seen.Add(t); });
            Invoke(p, "HandleWebSocketMessage", Trade("62000", "0.5"));
            Invoke(p, "HandleWebSocketMessage", Trade("61800", "0.25"));
            Invoke(p, "HandleWebSocketMessage", Trade("62100", "0.125"));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (seen) if (seen.Sum(t => t.Volume) >= 0.875 - 1e-12) break;
                await Task.Delay(20);
            }

            List<Ohlcv> ticks;
            lock (seen) ticks = seen.ToList();
            var bar = Consolidate(p, "1h", ticks);
            Assert.Equal(0.875, bar.Volume, 9);
            Assert.Equal(61800, bar.Low);
            Assert.Equal(62100, bar.High);
            Assert.Equal(62100, bar.Close);
            Assert.Equal(62000, bar.Open);
        }
    }
}
