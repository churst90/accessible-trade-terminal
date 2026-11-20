using System.Globalization;
using System.Net;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Headlines;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// One row of Finnhub's earnings calendar (<c>calendar/earnings?symbol=</c>), as its free key
    /// sends it (checked 2026-11-20 with Cody's key): the report <see cref="Date"/>, the
    /// <see cref="Hour"/> ("bmo" before the open, "amc" after the close, "dmh" during market
    /// hours, or empty when not announced), the FISCAL quarter and year, and the analysts' EPS and
    /// revenue estimates beside the actuals once reported. For one symbol it lists only the most
    /// recent report and the upcoming ones; the rows arrive twice over, so they are kept once per
    /// date.
    /// </summary>
    public sealed record EarningsCalendarRow(
        DateOnly Date,
        string Hour,
        int Quarter,
        int Year,
        double? EpsEstimate,
        double? EpsActual,
        double? RevenueEstimate,
        double? RevenueActual);

    /// <summary>
    /// One quarter of Finnhub's EPS history (<c>stock/earnings?symbol=</c>): the last four
    /// quarters' actual and estimated EPS, named by the quarter's END (<see cref="Period"/>, e.g.
    /// 2026-09-30) and fiscal quarter and year. It carries no report date: a quarter is placed on
    /// a chart only when EDGAR has its results filing.
    /// </summary>
    public sealed record EarningsQuarter(
        DateOnly Period,
        int Quarter,
        int Year,
        double? EpsEstimate,
        double? EpsActual);

    /// <summary>Both Finnhub earnings shapes, parsed without a network so fixtures pin them.
    /// A malformed body or row is skipped, never thrown.</summary>
    public static class FinnhubEarningsParser
    {
        public static IReadOnlyList<EarningsCalendarRow> ParseCalendar(string json)
        {
            var rows = new Dictionary<DateOnly, EarningsCalendarRow>();
            JToken? root;
            try { root = JToken.Parse(json); }
            catch (JsonException) { return Array.Empty<EarningsCalendarRow>(); }
            if (root is not JObject body || body["earningsCalendar"] is not JArray items)
                return Array.Empty<EarningsCalendarRow>();
            foreach (var m in items.OfType<JObject>())
            {
                if (!DateOnly.TryParseExact((string?)m["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date)) continue;
                rows[date] = new EarningsCalendarRow(
                    date,
                    ((string?)m["hour"] ?? "").Trim().ToLowerInvariant(),
                    Int(m["quarter"]),
                    Int(m["year"]),
                    Number(m["epsEstimate"]),
                    Number(m["epsActual"]),
                    Number(m["revenueEstimate"]),
                    Number(m["revenueActual"]));
            }
            return rows.Values.OrderBy(r => r.Date).ToList();
        }

        public static IReadOnlyList<EarningsQuarter> ParseHistory(string json)
        {
            var quarters = new Dictionary<DateOnly, EarningsQuarter>();
            JToken? root;
            try { root = JToken.Parse(json); }
            catch (JsonException) { return Array.Empty<EarningsQuarter>(); }
            if (root is not JArray items) return Array.Empty<EarningsQuarter>();
            foreach (var m in items.OfType<JObject>())
            {
                if (!DateOnly.TryParseExact((string?)m["period"], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var period)) continue;
                quarters[period] = new EarningsQuarter(
                    period, Int(m["quarter"]), Int(m["year"]),
                    Number(m["estimate"]), Number(m["actual"]));
            }
            return quarters.Values.OrderBy(q => q.Period).ToList();
        }

        private static double? Number(JToken? t)
            => t is { Type: JTokenType.Float or JTokenType.Integer } ? (double)t : null;

        private static int Int(JToken? t)
            => t is { Type: JTokenType.Integer } ? (int)t : 0;
    }

    public enum EarningsReadStatus { Ok, NoKey, Refused, RateLimited, Failed }

    public sealed record EarningsRead<T>(EarningsReadStatus Status, IReadOnlyList<T> Rows)
    {
        public static EarningsRead<T> Of(EarningsReadStatus status) => new(status, Array.Empty<T>());
    }

    /// <summary>Finnhub's two earnings reads, so the tests can drive the feed without a network.
    /// Production is <see cref="FinnhubEarningsHttpClient"/>.</summary>
    public interface IFinnhubEarningsClient
    {
        Task<EarningsRead<EarningsCalendarRow>> CalendarAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);
        Task<EarningsRead<EarningsQuarter>> HistoryAsync(string symbol, CancellationToken ct = default);
    }

    /// <summary>
    /// Finnhub's earnings with the Finnhub key from API Keys, in the <c>X-Finnhub-Token</c>
    /// header as the news reads send it — never in a URL. No key, no request.
    /// </summary>
    public sealed class FinnhubEarningsHttpClient : IFinnhubEarningsClient
    {
        private readonly HttpClient _http;
        private readonly IApiKeyCheckout _keys;
        private readonly ILogger<FinnhubEarningsHttpClient>? _logger;

        public FinnhubEarningsHttpClient(HttpClient http, IApiKeyCheckout keys, ILogger<FinnhubEarningsHttpClient>? logger = null)
        {
            _http = http;
            _keys = keys;
            _logger = logger;
        }

        public Task<EarningsRead<EarningsCalendarRow>> CalendarAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
            => ReadAsync($"{FinnhubNewsHttpClient.RestUrl}/calendar/earnings?symbol={Uri.EscapeDataString(symbol)}"
                         + $"&from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                         + $"&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
                FinnhubEarningsParser.ParseCalendar, ct);

        public Task<EarningsRead<EarningsQuarter>> HistoryAsync(string symbol, CancellationToken ct = default)
            => ReadAsync($"{FinnhubNewsHttpClient.RestUrl}/stock/earnings?symbol={Uri.EscapeDataString(symbol)}",
                FinnhubEarningsParser.ParseHistory, ct);

        private async Task<EarningsRead<T>> ReadAsync<T>(string url, Func<string, IReadOnlyList<T>> parse, CancellationToken ct)
        {
            var key = await _keys.CheckoutAsync("Finnhub", ct: ct).ConfigureAwait(false);
            if (!key.HasCredentials) return EarningsRead<T>.Of(EarningsReadStatus.NoKey);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Finnhub-Token", key.Key);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return EarningsRead<T>.Of(EarningsReadStatus.Refused);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return EarningsRead<T>.Of(EarningsReadStatus.RateLimited);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("Finnhub earnings read returned {Status}.", (int)response.StatusCode);
                    return EarningsRead<T>.Of(EarningsReadStatus.Failed);
                }
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new EarningsRead<T>(EarningsReadStatus.Ok, parse(json));
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                _logger?.LogDebug("Finnhub earnings read failed: {Type}.", ex.GetType().Name);
                return EarningsRead<T>.Of(EarningsReadStatus.Failed);
            }
        }
    }

    /// <summary>
    /// Builds a company's earnings events from what the three reads know (stage 4b), pure so
    /// fixtures can pin it.
    ///
    /// <para>
    /// <b>The calendar rows</b> give the report day, before the open or after the close, and EPS
    /// and revenue against the analysts' estimates. <b>EDGAR's results filing</b> (an 8-K with
    /// item 2.02) gives the exact minute the numbers became public: a row whose New York date
    /// matches a filing's acceptance becomes an exact event at that minute. <b>The EPS history</b>
    /// adds the earlier quarters, but only where EDGAR has the filing that dates them (accepted
    /// within <see cref="ReportWithinDays"/> of the quarter's end) — a quarter with no date is
    /// never guessed onto a bar. Each quarter's EPS is said beside the one before it.
    /// </para>
    /// </summary>
    public static class EarningsEvents
    {
        public const int ReportWithinDays = 100;
        public const string ExpectedBy = "analysts on Finnhub";
        public const string EpsName = "EPS";
        public const string RevenueName = "revenue";

        private static readonly IReadOnlyList<MarketType> StockScope = new[] { MarketType.Stock };

        public static IReadOnlyList<MarketEvent> Build(
            string symbol,
            IReadOnlyList<EarningsCalendarRow> calendar,
            IReadOnlyList<EarningsQuarter> history,
            IReadOnlyList<EdgarFiling> filings)
        {
            symbol = symbol.ToUpperInvariant();
            var results = filings
                .Where(f => f.Form.StartsWith("8-K", StringComparison.OrdinalIgnoreCase) && f.Items.Contains("2.02"))
                .OrderBy(f => f.AcceptedUtc)
                .ToList();
            var byDate = new Dictionary<DateOnly, MarketEvent>();

            foreach (var row in calendar)
            {
                var filing = results.FirstOrDefault(f => NewYorkDate(f.AcceptedUtc) == row.Date);
                var previous = PreviousEps(history, row.Quarter, row.Year);
                var figures = new List<MarketEventFigure>
                {
                    new(EpsName, row.EpsActual, previous, MarketEventFigureUnit.PerShare,
                        MarketEventFigureDirection.HigherIsStronger, row.EpsEstimate, ExpectedBy),
                };
                if (row.RevenueActual != null || row.RevenueEstimate != null)
                    figures.Add(new(RevenueName, row.RevenueActual, null, MarketEventFigureUnit.Money,
                        MarketEventFigureDirection.HigherIsStronger, row.RevenueEstimate, ExpectedBy));
                var session = row.Hour switch
                {
                    "bmo" => MarketEventSession.BeforeOpen,
                    "amc" => MarketEventSession.AfterClose,
                    _ => MarketEventSession.None,
                };
                byDate[row.Date] = Make(symbol, row.Date, row.Quarter, row.Year, filing, session, figures,
                    filing == null ? "Finnhub earnings calendar" : "Finnhub earnings calendar, time from SEC EDGAR");
            }

            foreach (var q in history)
            {
                var filing = results.FirstOrDefault(f =>
                    NewYorkDate(f.AcceptedUtc) > q.Period
                    && NewYorkDate(f.AcceptedUtc) <= q.Period.AddDays(ReportWithinDays));
                if (filing == null) continue;
                var date = NewYorkDate(filing.AcceptedUtc);
                if (byDate.ContainsKey(date)) continue;
                var figures = new List<MarketEventFigure>
                {
                    new(EpsName, q.EpsActual, PreviousEps(history, q.Quarter, q.Year),
                        MarketEventFigureUnit.PerShare, MarketEventFigureDirection.HigherIsStronger,
                        q.EpsEstimate, ExpectedBy),
                };
                byDate[date] = Make(symbol, date, q.Quarter, q.Year, filing, MarketEventSession.None, figures,
                    "Finnhub EPS history, time from SEC EDGAR");
            }

            return byDate.Values.OrderBy(e => e.MomentUtc).ToList();
        }

        private static MarketEvent Make(
            string symbol, DateOnly date, int quarter, int year, EdgarFiling? filing,
            MarketEventSession session, IReadOnlyList<MarketEventFigure> figures, string source)
        {
            bool exact = filing != null;
            var moment = exact
                ? filing!.AcceptedUtc
                : Eastern(date, session == MarketEventSession.AfterClose ? new TimeOnly(16, 0) : TimeOnly.MinValue);
            return new MarketEvent(
                // "earnings.AAPL-2026-11-02": the calendar's history keys a source on the text
                // before the first dash, so one company's reports are one source.
                Id: $"earnings.{symbol}-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
                Kind: MarketEventKind.Earnings,
                SpokenName: $"{symbol} earnings",
                LongName: $"{symbol} earnings{FiscalQuarter(quarter, year)}",
                Scope: StockScope,
                Importance: MarketEventImportance.High,
                MomentUtc: moment,
                Precision: exact ? MarketEventPrecision.Exact : MarketEventPrecision.DayOnly,
                Date: date,
                Source: source,
                Figures: new MarketEventFigures(figures, null, null),
                Symbol: symbol,
                Session: exact ? MarketEventSession.None : session);
        }

        /// <summary>The quarter before (<paramref name="quarter"/>, <paramref name="year"/>)'s
        /// actual EPS, when the history holds it.</summary>
        private static double? PreviousEps(IReadOnlyList<EarningsQuarter> history, int quarter, int year)
        {
            if (quarter < 1) return null;
            int pq = quarter == 1 ? 4 : quarter - 1;
            int py = quarter == 1 ? year - 1 : year;
            return history.FirstOrDefault(h => h.Quarter == pq && h.Year == py)?.EpsActual;
        }

        private static string FiscalQuarter(int quarter, int year)
        {
            if (quarter is < 1 or > 4 || year <= 0) return "";
            string word = quarter switch { 1 => "first", 2 => "second", 3 => "third", _ => "fourth" };
            return $", fiscal {word} quarter {year}";
        }

        internal static DateOnly NewYorkDate(DateTime utc)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc), ExchangeTime.UsEastern));

        private static DateTime Eastern(DateOnly day, TimeOnly time)
            => TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Unspecified), ExchangeTime.UsEastern);
    }

    /// <summary>The surface the calendar, the bar map and the settings read.</summary>
    public interface IMarketEventEarningsFeed
    {
        /// <summary>Every company's earnings read so far, merged into the calendar.</summary>
        IReadOnlyList<MarketEvent> Events { get; }

        /// <summary>Bumped whenever a company's events change; the bar-map key watches it.</summary>
        int Version { get; }

        /// <summary>The settings and calendar status line; never a network call.</summary>
        string Status { get; }

        /// <summary>Starts a background read for each stock chart open that is due one, gated by
        /// the events level and the Earnings tick box.</summary>
        void RequestRefresh(WorkspaceState state);
    }

    /// <summary>
    /// Earnings for the stocks on open charts: one read per company per <see cref="Refresh"/>
    /// (a failed one is tried again after <see cref="RetryAfterFailure"/>), started by the bar
    /// map or the calendar, never while market events are Off or the Earnings box is clear. The
    /// Finnhub half needs the Finnhub key; EDGAR's filing times need none, but without Finnhub
    /// there is nothing to put them on. Kept in memory: a restart reads again.
    /// </summary>
    public sealed class MarketEventEarningsFeed : IMarketEventEarningsFeed
    {
        public static readonly TimeSpan Refresh = TimeSpan.FromHours(6);
        public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);
        public const int CalendarDaysBack = 120;
        public const int CalendarDaysAhead = 120;

        private readonly IFinnhubEarningsClient _client;
        private readonly ICompanyFilingSource? _filings;
        private readonly TimeProvider _time;
        private readonly ILogger<MarketEventEarningsFeed>? _logger;
        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _lastUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<MarketEvent>> _bySymbol = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<MarketEvent> _events = Array.Empty<MarketEvent>();
        private int _version;
        private EarningsReadStatus? _lastStatus;

        public MarketEventEarningsFeed(
            IFinnhubEarningsClient client,
            ICompanyFilingSource? filings = null,
            TimeProvider? timeProvider = null,
            ILogger<MarketEventEarningsFeed>? logger = null)
        {
            _client = client;
            _filings = filings;
            _time = timeProvider ?? TimeProvider.System;
            _logger = logger;
        }

        public IReadOnlyList<MarketEvent> Events => Volatile.Read(ref _events);
        public int Version => Volatile.Read(ref _version);

        /// <summary>The reads started by the last refresh, for the tests to await.</summary>
        internal Task LastFill { get; private set; } = Task.CompletedTask;

        public string Status
        {
            get
            {
                lock (_lock)
                {
                    return _lastStatus switch
                    {
                        null => "Earnings: no stock chart read yet.",
                        EarningsReadStatus.NoKey => "Earnings unavailable, Finnhub key not set.",
                        EarningsReadStatus.Refused => "Earnings unavailable, Finnhub refused the key.",
                        EarningsReadStatus.RateLimited => "Earnings: Finnhub is limiting requests; trying again shortly.",
                        EarningsReadStatus.Failed => "Earnings: Finnhub not reached; trying again shortly.",
                        _ => $"Earnings: Finnhub, read for {_bySymbol.Count} "
                             + (_bySymbol.Count == 1 ? "company." : "companies."),
                    };
                }
            }
        }

        public void RequestRefresh(WorkspaceState state)
        {
            if (MarketEventSettings.Level(state) == MarketEventLevel.Off) return;
            if (!MarketEventSettings.Kinds(state).Earnings) return;
            var now = _time.GetUtcNow().UtcDateTime;
            var due = new List<string>();
            lock (_lock)
            {
                foreach (string symbol in StockSymbols(state))
                {
                    if (_lastUtc.TryGetValue(symbol, out var last) && now - last < Refresh) continue;
                    _lastUtc[symbol] = now;
                    due.Add(symbol);
                }
            }
            if (due.Count == 0) return;
            LastFill = Task.Run(async () =>
            {
                foreach (string symbol in due) await FillAsync(symbol, now).ConfigureAwait(false);
            });
        }

        /// <summary>The stock symbols on the focused chart and every open tab, as EDGAR keys them.</summary>
        public static IReadOnlyCollection<string> StockSymbols(WorkspaceState state)
        {
            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (MarketEventLiveNotices.MarketOfChart(state) == MarketType.Stock)
                Add(state.Identity.Symbol);
            foreach (var tab in state.TabSnapshots ?? System.Collections.Immutable.ImmutableList<TabSnapshot>.Empty)
                if (string.Equals(MarketKey.Category(tab.Identity.Market ?? ""), nameof(MarketType.Stock),
                        StringComparison.OrdinalIgnoreCase))
                    Add(tab.Identity.Symbol);
            return symbols;

            void Add(string? symbol)
            {
                string s = EdgarCompanyProfileSource.Normalise(symbol ?? "");
                if (s.Length > 0) symbols.Add(s);
            }
        }

        /// <summary>The symbol a chart's earnings are keyed on, or null for a chart that is not a stock.</summary>
        public static string? ChartSymbol(WorkspaceState state)
            => MarketEventLiveNotices.MarketOfChart(state) == MarketType.Stock
                ? EdgarCompanyProfileSource.Normalise(state.Identity.Symbol ?? "")
                : null;

        private async Task FillAsync(string symbol, DateTime now)
        {
            EarningsReadStatus status;
            try
            {
                var today = EarningsEvents.NewYorkDate(now);
                var calendar = await _client.CalendarAsync(symbol,
                    today.AddDays(-CalendarDaysBack), today.AddDays(CalendarDaysAhead)).ConfigureAwait(false);
                status = calendar.Status;
                if (status == EarningsReadStatus.Ok)
                {
                    var history = await _client.HistoryAsync(symbol).ConfigureAwait(false);
                    IReadOnlyList<EdgarFiling> filings = Array.Empty<EdgarFiling>();
                    if (_filings != null && await _filings.IsFilerAsync(symbol).ConfigureAwait(false))
                        filings = await _filings.RecentFilingsAsync(symbol).ConfigureAwait(false)
                                  ?? Array.Empty<EdgarFiling>();
                    var events = EarningsEvents.Build(symbol, calendar.Rows,
                        history.Status == EarningsReadStatus.Ok ? history.Rows : Array.Empty<EarningsQuarter>(),
                        filings);
                    lock (_lock)
                    {
                        _bySymbol[symbol] = events;
                        Volatile.Write(ref _events, _bySymbol.Values.SelectMany(e => e).ToList());
                        Interlocked.Increment(ref _version);
                    }
                }
            }
            catch (Exception ex)
            {
                status = EarningsReadStatus.Failed;
                _logger?.LogDebug("Earnings for a chart could not be read: {Type}.", ex.GetType().Name);
            }

            lock (_lock)
            {
                _lastStatus = status;
                // Anything short of a good read is tried again soon rather than in six hours:
                // a key added in API Keys is picked up within minutes (no key sends nothing).
                if (status != EarningsReadStatus.Ok)
                    _lastUtc[symbol] = now - Refresh + RetryAfterFailure;
            }
        }
    }
}
