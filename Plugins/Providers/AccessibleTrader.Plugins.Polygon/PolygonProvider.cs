using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;
using System.Reactive.Subjects;
using System.Reactive.Linq;

namespace AccessibleTrader.Plugins.Polygon
{
    public class PolygonProvider : BaseMarketDataProvider, IOrderBookProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private const string BaseUrl = "https://api.polygon.io";

        // Rate limiter: Polygon free tier = 5 req/min, paid = unlimited (use conservative default)
        private readonly RateLimiter _rateLimiter = new(5, TimeSpan.FromMinutes(1));

        private ReconnectingWebSocket? _ws;
        private string? _currentSymbol;
        private string? _currentMarket;

        // Order book streaming
        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();

        public override string Name => "Polygon";
        public override string Description => "Polygon.io Multi-Market Data";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Stock, MarketType.Crypto, MarketType.Forex };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 50000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.L2;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.OneHour, StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public PolygonProvider()
        {
            // Phase 4 Track B2 — allow-listed to api.polygon.io. The
            // delayed.polygon.io WS endpoints for stocks/crypto/forex use
            // ReconnectingWebSocket, not this HttpClient.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId:   "Polygon",
                allowedHosts: new[] { "api.polygon.io" });
        }

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(IOrderBookProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;

            // Adjust rate limit for paid plans
            if (config.TryGetValue("Plan", out var plan) && plan.Equals("paid", StringComparison.OrdinalIgnoreCase))
            {
                // paid plan users can override; we leave the default conservative for free tier
            }
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured");
            try
            {
                using var req = BuildAuthorizedGet($"{BaseUrl}/v3/reference/tickers?limit=1");
                var response = await _httpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                    return (true, "API key validated successfully");
                var body = await response.Content.ReadAsStringAsync();
                return (false, $"Key validation failed ({response.StatusCode}): {body}");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        // Authorization header carries the API key — keeps it out of URLs, query-string
        // logs, and any proxy/middleware that records request lines.
        private HttpRequestMessage BuildAuthorizedGet(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(_apiKey))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            return req;
        }

        private async Task<string> GetAuthorizedStringAsync(string url)
        {
            using var req = BuildAuthorizedGet(url);
            var response = await _httpClient.SendAsync(req);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public override async Task EnsureConnectedAsync()
        {
            if (_ws?.IsConnected == true) return;
            if (!IsConfigured) return;

            await DisconnectAsync();

            _connectionStateStream.OnNext(ConnectionState.Connecting);

            string wsUrl = _currentMarket switch
            {
                string m when m.Contains("Crypto", StringComparison.OrdinalIgnoreCase) => "wss://delayed.polygon.io/crypto",
                string m when m.Contains("Forex", StringComparison.OrdinalIgnoreCase)  => "wss://delayed.polygon.io/forex",
                _ => "wss://delayed.polygon.io/stocks"
            };

            _ws = new ReconnectingWebSocket(wsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    await ws.SendAsync($"{{\"action\":\"auth\",\"params\":\"{_apiKey}\"}}");
                    // Re-subscribe if we had a previous subscription
                    if (!string.IsNullOrEmpty(_currentSymbol) && !string.IsNullOrEmpty(_currentMarket))
                    {
                        string prefix = GetChannelPrefix(_currentMarket);
                        await ws.SendAsync($"{{\"action\":\"subscribe\",\"params\":\"{prefix}.{_currentSymbol}\"}}");
                    }
                })
                .OnMessage(HandleWebSocketMessage)
                .OnError(err => _errorStream.OnNext($"Polygon WS: {err}"))
                .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

            try
            {
                await _ws.ConnectAsync();
                _connectionStateStream.OnNext(ConnectionState.Connected);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Polygon connection failed: {ex.Message}");
                _connectionStateStream.OnNext(ConnectionState.Error);
                throw;
            }
        }

        private void HandleWebSocketMessage(string message)
        {
            try
            {
                var json = JArray.Parse(message);
                foreach (var item in json)
                {
                    var ev = item["ev"]?.ToString();
                    if (ev == "AM" || ev == "XA" || ev == "CA")
                    {
                        double o = item["o"]?.Value<double>() ?? 0;
                        double h = item["h"]?.Value<double>() ?? 0;
                        double l = item["l"]?.Value<double>() ?? 0;
                        double c = item["c"]?.Value<double>() ?? 0;
                        if (o == 0 && h == 0 && l == 0 && c == 0) continue;

                        _liveStream.OnNext(new Ohlcv(
                            DateTimeOffset.FromUnixTimeMilliseconds(item["e"]?.Value<long>() ?? 0).UtcDateTime,
                            o, h, l, c,
                            item["v"]?.Value<double>() ?? 0));
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Polygon] Malformed feed frame skipped: {ex.GetType().Name}"); }
        }

        private static string GetChannelPrefix(string market)
        {
            if (market.Contains("Crypto", StringComparison.OrdinalIgnoreCase)) return "XA";
            if (market.Contains("Forex", StringComparison.OrdinalIgnoreCase)) return "CA";
            return "AM";
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            _currentMarket = market;
            await EnsureConnectedAsync();
            if (_currentSymbol == symbol) return;

            // Unsubscribe previous
            if (!string.IsNullOrEmpty(_currentSymbol) && _ws != null)
            {
                string prefix = GetChannelPrefix(market);
                await _ws.SendAsync($"{{\"action\":\"unsubscribe\",\"params\":\"{prefix}.{_currentSymbol}\"}}");
            }

            _currentSymbol = symbol;
            if (_ws != null)
            {
                string newPrefix = GetChannelPrefix(market);
                await _ws.SendAsync($"{{\"action\":\"subscribe\",\"params\":\"{newPrefix}.{_currentSymbol}\"}}");
            }
        }

        public override async Task DisconnectAsync()
        {
            if (_ws != null)
            {
                await _ws.DisconnectAsync();
                _ws.Dispose();
                _ws = null;
            }
            _currentSymbol = null;
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            var symbol = request.Symbol.ToUpper();
            var (multiplier, timespan) = MapTimeframe(request.Timeframe);
            int limit = Math.Min(request.Limit, 1000);
            long fromMs = request.Since ?? (DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds());
            long toMs = request.Until ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string url = $"{BaseUrl}/v2/aggs/ticker/{symbol}/range/{multiplier}/{timespan}/{fromMs}/{toMs}?adjusted=true&sort=asc&limit={limit}";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await GetAuthorizedStringAsync(url);
                    var json = JObject.Parse(response);
                    var results = json["results"] as JArray;
                    if (results == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = results.Select(r => new Ohlcv(
                        DateTimeOffset.FromUnixTimeMilliseconds(r["t"]?.Value<long>() ?? 0).UtcDateTime,
                        r["o"]?.Value<double>() ?? 0,
                        r["h"]?.Value<double>() ?? 0,
                        r["l"]?.Value<double>() ?? 0,
                        r["c"]?.Value<double>() ?? 0,
                        r["v"]?.Value<double>() ?? 0)).ToList();
                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch (HttpRequestException ex)
            {
                _errorStream.OnNext($"Polygon: network error fetching {symbol}: {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
            catch (TaskCanceledException)
            {
                // Request timeout or upstream cancellation — the caller already knows.
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                _errorStream.OnNext($"Polygon: malformed response for {symbol}: {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Polygon: fetch error for {symbol}: {ex.Message}");
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
                    string m = market switch { MarketType.Stock => "stocks", MarketType.Crypto => "crypto", MarketType.Forex => "fx", _ => "stocks" };
                    var response = await GetAuthorizedStringAsync($"{BaseUrl}/v3/reference/tickers?market={m}&active=true&limit=1000");
                    var results = JObject.Parse(response)["results"] as JArray;
                    return results?.Select(r => r["ticker"]?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s).ToList() ?? new List<string>();
                });
            }
            catch (HttpRequestException ex)
            {
                _errorStream.OnNext($"Polygon: network error fetching symbol list: {ex.Message}");
                return new List<string>();
            }
            catch (TaskCanceledException)
            {
                return new List<string>();
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                _errorStream.OnNext($"Polygon: malformed symbol-list response: {ex.Message}");
                return new List<string>();
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Polygon: symbol-list error: {ex.Message}");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string> { "Standard" });
        public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(NativelySupportedTimeframes);

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            if (!IsConfigured) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

            // Polygon provides NBBO (National Best Bid/Offer) snapshots for stocks,
            // and full order books for crypto via the /v3/snapshot endpoint
            bool isCrypto = _currentMarket?.Contains("Crypto", StringComparison.OrdinalIgnoreCase) == true;

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    if (isCrypto)
                    {
                        // Crypto: Use the L2 order book snapshot
                        string url = $"{BaseUrl}/v3/snapshot/crypto/book/{symbol.ToUpper()}";
                        var response = await GetAuthorizedStringAsync(url);
                        var json = JObject.Parse(response);
                        var data = json["data"];
                        if (data == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                        var bids = (data["bids"] as JArray)?.Take(limit)
                            .Select(b => new OrderBookEntry(
                                b["p"]?.Value<double>() ?? 0,
                                (b["x"] as JArray)?.Sum(x => x["s"]?.Value<double>() ?? 0) ?? 0))
                            .ToList() ?? new();
                        var asks = (data["asks"] as JArray)?.Take(limit)
                            .Select(a => new OrderBookEntry(
                                a["p"]?.Value<double>() ?? 0,
                                (a["x"] as JArray)?.Sum(x => x["s"]?.Value<double>() ?? 0) ?? 0))
                            .ToList() ?? new();
                        return (bids, asks);
                    }
                    else
                    {
                        // Stocks: Use NBBO snapshot (1-level best bid/ask)
                        string url = $"{BaseUrl}/v3/snapshot?ticker.any_of={symbol.ToUpper()}";
                        var response = await GetAuthorizedStringAsync(url);
                        var json = JObject.Parse(response);
                        var results = json["results"] as JArray;
                        var snap = results?.FirstOrDefault();
                        if (snap == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                        var lastQuote = snap["last_quote"];
                        if (lastQuote == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                        double bidPx = lastQuote["bid_price"]?.Value<double>() ?? 0;
                        double bidSz = lastQuote["bid_size"]?.Value<double>() ?? 0;
                        double askPx = lastQuote["ask_price"]?.Value<double>() ?? 0;
                        double askSz = lastQuote["ask_size"]?.Value<double>() ?? 0;

                        var bids = bidPx > 0 ? new List<OrderBookEntry> { new(bidPx, bidSz) } : new();
                        var asks = askPx > 0 ? new List<OrderBookEntry> { new(askPx, askSz) } : new();
                        return (bids, asks);
                    }
                });
            }
            catch { return (new List<OrderBookEntry>(), new List<OrderBookEntry>()); }
        }

        // ── IOrderBookProvider ──────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) => _orderBookSubject.AsObservable();

        // ── Helpers ─────────────────────────────────────────────────────────

        private (int multiplier, string timespan) MapTimeframe(string tf) => tf switch
        {
            "1m"  => (1, "minute"),
            "5m"  => (5, "minute"),
            "15m" => (15, "minute"),
            "1h"  => (1, "hour"),
            "1d"  => (1, "day"),
            "1w"  => (1, "week"),
            "1M"  => (1, "month"),
            _     => (1, "hour")
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
                _ws?.Dispose();
                _orderBookSubject?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
