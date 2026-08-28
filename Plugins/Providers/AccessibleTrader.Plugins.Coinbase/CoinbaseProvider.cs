using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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

        // Rate limiter: Coinbase Advanced Trade allows ~30 requests/second
        private readonly RateLimiter _rateLimiter = new(30, TimeSpan.FromSeconds(1));

        // Order update stream
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();

        /// <summary>
        /// Coinbase product-id normalisation. The base <see cref="BaseMarketDataProvider.CleanSymbol"/> strips
        /// every separator; Coinbase wants a dash ("BTC-USD"), so this is the one
        /// provider that deviates from the shared normalisation. Consolidating the
        /// three inline <c>Replace("/", "-").ToUpper()</c> sites here means a future
        /// symbol-format change (e.g. fiat pairs with a different separator) is a
        /// one-line edit rather than a three-site sweep.
        /// </summary>
        private static string ToProductId(string symbol)
            => string.IsNullOrEmpty(symbol) ? string.Empty : symbol.Replace("/", "-").ToUpperInvariant();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // True only after the venue ACKNOWLEDGED the user-channel subscription on
        // a currently-connected socket. Coinbase used to inherit the default-true
        // flag while its WS JWT was malformed, so the subscription was rejected,
        // the poller never ran, and fills were announced by no path at all.
        private volatile bool _userChannelUp;
        public bool SupportsOrderEventStreaming => _userChannelUp && (_ws?.IsConnected ?? false);

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
        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => false;
        public override double MaxLeverage          => 1.0;

        public CoinbaseProvider()
        {
            // Phase 4 Track B2 — allow-listed to api.coinbase.com only.
            // The advanced-trade-ws.coinbase.com WS endpoint uses
            // ReconnectingWebSocket, not this HttpClient.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId:   "Coinbase",
                allowedHosts: new[] { "api.coinbase.com" });
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
                using var response = await SendSignedAsync(HttpMethod.Get, $"https://api.coinbase.com{path}", path).ConfigureAwait(false);
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

            var productId = ToProductId(symbol);

            _ws = new ReconnectingWebSocket(
                "wss://advanced-trade-ws.coinbase.com",
                heartbeatInterval: TimeSpan.FromSeconds(30),
                reconnectBaseDelay: TimeSpan.FromSeconds(3))
                .OnConnected(async ws =>
                {
                    _userChannelUp = false; // until the new subscription is acknowledged
                    string jwt;
                    try
                    {
                        var (apiKey, apiSecret) = await CheckoutCoinbaseCredentialsAsync().ConfigureAwait(false);
                        jwt = GenerateWsJwt(apiKey, apiSecret);
                    }
                    catch
                    {
                        // Checkout failed — WS will still connect but private
                        // channels will be rejected server-side. Surface once.
                        jwt = string.Empty;
                    }

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
                .OnDisconnected(() => _userChannelUp = false)
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
                        double price = double.Parse(ticker["price"]?.ToString() ?? "0", CultureInfo.InvariantCulture);
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
                                double px = double.TryParse(u["price_level"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double p) ? p : 0;
                                double qty = double.TryParse(u["new_quantity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double q) ? q : 0;
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
                else if (channel == "subscriptions")
                {
                    // The venue's acknowledgment of what we are ACTUALLY subscribed
                    // to. Only here does the user channel count as up — a rejected
                    // subscription (bad JWT) leaves the socket healthy and silent,
                    // which is exactly the state the old default-true flag hid.
                    if (msg.ToString().Contains("\"user\"", StringComparison.Ordinal))
                        _userChannelUp = true;
                }
                else if (channel == "user")
                {
                    _userChannelUp = true;
                    var events = msg["events"] as JArray;
                    var orders = events?.FirstOrDefault()?["orders"] as JArray;
                    if (orders != null)
                    {
                        foreach (var o in orders)
                        {
                            var statusStr = o["status"]?.ToString() ?? "";
                            var sideStr = o["side"]?.ToString() ?? "";
                            double fs = double.TryParse(o["filled_size"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double fsv) ? fsv : 0;
                            var status = MapToOrderStatus(statusStr, fs);
                            _orderUpdateSubject.OnNext(new OrderUpdate(
                                o["order_id"]?.ToString() ?? "",
                                o["product_id"]?.ToString() ?? "",
                                sideStr == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                                fs,
                                double.TryParse(o["avg_price"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double ap) ? ap : 0,
                                double.TryParse(o["leaves_quantity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lq) ? lq : 0,
                                status,
                                false, false, DateTime.UtcNow,
                                Reason: status == OrderStatus.Unknown ? $"Coinbase status '{statusStr}'" : null));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // One bad frame shouldn't kill the WebSocket, but staying silent meant the
                // user never learned why their order updates stopped flowing after a feed
                // change. Publish a one-shot diagnostic per type of failure.
                _errorStream.OnNext($"Coinbase user-update parse failed: {ex.GetType().Name}");
            }
        }

        /// <summary>Maps a user-channel order status to an order status. The
        /// <c>user</c> channel sends <c>status: "OPEN"</c> with
        /// <c>filled_size: "0"</c> the instant an order rests — the old
        /// unconditional OPEN→PartialFill mapping announced "partially filled"
        /// for every freshly-accepted limit order. OPEN is a partial fill only
        /// when something actually filled. The old fallback was <c>Triggered</c>
        /// (silently discarded), which swallowed FAILED — a refusal the trader
        /// never heard. Internal for direct testing.</summary>
        internal static OrderStatus MapToOrderStatus(string status, double filledSize) => status.ToUpperInvariant() switch
        {
            "FILLED"    => OrderStatus.Filled,
            "CANCELLED" => OrderStatus.Cancelled,
            "EXPIRED"   => OrderStatus.Expired,
            "REJECTED" or "FAILED" => OrderStatus.Rejected,
            "OPEN"      => filledSize > 0 ? OrderStatus.PartialFill : OrderStatus.New,
            "PENDING" or "QUEUED" => OrderStatus.New,
            _           => OrderStatus.Unknown,
        };

        private TimeSpan MapTimeframeToTimeSpan(string tf) => tf.ToLowerInvariant() switch
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

            // Drop references to the JWT-signing PEM private key so a crash
            // dump after disconnect can't recover the key material.
            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            var product     = ToProductId(request.Symbol);
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
                    var response = await GetSignedStringAsync(url, path).ConfigureAwait(false);
                    var json     = JObject.Parse(response);
                    var candles  = json["candles"] as JArray;
                    if (candles == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = candles.Select(c => new Ohlcv(
                        DateTimeOffset.FromUnixTimeSeconds(long.Parse(c["start"]?.ToString() ?? "0", CultureInfo.InvariantCulture)).UtcDateTime,
                        double.Parse(c["open"]?.ToString()   ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(c["high"]?.ToString()   ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(c["low"]?.ToString()    ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(c["close"]?.ToString()  ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(c["volume"]?.ToString() ?? "0", CultureInfo.InvariantCulture)))
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
                    string path = "/api/v3/brokerage/products";
                    var response = await GetSignedStringAsync($"https://api.coinbase.com{path}", path).ConfigureAwait(false);
                    var json     = JObject.Parse(response);
                    var products = json["products"] as JArray;
                    return products?.Select(p => p["product_id"]?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s).ToList()
                        ?? new List<string>();
                });
            }
            catch (Exception ex)
            {
                // An empty symbol list and a failed products call look identical in the picker.
                _errorStream.OnNext($"Coinbase symbol list unavailable: {ex.GetType().Name}");
                return new List<string>();
            }
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
                    var cleanSymbol = ToProductId(symbol);
                    string path = "/api/v3/brokerage/product_book";
                    var response = await GetSignedStringAsync($"https://api.coinbase.com{path}?product_id={cleanSymbol}&limit={limit}", path).ConfigureAwait(false);
                    var book = JObject.Parse(response)["pricebook"];
                    var bids = (book?["bids"] as JArray)?.Select(b => new OrderBookEntry(
                        double.Parse(b["price"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(b["size"]?.ToString()  ?? "0", CultureInfo.InvariantCulture))).ToList() ?? new();
                    var asks = (book?["asks"] as JArray)?.Select(a => new OrderBookEntry(
                        double.Parse(a["price"]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(a["size"]?.ToString()  ?? "0", CultureInfo.InvariantCulture))).ToList() ?? new();
                    return (bids, asks);
                });
            }
            catch (Exception ex)
            {
                // SAY SO. A bare catch here reported a failed read as a book with no liquidity —
                // for a sighted user an empty depth ladder is a visible oddity, for this
                // product's audience the two are the same thing.
                _errorStream.OnNext($"Coinbase order book unavailable for {symbol}: {ex.GetType().Name}");
                return (new(), new());
            }
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
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                string path = "/api/v3/brokerage/accounts";
                var response = await GetSignedStringAsync($"https://api.coinbase.com{path}", path).ConfigureAwait(false);
                var json     = JObject.Parse(response);
                var accounts = json["accounts"] as JArray;
                if (accounts == null) return new List<Balance>();
                return accounts
                    .Where(a =>
                        double.TryParse(a["available_balance"]?["value"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double av) && av > 0
                        || double.TryParse(a["hold"]?["value"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double hold) && hold > 0)
                    .Select(a => new Balance(
                        a["currency"]?.ToString() ?? "",
                        double.TryParse(a["available_balance"]?["value"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double avf) ? avf : 0,
                        double.TryParse(a["hold"]?["value"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double hf) ? hf : 0))
                    .ToList();
            });
        }

        public Task<List<Position>> GetPositionsAsync() => Task.FromResult(new List<Position>());

        /// <summary>Fill history via /orders/historical/fills (History tab parity —
        /// returned the interface default empty until 2026-07-22). Deliberately no
        /// query string: the auth header signs the bare path, so filtering and the
        /// limit are applied client-side on the default page.</summary>
        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            if (!IsConfigured) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                string path = "/api/v3/brokerage/orders/historical/fills";
                var response = await GetSignedStringAsync($"https://api.coinbase.com{path}", path).ConfigureAwait(false);
                var json = JObject.Parse(response);
                var arr = json["fills"] as JArray;
                if (arr == null) return new List<TradeFill>();

                var fills = new List<TradeFill>();
                foreach (var f in arr)
                {
                    string sym = f["product_id"]?.ToString() ?? "";
                    if (symbol != null && !sym.Replace("-", "/").Equals(symbol, StringComparison.OrdinalIgnoreCase)
                        && !sym.Equals(symbol, StringComparison.OrdinalIgnoreCase)) continue;
                    fills.Add(new TradeFill(
                        f["trade_id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                        sym,
                        (f["side"]?.ToString() ?? "BUY").Equals("SELL", StringComparison.OrdinalIgnoreCase)
                            ? OrderSide.Sell : OrderSide.Buy,
                        f["size"]?.Value<double>() ?? 0,
                        f["price"]?.Value<double>() ?? 0,
                        f["trade_time"]?.Value<DateTime>() ?? DateTime.MinValue,
                        f["commission"]?.Value<double>() ?? 0,
                        f["order_id"]?.ToString()));
                }
                return fills.OrderByDescending(x => x.FilledAt).Take(limit).ToList();
            });
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConfigured) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                string path = "/api/v3/brokerage/orders/historical/batch";
                string query = "?order_status=OPEN";
                if (!string.IsNullOrEmpty(symbol))
                    query += $"&product_id={ToProductId(symbol)}";

                var response = await GetSignedStringAsync($"https://api.coinbase.com{path}{query}", path).ConfigureAwait(false);
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
                        ?? o["filled_size"]?.ToString() ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture, out double qty) ? qty : 0,
                    double.TryParse(
                        o["order_configuration"]?["limit_limit_gtc"]?["limit_price"]?.ToString()
                        ?? o["average_filled_price"]?.ToString() ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture, out double avgPx) ? avgPx : 0,
                    o["status"]?.ToString() ?? ""
                )).ToList();
            });
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
            // The idempotency key is minted HERE, outside the limiter call, and never
            // inside it. Coinbase rejects a repeat of a client_order_id, which is the
            // only thing that can turn a re-sent order into a no-op — and a key
            // generated inside the lambda would be regenerated by any retry, so the
            // exchange would see a brand-new order instead of a duplicate. The lambda
            // no longer retries either (ExecuteOnceAsync), but a stable key still
            // matters: GeneralOrderService's recovery scan and any caller-supplied
            // ClientOid both depend on the id on the wire being the id we chose.
            string clientOid = signal.ClientOid ?? Guid.NewGuid().ToString();
            try
            {
                return await _rateLimiter.ExecuteOnceAsync(async () =>
                {
                    string productId = ToProductId(signal.Symbol);

                    // F8 carries a sub-cent price without collapsing it, so the precision here
                    // was already right — but none of these carried a culture, so on a
                    // comma-decimal machine every size and price on the wire read "0,04000000".
                    // Same defect class as Bitstamp's F2, one field short of it.
                    JObject orderConfig;
                    if (signal.Type == OrderType.Market)
                        orderConfig = new JObject { ["market_market_ioc"] = new JObject { ["base_size"] = signal.Quantity.ToString("F8", CultureInfo.InvariantCulture) } };
                    else if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                        orderConfig = new JObject { ["limit_limit_gtc"] = new JObject { ["base_size"] = signal.Quantity.ToString("F8", CultureInfo.InvariantCulture), ["limit_price"] = signal.Price.Value.ToString("F8", CultureInfo.InvariantCulture), ["post_only"] = false } };
                    else if (signal.Type == OrderType.StopMarket && signal.StopLoss.HasValue)
                    {
                        orderConfig = new JObject
                        {
                            ["stop_limit_stop_limit_gtc"] = new JObject
                            {
                                ["base_size"] = signal.Quantity.ToString("F8", CultureInfo.InvariantCulture),
                                ["limit_price"] = (signal.StopLoss.Value * (signal.Side == OrderSide.Buy ? 1.05 : 0.95)).ToString("F8", CultureInfo.InvariantCulture),
                                ["stop_price"] = signal.StopLoss.Value.ToString("F8", CultureInfo.InvariantCulture),
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
                                ["base_size"] = signal.Quantity.ToString("F8", CultureInfo.InvariantCulture),
                                ["limit_price"] = signal.Price.Value.ToString("F8", CultureInfo.InvariantCulture),
                                ["stop_price"] = signal.StopLoss.Value.ToString("F8", CultureInfo.InvariantCulture),
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
                    var content  = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
                    using var response = await SendSignedAsync(HttpMethod.Post, $"https://api.coinbase.com{path}", path, content).ConfigureAwait(false);
                    var respStr  = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) return $"ORDER_FAILED:{respStr}";
                    var json = JObject.Parse(respStr);
                    return json["success_response"]?["order_id"]?.ToString() ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Coinbase order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConfigured) return false;
            try
            {
                // Through the rate limiter, like every other trading call in this file — this
                // was the only one that went straight to the wire. ExecuteOnceAsync, not
                // ExecuteAsync: a cancel MUTATES, and the retry-on-timeout that is right for a
                // GET is what re-sends a request the venue already booked.
                return await _rateLimiter.ExecuteOnceAsync(async () =>
                {
                    var body     = new JObject { ["order_ids"] = new JArray { orderId } };
                    string path = "/api/v3/brokerage/orders/batch_cancel";
                    var content  = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
                    using var response = await SendSignedAsync(HttpMethod.Post, $"https://api.coinbase.com{path}", path, content).ConfigureAwait(false);
                    return response.IsSuccessStatusCode;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // "We could not reach Coinbase" and "Coinbase refused the cancel" both used to
                // come back as a bare false, which the UI speaks as the order already being
                // gone. They are opposite facts: one means the order is still live and still
                // yours to manage. The bool stays false because the cancel did not happen, and
                // the reason is now SAID.
                _errorStream.OnNext(
                    $"Coinbase could not cancel order {orderId}: {ex.GetType().Name}. The order may still be working.");
                return false;
            }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        // ── Auth helpers ─────────────────────────────────────────────────────

        // Sign-time credential checkout (phase 4 Track B). Prefers the
        // PluginHostServices.ApiKeys bridge; falls back to Configure-populated
        // fields so unit tests and CLI runs still work.
        private async Task<(string Key, string Secret)> CheckoutCoinbaseCredentialsAsync()
        {
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("Coinbase").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("Coinbase: no active API key configured.");
                return (checkout.Key, checkout.Secret);
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                throw new InvalidOperationException("Coinbase: no API credentials configured.");
            return (_apiKey!, _apiSecret!);
        }

        // Per-request signed send. Building a fresh HttpRequestMessage with its OWN
        // Authorization header avoids the race the old shared-DefaultRequestHeaders
        // approach had: two concurrent signed calls (the rate limiter permits
        // concurrency) could overwrite each other's path-bound JWT, so one request
        // went out signed for the OTHER's path and Coinbase rejected it.
        private async Task<HttpResponseMessage> SendSignedAsync(
            HttpMethod method, string url, string requestPath, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url);
            if (content != null) request.Content = content;
            try
            {
                var (apiKey, apiSecret) = await CheckoutCoinbaseCredentialsAsync().ConfigureAwait(false);
                var jwt = GenerateJwt(apiKey, apiSecret, method.Method, requestPath);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
            }
            catch
            {
                // Leave the request unauthenticated; it fails and surfaces via the
                // caller's own error handling rather than racing a shared header.
            }
            return await _httpClient.SendAsync(request).ConfigureAwait(false);
        }

        private async Task<string> GetSignedStringAsync(string url, string requestPath)
        {
            using var resp = await SendSignedAsync(HttpMethod.Get, url, requestPath).ConfigureAwait(false);
            return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>WebSocket JWT. Same key and ES256 signing as the REST one,
        /// but the CDP WebSocket contract has NO uri claim — there is no
        /// method/path to bind — and requires a nonce header. The old code pushed
        /// the WS HOST through the REST path builder, producing
        /// uri = "GET api.coinbase.com/advanced-trade-ws.coinbase.com" and no
        /// nonce: the user-channel subscription was rejected server-side, and
        /// with SupportsOrderEventStreaming defaulting true the poller never ran,
        /// so Coinbase fills were announced by no path at all.</summary>
        internal string GenerateWsJwt(string apiKey, string apiSecret)
            => GenerateJwtCore(apiKey, apiSecret, uri: null, withNonce: true);

        internal string GenerateJwt(string apiKey, string apiSecret, string method, string requestPath)
        {
            var cleanPath = requestPath.StartsWith("/") ? requestPath : "/" + requestPath;
            if (!cleanPath.Contains("api.coinbase.com"))
                cleanPath = "api.coinbase.com" + cleanPath;

            return GenerateJwtCore(apiKey, apiSecret, $"{method} {cleanPath}", withNonce: false);
        }

        private string GenerateJwtCore(string apiKey, string apiSecret, string? uri, bool withNonce)
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

            using var ecdsa = System.Security.Cryptography.ECDsa.Create();
            try
            {
                ecdsa.ImportFromPem(apiSecret);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Coinbase Auth Error: Failed to import private key. {ex.Message}");
                return "AUTH_ERROR";
            }

            var key = new Microsoft.IdentityModel.Tokens.ECDsaSecurityKey(ecdsa) { KeyId = apiKey };
            var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.EcdsaSha256);

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var claims = new List<System.Security.Claims.Claim>
            {
                new("sub", apiKey),
                new("iss", "cdp"),
                new("nbf", now.ToString(CultureInfo.InvariantCulture)),
                new("exp", (now + 120).ToString(CultureInfo.InvariantCulture)),
            };
            // REST binds the token to "METHOD host/path"; the WS token has no
            // request to bind and must omit the claim entirely.
            if (uri != null) claims.Add(new System.Security.Claims.Claim("uri", uri));

            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                issuer: "cdp",
                audience: "cdp_service",
                claims: claims,
                signingCredentials: credentials);

            jwt.Header["kid"] = apiKey;
            if (withNonce)
                jwt.Header["nonce"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            jwt.Header.Remove("typ");

            return handler.WriteToken(jwt);
        }

        private string MapToCoinbaseGranularity(string tf) => tf.ToLowerInvariant() switch
        {
            "1m"  => "ONE_MINUTE",
            "5m"  => "FIVE_MINUTE",
            "15m" => "FIFTEEN_MINUTE",
            "1h"  => "ONE_HOUR",
            "6h"  => "SIX_HOUR",
            "1d"  => "ONE_DAY",
            _     => "ONE_HOUR"
        };

        private int MapTimeframeToSeconds(string tf) => tf.ToLowerInvariant() switch
        {
            "1m"  => 60,
            "5m"  => 300,
            "15m" => 900,
            "1h"  => 3600,
            "6h"  => 21600,
            "1d"  => 86400,
            _     => 3600
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
                _ws?.Dispose();
                _orderUpdateSubject?.Dispose();
                _orderBookSubject?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
