using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

namespace AccessibleTrader.Plugins.Bitstamp
{
    public class BitstampProvider : BaseMarketDataProvider, ITradingProvider, IOrderBookProvider
    {
        private readonly HttpClient _httpClient;
        private ReconnectingWebSocket? _ws;
        private string? _currentChannel;
        private string? _orderBookChannel;
        private string? _privateOrderChannel;
        private bool _isSubscribed;
        private DateTime _lastTickTime = DateTime.MinValue;
        private readonly TimeSpan _tickThrottle = TimeSpan.FromMilliseconds(250);

        private string? _lastMarket;
        private string? _lastSymbol;
        private string? _lastTimeframe;

        private string? _apiKey;
        private string? _apiSecret;
        private string? _customerId;

        // Rate limiter: Bitstamp allows ~8000 requests/10 minutes
        private readonly RateLimiter _rateLimiter = new(800, TimeSpan.FromMinutes(1));

        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // DELIBERATELY the static default (true) — the one provider of the six
        // audit-flagged ones where flipping on socket state would be WORSE:
        // Bitstamp implements no GetFillsAsync and no order-status query, so the
        // poller's open-list heuristic would announce a FILLED order as
        // "cancelled" (left the open list, no fill record). Until a
        // GetFillsAsync exists, a quiet stream is a smaller lie than a wrong
        // terminal state. See TODO's SupportsOrderEventStreaming-honesty item.
        public bool SupportsOrderEventStreaming => true;

        // Last-known remaining amount per live order id, captured from the
        // private-my_orders_ stream's order_created / order_changed events so we
        // can report the incremental fill quantity (Bitstamp only sends the
        // order's *remaining* amount, never the filled delta) and tell a fully-
        // filled order_deleted (remaining 0) apart from a user cancel (remaining
        // > 0). Bounded implicitly by the number of concurrently-open orders.
        private readonly ConcurrentDictionary<string, double> _orderRemaining = new();

        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();

        private const string BaseUrl = "https://www.bitstamp.net/api/v2";
        private const string WsUrl   = "wss://ws.bitstamp.net";

        public override string Name => "Bitstamp";
        public override string Description => "Bitstamp Exchange Data & Trading";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 1000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.ThreeMinutes, StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes, StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour,
            StandardTimeframes.TwoHours, StandardTimeframes.FourHours, StandardTimeframes.SixHours,
            StandardTimeframes.TwelveHours, StandardTimeframes.OneDay, StandardTimeframes.ThreeDays
        };

        public bool IsConnected => _ws?.IsConnected ?? false;
        public override bool SupportsStopLoss       => false;
        public override bool SupportsTakeProfit     => false;
        public override double MaxLeverage          => 1.0;

