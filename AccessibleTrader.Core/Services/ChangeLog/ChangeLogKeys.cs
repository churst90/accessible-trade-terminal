using System.Globalization;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>
    /// The de-duplication keys. Two producers of one firing must make the same key; two
    /// different firings must not. Times are UTC to the minute, and every number is invariant.
    /// </summary>
    internal static class ChangeLogKeys
    {
        public static string Alert(AlertFired alert, DateTime nowUtc)
        {
            string id = alert.Definition?.Id ?? "";
            string symbol = alert.Symbol ?? alert.Definition?.Symbol ?? "";
            return $"alert|{id}|{symbol}|{Minute(nowUtc)}";
        }

        public static string Order(string? provider, OrderUpdate order)
            => $"order|{provider ?? ""}|{order.OrderId}|{order.Status}|"
             + order.FilledQuantity.ToString("R", CultureInfo.InvariantCulture);

        public static string Event(string eventId, MarketEventNoticeKind kind)
            => $"event|{eventId}|{kind}";

        public static string Connection(string provider, ConnectionState state, DateTime nowUtc)
            => $"conn|{provider}|{state}|{Minute(nowUtc)}";

        private static string Minute(DateTime utc)
            => utc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
    }
}
