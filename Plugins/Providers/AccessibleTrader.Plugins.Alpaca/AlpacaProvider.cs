using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
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

namespace AccessibleTrader.Plugins.Alpaca
{
    public class AlpacaProvider : BaseMarketDataProvider, ITradingProvider, IOrderBookProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private string? _apiSecret;
        private const string StockDataUrl = "https://data.alpaca.markets/v2";
        private const string CryptoDataUrl = "https://data.alpaca.markets/v1beta3/crypto";
        private string _tradingBaseUrl = "https://paper-api.alpaca.markets/v2";

        // Rate limiter: Alpaca allows 200 requests/minute
        private readonly RateLimiter _rateLimiter = new(200, TimeSpan.FromMinutes(1));

        // Order update stream
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Order book streaming
        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();

        // Live data WebSocket
        private ReconnectingWebSocket? _dataWs;

        // Trading update WebSocket
        private ReconnectingWebSocket? _tradeWs;

        private string? _currentSymbol;
        private string? _currentTimeframe;
        private string? _currentMarket;

        public override string Name => "Alpaca";
        public override string Description => "Alpaca Stock & Crypto Market Integration";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Stock, MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment { get; } = ProviderEnvironment.Paper;
        public override int MaxBarsPerRequest => 10000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.L2 | ProviderCapabilities.Brackets;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.OneHour, StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public bool IsConnected => IsConfigured;

        public override bool SupportsMarginTrading  => false;
        public override bool SupportsFuturesTrading => false;
        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 1.0;

        public AlpacaProvider()
        {
            // Phase 4 Track B2 — allow-listed to Alpaca's REST hosts only.
            // data.alpaca.markets covers both stock + crypto data; api.alpaca.markets
            // is the live trading REST endpoint; paper-api.alpaca.markets is the
            // paper-trading REST endpoint. stream.data.alpaca.markets (WS) and
            // the two WS trading endpoints use ReconnectingWebSocket.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId:   "Alpaca",
                allowedHosts: new[]
                {
                    "data.alpaca.markets",
                    "api.alpaca.markets",
                    "paper-api.alpaca.markets",
                });
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
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;
            if (config.TryGetValue("ApiSecret", out var secret)) _apiSecret = secret;

            if (config.TryGetValue("Environment", out var env) && env == "Live")
                _tradingBaseUrl = "https://api.alpaca.markets/v2";

