using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Simulated broker for paper trading. Implements <see cref="ITradingProvider"/>
    /// so the order-execution service can route to it transparently when paper mode
    /// is on. Fills are driven by the <b>real-time live price feed</b> of the
    /// currently-loaded chart (via <see cref="IWorkspaceStore"/>): market orders
    /// fill at the live price; limit / stop / take-profit orders rest and fill when
    /// live price action crosses their trigger. It emits the same
    /// <see cref="OrderUpdate"/> records as a real provider, so every fill / stop /
    /// take-profit announcement works unchanged.
    ///
    /// v1 simplifications (intentional): cash-flow (spot-style) accounting in a
    /// single quote currency; leverage is recorded and reported but not used to
    /// reduce required cash; no partial fills; fills assume the trigger price with
    /// no slippage. Resting orders only fill while their symbol is the loaded chart.
    /// </summary>
    public interface IPaperTradingProvider : ITradingProvider
    {
        /// <summary>Wipe the paper account back to the starting balance.</summary>
        void ResetAccount();

        /// <summary>The quote-currency balance the account resets to.</summary>
        double StartingBalance { get; }

        /// <summary>
        /// Charts the account has an open position or a resting order on. The
        /// monitoring service watches these regardless of which tabs are open, so
        /// a position the user has navigated away from still fills and still
        /// reports.
        /// </summary>
        IReadOnlyList<ChartIdentity> ExposedIdentities();

        /// <summary>Feed one bar of any symbol into the fill engine.</summary>
        void ProcessBar(string symbol, Ohlcv bar);
    }

    public sealed class PaperTradingProvider : IPaperTradingProvider, IDisposable
    {
        private const string Quote = "USDT";
        public double StartingBalance => 100_000.0;

        private readonly IWorkspaceStore _store;
        private readonly ILogger<PaperTradingProvider> _logger;
        private readonly string _statePath;
        private readonly object _lock = new();
        private readonly IDisposable _priceSub;
        private readonly IDisposable? _monitorSub;

        private readonly Subject<OrderUpdate> _orderUpdates = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdates.AsObservable();

        private const double FeeRate = 0.0004;   // simulated 0.04% taker fee per fill
        private double _cash;
        // Last price seen for each symbol, from the focused chart or a background
        // monitor. Deliberately NOT persisted: a price restored from disk would be
        // stale by an unknown amount and would price positions off it.
        private readonly Dictionary<string, double> _lastPrice = new(StringComparer.OrdinalIgnoreCase);
        // The chart each traded symbol was traded under, so exposure can be priced
        // after its tab is gone. Persisted, unlike _lastPrice.
        private readonly Dictionary<string, ChartIdentity> _exposureIdentity = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (double Qty, double Avg)> _positions = new();
        private readonly Dictionary<string, double> _leverage = new();
        private readonly List<PaperOrder> _open = new();
        private readonly List<TradeFill> _history = new();   // newest first, capped

        public PaperTradingProvider(IWorkspaceStore store, IPlatformPathService paths,
            ILogger<PaperTradingProvider> logger, IEventBus? eventBus = null)
        {
            _store = store;
            _logger = logger;
            _statePath = Path.Combine(paths.AppDataDirectory, "paper_account.json");
            Load();

            // Drive resting-order fills off the live chart state. StateStream emits
            // on every data change, including each live tick into the forming bar.
            _priceSub = _store.StateStream.Subscribe(OnState);

            // …and off background monitors, which are the only price source for a
            // symbol whose tab is not the focused one. Without this the engine was
            // blind to every chart but the one on screen.
            _monitorSub = eventBus?.Subscribe<MonitoredBarEvent>(
                e => ProcessBar(e.Identity.Symbol ?? "", e.Latest));
        }

        // ── IProviderPlugin ───────────────────────────────────────────────────
        public string Name => "Paper";
        public string Description => "Paper trading (simulated)";

        /// <summary>
        /// Only what the broker actually does — and everything it does.
        ///
        /// <para>
        /// It previously declared <c>Leverage</c> and <c>Shorting</c> while
        /// funding every position from cash at 1× and having no borrow at all, so
        /// the dashboard offered a leverage selector that changed nothing and a
        /// sell side that minted money. Margin, shorting and futures return
        /// together in the margin rewrite; until then the flags say what is true.
        /// </para>
        ///
        /// <para>
        /// The omission cut the other way too: <c>TrailingStop</c> was never
        /// declared even though trailing stops and trailing take-profits are
        /// fully simulated, armed, persisted and tested here. The dashboard gates
        /// its trailing fields on that flag, so a complete feature was
        /// unreachable in paper mode. A capability list is a claim in both
        /// directions, and <c>PaperCapabilityConformanceTests</c> now checks both.
        /// </para>
        /// </summary>
        public ProviderCapabilities Capabilities =>
            ProviderCapabilities.Brackets | ProviderCapabilities.OCO | ProviderCapabilities.TrailingStop;
        public T? GetCapability<T>() where T : class => this as T;

        // ── ITradingProvider flags ────────────────────────────────────────────
        public bool IsConnected => true;
        public bool SupportsMarginTrading => false;
        public bool SupportsFuturesTrading => false;
        public double MaxLeverage => 1.0;

        // ── Account queries ───────────────────────────────────────────────────

        /// <summary>
        /// Quote cash plus every asset the account holds. On a spot account a
        /// position IS a balance — reporting only the cash left half the account
        /// invisible on the Balances tab, which is the account view the hosted
        /// demo leads with.
        /// </summary>
        public Task<List<Balance>> GetBalancesAsync()
        {
            lock (_lock)
            {
                var list = new List<Balance> { new Balance(Quote, _cash, 0) };
                foreach (var kv in _positions.Where(kv => Math.Abs(kv.Value.Qty) > 1e-12))
                {
                    var pair = SymbolAssets.Split(kv.Key);
                    // An unsplittable symbol would name the wrong asset beside a
                    // number, which is worse than omitting the row.
                    if (!pair.Recognised || pair.Base.Length == 0) continue;

                    // One asset can back several pairs (BTC/USDT and BTC/USD are
                    // both BTC), so holdings accumulate into a single row.
                    int at = list.FindIndex(b => b.Asset == pair.Base);
                    if (at >= 0) list[at] = list[at] with { Free = list[at].Free + kv.Value.Qty };
                    else         list.Add(new Balance(pair.Base, kv.Value.Qty, 0));
                }
                return Task.FromResult(list);
            }
        }

        public Task<List<Position>> GetPositionsAsync()
        {
            lock (_lock)
            {
                var list = _positions
                    .Where(kv => Math.Abs(kv.Value.Qty) > 1e-12)
                    .Select(kv =>
                    {
                        double price = PriceFor(kv.Key, kv.Value.Avg);
                        double lev = _leverage.TryGetValue(kv.Key, out var l) ? l : 1.0;
                        return new Position(
                            kv.Key, kv.Value.Qty, kv.Value.Avg,
                            kv.Value.Qty * price,
                            (price - kv.Value.Avg) * kv.Value.Qty,
                            lev, 0);
                    })
                    .ToList();
                return Task.FromResult(list);
            }
        }

        public Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            lock (_lock)
            {
                var list = _open
                    .Where(o => symbol == null || string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    .Select(o => new OpenOrder(o.Id, o.Symbol, o.Side, o.Type, o.Quantity, o.Price ?? o.Trigger ?? 0, "NEW"))
                    .ToList();
                return Task.FromResult(list);
            }
        }

        public Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            lock (_lock)
            {
                var list = _history
                    .Where(f => symbol == null || string.Equals(f.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    .Take(limit)
                    .ToList();
                return Task.FromResult(list);
            }
        }

        // ── Order management ──────────────────────────────────────────────────

        public Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            string symbol = (signal.Symbol ?? "").ToUpperInvariant();
            if (signal.Quantity <= 0) return Task.FromResult("ORDER_FAILED:quantity must be positive");

            lock (_lock)
            {
                if (signal.Leverage is > 1) _leverage[symbol] = Math.Clamp(signal.Leverage.Value, 1, MaxLeverage);

                // Remember which chart this symbol trades on, so the monitoring
                // service can keep pricing it after the tab is closed.
                var live = _store.State.Identity;
                if (string.Equals(live.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    _exposureIdentity[symbol] = live;

                if (signal.Type == OrderType.Market)
                {
                    double px = PriceFor(symbol, 0);
                    if (px <= 0) return Task.FromResult("ORDER_FAILED:no live price for symbol — load its chart first");
                    if (!CanFill(symbol, signal.Side, signal.Quantity, px, out string? why))
                    {
                        Emit(NewId(), symbol, signal.Side, 0, 0, signal.Quantity, OrderStatus.Rejected, false, false, reason: why);
                        return Task.FromResult("ORDER_FAILED:" + why);
                    }

                    string id = NewId();
                    var pnl = ApplyFill(symbol, signal.Side, signal.Quantity, px);
                    Emit(id, symbol, signal.Side, signal.Quantity, px, 0, OrderStatus.Filled, false, false, pnl);
                    RecordFill(symbol, signal.Side, signal.Quantity, px, pnl, id);

                    // Attach protective resting orders (reduce-only by nature here).
                    //
                    // Both legs share one OCO group. A bracket IS a one-cancels-other
                    // pair whether or not the caller supplied a group id, and without
                    // it the surviving leg outlived the position it was protecting:
                    // the stop closed the trade, the target stayed on the book, and
                    // the next rally filled it into a short nobody opened.
                    var exit = signal.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                    string bracket = signal.OcoGroupId ?? "bracket-" + id;
                    if (signal.TrailStopValue is > 0 && signal.TrailStopMode != null)
                        _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.StopMarket, signal.Quantity, null, px, true, false,
                            signal.TrailStopMode, signal.TrailStopValue, px, ocoGroupId: bracket));    // trailing stop anchored at entry
                    else if (signal.StopLoss is > 0)
                        _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.StopMarket, signal.Quantity, null, signal.StopLoss, true, false,
                            ocoGroupId: bracket));
                    if (signal.TakeProfit is > 0)
                        _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.TakeProfitMarket, signal.Quantity, null, signal.TakeProfit, false, true,
                            ocoGroupId: bracket));
                    if (signal.TrailTpValue is > 0 && signal.TrailTpMode != null)
                        _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.TakeProfitMarket, signal.Quantity, null, null, false, true,
                            signal.TrailTpMode, signal.TrailTpValue, null, signal.TrailTpActivation, armed: signal.TrailTpActivation == null,
                            ocoGroupId: bracket));  // trailing take-profit

                    Persist();
                    return Task.FromResult(id);
                }

                // Resting limit / stop / take-profit order.
                double? price = signal.Type is OrderType.Limit or OrderType.StopLimit or OrderType.TakeProfitLimit ? signal.Price : null;
                double? trigger = signal.Type switch
                {
                    OrderType.StopMarket or OrderType.StopLimit => signal.TriggerPrice ?? signal.StopLoss ?? signal.Price,
                    OrderType.TakeProfitMarket or OrderType.TakeProfitLimit => signal.TriggerPrice ?? signal.TakeProfit ?? signal.Price,
                    OrderType.Limit => signal.Price,
                    _ => null
                };
                if (price == null && trigger == null)
                    return Task.FromResult("ORDER_FAILED:order needs a price or trigger");

                bool isStop = signal.Type is OrderType.StopMarket or OrderType.StopLimit;
                bool isTp   = signal.Type is OrderType.TakeProfitMarket or OrderType.TakeProfitLimit;
                string oid = NewId();
                _open.Add(new PaperOrder(oid, symbol, signal.Side, signal.Type, signal.Quantity, price, trigger, isStop, isTp,
                    ocoGroupId: signal.OcoGroupId));
                Persist();
                return Task.FromResult(oid);
            }
        }

        /// <summary>One-cancels-other: after <paramref name="filled"/> executes (or is
        /// cancelled), every other open order sharing its OcoGroupId is cancelled with
        /// its own Cancelled update. Caller holds _lock.</summary>
        private void CancelOcoSiblings(PaperOrder filled)
        {
            if (filled.OcoGroupId == null) return;
            var siblings = _open
                .Where(s => s.OcoGroupId == filled.OcoGroupId && s.Id != filled.Id)
                .ToList();
            foreach (var s in siblings)
            {
                _open.Remove(s);
                Emit(s.Id, s.Symbol, s.Side, 0, 0, s.Quantity, OrderStatus.Cancelled, false, false);
            }
        }

        public Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            lock (_lock)
            {
                var o = _open.FirstOrDefault(x => x.Id == orderId);
                if (o == null) return Task.FromResult(false);
                _open.Remove(o);
                Emit(o.Id, o.Symbol, o.Side, 0, 0, o.Quantity, OrderStatus.Cancelled, false, false);
                CancelOcoSiblings(o); // cancelling one OCO leg cancels the pair
                Persist();
                return Task.FromResult(true);
            }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            double lev = Math.Clamp(leverage, 1, MaxLeverage);
            lock (_lock) _leverage[symbol.ToUpperInvariant()] = lev;
            return Task.FromResult(lev);
        }

        public void ResetAccount()
        {
            lock (_lock)
            {
                _cash = StartingBalance;
                _positions.Clear();
                _open.Clear();
                _leverage.Clear();
                _history.Clear();
                _exposureIdentity.Clear();
                _lastPrice.Clear();
                Persist();
            }
        }

        // ── Live-price fill engine ────────────────────────────────────────────

        private void OnState(WorkspaceState st)
        {
            var data = st.Data;
            string sym = (st.Identity.Symbol ?? "").ToUpperInvariant();
            if (data == null || data.Count == 0 || sym.Length == 0) return;
            ProcessBar(sym, data[data.Count - 1]);
        }

        /// <summary>
        /// Drive the fill engine from one bar of any symbol, focused chart or not.
        ///
        /// <para>
        /// The engine used to be reachable only from the focused chart's state
        /// stream, so a resting order in another tab could never fill and an open
        /// position there reported a frozen entry price as its market price.
        /// Background monitors already fetch bars for unfocused tabs; this is the
        /// entry point that lets those bars count.
        /// </para>
        /// </summary>
        public void ProcessBar(string symbol, Ohlcv bar)
        {
            string sym = (symbol ?? "").ToUpperInvariant();
            if (sym.Length == 0) return;

            lock (_lock)
            {
                // Remember the price even when nothing fills: it is what makes
                // unrealized P&L live for a position whose chart is not on screen.
                _lastPrice[sym] = bar.Close;

                // Advance trailing stops first so this tick uses the moved trigger.
                bool trailMoved = false;
                foreach (var o in _open)
                    if (o.Trail != null && string.Equals(o.Symbol, sym, StringComparison.OrdinalIgnoreCase))
                        trailMoved |= UpdateTrail(o, bar);

                var fills = _open.Where(o => string.Equals(o.Symbol, sym, StringComparison.OrdinalIgnoreCase) && Crossed(o, bar)).ToList();
                if (fills.Count == 0)
                {
                    if (trailMoved) Persist();
                    return;
                }
                foreach (var o in fills)
                {
                    // A wide/gapping bar can cross both legs of an OCO pair in one tick. When
                    // the first leg fills it cancels its sibling (CancelOcoSiblings removes it
                    // from _open), so skip any order a prior iteration already removed —
                    // otherwise the sibling would ALSO fill (double position + fee) and then
                    // get a spurious Cancelled. _open.Remove returns false when already gone.
                    if (!_open.Remove(o)) continue;
                    double px = o.Trigger ?? o.Price ?? bar.Close;

                    // Affording an order when it was placed does not mean affording it
                    // when it triggers — the cash can be spent, or the position sold,
                    // while the order rests. Filling regardless drove the account cash
                    // negative and sold assets it no longer held.
                    if (!CanFill(o.Symbol, o.Side, o.Quantity, px, out string? why))
                    {
                        Emit(o.Id, o.Symbol, o.Side, 0, 0, o.Quantity, OrderStatus.Rejected, o.IsStop, o.IsTp, reason: why);
                        continue;
                    }

                    var pnl = ApplyFill(o.Symbol, o.Side, o.Quantity, px);
                    Emit(o.Id, o.Symbol, o.Side, o.Quantity, px, 0, OrderStatus.Filled, o.IsStop, o.IsTp, pnl, o.Trail != null);
                    RecordFill(o.Symbol, o.Side, o.Quantity, px, pnl, o.Id);
                    CancelOcoSiblings(o);
                }
                Persist();
            }
        }

        private static bool Crossed(PaperOrder o, Ohlcv bar)
        {
            // Trailing exits (stop or take-profit) fire on a reversal to the
            // trailing trigger, regardless of stop/TP labelling — and only once
            // armed with a computed trigger.
            if (o.Trail != null)
                return o.Armed && o.Trigger != null &&
                       (o.Side == OrderSide.Buy ? bar.High >= o.Trigger : bar.Low <= o.Trigger);

            return o.Type switch
            {
                OrderType.Limit
                    => o.Side == OrderSide.Buy ? bar.Low <= o.Price : bar.High >= o.Price,
                OrderType.StopMarket or OrderType.StopLimit
                    => o.Side == OrderSide.Buy ? bar.High >= o.Trigger : bar.Low <= o.Trigger,
                OrderType.TakeProfitMarket or OrderType.TakeProfitLimit
                    => o.Side == OrderSide.Buy ? bar.Low <= o.Trigger : bar.High >= o.Trigger,
                _ => false
            };
        }

        // Advance a trailing stop's anchor toward the favourable extreme and
        // recompute its trigger. Returns true if the anchor moved this tick.
        private static bool UpdateTrail(PaperOrder o, Ohlcv bar)
        {
            if (o.Trail == null || o.TrailValue == null) return false;

            // A trailing take-profit stays dormant until price reaches its
            // activation level, then arms and begins trailing from there.
            if (!o.Armed)
            {
                bool reached = o.Activation == null ||
                    (o.Side == OrderSide.Sell ? bar.High >= o.Activation.Value : bar.Low <= o.Activation.Value);
                if (!reached) return false;
                o.Armed = true;
                o.TrailAnchor = o.Side == OrderSide.Sell ? bar.High : bar.Low;
            }

            double Dist(double anchor) => o.Trail == TrailMode.Amount ? o.TrailValue.Value : anchor * o.TrailValue.Value / 100.0;
            if (o.Side == OrderSide.Sell)   // protecting a long: trail up
            {
                double anchor = Math.Max(o.TrailAnchor ?? bar.High, bar.High);
                bool moved = o.TrailAnchor is null || anchor > o.TrailAnchor.Value;
                o.TrailAnchor = anchor;
                o.Trigger = anchor - Dist(anchor);
                return moved;
            }
            else                            // protecting a short: trail down
            {
                double anchor = Math.Min(o.TrailAnchor ?? bar.Low, bar.Low);
                bool moved = o.TrailAnchor is null || anchor < o.TrailAnchor.Value;
                o.TrailAnchor = anchor;
                o.Trigger = anchor + Dist(anchor);
                return moved;
            }
        }

        // ── Order validation (caller holds _lock) ─────────────────────────────

        /// <summary>
        /// Whether the account can actually settle this fill, with the reason in
        /// spoken words when it cannot.
        ///
        /// <para>
        /// The paper broker settles spot-style out of one cash pool and has no
        /// borrow, so there are exactly two ways an order is impossible: not
        /// enough quote currency to buy, or not enough of the asset to sell.
        /// Both were previously unchecked on at least one path, which let a sell
        /// with no position credit cash for an asset that had never been owned.
        /// </para>
        ///
        /// <para>
        /// This is a refusal of the <i>impossible</i>, not a refusal on taste —
        /// a real spot venue rejects both of these too.
        /// </para>
        /// </summary>
        private bool CanFill(string symbol, OrderSide side, double qty, double price, out string? reason)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var pair = SymbolAssets.Split(symbol);
            string asset = pair.Recognised && pair.Base.Length > 0 ? pair.Base : symbol;

            if (side == OrderSide.Buy)
            {
                // The numbers, not just the verdict. A risk-sized crypto position on a tight
                // stop routinely asks for several times the account in notional, and
                // "insufficient balance" alone leaves the user guessing by how much.
                double needed = qty * price;
                if (_cash + 1e-9 < needed)
                {
                    reason = $"insufficient paper balance — that position needs {needed.ToString("N2", ci)} {Quote} "
                           + $"and the account holds {_cash.ToString("N2", ci)}";
                    return false;
                }
                reason = null;
                return true;
            }

            double held = _positions.TryGetValue(symbol, out var p) ? p.Qty : 0.0;
            if (held + 1e-9 < qty)
            {
                reason = held <= 1e-12
                    ? $"cannot sell {qty.ToString("0.########", ci)} {asset} — the account holds none. "
                    + "Paper trading is spot only; short selling arrives with margin support"
                    : $"cannot sell {qty.ToString("0.########", ci)} {asset} — the account holds only "
                    + $"{held.ToString("0.########", ci)}";
                return false;
            }
            reason = null;
            return true;
        }

        // ── Account mutation (caller holds _lock) ─────────────────────────────

        // Returns realized P&L (quote currency) for the portion of this fill that
        // reduces an existing position; null when it only opens/adds.
        private double? ApplyFill(string symbol, OrderSide side, double qty, double price)
        {
            var pos = _positions.TryGetValue(symbol, out var p) ? p : (Qty: 0.0, Avg: 0.0);
            double signed = side == OrderSide.Buy ? qty : -qty;
            double newQty = pos.Qty + signed;

            // Spot-style cash flow: buying spends quote, selling returns it.
            _cash += (side == OrderSide.Buy ? -1 : 1) * qty * price;

            double? realized = null;
            if (pos.Qty != 0 && Math.Sign(signed) != Math.Sign(pos.Qty))
            {
                double closedQty = Math.Min(Math.Abs(signed), Math.Abs(pos.Qty));
                realized = pos.Qty > 0 ? (price - pos.Avg) * closedQty : (pos.Avg - price) * closedQty;
            }

            if (Math.Abs(newQty) < 1e-12)
            {
                _positions.Remove(symbol);
                return realized;
            }
            double avg;
            if (pos.Qty == 0 || Math.Sign(newQty) != Math.Sign(pos.Qty))
                avg = price;                                   // new or flipped position
            else if (Math.Sign(signed) == Math.Sign(pos.Qty))
                avg = (pos.Avg * Math.Abs(pos.Qty) + price * qty) / Math.Abs(newQty); // adding
            else
                avg = pos.Avg;                                 // reducing — keep basis
            _positions[symbol] = (newQty, avg);
            return realized;
        }

        // Apply a simulated taker fee to a fill and log it to the (capped) history.
        private void RecordFill(string symbol, OrderSide side, double qty, double price, double? realized, string orderId)
        {
            double fee = qty * price * FeeRate;
            _cash -= fee;
            _history.Insert(0, new TradeFill(NewId(), symbol, side, qty, price, DateTime.UtcNow, fee, orderId, realized ?? 0));
            if (_history.Count > 200) _history.RemoveRange(200, _history.Count - 200);
        }

        /// <summary>
        /// The most recent price known for a symbol: the focused chart's live bar
        /// when it is that symbol, otherwise the last bar any background monitor
        /// reported for it. The fallback is only reached for a symbol nothing has
        /// ever priced.
        /// </summary>
        private double PriceFor(string symbol, double fallback)
        {
            var st = _store.State;
            if (st.Data != null && st.Data.Count > 0 &&
                string.Equals(st.Identity.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                return st.Data[st.Data.Count - 1].Close;
            return _lastPrice.TryGetValue(symbol.ToUpperInvariant(), out double p) && p > 0 ? p : fallback;
        }

        /// <summary>
        /// Every chart the account has money riding on — an open position or a
        /// resting order — as the identity it was traded under.
        ///
        /// <para>
        /// The identity is recorded at order time and persisted, so exposure
        /// outlives the tab: closing the chart, or reopening the app the next day,
        /// does not strand a position without a price. That is the whole point —
        /// the position you forget about is the one that needs watching, and it is
        /// the one least likely to still have a tab.
        /// </para>
        /// </summary>
        public IReadOnlyList<ChartIdentity> ExposedIdentities()
        {
            lock (_lock)
            {
                var symbols = _positions.Where(kv => Math.Abs(kv.Value.Qty) > 1e-12).Select(kv => kv.Key)
                    .Concat(_open.Select(o => o.Symbol.ToUpperInvariant()))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                var list = new List<ChartIdentity>();
                foreach (string s in symbols)
                    if (_exposureIdentity.TryGetValue(s, out var id) && !string.IsNullOrWhiteSpace(id.Symbol))
                        list.Add(id);
                return list.Distinct().ToList();
            }
        }

        private void Emit(string id, string symbol, OrderSide side, double filledQty, double filledPx, double remaining, OrderStatus status, bool stop, bool tp, double? pnl = null, bool trailing = false, string? reason = null)
            => _orderUpdates.OnNext(new OrderUpdate(id, symbol, side, filledQty, filledPx, remaining, status, stop, tp, DateTime.UtcNow, pnl, trailing, reason));

        private static string NewId() => "paper-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        // ── Persistence (caller holds _lock for Persist) ──────────────────────

        private void Persist()
        {
            try
            {
                var dto = new PaperDto
                {
                    Cash = _cash,
                    Positions = _positions.Select(kv => new PosDto { Symbol = kv.Key, Qty = kv.Value.Qty, Avg = kv.Value.Avg }).ToList(),
                    Leverage = _leverage.Select(kv => new LevDto { Symbol = kv.Key, Value = kv.Value }).ToList(),
                    Open = _open.Select(o => new OrderDto
                    {
                        Id = o.Id, Symbol = o.Symbol, Side = o.Side.ToString(), Type = o.Type.ToString(),
                        Quantity = o.Quantity, Price = o.Price, Trigger = o.Trigger, Stop = o.IsStop, Tp = o.IsTp,
                        Trail = o.Trail?.ToString(), TrailValue = o.TrailValue, TrailAnchor = o.TrailAnchor,
                        Activation = o.Activation, Armed = o.Armed, Oco = o.OcoGroupId
                    }).ToList(),
                    History = _history.ToList(),
                    Charts = _exposureIdentity.Select(kv => new IdentDto
                    {
                        Symbol = kv.Key, Market = kv.Value.Market,
                        Provider = kv.Value.Provider, Timeframe = kv.Value.Timeframe
                    }).ToList()
                };
                AtomicFile.WriteAllText(_statePath, JsonConvert.SerializeObject(dto, Formatting.Indented));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Paper account persist failed."); }
        }

        private void Load()
        {
            _cash = StartingBalance;
            try
            {
                if (!File.Exists(_statePath)) return;
                var dto = JsonConvert.DeserializeObject<PaperDto>(File.ReadAllText(_statePath));
                if (dto == null) return;
                _cash = dto.Cash;
                foreach (var p in dto.Positions) _positions[p.Symbol] = (p.Qty, p.Avg);
                foreach (var l in dto.Leverage) _leverage[l.Symbol] = l.Value;
                foreach (var o in dto.Open)
                    _open.Add(new PaperOrder(o.Id, o.Symbol,
                        Enum.TryParse<OrderSide>(o.Side, out var s) ? s : OrderSide.Buy,
                        Enum.TryParse<OrderType>(o.Type, out var t) ? t : OrderType.Market,
                        o.Quantity, o.Price, o.Trigger, o.Stop, o.Tp,
                        Enum.TryParse<TrailMode>(o.Trail, out var tm) ? tm : (TrailMode?)null, o.TrailValue, o.TrailAnchor,
                        o.Activation, o.Armed, o.Oco));
                if (dto.History != null) _history.AddRange(dto.History);
                foreach (var c in dto.Charts ?? new List<IdentDto>())
                    _exposureIdentity[c.Symbol] = new ChartIdentity(c.Market, c.Provider, c.Symbol, c.Timeframe);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Paper account load failed; starting fresh.");
                CorruptFileQuarantine.MoveAside(_statePath, ex);
                _cash = StartingBalance;
            }
        }

        public void Dispose()
        {
            _monitorSub?.Dispose();
            _priceSub.Dispose();
            _orderUpdates.OnCompleted();
            _orderUpdates.Dispose();
        }

        // ── Internal records ──────────────────────────────────────────────────

        private sealed class PaperOrder
        {
            public string Id { get; }
            public string Symbol { get; }
            public OrderSide Side { get; }
            public OrderType Type { get; }
            public double Quantity { get; }
            public double? Price { get; }
            public double? Trigger { get; set; }        // mutable: a trailing stop moves it
            public bool IsStop { get; }
            public bool IsTp { get; }
            public TrailMode? Trail { get; }
            public double? TrailValue { get; }
            public double? TrailAnchor { get; set; }     // mutable high/low-water mark
            public double? Activation { get; }           // trailing TP arms when price reaches this
            public bool Armed { get; set; }              // mutable: whether trailing is active yet
            public string? OcoGroupId { get; }           // one-cancels-other pair membership

            public PaperOrder(string id, string symbol, OrderSide side, OrderType type, double quantity,
                double? price, double? trigger, bool isStop, bool isTp,
                TrailMode? trail = null, double? trailValue = null, double? trailAnchor = null,
                double? activation = null, bool armed = true, string? ocoGroupId = null)
            {
                Id = id; Symbol = symbol; Side = side; Type = type; Quantity = quantity;
                Price = price; Trigger = trigger; IsStop = isStop; IsTp = isTp;
                Trail = trail; TrailValue = trailValue; TrailAnchor = trailAnchor;
                Activation = activation; Armed = armed; OcoGroupId = ocoGroupId;
            }
        }

        private sealed class PaperDto
        {
            public double Cash { get; set; }
            public List<PosDto> Positions { get; set; } = new();
            public List<LevDto> Leverage { get; set; } = new();
            public List<OrderDto> Open { get; set; } = new();
            public List<TradeFill> History { get; set; } = new();
            public List<IdentDto> Charts { get; set; } = new();
        }
        private sealed class PosDto { public string Symbol { get; set; } = ""; public double Qty { get; set; } public double Avg { get; set; } }
        private sealed class IdentDto
        {
            public string Symbol { get; set; } = "";
            public string Market { get; set; } = "";
            public string Provider { get; set; } = "";
            public string Timeframe { get; set; } = "";
        }
        private sealed class LevDto { public string Symbol { get; set; } = ""; public double Value { get; set; } }
        private sealed class OrderDto
        {
            public string Id { get; set; } = "";
            public string Symbol { get; set; } = "";
            public string Side { get; set; } = "Buy";
            public string Type { get; set; } = "Market";
            public double Quantity { get; set; }
            public double? Price { get; set; }
            public double? Trigger { get; set; }
            public bool Stop { get; set; }
            public bool Tp { get; set; }
            public string? Trail { get; set; }
            public double? TrailValue { get; set; }
            public double? TrailAnchor { get; set; }
            public double? Activation { get; set; }
            public bool Armed { get; set; } = true;
            public string? Oco { get; set; }
        }
    }
}
