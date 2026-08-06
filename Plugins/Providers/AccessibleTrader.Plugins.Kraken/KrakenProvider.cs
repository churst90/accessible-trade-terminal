using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
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

namespace AccessibleTrader.Plugins.Kraken
{
    public class KrakenProvider : BaseMarketDataProvider, ITradingProvider, IOrderBookProvider, IWalletProvider, IWithdrawalProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private string? _apiSecret;

        private const string RestUrl  = "https://api.kraken.com";
        private const string WsUrl    = "wss://ws.kraken.com/v2";
        private const string WsAuthUrl = "wss://ws-auth.kraken.com/v2";

        // Rate limiter: Kraken allows ~15 calls/second for public, ~20/min for private
        private readonly RateLimiter _publicRateLimiter  = new(15, TimeSpan.FromSeconds(1));
        private readonly RateLimiter _privateRateLimiter = new(20, TimeSpan.FromMinutes(1));

        // Kraken requires strictly-increasing nonces per API key. A millisecond wall
        // clock collides under bursty order flow (two calls in the same ms are rejected);
        // seed from wall-clock so the sequence survives restarts, then increment atomically.
        private long _nonceCounter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // WebSocket connections
        private ReconnectingWebSocket? _publicWs;
        private ReconnectingWebSocket? _authWs;
        private string? _wsToken;

        private string? _currentSymbol;
        private string? _currentTimeframe;

        // Streams
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();
        private string? _orderBookSymbol;

        public override string Name => "Kraken";
        public override string Description => "Kraken Exchange — Crypto Spot & Margin Trading";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => true;
        // The v2 ohlc channel re-sends the current candle with CUMULATIVE
        // volume-so-far; consolidation must diff, not accumulate.
        public override AccessibleTrader.Sdk.Plugins.LiveTickStyle LiveTickStyle => AccessibleTrader.Sdk.Plugins.LiveTickStyle.CumulativeBars;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 720;
        /// <summary>
        /// Kraken's leverage is SPOT MARGIN — the venue lends the asset or the quote
        /// against the same order book — which is a different product from futures
        /// and is why <c>MarginTrading</c> is set and <c>FuturesTrading</c> is not.
        /// (Kraken Futures is a separate API this plugin does not talk to.)
        /// </summary>
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.Leverage | ProviderCapabilities.Brackets |
            ProviderCapabilities.MarginTrading | ProviderCapabilities.DepositAddresses |
            ProviderCapabilities.Withdrawals;

        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 5.0;


        // Kraken's close[] slot fits ONE protective order — see AddOrder below.
        public bool SupportsSimultaneousStopAndTarget => false;

