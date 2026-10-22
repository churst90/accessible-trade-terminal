using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A restored workspace tab opens with the history it had when it was closed.</b>
///
/// <para>
/// Cody, 2026-10-22: "the workspace should load with all of the bars it had when it was closed
/// last rather than defaulting to 200, 200 is the default for new fresh charts." Nothing saved
/// how much history a tab held, so every restore opened at 200 bars — and a drawing anchored
/// before those 200 was drawn from the first loaded bar with the wrong slope (the gold trend
/// line from 29 Jan: 385.63 where it belongs at 405.68, on 120 bars).
/// </para>
///
/// <para>
/// Found on the way: the restored ZOOM WIDTH was lost too. WorkspaceInitializer set it before
/// the data arrived, and DataManager's fresh-load <c>ZoomAction(100)</c> put it back to 100
/// straight after. ViewportRestoreTests tests the reducer alone, so it could not see that.
/// </para>
///
/// <para>
/// These run the real restore (WorkspaceInitializer.RestoreWorkspace) into a real store, then
/// the real DataManager → ChartFeed load over a scripted orchestrator, and read the store.
/// </para>
/// </summary>
public class RestoredHistoryDepthTests
{
    private static readonly DateTime Day0 = new(2026, 1, 1);
    private static Ohlcv Bar(int day) => new(Day0.AddDays(day), 100, 101, 99, 100, 1);
    private static List<Ohlcv> Days(int from, int toInclusive) =>
        Enumerable.Range(from, toInclusive - from + 1).Select(Bar).ToList();

    private sealed class Rig
    {
        public readonly KeyedFeedsTests.FakeOrchestrator Orch = new();
        public readonly WorkspaceStore Store;
        public readonly WorkspaceInitializer Initializer;
        public readonly DataManager Manager;
        public readonly WorkspaceLibraryService Library =
            new(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths());

        public Rig()
        {
            var bus = new EventBus();
            Store = new WorkspaceStore(bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
            var indicators = Substitute.For<IIndicatorService>();
            indicators.GetAvailableIndicators().Returns(new List<IndicatorMetadata>());
            Initializer = new WorkspaceInitializer(Substitute.For<ISeriesManagementService>(), Store, indicators);
            var hub = new MarketFeedHub(Orch, Substitute.For<IDataService>(), new DemoPolicy(false), NullLoggerFactory.Instance);
            Manager = new DataManager(hub, Store, bus, NullLogger<DataManager>.Instance, Substitute.For<IServiceProvider>());
        }

        /// <summary>What resume does: restore the config, focus the feed, load the active tab
        /// (empty snapshot → the fresh-load path).</summary>
        public async Task RestoreAndLoad(TabConfiguration tab)
        {
            Initializer.RestoreWorkspace(new WorkspaceConfiguration { Tabs = { tab } });
            Manager.Identity = Store.State.Identity;
            await Manager.CatchUpFromSnapshotAsync(Store.State.Data);
        }
    }

    private static TabConfiguration Tab(DateTime? earliest = null, int zoom = 0) => new()
    {
        Market = "Spot", Provider = "TestProv", Symbol = "SPY", Timeframe = "1d",
        ViewportLength = zoom, EarliestBarDate = earliest
    };

    [Fact]
    public async Task A_restored_tab_reloads_back_to_the_first_bar_it_had()
    {
        var rig = new Rig();
        rig.Orch.FetchResults.Enqueue(Days(300, 499)); // the live-edge 200
        rig.Orch.FetchResults.Enqueue(Days(100, 299)); // older page
        rig.Orch.FetchResults.Enqueue(Days(0, 99));    // older page, reaches the saved first bar

        await rig.RestoreAndLoad(Tab(earliest: Day0));

        var data = rig.Store.State.Data;
        Assert.Equal(500, data.Count);
        Assert.Equal(Day0, data[0].Date);
        Assert.Equal(Day0.AddDays(499), data[^1].Date);
        Assert.Equal(3, rig.Orch.FetchCalls.Count); // stopped once the date was reached
        Assert.Null(rig.Orch.FetchCalls[0].Until);  // the live edge is fetched as a fresh load fetches it
        Assert.Null(rig.Store.State.RestoredLoad);  // consumed
    }

    [Fact]
    public async Task A_restored_tab_keeps_its_saved_zoom_width()
    {
        var rig = new Rig();
        rig.Orch.FetchResults.Enqueue(Days(0, 199));

        await rig.RestoreAndLoad(Tab(zoom: 60));

        Assert.Equal(60, rig.Store.State.ViewportLength);
        Assert.Equal(199, rig.Store.State.CurrentDataIndex); // still opens at the live edge
    }

