using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// The setting: off by default everywhere, levels that gate by importance, five kind boxes, and
/// the market type filter. It travels the same path as DescribeCandlePatterns.
/// </summary>
public class MarketEventSettingsTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AppSettingsDefaultsToOffWithEveryKindTicked()
    {
        var app = new AppSettings(new DictSettings());

        Assert.Equal("off", app.MarketEventsLevel);
        Assert.True(app.MarketEventsEconomy);
        Assert.True(app.MarketEventsFed);
        Assert.True(app.MarketEventsExpiries);
        Assert.True(app.MarketEventsHolidays);
        Assert.True(app.MarketEventsHalvings);
        Assert.True(app.MarketEventsSoundOnEventBars);
        Assert.Equal("15,1", app.MarketEventsCountdowns);
    }

    [Fact]
    public void WorkspaceStateDefaultsToOffWithEveryKindTicked()
    {
        var state = WorkspaceState.Initial;

        Assert.Equal("off", state.MarketEventsLevel);
        Assert.True(state.MarketEventsEconomy);
        Assert.True(state.MarketEventsFed);
        Assert.True(state.MarketEventsExpiries);
        Assert.True(state.MarketEventsHolidays);
        Assert.True(state.MarketEventsHalvings);
        Assert.True(state.MarketEventsSoundOnEventBars);
        Assert.Equal("15,1", state.MarketEventsCountdowns);
    }

    [Theory]
    [InlineData("off", MarketEventLevel.Off, "Off")]
    [InlineData("importantOnly", MarketEventLevel.ImportantOnly, "Important only")]
    [InlineData("importantAndMedium", MarketEventLevel.ImportantAndMedium, "Important and medium")]
    [InlineData("all", MarketEventLevel.All, "All")]
    public void TheLevelRoundTripsAndIsSaidInWords(string setting, MarketEventLevel level, string label)
    {
        Assert.Equal(level, MarketEventLevels.Parse(setting));
        Assert.Equal(setting, MarketEventLevels.ToSetting(level));
        Assert.Equal(label, MarketEventLevels.Label(level));
    }

    [Fact]
    public void LevelAndKindsSurviveASaveAndReload()
    {
        var settings = new AppSettings(new DictSettings())
        {
            MarketEventsLevel = "all",
            MarketEventsEconomy = false,
            MarketEventsFed = true,
            MarketEventsExpiries = false,
            MarketEventsHolidays = true,
            MarketEventsHalvings = false,
            MarketEventsSoundOnEventBars = false,
            MarketEventsCountdowns = "30,15,1",
        };

        var store = new MockWorkspaceStore();
        using var svc = new PreferencePersistenceService(store, settings, NullLogger<PreferencePersistenceService>.Instance);
        svc.Initialize();

        var seeded = store.DispatchedActions.OfType<UpdateSettingsAction>()
            .Select(a => a.Updater(WorkspaceState.Initial))
            .Last();

        Assert.Equal("all", seeded.MarketEventsLevel);
        Assert.False(seeded.MarketEventsEconomy);
        Assert.True(seeded.MarketEventsFed);
        Assert.False(seeded.MarketEventsExpiries);
        Assert.True(seeded.MarketEventsHolidays);
        Assert.False(seeded.MarketEventsHalvings);
        Assert.False(seeded.MarketEventsSoundOnEventBars);
        Assert.Equal("30,15,1", seeded.MarketEventsCountdowns);
    }

    [Fact]
    public void EachLevelGatesByImportance()
    {
        string Say(MarketEventLevel level, MarketEventImportance importance)
            => MarketEventSpeech.Clause(
                new[] { MarketEventTestData.Placed(MarketEventTestData.Event(importance: importance), 0) },
                1800,
                level,
                MarketEventKindFilter.All,
                MarketType.Crypto);

        Assert.Equal("", Say(MarketEventLevel.Off, MarketEventImportance.High));
        Assert.NotEqual("", Say(MarketEventLevel.ImportantOnly, MarketEventImportance.High));
        Assert.Equal("", Say(MarketEventLevel.ImportantOnly, MarketEventImportance.Medium));
        Assert.Equal("", Say(MarketEventLevel.ImportantOnly, MarketEventImportance.Low));
        Assert.NotEqual("", Say(MarketEventLevel.ImportantAndMedium, MarketEventImportance.Medium));
        Assert.Equal("", Say(MarketEventLevel.ImportantAndMedium, MarketEventImportance.Low));
        Assert.NotEqual("", Say(MarketEventLevel.All, MarketEventImportance.Low));
    }

    [Fact]
    public void AnUntickedKindIsNotSpoken()
    {
        var fed = MarketEventTestData.Event(
            kind: MarketEventKind.Fed, spoken: "Fed decision", longName: "Fed decision");

        string said = MarketEventSpeech.Clause(
            new[] { MarketEventTestData.Placed(fed, 0) },
            1800,
            MarketEventLevel.All,
            new MarketEventKindFilter(Economy: true, Fed: false, Expiries: true, Holidays: true, Halvings: true),
            MarketType.Crypto);

        Assert.Equal("", said);
    }

    [Fact]
    public void AHalvingIsNotSpokenOnAStockChart()
    {
        var halving = MarketEventTestData.Event(
            kind: MarketEventKind.Halving,
            spoken: "Bitcoin halving",
            longName: "Bitcoin halving",
            moment: new DateTime(2024, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2024, 4, 20),
            scope: MarketType.Crypto);

        string said = MarketEventSpeech.Clause(
            new[] { MarketEventTestData.Placed(halving, 0) },
            1800,
            MarketEventLevel.All,
            MarketEventKindFilter.All,
            MarketType.Stock);

        Assert.Equal("", said);
    }

    [Fact]
    public void AStockExpiryIsNotSpokenOnACryptoChart()
    {
        var expiry = MarketEventTestData.Event(
            kind: MarketEventKind.Expiry,
            spoken: "options expiry",
            importance: MarketEventImportance.Low,
            moment: new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            precision: MarketEventPrecision.DayOnly,
            date: new DateOnly(2024, 3, 15),
            scope: new[] { MarketType.Stock, MarketType.Index, MarketType.Options });

        string said = MarketEventSpeech.Clause(
            new[] { MarketEventTestData.Placed(expiry, 0) },
            1800,
            MarketEventLevel.All,
            MarketEventKindFilter.All,
            MarketType.Crypto);

        Assert.Equal("", said);
    }

    [Fact]
    public void WithTheLevelOffTheArrowClauseIsEmpty()
    {
        var bars = new[] { MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)) };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var state = MarketEventTestData.State(bars, timeframe: "1d", level: "off");

        string said = Coordinator(state, e).MarketEventContext();

        Assert.Equal("", said);
    }

    [Fact]
    public void WithTheLevelOnTheArrowClauseNamesTheEvent()
    {
        var bars = new[] { MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)) };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var state = MarketEventTestData.State(bars, timeframe: "1d", level: "all");

        string said = Coordinator(state, e).MarketEventContext();

        Assert.Contains("CPI", said);
    }

    private static AccessibilityFeedbackCoordinator Coordinator(WorkspaceState state, MarketEvent e)
    {
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var speech = new CounterSpeechManager();
        var formatter = new SpeechFormatter();
        var router = new SpeechFeedbackRouter(speech, formatter, store);
        var coordinator = new AccessibilityFeedbackCoordinator(
            store,
            new NavigationFeedbackManager(router, formatter),
            router,
            new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
            formatter,
            bus,
            new MockEarconService(),
            new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(),
            new MockAutoNarrationService(),
            marketEventMap: new MarketEventBarMapCache(MarketEventTestData.Calendar(e)));
        store.EmitState(state);
        return coordinator;
    }

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
}
