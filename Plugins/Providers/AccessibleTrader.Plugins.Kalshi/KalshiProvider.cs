using System.Globalization;
using System.Net;
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

namespace AccessibleTrader.Plugins.Kalshi
{
    /// <summary>
    /// Kalshi perpetual futures ("margin" markets): crypto, US stocks, gold, silver, oil, currencies
    /// and indices, settled in US dollars (Kalshi's API docs, read 2026-11-14, and the live and demo
    /// venues probed the same day). Kalshi's event contracts are a different product and are not here.
    ///
    /// <para>
    /// <b>Prices are per contract.</b> A contract is a slice of the underlying — KXBTCPERP is
    /// 0.0001 BTC, so it trades near 6.68 when bitcoin is near 66,800 — and the chart, the order
    /// ticket and the positions all show that per-contract price, as Kalshi does. Sizes are in
    /// contracts; most markets take whole contracts only, a few take hundredths.
    /// </para>
    ///
    /// <para>
    /// <b>What Kalshi does not have, and what this plugin does instead.</b> There is no market
    /// order: one goes as an immediate-or-cancel limit 5% past the best opposite price in the book,
    /// as Hyperliquid's does, and is refused in words when nobody is on that side. There is no
    /// leverage setting: the exposure is the size ordered, margined from the account, so an order
    /// asking for more than 1 times is refused rather than sent as if it had been set. There are no
    /// stop or take-profit orders through this plugin (Kalshi's exit triggers are not built), so
    /// those are refused before anything is sent, and an entry carrying one is refused by the order
    /// service because <see cref="ITradingProvider.AttachesProtectiveLegs"/> is false.
    /// </para>
    ///
    /// <para>
    /// <b>Practice.</b> Kalshi runs a demo venue with play money and its own account and keys
    /// (demo.kalshi.co). A credential that is not marked Live signs there, and its charts and
    /// market list come from there too, because demo tickers differ (KXBTCPERP1). With no
    /// credential at all, charts come from the live venue, whose market data needs no key.
    /// </para>
    /// </summary>
    public class KalshiProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider
    {
        internal const string LiveBase = "https://external-api.kalshi.com/trade-api/v2";
        internal const string DemoBase = "https://external-api.demo.kalshi.co/trade-api/v2";

        // The signature covers the path from here on, never the host.
        private const string SignRoot = "/trade-api/v2";

        /// <summary>How far past the best opposite price a market order's limit goes. Kalshi's
        /// immediate orders on stocks may go 12% past fair value, so 5% stays inside every band.</summary>
        internal const double MarketSlippage = 0.05;

        // Kalshi's prices for "nobody is selling": the largest 64-bit number in hundredths of a cent.
        private const double NoPrice = 1e12;

        private readonly HttpClient _http;
        private readonly Subject<OrderUpdate> _orderUpdates = new();

        // Basic tier: 400 read and 200 write tokens a second, 10 tokens a request (docs, 2026-11-14).
        // Half of each.
        private readonly RateLimiter _readLimiter = new(20, TimeSpan.FromSeconds(1));
        private readonly RateLimiter _writeLimiter = new(10, TimeSpan.FromSeconds(1));

        // Market rules from /margin/markets, per venue (demo tickers differ), kept six hours.
        private readonly Dictionary<string, (Dictionary<string, MarketRules> Rules, DateTime At)> _rules = new();
        private readonly object _rulesLock = new();
        private static readonly TimeSpan RulesTtl = TimeSpan.FromHours(6);

        private string _apiKey = "";
        private string _apiSecret = "";
        private bool _useDemo;
        private KalshiSigner? _signer;
        private string? _keyError;

        /// <summary>The clock the signature is stamped with. Kalshi refuses a stamp more than 30
        /// seconds from its own.</summary>
        internal Func<long> NowMs = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public KalshiProvider()
        {
            _http = PluginHostServices.CreateHttpClient(
                providerId:   "Kalshi",
                allowedHosts: new[] { "external-api.kalshi.com", "external-api.demo.kalshi.co" });
        }

        private bool HasKey => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_apiSecret);

        /// <summary>Where orders, balances and positions go.</summary>
        private string TradeBase => _useDemo ? DemoBase : LiveBase;

        /// <summary>Where charts and the market list come from: the venue the credential trades
        /// on, so a demo account sees the demo tickers it can order; the live venue otherwise.</summary>
        private string DataBase => HasKey && _useDemo ? DemoBase : LiveBase;

