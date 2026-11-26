using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Hyperliquid
{
    /// <summary>
    /// The account's order events from the <c>orderUpdates</c> and <c>userFills</c> websocket
    /// subscriptions, turned into <see cref="OrderUpdate"/>s. Both are public by address: no key.
    ///
    /// <para>
    /// What a 75-second recording of a busy public account showed (2026-11-26), and why this is
    /// shaped the way it is:
    /// </para>
    /// <list type="bullet">
    /// <item>An order update carries sizes and a status word but no price and no order type, so
    /// the fill price comes from the fills and the stop/take-profit fact from one REST lookup.</item>
    /// <item>A partial fill sends NO order update; only the fill says it happened.</item>
    /// <item>A fill can come before the order's own "open" (an order that crossed on arrival),
    /// and every fill seen came before its order's "filled" or "canceled", but nothing promises
    /// that, so a terminal status waits a moment for fills it has not seen yet.</item>
    /// <item>A cancel after a partial fill reports the unfilled rest in <c>sz</c>.</item>
    /// <item>The first <c>userFills</c> frame is the recent history, marked <c>isSnapshot</c>;
    /// later frames have no such field.</item>
    /// </list>
    /// </summary>
    internal sealed class HyperliquidOrderStream
    {
        private const double Eps = 1e-12;
        private static readonly TimeSpan Forget = TimeSpan.FromMinutes(30);

        private sealed class Tally { public double Qty, Notional, Pnl; public DateTime Seen; }

        private readonly object _gate = new();
        private readonly Dictionary<long, Tally> _fills = new();
        private readonly Dictionary<long, (double OrigSize, DateTime Seen)> _orders = new();
        private bool _ordersOn, _fillsOn;

        private readonly Func<string, string> _display;
        private readonly Func<long, Task<string?>> _orderType;
        private readonly Func<long, Task<(double Qty, double Price)>> _restFills;
        private readonly Action<OrderUpdate> _emit;
        private readonly TimeSpan _fillWait;

        /// <param name="display">The terminal's symbol for a venue coin.</param>
        /// <param name="orderType">The order's type ("Stop Market", "Take Profit Limit", ...) from
        /// the REST order status, or null when it cannot be read.</param>
        /// <param name="restFills">Quantity and average price of an order's fills from the REST
        /// fill history, for when the stream's own fills fall short.</param>
        internal HyperliquidOrderStream(Func<string, string> display, Func<long, Task<string?>> orderType,
            Func<long, Task<(double Qty, double Price)>> restFills, Action<OrderUpdate> emit, TimeSpan fillWait)
        {
            _display = display; _orderType = orderType; _restFills = restFills; _emit = emit; _fillWait = fillWait;
        }

        /// <summary>True once the venue has confirmed both subscriptions on the current connection.</summary>
        public bool Subscribed { get { lock (_gate) return _ordersOn && _fillsOn; } }

        /// <summary>A new connection: nothing is subscribed on it yet.</summary>
        public void Reset()
        {
            lock (_gate) { _ordersOn = false; _fillsOn = false; }
        }

        public static string SubscribeMessage(string type, string user) =>
            new JObject
            {
                ["method"] = "subscribe",
                ["subscription"] = new JObject { ["type"] = type, ["user"] = user },
            }.ToString(Newtonsoft.Json.Formatting.None);

        /// <summary>
        /// One frame. The returned task finishes when every update the frame caused has been
        /// emitted (a terminal status may wait for its fills); the socket does not wait for it.
        /// </summary>
        public Task OnMessageAsync(string msg)
        {
            JObject root;
            try { root = JObject.Parse(msg); }
            catch (Exception) { return Task.CompletedTask; }

            switch (root["channel"]?.ToString())
            {
                case "subscriptionResponse":
                    string? type = root["data"]?["subscription"]?["type"]?.ToString();
                    lock (_gate)
                    {
                        if (type == "orderUpdates") _ordersOn = true;
                        else if (type == "userFills") _fillsOn = true;
                    }
                    return Task.CompletedTask;
                case "userFills":
                    OnFills(root["data"] as JObject);
                    return Task.CompletedTask;
                case "orderUpdates":
                    var pending = new List<Task>();
                    foreach (var item in root["data"] as JArray ?? new JArray())
                        if (OnOrder(item) is Task t) pending.Add(t);
                    return Task.WhenAll(pending);
                default:
                    return Task.CompletedTask;
            }
        }

        private void OnFills(JObject? data)
        {
            // The history that opens the subscription: already happened, already announced.
            if (data is null || data["isSnapshot"]?.Value<bool>() == true) return;

            foreach (var f in data["fills"] as JArray ?? new JArray())
            {
                if (f["oid"]?.Type != JTokenType.Integer) continue;
                long oid = f["oid"]!.Value<long>();
                double sz = HyperliquidWire.D(f["sz"]), px = HyperliquidWire.D(f["px"]), pnl = HyperliquidWire.D(f["closedPnl"]);
                if (sz <= 0) continue;

                double qty, notional, orig;
                lock (_gate)
                {
                    Prune();
                    if (!_fills.TryGetValue(oid, out var t)) _fills[oid] = t = new Tally();
                    t.Qty += sz; t.Notional += sz * px; t.Pnl += pnl; t.Seen = DateTime.UtcNow;
                    qty = t.Qty; notional = t.Notional;
                    // Only an order whose size is known can be called partly filled; a fill on an
                    // order not seen opening is left to its terminal status to report.
                    if (!_orders.TryGetValue(oid, out var known) || qty >= known.OrigSize - Eps) continue;
                    orig = known.OrigSize;
                }
                _emit(new OrderUpdate(oid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _display(f["coin"]?.ToString() ?? ""),
                    f["side"]?.ToString() == "A" ? OrderSide.Sell : OrderSide.Buy,
                    Q(qty), notional / qty, Q(orig - qty), OrderStatus.PartialFill,
                    StopTriggered: false, TakeProfitTriggered: false,
                    Timestamp: Time(f["time"]),
                    RealizedPnL: pnl != 0 ? pnl : null));
            }
        }

        private Task? OnOrder(JToken item)
        {
            var o = item["order"];
            if (o?["oid"]?.Type != JTokenType.Integer) return null;
            long oid = o["oid"]!.Value<long>();
            string status = item["status"]?.ToString() ?? "";
            double orig = HyperliquidWire.D(o["origSz"]), left = HyperliquidWire.D(o["sz"]);
            string symbol = _display(o["coin"]?.ToString() ?? "");
            var side = o["side"]?.ToString() == "A" ? OrderSide.Sell : OrderSide.Buy;
            var at = Time(item["statusTimestamp"]);

            if (status is "open" or "triggered")
            {
                lock (_gate) { Prune(); _orders[oid] = (orig, DateTime.UtcNow); }
                _emit(new OrderUpdate(Id(oid), symbol, side, Q(Math.Max(0, orig - left)), 0, left, OrderStatus.New,
                    false, false, at));
                return null;
            }

            var (state, reason) = Map(status);
            lock (_gate) _orders.Remove(oid);
            double filled = state == OrderStatus.Filled ? orig : Q(Math.Max(0, orig - left));
            if (filled <= Eps)
            {
                lock (_gate) _fills.Remove(oid);
                _emit(new OrderUpdate(Id(oid), symbol, side, 0, 0, left, state, false, false, at, Reason: reason));
                return null;
            }
            return EmitWithFillsAsync(oid, symbol, side, filled, state == OrderStatus.Filled ? 0 : left, state, reason, at);
        }

        private async Task EmitWithFillsAsync(long oid, string symbol, OrderSide side, double filled, double left,
            OrderStatus state, string? reason, DateTime at)
        {
            var deadline = DateTime.UtcNow + _fillWait;
            Tally? t;
            while (true)
            {
                lock (_gate) _fills.TryGetValue(oid, out t);
                if ((t?.Qty ?? 0) >= filled - Eps || DateTime.UtcNow >= deadline) break;
                await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
            }
            lock (_gate) _fills.Remove(oid);

            double price = t is { Qty: > 0 } ? t.Notional / t.Qty : 0;
            double? pnl = t is { Pnl: not 0 } ? t.Pnl : null;
            if ((t?.Qty ?? 0) < filled - Eps)
            {
                // The stream missed some of them (a reconnect, or they are still on the way).
                try
                {
                    var (qty, px) = await _restFills(oid).ConfigureAwait(false);
                    if (qty > 0) price = px;
                }
                catch (Exception) { /* the fill is still reported, without a price */ }
            }

            string? type = null;
            try { type = await _orderType(oid).ConfigureAwait(false); }
            catch (Exception) { /* reported as a plain fill */ }

            _emit(new OrderUpdate(Id(oid), symbol, side, filled, price, left, state,
                StopTriggered: type?.StartsWith("Stop", StringComparison.Ordinal) == true,
                TakeProfitTriggered: type?.StartsWith("Take Profit", StringComparison.Ordinal) == true,
                Timestamp: at, RealizedPnL: pnl, Reason: reason));
        }

        /// <summary>
        /// A terminal status word and, unless the user asked for it, why, in words to speak.
        /// Recorded live 2026-11-26: open, filled, canceled, badAloPxRejected. The rest are the
        /// venue's documented words.
        /// </summary>
        internal static (OrderStatus Status, string? Reason) Map(string status) => status switch
        {
            "filled"                   => (OrderStatus.Filled, null),
            "canceled"                 => (OrderStatus.Cancelled, null),
            "marginCanceled"           => (OrderStatus.Cancelled, "not enough margin"),
            "reduceOnlyCanceled"       => (OrderStatus.Cancelled, "it was reduce-only and the position it reduced has closed"),
            "siblingFilledCanceled"    => (OrderStatus.Cancelled, "the other leg of its stop and target filled"),
            "selfTradeCanceled"        => (OrderStatus.Cancelled, "it would have traded with your own order"),
            "openInterestCapCanceled"  => (OrderStatus.Cancelled, "the market reached its open interest cap"),
            "delistedCanceled"         => (OrderStatus.Cancelled, "the market was delisted"),
            "liquidatedCanceled"       => (OrderStatus.Cancelled, "the position was liquidated"),
            "vaultWithdrawalCanceled"  => (OrderStatus.Cancelled, "a vault withdrawal"),
            "scheduledCancel"          => (OrderStatus.Cancelled, "your scheduled cancel ran"),
            "tickRejected"             => (OrderStatus.Rejected, "the price is not a whole number of ticks"),
            "minTradeNtlRejected"      => (OrderStatus.Rejected, "it is under the 10 dollar minimum order value"),
            "perpMarginRejected"       => (OrderStatus.Rejected, "not enough margin"),
            "reduceOnlyRejected"       => (OrderStatus.Rejected, "it is reduce-only and would have added to the position"),
            "badAloPxRejected"         => (OrderStatus.Rejected, "it is post-only and would have traded at once"),
            "iocCancelRejected"        => (OrderStatus.Rejected, "it is immediate-or-cancel and nothing on the book matched it"),
            "badTriggerPxRejected"     => (OrderStatus.Rejected, "the trigger price is on the wrong side of the market"),
            "marketOrderNoLiquidityRejected" => (OrderStatus.Rejected, "there was nothing on the book to fill a market order"),
            "positionIncreaseAtOpenInterestCapRejected" or "positionFlipAtOpenInterestCapRejected"
                or "tooAggressiveAtOpenInterestCapRejected" or "openInterestIncreaseRejected"
                                       => (OrderStatus.Rejected, "the market is at its open interest cap"),
            "insufficientSpotBalanceRejected" => (OrderStatus.Rejected, "not enough spot balance"),
            "oracleRejected"           => (OrderStatus.Rejected, "the price is too far from the oracle price"),
            "perpMaxPositionRejected"  => (OrderStatus.Rejected, "the position would pass the market's largest allowed size"),
            _ when status.EndsWith("Rejected", StringComparison.Ordinal)
                                       => (OrderStatus.Rejected, $"Hyperliquid said {status}"),
            _ when status.EndsWith("Canceled", StringComparison.Ordinal)
                                       => (OrderStatus.Cancelled, $"Hyperliquid said {status}"),
            _                          => (OrderStatus.Unknown, status),
        };

        private void Prune()
        {
            if (_fills.Count + _orders.Count < 2000) return;
            var cutoff = DateTime.UtcNow - Forget;
            foreach (var k in _fills.Where(kv => kv.Value.Seen < cutoff).Select(kv => kv.Key).ToList()) _fills.Remove(k);
            foreach (var k in _orders.Where(kv => kv.Value.Seen < cutoff).Select(kv => kv.Key).ToList()) _orders.Remove(k);
        }

        /// <summary>
        /// A size worked out by subtraction, cleaned of binary leftovers: 0.0541 - 0.05365 is
        /// 0.0004499999999999962 in doubles, and was spoken that way in the live check. Sizes on
        /// the venue have at most 8 decimals.
        /// </summary>
        internal static double Q(double size) => Math.Round(size, 10);

        private static string Id(long oid) => oid.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static DateTime Time(JToken? ms) =>
            ms?.Type == JTokenType.Integer ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value<long>()).UtcDateTime : DateTime.UtcNow;
    }
}
