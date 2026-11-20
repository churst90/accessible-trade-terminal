using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Headlines;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Market events, stage 4b: a company's earnings on its own charts. Finnhub's calendar gives the
/// day, the session and the figures against the estimates; EDGAR's results filing gives the
/// minute; the EPS history adds older quarters only where EDGAR dates them.
/// </summary>
public class MarketEventEarningsTests
{
    // ── Parsing (shapes as Finnhub's free key sent them on 2026-11-20, cut short) ──

    private const string CalendarJson = """
        {"earningsCalendar":[
          {"date":"2026-10-30","epsActual":1.85,"epsEstimate":1.77,"hour":"amc","quarter":4,"revenueActual":102466000000,"revenueEstimate":102227000000,"symbol":"AAPL","year":2026},
          {"date":"2026-10-30","epsActual":1.85,"epsEstimate":1.77,"hour":"amc","quarter":4,"revenueActual":102466000000,"revenueEstimate":102227000000,"symbol":"AAPL","year":2026},
          {"date":"2027-01-28","epsActual":null,"epsEstimate":2.6,"hour":"","quarter":1,"revenueActual":null,"revenueEstimate":138000000000,"symbol":"AAPL","year":2027},
          {"date":"not a date","hour":"bmo"}
        ]}
        """;

    private const string HistoryJson = """
        [{"actual":1.85,"estimate":1.77,"period":"2026-09-30","quarter":4,"surprise":0.08,"symbol":"AAPL","year":2026},
         {"actual":1.57,"estimate":1.43,"period":"2026-06-30","quarter":3,"surprise":0.14,"symbol":"AAPL","year":2026}]
        """;

