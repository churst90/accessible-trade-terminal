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

namespace AccessibleTrader.Plugins.CoinMetrics
{
    /// <summary>
    /// CoinMetrics Community API provider — free, no auth, daily on-chain metrics for
    /// BTC, ETH, LTC, DOGE, ADA, XRP, DOT, LINK, UNI.
    ///
    /// Exposes MVRV ratio, active addresses, hash rate, transaction counts, exchange
    /// flows, supply data, fees, price, and 30-day ROI across nine assets. Symbols
    /// follow the pattern {ASSET}_{METRIC} (e.g. "BTC_MVRV", "ETH_ACTIVE_ADDR").
    ///
    /// -- Strategy use ----------------------------------------------------------------
    ///   - BTC_MVRV below 1.0 = market-cap below realized-cap, historically a strong
    ///     accumulation signal when combined with price-based oversold.
    ///   - Rising BTC_ACTIVE_ADDR at price lows = healthy network usage diverging from
    ///     price — bullish.
    ///   - BTC_EXCHANGE_INFLOW spikes often precede sell pressure; OUTFLOW spikes
    ///     suggest accumulation / cold-storage moves.
    ///
    /// -- API -------------------------------------------------------------------------
    /// Base: https://community-api.coinmetrics.io/v4
    /// Endpoint: GET /timeseries/asset-metrics?assets={asset}&amp;metrics={metrics}&amp;frequency=1d
    /// No API key required for community tier. Rate limit ~10 req/6s.
    /// </summary>
    public class CoinMetricsProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _http = new();
        private readonly RateLimiter _rateLimiter = new(10, TimeSpan.FromSeconds(6));

        private const string BaseUrl = "https://community-api.coinmetrics.io/v4";

        // ── Supported assets (community tier) ────────────────────────────────────
        private static readonly string[] SupportedAssets =
            { "btc", "eth", "ltc", "doge", "ada", "xrp", "dot", "link", "uni" };

        // ── Metric definitions ───────────────────────────────────────────────────
        // Each tuple: (symbol suffix, CoinMetrics API metric name, display label)
        private static readonly (string Suffix, string ApiMetric, string Label)[] MetricDefs =
        {
            ("MVRV",             "CapMVRVCur",     "MVRV Ratio"),
            ("ACTIVE_ADDR",      "AdrActCnt",      "Active Addresses"),
            ("HASH_RATE",        "HashRate",        "Hash Rate"),
            ("TX_COUNT",         "TxCnt",           "Transaction Count"),
            ("TRANSFER_COUNT",   "TxTfrCnt",        "Transfer Count"),
            ("MARKET_CAP",       "CapMrktCurUSD",   "Market Cap USD"),
            ("EXCHANGE_INFLOW",  "FlowInExUSD",     "Exchange Inflow USD"),
            ("EXCHANGE_OUTFLOW", "FlowOutExUSD",    "Exchange Outflow USD"),
            ("SUPPLY",           "SplyCur",         "Current Supply"),
            ("EXCHANGE_SUPPLY",  "SplyExUSD",       "Exchange Supply USD"),
            ("FEES",             "FeeTotNtv",       "Total Fees (Native)"),
            ("PRICE",            "PriceUSD",        "Price USD"),
            ("ROI_30D",          "ROI30d",          "30-Day ROI"),
        };

        // Symbol → (asset, API metric) quick lookup, built once.
        private static readonly Dictionary<string, (string Asset, string ApiMetric)> SymbolMap;
        // Symbol → display label quick lookup, built once.
        private static readonly Dictionary<string, string> DisplayMap;
        // Pre-built ordered symbol list (BTC first, then ETH, then rest alphabetical).
        private static readonly List<string> OrderedSymbols;

        static CoinMetricsProvider()
        {
            SymbolMap = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            DisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var btcSymbols = new List<string>();
            var ethSymbols = new List<string>();
            var otherSymbols = new List<string>();

            foreach (var asset in SupportedAssets)
            {
                var assetUpper = asset.ToUpperInvariant();
                foreach (var (suffix, apiMetric, label) in MetricDefs)
                {
                    var symbol = $"{assetUpper}_{suffix}";
                    SymbolMap[symbol] = (asset, apiMetric);
                    DisplayMap[symbol] = $"{assetUpper} \u2014 {label}";

                    if (asset == "btc") btcSymbols.Add(symbol);
                    else if (asset == "eth") ethSymbols.Add(symbol);
                    else otherSymbols.Add(symbol);
                }
            }

            otherSymbols.Sort(StringComparer.OrdinalIgnoreCase);
            OrderedSymbols = new List<string>(btcSymbols.Count + ethSymbols.Count + otherSymbols.Count);
            OrderedSymbols.AddRange(btcSymbols);
            OrderedSymbols.AddRange(ethSymbols);
            OrderedSymbols.AddRange(otherSymbols);
        }

