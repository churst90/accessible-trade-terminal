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
    /// Shorting is simulated at 1× — fully collateralised, so shorting N costs the
    /// same free cash as buying N and the position is liquidated at twice its entry
    /// price. That liquidation is ENFORCED, because a short can go to infinity where
    /// a long can only go to zero, and a paper account that never buys you in teaches
    /// that shorting is free money.
    ///
    /// Remaining simplifications (intentional): a single quote currency; leverage
    /// above 1× is recorded and reported but not used to reduce required margin; no
    /// partial fills; fills assume the trigger price with no slippage; no borrow
    /// interest or funding.
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

        /// <summary>
        /// Quote currency locked against a short: the sale proceeds (which are owed
        /// back as the asset) plus the initial margin. Not spendable, and reported as
        /// <c>Balance.Locked</c> — the field that was hardcoded to zero until shorting
        /// gave it something to mean.
        /// </summary>
        private readonly Dictionary<string, double> _collateral = new();

        /// <summary>
        /// Margin posted on top of the proceeds, as a fraction of notional. 1.0 is
        /// full collateralisation — "1×" — which makes shorting N cost exactly the
        /// same free cash as buying N, and puts liquidation at twice the entry price.
        /// Deliberately more conservative than real venues: a paper account that
        /// teaches thinner margin than reality teaches the wrong lesson, and the
        /// configurable version belongs with the leverage work.
        /// </summary>
        private const double InitialMarginRate = 1.0;
        private readonly Dictionary<string, double> _leverage = new();
        private readonly List<PaperOrder> _open = new();
        private readonly List<TradeFill> _history = new();   // newest first, capped

        /// <summary>
        /// Market-data access, for the two things the ledger cannot do from its own state: ask a
        /// venue what market a symbol really is (<see cref="ResolveLedgerKeyAsync"/>) and fetch a price
        /// for a symbol whose chart is not open (<see cref="ResolvePriceAsync"/>). Optional so the
        /// desktop head and the tests can construct the account without a data layer; both paths
        /// degrade to the behaviour that existed before rather than failing.
        /// </summary>
        private readonly IDataService? _data;

        public PaperTradingProvider(IWorkspaceStore store, IPlatformPathService paths,
            ILogger<PaperTradingProvider> logger, IEventBus? eventBus = null,
            IDataService? dataService = null)
        {
            _store = store;
            _logger = logger;
            _data = dataService;
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
        /// It once declared <c>Leverage</c> and <c>Shorting</c> while funding
        /// every position from cash and having no borrow at all, so the dashboard
        /// offered a leverage selector that changed nothing and a sell side that
        /// minted money. Both were withdrawn. <c>Shorting</c> is back, earned:
        /// shorts are collateralised and liquidated. <c>Leverage</c> stays absent
        /// until margin above 1× is real, and <c>FuturesTrading</c> with it.
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
            ProviderCapabilities.Brackets | ProviderCapabilities.OCO | ProviderCapabilities.TrailingStop |
            ProviderCapabilities.Shorting | ProviderCapabilities.MarginTrading;
        public T? GetCapability<T>() where T : class => this as T;

        // ── ITradingProvider flags ────────────────────────────────────────────
        public bool IsConnected => true;

        // Derived from the flags, matching BaseMarketDataProvider, so this broker
        // cannot state one of these facts in two places and disagree with itself.
        public bool SupportsMarginTrading  => Capabilities.HasFlag(ProviderCapabilities.MarginTrading);
        public bool SupportsFuturesTrading => Capabilities.HasFlag(ProviderCapabilities.FuturesTrading);
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
                // Locked is real at last: quote currency held against open shorts,
                // which is spendable by nobody until the position closes.
                var list = new List<Balance> { new Balance(Quote, _cash, _collateral.Values.Sum()) };
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
                        // Longs have no liquidation price — a spot long simply goes to
                        // zero — so 0 there means "not applicable", not "at zero".
                        double liq = kv.Value.Qty < 0 && CollateralOf(kv.Key) > 0
                            ? CollateralOf(kv.Key) / Math.Abs(kv.Value.Qty)
                            : 0;
                        return new Position(
                            kv.Key, kv.Value.Qty, kv.Value.Avg,
                            kv.Value.Qty * price,
                            (price - kv.Value.Avg) * kv.Value.Qty,
                            lev, liq);
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

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            string raw = (signal.Symbol ?? "").ToUpperInvariant();
            if (signal.Quantity <= 0) return "ORDER_FAILED:quantity must be positive";

            // Resolved BEFORE the lock, because both need the network and no lock may be held
            // across an await.
            //
            //  1. The market this symbol really is on its venue, so one book cannot become two
            //     positions because the user reached it by two spellings.
            //  2. A price, when the symbol's chart is not the one on screen. Without this a
            //     position was unclosable whenever its tab was shut or the server had restarted:
            //     the in-memory price table is deliberately not persisted, so Close returned "no
            //     live price for symbol" and there was no way to act on it from that screen.
            string symbol = await ResolveLedgerKeyAsync(raw).ConfigureAwait(false);

            double marketPrice = 0;
            if (signal.Type == OrderType.Market)
            {
                lock (_lock) marketPrice = PriceFor(symbol, 0);
                if (marketPrice <= 0) marketPrice = await ResolvePriceAsync(symbol, raw).ConfigureAwait(false);
            }

            lock (_lock)
            {
                if (signal.Leverage is > 1) _leverage[symbol] = Math.Clamp(signal.Leverage.Value, 1, MaxLeverage);

                // Remember which chart this symbol trades on, so the monitoring service — and
                // ResolvePriceAsync — can keep pricing it after the tab is closed. Compared
                // canonically: the live identity may spell the same market differently from the
                // signal, and an identity filed under a spelling nothing looks up is an identity
                // that will not be there when a close needs it.
                var live = _store.State.Identity;
                if (!string.IsNullOrEmpty(live.Provider)
                    && string.Equals(LocalCanonical(live.Symbol ?? ""), LocalCanonical(raw),
                                     StringComparison.OrdinalIgnoreCase))
                    _exposureIdentity[symbol] = live;

                if (signal.Type == OrderType.Market)
                {
                    // The price was resolved before the lock: PriceFor first, then the
                    // venue if this symbol's chart is not the one on screen. The refusal
                    // names the symbol and says what to do about it, because the half a
                    // screen-reader user needs is the half the old message threw away.
                    double px = marketPrice;
                    if (px <= 0)
                        return "ORDER_FAILED:no price available for " + raw
                             + " — open its chart, or check the venue is reachable";

                    // Reduce-only means reduce-only whatever the order type. A "close
                    // position" that arrives a moment after the position went away must
                    // not become a fresh trade in the opposite direction.
                    double mktQty = signal.Quantity;
                    if (signal.ReduceOnly)
                    {
                        double held = _positions.TryGetValue(symbol, out var cur) ? cur.Qty : 0.0;
                        int wants = signal.Side == OrderSide.Buy ? 1 : -1;
                        if (Math.Abs(held) < 1e-12 || Math.Sign(held) == wants)
                        {
                            const string gone = "the position was already closed";
                            Emit(NewId(), symbol, signal.Side, 0, 0, signal.Quantity, OrderStatus.Cancelled, false, false, reason: gone);
                            return "ORDER_FAILED:" + gone;
                        }
                        mktQty = Math.Min(signal.Quantity, Math.Abs(held));
                    }

                    if (!CanFill(symbol, signal.Side, mktQty, px, out string? why))
                    {
                        Emit(NewId(), symbol, signal.Side, 0, 0, signal.Quantity, OrderStatus.Rejected, false, false, reason: why);
                        return "ORDER_FAILED:" + why;
                    }

                    string id = NewId();
                    var pnl = ApplyFill(symbol, signal.Side, mktQty, px);
                    Emit(id, symbol, signal.Side, mktQty, px, 0, OrderStatus.Filled, false, false, pnl);
                    RecordFill(symbol, signal.Side, mktQty, px, pnl, id);

                    // A market entry exists the instant it fills, so its protection can
                    // be attached here and now. A resting entry cannot — see below.
                    var spec = signal.ReduceOnly ? null : BracketFrom(signal, stopLossIsEntryTrigger: false);
                    if (spec != null) AttachProtectiveLegs(symbol, signal.Side, mktQty, px, spec, id);

                    Persist();
                    return id;
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
                    return "ORDER_FAILED:order needs a price or trigger";

                bool isStop = signal.Type is OrderType.StopMarket or OrderType.StopLimit;

                // A stop entry on the wrong side of the market is an order to trade
                // at a price the market has already passed: a buy stop below spot
                // triggers on the next bar and fills below the asking price. Nothing
                // else on this path checks the trigger against the live price —
                // GeneralOrderService validates Price and never TriggerPrice — so
                // this is the only guard, and without it the simulator mints money.
                if (isStop && trigger is double t)
                {
                    double spot = PriceFor(symbol, 0);
                    var check = ProtectiveLevelValidator.ValidateStopEntry(
                        signal.Side == OrderSide.Buy, t, spot);
                    if (!check.Ok)
                    {
                        Emit(NewId(), symbol, signal.Side, 0, 0, signal.Quantity,
                            OrderStatus.Rejected, true, false, reason: check.Message);
                        return "ORDER_FAILED:" + check.Message;
                    }
                }

                bool isTp   = signal.Type is OrderType.TakeProfitMarket or OrderType.TakeProfitLimit;
                string oid = NewId();

                // The protection travels with the entry. Before this, the bracket block
                // lived inside the market branch alone, so a limit entry carrying a stop
                // dropped it silently — and the quick-trade flow sizes the position FROM
                // the stop distance, meaning the flagship workflow placed a stop-derived
                // size with no stop on it. The legs cannot be placed yet (there is no
                // position to protect), so the spec rides along until the entry fills.
                //
                // StopLoss is ambiguous here: for a stop entry with no explicit
                // TriggerPrice it was already consumed above as the entry trigger, and
                // reusing it as a protective leg would put the stop exactly at the entry.
                bool stopLossIsEntryTrigger =
                    isStop && signal.TriggerPrice == null && signal.StopLoss != null;
                var bracket = BracketFrom(signal, stopLossIsEntryTrigger);

                _open.Add(new PaperOrder(oid, symbol, signal.Side, signal.Type, signal.Quantity, price, trigger, isStop, isTp,
                    ocoGroupId: signal.OcoGroupId, reduceOnly: signal.ReduceOnly, bracket: bracket));
                Persist();
                return oid;
            }
        }

        /// <summary>
        /// Reads the protective fields off a signal, or null when it carries none.
        /// </summary>
        /// <param name="stopLossIsEntryTrigger">
        /// True when <c>StopLoss</c> was already spent as the entry's own trigger, so it
        /// must not also become a protective leg.
        /// </param>
        private static BracketSpec? BracketFrom(TradeSignal signal, bool stopLossIsEntryTrigger)
        {
            var spec = new BracketSpec(
                stopLossIsEntryTrigger ? null : signal.StopLoss,
                signal.TakeProfit,
                signal.TrailStopMode, signal.TrailStopValue,
                signal.TrailTpMode, signal.TrailTpValue, signal.TrailTpActivation,
                signal.OcoGroupId);
            return spec.Any ? spec : null;
        }

        /// <summary>
        /// Places the stop and target that protect a position that has just been opened.
        ///
        /// <para>
        /// Both legs share one OCO group. A bracket IS a one-cancels-other pair whether
        /// or not the caller supplied a group id, and without it the surviving leg
        /// outlived the position it was protecting: the stop closed the trade, the
        /// target stayed on the book, and the next rally filled it into a short nobody
        /// opened.
        /// </para>
        ///
        /// <para>
        /// **Every leg is reduce-only, and that is not decoration.** The old comment
        /// here called them "reduce-only by nature", which stopped being true the moment
        /// <see cref="Settle"/> learned to turn a sell-with-no-position into a
        /// collateralised short. Close a bracketed long by hand and the stop would later
        /// fire into a brand-new short, cancel its own target, and announce "stop loss
        /// hit" for a trade it had just opened. Reduce-only is what makes an exit an
        /// exit; anything added here must carry it.
        /// </para>
        ///
        /// <para>Caller holds the lock. The trailing anchor is the FILL price, not the
        /// price that was requested.</para>
        /// </summary>
        private void AttachProtectiveLegs(string symbol, OrderSide entrySide, double qty, double entryPx,
            BracketSpec spec, string entryOrderId)
        {
            if (qty <= 1e-12) return;

            var exit = entrySide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            string bracket = spec.OcoGroupId ?? "bracket-" + entryOrderId;

            if (spec.TrailStopValue is > 0 && spec.TrailStopMode != null)
                _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.StopMarket, qty, null, entryPx, true, false,
                    spec.TrailStopMode, spec.TrailStopValue, entryPx, ocoGroupId: bracket,
                    reduceOnly: true));    // trailing stop anchored at the fill
            else if (spec.StopLoss is > 0)
                _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.StopMarket, qty, null, spec.StopLoss, true, false,
                    ocoGroupId: bracket, reduceOnly: true));

            if (spec.TakeProfit is > 0)
                _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.TakeProfitMarket, qty, null, spec.TakeProfit, false, true,
                    ocoGroupId: bracket, reduceOnly: true));

            if (spec.TrailTpValue is > 0 && spec.TrailTpMode != null)
                _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.TakeProfitMarket, qty, null, null, false, true,
                    spec.TrailTpMode, spec.TrailTpValue, null, spec.TrailTpActivation,
                    armed: spec.TrailTpActivation == null, ocoGroupId: bracket,
                    reduceOnly: true));  // trailing take-profit
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
                _collateral.Clear();
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

                // Liquidation before anything else this tick. A short whose collateral
                // is gone is not a position any more, and letting resting orders act on
                // it first would report fills against something that no longer exists.
                LiquidateIfCollateralExhausted(sym, bar);

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
                    double px = FillPrice(o, bar);
                    double qty = o.Quantity;

                    // A reduce-only order may shrink a position and nothing else.
                    //
                    // Protective legs used to be called "reduce-only by nature", which
                    // stopped being true once a sell with no position became a short.
                    // Close a bracketed long from the dashboard — which does not cancel
                    // the legs — and the stop would fire into a NEW short, cancel its own
                    // target, and announce "stop loss hit" for a trade it had just opened.
                    // Partial closes were worse: the legs still carried the original size,
                    // so the remainder flipped straight through flat into a reversal.
                    if (o.ReduceOnly)
                    {
                        double held = _positions.TryGetValue(o.Symbol, out var cur) ? cur.Qty : 0.0;
                        int wants = o.Side == OrderSide.Buy ? 1 : -1;

                        // Flat, or pointing the same way as the position: this can only open.
                        if (Math.Abs(held) < 1e-12 || Math.Sign(held) == wants)
                        {
                            Emit(o.Id, o.Symbol, o.Side, 0, 0, o.Quantity, OrderStatus.Cancelled, o.IsStop, o.IsTp,
                                reason: "the position was already closed");
                            // Its sibling protects nothing either; leaving it resting is
                            // exactly how the surviving leg opens a position of its own.
                            CancelOcoSiblings(o);
                            continue;
                        }

                        qty = Math.Min(o.Quantity, Math.Abs(held));
                    }

                    // Affording an order when it was placed does not mean affording it
                    // when it triggers — the cash can be spent, or the position sold,
                    // while the order rests. Filling regardless drove the account cash
                    // negative and sold assets it no longer held.
                    //
                    // Every one of the four calls below takes `qty`, not o.Quantity. A
                    // clamp applied to only some of them is how this class of bug breeds.
                    if (!CanFill(o.Symbol, o.Side, qty, px, out string? why))
                    {
                        Emit(o.Id, o.Symbol, o.Side, 0, 0, o.Quantity, OrderStatus.Rejected, o.IsStop, o.IsTp, reason: why);
                        continue;
                    }

                    var pnl = ApplyFill(o.Symbol, o.Side, qty, px);
                    Emit(o.Id, o.Symbol, o.Side, qty, px, 0, OrderStatus.Filled, o.IsStop, o.IsTp, pnl, o.Trail != null);
                    RecordFill(o.Symbol, o.Side, qty, px, pnl, o.Id);
                    CancelOcoSiblings(o);

                    // The entry has become a position, so now its protection can exist.
                    // Anchored at the FILL price, which is what a market entry already
                    // did — a trailing stop measured from the requested price would
                    // trail from a number the trade never touched.
                    if (o.Bracket != null && !o.ReduceOnly)
                    {
                        double held = _positions.TryGetValue(o.Symbol, out var np) ? np.Qty : 0.0;
                        AttachProtectiveLegs(o.Symbol, o.Side, Math.Min(qty, Math.Abs(held)), px, o.Bracket, o.Id);
                    }
                }
                Persist();
            }
        }

        /// <summary>
        /// Buys a short back when what it owes reaches what is held against it.
        ///
        /// <para>
        /// **This is the whole reason shorting needed collateral accounting rather
        /// than a permissive sell.** A long can only go to zero; a short can go to
        /// infinity, and liquidation is the mechanism that stops it. A paper account
        /// that lets you short without ever being bought in does not teach shorting —
        /// it teaches that shorting is free money with no ruin risk, which is the
        /// opposite of the truth and worse than not offering it at all.
        /// </para>
        ///
        /// <para>
        /// Uses the bar's HIGH, not its close: the collateral is gone at the moment
        /// price touches the level, and a bar that spiked through and recovered still
        /// liquidated you on a real venue. Caller holds the lock.
        /// </para>
        ///
        /// <para>
        /// **This is the one path that may leave cash negative, and that is
        /// deliberate.** It does not consult <see cref="CanFill"/> — a liquidation
        /// is not an order the account gets to decline — so the buy-back's fee can
        /// take free cash below zero, and <see cref="CanFill"/> will then refuse
        /// every subsequent order. Being wiped out is the lesson; a ruined account
        /// that still accepts trades would teach the opposite. Recovery is
        /// <see cref="ResetAccount"/>, exposed in Settings.
        /// </para>
        /// </summary>
        private void LiquidateIfCollateralExhausted(string symbol, Ohlcv bar)
        {
            if (!_positions.TryGetValue(symbol, out var pos) || pos.Qty >= 0) return;

            double locked = CollateralOf(symbol);
            if (locked <= 0) return;

            double shortQty = Math.Abs(pos.Qty);
            double liqPrice = locked / shortQty;
            if (bar.High < liqPrice) return;

            // Bought back AT the liquidation price: that is where the collateral runs
            // out, and it is what the account is left with either way.
            string id = NewId();
            var pnl = ApplyFill(symbol, OrderSide.Buy, shortQty, liqPrice);
            Emit(id, symbol, OrderSide.Buy, shortQty, liqPrice, 0, OrderStatus.Filled, false, false, pnl,
                reason: $"LIQUIDATED — the short's collateral was exhausted at "
                      + liqPrice.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            RecordFill(symbol, OrderSide.Buy, shortQty, liqPrice, pnl, id);

            // Its protective orders protect nothing now, and leaving them resting
            // would reopen the position on the next touch.
            foreach (var o in _open.Where(o => string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _open.Remove(o);
                Emit(o.Id, o.Symbol, o.Side, 0, 0, o.Quantity, OrderStatus.Cancelled, false, false,
                    reason: "the position was liquidated");
            }

            _logger.LogWarning("Paper short on {Symbol} liquidated at {Price}", symbol, liqPrice);
        }

        /// <summary>
        /// What a crossed resting order actually fills at. The rule — and why it is
        /// not written out here — is in <see cref="BarFill"/>; this only decides
        /// which level and which flavour of order to hand it.
        /// </summary>
        private static double FillPrice(PaperOrder o, Ohlcv bar)
        {
            double level = o.Trigger ?? o.Price ?? bar.Close;

            // Trailing exits trigger stop-wise (on a reversal to the trigger)
            // whether they are labelled stop or take-profit — matching Crossed.
            return BarFill.Price(level, bar.Open, o.Side, stopLike: o.IsStop || o.Trail != null);
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
        /// <summary>
        /// How a fill would settle: what it closes, what it opens, and what that does
        /// to free cash and to locked collateral.
        ///
        /// <para>
        /// **One calculation, used by both the check and the mutation.** A guard that
        /// computes affordability differently from the code that spends the money is
        /// worse than no guard, because it passes the cases it should refuse and
        /// refuses the ones it should pass — and both look like arithmetic bugs
        /// somewhere else entirely.
        /// </para>
        ///
        /// <para>
        /// A fill is split into the part that CLOSES existing exposure and the part
        /// that OPENS new exposure, because the two settle by different rules. A sell
        /// of 2.5 against a long of 1 closes the long at spot and opens a 1.5 short
        /// on collateral.
        /// </para>
        /// </summary>
        private Settlement Settle(string symbol, OrderSide side, double qty, double price)
        {
            var pos = _positions.TryGetValue(symbol, out var p) ? p : (Qty: 0.0, Avg: 0.0);
            double signed = side == OrderSide.Buy ? qty : -qty;

            double closingQty = 0, openingQty = qty;
            if (pos.Qty != 0 && Math.Sign(signed) != Math.Sign(pos.Qty))
            {
                closingQty = Math.Min(qty, Math.Abs(pos.Qty));
                openingQty = qty - closingQty;
            }

            double cash = 0, collateral = 0;

            if (closingQty > 0)
            {
                if (pos.Qty < 0)
                {
                    // Closing a short: the buy-back is funded out of the collateral
                    // that was locked when it opened, not out of free cash. Releasing
                    // proportionally keeps a partial close honest.
                    double release = CollateralOf(symbol) * (closingQty / Math.Abs(pos.Qty));
                    collateral -= release;
                    cash       += release - closingQty * price;
                }
                else
                {
                    cash += closingQty * price;      // ordinary spot sale
                }
            }

            if (openingQty > 0)
            {
                double notional = openingQty * price;
                if (signed < 0)
                {
                    // Opening a short. The proceeds are received and immediately
                    // locked — you owe the asset back — and an equal amount of margin
                    // is locked on top. So shorting N costs N of free cash, exactly
                    // as buying N does, and the position is liquidated at twice its
                    // entry price where the collateral runs out.
                    cash       -= notional;
                    collateral += notional * (1.0 + InitialMarginRate);
                }
                else
                {
                    cash -= notional;                // ordinary spot purchase
                }
            }

            // The taker fee is part of what a fill costs, so it belongs in the same
            // number the affordability check tests. Charged unconditionally, both
            // directions: closing costs the fee too. Anything that spends cash
            // outside this method can overdraw an account that CanFill just cleared.
            double fee = qty * price * FeeRate;
            cash -= fee;

            return new Settlement(closingQty, openingQty, cash, collateral, pos.Qty < 0, fee);
        }

        private readonly record struct Settlement(
            double ClosingQty, double OpeningQty, double CashDelta, double CollateralDelta, bool WasShort,
            double Fee);

        /// <summary>
        /// Whether the account can settle this fill, with the reason in spoken words
        /// when it cannot.
        ///
        /// <para>
        /// There is exactly one way to be unable to settle: free cash would go
        /// negative. Buying spends it, opening a short posts margin from it,
        /// closing either returns it, and the taker fee comes off every one of
        /// them. Everything else — including selling an asset you do not hold,
        /// which is now a short rather than an impossibility — falls out of that
        /// single test, but only because <see cref="Settle"/> is the sole place
        /// cash moves. Keep it that way.
        /// </para>
        /// </summary>
        private bool CanFill(string symbol, OrderSide side, double qty, double price, out string? reason)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var s = Settle(symbol, side, qty, price);

            if (_cash + s.CashDelta + 1e-9 >= 0)
            {
                reason = null;
                return true;
            }

            double needed = -s.CashDelta;
            bool opensShort = s.OpeningQty > 0 && side == OrderSide.Sell;

            // The numbers, not just the verdict — and for a short, the fact that the
            // cost is collateral rather than a purchase, because "insufficient
            // balance" on a SELL reads as nonsense otherwise.
            reason = opensShort
                ? $"insufficient paper balance — shorting that much needs {needed.ToString("N2", ci)} {Quote} "
                + $"of collateral and the account holds {_cash.ToString("N2", ci)}. A short is "
                + "collateralised at 1 times its value here"
                : $"insufficient paper balance — that position needs {needed.ToString("N2", ci)} {Quote} "
                + $"and the account holds {_cash.ToString("N2", ci)}";
            return false;
        }

        // ── Symbol identity and price resolution ─────────────────────────────
        //
        // Both exist because the ledger is one account spanning many venues, and a symbol string
        // alone does not say which market it is. See GetCanonicalSymbol on IMarketDataProvider.

        /// <summary>
        /// Venue-independent normalisation: strip separators, uppercase. The fallback for when no
        /// provider can be resolved, and the comparison used to decide whether two spellings could
        /// possibly be the same market before paying for a lookup.
        /// </summary>
        private static string LocalCanonical(string symbol) =>
            symbol?.Replace("/", "").Replace("-", "").ToUpperInvariant() ?? string.Empty;

        /// <summary>
        /// The ledger key for this order: an EXISTING position's key when one names the same
        /// market, otherwise the symbol exactly as the user spelled it.
        ///
        /// <para>
        /// Two rules, and the tension between them is the whole point. Matching is CANONICAL, so
        /// <c>BTC/USD</c> and <c>BTCUSDT</c> on Bitstamp — one book, because the venue routes
        /// Tether quotes to its USD market — find the same position instead of standing as a long
        /// and a short that offset each other invisibly. Storing keeps the SPELLING, because the
        /// key is what the positions table shows and what speech reads out, and silently renaming
        /// a user's BTC/USD to BTCUSD is a worse bug than the one being fixed.
        /// </para>
        ///
        /// <para>
        /// Existing positions are matched, never renamed. Re-keying a stored account would mean
        /// merging a long against a short, which books realised profit at a price no trade ever
        /// happened at — an accounting guess written into somebody's balance without being asked.
        /// </para>
        /// </summary>
        private async Task<string> ResolveLedgerKeyAsync(string raw)
        {
            IMarketDataProvider? provider = null;
            string? providerName = ProviderForSymbol(raw);
            if (_data != null && !string.IsNullOrEmpty(providerName))
            {
                try { provider = await _data.GetProviderAsync(providerName).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Provider lookup failed for {Provider}.", providerName);
                }
            }

            string Canon(string sym)
            {
                try
                {
                    string c = provider?.GetCanonicalSymbol(sym) ?? "";
                    if (c.Length > 0) return c.ToUpperInvariant();
                }
                catch { /* a venue that cannot answer falls back to the neutral form */ }
                return LocalCanonical(sym);
            }

            string canonical = Canon(raw);
            lock (_lock)
            {
                if (_positions.ContainsKey(raw)) return raw;
                foreach (var key in _positions.Keys)
                    if (string.Equals(Canon(key), canonical, StringComparison.OrdinalIgnoreCase))
                        return key;
            }
            return raw;
        }

        /// <summary>
        /// The data provider a symbol trades under: the chart it was traded on if that is still
        /// recorded, otherwise the chart currently on screen. Null when neither is known.
        /// </summary>
        private string? ProviderForSymbol(string rawSymbol)
        {
            string local = LocalCanonical(rawSymbol);
            lock (_lock)
            {
                foreach (var kv in _exposureIdentity)
                {
                    if (string.Equals(LocalCanonical(kv.Key), local, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(kv.Value.Provider))
                        return kv.Value.Provider;
                }
            }
            var live = _store.State.Identity;
            return string.IsNullOrEmpty(live.Provider) ? null : live.Provider;
        }

        /// <summary>
        /// A price for a symbol whose chart is not loaded, fetched from its venue.
        ///
        /// <para>
        /// <b>Why this is not optional.</b> <see cref="PriceFor"/> knows only the focused chart and
        /// an in-memory table that is deliberately never persisted, so after a restart — or simply
        /// with a different tab in front — every position in any other symbol became impossible to
        /// close: the order was refused for want of a price, and the refusal was the only thing
        /// standing between the user and their own money. A position you cannot close is not a
        /// cache miss; it is a trap.
        /// </para>
        ///
        /// <para>
        /// The recorded identity is TRIED, not trusted. An identity can be internally impossible —
        /// a symbol paired with a venue that does not list it, which is exactly what a workspace
        /// restore produced on 2026-08-21 (BTCUSDT recorded against Bitstamp). So a fetch that
        /// comes back empty falls through to the chart on screen rather than being taken as proof
        /// there is no price, and a recorded identity that fails is dropped so it cannot poison
        /// the next attempt.
        /// </para>
        /// </summary>
        private async Task<double> ResolvePriceAsync(string symbol, string rawSymbol)
        {
            if (_data == null) return 0;

            var attempts = new List<(ChartIdentity Id, bool FromRecord)>();
            lock (_lock)
            {
                foreach (var kv in _exposureIdentity)
                    if (string.Equals(LocalCanonical(kv.Key), LocalCanonical(rawSymbol), StringComparison.OrdinalIgnoreCase))
                        attempts.Add((kv.Value, true));
            }
            var live = _store.State.Identity;
            if (!string.IsNullOrEmpty(live.Provider)) attempts.Add((live, false));

            foreach (var (id, fromRecord) in attempts)
            {
                if (string.IsNullOrEmpty(id.Provider)) continue;
                try
                {
                    string market = string.IsNullOrEmpty(id.Market) ? "Crypto" : id.Market;
                    string tf = string.IsNullOrEmpty(id.Timeframe) ? "1h" : id.Timeframe;
                    var (bars, _) = await _data
                        .FetchOhlcvAsync(id.Provider, new MarketDataRequest(market, rawSymbol, tf, 2))
                        .ConfigureAwait(false);

                    double px = bars is { Count: > 0 } ? bars[^1].Close : 0;
                    if (px > 0)
                    {
                        lock (_lock) _lastPrice[symbol] = px;
                        _logger.LogInformation(
                            "Priced {Symbol} at {Price} from {Provider} for an order with no chart loaded.",
                            rawSymbol, px, id.Provider);
                        return px;
                    }

                    if (fromRecord)
                    {
                        // The venue does not answer for this symbol. Keeping the pairing would make
                        // every later attempt fail the same way, silently.
                        _logger.LogWarning(
                            "Recorded chart identity {Provider}/{Symbol} returned no price; dropping it.",
                            id.Provider, rawSymbol);
                        lock (_lock)
                        {
                            var dead = _exposureIdentity
                                .Where(kv => string.Equals(LocalCanonical(kv.Key), LocalCanonical(rawSymbol),
                                                           StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(kv.Value.Provider, id.Provider, StringComparison.OrdinalIgnoreCase))
                                .Select(kv => kv.Key).ToList();
                            foreach (var k in dead) _exposureIdentity.Remove(k);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Price fetch failed for {Symbol} on {Provider}.", rawSymbol, id.Provider);
                }
            }
            return 0;
        }

        private double CollateralOf(string symbol) =>
            _collateral.TryGetValue(symbol, out double c) ? c : 0.0;

        // ── Account mutation (caller holds _lock) ─────────────────────────────

        // Returns realized P&L (quote currency) for the portion of this fill that
        // reduces an existing position; null when it only opens/adds.
        private double? ApplyFill(string symbol, OrderSide side, double qty, double price)
        {
            var pos = _positions.TryGetValue(symbol, out var p) ? p : (Qty: 0.0, Avg: 0.0);
            double signed = side == OrderSide.Buy ? qty : -qty;
            double newQty = pos.Qty + signed;

            // The SAME settlement the affordability check used, so the two can never
            // disagree about what a fill costs.
            var s = Settle(symbol, side, qty, price);
            _cash += s.CashDelta;

            double newCollateral = CollateralOf(symbol) + s.CollateralDelta;
            if (newCollateral > 1e-9) _collateral[symbol] = newCollateral;
            else                      _collateral.Remove(symbol);

            double? realized = null;
            if (s.ClosingQty > 0)
                realized = pos.Qty > 0 ? (price - pos.Avg) * s.ClosingQty : (pos.Avg - price) * s.ClosingQty;

            if (Math.Abs(newQty) < 1e-12)
            {
                _positions.Remove(symbol);
                // Nothing is owed any more, so nothing stays locked. Guards against
                // rounding dust holding a few cents hostage forever.
                _collateral.Remove(symbol);
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

        // Log a fill to the (capped) history. The fee is recomputed here for the
        // history line only — it was already charged inside Settle, which is what
        // lets CanFill see it. Do not subtract it again.
        private void RecordFill(string symbol, OrderSide side, double qty, double price, double? realized, string orderId)
        {
            double fee = qty * price * FeeRate;
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
                        Activation = o.Activation, Armed = o.Armed, Oco = o.OcoGroupId,
                        ReduceOnly = o.ReduceOnly,
                        Bracket = o.Bracket == null ? null : new BracketDto
                        {
                            StopLoss = o.Bracket.StopLoss, TakeProfit = o.Bracket.TakeProfit,
                            TrailStopMode = o.Bracket.TrailStopMode?.ToString(), TrailStopValue = o.Bracket.TrailStopValue,
                            TrailTpMode = o.Bracket.TrailTpMode?.ToString(), TrailTpValue = o.Bracket.TrailTpValue,
                            TrailTpActivation = o.Bracket.TrailTpActivation, Oco = o.Bracket.OcoGroupId
                        }
                    }).ToList(),
                    History = _history.ToList(),
                    Collateral = _collateral.Select(kv => new LevDto { Symbol = kv.Key, Value = kv.Value }).ToList(),
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
                        o.Activation, o.Armed, o.Oco, o.ReduceOnly,
                        o.Bracket == null ? null : new BracketSpec(
                            o.Bracket.StopLoss, o.Bracket.TakeProfit,
                            Enum.TryParse<TrailMode>(o.Bracket.TrailStopMode, out var bsm) ? bsm : (TrailMode?)null,
                            o.Bracket.TrailStopValue,
                            Enum.TryParse<TrailMode>(o.Bracket.TrailTpMode, out var btm) ? btm : (TrailMode?)null,
                            o.Bracket.TrailTpValue, o.Bracket.TrailTpActivation, o.Bracket.Oco)));
                if (dto.History != null) _history.AddRange(dto.History);
                foreach (var c in dto.Collateral ?? new List<LevDto>()) _collateral[c.Symbol] = c.Value;
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

            /// <summary>
            /// May only shrink an existing position, never open one. Every protective
            /// leg carries this; see <see cref="AttachProtectiveLegs"/>.
            /// </summary>
            public bool ReduceOnly { get; }

            /// <summary>
            /// Protective legs to attach when this order fills, for an entry that is
            /// still resting. Null for market entries (which attach immediately) and
            /// for orders carrying no protection.
            /// </summary>
            public BracketSpec? Bracket { get; }

            public PaperOrder(string id, string symbol, OrderSide side, OrderType type, double quantity,
                double? price, double? trigger, bool isStop, bool isTp,
                TrailMode? trail = null, double? trailValue = null, double? trailAnchor = null,
                double? activation = null, bool armed = true, string? ocoGroupId = null,
                bool reduceOnly = false, BracketSpec? bracket = null)
            {
                Id = id; Symbol = symbol; Side = side; Type = type; Quantity = quantity;
                Price = price; Trigger = trigger; IsStop = isStop; IsTp = isTp;
                Trail = trail; TrailValue = trailValue; TrailAnchor = trailAnchor;
                Activation = activation; Armed = armed; OcoGroupId = ocoGroupId;
                ReduceOnly = reduceOnly; Bracket = bracket;
            }
        }

        /// <summary>
        /// The protective legs an entry is supposed to acquire — carried on a resting
        /// entry until it fills, because a bracket cannot be placed against a position
        /// that does not exist yet.
        /// </summary>
        private sealed record BracketSpec(
            double? StopLoss, double? TakeProfit,
            TrailMode? TrailStopMode, double? TrailStopValue,
            TrailMode? TrailTpMode, double? TrailTpValue, double? TrailTpActivation,
            string? OcoGroupId)
        {
            /// <summary>Whether there is any protection here worth carrying.</summary>
            public bool Any =>
                StopLoss is > 0 || TakeProfit is > 0
                || (TrailStopValue is > 0 && TrailStopMode != null)
                || (TrailTpValue is > 0 && TrailTpMode != null);
        }

        private sealed class PaperDto
        {
            public double Cash { get; set; }
            public List<PosDto> Positions { get; set; } = new();
            public List<LevDto> Leverage { get; set; } = new();
            public List<OrderDto> Open { get; set; } = new();
            public List<TradeFill> History { get; set; } = new();
            public List<IdentDto> Charts { get; set; } = new();
            // Locked against open shorts. Restoring positions without it would hand
            // back collateral the account still owes — free money on every restart.
            public List<LevDto> Collateral { get; set; } = new();
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

            // A leg that comes back from disk without ReduceOnly would be free to
            // open a position, and a pending bracket that does not survive a restart
            // is a silently unprotected entry — the same bug in different clothes.
            public bool ReduceOnly { get; set; }
            public BracketDto? Bracket { get; set; }
        }

        private sealed class BracketDto
        {
            public double? StopLoss { get; set; }
            public double? TakeProfit { get; set; }
            public string? TrailStopMode { get; set; }
            public double? TrailStopValue { get; set; }
            public string? TrailTpMode { get; set; }
            public double? TrailTpValue { get; set; }
            public double? TrailTpActivation { get; set; }
            public string? Oco { get; set; }
        }
    }
}