        // ── IProviderPlugin ───────────────────────────────────────────────────

        public override string Name => "Kalshi";
        public override string Description => "Kalshi perpetual futures — crypto, US stocks, metals, oil, currencies and indices in US dollars";

        /// <summary>
        /// No <c>Leverage</c>: Kalshi has no leverage setting, on the order or anywhere else. No
        /// <c>Brackets</c>: nothing can be attached to an order.
        /// </summary>
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.FuturesTrading | ProviderCapabilities.Shorting |
            ProviderCapabilities.ReduceOnly | ProviderCapabilities.PostOnly |
            ProviderCapabilities.TimeInForce;

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider))    return this as T;
            if (typeof(T) == typeof(IOrderBookProvider))  return this as T;
            return null;
        }

        public override List<MarketType> SupportedMarkets => new()
            { MarketType.Crypto, MarketType.Stock, MarketType.Commodity, MarketType.Forex, MarketType.Index };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;          // market data is public
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => false;      // REST polling; the websocket is a later slice
        public override ProviderEnvironment Environment => _useDemo ? ProviderEnvironment.Paper : ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 5000;

        /// <summary>Kalshi's candles come in one minute, one hour and one day. A day ends at
        /// midnight New York time (04:00 or 05:00 UTC), not at midnight UTC.</summary>
        public override List<string> NativelySupportedTimeframes => new() { "1m", "1h", "1d" };

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var k)) _apiKey = k.Trim();
            if (config.TryGetValue("ApiSecret", out var s)) _apiSecret = s;
            // Anything not explicitly Live is practice, and Kalshi has a practice venue.
            _useDemo = !ProviderConfigKeys.IsLive(config);
            _signer = null;
            _keyError = null;
            if (!string.IsNullOrEmpty(_apiSecret))
            {
                try { _signer = KalshiSigner.Parse(_apiSecret); }
                catch (FormatException ex) { _keyError = ex.Message; }
            }
        }

        /// <summary>
        /// Asks Kalshi whether this account may trade perpetuals at all. Kalshi is opening them to
        /// its members a few at a time, so a key that works can still belong to an account that
        /// cannot trade them yet; that is said in words rather than left to the first order.
        /// </summary>
        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) return (true, "No API key provided (public market data only)");
            if (_keyError != null) return (false, _keyError);
            if (_signer == null) return (false, "Kalshi needs the private key as well as the Key ID");
            string venue = _useDemo ? "Kalshi's demo venue" : "Kalshi";
            try
            {
                var json = await GetPrivateAsync("/margin/enabled").ConfigureAwait(false);
                return json["enabled"]?.Value<bool>() == true
                    ? (true, $"Key accepted by {venue}; perpetual futures are enabled on this account")
                    : (false, $"{venue} accepted the key, but perpetual futures are not enabled on this account yet. "
                            + "Kalshi is opening them to members a few at a time; the demo venue at demo.kalshi.co has them now.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // ── Market data ───────────────────────────────────────────────────────

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Futures" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        /// <summary>Kalshi's asset classes, by the terminal's market type.</summary>
        internal static string[] AssetClasses(MarketType market) => market switch
        {
            MarketType.Crypto    => new[] { "Crypto" },
            MarketType.Stock     => new[] { "Equities" },
            MarketType.Commodity => new[] { "Metals", "Energy" },
            MarketType.Forex     => new[] { "FX" },
            MarketType.Index     => new[] { "Indices" },
            _                    => Array.Empty<string>(),
        };

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Futures")
        {
            var classes = AssetClasses(market);
            try
            {
                return (await GetMarketsAsync(DataBase).ConfigureAwait(false))
                    .Where(m => string.Equals(m["status"]?.ToString(), "active", StringComparison.OrdinalIgnoreCase))
                    .Where(m => classes.Contains(m["asset_class"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase))
                    .Select(m => m["ticker"]?.ToString())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(t => t!)
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex)
            {
                SurfaceError($"Kalshi markets failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return new List<string>();
            }
        }

        internal static int PeriodMinutes(string timeframe) => timeframe.ToLowerInvariant() switch
        {
            "1m" => 1,
            "1d" => 1440,
            _    => 60,
        };

        /// <summary>The most candles Kalshi gives in one answer; asking for more is refused.</summary>
        internal const int MaxCandlesPerCall = 5000;
        private const int MaxCandleCalls = 20;

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(
            MarketDataRequest request)
        {
            var bars = new List<Ohlcv>();
            var vols = new List<(long, double)>();
            try
            {
                int minutes = PeriodMinutes(request.Timeframe);
                long period = minutes * 60L;
                // Kalshi's candle times are SECONDS (milliseconds are refused), and each candle is
                // stamped with the time it ENDS.
                long end = request.Until is long u ? u / 1000 : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long start = request.Since is long s ? s / 1000 : end - Math.Max(1, request.Limit) * period;
                string ticker = Uri.EscapeDataString(request.Symbol.ToUpperInvariant());

                var seen = new HashSet<long>();
                for (int call = 0; call < MaxCandleCalls && start < end; call++)
                {
                    long chunkEnd = Math.Min(end, start + MaxCandlesPerCall * period);
                    var json = await GetPublicJsonAsync(
                        $"{DataBase}/margin/markets/{ticker}/candlesticks?start_ts={start}&end_ts={chunkEnd}&period_interval={minutes}")
                        .ConfigureAwait(false);
                    foreach (var c in json["candlesticks"] as JArray ?? new JArray())
                    {
                        var p = c["price"];
                        // No trade in the period: Kalshi sends the candle with no prices.
                        if (p == null || p.Type == JTokenType.Null || IsNull(p["close"]) || IsNull(p["open"])) continue;
                        long openTs = (c["end_period_ts"]?.Value<long>() ?? 0) - period;
                        if (!seen.Add(openTs)) continue;
                        double v = D(c["volume"]);
                        bars.Add(new Ohlcv(DateTimeOffset.FromUnixTimeSeconds(openTs).UtcDateTime,
                            D(p["open"]), D(p["high"]), D(p["low"]), D(p["close"]), v));
                        vols.Add((openTs, v));
                    }
                    start = chunkEnd;
                }

                var order = Enumerable.Range(0, bars.Count).OrderBy(i => bars[i].Date).ToList();
                bars = order.Select(i => bars[i]).ToList();
                vols = order.Select(i => vols[i]).ToList();
                (bars, vols) = WindowedBars.Apply(request, bars, vols, "Kalshi", request.Symbol, SurfaceWindowMiss);
            }
            catch (Exception ex)
            {
                SurfaceError($"Kalshi candles failed for {request.Symbol}: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                if (TransportFailure.IsTransient(ex)) throw;
            }
            return (bars, vols);
        }

        private static bool IsNull(JToken? t) => t == null || t.Type == JTokenType.Null;

        private void SurfaceWindowMiss(string message) =>
            SurfaceError(message, ErrorSeverity.Medium, ErrorCategory.Provider);

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(
            string symbol, int limit = 10)
        {
            try { return await ReadBookAsync(DataBase, symbol, limit).ConfigureAwait(false); }
            catch (Exception ex)
            {
                SurfaceError($"Kalshi order book failed for {symbol}: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        /// <summary>
        /// Kalshi's docs say best first; the live venue sends best LAST (asks highest first, bids
        /// lowest first — seen 2026-11-14). Both sides are sorted here so that neither order can
        /// put a far price at the top of the book.
        /// </summary>
        private async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> ReadBookAsync(string baseUrl, string symbol, int limit)
        {
            var json = await GetPublicJsonAsync(
                $"{baseUrl}/margin/markets/{Uri.EscapeDataString(symbol.ToUpperInvariant())}/orderbook?depth={Math.Max(1, limit)}")
                .ConfigureAwait(false);
            var book = json["orderbook"];
            return (Side(book?["bids"] as JArray, limit, descending: true),
                    Side(book?["asks"] as JArray, limit, descending: false));
        }

        internal static List<OrderBookEntry> Side(JArray? rows, int limit, bool descending)
        {
            var levels = (rows ?? new JArray())
                .Select(r => new OrderBookEntry(D(r[0]), D(r[1])))
                .Where(e => e.Price > 0 && e.Price < NoPrice && e.Quantity > 0);
            return (descending ? levels.OrderByDescending(e => e.Price) : levels.OrderBy(e => e.Price))
                .Take(limit).ToList();
        }

        // ── IOrderBookProvider ────────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth).ConfigureAwait(false);
            // Sequence 0: the REST snapshot carries none.
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) =>
            Observable.Empty<OrderBookUpdate>();

        // ── ITradingProvider ──────────────────────────────────────────────────

        public bool IsConnected => HasKey;

        /// <summary>One: Kalshi has no leverage to set. See <see cref="SetLeverageAsync"/>.</summary>
        public override double MaxLeverage => 1.0;
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdates.AsObservable();

        /// <summary>No order push channel in this plugin (Kalshi's websocket is not built): the
        /// order service polls.</summary>
        public bool ProvidesOrderStream => false;

        /// <summary>Kalshi's demo venue, demo.kalshi.co: play money, its own account and keys.</summary>
        public bool HasPracticeEnvironment => true;

        public bool SupportsOrderEventStreaming => false;

        /// <summary>Nothing can be attached to a Kalshi order.</summary>
        public bool AttachesProtectiveLegs(TradeSignal signal) => false;

        /// <summary>
        /// The account in dollars. "Free" is what Kalshi says is available for new orders;
        /// "Locked" is the rest of the account's equity — margin held by positions and resting
        /// orders, and any unrealised loss or gain.
        /// </summary>
        public async Task<List<Balance>> GetBalancesAsync()
        {
            var json = await GetPrivateAsync("/margin/balance?compute_available_balance=true").ConfigureAwait(false);
            return new List<Balance> { ReadBalance(json) };
        }

        internal static Balance ReadBalance(JObject json)
        {
            double available = 0, equity = 0;
            foreach (var s in json["subaccount_balances"] as JArray ?? new JArray())
            {
                available += D(s["available_balance"]);
                equity += D(s["account_equity"]);
            }
            return new Balance("USD", available, Math.Max(0, equity - available));
        }

        /// <summary>
        /// Positions from <c>/margin/positions</c>, with the mark price and the liquidation price
        /// from <c>/margin/risk</c>. Either read failing fails the whole read: positions shown
        /// without their liquidation price would look like positions that have none.
        /// </summary>
        public async Task<List<Position>> GetPositionsAsync()
        {
            var json = await GetPrivateAsync("/margin/positions").ConfigureAwait(false);
            var risk = await GetPrivateAsync("/margin/risk").ConfigureAwait(false);
            return ReadPositions(json, risk);
        }

        /// <summary>
        /// Leverage is the position's value over the margin it holds, which is how Kalshi works out
        /// its own return on equity; Kalshi's <c>position_leverage</c> is over the MAINTENANCE margin
        /// and would show a far larger number than the account is carrying.
        /// </summary>
        internal static List<Position> ReadPositions(JObject positions, JObject? risk)
        {
            var riskByTicker = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in risk?["positions"] as JArray ?? new JArray())
                if (r["market_ticker"]?.ToString() is { Length: > 0 } t) riskByTicker.TryAdd(t, r);

            var list = new List<Position>();
            foreach (var p in positions["positions"] as JArray ?? new JArray())
            {
                double qty = D(p["position"]);
                if (qty == 0) continue;
                string ticker = p["market_ticker"]?.ToString() ?? "";
                double entry = D(p["entry_price"]);
                riskByTicker.TryGetValue(ticker, out var r);
                double mark = D(r?["mark_price"]);
                double margin = D(p["margin_used"]);
                double leverage = margin > 0 && entry > 0 ? Math.Abs(qty) * entry / margin : 1.0;
                string mode = p["subaccount_type"]?.ToString() ?? "";
                list.Add(new Position(
                    ticker, qty, entry, qty * (mark > 0 ? mark : entry), D(p["unrealized_pnl"]),
                    Leverage: leverage,
                    LiquidationPrice: D(r?["estimated_liquidation_price"]),
                    MarginMode: mode switch
                    {
                        "cross"                          => MarginMode.Cross,
                        "isolated" or "managed_isolated" => MarginMode.Isolated,
                        _                                => MarginMode.None,
                    }));
            }
            return list;
        }

        /// <summary>
        /// Orders with something left to fill. Kalshi's margin orders carry no status word, and its
        /// docs do not list the values its status filter takes, so every order is read and those
        /// with nothing remaining are left out. Not yet seen against an account with orders.
        /// </summary>
        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            var list = new List<OpenOrder>();
            string? cursor = null;
            for (int page = 0; page < 5; page++)
            {
                string q = "/margin/orders?limit=1000"
                         + (symbol != null ? "&ticker=" + Uri.EscapeDataString(symbol.ToUpperInvariant()) : "")
                         + (cursor != null ? "&cursor=" + Uri.EscapeDataString(cursor) : "");
                var json = await GetPrivateAsync(q).ConfigureAwait(false);
                list.AddRange(ReadOpenOrders(json));
                string? next = json["cursor"]?.ToString();
                if (string.IsNullOrEmpty(next) || next == cursor) break;
                cursor = next;
            }
            return list;
        }

        internal static IEnumerable<OpenOrder> ReadOpenOrders(JObject json) =>
            (json["orders"] as JArray ?? new JArray())
                .Where(o => D(o["remaining_count"]) > 0)
                .Select(o => new OpenOrder(
                    o["order_id"]?.ToString() ?? "",
                    o["ticker"]?.ToString() ?? "",
                    IsAsk(o["side"]) ? OrderSide.Sell : OrderSide.Buy,
                    OrderType.Limit,
                    D(o["remaining_count"]), D(o["price"]),
                    "resting"));

        private static bool IsAsk(JToken? side) =>
            string.Equals(side?.ToString(), "ask", StringComparison.OrdinalIgnoreCase);

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (string.IsNullOrEmpty(_apiKey)) return "ORDER_FAILED:no Kalshi API key configured";
            if (_keyError != null) return $"ORDER_FAILED:{_keyError}";
            if (_signer == null) return "ORDER_FAILED:Kalshi needs the private key as well as the Key ID";

            // Everything Kalshi cannot do is refused here, before a single request.
            if (signal.Type is not (OrderType.Market or OrderType.Limit))
                return "ORDER_FAILED:Kalshi takes market and limit orders only through the terminal; "
                     + "stop and take-profit orders are not available on Kalshi here";
            if (signal is { Leverage: > 1 })
                return "ORDER_FAILED:Kalshi has no leverage setting. The size you order is the exposure, "
                     + "held on margin from the account; set leverage to 1 and choose the size";
            string? tif = signal.TimeInForce?.Trim().ToUpperInvariant();
            if (tif is "DAY" or "GTD")
                return "ORDER_FAILED:Kalshi takes good-till-cancelled, immediate-or-cancel and fill-or-kill orders only";
            bool immediate = signal.Type == OrderType.Market || tif is "IOC" or "FOK";
            if (signal.ReduceOnly && !immediate)
                return "ORDER_FAILED:Kalshi takes a reduce-only order only if it fills at once; "
                     + "send it as a market order, or a limit marked immediate-or-cancel or fill-or-kill";
            if (signal.PostOnly && immediate)
                return "ORDER_FAILED:A post-only order rests in the book, so it cannot also be a market, "
                     + "immediate-or-cancel or fill-or-kill order";
            if (signal.Type == OrderType.Limit && signal.Price is not > 0)
                return "ORDER_FAILED:A Kalshi limit order needs a price";

            string ticker = signal.Symbol.ToUpperInvariant();
            var market = await GetMarketRulesAsync(ticker).ConfigureAwait(false);
            if (market != null && SizeRefusal(market, signal.Quantity) is string refusal)
                return $"ORDER_FAILED:{refusal}";
            var rules = market?.Rules;

            double price;
            if (signal.Type == OrderType.Market)
            {
                (List<OrderBookEntry> bids, List<OrderBookEntry> asks) book;
                try { book = await ReadBookAsync(TradeBase, ticker, 1).ConfigureAwait(false); }
                catch (Exception ex) { return $"ORDER_FAILED:Kalshi's order book for {ticker} could not be read, so no price could be set: {ex.Message}"; }
                bool buy = signal.Side == OrderSide.Buy;
                // A book level is a value, so an empty side has no "null" to find: count it.
                var side = buy ? book.asks : book.bids;
                if (side.Count == 0)
                    return $"ORDER_FAILED:Nobody is {(buy ? "selling" : "buying")} {ticker} on Kalshi right now, "
                         + "so a market order has nothing to fill against";
                price = side[0].Price * (buy ? 1 + MarketSlippage : 1 - MarketSlippage);
            }
            else price = signal.Price!.Value;

            var body = new JObject
            {
                ["ticker"] = ticker,
                ["client_order_id"] = string.IsNullOrEmpty(signal.ClientOid) ? Guid.NewGuid().ToString() : signal.ClientOid,
                ["side"] = signal.Side == OrderSide.Buy ? "bid" : "ask",
                ["count"] = Count(rules?.FloorQuantity(signal.Quantity) ?? (decimal)signal.Quantity),
                ["price"] = Dollars(rules?.RoundPrice(price) ?? (decimal)price),
                ["time_in_force"] = signal.Type == OrderType.Market || tif == "IOC" ? "immediate_or_cancel"
                                  : tif == "FOK" ? "fill_or_kill" : "good_till_canceled",
                ["self_trade_prevention_type"] = "taker_at_cross",
            };
            if (signal.PostOnly) body["post_only"] = true;
            if (signal.ReduceOnly) body["reduce_only"] = true;

            try
            {
                var (status, json, raw) = await SendPrivateAsync(HttpMethod.Post, "/margin/orders",
                    body.ToString(Formatting.None)).ConfigureAwait(false);
                if ((int)status is < 200 or > 299) return $"ORDER_FAILED:{Words(json, raw, status)}";

                var o = json?["order"] as JObject ?? json;
                string? id = o?["order_id"]?.ToString();
                if (string.IsNullOrEmpty(id)) return "ORDER_FAILED:Kalshi took the order but returned no id";
                // An immediate order that found nothing to fill is gone, not working.
                if (immediate && o?["fill_count"] is JToken f && f.Type != JTokenType.Null && D(f) == 0)
                    return $"ORDER_FAILED:Kalshi filled none of it at {Dollars(rules?.RoundPrice(price) ?? (decimal)price)} or better, "
                         + "and an immediate order does not wait, so nothing is open";
                return id;
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            try
            {
                var (status, json, raw) = await SendPrivateAsync(HttpMethod.Delete,
                    $"/margin/orders/{Uri.EscapeDataString(orderId)}", null).ConfigureAwait(false);
                if ((int)status is >= 200 and <= 299) return true;
                SurfaceError($"Kalshi cancel failed: {Words(json, raw, status)}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return false;
            }
            catch (Exception ex)
            {
                SurfaceError($"Kalshi cancel failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return false;
            }
        }

        /// <summary>
        /// Kalshi has no leverage to set — not per order, not per market, not per account. The
        /// answer is 1 so a caller that asked for more knows it was not set; an order asking for
        /// more is refused in <see cref="PlaceOrderAsync"/>.
        /// </summary>
        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            string q = $"/margin/fills?limit={Math.Clamp(limit, 1, 1000)}"
                     + (symbol != null ? "&ticker=" + Uri.EscapeDataString(symbol.ToUpperInvariant()) : "");
            var json = await GetPrivateAsync(q).ConfigureAwait(false);
            return ReadFills(json).Take(limit).ToList();
        }

        internal static IEnumerable<TradeFill> ReadFills(JObject json) =>
            (json["fills"] as JArray ?? new JArray()).Select(f => new TradeFill(
                f["fill_id"]?.ToString() ?? "",
                f["ticker"]?.ToString() ?? "",
                IsAsk(f["side"]) ? OrderSide.Sell : OrderSide.Buy,
                D(f["count"]), D(f["price"]),
                DateTime.TryParse(f["created_time"]?.ToString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.UtcNow,
                Fee: D(f["fees"]),
                OrderId: f["order_id"]?.ToString(),
                RealizedPnL: D(f["realized_pnl"])));

        // ── Market rules ──────────────────────────────────────────────────────

        /// <summary>One market's order rules, and what one contract is in words ("0.0001 BTC").</summary>
        internal sealed record MarketRules(SymbolOrderRules Rules, string? ContractIs);

        /// <summary>
        /// A size Kalshi would refuse, in words, or null. A size between steps is REFUSED rather than
        /// cut down: 9.7 contracts on a whole-contract market is a typing slip as often as not, and
        /// sending 9 would be an order nobody asked for.
        /// </summary>
        internal static string? SizeRefusal(MarketRules market, double quantity)
        {
            var r = market.Rules;
            string note = market.ContractIs != null ? $" (one contract is {market.ContractIs})" : "";
            if (r.Refusal(quantity, null) is string refusal)
                return refusal + (r.NotTrading == null ? note : "");
            decimal q = (decimal)quantity;
            if (r.QuantityStep > 0 && r.FloorQuantity(quantity) != q)
                return r.QuantityStep == 1
                    ? $"Kalshi trades {r.Symbol} in whole contracts; this order is {SymbolOrderRules.Plain(q)}{note}"
                    : $"Kalshi trades {r.Symbol} in steps of {SymbolOrderRules.Plain(r.QuantityStep)} contracts; "
                    + $"this order is {SymbolOrderRules.Plain(q)}{note}";
            return null;
        }

        internal async Task<MarketRules?> GetMarketRulesAsync(string ticker)
        {
            string host = TradeBase;
            Dictionary<string, MarketRules>? all = null;
            lock (_rulesLock)
                if (_rules.TryGetValue(host, out var cached) && DateTime.UtcNow - cached.At < RulesTtl) all = cached.Rules;
            if (all == null)
            {
                try
                {
                    all = new Dictionary<string, MarketRules>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in await GetMarketsAsync(host).ConfigureAwait(false))
                        if (ReadMarketRules(m) is { } r) all[r.Rules.Symbol] = r;
                    lock (_rulesLock) _rules[host] = (all, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    SurfaceError($"Kalshi order rules could not be read: {ex.Message}", ErrorSeverity.Low, ErrorCategory.Provider);
                    return null;
                }
            }
            return all.TryGetValue(ticker, out var rules) ? rules : null;
        }

        /// <summary>
        /// One <c>/margin/markets</c> entry (live, 2026-11-14): <c>minimum_quantity</c> "1.00",
        /// <c>fractional_trading_enabled</c> (hundredths of a contract when true), <c>tick_size</c>
        /// "0.0001", <c>status</c> active / inactive / closed, and a <c>title</c> that names one
        /// contract when it starts with a number ("0.0001 BTC", "100 DOGE").
        /// </summary>
        internal static MarketRules? ReadMarketRules(JToken m)
        {
            string? ticker = m["ticker"]?.ToString();
            if (string.IsNullOrEmpty(ticker)) return null;
            string status = m["status"]?.ToString() ?? "";
            string title = m["title"]?.ToString() ?? "";
            var rules = new SymbolOrderRules
            {
                Venue        = "Kalshi",
                Symbol       = ticker.ToUpperInvariant(),
                QuantityUnit = "contracts",
                QuoteAsset   = "USD",
                MinQuantity  = SymbolOrderRules.ParseStep(m["minimum_quantity"]?.ToString()),
                QuantityStep = m["fractional_trading_enabled"]?.Value<bool>() == true ? 0.01m : 1m,
                PriceTick    = SymbolOrderRules.ParseStep(m["tick_size"]?.ToString()),
                NotTrading   = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) ? null
                             : $"Kalshi lists {ticker.ToUpperInvariant()} as {(status.Length > 0 ? status : "not trading")}; it takes no orders",
            };
            return new MarketRules(rules, title.Length > 0 && char.IsDigit(title[0]) ? title : null);
        }

        private async Task<List<JToken>> GetMarketsAsync(string baseUrl)
        {
            var list = new List<JToken>();
            string? cursor = null;
            for (int page = 0; page < 10; page++)
            {
                var json = await GetPublicJsonAsync($"{baseUrl}/margin/markets"
                    + (cursor != null ? "?cursor=" + Uri.EscapeDataString(cursor) : "")).ConfigureAwait(false);
                list.AddRange(json["markets"] as JArray ?? new JArray());
                string? next = json["cursor"]?.ToString();
                if (string.IsNullOrEmpty(next) || next == cursor) break;
                cursor = next;
            }
            return list;
        }

        // ── Numbers as Kalshi writes them ─────────────────────────────────────

        /// <summary>Kalshi's prices are dollar strings with two to four decimals: "101.50", never "101.5".</summary>
        internal static string Dollars(decimal price) => price.ToString("0.00##", CultureInfo.InvariantCulture);

        /// <summary>Contracts with up to two decimals.</summary>
        internal static string Count(decimal count) => count.ToString("0.##", CultureInfo.InvariantCulture);

        // ── Transport ─────────────────────────────────────────────────────────

        private async Task<JObject> GetPublicJsonAsync(string url)
        {
            await _readLimiter.WaitAsync().ConfigureAwait(false);
            using var res = await _http.GetAsync(url).ConfigureAwait(false);
            string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Kalshi answered HTTP {(int)res.StatusCode}: {Words(TryParse(body), body, res.StatusCode)}",
                    null, res.StatusCode);
            return TryParse(body) ?? throw new HttpRequestException($"Kalshi answered with a non-JSON body (HTTP {(int)res.StatusCode}).");
        }

        /// <summary>A signed GET that must succeed. Only the path travels in a message, never
        /// the URL.</summary>
        private async Task<JObject> GetPrivateAsync(string pathAndQuery)
        {
            var (status, json, raw) = await SendPrivateAsync(HttpMethod.Get, pathAndQuery, null).ConfigureAwait(false);
            string path = pathAndQuery.Split('?')[0];
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException($"Kalshi refused {path}: {Words(json, raw, status)}");
            if ((int)status is < 200 or > 299)
                throw new HttpRequestException($"Kalshi refused {path}: {Words(json, raw, status)}", null, status);
            return json ?? throw new HttpRequestException($"Kalshi answered {path} with a non-JSON body (HTTP {(int)status}).");
        }

        private async Task<(HttpStatusCode Status, JObject? Json, string Raw)> SendPrivateAsync(
            HttpMethod method, string pathAndQuery, string? jsonBody)
        {
            if (_signer == null) throw new UnauthorizedAccessException(_keyError ?? "No Kalshi private key configured");
            await (method == HttpMethod.Get ? _readLimiter : _writeLimiter).WaitAsync().ConfigureAwait(false);
            // Stamped after the wait, so a queued request does not age past Kalshi's 30 seconds.
            string ts = NowMs().ToString(CultureInfo.InvariantCulture);
            using var req = new HttpRequestMessage(method, TradeBase + pathAndQuery);
            req.Headers.Add("KALSHI-ACCESS-KEY", _apiKey);
            req.Headers.Add("KALSHI-ACCESS-TIMESTAMP", ts);
            req.Headers.Add("KALSHI-ACCESS-SIGNATURE", _signer.Sign(KalshiSigner.Message(ts, method.Method, SignRoot + pathAndQuery)));
            if (jsonBody != null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            string raw = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (res.StatusCode, TryParse(raw), raw);
        }

        private static JObject? TryParse(string body)
        {
            try { return JToken.Parse(body) as JObject; }
            catch (JsonReaderException) { return null; }
        }

        /// <summary>
        /// Kalshi's refusal, <c>{"error":{"code","message","details"}}</c>, in words a person can
        /// act on. Codes are from Kalshi's docs (2026-11-14); one not listed keeps Kalshi's message.
        /// </summary>
        internal static string Words(JObject? json, string raw, HttpStatusCode status)
        {
            var err = json?["error"];
            string code = (err is JObject ? err["code"]?.ToString() : null) ?? "";
            string message = (err is JObject ? err["message"]?.ToString() : err?.ToString()) ?? "";
            return code.ToLowerInvariant() switch
            {
                "token_authentication_failure" or "authentication_error" =>
                    "Kalshi did not accept the key. Check the Key ID and the private key, that they were made on "
                  + "the venue this profile uses (demo keys are separate), and that this computer's clock is right",
                "stock_perps_application_required" =>
                    "Kalshi needs your stocks application approved before it takes orders on stock perpetuals; apply on kalshi.com",
                "stock_perps_insider_blocked" =>
                    "Kalshi does not let this account trade this stock (an insider restriction on the account)",
                "invalid_price" =>
                    $"Kalshi refused the price: it must be a whole number of ticks and inside the market's price band ({message})",
                "insufficient_balance" =>
                    "Kalshi says the account does not have enough available balance for this order",
                "notional_risk_limit_exceeded" =>
                    "The order would take the account past Kalshi's limit on position size",
                "market_inactive" =>
                    "Kalshi is not taking orders on this market right now",
                "trading_is_paused" =>
                    "Kalshi has paused trading",
                "trading_blocked" =>
                    "Kalshi has blocked trading on this account; contact Kalshi",
                "fill_or_kill_insufficient_resting_volume" =>
                    "There was not enough in the book to fill all of it at once, so Kalshi filled none of it (fill or kill)",
                "order_already_exists" =>
                    "Kalshi already has an order with this client id; nothing new was sent",
                "invalid_order_size" =>
                    $"Kalshi refused the size ({message})",
                _ when message.Length > 0 => $"Kalshi refused: {message}" + (code.Length > 0 ? $" ({code})" : ""),
                _ => $"Kalshi refused the request (HTTP {(int)status})",
            };
        }

        private static double D(JToken? t) =>
            double.TryParse(t?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

        // ── Connection lifecycle ──────────────────────────────────────────────
        //
        // REST only: every read is a fresh request, so there is no session to keep.

        public override Task EnsureConnectedAsync() => Task.CompletedTask;

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) =>
            Task.CompletedTask;

        public override Task DisconnectAsync() => Task.CompletedTask;
    }
}