        // ── Provider metadata ────────────────────────────────────────────────────
        public override string Name        => "CoinMetrics";
        public override string Description => "CoinMetrics Community \u2014 MVRV, active addresses, hash rate, exchange flows for BTC/ETH/LTC/DOGE/ADA/XRP/DOT/LINK/UNI";
        public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 10000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override List<string> NativelySupportedTimeframes => new()
        {
            StandardTimeframes.OneDay
        };

        // ── Display names ────────────────────────────────────────────────────────
        public override string GetSymbolDisplayName(string symbol)
        {
            if (DisplayMap.TryGetValue(symbol, out var name)) return name;
            return symbol;
        }

        // ── Render hints ─────────────────────────────────────────────────────────
        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            if (!SymbolMap.TryGetValue(symbol, out var entry)) return null;

            // MVRV: bounded at 0, reference at 1.0 (realized-cap break-even)
            if (entry.ApiMetric == "CapMVRVCur")
            {
                return new SymbolRenderHints(
                    RangeMin: 0,
                    ReferenceLevels: new[]
                    {
                        new LevelDescriptor(
                            Name: "Realized Cap Break-Even", Value: 1.0,
                            ColorHex: "#FFD54F", Dash: DashStyle.Dash)
                    });
            }

            // ROI 30d: reference at 0 (break-even)
            if (entry.ApiMetric == "ROI30d")
            {
                return new SymbolRenderHints(
                    ReferenceLevels: new[]
                    {
                        new LevelDescriptor(
                            Name: "Break-Even", Value: 0,
                            ColorHex: "#888888", Dash: DashStyle.Dash)
                    });
            }

            return null;
        }

        // ── Configuration (no-op, no API key needed) ─────────────────────────────
        public override void Configure(Dictionary<string, string> config) { }

        public override Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;

        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
            => Task.FromResult(new List<string>(OrderedSymbols));

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        // ── Data fetch ───────────────────────────────────────────────────────────
        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var symbol = request.Symbol ?? string.Empty;
            if (!SymbolMap.TryGetValue(symbol, out var entry))
            {
                _errorStream.OnNext($"CoinMetrics unknown symbol: {symbol}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var allBars = new List<Ohlcv>();
                    string? pageToken = null;
                    int pagesRead = 0;
                    const int maxPages = 3;

                    // Convert Since/Until from unix ms to ISO date strings
                    string? startTime = request.Since.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : null;
                    string? endTime = request.Until.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : null;

                    do
                    {
                        var url = $"{BaseUrl}/timeseries/asset-metrics?assets={entry.Asset}&metrics={entry.ApiMetric}&frequency=1d&page_size=10000";
                        if (startTime != null) url += $"&start_time={startTime}";
                        if (endTime != null) url += $"&end_time={endTime}";
                        if (pageToken != null) url += $"&next_page_token={pageToken}";

                        var json = await _http.GetStringAsync(url).ConfigureAwait(false);
                        var root = JObject.Parse(json);
                        var dataArr = root["data"] as JArray;
                        if (dataArr == null || dataArr.Count == 0) break;

                        foreach (var pt in dataArr)
                        {
                            var timeStr = pt["time"]?.ToString();
                            if (timeStr == null) continue;

                            if (!DateTimeOffset.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                                continue;

                            // Values come as strings in the JSON response
                            var valStr = pt[entry.ApiMetric]?.ToString();
                            if (valStr == null) continue;
                            if (!double.TryParse(valStr, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var val))
                                continue;
                            if (double.IsNaN(val)) continue;

                            var date = dto.UtcDateTime;
                            allBars.Add(new Ohlcv(date, val, val, val, val, 0));
                        }

                        // Handle pagination
                        pageToken = root["next_page_token"]?.ToString();
                        pagesRead++;
                    }
                    while (!string.IsNullOrEmpty(pageToken) && pagesRead < maxPages);

                    // Ensure chronological order
                    allBars.Sort((a, b) => a.Date.CompareTo(b.Date));

                    var vols = allBars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                    return (allBars, vols);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"CoinMetrics fetch error ({symbol}): {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        // ── Disposal ─────────────────────────────────────────────────────────────
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _http?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
