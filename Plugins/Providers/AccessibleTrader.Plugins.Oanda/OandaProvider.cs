using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Oanda
{
    /// <summary>
    /// OANDA provider — Forex, commodity CFDs, and index CFDs.
    /// REST v20 API + HTTP chunked streaming for real-time pricing.
    /// </summary>
    public class OandaProvider : BaseMarketDataProvider, ITradingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient _streamClient;
        private string? _accessToken;
        private string? _accountId;
        private string _restUrl = "https://api-fxpractice.oanda.com/v3";
        private string _streamUrl = "https://stream-fxpractice.oanda.com/v3";
        private bool _isPractice = true;

        // Rate limiter: OANDA allows ~120/sec (very generous)
        private readonly RateLimiter _rateLimiter = new(120, TimeSpan.FromSeconds(1));

        // HTTP chunked streaming state
        private CancellationTokenSource? _streamCts;
        private string? _currentSymbol;
        private string? _currentTimeframe;
        private Ohlcv? _lastCandle;
        private DateTime? _lastCandleStart;

        // Streams
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();

        // True only while the transactions HTTP stream is actually delivering.
        // The default-true flag used to claim streaming through every retry gap
        // (and before the stream ever connected), so the order service never
        // polled while fills were going nowhere.
        private volatile bool _txStreamUp;
        public bool SupportsOrderEventStreaming => _txStreamUp;

        // Orders seen on the transaction stream: id → (instrument, SIGNED units),
        // plus cumulative fill (qty, last price) per order — so cancels, rejects
        // and partial fills can be reported truthfully. The ORDER_CANCEL
        // transaction itself carries neither instrument nor side, which is how a
        // cancelled sell on EUR/USD used to announce as a cancelled BUY on an
        // empty symbol.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Instrument, double Units)> _streamOrders = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (double Cum, double LastPx)> _streamFills = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Transaction stream for order updates
        private CancellationTokenSource? _txnStreamCts;

        public override string Name => "OANDA";
        public override string Description => "OANDA — Forex & CFD Trading";
        public override List<MarketType> SupportedMarkets => new List<MarketType>
        {
            MarketType.Forex, MarketType.Commodity, MarketType.Index
        };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_accountId);
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => _isPractice ? ProviderEnvironment.Paper : ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 5000;
        /// <summary>
        /// <c>TrailingStop</c> was declared here and never implemented — the string
        /// "Trail" appeared nowhere else in this file — so the dashboard drew its
        /// trailing fields and the order went out with no trail attached. The flag
        /// was withdrawn, and is back now that <c>trailingStopLossOnFill</c> is
        /// really sent.
        /// </summary>
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.Leverage | ProviderCapabilities.Shorting |
            ProviderCapabilities.MarginTrading | ProviderCapabilities.TrailingStop;

        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 50.0;

        public bool IsConnected => IsConfigured;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes, StandardTimeframes.ThirtyMinutes,
            StandardTimeframes.OneHour, StandardTimeframes.TwoHours,
            StandardTimeframes.FourHours, StandardTimeframes.SixHours,
            StandardTimeframes.EightHours, StandardTimeframes.TwelveHours,
            StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public OandaProvider()
        {
            // Phase 4 Track B2 — both clients allow-listed to the four
            // Oanda hosts (practice + live, REST + streaming). The stream
            // client keeps its infinite timeout for long-polling pricing
            // and transaction streams; the regular client uses the
            // factory default 60 s.
            var hosts = new[]
            {
                "api-fxpractice.oanda.com",
                "stream-fxpractice.oanda.com",
                "api-fxtrade.oanda.com",
                "stream-fxtrade.oanda.com",
            };
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId: "Oanda", allowedHosts: hosts);
            _streamClient = PluginHostServices.CreateHttpClient(
                providerId: "Oanda.Stream",
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
            if (config.TryGetValue("ApiKey", out var key)) _accessToken ??= key;
            if (config.TryGetValue("AccountId", out var acct)) _accountId = acct;

            if (config.TryGetValue("Environment", out var env))
            {
                // BOTH branches, not just live: the else half was missing, so once
                // switched to live a later practice config silently kept the LIVE
                // urls — the mirror image of Binance's case-sensitive testnet
                // compare, in the direction that trades real money by accident.
                bool live = env.Equals("live", StringComparison.OrdinalIgnoreCase);
                _isPractice = !live;
                _restUrl    = live ? "https://api-fxtrade.oanda.com/v3"    : "https://api-fxpractice.oanda.com/v3";
                _streamUrl  = live ? "https://stream-fxtrade.oanda.com/v3" : "https://stream-fxpractice.oanda.com/v3";
            }

            if (!string.IsNullOrEmpty(_accessToken))
            {
                // Strongly-typed Authorization header — avoids the raw token persisting
                // as a formatted string in the request pipeline and any diagnostic logs.
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                _httpClient.DefaultRequestHeaders.Add("Accept-Datetime-Format", "UNIX");

                _streamClient.DefaultRequestHeaders.Clear();
                _streamClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                _streamClient.DefaultRequestHeaders.Add("Accept-Datetime-Format", "UNIX");
            }
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
                return (false, "Access token not configured");
            try
            {
                var response = await _httpClient.GetAsync($"{_restUrl}/accounts");
                if (!response.IsSuccessStatusCode)
                    return (false, $"Token validation failed ({response.StatusCode})");

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var accounts = json["accounts"] as JArray;

                // Auto-discover account ID if not configured
                if (string.IsNullOrEmpty(_accountId) && accounts?.Count > 0)
                    _accountId = accounts[0]?["id"]?.ToString();

                if (string.IsNullOrEmpty(_accountId))
                    return (false, "No accounts found");

                return (true, $"Token valid. Account: {_accountId}");
            }
            catch (Exception ex) { return (false, $"Validation error: {ex.Message}"); }
        }

        // ── Connection & Streaming ──────────────────────────────────────────

        public override async Task EnsureConnectedAsync()
        {
            if (!IsConfigured)
            {
                if (!string.IsNullOrEmpty(_accessToken))
                    await ValidateApiKeyAsync(); // Auto-discovers account
            }
            if (IsConfigured)
                _connectionStateStream.OnNext(ConnectionState.Connected);
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();
            var instrument = FormatInstrument(symbol);
            if (_currentSymbol == instrument && _currentTimeframe == timeframe) return;

            // Cancel AND dispose — see SchwabProvider.SetSubscriptionAsync for why a cancelled
            // source that is never disposed accumulates over a long session of symbol switches.
            _streamCts?.Cancel();
            _streamCts?.Dispose();
            _currentSymbol = instrument;
            _currentTimeframe = timeframe;
            _lastCandle = null;
            _lastCandleStart = null;

            _streamCts = new CancellationTokenSource();
            _ = Task.Run(() => StreamPricingAsync(instrument, _streamCts.Token));

            // Also start transaction stream for order updates
            if (_txnStreamCts == null)
            {
                _txnStreamCts = new CancellationTokenSource();
                _ = Task.Run(() => StreamTransactionsAsync(_txnStreamCts.Token));
            }
        }

        private async Task StreamPricingAsync(string instrument, CancellationToken ct)
        {
            int retryCount = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    string url = $"{_streamUrl}/accounts/{_accountId}/pricing/stream?instruments={instrument}";
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    var response = await _streamClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var reader = new StreamReader(stream);

                    retryCount = 0;
                    _connectionStateStream.OnNext(ConnectionState.Connected);

                    while (!ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(ct);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var json = JObject.Parse(line);
                            var type = json["type"]?.ToString();

                            if (type == "PRICE")
                            {
                                // Extract best bid/ask and compute midpoint
                                var bids = json["bids"] as JArray;
                                var asks = json["asks"] as JArray;
                                double bid = bids?.FirstOrDefault()?["price"]?.Value<double>() ?? 0;
                                double ask = asks?.FirstOrDefault()?["price"]?.Value<double>() ?? 0;
                                if (bid <= 0 || ask <= 0) continue;

                                double mid = (bid + ask) / 2.0;
                                var now = DateTime.UtcNow;

                                // Try parse timestamp from OANDA. TimestampParser
                                // handles both spellings the API can send: fractional
                                // unix seconds (AcceptDatetimeFormat: UNIX) and
                                // RFC3339 — the inline version this replaces silently
                                // kept UtcNow for the RFC3339 case.
                                var tsStr = json["time"]?.ToString();
                                if (!string.IsNullOrEmpty(tsStr))
                                {
                                    var parsed = TimestampParser.Parse(tsStr);
                                    // Compare against the parser's own constant sentinel, not
                                    // against MinValue.ToUniversalTime() — that converts from the
                                    // machine's zone, so the value this test used to compare with
                                    // differed between a London box and a New York one.
                                    if (parsed > TimestampParser.Invalid) now = parsed;
                                }

                                var interval = MapTimeframeToTimeSpan(_currentTimeframe ?? "1h");

                                if (_lastCandle.HasValue && _lastCandleStart.HasValue)
                                {
                                    if (now >= _lastCandleStart.Value.Add(interval))
                                    {
                                        var newStart = _lastCandleStart.Value;
                                        while (now >= newStart.Add(interval)) newStart = newStart.Add(interval);
                                        _lastCandleStart = newStart;
                                        _lastCandle = new Ohlcv(newStart, mid, mid, mid, mid, 0);
                                    }
                                    else
                                    {
                                        var tick = new Ohlcv(now, mid, mid, mid, mid, 0);
                                        _lastCandle = _lastCandle.Value.UpdateWith(tick);
                                    }
                                }
                                else
                                {
                                    _lastCandleStart = now;
                                    _lastCandle = new Ohlcv(now, mid, mid, mid, mid, 0);
                                }
                                _liveStream.OnNext(_lastCandle.Value);
                            }
                            // HEARTBEAT type — just keep alive, no action needed
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[OANDA] Malformed pricing line skipped: {ex.GetType().Name}"); }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        retryCount++;
                        _errorStream.OnNext($"OANDA pricing stream error: {ex.Message}");
                        var delay = TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, retryCount - 1), 30000));
                        await Task.Delay(delay, ct);
                    }
                }
            }
        }

        private async Task StreamTransactionsAsync(CancellationToken ct)
        {
            int retryCount = 0;
            while (!ct.IsCancellationRequested)
            {
                _txStreamUp = false; // not delivering until (re)connected below
                try
                {
                    string url = $"{_streamUrl}/accounts/{_accountId}/transactions/stream";
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    var response = await _streamClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var reader = new StreamReader(stream);

                    retryCount = 0;
                    _txStreamUp = true;

                    while (!ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(ct);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var json = JObject.Parse(line);
                            var type = json["type"]?.ToString() ?? "";

                            if (type.EndsWith("_ORDER", StringComparison.Ordinal)
                                && json["units"] != null && json["instrument"] != null)
                            {
                                // Creation transactions carry instrument + signed
                                // units + the order's id; remember them so later
                                // cancels and partial fills can speak the truth.
                                var oid = json["id"]?.ToString();
                                if (!string.IsNullOrEmpty(oid))
                                    _streamOrders[oid!] = (json["instrument"]!.ToString(), json["units"]!.Value<double>());
                            }
                            else if (type == "ORDER_FILL")
                            {
                                var tradeId = json["tradeOpened"]?["tradeID"]?.ToString()
                                    ?? json["tradesClosed"]?.FirstOrDefault()?["tradeID"]?.ToString()
                                    ?? json["id"]?.ToString() ?? "";
                                var instrument = json["instrument"]?.ToString() ?? "";
                                double units = json["units"]?.Value<double>() ?? 0;
                                double price = json["price"]?.Value<double>() ?? 0;

                                // RemainingQuantity was hardcoded 0, so a partial
                                // fill announced as complete. The fill transaction
                                // doesn't carry the remainder — derive it from the
                                // creation transaction seen on this stream.
                                var orderId = json["orderID"]?.ToString() ?? "";
                                double filledNow = Math.Abs(units);
                                double remaining = 0;
                                if (orderId.Length > 0 && _streamOrders.TryGetValue(orderId, out var known))
                                {
                                    var cum = _streamFills.AddOrUpdate(orderId,
                                        (filledNow, price), (_, prev) => (prev.Cum + filledNow, price));
                                    remaining = Math.Max(0, Math.Abs(known.Units) - cum.Cum);
                                    if (remaining <= 1e-9)
                                    {
                                        _streamOrders.TryRemove(orderId, out _);
                                        _streamFills.TryRemove(orderId, out _);
                                    }
                                }

                                _orderUpdateSubject.OnNext(new OrderUpdate(
                                    tradeId, instrument,
                                    units >= 0 ? OrderSide.Buy : OrderSide.Sell,
                                    filledNow, price, remaining,
                                    remaining > 1e-9 ? OrderStatus.PartialFill : OrderStatus.Filled,
                                    false, false, DateTime.UtcNow));
                            }
                            else if (type == "ORDER_CANCEL")
                            {
                                await EmitCancelAsync(json).ConfigureAwait(false);
                            }
                            else if (type.EndsWith("_ORDER_REJECT", StringComparison.Ordinal))
                            {
                                // Reject transactions echo the requested instrument
                                // and signed units. Before this, rejections were
                                // not reported at all — the trader heard nothing.
                                double u = json["units"]?.Value<double>() ?? 0;
                                _orderUpdateSubject.OnNext(new OrderUpdate(
                                    json["id"]?.ToString() ?? "",
                                    json["instrument"]?.ToString() ?? "",
                                    u >= 0 ? OrderSide.Buy : OrderSide.Sell,
                                    0, 0, Math.Abs(u),
                                    OrderStatus.Rejected, false, false, DateTime.UtcNow,
                                    Reason: json["rejectReason"]?.ToString()));
                            }
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[OANDA] Malformed transaction line skipped: {ex.GetType().Name}"); }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _txStreamUp = false;
                    if (!ct.IsCancellationRequested)
                    {
                        retryCount++;
                        _errorStream.OnNext($"OANDA transaction stream error: {ex.Message}");
                        var delay = TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, retryCount - 1), 30000));
                        await Task.Delay(delay, ct);
                    }
                }
            }
        }

        /// <summary>Truthful ORDER_CANCEL reporting. The cancel transaction
        /// carries neither instrument nor side, so an order created before this
        /// stream connected is LOOKED UP, not guessed — fabricating "buy on an
        /// empty symbol" is the recorded defect. If the lookup fails, say the
        /// details are unavailable rather than announce a fabricated ticket.</summary>
        private async Task EmitCancelAsync(JObject cancelTxn)
        {
            var orderId = cancelTxn["orderID"]?.ToString() ?? "";
            string instrument;
            double units;
            if (_streamOrders.TryRemove(orderId, out var known))
            {
                (instrument, units) = known;
            }
            else
            {
                try
                {
                    var resp = await _httpClient.GetStringAsync($"{_restUrl}/accounts/{_accountId}/orders/{orderId}").ConfigureAwait(false);
                    var order = JObject.Parse(resp)["order"];
                    instrument = order?["instrument"]?.ToString() ?? "";
                    units = order?["units"]?.Value<double>() ?? 0;
                }
                catch (Exception ex)
                {
                    _errorStream.OnNext($"OANDA order {orderId} was cancelled; details unavailable ({ex.GetType().Name}) — check your open orders.");
                    return;
                }
                if (instrument.Length == 0)
                {
                    _errorStream.OnNext($"OANDA order {orderId} was cancelled; details unavailable — check your open orders.");
                    return;
                }
            }

            _streamFills.TryRemove(orderId, out var fills);
            _orderUpdateSubject.OnNext(new OrderUpdate(
                orderId, instrument,
                units >= 0 ? OrderSide.Buy : OrderSide.Sell,
                fills.Cum, fills.LastPx, Math.Max(0, Math.Abs(units) - fills.Cum),
                OrderStatus.Cancelled, false, false, DateTime.UtcNow,
                Reason: cancelTxn["reason"]?.ToString()));
        }

        public override async Task DisconnectAsync()
        {
            _streamCts?.Cancel();
            _streamCts?.Dispose();
            _streamCts = null;
            _txnStreamCts?.Cancel();
            _txnStreamCts?.Dispose();
            _txnStreamCts = null;
            _currentSymbol = null;
            _currentTimeframe = null;
            _lastCandle = null;
            _lastCandleStart = null;

            // Drop the live-money Bearer token from both HTTP clients and the fields
            // so a crash dump after disconnect can't recover it (every other provider
            // scrubs; Oanda previously left the token resident).
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _streamClient.DefaultRequestHeaders.Authorization = null;
            ScrubCredentials(
                () => _accessToken = null,
                () => _accountId = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        // ── Data Fetching ───────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());

            var instrument = FormatInstrument(request.Symbol);
            string granularity = MapGranularity(request.Timeframe);
            int count = Math.Min(request.Limit, 5000);

            // Oanda REJECTS (HTTP 400) count together with BOTH from and to. Send the
            // range alone when both bounds are set; otherwise count plus the single
            // bound (if any). The old code always appended count → range fetches 400'd.
            bool hasFrom = request.Since.HasValue, hasTo = request.Until.HasValue;
            string from = hasFrom ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since!.Value).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "";
            string to   = hasTo   ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until!.Value).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "";

            string url = $"{_restUrl}/instruments/{instrument}/candles?granularity={granularity}&price=M";
            if (hasFrom && hasTo)
                url += $"&from={from}&to={to}";
            else
            {
                url += $"&count={count}";
                if (hasFrom)     url += $"&from={from}";
                else if (hasTo)  url += $"&to={to}";
            }

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);
                    var candles = json["candles"] as JArray;
                    if (candles == null)
                    {
                        var err = json["errorMessage"]?.ToString();
                        if (!string.IsNullOrEmpty(err))
                            _errorStream.OnNext($"Oanda data error for {request.Symbol}: {err}");
                        return (new List<Ohlcv>(), new List<(long, double)>());
                    }

                    var ohlcvList = candles
                        .Where(c => c["complete"]?.Value<bool>() != false || candles.Last == c)
                        .Select(c =>
                        {
                            var mid = c["mid"];
                            // Fractional unix seconds or RFC3339 — TimestampParser
                            // handles both; the inline version this replaces
                            // hard-assumed SECONDS for any numeric value.
                            var date = TimestampParser.Parse(c["time"]?.ToString() ?? "0");

                            return new Ohlcv(
                                date,
                                double.Parse(mid?["o"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                                double.Parse(mid?["h"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                                double.Parse(mid?["l"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                                double.Parse(mid?["c"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                                c["volume"]?.Value<double>() ?? 0);
                        })
                        .OrderBy(x => x.Date)
                        .ToList();

                    if (ohlcvList.Any())
                    {
                        _lastCandle = ohlcvList.Last();
                        _lastCandleStart = _lastCandle.Value.Date;
                    }

                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"OANDA fetch error: {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            if (!IsConfigured) return new List<string>();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{_restUrl}/accounts/{_accountId}/instruments");
                    var json = JObject.Parse(response);
                    var instruments = json["instruments"] as JArray;
                    if (instruments == null) return new List<string>();

                    string typeFilter = market switch
                    {
                        MarketType.Forex     => "CURRENCY",
                        MarketType.Commodity => "CFD",
                        MarketType.Index     => "CFD",
                        _                    => ""
                    };

                    return instruments
                        .Where(i => string.IsNullOrEmpty(typeFilter) || i["type"]?.ToString() == typeFilter)
                        .Select(i => i["name"]?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .OrderBy(s => s)
                        .ToList();
                });
            }
            // An empty instrument list is how this provider spelled both "your account trades
            // nothing in this market" and "the read failed" — the same fact for a user who can
            // only hear the result. See ProviderSymbolListSilenceTests.
            catch (HttpRequestException ex)
            {
                _errorStream.OnNext($"OANDA: network error fetching instrument list: {ex.GetType().Name}");
                return new List<string>();
            }
            catch (TaskCanceledException)
            {
                return new List<string>();
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                _errorStream.OnNext($"OANDA: malformed instrument-list response: {ex.GetType().Name}");
                return new List<string>();
            }
            catch (Exception ex)
            {
                // The access token rides in a header rather than the URL here, but ex.Message
                // still carries the account id — name the type, not the message.
                _errorStream.OnNext($"OANDA: instrument-list error: {ex.GetType().Name}");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "1w", "1M" });

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            if (!IsConfigured) return (new(), new());
            var instrument = FormatInstrument(symbol);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{_restUrl}/instruments/{instrument}/orderBook");
                    var json = JObject.Parse(response);
                    var book = json["orderBook"];
                    if (book == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                    var buckets = book["buckets"] as JArray;
                    if (buckets == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                    var bids = buckets
                        .Where(b => (b["longCountPercent"]?.Value<double>() ?? 0) > 0)
                        .Take(limit)
                        .Select(b => new OrderBookEntry(
                            double.Parse(b["price"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            b["longCountPercent"]?.Value<double>() ?? 0))
                        .ToList();

                    var asks = buckets
                        .Where(b => (b["shortCountPercent"]?.Value<double>() ?? 0) > 0)
                        .Take(limit)
                        .Select(b => new OrderBookEntry(
                            double.Parse(b["price"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            b["shortCountPercent"]?.Value<double>() ?? 0))
                        .ToList();

                    return (bids, asks);
                });
            }
            catch (Exception ex)
            {
                // An empty ladder must not be the only thing the user hears: for this
                // product's audience it is indistinguishable from a book with no
                // liquidity, and those are opposite facts.
                _errorStream.OnNext($"OANDA order book unavailable for {symbol}: {ex.GetType().Name}");
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
                var response = await _httpClient.GetStringAsync($"{_restUrl}/accounts/{_accountId}/summary");
                var json = JObject.Parse(response);
                var acct = json["account"];
                if (acct == null) return new List<Balance>();

                double balance = double.TryParse(acct["balance"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double b) ? b : 0;
                double unrealizedPL = double.TryParse(acct["unrealizedPL"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double u) ? u : 0;
                double marginUsed = double.TryParse(acct["marginUsed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double m) ? m : 0;
                double marginAvail = double.TryParse(acct["marginAvailable"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double ma) ? ma : 0;
                double nav = double.TryParse(acct["NAV"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double n) ? n : 0;

                return new List<Balance>
                {
                    new("Balance", balance, 0),
                    new("NAV", nav, 0),
                    new("Unrealized P&L", unrealizedPL, 0),
                    new("Margin Available", marginAvail, marginUsed)
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
                var response = await _httpClient.GetStringAsync($"{_restUrl}/accounts/{_accountId}/openPositions");
                var json = JObject.Parse(response);
                var positions = json["positions"] as JArray;
                if (positions == null) return new List<Position>();

                return positions.Select(p =>
                {
                    // Short units arrive negative and STAY negative: consumers
                    // derive long/short from the sign, and the old Abs made a
                    // 10,000-unit short read identically to a 10,000-unit long
                    // in every risk calculation and every spoken summary.
                    var longUnits = p["long"]?["units"]?.Value<double>() ?? 0;
                    var shortUnits = p["short"]?["units"]?.Value<double>() ?? 0;
                    double units = longUnits != 0 ? longUnits : shortUnits;
                    double avgPrice = longUnits != 0
                        ? (p["long"]?["averagePrice"]?.Value<double>() ?? 0)
                        : (p["short"]?["averagePrice"]?.Value<double>() ?? 0);
                    double unrealizedPL = (p["long"]?["unrealizedPL"]?.Value<double>() ?? 0)
                        + (p["short"]?["unrealizedPL"]?.Value<double>() ?? 0);

                    return new Position(
                        p["instrument"]?.ToString() ?? "",
                        units,
                        avgPrice,
                        units * avgPrice, // approximate market value
                        unrealizedPL);
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
                var response = await _httpClient.GetStringAsync($"{_restUrl}/accounts/{_accountId}/pendingOrders");
                var json = JObject.Parse(response);
                var orders = json["orders"] as JArray;
                if (orders == null) return new List<OpenOrder>();

                return orders
                    .Where(o => symbol == null || (o["instrument"]?.ToString() ?? "").Contains(FormatInstrument(symbol), StringComparison.OrdinalIgnoreCase))
                    .Select(o =>
                    {
                        double units = o["units"]?.Value<double>() ?? 0;
                        return new OpenOrder(
                            o["id"]?.ToString() ?? "",
                            o["instrument"]?.ToString() ?? "",
                            units >= 0 ? OrderSide.Buy : OrderSide.Sell,
                            MapOandaOrderType(o["type"]?.ToString() ?? "MARKET"),
                            Math.Abs(units),
                            double.TryParse(o["price"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double px) ? px : 0,
                            o["state"]?.ToString() ?? "PENDING");
                    }).ToList();
            });
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteOnceAsync(async () =>
                {
                    var instrument = FormatInstrument(signal.Symbol);
                    // OANDA uses signed units: positive = buy, negative = sell
                    double units = signal.Side == OrderSide.Buy ? signal.Quantity : -signal.Quantity;

                    JObject orderRequest;

                    switch (signal.Type)
                    {
                        case OrderType.Market:
                            orderRequest = new JObject
                            {
                                ["type"] = "MARKET",
                                ["instrument"] = instrument,
                                ["units"] = units.ToString(CultureInfo.InvariantCulture),
                                ["timeInForce"] = "FOK"
                            };
                            break;

                        case OrderType.Limit when signal.Price.HasValue:
                            orderRequest = new JObject
                            {
                                ["type"] = "LIMIT",
                                ["instrument"] = instrument,
                                ["units"] = units.ToString(CultureInfo.InvariantCulture),
                                ["price"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture),
                                ["timeInForce"] = "GTC"
                            };
                            break;

                        case OrderType.StopMarket when signal.StopLoss.HasValue:
                            orderRequest = new JObject
                            {
                                ["type"] = "STOP",
                                ["instrument"] = instrument,
                                ["units"] = units.ToString(CultureInfo.InvariantCulture),
                                ["price"] = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture),
                                ["timeInForce"] = "GTC"
                            };
                            break;

                        case OrderType.StopLimit when signal.StopLoss.HasValue && signal.Price.HasValue:
                            orderRequest = new JObject
                            {
                                ["type"] = "STOP",
                                ["instrument"] = instrument,
                                ["units"] = units.ToString(CultureInfo.InvariantCulture),
                                ["price"] = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture),
                                ["priceBound"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture),
                                ["timeInForce"] = "GTC"
                            };
                            break;

                        default:
                            return "ORDER_FAILED:Unsupported order type";
                    }

                    // Attach stop-loss on fill.
                    //
                    // Not market-only. OANDA accepts stopLossOnFill on a LIMIT order
                    // exactly as it does on a market one, and gating this on Market
                    // while takeProfitOnFill below is ungated produced the worst
                    // possible split: a limit entry carrying both legs got its target
                    // and lost its stop, silently, leaving a live position naked with
                    // nothing said. Same shape as the paper broker's bracket bug.
                    //
                    // STOP entries are excluded because StopLoss is spent above as the
                    // entry's own trigger price (see the STOP cases); re-reading it
                    // here would attach a protective stop at the entry itself.
                    if (signal.StopLoss.HasValue && signal.Type is OrderType.Market or OrderType.Limit)
                    {
                        orderRequest["stopLossOnFill"] = new JObject
                        {
                            ["price"] = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture)
                        };
                    }

                    // Attach take-profit on fill
                    if (signal.TakeProfit.HasValue)
                    {
                        orderRequest["takeProfitOnFill"] = new JObject
                        {
                            ["price"] = signal.TakeProfit.Value.ToString(CultureInfo.InvariantCulture)
                        };
                    }

                    // Attach a trailing stop on fill.
                    //
                    // This provider DECLARED TrailingStop and implemented none of it —
                    // the string "Trail" appeared nowhere else in the file — while the
                    // dashboard gates its trailing distance and mode fields on that
                    // flag. So the controls rendered, the user set a trail, and the
                    // order went out with nothing attached.
                    //
                    // OANDA takes a price DISTANCE, not a level and not a percentage.
                    if (signal.TrailStopValue is > 0 && signal.TrailStopMode is { } trailMode)
                    {
                        double? distance = trailMode switch
                        {
                            TrailMode.Amount  => signal.TrailStopValue,
                            // A percentage needs a price to be a percentage OF. The
                            // only one available at submission is a limit/stop level;
                            // a market order has none here, and inventing a reference
                            // would silently place a trail at the wrong distance.
                            TrailMode.Percent => signal.Price is > 0
                                ? signal.Price.Value * signal.TrailStopValue.Value / 100.0
                                : (double?)null,
                            _ => null,
                        };

                        if (distance is > 0)
                        {
                            orderRequest["trailingStopLossOnFill"] = new JObject
                            {
                                ["distance"]    = distance.Value.ToString("0.#####", CultureInfo.InvariantCulture),
                                ["timeInForce"] = "GTC"
                            };
                        }
                        else
                        {
                            // Refusing beats attaching a wrong trail, and saying so
                            // beats refusing silently.
                            return "ORDER_FAILED:a percentage trailing stop needs a reference price on this "
                                 + "provider — set the trail as an amount, or use a limit order";
                        }
                    }

                    var body = new JObject { ["order"] = orderRequest };
                    var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync($"{_restUrl}/accounts/{_accountId}/orders", content);
                    var respStr = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return $"ORDER_FAILED:{respStr}";

                    var json = JObject.Parse(respStr);

                    // Check for filled trades or created orders
                    var fillTxnId = json["orderFillTransaction"]?["id"]?.ToString();
                    if (!string.IsNullOrEmpty(fillTxnId)) return fillTxnId;

                    var createTxnId = json["orderCreateTransaction"]?["id"]?.ToString();
                    return createTxnId ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Oanda order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                var response = await _rateLimiter.ExecuteAsync(async () =>
                    await _httpClient.PutAsync($"{_restUrl}/accounts/{_accountId}/orders/{orderId}/cancel",
                        new StringContent("{}", Encoding.UTF8, "application/json")));
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) =>
            Task.FromResult(Math.Clamp(leverage, 1, MaxLeverage));

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>Convert symbol formats to OANDA's underscore format (EUR_USD).</summary>
        private static string FormatInstrument(string symbol)
        {
            // "EUR/USD" -> "EUR_USD", "EURUSD" -> "EUR_USD", "EUR-USD" -> "EUR_USD".
            // SymbolFormat knows the major quotes (its list was extended with
            // JPY/AUD/CAD/CHF/NZD specifically for this site); the 3/3 fallback
            // stays for the exotic six-char pairs it cannot know (USD_SEK,
            // EUR_TRY, USD_MXN …) — dropping it would send those to OANDA
            // without a separator. The old inline version was ALSO culture-
            // sensitive (.ToUpper) and did nothing for 7-char pairs.
            var s = SymbolFormat.Underscored(symbol);
            if (!s.Contains('_') && s.Length == 6)
                s = s[..3] + "_" + s[3..];
            return s;
        }

        private static string MapGranularity(string tf) => tf switch
        {
            "1m"  => "M1",
            "5m"  => "M5",
            "15m" => "M15",
            "30m" => "M30",
            "1h"  => "H1",
            "2h"  => "H2",
            "4h"  => "H4",
            "6h"  => "H6",
            "8h"  => "H8",
            "12h" => "H12",
            "1d"  => "D",
            "1w"  => "W",
            "1M"  => "M",
            _     => "H1"
        };

        private static OrderType MapOandaOrderType(string type) => type.ToUpperInvariant() switch
        {
            "LIMIT"           => OrderType.Limit,
            "STOP"            => OrderType.StopMarket,
            "MARKET_IF_TOUCHED" => OrderType.TakeProfitMarket,
            _                 => OrderType.Market
        };

        private static TimeSpan MapTimeframeToTimeSpan(string tf) => tf switch
        {
            "1m"  => TimeSpan.FromMinutes(1),
            "5m"  => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h"  => TimeSpan.FromHours(1),
            "2h"  => TimeSpan.FromHours(2),
            "4h"  => TimeSpan.FromHours(4),
            "6h"  => TimeSpan.FromHours(6),
            "8h"  => TimeSpan.FromHours(8),
            "12h" => TimeSpan.FromHours(12),
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
                _txnStreamCts?.Dispose();
                _orderUpdateSubject?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