    [Fact]
    public async Task A_fresh_load_after_the_restore_is_200_bars_at_the_default_zoom()
    {
        var rig = new Rig();
        rig.Orch.FetchResults.Enqueue(Days(300, 499));
        rig.Orch.FetchResults.Enqueue(Days(100, 299));
        await rig.RestoreAndLoad(Tab(earliest: Day0.AddDays(100), zoom: 60));
        Assert.Equal(400, rig.Store.State.Data.Count);

        rig.Orch.FetchCalls.Clear();
        rig.Orch.FetchResults.Enqueue(Days(300, 499));
        await rig.Manager.RefreshDataAsync(); // Load Chart on the same tab

        Assert.Equal(200, rig.Store.State.Data.Count);
        Assert.Single(rig.Orch.FetchCalls);
        Assert.Equal(100, rig.Store.State.ViewportLength);
    }

    [Fact]
    public async Task A_different_symbol_loaded_before_the_restored_data_is_a_fresh_chart()
    {
        var rig = new Rig();
        rig.Initializer.RestoreWorkspace(new WorkspaceConfiguration { Tabs = { Tab(earliest: Day0, zoom: 60) } });
        rig.Store.Dispatch(new SetIdentityAction(new ChartIdentity("Spot", "TestProv", "QQQ", "1d")));
        rig.Manager.Identity = rig.Store.State.Identity;
        rig.Orch.FetchResults.Enqueue(Days(300, 499));

        await rig.Manager.RefreshDataAsync();

        Assert.Equal(200, rig.Store.State.Data.Count);
        Assert.Single(rig.Orch.FetchCalls);
        Assert.Equal(100, rig.Store.State.ViewportLength);
        Assert.Null(rig.Store.State.RestoredLoad);
    }

    [Fact]
    public async Task The_history_stops_where_the_provider_has_nothing_older()
    {
        var rig = new Rig();
        rig.Orch.FetchResults.Enqueue(Days(300, 499));
        rig.Orch.FetchResults.Enqueue(Days(250, 299));
        // the next older page comes back empty

        await rig.RestoreAndLoad(Tab(earliest: Day0));

        Assert.Equal(250, rig.Store.State.Data.Count);
        Assert.Equal(Day0.AddDays(250), rig.Store.State.Data[0].Date);
    }

    [Fact]
    public async Task Saving_reads_the_depth_back_and_an_unvisited_restored_tab_keeps_it()
    {
        var rig = new Rig();
        rig.Orch.FetchResults.Enqueue(Days(300, 499));
        rig.Orch.FetchResults.Enqueue(Days(100, 299));
        await rig.RestoreAndLoad(Tab(earliest: Day0.AddDays(100)));
        Assert.Equal(Day0.AddDays(100), rig.Library.CaptureWorkspace(rig.Store).Tabs[0].EarliestBarDate);

        // Restored but never loaded (a background tab nobody visited this session): saving
        // again must not shrink it to a fresh chart's 200 bars.
        var unvisited = new Rig();
        unvisited.Initializer.RestoreWorkspace(new WorkspaceConfiguration { Tabs = { Tab(earliest: Day0) } });
        Assert.Equal(Day0, unvisited.Library.CaptureWorkspace(unvisited.Store).Tabs[0].EarliestBarDate);
    }

    [Fact]
    public void The_target_reaches_back_to_the_oldest_drawing_anchor()
    {
        var tab = Tab(earliest: Day0.AddDays(100));
        tab.Series.Add(new SeriesConfig
        {
            IndicatorCode = "DRAWING",
            Drawing = new DrawingData { Type = DrawingType.TrendLine, AnchorDate1 = Day0.AddDays(40), AnchorDate2 = Day0.AddDays(150) }
        });
        tab.Series.Add(new SeriesConfig
        {
            IndicatorCode = "DRAWING",
            Drawing = new DrawingData { Type = DrawingType.None, AnchorDate1 = Day0 } // not a drawing
        });

        Assert.Equal(Day0.AddDays(40), WorkspaceInitializer.HistoryFromForTab(tab));
        Assert.Null(WorkspaceInitializer.HistoryFromForTab(Tab()));
    }

    /// <summary>
    /// A restored tab can now open close to the 5,000-bar cap, which makes a scrollback prepend
    /// at the cap reachable in one left arrow. It used to fetch 200 older bars and then shed
    /// the 200 NEWEST to stay under the cap: the live edge vanished, the count was unchanged,
    /// and the user heard "No more history available".
    /// </summary>
    [Fact]
    public async Task A_prepend_at_the_bar_cap_keeps_the_live_edge()
    {
        var orch = new KeyedFeedsTests.FakeOrchestrator();
        var feed = new ChartFeed(new ChartIdentity("Spot", "TestProv", "SPY", "1d"), orch, NullLogger.Instance);
        feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(Days(1000, 5999)));
        orch.FetchResults.Enqueue(Days(800, 999));

        int added = await feed.PrependOlderAsync();

        Assert.Equal(0, added);
        Assert.Equal(5000, feed.Bars.Count);
        Assert.Equal(Day0.AddDays(5999), feed.Bars[^1].Date);
        Assert.Empty(orch.FetchCalls); // nothing fetched that could not be kept
    }
}