    [Fact]
    public void TheCalendarIsReadOncePerDateInOrder()
    {
        var rows = FinnhubEarningsParser.ParseCalendar(CalendarJson);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 10, 30), rows[0].Date);
        Assert.Equal("amc", rows[0].Hour);
        Assert.Equal((4, 2026), (rows[0].Quarter, rows[0].Year));
        Assert.Equal(1.85, rows[0].EpsActual);
        Assert.Equal(102_227_000_000d, rows[0].RevenueEstimate);
        Assert.Null(rows[1].EpsActual);
        Assert.Equal("", rows[1].Hour);
    }

    [Fact]
    public void TheHistoryIsReadByQuarterEnd()
    {
        var quarters = FinnhubEarningsParser.ParseHistory(HistoryJson);

        Assert.Equal(new[] { new DateOnly(2026, 6, 30), new DateOnly(2026, 9, 30) },
            quarters.Select(q => q.Period));
        Assert.Equal(1.57, quarters[0].EpsActual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"error\":\"You don't have access\"}")]
    [InlineData("[{\"period\":7}]")]
    public void ABadBodyIsNothingNotAnError(string json)
    {
        Assert.Empty(FinnhubEarningsParser.ParseCalendar(json));
        Assert.Empty(FinnhubEarningsParser.ParseHistory(json));
    }

    // ── Building the events ──

    private static EdgarFiling Results(DateTime acceptedUtc, string form = "8-K", params string[] items)
        => new("AAPL", "Apple Inc.", form, items.Length == 0 ? new[] { "2.02", "9.01" } : items,
            acceptedUtc, "0000320193-26-" + acceptedUtc.Ticks % 100000, "https://www.sec.gov/");

    private static EarningsCalendarRow Row(DateOnly date, string hour = "amc", double? eps = 1.85,
        double? epsEstimate = 1.77, double? revenue = 102_466_000_000, double? revenueEstimate = 102_227_000_000,
        int quarter = 4, int year = 2026)
        => new(date, hour, quarter, year, epsEstimate, eps, revenueEstimate, revenue);

    private static readonly IReadOnlyList<EarningsQuarter> History = FinnhubEarningsParser.ParseHistory(HistoryJson);

    [Fact]
    public void ACalendarRowWithoutAFilingIsItsDayAndSession()
    {
        var e = Assert.Single(EarningsEvents.Build("aapl",
            new[] { Row(new DateOnly(2026, 10, 30)) }, History, Array.Empty<EdgarFiling>()));

        Assert.Equal("earnings.AAPL-2026-10-30", e.Id);
        Assert.Equal(MarketEventKind.Earnings, e.Kind);
        Assert.Equal("AAPL", e.Symbol);
        Assert.Equal("AAPL earnings", e.SpokenName);
        Assert.Equal("AAPL earnings, fiscal fourth quarter 2026", e.LongName);
        Assert.Equal(MarketEventPrecision.DayOnly, e.Precision);
        Assert.Equal(MarketEventSession.AfterClose, e.Session);
        // 16:00 in New York on a summer-time day.
        Assert.Equal(new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc), e.MomentUtc);
        Assert.Equal(new[] { MarketType.Stock }, e.Scope);
        Assert.Equal("Finnhub earnings calendar", e.Source);

        var eps = e.Figures!.Figures.Single(f => f.Name == EarningsEvents.EpsName);
        Assert.Equal((1.85, 1.77, 1.57), (eps.Actual, eps.Expected, eps.Previous));
        Assert.Equal(EarningsEvents.ExpectedBy, eps.ExpectedBy);
        Assert.Contains(e.Figures.Figures, f => f.Name == EarningsEvents.RevenueName && f.Unit == MarketEventFigureUnit.Money);
    }

    [Fact]
    public void TheResultsFilingMakesTheEventExactAtItsMinute()
    {
        var accepted = new DateTime(2026, 10, 30, 20, 30, 41, DateTimeKind.Utc);
        var e = Assert.Single(EarningsEvents.Build("AAPL",
            new[] { Row(new DateOnly(2026, 10, 30)) }, History,
            new[] { Results(accepted), Results(accepted.AddDays(-3), items: "5.02") }));

        Assert.Equal(MarketEventPrecision.Exact, e.Precision);
        Assert.Equal(accepted, e.MomentUtc);
        Assert.Equal(MarketEventSession.None, e.Session);
        Assert.Equal("Finnhub earnings calendar, time from SEC EDGAR", e.Source);
    }

    [Fact]
    public void AFilingOnAnotherNewYorkDayDoesNotTimeTheRow()
    {
        // 01:30 UTC on the 31st is still the 30th in New York; 04:30 UTC is not.
        var sameDay = EarningsEvents.Build("AAPL", new[] { Row(new DateOnly(2026, 10, 30)) }, History,
            new[] { Results(new DateTime(2026, 10, 31, 1, 30, 0, DateTimeKind.Utc)) });
        var nextDay = EarningsEvents.Build("AAPL", new[] { Row(new DateOnly(2026, 10, 30)) }, History,
            new[] { Results(new DateTime(2026, 10, 31, 4, 30, 0, DateTimeKind.Utc)) });

        Assert.Equal(MarketEventPrecision.Exact, Assert.Single(sameDay).Precision);
        Assert.Contains(nextDay, e => e.Date == new DateOnly(2026, 10, 30) && e.Precision == MarketEventPrecision.DayOnly);
    }

    [Fact]
    public void AnOlderQuarterIsPlacedOnlyWhereEdgarDatesIt()
    {
        var calendar = new[] { Row(new DateOnly(2026, 10, 30)) };

        var undated = EarningsEvents.Build("AAPL", calendar, History, Array.Empty<EdgarFiling>());
        Assert.Single(undated);

        var july = new DateTime(2026, 7, 31, 20, 30, 0, DateTimeKind.Utc);
        var dated = EarningsEvents.Build("AAPL", calendar, History, new[] { Results(july) });
        Assert.Equal(2, dated.Count);
        var older = dated[0];
        Assert.Equal("earnings.AAPL-2026-07-31", older.Id);
        Assert.Equal(MarketEventPrecision.Exact, older.Precision);
        Assert.Equal(july, older.MomentUtc);
        Assert.Equal("AAPL earnings, fiscal third quarter 2026", older.LongName);
        Assert.Equal("Finnhub EPS history, time from SEC EDGAR", older.Source);
        Assert.Equal(1.57, Assert.Single(older.Figures!.Figures).Actual);

        // A non-results 8-K and a filing past the reporting window date nothing.
        var notResults = EarningsEvents.Build("AAPL", calendar, History,
            new[] { Results(july, items: "8.01"), Results(new DateTime(2026, 12, 20, 15, 0, 0, DateTimeKind.Utc), form: "10-Q", items: "2.02") });
        Assert.Single(notResults);

        // A real results filing more than 100 days past every history quarter's end belongs to a
        // later quarter; it must not lend its date to an older one.
        var tooLate = EarningsEvents.Build("AAPL", calendar, History,
            new[] { Results(new DateTime(2027, 2, 5, 21, 30, 0, DateTimeKind.Utc)) });
        Assert.Single(tooLate);
    }

    [Fact]
    public void ABeforeTheOpenRowIsMidnightInNewYork()
    {
        var e = Assert.Single(EarningsEvents.Build("JPM",
            new[] { Row(new DateOnly(2027, 1, 14), hour: "bmo", eps: null) }, Array.Empty<EarningsQuarter>(),
            Array.Empty<EdgarFiling>()));

        Assert.Equal(MarketEventSession.BeforeOpen, e.Session);
        Assert.Equal(new DateTime(2027, 1, 14, 5, 0, 0, DateTimeKind.Utc), e.MomentUtc);
        Assert.Null(e.Figures!.Figures[0].Previous);
    }

    // ── Placement and speech ──

    /// <summary>Half-hour bars for 09:30–16:00 New York (summer time) on two days.</summary>
    private static IReadOnlyList<Ohlcv> SessionBars()
    {
        var bars = new List<Ohlcv>();
        foreach (var day in new[] { new DateTime(2026, 10, 30), new DateTime(2026, 11, 2) })
        {
            // 2 November is after the clocks went back: 09:30 New York is 14:30 UTC.
            int openHourUtc = day.Month == 10 ? 13 : 14;
            for (int i = 0; i < 13; i++)
                bars.Add(MarketEventTestData.Bar(day.AddHours(openHourUtc).AddMinutes(30 + 30 * i)));
        }
        return bars;
    }

    private static readonly DateTime Later = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("amc", 12, MarketEventPlacement.AfterTheClose)]
    [InlineData("bmo", 0, MarketEventPlacement.BeforeTheOpen)]
    [InlineData("", 0, null)]
    public void ADayOnlyReportGoesOnTheBarOfItsSession(string hour, int bar, string? word)
    {
        var e = Assert.Single(EarningsEvents.Build("AAPL", new[] { Row(new DateOnly(2026, 10, 30), hour) },
            History, Array.Empty<EdgarFiling>()));

        var placed = Assert.Single(MarketEventPlacement.Place(new[] { e }, SessionBars(), 1800,
            MarketEventChartZone.For(MarketType.Stock), Later));

        Assert.Equal(bar, placed.BarIndex);
        Assert.Equal(word, placed.Placement);
    }

    private static string Say(MarketEventBarPlacement placement)
        => MarketEventSpeech.Clause(new[] { placement }, 1800, MarketEventLevel.All, MarketEventKindFilter.All,
            MarketType.Stock);

    [Fact]
    public void TheBarSaysTheFiguresAgainstTheEstimatesAndTheSessionOnce()
    {
        var e = Assert.Single(EarningsEvents.Build("AAPL", new[] { Row(new DateOnly(2026, 10, 30)) },
            History, Array.Empty<EdgarFiling>()));

        string said = Say(MarketEventTestData.Placed(e, 12, MarketEventPlacement.AfterTheClose));

        Assert.Equal("AAPL earnings after the close, EPS 1.85, beat 1.77 expected, "
                     + "revenue 102.5 billion, beat 102.2 billion expected", said);
    }

    [Theory]
    [InlineData(1.70, 1.77, "EPS 1.70, missed 1.77 expected")]
    [InlineData(1.77, 1.77, "EPS 1.77, as expected")]
    [InlineData(1.85, null, "EPS 1.85")]
    public void EachSurpriseIsSaidInWords(double eps, double? estimate, string expected)
    {
        var e = Assert.Single(EarningsEvents.Build("AAPL",
            new[] { Row(new DateOnly(2026, 10, 30), eps: eps, epsEstimate: estimate, revenue: null, revenueEstimate: null) },
            History, Array.Empty<EdgarFiling>()));

        Assert.Equal(expected, MarketEventSpeech.FigureClause(e));
    }

    [Fact]
    public void TheDetailNamesTheEstimatesSourceAndThePreviousQuarter()
    {
        var e = Assert.Single(EarningsEvents.Build("AAPL", new[] { Row(new DateOnly(2026, 10, 30)) },
            History, Array.Empty<EdgarFiling>()));

        string detail = MarketEventSpeech.FigureDetail(e);

        Assert.Contains("Figure: EPS 1.85", detail);
        Assert.Contains("by analysts on Finnhub", detail);
        Assert.Contains("beat estimates", detail);
        Assert.Contains("Previous quarter: EPS 1.57.", detail);
        Assert.Contains("Revenue: 102.5 billion", detail);
    }

    [Fact]
    public void AnEarningsEventIsSkippedWhenItsBoxIsClear()
    {
        var e = Assert.Single(EarningsEvents.Build("AAPL", new[] { Row(new DateOnly(2026, 10, 30)) },
            History, Array.Empty<EdgarFiling>()));

        string said = MarketEventSpeech.Clause(new[] { MarketEventTestData.Placed(e, 12, MarketEventPlacement.AfterTheClose) },
            1800, MarketEventLevel.All, MarketEventKindFilter.All with { Earnings = false }, MarketType.Stock);

        Assert.Equal("", said);
    }

    // ── The bar map: one company's reports on its own chart only ──

    private sealed class FakeEarnings : IMarketEventEarningsFeed
    {
        public List<MarketEvent> List = new();
        public int Requests;
        public IReadOnlyList<MarketEvent> Events => List;
        public int Version { get; set; }
        public string Status => "fake";
        public void RequestRefresh(WorkspaceState state) => Requests++;
    }

    private static WorkspaceState StockChart(string symbol) =>
        MarketEventTestData.State(SessionBars(), MarketType.Stock, timeframe: "30m", level: "all", symbol: symbol)
            with { Identity = new ChartIdentity("Stock|US", "Alpaca", symbol, "30m") };

    [Fact]
    public void TheMapPlacesACompanysReportOnlyOnItsOwnChart()
    {
        var earnings = new FakeEarnings();
        earnings.List.AddRange(EarningsEvents.Build("AAPL", new[] { Row(new DateOnly(2026, 10, 30)) },
            History, Array.Empty<EdgarFiling>()));
        var cache = new MarketEventBarMapCache(
            new MarketEventCalendar(Array.Empty<MarketEvent>(), earningsFeed: earnings), earningsFeed: earnings);

        var onApple = cache.For(StockChart("AAPL"), Later);
        var onMicrosoft = cache.For(StockChart("MSFT"), Later);

        Assert.Contains(onApple, p => p.Event.Id == "earnings.AAPL-2026-10-30" && p.BarIndex == 12);
        Assert.DoesNotContain(onMicrosoft, p => p.Event.Kind == MarketEventKind.Earnings);
        Assert.Equal(2, earnings.Requests);
    }

    [Fact]
    public void ANewReadRebuildsTheMap()
    {
        var earnings = new FakeEarnings();
        var cache = new MarketEventBarMapCache(
            new MarketEventCalendar(Array.Empty<MarketEvent>(), earningsFeed: earnings), earningsFeed: earnings);
        var state = StockChart("AAPL");

        Assert.DoesNotContain(cache.For(state, Later), p => p.Event.Kind == MarketEventKind.Earnings);

        earnings.List.AddRange(EarningsEvents.Build("AAPL", new[] { Row(new DateOnly(2026, 10, 30)) },
            History, Array.Empty<EdgarFiling>()));
        earnings.Version++;

        Assert.Contains(cache.For(state, Later), p => p.Event.Kind == MarketEventKind.Earnings);
    }

    // ── The feed ──

    private sealed class FakeClient : IFinnhubEarningsClient
    {
        public EarningsReadStatus Status = EarningsReadStatus.Ok;
        public readonly List<string> Calendars = new();

        public Task<EarningsRead<EarningsCalendarRow>> CalendarAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            lock (Calendars) Calendars.Add(symbol);
            return Task.FromResult(Status == EarningsReadStatus.Ok
                ? new EarningsRead<EarningsCalendarRow>(Status, FinnhubEarningsParser.ParseCalendar(CalendarJson))
                : EarningsRead<EarningsCalendarRow>.Of(Status));
        }

        public Task<EarningsRead<EarningsQuarter>> HistoryAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult(new EarningsRead<EarningsQuarter>(EarningsReadStatus.Ok, FinnhubEarningsParser.ParseHistory(HistoryJson)));
    }

    private sealed class Filings : ICompanyFilingSource
    {
        public Task<IReadOnlyList<EdgarFiling>?> RecentFilingsAsync(string ticker, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EdgarFiling>?>(new[] { Results(new DateTime(2026, 10, 30, 20, 30, 41, DateTimeKind.Utc)) });
        public Task<bool> IsFilerAsync(string ticker, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc));
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task TheFeedReadsEachOpenStockOnceAndTimesItFromEdgar()
    {
        var client = new FakeClient();
        var clock = new Clock();
        var feed = new MarketEventEarningsFeed(client, new Filings(), clock);
        Assert.Equal("Earnings: no stock chart read yet.", feed.Status);

        feed.RequestRefresh(StockChart("AAPL"));
        await feed.LastFill;
        feed.RequestRefresh(StockChart("AAPL"));
        await feed.LastFill;

        Assert.Equal(new[] { "AAPL" }, client.Calendars);
        Assert.Equal(1, feed.Version);
        Assert.Equal("Earnings: Finnhub, read for 1 company.", feed.Status);
        var reported = feed.Events.Single(e => e.Date == new DateOnly(2026, 10, 30));
        Assert.Equal(MarketEventPrecision.Exact, reported.Precision);

        clock.Now += MarketEventEarningsFeed.Refresh;
        feed.RequestRefresh(StockChart("AAPL"));
        await feed.LastFill;
        Assert.Equal(2, client.Calendars.Count);
    }

    [Fact]
    public async Task TheFeedReadsNothingWhenEventsAreOffOrEarningsClearOrTheChartIsACoin()
    {
        var client = new FakeClient();
        var feed = new MarketEventEarningsFeed(client, new Filings(), new Clock());

        feed.RequestRefresh(StockChart("AAPL") with { MarketEventsLevel = "off" });
        feed.RequestRefresh(StockChart("AAPL") with { MarketEventsEarnings = false });
        feed.RequestRefresh(MarketEventTestData.State(SessionBars(), MarketType.Crypto, level: "all", symbol: "BTCUSDT")
            with { Identity = new ChartIdentity("Crypto|Spot", "MEXC", "BTCUSDT", "30m") });
        await feed.LastFill;

        Assert.Empty(client.Calendars);
    }

    [Fact]
    public async Task NoKeyIsSaidAndTriedAgainSoon()
    {
        var client = new FakeClient { Status = EarningsReadStatus.NoKey };
        var clock = new Clock();
        var feed = new MarketEventEarningsFeed(client, new Filings(), clock);

        feed.RequestRefresh(StockChart("AAPL"));
        await feed.LastFill;
        Assert.Equal("Earnings unavailable, Finnhub key not set.", feed.Status);
        Assert.Empty(feed.Events);

        // A key added in API Keys is picked up within minutes, not hours.
        client.Status = EarningsReadStatus.Ok;
        clock.Now += MarketEventEarningsFeed.RetryAfterFailure;
        feed.RequestRefresh(StockChart("AAPL"));
        await feed.LastFill;
        Assert.Equal(2, client.Calendars.Count);
        Assert.NotEmpty(feed.Events);
    }

    [Fact]
    public void TheOpenStockTabsAreReadToo()
    {
        var state = StockChart("aapl") with
        {
            TabSnapshots = System.Collections.Immutable.ImmutableList.Create(
                TabOf(new ChartIdentity("Stock|US", "Alpaca", "MSFT", "1d")),
                TabOf(new ChartIdentity("Crypto|Spot", "MEXC", "BTCUSDT", "1h"))),
        };

        Assert.Equal(new[] { "AAPL", "MSFT" }, MarketEventEarningsFeed.StockSymbols(state).OrderBy(s => s));
    }

    private static TabSnapshot TabOf(ChartIdentity identity)
    {
        var s = WorkspaceState.Initial;
        return new TabSnapshot(1, identity, s.Data, s.ActiveSeries, s.FocusedSeriesIndex, s.FocusedSeriesId,
            s.FocusedComponentIndex, s.FocusedBinIndex, s.CurrentDataIndex, s.ViewportStartIndex, s.ViewportLength,
            s.RightMarginBars, s.ViewportRange, s.PaneRanges, false, false, s.LastInteractionContext,
            s.PaneHeightRatios, InitializationStatus.Ready, DataStatus.Ready, false, null, 0, -1);
    }
}
