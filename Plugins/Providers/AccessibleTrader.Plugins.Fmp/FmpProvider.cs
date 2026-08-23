using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Fmp
{
    /// <summary>
    /// Financial Modeling Prep — OHLCV price data for stocks, crypto, forex, commodities, and indices.
    /// Covers 70,000+ securities across 60+ exchanges, 4,500+ cryptos, 1,500+ forex pairs.
    /// Free tier: 250 requests/day. Auth: ?apikey=KEY query param on every request.
    /// </summary>
    public class FmpProvider : BaseMarketDataProvider
    {
        // MIGRATED 2026-08-02 from /api/v3. FMP retired the v3 and v4 paths: they answer
        // 403 "Legacy Endpoint" for every key without a subscription predating 2025-08-31, so the
        // provider worked for old keys and was dead for every new user — a silent read-path failure
        // that no test caught because the old key in the repo still worked. On /stable the symbol is
        // a query parameter rather than a path segment, and several endpoints are plan-gated.
        private const string BaseUrl = "https://financialmodelingprep.com/stable";
        private HttpClient? _httpClient;
        private string _apiKey = "";
        private readonly RateLimiter _rateLimiter = new(4, TimeSpan.FromMinutes(1)); // Conservative for 250/day

        // Symbol list caches (FMP lists are large; avoid re-fetching)
        private List<string>? _stockSymbolCache;
        private List<string>? _etfSymbolCache;
        private List<string>? _cryptoSymbolCache;
        private List<string>? _forexSymbolCache;
        private List<string>? _commoditySymbolCache;
        private List<string>? _indexSymbolCache;

        public override string Name => "FMP";
        public override string Description => "Financial Modeling Prep — stocks, crypto, forex, commodities, indices";
        public override List<MarketType> SupportedMarkets => new()
        {
            MarketType.Stock, MarketType.Crypto, MarketType.Forex,
            MarketType.Commodity, MarketType.Index
        };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest => 5000;
        public override List<string> NativelySupportedTimeframes => new()
        {
            "1m", "5m", "15m", "30m", "1h", "4h", "1d"
        };

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key) && !string.IsNullOrWhiteSpace(key))
            {
                _apiKey = key;
                // Phase 4 Track B2 — allow-listed to financialmodelingprep.com.
                // Created on-demand here only when an API key is configured.
                // CreateHttpClient sets User-Agent by default.
                _httpClient = PluginHostServices.CreateHttpClient(
                    providerId:   "Fmp",
                    allowedHosts: new[] { "financialmodelingprep.com" });
            }
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured.");
            try
            {
                var url = $"{BaseUrl}/quote?symbol=AAPL&apikey={_apiKey}";
                var response = await _httpClient!.GetAsync(url).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (body.Contains("Error") || body.Contains("Invalid"))
                        return (false, "API key rejected by FMP.");
                    return (true, "FMP API key validated.");
                }
                // 403 here means the LEGACY-endpoint refusal, which is a code problem rather than a
                // key problem, and saying "invalid key" would send the user to regenerate a key that
                // was never the issue.
                if ((int)response.StatusCode == 403)
                    return (false, "FMP refused the request as a legacy endpoint. The app is calling a retired API path — this is a bug, not a key problem.");
                if ((int)response.StatusCode == 402)
                    return (false, "Your FMP plan does not include this endpoint.");
                return (false, $"Validation failed: HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex) { return (false, $"Validation error: {ex.Message}"); }
        }

        public override Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(IsConfigured ? ConnectionState.Connected : ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;
        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        // ── Sub-types ────────────────────────────────────────────────────────

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
        {
            return Task.FromResult(market switch
            {
                MarketType.Stock => new List<string> { "Spot", "ETF" },
                _ => new List<string> { "Spot" }
            });
        }

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        // ── Symbol listing ───────────────────────────────────────────────────

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            if (!IsConfigured) return new List<string>();

            try
            {
                return market switch
                {
                    MarketType.Stock when subType == "ETF" => await GetEtfSymbolsAsync().ConfigureAwait(false),
                    MarketType.Stock => await GetStockSymbolsAsync().ConfigureAwait(false),
                    MarketType.Crypto => await GetCryptoSymbolsAsync().ConfigureAwait(false),
                    MarketType.Forex => await GetForexSymbolsAsync().ConfigureAwait(false),
                    MarketType.Commodity => await GetCommoditySymbolsAsync().ConfigureAwait(false),
                    MarketType.Index => await GetIndexSymbolsAsync().ConfigureAwait(false),
                    _ => new List<string>()
                };
            }
            catch { return new List<string>(); }
        }

        private async Task<List<string>> GetStockSymbolsAsync()
        {
            if (_stockSymbolCache != null) return _stockSymbolCache;
            var json = await FetchJsonAsync($"{BaseUrl}/stock-list?apikey={_apiKey}").ConfigureAwait(false);
            if (json == null) return new List<string>();
            _stockSymbolCache = json.Select(t => t["symbol"]?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s)
                .ToList();
            return _stockSymbolCache;
        }

        private async Task<List<string>> GetEtfSymbolsAsync()
        {
            if (_etfSymbolCache != null) return _etfSymbolCache;
            var json = await FetchJsonAsync($"{BaseUrl}/etf-list?apikey={_apiKey}").ConfigureAwait(false);
            if (json == null) return new List<string>();
            _etfSymbolCache = json.Select(t => t["symbol"]?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s)
                .ToList();
            return _etfSymbolCache;
        }

        private async Task<List<string>> GetCryptoSymbolsAsync()
        {
            if (_cryptoSymbolCache != null) return _cryptoSymbolCache;
            var json = await FetchJsonAsync($"{BaseUrl}/cryptocurrency-list?apikey={_apiKey}").ConfigureAwait(false);
            if (json == null) return new List<string>();
            _cryptoSymbolCache = json.Select(t => t["symbol"]?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s) && s.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s)
                .ToList();
            return _cryptoSymbolCache;
        }

        private async Task<List<string>> GetForexSymbolsAsync()
        {
            if (_forexSymbolCache != null) return _forexSymbolCache;
            var json = await FetchJsonAsync($"{BaseUrl}/forex-list?apikey={_apiKey}").ConfigureAwait(false);
            if (json == null) return new List<string>();
            _forexSymbolCache = json.Select(t => t["symbol"]?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s)
                .ToList();
            return _forexSymbolCache;
        }

        private async Task<List<string>> GetCommoditySymbolsAsync()
        {
            if (_commoditySymbolCache != null) return _commoditySymbolCache;
            var json = await FetchJsonAsync($"{BaseUrl}/commodities-list?apikey={_apiKey}").ConfigureAwait(false);
            if (json == null) return new List<string>();
            _commoditySymbolCache = json.Select(t => t["symbol"]?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s)
                .ToList();
            return _commoditySymbolCache;
        }

        private async Task<List<string>> GetIndexSymbolsAsync()
        {
            if (_indexSymbolCache != null) return _indexSymbolCache;
            var json = await FetchJsonAsync($"{BaseUrl}/index-list?apikey={_apiKey}").ConfigureAwait(false);
            if (json == null) return new List<string>();
            _indexSymbolCache = json.Select(t => t["symbol"]?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s)
                .ToList();
            return _indexSymbolCache;
        }

        // ── OHLCV fetch ─────────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new(), new());

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var bars = request.Timeframe == "1d"
                        ? await FetchDailyAsync(request).ConfigureAwait(false)
                        : await FetchIntradayAsync(request).ConfigureAwait(false);

                    var volume = bars.Select(b =>
                        (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();

                    return (bars, volume);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"FMP FetchOhlcvAsync failed for {request.Symbol} ({ex.GetType().Name}): {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new(), new());
            }
        }

        private async Task<List<Ohlcv>> FetchDailyAsync(MarketDataRequest request)
        {
            var symbol = CleanSymbol(request.Symbol);
            var url = $"{BaseUrl}/historical-price-eod/full?symbol={symbol}&apikey={_apiKey}";

            if (request.Since.HasValue)
            {
                var from = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                url += $"&from={from}";
            }
            if (request.Until.HasValue)
            {
                var to = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                url += $"&to={to}";
            }

            // /stable returns a FLAT array; v3 wrapped it in {"historical": [...]} and dropped the
            // `timeseries` parameter, so the row cap is applied here instead.
            var body = await _httpClient!.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(body);
            if (!arr.Any()) return new List<Ohlcv>();

            return arr
                .Select(ParseDailyBar)
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => b.Date)
                .TakeLast(request.Limit)
                .ToList();
        }

        private async Task<List<Ohlcv>> FetchIntradayAsync(MarketDataRequest request)
        {
            var symbol = CleanSymbol(request.Symbol);
            var interval = MapTimeframe(request.Timeframe);
            var url = $"{BaseUrl}/historical-chart/{interval}?symbol={symbol}&apikey={_apiKey}";

            if (request.Since.HasValue)
            {
                var from = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                url += $"&from={from}";
            }
            if (request.Until.HasValue)
            {
                var to = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                url += $"&to={to}";
            }

            var body = await _httpClient!.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(body);

            return arr
                .Select(ParseIntradayBar)
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => b.Date)
                // TakeLast, not Take: Limit must keep the MOST-RECENT bars like every
                // sibling provider (Finnhub/Schwab/Kraken) — Take kept the oldest ones,
                // silently serving stale data when FMP returned more than Limit.
                // (Found by the Phase E contract-test enrollment, 2026-07-09.)
                .TakeLast(request.Limit)
                .ToList();
        }

        // ── Order book (not supported) ───────────────────────────────────────

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
            => Task.FromResult<(List<OrderBookEntry>, List<OrderBookEntry>)>((new(), new()));

        // ── Parsing helpers ──────────────────────────────────────────────────

        private static Ohlcv? ParseDailyBar(JToken t)
        {
            try
            {
                var dateStr = t["date"]?.ToString();
                if (string.IsNullOrEmpty(dateStr)) return null;
                var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                return new Ohlcv(
                    date.ToUniversalTime(),
                    t["open"]?.Value<double>() ?? 0,
                    t["high"]?.Value<double>() ?? 0,
                    t["low"]?.Value<double>() ?? 0,
                    t["close"]?.Value<double>() ?? 0,
                    t["volume"]?.Value<double>() ?? 0);
            }
            catch { return null; }
        }

        private static Ohlcv? ParseIntradayBar(JToken t)
        {
            try
            {
                var dateStr = t["date"]?.ToString();
                if (string.IsNullOrEmpty(dateStr)) return null;
                // Intraday "date" is US-Eastern wall-clock ("2021-10-08 16:00:00").
                // The old AssumeUniversal+ToUniversalTime treated it as UTC, so every
                // intraday bar was 4-5h off (silently WRONG data, not a blank chart).
                var wall = DateTime.Parse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None);
                return new Ohlcv(
                    AccessibleTrader.Sdk.Models.ExchangeTime.EasternWallClockToUtc(wall),
                    t["open"]?.Value<double>() ?? 0,
                    t["high"]?.Value<double>() ?? 0,
                    t["low"]?.Value<double>() ?? 0,
                    t["close"]?.Value<double>() ?? 0,
                    t["volume"]?.Value<double>() ?? 0);
            }
            catch { return null; }
        }

        private static string MapTimeframe(string tf) => tf switch
        {
            "1m" => "1min",
            "5m" => "5min",
            "15m" => "15min",
            "30m" => "30min",
            "1h" => "1hour",
            "4h" => "4hour",
            _ => "1hour"
        };

        /// <summary>
        /// Several /stable endpoints — the full stock and ETF lists, intraday charts — are gated by
        /// FMP subscription tier and answer 402. Swallowing that produced an empty dropdown, which
        /// reads as "this market has no symbols" rather than "your plan does not include it". The
        /// difference matters: one is a bug report, the other is a purchase decision.
        /// </summary>
        private async Task<JArray?> FetchJsonAsync(string url)
        {
            try
            {
                var response = await _httpClient!.GetAsync(url).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if ((int)response.StatusCode == 402)
                {
                    _errorStream.OnNext("FMP: your plan does not include this data set. The free tier covers quotes, "
                                      + "daily prices and the crypto/forex/commodity/index lists; full stock and ETF "
                                      + "lists and intraday charts need a paid plan.");
                    return null;
                }
                if ((int)response.StatusCode == 403 && body.Contains("Legacy", StringComparison.OrdinalIgnoreCase))
                {
                    _errorStream.OnNext("FMP refused a request as a legacy endpoint — the app is calling a retired API path. "
                                      + "This is a bug; please report it.");
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    _errorStream.OnNext($"FMP request failed: HTTP {(int)response.StatusCode}.");
                    return null;
                }
                return JArray.Parse(body);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"FMP request error: {ex.Message}");
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
