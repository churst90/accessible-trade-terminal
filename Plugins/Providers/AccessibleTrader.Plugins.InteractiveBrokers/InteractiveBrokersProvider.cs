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

        // _currentConId is the contract of the CURRENTLY CHARTED symbol. Reusing it
        // for any other symbol places an order against the wrong instrument — chart
        // AAPL, order MSFT from the panel, buy AAPL, real order id comes back. Every
        // reuse must go through this check; the raw field is never a valid shortcut.
        internal string? CachedConIdFor(string symbol) =>
            _currentConId != null
            && string.Equals(_currentSymbol, symbol, StringComparison.OrdinalIgnoreCase)
                ? _currentConId
                : null;

        internal void SeedConIdCacheForTest(string? chartedSymbol, string? conId)
        {
            _currentSymbol = chartedSymbol;
            _currentConId = conId;
        }

        // Streams
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Order updates arrive on the same gateway socket as market data (the
        // "sor" subscription made on connect). If it is down, fills must resolve
        // by polling — the default-true flag used to claim streaming regardless.
        public bool SupportsOrderEventStreaming => _ws?.IsConnected ?? false;

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
        // Only capabilities PlaceOrderAsync can actually honor. OCO (no IOcoTradingProvider),
        // TrailingStop (no TRAIL order body), and Brackets (no parent/child leg array) were
        // declared but never implemented — the UI offered controls that silently placed a
        // bare order with no linked/protective legs. Single stop-loss and take-profit orders
        // ARE supported and stay advertised via SupportsStopLoss / SupportsTakeProfit below.
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.Shorting | ProviderCapabilities.Leverage |
            ProviderCapabilities.MarginTrading | ProviderCapabilities.FuturesTrading;

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
                    //
                    // LiveTickStyle classification (2026-07-22 fleet audit): these
                    // are last-PRICE quote ticks emitted with volume 0, so the
                    // TradeDeltas default is correct — consolidation accumulates
                    // zero volume (honest: this stream carries no per-trade size)
                    // and merges prices by period. Do NOT switch to CumulativeBars
                    // unless field 87 (day volume) is ever mapped into ticks.
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
                            double filledQty    = order["filledQuantity"]?.Value<double>() ?? 0;
                            double remainingQty = order["remainingQuantity"]?.Value<double>() ?? 0;

                            var status = MapIbStatus(statusStr, filledQty, remainingQty);

                            _orderUpdateSubject.OnNext(new OrderUpdate(
                                orderId, symbol,
                                side.StartsWith("B", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell,
                                filledQty,
                                order["avgPrice"]?.Value<double>() ?? 0,
                                remainingQty,
                                status, false, false, DateTime.UtcNow,
                                Reason: status == OrderStatus.Unknown ? $"IBKR status '{statusStr}'" : null));
                        }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[IB] Malformed feed frame skipped: {ex.GetType().Name}"); }
        }

        /// <summary>Maps an IBKR order-status word to an order status. A working
        /// order with a partial fill IS a partial fill; a working order with none
        /// is <c>New</c> (accepted, resting). The old fallback was
        /// <c>Triggered</c> — silently discarded by the order service — so the
        /// pending states and anything unrecognised vanished without a log line.
        /// Internal for direct testing.</summary>
        internal static OrderStatus MapIbStatus(string statusStr, double filledQty, double remainingQty) => statusStr switch
        {
            "Filled"                       => OrderStatus.Filled,
            "Cancelled" or "ApiCancelled"  => OrderStatus.Cancelled,
            "Inactive"  or "Rejected"      => OrderStatus.Rejected,
            "Submitted" or "PreSubmitted"  => filledQty > 0 && remainingQty > 0
                ? OrderStatus.PartialFill : OrderStatus.New,
            // In-flight transitions: not yet accepted / cancel requested but not
            // confirmed. The venue will follow with a real state; meanwhile the
            // order is best described as accepted-and-working.
            "PendingSubmit" or "PendingCancel" or "ApiPending" => OrderStatus.New,
            _                              => OrderStatus.Unknown,
        };

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
            var conId = CachedConIdFor(request.Symbol) ?? await ResolveConIdAsync(request.Symbol, request.Market);
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
            // IBKR provides snapshot quotes via /iserver/marketdata/snapshot
            var conId = CachedConIdFor(symbol) ?? await ResolveConIdAsync(symbol, "Stock");
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
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
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

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                var response = await _httpClient.GetStringAsync($"{_gatewayUrl}/portfolio/{_accountId}/positions/0");
                var arr = JArray.Parse(response);
                return arr.Select(p => new Position(
                    p["ticker"]?.ToString() ?? p["contractDesc"]?.ToString() ?? "",
                    // Signed as the gateway reports it: consumers derive
                    // long/short from the sign; Abs made a short read as a long.
                    p["position"]?.Value<double>() ?? 0,
                    p["avgCost"]?.Value<double>() ?? 0,
                    p["mktValue"]?.Value<double>() ?? 0,
                    p["unrealizedPnl"]?.Value<double>() ?? 0
                )).ToList();
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

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";

            var conId = CachedConIdFor(signal.Symbol) ?? await ResolveConIdAsync(signal.Symbol, signal.SubType ?? "STK");
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

                    // If-touched orders (MIT/LIT) need the TRIGGER in auxPrice, and
                    // LIT additionally needs the limit price — without them IBKR
                    // rejects or mis-triggers the take-profit.
                    if (signal.Type is OrderType.TakeProfitMarket or OrderType.TakeProfitLimit)
                    {
                        double? trigger = signal.TriggerPrice ?? signal.TakeProfit;
                        if (trigger is not double t)
                            return "ORDER_FAILED:Take-profit order needs a trigger price";
                        orderBody["auxPrice"] = t;
                        if (signal.Type == OrderType.TakeProfitLimit)
                            orderBody["price"] = signal.Price ?? t;
                    }

                    // Protective legs are NOT attached by this provider.
                    //
                    // An IBKR bracket is a parent/child OCA structure, which this
                    // builder does not construct — but SupportsStopLoss and
                    // SupportsTakeProfit above are both true, so the dashboard renders
                    // the fields and the user fills them in. Everything above reads
                    // StopLoss/TakeProfit only as an ENTRY trigger, so on a market or
                    // limit entry they were being dropped on the floor: the user typed
                    // a stop, heard the order confirmed, and held a naked position —
                    // and with stop-distance sizing that position is sized for a stop
                    // that does not exist.
                    //
                    // Refusing is the honest failure. Saying nothing is not.
                    //
                    // Scoped to stop loss and take profit because those are the two this
                    // provider advertises. Trailing is deliberately not named here: the
                    // TrailingStop capability is NOT declared, so those controls never
                    // render for IBKR — and reading the trailing fields purely in order
                    // to refuse them would read to ProviderCapabilityAudit as evidence
                    // that trailing is implemented, which is how a capability claim the
                    // code cannot back gets created rather than caught.
                    bool slIsEntryTrigger = signal.Type is OrderType.StopMarket or OrderType.StopLimit
                                            && signal.StopLoss.HasValue;
                    bool tpIsEntryTrigger = signal.Type is OrderType.TakeProfitMarket or OrderType.TakeProfitLimit
                                            && signal.TriggerPrice == null && signal.TakeProfit.HasValue;
                    if ((signal.StopLoss is > 0 && !slIsEntryTrigger)
                        || (signal.TakeProfit is > 0 && !tpIsEntryTrigger))
                    {
                        return "ORDER_FAILED:this provider cannot attach a stop loss or take profit to an "
                             + "entry order yet. Place the entry on its own, then set the protective levels "
                             + "on the position once it fills";
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

                    // IBKR returns a CHAIN of confirmation prompts (each with a new
                    // id) for suppressible warnings — outside-RTH, size/price caps,
                    // algo disclosures. The old code handled only the first and
                    // confirmed it silently. Now we walk the whole chain, ANNOUNCE
                    // each warning so a blind trader hears what is being confirmed on
                    // a real-money account, and cap the loop so a misbehaving gateway
                    // can't spin forever.
                    int guard = 0;
                    while (first?["message"] != null && !string.IsNullOrEmpty(first["id"]?.ToString()) && guard++ < 8)
                    {
                        var msgText = first["message"] is JArray ma
                            ? string.Join(" ", ma.Select(m => m.ToString()))
                            : first["message"]!.ToString();
                        _errorStream.OnNext($"IBKR order notice (auto-confirmed): {msgText}");

                        var msgId = first["id"]!.ToString();
                        var confirmBody = new JObject { ["confirmed"] = true };
                        var confirmContent = new StringContent(confirmBody.ToString(), Encoding.UTF8, "application/json");
                        var confirmResp = await _httpClient.PostAsync($"{_gatewayUrl}/iserver/reply/{msgId}", confirmContent);
                        var confirmStr = await confirmResp.Content.ReadAsStringAsync();
                        if (!confirmResp.IsSuccessStatusCode) return $"ORDER_FAILED:{confirmStr}";
                        first = JArray.Parse(confirmStr).FirstOrDefault();
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
