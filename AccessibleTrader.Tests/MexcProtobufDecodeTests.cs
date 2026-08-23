using System;
using AccessibleTrader.Plugins.Mexc;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Google.Protobuf;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Offline validation of the MEXC direct-API Protobuf spot-kline decode. MEXC's
    /// spot WebSocket is Protobuf-only (the JSON WS was discontinued 2025-08-04); the
    /// plugin generates C# from the official mexcdevelop/websocket-proto files and
    /// projects the kline body to an Ohlcv. This round-trips a wrapper through the
    /// real generated parser so the decode path is verified without a live socket.
    /// </summary>
    public class MexcProtobufDecodeTests
    {
        private static byte[] EncodeKlineFrame(long windowStart, string o, string h, string l, string c, string v)
        {
            var kline = new PublicSpotKlineV3Api
            {
                Interval = "Min1",
                WindowStart = windowStart,
                OpeningPrice = o, HighestPrice = h, LowestPrice = l, ClosingPrice = c,
                Volume = v, Amount = "0", WindowEnd = windowStart + 60,
            };
            var wrapper = new PushDataV3ApiWrapper
            {
                Channel = "spot@public.kline.v3.api.pb@BTCUSDT@Min1",
                PublicSpotKline = kline,
                Symbol = "BTCUSDT",
            };
            return wrapper.ToByteArray();
        }

        [Fact]
        public void SpotKlineFrame_roundtrips_to_Ohlcv()
        {
            var frame = EncodeKlineFrame(1_700_000_000, "100.5", "102.0", "99.5", "101.0", "12.34");

            var wrapper = MexcProtobuf.TryParse(frame);
            Assert.NotNull(wrapper);
            Assert.True(MexcProtobuf.TryReadKline(wrapper!, out var bar));

            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, bar.Date);
            Assert.Equal(100.5, bar.Open);
            Assert.Equal(102.0, bar.High);
            Assert.Equal(99.5, bar.Low);
            Assert.Equal(101.0, bar.Close);
            Assert.Equal(12.34, bar.Volume, 4);
        }

        [Fact]
        public void Garbage_bytes_parse_to_null_or_no_kline()
        {
            // Random bytes must not throw — TryParse swallows and returns null, or
            // parses to a wrapper with no kline body.
            var wrapper = MexcProtobuf.TryParse(new byte[] { 0xFF, 0x01, 0x02, 0x99, 0x7A });
            if (wrapper != null)
                Assert.False(MexcProtobuf.TryReadKline(wrapper, out _));
        }

        [Fact]
        public void Zero_price_kline_is_rejected()
        {
            var frame = EncodeKlineFrame(1_700_000_000, "0", "0", "0", "0", "0");
            var wrapper = MexcProtobuf.TryParse(frame);
            Assert.NotNull(wrapper);
            Assert.False(MexcProtobuf.TryReadKline(wrapper!, out _));
        }

        // ── Private order stream (fill/cancel announcements) ─────────────────

        [Theory]
        [InlineData(2, PolledOrderStateNone.Filled)]        // filled
        [InlineData(3, PolledOrderStateNone.PartialFill)]   // partially filled
        [InlineData(4, PolledOrderStateNone.Cancelled)]     // canceled
        [InlineData(5, PolledOrderStateNone.Cancelled)]     // partially canceled
        public void PrivateOrder_status_maps_to_announcement(int wireStatus, PolledOrderStateNone expected)
        {
            var order = new PrivateOrdersV3Api
            {
                Id = "OID-1", Status = wireStatus, TradeType = 2, // sell
                Price = "100", AvgPrice = "101", CumulativeQuantity = "3", RemainQuantity = "2",
            };
            var u = MexcProtobuf.MapPrivateOrder(order, "BTCUSDT");
            Assert.NotNull(u);
            Assert.Equal("BTCUSDT", u!.Symbol);
            Assert.Equal(OrderSide.Sell, u.Side);            // tradeType 2 → sell
            Assert.Equal(101, u.FilledPrice);                // avg price preferred over order price
            Assert.Equal(3, u.FilledQuantity);
            Assert.Equal((OrderStatus)(int)expected, u.Status);
        }

        [Fact]
        public void PrivateOrder_new_status_maps_to_New()
        {
            // Status 1 (new) used to be dropped with no trace; the order service
            // now logs New, so the mapper reports it instead of returning null.
            var order = new PrivateOrdersV3Api { Id = "OID-2", Status = 1, TradeType = 1, Price = "100" };
            Assert.Equal(OrderStatus.New, MexcProtobuf.MapPrivateOrder(order, "BTCUSDT").Status);
        }

        [Fact]
        public void PrivateOrder_unrecognised_status_maps_to_Unknown_naming_the_code()
        {
            var order = new PrivateOrdersV3Api { Id = "OID-3", Status = 9, TradeType = 1, Price = "100" };
            var u = MexcProtobuf.MapPrivateOrder(order, "BTCUSDT");
            Assert.Equal(OrderStatus.Unknown, u.Status);
            Assert.Contains("9", u.Reason);
        }

        [Fact]
        public void PrivateOrder_partial_fill_then_cancel_carries_the_executed_part()
        {
            // Status 5 (PARTIALLY_FILLED_CANCELED) is terminal-cancelled, but the
            // fill that happened first opened a live position — the update must
            // carry it so the announcement can speak it.
            var order = new PrivateOrdersV3Api
            {
                Id = "OID-4", Status = 5, TradeType = 1,
                Price = "100", AvgPrice = "99.5", CumulativeQuantity = "0.4", RemainQuantity = "0.6",
            };
            var u = MexcProtobuf.MapPrivateOrder(order, "BTCUSDT");
            Assert.Equal(OrderStatus.Cancelled, u.Status);
            Assert.Equal(0.4, u.FilledQuantity);
            Assert.Equal(99.5, u.FilledPrice);
        }

        // Mirrors OrderStatus values so the theory reads clearly without exposing the
        // full enum in InlineData positions.
        public enum PolledOrderStateNone
        {
            PartialFill = OrderStatus.PartialFill,
            Filled = OrderStatus.Filled,
            Cancelled = OrderStatus.Cancelled,
        }
    }
}
