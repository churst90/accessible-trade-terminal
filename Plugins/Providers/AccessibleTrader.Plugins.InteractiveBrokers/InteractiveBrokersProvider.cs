using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

namespace AccessibleTrader.Plugins.InteractiveBrokers
{
    /// <summary>
    /// Interactive Brokers provider using the Client Portal API.
    /// Requires the IBKR Client Portal Gateway to be running locally.
    /// Supports stocks, options, futures, forex, bonds, and funds.
    /// </summary>
    public class InteractiveBrokersProvider : BaseMarketDataProvider, ITradingProvider
    {
        private readonly HttpClient _httpClient;

        // Default gateway URL — user can override via Configure()
        private string _gatewayUrl = "https://localhost:5000/v1/api";
        private string? _accountId;

        // TLS pinning: SHA-256 fingerprint (hex, no colons, case-insensitive) of the
        // gateway cert the user has approved. Configured via "GatewayCertSha256".
        // If null, the gateway host must be loopback and the default Windows cert
        // chain applies — we never blanket-accept arbitrary certs.
        private string? _pinnedCertSha256;

        // Rate limiter: IBKR Client Portal ~50 req/sec
        private readonly RateLimiter _rateLimiter = new(50, TimeSpan.FromSeconds(1));

        // WebSocket for streaming market data
        private ReconnectingWebSocket? _ws;
        private string? _currentConId;
        private string? _currentSymbol;
        private string? _currentTimeframe;

        // Streams
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Session keepalive timer
        private System.Timers.Timer? _tickleTimer;

        public override string Name => "Interactive Brokers";
        public override string Description => "IBKR — Stocks, Options, Futures, Forex, Bonds";
        public override List<MarketType> SupportedMarkets => new List<MarketType>
        {
            MarketType.Stock, MarketType.Options, MarketType.Futures,
            MarketType.Forex, MarketType.Bonds, MarketType.Index
        };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false; // Uses gateway session auth, not API keys
        public override bool IsConfigured => true;    // Gateway handles auth
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 1000;
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.Shorting | ProviderCapabilities.OCO |
            ProviderCapabilities.TrailingStop | ProviderCapabilities.Leverage | ProviderCapabilities.Brackets;

        public override bool SupportsMarginTrading  => true;
        public override bool SupportsFuturesTrading => true;
        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 4.0;  // Reg-T margin

