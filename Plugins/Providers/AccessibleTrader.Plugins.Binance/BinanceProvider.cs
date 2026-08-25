using System.Globalization;
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
    public class BinanceProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider, IOcoTradingProvider
    {
        // ── Endpoints (mainnet / testnet) ─────────────────────────────────────
        private string SpotRest => _isTestnet ? "https://testnet.binance.vision"     : "https://api.binance.com";
        private string FutRest  => _isTestnet ? "https://testnet.binancefuture.com"  : "https://fapi.binance.com";
        private string SpotWs   => _isTestnet ? "wss://testnet.binance.vision/ws/"   : "wss://stream.binance.com:9443/ws/";
        private string FutWs    => _isTestnet ? "wss://stream.binancefuture.com/ws/" : "wss://fstream.binance.com/ws/";

        private HttpClient? _httpField;
        private HttpClient Http => _httpField ??= PluginHostServices.CreateHttpClient(
            "Binance",
            new[] { "api.binance.com", "fapi.binance.com", "testnet.binance.vision", "testnet.binancefuture.com" },
            userAgent: "AccessibleTrader/1.0");

        private string? _apiKey;
        private string? _apiSecret;
        private bool _isTestnet = false;

        // Binance allows 1200 request-weight/minute for REST.
        private readonly RateLimiter _rateLimiter = new(1200, TimeSpan.FromMinutes(1));

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
            if (config.TryGetValue("Testnet",   out var tn))     _isTestnet = bool.TryParse(tn, out var b) && b;
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
            string wsBase = isFutures ? FutWs : SpotWs;
            string interval = MapInterval(timeframe);
            return new Uri($"{wsBase}{CleanSymbol(symbol).ToLowerInvariant()}@kline_{interval}");
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

        // ── ITradingProvider ──────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
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
            });
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
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
            });
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
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
            });
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
            return await _rateLimiter.ExecuteAsync(async () =>
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
            });
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
                return await _rateLimiter.ExecuteOnceAsync(async () =>
                {
                    var p = new Dictionary<string, string>
                    {
                        ["symbol"] = CleanSymbol(symbol),
                        ["side"] = side == OrderSide.Buy ? "BUY" : "SELL",
                        ["quantity"] = Fmt(quantity),
                    };
                    if (side == OrderSide.Sell)
                    {
                        p["aboveType"] = "LIMIT_MAKER";
                        p["abovePrice"] = Fmt(limitPrice);
                        p["belowType"] = "STOP_LOSS";
                        p["belowStopPrice"] = Fmt(stopTriggerPrice);
                    }
                    else
                    {
                        p["aboveType"] = "STOP_LOSS";
                        p["aboveStopPrice"] = Fmt(stopTriggerPrice);
                        p["belowType"] = "LIMIT_MAKER";
                        p["belowPrice"] = Fmt(limitPrice);
                    }

                    string body = await SignedRequestAsync(HttpMethod.Post, SpotRest, "/api/v3/orderList/oco", p);
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

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            bool isFutures = string.Equals(signal.SubType, "Futures", StringComparison.OrdinalIgnoreCase);
            try
            {
                return await _rateLimiter.ExecuteOnceAsync(async () =>
                {
                    var symbol = CleanSymbol(signal.Symbol);
                    string side = signal.Side == OrderSide.Buy ? "BUY" : "SELL";

                    if (isFutures)
                    {
                        // The stream is part of the placement contract: the order
                        // service reads SupportsOrderEventStreaming right after
                        // this returns, and a futures fill arrives ONLY on the
                        // futures user-data stream (or by polling if it is down).
                        await EnsureFuturesUserDataStreamAsync();

                        if (signal.Leverage.HasValue && signal.Leverage.Value > 1)
                            await SetLeverageAsync(symbol, signal.Leverage.Value);

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
                        p["quantity"] = Fmt(signal.Quantity);
                        // reduceOnly and positionSide are mutually exclusive on Binance
                        // (hedge mode rejects reduceOnly); only send reduceOnly in one-way mode.
                        if (signal.ReduceOnly && string.IsNullOrEmpty(signal.PositionSide)) p["reduceOnly"] = "true";
                        if (!string.IsNullOrEmpty(signal.PositionSide)) p["positionSide"] = signal.PositionSide!;
                        if (futType != "MARKET")
                        {
                            if (signal.Price.HasValue) p["price"] = Fmt(signal.Price.Value);
                            bool isStopOrTp = futType is "STOP_MARKET" or "STOP" or "TAKE_PROFIT_MARKET" or "TAKE_PROFIT";
                            double? futTrig = signal.TriggerPrice ?? signal.StopLoss ?? signal.TakeProfit;
                            if (isStopOrTp && futTrig.HasValue) p["stopPrice"] = Fmt(futTrig.Value);
                            p["timeInForce"] = ResolveTif(signal);
                        }
                        if (!string.IsNullOrEmpty(signal.ClientOid)) p["newClientOrderId"] = signal.ClientOid!;

                        string body = await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/order", p);
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
                        bool isStopOrTpEntry = futType is "STOP_MARKET" or "STOP" or "TAKE_PROFIT_MARKET" or "TAKE_PROFIT";
                        bool slIsEntryTrigger = isStopOrTpEntry && signal.TriggerPrice == null && signal.StopLoss.HasValue;
                        bool tpIsEntryTrigger = isStopOrTpEntry && signal.TriggerPrice == null
                                                && !signal.StopLoss.HasValue && signal.TakeProfit.HasValue;

                        string exitSide = side == "BUY" ? "SELL" : "BUY";
                        if (signal.TakeProfit.HasValue && !tpIsEntryTrigger)
                            await AttachProtectiveOrderAsync(symbol, exitSide, "TAKE_PROFIT_MARKET",
                                signal.Quantity, signal.TakeProfit.Value, DeriveProtectiveOid(signal.ClientOid, "tp"), "take-profit");
                        if (signal.StopLoss.HasValue && !slIsEntryTrigger)
                            await AttachProtectiveOrderAsync(symbol, exitSide, "STOP_MARKET",
                                signal.Quantity, signal.StopLoss.Value, DeriveProtectiveOid(signal.ClientOid, "sl"), "stop-loss");
                        // A trailing distance is never an entry trigger, so it has no
                        // ambiguity to resolve — it was only ever gated by copy-paste.
                        if (signal.TrailStopValue is > 0)
                            await AttachTrailingStopAsync(symbol, exitSide, signal.Quantity, signal.TrailStopValue.Value, DeriveProtectiveOid(signal.ClientOid, "ts"));
                        if (signal.TrailTpValue is > 0)
                            await AttachTrailingStopAsync(symbol, exitSide, signal.Quantity, signal.TrailTpValue.Value, DeriveProtectiveOid(signal.ClientOid, "ttp"), signal.TrailTpActivation);

                        return id;
                    }
                    else
                    {
                        var p = new Dictionary<string, string> { ["symbol"] = symbol, ["side"] = side };
                        // Standalone stop/TP order types trigger at TriggerPrice;
                        // fall back to StopLoss/TakeProfit for older callers.
                        double? trig = signal.TriggerPrice
                            ?? (signal.Type is OrderType.StopMarket or OrderType.StopLimit ? signal.StopLoss : signal.TakeProfit);
                        switch (signal.Type)
                        {
                            case OrderType.Market:
                                p["type"] = "MARKET"; p["quantity"] = Fmt(signal.Quantity);
                                break;
                            case OrderType.Limit when signal.Price.HasValue:
                                p["quantity"] = Fmt(signal.Quantity); p["price"] = Fmt(signal.Price.Value);
                                if (signal.PostOnly) { p["type"] = "LIMIT_MAKER"; }                 // spot maker-only
                                else { p["type"] = "LIMIT"; p["timeInForce"] = SpotTif(signal); }
                                break;
                            case OrderType.StopMarket when trig.HasValue:
                                p["type"] = "STOP_LOSS"; p["quantity"] = Fmt(signal.Quantity);
                                p["stopPrice"] = Fmt(trig.Value);
                                break;
                            case OrderType.StopLimit when trig.HasValue && signal.Price.HasValue:
                                p["type"] = "STOP_LOSS_LIMIT"; p["quantity"] = Fmt(signal.Quantity);
                                p["price"] = Fmt(signal.Price.Value); p["stopPrice"] = Fmt(trig.Value);
                                p["timeInForce"] = SpotTif(signal);
                                break;
                            case OrderType.TakeProfitMarket when trig.HasValue:
                                p["type"] = "TAKE_PROFIT"; p["quantity"] = Fmt(signal.Quantity);
                                p["stopPrice"] = Fmt(trig.Value);
                                break;
                            case OrderType.TakeProfitLimit when trig.HasValue && signal.Price.HasValue:
                                p["type"] = "TAKE_PROFIT_LIMIT"; p["quantity"] = Fmt(signal.Quantity);
                                p["price"] = Fmt(signal.Price.Value); p["stopPrice"] = Fmt(trig.Value);
                                p["timeInForce"] = SpotTif(signal);
                                break;
                            default:
                                return "ORDER_FAILED:Unsupported order type";
                        }
                        if (!string.IsNullOrEmpty(signal.ClientOid)) p["newClientOrderId"] = signal.ClientOid!;

                        string body = await SignedRequestAsync(HttpMethod.Post, SpotRest, "/api/v3/order", p);
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
            string symbol, string exitSide, string type, double quantity, double triggerPrice, string? clientOrderId, string label)
        {
            string? lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var p = new Dictionary<string, string>
                    {
                        ["symbol"] = symbol, ["side"] = exitSide, ["type"] = type,
                        ["quantity"] = Fmt(quantity), ["stopPrice"] = Fmt(triggerPrice),
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
        private async Task AttachTrailingStopAsync(string symbol, string exitSide, double quantity, double callbackRate, string? clientOrderId, double? activationPrice = null)
        {
            try
            {
                var p = new Dictionary<string, string>
                {
                    ["symbol"] = symbol, ["side"] = exitSide, ["type"] = "TRAILING_STOP_MARKET",
                    ["quantity"] = Fmt(quantity), ["callbackRate"] = Fmt(Math.Clamp(callbackRate, 0.1, 5.0)),
                    ["reduceOnly"] = "true"
                };
                if (activationPrice.HasValue) p["activationPrice"] = Fmt(activationPrice.Value);
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
                return await _rateLimiter.ExecuteAsync(async () =>
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
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance CancelOrderAsync failed for {orderId}/{symbol} ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        public async Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            if (!IsConnected) return 1.0;
            try
            {
                int lev = (int)Math.Clamp(leverage, 1, MaxLeverage);
                var p = new Dictionary<string, string> { ["symbol"] = CleanSymbol(symbol), ["leverage"] = lev.ToString(CultureInfo.InvariantCulture) };
                string body = await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/leverage", p);
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("leverage", out var l) ? l.GetDouble() : lev;
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance SetLeverageAsync failed for {symbol} ({ex.GetType().Name}): {ex.Message}");
                return 1.0;
            }
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

        private async Task<string> GetPublicAsync(string baseUrl, string path, string? query)
        {
            string url = $"{baseUrl}{path}" + (string.IsNullOrEmpty(query) ? "" : $"?{query}");
            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"{(int)resp.StatusCode} {body}");
            return body;
        }

        // SIGNED request: appends timestamp + recvWindow, HMAC-SHA256 signs the
        // query, sends params in the query string with the API-key header.
        private async Task<string> SignedRequestAsync(HttpMethod method, string baseUrl, string path, Dictionary<string, string> p)
        {
            var (key, secret) = await CheckoutBinanceCredentialsAsync().ConfigureAwait(false);

            string query = RestSigning.BuildQuery(p);
            if (query.Length > 0) query += "&";
            query += "recvWindow=5000&timestamp=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string signature = RestSigning.HmacSha256Hex(secret, query);

            using var req = new HttpRequestMessage(method, $"{baseUrl}{path}?{query}&signature={signature}");
            req.Headers.Add("X-MBX-APIKEY", key);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"{(int)resp.StatusCode} {body}");
            return body;
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
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"{(int)resp.StatusCode} {body}");
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
