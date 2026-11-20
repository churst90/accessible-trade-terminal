using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Stage 5c: the change log's "Unusual moves" and "Signals". The rule (a closed bar against its
/// own chart's usual move), the recorder's entries and their keys, and the in-browser watcher.
/// The browser-closed half, through the real monitor poll, is in
/// <c>WebHost.HeadlessUnusualMoveTests</c>.
/// </summary>
public class UnusualMovesAndSignalsTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Btc = new("Spot", "MEXC", "BTC/USDT", "1h");

    /// <summary>Closes that rock 100 / 100.1 (a usual move of about 0.1%), then the closed bar
    /// at <paramref name="closedMove"/> from the close before it, then a forming bar.</summary>
    private static List<Ohlcv> Calm(int bars, double closedMove, double formingMove = 0)
    {
        var list = new List<Ohlcv>();
        for (int i = 0; i < bars - 2; i++)
        {
            double c = i % 2 == 0 ? 100 : 100.1;
            list.Add(new Ohlcv(T0.AddHours(i), c, c, c, c, 1));
        }
        double closed = list[^1].Close * (1 + closedMove);
        list.Add(new Ohlcv(T0.AddHours(bars - 2), closed, closed, closed, closed, 1));
        double forming = closed * (1 + formingMove);
        list.Add(new Ohlcv(T0.AddHours(bars - 1), forming, forming, forming, forming, 1));
        return list;
    }

    // ── The rule ──

    [Fact]
    public void ABarTenTimesItsUsualMoveIsUnusual()
    {
        var move = UnusualMoveRules.AtClose(Calm(UnusualMoveRules.HistoryBars, 0.01));

        Assert.NotNull(move);
        Assert.Equal(T0.AddHours(UnusualMoveRules.HistoryBars - 2), move!.BarUtc);
        Assert.Equal(1.0, move.Percent, 6);
        Assert.InRange(move.Times, 9.9, 10.1);
        Assert.Equal("BTC/USDT 1h up 1.0% in one bar, 9 times its usual move.",
            UnusualMoveRules.Sentence(Btc, move with { Times = 9.97 }));
    }

    [Theory]
    [InlineData(0.0049, false)]
    [InlineData(0.0051, true)]
    [InlineData(-0.0051, true)]
    public void FiveTimesIsTheLine(double closedMove, bool unusual)
        => Assert.Equal(unusual, UnusualMoveRules.AtClose(Calm(UnusualMoveRules.HistoryBars, closedMove)) != null);

    [Fact]
    public void DownIsSaidAsDownWithTwoDecimalsUnderOnePercent()
    {
        var move = UnusualMoveRules.AtClose(Calm(UnusualMoveRules.HistoryBars, -0.0062))!;
        Assert.Equal("BTC/USDT 1h down 0.62% in one bar, 6 times its usual move.",
            UnusualMoveRules.Sentence(Btc, move));
    }

    [Fact]
    public void ItNeedsItsWholeHistory()
    {
        Assert.Null(UnusualMoveRules.AtClose(Calm(UnusualMoveRules.HistoryBars - 1, 0.05)));
        Assert.NotNull(UnusualMoveRules.AtClose(Calm(UnusualMoveRules.HistoryBars, 0.05)));
        // And the monitor's first fetch for a narrated chart already brings that much.
        Assert.True(UnusualMoveRules.HistoryBars <= AccessibleTrader.Core.Services.Accessibility.HeadlessChart.MinBars);
    }

    [Fact]
    public void AFlatHistoryHasNoUsualMoveToCompareWith()
    {
        var bars = Calm(UnusualMoveRules.HistoryBars, 0.05)
            .Select((b, i) => i < UnusualMoveRules.HistoryBars - 2 ? b with { Close = 100 } : b).ToList();
        Assert.Null(UnusualMoveRules.AtClose(bars));
    }

    [Fact]
    public void TheFormingBarIsNotJudged()
        => Assert.Null(UnusualMoveRules.AtClose(Calm(UnusualMoveRules.HistoryBars, 0, formingMove: 0.05)));

    [Fact]
    public void OneEarlierSpikeDoesNotHideTheNext()
    {
        var bars = Calm(UnusualMoveRules.HistoryBars, 0.01);
        bars[20] = bars[20] with { Close = 130 };
        Assert.NotNull(UnusualMoveRules.AtClose(bars));
    }

    // ── The recorder ──

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private static ChangeLogStore NewStore()
        => new(new TempPaths(TestTemp.NewDir("att-changelog-5c-")), NullLogger<ChangeLogStore>.Instance);

    private static async Task<ChangeLogDocument> WaitForAsync(ChangeLogStore store, int count)
    {
        for (int i = 0; i < 400; i++)
        {
            var doc = await store.SnapshotAsync();
            if (doc.Entries.Count >= count) return doc;
            await Task.Delay(10);
        }
        return await store.SnapshotAsync();
    }

    private static Ohlcv At(int hour) => new(T0.AddHours(hour), 100, 100, 100, 100, 1);

    [Fact]
    public void ABackgroundTabsLadderIsASignalLedByItsChart()
    {
        var entry = ChangeLogRecorder.MapSignal(
            new BackgroundBarClosedEvent(Btc, At(5), At(6), "RSI crossed above 70."))!;

        Assert.Equal(ChangeLogCategory.Signal, entry.Category);
        Assert.Equal("BTC/USDT 1h: RSI crossed above 70.", entry.Text);
        Assert.Equal("signal|MEXC|BTC/USDT|1h|202611020500", entry.Key);
        Assert.Equal(T0.AddHours(5), entry.TimeUtc);
        Assert.Equal("BTC/USDT", entry.Symbol);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ACloseWithNoLadderIsNoSignal(string? narration)
        => Assert.Null(ChangeLogRecorder.MapSignal(new BackgroundBarClosedEvent(Btc, At(5), At(6), narration)));

    [Fact]
    public void BricksWithALadderAreASignalAtTheMinute()
    {
        var now = new DateTime(2026, 11, 2, 7, 31, 20, DateTimeKind.Utc);
        var entry = ChangeLogRecorder.MapSignal(
            new BackgroundBricksFormedEvent(Btc, "2 up bricks", "RSI 71."), now)!;
        Assert.Equal("signal|MEXC|BTC/USDT|1h|202611020731", entry.Key);
        Assert.Null(ChangeLogRecorder.MapSignal(new BackgroundBricksFormedEvent(Btc, "2 up bricks"), now));
    }

    [Fact]
    public async Task TheSameChartAndBarFromTheBrowserAndTheMonitorIsOneSignal()
    {
        var store = NewStore();
        var bus = new SpyEventBus();
        using var recorder = new ChangeLogRecorder(bus, store);
        // Recent: the store drops what is older than it keeps.
        var hour = DateTime.UtcNow.Date.AddHours(-5);
        Ohlcv Bar(DateTime t) => new(t, 100, 100, 100, 100, 1);

        bus.Publish(new BackgroundBarClosedEvent(Btc, Bar(hour), Bar(hour.AddHours(1)), "RSI crossed above 70."));
        bus.Publish(new ChartSignalEvent(Btc, hour, ChangeLogRecorder.Led(Btc, "RSI crossed above 70.")));
        bus.Publish(new BackgroundBarClosedEvent(Btc, Bar(hour.AddHours(-1)), Bar(hour)));   // no ladder: nothing

        await Task.Delay(150);
        var doc = await WaitForAsync(store, 1);
        Assert.Equal(ChangeLogCategory.Signal, Assert.Single(doc.Entries).Category);
    }

    [Fact]
    public async Task AnUnusualMoveIsRecordedUnderUnusualMoves()
    {
        var store = NewStore();
        var bus = new SpyEventBus();
        using var recorder = new ChangeLogRecorder(bus, store);

        var hour = DateTime.UtcNow.Date.AddHours(-5);
        bus.Publish(new UnusualMoveEvent(Btc, hour, "BTC/USDT 1h up 4.2% in one bar, 6 times its usual move."));
        bus.Publish(new UnusualMoveEvent(Btc, hour, "BTC/USDT 1h up 4.2% in one bar, 6 times its usual move."));

        await Task.Delay(150);
        var entry = Assert.Single((await WaitForAsync(store, 1)).Entries);
        Assert.Equal(ChangeLogCategory.Unusual, entry.Category);
        Assert.Equal($"unusual|MEXC|BTC/USDT|1h|{hour:yyyyMMddHHmm}", entry.Key);
        Assert.Equal("unusual|MEXC|BTC/USDT|1h|202611020000", ChangeLogRecorder.MapUnusual(new UnusualMoveEvent(Btc, T0, "x")).Key);
        Assert.StartsWith("BTC/USDT 1h up 4.2%", entry.Text);
    }

    // ── The in-browser watcher ──

    private sealed class Rig : IDisposable
    {
        public readonly MarketFeedHub Hub = new(Substitute.For<IDataOrchestrator>(),
            Substitute.For<IDataService>(), new DemoPolicy(isDemo: false), NullLoggerFactory.Instance);
        public readonly IBackgroundTabFeedService Tabs = Substitute.For<IBackgroundTabFeedService>();
        public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
        public readonly EventBus Bus = new();
        public readonly List<UnusualMoveEvent> Seen = new();
        public readonly List<ChartIdentity> LiveTabs = new();
        public WorkspaceState State = WorkspaceState.Initial;
        private readonly UnusualMoveWatcher _watcher;

        public Rig()
        {
            Store.State.Returns(_ => State);
            Tabs.LiveBackgroundFeeds.Returns(_ => LiveTabs.ToList());
            _watcher = new UnusualMoveWatcher(Hub, Tabs, Store, Bus);
            Bus.Subscribe<UnusualMoveEvent>(e => { lock (Seen) Seen.Add(e); });
        }

        /// <summary>A feed whose newest held bar is the one about to close, then the tick that
        /// opens the next bar and closes it.</summary>
        public void CloseABar(ChartIdentity id, double closedMove)
        {
            var bars = Calm(UnusualMoveRules.HistoryBars, closedMove);
            var feed = Hub.GetOrCreateFeed(id);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(bars.Take(bars.Count - 1).ToList()));
            Assert.True(feed.ApplyLiveTick(bars[^1]));
        }

        public int Count { get { lock (Seen) return Seen.Count; } }

        public void Dispose() => _watcher.Dispose();
    }

    [Fact]
    public void TheFocusedChartsUnusualCloseIsPublished()
    {
        using var rig = new Rig();
        rig.Hub.SetFocus(Btc);

        rig.CloseABar(Btc, 0.03);

        var seen = Assert.Single(rig.Seen);
        Assert.Equal(Btc, seen.Identity);
        Assert.Equal(T0.AddHours(UnusualMoveRules.HistoryBars - 2), seen.BarUtc);
        Assert.StartsWith("BTC/USDT 1h up 3.0% in one bar", seen.Text);
    }

    [Fact]
    public void AnOrdinaryCloseIsNot()
    {
        using var rig = new Rig();
        rig.Hub.SetFocus(Btc);
        rig.CloseABar(Btc, 0.001);
        Assert.Equal(0, rig.Count);
    }

    [Fact]
    public void ABackgroundTabCountsAndALeasedFeedDoesNot()
    {
        using var rig = new Rig();
        var eth = Btc with { Symbol = "ETH/USDT" };
        var leased = Btc with { Symbol = "XMR/USDT" };
        rig.Hub.SetFocus(Btc);
        rig.LiveTabs.Add(eth);

        rig.CloseABar(eth, 0.03);
        rig.CloseABar(leased, 0.03);

        Assert.Equal(eth, Assert.Single(rig.Seen).Identity);
    }

    [Fact]
    public void ReplayIsHistoryNotAMove()
    {
        using var rig = new Rig();
        rig.Hub.SetFocus(Btc);
        rig.State = WorkspaceState.Initial with { IsPlaying = true };
        rig.CloseABar(Btc, 0.03);
        Assert.Equal(0, rig.Count);
    }
}
