using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;
using System.Reactive.Subjects;

namespace AccessibleTrader.Plugins.Fred
{
    public class FredProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private const string BaseUrl = "https://api.stlouisfed.org/fred";

        // Rate limiter: FRED allows ~120 requests/minute
        private readonly RateLimiter _rateLimiter = new(120, TimeSpan.FromMinutes(1));

        // Cache of popular series for quick access
        private static readonly List<string> PopularSeries = new()
        {
            "GDP", "GDPC1", "CPIAUCSL", "CPILFESL", "UNRATE", "PAYEMS",
            "FEDFUNDS", "DFF", "DGS10", "DGS2", "T10Y2Y", "T10YIE",
            "DCOILWTICO", "GOLDAMGBD228NLBM", "DEXUSEU", "DTWEXBGS",
            "M2SL", "WALCL", "MORTGAGE30US", "CSUSHPINSA",
            "UMCSENT", "VIXCLS", "BAMLH0A0HYM2", "SP500",
            "INDPRO", "RSAFS", "PCE", "PCEPI", "HOUST", "PERMIT"
        };

        public override string Name => "FRED";
        public override string Description => "Federal Reserve Economic Data";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Economic };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && _apiKey != "demo";
        public override bool SupportsLiveUpdates => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest => 100000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        // Human-readable labels for common FRED series ids so the Price series reads
        // "M2 Money Supply" instead of "M2SL" in speech/UI. Unknown ids pass through.
        public override string GetSymbolDisplayName(string symbol) => symbol switch
        {
            "DGS10"         => "10-Year Treasury Rate",
            "DGS2"          => "2-Year Treasury Rate",
            "DFF"           => "Federal Funds Rate",
            "CPIAUCSL"      => "Consumer Price Index",
            "UNRATE"        => "Unemployment Rate",
            "DCOILWTICO"    => "WTI Crude Oil Price",
            "GOLDAMGBD228NLBM" => "Gold Price",
            "DEXUSEU"       => "USD/EUR Exchange Rate",
            "DTWEXBGS"      => "Dollar Index (Broad)",
            "M2SL"          => "M2 Money Supply",
            "WALCL"         => "Fed Balance Sheet",
            "MORTGAGE30US"  => "30-Year Mortgage Rate",
            "CSUSHPINSA"    => "Case-Shiller Home Price Index",
            "UMCSENT"       => "Consumer Sentiment",
            "VIXCLS"        => "VIX (Volatility Index)",
            "BAMLH0A0HYM2"  => "High Yield Spread",
            "SP500"         => "S&P 500",
            "INDPRO"        => "Industrial Production",
            "RSAFS"         => "Retail Sales",
            "PCE"           => "Personal Consumption Expenditures",
            "PCEPI"         => "PCE Price Index",
            "HOUST"         => "Housing Starts",
            "PERMIT"        => "Building Permits",
            _               => symbol
        };

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneDay, StandardTimeframes.OneWeek,
            StandardTimeframes.OneMinute, StandardTimeframes.ThreeMinutes
        };

        public FredProvider()
        {
            _httpClient = new HttpClient();
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
                var response = await _httpClient.GetAsync($"{BaseUrl}/series?series_id=GDP&api_key={_apiKey}&file_type=json");
                if (response.IsSuccessStatusCode)
                    return (true, "API key validated successfully");
                return (false, $"Key validation failed ({response.StatusCode})");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        public override Task EnsureConnectedAsync()
        {
            if (IsConfigured) _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;

        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            var frequency = MapFrequency(request.Timeframe);
            string url = $"{BaseUrl}/series/observations?series_id={request.Symbol}&api_key={_apiKey}&file_type=json";
            if (!string.IsNullOrEmpty(frequency)) url += $"&frequency={frequency}";
            if (request.Since.HasValue)
                url += $"&observation_start={DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime:yyyy-MM-dd}";
            if (request.Until.HasValue)
                url += $"&observation_end={DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime:yyyy-MM-dd}";

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var observations = JObject.Parse(response)["observations"] as JArray;
                    if (observations == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcvList = observations
                        .Where(o => o["value"]?.ToString() != ".")  // FRED uses "." for missing data
                        .Select(o =>
                        {
                            double.TryParse(o["value"]?.ToString(), out var val);
                            return new Ohlcv(
                                DateTime.SpecifyKind(DateTime.ParseExact(o["date"]?.ToString() ?? "0001-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc),
                                val, val, val, val, 0);
                        }).ToList();

                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"FRED fetch error: {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            if (!IsConfigured) return PopularSeries;
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    // Search for popular economic series via the FRED API
                    var allSymbols = new List<string>(PopularSeries);

                    // Fetch additional popular series from multiple categories
                    var categories = new[] { "32992", "32455", "33060", "32263" }; // GDP, Employment, Prices, Interest Rates
                    foreach (var catId in categories)
                    {
                        try
                        {
                            string url = $"{BaseUrl}/category/series?category_id={catId}&api_key={_apiKey}&file_type=json&limit=50&order_by=popularity&sort_order=desc";
                            var response = await _httpClient.GetStringAsync(url);
                            var serieses = JObject.Parse(response)["seriess"] as JArray;
                            if (serieses != null)
                            {
                                var ids = serieses.Select(s => s["id"]?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s));
                                allSymbols.AddRange(ids);
                            }
                        }
                        catch { /* some categories may fail, continue */ }
                    }

                    return allSymbols.Distinct().OrderBy(s => s).ToList();
                });
            }
            catch
            {
                return PopularSeries;
            }
        }

        /// <summary>
        /// Searches FRED for series matching a search term.
        /// Called when the user types a search query in the symbol picker.
        /// </summary>
        public async Task<List<(string Id, string Title)>> SearchSeriesAsync(string searchText, int limit = 50)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(searchText)) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string url = $"{BaseUrl}/series/search?search_text={Uri.EscapeDataString(searchText)}&api_key={_apiKey}&file_type=json&limit={limit}&order_by=search_rank";
                    var response = await _httpClient.GetStringAsync(url);
                    var serieses = JObject.Parse(response)["seriess"] as JArray;
                    if (serieses == null) return new List<(string, string)>();

                    return serieses.Select(s => (
                        s["id"]?.ToString() ?? "",
                        s["title"]?.ToString() ?? ""
                    )).Where(x => !string.IsNullOrEmpty(x.Item1)).ToList();
                });
            }
            catch { return new(); }
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string> { "Standard" });
        public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string> { "1d", "1w", "1m", "3m", "1y" });
        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        private string MapFrequency(string tf) => tf.ToLower() switch
        {
            "1d" => "d",
            "1w" => "w",
            "1m" => "m",
            "3m" => "q",
            "1y" => "a",
            _    => ""
        };
    }
}
