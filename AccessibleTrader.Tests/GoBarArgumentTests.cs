using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Screening;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Screening;
using AccessibleTrader.Tests.Mocks;
using DynamicData;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Stage 6b's argument commands, sentence by sentence. The service is given a recording store,
/// a small controllable orchestrator and a spy bus, so each test can pin exactly what the user
/// hears and exactly what ran (and, just as important, what did not run).
/// </summary>
public class GoBarArgumentTests
{
    private readonly SpyEventBus _bus = new();
    private readonly MockWorkspaceStore _store = new();
    private readonly FakeOrchestrator _orchestrator = new();
    private readonly ModalStack _stack;
    private readonly IShortcutManager _shortcuts = Substitute.For<IShortcutManager>();

    public GoBarArgumentTests()
    {
        _stack = new ModalStack(_bus);
    }

    // ── Fixture ────────────────────────────────────────────────────────────

    private sealed class FakeOrchestrator : IMarketOrchestrator
    {
        private readonly Subject<MarketOrchestrator.Unit> _pipeline = new();
        private string _provider = "MEXC";

        public bool RefuseProviderChange { get; set; }
        public string SelectedMarket { get; set; } = "Crypto";
        public string SelectedAnalyticsType { get; set; } = "";
        public string SelectedProvider
        {
            get => _provider;
            set { if (!RefuseProviderChange) _provider = value; }
        }
        public string SelectedSubType { get; set; } = "Spot";
        public string SelectedSymbol { get; set; } = "BTCUSDT";
        public string SelectedTimeframe { get; set; } = "1h";

        public IReadOnlyList<string> AvailableMarkets { get; set; } = new[] { "Crypto" };
        public IReadOnlyList<string> AvailableAnalyticsTypes { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> AvailableProviders { get; set; } = new[] { "Bitstamp", "Kraken" };
        public IReadOnlyList<string> AvailableSubTypes { get; set; } = new[] { "Spot" };
        public IReadOnlyList<string> AvailableSymbols { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> AvailableTimeframes { get; set; } = new[] { "1h", "4h" };

        public IObservable<MarketOrchestrator.Unit> PipelineUpdated => _pipeline.AsObservable();

        public GoBarLoadOutcome Outcome { get; set; } =
            new(GoBarLoadOutcomeKind.Loaded, "MEXC", "BTCUSDT", "1h");

        public int RefreshSymbolsCalls { get; private set; }
        public int LoadCalls { get; private set; }
        public string? LastProvider { get; private set; }
        public string? LastSymbol { get; private set; }
        public string? LastTimeframe { get; private set; }
        public MarketType? LastMarket { get; private set; }
        public string? LastSubType { get; private set; }

        public Task RefreshPipelineAsync() => Task.CompletedTask;
        public Task RefreshProvidersAsync() => Task.CompletedTask;
        public Task RefreshSymbolsAsync()
        {
            RefreshSymbolsCalls++;
            return Task.CompletedTask;
        }

        public Task LoadChartAsync() => Task.CompletedTask;

        public Task<GoBarLoadOutcome> LoadOnActiveTabAsync(
            string? provider, string? symbol, string? timeframe,
            MarketType? market, string? subType)
        {
            LoadCalls++;
            LastProvider = provider;
            LastSymbol = symbol;
            LastTimeframe = timeframe;
            LastMarket = market;
            LastSubType = subType;
            return Task.FromResult(Outcome);
        }

        public Task LoadRestoredActiveTabAsync() => Task.CompletedTask;
        public Task<ProviderDataShape> GetSelectedProviderDataShapeAsync()
            => Task.FromResult(ProviderDataShape.Ohlcv);
        public Task LoadChartInNewTabAsync() => Task.CompletedTask;
    }

    /// <summary>A store with no subscriptions: only a fixed state and the actions dispatched.</summary>
    private sealed class RecordingStore : IWorkspaceStore
    {
        public WorkspaceState State { get; set; } = WorkspaceState.Initial;
        public List<WorkspaceAction> Actions { get; } = new();
        public IObservable<WorkspaceState> StateStream => Observable.Empty<WorkspaceState>();
        public IObservable<IChangeSet<Ohlcv, DateTime>> DataStream
            => Observable.Never<IChangeSet<Ohlcv, DateTime>>();
        public IObservable<IChangeSet<ChartSeries, string>> SeriesStream
            => Observable.Never<IChangeSet<ChartSeries, string>>();
        public void Dispatch(WorkspaceAction action) => Actions.Add(action);
    }

    private GoBarService Build(
        IIndicatorService? indicators = null,
        ISeriesManagementService? series = null,
        DemoPolicy? demo = null,
        MarketEventNavigator? navigator = null,
        IMarketEventBarMapCache? eventMap = null,
        IChartPatternCache? patterns = null,
        ISdkCandlePatternAnalyzer? candles = null,
        IWatchlistLibrary? watchlists = null,
        TimeZoneInfo? zone = null)
        => new(Substitute.For<ICommandDispatcher>(),
            _shortcuts, _bus, _stack,
            store: _store,
            orchestrator: _orchestrator,
            watchlists: watchlists,
            indicators: indicators,
            series: series,
            demo: demo,
            marketEvents: navigator,
            eventMap: eventMap,
            patterns: patterns,
            candles: candles,
            displayZone: zone ?? TimeZoneInfo.Utc);

    private static Ohlcv Bar(DateTime stamp, double close = 100) =>
        new(stamp, close - 1, close + 1, close - 2, close, 1000);

    private static List<Ohlcv> Hourly(DateTime startUtc, int count, double close = 100)
        => Enumerable.Range(0, count)
            .Select(i => Bar(startUtc.AddHours(i), close + i))
            .ToList();

    private static List<Ohlcv> Daily(DateTime firstUtc, int count, double close = 100)
        => Enumerable.Range(0, count)
            .Select(i => Bar(firstUtc.AddDays(i), close + i))
            .ToList();

    private static WorkspaceState State(
        IReadOnlyList<Ohlcv>? data = null,
        string provider = "MEXC",
        string symbol = "BTCUSDT",
        string timeframe = "1h",
        int cursor = -1,
        bool heikinAshi = false,
        bool eventsOn = false,
        ProviderDataShape shape = ProviderDataShape.Ohlcv,
        ImmutableList<ChartSeries>? series = null)
    {
        var bars = data ?? Array.Empty<Ohlcv>();
        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", provider, symbol, timeframe),
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = series ?? ImmutableList<ChartSeries>.Empty,
            CurrentDataIndex = cursor >= 0 ? cursor : bars.Count - 1,
            IsHeikinAshi = heikinAshi,
            CurrentDataShape = shape,
            MarketEventsLevel = eventsOn ? MarketEventLevels.All : MarketEventLevels.Off,
        };
    }

