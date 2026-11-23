using System.Globalization;
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Plugins.Binance
{
    /// <summary>
    /// Binance provider talking directly to the public/private REST and WebSocket
    /// endpoints — no Binance.Net / CryptoExchange.Net dependency (see the csproj
    /// header for why). Covers spot + USD-M futures: historical klines, symbols,
    /// the order book (REST + live), the user-data order stream, and full trading
    /// (balances, positions, open orders, place/cancel, leverage, protective TP/SL).
    /// </summary>
    public class BinanceProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider, IOcoTradingProvider, IOrderDryRunProvider, ISymbolLeverageProvider, ITradeStreamProvider
    {
        // ── Endpoints (mainnet / testnet) ─────────────────────────────────────
        private string SpotRest => _isTestnet ? "https://testnet.binance.vision"     : "https://api.binance.com";
        private string FutRest  => _isTestnet ? "https://testnet.binancefuture.com"  : "https://fapi.binance.com";
        private string SpotWs   => _isTestnet ? "wss://testnet.binance.vision/ws/"   : "wss://stream.binance.com:9443/ws/";
        private string FutWs    => _isTestnet ? "wss://stream.binancefuture.com/ws/" : "wss://fstream.binance.com/ws/";
        // Futures market streams (klines, trades) moved to /market on the live host. The old /ws/
        // path still opens for them and then sends nothing, with no error, so a futures chart
        // stopped updating live without a word: measured 2026-11-23, btcusdt@kline_1m gave 0
        // messages in 20 s on /ws/ and 50 on /market/ws/. Depth (depth20) still streams on /ws/.
        // The testnet sends on both paths, so it keeps the one it has.
        private string FutMarketWs => _isTestnet ? "wss://stream.binancefuture.com/ws/" : "wss://fstream.binance.com/market/ws/";

        private HttpClient? _httpField;
        private HttpClient Http => _httpField ??= PluginHostServices.CreateHttpClient(
            "Binance",
            new[] { "api.binance.com", "fapi.binance.com", "testnet.binance.vision", "testnet.binancefuture.com" },
            userAgent: "AccessibleTrader/1.0");

        private string? _apiKey;
        private string? _apiSecret;
        private bool _isTestnet = false;

        // Reads and public calls. This counts REQUESTS, and Binance meters WEIGHT per IP: 6,000
        // a minute on spot (exchangeInfo's rateLimits, read 2026-11-10; this comment said
        // 1,200, the old figure), and on USD-M futures 6,000 on the testnet's answer (mainnet
        // could not be read from here; 2,400 from memory). What the account reads weigh, from
        // Binance's docs fetched 2026-11-10 (spot rest-api.md; futures from Binance's own
        // connector, because the futures docs site answers a script with an empty page):
        //   spot    account 20 · openOrders 6 for one market, 80 for all · myTrades 20
        //   futures positionRisk 5 · openOrders 1, 40 for all · userTrades 5 · leverageBracket 1
        // The dashboard asks for balances, positions, open orders and fills every two seconds:
        // 30 × (20 + 80 + 20) = 3,600 spot weight and 30 × (5 + 40 + 5) = 1,500 futures weight
        // a minute from one window, before the order service's own reads — a second window
        // passes the spot budget, and Binance answers 429 and then bans the IP (418). The reads
        // are cached below (15 s, fills 60 s): about 4 × 120 = 480 and 4 × 50 = 200.
        private readonly RateLimiter _rateLimiter = new(1200, TimeSpan.FromMinutes(1));

        // ORDERS have their own line. Binance meters them apart from weight (spot 100 per
        // 10 s, futures 300 per 10 s, per account), and an order — a stop being moved, a
        // position being closed — used to wait behind the dashboard's reads in the line above.
        // Half the spot allowance, so a burst of protective orders never meets the venue's.
        private readonly RateLimiter _orderLimiter = new(50, TimeSpan.FromSeconds(10));

        // Account reads are cached for a few seconds so that every panel and the order service
        // asking at once is one call, not five. Emptied by every order, cancel and execution
        // report, so nothing the user just did is answered from before they did it.
        private readonly AccountReadCache _reads = new();
        internal static readonly TimeSpan AccountReadTtl = TimeSpan.FromSeconds(15);
        internal static readonly TimeSpan HistoryReadTtl = TimeSpan.FromSeconds(60);

        // Live kline subscription.
        private CancellationTokenSource? _klineCts;
        private string? _currentSymbol;
        private string? _currentTimeframe;

        // Order update stream (user-data).
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Live order book.
        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();
        private CancellationTokenSource? _orderBookCts;
        private string? _orderBookSymbol;

        // User-data stream lifecycle: one listen-key socket per book. The spot
        // stream starts at connect; the futures one on the first futures order.
        private UserDataStream? _spotUserData;
        private UserDataStream? _futuresUserData;
        // Latched by the first futures placement: from then on the futures stream
        // is part of the SupportsOrderEventStreaming contract below.
        private volatile bool _futuresStreamRequired;
        // The charted market, so symbol-only surfaces (depth REST + WS) can follow
        // the book the user is actually looking at.
        private volatile bool _currentIsFutures;

        public override string Name => "Binance";
        public override string Description => "Live Binance Exchange Data (Spot & Futures)";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => _isTestnet ? ProviderEnvironment.Paper : ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 1000;
        /// <summary>
        /// The four order-feature flags at the end were added from audit evidence,
        /// not from judgement: the code already honours <c>signal.ReduceOnly</c>,
        /// <c>signal.PostOnly</c>, <c>signal.TimeInForce</c> (both futures and spot)
        /// and <c>signal.PositionSide</c>, and had no way to say so — which is why
        /// the dashboard could not offer those controls on the provider that
        /// implements them most fully.
        /// </summary>
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.TrailingStop | ProviderCapabilities.OCO |
            ProviderCapabilities.Leverage | ProviderCapabilities.Brackets |
            ProviderCapabilities.MarginTrading | ProviderCapabilities.FuturesTrading |
            ProviderCapabilities.ReduceOnly | ProviderCapabilities.PostOnly |
            ProviderCapabilities.TimeInForce | ProviderCapabilities.HedgeMode;

        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 125.0;

        // Trading is available when we have either Configure-supplied creds or the
        // host credential-checkout bridge to pull an active key at sign time.
        public bool IsConnected => !string.IsNullOrEmpty(_apiKey) || PluginHostServices.ApiKeys != null;

        // Honest, and read at order-placement time: true only while the SPOT
        // user-data socket is actually connected — and, once any futures order has
        // been placed, the futures one too. The old check (listen key string
        // non-empty) stayed true after the key expired, so fills stopped announcing
        // permanently, polling never started, and nothing said so.
        public bool SupportsOrderEventStreaming =>
            (_spotUserData?.IsUp ?? false)
            && (!_futuresStreamRequired || (_futuresUserData?.IsUp ?? false));

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.ThreeMinutes, StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes, StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour,
            StandardTimeframes.TwoHours, StandardTimeframes.FourHours, StandardTimeframes.SixHours,
            StandardTimeframes.EightHours, StandardTimeframes.TwelveHours, StandardTimeframes.OneDay,
            StandardTimeframes.ThreeDays, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider)) return this as T;
            if (typeof(T) == typeof(IOrderBookProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey",    out var key))    _apiKey    = key;
            if (config.TryGetValue("ApiSecret", out var secret)) _apiSecret = secret;
            // bool.TryParse, not == "true": a config value round-tripped through
            // .NET's bool.ToString() arrives as "True", and the old case-sensitive
            // compare silently left testnet OFF — orders the user believed were
            // paper went to the real book.
            // ProviderConfigKeys.Environment is the contract's key; "Testnet" is the same fact in
            // this plugin's older spelling, and the host derives it from Environment so the two
            // cannot disagree. Environment wins when present; Testnet alone is still honoured for
            // a config that predates the contract; NEITHER present means practice — the fail-safe
            // polarity. Until 2026-09-07 Environment was not read at all here, so a Paper profile
            // (which never carried Testnet) signed against LIVE Binance.
            if (config.ContainsKey(ProviderConfigKeys.Environment))
                _isTestnet = !ProviderConfigKeys.IsLive(config);
            else if (config.TryGetValue("Testnet", out var tn))
                _isTestnet = bool.TryParse(tn, out var b) && b;
            else
                _isTestnet = true;
        }

        // Sign-time credential checkout. Prefers the PluginHostServices.ApiKeys
        // bridge (use-and-discard); falls back to Configure-populated fields for
        // unit tests / CLI runs.
        private async Task<(string Key, string Secret)> CheckoutBinanceCredentialsAsync()
        {
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("Binance").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("Binance: no active API key configured.");
                return (checkout.Key, checkout.Secret);
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                throw new InvalidOperationException("Binance: no API credentials configured.");
            return (_apiKey!, _apiSecret!);
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_apiKey) && PluginHostServices.ApiKeys == null)
                return (true, "No API key provided (public data only)");
            try
            {
                await SignedRequestAsync(HttpMethod.Get, SpotRest, "/api/v3/account", new Dictionary<string, string>());
                return (true, "API key validated successfully");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        // ── Connection & subscription management ──────────────────────────────

        public override async Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Connected);

            // Start the authenticated user-data stream only when a credential
            // source exists. Trade ops sign per-request regardless.
            if (IsConnected && _spotUserData == null)
            {
                try { await StartUserDataStreamAsync(); }
                catch { /* public-data-only mode */ }
            }
        }

        private async Task StartUserDataStreamAsync()
        {
            try
            {
                _spotUserData = new UserDataStream(
                    "spot",
                    createKey:   CreateListenKeyAsync,
                    keepAlive:   KeepAliveListenKeyAsync,
                    closeKey:    CloseListenKeyAsync,
                    wsUrlForKey: key => $"{SpotWs}{key}",
                    onFrame:     msg => HandleUserDataFrame(msg, futures: false),
                    onError:     e => _errorStream.OnNext(e));
                await _spotUserData.StartAsync();
            }
            catch (Exception ex)
            {
                _spotUserData = null;
                _errorStream.OnNext($"Binance user-data stream unavailable ({ex.GetType().Name}): order-fill updates won't be delivered.");
            }
        }

        /// <summary>Started by the first futures order. From then on the futures
        /// stream participates in <see cref="SupportsOrderEventStreaming"/>, so if
        /// it is down the order service polls instead of assuming the spot stream
        /// covers a futures fill — before this the futures user-data stream was
        /// never opened at all, and no path announced a futures fill.</summary>
        private async Task EnsureFuturesUserDataStreamAsync()
        {
            _futuresStreamRequired = true;
            if (_futuresUserData != null) return;
            try
            {
                _futuresUserData = new UserDataStream(
                    "futures",
                    createKey:   CreateFuturesListenKeyAsync,
                    keepAlive:   _ => KeepAliveFuturesListenKeyAsync(),
                    closeKey:    _ => CloseFuturesListenKeyAsync(),
                    wsUrlForKey: key => $"{FutWs}{key}",
                    onFrame:     msg => HandleUserDataFrame(msg, futures: true),
                    onError:     e => _errorStream.OnNext(e));
                await _futuresUserData.StartAsync();
            }
            catch (Exception ex)
            {
                _futuresUserData = null;
                _errorStream.OnNext($"Binance futures user-data stream unavailable ({ex.GetType().Name}): futures fills resolve by polling.");
            }
        }

        private void HandleUserDataFrame(string msg, bool futures)
        {
            // Every user-data frame is news about the account — an execution report, a balance
            // change, a funding payment, a liquidation — so the next read asks the venue rather
            // than answer from the cache.
            _reads.Invalidate();
            try
            {
                using var doc = JsonDocument.Parse(msg);
                if (futures) OnFuturesUserDataMessage(doc.RootElement);
                else OnUserDataMessage(doc.RootElement);
            }
            catch (Exception ex)
            {
                // Never a bare swallow: the frame this path drops may be a FILL.
                _errorStream.OnNext($"Binance {(futures ? "futures " : "")}user-data frame could not be processed ({ex.GetType().Name}) — check your open orders.");
            }
        }

        private void OnUserDataMessage(JsonElement root)
        {
            if (!root.TryGetProperty("e", out var ev) || ev.GetString() != "executionReport") return;

            // TryGetProperty throughout: a report variant missing a field must not
            // throw — the frame handler would count the whole frame LOST. The old
            // GetProperty calls threw KeyNotFoundException into a bare catch,
            // dropping the fill with zero trace.
            string statusStr = root.TryGetProperty("X", out var x) ? x.GetString() ?? "" : "";
            var status = MapExecutionStatus(statusStr);

            string symbol   = root.TryGetProperty("s", out var sym) ? sym.GetString() ?? "" : "";
            var side        = root.TryGetProperty("S", out var sd) && sd.GetString() == "SELL" ? OrderSide.Sell : OrderSide.Buy;
            double origQty  = Dbl(root, "q");
            double filled   = Dbl(root, "z");                 // cumulative filled qty
            double lastPx   = Dbl(root, "L");                 // last filled price
            double orderPx  = Dbl(root, "p");
            double fillPx   = lastPx > 0 ? lastPx : orderPx;
            string orderId  = root.TryGetProperty("i", out var idEl) ? idEl.GetRawText() : "";
            string oType    = root.TryGetProperty("o", out var ot) ? ot.GetString() ?? "" : "";

            bool stopTrig = oType is "STOP_LOSS" or "STOP_LOSS_LIMIT";
            bool tpTrig   = oType is "TAKE_PROFIT" or "TAKE_PROFIT_LIMIT";

            _orderUpdateSubject.OnNext(new OrderUpdate(
                orderId, symbol, side,
                filled, fillPx, Math.Max(0, origQty - filled),
                status, stopTrig, tpTrig, DateTime.UtcNow,
                Reason: status == OrderStatus.Unknown ? $"Binance status '{statusStr}'" : null));
        }

        /// <summary>Futures user-data: the order event is ORDER_TRADE_UPDATE with
        /// the payload nested under "o", keys mostly mirroring the spot
        /// executionReport (X status, s, S, q, z, L, p, i) — but the ORIGINAL
        /// order type lives in "ot" ("o" there is the current type).</summary>
        internal void OnFuturesUserDataMessage(JsonElement root)
        {
            if (!root.TryGetProperty("e", out var ev) || ev.GetString() != "ORDER_TRADE_UPDATE") return;
            if (!root.TryGetProperty("o", out var o)) return;

            string statusStr = o.TryGetProperty("X", out var x) ? x.GetString() ?? "" : "";
            var status = MapExecutionStatus(statusStr);

            string symbol   = o.TryGetProperty("s", out var sym) ? sym.GetString() ?? "" : "";
            var side        = o.TryGetProperty("S", out var sd) && sd.GetString() == "SELL" ? OrderSide.Sell : OrderSide.Buy;
            double origQty  = Dbl(o, "q");
            double filled   = Dbl(o, "z");
            double lastPx   = Dbl(o, "L");
            double orderPx  = Dbl(o, "p");
            double fillPx   = lastPx > 0 ? lastPx : orderPx;
            string orderId  = o.TryGetProperty("i", out var idEl) ? idEl.GetRawText() : "";
            string oType    = o.TryGetProperty("ot", out var ot) ? ot.GetString() ?? "" : "";

            bool stopTrig = oType is "STOP" or "STOP_MARKET";
            bool tpTrig   = oType is "TAKE_PROFIT" or "TAKE_PROFIT_MARKET";

            _orderUpdateSubject.OnNext(new OrderUpdate(
                orderId, symbol, side,
                filled, fillPx, Math.Max(0, origQty - filled),
                status, stopTrig, tpTrig, DateTime.UtcNow,
                Reason: status == OrderStatus.Unknown ? $"Binance status '{statusStr}'" : null));
        }

        /// <summary>Maps an executionReport <c>X</c> status to an order status.
        /// EXPIRED used to map to Rejected — but an expired order was ACCEPTED by
        /// the venue and timed out (an IOC remainder, an unfilled post-only), which
        /// is a different fact with a different fix than a refusal. NEW used to be
        /// dropped entirely; it is now logged by the order service. Internal for
        /// direct testing.</summary>
        internal static OrderStatus MapExecutionStatus(string statusStr) => statusStr switch
        {
            "PARTIALLY_FILLED"  => OrderStatus.PartialFill,
            "FILLED"            => OrderStatus.Filled,
            "CANCELED"          => OrderStatus.Cancelled,
            "REJECTED"          => OrderStatus.Rejected,
            "EXPIRED" or "EXPIRED_IN_MATCH" => OrderStatus.Expired,
            "NEW" or "PENDING_NEW"          => OrderStatus.New,
            _                   => OrderStatus.Unknown,
        };

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();

            var cleanSymbol = CleanSymbol(symbol);
            if (_currentSymbol == cleanSymbol && _currentTimeframe == timeframe && _klineCts != null) return;

            _klineCts?.Cancel();
            _klineCts?.Dispose();
            _klineCts = new CancellationTokenSource();
            var ct = _klineCts.Token;

            _currentSymbol = cleanSymbol;
            _currentTimeframe = timeframe;
            _currentIsFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);

            var uri = BuildKlineStreamUri(market, symbol, timeframe);

            _ = Task.Run(() => RunSocketAsync(uri, "kline", OnKlineMessage, ct));
        }

        internal Uri BuildKlineStreamUri(string market, string symbol, string timeframe)
        {
            bool isFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);
            string wsBase = isFutures ? FutMarketWs : SpotWs;
            string interval = MapInterval(timeframe);
            return new Uri($"{wsBase}{CleanSymbol(symbol).ToLowerInvariant()}@kline_{interval}");
        }

        /// <summary>
        /// Every trade on the symbol with the side that took it, for true buy/sell volume
        /// (<see cref="ITradeStreamProvider"/>): the <c>aggTrade</c> stream, spot or USD-M futures
        /// (the futures one on <see cref="FutMarketWs"/>). Each aggregate trade is numbered per
        /// symbol (<c>a</c>), one after another (no jumps in 2,790 spot and 5,711 futures,
        /// 2026-11-23), so a jump means one was lost and the bar is not counted. The stream takes no
        /// text ping; every 30 s it is asked for its subscriptions instead (it answers, and stays
        /// open), so a dead connection fails on the send and reconnects.
        /// </summary>
        public async Task<IAsyncDisposable> SubscribeTradesAsync(string market, string symbol,
            Action<PublicTrade> onTrade, Action<bool> onCounting)
        {
            string clean = CleanSymbol(symbol);
            long lastId = -1;
            var ws = new ReconnectingWebSocket(BuildTradeStreamUri(market, symbol).ToString(), heartbeatInterval: TimeSpan.FromSeconds(30))
                .WithHeartbeatMessage("{\"method\":\"LIST_SUBSCRIPTIONS\",\"id\":1}")
                .OnError(e => _errorStream.OnNext($"Binance trade stream ({clean}): {e}"));
            ws.OnConnected(_ =>
                {
                    Interlocked.Exchange(ref lastId, -1);
                    onCounting(true);
                    return Task.CompletedTask;
                })
              .OnDisconnected(() => onCounting(false))
              .OnMessage(msg =>
                {
                    var (id, trade) = ReadPublicTrade(msg, clean);
                    if (MessageSequence.IsGap(ref lastId, id)) { onCounting(false); onCounting(true); }
                    if (trade is { } t) onTrade(t);
                });
            await ws.ConnectAsync();
            return ws;
        }

        internal Uri BuildTradeStreamUri(string market, string symbol)
        {
            bool isFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);
            return new Uri($"{(isFutures ? FutMarketWs : SpotWs)}{CleanSymbol(symbol).ToLowerInvariant()}@aggTrade");
        }

        /// <summary>
        /// An <c>aggTrade</c> message's number (-1 if it is not one on this symbol) and its trade.
        /// <c>m</c> says the BUYER was the maker, so true is a seller taking (checked live
        /// 2026-11-23 against the book: spot 2,096 to 522, futures 4,676 to 863, the book ticker
        /// being a separate socket that lags). <c>q</c> is the whole quantity. Spot: each closed
        /// minute's sum equalled the kline volume exactly (6 of 6). USD-M futures: 4 of 8 exact and
        /// the rest a little short (0.002 to 1.472 BTC, at most 0.56 percent of the minute); the
        /// futures stream leaves out a few fills the kline counts, and why is not known.
        /// </summary>
        internal static (long Id, PublicTrade? Trade) ReadPublicTrade(string msg, string symbol)
        {
            try
            {
                using var doc = JsonDocument.Parse(msg);
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object
                    || !r.TryGetProperty("e", out var e) || e.ValueKind != JsonValueKind.String || e.GetString() != "aggTrade"
                    || !r.TryGetProperty("s", out var s) || s.ValueKind != JsonValueKind.String
                    || !string.Equals(s.GetString(), symbol, StringComparison.OrdinalIgnoreCase)
                    || !r.TryGetProperty("a", out var a) || a.ValueKind != JsonValueKind.Number || !a.TryGetInt64(out long id))
                    return (-1, null);
                if (!r.TryGetProperty("m", out var m) || m.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return (id, null);
                if (!Decimal(r, "p", out double price) || price <= 0 || !Decimal(r, "q", out double qty) || qty <= 0) return (id, null);
                if (!r.TryGetProperty("T", out var t) || t.ValueKind != JsonValueKind.Number || !t.TryGetInt64(out long ms)) return (id, null);
                return (id, new PublicTrade(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, price, qty,
                    m.GetBoolean() ? OrderSide.Sell : OrderSide.Buy));
            }
            catch (JsonException) { return (-1, null); }

            static bool Decimal(JsonElement r, string name, out double value)
            {
                value = 0;
                return r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }

        internal static bool TryParseKline(JsonElement root, out Ohlcv bar)
        {
            bar = default;
            if (!root.TryGetProperty("k", out var k)) return false;
            long openTime = k.GetProperty("t").GetInt64();
            bar = new Ohlcv(
                DateTimeOffset.FromUnixTimeMilliseconds(openTime).UtcDateTime,
                Dbl(k, "o"), Dbl(k, "h"), Dbl(k, "l"), Dbl(k, "c"), Dbl(k, "v"));
            return !(bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0);
        }

        private void OnKlineMessage(JsonElement root)
        {
            if (TryParseKline(root, out var bar))
                _liveStream.OnNext(bar);
        }

        // ── Keyed-feed subscriptions (multiple concurrent live streams) ───────

        // Kline messages carry the current kline's CUMULATIVE volume-so-far;
        // consolidation must diff, not accumulate (see LiveTickStyle docs).
        public override LiveTickStyle LiveTickStyle => LiveTickStyle.CumulativeBars;

        public override bool SupportsMultipleLiveSubscriptions => true;

        /// <summary>
        /// One public kline WebSocket per subscription — no auth, no interaction
        /// with the focused-chart subscription, and well within Binance's 300
        /// connections / 5 min / IP limit at the hub's feed scale. RunSocketAsync
        /// owns reconnection for the life of the handle.
        /// </summary>
        // Outstanding keyed-feed subscriptions, so DisconnectAsync can tear down
        // every socket — without this, background kline sockets kept reconnecting
        // after a disconnect until the hub disposed their handles.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> _keyedSubscriptions = new();
        internal int ActiveKeyedSubscriptionCount => _keyedSubscriptions.Count;

        public override Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
        {
            var uri = BuildKlineStreamUri(market, symbol, timeframe);
            var cts = new CancellationTokenSource();
            var id = Guid.NewGuid();
            _keyedSubscriptions[id] = cts;
            var consolidator = new BarBucketConsolidator(timeframe, LiveTickStyle);

            _ = Task.Run(() => RunSocketAsync(uri, "kline", root =>
            {
                if (!TryParseKline(root, out var raw)) return;
                var bar = consolidator.Apply(raw);
                if (bar.HasValue) onBar(bar.Value);
            }, cts.Token));

            return Task.FromResult<IAsyncDisposable>(new LiveSubscriptionHandle(this, id, cts));
        }

        private void ReleaseKeyedSubscription(Guid id, CancellationTokenSource cts)
        {
            _keyedSubscriptions.TryRemove(id, out _);
            // DisconnectAsync may have already cancelled+disposed this CTS — a
            // handle disposed after disconnect must be a clean no-op.
            try { cts.Cancel(); cts.Dispose(); }
            catch (ObjectDisposedException) { /* already torn down by DisconnectAsync */ }
        }

        private sealed class LiveSubscriptionHandle : IAsyncDisposable
        {
            private BinanceProvider? _owner;
            private readonly Guid _id;
            private readonly CancellationTokenSource _cts;
            public LiveSubscriptionHandle(BinanceProvider owner, Guid id, CancellationTokenSource cts)
            {
                _owner = owner; _id = id; _cts = cts;
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _owner, null)?.ReleaseKeyedSubscription(_id, _cts);
                return ValueTask.CompletedTask;
            }
        }

        public override async Task DisconnectAsync()
        {
            // Keyed-feed sockets (SubscribeLiveAsync) die with the provider too —
            // their handles remain valid to dispose but become no-ops.
            foreach (var kv in _keyedSubscriptions.ToArray())
            {
                if (_keyedSubscriptions.TryRemove(kv.Key, out var subCts))
                {
                    try { subCts.Cancel(); subCts.Dispose(); } catch { /* already torn down */ }
                }
            }

            _klineCts?.Cancel();      _klineCts?.Dispose();      _klineCts = null;
            _orderBookCts?.Cancel();  _orderBookCts?.Dispose();  _orderBookCts = null;
            _orderBookSymbol = null;

            if (_spotUserData != null)    { await _spotUserData.DisposeAsync();    _spotUserData = null; }
            if (_futuresUserData != null) { await _futuresUserData.DisposeAsync(); _futuresUserData = null; }
            _futuresStreamRequired = false;

            _currentSymbol = null;
            _currentTimeframe = null;

            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            await Task.CompletedTask;
        }

        // ── Data discovery & fetching ─────────────────────────────────────────

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Spot", "Futures" });

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    bool isFutures = subType.Equals("Futures", StringComparison.OrdinalIgnoreCase);
                    string body = await GetPublicAsync(isFutures ? FutRest : SpotRest,
                        isFutures ? "/fapi/v1/exchangeInfo" : "/api/v3/exchangeInfo", null);
                    using var doc = JsonDocument.Parse(body);
                    // The same answer carries every market's order rules; keep them rather
                    // than ask again at the first order.
                    StoreRules(doc.RootElement, isFutures);
                    return doc.RootElement.GetProperty("symbols")
                        .EnumerateArray()
                        .Select(s => s.GetProperty("symbol").GetString() ?? "")
                        .Where(s => s.Length > 0)
                        .OrderBy(s => s, StringComparer.Ordinal)
                        .ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance GetSymbolsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(new List<string> { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M" });

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var cleanSymbol = CleanSymbol(request.Symbol);
            bool isFutures = request.Market.Contains("Futures", StringComparison.OrdinalIgnoreCase);
            string interval = MapInterval(request.Timeframe);
            int limit = Math.Min(request.Limit, 1000);

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var q = new StringBuilder();
                    q.Append("symbol=").Append(cleanSymbol).Append("&interval=").Append(interval).Append("&limit=").Append(limit);
                    if (request.Since != null) q.Append("&startTime=").Append(request.Since.Value);
                    if (request.Until != null) q.Append("&endTime=").Append(request.Until.Value);

                    string body = await GetPublicAsync(isFutures ? FutRest : SpotRest,
                        isFutures ? "/fapi/v1/klines" : "/api/v3/klines", q.ToString());

                    using var doc = JsonDocument.Parse(body);
                    var ohlcv = new List<Ohlcv>();
                    foreach (var row in doc.RootElement.EnumerateArray())
                    {
                        // [ openTime, open, high, low, close, volume, ... ]
                        long openTime = row[0].GetInt64();
                        ohlcv.Add(new Ohlcv(
                            DateTimeOffset.FromUnixTimeMilliseconds(openTime).UtcDateTime,
                            DblAt(row, 1), DblAt(row, 2), DblAt(row, 3), DblAt(row, 4), DblAt(row, 5)));
                    }
                    var vol = ohlcv.Select(x => (new DateTimeOffset(x.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), x.Volume)).ToList();
                    return (ohlcv, vol);
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance FetchOhlcvAsync failed for {request.Symbol} ({ex.GetType().Name}): {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            var cleanSymbol = CleanSymbol(symbol);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    // The depth contract carries no market, so follow the book the
                    // user is charting — the spot book's depth for a futures chart
                    // is a different market's liquidity presented as this one's.
                    bool fut = _currentIsFutures && cleanSymbol == _currentSymbol;
                    string body = await GetPublicAsync(fut ? FutRest : SpotRest,
                        fut ? "/fapi/v1/depth" : "/api/v3/depth",
                        $"symbol={cleanSymbol}&limit={SnapDepth(limit)}");
                    using var doc = JsonDocument.Parse(body);
                    return (ParseLevels(doc.RootElement.GetProperty("bids")),
                            ParseLevels(doc.RootElement.GetProperty("asks")));
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance GetOrderBookAsync failed for {symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        // ── IOrderBookProvider ────────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol)
        {
            var cleanSymbol = CleanSymbol(symbol);
            if (_orderBookSymbol == cleanSymbol && _orderBookCts != null)
                return _orderBookSubject.AsObservable();

            _orderBookCts?.Cancel();
            _orderBookCts?.Dispose();
            _orderBookCts = new CancellationTokenSource();
            var ct = _orderBookCts.Token;
            _orderBookSymbol = cleanSymbol;

            bool fut = _currentIsFutures && cleanSymbol == _currentSymbol;
            var uri = new Uri(fut
                ? $"{FutWs}{cleanSymbol.ToLowerInvariant()}@depth20@500ms"
                : $"{SpotWs}{cleanSymbol.ToLowerInvariant()}@depth20@1000ms");
            _ = Task.Run(() => RunSocketAsync(uri, "order book", root =>
            {
                if (!root.TryGetProperty("bids", out var bids) || !root.TryGetProperty("asks", out var asks)) return;
                _orderBookSubject.OnNext(new OrderBookUpdate(
                    cleanSymbol, ParseLevels(bids), ParseLevels(asks), 0, DateTime.UtcNow));
            }, ct));

            return _orderBookSubject.AsObservable();
        }

        // ── Order rules and leverage limits ───────────────────────────────────

        // Spot rules by market (one exchangeInfo?symbol= read each — the whole spot answer is
        // megabytes), futures rules for the whole venue (its exchangeInfo takes no symbol),
        // and each futures market's most leverage. All read at most every six hours; a failed
        // read is not remembered, so the next order asks again.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, BinanceOrderRules? Rules)> _spotRules =
            new(StringComparer.OrdinalIgnoreCase);
        private volatile Dictionary<string, BinanceOrderRules> _futuresRules = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _futuresRulesAt = DateTime.MinValue;
        private volatile Dictionary<string, int> _maxLeverage = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _maxLeverageAt = DateTime.MinValue;
        internal static readonly TimeSpan RulesTtl = TimeSpan.FromHours(6);

        /// <summary>How long an order waits for a rules or leverage read before going without
        /// it. Those reads sit in front of real orders — a close, a stop — and the venue judges
        /// an order without them exactly as it always did.</summary>
        private static readonly TimeSpan PreflightTimeout = TimeSpan.FromSeconds(5);

        private void StoreRules(JsonElement exchangeInfo, bool futures)
        {
            try
            {
                var parsed = BinanceOrderRules.Parse(exchangeInfo, futures);
                if (futures)
                {
                    _futuresRules = parsed;
                    _futuresRulesAt = DateTime.UtcNow;
                }
                else
                {
                    foreach (var (symbol, rules) in parsed) _spotRules[symbol] = (DateTime.UtcNow, rules);
                }
            }
            catch (Exception ex)
            {
                // The symbol list this answer was read for must not fail over its rules.
                _errorStream.OnNext($"Binance order rules could not be read ({ex.GetType().Name}).");
            }
        }

        /// <summary>
        /// The market's order rules, or null when they cannot be had — the order then goes out
        /// as it did before 2026-11-10 and the venue judges it, rather than a failed public
        /// lookup blocking an order the venue would have taken.
        /// </summary>
        internal async Task<BinanceOrderRules?> GetOrderRulesAsync(string symbol, bool futures)
        {
            string wire = CleanSymbol(symbol);
            if (futures)
            {
                if (DateTime.UtcNow - _futuresRulesAt > RulesTtl)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(PreflightTimeout);
                        await _rateLimiter.WaitAsync(cts.Token).ConfigureAwait(false);
                        string body = await GetPublicAsync(FutRest, "/fapi/v1/exchangeInfo", null, cts.Token).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(body);
                        StoreRules(doc.RootElement, futures: true);
                    }
                    catch (Exception ex)
                    {
                        _errorStream.OnNext($"Binance futures order rules could not be read ({ex.GetType().Name}); the venue will judge the order.");
                    }
                }
                return _futuresRules.TryGetValue(wire, out var f) ? f : null;
            }

            if (_spotRules.TryGetValue(wire, out var cached) && DateTime.UtcNow - cached.At <= RulesTtl)
                return cached.Rules;
            try
            {
                using var cts = new CancellationTokenSource(PreflightTimeout);
                await _rateLimiter.WaitAsync(cts.Token).ConfigureAwait(false);
                string body = await GetPublicAsync(SpotRest, "/api/v3/exchangeInfo",
                    "symbol=" + Uri.EscapeDataString(wire), cts.Token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                StoreRules(doc.RootElement, futures: false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance order rules for {wire} could not be read ({ex.GetType().Name}); the venue will judge the order.");
                return null;
            }
            return _spotRules.TryGetValue(wire, out var fresh) ? fresh.Rules : null;
        }

        /// <summary>
        /// The most this futures market lends at, from the account's leverage brackets, or null
        /// when they cannot be read (no futures permission on the key, the venue unreachable).
        /// With null the order still goes; the venue's answer to the leverage change decides.
        /// </summary>
        internal async Task<int?> GetSymbolMaxLeverageAsync(string symbol, CancellationToken ct = default)
        {
            string wire = CleanSymbol(symbol);
            if (DateTime.UtcNow - _maxLeverageAt > RulesTtl)
            {
                if (!IsConnected) return null;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(PreflightTimeout);
                    await _rateLimiter.WaitAsync(cts.Token).ConfigureAwait(false);
                    string body = await SignedRequestAsync(HttpMethod.Get, FutRest, "/fapi/v1/leverageBracket",
                        new Dictionary<string, string>(), cts.Token).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    _maxLeverage = BinanceOrderRules.ParseLeverageBrackets(doc.RootElement);
                    _maxLeverageAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _errorStream.OnNext($"Binance leverage limits could not be read ({ex.GetType().Name}); the venue will judge the leverage.");
                    return null;
                }
            }
            return _maxLeverage.TryGetValue(wire, out int max) ? max : null;
        }

        /// <summary>
        /// The leverage amounts the order ticket offers on this market: 2 up to the most the
        /// account's brackets allow (1, no leverage, is always offered by the ticket itself).
        /// Binance lends at every whole number in between and the same on both sides. Empty for
        /// a market with no futures contract, where nothing is lent; null when the brackets
        /// could not be read, and the ticket falls back to <see cref="MaxLeverage"/>.
        ///
        /// <para>The ticket asks with the symbol alone, not spot or futures, so on a spot chart
        /// of a market that also has a contract this still names the contract's limits; spot
        /// orders send no leverage at all.</para>
        /// </summary>
        public async Task<IReadOnlyList<int>?> GetAllowedLeverageAsync(string symbol, OrderSide side, CancellationToken ct = default)
        {
            if (await GetSymbolMaxLeverageAsync(symbol, ct).ConfigureAwait(false) is int max)
                return Enumerable.Range(2, Math.Max(0, max - 1)).ToList();
            // Brackets read, but this market is not among them: no contract, nothing lent.
            return DateTime.UtcNow - _maxLeverageAt <= RulesTtl && _maxLeverage.Count > 0
                ? Array.Empty<int>()
                : null;
        }

        // ── ITradingProvider ──────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            var rows = await _reads.GetAsync("balances", AccountReadTtl, () => _rateLimiter.ExecuteAsync(async () =>
            {
                string body = await SignedRequestAsync(HttpMethod.Get, SpotRest, "/api/v3/account", new Dictionary<string, string>());
                using var doc = JsonDocument.Parse(body);
                var list = new List<Balance>();
                foreach (var b in doc.RootElement.GetProperty("balances").EnumerateArray())
                {
                    double free = Dbl(b, "free");
                    double locked = Dbl(b, "locked");
                    if (free > 0 || locked > 0)
                        list.Add(new Balance(b.GetProperty("asset").GetString() ?? "", free, locked));
                }
                return list;
            }));
            return rows.ToList();
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            var rows = await _reads.GetAsync("positions", AccountReadTtl, () => _rateLimiter.ExecuteAsync(async () =>
            {
                string body = await SignedRequestAsync(HttpMethod.Get, FutRest, "/fapi/v2/positionRisk", new Dictionary<string, string>());
                using var doc = JsonDocument.Parse(body);
                var list = new List<Position>();
                foreach (var p in doc.RootElement.EnumerateArray())
                {
                    double qty = Dbl(p, "positionAmt");
                    if (qty == 0) continue;
                    double mark = Dbl(p, "markPrice");
                    list.Add(new Position(
                        p.GetProperty("symbol").GetString() ?? "",
                        // Signed as positionAmt reports it: consumers derive
                        // long/short from the sign; Abs made a short read long.
                        qty,
                        Dbl(p, "entryPrice"),
                        Math.Abs(qty) * mark,
                        Dbl(p, "unRealizedProfit"),
                        Dbl(p, "leverage"),
                        Dbl(p, "liquidationPrice")));
                }
                return list;
            }));
            return rows.ToList();
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            string key = "orders:" + (symbol != null ? CleanSymbol(symbol) : "*");
            var rows = await _reads.GetAsync(key, AccountReadTtl, () => _rateLimiter.ExecuteAsync(async () =>
            {
                var p = new Dictionary<string, string>();
                if (symbol != null) p["symbol"] = CleanSymbol(symbol);
                string body = await SignedRequestAsync(HttpMethod.Get, SpotRest, "/api/v3/openOrders", p);
                var list = ParseOpenOrders(body);
                // Futures book too — Capabilities declares FuturesTrading, and a
                // futures order missing from this list can be neither watched nor
                // cancelled from the dashboard.
                list.AddRange(await GetFuturesOpenOrdersAsync(symbol));
                return list;
            }));
            return rows.ToList();
        }

        private static List<OpenOrder> ParseOpenOrders(string body)
        {
            // Spot /api/v3/openOrders and futures /fapi/v1/openOrders share these
            // field names exactly.
            using var doc = JsonDocument.Parse(body);
            var list = new List<OpenOrder>();
            foreach (var o in doc.RootElement.EnumerateArray())
            {
                list.Add(new OpenOrder(
                    o.GetProperty("orderId").GetRawText(),
                    o.GetProperty("symbol").GetString() ?? "",
                    (o.GetProperty("side").GetString() == "SELL") ? OrderSide.Sell : OrderSide.Buy,
                    MapOrderType(o.GetProperty("type").GetString() ?? ""),
                    Dbl(o, "origQty"),
                    Dbl(o, "price"),
                    o.GetProperty("status").GetString() ?? ""));
            }
            return list;
        }

        /// <summary>Futures open orders, tolerating exactly one failure shape: the
        /// venue refusing because the key has no futures permission — that is a
        /// NotSupported answer, not a transport failure. Everything else
        /// propagates so the order service classifies it (see ProviderResult).</summary>
        private async Task<List<OpenOrder>> GetFuturesOpenOrdersAsync(string? symbol)
        {
            var p = new Dictionary<string, string>();
            if (symbol != null) p["symbol"] = CleanSymbol(symbol);
            string body;
            try { body = await SignedRequestAsync(HttpMethod.Get, FutRest, "/fapi/v1/openOrders", p); }
            catch (HttpRequestException ex) when (IsFuturesPermissionRefusal(ex)) { return new(); }
            return ParseOpenOrders(body);
        }

        // Binance -2015: "Invalid API-key, IP, or permissions for action" — the
        // shape a spot-only key gets from every /fapi endpoint. Deliberately
        // narrow: transport failures and every other venue error propagate.
        private static bool IsFuturesPermissionRefusal(HttpRequestException ex) =>
            ex.Message.Contains("-2015");

        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            // Binance spot /myTrades requires a symbol; without one, return empty.
            if (!IsConnected || string.IsNullOrEmpty(symbol)) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            string key = $"fills:{CleanSymbol(symbol)}:{limit}";
            var rows = await _reads.GetAsync(key, HistoryReadTtl, () => _rateLimiter.ExecuteAsync(async () =>
            {
                var p = new Dictionary<string, string>
                {
                    ["symbol"] = CleanSymbol(symbol),
                    ["limit"] = Math.Clamp(limit, 1, 1000).ToString()
                };
                string body = await SignedRequestAsync(HttpMethod.Get, SpotRest, "/api/v3/myTrades", p);
                using var doc = JsonDocument.Parse(body);
                var list = new List<TradeFill>();
                foreach (var t in doc.RootElement.EnumerateArray())
                {
                    bool isBuyer = t.TryGetProperty("isBuyer", out var ib) && ib.GetBoolean();
                    long time = t.TryGetProperty("time", out var tm) ? tm.GetInt64() : 0;
                    list.Add(new TradeFill(
                        t.TryGetProperty("id", out var id) ? id.GetRawText() : "",
                        t.GetProperty("symbol").GetString() ?? symbol!,
                        isBuyer ? OrderSide.Buy : OrderSide.Sell,
                        Dbl(t, "qty"),
                        Dbl(t, "price"),
                        DateTimeOffset.FromUnixTimeMilliseconds(time).UtcDateTime,
                        Dbl(t, "commission"),
                        t.TryGetProperty("orderId", out var oid) ? oid.GetRawText() : null));
                }
                // Futures fills too — the poller resolves futures orders from this
                // list, and futures trades never appear in spot /myTrades.
                list.AddRange(await GetFuturesFillsAsync(symbol!, limit));
                return list.OrderByDescending(f => f.FilledAt).ToList();  // newest first, both books
            }));
            return rows.ToList();
        }

        /// <summary>Futures fill history (/fapi/v1/userTrades). Same single
        /// tolerated failure shape as the open-orders leg: a spot-only key's
        /// permission refusal reads as "no futures fills"; everything else
        /// propagates. Futures trades carry an explicit "side" (spot has
        /// "isBuyer") and "buyer" — side is authoritative.</summary>
        private async Task<List<TradeFill>> GetFuturesFillsAsync(string symbol, int limit)
        {
            var p = new Dictionary<string, string>
            {
                ["symbol"] = CleanSymbol(symbol),
                ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture)
            };
            string body;
            try { body = await SignedRequestAsync(HttpMethod.Get, FutRest, "/fapi/v1/userTrades", p); }
            catch (HttpRequestException ex) when (IsFuturesPermissionRefusal(ex)) { return new(); }
            using var doc = JsonDocument.Parse(body);
            var list = new List<TradeFill>();
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                var side = t.TryGetProperty("side", out var sd) && sd.GetString() == "SELL" ? OrderSide.Sell : OrderSide.Buy;
                long time = t.TryGetProperty("time", out var tm) ? tm.GetInt64() : 0;
                list.Add(new TradeFill(
                    t.TryGetProperty("id", out var id) ? id.GetRawText() : "",
                    t.GetProperty("symbol").GetString() ?? symbol,
                    side,
                    Dbl(t, "qty"),
                    Dbl(t, "price"),
                    DateTimeOffset.FromUnixTimeMilliseconds(time).UtcDateTime,
                    Dbl(t, "commission"),
                    t.TryGetProperty("orderId", out var oid) ? oid.GetRawText() : null));
            }
            return list;
        }

        /// <summary>
        /// Exchange-native spot OCO via POST /api/v3/orderList/oco (the current
        /// endpoint; the legacy /api/v3/order/oco was retired). Leg layout per
        /// Binance's above/below vocabulary: for a SELL pair the LIMIT_MAKER
        /// (take profit) sits ABOVE and the STOP_LOSS BELOW; a BUY pair is the
        /// mirror (breakout stop above, pullback limit below). The exchange
        /// enforces the layout server-side too — an inverted pair is rejected.
        /// </summary>
        public async Task<string> PlaceOcoPairAsync(string symbol, OrderSide side, double quantity,
            double limitPrice, double stopTriggerPrice)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                // Both legs are the same size, so the smaller-valued leg is the one the venue
                // judges against its minimum.
                var r = (await GetOrderRulesAsync(symbol, futures: false))?.Orders;
                if (r?.Refusal(quantity, Math.Min(limitPrice, stopTriggerPrice)) is string refusal)
                    return "ORDER_FAILED:" + refusal;
                string Px(double v) => r?.FormatPrice(v) ?? Fmt(v);

                return await _orderLimiter.ExecuteOnceAsync(async () =>
                {
                    var p = new Dictionary<string, string>
                    {
                        ["symbol"] = CleanSymbol(symbol),
                        ["side"] = side == OrderSide.Buy ? "BUY" : "SELL",
                        ["quantity"] = r?.FormatQuantity(quantity) ?? Fmt(quantity),
                    };
                    if (side == OrderSide.Sell)
                    {
                        p["aboveType"] = "LIMIT_MAKER";
                        p["abovePrice"] = Px(limitPrice);
                        p["belowType"] = "STOP_LOSS";
                        p["belowStopPrice"] = Px(stopTriggerPrice);
                    }
                    else
                    {
                        p["aboveType"] = "STOP_LOSS";
                        p["aboveStopPrice"] = Px(stopTriggerPrice);
                        p["belowType"] = "LIMIT_MAKER";
                        p["belowPrice"] = Px(limitPrice);
                    }

                    _reads.Invalidate();
                    string body = await SignedRequestAsync(HttpMethod.Post, SpotRest, "/api/v3/orderList/oco", p);
                    _reads.Invalidate();
                    var doc = System.Text.Json.JsonDocument.Parse(body);
                    return doc.RootElement.TryGetProperty("orderListId", out var id)
                        ? id.GetRawText()
                        : "ORDER_FAILED:no orderListId in response";
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance OCO error: {ex.GetType().Name}");
                return $"ORDER_FAILED:{ex.Message}";
            }
        }

        /// <summary>
        /// The spot order parameters for a signal — ONE builder shared by <see cref="PlaceOrderAsync"/>
        /// and <see cref="DryRunOrderAsync"/>, so what <c>/api/v3/order/test</c> validates is exactly
        /// what <c>/api/v3/order</c> would be sent.
        /// <para>
        /// <paramref name="rules"/> are the market's exchangeInfo rules (null when they could not
        /// be read: the order is then built as it was before 2026-11-10 and the venue judges
        /// it). With them, an order the venue would refuse for its size or value is refused
        /// here in words, and every quantity and price is cut to the market's step and tick.
        /// </para>
        /// </summary>
        private static (Dictionary<string, string>? Params, string? Error) BuildSpotOrderParams(
            TradeSignal signal, string symbol, string side, BinanceOrderRules? rules = null)
        {
            var p = new Dictionary<string, string> { ["symbol"] = symbol, ["side"] = side };
            // Standalone stop/TP order types trigger at TriggerPrice;
            // fall back to StopLoss/TakeProfit for older callers.
            double? trig = signal.TriggerPrice
                ?? (signal.Type is OrderType.StopMarket or OrderType.StopLimit ? signal.StopLoss : signal.TakeProfit);

            var r = rules?.For(signal.Type);
            if (r?.Refusal(signal.Quantity, PriceForValue(signal, trig)) is string refusal)
                return (null, "ORDER_FAILED:" + refusal);
            string Qty() => r?.FormatQuantity(signal.Quantity) ?? Fmt(signal.Quantity);
            string Px(double v) => r?.FormatPrice(v) ?? Fmt(v);

            switch (signal.Type)
            {
                case OrderType.Market:
                    p["type"] = "MARKET"; p["quantity"] = Qty();
                    break;
                case OrderType.Limit when signal.Price.HasValue:
                    p["quantity"] = Qty(); p["price"] = Px(signal.Price.Value);
                    if (signal.PostOnly) { p["type"] = "LIMIT_MAKER"; }                 // spot maker-only
                    else { p["type"] = "LIMIT"; p["timeInForce"] = SpotTif(signal); }
                    break;
                case OrderType.StopMarket when trig.HasValue:
                    p["type"] = "STOP_LOSS"; p["quantity"] = Qty();
                    p["stopPrice"] = Px(trig.Value);
                    break;
                case OrderType.StopLimit when trig.HasValue && signal.Price.HasValue:
                    p["type"] = "STOP_LOSS_LIMIT"; p["quantity"] = Qty();
                    p["price"] = Px(signal.Price.Value); p["stopPrice"] = Px(trig.Value);
                    p["timeInForce"] = SpotTif(signal);
                    break;
                case OrderType.TakeProfitMarket when trig.HasValue:
                    p["type"] = "TAKE_PROFIT"; p["quantity"] = Qty();
                    p["stopPrice"] = Px(trig.Value);
                    break;
                case OrderType.TakeProfitLimit when trig.HasValue && signal.Price.HasValue:
                    p["type"] = "TAKE_PROFIT_LIMIT"; p["quantity"] = Qty();
                    p["price"] = Px(signal.Price.Value); p["stopPrice"] = Px(trig.Value);
                    p["timeInForce"] = SpotTif(signal);
                    break;
                default:
                    return (null, "ORDER_FAILED:Unsupported order type");
            }
            if (!string.IsNullOrEmpty(signal.ClientOid)) p["newClientOrderId"] = signal.ClientOid!;
            return (p, null);
        }

        /// <summary>
        /// The price an order's value is judged at: its limit price, or its trigger when it has
        /// none. A market order has neither, and its value is left to the venue (Binance judges
        /// it at a recent average this plugin does not have).
        /// </summary>
        private static double? PriceForValue(TradeSignal signal, double? trigger) => signal.Type switch
        {
            OrderType.Market => null,
            OrderType.Limit or OrderType.StopLimit or OrderType.TakeProfitLimit => signal.Price ?? trigger,
            _ => trigger,
        };

        /// <summary>
        /// Binance spot's <c>POST /api/v3/order/test</c>: the same signed parameters as a real
        /// order, validated by the venue (symbol filters, balance, precision), nothing placed.
        /// USD-M futures has no equivalent endpoint, so a Futures signal cannot be dry-run and
        /// this throws rather than pretend. Unused anywhere in this repo before 2026-09-07.
        /// </summary>
        public async Task<OrderDryRunResult> DryRunOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) throw new InvalidOperationException("Binance: no API key configured, so the venue cannot be asked.");
            if (string.Equals(signal.SubType, "Futures", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Binance USD-M futures has no order test endpoint; only spot orders can be dry-run.");

            var symbol = CleanSymbol(signal.Symbol);
            string side = signal.Side == OrderSide.Buy ? "BUY" : "SELL";
            var rules = await GetOrderRulesAsync(symbol, futures: false);
            var (p, error) = BuildSpotOrderParams(signal, symbol, side, rules);
            if (p is null) return new OrderDryRunResult(false, error!);

            try
            {
                await _orderLimiter.ExecuteOnceAsync(() => SignedRequestAsync(HttpMethod.Post, SpotRest, "/api/v3/order/test", p));
                return new OrderDryRunResult(true, "Binance validated the order (nothing was placed)");
            }
            catch (HttpRequestException ex)
            {
                // The venue answered and said no — that is a verdict, not a transport failure.
                return new OrderDryRunResult(false, ex.Message);
            }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            bool isFutures = string.Equals(signal.SubType, "Futures", StringComparison.OrdinalIgnoreCase);
            try
            {
                var symbol = CleanSymbol(signal.Symbol);

                // Looked up BEFORE the order takes its slot: none of them is the order itself,
                // and the order's own call must stay the single, never-retried POST.
                var rules = await GetOrderRulesAsync(symbol, isFutures);
                double? futTrig = signal.Type is OrderType.StopMarket or OrderType.StopLimit
                    ? signal.TriggerPrice ?? signal.StopLoss
                    : signal.TriggerPrice ?? signal.TakeProfit;
                int leverage = 1;
                if (isFutures)
                {
                    // A reduce-only order closes, and Binance does not judge a close by its
                    // value (a position worth less than the minimum must still be closable).
                    double? valueAt = signal.ReduceOnly ? null : PriceForValue(signal, futTrig);
                    if (rules?.For(signal.Type).Refusal(signal.Quantity, valueAt) is string refusal)
                        return "ORDER_FAILED:" + refusal;
                    if (!signal.ReduceOnly)
                    {
                        var (want, leverageRefusal) = await CheckFuturesLeverageAsync(signal, symbol);
                        if (leverageRefusal != null) return "ORDER_FAILED:" + leverageRefusal;
                        leverage = want;
                    }
                }

                return await _orderLimiter.ExecuteOnceAsync(async () =>
                {
                    string side = signal.Side == OrderSide.Buy ? "BUY" : "SELL";

                    if (isFutures)
                    {
                        // The stream is part of the placement contract: the order
                        // service reads SupportsOrderEventStreaming right after
                        // this returns, and a futures fill arrives ONLY on the
                        // futures user-data stream (or by polling if it is down).
                        await EnsureFuturesUserDataStreamAsync();

                        // The leverage change is part of the order, not a preamble to it.
                        //
                        // This used to be a bare `await SetLeverageAsync(...)` with the result
                        // discarded, and SetLeverageAsync swallows every failure into
                        // `return 1.0`. If the venue refused the change — insufficient margin
                        // tier, symbol not enabled for that leverage, a clock-skew -1021 — the
                        // futures entry still went in at WHATEVER LEVERAGE THE ACCOUNT ALREADY
                        // HAD, which may be 20x left over from a previous session. The user
                        // heard the SetLeverageAsync error, but it does not say "your order is
                        // going in at the old leverage", and the order proceeded anyway.
                        //
                        // A position's leverage is a property of the trade, so a leverage the
                        // venue would not grant means the trade the user asked for cannot be
                        // placed. Refuse it and say so; the ticket is still there to retry.
                        //
                        // Until 2026-11-10 this ran only above 1 times. The ticket sends no
                        // leverage at 1 (no leverage), so an order at 1 went in at whatever the
                        // market was last set to — 20 times from a previous session, or from the
                        // venue's own default — while the review had said nothing about leverage
                        // at all. Every opening order now sets it, 1 included. A failed change is
                        // no longer read as "1 times applied": that was the old fallback's number,
                        // and at 1 it matched the request.
                        if (!signal.ReduceOnly)
                        {
                            var (applied, why) = await TrySetLeverageAsync(symbol, leverage);
                            if (applied is null)
                                return $"ORDER_FAILED:the venue would not set {leverage} times "
                                     + $"leverage on {symbol} ({why}), so nothing was placed.";
                            if (applied.Value != leverage)
                                return $"ORDER_FAILED:the venue would not set {leverage} times "
                                     + $"leverage on {symbol}, so nothing was placed. "
                                     + $"The account is still at {applied.Value} times.";
                        }

                        var p = new Dictionary<string, string> { ["symbol"] = symbol, ["side"] = side };
                        string futType = signal.Type switch
                        {
                            OrderType.Limit            => "LIMIT",
                            OrderType.StopMarket       => "STOP_MARKET",
                            OrderType.StopLimit        => "STOP",
                            OrderType.TakeProfitMarket => "TAKE_PROFIT_MARKET",
                            OrderType.TakeProfitLimit  => "TAKE_PROFIT",
                            _                          => "MARKET"
                        };
                        p["type"] = futType;
                        var r = rules?.For(signal.Type);
                        string Px(double v) => r?.FormatPrice(v) ?? Fmt(v);
                        p["quantity"] = r?.FormatQuantity(signal.Quantity) ?? Fmt(signal.Quantity);
                        // reduceOnly and positionSide are mutually exclusive on Binance
                        // (hedge mode rejects reduceOnly); only send reduceOnly in one-way mode.
                        if (signal.ReduceOnly && string.IsNullOrEmpty(signal.PositionSide)) p["reduceOnly"] = "true";
                        if (!string.IsNullOrEmpty(signal.PositionSide)) p["positionSide"] = signal.PositionSide!;
                        if (futType != "MARKET")
                        {
                            if (signal.Price.HasValue) p["price"] = Px(signal.Price.Value);
                            bool isStopOrTp = futType is "STOP_MARKET" or "STOP" or "TAKE_PROFIT_MARKET" or "TAKE_PROFIT";
                            if (isStopOrTp && futTrig.HasValue) p["stopPrice"] = Px(futTrig.Value);
                            p["timeInForce"] = ResolveTif(signal);
                        }
                        if (!string.IsNullOrEmpty(signal.ClientOid)) p["newClientOrderId"] = signal.ClientOid!;

                        _reads.Invalidate();
                        string body = await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/order", p);
                        _reads.Invalidate();
                        string id = ParseOrderId(body);

                        // Protective TP/SL attach (separate reduce-only orders).
                        //
                        // These used to be gated on futType == "MARKET" while the
                        // take-profit above was ungated, so a LIMIT entry carrying both
                        // legs got its target and silently lost its stop — a live
                        // position, naked, with the loud POSITION UNPROTECTED path never
                        // reached because the attach never ran. Binance accepts
                        // reduce-only protective orders against a resting entry, so the
                        // gate bought nothing and cost the stop.
                        //
                        // What the gate DOES have to express is which field the entry
                        // consumed as its own trigger: `futTrig` above reads
                        // TriggerPrice ?? StopLoss ?? TakeProfit, so on a stop/TP entry
                        // one of those is the entry price, not a protective level, and
                        // attaching it would place an exit exactly at the entry.
                        // Decided by ORDER TYPE alone. The old guards also required TriggerPrice
                        // to be null — but GeneralOrderService.NormaliseTrigger fills BOTH
                        // spellings before any signal reaches a plugin, so with the canonical
                        // field set the guard was false and a SECOND reduce-only stop went out at
                        // the entry's own trigger. Found by the conformance suite on 2026-09-07.
                        bool slIsEntryTrigger = futType is "STOP_MARKET" or "STOP";
                        bool tpIsEntryTrigger = futType is "TAKE_PROFIT_MARKET" or "TAKE_PROFIT";

                        string exitSide = side == "BUY" ? "SELL" : "BUY";
                        if (signal.TakeProfit.HasValue && !tpIsEntryTrigger)
                            await AttachProtectiveOrderAsync(symbol, exitSide, "TAKE_PROFIT_MARKET",
                                signal.Quantity, signal.TakeProfit.Value, DeriveProtectiveOid(signal.ClientOid, "tp"), "take-profit", rules?.Orders);
                        if (signal.StopLoss.HasValue && !slIsEntryTrigger)
                            await AttachProtectiveOrderAsync(symbol, exitSide, "STOP_MARKET",
                                signal.Quantity, signal.StopLoss.Value, DeriveProtectiveOid(signal.ClientOid, "sl"), "stop-loss", rules?.Orders);
                        // A trailing distance is never an entry trigger, so it has no
                        // ambiguity to resolve — it was only ever gated by copy-paste.
                        if (signal.TrailStopValue is > 0)
                            await AttachTrailingStopAsync(symbol, exitSide, signal.Quantity, signal.TrailStopValue.Value, DeriveProtectiveOid(signal.ClientOid, "ts"), rules: rules?.Orders);
                        if (signal.TrailTpValue is > 0)
                            await AttachTrailingStopAsync(symbol, exitSide, signal.Quantity, signal.TrailTpValue.Value, DeriveProtectiveOid(signal.ClientOid, "ttp"), signal.TrailTpActivation, rules?.Orders);

                        return id;
                    }
                    else
                    {
                        var (p, buildError) = BuildSpotOrderParams(signal, symbol, side, rules);
                        if (p is null) return buildError!;

                        _reads.Invalidate();
                        string body = await SignedRequestAsync(HttpMethod.Post, SpotRest, "/api/v3/order", p);
                        _reads.Invalidate();
                        return ParseOrderId(body);
                    }
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Binance order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.Message}"; }
        }

        /// <summary>
        /// Places a futures protective order (TP/SL) with one retry, surfacing a loud
        /// error if both attempts fail — a naked position must be heard about now.
        /// </summary>
        private async Task AttachProtectiveOrderAsync(
            string symbol, string exitSide, string type, double quantity, double triggerPrice, string? clientOrderId, string label,
            SymbolOrderRules? rules = null)
        {
            string? lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var p = new Dictionary<string, string>
                    {
                        ["symbol"] = symbol, ["side"] = exitSide, ["type"] = type,
                        ["quantity"] = rules?.FormatQuantity(quantity) ?? Fmt(quantity),
                        ["stopPrice"] = rules?.FormatPrice(triggerPrice) ?? Fmt(triggerPrice),
                        ["timeInForce"] = "GTC", ["reduceOnly"] = "true"
                    };
                    if (!string.IsNullOrEmpty(clientOrderId)) p["newClientOrderId"] = clientOrderId!;
                    await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/order", p);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    // Duplicate clientOrderId on the retry means the first attempt landed.
                    if (attempt == 1 && lastError.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }
            _errorStream.OnNext(
                $"POSITION UNPROTECTED: {label} order for {symbol} failed to attach after retry " +
                $"({lastError}). The entry order is live without its {label}. " +
                $"Place the {label} manually or close the position.");
        }

        private static string? DeriveProtectiveOid(string? entryOid, string suffix)
        {
            if (string.IsNullOrEmpty(entryOid)) return null;
            string baseOid = entryOid.Length > 32 ? entryOid.Substring(0, 32) : entryOid;
            return $"{baseOid}-{suffix}";
        }

        // Binance futures time-in-force. Post-only maps to GTX; otherwise GTC/IOC/FOK.
        private static string ResolveTif(TradeSignal s) =>
            s.PostOnly ? "GTX"
            : (s.TimeInForce?.ToUpperInvariant() switch { "IOC" => "IOC", "FOK" => "FOK", "GTX" => "GTX", _ => "GTC" });

        // Spot time-in-force (no GTX; spot maker-only is the LIMIT_MAKER order type).
        private static string SpotTif(TradeSignal s) =>
            s.TimeInForce?.ToUpperInvariant() switch { "IOC" => "IOC", "FOK" => "FOK", _ => "GTC" };

        // Futures trailing stop (reduce-only). Binance expresses the trail as a
        // callbackRate percent (0.1–5); TrailStopValue is taken as that percent.
        private async Task AttachTrailingStopAsync(string symbol, string exitSide, double quantity, double callbackRate, string? clientOrderId,
            double? activationPrice = null, SymbolOrderRules? rules = null)
        {
            try
            {
                var p = new Dictionary<string, string>
                {
                    ["symbol"] = symbol, ["side"] = exitSide, ["type"] = "TRAILING_STOP_MARKET",
                    ["quantity"] = rules?.FormatQuantity(quantity) ?? Fmt(quantity), ["callbackRate"] = Fmt(Math.Clamp(callbackRate, 0.1, 5.0)),
                    ["reduceOnly"] = "true"
                };
                if (activationPrice.HasValue) p["activationPrice"] = rules?.FormatPrice(activationPrice.Value) ?? Fmt(activationPrice.Value);
                if (!string.IsNullOrEmpty(clientOrderId)) p["newClientOrderId"] = clientOrderId!;
                await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/order", p);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance trailing-stop attach failed for {symbol}: {ex.Message}");
            }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                _reads.Invalidate();
                return await _orderLimiter.ExecuteAsync(async () =>
                {
                    var p = new Dictionary<string, string> { ["symbol"] = CleanSymbol(symbol), ["orderId"] = orderId };
                    // The cancel contract carries no market and Capabilities
                    // declares FuturesTrading, so try both books: an id the spot
                    // book doesn't know (-2011) may be a futures order. Before
                    // this, a futures order placed through this terminal could
                    // not be cancelled through this terminal.
                    try
                    {
                        await SignedRequestAsync(HttpMethod.Delete, SpotRest, "/api/v3/order", p);
                        return true;
                    }
                    catch (HttpRequestException)
                    {
                        await SignedRequestAsync(HttpMethod.Delete, FutRest, "/fapi/v1/order", p);
                        return true;
                    }
                    finally { _reads.Invalidate(); }
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance CancelOrderAsync failed for {orderId}/{symbol} ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets the futures market's leverage and returns what the venue applied; 1.0 when it
        /// could not be set, said on the error stream (the SDK contract).
        ///
        /// <para>
        /// This used to cut the request to a whole number between 1 and 125 before sending it,
        /// so 2.5 went out as 2 and 150 as 125, and the caller's "did it apply what I asked"
        /// check compared against a number nobody asked for. Binance takes whole numbers only,
        /// up to a limit that differs by market and account; anything else is now refused in
        /// words, and an amount above the market's limit is the venue's to refuse.
        /// </para>
        /// </summary>
        public async Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            if (!IsConnected) return 1.0;
            if (WholeLeverageRefusal(leverage, CleanSymbol(symbol)) is string refusal)
            {
                _errorStream.OnNext(refusal + ".");
                return 1.0;
            }
            await _orderLimiter.WaitAsync();
            var (applied, why) = await TrySetLeverageAsync(CleanSymbol(symbol), (int)leverage);
            if (applied is int a) return a;
            _errorStream.OnNext($"Binance would not set {(int)leverage} times leverage on {CleanSymbol(symbol)}: {why}");
            return 1.0;
        }

        /// <summary>
        /// POST /fapi/v1/leverage. The leverage the venue applied, or null and the venue's
        /// reason — never a made-up 1, which an order at 1 times would read as success.
        /// </summary>
        private async Task<(int? Applied, string? Why)> TrySetLeverageAsync(string wireSymbol, int leverage)
        {
            try
            {
                var p = new Dictionary<string, string> { ["symbol"] = wireSymbol, ["leverage"] = leverage.ToString(CultureInfo.InvariantCulture) };
                string body = await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/leverage", p);
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("leverage", out var l) && l.TryGetInt32(out int applied)
                    ? (applied, null)
                    : (null, "its answer named no leverage");
            }
            catch (Exception ex)
            {
                return (null, VenueSays(ex));
            }
        }

        /// <summary>Why <paramref name="leverage"/> is not one Binance takes, or null.</summary>
        private static string? WholeLeverageRefusal(double leverage, string wireSymbol)
        {
            if (double.IsNaN(leverage) || double.IsInfinity(leverage) || leverage < 1)
                return $"Binance takes leverage of 1 or more; {leverage.ToString("0.##", CultureInfo.InvariantCulture)} times on {wireSymbol} is not one, so nothing was placed";
            if (leverage != Math.Floor(leverage))
                return $"Binance takes whole-number leverage only; {leverage.ToString("0.##", CultureInfo.InvariantCulture)} times on {wireSymbol} "
                     + $"is not one, so nothing was placed. Choose {Math.Floor(leverage):0} or {Math.Ceiling(leverage):0}";
            if (leverage > 1000)
                return $"Binance does not lend at {leverage.ToString("0", CultureInfo.InvariantCulture)} times on {wireSymbol}, so nothing was placed";
            return null;
        }

        /// <summary>
        /// The leverage an opening futures order will be placed at, or why it cannot be.
        ///
        /// <para>
        /// The ticket sends nothing at 1 times (no leverage), so a missing amount is 1. A
        /// fraction is refused rather than cut (Binance takes whole numbers). An amount above
        /// the market's own limit, from the account's leverage brackets, is refused naming the
        /// limit; when the brackets cannot be read the order goes on and the venue's answer to
        /// the change decides.
        /// </para>
        ///
        /// <para>
        /// Binance keeps ONE leverage per market, and changing it changes an open position's
        /// too. Now that every opening order sets it, an order at 1 times — the ticket's
        /// default — against a position opened at 10 would quietly re-margin that position (or
        /// be refused for margin, in the venue's words). So when a position is open on the
        /// market at a different leverage, the order is refused and says which: match it, or
        /// close with reduce-only. When positions cannot be read, the order goes on.
        /// </para>
        /// </summary>
        private async Task<(int Leverage, string? Refusal)> CheckFuturesLeverageAsync(TradeSignal signal, string wireSymbol)
        {
            double asked = signal.Leverage ?? 1.0;
            if (asked < 1) asked = 1.0;
            if (WholeLeverageRefusal(asked, wireSymbol) is string notWhole) return (0, notWhole);
            int want = (int)asked;

            if (await GetSymbolMaxLeverageAsync(wireSymbol) is int max && want > max)
                return (0, $"Binance lends at most {max} times on {wireSymbol}; this order asked for {want} times, so nothing was placed");

            try
            {
                foreach (var pos in await GetPositionsAsync())
                {
                    if (!string.Equals(CleanSymbol(pos.Symbol), wireSymbol, StringComparison.OrdinalIgnoreCase) || pos.Quantity == 0) continue;
                    int held = (int)Math.Round(pos.Leverage);
                    if (held >= 1 && held != want)
                        return (0, $"{wireSymbol} already has a position open at {held} times, and Binance keeps one leverage per market, "
                                 + $"so an order at {want} times would change the open position's leverage. Nothing was placed. "
                                 + $"Set the leverage to {held} to add to it, or tick reduce-only to close it");
                }
            }
            catch (Exception)
            {
                // Positions could not be read: the venue judges the change, as it always did.
            }
            return (want, null);
        }

        /// <summary>The venue's own reason from a failed call: the "msg" of Binance's
        /// {"code":…,"msg":…} body when there is one, else the whole message.</summary>
        private static string VenueSays(Exception ex)
        {
            string m = ex.Message;
            int brace = m.IndexOf('{');
            if (brace >= 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(m[brace..]);
                    if (doc.RootElement.TryGetProperty("msg", out var msg) && msg.GetString() is { Length: > 0 } said)
                        return said;
                }
                catch (JsonException) { }
            }
            return m;
        }

        // ── User-data stream (listen key) REST ────────────────────────────────

        private async Task<string?> CreateListenKeyAsync()
        {
            string body = await KeyOnlyRequestAsync(HttpMethod.Post, "/api/v3/userDataStream", null);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("listenKey", out var lk) ? lk.GetString() : null;
        }

        private Task KeepAliveListenKeyAsync(string listenKey)
            => KeyOnlyRequestAsync(HttpMethod.Put, "/api/v3/userDataStream", listenKey);

        private Task CloseListenKeyAsync(string listenKey)
            => KeyOnlyRequestAsync(HttpMethod.Delete, "/api/v3/userDataStream", listenKey);

        // Futures listen-key lifecycle (fapi): keep-alive and close act on the
        // account's current key, so no key parameter goes on the wire.
        private async Task<string?> CreateFuturesListenKeyAsync()
        {
            string body = await KeyOnlyRequestAsync(HttpMethod.Post, "/fapi/v1/listenKey", null, FutRest);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("listenKey", out var lk) ? lk.GetString() : null;
        }

        private Task KeepAliveFuturesListenKeyAsync()
            => KeyOnlyRequestAsync(HttpMethod.Put, "/fapi/v1/listenKey", null, FutRest);

        private Task CloseFuturesListenKeyAsync()
            => KeyOnlyRequestAsync(HttpMethod.Delete, "/fapi/v1/listenKey", null, FutRest);

        // ── HTTP plumbing ─────────────────────────────────────────────────────

        /// <summary>
        /// A failed response as an exception that still knows its status code.
        ///
        /// <para>These three call sites used to do
        /// <c>throw new HttpRequestException($"{(int)resp.StatusCode} {body}")</c> — the status
        /// in the <i>message</i>, never in the property. <c>TransportFailure.IsTransient</c>
        /// reads <c>if (http.StatusCode is not { } status) return true;</c>, so a Binance
        /// <b>401 or 404 was classified as transient</b>, rethrown out of
        /// <c>FetchOhlcvAsync</c> into the pipeline's retry and circuit breaker, and announced
        /// to the user as a network issue — precisely the outcome TransportFailure's own doc
        /// says must not happen. <c>RateLimiter.ShouldRetry</c> was defeated the same way and
        /// retried a bad key three times.</para>
        ///
        /// <para>The tell that this was the bug and not the design:
        /// <c>ProviderResult.IsPermissionError</c> carries a message-substring fallback for
        /// exactly this habit, so order classification survived while the two retry policies
        /// did not.</para>
        /// </summary>
        private static HttpRequestException HttpFailure(HttpResponseMessage resp, string body)
            // 451 gets a sentence a person can act on, not the venue's legalese:
            // Binance geo-blocks by server location, so the hosted head gets this on
            // every call while the same build works from a desktop in an eligible
            // region — and "451 <json>" read as OUR bug rather than that fact.
            => resp.StatusCode == HttpStatusCode.UnavailableForLegalReasons
                ? new("451 Binance is not reachable from this machine's network location (geo-restricted). "
                    + "The same app works from an eligible region; this server cannot reach Binance at all.",
                    null, resp.StatusCode)
                : new($"{(int)resp.StatusCode} {body}", null, resp.StatusCode);

        private async Task<string> GetPublicAsync(string baseUrl, string path, string? query, CancellationToken ct = default)
        {
            string url = $"{baseUrl}{path}" + (string.IsNullOrEmpty(query) ? "" : $"?{query}");
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw HttpFailure(resp, body);
            return body;
        }

        // ── Venue clock ───────────────────────────────────────────────────────

        /// <summary>
        /// Milliseconds to add to the local clock to get Binance's. Zero until synced.
        ///
        /// <para>Every signed Binance call carries <c>recvWindow=5000</c> and a timestamp taken
        /// from the local clock, and the venue rejects anything outside that window with
        /// <c>-1021 Timestamp for this request is outside of the recvWindow</c>. Nothing in the
        /// plugin tier synced against venue time, so a desktop whose clock had drifted more
        /// than five seconds — a laptop resuming from sleep, a VM with lazy NTP — got -1021 on
        /// <b>every</b> signed call: balances, positions and orders all failed at once. Because
        /// the exception carried no status code (see <see cref="HttpFailure"/>), it was then
        /// announced as a network problem, which is the wrong thing to tell someone whose real
        /// problem is that their clock is wrong.</para>
        /// </summary>
        private long _clockOffsetMs;
        private DateTime _clockSyncedAtUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _clockGate = new(1, 1);

        /// <summary>How long a clock sync is trusted before it is taken again.</summary>
        private static readonly TimeSpan ClockSyncInterval = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Syncs <see cref="_clockOffsetMs"/> against <c>/api/v3/time</c>, at most once per
        /// <see cref="ClockSyncInterval"/> unless <paramref name="force"/> is set. Best effort:
        /// a failure here leaves the offset alone and lets the signed call proceed on the local
        /// clock, which is exactly the old behaviour and no worse.
        /// </summary>
        private async Task EnsureClockSyncedAsync(bool force = false)
        {
            if (!force && DateTime.UtcNow - _clockSyncedAtUtc < ClockSyncInterval) return;

            await _clockGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!force && DateTime.UtcNow - _clockSyncedAtUtc < ClockSyncInterval) return;

                long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string body = await GetPublicAsync(SpotRest, "/api/v3/time", null).ConfigureAwait(false);
                long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("serverTime", out var st)) return;

                // Take the midpoint of the round trip as "local now at the moment the venue
                // stamped its reply", so half the network latency does not land in the offset.
                _clockOffsetMs = st.GetInt64() - (before + (after - before) / 2);
                _clockSyncedAtUtc = DateTime.UtcNow;
            }
            catch
            {
                // Best effort by design — never let a clock probe break a signed call.
            }
            finally
            {
                _clockGate.Release();
            }
        }

        private long VenueNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _clockOffsetMs;

        // SIGNED request: appends timestamp + recvWindow, HMAC-SHA256 signs the
        // query, sends params in the query string with the API-key header.
        private async Task<string> SignedRequestAsync(HttpMethod method, string baseUrl, string path, Dictionary<string, string> p,
            CancellationToken ct = default)
        {
            var (key, secret) = await CheckoutBinanceCredentialsAsync().ConfigureAwait(false);
            await EnsureClockSyncedAsync().ConfigureAwait(false);

            for (int attempt = 0; ; attempt++)
            {
                string query = RestSigning.BuildQuery(p);
                if (query.Length > 0) query += "&";
                query += "recvWindow=5000&timestamp=" + VenueNowMs();
                string signature = RestSigning.HmacSha256Hex(secret, query);

                using var req = new HttpRequestMessage(method, $"{baseUrl}{path}?{query}&signature={signature}");
                req.Headers.Add("X-MBX-APIKEY", key);
                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (resp.IsSuccessStatusCode) return body;

                // -1021 is the venue telling us our clock is wrong. Re-sync against server
                // time and sign once more; a second -1021 is a real failure and is reported.
                // Retrying without re-syncing would just reproduce the same bad timestamp.
                if (attempt == 0 && body.Contains("-1021", StringComparison.Ordinal))
                {
                    await EnsureClockSyncedAsync(force: true).ConfigureAwait(false);
                    continue;
                }

                throw HttpFailure(resp, body);
            }
        }

        // Key-only request (user-data stream lifecycle): API-key header, no signature.
        private async Task<string> KeyOnlyRequestAsync(HttpMethod method, string path, string? listenKey, string? baseUrl = null)
        {
            var (key, _) = await CheckoutBinanceCredentialsAsync().ConfigureAwait(false);
            string url = $"{baseUrl ?? SpotRest}{path}" + (listenKey != null ? $"?listenKey={listenKey}" : "");
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Add("X-MBX-APIKEY", key);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw HttpFailure(resp, body);
            return body;
        }

        // ── WebSocket runner with reconnect ───────────────────────────────────

        // Market-data sockets only (klines, keyed feeds, depth). The user-data
        // streams live on ReconnectingWebSocket via UserDataStream — this loop
        // must never carry a listen-key URL again, because its error path is
        // SPOKEN, and speaking the path once published the listen key (a live
        // 60-minute credential for order and balance events).
        private async Task RunSocketAsync(Uri uri, string label, Action<JsonElement> onMessage, CancellationToken ct)
        {
            // ReconnectingWebSocket owns the transport, replacing the last
            // hand-rolled ClientWebSocket loop in the provider tier. What that
            // loop lacked and this gains: a 10-second connect timeout (a
            // black-holed handshake used to wedge the subscription forever) and
            // exponential reconnect backoff instead of a fixed 2s hammer.
            // Heartbeat is disabled: Binance market streams are URL-addressed,
            // ping at the WebSocket protocol level (auto-ponged by .NET), and a
            // text "ping" frame is not a valid stream message. maxReconnectAttempts
            // is unbounded because market data must never give up while the
            // subscription lives — the semantics of the loop this replaces.
            await using var ws = new ReconnectingWebSocket(
                    uri.ToString(),
                    heartbeatInterval: Timeout.InfiniteTimeSpan,
                    reconnectBaseDelay: TimeSpan.FromSeconds(2),
                    maxReconnectAttempts: int.MaxValue)
                .OnMessage(msg =>
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(msg);
                        onMessage(doc.RootElement);
                    }
                    catch { /* malformed market-data frame — the next one supersedes it */ }
                })
                .OnError(e => _errorStream.OnNext($"Binance {label} socket: {e}"));

            // The initial connect retries here (the SDK socket only self-heals
            // once it has connected at least once); after that, its receive loop
            // owns reconnection and this task just holds the subscription open.
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ws.ConnectAsync(ct).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _errorStream.OnNext($"Binance {label} socket connect failed ({ex.GetType().Name}); retrying.");
                    try { await Task.Delay(2000, ct).ConfigureAwait(false); } catch { return; }
                }
            }
            try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* subscription ended */ }
        }

        /// <summary>
        /// One listen-key user-data socket. <see cref="ReconnectingWebSocket"/>
        /// owns the transport (backoff reconnect, heartbeat staleness watchdog,
        /// 16 MB frame cap); this class owns the LISTEN KEY: keep-alive every
        /// 25 minutes against the 60-minute TTL, and when the keep-alive fails it
        /// creates a NEW key and rebuilds the socket on the new URL — the half the
        /// old hand-rolled loop was missing. Reconnecting to an expired key
        /// "succeeds" and delivers nothing, which is how fills stopped announcing
        /// permanently with the flag still claiming the stream was up.
        /// </summary>
        private sealed class UserDataStream : IAsyncDisposable
        {
            private readonly string _label;
            private readonly Func<Task<string?>> _createKey;
            private readonly Func<string, Task> _keepAlive;
            private readonly Func<string, Task> _closeKey;
            private readonly Func<string, string> _wsUrlForKey;
            private readonly Action<string> _onFrame;
            private readonly Action<string> _onError;
            private readonly CancellationTokenSource _cts = new();
            private ReconnectingWebSocket? _ws;
            private string? _listenKey;
            private volatile bool _keyHealthy;

            public bool IsUp => _keyHealthy && (_ws?.IsConnected ?? false);

            public UserDataStream(string label,
                Func<Task<string?>> createKey, Func<string, Task> keepAlive, Func<string, Task> closeKey,
                Func<string, string> wsUrlForKey, Action<string> onFrame, Action<string> onError)
            {
                _label = label; _createKey = createKey; _keepAlive = keepAlive; _closeKey = closeKey;
                _wsUrlForKey = wsUrlForKey; _onFrame = onFrame; _onError = onError;
            }

            public async Task StartAsync()
            {
                _listenKey = await _createKey().ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Binance returned no listenKey.");
                await ConnectSocketAsync().ConfigureAwait(false);
                _ = Task.Run(KeepAliveLoopAsync);
            }

            private async Task ConnectSocketAsync()
            {
                var old = _ws;
                _ws = new ReconnectingWebSocket(_wsUrlForKey(_listenKey!), heartbeatInterval: TimeSpan.FromSeconds(30))
                    .OnMessage(_onFrame)
                    .OnError(e => _onError($"Binance {_label} user-data socket: {e}"));
                if (old != null) await old.DisposeAsync().ConfigureAwait(false);
                await _ws.ConnectAsync(_cts.Token).ConfigureAwait(false);
                _keyHealthy = true;
            }

            private async Task KeepAliveLoopAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(25), _cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    try { await _keepAlive(_listenKey!).ConfigureAwait(false); }
                    catch
                    {
                        // Key presumed dead: recreate, don't log-and-hope.
                        _keyHealthy = false;
                        try
                        {
                            _listenKey = await _createKey().ConfigureAwait(false)
                                ?? throw new InvalidOperationException("Binance returned no listenKey.");
                            await ConnectSocketAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // IsUp is now false, so SupportsOrderEventStreaming is
                            // false and new orders fall back to the fill poller.
                            _onError($"Binance {_label} user-data listen key could not be renewed ({ex.GetType().Name}); fills for new orders resolve by polling.");
                        }
                    }
                }
            }

            public async ValueTask DisposeAsync()
            {
                _cts.Cancel();
                _keyHealthy = false;
                if (_ws != null) await _ws.DisposeAsync().ConfigureAwait(false);
                if (_listenKey != null)
                {
                    try { await _closeKey(_listenKey).ConfigureAwait(false); }
                    catch { /* venue-side key expires on its own TTL */ }
                }
                _cts.Dispose();
            }
        }

        // ── Small parse/format helpers ────────────────────────────────────────

        private static double Dbl(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var el)) return 0;
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDouble(),
                JsonValueKind.String => double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0,
                _ => 0
            };
        }

        private static double DblAt(JsonElement arr, int i)
        {
            var el = arr[i];
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDouble(),
                JsonValueKind.String => double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0,
                _ => 0
            };
        }

        private static List<OrderBookEntry> ParseLevels(JsonElement levels)
        {
            var list = new List<OrderBookEntry>();
            foreach (var lvl in levels.EnumerateArray())
                list.Add(new OrderBookEntry(DblAt(lvl, 0), DblAt(lvl, 1)));
            return list;
        }

        private static string ParseOrderId(string body)
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("orderId", out var id) ? id.GetRawText() : "ORDER_FAILED:no orderId";
        }

        private static string Fmt(double v) => v.ToString("0.##########", CultureInfo.InvariantCulture);

        // Binance /depth only accepts a fixed set of limits.
        private static int SnapDepth(int limit)
        {
            int[] allowed = { 5, 10, 20, 50, 100, 500, 1000, 5000 };
            foreach (var a in allowed) if (limit <= a) return a;
            return 5000;
        }

        private static OrderType MapOrderType(string type) => type switch
        {
            "LIMIT"             => OrderType.Limit,
            "MARKET"            => OrderType.Market,
            "STOP_LOSS"         => OrderType.StopMarket,
            "STOP_LOSS_LIMIT"   => OrderType.StopLimit,
            "TAKE_PROFIT"       => OrderType.TakeProfitMarket,
            "TAKE_PROFIT_LIMIT" => OrderType.TakeProfitLimit,
            _                   => OrderType.Market
        };

        // Binance kline interval strings == the app's timeframe codes; validate
        // and fall back to 1h on anything unexpected.
        private static string MapInterval(string tf) => tf switch
        {
            "1m" or "3m" or "5m" or "15m" or "30m"
            or "1h" or "2h" or "4h" or "6h" or "8h" or "12h"
            or "1d" or "3d" or "1w" or "1M" => tf,
            _ => "1h"
        };
    }
}
