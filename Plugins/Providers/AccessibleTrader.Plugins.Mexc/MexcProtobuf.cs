using System;
using System.Globalization;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;

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

        /// <summary>Projects a private-order push into an <see cref="OrderUpdate"/>.
        /// MEXC spot order status: 1 new, 2 filled, 3 partially filled, 4 canceled,
        /// 5 partially filled then canceled; tradeType 1 buy, 2 sell. Status 5 used
        /// to be squashed into a bare Cancelled — hiding the live position the
        /// partial fill opened before the cancel; it still maps to Cancelled (the
        /// order IS terminal) but the cumulative fill quantity rides along and the
        /// announcement speaks it. Unrecognised codes used to be dropped with no
        /// trace; they now map to Unknown, which the order service logs.</summary>
        public static OrderUpdate MapPrivateOrder(global::PrivateOrdersV3Api o, string symbol)
        {
            OrderStatus status = o.Status switch
            {
                1 => OrderStatus.New,
                2 => OrderStatus.Filled,
                3 => OrderStatus.PartialFill,
                4 or 5 => OrderStatus.Cancelled,
                _ => OrderStatus.Unknown,
            };

            double filled = ParseD(o.CumulativeQuantity);
            double avg    = ParseD(o.AvgPrice);
            double price  = avg > 0 ? avg : ParseD(o.Price);
            return new OrderUpdate(
                o.Id ?? string.Empty, symbol,
                o.TradeType == 1 ? OrderSide.Buy : OrderSide.Sell,
                filled, price, ParseD(o.RemainQuantity), status,
                false, false, DateTime.UtcNow,
                Reason: status == OrderStatus.Unknown ? $"MEXC status code {o.Status}" : null);
        }

        private static double ParseD(string? s) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