        public bool IsConnected => !string.IsNullOrEmpty(_accountId);

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour, StandardTimeframes.FourHours,
            StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public InteractiveBrokersProvider()
        {
            // IBKR Client Portal Gateway commonly uses a self-signed cert. We do NOT
            // blanket-accept any certificate (that allows MITM on untrusted networks).
            // Instead: fail closed by default, and allow either (a) trusting the
            // system chain if the user installed the gateway cert into the OS store,
            // or (b) pinning a specific SHA-256 fingerprint via "GatewayCertSha256".
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = ValidateGatewayCertificate
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AccessibleTrader/1.0");
            // Cap response size so a compromised/hostile endpoint can't OOM the app
            // with an unbounded body. IBKR payloads are tiny; 16 MB is a generous cap.
            _httpClient.MaxResponseContentBufferSize = 16 * 1024 * 1024;
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private bool ValidateGatewayCertificate(
            HttpRequestMessage request,
            X509Certificate2? cert,
            X509Chain? chain,
            SslPolicyErrors errors)
        {
            // No cert presented — always reject.
            if (cert == null) return false;

            // If the user pinned a fingerprint, that is the authoritative check.
            // We do NOT fall back to the system chain when a pin is configured.
            if (!string.IsNullOrEmpty(_pinnedCertSha256))
            {
                var thumb = Convert.ToHexString(SHA256.HashData(cert.RawData));
                return string.Equals(thumb, _pinnedCertSha256, StringComparison.OrdinalIgnoreCase);
            }

            // No pin configured: only accept if the cert fully validates AND the
            // host is a local loopback address. This keeps the default local-only
            // gateway flow working while refusing MITM on any non-loopback URL.
            if (errors != SslPolicyErrors.None) return false;
            if (!IsLoopbackUri(_gatewayUrl)) return false;
            return true;
        }

        private static bool IsLoopbackUri(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return uri.IsLoopback
                || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host == "127.0.0.1"
                || uri.Host == "::1";
        }

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("GatewayUrl", out var url))
            {
                var trimmed = url.TrimEnd('/');
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var gw))
                {
                    _errorStream.OnNext($"IBKR: invalid GatewayUrl '{url}'. Keeping default.");
                }
                else if (!string.Equals(gw.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    _errorStream.OnNext("IBKR: GatewayUrl must use https. Keeping default.");
                }
                else if (!IsLoopbackUri(trimmed))
                {
                    // Non-loopback gateway is refused by default to prevent SSRF and
                    // the "point it at 169.254.169.254" class of attacks. If this
                    // ever needs to be relaxed, add an explicit opt-in flag that
                    // logs the chosen host and requires a matching cert pin.
                    _errorStream.OnNext(
                        $"IBKR: GatewayUrl host '{gw.Host}' is not a loopback address. " +
                        "Non-loopback gateways are disabled. Run the IBKR Client Portal Gateway " +
                        "locally and use https://localhost:5000/v1/api.");
                }
                else
                {
                    _gatewayUrl = trimmed;
                }
            }
            if (config.TryGetValue("AccountId", out var acct))
                _accountId = acct;
            if (config.TryGetValue("GatewayCertSha256", out var pin))
            {
                var cleaned = (pin ?? "").Replace(":", "").Replace(" ", "").Trim();
                // SHA-256 hex is 64 chars. Anything else is a misconfiguration.
                _pinnedCertSha256 = cleaned.Length == 64 ? cleaned : null;
                if (!string.IsNullOrEmpty(pin) && _pinnedCertSha256 == null)
                    _errorStream.OnNext("IBKR: GatewayCertSha256 must be a 64-char hex SHA-256 digest. Pin ignored.");
            }
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_gatewayUrl}/iserver/auth/status");
                if (!response.IsSuccessStatusCode)
                    return (false, $"Gateway not reachable ({response.StatusCode}). Is the Client Portal Gateway running?");

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                bool authenticated = json["authenticated"]?.Value<bool>() ?? false;
                if (!authenticated)
                    return (false, "Not authenticated. Please log in via the Client Portal Gateway web UI.");

                // Fetch account ID if not configured
                if (string.IsNullOrEmpty(_accountId))
                {
                    var acctResp = await _httpClient.GetStringAsync($"{_gatewayUrl}/iserver/accounts");
                    var acctJson = JObject.Parse(acctResp);
                    var accounts = acctJson["accounts"] as JArray;
                    _accountId = accounts?.FirstOrDefault()?.ToString();
                }

                return (true, $"Authenticated. Account: {_accountId}");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Cannot connect to IBKR Gateway at {_gatewayUrl}. Error: {ex.Message}");
            }
            catch (Exception ex) { return (false, $"Validation error: {ex.Message}"); }
        }

        // ── Connection ──────────────────────────────────────────────────────

        public override async Task EnsureConnectedAsync()
        {
            // Validate auth status and fetch account
            var (valid, msg) = await ValidateApiKeyAsync();
            if (!valid)
            {
                _errorStream.OnNext(msg);
                _connectionStateStream.OnNext(ConnectionState.Error);
                return;
            }

            _connectionStateStream.OnNext(ConnectionState.Connected);

            // Start session keepalive (tickle every 55 seconds to prevent timeout)
            if (_tickleTimer == null)
            {
                _tickleTimer = new System.Timers.Timer(55_000);
                _tickleTimer.Elapsed += async (_, _) =>
                {
                    try { await _httpClient.PostAsync($"{_gatewayUrl}/tickle", null); }
                    catch (Exception ex)
                    {
                        // Tickle failures are non-fatal (the next tick retries) but
                        // were silently swallowed; surface a Debug-level breadcrumb so
                        // a wedged session is diagnosable.
                        System.Diagnostics.Debug.WriteLine($"[IBKR] tickle failed: {ex.Message}");
                    }
                };
                _tickleTimer.AutoReset = true;
                _tickleTimer.Start();
            }
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            _currentSymbol = symbol;
            _currentTimeframe = timeframe;

            // Resolve conId for the symbol
            var conId = await ResolveConIdAsync(symbol, market);
            if (string.IsNullOrEmpty(conId))
            {
                _errorStream.OnNext($"IBKR: Could not resolve conId for {symbol}");
                return;
            }
            _currentConId = conId;

            // Connect WebSocket for streaming
            if (_ws != null) { await _ws.DisconnectAsync(); _ws.Dispose(); }

            string wsUrl = _gatewayUrl.Replace("https://", "wss://").Replace("http://", "ws://")
                .Replace("/v1/api", "") + "/v1/api/ws";

            _ws = new ReconnectingWebSocket(wsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    // Subscribe to market data for the conId
                    var sub = $"smd+{_currentConId}+{{\"fields\":[\"31\",\"84\",\"85\",\"86\",\"88\"]}}";
                    await ws.SendAsync(sub);

                    // Subscribe to order updates
                    await ws.SendAsync("sor+{}");
                })
                .OnMessage(HandleWebSocketMessage)
                .OnError(err => _errorStream.OnNext($"IBKR WS: {err}"))
                .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

            await _ws.ConnectAsync();
        }

        private void HandleWebSocketMessage(string msg)
        {
            try
            {
                var json = JObject.Parse(msg);
                var topic = json["topic"]?.ToString() ?? "";

                if (topic.StartsWith("smd+"))
                {
                    // Streaming market data update
                    // Field 31=Last, 84=Bid, 85=BidSize, 86=Ask, 88=AskSize
                    double last = double.TryParse(json["31"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double l) ? l : 0;
                    if (last > 0)
                    {
                        _liveStream.OnNext(new Ohlcv(DateTime.UtcNow, last, last, last, last, 0));
                    }
                }
                else if (topic.StartsWith("sor"))
                {
                    // Order status update
                    var args = json["args"] as JArray;
                    if (args != null)
                    {
                        foreach (var order in args)
                        {
                            var orderId = order["orderId"]?.ToString() ?? "";
                            var symbol = order["ticker"]?.ToString() ?? "";
                            var side = order["side"]?.ToString() ?? "";
                            var statusStr = order["status"]?.ToString() ?? "";

                            var status = statusStr switch
                            {
                                "Filled"          => OrderStatus.Filled,
                                "PreSubmitted"    => OrderStatus.Triggered,
                                "Submitted"       => OrderStatus.Triggered,
                                "Cancelled"       => OrderStatus.Cancelled,
                                "Inactive"        => OrderStatus.Rejected,
                                _                 => OrderStatus.PartialFill
                            };

                            _orderUpdateSubject.OnNext(new OrderUpdate(
                                orderId, symbol,
                                side.StartsWith("B", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell,
                                order["filledQuantity"]?.Value<double>() ?? 0,
                                order["avgPrice"]?.Value<double>() ?? 0,
                                order["remainingQuantity"]?.Value<double>() ?? 0,
                                status, false, false, DateTime.UtcNow));
                        }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[IB] Malformed feed frame skipped: {ex.GetType().Name}"); }
        }

        public override async Task DisconnectAsync()
        {
            _tickleTimer?.Stop();
            _tickleTimer?.Dispose();
            _tickleTimer = null;

            if (_ws != null) { await _ws.DisconnectAsync(); _ws.Dispose(); _ws = null; }

            _currentConId = null;
            _currentSymbol = null;
            _currentTimeframe = null;
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        // ── Data Discovery ──────────────────────────────────────────────────

        public override async Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            market switch
            {
                MarketType.Stock   => new List<string> { "STK" },
                MarketType.Options => new List<string> { "OPT" },
                MarketType.Futures => new List<string> { "FUT" },
                MarketType.Forex   => new List<string> { "CASH" },
                MarketType.Bonds   => new List<string> { "BOND" },
                MarketType.Index   => new List<string> { "IND" },
                _                  => new List<string> { "STK" }
            };

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            // IBKR doesn't have a "list all symbols" endpoint.
            // Return common symbols as suggestions; users search via symbol picker.
            return market switch
            {
                MarketType.Stock => new List<string>
                {
                    "AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA", "BRK.B",
                    "JPM", "V", "JNJ", "WMT", "PG", "MA", "UNH", "HD", "DIS", "BAC",
                    "XOM", "PFE", "ABBV", "KO", "PEP", "TMO", "COST", "AVGO", "MRK",
                    "CSCO", "ABT", "CVX", "ACN", "NFLX", "AMD", "INTC", "QCOM", "TXN",
                    "SPY", "QQQ", "IWM", "DIA", "VTI", "VOO", "ARKK", "XLF", "XLE"
                },
                MarketType.Futures => new List<string>
                {
                    "ES", "NQ", "YM", "RTY", "CL", "GC", "SI", "ZB", "ZN", "ZF",
                    "NG", "HG", "6E", "6J", "6B", "ZC", "ZW", "ZS", "LE", "HE"
                },
                MarketType.Forex => new List<string>
                {
                    "EUR.USD", "GBP.USD", "USD.JPY", "USD.CHF", "AUD.USD",
                    "USD.CAD", "NZD.USD", "EUR.GBP", "EUR.JPY", "GBP.JPY"
                },
                MarketType.Index => new List<string>
                {
                    "SPX", "NDX", "DJI", "RUT", "VIX"
                },
                _ => new List<string>()
            };
        }

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "30m", "1h", "4h", "1d", "1w", "1M" });

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var conId = _currentConId ?? await ResolveConIdAsync(request.Symbol, request.Market);
            if (string.IsNullOrEmpty(conId))
                return (new List<Ohlcv>(), new List<(long, double)>());

            string period = MapToPeriod(request);
            string bar = MapToBarSize(request.Timeframe);

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string url = $"{_gatewayUrl}/iserver/marketdata/history?conid={conId}&period={period}&bar={bar}";
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);
                    var data = json["data"] as JArray;
                    if (data == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = data.Select(item => new Ohlcv(
                        DateTimeOffset.FromUnixTimeMilliseconds(item["t"]?.Value<long>() ?? 0).UtcDateTime,
                        item["o"]?.Value<double>() ?? 0,
                        item["h"]?.Value<double>() ?? 0,
                        item["l"]?.Value<double>() ?? 0,
                        item["c"]?.Value<double>() ?? 0,
                        item["v"]?.Value<double>() ?? 0))
                        .OrderBy(x => x.Date)
                        .ToList();

                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"IBKR fetch error: {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            // IBKR provides snapshot quotes via /iserver/marketdata/snapshot
            var conId = _currentConId ?? await ResolveConIdAsync(symbol, "Stock");
            if (string.IsNullOrEmpty(conId)) return (new(), new());

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string url = $"{_gatewayUrl}/iserver/marketdata/snapshot?conids={conId}&fields=84,85,86,88";
                    var response = await _httpClient.GetStringAsync(url);
                    var arr = JArray.Parse(response);
                    var snap = arr.FirstOrDefault();
                    if (snap == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                    double bidPx = snap["84"]?.Value<double>() ?? 0;
                    double bidSz = snap["85"]?.Value<double>() ?? 0;
                    double askPx = snap["86"]?.Value<double>() ?? 0;
                    double askSz = snap["88"]?.Value<double>() ?? 0;

                    var bids = bidPx > 0 ? new List<OrderBookEntry> { new(bidPx, bidSz) } : new();
                    var asks = askPx > 0 ? new List<OrderBookEntry> { new(askPx, askSz) } : new();
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
                    var response = await _httpClient.GetStringAsync($"{_gatewayUrl}/portfolio/{_accountId}/summary");
                    var json = JObject.Parse(response);

                    var result = new List<Balance>();
                    foreach (var prop in json.Properties())
                    {
                        var val = prop.Value?["amount"]?.Value<double>() ?? 0;
                        if (Math.Abs(val) > 0.01)
                            result.Add(new Balance(prop.Name, val, 0));
                    }
                    return result;
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
                    var response = await _httpClient.GetStringAsync($"{_gatewayUrl}/portfolio/{_accountId}/positions/0");
                    var arr = JArray.Parse(response);
                    return arr.Select(p => new Position(
                        p["ticker"]?.ToString() ?? p["contractDesc"]?.ToString() ?? "",
                        Math.Abs(p["position"]?.Value<double>() ?? 0),
                        p["avgCost"]?.Value<double>() ?? 0,
                        p["mktValue"]?.Value<double>() ?? 0,
                        p["unrealizedPnl"]?.Value<double>() ?? 0
                    )).ToList();
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
                    var response = await _httpClient.GetStringAsync($"{_gatewayUrl}/iserver/account/orders");
                    var json = JObject.Parse(response);
                    var orders = json["orders"] as JArray;
                    if (orders == null) return new List<OpenOrder>();

                    return orders
                        .Where(o => symbol == null || (o["ticker"]?.ToString() ?? "").Contains(symbol, StringComparison.OrdinalIgnoreCase))
                        .Where(o => o["status"]?.ToString() != "Filled" && o["status"]?.ToString() != "Cancelled")
                        .Select(o => new OpenOrder(
                            o["orderId"]?.ToString() ?? "",
                            o["ticker"]?.ToString() ?? "",
                            o["side"]?.ToString()?.StartsWith("B", StringComparison.OrdinalIgnoreCase) == true ? OrderSide.Buy : OrderSide.Sell,
                            MapIbkrOrderType(o["orderType"]?.ToString() ?? "MKT"),
                            o["totalSize"]?.Value<double>() ?? 0,
                            o["price"]?.Value<double>() ?? 0,
                            o["status"]?.ToString() ?? ""
                        )).ToList();
                });
            }
            catch { return new(); }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";

            var conId = _currentConId ?? await ResolveConIdAsync(signal.Symbol, signal.SubType ?? "STK");
            if (string.IsNullOrEmpty(conId)) return "ORDER_FAILED:Could not resolve contract ID";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var orderBody = new JObject
                    {
                        ["conid"]     = int.Parse(conId),
                        ["orderType"] = MapToIbkrOrderType(signal.Type),
                        ["side"]      = signal.Side == OrderSide.Buy ? "BUY" : "SELL",
                        ["quantity"]  = signal.Quantity,
                        ["tif"]       = "GTC"
                    };

                    if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                        orderBody["price"] = signal.Price.Value;

                    if (signal.Type == OrderType.StopMarket && signal.StopLoss.HasValue)
                        orderBody["auxPrice"] = signal.StopLoss.Value;

                    if (signal.Type == OrderType.StopLimit && signal.StopLoss.HasValue && signal.Price.HasValue)
                    {
                        orderBody["price"]    = signal.Price.Value;
                        orderBody["auxPrice"] = signal.StopLoss.Value;
                    }

                    if (!string.IsNullOrEmpty(signal.ClientOid))
                        orderBody["cOID"] = signal.ClientOid;

                    var orders = new JObject { ["orders"] = new JArray { orderBody } };
                    var content = new StringContent(orders.ToString(), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync($"{_gatewayUrl}/iserver/account/{_accountId}/orders", content);
                    var respStr = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return $"ORDER_FAILED:{respStr}";

                    var arr = JArray.Parse(respStr);
                    var first = arr.FirstOrDefault();

                    // IBKR may return a confirmation prompt
                    var msgId = first?["id"]?.ToString();
                    if (first?["message"] != null && !string.IsNullOrEmpty(msgId))
                    {
                        // Auto-confirm the order
                        var confirmBody = new JObject { ["confirmed"] = true };
                        var confirmContent = new StringContent(confirmBody.ToString(), Encoding.UTF8, "application/json");
                        var confirmResp = await _httpClient.PostAsync($"{_gatewayUrl}/iserver/reply/{msgId}", confirmContent);
                        var confirmStr = await confirmResp.Content.ReadAsStringAsync();
                        var confirmArr = JArray.Parse(confirmStr);
                        var orderId = confirmArr.FirstOrDefault()?["order_id"]?.ToString();
                        return orderId ?? "ORDER_SUBMITTED";
                    }

                    return first?["order_id"]?.ToString() ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"IBKR order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                var response = await _rateLimiter.ExecuteAsync(async () =>
                    await _httpClient.DeleteAsync($"{_gatewayUrl}/iserver/account/{_accountId}/order/{orderId}"));
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) =>
            Task.FromResult(Math.Clamp(leverage, 1, MaxLeverage));

        // ── Helpers ─────────────────────────────────────────────────────────

        private async Task<string?> ResolveConIdAsync(string symbol, string market)
        {
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string secType = market switch
                    {
                        string m when m.Contains("Futures", StringComparison.OrdinalIgnoreCase) => "FUT",
                        string m when m.Contains("Options", StringComparison.OrdinalIgnoreCase) => "OPT",
                        string m when m.Contains("Forex", StringComparison.OrdinalIgnoreCase)   => "CASH",
                        string m when m.Contains("Bond", StringComparison.OrdinalIgnoreCase)    => "BOND",
                        string m when m.Contains("Index", StringComparison.OrdinalIgnoreCase)   => "IND",
                        _ => "STK"
                    };

                    var body = new JObject { ["symbol"] = symbol.ToUpper(), ["secType"] = secType };
                    var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync($"{_gatewayUrl}/iserver/secdef/search", content);
                    var respStr = await response.Content.ReadAsStringAsync();
                    var arr = JArray.Parse(respStr);
                    return arr.FirstOrDefault()?["conid"]?.ToString();
                });
            }
            catch { return null; }
        }

        private static string MapToPeriod(MarketDataRequest request)
        {
            if (request.Since.HasValue && request.Until.HasValue)
            {
                var span = TimeSpan.FromMilliseconds(request.Until.Value - request.Since.Value);
                if (span.TotalDays > 365) return $"{(int)(span.TotalDays / 365)}y";
                if (span.TotalDays > 30) return $"{(int)(span.TotalDays / 30)}m";
                return $"{(int)span.TotalDays}d";
            }

            // Default period based on timeframe
            return request.Timeframe switch
            {
                "1m"  => "1d",
                "5m"  => "5d",
                "15m" => "10d",
                "30m" => "20d",
                "1h"  => "1m",
                "4h"  => "3m",
                "1d"  => "1y",
                "1w"  => "5y",
                "1M"  => "10y",
                _     => "1m"
            };
        }

        private static string MapToBarSize(string tf) => tf switch
        {
            "1m"  => "1min",
            "5m"  => "5min",
            "15m" => "15min",
            "30m" => "30min",
            "1h"  => "1h",
            "4h"  => "4h",
            "1d"  => "1d",
            "1w"  => "1w",
            "1M"  => "1m",
            _     => "1h"
        };

        private static string MapToIbkrOrderType(OrderType type) => type switch
        {
            OrderType.Market           => "MKT",
            OrderType.Limit            => "LMT",
            OrderType.StopMarket       => "STP",
            OrderType.StopLimit        => "STP LMT",
            OrderType.TakeProfitMarket => "MIT",  // Market If Touched
            OrderType.TakeProfitLimit  => "LIT",  // Limit If Touched
            _                          => "MKT"
        };

        private static OrderType MapIbkrOrderType(string type) => type.ToUpper() switch
        {
            "LMT"     => OrderType.Limit,
            "STP"     => OrderType.StopMarket,
            "STP LMT" => OrderType.StopLimit,
            "MIT"     => OrderType.TakeProfitMarket,
            "LIT"     => OrderType.TakeProfitLimit,
            _         => OrderType.Market
        };
    }
}
