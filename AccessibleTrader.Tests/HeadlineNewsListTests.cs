using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Headlines;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Headlines kept said or not (2026-11-20): the history behind the Go bar's NEWS list, each
/// headline on the bar it arrived in, period / Shift+period between them, the arrow keys'
/// clause and Alt+Shift+D's section — and the Ctrl+Alt+Shift switches Cody asked for the same
/// day.
/// </summary>
public class HeadlineNewsListTests
{
    private static readonly DateTime Start = new(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Aapl = new("Stock|Spot", "Alpaca", "AAPL", "1h");

    private static HeadlineRecord Rec(string key, DateTime at, string text = "Apple Reports Q4 Results.",
        MarketEventImportance importance = MarketEventImportance.High, string symbol = "AAPL", bool said = false)
        => new(key, symbol, at, text, "Yahoo", importance, said);

    private static List<Ohlcv> Hourly(int count)
        => Enumerable.Range(0, count)
            .Select(i => new Ohlcv(Start.AddHours(i), 99, 101, 98, 100, 1000))
            .ToList();

    private static WorkspaceState State(IReadOnlyList<Ohlcv> bars, int cursor = 0, string level = "all",
        ChartIdentity? identity = null, PriceChartStyle style = PriceChartStyle.Candles)
        => WorkspaceState.Initial with
        {
            Identity = identity ?? Aapl,
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = cursor,
            HeadlinesLevel = level,
            ChartStyle = style,
        };

    // ── The history ─────────────────────────────────────────────────────────

    [Fact]
    public void AStoryIsKeptOncePerSymbolEvenFromAnotherWireInOtherWords()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("finnhub|1", Start, "Apple Reports Q4 Results, Beats Estimates."));
        h.Add(Rec("finnhub|1", Start, "Apple Reports Q4 Results, Beats Estimates."));       // same key
        h.Add(Rec("alpaca|9", Start.AddMinutes(20), "Apple Reports Q4 Results; Beats Estimates."));
        h.Add(Rec("finnhub|1", Start, "Apple Reports Q4 Results, Beats Estimates.", symbol: "MSFT"));

