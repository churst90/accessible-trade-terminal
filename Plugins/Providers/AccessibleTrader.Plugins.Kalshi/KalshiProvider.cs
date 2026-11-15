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
        /// No <c>Leverage</c>: Kalshi has no leverage setting, on the order or anywhere else.
        /// <c>Brackets</c>: a stop loss and take profit on an entry are set as the position's exit
        /// trigger once the entry fills (see <see cref="PlaceOrderAsync"/>). No <c>TrailingStop</c>:
        /// Kalshi has trailing stops, and the terminal does not set them yet.
        /// </summary>
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.FuturesTrading | ProviderCapabilities.Shorting |
            ProviderCapabilities.ReduceOnly | ProviderCapabilities.PostOnly |
            ProviderCapabilities.TimeInForce | ProviderCapabilities.Brackets;

        public override bool SupportsStopLoss => true;
        public override bool SupportsTakeProfit => true;

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

        /// <summary>
        /// A stop loss and take profit on a market or limit entry are set as the position's exit
        /// trigger: after the fill for an order that fills at once, tied to the order for one that rests.
        /// </summary>
        public bool AttachesProtectiveLegs(TradeSignal signal) => signal.Type is OrderType.Market or OrderType.Limit;

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
        /// Orders with something left to fill, and each live stop-loss and take-profit leg of the
        /// positions' exit triggers as a stop or take-profit order (see <see cref="GetExitLegsAsync"/>).
        /// Kalshi's margin orders carry no status word, and its docs do not list the values its
        /// status filter takes, so every order is read and those with nothing remaining are left
        /// out. Not yet seen against an account with orders.
        /// </summary>
        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            var list = await GetRestingOrdersAsync(symbol).ConfigureAwait(false);
            list.AddRange(await GetExitLegsAsync(symbol, list).ConfigureAwait(false));
            return list;
        }

        private async Task<List<OpenOrder>> GetRestingOrdersAsync(string? symbol)
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
            if (signal.Type is OrderType.StopLimit or OrderType.TakeProfitLimit)
                return "ORDER_FAILED:A Kalshi stop or target fires its own immediate order near the level and "
                     + "takes no limit price of yours; send it as a stop-market or take-profit-market order";
            bool exitLeg = signal.Type is OrderType.StopMarket or OrderType.TakeProfitMarket;
            if (exitLeg && !signal.ReduceOnly)
                return "ORDER_FAILED:On Kalshi a stop or target closes a position you hold and cannot open one; "
                     + "mark it reduce-only to set the position's stop loss or take profit";
            if (signal is { Leverage: > 1 })
                return "ORDER_FAILED:Kalshi has no leverage setting. The size you order is the exposure, "
                     + "held on margin from the account; set leverage to 1 and choose the size";
            if (exitLeg)
            {
                string exitTicker = signal.Symbol.ToUpperInvariant();
                var exitMarket = await GetMarketRulesAsync(exitTicker).ConfigureAwait(false);
                return await PlaceExitLegAsync(signal, exitTicker, exitMarket?.Rules).ConfigureAwait(false);
            }
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

            // A stop or target on the wrong side would be refused by Kalshi only AFTER the entry
            // filled, leaving the position without it: check the sides first.
            decimal? stopLoss = signal.StopLoss is > 0 ? Round(rules, signal.StopLoss.Value) : null;
            decimal? takeProfit = signal.TakeProfit is > 0 ? Round(rules, signal.TakeProfit.Value) : null;
            double reference = signal.Type == OrderType.Market
                ? price / (signal.Side == OrderSide.Buy ? 1 + MarketSlippage : 1 - MarketSlippage)
                : price;
            if (LegRefusal(signal.Side, (decimal)reference, stopLoss, takeProfit) is string legRefusal)
                return $"ORDER_FAILED:{legRefusal}";

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
                if (stopLoss != null || takeProfit != null)
                {
                    decimal count = Dec(body["count"])!.Value;
                    decimal filled = Dec(o?["fill_count"]) ?? (immediate ? count : 0);
                    // A resting order gets a bracket tied to it, which follows its fills; one that
                    // filled in full (or at once) gets the bracket on the position it made.
                    bool anchored = !immediate && filled < count;
                    await AttachToEntryAsync(signal.Side, ticker, id, anchored, immediate ? filled : count,
                        stopLoss, takeProfit).ConfigureAwait(false);
                }
                return id;
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (orderId.StartsWith(LegIdPrefix, StringComparison.Ordinal))
                return await CancelLegAsync(orderId).ConfigureAwait(false);
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

        // ── Exit triggers: the stop loss and take profit ──────────────────────
        //
        // Kalshi takes no stop or target ORDER. It keeps them as an exit trigger on the position:
        // a bracket of a stop-loss price and a take-profit price, watched against the MARK price,
        // which fires a reduce-only immediate-or-cancel limit near the level (so it can fill in
        // part, or not at all). The terminal's stop and target orders map onto those legs:
        //   • each live leg is listed as an open stop or take-profit order, whose id names the
        //     route, the market, the bracket, the leg and the leg's price;
        //   • a reduce-only stop or take-profit order SETS that leg on the position's bracket,
        //     keeping the other leg, or starts a bracket when the position has none;
        //   • a cancel of a leg keeps the other leg, and of a leg that has since moved, or whose
        //     bracket is gone, is already done — so the Positions tab's "place the new one, then
        //     cancel the old one" moves the stop without ever leaving the position bare.
        // A cross-margin position takes up to 20 brackets whose sizes fit it; an isolated one
        // takes one, which closes all of it. Docs: docs.kalshi.com/margin#exit-triggers (2026-11-15).

        internal const string LegIdPrefix = "xt:";

        internal sealed record ExitTrigger(string Id, string Kind, string Status, decimal? StopLoss, decimal? TakeProfit,
            decimal Count, string? AnchorOrderId, string? ClientTriggerId, decimal? EffectiveStop);

        internal static ExitTrigger ReadTrigger(JToken t) => new(
            t["id"]?.ToString() ?? "", t["kind"]?.ToString() ?? "bracket", t["status"]?.ToString() ?? "",
            Dec(t["stop_loss_price"]), Dec(t["take_profit_price"]), Dec(t["count"]) ?? 0,
            NonEmpty(t["anchor_order_id"]), NonEmpty(t["client_trigger_id"]), Dec(t["effective_stop_price"]));

        private static string? NonEmpty(JToken? t) => IsNull(t) || t!.ToString().Length == 0 ? null : t.ToString();

        private static decimal? Dec(JToken? t) =>
            !IsNull(t) && decimal.TryParse(t!.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? v : null;

        private static decimal Round(SymbolOrderRules? rules, double price) => rules?.RoundPrice(price) ?? (decimal)price;

        private static bool IsIsolated(JToken position) =>
            position["subaccount_type"]?.ToString() is "isolated" or "managed_isolated";

        internal static string TriggerRoute(bool isolated, string ticker) =>
            $"/margin/{(isolated ? "isolated" : "cross")}/positions/{Uri.EscapeDataString(ticker)}/exit_trigger";

        /// <summary>A cross bracket is changed by its id; an isolated position has one, changed by setting it again.</summary>
        private static string BracketRoute(bool isolated, string ticker, string triggerId) =>
            isolated ? TriggerRoute(true, ticker) : TriggerRoute(false, ticker) + "/" + Uri.EscapeDataString(triggerId);

        internal static string LegId(bool isolated, string ticker, string triggerId, string leg, decimal price) =>
            $"{LegIdPrefix}{(isolated ? "i" : "c")}:{ticker}:{triggerId}:{leg}:{Dollars(price)}";

        internal static bool TryParseLegId(string id, out bool isolated, out string ticker, out string triggerId,
            out string leg, out decimal price)
        {
            isolated = false; ticker = triggerId = leg = ""; price = 0;
            if (!id.StartsWith(LegIdPrefix, StringComparison.Ordinal)) return false;
            var parts = id.Substring(LegIdPrefix.Length).Split(':');
            if (parts.Length < 5 || parts[0] is not ("c" or "i")) return false;
            isolated = parts[0] == "i";
            ticker = parts[1];
            leg = parts[^2];
            triggerId = string.Join(":", parts[2..^2]);
            return decimal.TryParse(parts[^1], NumberStyles.Number, CultureInfo.InvariantCulture, out price)
                && triggerId.Length > 0 && leg is "sl" or "tp" or "trail";
        }

        /// <summary>A bracket body. A leg left null is CLEARED by Kalshi, so a change to one leg
        /// passes the other one back.</summary>
        internal static JObject BracketBody(decimal? stopLoss, decimal? takeProfit)
        {
            var body = new JObject();
            if (stopLoss != null) body["stop_loss_price"] = Dollars(stopLoss.Value);
            if (takeProfit != null) body["take_profit_price"] = Dollars(takeProfit.Value);
            return body;
        }

        /// <summary>Stop and target on the wrong side, checked before the entry is sent: Kalshi
        /// would refuse them only after the entry had filled.</summary>
        internal static string? LegRefusal(OrderSide side, decimal reference, decimal? stopLoss, decimal? takeProfit)
        {
            bool buy = side == OrderSide.Buy;
            string at = Dollars(reference);
            if (stopLoss != null && (buy ? stopLoss >= reference : stopLoss <= reference))
                return $"The stop loss on a {(buy ? "buy" : "sell")} must be {(buy ? "below" : "above")} the price ({at}); it is {Dollars(stopLoss.Value)}";
            if (takeProfit != null && (buy ? takeProfit <= reference : takeProfit >= reference))
                return $"The take profit on a {(buy ? "buy" : "sell")} must be {(buy ? "above" : "below")} the price ({at}); it is {Dollars(takeProfit.Value)}";
            return null;
        }

        private async Task<JToken?> GetPositionAsync(string ticker)
        {
            var json = await GetPrivateAsync("/margin/positions").ConfigureAwait(false);
            return (json["positions"] as JArray ?? new JArray()).FirstOrDefault(p =>
                string.Equals(p["market_ticker"]?.ToString(), ticker, StringComparison.OrdinalIgnoreCase) && D(p["position"]) != 0);
        }

        /// <summary>The live triggers on one market: active, or waiting for their order to fill.</summary>
        private async Task<List<ExitTrigger>> GetTriggersAsync(bool isolated, string ticker)
        {
            var json = await GetPrivateAsync(TriggerRoute(isolated, ticker)).ConfigureAwait(false);
            return (json["exit_triggers"] as JArray ?? new JArray()).Select(ReadTrigger)
                .Where(t => t.Status is "active" or "pending_on_entry").ToList();
        }

        /// <summary>One set or change. Retry is true only where a second try could go differently.</summary>
        private async Task<(ExitTrigger? Trigger, string? Error, bool Retry)> SendTriggerAsync(string path, JObject body)
        {
            try
            {
                var (status, json, raw) = await SendPrivateAsync(HttpMethod.Put, path, body.ToString(Formatting.None)).ConfigureAwait(false);
                if ((int)status is < 200 or > 299)
                    return (null, Words(json, raw, status), (int)status == 429 || (int)status >= 500);
                var t = json?["exit_trigger"] as JObject ?? json;
                return string.IsNullOrEmpty(t?["id"]?.ToString())
                    ? (null, "Kalshi answered without the trigger it set", false)
                    : (ReadTrigger(t!), null, false);
            }
            catch (Exception ex) { return (null, ex.Message, true); }
        }

        /// <summary>
        /// The stop loss and take profit of an entry that went through. On a failure it is tried
        /// once more — after reading again what is live, so a first try that did land is not set
        /// twice — and then said loudly: the entry stands without them.
        /// </summary>
        private async Task AttachToEntryAsync(OrderSide side, string ticker, string orderId, bool anchored,
            decimal filled, decimal? stopLoss, decimal? takeProfit)
        {
            string clientId = ("at-" + orderId) is { Length: <= 64 } c ? c : "at-" + orderId[..61];
            string? error = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool retry;
                (error, retry) = anchored
                    ? await AnchorBracketAsync(ticker, orderId, clientId, stopLoss, takeProfit).ConfigureAwait(false)
                    : await BracketPositionAsync(side, ticker, clientId, filled, stopLoss, takeProfit).ConfigureAwait(false);
                if (error == null || !retry) break;
            }
            if (error == null) return;

            string label = stopLoss != null && takeProfit != null ? "stop loss and take profit"
                         : stopLoss != null ? "stop loss" : "take profit";
            SurfaceError(anchored
                ? $"ORDER WITHOUT ITS {label.ToUpperInvariant()}: Kalshi took the {ticker} order but not its {label} ({error}). "
                  + $"The order is resting without it, and if it fills the position has no {label}. "
                  + $"Cancel the order, or set the {label} on the Positions tab once it fills."
                : $"POSITION UNPROTECTED: Kalshi filled the {ticker} order but did not take its {label} ({error}). "
                  + $"The position is open without its {label}. Set it on the Positions tab or close the position.",
                ErrorSeverity.High, ErrorCategory.UserActionable);
        }

        /// <summary>A resting entry's bracket, tied to the order: Kalshi holds it until the order
        /// fills and then follows the fills. Cross margin only.</summary>
        private async Task<(string? Error, bool Retry)> AnchorBracketAsync(string ticker, string orderId, string clientId,
            decimal? stopLoss, decimal? takeProfit)
        {
            try
            {
                var live = await GetTriggersAsync(false, ticker).ConfigureAwait(false);
                if (live.Any(t => t.ClientTriggerId == clientId || t.AnchorOrderId == orderId)) return (null, false);
                var body = BracketBody(stopLoss, takeProfit);
                body["anchor_order_id"] = orderId;
                body["client_trigger_id"] = clientId;
                var r = await SendTriggerAsync(TriggerRoute(false, ticker), body).ConfigureAwait(false);
                return (r.Error, r.Retry);
            }
            catch (Exception ex) { return (ex.Message, true); }
        }

        /// <summary>
        /// A filled entry's bracket, on the position. A position with a bracket over all of it (an
        /// isolated position's only one, or a cross one set without a size) has that bracket
        /// changed to the entry's levels — Kalshi would not take a second over the same contracts
        /// — and that is said. Otherwise a cross position gets a bracket over what this entry filled,
        /// or over all of it when this entry is the whole position and nothing else is set.
        /// </summary>
        private async Task<(string? Error, bool Retry)> BracketPositionAsync(OrderSide side, string ticker, string clientId,
            decimal filled, decimal? stopLoss, decimal? takeProfit)
        {
            try
            {
                bool buy = side == OrderSide.Buy;
                var pos = await GetPositionAsync(ticker).ConfigureAwait(false);
                if (pos == null || D(pos["position"]) > 0 != buy)
                    return ($"after the fill there is no {(buy ? "long" : "short")} {ticker} position to set it on", false);
                bool isolated = IsIsolated(pos);
                decimal whole = (decimal)Math.Abs(D(pos["position"]));

                var live = await GetTriggersAsync(isolated, ticker).ConfigureAwait(false);
                if (live.Any(t => t.ClientTriggerId == clientId)) return (null, false);   // the first try landed
                var brackets = live.Where(t => t.Kind == "bracket").ToList();
                var overAll = brackets.FirstOrDefault(t => t.Count == 0) ?? (isolated ? brackets.FirstOrDefault() : null);

                if (overAll != null)
                {
                    decimal? sl = stopLoss ?? overAll.StopLoss, tp = takeProfit ?? overAll.TakeProfit;
                    var changed = await SendTriggerAsync(BracketRoute(isolated, ticker, overAll.Id), BracketBody(sl, tp)).ConfigureAwait(false);
                    if (changed.Error == null && (overAll.StopLoss != null && overAll.StopLoss != sl || overAll.TakeProfit != null && overAll.TakeProfit != tp))
                        SurfaceError($"Kalshi keeps one stop loss and take profit over the whole {ticker} position, so this order's "
                                   + $"moved them: stop loss {(sl is decimal s ? Dollars(s) : "none")}, take profit {(tp is decimal p ? Dollars(p) : "none")}.",
                            ErrorSeverity.Medium, ErrorCategory.Informational);
                    return (changed.Error, changed.Retry);
                }

                var body = BracketBody(stopLoss, takeProfit);
                if (!isolated && (brackets.Count > 0 || filled < whole))
                {
                    body["count"] = Count(filled);
                    body["client_trigger_id"] = clientId;
                }
                var r = await SendTriggerAsync(TriggerRoute(isolated, ticker), body).ConfigureAwait(false);
                return (r.Error, r.Retry);
            }
            catch (Exception ex) { return (ex.Message, true); }
        }

        /// <summary>
        /// A reduce-only stop or take-profit order: that leg of the position's bracket. The bracket
        /// it goes on is the one over the same number of contracts; a position with none of that
        /// size gets a new one with only this leg. The answer is the leg's id, as listed.
        /// </summary>
        private async Task<string> PlaceExitLegAsync(TradeSignal signal, string ticker, SymbolOrderRules? rules)
        {
            bool stop = signal.Type == OrderType.StopMarket;
            string name = stop ? "stop loss" : "take profit";
            double? level = signal.TriggerPrice ?? (stop ? signal.StopLoss : signal.TakeProfit);
            if (level is not > 0) return $"ORDER_FAILED:A Kalshi {name} needs a trigger price";
            decimal at = Round(rules, level.Value);
            try
            {
                var pos = await GetPositionAsync(ticker).ConfigureAwait(false);
                if (pos == null)
                    return $"ORDER_FAILED:Kalshi sets a {name} on a position you hold, and there is no {ticker} position";
                double held = D(pos["position"]);
                if (held > 0 == (signal.Side == OrderSide.Buy))
                    return $"ORDER_FAILED:The {ticker} position is {(held > 0 ? "long" : "short")}, so its {name} "
                         + $"{(held > 0 ? "sells" : "buys")}; this order {(signal.Side == OrderSide.Buy ? "buys" : "sells")}";
                bool isolated = IsIsolated(pos);
                decimal whole = (decimal)Math.Abs(held);
                decimal size = Math.Min(rules?.FloorQuantity(signal.Quantity) ?? (decimal)signal.Quantity, whole);
                if (isolated && size < whole)
                    return $"ORDER_FAILED:Kalshi's stop loss and take profit on an isolated position close all of it ({Count(whole)}); "
                         + $"this order is for {Count(size)}";

                var brackets = (await GetTriggersAsync(isolated, ticker).ConfigureAwait(false)).Where(t => t.Kind == "bracket").ToList();
                var same = brackets.Where(t => isolated || (t.Count == 0 ? whole : t.Count) == size).ToList();
                // Of two the same size, the one still missing this leg; otherwise which one is meant is not known.
                var missing = same.Where(t => (stop ? t.StopLoss : t.TakeProfit) == null).ToList();
                var onto = same.Count == 1 ? same[0] : missing.Count == 1 ? missing[0] : null;
                if (onto == null && same.Count > 1)
                    return $"ORDER_FAILED:The {ticker} position has {same.Count} stop-and-target brackets over {Count(size)} contracts, "
                         + $"so which {name} to set is not known; change it on Kalshi's site";

                (ExitTrigger? Trigger, string? Error, bool Retry) r;
                if (onto != null)
                    r = await SendTriggerAsync(BracketRoute(isolated, ticker, onto.Id),
                        BracketBody(stop ? at : onto.StopLoss, stop ? onto.TakeProfit : at)).ConfigureAwait(false);
                else
                {
                    var body = BracketBody(stop ? at : null, stop ? null : at);
                    if (!isolated && (brackets.Count > 0 || size < whole))
                    {
                        body["count"] = Count(size);
                        body["client_trigger_id"] = string.IsNullOrEmpty(signal.ClientOid) ? Guid.NewGuid().ToString("N") : signal.ClientOid;
                    }
                    r = await SendTriggerAsync(TriggerRoute(isolated, ticker), body).ConfigureAwait(false);
                }
                if (r.Error != null) return $"ORDER_FAILED:{r.Error}";
                return LegId(isolated, ticker, r.Trigger!.Id, stop ? "sl" : "tp", (stop ? r.Trigger.StopLoss : r.Trigger.TakeProfit) ?? at);
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        /// <summary>
        /// Takes one leg off its bracket and keeps the other; the bracket goes when nothing is
        /// left. A leg that is no longer live at the price its id names — the bracket fired or was
        /// cancelled, or the leg was moved, which replaced it — is already gone, and that is a yes.
        /// </summary>
        private async Task<bool> CancelLegAsync(string legId)
        {
            if (!TryParseLegId(legId, out bool isolated, out string ticker, out string triggerId, out string leg, out decimal price))
            {
                SurfaceError($"Kalshi cancel failed: '{legId}' is not a stop or target this terminal listed", ErrorSeverity.Medium, ErrorCategory.Provider);
                return false;
            }
            try
            {
                var t = (await GetTriggersAsync(isolated, ticker).ConfigureAwait(false)).FirstOrDefault(x => x.Id == triggerId);
                if (t == null) return true;

                string path;
                HttpMethod method;
                string? body = null;
                if (leg == "trail")
                {
                    // Trailing stops are not addressed by id; one per position, cancelled by kind.
                    (method, path) = (HttpMethod.Delete, TriggerRoute(isolated, ticker) + "?kind=trailing");
                }
                else
                {
                    decimal? now = leg == "sl" ? t.StopLoss : t.TakeProfit;
                    if (now == null || Dollars(now.Value) != Dollars(price)) return true;
                    decimal? keep = leg == "sl" ? t.TakeProfit : t.StopLoss;
                    if (keep != null)
                        (method, path, body) = (HttpMethod.Put, BracketRoute(isolated, ticker, triggerId),
                            BracketBody(leg == "sl" ? null : keep, leg == "sl" ? keep : null).ToString(Formatting.None));
                    else
                        (method, path) = (HttpMethod.Delete, isolated
                            ? TriggerRoute(true, ticker) + "?kind=bracket"
                            : BracketRoute(false, ticker, triggerId));
                }
                var (status, json, raw) = await SendPrivateAsync(method, path, body).ConfigureAwait(false);
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
        /// The live legs, as stop and take-profit orders, of every market with a position or a
        /// resting order (or of <paramref name="symbol"/> alone). Each leg closes the position, so
        /// its side is the position's opposite — or, for a bracket still waiting on its order, the
        /// order's opposite. A read that fails fails the whole list: a list without the stops
        /// would say the position has none.
        /// </summary>
        private async Task<List<OpenOrder>> GetExitLegsAsync(string? symbol, List<OpenOrder> resting)
        {
            var positions = await GetPrivateAsync("/margin/positions").ConfigureAwait(false);
            var held = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in positions["positions"] as JArray ?? new JArray())
                if (D(p["position"]) != 0 && p["market_ticker"]?.ToString() is { Length: > 0 } t) held.TryAdd(t, p);

            var tickers = held.Keys.Concat(resting.Select(o => o.Symbol))
                .Where(t => symbol == null || string.Equals(t, symbol, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.ToUpperInvariant()).Distinct().ToList();
            if (symbol != null && !tickers.Contains(symbol.ToUpperInvariant())) return new List<OpenOrder>();

            var legs = new List<OpenOrder>();
            foreach (string ticker in tickers)
            {
                held.TryGetValue(ticker, out var pos);
                bool isolated = pos != null && IsIsolated(pos);
                double position = pos != null ? D(pos["position"]) : 0;
                foreach (var t in await GetTriggersAsync(isolated, ticker).ConfigureAwait(false))
                    legs.AddRange(LegOrders(t, ticker, isolated, position, resting));
            }
            return legs;
        }

        internal static IEnumerable<OpenOrder> LegOrders(ExitTrigger t, string ticker, bool isolated, double position,
            IReadOnlyList<OpenOrder> resting)
        {
            var anchor = t.AnchorOrderId == null ? null : resting.FirstOrDefault(o => o.Id == t.AnchorOrderId);
            OrderSide side;
            if (position != 0) side = position > 0 ? OrderSide.Sell : OrderSide.Buy;
            else if (anchor != null) side = anchor.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            else yield break;   // nothing it could close is known
            double qty = t.Count > 0 ? (double)t.Count : position != 0 ? Math.Abs(position) : anchor!.Quantity;

            if (t.Kind == "trailing")
            {
                decimal stopAt = t.EffectiveStop ?? 0;
                yield return new OpenOrder(LegId(isolated, ticker, t.Id, "trail", stopAt), ticker, side, OrderType.StopMarket,
                    qty, (double)stopAt, t.Status, StopLoss: (double)stopAt);
                yield break;
            }
            if (t.StopLoss is decimal sl)
                yield return new OpenOrder(LegId(isolated, ticker, t.Id, "sl", sl), ticker, side, OrderType.StopMarket,
                    qty, (double)sl, t.Status, StopLoss: (double)sl);
            if (t.TakeProfit is decimal tp)
                yield return new OpenOrder(LegId(isolated, ticker, t.Id, "tp", tp), ticker, side, OrderType.TakeProfitMarket,
                    qty, (double)tp, t.Status, TakeProfit: (double)tp);
        }

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
                "stop_loss_past_liquidation" =>
                    "Kalshi refused the stop loss: the position would be liquidated before it fired; set it closer to the price",
                "close_order_blocks_exit_trigger" =>
                    "Kalshi refused the stop or target because a closing order is resting on this position; cancel that order first",
                "anchor_order_unavailable" =>
                    "Kalshi could not tie the stop and target to the order; it may have filled or been cancelled already",
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
