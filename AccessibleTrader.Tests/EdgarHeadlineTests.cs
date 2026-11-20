using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Core.Services.Headlines;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// Headlines, stage 1 (SEC filings). The parsers read fixtures captured from EDGAR on 2026-11-20
/// (Apple's Atom feed and a trimmed submissions document); the poller runs on a fake clock and a
/// fake filing source. No network, no speech, no sound.
/// </summary>
public class EdgarHeadlineTests
{
    private static string Fixture(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static EdgarFiling Filing(string form, DateTime acceptedUtc, string accession,
        params string[] items)
        => new("AAPL", "Apple Inc.", form, items, acceptedUtc, accession, "https://www.sec.gov/x");

    // ── Parsers ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheAtomFeedReadsFormsItemsAndTheAcceptanceTimeInUtc()
    {
        var list = EdgarFilingParser.ParseAtom(Fixture("edgar_aapl_atom_2026-11-20.xml"), "AAPL");

        Assert.Equal(10, list.Count);
        Assert.All(list, f => Assert.Equal("Apple Inc.", f.Company));
        Assert.Equal("4", list[0].Form);
        Assert.Equal("0001140361-26-044952", list[0].Accession);
        // <updated>2026-11-19T18:30:21-05:00</updated>
        Assert.Equal(new DateTime(2026, 11, 19, 23, 30, 21, DateTimeKind.Utc), list[0].AcceptedUtc);
        Assert.Equal(DateTimeKind.Utc, list[0].AcceptedUtc.Kind);
        Assert.Empty(list[0].Items);

        var eightK = Assert.Single(list, f => f.Form == "8-K");
        // "items 8.01 and 9.01" in words, read back out as numbers.
        Assert.Equal(new[] { "8.01", "9.01" }, eightK.Items);
        Assert.Equal(new DateTime(2026, 11, 9, 21, 30, 40, DateTimeKind.Utc), eightK.AcceptedUtc);
        Assert.StartsWith("https://www.sec.gov/Archives/edgar/data/320193/", eightK.Url);
    }

    [Fact]
    public void BothShapesAgreeOnTheSameFilings()
    {
        var atom = EdgarFilingParser.ParseAtom(Fixture("edgar_aapl_atom_2026-11-20.xml"), "AAPL");
        var json = EdgarFilingParser.ParseSubmissions(
            JObject.Parse(Fixture("edgar_aapl_submissions_2026-11-20.json")), "AAPL", 25);

        foreach (var a in atom)
        {
            var j = Assert.Single(json, f => f.Accession == a.Accession);
            Assert.Equal(a.Form, j.Form);
            Assert.Equal(a.AcceptedUtc, j.AcceptedUtc);
            Assert.Equal(a.Items, j.Items);
        }
    }

    [Fact]
    public void TheSubmissionsListStopsAtMaxAndReadsAnEarningsReport()
    {
        var doc = JObject.Parse(Fixture("edgar_aapl_submissions_2026-11-20.json"));
        Assert.Equal(5, EdgarFilingParser.ParseSubmissions(doc, "AAPL", 5).Count);

        var all = EdgarFilingParser.ParseSubmissions(doc, "AAPL", 25);
        var results = Assert.Single(all, f => f.Accession == "0000320193-26-000024");
        Assert.Equal("8-K", results.Form);
        Assert.Contains("2.02", results.Items);
        // 4:30 PM New York on 2 November (after the clocks went back).
        Assert.Equal(new DateTime(2026, 11, 2, 21, 30, 23, DateTimeKind.Utc), results.AcceptedUtc);
        Assert.StartsWith("https://www.sec.gov/Archives/edgar/data/320193/000032019326000024/", results.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><updated>nope</updated></entry></feed>")]
    public void ABrokenFeedIsEmptyNotAnException(string xml)
        => Assert.Empty(EdgarFilingParser.ParseAtom(xml, "AAPL"));

    // ── Words ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("8-K", "2.02,9.01", MarketEventImportance.High)]
    [InlineData("8-K", "8.01,9.01", MarketEventImportance.Medium)]
    [InlineData("8-K", "5.07", MarketEventImportance.Low)]
    [InlineData("8-K", "", MarketEventImportance.Medium)]
    [InlineData("8-K/A", "4.02", MarketEventImportance.High)]
    [InlineData("10-Q", "", MarketEventImportance.Medium)]
    [InlineData("4", "", MarketEventImportance.Low)]
    [InlineData("SC TO-T", "", MarketEventImportance.High)]
    public void ImportanceIsTheMostImportantItemOrTheFormsRow(
        string form, string items, MarketEventImportance expected)
        => Assert.Equal(expected, EdgarFilingWords.Importance(form, Split(items)));

    [Theory]
    [InlineData("424B2")]
    [InlineData("FWP")]
    [InlineData("25-NSE")]
    [InlineData("DEFA14A")]
    public void AFormWithNoRowIsNeverAnnounced(string form)
        => Assert.Null(EdgarFilingWords.Importance(form, Array.Empty<string>()));

    [Theory]
    [InlineData("8-K", "8.01,9.01", "AAPL filed an 8-K: other events.")]
    [InlineData("8-K", "9.01", "AAPL filed an 8-K: financial statements and exhibits.")]
    [InlineData("8-K", "", "AAPL filed an 8-K: current report.")]
    [InlineData("8-K", "2.02,5.02,9.01", "AAPL filed an 8-K: earnings results, director or officer change.")]
    [InlineData("10-Q/A", "", "AAPL filed an amended 10-Q: quarterly report.")]
    [InlineData("4", "", "AAPL filed a Form 4: insider trade.")]
    [InlineData("424B2", "", "AAPL filed a 424B2.")]
    public void TheSentenceSaysTheFormAndWhatItIsAbout(string form, string items, string expected)
        => Assert.Equal(expected, EdgarHeadlineWording.Sentence("AAPL",
            Filing(form, DateTime.UtcNow, "a", Split(items))));

    [Theory]
    [InlineData("8-K", "2.02,9.01", "8-K: earnings results")]
    [InlineData("4", "", "Form 4: insider trade")]
    [InlineData("424B2", "", "424B2")]
    public void TheDossierLabelDropsTheArticle(string form, string items, string expected)
        => Assert.Equal(expected, EdgarFilingWords.Label(form, Split(items)));

    private static string[] Split(string items)
        => items.Length == 0 ? Array.Empty<string>() : items.Split(',');

    // ── Tracker ──────────────────────────────────────────────────────────────

    private static readonly DateTime Now = new(2026, 11, 2, 21, 31, 0, DateTimeKind.Utc);

    [Fact]
    public void TheFirstLookIsABaselineExceptWhatJustCameOut()
    {
        var tracker = new EdgarHeadlineTracker();
        var fresh = tracker.Observe("AAPL", new[]
        {
            Filing("8-K", Now.AddSeconds(-37), "results", "2.02"),
            Filing("4", Now.AddMinutes(-6), "old-form-4"),
            Filing("10-K", Now.AddDays(-30), "annual"),
        }, Now);

        Assert.Equal("results", Assert.Single(fresh).Accession);
        Assert.True(tracker.Knows("AAPL"));
    }

    [Fact]
    public void LaterLooksSayOnlyWhatIsNewAndNotTooLateOldestFirst()
    {
        var tracker = new EdgarHeadlineTracker();
        var a = Filing("4", Now.AddDays(-1), "a");
        tracker.Observe("AAPL", new[] { a }, Now);

        var b = Filing("8-K", Now.AddMinutes(-1), "b", "8.01");
        var c = Filing("4", Now.AddMinutes(-90), "c");
        var stale = Filing("4", Now.AddHours(-3), "stale");
        var fresh = tracker.Observe("AAPL", new[] { b, c, stale, a }, Now);

        Assert.Equal(new[] { "c", "b" }, fresh.Select(f => f.Accession));
        // Every one is remembered: the stale one is not said on the next look either.
        Assert.Empty(tracker.Observe("AAPL", new[] { b, c, stale, a }, Now.AddMinutes(1)));
    }

    [Fact]
    public void KeepForgetsASymbolSoItsNextLookIsABaselineAgain()
    {
        var tracker = new EdgarHeadlineTracker();
        tracker.Observe("AAPL", new[] { Filing("4", Now.AddDays(-1), "a") }, Now);
        tracker.Observe("MSFT", new[] { Filing("4", Now.AddDays(-1), "m") }, Now);

        tracker.Keep(new[] { "msft" });

        Assert.False(tracker.Knows("AAPL"));
        Assert.True(tracker.Knows("MSFT"));
        // AAPL comes back: its 30-minute-old filing is part of the new baseline, not news.
        Assert.Empty(tracker.Observe("AAPL", new[] { Filing("4", Now.AddMinutes(-30), "b") }, Now));
    }

    // ── Wording ──────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeLeadsWithTheMostImportantAndCountsTheRest()
    {
        var fresh = new[]
        {
            Filing("4", Now.AddMinutes(-2), "f1"),
            Filing("8-K", Now.AddMinutes(-1), "results", "2.02", "9.01"),
            Filing("4", Now.AddMinutes(-1), "f2"),
            Filing("424B2", Now.AddMinutes(-1), "prospectus"),
        };

        var h = EdgarHeadlineWording.Compose("AAPL", fresh, MarketEventLevel.All, Now)!;

        Assert.Equal("AAPL filed an 8-K: earnings results. 2 more filings.", h.Text);
        Assert.Equal(MarketEventImportance.High, h.Importance);
        Assert.Equal("edgar|results", h.Id);
        Assert.Equal("AAPL", h.Symbol);
    }

    [Fact]
    public void TheLevelFiltersBeforeAnythingIsCounted()
    {
        var fresh = new[]
        {
            Filing("4", Now.AddMinutes(-2), "f1"),
            Filing("10-Q", Now.AddMinutes(-1), "q"),
        };

        Assert.Null(EdgarHeadlineWording.Compose("AAPL", fresh, MarketEventLevel.ImportantOnly, Now));
        Assert.Equal("AAPL filed a 10-Q: quarterly report.",
            EdgarHeadlineWording.Compose("AAPL", fresh, MarketEventLevel.ImportantAndMedium, Now)!.Text);
        Assert.Equal("AAPL filed a 10-Q: quarterly report. 1 more filing.",
            EdgarHeadlineWording.Compose("AAPL", fresh, MarketEventLevel.All, Now)!.Text);
        Assert.Null(EdgarHeadlineWording.Compose("AAPL", fresh, MarketEventLevel.Off, Now));
    }

    [Theory]
    [InlineData(9, "AAPL filed a Form 4: insider trade.")]
    [InlineData(10, "AAPL filed a Form 4: insider trade. Accepted 10 minutes ago.")]
    [InlineData(61, "AAPL filed a Form 4: insider trade. Accepted 1 hour ago.")]
    [InlineData(119, "AAPL filed a Form 4: insider trade. Accepted 1 hour ago.")]
    public void ALateHeadlineSaysItsAge(int minutesOld, string expected)
        => Assert.Equal(expected, EdgarHeadlineWording.Compose("AAPL",
            new[] { Filing("4", Now.AddMinutes(-minutesOld), "f") }, MarketEventLevel.All, Now)!.Text);

    // ── The poller ───────────────────────────────────────────────────────────

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTime now) => Set(now);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTime now) =>
            _now = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new DummyTimer();
        private sealed class DummyTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSource : ICompanyFilingSource
    {
        public readonly Dictionary<string, List<EdgarFiling>?> Filings = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> FilingReads = new();
        public readonly List<string> FilerChecks = new();

        public Task<bool> IsFilerAsync(string ticker, CancellationToken ct = default)
        {
            FilerChecks.Add(ticker);
            return Task.FromResult(Filings.ContainsKey(ticker));
        }

        public Task<IReadOnlyList<EdgarFiling>?> RecentFilingsAsync(string ticker, CancellationToken ct = default)
        {
            FilingReads.Add(ticker);
            return Task.FromResult<IReadOnlyList<EdgarFiling>?>(Filings.TryGetValue(ticker, out var l) ? l : null);
        }
    }

    private sealed class FakeHeld(params MarketEventHeldPosition[] positions) : IHeldPositionReader
    {
        public int Reads;
        public Task<IReadOnlyList<MarketEventHeldPosition>> ReadHeldPositionsAsync()
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<MarketEventHeldPosition>>(positions);
        }
    }

