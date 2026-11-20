using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Stage 5c with the browser closed: the monitor publishes a watched chart's unusual close and
/// its spoken ladder on the headless session's bus, where the change-log recorder files them
/// for Alt+U. Driven through the real poll; the rule itself is in
/// <c>UnusualMovesAndSignalsTests</c>.
/// </summary>
[Collection("CircuitCoverage")]
public sealed class HeadlessUnusualMoveTests : IDisposable
{
    public void Dispose() => CircuitAlertCoverage.ResetForTests();

    private const int Closing = HeadlessMonitorHarness.StartHour;

    /// <summary>Rocks 100 / 100.1, and the hour that closes on the second poll moves
    /// <paramref name="move"/> from the one before it.</summary>
    private static Func<int, double> Prices(double move)
        => h => h == Closing || h == Closing + 1 ? 100 * (1 + move) : (h % 2 == 0 ? 100 : 100.1);

    private static (List<UnusualMoveEvent> Moves, List<ChartSignalEvent> Signals) Listen(HeadlessMonitorHarness h)
    {
        var moves = new List<UnusualMoveEvent>();
        var signals = new List<ChartSignalEvent>();
        var bus = h.Session.Get<IEventBus>();
        bus.Subscribe<UnusualMoveEvent>(e => { lock (moves) moves.Add(e); });
        bus.Subscribe<ChartSignalEvent>(e => { lock (signals) signals.Add(e); });
        return (moves, signals);
    }

    [Fact]
    public async Task AnUnusualCloseOnASavedTabIsPublishedOnceAndTheFetchStaysSmall()
    {
        using var h = new HeadlessMonitorHarness(Array.Empty<SeriesConfig>(), priceAt: Prices(0.03));
        h.NewBarToasts(true);
        var (moves, _) = Listen(h);

        await h.PollAsync();          // seeds the history
        Assert.Empty(moves);
        h.CloseABar();
        await h.PollAsync();          // hour 99 closed 3% up
        await h.PollAsync();          // the same bar again: nothing new closed
        h.CloseABar();
        await h.PollAsync();          // hour 100 closed flat

        var move = Assert.Single(moves);
        Assert.Equal(HeadlessMonitorHarness.T0.AddHours(Closing), move.BarUtc);
        Assert.Equal("BTC/USD", move.Identity.Symbol);
        Assert.StartsWith("BTC/USD 1h up 3.0% in one bar", move.Text);
        // The history is asked for once; the polls after it are the small fetch they always were.
        Assert.Equal(new[] { UnusualMoveRules.HistoryBars, LocalBackgroundMonitor.MinFetch, LocalBackgroundMonitor.MinFetch,
                LocalBackgroundMonitor.MinFetch },
            h.RequestedLimits);
    }

    [Fact]
    public async Task AnOrdinaryCloseIsNotPublished()
    {
        using var h = new HeadlessMonitorHarness(Array.Empty<SeriesConfig>(), priceAt: Prices(0.0002));
        h.NewBarToasts(true);
        var (moves, _) = Listen(h);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Empty(moves);
    }

    [Fact]
    public void AMissedStretchStartsTheHistoryAgain()
    {
        Ohlcv At(int hour) => new(HeadlessMonitorHarness.T0.AddHours(hour), 1, 1, 1, 1, 1);
        var kept = Enumerable.Range(0, 10).Select(At).ToList();

        // Overlapping: the forming bar is replaced, the new one added.
        var next = LocalBackgroundMonitor.MergeMoveHistory(kept, new[] { At(8), At(9) with { Close = 2 }, At(10) });
        Assert.Equal(11, next.Count);
        Assert.Equal(2, next[9].Close);

        // A gap: what was kept is not one bar before what came.
        Assert.Equal(3, LocalBackgroundMonitor.MergeMoveHistory(kept, new[] { At(20), At(21), At(22) }).Count);

        // Capped.
        var many = Enumerable.Range(0, UnusualMoveRules.HistoryBars + 5).Select(At).ToList();
        Assert.Equal(UnusualMoveRules.HistoryBars, LocalBackgroundMonitor.MergeMoveHistory(null, many).Count);
    }

    [Fact]
    public async Task TheLadderTheMonitorSpeaksIsPublishedAsASignal()
    {
        var volume = new SeriesConfig
        {
            Id = CoreSeriesIds.Volume, Name = "Volume", FriendlyName = "Volume", IndicatorCode = "VOLUME",
            Pane = "Volume", IsAutoNarrated = true, IsVisible = true,
        };
        volume.Components.Add(new ComponentConfig
        {
            Name = "Volume", DisplayName = "Volume", DisplayType = ComponentDisplayType.Bar,
            Role = ComponentRole.Volume, DataMapping = "volume", IsVisible = true,
        });
        using var h = new HeadlessMonitorHarness(new[] { volume });
        h.NewBarToasts(true);
        var (_, signals) = Listen(h);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        var signal = Assert.Single(signals);
        Assert.Equal(HeadlessMonitorHarness.T0.AddHours(Closing), signal.BarUtc);
        Assert.StartsWith("BTC/USD 1h: ", signal.Text);
        Assert.Contains("Volume 100,000", signal.Text);
    }
}
