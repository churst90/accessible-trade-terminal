using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Mexc
{
    /// <summary>
    /// MEXC provider — DIRECT REST + WebSocket, no JK.Mexc.Net / CryptoExchange.Net.
    /// Spot REST is Binance-style (HMAC query-string, X-MEXC-APIKEY); the spot WS is
    /// Protobuf (MEXC discontinued the JSON spot WS on 2025-08-04) decoded via the
    /// generated <see cref="MexcProtobuf"/> helper. Futures REST/WS are JSON.
    /// </summary>
    public class MexcProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider
    {
        private const string SpotWsUrl    = "wss://wbs-api.mexc.com/ws";
        private const string FuturesWsUrl = "wss://contract.mexc.com/edge";
        private const string SpotPing     = "{\"method\":\"PING\"}";
        private const string FuturesPing  = "{\"method\":\"ping\"}";

        private readonly HttpClient _http;
        private readonly MexcRestApi _rest;

        private ReconnectingWebSocket? _focusedWs;   // legacy focused-chart live stream
        private ReconnectingWebSocket? _orderBookWs;
        private ReconnectingWebSocket? _privateWs;    // user-data (order updates)
        private string? _currentSymbol;
        private string? _currentTimeframe;
        private bool _connected;

        private string? _apiKey;
        private string? _apiSecret;

        // MEXC spot: 500 requests / 10s per IP. Conservative 1000/min leaves headroom.
        private readonly RateLimiter _rateLimiter = new(1000, TimeSpan.FromMinutes(1));

        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();
        private string? _orderBookSymbol;

        private string? _listenKey;
        private System.Timers.Timer? _keepAliveTimer;

        public override string Name => "MEXC";
        public override string Description => "Live MEXC Exchange Data (Spot & Futures). Not officially available in the US — review provider ToS before use.";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => true;
        // Kline updates re-send the current candle with CUMULATIVE volume-so-far;
        // consolidation must diff, not accumulate (see LiveTickStyle docs).
        public override AccessibleTrader.Sdk.Plugins.LiveTickStyle LiveTickStyle => AccessibleTrader.Sdk.Plugins.LiveTickStyle.CumulativeBars;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 500;
        /// <summary>
        /// Leverage here is futures-only — there is no spot margin, so
        /// <c>MarginTrading</c> is absent while <c>FuturesTrading</c> is present.
        /// That combination reads like a contradiction and is not one, which an
        /// earlier draft of the audit got wrong before the probe corrected it.
        /// <c>IsolatedMargin</c> is added on evidence: the futures order body sets
        /// <c>openType</c> from <c>signal.MarginType</c>. <c>ReduceOnly</c>
        /// likewise: <see cref="MapFuturesSide"/> reads <c>signal.ReduceOnly</c>
        /// to pick the close-side wire codes (2/4) instead of opening the
        /// opposite position.
        /// </summary>
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.Leverage | ProviderCapabilities.FuturesTrading |
            ProviderCapabilities.IsolatedMargin | ProviderCapabilities.ReduceOnly;

        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 200.0;

        // Reflects a live connection AND available credentials — not mere credential
        // presence. A connected-but-silent socket is surfaced by the quiet-stream watchdog.
        public bool IsConnected =>
            _connected && (!string.IsNullOrEmpty(_apiKey) || PluginHostServices.ApiKeys != null);

        // Honest: order events only stream while the private WS is actually up. When
        // it isn't (no creds, auth rejected, dropped), the order service falls back to
        // polling so fills still announce.
        public bool SupportsOrderEventStreaming => _privateWs?.IsConnected ?? false;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour, StandardTimeframes.FourHours,
            StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public MexcProvider()
        {
            _http = PluginHostServices.CreateHttpClient(
                providerId:   "MEXC",
                allowedHosts: new[] { "api.mexc.com", "contract.mexc.com" });
            _rest = new MexcRestApi(_http);
        }

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
        }

        private async Task<(string Key, string Secret)> CheckoutMexcCredentialsAsync()
        {
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("MEXC").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("MEXC: no active API key configured.");
                return (checkout.Key, checkout.Secret);
            }
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                throw new InvalidOperationException("MEXC: no API credentials configured.");
            return (_apiKey!, _apiSecret!);
        }

        private bool HasCredentials => !string.IsNullOrEmpty(_apiKey) || PluginHostServices.ApiKeys != null;

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!HasCredentials) return (true, "No API key provided (public data only)");
            try
            {
                var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                var body = await _rest.SpotSignedAsync(HttpMethod.Get, "/api/v3/account", key, secret).ConfigureAwait(false);
                var json = JObject.Parse(body);
                if (json["code"] != null && json["code"]!.Value<int>() != 0)
                    return (false, $"Key validation failed: {json["msg"]}");
                return (true, "API key validated successfully");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        // ── Connection & user-data stream ────────────────────────────────────

        public override async Task EnsureConnectedAsync()
        {
            if (_connected) return;
            _connected = true;
            _connectionStateStream.OnNext(ConnectionState.Connected);

            if (HasCredentials)
            {
                try { await StartUserDataStreamAsync().ConfigureAwait(false); }
                catch { /* public-data-only mode */ }
            }
        }

        private async Task StartUserDataStreamAsync()
        {
            try
            {
                var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                var body = await _rest.SpotSignedAsync(HttpMethod.Post, "/api/v3/userDataStream", key, secret).ConfigureAwait(false);
                _listenKey = JObject.Parse(body)["listenKey"]?.ToString();
                if (string.IsNullOrEmpty(_listenKey)) return;

                // Private push frames are Protobuf on the same spot WS host; the listenKey
                // authenticates the connection (passed on the URL). Subscribe to the
                // order + deal private channels.
                _privateWs = new ReconnectingWebSocket($"{SpotWsUrl}?listenKey={_listenKey}")
                    .WithHeartbeatMessage(SpotPing)
                    .OnConnected(async ws =>
                    {
                        await ws.SendAsync(SubscribeMsg("spot@private.orders.v3.api.pb")).ConfigureAwait(false);
                        await ws.SendAsync(SubscribeMsg("spot@private.deals.v3.api.pb")).ConfigureAwait(false);
                    })
                    .OnBinary(HandlePrivateFrame)
                    .OnError(err => SurfaceError($"MEXC user-data stream: {err}"));
                await _privateWs.ConnectAsync().ConfigureAwait(false);

                // listenKey TTL is 60 min; refresh at 30.
                _keepAliveTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30).TotalMilliseconds) { AutoReset = true };
                _keepAliveTimer.Elapsed += async (_, _) =>
                {
                    try
                    {
                        var (k, s) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                        await _rest.SpotSignedAsync(HttpMethod.Put, "/api/v3/userDataStream", k, s,
                            new Dictionary<string, string> { ["listenKey"] = _listenKey! }).ConfigureAwait(false);
                    }
                    catch (Exception ex) { SurfaceError($"MEXC user-data keep-alive failed: {ex.GetType().Name}"); }
                };
                _keepAliveTimer.Start();
            }
            catch (Exception ex)
            {
                SurfaceError($"MEXC user-data stream unavailable ({ex.GetType().Name}): order-fill updates won't be delivered.");
            }
        }

        private void HandlePrivateFrame(byte[] data)
        {
            var wrapper = MexcProtobuf.TryParse(data);
            if (wrapper == null || wrapper.BodyCase != global::PushDataV3ApiWrapper.BodyOneofCase.PrivateOrders) return;
            var update = MexcProtobuf.MapPrivateOrder(wrapper.PrivateOrders, wrapper.Symbol ?? _currentSymbol ?? string.Empty);
            _orderUpdateSubject.OnNext(update);
        }

        // ── Keyed-feed subscriptions (multi-live) ────────────────────────────

        public override bool SupportsMultipleLiveSubscriptions => true;

        public override async Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
        {
            var cleanSymbol = CleanSymbol(symbol);
            var consolidator = new BarBucketConsolidator(timeframe, LiveTickStyle.CumulativeBars);
            bool isFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);

            void Emit(Ohlcv raw)
            {
                if (IsEmptyBar(raw)) return;
                var bar = consolidator.Apply(raw);
                if (bar.HasValue) onBar(bar.Value);
            }

            ReconnectingWebSocket ws = isFutures
                ? BuildFuturesKlineSocket(ToFuturesSymbol(cleanSymbol), timeframe, Emit)
                : BuildSpotKlineSocket(cleanSymbol, timeframe, Emit);
            await ws.ConnectAsync().ConfigureAwait(false);
            return ws;
        }

        private ReconnectingWebSocket BuildSpotKlineSocket(string cleanSymbol, string timeframe, Action<Ohlcv> emit)
        {
            string channel = $"spot@public.kline.v3.api.pb@{cleanSymbol}@{MexcRestApi.SpotWsInterval(timeframe)}";
            return new ReconnectingWebSocket(SpotWsUrl)
                .WithHeartbeatMessage(SpotPing)
                .OnConnected(async ws => await ws.SendAsync(SubscribeMsg(channel)).ConfigureAwait(false))
                .OnBinary(data =>
                {
                    var w = MexcProtobuf.TryParse(data);
                    if (w != null && MexcProtobuf.TryReadKline(w, out var bar)) emit(bar);
                })
                .OnMessage(text => SurfaceSubscribeError(text, "spot kline"))
                .OnError(err => SurfaceError($"MEXC spot kline stream ({cleanSymbol}): {err}"));
        }

        private ReconnectingWebSocket BuildFuturesKlineSocket(string futuresSymbol, string timeframe, Action<Ohlcv> emit)
        {
            string sub = new JObject
            {
                ["method"] = "sub.kline",
                ["param"] = new JObject { ["symbol"] = futuresSymbol, ["interval"] = MexcRestApi.FuturesInterval(timeframe) },
            }.ToString(Newtonsoft.Json.Formatting.None);

            return new ReconnectingWebSocket(FuturesWsUrl)
                .WithHeartbeatMessage(FuturesPing)
                .OnConnected(async ws => await ws.SendAsync(sub).ConfigureAwait(false))
                .OnMessage(text =>
                {
                    try
                    {
                        var json = JObject.Parse(text);
                        if (json["channel"]?.ToString() != "push.kline") return;
                        var d = json["data"];
                        if (d == null) return;
                        var raw = new Ohlcv(
                            DateTimeOffset.FromUnixTimeSeconds(d["t"]?.Value<long>() ?? 0).UtcDateTime,
                            d["o"]?.Value<double>() ?? 0, d["h"]?.Value<double>() ?? 0,
                            d["l"]?.Value<double>() ?? 0, d["c"]?.Value<double>() ?? 0,
                            d["q"]?.Value<double>() ?? 0);
                        emit(raw);
                    }
                    catch { /* malformed frame */ }
                })
                .OnError(err => SurfaceError($"MEXC futures kline stream ({futuresSymbol}): {err}"));
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);
            var cleanSymbol = CleanSymbol(symbol);
            if (_currentSymbol == cleanSymbol && _currentTimeframe == timeframe && _focusedWs != null) return;

            if (_focusedWs != null) { await _focusedWs.DisconnectAsync().ConfigureAwait(false); _focusedWs.Dispose(); _focusedWs = null; }

            _currentSymbol = cleanSymbol;
            _currentTimeframe = timeframe;
            bool isFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);

            // Focused path pushes RAW bars; LiveStreamManager consolidates with LiveTickStyle.
            _focusedWs = isFutures
                ? BuildFuturesKlineSocket(ToFuturesSymbol(cleanSymbol), timeframe, raw => { if (!IsEmptyBar(raw)) _liveStream.OnNext(raw); })
                : BuildSpotKlineSocket(cleanSymbol, timeframe, raw => { if (!IsEmptyBar(raw)) _liveStream.OnNext(raw); });
            await _focusedWs.ConnectAsync().ConfigureAwait(false);
        }

        public override async Task DisconnectAsync()
        {
            _keepAliveTimer?.Stop();
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;

            await SafeCloseAsync(_focusedWs).ConfigureAwait(false);   _focusedWs = null;
            await SafeCloseAsync(_orderBookWs).ConfigureAwait(false); _orderBookWs = null; _orderBookSymbol = null;
            await SafeCloseAsync(_privateWs).ConfigureAwait(false);   _privateWs = null;

            if (!string.IsNullOrEmpty(_listenKey))
            {
                try
                {
                    var (k, s) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                    await _rest.SpotSignedAsync(HttpMethod.Delete, "/api/v3/userDataStream", k, s,
                        new Dictionary<string, string> { ["listenKey"] = _listenKey! }).ConfigureAwait(false);
                }
                catch { /* best-effort */ }
            }
            _listenKey = null;
            _connected = false;
            _currentSymbol = null;
            _currentTimeframe = null;

            ScrubCredentials(() => _apiKey = null, () => _apiSecret = null);
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        private static async Task SafeCloseAsync(ReconnectingWebSocket? ws)
        {
            if (ws == null) return;
            try { await ws.DisconnectAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            ws.Dispose();
        }

        // ── Data discovery & fetching ────────────────────────────────────────

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Spot", "Futures" });

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    if (subType.Equals("Futures", StringComparison.OrdinalIgnoreCase))
                    {
                        var body = await _rest.FuturesGetAsync("/api/v1/contract/detail").ConfigureAwait(false);
                        var arr = JObject.Parse(body)["data"] as JArray;
                        return arr?.Select(s => s["symbol"]?.ToString() ?? "").Where(s => s.Length > 0).OrderBy(s => s).ToList()
                               ?? new List<string>();
                    }
                    else
                    {
                        var body = await _rest.SpotGetAsync("/api/v3/exchangeInfo").ConfigureAwait(false);
                        var arr = JObject.Parse(body)["symbols"] as JArray;
                        return arr?.Select(s => s["symbol"]?.ToString() ?? "").Where(s => s.Length > 0).OrderBy(s => s).ToList()
                               ?? new List<string>();
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SurfaceError($"MEXC GetAvailableSymbolsAsync failed ({ex.GetType().Name}).");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "30m", "1h", "4h", "1d", "1w", "1M" });

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var cleanSymbol = CleanSymbol(request.Symbol);
            bool isFutures = request.Market.Contains("Futures", StringComparison.OrdinalIgnoreCase);
            int limit = Math.Min(request.Limit, 500);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                    isFutures
                        ? await FetchFuturesKlinesAsync(cleanSymbol, request, limit).ConfigureAwait(false)
                        : await FetchSpotKlinesAsync(cleanSymbol, request, limit).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SurfaceError($"MEXC FetchOhlcvAsync failed for {request.Symbol} ({ex.GetType().Name}): {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchSpotKlinesAsync(string symbol, MarketDataRequest request, int limit)
        {
            var q = new Dictionary<string, string>
            {
                ["symbol"] = symbol,
                ["interval"] = MexcRestApi.SpotRestInterval(request.Timeframe),
                ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            };
            if (request.Since.HasValue) q["startTime"] = request.Since.Value.ToString(CultureInfo.InvariantCulture);
            if (request.Until.HasValue) q["endTime"]   = request.Until.Value.ToString(CultureInfo.InvariantCulture);

            var body = await _rest.SpotGetAsync("/api/v3/klines", q).ConfigureAwait(false);
            var token = JToken.Parse(body);
            if (token is not JArray arr)
            {
                SurfaceError($"MEXC data unavailable for {request.Symbol}: {token["msg"]?.ToString() ?? "no data"}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
            // [openTime(ms), open, high, low, close, volume, closeTime, quoteVolume]
            var bars = arr.Select(k => new Ohlcv(
                DateTimeOffset.FromUnixTimeMilliseconds(k[0]!.Value<long>()).UtcDateTime,
                ParseD(k[1]?.ToString()), ParseD(k[2]?.ToString()), ParseD(k[3]?.ToString()),
                ParseD(k[4]?.ToString()), ParseD(k[5]?.ToString())))
                .Where(x => x.Open > 0 && x.High > 0 && x.Low > 0 && x.Close > 0)
                .OrderBy(x => x.Date).ToList();
            return (bars, bars.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
        }

        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchFuturesKlinesAsync(string spotSymbol, MarketDataRequest request, int limit)
        {
            var futuresSymbol = ToFuturesSymbol(spotSymbol);
            var q = new Dictionary<string, string> { ["interval"] = MexcRestApi.FuturesInterval(request.Timeframe) };
            if (request.Since.HasValue) q["start"] = (request.Since.Value / 1000).ToString(CultureInfo.InvariantCulture);
            if (request.Until.HasValue) q["end"]   = (request.Until.Value / 1000).ToString(CultureInfo.InvariantCulture);

            var body = await _rest.FuturesGetAsync($"/api/v1/contract/kline/{futuresSymbol}", q).ConfigureAwait(false);
            var data = JObject.Parse(body)["data"];
            var times = data?["time"] as JArray;
            if (times == null || times.Count == 0)
            {
                SurfaceError($"MEXC futures data unavailable for {request.Symbol}.");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
            // Columnar: data.time/open/close/high/low/vol (seconds).
            var open = data!["open"] as JArray; var close = data["close"] as JArray;
            var high = data["high"] as JArray;  var low = data["low"] as JArray;
            var vol  = data["vol"] as JArray;
            var bars = new List<Ohlcv>(times.Count);
            for (int i = 0; i < times.Count; i++)
            {
                bars.Add(new Ohlcv(
                    DateTimeOffset.FromUnixTimeSeconds(times[i]!.Value<long>()).UtcDateTime,
                    open?[i]?.Value<double>() ?? 0, high?[i]?.Value<double>() ?? 0,
                    low?[i]?.Value<double>() ?? 0, close?[i]?.Value<double>() ?? 0,
                    vol?[i]?.Value<double>() ?? 0));
            }
            bars = bars.Where(x => x.Open > 0).OrderBy(x => x.Date).ToList();
            return (bars, bars.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
        }

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            var cleanSymbol = CleanSymbol(symbol);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var body = await _rest.SpotGetAsync("/api/v3/depth",
                        new Dictionary<string, string> { ["symbol"] = cleanSymbol, ["limit"] = limit.ToString(CultureInfo.InvariantCulture) }).ConfigureAwait(false);
                    var json = JObject.Parse(body);
                    return (ParseLevels(json["bids"] as JArray, limit), ParseLevels(json["asks"] as JArray, limit));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SurfaceError($"MEXC GetOrderBookAsync failed for {symbol} ({ex.GetType().Name}).");
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        private static List<OrderBookEntry> ParseLevels(JArray? levels, int limit) =>
            levels?.Take(limit).Select(l => new OrderBookEntry(ParseD(l[0]?.ToString()), ParseD(l[1]?.ToString()))).ToList()
            ?? new List<OrderBookEntry>();

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol)
        {
            var cleanSymbol = CleanSymbol(symbol);
            if (_orderBookSymbol == cleanSymbol && _orderBookWs != null) return _orderBookSubject.AsObservable();

            _ = SafeCloseAsync(_orderBookWs);
            _orderBookSymbol = cleanSymbol;
            string channel = $"spot@public.limit.depth.v3.api.pb@{cleanSymbol}@20";

            _orderBookWs = new ReconnectingWebSocket(SpotWsUrl)
                .WithHeartbeatMessage(SpotPing)
                .OnConnected(async ws => await ws.SendAsync(SubscribeMsg(channel)).ConfigureAwait(false))
                .OnBinary(data =>
                {
                    var w = MexcProtobuf.TryParse(data);
                    if (w == null || w.BodyCase != global::PushDataV3ApiWrapper.BodyOneofCase.PublicLimitDepths) return;
                    var d = w.PublicLimitDepths;
                    var bids = d.Bids.Select(b => new OrderBookEntry(ParseD(b.Price), ParseD(b.Quantity))).ToList();
                    var asks = d.Asks.Select(a => new OrderBookEntry(ParseD(a.Price), ParseD(a.Quantity))).ToList();
                    _orderBookSubject.OnNext(new OrderBookUpdate(cleanSymbol, bids, asks, 0, DateTime.UtcNow));
                })
                .OnMessage(text => SurfaceSubscribeError(text, "order book"))
                .OnError(err => SurfaceError($"MEXC order book stream ({cleanSymbol}): {err}"));
            _ = _orderBookWs.ConnectAsync();
            return _orderBookSubject.AsObservable();
        }

        // ── ITradingProvider ────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                var body = await _rest.SpotSignedAsync(HttpMethod.Get, "/api/v3/account", key, secret).ConfigureAwait(false);
                var balances = JObject.Parse(body)["balances"] as JArray;
                if (balances == null) return new List<Balance>();
                return balances.Select(b => new Balance(
                        b["asset"]?.ToString() ?? "", ParseD(b["free"]?.ToString()), ParseD(b["locked"]?.ToString())))
                    .Where(b => b.Free > 0 || b.Locked > 0).ToList();
            }).ConfigureAwait(false);
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
                var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                var body = await _rest.FuturesSignedAsync(HttpMethod.Get, "/api/v1/private/position/open_positions", key, secret).ConfigureAwait(false);
                var arr = JObject.Parse(body)["data"] as JArray;
                if (arr == null) return new List<Position>();
                return arr.Where(p => (p["holdVol"]?.Value<double>() ?? 0) != 0).Select(p =>
                {
                    // positionType: 1 long, 2 short.
                    double qty = (p["holdVol"]?.Value<double>() ?? 0) * ((p["positionType"]?.Value<int>() ?? 1) == 2 ? -1 : 1);
                    double avg = p["holdAvgPrice"]?.Value<double>() ?? 0;
                    double pnl = p["unrealised"]?.Value<double>() ?? p["realised"]?.Value<double>() ?? 0;
                    double liq = p["liquidatePrice"]?.Value<double>() ?? 0;
                    double lev = p["leverage"]?.Value<double>() ?? 1;
                    return new Position(p["symbol"]?.ToString() ?? "", qty, avg, Math.Abs(qty) * avg, pnl, lev, liq);
                }).ToList();
            }).ConfigureAwait(false);
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            var orders = new List<OpenOrder>();

            // No catch on either leg: a failed read must throw so the order service
            // can classify it (ProviderResult.FromException). Swallowing here is
            // what re-armed the reconciliation incident ProviderResult.cs documents.
            if (!string.IsNullOrEmpty(symbol) && !symbol.Contains('_'))
            {
                var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                var body = await _rateLimiter.ExecuteAsync(() => _rest.SpotSignedAsync(HttpMethod.Get, "/api/v3/openOrders", key, secret,
                    new Dictionary<string, string> { ["symbol"] = CleanSymbol(symbol) })).ConfigureAwait(false);
                if (JToken.Parse(body) is JArray arr)
                    orders.AddRange(arr.Select(o => new OpenOrder(
                        o["orderId"]?.ToString() ?? "", o["symbol"]?.ToString() ?? "",
                        (o["side"]?.ToString() ?? "BUY").Equals("BUY", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell,
                        MapSpotType(o["type"]?.ToString()),
                        ParseD(o["origQty"]?.ToString()), ParseD(o["price"]?.ToString()), o["status"]?.ToString() ?? "")));
                else if (JObject.Parse(body)["code"]?.Value<int>() is int c && c != 0)
                    throw new InvalidOperationException($"MEXC open orders unavailable for {symbol}: {JObject.Parse(body)["msg"]}");
            }

            if (string.IsNullOrEmpty(symbol) || symbol.Contains('_'))
            {
                var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                string want = string.IsNullOrEmpty(symbol) ? "" : ToFuturesSymbol(CleanSymbol(symbol));
                var path = string.IsNullOrEmpty(want) ? "/api/v1/private/order/list/open_orders" : $"/api/v1/private/order/list/open_orders/{want}";
                var body = await _rateLimiter.ExecuteAsync(() => _rest.FuturesSignedAsync(HttpMethod.Get, path, key, secret)).ConfigureAwait(false);
                // A spot-only key gets an error BODY here (success=false), not an
                // exception — FuturesSignedAsync never throws on HTTP status. That
                // legitimate no-futures-access case stays quiet by body shape;
                // transport failures and garbage bodies now propagate instead of
                // being swallowed into "no futures orders".
                if (JObject.Parse(body)["data"] is JArray arr)
                    orders.AddRange(arr.Select(o => new OpenOrder(
                        o["orderId"]?.ToString() ?? "", o["symbol"]?.ToString() ?? "",
                        MapFuturesSideToOrderSide(o["side"]?.Value<int>() ?? 1),
                        (o["orderType"]?.Value<int>() ?? 5) == 5 ? OrderType.Market : OrderType.Limit,
                        o["vol"]?.Value<double>() ?? 0, o["price"]?.Value<double>() ?? 0, o["state"]?.ToString() ?? "")));
            }
            return orders;
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            bool isFutures = string.Equals(signal.SubType, "Futures", StringComparison.OrdinalIgnoreCase);
            try
            {
                return await _rateLimiter.ExecuteOnceAsync(async () =>
                    isFutures ? await PlaceFuturesOrderAsync(signal).ConfigureAwait(false)
                              : await PlaceSpotOrderAsync(signal).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (Exception ex) { SurfaceError($"MEXC order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        // Only these two reach the wire mapping below (1 limit / 5 market). The
        // stop and take-profit families need MEXC's plan-order endpoint, which
        // this provider does not call — sending them as type 5 executed the
        // "protective" stop immediately at market.
        internal static bool IsSupportedFuturesEntryType(OrderType type) =>
            type is OrderType.Market or OrderType.Limit;

        // MEXC futures order sides. `Buy ? 1 : 3` ignored ReduceOnly, so a
        // sell-to-close OPENED an opposing short in hedge mode instead of closing
        // the long. A reduce-only buy closes a short (2); a reduce-only sell
        // closes a long (4).
        internal static int MapFuturesSide(OrderSide side, bool reduceOnly) =>
            side == OrderSide.Buy ? (reduceOnly ? 2 : 1) : (reduceOnly ? 4 : 3);

        // The read direction of the same table: sides 1 (open long) and 2 (close
        // short) are buys; 3 (open short) and 4 (close long) are sells. The old
        // read mapped {1,4} to Buy, so a resting close-long showed as a buy.
        internal static OrderSide MapFuturesSideToOrderSide(int side) =>
            side is 1 or 2 ? OrderSide.Buy : OrderSide.Sell;

        private async Task<string> PlaceSpotOrderAsync(TradeSignal signal)
        {
            if (signal.Type is not (OrderType.Market or OrderType.Limit))
                return "ORDER_FAILED:MEXC spot only supports Market/Limit entries. Stop and take-profit "
                     + "levels attach to a Futures entry as protective legs.";
            if (signal.Type == OrderType.Limit && !signal.Price.HasValue)
                return "ORDER_FAILED:Limit order requires Price.";

            var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
            var p = new Dictionary<string, string>
            {
                ["symbol"] = CleanSymbol(signal.Symbol),
                ["side"]   = signal.Side == OrderSide.Buy ? "BUY" : "SELL",
                ["type"]   = signal.Type == OrderType.Market ? "MARKET" : "LIMIT",
                ["quantity"] = signal.Quantity.ToString(CultureInfo.InvariantCulture),
            };
            if (signal.Type == OrderType.Limit) p["price"] = signal.Price!.Value.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(signal.ClientOid)) p["newClientOrderId"] = signal.ClientOid;

            var body = await _rest.SpotSignedAsync(HttpMethod.Post, "/api/v3/order", key, secret, p).ConfigureAwait(false);
            var json = JObject.Parse(body);
            if (json["code"] != null && json["code"]!.Value<int>() != 0) return $"ORDER_FAILED:{json["msg"]}";
            return json["orderId"]?.ToString() ?? "ORDER_SUBMITTED";
        }

        private async Task<string> PlaceFuturesOrderAsync(TradeSignal signal)
        {
            // A stop or take-profit order type needs MEXC's separate plan-order
            // (trigger) endpoint, which this provider does not call. The old code
            // sent StopMarket as type 5 — a plain market order, executed NOW: a
            // protective stop at 90,000 with the mark at 100,000 flattened the
            // position at 100,000 and reported success. Refusing is the honest
            // failure; the trigger levels DO work as protective legs attached to
            // a Market/Limit entry (stopLossPrice/takeProfitPrice below).
            if (!IsSupportedFuturesEntryType(signal.Type))
                return "ORDER_FAILED:MEXC futures does not support stop or take-profit order types yet. "
                     + "Place a Market or Limit entry and set Stop Loss / Take Profit on it — those attach "
                     + "as protective levels.";
            if (signal.Type == OrderType.Limit && !signal.Price.HasValue)
                return "ORDER_FAILED:Limit order requires Price.";

            var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
            // side: 1 open long, 2 close short, 3 open short, 4 close long. type: 1 limit, 5 market. openType: 1 iso, 2 cross.
            var order = new JObject
            {
                ["symbol"] = ToFuturesSymbol(CleanSymbol(signal.Symbol)),
                ["side"]   = MapFuturesSide(signal.Side, signal.ReduceOnly),
                ["type"]   = signal.Type == OrderType.Limit ? 1 : 5,
                ["vol"]    = signal.Quantity,
                ["openType"] = string.Equals(signal.MarginType, "Cross", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            };
            if (signal.Price.HasValue) order["price"] = signal.Price.Value;
            if (signal.Leverage is > 1) order["leverage"] = (int)Math.Clamp(signal.Leverage.Value, 1, MaxLeverage);
            if (signal.StopLoss.HasValue) order["stopLossPrice"] = signal.StopLoss.Value;
            if (signal.TakeProfit.HasValue) order["takeProfitPrice"] = signal.TakeProfit.Value;
            if (!string.IsNullOrEmpty(signal.ClientOid)) order["externalOid"] = signal.ClientOid;

            var body = await _rest.FuturesSignedAsync(HttpMethod.Post, "/api/v1/private/order/submit", key, secret,
                jsonBody: order.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
            var json = JObject.Parse(body);
            if (json["success"]?.Value<bool>() == true) return json["data"]?.ToString() ?? "ORDER_SUBMITTED";
            return $"ORDER_FAILED:{json["message"] ?? json["msg"] ?? "futures order rejected"}";
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                // Spot first.
                var spot = await _rest.SpotSignedAsync(HttpMethod.Delete, "/api/v3/order", key, secret,
                    new Dictionary<string, string> { ["symbol"] = CleanSymbol(symbol), ["orderId"] = orderId }).ConfigureAwait(false);
                var sj = JObject.Parse(spot);
                if (sj["code"] == null || sj["code"]!.Value<int>() == 0) return true;

                // Fall back to futures cancel (JSON array of ids).
                var fut = await _rest.FuturesSignedAsync(HttpMethod.Post, "/api/v1/private/order/cancel", key, secret,
                    jsonBody: new JArray { orderId }.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
                return JObject.Parse(fut)["success"]?.Value<bool>() == true;
            }
            catch (Exception ex) { SurfaceError($"MEXC CancelOrderAsync failed for {orderId} ({ex.GetType().Name})."); return false; }
        }

        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            if (!IsConnected || string.IsNullOrEmpty(symbol)) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
            var body = await _rateLimiter.ExecuteAsync(() => _rest.SpotSignedAsync(HttpMethod.Get, "/api/v3/myTrades", key, secret,
                new Dictionary<string, string> { ["symbol"] = CleanSymbol(symbol), ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture) })).ConfigureAwait(false);
            if (JToken.Parse(body) is not JArray arr) return new();
            return arr.Select(t => new TradeFill(
                    t["id"]?.ToString() ?? "", t["symbol"]?.ToString() ?? "",
                    (t["isBuyer"]?.Value<bool>() ?? true) ? OrderSide.Buy : OrderSide.Sell,
                    ParseD(t["qty"]?.ToString()), ParseD(t["price"]?.ToString()),
                    DateTimeOffset.FromUnixTimeMilliseconds(t["time"]?.Value<long>() ?? 0).UtcDateTime,
                    ParseD(t["commission"]?.ToString()), t["orderId"]?.ToString()))
                .OrderByDescending(f => f.FilledAt).Take(limit).ToList();
        }

        public async Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            if (!IsConnected) return 1.0;
            try
            {
                var (key, secret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
                int lev = (int)Math.Clamp(leverage, 1, MaxLeverage);
                var b = new JObject { ["symbol"] = ToFuturesSymbol(CleanSymbol(symbol)), ["leverage"] = lev, ["openType"] = 1 };
                var body = await _rest.FuturesSignedAsync(HttpMethod.Post, "/api/v1/private/position/change_leverage", key, secret,
                    jsonBody: b.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
                if (JObject.Parse(body)["success"]?.Value<bool>() == true) return lev;

                // The exchange refused. 1.0 is the contract's "could not set it" answer
                // (GeneralOrderService.SetLeverageAsync documents why), but it is also a
                // perfectly ordinary leverage, so the number alone cannot carry the news.
                _errorStream.OnNext(
                    $"MEXC refused the leverage change on {symbol}: it is not at {lev} times.");
                return 1.0;
            }
            catch (Exception ex)
            {
                // A THROW IS NOT A REFUSAL, and this used to report both as a silent 1.0.
                //
                // 1.0 is also the "the exchange said no" return, so an exchange-side change that
                // SUCCEEDED and then failed to parse — or timed out after the venue had already
                // applied it — came back as the account sitting at 1x with nothing said. The
                // caller then sizes the position against 1x while the account may be on 20x from
                // this very call. The return value stays 1.0 because that is what the interface
                // means by "not set"; what was missing is that anybody was told.
                _errorStream.OnNext(
                    $"MEXC could not confirm the leverage change for {symbol}: {ex.GetType().Name}. "
                    + "The change may have been applied — check the leverage on the exchange before trading it.");
                return 1.0;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string SubscribeMsg(string channel) =>
            new JObject { ["method"] = "SUBSCRIPTION", ["params"] = new JArray { channel } }.ToString(Newtonsoft.Json.Formatting.None);

        private void SurfaceSubscribeError(string text, string what)
        {
            try
            {
                var json = JObject.Parse(text);
                if (json["code"] != null && json["code"]!.Value<int>() != 0)
                    SurfaceError($"MEXC {what} subscription rejected: {json["msg"]}");
            }
            catch { /* not a JSON control frame */ }
        }

        internal static double ParseD(string? s) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

        // Only the all-zero SENTINEL bar (zeros + a zero/epoch timestamp) is "empty";
        // all-zero legs at a real timestamp are left for the >0 filters to drop, not
        // silently swallowed here.
        private static bool IsEmptyBar(Ohlcv bar) =>
            bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0
            && (bar.Date == DateTime.MinValue || bar.Date == DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime);

        // MEXC futures uses underscore-separated pairs (BTC_USDT); spot is concatenated.
        internal static string ToFuturesSymbol(string spotSymbol) => SymbolFormat.Underscored(spotSymbol);

        private static OrderType MapSpotType(string? type) => type?.ToUpperInvariant() switch
        {
            "LIMIT" => OrderType.Limit,
            "MARKET" => OrderType.Market,
            "LIMIT_MAKER" => OrderType.Limit,
            "IMMEDIATE_OR_CANCEL" => OrderType.Limit,
            "FILL_OR_KILL" => OrderType.Limit,
            _ => OrderType.Market,
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keepAliveTimer?.Dispose();
                _focusedWs?.Dispose();
                _orderBookWs?.Dispose();
                _privateWs?.Dispose();
                _orderUpdateSubject?.Dispose();
                _orderBookSubject?.Dispose();
                _http?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