    private sealed class FakePresence : IUserPresence
    {
        public bool CanReachUser { get; set; } = true;
        public bool IsAppVisible { get; set; } = true;
    }

    // Monday 2 November 2026, 4:31 PM New York: EDGAR is accepting filings.
    private static readonly DateTime Open = new(2026, 11, 2, 21, 31, 0, DateTimeKind.Utc);

    private sealed class Rig : IDisposable
    {
        public readonly FakeClock Clock = new(Open);
        public readonly FakeSource Source = new();
        public readonly SpyEventBus Bus = new();
        public readonly MockWorkspaceStore Store = new();
        public readonly FakePresence Presence = new();
        public readonly List<HeadlineEvent> Said = new();
        public readonly EdgarHeadlineLiveNotices Service;

        public Rig(string level = "all", IHeldPositionReader? held = null, string market = "Stock|Spot",
            string symbol = "AAPL")
        {
            Store.EmitState(WorkspaceState.Initial with
            {
                Identity = new ChartIdentity(market, "Alpaca", symbol, "1h"),
                HeadlinesLevel = level,
            });
            Bus.Subscribe<HeadlineEvent>(Said.Add);
            Service = new EdgarHeadlineLiveNotices(Store, Bus, Source, held, Clock, Presence);
        }

        public async Task TickAt(DateTime now)
        {
            Clock.Set(now);
            await Service.TickAsync();
        }

