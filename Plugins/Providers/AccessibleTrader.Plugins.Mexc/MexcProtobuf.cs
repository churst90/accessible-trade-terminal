using System;
using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Plugins.Mexc
{
    /// <summary>
    /// Maps decoded MEXC spot WebSocket Protobuf messages (generated from the official
    /// github.com/mexcdevelop/websocket-proto definitions, global namespace) to the
    /// app's domain types. The generated <c>PushDataV3ApiWrapper.Parser</c> does the
    /// wire decoding; this only projects the relevant oneof bodies. MEXC re-sends the
    /// current kline window with cumulative volume-so-far, so callers consolidate with
    /// <see cref="AccessibleTrader.Sdk.Plugins.LiveTickStyle.CumulativeBars"/>.
    /// </summary>
    internal static class MexcProtobuf
    {
        /// <summary>Parses a binary push frame; returns null if it isn't valid Protobuf.</summary>
        public static global::PushDataV3ApiWrapper? TryParse(byte[] data)
        {
            try { return global::PushDataV3ApiWrapper.Parser.ParseFrom(data); }
            catch { return null; }
        }

        /// <summary>Projects a spot-kline push into an <see cref="Ohlcv"/> bar (window
        /// start is a UTC second timestamp; O/H/L/C/V are decimal strings).</summary>
        public static bool TryReadKline(global::PushDataV3ApiWrapper wrapper, out Ohlcv bar)
        {
            bar = default;
            if (wrapper.BodyCase != global::PushDataV3ApiWrapper.BodyOneofCase.PublicSpotKline) return false;
            var k = wrapper.PublicSpotKline;
            if (k == null) return false;

            var date = DateTimeOffset.FromUnixTimeSeconds(k.WindowStart).UtcDateTime;
            double open  = ParseD(k.OpeningPrice);
            double high  = ParseD(k.HighestPrice);
            double low   = ParseD(k.LowestPrice);
            double close = ParseD(k.ClosingPrice);
            double vol   = ParseD(k.Volume);
            if (open <= 0 || high <= 0 || low <= 0 || close <= 0) return false;

            bar = new Ohlcv(date, open, high, low, close, vol);
            return true;
        }

        private static double ParseD(string? s) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
