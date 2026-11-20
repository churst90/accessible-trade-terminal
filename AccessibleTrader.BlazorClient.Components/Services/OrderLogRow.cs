using System.Globalization;
using AccessibleTrader.Core.Services.Trading;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// One row of the trading dashboard's Order log tab: a line of <see cref="OrderAuditLog"/> in
    /// words a trader reads, not the file's field names. "What happened" leads after the time
    /// because it is the question the tab is opened to answer ("did my stop fire? was that order
    /// refused?"); Details carries whatever else the line holds, the venue's reason first.
    /// </summary>
    public sealed record OrderLogRow(
        DateTime Utc, string What, string Symbol, string Exchange, string Side, string Type,
        string Quantity, string Price, string Details)
    {
        public static OrderLogRow From(OrderAuditLog.Record r)
        {
            double? qty = r.Event == "update" ? r.FilledQuantity ?? r.Quantity : r.Quantity;
            double? price = r.Event == "update" ? r.FilledPrice ?? r.Price : r.Price ?? r.Trigger;
            return new OrderLogRow(
                r.Utc,
                WhatHappened(r),
                r.Symbol ?? "",
                r.Provider,
                r.Side ?? "",
                TypeWords(r.Type),
                // A fill with nothing filled (a plain cancel) has no quantity worth reading.
                qty is double q && q != 0 ? PriceFormatter.FormatQuantity(q) : "",
                price is double p && p != 0 ? PriceFormatter.FormatPrice(p) : "",
                DetailsOf(r, price));
        }

        /// <summary>The time column: today reads as the time alone, any other day leads with
        /// the date, so a row from last week is never mistaken for this morning's.</summary>
        public string When(DateTime nowLocal)
        {
            var local = Utc.ToLocalTime();
            return local.Date == nowLocal.Date
                ? local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : local.ToString("MMM d, HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>Plain text for a CSV file: every column, the time as an ISO stamp in UTC.</summary>
        public static string ToCsv(IEnumerable<OrderLogRow> rows)
        {
            var sb = new System.Text.StringBuilder("Time (UTC),What happened,Symbol,Exchange,Side,Type,Quantity,Price,Details\n");
            foreach (var r in rows)
            {
                sb.Append(string.Join(",", new[]
                {
                    r.Utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    r.What, r.Symbol, r.Exchange, r.Side, r.Type, r.Quantity, r.Price, r.Details,
                }.Select(Cell)));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string Cell(string s)
        {
            // A leading = + - @ makes a spreadsheet run the cell as a formula; a venue's reason
            // text is not ours to trust that far.
            if (s.Length > 0 && "=+-@".Contains(s[0]) && !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                s = "'" + s;
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private static string WhatHappened(OrderAuditLog.Record r) => r.Event switch
        {
            "sent"          => "Sent",
            "uncertain"     => "Sent, not confirmed",
            "refused"       => "Refused",
            "failed"        => "Failed",
            "cancel-sent"   => "Cancel sent",
            "cancel-failed" => "Cancel failed",
            "update" => r.Outcome switch
            {
                "Filled"      => r.Leg != null ? $"Filled by {r.Leg}" : "Filled",
                "PartialFill" => r.Leg != null ? $"Part filled by {r.Leg}" : "Part filled",
                "Cancelled"   => "Cancelled",
                "Rejected"    => "Refused by the venue",
                "New"         => "Accepted",
                "Expired"     => "Expired",
                "Replaced"    => "Changed",
                _             => "Update",
            },
            _ => r.Event,
        };

        private static string TypeWords(string? type) => type switch
        {
            null => "",
            "OcoPair" => "One cancels other",
            "StopMarket" => "Stop",
            "StopLimit" => "Stop limit",
            "TakeProfitMarket" => "Take profit",
            "TakeProfitLimit" => "Take profit limit",
            _ => type,
        };

        private static string DetailsOf(OrderAuditLog.Record r, double? shownPrice)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Reason)) parts.Add(r.Reason!.Trim());
            if (r.Trigger is double t && t != 0 && t != shownPrice) parts.Add($"trigger {PriceFormatter.FormatPrice(t)}");
            if (r.StopLoss is double sl && sl != 0) parts.Add($"stop loss {PriceFormatter.FormatPrice(sl)}");
            if (r.TakeProfit is double tp && tp != 0) parts.Add($"take profit {PriceFormatter.FormatPrice(tp)}");
            if (r.Leverage is double lev && lev > 1) parts.Add($"{lev.ToString("0.##", CultureInfo.InvariantCulture)} times leverage");
            if (r.ReduceOnly) parts.Add("reduce only");
            if (r.Outcome == "PartialFill" && r.Remaining is double rem && rem > 0) parts.Add($"{PriceFormatter.FormatQuantity(rem)} left");
            if (r.RealizedPnL is double pnl && pnl != 0) parts.Add($"P&L {PriceFormatter.FormatPnL(pnl)}");
            if (!string.IsNullOrEmpty(r.OrderId)) parts.Add($"order {r.OrderId}");
            return string.Join("; ", parts);
        }
    }
}
