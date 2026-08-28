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
    public class GeminiProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider
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

        // Symbol details (price increment above all) fetched once per symbol and
        // kept: the market-order emulation must round its aggressive price to the
        // venue's quote increment or the order is rejected for precision.
        private readonly Dictionary<string, JObject> _symbolDetails = new(StringComparer.OrdinalIgnoreCase);

        public GeminiProvider()
        {
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
            if (config.TryGetValue("Environment", out var e))
                _useSandbox = string.Equals(e, "Paper", StringComparison.OrdinalIgnoreCase);
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
        }

        // Spot venue: there are no positions, only balances. An empty list is the
        // truthful answer, not a stub.
        public Task<List<Position>> GetPositionsAsync() => Task.FromResult(new List<Position>());

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            var json = await PostPrivateAsync("/v1/orders").ConfigureAwait(false);
            return (json as JArray ?? new JArray())
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
            var payload = new JObject
            {
                ["symbol"] = symbol,
                ["side"]   = signal.Side == OrderSide.Buy ? "buy" : "sell",
                ["amount"] = Fmt(signal.Quantity),
            };
            if (!string.IsNullOrEmpty(signal.ClientOid)) payload["client_order_id"] = signal.ClientOid;

            switch (signal.Type)
            {
                case OrderType.Limit:
                    if (signal.Price is not > 0)
                        return "ORDER_FAILED:a limit order needs a limit price";
                    payload["type"]  = "exchange limit";
                    payload["price"] = Fmt(signal.Price.Value);
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
                    payload["price"]   = Fmt(aggressive.Value);
                    payload["options"] = new JArray("immediate-or-cancel");
                    break;
                }

                case OrderType.StopLimit:
                {
                    double? trigger = signal.TriggerPrice is > 0 ? signal.TriggerPrice : signal.StopLoss;
                    if (trigger is not > 0 || signal.Price is not > 0)
                        return "ORDER_FAILED:a stop-limit order needs both a trigger price and a limit price";
                    payload["type"]       = "exchange stop limit";
                    payload["stop_price"] = Fmt(trigger.Value);
                    payload["price"]      = Fmt(signal.Price.Value);
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
                var json = await PostPrivateAsync("/v1/order/new", payload).ConfigureAwait(false);

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
                var json = await PostPrivateAsync("/v1/order/cancel",
                    new JObject { ["order_id"] = long.TryParse(orderId, out long id) ? id : orderId })
                    .ConfigureAwait(false);
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

            var json = await PostPrivateAsync("/v1/mytrades", payload).ConfigureAwait(false);
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
            double inc = await QuoteIncrementAsync(symbol).ConfigureAwait(false);
            return inc > 0 ? Math.Round(raw / inc) * inc : raw;
        }

        private async Task<double> QuoteIncrementAsync(string symbol)
        {
            if (!_symbolDetails.TryGetValue(symbol, out var details))
            {
                try
                {
                    details = (JObject)await GetPublicAsync(
                        $"/v1/symbols/details/{Uri.EscapeDataString(symbol)}").ConfigureAwait(false);
                    _symbolDetails[symbol] = details;
                }
                catch { return 0; }   // caller falls back to the unrounded price
            }
            return D(details["quote_increment"]);
        }

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
            var json = JToken.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            ThrowOnError(json);
            return json;
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
            var json = JToken.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            ThrowOnError(json);
            return json;
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
