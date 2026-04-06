using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Coinbase
{
    public class CoinbaseProvider : BaseMarketDataProvider, ITradingProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private string? _apiSecret;
        private const string BaseUrl = "https://api.coinbase.com/api/v3/brokerage";

        // Order update stream — stub (Coinbase order callbacks not yet wired).
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        private string? _currentSymbol;
        private string? _currentTimeframe;
        private Ohlcv? _lastCandle;
        private System.Net.WebSockets.ClientWebSocket? _webSocket;
        private System.Threading.CancellationTokenSource? _wsCts;
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

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.OneHour, StandardTimeframes.SixHours, StandardTimeframes.OneDay
        };

        // ITradingProvider properties
        public bool IsConnected => IsConfigured;
        public override bool SupportsMarginTrading  => false;
        public override bool SupportsFuturesTrading => false;
        public override bool SupportsStopLoss       => true;   // Coinbase supports stop orders
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
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey",    out var key))    _apiKey    = key;
            if (config.TryGetValue("ApiSecret", out var secret)) _apiSecret = secret;
        }

        public override Task EnsureConnectedAsync()
        {
            if (IsConfigured) _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            if (_currentSymbol == symbol && _currentTimeframe == timeframe && _webSocket?.State == System.Net.WebSockets.WebSocketState.Open) return;

            _currentSymbol    = symbol;
            _currentTimeframe = timeframe;
            
            _wsCts?.Cancel();
            _wsCts = new System.Threading.CancellationTokenSource();
            
            await ConnectWebSocketAsync(symbol, _wsCts.Token);
        }

        private async Task ConnectWebSocketAsync(string symbol, System.Threading.CancellationToken ct)
        {
            try
            {
                _webSocket?.Dispose();
                _webSocket = new System.Net.WebSockets.ClientWebSocket();
                
                var wsUrl = new Uri("wss://advanced-trade-ws.coinbase.com");
                await _webSocket.ConnectAsync(wsUrl, ct);

                // Subscribe to ticker for real-time price updates
                var productId = symbol.Replace("/", "-").ToUpper();
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                
                // For WebSocket, Coinbase requires a specific JWT or a signature. 
                // Using the same JWT logic but without the 'uri' claim if they don't want it, 
                // however most V3 WS docs now point to a similar JWT.
                var jwt = GenerateJwt("GET", "advanced-trade-ws.coinbase.com");

                var subMsg = new JObject
                {
                    ["type"] = "subscribe",
                    ["product_ids"] = new JArray { productId },
                    ["channel"] = "ticker",
                    ["jwt"] = jwt
                };

                var userSubMsg = new JObject
                {
                    ["type"] = "subscribe",
                    ["product_ids"] = new JArray { productId },
                    ["channel"] = "user",
                    ["jwt"] = jwt
                };

                var bytes = System.Text.Encoding.UTF8.GetBytes(subMsg.ToString());
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, ct);

                var userBytes = System.Text.Encoding.UTF8.GetBytes(userSubMsg.ToString());
                await _webSocket.SendAsync(new ArraySegment<byte>(userBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, ct);

                _ = Task.Run(() => ReceiveLoopAsync(ct), ct);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Coinbase WebSocket connection error: {ex.Message}");
            }
        }

        private async Task ReceiveLoopAsync(System.Threading.CancellationToken ct)
        {
            var buffer = new byte[1024 * 16];
            var ms = new System.IO.MemoryStream();
            
            try
            {
                while (!ct.IsCancellationRequested && _webSocket?.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;

                    var jsonStr = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0); // Reset for next message
                    
                    var msg = JObject.Parse(jsonStr);

                    if (msg["channel"]?.ToString() == "ticker")
                    {
                        var events = msg["events"] as JArray;
                        var ticker = events?.FirstOrDefault()?["tickers"]?.FirstOrDefault();
                        if (ticker != null)
                        {
                            double price = double.Parse(ticker["price"]?.ToString() ?? "0");

                            // Filter subscribe-confirmation and zero-value frames
                            if (price <= 0) continue;

                            if (_lastCandle.HasValue && _lastCandleStart.HasValue)
                            {
                                var now = DateTime.UtcNow;
                                var interval = MapTimeframeToTimeSpan(_currentTimeframe ?? "1h");
                                
                                // Check for candle boundary
                                if (now >= _lastCandleStart.Value.Add(interval))
                                {
                                    // Start a new candle
                                    var newStart = _lastCandleStart.Value;
                                    while (now >= newStart.Add(interval)) newStart = newStart.Add(interval);
                                    
                                    _lastCandleStart = newStart;
                                    _lastCandle = new Ohlcv(newStart, price, price, price, price, 0);
                                }
                                else
                                {
                                    // Update existing candle
                                    var tick = new Ohlcv(now, price, price, price, price, 0);
                                    _lastCandle = _lastCandle.Value.UpdateWith(tick);
                                }
                                
                                _liveStream.OnNext(_lastCandle.Value);
                            }
                        }
                    }
                    else if (msg["channel"]?.ToString() == "user")
                    {
                        var events = msg["events"] as JArray;
                        var orders = events?.FirstOrDefault()?["orders"] as JArray;
                        if (orders != null)
                        {
                            foreach (var o in orders)
                            {
                                var statusStr = o["status"]?.ToString() ?? "";
                                var sideStr = o["side"]?.ToString() ?? "";
                                
                                var update = new OrderUpdate(
                                    o["order_id"]?.ToString() ?? "",
                                    o["product_id"]?.ToString() ?? "",
                                    sideStr == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                                    double.TryParse(o["filled_size"]?.ToString(), out double fs) ? fs : 0,
                                    double.TryParse(o["avg_price"]?.ToString(), out double ap) ? ap : 0,
                                    double.TryParse(o["leaves_quantity"]?.ToString(), out double lq) ? lq : 0,
                                    MapToOrderStatus(statusStr),
                                    false,
                                    false,
                                    DateTime.UtcNow
                                );
                                _orderUpdateSubject.OnNext(update);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _errorStream.OnNext($"Coinbase WebSocket receive error: {ex.Message}");
            }
            finally
            {
                ms.Dispose();
            }
        }

        private OrderStatus MapToOrderStatus(string status) => status.ToUpper() switch
        {
            "FILLED" => OrderStatus.Filled,
            "CANCELLED" => OrderStatus.Cancelled,
            "EXPIRED" => OrderStatus.Cancelled,
            "REJECTED" => OrderStatus.Rejected,
            "OPEN" => OrderStatus.PartialFill, // Best fit for partial fills in stream
            _ => OrderStatus.Triggered
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

        public override Task DisconnectAsync()
        {
            _wsCts?.Cancel();
            _webSocket?.Dispose();
            _webSocket        = null;
            _currentSymbol    = null;
            _currentTimeframe = null;
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
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
                string path = "/api/v3/brokerage/products";
                AddAuthHeaders("GET", path);
                var response = await _httpClient.GetStringAsync($"https://api.coinbase.com{path}");
                var json     = JObject.Parse(response);
                var products = json["products"] as JArray;
                return products?.Select(p => p["product_id"]?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s).ToList()
                    ?? new List<string>();
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
            }
            catch { return (new(), new()); }
        }

        // ── ITradingProvider ─────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConfigured) return new();
            try
            {
                string path = "/api/v3/brokerage/accounts";
                AddAuthHeaders("GET", path);
                var response = await _httpClient.GetStringAsync($"https://api.coinbase.com{path}");
                var json     = JObject.Parse(response);
                var accounts = json["accounts"] as JArray;
                if (accounts == null) return new();
                return accounts
                    .Where(a =>
                        double.TryParse(a["available_balance"]?["value"]?.ToString(), out double av) && av > 0
                        || double.TryParse(a["hold"]?["value"]?.ToString(), out double hold) && hold > 0)
                    .Select(a => new Balance(
                        a["currency"]?.ToString() ?? "",
                        double.TryParse(a["available_balance"]?["value"]?.ToString(), out double avf) ? avf : 0,
                        double.TryParse(a["hold"]?["value"]?.ToString(), out double hf) ? hf : 0))
                    .ToList();
            }
            catch { return new(); }
        }

        /// <summary>Coinbase Spot has no margin positions; always returns empty.</summary>
        public Task<List<Position>> GetPositionsAsync() => Task.FromResult(new List<Position>());

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConfigured) return new();
            try
            {
                string path = "/api/v3/brokerage/orders/historical/batch";
                string query = "?order_status=OPEN";
                if (!string.IsNullOrEmpty(symbol))
                    query += $"&product_id={symbol.Replace("/", "-").ToUpper()}";

                AddAuthHeaders("GET", path);
                var response = await _httpClient.GetStringAsync($"https://api.coinbase.com{path}{query}");
                var json     = JObject.Parse(response);
                var orders   = json["orders"] as JArray;
                if (orders == null) return new();

                return orders.Select(o => new OpenOrder(
                    o["order_id"]?.ToString() ?? "",
                    o["product_id"]?.ToString() ?? "",
                    o["side"]?.ToString() == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                    OrderType.Market,
                    double.TryParse(
                        o["order_configuration"]?["market_market_ioc"]?["base_size"]?.ToString()
                        ?? o["filled_size"]?.ToString() ?? "0", out double qty) ? qty : 0,
                    double.TryParse(o["average_filled_price"]?.ToString() ?? "0", out double avgPx) ? avgPx : 0,
                    o["status"]?.ToString() ?? ""
                )).ToList();
            }
            catch { return new(); }
        }

        // STUB: Coinbase trading API (PlaceOrder, CancelOrder, GetBalances, GetPositions) is implemented
        // but has NOT been end-to-end tested against the live Coinbase Advanced Trade V3 API.
        // JWT generation uses ECDsa PEM-key signing (GenerateJwt). Verify with a real CDP API key
        // before enabling trading in production. See Phase 5 roadmap.
        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConfigured) return "PROVIDER_NOT_CONFIGURED";
            try
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
                    // Coinbase doesn't have a "Stop Market" in the same way, usually it's a Stop Limit with a wide offset or specialized.
                    // But Advanced Trade V3 has stop_limit_stop_limit_gtc.
                    orderConfig = new JObject 
                    { 
                        ["stop_limit_stop_limit_gtc"] = new JObject 
                        { 
                            ["base_size"] = signal.Quantity.ToString("F8"), 
                            ["limit_price"] = (signal.StopLoss.Value * (signal.Side == OrderSide.Buy ? 1.05 : 0.95)).ToString("F8"), // 5% slippage allowance for "market-like" stop
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
                var content  = new System.Net.Http.StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"https://api.coinbase.com{path}", content);
                var respStr  = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return $"ORDER_FAILED:{respStr}";
                var json = JObject.Parse(respStr);
                return json["success_response"]?["order_id"]?.ToString() ?? "ORDER_SUBMITTED";
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
                var content  = new System.Net.Http.StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"https://api.coinbase.com{path}", content);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>Coinbase does not support leverage multipliers; always returns 1.0.</summary>
        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        // ── Auth helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds authentication headers for Coinbase Advanced Trade API.
        /// Uses JWT-based auth (CDP API key signing) as required by the V3 API.
        /// </summary>
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

            // Coinbase request path for JWT 'uri' claim should not include the base URL but should include method.
            // Documentation states it should be like "GET api.coinbase.com/api/v3/brokerage/accounts"
            // We strip leading slashes and ensure we don't have double "api.coinbase.com"
            var cleanPath = requestPath.StartsWith("/") ? requestPath : "/" + requestPath;
            if (!cleanPath.Contains("api.coinbase.com"))
            {
                cleanPath = "api.coinbase.com" + cleanPath;
            }
            
            var uri = $"{method} {cleanPath}";

            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            
            // The secret provided by Coinbase is a PEM formatted private key
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

            // Set the header "kid" as the API Key
            jwt.Header["kid"] = _apiKey;
            jwt.Header.Remove("typ"); // Some implementations prefer no 'typ' or 'JWT'

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
