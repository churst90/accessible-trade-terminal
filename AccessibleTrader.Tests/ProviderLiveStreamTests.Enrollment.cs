using System.Reflection;
using System.Text.Json;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase E test-debt enrollment: live-stream frame-parse coverage for the
    /// providers missing from <see cref="ProviderLiveStreamTests"/> — Binance,
    /// InteractiveBrokers, TwelveData, plus the Finnhub happy path the original
    /// suite skipped.
    ///
    /// Same approach as the existing suite: reflect into the private parse
    /// method, feed synthetic frames, assert on the public observables. Two
    /// enrolment notes:
    /// - Binance parses frames with System.Text.Json inside its socket runner
    ///   and hands a <see cref="JsonElement"/> (not a string) to OnKlineMessage /
    ///   OnUserDataMessage; malformed JSON is filtered upstream in
    ///   RunSocketAsync, so the string-garbage test is not applicable there.
    /// - Schwab has no WebSocket path at all (live updates are REST polling
    ///   through FetchOhlcvAsync, covered by the fetch enrollment suite), and
    ///   MEXC's socket parsing lives inside the JK.Mexc.Net SDK — neither has
    ///   a frame parser to test here.
    /// </summary>
    public class ProviderLiveStreamEnrollmentTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

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
        /// Binance-shaped dispatch: parse the JSON here (mirroring RunSocketAsync)
        /// and invoke the named private handler with the root JsonElement.
        /// </summary>
        private static void DispatchJsonElement(object provider, string methodName, string json)
        {
            var method = provider.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(JsonElement) }, null)
                ?? throw new InvalidOperationException($"{methodName}(JsonElement) not found on {provider.GetType().Name}.");
            using var doc = JsonDocument.Parse(json);
            method.Invoke(provider, new object[] { doc.RootElement });
        }

        private static List<T> Capture<T>(IObservable<T> source, Action action)
        {
            var captured = new List<T>();
            using var sub = source.Subscribe(captured.Add);
            action();
            return captured;
        }

        // ── Binance — kline stream + user-data executionReport ───────────────
        // Kline frame: {"e":"kline","k":{"t":ms,"o":"","h":"","l":"","c":"","v":""}}
        // User-data:   {"e":"executionReport","s":,"S":,"X":,"q":,"z":,"L":,"p":,"i":,"o":}

        [Collection("ProviderCredentialBridge")]
        public class Binance
        {
            [Fact]
            public void KlineFrame_EmitsOhlcvOnLiveStream()
            {
                var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
                var frame = """
                    {"e":"kline","s":"BTCUSDT","k":{
                      "t":1700000000000,"o":"50000","h":"50100","l":"49900","c":"50050","v":"1.5"}}
                    """;

                var emitted = Capture(p.LiveStream,
                    () => DispatchJsonElement(p, "OnKlineMessage", frame));

                Assert.Single(emitted);
                Assert.Equal(50000.0, emitted[0].Open);
                Assert.Equal(50050.0, emitted[0].Close);
                Assert.Equal(1.5, emitted[0].Volume);
                Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime, emitted[0].Date);
            }

            [Fact]
            public void AllZeroKline_Dropped()
            {
                var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
                var frame = """
                    {"e":"kline","k":{"t":1700000000000,"o":"0","h":"0","l":"0","c":"0","v":"0"}}
                    """;

                var emitted = Capture(p.LiveStream,
                    () => DispatchJsonElement(p, "OnKlineMessage", frame));

                Assert.Empty(emitted);
            }

            [Fact]
            public void FrameWithoutKlinePayload_NoEmission()
            {
                var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();

                var emitted = Capture(p.LiveStream,
                    () => DispatchJsonElement(p, "OnKlineMessage", """{"e":"aggTrade","p":"50000"}"""));

                Assert.Empty(emitted);
            }

            [Fact]
            public void ExecutionReport_EmitsOrderUpdate()
            {
                var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
                var frame = """
                    {"e":"executionReport","s":"BTCUSDT","S":"BUY","X":"FILLED",
                     "q":"0.5","z":"0.5","L":"50000","p":"0","i":12345,"o":"LIMIT"}
                    """;

                var emitted = Capture(p.OrderUpdateStream,
                    () => DispatchJsonElement(p, "OnUserDataMessage", frame));

                Assert.Single(emitted);
                Assert.Equal("12345", emitted[0].OrderId);
                Assert.Equal("BTCUSDT", emitted[0].Symbol);
                Assert.Equal(50000.0, emitted[0].FilledPrice);
                Assert.Equal(0.5, emitted[0].FilledQuantity);
                Assert.Equal(0.0, emitted[0].RemainingQuantity);
            }

            [Fact]
            public void ExecutionReport_NewStatus_EmitsNew()
            {
                // NEW used to be dropped with no trace; it now flows through as
                // OrderStatus.New, which the order service logs (and does not
                // announce — placement was already announced).
                var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
                var frame = """
                    {"e":"executionReport","s":"BTCUSDT","S":"BUY","X":"NEW",
                     "q":"0.5","z":"0","L":"0","p":"50000","i":12345,"o":"LIMIT"}
                    """;

                var emitted = Capture(p.OrderUpdateStream,
                    () => DispatchJsonElement(p, "OnUserDataMessage", frame));

                Assert.Equal(AccessibleTrader.Sdk.Trading.OrderStatus.New, Assert.Single(emitted).Status);
            }

            [Fact]
            public void NonExecutionReportEvent_NoEmission()
            {
                var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();

                var emitted = Capture(p.OrderUpdateStream,
                    () => DispatchJsonElement(p, "OnUserDataMessage", """{"e":"outboundAccountPosition"}"""));

                Assert.Empty(emitted);
            }
        }

        // ── InteractiveBrokers — smd (market data) + sor (order status) topics ─

        [Collection("ProviderCredentialBridge")]
        public class InteractiveBrokers
        {
            [Fact]
            public void SmdFrame_EmitsFlatOhlcvFromLastPrice()
            {
                // Field 31 = last price; the provider broadcasts it as a flat bar.
                var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
                var frame = """{"topic":"smd+265598","31":"150.5"}""";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal(150.5, emitted[0].Close);
                Assert.Equal(emitted[0].Open, emitted[0].Close);
                Assert.Equal(emitted[0].High, emitted[0].Close);
                Assert.Equal(emitted[0].Low,  emitted[0].Close);
            }

            [Fact]
            public void SmdFrame_ZeroOrMissingLast_NoEmission()
            {
                var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();

                var zero    = Capture(p.LiveStream, () => DispatchFrame(p, """{"topic":"smd+265598","31":"0"}"""));
                var missing = Capture(p.LiveStream, () => DispatchFrame(p, """{"topic":"smd+265598","84":"150.0"}"""));

                Assert.Empty(zero);
                Assert.Empty(missing);
            }

            [Fact]
            public void SorFrame_EmitsOrderUpdate()
            {
                var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
                var frame = """
                    {"topic":"sor","args":[
                      {"orderId":"1001","ticker":"AAPL","side":"BUY","status":"Filled",
                       "filledQuantity":10,"avgPrice":150.25,"remainingQuantity":0}
                    ]}
                    """;

                var emitted = Capture(p.OrderUpdateStream, () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal("1001", emitted[0].OrderId);
                Assert.Equal("AAPL", emitted[0].Symbol);
                Assert.Equal(150.25, emitted[0].FilledPrice);
                Assert.Equal(10.0, emitted[0].FilledQuantity);
            }

            [Fact]
            public void MalformedFrame_DoesNotThrow_NoEmission()
            {
                var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, "garbage"));
                Assert.Empty(emitted);
            }

            [Fact]
            public void UnknownTopic_NoEmission()
            {
                var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
                var live   = Capture(p.LiveStream,        () => DispatchFrame(p, """{"topic":"system","hb":1}"""));
                var orders = Capture(p.OrderUpdateStream, () => DispatchFrame(p, """{"topic":"system","hb":1}"""));
                Assert.Empty(live);
                Assert.Empty(orders);
            }
        }

        // ── TwelveData — single "price" event feeding a candle aggregator ────

        [Collection("ProviderCredentialBridge")]
        public class TwelveData
        {
            [Fact]
            public void PriceEvent_EmitsOhlcvOnLiveStream()
            {
                var p = new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider();
                var frame = """{"event":"price","symbol":"AAPL","price":150.5,"timestamp":1700000000}""";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal(150.5, emitted[0].Close);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, emitted[0].Date);
            }

            [Fact]
            public void ZeroPrice_Discarded()
            {
                var p = new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider();
                var emitted = Capture(p.LiveStream,
                    () => DispatchFrame(p, """{"event":"price","price":0,"timestamp":1700000000}"""));
                Assert.Empty(emitted);
            }

            [Fact]
            public void NonPriceEvent_NoEmission()
            {
                var p = new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider();
                var emitted = Capture(p.LiveStream,
                    () => DispatchFrame(p, """{"event":"subscribe-status","status":"ok"}"""));
                Assert.Empty(emitted);
            }

            [Fact]
            public void MalformedFrame_DoesNotThrow_NoEmission()
            {
                var p = new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider();
                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, "not-json"));
                Assert.Empty(emitted);
            }
        }

        // ── Finnhub — happy path missing from the original suite ─────────────

        [Collection("ProviderCredentialBridge")]
        public class Finnhub
        {
            [Fact]
            public void TradeFrame_EmitsAggregatedCandle()
            {
                var p = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
                long tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var frame = "{\"type\":\"trade\",\"data\":[{\"p\":50000.0,\"v\":0.1,\"t\":" + tsMs + "}]}";

                var emitted = Capture(p.LiveStream, () => DispatchFrame(p, frame));

                Assert.Single(emitted);
                Assert.Equal(50000.0, emitted[0].Close);
                Assert.Equal(0.1, emitted[0].Volume);
            }

            [Fact]
            public void SecondTradeInSameBar_UpdatesHighLow()
            {
                // Two trades inside one aggregation window: the second updates the
                // forming candle rather than opening a new one.
                var p = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
                long tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var first  = "{\"type\":\"trade\",\"data\":[{\"p\":50000.0,\"v\":0.1,\"t\":" + tsMs + "}]}";
                var second = "{\"type\":\"trade\",\"data\":[{\"p\":50100.0,\"v\":0.2,\"t\":" + (tsMs + 1000) + "}]}";

                var emitted = Capture(p.LiveStream, () =>
                {
                    DispatchFrame(p, first);
                    DispatchFrame(p, second);
                });

                Assert.Equal(2, emitted.Count);
                Assert.Equal(50000.0, emitted[1].Open);    // open preserved from first trade
                Assert.Equal(50100.0, emitted[1].High);    // high lifted by second trade
                Assert.Equal(50100.0, emitted[1].Close);
            }
        }
    }
}
