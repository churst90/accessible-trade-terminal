using System.Globalization;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>What the settings status line should say about the FRED feed.</summary>
    public enum MarketEventFeedState
    {
        NoKey,
        NotFetched,
        Fetched,
        Failed,
    }

    /// <summary>
    /// The feed's state for the settings screen: the seed's coverage, the last success and the
    /// last failure. Never contains the key.
    /// </summary>
    public sealed record MarketEventFeedStatus(
        MarketEventFeedState State,
        DateTime? FetchedUtc,
        DateTime? FailedUtc,
        string? FailureReason,
        DateOnly SeedNewestReleaseDate)
    {
        /// <summary>The exact status sentence shown beside the Level select and spoken on update.</summary>
        public string Describe()
        {
            string stored = SeedNewestReleaseDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            return State switch
            {
                MarketEventFeedState.Fetched =>
                    $"Release dates and figures updated from FRED on {When(FetchedUtc)}.",
                MarketEventFeedState.Failed =>
                    $"The last update from FRED failed on {When(FailedUtc)}: "
                    + $"{FailureReason ?? "an error"}. Using what was stored before.",
                MarketEventFeedState.NotFetched =>
                    $"Release dates stored up to {stored}. Not yet updated from FRED.",
                _ =>
                    $"Release dates stored up to {stored}. Add a FRED key in API Keys to keep "
                    + "them current and get PPI figures.",
            };
        }

        /// <summary>The user's own clock, in the wording the handoff asks for.</summary>
        private static string When(DateTime? utc)
        {
            if (utc is not { } value) return "an unknown time";
            return value.ToLocalTime().ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Live release dates and figures from FRED. One process-wide instance per head, optional
    /// (no key means nothing is fetched and every consumer works from the seed), and never on a
    /// navigation path: a read uses whatever is loaded, a background task replaces it later.
    ///
    /// <para>
    /// <b>The key never leaves this class.</b> It is put into a request URL and nowhere else:
    /// failure reasons are a short phrase, never an exception message (HTTP error messages can
    /// include the full URL), and the disk cache holds figures, never the request.
    /// </para>
    /// </summary>
    public sealed class MarketEventFredFeed
    {
        private const string BaseUrl = "https://api.stlouisfed.org/fred";
        private const string ProviderName = "FRED";
        private static readonly TimeSpan FetchInterval = TimeSpan.FromHours(12);

        private static readonly IReadOnlyDictionary<string, int> ReleaseIds =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["cpi"] = 10,
                ["nfp"] = 50,
                ["ppi"] = 46,
                ["gdp"] = 53,
            };

        private static readonly IReadOnlyDictionary<string, string[]> SourceSeries =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["cpi"] = new[] { "CPIAUCSL", "CPILFESL" },
                ["nfp"] = new[] { "PAYEMS", "UNRATE" },
                ["gdp"] = new[] { "GDPC1" },
                ["ppi"] = new[] { "PPIFIS" },
            };

        private static readonly IReadOnlyDictionary<string, string> SeriesSource =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CPIAUCSL"] = "cpi",
                ["CPILFESL"] = "cpi",
                ["PAYEMS"] = "nfp",
                ["UNRATE"] = "nfp",
                ["GDPC1"] = "gdp",
                ["PPIFIS"] = "ppi",
            };

        private readonly IApiKeyService _apiKeys;
        private readonly ILogger<MarketEventFredFeed> _logger;
        private readonly HttpClient _http;
        private readonly IReadOnlyList<MarketEvent> _seedEvents;
        private readonly Func<DateTime> _nowUtc;
        private readonly string _cachePath;
        private readonly SemaphoreSlim _oneAtATime = new(1, 1);
        private readonly object _gate = new();

        private IReadOnlyList<MarketEvent> _events = Array.Empty<MarketEvent>();
        private int _version;
        private DateTime? _fetchedUtc;
        private DateTime? _failedUtc;
        private string? _failureReason;
        private DateTime? _lastAttemptUtc;
        private DateTime? _lastRequestUtc;

        /// <summary>
        /// The production constructor. Registers with the shared HTTP factory like the asset
        /// dossier: one client, an allow-list of one host, and a 30-second cap.
        /// </summary>
        public MarketEventFredFeed(
            IApiKeyService apiKeys,
            IPlatformPathService paths,
            ILogger<MarketEventFredFeed> logger)
            : this(apiKeys, paths, logger,
                PluginHostServices.CreateHttpClient(
                    "MarketEvents.Fred",
                    new[] { "api.stlouisfed.org" },
                    timeout: TimeSpan.FromSeconds(30),
                    userAgent: "AccessibleTrader/2.2 (accessible-trade-terminal)"),
                seedEvents: null,
                nowUtc: null,
                cachePath: null)
        {
        }

        /// <summary>Test seam: a caller-supplied client, clock and cache path; no network in tests.</summary>
        internal MarketEventFredFeed(
            IApiKeyService apiKeys,
            IPlatformPathService paths,
            ILogger<MarketEventFredFeed> logger,
            HttpClient http,
            IReadOnlyList<MarketEvent>? seedEvents,
            Func<DateTime>? nowUtc,
            string? cachePath)
        {
            _apiKeys = apiKeys;
            _logger = logger;
            _http = http;
            _seedEvents = seedEvents ?? MarketEventSeed.Load().Events;
            _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
            _cachePath = cachePath ?? Path.Combine(paths.AppDataDirectory, "market-events", "fred.json");
            SeedNewestReleaseDate = _seedEvents
                .Where(e => e.Kind == MarketEventKind.Economy)
                .Select(e => e.Date)
                .DefaultIfEmpty(default)
                .Max();
            LoadCache();
        }

        /// <summary>Bumped whenever new fetched data is applied; the bar-map cache watches it.</summary>
        public int Version => Volatile.Read(ref _version);

        /// <summary>The fetched events in force. Merged over the seed by <see cref="MarketEventCalendar"/>.</summary>
        public IReadOnlyList<MarketEvent> Events => Volatile.Read(ref _events);

        /// <summary>The newest economic release date in the seed, for the "stored up to" line.</summary>
        public DateOnly SeedNewestReleaseDate { get; }

        /// <summary>Loads the last fetch from disk. A corrupt file is ignored and logged, never a crash.</summary>
        private void LoadCache()
        {
            if (!File.Exists(_cachePath)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(_cachePath));
                if (!DateTime.TryParse(root["fetchedUtc"]?.ToString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fetched))
                    return;
                var releases = new List<MarketEvent>();
                foreach (var row in (root["releases"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string source = (string?)row["source"] ?? "";
                    if (!ReleaseIds.ContainsKey(source)) continue;
                    if (row["date"]?.Value<DateTime>() is not { } date) continue;
                    var day = DateOnly.FromDateTime(date);
                    var figures = ReadFigures(row["figures"] as JArray, row);
                    releases.Add(MarketEventSeed.CreateRelease(
                        source, day, figures, fetched.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                }
                lock (_gate)
                {
                    _events = releases;
                    _fetchedUtc = fetched;
                    _version = releases.Count > 0 ? 1 : 0;
                }
            }
            catch (Exception ex)
            {
                // The file is ours and contains no key, but a half-written JSON must not stop
                // the terminal. The seed alone is a complete, working answer.
                _logger.LogWarning("Ignoring an unreadable market-events FRED cache: {Type}.",
                    ex.GetType().Name);
            }
        }

        /// <summary>
        /// Called on every event read. Off means nothing is fetched (stage 1 still reads the
        /// seed); otherwise the first read after start or after the level is turned on starts a
        /// background fetch, gated to once every 12 hours.
        /// </summary>
        public void RequestRefresh(WorkspaceState state)
        {
            if (MarketEventLevels.Parse(state.MarketEventsLevel) == MarketEventLevel.Off) return;

            var now = AsUtc(_nowUtc());
            lock (_gate)
            {
                if (_lastRequestUtc is { } requested && now - requested < TimeSpan.FromSeconds(30))
                    return;
                _lastRequestUtc = now;
            }
            _ = Task.Run(async () =>
            {
                try { await RefreshIfDueAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    // Never into navigation. A background fetch has no error sound or speech.
                    _logger.LogWarning("A background market-events FRED refresh failed: {Type}.",
                        ex.GetType().Name);
                }
            });
        }

        /// <summary>Honours the 12-hour gate. Public for tests; the UI's button skips it.</summary>
        public Task<MarketEventFeedStatus> RefreshIfDueAsync() => RefreshAsync(force: false);

        /// <summary>The settings button: skips the 12-hour gate, one fetch at a time.</summary>
        public Task<MarketEventFeedStatus> UpdateNowAsync() => RefreshAsync(force: true);

        /// <summary>The current state, with a key check but no network.</summary>
        public async Task<MarketEventFeedStatus> GetStatusAsync()
        {
            var key = await _apiKeys.GetKeyForProviderAsync(ProviderName).ConfigureAwait(false);
            return Snapshot(hasKey: !string.IsNullOrWhiteSpace(key?.ApiKey));
        }

        private async Task<MarketEventFeedStatus> RefreshAsync(bool force)
        {
            if (!await _oneAtATime.WaitAsync(0).ConfigureAwait(false))
                return await GetStatusAsync().ConfigureAwait(false);

            try
            {
                var key = await _apiKeys.GetKeyForProviderAsync(ProviderName).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(key?.ApiKey))
                    return Snapshot(hasKey: false);

                var now = AsUtc(_nowUtc());
                if (!force && _lastAttemptUtc is { } last && now - last < FetchInterval)
                    return Snapshot(hasKey: true);

                _lastAttemptUtc = now;
                try
                {
                    var events = await FetchAsync(key.ApiKey, now).ConfigureAwait(false);
                    lock (_gate)
                    {
                        _events = events;
                        _fetchedUtc = now;
                        _failedUtc = null;
                        _failureReason = null;
                        _version++;
                    }
                    SaveCache();
                }
                catch (Exception ex)
                {
                    string reason = FailureReason(ex);
                    lock (_gate)
                    {
                        _failedUtc = now;
                        _failureReason = reason;
                    }
                    // Type name only: a message can carry the request URL and its api_key.
                    _logger.LogWarning("Market-events FRED update failed: {Reason} ({Type}).",
                        reason, ex.GetType().Name);
                }

                return Snapshot(hasKey: true);
            }
            finally
            {
                _oneAtATime.Release();
            }
        }

        private MarketEventFeedStatus Snapshot(bool hasKey)
        {
            lock (_gate)
            {
                if (!hasKey)
                    return new MarketEventFeedStatus(MarketEventFeedState.NoKey,
                        _fetchedUtc, _failedUtc, _failureReason, SeedNewestReleaseDate);
                if (_failedUtc is { } failed && (_fetchedUtc == null || failed >= _fetchedUtc))
                    return new MarketEventFeedStatus(MarketEventFeedState.Failed,
                        _fetchedUtc, failed, _failureReason, SeedNewestReleaseDate);
                if (_fetchedUtc != null)
                    return new MarketEventFeedStatus(MarketEventFeedState.Fetched,
                        _fetchedUtc, _failedUtc, _failureReason, SeedNewestReleaseDate);
                return new MarketEventFeedStatus(MarketEventFeedState.NotFetched,
                    null, null, null, SeedNewestReleaseDate);
            }
        }

        private static string FailureReason(Exception ex) => ex switch
        {
            FredHttpException http => $"HTTP {http.StatusCode}",
            TaskCanceledException => "no answer",
            HttpRequestException => "no answer",
            Newtonsoft.Json.JsonException => "a bad answer",
            FormatException => "a bad answer",
            _ => "an error",
        };

        private async Task<IReadOnlyList<MarketEvent>> FetchAsync(string key, DateTime fetchedUtc)
        {
            string fetched = fetchedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var releaseDates = new Dictionary<string, List<DateOnly>>(StringComparer.Ordinal);
            foreach (var (source, releaseId) in ReleaseIds)
            {
                var last = SeedLastDate(source);
                string start = last.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string url = $"{BaseUrl}/release/dates?release_id={releaseId}"
                    + $"&realtime_start={start}&realtime_end=9999-12-31"
                    + "&include_release_dates_with_no_data=true&sort_order=asc&file_type=json"
                    + $"&api_key={Uri.EscapeDataString(key)}";
                var json = JObject.Parse(await GetAsync(url).ConfigureAwait(false));
                releaseDates[source] = (json["release_dates"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .Select(row => row["date"]?.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => DateOnly.ParseExact(value!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Distinct()
                    .ToList();
            }

            var rows = new Dictionary<string, IReadOnlyList<MarketEventVintageRow>>(StringComparer.Ordinal);
            foreach (var (series, source) in SeriesSource)
            {
                var last = source == "ppi"
                    ? new DateOnly(2014, 1, 1)
                    : SeedLastDate(source).AddDays(-400);
                string start = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string url = $"{BaseUrl}/series/observations?series_id={series}"
                    + $"&realtime_start={start}&realtime_end=9999-12-31";
                if (source != "ppi")
                {
                    string observationStart = last.AddYears(-2)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    url += $"&observation_start={observationStart}";
                }
                url += $"&file_type=json&api_key={Uri.EscapeDataString(key)}";
                rows[series] = ParseObservations(await GetAsync(url).ConfigureAwait(false));
            }

            var events = new List<MarketEvent>();
            foreach (var (source, dates) in releaseDates)
                foreach (var day in dates)
                    events.Add(MarketEventSeed.CreateRelease(
                        source, day, MarketEventFiguresCalculator.Compute(source, day, rows), fetched));
            return events;
        }

        private DateOnly SeedLastDate(string source)
            => _seedEvents
                .Where(e => e.Id.StartsWith(source + "-", StringComparison.Ordinal))
                .Select(e => e.Date)
                .DefaultIfEmpty(new DateOnly(2014, 1, 1))
                .Max();

        private async Task<string> GetAsync(string url)
        {
            using var response = await _http.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new FredHttpException((int)response.StatusCode);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private static IReadOnlyList<MarketEventVintageRow> ParseObservations(string json)
        {
            var root = JObject.Parse(json);
            var rows = new List<MarketEventVintageRow>();
            foreach (var row in (root["observations"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string value = row["value"]?.ToString() ?? ".";
                if (value == "." || value.Length == 0) continue;
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                    continue;
                if (DateOnly.TryParseExact(row["date"]?.ToString(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var period)
                    && DateOnly.TryParseExact(row["realtime_start"]?.ToString(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                {
                    var end = DateOnly.TryParseExact(row["realtime_end"]?.ToString(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd)
                        ? parsedEnd
                        : new DateOnly(9999, 12, 31);
                    rows.Add(new MarketEventVintageRow(period, parsed, start, end));
                }
            }
            return rows;
        }

        /// <summary>
        /// Writes what was fetched, never how it was fetched. The key is not in this document.
        /// </summary>
        private void SaveCache()
        {
            try
            {
                var releases = new JArray();
                foreach (var e in Events)
                {
                    string source = e.Id.Split('-')[0];
                    if (!ReleaseIds.ContainsKey(source)) continue;
                    var row = new JObject
                    {
                        ["source"] = source,
                        ["date"] = e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    };
                    if (e.Figures is { } figures)
                    {
                        row["figures"] = new JArray(figures.Figures.Select(f => new JArray(
                            f.Name, f.Actual, f.Previous, (int)f.Unit, (int)f.Direction)));
                        if (figures.EstimateNumber is int estimate) row["estimate"] = estimate;
                        if (figures.Period is DateOnly period)
                            row["period"] = period.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    releases.Add(row);
                }

                var doc = new JObject
                {
                    ["fetchedUtc"] = (_fetchedUtc ?? AsUtc(_nowUtc())).ToString("O", CultureInfo.InvariantCulture),
                    ["releases"] = releases,
                };
                AtomicFile.WriteAllText(_cachePath, doc.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not write the market-events FRED cache: {Type}.",
                    ex.GetType().Name);
            }
        }

        private static MarketEventFigures? ReadFigures(JArray? rows, JObject release)
        {
            if (rows == null || rows.Count == 0) return null;
            var figures = new List<MarketEventFigure>(rows.Count);
            foreach (var row in rows.OfType<JArray>())
            {
                string name = row[0]?.ToString() ?? "";
                double? actual = row[1]?.Type == JTokenType.Null ? null : row[1]?.Value<double>();
                double? previous = row[2]?.Type == JTokenType.Null ? null : row[2]?.Value<double>();
                var unit = (MarketEventFigureUnit)(row[3]?.Value<int>() ?? 0);
                var direction = (MarketEventFigureDirection)(row[4]?.Value<int>() ?? 0);
                figures.Add(new MarketEventFigure(name, actual, previous, unit, direction));
            }
            int? estimate = release["estimate"]?.Value<int>();
            DateOnly? period = DateOnly.TryParseExact(release["period"]?.ToString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : null;
            return new MarketEventFigures(figures, estimate, period);
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        private sealed class FredHttpException : Exception
        {
            public FredHttpException(int statusCode) => StatusCode = statusCode;
            public int StatusCode { get; }
        }
    }
}