    private void Use(WorkspaceState state) => _store.EmitState(state);

    private IReadOnlyList<string> Spoken =>
        _bus.Log.OfType<AnnouncementEvent>().Select(e => e.Message ?? "").ToList();

    private void WaitForSpoken(string sentence)
    {
        Assert.True(SpinWait.SpinUntil(
            () => Spoken.Contains(sentence), TimeSpan.FromSeconds(3)),
            $"never heard \"{sentence}\". Heard: {string.Join(" || ", Spoken)}");
    }

    private static GoBarOption Only(GoBarService service, string input)
    {
        var suggestion = service.Suggest(input);
        Assert.True(suggestion.Options.Count == 1,
            $"expected one option for \"{input}\", got {suggestion.Options.Count}: "
            + string.Join(", ", suggestion.Options.Select(o => o.Name)));
        return suggestion.Options[0];
    }

    // ── Timeframe ──────────────────────────────────────────────────────────

    [Fact]
    public void TfLoadsTheChartOnScreenWithTheTabIdentity()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            symbol: "BTCUSDT", timeframe: "1h"));
        _orchestrator.SelectedSymbol = "ETHUSDT";      // a stray toolbar selection
        _orchestrator.SelectedTimeframe = "1d";
        var service = Build();

        var option = Only(service, "TF 4h");
        Assert.Equal("Timeframe 4 hours", option.Name);
        service.Run(option);

        WaitForSpoken("Loading BTCUSDT, 4 hours.");
        Assert.Equal(1, _orchestrator.LoadCalls);
        Assert.Null(_orchestrator.LastProvider);
        Assert.Null(_orchestrator.LastSymbol);
        Assert.Equal("4h", _orchestrator.LastTimeframe);
        Assert.Null(_orchestrator.LastMarket);
        Assert.Null(_orchestrator.LastSubType);
    }

    [Fact]
    public void TfAlreadyOnItSaysSoAndDoesNotLoad()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            timeframe: "4h"));
        var service = Build();

        service.Run(Only(service, "TF 4h"));

        Assert.Contains("Already on 4 hours.", Spoken);
        Assert.Equal(0, _orchestrator.LoadCalls);
    }

    [Fact]
    public void TfDemoRefusalIsSaid()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            timeframe: "1h"));
        _orchestrator.Outcome = new(GoBarLoadOutcomeKind.NotInDemo);
        var service = Build();

        service.Run(Only(service, "TF 5m"));

        WaitForSpoken("Not in the public demo.");
        Assert.Contains("Loading BTCUSDT, 5 minutes.", Spoken);
    }

    [Fact]
    public void TfWithoutAChartSaysNoChartLoaded()
    {
        Use(State());
        var service = Build();

        service.Run(Only(service, "TF 4h"));

        Assert.Contains("No chart loaded.", Spoken);
        Assert.Equal(0, _orchestrator.LoadCalls);
    }

    [Fact]
    public void RefreshReloadsTheChartOnScreen()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            timeframe: "4h"));
        var service = Build();

        // Built by name, exactly as the matcher offers the 6a entry ("ref" also prefixes
        // Add or remove the midline's "reference level" alias, so pick the entry itself).
        var option = service.Suggest("REF").Options
            .Single(o => o.Entry?.Command == SystemCommand.RefreshData);
        service.Run(option);

        WaitForSpoken("Reloading BTCUSDT, 4 hours.");
        Assert.Equal(1, _orchestrator.LoadCalls);
        Assert.Null(_orchestrator.LastTimeframe);
    }

    // ── Provider ───────────────────────────────────────────────────────────

    [Fact]
    public void ProviderChoosesAndListsItsSymbolsWithoutLoading()
    {
        Use(State());
        _orchestrator.AvailableProviders = new[] { "Bitstamp", "Kraken" };
        _orchestrator.AvailableSymbols = Enumerable.Range(0, 812).Select(i => "S" + i).ToList();
        var service = Build();

        var option = Only(service, "PROV Kraken");
        Assert.Equal("Provider Kraken", option.Name);
        service.Run(option);

        WaitForSpoken("Provider Kraken, 812 symbols. Type GP and a symbol to open one.");
        Assert.Equal("Kraken", _orchestrator.SelectedProvider);
        Assert.Equal(1, _orchestrator.RefreshSymbolsCalls);
        Assert.Equal(0, _orchestrator.LoadCalls);
    }

    [Fact]
    public void ProviderDemoRefusalIsSaid()
    {
        Use(State());
        _orchestrator.RefuseProviderChange = true;
        var service = Build();

        var option = Only(service, "PROV Kraken");
        service.Run(option);

        Assert.Contains("Not in the public demo.", Spoken);
        Assert.Equal(0, _orchestrator.RefreshSymbolsCalls);
    }

    [Theory]
    [InlineData("TF", "Type a timeframe after TF, like TF 4h.", "TF ")]
    [InlineData("GP", "Type a symbol after GP, like GP BTC.", "GP ")]
    [InlineData("PROV", "Type a provider after PROV, like PROV Kraken.", "PROV ")]
    public void ABareVerbKeepsTheBoxOpenAndCompletesTheText(
        string typed, string prompt, string completion)
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        var service = Build();

        service.Run(Only(service, typed));

        Assert.Contains(prompt, Spoken);
        var complete = Assert.Single(_bus.Log.OfType<GoBarCompleteRequestEvent>());
        Assert.Equal(completion, complete.Text);
        Assert.Empty(_bus.Log.OfType<CloseGoBarRequestEvent>());
    }

    // ── Symbols ────────────────────────────────────────────────────────────

    [Fact]
    public void SeveralSymbolMatchesAreListedAndEnterAloneRefusesToPickOne()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            timeframe: "4h"));
        _orchestrator.AvailableSymbols = new[] { "BTCUSDT", "BTCUSDC", "ETHUSDT" };
        var service = Build();

        var suggestion = service.Suggest("GP BTC");
        Assert.Equal(2, suggestion.Options.Count);
        Assert.Equal("BTC, 2 matches: MEXC BTCUSDT, MEXC BTCUSDC.", suggestion.Sentence);
        Assert.All(suggestion.Options, o => Assert.True(o.RequiresExplicitPick));

        service.Run(suggestion.Options[0]);

        Assert.Contains("BTC, 2 matches. Arrow down to pick one.", Spoken);
        Assert.Equal(0, _orchestrator.LoadCalls);
    }

    [Fact]
    public void SeveralSymbolMatchesRunOnceTheUserArrowsOnOne()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            timeframe: "4h"));
        _orchestrator.AvailableSymbols = new[] { "BTCUSDT", "BTCUSDC" };
        var service = Build();

        var suggestion = service.Suggest("BTC GP");
        Assert.Equal(2, suggestion.Options.Count);
        service.Run(suggestion.Options[1], explicitlyChosen: true);

        WaitForSpoken("Loading MEXC BTCUSDC, 4 hours.");
        Assert.Equal(1, _orchestrator.LoadCalls);
        Assert.Equal("BTCUSDC", _orchestrator.LastSymbol);
    }

    [Fact]
    public void AnExactFullSymbolOnTheCurrentVenueRunsAlone()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            timeframe: "4h"));
        _orchestrator.AvailableSymbols = new[] { "BTCUSDT", "BTCUSDC" };
        var service = Build();

        var suggestion = service.Suggest("GP BTCUSDT");
        var option = Assert.Single(suggestion.Options);
        Assert.False(option.RequiresExplicitPick);
        service.Run(option);

        WaitForSpoken("Loading MEXC BTCUSDT, 4 hours.");
        Assert.Equal("BTCUSDT", _orchestrator.LastSymbol);
    }

    [Fact]
    public void SymbolSearchOrderIsVenueThenWatchlistsThenOtherVenues()
    {
        var watchlists = new[]
        {
            new Watchlist("w1", "Watching", new[]
            {
                new WatchlistEntry("Kraken", "BTCUSD", MarketType.Crypto, "Spot"),
                new WatchlistEntry("MEXC", "BTCUSDT", MarketType.Crypto, "Spot"),
            }),
        };
        var others = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Binance"] = new[] { "BTC-EUR" },
        };

        var found = GoBarSymbolSearch.Find(
            "BTC",
            "MEXC",
            new[] { "BTCUSDT" },
            watchlists,
            others,
            MarketType.Crypto,
            "Spot");

        Assert.Equal(
            new[] { ("MEXC", "BTCUSDT"), ("Kraken", "BTCUSD"), ("Binance", "BTC-EUR") },
            found.Select(m => (m.Provider, m.Symbol)).ToArray());
        Assert.Single(found, m => m.Provider == "MEXC" && m.Symbol == "BTCUSDT");
    }

    [Fact]
    public void SymbolFromAWatchlistSwitchesProviderAndKeepsItsMarket()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            timeframe: "4h"));
        var watchlists = Substitute.For<IWatchlistLibrary>();
        watchlists.All.Returns(new[]
        {
            new Watchlist("w1", "Watching", new[]
            {
                new WatchlistEntry("Kraken", "XBTUSD", MarketType.Crypto, "Perpetual"),
            }),
        });
        var service = Build(watchlists: watchlists);

        var option = Only(service, "GP XBT");
        Assert.Equal("Open Kraken XBTUSD", option.Name);
        service.Run(option);

        WaitForSpoken("Loading Kraken XBTUSD, 4 hours.");
        Assert.Equal("Kraken", _orchestrator.LastProvider);
        Assert.Equal("XBTUSD", _orchestrator.LastSymbol);
        Assert.Equal(MarketType.Crypto, _orchestrator.LastMarket);
        Assert.Equal("Perpetual", _orchestrator.LastSubType);
    }

    [Fact]
    public void SymbolNoMatchNamesWhereItLooked()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        _orchestrator.AvailableSymbols = new[] { "ETHUSDT" };
        var service = Build();

        var suggestion = service.Suggest("GP BTC");

        Assert.Empty(suggestion.Options);
        Assert.Equal("No BTC on MEXC or your watchlists.", suggestion.Sentence);
    }

    [Fact]
    public void SymbolShortcutSearchIgnoresSeparatorsAndCase()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        _orchestrator.AvailableSymbols = new[] { "BTC/USDT" };
        var service = Build();

        var option = Only(service, "GP btc-usdt");

        Assert.Equal("Open MEXC BTC/USDT", option.Name);
    }

    [Fact]
    public void SymbolNamesTheFirstFiveAndCountsTheRest()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        _orchestrator.AvailableSymbols = Enumerable.Range(1, 7).Select(i => $"BTC{i}USDT").ToList();
        var service = Build();

        var suggestion = service.Suggest("GP BTC");

        Assert.Equal(7, suggestion.Options.Count);
        Assert.Equal(
            "BTC, 7 matches: MEXC BTC1USDT, MEXC BTC2USDT, MEXC BTC3USDT, "
            + "MEXC BTC4USDT, MEXC BTC5USDT, and 2 more.",
            suggestion.Sentence);
    }

    // ── Indicators ─────────────────────────────────────────────────────────

    private static IndicatorMetadata Rsi() => new()
    {
        Code = "Rsi",
        Name = "RSI",
        Category = "Oscillator",
        DefaultPane = "",
        Parameters = new List<IndicatorParameterMetadata>
        {
            new() { Name = "Period", DataType = typeof(int), DefaultValue = 14 },
        },
    };

    private static IndicatorMetadata Ema(bool deprecated = false) => new()
    {
        Code = "Ema",
        Name = "EMA",
        Category = "Overlays",
        IsDeprecated = deprecated,
        Parameters = new List<IndicatorParameterMetadata>
        {
            new() { Name = "Period", DataType = typeof(int), DefaultValue = 20 },
        },
    };

    private static IIndicatorService Indicators(params IndicatorMetadata[] all)
    {
        var service = Substitute.For<IIndicatorService>();
        service.GetAvailableIndicators().Returns(all.ToList());
        return service;
    }

    [Fact]
    public void IndicatorAddsWithItsDefaultsAndSaysWhereItLands()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        var series = Substitute.For<ISeriesManagementService>();
        var service = Build(indicators: Indicators(Rsi()), series: series,
            demo: new DemoPolicy(isDemo: false));

        var option = Only(service, "IND rsi");
        Assert.Equal("Add RSI", option.Name);
        service.Run(option);

        Assert.Contains("RSI added, its own pane.", Spoken);
        series.Received(1).RegisterSeriesFromMetadata(
            Arg.Is<IndicatorMetadata>(m => m.Code == "Rsi"),
            Arg.Is<Dictionary<string, object>>(p =>
                p.Count == 1 && p.ContainsKey("Period") && (double)p["Period"] == 14.0));
    }

    [Fact]
    public void IndicatorSearchUsesTheDialogsFilteredList()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        var retired = Indicators(Rsi(), Ema(deprecated: true));
        var service = Build(indicators: retired, demo: new DemoPolicy(isDemo: false));

        // A retired indicator stays functional but is not offered.
        Assert.Equal("No indicator called ema.", service.Suggest("IND ema").Sentence);
        Assert.Single(service.Suggest("IND rsi").Options);

        // In the public demo the curated code list is the second filter.
        var notCurated = new IndicatorMetadata { Code = "CipherB", Name = "Cipher B" };
        var demo = Build(indicators: Indicators(Rsi(), notCurated), demo: new DemoPolicy(isDemo: true));
        Assert.Equal("No indicator called cipher.", demo.Suggest("IND cipher").Sentence);
        Assert.Single(demo.Suggest("IND rsi").Options);
    }

    [Fact]
    public void IndicatorOnAnAnalyticsChartIsRefused()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            shape: ProviderDataShape.SingleValueLine));
        var service = Build(indicators: Indicators(Rsi()), series: Substitute.For<ISeriesManagementService>(),
            demo: new DemoPolicy(isDemo: false));

        service.Run(Only(service, "IND rsi"));

        Assert.Contains("This is an analytics chart; indicators need a price chart.", Spoken);
    }

    [Fact]
    public void IndicatorWithoutAChartSaysNoChartLoaded()
    {
        Use(State());
        var service = Build(indicators: Indicators(Rsi()), series: Substitute.For<ISeriesManagementService>(),
            demo: new DemoPolicy(isDemo: false));

        service.Run(Only(service, "IND rsi"));

        Assert.Contains("No chart loaded.", Spoken);
    }

    // ── Price alert ────────────────────────────────────────────────────────

    [Fact]
    public void PriceAlertOpensTheFormWithTheValue()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        var service = Build();

        var option = Only(service, "ALERT 95k");
        Assert.Equal("Price alert at 95,000", option.Name);
        service.Run(option);

        var opened = Assert.Single(_bus.Log.OfType<OpenAlertsEvent>());
        Assert.Equal(AlertTarget.Price, opened.Target);
        Assert.Equal(95000, opened.Value);
    }

    [Fact]
    public void PriceAlertWithoutAChartSaysNoChartLoaded()
    {
        Use(State());
        var service = Build();

        service.Run(Only(service, "ALERT 95000"));

        Assert.Contains("No chart loaded.", Spoken);
        Assert.Empty(_bus.Log.OfType<OpenAlertsEvent>());
    }

    [Fact]
    public void AnImpossiblePriceIsRefusedInWords()
    {
        var service = Build();

        var suggestion = service.Suggest("ALERT 0");

        Assert.Empty(suggestion.Options);
        Assert.Equal("A price must be more than zero, like ALERT 95000.", suggestion.Sentence);
    }

    // ── GOTO a date ────────────────────────────────────────────────────────

    [Fact]
    public void GoToDateMovesToTheLastBarAtOrBefore()
    {
        Use(State(Daily(new DateTime(2024, 2, 27, 0, 0, 0, DateTimeKind.Utc), 8),
            timeframe: "1d", cursor: 7));
        var service = Build();

        service.Run(Only(service, "GOTO 2024-03-01"));

        var nav = Assert.Single(_store.DispatchedActions.OfType<NavigateAction>());
        Assert.Equal(3, nav.NewIndex);
        Assert.Empty(Spoken);
        Assert.Contains(_bus.Log, e => e is FeedbackRequestEvent);
    }

    [Fact]
    public void GoToDateBeforeTheFirstBarSaysSoAndDoesNotMove()
    {
        Use(State(Daily(new DateTime(2023, 1, 3, 0, 0, 0, DateTimeKind.Utc), 5),
            timeframe: "1d"));
        var service = Build();

        service.Run(Only(service, "GOTO 2019-03-01"));

        Assert.Contains(
            "1 March 2019 is before this chart's first bar, 3 January 2023.", Spoken);
        Assert.Empty(_store.DispatchedActions.OfType<NavigateAction>());
    }

    [Fact]
    public void GoToDateAfterTheLatestBarSaysSoAndDoesNotMove()
    {
        Use(State(Daily(new DateTime(2024, 2, 27, 0, 0, 0, DateTimeKind.Utc), 3),
            timeframe: "1d"));
        var service = Build();

        service.Run(Only(service, "GOTO 2024-03-01"));

        Assert.Contains("That is after the latest bar.", Spoken);
        Assert.Empty(_store.DispatchedActions.OfType<NavigateAction>());
    }

    [Fact]
    public void GoToDateUsesTheDisplayZoneItIsGiven()
    {
        // 00:00 in a +2 zone is 22:00Z the day before.
        var zone = TimeZoneInfo.CreateCustomTimeZone("att-test-zone", TimeSpan.FromHours(2), "Test", "Test");
        Use(State(Hourly(new DateTime(2024, 2, 29, 20, 0, 0, DateTimeKind.Utc), 8),
            timeframe: "1h"));
        var service = Build(zone: zone);

        service.Run(Only(service, "GOTO 2024-03-01"));

        var nav = Assert.Single(_store.DispatchedActions.OfType<NavigateAction>());
        Assert.Equal(2, nav.NewIndex);          // the 22:00Z bar reads 00:00 on 1 March
    }

    // ── GOTO an event ──────────────────────────────────────────────────────

    private static MarketEvent Event(string id, string spoken, string longName, int day)
        => new(
            id, MarketEventKind.Economy, spoken, longName,
            new[] { MarketType.Crypto }, MarketEventImportance.High,
            new DateTime(2024, 3, day, 13, 30, 0, DateTimeKind.Utc),
            MarketEventPrecision.Exact, new DateOnly(2024, 3, day), "test");

    private static IMarketEventBarMapCache Map(params MarketEventBarPlacement[] placements)
    {
        var map = Substitute.For<IMarketEventBarMapCache>();
        map.For(Arg.Any<WorkspaceState>(), Arg.Any<DateTime>())
            .Returns(placements.ToList());
        return map;
    }

    [Fact]
    public void GoToEventWhenEventsAreOffSaysSo()
    {
        Use(State(Daily(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), 5),
            timeframe: "1d", eventsOn: false));
        var service = Build();

        var suggestion = service.Suggest("GOTO CPI");
        Assert.Empty(suggestion.Options);
        Assert.Equal(
            "Market events are off. Turn them on in Settings to use GOTO with an event.",
            suggestion.Sentence);

        service.Run(new GoBarOption("Go to the last CPI",
            Action: new GoBarEventAction("cpi", "CPI")));
        Assert.Contains(
            "Market events are off. Turn them on in Settings to use GOTO with an event.", Spoken);
    }

    [Fact]
    public void GoToEventWithNoneOnThisChartSaysSo()
    {
        Use(State(Daily(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), 5),
            timeframe: "1d", eventsOn: true));
        var service = Build(eventMap: Map());

        var suggestion = service.Suggest("GOTO CPI");
        Assert.Empty(suggestion.Options);
        Assert.Equal("No CPI on this chart.", suggestion.Sentence);

        service.Run(new GoBarOption("Go to the last CPI",
            Action: new GoBarEventAction("cpi", "CPI")));
        Assert.Contains("No CPI on this chart.", Spoken);
    }

    [Fact]
    public void GoToEventMovesToTheLatestPlacedBar()
    {
        var cpi = Event("cpi-1", "CPI", "Consumer price index", 12);
        Use(State(Daily(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), 20),
            timeframe: "1d", eventsOn: true, cursor: 0));
        var service = Build(eventMap: Map(new MarketEventBarPlacement(cpi, 12, "scheduled")));

        var option = Only(service, "GOTO CPI");
        Assert.Equal("Go to the last CPI", option.Name);
        service.Run(option);

        var nav = Assert.Single(_store.DispatchedActions.OfType<NavigateAction>());
        Assert.Equal(12, nav.NewIndex);
    }

    [Fact]
    public void GoToEventListsDistinctNamesAsOptions()
    {
        var cpi = Event("cpi-1", "CPI", "Consumer price index", 12);
        var core = Event("cpi-2", "Core CPI", "Core consumer price index", 13);
        Use(State(Daily(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), 20),
            timeframe: "1d", eventsOn: true));
        var service = Build(eventMap: Map(
            new MarketEventBarPlacement(cpi, 12, "scheduled"),
            new MarketEventBarPlacement(core, 13, "scheduled")));

        var suggestion = service.Suggest("GOTO CPI");

        Assert.Equal(2, suggestion.Options.Count);
        Assert.Equal(
            new[] { "Go to the last Core CPI", "Go to the last CPI" },
            suggestion.Options.Select(o => o.Name).ToArray());
    }

    // ── FIND ───────────────────────────────────────────────────────────────

    private static readonly SdkCandlePatternAnalyzer RealAnalyzer = new();

    private static CandleAnalysis Hammer() => new()
    {
        Direction = CandleDirection.Bullish,
        Type = AccessibleTrader.Sdk.Analysis.CandleType.Hammer,
        Pattern = AccessibleTrader.Sdk.Analysis.CandlePattern.None,
        PatternBarCount = 1,
        BodyPercent = 20,
        UpperWickPercent = 10,
        LowerWickPercent = 70,
        ChangePercent = 0,
        IsReversal = true,
        IsContinuation = false,
    };

    private static CandleAnalysis Ordinary() => new()
    {
        Direction = CandleDirection.Bullish,
        Type = AccessibleTrader.Sdk.Analysis.CandleType.Normal,
        Pattern = AccessibleTrader.Sdk.Analysis.CandlePattern.None,
        PatternBarCount = 1,
        BodyPercent = 80,
        UpperWickPercent = 10,
        LowerWickPercent = 10,
        ChangePercent = 0,
        IsReversal = false,
        IsContinuation = false,
    };

    /// <summary>The same downtrend fixture the analyzer's own tests use, with a known hammer.</summary>
    private static List<Ohlcv> DowntrendThenHammer()
    {
        static Ohlcv B(double o, double h, double l, double c, int day)
            => new(new DateTime(2026, 1, 1).AddDays(day), o, h, l, c, 1000);

        var bars = Enumerable.Range(0, 6)
            .Select(i => B(120 - i, 120 - i + 0.5, 120 - i - 1.5, 120 - i - 1, i))
            .ToList();
        bars.Add(B(105, 106, 104.5, 106, 6));
        bars.Add(B(100, 100.6, 94, 100.4, 7));
        return bars;
    }

    [Fact]
    public void FindCandleMovesToTheSameBarTheClassifierNames()
    {
        var data = DowntrendThenHammer();
        Use(State(data, timeframe: "1d", cursor: 0));
        var service = Build(candles: RealAnalyzer);

        var suggestion = service.Suggest("FIND hammer");
        var option = Assert.Single(
            suggestion.Options, o => o.Name == "Find the next Hammer");
        service.Run(option);

        var nav = Assert.Single(_store.DispatchedActions.OfType<NavigateAction>());
        Assert.Equal(7, nav.NewIndex);

        var atLanding = CandlePatternSpeech.AnalyzeAt(RealAnalyzer, data, nav.NewIndex, heikinAshi: false);
        Assert.Equal("Hammer", CandlePatternSpeech.DescribeShape(atLanding));
    }

    [Fact]
    public void FindCandleWhenNoneAfterCountsTheEarlierOnes()
    {
        var candles = Substitute.For<ISdkCandlePatternAnalyzer>();
        candles.Analyze(default, default, default, default).ReturnsForAnyArgs(Hammer());
        var data = Daily(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3);
        Use(State(data, timeframe: "1d", cursor: 2));
        var service = Build(candles: candles);

        service.Run(service.Suggest("FIND hammer").Options
            .Single(o => o.Name == "Find the next Hammer"));

        Assert.Contains("No Hammer after this bar; 3 before it.", Spoken);
        Assert.Empty(_store.DispatchedActions.OfType<NavigateAction>());
    }

    /// <summary>
    /// A hammer that is also the last bar of a multi-bar pattern is read with the pattern's
    /// name first, but it is still a hammer: FIND hammer must stop on it (review fix).
    /// </summary>
    [Fact]
    public void FindCandleStopsOnAShapeInsideAMultiBarPattern()
    {
        var candles = Substitute.For<ISdkCandlePatternAnalyzer>();
        candles.Analyze(default, default, default, default).ReturnsForAnyArgs(
            Hammer() with { Pattern = AccessibleTrader.Sdk.Analysis.CandlePattern.MorningStar, PatternBarCount = 3 });
        var data = Daily(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3);
        Use(State(data, timeframe: "1d", cursor: 0));
        var service = Build(candles: candles);

        service.Run(service.Suggest("FIND hammer").Options
            .Single(o => o.Name == "Find the next Hammer"));

        var nav = Assert.Single(_store.DispatchedActions.OfType<NavigateAction>());
        Assert.Equal(1, nav.NewIndex);
    }

    /// <summary>
    /// FIND must use the flag the arrow keys use. The fake analyzer only names a hammer when
    /// handed the Heikin-Ashi close, so a fresh analyzer with the raw flag would find nothing.
    /// </summary>
    [Fact]
    public void FindCandleUsesTheChartHeikinAshiFlag()
    {
        var data = Daily(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 8, close: 100);
        var ha = ChartMath.SeriesAsDrawn(data, isHeikinAshi: true);
        var target = 4;
        double haClose = ha[target].Close;   // (O+H+L+C) / 4, never the raw close here

        var candles = Substitute.For<ISdkCandlePatternAnalyzer>();
        candles.Analyze(default, default, default, default).ReturnsForAnyArgs(call =>
            Math.Abs(call.ArgAt<Ohlcv>(0).Close - haClose) < 0.0001 ? Hammer() : Ordinary());
        Use(State(data, timeframe: "1d", cursor: 0, heikinAshi: true));
        var service = Build(candles: candles);

        service.Run(service.Suggest("FIND hammer").Options
            .Single(o => o.Name == "Find the next Hammer"));

        var nav = Assert.Single(_store.DispatchedActions.OfType<NavigateAction>());
        Assert.Equal(target, nav.NewIndex);
    }

    [Fact]
    public void FindFormationWhenDescriptionIsOffKeepsTheRefusal()
    {
        Use(State(Daily(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 40),
            timeframe: "1d", cursor: 0));
        var service = Build(patterns: Substitute.For<IChartPatternCache>());

        service.Run(Only(service, "FIND double top"));

        Assert.Contains(
            "Chart formation description is off. Turn it on in Settings to use these keys.", Spoken);
        Assert.Empty(_store.DispatchedActions.OfType<NavigateAction>());
    }

    [Fact]
    public void FindFormationMovesToTheNextEdge()
    {
        var data = Daily(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 40);
        Use(State(data, timeframe: "1d", cursor: 4, eventsOn: false) with
        {
            DescribeChartPatterns = true,
        });
        var patterns = Substitute.For<IChartPatternCache>();
        patterns.For(Arg.Any<ChartIdentity>(), Arg.Any<IReadOnlyList<Ohlcv>>())
            .Returns(new[]
            {
                new ChartPattern(ChartPatternKind.DoubleTop, ChartPatternState.Forming,
                    StartBarIndex: 2, EndBarIndex: 6, KnownAtIndex: 6, TriggerLevel: 99,
                    StartTime: data[2].Date, EndTime: data[6].Date, CompletedAtIndex: 8),
            });
        var service = Build(patterns: patterns);

        service.Run(Only(service, "FIND double top"));

        var nav = Assert.Single(_store.DispatchedActions.OfType<NavigateAction>());
        Assert.Equal(6, nav.NewIndex);
    }

    // ── Closing the box first ──────────────────────────────────────────────

    [Fact]
    public void AnArgumentOptionWaitsForTheBoxToClose()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            timeframe: "1h"));
        var service = Build();
        _bus.Publish(new ModalStateChangedEvent(true, "GoBar"));

        service.Run(Only(service, "TF 4h"));

        Assert.Contains(_bus.Log, e => e is CloseGoBarRequestEvent);
        Assert.Empty(Spoken);
        Assert.Equal(0, _orchestrator.LoadCalls);

        _bus.Publish(new ModalStateChangedEvent(false, "GoBar"));

        WaitForSpoken("Loading BTCUSDT, 4 hours.");
        Assert.Equal(1, _orchestrator.LoadCalls);
    }

    [Fact]
    public void APriceAlertAlsoWaitsForTheBoxToClose()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        var service = Build();
        _bus.Publish(new ModalStateChangedEvent(true, "GoBar"));

        service.Run(Only(service, "ALERT 95000"));
        Assert.Empty(_bus.Log.OfType<OpenAlertsEvent>());

        _bus.Publish(new ModalStateChangedEvent(false, "GoBar"));
        Assert.Single(_bus.Log.OfType<OpenAlertsEvent>());
    }

    // ── The real orchestrator's one load path ──────────────────────────────

    private static (MarketOrchestrator Orch, RecordingStore Store, IDataService Data, IDataManager Manager)
        RealOrchestrator(DemoPolicy? demo = null, WorkspaceState? state = null)
    {
        var data = Substitute.For<IDataService>();
        data.LoadAvailableMarketsAsync().Returns(new List<string> { "Crypto" });
        data.LoadProvidersByMarketTypeAsync(Arg.Any<string>())
            .Returns(new List<string> { "MEXC", "Kraken" });
        data.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new List<string> { "Spot" });
        data.LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new List<string> { "BTCUSDT" });
        data.GetSupportedTimeframesAsync(Arg.Any<string>())
            .Returns(new List<string> { "1h", "4h" });
        data.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(false);
        data.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(true);

        var store = new RecordingStore { State = state ?? State() };
        var manager = Substitute.For<IDataManager>();
        var orch = new MarketOrchestrator(
            data, manager, store, Substitute.For<IWorkspaceInitializer>(),
            new EventBus(), demo ?? new DemoPolicy(isDemo: false));
        return (orch, store, data, manager);
    }

    [Fact]
    public async Task LoadOnActiveTabStartsFromTheTabsIdentityNotTheToolbarStray()
    {
        var tab = State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
            provider: "MEXC", symbol: "BTCUSDT", timeframe: "1h");
        var (orch, store, data, _) = RealOrchestrator(state: tab);
        orch.SelectedProvider = "MEXC";
        orch.SelectedSymbol = "ETHUSDT";      // a dropdown the user never loaded
        orch.SelectedTimeframe = "1d";

        await orch.LoadOnActiveTabAsync(provider: null, symbol: null, timeframe: "4h",
            market: null, subType: null);

        var identity = Assert.Single(store.Actions.OfType<SetIdentityAction>()).Identity;
        Assert.Equal("MEXC", identity.Provider);
        Assert.Equal("BTCUSDT", identity.Symbol);
        Assert.Equal("4h", identity.Timeframe);
    }

    [Fact]
    public async Task LoadOnActiveTabTurnsTheDemosSilentRefusalIntoAnOutcome()
    {
        var tab = State(Daily(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3),
            provider: "Bitstamp", symbol: "BTC/USD", timeframe: "1d");
        var (orch, _, _, manager) = RealOrchestrator(demo: new DemoPolicy(isDemo: true), state: tab);

        var outcome = await orch.LoadOnActiveTabAsync(null, null, "15m", null, null);

        Assert.Equal(GoBarLoadOutcomeKind.NotInDemo, outcome.Kind);
        await manager.DidNotReceive().RefreshDataAsync();
    }

    [Fact]
    public async Task LoadOnActiveTabRefusesAShapeChangeThatWouldStripIndicators()
    {
        var ema = new ChartSeries(
            new SeriesConfig { Id = "ema-20", Name = "EMA 20" },
            new SeriesDataBuffer { SeriesId = "ema-20" });
        var tab = State(Daily(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3),
            provider: "MEXC", symbol: "BTCUSDT", timeframe: "1d",
            series: ImmutableList.Create(ema));
        var (orch, _, data, manager) = RealOrchestrator(state: tab);
        var provider = Substitute.For<IMarketDataProvider>();
        provider.GetDataShapeForSymbol(Arg.Any<string>())
            .Returns(ProviderDataShape.SingleValueLine);
        data.GetProviderAsync(Arg.Any<string>()).Returns(provider);

        var outcome = await orch.LoadOnActiveTabAsync(null, "FEAR", null, null, null);

        Assert.Equal(GoBarLoadOutcomeKind.ShapeChange, outcome.Kind);
        Assert.Equal(1, outcome.NonCoreSeries);
        await manager.DidNotReceive().RefreshDataAsync();
    }

    [Fact]
    public async Task LoadOnActiveTabRefreshesSymbolsOnlyWhenTheProviderChanges()
    {
        var tab = State(Daily(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 3),
            provider: "MEXC", symbol: "BTCUSDT", timeframe: "1d");
        var (orch, _, data, _) = RealOrchestrator(state: tab);
        orch.SelectedProvider = "MEXC";

        await orch.LoadOnActiveTabAsync(null, null, "4h", null, null);

        await data.DidNotReceive().LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>());

        await orch.LoadOnActiveTabAsync("Kraken", null, null, null, null);

        await data.Received(1).LoadSymbolsAsync(Arg.Any<string>(), "Kraken");
    }

    // ── A verb word inside a command's own name (review fix) ──────────────

    private static IndicatorMetadata Named(string code, string name) => new()
    {
        Code = code,
        Name = name,
        Category = "Oscillators",
        Parameters = new List<IndicatorParameterMetadata>(),
    };

    /// <summary>
    /// "add tab", "Change the timeframe", "focus the chart" and "alert manager" each hold a verb
    /// word (ADD, TIMEFRAME, CHART, ALERT). Every 6a name, code and alias must still put its own
    /// command first, exactly as the 6a matcher alone ranks it (money aliases are left out: the
    /// matcher refuses them by design). The resolvers are given symbols and indicators that DO
    /// match the leftover words, so the argument options are really there to compete.
    /// </summary>
    [Fact]
    public void EveryCommandsOwnWordsStillPutThatCommandFirst()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        _orchestrator.AvailableSymbols = new[] { "FOCUSUSDT", "LOADUSDT", "SPEECHUSDT", "PLAYUSDT" };
        _orchestrator.AvailableProviders = new[] { "Change The Exchange" };
        var service = Build(
            indicators: Indicators(Named("TabInd", "Tab Indicator"), Named("IndInd", "Indicator Index")),
            demo: new DemoPolicy(isDemo: false));

        var wrong = new List<string>();
        foreach (var entry in GoBarRegistry.All)
        {
            var words = new[] { entry.Name }.Concat(entry.Codes);
            if (entry.Kind != GoBarKind.Money) words = words.Concat(entry.Aliases);

            foreach (var text in words)
            {
                var expected = GoBarMatcher.Match(text, GoBarRegistry.All).FirstOrDefault()?.Entry;
                var top = service.Suggest(text).Options.FirstOrDefault();
                if (expected != null && top?.Entry?.Command != expected.Command)
                    wrong.Add($"\"{text}\" -> {top?.Name ?? "nothing"} (wanted {expected.Name})");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void AnExactCommandLeadsAndTheArgumentOptionsStillFollow()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        var service = Build(indicators: Indicators(Named("TabInd", "Tab Indicator")),
            demo: new DemoPolicy(isDemo: false));

        var options = service.Suggest("add tab").Options;

        Assert.Equal(SystemCommand.AddTab, options[0].Entry?.Command);
        Assert.Contains(options, o => o.Action is GoBarIndicatorAction { Code: "TabInd" });
    }

    [Fact]
    public void AnArgumentLeadsWhileTypingAndTheCommandIsStillListed()
    {
        Use(State(Hourly(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10)));
        var service = Build(indicators: Indicators(Named("TabInd", "Tab Indicator")),
            demo: new DemoPolicy(isDemo: false));

        var options = service.Suggest("add ta").Options;

        Assert.True(options[0].Action is GoBarIndicatorAction { Code: "TabInd" },
            $"top was {options[0].Name}");
        Assert.Contains(options, o => o.Entry?.Command == SystemCommand.AddTab);
    }

    [Fact]
    public void ARefusedArgumentGivesWayToAMatchingCommand()
    {
        var service = Build();

        var suggestion = service.Suggest("alert manager");

        Assert.Equal(SystemCommand.OpenAlerts, suggestion.Options[0].Entry?.Command);
        Assert.DoesNotContain("price", suggestion.Sentence, StringComparison.OrdinalIgnoreCase);
    }
}