        public void Level(string level) => Store.EmitState(Store.State with { HeadlinesLevel = level });

        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public async Task OffFetchesNothing()
    {
        using var rig = new Rig(level: "off");
        rig.Source.Filings["AAPL"] = new() { Filing("8-K", Open.AddSeconds(-20), "r", "2.02") };

        await rig.TickAt(Open);

        Assert.Empty(rig.Source.FilerChecks);
        Assert.Empty(rig.Source.FilingReads);
        Assert.Empty(rig.Said);
    }

    [Fact]
    public async Task ANewFilingIsSaidOnceAtTheNextPoll()
    {
        using var rig = new Rig();
        var list = new List<EdgarFiling> { Filing("10-K", Open.AddDays(-20), "annual") };
        rig.Source.Filings["AAPL"] = list;

        await rig.TickAt(Open);                       // baseline
        Assert.Empty(rig.Said);

        list.Insert(0, Filing("8-K", Open.AddSeconds(30), "results", "2.02", "9.01"));
        await rig.TickAt(Open.AddSeconds(40));         // not due yet: a minute between reads
        Assert.Single(rig.Source.FilingReads);
        Assert.Empty(rig.Said);

        await rig.TickAt(Open.AddSeconds(60));
        var said = Assert.Single(rig.Said);
        Assert.Equal("AAPL filed an 8-K: earnings results.", said.Text);
        Assert.Equal(MarketEventImportance.High, said.Importance);
        Assert.Equal("AAPL", said.Symbol);

        await rig.TickAt(Open.AddSeconds(120));
        Assert.Single(rig.Said);
    }

