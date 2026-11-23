using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Gemini
{
    /// <summary>
    /// Gemini — US-regulated (NYDFS) spot exchange.
    ///
    /// <para>
    /// **The sandbox is a first-class target.** <c>api.sandbox.gemini.com</c> is a
    /// real environment with self-service accounts (exchange.sandbox.gemini.com),
    /// its own keys, and paper funds — reachable from the US, which is what made
    /// this venue first in the expansion order after Bybit's API turned out to be
    /// geo-blocked. Environment = Paper on the key profile routes here. Sandbox
    /// keys and live keys are different credentials from different sites; using
    /// one against the other fails auth on every call.
    /// </para>
    ///
    /// <para>
    /// **Gemini has no native market order.** The API accepts limit orders only
    /// (plus stop-limits), so <see cref="OrderType.Market"/> is emulated the way
    /// Gemini's own documentation prescribes: an immediate-or-cancel LIMIT priced
    /// through the book (1% past the current touch). The consequence is stated
    /// rather than hidden — the emulation bounds slippage at that 1%, and in a
    /// thin market the unfilled remainder cancels instead of walking the book,
    /// which is a market order with a safety property rather than a lie about one.
    /// </para>
    ///
    /// <para>
    /// Refusals arrive as JSON <c>{result: "error", reason, message}</c> — on
    /// non-200 statuses, unlike both Kraken APIs' 200-with-error-body. The
    /// transport reads the body regardless of status, because the body is where
    /// the reason lives and an <c>HttpRequestException</c> would discard it.
    /// </para>
    /// </summary>
    public class GeminiProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider, ITradeStreamProvider
    {
        private const string LiveHost    = "https://api.gemini.com";
        private const string SandboxHost = "https://api.sandbox.gemini.com";

        private readonly HttpClient _http;
        private readonly Subject<OrderUpdate> _orderUpdates = new();

        // Gemini's documented public REST limit (120 requests/minute).
        private readonly RateLimiter _publicLimiter = new(120, TimeSpan.FromMinutes(1));
        // Deliberately far below Gemini's 600/min private cap: the nonce is
        // SECOND-resolution with a monotonic bump (GeminiAuth.Nonce), so every
        // same-second call pushes the nonce one second into the future, and the
        // venue rejects anything more than ~30s ahead as InvalidNonce — which
        // reads to the user as a credentials failure. At ≤30 calls per rolling
        // 30s the worst-case forward drift stays under the 30s window, so the
        // limiter is what keeps the nonce valid, not just politeness.
        private readonly RateLimiter _privateLimiter = new(30, TimeSpan.FromSeconds(30));

        private string _apiKey = "";
        private string _apiSecret = "";
        private bool _useSandbox;

        // Symbol details (smallest order, amount step, price increment, status) read
        // per symbol and kept for hours: every order is checked and formatted against
        // them, and the market-order emulation rounds its aggressive price to the
        // quote increment or the order is rejected for precision.
        private readonly Dictionary<string, (DateTime At, SymbolOrderRules Rules)> _symbolRules = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly TimeSpan SymbolRulesTtl = TimeSpan.FromHours(6);

        // Account reads are remembered for a few seconds. The dashboard asks for
        // balances, open orders and fills every two seconds — about 45 private calls
        // per 30s against a limiter that must stay at 30 for the nonce's sake (above),
        // so the queue never drained and an order or a close waited behind it.
        // Emptied by every order, every cancel, and every status read that shows a
        // fill or an order ending (Gemini's stand-in for an execution report).
        private readonly AccountReadCache _reads;
        private readonly Func<DateTime> _clock;
        internal static readonly TimeSpan AccountReadTtl = TimeSpan.FromSeconds(15);
        internal static readonly TimeSpan HistoryReadTtl = TimeSpan.FromSeconds(60);

        // The last state each polled order was seen in, so that only a CHANGE empties
        // the read cache: a part-filled order resting for hours is polled the whole
        // time, and emptying on every poll would bring the queue back.
        private readonly Dictionary<string, (bool Live, double Executed)> _lastOrderState = new(StringComparer.Ordinal);

        public GeminiProvider() : this(() => DateTime.UtcNow) { }

        // The clock is a seam for the read-cache and rules tests, which step time
        // instead of sleeping through it.
        internal GeminiProvider(Func<DateTime> clock)
        {
            _clock = clock;
            _reads = new AccountReadCache(clock);
            _http = PluginHostServices.CreateHttpClient(
                providerId:   "Gemini",
                allowedHosts: new[] { "api.gemini.com", "api.sandbox.gemini.com" });
        }

        private string Host => _useSandbox ? SandboxHost : LiveHost;

        // ── IProviderPlugin ───────────────────────────────────────────────────

        public override string Name => "Gemini";
        public override string Description => "Gemini — US-regulated spot crypto exchange";

        /// <summary>
        /// Declared from what this plugin actually does. Spot only: no leverage, no
        /// margin, no shorting, no futures — Gemini's derivatives platform is a
        /// separate non-US product with separate keys, exactly the situation that
        /// makes it a separate plugin if ever wanted. PostOnly maps to Gemini's
        /// maker-or-cancel option and TimeInForce to immediate-or-cancel /
        /// fill-or-kill; both are sent, not merely accepted.
        /// </summary>
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.PostOnly | ProviderCapabilities.TimeInForce;

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider))    return this as T;
            if (typeof(T) == typeof(IOrderBookProvider))  return this as T;
            return null;
        }

        public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;          // public market data needs none
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => false;     // REST polling; WS is a later slice

        /// <summary>Gemini has no order push channel: OrderUpdateStream is a dead subject and
        /// fills are resolved by the order-status poller. Measured 2026-09-07 — subscribing
        /// SUCCEEDS and emits nothing, forever, which is why the static fact needs saying.</summary>
        public bool ProvidesOrderStream => false;
        public override ProviderEnvironment Environment => _useSandbox ? ProviderEnvironment.Paper : ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 1440;         // /v2/candles returns a fixed window; 1440 rows measured on 1m
        public override List<string> NativelySupportedTimeframes =>
            new() { "1m", "5m", "15m", "30m", "1h", "6h", "1d" };

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var k)) _apiKey = k;
            if (config.TryGetValue("ApiSecret", out var s)) _apiSecret = s;
            // A Paper profile routes to the sandbox, which is a real environment
            // with its own keys — not a simulation we run.
            // ProviderConfigKeys polarity: anything not explicitly Live is the sandbox. The old
            // compare branched on "Paper", so a MISSING environment meant live.
            _useSandbox = !ProviderConfigKeys.IsLive(config);
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) return (true, "No API key provided (public market data only)");
            try
            {
                await PostPrivateAsync("/v1/balances").ConfigureAwait(false);
                return (true, $"API key validated against {(_useSandbox ? "the sandbox" : "the live venue")}");
            }
            catch (Exception ex) { return (false, $"Gemini key check failed: {ex.Message}"); }
        }

        // ── Market data ───────────────────────────────────────────────────────

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Spot" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            try
            {
                var json = await GetPublicAsync("/v1/symbols").ConfigureAwait(false);
                return (json as JArray ?? new JArray())
                    .Select(s => s.ToString().ToUpperInvariant())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex)
            {
                SurfaceError($"Gemini symbols failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return new List<string>();
            }
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(
            MarketDataRequest request)
        {
            var bars = new List<Ohlcv>();
            var vols = new List<(long, double)>();
            try
            {
                string res = MapResolution(request.Timeframe);
                var json = await GetPublicAsync(
                    $"/v2/candles/{Uri.EscapeDataString(request.Symbol.ToLowerInvariant())}/{res}")
                    .ConfigureAwait(false);

                // Rows arrive NEWEST FIRST — [time_ms, open, high, low, close, volume]
                // in descending time. Reversed here so the chart gets ascending bars;
                // consuming them in arrival order would draw the chart mirrored.
                foreach (var c in (json as JArray ?? new JArray()).Reverse())
                {
                    long ms = c[0]?.Value<long>() ?? 0;
                    var t = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                    double o = D(c[1]), h = D(c[2]), l = D(c[3]), cl = D(c[4]), v = D(c[5]);
                    bars.Add(new Ohlcv(t, o, h, l, cl, v));
                    vols.Add((ms / 1000, v));
                }

                (bars, vols) = WindowedBars.Apply(request, bars, vols, "Gemini", request.Symbol, SurfaceWindowMiss);
            }
            catch (Exception ex)
            {
                SurfaceError($"Gemini candles failed for {request.Symbol}: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure); swallowing them left an empty chart as the
                // only symptom of a dead venue. The scan gate never saw this method —
                // its signature is line-wrapped — which is how this one outlived the
                // fleet-wide fix.
                if (TransportFailure.IsTransient(ex)) throw;
            }
            return (bars, vols);
        }

        private void SurfaceWindowMiss(string message) =>
            SurfaceError(message, ErrorSeverity.Medium, ErrorCategory.Provider);

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(
            string symbol, int limit = 10)
        {
            try
            {
                var json = await GetPublicAsync(
                    $"/v1/book/{Uri.EscapeDataString(symbol.ToLowerInvariant())}?limit_bids={limit}&limit_asks={limit}")
                    .ConfigureAwait(false);
                return (Side(json["bids"] as JArray, limit), Side(json["asks"] as JArray, limit));
            }
            catch (Exception ex)
            {
                SurfaceError($"Gemini order book failed for {symbol}: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        private static List<OrderBookEntry> Side(JArray? rows, int limit) =>
            (rows ?? new JArray()).Take(limit)
                .Select(r => new OrderBookEntry(D(r["price"]), D(r["amount"])))
                .ToList();

        // ── IOrderBookProvider ────────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth).ConfigureAwait(false);
            // Sequence 0: the REST snapshot carries no sequence number, and inventing
            // one would let a consumer believe it could order updates against it.
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        /// <summary>
        /// Every trade on the symbol with the side that took it, for true buy/sell volume
        /// (<see cref="ITradeStreamProvider"/>). Gemini's v1 market data socket with trades only
        /// (no book) and heartbeats; it sends no trades from before connecting. Every message is
        /// numbered (<c>socket_sequence</c>): a gap means one was lost, so the stream counts as
        /// dropped and up again, and the bar it fell in is not counted.
        /// </summary>
        public async Task<IAsyncDisposable> SubscribeTradesAsync(string market, string symbol,
            Action<PublicTrade> onTrade, Action<bool> onCounting)
        {
            string pair = symbol.Replace("/", "").Replace("-", "").ToUpperInvariant();
            string url = Host.Replace("https://", "wss://") +
                         $"/v1/marketdata/{Uri.EscapeDataString(pair)}?trades=true&heartbeat=true&bids=false&offers=false&auctions=false";
            long lastSeq = -1;
            var ws = new ReconnectingWebSocket(url, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnError(err => _errorStream.OnNext($"Gemini trade stream ({pair}): {err}"));
            ws.OnConnected(_ =>
                {
                    Interlocked.Exchange(ref lastSeq, -1);   // numbering starts again on a new socket
                    onCounting(true);
                    return Task.CompletedTask;
                })
              .OnDisconnected(() => onCounting(false))
              .OnMessage(msg =>
                {
                    var (seq, trades) = ReadPublicTrades(msg);
                    if (MessageSequence.IsGap(ref lastSeq, seq)) { onCounting(false); onCounting(true); }
                    foreach (var t in trades) onTrade(t);
                });
            await ws.ConnectAsync();
            return ws;
        }

        /// <summary>
        /// A v1 market data message's <c>socket_sequence</c> (-1 if it has none) and its trades.
        /// Gemini's v1 <c>makerSide</c> is the side that was resting, so <c>ask</c> means a buyer
        /// took the trade: checked live 2026-11-23 against the v2 feed's taker side for the same
        /// trade ids, opposite on 97 of 97. An auction trade has no taker and is left out.
        /// </summary>
        internal static (long Sequence, List<PublicTrade> Trades) ReadPublicTrades(string msg)
        {
            var trades = new List<PublicTrade>();
            long seq = -1;
            try
            {
                var json = JObject.Parse(msg);
                if (json["socket_sequence"] is JValue { Type: JTokenType.Integer } n) seq = n.Value<long>();
                if (json["type"]?.ToString() != "update" || json["events"] is not JArray events) return (seq, trades);
                if (json["timestampms"] is not JValue { Type: JTokenType.Integer } ms) return (seq, trades);
                var at = DateTimeOffset.FromUnixTimeMilliseconds(ms.Value<long>()).UtcDateTime;
                foreach (var e in events)
                {
                    if (e["type"]?.ToString() != "trade") continue;
                    OrderSide? taker = e["makerSide"]?.ToString() switch { "ask" => OrderSide.Buy, "bid" => OrderSide.Sell, _ => null };
                    if (taker == null) continue;
                    if (!double.TryParse(e["price"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double price) || price <= 0) continue;
                    if (!double.TryParse(e["amount"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) || amount <= 0) continue;
                    trades.Add(new PublicTrade(at, price, amount, taker.Value));
                }
            }
            catch { /* malformed frame */ }
            return (seq, trades);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) =>
            Observable.Empty<OrderBookUpdate>();   // websocket streaming is a later slice

        // ── ITradingProvider ──────────────────────────────────────────────────

        public bool IsConnected => !string.IsNullOrEmpty(_apiKey);
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdates.AsObservable();

        // No order-event push in this plugin yet, so the order service must poll
        // rather than wait on a stream that never emits.
        public bool SupportsOrderEventStreaming => false;

        // Gemini has a real order-by-id lookup (/v1/order/status), so the poller
        // can resolve a placed order authoritatively instead of guessing from the
        // open-orders + fills heuristic.
        public bool SupportsOrderStatusQuery => true;

        public async Task<List<Balance>> GetBalancesAsync()
        {
            // Each caller gets its own list: one changing it cannot change what the
            // next caller inside the time-to-live sees.
            var rows = await _reads.GetAsync("balances", AccountReadTtl, async () =>
            {
                var json = await PostPrivateAsync("/v1/balances").ConfigureAwait(false);
                return (json as JArray ?? new JArray())
                    .Select(b =>
                    {
                        double amount = D(b["amount"]), available = D(b["available"]);
                        return new Balance(
                            b["currency"]?.ToString().ToUpperInvariant() ?? "",
                            available,
                            Math.Max(0, amount - available));
                    })
                    .Where(b => b.Free > 0 || b.Locked > 0)
                    .ToList();
            }).ConfigureAwait(false);
            return rows.ToList();
        }

        // Spot venue: there are no positions, only balances. An empty list is the
        // truthful answer, not a stub.
        public Task<List<Position>> GetPositionsAsync() => Task.FromResult(new List<Position>());

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            // /v1/orders is the whole account in one call, so it is remembered whole
            // and filtered per caller: a per-symbol key would ask once per symbol.
            var all = await _reads.GetAsync("orders", AccountReadTtl, async () =>
                (await PostPrivateAsync("/v1/orders").ConfigureAwait(false)) as JArray ?? new JArray())
                .ConfigureAwait(false);
            return all
                .Where(o => symbol == null ||
                            string.Equals(o["symbol"]?.ToString(), symbol, StringComparison.OrdinalIgnoreCase))
                .Select(o => new OpenOrder(
                    o["order_id"]?.ToString() ?? "",
                    o["symbol"]?.ToString().ToUpperInvariant() ?? "",
                    string.Equals(o["side"]?.ToString(), "sell", StringComparison.OrdinalIgnoreCase)
                        ? OrderSide.Sell : OrderSide.Buy,
                    MapOrderType(o["type"]?.ToString()),
                    D(o["remaining_amount"]), D(o["price"]),
                    o["is_live"]?.Value<bool>() == true ? "open" : "closed"))
                .ToList();
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (string.IsNullOrEmpty(_apiKey)) return "ORDER_FAILED:no Gemini API key configured";

            string symbol = signal.Symbol.ToLowerInvariant();

            // Read BEFORE the order takes its limiter slot, and refused in words here
            // rather than by Gemini's "InvalidQuantity" after the fact. Null when the
            // details cannot be read: the venue then judges the order as it always
            // did, rather than a failed public lookup blocking a close.
            var rules = await GetSymbolRulesAsync(symbol).ConfigureAwait(false);
            string? refusal = rules?.Refusal(signal.Quantity,
                signal.Price is > 0 ? signal.Price : signal.TriggerPrice is > 0 ? signal.TriggerPrice : null);
            if (refusal != null) return $"ORDER_FAILED:{refusal}";

            var payload = new JObject
            {
                ["symbol"] = symbol,
                ["side"]   = signal.Side == OrderSide.Buy ? "buy" : "sell",
                ["amount"] = rules?.FormatQuantity(signal.Quantity) ?? Fmt(signal.Quantity),
            };
            if (!string.IsNullOrEmpty(signal.ClientOid)) payload["client_order_id"] = signal.ClientOid;

            switch (signal.Type)
            {
                case OrderType.Limit:
                    if (signal.Price is not > 0)
                        return "ORDER_FAILED:a limit order needs a limit price";
                    payload["type"]  = "exchange limit";
                    payload["price"] = rules?.FormatPrice(signal.Price.Value) ?? Fmt(signal.Price.Value);
                    break;

                case OrderType.Market:
                {
                    // Emulated per Gemini's own prescription: IOC limit priced 1%
                    // through the touch. See the class doc — this bounds slippage
                    // where a true market order would not.
                    double? aggressive = await AggressivePriceAsync(symbol, signal.Side).ConfigureAwait(false);
                    if (aggressive is null)
                        return "ORDER_FAILED:Gemini has no native market order and the current price "
                             + "could not be read to emulate one — try a limit order";
                    payload["type"]    = "exchange limit";
                    payload["price"]   = rules?.FormatPrice(aggressive.Value) ?? Fmt(aggressive.Value);
                    payload["options"] = new JArray("immediate-or-cancel");
                    break;
                }

                case OrderType.StopLimit:
                {
                    double? trigger = signal.TriggerPrice is > 0 ? signal.TriggerPrice : signal.StopLoss;
                    if (trigger is not > 0 || signal.Price is not > 0)
                        return "ORDER_FAILED:a stop-limit order needs both a trigger price and a limit price";
                    payload["type"]       = "exchange stop limit";
                    payload["stop_price"] = rules?.FormatPrice(trigger.Value) ?? Fmt(trigger.Value);
                    payload["price"]      = rules?.FormatPrice(signal.Price.Value) ?? Fmt(signal.Price.Value);
                    break;
                }

                default:
                    // Stop-market and take-profit types do not exist on this venue.
                    // Refused by NAME rather than silently downgraded to something
                    // that fills at a different time than the user asked for.
                    return $"ORDER_FAILED:Gemini does not support {signal.Type} orders — "
                         + "it offers limit and stop-limit only";
            }

            // Options are mutually exclusive on Gemini (one per order). PostOnly
            // wins over a TimeInForce because resting as maker is a price guarantee
            // and IOC is only a timing one; the combination is impossible to send.
            if (signal.PostOnly && signal.Type == OrderType.Limit)
                payload["options"] = new JArray("maker-or-cancel");
            else if (signal.Type == OrderType.Limit && !string.IsNullOrEmpty(signal.TimeInForce))
            {
                if (signal.TimeInForce.Equals("IOC", StringComparison.OrdinalIgnoreCase))
                    payload["options"] = new JArray("immediate-or-cancel");
                else if (signal.TimeInForce.Equals("FOK", StringComparison.OrdinalIgnoreCase))
                    payload["options"] = new JArray("fill-or-kill");
            }

            try
            {
                JToken json;
                try { json = await PostPrivateAsync("/v1/order/new", payload).ConfigureAwait(false); }
                // Emptied whatever the answer: a refusal can still follow a booking
                // the response lost, and the next read must ask the venue.
                finally { _reads.Invalidate(); }

                string? orderId = json["order_id"]?.ToString();
                bool cancelled = json["is_cancelled"]?.Value<bool>() == true;
                double executed = D(json["executed_amount"]);

                // An IOC that found no liquidity, or a maker-or-cancel that would
                // have crossed, comes back already-cancelled with nothing executed —
                // HTTP 200, valid order id. Returning that id as success would
                // report an order that no longer exists as working.
                if (cancelled && executed <= 0)
                    return "ORDER_FAILED:Gemini cancelled the order immediately without filling any of it "
                         + "(no liquidity at the price, or a maker-only order that would have crossed the book)";

                return orderId ?? "ORDER_FAILED:Gemini accepted the order but returned no id";
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            try
            {
                JToken json;
                try
                {
                    json = await PostPrivateAsync("/v1/order/cancel",
                        new JObject { ["order_id"] = long.TryParse(orderId, out long id) ? id : orderId })
                        .ConfigureAwait(false);
                }
                finally { _reads.Invalidate(); }
                // The response is the order's state after the attempt: is_cancelled
                // false here means it was already filled (or never known).
                return json["is_cancelled"]?.Value<bool>() == true;
            }
            catch (Exception ex)
            {
                SurfaceError($"Gemini cancel failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return false;
            }
        }

        /// <summary>
        /// Spot venue with no margin product: there is nothing for leverage to
        /// apply to, and pretending to set it would be the exact no-op the
        /// capability audit exists to catch. Always returns 1.
        /// </summary>
        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        public async Task<OrderStatusSnapshot?> GetOrderStatusAsync(string orderId, string? symbol = null)
        {
            // No catch: the order poller counts consecutive failures and gives up
            // with a spoken warning. Returning null here read as "still resolving"
            // and turned a dead endpoint into a silent infinite retry.
            var json = await PostPrivateAsync("/v1/order/status",
                new JObject { ["order_id"] = long.TryParse(orderId, out long id) ? id : orderId })
                .ConfigureAwait(false);

            bool live      = json["is_live"]?.Value<bool>() == true;
            bool cancelled = json["is_cancelled"]?.Value<bool>() == true;
            double executed  = D(json["executed_amount"]);
            double remaining = D(json["remaining_amount"]);

            // Gemini has no execution reports; this poll is where a fill or an order
            // ending is first seen, so it is where the remembered balances and open
            // orders stop being true. Only a change counts (see _lastOrderState).
            lock (_lastOrderState)
            {
                bool changed = _lastOrderState.TryGetValue(orderId, out var seen)
                    ? seen != (live, executed)
                    : !live || executed > 0;   // first sighting: only an order already acting
                if (changed) _reads.Invalidate();
                // An ended order is not polled again, so it is not kept.
                if (live) _lastOrderState[orderId] = (live, executed);
                else      _lastOrderState.Remove(orderId);
            }

            var state = MapOrderState(live, cancelled, executed, remaining);

            return new OrderStatusSnapshot(
                state,
                string.Equals(json["side"]?.ToString(), "sell", StringComparison.OrdinalIgnoreCase)
                    ? OrderSide.Sell : OrderSide.Buy,
                json["symbol"]?.ToString().ToUpperInvariant() ?? symbol ?? "",
                executed,
                D(json["avg_execution_price"]),
                remaining);
        }

        /// <summary>
        /// Order of the tests matters. Gemini has no native market order —
        /// OrderType.Market is emulated as an IOC limit, so the DEFAULT order
        /// type on this venue is the one most likely to partially fill and then
        /// cancel. The old ladder tested is_cancelled before executed, so that
        /// case reported a bare "cancelled" while the trader owned coins. Now:
        /// everything-executed is Filled no matter what the cancel flag says
        /// about the zero remainder, and a cancel-after-partial reports
        /// Cancelled WITH the executed amount in the snapshot — the
        /// announcement speaks the fill. Internal for direct testing.
        /// </summary>
        internal static PolledOrderState MapOrderState(bool live, bool cancelled, double executed, double remaining) =>
            live && executed > 0           ? PolledOrderState.PartiallyFilled :
            live                           ? PolledOrderState.Working :
            executed > 0 && remaining <= 0 ? PolledOrderState.Filled :
            cancelled                      ? PolledOrderState.Cancelled :
            executed > 0                   ? PolledOrderState.Filled :
                                             PolledOrderState.Cancelled;

        /// <summary>
        /// Recent fills via <c>/v1/mytrades</c>. Sandbox caveat, verified live
        /// 2026-08-05: the sandbox returns an EMPTY list here even minutes after a
        /// confirmed fill (the fill is real — order/status shows it — the sandbox
        /// just does not serve trade history). Order resolution does not depend on
        /// this: the poller uses <see cref="GetOrderStatusAsync"/>, which reports
        /// fills correctly in both environments.
        /// </summary>
        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            var payload = new JObject { ["limit_trades"] = Math.Clamp(limit, 1, 500) };
            // Gemini scopes /v1/mytrades to one symbol per call; omitting it scopes
            // to the account default. Sent only when the caller supplied one.
            if (!string.IsNullOrEmpty(symbol)) payload["symbol"] = symbol.ToLowerInvariant();

            // History changes only by a fill, and a fill empties the cache (order,
            // cancel, status poll), so it is kept longer than balances. Keyed by what
            // was asked: a different symbol or length is a different answer.
            var json = await _reads.GetAsync($"fills|{symbol?.ToLowerInvariant()}|{limit}", HistoryReadTtl, async () =>
                await PostPrivateAsync("/v1/mytrades", payload).ConfigureAwait(false))
                .ConfigureAwait(false);
            return (json as JArray ?? new JArray())
                .Take(limit)
                .Select(f => new TradeFill(
                    f["tid"]?.ToString() ?? "",
                    (f["symbol"]?.ToString() ?? symbol ?? "").ToUpperInvariant(),
                    string.Equals(f["type"]?.ToString(), "sell", StringComparison.OrdinalIgnoreCase)
                        ? OrderSide.Sell : OrderSide.Buy,
                    D(f["amount"]), D(f["price"]),
                    DateTimeOffset.FromUnixTimeMilliseconds(f["timestampms"]?.Value<long>() ?? 0).UtcDateTime,
                    Fee: D(f["fee_amount"]),
                    OrderId: f["order_id"]?.ToString()))
                .ToList();
        }

        // ── Market-order emulation ────────────────────────────────────────────

        /// <summary>
        /// The IOC limit price for an emulated market order: 1% through the current
        /// touch, rounded to the venue's quote increment (an unrounded price is
        /// rejected for precision, which would make market orders fail only on
        /// symbols with coarse ticks — a bug that hides until the wrong symbol).
        /// </summary>
        private async Task<double?> AggressivePriceAsync(string symbol, OrderSide side)
        {
            var ticker = await GetPublicAsync($"/v1/pubticker/{Uri.EscapeDataString(symbol)}").ConfigureAwait(false);
            double touch = side == OrderSide.Buy ? D(ticker["ask"]) : D(ticker["bid"]);
            if (touch <= 0) return null;

            double raw = side == OrderSide.Buy ? touch * 1.01 : touch * 0.99;
            double inc = (double)((await GetSymbolRulesAsync(symbol).ConfigureAwait(false))?.PriceTick ?? 0m);
            return inc > 0 ? Math.Round(raw / inc) * inc : raw;
        }

        /// <summary>
        /// What Gemini takes on <paramref name="symbol"/>, from <c>/v1/symbols/details</c>,
        /// read at most every six hours. Gemini's names are not the obvious ones:
        /// <c>tick_size</c> is the AMOUNT step and <c>quote_increment</c> the price step
        /// (live 2026-11-10: BTCUSD 0.00000001 and 0.01, minimum "0.00001"; SOLUSD
        /// 0.000001 and 0.001, minimum "0.001"). Steps arrive as JSON numbers and the
        /// minimum as a string, so both are read as text. Null when the details cannot be
        /// read — the caller then sends as before.
        /// </summary>
        internal async Task<SymbolOrderRules?> GetSymbolRulesAsync(string symbol)
        {
            lock (_symbolRules)
            {
                if (_symbolRules.TryGetValue(symbol, out var kept) && _clock() - kept.At < SymbolRulesTtl)
                    return kept.Rules;
            }

            JObject details;
            try
            {
                details = (JObject)await GetPublicAsync(
                    $"/v1/symbols/details/{Uri.EscapeDataString(symbol)}").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SurfaceError($"Gemini's order rules for {symbol.ToUpperInvariant()} could not be read: {ex.Message}",
                    ErrorSeverity.Low, ErrorCategory.Provider);
                return null;
            }

            string baseAsset  = details["base_currency"]?.ToString().ToUpperInvariant() ?? "";
            string quoteAsset = details["quote_currency"]?.ToString().ToUpperInvariant() ?? "";
            string status     = details["status"]?.ToString() ?? "";
            string spoken     = baseAsset.Length > 0 && quoteAsset.Length > 0
                ? $"{baseAsset}/{quoteAsset}" : symbol.ToUpperInvariant();

            var rules = new SymbolOrderRules
            {
                Venue        = "Gemini",
                Symbol       = spoken,
                QuantityUnit = baseAsset,
                QuoteAsset   = quoteAsset,
                MinQuantity  = SymbolOrderRules.ParseStep(Text(details["min_order_size"])),
                QuantityStep = SymbolOrderRules.ParseStep(Text(details["tick_size"])),
                PriceTick    = SymbolOrderRules.ParseStep(Text(details["quote_increment"])),
                // Gemini's other states (closed, cancel_only, post_only, limit_only) all
                // refuse some order this terminal can send; the status is named so the
                // user hears which.
                NotTrading   = string.Equals(status, "open", StringComparison.OrdinalIgnoreCase) ? null
                             : $"Gemini is not taking new {spoken} orders (the market is {(status.Length > 0 ? status.Replace('_', ' ') : "not open")})",
            };
            lock (_symbolRules) _symbolRules[symbol] = (_clock(), rules);
            return rules;
        }

        // A JSON number as written, in the invariant culture: JValue.ToString() follows the
        // current culture and would read 0.01 as "0,01" on a comma-decimal machine.
        private static string? Text(JToken? t) =>
            t is JValue v ? Convert.ToString(v.Value, CultureInfo.InvariantCulture) : null;

        // ── Transport ─────────────────────────────────────────────────────────

        /// <summary>
        /// Public GET. Reads the body regardless of HTTP status: Gemini reports
        /// refusals as non-200 WITH a JSON reason, and GetStringAsync would throw
        /// away exactly the part that says what went wrong.
        /// </summary>
        private async Task<JToken> GetPublicAsync(string pathAndQuery)
        {
            await _publicLimiter.WaitAsync().ConfigureAwait(false);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Host}{pathAndQuery}");
            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            return ReadJson(res, await res.Content.ReadAsStringAsync().ConfigureAwait(false), pathAndQuery);
        }

        /// <summary>
        /// Body first, then status — and neither one alone.
        ///
        /// <para>
        /// Reading the body regardless of status is right, and it used to be the ONLY check:
        /// <see cref="ThrowOnError"/> fires only for a body shaped <c>{"result":"error"}</c>, so
        /// any other non-2xx — an edge 401, a CDN 429, a proxy 502, a 451 geo-block of the kind
        /// Binance answers with — parsed as ordinary JSON and came back as a success carrying
        /// nothing. <c>GetAvailableSymbolsAsync</c> then read <c>json as JArray ?? new JArray()</c>
        /// and returned an EMPTY LIST with no exception to catch and nothing said, which is the
        /// same silent-empty failure the Twelve Data symbol list shipped. Found by
        /// <c>ProviderSymbolListSilenceTests</c>, not by a report.
        /// </para>
        ///
        /// <para>
        /// Order matters: Gemini's own error shape is classified first, because
        /// <see cref="ThrowOnError"/> turns an auth-shaped reason into an
        /// <see cref="UnauthorizedAccessException"/> the order service acts on, where a bare
        /// status would flatten it back to "something went wrong".
        /// </para>
        /// </summary>
        private JToken ReadJson(HttpResponseMessage res, string body, string what)
        {
            JToken? json = null;
            JsonException? unparseable = null;
            try { json = JToken.Parse(body); }
            catch (JsonException ex) { unparseable = ex; }   // an HTML error page is not JSON

            if (json is not null) ThrowOnError(json);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Gemini refused {what}: HTTP {(int)res.StatusCode}.", null, res.StatusCode);

            return json ?? throw unparseable!;
        }

        // Master-scoped keys (the sandbox's default kind) must name the account in
        // every payload; account-scoped keys must not. Which kind THIS key is only
        // shows at the first refusal, so it is learned once from the venue's own
        // answer rather than asked of the user — who was never shown the
        // distinction when Gemini created the key.
        private bool _sendAccountField;

        /// <summary>
        /// Private POST. Everything — the endpoint path included — travels in the
        /// signed payload headers; the body is empty by the venue's design.
        /// </summary>
        private async Task<JToken> PostPrivateAsync(string path, JObject? args = null)
        {
            try
            {
                return await PostPrivateOnceAsync(path, args, _sendAccountField).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (!_sendAccountField && ex.Message.Contains("MissingAccounts"))
            {
                // A master key. "primary" is the account every Gemini master
                // account starts with; remembered so every later call skips the
                // failed first attempt.
                _sendAccountField = true;
                return await PostPrivateOnceAsync(path, args, true).ConfigureAwait(false);
            }
        }

        private async Task<JToken> PostPrivateOnceAsync(string path, JObject? args, bool withAccount)
        {
            // The limiter must run BEFORE the nonce is stamped: a request that
            // queued behind WaitAsync with a pre-stamped nonce could leave the
            // venue's 30-second acceptance window while it waited.
            await _privateLimiter.WaitAsync().ConfigureAwait(false);
            var payload = args is null ? new JObject() : (JObject)args.DeepClone();
            payload["request"] = path;
            payload["nonce"]   = GeminiAuth.Nonce();
            if (withAccount) payload["account"] = "primary";

            string b64 = GeminiAuth.Payload(payload.ToString(Formatting.None));
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Host}{path}");
            req.Headers.Add("X-GEMINI-APIKEY", _apiKey);
            req.Headers.Add("X-GEMINI-PAYLOAD", b64);
            req.Headers.Add("X-GEMINI-SIGNATURE", GeminiAuth.Sign(b64, _apiSecret));
            req.Headers.Add("Cache-Control", "no-cache");
            req.Content = new StringContent("", Encoding.UTF8, "text/plain");

            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            return ReadJson(res, await res.Content.ReadAsStringAsync().ConfigureAwait(false), path);
        }

        /// <summary>
        /// A permission problem is fixable by the user; a fault is not. The order
        /// service classifies on this distinction, so auth-shaped reasons become
        /// <see cref="UnauthorizedAccessException"/> — with the one hint that
        /// resolves most of them: sandbox and live keys are separate credentials.
        /// </summary>
        private void ThrowOnError(JToken json)
        {
            if (json is not JObject obj ||
                !string.Equals(obj["result"]?.ToString(), "error", StringComparison.OrdinalIgnoreCase))
                return;

            string reason  = obj["reason"]?.ToString() ?? "unspecified";
            string message = obj["message"]?.ToString() ?? "";

            // MissingRole: the key is real and correctly signed, it just lacks a
            // scope — and Gemini's own message already names the key, the missing
            // roles, and the settings page. Appending the sandbox-vs-live hint
            // there would point at the one thing that is NOT wrong. Verified live:
            // a fresh sandbox key ships as Auditor (read-only) until the Trader
            // role is assigned on the site.
            if (reason is "MissingRole")
                throw new UnauthorizedAccessException($"Gemini refused the request ({reason}): {message}");

            // InvalidNonce is a TIMING fault, not a key problem: the identical
            // request retried a second later succeeds. It used to sit in the
            // credentials branch below, which told the user to go check their
            // keys — the wrong place to debug it. The private-lane rate limiter
            // is what prevents it; if it still fires, say what it actually is.
            if (reason is "InvalidNonce")
                throw new HttpRequestException(
                    $"Gemini rejected the request nonce ({reason}): {message}. This is a transient "
                  + "timing fault (requests arriving too fast or the clock drifting), not a "
                  + "credentials problem — retry the operation; the key itself is fine.");

            if (reason is "InvalidApiKey" or "InvalidSignature" or "MissingSecurityHeaders"
                       or "NotAuthorized")
                throw new UnauthorizedAccessException(
                    $"Gemini refused the credentials ({reason}): {message}. Sandbox and live keys are "
                  + $"separate — this profile targets {(_useSandbox ? "the SANDBOX (exchange.sandbox.gemini.com)" : "the LIVE venue (exchange.gemini.com)")}, "
                  + "so the key must come from there.");

            throw new InvalidOperationException($"Gemini refused the request ({reason}): {message}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static string MapResolution(string timeframe) => timeframe.ToLowerInvariant() switch
        {
            "1m"  => "1m",  "5m" => "5m", "15m" => "15m", "30m" => "30m",
            "1h"  => "1hr", "6h" => "6hr",
            "1d"  => "1day",
            _     => "1hr",
        };

        private static OrderType MapOrderType(string? t) => t?.ToLowerInvariant() switch
        {
            "exchange limit"      => OrderType.Limit,
            "exchange stop limit" or "stop-limit" => OrderType.StopLimit,
            _                     => OrderType.Limit,
        };

        private static double D(JToken? t) =>
            double.TryParse(t?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static string Fmt(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);

        // ── Connection lifecycle ──────────────────────────────────────────────
        //
        // REST-only for now: every read is a fresh request, so there is no session
        // to open, keep or tear down. Declared explicitly rather than left to a
        // default so it is obvious this is a decision and not an omission — and
        // SupportsLiveUpdates is false to match, so nothing waits on a stream that
        // will never emit.

        public override Task EnsureConnectedAsync() => Task.CompletedTask;

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) =>
            Task.CompletedTask;

        public override Task DisconnectAsync() => Task.CompletedTask;
    }
}
