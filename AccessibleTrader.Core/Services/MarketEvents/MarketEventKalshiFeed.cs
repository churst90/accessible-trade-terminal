using System.Globalization;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>The narrow expected-figure surface the calendar and bar map read.</summary>
    public interface IMarketEventExpectedFeed
    {
        /// <summary>Bumped whenever a fetched expected figure changes.</summary>
        int Version { get; }

        /// <summary>The expected figure for one release and figure, or null when none is stored.</summary>
        double? ExpectedFor(string source, DateOnly releaseDate, string figureName);

        /// <summary>Starts a background pass, gated by the events level and the ten-minute interval.</summary>
        void RequestRefresh(WorkspaceState state);
    }

    /// <summary>The settings and calendar status line for Kalshi; never a network call.</summary>
    public sealed record MarketEventKalshiStatus(int ReleasesRead, DateTime? UpdatedUtc)
    {
        public string Describe()
        {
            if (UpdatedUtc is not { } updated)
                return "Expected figures: Kalshi not reached yet.";
            string when = updated.ToLocalTime()
                .ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);
            return $"Expected figures: Kalshi, {ReleasesRead} "
                + $"release{(ReleasesRead == 1 ? "" : "s")} read, updated {when}.";
        }
    }

    /// <summary>
    /// Kalshi's expected figures: what its traders priced before a release, read from public
    /// market data with no key. One process-wide instance per head; reads are served from memory
    /// and a pass replaces them in the background, never on a navigation path.
    ///
    /// <para>
    /// A 429, a 5xx, a timeout or malformed JSON stops the pass and is never remembered as "no
    /// market". A 404 or an empty list from both the live and historical endpoints is a real no
    /// market, and becomes permanent only once the release is more than two days past.
    /// </para>
    /// </summary>
    public sealed class MarketEventKalshiFeed : IMarketEventExpectedFeed
    {
        private const string BaseUrl = "https://api.elections.kalshi.com/trade-api/v2";
        private static readonly TimeSpan PassInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan UpcomingReRead = TimeSpan.FromHours(1);
        private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(340);
        private static readonly TimeSpan NoMarketSettlesAfter = TimeSpan.FromDays(2);
        private static readonly DateOnly BackfillStart = new(2021, 6, 1);
        private const int PastReleasesPerPass = 40;

        private static readonly IReadOnlyDictionary<string, string[]> SourceFigures =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["cpi"] = new[] { "CPI", "core CPI" },
                ["nfp"] = new[] { "payrolls", "unemployment" },
                ["gdp"] = new[] { "GDP" },
            };

        private sealed record AnswerKey(string Source, DateOnly Date, string Figure);

        private sealed record Answer(double? Expected, bool Final, DateTime ReadUtc);

        private readonly HttpClient _http;
        private readonly ILogger<MarketEventKalshiFeed> _logger;
        private readonly IReadOnlyList<MarketEvent>? _fixedEvents;
        private readonly IReadOnlyList<MarketEvent> _seedEvents;
        private readonly IMarketEventFigureFeed? _fredFeed;
        private readonly Func<DateTime> _nowUtc;
        private readonly string _cachePath;
        private readonly Func<TimeSpan, Task> _delay;
        private readonly SemaphoreSlim _requestGate = new(1, 1);
        private readonly object _gate = new();
        private readonly Dictionary<AnswerKey, Answer> _answers = new();

        private DateTime? _updatedUtc;
        private DateTime? _lastPassFinishedUtc;
        private DateTime _lastHttpRequestUtc = DateTime.MinValue;
        private int _version;
        private int _running;
        private bool _dirty;

        /// <summary>
        /// The production constructor. Registers with the shared HTTP factory like the FRED feed:
        /// one client, an allow-list of one host, and a 30-second cap. There is no key.
        /// </summary>
        public MarketEventKalshiFeed(
            IPlatformPathService paths,
            ILogger<MarketEventKalshiFeed> logger,
            IMarketEventFigureFeed fredFeed)
            : this(
                PluginHostServices.CreateHttpClient(
                    "MarketEvents.Kalshi",
                    new[] { "api.elections.kalshi.com" },
                    timeout: TimeSpan.FromSeconds(30),
                    userAgent: "AccessibleTrader/2.2 (accessible-trade-terminal)"),
                logger,
                seedEvents: MarketEventSeed.Load().Events,
                fredFeed: fredFeed,
                fixedEvents: null,
                nowUtc: null,
                cachePath: Path.Combine(paths.AppDataDirectory, "market-events", "kalshi.json"),
                delay: span => Task.Delay(span))
        {
        }

        /// <summary>Test seam: a caller-supplied client, clock, cache path and fixed event set.</summary>
        internal MarketEventKalshiFeed(
            HttpClient http,
            IReadOnlyList<MarketEvent> knownEvents,
            Func<DateTime> nowUtc,
            string cachePath,
            ILogger<MarketEventKalshiFeed> logger,
            Func<TimeSpan, Task>? delay = null)
            : this(http, logger, Array.Empty<MarketEvent>(), null, knownEvents, nowUtc, cachePath,
                delay ?? (_ => Task.CompletedTask))
        {
        }

        private MarketEventKalshiFeed(
            HttpClient http,
            ILogger<MarketEventKalshiFeed> logger,
            IReadOnlyList<MarketEvent> seedEvents,
            IMarketEventFigureFeed? fredFeed,
            IReadOnlyList<MarketEvent>? fixedEvents,
            Func<DateTime>? nowUtc,
            string cachePath,
            Func<TimeSpan, Task>? delay)
        {
            _http = http;
            _logger = logger;
            _seedEvents = seedEvents;
            _fredFeed = fredFeed;
            _fixedEvents = fixedEvents;
            _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
            _cachePath = cachePath;
            _delay = delay ?? (span => Task.Delay(span));
            LoadCache();
        }

        /// <summary>Bumped whenever an answer is added or changes; the bar-map cache watches it.</summary>
        public int Version => Volatile.Read(ref _version);

        /// <summary>The settings/calendar status; no key and no request.</summary>
        public MarketEventKalshiStatus Status
        {
            get
            {
                lock (_gate)
                {
                    int releases = _answers.Keys
                        .Select(k => (k.Source, k.Date))
                        .Distinct()
                        .Count();
                    return new MarketEventKalshiStatus(releases, _updatedUtc);
                }
            }
        }

        /// <summary>The stored expected figure, final or provisional, or null.</summary>
        public double? ExpectedFor(string source, DateOnly releaseDate, string figureName)
        {
            var key = new AnswerKey(source, releaseDate, figureName);
            lock (_gate)
                return _answers.TryGetValue(key, out var answer) ? answer.Expected : null;
        }

        /// <summary>
        /// Called on every event read. Off means nothing at all; otherwise a background pass runs
        /// when none is running and the last one finished more than ten minutes ago.
        /// </summary>
        public void RequestRefresh(WorkspaceState state)
        {
            if (MarketEventLevels.Parse(state.MarketEventsLevel) == MarketEventLevel.Off) return;

            var now = AsUtc(_nowUtc());
            lock (_gate)
            {
                if (_lastPassFinishedUtc is { } last && now - last < PassInterval) return;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunPassAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("A background Kalshi expected-figures refresh failed: {Type}.",
                        ex.GetType().Name);
                }
            });
        }

        /// <summary>
        /// One pass. Internal so a test can run it without the ten-minute gate; the UI never
        /// calls it directly. It never throws to the caller.
        /// </summary>
        internal async Task RunPassAsync()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            bool clean = false;
            try
            {
                await PassCoreAsync().ConfigureAwait(false);
                clean = true;
            }
            catch (KalshiStopException ex)
            {
                _logger.LogWarning("A Kalshi expected-figures pass stopped at HTTP {Status}; trying later.",
                    ex.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("A Kalshi expected-figures pass failed: {Type}.", ex.GetType().Name);
            }
            finally
            {
                var now = AsUtc(_nowUtc());
                lock (_gate)
                {
                    _lastPassFinishedUtc = now;
                    if (clean) _updatedUtc = now;
                }
                SaveCache();
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private async Task PassCoreAsync()
        {
            var now = AsUtc(_nowUtc());
            var all = KnownEvents();

            var upcoming = all
                .Where(e => e.Kind == MarketEventKind.Economy)
                .Where(e => HasKnownFigures(e))
                .Where(e => AsUtc(e.MomentUtc) > now && AsUtc(e.MomentUtc) <= now.AddDays(14))
                .OrderBy(e => e.MomentUtc)
                .ToList();

            var past = all
                .Where(e => e.Kind == MarketEventKind.Economy)
                .Where(e => HasKnownFigures(e))
                .Where(e => e.Date >= BackfillStart && AsUtc(e.MomentUtc) <= now)
                .Where(e => HasPendingFinalAnswer(e))
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.MomentUtc)
                .Take(PastReleasesPerPass)
                .ToList();

            foreach (var e in upcoming)
            {
                foreach (string figure in SourceFigures[SourceOf(e)])
                {
                    var key = new AnswerKey(SourceOf(e), e.Date, figure);
                    if (SkipUpcoming(key, now)) continue;
                    double? expected = await FetchExpectedAsync(e, figure, upcoming: true)
                        .ConfigureAwait(false);
                    Apply(key, expected, final: false, now);
                }
            }

            foreach (var e in past)
            {
                foreach (string figure in SourceFigures[SourceOf(e)])
                {
                    var key = new AnswerKey(SourceOf(e), e.Date, figure);
                    if (HasFinalAnswer(key)) continue;
                    double? expected = await FetchExpectedAsync(e, figure, upcoming: false)
                        .ConfigureAwait(false);
                    bool settled = now - AsUtc(e.MomentUtc) > NoMarketSettlesAfter;
                    Apply(key, expected, final: expected is not null || settled, now);
                }
            }
        }

        private bool SkipUpcoming(AnswerKey key, DateTime now)
        {
            lock (_gate)
                return _answers.TryGetValue(key, out var answer)
                    && (answer.Final || now - answer.ReadUtc < UpcomingReRead);
        }

        private bool HasFinalAnswer(AnswerKey key)
        {
            lock (_gate) return _answers.TryGetValue(key, out var answer) && answer.Final;
        }

        private bool HasPendingFinalAnswer(MarketEvent e)
        {
            foreach (string figure in SourceFigures[SourceOf(e)])
                if (!HasFinalAnswer(new AnswerKey(SourceOf(e), e.Date, figure)))
                    return true;
            return false;
        }

        private void Apply(AnswerKey key, double? expected, bool final, DateTime now)
        {
            bool changed;
            lock (_gate)
            {
                changed = !_answers.TryGetValue(key, out var old)
                    || old.Expected != expected
                    || old.Final != final;
                _answers[key] = new Answer(expected, final, now);
            }
            if (changed)
            {
                _dirty = true;
                Interlocked.Increment(ref _version);
            }
        }

        private async Task<double?> FetchExpectedAsync(
            MarketEvent e, string figureName, bool upcoming)
        {
            if (!KalshiExpectedFigures.TryGetSpec(figureName, out var spec)) return null;

            foreach (string eventTicker in KalshiExpectedFigures.EventTickers(figureName, e.Date))
            {
                var listing = await ListMarketsAsync(eventTicker).ConfigureAwait(false);
                if (listing == null) continue;

                var markets = listing.Markets
                    .Where(m => string.Equals((string?)m["strike_type"], "greater",
                        StringComparison.Ordinal))
                    .Where(m => m["floor_strike"] != null)
                    .Where(m => KalshiExpectedFigures.CloseDateMatches(
                        (string?)m["close_time"], e.Date))
                    .ToList();
                if (markets.Count == 0) continue;

                var ladder = new List<KalshiStrikeProbability>(markets.Count);
                string series = eventTicker[..Math.Max(0, eventTicker.IndexOf('-'))];
                foreach (var market in markets)
                {
                    double? probability = upcoming
                        ? KalshiExpectedFigures.MarketProbability(market)
                        : await CandleProbabilityAsync(
                            market, series, listing.Historical, e.MomentUtc).ConfigureAwait(false);
                    if (probability is not double value) continue;

                    double strike = market["floor_strike"]!.Value<double>() / spec.StrikeScale;
                    ladder.Add(new KalshiStrikeProbability(strike, value));
                }
                return KalshiExpectedFigures.Median(ladder, spec.Step);
            }
            return null;
        }

        private async Task<double?> CandleProbabilityAsync(
            JObject market, string series, bool historical, DateTime releaseMomentUtc)
        {
            string? ticker = (string?)market["ticker"];
            if (string.IsNullOrWhiteSpace(ticker)) return null;

            long end = new DateTimeOffset(AsUtc(releaseMomentUtc)).ToUnixTimeSeconds();
            long start = end - (long)TimeSpan.FromDays(3).TotalSeconds;
            string path = historical
                ? $"/historical/markets/{Uri.EscapeDataString(ticker)}/candlesticks"
                : $"/series/{Uri.EscapeDataString(series)}/markets/{Uri.EscapeDataString(ticker)}/candlesticks";
            string url = $"{BaseUrl}{path}?start_ts={start}&end_ts={end}&period_interval=60";

            IReadOnlyList<JObject> candles;
            try
            {
                candles = Candles(await GetAsync(url).ConfigureAwait(false));
            }
            catch (KalshiNotFoundException)
            {
                // This market has no saved/live candle path; it simply has no usable quote.
                return null;
            }
            return KalshiExpectedFigures.ProbabilityAt(candles, releaseMomentUtc);
        }

        private async Task<MarketListing?> ListMarketsAsync(string eventTicker)
        {
            string query = "?event_ticker=" + Uri.EscapeDataString(eventTicker) + "&limit=100";
            try
            {
                var live = Markets(await GetAsync(BaseUrl + "/markets" + query).ConfigureAwait(false));
                if (live.Count > 0) return new MarketListing(live, Historical: false);
            }
            catch (KalshiNotFoundException)
            {
                // Older events are not on the live endpoint; the historical path is next.
            }

            try
            {
                var historical = Markets(
                    await GetAsync(BaseUrl + "/historical/markets" + query).ConfigureAwait(false));
                if (historical.Count > 0) return new MarketListing(historical, Historical: true);
            }
            catch (KalshiNotFoundException)
            {
            }
            return null;
        }

        private async Task<string> GetAsync(string url)
        {
            await _requestGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var now = AsUtc(_nowUtc());
                var wait = _lastHttpRequestUtc + RequestSpacing - now;
                if (wait > TimeSpan.Zero && _lastHttpRequestUtc != DateTime.MinValue)
                    await _delay(wait).ConfigureAwait(false);
                _lastHttpRequestUtc = AsUtc(_nowUtc());

                using var response = await _http.GetAsync(url).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status == 404) throw new KalshiNotFoundException();
                if (status == 429 || status >= 500) throw new KalshiStopException(status);
                if (!response.IsSuccessStatusCode) throw new KalshiStopException(status);
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            finally
            {
                _requestGate.Release();
            }
        }

        private IReadOnlyList<MarketEvent> KnownEvents()
        {
            if (_fixedEvents != null) return _fixedEvents;

            var byId = new Dictionary<string, MarketEvent>(StringComparer.Ordinal);
            foreach (var e in _seedEvents) byId[e.Id] = e;
            if (_fredFeed != null)
                foreach (var e in _fredFeed.Events) byId[e.Id] = e;
            return byId.Values.ToList();
        }

        private static bool HasKnownFigures(MarketEvent e)
            => SourceFigures.ContainsKey(SourceOf(e));

        private static string SourceOf(MarketEvent e)
        {
            int dash = e.Id.IndexOf('-');
            return dash > 0 ? e.Id[..dash] : e.Id;
        }

        private static List<JObject> Markets(string json)
            => (JObject.Parse(json)["markets"] as JArray ?? new JArray()).OfType<JObject>().ToList();

        private static List<JObject> Candles(string json)
            => (JObject.Parse(json)["candlesticks"] as JArray ?? new JArray()).OfType<JObject>().ToList();

        private void LoadCache()
        {
            if (!File.Exists(_cachePath)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(_cachePath));
                DateTime? updated = ParseUtc(root["updatedUtc"]?.ToString());
                var answers = new Dictionary<AnswerKey, Answer>();
                foreach (var row in (root["answers"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string source = (string?)row["source"] ?? "";
                    string figure = (string?)row["figure"] ?? "";
                    if (source.Length == 0 || figure.Length == 0) continue;
                    if (!DateOnly.TryParseExact((string?)row["date"], "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;
                    double? expected = row["expected"]?.Type == JTokenType.Null ||
                        row["expected"] == null
                        ? null
                        : row["expected"]!.Value<double>();
                    bool final = row["final"]?.Value<bool>() ?? false;
                    var read = ParseUtc(row["readUtc"]?.ToString()) ?? updated ?? DateTime.MinValue;
                    answers[new AnswerKey(source, date, figure)] = new Answer(expected, final, read);
                }

                lock (_gate)
                {
                    _answers.Clear();
                    foreach (var pair in answers) _answers[pair.Key] = pair.Value;
                    _updatedUtc = updated;
                    _version = answers.Count > 0 ? 1 : 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Ignoring an unreadable Kalshi expected-figures cache: {Type}.",
                    ex.GetType().Name);
            }
        }

        private void SaveCache()
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                JArray rows;
                DateTime? updated;
                lock (_gate)
                {
                    rows = new JArray(_answers
                        .OrderBy(pair => pair.Key.Date)
                        .ThenBy(pair => pair.Key.Source, StringComparer.Ordinal)
                        .ThenBy(pair => pair.Key.Figure, StringComparer.Ordinal)
                        .Select(pair => new JObject
                        {
                            ["source"] = pair.Key.Source,
                            ["date"] = pair.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            ["figure"] = pair.Key.Figure,
                            ["expected"] = pair.Value.Expected is double expected
                                ? new JValue(expected)
                                : JValue.CreateNull(),
                            ["final"] = pair.Value.Final,
                            ["readUtc"] = pair.Value.ReadUtc.ToString("O", CultureInfo.InvariantCulture),
                        }));
                    updated = _updatedUtc;
                }

                var doc = new JObject
                {
                    ["updatedUtc"] = updated?.ToString("O", CultureInfo.InvariantCulture),
                    ["answers"] = rows,
                };
                AtomicFile.WriteAllText(_cachePath, doc.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not write the Kalshi expected-figures cache: {Type}.",
                    ex.GetType().Name);
            }
        }

        private static DateTime? ParseUtc(string? text)
            => DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
                ? value
                : null;

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        private sealed record MarketListing(IReadOnlyList<JObject> Markets, bool Historical);

        private sealed class KalshiNotFoundException : Exception
        {
        }

        private sealed class KalshiStopException : Exception
        {
            public KalshiStopException(int statusCode) => StatusCode = statusCode;
            public int StatusCode { get; }
        }
    }
}