    [Fact]
    public async Task AFailedReadTeachesNothingSoTheFilingIsSaidWhenItComesBack()
    {
        using var rig = new Rig();
        var list = new List<EdgarFiling> { Filing("10-K", Open.AddDays(-20), "annual") };
        rig.Source.Filings["AAPL"] = list;
        await rig.TickAt(Open);

        rig.Source.Filings["AAPL"] = null;             // the feed fails for a minute
        await rig.TickAt(Open.AddMinutes(1));
        Assert.Empty(rig.Said);

        // Accepted during the outage, read 7 minutes later: past the first-look reach, so it is
        // only said because the old baseline was kept.
        list.Insert(0, Filing("8-K", Open.AddMinutes(1).AddSeconds(5), "late", "8.01"));
        rig.Source.Filings["AAPL"] = list;
        await rig.TickAt(Open.AddMinutes(8));
        Assert.Equal("AAPL filed an 8-K: other events.", Assert.Single(rig.Said).Text);
    }

    [Fact]
    public async Task ACoinIsAskedAboutHourlyAndNeverRead()
    {
        using var rig = new Rig(market: "Crypto|Spot", symbol: "BTC");
        await rig.TickAt(Open);
        Assert.Empty(rig.Source.FilerChecks);          // a crypto chart is not a stock at all

        rig.Store.EmitState(rig.Store.State with
        {
            Identity = new ChartIdentity("Stock|Spot", "Alpaca", "SPYX", "1h"),
        });
        await rig.TickAt(Open.AddMinutes(1));
        await rig.TickAt(Open.AddMinutes(30));
        Assert.Equal(new[] { "SPYX" }, rig.Source.FilerChecks);
        Assert.Empty(rig.Source.FilingReads);

        await rig.TickAt(Open.AddMinutes(61));
        Assert.Equal(2, rig.Source.FilerChecks.Count);
    }

