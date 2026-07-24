using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Live-stream parse tests. Each provider's WebSocket parse path is a private
    /// HandleWebSocketMessage(string json) method registered with the
    /// <c>ReconnectingWebSocket.OnMessage</c> callback. Mocking the entire
    /// <see cref="System.Net.WebSockets.ClientWebSocket"/> would mean dropping in
    /// a fake socket and pumping bytes — tractable but a lot of scaffolding for
    /// a test that just wants to know "does the parser turn JSON X into Ohlcv Y".
    ///
    /// Instead these tests reflect into the private parse method directly and
    /// invoke it with synthetic frames, then assert on the public IObservable
    /// streams (<see cref="BaseMarketDataProvider.LiveStream"/>,
    /// <c>OrderUpdateStream</c>, <c>SubscribeOrderBook</c>). This catches the
    /// bug class users hit on live streams: malformed frames, channel routing
    /// errors, missing field crashes, and silent-no-op frames that should still
    /// be observable.
    /// </summary>
    public class ProviderLiveStreamTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Reflects a private HandleWebSocketMessage(string)-shaped method on the
        /// provider and invokes it with the supplied JSON. Any name beginning
        /// with "Handle" works (HandleWebSocketMessage, HandleMessage, etc.).
        /// </summary>
        private static void DispatchFrame(object provider, string json)
        {
            var t = provider.GetType();
            var method = t.GetMethod("HandleWebSocketMessage",
                            BindingFlags.NonPublic | BindingFlags.Instance,
                            null, new[] { typeof(string) }, null)
                       ?? t.GetMethod("HandleMessage",
                            BindingFlags.NonPublic | BindingFlags.Instance,
                            null, new[] { typeof(string) }, null);
            if (method == null)
                throw new InvalidOperationException($"No HandleWebSocketMessage / HandleMessage(string) on {t.Name}.");
            method.Invoke(provider, new object[] { json });
        }

        /// <summary>
        /// Subscribes to an IObservable for the duration of <paramref name="action"/>
        /// and returns the captured emissions.
        /// </summary>
        private static List<T> Capture<T>(IObservable<T> source, Action action)
        {
            var captured = new List<T>();
            using var sub = source.Subscribe(captured.Add);
            action();
            return captured;
        }

        // ── Bitstamp ──────────────────────────────────────────────────────────
        // {"event":"trade","data":{"price":..,"amount":..,"timestamp":"unix_seconds"}}
        // throttled at 250ms — first frame after construction goes through.

        public class Bitstamp
        {
            [Fact]
            public void TradeFrame_EmitsOhlcvOnLiveStream()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var frame = "{\"event\":\"trade\",\"data\":{\"price\":50000.0,\"amount\":0.1,\"timestamp\":\"" + ts + "\"}}";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal(50000.0, emitted[0].Close);
                Assert.Equal(0.1, emitted[0].Volume);
            }

            [Fact]
            public void TradeFrame_WithZeroPrice_DoesNotEmit()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var frame = "{\"event\":\"trade\",\"data\":{\"price\":0,\"amount\":0.1,\"timestamp\":\"" + ts + "\"}}";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Empty(emitted);
            }

            [Fact]
            public void TradeFrame_WithPreBitcoinTimestamp_Discarded()
            {
                // Sentinel value (< 2009-01-01) should never produce a bar.
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                var frame = """{"event":"trade","data":{"price":50000,"amount":0.1,"timestamp":"0"}}""";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Empty(emitted);
            }

            [Fact]
            public void OrderBookDiffFrame_EmitsOnSubscribeOrderBook()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                var frame = """
                    {"event":"data","channel":"diff_order_book_btcusd","data":{
                      "bids":[["49999","0.5"]],
                      "asks":[["50001","0.6"]]
                    }}
                    """;

                var emitted = Capture(p.SubscribeOrderBook("BTC/USD"),
                    () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal("BTCUSD", emitted[0].Symbol);
                Assert.Single(emitted[0].Bids);
                Assert.Single(emitted[0].Asks);
                Assert.Equal(49999.0, emitted[0].Bids[0].Price);
                Assert.Equal(50001.0, emitted[0].Asks[0].Price);
            }

            [Fact]
            public void MalformedFrame_DoesNotThrow_AndDoesNotEmit()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, "garbage"));
                Assert.Empty(emitted);
            }

            [Fact]
            public void UnknownEventChannel_NoEmissions()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                var emittedLive = Capture(p.LiveStream,
                    () => DispatchFrame(p, """{"event":"heartbeat","data":{}}"""));
                Assert.Empty(emittedLive);
            }

            // ── Private order events (private-my_orders_{pair}) ─────────────────
            // Regression guard: the channel is joined to the pair with an UNDERSCORE
            // (private-my_orders_btcusd). The old code used a hyphen, so the private
            // order stream never matched and no fill was ever announced.

            private const string Created =
                """{"event":"order_created","channel":"private-my_orders_btcusd","data":{"id":42,"amount":"0.50","price":"50000","order_type":0,"microtimestamp":"1700000000000000"}}""";

            [Fact]
            public void OrderCreated_RegistersBaseline_ButDoesNotAnnounce()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                var seen = Capture(p.OrderUpdateStream, () => DispatchFrame(p, Created));
                Assert.Empty(seen); // no "working" status in the enum → silent baseline
            }

            [Fact]
            public void PartialFill_ReportsFilledDelta_RemainingAndSide()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                // remaining drops 0.50 → 0.30 → a 0.20 partial fill.
                const string changed =
                    """{"event":"order_changed","channel":"private-my_orders_btcusd","data":{"id":42,"amount":"0.30","price":"50000","order_type":0,"microtimestamp":"1700000000000000"}}""";

                var seen = Capture(p.OrderUpdateStream, () =>
                {
                    DispatchFrame(p, Created);
                    DispatchFrame(p, changed);
                });

                var u = Assert.Single(seen);
                Assert.Equal("42", u.OrderId);
                Assert.Equal("BTCUSD", u.Symbol);
                Assert.Equal(OrderSide.Buy, u.Side);
                Assert.Equal(OrderStatus.PartialFill, u.Status);
                Assert.Equal(0.20, u.FilledQuantity, 8);   // this fill, not the running total
                Assert.Equal(0.30, u.RemainingQuantity, 8);
            }

            [Fact]
            public void OrderDeleted_WithZeroRemaining_IsFilled()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                const string deleted =
                    """{"event":"order_deleted","channel":"private-my_orders_btcusd","data":{"id":42,"amount":"0","price":"50000","order_type":0,"microtimestamp":"1700000000000000"}}""";

                var seen = Capture(p.OrderUpdateStream, () =>
                {
                    DispatchFrame(p, Created);
                    DispatchFrame(p, deleted);
                });

                var u = Assert.Single(seen);
                Assert.Equal(OrderStatus.Filled, u.Status);
                Assert.Equal(0.50, u.FilledQuantity, 8);
                Assert.Equal(0.0, u.RemainingQuantity, 8);
            }

            [Fact]
            public void OrderDeleted_WithRemaining_IsCancelled_NotFilled()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                // A user cancel leaves an unfilled remainder — must NOT announce Filled.
                const string deleted =
                    """{"event":"order_deleted","channel":"private-my_orders_btcusd","data":{"id":42,"amount":"0.50","price":"50000","order_type":0,"microtimestamp":"1700000000000000"}}""";

                var seen = Capture(p.OrderUpdateStream, () =>
                {
                    DispatchFrame(p, Created);
                    DispatchFrame(p, deleted);
                });

                Assert.Equal(OrderStatus.Cancelled, Assert.Single(seen).Status);
            }

            [Fact]
            public void SellOrder_MapsSideFromOrderType1()
            {
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                const string sellDeleted =
                    """{"event":"order_deleted","channel":"private-my_orders_btcusd","data":{"id":7,"amount":"0","price":"50000","order_type":1,"microtimestamp":"1700000000000000"}}""";

                var seen = Capture(p.OrderUpdateStream, () => DispatchFrame(p, sellDeleted));
                Assert.Equal(OrderSide.Sell, Assert.Single(seen).Side);
            }

            [Fact]
            public void HyphenChannel_DoesNotMatch_NoEmission()
            {
                // Pins the exact bug: private-my_orders-btcusd (hyphen) is NOT a real
                // Bitstamp channel and must never be treated as an order event.
                var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
                const string hyphen =
                    """{"event":"order_deleted","channel":"private-my_orders-btcusd","data":{"id":1,"amount":"0","price":"50000","order_type":0}}""";

                var seen = Capture(p.OrderUpdateStream, () => DispatchFrame(p, hyphen));
                Assert.Empty(seen);
            }
        }

        // ── Coinbase ──────────────────────────────────────────────────────────
        // Channel routing: ticker / l2_data | level2 / user — each goes to a
        // different observable. Tests cover the routing path independently from
        // the candle-aggregation logic (which depends on _lastCandle state set
        // by an earlier FetchOhlcv call).

        public class Coinbase
        {
            [Fact]
            public void L2DataFrame_EmitsOrderBookUpdate()
            {
                var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
                var frame = """
                    {"channel":"l2_data","events":[
                      {"product_id":"BTC-USD","updates":[
                        {"side":"bid","price_level":"49999","new_quantity":"0.5"},
                        {"side":"offer","price_level":"50001","new_quantity":"0.6"}
                      ]}
                    ]}
                    """;

                var emitted = Capture(p.SubscribeOrderBook("BTC-USD"),
                    () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal("BTC-USD", emitted[0].Symbol);
                Assert.Single(emitted[0].Bids);
                Assert.Single(emitted[0].Asks);
            }

            [Fact]
            public void Level2AliasChannel_AlsoRoutesToOrderBook()
            {
                // Coinbase's docs and live API have flipped between "l2_data" and
                // "level2" over time. The provider accepts both; this pins it.
                var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
                var frame = """
                    {"channel":"level2","events":[
                      {"product_id":"ETH-USD","updates":[
                        {"side":"bid","price_level":"3000","new_quantity":"1.0"}
                      ]}
                    ]}
                    """;

                var emitted = Capture(p.SubscribeOrderBook("ETH-USD"),
                    () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal("ETH-USD", emitted[0].Symbol);
            }

            [Fact]
            public void EmptyUpdates_DoesNotEmit()
            {
                // An events frame with empty updates array is a no-op — never
                // emit a zero-bid/zero-ask snapshot, which would mislead the UI.
                var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
                var frame = """
                    {"channel":"l2_data","events":[
                      {"product_id":"BTC-USD","updates":[]}
                    ]}
                    """;

                var emitted = Capture(p.SubscribeOrderBook("BTC-USD"),
                    () => DispatchFrame(p, frame));

                Assert.Empty(emitted);
            }

            [Fact]
            public void MalformedFrame_DoesNotThrow()
            {
                var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, "garbage"));
                Assert.Empty(emitted);
            }

            [Fact]
            public void UnknownChannel_DoesNotEmit()
            {
                var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
                var live = Capture(p.LiveStream,
                    () => DispatchFrame(p, """{"channel":"heartbeats"}"""));
                Assert.Empty(live);
            }
        }

        // ── Polygon ───────────────────────────────────────────────────────────
        // Polygon multiplexes equity (AM), crypto (XA), and forex (CA) bars in
        // the same JSON-array stream. Each bar has o/h/l/c/v + e (end-time ms).
        // All-zero bars dropped.

        public class Polygon
        {
            [Fact]
            public void EquityAggregateMinute_EmitsOhlcv()
            {
                var p = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
                var frame = """[{"ev":"AM","sym":"AAPL","o":150.0,"h":151.0,"l":149.5,"c":150.5,"v":1234,"e":1700000000000}]""";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal(150.0, emitted[0].Open);
                Assert.Equal(150.5, emitted[0].Close);
                Assert.Equal(1234, emitted[0].Volume);
            }

            [Fact]
            public void CryptoAggregate_EmitsOhlcv()
            {
                var p = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
                var frame = """[{"ev":"XA","pair":"BTC-USD","o":50000,"h":50100,"l":49900,"c":50050,"v":2.5,"e":1700000000000}]""";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal(50050, emitted[0].Close);
            }

            [Fact]
            public void AllZeroBar_Dropped()
            {
                var p = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
                var frame = """[{"ev":"AM","o":0,"h":0,"l":0,"c":0,"v":0,"e":1700000000000}]""";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));
                Assert.Empty(emitted);
            }

            [Fact]
            public void MalformedFrame_DoesNotThrow()
            {
                var p = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, "not-json"));
                Assert.Empty(emitted);
            }

            [Fact]
            public void MultipleBars_InOneFrame_AllEmitted()
            {
                // Polygon batches multiple ticks per frame on busy streams. Each
                // entry must surface independently.
                var p = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
                var frame = "[" +
                    "{\"ev\":\"AM\",\"o\":1,\"h\":1,\"l\":1,\"c\":1,\"v\":1,\"e\":1700000000000}," +
                    "{\"ev\":\"AM\",\"o\":2,\"h\":2,\"l\":2,\"c\":2,\"v\":2,\"e\":1700000060000}" +
                "]";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Equal(2, emitted.Count);
            }
        }

        // ── Kraken — has separate public + auth handlers ─────────────────────
        // Public channel "ohlc" → LiveStream Ohlcv;
        // Public channel "book" → SubscribeOrderBook with bid/ask round-trip.
        // Reflection target for these tests is HandlePublicMessage.

        public class Kraken
        {
            // Override DispatchFrame because Kraken doesn't have a method named
            // "HandleWebSocketMessage" — it has HandlePublicMessage / HandleAuthMessage.
            private static void DispatchPublic(object provider, string json)
            {
                var method = provider.GetType().GetMethod("HandlePublicMessage",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(string) }, null)
                    ?? throw new InvalidOperationException("HandlePublicMessage not found.");
                method.Invoke(provider, new object[] { json });
            }

            private static void DispatchAuth(object provider, string json)
            {
                var method = provider.GetType().GetMethod("HandleAuthMessage",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(string) }, null)
                    ?? throw new InvalidOperationException("HandleAuthMessage not found.");
                method.Invoke(provider, new object[] { json });
            }

            [Fact]
            public void OhlcChannelFrame_EmitsLiveStream()
            {
                var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
                var frame = """
                    {"channel":"ohlc","data":[
                      {"timestamp":"2026-04-24T12:00:00Z","open":50000,"high":50100,"low":49900,"close":50050,"volume":2.5}
                    ]}
                    """;

                var emitted = Capture(p.LiveStream, () => DispatchPublic(p, frame));

                Assert.Single(emitted);
                Assert.Equal(50050, emitted[0].Close);
                Assert.Equal(2.5, emitted[0].Volume);
            }

            [Fact]
            public void OhlcAllZero_Dropped()
            {
                var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
                var frame = """
                    {"channel":"ohlc","data":[
                      {"timestamp":"2026-04-24T12:00:00Z","open":0,"high":0,"low":0,"close":0,"volume":0}
                    ]}
                    """;
                var emitted = Capture(p.LiveStream, () => DispatchPublic(p, frame));
                Assert.Empty(emitted);
            }

            [Fact]
            public void BookChannelFrame_EmitsOrderBookUpdate()
            {
                var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
                var frame = """
                    {"channel":"book","data":[
                      {"symbol":"BTC/USD",
                       "bids":[{"price":49999,"qty":0.5}],
                       "asks":[{"price":50001,"qty":0.6}]}
                    ]}
                    """;

                var emitted = Capture(p.SubscribeOrderBook("BTC/USD"),
                    () => DispatchPublic(p, frame));

                Assert.Single(emitted);
                Assert.Equal("BTC/USD", emitted[0].Symbol);
                Assert.Equal(49999, emitted[0].Bids[0].Price);
                Assert.Equal(50001, emitted[0].Asks[0].Price);
            }

            [Fact]
            public void EmptyBookSnapshot_DoesNotEmit()
            {
                // Book frame with empty bids+asks should not emit a phantom
                // snapshot — the provider gates on (any bid || any ask).
                var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
                var frame = """
                    {"channel":"book","data":[
                      {"symbol":"BTC/USD","bids":[],"asks":[]}
                    ]}
                    """;

                var emitted = Capture(p.SubscribeOrderBook("BTC/USD"),
                    () => DispatchPublic(p, frame));

                Assert.Empty(emitted);
            }

            [Fact]
            public void MalformedFrame_DoesNotThrow()
            {
                var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
                var emitted = Capture(p.LiveStream, () => DispatchPublic(p, "garbage"));
                Assert.Empty(emitted);
            }

            [Fact]
            public void ExecutionsChannel_EmitsOrderUpdate()
            {
                var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
                var frame = """
                    {"channel":"executions","data":[
                      {"order_id":"O1","symbol":"BTC/USD","side":"buy",
                       "order_status":"filled","cum_qty":0.1,"avg_price":50000,"leaves_qty":0}
                    ]}
                    """;

                var emitted = Capture(p.OrderUpdateStream, () => DispatchAuth(p, frame));

                Assert.Single(emitted);
                Assert.Equal("O1", emitted[0].OrderId);
                Assert.Equal(50000, emitted[0].FilledPrice);
            }
        }

        // ── Finnhub — single channel, candle-aggregation depends on lastCandle ─
        // Tests focus on the trade-frame routing + price-zero discard.

        public class Finnhub
        {
            [Fact]
            public void NonTradeFrame_NoEmission()
            {
                var p = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
                var emitted = Capture(p.LiveStream,
                    () => DispatchFrame(p, """{"type":"ping"}"""));
                Assert.Empty(emitted);
            }

            [Fact]
            public void EmptyTradeArray_NoEmission()
            {
                // Trade frames with no entries (start-of-stream subscribe ack)
                // must not crash or emit.
                var p = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
                var emitted = Capture(p.LiveStream,
                    () => DispatchFrame(p, """{"type":"trade","data":[]}"""));
                Assert.Empty(emitted);
            }

            [Fact]
            public void ZeroPriceTrade_Discarded()
            {
                var p = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
                var emitted = Capture(p.LiveStream,
                    () => DispatchFrame(p, """{"type":"trade","data":[{"p":0,"v":1,"t":1700000000000}]}"""));
                Assert.Empty(emitted);
            }

            [Fact]
            public void MalformedFrame_DoesNotThrow()
            {
                var p = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, "not-json"));
                Assert.Empty(emitted);
            }
        }
    }
}