            // NOTE: phase 4 Track B removed the DefaultRequestHeaders injection
            // that used to live here. Headers are now applied per-request via
            // ApplyAlpacaHeadersAsync which does a sign-time credential
            // checkout. Configure still populates _apiKey / _apiSecret so the
            // no-host-bridge fallback path (unit tests / CLI) continues to work.
        }

        // Sign-time credential checkout (phase 4 Track B). Prefers the
        // PluginHostServices.ApiKeys bridge; falls back to Configure-populated
        // fields so unit tests / CLI runs continue to work.
        private async Task<(string Key, string Secret)> CheckoutAlpacaCredentialsAsync()
        {
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("Alpaca").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("Alpaca: no active API key configured.");
                return (checkout.Key, checkout.Secret);
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                throw new InvalidOperationException("Alpaca: no API credentials configured.");
            return (_apiKey!, _apiSecret!);
        }

        // Applies APCA-API-KEY-ID / APCA-API-SECRET-KEY headers to the shared
        // _httpClient from a fresh checkout. The rate limiter serializes REST
        // calls so DefaultRequestHeaders mutation is safe within the current
        // usage pattern.
        private async Task ApplyAlpacaHeadersAsync()
        {
            var (apiKey, apiSecret) = await CheckoutAlpacaCredentialsAsync().ConfigureAwait(false);
            _httpClient.DefaultRequestHeaders.Remove("APCA-API-KEY-ID");
            _httpClient.DefaultRequestHeaders.Remove("APCA-API-SECRET-KEY");
            _httpClient.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKey);
            _httpClient.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecret);
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured");
            try
            {
                await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                var response = await _httpClient.GetAsync($"{_tradingBaseUrl}/account");
                if (response.IsSuccessStatusCode)
                    return (true, "API key validated successfully");
                var body = await response.Content.ReadAsStringAsync();
                return (false, $"Key validation failed ({response.StatusCode}): {body}");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        public override Task EnsureConnectedAsync()
        {
            if (IsConfigured) _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            if (_currentSymbol == symbol && _currentTimeframe == timeframe && _dataWs?.IsConnected == true) return;

            _currentSymbol    = symbol;
            _currentTimeframe = timeframe;
            _currentMarket    = market;

            // Set up data stream with auto-reconnect
            if (_dataWs != null) { await _dataWs.DisconnectAsync(); _dataWs.Dispose(); }

            bool isCrypto = market.Contains("Crypto", StringComparison.OrdinalIgnoreCase);
            string wsUrl = isCrypto
                ? "wss://stream.data.alpaca.markets/v1beta3/crypto/us"
                : "wss://stream.data.alpaca.markets/v2/stocks";

            var cleanSymbol = CleanSymbol(symbol);

            _dataWs = new ReconnectingWebSocket(wsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    try
                    {
                        var (apiKey, apiSecret) = await CheckoutAlpacaCredentialsAsync().ConfigureAwait(false);
                        var auth = new JObject { ["action"] = "auth", ["key"] = apiKey, ["secret"] = apiSecret };
                        await ws.SendAsync(auth.ToString());
                    }
                    catch (Exception ex)
                    {
                        _errorStream.OnNext($"Alpaca data WS auth failed: {ex.Message}");
                        return;
                    }
                    // Small delay to allow auth to process
                    await Task.Delay(500);
                    var sub = new JObject { ["action"] = "subscribe", ["bars"] = new JArray { cleanSymbol } };
                    await ws.SendAsync(sub.ToString());
                })
                .OnMessage(HandleDataMessage)
                .OnError(err => _errorStream.OnNext($"Alpaca data: {err}"))
                .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

            await _dataWs.ConnectAsync();
            _connectionStateStream.OnNext(ConnectionState.Connected);

            // Start trading stream if not already running
            if (IsConfigured && _tradeWs?.IsConnected != true)
                await StartTradeStreamAsync();
        }

        private void HandleDataMessage(string msg)
        {
            try
            {
                var arr = JArray.Parse(msg);
                foreach (var item in arr)
                {
                    string? ev = item["T"]?.ToString();
                    if (ev == "b")
                    {
                        var bar = new Ohlcv(
                            item["t"]?.Value<DateTime>().ToUniversalTime() ?? DateTime.UtcNow,
                            item["o"]?.Value<double>() ?? 0,
                            item["h"]?.Value<double>() ?? 0,
                            item["l"]?.Value<double>() ?? 0,
                            item["c"]?.Value<double>() ?? 0,
                            item["v"]?.Value<double>() ?? 0);
                        if (bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0
                            && (bar.Date == DateTime.MinValue || bar.Date == DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime))
                            continue;
                        _liveStream.OnNext(bar);
                    }
                }
            }
            catch { /* malformed */ }
        }

        private async Task StartTradeStreamAsync()
        {
            if (_tradeWs != null) { await _tradeWs.DisconnectAsync(); _tradeWs.Dispose(); }

            string wsUrl = _tradingBaseUrl.Contains("paper")
                ? "wss://paper-api.alpaca.markets/stream"
                : "wss://api.alpaca.markets/stream";

            _tradeWs = new ReconnectingWebSocket(wsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    try
                    {
                        var (apiKey, apiSecret) = await CheckoutAlpacaCredentialsAsync().ConfigureAwait(false);
                        var auth = new JObject
                        {
                            ["action"] = "authenticate",
                            ["data"]   = new JObject { ["key_id"] = apiKey, ["secret_key"] = apiSecret }
                        };
                        await ws.SendAsync(auth.ToString());
                    }
                    catch (Exception ex)
                    {
                        _errorStream.OnNext($"Alpaca trade WS auth failed: {ex.Message}");
                        return;
                    }
                    await Task.Delay(500);
                    var listen = new JObject
                    {
                        ["action"] = "listen",
                        ["data"]   = new JObject { ["streams"] = new JArray { "trade_updates" } }
                    };
                    await ws.SendAsync(listen.ToString());
                })
                .OnMessage(HandleTradeMessage)
                .OnError(err => _errorStream.OnNext($"Alpaca trade stream: {err}"));

            await _tradeWs.ConnectAsync();
        }

        private void HandleTradeMessage(string msg)
        {
            try
            {
                var json = JObject.Parse(msg);
                if (json["stream"]?.ToString() == "trade_updates")
                {
                    var data = json["data"];
                    if (data == null) return;
                    var evt   = data["event"]?.ToString();
                    var order = data["order"];
                    if (order == null) return;

                    var status = evt switch
                    {
                        "fill"         => OrderStatus.Filled,
                        "partial_fill" => OrderStatus.PartialFill,
                        "canceled"     => OrderStatus.Cancelled,
                        "rejected"     => OrderStatus.Rejected,
                        _              => OrderStatus.Triggered
                    };

                    _orderUpdateSubject.OnNext(new OrderUpdate(
                        order["id"]?.ToString() ?? "",
                        order["symbol"]?.ToString() ?? "",
                        order["side"]?.ToString() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                        double.TryParse(order["filled_qty"]?.ToString(), out double fq) ? fq : 0,
                        double.TryParse(order["filled_avg_price"]?.ToString(), out double fp) ? fp : 0,
                        double.TryParse(order["qty"]?.ToString(), out double tq) ? tq - fq : 0,
                        status, false, false, DateTime.UtcNow));
                }
            }
            catch { /* malformed */ }
        }

        public override async Task DisconnectAsync()
        {
            if (_dataWs != null) { await _dataWs.DisconnectAsync(); _dataWs.Dispose(); _dataWs = null; }
            if (_tradeWs != null) { await _tradeWs.DisconnectAsync(); _tradeWs.Dispose(); _tradeWs = null; }

            _currentSymbol    = null;
            _currentTimeframe = null;
            _currentMarket    = null;

            // Drop references to the API key/secret so a crash dump after
            // disconnect can't recover them.
            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            bool isCrypto = request.Market.Contains("Crypto", StringComparison.OrdinalIgnoreCase);
            var symbol = CleanSymbol(request.Symbol);
            var timeframe = MapTimeframe(request.Timeframe);
            int limit = Math.Min(request.Limit, 1000);

            string url = isCrypto
                ? $"{CryptoDataUrl}/us/bars?symbols={symbol}&timeframe={timeframe}"
                : $"{StockDataUrl}/stocks/{symbol}/bars?timeframe={timeframe}";
            url += $"&limit={limit}";
            if (request.Since.HasValue) url += $"&start={DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            if (request.Until.HasValue) url += $"&end={DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).ToString("yyyy-MM-ddTHH:mm:ssZ")}";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);
                    JArray? bars = isCrypto ? json["bars"]?[symbol] as JArray : json["bars"] as JArray;
                    if (bars == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = bars.Select(b => new Ohlcv(
                        b["t"]?.Value<DateTime>().ToUniversalTime() ?? DateTime.MinValue,
                        b["o"]?.Value<double>() ?? 0,
                        b["h"]?.Value<double>() ?? 0,
                        b["l"]?.Value<double>() ?? 0,
                        b["c"]?.Value<double>() ?? 0,
                        b["v"]?.Value<double>() ?? 0)).OrderBy(x => x.Date).ToList();
                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch { return (new List<Ohlcv>(), new List<(long, double)>()); }
        }

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            if (!IsConfigured) return new List<string>();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string url = market == MarketType.Crypto
                        ? "https://api.alpaca.markets/v2/assets?asset_class=crypto&status=active"
                        : "https://api.alpaca.markets/v2/assets?asset_class=us_equity&status=active&tradable=true";

                    var (apiKey, apiSecret) = await CheckoutAlpacaCredentialsAsync().ConfigureAwait(false);
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("APCA-API-KEY-ID",     apiKey);
                    req.Headers.Add("APCA-API-SECRET-KEY", apiSecret);

                    var response = await _httpClient.SendAsync(req);
                    response.EnsureSuccessStatusCode();

                    var json   = await response.Content.ReadAsStringAsync();
                    var assets = JArray.Parse(json);
                    return assets
                        .Select(a => a["symbol"]?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .OrderBy(s => s)
                        .ToList();
                });
            }
            catch { return new List<string>(); }
        }

        public override async Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => new List<string> { "Spot" };
        public override async Task<List<string>> GetSupportedTimeframesAsync() => NativelySupportedTimeframes;

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            if (!IsConfigured) return (new(), new());
            var cleanSymbol = CleanSymbol(symbol);

            // Determine if this is a crypto or stock symbol
            bool isCrypto = _currentMarket?.Contains("Crypto", StringComparison.OrdinalIgnoreCase) == true;

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    if (isCrypto)
                    {
                        string url = $"{CryptoDataUrl}/us/orderbooks?symbols={cleanSymbol}";
                        var response = await _httpClient.GetStringAsync(url);
                        var json = JObject.Parse(response);
                        var book = json["orderbooks"]?[cleanSymbol];
                        if (book == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                        var bids = (book["b"] as JArray)?.Take(limit)
                            .Select(b => new OrderBookEntry(double.Parse(b["p"]!.ToString()), double.Parse(b["s"]!.ToString())))
                            .ToList() ?? new();
                        var asks = (book["a"] as JArray)?.Take(limit)
                            .Select(a => new OrderBookEntry(double.Parse(a["p"]!.ToString()), double.Parse(a["s"]!.ToString())))
                            .ToList() ?? new();
                        return (bids, asks);
                    }
                    else
                    {
                        // Stock: use latest quote (NBBO) as a 1-level order book
                        string url = $"{StockDataUrl}/stocks/{cleanSymbol}/quotes/latest";
                        var response = await _httpClient.GetStringAsync(url);
                        var json = JObject.Parse(response);
                        var quote = json["quote"];
                        if (quote == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                        double bidPx  = quote["bp"]?.Value<double>() ?? 0;
                        double bidSz  = quote["bs"]?.Value<double>() ?? 0;
                        double askPx  = quote["ap"]?.Value<double>() ?? 0;
                        double askSz  = quote["as"]?.Value<double>() ?? 0;

                        var bids = bidPx > 0 ? new List<OrderBookEntry> { new(bidPx, bidSz) } : new List<OrderBookEntry>();
                        var asks = askPx > 0 ? new List<OrderBookEntry> { new(askPx, askSz) } : new List<OrderBookEntry>();
                        return (bids, asks);
                    }
                });
            }
            catch { return (new(), new()); }
        }

        // ── IOrderBookProvider ──────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) => _orderBookSubject.AsObservable();

        // ── ITradingProvider ─────────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConfigured) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    var response = await _httpClient.GetStringAsync($"{_tradingBaseUrl}/account");
                    var json = JObject.Parse(response);
                    double equity      = json["equity"]?.Value<double>() ?? 0;
                    double cash        = json["cash"]?.Value<double>() ?? 0;
                    double buyingPower = json["buying_power"]?.Value<double>() ?? 0;
                    return new List<Balance>
                    {
                        new("USD", cash, equity - cash),
                        new("Buying Power", buyingPower, 0)
                    };
                });
            }
            catch { return new(); }
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConfigured) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    var response = await _httpClient.GetStringAsync($"{_tradingBaseUrl}/positions");
                    var arr = JArray.Parse(response);
                    return arr.Select(p => new Position(
                        p["symbol"]?.ToString() ?? "",
                        p["qty"]?.Value<double>() ?? 0,
                        p["avg_entry_price"]?.Value<double>() ?? 0,
                        p["market_value"]?.Value<double>() ?? 0,
                        p["unrealized_pl"]?.Value<double>() ?? 0
                    )).ToList();
                });
            }
            catch { return new(); }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConfigured) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string url = $"{_tradingBaseUrl}/orders?status=open";
                    if (!string.IsNullOrEmpty(symbol)) url += $"&symbols={symbol}";
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    var response = await _httpClient.GetStringAsync(url);
                    var arr = JArray.Parse(response);
                    return arr.Select(o => new OpenOrder(
                        o["id"]?.ToString() ?? "",
                        o["symbol"]?.ToString() ?? "",
                        o["side"]?.ToString() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                        MapAlpacaOrderType(o["type"]?.ToString() ?? "market"),
                        o["qty"]?.Value<double>() ?? 0,
                        o["limit_price"]?.Value<double>() ?? 0,
                        o["status"]?.ToString() ?? "",
                        o["stop_price"]?.Value<double>(),
                        null
                    )).ToList();
                });
            }
            catch { return new(); }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConfigured) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var body = new JObject
                    {
                        ["symbol"]        = signal.Symbol,
                        ["qty"]           = signal.Quantity.ToString("F4"),
                        ["side"]          = signal.Side == OrderSide.Buy ? "buy" : "sell",
                        ["time_in_force"] = "gtc"
                    };

                    // Determine order type
                    if (signal.Type == OrderType.StopMarket && signal.StopLoss.HasValue)
                    {
                        body["type"] = "stop";
                        body["stop_price"] = signal.StopLoss.Value;
                    }
                    else if (signal.Type == OrderType.StopLimit && signal.StopLoss.HasValue && signal.Price.HasValue)
                    {
                        body["type"] = "stop_limit";
                        body["stop_price"] = signal.StopLoss.Value;
                        body["limit_price"] = signal.Price.Value;
                    }
                    else if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                    {
                        body["type"] = "limit";
                        body["limit_price"] = signal.Price.Value;
                    }
                    else
                    {
                        body["type"] = "market";
                    }

                    // Bracket orders with SL/TP
                    if (signal.StopLoss.HasValue || signal.TakeProfit.HasValue)
                    {
                        body["order_class"] = "bracket";
                        if (signal.StopLoss.HasValue)
                            body["stop_loss"] = new JObject { ["stop_price"] = signal.StopLoss.Value };
                        if (signal.TakeProfit.HasValue)
                            body["take_profit"] = new JObject { ["limit_price"] = signal.TakeProfit.Value };
                    }

                    if (!string.IsNullOrEmpty(signal.ClientOid))
                        body["client_order_id"] = signal.ClientOid;

                    var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    var response = await _httpClient.PostAsync($"{_tradingBaseUrl}/orders", content);
                    var responseStr = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) return $"ORDER_FAILED:{responseStr}";
                    var json = JObject.Parse(responseStr);
                    return json["id"]?.ToString() ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConfigured) return false;
            try
            {
                var response = await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    return await _httpClient.DeleteAsync($"{_tradingBaseUrl}/orders/{orderId}");
                });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        private static OrderType MapAlpacaOrderType(string type) => type switch
        {
            "limit"       => OrderType.Limit,
            "stop"        => OrderType.StopMarket,
            "stop_limit"  => OrderType.StopLimit,
            _             => OrderType.Market
        };

        private string MapTimeframe(string tf) => tf switch
        {
            "1m"  => "1Min",
            "5m"  => "5Min",
            "15m" => "15Min",
            "1h"  => "1Hour",
            "1d"  => "1Day",
            "1w"  => "1Week",
            "1M"  => "1Month",
            _     => "1Hour"
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
                _dataWs?.Dispose();
                _tradeWs?.Dispose();
                _orderUpdateSubject?.Dispose();
                _orderBookSubject?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
