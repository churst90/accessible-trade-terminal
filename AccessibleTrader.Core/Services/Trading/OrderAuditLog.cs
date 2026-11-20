using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// An append-only record of every order the terminal sent, refused, cancelled or heard back
    /// about: one JSON object per line in <c>OrderAudit/orders-yyyy-MM.jsonl</c> under the user's
    /// own data folder (per account on the hosted server).
    ///
    /// <para>Code review, 2026-11-20: the spoken announcements and the venue's own history were the
    /// only record, so "did I really send that?" had no answer of the terminal's own once speech had
    /// moved on. Written by <see cref="GeneralOrderService"/>, the one door every order goes through
    /// (dashboard ticket, quick trade, close position, protective-order edits, strategies), and by
    /// its order-update stream, which carries the fills and cancels a venue or the paper broker
    /// reports, stops firing included.</para>
    ///
    /// <para>Never a credential: a line holds the order's own fields, the outcome and the reason in
    /// the words the user heard. A write that fails is logged and dropped; recording an order must
    /// never stop one. A month's file stops growing at <see cref="MaxFileBytes"/> (one warning),
    /// so a public demo cannot fill a disk with it.</para>
    /// </summary>
    public sealed class OrderAuditLog
    {
        public const string FolderName = "OrderAudit";
        internal const long MaxFileBytes = 20L * 1024 * 1024;
        private const int MaxReasonChars = 500;

        private static readonly JsonSerializerSettings Json = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
        };

        private readonly IPlatformPathService _paths;
        private readonly ILogger<OrderAuditLog>? _logger;
        private readonly Func<DateTime> _utcNow;
        // One lock for the process, not per instance: the hosted head makes one log per circuit, and
        // on the demo every circuit writes the same file. Two appends opening it at once would throw.
        private static readonly object Gate = new();
        private bool _capWarned;

        public OrderAuditLog(IPlatformPathService paths, ILogger<OrderAuditLog>? logger = null)
            : this(paths, logger, () => DateTime.UtcNow) { }

        internal OrderAuditLog(IPlatformPathService paths, ILogger<OrderAuditLog>? logger, Func<DateTime> utcNow)
        {
            _paths = paths;
            _logger = logger;
            _utcNow = utcNow;
        }

        /// <summary>This month's file.</summary>
        public string CurrentPath => PathFor(_utcNow());

        private string PathFor(DateTime utc) =>
            Path.Combine(_paths.AppDataDirectory, FolderName, $"orders-{utc:yyyy-MM}.jsonl");

        /// <summary>An order the terminal was asked to send, and what became of it.</summary>
        public void Placement(string provider, TradeSignal signal, OrderPlacement placement) =>
            Write(new Entry
            {
                Event = placement.Outcome switch
                {
                    OrderOutcome.Placed or OrderOutcome.Accepted => "sent",
                    OrderOutcome.Uncertain => "uncertain",
                    OrderOutcome.Rejected => "refused",
                    _ => "failed",
                },
                Outcome = placement.Outcome.ToString(),
                Provider = provider,
                Symbol = signal.Symbol,
                Side = signal.Side.ToString(),
                Type = signal.Type.ToString(),
                Quantity = signal.Quantity,
                Price = signal.Price,
                Trigger = signal.TriggerPrice,
                StopLoss = signal.StopLoss,
                TakeProfit = signal.TakeProfit,
                Leverage = signal.Leverage,
                ReduceOnly = signal.ReduceOnly ? true : null,
                OrderId = placement.OrderId,
                Reason = Clip(placement.FailureMessage),
            });

        /// <summary>A one-cancels-other pair, sent or refused.</summary>
        public void OcoPair(string provider, string symbol, OrderSide side, double quantity,
                            double limitPrice, double stopTrigger, bool ok, string message) =>
            Write(new Entry
            {
                Event = ok ? "sent" : "refused",
                Provider = provider,
                Symbol = symbol,
                Side = side.ToString(),
                Type = "OcoPair",
                Quantity = quantity,
                Price = limitPrice,
                Trigger = stopTrigger,
                Reason = ok ? null : Clip(message),
            });

        /// <summary>A cancel the user or the terminal asked for, and whether the venue took it.</summary>
        public void Cancel(string provider, string orderId, string symbol, bool ok) =>
            Write(new Entry
            {
                Event = ok ? "cancel-sent" : "cancel-failed",
                Provider = provider,
                Symbol = symbol,
                OrderId = orderId,
            });

        /// <summary>What a venue or the paper broker reported back: a fill, a cancel, a refusal.</summary>
        public void Update(string provider, OrderUpdate update) =>
            Write(new Entry
            {
                Event = "update",
                Outcome = update.Status.ToString(),
                Provider = provider,
                Symbol = update.Symbol,
                Side = update.Side.ToString(),
                OrderId = update.OrderId,
                FilledQuantity = update.FilledQuantity,
                FilledPrice = update.FilledPrice,
                Remaining = update.RemainingQuantity,
                Leg = update.StopTriggered ? (update.Trailing ? "trailing stop" : "stop")
                    : update.TakeProfitTriggered ? (update.Trailing ? "trailing take profit" : "take profit")
                    : null,
                RealizedPnL = update.RealizedPnL,
                VenueTime = update.Timestamp == default ? null : update.Timestamp,
                Reason = Clip(update.Reason),
            });

        private void Write(Entry entry)
        {
            try
            {
                var now = _utcNow();
                entry.Utc = now;
                string line = JsonConvert.SerializeObject(entry, Json) + "\n";
                string path = PathFor(now);
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length >= MaxFileBytes)
                    {
                        if (!_capWarned)
                        {
                            _capWarned = true;
                            _logger?.LogWarning("Order audit file {Path} reached {Max} bytes; later orders this month are not recorded.", path, MaxFileBytes);
                        }
                        return;
                    }
                    File.AppendAllText(path, line);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Order audit line could not be written.");
            }
        }

        private static string? Clip(string? s) =>
            string.IsNullOrEmpty(s) ? null : s.Length <= MaxReasonChars ? s : s[..MaxReasonChars] + "…";

        private sealed class Entry
        {
            [JsonProperty("utc")] public DateTime Utc { get; set; }
            [JsonProperty("event")] public string Event { get; set; } = "";
            [JsonProperty("outcome")] public string? Outcome { get; set; }
            [JsonProperty("provider")] public string Provider { get; set; } = "";
            [JsonProperty("symbol")] public string? Symbol { get; set; }
            [JsonProperty("side")] public string? Side { get; set; }
            [JsonProperty("type")] public string? Type { get; set; }
            [JsonProperty("quantity")] public double? Quantity { get; set; }
            [JsonProperty("price")] public double? Price { get; set; }
            [JsonProperty("trigger")] public double? Trigger { get; set; }
            [JsonProperty("stopLoss")] public double? StopLoss { get; set; }
            [JsonProperty("takeProfit")] public double? TakeProfit { get; set; }
            [JsonProperty("leverage")] public double? Leverage { get; set; }
            [JsonProperty("reduceOnly")] public bool? ReduceOnly { get; set; }
            [JsonProperty("orderId")] public string? OrderId { get; set; }
            [JsonProperty("filledQuantity")] public double? FilledQuantity { get; set; }
            [JsonProperty("filledPrice")] public double? FilledPrice { get; set; }
            [JsonProperty("remaining")] public double? Remaining { get; set; }
            [JsonProperty("leg")] public string? Leg { get; set; }
            [JsonProperty("realizedPnL")] public double? RealizedPnL { get; set; }
            [JsonProperty("venueTime")] public DateTime? VenueTime { get; set; }
            [JsonProperty("reason")] public string? Reason { get; set; }
        }
    }
}
