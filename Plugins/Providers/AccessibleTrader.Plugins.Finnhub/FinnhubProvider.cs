using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Finnhub
{
    /// <summary>
    /// Finnhub provider — global stocks, forex, crypto, commodities, and indices.
    /// Data-only (no trading). REST historical + WebSocket real-time trades.
    /// </summary>
    public class FinnhubProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private const string RestUrl = "https://finnhub.io/api/v1";

        // Rate limiter: 60 req/min free tier
        private readonly RateLimiter _rateLimiter = new(60, TimeSpan.FromMinutes(1));

        // WebSocket for real-time trades
        private ReconnectingWebSocket? _ws;
        private string? _currentSymbol;
        private string? _currentTimeframe;
        private Ohlcv? _lastCandle;
        private DateTime? _lastCandleStart;

        public override string Name => "Finnhub";
        public override string Description => "Finnhub — Global Stocks, Forex, Crypto & Commodities";
        public override List<MarketType> SupportedMarkets => new List<MarketType>
        {
            MarketType.Stock, MarketType.Forex, MarketType.Crypto,
            MarketType.Commodity, MarketType.Index
        };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => true;
        // The live path builds a candle client-side and re-emits the SAME bar each
        // trade with cumulative volume (see the trade handler / UpdateWith) —
        // cumulative, not per-tick deltas. Without this the consumer's consolidator
        // re-accumulates volume and inflates it (same fix as Kraken/MEXC).
        public override AccessibleTrader.Sdk.Plugins.LiveTickStyle LiveTickStyle =>
            AccessibleTrader.Sdk.Plugins.LiveTickStyle.CumulativeBars;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 1000;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes, StandardTimeframes.ThirtyMinutes,
            StandardTimeframes.OneHour, StandardTimeframes.OneDay,
            StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        // Common commodity symbols for GetAvailableSymbolsAsync
        private static readonly List<string> CommoditySymbols = new()
        {
            "OANDA:WTICO_USD", "OANDA:BCO_USD", "OANDA:NATGAS_USD",
            "OANDA:XAU_USD", "OANDA:XAG_USD", "OANDA:XPT_USD", "OANDA:XPD_USD",
            "OANDA:CORN_USD", "OANDA:WHEAT_USD", "OANDA:SOYBN_USD",
            "OANDA:SUGAR_USD", "OANDA:XCU_USD"
        };

        public FinnhubProvider()
        {
            // Phase 4 Track B2 — allow-listed to finnhub.io. The
            // ws.finnhub.io WS endpoint uses ReconnectingWebSocket.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId:   "Finnhub",
                allowedHosts: new[] { "finnhub.io" });
        }

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured");
            try
            {
                var response = await _httpClient.GetAsync($"{RestUrl}/quote?symbol=AAPL&token={_apiKey}");
                if (response.IsSuccessStatusCode)
                    return (true, "API key validated successfully");
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return (false, "Invalid API key");
                return (false, $"Key validation failed ({response.StatusCode})");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
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

            _currentSymbol = symbol;
            _currentTimeframe = timeframe;
            _lastCandle = null;
            _lastCandleStart = null;

            if (_ws != null) { await _ws.DisconnectAsync(); _ws.Dispose(); }

            _ws = new ReconnectingWebSocket($"wss://ws.finnhub.io?token={_apiKey}",
                heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    var sub = new JObject { ["type"] = "subscribe", ["symbol"] = symbol };
                    await ws.SendAsync(sub.ToString());
                })
                .OnMessage(HandleWebSocketMessage)
                .OnError(err => _errorStream.OnNext($"Finnhub WS: {err}"))
                .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

            await _ws.ConnectAsync();
        }

        private void HandleWebSocketMessage(string msg)
        {
            try
            {
                var json = JObject.Parse(msg);
                if (json["type"]?.ToString() != "trade") return;

                var data = json["data"] as JArray;
                if (data == null || data.Count == 0) return;

                // Process the latest trade
                var trade = data.Last!;
                double price = trade["p"]?.Value<double>() ?? 0;
                double volume = trade["v"]?.Value<double>() ?? 0;
                long tsMs = trade["t"]?.Value<long>() ?? 0;

                if (price <= 0) return;

                var tradeTime = tsMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(tsMs).UtcDateTime
                    : DateTime.UtcNow;

                var interval = MapTimeframeToTimeSpan(_currentTimeframe ?? "1h");

                if (_lastCandle.HasValue && _lastCandleStart.HasValue)
                {
                    if (tradeTime >= _lastCandleStart.Value.Add(interval))
                    {
                        var newStart = _lastCandleStart.Value;
                        while (tradeTime >= newStart.Add(interval)) newStart = newStart.Add(interval);
                        _lastCandleStart = newStart;
                        _lastCandle = new Ohlcv(newStart, price, price, price, price, volume);
                    }
                    else
                    {
                        var tick = new Ohlcv(tradeTime, price, price, price, price, volume);
                        _lastCandle = _lastCandle.Value.UpdateWith(tick);
                    }
                }
                else
                {
                    _lastCandleStart = tradeTime;
                    _lastCandle = new Ohlcv(tradeTime, price, price, price, price, volume);
                }

                _liveStream.OnNext(_lastCandle.Value);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Finnhub] Malformed feed frame skipped: {ex.GetType().Name}"); }
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

            string resolution = MapResolution(request.Timeframe);
            long from = request.Since.HasValue
                ? request.Since.Value / 1000
                : DateTimeOffset.UtcNow.AddDays(-365).ToUnixTimeSeconds();
            long to = request.Until.HasValue
                ? request.Until.Value / 1000
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            string url = $"{RestUrl}/stock/candle?symbol={Uri.EscapeDataString(request.Symbol)}&resolution={resolution}&from={from}&to={to}&token={_apiKey}";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JObject.Parse(response);

                    // Finnhub moved /stock/candle behind a paid plan — free keys now
                    // get s!="ok" (or 403). Surface it so a permanently-blank chart
                    // isn't silent; "no_data" is a legitimate empty range, not an error.
                    var s = json["s"]?.ToString();
                    if (s != "ok")
                    {
                        if (s != "no_data")
                            _errorStream.OnNext($"Finnhub candles unavailable for {request.Symbol} (status '{s}') — the candle endpoint may require a paid plan.");
                        return (new List<Ohlcv>(), new List<(long, double)>());
                    }

                    // Finnhub returns parallel arrays that must be zipped
                    var opens   = json["o"] as JArray;
                    var highs   = json["h"] as JArray;
                    var lows    = json["l"] as JArray;
                    var closes  = json["c"] as JArray;
                    var volumes = json["v"] as JArray;
                    var times   = json["t"] as JArray;

                    if (opens == null || times == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    int count = Math.Min(opens.Count, times.Count);
                    var ohlcvList = new List<Ohlcv>(count);

                    for (int i = 0; i < count; i++)
                    {
                        long ts = times[i]?.Value<long>() ?? 0;
                        ohlcvList.Add(new Ohlcv(
                            DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime,
                            opens[i]?.Value<double>() ?? 0,
                            highs?[i]?.Value<double>() ?? 0,
                            lows?[i]?.Value<double>() ?? 0,
                            closes?[i]?.Value<double>() ?? 0,
                            volumes?[i]?.Value<double>() ?? 0));
                    }

                    ohlcvList = ohlcvList.OrderBy(x => x.Date).ToList();

                    int limit = Math.Min(request.Limit, ohlcvList.Count);
                    ohlcvList = ohlcvList.TakeLast(limit).ToList();

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
                _errorStream.OnNext($"Finnhub fetch error: {ex.Message}");
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
                    string url = market switch
                    {
                        MarketType.Forex     => $"{RestUrl}/forex/symbol?exchange=oanda&token={_apiKey}",
                        MarketType.Crypto    => $"{RestUrl}/crypto/symbol?exchange=binance&token={_apiKey}",
                        MarketType.Commodity => null!, // Use static list
                        MarketType.Index     => $"{RestUrl}/stock/symbol?exchange=US&token={_apiKey}", // Indices mixed with stocks
                        _                    => $"{RestUrl}/stock/symbol?exchange=US&token={_apiKey}"
                    };

                    if (market == MarketType.Commodity)
                        return CommoditySymbols.ToList();

                    var response = await _httpClient.GetStringAsync(url);
                    var arr = JArray.Parse(response);

                    return arr
                        .Select(s => s["symbol"]?.ToString() ?? s["displaySymbol"]?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Distinct()
                        .OrderBy(s => s)
                        .Take(2000)
                        .ToList();
                });
            }
            catch (HttpRequestException ex)
            {
                _errorStream.OnNext($"Finnhub: network error fetching symbol list: {ex.Message}");
                return new List<string>();
            }
            catch (TaskCanceledException)
            {
                return new List<string>();
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                _errorStream.OnNext($"Finnhub: malformed symbol-list response: {ex.Message}");
                return new List<string>();
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Finnhub: symbol-list error: {ex.Message}");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(market == MarketType.Stock
                ? new List<string> { "US", "LSE", "TSE", "XETRA", "NSE" }
                : new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "30m", "1h", "1d", "1w", "1M" });

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string MapResolution(string tf) => tf switch
        {
            "1m"  => "1",
            "5m"  => "5",
            "15m" => "15",
            "30m" => "30",
            "1h"  => "60",
            "1d"  => "D",
            "1w"  => "W",
            "1M"  => "M",
            _     => "60"
        };

        private static TimeSpan MapTimeframeToTimeSpan(string tf) => tf switch
        {
            "1m"  => TimeSpan.FromMinutes(1),
            "5m"  => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h"  => TimeSpan.FromHours(1),
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