    [Fact]
    public async Task NothingIsReadWhileNoWindowCanBeReached()
    {
        using var rig = new Rig();
        rig.Source.Filings["AAPL"] = new();
        rig.Presence.CanReachUser = false;

        await rig.TickAt(Open);

        Assert.Empty(rig.Source.FilingReads);
    }

    [Fact]
    public async Task TurningItOffForgetsTheBaselineSoTheGapIsNotReadOut()
    {
        using var rig = new Rig();
        var list = new List<EdgarFiling> { Filing("10-K", Open.AddDays(-20), "annual") };
        rig.Source.Filings["AAPL"] = list;
        await rig.TickAt(Open);

        rig.Level("off");
        await rig.TickAt(Open.AddMinutes(1));
        list.Insert(0, Filing("8-K", Open.AddMinutes(30), "while-off", "8.01"));

        rig.Level("all");
        await rig.TickAt(Open.AddMinutes(40));         // a fresh first look: 10 min old = baseline
        Assert.Empty(rig.Said);
    }

    [Fact]
    public async Task AHeldStockIsWatchedWithNoChartOpen()
    {
        var held = new FakeHeld(new MarketEventHeldPosition("MSFT", 10, "Paper", null, MarketType.Stock));
        using var rig = new Rig(held: held, market: "Crypto|Spot", symbol: "BTC");
        rig.Source.Filings["MSFT"] = new();

        await rig.TickAt(Open);
        await rig.TickAt(Open.AddMinutes(1));

        Assert.Equal(new[] { "MSFT", "MSFT" }, rig.Source.FilingReads);
        Assert.Equal(1, held.Reads);                   // positions are re-read every 5 minutes
    }

    [Fact]
    public void TouchedStocksAreTheStockChartsTabsAndHeldStocksOnceEach()
    {
        static TabSnapshot Tab(string market, string symbol)
        {
            var s = WorkspaceState.Initial;
            return new TabSnapshot(
                TabIndex: 1,
                Identity: new ChartIdentity(market, "Alpaca", symbol, "1d"),
                Data: s.Data,
                ActiveSeries: s.ActiveSeries,
                FocusedSeriesIndex: s.FocusedSeriesIndex,
                FocusedSeriesId: s.FocusedSeriesId,
                FocusedComponentIndex: s.FocusedComponentIndex,
                FocusedBinIndex: s.FocusedBinIndex,
                CurrentDataIndex: s.CurrentDataIndex,
                ViewportStartIndex: s.ViewportStartIndex,
                ViewportLength: s.ViewportLength,
                RightMarginBars: s.RightMarginBars,
                ViewportRange: s.ViewportRange,
                PaneRanges: s.PaneRanges,
                IsHeikinAshi: false,
                IsLogScale: false,
                LastInteractionContext: s.LastInteractionContext,
                PaneHeightRatios: s.PaneHeightRatios,
                InitStatus: InitializationStatus.Ready,
                DataStatus: DataStatus.Ready,
                IsCoordinateEntryMode: false,
                PendingDrawingTool: null,
                CoordinateEntryAnchorCount: 0,
                CoordinateEntryAnchor1Index: -1,
                SymbolDisplayName: symbol);
        }

        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Stock|Spot", "Alpaca", "aapl", "1h"),
            TabSnapshots = ImmutableList.Create(
                Tab("Stock|Spot", "NVDA"),
                Tab("Crypto|Spot", "BTC/USD"),
                Tab("Stock|Spot", "AAPL")),
        };
        var held = new[]
        {
            new MarketEventHeldPosition("MSFT", 1, "Paper", null, MarketType.Stock),
            new MarketEventHeldPosition("ETH", 1, "Paper", null, MarketType.Crypto),
            new MarketEventHeldPosition("NVDA", 1, "Alpaca", null, MarketType.Stock),
        };

        Assert.Equal(new[] { "AAPL", "NVDA", "MSFT" },
            EdgarHeadlineLiveNotices.TouchedStocks(state, held));
    }

