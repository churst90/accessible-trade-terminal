using System.Reactive.Linq;
using System.Reactive.Subjects;
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

        // The charts this account is watching. ONE account can be looked at from several browser
        // tabs at once, each with its own IWorkspaceStore, so this is a set rather than the single
        // store it used to be — see PaperAccountHub for why the account itself is now shared.
        private readonly List<(IWorkspaceStore Store, IDisposable Sub, IDisposable? Monitor)> _stores = new();
        private readonly object _storeLock = new();
        // Written from every attached store's subscription callback and read from
        // paths that do not hold _lock (ProviderForSymbol, ResolvePriceAsync), so the
        // publication has to be visible across threads.
        // Nullable: when the LAST tab detaches there is no chart to speak of, and
        // pointing at the dead one would keep resolving prices and identities against a
        // workspace nobody is looking at. Every read falls back to the account's own
        // persisted exposure instead.
        private volatile IWorkspaceStore? _store;   // the most recently active attached store
        private readonly ILogger<PaperTradingProvider> _logger;
        private readonly string _statePath;
        private readonly object _lock = new();

        /// <summary>
        /// The subscription pair the constructor made for <see cref="PrimaryStore"/>,
        /// handed to whoever claims it via <see cref="TakePrimaryAttachment"/>. See that
        /// method for why the creating tab has to be able to detach like any other.
        /// </summary>
        private IDisposable? _primaryAttachment;

        private readonly Subject<OrderUpdate> _orderUpdates = new();

        /// <summary>
        /// Order lifecycle notifications, with each subscriber isolated from the others.
        ///
        /// <para>
        /// Handing out <c>_orderUpdates.AsObservable()</c> directly put every listener on one
        /// observer walk: <c>OnNext</c> stops at the first handler that throws, so a fault in any
        /// one of them denied the fill to all the ones behind it and threw the exception back out
        /// of <c>PlaceOrderAsync</c> — after the position had opened. The speech announcement, the
        /// earcon, the journal entry and the reconciliation coordinator all read this stream.
        /// </para>
        ///
        /// <para>
        /// Giving each subscriber its own subscription and catching inside it means a broken
        /// listener costs nobody else anything, and the order path never sees the exception.
        /// </para>
        ///
        /// <para>
        /// <b>What this does NOT fix, and it is a real gap.</b> Consumers call
        /// <c>Subscribe(Action&lt;OrderUpdate&gt;)</c> themselves, and Rx's own
        /// <c>AnonymousObserver</c> disposes the subscription when that action throws — before the
        /// catch below is reached. So the faulty listener goes silent for the rest of the session,
        /// and if it was an announcement path, the trader stops being told about fills with only
        /// this log line to show for it. Closing that means giving the stream a subscribe method of
        /// its own instead of handing out a bare <c>IObservable</c>, which changes a contract every
        /// provider plugin implements. Pinned by
        /// <c>SubscriberFaultIsolationTests.An_order_subscriber_that_throws_loses_its_own_
        /// subscription_and_nobody_elses</c>.
        /// </para>
        /// </summary>
        public IObservable<OrderUpdate> OrderUpdateStream =>
            Observable.Create<OrderUpdate>(observer => _orderUpdates.Subscribe(
                update =>
                {
                    try { observer.OnNext(update); }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex,
                            "A subscriber to the paper broker's order stream threw handling {Status} for {Symbol}. "
                            + "Its subscription survives; other subscribers were unaffected.",
                            update.Status, update.Symbol);
                    }
                },
                observer.OnError,
                observer.OnCompleted));

        private const double FeeRate = 0.0004;   // simulated 0.04% taker fee per fill
        private double _cash;
        // Last price seen for each symbol, from the focused chart or a background
        // monitor. Deliberately NOT persisted: a price restored from disk would be
        // stale by an unknown amount and would price positions off it.
        private readonly Dictionary<string, double> _lastPrice = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The most recent bar seen for each symbol, whole rather than just its close.
        /// An order placed part-way through a bar needs to know what that bar had
        /// already printed before it existed — see <see cref="EligibleRange"/>. Not
        /// persisted, for the same reason <see cref="_lastPrice"/> is not.
        /// </summary>
        private readonly Dictionary<string, Ohlcv> _lastBar = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Spellings that resolved onto a DIFFERENT existing position's key, mapped to
        /// that key.
        ///
        /// <para>
        /// <see cref="ResolveLedgerKeyAsync"/> deliberately files a trade under an
        /// existing position's spelling rather than renaming it, so after trading
        /// <c>BTC/USD</c> and then <c>BTCUSDT</c> on a venue that routes both to one
        /// book, everything — position, collateral, protective legs — is keyed
        /// <c>BTC/USD</c>. Bars, however, arrive spelled the way the CHART spells them.
        /// Without this map the fill engine looked for <c>BTCUSDT</c> orders, found
        /// none, and the stop, the take-profit and liquidation all went quiet: a short
        /// there could never be bought in, and unrealised P&amp;L sat frozen at the entry
        /// price because <c>_lastPrice</c> was written under the other key.
        /// </para>
        ///
        /// <para>
        /// Persisted, because the venue lookup that produced the mapping needs the
        /// network and the fill engine runs under a lock. Every read re-checks that the
        /// target is still a live ledger key, so an alias cannot resurrect itself after
        /// the position it pointed at is closed.
        /// </para>
        /// </summary>
        private readonly Dictionary<string, string> _ledgerAlias = new(StringComparer.OrdinalIgnoreCase);

        // The chart each traded symbol was traded under, so exposure can be priced
        // after its tab is gone. Persisted, unlike _lastPrice.
        private readonly Dictionary<string, ChartIdentity> _exposureIdentity = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Case-insensitive, like every other ledger dictionary here. It was the default
        /// comparer until 2026-08-25, which was latent only because every writer uppercases
        /// the key first — but <see cref="Load"/> restores whatever keys are in the JSON, and a
        /// position split across two casings gives a short whose quantity and collateral live
        /// under different keys: one that can never be liquidated, because the liquidation
        /// check looks up collateral by the position's key and finds nothing.
        /// </summary>
        private readonly Dictionary<string, (double Qty, double Avg)> _positions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Quote currency locked against a short: the sale proceeds (which are owed
        /// back as the asset) plus the initial margin. Not spendable, and reported as
        /// <c>Balance.Locked</c> — the field that was hardcoded to zero until shorting
        /// gave it something to mean.
        /// </summary>
        private readonly Dictionary<string, double> _collateral = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Margin posted on top of the proceeds, as a fraction of notional. 1.0 is
        /// full collateralisation — "1×" — which makes shorting N cost exactly the
        /// same free cash as buying N, and puts liquidation at twice the entry price.
        /// Deliberately more conservative than real venues: a paper account that
        /// teaches thinner margin than reality teaches the wrong lesson, and the
        /// configurable version belongs with the leverage work.
        /// </summary>
        private const double InitialMarginRate = 1.0;

        /// <summary>
        /// How each position's collateral is held. Set when a position is OPENED (or
        /// flipped) from <c>TradeSignal.MarginType</c>, cleared when it closes.
        ///
        /// <para>
        /// Isolated is the default and is what this broker always did: the collateral
        /// in <see cref="_collateral"/> backs one symbol, and that symbol liquidates
        /// alone. Cross posts the same collateral but pools it — a cross short is
        /// judged against every cross short's collateral PLUS free cash, so it
        /// survives further and then takes every other cross position with it. Those
        /// are two different trades from the same entry, which is why the mode is
        /// reported per position rather than kept as a setting.
        /// </para>
        ///
        /// <para>
        /// Absent for a symbol means "not recorded" — see <see cref="MarginModeOf"/>,
        /// which reads a legacy account (written before this map existed) as Isolated
        /// wherever collateral is held, because that is what those positions actually
        /// are.
        /// </para>
        /// </summary>
        private readonly Dictionary<string, MarginMode> _marginMode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _leverage = new(StringComparer.OrdinalIgnoreCase);
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
            PrimaryStore = store;
            _logger = logger;
            _data = dataService;
            _statePath = Path.Combine(paths.AppDataDirectory, "paper_account.json");
            Load();

            // Attached LAST, and deliberately: StateStream is a BehaviorSubject, so
            // subscribing replays the current state straight into the fill engine. Doing
            // that before Load() ran the engine against an empty ledger — and before
            // _logger was assigned.
            //
            // The monitor subscription rides along with the store, not with the
            // constructor: IEventBus is scoped per browser tab on the hosted head, so an
            // account that kept only its creator's bus never saw a background bar from
            // any other tab. Each attachment now brings its own; see Attach.
            _primaryAttachment = Attach(store, eventBus);
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
        ///
        /// <para>
        /// <c>IsolatedMargin</c> is the newest entry and it is earned the same way:
        /// <c>TradeSignal.MarginType</c> is recorded against the position and the two
        /// modes liquidate by different maths — isolated against that symbol's own
        /// collateral, cross against the pooled collateral of every cross short plus
        /// free cash. Before it, the dashboard hid the cross/isolated selector in
        /// paper mode, so the one account every hosted user has could not reach the
        /// choice at all.
        /// </para>
        /// </summary>
        public ProviderCapabilities Capabilities =>
            ProviderCapabilities.Brackets | ProviderCapabilities.OCO | ProviderCapabilities.TrailingStop |
            ProviderCapabilities.Shorting | ProviderCapabilities.MarginTrading |
            ProviderCapabilities.IsolatedMargin;
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
                        return new Position(
                            kv.Key, kv.Value.Qty, kv.Value.Avg,
                            kv.Value.Qty * price,
                            (price - kv.Value.Avg) * kv.Value.Qty,
                            lev, LiquidationPriceOf(kv.Key), MarginModeOf(kv.Key));
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
                // The resolver just matched this spelling onto an existing position's
                // key using a venue lookup the fill engine cannot make (it runs under
                // this lock, and a lock may not be held across an await). Remember the
                // answer so bars arriving under the chart's spelling can find the
                // position, the stop and the collateral that are filed under the other.
                if (!string.Equals(symbol, raw, StringComparison.OrdinalIgnoreCase))
                    _ledgerAlias[raw] = symbol;

                if (signal.Leverage is > 1) _leverage[symbol] = Math.Clamp(signal.Leverage.Value, 1, MaxLeverage);

                // Remember which chart this symbol trades on, so the monitoring service — and
                // ResolvePriceAsync — can keep pricing it after the tab is closed. Compared
                // canonically: the live identity may spell the same market differently from the
                // signal, and an identity filed under a spelling nothing looks up is an identity
                // that will not be there when a close needs it.
                var live = _store?.State.Identity ?? default;
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
                    var pnl = ApplyFill(symbol, signal.Side, mktQty, px, ParseMarginMode(signal.MarginType));
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

                Rest(new PaperOrder(oid, symbol, signal.Side, signal.Type, signal.Quantity, price, trigger, isStop, isTp,
                    ocoGroupId: signal.OcoGroupId, reduceOnly: signal.ReduceOnly, bracket: bracket,
                    marginMode: ParseMarginMode(signal.MarginType)));
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

            // ── The choke point for direction ────────────────────────────────────
            // A leg on the wrong side of the fill is not protection, it is an order to
            // close the position on the next bar: Crossed() for a sell stop is
            // `bar.Low <= trigger`, so a stop of 110 attached to a long filled at 100 is
            // true immediately and the trade closes itself having paid two fees. The
            // validator that states this rule (ProtectiveLevelValidator) had exactly one
            // caller — the position table's inline editor — so a bracket typed into the
            // ticket, or emitted by a strategy, arrived here unchecked.
            //
            // Refuse the leg rather than attach it, and SAY SO. Silently dropping it
            // would leave the position naked while the user believes it is protected,
            // which is the worse of the two failures.
            bool isLong = entrySide == OrderSide.Buy;
            bool StopIsSane(double level) =>
                ReportIfCrossed(symbol, exit, level, entryPx, ProtectiveLevel.StopLoss, isLong);
            bool TargetIsSane(double level) =>
                ReportIfCrossed(symbol, exit, level, entryPx, ProtectiveLevel.TakeProfit, isLong);

            if (spec.TrailStopValue is > 0 && spec.TrailStopMode != null)
                Rest(new PaperOrder(NewId(), symbol, exit, OrderType.StopMarket, qty, null, entryPx, true, false,
                    spec.TrailStopMode, spec.TrailStopValue, entryPx, ocoGroupId: bracket,
                    reduceOnly: true));    // trailing stop anchored at the fill — always on the right side by construction
            else if (spec.StopLoss is > 0 && StopIsSane(spec.StopLoss.Value))
                Rest(new PaperOrder(NewId(), symbol, exit, OrderType.StopMarket, qty, null, spec.StopLoss, true, false,
                    ocoGroupId: bracket, reduceOnly: true));

            if (spec.TakeProfit is > 0 && TargetIsSane(spec.TakeProfit.Value))
                Rest(new PaperOrder(NewId(), symbol, exit, OrderType.TakeProfitMarket, qty, null, spec.TakeProfit, false, true,
                    ocoGroupId: bracket, reduceOnly: true));

            if (spec.TrailTpValue is > 0 && spec.TrailTpMode != null)
                Rest(new PaperOrder(NewId(), symbol, exit, OrderType.TakeProfitMarket, qty, null, null, false, true,
                    spec.TrailTpMode, spec.TrailTpValue, null, spec.TrailTpActivation,
                    armed: spec.TrailTpActivation == null, ocoGroupId: bracket,
                    reduceOnly: true));  // trailing take-profit
        }

        /// <summary>
        /// True when a protective level sits on the correct side of the entry and may be
        /// attached; false — having reported WHY — when it would trigger on the next bar.
        /// </summary>
        private bool ReportIfCrossed(string symbol, OrderSide exit, double level, double entryPx,
            ProtectiveLevel which, bool isLong)
        {
            var check = ProtectiveLevelValidator.Validate(
                level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                which, isLong, entryPx);
            if (check.Ok) return true;

            bool isStop = which == ProtectiveLevel.StopLoss;
            string name = isStop ? "stop loss" : "take profit";
            // A Rejected update carrying its Reason is the channel the speech layer
            // already speaks (GeneralOrderService.PublishOrderEvent), so this refusal
            // is heard the same way a venue's refusal would be. The flags name the leg,
            // and Rejected keeps them out of the "stop hit" branch.
            Emit(NewId(), symbol, exit, 0, 0, 0, OrderStatus.Rejected, isStop, !isStop,
                reason: $"the {name} was not attached. {check.Message} "
                      + $"The {symbol} position is open at "
                      + $"{entryPx.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)} "
                      + $"with no {name}.");
            _logger.LogWarning(
                "Paper bracket leg refused on {Symbol}: {Which} {Level} against entry {Entry} ({Message})",
                symbol, name, level, entryPx, check.Message);
            return false;
        }

        /// <summary>
        /// Put a resting order on the book, stamped with the moment it was placed and
        /// with what its symbol's current bar had already printed.
        ///
        /// <para>
        /// The single door onto <c>_open</c> for new orders, deliberately: the stamp is
        /// what stops an order filling off price action that predates it
        /// (<see cref="EligibleRange"/>), and a placement path that added to the list
        /// directly would silently opt out of that. <c>Load</c> is the one exception —
        /// it restores the stamp that was persisted rather than minting a new one.
        /// </para>
        ///
        /// <para>Caller holds <see cref="_lock"/>.</para>
        /// </summary>
        private void Rest(PaperOrder o)
        {
            o.PlacedAt = DateTime.UtcNow;
            if (_lastBar.TryGetValue(o.Symbol, out var b))
            {
                o.PlacedBarHigh = b.High;
                o.PlacedBarLow = b.Low;
                o.PlacedBarClose = b.Close;
            }
            _open.Add(o);
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
                _lastBar.Clear();
                _ledgerAlias.Clear();
                _collateral.Clear();
                _marginMode.Clear();
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
                // The spelling this bar arrives under is the CHART's, which is not
                // necessarily the one the money is filed under. Everything below works
                // on the ledger key; both spellings get the price, because callers ask
                // for whichever one they hold.
                string key = LedgerKeyFor(sym);

                // Remember the price even when nothing fills: it is what makes
                // unrealized P&L live for a position whose chart is not on screen.
                _lastPrice[sym] = bar.Close;
                _lastBar[sym] = bar;
                if (!string.Equals(key, sym, StringComparison.OrdinalIgnoreCase))
                {
                    _lastPrice[key] = bar.Close;
                    _lastBar[key] = bar;
                }

                // Liquidation before anything else this tick. A short whose collateral
                // is gone is not a position any more, and letting resting orders act on
                // it first would report fills against something that no longer exists.
                LiquidateIfCollateralExhausted(key, bar);
                LiquidateCrossIfPooledEquityExhausted(key, bar);

                // Advance trailing stops first so this tick uses the moved trigger.
                bool trailMoved = false;
                foreach (var o in _open)
                    if (o.Trail != null && string.Equals(o.Symbol, key, StringComparison.OrdinalIgnoreCase))
                        trailMoved |= UpdateTrail(o, bar);

                var fills = _open.Where(o => string.Equals(o.Symbol, key, StringComparison.OrdinalIgnoreCase) && Crossed(o, bar)).ToList();
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

                    var pnl = ApplyFill(o.Symbol, o.Side, qty, px, o.MarginMode);
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

            // A cross short is not judged on its own collateral — that is the whole
            // difference between the modes — so it is left to the pooled check below.
            // Running both would liquidate it at the isolated threshold and cross
            // would be a label with no behaviour behind it.
            if (MarginModeOf(symbol) == MarginMode.Cross) return;

            double locked = CollateralOf(symbol);
            if (locked <= 0) return;

            double shortQty = Math.Abs(pos.Qty);
            double liqPrice = locked / shortQty;
            if (bar.High < liqPrice) return;

            // Bought back AT the liquidation price: that is where the collateral runs
            // out, and it is what the account is left with either way.
            ForceClose(symbol, shortQty, liqPrice,
                "LIQUIDATED — the short's collateral was exhausted at "
                + liqPrice.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Buys back EVERY cross short when the pooled resources behind them run out.
        ///
        /// <para>
        /// Cross margin is one collateral bucket plus the free cash in the account, so
        /// no single position has a threshold of its own: the test is whether what all
        /// the cross shorts owe has reached what the account has to pay it with. That
        /// is why a cross short survives past the price an isolated one would have died
        /// at — and why, when it finally goes, it takes every other cross position with
        /// it. Isolated positions are ring-fenced and are not touched here, which is
        /// the property a trader chooses isolated for.
        /// </para>
        ///
        /// <para>
        /// The symbol whose bar just printed is marked at that bar's HIGH, for the same
        /// reason the isolated check uses it: the money is gone at the moment price
        /// touches the level, and a bar that spiked through and recovered still
        /// liquidated you on a real venue. Every other cross short is marked at its
        /// last known price. Unlike the isolated path the buy-back is AT that mark
        /// rather than at a computed threshold, because with several positions there
        /// is no single price the pool ran out at — and a gap taking the account
        /// further negative than its collateral is a true fact about cross margin, not
        /// a rounding error. Caller holds the lock.
        /// </para>
        /// </summary>
        private void LiquidateCrossIfPooledEquityExhausted(string pricedSymbol, Ohlcv bar)
        {
            var shorts = new List<(string Symbol, double Qty, double Mark)>();
            double resources = _cash;

            foreach (var kv in _positions)
            {
                if (kv.Value.Qty >= 0) continue;
                if (MarginModeOf(kv.Key) != MarginMode.Cross) continue;
                double mark = string.Equals(kv.Key, pricedSymbol, StringComparison.OrdinalIgnoreCase)
                    ? bar.High
                    : PriceFor(kv.Key, kv.Value.Avg);
                if (mark <= 0) continue;   // never priced: nothing to judge it on yet
                resources += CollateralOf(kv.Key);
                shorts.Add((kv.Key, Math.Abs(kv.Value.Qty), mark));
            }

            if (shorts.Count == 0) return;
            double owed = shorts.Sum(s => s.Mark * s.Qty);
            if (owed < resources) return;

            // Materialised before the loop: ForceClose mutates _positions, and the
            // whole point of cross is that they all go together.
            foreach (var s in shorts)
                ForceClose(s.Symbol, s.Qty, s.Mark,
                    "LIQUIDATED — cross margin: the account's pooled collateral and cash were "
                    + "exhausted by its short positions together");
        }

        /// <summary>
        /// The buy-back itself: fill it, record it, and cancel the orders that were
        /// protecting a position which no longer exists. Leaving those resting is how
        /// a liquidated short reopens itself on the next touch. Caller holds the lock.
        /// </summary>
        private void ForceClose(string symbol, double qty, double price, string reason)
        {
            string id = NewId();
            var pnl = ApplyFill(symbol, OrderSide.Buy, qty, price);
            Emit(id, symbol, OrderSide.Buy, qty, price, 0, OrderStatus.Filled, false, false, pnl, reason: reason);
            RecordFill(symbol, OrderSide.Buy, qty, price, pnl, id);

            foreach (var o in _open.Where(o => string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _open.Remove(o);
                Emit(o.Id, o.Symbol, o.Side, 0, 0, o.Quantity, OrderStatus.Cancelled, false, false,
                    reason: "the position was liquidated");
            }

            _logger.LogWarning("Paper short on {Symbol} liquidated at {Price}", symbol, price);
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
            return BarFill.Price(level, MarketWhenLive(o, bar), o.Side, stopLike: o.IsStop || o.Trail != null);
        }

        /// <summary>
        /// Where the market was when this order became live — the reference
        /// <see cref="BarFill"/> needs to tell a gap from a touch.
        ///
        /// <para>
        /// The bar's OPEN is that reference only for an order that already existed when
        /// the bar opened. For one placed part-way through, the open is a price the
        /// order was never live at, and handing it over gives away the whole gap: a buy
        /// limit at 99 typed while the market is 105, on a bar that opened at 98, would
        /// have filled at 98.
        /// </para>
        /// </summary>
        private static double MarketWhenLive(PaperOrder o, Ohlcv bar) =>
            bar.Date > o.PlacedAt ? bar.Open : (o.PlacedBarClose ?? bar.Close);

        /// <summary>
        /// The high and low that may fill this order: the part of the bar that happened
        /// AFTER it was placed.
        ///
        /// <para>
        /// <b>The bug this exists to close.</b> The engine is driven by the newest,
        /// still-forming bar, and an order carried no placement time, so it was tested
        /// against the whole bar's accumulated extremes — including price action from
        /// before it existed. On the 4h and 1d charts the demo exposes, a buy limit at
        /// 99 placed when the day already printed 99 six hours ago and spot is 105
        /// crossed on the very next tick and filled at 99. Free money, minted by the
        /// simulator, and the same root cause anchored a fresh trailing stop at an
        /// extreme it had never seen.
        /// </para>
        ///
        /// <para>
        /// A bar that OPENED after placement happened entirely afterwards, so all of it
        /// counts — the common case, and the fast path. Otherwise the bar was already
        /// forming: an extreme BEYOND what it had printed at placement is new price
        /// action and counts, while an extreme that was already there does not, and the
        /// only two prices known to have occurred after placement are the price then and
        /// the price now. That is a lower bound on the true post-placement range, which
        /// is the safe direction: it can delay a fill by a tick, never invent one.
        /// </para>
        ///
        /// <para>
        /// <b>Bar timestamps decide this, and they are not all UTC.</b> A venue stamping
        /// bars in exchange-local time behind UTC simply looks older and takes the
        /// conservative branch; one ahead of UTC takes the whole bar, which is exactly
        /// the behaviour that existed before. The failure mode is degradation to the old
        /// rule, never something worse. An order restored from a state file written
        /// before this field existed has <c>PlacedAt = default</c> and is treated the
        /// same way.
        /// </para>
        /// </summary>
        private static (double High, double Low) EligibleRange(PaperOrder o, Ohlcv bar)
        {
            if (bar.Date > o.PlacedAt) return (bar.High, bar.Low);

            double now = bar.Close;
            double then = o.PlacedBarClose ?? now;
            double high = o.PlacedBarHigh is double ph && bar.High > ph ? bar.High : Math.Max(then, now);
            double low  = o.PlacedBarLow  is double pl && bar.Low  < pl ? bar.Low  : Math.Min(then, now);
            return (high, low);
        }

        private static bool Crossed(PaperOrder o, Ohlcv bar)
        {
            var (high, low) = EligibleRange(o, bar);

            // Trailing exits (stop or take-profit) fire on a reversal to the
            // trailing trigger, regardless of stop/TP labelling — and only once
            // armed with a computed trigger.
            if (o.Trail != null)
                return o.Armed && o.Trigger != null &&
                       (o.Side == OrderSide.Buy ? high >= o.Trigger : low <= o.Trigger);

            return o.Type switch
            {
                OrderType.Limit
                    => o.Side == OrderSide.Buy ? low <= o.Price : high >= o.Price,
                OrderType.StopMarket or OrderType.StopLimit
                    => o.Side == OrderSide.Buy ? high >= o.Trigger : low <= o.Trigger,
                OrderType.TakeProfitMarket or OrderType.TakeProfitLimit
                    => o.Side == OrderSide.Buy ? low <= o.Trigger : high >= o.Trigger,
                _ => false
            };
        }

        // Advance a trailing stop's anchor toward the favourable extreme and
        // recompute its trigger. Returns true if the anchor moved this tick.
        private static bool UpdateTrail(PaperOrder o, Ohlcv bar)
        {
            if (o.Trail == null || o.TrailValue == null) return false;

            // Same rule as Crossed: a trailing stop may only trail from price action
            // that happened after it existed. Anchoring a stop placed at 10:00 to the
            // day's 06:00 high sets its trigger from a move it never rode.
            var (high, low) = EligibleRange(o, bar);

            // A trailing take-profit stays dormant until price reaches its
            // activation level, then arms and begins trailing from there.
            if (!o.Armed)
            {
                bool reached = o.Activation == null ||
                    (o.Side == OrderSide.Sell ? high >= o.Activation.Value : low <= o.Activation.Value);
                if (!reached) return false;
                o.Armed = true;
                o.TrailAnchor = o.Side == OrderSide.Sell ? high : low;
            }

            double Dist(double anchor) => o.Trail == TrailMode.Amount ? o.TrailValue.Value : anchor * o.TrailValue.Value / 100.0;
            if (o.Side == OrderSide.Sell)   // protecting a long: trail up
            {
                double anchor = Math.Max(o.TrailAnchor ?? high, high);
                bool moved = o.TrailAnchor is null || anchor > o.TrailAnchor.Value;
                o.TrailAnchor = anchor;
                o.Trigger = anchor - Dist(anchor);
                return moved;
            }
            else                            // protecting a short: trail down
            {
                double anchor = Math.Min(o.TrailAnchor ?? low, low);
                bool moved = o.TrailAnchor is null || anchor < o.TrailAnchor.Value;
                o.TrailAnchor = anchor;
                o.Trigger = anchor + Dist(anchor);
                return moved;
            }
        }

        // ── Order validation (caller holds _lock) ─────────────────────────────

        /// <summary>
        /// How a fill would settle: what it closes, what it opens, and what that does
        /// to free cash and to locked collateral — and whether it can settle at all.
        ///
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

        /// <summary>
        /// Watch another chart. Each browser tab has its own <see cref="IWorkspaceStore"/>, and an
        /// account shared between tabs has to see all of them or a resting order would fill only
        /// while the tab that placed it happened to be in front. Dispose the token to stop watching
        /// — the circuit scope does that when the tab goes away.
        /// </summary>
        /// <param name="eventBus">
        /// This tab's bus, subscribed for the life of the attachment. Optional, and null
        /// on any head that has no background monitoring. On the hosted head
        /// <c>IEventBus</c> is registered <c>AddScoped</c> — a Blazor scope IS a browser
        /// tab — so the bus a background monitor publishes on is the publishing tab's own.
        /// An account that held only the bus of whichever tab happened to create it was
        /// deaf to every other tab's monitors, which is precisely the case the background
        /// fill path exists for.
        /// </param>
        public IDisposable Attach(IWorkspaceStore store, IEventBus? eventBus = null)
        {
            if (store == null) return new NoOpDisposable();

            var sub = store.StateStream.Subscribe(st => { _store = store; OnState(st); });
            var monitorSub = eventBus?.Subscribe<MonitoredBarEvent>(
                e => ProcessBar(e.Identity.Symbol ?? "", e.Latest));
            lock (_storeLock) _stores.Add((store, sub, monitorSub));

            return new DetachToken(this, store, sub);
        }

        /// <summary>
        /// Claim the constructor's own subscription, once. Null-returning after that.
        ///
        /// <para>
        /// The tab that CREATES the account is the one whose store the constructor
        /// attached, and it used to be handed a no-op token — so when that tab closed,
        /// nothing detached: its dead store kept its subscription in <c>_stores</c>, and
        /// <see cref="_store"/> could go on pointing at a workspace nobody was looking
        /// at, resolving prices and identities against a chart that had gone. Every
        /// tab must be able to leave, including the first one.
        /// </para>
        /// </summary>
        public IDisposable TakePrimaryAttachment()
        {
            var claimed = System.Threading.Interlocked.Exchange(ref _primaryAttachment, null);
            return claimed ?? new NoOpDisposable();
        }

        private void Detach(IWorkspaceStore store, IDisposable sub)
        {
            List<IDisposable?> monitors;
            lock (_storeLock)
            {
                monitors = _stores
                    .Where(e => ReferenceEquals(e.Store, store) && ReferenceEquals(e.Sub, sub))
                    .Select(e => e.Monitor).ToList();
                _stores.RemoveAll(e => ReferenceEquals(e.Store, store) && ReferenceEquals(e.Sub, sub));
                // Fall back to any surviving store so identity reads keep working.
                if (ReferenceEquals(_store, store)) _store = _stores.Count > 0 ? _stores[^1].Store : null;
            }
            foreach (var m in monitors) m?.Dispose();
            sub.Dispose();
        }

        private sealed class DetachToken : IDisposable
        {
            private readonly PaperTradingProvider _owner;
            private readonly IWorkspaceStore _store;
            private readonly IDisposable _sub;
            private bool _done;
            public DetachToken(PaperTradingProvider o, IWorkspaceStore s, IDisposable sub)
                { _owner = o; _store = s; _sub = sub; }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _owner.Detach(_store, _sub);
            }
        }

        private sealed class NoOpDisposable : IDisposable { public void Dispose() { } }

        /// <summary>
        /// True when this account is owned by <c>PaperAccountHub</c> and shared between a user's
        /// browser tabs. The DI container disposes whatever a scoped factory hands it, so without
        /// this the first tab to close would tear down an account the other tabs are still trading
        /// on — a worse bug than the one sharing exists to fix.
        /// </summary>
        public bool SharedOwnership { get; set; }

        /// <summary>The store this account was constructed with — already attached by the
        /// constructor, so the tab that created the account must not attach it a second time.</summary>
        public IWorkspaceStore PrimaryStore { get; private set; }

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
        /// Every spelling the ledger currently has money filed under: open positions,
        /// locked collateral, and resting orders. Caller holds <see cref="_lock"/>.
        /// </summary>
        private IEnumerable<string> LedgerKeys() =>
            _positions.Keys
                .Concat(_collateral.Keys)
                .Concat(_open.Select(o => o.Symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase);

        private bool IsLedgerKey(string sym) =>
            _positions.ContainsKey(sym)
            || _collateral.ContainsKey(sym)
            || _open.Any(o => string.Equals(o.Symbol, sym, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The ledger key a bar for <paramref name="sym"/> belongs to.
        ///
        /// <para>
        /// The account files a trade under an EXISTING position's spelling
        /// (<see cref="ResolveLedgerKeyAsync"/>), but bars arrive under the chart's. The
        /// fill engine compares the two with <c>string.Equals</c>, so without this the
        /// two halves of the same market never met: stops and targets on the aliased
        /// position never fired, and a short filed under the other spelling could not be
        /// liquidated no matter how far it ran.
        /// </para>
        ///
        /// <para>
        /// Three ways in, cheapest first, and all of them synchronous — the fill engine
        /// runs under a lock and cannot ask a venue anything. (1) The spelling IS a
        /// ledger key. (2) A recorded alias, re-checked against the live ledger so a
        /// closed position's alias cannot capture a fresh trade under the same name.
        /// (3) A separator-and-case match, which catches <c>BTC/USD</c> against
        /// <c>BTCUSD</c> with no venue knowledge at all. Otherwise the spelling stands
        /// on its own, which is what a symbol with no exposure should do.
        /// </para>
        ///
        /// <para>Caller holds <see cref="_lock"/>.</para>
        /// </summary>
        private string LedgerKeyFor(string sym)
        {
            if (IsLedgerKey(sym)) return sym;

            if (_ledgerAlias.TryGetValue(sym, out string? mapped)
                && mapped != null && IsLedgerKey(mapped))
                return mapped;

            string canon = LocalCanonical(sym);
            foreach (string key in LedgerKeys())
                if (string.Equals(LocalCanonical(key), canon, StringComparison.OrdinalIgnoreCase))
                    return key;

            return sym;
        }

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
            var live = _store?.State.Identity ?? default;
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
            var live = _store?.State.Identity ?? default;
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

        /// <summary>
        /// How this position's collateral is held.
        ///
        /// <para>
        /// A position with no collateral posted against it — every long here, which
        /// is bought outright — reports <see cref="MarginMode.None"/> rather than a
        /// mode, because nothing is held either way and naming a mode would imply a
        /// liquidation story the position does not have.
        /// </para>
        ///
        /// <para>
        /// An account saved before <see cref="_marginMode"/> existed carries no
        /// entries, and every short in it was collateralised per symbol. Reading
        /// those as Isolated is not a default, it is what they are.
        /// </para>
        /// </summary>
        /// <summary>
        /// Reads <c>TradeSignal.MarginType</c>, which is a free string on the SDK
        /// contract because exchanges spell it differently.
        ///
        /// <para>
        /// Anything unrecognised — including null, which is what every caller that
        /// does not care sends — is <see cref="MarginMode.None"/>, meaning "did not
        /// ask". That leaves the position on this broker's long-standing isolated
        /// behaviour rather than guessing cross, which would move a liquidation price
        /// on the strength of a typo.
        /// </para>
        /// </summary>
        internal static MarginMode ParseMarginMode(string? marginType) => marginType?.Trim().ToLowerInvariant() switch
        {
            "cross"    => MarginMode.Cross,
            "isolated" => MarginMode.Isolated,
            _          => MarginMode.None,
        };

        private MarginMode MarginModeOf(string symbol) =>
            CollateralOf(symbol) <= 0 ? MarginMode.None
            : _marginMode.TryGetValue(symbol, out var m) && m != MarginMode.None ? m
            : MarginMode.Isolated;

        /// <summary>
        /// The price at which this position's collateral runs out, or 0 when it has
        /// none — a long is bought outright and simply goes to zero, so 0 here means
        /// "not applicable", not "at zero".
        ///
        /// <para>
        /// Isolated is the position's own collateral over what it owes. Cross adds
        /// free cash and the headroom the other cross shorts still have, which is the
        /// whole difference between the modes: the same short liquidates later under
        /// cross and takes the rest of the account with it when it does. Both are the
        /// exact price the tick check fires at, so the number on the row and the
        /// number that closes the trade cannot drift.
        /// </para>
        ///
        /// <para>Caller holds <c>_lock</c>.</para>
        /// </summary>
        private double LiquidationPriceOf(string symbol)
        {
            if (!_positions.TryGetValue(symbol, out var pos) || pos.Qty >= 0) return 0;
            double own = CollateralOf(symbol);
            if (own <= 0) return 0;

            double qty = Math.Abs(pos.Qty);
            if (MarginModeOf(symbol) != MarginMode.Cross) return own / qty;

            double liq = (own + _cash + CrossHeadroomExcluding(symbol)) / qty;
            // Already past it: the next tick liquidates. A negative price is not a
            // thing to read out, so report "not applicable" rather than nonsense.
            return liq > 0 ? liq : 0;
        }

        /// <summary>
        /// What the OTHER cross shorts still have spare — each one's collateral less
        /// what it currently owes. Negative for a cross short that is underwater,
        /// which is the point: in cross margin someone else's losing trade moves your
        /// liquidation price. Caller holds <c>_lock</c>.
        /// </summary>
        private double CrossHeadroomExcluding(string symbol)
        {
            double headroom = 0;
            foreach (var kv in _positions)
            {
                if (kv.Value.Qty >= 0) continue;
                if (string.Equals(kv.Key, symbol, StringComparison.OrdinalIgnoreCase)) continue;
                if (MarginModeOf(kv.Key) != MarginMode.Cross) continue;
                headroom += CollateralOf(kv.Key)
                          - PriceFor(kv.Key, kv.Value.Avg) * Math.Abs(kv.Value.Qty);
            }
            return headroom;
        }

        // ── Account mutation (caller holds _lock) ─────────────────────────────

        // Returns realized P&L (quote currency) for the portion of this fill that
        // reduces an existing position; null when it only opens/adds.
        //
        // `requested` is the margin mode the ORDER asked for, and it is applied only
        // where it can honestly take effect: opening a position, or flipping one
        // through flat, which is a new position wearing the old symbol. Adding to a
        // position leaves the mode alone — a venue does not re-margin an existing
        // short because the second lot asked for something else, and silently
        // switching an isolated position to cross would move its liquidation price
        // without anyone asking.
        private double? ApplyFill(string symbol, OrderSide side, double qty, double price,
            MarginMode requested = MarginMode.None)
        {
            var pos = _positions.TryGetValue(symbol, out var p) ? p : (Qty: 0.0, Avg: 0.0);
            double signed = side == OrderSide.Buy ? qty : -qty;
            double newQty = pos.Qty + signed;
            bool opensFresh = Math.Abs(pos.Qty) < 1e-12 || Math.Sign(newQty) != Math.Sign(pos.Qty);
            if (requested != MarginMode.None && opensFresh) _marginMode[symbol] = requested;

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
                // The mode described THAT position. Left behind, it would silently
                // re-margin the next unrelated trade in the same symbol.
                _marginMode.Remove(symbol);
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
            var st = _store?.State;
            if (st != null && st.Data != null && st.Data.Count > 0 &&
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

        /// <summary>
        /// Announces an order-lifecycle change to whoever is listening.
        ///
        /// <para>
        /// The try/catch is load-bearing and is the fix for the worst failure mode this class had.
        /// <c>Subject&lt;T&gt;.OnNext</c> runs subscribers on the CALLING thread and lets any
        /// exception they raise propagate straight back out — so one broken listener took the
        /// exception all the way out of <c>PlaceOrderAsync</c>, <b>after the position had already
        /// been opened</b>. Measured: the caller saw an <c>InvalidOperationException</c>, the
        /// healthy subscriber received nothing, and <c>GetPositionsAsync</c> returned one position.
        /// A trader told their order failed while holding it is the one outcome a simulated broker
        /// must never produce, and a live broker adapter would behave the same way.
        /// </para>
        ///
        /// <para>
        /// Each subscriber is isolated rather than the batch: a listener that throws loses its own
        /// notification and no one else's. Swallowing quietly would be the other bug — in this app
        /// an unreported error is inaudible — so it goes to the log, which is where the terminal's
        /// error surface reads from.
        /// </para>
        /// </summary>
        private void Emit(string id, string symbol, OrderSide side, double filledQty, double filledPx, double remaining, OrderStatus status, bool stop, bool tp, double? pnl = null, bool trailing = false, string? reason = null)
        {
            var update = new OrderUpdate(id, symbol, side, filledQty, filledPx, remaining, status, stop, tp, DateTime.UtcNow, pnl, trailing, reason);
            try
            {
                _orderUpdates.OnNext(update);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "A subscriber to the paper broker's order stream threw while handling {Status} for {Symbol} (order {OrderId}). "
                    + "The order itself is unaffected; the notification was lost for that subscriber.",
                    status, symbol, id);
            }
        }

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
                    // 1.0 is the default; persisting it would re-record the withdrawn
                    // leverage feature as state. Only meaningful values survive a save.
                    Leverage = _leverage.Where(kv => kv.Value > 1.0)
                        .Select(kv => new LevDto { Symbol = kv.Key, Value = kv.Value }).ToList(),
                    Open = _open.Select(o => new OrderDto
                    {
                        Id = o.Id, Symbol = o.Symbol, Side = o.Side.ToString(), Type = o.Type.ToString(),
                        Quantity = o.Quantity, Price = o.Price, Trigger = o.Trigger, Stop = o.IsStop, Tp = o.IsTp,
                        Trail = o.Trail?.ToString(), TrailValue = o.TrailValue, TrailAnchor = o.TrailAnchor,
                        Activation = o.Activation, Armed = o.Armed, Oco = o.OcoGroupId,
                        ReduceOnly = o.ReduceOnly,
                        Margin = o.MarginMode == MarginMode.None ? null : o.MarginMode.ToString(),
                        PlacedAt = o.PlacedAt, PlacedBarHigh = o.PlacedBarHigh,
                        PlacedBarLow = o.PlacedBarLow, PlacedBarClose = o.PlacedBarClose,
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
                    // Only the positions that actually have collateral behind them: a
                    // mode recorded against a long describes nothing, and reloading it
                    // would put a margin label on a spot holding.
                    Margins = _marginMode.Where(kv => CollateralOf(kv.Key) > 0 && kv.Value != MarginMode.None)
                        .Select(kv => new ModeDto { Symbol = kv.Key, Value = kv.Value.ToString() }).ToList(),
                    Charts = _exposureIdentity.Select(kv => new IdentDto
                    {
                        Symbol = kv.Key, Market = kv.Value.Market,
                        Provider = kv.Value.Provider, Timeframe = kv.Value.Timeframe
                    }).ToList(),
                    // Only aliases still pointing at live exposure are worth keeping —
                    // the map would otherwise grow a row per spelling ever typed.
                    Aliases = _ledgerAlias.Where(kv => IsLedgerKey(kv.Value))
                        .Select(kv => new AliasDto { From = kv.Key, To = kv.Value }).ToList()
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
                // Accounts written before leverage was withdrawn can carry entries above
                // MaxLeverage (e.g. a stale 3.0) that would be reported on positions as if
                // the feature still existed. Clamp on load; Persist() then drops the no-ops.
                foreach (var l in dto.Leverage) _leverage[l.Symbol] = Math.Clamp(l.Value, 1, MaxLeverage);
                foreach (var o in dto.Open)
                {
                    var restored = new PaperOrder(o.Id, o.Symbol,
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
                            o.Bracket.TrailTpValue, o.Bracket.TrailTpActivation, o.Bracket.Oco),
                        ParseMarginMode(o.Margin));
                    // Restored, not re-minted: stamping these with "now" would let a
                    // restart hand every resting order the whole current bar to fill
                    // against, which is the exploit this field closes.
                    restored.PlacedAt = o.PlacedAt;
                    restored.PlacedBarHigh = o.PlacedBarHigh;
                    restored.PlacedBarLow = o.PlacedBarLow;
                    restored.PlacedBarClose = o.PlacedBarClose;
                    _open.Add(restored);
                }
                if (dto.History != null) _history.AddRange(dto.History);
                foreach (var c in dto.Collateral ?? new List<LevDto>()) _collateral[c.Symbol] = c.Value;
                foreach (var m in dto.Margins ?? new List<ModeDto>())
                {
                    var mode = ParseMarginMode(m.Value);
                    if (mode != MarginMode.None) _marginMode[m.Symbol] = mode;
                }
                foreach (var c in dto.Charts ?? new List<IdentDto>())
                    _exposureIdentity[c.Symbol] = new ChartIdentity(c.Market, c.Provider, c.Symbol, c.Timeframe);
                foreach (var a in dto.Aliases ?? new List<AliasDto>())
                    if (!string.IsNullOrEmpty(a.From) && !string.IsNullOrEmpty(a.To))
                        _ledgerAlias[a.From] = a.To;
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
            // A shared account outlives the scope that handed it out. The container disposes
            // whatever a scoped factory returns, so obeying that here would close one user's
            // account the moment any ONE of their tabs went away, while the others kept trading
            // against a dead object. The hub owns the lifetime; see DisposeAccount.
            if (SharedOwnership) return;
            DisposeAccount();
        }

        /// <summary>
        /// Really tear down. Called by the owner, not by scope disposal.
        ///
        /// <para>
        /// <b>Idempotent, and it has to be, because two owners can both reach it at shutdown.</b>
        /// <c>PaperAccountHub</c> disposes the account by clearing <see cref="SharedOwnership"/>
        /// and calling this; any scope still holding the same instance then disposes it too, and
        /// with the flag now false <see cref="Dispose"/> no longer returns early. Container
        /// disposal order between two singletons is not something a caller can rely on, so the
        /// second call has to be a no-op rather than an <see cref="ObjectDisposedException"/> out
        /// of <c>Subject.OnCompleted</c> — which is what host shutdown started throwing the
        /// moment a long-lived headless scope began resolving the broker (Phase 2, 2026-09-06).
        /// </para>
        /// </summary>
        internal void DisposeAccount()
        {
            lock (_storeLock)
            {
                if (_accountDisposed) return;
                _accountDisposed = true;
                foreach (var e in _stores) { e.Sub.Dispose(); e.Monitor?.Dispose(); }
                _stores.Clear();
            }
            _primaryAttachment = null;
            _orderUpdates.OnCompleted();
            _orderUpdates.Dispose();
        }

        /// <summary>Set once by <see cref="DisposeAccount"/>, under <c>_storeLock</c>.</summary>
        private bool _accountDisposed;

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
            /// The margin mode this entry asked for, carried to the fill. A resting
            /// order can wait days; reading the mode off whatever the ticket happens
            /// to be set to when it finally triggers would margin the position on a
            /// choice made for a different trade.
            /// </summary>
            public MarginMode MarginMode { get; }

            /// <summary>
            /// Protective legs to attach when this order fills, for an entry that is
            /// still resting. Null for market entries (which attach immediately) and
            /// for orders carrying no protection.
            /// </summary>
            public BracketSpec? Bracket { get; }

            /// <summary>
            /// When this order joined the book, and what the bar it joined mid-way
            /// through had already printed by then. <see cref="EligibleRange"/> is the
            /// only reader; <see cref="Rest"/> is the only writer outside <c>Load</c>.
            /// <c>default</c> means unknown — an order restored from a state file
            /// written before these existed — and is treated as "the whole bar counts",
            /// which is the behaviour that shipped before.
            /// </summary>
            public DateTime PlacedAt { get; set; }
            public double? PlacedBarHigh { get; set; }
            public double? PlacedBarLow { get; set; }
            public double? PlacedBarClose { get; set; }

            public PaperOrder(string id, string symbol, OrderSide side, OrderType type, double quantity,
                double? price, double? trigger, bool isStop, bool isTp,
                TrailMode? trail = null, double? trailValue = null, double? trailAnchor = null,
                double? activation = null, bool armed = true, string? ocoGroupId = null,
                bool reduceOnly = false, BracketSpec? bracket = null,
                MarginMode marginMode = MarginMode.None)
            {
                Id = id; Symbol = symbol; Side = side; Type = type; Quantity = quantity;
                Price = price; Trigger = trigger; IsStop = isStop; IsTp = isTp;
                Trail = trail; TrailValue = trailValue; TrailAnchor = trailAnchor;
                Activation = activation; Armed = armed; OcoGroupId = ocoGroupId;
                ReduceOnly = reduceOnly; Bracket = bracket; MarginMode = marginMode;
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
            // How each short's collateral is held. Absent for accounts written before
            // cross margin existed, and those are all isolated — MarginModeOf reads
            // them that way rather than defaulting, so a restart cannot quietly move
            // an existing short's liquidation price.
            public List<ModeDto> Margins { get; set; } = new();
            // Chart spellings that resolved onto another position's key. Without these
            // a restart loses the venue lookup that produced them, and the fill engine
            // goes back to missing the position it should be filling against.
            public List<AliasDto> Aliases { get; set; } = new();
        }
        private sealed class AliasDto { public string From { get; set; } = ""; public string To { get; set; } = ""; }
        private sealed class PosDto { public string Symbol { get; set; } = ""; public double Qty { get; set; } public double Avg { get; set; } }
        private sealed class IdentDto
        {
            public string Symbol { get; set; } = "";
            public string Market { get; set; } = "";
            public string Provider { get; set; } = "";
            public string Timeframe { get; set; } = "";
        }
        private sealed class LevDto { public string Symbol { get; set; } = ""; public double Value { get; set; } }
        private sealed class ModeDto { public string Symbol { get; set; } = ""; public string Value { get; set; } = ""; }
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

            // The mode the entry asked for. Null in files written before cross margin
            // existed, and null is "did not ask" — the position it opens then lands on
            // the isolated behaviour those files were written under.
            public string? Margin { get; set; }

            // When the order joined the book, and what its bar had already printed by
            // then. Absent in files written before EligibleRange existed, which
            // deserialise to default/null and are treated as "the whole bar counts".
            public DateTime PlacedAt { get; set; }
            public double? PlacedBarHigh { get; set; }
            public double? PlacedBarLow { get; set; }
            public double? PlacedBarClose { get; set; }

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
