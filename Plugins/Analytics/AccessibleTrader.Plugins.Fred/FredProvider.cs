using System.Globalization;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// FRED publishes macro series at daily, weekly, monthly and quarterly frequencies.
        ///
        /// <para>This used to list <c>StandardTimeframes.OneMinute</c> and
        /// <c>ThreeMinutes</c> — which are <c>"1m"</c> and <c>"3m"</c>. The author plainly
        /// meant one MONTH and three MONTHS, but the month token is <c>"1M"</c>, and
        /// <c>MapFrequency</c> lower-cased its input so <c>"1m"</c> and <c>"1M"</c> collapsed
        /// onto the same branch. Selecting "1 minute" on a FRED chart returned MONTHLY
        /// observations labelled as minute bars, and because
        /// <c>TimeframeUtility.ToMilliseconds("1m")</c> returns 60000, the aggregation layer
        /// and <c>DataService.AnalyticsCacheTtl</c> both treated a monthly macro series as
        /// minute data — a 15-minute cache TTL instead of 12 hours.</para>
        /// </summary>
        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneDay, StandardTimeframes.OneWeek,
            OneMonthTf, ThreeMonthsTf
        };

        /// <summary>One month. <c>"1M"</c> — capital M — is the month token; <c>"1m"</c> is a
        /// minute.</summary>
        private const string OneMonthTf = "1M";

        /// <summary>Three months, i.e. FRED's quarterly frequency.</summary>
        private const string ThreeMonthsTf = "3M";

        public FredProvider()
        {
            // Host-provided HttpClient: 32 MB / 60 s + outbound allow-list.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId: "FRED",
                allowedHosts: new[] { "api.stlouisfed.org" });
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
                var response = await _httpClient.GetAsync($"{BaseUrl}/series?series_id=GDP&api_key={Uri.EscapeDataString(_apiKey ?? "")}&file_type=json");
                if (response.IsSuccessStatusCode)
                    return (true, "API key validated successfully");
                return (false, $"Key validation failed ({response.StatusCode})");
            }
            catch (Exception ex)
            {
                // Avoid surfacing ex.Message verbatim: on rare HttpClient code paths (proxy
                // errors, name-resolution failures) the exception can include the full
                // request URL, which for FRED's REST API embeds the api_key query param.
                // GetType().Name keeps the signal without leaking the key.
                return (false, $"Key validation error: {ex.GetType().Name}");
            }
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
            string url = BuildObservationsUrl(request, _apiKey, MapFrequency(request.Timeframe));

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var ohlcvList = ParseObservations(response);
                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch (Exception ex)
            {
                // Strip ex.Message — see ValidateApiKeyAsync for rationale (FRED api_key is
                // URL-embedded; HttpRequestException messages occasionally include the URL).
                _errorStream.OnNext($"FRED fetch error: {ex.GetType().Name}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        // FRED's documented "every vintage" window. 1776-07-04 is the API's own floor for
        // realtime_start and 9999-12-31 its ceiling for realtime_end.
        private const string RealtimeStart = "1776-07-04";
        private const string RealtimeEnd = "9999-12-31";

        /// <summary>
        /// The observations request.
        ///
        /// <para>
        /// <c>output_type=4</c> plus the full realtime window is the half of the point-in-time
        /// fix that lives in the REQUEST: it asks FRED for the initial release of each
        /// observation rather than the latest revision, and it is what makes
        /// <c>realtime_start</c> present on every returned row for
        /// <see cref="ParseObservations"/> to stamp with. Drop these parameters and the parser
        /// falls back to the period date — silently, and with the look-ahead bias restored.
        /// Extracted so a test can hold that, rather than leaving it to an HTTP fake.
        /// </para>
        /// </summary>
        internal static string BuildObservationsUrl(MarketDataRequest request, string? apiKey, string? frequency)
        {
            // Escape the user-supplied symbol so it cannot inject extra query params
            // (e.g. "GDP&api_key=attackerKey") into the FRED request URL.
            string seriesId = Uri.EscapeDataString(request.Symbol ?? "");
            string keyParam = Uri.EscapeDataString(apiKey ?? "");

            string url = $"{BaseUrl}/series/observations?series_id={seriesId}&api_key={keyParam}&file_type=json";
            url += $"&output_type=4&realtime_start={RealtimeStart}&realtime_end={RealtimeEnd}";
            if (!string.IsNullOrEmpty(frequency)) url += $"&frequency={frequency}";
            if (request.Since.HasValue)
                url += $"&observation_start={DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            if (request.Until.HasValue)
                url += $"&observation_end={DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            return url;
        }

        /// <summary>
        /// FRED observations → a point-in-time series.
        ///
        /// <para>
        /// ── THE POINT-IN-TIME DECISION ──
        /// Every observation carries both <c>date</c> (the period the number describes) and
        /// <c>realtime_start</c> (the day that value became public). <b>This provider stamps
        /// every bar with <c>realtime_start</c>, not <c>date</c>.</b> Same rule, same reason as
        /// <c>SecEdgarProvider.ParseConcept</c>, which stamps at <c>filed</c>.
        /// </para>
        ///
        /// <para>
        /// Using <c>date</c> was look-ahead bias of the most damaging kind, and it was what this
        /// provider did. CPIAUCSL for 2020-01 lands on 2020-01-01; it was first released
        /// 2020-02-13 and revised twice after. A strategy gated on "CPI rising" saw January's
        /// CPI on January 1st, six weeks before anyone could. GDP is worse: the Q1 advance
        /// estimate is published a month after quarter-end and revised twice more, and the
        /// default request serves the LATEST vintage — a number that did not exist in any form
        /// until long after the bar it sat on. Every macro-conditioned result in
        /// <c>docs/*_FINDINGS.md</c> produced before 2026-08-25 inherited this.
        /// </para>
        ///
        /// <para>
        /// Revisions are handled by the same rule rather than a special case: the request asks
        /// for initial releases (<c>output_type=4</c>), so what lands on the chart is what was
        /// first printed, on the day it was first printed. Where several observations share a
        /// release date — a backfill, or a monthly print that revises the prior month alongside
        /// it — the one covering the LATEST period wins, because that is the current reading as
        /// of that release.
        /// </para>
        /// </summary>
        internal static List<Ohlcv> ParseObservations(string json)
        {
            var observations = JObject.Parse(json)["observations"] as JArray;
            if (observations == null) return new List<Ohlcv>();

            var best = new Dictionary<DateTime, (DateTime Period, double Val)>();
            foreach (var o in observations)
            {
                string? raw = o["value"]?.ToString();
                if (raw == null || raw == ".") continue;  // FRED uses "." for missing data
                if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)) continue;

                // realtime_start is the publication date. Fall back to the period date only if
                // the field is absent — an older cached payload, or a FRED response shape
                // change — which keeps the series rendering rather than going blank, at the
                // cost of the old bias for those rows.
                string? stamp = o["realtime_start"]?.ToString();
                if (string.IsNullOrEmpty(stamp)) stamp = o["date"]?.ToString();
                if (!DateTime.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var stampDt)) continue;
                stampDt = DateTime.SpecifyKind(stampDt.Date, DateTimeKind.Utc);

                DateTime.TryParseExact(o["date"]?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var periodDt);

                if (!best.TryGetValue(stampDt, out var cur) || periodDt > cur.Period)
                    best[stampDt] = (periodDt, val);
            }

            return best.OrderBy(kv => kv.Key)
                       .Select(kv => new Ohlcv(kv.Key, kv.Value.Val, kv.Value.Val, kv.Value.Val, kv.Value.Val, 0))
                       .ToList();
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
                            string url = $"{BaseUrl}/category/series?category_id={Uri.EscapeDataString(catId)}&api_key={Uri.EscapeDataString(_apiKey ?? "")}&file_type=json&limit=50&order_by=popularity&sort_order=desc";
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
                    string url = $"{BaseUrl}/series/search?search_text={Uri.EscapeDataString(searchText)}&api_key={Uri.EscapeDataString(_apiKey ?? "")}&file_type=json&limit={limit}&order_by=search_rank";
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
        /// <summary>
        /// The same list as <see cref="NativelySupportedTimeframes"/>, and derived from it so
        /// the two cannot drift. It used to repeat the list by hand AND add a <c>"1y"</c> that
        /// <c>TimeframeUtility</c>'s grammar (<c>^(\d+)([mhdMw])$</c>) rejects outright — an
        /// annual FRED series was offered and could never be parsed.
        /// </summary>
        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(new List<string>(NativelySupportedTimeframes));
        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        /// <summary>
        /// A timeframe token to FRED's <c>frequency</c> parameter.
        ///
        /// <para><b>CASE SENSITIVE, and that is the whole point.</b> This used to call
        /// <c>tf.ToLower()</c> first, which made <c>"1m"</c> (one minute) and <c>"1M"</c> (one
        /// month) indistinguishable — so a minute chart was served monthly observations. An
        /// unrecognised token returns empty, which is what FRED's default frequency handling
        /// expects, and is the honest answer for a resolution this provider does not publish.</para>
        /// </summary>
        private static string MapFrequency(string tf) => tf switch
        {
            "1d"        => "d",
            "1w"        => "w",
            OneMonthTf  => "m",
            ThreeMonthsTf => "q",
            _           => ""
        };

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