    [Theory]
    [InlineData("2026-11-02T10:59:00Z", 15)]   // Monday 5:59 AM New York: closed
    [InlineData("2026-11-02T11:00:00Z", 1)]    // 6:00 AM: open
    [InlineData("2026-11-03T02:59:00Z", 1)]    // Monday 9:59 PM: open
    [InlineData("2026-11-03T03:00:00Z", 15)]   // 10:00 PM: closed
    [InlineData("2026-11-07T15:00:00Z", 15)]   // Saturday
    [InlineData("2026-07-06T10:30:00Z", 1)]    // summer: 6:30 AM EDT
    public void ReadsAreAMinuteApartWhileEdgarAcceptsFilings(string utc, int minutes)
        => Assert.Equal(TimeSpan.FromMinutes(minutes), EdgarHeadlineLiveNotices.Interval(
            DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal)));

    // ── Settings, change log, dossier ───────────────────────────────────────

    private sealed class DictSettings : ISettingsManager
    {
        public readonly Dictionary<string, JToken> Store = new(StringComparer.Ordinal);
        public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
            => Store.TryGetValue(keyPath, out var v) ? v : defaultValue;
        public void SetSetting(string keyPath, JToken value) => Store[keyPath] = value;
        public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
        public void SaveSettings() { }
        public void ResetToDefaults() { }
        public void Reload() { }
    }

    [Fact]
    public void TheLevelSurvivesASaveAndReloadAndDefaultsToOff()
    {
        Assert.Equal("off", WorkspaceState.Initial.HeadlinesLevel);
        Assert.Equal("off", new AppSettings(new DictSettings()).HeadlinesLevel);

        var settings = new AppSettings(new DictSettings()) { HeadlinesLevel = "importantAndMedium" };
        var store = new MockWorkspaceStore();
        using var svc = new PreferencePersistenceService(store, settings,
            NullLogger<PreferencePersistenceService>.Instance);
        svc.Initialize();

        var seeded = store.DispatchedActions.OfType<UpdateSettingsAction>()
            .Select(a => a.Updater(WorkspaceState.Initial))
            .Last();
        Assert.Equal("importantAndMedium", seeded.HeadlinesLevel);
    }

    [Fact]
    public void AHeadlineIsLoggedUnderNewsWithItsSymbol()
    {
        var at = new DateTime(2026, 11, 2, 21, 31, 0, DateTimeKind.Utc);
        var entry = ChangeLogRecorder.MapHeadline(new HeadlineEvent(
            "AAPL filed an 8-K: earnings results.", MarketEventImportance.High, "edgar|r", "AAPL"), at);

        Assert.Equal(ChangeLogCategory.News, entry.Category);
        Assert.Equal("news|edgar|r", entry.Key);
        Assert.Equal("AAPL", entry.Symbol);
        Assert.Equal("Headlines", ChangeLogWording.CategoryWord(ChangeLogCategory.News));
        // Read after the market-wide events, before the rest.
        Assert.Equal(ChangeLogCategory.Event + 1, ChangeLogCategory.News);
    }

    [Fact]
    public void TheDossierListsRecentFilingsNewestFirstInNewYorkTime()
    {
        var now = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
        var section = AssetDossierService.RecentFilingsSection(new[]
        {
            Filing("424B2", new DateTime(2026, 11, 6, 21, 30, 47, DateTimeKind.Utc), "p"),
            Filing("8-K", new DateTime(2026, 11, 2, 21, 30, 23, DateTimeKind.Utc), "r", "2.02", "9.01"),
            Filing("4", new DateTime(2026, 11, 19, 23, 30, 21, DateTimeKind.Utc), "f"),
        }, now);

        Assert.Equal("Recent filings", section.Title);
        Assert.Equal(DossierStatus.Ok, section.Status);
        Assert.Equal(new[] { "2026-11-19 6:30 PM New York", "2026-11-06 4:30 PM New York", "2026-11-02 4:30 PM New York" },
            section.Fields.Select(f => f.Label));
        Assert.Equal(new[] { "Form 4: insider trade", "424B2", "8-K: earnings results" },
            section.Fields.Select(f => f.Value));
        Assert.Equal(new[] { "Low", "Not announced", "Important" },
            section.Fields.Select(f => f.Note));

        Assert.Equal(DossierStatus.Unavailable, AssetDossierService.RecentFilingsSection(null, now).Status);
        Assert.Equal(DossierStatus.NoData,
            AssetDossierService.RecentFilingsSection(Array.Empty<EdgarFiling>(), now).Status);
    }
}
