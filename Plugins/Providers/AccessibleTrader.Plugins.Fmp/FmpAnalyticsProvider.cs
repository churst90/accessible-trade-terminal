using System.Globalization;
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
        // MIGRATED 2026-08-02 from /api/v3 and /api/v4, both of which FMP retired: they answer
        // 403 "Legacy Endpoint" for any key without a subscription predating 2025-08-31. On /stable
        // the ticker moves from the path into a `symbol` query parameter. Several of these data sets
        // are also plan-gated and answer 402 — see FetchArrayAsync, which says so out loud rather
        // than returning an empty series that reads as "this metric has no data".
        private const string BaseUrl = "https://financialmodelingprep.com/stable";
        private HttpClient? _httpClient;
        private string _apiKey = "";

        /// <summary>
        /// The API key, escaped for use as a query-string VALUE.
        ///
        /// <para>A raw interpolation mangles any key that is not already URL-safe: <c>&amp;</c>
        /// ends the parameter and starts a new one (the key is TRUNCATED at the ampersand),
        /// <c>+</c> decodes to a space at the server, <c>#</c> throws the rest of the URL away
        /// as a fragment. All three are legal in a generated credential, and the user is then
        /// told "validation failed" about a key they pasted correctly. <c>FredProvider</c> was
        /// the only provider that escaped its key; the name is what
        /// <c>ApiKeyUrlEscapingTests</c> requires at every key-bearing query site.</para>
        /// </summary>
        private string KeyParam => Uri.EscapeDataString(_apiKey);
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
                // Phase-4 Track B2 parity with FmpProvider — allow-listed to
                // financialmodelingprep.com, 32 MB response cap, 60 s timeout,
                // default User-Agent set by the factory.
                _httpClient = PluginHostServices.CreateHttpClient(
                    providerId:   "FmpAnalytics",
                    allowedHosts: new[] { "financialmodelingprep.com" });
            }
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured.");
            try
            {
                var url = $"{BaseUrl}/key-metrics-ttm?symbol=AAPL&apikey={KeyParam}";
                var response = await _httpClient!.GetAsync(url).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? (true, "FMP Analytics API key validated.")
                    : (false, $"Validation failed: HTTP {(int)response.StatusCode}");
            }
            // ?apikey=KEY on every request; HttpRequestException messages carry the URI.
            catch (Exception ex) { return (false, $"Validation error: {ex.GetType().Name}"); }
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
                return await FetchCalendarAsync("ipos-calendar", limit).ConfigureAwait(false);
            if (symbol.StartsWith("DIVIDEND_CALENDAR", StringComparison.OrdinalIgnoreCase))
                return await FetchCalendarAsync("dividends-calendar", limit).ConfigureAwait(false);
            if (symbol.StartsWith("SPLIT_CALENDAR", StringComparison.OrdinalIgnoreCase))
                return await FetchCalendarAsync("splits-calendar", limit).ConfigureAwait(false);

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
            var url = $"{BaseUrl}/{def.Endpoint}?symbol={Uri.EscapeDataString(ticker)}&period=quarter&limit={limit}&apikey={KeyParam}";
            var arr = await FetchArrayAsync(url).ConfigureAwait(false);
            if (arr == null) return new List<Ohlcv>();

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
            // /stable folds earnings-surprises into `earnings`, which carries both the reported and
            // the estimated EPS on the free tier.
            var url = $"{BaseUrl}/earnings?symbol={Uri.EscapeDataString(ticker)}&apikey={KeyParam}";
            var arr = await FetchArrayAsync(url).ConfigureAwait(false);
            if (arr == null) return new List<Ohlcv>();

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
            // /stable takes ONE sector and a date range and returns rows already scoped to it,
            // where v3 returned every sector per date in wide form.
            var to = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var from = DateTime.UtcNow.AddDays(-365).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = $"{BaseUrl}/historical-sector-performance?sector={Uri.EscapeDataString(sector)}&from={from}&to={to}&apikey={KeyParam}";
            var arr = await FetchArrayAsync(url).ConfigureAwait(false);
            if (arr == null) return new List<Ohlcv>();

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
            var to = DateTime.UtcNow.AddMonths(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var from = DateTime.UtcNow.AddMonths(-3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = $"{BaseUrl}/earnings-calendar?from={from}&to={to}&apikey={KeyParam}";
            var arr = await FetchArrayAsync(url).ConfigureAwait(false);
            if (arr == null) return new List<Ohlcv>();

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
            var to = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var from = DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = $"{BaseUrl}/economic-calendar?from={from}&to={to}&apikey={KeyParam}";
            var arr = await FetchArrayAsync(url).ConfigureAwait(false);
            if (arr == null) return new List<Ohlcv>();

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
            var to = DateTime.UtcNow.AddMonths(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var from = DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = $"{BaseUrl}/{endpoint}?from={from}&to={to}&apikey={KeyParam}";
            var arr = await FetchArrayAsync(url).ConfigureAwait(false);
            if (arr == null) return new List<Ohlcv>();

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

        /// <summary>
        /// One place where every /stable call lands, so a plan-gated data set reports itself instead
        /// of arriving as an empty series. On the free tier `key-metrics` and `ratios` refuse the
        /// quarterly period, the calendars refuse wide date ranges, and IPO data is unavailable —
        /// all of which used to surface as a blank chart with no explanation anywhere.
        /// </summary>
        private async Task<JArray?> FetchArrayAsync(string url)
        {
            try
            {
                var response = await _httpClient!.GetAsync(url).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if ((int)response.StatusCode == 402)
                {
                    _errorStream.OnNext("FMP Analytics: your plan does not include this data set (or this "
                                      + "period/date range). Free covers income statements, earnings, treasury "
                                      + "rates, economic indicators and sector performance.");
                    return null;
                }
                if ((int)response.StatusCode == 403 && body.Contains("Legacy", StringComparison.OrdinalIgnoreCase))
                {
                    _errorStream.OnNext("FMP Analytics called a retired API path — this is a bug; please report it.");
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    _errorStream.OnNext($"FMP Analytics request failed: HTTP {(int)response.StatusCode}.");
                    return null;
                }
                return JArray.Parse(body);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"FMP Analytics request error: {ex.GetType().Name}");
                return null;
            }
        }

        private record MetricDef(string Field, string Endpoint, string Label);
    }
}
