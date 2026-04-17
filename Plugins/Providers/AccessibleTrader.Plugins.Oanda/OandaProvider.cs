using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
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
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.Leverage | ProviderCapabilities.Shorting | ProviderCapabilities.TrailingStop;

        public override bool SupportsMarginTrading  => true;
        public override bool SupportsFuturesTrading => false;
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

            if (config.TryGetValue("Environment", out var env) && env.Equals("live", StringComparison.OrdinalIgnoreCase))
            {
                _isPractice = false;
                _restUrl = "https://api-fxtrade.oanda.com/v3";
                _streamUrl = "https://stream-fxtrade.oanda.com/v3";
            }

            if (!string.IsNullOrEmpty(_accessToken))
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                _httpClient.DefaultRequestHeaders.Add("Accept-Datetime-Format", "UNIX");

                _streamClient.DefaultRequestHeaders.Clear();
                _streamClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
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

            _streamCts?.Cancel();
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

                                // Try parse timestamp from OANDA
                                var tsStr = json["time"]?.ToString();
                                if (!string.IsNullOrEmpty(tsStr) && double.TryParse(tsStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double unixTs))
                                    now = DateTimeOffset.FromUnixTimeSeconds((long)unixTs).UtcDateTime;

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
                        catch { /* malformed line */ }
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
                try
                {
                    string url = $"{_streamUrl}/accounts/{_accountId}/transactions/stream";
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    var response = await _streamClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var reader = new StreamReader(stream);

                    retryCount = 0;

                    while (!ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(ct);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var json = JObject.Parse(line);
                            var type = json["type"]?.ToString();

                            if (type == "ORDER_FILL")
                            {
                                var tradeId = json["tradeOpened"]?["tradeID"]?.ToString()
                                    ?? json["tradesClosed"]?.FirstOrDefault()?["tradeID"]?.ToString()
                                    ?? json["id"]?.ToString() ?? "";
                                var instrument = json["instrument"]?.ToString() ?? "";
                                double units = json["units"]?.Value<double>() ?? 0;
                                double price = json["price"]?.Value<double>() ?? 0;

                                _orderUpdateSubject.OnNext(new OrderUpdate(
                                    tradeId, instrument,
                                    units >= 0 ? OrderSide.Buy : OrderSide.Sell,
                                    Math.Abs(units), price, 0,
                                    OrderStatus.Filled, false, false, DateTime.UtcNow));
                            }
                            else if (type == "ORDER_CANCEL")
                            {
                                var orderId = json["orderID"]?.ToString() ?? "";
                                _orderUpdateSubject.OnNext(new OrderUpdate(
                                    orderId, "", OrderSide.Buy, 0, 0, 0,
                                    OrderStatus.Cancelled, false, false, DateTime.UtcNow));
                            }
                        }
                        catch { /* malformed */ }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
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

        public override async Task DisconnectAsync()
        {
            _streamCts?.Cancel();
            _txnStreamCts?.Cancel();
            _txnStreamCts = null;
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

            var instrument = FormatInstrument(request.Symbol);
            string granularity = MapGranularity(request.Timeframe);
            int count = Math.Min(request.Limit, 5000);

            string url = $"{_restUrl}/instruments/{instrument}/candles?granularity={granularity}&count={count}&price=M";

            if (request.Since.HasValue)
                url += $"&from={DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
            if (request.Until.HasValue)
                url += $"&to={DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);
                    var candles = json["candles"] as JArray;
                    if (candles == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = candles
                        .Where(c => c["complete"]?.Value<bool>() != false || candles.Last == c)
                        .Select(c =>
                        {
                            var mid = c["mid"];
                            var tsStr = c["time"]?.ToString() ?? "0";
                            DateTime date;
                            if (double.TryParse(tsStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double unixTs))
                                date = DateTimeOffset.FromUnixTimeSeconds((long)unixTs).UtcDateTime;
                            else
                                DateTime.TryParse(tsStr, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);

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
            catch { return new List<string>(); }
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
            catch { return new(); }
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{_restUrl}/accounts/{_accountId}/openPositions");
                    var json = JObject.Parse(response);
                    var positions = json["positions"] as JArray;
                    if (positions == null) return new List<Position>();

                    return positions.Select(p =>
                    {
                        var longUnits = p["long"]?["units"]?.Value<double>() ?? 0;
                        var shortUnits = Math.Abs(p["short"]?["units"]?.Value<double>() ?? 0);
                        double units = longUnits > 0 ? longUnits : shortUnits;
                        double avgPrice = longUnits > 0
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
            catch { return new(); }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            try
            {
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
            catch { return new(); }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
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

                    // Attach stop-loss on fill
                    if (signal.StopLoss.HasValue && signal.Type == OrderType.Market)
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
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
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
            // "EUR/USD" -> "EUR_USD", "EURUSD" -> "EUR_USD", "EUR-USD" -> "EUR_USD"
            symbol = symbol.Replace("/", "_").Replace("-", "_").ToUpper();
            if (!symbol.Contains('_') && symbol.Length == 6)
                symbol = symbol[..3] + "_" + symbol[3..];
            return symbol;
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

        private static OrderType MapOandaOrderType(string type) => type.ToUpper() switch
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
