using System;
using System.Collections.Generic;
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

namespace AccessibleTrader.Plugins.Coinbase
{
    public class CoinbaseProvider : BaseMarketDataProvider, ITradingProvider, IOrderBookProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private string? _apiSecret;
        private const string BaseUrl = "https://api.coinbase.com/api/v3/brokerage";

        // Rate limiter: Coinbase Advanced Trade allows ~30 requests/second
        private readonly RateLimiter _rateLimiter = new(30, TimeSpan.FromSeconds(1));

        // Order update stream
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Order book streaming
        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();

        private string? _currentSymbol;
        private string? _currentTimeframe;
        private Ohlcv? _lastCandle;
        private ReconnectingWebSocket? _ws;
        private DateTime? _lastCandleStart;

        public override string Name => "Coinbase";
        public override string Description => "Coinbase Advanced Trade Integration";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 300;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.L2;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.OneHour, StandardTimeframes.SixHours, StandardTimeframes.OneDay
        };

        public bool IsConnected => IsConfigured;
        public override bool SupportsMarginTrading  => false;
        public override bool SupportsFuturesTrading => false;
        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => false;
        public override double MaxLeverage          => 1.0;

        public CoinbaseProvider()
        {
            _httpClient = new HttpClient();
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

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured");
            try
            {
                string path = "/api/v3/brokerage/accounts";
                AddAuthHeaders("GET", path);
                var response = await _httpClient.GetAsync($"https://api.coinbase.com{path}");
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
            if (_currentSymbol == symbol && _currentTimeframe == timeframe && _ws?.IsConnected == true) return;

            _currentSymbol    = symbol;
            _currentTimeframe = timeframe;

            if (_ws != null)
            {
                await _ws.DisconnectAsync();
                _ws.Dispose();
            }

            var productId = symbol.Replace("/", "-").ToUpper();

            _ws = new ReconnectingWebSocket(
                "wss://advanced-trade-ws.coinbase.com",
                heartbeatInterval: TimeSpan.FromSeconds(30),
                reconnectBaseDelay: TimeSpan.FromSeconds(3))
                .OnConnected(async ws =>
                {
                    var jwt = GenerateJwt("GET", "advanced-trade-ws.coinbase.com");

                    var subMsg = new JObject
                    {
                        ["type"] = "subscribe",
                        ["product_ids"] = new JArray { productId },
                        ["channel"] = "ticker",
                        ["jwt"] = jwt
                    };
                    await ws.SendAsync(subMsg.ToString());

                    // Subscribe to level2 for order book updates
                    var l2Msg = new JObject
                    {
                        ["type"] = "subscribe",
                        ["product_ids"] = new JArray { productId },
                        ["channel"] = "level2",
                        ["jwt"] = jwt
                    };
                    await ws.SendAsync(l2Msg.ToString());

                    // Subscribe to user channel for order updates
                    var userMsg = new JObject
                    {
                        ["type"] = "subscribe",
                        ["product_ids"] = new JArray { productId },
                        ["channel"] = "user",
                        ["jwt"] = jwt
                    };
                    await ws.SendAsync(userMsg.ToString());
                })
                .OnMessage(HandleWebSocketMessage)
                .OnError(err => _errorStream.OnNext($"Coinbase WS: {err}"))
                .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

            await _ws.ConnectAsync();
        }

        private void HandleWebSocketMessage(string jsonStr)
        {
            try
            {
                var msg = JObject.Parse(jsonStr);
                var channel = msg["channel"]?.ToString();

                if (channel == "ticker")
                {
                    var events = msg["events"] as JArray;
                    var ticker = events?.FirstOrDefault()?["tickers"]?.FirstOrDefault();
                    if (ticker != null)
                    {
                        double price = double.Parse(ticker["price"]?.ToString() ?? "0");
                        if (price <= 0) return;

                        if (_lastCandle.HasValue && _lastCandleStart.HasValue)
                        {
                            var now = DateTime.UtcNow;
                            var interval = MapTimeframeToTimeSpan(_currentTimeframe ?? "1h");

                            if (now >= _lastCandleStart.Value.Add(interval))
                            {
                                var newStart = _lastCandleStart.Value;
                                while (now >= newStart.Add(interval)) newStart = newStart.Add(interval);
                                _lastCandleStart = newStart;
                                _lastCandle = new Ohlcv(newStart, price, price, price, price, 0);
                            }
                            else
                            {
                                var tick = new Ohlcv(now, price, price, price, price, 0);
                                _lastCandle = _lastCandle.Value.UpdateWith(tick);
                            }
                            _liveStream.OnNext(_lastCandle.Value);
                        }
                    }
                }
                else if (channel == "l2_data" || channel == "level2")
                {
                    var events = msg["events"] as JArray;
                    if (events != null)
                    {
                        foreach (var ev in events)
                        {
                            var updates = ev["updates"] as JArray;
                            if (updates == null || !updates.Any()) continue;

                            var bids = new List<OrderBookEntry>();
                            var asks = new List<OrderBookEntry>();
                            foreach (var u in updates)
                            {
                                var side = u["side"]?.ToString();
                                double px = double.TryParse(u["price_level"]?.ToString(), out double p) ? p : 0;
                                double qty = double.TryParse(u["new_quantity"]?.ToString(), out double q) ? q : 0;
                                if (side == "bid") bids.Add(new OrderBookEntry(px, qty));
                                else if (side == "offer") asks.Add(new OrderBookEntry(px, qty));
                            }
                            if (bids.Any() || asks.Any())
                            {
                                var productId = ev["product_id"]?.ToString() ?? _currentSymbol ?? "";
                                _orderBookSubject.OnNext(new OrderBookUpdate(productId, bids, asks, 0, DateTime.UtcNow));
                            }
                        }
                    }
                }
                else if (channel == "user")
                {
                    var events = msg["events"] as JArray;
                    var orders = events?.FirstOrDefault()?["orders"] as JArray;
                    if (orders != null)
                    {
                        foreach (var o in orders)
                        {
                            var statusStr = o["status"]?.ToString() ?? "";
                            var sideStr = o["side"]?.ToString() ?? "";
                            _orderUpdateSubject.OnNext(new OrderUpdate(
                                o["order_id"]?.ToString() ?? "",
                                o["product_id"]?.ToString() ?? "",
                                sideStr == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                                double.TryParse(o["filled_size"]?.ToString(), out double fs) ? fs : 0,
                                double.TryParse(o["avg_price"]?.ToString(), out double ap) ? ap : 0,
                                double.TryParse(o["leaves_quantity"]?.ToString(), out double lq) ? lq : 0,
                                MapToOrderStatus(statusStr),
                                false, false, DateTime.UtcNow));
                        }
                    }
                }
            }
            catch { /* malformed message */ }
        }

        private OrderStatus MapToOrderStatus(string status) => status.ToUpper() switch
        {
            "FILLED"    => OrderStatus.Filled,
            "CANCELLED" => OrderStatus.Cancelled,
            "EXPIRED"   => OrderStatus.Cancelled,
            "REJECTED"  => OrderStatus.Rejected,
            "OPEN"      => OrderStatus.PartialFill,
            _           => OrderStatus.Triggered
        };

        private TimeSpan MapTimeframeToTimeSpan(string tf) => tf.ToLower() switch
        {
            "1m"  => TimeSpan.FromMinutes(1),
            "5m"  => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "1h"  => TimeSpan.FromHours(1),
            "6h"  => TimeSpan.FromHours(6),
            "1d"  => TimeSpan.FromDays(1),
            _     => TimeSpan.FromHours(1)
        };

        public override async Task DisconnectAsync()
        {
            if (_ws != null)
            {
                await _ws.DisconnectAsync();
                _ws.Dispose();
                _ws = null;
            }
            _currentSymbol    = null;
            _currentTimeframe = null;
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            var product     = request.Symbol.Replace("/", "-").ToUpper();
            int granSec     = MapTimeframeToSeconds(request.Timeframe);
            int limit       = Math.Min(request.Limit, 350);

            long start = request.Since.HasValue ? request.Since.Value / 1000 : DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (limit * granSec);
            long end   = request.Until.HasValue ? request.Until.Value / 1000 : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            string path = $"/api/v3/brokerage/products/{product}/candles";
            string query = $"?start={start}&end={end}&granularity={MapToCoinbaseGranularity(request.Timeframe)}";
            string url = $"https://api.coinbase.com{path}{query}";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    AddAuthHeaders("GET", path);
                    var response = await _httpClient.GetStringAsync(url);
                    var json     = JObject.Parse(response);
                    var candles  = json["candles"] as JArray;
                    if (candles == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = candles.Select(c => new Ohlcv(
                        DateTimeOffset.FromUnixTimeSeconds(long.Parse(c["start"]?.ToString() ?? "0")).UtcDateTime,
                        double.Parse(c["open"]?.ToString()   ?? "0"),
                        double.Parse(c["high"]?.ToString()   ?? "0"),
                        double.Parse(c["low"]?.ToString()    ?? "0"),
                        double.Parse(c["close"]?.ToString()  ?? "0"),
                        double.Parse(c["volume"]?.ToString() ?? "0")))
                        .OrderBy(x => x.Date).ToList();

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
                _errorStream.OnNext($"Coinbase fetch error: {ex.Message}");
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
                    string path = "/api/v3/brokerage/products";
                    AddAuthHeaders("GET", path);
                    var response = await _httpClient.GetStringAsync($"https://api.coinbase.com{path}");
                    var json     = JObject.Parse(response);
                    var products = json["products"] as JArray;
                    return products?.Select(p => p["product_id"]?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s).ToList()
                        ?? new List<string>();
                });
            }
            catch { return new List<string>(); }
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Spot" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "1h", "6h", "1d" });

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            if (!IsConfigured) return (new(), new());
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var cleanSymbol = symbol.Replace("/", "-").ToUpper();
                    string path = "/api/v3/brokerage/product_book";
                    AddAuthHeaders("GET", path);
                    var response = await _httpClient.GetStringAsync($"https://api.coinbase.com{path}?product_id={cleanSymbol}&limit={limit}");
                    var book = JObject.Parse(response)["pricebook"];
                    var bids = (book?["bids"] as JArray)?.Select(b => new OrderBookEntry(
                        double.Parse(b["price"]?.ToString() ?? "0"),
                        double.Parse(b["size"]?.ToString()  ?? "0"))).ToList() ?? new();
                    var asks = (book?["asks"] as JArray)?.Select(a => new OrderBookEntry(
                        double.Parse(a["price"]?.ToString() ?? "0"),
                        double.Parse(a["size"]?.ToString()  ?? "0"))).ToList() ?? new();
                    return (bids, asks);
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

        // ── ITradingProvider ─────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConfigured) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string path = "/api/v3/brokerage/accounts";
                    AddAuthHeaders("GET", path);
                    var response = await _httpClient.GetStringAsync($"https://api.coinbase.com{path}");
                    var json     = JObject.Parse(response);
                    var accounts = json["accounts"] as JArray;
                    if (accounts == null) return new List<Balance>();
                    return accounts
                        .Where(a =>
                            double.TryParse(a["available_balance"]?["value"]?.ToString(), out double av) && av > 0
                            || double.TryParse(a["hold"]?["value"]?.ToString(), out double hold) && hold > 0)
                        .Select(a => new Balance(
                            a["currency"]?.ToString() ?? "",
                            double.TryParse(a["available_balance"]?["value"]?.ToString(), out double avf) ? avf : 0,
                            double.TryParse(a["hold"]?["value"]?.ToString(), out double hf) ? hf : 0))
                        .ToList();
                });
            }
            catch { return new(); }
        }

        public Task<List<Position>> GetPositionsAsync() => Task.FromResult(new List<Position>());

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConfigured) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string path = "/api/v3/brokerage/orders/historical/batch";
                    string query = "?order_status=OPEN";
                    if (!string.IsNullOrEmpty(symbol))
                        query += $"&product_id={symbol.Replace("/", "-").ToUpper()}";

                    AddAuthHeaders("GET", path);
                    var response = await _httpClient.GetStringAsync($"https://api.coinbase.com{path}{query}");
                    var json     = JObject.Parse(response);
                    var orders   = json["orders"] as JArray;
                    if (orders == null) return new List<OpenOrder>();

                    return orders.Select(o => new OpenOrder(
                        o["order_id"]?.ToString() ?? "",
                        o["product_id"]?.ToString() ?? "",
                        o["side"]?.ToString() == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                        MapCoinbaseOrderType(o["order_configuration"]),
                        double.TryParse(
                            o["order_configuration"]?["limit_limit_gtc"]?["base_size"]?.ToString()
                            ?? o["order_configuration"]?["market_market_ioc"]?["base_size"]?.ToString()
                            ?? o["filled_size"]?.ToString() ?? "0", out double qty) ? qty : 0,
                        double.TryParse(
                            o["order_configuration"]?["limit_limit_gtc"]?["limit_price"]?.ToString()
                            ?? o["average_filled_price"]?.ToString() ?? "0", out double avgPx) ? avgPx : 0,
                        o["status"]?.ToString() ?? ""
                    )).ToList();
                });
            }
            catch { return new(); }
        }

        private static OrderType MapCoinbaseOrderType(JToken? config)
        {
            if (config == null) return OrderType.Market;
            if (config["market_market_ioc"] != null) return OrderType.Market;
            if (config["limit_limit_gtc"] != null) return OrderType.Limit;
            if (config["stop_limit_stop_limit_gtc"] != null) return OrderType.StopLimit;
            return OrderType.Market;
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConfigured) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string productId = signal.Symbol.Replace("/", "-").ToUpper();
                    string clientOid = signal.ClientOid ?? Guid.NewGuid().ToString();

                    JObject orderConfig;
                    if (signal.Type == OrderType.Market)
                        orderConfig = new JObject { ["market_market_ioc"] = new JObject { ["base_size"] = signal.Quantity.ToString("F8") } };
                    else if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                        orderConfig = new JObject { ["limit_limit_gtc"] = new JObject { ["base_size"] = signal.Quantity.ToString("F8"), ["limit_price"] = signal.Price.Value.ToString("F8"), ["post_only"] = false } };
                    else if (signal.Type == OrderType.StopMarket && signal.StopLoss.HasValue)
                    {
                        orderConfig = new JObject
                        {
                            ["stop_limit_stop_limit_gtc"] = new JObject
                            {
                                ["base_size"] = signal.Quantity.ToString("F8"),
                                ["limit_price"] = (signal.StopLoss.Value * (signal.Side == OrderSide.Buy ? 1.05 : 0.95)).ToString("F8"),
                                ["stop_price"] = signal.StopLoss.Value.ToString("F8"),
                                ["stop_direction"] = signal.Side == OrderSide.Buy ? "STOP_DIRECTION_STOP_UP" : "STOP_DIRECTION_STOP_DOWN"
                            }
                        };
                    }
                    else if (signal.Type == OrderType.StopLimit && signal.StopLoss.HasValue && signal.Price.HasValue)
                    {
                        orderConfig = new JObject
                        {
                            ["stop_limit_stop_limit_gtc"] = new JObject
                            {
                                ["base_size"] = signal.Quantity.ToString("F8"),
                                ["limit_price"] = signal.Price.Value.ToString("F8"),
                                ["stop_price"] = signal.StopLoss.Value.ToString("F8"),
                                ["stop_direction"] = signal.Side == OrderSide.Buy ? "STOP_DIRECTION_STOP_UP" : "STOP_DIRECTION_STOP_DOWN"
                            }
                        };
                    }
                    else
                        return "ORDER_FAILED:Unsupported order type";

                    var body = new JObject
                    {
                        ["client_order_id"]     = clientOid,
                        ["product_id"]          = productId,
                        ["side"]                = signal.Side == OrderSide.Buy ? "BUY" : "SELL",
                        ["order_configuration"] = orderConfig
                    };

                    string path = "/api/v3/brokerage/orders";
                    AddAuthHeaders("POST", path);
                    var content  = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync($"https://api.coinbase.com{path}", content);
                    var respStr  = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) return $"ORDER_FAILED:{respStr}";
                    var json = JObject.Parse(respStr);
                    return json["success_response"]?["order_id"]?.ToString() ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConfigured) return false;
            try
            {
                var body     = new JObject { ["order_ids"] = new JArray { orderId } };
                string path = "/api/v3/brokerage/orders/batch_cancel";
                AddAuthHeaders("POST", path);
                var content  = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"https://api.coinbase.com{path}", content);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        // ── Auth helpers ��────────────────────────────────────────────────────

        private void AddAuthHeaders(string method, string requestPath)
        {
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_apiSecret))
            {
                var jwt = GenerateJwt(method, requestPath);
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {jwt}");
            }
        }

        private string GenerateJwt(string method, string requestPath)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                return string.Empty;

            var cleanPath = requestPath.StartsWith("/") ? requestPath : "/" + requestPath;
            if (!cleanPath.Contains("api.coinbase.com"))
                cleanPath = "api.coinbase.com" + cleanPath;

            var uri = $"{method} {cleanPath}";

            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

            using var ecdsa = System.Security.Cryptography.ECDsa.Create();
            try
            {
                ecdsa.ImportFromPem(_apiSecret);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Coinbase Auth Error: Failed to import private key. {ex.Message}");
                return "AUTH_ERROR";
            }

            var key = new Microsoft.IdentityModel.Tokens.ECDsaSecurityKey(ecdsa) { KeyId = _apiKey };
            var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.EcdsaSha256);

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                issuer: "cdp",
                audience: "cdp_service",
                claims: new[]
                {
                    new System.Security.Claims.Claim("sub", _apiKey),
                    new System.Security.Claims.Claim("iss", "cdp"),
                    new System.Security.Claims.Claim("nbf", now.ToString()),
                    new System.Security.Claims.Claim("exp", (now + 120).ToString()),
                    new System.Security.Claims.Claim("uri", uri),
                },
                signingCredentials: credentials);

            jwt.Header["kid"] = _apiKey;
            jwt.Header.Remove("typ");

            return handler.WriteToken(jwt);
        }

        private string MapToCoinbaseGranularity(string tf) => tf.ToLower() switch
        {
            "1m"  => "ONE_MINUTE",
            "5m"  => "FIVE_MINUTE",
            "15m" => "FIFTEEN_MINUTE",
            "1h"  => "ONE_HOUR",
            "6h"  => "SIX_HOUR",
            "1d"  => "ONE_DAY",
            _     => "ONE_HOUR"
        };

        private int MapTimeframeToSeconds(string tf) => tf.ToLower() switch
        {
            "1m"  => 60,
            "5m"  => 300,
            "15m" => 900,
            "1h"  => 3600,
            "6h"  => 21600,
            "1d"  => 86400,
            _     => 3600
        };
    }
}