        public bool IsConnected => !string.IsNullOrEmpty(_apiKey);

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour, StandardTimeframes.FourHours,
            StandardTimeframes.OneDay, StandardTimeframes.OneWeek
        };

        public KrakenProvider()
        {
            // Phase 4 Track B2 — HttpClient via the host factory so outbound
            // requests are pinned to api.kraken.com and capped at 32 MB.
            // User-Agent is set by CreateHttpClient. WS endpoints
            // (ws.kraken.com / ws-auth.kraken.com) use ReconnectingWebSocket,
            // not this HttpClient, so they're not in the allow-list.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId:   "Kraken",
                allowedHosts: new[] { "api.kraken.com" });
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
            if (config.TryGetValue("ApiKey",    out var k)) _apiKey    = k;
            if (config.TryGetValue("ApiSecret", out var s)) _apiSecret = s;
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) return (true, "No API key provided (public data only)");
            try
            {
                var result = await PostPrivateAsync("/0/private/Balance", new Dictionary<string, string>());
                var json = JObject.Parse(result);
                var errors = json["error"] as JArray;
                if (errors != null && errors.Count > 0)
                    return (false, $"Key validation failed: {string.Join(", ", errors)}");
                return (true, "API key validated successfully");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        // ── Connection & Subscription ───────────────────────────────────────

        public override async Task EnsureConnectedAsync()
        {
            if (_publicWs?.IsConnected == true) return;

            _publicWs = new ReconnectingWebSocket(WsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    // Re-subscribe if we had active subscriptions
                    if (!string.IsNullOrEmpty(_currentSymbol) && !string.IsNullOrEmpty(_currentTimeframe))
                    {
                        int interval = MapWsInterval(_currentTimeframe);
                        var sub = new JObject
                        {
                            ["method"] = "subscribe",
                            ["params"] = new JObject
                            {
                                ["channel"] = "ohlc",
                                ["symbol"] = new JArray { _currentSymbol },
                                ["interval"] = interval
                            }
                        };
                        await ws.SendAsync(sub.ToString());
                    }
                    if (!string.IsNullOrEmpty(_orderBookSymbol))
                    {
                        var bookSub = new JObject
                        {
                            ["method"] = "subscribe",
                            ["params"] = new JObject
                            {
                                ["channel"] = "book",
                                ["symbol"] = new JArray { _orderBookSymbol },
                                ["depth"] = 25
                            }
                        };
                        await ws.SendAsync(bookSub.ToString());
                    }
                })
                .OnMessage(HandlePublicMessage)
                .OnError(err => _errorStream.OnNext($"Kraken WS: {err}"))
                .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

            await _publicWs.ConnectAsync();
            _connectionStateStream.OnNext(ConnectionState.Connected);

            // Start authenticated WS if keys present
            if (!string.IsNullOrEmpty(_apiKey))
                await StartAuthWebSocketAsync();
        }

        private async Task StartAuthWebSocketAsync()
        {
            try
            {
                // Get WebSocket auth token
                var tokenResult = await PostPrivateAsync("/0/private/GetWebSocketsToken", new Dictionary<string, string>());
                var tokenJson = JObject.Parse(tokenResult);
                _wsToken = tokenJson["result"]?["token"]?.ToString();
                if (string.IsNullOrEmpty(_wsToken)) return;

                _authWs = new ReconnectingWebSocket(WsAuthUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                    .OnConnected(async ws =>
                    {
                        // Re-acquire token on reconnect
                        var tr = await PostPrivateAsync("/0/private/GetWebSocketsToken", new Dictionary<string, string>());
                        var tj = JObject.Parse(tr);
                        _wsToken = tj["result"]?["token"]?.ToString();
                        if (string.IsNullOrEmpty(_wsToken)) return;

                        // Subscribe to own orders
                        var sub = new JObject
                        {
                            ["method"] = "subscribe",
                            ["params"] = new JObject
                            {
                                ["channel"] = "executions",
                                ["token"] = _wsToken,
                                ["snap_orders"] = true
                            }
                        };
                        await ws.SendAsync(sub.ToString());
                    })
                    .OnMessage(HandleAuthMessage)
                    .OnError(err => _errorStream.OnNext($"Kraken auth WS: {err}"));

                await _authWs.ConnectAsync();
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Kraken auth WS unavailable ({ex.GetType().Name}): order execution updates won't be delivered.");
            }
        }

        private void HandlePublicMessage(string msg)
        {
            try
            {
                var json = JObject.Parse(msg);
                var channel = json["channel"]?.ToString();

                if (channel == "ohlc")
                {
                    var data = json["data"] as JArray;
                    if (data == null) return;
                    foreach (var item in data)
                    {
                        if (TryParseOhlcItem(item, out var bar))
                            _liveStream.OnNext(bar);
                    }
                }
                else if (channel == "book")
                {
                    var data = json["data"] as JArray;
                    if (data == null) return;
                    foreach (var snap in data)
                    {
                        var symbol = snap["symbol"]?.ToString() ?? _orderBookSymbol ?? "";
                        var bids = (snap["bids"] as JArray)?.Select(b => new OrderBookEntry(
                            b["price"]?.Value<double>() ?? 0,
                            b["qty"]?.Value<double>() ?? 0)).ToList() ?? new();
                        var asks = (snap["asks"] as JArray)?.Select(a => new OrderBookEntry(
                            a["price"]?.Value<double>() ?? 0,
                            a["qty"]?.Value<double>() ?? 0)).ToList() ?? new();
                        if (bids.Any() || asks.Any())
                            _orderBookSubject.OnNext(new OrderBookUpdate(symbol, bids, asks, 0, DateTime.UtcNow));
                    }
                }
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Kraken message parse failed: {ex.GetType().Name}");
            }
        }

        private void HandleAuthMessage(string msg)
        {
            try
            {
                var json = JObject.Parse(msg);
                var channel = json["channel"]?.ToString();

                if (channel == "executions")
                {
                    var data = json["data"] as JArray;
                    if (data == null) return;
                    foreach (var exec in data)
                    {
                        var orderId = exec["order_id"]?.ToString() ?? "";
                        var symbol  = exec["symbol"]?.ToString() ?? "";
                        var sideStr = exec["side"]?.ToString() ?? "";
                        var statusStr = exec["order_status"]?.ToString() ?? "";

                        var status = statusStr switch
                        {
                            "filled"           => OrderStatus.Filled,
                            "partially_filled" => OrderStatus.PartialFill,
                            "canceled"         => OrderStatus.Cancelled,
                            "expired"          => OrderStatus.Cancelled,
                            _                  => OrderStatus.Triggered
                        };

                        _orderUpdateSubject.OnNext(new OrderUpdate(
                            orderId, symbol,
                            sideStr == "buy" ? OrderSide.Buy : OrderSide.Sell,
                            exec["cum_qty"]?.Value<double>() ?? 0,
                            exec["avg_price"]?.Value<double>() ?? 0,
                            exec["leaves_qty"]?.Value<double>() ?? 0,
                            status, false, false, DateTime.UtcNow));
                    }
                }
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Kraken message parse failed: {ex.GetType().Name}");
            }
        }

        /// <summary>Parses one v2 ohlc channel candle. The channel re-sends the
        /// current candle with CUMULATIVE volume-so-far — see LiveTickStyle.
        /// Internal for direct testing.</summary>
        internal static bool TryParseOhlcItem(JToken item, out Ohlcv bar)
        {
            bar = default;
            double open  = item["open"]?.Value<double>() ?? 0;
            double high  = item["high"]?.Value<double>() ?? 0;
            double low   = item["low"]?.Value<double>() ?? 0;
            double close = item["close"]?.Value<double>() ?? 0;
            double vol   = item["volume"]?.Value<double>() ?? 0;
            if (open == 0 && high == 0 && low == 0 && close == 0) return false;

            // AdjustToUniversal, NOT RoundtripKind + ToUniversalTime: the old
            // combination round-tripped through the machine's LOCAL zone, so on
            // any non-UTC box every live Kraken bar landed hours in the future —
            // breaking period bucketing and buffer merges. (Found by the parse
            // test the 2026-07-22 multi-live enrollment added.)
            var ts = item["timestamp"]?.ToString();
            DateTime date = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(ts) && DateTime.TryParse(ts, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                date = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

            bar = new Ohlcv(date, open, high, low, close, vol);
            return true;
        }

        // ── Keyed-feed subscriptions (multiple concurrent live streams) ───────

        public override bool SupportsMultipleLiveSubscriptions => true;

        /// <summary>
        /// One dedicated public v2 websocket per subscription with its own ohlc
        /// channel — independent of the provider's main socket and its book/
        /// trading channels. ReconnectingWebSocket owns reconnection and
        /// re-subscribes via OnConnected; disposing the returned socket
        /// unsubscribes. Every ohlc message on this socket belongs to this one
        /// subscription, so no symbol demux is needed.
        /// </summary>
        public override async Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
        {
            var pair = FormatPair(symbol);
            int interval = MapWsInterval(timeframe);
            var consolidator = new BarBucketConsolidator(timeframe, LiveTickStyle.CumulativeBars);

            string subscribeMessage = new JObject
            {
                ["method"] = "subscribe",
                ["params"] = new JObject
                {
                    ["channel"] = "ohlc",
                    ["symbol"] = new JArray { pair },
                    ["interval"] = interval,
                },
            }.ToString(Newtonsoft.Json.Formatting.None);

            var ws = new ReconnectingWebSocket(WsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnError(err => _errorStream.OnNext($"Kraken keyed stream ({pair}): {err}"));
            ws.OnConnected(async w => await w.SendAsync(subscribeMessage))
              .OnMessage(msg =>
                {
                    try
                    {
                        var json = JObject.Parse(msg);
                        if (json["channel"]?.ToString() != "ohlc") return;
                        if (json["data"] is not JArray data) return;
                        foreach (var item in data)
                        {
                            if (!TryParseOhlcItem(item, out var raw)) continue;
                            var bar = consolidator.Apply(raw);
                            if (bar.HasValue) onBar(bar.Value);
                        }
                    }
                    catch { /* malformed frame */ }
                });

            await ws.ConnectAsync();
            return ws; // ReconnectingWebSocket is IAsyncDisposable — disposal closes the socket
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();

            var pair = FormatPair(symbol);
            if (_currentSymbol == pair && _currentTimeframe == timeframe) return;

            // Unsubscribe previous
            if (!string.IsNullOrEmpty(_currentSymbol) && _publicWs != null)
            {
                var unsub = new JObject
                {
                    ["method"] = "unsubscribe",
                    ["params"] = new JObject
                    {
                        ["channel"] = "ohlc",
                        ["symbol"] = new JArray { _currentSymbol }
                    }
                };
                await _publicWs.SendAsync(unsub.ToString());
            }

            _currentSymbol = pair;
            _currentTimeframe = timeframe;

            if (_publicWs != null)
            {
                int interval = MapWsInterval(timeframe);
                var sub = new JObject
                {
                    ["method"] = "subscribe",
                    ["params"] = new JObject
                    {
                        ["channel"] = "ohlc",
                        ["symbol"] = new JArray { pair },
                        ["interval"] = interval
                    }
                };
                await _publicWs.SendAsync(sub.ToString());
            }
        }

        public override async Task DisconnectAsync()
        {
            if (_publicWs != null) { await _publicWs.DisconnectAsync(); _publicWs.Dispose(); _publicWs = null; }
            if (_authWs != null)   { await _authWs.DisconnectAsync();   _authWs.Dispose();   _authWs = null; }
            _currentSymbol = null;
            _currentTimeframe = null;
            _orderBookSymbol = null;

            // Drop references to HMAC signing material + the short-lived WS
            // token so a crash dump after disconnect doesn't leak them.
            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null,
                () => _wsToken = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        // ── Data Discovery & Fetching ───────────────────────────────────────

        public override async Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            new List<string> { "Spot" };

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            try
            {
                return await _publicRateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{RestUrl}/0/public/AssetPairs");
                    var json = JObject.Parse(response);
                    var result = json["result"] as JObject;
                    if (result == null) return new List<string>();

                    return result.Properties()
                        .Select(p => p.Value["wsname"]?.ToString() ?? p.Name)
                        .Where(s => !string.IsNullOrEmpty(s) && !s.EndsWith(".d"))
                        .OrderBy(s => s)
                        .ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Kraken GetAvailableSymbolsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "30m", "1h", "4h", "1d", "1w" });

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var pair = FormatRestPair(request.Symbol);
            int interval = MapRestInterval(request.Timeframe);

            string url = $"{RestUrl}/0/public/OHLC?pair={pair}&interval={interval}";
            if (request.Since.HasValue)
                url += $"&since={request.Since.Value / 1000}";

            try
            {
                return await _publicRateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);
                    var result = json["result"] as JObject;
                    if (result == null)
                    {
                        var err = (json["error"] as JArray)?.FirstOrDefault()?.ToString();
                        if (!string.IsNullOrEmpty(err))
                            _errorStream.OnNext($"Kraken data error for {request.Symbol}: {err}");
                        return (new List<Ohlcv>(), new List<(long, double)>());
                    }

                    // The result contains the pair data under a key that varies (e.g., "XXBTZUSD")
                    // plus a "last" key. Find the data array.
                    JArray? ohlcArray = null;
                    foreach (var prop in result.Properties())
                    {
                        if (prop.Name == "last") continue;
                        ohlcArray = prop.Value as JArray;
                        if (ohlcArray != null) break;
                    }
                    if (ohlcArray == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = ohlcArray.Select(item =>
                    {
                        var arr = item as JArray;
                        if (arr == null || arr.Count < 7) return default(Ohlcv?);
                        long ts = arr[0]?.Value<long>() ?? 0;
                        return new Ohlcv(
                            DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime,
                            double.Parse(arr[1]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(arr[2]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(arr[3]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(arr[4]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(arr[6]?.ToString() ?? "0", CultureInfo.InvariantCulture));
                    })
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .OrderBy(x => x.Date)
                    .ToList();

                    int limit = Math.Min(request.Limit, ohlcvList.Count);
                    ohlcvList = ohlcvList.TakeLast(limit).ToList();

                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Kraken FetchOhlcvAsync failed for {request.Symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            var pair = FormatRestPair(symbol);
            try
            {
                return await _publicRateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync($"{RestUrl}/0/public/Depth?pair={pair}&count={limit}");
                    var json = JObject.Parse(response);
                    var result = json["result"] as JObject;
                    if (result == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                    JObject? bookData = null;
                    foreach (var prop in result.Properties())
                    {
                        bookData = prop.Value as JObject;
                        if (bookData != null) break;
                    }
                    if (bookData == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                    var bids = (bookData["bids"] as JArray)?.Take(limit).Select(b =>
                    {
                        var arr = b as JArray;
                        return new OrderBookEntry(
                            double.Parse(arr?[0]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(arr?[1]?.ToString() ?? "0", CultureInfo.InvariantCulture));
                    }).ToList() ?? new();

                    var asks = (bookData["asks"] as JArray)?.Take(limit).Select(a =>
                    {
                        var arr = a as JArray;
                        return new OrderBookEntry(
                            double.Parse(arr?[0]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(arr?[1]?.ToString() ?? "0", CultureInfo.InvariantCulture));
                    }).ToList() ?? new();

                    return (bids, asks);
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Kraken GetOrderBookAsync failed for {symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        // ── IOrderBookProvider ──────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol)
        {
            var pair = FormatPair(symbol);
            if (_orderBookSymbol == pair) return _orderBookSubject.AsObservable();

            _orderBookSymbol = pair;

            _ = Task.Run(async () =>
            {
                await EnsureConnectedAsync();
                if (_publicWs != null)
                {
                    var sub = new JObject
                    {
                        ["method"] = "subscribe",
                        ["params"] = new JObject
                        {
                            ["channel"] = "book",
                            ["symbol"] = new JArray { pair },
                            ["depth"] = 25
                        }
                    };
                    await _publicWs.SendAsync(sub.ToString());
                }
            });

            return _orderBookSubject.AsObservable();
        }

        // ── ITradingProvider ────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _privateRateLimiter.ExecuteAsync(async () =>
                {
                    var result = await PostPrivateAsync("/0/private/Balance", new Dictionary<string, string>());
                    var json = JObject.Parse(result);
                    var balances = json["result"] as JObject;
                    if (balances == null) return new List<Balance>();

                    return balances.Properties()
                        .Select(p =>
                        {
                            double.TryParse(p.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double val);
                            return new Balance(p.Name, val, 0);
                        })
                        .Where(b => b.Free > 0)
                        .ToList();
                });
            }
            catch { return new(); }
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _privateRateLimiter.ExecuteAsync(async () =>
                {
                    var result = await PostPrivateAsync("/0/private/OpenPositions", new Dictionary<string, string>());
                    var json = JObject.Parse(result);
                    var positions = json["result"] as JObject;
                    if (positions == null) return new List<Position>();

                    return positions.Properties().Select(p =>
                    {
                        var pos = p.Value as JObject;
                        return new Position(
                            pos?["pair"]?.ToString() ?? p.Name,
                            Math.Abs(pos?["vol"]?.Value<double>() ?? 0),
                            pos?["cost"]?.Value<double>() ?? 0,
                            pos?["value"]?.Value<double>() ?? 0,
                            pos?["net"]?.Value<double>() ?? 0,
                            pos?["margin"]?.Value<double>() > 0 ? (pos?["cost"]?.Value<double>() ?? 0) / (pos?["margin"]?.Value<double>() ?? 1) : 1.0);
                    }).ToList();
                });
            }
            catch { return new(); }
        }

        /// <summary>Account fill history via TradesHistory (dashboard History tab
        /// parity — this returned the interface default empty list until 2026-07-22).</summary>
        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            if (!IsConnected) return new();
            try
            {
                // Signed history call — must share the private rate limiter with
                // Balances/Positions/OpenOrders or a History-tab refresh can burst
                // past Kraken's private nonce/rate window and get rejected.
                return await _privateRateLimiter.ExecuteAsync(async () =>
                {
                    var result = await PostPrivateAsync("/0/private/TradesHistory", new Dictionary<string, string>());
                    var json = JObject.Parse(result);
                    var trades = json["result"]?["trades"] as JObject;
                    if (trades == null) return new List<TradeFill>();

                    string? want = symbol == null ? null : CleanSymbol(symbol);
                    var fills = new List<TradeFill>();
                    foreach (var kv in trades.Properties())
                    {
                        var t = kv.Value;
                        string pair = t["pair"]?.ToString() ?? "";
                        if (want != null && !pair.Replace("/", "").ToUpperInvariant().Contains(want)) continue;
                        fills.Add(new TradeFill(
                            kv.Name, pair,
                            (t["type"]?.ToString() ?? "buy") == "sell" ? OrderSide.Sell : OrderSide.Buy,
                            t["vol"]?.Value<double>() ?? 0,
                            t["price"]?.Value<double>() ?? 0,
                            DateTimeOffset.FromUnixTimeSeconds((long)(t["time"]?.Value<double>() ?? 0)).UtcDateTime,
                            t["fee"]?.Value<double>() ?? 0,
                            t["ordertxid"]?.ToString()));
                    }
                    return fills.OrderByDescending(f => f.FilledAt).Take(limit).ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Kraken GetFillsAsync failed ({ex.GetType().Name})");
                return new();
            }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            try
            {
                return await _privateRateLimiter.ExecuteAsync(async () =>
                {
                    var result = await PostPrivateAsync("/0/private/OpenOrders", new Dictionary<string, string>());
                    var json = JObject.Parse(result);
                    var open = json["result"]?["open"] as JObject;
                    if (open == null) return new List<OpenOrder>();

                    return open.Properties().Select(p =>
                    {
                        var o = p.Value?["descr"];
                        return new OpenOrder(
                            p.Name,
                            o?["pair"]?.ToString() ?? "",
                            o?["type"]?.ToString() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                            MapKrakenOrderType(o?["ordertype"]?.ToString() ?? "market"),
                            p.Value?["vol"]?.Value<double>() ?? 0,
                            double.TryParse(o?["price"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double px) ? px : 0,
                            p.Value?["status"]?.ToString() ?? "open");
                    })
                    .Where(o => symbol == null || o.Symbol.Contains(symbol.Replace("/", ""), StringComparison.OrdinalIgnoreCase))
                    .ToList();
                });
            }
            catch { return new(); }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _privateRateLimiter.ExecuteAsync(async () =>
                {
                    var pair = FormatRestPair(signal.Symbol);
                    var postData = new Dictionary<string, string>
                    {
                        ["pair"]      = pair,
                        ["type"]      = signal.Side == OrderSide.Buy ? "buy" : "sell",
                        ["volume"]    = signal.Quantity.ToString("F8", CultureInfo.InvariantCulture),
                        ["ordertype"] = MapToKrakenOrderType(signal.Type)
                    };

                    if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                        postData["price"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture);

                    if (signal.Type == OrderType.StopMarket && signal.StopLoss.HasValue)
                        postData["price"] = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture);

                    if (signal.Type == OrderType.StopLimit && signal.StopLoss.HasValue && signal.Price.HasValue)
                    {
                        postData["price"]  = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture);
                        postData["price2"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    if (signal.Type == OrderType.TakeProfitMarket && signal.TakeProfit.HasValue)
                        postData["price"] = signal.TakeProfit.Value.ToString(CultureInfo.InvariantCulture);

                    if (signal.Type == OrderType.TakeProfitLimit && signal.TakeProfit.HasValue && signal.Price.HasValue)
                    {
                        postData["price"]  = signal.TakeProfit.Value.ToString(CultureInfo.InvariantCulture);
                        postData["price2"] = signal.Price.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    if (signal.Leverage.HasValue && signal.Leverage.Value > 1)
                        postData["leverage"] = ((int)Math.Clamp(signal.Leverage.Value, 2, MaxLeverage)).ToString();

                    // Close-on-trigger order for SL/TP. Kraken's close[] slot holds
                    // exactly ONE order — when both are requested the STOP wins
                    // (safety over profit) and SupportsSimultaneousStopAndTarget=false
                    // lets the order service tell the user the TP was not attached.
                    if (signal.StopLoss.HasValue && signal.Type == OrderType.Market)
                    {
                        postData["close[ordertype]"] = "stop-loss";
                        postData["close[price]"] = signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture);
                    }
                    else if (signal.TakeProfit.HasValue && signal.Type == OrderType.Market)
                    {
                        postData["close[ordertype]"] = "take-profit";
                        postData["close[price]"] = signal.TakeProfit.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    if (!string.IsNullOrEmpty(signal.ClientOid))
                        postData["cl_ord_id"] = signal.ClientOid;

                    var result = await PostPrivateAsync("/0/private/AddOrder", postData);
                    var json = JObject.Parse(result);
                    var errors = json["error"] as JArray;
                    if (errors != null && errors.Count > 0)
                        return $"ORDER_FAILED:{string.Join(", ", errors)}";

                    var txids = json["result"]?["txid"] as JArray;
                    return txids?.FirstOrDefault()?.ToString() ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Kraken order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                var result = await _privateRateLimiter.ExecuteAsync(async () =>
                    await PostPrivateAsync("/0/private/CancelOrder", new Dictionary<string, string> { ["txid"] = orderId }));
                var json = JObject.Parse(result);
                var errors = json["error"] as JArray;
                return errors == null || errors.Count == 0;
            }
            catch { return false; }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) =>
            Task.FromResult(Math.Clamp(leverage, 1, MaxLeverage));

        // ── IWalletProvider ─────────────────────────────────────────────────
        //
        // Kraken calls a chain a "method": DepositMethods returns entries such as
        // "Bitcoin", "Ethereum (ERC20)" and "Tether USD (TRC20)", and that same
        // string is what DepositAddresses wants back. So Kraken's method IS our
        // network, and no translation table is needed — which is fortunate, because
        // a hand-maintained one would be a list of ways to send funds to the wrong
        // chain.
        //
        // Deliberately does NOT swallow failures into empty lists, unlike the older
        // read paths in this file: WalletService needs to tell "cannot deposit this
        // asset" from "your key lacks the scope" from "the call failed", and it can
        // only do that if the exception reaches it.

        public async Task<IReadOnlyList<string>> GetDepositNetworksAsync(
            string asset, CancellationToken ct = default)
        {
            if (!IsConnected) throw new InvalidOperationException("Kraken: not connected.");

            return await _privateRateLimiter.ExecuteAsync(async () =>
            {
                var result = await PostPrivateAsync("/0/private/DepositMethods",
                    new Dictionary<string, string> { ["asset"] = NormaliseAsset(asset) });

                ThrowOnKrakenError(result, $"deposit methods for {asset}");

                var methods = JObject.Parse(result)["result"] as JArray;
                if (methods == null) return (IReadOnlyList<string>)Array.Empty<string>();

                return methods
                    .Select(m => m?["method"]?.ToString())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            });
        }

        public async Task<DepositAddress> GetDepositAddressAsync(
            string asset, string network, CancellationToken ct = default)
        {
            if (!IsConnected) throw new InvalidOperationException("Kraken: not connected.");

            return await _privateRateLimiter.ExecuteAsync(async () =>
            {
                // `new=false`: ask for an EXISTING address rather than minting one on
                // every open. Kraken rotates addresses and keeps old ones fundable,
                // so generating per view would litter the account and make the "is
                // this the address I wrote down?" check fail for no reason.
                var result = await PostPrivateAsync("/0/private/DepositAddresses",
                    new Dictionary<string, string>
                    {
                        ["asset"]  = NormaliseAsset(asset),
                        ["method"] = network,
                    });

                ThrowOnKrakenError(result, $"deposit address for {asset} on {network}");

                var addresses = JObject.Parse(result)["result"] as JArray;
                var first = addresses?.FirstOrDefault() as JObject
                    ?? throw new InvalidOperationException(
                        $"Kraken returned no {asset} address for {network}. If this asset is new to "
                      + "your account, generate the first address on kraken.com.");

                string? addr = first["address"]?.ToString();
                if (string.IsNullOrWhiteSpace(addr))
                    throw new InvalidOperationException("Kraken returned an address entry with no address in it.");

                // Kraken names the destination tag differently per asset, and does not
                // always send one. Read whichever key is present rather than assuming;
                // a memo silently dropped loses the deposit as completely as a wrong
                // address does.
                string? memo = first["tag"]?.ToString()
                            ?? first["memo"]?.ToString()
                            ?? first["destination_tag"]?.ToString();
                string? memoLabel = first["tag"] != null ? "destination tag"
                                  : first["memo"] != null ? "memo"
                                  : first["destination_tag"] != null ? "destination tag" : null;

                return new DepositAddress(
                    Asset:     asset.ToUpperInvariant(),
                    Network:   network,
                    Address:   addr!,
                    Memo:      string.IsNullOrWhiteSpace(memo) ? null : memo,
                    MemoLabel: memoLabel);
            });
        }

        public async Task<IReadOnlyList<DepositRecord>> GetDepositsAsync(
            string? asset = null, int limit = 50, CancellationToken ct = default)
        {
            if (!IsConnected) throw new InvalidOperationException("Kraken: not connected.");

            return await _privateRateLimiter.ExecuteAsync(async () =>
            {
                var args = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(asset)) args["asset"] = NormaliseAsset(asset!);

                var result = await PostPrivateAsync("/0/private/DepositStatus", args);
                ThrowOnKrakenError(result, "deposit history");

                var rows = JObject.Parse(result)["result"] as JArray;
                if (rows == null) return (IReadOnlyList<DepositRecord>)Array.Empty<DepositRecord>();

                return rows.OfType<JObject>().Take(limit).Select(r =>
                {
                    double.TryParse(r["amount"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double amt);
                    double.TryParse(r["time"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double unix);
                    return new DepositRecord(
                        Asset:     r["asset"]?.ToString() ?? asset?.ToUpperInvariant() ?? "",
                        Network:   r["method"]?.ToString() ?? "",
                        Amount:    amt,
                        Status:    r["status"]?.ToString() ?? "unknown",
                        SeenAtUtc: DateTimeOffset.FromUnixTimeSeconds((long)unix).UtcDateTime,
                        TxId:      r["txid"]?.ToString());
                }).ToList();
            });
        }

        // ── IWithdrawalProvider ─────────────────────────────────────────────
        //
        // Kraken's Withdraw endpoint takes a `key` — the NAME of a withdrawal
        // address the user saved on kraken.com — and not an address. That is the
        // property the whole withdrawal design rests on: this terminal cannot
        // invent a destination, so even fully compromised it could only send funds
        // somewhere the user had personally whitelisted behind Kraken's own 2FA.
        //
        // These are reached only with a credential from a withdrawal-enabled key
        // profile; the ordinary trading key cannot get here.

        public async Task<IReadOnlyList<WithdrawalDestination>> GetWithdrawalDestinationsAsync(
            string asset, CancellationToken ct = default)
        {
            if (!IsConnected) throw new InvalidOperationException("Kraken: not connected.");

            return await _privateRateLimiter.ExecuteAsync(async () =>
            {
                var result = await PostPrivateAsync("/0/private/WithdrawAddresses",
                    new Dictionary<string, string> { ["asset"] = NormaliseAsset(asset) });
                ThrowOnKrakenError(result, $"withdrawal addresses for {asset}");

                var rows = JObject.Parse(result)["result"] as JArray;
                if (rows == null) return (IReadOnlyList<WithdrawalDestination>)Array.Empty<WithdrawalDestination>();

                return rows.OfType<JObject>().Select(r => new WithdrawalDestination(
                    Key:      r["key"]?.ToString() ?? "",
                    Asset:    asset.ToUpperInvariant(),
                    Address:  r["address"]?.ToString(),
                    Network:  r["method"]?.ToString(),
                    // Kraken holds new addresses for a period before they can be
                    // used. Sending to an unverified one fails at the venue, so it
                    // is surfaced rather than discovered at the confirmation step.
                    Verified: r["verified"]?.Value<bool>() ?? true))
                    .Where(d => !string.IsNullOrEmpty(d.Key))
                    .ToList();
            });
        }

        public async Task<WithdrawalQuote> GetWithdrawalQuoteAsync(
            string asset, string destinationKey, double amount, CancellationToken ct = default)
        {
            if (!IsConnected) throw new InvalidOperationException("Kraken: not connected.");

            return await _privateRateLimiter.ExecuteAsync(async () =>
            {
                var result = await PostPrivateAsync("/0/private/WithdrawInfo", new Dictionary<string, string>
                {
                    ["asset"]  = NormaliseAsset(asset),
                    ["key"]    = destinationKey,
                    ["amount"] = amount.ToString("0.########", CultureInfo.InvariantCulture),
                });
                ThrowOnKrakenError(result, $"withdrawal fees for {asset}");

                var r = JObject.Parse(result)["result"] as JObject
                    ?? throw new InvalidOperationException("Kraken returned no withdrawal quote.");

                double fee = Num(r["fee"]);
                // Kraken's `amount` here is what ARRIVES, and `limit` is the most
                // that may still be withdrawn. Reading `amount` as the amount SENT
                // would understate the cost by the fee, on the number the user is
                // deciding about.
                double net = Num(r["amount"]);
                return new WithdrawalQuote(
                    asset.ToUpperInvariant(), amount, fee,
                    net > 0 ? net : amount - fee,
                    MinimumAmount: null,
                    RemainingDailyLimit: r["limit"] != null ? Num(r["limit"]) : null);
            });
        }

        public async Task<WithdrawalResult> WithdrawAsync(
            string asset, string destinationKey, double amount, CancellationToken ct = default)
        {
            if (!IsConnected) throw new InvalidOperationException("Kraken: not connected.");

            return await _privateRateLimiter.ExecuteAsync(async () =>
            {
                var result = await PostPrivateAsync("/0/private/Withdraw", new Dictionary<string, string>
                {
                    ["asset"]  = NormaliseAsset(asset),
                    ["key"]    = destinationKey,
                    ["amount"] = amount.ToString("0.########", CultureInfo.InvariantCulture),
                });
                ThrowOnKrakenError(result, "the withdrawal");

                string? refId = JObject.Parse(result)["result"]?["refid"]?.ToString();
                return string.IsNullOrEmpty(refId)
                    // Kraken accepted the call but named no reference. Treated as a
                    // failure rather than a success, because a withdrawal we cannot
                    // point at is one the user cannot follow up.
                    ? throw new InvalidOperationException(
                        "Kraken accepted the withdrawal but returned no reference id — check its status on kraken.com")
                    : new WithdrawalResult(refId!, "initiated");
            });
        }

        private static double Num(JToken? t) =>
            double.TryParse(t?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

        /// <summary>
        /// Kraken answers HTTP 200 with an <c>error</c> array for permission and
        /// argument problems, so a caller that only checks the status code sees
        /// success and an empty result. Turning it into an exception is what lets
        /// <c>WalletService</c> classify it — and "EAPI:Invalid permissions" is
        /// exactly the case the user can fix, by editing the key on kraken.com.
        /// </summary>
        private static void ThrowOnKrakenError(string body, string what)
        {
            var errors = JObject.Parse(body)["error"] as JArray;
            if (errors == null || errors.Count == 0) return;

            string joined = string.Join("; ", errors.Select(e => e?.ToString()));
            if (joined.Contains("permission", StringComparison.OrdinalIgnoreCase)
             || joined.Contains("EAPI:Invalid key", StringComparison.OrdinalIgnoreCase))
                // "EAPI:Invalid key" is NOT only about the credential. Observed live
                // 2026-08-05: a key whose Funding scope demonstrably worked (deposit
                // methods and the primary Bitcoin address both answered) still got
                // EAPI:Invalid key for Lightning and the kBTC L2 networks — Kraken
                // answers it for address types it will not issue over the API. The
                // message names both readings so the user checks the right one.
                throw new UnauthorizedAccessException(
                    $"Kraken refused {what}: {joined}. If other funding calls work with this key, Kraken "
                  + "gives this same error for address types it does not issue over the API (Lightning "
                  + "invoices, kBTC L2 addresses) — use kraken.com for those. Otherwise check the key's "
                  + "Funding permissions on kraken.com.");

            throw new InvalidOperationException($"Kraken could not return {what}: {joined}");
        }

        /// <summary>
        /// Kraken's own asset codes — XBT for bitcoin, and the X/Z prefixes on its
        /// older listings. The API accepts the common spelling for most assets, but
        /// not BTC, which is the one holding most likely to be asked for.
        /// </summary>
        private static string NormaliseAsset(string asset)
        {
            string a = asset.Trim().ToUpperInvariant();
            return a switch { "BTC" => "XBT", "DOGE" => "XDG", _ => a };
        }

        // ── Auth helpers (HMAC-SHA512) ──────────────────────────────────────

        private async Task<string> PostPrivateAsync(string path, Dictionary<string, string> data)
        {
            // Sign-time credential checkout (phase 4 Track B canary).
            //
            // Before phase 4 this method read _apiKey / _apiSecret fields that
            // Configure() stashed at startup. Now we ask PluginHostServices.ApiKeys
            // for the credential at each sign site, use it locally, and let the
            // strings go out of scope at the end of the method. The GC still
            // holds the backing bytes until collection — .NET strings are
            // interned and immutable — but the root window drops from
            // "lifetime of the app" to "lifetime of one signed request".
            //
            // If the host bridge is null (unit tests, CLI runs) fall back to
            // the Configure-populated fields so existing callers still work.
            string apiKey, apiSecret;
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("Kraken").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("Kraken: no active API key configured.");
                apiKey    = checkout.Key;
                apiSecret = checkout.Secret;
            }
            else
            {
                if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                    throw new InvalidOperationException("Kraken: no API credentials configured.");
                apiKey    = _apiKey!;
                apiSecret = _apiSecret!;
            }

            // CAS loop guarantees strictly-increasing, never-reused nonces under concurrent
            // signers. An Increment+Exchange+Increment sequence races: two threads can both
            // observe `next < now`, both Exchange to `now`, then both Increment to `now+1`,
            // producing a duplicate nonce that Kraken silently rejects.
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long next;
            while (true)
            {
                long current = Interlocked.Read(ref _nonceCounter);
                long candidate = Math.Max(current + 1, now);
                if (Interlocked.CompareExchange(ref _nonceCounter, candidate, current) == current)
                {
                    next = candidate;
                    break;
                }
            }
            string nonce = next.ToString(CultureInfo.InvariantCulture);
            data["nonce"] = nonce;

            string postData = string.Join("&", data.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            byte[] noncePost = Encoding.UTF8.GetBytes(nonce + postData);
            byte[] sha256Hash = SHA256.HashData(noncePost);
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);

            byte[] combined = new byte[pathBytes.Length + sha256Hash.Length];
            Buffer.BlockCopy(pathBytes, 0, combined, 0, pathBytes.Length);
            Buffer.BlockCopy(sha256Hash, 0, combined, pathBytes.Length, sha256Hash.Length);

            byte[] secretBytes = Convert.FromBase64String(apiSecret);
            using var hmac = new HMACSHA512(secretBytes);
            byte[] signature = hmac.ComputeHash(combined);
            // Best-effort scrub of the base64-decoded secret bytes before they
            // leave scope. The managed strings above the HMAC (apiSecret) stay
            // until GC — we can't reach into the string backing — but we can
            // zero this byte[] without dodging the framework.
            Array.Clear(secretBytes, 0, secretBytes.Length);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{RestUrl}{path}")
            {
                Content = new FormUrlEncodedContent(data)
            };
            request.Headers.Add("API-Key", apiKey);
            request.Headers.Add("API-Sign", Convert.ToBase64String(signature));

            var response = await _httpClient.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string FormatPair(string symbol)
        {
            // Convert "BTC/USD" or "BTCUSD" to "BTC/USD" for WS v2
            if (symbol.Contains('/')) return symbol.ToUpper();
            if (symbol.Length >= 6)
            {
                var basePart = symbol[..^3].ToUpper();
                var quotePart = symbol[^3..].ToUpper();
                return $"{basePart}/{quotePart}";
            }
            return symbol.ToUpper();
        }

        private static string FormatRestPair(string symbol)
        {
            // REST API wants no separator: "XBTUSD" or "BTCUSD"
            return symbol.Replace("/", "").Replace("-", "").ToUpper();
        }

        private static string MapToKrakenOrderType(OrderType type) => type switch
        {
            OrderType.Market           => "market",
            OrderType.Limit            => "limit",
            OrderType.StopMarket       => "stop-loss",
            OrderType.StopLimit        => "stop-loss-limit",
            OrderType.TakeProfitMarket => "take-profit",
            OrderType.TakeProfitLimit  => "take-profit-limit",
            _                          => "market"
        };

        private static OrderType MapKrakenOrderType(string type) => type switch
        {
            "limit"              => OrderType.Limit,
            "stop-loss"          => OrderType.StopMarket,
            "stop-loss-limit"    => OrderType.StopLimit,
            "take-profit"        => OrderType.TakeProfitMarket,
            "take-profit-limit"  => OrderType.TakeProfitLimit,
            _                    => OrderType.Market
        };

        private static int MapWsInterval(string tf) => tf switch
        {
            "1m"  => 1,
            "5m"  => 5,
            "15m" => 15,
            "30m" => 30,
            "1h"  => 60,
            "4h"  => 240,
            "1d"  => 1440,
            "1w"  => 10080,
            _     => 60
        };

        private static int MapRestInterval(string tf) => MapWsInterval(tf);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
                _publicWs?.Dispose();
                _authWs?.Dispose();
                _orderUpdateSubject?.Dispose();
                _orderBookSubject?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
