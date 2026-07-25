using System;
using AccessibleTrader.Plugins.Mexc;
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
    }
}
