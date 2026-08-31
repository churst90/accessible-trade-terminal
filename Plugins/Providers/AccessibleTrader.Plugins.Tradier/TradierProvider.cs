using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Tradier
{
    /// <summary>
    /// Tradier provider — US stocks and options trading.
    /// REST API + HTTP SSE streaming for real-time data.
    /// </summary>
    public class TradierProvider : BaseMarketDataProvider, ITradingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient _streamClient;
        private string? _accessToken;
        private string? _accountId;
        private string _baseUrl = "https://api.tradier.com/v1";
        private string _streamUrl = "https://stream.tradier.com/v1/markets/events";
        private bool _isSandbox;

        // Rate limiter: 120 req/min
        private readonly RateLimiter _rateLimiter = new(120, TimeSpan.FromMinutes(1));

        // HTTP SSE streaming state
        private CancellationTokenSource? _streamCts;
        private string? _currentSymbol;
        private string? _currentTimeframe;
        private Ohlcv? _lastCandle;
        private DateTime? _lastCandleStart;

        // Streams
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Dynamic: true only while the account websocket is actually connected and
        // subscribed. GeneralOrderService reads this at order-placement time — if
        // the stream is down (network trouble, sandbox without the entitlement),
        // it falls back to status polling so fills still announce either way.
        public bool SupportsOrderEventStreaming => _accountStreamUp && (_accountWs?.IsConnected ?? false);

        // The order-service poller (used whenever the account stream is down)
        // resolves via the authoritative /orders/{id} lookup below rather than the
        // fills heuristic — Tradier history events don't carry the placed order id.
        public bool SupportsOrderStatusQuery => true;

        // Account-event websocket (order status push).
        private ReconnectingWebSocket? _accountWs;
        private volatile bool _accountStreamUp;

        public override string Name => "Tradier";
        public override string Description => "Tradier — US Stocks & Options Trading";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Stock, MarketType.Options };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_accessToken);
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => _isSandbox ? ProviderEnvironment.Sandbox : ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 10000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.Brackets;

        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 1.0;

        public bool IsConnected => IsConfigured && !string.IsNullOrEmpty(_accountId);

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes, StandardTimeframes.OneDay,
            StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public TradierProvider()
        {
            // Phase 4 Track B2 — allow-listed to the three Tradier hosts
            // (production REST + streaming + sandbox). Streaming client
            // keeps its infinite timeout; regular client uses the factory
            // default 60 s.
            var hosts = new[]
            {
                "api.tradier.com",
                "stream.tradier.com",
                "sandbox.tradier.com",
            };
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId: "Tradier", allowedHosts: hosts);
            _streamClient = PluginHostServices.CreateHttpClient(
                providerId: "Tradier.Stream",
                allowedHosts: hosts,
                timeout: Timeout.InfiniteTimeSpan);
        }

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("AccessToken", out var token)) _accessToken = token;
            if (config.TryGetValue("ApiKey", out var key)) _accessToken ??= key; // Accept either name
            if (config.TryGetValue("AccountId", out var acct)) _accountId = acct;

            if (config.TryGetValue("Environment", out var env) && env.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
            {
                _isSandbox = true;
                _baseUrl = "https://sandbox.tradier.com/v1";
            }

            if (IsConfigured)
            {
                // Use the strongly-typed Authorization header rather than string
                // interpolation so the bearer token doesn't survive as a formatted
                // string anywhere in the request pipeline — reduces the chance of
                // the raw token appearing in HttpClient diagnostic logs.
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                _streamClient.DefaultRequestHeaders.Clear();
                _streamClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                _streamClient.DefaultRequestHeaders.Add("Accept", "application/json");
            }
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "Access token not configured");
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/user/profile");
                if (!response.IsSuccessStatusCode)
                    return (false, $"Token validation failed ({response.StatusCode})");

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());

                // Auto-discover account ID if not configured
                if (string.IsNullOrEmpty(_accountId))
                {
                    _accountId = json["profile"]?["account"]?["account_number"]?.ToString();
                    // Handle array of accounts
                    if (string.IsNullOrEmpty(_accountId))
                    {
                        var accounts = json["profile"]?["account"] as JArray;
                        _accountId = accounts?.FirstOrDefault()?["account_number"]?.ToString();
                    }
                }

                return (true, $"Token valid. Account: {_accountId}");
            }
            catch (Exception ex) { return (false, $"Validation error: {ex.Message}"); }
        }

        // ── Connection & Streaming ──────────────────────────────────────────

        public override async Task EnsureConnectedAsync()
        {
            if (!IsConfigured) return;
            if (string.IsNullOrEmpty(_accountId))
                await ValidateApiKeyAsync();
            _connectionStateStream.OnNext(ConnectionState.Connected);
            if (!string.IsNullOrEmpty(_accountId))
                StartAccountEventStream();
        }

        // ── Account-event stream ────────────────────────────────────────────
        // Tradier pushes order status over a websocket: mint a session id
        // (POST /accounts/events/session), connect wss://ws.tradier.com
        // /v1/accounts/events, and send {"events":["order"],"sessionid":...}.
        // Sessions are single-use and expire 5 minutes after creation, so a
        // fresh one is minted inside OnConnected on every (re)connect.
        private void StartAccountEventStream()
        {
            if (_accountWs != null) return;
            string wsUrl = _isSandbox
                ? "wss://sandbox-ws.tradier.com/v1/accounts/events"
                : "wss://ws.tradier.com/v1/accounts/events";

            // Heartbeat disabled (negative interval): Tradier defines no client
            // ping message, and an unrecognised text frame risks a server-side
            // close → reconnect churn that burns a session POST each cycle.
            _accountWs = new ReconnectingWebSocket(wsUrl, heartbeatInterval: TimeSpan.FromMilliseconds(-1))
                .OnConnected(async ws =>
                {
                    var resp = await _httpClient.PostAsync($"{_baseUrl}/accounts/events/session", null);
                    // Tradier answers a refused session with a PLAIN-TEXT body ("Invalid
                    // Access Token"), so the blind JObject.Parse below used to turn a 401
                    // into a JsonReaderException — the stream's error handler then spoke a
                    // parse error instead of the actual fact, which is that the key was
                    // refused and reconnecting will never help.
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException(
                            $"Tradier account-events session refused (HTTP {(int)resp.StatusCode}). Check the access token.");
                    var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    var sessionId = json["stream"]?["sessionid"]?.ToString();
                    if (string.IsNullOrEmpty(sessionId))
                        throw new InvalidOperationException("Tradier account-events session denied.");
                    await ws.SendAsync(new JObject
                    {
                        ["events"] = new JArray("order"),
                        ["sessionid"] = sessionId,
                        ["excludeAccounts"] = new JArray(),
                    }.ToString(Newtonsoft.Json.Formatting.None));
                    _accountStreamUp = true;
                })
                .OnMessage(HandleAccountEvent)
                .OnDisconnected(() => _accountStreamUp = false)
                .OnError(err =>
                {
                    _accountStreamUp = false;
                    _errorStream.OnNext($"Tradier account stream: {err}");
                });
            _ = _accountWs.ConnectAsync();
        }

        internal void HandleAccountEvent(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                if (json["event"]?.ToString() != "order") return;

                string status = json["status"]?.ToString() ?? "";
                OrderStatus mapped = MapAccountEventStatus(status);

                string type = json["type"]?.ToString() ?? "";
                string side = json["side"]?.ToString() ?? "";
                double avgFill   = json["avg_fill_price"]?.Value<double>() ?? 0;
                double lastQty   = json["last_fill_quantity"]?.Value<double>() ?? 0;
                double execQty   = json["executed_quantity"]?.Value<double>() ?? 0;
                double remaining = json["remaining_quantity"]?.Value<double>() ?? 0;

                _orderUpdateSubject.OnNext(new OrderUpdate(
                    OrderId: json["id"]?.ToString() ?? "",
                    Symbol: json["symbol"]?.ToString() ?? "",
                    // Tradier sides include buy_to_cover / sell_short — prefix match.
                    Side: side.StartsWith("buy", StringComparison.OrdinalIgnoreCase)
                        ? OrderSide.Buy : OrderSide.Sell,
                    FilledQuantity: mapped == OrderStatus.PartialFill && lastQty > 0 ? lastQty : execQty,
                    FilledPrice: avgFill,
                    RemainingQuantity: remaining,
                    Status: mapped,
                    // No trigger flags on the wire; the ORDER TYPE says what a fill means.
                    StopTriggered: mapped == OrderStatus.Filled
                        && type.Contains("stop", StringComparison.OrdinalIgnoreCase),
                    TakeProfitTriggered: false,
                    Timestamp: DateTime.UtcNow,
                    Reason: mapped == OrderStatus.Unknown ? $"Tradier status '{status}'" : null));
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Tradier account event parse error: {ex.Message}");
            }
        }

        /// <summary>Maps an account-stream order status word to an order status.
        /// "expired" was squashed into Cancelled (wrong reason spoken); "open" and
        /// "pending" were dropped entirely — they are the venue saying "accepted
        /// and working", which the order service now logs. Internal for direct
        /// testing.</summary>
        internal static OrderStatus MapAccountEventStatus(string status) => status switch
        {
            "filled"                  => OrderStatus.Filled,
            "partially_filled"        => OrderStatus.PartialFill,
            "canceled" or "cancelled" => OrderStatus.Cancelled,
            "expired"                 => OrderStatus.Expired,
            "rejected" or "error"     => OrderStatus.Rejected,
            "open" or "pending"       => OrderStatus.New,
            _                         => OrderStatus.Unknown,
        };

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();
            if (_currentSymbol == symbol && _currentTimeframe == timeframe) return;

            // Cancel existing stream — and DISPOSE it. A cancelled-but-undisposed source holds
            // its registration list, and a symbol switch does this hundreds of times over a long
            // session. Safe here for the same reason it is safe in Binance's version: the loop
            // that holds the token sees it already cancelled, so nothing registers on it again.
            _streamCts?.Cancel();
            _streamCts?.Dispose();
            _currentSymbol = symbol;
            _currentTimeframe = timeframe;
            _lastCandle = null;
            _lastCandleStart = null;

            _streamCts = new CancellationTokenSource();
            _ = Task.Run(() => StreamEventsAsync(symbol, _streamCts.Token));
        }

        /// <summary>
        /// How long the SSE read may block with nothing arriving before the connection is
        /// treated as dead. Matches <c>LiveStreamManager.SilenceThreshold</c> so the provider
        /// watchdog and the app-level one agree on what "quiet" means.
        /// </summary>
        internal const int StreamSilenceSeconds = 60;

        private async Task StreamEventsAsync(string symbol, CancellationToken ct)
        {
            int retryCount = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Step 1: Get a streaming session
                    var sessionResp = await _httpClient.PostAsync(
                        $"{_baseUrl}/markets/events/session", null, ct);
                    // A refusal carries a plain-text body, so the JObject.Parse below
                    // used to throw on a 401 and the retry loop spoke a parse error
                    // every five seconds. Name the actual fact instead — a refused key
                    // is fixable, a vague "failed to get session" is not.
                    if (!sessionResp.IsSuccessStatusCode)
                    {
                        _errorStream.OnNext(
                            $"Tradier: stream session refused (HTTP {(int)sessionResp.StatusCode}). Check the access token.");
                        await Task.Delay(5000, ct);
                        continue;
                    }
                    var sessionJson = JObject.Parse(await sessionResp.Content.ReadAsStringAsync(ct));
                    var sessionId = sessionJson["stream"]?["sessionid"]?.ToString();
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        _errorStream.OnNext("Tradier: Failed to get stream session");
                        await Task.Delay(5000, ct);
                        continue;
                    }

                    // Step 2: Connect to stream
                    var streamBody = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["sessionid"] = sessionId,
                        ["symbols"] = symbol,
                        ["linebreak"] = "true"
                    });

                    var request = new HttpRequestMessage(HttpMethod.Post, _streamUrl) { Content = streamBody };
                    var response = await _streamClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var reader = new StreamReader(stream);

                    retryCount = 0; // Reset on successful connection
                    _connectionStateStream.OnNext(ConnectionState.Connected);

                    // Step 3: Read line by line
                    while (!ct.IsCancellationRequested)
                    {
                        // The read carries an IDLE DEADLINE.
                        //
                        // _streamClient is built with Timeout.InfiniteTimeSpan, which is
                        // correct for a long-lived SSE body but means ReadLineAsync had no
                        // deadline of any kind. If the TCP connection was silently dropped —
                        // NAT timeout, a sleeping laptop, a middlebox — the read never returned,
                        // the outer reconnect loop never iterated, and the chart simply stopped
                        // updating with NOTHING SPOKEN. This is the exact failure
                        // ReconnectingWebSocket.MaxConsecutiveHeartbeatFailures exists to
                        // catch; Tradier's data path is the one that did not get it.
                        //
                        // The subscription requests linebreak=true, so Tradier emits newlines
                        // as keepalive even when nothing is trading. Those arrive as blank
                        // lines and reset the deadline below, which is why a fixed deadline is
                        // safe outside market hours. SilenceSeconds matches
                        // LiveStreamManager.SilenceThreshold so the two watchdogs agree on
                        // what "quiet" means.
                        string? line;
                        using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            idle.CancelAfter(TimeSpan.FromSeconds(StreamSilenceSeconds));
                            try
                            {
                                line = await reader.ReadLineAsync(idle.Token);
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                // Half-open connection. Say so and reconnect, rather than
                                // blocking here forever while the user watches a frozen chart.
                                _errorStream.OnNext(
                                    $"Tradier market stream went quiet for {StreamSilenceSeconds} seconds; reconnecting.");
                                _connectionStateStream.OnNext(ConnectionState.Disconnected);
                                break;
                            }
                        }

                        if (line == null) break; // Stream ended
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var json = JObject.Parse(line);
                            var type = json["type"]?.ToString();

                            if (type == "trade")
                            {
                                double price = json["price"]?.Value<double>() ?? 0;
                                double vol = json["size"]?.Value<double>() ?? 0;
                                if (price <= 0) continue;

                                var now = DateTime.UtcNow;
                                var interval = MapTimeframeToTimeSpan(_currentTimeframe ?? "1h");

                                if (_lastCandle.HasValue && _lastCandleStart.HasValue)
                                {
                                    if (now >= _lastCandleStart.Value.Add(interval))
                                    {
                                        var newStart = _lastCandleStart.Value;
                                        while (now >= newStart.Add(interval)) newStart = newStart.Add(interval);
                                        _lastCandleStart = newStart;
                                        _lastCandle = new Ohlcv(newStart, price, price, price, price, vol);
                                    }
                                    else
                                    {
                                        var tick = new Ohlcv(now, price, price, price, price, vol);
                                        _lastCandle = _lastCandle.Value.UpdateWith(tick);
                                    }
                                }
                                else
                                {
                                    // Floor to the PERIOD BOUNDARY, not the wall clock.
                                    //
                                    // This used to seed `_lastCandleStart = now`, and the
                                    // roll-forward above then advanced by exactly `interval`
                                    // from that seed. A 5-minute subscription started at
                                    // 10:03:47 emitted bars stamped 10:03:47, 10:08:47,
                                    // 10:13:47 — none of which line up with the REST timesales
                                    // bars at 10:00, 10:05, 10:10 that FetchIntradayAsync
                                    // returns from the same provider. The live bar then never
                                    // merged with the historical buffer; it appended as a
                                    // phantom bar at the wrong timestamp and every indicator
                                    // recomputed across it. This is the call that every other
                                    // provider's keyed feeds already make via
                                    // BarBucketConsolidator.
                                    var start = TimeframeUtility.GetPeriodStart(
                                        now, _currentTimeframe ?? "1h");
                                    _lastCandleStart = start;
                                    _lastCandle = new Ohlcv(start, price, price, price, price, vol);
                                }
                                _liveStream.OnNext(_lastCandle.Value);
                            }
                        }
                        catch { /* malformed line */ }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        retryCount++;
                        _errorStream.OnNext($"Tradier stream error: {ex.Message}");
                        var delay = TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, retryCount - 1), 30000));
                        await Task.Delay(delay, ct);
                    }
                }
            }
        }

        /// <summary>
        /// Disconnect means DISCONNECTED: the credentials are dropped and every channel this
        /// provider opened is torn down.
        ///
        /// <para>
        /// This used to cancel <c>_streamCts</c>, null two strings and return. Two things
        /// survived. The access token and the <c>Bearer</c> header planted on both HTTP clients
        /// stayed live, where every other provider in the fleet calls <c>ScrubCredentials</c>
        /// here (Kraken, Binance, Schwab). And <c>_accountWs</c> — only ever released in
        /// <c>Dispose</c> — kept reconnecting and kept pushing <c>OrderUpdate</c>s into
        /// <c>_orderUpdateSubject</c> after the user had disconnected the provider, so a fill on
        /// an account they had just walked away from still announced itself.
        /// </para>
        /// </summary>
        public override async Task DisconnectAsync()
        {
            _streamCts?.Cancel();
            _streamCts?.Dispose();
            _streamCts = null;
            _currentSymbol = null;
            _currentTimeframe = null;
            _lastCandle = null;
            _lastCandleStart = null;

            // Tear the account websocket down before scrubbing, so its reconnect cannot
            // re-authenticate on the way out with a token we are about to drop.
            var ws = _accountWs;
            _accountWs = null;
            _accountStreamUp = false;
            if (ws != null)
            {
                try { await ws.DisconnectAsync().ConfigureAwait(false); }
                catch { /* best-effort: we are tearing down either way */ }
                ws.Dispose();
            }

            ScrubCredentials(() => _accessToken = null);
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _streamClient.DefaultRequestHeaders.Authorization = null;

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        // ── Data Fetching ───────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());

            bool isIntraday = request.Timeframe == "1m" || request.Timeframe == "5m" || request.Timeframe == "15m";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    if (isIntraday)
                        return await FetchIntradayAsync(request);
                    else
                        return await FetchHistoryAsync(request);
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Tradier fetch error: {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchIntradayAsync(MarketDataRequest request)
        {
            string interval = request.Timeframe switch
            {
                "1m"  => "1min",
                "5m"  => "5min",
                "15m" => "15min",
                _     => "5min"
            };

            // Tradier's timesales start/end are interpreted in US-Eastern, so the
            // UTC request window must be rendered as Eastern wall-clock — otherwise
            // the window is shifted ~4-5h and the newest bars can be missed.
            string start = AccessibleTrader.Sdk.Models.ExchangeTime.ToEasternString(
                request.Since.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime
                    : DateTime.UtcNow.AddDays(-5), "yyyy-MM-dd HH:mm");
            string end = AccessibleTrader.Sdk.Models.ExchangeTime.ToEasternString(
                request.Until.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime
                    : DateTime.UtcNow, "yyyy-MM-dd HH:mm");

            string url = $"{_baseUrl}/markets/timesales?symbol={Uri.EscapeDataString(request.Symbol)}&interval={interval}&start={start}&end={end}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);
            var series = json["series"]?["data"];

            if (series == null) return (new List<Ohlcv>(), new List<(long, double)>());

            // Handle single item vs array
            JArray items = series is JArray arr ? arr : new JArray { series };

            var ohlcvList = items.Select(item =>
            {
                // timesales "timestamp" is Unix epoch SECONDS (e.g. 1557408600) —
                // DateTime.TryParse can't read that and yielded 0001-01-01 for every
                // intraday bar. TimestampParser handles the epoch → UTC conversion.
                var date = AccessibleTrader.Sdk.Models.TimestampParser.Parse(item["timestamp"]);
                return new Ohlcv(
                    date,
                    item["open"]?.Value<double>() ?? 0,
                    item["high"]?.Value<double>() ?? 0,
                    item["low"]?.Value<double>() ?? 0,
                    item["close"]?.Value<double>() ?? 0,
                    item["volume"]?.Value<double>() ?? 0);
            }).Where(x => x.Date > new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc))
              .OrderBy(x => x.Date).ToList();

            return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
        }

        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchHistoryAsync(MarketDataRequest request)
        {
            string interval = request.Timeframe switch
            {
                "1d" => "daily",
                "1w" => "weekly",
                "1M" => "monthly",
                _    => "daily"
            };

            string start = request.Since.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DateTime.UtcNow.AddYears(-2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string end = request.Until.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string url = $"{_baseUrl}/markets/history?symbol={Uri.EscapeDataString(request.Symbol)}&interval={interval}&start={start}&end={end}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);
            var history = json["history"]?["day"];

            if (history == null) return (new List<Ohlcv>(), new List<(long, double)>());

            JArray items = history is JArray arr ? arr : new JArray { history };

            var ohlcvList = items.Select(item =>
            {
                DateTime.TryParse(item["date"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date);
                return new Ohlcv(
                    date,
                    item["open"]?.Value<double>() ?? 0,
                    item["high"]?.Value<double>() ?? 0,
                    item["low"]?.Value<double>() ?? 0,
                    item["close"]?.Value<double>() ?? 0,
                    item["volume"]?.Value<double>() ?? 0);
            }).OrderBy(x => x.Date).ToList();

            int limit = Math.Min(request.Limit, ohlcvList.Count);
            ohlcvList = ohlcvList.TakeLast(limit).ToList();

            return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
        }

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            if (!IsConfigured) return new List<string>();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    // Tradier doesn't have a bulk symbol list endpoint; use lookup with common prefixes
                    var response = await _httpClient.GetStringAsync($"{_baseUrl}/markets/search?q=A&indexes=false");
                    var json = JObject.Parse(response);
                    var securities = json["securities"]?["security"];
                    if (securities == null) return new List<string>();

                    JArray items = securities is JArray arr ? arr : new JArray { securities };
                    return items
                        .Select(s => s["symbol"]?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .OrderBy(s => s)
                        .ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Tradier GetSymbolsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(market == MarketType.Options
                ? new List<string> { "Call", "Put" }
                : new List<string> { "Spot" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "1d", "1w", "1M" });

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            if (!IsConfigured) return (new(), new());
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{_baseUrl}/markets/quotes?symbols={Uri.EscapeDataString(symbol)}");
                    var json = JObject.Parse(response);
                    var quote = json["quotes"]?["quote"];
                    if (quote == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                    double bid = quote["bid"]?.Value<double>() ?? 0;
                    double bidSz = quote["bidsize"]?.Value<double>() ?? 0;
                    double ask = quote["ask"]?.Value<double>() ?? 0;
                    double askSz = quote["asksize"]?.Value<double>() ?? 0;

                    var bids = bid > 0 ? new List<OrderBookEntry> { new(bid, bidSz * 100) } : new();
                    var asks = ask > 0 ? new List<OrderBookEntry> { new(ask, askSz * 100) } : new();
                    return (bids, asks);
                });
            }
            catch (Exception ex)
            {
                // SAY SO. An empty depth ladder is a visible oddity for a sighted user and
                // indistinguishable from a book with no liquidity for this product's audience,
                // so a bare `catch { return (new(), new()); }` here reported a dead endpoint as
                // a market nobody is quoting. Kraken and Binance already push to _errorStream on
                // this path; Tradier, Alpaca and Coinbase did not.
                _errorStream.OnNext($"Tradier order book unavailable for {symbol}: {ex.GetType().Name}");
                return (new(), new());
            }
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
                var response = await _httpClient.GetStringAsync($"{_baseUrl}/accounts/{_accountId}/balances");
                var json = JObject.Parse(response);
                var bal = json["balances"];
                if (bal == null) return new List<Balance>();

                double equity = bal["equity"]?.Value<double>() ?? bal["total_equity"]?.Value<double>() ?? 0;
                double cash = bal["cash"]?["cash_available"]?.Value<double>() ?? bal["total_cash"]?.Value<double>() ?? 0;
                double marketValue = bal["market_value"]?.Value<double>() ?? 0;

                return new List<Balance>
                {
                    new("Cash", cash, 0),
                    new("Equity", equity, 0),
                    new("Market Value", marketValue, 0)
                };
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
                var response = await _httpClient.GetStringAsync($"{_baseUrl}/accounts/{_accountId}/positions");
                var json = JObject.Parse(response);
                // An empty account is {"positions":"null"} — the STRING — and
                // indexing into a JValue throws, so an empty book must be detected
                // before the child access, not after.
                var positions = (json["positions"] as JObject)?["position"];
                if (positions == null || positions.Type == JTokenType.Null) return new List<Position>();

                JArray items = positions is JArray arr ? arr : new JArray { positions };
                return items.Select(p =>
                {
                    // Signed as the venue reports it (shorts are negative):
                    // consumers derive long/short from the sign.
                    double qty = p["quantity"]?.Value<double>() ?? 0;
                    // Tradier's cost_basis is the TOTAL quote-currency cost of the position,
                    // not the per-unit average that Position.AveragePrice is contracted to
                    // carry — 100 shares of a $50 stock reports cost_basis 5000. This number
                    // is spoken in the positions panel and feeds risk math, so it is divided
                    // here. A zero quantity has no average price to report.
                    double costBasis = p["cost_basis"]?.Value<double>() ?? 0;
                    double avgPrice  = qty != 0 ? costBasis / Math.Abs(qty) : 0;
                    return new Position(
                        p["symbol"]?.ToString() ?? "",
                        qty,
                        avgPrice,
                        qty * (p["last_price"]?.Value<double>() ?? 0),
                        0 // Tradier doesn't provide unrealized P&L directly in positions
                    );
                }).ToList();
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
                var response = await _httpClient.GetStringAsync($"{_baseUrl}/accounts/{_accountId}/orders");
                var json = JObject.Parse(response);
                // An empty book is {"orders":"null"} — the STRING — and indexing
                // into a JValue throws, so it must be detected before the child
                // access, not after.
                var orders = (json["orders"] as JObject)?["order"];
                if (orders == null || orders.Type == JTokenType.Null) return new List<OpenOrder>();

                JArray items = orders is JArray arr ? arr : new JArray { orders };
                var result = new List<OpenOrder>();

                static bool IsOpenStatus(string? st) => st is "open" or "pending" or "partially_filled";
                void AddIfOpen(JToken node)
                {
                    var status = node["status"]?.ToString() ?? "";
                    if (!IsOpenStatus(status)) return;
                    var sym = node["symbol"]?.ToString() ?? "";
                    if (symbol != null && sym != symbol) return;
                    // Stop orders carry stop_price, not price — without the
                    // fallback every resting stop displayed (and spoke) as 0.
                    double price = node["price"]?.Value<double>()
                                   ?? node["stop_price"]?.Value<double>() ?? 0;
                    result.Add(new OpenOrder(
                        node["id"]?.ToString() ?? "",
                        sym,
                        node["side"]?.ToString() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                        MapTradierOrderType(node["type"]?.ToString() ?? "market"),
                        node["quantity"]?.Value<double>() ?? 0,
                        price,
                        status));
                }

                foreach (var o in items)
                {
                    AddIfOpen(o);
                    // Advanced-class (oto/otoco) orders nest their protective
                    // legs in "leg" — surface them so resting SL/TP are audible
                    // in the Orders tab instead of invisibly attached.
                    if (o["leg"] is JArray legArr)
                        foreach (var leg in legArr) AddIfOpen(leg);
                    else if (o["leg"] is JObject legObj)
                        AddIfOpen(legObj); // Tradier collapses single-element arrays
                }
                return result;
            });
        }

        /// <summary>Fill history via /accounts/{id}/history?type=trade (History tab
        /// parity — returned the interface default empty list until 2026-07-22).
        /// Tradier collapses single-element arrays to a bare object; both handled.</summary>
        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            // Through the rate limiter, like every other call in this file. This one and
            // GetOrderStatusAsync used to go straight to the wire — and GetOrderStatusAsync is
            // the call the order poller makes in a LOOP while an order is working, against a
            // 120 req/min budget shared with the chart's own fetches. A poll loop plus a chart
            // refresh could push the account into a 429 during exactly the window where the user
            // is waiting to hear whether they got filled.
            var response = await _rateLimiter.ExecuteAsync(() => _httpClient.GetStringAsync(
                $"{_baseUrl}/accounts/{_accountId}/history?type=trade&limit={limit}"));
            var json = JObject.Parse(response);
            var raw = json["history"]?["event"];
            if (raw == null || raw.Type == JTokenType.Null) return new();
            var events = raw is JArray arr ? arr : new JArray(raw);

            var fills = new List<TradeFill>();
            foreach (var ev in events)
            {
                var trade = ev["trade"];
                if (trade == null) continue;
                string sym = trade["symbol"]?.ToString() ?? "";
                if (symbol != null && !sym.Equals(symbol, StringComparison.OrdinalIgnoreCase)) continue;
                double qty = trade["quantity"]?.Value<double>() ?? 0;
                var filledAt = ev["date"]?.Value<DateTime>() ?? DateTime.MinValue;
                double price = trade["price"]?.Value<double>() ?? 0;
                fills.Add(new TradeFill(
                    FillId(sym, filledAt, qty, price), sym,
                    qty >= 0 ? OrderSide.Buy : OrderSide.Sell,
                    Math.Abs(qty),
                    price,
                    filledAt,
                    Math.Abs(trade["commission"]?.Value<double>() ?? 0)));
            }
            return fills.OrderByDescending(f => f.FilledAt).Take(limit).ToList();
        }

        /// <summary>
        /// A STABLE identity for a fill, derived from the fill itself.
        ///
        /// <para>
        /// Tradier's trade-history events carry no id of their own, and this used to pass
        /// <c>Guid.NewGuid()</c> — so the same broker fill got a different identity on every
        /// History-tab refresh and any consumer that dedupes or reconciles by id saw the whole
        /// history as new each time. The event does carry date, symbol, quantity and price;
        /// hashing those gives the same string for the same fill on every fetch, and different
        /// strings for two fills that differ in any of them. Two genuinely identical fills at
        /// the same instant collapse to one id — that is a real limit of what Tradier tells us,
        /// and it is strictly better than every fill being unrecognisable.
        /// </para>
        /// </summary>
        internal static string FillId(string symbol, DateTime filledAt, double quantity, double price)
        {
            string material = string.Create(CultureInfo.InvariantCulture,
                $"{symbol}|{filledAt.Ticks}|{quantity:R}|{price:R}");
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        }

        /// <summary>Authoritative single-order status via GET /accounts/{id}/orders/{id}.
        /// Returns null only when the order cannot be identified; a transient failure THROWS —
        /// see the comment in the body and <see cref="ITradingProvider.GetOrderStatusAsync"/>.</summary>
        public async Task<OrderStatusSnapshot?> GetOrderStatusAsync(string orderId, string? symbol = null)
        {
            if (!IsConnected || string.IsNullOrEmpty(orderId)) return null;
            // No catch: the order poller counts consecutive failures and gives up
            // with a spoken warning. Returning null here read as "still resolving"
            // and turned a dead endpoint into a silent infinite retry.
            // Through the rate limiter — see GetFillsAsync. This is the poll-loop call.
            var response = await _rateLimiter.ExecuteAsync(() => _httpClient.GetStringAsync(
                $"{_baseUrl}/accounts/{_accountId}/orders/{orderId}"));
            var order = JObject.Parse(response)["order"] as JObject;
            if (order == null) return null;
            return MapOrderToSnapshot(order);
        }

        /// <summary>Maps a Tradier order object (account event OR /orders/{id}) to a
        /// status snapshot. Handles both field spellings the two endpoints use
        /// (executed_quantity vs exec_quantity).</summary>
        internal static OrderStatusSnapshot MapOrderToSnapshot(JObject order)
        {
            string status = order["status"]?.ToString() ?? "";
            var state = status switch
            {
                "filled"                     => PolledOrderState.Filled,
                "partially_filled"           => PolledOrderState.PartiallyFilled,
                "canceled" or "cancelled"    => PolledOrderState.Cancelled,
                // Expired is not a cancel: nobody asked for it — a day order hit
                // the close. It was squashed into Cancelled here, so the trader
                // heard the wrong reason their order left the book.
                "expired"                    => PolledOrderState.Expired,
                "rejected" or "error"        => PolledOrderState.Rejected,
                _                            => PolledOrderState.Working,
            };
            string type = order["type"]?.ToString() ?? "";
            string side = order["side"]?.ToString() ?? "";
            double exec = order["exec_quantity"]?.Value<double>()
                          ?? order["executed_quantity"]?.Value<double>() ?? 0;
            return new OrderStatusSnapshot(
                state,
                side.StartsWith("buy", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell,
                order["symbol"]?.ToString() ?? "",
                FilledQuantity: exec,
                FilledPrice: order["avg_fill_price"]?.Value<double>() ?? 0,
                RemainingQuantity: order["remaining_quantity"]?.Value<double>() ?? 0,
                StopTriggered: state == PolledOrderState.Filled
                    && type.Contains("stop", StringComparison.OrdinalIgnoreCase));
        }

        // Tradier equities and options trade whole units only. The old
        // `((int)signal.Quantity)` truncated silently: a risk sizer emitting 9.7
        // shares placed 9, and 0.6 became 0 — which upstream validation had
        // already passed, because 0.6 IS finite-positive. Whole-share venues get
        // a refusal, never a silent resize.
        internal static string? WholeShareQuantityOrNull(double quantity) =>
            quantity >= 1 && Math.Abs(quantity - Math.Round(quantity)) < 1e-9
                ? ((long)Math.Round(quantity)).ToString(CultureInfo.InvariantCulture)
                : null;

        // OCC option symbols end in a fixed 15-character tail — YYMMDD, C or P, and
        // an 8-digit strike in thousandths (AAPL260918C00195000) — so the underlying
        // is whatever precedes it (roots may themselves contain digits, so parse from
        // the tail, never the front). Returns null when the tail doesn't scan; the
        // callers omit or refuse rather than sending a mangled root to the venue.
        internal static string? UnderlyingFromOccSymbol(string symbol)
        {
            if (symbol.Length <= 15) return null;
            var tail = symbol.AsSpan(symbol.Length - 15);
            if (tail[6] is not ('C' or 'P' or 'c' or 'p')) return null;
            for (int i = 0; i < 15; i++)
                if (i != 6 && tail[i] is < '0' or > '9') return null;
            var root = symbol[..^15].TrimEnd();
            return root.Length > 0 ? root : null;
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            if (WholeShareQuantityOrNull(signal.Quantity) is null)
                return $"ORDER_FAILED:Tradier trades whole shares only; "
                     + $"{signal.Quantity.ToString(CultureInfo.InvariantCulture)} is not a whole number of shares. "
                     + "Round the quantity and place the order again";
            try
            {
                return await _rateLimiter.ExecuteOnceAsync(async () =>
                {
                    bool isOption = string.Equals(signal.SubType, "Options", StringComparison.OrdinalIgnoreCase);

                    // Entry with protective legs → Tradier's native advanced classes
                    // (exchange-side linking, not attach-after-fill): OTO for entry+one
                    // leg, OTOCO for entry+both (the exits are an OCO pair). Before
                    // 2026-07-22 SL/TP on equity entries were SILENTLY DROPPED here;
                    // option entries were refused until 2026-08-23 — the venue takes
                    // the same indexed-leg classes with option_symbol + open/close sides.
                    if (signal.Type is OrderType.Market or OrderType.Limit
                        && (signal.StopLoss is > 0 || signal.TakeProfit is > 0))
                    {
                        return await PlaceBracketAsync(signal, isOption);
                    }

                    // Tradier's option side vocabulary is positional: buy_to_open /
                    // buy_to_close / sell_to_open / sell_to_close — the bare
                    // "buy"/"sell" this branch used to send is equity vocabulary the
                    // venue refuses on class=option. Open-versus-close depends on the
                    // position, so look it up; guessing turns "close my long call"
                    // into a naked short. If the read fails, refuse loudly instead.
                    string side;
                    if (isOption)
                    {
                        double held;
                        try
                        {
                            held = (await GetPositionsAsync())
                                .FirstOrDefault(pos => string.Equals(pos.Symbol, signal.Symbol,
                                    StringComparison.OrdinalIgnoreCase))
                                ?.Quantity ?? 0;
                        }
                        catch (Exception ex)
                        {
                            _errorStream.OnNext($"Tradier option order: positions read failed ({ex.GetType().Name})");
                            return "ORDER_FAILED:could not read positions to tell whether this option order "
                                 + "opens or closes a position. Check the connection and place the order again";
                        }
                        side = signal.Side == OrderSide.Buy
                            ? (held < 0 ? "buy_to_close" : "buy_to_open")
                            : (held > 0 ? "sell_to_close" : "sell_to_open");
                    }
                    else
                    {
                        side = signal.Side == OrderSide.Buy ? "buy" : "sell";
                    }

                    var postData = new Dictionary<string, string>
                    {
                        ["class"] = isOption ? "option" : "equity",
                        // Tradier REJECTS gtc on market orders (only day/pre/post are
                        // valid there); resting limit/stop orders keep gtc so they
                        // don't expire at the session close.
                        ["duration"] = signal.Type == OrderType.Market ? "day" : "gtc",
                        ["side"] = side,
                        ["quantity"] = WholeShareQuantityOrNull(signal.Quantity)!
                    };

                    if (isOption)
                    {
                        postData["option_symbol"] = signal.Symbol;
                        // The option-order contract wants the underlying alongside the
                        // OCC contract; omit it only when the tail doesn't scan.
                        if (UnderlyingFromOccSymbol(signal.Symbol) is string underlying)
                            postData["symbol"] = underlying;
                    }
                    else
                        postData["symbol"] = signal.Symbol;

                    switch (signal.Type)
                    {
                        case OrderType.Market:
                            postData["type"] = "market";
                            break;
                        case OrderType.Limit when signal.Price.HasValue:
                            postData["type"] = "limit";
                            postData["price"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture);
                            break;
                        case OrderType.StopMarket when signal.StopLoss.HasValue:
                            postData["type"] = "stop";
                            postData["stop"] = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture);
                            break;
                        case OrderType.StopLimit when signal.StopLoss.HasValue && signal.Price.HasValue:
                            postData["type"] = "stop_limit";
                            postData["price"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture);
                            postData["stop"] = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture);
                            break;
                        case OrderType.TakeProfitMarket or OrderType.TakeProfitLimit
                            when (signal.TriggerPrice ?? signal.TakeProfit ?? signal.Price) is double tpPx:
                            // Equities have no distinct TP type — a resting limit at the
                            // target IS the take profit.
                            postData["type"] = "limit";
                            postData["price"] = tpPx.ToString(CultureInfo.InvariantCulture);
                            break;
                        default:
                            return "ORDER_FAILED:Unsupported order type";
                    }

                    var content = new FormUrlEncodedContent(postData);
                    var response = await _httpClient.PostAsync($"{_baseUrl}/accounts/{_accountId}/orders", content);
                    var respStr = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return $"ORDER_FAILED:{respStr}";

                    var json = JObject.Parse(respStr);
                    if (json["errors"] != null)
                        return $"ORDER_FAILED:{json["errors"]}";

                    return json["order"]?["id"]?.ToString() ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Tradier order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        /// <summary>
        /// Entry + protective legs as ONE Tradier advanced order (indexed-leg form:
        /// type[0]/side[0]/quantity[0] is the entry; legs 1..n are the exits).
        /// class=oto for a single protective leg, class=otoco when both are given —
        /// the two exits are then an exchange-linked OCO pair. Exchange-side, so the
        /// protection exists even if this terminal dies right after submit. Options
        /// take the same classes with option_symbol and the open/close vocabulary.
        /// </summary>
        private async Task<string> PlaceBracketAsync(TradeSignal signal, bool isOption)
        {
            // A bracket IS an opening trade — protection on a position being
            // entered — so the option position effect is fixed by shape: the entry
            // opens, both exits close. (A close has no protective legs; it is the
            // exit.) Equities keep the plain vocabulary.
            string entrySide = isOption
                ? (signal.Side == OrderSide.Buy ? "buy_to_open" : "sell_to_open")
                : (signal.Side == OrderSide.Buy ? "buy" : "sell");
            string exitSide = isOption
                ? (signal.Side == OrderSide.Buy ? "sell_to_close" : "buy_to_close")
                : (signal.Side == OrderSide.Buy ? "sell" : "buy");
            // PlaceOrderAsync has already refused fractional quantities.
            string qty = WholeShareQuantityOrNull(signal.Quantity)!;
            bool both = signal.StopLoss is > 0 && signal.TakeProfit is > 0;

            var p = new Dictionary<string, string>
            {
                ["class"] = both ? "otoco" : "oto",
                ["duration"] = "gtc",
                ["side[0]"] = entrySide,
                ["quantity[0]"] = qty,
                ["type[0]"] = signal.Type == OrderType.Limit ? "limit" : "market",
            };

            // Non-indexed params apply to EVERY leg, and all three legs are the same
            // instrument: equities carry the ticker in symbol; options carry the OCC
            // contract in option_symbol plus the underlying in symbol.
            if (isOption)
            {
                if (UnderlyingFromOccSymbol(signal.Symbol) is not string underlying)
                    return "ORDER_FAILED:cannot read the underlying out of option symbol "
                         + $"{signal.Symbol}, so a bracket cannot be built. Place the option "
                         + "order on its own, then set the stop or target on the position";
                p["symbol"] = underlying;
                p["option_symbol"] = signal.Symbol;
            }
            else
            {
                p["symbol"] = signal.Symbol;
            }
            if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                p["price[0]"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture);

            int leg = 1;
            if (signal.TakeProfit is > 0)
            {
                p[$"side[{leg}]"] = exitSide;
                p[$"quantity[{leg}]"] = qty;
                p[$"type[{leg}]"] = "limit";
                p[$"price[{leg}]"] = signal.TakeProfit.Value.ToString(CultureInfo.InvariantCulture);
                leg++;
            }
            if (signal.StopLoss is > 0)
            {
                p[$"side[{leg}]"] = exitSide;
                p[$"quantity[{leg}]"] = qty;
                p[$"type[{leg}]"] = "stop";
                p[$"stop[{leg}]"] = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture);
            }

            // No limiter wrapper here on purpose: the only caller is PlaceOrderAsync,
            // from inside its ExecuteOnceAsync lambda, so the rate slot is already
            // held and the no-retry rule (see RateLimiter.ExecuteOnceAsync) already
            // covers this POST. Wrapping it again would double-charge the budget.
            var content = new FormUrlEncodedContent(p);
            var response = await _httpClient.PostAsync($"{_baseUrl}/accounts/{_accountId}/orders", content);
            var respStr = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return $"ORDER_FAILED:{respStr}";
            var json = JObject.Parse(respStr);
            if (json["errors"] != null) return $"ORDER_FAILED:{json["errors"]}";
            return json["order"]?["id"]?.ToString() ?? "ORDER_SUBMITTED";
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                var response = await _rateLimiter.ExecuteAsync(async () =>
                    await _httpClient.DeleteAsync($"{_baseUrl}/accounts/{_accountId}/orders/{orderId}"));
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Tradier CancelOrderAsync failed for {orderId} ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        // ── Helpers ─────────────────────────────────────────────────────────

        private static OrderType MapTradierOrderType(string type) => type switch
        {
            "limit"      => OrderType.Limit,
            "stop"       => OrderType.StopMarket,
            "stop_limit" => OrderType.StopLimit,
            _            => OrderType.Market
        };

        private static TimeSpan MapTimeframeToTimeSpan(string tf) => tf switch
        {
            "1m"  => TimeSpan.FromMinutes(1),
            "5m"  => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "1d"  => TimeSpan.FromDays(1),
            "1w"  => TimeSpan.FromDays(7),
            "1M"  => TimeSpan.FromDays(30),
            _     => TimeSpan.FromHours(1)
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
                _streamClient?.Dispose();
                _streamCts?.Dispose();
                _accountWs?.Dispose();
                _orderUpdateSubject?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
