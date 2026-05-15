using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
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

        public override bool SupportsMarginTrading  => false;
        public override bool SupportsFuturesTrading => false;
        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => false;
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
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();
            if (_currentSymbol == symbol && _currentTimeframe == timeframe) return;

            // Cancel existing stream
            _streamCts?.Cancel();
            _currentSymbol = symbol;
            _currentTimeframe = timeframe;
            _lastCandle = null;
            _lastCandleStart = null;

            _streamCts = new CancellationTokenSource();
            _ = Task.Run(() => StreamEventsAsync(symbol, _streamCts.Token));
        }

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
                        var line = await reader.ReadLineAsync(ct);
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
                                    _lastCandleStart = now;
                                    _lastCandle = new Ohlcv(now, price, price, price, price, vol);
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

        public override async Task DisconnectAsync()
        {
            _streamCts?.Cancel();
            _currentSymbol = null;
            _currentTimeframe = null;
            _lastCandle = null;
            _lastCandleStart = null;
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

            string start = request.Since.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.ToString("yyyy-MM-dd HH:mm")
                : DateTime.UtcNow.AddDays(-5).ToString("yyyy-MM-dd HH:mm");
            string end = request.Until.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.ToString("yyyy-MM-dd HH:mm")
                : DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");

            string url = $"{_baseUrl}/markets/timesales?symbol={Uri.EscapeDataString(request.Symbol)}&interval={interval}&start={start}&end={end}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);
            var series = json["series"]?["data"];

            if (series == null) return (new List<Ohlcv>(), new List<(long, double)>());

            // Handle single item vs array
            JArray items = series is JArray arr ? arr : new JArray { series };

            var ohlcvList = items.Select(item =>
            {
                DateTime.TryParse(item["timestamp"]?.ToString(), null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date);
                return new Ohlcv(
                    date,
                    item["open"]?.Value<double>() ?? 0,
                    item["high"]?.Value<double>() ?? 0,
                    item["low"]?.Value<double>() ?? 0,
                    item["close"]?.Value<double>() ?? 0,
                    item["volume"]?.Value<double>() ?? 0);
            }).OrderBy(x => x.Date).ToList();

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
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.ToString("yyyy-MM-dd")
                : DateTime.UtcNow.AddYears(-2).ToString("yyyy-MM-dd");
            string end = request.Until.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.ToString("yyyy-MM-dd")
                : DateTime.UtcNow.ToString("yyyy-MM-dd");

            string url = $"{_baseUrl}/markets/history?symbol={Uri.EscapeDataString(request.Symbol)}&interval={interval}&start={start}&end={end}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);
            var history = json["history"]?["day"];

            if (history == null) return (new List<Ohlcv>(), new List<(long, double)>());

            JArray items = history is JArray arr ? arr : new JArray { history };

            var ohlcvList = items.Select(item =>
            {
                DateTime.TryParse(item["date"]?.ToString(), null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date);
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
            catch { return (new(), new()); }
        }

        // ── ITradingProvider ────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            try
            {
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
            catch (Exception ex)
            {
                _errorStream.OnNext($"Tradier GetBalancesAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{_baseUrl}/accounts/{_accountId}/positions");
                    var json = JObject.Parse(response);
                    var positions = json["positions"]?["position"];
                    if (positions == null) return new List<Position>();

                    JArray items = positions is JArray arr ? arr : new JArray { positions };
                    return items.Select(p => new Position(
                        p["symbol"]?.ToString() ?? "",
                        Math.Abs(p["quantity"]?.Value<double>() ?? 0),
                        p["cost_basis"]?.Value<double>() ?? 0,
                        (p["quantity"]?.Value<double>() ?? 0) * (p["last_price"]?.Value<double>() ?? 0),
                        0 // Tradier doesn't provide unrealized P&L directly in positions
                    )).ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Tradier GetPositionsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{_baseUrl}/accounts/{_accountId}/orders");
                    var json = JObject.Parse(response);
                    var orders = json["orders"]?["order"];
                    if (orders == null) return new List<OpenOrder>();

                    JArray items = orders is JArray arr ? arr : new JArray { orders };
                    return items
                        .Where(o => o["status"]?.ToString() == "open" || o["status"]?.ToString() == "pending" || o["status"]?.ToString() == "partially_filled")
                        .Where(o => symbol == null || o["symbol"]?.ToString() == symbol)
                        .Select(o => new OpenOrder(
                            o["id"]?.ToString() ?? "",
                            o["symbol"]?.ToString() ?? "",
                            o["side"]?.ToString() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                            MapTradierOrderType(o["type"]?.ToString() ?? "market"),
                            o["quantity"]?.Value<double>() ?? 0,
                            o["price"]?.Value<double>() ?? 0,
                            o["status"]?.ToString() ?? ""
                        )).ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Tradier GetOpenOrdersAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    bool isOption = string.Equals(signal.SubType, "Options", StringComparison.OrdinalIgnoreCase);

                    var postData = new Dictionary<string, string>
                    {
                        ["class"] = isOption ? "option" : "equity",
                        ["duration"] = "gtc",
                        ["side"] = signal.Side == OrderSide.Buy ? "buy" : "sell",
                        ["quantity"] = ((int)signal.Quantity).ToString()
                    };

                    if (isOption)
                        postData["option_symbol"] = signal.Symbol;
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
                _orderUpdateSubject?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
