using System.Globalization;
using System.Reactive.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.TwelveData
{
    /// <summary>
    /// Twelve Data provider — global stocks, forex, crypto, ETFs, indices.
    /// Data-only (no trading). Supports REST historical + WebSocket real-time.
    /// </summary>
    public class TwelveDataProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;

        /// <summary>
        /// The API key, escaped for use as a query-string VALUE.
        ///
        /// <para>Every call in this file authenticates by putting the key in the URL, and a raw
        /// interpolation mangles any key that is not already URL-safe: <c>&amp;</c> ends the
        /// parameter and starts a new one (so the key is TRUNCATED at the ampersand), <c>+</c>
        /// decodes to a space at the server, <c>#</c> throws the rest of the URL away as a
        /// fragment. All three are legal in a generated credential. The request then fails to
        /// authenticate and the user is told "validation failed" with nothing to act on —
        /// the key they pasted is correct, and nothing in the message says otherwise.</para>
        ///
        /// <para><c>FredProvider</c> was the only provider in the fleet that escaped its key.
        /// This property is that fix, and it is named so <c>ApiKeyUrlEscapingTests</c> can
        /// require the name at every key-bearing query site.</para>
        /// </summary>
        private string KeyParam => Uri.EscapeDataString(_apiKey ?? string.Empty);

        private const string RestUrl = "https://api.twelvedata.com";
        private const string WsUrl   = "wss://ws.twelvedata.com/v1/quotes/price";

        // Rate limiter: Free tier = 8 req/min, Growth = 30/min
        private readonly RateLimiter _rateLimiter = new(8, TimeSpan.FromMinutes(1));

        // WebSocket
        private ReconnectingWebSocket? _ws;
        private string? _currentSymbol;
        private string? _currentTimeframe;
        private Ohlcv? _lastCandle;
        private DateTime? _lastCandleStart;

        public override string Name => "Twelve Data";
        public override string Description => "Global Stocks, Forex, Crypto & Indices";
        public override List<MarketType> SupportedMarkets => new List<MarketType>
        {
            MarketType.Stock, MarketType.Forex, MarketType.Crypto, MarketType.Index
        };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 5000;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour, StandardTimeframes.TwoHours,
            StandardTimeframes.FourHours, StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public TwelveDataProvider()
        {
            // Phase 4 Track B2 — allow-listed to api.twelvedata.com. The
            // ws.twelvedata.com WS endpoint uses ReconnectingWebSocket.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId:   "TwelveData",
                allowedHosts: new[] { "api.twelvedata.com" });
        }

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;

            // Upgrade rate limit for paid plans
            if (config.TryGetValue("Plan", out var plan))
            {
                if (plan.Equals("growth", StringComparison.OrdinalIgnoreCase) ||
                    plan.Equals("pro", StringComparison.OrdinalIgnoreCase))
                {
                    // Can't change RateLimiter after construction; users on paid plans
                    // will benefit from the backoff mechanism handling bursts
                }
            }
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured");
            try
            {
                var response = await _httpClient.GetAsync($"{RestUrl}/api_usage?apikey={KeyParam}");
                if (response.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                    var used = json["current_usage"]?.Value<int>() ?? 0;
                    var limit = json["plan_limit"]?.Value<int>() ?? 0;
                    return (true, $"API key valid. Usage: {used}/{limit} requests today.");
                }
                return (false, $"Key validation failed ({response.StatusCode})");
            }
            catch (Exception ex)
            {
                // TwelveData's apikey is a URL query param; HttpRequestException messages
                // can include the URL on certain failure paths, which would leak the key.
                // Type name gives the user enough signal without the key bleeding through.
                return (false, $"Key validation error: {ex.GetType().Name}");
            }
        }

        // ── Connection ──────────────────────────────────────────────────────

        public override Task EnsureConnectedAsync()
        {
            if (IsConfigured) _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            if (_currentSymbol == symbol && _currentTimeframe == timeframe && _ws?.IsConnected == true)
                return;

            _currentSymbol    = symbol;
            _currentTimeframe = timeframe;

            if (_ws != null) { await _ws.DisconnectAsync(); _ws.Dispose(); }

            _ws = new ReconnectingWebSocket($"{WsUrl}?apikey={KeyParam}",
                heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    // Subscribe to real-time price updates
                    var sub = new JObject
                    {
                        ["action"] = "subscribe",
                        ["params"] = new JObject
                        {
                            ["symbols"] = symbol.ToUpperInvariant()
                        }
                    };
                    await ws.SendAsync(sub.ToString());
                })
                .OnMessage(HandleWebSocketMessage)
                .OnError(err => _errorStream.OnNext($"TwelveData WS: {err}"))
                .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

            await _ws.ConnectAsync();
        }

        private void HandleWebSocketMessage(string msg)
        {
            try
            {
                var json = JObject.Parse(msg);
                var ev = json["event"]?.ToString();

                if (ev == "price")
                {
                    double price = json["price"]?.Value<double>() ?? 0;
                    if (price <= 0) return;

                    // TwelveData's WS price event sends a Unix timestamp in SECONDS
                    // (e.g. 1600595462 = 2020-09-13; confirmed against their docs).
                    // TimestampParser normalizes by magnitude anyway so a future
                    // switch to milliseconds can never silently throw the candle
                    // ~50,000 years into the future — this site used to hand-roll
                    // the same rule with a different threshold than the SDK's.
                    long ts = json["timestamp"]?.Value<long>() ?? 0;
                    var now = ts <= 0 ? DateTime.UtcNow : TimestampParser.Parse(ts);

                    if (_lastCandle.HasValue && _lastCandleStart.HasValue)
                    {
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
                    else
                    {
                        // Initialize first candle
                        _lastCandleStart = now;
                        _lastCandle = new Ohlcv(now, price, price, price, price, 0);
                        _liveStream.OnNext(_lastCandle.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"TwelveData tick parse failed: {ex.GetType().Name}");
            }
        }

        public override async Task DisconnectAsync()
        {
            if (_ws != null) { await _ws.DisconnectAsync(); _ws.Dispose(); _ws = null; }
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

            string interval = MapToTwelveDataInterval(request.Timeframe);
            int limit = Math.Min(request.Limit, 5000);

            // timezone=UTC is load-bearing: without it the venue returns EXCHANGE-LOCAL
            // wall clock (verified live 2026-08-23: AAPL's last Friday bar reads
            // "15:55:00" bare and "19:55:00" with timezone=UTC; BTC/USD is UTC either
            // way), which the AssumeUniversal parse below then declares to be UTC —
            // every US-stock intraday bar sat 4-5 hours out of session, and the candles
            // look plausible so nothing signals it. The parameter also makes the venue
            // read start_date/end_date below in UTC, matching how they are rendered.
            string url = $"{RestUrl}/time_series?symbol={Uri.EscapeDataString(request.Symbol ?? "")}&interval={interval}&outputsize={limit}&apikey={KeyParam}&format=JSON&timezone=UTC";

            if (request.Since.HasValue)
                url += $"&start_date={DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}";
            if (request.Until.HasValue)
                url += $"&end_date={DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);

                    // Check for errors
                    if (json["status"]?.ToString() == "error")
                    {
                        _errorStream.OnNext($"TwelveData: {json["message"]}");
                        return (new List<Ohlcv>(), new List<(long, double)>());
                    }

                    var values = json["values"] as JArray;
                    if (values == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = values.Select(v =>
                    {
                        var dateStr = v["datetime"]?.ToString() ?? "";
                        DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date);
                        return new Ohlcv(
                            date,
                            double.Parse(v["open"]?.ToString()   ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(v["high"]?.ToString()   ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(v["low"]?.ToString()    ?? "0", CultureInfo.InvariantCulture),
                            double.Parse(v["close"]?.ToString()  ?? "0", CultureInfo.InvariantCulture),
                            double.TryParse(v["volume"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double vol) ? vol : 0);
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
                // See ValidateApiKeyAsync: ex.Message can contain the URL including apikey.
                _errorStream.OnNext($"TwelveData fetch error: {ex.GetType().Name}");
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
                    string type = market switch
                    {
                        MarketType.Stock  => "Stock",
                        MarketType.Forex  => "Forex",
                        MarketType.Crypto => "Digital Currency",
                        MarketType.Index  => "Index",
                        _                 => "Stock"
                    };

                    // Fetch US exchange symbols by default; global coverage available
                    string url = $"{RestUrl}/stocks?source=docs&exchange=NYSE,NASDAQ&apikey={KeyParam}";
                    if (market == MarketType.Forex)
                        url = $"{RestUrl}/forex_pairs?apikey={KeyParam}";
                    else if (market == MarketType.Crypto)
                        url = $"{RestUrl}/cryptocurrencies?apikey={KeyParam}";
                    else if (market == MarketType.Index)
                        url = $"{RestUrl}/indices?apikey={KeyParam}";

                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);
                    var data = json["data"] as JArray;
                    if (data == null) return new List<string>();

                    return data
                        .Select(s => s["symbol"]?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Distinct()
                        .OrderBy(s => s)
                        .Take(2000) // Cap to avoid huge lists
                        .ToList();
                });
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// Search for symbols matching a query across all markets.
        /// </summary>
        public async Task<List<(string Symbol, string Name, string Type, string Exchange)>> SearchSymbolsAsync(string query, int limit = 30)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(query)) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string url = $"{RestUrl}/symbol_search?symbol={Uri.EscapeDataString(query)}&outputsize={limit}&apikey={KeyParam}";
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);
                    var data = json["data"] as JArray;
                    if (data == null) return new List<(string, string, string, string)>();

                    return data.Select(s => (
                        s["symbol"]?.ToString() ?? "",
                        s["instrument_name"]?.ToString() ?? "",
                        s["instrument_type"]?.ToString() ?? "",
                        s["exchange"]?.ToString() ?? ""
                    )).Where(x => !string.IsNullOrEmpty(x.Item1)).ToList();
                });
            }
            catch { return new(); }
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "30m", "1h", "2h", "4h", "1d", "1w", "1M" });

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string MapToTwelveDataInterval(string tf) => tf switch
        {
            "1m"  => "1min",
            "5m"  => "5min",
            "15m" => "15min",
            "30m" => "30min",
            "1h"  => "1h",
            "2h"  => "2h",
            "4h"  => "4h",
            "1d"  => "1day",
            "1w"  => "1week",
            "1M"  => "1month",
            _     => "1h"
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
                _ws?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