        Assert.Single(h.For("AAPL"));
        Assert.Single(h.For("MSFT"));
    }

    [Fact]
    public void TheSameWordsMoreThanTwoHoursApartAreTwoHeadlines()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("finnhub|1", Start, "Apple Shares Halted."));
        h.Add(Rec("finnhub|2", Start.AddHours(3), "Apple Shares Halted."));
        Assert.Equal(2, h.For("AAPL").Count);
    }

    [Fact]
    public void TheHistoryKeepsTheNewestUpToItsCap()
    {
        var h = new HeadlineHistory();
        for (int i = 0; i < HeadlineHistory.Cap + 5; i++)
            h.Add(Rec("finnhub|" + i, Start.AddHours(i * 3), $"Story number {i} about apple word{i}."));

        Assert.Equal(HeadlineHistory.Cap, h.Count);
        Assert.DoesNotContain(h.Newest(), r => r.Key == "finnhub|0");
        Assert.Equal("finnhub|" + (HeadlineHistory.Cap + 4), h.Newest()[0].Key);
    }

    [Fact]
    public void MarkSaidMarksEverySymbolTheStoryWasFor()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("finnhub|1", Start));
        h.Add(Rec("finnhub|1", Start, symbol: "MSFT"));
        h.Add(Rec("finnhub|2", Start.AddHours(5), "Something else entirely."));
        int before = h.Version;

        h.MarkSaid("finnhub|1");

        Assert.All(h.Newest().Where(r => r.Key == "finnhub|1"), r => Assert.True(r.Said));
        Assert.False(h.Newest().Single(r => r.Key == "finnhub|2").Said);
        Assert.NotEqual(before, h.Version);
    }

    [Fact]
    public void TheGateKeepsEveryMatchedStoryAndMarksTheOnesItSays()
    {
        var history = new HeadlineHistory();
        var gate = new NewsHeadlineGate(history);
        var target = new[] { new NewsTarget("AAPL", false) };
        var story = new NewsStory(1, "Apple Reports Q4 Results", "", "Yahoo", Start, Start,
            new[] { "AAPL" }, "https://x", NewsStory.FinnhubWire);

        gate.Note(story, target, MarketEventImportance.High);
        Assert.False(history.For("AAPL").Single().Said);

        gate.Offer(story, target, MarketEventImportance.High, Start);
        Assert.Single(gate.Due(Start.AddSeconds(5), new[] { "AAPL" }));
        Assert.True(history.For("AAPL").Single().Said);
    }

    // ── On the bars ─────────────────────────────────────────────────────────

    [Fact]
    public void AHeadlineIsOnTheBarItArrivedInAndBetweenBarsOnTheNext()
    {
        // Bars at 14:00, 15:00 and (after a gap, as overnight) 20:00.
        var bars = new List<Ohlcv>
        {
            new(Start, 1, 1, 1, 1, 1),
            new(Start.AddHours(1), 1, 1, 1, 1, 1),
            new(Start.AddHours(6), 1, 1, 1, 1, 1),
        };
        var placed = HeadlineChart.Place(new[]
        {
            Rec("a", Start.AddMinutes(10)),             // inside the first bar
            Rec("b", Start.AddHours(1)),                // the second bar's first second
            Rec("c", Start.AddHours(3)),                // in the gap: the next bar
            Rec("d", Start.AddHours(9)),                // after the last bar ended: the last
            Rec("e", Start.AddMinutes(-1)),             // before the chart: not placed
        }, bars, 3600);

        Assert.Equal(new[] { ("a", 0), ("b", 1), ("c", 2), ("d", 2) },
            placed.Select(p => (p.Record.Key, p.BarIndex)).ToArray());
    }

    [Fact]
    public void AStockOrACoinHasHeadlinesAndABrickChartOrForexHasNone()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("a", Start.AddMinutes(5)));
        h.Add(Rec("b", Start.AddMinutes(5), "Bitcoin Hits A Record.", symbol: "BTC"));
        var map = new HeadlineBarMap(h);
        var bars = Hourly(3);

        Assert.Single(map.For(State(bars)));
        Assert.Single(map.For(State(bars, identity: new ChartIdentity("Crypto|Spot", "Kraken", "XBTUSD", "1h"))));
        Assert.Empty(map.For(State(bars, style: PriceChartStyle.Renko)));
        Assert.Empty(map.For(State(bars, identity: new ChartIdentity("Forex|Spot", "Oanda", "EUR/USD", "1h"))));
    }

    [Fact]
    public void TheClauseLeadsWithTheMostImportantThatPassesAndCountsTheRest()
    {
        var onBar = new[]
        {
            new HeadlineBarPlacement(0, Rec("a", Start.AddMinutes(1), "Analyst Raises Apple Target.", MarketEventImportance.Medium)),
            new HeadlineBarPlacement(0, Rec("b", Start.AddMinutes(2), "Apple Reports Q4 Results.", MarketEventImportance.High)),
            new HeadlineBarPlacement(0, Rec("c", Start.AddMinutes(3), "Apple Store Opens In Town.", MarketEventImportance.Low)),
        };

        Assert.Equal("Headline: Apple Reports Q4 Results. 2 more headlines.",
            HeadlineSpeech.Clause(HeadlineSpeech.Passing(onBar, MarketEventLevel.All)));
        Assert.Equal("Headline: Apple Reports Q4 Results.",
            HeadlineSpeech.Clause(HeadlineSpeech.Passing(onBar, MarketEventLevel.ImportantOnly)));
        Assert.Equal("", HeadlineSpeech.Clause(HeadlineSpeech.Passing(onBar.Take(1), MarketEventLevel.ImportantOnly)));
    }

    private static AccessibilityFeedbackCoordinator Coordinator(MockWorkspaceStore store, IHeadlineBarMap map)
    {
        var formatter = new SpeechFormatter();
        var router = new SpeechFeedbackRouter(new CounterSpeechManager(), formatter, store);
        return new AccessibilityFeedbackCoordinator(
            store,
            new NavigationFeedbackManager(router, formatter),
            router,
            new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
            formatter,
            new SpyEventBus(),
            new MockEarconService(),
            new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(),
            new MockAutoNarrationService(),
            headlineMap: map);
    }

    [Fact]
    public void ArrowingOntoABarReadsItsHeadlineClauseOnlyWhileHeadlinesAreOn()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("a", Start.AddHours(1).AddMinutes(5)));
        var store = new MockWorkspaceStore();
        var coordinator = Coordinator(store, new HeadlineBarMap(h));

        store.EmitState(State(Hourly(3), cursor: 1));
        Assert.Equal("Headline: Apple Reports Q4 Results.", coordinator.HeadlineContext());

        store.EmitState(State(Hourly(3), cursor: 0));
        Assert.Equal("", coordinator.HeadlineContext());

        store.EmitState(State(Hourly(3), cursor: 1, level: "off"));
        Assert.Equal("", coordinator.HeadlineContext());
    }

    [Fact]
    public void AltShiftDReadsEveryHeadlineOnTheBarWhateverTheLevel()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("a", Start.AddHours(1).AddMinutes(5), "Apple Store Opens In Town.", MarketEventImportance.Low));
        var bus = new SpyEventBus();
        var detail = new BarDetailService(bus, headlines: new HeadlineBarMap(h));

        detail.AnnounceDetails(State(Hourly(3), cursor: 1, level: "off"));

        var said = bus.Log.OfType<AnnouncementEvent>().Single().Message;
        Assert.Contains("Headline on this bar: Nov 16 15:05 UTC, Yahoo, ", said);
        Assert.Contains("Apple Store Opens In Town.", said);
    }

    // ── Period and Shift+period ─────────────────────────────────────────────

    [Fact]
    public void PeriodJumpsToTheNextHeadlineThatPassesTheLevelAndShiftPeriodBack()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("low", Start.AddHours(2).AddMinutes(5), "Apple Store Opens In Town.", MarketEventImportance.Low));
        h.Add(Rec("high", Start.AddHours(4).AddMinutes(5)));
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var nav = new HeadlineNavigator(store, bus, new HeadlineBarMap(h));

        store.EmitState(State(Hourly(6), cursor: 0, level: "importantOnly"));
        nav.Jump(forward: true);
        Assert.Equal(new NavigateAction(4), store.DispatchedActions.OfType<NavigateAction>().Last());

        store.EmitState(State(Hourly(6), cursor: 5, level: "all"));
        nav.Jump(forward: false);
        Assert.Equal(new NavigateAction(4), store.DispatchedActions.OfType<NavigateAction>().Last());

        store.EmitState(State(Hourly(6), cursor: 4, level: "all"));
        nav.Jump(forward: false);
        Assert.Equal(new NavigateAction(2), store.DispatchedActions.OfType<NavigateAction>().Last());

        store.EmitState(State(Hourly(6), cursor: 2, level: "all"));
        nav.Jump(forward: false);
        Assert.Equal("No earlier headlines.", bus.Log.OfType<FeedbackRequestEvent>().Last().Message);
    }

    [Fact]
    public void PeriodWithHeadlinesOffSaysSo()
    {
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var nav = new HeadlineNavigator(store, bus, new HeadlineBarMap(new HeadlineHistory()));
        store.EmitState(State(Hourly(3), level: "off"));

        nav.Jump(forward: true);

        Assert.Empty(store.DispatchedActions.OfType<NavigateAction>());
        Assert.StartsWith("Headlines are off.", bus.Log.OfType<FeedbackRequestEvent>().Single().Message);
    }

    // ── The backfill ────────────────────────────────────────────────────────

    private sealed class Filings : ICompanyFilingSource
    {
        public int Reads;
        public IReadOnlyList<EdgarFiling> List = Array.Empty<EdgarFiling>();
        public Task<IReadOnlyList<EdgarFiling>?> RecentFilingsAsync(string ticker, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<EdgarFiling>?>(List);
        }
        public Task<bool> IsFilerAsync(string ticker, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(Start.AddHours(10));
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task TheChartsOwnFilingsAreReadOnceEachHalfHourAndNeverWhileOff()
    {
        var filings = new Filings
        {
            List = new[]
            {
                new EdgarFiling("AAPL", "Apple Inc.", "8-K", new[] { "2.02" }, Start.AddHours(1).AddMinutes(30), "0000320193-26-000001", "https://x"),
            },
        };
        var clock = new Clock();
        var h = new HeadlineHistory();
        var backfill = new HeadlineBackfill(h, filings: filings, timeProvider: clock);
        var target = new NewsTarget("AAPL", false);

        backfill.RequestRefresh(State(Hourly(6), level: "off"), target);
        await backfill.LastFill;
        Assert.Equal(0, filings.Reads);

        backfill.RequestRefresh(State(Hourly(6)), target);
        await backfill.LastFill;
        backfill.RequestRefresh(State(Hourly(6)), target);
        await backfill.LastFill;
        Assert.Equal(1, filings.Reads);
        Assert.Equal("edgar|0000320193-26-000001", h.For("AAPL").Single().Key);

        clock.Now += HeadlineBackfill.Refresh;
        backfill.RequestRefresh(State(Hourly(6)), target);
        await backfill.LastFill;
        Assert.Equal(2, filings.Reads);
    }

    // ── The NEWS list ───────────────────────────────────────────────────────

    private static GoBarService GoBar(MockWorkspaceStore store, SpyEventBus bus, HeadlineHistory h,
        HeadlineNavigator? nav = null)
        => new(Substitute.For<ICommandDispatcher>(), Substitute.For<IShortcutManager>(), bus,
            new ModalStack(bus), store: store, headlines: h, headlineNavigator: nav);

    [Fact]
    public void NewsListsEveryHeadlineNewestFirstSaidOrNot()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("a", Start, said: true));
        h.Add(Rec("b", Start.AddHours(3), "Microsoft Wins A Contract.", MarketEventImportance.Medium, symbol: "MSFT"));
        var store = new MockWorkspaceStore();
        store.EmitState(State(Hourly(6)));

        var s = GoBar(store, new SpyEventBus(), h).Suggest("NEWS");

        Assert.Equal(new[]
        {
            "MSFT: Microsoft Wins A Contract. Medium, Yahoo, Nov 16 17:00 UTC, not said.",
            "AAPL: Apple Reports Q4 Results. Important, Yahoo, Nov 16 14:00 UTC.",
        }, s.Options.Select(o => o.Name));
        Assert.StartsWith("2 headlines, newest first. MSFT:", s.Sentence);
    }

    [Theory]
    [InlineData("NEWS AAPL", "a")]
    [InlineData("NEWS msft", "b")]
    [InlineData("news contract", "b")]
    public void NewsWithAWordListsThatSymbolOrThoseWords(string typed, string key)
    {
        var h = new HeadlineHistory();
        h.Add(Rec("a", Start));
        h.Add(Rec("b", Start.AddHours(3), "Microsoft Wins A Contract.", symbol: "MSFT"));
        var store = new MockWorkspaceStore();
        store.EmitState(State(Hourly(6)));

        var option = Assert.Single(GoBar(store, new SpyEventBus(), h).Suggest(typed).Options);
        Assert.Equal(key, Assert.IsType<GoBarHeadlineAction>(option.Action).Record.Key);
    }

    [Fact]
    public void AnEmptyNewsListSaysWhyAndHowToTurnHeadlinesOn()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(State(Hourly(6), level: "off"));

        var s = GoBar(store, new SpyEventBus(), new HeadlineHistory()).Suggest("NEWS");

        Assert.Empty(s.Options);
        Assert.Equal("No headlines yet. Headlines are off; Control Alt Shift period turns them on.", s.Sentence);
    }

    [Fact]
    public void EnterOnAHeadlineGoesToItsBarOrSaysWhereItIs()
    {
        var h = new HeadlineHistory();
        h.Add(Rec("a", Start.AddHours(2).AddMinutes(5)));
        h.Add(Rec("b", Start.AddHours(3), "Microsoft Wins A Contract.", symbol: "MSFT"));
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        store.EmitState(State(Hourly(6)));
        var service = GoBar(store, bus, h, new HeadlineNavigator(store, bus, new HeadlineBarMap(h)));

        service.Run(service.Suggest("NEWS AAPL").Options.Single());
        Assert.True(SpinWait.SpinUntil(() => store.DispatchedActions.OfType<NavigateAction>().Any(),
            TimeSpan.FromSeconds(3)));
        Assert.Equal(new NavigateAction(2), store.DispatchedActions.OfType<NavigateAction>().Single());

        service.Run(service.Suggest("NEWS MSFT").Options.Single());
        Assert.True(SpinWait.SpinUntil(() => bus.Log.OfType<AnnouncementEvent>()
            .Any(e => e.Message == "Microsoft Wins A Contract. It is for MSFT, not this chart. GP MSFT opens it."),
            TimeSpan.FromSeconds(3)));
    }

    // ── Ctrl+Alt+Shift: the three switches ──────────────────────────────────

    private static (CommandDispatcher Dispatcher, MockWorkspaceStore Store, SpyEventBus Bus) Dispatcher(WorkspaceState state)
    {
        var store = new MockWorkspaceStore();
        store.EmitState(state);
        var bus = new SpyEventBus();
        var d = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        return (d, store, bus);
    }

    /// <summary>Runs the command and applies the settings change it asked for, as the real
    /// store does.</summary>
    private static string Press(CommandDispatcher d, MockWorkspaceStore store, SpyEventBus bus, SystemCommand command)
    {
        int seen = store.DispatchedActions.Count;
        d.Dispatch(command);
        foreach (var a in store.DispatchedActions.Skip(seen).OfType<UpdateSettingsAction>().ToList())
            store.EmitState(a.Updater(store.State));
        return bus.Log.OfType<FeedbackRequestEvent>().Last().Message ?? "";
    }

    [Fact]
    public void TheHeadlinesSwitchTurnsOffAndBackToTheLevelItHad()
    {
        var (d, store, bus) = Dispatcher(State(Hourly(3), level: MarketEventLevels.ImportantAndMedium));

        Assert.Equal("Headlines off.", Press(d, store, bus, SystemCommand.ToggleHeadlines));
        Assert.Equal(MarketEventLevels.Off, store.State.HeadlinesLevel);
        Assert.Equal("Headlines on, important and medium.", Press(d, store, bus, SystemCommand.ToggleHeadlines));
        Assert.Equal(MarketEventLevels.ImportantAndMedium, store.State.HeadlinesLevel);
    }

    [Fact]
    public void FromOffTheSwitchesTurnOnAtImportantOnly()
    {
        var (d, store, bus) = Dispatcher(State(Hourly(3), level: "off") with { MarketEventsLevel = MarketEventLevels.Off });

        Assert.Equal("Headlines on, important only.", Press(d, store, bus, SystemCommand.ToggleHeadlines));
        Assert.Equal("Market events on, important only.", Press(d, store, bus, SystemCommand.ToggleMarketEvents));
        Assert.Equal(MarketEventLevels.ImportantOnly, store.State.MarketEventsLevel);
    }

    [Fact]
    public void TheMarketEventsSwitchRemembersItsOwnLevelNotTheHeadlines()
    {
        var (d, store, bus) = Dispatcher(State(Hourly(3), level: MarketEventLevels.All)
            with { MarketEventsLevel = MarketEventLevels.ImportantAndMedium });

        Press(d, store, bus, SystemCommand.ToggleHeadlines);
        Press(d, store, bus, SystemCommand.ToggleMarketEvents);
        Assert.Equal("Market events on, important and medium.", Press(d, store, bus, SystemCommand.ToggleMarketEvents));
        Assert.Equal("Headlines on, all.", Press(d, store, bus, SystemCommand.ToggleHeadlines));
    }

    [Fact]
    public void TheChartPatternsSwitchFlipsDescribeChartPatterns()
    {
        var (d, store, bus) = Dispatcher(State(Hourly(3)) with { DescribeChartPatterns = false });

        Assert.Equal("Describe chart patterns on.", Press(d, store, bus, SystemCommand.ToggleChartPatterns));
        Assert.True(store.State.DescribeChartPatterns);
        Assert.Equal("Describe chart patterns off.", Press(d, store, bus, SystemCommand.ToggleChartPatterns));
        Assert.False(store.State.DescribeChartPatterns);
    }

    // ── The keys ────────────────────────────────────────────────────────────

    private sealed class TempPaths : IPlatformPathService
    {
        public string AppDataDirectory { get; } = TestTemp.NewDir("att-headline-keys-");
        public string CacheDirectory => AppDataDirectory;
    }

    [Theory]
    [InlineData(",", false, false, SystemCommand.NavPatternNext)]
    [InlineData("<", true, false, SystemCommand.NavPatternPrev)]
    [InlineData(",", true, false, SystemCommand.NavPatternPrev)]
    [InlineData(".", false, false, SystemCommand.NavHeadlineNext)]
    [InlineData(">", true, false, SystemCommand.NavHeadlinePrev)]
    [InlineData("OEM2", false, false, SystemCommand.OpenGoBar)]
    [InlineData("/", false, false, SystemCommand.OpenGoBar)]
    [InlineData(",", true, true, SystemCommand.ToggleChartPatterns)]
    [InlineData("<", true, true, SystemCommand.ToggleChartPatterns)]
    [InlineData(".", true, true, SystemCommand.ToggleHeadlines)]
    [InlineData("'", true, true, SystemCommand.ToggleMarketEvents)]
    [InlineData("\"", true, true, SystemCommand.ToggleMarketEvents)]
    [InlineData("B", false, true, SystemCommand.MorningBrief)]
    [InlineData("b", false, true, SystemCommand.MorningBrief)]
    public void CommaFormationsPeriodHeadlinesSlashTheGoBar(string key, bool shift, bool ctrlAlt, SystemCommand expected)
        => Assert.Equal(expected, new ShortcutManager(new TempPaths())
            .GetCommand(key, shift, ctrl: ctrlAlt, alt: ctrlAlt));

    [Theory]
    [InlineData(SystemCommand.ToggleChartPatterns, "Control plus Alt plus Shift plus Comma")]
    [InlineData(SystemCommand.ToggleHeadlines, "Control plus Alt plus Shift plus Period")]
    [InlineData(SystemCommand.ToggleMarketEvents, "Control plus Alt plus Shift plus Apostrophe")]
    [InlineData(SystemCommand.NavHeadlineNext, "Period")]
    public void TheSwitchesAreSpokenAsKeysNotCodes(SystemCommand command, string words)
        => Assert.Equal(words, ShortcutWords.ForCommand(new ShortcutManager(new TempPaths()), command));

    private static ShortcutProfile Defaults()
        => new ShortcutManager(new TempPaths()).CurrentProfile;

    private static ShortcutProfile Saved(params ShortcutDefinition[] bindings)
        => new() { Shortcuts = bindings.ToList() };

    [Fact]
    public void ASavedProfileWithTheOldCommaAndPeriodMovesToTheNewOnes()
    {
        var saved = Saved(
            new ShortcutDefinition(SystemCommand.NavPatternNext, "."),
            new ShortcutDefinition(SystemCommand.NavPatternPrev, ","),
            new ShortcutDefinition(SystemCommand.OpenGoBar, "G", Alt: true));

        ShortcutManager.UpgradeSavedProfile(saved, Defaults());

        Assert.Contains(saved.Shortcuts, b => b is { Command: SystemCommand.NavPatternNext, Key: ",", Shift: false });
        Assert.Contains(saved.Shortcuts, b => b is { Command: SystemCommand.NavPatternPrev, Key: ",", Shift: true });
        Assert.Contains(saved.Shortcuts, b => b is { Command: SystemCommand.NavHeadlineNext, Key: ".", Shift: false });
        Assert.Contains(saved.Shortcuts, b => b is { Command: SystemCommand.OpenGoBar, Key: "OEM2" });
        Assert.Contains(saved.Shortcuts, b => b.Command == SystemCommand.ToggleHeadlines && b.Ctrl && b.Alt && b.Shift);
    }

    [Fact]
    public void AShortcutsFileSavedBeforeTodayGetsTheNewKeysOnLoad()
    {
        var paths = new TempPaths();
        File.WriteAllText(Path.Combine(paths.AppDataDirectory, "shortcuts.json"), """
            {"Name":"Default","Shortcuts":[
              {"Command":"NavPatternNext","Key":"."},
              {"Command":"NavPatternPrev","Key":","},
              {"Command":"OpenGoBar","Key":"G","Alt":true}]}
            """);
        var mgr = new ShortcutManager(paths);

        Assert.Equal(SystemCommand.NavPatternNext, mgr.GetCommand(",", false, false, false));
        Assert.Equal(SystemCommand.NavHeadlineNext, mgr.GetCommand(".", false, false, false));
        Assert.Equal(SystemCommand.OpenGoBar, mgr.GetCommand("OEM2", false, false, false));
        Assert.Equal(SystemCommand.ToggleMarketEvents, mgr.GetCommand("'", true, true, true));
    }

    [Fact]
    public void AUsersOwnBindingsStayAndNoDefaultTakesAKeyTheyUse()
    {
        var saved = Saved(
            new ShortcutDefinition(SystemCommand.NavPatternNext, "N", Alt: true),
            new ShortcutDefinition(SystemCommand.NavPatternPrev, ","),
            new ShortcutDefinition(SystemCommand.OpenSettings, "."),
            new ShortcutDefinition(SystemCommand.OpenGoBar, "K", Ctrl: true));

        ShortcutManager.UpgradeSavedProfile(saved, Defaults());

        Assert.Contains(saved.Shortcuts, b => b is { Command: SystemCommand.NavPatternNext, Key: "N" });
        Assert.Contains(saved.Shortcuts, b => b is { Command: SystemCommand.NavPatternPrev, Key: ",", Shift: false });
        Assert.Single(saved.Shortcuts, b => b.Key == "." && !b.Shift && !b.Ctrl && !b.Alt);
        Assert.DoesNotContain(saved.Shortcuts, b => b.Command == SystemCommand.NavHeadlineNext && b.Key == "." && !b.Shift);
        Assert.DoesNotContain(saved.Shortcuts, b => b.Command == SystemCommand.OpenGoBar && b.Key == "OEM2");
    }
}