        private bool IsTradeConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_apiSecret);

        public BitstampProvider()
        {
            // Phase 4 Track B2 — allow-listed to www.bitstamp.net only.
            // ws.bitstamp.net uses ReconnectingWebSocket, not this HttpClient.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId:   "Bitstamp",
                allowedHosts: new[] { "www.bitstamp.net" });
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
            if (config.TryGetValue("ApiKey",     out var k)) _apiKey     = k;
            if (config.TryGetValue("ApiSecret",  out var s)) _apiSecret  = s;
            if (config.TryGetValue("CustomerId", out var c)) _customerId = c;
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsTradeConfigured) return (true, "No API key provided (public data only)");
            try
            {
                var response = await PostAuthenticatedAsync("/api/v2/balance/", new Dictionary<string, string>());
                var json = JObject.Parse(response);
                if (json["status"]?.ToString() == "error")
                    return (false, $"Key validation failed: {json["reason"]}");
                return (true, "API key validated successfully");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        public override async Task EnsureConnectedAsync()
        {
            if (_ws?.IsConnected == true) return;

            await DisconnectAsync();

            _connectionStateStream.OnNext(ConnectionState.Connecting);

            try
            {
                _ws = new ReconnectingWebSocket(WsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                    .OnConnected(async ws =>
                    {
                        // Re-subscribe to channels after reconnection
                        if (!string.IsNullOrEmpty(_currentChannel))
                        {
                            await ws.SendAsync($"{{\"event\":\"bts:subscribe\",\"data\":{{\"channel\":\"{_currentChannel}\"}}}}");
                            if (!string.IsNullOrEmpty(_orderBookChannel))
                                await ws.SendAsync($"{{\"event\":\"bts:subscribe\",\"data\":{{\"channel\":\"{_orderBookChannel}\"}}}}");
                            if (!string.IsNullOrEmpty(_privateOrderChannel) && IsTradeConfigured)
                                await SubscribePrivateChannelInternalAsync(ws, _privateOrderChannel);
                        }
                    })
                    .OnMessage(HandleWebSocketMessage)
                    .OnError(err => _errorStream.OnNext($"Bitstamp WS: {err}"))
                    .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

                await _ws.ConnectAsync();
                _connectionStateStream.OnNext(ConnectionState.Connected);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Bitstamp connection failed: {ex.Message}");
                _connectionStateStream.OnNext(ConnectionState.Error);
                throw;
            }
        }

        /// <summary>Parses a live_trades event into a single-trade Ohlcv tick
        /// (price on all four legs, the trade's own amount as volume — delta
        /// semantics). Internal for direct testing.</summary>
        internal static bool TryParseTrade(JObject json, out Ohlcv bar)
        {
            bar = default;
            var data = json["data"];
            if (data == null) return false;

            double price  = data["price"]?.Value<double>() ?? 0;
            double amount = data["amount"]?.Value<double>() ?? 0;
            long.TryParse(data["timestamp"]?.ToString()?.Split('.')[0], out long timestamp);
            var barDate = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            if (price <= 0 || barDate < new DateTime(2009, 1, 1, 0, 0, 0, DateTimeKind.Utc)) return false;

            bar = new Ohlcv(barDate, price, price, price, price, amount);
            return true;
        }

        /// <summary>
        /// Canonical Bitstamp market key (e.g. "btcusd") for a UI symbol. Strips
        /// separators, lower-cases, and routes a Tether-quoted symbol to the USD
        /// book (Bitstamp quotes USD; the app's canonical symbols use USDT). ALL
        /// data, live, order-book and private-channel paths must go through this
        /// so historical and live feeds always target the same market — a live
        /// path that skipped the usdt→usd remap subscribed to a dead channel.
        /// Only the trailing quote is remapped, never a base that merely contains
        /// "usdt".
        /// </summary>
        internal static string ToBitstampPair(string symbol)
        {
            var s = symbol.Replace("/", "").Replace("-", "").ToLowerInvariant();
            if (s.EndsWith("usdt")) s = string.Concat(s.AsSpan(0, s.Length - 4), "usd");
            return s;
        }

        /// <summary>
        /// The usdt→usd remap above is not merely a wire-format detail: it means two different UI
        /// symbols name the SAME Bitstamp book. A ledger keying on the display string therefore
        /// splits one market in two — which is exactly how an account came to hold a long under
        /// "BTC/USD" and a short under "BTCUSDT" on this venue, offsetting positions that no net
        /// exposure or risk check could see as related. Canonical form is the book itself.
        /// </summary>
        public override string GetCanonicalSymbol(string symbol) =>
            string.IsNullOrEmpty(symbol) ? string.Empty : ToBitstampPair(symbol).ToUpperInvariant();

        // ── Keyed-feed subscriptions (multiple concurrent live streams) ───────

        public override bool SupportsMultipleLiveSubscriptions => true;

        /// <summary>
        /// One dedicated public websocket per subscription, subscribed to its own
        /// live_trades channel — fully independent of the provider's main socket
        /// and its order-book/private channels. ReconnectingWebSocket owns
        /// reconnection (and re-subscribes via OnConnected) for the handle's
        /// lifetime; disposing the returned socket unsubscribes. No tick throttle
        /// here: the consolidator collapses trades into period bars and background
        /// feeds don't render, so there is no UI churn to protect.
        /// </summary>
        public override async Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
        {
            string channel = $"live_trades_{ToBitstampPair(symbol)}";
            var consolidator = new BarBucketConsolidator(timeframe, LiveTickStyle.TradeDeltas);

            var ws = new ReconnectingWebSocket(WsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnError(err => _errorStream.OnNext($"Bitstamp keyed stream ({channel}): {err}"));
            string subscribeMessage = new JObject
            {
                ["event"] = "bts:subscribe",
                ["data"] = new JObject { ["channel"] = channel },
            }.ToString(Newtonsoft.Json.Formatting.None);
            ws.OnConnected(async w => await w.SendAsync(subscribeMessage))
              .OnMessage(msg =>
                {
                    try
                    {
                        var json = JObject.Parse(msg);
                        if (json["event"]?.ToString() != "trade") return;
                        if (json["channel"]?.ToString() != channel) return;
                        if (!TryParseTrade(json, out var tick)) return;
                        var bar = consolidator.Apply(tick);
                        if (bar.HasValue) onBar(bar.Value);
                    }
                    catch { /* malformed frame */ }
                });

            await ws.ConnectAsync();
            return ws; // ReconnectingWebSocket is IAsyncDisposable — disposal closes the socket
        }

        /// <summary>Routes a raw websocket frame to the trade / order-book /
        /// private-order handlers. Internal so tests can drive the private-order
        /// mapping through the real channel-matching path.</summary>
        internal void HandleWebSocketMessage(string msg)
        {
            try
            {
                var json = JObject.Parse(msg);
                var ev = json["event"]?.ToString();
                var channel = json["channel"]?.ToString();

                if (ev == "trade")
                {
                    if (!TryParseTrade(json, out var bar)) return;

                    var now = DateTime.UtcNow;
                    if (now - _lastTickTime >= _tickThrottle)
                    {
                        _lastTickTime = now;
                        _liveStream.OnNext(bar);
                    }
                }
                else if (ev == "data" && channel != null && channel.StartsWith("diff_order_book_"))
                {
                    var data = json["data"];
                    if (data != null)
                    {
                        var symbol = channel.Replace("diff_order_book_", "").ToUpperInvariant();
                        var bids = (data["bids"] as JArray)?.Select(b => new OrderBookEntry(double.Parse(b[0]!.ToString(), CultureInfo.InvariantCulture), double.Parse(b[1]!.ToString(), CultureInfo.InvariantCulture))).ToList() ?? new();
                        var asks = (data["asks"] as JArray)?.Select(a => new OrderBookEntry(double.Parse(a[0]!.ToString(), CultureInfo.InvariantCulture), double.Parse(a[1]!.ToString(), CultureInfo.InvariantCulture))).ToList() ?? new();
                        _orderBookSubject.OnNext(new OrderBookUpdate(symbol, bids, asks, 0, DateTime.UtcNow));
                    }
                }
                else if ((ev == "order_created" || ev == "order_changed" || ev == "order_deleted") &&
                         channel != null && channel.StartsWith("private-my_orders_"))
                {
                    HandlePrivateOrderEvent(ev!, channel, json["data"] as JObject);
                }
                else if (ev == "bts:subscription_succeeded")
                {
                    _isSubscribed = true;
                }
                else if (ev == "bts:request_reconnect")
                {
                    // ReconnectingWebSocket handles this automatically
                    _connectionStateStream.OnNext(ConnectionState.Disconnected);
                }
            }
            catch (Exception ex) { _errorStream.OnNext($"WebSocket message error: {ex.Message}"); }
        }

        /// <summary>
        /// Maps a Bitstamp private-my_orders_ order event to an OrderUpdate.
        /// Bitstamp only reports the order's REMAINING amount ("amount"), never a
        /// filled delta, and its order_deleted covers both "fully filled" and
        /// "cancelled" — so we track the last-known remaining per order id to (a)
        /// report the incremental fill quantity and (b) distinguish a completed
        /// fill (remaining ≈ 0) from a user cancel (remaining &gt; 0). order_created
        /// only registers the baseline; it isn't announced (no working-order
        /// status exists in the enum). order_type is 0 = buy, 1 = sell.
        /// </summary>
        private void HandlePrivateOrderEvent(string ev, string channel, JObject? data)
        {
            if (data == null) return;

            const double eps = 1e-9;
            string orderId = data["id"]?.ToString() ?? data["id_str"]?.ToString() ?? "";
            string pair    = channel.Replace("private-my_orders_", "").ToUpperInvariant();
            double amount  = data["amount"]?.Value<double>() ?? 0;   // remaining on the order
            double price   = data["price"]?.Value<double>() ?? 0;
            var    side    = (data["order_type"]?.Value<int>() ?? 0) == 0 ? OrderSide.Buy : OrderSide.Sell;
            var    ts      = ParseOrderEventTime(data);

            if (ev == "order_created")
            {
                if (!string.IsNullOrEmpty(orderId)) _orderRemaining[orderId] = amount;
                return; // baseline only — nothing to announce yet
            }

            double prevRemaining = (!string.IsNullOrEmpty(orderId) && _orderRemaining.TryGetValue(orderId, out var p))
                ? p : amount;
            double filledDelta = Math.Max(0, prevRemaining - amount);

            OrderStatus status;
            if (ev == "order_deleted")
            {
                status = amount > eps ? OrderStatus.Cancelled : OrderStatus.Filled;
                if (!string.IsNullOrEmpty(orderId)) _orderRemaining.TryRemove(orderId, out _);
            }
            else // order_changed — a partial fill reduced the remaining amount
            {
                status = amount > eps ? OrderStatus.PartialFill : OrderStatus.Filled;
                if (!string.IsNullOrEmpty(orderId))
                {
                    if (status == OrderStatus.Filled) _orderRemaining.TryRemove(orderId, out _);
                    else                              _orderRemaining[orderId] = amount;
                }
            }

            _orderUpdateSubject.OnNext(new OrderUpdate(
                orderId, pair, side,
                filledDelta, price, amount, status,
                false, false, ts));
        }

        /// <summary>Event time from Bitstamp's microtimestamp (µs) or datetime (s),
        /// falling back to now.</summary>
        private static DateTime ParseOrderEventTime(JObject data)
        {
            if (long.TryParse(data["microtimestamp"]?.ToString(), out var micros) && micros > 0)
                return DateTimeOffset.FromUnixTimeMilliseconds(micros / 1000).UtcDateTime;
            if (long.TryParse(data["datetime"]?.ToString()?.Split('.')[0], out var secs) && secs > 0)
                return DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
            return DateTime.UtcNow;
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();

            var cleanSymbol = ToBitstampPair(symbol);
            string newChannel = $"live_trades_{cleanSymbol}";
            string newBookChannel = $"diff_order_book_{cleanSymbol}";

            if (_currentChannel == newChannel && _isSubscribed) return;

            if (!string.IsNullOrEmpty(_currentChannel) && _isSubscribed && _ws != null)
            {
                await _ws.SendAsync($"{{\"event\":\"bts:unsubscribe\",\"data\":{{\"channel\":\"{_currentChannel}\"}}}}");
                if (!string.IsNullOrEmpty(_orderBookChannel))
                    await _ws.SendAsync($"{{\"event\":\"bts:unsubscribe\",\"data\":{{\"channel\":\"{_orderBookChannel}\"}}}}");
                _isSubscribed = false;
            }

            _currentChannel = newChannel;
            _orderBookChannel = newBookChannel;
            _lastMarket = market; _lastSymbol = symbol; _lastTimeframe = timeframe;

            if (_ws != null)
            {
                await _ws.SendAsync($"{{\"event\":\"bts:subscribe\",\"data\":{{\"channel\":\"{_currentChannel}\"}}}}");
                await _ws.SendAsync($"{{\"event\":\"bts:subscribe\",\"data\":{{\"channel\":\"{_orderBookChannel}\"}}}}");

                if (IsTradeConfigured)
                {
                    _privateOrderChannel = $"private-my_orders_{cleanSymbol}";
                    await SubscribePrivateChannelInternalAsync(_ws, _privateOrderChannel);
                }
            }
        }

        private async Task SubscribePrivateChannelInternalAsync(ReconnectingWebSocket ws, string channel)
        {
            try
            {
                var (apiKey, apiSecret, _) = await CheckoutBitstampCredentialsAsync().ConfigureAwait(false);

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string nonce   = Guid.NewGuid().ToString("N");
                string message = nonce + timestamp.ToString() + apiKey;
                byte[] secretBytes = Encoding.UTF8.GetBytes(apiSecret);
                using var hmac = new System.Security.Cryptography.HMACSHA256(secretBytes);
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                string sig  = BitConverter.ToString(hash).Replace("-", "").ToUpper();
                Array.Clear(secretBytes, 0, secretBytes.Length);

                var authMsg = new JObject
                {
                    ["event"] = "bts:subscribe",
                    ["data"]  = new JObject
                    {
                        ["channel"] = channel,
                        ["auth"]    = new JObject
                        {
                            ["key"]              = apiKey,
                            ["signature"]        = sig,
                            ["nonce"]            = nonce,
                            ["timestamp"]        = timestamp,
                            ["valid_for_seconds"] = 900
                        }
                    }
                };
                await ws.SendAsync(authMsg.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch { /* non-critical */ }
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) => _orderBookSubject.AsObservable();

        public override async Task DisconnectAsync()
        {
            if (_ws != null)
            {
                await _ws.DisconnectAsync();
                _ws.Dispose();
                _ws = null;
            }
            _isSubscribed        = false;
            _currentChannel      = null;
            _orderBookChannel    = null;
            _privateOrderChannel = null;

            // Drop references to HMAC-SHA256 signing material so a crash
            // dump after disconnect can't recover the API key/secret/customer-id.
            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null,
                () => _customerId = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        public override async Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => new List<string> { "Spot" };

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{BaseUrl}/trading-pairs-info/");
                    var arr = JArray.Parse(response);
                    return arr.Select(p => p["url_symbol"]?.ToString().ToUpper() ?? "").OrderBy(s => s).ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Bitstamp GetSymbolsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string> { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "12h", "1d", "3d", "1w", "1M" });

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var cleanSymbol = ToBitstampPair(request.Symbol);
            // Regex-based parser handles every N<unit> combination; returns 0 on an
            // unrecognised timeframe, which the guard below maps to the same empty-
            // result shape the legacy ToSeconds returned for -1.
            var step = AccessibleTrader.Sdk.Models.TimeframeUtility.ToSeconds(request.Timeframe);
            if (step <= 0) return (new List<Ohlcv>(), new List<(long, double)>());

            int limit = Math.Min(request.Limit, 1000);
            string url = $"{BaseUrl}/ohlc/{cleanSymbol}/?step={step}&limit={limit}";
            if (request.Since.HasValue) url += $"&start={request.Since.Value / 1000}";
            if (request.Until.HasValue) url += $"&end={request.Until.Value / 1000}";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        _errorStream.OnNext($"Bitstamp data unavailable for {request.Symbol} ({(int)response.StatusCode} {response.StatusCode}).");
                        return (new List<Ohlcv>(), new List<(long, double)>());
                    }

                    var jsonStr = await response.Content.ReadAsStringAsync();
                    var json    = JObject.Parse(jsonStr);
                    var ohlcArray = json["data"]?["ohlc"] as JArray;
                    if (ohlcArray == null)
                    {
                        var reason = json["reason"]?.ToString() ?? json["error"]?.ToString();
                        if (!string.IsNullOrEmpty(reason))
                            _errorStream.OnNext($"Bitstamp data error for {request.Symbol}: {reason}");
                        return (new List<Ohlcv>(), new List<(long, double)>());
                    }

                    var ohlcvList = ohlcArray.Select(item => new Ohlcv(
                        DateTimeOffset.FromUnixTimeSeconds(long.Parse(item["timestamp"]?.ToString() ?? "0")).UtcDateTime,
                        double.Parse(item["open"]?.ToString()   ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(item["high"]?.ToString()   ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(item["low"]?.ToString()    ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(item["close"]?.ToString()  ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(item["volume"]?.ToString() ?? "0", CultureInfo.InvariantCulture)
                    )).Where(x => x.Open > 0 && x.High > 0 && x.Low > 0 && x.Close > 0).OrderBy(x => x.Date).ToList();

                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Bitstamp FetchOhlcvAsync failed for {request.Symbol} ({ex.GetType().Name}): {ex.Message}");
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
            var cleanSymbol = ToBitstampPair(symbol);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{BaseUrl}/order_book/{cleanSymbol}/");
                    var json = JObject.Parse(response);
                    var bids = (json["bids"] as JArray)?.Take(limit).Select(b => new OrderBookEntry(double.Parse(b[0]!.ToString(), CultureInfo.InvariantCulture), double.Parse(b[1]!.ToString(), CultureInfo.InvariantCulture))).ToList() ?? new();
                    var asks = (json["asks"] as JArray)?.Take(limit).Select(a => new OrderBookEntry(double.Parse(a[0]!.ToString(), CultureInfo.InvariantCulture), double.Parse(a[1]!.ToString(), CultureInfo.InvariantCulture))).ToList() ?? new();
                    return (bids, asks);
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Bitstamp GetOrderBookAsync failed for {symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new(), new());
            }
        }

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        // ── ITradingProvider ─────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsTradeConfigured) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                var response = await PostAuthenticatedAsync("/api/v2/balance/", new Dictionary<string, string>());
                var json     = JObject.Parse(response);
                var result   = new List<Balance>();
                var currencies = json.Properties().Where(p => p.Name.EndsWith("_available")).Select(p => p.Name.Replace("_available", "")).ToList();

                foreach (var cur in currencies)
                {
                    double avail = json[$"{cur}_available"]?.Value<double>() ?? 0;
                    double res   = json[$"{cur}_reserved"]?.Value<double>() ?? 0;
                    if (avail > 0 || res > 0) result.Add(new Balance(cur.ToUpper(), avail, res));
                }
                return result;
            });
        }

        public Task<List<Position>> GetPositionsAsync() => Task.FromResult(new List<Position>());

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsTradeConfigured) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                string endpoint = !string.IsNullOrEmpty(symbol) ? $"/api/v2/open_orders/{symbol.Replace("/", "").ToLower()}/" : "/api/v2/open_orders/all/";
                var response = await PostAuthenticatedAsync(endpoint, new Dictionary<string, string>());
                var arr = JArray.Parse(response);
                return arr.Select(o => new OpenOrder(
                    o["id"]?.ToString() ?? "",
                    o["currency_pair"]?.ToString() ?? symbol ?? "",
                    o["type"]?.ToString() == "0" ? OrderSide.Buy : OrderSide.Sell,
                    OrderType.Limit,
                    o["amount"]?.Value<double>() ?? 0,
                    o["price"]?.Value<double>() ?? 0,
                    "Open"
                )).ToList();
            });
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsTradeConfigured) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var pair   = signal.Symbol.Replace("/", "").ToLower();
                    bool isMkt = signal.Type == OrderType.Market;
                    string endpoint = signal.Side == OrderSide.Buy
                        ? $"/api/v2/buy/{(isMkt ? "market/" : "")}{pair}/"
                        : $"/api/v2/sell/{(isMkt ? "market/" : "")}{pair}/";

                    // FOUND 2026-08-21 by the fixed-precision price sweep. These were "F2",
                    // which is the speech bug with a far worse victim: this string is the ORDER,
                    // not a description of one. A limit on a sub-dollar pair — and Bitstamp
                    // lists plenty — was submitted rounded to the nearest cent, so 0.0363
                    // became "0.04" (7 percent away from the level the user chose) and anything
                    // under half a cent became "0.00". Neither format carried the culture
                    // either, so a comma-decimal machine posted "0,04" to the exchange.
                    //
                    // Every other provider in this repo already does exactly this: full
                    // precision, invariant culture. Bitstamp was the one that did not.
                    var postData = new Dictionary<string, string>
                        { ["amount"] = signal.Quantity.ToString(CultureInfo.InvariantCulture) };
                    if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                        postData["price"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture);

                    // Bitstamp supports limit_price for instant orders (acts as price ceiling/floor)
                    if (isMkt && signal.Price.HasValue)
                        postData["limit_price"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture);

                    var response = await PostAuthenticatedAsync(endpoint, postData);
                    var json     = JObject.Parse(response);
                    if (json["status"]?.ToString() == "error") return $"ORDER_FAILED:{json["reason"]}";
                    return json["id"]?.ToString() ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Bitstamp order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsTradeConfigured) return false;
            try
            {
                var response = await _rateLimiter.ExecuteAsync(async () =>
                    await PostAuthenticatedAsync("/api/v2/cancel_order/", new Dictionary<string, string> { ["id"] = orderId }));
                var json = JObject.Parse(response);
                return json["status"]?.ToString() != "error";
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Bitstamp CancelOrderAsync failed for {orderId} ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        private async Task<string> PostAuthenticatedAsync(string endpoint, Dictionary<string, string> parameters)
        {
            var (apiKey, apiSecret, customerId) = await CheckoutBitstampCredentialsAsync().ConfigureAwait(false);

            string nonce   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            string message = nonce + customerId + apiKey;
            byte[] secretBytes = Encoding.UTF8.GetBytes(apiSecret);
            using var hmac = new System.Security.Cryptography.HMACSHA256(secretBytes);
            byte[] hash     = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            string signature = BitConverter.ToString(hash).Replace("-", "").ToUpper();
            Array.Clear(secretBytes, 0, secretBytes.Length);

            var postParams = new Dictionary<string, string>(parameters) { ["key"] = apiKey, ["signature"] = signature, ["nonce"] = nonce };
            var content  = new FormUrlEncodedContent(postParams);
            var response = await _httpClient.PostAsync($"https://www.bitstamp.net{endpoint}", content);
            return await response.Content.ReadAsStringAsync();
        }

        // Sign-time credential checkout (phase 4 Track B). Prefers the
        // PluginHostServices.ApiKeys bridge; falls back to Configure-populated
        // fields for unit tests / CLI runs. The Bitstamp customer-id lives in
        // the ApiKeyCheckoutResult.Passphrase slot when provided by the host.
        private async Task<(string Key, string Secret, string CustomerId)> CheckoutBitstampCredentialsAsync()
        {
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("Bitstamp").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("Bitstamp: no active API key configured.");
                var cust = !string.IsNullOrEmpty(checkout.Passphrase) ? checkout.Passphrase : (_customerId ?? "");
                return (checkout.Key, checkout.Secret, cust);
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                throw new InvalidOperationException("Bitstamp: no API credentials configured.");
            return (_apiKey!, _apiSecret!, _customerId ?? "");
        }

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
