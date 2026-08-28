using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.SecEdgar
{
    /// <summary>
    /// SEC EDGAR — company financials from XBRL, and filing-event rates from the submissions index.
    ///
    /// <para>
    /// ── Why this is the anchor of the whole data layer ──────────────────────────
    /// It is the PRIMARY SOURCE, it is free, and it needs no key. Verified 2026-08-02: one
    /// unauthenticated call returns 338 diluted-EPS datapoints for Apple back to 2007, and the
    /// company-facts document carries 503 distinct us-gaap concepts. FMP's paid tiers are
    /// substantially reselling this — which is why "surely the paid tier has the fundamentals" was a
    /// reasonable expectation and still the wrong purchase. Nothing in the dossier design needs
    /// buying; it needed building.
    /// </para>
    ///
    /// <para>
    /// ── Two endpoint families, two very different shapes ────────────────────────
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <b>XBRL company concepts</b> — <c>data.sec.gov/api/xbrl/companyconcept/CIK{10}/us-gaap/{Concept}.json</c>.
    ///     A time series of reported values, each stamped with the period it covers AND the date it
    ///     was FILED. That second date is the one that matters and it is why this source can be used
    ///     honestly: see the point-in-time note below.
    ///   </item>
    ///   <item>
    ///     <b>Submissions index</b> — <c>data.sec.gov/submissions/CIK{10}.json</c>, PLUS the
    ///     supplementary pages it lists under <c>filings.files</c>. Every filing with its form
    ///     type and date. Turned into COUNTS per period, which is the shape the research
    ///     wants: 8-K frequency against a company's own baseline, Form 4 insider-transaction
    ///     clustering. Counts and rates are honest history; a judgement about what a filing MEANT
    ///     would not be. Reading only <c>filings.recent</c> — as this did until 2026-08-27 —
    ///     caps a large filer at roughly the last twelve months and hands the consumer a
    ///     truncation it cannot tell apart from a quiet stretch.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// ── THE POINT-IN-TIME DECISION, which is the whole reason this is trustworthy ──
    /// Every XBRL fact carries both <c>end</c> (the period it describes) and <c>filed</c> (the day it
    /// became public). <b>This provider stamps every bar with <c>filed</c>, not <c>end</c>.</b>
    /// </para>
    ///
    /// <para>
    /// Using <c>end</c> would be lookahead of the most damaging kind: Apple's quarter ending
    /// 2026-06-27 was not knowable until it was filed weeks later, and a backtest that placed the
    /// number on the quarter-end date would trade on information nobody had. It would also look
    /// excellent, because earnings are correlated with the price move that follows their
    /// announcement. This is the same class of error as the Cipher SR proximity artifact, and unlike
    /// that one it is invisible — the series looks perfectly reasonable either way.
    /// </para>
    ///
    /// <para>
    /// Restatements are handled by the same rule rather than a special case: a restated figure has a
    /// LATER <c>filed</c> date, so it lands on the day it was actually published, and the original
    /// figure stays where it was. The series therefore reads as "what the market knew, when it knew
    /// it" rather than "what turned out to be true".
    /// </para>
    ///
    /// <para>
    /// ── Rate limit ──────────────────────────────────────────────────────────────
    /// The SEC asks for a **descriptive User-Agent with contact information** and caps clients at
    /// **10 requests per second**. Both are enforced here. Anonymous or generic agents get blocked,
    /// which would surface as an empty dossier rather than an error — so the agent string is not
    /// cosmetic.
    /// </para>
    /// </summary>
    public class SecEdgarProvider : BaseMarketDataProvider
    {
        /// <summary>
        /// The default contact used in the User-Agent.
        ///
        /// <para>
        /// This is NOT cosmetic and the requirement is asymmetric in a way that is easy to miss.
        /// <b><c>www.sec.gov</c> returns 403 to any User-Agent without a contact email;
        /// <c>data.sec.gov</c> does not care.</b> Verified 2026-08-02: the identical request differs
        /// only in the agent string and returns 403 versus 200. Since the XBRL and submissions
        /// endpoints live on data.sec.gov and only the ticker→CIK map lives on www.sec.gov, an agent
        /// without an email produces a provider that resolves NO TICKERS AT ALL while every other
        /// call looks healthy — which reads as "the data is missing" rather than "you are blocked".
        /// </para>
        ///
        /// <para>
        /// Overridable via <c>Configure(["ContactEmail"])</c>. Operators running their own instance
        /// should set their own address: it is what the SEC uses to reach a client that is
        /// misbehaving, and pointing it at someone else is both rude and fragile.
        /// </para>
        /// </summary>
        internal const string DefaultContact = "codythurst@gmail.com";

        private string _contact = DefaultContact;
        private HttpClient _http;

        public SecEdgarProvider() => _http = BuildClient(DefaultContact);

        private static HttpClient BuildClient(string contact) => PluginHostServices.CreateHttpClient(
            providerId: "SecEdgar",
            allowedHosts: new[] { "data.sec.gov", "www.sec.gov" },
            userAgent: UserAgentFor(contact));

        /// <summary>
        /// The SEC asks for "Sample Company Name AdminContact@example.com", but a bare email is not
        /// a legal User-Agent value — <c>HttpHeaders.Add</c> rejects it, because RFC 9110 allows only
        /// product tokens and parenthesised comments. Putting the address in a COMMENT satisfies both
        /// the parser and the SEC; verified 2026-08-02 that the parenthesised form returns 200.
        /// </summary>
        internal static string UserAgentFor(string contact) => $"AccessibleTrader/2.2 ({contact})";

        // The SEC's published ceiling is 10/sec. Sitting at 8 leaves headroom for the ticker-map
        // fetch that piggybacks on the same limiter.
        private readonly RateLimiter _rateLimiter = new(8, TimeSpan.FromSeconds(1));

        private const string XbrlBase = "https://data.sec.gov/api/xbrl/companyconcept";
        private const string SubmissionsBase = "https://data.sec.gov/submissions";
        private const string TickerMapUrl = "https://www.sec.gov/files/company_tickers.json";

        /// <summary>ticker → CIK. Fetched once; the SEC publishes it as a single small document.</summary>
        private readonly ConcurrentDictionary<string, int> _cikByTicker = new(StringComparer.OrdinalIgnoreCase);
        private bool _tickerMapLoaded;

        public override string Name => "SEC EDGAR";
        public override string Description => "SEC filings and XBRL financials — free, no key, point-in-time by filing date.";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Economic };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override List<string> NativelySupportedTimeframes => new()
        {
            // Fundamentals arrive quarterly at best. Daily is offered because the series is stamped
            // by FILING date, which is a specific day, and the chart aligns it to daily bars.
            StandardTimeframes.OneDay,
        };

        // ── Symbol catalogue ────────────────────────────────────────────────────

        /// <summary>
        /// The metrics offered per ticker, mapped to their us-gaap concept.
        ///
        /// <para>
        /// Deliberately small. EDGAR exposes 503 concepts for a large filer and most of them are
        /// noise for a reader — the dossier's job is a headline, not a data dump. Accruals is in the
        /// list because it is the one item here with a documented anomaly behind it (Sloan), and it
        /// is derived rather than reported, which is flagged below.
        /// </para>
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, (string Concept, string Display)> Metrics =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["EPS"] = ("EarningsPerShareDiluted", "diluted EPS"),
                ["REVENUE"] = ("RevenueFromContractWithCustomerExcludingAssessedTax", "revenue"),
                ["NETINCOME"] = ("NetIncomeLoss", "net income"),
                ["ASSETS"] = ("Assets", "total assets"),
                ["LIABILITIES"] = ("Liabilities", "total liabilities"),
                ["EQUITY"] = ("StockholdersEquity", "shareholders' equity"),
                ["CASH"] = ("CashAndCashEquivalentsAtCarryingValue", "cash and equivalents"),
                ["OPCASHFLOW"] = ("NetCashProvidedByUsedInOperatingActivities", "operating cash flow"),
                ["SHARES"] = ("WeightedAverageNumberOfDilutedSharesOutstanding", "diluted share count"),
                ["RND"] = ("ResearchAndDevelopmentExpense", "R&D expense"),
            };

        /// <summary>Filing-count series, which are rates rather than reported values.</summary>
        internal static readonly IReadOnlyDictionary<string, (string Form, string Display)> FilingCounts =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["INSIDER"] = ("4", "insider transactions (Form 4)"),
                ["EVENTS"] = ("8-K", "material events (8-K)"),
                ["INSTITUTIONAL"] = ("13F-HR", "institutional holdings filings (13F)"),
            };

        /// <summary>A seed list for the dropdown. Any ticker EDGAR knows works, listed or not.</summary>
        private static readonly string[] SeedTickers =
        {
            "AAPL", "MSFT", "NVDA", "GOOGL", "AMZN", "META", "TSLA", "JPM", "XOM", "WMT",
            "JNJ", "PG", "KO", "MCD", "CAT", "IBM", "PFE", "CVX", "MMM", "VZ",
        };

        public override string GetSymbolDisplayName(string symbol)
        {
            var (ticker, metric) = Split(symbol);
            if (metric == null) return symbol;
            if (Metrics.TryGetValue(metric, out var m)) return $"{ticker} {m.Display}";
            if (FilingCounts.TryGetValue(metric, out var f)) return $"{ticker} {f.Display}";
            return symbol;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ContactEmail", out var email)
                && !string.IsNullOrWhiteSpace(email) && email.Contains('@'))
            {
                _contact = email.Trim();
                // Dispose the client we are replacing. Configure runs again on every
                // contact-email change, and each run used to strand the previous client and its
                // connections — on the WebHost, where providers are rebuilt per configuration
                // change, that is once per reconfigure for the life of the process.
                var previous = _http;
                _http = BuildClient(_contact);
                previous?.Dispose();
                _tickerMapLoaded = false;      // re-resolve under the new identity
                _cikByTicker.Clear();
            }
        }

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
        {
            var keys = string.Equals(subType, "Filings", StringComparison.OrdinalIgnoreCase)
                ? FilingCounts.Keys.ToList()
                : Metrics.Keys.ToList();

            return Task.FromResult(SeedTickers
                .SelectMany(t => keys.Select(k => $"{t}_{k}"))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList());
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Financials", "Filings" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        // ── Fetch ───────────────────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var empty = (new List<Ohlcv>(), new List<(long, double)>());
            try
            {
                var (ticker, metric) = Split(request.Symbol);
                if (metric == null)
                {
                    _errorStream.OnNext($"SEC EDGAR: '{request.Symbol}' is not TICKER_METRIC (e.g. AAPL_EPS).");
                    return empty;
                }

                int? cik = await ResolveCikAsync(ticker);
                if (cik == null)
                {
                    _errorStream.OnNext($"SEC EDGAR: no CIK for ticker '{ticker}'.");
                    return empty;
                }

                List<Ohlcv> bars;
                if (Metrics.TryGetValue(metric, out var m))
                    bars = await FetchConceptAsync(cik.Value, m.Concept, request.Symbol);
                else if (FilingCounts.TryGetValue(metric, out var f))
                    bars = await FetchFilingCountsAsync(cik.Value, f.Form);
                else
                {
                    _errorStream.OnNext($"SEC EDGAR: unknown metric '{metric}'.");
                    return empty;
                }

                bars = Clip(bars, request);
                return (bars, bars.Select(b => (new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), b.Volume)).ToList());
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"SEC EDGAR fetch error: {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return empty;
            }
        }

        private async Task<List<Ohlcv>> FetchConceptAsync(int cik, string concept, string symbol)
        {
            string url = $"{XbrlBase}/CIK{cik:D10}/us-gaap/{concept}.json";
            using var resp = await _rateLimiter.ExecuteAsync(() => _http.GetAsync(url));

            // A 404 means this filer does not report that concept — common and not an error. A bank
            // has no R&D line. Raising it would put noise on the stream for an ordinary situation.
            if (resp.StatusCode == HttpStatusCode.NotFound) return new List<Ohlcv>();
            resp.EnsureSuccessStatusCode();

            return ParseConcept(await resp.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// XBRL facts → a point-in-time series.
        ///
        /// <para>
        /// Stamped by <c>filed</c>, never by <c>end</c> — see the class docs; this is the single
        /// most important line in the file. Where several facts share a filing date (a 10-K restates
        /// prior quarters), the one covering the LATEST period wins, because that is the current
        /// figure as of that filing.
        /// </para>
        /// </summary>
        internal static List<Ohlcv> ParseConcept(string json)
        {
            var root = JObject.Parse(json);
            var units = root["units"] as JObject;
            if (units == null) return new List<Ohlcv>();

            // Prefer a USD unit where present; otherwise take whichever the filer used (USD/shares
            // for EPS, "shares" for counts).
            var unit = units.Properties().FirstOrDefault(p => p.Name == "USD")
                    ?? units.Properties().FirstOrDefault();
            if (unit?.Value is not JArray facts) return new List<Ohlcv>();

            var best = new Dictionary<DateTime, (DateTime End, double Val)>();
            foreach (var f in facts)
            {
                string? filed = f["filed"]?.ToString();
                string? end = f["end"]?.ToString();
                var valTok = f["val"];
                if (string.IsNullOrEmpty(filed) || valTok == null) continue;
                if (!DateTime.TryParse(filed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var filedDt)) continue;
                if (!double.TryParse(valTok.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val)) continue;
                DateTime.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDt);

                if (!best.TryGetValue(filedDt.Date, out var cur) || endDt > cur.End)
                    best[filedDt.Date] = (endDt, val);
            }

            return best.OrderBy(kv => kv.Key)
                       .Select(kv => new Ohlcv(kv.Key, kv.Value.Val, kv.Value.Val, kv.Value.Val, kv.Value.Val, 0))
                       .ToList();
        }

        /// <summary>
        /// How many supplementary submission documents will be pulled in for one filer.
        ///
        /// <para>
        /// EDGAR paginates a filer's older filings into <c>filings.files</c>, roughly 1,000
        /// entries per document. Ten covers about 11,000 filings — more than any filer in the
        /// index has — and the cap exists as a runaway guard, not as a data limit. If it is ever
        /// hit, the shortfall is SAID rather than silently trimmed: a truncated count is
        /// indistinguishable from a quiet period, which is the whole defect this fixes.
        /// </para>
        /// </summary>
        private const int MaxSupplementaryFilingDocuments = 10;

        private async Task<List<Ohlcv>> FetchFilingCountsAsync(int cik, string form)
        {
            string url = $"{SubmissionsBase}/CIK{cik:D10}.json";
            string body;
            using (var resp = await _rateLimiter.ExecuteAsync(() => _http.GetAsync(url)))
            {
                if (resp.StatusCode == HttpStatusCode.NotFound) return new List<Ohlcv>();
                resp.EnsureSuccessStatusCode();
                body = await resp.Content.ReadAsStringAsync();
            }

            // `filings.recent` IS NOT THE HISTORY. It caps out around 1,000 entries, which for a
            // large filer is well under a single year of Form 4s — so a five-year insider-
            // clustering study ran against four years of implicit zeros, and the class doc
            // promised "every filing with its form type and date". EDGAR paginates the rest into
            // `filings.files`, a list of supplementary documents this never looked at. The
            // per-day counts below are the union of all of them.
            var counts = ParseFilingCountsInto(body, form);

            var extras = SupplementaryFilingDocuments(body);
            int fetched = 0;
            foreach (var name in extras)
            {
                if (fetched >= MaxSupplementaryFilingDocuments) break;
                fetched++;
                try
                {
                    using var extraResp = await _rateLimiter.ExecuteAsync(
                        () => _http.GetAsync($"{SubmissionsBase}/{name}"));
                    if (!extraResp.IsSuccessStatusCode) continue;
                    MergeFilingCounts(counts, ParseFilingCountsInto(await extraResp.Content.ReadAsStringAsync(), form));
                }
                catch (Exception ex)
                {
                    // One unreachable page means the older end of the series is INCOMPLETE, and
                    // a short series looks exactly like a filer that was quiet then.
                    _errorStream.OnNext(
                        $"SEC EDGAR could not read an older filings page for CIK {cik} ({ex.GetType().Name}); "
                        + "counts before the most recent page may be understated.");
                }
            }

            if (extras.Count > MaxSupplementaryFilingDocuments)
            {
                _errorStream.OnNext(
                    $"SEC EDGAR filing counts for CIK {cik} stop after {MaxSupplementaryFilingDocuments} "
                    + $"history pages; {extras.Count - MaxSupplementaryFilingDocuments} older pages were not read.");
            }

            return counts.OrderBy(kv => kv.Key)
                         .Select(kv => new Ohlcv(kv.Key, kv.Value, kv.Value, kv.Value, kv.Value, 0))
                         .ToList();
        }

        /// <summary>
        /// The names of the supplementary submission documents holding a filer's older filings,
        /// newest first as EDGAR lists them. Empty for a filer whose whole history fits in
        /// <c>filings.recent</c>.
        /// </summary>
        internal static List<string> SupplementaryFilingDocuments(string json)
        {
            var files = JObject.Parse(json)["filings"]?["files"] as JArray;
            if (files == null) return new List<string>();
            return files.Select(f => f["name"]?.ToString() ?? string.Empty)
                        .Where(n => n.Length > 0)
                        .ToList();
        }

        private static void MergeFilingCounts(SortedDictionary<DateTime, int> into, SortedDictionary<DateTime, int> from)
        {
            foreach (var kv in from)
            {
                into.TryGetValue(kv.Key, out var existing);
                into[kv.Key] = existing + kv.Value;
            }
        }

        /// <summary>
        /// Filings of one form type, as a count per DAY.
        ///
        /// <para>
        /// A count, not a judgement — which is exactly what makes it backtestable. "Six Form 4s were
        /// filed on this day" is a fact fixed on that day; "this insider buying was bullish" is an
        /// inference a model would be making with hindsight. Days with no filings are absent rather
        /// than zero-filled: the consumer decides whether absence means zero or means no data, and
        /// for a company that IPO'd last year those are different.
        /// </para>
        /// </summary>
        internal static List<Ohlcv> ParseFilingCounts(string json, string form) =>
            ParseFilingCountsInto(json, form)
                .Select(kv => new Ohlcv(kv.Key, kv.Value, kv.Value, kv.Value, kv.Value, 0))
                .ToList();

        /// <summary>
        /// Per-day counts from ONE submissions document, as a dictionary so several documents
        /// can be merged.
        ///
        /// <para>
        /// Handles both shapes EDGAR serves: the primary document nests the arrays under
        /// <c>filings.recent</c>, while a supplementary page from <c>filings.files</c> carries
        /// the same <c>form</c>/<c>filingDate</c> arrays at its ROOT.
        /// </para>
        /// </summary>
        internal static SortedDictionary<DateTime, int> ParseFilingCountsInto(string json, string form)
        {
            var counts = new SortedDictionary<DateTime, int>();
            var root = JObject.Parse(json);
            var block = root["filings"]?["recent"] as JObject ?? root;

            var forms = block["form"] as JArray;
            var dates = block["filingDate"] as JArray;
            if (forms == null || dates == null) return counts;

            for (int i = 0; i < Math.Min(forms.Count, dates.Count); i++)
            {
                if (!string.Equals(forms[i].ToString(), form, StringComparison.OrdinalIgnoreCase)) continue;
                if (!DateTime.TryParse(dates[i].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) continue;
                counts.TryGetValue(d.Date, out var c);
                counts[d.Date] = c + 1;
            }
            return counts;
        }

        // ── CIK resolution ──────────────────────────────────────────────────────

        internal async Task<int?> ResolveCikAsync(string ticker)
        {
            if (!_tickerMapLoaded)
            {
                try
                {
                    var json = await _rateLimiter.ExecuteAsync(() => _http.GetStringAsync(TickerMapUrl));
                    LoadTickerMap(json, _cikByTicker);
                    _tickerMapLoaded = true;
                }
                catch (Exception ex)
                {
                    _errorStream.OnNext($"SEC EDGAR: could not load the ticker→CIK map: {ex.Message}");
                    return null;
                }
            }
            return _cikByTicker.TryGetValue(ticker, out var cik) ? cik : null;
        }

        /// <summary>
        /// The SEC publishes the map as an OBJECT keyed by row number, not an array — an easy shape
        /// to get wrong, and getting it wrong yields an empty map and a provider that reports "no
        /// CIK" for every ticker on earth.
        /// </summary>
        internal static void LoadTickerMap(string json, ConcurrentDictionary<string, int> into)
        {
            var root = JObject.Parse(json);
            foreach (var prop in root.Properties())
            {
                var t = prop.Value["ticker"]?.ToString();
                var c = prop.Value["cik_str"];
                if (string.IsNullOrEmpty(t) || c == null) continue;
                if (int.TryParse(c.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cik))
                    into[t!] = cik;
            }
        }

        // ── Plumbing ────────────────────────────────────────────────────────────

        /// <summary>TICKER_METRIC. The metric is the last underscore-separated part.</summary>
        internal static (string Ticker, string? Metric) Split(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return ("", null);
            int i = symbol.LastIndexOf('_');
            if (i <= 0 || i == symbol.Length - 1) return (symbol, null);
            return (symbol[..i], symbol[(i + 1)..]);
        }

        private static List<Ohlcv> Clip(List<Ohlcv> bars, MarketDataRequest request)
        {
            if (request.Since.HasValue)
            {
                var since = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime.Date;
                bars = bars.Where(b => b.Date >= since).ToList();
            }
            if (request.Until.HasValue)
            {
                var until = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime.Date;
                bars = bars.Where(b => b.Date <= until).ToList();
            }
            return bars;
        }

        /// <summary>Releases the current <see cref="HttpClient"/>; <c>Configure</c> disposes the
        /// ones it replaces.</summary>
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
