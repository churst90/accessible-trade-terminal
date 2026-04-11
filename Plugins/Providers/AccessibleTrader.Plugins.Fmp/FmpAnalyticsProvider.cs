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
    /// Financial Modeling Prep Analytics — fundamentals, ratios, earnings, economic data as time series.
    /// Exposes quarterly/annual financial data as SingleValueLine charts so they can be overlaid
    /// on price charts or used as strategy condition inputs.
    ///
    /// Sub-types organize the data:
    ///   Key Metrics    — PE, PB, EV/EBITDA, dividend yield, ROE, market cap, etc.
    ///   Income         — Revenue, net income, gross profit, operating income, EPS
    ///   Ratios         — Current ratio, debt/equity, interest coverage, profit margins
    ///   Earnings       — Actual vs estimated EPS, revenue surprise
    ///   Sector Perf    — Sector returns (Technology, Healthcare, Energy, etc.)
    ///   Economic       — Economic calendar events (GDP, CPI, unemployment, etc.)
    ///
    /// Symbols for financial sub-types use "{TICKER}_{METRIC}" format, e.g. "AAPL_REVENUE".
    /// The provider parses the ticker and metric, fetches the appropriate endpoint,
    /// and returns a time series where each point = one reporting period.
    /// </summary>
    public class FmpAnalyticsProvider : BaseMarketDataProvider
    {
        private const string BaseUrl = "https://financialmodelingprep.com/api/v3";
        private const string BaseUrlV4 = "https://financialmodelingprep.com/api/v4";
        private HttpClient? _httpClient;
        private string _apiKey = "";
        private readonly RateLimiter _rateLimiter = new(4, TimeSpan.FromMinutes(1));

        public override string Name => "FMP Analytics";
        public override string Description => "FMP fundamentals, ratios, earnings, and economic data";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Economic };
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest => 500;
        public override List<string> NativelySupportedTimeframes => new() { "1d" };

        // ── Metric definitions ───────────────────────────────────────────────

        private static readonly string[] PopularTickers = {
            "AAPL", "MSFT", "GOOG", "AMZN", "NVDA", "META", "TSLA", "BRK-B",
            "JPM", "V", "JNJ", "WMT", "PG", "MA", "UNH", "HD", "DIS", "BAC",
            "XOM", "PFE", "KO", "PEP", "CSCO", "INTC", "AMD", "NFLX", "CRM",
            "ORCL", "ADBE", "AVGO", "COST", "MRK", "ABT", "TMO", "NKE",
            "COIN", "MSTR", "SQ", "PYPL", "RIVN", "PLTR", "SOFI"
        };

        private static readonly Dictionary<string, MetricDef> KeyMetrics = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PE"]              = new("peRatio",            "key-metrics", "P/E Ratio"),
            ["PB"]              = new("pbRatio",            "key-metrics", "P/B Ratio"),
            ["PS"]              = new("priceToSalesRatio",  "key-metrics", "P/S Ratio"),
            ["EV_EBITDA"]       = new("evToOperatingCashFlow", "key-metrics", "EV/Operating CF"),
            ["DIVIDEND_YIELD"]  = new("dividendYield",      "key-metrics", "Dividend Yield"),
            ["ROE"]             = new("roe",                "key-metrics", "Return on Equity"),
            ["ROA"]             = new("roa",                "key-metrics", "Return on Assets"),
            ["ROIC"]            = new("roic",               "key-metrics", "Return on Invested Capital"),
            ["MARKET_CAP"]      = new("marketCap",          "key-metrics", "Market Cap"),
            ["EV"]              = new("enterpriseValue",    "key-metrics", "Enterprise Value"),
            ["DEBT_EQUITY"]     = new("debtToEquity",       "key-metrics", "Debt/Equity"),
            ["CURRENT_RATIO"]   = new("currentRatio",       "key-metrics", "Current Ratio"),
            ["FCF_YIELD"]       = new("freeCashFlowYield",  "key-metrics", "Free Cash Flow Yield"),
            ["EARNINGS_YIELD"]  = new("earningsYield",      "key-metrics", "Earnings Yield"),
            ["BOOK_VALUE"]      = new("bookValuePerShare",  "key-metrics", "Book Value/Share"),
        };

        private static readonly Dictionary<string, MetricDef> IncomeMetrics = new(StringComparer.OrdinalIgnoreCase)
        {
            ["REVENUE"]         = new("revenue",            "income-statement", "Revenue"),
            ["NET_INCOME"]      = new("netIncome",          "income-statement", "Net Income"),
            ["GROSS_PROFIT"]    = new("grossProfit",        "income-statement", "Gross Profit"),
            ["OPERATING_INCOME"]= new("operatingIncome",    "income-statement", "Operating Income"),
            ["EPS"]             = new("epsdiluted",         "income-statement", "EPS (Diluted)"),
            ["EBITDA"]          = new("ebitda",             "income-statement", "EBITDA"),
            ["COST_OF_REVENUE"] = new("costOfRevenue",      "income-statement", "Cost of Revenue"),
            ["RD_EXPENSES"]     = new("researchAndDevelopmentExpenses", "income-statement", "R&D Expenses"),
            ["GROSS_MARGIN"]    = new("grossProfitRatio",   "income-statement", "Gross Margin"),
            ["OPERATING_MARGIN"]= new("operatingIncomeRatio","income-statement", "Operating Margin"),
            ["NET_MARGIN"]      = new("netIncomeRatio",     "income-statement", "Net Margin"),
        };

        private static readonly Dictionary<string, MetricDef> RatioMetrics = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PROFIT_MARGIN"]     = new("netProfitMargin",        "ratios", "Net Profit Margin"),
            ["ASSET_TURNOVER"]    = new("assetTurnover",          "ratios", "Asset Turnover"),
            ["INVENTORY_TURNOVER"]= new("inventoryTurnover",      "ratios", "Inventory Turnover"),
            ["RECEIVABLES_TURNOV"]= new("receivablesTurnover",    "ratios", "Receivables Turnover"),
            ["INTEREST_COVERAGE"] = new("interestCoverage",       "ratios", "Interest Coverage"),
            ["DEBT_RATIO"]        = new("debtRatio",              "ratios", "Debt Ratio"),
            ["PAYOUT_RATIO"]      = new("payoutRatio",            "ratios", "Payout Ratio"),
            ["CASH_RATIO"]        = new("cashRatio",              "ratios", "Cash Ratio"),
            ["QUICK_RATIO"]       = new("quickRatio",             "ratios", "Quick Ratio"),
            ["PRICE_FAIR_VALUE"]  = new("priceFairValue",         "ratios", "Price/Fair Value"),
        };

        private static readonly string[] SectorNames = {
            "Technology", "Healthcare", "Financial Services", "Consumer Cyclical",
            "Communication Services", "Industrials", "Consumer Defensive",
            "Energy", "Basic Materials", "Real Estate", "Utilities"
        };

        // ── Configuration ────────────────────────────────────────────────────

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key) && !string.IsNullOrWhiteSpace(key))
            {
                _apiKey = key;
                _httpClient = new HttpClient();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "AccessibleTrader/1.0");
            }
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured.");
            try
            {
                var url = $"{BaseUrl}/key-metrics-ttm/AAPL?apikey={_apiKey}";
                var response = await _httpClient!.GetAsync(url).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? (true, "FMP Analytics API key validated.")
                    : (false, $"Validation failed: HTTP {(int)response.StatusCode}");
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

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string>
            {
                "Key Metrics", "Income", "Ratios",
                "Earnings", "Sector Perf", "Economic"
            });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1d" });

        // ── Symbol listing ───────────────────────────────────────────────────

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            var symbols = subType switch
            {
                "Key Metrics"  => ExpandSymbols(PopularTickers, KeyMetrics),
                "Income"       => ExpandSymbols(PopularTickers, IncomeMetrics),
                "Ratios"       => ExpandSymbols(PopularTickers, RatioMetrics),
                "Earnings"     => PopularTickers.Select(t => $"{t}_EARNINGS").ToList(),
                "Sector Perf"  => SectorNames.ToList(),
                "Economic"     => new List<string>
                {
                    "EARNINGS_CALENDAR", "ECONOMIC_CALENDAR",
                    "IPO_CALENDAR", "DIVIDEND_CALENDAR", "SPLIT_CALENDAR"
                },
                _ => new List<string>()
            };
            return Task.FromResult(symbols);
        }

        private static List<string> ExpandSymbols(string[] tickers, Dictionary<string, MetricDef> metrics) =>
            tickers.SelectMany(t => metrics.Keys.Select(m => $"{t}_{m}")).ToList();

        // ── Display names ────────────────────────────────────────────────────

        public override string GetSymbolDisplayName(string symbol)
        {
            if (SectorNames.Contains(symbol)) return $"Sector: {symbol}";
            if (symbol.Contains('_'))
            {
                var parts = symbol.Split('_', 2);
                var ticker = parts[0];
                var metricKey = parts[1];

                string? label = null;
                if (KeyMetrics.TryGetValue(metricKey, out var km)) label = km.Label;
                else if (IncomeMetrics.TryGetValue(metricKey, out var im)) label = im.Label;
                else if (RatioMetrics.TryGetValue(metricKey, out var rm)) label = rm.Label;
                else if (metricKey == "EARNINGS") label = "Earnings (EPS)";

                return label != null ? $"{ticker} — {label}" : symbol;
            }
            return symbol;
        }

        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            if (symbol.EndsWith("_PE", StringComparison.OrdinalIgnoreCase) ||
                symbol.EndsWith("_PB", StringComparison.OrdinalIgnoreCase) ||
                symbol.EndsWith("_PS", StringComparison.OrdinalIgnoreCase))
            {
                return new SymbolRenderHints(RangeMin: 0);
            }
            if (symbol.EndsWith("_ROE", StringComparison.OrdinalIgnoreCase) ||
                symbol.EndsWith("_ROA", StringComparison.OrdinalIgnoreCase) ||
                symbol.EndsWith("_GROSS_MARGIN", StringComparison.OrdinalIgnoreCase) ||
                symbol.EndsWith("_NET_MARGIN", StringComparison.OrdinalIgnoreCase) ||
                symbol.EndsWith("_OPERATING_MARGIN", StringComparison.OrdinalIgnoreCase))
            {
                return new SymbolRenderHints(RangeMin: -1.0, RangeMax: 1.0,
                    ReferenceLevels: new[] { new LevelDescriptor("Zero", 0, "#666666", DashStyle.Dash) });
            }
            return null;
        }

        // ── Data fetch ───────────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new(), new());

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var bars = await FetchMetricAsync(request.Symbol, request.Limit).ConfigureAwait(false);
                    var volume = bars.Select(b =>
                        (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), 0.0)).ToList();
                    return (bars, volume);
                }).ConfigureAwait(false);
            }
            catch { return (new(), new()); }
        }

        private async Task<List<Ohlcv>> FetchMetricAsync(string symbol, int limit)
        {
            // Sector performance
            if (SectorNames.Contains(symbol))
                return await FetchSectorPerformanceAsync(symbol).ConfigureAwait(false);

            // Calendar endpoints
            if (symbol.StartsWith("EARNINGS_CALENDAR", StringComparison.OrdinalIgnoreCase))
                return await FetchEarningsCalendarAsync(limit).ConfigureAwait(false);
            if (symbol.StartsWith("ECONOMIC_CALENDAR", StringComparison.OrdinalIgnoreCase))
                return await FetchEconomicCalendarAsync(limit).ConfigureAwait(false);
            if (symbol.StartsWith("IPO_CALENDAR", StringComparison.OrdinalIgnoreCase))
                return await FetchCalendarAsync("ipo_calendar", limit).ConfigureAwait(false);
            if (symbol.StartsWith("DIVIDEND_CALENDAR", StringComparison.OrdinalIgnoreCase))
                return await FetchCalendarAsync("stock_dividend_calendar", limit).ConfigureAwait(false);
            if (symbol.StartsWith("SPLIT_CALENDAR", StringComparison.OrdinalIgnoreCase))
                return await FetchCalendarAsync("stock_split_calendar", limit).ConfigureAwait(false);

            // Compound symbol: TICKER_METRIC
            var idx = symbol.IndexOf('_');
            if (idx < 1) return new List<Ohlcv>();

            var ticker = symbol[..idx];
            var metricKey = symbol[(idx + 1)..];

            // Earnings (actual vs estimated EPS)
            if (metricKey.Equals("EARNINGS", StringComparison.OrdinalIgnoreCase))
                return await FetchEarningsSurprisesAsync(ticker, limit).ConfigureAwait(false);

            // Find the metric definition
            MetricDef? def = null;
            if (KeyMetrics.TryGetValue(metricKey, out var km)) def = km;
            else if (IncomeMetrics.TryGetValue(metricKey, out var im)) def = im;
            else if (RatioMetrics.TryGetValue(metricKey, out var rm)) def = rm;

            if (def == null) return new List<Ohlcv>();

            return await FetchFinancialMetricAsync(ticker, def, limit).ConfigureAwait(false);
        }

        private async Task<List<Ohlcv>> FetchFinancialMetricAsync(string ticker, MetricDef def, int limit)
        {
            var url = $"{BaseUrl}/{def.Endpoint}/{ticker}?period=quarter&limit={limit}&apikey={_apiKey}";
            var body = await _httpClient!.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(body);

            return arr
                .Select(t =>
                {
                    var dateStr = t["date"]?.ToString();
                    if (string.IsNullOrEmpty(dateStr)) return (Ohlcv?)null;
                    if (!DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                        return null;
                    var val = t[def.Field]?.Value<double>() ?? double.NaN;
                    if (double.IsInfinity(val)) val = double.NaN;
                    return (Ohlcv?)new Ohlcv(date.ToUniversalTime(), val, val, val, val, 0);
                })
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => b.Date)
                .ToList();
        }

        private async Task<List<Ohlcv>> FetchEarningsSurprisesAsync(string ticker, int limit)
        {
            var url = $"{BaseUrl}/earnings-surprises/{ticker}?apikey={_apiKey}";
            var body = await _httpClient!.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(body);

            return arr
                .Select(t =>
                {
                    var dateStr = t["date"]?.ToString();
                    if (string.IsNullOrEmpty(dateStr)) return (Ohlcv?)null;
                    if (!DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                        return null;
                    var actual = t["actualEarningResult"]?.Value<double>() ?? double.NaN;
                    var estimated = t["estimatedEarning"]?.Value<double>() ?? double.NaN;
                    // Close = actual EPS, Open = estimated EPS (so the "candle" shows beat/miss)
                    return (Ohlcv?)new Ohlcv(date.ToUniversalTime(), estimated, actual, estimated, actual, 0);
                })
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => b.Date)
                .Take(limit)
                .ToList();
        }

        private async Task<List<Ohlcv>> FetchSectorPerformanceAsync(string sector)
        {
            var url = $"{BaseUrl}/historical-sectors-performance?limit=365&apikey={_apiKey}";
            var body = await _httpClient!.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(body);

            // Field name in response: sector name with "ChangesPercentage" suffix
            // The API returns all sectors per date — we pick the one matching our sector
            var fieldName = $"{sector.Replace(" ", "")}ChangesPercentage";

            return arr
                .Select(t =>
                {
                    var dateStr = t["date"]?.ToString();
                    if (string.IsNullOrEmpty(dateStr)) return (Ohlcv?)null;
                    if (!DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                        return null;

                    // Try exact field name, fall back to searching
                    var val = t[fieldName]?.Value<double>() ?? double.NaN;
                    if (double.IsNaN(val))
                    {
                        // Try case-insensitive property search
                        var prop = (t as JObject)?.Properties()
                            .FirstOrDefault(p => p.Name.Contains(sector.Split(' ')[0], StringComparison.OrdinalIgnoreCase)
                                              && p.Name.Contains("Changes", StringComparison.OrdinalIgnoreCase));
                        if (prop != null) val = prop.Value.Value<double>();
                    }

                    return (Ohlcv?)new Ohlcv(date.ToUniversalTime(), val, val, val, val, 0);
                })
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => b.Date)
                .ToList();
        }

        private async Task<List<Ohlcv>> FetchEarningsCalendarAsync(int limit)
        {
            var to = DateTime.UtcNow.AddMonths(3).ToString("yyyy-MM-dd");
            var from = DateTime.UtcNow.AddMonths(-3).ToString("yyyy-MM-dd");
            var url = $"{BaseUrl}/earning_calendar?from={from}&to={to}&apikey={_apiKey}";
            var body = await _httpClient!.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(body);

            // Aggregate: count of earnings reports per day
            return arr
                .GroupBy(t => t["date"]?.ToString() ?? "")
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g =>
                {
                    if (!DateTime.TryParse(g.Key, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                        return (Ohlcv?)null;
                    double count = g.Count();
                    return (Ohlcv?)new Ohlcv(date.ToUniversalTime(), count, count, count, count, 0);
                })
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => b.Date)
                .Take(limit)
                .ToList();
        }

        private async Task<List<Ohlcv>> FetchEconomicCalendarAsync(int limit)
        {
            var to = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd");
            var from = DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-dd");
            var url = $"{BaseUrl}/economic_calendar?from={from}&to={to}&apikey={_apiKey}";
            var body = await _httpClient!.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(body);

            return arr
                .Select(t =>
                {
                    var dateStr = t["date"]?.ToString();
                    if (string.IsNullOrEmpty(dateStr)) return (Ohlcv?)null;
                    if (!DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                        return null;
                    var actual = t["actual"]?.Value<double>() ?? double.NaN;
                    return (Ohlcv?)new Ohlcv(date.ToUniversalTime(), actual, actual, actual, actual, 0);
                })
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => b.Date)
                .Take(limit)
                .ToList();
        }

        private async Task<List<Ohlcv>> FetchCalendarAsync(string endpoint, int limit)
        {
            var to = DateTime.UtcNow.AddMonths(3).ToString("yyyy-MM-dd");
            var from = DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-dd");
            var url = $"{BaseUrl}/{endpoint}?from={from}&to={to}&apikey={_apiKey}";
            var body = await _httpClient!.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(body);

            return arr
                .GroupBy(t => t["date"]?.ToString() ?? "")
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g =>
                {
                    if (!DateTime.TryParse(g.Key, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                        return (Ohlcv?)null;
                    double count = g.Count();
                    return (Ohlcv?)new Ohlcv(date.ToUniversalTime(), count, count, count, count, 0);
                })
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .OrderBy(b => b.Date)
                .Take(limit)
                .ToList();
        }

        // ── Order book (not supported) ───────────────────────────────────────

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
            => Task.FromResult<(List<OrderBookEntry>, List<OrderBookEntry>)>((new(), new()));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
            }
            base.Dispose(disposing);
        }

        // ── Internal types ───────────────────────────────────────────────────

        private record MetricDef(string Field, string Endpoint, string Label);
    }
}
