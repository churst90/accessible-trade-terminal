using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>The bar-to-event map is built once per chart data, not once per key press.</summary>
public class MarketEventBarMapCacheTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OneCachePassedToBothServicesIsBuiltOnce()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };
        var e = MarketEventTestData.Event(
            moment: new DateTime(2024, 3, 11, 12, 30, 0, DateTimeKind.Utc));
        var state = MarketEventTestData.State(bars, timeframe: "1d", level: "all");
        var map = new MarketEventBarMapCache(MarketEventTestData.Calendar(e));
        var shared = new CountingMap(map);

        var store = new MockWorkspaceStore();
        var formatter = new SpeechFormatter();
        var router = new SpeechFeedbackRouter(new CounterSpeechManager(), formatter, store);
        var coordinator = new AccessibilityFeedbackCoordinator(
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
            marketEventMap: shared);
        store.EmitState(state);
        var detail = new BarDetailService(new SpyEventBus(), marketEvents: shared);

        _ = coordinator.MarketEventContext();
        detail.AnnounceDetails(state);

        Assert.Equal(2, shared.Reads);
        Assert.Equal(1, map.BuildCount);
    }

    private sealed class CountingMap : IMarketEventBarMapCache
    {
        private readonly MarketEventBarMapCache _inner;

        public CountingMap(MarketEventBarMapCache inner) => _inner = inner;

        public int Reads { get; private set; }

        public IReadOnlyList<MarketEventBarPlacement> For(WorkspaceState state, DateTime nowUtc)
        {
            Reads++;
            return _inner.For(state, nowUtc);
        }
    }

    [Fact]
    public void ASecondReadDoesNotRebuildTheMap()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var cache = new MarketEventBarMapCache(MarketEventTestData.Calendar(e));
        var state = MarketEventTestData.State(bars, timeframe: "1d");

        var first = cache.For(state, Now);
        var second = cache.For(state, Now);

        Assert.Equal(1, cache.BuildCount);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Select(p => p.BarIndex), second.Select(p => p.BarIndex));
    }

    [Fact]
    public void AStockChartsDayIsNewYorkTimeIncludingDaylightSaving()
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };
        // 00:30 New York on the 12th in March (EDT); a fixed UTC-5 would call it the 11th.
        var e = MarketEventTestData.Event(
            id: "dst", moment: new DateTime(2024, 3, 12, 4, 30, 0, DateTimeKind.Utc));
        var cache = new MarketEventBarMapCache(MarketEventTestData.Calendar(e));
        var state = MarketEventTestData.State(bars, timeframe: "1d", market: MarketType.Stock);

        var placed = cache.For(state, Now).Single(p => p.Event.Id == "dst" && p.IsPlaced);

        Assert.Equal(1, placed.BarIndex);
    }

    [Fact]
    public void NewBarsRebuildTheMap()
    {
        var bars = new List<Ohlcv>
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var cache = new MarketEventBarMapCache(MarketEventTestData.Calendar(e));
        var state = MarketEventTestData.State(bars, timeframe: "1d");

        cache.For(state, Now);
        bars.Add(MarketEventTestData.Bar(new DateTime(2024, 3, 13, 0, 0, 0, DateTimeKind.Utc)));
        cache.For(MarketEventTestData.State(bars, timeframe: "1d"), Now);

        Assert.Equal(2, cache.BuildCount);
    }

    [Fact]
    public void ScheduledIsAppliedOnReadWithoutRebuilding()
    {
        var bars = new[] { MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)) };
        var e = MarketEventTestData.Event(moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var cache = new MarketEventBarMapCache(MarketEventTestData.Calendar(e));
        var state = MarketEventTestData.State(bars, timeframe: "1d");

        var before = cache.For(state, new DateTime(2024, 3, 12, 10, 0, 0, DateTimeKind.Utc));
        var after = cache.For(state, new DateTime(2024, 3, 12, 13, 0, 0, DateTimeKind.Utc));

        var beforePlaced = Assert.Single(before, p => p.IsPlaced);
        var afterPlaced = Assert.Single(after, p => p.IsPlaced);
        Assert.Equal(MarketEventPlacement.Scheduled, beforePlaced.Placement);
        Assert.Null(afterPlaced.Placement);
        Assert.Equal(1, cache.BuildCount);
    }

    /// <summary>
    /// The apostrophe jumps read the same map as the arrow keys and Alt+Shift+D. CommandDispatcher
    /// takes its MarketEventNavigator as an optional parameter, so if no head registers the
    /// navigator, dependency injection passes null and the dispatcher quietly builds a navigator
    /// with a private third map (it did, until 2026-11-18). Both heads must register it, with the
    /// same lifetime as the shared map, so the scoped map is the one injected.
    /// </summary>
    [Theory]
    [InlineData("AccessibleTrader.WebHost/ServiceCollectionExtensions.cs", "AddScoped")]
    [InlineData("AccessibleTrader.BlazorClient/ServiceCollectionExtensions.cs", "AddSingleton")]
    public void EachHeadRegistersTheJumpNavigatorBesideTheSharedMap(string file, string lifetime)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, file);
        Assert.True(File.Exists(path), $"{file} not found at {path}");
        string text = File.ReadAllText(path);

        Assert.Contains($"services.{lifetime}<IMarketEventBarMapCache, MarketEventBarMapCache>();", text);
        Assert.Contains($"services.{lifetime}<MarketEventNavigator>();", text);
    }
}
